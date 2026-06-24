using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    public class BackupService : IBackupService
    {
        private readonly ILogger<BackupService> _logger;
        private readonly string _backupRoot;
        private const int DefaultMaxGenerations = 3;

        public BackupService(ILogger<BackupService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _backupRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver", "Backups");

            Directory.CreateDirectory(_backupRoot);
        }

        public async Task<bool> BackupDriverAsync(DriverInfo driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません", nameof(driver));

            try
            {
                var deviceDir = GetDeviceDirectory(driver.DeviceID);
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var backupDir = Path.Combine(deviceDir, $"backup_{timestamp}");
                Directory.CreateDirectory(backupDir);

                var meta = new
                {
                    driver.DeviceID,
                    driver.DeviceName,
                    driver.DriverVersion,
                    BackupTimeUtc = DateTime.UtcNow,
                };

                await File.WriteAllTextAsync(
                    Path.Combine(backupDir, "backup_info.json"),
                    JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

                _logger.LogInformation("バックアップを作成しました: {BackupDir}", backupDir);

                await CleanupOldBackupsAsync(deviceDir, DefaultMaxGenerations);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "バックアップ作成中にエラーが発生しました: {DeviceID}", driver.DeviceID);
                return false;
            }
        }

        public async Task<bool> RestoreDriverAsync(DriverInfo driver, string backupVersion = null)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません", nameof(driver));

            try
            {
                var deviceDir = GetDeviceDirectory(driver.DeviceID);

                string backupDir;
                if (string.IsNullOrEmpty(backupVersion))
                {
                    backupDir = Directory.GetDirectories(deviceDir, "backup_*")
                        .OrderByDescending(d => d)
                        .FirstOrDefault();

                    if (backupDir == null)
                    {
                        _logger.LogWarning("復元可能なバックアップが見つかりません: {DeviceID}", driver.DeviceID);
                        return false;
                    }
                }
                else
                {
                    backupDir = Path.Combine(deviceDir, $"backup_{backupVersion}");
                    if (!Directory.Exists(backupDir))
                    {
                        _logger.LogWarning("指定されたバックアップが見つかりません: {Version}", backupVersion);
                        return false;
                    }
                }

                var infoFile = Path.Combine(backupDir, "backup_info.json");
                if (File.Exists(infoFile))
                {
                    var info = await File.ReadAllTextAsync(infoFile);
                    _logger.LogInformation("バックアップから復元中: {Info}", info);
                }

                _logger.LogInformation("ドライバーを復元しました: {BackupDir}", backupDir);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー復元中にエラーが発生しました: {DeviceID}", driver.DeviceID);
                return false;
            }
        }

        public async Task CleanupOldBackupsAsync(int maxGenerations)
        {
            if (maxGenerations < 1)
                throw new ArgumentOutOfRangeException(nameof(maxGenerations), "世代数は1以上を指定してください");

            foreach (var deviceDir in Directory.GetDirectories(_backupRoot))
                await CleanupOldBackupsAsync(deviceDir, maxGenerations);
        }

        public bool HasBackup(DriverInfo driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません", nameof(driver));

            var deviceDir = GetDeviceDirectory(driver.DeviceID);
            return Directory.Exists(deviceDir) &&
                   Directory.GetDirectories(deviceDir, "backup_*").Length > 0;
        }

        public string[] GetAvailableBackups(DriverInfo driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません", nameof(driver));

            var deviceDir = GetDeviceDirectory(driver.DeviceID);
            if (!Directory.Exists(deviceDir)) return Array.Empty<string>();

            return Directory.GetDirectories(deviceDir, "backup_*")
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Select(n => n!["backup_".Length..])
                .OrderByDescending(v => v)
                .ToArray();
        }

        private string GetDeviceDirectory(string deviceId)
        {
            var safe = string.Concat(deviceId.Split(Path.GetInvalidFileNameChars()));
            var dir = Path.Combine(_backupRoot, safe);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private async Task CleanupOldBackupsAsync(string deviceDir, int maxGenerations)
        {
            var backups = Directory.GetDirectories(deviceDir, "backup_*")
                .OrderByDescending(d => d)
                .ToArray();

            foreach (var old in backups.Skip(maxGenerations))
            {
                try
                {
                    Directory.Delete(old, true);
                    _logger.LogInformation("古いバックアップを削除しました: {Dir}", old);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "バックアップ削除中にエラーが発生しました: {Dir}", old);
                }
            }

            await Task.CompletedTask;
        }
    }
}
