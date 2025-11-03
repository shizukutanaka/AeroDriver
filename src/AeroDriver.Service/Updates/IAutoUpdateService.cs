using System.Threading.Tasks;

namespace AeroDriver.Service;

public interface IAutoUpdateService
{
    void StartAutoUpdateCheck(int intervalHours);
    Task<AutoUpdateResult> CheckForUpdatesAsync();
    void StopAutoUpdateCheck();
}

public record AutoUpdateResult(bool Success, string Message);
