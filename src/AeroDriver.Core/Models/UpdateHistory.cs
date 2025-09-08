namespace AeroDriver.Core.Models
{
    /// <summary>
    /// 自動更新履歴を管理するモデル
    /// </summary>
    public class UpdateHistory
    {
        public string UpdateId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string OldVersion { get; set; } = string.Empty;
        public string NewVersion { get; set; } = string.Empty;
        public DateTime UpdateTime { get; set; }
        public UpdateResultType Result { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public bool BackupCreated { get; set; }
        public string BackupPath { get; set; } = string.Empty;
    }
}