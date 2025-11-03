using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Services;

public class DriverRepository : IDriverRepository
{
    private static readonly TimeSpan DriverCacheDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StatsCacheDuration = TimeSpan.FromMinutes(1);

    private readonly ISimpleLogger _logger;
    private readonly Dictionary<string, int> _cachedStats = new();
    private DateTime _lastStatsUpdateUtc = DateTime.MinValue;
    private List<DriverInfo>? _cachedDrivers;
    private DateTime _lastDriversUpdateUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _driverCacheLock = new(1, 1);
    private readonly SemaphoreSlim _statsCacheLock = new(1, 1);

    public DriverRepository(ISimpleLogger logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<DriverInfo>> GetAllDriversAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("Driver enumeration only supported on Windows");
            return Array.Empty<DriverInfo>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _driverCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedDrivers != null && DateTime.UtcNow - _lastDriversUpdateUtc < DriverCacheDuration)
            {
                return CloneDrivers(_cachedDrivers);
            }
        }
        finally
        {
            _driverCacheLock.Release();
        }

        List<DriverInfo> drivers;

        try
        {
            drivers = await Task.Run(() => QueryDrivers(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to enumerate drivers: {ex.Message}");
            return Array.Empty<DriverInfo>();
        }

        await _driverCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedDrivers = drivers;
            _lastDriversUpdateUtc = DateTime.UtcNow;
        }
        finally
        {
            _driverCacheLock.Release();
        }

        return CloneDrivers(drivers);
    }

    public async Task<DriverInfo?> GetDriverByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var drivers = await GetAllDriversAsync(cancellationToken).ConfigureAwait(false);
        return drivers.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> UpdateDriverAsync(DriverInfo driver, CancellationToken cancellationToken = default)
    {
        if (driver == null)
        {
            throw new ArgumentNullException(nameof(driver));
        }

        try
        {
            _logger.LogInformation($"Driver update requested for: {driver.Name}");
            // In a real implementation, this would update the actual driver
            // For now, just simulate success
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to update driver {driver.Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteDriverAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            _logger.LogInformation($"Driver deletion requested for ID: {id}");
            // In a real implementation, this would remove the driver
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete driver {id}: {ex.Message}");
            return false;
        }
    }

    public async Task<Dictionary<string, int>> GetDriverStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _statsCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedStats.Count > 0 && DateTime.UtcNow - _lastStatsUpdateUtc < StatsCacheDuration)
            {
                return new Dictionary<string, int>(_cachedStats);
            }
        }
        finally
        {
            _statsCacheLock.Release();
        }

        var drivers = (await GetAllDriversAsync(cancellationToken).ConfigureAwait(false)).ToList();

        var stats = new Dictionary<string, int>
        {
            ["Total"] = drivers.Count,
            ["OK"] = drivers.Count(d => string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase)),
            ["Error"] = drivers.Count(d => !string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase) && !string.Equals(d.Status, "Unknown", StringComparison.OrdinalIgnoreCase)),
            ["Warning"] = drivers.Count(d => string.Equals(d.Status, "Unknown", StringComparison.OrdinalIgnoreCase)),
            ["Critical"] = drivers.Count(d => d.IsEssential && !string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase))
        };

        await _statsCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedStats.Clear();
            foreach (var kvp in stats)
            {
                _cachedStats[kvp.Key] = kvp.Value;
            }

            _lastStatsUpdateUtc = DateTime.UtcNow;
        }
        finally
        {
            _statsCacheLock.Release();
        }

        return stats;
    }

    public async Task<IEnumerable<DriverInfo>> GetBackupsAsync(string driverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Simulate backup retrieval
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        return new List<DriverInfo>();
    }

    public async Task<bool> BackupDriverAsync(string driverId, string backupName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            return false;
        }

        try
        {
            _logger.LogInformation($"Creating backup '{backupName}' for driver: {driverId}");
            // Simulate backup creation
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to backup driver {driverId}: {ex.Message}");
            return false;
        }
    }

    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        await _driverCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedDrivers = null;
            _lastDriversUpdateUtc = DateTime.MinValue;
        }
        finally
        {
            _driverCacheLock.Release();
        }

        await _statsCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedStats.Clear();
            _lastStatsUpdateUtc = DateTime.MinValue;
        }
        finally
        {
            _statsCacheLock.Release();
        }

        _logger.LogInformation("Driver cache cleared");
    }

    private static int GetDevicePriority(string? deviceClass)
    {
        return deviceClass?.ToLowerInvariant() switch
        {
            "system" => 1,
            "processor" => 1,
            "computer" => 1,
            "diskdrive" => 2,
            "display" => 2,
            "net" => 3,
            "usb" => 4,
            "hidclass" => 4,
            _ => 5
        };
    }

    private List<DriverInfo> QueryDrivers(CancellationToken cancellationToken)
    {
        var drivers = new List<DriverInfo>();

        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity");
        using var collection = searcher.Get();

        foreach (ManagementObject device in collection)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var driver = new DriverInfo
                {
                    Id = device["DeviceID"]?.ToString() ?? Guid.NewGuid().ToString(),
                    Name = device["Name"]?.ToString() ?? "Unknown Device",
                    Status = device["Status"]?.ToString() ?? "Unknown",
                    DeviceClass = device["PNPClass"]?.ToString() ?? "Unknown",
                    Manufacturer = device["Manufacturer"]?.ToString() ?? "Unknown",
                    DeviceId = device["DeviceID"]?.ToString() ?? string.Empty,
                    Version = device["DriverVersion"]?.ToString() ?? string.Empty,
                    Priority = GetDevicePriority(device["PNPClass"]?.ToString()),
                    DriverPath = device["Driver"]?.ToString() ?? string.Empty,
                    Provider = device["ProviderName"]?.ToString() ?? string.Empty,
                    Location = device["LocationInformation"]?.ToString() ?? string.Empty,
                    IsSigned = IsDriverSigned(device)
                };

                var installDate = ParseManagementDate(device["InstallDate"]);
                if (installDate.HasValue)
                {
                    driver.InstallDate = installDate.Value;
                }

                var driverDate = ParseManagementDate(device["DriverDate"]);
                if (driverDate.HasValue)
                {
                    driver.DriverDate = driverDate.Value;
                }

                driver.Metadata["ClassGuid"] = device["ClassGuid"]?.ToString() ?? string.Empty;
                driver.Metadata["HardwareId"] = device["HardwareID"] is string[] hardwareIds ? string.Join(";", hardwareIds) : string.Empty;

                drivers.Add(driver);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing device: {ex.Message}");
            }
        }

        return drivers;
    }

    private static bool IsDriverSigned(ManagementObject device)
    {
        var status = device["ConfigManagerErrorCode"] as int?;
        return status is null or 0;
    }

    private static DateTime? ParseManagementDate(object? value)
    {
        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                return ManagementDateTimeConverter.ToDateTime(text);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static List<DriverInfo> CloneDrivers(List<DriverInfo> source)
    {
        return source.Select(CloneDriverInfo).ToList();
    }

    private static DriverInfo CloneDriverInfo(DriverInfo driver)
    {
        return new DriverInfo
        {
            Id = driver.Id,
            Name = driver.Name,
            Version = driver.Version,
            Status = driver.Status,
            Type = driver.Type,
            DeviceClass = driver.DeviceClass,
            DeviceId = driver.DeviceId,
            Provider = driver.Provider,
            Manufacturer = driver.Manufacturer,
            Location = driver.Location,
            DriverPath = driver.DriverPath,
            Path = driver.Path,
            IsSigned = driver.IsSigned,
            IsEssential = driver.IsEssential,
            Priority = driver.Priority,
            FileSize = driver.FileSize,
            InstallDate = driver.InstallDate,
            DriverDate = driver.DriverDate,
            LastUpdated = driver.LastUpdated,
            Metadata = new Dictionary<string, string>(driver.Metadata)
        };
    }
}