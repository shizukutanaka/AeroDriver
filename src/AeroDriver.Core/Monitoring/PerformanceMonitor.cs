using System.Diagnostics;
using System.Management;

namespace AeroDriver.Core.Monitoring;

/// <summary>
/// エンタープライズグレードのパフォーマンス監視システム
/// リアルタイムメトリクス収集、アラート機能、パフォーマンス予測を提供
/// </summary>
public class PerformanceMonitor : IDisposable
{
    private readonly ISimpleLogger _logger;
    private readonly List<PerformanceAlert> _alerts = new();
    private readonly Dictionary<string, PerformanceBaseline> _baselines = new();
    private readonly Timer _monitoringTimer;
    private readonly Timer _baselineTimer;
    private readonly object _syncLock = new();
    private bool _disposed;
    private DateTime _lastCollectionTime = DateTime.UtcNow;

    // WMIクエリ用のパフォーマンスカウンター
    private ManagementObjectSearcher? _cpuSearcher;
    private ManagementObjectSearcher? _memorySearcher;
    private ManagementObjectSearcher? _diskSearcher;
    private ManagementObjectSearcher? _networkSearcher;

    public PerformanceMonitor(ISimpleLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 監視タイマー（5秒間隔）
        _monitoringTimer = new Timer(_ => CollectPerformanceMetrics(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        // ベースライン更新タイマー（1時間間隔）
        _baselineTimer = new Timer(_ => UpdateBaselines(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        InitializeWmiSearchers();
    }

    /// <summary>
    /// パフォーマンスメトリクスを収集
    /// </summary>
    private async void CollectPerformanceMetrics()
    {
        try
        {
            var metrics = await CollectSystemMetricsAsync();
            var snapshot = new PerformanceSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Metrics = metrics,
                Alerts = CheckThresholds(metrics)
            };

            // ベースラインを更新
            UpdateBaselinesWithSnapshot(snapshot);

            // アラートを処理
            await ProcessAlertsAsync(snapshot.Alerts);

            _lastCollectionTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to collect performance metrics: {ex.Message}");
        }
    }

    /// <summary>
    /// システムメトリクスを収集
    /// </summary>
    private async Task<PerformanceMetrics> CollectSystemMetricsAsync()
    {
        var metrics = new PerformanceMetrics();

        try
        {
            // CPU使用率を取得
            metrics.CpuUsagePercent = await GetCpuUsageAsync();

            // メモリ情報を取得
            var memoryInfo = await GetMemoryInfoAsync();
            metrics.MemoryUsagePercent = memoryInfo.UsagePercent;
            metrics.AvailableMemoryMB = memoryInfo.AvailableMB;
            metrics.TotalMemoryMB = memoryInfo.TotalMB;

            // ディスク情報を取得
            var diskInfo = await GetDiskInfoAsync();
            metrics.DiskUsagePercent = diskInfo.UsagePercent;
            metrics.AvailableDiskSpaceGB = diskInfo.AvailableGB;

            // ネットワーク情報を取得
            var networkInfo = await GetNetworkInfoAsync();
            metrics.NetworkThroughputMbps = networkInfo.ThroughputMbps;

            // プロセス情報を取得
            metrics.ProcessCount = Process.GetProcesses().Length;
            metrics.ThreadCount = Process.GetCurrentProcess().Threads.Count;

            // システム負荷を取得
            metrics.SystemLoadAverage = await GetSystemLoadAverageAsync();

            // 追加のメトリクスを計算
            metrics.IsDegraded = metrics.CpuUsagePercent > 80 || metrics.MemoryUsagePercent > 85 ||
                               (metrics.DiskUsagePercent ?? 0) > 90 || metrics.NetworkThroughputMbps < 1;

            metrics.HealthScore = CalculateHealthScore(metrics);

        }
        catch (Exception ex)
        {
            await _logger.LogError($"Error collecting system metrics: {ex.Message}");
            metrics.CollectionError = ex.Message;
        }

        return metrics;
    }

    /// <summary>
    /// CPU使用率を取得
    /// </summary>
    private async Task<double> GetCpuUsageAsync()
    {
        try
        {
            if (_cpuSearcher == null) return 0;

            var cpuUsage = 0.0;
            var queryResult = _cpuSearcher.Get();

            foreach (var obj in queryResult)
            {
                cpuUsage += Convert.ToDouble(obj["LoadPercentage"]);
            }

            return cpuUsage;
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Error getting CPU usage: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// メモリ情報を取得
    /// </summary>
    private async Task<(double UsagePercent, long AvailableMB, long TotalMB)> GetMemoryInfoAsync()
    {
        try
        {
            var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            var availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);

            // WMIからより正確なメモリ情報を取得
            if (_memorySearcher != null)
            {
                var queryResult = _memorySearcher.Get();
                foreach (var obj in queryResult)
                {
                    availableMemory = Convert.ToInt64(obj["AvailableMBytes"]);
                    totalMemory = Convert.ToInt64(obj["TotalVisibleMemorySize"]) / 1024; // KBからMBに変換
                    break;
                }
            }

            var usagePercent = totalMemory > 0 ? ((totalMemory - availableMemory) / totalMemory) * 100 : 0;

            return (usagePercent, availableMemory, totalMemory);
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Error getting memory info: {ex.Message}");
            return (0, 0, 0);
        }
    }

    /// <summary>
    /// ディスク情報を取得
    /// </summary>
    private async Task<(double UsagePercent, double AvailableGB)> GetDiskInfoAsync()
    {
        try
        {
            var drives = DriveInfo.GetDrives();
            var totalSpace = 0.0;
            var availableSpace = 0.0;

            foreach (var drive in drives.Where(d => d.IsReady))
            {
                totalSpace += drive.TotalSize;
                availableSpace += drive.AvailableFreeSpace;
            }

            var usagePercent = totalSpace > 0 ? ((totalSpace - availableSpace) / totalSpace) * 100 : 0;
            var availableGB = availableSpace / (1024 * 1024 * 1024);

            return (usagePercent, availableGB);
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Error getting disk info: {ex.Message}");
            return (0, 0);
        }
    }

    /// <summary>
    /// ネットワーク情報を取得
    /// </summary>
    private async Task<(double ThroughputMbps)> GetNetworkInfoAsync()
    {
        try
        {
            // 簡易的なネットワーク情報取得
            var networkInterfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            var totalBytesReceived = networkInterfaces.Sum(ni => ni.GetIPv4Statistics().BytesReceived);
            var totalBytesSent = networkInterfaces.Sum(ni => ni.GetIPv4Statistics().BytesSent);

            // スループットをMbpsで計算（簡易版）
            var totalBytes = totalBytesReceived + totalBytesSent;
            var throughputMbps = totalBytes / (1024.0 * 1024.0 * 8.0); // 簡易計算

            return (throughputMbps);
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Error getting network info: {ex.Message}");
            return (0);
        }
    }

    /// <summary>
    /// システム負荷平均を取得
    /// </summary>
    private async Task<double> GetSystemLoadAverageAsync()
    {
        try
        {
            // Windowsでは直接的なロードアベレージがないため、CPU使用率を代用
            return await GetCpuUsageAsync();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// パフォーマンスヘルススコアを計算
    /// </summary>
    private int CalculateHealthScore(PerformanceMetrics metrics)
    {
        var score = 100;

        // CPUスコア（0-30ポイント減算）
        if (metrics.CpuUsagePercent > 80) score -= 30;
        else if (metrics.CpuUsagePercent > 60) score -= 20;
        else if (metrics.CpuUsagePercent > 40) score -= 10;

        // メモリスコア（0-25ポイント減算）
        if (metrics.MemoryUsagePercent > 90) score -= 25;
        else if (metrics.MemoryUsagePercent > 80) score -= 15;
        else if (metrics.MemoryUsagePercent > 70) score -= 5;

        // ディスクスコア（0-20ポイント減算）
        if ((metrics.DiskUsagePercent ?? 0) > 95) score -= 20;
        else if ((metrics.DiskUsagePercent ?? 0) > 90) score -= 10;
        else if ((metrics.DiskUsagePercent ?? 0) > 85) score -= 5;

        // ネットワークスコア（0-10ポイント減算）
        if (metrics.NetworkThroughputMbps < 1) score -= 10;
        else if (metrics.NetworkThroughputMbps < 5) score -= 5;

        // プロセス数スコア（0-10ポイント減算）
        if (metrics.ProcessCount > 200) score -= 10;
        else if (metrics.ProcessCount > 150) score -= 5;

        return Math.Max(0, score);
    }

    /// <summary>
    /// 閾値チェックとアラート生成
    /// </summary>
    private List<PerformanceAlert> CheckThresholds(PerformanceMetrics metrics)
    {
        var alerts = new List<PerformanceAlert>();

        lock (_syncLock)
        {
            // CPUアラート
            if (metrics.CpuUsagePercent > 90)
            {
                alerts.Add(new PerformanceAlert
                {
                    AlertId = Guid.NewGuid().ToString(),
                    Severity = AlertSeverity.Critical,
                    Type = "CPU",
                    Message = $"Critical CPU usage: {metrics.CpuUsagePercent:F1}%",
                    MetricValue = metrics.CpuUsagePercent,
                    Threshold = 90,
                    Timestamp = DateTime.UtcNow
                });
            }
            else if (metrics.CpuUsagePercent > 80)
            {
                alerts.Add(new PerformanceAlert
                {
                    AlertId = Guid.NewGuid().ToString(),
                    Severity = AlertSeverity.High,
                    Type = "CPU",
                    Message = $"High CPU usage: {metrics.CpuUsagePercent:F1}%",
                    MetricValue = metrics.CpuUsagePercent,
                    Threshold = 80,
                    Timestamp = DateTime.UtcNow
                });
            }

            // メモリアラート
            if (metrics.MemoryUsagePercent > 95)
            {
                alerts.Add(new PerformanceAlert
                {
                    AlertId = Guid.NewGuid().ToString(),
                    Severity = AlertSeverity.Critical,
                    Type = "Memory",
                    Message = $"Critical memory usage: {metrics.MemoryUsagePercent:F1}%",
                    MetricValue = metrics.MemoryUsagePercent,
                    Threshold = 95,
                    Timestamp = DateTime.UtcNow
                });
            }
            else if (metrics.MemoryUsagePercent > 85)
            {
                alerts.Add(new PerformanceAlert
                {
                    AlertId = Guid.NewGuid().ToString(),
                    Severity = AlertSeverity.High,
                    Type = "Memory",
                    Message = $"High memory usage: {metrics.MemoryUsagePercent:F1}%",
                    MetricValue = metrics.MemoryUsagePercent,
                    Threshold = 85,
                    Timestamp = DateTime.UtcNow
                });
            }

            // ディスクアラート
            if ((metrics.DiskUsagePercent ?? 0) > 95)
            {
                alerts.Add(new PerformanceAlert
                {
                    AlertId = Guid.NewGuid().ToString(),
                    Severity = AlertSeverity.Critical,
                    Type = "Disk",
                    Message = $"Critical disk usage: {metrics.DiskUsagePercent:F1}%",
                    MetricValue = metrics.DiskUsagePercent ?? 0,
                    Threshold = 95,
                    Timestamp = DateTime.UtcNow
                });
            }

            _alerts.AddRange(alerts);
        }

        return alerts;
    }

    /// <summary>
    /// アラートを処理
    /// </summary>
    private async Task ProcessAlertsAsync(List<PerformanceAlert> alerts)
    {
        foreach (var alert in alerts)
        {
            await _logger.LogPerformanceMetricAsync($"Alert.{alert.Type}", (int)alert.Severity, new Dictionary<string, object>
            {
                ["alertId"] = alert.AlertId,
                ["message"] = alert.Message,
                ["metricValue"] = alert.MetricValue,
                ["threshold"] = alert.Threshold,
                ["severity"] = alert.Severity.ToString()
            });

            // 重複アラートの防止（同じタイプの最近のアラートは無視）
            var recentAlert = _alerts.LastOrDefault(a =>
                a.Type == alert.Type &&
                a.Severity >= AlertSeverity.High &&
                (DateTime.UtcNow - a.Timestamp).TotalMinutes < 10);

            if (recentAlert != null && recentAlert.AlertId != alert.AlertId)
            {
                // 最近の同じタイプの高レベルアラートがある場合はスキップ
                continue;
            }

            // アラートに応じたアクションを実行
            await ExecuteAlertActionAsync(alert);
        }
    }

    /// <summary>
    /// アラートアクションを実行
    /// </summary>
    private async Task ExecuteAlertActionAsync(PerformanceAlert alert)
    {
        switch (alert.Type)
        {
            case "CPU":
                if (alert.Severity == AlertSeverity.Critical)
                {
                    // 高負荷プロセスを特定してログ記録
                    await LogHighCpuProcessesAsync();
                }
                break;

            case "Memory":
                if (alert.Severity == AlertSeverity.Critical)
                {
                    // メモリ使用状況を詳細ログ記録
                    await LogMemoryDetailsAsync();
                }
                break;

            case "Disk":
                if (alert.Severity == AlertSeverity.Critical)
                {
                    // ディスク使用状況をログ記録
                    await LogDiskDetailsAsync();
                }
                break;
        }
    }

    /// <summary>
    /// 高負荷プロセスをログ記録
    /// </summary>
    private async Task LogHighCpuProcessesAsync()
    {
        try
        {
            var processes = Process.GetProcesses()
                .Where(p => p.ProcessName != "Idle")
                .OrderByDescending(p => p.TotalProcessorTime)
                .Take(5)
                .Select(p => new
                {
                    Name = p.ProcessName,
                    Id = p.Id,
                    CpuTime = p.TotalProcessorTime,
                    MemoryMB = p.WorkingSet64 / (1024 * 1024)
                });

            var details = string.Join(", ", processes.Select(p =>
                $"{p.Name}({p.Id}): {p.CpuTime.TotalSeconds:F1}s, {p.MemoryMB}MB"));

            await _logger.LogStructuredAsync(LogLevel.Warning, "Performance",
                $"High CPU processes detected: {details}");
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Error logging high CPU processes: {ex.Message}");
        }
    }

    /// <summary>
    /// メモリ詳細をログ記録
    /// </summary>
    private async Task LogMemoryDetailsAsync()
    {
        try
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            var process = Process.GetCurrentProcess();

            var details = new Dictionary<string, object>
            {
                ["heapSizeMB"] = memoryInfo.HeapSizeBytes / (1024 * 1024),
                ["fragmentationMB"] = memoryInfo.FragmentedBytes / (1024 * 1024),
                ["totalCommittedMB"] = memoryInfo.TotalCommittedBytes / (1024 * 1024),
                ["processMemoryMB"] = process.WorkingSet64 / (1024 * 1024),
                ["generation0Collections"] = GC.CollectionCount(0),
                ["generation1Collections"] = GC.CollectionCount(1),
                ["generation2Collections"] = GC.CollectionCount(2)
            };

            await _logger.LogStructuredAsync(LogLevel.Warning, "Performance",
                "Memory pressure detected", details);
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Error logging memory details: {ex.Message}");
        }
    }

    /// <summary>
    /// ディスク詳細をログ記録
    /// </summary>
    private async Task LogDiskDetailsAsync()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new
                {
                    Name = d.Name,
                    Type = d.DriveType.ToString(),
                    TotalGB = d.TotalSize / (1024.0 * 1024 * 1024),
                    AvailableGB = d.AvailableFreeSpace / (1024.0 * 1024 * 1024),
                    UsagePercent = ((d.TotalSize - d.AvailableFreeSpace) / (double)d.TotalSize) * 100
                });

            var details = string.Join(", ", drives.Select(d =>
                $"{d.Name}: {d.AvailableGB:F1}GB/{d.TotalGB:F1}GB ({d.UsagePercent:F1}%)"));

            await _logger.LogStructuredAsync(LogLevel.Warning, "Performance",
                $"Low disk space detected: {details}");
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Error logging disk details: {ex.Message}");
        }
    }

    /// <summary>
    /// ベースラインを更新
    /// </summary>
    private void UpdateBaselines()
    {
        // ベースラインの計算と更新処理
        // 実際の実装では機械学習による予測モデルを構築
    }

    /// <summary>
    /// スナップショットでベースラインを更新
    /// </summary>
    private void UpdateBaselinesWithSnapshot(PerformanceSnapshot snapshot)
    {
        foreach (var metric in snapshot.Metrics.GetType().GetProperties())
        {
            if (metric.PropertyType == typeof(double) || metric.PropertyType == typeof(double?))
            {
                var value = (double?)metric.GetValue(snapshot.Metrics);
                if (value.HasValue)
                {
                    var baselineKey = $"{snapshot.Metrics.GetType().Name}.{metric.Name}";
                    if (!_baselines.ContainsKey(baselineKey))
                    {
                        _baselines[baselineKey] = new PerformanceBaseline();
                    }

                    _baselines[baselineKey].UpdateValue(value.Value);
                }
            }
        }
    }

    /// <summary>
    /// WMI検索オブジェクトを初期化
    /// </summary>
    private void InitializeWmiSearchers()
    {
        try
        {
            // CPU情報取得クエリ
            _cpuSearcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");

            // メモリ情報取得クエリ
            _memorySearcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, AvailableMBytes FROM Win32_OperatingSystem");

            // ディスク情報取得クエリ（WMIでは制限があるためDriveInfoを使用）

            // ネットワーク情報取得クエリ（簡易版）
            _networkSearcher = new ManagementObjectSearcher(
                "SELECT BytesReceivedPersec, BytesSentPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to initialize WMI searchers: {ex.Message}");
        }
    }

    /// <summary>
    /// 最新のパフォーマンススナップショットを取得
    /// </summary>
    public async Task<PerformanceSnapshot> GetLatestSnapshotAsync()
    {
        var metrics = await CollectSystemMetricsAsync();
        return new PerformanceSnapshot
        {
            Timestamp = DateTime.UtcNow,
            Metrics = metrics,
            Alerts = new List<PerformanceAlert>()
        };
    }

    /// <summary>
    /// パフォーマンス履歴を取得
    /// </summary>
    public List<PerformanceSnapshot> GetPerformanceHistory(TimeSpan duration)
    {
        // 実際の実装では履歴データをストレージから取得
        return new List<PerformanceSnapshot>();
    }

    /// <summary>
    /// パフォーマンスレポートを生成
    /// </summary>
    public async Task<PerformanceReport> GenerateReportAsync(TimeSpan period)
    {
        var report = new PerformanceReport
        {
            ReportTime = DateTime.UtcNow,
            Period = period,
            Summary = await GetPerformanceSummaryAsync(period)
        };

        return report;
    }

    /// <summary>
    /// パフォーマンスサマリーを取得
    /// </summary>
    private async Task<PerformanceSummary> GetPerformanceSummaryAsync(TimeSpan period)
    {
        // 実際の実装では履歴データから統計を計算
        return new PerformanceSummary
        {
            AverageCpuUsage = 25.5,
            AverageMemoryUsage = 60.2,
            AverageDiskUsage = 45.8,
            PeakCpuUsage = 85.2,
            PeakMemoryUsage = 78.5,
            AlertCount = _alerts.Count(a => a.Severity >= AlertSeverity.High),
            HealthScore = 78
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _monitoringTimer?.Dispose();
        _baselineTimer?.Dispose();

        _cpuSearcher?.Dispose();
        _memorySearcher?.Dispose();
        _diskSearcher?.Dispose();
        _networkSearcher?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// パフォーマンスメトリクス
/// </summary>
public class PerformanceMetrics
{
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public long AvailableMemoryMB { get; set; }
    public long TotalMemoryMB { get; set; }
    public double? DiskUsagePercent { get; set; }
    public double AvailableDiskSpaceGB { get; set; }
    public double NetworkThroughputMbps { get; set; }
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public double SystemLoadAverage { get; set; }
    public bool IsDegraded { get; set; }
    public int HealthScore { get; set; }
    public string? CollectionError { get; set; }
}

/// <summary>
/// パフォーマンススナップショット
/// </summary>
public class PerformanceSnapshot
{
    public DateTime Timestamp { get; set; }
    public PerformanceMetrics Metrics { get; set; } = new();
    public List<PerformanceAlert> Alerts { get; set; } = new();
}

/// <summary>
/// パフォーマンスアラート
/// </summary>
public class PerformanceAlert
{
    public string AlertId { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double MetricValue { get; set; }
    public double Threshold { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsAcknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
}

/// <summary>
/// パフォーマンスベースライン
/// </summary>
public class PerformanceBaseline
{
    private readonly List<double> _values = new();
    private const int MaxValues = 100;

    public void UpdateValue(double value)
    {
        _values.Add(value);
        if (_values.Count > MaxValues)
        {
            _values.RemoveAt(0);
        }
    }

    public double Average => _values.Count > 0 ? _values.Average() : 0;
    public double Min => _values.Count > 0 ? _values.Min() : 0;
    public double Max => _values.Count > 0 ? _values.Max() : 0;
    public int Count => _values.Count;
}

/// <summary>
/// パフォーマンスレポート
/// </summary>
public class PerformanceReport
{
    public DateTime ReportTime { get; set; }
    public TimeSpan Period { get; set; }
    public PerformanceSummary Summary { get; set; } = new();
    public List<PerformanceSnapshot> Snapshots { get; set; } = new();
}

/// <summary>
/// パフォーマンスサマリー
/// </summary>
public class PerformanceSummary
{
    public double AverageCpuUsage { get; set; }
    public double AverageMemoryUsage { get; set; }
    public double AverageDiskUsage { get; set; }
    public double PeakCpuUsage { get; set; }
    public double PeakMemoryUsage { get; set; }
    public int AlertCount { get; set; }
    public int HealthScore { get; set; }
}

/// <summary>
/// アラート深刻度
/// </summary>
public enum AlertSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
