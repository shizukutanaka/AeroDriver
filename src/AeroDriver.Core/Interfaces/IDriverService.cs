using System.Collections.Generic;
using System.Threading.Tasks;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    public interface IDriverService
    {
        Task<List<DriverInfo>> GetDriversAsync();
        Task<List<DriverInfo>> ScanForDriversAsync();
        Task<bool> UpdateDriverAsync(string deviceId);
        Task<bool> RollbackDriverAsync(string deviceId);
        Task<bool> BackupDriverAsync(string deviceId);
    }
}