using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Resilience;

/// <summary>
/// Enterprise-grade retry policy with exponential backoff and circuit breaker pattern
/// Implements fault tolerance for national-level reliability
/// </summary>
public class RetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _backoffMultiplier;
    private readonly Func<Exception, bool> _retryableExceptionPredicate;
    private readonly ISimpleLogger? _logger;

    public RetryPolicy(
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        Func<Exception, bool>? retryableExceptionPredicate = null,
        ISimpleLogger? logger = null)
    {
        if (maxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries must be non-negative");
        }

        if (backoffMultiplier < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(backoffMultiplier), "Backoff multiplier must be >= 1.0");
        }

        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromMinutes(1);
        _backoffMultiplier = backoffMultiplier;
        _retryableExceptionPredicate = retryableExceptionPredicate ?? DefaultRetryableExceptionPredicate;
        _logger = logger;
    }

    /// <summary>
    /// Executes an operation with retry policy
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default,
        string? operationName = null)
    {
        if (operation == null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        var attemptNumber = 0;
        var exceptions = new List<Exception>();
        operationName ??= "Operation";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                attemptNumber++;
                _logger?.LogInformation($"{operationName} - Attempt {attemptNumber}/{_maxRetries + 1}");

                var result = await operation().ConfigureAwait(false);

                if (attemptNumber > 1)
                {
                    _logger?.LogInformation($"{operationName} succeeded after {attemptNumber} attempt(s)");
                }

                return result;
            }
            catch (Exception ex) when (attemptNumber <= _maxRetries && _retryableExceptionPredicate(ex))
            {
                exceptions.Add(ex);
                _logger?.LogWarning($"{operationName} failed on attempt {attemptNumber}: {ex.Message}");

                if (attemptNumber < _maxRetries + 1)
                {
                    var delay = CalculateDelay(attemptNumber);
                    _logger?.LogInformation($"{operationName} - Waiting {delay.TotalMilliseconds}ms before retry...");

                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.LogWarning($"{operationName} cancelled during retry delay");
                        throw;
                    }
                }
                else
                {
                    _logger?.LogError($"{operationName} failed after {attemptNumber} attempts");
                    throw new RetryExhaustedException(
                        $"{operationName} failed after {attemptNumber} attempts",
                        exceptions);
                }
            }
            catch (Exception ex)
            {
                // Non-retryable exception or max retries exceeded
                _logger?.LogError($"{operationName} encountered non-retryable exception: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Executes an operation with retry policy (void return)
    /// </summary>
    public async Task ExecuteAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default,
        string? operationName = null)
    {
        await ExecuteAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, cancellationToken, operationName).ConfigureAwait(false);
    }

    private TimeSpan CalculateDelay(int attemptNumber)
    {
        var delay = TimeSpan.FromMilliseconds(
            _initialDelay.TotalMilliseconds * Math.Pow(_backoffMultiplier, attemptNumber - 1));

        // Add jitter to prevent thundering herd
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        delay = delay.Add(jitter);

        return delay > _maxDelay ? _maxDelay : delay;
    }

    private static bool DefaultRetryableExceptionPredicate(Exception ex)
    {
        return ex is TimeoutException
            || ex is System.Net.Http.HttpRequestException
            || ex is System.IO.IOException
            || ex is System.Net.Sockets.SocketException
            || ex is TaskCanceledException && ex.InnerException is TimeoutException;
    }

    /// <summary>
    /// Creates a default retry policy for WMI operations
    /// </summary>
    public static RetryPolicy ForWmiOperations(ISimpleLogger? logger = null)
    {
        return new RetryPolicy(
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(500),
            maxDelay: TimeSpan.FromSeconds(10),
            backoffMultiplier: 2.0,
            retryableExceptionPredicate: ex =>
                ex is System.Management.ManagementException ||
                ex is System.Runtime.InteropServices.COMException ||
                ex is TimeoutException,
            logger: logger);
    }

    /// <summary>
    /// Creates a default retry policy for file operations
    /// </summary>
    public static RetryPolicy ForFileOperations(ISimpleLogger? logger = null)
    {
        return new RetryPolicy(
            maxRetries: 5,
            initialDelay: TimeSpan.FromMilliseconds(100),
            maxDelay: TimeSpan.FromSeconds(5),
            backoffMultiplier: 2.0,
            retryableExceptionPredicate: ex =>
                ex is System.IO.IOException ||
                ex is UnauthorizedAccessException,
            logger: logger);
    }

    /// <summary>
    /// Creates a default retry policy for network operations
    /// </summary>
    public static RetryPolicy ForNetworkOperations(ISimpleLogger? logger = null)
    {
        return new RetryPolicy(
            maxRetries: 3,
            initialDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(30),
            backoffMultiplier: 2.0,
            retryableExceptionPredicate: ex =>
                ex is System.Net.Http.HttpRequestException ||
                ex is System.Net.Sockets.SocketException ||
                ex is TimeoutException,
            logger: logger);
    }
}

/// <summary>
/// Exception thrown when retry attempts are exhausted
/// </summary>
public class RetryExhaustedException : Exception
{
    public List<Exception> Attempts { get; }

    public RetryExhaustedException(string message, List<Exception> attempts)
        : base(message, attempts.Count > 0 ? attempts[attempts.Count - 1] : null)
    {
        Attempts = attempts;
    }
}

/// <summary>
/// Circuit breaker pattern for preventing cascading failures
/// </summary>
public class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly ISimpleLogger? _logger;
    private readonly object _lock = new();

    private int _consecutiveFailures;
    private DateTime _lastFailureTime;
    private CircuitBreakerState _state;

    public CircuitBreaker(
        int failureThreshold = 5,
        TimeSpan? timeout = null,
        ISimpleLogger? logger = null)
    {
        _failureThreshold = failureThreshold;
        _timeout = timeout ?? TimeSpan.FromMinutes(1);
        _logger = logger;
        _state = CircuitBreakerState.Closed;
    }

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string? operationName = null)
    {
        operationName ??= "Operation";

        lock (_lock)
        {
            if (_state == CircuitBreakerState.Open)
            {
                if (DateTime.UtcNow - _lastFailureTime >= _timeout)
                {
                    _state = CircuitBreakerState.HalfOpen;
                    _logger?.LogInformation($"Circuit breaker entering half-open state for {operationName}");
                }
                else
                {
                    throw new CircuitBreakerOpenException($"Circuit breaker is open for {operationName}");
                }
            }
        }

        try
        {
            var result = await operation().ConfigureAwait(false);

            lock (_lock)
            {
                if (_state == CircuitBreakerState.HalfOpen)
                {
                    _state = CircuitBreakerState.Closed;
                    _consecutiveFailures = 0;
                    _logger?.LogInformation($"Circuit breaker closed for {operationName}");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;

                if (_consecutiveFailures >= _failureThreshold || _state == CircuitBreakerState.HalfOpen)
                {
                    _state = CircuitBreakerState.Open;
                    _logger?.LogError($"Circuit breaker opened for {operationName} after {_consecutiveFailures} failures");
                }
            }

            throw;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _consecutiveFailures = 0;
            _lastFailureTime = DateTime.MinValue;
        }
    }
}

public enum CircuitBreakerState
{
    Closed,    // Normal operation
    Open,      // Failing, rejecting calls
    HalfOpen   // Testing if system recovered
}

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message)
    {
    }
}
