using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// ドライバーの検出、更新、インストールを管理するインターフェース
    /// </summary>
    public interface IDriverService : IDisposable
    {
        /// <summary>
        /// ドライバー更新が検出されたときに発生するイベント
        /// </summary>
        event EventHandler<UpdatesAvailableEventArgs> UpdatesAvailable;
        
        /// <summary>
        /// ドライバーがインストールされたときに発生するイベント
        /// </summary>
        event EventHandler<UpdatesInstalledEventArgs> UpdatesInstalled;
        
        /// <summary>
        /// システム内のすべてのドライバー情報を取得します
        /// </summary>
        Task<List<DriverInfo>> GetAllDriversAsync();
        
        /// <summary>
        /// ドライバーの更新を確認します
        /// </summary>
        Task<List<DriverInfo>> CheckForUpdatesAsync();
        
        /// <summary>
        /// ドライバーをダウンロードしてインストールします
        /// </summary>
        Task<bool> InstallDriverUpdateAsync(DriverInfo driverUpdate);
        
        /// <summary>
        /// ドライバーをロールバックします
        /// </summary>
        Task<bool> RollbackDriverAsync(string deviceId);
        
        /// <summary>
        /// ドライバーを無効化します
        /// </summary>
        Task<bool> DisableDriverAsync(string deviceId);
        
        /// <summary>
        /// ドライバーを有効化します
        /// </summary>
        Task<bool> EnableDriverAsync(string deviceId);
        
        /// <summary>
        /// ドライバーの詳細情報を取得します
        /// </summary>
        Task<DriverDetailInfo> GetDriverDetailsAsync(string deviceId);
        
        /// <summary>
        /// カスタムドライバーをインストールします
        /// </summary>
        Task<bool> InstallCustomDriverAsync(string driverPath);
    }
}
