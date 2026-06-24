using System.Threading.Tasks;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    public interface IWhqlDatabaseService
    {
        Task<DriverInfo?> FindDriverByHardwareIdAsync(string hardwareId);
        Task<string?> GetVendorIdByNameAsync(string vendorName);
        Task<bool> UpdateDriverDatabaseAsync();
    }
}
