using System;
using System.Linq;
using System.Threading.Tasks;
using AeroDriver.Core.Services;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.CLI
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                Console.WriteLine("AeroDriver CLI v1.0");
                Console.WriteLine("===================");

                // Initialize services
                var backupService = new BackupService();
                var whqlService = new WhqlDatabaseService();
                var driverService = new DriverService(whqlService, backupService);

                if (args.Length == 0)
                {
                    await ShowDriverList(driverService);
                    return 0;
                }

                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "list":
                        await ShowDriverList(driverService);
                        break;
                    case "scan":
                        await ScanForUpdates(driverService);
                        break;
                    case "update":
                        if (args.Length > 1)
                        {
                            await UpdateDriver(driverService, args[1]);
                        }
                        else
                        {
                            Console.WriteLine("Usage: update <deviceId>");
                        }
                        break;
                    case "backup":
                        if (args.Length > 1)
                        {
                            await BackupDriver(driverService, args[1]);
                        }
                        else
                        {
                            Console.WriteLine("Usage: backup <deviceId>");
                        }
                        break;
                    case "rollback":
                        if (args.Length > 1)
                        {
                            await RollbackDriver(driverService, args[1]);
                        }
                        else
                        {
                            Console.WriteLine("Usage: rollback <deviceId>");
                        }
                        break;
                    case "help":
                    case "-h":
                    case "--help":
                        ShowHelp();
                        break;
                    default:
                        Console.WriteLine($"Unknown command: {command}");
                        Console.WriteLine("Use 'help' for available commands.");
                        return 1;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        private static async Task ShowDriverList(DriverService driverService)
        {
            Console.WriteLine("Getting driver list...");
            var drivers = await driverService.GetDriversAsync();
            
            Console.WriteLine($"\nFound {drivers.Count} drivers:");
            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"{"Device Name",-40} {"Version",-15} {"Status",-10}");
            Console.WriteLine(new string('-', 80));
            
            foreach (var driver in drivers.Take(20)) // Limit to first 20 for readability
            {
                var name = driver.DeviceName?.Length > 37 ? driver.DeviceName.Substring(0, 37) + "..." : driver.DeviceName ?? "Unknown";
                Console.WriteLine($"{name,-40} {driver.DriverVersion,-15} {driver.Status,-10}");
            }
            
            if (drivers.Count > 20)
            {
                Console.WriteLine($"... and {drivers.Count - 20} more drivers");
            }
        }

        private static async Task ScanForUpdates(DriverService driverService)
        {
            Console.WriteLine("Scanning for driver updates...");
            var updates = await driverService.ScanForDriversAsync();
            
            if (updates.Any())
            {
                Console.WriteLine($"\nFound {updates.Count} available updates:");
                foreach (var update in updates)
                {
                    Console.WriteLine($"- {update.DeviceName} (Current: {update.DriverVersion})");
                }
            }
            else
            {
                Console.WriteLine("No driver updates available.");
            }
        }

        private static async Task UpdateDriver(DriverService driverService, string deviceId)
        {
            Console.WriteLine($"Updating driver: {deviceId}");
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

        private static async Task BackupDriver(DriverService driverService, string deviceId)
        {
            Console.WriteLine($"Backing up driver: {deviceId}");
            var result = await driverService.BackupDriverAsync(deviceId);
            
            if (result)
            {
                Console.WriteLine("Driver backup created successfully.");
            }
            else
            {
                Console.WriteLine("Failed to create driver backup.");
            }
        }

        private static async Task RollbackDriver(DriverService driverService, string deviceId)
        {
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

        private static void ShowHelp()
        {
            Console.WriteLine("AeroDriver CLI - Driver Management Tool");
            Console.WriteLine("\nUsage: AeroDriver.CLI [command] [options]");
            Console.WriteLine("\nCommands:");
            Console.WriteLine("  list                  List all installed drivers");
            Console.WriteLine("  scan                  Scan for available driver updates");
            Console.WriteLine("  update <deviceId>     Update a specific driver");
            Console.WriteLine("  backup <deviceId>     Create backup of a driver");
            Console.WriteLine("  rollback <deviceId>   Rollback a driver to previous version");
            Console.WriteLine("  help                  Show this help message");
            Console.WriteLine("\nExamples:");
            Console.WriteLine("  AeroDriver.CLI list");
            Console.WriteLine("  AeroDriver.CLI scan");
            Console.WriteLine("  AeroDriver.CLI update \"PCI\\VEN_8086&DEV_1234\"");
        }
    }
}