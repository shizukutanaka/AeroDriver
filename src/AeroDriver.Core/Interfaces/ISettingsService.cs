using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    /// <summary>
    /// アプリケーション設定を管理するインターフェース
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// 自動更新を有効にするかどうか
        /// </summary>
        bool AutoUpdateEnabled { get; set; }
        
        /// <summary>
        /// ベータ版のドライバーを含めるかどうか
        /// </summary>
        bool IncludeBetaDrivers { get; set; }
        
        /// <summary>
        /// バックアップを有効にするかどうか
        /// </summary>
        bool BackupEnabled { get; set; }
        
        /// <summary>
        /// バックアップの最大世代数
        /// </summary>
        int MaxBackupGenerations { get; set; }
        
        /// <summary>
        /// 詳細ログ出力を有効にするかどうか
        /// </summary>
        bool VerboseLogging { get; set; }
        
        /// <summary>
        /// キャッシュの有効期限（分）
        /// </summary>
        int CacheExpirationMinutes { get; set; }
        
        /// <summary>
        /// 自動クリーンアップを有効にするかどうか
        /// </summary>
        bool AutoCleanupEnabled { get; set; }
        
        /// <summary>
        /// ヘルスチェックの間隔（時間）
        /// </summary>
        int HealthCheckIntervalHours { get; set; }
        
        /// <summary>
        /// 優先言語（"auto"で自動検出）
        /// </summary>
        string PreferredLanguage { get; set; }
        
        /// <summary>
        /// 設定を保存する
        /// </summary>
        void Save();
        
        /// <summary>
        /// 設定をリセットする
        /// </summary>
        void ResetToDefaults();
        
        /// <summary>
        /// 設定ファイルが変更されたかチェック
        /// </summary>
        bool HasChanged();
        
        /// <summary>
        /// 設定をリロード
        /// </summary>
        void Reload();
        
        /// <summary>
        /// すべての設定を辞書として取得
        /// </summary>
        Dictionary<string, object> GetAllSettings();
    }
}
