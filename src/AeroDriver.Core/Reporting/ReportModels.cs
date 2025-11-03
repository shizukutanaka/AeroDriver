using System;
using System.Collections.Generic;

namespace AeroDriver.Core.Reporting;

/// <summary>
/// Comprehensive compliance report for audit trail analysis
/// </summary>
public class ComplianceReport
{
    public Guid ReportId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateRange ReportPeriod { get; set; } = new();
    public int TotalEvents { get; set; }
    public ComplianceReportSummary Summary { get; set; } = new();
    public List<SecurityIncident> SecurityIncidents { get; set; } = new();
    public UserActivityReport UserActivity { get; set; } = new();
    public SystemOperationsReport SystemOperations { get; set; } = new();
}

/// <summary>
/// Summary statistics for compliance reporting
/// </summary>
public class ComplianceReportSummary
{
    public int TotalEvents { get; set; }
    public List<ActionSummary> EventsByAction { get; set; } = new();
    public List<ResultSummary> EventsByResult { get; set; } = new();
    public List<UserSummary> EventsByUser { get; set; } = new();
    public List<MachineSummary> EventsByMachine { get; set; } = new();
}

/// <summary>
/// Security incident details
/// </summary>
public class SecurityIncident
{
    public Guid EventId { get; set; }
    public DateTime Timestamp { get; set; }
    public SecuritySeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
}

/// <summary>
/// User activity analysis report
/// </summary>
public class UserActivityReport
{
    public int ActiveUsers { get; set; }
    public List<UserActivity> UserActivities { get; set; } = new();
}

/// <summary>
/// Individual user activity details
/// </summary>
public class UserActivity
{
    public string User { get; set; } = string.Empty;
    public int TotalActions { get; set; }
    public List<ActionCount> ActionsByType { get; set; } = new();
    public DateTime LastActivity { get; set; }
    public int FailedActions { get; set; }
}

/// <summary>
/// System operations performance report
/// </summary>
public class SystemOperationsReport
{
    public int TotalSystemOperations { get; set; }
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public List<HourlyOperation> OperationsByHour { get; set; } = new();
}

/// <summary>
/// Security dashboard for real-time monitoring
/// </summary>
public class SecurityDashboardReport
{
    public Guid ReportId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateRange TimeRange { get; set; } = new();
    public int CriticalAlerts { get; set; }
    public int FailedAuthentications { get; set; }
    public int UnauthorizedAccessAttempts { get; set; }
    public List<SecurityEventSummary> TopSecurityEvents { get; set; } = new();
}

/// <summary>
/// Security event summary
/// </summary>
public class SecurityEventSummary
{
    public string EventType { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime LastOccurrence { get; set; }
}

/// <summary>
/// Performance metrics report
/// </summary>
public class PerformanceReport
{
    public Guid ReportId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateRange ReportPeriod { get; set; } = new();
    public int TotalDriverOperations { get; set; }
    public double OperationSuccessRate { get; set; }
    public double AverageOperationDuration { get; set; }
    public List<OperationSummary> OperationsByType { get; set; } = new();
    public List<int> PeakUsageHours { get; set; } = new();
}

/// <summary>
/// Operation performance summary
/// </summary>
public class OperationSummary
{
    public string OperationType { get; set; } = string.Empty;
    public int Count { get; set; }
    public int SuccessCount { get; set; }
    public double AverageDuration { get; set; }
}

/// <summary>
/// Date range for reports
/// </summary>
public class DateRange
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

/// <summary>
/// Action summary statistics
/// </summary>
public class ActionSummary
{
    public AuditAction Action { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Result summary statistics
/// </summary>
public class ResultSummary
{
    public AuditResult Result { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// User summary statistics
/// </summary>
public class UserSummary
{
    public string User { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// Machine summary statistics
/// </summary>
public class MachineSummary
{
    public string Machine { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// Action count details
/// </summary>
public class ActionCount
{
    public AuditAction Action { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Hourly operation statistics
/// </summary>
public class HourlyOperation
{
    public int Hour { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Report export format options
/// </summary>
public enum ReportFormat
{
    Json,
    Csv,
    Html
}
