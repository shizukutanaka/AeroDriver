using System.Collections.Generic;
using System.Threading.Tasks;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    /// <summary>
    /// ドライバーのバックアップと復元を管理するインターフェース
    /// </summary>
    public interface IBackupService
    {
        /// <summary>
        /// ドライバーのバックアップを作成する
        /// </summary>
        /// <param name="driver">バックアップするドライバー情報</param>
        /// <returns>バックアップが成功したかどうか</returns>
        Task<bool> BackupDriverAsync(DriverInfo driver);
        
        /// <summary>
        /// システム全体のバックアップを作成する
        /// </summary>
        /// <returns>バックアップが成功したかどうか</returns>
        Task<bool> CreateBackupAsync();
        
        /// <summary>
        /// ドライバーをバックアップから復元する
        /// </summary>
        /// <param name="driver">復元するドライバー情報</param>
        /// <param name="backupVersion">復元するバックアップのバージョン（省略時は最新）</param>
        /// <returns>復元が成功したかどうか</returns>
        Task<bool> RestoreDriverAsync(DriverInfo driver, string backupVersion = null);
        
        /// <summary>
        /// バックアップから復元する
        /// </summary>
        /// <param name="backupId">復元するバックアップのID</param>
        /// <returns>復元が成功したかどうか</returns>
        Task<bool> RestoreBackupAsync(string backupId);
        
        /// <summary>
        /// 利用可能なバックアップリストを取得する
        /// </summary>
        /// <returns>バックアップのリスト</returns>
        Task<List<BackupInfo>> GetBackupsAsync();
        
        /// <summary>
        /// 古いバックアップをクリーンアップする
        /// </summary>
        /// <param name="maxGenerations">保持する最大世代数</param>
        /// <returns>タスク</returns>
        Task CleanupOldBackupsAsync(int maxGenerations);
        
        /// <summary>
        /// ドライバーのバックアップが存在するかどうかを確認する
        /// </summary>
        /// <param name="driver">確認するドライバー情報</param>
        /// <returns>バックアップが存在する場合はtrue、それ以外はfalse</returns>
        bool HasBackup(DriverInfo driver);
        
        /// <summary>
        /// 利用可能なバックアップのリストを取得する
        /// </summary>
        /// <param name="driver">確認するドライバー情報</param>
        /// <returns>バックアップのバージョンリスト</returns>
        string[] GetAvailableBackups(DriverInfo driver);
    }

    /// <summary>
    /// バックアップ情報
    /// </summary>
    public class BackupInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreationDate { get; set; }
        public long Size { get; set; }
        public string Description { get; set; } = string.Empty;
        public List<string> IncludedDrivers { get; set; } = new List<string>();
    }
}
