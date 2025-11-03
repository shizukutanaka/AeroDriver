using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Interfaces;

public interface IDriverRepository
{
    Task<IEnumerable<DriverInfo>> GetAllDriversAsync(CancellationToken cancellationToken = default);
    Task<DriverInfo?> GetDriverByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> UpdateDriverAsync(DriverInfo driver, CancellationToken cancellationToken = default);
    Task<bool> DeleteDriverAsync(string id, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetDriverStatisticsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<DriverInfo>> GetBackupsAsync(string driverId, CancellationToken cancellationToken = default);
    Task<bool> BackupDriverAsync(string driverId, string backupName, CancellationToken cancellationToken = default);
    Task ClearCacheAsync(CancellationToken cancellationToken = default);
}