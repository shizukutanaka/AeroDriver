using Microsoft.AspNetCore.Mvc;
using AeroDriver.Core.Reporting;
using AeroDriver.Core.Security;
using System.ComponentModel.DataAnnotations;

namespace AeroDriver.API.Controllers;

/// <summary>
/// Enterprise reporting API for compliance and audit analysis
/// Provides comprehensive reporting capabilities for offline analysis and compliance monitoring
/// </summary>
[ApiController]
[Route("api/reports")]
[Produces("application/json")]
public class ReportsController : ControllerBase
{
    private readonly AuditReportGenerator _reportGenerator;
    private readonly AuditTrail _auditTrail;
    private readonly ISimpleLogger _logger;

    public ReportsController(
        AuditReportGenerator reportGenerator,
        AuditTrail auditTrail,
        ISimpleLogger logger)
    {
        _reportGenerator = reportGenerator ?? throw new ArgumentNullException(nameof(reportGenerator));
        _auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a comprehensive compliance report for the specified time period
    /// </summary>
    /// <param name="startTime">Start time for the report period (UTC)</param>
    /// <param name="endTime">End time for the report period (UTC)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comprehensive compliance report</returns>
    /// <response code="200">Compliance report generated successfully</response>
    /// <response code="400">Invalid date range</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("compliance")]
    [ProducesResponseType(typeof(ComplianceReport), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<ComplianceReport>> GetComplianceReport(
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        if (startTime >= endTime)
        {
            return BadRequest("Start time must be before end time");
        }

        if ((endTime - startTime).TotalDays > 365)
        {
            return BadRequest("Report period cannot exceed 365 days");
        }

        try
        {
            var report = await _reportGenerator.GenerateComplianceReportAsync(
                startTime, endTime, cancellationToken);

            await _logger.LogInformationAsync(
                $"Generated compliance report: {report.ReportId} with {report.TotalEvents} events");

            return Ok(report);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to generate compliance report");
            return StatusCode(500, "Failed to generate compliance report");
        }
    }

    /// <summary>
    /// Generates a security dashboard report for real-time monitoring
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Security dashboard report for the last 24 hours</returns>
    /// <response code="200">Security dashboard generated successfully</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("security-dashboard")]
    [ProducesResponseType(typeof(SecurityDashboardReport), 200)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<SecurityDashboardReport>> GetSecurityDashboard(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _reportGenerator.GenerateSecurityDashboardAsync(cancellationToken);

            await _logger.LogInformationAsync(
                $"Generated security dashboard: {report.ReportId} with {report.CriticalAlerts} critical alerts");

            return Ok(report);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to generate security dashboard");
            return StatusCode(500, "Failed to generate security dashboard");
        }
    }

    /// <summary>
    /// Generates a performance metrics report for system optimization
    /// </summary>
    /// <param name="startTime">Start time for the report period (UTC)</param>
    /// <param name="endTime">End time for the report period (UTC)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Performance metrics report</returns>
    /// <response code="200">Performance report generated successfully</response>
    /// <response code="400">Invalid date range</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("performance")]
    [ProducesResponseType(typeof(PerformanceReport), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<PerformanceReport>> GetPerformanceReport(
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        if (startTime >= endTime)
        {
            return BadRequest("Start time must be before end time");
        }

        if ((endTime - startTime).TotalDays > 90)
        {
            return BadRequest("Performance report period cannot exceed 90 days");
        }

        try
        {
            var report = await _reportGenerator.GeneratePerformanceReportAsync(
                startTime, endTime, cancellationToken);

            await _logger.LogInformationAsync(
                $"Generated performance report: {report.ReportId} with {report.TotalDriverOperations} operations");

            return Ok(report);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to generate performance report");
            return StatusCode(500, "Failed to generate performance report");
        }
    }

    /// <summary>
    /// Exports a compliance report in multiple formats
    /// </summary>
    /// <param name="startTime">Start time for the report period (UTC)</param>
    /// <param name="endTime">End time for the report period (UTC)</param>
    /// <param name="formats">Comma-separated list of export formats (json,csv,html)</param>
    /// <param name="baseFileName">Base filename for exported files (without extension)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Export result with file paths</returns>
    /// <response code="200">Report exported successfully</response>
    /// <response code="400">Invalid parameters</response>
    /// <response code="500">Export failed</response>
    [HttpPost("compliance/export")]
    [ProducesResponseType(typeof(ReportExportResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<ReportExportResult>> ExportComplianceReport(
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime,
        [FromQuery] string formats = "json",
        [FromQuery] string? baseFileName = null,
        CancellationToken cancellationToken = default)
    {
        if (startTime >= endTime)
        {
            return BadRequest("Start time must be before end time");
        }

        if ((endTime - startTime).TotalDays > 365)
        {
            return BadRequest("Report period cannot exceed 365 days");
        }

        var formatList = ParseFormats(formats);
        if (formatList.Count == 0)
        {
            return BadRequest("At least one valid format must be specified");
        }

        baseFileName ??= $"compliance_report_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

        try
        {
            var report = await _reportGenerator.GenerateComplianceReportAsync(
                startTime, endTime, cancellationToken);

            await _reportGenerator.ExportComplianceReportAsync(
                report, baseFileName, formatList.ToArray(), cancellationToken);

            var result = new ReportExportResult
            {
                ReportId = report.ReportId,
                ExportedFormats = formatList,
                BaseFileName = baseFileName,
                ExportDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AeroDriver", "Reports"),
                ExportedAt = DateTime.UtcNow
            };

            await _logger.LogInformationAsync(
                $"Exported compliance report {report.ReportId} in formats: {string.Join(", ", formatList)}");

            return Ok(result);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to export compliance report");
            return StatusCode(500, "Failed to export compliance report");
        }
    }

    /// <summary>
    /// Queries audit events with filtering options
    /// </summary>
    /// <param name="startTime">Optional start time filter (UTC)</param>
    /// <param name="endTime">Optional end time filter (UTC)</param>
    /// <param name="action">Optional action filter</param>
    /// <param name="userIdentity">Optional user identity filter</param>
    /// <param name="maxResults">Maximum number of results to return (1-1000)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of audit events matching the filters</returns>
    /// <response code="200">Audit events retrieved successfully</response>
    /// <response code="400">Invalid parameters</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("audit-events")]
    [ProducesResponseType(typeof(List<AuditEvent>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<List<AuditEvent>>> GetAuditEvents(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] AuditAction? action = null,
        [FromQuery] string? userIdentity = null,
        [FromQuery, Range(1, 1000)] int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var events = await _auditTrail.QueryEventsAsync(
                startTime, endTime, action, userIdentity, maxResults, cancellationToken);

            await _logger.LogInformationAsync(
                $"Retrieved {events.Count} audit events with filters");

            return Ok(events);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to retrieve audit events");
            return StatusCode(500, "Failed to retrieve audit events");
        }
    }

    /// <summary>
    /// Gets available report formats
    /// </summary>
    /// <returns>List of supported report formats</returns>
    /// <response code="200">Report formats retrieved successfully</response>
    [HttpGet("formats")]
    [ProducesResponseType(typeof(Dictionary<string, string>), 200)]
    public ActionResult<Dictionary<string, string>> GetReportFormats()
    {
        var formats = new Dictionary<string, string>
        {
            ["json"] = "Structured JSON format for programmatic processing",
            ["csv"] = "Comma-separated values for spreadsheet analysis",
            ["html"] = "HTML report with styling and charts"
        };

        return Ok(formats);
    }

    private static List<ReportFormat> ParseFormats(string formats)
    {
        var result = new List<ReportFormat>();
        var formatStrings = formats.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var format in formatStrings)
        {
            if (Enum.TryParse<ReportFormat>(format.Trim(), true, out var reportFormat))
            {
                result.Add(reportFormat);
            }
        }

        return result;
    }
}

/// <summary>
/// Result of a report export operation
/// </summary>
public class ReportExportResult
{
    /// <summary>
    /// Unique identifier of the exported report
    /// </summary>
    public Guid ReportId { get; set; }

    /// <summary>
    /// List of formats in which the report was exported
    /// </summary>
    public List<ReportFormat> ExportedFormats { get; set; } = new();

    /// <summary>
    /// Base filename used for the exported files
    /// </summary>
    public string BaseFileName { get; set; } = string.Empty;

    /// <summary>
    /// Directory where the files were exported
    /// </summary>
    public string ExportDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the export was completed
    /// </summary>
    public DateTime ExportedAt { get; set; }
}
