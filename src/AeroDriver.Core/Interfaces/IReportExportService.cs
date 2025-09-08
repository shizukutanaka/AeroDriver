using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    public interface IReportExportService
    {
        Task<string> ExportHealthReportAsync(HealthReport report, ReportFormat format = ReportFormat.Text);
        Task<string> ExportDriverListAsync(List<DriverInfo> drivers, ReportFormat format = ReportFormat.Text);
        Task<string> ExportSystemInfoAsync(SystemInfo systemInfo, ReportFormat format = ReportFormat.Text);
        Task<bool> SaveReportToFileAsync(string content, string filePath);
        Task<string> GenerateQuickReportAsync(ISystemHealthService healthService, IDriverService driverService);
    }
}