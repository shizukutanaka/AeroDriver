using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// ドライバーのバックアップと復元を管理するサービス
    /// </summary>
    public class BackupService : IBackupService
    {
        private readonly ILogger<BackupService> _logger;
        private const string BackupRoot = "Backups";
        private const int DefaultMaxGenerations = 3;

        public BackupService(ILogger<BackupService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // バックアップディレクトリが存在しない場合は作成
            if (!Directory.Exists(BackupRoot))
            {
                Directory.CreateDirectory(BackupRoot);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> BackupDriverAsync(DriverInfo driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません。", nameof(driver.DeviceID));

            try
            {
                var deviceBackupDir = GetDeviceBackupDirectory(driver.DeviceID);
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var backupDir = Path.Combine(deviceBackupDir, $"backup_{timestamp}");

                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                // ここに実際のバックアップ処理を実装
                // 例: デバイスマネージャーからドライバーファイルをエクスポート
                // この例ではダミーのバックアップファイルを作成
                var backupInfo = new
                {
                    DeviceID = driver.DeviceID,
                    DeviceName = driver.DeviceName,
                    DriverVersion = driver.DriverVersion,
                    BackupTime = DateTime.Now,
                    Files = new[] { "driver.sys", "driver.inf", "driver.cat" }
                };

                var backupFile = Path.Combine(backupDir, "backup_info.json");
                await File.WriteAllTextAsync(backupFile, System.Text.Json.JsonSerializer.Serialize(backupInfo));

                _logger.LogInformation($"バックアップが作成されました: {backupDir}");

                // 古いバックアップをクリーンアップ
                await CleanupOldBackupsAsync(DefaultMaxGenerations);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ドライバーのバックアップ中にエラーが発生しました: {driver.DeviceID}");
                return false;
            }
        }


        /// <inheritdoc/>
        public async Task<bool> RestoreDriverAsync(DriverInfo driver, string backupVersion = null)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません。", nameof(driver.DeviceID));

            try
            {
                var deviceBackupDir = GetDeviceBackupDirectory(driver.DeviceID);
                
                if (!Directory.Exists(deviceBackupDir))
                {
                    _logger.LogWarning($"バックアップが見つかりません: {driver.DeviceID}");
                    return false;
                }

                string backupDir;
                
                if (string.IsNullOrEmpty(backupVersion))
                {
                    // 最新のバックアップを使用
                    var backups = Directory.GetDirectories(deviceBackupDir, "backup_*")
                        .OrderByDescending(d => d)
                        .FirstOrDefault();

                    if (string.IsNullOrEmpty(backups))
                    {
                        _logger.LogWarning($"復元可能なバックアップが見つかりません: {driver.DeviceID}");
                        return false;
                    }
                    
                    backupDir = backups;
                }
                else
                {
                    // 指定バージョンのバックアップを使用
                    backupDir = Path.Combine(deviceBackupDir, $"backup_{backupVersion}");
                    if (!Directory.Exists(backupDir))
                    {
                        _logger.LogWarning($"指定されたバージョンのバックアップが見つかりません: {backupVersion}");
                        return false;
                    }
                }

                // ここに実際の復元処理を実装
                // 例: バックアップからドライバーファイルを復元
                var backupInfoFile = Path.Combine(backupDir, "backup_info.json");
                if (File.Exists(backupInfoFile))
                {
                    var backupInfo = await File.ReadAllTextAsync(backupInfoFile);
                    _logger.LogInformation($"バックアップから復元中: {backupInfo}");
                }

                _logger.LogInformation($"ドライバーを復元しました: {backupDir}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ドライバーの復元中にエラーが発生しました: {driver.DeviceID}");
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task CleanupOldBackupsAsync(int maxGenerations)
        {
            if (maxGenerations < 1)
                throw new ArgumentOutOfRangeException(nameof(maxGenerations), "世代数は1以上を指定してください。");

            try
            {
                foreach (var deviceDir in Directory.GetDirectories(BackupRoot))
                {
                    var backups = Directory.GetDirectories(deviceDir, "backup_*")
                        .OrderByDescending(d => d)
                        .ToArray();

                    if (backups.Length > maxGenerations)
                    {
                        foreach (var oldBackup in backups.Skip(maxGenerations))
                        {
                            try
                            {
                                Directory.Delete(oldBackup, true);
                                _logger.LogInformation($"古いバックアップを削除しました: {oldBackup}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"バックアップの削除中にエラーが発生しました: {oldBackup}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "バックアップのクリーンアップ中にエラーが発生しました");
                throw;
            }
            
            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public bool HasBackup(DriverInfo driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません。", nameof(driver.DeviceID));

            var deviceBackupDir = GetDeviceBackupDirectory(driver.DeviceID);
            return Directory.Exists(deviceBackupDir) && 
                   Directory.GetDirectories(deviceBackupDir, "backup_*").Any();
        }

        /// <inheritdoc/>
        public string[] GetAvailableBackups(DriverInfo driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません。", nameof(driver.DeviceID));

            var deviceBackupDir = GetDeviceBackupDirectory(driver.DeviceID);
            
            if (!Directory.Exists(deviceBackupDir))
                return Array.Empty<string>();

            return Directory.GetDirectories(deviceBackupDir, "backup_*")
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Select(name => name!.Substring(7)) // "backup_" を除去
                .ToArray();
        }

        private string GetDeviceBackupDirectory(string deviceId)
        {
            // デバイスIDを安全なディレクトリ名に変換
            var safeDeviceId = string.Join("", deviceId.Split(Path.GetInvalidFileNameChars()));
            var deviceBackupDir = Path.Combine(BackupRoot, safeDeviceId);

            if (!Directory.Exists(deviceBackupDir))
            {
                Directory.CreateDirectory(deviceBackupDir);
            }

            return deviceBackupDir;
        }
    }
}
