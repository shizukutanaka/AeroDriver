// This file has been created as part of monitoring enhancement feature implementation
// It provides comprehensive system monitoring and metrics collection capabilities

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Monitoring;

/// <summary>
/// 統合監視・メトリクス収集システム
/// システム全体の監視とパフォーマンスメトリクスの収集・分析を提供
/// </summary>
public static class SystemMonitor
{
    private static readonly ConcurrentDictionary<string, MetricCollector> _collectors = new();
    private static readonly ConcurrentQueue<MetricSnapshot> _metricHistory = new();
    private static readonly ConcurrentDictionary<string, HealthCheck> _healthChecks = new();
    private static readonly Timer _monitoringTimer;
    private static readonly Timer _healthCheckTimer;
    private static TimeSpan _collectionInterval = TimeSpan.FromSeconds(30);
    private static TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(1);
    private static int _maxHistorySize = 10000;
    private static bool _isMonitoringEnabled;
    private static readonly object _monitorLock = new();

    static SystemMonitor()
    {
        _monitoringTimer = new Timer(_ => CollectMetrics(), null, Timeout.Infinite, Timeout.Infinite);
        _healthCheckTimer = new Timer(_ => RunHealthChecks(), null, Timeout.Infinite, Timeout.Infinite);

        // デフォルトのメトリクスコレクターを登録
        RegisterDefaultCollectors();
    }

    /// <summary>
    /// 監視を開始
    /// </summary>
    public static void StartMonitoring()
    {
        lock (_monitorLock)
        {
            if (_isMonitoringEnabled) return;

            _isMonitoringEnabled = true;
            _monitoringTimer.Change(_collectionInterval, _collectionInterval);
            _healthCheckTimer.Change(_healthCheckInterval, _healthCheckInterval);
        }
    }

    /// <summary>
    /// 監視を停止
    /// </summary>
    public static void StopMonitoring()
    {
        lock (_monitorLock)
        {
            if (!_isMonitoringEnabled) return;

            _isMonitoringEnabled = false;
            _monitoringTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _healthCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>
    /// カスタムメトリクスコレクターを登録
    /// </summary>
    public static void RegisterCollector(string name, Func<Dictionary<string, object>> collectorFunc, MetricCategory category = MetricCategory.Custom)
    {
        var collector = new MetricCollector
        {
            Name = name,
            CollectorFunc = collectorFunc,
            Category = category,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        _collectors[name] = collector;
    }

    /// <summary>
    /// ヘルスチェックを登録
    /// </summary>
    public static void RegisterHealthCheck(string name, Func<Task<HealthStatus>> checkFunc, HealthCheckPriority priority = HealthCheckPriority.Medium)
    {
        var healthCheck = new HealthCheck
        {
            Name = name,
            CheckFunc = checkFunc,
            Priority = priority,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        _healthChecks[name] = healthCheck;
    }

    /// <summary>
    /// 現在のメトリクスを取得
    /// </summary>
    public static Dictionary<string, object> GetCurrentMetrics(MetricCategory? categoryFilter = null)
    {
        var metrics = new Dictionary<string, object>();

        foreach (var collector in _collectors.Values.Where(c => c.IsEnabled))
        {
            if (categoryFilter.HasValue && collector.Category != categoryFilter.Value)
                continue;

            try
            {
                var collectorMetrics = collector.CollectorFunc();
                foreach (var kvp in collectorMetrics)
                {
                    metrics[$"{collector.Name}.{kvp.Key}"] = kvp.Value;
                }
            }
            catch (Exception ex)
            {
                metrics[$"{collector.Name}.error"] = ex.Message;
            }
        }

        return metrics;
    }

    /// <summary>
    /// ヘルスステータスを取得
    /// </summary>
    public static async Task<SystemHealthStatus> GetHealthStatusAsync()
    {
        var status = new SystemHealthStatus
        {
            Timestamp = DateTime.UtcNow,
            OverallStatus = HealthStatus.Healthy
        };

        var checkTasks = _healthChecks.Values
            .Where(hc => hc.IsEnabled)
            .OrderByDescending(hc => hc.Priority)
            .Select(async hc =>
            {
                try
                {
                    var result = await hc.CheckFunc();
                    return new HealthCheckResult
                    {
                        Name = hc.Name,
                        Status = result,
                        LastChecked = DateTime.UtcNow,
                        Priority = hc.Priority
                    };
                }
                catch (Exception ex)
                {
                    return new HealthCheckResult
                    {
                        Name = hc.Name,
                        Status = HealthStatus.Unhealthy,
                        ErrorMessage = ex.Message,
                        LastChecked = DateTime.UtcNow,
                        Priority = hc.Priority
                    };
                }
            });

        status.CheckResults = await Task.WhenAll(checkTasks);

        // 全体的なヘルスステータスを決定
        if (status.CheckResults.Any(r => r.Status == HealthStatus.Unhealthy && r.Priority >= HealthCheckPriority.High))
        {
            status.OverallStatus = HealthStatus.Unhealthy;
        }
        else if (status.CheckResults.Any(r => r.Status == HealthStatus.Degraded))
        {
            status.OverallStatus = HealthStatus.Degraded;
        }

        return status;
    }

    /// <summary>
    /// メトリクス履歴を取得
    /// </summary>
    public static IEnumerable<MetricSnapshot> GetMetricHistory(TimeSpan? timeRange = null, string? metricFilter = null)
    {
        var cutoffTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : DateTime.MinValue;

        return _metricHistory
            .Where(snapshot => snapshot.Timestamp >= cutoffTime)
            .Where(snapshot => string.IsNullOrEmpty(metricFilter) ||
                             snapshot.Metrics.Keys.Any(k => k.Contains(metricFilter, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(snapshot => snapshot.Timestamp);
    }

    /// <summary>
    /// メトリクス統計を取得
    /// </summary>
    public static MetricStatistics GetMetricStatistics(string metricName, TimeSpan timeRange)
    {
        var history = GetMetricHistory(timeRange, metricName).ToArray();

        if (!history.Any())
        {
            return new MetricStatistics
            {
                MetricName = metricName,
                SampleCount = 0
            };
        }

        var values = history
            .SelectMany(h => h.Metrics.Where(m => m.Key.Contains(metricName, StringComparison.OrdinalIgnoreCase)))
            .Select(m => Convert.ToDouble(m.Value))
            .Where(v => !double.IsNaN(v))
            .ToArray();

        if (!values.Any())
        {
            return new MetricStatistics
            {
                MetricName = metricName,
                SampleCount = 0
            };
        }

        return new MetricStatistics
        {
            MetricName = metricName,
            SampleCount = values.Length,
            Average = values.Average(),
            Minimum = values.Min(),
            Maximum = values.Max(),
            StandardDeviation = CalculateStandardDeviation(values),
            Percentile95 = CalculatePercentile(values, 95),
            Percentile99 = CalculatePercentile(values, 99),
            TimeRange = timeRange
        };
    }

    /// <summary>
    /// パフォーマンスレポートを生成
    /// </summary>
    public static PerformanceReport GeneratePerformanceReport(TimeSpan timeRange)
    {
        var currentMetrics = GetCurrentMetrics();
        var healthStatus = GetHealthStatusAsync().GetAwaiter().GetResult();

        var report = new PerformanceReport
        {
            GeneratedAt = DateTime.UtcNow,
            TimeRange = timeRange,
            CurrentMetrics = currentMetrics,
            HealthStatus = healthStatus
        };

        // 主要メトリクスの統計を計算
        var keyMetrics = new[] { "cpu", "memory", "disk", "network" };
        report.MetricStatistics = new Dictionary<string, MetricStatistics>();

        foreach (var metric in keyMetrics)
        {
            report.MetricStatistics[metric] = GetMetricStatistics(metric, timeRange);
        }

        // パフォーマンス評価
        report.PerformanceScore = CalculatePerformanceScore(report);

        // 推奨事項の生成
        report.Recommendations = GenerateRecommendations(report);

        return report;
    }

    /// <summary>
    /// アラートを設定
    /// </summary>
    public static void ConfigureAlert(string metricName, AlertCondition condition, double threshold, string message)
    {
        // 実際の実装ではアラート管理システムを統合
        // ここでは簡易的な実装
        Debug.WriteLine($"Alert configured: {metricName} {condition} {threshold} - {message}");
    }

    /// <summary>
    /// デフォルトのメトリクスコレクターを登録
    /// </summary>
    private static void RegisterDefaultCollectors()
    {
        // CPU使用率
        RegisterCollector("cpu", () => new Dictionary<string, object>
        {
            ["usage_percent"] = GetCpuUsage(),
            ["process_count"] = Process.GetProcesses().Length
        }, MetricCategory.System);

        // メモリ使用量
        RegisterCollector("memory", () => new Dictionary<string, object>
        {
            ["managed_mb"] = GC.GetTotalMemory(false) / (1024 * 1024),
            ["process_working_set_mb"] = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024),
            ["system_available_mb"] = GetAvailableMemoryMB()
        }, MetricCategory.System);

        // ディスク使用量
        RegisterCollector("disk", () => new Dictionary<string, object>
        {
            ["system_free_mb"] = GetSystemDiskFreeSpaceMB(),
            ["process_directory_size_mb"] = GetDirectorySizeMB(AppDomain.CurrentDomain.BaseDirectory)
        }, MetricCategory.System);

        // ネットワーク統計
        RegisterCollector("network", () => new Dictionary<string, object>
        {
            ["connections_active"] = GetActiveNetworkConnections(),
            ["bytes_sent_mb"] = 0, // 実際の実装ではネットワークカウンターを使用
            ["bytes_received_mb"] = 0
        }, MetricCategory.System);

        // アプリケーション固有メトリクス
        RegisterCollector("application", () => new Dictionary<string, object>
        {
            ["uptime_seconds"] = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
            ["thread_count"] = Process.GetCurrentProcess().Threads.Count,
            ["handle_count"] = Process.GetCurrentProcess().HandleCount,
            ["gc_collections_gen0"] = GC.CollectionCount(0),
            ["gc_collections_gen1"] = GC.CollectionCount(1),
            ["gc_collections_gen2"] = GC.CollectionCount(2)
        }, MetricCategory.Application);

        // パフォーマンスメトリクス
        RegisterCollector("performance", () => new Dictionary<string, object>
        {
            ["response_time_avg_ms"] = 0, // 実際の実装ではレスポンスタイムを収集
            ["throughput_requests_per_sec"] = 0,
            ["error_rate_percent"] = 0
        }, MetricCategory.Performance);
    }

    /// <summary>
    /// デフォルトのヘルスチェックを登録
    /// </summary>
    private static void RegisterDefaultHealthChecks()
    {
        // メモリヘルスチェック
        RegisterHealthCheck("memory", async () =>
        {
            var memoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
            return memoryMB > 800 ? HealthStatus.Unhealthy :
                   memoryMB > 500 ? HealthStatus.Degraded :
                   HealthStatus.Healthy;
        }, HealthCheckPriority.High);

        // ディスク容量ヘルスチェック
        RegisterHealthCheck("disk_space", async () =>
        {
            var freeSpaceMB = GetSystemDiskFreeSpaceMB();
            return freeSpaceMB < 100 ? HealthStatus.Unhealthy :
                   freeSpaceMB < 500 ? HealthStatus.Degraded :
                   HealthStatus.Healthy;
        }, HealthCheckPriority.Medium);

        // CPU使用率ヘルスチェック
        RegisterHealthCheck("cpu_usage", async () =>
        {
            var cpuUsage = GetCpuUsage();
            return cpuUsage > 90 ? HealthStatus.Unhealthy :
                   cpuUsage > 70 ? HealthStatus.Degraded :
                   HealthStatus.Healthy;
        }, HealthCheckPriority.Medium);

        // アプリケーション応答性チェック
        RegisterHealthCheck("application_responsiveness", async () =>
        {
            // 簡易的な応答性チェック
            var startTime = DateTime.UtcNow;
            await Task.Delay(1); // 最小の非同期操作
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return responseTime > 1000 ? HealthStatus.Unhealthy :
                   responseTime > 100 ? HealthStatus.Degraded :
                   HealthStatus.Healthy;
        }, HealthCheckPriority.High);
    }

    /// <summary>
    /// メトリクス収集タイマーコールバック
    /// </summary>
    private static void CollectMetrics()
    {
        if (!_isMonitoringEnabled) return;

        try
        {
            var snapshot = new MetricSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Metrics = GetCurrentMetrics()
            };

            _metricHistory.Enqueue(snapshot);

            // 履歴サイズを制限
            while (_metricHistory.Count > _maxHistorySize)
            {
                _metricHistory.TryDequeue(out _);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Metrics collection error: {ex.Message}");
        }
    }

    /// <summary>
    /// ヘルスチェックタイマーコールバック
    /// </summary>
    private static void RunHealthChecks()
    {
        if (!_isMonitoringEnabled) return;

        // ヘルスチェックは非同期で実行
        Task.Run(async () =>
        {
            try
            {
                await GetHealthStatusAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Health check error: {ex.Message}");
            }
        });
    }

    #region Helper Methods

    private static double GetCpuUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var cpuTime = process.TotalProcessorTime.TotalMilliseconds;
            var uptime = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;

            if (uptime > 0)
            {
                return (cpuTime / uptime) * 100 / Environment.ProcessorCount;
            }
        }
        catch
        {
            // CPU使用率の取得に失敗した場合は0を返す
        }

        return 0;
    }

    private static long GetAvailableMemoryMB()
    {
        try
        {
            // 簡易的な実装 - 実際のシステムではより正確な方法を使用
            var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64 / (1024 * 1024);
            return Math.Max(1024 - workingSet, 0); // 仮定のシステムメモリ
        }
        catch
        {
            return 1024; // デフォルト値
        }
    }

    private static long GetSystemDiskFreeSpaceMB()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:");
            return drive.AvailableFreeSpace / (1024 * 1024);
        }
        catch
        {
            return 1024 * 1024; // デフォルト値
        }
    }

    private static long GetDirectorySizeMB(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            return dir.GetFiles("*", SearchOption.AllDirectories)
                     .Sum(f => f.Length) / (1024 * 1024);
        }
        catch
        {
            return 0;
        }
    }

    private static int GetActiveNetworkConnections()
    {
        try
        {
            // 簡易的な実装 - 実際のシステムではネットワークAPIを使用
            return Process.GetProcesses().Length / 10; // 仮定値
        }
        catch
        {
            return 0;
        }
    }

    private static double CalculateStandardDeviation(double[] values)
    {
        if (values.Length <= 1) return 0;

        var average = values.Average();
        var sumOfSquaresOfDifferences = values.Sum(val => Math.Pow(val - average, 2));
        return Math.Sqrt(sumOfSquaresOfDifferences / (values.Length - 1));
    }

    private static double CalculatePercentile(double[] values, double percentile)
    {
        if (values.Length == 0) return 0;

        Array.Sort(values);
        var index = (int)Math.Ceiling((percentile / 100.0) * values.Length) - 1;
        index = Math.Max(0, Math.Min(index, values.Length - 1));
        return values[index];
    }

    private static double CalculatePerformanceScore(PerformanceReport report)
    {
        var score = 100.0;

        // メモリ使用量による減点
        var memoryMB = Convert.ToDouble(report.CurrentMetrics.GetValueOrDefault("memory.managed_mb", 0));
        if (memoryMB > 500) score -= 20;
        else if (memoryMB > 200) score -= 10;

        // CPU使用量による減点
        var cpuPercent = Convert.ToDouble(report.CurrentMetrics.GetValueOrDefault("cpu.usage_percent", 0));
        if (cpuPercent > 80) score -= 20;
        else if (cpuPercent > 50) score -= 10;

        // ヘルスステータスによる減点
        if (report.HealthStatus.OverallStatus == HealthStatus.Unhealthy) score -= 30;
        else if (report.HealthStatus.OverallStatus == HealthStatus.Degraded) score -= 15;

        return Math.Max(0, score);
    }

    private static List<string> GenerateRecommendations(PerformanceReport report)
    {
        var recommendations = new List<string>();

        var memoryMB = Convert.ToDouble(report.CurrentMetrics.GetValueOrDefault("memory.managed_mb", 0));
        if (memoryMB > 500)
        {
            recommendations.Add("High memory usage detected. Consider optimizing memory allocation or increasing system memory.");
        }

        var cpuPercent = Convert.ToDouble(report.CurrentMetrics.GetValueOrDefault("cpu.usage_percent", 0));
        if (cpuPercent > 80)
        {
            recommendations.Add("High CPU usage detected. Consider optimizing CPU-intensive operations or scaling resources.");
        }

        if (report.HealthStatus.OverallStatus != HealthStatus.Healthy)
        {
            recommendations.Add("System health issues detected. Review health check results for specific issues.");
        }

        var diskFreeMB = Convert.ToDouble(report.CurrentMetrics.GetValueOrDefault("disk.system_free_mb", 0));
        if (diskFreeMB < 1000)
        {
            recommendations.Add("Low disk space detected. Consider cleaning up old files or increasing disk capacity.");
        }

        return recommendations;
    }

    #endregion

    #region Data Classes

    /// <summary>
    /// メトリクスコレクター
    /// </summary>
    private class MetricCollector
    {
        public string Name { get; set; } = string.Empty;
        public Func<Dictionary<string, object>> CollectorFunc { get; set; } = null!;
        public MetricCategory Category { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// メトリクススナップショット
    /// </summary>
    private class MetricSnapshot
    {
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new();
    }

    /// <summary>
    /// ヘルスチェック
    /// </summary>
    private class HealthCheck
    {
        public string Name { get; set; } = string.Empty;
        public Func<Task<HealthStatus>> CheckFunc { get; set; } = null!;
        public HealthCheckPriority Priority { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// メトリクスカテゴリ
    /// </summary>
    public enum MetricCategory
    {
        System,
        Application,
        Performance,
        Custom
    }

    /// <summary>
    /// ヘルスステータス
    /// </summary>
    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy
    }

    /// <summary>
    /// ヘルスチェック優先度
    /// </summary>
    public enum HealthCheckPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// アラート条件
    /// </summary>
    public enum AlertCondition
    {
        GreaterThan,
        LessThan,
        Equals,
        NotEquals
    }

    /// <summary>
    /// システムヘルスステータス
    /// </summary>
    public class SystemHealthStatus
    {
        public DateTime Timestamp { get; set; }
        public HealthStatus OverallStatus { get; set; }
        public HealthCheckResult[] CheckResults { get; set; } = Array.Empty<HealthCheckResult>();
    }

    /// <summary>
    /// ヘルスチェック結果
    /// </summary>
    public class HealthCheckResult
    {
        public string Name { get; set; } = string.Empty;
        public HealthStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime LastChecked { get; set; }
        public HealthCheckPriority Priority { get; set; }
    }

    /// <summary>
    /// メトリクス統計
    /// </summary>
    public class MetricStatistics
    {
        public string MetricName { get; set; } = string.Empty;
        public int SampleCount { get; set; }
        public double Average { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double StandardDeviation { get; set; }
        public double Percentile95 { get; set; }
        public double Percentile99 { get; set; }
        public TimeSpan TimeRange { get; set; }
    }

    /// <summary>
    /// パフォーマンスレポート
    /// </summary>
    public class PerformanceReport
    {
        public DateTime GeneratedAt { get; set; }
        public TimeSpan TimeRange { get; set; }
        public Dictionary<string, object> CurrentMetrics { get; set; } = new();
        public SystemHealthStatus HealthStatus { get; set; } = null!;
        public Dictionary<string, MetricStatistics> MetricStatistics { get; set; } = new();
        public double PerformanceScore { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    #endregion
}
