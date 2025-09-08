using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    public class BackgroundTaskService : IBackgroundTaskService, IDisposable
    {
        private readonly ILogger<BackgroundTaskService> _logger;
        private readonly IPerformanceMonitor _performanceMonitor;
        private readonly ConcurrentDictionary<Guid, BackgroundTask> _tasks;
        private readonly ConcurrentQueue<BackgroundTask> _pendingTasks;
        private readonly SemaphoreSlim _taskSemaphore;
        private readonly Timer _cleanupTimer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly int _maxConcurrentTasks;
        private bool _disposed;

        public BackgroundTaskService(
            ILogger<BackgroundTaskService> logger,
            IPerformanceMonitor performanceMonitor)
        {
            _logger = logger;
            _performanceMonitor = performanceMonitor;
            _tasks = new ConcurrentDictionary<Guid, BackgroundTask>();
            _pendingTasks = new ConcurrentQueue<BackgroundTask>();
            _maxConcurrentTasks = Environment.ProcessorCount * 2;
            _taskSemaphore = new SemaphoreSlim(_maxConcurrentTasks, _maxConcurrentTasks);
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Cleanup completed tasks every 5 minutes
            _cleanupTimer = new Timer(CleanupCompletedTasks, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            // Start task processor
            Task.Run(() => ProcessTaskQueueAsync(_cancellationTokenSource.Token));
        }

        public async Task<Guid> QueueTaskAsync(string name, Func<IProgress<int>, CancellationToken, Task> taskAction)
        {
            var task = new BackgroundTask
            {
                Id = Guid.NewGuid(),
                Name = name,
                Action = taskAction,
                Status = TaskStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                Progress = 0
            };

            _tasks[task.Id] = task;
            _pendingTasks.Enqueue(task);
            
            _logger.LogInformation($"Task '{name}' queued with ID: {task.Id}");
            
            return task.Id;
        }

        public async Task<Guid> ScheduleTaskAsync(
            string name, 
            Func<IProgress<int>, CancellationToken, Task> taskAction, 
            TimeSpan delay)
        {
            var taskId = Guid.NewGuid();
            
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay, _cancellationTokenSource.Token);
                
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await QueueTaskAsync(name, taskAction);
                }
            }, _cancellationTokenSource.Token);
            
            _logger.LogInformation($"Task '{name}' scheduled to run after {delay.TotalMinutes} minutes");
            
            return taskId;
        }

        public async Task<Guid> ScheduleRecurringTaskAsync(
            string name,
            Func<IProgress<int>, CancellationToken, Task> taskAction,
            TimeSpan interval)
        {
            var taskId = Guid.NewGuid();
            
            _ = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await QueueTaskAsync(name, taskAction);
                        await Task.Delay(interval, _cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error in recurring task '{name}'");
                        await Task.Delay(TimeSpan.FromMinutes(1), _cancellationTokenSource.Token);
                    }
                }
            }, _cancellationTokenSource.Token);
            
            _logger.LogInformation($"Recurring task '{name}' scheduled with interval {interval.TotalMinutes} minutes");
            
            return taskId;
        }

        public async Task<bool> CancelTaskAsync(Guid taskId)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                if (task.Status == TaskStatus.Running)
                {
                    task.CancellationTokenSource?.Cancel();
                    task.Status = TaskStatus.Cancelled;
                    _logger.LogInformation($"Task {taskId} cancelled");
                    return true;
                }
                else if (task.Status == TaskStatus.Pending)
                {
                    task.Status = TaskStatus.Cancelled;
                    _logger.LogInformation($"Pending task {taskId} cancelled");
                    return true;
                }
            }
            
            return false;
        }

        public TaskStatus GetTaskStatus(Guid taskId)
        {
            return _tasks.TryGetValue(taskId, out var task) ? task.Status : TaskStatus.NotFound;
        }

        public int GetTaskProgress(Guid taskId)
        {
            return _tasks.TryGetValue(taskId, out var task) ? task.Progress : 0;
        }

        public IReadOnlyList<TaskInfo> GetAllTasks()
        {
            return _tasks.Values.Select(t => new TaskInfo
            {
                Id = t.Id,
                Name = t.Name,
                Status = t.Status,
                Progress = t.Progress,
                CreatedAt = t.CreatedAt,
                StartedAt = t.StartedAt,
                CompletedAt = t.CompletedAt,
                Error = t.Error
            }).ToList();
        }

        public IReadOnlyList<TaskInfo> GetActiveTasks()
        {
            return _tasks.Values
                .Where(t => t.Status == TaskStatus.Running || t.Status == TaskStatus.Pending)
                .Select(t => new TaskInfo
                {
                    Id = t.Id,
                    Name = t.Name,
                    Status = t.Status,
                    Progress = t.Progress,
                    CreatedAt = t.CreatedAt,
                    StartedAt = t.StartedAt
                })
                .ToList();
        }

        public async Task WaitForTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_tasks.TryGetValue(taskId, out var task))
                {
                    if (task.Status == TaskStatus.Completed || 
                        task.Status == TaskStatus.Failed || 
                        task.Status == TaskStatus.Cancelled)
                    {
                        return;
                    }
                }
                else
                {
                    return; // Task not found
                }
                
                await Task.Delay(100, cancellationToken);
            }
        }

        public async Task WaitForAllTasksAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_tasks.Values.Any(t => t.Status == TaskStatus.Running || t.Status == TaskStatus.Pending))
                {
                    return;
                }
                
                await Task.Delay(100, cancellationToken);
            }
        }

        private async Task ProcessTaskQueueAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_pendingTasks.TryDequeue(out var task))
                    {
                        await _taskSemaphore.WaitAsync(cancellationToken);
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ExecuteTaskAsync(task, cancellationToken);
                            }
                            finally
                            {
                                _taskSemaphore.Release();
                            }
                        }, cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in task queue processor");
                }
            }
        }

        private async Task ExecuteTaskAsync(BackgroundTask task, CancellationToken cancellationToken)
        {
            try
            {
                using (var activity = _performanceMonitor.StartActivity($"BackgroundTask.{task.Name}"))
                {
                    task.Status = TaskStatus.Running;
                    task.StartedAt = DateTime.UtcNow;
                    task.CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    
                    _logger.LogInformation($"Starting task '{task.Name}' (ID: {task.Id})");
                    
                    var progress = new Progress<int>(value =>
                    {
                        task.Progress = value;
                        _logger.LogDebug($"Task '{task.Name}' progress: {value}%");
                    });
                    
                    await task.Action(progress, task.CancellationTokenSource.Token);
                    
                    task.Status = TaskStatus.Completed;
                    task.CompletedAt = DateTime.UtcNow;
                    task.Progress = 100;
                    
                    _logger.LogInformation($"Task '{task.Name}' completed successfully (ID: {task.Id})");
                    
                    activity.SetTag("status", "completed");
                    activity.SetTag("duration_ms", (task.CompletedAt.Value - task.StartedAt.Value).TotalMilliseconds);
                }
            }
            catch (OperationCanceledException)
            {
                task.Status = TaskStatus.Cancelled;
                task.CompletedAt = DateTime.UtcNow;
                _logger.LogWarning($"Task '{task.Name}' was cancelled (ID: {task.Id})");
            }
            catch (Exception ex)
            {
                task.Status = TaskStatus.Failed;
                task.CompletedAt = DateTime.UtcNow;
                task.Error = ex.Message;
                _logger.LogError(ex, $"Task '{task.Name}' failed (ID: {task.Id})");
            }
            finally
            {
                task.CancellationTokenSource?.Dispose();
                task.CancellationTokenSource = null;
            }
        }

        private void CleanupCompletedTasks(object state)
        {
            try
            {
                var cutoffTime = DateTime.UtcNow.AddHours(-1);
                var tasksToRemove = _tasks.Values
                    .Where(t => t.CompletedAt.HasValue && 
                               t.CompletedAt.Value < cutoffTime &&
                               (t.Status == TaskStatus.Completed || 
                                t.Status == TaskStatus.Failed || 
                                t.Status == TaskStatus.Cancelled))
                    .Select(t => t.Id)
                    .ToList();
                
                foreach (var taskId in tasksToRemove)
                {
                    _tasks.TryRemove(taskId, out _);
                }
                
                if (tasksToRemove.Count > 0)
                {
                    _logger.LogDebug($"Cleaned up {tasksToRemove.Count} completed tasks");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during task cleanup");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            
            _cancellationTokenSource?.Cancel();
            _cleanupTimer?.Dispose();
            _taskSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();
            
            // Cancel all running tasks
            foreach (var task in _tasks.Values.Where(t => t.Status == TaskStatus.Running))
            {
                task.CancellationTokenSource?.Cancel();
                task.CancellationTokenSource?.Dispose();
            }
            
            _tasks.Clear();
            _disposed = true;
        }

        private class BackgroundTask
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public Func<IProgress<int>, CancellationToken, Task> Action { get; set; }
            public TaskStatus Status { get; set; }
            public int Progress { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? StartedAt { get; set; }
            public DateTime? CompletedAt { get; set; }
            public string Error { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; }
        }
    }

    public enum TaskStatus
    {
        NotFound,
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public class TaskInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public TaskStatus Status { get; set; }
        public int Progress { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string Error { get; set; }
    }
}