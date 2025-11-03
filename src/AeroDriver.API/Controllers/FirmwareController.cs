using Microsoft.AspNetCore.Mvc;
using AeroDriver.Core.Firmware;
using System.ComponentModel.DataAnnotations;

namespace AeroDriver.API.Controllers;

/// <summary>
/// Enterprise firmware management API
/// Provides comprehensive firmware and BIOS update capabilities for enterprise environments
/// </summary>
[ApiController]
[Route("api/firmware")]
[Produces("application/json")]
public class FirmwareController : ControllerBase
{
    private readonly FirmwareManager _firmwareManager;
    private readonly ISimpleLogger _logger;

    public FirmwareController(FirmwareManager firmwareManager, ISimpleLogger logger)
    {
        _firmwareManager = firmwareManager ?? throw new ArgumentNullException(nameof(firmwareManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Scans the system for firmware and BIOS information
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comprehensive firmware inventory</returns>
    /// <response code="200">Firmware inventory scanned successfully</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("inventory")]
    [ProducesResponseType(typeof(FirmwareInventory), 200)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<FirmwareInventory>> GetFirmwareInventory(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var inventory = await _firmwareManager.ScanFirmwareInventoryAsync(cancellationToken);

            await _logger.LogInformationAsync(
                $"Firmware inventory scanned: BIOS {inventory.BiosInfo?.Version}, {inventory.FirmwareDevices.Count} devices");

            return Ok(inventory);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to scan firmware inventory");
            return StatusCode(500, "Failed to scan firmware inventory");
        }
    }

    /// <summary>
    /// Checks for available firmware updates
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available firmware updates</returns>
    /// <response code="200">Update check completed successfully</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("updates/check")]
    [ProducesResponseType(typeof(List<FirmwareUpdateCheck>), 200)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<List<FirmwareUpdateCheck>>> CheckForFirmwareUpdates(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updates = await _firmwareManager.CheckForFirmwareUpdatesAsync(cancellationToken);

            await _logger.LogInformationAsync(
                $"Firmware update check completed: {updates.Count} updates available");

            return Ok(updates);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to check for firmware updates");
            return StatusCode(500, "Failed to check for firmware updates");
        }
    }

    /// <summary>
    /// Updates BIOS firmware
    /// </summary>
    /// <param name="request">BIOS update request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>BIOS update result</returns>
    /// <response code="200">BIOS update completed</response>
    /// <response code="400">Invalid request</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("bios/update")]
    [ProducesResponseType(typeof(FirmwareUpdateResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<FirmwareUpdateResult>> UpdateBios(
        [FromBody] BiosUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var package = new BiosUpdatePackage
            {
                Version = request.Version,
                FilePath = request.PackagePath,
                Manufacturer = request.Manufacturer,
                UpdateMethod = request.UpdateMethod,
                RequiresReboot = request.RequiresReboot,
                RequiresSecureBootDisable = request.RequiresSecureBootDisable,
                Checksum = request.Checksum,
                Description = request.Description,
                ReleaseDate = request.ReleaseDate,
                IsCritical = request.IsCritical,
                SupportedModels = request.SupportedModels ?? new List<string>()
            };

            var options = new FirmwareUpdateOptions
            {
                CreateRestorePoint = request.CreateRestorePoint,
                ForceUpdate = request.ForceUpdate,
                VerifyAfterUpdate = request.VerifyAfterUpdate,
                BackupCurrentFirmware = request.BackupCurrentFirmware
            };

            var result = await _firmwareManager.UpdateBiosAsync(package, options, cancellationToken);

            await _logger.LogInformationAsync(
                $"BIOS update {(result.Success ? "succeeded" : "failed")}: {result.Message}");

            return Ok(result);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to update BIOS");
            return StatusCode(500, "Failed to update BIOS");
        }
    }

    /// <summary>
    /// Updates firmware for a specific hardware component
    /// </summary>
    /// <param name="deviceId">Device identifier</param>
    /// <param name="request">Firmware update request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Firmware update result</returns>
    /// <response code="200">Firmware update completed</response>
    /// <response code="400">Invalid request</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("devices/{deviceId}/update")]
    [ProducesResponseType(typeof(FirmwareUpdateResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<FirmwareUpdateResult>> UpdateFirmware(
        string deviceId,
        [FromBody] FirmwareUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest("Device ID is required");
        }

        try
        {
            var package = new FirmwareUpdatePackage
            {
                Version = request.Version,
                FilePath = request.PackagePath,
                Manufacturer = request.Manufacturer,
                UpdateMethod = request.UpdateMethod,
                Checksum = request.Checksum,
                Description = request.Description,
                ReleaseDate = request.ReleaseDate,
                IsCritical = request.IsCritical
            };

            var options = new FirmwareUpdateOptions
            {
                CreateRestorePoint = request.CreateRestorePoint,
                ForceUpdate = request.ForceUpdate,
                VerifyAfterUpdate = request.VerifyAfterUpdate,
                BackupCurrentFirmware = request.BackupCurrentFirmware
            };

            var result = await _firmwareManager.UpdateFirmwareAsync(deviceId, package, options, cancellationToken);

            await _logger.LogInformationAsync(
                $"Firmware update for device {deviceId} {(result.Success ? "succeeded" : "failed")}: {result.Message}");

            return Ok(result);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, $"Failed to update firmware for device {deviceId}");
            return StatusCode(500, $"Failed to update firmware for device {deviceId}");
        }
    }

    /// <summary>
    /// Executes a comprehensive firmware update campaign
    /// </summary>
    /// <param name="request">Firmware campaign request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Firmware campaign result</returns>
    /// <response code="200">Campaign completed</response>
    /// <response code="400">Invalid request</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("campaigns")]
    [ProducesResponseType(typeof(FirmwareCampaignResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<FirmwareCampaignResult>> ExecuteFirmwareCampaign(
        [FromBody] FirmwareCampaignRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var campaign = new FirmwareCampaign
            {
                Name = request.Name,
                Description = request.Description,
                AllowPartialSuccess = request.AllowPartialSuccess,
                Targets = request.Targets.Select(t => new FirmwareCampaignTarget
                {
                    DeviceId = t.DeviceId,
                    ComponentType = t.ComponentType,
                    Package = t.ComponentType == FirmwareComponentType.BIOS ?
                        new BiosUpdatePackage
                        {
                            Version = t.Version,
                            FilePath = t.PackagePath,
                            Manufacturer = t.Manufacturer,
                            UpdateMethod = t.UpdateMethod,
                            RequiresReboot = t.RequiresReboot ?? false,
                            Checksum = t.Checksum,
                            Description = t.Description,
                            ReleaseDate = t.ReleaseDate,
                            IsCritical = t.IsCritical
                        } :
                        new FirmwareUpdatePackage
                        {
                            Version = t.Version,
                            FilePath = t.PackagePath,
                            Manufacturer = t.Manufacturer,
                            UpdateMethod = t.UpdateMethod,
                            Checksum = t.Checksum,
                            Description = t.Description,
                            ReleaseDate = t.ReleaseDate,
                            IsCritical = t.IsCritical
                        }
                }).ToList(),
                Options = new FirmwareUpdateOptions
                {
                    CreateRestorePoint = request.CreateRestorePoint,
                    ForceUpdate = request.ForceUpdate,
                    VerifyAfterUpdate = request.VerifyAfterUpdate,
                    BackupCurrentFirmware = request.BackupCurrentFirmware
                }
            };

            var result = await _firmwareManager.ExecuteFirmwareCampaignAsync(campaign, cancellationToken);

            await _logger.LogInformationAsync(
                $"Firmware campaign {campaign.Id} completed: {result.SuccessfulUpdates}/{result.TotalTargets} successful");

            return Ok(result);
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync(ex, "Failed to execute firmware campaign");
            return StatusCode(500, "Failed to execute firmware campaign");
        }
    }
}

/// <summary>
/// Request model for BIOS updates
/// </summary>
public class BiosUpdateRequest
{
    [Required]
    public string Version { get; set; } = string.Empty;

    [Required]
    public string PackagePath { get; set; } = string.Empty;

    [Required]
    public string Manufacturer { get; set; } = string.Empty;

    public FirmwareUpdateMethod UpdateMethod { get; set; } = FirmwareUpdateMethod.Executable;

    public bool RequiresReboot { get; set; } = true;

    public bool RequiresSecureBootDisable { get; set; }

    public string Checksum { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;

    public bool IsCritical { get; set; }

    public List<string> SupportedModels { get; set; } = new();

    public bool CreateRestorePoint { get; set; } = true;

    public bool ForceUpdate { get; set; }

    public bool VerifyAfterUpdate { get; set; } = true;

    public bool BackupCurrentFirmware { get; set; } = true;
}

/// <summary>
/// Request model for firmware updates
/// </summary>
public class FirmwareUpdateRequest
{
    [Required]
    public string Version { get; set; } = string.Empty;

    [Required]
    public string PackagePath { get; set; } = string.Empty;

    [Required]
    public string Manufacturer { get; set; } = string.Empty;

    public FirmwareUpdateMethod UpdateMethod { get; set; } = FirmwareUpdateMethod.Executable;

    public string Checksum { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;

    public bool IsCritical { get; set; }

    public bool CreateRestorePoint { get; set; } = true;

    public bool ForceUpdate { get; set; }

    public bool VerifyAfterUpdate { get; set; } = true;

    public bool BackupCurrentFirmware { get; set; } = true;
}

/// <summary>
/// Request model for firmware update campaigns
/// </summary>
public class FirmwareCampaignRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<FirmwareCampaignTargetRequest> Targets { get; set; } = new();

    public bool AllowPartialSuccess { get; set; }

    public bool CreateRestorePoint { get; set; } = true;

    public bool ForceUpdate { get; set; }

    public bool VerifyAfterUpdate { get; set; } = true;

    public bool BackupCurrentFirmware { get; set; } = true;
}

/// <summary>
/// Target component in a firmware campaign request
/// </summary>
public class FirmwareCampaignTargetRequest
{
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    public FirmwareComponentType ComponentType { get; set; }

    [Required]
    public string Version { get; set; } = string.Empty;

    [Required]
    public string PackagePath { get; set; } = string.Empty;

    [Required]
    public string Manufacturer { get; set; } = string.Empty;

    public FirmwareUpdateMethod UpdateMethod { get; set; } = FirmwareUpdateMethod.Executable;

    public bool? RequiresReboot { get; set; }

    public string Checksum { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;

    public bool IsCritical { get; set; }
}
