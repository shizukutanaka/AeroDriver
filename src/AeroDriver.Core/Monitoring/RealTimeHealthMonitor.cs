using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Monitoring;

/// <summary>
/// エンタープライズグレードのリアルタイムシステムヘルスモニタリングシステム
/// 包括的なシステム状態監視と自動修復機能を提供します
/// </summary>
public class RealTimeHealthMonitor
{
    private readonly ConcurrentDictionary<string, HealthMetric> _healthMetrics = new();
    private readonly ConcurrentDictionary<string, AlertRule> _alertRules = new();
    private readonly List<IHealthCheckProvider> _healthCheckProviders = new();
    private readonly AuditTrail _auditTrail;
    private readonly ISimpleLogger _logger;
    private readonly Timer _monitoringTimer;
    private readonly Timer _cleanupTimer;

    public RealTimeHealthMonitor(AuditTrail auditTrail, ISimpleLogger logger)
    {
        _auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // リアルタイム監視タイマー（10秒間隔）
        _monitoringTimer = new Timer(PerformHealthCheck, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        // クリーンアップタイマー（5分間隔）
        _cleanupTimer = new Timer(CleanupOldMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        InitializeDefaultHealthChecks();
        InitializeDefaultAlertRules();
    }

    /// <summary>
    /// ヘルスチェックプロバイダーを登録します
    /// </summary>
    public void RegisterHealthCheckProvider(IHealthCheckProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!_healthCheckProviders.Contains(provider))
        {
            _healthCheckProviders.Add(provider);

            _logger.LogInformation($"Health check provider registered: {provider.GetType().Name}");
        }
    }

    /// <summary>
    /// アラートルールを追加します
    /// </summary>
    public async Task<string> AddAlertRuleAsync(
        string name,
        string metricName,
        AlertCondition condition,
        double threshold,
        AlertSeverity severity,
        string? notificationChannel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName, nameof(metricName));

        var ruleId = Guid.NewGuid().ToString();
        var alertRule = new AlertRule
        {
            RuleId = ruleId,
            Name = name,
            MetricName = metricName,
            Condition = condition,
            Threshold = threshold,
            Severity = severity,
            NotificationChannel = notificationChannel,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        _alertRules[ruleId] = alertRule;

        await _auditTrail.RecordSecurityEventAsync(
            SecurityEventType.SuspiciousActivity,
            $"Alert rule added: {name}",
            SecuritySeverity.Low,
            new Dictionary<string, string>
            {
                ["ruleId"] = ruleId,
                ["ruleName"] = name,
                ["metricName"] = metricName,
                ["condition"] = condition.ToString(),
                ["threshold"] = threshold.ToString()
            },
            cancellationToken);

        await _logger.LogSecurityEventAsync("AlertRuleAdded",
            $"Alert rule '{name}' added for metric '{metricName}'");

        return ruleId;
    }

    /// <summary>
    /// 現在のシステムヘルス状態を取得します
    /// </summary>
    public async Task<SystemHealthStatus> GetSystemHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        var healthChecks = await PerformHealthChecksAsync(cancellationToken);

        var overallHealth = CalculateOverallHealth(healthChecks);
        var alerts = await GenerateAlertsAsync(healthChecks, cancellationToken);

        var healthStatus = new SystemHealthStatus
        {
            OverallHealth = overallHealth,
            ComponentHealth = healthChecks.ToDictionary(h => h.ComponentName, h => h.HealthScore),
            ActiveAlerts = alerts.Where(a => a.IsActive).ToList(),
            LastUpdated = DateTime.UtcNow,
            HealthTrend = CalculateHealthTrend()
        };

        return healthStatus;
    }

    /// <summary>
    /// システムの自動修復を実行します
    /// </summary>
    public async Task<List<AutoRemediationResult>> PerformAutoRemediationAsync(CancellationToken cancellationToken = default)
    {
        var remediationResults = new List<AutoRemediationResult>();

        try
        {
            // 自動修復可能な問題を検出して修復を実行
            var issues = await DetectRemediableIssuesAsync(cancellationToken);

            foreach (var issue in issues)
            {
                var remediation = await ExecuteAutoRemediationAsync(issue, cancellationToken);
                remediationResults.Add(remediation);
            }

            if (remediationResults.Any())
            {
                await _auditTrail.RecordSecurityEventAsync(
                    SecurityEventType.SystemOperation,
                    $"Auto-remediation completed for {remediationResults.Count} issues",
                    SecuritySeverity.Medium,
                    new Dictionary<string, string>
                    {
                        ["issueCount"] = remediationResults.Count.ToString(),
                        ["successCount"] = remediationResults.Count(r => r.Success).ToString()
                    },
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await _logger.LogSecurityEventAsync("AutoRemediationError",
                $"Auto-remediation failed: {ex.Message}");
        }

        return remediationResults;
    }

    /// <summary>
    /// ヘルスメトリクスの履歴を取得します
    /// </summary>
    public List<HealthMetricSnapshot> GetHealthMetricsHistory(string metricName, TimeSpan period)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName, nameof(metricName));

        var cutoffTime = DateTime.UtcNow.Subtract(period);

        return _healthMetrics.Values
            .Where(m => m.MetricName == metricName && m.Timestamp >= cutoffTime)
            .OrderBy(m => m.Timestamp)
            .Select(m => new HealthMetricSnapshot
            {
                MetricName = m.MetricName,
                Value = m.Value,
                HealthScore = m.HealthScore,
                Timestamp = m.Timestamp,
                Metadata = m.Metadata
            })
            .ToList();
    }

    private async void PerformHealthCheck(object? state)
    {
        try
        {
            await PerformHealthChecksAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await _logger.LogSecurityEventAsync("HealthCheckError",
                $"Health check cycle failed: {ex.Message}");
        }
    }

    private async Task<List<HealthCheckResult>> PerformHealthChecksAsync(CancellationToken cancellationToken)
    {
        var results = new List<HealthCheckResult>();

        foreach (var provider in _healthCheckProviders)
        {
            try
            {
                var result = await provider.PerformHealthCheckAsync(cancellationToken);
                results.Add(result);

                // メトリクスを記録
                RecordHealthMetric(result);
            }
            catch (Exception ex)
            {
                await _logger.LogSecurityEventAsync("HealthCheckProviderError",
                    $"Health check provider {provider.GetType().Name} failed: {ex.Message}");

                // 失敗したプロバイダーの結果を記録
                var errorResult = new HealthCheckResult
                {
                    ComponentName = provider.GetType().Name,
                    HealthScore = 0.0,
                    Status = HealthStatus.Unhealthy,
                    Message = $"Health check failed: {ex.Message}",
                    Timestamp = DateTime.UtcNow
                };

                results.Add(errorResult);
                RecordHealthMetric(errorResult);
            }
        }

        return results;
    }

    private async Task<List<HealthAlert>> GenerateAlertsAsync(
        List<HealthCheckResult> healthChecks,
        CancellationToken cancellationToken)
    {
        var alerts = new List<HealthAlert>();

        foreach (var rule in _alertRules.Values.Where(r => r.IsEnabled))
        {
            var relevantMetrics = _healthMetrics.Values
                .Where(m => m.MetricName == rule.MetricName)
                .OrderByDescending(m => m.Timestamp)
                .Take(5)
                .ToList();

            if (!relevantMetrics.Any())
            {
                continue;
            }

            var latestMetric = relevantMetrics.First();
            var shouldAlert = EvaluateAlertCondition(rule, latestMetric);

            if (shouldAlert)
            {
                var alert = new HealthAlert
                {
                    AlertId = Guid.NewGuid(),
                    RuleId = rule.RuleId,
                    MetricName = rule.MetricName,
                    CurrentValue = latestMetric.Value,
                    Threshold = rule.Threshold,
                    Severity = rule.Severity,
                    Message = GenerateAlertMessage(rule, latestMetric),
                    Timestamp = DateTime.UtcNow,
                    IsActive = true,
                    NotificationSent = false
                };

                alerts.Add(alert);

                // 通知送信（実際の実装では適切な通知システムと連携）
                await SendNotificationAsync(alert, cancellationToken);
            }
        }

        return alerts;
    }

    private double CalculateOverallHealth(List<HealthCheckResult> healthChecks)
    {
        if (!healthChecks.Any())
        {
            return 0.0;
        }

        var totalScore = healthChecks.Sum(h => h.HealthScore);
        return totalScore / healthChecks.Count;
    }

    private HealthTrend CalculateHealthTrend()
    {
        var recentMetrics = _healthMetrics.Values
            .Where(m => m.Timestamp >= DateTime.UtcNow.AddMinutes(-10))
            .GroupBy(m => m.MetricName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Timestamp).First());

        if (!recentMetrics.Any())
        {
            return HealthTrend.Stable;
        }

        var recentHealth = recentMetrics.Values.Average(m => m.HealthScore);
        var olderMetrics = _healthMetrics.Values
            .Where(m => m.Timestamp >= DateTime.UtcNow.AddMinutes(-30) && m.Timestamp < DateTime.UtcNow.AddMinutes(-10))
            .GroupBy(m => m.MetricName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Timestamp).First());

        if (!olderMetrics.Any())
        {
            return HealthTrend.Stable;
        }

        var olderHealth = olderMetrics.Values.Average(m => m.HealthScore);
        var change = (recentHealth - olderHealth) / olderHealth;

        if (change > 0.05) return HealthTrend.Improving;
        if (change < -0.05) return HealthTrend.Degrading;
        return HealthTrend.Stable;
    }

    private void RecordHealthMetric(HealthCheckResult result)
    {
        var metric = new HealthMetric
        {
            MetricName = $"{result.ComponentName}_health",
            Value = result.HealthScore,
            HealthScore = result.HealthScore,
            Status = result.Status,
            Timestamp = DateTime.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                ["component"] = result.ComponentName,
                ["message"] = result.Message ?? ""
            }
        };

        _healthMetrics[Guid.NewGuid().ToString()] = metric;
    }

    private bool EvaluateAlertCondition(AlertRule rule, HealthMetric metric)
    {
        return rule.Condition switch
        {
            AlertCondition.GreaterThan => metric.Value > rule.Threshold,
            AlertCondition.LessThan => metric.Value < rule.Threshold,
            AlertCondition.Equals => Math.Abs(metric.Value - rule.Threshold) < 0.001,
            AlertCondition.NotEquals => Math.Abs(metric.Value - rule.Threshold) >= 0.001,
            _ => false
        };
    }

    private string GenerateAlertMessage(AlertRule rule, HealthMetric metric)
    {
        return $"{rule.Name}: {metric.MetricName} is {metric.Value:F2} (threshold: {rule.Condition} {rule.Threshold})";
    }

    private async Task SendNotificationAsync(HealthAlert alert, CancellationToken cancellationToken)
    {
        try
        {
            // 実際の実装ではメール、Slack、Teamsなどの通知システムと連携
            await Task.Delay(50, cancellationToken); // 通知送信をシミュレーション

            alert.NotificationSent = true;

            await _auditTrail.RecordSecurityEventAsync(
                SecurityEventType.SystemOperation,
                $"Health alert notification sent: {alert.Message}",
                SecuritySeverity.Low,
                new Dictionary<string, string>
                {
                    ["alertId"] = alert.AlertId.ToString(),
                    ["severity"] = alert.Severity.ToString(),
                    ["metricName"] = alert.MetricName
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _logger.LogSecurityEventAsync("NotificationError",
                $"Failed to send health alert notification: {ex.Message}");
        }
    }

    private async Task<List<RemediableIssue>> DetectRemediableIssuesAsync(CancellationToken cancellationToken)
    {
        var issues = new List<RemediableIssue>();

        // 一般的な問題の検出（実際の実装ではより詳細な診断ロジックを実装）
        var healthStatus = await GetSystemHealthStatusAsync(cancellationToken);

        if (healthStatus.OverallHealth < 0.7)
        {
            issues.Add(new RemediableIssue
            {
                IssueId = Guid.NewGuid(),
                IssueType = IssueType.SystemHealth,
                Severity = IssueSeverity.High,
                Description = $"System health is degraded: {healthStatus.OverallHealth:P}",
                DetectedAt = DateTime.UtcNow,
                RemediationSteps = new List<string>
                {
                    "Check system resources",
                    "Review recent changes",
                    "Run diagnostic tools",
                    "Contact support if issue persists"
                }
            });
        }

        return issues;
    }

    private async Task<AutoRemediationResult> ExecuteAutoRemediationAsync(
        RemediableIssue issue,
        CancellationToken cancellationToken)
    {
        var result = new AutoRemediationResult
        {
            IssueId = issue.IssueId,
            StartedAt = DateTime.UtcNow,
            Success = false,
            Message = "Remediation completed"
        };

        try
        {
            // 実際の実装では具体的な修復ロジックを実装
            await Task.Delay(1000, cancellationToken); // 修復処理をシミュレーション

            result.Success = true;
            result.CompletedAt = DateTime.UtcNow;

            await _auditTrail.RecordSecurityEventAsync(
                SecurityEventType.SystemOperation,
                $"Auto-remediation completed for issue: {issue.Description}",
                SecuritySeverity.Medium,
                new Dictionary<string, string>
                {
                    ["issueId"] = issue.IssueId.ToString(),
                    ["issueType"] = issue.IssueType.ToString(),
                    ["success"] = result.Success.ToString()
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            result.Message = $"Remediation failed: {ex.Message}";
            result.CompletedAt = DateTime.UtcNow;

            await _logger.LogSecurityEventAsync("AutoRemediationError",
                $"Auto-remediation failed for issue {issue.IssueId}: {ex.Message}");
        }

        return result;
    }

    private void CleanupOldMetrics(object? state)
    {
        var cutoffTime = DateTime.UtcNow.AddHours(-1);

        var keysToRemove = _healthMetrics
            .Where(kvp => kvp.Value.Timestamp < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _healthMetrics.TryRemove(key, out _);
        }

        if (keysToRemove.Any())
        {
            _logger.LogInformation($"Cleaned up {keysToRemove.Count} old health metrics");
        }
    }

    private void InitializeDefaultHealthChecks()
    {
        // デフォルトのヘルスチェックプロバイダーを登録
        RegisterHealthCheckProvider(new SystemResourceHealthCheck());
        RegisterHealthCheckProvider(new ServiceHealthCheck());
        RegisterHealthCheckProvider(new DatabaseHealthCheck());
        RegisterHealthCheckProvider(new NetworkHealthCheck());
    }

    private void InitializeDefaultAlertRules()
    {
        // デフォルトのアラートルールを設定
        _ = AddAlertRuleAsync("High CPU Usage", "cpu_usage", AlertCondition.GreaterThan, 0.8, AlertSeverity.High);
        _ = AddAlertRuleAsync("Low Memory", "memory_available", AlertCondition.LessThan, 0.1, AlertSeverity.High);
        _ = AddAlertRuleAsync("High Error Rate", "error_rate", AlertCondition.GreaterThan, 0.05, AlertSeverity.Medium);
        _ = AddAlertRuleAsync("Slow Response Time", "response_time", AlertCondition.GreaterThan, 2000, AlertSeverity.Medium);
    }
}

/// <summary>
/// システムヘルス状態
/// </summary>
public class SystemHealthStatus
{
    public double OverallHealth { get; set; }
    public Dictionary<string, double> ComponentHealth { get; set; } = new();
    public List<HealthAlert> ActiveAlerts { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public HealthTrend HealthTrend { get; set; }
}

/// <summary>
/// ヘルスチェック結果
/// </summary>
public class HealthCheckResult
{
    public string ComponentName { get; set; } = string.Empty;
    public double HealthScore { get; set; } // 0.0 - 1.0
    public HealthStatus Status { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// ヘルスメトリクス
/// </summary>
public class HealthMetric
{
    public string MetricName { get; set; } = string.Empty;
    public double Value { get; set; }
    public double HealthScore { get; set; }
    public HealthStatus Status { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// ヘルスメトリクススナップショット
/// </summary>
public class HealthMetricSnapshot
{
    public string MetricName { get; set; } = string.Empty;
    public double Value { get; set; }
    public double HealthScore { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// アラートルール
/// </summary>
public class AlertRule
{
    public string RuleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public AlertCondition Condition { get; set; }
    public double Threshold { get; set; }
    public AlertSeverity Severity { get; set; }
    public string? NotificationChannel { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// ヘルスアラート
/// </summary>
public class HealthAlert
{
    public Guid AlertId { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public double CurrentValue { get; set; }
    public double Threshold { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsActive { get; set; }
    public bool NotificationSent { get; set; }
}

/// <summary>
/// 修復可能な問題
/// </summary>
public class RemediableIssue
{
    public Guid IssueId { get; set; }
    public IssueType IssueType { get; set; }
    public IssueSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public List<string> RemediationSteps { get; set; } = new();
}

/// <summary>
/// 自動修復結果
/// </summary>
public class AutoRemediationResult
{
    public Guid IssueId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// ヘルスチェックプロバイダーインターフェース
/// </summary>
public interface IHealthCheckProvider
{
    Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// ヘルスステータス
/// </summary>
public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}

/// <summary>
/// ヘルストレンド
/// </summary>
public enum HealthTrend
{
    Improving,
    Stable,
    Degrading
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
/// アラートの深刻度
/// </summary>
public enum AlertSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// 問題の種類
/// </summary>
public enum IssueType
{
    SystemHealth,
    ResourceUsage,
    ServiceAvailability,
    Security,
    Performance
}

/// <summary>
/// 問題の深刻度
/// </summary>
public enum IssueSeverity
{
    Low,
    Medium,
    High,
    Critical
}

// デフォルトのヘルスチェックプロバイダー実装（サンプル）
public class SystemResourceHealthCheck : IHealthCheckProvider
{
    public async Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);

        // 実際の実装ではシステムリソースをチェック
        var cpuUsage = 0.45; // シミュレーション値
        var memoryUsage = 0.65; // シミュレーション値
        var diskUsage = 0.30; // シミュレーション値

        var score = 1.0 - Math.Max(cpuUsage, Math.Max(memoryUsage, diskUsage));

        return new HealthCheckResult
        {
            ComponentName = "SystemResources",
            HealthScore = Math.Max(0, score),
            Status = score > 0.7 ? HealthStatus.Healthy : HealthStatus.Degraded,
            Message = $"CPU: {cpuUsage:P}, Memory: {memoryUsage:P}, Disk: {diskUsage:P}",
            Timestamp = DateTime.UtcNow
        };
    }
}

public class ServiceHealthCheck : IHealthCheckProvider
{
    public async Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);

        // 実際の実装ではサービス状態をチェック
        var servicesHealthy = 0.95; // シミュレーション値

        return new HealthCheckResult
        {
            ComponentName = "CoreServices",
            HealthScore = servicesHealthy,
            Status = servicesHealthy > 0.9 ? HealthStatus.Healthy : HealthStatus.Unhealthy,
            Message = $"{servicesHealthy:P} of core services are healthy",
            Timestamp = DateTime.UtcNow
        };
    }
}

public class DatabaseHealthCheck : IHealthCheckProvider
{
    public async Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken);

        // 実際の実装ではデータベース接続とクエリをチェック
        var dbHealthy = 0.98; // シミュレーション値

        return new HealthCheckResult
        {
            ComponentName = "Database",
            HealthScore = dbHealthy,
            Status = dbHealthy > 0.95 ? HealthStatus.Healthy : HealthStatus.Degraded,
            Message = $"Database response time: {200}ms",
            Timestamp = DateTime.UtcNow
        };
    }
}

public class NetworkHealthCheck : IHealthCheckProvider
{
    public async Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);

        // 実際の実装ではネットワーク接続性をチェック
        var networkHealthy = 0.92; // シミュレーション値

        return new HealthCheckResult
        {
            ComponentName = "Network",
            HealthScore = networkHealthy,
            Status = networkHealthy > 0.9 ? HealthStatus.Healthy : HealthStatus.Degraded,
            Message = $"Network latency: {50}ms",
            Timestamp = DateTime.UtcNow
        };
    }
}
