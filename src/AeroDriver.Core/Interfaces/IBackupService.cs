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
        /// ドライバーをバックアップから復元する
        /// </summary>
        /// <param name="driver">復元するドライバー情報</param>
        /// <param name="backupVersion">復元するバックアップのバージョン（省略時は最新）</param>
        /// <returns>復元が成功したかどうか</returns>
        Task<bool> RestoreDriverAsync(DriverInfo driver, string? backupVersion = null);
        
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
}
