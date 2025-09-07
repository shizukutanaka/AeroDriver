using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// ドライバーサービス（Carmack式：高性能化とMartin式：単一責任の統合）
    /// - データ指向設計によるメモリ効率
    /// - 責任分離によるメンテナンス性
    /// - キャッシュとバッチ処理による高速化
    /// </summary>
    public class DriverService : IDriverService, IDisposable
    {
        private readonly ILogger<DriverService> _logger;
        private readonly ISettingsService _settingsService;
        private readonly IBackupService _backupService;
        private readonly IWhqlDatabaseService _whqlDatabaseService;
        private readonly INotificationService _notificationService;
        private readonly HttpClient _httpClient;
        private readonly Cache.CacheManager _cacheManager;
        private readonly EfficientUpdateChecker _updateChecker;
        private readonly OptimizedWmiService _wmiService;
        private bool _disposed = false;


        /// <summary>
        /// 新しいインスタンスを初期化します
        /// </summary>
        public DriverService(
            ILogger<DriverService> logger,
            ISettingsService settingsService,
            IBackupService backupService,
            IWhqlDatabaseService whqlDatabaseService,
            INotificationService notificationService,
            OptimizedWmiService wmiService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _whqlDatabaseService = whqlDatabaseService ?? throw new ArgumentNullException(nameof(whqlDatabaseService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _wmiService = wmiService ?? throw new ArgumentNullException(nameof(wmiService));
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AeroDriver/1.0");
            _cacheManager = new Cache.CacheManager(_logger as ILogger<Cache.CacheManager>);
            _updateChecker = new EfficientUpdateChecker(_logger as ILogger<EfficientUpdateChecker>, _cacheManager);
        }

        /// <summary>
        /// システム内のすべてのドライバー情報を取得します（最適化版）
        /// </summary>
        public async Task<List<DriverInfo>> GetAllDriversAsync()
        {
            const string cacheKey = "all_drivers_optimized";
            var cachedDrivers = await _cacheManager.GetAsync<List<DriverInfo>>(cacheKey);
            if (cachedDrivers != null)
            {
                _logger.LogDebug("ドライバー情報をキャッシュから取得しました ({Count} 件)", cachedDrivers.Count);
                return cachedDrivers;
            }

            try
            {
                // 最適化されたWMIサービスを使用
                var drivers = await _wmiService.GetAllDevicesOptimizedAsync();
                
                _logger.LogInformation("最適化ドライバー取得完了: {Count} 件", drivers.Count);
                
                // 結果をキャッシュに保存（10分有効 - さらに高速化）
                if (drivers.Count > 0)
                {
                    await _cacheManager.SetAsync(cacheKey, drivers, TimeSpan.FromMinutes(10));
                }
                
                return drivers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "最適化ドライバー取得に失敗");
                return new List<DriverInfo>();
            }
        }


        /// <summary>
        /// 問題のあるドライバーを高速検出
        /// </summary>
        public async Task<List<DriverInfo>> GetProblematicDriversAsync()
        {
            try
            {
                var problems = await _wmiService.GetProblemDevicesAsync();
                var problematicDrivers = new List<DriverInfo>();
                
                foreach (var problem in problems)
                {
                    var driverInfo = await _wmiService.GetDeviceDetailsAsync(problem.DeviceId);
                    if (driverInfo != null)
                    {
                        problematicDrivers.Add(driverInfo);
                    }
                }
                
                return problematicDrivers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "問題ドライバー検出に失敗");
                return new List<DriverInfo>();
            }
        }

        /// <summary>
        /// ドライバークラス別統計を取得
        /// </summary>
        public async Task<Dictionary<string, int>> GetDriverStatisticsAsync()
        {
            try
            {
                var allDrivers = await _wmiService.GetAllDevicesOptimizedAsync();
                var stats = allDrivers
                    .GroupBy(d => d.DeviceClass ?? "Unknown")
                    .ToDictionary(g => g.Key, g => g.Count());
                
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー統計取得に失敗");
                return new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// ドライバーの更新を確認します（効率的）
        /// </summary>
        public async Task<List<DriverInfo>> CheckForUpdatesAsync()
        {
            _logger.LogInformation("効率的なドライバー更新確認を開始");
            
            try
            {
                var allDrivers = await GetAllDriversAsync();
                
                // 効率的な更新チェッカーを使用
                var updateResults = await _updateChecker.CheckMultipleUpdatesAsync(allDrivers);
                var updatesAvailable = updateResults.Values.ToList();
                
                if (updatesAvailable.Count > 0)
                {
                    UpdatesAvailable?.Invoke(this, new UpdatesAvailableEventArgs(updatesAvailable));
                    _notificationService.SendNotification("更新利用可能", 
                        $"{updatesAvailable.Count}個のドライバー更新が見つかりました", NotificationType.Info);
                }
                
                _logger.LogInformation("効率的な更新確認完了: {UpdateCount} 件の更新", updatesAvailable.Count);
                return updatesAvailable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "効率的な更新確認中にエラーが発生しました");
                
                // フォールバック: 従来の方法
                return await CheckForUpdatesLegacyAsync();
            }
        }

        /// <summary>
        /// 重要なドライバーのみの更新確認
        /// </summary>
        public async Task<List<DriverInfo>> CheckCriticalUpdatesAsync()
        {
            _logger.LogInformation("重要ドライバーの更新確認を開始");
            
            try
            {
                var allDrivers = await GetAllDriversAsync();
                var updateResults = await _updateChecker.CheckCriticalUpdatesAsync(allDrivers);
                var criticalUpdates = updateResults.Values.ToList();
                
                if (criticalUpdates.Count > 0)
                {
                    _notificationService.SendNotification("重要な更新利用可能", 
                        $"{criticalUpdates.Count}個の重要なドライバー更新が見つかりました", NotificationType.Warning);
                }
                
                _logger.LogInformation("重要ドライバー更新確認完了: {UpdateCount} 件", criticalUpdates.Count);
                return criticalUpdates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重要ドライバー更新確認エラー");
                return new List<DriverInfo>();
            }
        }

        /// <summary>
        /// 問題のあるドライバーの更新確認
        /// </summary>
        public async Task<List<DriverInfo>> CheckProblematicDriverUpdatesAsync()
        {
            _logger.LogInformation("問題ドライバーの更新確認を開始");
            
            try
            {
                var problematicDrivers = await GetProblematicDriversAsync();
                
                if (problematicDrivers.Count == 0)
                {
                    _logger.LogInformation("問題のあるドライバーは見つかりませんでした");
                    return new List<DriverInfo>();
                }

                var updateResults = await _updateChecker.CheckProblematicDriverUpdatesAsync(problematicDrivers);
                var updates = updateResults.Values.ToList();
                
                if (updates.Count > 0)
                {
                    _notificationService.SendNotification("問題ドライバーの更新", 
                        $"{updates.Count}個の問題ドライバーに更新が利用可能です", NotificationType.Error);
                }
                
                _logger.LogInformation("問題ドライバー更新確認完了: {UpdateCount} 件", updates.Count);
                return updates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "問題ドライバー更新確認エラー");
                return new List<DriverInfo>();
            }
        }

        /// <summary>
        /// 従来の更新確認方法（フォールバック用）
        /// </summary>
        private async Task<List<DriverInfo>> CheckForUpdatesLegacyAsync()
        {
            _logger.LogInformation("レガシー更新確認を実行");
            
            var allDrivers = await GetAllDriversAsync();
            var updatesAvailable = new List<DriverInfo>();
            
            // 並列処理で更新確認を高速化（最大5並列に削減）
            var semaphore = new SemaphoreSlim(5, 5);
            var updateTasks = allDrivers.Take(20).Select(async driver => // 最大20個に制限
            {
                await semaphore.WaitAsync();
                try
                {
                    var availableUpdate = await _whqlDatabaseService.FindAvailableUpdateAsync(driver);
                    if (availableUpdate != null && IsNewerVersion(availableUpdate.DriverVersion, driver.DriverVersion))
                    {
                        lock (updatesAvailable)
                        {
                            updatesAvailable.Add(availableUpdate);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "レガシー更新確認エラー: {DeviceName}", driver.DeviceName);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(updateTasks);
            
            _logger.LogInformation("レガシー更新確認完了: {UpdateCount} 件", updatesAvailable.Count);
            return updatesAvailable;
        }

        /// <summary>
        /// ドライバーをダウンロードしてインストールします
        /// </summary>
        public async Task<bool> InstallDriverUpdateAsync(DriverInfo driverUpdate)
        {
            try
            {
                _logger.LogInformation("ドライバー更新を開始: {DeviceName}", driverUpdate.DeviceName);
                
                if (_settingsService.BackupEnabled)
                {
                    await _backupService.CreateBackupAsync();
                }
                
                _notificationService.ShowProgress($"ドライバー更新中: {driverUpdate.DeviceName}", 20);
                
                var retryService = new RetryService(_logger as ILogger<RetryService>);
                
                var downloadPath = await retryService.ExecuteWithRetryAsync(
                    () => DownloadDriverAsync(driverUpdate),
                    maxRetries: 2);
                    
                if (string.IsNullOrEmpty(downloadPath))
                {
                    return false;
                }
                
                _notificationService.ShowProgress($"インストール中: {driverUpdate.DeviceName}", 60);
                
                var installResult = await retryService.ExecuteWithRetryAsync(
                    () => InstallDriverFromPathAsync(downloadPath, driverUpdate),
                    maxRetries: 1); // インストールは1回だけリトライ
                
                _notificationService.HideProgress();
                
                var eventArgs = new UpdatesInstalledEventArgs(driverUpdate, installResult, 
                    installResult ? null : "インストールに失敗しました");
                UpdatesInstalled?.Invoke(this, eventArgs);
                
                if (installResult)
                {
                    _notificationService.SendNotification("更新完了", 
                        $"{driverUpdate.DeviceName} の更新が完了しました", NotificationType.Success);
                }
                else
                {
                    _notificationService.SendNotification("更新失敗", 
                        $"{driverUpdate.DeviceName} の更新に失敗しました", NotificationType.Error);
                }
                
                return installResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー更新エラー: {DeviceName}", driverUpdate.DeviceName);
                _notificationService.HideProgress();
                return false;
            }
        }

        /// <summary>
        /// ドライバーを無効化します
        /// </summary>
        public async Task<bool> DisableDriverAsync(string deviceId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "pnputil",
                        Arguments = $"/disable-device \"{deviceId}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });
                    
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ドライバー無効化エラー: {DeviceID}", deviceId);
                    return false;
                }
            });
        }

        /// <summary>
        /// ドライバーを有効化します
        /// </summary>
        public async Task<bool> EnableDriverAsync(string deviceId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "pnputil",
                        Arguments = $"/enable-device \"{deviceId}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });
                    
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ドライバー有効化エラー: {DeviceID}", deviceId);
                    return false;
                }
            });
        }

        /// <summary>
        /// ドライバーの詳細情報を取得します
        /// </summary>
        public async Task<DriverDetailInfo> GetDriverDetailsAsync(string deviceId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{deviceId.Replace("\\", "\\\\")}'");
                    using var collection = searcher.Get();
                    
                    var device = collection.Cast<ManagementObject>().FirstOrDefault();
                    return device != null ? CreateDriverDetailInfoFromDevice(device) : null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ドライバー詳細情報取得エラー: {DeviceID}", deviceId);
                    return null;
                }
            });
        }

        /// <summary>
        /// カスタムドライバーをインストールします
        /// </summary>
        public async Task<bool> InstallCustomDriverAsync(string driverPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "pnputil",
                        Arguments = $"/add-driver \"{driverPath}\" /install",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });
                    
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "カスタムドライバーインストールエラー: {DriverPath}", driverPath);
                    return false;
                }
            });
        }

        /// <summary>
        /// WMIデバイスオブジェクトからDriverInfoを作成します
        /// </summary>
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
                    DriverVersion = GetDeviceDriverVersion(deviceId),
                    DriverProviderName = device.Properties["Manufacturer"]?.Value?.ToString() ?? "Unknown",
                    HardwareID = GetDeviceHardwareId(deviceId),
                    IsWHQLCertified = IsDriverWHQLCertified(deviceId),
                    DeviceClass = device.Properties["PNPClass"]?.Value?.ToString() ?? "Unknown"
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DriverInfo作成エラー");
                return null;
            }
        }

        /// <summary>
        /// WMIデバイスオブジェクトからDriverDetailInfoを作成します
        /// </summary>
        private DriverDetailInfo CreateDriverDetailInfoFromDevice(ManagementObject device)
        {
            var driverInfo = CreateDriverInfoFromDevice(device);
            if (driverInfo == null) return null;

            return new DriverDetailInfo
            {
                DeviceID = driverInfo.DeviceID,
                DeviceName = driverInfo.DeviceName,
                DriverVersion = driverInfo.DriverVersion,
                DriverProviderName = driverInfo.DriverProviderName,
                HardwareID = driverInfo.HardwareID,
                IsWHQLCertified = driverInfo.IsWHQLCertified,
                Status = device.Properties["Status"]?.Value?.ToString() ?? "Unknown",
                StatusInfo = GetDeviceStatusInfo(device),
                ClassGuid = device.Properties["ClassGuid"]?.Value?.ToString(),
                DeviceClass = device.Properties["PNPClass"]?.Value?.ToString(),
                Manufacturer = device.Properties["Manufacturer"]?.Value?.ToString(),
                Description = device.Properties["Description"]?.Value?.ToString(),
                Properties = GetDeviceProperties(device)
            };
        }

        /// <summary>
        /// デバイスのドライバーバージョンを取得します
        /// </summary>
        private string GetDeviceDriverVersion(string deviceId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_SystemDriver WHERE Name LIKE '%{deviceId.Split('\\').Last()}%'");
                using var collection = searcher.Get();
                
                var driver = collection.Cast<ManagementObject>().FirstOrDefault();
                return driver?.Properties["Version"]?.Value?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// デバイスのハードウェアIDを取得します
        /// </summary>
        private string GetDeviceHardwareId(string deviceId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{deviceId.Replace("\\", "\\\\")}'");
                using var collection = searcher.Get();
                
                var device = collection.Cast<ManagementObject>().FirstOrDefault();
                var hardwareIds = device?.Properties["HardwareID"]?.Value as string[];
                return hardwareIds?.FirstOrDefault() ?? deviceId;
            }
            catch
            {
                return deviceId;
            }
        }

        /// <summary>
        /// ドライバーのWHQL認証状態を確認します
        /// </summary>
        private bool IsDriverWHQLCertified(string deviceId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT IsSigned FROM Win32_PnPSignedDriver WHERE DeviceID='{deviceId.Replace("\\", "\\\\")}'");
                using var collection = searcher.Get();
                
                var driver = collection.Cast<ManagementObject>().FirstOrDefault();
                if (driver?.Properties["IsSigned"]?.Value is bool isSigned)
                {
                    using (driver)
                    {
                        return isSigned;
                    }
                }
                
                // フォールバック: デジタル署名の存在確認
                return CheckDriverSignature(deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WHQL認証確認エラー: {DeviceID}", deviceId);
                return false;
            }
        }

        /// <summary>
        /// ドライバーファイルのデジタル署名をチェック
        /// </summary>
        private bool CheckDriverSignature(string deviceId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT PathName FROM Win32_SystemDriver WHERE Name LIKE '%{deviceId.Split('\\').Last()}%'");
                using var collection = searcher.Get();
                
                foreach (ManagementObject driver in collection)
                {
                    using (driver)
                    {
                        var pathName = driver.Properties["PathName"]?.Value?.ToString();
                        if (!string.IsNullOrEmpty(pathName) && File.Exists(pathName))
                        {
                            try
                            {
                                X509Certificate.CreateFromSignedFile(pathName);
                                return true;
                            }
                            catch
                            {
                                continue;
                            }
                        }
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// デバイスステータス情報を取得します
        /// </summary>
        private int GetDeviceStatusInfo(ManagementObject device)
        {
            try
            {
                var configManagerErrorCode = device.Properties["ConfigManagerErrorCode"]?.Value;
                return configManagerErrorCode == null ? 1 : Convert.ToInt32(configManagerErrorCode);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// デバイスプロパティを取得します
        /// </summary>
        private Dictionary<string, string> GetDeviceProperties(ManagementObject device)
        {
            var properties = new Dictionary<string, string>();
            
            try
            {
                foreach (var property in device.Properties)
                {
                    var value = property.Value?.ToString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        properties[property.Name] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "デバイスプロパティ取得エラー");
            }
            
            return properties;
        }

        /// <summary>
        /// ドライバーファイルをダウンロードします
        /// </summary>
        private async Task<string> DownloadDriverAsync(DriverInfo driverInfo)
        {
            try
            {
                if (string.IsNullOrEmpty(driverInfo.DownloadUrl))
                    return null;

                var tempPath = Path.Combine(Path.GetTempPath(), "AeroDriver", Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempPath);

                var fileName = Path.GetFileName(driverInfo.DownloadUrl) ?? "driver.zip";
                var filePath = Path.Combine(tempPath, fileName);

                using var response = await _httpClient.GetAsync(driverInfo.DownloadUrl);
                response.EnsureSuccessStatusCode();

                await using var fileStream = File.Create(filePath);
                await response.Content.CopyToAsync(fileStream);

                _logger.LogInformation("ドライバーダウンロード完了: {FilePath}", filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバーダウンロードエラー: {DownloadUrl}", driverInfo.DownloadUrl);
                return null;
            }
        }

        /// <summary>
        /// ドライバーファイルからインストールします
        /// </summary>
        private async Task<bool> InstallDriverFromPathAsync(string driverPath, DriverInfo driverInfo)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // セキュリティチェック: パスの安全性を確認
                    if (string.IsNullOrEmpty(driverPath))
                    {
                        _logger.LogError("ドライバーパスが指定されていません");
                        return false;
                    }
                    
                    if (!File.Exists(driverPath))
                    {
                        _logger.LogError("ドライバーファイルが存在しません: {DriverPath}", driverPath);
                        return false;
                    }

                    // ファイル整合性チェック
                    if (!Utilities.FileOperations.ValidateFileIntegrity(driverPath))
                    {
                        _logger.LogError("ドライバーファイルが破損している可能性があります: {DriverPath}", driverPath);
                        return false;
                    }

                    // ファイルサイズチェック（異常に大きいファイルを拒否）
                    var fileSize = Utilities.FileOperations.GetFileSize(driverPath);
                    if (fileSize > 500 * 1024 * 1024) // 500MB制限
                    {
                        _logger.LogError("ドライバーファイルが大きすぎます: {Size} MB", fileSize / 1024 / 1024);
                        return false;
                    }

                    // ファイルのデジタル署名を確認（セキュリティ）
                    try
                    {
                        var certificate = X509Certificate.CreateFromSignedFile(driverPath);
                        _logger.LogInformation("ドライバーファイルの署名確認成功: {Issuer}", certificate.Issuer);
                    }
                    catch (Exception)
                    {
                        _logger.LogWarning("ドライバーファイルの署名確認に失敗: {DriverPath}", driverPath);
                        if (!_settingsService.IncludeBetaDrivers) // 署名なしドライバーの拒否
                        {
                            return false;
                        }
                    }

                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "pnputil",
                        Arguments = $"/add-driver \"{driverPath}\" /install",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        Verb = "runas" // 管理者権限で実行
                    });

                    if (process == null)
                    {
                        _logger.LogError("pnputilプロセスの開始に失敗");
                        return false;
                    }

                    process.WaitForExit();
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError("pnputilエラー: {Error}", error);
                    }
                    else
                    {
                        _logger.LogInformation("ドライバーインストール成功: {Output}", output);
                    }

                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ドライバーインストールエラー: {DriverPath}", driverPath);
                    return false;
                }
            });
        }

        /// <summary>
        /// バージョンが新しいかどうかを確認します
        /// </summary>
        private bool IsNewerVersion(string newVersion, string currentVersion)
        {
            return Utilities.VersionHelper.IsNewer(newVersion, currentVersion);
        }


        /// <summary>
        /// ドライバーをロールバックします
        /// </summary>
        public async Task<bool> RollbackDriverAsync(string deviceId)
        {
            try
            {
                _logger.LogInformation("ドライバーをロールバックします: {DeviceID}", deviceId);
                
                // バックアップリストを取得
                var backups = await _backupService.GetBackupsAsync();
                
                if (backups.Count == 0)
                {
                    _logger.LogWarning("利用可能なバックアップがありません");
                    return false;
                }
                
                // 最新のバックアップを使用
                var latestBackup = backups.OrderByDescending(b => b.CreationDate).First();
                
                // バックアップから復元
                bool result = await _backupService.RestoreBackupAsync(latestBackup.Id);
                
                if (result)
                {
                    _logger.LogInformation("ドライバーのロールバックが成功しました: {DeviceID}", deviceId);
                    
                    // 通知を送信
                    _notificationService.SendNotification(
                        "ドライバーロールバック完了",
                        "ドライバーが正常に以前のバージョンに戻されました。",
                        NotificationType.Success);
                }
                else
                {
                    _logger.LogError("ドライバーのロールバックに失敗しました: {DeviceID}", deviceId);
                    
                    // 通知を送信
                    _notificationService.SendNotification(
                        "ドライバーロールバック失敗",
                        "ドライバーを以前のバージョンに戻せませんでした。",
                        NotificationType.Error);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバーのロールバック中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                    _cacheManager?.Dispose();
                    _updateChecker?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
