using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Core.Helpers;

namespace AeroDriver.Core.Services
{
    public class DriverService : IDriverService
    {
        private readonly IWhqlDatabaseService _whqlDatabaseService;
        private readonly IBackupService _backupService;
        private readonly ICacheService? _cacheService;
        private readonly ILogger<DriverService>? _logger;
        private readonly IPerformanceMonitor? _performanceMonitor;

        public DriverService(IWhqlDatabaseService whqlDatabaseService, IBackupService backupService, 
            ICacheService? cacheService = null, ILogger<DriverService>? logger = null,
            IPerformanceMonitor? performanceMonitor = null)
        {
            _whqlDatabaseService = whqlDatabaseService ?? throw new ArgumentNullException(nameof(whqlDatabaseService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _cacheService = cacheService;
            _logger = logger;
            _performanceMonitor = performanceMonitor;
        }

        public async Task<List<DriverInfo>> GetDriversAsync()
        {
            return await _performanceMonitor?.MeasureAsync("get_drivers", async () => 
            {
                return await GetDriversInternalAsync();
            }) ?? await GetDriversInternalAsync();
        }
        
        private async Task<List<DriverInfo>> GetDriversInternalAsync()
        {
            const string cacheKey = CacheKeys.AllDrivers;
            
            // Try cache first
            if (_cacheService?.TryGet<List<DriverInfo>>(cacheKey, out var cachedDrivers) == true && cachedDrivers != null)
            {
                _logger?.LogDebug("Retrieved {Count} drivers from cache", cachedDrivers.Count);
                _performanceMonitor?.RecordOperation("cache.hit", 0, true);
                return cachedDrivers;
            }

            _performanceMonitor?.RecordOperation("cache.miss", 0, true);

            try
            {
                _logger?.LogInformation("Starting driver enumeration");
                var drivers = await WmiHelper.GetDriversAsync(_logger);
                
                // Filter out invalid drivers
                var validDrivers = drivers.Where(d => !string.IsNullOrEmpty(d.DeviceId)).ToList();
                
                _logger?.LogInformation("Driver enumeration completed, found {Total} drivers ({Valid} valid)", 
                    drivers.Count, validDrivers.Count);

                // Cache the results
                _cacheService?.Set(cacheKey, validDrivers, TimeSpan.FromMinutes(10));
                // Record driver count

                return validDrivers;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting drivers");
                _performanceMonitor?.RecordOperation("errors.get_drivers", 0, false);
                return new List<DriverInfo>();
            }
        }

        public async Task<List<DriverInfo>> ScanForDriversAsync()
        {
            const string cacheKey = CacheKeys.AvailableUpdates;
            
            // Try cache first
            if (_cacheService?.TryGet<List<DriverInfo>>(cacheKey, out var cachedUpdates) == true && cachedUpdates != null)
            {
                _logger?.LogDebug("Retrieved {Count} driver updates from cache", cachedUpdates.Count);
                return cachedUpdates;
            }

            try
            {
                _logger?.LogInformation("Scanning for driver updates");
                var stopwatch = Stopwatch.StartNew();
                
                var drivers = await GetDriversAsync();
                var updatesAvailable = new List<DriverInfo>();
                
                // Check each driver for updates
                var tasks = drivers.Select(async driver =>
                {
                    try
                    {
                        var update = await _whqlDatabaseService.FindAvailableUpdateAsync(driver);
                        return update;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Error checking updates for driver {DeviceId}", driver.DeviceID);
                        return null;
                    }
                });

                var results = await Task.WhenAll(tasks);
                updatesAvailable.AddRange(results.Where(r => r != null).Cast<DriverInfo>());
                
                stopwatch.Stop();
                _logger?.LogInformation("Update scan completed in {ElapsedMs}ms, found {Count} updates", 
                    stopwatch.ElapsedMilliseconds, updatesAvailable.Count);

                // Cache the results
                _cacheService?.Set(cacheKey, updatesAvailable, TimeSpan.FromMinutes(5));
                
                return updatesAvailable;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error scanning for driver updates");
                return new List<DriverInfo>();
            }
        }

        public async Task<bool> UpdateDriverAsync(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                _logger?.LogWarning("UpdateDriverAsync called with null or empty deviceId");
                return false;
            }

            try
            {
                _logger?.LogInformation("Starting driver update for {DeviceId}", deviceId);

                // Create backup before updating
                var backupSuccess = await _backupService.CreateBackupAsync(deviceId);
                if (!backupSuccess)
                {
                    _logger?.LogWarning("Backup failed for device {DeviceId}, aborting update", deviceId);
                    return false;
                }

                // Simulate driver update using pnputil
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "pnputil",
                    Arguments = "/scan-devices",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var success = process.ExitCode == 0;
                    
                    if (success)
                    {
                        _logger?.LogInformation("Driver update completed successfully for {DeviceId}", deviceId);
                        // Invalidate cache
                        _cacheService?.Remove(CacheKeys.AllDrivers);
                        _cacheService?.Remove(CacheKeys.AvailableUpdates);
                    }
                    else
                    {
                        _logger?.LogWarning("Driver update failed for {DeviceId}, exit code: {ExitCode}", deviceId, process.ExitCode);
                    }
                    
                    return success;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating driver {DeviceId}", deviceId);
                return false;
            }
        }

        public async Task<bool> RollbackDriverAsync(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                _logger?.LogWarning("RollbackDriverAsync called with null or empty deviceId");
                return false;
            }

            try
            {
                _logger?.LogInformation("Starting driver rollback for {DeviceId}", deviceId);
                
                var backups = await _backupService.GetBackupsAsync();
                var deviceBackups = backups.Where(b => b.DeviceId == deviceId).OrderByDescending(b => b.BackupDate).ToList();
                
                if (!deviceBackups.Any())
                {
                    _logger?.LogWarning("No backups found for device {DeviceId}", deviceId);
                    return false;
                }

                var latestBackup = deviceBackups.First();
                var success = await _backupService.RestoreBackupAsync(latestBackup.BackupPath);
                
                if (success)
                {
                    _logger?.LogInformation("Driver rollback completed successfully for {DeviceId}", deviceId);
                    // Invalidate cache
                    _cacheService?.Remove(CacheKeys.AllDrivers);
                    _cacheService?.Remove(CacheKeys.AvailableUpdates);
                }
                else
                {
                    _logger?.LogWarning("Driver rollback failed for {DeviceId}", deviceId);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error rolling back driver {DeviceId}", deviceId);
                return false;
            }
        }

        public async Task<bool> BackupDriverAsync(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                _logger?.LogWarning("BackupDriverAsync called with null or empty deviceId");
                return false;
            }

            try
            {
                _logger?.LogInformation("Creating backup for driver {DeviceId}", deviceId);
                return await _backupService.CreateBackupAsync(deviceId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error backing up driver {DeviceId}", deviceId);
                return false;
            }
        }
        
        public async Task<bool> ValidateDriverAsync(string deviceId)
        {
            try
            {
                _logger?.LogInformation("Validating driver: {DeviceId}", deviceId);
                var drivers = await GetDriversAsync();
                var driver = drivers.FirstOrDefault(d => d.DeviceId == deviceId);
                
                if (driver == null)
                {
                    _logger?.LogWarning("Driver not found: {DeviceId}", deviceId);
                    return false;
                }
                
                // Basic validation checks
                if (string.IsNullOrEmpty(driver.DriverVersion))
                {
                    _logger?.LogWarning("Driver {DeviceId} has no version information", deviceId);
                    return false;
                }
                
                if (driver.Status != "OK" && driver.Status != "Unknown")
                {
                    _logger?.LogWarning("Driver {DeviceId} has problematic status: {Status}", deviceId, driver.Status);
                    return false;
                }
                
                _logger?.LogInformation("Driver {DeviceId} validation passed", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error validating driver {DeviceId}", deviceId);
                return false;
            }
        }
    }
}