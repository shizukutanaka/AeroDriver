namespace AeroDriver.Core.Models
{
    /// <summary>
    /// 更新チェック結果
    /// </summary>
    public class UpdateCheckResult
    {
        public DateTime CheckedAt { get; set; }
        public int AvailableUpdates { get; set; }
        public List<DriverUpdateInfo> Updates { get; set; } = new();
        public TimeSpan CheckDuration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
    
    /// <summary>
    /// ドライバー更新情報
    /// </summary>
    public class DriverUpdateInfo
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string NewVersion { get; set; } = "";
        public DateTime ReleaseDate { get; set; }
        public long DownloadSize { get; set; }
        public bool IsWHQLCertified { get; set; }
        public string? ReleaseNotes { get; set; }
        public UpdatePriority Priority { get; set; }
    }
}