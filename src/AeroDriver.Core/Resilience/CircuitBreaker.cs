using System;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Resilience;

/// <summary>
/// サーキットブレーカーパターン実装
/// 障害発生時に自動的に処理を停止し、回復を試行します
/// </summary>
public class CircuitBreaker
{
    private readonly CircuitBreakerConfiguration _config;
    private readonly ISimpleLogger _logger;
    private readonly AuditTrail _auditTrail;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount;
    private DateTime _lastFailureTime;
    private DateTime _nextAttemptTime = DateTime.UtcNow;

    public CircuitBreaker(CircuitBreakerConfiguration config, AuditTrail auditTrail, ISimpleLogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 操作を実行します
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);

        try
        {
            if (_state == CircuitBreakerState.Open)
            {
                if (DateTime.UtcNow < _nextAttemptTime)
                {
                    throw new CircuitBreakerOpenException($"Circuit breaker is OPEN. Next retry at {_nextAttemptTime}");
                }

                // ハーフオープン状態に移行
                _state = CircuitBreakerState.HalfOpen;
                await _auditTrail.RecordSecurityEventAsync(
                    SecurityEventType.SuspiciousActivity,
                    $"Circuit breaker transitioning to Half-Open state",
                    SecuritySeverity.Low,
                    cancellationToken: cancellationToken);
            }

            if (_state == CircuitBreakerState.HalfOpen)
            {
                // ハーフオープン状態では制限付きで処理を許可
                return await ExecuteWithHalfOpenLogicAsync(operation, cancellationToken);
            }

            // クローズ状態での通常処理
            return await ExecuteWithClosedLogicAsync(operation, cancellationToken);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task<TResult> ExecuteWithClosedLogicAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        try
        {
            var result = await operation(cancellationToken);

            // 成功時は失敗カウンターをリセット
            if (_failureCount > 0)
            {
                _failureCount = 0;
                await _auditTrail.RecordSecurityEventAsync(
                    SecurityEventType.SuspiciousActivity,
                    "Circuit breaker failure count reset",
                    SecuritySeverity.Low,
                    cancellationToken: cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(ex, cancellationToken);
            throw;
        }
    }

    private async Task<TResult> ExecuteWithHalfOpenLogicAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        try
        {
            var result = await operation(cancellationToken);

            // ハーフオープンで成功したらクローズ状態に戻る
            await TransitionToClosedAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            // ハーフオープンで失敗したらオープン状態に戻る
            await TransitionToOpenAsync(cancellationToken);
            throw;
        }
    }

    private async Task HandleFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        _failureCount++;
        _lastFailureTime = DateTime.UtcNow;

        await _logger.LogSecurityEventAsync("CircuitBreakerFailure",
            $"Circuit breaker failure count: {_failureCount}. Exception: {exception.Message}");

        if (_failureCount >= _config.FailureThreshold)
        {
            await TransitionToOpenAsync(cancellationToken);
        }
    }

    private async Task TransitionToOpenAsync(CancellationToken cancellationToken)
    {
        _state = CircuitBreakerState.Open;
        _nextAttemptTime = DateTime.UtcNow.Add(_config.Timeout);

        await _auditTrail.RecordSecurityEventAsync(
            SecurityEventType.SuspiciousActivity,
            $"Circuit breaker opened. Next retry at {_nextAttemptTime}",
            SecuritySeverity.Medium,
            cancellationToken: cancellationToken);

        await _logger.LogSecurityEventAsync("CircuitBreakerOpened",
            $"Circuit breaker opened for {_config.Timeout.TotalSeconds} seconds");
    }

    private async Task TransitionToClosedAsync(CancellationToken cancellationToken)
    {
        _state = CircuitBreakerState.Closed;
        _failureCount = 0;

        await _auditTrail.RecordSecurityEventAsync(
            SecurityEventType.SuspiciousActivity,
            "Circuit breaker closed - service recovered",
            SecuritySeverity.Low,
            cancellationToken: cancellationToken);

        await _logger.LogSecurityEventAsync("CircuitBreakerClosed", "Circuit breaker closed - service recovered");
    }

    /// <summary>
    /// 現在の状態を取得します
    /// </summary>
    public CircuitBreakerState State => _state;

    /// <summary>
    /// 失敗カウンターを取得します
    /// </summary>
    public int FailureCount => _failureCount;

    /// <summary>
    /// 次回試行予定時間を取得します
    /// </summary>
    public DateTime NextAttemptTime => _nextAttemptTime;
}

/// <summary>
/// サーキットブレーカー設定
/// </summary>
public class CircuitBreakerConfiguration
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// サーキットブレーカー状態
/// </summary>
public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// サーキットブレーカーがオープン状態の例外
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}
