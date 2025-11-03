using System;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core
{
    /// <summary>
    /// 指数バックオフを使用したリトライポリシーの実装
    /// </summary>
    public class ExponentialBackoffRetryPolicy : IRetryPolicy
    {
        private readonly int _maxRetries;
        private readonly TimeSpan _initialDelay;
        private readonly TimeSpan _maxDelay;
        private readonly double _backoffMultiplier;
        private readonly ILogger _logger;

        public ExponentialBackoffRetryPolicy(
            int maxRetries = 3,
            TimeSpan? initialDelay = null,
            TimeSpan? maxDelay = null,
            double backoffMultiplier = 2.0,
            ILogger? logger = null)
        {
            _maxRetries = maxRetries;
            _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100);
            _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
            _backoffMultiplier = backoffMultiplier;
            _logger = logger ?? new LoggerAdapter("RetryPolicy");
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var exceptions = new List<Exception>();

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        await _logger.LogInformationAsync($"Retry attempt {attempt} for operation", cancellationToken: cancellationToken);
                    }

                    return await operation();
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    exceptions.Add(ex);

                    if (attempt == _maxRetries)
                    {
                        // 最後の試行が失敗した場合
                        await _logger.LogErrorAsync($"Operation failed after {_maxRetries} retries", new { Attempt = attempt }, ex, cancellationToken);
                        throw new AggregateException($"Operation failed after {_maxRetries} retries", exceptions);
                    }

                    // 遅延を計算
                    var delay = CalculateDelay(attempt);
                    await _logger.LogWarningAsync($"Operation failed, retrying in {delay.TotalMilliseconds}ms", new { Attempt = attempt, Delay = delay }, ex, cancellationToken);

                    await Task.Delay(delay, cancellationToken);
                }
            }

            // この行は到達しないはずだが、コンパイラを満足させるため
            throw new AggregateException($"Operation failed after {_maxRetries} retries", exceptions);
        }

        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async () =>
            {
                await operation();
                return true;
            }, cancellationToken);
        }

        private TimeSpan CalculateDelay(int attempt)
        {
            var delay = TimeSpan.FromMilliseconds(_initialDelay.TotalMilliseconds * Math.Pow(_backoffMultiplier, attempt));

            // 最大遅延を超えないようにする
            if (delay > _maxDelay)
            {
                delay = _maxDelay;
            }

            // ジッターを追加してスラッシングを防ぐ
            var jitter = Random.Shared.NextDouble() * 0.1 * delay.TotalMilliseconds;
            delay = delay.Add(TimeSpan.FromMilliseconds(jitter));

            return delay;
        }
    }

    /// <summary>
    /// 固定間隔のリトライポリシー
    /// </summary>
    public class FixedIntervalRetryPolicy : IRetryPolicy
    {
        private readonly int _maxRetries;
        private readonly TimeSpan _delay;
        private readonly ILogger _logger;

        public FixedIntervalRetryPolicy(int maxRetries = 3, TimeSpan? delay = null, ILogger? logger = null)
        {
            _maxRetries = maxRetries;
            _delay = delay ?? TimeSpan.FromSeconds(1);
            _logger = logger ?? new LoggerAdapter("FixedRetryPolicy");
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var exceptions = new List<Exception>();

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        await _logger.LogInformationAsync($"Retry attempt {attempt} for operation", cancellationToken: cancellationToken);
                    }

                    return await operation();
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    exceptions.Add(ex);

                    if (attempt == _maxRetries)
                    {
                        await _logger.LogErrorAsync($"Operation failed after {_maxRetries} retries", new { Attempt = attempt }, ex, cancellationToken);
                        throw new AggregateException($"Operation failed after {_maxRetries} retries", exceptions);
                    }

                    await _logger.LogWarningAsync($"Operation failed, retrying in {_delay.TotalMilliseconds}ms", new { Attempt = attempt }, ex, cancellationToken);
                    await Task.Delay(_delay, cancellationToken);
                }
            }

            throw new AggregateException($"Operation failed after {_maxRetries} retries", exceptions);
        }

        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async () =>
            {
                await operation();
                return true;
            }, cancellationToken);
        }
    }

    /// <summary>
    /// カスタム条件に基づくリトライポリシー
    /// </summary>
    public class ConditionalRetryPolicy : IRetryPolicy
    {
        private readonly int _maxRetries;
        private readonly Func<Exception, int, bool> _shouldRetry;
        private readonly Func<int, TimeSpan> _delayCalculator;
        private readonly ILogger _logger;

        public ConditionalRetryPolicy(
            int maxRetries = 3,
            Func<Exception, int, bool>? shouldRetry = null,
            Func<int, TimeSpan>? delayCalculator = null,
            ILogger? logger = null)
        {
            _maxRetries = maxRetries;
            _shouldRetry = shouldRetry ?? ((ex, attempt) => true); // デフォルトでは常にリトライ
            _delayCalculator = delayCalculator ?? (attempt => TimeSpan.FromSeconds(Math.Min(attempt, 30)));
            _logger = logger ?? new LoggerAdapter("ConditionalRetryPolicy");
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var exceptions = new List<Exception>();

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        await _logger.LogInformationAsync($"Retry attempt {attempt} for operation", cancellationToken: cancellationToken);
                    }

                    return await operation();
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    exceptions.Add(ex);

                    // リトライ条件をチェック
                    if (!_shouldRetry(ex, attempt) || attempt == _maxRetries)
                    {
                        await _logger.LogErrorAsync($"Operation failed after {attempt} retries", new { Attempt = attempt }, ex, cancellationToken);
                        throw new AggregateException($"Operation failed after {attempt} retries", exceptions);
                    }

                    var delay = _delayCalculator(attempt);
                    await _logger.LogWarningAsync($"Operation failed, retrying in {delay.TotalMilliseconds}ms", new { Attempt = attempt }, ex, cancellationToken);
                    await Task.Delay(delay, cancellationToken);
                }
            }

            throw new AggregateException($"Operation failed after {_maxRetries} retries", exceptions);
        }

        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async () =>
            {
                await operation();
                return true;
            }, cancellationToken);
        }
    }
}
