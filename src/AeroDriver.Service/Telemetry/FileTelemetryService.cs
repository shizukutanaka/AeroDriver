using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Service;

public sealed class FileTelemetryService : ITelemetryService, IPerformanceTelemetrySink, IDisposable
{
    private readonly ILogger<FileTelemetryService> _logger;
    private readonly string _telemetryDirectory;
    private readonly string _eventLogPath;
    private readonly string _metricsLogPath;
    private readonly string _monitoringLogPath;
    private readonly ConcurrentQueue<string> _eventBuffer = new();
    private readonly ConcurrentQueue<string> _metricBuffer = new();
    private readonly ConcurrentQueue<string> _monitoringBuffer = new();
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private Timer? _flushTimer;
    private bool _isRunning;
    private int _intervalMinutes;
    private const int MaxBufferedItems = 5000;
    private const long MaxLogFileSizeBytes = 10 * 1024 * 1024; // 10MB

    public FileTelemetryService(ILogger<FileTelemetryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroDriver", "telemetry");
        Directory.CreateDirectory(baseDirectory);

        _telemetryDirectory = baseDirectory;
        _eventLogPath = Path.Combine(_telemetryDirectory, "events.log");
        _metricsLogPath = Path.Combine(_telemetryDirectory, "metrics.log");
        _monitoringLogPath = Path.Combine(_telemetryDirectory, "monitoring.log");
    }

    public void StartTelemetryCollection(int intervalMinutes)
    {
        _intervalMinutes = Math.Max(1, intervalMinutes);
        _flushTimer?.Dispose();
        _flushTimer = new Timer(async _ => await FlushAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(_intervalMinutes));
        _isRunning = true;
        _logger.LogInformation("File telemetry collection started (interval: {Interval} minutes)", _intervalMinutes);
    }

    public void RecordEvent(string name, Dictionary<string, object> properties)
    {
        if (!EnsureRunning())
        {
            return;
        }

        var payload = new Dictionary<string, object>(properties ?? new())
        {
            ["Name"] = name,
            ["Timestamp"] = DateTime.UtcNow
        };

        EnqueueWithLimit(_eventBuffer, JsonSerializer.Serialize(payload, _jsonOptions), "event");
    }

    public Task RecordEventAsync(string name, Dictionary<string, object> properties)
    {
        RecordEvent(name, properties);
        return Task.CompletedTask;
    }

    public void RecordPerformanceMetric(string name, double value)
    {
        if (!EnsureRunning())
        {
            return;
        }

        var payload = new Dictionary<string, object>
        {
            ["Name"] = name,
            ["Value"] = value,
            ["Timestamp"] = DateTime.UtcNow
        };

        EnqueueWithLimit(_metricBuffer, JsonSerializer.Serialize(payload, _jsonOptions), "metric");
    }

    public Task RecordPerformanceMetricAsync(string name, double value)
    {
        RecordPerformanceMetric(name, value);
        return Task.CompletedTask;
    }

    public async Task ClearTelemetryDataAsync()
    {
        while (_eventBuffer.TryDequeue(out _)) { }
        while (_metricBuffer.TryDequeue(out _)) { }
        while (_monitoringBuffer.TryDequeue(out _)) { }

        await _flushSemaphore.WaitAsync();
        try
        {
            TryDeleteFile(_eventLogPath);
            TryDeleteFile(_metricsLogPath);
            TryDeleteFile(_monitoringLogPath);
        }
        finally
        {
            _flushSemaphore.Release();
        }

        _logger.LogInformation("Telemetry logs cleared");
    }

    public void StopTelemetryCollection()
    {
        _isRunning = false;
        _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _flushTimer?.Dispose();
        _flushTimer = null;

        FlushAsync().GetAwaiter().GetResult();
        _logger.LogInformation("File telemetry collection stopped");
    }

    public void Dispose()
    {
        StopTelemetryCollection();
        _flushSemaphore.Dispose();
    }

    private bool EnsureRunning()
    {
        if (!_isRunning)
        {
            _logger.LogDebug("Telemetry event ignored because collection is not running");
            return false;
        }

        return true;
    }

    private void EnqueueWithLimit(ConcurrentQueue<string> queue, string entry, string entryType)
    {
        queue.Enqueue(entry);

        if (queue.Count <= MaxBufferedItems)
            return;

        var trimmed = 0;
        while (queue.Count > MaxBufferedItems && queue.TryDequeue(out _))
        {
            trimmed++;
        }

        if (trimmed > 0)
        {
            _logger.LogWarning("Telemetry {EntryType} buffer exceeded {MaxItems} items; dropped {Trimmed} entries", entryType, MaxBufferedItems, trimmed);
        }
    }

    private void EnqueueMonitoringEntry(string entry)
    {
        EnqueueWithLimit(_monitoringBuffer, entry, "monitoring");
    }

    public async Task ReportSummaryAsync(string correlationId, PerformanceMonitoringSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        cancellationToken.ThrowIfCancellationRequested();

        snapshot.CorrelationId = string.IsNullOrWhiteSpace(snapshot.CorrelationId) ? correlationId : snapshot.CorrelationId;

        var payload = new
        {
            Type = "PerformanceSummary",
            Timestamp = DateTime.UtcNow,
            CorrelationId = snapshot.CorrelationId,
            Snapshot = snapshot
        };

        EnqueueMonitoringEntry(JsonSerializer.Serialize(payload, _jsonOptions));
        await FlushMonitoringIfInactiveAsync(cancellationToken);
    }

    public async Task ReportAlertAsync(string correlationId, MonitoringAlert alert, CancellationToken cancellationToken)
    {
        if (alert == null)
            throw new ArgumentNullException(nameof(alert));

        cancellationToken.ThrowIfCancellationRequested();

        alert.CorrelationId = string.IsNullOrWhiteSpace(alert.CorrelationId) ? correlationId : alert.CorrelationId;

        var payload = new
        {
            Type = "PerformanceAlert",
            Timestamp = DateTime.UtcNow,
            CorrelationId = alert.CorrelationId,
            Alert = alert
        };

        EnqueueMonitoringEntry(JsonSerializer.Serialize(payload, _jsonOptions));
        await FlushMonitoringIfInactiveAsync(cancellationToken);
    }

    public async Task ReportFailureAsync(string correlationId, string context, Exception exception, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context))
            throw new ArgumentNullException(nameof(context));
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            Type = "PerformanceFailure",
            Timestamp = DateTime.UtcNow,
            CorrelationId = correlationId,
            Context = context,
            Error = SerializeException(exception)
        };

        EnqueueMonitoringEntry(JsonSerializer.Serialize(payload, _jsonOptions));
        await FlushMonitoringIfInactiveAsync(cancellationToken);
    }

    private async Task FlushMonitoringIfInactiveAsync(CancellationToken cancellationToken)
    {
        if (_isRunning)
        {
            if (_monitoringBuffer.Count > MaxBufferedItems / 2)
            {
                await FlushAsync();
            }
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await FlushAsync();
    }

    private static object SerializeException(Exception exception)
    {
        return new
        {
            ExceptionType = exception.GetType().FullName,
            exception.Message,
            exception.HResult
        };
    }

    private async Task FlushAsync()
    {
        if (!_isRunning && _eventBuffer.IsEmpty && _metricBuffer.IsEmpty && _monitoringBuffer.IsEmpty)
        {
            return;
        }

        await _flushSemaphore.WaitAsync();
        try
        {
            var eventsToFlush = DequeueAll(_eventBuffer);
            var metricsToFlush = DequeueAll(_metricBuffer);
            var monitoringToFlush = DequeueAll(_monitoringBuffer);

            if (eventsToFlush.Count == 0 && metricsToFlush.Count == 0)
            {
                if (monitoringToFlush.Count == 0)
                {
                    return;
                }
            }

            if (eventsToFlush.Count > 0)
            {
                await AppendLinesAsync(_eventLogPath, eventsToFlush);
                RotateIfNeeded(_eventLogPath);
            }

            if (metricsToFlush.Count > 0)
            {
                await AppendLinesAsync(_metricsLogPath, metricsToFlush);
                RotateIfNeeded(_metricsLogPath);
            }

            if (monitoringToFlush.Count > 0)
            {
                await AppendLinesAsync(_monitoringLogPath, monitoringToFlush);
                RotateIfNeeded(_monitoringLogPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush telemetry buffers");
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    private static List<string> DequeueAll(ConcurrentQueue<string> queue)
    {
        var result = new List<string>();
        while (queue.TryDequeue(out var entry))
        {
            result.Add(entry);
        }

        return result;
    }

    private static async Task AppendLinesAsync(string path, IEnumerable<string> lines)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.AppendAllLinesAsync(path, lines);
    }

    private void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length <= MaxLogFileSizeBytes)
            {
                return;
            }

            var archivePath = Path.Combine(_telemetryDirectory, $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}{Path.GetExtension(path)}");
            File.Move(path, archivePath, overwrite: false);
            _logger.LogInformation("Telemetry log rotated: {Path}", archivePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rotate telemetry file {Path}", path);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete telemetry file {Path}", path);
        }
    }
}
