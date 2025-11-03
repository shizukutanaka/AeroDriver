using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Validation;

namespace AeroDriver.Core.Services;

/// <summary>
/// コア機能を統合し、CLI や API、Windows サービスから安全に利用できるファサードを提供します。
/// パフォーマンス最適化と保守性向上のため、同期メソッドを廃止し、非同期処理を標準としています。
/// </summary>
public class CoreDriverService
{
    private readonly ISimpleLogger _logger;
    private readonly DriverManager _driverManager;
    private readonly IDriverRepository _driverRepository;
    private readonly ISecurityService _securityService;

    // 軽量キャッシュ機能 - 頻繁にアクセスされるデータをキャッシュ
    private readonly Dictionary<string, object> _cache = new();
    private readonly object _cacheLock = new();
    private const int MaxCacheSize = 50; // キャッシュサイズ制限
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private static readonly int MaxRetryAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

    /// <summary>
    /// エラーハンドリングとログ記録を強化した操作実行ヘルパー
    /// </summary>
    private async Task<TResult> ExecuteWithRetryAsync<TResult>(
        Func<Task<TResult>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        var lastException = default(Exception);

        while (attempt < MaxRetryAttempts)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // キャンセル例外は再試行しない
            }
            catch (Exception ex)
            {
                attempt++;
                lastException = ex;

                _logger.LogWarning($"操作 '{operationName}' の試行 {attempt}/{MaxRetryAttempts} が失敗: {ex.Message}");

                if (attempt >= MaxRetryAttempts)
                {
                    _logger.LogError($"操作 '{operationName}' が {MaxRetryAttempts} 回の試行後に失敗しました", ex);
                    throw;
                }

                if (attempt < RetryDelays.Length)
                {
                    await Task.Delay(RetryDelays[attempt - 1], cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw lastException ?? new InvalidOperationException($"操作 '{operationName}' が予期せず失敗しました");
    }

    /// <summary>
    /// エラーハンドリングとログ記録を強化した操作実行ヘルパー（戻り値なし）
    /// </summary>
    private async Task ExecuteWithRetryAsync(
        Func<Task> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, operationName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// キャッシュから値を取得します。期限切れの場合はnullを返します。
    /// </summary>
    private T? GetCachedValue<T>(string key) where T : class
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var value) && value is CacheItem<T> cacheItem)
            {
                if (DateTime.Now - cacheItem.Timestamp < CacheExpiration)
                {
                    return cacheItem.Value;
                }
                else
                {
                    _cache.Remove(key);
                }
            }
            return null;
        }
    }

    /// <summary>
    /// キャッシュに値を設定します。
    /// </summary>
    private void SetCachedValue<T>(string key, T value) where T : class
    {
        lock (_cacheLock)
        {
            // キャッシュサイズ制限をチェックし、必要に応じて古いエントリを削除
            if (_cache.Count >= MaxCacheSize)
            {
                // 最も古いエントリを削除（単純なFIFO戦略）
                var oldestKey = _cache.Keys.FirstOrDefault();
                if (oldestKey != null)
                {
                    _cache.Remove(oldestKey);
                    _logger.LogDebug($"キャッシュサイズ制限のため古いエントリを削除しました: {oldestKey}");
                }
            }

            _cache[key] = new CacheItem<T>(value, DateTime.Now);
        }
    }

    /// <summary>
    /// キャッシュをクリアします。
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    /// <summary>
    /// キャッシュアイテムを表す内部クラス
    /// </summary>
    private class CacheItem<T>
    {
        public T Value { get; }
        public DateTime Timestamp { get; }

        public CacheItem(T value, DateTime timestamp)
        {
            Value = value;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// 依存関係を手動で組み立てるためのコンストラクタです。
    /// 既存コードとの互換性のために残しています。
    /// </summary>
    public CoreDriverService(SimpleLogger logger)
        : this(logger, CreateRepository(logger, out var repository), new SecurityService(logger, repository), null)
    {
    }

    private static DriverRepository CreateRepository(SimpleLogger logger, out DriverRepository repository)
    {
        repository = new DriverRepository(logger);
        return repository;
    }

    /// <summary>
    /// 依存性注入コンテナから生成される際に使用する標準コンストラクタです。
    /// </summary>
    public CoreDriverService(ISimpleLogger logger, IDriverRepository driverRepository, ISecurityService securityService, DriverManager? driverManager = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driverRepository = driverRepository ?? throw new ArgumentNullException(nameof(driverRepository));
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _driverManager = driverManager ?? new DriverManager(_logger, _driverRepository);

        _logger.LogInformation("CoreDriverService initialised");
    }

    public async Task<List<DriverInfo>> GetAllDriversAsync(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "AllDrivers";
        const string operationName = "GetAllDrivers";

        // キャッシュから取得を試行
        var cachedDrivers = GetCachedValue<List<DriverInfo>>(cacheKey);
        if (cachedDrivers != null)
        {
            LogStructured("DEBUG", "ドライバー一覧をキャッシュから取得しました", operationName);
            return cachedDrivers;
        }

        var stopwatch = Stopwatch.StartNew();
        var drivers = await ExecuteWithRetryAsync(
            () => _driverManager.GetDriversAsync(cancellationToken: cancellationToken),
            operationName,
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        // パフォーマンスメトリクスをログ記録
        LogPerformanceMetrics(operationName, stopwatch.Elapsed, drivers.Count);

        // キャッシュに保存（スレッドセーフに）
        SetCachedValue(cacheKey, drivers);

        LogStructured("DEBUG", $"ドライバー一覧をキャッシュに保存しました", operationName, $"件数: {drivers.Count}");
        return drivers;
    }

    public async Task<DriverInfo?> GetDriverByIdAsync(string driverId, CancellationToken cancellationToken = default)
    {
        var validation = InputValidator.ValidateDeviceId(driverId);
        try
        {
            validation.ThrowIfInvalid();
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning($"Driver lookup rejected: {InputValidator.SanitizeForLogging(driverId)} - {ex.Message}");
            throw;
        }

        return await _driverRepository.GetDriverByIdAsync(driverId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemStats> GetSystemStatsAsync(CancellationToken cancellationToken = default)
    {
        var drivers = await GetAllDriversAsync(cancellationToken).ConfigureAwait(false);

        // メモリ効率を考慮したカウント処理
        var totalDrivers = drivers.Count;
        var activeDrivers = CountWithMemoryOptimization(drivers, d => string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase));
        var problemDrivers = CountWithMemoryOptimization(drivers, d => !string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase));
        var outdatedDrivers = CountWithMemoryOptimization(drivers, d => string.Equals(d.Status, "Outdated", StringComparison.OrdinalIgnoreCase));
        var unsignedDrivers = CountWithMemoryOptimization(drivers, d => !d.IsSigned);

        return new SystemStats
        {
            TotalDrivers = totalDrivers,
            ActiveDrivers = activeDrivers,
            ProblemDrivers = problemDrivers,
            OutdatedDrivers = outdatedDrivers,
            UnsignedDrivers = unsignedDrivers,
            LastScanTime = DateTime.Now
        };
    }

    public async Task<DriverScanResult> ScanSystemAsync(CancellationToken cancellationToken = default)
    {
        var drivers = await GetAllDriversAsync(cancellationToken).ConfigureAwait(false);

        // メモリ効率を考慮したカウント処理
        var scannedDrivers = drivers.Count;
        var availableUpdates = CountWithMemoryOptimization(drivers, d => string.Equals(d.Status, "Outdated", StringComparison.OrdinalIgnoreCase));

        return new DriverScanResult
        {
            ScannedDrivers = scannedDrivers,
            AvailableUpdates = availableUpdates,
            ScanDate = DateTime.Now
        };
    }

    public async Task<DriverUpdateResult> UpdateDriversAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteWithRetryAsync(
            () => _driverManager.UpdateOutdatedDriversAsync(cancellationToken),
            "ドライバー更新",
            cancellationToken).ConfigureAwait(false);

        if (!result.Success && result.Errors.Count > 0)
        {
            foreach (var error in result.Errors)
            {
                _logger.LogWarning($"Driver update issue: {error}");
            }
        }

        return result;
    }

    public async Task<DriverComplianceReport> GenerateComplianceReportAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
        => await _driverManager.GenerateComplianceReportAsync(staleThreshold, cancellationToken).ConfigureAwait(false);

    public async Task<OperationResult> OptimizeSystemAsync()
    {
        var result = await _driverManager.OptimizeSystemAsync().ConfigureAwait(false);

        return new OperationResult
        {
            Success = result.Success,
            Message = result.Success
                ? $"Optimisation completed. Success: {result.SuccessCount}, Failed: {result.FailedCount}."
                : string.Join(Environment.NewLine, result.ErrorMessages)
        };
    }

    public async Task<BatchOperationResult> FixIssuesAsync()
    {
        var repairResult = await _driverManager.AutoRepairAsync().ConfigureAwait(false);
        var batchResult = new BatchOperationResult
        {
            Success = repairResult.Success,
            ProcessedCount = repairResult.SuccessCount,
            StartTime = DateTime.Now,
            EndTime = DateTime.Now,
            Message = repairResult.Success
                ? $"Resolved {repairResult.SuccessCount} drivers."
                : $"Failed repairs: {repairResult.FailedCount}."
        };

        if (!repairResult.Success)
        {
            batchResult.Errors.AddRange(repairResult.ErrorMessages);
        }

        return batchResult;
    }

    public async Task<OperationResult> BackupDriverAsync(string driverId)
    {
        var validation = InputValidator.ValidateDeviceId(driverId);
        if (!validation.IsValid)
        {
            _logger.LogWarning($"Backup rejected for driver ID: {InputValidator.SanitizeForLogging(driverId)} - {validation.Message}");
            return new OperationResult
            {
                Success = false,
                Message = validation.Message
            };
        }

        var driver = await _driverRepository.GetDriverByIdAsync(driverId).ConfigureAwait(false);
        if (driver is null)
        {
            return new OperationResult
            {
                Success = false,
                Message = $"Driver '{InputValidator.SanitizeForLogging(driverId)}' was not found."
            };
        }

        var success = await _driverManager.BackupDriverAsync(driverId).ConfigureAwait(false);
        return new OperationResult
        {
            Success = success,
            Message = success ? $"Backup completed for {driverId}." : $"Backup failed for {driverId}."
        };
    }

    public async Task<BatchOperationResult> BackupAllDriversAsync()
    {
        var drivers = await GetAllDriversAsync().ConfigureAwait(false);
        var result = new BatchOperationResult
        {
            StartTime = DateTime.Now,
            ProcessedCount = 0
        };

        foreach (var driver in drivers)
        {
            var backupResult = await _driverManager.BackupDriverAsync(driver.Id).ConfigureAwait(false);
            if (backupResult)
            {
                result.ProcessedCount++;
            }
            else
            {
                result.Errors.Add($"Backup failed for {driver.Name} ({driver.Id}).");
            }
        }

        result.EndTime = DateTime.Now;
        result.Success = result.Errors.Count == 0;
        result.Message = result.Success
            ? $"Backups created for {result.ProcessedCount} drivers."
            : $"Created {result.ProcessedCount} backups with {result.Errors.Count} failures.";

        return result;
    }

    /// <summary>
    /// メモリ効率を考慮した大きなリストの処理
    /// </summary>
    private int CountWithMemoryOptimization<T>(IEnumerable<T> source, Func<T, bool> predicate)
    {
        // 大きなリストの場合、メモリ効率を考慮して処理
        if (source is ICollection<T> collection)
        {
            return collection.Count(predicate);
        }

        var count = 0;
        foreach (var item in source)
        {
            if (predicate(item))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// メモリークリーンアップを実行します。
    /// </summary>
    public void PerformMemoryCleanup()
    {
        lock (_cacheLock)
        {
            // 期限切れのキャッシュエントリを削除
            var expiredKeys = _cache.Keys
                .Where(key => _cache[key] is CacheItem<object> cacheItem &&
                             DateTime.Now - ((CacheItem<object>)_cache[key]).Timestamp >= CacheExpiration)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.Remove(key);
            }

            if (expiredKeys.Any())
            {
                _logger.LogDebug($"メモリクリーンアップ: {expiredKeys.Count} 個の期限切れキャッシュエントリを削除しました");
            }
        }

        // ガベージコレクションを提案（ただし強制しない）
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
    }

    /// <summary>
    /// リソースを破棄します。
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースを破棄します。
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // マネージドリソースのクリーンアップ
            ClearCache();
        }
        // アンマネージドリソースのクリーンアップ（該当なし）
    }

    /// <summary>
    /// ファイナライザー
    /// </summary>
    ~CoreDriverService()
    {
        Dispose(false);
    }

    /// <summary>
    /// 構造化ログを記録します。
    /// </summary>
    private void LogStructured(string level, string message, string operation, string details = null, Exception exception = null)
    {
        var logMessage = $"[{operation}] {message}";
        if (!string.IsNullOrEmpty(details))
        {
            logMessage += $" | Details: {details}";
        }

        switch (level.ToUpper())
        {
            case "DEBUG":
                if (exception != null)
                    _logger.LogDebug(logMessage, exception);
                else
                    _logger.LogDebug(logMessage);
                break;
            case "INFO":
                if (exception != null)
                    _logger.LogInformation(logMessage, exception);
                else
                    _logger.LogInformation(logMessage);
                break;
            case "WARNING":
                if (exception != null)
                    _logger.LogWarning(logMessage, exception);
                else
                    _logger.LogWarning(logMessage);
                break;
            case "ERROR":
                _logger.LogError(logMessage, exception);
                break;
            default:
                _logger.LogInformation(logMessage);
                break;
        }
    }

    /// <summary>
    /// パフォーマンスメトリクスをログ記録します。
    /// </summary>
    private void LogPerformanceMetrics(string operation, TimeSpan duration, int itemCount = 0)
    {
        var details = $"Duration: {duration.TotalMilliseconds:F2}ms";
        if (itemCount > 0)
        {
            details += $", Items: {itemCount}";
        }

        LogStructured("DEBUG", $"パフォーマンスメトリクス: {operation}", operation, details);
    }

    /// <summary>
    /// 機密情報を含む可能性のあるログメッセージを安全に記録します。
    /// </summary>
    private void LogSecure(string level, string message, string operation, Dictionary<string, string> secureData = null, Exception exception = null)
    {
        // 機密情報を含む可能性のあるデータをサニタイズ
        var sanitizedMessage = message;
        if (secureData != null)
        {
            foreach (var pair in secureData)
            {
                if (pair.Value.Contains("password") || pair.Value.Contains("secret") || pair.Value.Contains("key"))
                {
                    sanitizedMessage = sanitizedMessage.Replace(pair.Value, "***REDACTED***");
                }
            }
        }

        LogStructured(level, sanitizedMessage, operation, null, exception);
    }

    /// <summary>
    /// 入力データを検証し、安全性を確保します。
    /// </summary>
    private void ValidateInput(string input, string fieldName, int maxLength = 255)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ValidationException($"{fieldName} は必須です。");
        }

        if (input.Length > maxLength)
        {
            throw new ValidationException($"{fieldName} は {maxLength} 文字以内で入力してください。");
        }

        // 危険な文字をチェック
        var dangerousChars = new[] { '<', '>', '"', '\'', '&', '|', ';', '$', '`' };
        if (dangerousChars.Any(c => input.Contains(c)))
        {
            throw new ValidationException($"{fieldName} に危険な文字が含まれています。");
        }
    }

    public async Task<PerformanceReport> GetPerformanceReportAsync()
    {
        var drivers = await GetAllDriversAsync().ConfigureAwait(false);
        var report = new PerformanceReport
        {
            TotalDrivers = drivers.Count,
            ActiveDrivers = drivers.Count(d => string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase)),
            ProblemDrivers = drivers.Count(d => !string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase)),
            LastScanTime = DateTime.Now
        };

        return report;
    }

    public async Task<SecurityReport> GetSecurityReportAsync()
        => await _securityService.PerformSecurityAuditAsync().ConfigureAwait(false);
}
