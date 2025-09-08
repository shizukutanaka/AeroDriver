using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    public class CleanupService : ICleanupService
    {
        private readonly IBackupService _backupService;
        private readonly ICacheService? _cacheService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<CleanupService>? _logger;

        public CleanupService(IBackupService backupService, ISettingsService settingsService, 
            ICacheService? cacheService = null, ILogger<CleanupService>? logger = null)
        {
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<CleanupResult> CleanupOldBackupsAsync()
        {
            var result = new CleanupResult { OperationType = "Backup Cleanup" };
            
            try
            {
                _logger?.LogInformation("Starting backup cleanup");
                var backups = await _backupService.GetBackupsAsync();
                
                // Group by device ID and keep only the most recent N backups per device
                var maxGenerations = _settingsService.MaxBackupGenerations;
                var toDelete = new List<string>();
                
                var groupedBackups = backups.GroupBy(b => b.DeviceId);
                foreach (var group in groupedBackups)
                {
                    var sortedBackups = group.OrderByDescending(b => b.BackupDate).ToList();
                    if (sortedBackups.Count > maxGenerations)
                    {
                        var oldBackups = sortedBackups.Skip(maxGenerations);
                        toDelete.AddRange(oldBackups.Select(b => b.BackupPath));
                    }
                }

                // Delete old backup directories
                foreach (var backupPath in toDelete)
                {
                    try
                    {
                        if (Directory.Exists(backupPath))
                        {
                            var sizeBeforeDelete = await GetDirectorySizeAsync(backupPath);
                            Directory.Delete(backupPath, true);
                            result.FilesDeleted++;
                            result.BytesFreed += sizeBeforeDelete;
                            _logger?.LogDebug("Deleted old backup: {BackupPath}", backupPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Failed to delete backup {backupPath}: {ex.Message}");
                        _logger?.LogWarning(ex, "Failed to delete backup {BackupPath}", backupPath);
                    }
                }

                result.Success = true;
                _logger?.LogInformation("Backup cleanup completed: {FilesDeleted} files deleted, {BytesFreed} bytes freed", 
                    result.FilesDeleted, result.BytesFreed);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Backup cleanup failed: {ex.Message}");
                _logger?.LogError(ex, "Backup cleanup failed");
            }

            return result;
        }

        public async Task<CleanupResult> CleanupTemporaryFilesAsync()
        {
            var result = new CleanupResult { OperationType = "Temporary Files Cleanup" };

            try
            {
                _logger?.LogInformation("Starting temporary files cleanup");
                var tempPaths = new[]
                {
                    Path.GetTempPath(),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AeroDriver", "temp")
                };

                foreach (var tempPath in tempPaths)
                {
                    if (!Directory.Exists(tempPath))
                        continue;

                    var tempFiles = Directory.GetFiles(tempPath, "aerodriver_*", SearchOption.TopDirectoryOnly);
                    var cutoffDate = DateTime.Now.AddDays(-7); // Delete files older than 7 days

                    foreach (var file in tempFiles)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            if (fileInfo.CreationTime < cutoffDate)
                            {
                                result.BytesFreed += fileInfo.Length;
                                fileInfo.Delete();
                                result.FilesDeleted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Failed to delete temp file {file}: {ex.Message}");
                        }
                    }
                }

                result.Success = true;
                _logger?.LogInformation("Temporary files cleanup completed: {FilesDeleted} files deleted, {BytesFreed} bytes freed", 
                    result.FilesDeleted, result.BytesFreed);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Temporary files cleanup failed: {ex.Message}");
                _logger?.LogError(ex, "Temporary files cleanup failed");
            }

            return result;
        }

        public async Task<CleanupResult> CleanupCacheAsync()
        {
            var result = new CleanupResult { OperationType = "Cache Cleanup" };

            try
            {
                _logger?.LogInformation("Starting cache cleanup");
                
                if (_cacheService != null)
                {
                    _cacheService.Clear();
                    result.FilesDeleted = 1; // Represent cache clear as 1 operation
                    _logger?.LogDebug("In-memory cache cleared");
                }

                // Clean up any file-based caches
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var cachePath = Path.Combine(appDataPath, "AeroDriver", "cache");
                
                if (Directory.Exists(cachePath))
                {
                    var cacheSize = await GetDirectorySizeAsync(cachePath);
                    Directory.Delete(cachePath, true);
                    result.BytesFreed = cacheSize;
                    result.FilesDeleted++;
                }

                result.Success = true;
                _logger?.LogInformation("Cache cleanup completed: {BytesFreed} bytes freed", result.BytesFreed);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Cache cleanup failed: {ex.Message}");
                _logger?.LogError(ex, "Cache cleanup failed");
            }

            return result;
        }

        public async Task<long> GetDirectorySizeAsync(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return await Task.FromResult(0L).ConfigureAwait(false);

                var dirInfo = new DirectoryInfo(path);
                var size = dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
                return await Task.FromResult(size).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error calculating directory size for {Path}", path);
                return await Task.FromResult(0L).ConfigureAwait(false);
            }
        }

        public async Task<CleanupResult> PerformFullCleanupAsync()
        {
            _logger?.LogInformation("Starting full cleanup operation");
            
            var overallResult = new CleanupResult { OperationType = "Full Cleanup" };

            try
            {
                var tasks = new[]
                {
                    CleanupOldBackupsAsync(),
                    CleanupTemporaryFilesAsync(),
                    CleanupCacheAsync()
                };

                var results = await Task.WhenAll(tasks);

                foreach (var result in results)
                {
                    overallResult.FilesDeleted += result.FilesDeleted;
                    overallResult.BytesFreed += result.BytesFreed;
                    overallResult.Errors.AddRange(result.Errors);
                }

                overallResult.Success = results.All(r => r.Success);

                _logger?.LogInformation("Full cleanup completed: {FilesDeleted} files deleted, {BytesFreed} bytes freed, {ErrorCount} errors", 
                    overallResult.FilesDeleted, overallResult.BytesFreed, overallResult.Errors.Count);
            }
            catch (Exception ex)
            {
                overallResult.Errors.Add($"Full cleanup failed: {ex.Message}");
                _logger?.LogError(ex, "Full cleanup failed");
            }

            return overallResult;
        }
    }
}