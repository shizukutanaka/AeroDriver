namespace AeroDriver.Core.Models
{
    /// <summary>
    /// 更新優先度
    /// </summary>
    public enum UpdatePriority
    {
        Low,
        Normal,
        High,
        Critical
    }
    
    /// <summary>
    /// 更新結果の種類
    /// </summary>
    public enum UpdateResultType
    {
        Success,
        Failed,
        Cancelled,
        Skipped,
        BackupFailed,
        RollbackRequired,
        PartialSuccess
    }

    /// <summary>
    /// レポート出力形式
    /// </summary>
    public enum ReportFormat
    {
        Text,
        Json,
        Csv,
        Html
    }
}