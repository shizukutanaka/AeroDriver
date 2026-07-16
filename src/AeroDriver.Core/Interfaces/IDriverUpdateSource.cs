using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    /// <summary>
    /// ドライバー更新情報の取得元を抽象化するインターフェース。
    /// 複数のデータソース（Windows Update、カタログ等）を同一インターフェースで扱う。
    /// </summary>
    public interface IDriverUpdateSource
    {
        /// <summary>このソースの識別名</summary>
        string SourceName { get; }

        /// <summary>
        /// 利用可能なドライバー更新を検索します
        /// </summary>
        Task<IReadOnlyList<DriverInfo>> SearchUpdatesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 特定のハードウェアIDに対応するドライバーを検索します
        /// </summary>
        Task<DriverInfo?> FindDriverAsync(string hardwareId, CancellationToken cancellationToken = default);
    }
}
