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
        /// ドライバーインストール前にWindowsのシステム復元ポイントを作成するかどうか。
        /// 作成できない環境(Server SKU・システムの保護が無効・直近24時間以内に作成済み)では
        /// 警告ログを出してインストールは継続します。
        /// </summary>
        bool CreateRestorePoint { get; set; }
        
        /// <summary>
        /// GUI で選択されたテーマ名(<c>AppTheme</c> の名前)。null は未設定。
        /// </summary>
        string? ThemeName { get; set; }

        /// <summary>
        /// GUI で選択されたカルチャ名(例: "ja-JP")。null は未設定で、
        /// この場合は OS の UI カルチャに従います。
        /// </summary>
        string? CultureName { get; set; }

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
