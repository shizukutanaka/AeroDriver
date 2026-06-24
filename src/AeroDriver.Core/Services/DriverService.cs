using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Events;
using AeroDriver.Core.Helpers;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    public class DriverService : IDriverService, IDisposable
    {
        private readonly ILogger<DriverService> _logger;
        private readonly ISettingsService _settingsService;
        private readonly IBackupService _backupService;
        private readonly IWhqlDatabaseService _whqlDatabaseService;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public event EventHandler<UpdatesAvailableEventArgs> UpdatesAvailable;
        public event EventHandler<UpdatesInstalledEventArgs> UpdatesInstalled;

        public DriverService(
            ILogger<DriverService> logger,
            ISettingsService settingsService,
            IBackupService backupService,
            IWhqlDatabaseService whqlDatabaseService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _whqlDatabaseService = whqlDatabaseService ?? throw new ArgumentNullException(nameof(whqlDatabaseService));
            _httpClient = new HttpClient();
        }

        public async Task<List<DriverInfo>> GetAllDriversAsync(CancellationToken cancellationToken = default)
        {
            var drivers = new List<DriverInfo>();

            try
            {
                await Task.Run(() =>
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT * FROM Win32_PnPSignedDriver WHERE DriverVersion IS NOT NULL");

                    foreach (ManagementObject obj in searcher.Get())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var driver = new DriverInfo
                        {
                            DeviceID = obj["DeviceID"]?.ToString(),
                            DeviceName = obj["DeviceName"]?.ToString(),
                            DriverVersion = obj["DriverVersion"]?.ToString(),
                            DriverProviderName = obj["DriverProviderName"]?.ToString(),
                            InfName = obj["InfName"]?.ToString(),
                            HardwareID = obj["HardwareID"]?.ToString(),
                            IsWHQLCertified = obj["IsSigned"] is bool signed && signed,
                        };

                        if (DateTime.TryParse(obj["DriverDate"]?.ToString(), out var date))
                            driver.DriverDate = date;

                        drivers.Add(driver);
                    }
                }, cancellationToken);

                _logger.LogInformation("{Count} 件のドライバーを検出しました", drivers.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー一覧の取得中にエラーが発生しました");
            }

            return drivers;
        }

        public async Task<List<DriverInfo>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            var updates = new List<DriverInfo>();

            try
            {
                var installed = await GetAllDriversAsync(cancellationToken);

                foreach (var driver in installed)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(driver.HardwareID)) continue;

                    var latest = await _whqlDatabaseService.FindDriverByHardwareIdAsync(driver.HardwareID);
                    if (latest == null) continue;

                    if (VersionHelper.IsNewer(latest.DriverVersion, driver.DriverVersion))
                    {
                        latest.DeviceID = driver.DeviceID;
                        latest.DeviceName = driver.DeviceName;
                        updates.Add(latest);
                    }
                }

                if (updates.Count > 0)
                    UpdatesAvailable?.Invoke(this, new UpdatesAvailableEventArgs(updates));

                _logger.LogInformation("{Count} 件の更新が見つかりました", updates.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー更新確認中にエラーが発生しました");
            }

            return updates;
        }

        public async Task<bool> InstallDriverUpdateAsync(DriverInfo driverUpdate, CancellationToken cancellationToken = default)
        {
            if (driverUpdate == null) throw new ArgumentNullException(nameof(driverUpdate));

            try
            {
                _logger.LogInformation("ドライバーをインストールします: {DeviceName} {Version}",
                    driverUpdate.DeviceName, driverUpdate.DriverVersion);

                if (_settingsService.BackupEnabled)
                    await _backupService.BackupDriverAsync(driverUpdate);

                if (string.IsNullOrEmpty(driverUpdate.DownloadUrl))
                {
                    _logger.LogWarning("ダウンロードURLが指定されていません: {DeviceID}", driverUpdate.DeviceID);
                    return false;
                }

                var tempPath = Path.Combine(Path.GetTempPath(), $"aerodriver_{Guid.NewGuid():N}.tmp");
                try
                {
                    var response = await _httpClient.GetAsync(driverUpdate.DownloadUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    await File.WriteAllBytesAsync(tempPath, await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);

                    bool success = await InstallFromFileAsync(tempPath, driverUpdate.InstallerType, cancellationToken);
                    UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, success));
                    return success;
                }
                finally
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバーインストール中にエラーが発生しました: {DeviceID}", driverUpdate.DeviceID);
                UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, false, ex.Message));
                return false;
            }
        }

        public async Task<bool> RollbackDriverAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));

            try
            {
                _logger.LogInformation("ドライバーをロールバックします: {DeviceID}", deviceId);

                var driver = new DriverInfo { DeviceID = deviceId };

                if (!_backupService.HasBackup(driver))
                {
                    _logger.LogWarning("デバイス {DeviceID} のバックアップが見つかりません", deviceId);
                    return false;
                }

                bool result = await _backupService.RestoreDriverAsync(driver);

                if (result)
                    _logger.LogInformation("ロールバック完了: {DeviceID}", deviceId);
                else
                    _logger.LogError("ロールバック失敗: {DeviceID}", deviceId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ロールバック中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        public async Task<bool> DisableDriverAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));

            try
            {
                _logger.LogInformation("ドライバーを無効化します: {DeviceID}", deviceId);

                bool result = await Task.Run(() => SetDriverState(deviceId, enable: false), cancellationToken);
                _logger.LogInformation("ドライバー無効化 {Result}: {DeviceID}", result ? "成功" : "失敗", deviceId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー無効化中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        public async Task<bool> EnableDriverAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));

            try
            {
                _logger.LogInformation("ドライバーを有効化します: {DeviceID}", deviceId);

                bool result = await Task.Run(() => SetDriverState(deviceId, enable: true), cancellationToken);
                _logger.LogInformation("ドライバー有効化 {Result}: {DeviceID}", result ? "成功" : "失敗", deviceId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー有効化中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        public async Task<DriverDetailInfo> GetDriverDetailsAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));

            try
            {
                return await Task.Run(() =>
                {
                    using var searcher = new ManagementObjectSearcher(
                        $"SELECT * FROM Win32_PnPSignedDriver WHERE DeviceID = '{deviceId.Replace("'", "''")}'");

                    foreach (ManagementObject obj in searcher.Get())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var detail = new DriverDetailInfo
                        {
                            DeviceID = obj["DeviceID"]?.ToString(),
                            DeviceName = obj["DeviceName"]?.ToString(),
                            DriverVersion = obj["DriverVersion"]?.ToString(),
                            DriverProviderName = obj["DriverProviderName"]?.ToString(),
                            InfName = obj["InfName"]?.ToString(),
                            HardwareID = obj["HardwareID"]?.ToString(),
                            IsWHQLCertified = obj["IsSigned"] is bool signed && signed,
                            Manufacturer = obj["Manufacturer"]?.ToString(),
                            DeviceClass = obj["DeviceClass"]?.ToString(),
                            ClassGuid = obj["DeviceClassGUID"]?.ToString(),
                            Description = obj["Description"]?.ToString(),
                            Status = obj["Status"]?.ToString(),
                        };

                        if (DateTime.TryParse(obj["DriverDate"]?.ToString(), out var date))
                            detail.DriverDate = date;

                        if (int.TryParse(obj["ConfigManagerErrorCode"]?.ToString(), out int errCode))
                            detail.StatusInfo = errCode == 0 ? 1 : 3;

                        return detail;
                    }

                    return null;
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー詳細取得中にエラーが発生しました: {DeviceID}", deviceId);
                return null;
            }
        }

        public async Task<bool> InstallCustomDriverAsync(string driverPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(driverPath)) throw new ArgumentException("ドライバーパスが必要です", nameof(driverPath));
            if (!File.Exists(driverPath)) throw new FileNotFoundException("ドライバーファイルが見つかりません", driverPath);

            try
            {
                _logger.LogInformation("カスタムドライバーをインストールします: {Path}", driverPath);
                string ext = Path.GetExtension(driverPath).ToLowerInvariant();
                return await InstallFromFileAsync(driverPath, ext.TrimStart('.'), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "カスタムドライバーインストール中にエラーが発生しました: {Path}", driverPath);
                return false;
            }
        }

        // --- non-interface public method kept for backward compatibility ---
        public int CompareVersions(string version1, string version2) => VersionHelper.Compare(version1, version2);

        private static bool SetDriverState(string deviceId, bool enable)
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_PnPEntity WHERE DeviceID = '{deviceId.Replace("'", "''")}'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var method = enable ? "Enable" : "Disable";
                var result = obj.InvokeMethod(method, null);
                return result != null;
            }

            return false;
        }

        private async Task<bool> InstallFromFileAsync(string filePath, string installerType, CancellationToken cancellationToken)
        {
            var ext = (installerType ?? Path.GetExtension(filePath)).ToLowerInvariant().TrimStart('.');

            string args = ext switch
            {
                "inf" => $"/c pnputil /add-driver \"{filePath}\" /install",
                "exe" => $"/c \"{filePath}\" /quiet /norestart",
                "msi" => $"/c msiexec /i \"{filePath}\" /quiet /norestart",
                _ => null
            };

            if (args == null)
            {
                _logger.LogWarning("未対応のインストーラー形式: {Type}", ext);
                return false;
            }

            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing) _httpClient?.Dispose();
            _disposed = true;
        }
    }
}
