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
        event EventHandler<UpdatesAvailableEventArgs> UpdatesAvailable;
        event EventHandler<UpdatesInstalledEventArgs> UpdatesInstalled;

        Task<List<DriverInfo>> GetAllDriversAsync(CancellationToken cancellationToken = default);
        Task<List<DriverInfo>> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
        Task<bool> InstallDriverUpdateAsync(DriverInfo driverUpdate, CancellationToken cancellationToken = default);
        Task<bool> RollbackDriverAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<bool> DisableDriverAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<bool> EnableDriverAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<DriverDetailInfo> GetDriverDetailsAsync(string deviceId, CancellationToken cancellationToken = default);
        Task<bool> InstallCustomDriverAsync(string driverPath, CancellationToken cancellationToken = default);
    }
}
