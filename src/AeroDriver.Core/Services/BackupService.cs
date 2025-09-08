using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Management;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Core.Helpers;

namespace AeroDriver.Core.Services
{
    public class BackupService : IBackupService
    {
        private readonly string _backupRoot;
        private readonly ILogger<BackupService>? _logger;
        private readonly MetricsCollectionService? _metricsService;
        private readonly int _maxBackupsPerDevice;

        public BackupService(ILogger<BackupService>? logger = null, 
            MetricsCollectionService? metricsService = null, 
            string? customBackupRoot = null, 
            int maxBackupsPerDevice = 5)
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _backupRoot = customBackupRoot ?? Path.Combine(appDataPath, "AeroDriver", "Backups");
            _logger = logger;
            _metricsService = metricsService;
            _maxBackupsPerDevice = maxBackupsPerDevice;

            if (!Directory.Exists(_backupRoot))
            {
                Directory.CreateDirectory(_backupRoot);
            }
        }

        public async Task<bool> CreateBackupAsync(string deviceId)
        {
            return await _metricsService?.MeasureAsync("backup_create", async () =>
            {
                return await CreateBackupInternalAsync(deviceId);
            }, "backup") ?? await CreateBackupInternalAsync(deviceId);
        }
        
        private async Task<bool> CreateBackupInternalAsync(string deviceId)
        {
            try
            {
                if (string.IsNullOrEmpty(deviceId))
                {
                    _logger?.LogWarning("Cannot create backup: device ID is empty");
                    return false;
                }

                _logger?.LogInformation("Starting backup for device: {DeviceId}", deviceId);
                
                // Get device information
                var deviceInfo = await GetDeviceInfoAsync(deviceId);
                if (deviceInfo == null)
                {
                    _logger?.LogWarning("Device not found: {DeviceId}", deviceId);
                    return false;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var sanitizedDeviceId = SecurityHelper.IsFilenameSafe(deviceId) ? 
                    SanitizeFileName(deviceId) : Guid.NewGuid().ToString("N")[..8];
                    
                var deviceBackupDir = Path.Combine(_backupRoot, sanitizedDeviceId);
                var backupDir = Path.Combine(deviceBackupDir, $"backup_{timestamp}");
                
                Directory.CreateDirectory(backupDir);

                // Backup driver files
                var backedUpFiles = new List<string>();
                if (!string.IsNullOrEmpty(deviceInfo.DriverPath))
                {
                    var driverFiles = await GetDriverFilesAsync(deviceInfo.DriverPath);
                    foreach (var file in driverFiles)
                    {
                        var backupPath = Path.Combine(backupDir, Path.GetFileName(file));
                        if (await FileHelper.SafeCopyAsync(file, backupPath, false, _logger))
                        {
                            backedUpFiles.Add(file);
                        }
                    }
                }

                // Create backup metadata
                var backupInfo = new EnhancedBackupInfo
                {
                    Id = Guid.NewGuid().ToString(),
                    DeviceId = deviceId,
                    DeviceName = deviceInfo.DeviceName,
                    BackupDate = DateTime.UtcNow,
                    BackupPath = backupDir,
                    DriverVersion = deviceInfo.DriverVersion,
                    BackupSize = await CalculateBackupSizeAsync(backupDir),
                    Description = $"Driver backup for {deviceInfo.DeviceName}",
                    BackedUpFiles = backedUpFiles,
                    OriginalDriverPath = deviceInfo.DriverPath ?? "",
                    Manufacturer = deviceInfo.Manufacturer ?? ""
                };

                // Save backup metadata
                var metadataFile = Path.Combine(backupDir, "backup_metadata.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(metadataFile, JsonSerializer.Serialize(backupInfo, options));

                // Clean up old backups for this device
                await CleanupOldBackupsAsync(deviceBackupDir);

                _logger?.LogInformation("Backup completed for {DeviceId}: {FileCount} files backed up", 
                    deviceId, backedUpFiles.Count);
                    
                _metricsService?.IncrementCounter("backup.created", "backup");
                _metricsService?.SetGauge("backup.files_backed_up", backedUpFiles.Count, "backup");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating backup for device: {DeviceId}", deviceId);
                _metricsService?.IncrementCounter("backup.failed", "backup");
                return false;
            }
        }

        public async Task<bool> RestoreBackupAsync(string backupPath)
        {
            return await _metricsService?.MeasureAsync("backup_restore", async () =>
            {
                return await RestoreBackupInternalAsync(backupPath);
            }, "backup") ?? await RestoreBackupInternalAsync(backupPath);
        }
        
        private async Task<bool> RestoreBackupInternalAsync(string backupPath)
        {
            try
            {
                if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
                {
                    _logger?.LogWarning("Invalid backup path: {BackupPath}", backupPath);
                    return false;
                }

                // Read backup metadata
                var metadataFile = Path.Combine(backupPath, "backup_metadata.json");
                if (!File.Exists(metadataFile))
                {
                    _logger?.LogWarning("Backup metadata not found: {MetadataFile}", metadataFile);
                    return false;
                }

                var metadataJson = await File.ReadAllTextAsync(metadataFile);
                var backupInfo = JsonSerializer.Deserialize<EnhancedBackupInfo>(metadataJson);
                
                if (backupInfo == null)
                {
                    _logger?.LogWarning("Could not deserialize backup metadata");
                    return false;
                }

                _logger?.LogInformation("Starting restore from backup: {BackupId} for device {DeviceId}", 
                    backupInfo.Id, backupInfo.DeviceId);

                // Check if we're running as administrator (required for driver operations)
                if (!SecurityHelper.IsRunningAsAdministrator())
                {
                    _logger?.LogWarning("Administrator privileges required for driver restore");
                    return false;
                }

                // Create system restore point (if available)
                await CreateSystemRestorePointAsync($"Before driver restore: {backupInfo.DeviceName}");

                // Restore driver files to system location
                var systemDriverPath = GetSystemDriverPath();
                var restoredFiles = new List<string>();
                
                foreach (var backedUpFile in backupInfo.BackedUpFiles)
                {
                    var fileName = Path.GetFileName(backedUpFile);
                    var sourceFile = Path.Combine(backupPath, fileName);
                    var destinationFile = Path.Combine(systemDriverPath, fileName);
                    
                    if (File.Exists(sourceFile))
                    {
                        // Backup current file before replacing
                        var currentBackup = await FileHelper.CreateBackupAsync(destinationFile, null, _logger);
                        
                        if (await FileHelper.SafeCopyAsync(sourceFile, destinationFile, true, _logger))
                        {
                            restoredFiles.Add(destinationFile);
                        }
                        else
                        {
                            _logger?.LogWarning("Failed to restore file: {FileName}", fileName);
                        }
                    }
                }

                _logger?.LogInformation("Restore completed: {RestoredCount}/{TotalCount} files restored", 
                    restoredFiles.Count, backupInfo.BackedUpFiles.Count);
                    
                _metricsService?.IncrementCounter("backup.restored", "backup");
                _metricsService?.SetGauge("backup.files_restored", restoredFiles.Count, "backup");
                
                return restoredFiles.Count > 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error restoring backup from: {BackupPath}", backupPath);
                _metricsService?.IncrementCounter("backup.restore_failed", "backup");
                return false;
            }
        }

        public async Task<List<BackupInfo>> GetBackupsAsync()
        {
            var backups = new List<BackupInfo>();

            try
            {
                if (!Directory.Exists(_backupRoot))
                    return backups;

                var deviceDirs = Directory.GetDirectories(_backupRoot);
                
                foreach (var deviceDir in deviceDirs)
                {
                    var backupDirs = Directory.GetDirectories(deviceDir, "backup_*");
                    
                    foreach (var backupDir in backupDirs)
                    {
                        try
                        {
                            var metadataFile = Path.Combine(backupDir, "backup_metadata.json");
                            if (File.Exists(metadataFile))
                            {
                                var json = await File.ReadAllTextAsync(metadataFile);
                                var enhancedBackup = JsonSerializer.Deserialize<EnhancedBackupInfo>(json);
                                
                                if (enhancedBackup != null)
                                {
                                    // Convert to base BackupInfo for compatibility
                                    var backupInfo = new BackupInfo
                                    {
                                        Id = enhancedBackup.Id,
                                        DeviceId = enhancedBackup.DeviceId,
                                        DeviceName = enhancedBackup.DeviceName,
                                        BackupPath = enhancedBackup.BackupPath,
                                        BackupDate = enhancedBackup.BackupDate,
                                        DriverVersion = enhancedBackup.DriverVersion,
                                        BackupSize = enhancedBackup.BackupSize,
                                        Description = enhancedBackup.Description
                                    };
                                    backups.Add(backupInfo);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Error reading backup metadata from {BackupDir}", backupDir);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting backups from {BackupRoot}", _backupRoot);
            }

            return backups.OrderByDescending(b => b.BackupDate).ToList();
        }

        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            return sanitized.Length > 50 ? sanitized[..50] : sanitized; // Limit length
        }
        
        private async Task<DriverInfo?> GetDeviceInfoAsync(string deviceId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_PnPEntity WHERE DeviceID = '{deviceId.Replace("\\", "\\\\")}'")
;
                using var collection = searcher.Get();
                
                foreach (ManagementObject device in collection)
                {
                    using (device)
                    {
                        var name = device.Properties["Name"]?.Value?.ToString() ?? "Unknown Device";
                        var driverVersion = device.Properties["DriverVersion"]?.Value?.ToString() ?? "Unknown";
                        var manufacturer = device.Properties["Manufacturer"]?.Value?.ToString() ?? "Unknown";
                        
                        return new DriverInfo
                        {
                            DeviceID = deviceId,
                            DeviceName = name,
                            DriverVersion = driverVersion,
                            Manufacturer = manufacturer,
                            DriverPath = GetDriverPathFromDevice(device)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error getting device info for {DeviceId}", deviceId);
            }
            
            return null;
        }
        
        private string? GetDriverPathFromDevice(ManagementObject device)
        {
            try
            {
                var service = device.Properties["Service"]?.Value?.ToString();
                if (!string.IsNullOrEmpty(service))
                {
                    var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    var driverPath = Path.Combine(systemRoot, "System32", "drivers", $"{service}.sys");
                    if (File.Exists(driverPath))
                    {
                        return driverPath;
                    }
                }
            }
            catch { /* Ignore errors */ }
            
            return null;
        }
        
        private async Task<List<string>> GetDriverFilesAsync(string primaryDriverPath)
        {
            var files = new List<string>();
            
            if (string.IsNullOrEmpty(primaryDriverPath) || !File.Exists(primaryDriverPath))
                return files;
                
            files.Add(primaryDriverPath);
            
            var directory = Path.GetDirectoryName(primaryDriverPath);
            var baseName = Path.GetFileNameWithoutExtension(primaryDriverPath);
            
            if (!string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(baseName))
            {
                // Look for related files (INF, CAT, etc.)
                var relatedExtensions = new[] { ".inf", ".cat", ".pdb" };
                
                foreach (var ext in relatedExtensions)
                {
                    var relatedFile = Path.Combine(directory, baseName + ext);
                    if (File.Exists(relatedFile))
                    {
                        files.Add(relatedFile);
                    }
                }
            }
            
            return files;
        }
        
        private async Task<long> CalculateBackupSizeAsync(string backupPath)
        {
            try
            {
                return Directory.GetFiles(backupPath, "*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f).Length)
                    .Sum();
            }
            catch
            {
                return 0;
            }
        }
        
        private async Task CleanupOldBackupsAsync(string deviceBackupDir)
        {
            try
            {
                var backupDirs = Directory.GetDirectories(deviceBackupDir, "backup_*")
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .Skip(_maxBackupsPerDevice)
                    .ToList();
                    
                foreach (var oldBackupDir in backupDirs)
                {
                    try
                    {
                        Directory.Delete(oldBackupDir, true);
                        _logger?.LogTrace("Deleted old backup: {BackupDir}", oldBackupDir);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to delete old backup: {BackupDir}", oldBackupDir);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error cleaning up old backups in {DeviceBackupDir}", deviceBackupDir);
            }
        }
        
        private string GetSystemDriverPath()
        {
            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return Path.Combine(systemRoot, "System32", "drivers");
        }
        
        private async Task CreateSystemRestorePointAsync(string description)
        {
            try
            {
                // This would require additional implementation for Windows restore points
                // For now, just log the intention
                _logger?.LogInformation("System restore point would be created: {Description}", description);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not create system restore point");
            }
        }
    }
    
    /// <summary>
    /// Enhanced backup information with more detailed metadata
    /// </summary>
    internal class EnhancedBackupInfo : BackupInfo
    {
        public List<string> BackedUpFiles { get; set; } = new();
        public string OriginalDriverPath { get; set; } = "";
        public string Manufacturer { get; set; } = "";
    }
    }
}