using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core
{
    /// <summary>
    /// 回復力のある操作実行のユーティリティ
    /// </summary>
    public static class ResilienceHelper
    {
        private static readonly ILogger _defaultLogger = new LoggerAdapter("ResilienceHelper");

        /// <summary>
        /// タイムアウト付きで操作を実行
        /// </summary>
        public static async Task<T> ExecuteWithTimeoutAsync<T>(
            Func<Task<T>> operation,
            TimeSpan timeout,
            string operationName = "operation",
            ILogger? logger = null)
        {
            var loggerToUse = logger ?? _defaultLogger;

            using var cts = new CancellationTokenSource(timeout);

            try
            {
                await loggerToUse.LogDebugAsync($"Starting {operationName} with timeout {timeout.TotalSeconds}s");
                var result = await operation();
                await loggerToUse.LogDebugAsync($"{operationName} completed successfully");
                return result;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                await loggerToUse.LogWarningAsync($"{operationName} timed out after {timeout.TotalSeconds}s");
                throw new TimeoutException($"{operationName} timed out after {timeout.TotalSeconds} seconds");
            }
            catch (Exception ex)
            {
                await loggerToUse.LogErrorAsync($"{operationName} failed", null, ex);
                throw;
            }
        }

        /// <summary>
        /// リトライ付きで操作を実行
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            IRetryPolicy retryPolicy,
            string operationName = "operation",
            ILogger? logger = null)
        {
            var loggerToUse = logger ?? _defaultLogger;

            try
            {
                await loggerToUse.LogDebugAsync($"Starting {operationName} with retry policy");
                var result = await retryPolicy.ExecuteAsync(operation);
                await loggerToUse.LogDebugAsync($"{operationName} completed successfully with retry");
                return result;
            }
            catch (Exception ex)
            {
                await loggerToUse.LogErrorAsync($"{operationName} failed after retries", null, ex);
                throw;
            }
        }

        /// <summary>
        /// タイムアウトとリトライ付きで操作を実行
        /// </summary>
        public static async Task<T> ExecuteWithTimeoutAndRetryAsync<T>(
            Func<Task<T>> operation,
            TimeSpan timeout,
            IRetryPolicy retryPolicy,
            string operationName = "operation",
            ILogger? logger = null)
        {
            return await ExecuteWithRetryAsync(
                () => ExecuteWithTimeoutAsync(operation, timeout, operationName, logger),
                retryPolicy,
                operationName,
                logger);
        }

        /// <summary>
        /// サーキットブレーカー付きで操作を実行
        /// </summary>
        public static async Task<T> ExecuteWithCircuitBreakerAsync<T>(
            Func<Task<T>> operation,
            ICircuitBreaker circuitBreaker,
            string operationName = "operation",
            ILogger? logger = null)
        {
            var loggerToUse = logger ?? _defaultLogger;

            try
            {
                await loggerToUse.LogDebugAsync($"Executing {operationName} through circuit breaker");
                var result = await circuitBreaker.ExecuteAsync(operation);
                await loggerToUse.LogDebugAsync($"{operationName} completed successfully through circuit breaker");
                return result;
            }
            catch (CircuitBreakerOpenException ex)
            {
                await loggerToUse.LogWarningAsync($"{operationName} failed due to open circuit breaker", null, ex);
                throw;
            }
            catch (Exception ex)
            {
                await loggerToUse.LogErrorAsync($"{operationName} failed", null, ex);
                throw;
            }
        }

        /// <summary>
        /// バルクヘッドパターンで操作を実行
        /// </summary>
        public static async Task<T> ExecuteInBulkheadAsync<T>(
            Func<Task<T>> operation,
            IBulkhead bulkhead,
            string operationName = "operation",
            ILogger? logger = null)
        {
            var loggerToUse = logger ?? _defaultLogger;

            try
            {
                await loggerToUse.LogDebugAsync($"Executing {operationName} in bulkhead");
                var result = await bulkhead.ExecuteAsync(operation);
                await loggerToUse.LogDebugAsync($"{operationName} completed successfully in bulkhead");
                return result;
            }
            catch (BulkheadRejectedException ex)
            {
                await loggerToUse.LogWarningAsync($"{operationName} rejected by bulkhead", null, ex);
                throw;
            }
            catch (Exception ex)
            {
                await loggerToUse.LogErrorAsync($"{operationName} failed in bulkhead", null, ex);
                throw;
            }
        }

        /// <summary>
        /// フォールバック付きで操作を実行
        /// </summary>
        public static async Task<T> ExecuteWithFallbackAsync<T>(
            Func<Task<T>> primaryOperation,
            Func<Task<T>> fallbackOperation,
            string operationName = "operation",
            ILogger? logger = null)
        {
            var loggerToUse = logger ?? _defaultLogger;

            try
            {
                await loggerToUse.LogDebugAsync($"Executing primary {operationName}");
                return await primaryOperation();
            }
            catch (Exception ex)
            {
                await loggerToUse.LogWarningAsync($"Primary {operationName} failed, executing fallback", null, ex);

                try
                {
                    return await fallbackOperation();
                }
                catch (Exception fallbackEx)
                {
                    await loggerToUse.LogErrorAsync($"Fallback {operationName} also failed", null, fallbackEx);
                    throw new AggregateException($"Both primary and fallback {operationName} failed", ex, fallbackEx);
                }
            }
        }

        /// <summary>
        /// キャッシュ付きで操作を実行
        /// </summary>
        public static async Task<T> ExecuteWithCacheAsync<T>(
            Func<Task<T>> operation,
            string cacheKey,
            ICacheProvider cache,
            TimeSpan? cacheDuration = null,
            string operationName = "operation",
            ILogger? logger = null)
        {
            var loggerToUse = logger ?? _defaultLogger;

            // キャッシュから取得を試行
            if (cache.TryGet<T>(cacheKey, out var cachedResult))
            {
                await loggerToUse.LogDebugAsync($"Retrieved {operationName} result from cache");
                return cachedResult;
            }

            // キャッシュにない場合は操作を実行
            var result = await operation();

            // 結果をキャッシュ
            var duration = cacheDuration ?? TimeSpan.FromMinutes(5);
            cache.Set(cacheKey, result, duration);

            await loggerToUse.LogDebugAsync($"Cached {operationName} result for {duration.TotalMinutes} minutes");
            return result;
        }

        /// <summary>
        /// ヘルスチェック付きで操作を実行
        /// </summary>
        public static async Task<T> ExecuteWithHealthCheckAsync<T>(
            Func<Task<T>> operation,
            IHealthCheck healthCheck,
            string operationName = "operation",
            ILogger? logger = null)
        {
            var loggerToUse = logger ?? _defaultLogger;

            // ヘルスチェックを実行
            var healthResult = await healthCheck.CheckHealthAsync();
            if (healthResult.Status != HealthStatus.Healthy)
            {
                await loggerToUse.LogWarningAsync($"Health check failed for {operationName}, status: {healthResult.Status}");
                throw new HealthCheckException($"Service is not healthy: {healthResult.Description}");
            }

            return await operation();
        }
    }

    /// <summary>
    /// サーキットブレーカーインターフェース
    /// </summary>
    public interface ICircuitBreaker
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation);
        CircuitBreakerState State { get; }
        void Reset();
    }

    /// <summary>
    /// バルクヘッドインターフェース
    /// </summary>
    public interface IBulkhead
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation);
        int AvailableSlots { get; }
    }

    /// <summary>
    /// キャッシュプロバイダーインターフェース
    /// </summary>
    public interface ICacheProvider
    {
        bool TryGet<T>(string key, out T value);
        void Set<T>(string key, T value, TimeSpan duration);
        void Remove(string key);
        void Clear();
    }

    /// <summary>
    /// ヘルスチェックインターフェース
    /// </summary>
    public interface IHealthCheck
    {
        Task<HealthCheckResult> CheckHealthAsync();
    }

    /// <summary>
    /// サーキットブレーカーの状態
    /// </summary>
    public enum CircuitBreakerState
    {
        Closed,
        Open,
        HalfOpen
    }

    /// <summary>
    /// ヘルスステータス
    /// </summary>
    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy
    }

    /// <summary>
    /// ヘルスチェック結果
    /// </summary>
    public class HealthCheckResult
    {
        public HealthStatus Status { get; set; }
        public string Description { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }

    /// <summary>
    /// サーキットブレーカー例外
    /// </summary>
    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }

    /// <summary>
    /// バルクヘッド拒否例外
    /// </summary>
    public class BulkheadRejectedException : Exception
    {
        public BulkheadRejectedException(string message) : base(message) { }
    }

    /// <summary>
    /// ヘルスチェック例外
    /// </summary>
    public class HealthCheckException : Exception
    {
        public HealthCheckException(string message) : base(message) { }
    }

    /// <summary>
    /// シンプルなサーキットブレーカーの実装
    /// </summary>
    public class SimpleCircuitBreaker : ICircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _timeoutPeriod;
        private readonly ILogger _logger;

        private int _failureCount;
        private DateTime _lastFailureTime;
        private CircuitBreakerState _state = CircuitBreakerState.Closed;

        public SimpleCircuitBreaker(int failureThreshold = 5, TimeSpan? timeoutPeriod = null, ILogger? logger = null)
        {
            _failureThreshold = failureThreshold;
            _timeoutPeriod = timeoutPeriod ?? TimeSpan.FromMinutes(1);
            _logger = logger ?? new LoggerAdapter("CircuitBreaker");
        }

        public CircuitBreakerState State => _state;

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
        {
            if (_state == CircuitBreakerState.Open)
            {
                if (DateTime.UtcNow - _lastFailureTime < _timeoutPeriod)
                {
                    throw new CircuitBreakerOpenException("Circuit breaker is open");
                }

                // タイムアウト期間が経過したらHalf-Open状態に
                _state = CircuitBreakerState.HalfOpen;
                await _logger.LogInformationAsync("Circuit breaker transitioning to half-open state");
            }

            try
            {
                var result = await operation();

                // 成功したらClosed状態にリセット
                if (_state == CircuitBreakerState.HalfOpen)
                {
                    _state = CircuitBreakerState.Closed;
                    _failureCount = 0;
                    await _logger.LogInformationAsync("Circuit breaker closed after successful operation");
                }

                return result;
            }
            catch (Exception ex)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;

                if (_failureCount >= _failureThreshold)
                {
                    _state = CircuitBreakerState.Open;
                    await _logger.LogWarningAsync($"Circuit breaker opened after {_failureCount} failures");
                }

                throw;
            }
        }

        public void Reset()
        {
            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _lastFailureTime = DateTime.MinValue;
        }
    }

    /// <summary>
    /// シンプルなバルクヘッドの実装
    /// </summary>
    public class SimpleBulkhead : IBulkhead
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly ILogger _logger;

        public SimpleBulkhead(int maxConcurrentOperations, ILogger? logger = null)
        {
            _semaphore = new SemaphoreSlim(maxConcurrentOperations, maxConcurrentOperations);
            _logger = logger ?? new LoggerAdapter("Bulkhead");
        }

        public int AvailableSlots => _semaphore.CurrentCount;

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
        {
            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                throw new BulkheadRejectedException("Bulkhead rejected the operation due to capacity limit");
            }

            try
            {
                await _logger.LogDebugAsync($"Executing operation in bulkhead (available slots: {_semaphore.CurrentCount})");
                return await operation();
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    /// <summary>
    /// メモリベースのキャッシュプロバイダー
    /// </summary>
    public class MemoryCacheProvider : ICacheProvider
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly ILogger _logger;

        public MemoryCacheProvider(ILogger? logger = null)
        {
            _logger = logger ?? new LoggerAdapter("MemoryCache");
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow < entry.Expiration)
                {
                    value = (T)entry.Value;
                    return true;
                }
                else
                {
                    // 期限切れのエントリを削除
                    _cache.TryRemove(key, out _);
                }
            }

            value = default!;
            return false;
        }

        public void Set<T>(string key, T value, TimeSpan duration)
        {
            var entry = new CacheEntry
            {
                Value = value,
                Expiration = DateTime.UtcNow.Add(duration)
            };

            _cache[key] = entry;
            _logger.LogDebugAsync($"Cached item with key {key} for {duration.TotalMinutes} minutes");
        }

        public void Remove(string key)
        {
            _cache.TryRemove(key, out _);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        private class CacheEntry
        {
            public object Value { get; set; } = null!;
            public DateTime Expiration { get; set; }
        }
    }

    /// <summary>
    /// 基本的なヘルスチェックの実装
    /// </summary>
    public class BasicHealthCheck : IHealthCheck
    {
        private readonly Func<Task<bool>> _healthCheckFunc;
        private readonly string _name;
        private readonly ILogger _logger;

        public BasicHealthCheck(Func<Task<bool>> healthCheckFunc, string name = "HealthCheck", ILogger? logger = null)
        {
            _healthCheckFunc = healthCheckFunc ?? throw new ArgumentNullException(nameof(healthCheckFunc));
            _name = name;
            _logger = logger ?? new LoggerAdapter("HealthCheck");
        }

        public async Task<HealthCheckResult> CheckHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var isHealthy = await _healthCheckFunc();

                stopwatch.Stop();

                var result = new HealthCheckResult
                {
                    Status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                    Description = isHealthy ? $"{_name} is healthy" : $"{_name} is unhealthy",
                    Duration = stopwatch.Elapsed,
                    Data = new Dictionary<string, object>
                    {
                        ["check_name"] = _name,
                        ["timestamp"] = DateTime.UtcNow
                    }
                };

                await _logger.LogDebugAsync($"{_name} health check result: {result.Status}");
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                var result = new HealthCheckResult
                {
                    Status = HealthStatus.Unhealthy,
                    Description = $"{_name} health check failed: {ex.Message}",
                    Duration = stopwatch.Elapsed,
                    Data = new Dictionary<string, object>
                    {
                        ["check_name"] = _name,
                        ["error"] = ex.Message,
                        ["timestamp"] = DateTime.UtcNow
                    }
                };

                await _logger.LogWarningAsync($"{_name} health check failed", null, ex);
                return result;
            }
        }
    }
}
