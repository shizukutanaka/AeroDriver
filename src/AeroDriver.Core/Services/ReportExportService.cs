using System.Text;
using System.Text.Json;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    public class ReportExportService : IReportExportService
    {
        public async Task<string> ExportHealthReportAsync(HealthReport report, ReportFormat format = ReportFormat.Text)
        {
            var result = format switch
            {
                ReportFormat.Json => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                ReportFormat.Csv => ConvertHealthReportToCsv(report),
                ReportFormat.Html => ConvertHealthReportToHtml(report),
                _ => ConvertHealthReportToText(report)
            };
            return await Task.FromResult(result).ConfigureAwait(false);
        }

        public async Task<string> ExportDriverListAsync(List<DriverInfo> drivers, ReportFormat format = ReportFormat.Text)
        {
            var result = format switch
            {
                ReportFormat.Json => JsonSerializer.Serialize(drivers, new JsonSerializerOptions { WriteIndented = true }),
                ReportFormat.Csv => ConvertDriverListToCsv(drivers),
                ReportFormat.Html => ConvertDriverListToHtml(drivers),
                _ => ConvertDriverListToText(drivers)
            };
            return await Task.FromResult(result).ConfigureAwait(false);
        }

        public async Task<string> ExportSystemInfoAsync(SystemInfo systemInfo, ReportFormat format = ReportFormat.Text)
        {
            var result = format switch
            {
                ReportFormat.Json => JsonSerializer.Serialize(systemInfo, new JsonSerializerOptions { WriteIndented = true }),
                ReportFormat.Csv => ConvertSystemInfoToCsv(systemInfo),
                ReportFormat.Html => ConvertSystemInfoToHtml(systemInfo),
                _ => ConvertSystemInfoToText(systemInfo)
            };
            return await Task.FromResult(result).ConfigureAwait(false);
        }

        public async Task<bool> SaveReportToFileAsync(string content, string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GenerateQuickReportAsync(ISystemHealthService healthService, IDriverService driverService)
        {
            try
            {
                var healthReport = await healthService.GetHealthReportAsync();
                var drivers = await driverService.GetDriversAsync();

                var sb = new StringBuilder();
                sb.AppendLine("AeroDriver Quick System Report");
                sb.AppendLine("=================================");
                sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                sb.AppendLine();

                // Health Summary
                sb.AppendLine("Health Summary:");
                sb.AppendLine($"  Overall Score: {healthReport.HealthScore}/100");
                sb.AppendLine($"  Total Drivers: {healthReport.TotalDrivers}");
                sb.AppendLine($"  Working: {healthReport.WorkingDrivers}");
                sb.AppendLine($"  Problematic: {healthReport.ProblematicDrivers}");
                sb.AppendLine($"  Updates Available: {healthReport.AvailableUpdates}");
                sb.AppendLine();

                // Top Issues
                if (healthReport.Recommendations.Length > 0)
                {
                    sb.AppendLine("Recommendations:");
                    foreach (var rec in healthReport.Recommendations.Take(3))
                    {
                        sb.AppendLine($"  - {rec}");
                    }
                    sb.AppendLine();
                }

                // Device Classes
                if (healthReport.DriverClasses.Any())
                {
                    sb.AppendLine("Driver Categories:");
                    foreach (var category in healthReport.DriverClasses.OrderByDescending(kvp => kvp.Value).Take(5))
                    {
                        sb.AppendLine($"  {category.Key}: {category.Value} devices");
                    }
                    sb.AppendLine();
                }

                // Recent Problematic Drivers
                var problemDrivers = drivers.Where(d => d.Status != "OK" && d.Status != "Unknown").Take(5);
                if (problemDrivers.Any())
                {
                    sb.AppendLine("Problematic Drivers:");
                    foreach (var driver in problemDrivers)
                    {
                        sb.AppendLine($"  - {driver.DeviceName}: {driver.Status}");
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error generating quick report: {ex.Message}";
            }
        }

        private static string ConvertHealthReportToText(HealthReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("System Health Report");
            sb.AppendLine("===================");
            sb.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Health Score: {report.HealthScore}/100");
            sb.AppendLine($"Generation Time: {report.GenerationTimeMs}ms");
            sb.AppendLine();

            sb.AppendLine("Driver Summary:");
            sb.AppendLine($"  Total: {report.TotalDrivers}");
            sb.AppendLine($"  Working: {report.WorkingDrivers}");
            sb.AppendLine($"  Problematic: {report.ProblematicDrivers}");
            sb.AppendLine($"  Unknown: {report.UnknownStatusDrivers}");
            sb.AppendLine($"  Updates Available: {report.AvailableUpdates}");
            sb.AppendLine();

            if (report.Recommendations.Length > 0)
            {
                sb.AppendLine("Recommendations:");
                foreach (var rec in report.Recommendations)
                {
                    sb.AppendLine($"  - {rec}");
                }
            }

            return sb.ToString();
        }

        private static string ConvertHealthReportToCsv(HealthReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Property,Value");
            sb.AppendLine($"Generated,{report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"HealthScore,{report.HealthScore}");
            sb.AppendLine($"TotalDrivers,{report.TotalDrivers}");
            sb.AppendLine($"WorkingDrivers,{report.WorkingDrivers}");
            sb.AppendLine($"ProblematicDrivers,{report.ProblematicDrivers}");
            sb.AppendLine($"AvailableUpdates,{report.AvailableUpdates}");
            sb.AppendLine($"GenerationTimeMs,{report.GenerationTimeMs}");
            return sb.ToString();
        }

        private static string ConvertHealthReportToHtml(HealthReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><title>System Health Report</title></head><body>");
            sb.AppendLine($"<h1>System Health Report</h1>");
            sb.AppendLine($"<p>Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC</p>");
            sb.AppendLine($"<h2>Health Score: {report.HealthScore}/100</h2>");
            sb.AppendLine("<table border='1'>");
            sb.AppendLine("<tr><th>Metric</th><th>Value</th></tr>");
            sb.AppendLine($"<tr><td>Total Drivers</td><td>{report.TotalDrivers}</td></tr>");
            sb.AppendLine($"<tr><td>Working</td><td>{report.WorkingDrivers}</td></tr>");
            sb.AppendLine($"<tr><td>Problematic</td><td>{report.ProblematicDrivers}</td></tr>");
            sb.AppendLine($"<tr><td>Updates Available</td><td>{report.AvailableUpdates}</td></tr>");
            sb.AppendLine("</table>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string ConvertDriverListToText(List<DriverInfo> drivers)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Driver List ({drivers.Count} drivers)");
            sb.AppendLine("===================");
            
            foreach (var driver in drivers)
            {
                sb.AppendLine($"Device: {driver.DeviceName}");
                sb.AppendLine($"  ID: {driver.DeviceID}");
                sb.AppendLine($"  Version: {driver.DriverVersion}");
                sb.AppendLine($"  Manufacturer: {driver.DriverProviderName}");
                sb.AppendLine($"  Class: {driver.DeviceClass}");
                sb.AppendLine($"  Status: {driver.Status}");
                sb.AppendLine();
            }
            
            return sb.ToString();
        }

        private static string ConvertDriverListToCsv(List<DriverInfo> drivers)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DeviceName,DeviceID,Version,Manufacturer,Class,Status,WHQL");
            
            foreach (var driver in drivers)
            {
                sb.AppendLine($"\"{driver.DeviceName}\",\"{driver.DeviceID}\",\"{driver.DriverVersion}\",\"{driver.DriverProviderName}\",\"{driver.DeviceClass}\",\"{driver.Status}\",{driver.IsWHQLCertified}");
            }
            
            return sb.ToString();
        }

        private static string ConvertDriverListToHtml(List<DriverInfo> drivers)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><title>Driver List</title></head><body>");
            sb.AppendLine($"<h1>Driver List ({drivers.Count} drivers)</h1>");
            sb.AppendLine("<table border='1'>");
            sb.AppendLine("<tr><th>Device Name</th><th>Version</th><th>Manufacturer</th><th>Class</th><th>Status</th></tr>");
            
            foreach (var driver in drivers)
            {
                sb.AppendLine($"<tr><td>{driver.DeviceName}</td><td>{driver.DriverVersion}</td><td>{driver.DriverProviderName}</td><td>{driver.DeviceClass}</td><td>{driver.Status}</td></tr>");
            }
            
            sb.AppendLine("</table>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string ConvertSystemInfoToText(SystemInfo systemInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("System Information");
            sb.AppendLine("==================");
            sb.AppendLine($"Computer: {systemInfo.ComputerName}");
            sb.AppendLine($"Manufacturer: {systemInfo.Manufacturer}");
            sb.AppendLine($"Model: {systemInfo.Model}");
            sb.AppendLine($"OS: {systemInfo.OperatingSystem}");
            sb.AppendLine($"Version: {systemInfo.Version}");
            sb.AppendLine($"Architecture: {systemInfo.Architecture}");
            sb.AppendLine($"RAM: {systemInfo.TotalRAM}");
            sb.AppendLine($"Processor: {systemInfo.Processor}");
            return sb.ToString();
        }

        private static string ConvertSystemInfoToCsv(SystemInfo systemInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Property,Value");
            sb.AppendLine($"ComputerName,\"{systemInfo.ComputerName}\"");
            sb.AppendLine($"Manufacturer,\"{systemInfo.Manufacturer}\"");
            sb.AppendLine($"Model,\"{systemInfo.Model}\"");
            sb.AppendLine($"OperatingSystem,\"{systemInfo.OperatingSystem}\"");
            sb.AppendLine($"Version,\"{systemInfo.Version}\"");
            sb.AppendLine($"Architecture,\"{systemInfo.Architecture}\"");
            sb.AppendLine($"RAM,\"{systemInfo.TotalRAM}\"");
            sb.AppendLine($"Processor,\"{systemInfo.Processor}\"");
            return sb.ToString();
        }

        private static string ConvertSystemInfoToHtml(SystemInfo systemInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><title>System Information</title></head><body>");
            sb.AppendLine("<h1>System Information</h1>");
            sb.AppendLine("<table border='1'>");
            sb.AppendLine("<tr><th>Property</th><th>Value</th></tr>");
            sb.AppendLine($"<tr><td>Computer Name</td><td>{systemInfo.ComputerName}</td></tr>");
            sb.AppendLine($"<tr><td>Manufacturer</td><td>{systemInfo.Manufacturer}</td></tr>");
            sb.AppendLine($"<tr><td>Model</td><td>{systemInfo.Model}</td></tr>");
            sb.AppendLine($"<tr><td>Operating System</td><td>{systemInfo.OperatingSystem}</td></tr>");
            sb.AppendLine($"<tr><td>Version</td><td>{systemInfo.Version}</td></tr>");
            sb.AppendLine($"<tr><td>Architecture</td><td>{systemInfo.Architecture}</td></tr>");
            sb.AppendLine($"<tr><td>RAM</td><td>{systemInfo.TotalRAM}</td></tr>");
            sb.AppendLine($"<tr><td>Processor</td><td>{systemInfo.Processor}</td></tr>");
            sb.AppendLine("</table>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }
    }
}