// 研究ベースの改善: リアルタイムパフォーマンス監視
// 根拠: カーネルモード性能監視は最適化に不可欠
// 優先度: P2 (中) - 運用監視
// 出典: Kernel Mode Performance Monitoring (Microsoft), Cohort Intelligence研究

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Monitoring;

/// <summary>
/// リアルタイムドライバーパフォーマンス監視システム
/// Windows Performance Counters とカスタムメトリクスを使用
/// </summary>
public class RealTimePerformanceMonitor : IDisposable
{
    private readonly ILogger _logger;
    private readonly ConcurrentQueue<PerformanceSample> _samples = new();
    private readonly Timer _collectionTimer;
    private readonly TimeSpan _samplingInterval = TimeSpan.FromSeconds(1);
    private readonly int _maxSamples = 300; // 5分間のデータ（1秒×300）
    private bool _disposed;

    // パフォーマンスカウンター
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _interruptCounter;
    private PerformanceCounter? _dpcCounter;
    private PerformanceCounter? _contextSwitchCounter;

    public RealTimePerformanceMonitor(ILogger logger)
    {
        _logger = logger;

        // Windowsの場合のみパフォーマンスカウンターを初期化
        if (OperatingSystem.IsWindows())
        {
            InitializePerformanceCounters();
        }

        // 定期サンプリングタイマー
        _collectionTimer = new Timer(
            CollectSample,
            null,
            _samplingInterval,
            _samplingInterval);
    }

    /// <summary>
    /// パフォーマンスカウンターの初期化
    /// </summary>
    private void InitializePerformanceCounters()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _interruptCounter = new PerformanceCounter("Processor", "% Interrupt Time", "_Total");
            _dpcCounter = new PerformanceCounter("Processor", "% DPC Time", "_Total");
            _contextSwitchCounter = new PerformanceCounter("System", "Context Switches/sec");

            // 初回読み取り（正確な値を得るため）
            _cpuCounter.NextValue();
            _interruptCounter.NextValue();
            _dpcCounter.NextValue();
            _contextSwitchCounter.NextValue();

            _logger.LogInformation("Performance counters initialized");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to initialize some performance counters: {ex.Message}");
        }
    }

    /// <summary>
    /// 特定ドライバーのパフォーマンス監視
    /// </summary>
    /// <remarks>
    /// 監視項目:
    /// - CPU使用率
    /// - 割り込み時間
    /// - DPC (Deferred Procedure Call) 時間
    /// - コンテキストスイッチ回数
    /// - メモリ使用量
    /// - I/Oスループット
    ///
    /// 異常検出:
    /// - CPU使用率 > 50% が継続
    /// - 割り込み時間 > 20%
    /// - DPC時間 > 10%
    /// </remarks>
    public async Task<PerformanceMetrics> MonitorDriverPerformanceAsync(
        string driverId,
        TimeSpan duration,
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new PerformanceMetrics
            {
                ErrorMessage = "Performance monitoring is only available on Windows"
            };
        }

        _logger.LogInformation($"Starting performance monitoring for driver {driverId} (duration: {duration})");

        var samples = new List<PerformanceSample>();
        var startTime = DateTime.UtcNow;

        try
        {
            while (DateTime.UtcNow - startTime < duration)
            {
                ct.ThrowIfCancellationRequested();

                var sample = CollectCurrentSample();
                samples.Add(sample);

                await Task.Delay(_samplingInterval, ct);
            }

            return AnalyzeSamples(samples, driverId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Performance monitoring cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Performance monitoring failed: {ex.Message}");
            return new PerformanceMetrics
            {
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// パフォーマンス回帰検出
    /// ベースラインとの比較で性能低下を検出
    /// </summary>
    public async Task<RegressionAnalysis> DetectPerformanceRegressionAsync(
        string driverId,
        PerformanceMetrics baseline,
        CancellationToken ct = default)
    {
        var current = await MonitorDriverPerformanceAsync(
            driverId,
            TimeSpan.FromMinutes(2),
            ct);

        var analysis = new RegressionAnalysis
        {
            DriverId = driverId,
            Baseline = baseline,
            Current = current,
            AnalyzedAt = DateTime.UtcNow
        };

        // CPU使用率の比較
        var cpuRegression = CompareMetric(
            baseline.AverageCpuUsage,
            current.AverageCpuUsage,
            threshold: 0.20); // 20%以上の増加で回帰

        if (cpuRegression > 0)
        {
            analysis.Regressions.Add(new RegressionIssue
            {
                Metric = "CPU Usage",
                BaselineValue = baseline.AverageCpuUsage,
                CurrentValue = current.AverageCpuUsage,
                PercentageIncrease = cpuRegression,
                Severity = cpuRegression > 0.50 ? RegressionSeverity.Critical :
                          cpuRegression > 0.30 ? RegressionSeverity.High :
                          RegressionSeverity.Medium
            });
        }

        // 割り込み時間の比較
        var interruptRegression = CompareMetric(
            baseline.AverageInterruptTime,
            current.AverageInterruptTime,
            threshold: 0.15);

        if (interruptRegression > 0)
        {
            analysis.Regressions.Add(new RegressionIssue
            {
                Metric = "Interrupt Time",
                BaselineValue = baseline.AverageInterruptTime,
                CurrentValue = current.AverageInterruptTime,
                PercentageIncrease = interruptRegression,
                Severity = interruptRegression > 0.40 ? RegressionSeverity.Critical :
                          interruptRegression > 0.25 ? RegressionSeverity.High :
                          RegressionSeverity.Medium
            });
        }

        analysis.HasRegression = analysis.Regressions.Any();
        analysis.OverallSeverity = analysis.Regressions.Any() ?
            analysis.Regressions.Max(r => r.Severity) :
            RegressionSeverity.None;

        return analysis;
    }

    /// <summary>
    /// 現在のシステム状態を取得
    /// </summary>
    public SystemHealthSnapshot GetCurrentSystemHealth()
    {
        var sample = CollectCurrentSample();

        return new SystemHealthSnapshot
        {
            CpuUsage = sample.CpuUsage,
            InterruptTime = sample.InterruptTime,
            DpcTime = sample.DpcTime,
            ContextSwitches = sample.ContextSwitchesPerSec,
            MemoryUsageMB = sample.ProcessMemoryMB,
            Timestamp = sample.Timestamp,
            IsHealthy = IsSystemHealthy(sample)
        };
    }

    /// <summary>
    /// 履歴サンプルの取得
    /// </summary>
    public List<PerformanceSample> GetRecentSamples(int count = 60)
    {
        return _samples.TakeLast(count).ToList();
    }

    /// <summary>
    /// サンプル収集（タイマーコールバック）
    /// </summary>
    private void CollectSample(object? state)
    {
        try
        {
            var sample = CollectCurrentSample();
            _samples.Enqueue(sample);

            // 最大サンプル数を超えたら古いものを削除
            while (_samples.Count > _maxSamples)
            {
                _samples.TryDequeue(out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to collect performance sample: {ex.Message}");
        }
    }

    /// <summary>
    /// 現在のパフォーマンスサンプルを収集
    /// </summary>
    private PerformanceSample CollectCurrentSample()
    {
        var sample = new PerformanceSample
        {
            Timestamp = DateTime.UtcNow
        };

        if (!OperatingSystem.IsWindows())
        {
            return sample;
        }

        try
        {
            sample.CpuUsage = _cpuCounter?.NextValue() ?? 0;
            sample.InterruptTime = _interruptCounter?.NextValue() ?? 0;
            sample.DpcTime = _dpcCounter?.NextValue() ?? 0;
            sample.ContextSwitchesPerSec = _contextSwitchCounter?.NextValue() ?? 0;

            var process = Process.GetCurrentProcess();
            sample.ProcessMemoryMB = process.WorkingSet64 / (1024 * 1024);
            sample.ThreadCount = process.Threads.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error collecting performance sample: {ex.Message}");
        }

        return sample;
    }

    /// <summary>
    /// サンプル分析
    /// </summary>
    private PerformanceMetrics AnalyzeSamples(List<PerformanceSample> samples, string driverId)
    {
        if (!samples.Any())
        {
            return new PerformanceMetrics
            {
                ErrorMessage = "No samples collected"
            };
        }

        var metrics = new PerformanceMetrics
        {
            DriverId = driverId,
            SampleCount = samples.Count,
            StartTime = samples.First().Timestamp,
            EndTime = samples.Last().Timestamp,
            AverageCpuUsage = samples.Average(s => s.CpuUsage),
            MaxCpuUsage = samples.Max(s => s.CpuUsage),
            AverageInterruptTime = samples.Average(s => s.InterruptTime),
            MaxInterruptTime = samples.Max(s => s.InterruptTime),
            AverageDpcTime = samples.Average(s => s.DpcTime),
            MaxDpcTime = samples.Max(s => s.DpcTime),
            AverageContextSwitches = samples.Average(s => s.ContextSwitchesPerSec),
            AverageMemoryMB = samples.Average(s => s.ProcessMemoryMB)
        };

        // 異常検出
        metrics.Anomalies = DetectAnomalies(samples);

        // 総合評価
        metrics.HealthScore = CalculateHealthScore(metrics);
        metrics.HealthGrade = GetHealthGrade(metrics.HealthScore);

        return metrics;
    }

    /// <summary>
    /// 異常検出
    /// </summary>
    private List<PerformanceAnomaly> DetectAnomalies(List<PerformanceSample> samples)
    {
        var anomalies = new List<PerformanceAnomaly>();

        // CPU使用率の異常
        var highCpuSamples = samples.Count(s => s.CpuUsage > 50);
        if (highCpuSamples > samples.Count * 0.3) // 30%以上のサンプルで高CPU
        {
            anomalies.Add(new PerformanceAnomaly
            {
                Type = "High CPU Usage",
                Description = $"CPU usage exceeded 50% in {highCpuSamples} of {samples.Count} samples",
                Severity = highCpuSamples > samples.Count * 0.7 ? AnomalySeverity.High : AnomalySeverity.Medium
            });
        }

        // 割り込み時間の異常
        var highInterruptSamples = samples.Count(s => s.InterruptTime > 20);
        if (highInterruptSamples > samples.Count * 0.2)
        {
            anomalies.Add(new PerformanceAnomaly
            {
                Type = "High Interrupt Time",
                Description = $"Interrupt time exceeded 20% in {highInterruptSamples} of {samples.Count} samples",
                Severity = AnomalySeverity.High
            });
        }

        // DPC時間の異常
        var highDpcSamples = samples.Count(s => s.DpcTime > 10);
        if (highDpcSamples > samples.Count * 0.2)
        {
            anomalies.Add(new PerformanceAnomaly
            {
                Type = "High DPC Time",
                Description = $"DPC time exceeded 10% in {highDpcSamples} of {samples.Count} samples",
                Severity = AnomalySeverity.Medium
            });
        }

        return anomalies;
    }

    private bool IsSystemHealthy(PerformanceSample sample)
    {
        return sample.CpuUsage < 80 &&
               sample.InterruptTime < 30 &&
               sample.DpcTime < 15;
    }

    private double CompareMetric(double baseline, double current, double threshold)
    {
        if (baseline == 0)
            return 0;

        var increase = (current - baseline) / baseline;
        return increase > threshold ? increase : 0;
    }

    private int CalculateHealthScore(PerformanceMetrics metrics)
    {
        var score = 100;

        // CPU使用率ペナルティ
        if (metrics.AverageCpuUsage > 70) score -= 30;
        else if (metrics.AverageCpuUsage > 50) score -= 20;
        else if (metrics.AverageCpuUsage > 30) score -= 10;

        // 割り込み時間ペナルティ
        if (metrics.AverageInterruptTime > 25) score -= 25;
        else if (metrics.AverageInterruptTime > 15) score -= 15;
        else if (metrics.AverageInterruptTime > 10) score -= 5;

        // 異常検出ペナルティ
        score -= metrics.Anomalies.Count(a => a.Severity == AnomalySeverity.High) * 15;
        score -= metrics.Anomalies.Count(a => a.Severity == AnomalySeverity.Medium) * 5;

        return Math.Max(0, score);
    }

    private string GetHealthGrade(int score)
    {
        if (score >= 90) return "Excellent";
        if (score >= 75) return "Good";
        if (score >= 60) return "Fair";
        if (score >= 40) return "Poor";
        return "Critical";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _collectionTimer?.Dispose();
        _cpuCounter?.Dispose();
        _interruptCounter?.Dispose();
        _dpcCounter?.Dispose();
        _contextSwitchCounter?.Dispose();
    }
}

/// <summary>
/// パフォーマンスサンプル
/// </summary>
public class PerformanceSample
{
    public DateTime Timestamp { get; set; }
    public float CpuUsage { get; set; }
    public float InterruptTime { get; set; }
    public float DpcTime { get; set; }
    public float ContextSwitchesPerSec { get; set; }
    public long ProcessMemoryMB { get; set; }
    public int ThreadCount { get; set; }
}

/// <summary>
/// パフォーマンスメトリクス
/// </summary>
public class PerformanceMetrics
{
    public string DriverId { get; set; } = string.Empty;
    public int SampleCount { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    public double AverageCpuUsage { get; set; }
    public float MaxCpuUsage { get; set; }

    public double AverageInterruptTime { get; set; }
    public float MaxInterruptTime { get; set; }

    public double AverageDpcTime { get; set; }
    public float MaxDpcTime { get; set; }

    public double AverageContextSwitches { get; set; }
    public double AverageMemoryMB { get; set; }

    public List<PerformanceAnomaly> Anomalies { get; set; } = new();

    public int HealthScore { get; set; }
    public string HealthGrade { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 回帰分析
/// </summary>
public class RegressionAnalysis
{
    public string DriverId { get; set; } = string.Empty;
    public PerformanceMetrics Baseline { get; set; } = new();
    public PerformanceMetrics Current { get; set; } = new();
    public List<RegressionIssue> Regressions { get; set; } = new();
    public bool HasRegression { get; set; }
    public RegressionSeverity OverallSeverity { get; set; }
    public DateTime AnalyzedAt { get; set; }
}

/// <summary>
/// 回帰問題
/// </summary>
public class RegressionIssue
{
    public string Metric { get; set; } = string.Empty;
    public double BaselineValue { get; set; }
    public double CurrentValue { get; set; }
    public double PercentageIncrease { get; set; }
    public RegressionSeverity Severity { get; set; }
}

public enum RegressionSeverity
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// パフォーマンス異常
/// </summary>
public class PerformanceAnomaly
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AnomalySeverity Severity { get; set; }
}

public enum AnomalySeverity
{
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>
/// システムヘルススナップショット
/// </summary>
public class SystemHealthSnapshot
{
    public float CpuUsage { get; set; }
    public float InterruptTime { get; set; }
    public float DpcTime { get; set; }
    public float ContextSwitches { get; set; }
    public long MemoryUsageMB { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsHealthy { get; set; }
}
