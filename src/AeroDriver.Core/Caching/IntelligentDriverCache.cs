// 研究ベースの改善: インテリジェントキャッシング戦略
// 根拠: WMIクエリは高コスト - 適切なキャッシングで応答時間を大幅改善
// 優先度: P1 (高) - パフォーマンス改善
// 出典: WMI Performance Best Practices, Enterprise Driver Management

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace AeroDriver.Core.Caching;

/// <summary>
/// 多層キャッシングシステム
/// 研究に基づく3層キャッシング戦略でWMIクエリのオーバーヘッドを削減
/// </summary>
public class IntelligentDriverCache : IDisposable
{
    private readonly ILogger _logger;
    private readonly IMemoryCache _memoryCache;  // L1: メモリキャッシュ（最速）
    private readonly DiskCache _diskCache;        // L2: ディスクキャッシュ（永続的）
    private readonly CacheMetrics _metrics;
    private bool _disposed;

    // キャッシュTTL設定
    private static readonly TimeSpan ShortLivedTtl = TimeSpan.FromMinutes(5);   // 問題のあるドライバー
    private static readonly TimeSpan LongLivedTtl = TimeSpan.FromHours(1);      // 正常なドライバー
    private static readonly TimeSpan PermanentTtl = TimeSpan.FromDays(7);       // 静的情報

    public IntelligentDriverCache(ILogger logger, IMemoryCache memoryCache)
    {
        _logger = logger;
        _memoryCache = memoryCache;
        _diskCache = new DiskCache(logger);
        _metrics = new CacheMetrics();
    }

    /// <summary>
    /// キャッシュからドライバー情報を取得、なければファクトリー関数で生成
    /// </summary>
    /// <remarks>
    /// キャッシング戦略:
    /// 1. L1 (Memory): 最速だがプロセス再起動で消失
    /// 2. L2 (Disk): 永続的だが読み込みに時間がかかる
    /// 3. L3 (Source): WMI/レジストリから直接取得（最も遅い）
    ///
    /// 期待効果: キャッシュヒット時は95%以上の応答時間削減
    /// </remarks>
    public async Task<DriverInfo?> GetOrFetchDriverAsync(
        string deviceId,
        Func<Task<DriverInfo?>> factory,
        CancellationToken ct = default)
    {
        var cacheKey = $"driver:{deviceId}";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // L1: メモリキャッシュチェック（最速）
            if (_memoryCache.TryGetValue<DriverInfo>(cacheKey, out var cachedDriver))
            {
                _metrics.RecordHit(CacheLevel.Memory, stopwatch.Elapsed);
                _logger.LogDebug($"L1 cache hit for {deviceId} ({stopwatch.ElapsedMilliseconds}ms)");
                return cachedDriver;
            }

            // L2: ディスクキャッシュチェック
            var diskCachedDriver = await _diskCache.GetAsync<DriverInfo>(cacheKey, ct);
            if (diskCachedDriver != null)
            {
                // L1に昇格
                var ttl = DetermineTtl(diskCachedDriver);
                _memoryCache.Set(cacheKey, diskCachedDriver, ttl);

                _metrics.RecordHit(CacheLevel.Disk, stopwatch.Elapsed);
                _logger.LogDebug($"L2 cache hit for {deviceId} ({stopwatch.ElapsedMilliseconds}ms)");
                return diskCachedDriver;
            }

            // L3: ソースから取得（WMI/レジストリ）
            var driver = await factory();

            if (driver != null)
            {
                // 両方のキャッシュに保存
                var ttl = DetermineTtl(driver);

                _memoryCache.Set(cacheKey, driver, ttl);
                await _diskCache.SetAsync(cacheKey, driver, ttl, ct);

                _metrics.RecordMiss(stopwatch.Elapsed);
                _logger.LogDebug($"L3 source fetch for {deviceId} ({stopwatch.ElapsedMilliseconds}ms)");
            }

            return driver;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Cache operation failed for {deviceId}: {ex.Message}");
            _metrics.RecordError();
            throw;
        }
    }

    /// <summary>
    /// 複数ドライバーのバッチキャッシュ取得
    /// </summary>
    public async Task<Dictionary<string, DriverInfo>> GetOrFetchBatchAsync(
        IEnumerable<string> deviceIds,
        Func<IEnumerable<string>, Task<Dictionary<string, DriverInfo>>> factory,
        CancellationToken ct = default)
    {
        var deviceIdList = deviceIds.ToList();
        var result = new Dictionary<string, DriverInfo>();
        var missingIds = new List<string>();

        // キャッシュから取得を試みる
        foreach (var deviceId in deviceIdList)
        {
            var cachedDriver = await GetFromCacheAsync(deviceId, ct);
            if (cachedDriver != null)
            {
                result[deviceId] = cachedDriver;
            }
            else
            {
                missingIds.Add(deviceId);
            }
        }

        // キャッシュミスしたものをバッチで取得
        if (missingIds.Any())
        {
            var fetchedDrivers = await factory(missingIds);

            foreach (var kvp in fetchedDrivers)
            {
                result[kvp.Key] = kvp.Value;

                // キャッシュに保存
                var cacheKey = $"driver:{kvp.Key}";
                var ttl = DetermineTtl(kvp.Value);

                _memoryCache.Set(cacheKey, kvp.Value, ttl);
                await _diskCache.SetAsync(cacheKey, kvp.Value, ttl, ct);
            }
        }

        _logger.LogInformation($"Batch fetch: {result.Count} total, {deviceIdList.Count - missingIds.Count} from cache, {missingIds.Count} from source");

        return result;
    }

    /// <summary>
    /// キャッシュから取得のみ（ファクトリーを呼ばない）
    /// </summary>
    private async Task<DriverInfo?> GetFromCacheAsync(string deviceId, CancellationToken ct)
    {
        var cacheKey = $"driver:{deviceId}";

        // L1チェック
        if (_memoryCache.TryGetValue<DriverInfo>(cacheKey, out var memoryDriver))
        {
            return memoryDriver;
        }

        // L2チェック
        var diskDriver = await _diskCache.GetAsync<DriverInfo>(cacheKey, ct);
        if (diskDriver != null)
        {
            // L1に昇格
            var ttl = DetermineTtl(diskDriver);
            _memoryCache.Set(cacheKey, diskDriver, ttl);
        }

        return diskDriver;
    }

    /// <summary>
    /// 動的TTL決定
    /// ドライバーの状態に基づいて適切なキャッシュ期間を決定
    /// </summary>
    private TimeSpan DetermineTtl(DriverInfo driver)
    {
        // 問題のあるドライバーは短いTTL（頻繁に再チェック）
        if (!string.Equals(driver.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            return ShortLivedTtl;
        }

        // 重要なデバイスは中程度のTTL
        if (driver.IsEssential)
        {
            return TimeSpan.FromMinutes(30);
        }

        // 通常のドライバーは長いTTL
        return LongLivedTtl;
    }

    /// <summary>
    /// キャッシュクリア
    /// </summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Clearing all caches");

        // メモリキャッシュクリア（IMemoryCacheには全クリア機能がない）
        if (_memoryCache is MemoryCache memCache)
        {
            memCache.Compact(1.0); // 100%圧縮 = 全削除
        }

        // ディスクキャッシュクリア
        await _diskCache.ClearAsync(ct);

        _metrics.Reset();
        _logger.LogInformation("All caches cleared");
    }

    /// <summary>
    /// キャッシュ統計取得
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return _metrics.GetStatistics();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _diskCache.Dispose();
    }
}

/// <summary>
/// ディスクキャッシュ
/// JSON形式でドライバー情報を永続化
/// </summary>
internal class DiskCache : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public DiskCache(ILogger logger)
    {
        _logger = logger;
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AeroDriver", "Cache");

        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class
    {
        var filePath = GetCacheFilePath(key);

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var cacheEntry = JsonSerializer.Deserialize<CacheEntry<T>>(json);

            if (cacheEntry == null)
            {
                return null;
            }

            // 有効期限チェック
            if (cacheEntry.ExpiresAt < DateTime.UtcNow)
            {
                // 期限切れの場合は削除
                File.Delete(filePath);
                return null;
            }

            return cacheEntry.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to read disk cache for {key}: {ex.Message}");
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) where T : class
    {
        var filePath = GetCacheFilePath(key);

        await _writeLock.WaitAsync(ct);
        try
        {
            var cacheEntry = new CacheEntry<T>
            {
                Value = value,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(ttl)
            };

            var json = JsonSerializer.Serialize(cacheEntry, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            await File.WriteAllTextAsync(filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to write disk cache for {key}: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.json");

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to clear disk cache: {ex.Message}");
        }
    }

    private string GetCacheFilePath(string key)
    {
        // キーをファイル名に安全な形式に変換
        var safeKey = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(key))
            .Replace('/', '_')
            .Replace('+', '-');

        return Path.Combine(_cacheDirectory, $"{safeKey}.json");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _writeLock.Dispose();
    }
}

/// <summary>
/// キャッシュエントリ
/// </summary>
internal class CacheEntry<T>
{
    public T? Value { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// キャッシュメトリクス
/// パフォーマンス監視とキャッシュ効率の測定
/// </summary>
internal class CacheMetrics
{
    private long _memoryHits;
    private long _diskHits;
    private long _misses;
    private long _errors;
    private readonly ConcurrentBag<TimeSpan> _memoryHitTimes = new();
    private readonly ConcurrentBag<TimeSpan> _diskHitTimes = new();
    private readonly ConcurrentBag<TimeSpan> _missTimes = new();

    public void RecordHit(CacheLevel level, TimeSpan elapsed)
    {
        switch (level)
        {
            case CacheLevel.Memory:
                Interlocked.Increment(ref _memoryHits);
                _memoryHitTimes.Add(elapsed);
                break;
            case CacheLevel.Disk:
                Interlocked.Increment(ref _diskHits);
                _diskHitTimes.Add(elapsed);
                break;
        }
    }

    public void RecordMiss(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _misses);
        _missTimes.Add(elapsed);
    }

    public void RecordError()
    {
        Interlocked.Increment(ref _errors);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _memoryHits, 0);
        Interlocked.Exchange(ref _diskHits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Interlocked.Exchange(ref _errors, 0);

        _memoryHitTimes.Clear();
        _diskHitTimes.Clear();
        _missTimes.Clear();
    }

    public CacheStatistics GetStatistics()
    {
        var totalRequests = _memoryHits + _diskHits + _misses;

        return new CacheStatistics
        {
            TotalRequests = totalRequests,
            MemoryHits = _memoryHits,
            DiskHits = _diskHits,
            Misses = _misses,
            Errors = _errors,
            HitRate = totalRequests > 0 ? (double)(_memoryHits + _diskHits) / totalRequests : 0,
            MemoryHitRate = totalRequests > 0 ? (double)_memoryHits / totalRequests : 0,
            AvgMemoryHitTime = _memoryHitTimes.Any() ? TimeSpan.FromTicks((long)_memoryHitTimes.Average(t => t.Ticks)) : TimeSpan.Zero,
            AvgDiskHitTime = _diskHitTimes.Any() ? TimeSpan.FromTicks((long)_diskHitTimes.Average(t => t.Ticks)) : TimeSpan.Zero,
            AvgMissTime = _missTimes.Any() ? TimeSpan.FromTicks((long)_missTimes.Average(t => t.Ticks)) : TimeSpan.Zero
        };
    }
}

public enum CacheLevel
{
    Memory,
    Disk
}

/// <summary>
/// キャッシュ統計
/// </summary>
public class CacheStatistics
{
    public long TotalRequests { get; set; }
    public long MemoryHits { get; set; }
    public long DiskHits { get; set; }
    public long Misses { get; set; }
    public long Errors { get; set; }

    public double HitRate { get; set; }
    public double MemoryHitRate { get; set; }

    public TimeSpan AvgMemoryHitTime { get; set; }
    public TimeSpan AvgDiskHitTime { get; set; }
    public TimeSpan AvgMissTime { get; set; }

    public override string ToString()
    {
        return $"Cache Stats: {TotalRequests} requests, {HitRate:P1} hit rate " +
               $"(Memory: {MemoryHitRate:P1}, {AvgMemoryHitTime.TotalMilliseconds:F1}ms | " +
               $"Disk: {AvgDiskHitTime.TotalMilliseconds:F1}ms | " +
               $"Miss: {AvgMissTime.TotalMilliseconds:F1}ms)";
    }
}
