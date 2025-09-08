using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    public interface ICleanupService
    {
        Task<CleanupResult> CleanupOldBackupsAsync();
        Task<CleanupResult> CleanupTemporaryFilesAsync();
        Task<CleanupResult> CleanupCacheAsync();
        Task<long> GetDirectorySizeAsync(string path);
        Task<CleanupResult> PerformFullCleanupAsync();
    }
}