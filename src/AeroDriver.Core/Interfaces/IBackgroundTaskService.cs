namespace AeroDriver.Core.Interfaces
{
    /// <summary>
    /// バックグラウンドタスクサービスインターフェース
    /// </summary>
    public interface IBackgroundTaskService
    {
        /// <summary>
        /// タスクをスケジュール
        /// </summary>
        Task<string> ScheduleTaskAsync(string taskName, Func<CancellationToken, Task> taskAction, TimeSpan delay);
        
        /// <summary>
        /// 定期タスクをスケジュール
        /// </summary>
        Task<string> ScheduleRecurringTaskAsync(string taskName, Func<CancellationToken, Task> taskAction, TimeSpan interval);
        
        /// <summary>
        /// タスクをキャンセル
        /// </summary>
        Task<bool> CancelTaskAsync(string taskId);
        
        /// <summary>
        /// すべてのタスクをキャンセル
        /// </summary>
        Task CancelAllTasksAsync();
        
        /// <summary>
        /// タスクの状態を取得
        /// </summary>
        TaskStatus? GetTaskStatus(string taskId);
        
        /// <summary>
        /// 実行中のタスク一覧を取得
        /// </summary>
        Dictionary<string, TaskInfo> GetRunningTasks();
    }
    
    /// <summary>
    /// タスク情報
    /// </summary>
    public class TaskInfo
    {
        public string TaskId { get; set; } = "";
        public string TaskName { get; set; } = "";
        public TaskStatus Status { get; set; }
        public DateTime ScheduledAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsRecurring { get; set; }
        public TimeSpan? Interval { get; set; }
        public int ExecutionCount { get; set; }
        public string? LastError { get; set; }
    }
}