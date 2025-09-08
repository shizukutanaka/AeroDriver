using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    public interface ISystemHealthService
    {
        Task<HealthReport> GetHealthReportAsync();
        Task<bool> IsAdministratorAsync();
        Task<SystemInfo> GetSystemInfoAsync();
        Task<PerformanceMetrics> GetPerformanceMetricsAsync();
    }
}