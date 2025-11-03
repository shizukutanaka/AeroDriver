using Microsoft.AspNetCore.Mvc;
using AeroDriver.Core;
using AeroDriver.Core.Services;
using AeroDriver.Core.Validation;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DriversController : ControllerBase
{
    private readonly CoreDriverService _driverService;
    private readonly ISimpleLogger _logger;

    public DriversController(CoreDriverService driverService, ISimpleLogger logger)
    {
        _driverService = driverService;
        _logger = logger;
    }

    /// <summary>
    /// Get all drivers
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllDrivers(CancellationToken cancellationToken)
    {
        try
        {
            var drivers = await _driverService.GetAllDriversAsync(cancellationToken).ConfigureAwait(false);
            return Ok(new ApiResponse<IEnumerable<DriverInfo>>
            {
                Success = true,
                Data = drivers,
                Message = $"Retrieved {drivers.Count} drivers"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting all drivers: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error retrieving drivers: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Get driver by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDriver(string id, CancellationToken cancellationToken)
    {
        try
        {
            var driver = await _driverService.GetDriverByIdAsync(id, cancellationToken).ConfigureAwait(false);

            if (driver == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Driver with ID '{id}' not found"
                });
            }

            return Ok(new ApiResponse<DriverInfo>
            {
                Success = true,
                Data = driver,
                Message = "Driver retrieved successfully"
            });
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning($"Invalid driver request: {InputValidator.SanitizeForLogging(id)} - {ex.Message}");
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting driver {id}: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error retrieving driver: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Scan for drivers
    /// </summary>
    [HttpPost("scan")]
    public async Task<IActionResult> ScanDrivers(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _driverService.ScanSystemAsync(cancellationToken).ConfigureAwait(false);

            return Ok(new ApiResponse<DriverScanResult>
            {
                Success = true,
                Data = result,
                Message = "Driver scan completed successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error scanning drivers: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error scanning drivers: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Install available driver updates via Windows Update Agent
    /// </summary>
    [HttpPost("update")]
    public async Task<IActionResult> UpdateDrivers(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _driverService.UpdateDriversAsync(cancellationToken).ConfigureAwait(false);

            return Ok(new ApiResponse<DriverUpdateResult>
            {
                Success = result.Success,
                Data = result,
                Message = result.Message
            });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new ApiResponse<object>
            {
                Success = false,
                Message = "Driver update request was cancelled."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating drivers: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error updating drivers: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Generate a compliance report for installed drivers
    /// </summary>
    [HttpGet("compliance")]
    public async Task<IActionResult> GetComplianceReport([FromQuery] int? staleThresholdDays, CancellationToken cancellationToken)
    {
        try
        {
            var thresholdDays = staleThresholdDays.GetValueOrDefault(180);
            thresholdDays = Math.Clamp(thresholdDays, 30, 3650);
            var report = await _driverService.GenerateComplianceReportAsync(TimeSpan.FromDays(thresholdDays), cancellationToken).ConfigureAwait(false);

            return Ok(new ApiResponse<DriverComplianceReport>
            {
                Success = true,
                Data = report,
                Message = $"Compliance report generated with threshold {thresholdDays} days"
            });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new ApiResponse<object>
            {
                Success = false,
                Message = "Compliance report request was cancelled."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error generating compliance report: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error generating compliance report: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Backup specific driver
    /// </summary>
    [HttpPost("{id}/backup")]
    public async Task<IActionResult> BackupDriver(string id, CancellationToken cancellationToken)
    {
        try
        {
            _ = cancellationToken; // Reserved for future cancellation-enabled overload
            var result = await _driverService.BackupDriverAsync(id).ConfigureAwait(false);

            return Ok(new ApiResponse<OperationResult>
            {
                Success = result.Success,
                Data = result,
                Message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error backing up driver {id}: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error backing up driver: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Backup all drivers
    /// </summary>
    [HttpPost("backup")]
    public async Task<IActionResult> BackupAllDrivers()
    {
        try
        {
            var result = await _driverService.BackupAllDriversAsync().ConfigureAwait(false);

            return Ok(new ApiResponse<BatchOperationResult>
            {
                Success = result.Success,
                Data = result,
                Message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error backing up all drivers: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error backing up drivers: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Get driver problems
    /// </summary>
    [HttpGet("problems")]
    public async Task<IActionResult> GetProblems(CancellationToken cancellationToken)
    {
        try
        {
            var drivers = await _driverService.GetAllDriversAsync(cancellationToken).ConfigureAwait(false);
            var problemDrivers = drivers.Where(d => d.Status != "OK").ToList();

            return Ok(new ApiResponse<IEnumerable<DriverInfo>>
            {
                Success = true,
                Data = problemDrivers,
                Message = $"Found {problemDrivers.Count} problem drivers"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting driver problems: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error retrieving driver problems: {ex.Message}"
            });
        }
    }
}

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly CoreDriverService _driverService;
    private readonly ISimpleLogger _logger;

    public SystemController(CoreDriverService driverService, ISimpleLogger logger)
    {
        _driverService = driverService;
        _logger = logger;
    }

    /// <summary>
    /// Get system status
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetSystemStatus(CancellationToken cancellationToken)
    {
        try
        {
            var drivers = await _driverService.GetAllDriversAsync(cancellationToken).ConfigureAwait(false);
            var systemStats = new SystemStats
            {
                TotalDrivers = drivers.Count,
                ActiveDrivers = drivers.Count(d => d.Status == "OK"),
                ProblemDrivers = drivers.Count(d => d.Status != "OK"),
                OutdatedDrivers = drivers.Count(d => d.Status == "Outdated"),
                UnsignedDrivers = drivers.Count(d => !d.IsSigned),
                LastScanTime = DateTime.Now
            };

            return Ok(new ApiResponse<SystemStats>
            {
                Success = true,
                Data = systemStats,
                Message = "System status retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting system status: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error retrieving system status: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Optimize system
    /// </summary>
    [HttpPost("optimize")]
    public async Task<IActionResult> OptimizeSystem(CancellationToken cancellationToken)
    {
        try
        {
            _ = cancellationToken;
            var result = await _driverService.OptimizeSystemAsync().ConfigureAwait(false);

            return Ok(new ApiResponse<OperationResult>
            {
                Success = result.Success,
                Data = result,
                Message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error optimizing system: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error optimizing system: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Fix driver issues
    /// </summary>
    [HttpPost("fix")]
    public async Task<IActionResult> FixIssues(CancellationToken cancellationToken)
    {
        try
        {
            _ = cancellationToken;
            var result = await _driverService.FixIssuesAsync().ConfigureAwait(false);

            return Ok(new ApiResponse<BatchOperationResult>
            {
                Success = result.Success,
                Data = result,
                Message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fixing issues: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = $"Error fixing issues: {ex.Message}"
            });
        }
    }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
