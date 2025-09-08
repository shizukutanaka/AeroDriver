using System.Collections.Concurrent;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// 非同期処理を最適化するヘルパークラス
    /// </summary>
    public static class AsyncHelper
    {
        /// <summary>
        /// 複数のタスクを並列実行し、結果を収集
        /// </summary>
        public static async Task<List<T>> RunParallelAsync<T>(
            IEnumerable<Func<Task<T>>> taskFactories,
            int maxConcurrency = 4,
            CancellationToken cancellationToken = default)
        {
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var results = new ConcurrentBag<T>();
            
            var tasks = taskFactories.Select(async taskFactory =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var result = await taskFactory().ConfigureAwait(false);
                    if (result != null)
                        results.Add(result);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.ToList();
        }
        
        /// <summary>
        /// タスクを並列実行（戻り値なし）
        /// </summary>
        public static async Task RunParallelAsync(
            IEnumerable<Func<Task>> taskFactories,
            int maxConcurrency = 4,
            CancellationToken cancellationToken = default)
        {
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            
            var tasks = taskFactories.Select(async taskFactory =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await taskFactory().ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        
        /// <summary>
        /// タイムアウト付きタスク実行
        /// </summary>
        public static async Task<T> WithTimeout<T>(
            Task<T> task,
            TimeSpan timeout,
            T defaultValue = default!)
        {
            using var cts = new CancellationTokenSource(timeout);
            var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cts.Token));
            
            if (completedTask == task)
            {
                cts.Cancel();
                return await task;
            }
            
            return defaultValue;
        }
        
        /// <summary>
        /// リトライ付き非同期実行
        /// </summary>
        public static async Task<T> RetryAsync<T>(
            Func<Task<T>> taskFactory,
            int maxRetries = 3,
            TimeSpan? delay = null)
        {
            var exceptions = new List<Exception>();
            
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    return await taskFactory().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    
                    if (i < maxRetries && delay.HasValue)
                    {
                        await Task.Delay(delay.Value).ConfigureAwait(false);
                    }
                }
            }
            
            throw new AggregateException(
                $"Operation failed after {maxRetries} retries", 
                exceptions);
        }
        
        /// <summary>
        /// 条件付き非同期実行
        /// </summary>
        public static async Task<T> ConditionalAsync<T>(
            bool condition,
            Func<Task<T>> trueTask,
            Func<Task<T>> falseTask)
        {
            return condition 
                ? await trueTask().ConfigureAwait(false)
                : await falseTask().ConfigureAwait(false);
        }
    }
}