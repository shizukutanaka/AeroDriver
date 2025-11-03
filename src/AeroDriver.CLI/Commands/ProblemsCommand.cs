using System.Runtime.Versioning;
using AeroDriver.Core;
using AeroDriver.Core.Services;
using AeroDriver.Core.UI;

namespace AeroDriver.CLI.Commands;

/// <summary>
/// Problems command with detailed driver issue analysis
/// </summary>
[SupportedOSPlatform("windows")]
public class ProblemsCommand
{
    private readonly ISimpleLogger _logger;
    private readonly CoreDriverService _driverService;

    public ProblemsCommand(ISimpleLogger logger, CoreDriverService driverService)
    {
        _logger = logger;
        _driverService = driverService;
    }

    public async Task<int> ExecuteAsync(bool verbose = false)
    {
        try
        {
            EnhancedCliOutput.WritePageHeader("Driver Problems", "System Driver Issue Analysis");

            // Scan for problems
            var scanTask = Task.Run(() =>
            {
                var drivers = _driverService.GetAllDrivers();
                var problemDrivers = drivers.Where(d =>
                    d.Status.Contains("Problem", StringComparison.OrdinalIgnoreCase) ||
                    d.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                    d.Status.Contains("Degraded", StringComparison.OrdinalIgnoreCase)
                ).ToList();

                return (drivers, problemDrivers);
            });

            var result = await EnhancedCliOutput.ShowSpinnerAsync("Analyzing driver status", scanTask);

            if (!result.problemDrivers.Any())
            {
                EnhancedCliOutput.WriteNotification("No driver problems detected. System is healthy!", StatusLevel.Success);
                return 0;
            }

            // Categorize problems by severity
            var critical = result.problemDrivers.Where(d => d.Status.Contains("Error", StringComparison.OrdinalIgnoreCase)).ToList();
            var warnings = result.problemDrivers.Where(d => d.Status.Contains("Degraded", StringComparison.OrdinalIgnoreCase)).ToList();
            var info = result.problemDrivers.Except(critical).Except(warnings).ToList();

            // Display summary
            var summaryItems = new List<(string label, string value, StatusLevel status)>
            {
                ("Critical Issues", critical.Count.ToString(), critical.Any() ? StatusLevel.Error : StatusLevel.Success),
                ("Warnings", warnings.Count.ToString(), warnings.Any() ? StatusLevel.Warning : StatusLevel.Success),
                ("Informational", info.Count.ToString(), StatusLevel.Info),
                ("Total Problems", result.problemDrivers.Count.ToString(), StatusLevel.Warning)
            };

            EnhancedCliOutput.WriteStatusPanel("Problem Summary", summaryItems);

            // Display detailed problem table
            if (verbose)
            {
                var headers = new[] { "Driver Name", "Status", "Type", "Version" };
                var rows = result.problemDrivers.Select(d => new[]
                {
                    TruncateString(d.Name, 30),
                    d.Status,
                    d.Type,
                    d.Version
                }).ToList();

                EnhancedCliOutput.WriteTable(headers, rows);
            }
            else
            {
                // Show categorized lists
                if (critical.Any())
                {
                    var criticalList = critical.Select(d => ($"{d.Name} - {d.Status}", StatusLevel.Error)).ToList();
                    EnhancedCliOutput.WriteList("Critical Issues", criticalList, 5);
                }

                if (warnings.Any())
                {
                    var warningList = warnings.Select(d => ($"{d.Name} - {d.Status}", StatusLevel.Warning)).ToList();
                    EnhancedCliOutput.WriteList("Warnings", warningList, 5);
                }

                if (info.Any())
                {
                    var infoList = info.Select(d => ($"{d.Name} - {d.Status}", StatusLevel.Info)).ToList();
                    EnhancedCliOutput.WriteList("Other Issues", infoList, 3);
                }
            }

            // Recommendations
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Recommended Actions:");
            Console.ResetColor();

            if (critical.Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  ✕ Critical issues require immediate attention");
                Console.ResetColor();
                Console.WriteLine("    Run: aerodriver fix --critical");
            }

            if (warnings.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  ⚠ Review warnings and consider driver updates");
                Console.ResetColor();
                Console.WriteLine("    Run: aerodriver update --recommended");
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("For detailed view, use: aerodriver problems --verbose");
            Console.ResetColor();
            Console.WriteLine();

            return result.problemDrivers.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Problems command error: {ex.Message}");
            EnhancedCliOutput.WriteAlert("Error", $"Failed to analyze driver problems: {ex.Message}", StatusLevel.Error);
            return 1;
        }
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
    }
}
