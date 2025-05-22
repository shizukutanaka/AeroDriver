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
        /// 設定を保存する
        /// </summary>
        void Save();
        
        /// <summary>
        /// 設定をリセットする
        /// </summary>
        void ResetToDefaults();
    }
}
