using Microsoft.AspNetCore.Mvc;
using AeroDriver.Core.Metadata;
using System.ComponentModel.DataAnnotations;

namespace AeroDriver.API.Controllers;

/// <summary>
/// Enterprise driver catalog management API
/// Provides comprehensive driver catalog synchronization and metadata management
/// </summary>
[ApiController]
[Route("api/catalog")]
[Produces("application/json")]
public class MetadataController : ControllerBase
{
    private readonly DriverCatalogManager _catalogManager;
    private readonly ISimpleLogger _logger;

    public MetadataController(DriverCatalogManager catalogManager, ISimpleLogger logger)
    {
        _catalogManager = catalogManager ?? throw new ArgumentNullException(nameof(catalogManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Synchronizes driver catalogs from configured vendor sources
    /// </summary>
    /// <param name="sources">List of vendor catalog sources to sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Catalog synchronization result</returns>
    /// <response code="200">Catalog synchronization completed</response>
    /// <response code="400">Invalid request</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("sync")]
    [ProducesResponseType(typeof(CatalogSyncResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<CatalogSyncResult>> SynchronizeCatalogs(
        [FromBody] List<VendorCatalogSource> sources,
        CancellationToken cancellationToken = default)
    {
        if (sources == null || sources.Count == 0)
        {
            return BadRequest("At least one catalog source must be specified");
        }

        try
        {
            var result = await _catalogManager.SynchronizeCatalogsAsync(sources, cancellationToken);

            await _logger.LogInformationAsync(
                $"Catalog sync completed: {result.SuccessfulSyncs}/{result.SourcesProcessed} successful");

            return Ok(result);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to synchronize catalogs");
            return StatusCode(500, "Failed to synchronize catalogs");
        }
    }

    /// <summary>
    /// Queries the driver catalog for drivers matching specific hardware IDs
    /// </summary>
    /// <param name="hardwareIds">Hardware IDs to search for</param>
    /// <param name="vendor">Optional vendor filter</param>
    /// <param name="operatingSystem">Optional OS filter</param>
    /// <param name="maxResults">Maximum number of results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching driver catalog entries</returns>
    /// <response code="200">Query completed successfully</response>
    /// <response code="400">Invalid request</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("drivers")]
    [ProducesResponseType(typeof(List<DriverCatalogEntry>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<List<DriverCatalogEntry>>> QueryDrivers(
        [FromQuery] List<string> hardwareIds,
        [FromQuery] string? vendor = null,
        [FromQuery] string? operatingSystem = null,
        [FromQuery, Range(1, 1000)] int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        if (hardwareIds == null || hardwareIds.Count == 0)
        {
            return BadRequest("At least one hardware ID must be specified");
        }

        try
        {
            var drivers = await _catalogManager.QueryDriversAsync(
                hardwareIds, vendor, operatingSystem, cancellationToken);

            var limitedResults = drivers.Take(maxResults).ToList();

            await _logger.LogInformationAsync(
                $"Catalog query returned {limitedResults.Count} drivers for {hardwareIds.Count} hardware IDs");

            return Ok(limitedResults);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to query driver catalog");
            return StatusCode(500, "Failed to query driver catalog");
        }
    }

    /// <summary>
    /// Gets comprehensive catalog statistics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Catalog statistics</returns>
    /// <response code="200">Statistics retrieved successfully</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(CatalogStatistics), 200)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<CatalogStatistics>> GetCatalogStatistics(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statistics = await _catalogManager.GetCatalogStatisticsAsync(cancellationToken);

            await _logger.LogInformationAsync(
                $"Retrieved catalog statistics: {statistics.TotalVendors} vendors, {statistics.TotalDrivers} drivers");

            return Ok(statistics);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to get catalog statistics");
            return StatusCode(500, "Failed to get catalog statistics");
        }
    }

    /// <summary>
    /// Cleans up old catalog entries based on retention policy
    /// </summary>
    /// <param name="retentionDays">Number of days to retain catalog entries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cleanup operation result</returns>
    /// <response code="200">Cleanup completed successfully</response>
    /// <response code="400">Invalid retention period</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("cleanup")]
    [ProducesResponseType(typeof(CatalogCleanupResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<CatalogCleanupResult>> CleanupCatalog(
        [FromQuery, Range(1, 3650)] int retentionDays = 365,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var retentionPeriod = TimeSpan.FromDays(retentionDays);
            var result = await _catalogManager.CleanupCatalogAsync(retentionPeriod, cancellationToken);

            await _logger.LogInformationAsync(
                $"Catalog cleanup completed: {result.EntriesRemoved} entries removed from {result.FilesModified} files");

            return Ok(result);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to cleanup catalog");
            return StatusCode(500, "Failed to cleanup catalog");
        }
    }

    /// <summary>
    /// Exports the entire driver catalog for backup or offline analysis
    /// </summary>
    /// <param name="format">Export format (json or csv)</param>
    /// <param name="exportPath">Optional custom export path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Export operation result</returns>
    /// <response code="200">Export completed successfully</response>
    /// <response code="400">Invalid format</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("export")]
    [ProducesResponseType(typeof(CatalogExportResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<CatalogExportResult>> ExportCatalog(
        [FromQuery] CatalogExportFormat format = CatalogExportFormat.Json,
        [FromQuery] string? exportPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use default export path if not specified
            exportPath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AeroDriver",
                "Exports");

            var result = await _catalogManager.ExportCatalogAsync(exportPath, format, cancellationToken);

            await _logger.LogInformationAsync(
                $"Catalog exported to {result.FilePath} in {format} format");

            return Ok(result);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to export catalog");
            return StatusCode(500, "Failed to export catalog");
        }
    }

    /// <summary>
    /// Advanced driver search with filtering options
    /// </summary>
    /// <param name="request">Search request with filters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of drivers matching the search criteria</returns>
    /// <response code="200">Search completed successfully</response>
    /// <response code="400">Invalid request</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("search")]
    [ProducesResponseType(typeof(List<DriverCatalogEntry>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<List<DriverCatalogEntry>>> SearchDrivers(
        [FromBody] DriverSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var drivers = await _catalogManager.QueryDriversAsync(
                request.HardwareIds,
                request.Vendor,
                request.OperatingSystem,
                cancellationToken);

            // Apply additional filters
            var filteredDrivers = drivers.AsEnumerable();

            if (request.Category.HasValue)
            {
                filteredDrivers = filteredDrivers.Where(d => d.Category == request.Category.Value);
            }

            if (request.IsBeta.HasValue)
            {
                filteredDrivers = filteredDrivers.Where(d => d.IsBeta == request.IsBeta.Value);
            }

            if (request.IsCritical.HasValue)
            {
                filteredDrivers = filteredDrivers.Where(d => d.IsCritical == request.IsCritical.Value);
            }

            if (request.ReleasedAfter.HasValue)
            {
                filteredDrivers = filteredDrivers.Where(d => d.ReleaseDate >= request.ReleasedAfter.Value);
            }

            if (request.ReleasedBefore.HasValue)
            {
                filteredDrivers = filteredDrivers.Where(d => d.ReleaseDate <= request.ReleasedBefore.Value);
            }

            var results = filteredDrivers
                .Take(request.MaxResults ?? 100)
                .ToList();

            await _logger.LogInformationAsync(
                $"Advanced driver search returned {results.Count} results");

            return Ok(results);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to search drivers");
            return StatusCode(500, "Failed to search drivers");
        }
    }

    /// <summary>
    /// Gets supported export formats
    /// </summary>
    /// <returns>List of supported export formats</returns>
    /// <response code="200">Formats retrieved successfully</response>
    [HttpGet("export/formats")]
    [ProducesResponseType(typeof(Dictionary<string, string>), 200)]
    public ActionResult<Dictionary<string, string>> GetExportFormats()
    {
        var formats = new Dictionary<string, string>
        {
            ["json"] = "Structured JSON format with full metadata for programmatic processing",
            ["csv"] = "Comma-separated values for spreadsheet analysis and reporting"
        };

        return Ok(formats);
    }
}

/// <summary>
/// Request model for advanced driver search
/// </summary>
public class DriverSearchRequest
{
    [Required]
    [MinLength(1)]
    public List<string> HardwareIds { get; set; } = new();

    public string? Vendor { get; set; }

    public string? OperatingSystem { get; set; }

    public DriverCategory? Category { get; set; }

    public bool? IsBeta { get; set; }

    public bool? IsCritical { get; set; }

    public DateTime? ReleasedAfter { get; set; }

    public DateTime? ReleasedBefore { get; set; }

    [Range(1, 1000)]
    public int? MaxResults { get; set; } = 100;
}
