using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    public class DriverService : IDriverService
    {
        private readonly IWhqlDatabaseService _whqlDatabaseService;
        private readonly IBackupService _backupService;

        public DriverService(IWhqlDatabaseService whqlDatabaseService, IBackupService backupService)
        {
            _whqlDatabaseService = whqlDatabaseService ?? throw new ArgumentNullException(nameof(whqlDatabaseService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        }

        public async Task<List<DriverInfo>> GetDriversAsync()
        {
            return await Task.Run(() =>
            {
                var drivers = new List<DriverInfo>();
                
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity");
                    using var collection = searcher.Get();
                    
                    foreach (ManagementObject device in collection)
                    {
                        using (device)
                        {
                            var driverInfo = CreateDriverInfoFromDevice(device);
                            if (driverInfo != null)
                            {
                                drivers.Add(driverInfo);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting drivers: {ex.Message}");
                }
                
                return drivers;
            });
        }

        public async Task<List<DriverInfo>> ScanForDriversAsync()
        {
            var drivers = await GetDriversAsync();
            var updatesAvailable = new List<DriverInfo>();
            
            foreach (var driver in drivers)
            {
                var updates = await _whqlDatabaseService.CheckForUpdatesAsync();
                if (updates != null && updates.Any())
                {
                    updatesAvailable.AddRange(updates);
                }
            }
            
            return updatesAvailable;
        }

        public async Task<bool> UpdateDriverAsync(string deviceId)
        {
            try
            {
                // Create backup before updating
                await _backupService.CreateBackupAsync(deviceId);
                
                // Simulate driver update
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "pnputil",
                    Arguments = $"/scan-devices",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating driver: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RollbackDriverAsync(string deviceId)
        {
            try
            {
                var backups = await _backupService.GetBackupsAsync();
                if (backups.Any())
                {
                    var latestBackup = backups.OrderByDescending(b => b.BackupDate).First();
                    return await _backupService.RestoreBackupAsync(latestBackup.BackupPath);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rolling back driver: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BackupDriverAsync(string deviceId)
        {
            return await _backupService.CreateBackupAsync(deviceId);
        }

        private DriverInfo CreateDriverInfoFromDevice(ManagementObject device)
        {
            try
            {
                var deviceId = device.Properties["DeviceID"]?.Value?.ToString();
                var name = device.Properties["Name"]?.Value?.ToString();
                
                if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(name))
                    return null;

                return new DriverInfo
                {
                    DeviceID = deviceId,
                    DeviceName = name,
                    DriverVersion = device.Properties["DriverVersion"]?.Value?.ToString() ?? "Unknown",
                    DriverProviderName = device.Properties["Manufacturer"]?.Value?.ToString() ?? "Unknown",
                    DeviceClass = device.Properties["PNPClass"]?.Value?.ToString() ?? "Unknown",
                    Status = device.Properties["Status"]?.Value?.ToString() ?? "OK"
                };
            }
            catch
            {
                return null;
            }
        }
    }
}