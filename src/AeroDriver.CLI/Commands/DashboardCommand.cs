using System.Runtime.Versioning;
using AeroDriver.Core;
using AeroDriver.Core.Services;
using AeroDriver.Core.UI;

namespace AeroDriver.CLI.Commands;

/// <summary>
/// Dashboard command with Atlassian-inspired UI
/// </summary>
[SupportedOSPlatform("windows")]
public class DashboardCommand
{
    private readonly ISimpleLogger _logger;
    private readonly CoreDriverService _driverService;

    public DashboardCommand(ISimpleLogger logger, CoreDriverService driverService)
    {
        _logger = logger;
        _driverService = driverService;
    }

    public async Task<int> ExecuteAsync()
    {
        try
        {
            EnhancedCliOutput.WritePageHeader("AeroDriver Dashboard", "Professional Windows Driver Management");

            // Gather system information
            var stats = new Dictionary<string, string>
            {
                ["Operating System"] = Environment.OSVersion.ToString(),
                ["Machine Name"] = Environment.MachineName,
                ["Processor Count"] = Environment.ProcessorCount.ToString(),
                ["System Directory"] = Environment.SystemDirectory,
                ["User Domain"] = Environment.UserDomainName
            };

            EnhancedCliOutput.WriteKeyValuePairs(stats, "System Information");

            // Collect driver data
            var scanTask = Task.Run(() =>
            {
                var scanResult = _driverService.ScanSystem();
                var drivers = _driverService.GetAllDrivers();
                var problemDrivers = drivers.Where(d => d.Status.Contains("Problem", StringComparison.OrdinalIgnoreCase)).ToList();

                return (scanResult, drivers, problemDrivers);
            });

            var result = await EnhancedCliOutput.ShowSpinnerAsync("Scanning system drivers", scanTask);

            // Display health status
            var healthItems = new List<(string label, string value, StatusLevel status)>
            {
                ("Total Drivers", result.drivers.Count.ToString(), StatusLevel.Info),
                ("Scanned Drivers", result.scanResult.ScannedDrivers.ToString(), StatusLevel.Success),
                ("Problem Drivers", result.problemDrivers.Count.ToString(),
                    result.problemDrivers.Count == 0 ? StatusLevel.Success :
                    result.problemDrivers.Count < 3 ? StatusLevel.Warning : StatusLevel.Error),
                ("System Status", result.problemDrivers.Count == 0 ? "Healthy" : "Needs Attention",
                    result.problemDrivers.Count == 0 ? StatusLevel.Success : StatusLevel.Warning)
            };

            EnhancedCliOutput.WriteStatusPanel("Driver Health", healthItems);

            // Show recent activity or issues
            if (result.problemDrivers.Any())
            {
                var issues = result.problemDrivers.Take(5).Select(d =>
                    ($"{d.Name} - {d.Status}", StatusLevel.Warning)
                ).ToList();

                EnhancedCliOutput.WriteList("Issues Detected", issues);

                EnhancedCliOutput.WriteAlert(
                    "Action Required",
                    $"Found {result.problemDrivers.Count} driver(s) requiring attention. Run 'aerodriver problems' for details.",
                    StatusLevel.Warning
                );
            }
            else
            {
                EnhancedCliOutput.WriteNotification("All drivers are functioning normally", StatusLevel.Success);
            }

            // Quick actions menu
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Quick Actions:");
            Console.ResetColor();
            Console.WriteLine("  aerodriver scan        - Full system scan");
            Console.WriteLine("  aerodriver problems    - View driver problems");
            Console.WriteLine("  aerodriver optimize    - Optimize system performance");
            Console.WriteLine("  aerodriver help        - Show all commands");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Dashboard error: {ex.Message}");
            EnhancedCliOutput.WriteAlert("Error", $"Failed to load dashboard: {ex.Message}", StatusLevel.Error);
            return 1;
        }
    }
}
