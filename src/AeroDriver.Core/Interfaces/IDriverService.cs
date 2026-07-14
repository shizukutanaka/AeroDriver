using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Events;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    public interface IDriverService : IDisposable
    {
        event EventHandler<UpdatesAvailableEventArgs>? UpdatesAvailable;
        event EventHandler<UpdatesInstalledEventArgs>? UpdatesInstalled;

        /// <summary>インストール済みドライバーをすべて列挙します（バッファリング版）</summary>
        Task<List<DriverInfo>> GetAllDriversAsync(
            IProgress<DriverScanProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>インストール済みドライバーをストリーミングで列挙します（消費者がペースを制御）</summary>
        IAsyncEnumerable<DriverInfo> StreamAllDriversAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default);

        /// <summary>全データソースに更新を問い合わせます</summary>
        Task<List<DriverInfo>> CheckForUpdatesAsync(
            IProgress<DriverScanProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// ドライバーをインストールします。成功/失敗のみが必要な場合の簡易版。
        /// 失敗理由を区別したい場合は <see cref="InstallDriverUpdateWithResultAsync"/> を使用してください。
        /// </summary>
        Task<bool> InstallDriverUpdateAsync(DriverInfo driverUpdate, CancellationToken cancellationToken = default);

        /// <summary>
        /// ドライバーをインストールし、失敗理由を区別できる詳細結果を返します。
        /// 管理者権限不足は例外ではなく <see cref="DriverInstallResult.AdminRequired"/> として返されます
        /// （他の Enable/Disable/Rollback 系メソッドは例外ベースの <c>ElevationGuard.ThrowIfNotElevated</c> のままです）。
        /// </summary>
        Task<DriverInstallResult> InstallDriverUpdateWithResultAsync(DriverInfo driverUpdate, CancellationToken cancellationToken = default);

        Task<bool> RollbackDriverAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// デバイスを無効化します。ストレージコントローラーやシステムデバイスなど
        /// ブートクリティカルなクラスは誤操作防止のため既定で拒否されます。
        /// 意図的に無効化したい場合は <paramref name="force"/> を true にしてください。
        /// </summary>
        Task<bool> DisableDriverAsync(string deviceId, bool force = false, CancellationToken cancellationToken = default);
        Task<bool> EnableDriverAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<DriverDetailInfo?> GetDriverDetailsAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<bool> InstallCustomDriverAsync(string driverPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 2つのバージョン文字列を比較します（<see cref="AeroDriver.Core.Helpers.VersionHelper.Compare"/> への委譲）。
        /// 返り値: 正 = version1 が新しい, 0 = 同じ, 負 = version1 が古い
        /// </summary>
        int CompareVersions(string version1, string version2);
    }
}
