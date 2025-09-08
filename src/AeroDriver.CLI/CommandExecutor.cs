using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Core.Services;
using AeroDriver.Core.Helpers;

namespace AeroDriver.CLI
{
    /// <summary>
    /// コマンド実行クラス
    /// </summary>
    public class CommandExecutor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly CommandLineOptions _options;
        private readonly ILogger<CommandExecutor>? _logger;
        private readonly IPerformanceMonitor? _performanceMonitor;
        
        public CommandExecutor(IServiceProvider serviceProvider, CommandLineOptions options)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = _serviceProvider.GetService<ILogger<CommandExecutor>>();
            _performanceMonitor = _serviceProvider.GetService<IPerformanceMonitor>();
        }
        
        public async Task ListDriversAsync()
        {
            using var scope = _performanceMonitor?.StartOperation("ListDrivers");
            var driverService = _serviceProvider.GetRequiredService<IDriverService>();
            
            if (!_options.Silent)
                Console.WriteLine("Getting driver list...");
            
            var drivers = await driverService.GetDriversAsync();
            
            if (_options.OutputFormat == "json")
            {
                var json = System.Text.Json.JsonSerializer.Serialize(drivers, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                if (!string.IsNullOrEmpty(_options.OutputPath))
                {
                    await System.IO.File.WriteAllTextAsync(_options.OutputPath, json);
                    Console.WriteLine($"Output saved to: {_options.OutputPath}");
                }
                else
                {
                    Console.WriteLine(json);
                }
            }
            else
            {
                Console.WriteLine($"\nFound {drivers.Count} drivers:");
                Console.WriteLine(new string('-', 80));
                Console.WriteLine($"{"Device Name",-40} {"Version",-15} {"Status",-10}");
                Console.WriteLine(new string('-', 80));
                
                var displayCount = _options.Verbose ? drivers.Count : Math.Min(20, drivers.Count);
                foreach (var driver in drivers.Take(displayCount))
                {
                    var name = driver.DeviceName?.Length > 37 
                        ? driver.DeviceName.Substring(0, 37) + "..." 
                        : driver.DeviceName ?? "Unknown";
                    Console.WriteLine($"{name,-40} {driver.DriverVersion,-15} {driver.Status,-10}");
                }
                
                if (!_options.Verbose && drivers.Count > 20)
                {
                    Console.WriteLine($"... and {drivers.Count - 20} more drivers (use --verbose to see all)");
                }
            }
        }
        
        public async Task AutoModeAsync()
        {
            using var scope = _performanceMonitor?.StartOperation("AutoMode");
            var driverService = _serviceProvider.GetRequiredService<IDriverService>();
            var backupService = _serviceProvider.GetRequiredService<IBackupService>();
            var autoUpdateService = _serviceProvider.GetService<IAutoUpdateService>();
            
            Console.WriteLine("Running auto mode - comprehensive driver check and maintenance...");
            Console.WriteLine();
            
            // Step 1: Scan for updates
            Console.WriteLine("1. Scanning for driver updates...");
            var updates = await driverService.ScanForDriversAsync();
            
            if (updates.Any())
            {
                Console.WriteLine($"   Found {updates.Count} available updates");
                
                if (!_options.NoBackup)
                {
                    // Step 2: Create backups before updating
                    Console.WriteLine("2. Creating backups before updating...");
                    var updateLimit = _options.Force ? updates.Count : Math.Min(3, updates.Count);
                    
                    foreach (var update in updates.Take(updateLimit))
                    {
                        await backupService.CreateBackupAsync(update.DeviceID);
                        Console.WriteLine($"   Backup created for {update.DeviceName}");
                    }
                }
                
                // Step 3: Apply updates if force flag is set
                if (_options.Force)
                {
                    Console.WriteLine("3. Applying updates...");
                    foreach (var update in updates)
                    {
                        var success = await driverService.UpdateDriverAsync(update.DeviceID);
                        Console.WriteLine($"   {update.DeviceName}: {(success ? "Updated" : "Failed")}");
                    }
                }
                else
                {
                    Console.WriteLine("3. Updates available but not applied (use --force to apply)");
                }
            }
            else
            {
                Console.WriteLine("   No updates found - system is up to date");
            }
            
            // Step 4: Start auto-update if available
            if (autoUpdateService != null && !_options.Silent)
            {
                Console.WriteLine("\n4. Auto-update service status:");
                var status = autoUpdateService.GetStatus();
                Console.WriteLine($"   Running: {status.IsRunning}");
                if (status.LastCheckAt.HasValue)
                {
                    Console.WriteLine($"   Last check: {status.LastCheckAt.Value:yyyy-MM-dd HH:mm:ss}");
                }
            }
        }
        
        public async Task ScanForUpdatesAsync()
        {
            using var scope = _performanceMonitor?.StartOperation("ScanForUpdates");
            var driverService = _serviceProvider.GetRequiredService<IDriverService>();
            
            Console.WriteLine("Scanning for driver updates...");
            var updates = await driverService.ScanForDriversAsync();
            
            if (updates.Any())
            {
                Console.WriteLine($"\nFound {updates.Count} available updates:");
                foreach (var update in updates)
                {
                    Console.WriteLine($"- {update.DeviceName} (Current: {update.DriverVersion})");
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"  Device ID: {update.DeviceID}");
                        Console.WriteLine($"  Class: {update.DeviceClass}");
                        Console.WriteLine($"  WHQL: {(update.IsWHQLCertified ? "Yes" : "No")}");
                    }
                }
            }
            else
            {
                Console.WriteLine("No driver updates available.");
            }
        }
        
        public async Task UpdateDriverAsync(string deviceId)
        {
            using var scope = _performanceMonitor?.StartOperation("UpdateDriver");
            var driverService = _serviceProvider.GetRequiredService<IDriverService>();
            var backupService = _serviceProvider.GetRequiredService<IBackupService>();
            
            Console.WriteLine($"Updating driver: {deviceId}");
            
            // Create backup unless --no-backup is specified
            if (!_options.NoBackup)
            {
                Console.WriteLine("Creating backup...");
                var backupSuccess = await backupService.CreateBackupAsync(deviceId);
                if (!backupSuccess)
                {
                    if (!_options.Force)
                    {
                        Console.WriteLine("Backup failed. Use --force to continue without backup.");
                        return;
                    }
                    Console.WriteLine("Warning: Continuing without backup (--force specified)");
                }
            }
            
            var result = await driverService.UpdateDriverAsync(deviceId);
            
            if (result)
            {
                Console.WriteLine("Driver updated successfully.");
            }
            else
            {
                Console.WriteLine("Failed to update driver.");
            }
        }
        
        public async Task BackupDriverAsync(string deviceId)
        {
            using var scope = _performanceMonitor?.StartOperation("BackupDriver");
            var backupService = _serviceProvider.GetRequiredService<IBackupService>();
            
            Console.WriteLine($"Backing up driver: {deviceId}");
            var result = await backupService.CreateBackupAsync(deviceId);
            
            if (result)
            {
                Console.WriteLine("Driver backup created successfully.");
                
                if (_options.Verbose)
                {
                    var backups = await backupService.GetBackupsAsync(deviceId);
                    var latest = backups.FirstOrDefault();
                    if (latest != null)
                    {
                        Console.WriteLine($"Backup path: {latest.BackupPath}");
                        Console.WriteLine($"Backup date: {latest.BackupDate:yyyy-MM-dd HH:mm:ss}");
                    }
                }
            }
            else
            {
                Console.WriteLine("Failed to create driver backup.");
            }
        }
        
        public async Task RollbackDriverAsync(string deviceId)
        {
            using var scope = _performanceMonitor?.StartOperation("RollbackDriver");
            var driverService = _serviceProvider.GetRequiredService<IDriverService>();
            
            Console.WriteLine($"Rolling back driver: {deviceId}");
            var result = await driverService.RollbackDriverAsync(deviceId);
            
            if (result)
            {
                Console.WriteLine("Driver rollback completed successfully.");
            }
            else
            {
                Console.WriteLine("Failed to rollback driver.");
            }
        }
        
        public async Task FixDriverIssuesAsync()
        {
            using var scope = _performanceMonitor?.StartOperation("FixDriverIssues");
            var driverService = _serviceProvider.GetRequiredService<IDriverService>();
            
            Console.WriteLine("Analyzing driver issues...");
            var drivers = await driverService.GetDriversAsync();
            
            var problematicDrivers = drivers.Where(d => 
                d.Status != "OK" && 
                d.Status != "Unknown").ToList();
            
            if (problematicDrivers.Any())
            {
                Console.WriteLine($"Found {problematicDrivers.Count} drivers with issues:");
                foreach (var driver in problematicDrivers)
                {
                    Console.WriteLine($"- {driver.DeviceName}: {driver.Status}");
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"  Device ID: {driver.DeviceID}");
                        Console.WriteLine($"  Version: {driver.DriverVersion}");
                    }
                }
                
                if (_options.Force)
                {
                    Console.WriteLine("\nAttempting to fix issues...");
                    foreach (var driver in problematicDrivers)
                    {
                        var success = await driverService.UpdateDriverAsync(driver.DeviceID);
                        Console.WriteLine($"  {driver.DeviceName}: {(success ? "Fixed" : "Failed")}");
                    }
                }
                else
                {
                    Console.WriteLine("\nUse --force to attempt automatic fixes");
                    Console.WriteLine("Or run 'update <deviceId>' for individual drivers");
                }
            }
            else
            {
                Console.WriteLine("No driver issues detected.");
            }
        }
        
        public async Task RunDiagnosticsAsync()
        {
            using var scope = _performanceMonitor?.StartOperation("RunDiagnostics");
            var driverService = _serviceProvider.GetRequiredService<IDriverService>();
            var healthService = _serviceProvider.GetRequiredService<ISystemHealthService>();
            
            Console.WriteLine("Running system diagnostics...");
            Console.WriteLine();
            
            var drivers = await driverService.GetDriversAsync();
            var healthReport = await healthService.GetHealthReportAsync();
            
            Console.WriteLine("Driver Statistics:");
            Console.WriteLine($"- Total drivers: {drivers.Count}");
            Console.WriteLine($"- Working drivers: {drivers.Count(d => d.Status == "OK")}");
            Console.WriteLine($"- Unknown status: {drivers.Count(d => d.Status == "Unknown")}");
            Console.WriteLine($"- Problem drivers: {drivers.Count(d => d.Status != "OK" && d.Status != "Unknown")}");
            
            Console.WriteLine("\nSystem Health:");
            Console.WriteLine($"- Health Score: {healthReport.HealthScore}/100");
            Console.WriteLine($"- Administrator: {(healthReport.IsAdministrator ? "Yes" : "No")}");
            
            Console.WriteLine("\nTop Device Classes:");
            var topClasses = drivers.GroupBy(d => d.DeviceClass)
                .OrderByDescending(g => g.Count())
                .Take(5);
            
            foreach (var group in topClasses)
            {
                Console.WriteLine($"- {group.Key}: {group.Count()} devices");
            }
            
            if (_performanceMonitor != null)
            {
                Console.WriteLine("\nPerformance Metrics:");
                var perfSummary = _performanceMonitor.GetSummary();
                Console.WriteLine($"- Total operations: {perfSummary.TotalOperations}");
                Console.WriteLine($"- Success rate: {(perfSummary.TotalOperations > 0 ? (double)perfSummary.TotalSuccessful / perfSummary.TotalOperations * 100 : 0):F1}%");
                Console.WriteLine($"- Average response: {perfSummary.AverageResponseTimeMs:F0}ms");
            }
            
            Console.WriteLine("\nDiagnostics completed.");
        }
        
        public async Task ShowSystemInfoAsync()
        {
            using var scope = _performanceMonitor?.StartOperation("ShowSystemInfo");
            var healthService = _serviceProvider.GetRequiredService<ISystemHealthService>();
            
            Console.WriteLine("System Information:");
            Console.WriteLine("==================");
            
            var systemInfo = await healthService.GetSystemInfoAsync();
            
            Console.WriteLine($"Computer: {systemInfo.ComputerName}");
            Console.WriteLine($"Manufacturer: {systemInfo.Manufacturer}");
            Console.WriteLine($"Model: {systemInfo.Model}");
            Console.WriteLine($"OS: {systemInfo.OperatingSystem}");
            Console.WriteLine($"Version: {systemInfo.Version}");
            Console.WriteLine($"Architecture: {systemInfo.Architecture}");
            Console.WriteLine($"Build: {systemInfo.BuildNumber}");
            Console.WriteLine($"RAM: {systemInfo.TotalRAM}");
            Console.WriteLine($"Processor: {systemInfo.Processor}");
            Console.WriteLine($"Cores: {systemInfo.Cores}");
            Console.WriteLine($"Threads: {systemInfo.Threads}");
            
            if (_options.Verbose)
            {
                var perfMetrics = await healthService.GetPerformanceMetricsAsync();
                Console.WriteLine("\nPerformance Metrics:");
                Console.WriteLine($"Working Set: {perfMetrics.WorkingSet / (1024 * 1024)} MB");
                Console.WriteLine($"Private Memory: {perfMetrics.PrivateMemorySize / (1024 * 1024)} MB");
                Console.WriteLine($"Thread Count: {perfMetrics.ThreadCount}");
                Console.WriteLine($"Handle Count: {perfMetrics.HandleCount}");
            }
        }
        
        public async Task ShowHealthReportAsync()
        {
            using var scope = _performanceMonitor?.StartOperation("ShowHealthReport");
            var healthService = _serviceProvider.GetRequiredService<ISystemHealthService>();
            var reportService = _serviceProvider.GetRequiredService<IReportExportService>();
            
            Console.WriteLine("Generating system health report...");
            var report = await healthService.GetHealthReportAsync();
            
            if (!string.IsNullOrEmpty(_options.OutputFormat))
            {
                var format = _options.OutputFormat.ToLowerInvariant() switch
                {
                    "json" => ReportFormat.Json,
                    "csv" => ReportFormat.Csv,
                    "html" => ReportFormat.Html,
                    _ => ReportFormat.Text
                };
                
                var output = await reportService.ExportHealthReportAsync(report, format);
                
                if (!string.IsNullOrEmpty(_options.OutputPath))
                {
                    await reportService.SaveReportToFileAsync(output, _options.OutputPath);
                    Console.WriteLine($"Report saved to: {_options.OutputPath}");
                }
                else
                {
                    Console.WriteLine(output);
                }
            }
            else
            {
                Console.WriteLine($"\nSystem Health Report - Generated at {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
                Console.WriteLine("========================================");
                Console.WriteLine($"Overall Health Score: {report.HealthScore}/100");
                
                if (report.SystemInfo != null)
                {
                    Console.WriteLine($"Computer: {report.SystemInfo.ComputerName}");
                    Console.WriteLine($"OS: {report.SystemInfo.OperatingSystem} {report.SystemInfo.Architecture}");
                }
                
                Console.WriteLine($"\nDriver Summary:");
                Console.WriteLine($"  Total Drivers: {report.TotalDrivers}");
                Console.WriteLine($"  Working: {report.WorkingDrivers}");
                Console.WriteLine($"  Problematic: {report.ProblematicDrivers}");
                Console.WriteLine($"  Available Updates: {report.AvailableUpdates}");
                
                if (report.Recommendations.Length > 0)
                {
                    Console.WriteLine("\nRecommendations:");
                    foreach (var recommendation in report.Recommendations)
                    {
                        Console.WriteLine($"  - {recommendation}");
                    }
                }
                
                Console.WriteLine($"\nReport generated in {report.GenerationTimeMs}ms");
            }
        }
        
        public async Task RunCleanupAsync(string type)
        {
            using var scope = _performanceMonitor?.StartOperation("RunCleanup");
            var cleanupService = _serviceProvider.GetRequiredService<ICleanupService>();
            
            CleanupResult result;
            
            switch (type.ToLowerInvariant())
            {
                case "backups":
                case "backup":
                    Console.WriteLine("Cleaning up old backups...");
                    result = await cleanupService.CleanupOldBackupsAsync();
                    break;
                case "temp":
                case "temporary":
                    Console.WriteLine("Cleaning up temporary files...");
                    result = await cleanupService.CleanupTemporaryFilesAsync();
                    break;
                case "cache":
                    Console.WriteLine("Cleaning up cache...");
                    result = await cleanupService.CleanupCacheAsync();
                    break;
                case "all":
                default:
                    Console.WriteLine("Performing full cleanup...");
                    result = await cleanupService.PerformFullCleanupAsync();
                    break;
            }
            
            Console.WriteLine($"\n{result.OperationType} Results:");
            Console.WriteLine($"  Files Deleted: {result.FilesDeleted}");
            Console.WriteLine($"  Space Freed: {result.GetFormattedSize()}");
            Console.WriteLine($"  Success: {(result.Success ? "Yes" : "No")}");
            
            if (result.Errors.Any())
            {
                Console.WriteLine("  Errors:");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"    - {error}");
                }
            }
        }
        
        public async Task ManageCacheAsync(string action)
        {
            using var scope = _performanceMonitor?.StartOperation("ManageCache");
            var cacheService = _serviceProvider.GetRequiredService<ICacheService>();
            
            switch (action.ToLowerInvariant())
            {
                case "clear":
                    cacheService.Clear();
                    Console.WriteLine("Cache cleared successfully");
                    break;
                case "cleanup":
                    cacheService.ClearExpired();
                    Console.WriteLine("Expired cache entries cleaned up");
                    break;
                case "info":
                    // Cache統計を表示（将来的に実装）
                    Console.WriteLine("Cache information:");
                    Console.WriteLine("  Implementation pending");
                    break;
                default:
                    Console.WriteLine("Available cache actions: clear, cleanup, info");
                    break;
            }
            
            await Task.CompletedTask;
        }
        
        public async Task GenerateReportAsync(string type)
        {
            using var scope = _performanceMonitor?.StartOperation("GenerateReport");
            var reportService = _serviceProvider.GetRequiredService<IReportExportService>();
            var healthService = _serviceProvider.GetRequiredService<ISystemHealthService>();
            var driverService = _serviceProvider.GetRequiredService<IDriverService>();
            
            string report;
            
            switch (type.ToLowerInvariant())
            {
                case "quick":
                    Console.WriteLine("Generating quick report...");
                    report = await reportService.GenerateQuickReportAsync(healthService, driverService);
                    break;
                    
                case "full":
                case "health":
                    Console.WriteLine("Generating full health report...");
                    var healthReport = await healthService.GetHealthReportAsync();
                    var format = _options.OutputFormat?.ToLowerInvariant() switch
                    {
                        "json" => ReportFormat.Json,
                        "csv" => ReportFormat.Csv,
                        "html" => ReportFormat.Html,
                        _ => ReportFormat.Text
                    };
                    report = await reportService.ExportHealthReportAsync(healthReport, format);
                    break;
                    
                case "drivers":
                    Console.WriteLine("Generating driver report...");
                    var drivers = await driverService.GetDriversAsync();
                    var driverFormat = _options.OutputFormat?.ToLowerInvariant() switch
                    {
                        "json" => ReportFormat.Json,
                        "csv" => ReportFormat.Csv,
                        "html" => ReportFormat.Html,
                        _ => ReportFormat.Text
                    };
                    report = await reportService.ExportDriverListAsync(drivers, driverFormat);
                    break;
                    
                case "system":
                    Console.WriteLine("Generating system report...");
                    var systemInfo = await healthService.GetSystemInfoAsync();
                    var sysFormat = _options.OutputFormat?.ToLowerInvariant() switch
                    {
                        "json" => ReportFormat.Json,
                        "csv" => ReportFormat.Csv,
                        "html" => ReportFormat.Html,
                        _ => ReportFormat.Text
                    };
                    report = await reportService.ExportSystemInfoAsync(systemInfo, sysFormat);
                    break;
                    
                default:
                    Console.WriteLine("Unknown report type. Available: quick, full, drivers, system");
                    return;
            }
            
            if (!string.IsNullOrEmpty(_options.OutputPath))
            {
                await reportService.SaveReportToFileAsync(report, _options.OutputPath);
                Console.WriteLine($"Report saved to: {_options.OutputPath}");
            }
            else
            {
                Console.WriteLine(report);
            }
        }
        
        public async Task ViewLogsAsync(string filter)
        {
            using var scope = _performanceMonitor?.StartOperation("ViewLogs");
            var loggerService = _serviceProvider.GetService<ISimpleLogger>();
            
            if (loggerService == null)
            {
                Console.WriteLine("Logger service not available");
                return;
            }
            
            string[] logs;
            
            switch (filter.ToLowerInvariant())
            {
                case "recent":
                    logs = await loggerService.GetRecentLogsAsync(50);
                    break;
                case "today":
                    logs = await loggerService.GetRecentLogsAsync(200);
                    var today = DateTime.Today;
                    logs = logs.Where(l => l.Contains(today.ToString("yyyy-MM-dd"))).ToArray();
                    break;
                case "errors":
                    logs = await loggerService.GetRecentLogsAsync(100);
                    logs = logs.Where(l => l.Contains("[ERROR]") || l.Contains("[WARN]")).ToArray();
                    break;
                case "all":
                    logs = await loggerService.GetRecentLogsAsync(int.MaxValue);
                    break;
                default:
                    Console.WriteLine("Unknown filter. Available: recent, today, errors, all");
                    return;
            }
            
            Console.WriteLine($"Showing {logs.Length} log entries:");
            Console.WriteLine(new string('-', 80));
            
            foreach (var log in logs)
            {
                Console.WriteLine(log);
            }
            
            if (logs.Length == 0)
            {
                Console.WriteLine("No log entries found matching the filter");
            }
        }
        
        public async Task ShowSettingsAsync()
        {
            using var scope = _performanceMonitor?.StartOperation("ShowSettings");
            var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            
            Console.WriteLine("Current Settings:");
            Console.WriteLine("================");
            Console.WriteLine($"Auto Update Enabled: {settingsService.AutoUpdateEnabled}");
            Console.WriteLine($"Include Beta Drivers: {settingsService.IncludeBetaDrivers}");
            Console.WriteLine($"Backup Enabled: {settingsService.BackupEnabled}");
            Console.WriteLine($"Max Backup Generations: {settingsService.MaxBackupGenerations}");
            Console.WriteLine($"Verbose Logging: {settingsService.VerboseLogging}");
            Console.WriteLine($"Cache Expiration: {settingsService.CacheExpirationMinutes} minutes");
            Console.WriteLine($"Auto Cleanup: {settingsService.AutoCleanupEnabled}");
            Console.WriteLine($"Health Check Interval: {settingsService.HealthCheckIntervalHours} hours");
            Console.WriteLine($"Language: {settingsService.PreferredLanguage}");
            
            if (_options.Verbose)
            {
                Console.WriteLine("\nAll Settings:");
                var allSettings = settingsService.GetAllSettings();
                foreach (var setting in allSettings)
                {
                    Console.WriteLine($"  {setting.Key}: {setting.Value}");
                }
            }
            
            await Task.CompletedTask;
        }
        
        public async Task ManageAutoUpdateAsync(string action)
        {
            using var scope = _performanceMonitor?.StartOperation("ManageAutoUpdate");
            var autoUpdateService = _serviceProvider.GetService<IAutoUpdateService>();
            
            if (autoUpdateService == null)
            {
                Console.WriteLine("Auto-update service not available");
                return;
            }
            
            switch (action.ToLowerInvariant())
            {
                case "start":
                    Console.WriteLine("Starting auto-update service...");
                    var options = new AutoUpdateOptions
                    {
                        CheckInterval = TimeSpan.FromHours(24),
                        AutoApply = _options.Force,
                        CreateBackup = !_options.NoBackup,
                        OnlyWHQLCertified = true
                    };
                    var taskId = await autoUpdateService.StartAutoUpdateAsync(options);
                    Console.WriteLine($"Auto-update started with task ID: {taskId}");
                    break;
                    
                case "stop":
                    Console.WriteLine("Stopping auto-update service...");
                    await autoUpdateService.StopAutoUpdateAsync();
                    Console.WriteLine("Auto-update stopped");
                    break;
                    
                case "status":
                    var status = autoUpdateService.GetStatus();
                    Console.WriteLine("Auto-Update Status:");
                    Console.WriteLine($"  Running: {status.IsRunning}");
                    if (status.StartedAt.HasValue)
                        Console.WriteLine($"  Started: {status.StartedAt.Value:yyyy-MM-dd HH:mm:ss}");
                    if (status.LastCheckAt.HasValue)
                        Console.WriteLine($"  Last Check: {status.LastCheckAt.Value:yyyy-MM-dd HH:mm:ss}");
                    if (status.NextCheckAt.HasValue)
                        Console.WriteLine($"  Next Check: {status.NextCheckAt.Value:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"  Updates Applied: {status.UpdatesApplied}");
                    Console.WriteLine($"  Updates Pending: {status.UpdatesPending}");
                    Console.WriteLine($"  Errors: {status.Errors}");
                    break;
                    
                case "history":
                    var history = await autoUpdateService.GetUpdateHistoryAsync(20);
                    Console.WriteLine($"Update History (last {history.Count} entries):");
                    foreach (var item in history)
                    {
                        Console.WriteLine($"  [{item.Timestamp:yyyy-MM-dd HH:mm}] {item.DeviceName}: {(item.Success ? "Success" : "Failed")}");
                        if (_options.Verbose && !string.IsNullOrEmpty(item.ErrorMessage))
                        {
                            Console.WriteLine($"    Error: {item.ErrorMessage}");
                        }
                    }
                    break;
                    
                default:
                    Console.WriteLine("Available actions: start, stop, status, history");
                    break;
            }
        }
        
        public async Task ShowPerformanceMonitorAsync()
        {
            using var scope = _performanceMonitor?.StartOperation("ShowPerformanceMonitor");
            
            if (_performanceMonitor == null)
            {
                Console.WriteLine("Performance monitor not available");
                return;
            }
            
            var summary = _performanceMonitor.GetSummary();
            
            Console.WriteLine("Performance Monitor Summary");
            Console.WriteLine("==========================");
            Console.WriteLine($"Generated: {summary.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();
            Console.WriteLine($"Total Operations: {summary.TotalOperations}");
            Console.WriteLine($"Successful: {summary.TotalSuccessful}");
            Console.WriteLine($"Failed: {summary.TotalFailed}");
            Console.WriteLine($"Success Rate: {(summary.TotalOperations > 0 ? (double)summary.TotalSuccessful / summary.TotalOperations * 100 : 0):F1}%");
            Console.WriteLine($"Average Response Time: {summary.AverageResponseTimeMs:F0}ms");
            Console.WriteLine($"Slowest Operation: {summary.SlowestOperation}");
            Console.WriteLine($"Most Frequent: {summary.MostFrequentOperation}");
            
            if (_options.Verbose)
            {
                Console.WriteLine("\nDetailed Metrics:");
                var allMetrics = _performanceMonitor.GetAllMetrics();
                foreach (var metric in allMetrics.OrderByDescending(m => m.Value.TotalCount).Take(10))
                {
                    Console.WriteLine($"  {metric.Key}:");
                    Console.WriteLine($"    Count: {metric.Value.TotalCount}");
                    Console.WriteLine($"    Success Rate: {metric.Value.SuccessRate:F1}%");
                    Console.WriteLine($"    Avg: {metric.Value.AverageMs:F0}ms");
                    Console.WriteLine($"    Min: {metric.Value.MinMs}ms");
                    Console.WriteLine($"    Max: {metric.Value.MaxMs}ms");
                }
            }
            
            await Task.CompletedTask;
        }

        public async Task ShowMetricsAsync()
        {
            var performanceMonitor = _serviceProvider.GetService<IPerformanceMonitor>();
            if (performanceMonitor == null)
            {
                Console.WriteLine("Performance monitor not available");
                return;
            }

            Console.WriteLine("System Metrics");
            Console.WriteLine("===============");

            // Collect system metrics
            
            var summary = performanceMonitor.GetSummary();

            Console.WriteLine($"Total Operations: {summary.TotalOperations}");
            Console.WriteLine($"- Successful: {summary.TotalSuccessful}");
            Console.WriteLine($"- Failed: {summary.TotalFailed}");
            Console.WriteLine($"- Average Response: {summary.AverageResponseTimeMs:F0}ms");
            Console.WriteLine();

            // Display top operations
            var allMetrics = performanceMonitor.GetAllMetrics();
            if (allMetrics.Any())
            {
                Console.WriteLine("Top Operations:");
                foreach (var metric in allMetrics.OrderByDescending(m => m.Value.TotalCount).Take(5))
                {
                    Console.WriteLine($"  {metric.Key}: {metric.Value.TotalCount} calls, {metric.Value.AverageMs:F0}ms avg");
                }
            }

            if (_options.OutputFormat?.ToLowerInvariant() == "json")
            {
                var json = System.Text.Json.JsonSerializer.Serialize(summary, 
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                
                if (!string.IsNullOrEmpty(_options.OutputPath))
                {
                    await File.WriteAllTextAsync(_options.OutputPath, json);
                    Console.WriteLine($"Metrics saved to: {_options.OutputPath}");
                }
                else
                {
                    Console.WriteLine("JSON Output:");
                    Console.WriteLine(json);
                }
            }
        }

        public async Task MonitorSystemResourcesAsync()
        {
            var performanceMonitor = _serviceProvider.GetService<IPerformanceMonitor>();
            if (performanceMonitor == null)
            {
                Console.WriteLine("Performance monitor not available");
                return;
            }

            if (!_options.Silent)
            {
                using var progress = new ProgressIndicator("Collecting system information", 
                    showSpinner: true, enabled: _options.ShowProgress);
                
                await Task.Delay(1000);
                progress.Complete();
            }

            Console.WriteLine("System Resource Report");
            Console.WriteLine("=====================");
            Console.WriteLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine();

            // Display process information
            var process = Process.GetCurrentProcess();
            Console.WriteLine("Process Information:");
            Console.WriteLine($"  Memory Usage: {process.WorkingSet64 / (1024.0 * 1024.0):F1}MB");
            Console.WriteLine($"  CPU Time: {process.TotalProcessorTime.TotalMilliseconds:F0}ms");
            Console.WriteLine($"  Thread Count: {process.Threads.Count}");
            Console.WriteLine();

            // Display performance summary
            var summary = performanceMonitor.GetSummary();
            Console.WriteLine("Performance Summary:");
            Console.WriteLine($"  Total Operations: {summary.TotalOperations}");
            Console.WriteLine($"  Success Rate: {(summary.TotalOperations > 0 ? (double)summary.TotalSuccessful / summary.TotalOperations * 100 : 0):F1}%");
            Console.WriteLine($"  Average Response: {summary.AverageResponseTimeMs:F0}ms");
            
            if (_options.Verbose)
            {
                Console.WriteLine("\nTop Operations:");
                var metrics = performanceMonitor.GetAllMetrics();
                foreach (var metric in metrics.OrderByDescending(m => m.Value.TotalCount).Take(5))
                {
                    Console.WriteLine($"  {metric.Key}: {metric.Value.TotalCount} calls, {metric.Value.AverageMs:F0}ms avg");
                }
            }
        }

        public async Task RunMaintenanceAsync(string? maintenanceType = null)
        {
            var cleanupService = _serviceProvider.GetService<ICleanupService>();
            if (cleanupService == null)
            {
                Console.WriteLine("Cleanup service not available");
                return;
            }

            Console.WriteLine($"Starting maintenance: {maintenanceType ?? "all"}");
            
            CleanupResult result;
            
            switch (maintenanceType?.ToLowerInvariant())
            {
                case "cache":
                    result = await cleanupService.CleanupCacheAsync();
                    break;
                case "temp":
                    result = await cleanupService.CleanupTemporaryFilesAsync();
                    break;
                case "backups":
                    result = await cleanupService.CleanupOldBackupsAsync();
                    break;
                case "all":
                case null:
                    result = await cleanupService.PerformFullCleanupAsync();
                    break;
                default:
                    Console.WriteLine($"Unknown maintenance type: {maintenanceType}");
                    return;
            }

            Console.WriteLine();
            Console.WriteLine("Maintenance Report");
            Console.WriteLine("==================");
            Console.WriteLine($"Status: {(result.Success ? "Completed" : "Failed")}");
            Console.WriteLine($"Files Deleted: {result.FilesDeleted}");
            Console.WriteLine($"Space Freed: {result.GetFormattedSize()}");

            if (result.Errors.Any())
            {
                Console.WriteLine("\nErrors:");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  - {error}");
                }
            }
        }

        public async Task ValidateDriversAsync(string? deviceId = null)
        {
            var driverService = _serviceProvider.GetRequiredService<IDriverService>();
            
            Console.WriteLine("Driver Validation");
            Console.WriteLine("=================");

            var drivers = await driverService.GetDriversAsync();
            
            if (!string.IsNullOrEmpty(deviceId))
            {
                drivers = drivers.Where(d => d.DeviceId.Contains(deviceId, StringComparison.OrdinalIgnoreCase)).ToList();
                
                if (!drivers.Any())
                {
                    Console.WriteLine($"No drivers found matching: {deviceId}");
                    return;
                }
            }

            Console.WriteLine($"Validating {drivers.Count} driver(s)...");
            Console.WriteLine();

            var validCount = 0;
            var problemDrivers = new List<DriverInfo>();
            
            foreach (var driver in drivers)
            {
                var isValid = await driverService.ValidateDriverAsync(driver.DeviceId);
                if (isValid)
                    validCount++;
                else
                    problemDrivers.Add(driver);
            }
            
            Console.WriteLine($"Validation Summary:");
            Console.WriteLine($"  Total: {drivers.Count}");
            Console.WriteLine($"  Valid: {validCount}");
            Console.WriteLine($"  Invalid: {drivers.Count - validCount}");
            Console.WriteLine();

            if (problemDrivers.Any())
            {
                Console.WriteLine("Drivers requiring attention:");
                Console.WriteLine("----------------------------");
                
                foreach (var driver in problemDrivers)
                {
                    Console.WriteLine($"  - {driver.DeviceName} ({driver.DeviceId})");
                    Console.WriteLine($"    Status: {driver.Status}");
                    Console.WriteLine($"    Version: {driver.DriverVersion}");
                }
            }
            else
            {
                Console.WriteLine("All drivers passed validation");
            }

            if (_options.OutputFormat?.ToLowerInvariant() == "json" && !string.IsNullOrEmpty(_options.OutputPath))
            {
                var results = new { Valid = validCount, Invalid = problemDrivers.Count, Drivers = problemDrivers };
                var json = System.Text.Json.JsonSerializer.Serialize(results, 
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_options.OutputPath, json);
                Console.WriteLine($"\nValidation results saved to: {_options.OutputPath}");
            }
        }

        public async Task CheckSystemHealthAsync()
        {
            var healthService = _serviceProvider.GetService<ISystemHealthService>();
            if (healthService == null)
            {
                Console.WriteLine("System health service not available");
                return;
            }

            Console.WriteLine("System Health Check");
            Console.WriteLine("==================");

            var healthReport = await healthService.GetHealthReportAsync();
            
            var healthIcon = healthReport.HealthScore switch
            {
                >= 90 => "Excellent",
                >= 75 => "Good", 
                >= 50 => "Fair",
                >= 25 => "Poor",
                _ => "Critical"
            };

            Console.WriteLine($"Health Score: {healthReport.HealthScore:F0}/100 {healthIcon}");
            Console.WriteLine($"Check Time: {healthReport.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            Console.WriteLine("System Status:");
            Console.WriteLine($"  Total Drivers: {healthReport.TotalDrivers}");
            Console.WriteLine($"  Working Drivers: {healthReport.WorkingDrivers}");
            Console.WriteLine($"  Problematic Drivers: {healthReport.ProblematicDrivers}");
            Console.WriteLine($"  Available Updates: {healthReport.AvailableUpdates}");
            Console.WriteLine();

            if (healthReport.Recommendations.Length > 0)
            {
                Console.WriteLine("Recommendations:");
                foreach (var recommendation in healthReport.Recommendations)
                {
                    Console.WriteLine($"  - {recommendation}");
                }
                Console.WriteLine();
            }

            if (healthReport.ProblematicDrivers == 0 && healthReport.AvailableUpdates == 0)
            {
                Console.WriteLine("No issues detected - system is healthy");
            }
        }
    }
}