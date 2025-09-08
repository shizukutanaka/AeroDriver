namespace AeroDriver.Core.Models
{
    /// <summary>
    /// 自動更新のオプション設定
    /// </summary>
    public class AutoUpdateOptions
    {
        /// <summary>
        /// 強制更新フラグ
        /// </summary>
        public bool ForceUpdate { get; set; }
        
        /// <summary>
        /// バックアップ作成をスキップするかどうか
        /// </summary>
        public bool SkipBackup { get; set; }
        
        /// <summary>
        /// WHQL認証済みドライバーのみを使用するかどうか
        /// </summary>
        public bool OnlyWhqlCertified { get; set; } = true;
        
        /// <summary>
        /// ベータドライバーを含めるかどうか
        /// </summary>
        public bool IncludeBetaDrivers { get; set; }
        
        /// <summary>
        /// 除外するデバイスID
        /// </summary>
        public string[] ExcludedDeviceIds { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// 更新の優先度
        /// </summary>
        public UpdatePriority Priority { get; set; } = UpdatePriority.Normal;
        
        /// <summary>
        /// タイムアウト設定（秒）
        /// </summary>
        public int TimeoutSeconds { get; set; } = 300;
        
        /// <summary>
        /// 並列実行数の最大値
        /// </summary>
        public int MaxParallelOperations { get; set; } = 2;
        
        /// <summary>
        /// エラー時に継続するかどうか
        /// </summary>
        public bool ContinueOnError { get; set; } = true;
        
        /// <summary>
        /// 更新の繰り返し間隔（時間）
        /// </summary>
        public int IntervalHours { get; set; } = 24;
        
        /// <summary>
        /// 詳細ログを出力するかどうか
        /// </summary>
        public bool VerboseLogging { get; set; }
        
        /// <summary>
        /// 通知を表示するかどうか
        /// </summary>
        public bool ShowNotifications { get; set; } = true;
        
        /// <summary>
        /// 再起動が必要な場合に自動で再起動するか
        /// </summary>
        public bool AutoRestart { get; set; }
    }
    
}