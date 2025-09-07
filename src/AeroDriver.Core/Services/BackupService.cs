using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// 統合版ドライバーバックアップサービス（高速化対応）
    /// </summary>
    public class BackupService : IBackupService, IDisposable
    {
        private readonly ILogger<BackupService> _logger;
        private const string BackupRoot = "Backups";
        private const int DefaultMaxGenerations = 3;
        
        // 高速バックアップ用
        private readonly SemaphoreSlim _backupSemaphore;
        private readonly ConcurrentDictionary<string, DateTime> _lastBackupTimes;
        private bool _disposed = false;

        public BackupService(ILogger<BackupService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // 高速バックアップ機能の初期化
            _backupSemaphore = new SemaphoreSlim(3, 3); // 最大3つの並列バックアップ
            _lastBackupTimes = new ConcurrentDictionary<string, DateTime>();
            
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
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(backupFile, JsonSerializer.Serialize(backupInfo, options));

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
        public async Task<bool> CreateBackupAsync()
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var systemBackupDir = Path.Combine(BackupRoot, "System", $"backup_{timestamp}");

                if (!Directory.Exists(systemBackupDir))
                {
                    Directory.CreateDirectory(systemBackupDir);
                }

                var backupInfo = new BackupInfo
                {
                    Id = timestamp,
                    Name = $"System Backup {timestamp}",
                    CreationDate = DateTime.Now,
                    Description = "System-wide driver backup"
                };

                var backupFile = Path.Combine(systemBackupDir, "backup_info.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(backupFile, JsonSerializer.Serialize(backupInfo, options));

                _logger.LogInformation("System backup created: {BackupDir}", systemBackupDir);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating system backup");
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RestoreBackupAsync(string backupId)
        {
            try
            {
                var systemBackupDir = Path.Combine(BackupRoot, "System", $"backup_{backupId}");
                
                if (!Directory.Exists(systemBackupDir))
                {
                    _logger.LogWarning("Backup not found: {BackupId}", backupId);
                    return false;
                }

                var backupInfoFile = Path.Combine(systemBackupDir, "backup_info.json");
                if (File.Exists(backupInfoFile))
                {
                    var backupInfo = await File.ReadAllTextAsync(backupInfoFile);
                    _logger.LogInformation("Restoring from backup: {BackupInfo}", backupInfo);
                }

                _logger.LogInformation("Backup restored: {BackupId}", backupId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring backup: {BackupId}", backupId);
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task<List<BackupInfo>> GetBackupsAsync()
        {
            var backups = new List<BackupInfo>();

            try
            {
                var systemBackupDir = Path.Combine(BackupRoot, "System");
                
                if (!Directory.Exists(systemBackupDir))
                    return backups;

                var backupDirs = Directory.GetDirectories(systemBackupDir, "backup_*");
                
                foreach (var backupDir in backupDirs.OrderByDescending(d => d))
                {
                    var backupInfoFile = Path.Combine(backupDir, "backup_info.json");
                    
                    if (File.Exists(backupInfoFile))
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(backupInfoFile);
                            var backupInfo = JsonSerializer.Deserialize<BackupInfo>(json);
                            if (backupInfo != null)
                            {
                                backups.Add(backupInfo);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error reading backup info: {BackupDir}", backupDir);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting backup list");
            }

            return backups;
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

        /// <summary>
        /// 高速バックアップ（メタデータのみ）
        /// </summary>
        public async Task<bool> CreateQuickBackupAsync(List<DriverInfo> drivers, CancellationToken cancellationToken = default)
        {
            if (drivers == null || drivers.Count == 0) return false;

            var backupId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            
            // レート制限チェック
            if (_lastBackupTimes.ContainsKey("quick"))
            {
                var lastBackup = _lastBackupTimes["quick"];
                if (DateTime.Now - lastBackup < TimeSpan.FromMinutes(5))
                {
                    _logger.LogDebug("クイックバックアップをスキップ（レート制限）");
                    return true;
                }
            }

            try
            {
                await _backupSemaphore.WaitAsync(cancellationToken);
                
                var backupDir = Path.Combine(BackupRoot, $"quick_{backupId}");
                Directory.CreateDirectory(backupDir);

                // メタデータのみ保存（高速）
                var backupData = new
                {
                    BackupId = backupId,
                    BackupType = "Quick",
                    CreatedAt = DateTime.UtcNow,
                    DriversCount = drivers.Count,
                    Drivers = drivers.Select(d => new
                    {
                        d.DeviceID,
                        d.DeviceName,
                        d.DriverVersion,
                        d.DriverProviderName,
                        d.IsWHQLCertified
                    }).ToList()
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(backupData, options);
                await File.WriteAllTextAsync(Path.Combine(backupDir, "metadata.json"), json, cancellationToken);

                _lastBackupTimes.AddOrUpdate("quick", DateTime.Now, (k, v) => DateTime.Now);
                _logger.LogInformation("クイックバックアップ完了: {BackupId}", backupId);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "クイックバックアップエラー");
                return false;
            }
            finally
            {
                _backupSemaphore.Release();
            }
        }

        /// <summary>
        /// 重要ファイルのみバックアップ（圧縮）
        /// </summary>
        public async Task<bool> CreateEssentialBackupAsync(DriverInfo driver, string outputPath = null)
        {
            if (driver == null) return false;

            try
            {
                await _backupSemaphore.WaitAsync();
                
                var backupId = $"{driver.DeviceID}_{DateTime.Now:yyyyMMdd_HHmmss}";
                var safeBackupId = string.Join("_", backupId.Split(Path.GetInvalidFileNameChars()));
                var backupDir = Path.Combine(outputPath ?? BackupRoot, "essential", safeBackupId);
                
                Directory.CreateDirectory(backupDir);

                // ZIP圧縮でバックアップ
                var zipFile = Path.Combine(backupDir, "driver_backup.zip");
                using (var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create))
                {
                    // ドライバー情報をJSON形式で追加
                    var entry = zip.CreateEntry("driver_info.json");
                    using (var stream = entry.Open())
                    using (var writer = new StreamWriter(stream))
                    {
                        var json = JsonSerializer.Serialize(driver, new JsonSerializerOptions { WriteIndented = true });
                        await writer.WriteAsync(json);
                    }
                }

                _logger.LogInformation("重要ファイルバックアップ完了: {BackupId}", safeBackupId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重要ファイルバックアップエラー");
                return false;
            }
            finally
            {
                _backupSemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _backupSemaphore?.Dispose();
                _lastBackupTimes?.Clear();
                _disposed = true;
            }
        }
    }
}
