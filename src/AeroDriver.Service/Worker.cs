using AeroDriver.Core;
using AeroDriver.Core.Services;
using AeroDriver.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Service;

public class AeroDriverWorker : BackgroundService, IDisposable
{
    private readonly ILogger<AeroDriverWorker> _logger;
    private readonly CoreDriverService _driverService;
    private readonly ITelemetryService _telemetryService;
    private readonly IAutoUpdateService _autoUpdateService;
    private readonly ISecurityService _securityService;
    private readonly ErrorHandlingService _errorHandlingService;
    private readonly PerformanceMonitoringService _performanceMonitoringService;

    // パフォーマンス監視用
    private double? _lastCacheUsageRatio;
    private int? _lastCacheEntryCount;
    private DateTime? _lastCacheStatusTimestamp;
    private DateTime _serviceStartTime;
    private long _totalOperationsCount;
    private long _failedOperationsCount;
    private readonly Stopwatch _uptimeTimer;

    // ヘルスチェック用
    private readonly Timer _healthCheckTimer;
    private volatile bool _isHealthy = true;
    private int _consecutiveFailures;
    private static readonly int MaxConsecutiveFailures = 5;

    // リソース管理用
    private CancellationTokenSource _maintenanceCts;
    private Task _maintenanceTask;

    private static readonly TimeSpan CacheStatusMinInterval = TimeSpan.FromMinutes(1);
    private static readonly double CacheHighUsageThreshold = 0.85;
    private static readonly double CacheRecoveryThreshold = 0.75;
    private const string CorrelationIdProperty = "CorrelationId";

    private static Dictionary<string, object> CreateEventPayload(string correlationId, Dictionary<string, object> properties)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            properties[CorrelationIdProperty] = correlationId;
        }

        return properties;
    }

    private async Task ExecuteWithResilienceAsync(string operationName, Func<Task> operation, string correlationId, RetryPolicy? policy = null)
    {
        try
        {
            await _errorHandlingService.ExecuteWithRetryAsync(async () =>
            {
                await operation();
                return true;
            }, policy ?? new RetryPolicy());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation {OperationName} failed after retries", operationName);

            try
            {
                await _telemetryService.RecordEventAsync("OperationFailure", CreateEventPayload(correlationId, new Dictionary<string, object>
                {
                    ["Operation"] = operationName,
                    ["Message"] = ex.Message,
                    ["Timestamp"] = DateTime.UtcNow
                }));
            }
            catch (Exception telemetryEx)
            {
                _logger.LogDebug(telemetryEx, "Failed to record telemetry for operation failure {Operation}", operationName);
            }

            throw;
        }
    }

    public AeroDriverWorker(
        ILogger<AeroDriverWorker> logger,
        CoreDriverService driverService,
        ITelemetryService telemetryService,
        IAutoUpdateService autoUpdateService,
        ISecurityService securityService,
        ErrorHandlingService errorHandlingService,
        PerformanceMonitoringService performanceMonitoringService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driverService = driverService ?? throw new ArgumentNullException(nameof(driverService));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _autoUpdateService = autoUpdateService ?? throw new ArgumentNullException(nameof(autoUpdateService));
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _performanceMonitoringService = performanceMonitoringService ?? throw new ArgumentNullException(nameof(performanceMonitoringService));

        _uptimeTimer = new Stopwatch();
        _healthCheckTimer = new Timer(PerformHealthCheck, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting AeroDriver Windows Service...");
            _serviceStartTime = DateTime.UtcNow;
            _uptimeTimer.Start();

            await base.StartAsync(cancellationToken);
            _logger.LogInformation("AeroDriver Windows Service started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to start AeroDriver Windows Service");
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AeroDriver Windows Service execution started at: {time}", DateTimeOffset.Now);

        try
        {
            // サービス開始時の初期化
            var initializationCorrelationId = Guid.NewGuid().ToString("N");
            await InitializeServicesAsync(initializationCorrelationId);

            // メインの監視ループ
            await RunMainLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Service execution cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical error in service execution");
            _isHealthy = false;
            throw;
        }
        finally
        {
            _logger.LogInformation("AeroDriver Windows Service execution completed at: {time}", DateTimeOffset.Now);
        }
    }

    private async Task InitializeServicesAsync(string correlationId)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Initializing AeroDriver services...");

            // セキュリティサービスの初期化
            await _securityService.PerformSecurityAuditAsync();

            // テレメトリサービスの開始
            _telemetryService.StartTelemetryCollection(15);

            // 自動更新チェックの開始
            _autoUpdateService.StartAutoUpdateCheck(24);

            // メンテナンスタスクの開始
            StartMaintenanceTasks();

            _logger.LogInformation("All services initialized successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            _performanceMonitoringService.Configure(new PerformanceMonitoringConfig
            {
                EnableCpuMonitoring = true,
                EnableMemoryMonitoring = true,
                EnableDiskMonitoring = true,
                MonitoringIntervalSeconds = 60
            });

            // 初期化完了イベントを記録
            await _telemetryService.RecordEventAsync("ServiceInitialized", new Dictionary<string, object>
            {
                ["InitializationTimeMs"] = stopwatch.ElapsedMilliseconds,
                ["Timestamp"] = DateTime.UtcNow,
                [CorrelationIdProperty] = correlationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing services after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task RunMainLoopAsync(CancellationToken stoppingToken)
    {
        var iterationCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var correlationId = Guid.NewGuid().ToString("N");

            try
            {
                iterationCount++;
                var iterationStartTime = DateTime.UtcNow;

                _logger.LogDebug("Starting maintenance iteration {Iteration}", iterationCount);

                // 定期的なメンテナンス処理
                await ExecuteWithResilienceAsync("PerformMaintenanceTasks", () => PerformMaintenanceTasksAsync(correlationId), correlationId);

                // システム状態の監視
                await ExecuteWithResilienceAsync("MonitorSystemHealth", () => MonitorSystemHealthAsync(correlationId), correlationId);

                // テレメトリデータの送信
                await ExecuteWithResilienceAsync("SendTelemetryData", () => SendTelemetryDataAsync(correlationId), correlationId, new RetryPolicy { MaxAttempts = 2, InitialDelayMs = 2000 });

                // パフォーマンスメトリクスの記録
                await ExecuteWithResilienceAsync("RecordPerformanceMetrics", () => RecordPerformanceMetricsAsync(iterationStartTime, correlationId), correlationId);

                try
                {
                    await _telemetryService.RecordEventAsync("MaintenanceIterationCompleted", CreateEventPayload(correlationId, new Dictionary<string, object>
                    {
                        ["Iteration"] = iterationCount,
                        ["StartedAtUtc"] = iterationStartTime,
                        ["CompletedAtUtc"] = DateTime.UtcNow
                    }));
                }
                catch (Exception telemetryEx)
                {
                    _logger.LogDebug(telemetryEx, "Failed to record iteration completion telemetry");
                }

                // 次の実行まで待機（適応性のある間隔）
                var nextInterval = CalculateOptimalInterval();
                _logger.LogDebug("Maintenance iteration {Iteration} completed, next in {Interval}", iterationCount, nextInterval);

                await Task.Delay(nextInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Main loop cancelled at iteration {Iteration}", iterationCount);
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _failedOperationsCount++;

                _logger.LogError(ex, "Error in main service loop iteration {Iteration}", iterationCount);

                 try
                 {
                     await _telemetryService.RecordEventAsync("MaintenanceIterationFailed", CreateEventPayload(correlationId, new Dictionary<string, object>
                     {
                         ["Iteration"] = iterationCount,
                         ["Message"] = ex.Message,
                         ["Timestamp"] = DateTime.UtcNow
                     }));
                 }
                 catch (Exception telemetryEx)
                 {
                     _logger.LogDebug(telemetryEx, "Failed to record iteration failure telemetry");
                 }

                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogCritical("Too many consecutive failures ({Failures}), marking service as unhealthy", _consecutiveFailures);
                    _isHealthy = false;
                }

                // エラーバックオフ戦略
                var backoffDelay = TimeSpan.FromMinutes(Math.Min(30, Math.Pow(2, Math.Min(_consecutiveFailures, 8))));
                _logger.LogWarning("Backing off for {BackoffDelay} due to errors", backoffDelay);

                await Task.Delay(backoffDelay, stoppingToken);
            }
        }
    }

    private async Task PerformMaintenanceTasksAsync(string correlationId)
    {
        _totalOperationsCount++;

        try
        {
            _logger.LogDebug("Performing maintenance tasks...");

            // システム最適化の実行
            var optimizationResult = await _driverService.OptimizeSystemAsync();
            if (optimizationResult.Success)
            {
                _logger.LogInformation("System optimization completed: {message}", optimizationResult.Message);
            }

            // セキュリティスキャンの実行
            var securityReport = await _securityService.PerformSecurityAuditAsync();
            if (securityReport.TotalIssues > 0)
            {
                _logger.LogWarning("Security issues found: {issues}", securityReport.TotalIssues);

                // 重大なセキュリティ問題の場合、即時対応
                if (securityReport.CriticalIssues.Count > 0)
                {
                    await HandleCriticalSecurityIssuesAsync(securityReport, correlationId);
                }
            }

            // 古いログのクリーンアップ
            await _telemetryService.ClearTelemetryDataAsync();

            _logger.LogDebug("Maintenance tasks completed");

            // 連続失敗カウンターをリセット
            if (_consecutiveFailures > 0)
            {
                _consecutiveFailures = 0;
                _isHealthy = true;
                _logger.LogInformation("Service health restored after successful maintenance");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing maintenance tasks");
            throw;
        }
    }

    private async Task MonitorSystemHealthAsync(string correlationId)
    {
        try
        {
            // システムヘルスの監視
            var drivers = await _driverService.GetAllDriversAsync();
            var problemDrivers = drivers.Where(d => d.Status != "OK").ToList();

            if (problemDrivers.Any())
            {
                _logger.LogWarning("Problem drivers detected: {count}", problemDrivers.Count);

                // 問題のあるドライバーの自動修復を試行
                var fixResult = await _driverService.FixIssuesAsync();
                if (fixResult.Success)
                {
                    _logger.LogInformation("Driver issues fixed: {count}", fixResult.ProcessedCount);
                }
                else
                {
                    _logger.LogWarning("Failed to fix all driver issues: {errors}", string.Join(", ", fixResult.Errors));
                }
            }

            // パフォーマンスメトリクスの記録
            var performanceReport = await _driverService.GetPerformanceReportAsync();
            _telemetryService.RecordPerformanceMetric("OverallScore", performanceReport.Metrics.OverallScore);

            _logger.LogDebug("System health monitoring completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring system health");
            throw;
        }
    }

    private async Task SendTelemetryDataAsync(string correlationId)
    {
        try
        {
            _logger.LogDebug("Sending telemetry data...");

            // システム情報を記録
            var systemStats = await _driverService.GetSystemStatsAsync();
            await _telemetryService.RecordEventAsync("SystemStatus", CreateEventPayload(correlationId, new Dictionary<string, object>
            {
                ["TotalDrivers"] = systemStats.TotalDrivers,
                ["ActiveDrivers"] = systemStats.ActiveDrivers,
                ["ProblemDrivers"] = systemStats.ProblemDrivers,
                ["ServiceUptimeMinutes"] = _uptimeTimer.Elapsed.TotalMinutes,
                ["TotalOperations"] = _totalOperationsCount,
                ["FailedOperations"] = _failedOperationsCount
            }));

            var cacheDiagnostics = MemoryOptimizer.GetDiagnostics();
            await _telemetryService.RecordPerformanceMetricAsync("CacheEntries", cacheDiagnostics.CurrentEntries);
            await _telemetryService.RecordPerformanceMetricAsync("CacheCapacity", cacheDiagnostics.Capacity);
            await _telemetryService.RecordPerformanceMetricAsync("CacheUsageRatio", cacheDiagnostics.UsageRatio);

            await RecordCacheStatusAsync(cacheDiagnostics, correlationId);

            // メモリキャッシュの自動管理
            await ManageMemoryCacheAsync(cacheDiagnostics, correlationId);

            _logger.LogDebug("Telemetry data sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending telemetry data");
        }
    }

    private async Task ManageMemoryCacheAsync(MemoryOptimizer.CacheDiagnostics diagnostics, string correlationId)
    {
        if (diagnostics.UsageRatio >= CacheHighUsageThreshold)
        {
            if (!_isHealthy)
            {
                _logger.LogWarning("Skipping cache management due to unhealthy service state");
                return;
            }

            _logger.LogWarning("Memory cache usage is above {Threshold:P0} (entries: {Entries}/{Capacity}, usage: {Usage:P1})",
                CacheHighUsageThreshold, diagnostics.CurrentEntries, diagnostics.Capacity, diagnostics.UsageRatio);

            await _telemetryService.RecordEventAsync("CacheCapacityAlert", CreateEventPayload(correlationId, new Dictionary<string, object>
            {
                ["Entries"] = diagnostics.CurrentEntries,
                ["Capacity"] = diagnostics.Capacity,
                ["UsageRatio"] = Math.Round(diagnostics.UsageRatio, 4)
            }));

            var trimResult = MemoryOptimizer.TrimToCapacity(CacheRecoveryThreshold);
            if (trimResult.Trimmed)
            {
                var postTrimDiagnostics = MemoryOptimizer.GetDiagnostics();
                await _telemetryService.RecordEventAsync("CacheTrim", CreateEventPayload(correlationId, new Dictionary<string, object>
                {
                    ["RemovedEntries"] = trimResult.RemovedEntries,
                    ["RemainingEntries"] = trimResult.RemainingEntries,
                    ["Capacity"] = trimResult.Capacity,
                    ["UsageBefore"] = Math.Round(trimResult.UsageBefore, 4),
                    ["UsageAfter"] = Math.Round(trimResult.UsageAfter, 4),
                    ["Threshold"] = Math.Round(trimResult.Threshold, 4)
                }));

                await _telemetryService.RecordPerformanceMetricAsync("CacheUsageRatio", postTrimDiagnostics.UsageRatio);
                await RecordCacheStatusAsync(postTrimDiagnostics, correlationId, force: true);

                _logger.LogInformation("Cache trimmed to maintain usage threshold (removed: {Removed}, remaining: {Remaining}, usage: {UsageAfter:P1})",
                    trimResult.RemovedEntries, trimResult.RemainingEntries, postTrimDiagnostics.UsageRatio);
            }
            else
            {
                _logger.LogWarning("Cache trim attempt removed no entries while usage remains above threshold (usage: {Usage:P1})", diagnostics.UsageRatio);
            }
        }
    }

    private async Task RecordPerformanceMetricsAsync(DateTime iterationStartTime, string correlationId)
    {
        var iterationDuration = DateTime.UtcNow - iterationStartTime;

        await _telemetryService.RecordPerformanceMetricAsync("IterationDurationMs", iterationDuration.TotalMilliseconds);
        await _telemetryService.RecordPerformanceMetricAsync("ServiceUptimeMinutes", _uptimeTimer.Elapsed.TotalMinutes);
        await _telemetryService.RecordPerformanceMetricAsync("TotalOperations", _totalOperationsCount);
        await _telemetryService.RecordPerformanceMetricAsync("FailedOperations", _failedOperationsCount);
        await _telemetryService.RecordPerformanceMetricAsync("SuccessRate", CalculateSuccessRate());
        await _telemetryService.RecordPerformanceMetricAsync("IsHealthy", _isHealthy ? 1 : 0);

        await _telemetryService.RecordEventAsync("PerformanceMetricsSnapshot", CreateEventPayload(correlationId, new Dictionary<string, object>
        {
            ["IterationDurationMs"] = iterationDuration.TotalMilliseconds,
            ["UptimeMinutes"] = _uptimeTimer.Elapsed.TotalMinutes,
            ["TotalOperations"] = _totalOperationsCount,
            ["FailedOperations"] = _failedOperationsCount,
            ["SuccessRate"] = CalculateSuccessRate(),
            ["IsHealthy"] = _isHealthy
        }));
    }

    private double CalculateSuccessRate()
    {
        if (_totalOperationsCount == 0) return 1.0;
        return (double)(_totalOperationsCount - _failedOperationsCount) / _totalOperationsCount;
    }

    private TimeSpan CalculateOptimalInterval()
    {
        // システム負荷とヘルス状態に基づいて最適な間隔を計算
        var baseInterval = TimeSpan.FromMinutes(15);

        if (!_isHealthy)
            return baseInterval * 2; // 異常時は間隔を延長

        // 高い失敗率の場合も間隔を延長
        var failureRate = CalculateSuccessRate();
        if (failureRate < 0.8)
            return baseInterval * 1.5;

        return baseInterval;
    }

    private async Task HandleCriticalSecurityIssuesAsync(SecurityReport report, string correlationId)
    {
        _logger.LogCritical("Critical security issues detected: {Count}", report.CriticalIssues.Count);

        // 重大なセキュリティ問題の即時対応
        foreach (var issue in report.CriticalIssues)
        {
            _logger.LogCritical("Critical Issue: {Type} - {Description}", issue.Type, issue.Description);

            // セキュリティイベントとして記録
            await _telemetryService.RecordEventAsync("CriticalSecurityIssue", CreateEventPayload(correlationId, new Dictionary<string, object>
            {
                ["IssueType"] = issue.Type,
                ["Description"] = issue.Description,
                ["Severity"] = issue.Severity.ToString(),
                ["Recommendation"] = issue.Recommendation,
                ["Timestamp"] = DateTime.UtcNow
            }));
        }

        // サービスを安全モードに切り替え
        _isHealthy = false;
    }

    private void PerformHealthCheck(object state)
    {
        try
        {
            // 基本的なヘルスチェック
            var isProcessHealthy = Process.GetCurrentProcess().Responding;
            var memoryUsage = Process.GetCurrentProcess().WorkingSet64;

            if (!isProcessHealthy || memoryUsage > 500 * 1024 * 1024) // 500MB以上
            {
                _logger.LogWarning("Health check failed - Process issues detected");
                _isHealthy = false;
                return;
            }

            // メモリ使用量チェック
            if (GC.GetTotalMemory(false) > 100 * 1024 * 1024) // 100MB以上
            {
                GC.Collect();
            }

            _logger.LogDebug("Health check passed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            _isHealthy = false;
        }
    }

    private void StartMaintenanceTasks()
    {
        _maintenanceCts = new CancellationTokenSource();

        _maintenanceTask = Task.Run(async () =>
        {
            while (!_maintenanceCts.Token.IsCancellationRequested)
            {
                try
                {
                    // バックグラウンドメンテナンス（低優先度）
                    await Task.Delay(TimeSpan.FromHours(1), _maintenanceCts.Token);

                    if (_maintenanceCts.Token.IsCancellationRequested)
                        break;

                    // ログローテーション
                    await PerformLogRotationAsync();

                    // 一時ファイルクリーンアップ
                    await CleanupTempFilesAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in background maintenance task");
                }
            }
        }, _maintenanceCts.Token);
    }

    private async Task PerformLogRotationAsync()
    {
        try
        {
            // ログファイルサイズチェックとローテーション
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroDriver", "Logs");
            if (Directory.Exists(logPath))
            {
                var logFiles = Directory.GetFiles(logPath, "*.log", SearchOption.AllDirectories);
                foreach (var file in logFiles)
                {
                    var info = new FileInfo(file);
                    if (info.Length > 10 * 1024 * 1024) // 10MB以上
                    {
                        // ログローテーション
                        var rotatedPath = file + ".old";
                        if (File.Exists(rotatedPath))
                            File.Delete(rotatedPath);
                        File.Move(file, rotatedPath);
                        _logger.LogInformation("Rotated log file: {File}", file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Log rotation failed");
        }
    }

    private async Task CleanupTempFilesAsync()
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var aeroDriverTemp = Path.Combine(tempPath, "AeroDriver");

            if (Directory.Exists(aeroDriverTemp))
            {
                var tempFiles = Directory.GetFiles(aeroDriverTemp, "*", SearchOption.AllDirectories)
                    .Where(f => File.GetLastAccessTime(f) < DateTime.Now.AddHours(-1));

                foreach (var file in tempFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to cleanup temp file: {File}", file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Temp file cleanup failed");
        }
    }

    private async Task RecordCacheStatusAsync(MemoryOptimizer.CacheDiagnostics diagnostics, string correlationId, bool force = false)
    {
        var usage = diagnostics.UsageRatio;
        var entries = diagnostics.CurrentEntries;
        var now = DateTime.UtcNow;

        var hasUsageBaseline = _lastCacheUsageRatio.HasValue;
        var hasEntryBaseline = _lastCacheEntryCount.HasValue;

        var significantChange = force
            || !hasUsageBaseline
            || !hasEntryBaseline
            || Math.Abs(usage - _lastCacheUsageRatio!.Value) >= 0.02
            || Math.Abs(entries - _lastCacheEntryCount!.Value) >= 10;

        if (!significantChange)
            return;

        if (!force && _lastCacheStatusTimestamp.HasValue && now - _lastCacheStatusTimestamp.Value < CacheStatusMinInterval)
            return;

        await _telemetryService.RecordEventAsync("CacheStatus", CreateEventPayload(correlationId, new Dictionary<string, object>
        {
            ["Entries"] = diagnostics.CurrentEntries,
            ["Capacity"] = diagnostics.Capacity,
            ["UsageRatio"] = Math.Round(diagnostics.UsageRatio, 4),
            ["ExpirationSeconds"] = (int)diagnostics.CacheExpiration.TotalSeconds
        }));

        _lastCacheUsageRatio = usage;
        _lastCacheEntryCount = entries;
        _lastCacheStatusTimestamp = now;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
                {
                    _logger.LogWarning("Maintenance task did not complete within {Timeout} during service stop", timeout);
                }
                else if (_maintenanceTask.IsFaulted)
                {
                    _logger.LogError(_maintenanceTask.Exception, "Maintenance task terminated with errors during service stop");
                }
            }

            _maintenanceTask = null;

            _maintenanceCts?.Dispose();
            _maintenanceCts = null;

            // 最終テレメトリデータを送信
            await SendFinalTelemetryAsync();

            // バックグラウンドサービスを停止
            _telemetryService.StopTelemetryCollection();
            _autoUpdateService.StopAutoUpdateCheck();

            _uptimeTimer.Stop();

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("AeroDriver Windows Service stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service shutdown");
        }
    }

    private async Task SendFinalTelemetryAsync()
    {
        try
        {
            await _telemetryService.RecordEventAsync("ServiceShutdown", new Dictionary<string, object>
            {
                ["UptimeMinutes"] = _uptimeTimer.Elapsed.TotalMinutes,
                ["TotalOperations"] = _totalOperationsCount,
                ["FailedOperations"] = _failedOperationsCount,
                ["FinalHealthStatus"] = _isHealthy,
                ["ShutdownTime"] = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending final telemetry");
        }
    }

    public void Dispose()
    {
        _telemetryService.StopTelemetryCollection();
        _autoUpdateService.StopAutoUpdateCheck();

        _healthCheckTimer?.Dispose();

        if (_maintenanceCts != null)
        {
            _maintenanceCts.Cancel();
        }

        if (_maintenanceTask != null)
        {
            try
            {
                var timeout = TimeSpan.FromSeconds(5);
                var timeoutTask = Task.Delay(timeout);
                var completedIndex = Task.WaitAny(_maintenanceTask, timeoutTask);

                if (completedIndex == 1 && !_maintenanceTask.IsCompleted)
                {
                    _logger.LogWarning("Maintenance task did not complete within {Timeout} during disposal", timeout);
                }
                else if (_maintenanceTask.IsFaulted)
                {
                    _logger.LogWarning(_maintenanceTask.Exception, "Maintenance task terminated with errors during disposal");
                }
                else if (_maintenanceTask.IsCanceled)
                {
                    _logger.LogDebug("Maintenance task was cancelled during disposal");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to wait for maintenance task during disposal");
            }

            _maintenanceTask = null;
        }

        _maintenanceCts?.Dispose();
        _maintenanceCts = null;

        _uptimeTimer?.Stop();
    }
}
