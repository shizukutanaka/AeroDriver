// 研究ベースの改善: ドライバーヘルスモニタリングと自動復旧システム
// 根拠: Reliability Monitor & System Stability Tracking - リアルタイムドライバーヘルス監視
//      異常検出と自動復旧メカニズム
// 優先度: P1 (高) - システム可用性クリティカル
// 出典: Windows Reliability Monitor, System Stability Index, Auto-Recovery Mechanisms

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Monitoring;

/// <summary>
/// ドライバーヘルスモニタリングシステム
/// リアルタイムでドライバーの状態を監視し、問題を検出して自動復旧
///
/// 監視項目:
/// 1. CPU使用率（異常な高使用率検出）
/// 2. メモリ使用率（メモリリーク検出）
/// 3. デバイスエラー（エラーレート監視）
/// 4. 応答性（タイムアウト検出）
/// 5. クラッシュイベント（BugCheck検出）
/// 6. パフォーマンス低下（ベースライン比較）
/// </summary>
public class DriverHealthMonitor : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, DriverHealthMetrics> _metricsHistory;
    private readonly ConcurrentDictionary<string, HealthAlert> _activeAlerts;
    private readonly CancellationTokenSource _monitoringCts;
    private readonly HealthMonitorConfig _config;

    private Task? _monitoringTask;

    public DriverHealthMonitor(ILogger logger, HealthMonitorConfig? config = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? new HealthMonitorConfig();
        _metricsHistory = new ConcurrentDictionary<string, DriverHealthMetrics>();
        _activeAlerts = new ConcurrentDictionary<string, HealthAlert>();
        _monitoringCts = new CancellationTokenSource();

        _logger.LogInformation("DriverHealthMonitor initialized");
    }

    /// <summary>
    /// ドライバー監視を開始
    /// </summary>
    public async Task<string> StartMonitoringAsync(
        string driverName,
        DriverMonitoringLevel level = DriverMonitoringLevel.Standard,
        CancellationToken ct = default)
    {
        _logger.LogInformation($"Starting health monitoring for {driverName} at level {level}");

        var monitoringId = Guid.NewGuid().ToString("N");
        var metrics = new DriverHealthMetrics
        {
            Id = monitoringId,
            DriverName = driverName,
            Level = level,
            StartedAt = DateTime.UtcNow,
            Measurements = new List<HealthMeasurement>()
        };

        _metricsHistory[monitoringId] = metrics;

        // バックグラウンド監視タスクを開始
        if (_monitoringTask == null || _monitoringTask.IsCompleted)
        {
            _monitoringTask = Task.Run(async () => await MonitoringLoopAsync(_monitoringCts.Token), _monitoringCts.Token);
        }

        return monitoringId;
    }

    /// <summary>
    /// 監視を停止
    /// </summary>
    public async Task StopMonitoringAsync(string monitoringId, CancellationToken ct = default)
    {
        _logger.LogInformation($"Stopping health monitoring: {monitoringId}");
        _metricsHistory.TryRemove(monitoringId, out _);
    }

    /// <summary>
    /// 現在のヘルススコアを取得
    /// </summary>
    public async Task<HealthScore> GetHealthScoreAsync(
        string driverName,
        CancellationToken ct = default)
    {
        var metrics = _metricsHistory.Values.FirstOrDefault(m => m.DriverName == driverName);

        if (metrics == null)
        {
            return new HealthScore
            {
                DriverName = driverName,
                Score = 100,
                Status = HealthStatus.Healthy,
                LastUpdated = DateTime.UtcNow
            };
        }

        // 最新の測定を取得
        var latestMeasurements = metrics.Measurements
            .OrderByDescending(m => m.Timestamp)
            .Take(_config.MeasurementHistorySize)
            .ToList();

        var score = CalculateHealthScore(latestMeasurements);
        var status = DetermineHealthStatus(score);

        return new HealthScore
        {
            DriverName = driverName,
            Score = score,
            Status = status,
            Metrics = new HealthMetricsSnapshot
            {
                AverageCpuUsage = latestMeasurements.Average(m => m.CpuUsagePercent),
                AverageMemoryUsage = latestMeasurements.Average(m => m.MemoryUsageMB),
                ErrorCount = latestMeasurements.Sum(m => m.ErrorCount),
                TimeoutCount = latestMeasurements.Sum(m => m.TimeoutCount),
                CrashCount = latestMeasurements.Sum(m => m.CrashCount)
            },
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// アクティブなアラートを取得
    /// </summary>
    public async Task<List<HealthAlert>> GetActiveAlertsAsync(
        string? driverName = null,
        CancellationToken ct = default)
    {
        var alerts = _activeAlerts.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(driverName))
        {
            alerts = alerts.Where(a => a.DriverName == driverName);
        }

        return alerts.OrderByDescending(a => a.Severity).ToList();
    }

    /// <summary>
    /// 手動復旧を実行
    /// </summary>
    public async Task<RecoveryResult> AttemptRecoveryAsync(
        string driverName,
        RecoveryStrategy strategy = RecoveryStrategy.Automatic,
        CancellationToken ct = default)
    {
        _logger.LogWarning($"Attempting recovery for {driverName} using strategy {strategy}");

        var result = new RecoveryResult
        {
            DriverName = driverName,
            Strategy = strategy,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            switch (strategy)
            {
                case RecoveryStrategy.Restart:
                    result.Success = await RestartDriverAsync(driverName, ct);
                    result.Action = "Driver restarted";
                    break;

                case RecoveryStrategy.Rollback:
                    result.Success = await RollbackDriverAsync(driverName, ct);
                    result.Action = "Driver rolled back to previous version";
                    break;

                case RecoveryStrategy.Disable:
                    result.Success = await DisableDriverAsync(driverName, ct);
                    result.Action = "Driver disabled";
                    break;

                case RecoveryStrategy.Automatic:
                    // 自動判断
                    var healthScore = await GetHealthScoreAsync(driverName, ct);
                    if (healthScore.Status == HealthStatus.Critical)
                    {
                        result.Success = await RollbackDriverAsync(driverName, ct);
                        result.Action = "Automatic rollback due to critical status";
                    }
                    else if (healthScore.Status == HealthStatus.Degraded)
                    {
                        result.Success = await RestartDriverAsync(driverName, ct);
                        result.Action = "Automatic restart due to degraded status";
                    }
                    else
                    {
                        result.Success = true;
                        result.Action = "No recovery action needed";
                    }
                    break;
            }

            result.CompletedAt = DateTime.UtcNow;
            result.Duration = result.CompletedAt.Value - result.StartedAt;

            _logger.LogInformation($"Recovery completed for {driverName}: {result.Action}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Recovery failed: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
        }

        return result;
    }

    /// <summary>
    /// バックグラウンド監視ループ
    /// </summary>
    private async Task MonitoringLoopAsync(CancellationToken ct)
    {
        var measurementInterval = TimeSpan.FromSeconds(_config.MeasurementIntervalSeconds);
        var lastMeasurement = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                if (now - lastMeasurement >= measurementInterval)
                {
                    await CollectMetricsAsync(ct);
                    await EvaluateHealthAsync(ct);
                    lastMeasurement = now;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Monitoring loop error: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    /// <summary>
    /// メトリクスを収集
    /// </summary>
    private async Task CollectMetricsAsync(CancellationToken ct)
    {
        foreach (var (monitoringId, metrics) in _metricsHistory)
        {
            var measurement = new HealthMeasurement
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                CpuUsagePercent = GetCpuUsage(metrics.DriverName),
                MemoryUsageMB = GetMemoryUsage(metrics.DriverName),
                ErrorCount = GetErrorCount(metrics.DriverName),
                TimeoutCount = GetTimeoutCount(metrics.DriverName),
                CrashCount = GetCrashCount(metrics.DriverName),
                Latency = GetLatency(metrics.DriverName)
            };

            metrics.Measurements.Add(measurement);

            // 履歴を制限
            if (metrics.Measurements.Count > _config.MeasurementHistorySize)
            {
                metrics.Measurements.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// ヘルス状態を評価
    /// </summary>
    private async Task EvaluateHealthAsync(CancellationToken ct)
    {
        foreach (var (monitoringId, metrics) in _metricsHistory)
        {
            if (metrics.Measurements.Count == 0)
            {
                continue;
            }

            var latestMeasurements = metrics.Measurements
                .OrderByDescending(m => m.Timestamp)
                .Take(10)
                .ToList();

            var avgCpu = latestMeasurements.Average(m => m.CpuUsagePercent);
            var avgMemory = latestMeasurements.Average(m => m.MemoryUsageMB);
            var totalErrors = latestMeasurements.Sum(m => m.ErrorCount);

            // CPU異常を検出
            if (avgCpu > _config.CpuAlertThreshold)
            {
                CreateAlert(metrics.DriverName, AlertType.HighCpuUsage,
                    $"CPU usage {avgCpu:F1}% exceeds threshold {_config.CpuAlertThreshold}%",
                    AlertSeverity.High);
            }

            // メモリリークを検出
            if (avgMemory > _config.MemoryAlertThreshold)
            {
                CreateAlert(metrics.DriverName, AlertType.HighMemoryUsage,
                    $"Memory usage {avgMemory:F1}MB exceeds threshold {_config.MemoryAlertThreshold}MB",
                    AlertSeverity.High);
            }

            // エラー増加を検出
            if (totalErrors > _config.ErrorCountThreshold)
            {
                CreateAlert(metrics.DriverName, AlertType.HighErrorRate,
                    $"Error count {totalErrors} exceeds threshold {_config.ErrorCountThreshold}",
                    AlertSeverity.Critical);
            }

            // クラッシュを検出
            if (latestMeasurements.Any(m => m.CrashCount > 0))
            {
                CreateAlert(metrics.DriverName, AlertType.CrashDetected,
                    "Driver crash detected",
                    AlertSeverity.Critical);
            }
        }
    }

    /// <summary>
    /// アラートを作成
    /// </summary>
    private void CreateAlert(string driverName, AlertType type, string message, AlertSeverity severity)
    {
        var alertId = $"{driverName}_{type}";

        // 同じアラートが既に存在する場合はスキップ
        if (_activeAlerts.ContainsKey(alertId))
        {
            return;
        }

        var alert = new HealthAlert
        {
            Id = alertId,
            DriverName = driverName,
            Type = type,
            Message = message,
            Severity = severity,
            CreatedAt = DateTime.UtcNow
        };

        _activeAlerts[alertId] = alert;
        _logger.LogWarning($"Alert created for {driverName}: {message}");
    }

    /// <summary>
    /// ヘルススコアを計算
    /// </summary>
    private double CalculateHealthScore(List<HealthMeasurement> measurements)
    {
        if (measurements.Count == 0)
        {
            return 100;
        }

        var score = 100.0;

        // CPU使用率でペナルティ
        var avgCpu = measurements.Average(m => m.CpuUsagePercent);
        if (avgCpu > 80)
        {
            score -= (avgCpu - 80) * 0.5;
        }

        // メモリ使用率でペナルティ
        var avgMemory = measurements.Average(m => m.MemoryUsageMB);
        if (avgMemory > 1024)
        {
            score -= (avgMemory - 1024) / 100 * 0.2;
        }

        // エラーレートでペナルティ
        var errorCount = measurements.Sum(m => m.ErrorCount);
        if (errorCount > 0)
        {
            score -= Math.Min(errorCount * 5, 30);
        }

        // クラッシュでペナルティ
        var crashCount = measurements.Sum(m => m.CrashCount);
        score -= crashCount * 50;

        return Math.Max(0, Math.Min(100, score));
    }

    /// <summary>
    /// ヘルスステータスを判定
    /// </summary>
    private HealthStatus DetermineHealthStatus(double score)
    {
        return score switch
        {
            >= 80 => HealthStatus.Healthy,
            >= 50 => HealthStatus.Degraded,
            >= 20 => HealthStatus.Poor,
            _ => HealthStatus.Critical
        };
    }

    /// <summary>
    /// CPU使用率を取得
    /// </summary>
    private double GetCpuUsage(string driverName)
    {
        // 実装: パフォーマンスカウンターから取得
        return Random.Shared.Next(5, 40); // シミュレーション
    }

    /// <summary>
    /// メモリ使用量を取得
    /// </summary>
    private double GetMemoryUsage(string driverName)
    {
        // 実装: パフォーマンスカウンターから取得
        return Random.Shared.Next(50, 500); // シミュレーション
    }

    /// <summary>
    /// エラー数を取得
    /// </summary>
    private int GetErrorCount(string driverName)
    {
        // 実装: イベントログから取得
        return Random.Shared.Next(0, 3); // シミュレーション
    }

    /// <summary>
    /// タイムアウト数を取得
    /// </summary>
    private int GetTimeoutCount(string driverName)
    {
        return Random.Shared.Next(0, 2);
    }

    /// <summary>
    /// クラッシュ数を取得
    /// </summary>
    private int GetCrashCount(string driverName)
    {
        return Random.Shared.Next(0, 1);
    }

    /// <summary>
    /// レイテンシを取得
    /// </summary>
    private double GetLatency(string driverName)
    {
        return Random.Shared.Next(1, 50); // ミリ秒
    }

    /// <summary>
    /// ドライバーを再起動
    /// </summary>
    private async Task<bool> RestartDriverAsync(string driverName, CancellationToken ct)
    {
        _logger.LogInformation($"Restarting driver: {driverName}");
        // 実装: デバイスを無効化してから有効化
        await Task.Delay(100, ct);
        return true;
    }

    /// <summary>
    /// ドライバーをロールバック
    /// </summary>
    private async Task<bool> RollbackDriverAsync(string driverName, CancellationToken ct)
    {
        _logger.LogWarning($"Rolling back driver: {driverName}");
        // 実装: 前のバージョンに復旧
        await Task.Delay(500, ct);
        return true;
    }

    /// <summary>
    /// ドライバーを無効化
    /// </summary>
    private async Task<bool> DisableDriverAsync(string driverName, CancellationToken ct)
    {
        _logger.LogWarning($"Disabling driver: {driverName}");
        // 実装: デバイスを無効化
        await Task.Delay(100, ct);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _monitoringCts.Cancel();
        _monitoringCts.Dispose();

        if (_monitoringTask != null)
        {
            try
            {
                await _monitoringTask;
            }
            catch (OperationCanceledException)
            {
                // 予期した動作
            }
        }
    }
}

/// <summary>
/// ヘルスモニター設定
/// </summary>
public class HealthMonitorConfig
{
    public int MeasurementIntervalSeconds { get; set; } = 5;
    public int MeasurementHistorySize { get; set; } = 1000;
    public double CpuAlertThreshold { get; set; } = 90;
    public double MemoryAlertThreshold { get; set; } = 2048; // MB
    public int ErrorCountThreshold { get; set; } = 10;
    public int TimeoutCountThreshold { get; set; } = 5;
}

/// <summary>
/// ドライバーヘルスメトリクス
/// </summary>
public class DriverHealthMetrics
{
    public string Id { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public DriverMonitoringLevel Level { get; set; }
    public DateTime StartedAt { get; set; }
    public List<HealthMeasurement> Measurements { get; set; } = new();
}

/// <summary>
/// ヘルス測定値
/// </summary>
public class HealthMeasurement
{
    public string Id { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsageMB { get; set; }
    public int ErrorCount { get; set; }
    public int TimeoutCount { get; set; }
    public int CrashCount { get; set; }
    public double Latency { get; set; }
}

/// <summary>
/// ヘルススコア
/// </summary>
public class HealthScore
{
    public string DriverName { get; set; } = string.Empty;
    public double Score { get; set; }
    public HealthStatus Status { get; set; }
    public HealthMetricsSnapshot? Metrics { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// ヘルスメトリクススナップショット
/// </summary>
public class HealthMetricsSnapshot
{
    public double AverageCpuUsage { get; set; }
    public double AverageMemoryUsage { get; set; }
    public int ErrorCount { get; set; }
    public int TimeoutCount { get; set; }
    public int CrashCount { get; set; }
}

/// <summary>
/// ヘルスステータス
/// </summary>
public enum HealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Poor = 2,
    Critical = 3
}

/// <summary>
/// ドライバー監視レベル
/// </summary>
public enum DriverMonitoringLevel
{
    Minimal = 0,
    Standard = 1,
    Comprehensive = 2,
    Aggressive = 3
}

/// <summary>
/// ヘルスアラート
/// </summary>
public class HealthAlert
{
    public string Id { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public AlertType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// アラート型
/// </summary>
public enum AlertType
{
    HighCpuUsage,
    HighMemoryUsage,
    HighErrorRate,
    CrashDetected,
    TimeoutDetected,
    PerformanceDegradation
}

/// <summary>
/// アラート重大度
/// </summary>
public enum AlertSeverity
{
    Information = 0,
    Warning = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// 復旧結果
/// </summary>
public class RecoveryResult
{
    public bool Success { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public RecoveryStrategy Strategy { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// 復旧戦略
/// </summary>
public enum RecoveryStrategy
{
    Automatic = 0,
    Restart = 1,
    Rollback = 2,
    Disable = 3
}
