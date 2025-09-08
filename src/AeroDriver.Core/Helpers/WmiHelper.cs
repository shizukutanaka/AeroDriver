using System.Collections.Concurrent;
using System.Management;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Helpers
{
    public static class WmiHelper
    {
        private static readonly ConcurrentDictionary<string, CachedSearcher> _searcherCache = new();
        private static readonly SemaphoreSlim _semaphore = new(Environment.ProcessorCount, Environment.ProcessorCount);
        private static readonly object _cleanupLock = new();
        private static DateTime _lastCleanup = DateTime.UtcNow;
        private static volatile bool _isWmiHealthy = true;
        
        private const int DefaultTimeoutMs = 30000;
        private const int MaxRetryAttempts = 3;
        private const int CacheExpirationMinutes = 5;
        private const int HealthCheckIntervalMinutes = 1;
        private const int MaxCachedSearchers = 50;
        
        private sealed class CachedSearcher
        {
            public ManagementObjectSearcher Searcher { get; }
            public DateTime CreatedAt { get; }
            public DateTime LastUsed { get; set; }
            public int UseCount { get; set; }
            
            public CachedSearcher(ManagementObjectSearcher searcher)
            {
                Searcher = searcher;
                CreatedAt = DateTime.UtcNow;
                LastUsed = DateTime.UtcNow;
                UseCount = 1;
            }
            
            public bool IsExpired => DateTime.UtcNow - LastUsed > TimeSpan.FromMinutes(CacheExpirationMinutes);
        }

        public static async Task<List<DriverInfo>> GetDriversAsync(ILogger? logger = null, int timeoutMs = DefaultTimeoutMs)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    PerformMaintenanceIfNeeded();
                    return await Task.Run(() => GetDriversInternal(logger), 
                        new CancellationTokenSource(timeoutMs).Token);
                }
                finally
                {
                    _semaphore.Release();
                }
            }, logger, "GetDriversAsync");
        }

        public static async Task<Dictionary<string, string>> GetSystemInfoAsync(ILogger? logger = null, int timeoutMs = DefaultTimeoutMs)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    PerformMaintenanceIfNeeded();
                    return await Task.Run(() => GetSystemInfoInternal(logger), 
                        new CancellationTokenSource(timeoutMs).Token);
                }
                finally
                {
                    _semaphore.Release();
                }
            }, logger, "GetSystemInfoAsync");
        }

        public static async Task<bool> CheckWmiHealthAsync(ILogger? logger = null)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                using var collection = searcher.Get();
                
                var hasResults = false;
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        hasResults = true;
                        break;
                    }
                }
                
                _isWmiHealthy = hasResults;
                logger?.LogTrace("WMI health check: {Status}", _isWmiHealthy ? "Healthy" : "Unhealthy");
                return _isWmiHealthy;
            }
            catch (Exception ex)
            {
                _isWmiHealthy = false;
                logger?.LogWarning(ex, "WMI health check failed");
                return false;
            }
        }

        private static List<DriverInfo> GetDriversInternal(ILogger? logger)
        {
            var drivers = new List<DriverInfo>();

            try
            {
                using var searcher = GetOrCreateSearcher("SELECT * FROM Win32_PnPEntity WHERE ClassGuid IS NOT NULL");
                using var collection = searcher.Get();

                var processedCount = 0;
                foreach (ManagementObject device in collection)
                {
                    try
                    {
                        using (device)
                        {
                            var driverInfo = CreateDriverInfoFromDevice(device);
                            if (driverInfo != null)
                            {
                                drivers.Add(driverInfo);
                                processedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error processing device");
                    }
                }

                logger?.LogInformation("Processed {ProcessedCount} devices, found {DriverCount} drivers", 
                    processedCount, drivers.Count);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error enumerating drivers");
            }

            return drivers.OrderBy(d => d.DeviceClass).ThenBy(d => d.DeviceName).ToList();
        }

        private static Dictionary<string, string> GetSystemInfoInternal(ILogger? logger)
        {
            var info = new Dictionary<string, string>();

            try
            {
                // Computer System Info
                using (var searcher = GetOrCreateSearcher("SELECT * FROM Win32_ComputerSystem"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject system in collection)
                    {
                        using (system)
                        {
                            SafeAddProperty(info, "Computer Name", system, "Name");
                            SafeAddProperty(info, "Manufacturer", system, "Manufacturer");
                            SafeAddProperty(info, "Model", system, "Model");
                            
                            var totalMemory = system.Properties["TotalPhysicalMemory"]?.Value;
                            if (totalMemory != null)
                            {
                                var memoryGB = Convert.ToDouble(totalMemory) / (1024 * 1024 * 1024);
                                info["Total RAM"] = $"{memoryGB:F1} GB";
                            }
                        }
                    }
                }

                // Operating System Info
                using (var searcher = GetOrCreateSearcher("SELECT * FROM Win32_OperatingSystem"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject os in collection)
                    {
                        using (os)
                        {
                            SafeAddProperty(info, "Operating System", os, "Caption");
                            SafeAddProperty(info, "Version", os, "Version");
                            SafeAddProperty(info, "Architecture", os, "OSArchitecture");
                            SafeAddProperty(info, "Build Number", os, "BuildNumber");
                        }
                    }
                }

                // Processor Info
                using (var searcher = GetOrCreateSearcher("SELECT * FROM Win32_Processor"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject processor in collection)
                    {
                        using (processor)
                        {
                            SafeAddProperty(info, "Processor", processor, "Name");
                            SafeAddProperty(info, "Cores", processor, "NumberOfCores");
                            SafeAddProperty(info, "Threads", processor, "NumberOfLogicalProcessors");
                            break; // First processor only
                        }
                    }
                }

                logger?.LogInformation("Retrieved system information with {Count} properties", info.Count);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error retrieving system information");
            }

            return info;
        }

        private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, ILogger? logger, string operationName)
        {
            var attempt = 0;
            var delays = new[] { 100, 500, 1000 }; // Progressive delay
            
            while (attempt < MaxRetryAttempts)
            {
                try
                {
                    if (!_isWmiHealthy)
                    {
                        logger?.LogWarning("WMI is marked as unhealthy, attempting health check");
                        await CheckWmiHealthAsync(logger);
                        if (!_isWmiHealthy)
                        {
                            throw new InvalidOperationException("WMI service is not available");
                        }
                    }
                    
                    return await operation();
                }
                catch (Exception ex) when (attempt < MaxRetryAttempts - 1)
                {
                    attempt++;
                    var delay = delays[Math.Min(attempt - 1, delays.Length - 1)];
                    
                    logger?.LogWarning(ex, "Operation {OperationName} failed (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}ms",
                        operationName, attempt, MaxRetryAttempts, delay);
                        
                    await Task.Delay(delay);
                    
                    // Reset WMI health status to trigger health check on next attempt
                    if (IsWmiException(ex))
                    {
                        _isWmiHealthy = false;
                    }
                }
            }
            
            // Final attempt - let the exception bubble up
            return await operation();
        }

        private static void PerformMaintenanceIfNeeded()
        {
            var now = DateTime.UtcNow;
            if (now - _lastCleanup < TimeSpan.FromMinutes(HealthCheckIntervalMinutes))
                return;
                
            lock (_cleanupLock)
            {
                // Double-check pattern
                if (now - _lastCleanup < TimeSpan.FromMinutes(HealthCheckIntervalMinutes))
                    return;
                    
                try
                {
                    CleanupExpiredSearchers();
                    _lastCleanup = now;
                }
                catch (Exception)
                {
                    // Ignore cleanup errors to avoid breaking main operations
                }
            }
        }

        private static void CleanupExpiredSearchers()
        {
            var toRemove = new List<string>();
            var cacheSize = _searcherCache.Count;
            
            foreach (var kvp in _searcherCache)
            {
                if (kvp.Value.IsExpired)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            // If we're over the cache limit, remove oldest entries
            if (cacheSize > MaxCachedSearchers)
            {
                var oldestEntries = _searcherCache
                    .OrderBy(kvp => kvp.Value.LastUsed)
                    .Take(cacheSize - MaxCachedSearchers)
                    .Select(kvp => kvp.Key)
                    .ToList();
                    
                toRemove.AddRange(oldestEntries);
            }
            
            foreach (var key in toRemove.Distinct())
            {
                if (_searcherCache.TryRemove(key, out var cachedSearcher))
                {
                    try
                    {
                        cachedSearcher.Searcher.Dispose();
                    }
                    catch (Exception)
                    {
                        // Ignore disposal errors
                    }
                }
            }
        }

        private static bool IsWmiException(Exception ex)
        {
            return ex is ManagementException ||
                   ex is UnauthorizedAccessException ||
                   ex is TimeoutException ||
                   ex.Message.Contains("WMI", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("RPC", StringComparison.OrdinalIgnoreCase);
        }

        private static ManagementObjectSearcher GetOrCreateSearcher(string query)
        {
            var cachedSearcher = _searcherCache.GetOrAdd(query, q => new CachedSearcher(CreateSearcher(q)));
            
            cachedSearcher.LastUsed = DateTime.UtcNow;
            cachedSearcher.UseCount++;
            
            return cachedSearcher.Searcher;
        }

        private static ManagementObjectSearcher CreateSearcher(string query)
        {
            var searcher = new ManagementObjectSearcher(query);
            
            // Configure timeout options
            var options = new EnumerationOptions
            {
                Timeout = TimeSpan.FromMilliseconds(DefaultTimeoutMs),
                ReturnImmediately = true,
                Rewindable = false
            };
            
            searcher.Options = options;
            return searcher;
        }

        private static DriverInfo? CreateDriverInfoFromDevice(ManagementObject device)
        {
            try
            {
                var deviceId = device.Properties["DeviceID"]?.Value?.ToString();
                var name = device.Properties["Name"]?.Value?.ToString();
                
                if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(name))
                    return null;

                // Filter out software-only devices
                if (IsSystemDevice(deviceId) || IsSoftwareDevice(name))
                    return null;

                var status = device.Properties["Status"]?.Value?.ToString() ?? "Unknown";
                var configManagerErrorCode = device.Properties["ConfigManagerErrorCode"]?.Value;
                
                // Improve status determination
                if (configManagerErrorCode != null && Convert.ToInt32(configManagerErrorCode) != 0)
                {
                    status = "Problem";
                }

                return new DriverInfo
                {
                    DeviceID = deviceId,
                    DeviceName = CleanDeviceName(name),
                    DriverVersion = GetDriverVersion(device),
                    DriverProviderName = device.Properties["Manufacturer"]?.Value?.ToString() ?? "Unknown",
                    DeviceClass = GetDeviceClass(device),
                    Status = status,
                    IsWHQLCertified = true // Assume WHQL for system-installed drivers
                };
            }
            catch
            {
                return null;
            }
        }

        private static string GetDriverVersion(ManagementObject device)
        {
            var driverVersion = device.Properties["DriverVersion"]?.Value?.ToString();
            if (!string.IsNullOrEmpty(driverVersion))
                return driverVersion;

            var driverDate = device.Properties["DriverDate"]?.Value?.ToString();
            if (!string.IsNullOrEmpty(driverDate) && driverDate.Length >= 8)
            {
                return $"Date: {driverDate[0..8]}";
            }

            return "Unknown";
        }

        private static string GetDeviceClass(ManagementObject device)
        {
            var pnpClass = device.Properties["PNPClass"]?.Value?.ToString();
            if (!string.IsNullOrEmpty(pnpClass))
                return pnpClass;

            var classGuid = device.Properties["ClassGuid"]?.Value?.ToString();
            return MapClassGuidToFriendlyName(classGuid) ?? "Unknown";
        }

        private static string? MapClassGuidToFriendlyName(string? classGuid)
        {
            if (string.IsNullOrEmpty(classGuid))
                return null;

            return classGuid.ToUpperInvariant() switch
            {
                "{4D36E972-E325-11CE-BFC1-08002BE10318}" => "Network Adapter",
                "{4D36E968-E325-11CE-BFC1-08002BE10318}" => "Display",
                "{4D36E96C-E325-11CE-BFC1-08002BE10318}" => "Sound",
                "{4D36E97D-E325-11CE-BFC1-08002BE10318}" => "System",
                "{4D36E978-E325-11CE-BFC1-08002BE10318}" => "Ports",
                _ => null
            };
        }

        private static string CleanDeviceName(string name)
        {
            return name
                .Replace("Microsoft ", "")
                .Replace("Standard ", "")
                .Trim();
        }

        private static bool IsSystemDevice(string deviceId)
        {
            var systemPrefixes = new[] { "ROOT\\", "SW\\", "ACPI_HAL\\", "HTREE\\", "UMB\\" };
            return systemPrefixes.Any(prefix => deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSoftwareDevice(string name)
        {
            var softwareKeywords = new[] { "Software", "Generic", "Composite", "Hub", "Controller", "Interface" };
            return softwareKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static void SafeAddProperty(Dictionary<string, string> dictionary, string key, ManagementObject obj, string propertyName)
        {
            try
            {
                var value = obj.Properties[propertyName]?.Value?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    dictionary[key] = value.Trim();
                }
            }
            catch
            {
                // Ignore property access errors
            }
        }

        public static void ClearCache()
        {
            foreach (var cachedSearcher in _searcherCache.Values)
            {
                try
                {
                    cachedSearcher.Searcher.Dispose();
                }
                catch (Exception)
                {
                    // Ignore disposal errors
                }
            }
            _searcherCache.Clear();
            _lastCleanup = DateTime.UtcNow;
        }

        public static CacheStatistics GetCacheStatistics()
        {
            var statistics = new CacheStatistics();
            
            foreach (var kvp in _searcherCache)
            {
                statistics.TotalEntries++;
                statistics.TotalUseCount += kvp.Value.UseCount;
                
                if (kvp.Value.IsExpired)
                    statistics.ExpiredEntries++;
                    
                var age = DateTime.UtcNow - kvp.Value.CreatedAt;
                if (age > statistics.OldestEntryAge)
                    statistics.OldestEntryAge = age;
                    
                var lastUsed = DateTime.UtcNow - kvp.Value.LastUsed;
                if (lastUsed > statistics.LongestUnusedTime)
                    statistics.LongestUnusedTime = lastUsed;
            }
            
            statistics.HealthStatus = _isWmiHealthy;
            statistics.LastCleanup = _lastCleanup;
            
            return statistics;
        }

        public static async Task<WmiPerformanceMetrics> MeasurePerformanceAsync(string query, ILogger? logger = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var metrics = new WmiPerformanceMetrics { Query = query };
            
            try
            {
                using var searcher = new ManagementObjectSearcher(query);
                using var collection = searcher.Get();
                
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        metrics.ObjectCount++;
                    }
                }
                
                metrics.Success = true;
            }
            catch (Exception ex)
            {
                metrics.Success = false;
                metrics.ErrorMessage = ex.Message;
                logger?.LogError(ex, "Performance measurement failed for query: {Query}", query);
            }
            finally
            {
                stopwatch.Stop();
                metrics.ExecutionTime = stopwatch.Elapsed;
            }
            
            return metrics;
        }

        public sealed class CacheStatistics
        {
            public int TotalEntries { get; set; }
            public int ExpiredEntries { get; set; }
            public long TotalUseCount { get; set; }
            public TimeSpan OldestEntryAge { get; set; }
            public TimeSpan LongestUnusedTime { get; set; }
            public bool HealthStatus { get; set; }
            public DateTime LastCleanup { get; set; }
        }

        public sealed class WmiPerformanceMetrics
        {
            public string Query { get; set; } = "";
            public TimeSpan ExecutionTime { get; set; }
            public int ObjectCount { get; set; }
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}