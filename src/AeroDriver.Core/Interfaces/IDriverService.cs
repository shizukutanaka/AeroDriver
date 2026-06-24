using System;
using System.Collections.Generic;
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

        /// <summary>インストール済みドライバーをすべて列挙します</summary>
        Task<List<DriverInfo>> GetAllDriversAsync(
            IProgress<DriverScanProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>全データソースに更新を問い合わせます</summary>
        Task<List<DriverInfo>> CheckForUpdatesAsync(
            IProgress<DriverScanProgress>? progress = null,
            CancellationToken cancellationToken = default);

        Task<bool> InstallDriverUpdateAsync(DriverInfo driverUpdate, CancellationToken cancellationToken = default);
        Task<bool> RollbackDriverAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<bool> DisableDriverAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<bool> EnableDriverAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<DriverDetailInfo?> GetDriverDetailsAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<bool> InstallCustomDriverAsync(string driverPath, CancellationToken cancellationToken = default);
    }
}
