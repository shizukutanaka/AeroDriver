using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AeroDriver.Service;

public sealed class StubTelemetryService : ITelemetryService
{
    private readonly ILogger<StubTelemetryService> _logger;
    private readonly ConcurrentQueue<string> _events = new();
    private readonly ConcurrentQueue<(string Metric, double Value)> _metrics = new();
    private const int MaxBufferedItems = 2048;
    private int _intervalMinutes;
    private bool _isRunning;

    public StubTelemetryService(ILogger<StubTelemetryService> logger)
    {
        _logger = logger;
    }

    public void StartTelemetryCollection(int intervalMinutes)
    {
        _intervalMinutes = intervalMinutes;
        _isRunning = true;
        _logger.LogInformation("Stub telemetry collection started (interval: {Interval} minutes)", intervalMinutes);
    }

    public void RecordEvent(string name, Dictionary<string, object> properties)
    {
        var entry = $"{DateTime.UtcNow:o} | {name} | {string.Join(",", properties.Select(kv => $"{kv.Key}={kv.Value}"))}";
        EnqueueWithLimit(_events, entry, "event");
    }

    public Task RecordEventAsync(string name, Dictionary<string, object> properties)
    {
        RecordEvent(name, properties);
        return Task.CompletedTask;
    }

    public void RecordPerformanceMetric(string name, double value)
    {
        EnqueueWithLimit(_metrics, (name, value), "metric");
    }

    public Task RecordPerformanceMetricAsync(string name, double value)
    {
        RecordPerformanceMetric(name, value);
        return Task.CompletedTask;
    }

    public Task ClearTelemetryDataAsync()
    {
        _logger.LogDebug("Clearing telemetry queues (events: {Events}, metrics: {Metrics})", _events.Count, _metrics.Count);

        while (_events.TryDequeue(out _)) { }
        while (_metrics.TryDequeue(out _)) { }
        return Task.CompletedTask;
    }

    public void StopTelemetryCollection()
    {
        _isRunning = false;
        _logger.LogInformation("Stub telemetry collection stopped (processed ~{Events} events, ~{Metrics} metrics)", _events.Count, _metrics.Count);
    }

    private void EnqueueWithLimit<T>(ConcurrentQueue<T> queue, T item, string itemDescription)
    {
        queue.Enqueue(item);

        var overflowLogged = false;
        while (queue.Count > MaxBufferedItems && queue.TryDequeue(out _))
        {
            if (!overflowLogged)
            {
                _logger.LogWarning("Stub telemetry {ItemDescription} queue exceeded {MaxItems} items; dropping oldest entries", itemDescription, MaxBufferedItems);
                overflowLogged = true;
            }
        }
    }
}
