using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// 自動更新サービス
    /// </summary>
    public class AutoUpdateService : IAutoUpdateService, IDisposable
    {
        private readonly IDriverService _driverService;
        private readonly IBackupService _backupService;
        private readonly IBackgroundTaskService _backgroundTaskService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<AutoUpdateService>? _logger;
        private readonly ConcurrentQueue<UpdateHistory> _updateHistory;
        
        private AutoUpdateOptions? _currentOptions;
        private string? _backgroundTaskId;
        private AutoUpdateStatus _status;
        private readonly object _lockObject = new();
        private bool _disposed;
        
        public AutoUpdateService(
            IDriverService driverService,
            IBackupService backupService,
            IBackgroundTaskService backgroundTaskService,
            ISettingsService settingsService,
            ILogger<AutoUpdateService>? logger = null)
        {
            _driverService = driverService ?? throw new ArgumentNullException(nameof(driverService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _backgroundTaskService = backgroundTaskService ?? throw new ArgumentNullException(nameof(backgroundTaskService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger;
            _updateHistory = new ConcurrentQueue<UpdateHistory>();
            _status = new AutoUpdateStatus();
        }
        
        public async Task<string> StartAutoUpdateAsync(AutoUpdateOptions options)
        {
            lock (_lockObject)
            {
                if (_status.IsRunning)
                {
                    throw new InvalidOperationException("Auto update is already running");
                }
                
                _currentOptions = options;
                _status = new AutoUpdateStatus
                {
                    IsRunning = true,
                    StartedAt = DateTime.UtcNow
                };
            }
            
            _logger?.LogInformation("Starting auto update with interval: {Interval}", options.CheckInterval);
            
            // バックグラウンドタスクをスケジュール
            _backgroundTaskId = await _backgroundTaskService.ScheduleRecurringTaskAsync(
                "AutoUpdateCheck",
                async (ct) => await PerformUpdateCheckAsync(ct),
                options.CheckInterval);
            
            // 初回チェックを即座に実行
            _ = Task.Run(async () => await PerformUpdateCheckAsync(CancellationToken.None));
            
            return _backgroundTaskId;
        }
        
        public async Task StopAutoUpdateAsync()
        {
            lock (_lockObject)
            {
                if (!_status.IsRunning)
                {
                    return;
                }
                
                _status.IsRunning = false;
            }
            
            if (!string.IsNullOrEmpty(_backgroundTaskId))
            {
                await _backgroundTaskService.CancelTaskAsync(_backgroundTaskId);
                _backgroundTaskId = null;
            }
            
            _logger?.LogInformation("Auto update stopped");
        }
        
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new UpdateCheckResult
            {
                CheckedAt = DateTime.UtcNow
            };
            
            try
            {
                _logger?.LogDebug("Checking for driver updates");
                
                // ドライバー更新をスキャン
                var availableUpdates = await _driverService.ScanForDriversAsync();
                
                // フィルタリング
                if (_currentOptions != null)
                {
                    if (_currentOptions.OnlyWHQLCertified)
                    {
                        availableUpdates = availableUpdates.Where(d => d.IsWHQLCertified).ToList();
                    }
                    
                    if (_currentOptions.ExcludedDeviceIds.Any())
                    {
                        availableUpdates = availableUpdates
                            .Where(d => !_currentOptions.ExcludedDeviceIds.Contains(d.DeviceID))
                            .ToList();
                    }
                }
                
                // 更新情報を作成
                foreach (var update in availableUpdates)
                {
                    result.Updates.Add(new DriverUpdateInfo
                    {
                        DeviceId = update.DeviceID,
                        DeviceName = update.DeviceName,
                        CurrentVersion = update.DriverVersion,
                        NewVersion = update.DriverVersion, // 実際には新バージョン情報が必要
                        ReleaseDate = DateTime.UtcNow, // 実際のリリース日が必要
                        IsWHQLCertified = update.IsWHQLCertified,
                        Priority = DeterminePriority(update)
                    });
                }
                
                result.AvailableUpdates = result.Updates.Count;
                result.Success = true;
                
                _logger?.LogInformation("Update check completed: {Count} updates available", result.AvailableUpdates);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger?.LogError(ex, "Error checking for updates");
            }
            finally
            {
                stopwatch.Stop();
                result.CheckDuration = stopwatch.Elapsed;
            }
            
            lock (_lockObject)
            {
                _status.LastCheckAt = result.CheckedAt;
                _status.UpdatesPending = result.AvailableUpdates;
                
                if (_currentOptions != null)
                {
                    _status.NextCheckAt = result.CheckedAt.Add(_currentOptions.CheckInterval);
                }
            }
            
            return result;
        }
        
        public async Task<UpdateResult> ApplyUpdatesAsync(IEnumerable<string> deviceIds, bool createBackup = true)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new UpdateResult
            {
                UpdatedAt = DateTime.UtcNow
            };
            
            foreach (var deviceId in deviceIds)
            {
                result.TotalAttempted++;
                var itemResult = new UpdateItemResult
                {
                    DeviceId = deviceId
                };
                
                try
                {
                    // バックアップを作成
                    if (createBackup)
                    {
                        _logger?.LogDebug("Creating backup for device: {DeviceId}", deviceId);
                        var backupSuccess = await _backupService.CreateBackupAsync(deviceId);
                        
                        if (backupSuccess)
                        {
                            var backups = await _backupService.GetBackupsAsync(deviceId);
                            itemResult.BackupPath = backups.FirstOrDefault()?.BackupPath;
                        }
                    }
                    
                    // 更新を適用
                    _logger?.LogInformation("Applying update for device: {DeviceId}", deviceId);
                    var updateSuccess = await _driverService.UpdateDriverAsync(deviceId);
                    
                    if (updateSuccess)
                    {
                        itemResult.Success = true;
                        result.Successful++;
                        
                        // 履歴に追加
                        AddToHistory(deviceId, true, null, itemResult.BackupPath);
                    }
                    else
                    {
                        itemResult.Success = false;
                        itemResult.ErrorMessage = "Update failed";
                        result.Failed++;
                        
                        AddToHistory(deviceId, false, "Update failed", itemResult.BackupPath);
                    }
                }
                catch (Exception ex)
                {
                    itemResult.Success = false;
                    itemResult.ErrorMessage = ex.Message;
                    result.Failed++;
                    
                    _logger?.LogError(ex, "Error updating device: {DeviceId}", deviceId);
                    AddToHistory(deviceId, false, ex.Message, itemResult.BackupPath);
                }
                
                result.Items.Add(itemResult);
            }
            
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            
            lock (_lockObject)
            {
                _status.UpdatesApplied += result.Successful;
                _status.Errors += result.Failed;
            }
            
            _logger?.LogInformation("Update completed: {Successful}/{Total} successful", 
                result.Successful, result.TotalAttempted);
            
            return result;
        }
        
        public AutoUpdateStatus GetStatus()
        {
            lock (_lockObject)
            {
                return new AutoUpdateStatus
                {
                    IsRunning = _status.IsRunning,
                    StartedAt = _status.StartedAt,
                    LastCheckAt = _status.LastCheckAt,
                    NextCheckAt = _status.NextCheckAt,
                    UpdatesApplied = _status.UpdatesApplied,
                    UpdatesPending = _status.UpdatesPending,
                    Errors = _status.Errors
                };
            }
        }
        
        public async Task<List<UpdateHistory>> GetUpdateHistoryAsync(int count = 50)
        {
            var history = _updateHistory.ToArray()
                .OrderByDescending(h => h.Timestamp)
                .Take(count)
                .ToList();
            
            return await Task.FromResult(history);
        }
        
        private async Task PerformUpdateCheckAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_currentOptions == null)
                    return;
                
                // 更新ウィンドウのチェック
                if (_currentOptions.UpdateWindow.HasValue)
                {
                    var now = DateTime.Now.TimeOfDay;
                    var windowEnd = now.Add(_currentOptions.UpdateWindow.Value);
                    
                    if (windowEnd < now)
                    {
                        _logger?.LogDebug("Outside update window, skipping check");
                        return;
                    }
                }
                
                // 更新チェック
                var checkResult = await CheckForUpdatesAsync();
                
                if (checkResult.Success && checkResult.AvailableUpdates > 0)
                {
                    _logger?.LogInformation("Found {Count} available updates", checkResult.AvailableUpdates);
                    
                    // 自動適用が有効な場合
                    if (_currentOptions.AutoApply && _settingsService.AutoUpdateEnabled)
                    {
                        var criticalUpdates = checkResult.Updates
                            .Where(u => u.Priority >= UpdatePriority.High)
                            .Select(u => u.DeviceId);
                        
                        if (criticalUpdates.Any())
                        {
                            _logger?.LogInformation("Applying {Count} critical updates automatically", 
                                criticalUpdates.Count());
                            
                            await ApplyUpdatesAsync(criticalUpdates, _currentOptions.CreateBackup);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in auto update check");
                
                lock (_lockObject)
                {
                    _status.Errors++;
                }
            }
        }
        
        private UpdatePriority DeterminePriority(DriverInfo driver)
        {
            // 優先度判定ロジック（簡略版）
            if (driver.Status != "OK")
                return UpdatePriority.Critical;
            
            if (driver.DeviceClass == "Display" || driver.DeviceClass == "Network Adapter")
                return UpdatePriority.High;
            
            return UpdatePriority.Normal;
        }
        
        private void AddToHistory(string deviceId, bool success, string? errorMessage, string? backupPath)
        {
            var history = new UpdateHistory
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                DeviceId = deviceId,
                Success = success,
                ErrorMessage = errorMessage,
                BackupPath = backupPath
            };
            
            _updateHistory.Enqueue(history);
            
            // 履歴を最大1000件に制限
            while (_updateHistory.Count > 1000)
            {
                _updateHistory.TryDequeue(out _);
            }
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                StopAutoUpdateAsync().GetAwaiter().GetResult();
                _disposed = true;
            }
        }
    }
}