using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Validation;

namespace AeroDriver.Core.Services;

/// <summary>
/// Provides security-oriented analytics and monitoring helpers built on top of the driver repository.
/// </summary>
public sealed class SecurityService : ISecurityService
{
    private static readonly TimeSpan StaleDriverThreshold = TimeSpan.FromDays(180);

    private readonly ISimpleLogger _logger;
    private readonly IDriverRepository _driverRepository;
    private readonly List<RealTimeMonitoringInfo> _activeMonitors = new();
    private readonly List<VulnerabilityInfo> _vulnerabilityDatabase;

    private SecurityMonitoringConfig _monitoringConfig = new();

    public SecurityService(ISimpleLogger logger, IDriverRepository driverRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driverRepository = driverRepository ?? throw new ArgumentNullException(nameof(driverRepository));
        _vulnerabilityDatabase = BuildBaselineVulnerabilityDatabase();
    }

    public async Task<SecurityReport> PerformSecurityAuditAsync(CancellationToken cancellationToken = default)
    {
        var report = new SecurityReport
        {
            AuditTime = DateTime.UtcNow,
            Metrics = new List<SecurityMetrics>(),
            CriticalIssues = new List<SecurityIssue>(),
            WarningIssues = new List<SecurityIssue>(),
            InfoIssues = new List<SecurityIssue>(),
            IssuesByCategory = new Dictionary<string, int>()
        };

        try
        {
            var drivers = (await _driverRepository.GetAllDriversAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (drivers.Count == 0)
            {
                report.SecurityScore = 100;
                report.ComplianceStatus = SecurityComplianceStatus.Compliant;
                return report;
            }

            var unsignedDrivers = drivers.Where(d => !d.IsSigned).ToList();
            var staleDrivers = drivers.Where(d => d.LastUpdated.HasValue && DateTime.UtcNow - d.LastUpdated.Value > StaleDriverThreshold).ToList();
            var problemDrivers = drivers.Where(d => !string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase)).ToList();

            var totalIssues = unsignedDrivers.Count + staleDrivers.Count + problemDrivers.Count;
            report.TotalIssues = totalIssues;
            report.SecurityScore = CalculateSecurityScore(drivers.Count, unsignedDrivers.Count, staleDrivers.Count, problemDrivers.Count);
            report.ComplianceStatus = DetermineComplianceStatus(report.SecurityScore, totalIssues);

            if (unsignedDrivers.Count > 0)
            {
                report.CriticalIssues.AddRange(unsignedDrivers.Select(driver => new SecurityIssue
                {
                    DriverId = driver.Id,
                    DriverName = driver.Name,
                    Description = "Driver is not signed",
                    Severity = SecuritySeverity.High,
                    Recommendation = "Acquire and install a signed version of this driver.",
                    Type = "UnsignedDriver",
                    DiscoveredAt = DateTime.UtcNow
                }));
                report.IssuesByCategory["Unsigned"] = unsignedDrivers.Count;
            }

            if (staleDrivers.Count > 0)
            {
                report.WarningIssues.AddRange(staleDrivers.Select(driver => new SecurityIssue
                {
                    DriverId = driver.Id,
                    DriverName = driver.Name,
                    Description = "Driver has not been updated within the required timeframe",
                    Severity = SecuritySeverity.Medium,
                    Recommendation = "Review availability of updates and refresh the driver if appropriate.",
                    Type = "StaleDriver",
                    DiscoveredAt = DateTime.UtcNow
                }));
                report.IssuesByCategory["Stale"] = staleDrivers.Count;
            }

            if (problemDrivers.Count > 0)
            {
                report.WarningIssues.AddRange(problemDrivers.Select(driver => new SecurityIssue
                {
                    DriverId = driver.Id,
                    DriverName = driver.Name,
                    Description = $"Driver reported status '{driver.Status}'",
                    Severity = SecuritySeverity.Medium,
                    Recommendation = "Investigate the reported driver status and remediate.",
                    Type = "ProblemDriver",
                    DiscoveredAt = DateTime.UtcNow
                }));
                report.IssuesByCategory["Problem"] = problemDrivers.Count;
            }

            report.Metrics.Add(new SecurityMetrics
            {
                MetricName = "unsigned_driver_ratio",
                Value = drivers.Count == 0 ? 0 : unsignedDrivers.Count / (double)drivers.Count,
                Unit = "ratio",
                Timestamp = DateTime.UtcNow
            });

            report.Metrics.Add(new SecurityMetrics
            {
                MetricName = "stale_driver_ratio",
                Value = drivers.Count == 0 ? 0 : staleDrivers.Count / (double)drivers.Count,
                Unit = "ratio",
                Timestamp = DateTime.UtcNow
            });

            if (totalIssues == 0)
            {
                report.InfoIssues.Add(new SecurityIssue
                {
                    Type = "Summary",
                    Description = "No security-related driver issues detected.",
                    Severity = SecuritySeverity.Info,
                    Recommendation = "Continue routine monitoring.",
                    DiscoveredAt = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("Security audit failed", exception: ex).ConfigureAwait(false);
            report.Error = ex.Message;
            report.SecurityScore = 0;
            report.ComplianceStatus = SecurityComplianceStatus.Unknown;
        }

        return report;
    }

    public void ConfigureMonitoring(SecurityMonitoringConfig config)
    {
        _monitoringConfig = config ?? throw new ArgumentNullException(nameof(config));
        RefreshActiveMonitors();
    }

    public IReadOnlyList<RealTimeMonitoringInfo> GetActiveMonitors()
        => _activeMonitors.Select(CloneMonitor).ToList();

    public IReadOnlyList<VulnerabilityInfo> GetVulnerabilityDatabase()
        => _vulnerabilityDatabase.Select(CloneVulnerability).ToList();

    private void RefreshActiveMonitors()
    {
        _activeMonitors.Clear();

        if (_monitoringConfig.EnableVulnerabilityMonitoring)
        {
            _activeMonitors.Add(CreateMonitor(MonitoringType.Security, "VulnerabilityMonitoring", _monitoringConfig.VulnerabilityCheckIntervalMinutes * 60));
        }

        if (_monitoringConfig.EnableSignatureValidation)
        {
            _activeMonitors.Add(CreateMonitor(MonitoringType.Security, "SignatureValidation", _monitoringConfig.SignatureValidationIntervalMinutes * 60));
        }

        if (_monitoringConfig.EnableBehaviorAnalysis)
        {
            _activeMonitors.Add(CreateMonitor(MonitoringType.Security, "BehaviorAnalysis", 300));
        }
    }

    private RealTimeMonitoringInfo CreateMonitor(MonitoringType type, string name, int intervalSeconds)
    {
        return new RealTimeMonitoringInfo
        {
            MonitorId = Guid.NewGuid().ToString("n"),
            Type = type,
            StartTime = DateTime.UtcNow,
            Status = MonitoringStatus.Normal,
            Alerts = new List<MonitoringAlert>(),
            Metrics = new Dictionary<string, object>
            {
                ["monitorName"] = name
            },
            CheckIntervalSeconds = Math.Max(intervalSeconds, 30)
        };
    }

    private static RealTimeMonitoringInfo CloneMonitor(RealTimeMonitoringInfo source)
    {
        return new RealTimeMonitoringInfo
        {
            MonitorId = source.MonitorId,
            Type = source.Type,
            StartTime = source.StartTime,
            Status = source.Status,
            Alerts = source.Alerts.Select(alert => new MonitoringAlert
            {
                AlertId = alert.AlertId,
                Severity = alert.Severity,
                Message = alert.Message,
                Timestamp = alert.Timestamp,
                AcknowledgedBy = alert.AcknowledgedBy,
                IsAcknowledged = alert.IsAcknowledged,
                MonitorName = alert.MonitorName,
                MetricValue = alert.MetricValue,
                Threshold = alert.Threshold
            }).ToList(),
            Metrics = new Dictionary<string, object>(source.Metrics),
            CheckIntervalSeconds = source.CheckIntervalSeconds
        };
    }

    private static VulnerabilityInfo CloneVulnerability(VulnerabilityInfo source)
    {
        return new VulnerabilityInfo
        {
            CveId = source.CveId,
            Description = source.Description,
            Severity = source.Severity,
            CvssScore = source.CvssScore,
            AffectedProducts = new List<string>(source.AffectedProducts),
            AffectedVersions = new List<string>(source.AffectedVersions),
            Recommendation = source.Recommendation,
            PublishedDate = source.PublishedDate,
            LastModifiedDate = source.LastModifiedDate,
            Status = source.Status,
            References = new Dictionary<string, string>(source.References)
        };
    }

    private static int CalculateSecurityScore(int totalDrivers, int unsignedDrivers, int staleDrivers, int problemDrivers)
    {
        if (totalDrivers <= 0)
        {
            return 100;
        }

        var score = 100;
        score -= Math.Min(unsignedDrivers * 20, 60);
        score -= Math.Min(staleDrivers * 10, 20);
        score -= Math.Min(problemDrivers * 5, 20);
        return Math.Clamp(score, 0, 100);
    }

    private static SecurityComplianceStatus DetermineComplianceStatus(int securityScore, int totalIssues)
    {
        if (totalIssues == 0)
        {
            return SecurityComplianceStatus.Compliant;
        }

        if (securityScore >= 80)
        {
            return SecurityComplianceStatus.PartiallyCompliant;
        }

        return SecurityComplianceStatus.NonCompliant;
    }

    private static List<VulnerabilityInfo> BuildBaselineVulnerabilityDatabase()
    {
        return new List<VulnerabilityInfo>
        {
            new VulnerabilityInfo
            {
                CveId = "CVE-2024-0001",
                Description = "Unsigned driver allows privilege escalation if loaded without signature enforcement.",
                Severity = "High",
                CvssScore = 7.5,
                AffectedProducts = new List<string> { "Generic USB Controller" },
                AffectedVersions = new List<string> { "1.x", "2.x" },
                Recommendation = "Enable signature enforcement and deploy signed driver updates.",
                PublishedDate = DateTime.UtcNow.AddMonths(-6),
                LastModifiedDate = DateTime.UtcNow.AddMonths(-1),
                Status = VulnerabilityStatus.Active,
                References = new Dictionary<string, string>
                {
                    ["info"] = "Vulnerability information available in local security database"
                }
            }
        };
    }
}
