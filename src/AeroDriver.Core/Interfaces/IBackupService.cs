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
        /// <param name="deviceId">バックアップするデバイスID</param>
        /// <returns>バックアップが成功したかどうか</returns>
        Task<bool> CreateBackupAsync(string deviceId);
        
        /// <summary>
        /// バックアップから復元する
        /// </summary>
        /// <param name="backupPath">復元するバックアップのパス</param>
        /// <returns>復元が成功したかどうか</returns>
        Task<bool> RestoreBackupAsync(string backupPath);
        
        /// <summary>
        /// 利用可能なバックアップリストを取得する
        /// </summary>
        /// <returns>バックアップのリスト</returns>
        Task<List<BackupInfo>> GetBackupsAsync();
    }

}
