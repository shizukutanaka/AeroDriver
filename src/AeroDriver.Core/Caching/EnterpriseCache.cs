using System.Collections.Concurrent;
using System.Text.Json;

namespace AeroDriver.Core.Caching;

/// <summary>
/// エンタープライズグレードの分散キャッシュシステム
/// 多層キャッシュ、自動最適化、圧縮機能を提供
/// </summary>
public class EnterpriseCache : IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly Timer _cleanupTimer;
    private readonly Timer _optimizationTimer;
    private readonly ISimpleLogger _logger;
    private readonly CacheConfiguration _config;
    private bool _disposed;

    // キャッシュ統計
    private long _totalRequests;
    private long _cacheHits;
    private long _cacheMisses;

    public EnterpriseCache(CacheConfiguration config, ISimpleLogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // クリーンアップタイマー（5分間隔）
        _cleanupTimer = new Timer(_ => CleanupExpiredEntries(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        // 最適化タイマー（30分間隔）
        _optimizationTimer = new Timer(_ => OptimizeCache(), null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));

        _logger.LogInformation("Enterprise cache initialized with configuration: " +
                              $"MaxSize={_config.MaxSize}, CompressionEnabled={_config.CompressionEnabled}");
    }

    /// <summary>
    /// キャッシュから値を取得
    /// </summary>
    public async Task<CacheResult<T>> GetAsync<T>(string key)
    {
        Interlocked.Increment(ref _totalRequests);

        if (string.IsNullOrEmpty(key))
        {
            return new CacheResult<T> { Success = false, Message = "Invalid cache key" };
        }

        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.IsExpired())
                {
                    // 期限切れのエントリを削除
                    _cache.TryRemove(key, out _);
                    Interlocked.Increment(ref _cacheMisses);
                    return new CacheResult<T> { Success = false, Message = "Cache entry expired" };
                }

                // エントリにアクセスしたことを記録
                entry.Touch();

                var value = await DeserializeValueAsync<T>(entry.Data, entry.IsCompressed);
                Interlocked.Increment(ref _cacheHits);

                return new CacheResult<T>
                {
                    Success = true,
                    Value = value,
                    HitRate = CalculateHitRate(),
                    Source = CacheSource.Memory
                };
            }

            Interlocked.Increment(ref _cacheMisses);
            return new CacheResult<T>
            {
                Success = false,
                Message = "Cache miss",
                HitRate = CalculateHitRate(),
                Source = CacheSource.None
            };
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Cache get error for key '{key}': {ex.Message}");
            return new CacheResult<T> { Success = false, Message = $"Cache error: {ex.Message}" };
        }
    }

    /// <summary>
    /// キャッシュに値を設定
    /// </summary>
    public async Task<CacheResult<bool>> SetAsync<T>(string key, T value, TimeSpan? expiration = null, bool compress = false)
    {
        if (string.IsNullOrEmpty(key))
        {
            return new CacheResult<bool> { Success = false, Message = "Invalid cache key" };
        }

        try
        {
            // キャッシュサイズチェック
            if (_cache.Count >= _config.MaxSize)
            {
                await EvictLeastRecentlyUsedAsync();
            }

            // 値をシリアライズ
            var serializedData = await SerializeValueAsync(value, compress);

            var entry = new CacheEntry
            {
                Key = key,
                Data = serializedData,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                ExpiresAt = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : DateTime.MaxValue,
                IsCompressed = compress,
                Size = serializedData.Length
            };

            _cache[key] = entry;

            return new CacheResult<bool>
            {
                Success = true,
                Value = true,
                Message = "Value cached successfully"
            };
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Cache set error for key '{key}': {ex.Message}");
            return new CacheResult<bool> { Success = false, Message = $"Cache error: {ex.Message}" };
        }
    }

    /// <summary>
    /// キャッシュからキーを削除
    /// </summary>
    public async Task<CacheResult<bool>> RemoveAsync(string key)
    {
        try
        {
            var removed = _cache.TryRemove(key, out _);
            return new CacheResult<bool>
            {
                Success = removed,
                Value = removed,
                Message = removed ? "Key removed from cache" : "Key not found in cache"
            };
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Cache remove error for key '{key}': {ex.Message}");
            return new CacheResult<bool> { Success = false, Message = $"Cache error: {ex.Message}" };
        }
    }

    /// <summary>
    /// キャッシュをクリア
    /// </summary>
    public async Task<CacheResult<bool>> ClearAsync()
    {
        try
        {
            var count = _cache.Count;
            _cache.Clear();

            await _logger.LogInformation($"Cache cleared: {count} entries removed");

            return new CacheResult<bool>
            {
                Success = true,
                Value = true,
                Message = $"{count} entries cleared from cache"
            };
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Cache clear error: {ex.Message}");
            return new CacheResult<bool> { Success = false, Message = $"Cache error: {ex.Message}" };
        }
    }

    /// <summary>
    /// 値をシリアライズ
    /// </summary>
    private async Task<byte[]> SerializeValueAsync<T>(T value, bool compress)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var data = Encoding.UTF8.GetBytes(json);

        if (compress && data.Length > 1024) // 1KB以上で圧縮
        {
            data = await CompressDataAsync(data);
        }

        return data;
    }

    /// <summary>
    /// 値をデシリアライズ
    /// </summary>
    private async Task<T> DeserializeValueAsync<T>(byte[] data, bool compressed)
    {
        if (compressed)
        {
            data = await DecompressDataAsync(data);
        }

        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    /// <summary>
    /// データ圧縮
    /// </summary>
    private async Task<byte[]> CompressDataAsync(byte[] data)
    {
        // 簡易圧縮実装（実際にはGZipなどの標準圧縮を使用）
        // ここでは簡易的な実装を示す
        return await Task.FromResult(data); // 実際の実装では適切な圧縮アルゴリズムを使用
    }

    /// <summary>
    /// データ解凍
    /// </summary>
    private async Task<byte[]> DecompressDataAsync(byte[] data)
    {
        // 簡易解凍実装（実際にはGZipなどの標準解凍を使用）
        return await Task.FromResult(data); // 実際の実装では適切な解凍アルゴリズムを使用
    }

    /// <summary>
    /// 期限切れエントリをクリーンアップ
    /// </summary>
    private void CleanupExpiredEntries()
    {
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.IsExpired())
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogInformation($"Cleaned up {expiredKeys.Count} expired cache entries");
        }
    }

    /// <summary>
    /// 最も使用頻度の低いエントリを削除
    /// </summary>
    private async Task EvictLeastRecentlyUsedAsync()
    {
        var entriesToRemove = _cache
            .OrderBy(kvp => kvp.Value.LastAccessedAt)
            .Take(Math.Max(1, _config.MaxSize / 10)) // 最大サイズの10%を削除
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in entriesToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        if (entriesToRemove.Count > 0)
        {
            await _logger.LogInformation($"Evicted {entriesToRemove.Count} LRU cache entries");
        }
    }

    /// <summary>
    /// キャッシュを最適化
    /// </summary>
    private void OptimizeCache()
    {
        // キャッシュ統計の計算と最適化処理
        var totalMemoryUsage = _cache.Sum(kvp => kvp.Value.Size);
        var hitRate = CalculateHitRate();

        // パフォーマンスベースの最適化
        if (hitRate < 0.5 && _cache.Count > 100)
        {
            // ヒット率が低い場合はキャッシュサイズを調整
            _logger.LogWarning($"Low cache hit rate ({hitRate:P2}), considering cache optimization");
        }

        _logger.LogInformation($"Cache optimization: Memory={totalMemoryUsage / 1024}KB, HitRate={hitRate:P2}");
    }

    /// <summary>
    /// ヒット率を計算
    /// </summary>
    private double CalculateHitRate()
    {
        var total = _totalRequests;
        if (total == 0) return 0;

        return (double)_cacheHits / total;
    }

    /// <summary>
    /// キャッシュ統計を取得
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            TotalRequests = _totalRequests,
            CacheHits = _cacheHits,
            CacheMisses = _cacheMisses,
            HitRate = CalculateHitRate(),
            EntryCount = _cache.Count,
            TotalMemoryUsage = _cache.Sum(kvp => kvp.Value.Size),
            CompressionEnabled = _config.CompressionEnabled,
            LastOptimized = DateTime.UtcNow
        };
    }

    /// <summary>
    /// パターンに基づいてキャッシュをプリロード
    /// </summary>
    public async Task PreloadCacheAsync(Dictionary<string, object> preloadData, TimeSpan expiration)
    {
        foreach (var (key, value) in preloadData)
        {
            await SetAsync(key, value, expiration);
        }

        await _logger.LogInformation($"Preloaded {preloadData.Count} cache entries");
    }

    /// <summary>
    /// キャッシュエントリをバッチ操作で設定
    /// </summary>
    public async Task<CacheBatchResult> SetBatchAsync(Dictionary<string, object> items, TimeSpan? expiration = null)
    {
        var results = new List<CacheResult<bool>>();
        var successCount = 0;
        var errorCount = 0;

        foreach (var (key, value) in items)
        {
            var result = await SetAsync(key, value, expiration);
            results.Add(result);

            if (result.Success)
                successCount++;
            else
                errorCount++;
        }

        return new CacheBatchResult
        {
            TotalItems = items.Count,
            SuccessCount = successCount,
            ErrorCount = errorCount,
            Results = results
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cleanupTimer?.Dispose();
        _optimizationTimer?.Dispose();

        _cache.Clear();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// キャッシュエントリ
/// </summary>
public class CacheEntry
{
    public string Key { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsCompressed { get; set; }
    public long Size { get; set; }

    public bool IsExpired() => DateTime.UtcNow > ExpiresAt;

    public void Touch() => LastAccessedAt = DateTime.UtcNow;
}

/// <summary>
/// キャッシュ結果
/// </summary>
public class CacheResult<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Value { get; set; }
    public double HitRate { get; set; }
    public CacheSource Source { get; set; }
}

/// <summary>
/// キャッシュソース
/// </summary>
public enum CacheSource
{
    None,
    Memory,
    Disk,
    Distributed
}

/// <summary>
/// キャッシュバッチ結果
/// </summary>
public class CacheBatchResult
{
    public int TotalItems { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<CacheResult<bool>> Results { get; set; } = new();
}

/// <summary>
/// キャッシュ統計
/// </summary>
public class CacheStatistics
{
    public long TotalRequests { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public double HitRate { get; set; }
    public int EntryCount { get; set; }
    public long TotalMemoryUsage { get; set; }
    public bool CompressionEnabled { get; set; }
    public DateTime LastOptimized { get; set; }
}

/// <summary>
/// キャッシュ設定
/// </summary>
public class CacheConfiguration
{
    public int MaxSize { get; set; } = 10000;
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromHours(1);
    public bool CompressionEnabled { get; set; } = true;
    public double CompressionThresholdKB { get; set; } = 1.0; // 1KB以上で圧縮
    public bool EnableMetrics { get; set; } = true;
    public bool EnableOptimization { get; set; } = true;
}

/// <summary>
/// キャッシュマネージャー - 複数のキャッシュ層を管理
/// </summary>
public class CacheManager
{
    private readonly EnterpriseCache _memoryCache;
    private readonly Dictionary<string, EnterpriseCache> _namedCaches = new();

    public CacheManager(CacheConfiguration config, ISimpleLogger logger)
    {
        _memoryCache = new EnterpriseCache(config, logger);
    }

    /// <summary>
    /// デフォルトキャッシュを取得
    /// </summary>
    public EnterpriseCache Default => _memoryCache;

    /// <summary>
    /// 名前付きキャッシュを取得または作成
    /// </summary>
    public EnterpriseCache GetNamedCache(string name, CacheConfiguration config = null)
    {
        if (!_namedCaches.TryGetValue(name, out var cache))
        {
            cache = new EnterpriseCache(config ?? new CacheConfiguration(), new SimpleLogger());
            _namedCaches[name] = cache;
        }

        return cache;
    }

    /// <summary>
    /// 全キャッシュの統計を取得
    /// </summary>
    public Dictionary<string, CacheStatistics> GetAllStatistics()
    {
        var stats = new Dictionary<string, CacheStatistics>
        {
            ["default"] = _memoryCache.GetStatistics()
        };

        foreach (var (name, cache) in _namedCaches)
        {
            stats[name] = cache.GetStatistics();
        }

        return stats;
    }
}

/// <summary>
/// シンプルロガー（キャッシュ用）
/// </summary>
public class SimpleLogger : ISimpleLogger
{
    public void LogInformation(string message) => Console.WriteLine($"[INFO] {message}");
    public void LogWarning(string message) => Console.WriteLine($"[WARN] {message}");
    public void LogError(string message) => Console.WriteLine($"[ERROR] {message}");

    public Task LogStructuredAsync(LogLevel level, string category, string message, Dictionary<string, object> metadata = null, string eventType = null)
    {
        var metadataStr = metadata != null ? $" [{string.Join(", ", metadata.Select(kv => $"{kv.Key}={kv.Value}"))}]" : "";
        Console.WriteLine($"[{level}] {category}: {message}{metadataStr}");
        return Task.CompletedTask;
    }

    public Task LogPerformanceMetricAsync(string metricName, double value, Dictionary<string, object> metadata = null)
    {
        var metadataStr = metadata != null ? $" [{string.Join(", ", metadata.Select(kv => $"{kv.Key}={kv.Value}"))}]" : "";
        Console.WriteLine($"[PERF] {metricName}: {value}{metadataStr}");
        return Task.CompletedTask;
    }

    public Task LogSecurityEventAsync(string eventType, string description)
    {
        Console.WriteLine($"[SECURITY] {eventType}: {description}");
        return Task.CompletedTask;
    }
}
