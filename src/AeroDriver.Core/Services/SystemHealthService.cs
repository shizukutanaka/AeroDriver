using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Core.Helpers;

namespace AeroDriver.Core.Services
{
    public class SystemHealthService : ISystemHealthService
    {
        private readonly IDriverService _driverService;
        private readonly ICacheService? _cacheService;
        private readonly ILogger<SystemHealthService>? _logger;

        public SystemHealthService(IDriverService driverService, ICacheService? cacheService = null, ILogger<SystemHealthService>? logger = null)
        {
            _driverService = driverService ?? throw new ArgumentNullException(nameof(driverService));
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<HealthReport> GetHealthReportAsync()
        {
            try
            {
                _logger?.LogInformation("Starting system health check");
                var stopwatch = Stopwatch.StartNew();

                var drivers = await _driverService.GetDriversAsync();
                var updates = await _driverService.ScanForDriversAsync();
                var systemInfo = await GetSystemInfoAsync();
                var isAdmin = await IsAdministratorAsync();

                var healthReport = new HealthReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    IsAdministrator = isAdmin,
                    SystemInfo = systemInfo,
                    TotalDrivers = drivers.Count,
                    WorkingDrivers = drivers.Count(d => d.Status == "OK"),
                    ProblematicDrivers = drivers.Count(d => d.Status != "OK" && d.Status != "Unknown"),
                    UnknownStatusDrivers = drivers.Count(d => d.Status == "Unknown"),
                    AvailableUpdates = updates.Count,
                    DriverClasses = drivers.GroupBy(d => d.DeviceClass)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    TopManufacturers = drivers.GroupBy(d => d.DriverProviderName)
                        .OrderByDescending(g => g.Count())
                        .Take(5)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    HealthScore = CalculateHealthScore(drivers, updates),
                    Recommendations = GenerateRecommendations(drivers, updates, isAdmin)
                };

                stopwatch.Stop();
                healthReport.GenerationTimeMs = (int)stopwatch.ElapsedMilliseconds;

                _logger?.LogInformation("Health check completed in {ElapsedMs}ms, health score: {HealthScore}", 
                    stopwatch.ElapsedMilliseconds, healthReport.HealthScore);

                return healthReport;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error generating health report");
                return new HealthReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    HealthScore = 0,
                    Recommendations = new[] { "Unable to generate health report. Check system permissions and try again." }
                };
            }
        }

        public async Task<bool> IsAdministratorAsync()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return await Task.FromResult(principal.IsInRole(WindowsBuiltInRole.Administrator)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error checking administrator status");
                return await Task.FromResult(false).ConfigureAwait(false);
            }
        }

        public async Task<SystemInfo> GetSystemInfoAsync()
        {
            const string cacheKey = CacheKeys.SystemInfo;
            
            // Try cache first
            if (_cacheService?.TryGet<SystemInfo>(cacheKey, out var cachedInfo) == true && cachedInfo != null)
            {
                return cachedInfo;
            }

            try
            {
                var wmiInfo = await WmiHelper.GetSystemInfoAsync(_logger);
                
                var systemInfo = new SystemInfo
                {
                    ComputerName = wmiInfo.GetValueOrDefault("Computer Name", "Unknown"),
                    Manufacturer = wmiInfo.GetValueOrDefault("Manufacturer", "Unknown"),
                    Model = wmiInfo.GetValueOrDefault("Model", "Unknown"),
                    OperatingSystem = wmiInfo.GetValueOrDefault("Operating System", "Unknown"),
                    Version = wmiInfo.GetValueOrDefault("Version", "Unknown"),
                    Architecture = wmiInfo.GetValueOrDefault("Architecture", "Unknown"),
                    BuildNumber = wmiInfo.GetValueOrDefault("Build Number", "Unknown"),
                    TotalRAM = wmiInfo.GetValueOrDefault("Total RAM", "Unknown"),
                    Processor = wmiInfo.GetValueOrDefault("Processor", "Unknown"),
                    Cores = wmiInfo.GetValueOrDefault("Cores", "Unknown"),
                    Threads = wmiInfo.GetValueOrDefault("Threads", "Unknown")
                };

                // Cache for 30 minutes
                _cacheService?.Set(cacheKey, systemInfo, TimeSpan.FromMinutes(30));

                return systemInfo;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error retrieving system information");
                return new SystemInfo { ComputerName = "Error retrieving system info" };
            }
        }

        public async Task<PerformanceMetrics> GetPerformanceMetricsAsync()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                
                var metrics = new PerformanceMetrics
                {
                    WorkingSet = process.WorkingSet64,
                    PrivateMemorySize = process.PrivateMemorySize64,
                    ProcessorTime = process.TotalProcessorTime,
                    ThreadCount = process.Threads.Count,
                    HandleCount = process.HandleCount,
                    MeasuredAt = DateTime.UtcNow
                };
                return await Task.FromResult(metrics).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error retrieving performance metrics");
                return await Task.FromResult(new PerformanceMetrics { MeasuredAt = DateTime.UtcNow }).ConfigureAwait(false);
            }
        }

        private static int CalculateHealthScore(List<DriverInfo> drivers, List<DriverInfo> updates)
        {
            if (drivers.Count == 0)
                return 0;

            var workingDrivers = drivers.Count(d => d.Status == "OK");
            var problematicDrivers = drivers.Count(d => d.Status != "OK" && d.Status != "Unknown");
            var availableUpdates = updates.Count;

            // Base score from working drivers (0-70 points)
            var baseScore = (int)(70.0 * workingDrivers / drivers.Count);

            // Penalty for problematic drivers (-30 points max)
            var problemPenalty = Math.Min(30, problematicDrivers * 5);

            // Penalty for many available updates (-20 points max)
            var updatePenalty = Math.Min(20, availableUpdates * 2);

            var finalScore = Math.Max(0, Math.Min(100, baseScore - problemPenalty - updatePenalty));
            return finalScore;
        }

        private static string[] GenerateRecommendations(List<DriverInfo> drivers, List<DriverInfo> updates, bool isAdmin)
        {
            var recommendations = new List<string>();

            if (!isAdmin)
            {
                recommendations.Add("Run as Administrator to perform driver operations");
            }

            var problematicDrivers = drivers.Count(d => d.Status != "OK" && d.Status != "Unknown");
            if (problematicDrivers > 0)
            {
                recommendations.Add($"Investigate {problematicDrivers} problematic drivers");
            }

            if (updates.Count > 0)
            {
                recommendations.Add($"Consider updating {updates.Count} drivers with available updates");
            }

            if (updates.Count > 10)
            {
                recommendations.Add("Many driver updates available - consider using auto mode");
            }

            var unknownDrivers = drivers.Count(d => d.Status == "Unknown");
            if (unknownDrivers > drivers.Count * 0.3)
            {
                recommendations.Add("Many drivers have unknown status - system may need maintenance");
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("System appears to be in good condition");
            }

            return recommendations.ToArray();
        }
    }
}