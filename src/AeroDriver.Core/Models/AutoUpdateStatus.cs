namespace AeroDriver.Core.Models
{
    /// <summary>
    /// 自動更新の現在の状態を表すモデル
    /// </summary>
    public class AutoUpdateStatus
    {
        public bool IsRunning { get; set; }
        public string CurrentTask { get; set; } = string.Empty;
        public int TotalDrivers { get; set; }
        public int ProcessedDrivers { get; set; }
        public int SuccessfulUpdates { get; set; }
        public int FailedUpdates { get; set; }
        public int SkippedUpdates { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan ElapsedTime => EndTime.HasValue 
            ? EndTime.Value - StartTime 
            : DateTime.UtcNow - StartTime;
        public double ProgressPercentage => TotalDrivers > 0 
            ? (double)ProcessedDrivers / TotalDrivers * 100 
            : 0;
        public List<string> CurrentErrors { get; set; } = new();
        public AutoUpdatePhase Phase { get; set; } = AutoUpdatePhase.Idle;
        public string? CurrentDeviceId { get; set; }
        public string? CurrentDeviceName { get; set; }
    }
    
    public enum AutoUpdatePhase
    {
        Idle,
        Initializing,
        Scanning,
        CreatingBackups,
        UpdatingDrivers,
        Finalizing,
        Completed,
        Error
    }
}