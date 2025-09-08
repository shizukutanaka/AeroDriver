using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    /// <summary>
    /// 自動更新サービスインターフェース
    /// </summary>
    public interface IAutoUpdateService
    {
        /// <summary>
        /// 自動更新を開始
        /// </summary>
        Task<string> StartAutoUpdateAsync(AutoUpdateOptions options);
        
        /// <summary>
        /// 自動更新を停止
        /// </summary>
        Task StopAutoUpdateAsync();
        
        /// <summary>
        /// 更新チェックを実行
        /// </summary>
        Task<UpdateCheckResult> CheckForUpdatesAsync();
        
        /// <summary>
        /// 利用可能な更新を適用
        /// </summary>
        Task<UpdateResult> ApplyUpdatesAsync(IEnumerable<string> deviceIds, bool createBackup = true);
        
        /// <summary>
        /// 自動更新の状態を取得
        /// </summary>
        AutoUpdateStatus GetStatus();
        
        /// <summary>
        /// 更新履歴を取得
        /// </summary>
        Task<List<UpdateHistory>> GetUpdateHistoryAsync(int count = 50);
    }
}