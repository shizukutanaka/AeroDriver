using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Logging;

/// <summary>
/// Enterprise-grade structured logging system for national-level deployment
/// Implements async logging, log rotation, and multiple output targets
/// </summary>
public class EnterpriseLogger : ISimpleLogger, IDisposable
{
    private readonly string _logDirectory;
    private readonly LogLevel _minLogLevel;
    private readonly BlockingCollection<LogEntry> _logQueue;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _loggingTask;
    private readonly object _fileLock = new();
    private readonly long _maxLogFileSizeBytes;
    private readonly int _maxLogFiles;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public EnterpriseLogger(
        string? logDirectory = null,
        LogLevel minLogLevel = LogLevel.Information,
        long maxLogFileSizeBytes = 10 * 1024 * 1024, // 10 MB
        int maxLogFiles = 10)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AeroDriver",
            "Logs");

        _minLogLevel = minLogLevel;
        _maxLogFileSizeBytes = maxLogFileSizeBytes;
        _maxLogFiles = maxLogFiles;

        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        catch (Exception ex)
        {
            // Fallback to temp directory if can't create in app data
            _logDirectory = Path.Combine(Path.GetTempPath(), "AeroDriver", "Logs");
            Directory.CreateDirectory(_logDirectory);
            Console.Error.WriteLine($"Warning: Using temp directory for logs: {ex.Message}");
        }

        _logQueue = new BlockingCollection<LogEntry>(1000);
        _cancellationTokenSource = new CancellationTokenSource();
        _loggingTask = Task.Run(() => ProcessLogQueue(_cancellationTokenSource.Token));
    }

    public void LogInformation(string message, object? data = null)
    {
        Log(LogLevel.Information, message, data, null);
    }

    public void LogWarning(string message, object? data = null)
    {
        Log(LogLevel.Warning, message, data, null);
    }

    public void LogError(string message, object? data = null, Exception? exception = null)
    {
        Log(LogLevel.Error, message, data, exception);
    }

    public void LogDebug(string message, object? data = null)
    {
        Log(LogLevel.Debug, message, data, null);
    }

    public Task InfoAsync(string message, string? context = null, CancellationToken cancellationToken = default)
    {
        LogInformation(message, context);
        return Task.CompletedTask;
    }

    public Task WarningAsync(string message, string? context = null, CancellationToken cancellationToken = default)
    {
        LogWarning(message, context);
        return Task.CompletedTask;
    }

    public Task ErrorAsync(string message, string? context = null, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        LogError(message, context, exception);
        return Task.CompletedTask;
    }

    public Task DebugAsync(string message, string? context = null, CancellationToken cancellationToken = default)
    {
        LogDebug(message, context);
        return Task.CompletedTask;
    }

    public Task LogSecurityEventAsync(string eventType, string message, CancellationToken cancellationToken = default)
    {
        return LogSecurityEventAsync(eventType, message, null);
    }

    private Task LogSecurityEventAsync(string eventType, string message, object? data = null)
    {
        var enrichedData = new Dictionary<string, object?>
        {
            ["eventType"] = eventType,
            ["category"] = "Security",
            ["timestamp"] = DateTime.UtcNow
        };

        if (data != null)
        {
            enrichedData["details"] = data;
        }

        Log(LogLevel.Security, message, enrichedData, null);
        return Task.CompletedTask;
    }

    public Task LogAuditEventAsync(string action, string user, object? details = null)
    {
        var auditData = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["user"] = user,
            ["timestamp"] = DateTime.UtcNow,
            ["machineName"] = Environment.MachineName,
            ["processId"] = Environment.ProcessId,
            ["details"] = details
        };

        Log(LogLevel.Audit, $"AUDIT: {action}", auditData, null);
        return Task.CompletedTask;
    }

    public Task LogPerformanceMetricAsync(string metricName, double value, Dictionary<string, object>? tags = null)
    {
        var metricData = new Dictionary<string, object?>
        {
            ["metricName"] = metricName,
            ["value"] = value,
            ["unit"] = "ms",
            ["timestamp"] = DateTime.UtcNow,
            ["tags"] = tags
        };

        Log(LogLevel.Metrics, $"METRIC: {metricName} = {value}", metricData, null);
        return Task.CompletedTask;
    }

    private void Log(LogLevel level, string message, object? data, Exception? exception)
    {
        if (level < _minLogLevel)
        {
            return;
        }

        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message,
            Data = data,
            Exception = exception,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Environment.ProcessId,
            MachineName = Environment.MachineName
        };

        try
        {
            if (!_logQueue.TryAdd(entry, 100))
            {
                // Queue is full, log to console as fallback
                Console.Error.WriteLine($"[{DateTime.UtcNow:O}] Log queue full: {message}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{DateTime.UtcNow:O}] Logging error: {ex.Message}");
        }
    }

    private void ProcessLogQueue(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_logQueue.TryTake(out var entry, 100, cancellationToken))
                {
                    WriteLogEntry(entry);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:O}] Log processing error: {ex.Message}");
            }
        }

        // Process remaining entries
        while (_logQueue.TryTake(out var entry))
        {
            try
            {
                WriteLogEntry(entry);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:O}] Final log processing error: {ex.Message}");
            }
        }
    }

    private void WriteLogEntry(LogEntry entry)
    {
        var logFile = GetCurrentLogFile();

        // Check if rotation needed
        lock (_fileLock)
        {
            if (File.Exists(logFile))
            {
                var fileInfo = new FileInfo(logFile);
                if (fileInfo.Length > _maxLogFileSizeBytes)
                {
                    RotateLogFiles();
                    logFile = GetCurrentLogFile();
                }
            }

            // Write to file
            try
            {
                var logLine = FormatLogEntry(entry);
                File.AppendAllText(logFile, logLine + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:O}] File write error: {ex.Message}");
            }
        }

        // Also write to console for important levels
        if (entry.Level >= LogLevel.Warning || entry.Level == LogLevel.Security || entry.Level == LogLevel.Audit)
        {
            var consoleMessage = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}";
            if (entry.Exception != null)
            {
                consoleMessage += $"\n  Exception: {entry.Exception.Message}";
            }

            if (entry.Level >= LogLevel.Error)
            {
                Console.Error.WriteLine(consoleMessage);
            }
            else
            {
                Console.WriteLine(consoleMessage);
            }
        }
    }

    private string FormatLogEntry(LogEntry entry)
    {
        var logObject = new Dictionary<string, object?>
        {
            ["@timestamp"] = entry.Timestamp.ToString("O"),
            ["level"] = entry.Level.ToString(),
            ["message"] = entry.Message,
            ["processId"] = entry.ProcessId,
            ["threadId"] = entry.ThreadId,
            ["machineName"] = entry.MachineName
        };

        if (entry.Data != null)
        {
            logObject["data"] = entry.Data;
        }

        if (entry.Exception != null)
        {
            logObject["exception"] = new
            {
                type = entry.Exception.GetType().FullName,
                message = entry.Exception.Message,
                stackTrace = entry.Exception.StackTrace,
                innerException = entry.Exception.InnerException?.Message
            };
        }

        return JsonSerializer.Serialize(logObject, JsonOptions);
    }

    private string GetCurrentLogFile()
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        return Path.Combine(_logDirectory, $"aerodriver_{date}.log");
    }

    private void RotateLogFiles()
    {
        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "aerodriver_*.log");
            Array.Sort(logFiles);

            // Delete old files if exceeding max count
            if (logFiles.Length >= _maxLogFiles)
            {
                var filesToDelete = logFiles.Length - _maxLogFiles + 1;
                for (int i = 0; i < filesToDelete && i < logFiles.Length; i++)
                {
                    try
                    {
                        File.Delete(logFiles[i]);
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                }
            }

            // Rename current file with timestamp
            var currentFile = GetCurrentLogFile();
            if (File.Exists(currentFile))
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var newName = Path.Combine(_logDirectory, $"aerodriver_{timestamp}.log");
                File.Move(currentFile, newName, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Log rotation error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _cancellationTokenSource.Cancel();
            _loggingTask.Wait(TimeSpan.FromSeconds(5));
            _logQueue.Dispose();
            _cancellationTokenSource.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Logger disposal error: {ex.Message}");
        }
    }
}

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    Security = 6,
    Audit = 7,
    Metrics = 8
}

internal class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public required string Message { get; set; }
    public object? Data { get; set; }
    public Exception? Exception { get; set; }
    public int ThreadId { get; set; }
    public int ProcessId { get; set; }
    public required string MachineName { get; set; }
}

/// <summary>
/// Performance metrics collector for monitoring system health
/// </summary>
public class PerformanceMetricsCollector
{
    private readonly EnterpriseLogger _logger;
    private readonly ConcurrentDictionary<string, PerformanceCounter> _counters = new();

    public PerformanceMetricsCollector(EnterpriseLogger logger)
    {
        _logger = logger;
    }

    public async Task RecordOperationDurationAsync(string operationName, TimeSpan duration, Dictionary<string, object>? tags = null)
    {
        await _logger.LogPerformanceMetricAsync(
            $"{operationName}.duration",
            duration.TotalMilliseconds,
            tags);
    }

    public async Task RecordCounterAsync(string counterName, long value, Dictionary<string, object>? tags = null)
    {
        await _logger.LogPerformanceMetricAsync(
            counterName,
            value,
            tags);
    }

    public async Task<T> MeasureAsync<T>(string operationName, Func<Task<T>> operation, Dictionary<string, object>? tags = null)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await operation();
            stopwatch.Stop();

            var successTags = new Dictionary<string, object>(tags ?? new Dictionary<string, object>())
            {
                ["success"] = true
            };

            await RecordOperationDurationAsync(operationName, stopwatch.Elapsed, successTags);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            var failureTags = new Dictionary<string, object>(tags ?? new Dictionary<string, object>())
            {
                ["success"] = false,
                ["errorType"] = ex.GetType().Name
            };

            await RecordOperationDurationAsync(operationName, stopwatch.Elapsed, failureTags);
            throw;
        }
    }
}

internal class PerformanceCounter
{
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
    public double Total { get; set; }
    public double Min { get; set; } = double.MaxValue;
    public double Max { get; set; } = double.MinValue;
    public DateTime LastUpdated { get; set; }

    public void Record(double value)
    {
        Count++;
        Total += value;
        Min = Math.Min(Min, value);
        Max = Math.Max(Max, value);
        LastUpdated = DateTime.UtcNow;
    }

    public double Average => Count > 0 ? Total / Count : 0;
}
