using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

// ===== Core Models =====
public record Driver(
    string Id,
    string Name,
    string Version,
    string DeviceType,
    DateTime LastUpdated,
    DriverStatus Status,
    string Vendor,
    string FilePath = "",
    long FileSize = 0,
    string Checksum = ""
);

public record DriverBackup(
    string Id,
    string DriverId,
    string Version,
    DateTime BackupDate,
    string BackupPath,
    string Checksum,
    BackupType Type,
    long CompressedSize = 0,
    long OriginalSize = 0
);

public record InstallationHistory(
    string Id,
    string DriverId,
    string FromVersion,
    string ToVersion,
    DateTime InstallationDate,
    InstallationStatus Status,
    string BackupId,
    string ErrorMessage = "",
    TimeSpan Duration = default
);

public enum DriverStatus { Good, Warning, Critical, Installing, Failed, Rollback }
public enum InstallationStatus { Pending, InProgress, Success, Failed, RolledBack }
public enum BackupType { Auto, Manual, PreInstall }

// ===== Driver Repository =====
public interface IDriverRepository
{
    Task<List<Driver>> GetAllDriversAsync();
    Task<Driver> GetDriverByIdAsync(string id);
    Task<Driver> UpdateDriverAsync(Driver driver);
}

public class DriverRepository : IDriverRepository
{
    private readonly List<Driver> _drivers = new();
    private readonly ILogger<DriverRepository> _logger;
    private readonly string _dataFile;

    public DriverRepository(ILogger<DriverRepository> logger, IConfiguration config)
    {
        _logger = logger;
        _dataFile = config.GetValue<string>("DataFile", "drivers.json");
        LoadDriversAsync().Wait();
        
        // Initialize with sample drivers if none exist
        if (!_drivers.Any())
        {
            InitializeSampleDrivers();
            SaveDriversAsync().Wait();
        }
    }

    private void InitializeSampleDrivers()
    {
        _drivers.AddRange(new[]
        {
            new Driver("nvidia-geforce", "NVIDIA GeForce Driver", "531.41", "Graphics", 
                DateTime.UtcNow.AddDays(-5), DriverStatus.Warning, "NVIDIA"),
            new Driver("intel-hd", "Intel HD Graphics Driver", "30.0.101.1404", "Graphics", 
                DateTime.UtcNow.AddDays(-2), DriverStatus.Good, "Intel"),
            new Driver("realtek-audio", "Realtek High Definition Audio", "6.0.9088.1", "Audio", 
                DateTime.UtcNow.AddDays(-10), DriverStatus.Critical, "Realtek")
        });
    }

    public async Task<List<Driver>> GetAllDriversAsync()
    {
        return _drivers.ToList();
    }

    public async Task<Driver> GetDriverByIdAsync(string id)
    {
        return _drivers.FirstOrDefault(d => d.Id == id);
    }

    public async Task<Driver> UpdateDriverAsync(Driver driver)
    {
        var index = _drivers.FindIndex(d => d.Id == driver.Id);
        if (index >= 0)
        {
            _drivers[index] = driver;
            await SaveDriversAsync();
        }
        return driver;
    }

    private async Task LoadDriversAsync()
    {
        try
        {
            if (File.Exists(_dataFile))
            {
                var json = await File.ReadAllTextAsync(_dataFile);
                var drivers = JsonSerializer.Deserialize<List<Driver>>(json);
                if (drivers != null)
                {
                    _drivers.Clear();
                    _drivers.AddRange(drivers);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading drivers from file");
        }
    }

    private async Task SaveDriversAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_drivers, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(_dataFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving drivers to file");
        }
    }
}

// ===== Backup Manager =====
public class DriverBackupManager
{
    private readonly ILogger<DriverBackupManager> _logger;
    private readonly string _backupPath;
    private readonly List<DriverBackup> _backups = new();
    private const int MAX_BACKUP_GENERATIONS = 3;

    public DriverBackupManager(ILogger<DriverBackupManager> logger, IConfiguration config)
    {
        _logger = logger;
        _backupPath = config.GetValue<string>("BackupPath", "./backups");
        Directory.CreateDirectory(_backupPath);
        LoadBackupsAsync().Wait();
    }

    public async Task<DriverBackup> CreateBackupAsync(Driver driver, BackupType type)
    {
        _logger.LogInformation($"Creating {type} backup for {driver.Name}");

        var backupId = Guid.NewGuid().ToString();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"{driver.Id}_{driver.Version}_{timestamp}.backup";
        var backupFullPath = Path.Combine(_backupPath, backupFileName);

        // Create a mock driver file for backup simulation
        var tempDriverFile = Path.Combine(Path.GetTempPath(), $"{driver.Id}_temp.driver");
        await File.WriteAllTextAsync(tempDriverFile, $"Mock driver file for {driver.Name} v{driver.Version}");

        // Compress the driver file
        using var fileStream = File.Create(backupFullPath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Compress);
        
        if (File.Exists(tempDriverFile))
        {
            using var sourceStream = File.OpenRead(tempDriverFile);
            await sourceStream.CopyToAsync(gzipStream);
        }

        var originalSize = new FileInfo(tempDriverFile).Length;
        var compressedSize = new FileInfo(backupFullPath).Length;

        // Cleanup temp file
        File.Delete(tempDriverFile);

        // Calculate checksum
        var checksum = await ComputeChecksumAsync(backupFullPath);

        var backup = new DriverBackup(
            backupId,
            driver.Id,
            driver.Version,
            DateTime.UtcNow,
            backupFullPath,
            checksum,
            type,
            compressedSize,
            originalSize
        );

        _backups.Add(backup);
        await SaveBackupsAsync();

        // Maintain 3 generations
        await MaintainBackupGenerationsAsync(driver.Id);

        _logger.LogInformation($"Backup created: {backupId} ({compressedSize} bytes, {(1.0 - (double)compressedSize / originalSize) * 100:F1}% compression)");
        return backup;
    }

    private async Task MaintainBackupGenerationsAsync(string driverId)
    {
        var driverBackups = _backups.Where(b => b.DriverId == driverId)
                                  .OrderByDescending(b => b.BackupDate)
                                  .ToList();

        // Remove backups beyond the 3rd generation
        for (int i = MAX_BACKUP_GENERATIONS; i < driverBackups.Count; i++)
        {
            var oldBackup = driverBackups[i];
            if (File.Exists(oldBackup.BackupPath))
            {
                File.Delete(oldBackup.BackupPath);
                _logger.LogInformation($"Deleted old backup: {oldBackup.Id}");
            }
            _backups.Remove(oldBackup);
        }

        await SaveBackupsAsync();
    }

    public async Task<bool> RestoreFromBackupAsync(DriverBackup backup, string targetPath)
    {
        try
        {
            _logger.LogInformation($"Restoring driver from backup: {backup.Id}");

            // Verify backup integrity
            var currentChecksum = await ComputeChecksumAsync(backup.BackupPath);
            if (currentChecksum != backup.Checksum)
            {
                _logger.LogError($"Backup integrity check failed for {backup.Id}");
                return false;
            }

            // Create target directory
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

            // Restore from backup
            using var fileStream = File.OpenRead(backup.BackupPath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var outputStream = File.Create(targetPath);
            
            await gzipStream.CopyToAsync(outputStream);

            _logger.LogInformation($"Successfully restored driver to {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to restore backup {backup.Id}");
            return false;
        }
    }

    public async Task<List<DriverBackup>> GetBackupsForDriverAsync(string driverId)
    {
        return _backups.Where(b => b.DriverId == driverId)
                      .OrderByDescending(b => b.BackupDate)
                      .Take(MAX_BACKUP_GENERATIONS)
                      .ToList();
    }

    private async Task<string> ComputeChecksumAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await Task.Run(() => sha256.ComputeHash(stream));
        return Convert.ToHexString(hash);
    }

    private async Task LoadBackupsAsync()
    {
        var backupFile = Path.Combine(_backupPath, "backups.json");
        try
        {
            if (File.Exists(backupFile))
            {
                var json = await File.ReadAllTextAsync(backupFile);
                var backups = JsonSerializer.Deserialize<List<DriverBackup>>(json);
                if (backups != null)
                {
                    _backups.Clear();
                    _backups.AddRange(backups);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading backups from file");
        }
    }

    private async Task SaveBackupsAsync()
    {
        var backupFile = Path.Combine(_backupPath, "backups.json");
        try
        {
            var json = JsonSerializer.Serialize(_backups, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(backupFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving backups to file");
        }
    }
}

// ===== Installation History Manager =====
public class InstallationHistoryManager
{
    private readonly List<InstallationHistory> _history = new();
    private readonly ILogger<InstallationHistoryManager> _logger;
    private readonly string _historyFile;

    public InstallationHistoryManager(ILogger<InstallationHistoryManager> logger, IConfiguration config)
    {
        _logger = logger;
        _historyFile = config.GetValue<string>("HistoryFile", "history.json");
        LoadHistoryAsync().Wait();
    }

    public async Task<string> StartInstallationAsync(string driverId, string fromVersion, string toVersion, string backupId)
    {
        var historyId = Guid.NewGuid().ToString();
        var history = new InstallationHistory(
            historyId,
            driverId,
            fromVersion,
            toVersion,
            DateTime.UtcNow,
            InstallationStatus.Pending,
            backupId
        );

        _history.Add(history);
        await SaveHistoryAsync();
        _logger.LogInformation($"Started installation tracking: {historyId}");
        return historyId;
    }

    public async Task UpdateInstallationStatusAsync(string historyId, InstallationStatus status, string errorMessage = "", TimeSpan duration = default)
    {
        var history = _history.FirstOrDefault(h => h.Id == historyId);
        if (history != null)
        {
            var updatedHistory = history with 
            { 
                Status = status, 
                ErrorMessage = errorMessage,
                Duration = duration
            };
            var index = _history.IndexOf(history);
            _history[index] = updatedHistory;
            await SaveHistoryAsync();

            _logger.LogInformation($"Updated installation {historyId}: {status}");
        }
    }

    public List<InstallationHistory> GetHistoryForDriver(string driverId)
    {
        return _history.Where(h => h.DriverId == driverId)
                      .OrderByDescending(h => h.InstallationDate)
                      .ToList();
    }

    public List<InstallationHistory> GetAllHistory()
    {
        return _history.OrderByDescending(h => h.InstallationDate).ToList();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            if (File.Exists(_historyFile))
            {
                var json = await File.ReadAllTextAsync(_historyFile);
                var history = JsonSerializer.Deserialize<List<InstallationHistory>>(json);
                if (history != null)
                {
                    _history.Clear();
                    _history.AddRange(history);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading history from file");
        }
    }

    private async Task SaveHistoryAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(_historyFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving history to file");
        }
    }
}

// ===== Auto Recovery Service =====
public class AutoRecoveryService
{
    private readonly DriverBackupManager _backupManager;
    private readonly InstallationHistoryManager _historyManager;
    private readonly ILogger<AutoRecoveryService> _logger;

    public AutoRecoveryService(
        DriverBackupManager backupManager,
        InstallationHistoryManager historyManager,
        ILogger<AutoRecoveryService> logger)
    {
        _backupManager = backupManager;
        _historyManager = historyManager;
        _logger = logger;
    }

    public async Task<bool> AttemptRecoveryAsync(string driverId, Exception lastError)
    {
        _logger.LogWarning($"Attempting automatic recovery for driver {driverId} due to error: {lastError.Message}");

        try
        {
            // Get the most recent successful backup
            var backups = await _backupManager.GetBackupsForDriverAsync(driverId);
            var lastSuccessfulBackup = backups.FirstOrDefault();

            if (lastSuccessfulBackup == null)
            {
                _logger.LogError($"No backup available for recovery of driver {driverId}");
                return false;
            }

            // Temporary restore location
            var tempRestorePath = Path.GetTempFileName();
            
            // Restore from backup
            bool restored = await _backupManager.RestoreFromBackupAsync(lastSuccessfulBackup, tempRestorePath);
            
            if (restored)
            {
                _logger.LogInformation($"Successfully recovered driver {driverId} from backup {lastSuccessfulBackup.Id}");
                
                // Record in history
                var historyId = await _historyManager.StartInstallationAsync(
                    driverId, 
                    "Failed", 
                    lastSuccessfulBackup.Version, 
                    lastSuccessfulBackup.Id
                );
                await _historyManager.UpdateInstallationStatusAsync(historyId, InstallationStatus.Success);
                
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Auto-recovery failed for driver {driverId}");
        }

        return false;
    }
}

// ===== Auto Driver Manager Service =====
public class AutoDriverManagerService : BackgroundService
{
    private readonly IDriverRepository _driverRepository;
    private readonly DriverBackupManager _backupManager;
    private readonly InstallationHistoryManager _historyManager;
    private readonly AutoRecoveryService _recoveryService;
    private readonly ILogger<AutoDriverManagerService> _logger;
    private readonly Timer _updateTimer;
    private readonly HttpClient _httpClient;

    public AutoDriverManagerService(
        IDriverRepository driverRepository,
        DriverBackupManager backupManager,
        InstallationHistoryManager historyManager,
        AutoRecoveryService recoveryService,
        ILogger<AutoDriverManagerService> logger)
    {
        _driverRepository = driverRepository;
        _backupManager = backupManager;
        _historyManager = historyManager;
        _recoveryService = recoveryService;
        _logger = logger;
        _httpClient = new HttpClient();
        
        // Check for updates every 30 minutes
        _updateTimer = new Timer(CheckForUpdates, null, TimeSpan.Zero, TimeSpan.FromMinutes(30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Auto Driver Manager Service started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async void CheckForUpdates(object state)
    {
        try
        {
            _logger.LogInformation("Starting automatic driver update check");
            
            var drivers = await _driverRepository.GetAllDriversAsync();
            
            foreach (var driver in drivers)
            {
                if (await ShouldUpdateDriverAsync(driver))
                {
                    await UpdateDriverAutomaticallyAsync(driver);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during automatic update check");
        }
    }

    private async Task<bool> ShouldUpdateDriverAsync(Driver driver)
    {
        // Update drivers that are more than 7 days old and have warning/critical status
        if ((DateTime.UtcNow - driver.LastUpdated).TotalDays > 7 && 
            (driver.Status == DriverStatus.Warning || driver.Status == DriverStatus.Critical))
        {
            return await CheckUpdateAvailableAsync(driver);
        }
        return false;
    }

    private async Task<bool> CheckUpdateAvailableAsync(Driver driver)
    {
        // Simulate checking for updates from vendor website
        await Task.Delay(100);
        
        // Randomly determine if update is available (simulation)
        return Random.Shared.NextDouble() < 0.3;
    }

    public async Task<bool> UpdateDriverAutomaticallyAsync(Driver driver)
    {
        string historyId = "";
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation($"Starting automatic update for {driver.Name}");

            // Create pre-install backup
            var backup = await _backupManager.CreateBackupAsync(driver, BackupType.PreInstall);
            
            // Start installation history
            historyId = await _historyManager.StartInstallationAsync(
                driver.Id, 
                driver.Version, 
                "Auto-Update", 
                backup.Id
            );
            
            await _historyManager.UpdateInstallationStatusAsync(historyId, InstallationStatus.InProgress);

            // Update driver status to installing
            var installingDriver = driver with { Status = DriverStatus.Installing };
            await _driverRepository.UpdateDriverAsync(installingDriver);

            // Download driver
            var downloadSuccess = await DownloadDriverAsync(driver);
            if (!downloadSuccess)
            {
                throw new InvalidOperationException("Driver download failed");
            }

            // Install driver
            var installSuccess = await InstallDriverAsync(driver);
            if (!installSuccess)
            {
                throw new InvalidOperationException("Driver installation failed");
            }

            // Success - update driver info
            stopwatch.Stop();
            await _historyManager.UpdateInstallationStatusAsync(historyId, InstallationStatus.Success, "", stopwatch.Elapsed);
            
            var newVersion = $"{driver.Version}.{Random.Shared.Next(1, 100)}";
            var updatedDriver = driver with 
            { 
                Version = newVersion,
                LastUpdated = DateTime.UtcNow,
                Status = DriverStatus.Good
            };
            await _driverRepository.UpdateDriverAsync(updatedDriver);

            _logger.LogInformation($"Successfully updated {driver.Name} to version {newVersion}");
            return true;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, $"Failed to update driver {driver.Name}");
            
            // Attempt automatic recovery
            var recoverySuccess = await _recoveryService.AttemptRecoveryAsync(driver.Id, ex);
            
            if (!string.IsNullOrEmpty(historyId))
            {
                var status = recoverySuccess ? InstallationStatus.RolledBack : InstallationStatus.Failed;
                await _historyManager.UpdateInstallationStatusAsync(historyId, status, ex.Message, stopwatch.Elapsed);
            }

            // Update driver status
            var failedDriver = driver with 
            { 
                Status = recoverySuccess ? DriverStatus.Rollback : DriverStatus.Failed 
            };
            await _driverRepository.UpdateDriverAsync(failedDriver);

            return recoverySuccess;
        }
    }

    private async Task<bool> DownloadDriverAsync(Driver driver)
    {
        _logger.LogInformation($"Downloading driver: {driver.Name}");
        
        // Simulate downloading driver from vendor website
        await Task.Delay(2000);
        
        // 90% success rate for simulation
        return Random.Shared.NextDouble() < 0.9;
    }

    private async Task<bool> InstallDriverAsync(Driver driver)
    {
        _logger.LogInformation($"Installing driver: {driver.Name}");
        
        // Simulate driver installation process
        await Task.Delay(3000);
        
        // 95% success rate for simulation
        return Random.Shared.NextDouble() < 0.95;
    }

    public override void Dispose()
    {
        _updateTimer?.Dispose();
        _httpClient?.Dispose();
        base.Dispose();
    }
}

// ===== Command Interface =====
public class DriverManagerCommands
{
    private readonly IDriverRepository _driverRepository;
    private readonly DriverBackupManager _backupManager;
    private readonly InstallationHistoryManager _historyManager;
    private readonly AutoDriverManagerService _autoManagerService;
    private readonly ILogger<DriverManagerCommands> _logger;

    public DriverManagerCommands(
        IDriverRepository driverRepository,
        DriverBackupManager backupManager,
        InstallationHistoryManager historyManager,
        AutoDriverManagerService autoManagerService,
        ILogger<DriverManagerCommands> logger)
    {
        _driverRepository = driverRepository;
        _backupManager = backupManager;
        _historyManager = historyManager;
        _autoManagerService = autoManagerService;
        _logger = logger;
    }

    public async Task ShowInstallationHistoryAsync(string driverId = null)
    {
        Console.WriteLine("\n=== Installation History ===");
        
        var history = string.IsNullOrEmpty(driverId) 
            ? _historyManager.GetAllHistory() 
            : _historyManager.GetHistoryForDriver(driverId);

        if (!history.Any())
        {
            Console.WriteLine("No installation history found.");
            return;
        }

        foreach (var record in history.Take(10))
        {
            var statusIcon = record.Status switch
            {
                InstallationStatus.Success => "✓",
                InstallationStatus.Failed => "✗",
                InstallationStatus.RolledBack => "↶",
                InstallationStatus.InProgress => "⋯",
                _ => "?"
            };

            Console.WriteLine($"{statusIcon} {record.InstallationDate:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"   Driver: {record.DriverId}");
            Console.WriteLine($"   Version: {record.FromVersion} → {record.ToVersion}");
            Console.WriteLine($"   Status: {record.Status}");
            Console.WriteLine($"   Duration: {record.Duration.TotalSeconds:F1}s");
            if (!string.IsNullOrEmpty(record.ErrorMessage))
            {
                Console.WriteLine($"   Error: {record.ErrorMessage}");
            }
            Console.WriteLine($"   Backup: {record.BackupId}");
            Console.WriteLine();
        }
    }

    public async Task RestoreFromBackupAsync(string driverId, int backupIndex = 0)
    {
        try
        {
            _logger.LogInformation($"Restoring driver {driverId} from backup (index: {backupIndex})");
            
            var driver = await _driverRepository.GetDriverByIdAsync(driverId);
            if (driver == null)
            {
                Console.WriteLine($"Driver {driverId} not found.");
                return;
            }

            var backups = await _backupManager.GetBackupsForDriverAsync(driverId);
            if (backupIndex >= backups.Count)
            {
                Console.WriteLine($"Backup index {backupIndex} not available.");
                return;
            }

            var selectedBackup = backups[backupIndex];
            Console.WriteLine($"Restoring from backup: {selectedBackup.Version} ({selectedBackup.BackupDate:yyyy-MM-dd HH:mm})");

            // Create restore path
            var restorePath = Path.Combine(Path.GetTempPath(), "aerodriver_restore", $"{driver.Id}_{selectedBackup.Version}");
            
            // Restore from backup
            var success = await _backupManager.RestoreFromBackupAsync(selectedBackup, restorePath);
            
            if (success)
            {
                // Record in history
                var historyId = await _historyManager.StartInstallationAsync(
                    driverId, 
                    driver.Version, 
                    selectedBackup.Version, 
                    selectedBackup.Id
                );

                // Simulate installation
                await Task.Delay(1000);
                await _historyManager.UpdateInstallationStatusAsync(historyId, InstallationStatus.Success);

                // Update driver information
                var restoredDriver = driver with 
                { 
                    Version = selectedBackup.Version,
                    LastUpdated = DateTime.UtcNow,
                    Status = DriverStatus.Good
                };
                await _driverRepository.UpdateDriverAsync(restoredDriver);

                Console.WriteLine($"✓ Successfully restored {driver.Name} to version {selectedBackup.Version}");
            }
            else
            {
                Console.WriteLine($"✗ Failed to restore driver from backup");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error restoring driver {driverId}");
            Console.WriteLine($"✗ Error during restoration: {ex.Message}");
        }
    }

    public async Task ShowBackupsAsync(string driverId)
    {
        Console.WriteLine($"\n=== Backups for Driver {driverId} ===");
        
        var backups = await _backupManager.GetBackupsForDriverAsync(driverId);
        
        if (!backups.Any())
        {
            Console.WriteLine("No backups found for this driver.");
            return;
        }

        for (int i = 0; i < backups.Count; i++)
        {
            var backup = backups[i];
            var compressionRatio = backup.OriginalSize > 0 
                ? (1.0 - (double)backup.CompressedSize / backup.OriginalSize) * 100 
                : 0;

            Console.WriteLine($"[{i}] Version: {backup.Version}");
            Console.WriteLine($"    Date: {backup.BackupDate:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"    Type: {backup.Type}");
            Console.WriteLine($"    Size: {backup.CompressedSize / 1024.0:F1} KB ({compressionRatio:F1}% compression)");
            Console.WriteLine($"    Checksum: {backup.Checksum[..8]}...");
            Console.WriteLine();
        }
    }

    public async Task CreateManualBackupAsync(string driverId)
    {
        try
        {
            var driver = await _driverRepository.GetDriverByIdAsync(driverId);
            if (driver == null)
            {
                Console.WriteLine($"Driver {driverId} not found.");
                return;
            }

            Console.WriteLine($"Creating manual backup for {driver.Name}...");
            
            var backup = await _backupManager.CreateBackupAsync(driver, BackupType.Manual);
            
            Console.WriteLine($"✓ Manual backup created: {backup.Id}");
            Console.WriteLine($"  Version: {backup.Version}");
            Console.WriteLine($"  Size: {backup.CompressedSize / 1024.0:F1} KB");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating manual backup for {driverId}");
            Console.WriteLine($"✗ Error creating backup: {ex.Message}");
        }
    }

    public async Task ForceUpdateAsync(string driverId)
    {
        try
        {
            var driver = await _driverRepository.GetDriverByIdAsync(driverId);
            if (driver == null)
            {
                Console.WriteLine($"Driver {driverId} not found.");
                return;
            }

            Console.WriteLine($"Starting manual update for {driver.Name}...");
            var success = await _autoManagerService.UpdateDriverAutomaticallyAsync(driver);
            
            if (success)
            {
                Console.WriteLine($"✓ Successfully updated {driver.Name}");
            }
            else
            {
                Console.WriteLine($"✗ Failed to update {driver.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error forcing update for {driverId}");
            Console.WriteLine($"✗ Error during update: {ex.Message}");
        }
    }

    public async Task ShowDriverStatusAsync()
    {
        Console.WriteLine("\n=== Driver Status ===");
        
        var drivers = await _driverRepository.GetAllDriversAsync();
        
        foreach (var driver in drivers)
        {
            var statusIcon = driver.Status switch
            {
                DriverStatus.Good => "✓",
                DriverStatus.Warning => "⚠",
                DriverStatus.Critical => "✗",
                DriverStatus.Installing => "⋯",
                DriverStatus.Failed => "✗",
                DriverStatus.Rollback => "↶",
                _ => "?"
            };

            var daysSinceUpdate = (DateTime.UtcNow - driver.LastUpdated).Days;
            
            Console.WriteLine($"{statusIcon} {driver.Name}");
            Console.WriteLine($"   ID: {driver.Id}");
            Console.WriteLine($"   Status: {driver.Status}");
            Console.WriteLine($"   Version: {driver.Version}");
            Console.WriteLine($"   Vendor: {driver.Vendor}");
            Console.WriteLine($"   Last Updated: {driver.LastUpdated:yyyy-MM-dd} ({daysSinceUpdate} days ago)");
            Console.WriteLine();
        }
    }
}

// ===== Main Program =====
public class Program
{
    public static async Task Main(string[] args)
    {
        // Setup configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Setup dependency injection
        var services = new ServiceCollection();
        
        services.AddSingleton<IConfiguration>(configuration);
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        // Core services
        services.AddSingleton<IDriverRepository, DriverRepository>();
        services.AddSingleton<DriverBackupManager>();
        services.AddSingleton<InstallationHistoryManager>();
        services.AddSingleton<AutoRecoveryService>();
        services.AddSingleton<AutoDriverManagerService>();
        services.AddSingleton<DriverManagerCommands>();
        
        // Background service
        services.AddHostedService<AutoDriverManagerService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        Console.WriteLine("=== AeroDriver Version 1.0 ===");
        Console.WriteLine("Automatic Driver Management System\n");
        
        // Get services
        var commands = serviceProvider.GetRequiredService<DriverManagerCommands>();
        
        // Initial status display
        await commands.ShowDriverStatusAsync();
        
        // Interactive command loop
        Console.WriteLine("\n=== Available Commands ===");
        Console.WriteLine("  status                    - Show all drivers status");
        Console.WriteLine("  history [driverId]        - Show installation history");
        Console.WriteLine("  backups <driverId>        - Show backups for driver");
        Console.WriteLine("  restore <driverId> [index] - Restore from backup");
        Console.WriteLine("  backup <driverId>         - Create manual backup");
        Console.WriteLine("  update <driverId>         - Force driver update");
        Console.WriteLine("  exit                      - Exit program");
        
        while (true)
        {
            Console.Write("\nAeroDriver> ");
            var input = Console.ReadLine()?.Trim().ToLower();
            
            if (string.IsNullOrEmpty(input)) continue;
            
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0];
            
            try
            {
                switch (command)
                {
                    case "status":
                        await commands.ShowDriverStatusAsync();
                        break;
                        
                    case "history":
                        var driverId = parts.Length > 1 ? parts[1] : null;
                        await commands.ShowInstallationHistoryAsync(driverId);
                        break;
                        
                    case "backups":
                        if (parts.Length > 1)
                        {
                            await commands.ShowBackupsAsync(parts[1]);
                        }
                        else
                        {
                            Console.WriteLine("Usage: backups <driverId>");
                        }
                        break;
                        
                    case "restore":
                        if (parts.Length > 1)
                        {
                            var backupIndex = parts.Length > 2 && int.TryParse(parts[2], out var index) ? index : 0;
                            await commands.RestoreFromBackupAsync(parts[1], backupIndex);
                        }
                        else
                        {
                            Console.WriteLine("Usage: restore <driverId> [backupIndex]");
                        }
                        break;
                        
                    case "backup":
                        if (parts.Length > 1)
                        {
                            await commands.CreateManualBackupAsync(parts[1]);
                        }
                        else
                        {
                            Console.WriteLine("Usage: backup <driverId>");
                        }
                        break;
                        
                    case "update":
                        if (parts.Length > 1)
                        {
                            await commands.ForceUpdateAsync(parts[1]);
                        }
                        else
                        {
                            Console.WriteLine("Usage: update <driverId>");
                        }
                        break;
                        
                    case "exit":
                        Console.WriteLine("Goodbye!");
                        return;
                        
                    default:
                        Console.WriteLine("Unknown command. Type 'exit' to quit.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                logger.LogError(ex, "Command execution error");
            }
        }
    }
}