namespace AeroDriver.Core.Models
{
    /// <summary>
    /// 更新結果
    /// </summary>
    public class UpdateResult
    {
        public DateTime UpdatedAt { get; set; }
        public int TotalAttempted { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public List<UpdateItemResult> Items { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }
    
    /// <summary>
    /// 個別更新結果
    /// </summary>
    public class UpdateItemResult
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? BackupPath { get; set; }
    }
}