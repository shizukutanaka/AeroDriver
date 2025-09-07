using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    public class BackupService : IBackupService
    {
        private const string BackupRoot = "Backups";

        public BackupService()
        {
            if (!Directory.Exists(BackupRoot))
            {
                Directory.CreateDirectory(BackupRoot);
            }
        }

        public async Task<bool> CreateBackupAsync(string deviceId)
        {
            try
            {
                if (string.IsNullOrEmpty(deviceId))
                    return false;

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var backupDir = Path.Combine(BackupRoot, SanitizeFileName(deviceId));
                
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                var backupInfo = new BackupInfo
                {
                    DeviceId = deviceId,
                    BackupDate = DateTime.Now,
                    BackupPath = backupDir,
                    Description = $"Backup for device {deviceId}"
                };

                var backupFile = Path.Combine(backupDir, $"backup_{timestamp}.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(backupFile, JsonSerializer.Serialize(backupInfo, options));

                Console.WriteLine($"Backup created: {backupFile}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating backup: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RestoreBackupAsync(string backupPath)
        {
            try
            {
                if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
                    return false;

                var backupFiles = Directory.GetFiles(backupPath, "backup_*.json");
                if (!backupFiles.Any())
                    return false;

                var latestBackup = backupFiles.OrderByDescending(f => f).First();
                var backupContent = await File.ReadAllTextAsync(latestBackup);
                
                Console.WriteLine($"Restoring from backup: {backupContent}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error restoring backup: {ex.Message}");
                return false;
            }
        }

        public async Task<List<BackupInfo>> GetBackupsAsync()
        {
            var backups = new List<BackupInfo>();

            try
            {
                if (!Directory.Exists(BackupRoot))
                    return backups;

                var deviceDirs = Directory.GetDirectories(BackupRoot);
                
                foreach (var deviceDir in deviceDirs)
                {
                    var backupFiles = Directory.GetFiles(deviceDir, "backup_*.json");
                    
                    foreach (var backupFile in backupFiles.OrderByDescending(f => f))
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(backupFile);
                            var backupInfo = JsonSerializer.Deserialize<BackupInfo>(json);
                            if (backupInfo != null)
                            {
                                backups.Add(backupInfo);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading backup file {backupFile}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting backups: {ex.Message}");
            }

            return backups.OrderByDescending(b => b.BackupDate).ToList();
        }

        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}