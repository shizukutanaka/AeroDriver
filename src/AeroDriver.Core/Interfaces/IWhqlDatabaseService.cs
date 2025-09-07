using System.Collections.Generic;
using System.Threading.Tasks;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    public interface IWhqlDatabaseService
    {
        Task<List<DriverInfo>> CheckForUpdatesAsync();
        Task<DriverInfo> FindAvailableUpdateAsync(DriverInfo currentDriver);
    }
}
