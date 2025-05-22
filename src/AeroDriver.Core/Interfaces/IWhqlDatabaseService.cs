using System.Threading.Tasks;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// Windows Update Catalogと連携してWHQL認証ドライバーを検索・ダウンロードするインターフェース
    /// </summary>
    public interface IWhqlDatabaseService
    {
        /// <summary>
        /// ハードウェアIDに基づいてドライバーを検索します
        /// </summary>
        Task<DriverInfo> FindDriverByHardwareIdAsync(string hardwareId);
        
        /// <summary>
        /// 製造元名からベンダーIDを取得します
        /// </summary>
        Task<string> GetVendorIdByNameAsync(string vendorName);
        
        /// <summary>
        /// WHQL認証ドライバーデータベースを更新します
        /// </summary>
        Task<bool> UpdateDriverDatabaseAsync();
    }
}
