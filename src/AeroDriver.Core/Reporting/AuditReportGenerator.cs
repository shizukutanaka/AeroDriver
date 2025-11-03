using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Reporting;

/// <summary>
/// Enterprise reporting system for audit log analysis and compliance reporting
/// Generates comprehensive reports from audit trail data for offline analysis
/// </summary>
public class AuditReportGenerator : IDisposable
{
    private readonly AuditTrail _auditTrail;
    private readonly ISimpleLogger _logger;
    private readonly string _reportsDirectory;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuditReportGenerator(AuditTrail auditTrail, ISimpleLogger? logger = null)
    {
        _auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
        _logger = logger ?? new SimpleLogger();

        _reportsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AeroDriver",
            "Reports");

        Directory.CreateDirectory(_reportsDirectory);
    }

    /// <summary>
    /// Generates a comprehensive compliance report covering the specified time period
    /// </summary>
    public async Task<ComplianceReport> GenerateComplianceReportAsync(
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var events = await _auditTrail.QueryEventsAsync(
            startTime: startTime,
            endTime: endTime,
            maxResults: 10000,
            cancellationToken: cancellationToken);

        var report = new ComplianceReport
        {
            ReportId = Guid.NewGuid(),
            GeneratedAt = DateTime.UtcNow,
            ReportPeriod = new DateRange { Start = startTime, End = endTime },
            TotalEvents = events.Count,
            Summary = GenerateSummaryStatistics(events),
            SecurityIncidents = ExtractSecurityIncidents(events),
            UserActivity = GenerateUserActivityReport(events),
            SystemOperations = GenerateSystemOperationsReport(events)
        };

        await _logger.LogInformationAsync($"Generated compliance report with {events.Count} events");
        return report;
    }

    /// <summary>
    /// Exports the compliance report to multiple formats (JSON, CSV, HTML)
    /// </summary>
    public async Task ExportComplianceReportAsync(
        ComplianceReport report,
        string baseFileName,
        ReportFormat[] formats,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var format in formats)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = $"{baseFileName}_{report.ReportId:N}.{GetFileExtension(format)}";
            var filePath = Path.Combine(_reportsDirectory, fileName);

            switch (format)
            {
                case ReportFormat.Json:
                    await ExportAsJsonAsync(report, filePath, cancellationToken);
                    break;
                case ReportFormat.Csv:
                    await ExportAsCsvAsync(report, filePath, cancellationToken);
                    break;
                case ReportFormat.Html:
                    await ExportAsHtmlAsync(report, filePath, cancellationToken);
                    break;
            }

            await _logger.LogInformationAsync($"Exported {format} report to {filePath}");
        }
    }

    /// <summary>
    /// Generates a security dashboard report for real-time monitoring
    /// </summary>
    public async Task<SecurityDashboardReport> GenerateSecurityDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // Get last 24 hours of security events
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-24);

        var events = await _auditTrail.QueryEventsAsync(
            startTime: startTime,
            endTime: endTime,
            maxResults: 5000,
            cancellationToken: cancellationToken);

        var securityEvents = events.Where(e =>
            e.Action == AuditAction.SecurityEvent ||
            e.Action == AuditAction.Authentication ||
            e.Action == AuditAction.Authorization).ToList();

        var dashboard = new SecurityDashboardReport
        {
            ReportId = Guid.NewGuid(),
            GeneratedAt = DateTime.UtcNow,
            TimeRange = new DateRange { Start = startTime, End = endTime },
            CriticalAlerts = securityEvents.Count(e =>
            {
                if (e.Metadata.TryGetValue("severity", out var severityStr) &&
                    Enum.TryParse<SecuritySeverity>(severityStr, out var severity))
                {
                    return severity == SecuritySeverity.Critical;
                }
                return false;
            }),
            FailedAuthentications = securityEvents.Count(e =>
                e.Action == AuditAction.Authentication && e.Result == AuditResult.Failure),
            UnauthorizedAccessAttempts = securityEvents.Count(e =>
                e.Action == AuditAction.Authorization && e.Result == AuditResult.Failure),
            TopSecurityEvents = securityEvents
                .GroupBy(e => e.Resource)
                .Select(g => new SecurityEventSummary
                {
                    EventType = g.Key,
                    Count = g.Count(),
                    LastOccurrence = g.Max(e => e.Timestamp)
                })
                .OrderByDescending(s => s.Count)
                .Take(10)
                .ToList()
        };

        return dashboard;
    }

    /// <summary>
    /// Generates a performance metrics report for system optimization
    /// </summary>
    public async Task<PerformanceReport> GeneratePerformanceReportAsync(
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var events = await _auditTrail.QueryEventsAsync(
            startTime: startTime,
            endTime: endTime,
            maxResults: 5000,
            cancellationToken: cancellationToken);

        var driverOperations = events.Where(e =>
            e.Action.ToString().StartsWith("Driver")).ToList();

        var report = new PerformanceReport
        {
            ReportId = Guid.NewGuid(),
            GeneratedAt = DateTime.UtcNow,
            ReportPeriod = new DateRange { Start = startTime, End = endTime },
            TotalDriverOperations = driverOperations.Count,
            OperationSuccessRate = driverOperations.Count > 0 ?
                (double)driverOperations.Count(e => e.Result == AuditResult.Success) / driverOperations.Count : 0,
            AverageOperationDuration = CalculateAverageDuration(driverOperations),
            OperationsByType = driverOperations
                .GroupBy(e => e.Action)
                .Select(g => new OperationSummary
                {
                    OperationType = g.Key.ToString(),
                    Count = g.Count(),
                    SuccessCount = g.Count(e => e.Result == AuditResult.Success),
                    AverageDuration = CalculateAverageDuration(g.ToList())
                })
                .ToList(),
            PeakUsageHours = IdentifyPeakHours(driverOperations)
        };

        return report;
    }

    private static ComplianceReportSummary GenerateSummaryStatistics(List<AuditEvent> events)
    {
        return new ComplianceReportSummary
        {
            TotalEvents = events.Count,
            EventsByAction = events.GroupBy(e => e.Action)
                .Select(g => new ActionSummary { Action = g.Key, Count = g.Count() })
                .ToList(),
            EventsByResult = events.GroupBy(e => e.Result)
                .Select(g => new ResultSummary { Result = g.Key, Count = g.Count() })
                .ToList(),
            EventsByUser = events.GroupBy(e => e.UserIdentity)
                .Select(g => new UserSummary { User = g.Key, Count = g.Count() })
                .ToList(),
            EventsByMachine = events.GroupBy(e => e.MachineName)
                .Select(g => new MachineSummary { Machine = g.Key, Count = g.Count() })
                .ToList()
        };
    }

    private static List<SecurityIncident> ExtractSecurityIncidents(List<AuditEvent> events)
    {
        return events.Where(e =>
            e.Action == AuditAction.SecurityEvent ||
            (e.Action == AuditAction.Authentication && e.Result == AuditResult.Failure) ||
            (e.Action == AuditAction.Authorization && e.Result == AuditResult.Failure))
            .Select(e => new SecurityIncident
            {
                EventId = e.EventId,
                Timestamp = e.Timestamp,
                Severity = ExtractSeverity(e),
                Description = e.Details ?? "Security incident",
                Source = e.UserIdentity,
                Resource = e.Resource
            })
            .ToList();
    }

    private static SecuritySeverity ExtractSeverity(AuditEvent auditEvent)
    {
        if (auditEvent.Metadata.TryGetValue("severity", out var severityStr) &&
            Enum.TryParse<SecuritySeverity>(severityStr, out var severity))
        {
            return severity;
        }

        // Default severity based on event type
        return auditEvent.Action switch
        {
            AuditAction.SecurityEvent => SecuritySeverity.Medium,
            AuditAction.Authentication when auditEvent.Result == AuditResult.Failure => SecuritySeverity.Low,
            AuditAction.Authorization when auditEvent.Result == AuditResult.Failure => SecuritySeverity.Medium,
            _ => SecuritySeverity.Low
        };
    }

    private static UserActivityReport GenerateUserActivityReport(List<AuditEvent> events)
    {
        return new UserActivityReport
        {
            ActiveUsers = events.Select(e => e.UserIdentity).Distinct().Count(),
            UserActivities = events.GroupBy(e => e.UserIdentity)
                .Select(g => new UserActivity
                {
                    User = g.Key,
                    TotalActions = g.Count(),
                    ActionsByType = g.GroupBy(e => e.Action)
                        .Select(ag => new ActionCount { Action = ag.Key, Count = ag.Count() })
                        .ToList(),
                    LastActivity = g.Max(e => e.Timestamp),
                    FailedActions = g.Count(e => e.Result == AuditResult.Failure)
                })
                .ToList()
        };
    }

    private static SystemOperationsReport GenerateSystemOperationsReport(List<AuditEvent> events)
    {
        var systemEvents = events.Where(e =>
            e.Action == AuditAction.SystemOperation ||
            e.Action.ToString().Contains("Driver")).ToList();

        return new SystemOperationsReport
        {
            TotalSystemOperations = systemEvents.Count,
            SuccessfulOperations = systemEvents.Count(e => e.Result == AuditResult.Success),
            FailedOperations = systemEvents.Count(e => e.Result == AuditResult.Failure),
            OperationsByHour = systemEvents
                .GroupBy(e => e.Timestamp.Hour)
                .Select(g => new HourlyOperation { Hour = g.Key, Count = g.Count() })
                .OrderBy(h => h.Hour)
                .ToList()
        };
    }

    private static double CalculateAverageDuration(List<AuditEvent> events)
    {
        // Note: Actual duration calculation would require start/end time tracking
        // This is a simplified implementation
        return events.Count > 0 ? events.Average(e => 1.0) : 0; // Placeholder
    }

    private static List<int> IdentifyPeakHours(List<AuditEvent> events)
    {
        return events.GroupBy(e => e.Timestamp.Hour)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => g.Key)
            .ToList();
    }

    private async Task ExportAsJsonAsync(ComplianceReport report, string filePath, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private async Task ExportAsCsvAsync(ComplianceReport report, string filePath, CancellationToken cancellationToken)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Report ID,Generated At,Period Start,Period End,Total Events");

        csv.AppendLine($"{report.ReportId},{report.GeneratedAt:O},{report.ReportPeriod.Start:O},{report.ReportPeriod.End:O},{report.TotalEvents}");

        csv.AppendLine();
        csv.AppendLine("Security Incidents:");
        csv.AppendLine("Event ID,Timestamp,Severity,Description,Source,Resource");

        foreach (var incident in report.SecurityIncidents)
        {
            csv.AppendLine($"{incident.EventId},{incident.Timestamp:O},{incident.Severity},{incident.Description},{incident.Source},{incident.Resource}");
        }

        await File.WriteAllTextAsync(filePath, csv.ToString(), cancellationToken);
    }

    private async Task ExportAsHtmlAsync(ComplianceReport report, string filePath, CancellationToken cancellationToken)
    {
        var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>AeroDriver Compliance Report</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; }}
        .header {{ background-color: #f8f9fa; padding: 20px; border-radius: 5px; }}
        .summary {{ margin: 20px 0; }}
        .section {{ margin: 30px 0; }}
        table {{ border-collapse: collapse; width: 100%; }}
        th, td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
        th {{ background-color: #f2f2f2; }}
        .critical {{ color: red; }}
        .warning {{ color: orange; }}
    </style>
</head>
<body>
    <div class='header'>
        <h1>AeroDriver Compliance Report</h1>
        <p>Report ID: {report.ReportId}</p>
        <p>Generated: {report.GeneratedAt:G}</p>
        <p>Period: {report.ReportPeriod.Start:G} - {report.ReportPeriod.End:G}</p>
    </div>

    <div class='summary'>
        <h2>Summary</h2>
        <p>Total Events: {report.TotalEvents}</p>
        <p>Security Incidents: {report.SecurityIncidents.Count}</p>
        <p>Active Users: {report.UserActivity.ActiveUsers}</p>
    </div>

    <div class='section'>
        <h2>Security Incidents</h2>
        <table>
            <tr><th>Timestamp</th><th>Severity</th><th>Description</th><th>Source</th></tr>
            {string.Join("", report.SecurityIncidents.Select(i => $"<tr><td>{i.Timestamp:G}</td><td class='{i.Severity.ToString().ToLower()}'>{i.Severity}</td><td>{i.Description}</td><td>{i.Source}</td></tr>"))}
        </table>
    </div>
</body>
</html>";

        await File.WriteAllTextAsync(filePath, html, cancellationToken);
    }

    private static string GetFileExtension(ReportFormat format)
    {
        return format switch
        {
            ReportFormat.Json => "json",
            ReportFormat.Csv => "csv",
            ReportFormat.Html => "html",
            _ => "txt"
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AuditReportGenerator));
        }
    }

    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
