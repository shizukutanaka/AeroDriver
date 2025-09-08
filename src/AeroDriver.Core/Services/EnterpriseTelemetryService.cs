using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Models;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// Enterprise-grade telemetry and monitoring service for operational intelligence
    /// </summary>
    public class EnterpriseTelemetryService : IDisposable
    {
        private readonly ILogger<EnterpriseTelemetryService>? _logger;
        private readonly EnterpriseConfigurationService _configService;
        private readonly Timer _metricsCollectionTimer;
        private readonly Timer _healthCheckTimer;
        private readonly ConcurrentDictionary<string, MetricSeries> _metrics = new();
        private readonly ConcurrentQueue<TelemetryEvent> _eventQueue = new();
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _memoryCounter;
        private readonly object _metricsLock = new();
        
        public event EventHandler<TelemetryEventArgs>? TelemetryAlert;
        
        // System metrics
        public SystemMetrics CurrentSystemMetrics { get; private set; } = new();
        
        // Application metrics
        public ApplicationMetrics CurrentApplicationMetrics { get; private set; } = new();

        public EnterpriseTelemetryService(
            EnterpriseConfigurationService configService,
            ILogger<EnterpriseTelemetryService>? logger = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger;

            try
            {
                // Initialize performance counters
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                
                // Initialize timers
                var collectionInterval = TimeSpan.FromSeconds(_configService.Configuration.Monitoring.MetricCollectionInterval);
                var healthInterval = TimeSpan.FromMinutes(_configService.Configuration.Monitoring.HealthCheckInterval);
                
                _metricsCollectionTimer = new Timer(CollectMetrics, null, TimeSpan.Zero, collectionInterval);
                _healthCheckTimer = new Timer(PerformHealthCheck, null, healthInterval, healthInterval);
                
                _logger?.LogInformation("Enterprise telemetry service initialized");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing telemetry service");
            }
        }

        /// <summary>
        /// Record a custom metric value
        /// </summary>
        public void RecordMetric(string name, double value, Dictionary<string, string>? tags = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            
            try
            {
                var metricPoint = new MetricPoint
                {
                    Timestamp = DateTime.UtcNow,
                    Value = value,
                    Tags = tags ?? new Dictionary<string, string>()
                };

                _metrics.AddOrUpdate(name, 
                    key => new MetricSeries(key, metricPoint),
                    (key, existing) => 
                    {
                        existing.AddPoint(metricPoint);
                        return existing;
                    });

                _logger?.LogTrace("Recorded metric: {Name} = {Value}", name, value);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error recording metric: {Name}", name);
            }
        }

        /// <summary>
        /// Record an operation with timing and outcome
        /// </summary>
        public void RecordOperation(string operationName, TimeSpan duration, bool success, 
            Dictionary<string, object>? metadata = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(operationName);
            
            try
            {
                var telemetryEvent = new TelemetryEvent
                {
                    EventType = TelemetryEventType.Operation,
                    Name = operationName,
                    Timestamp = DateTime.UtcNow,
                    Duration = duration,
                    Success = success,
                    Metadata = metadata ?? new Dictionary<string, object>(),
                    SessionId = GetSessionId()
                };

                _eventQueue.Enqueue(telemetryEvent);
                
                // Update operation metrics
                RecordMetric($"operations.{operationName}.duration_ms", duration.TotalMilliseconds);
                RecordMetric($"operations.{operationName}.success_rate", success ? 1.0 : 0.0);
                
                _logger?.LogDebug("Recorded operation: {Operation} ({Duration}ms, Success: {Success})", 
                    operationName, duration.TotalMilliseconds, success);

                // Check for performance alerts
                CheckPerformanceAlerts(operationName, duration, success);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error recording operation: {OperationName}", operationName);
            }
        }

        /// <summary>
        /// Record a security event
        /// </summary>
        public void RecordSecurityEvent(string eventType, string description, SecuritySeverity severity,
            Dictionary<string, object>? context = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(eventType);
            ArgumentException.ThrowIfNullOrEmpty(description);
            
            try
            {
                var telemetryEvent = new TelemetryEvent
                {
                    EventType = TelemetryEventType.Security,
                    Name = eventType,
                    Description = description,
                    Timestamp = DateTime.UtcNow,
                    Severity = severity.ToString(),
                    Metadata = context ?? new Dictionary<string, object>(),
                    SessionId = GetSessionId()
                };

                _eventQueue.Enqueue(telemetryEvent);
                
                RecordMetric($"security.events.{eventType}", 1.0);
                RecordMetric($"security.severity.{severity}", 1.0);
                
                _logger?.LogInformation("Recorded security event: {EventType} - {Description} (Severity: {Severity})", 
                    eventType, description, severity);

                // Trigger alerts for high/critical security events
                if (severity >= SecuritySeverity.High)
                {
                    OnTelemetryAlert(new TelemetryEventArgs(
                        TelemetryAlertType.SecurityAlert,
                        $"Security event: {eventType} - {description}",
                        severity.ToString()));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error recording security event: {EventType}", eventType);
            }
        }

        /// <summary>
        /// Record an error occurrence
        /// </summary>
        public void RecordError(Exception exception, string? operationName = null, 
            Dictionary<string, object>? context = null)
        {
            ArgumentNullException.ThrowIfNull(exception);
            
            try
            {
                var telemetryEvent = new TelemetryEvent
                {
                    EventType = TelemetryEventType.Error,
                    Name = operationName ?? "unknown_operation",
                    Description = exception.Message,
                    Timestamp = DateTime.UtcNow,
                    Severity = "Error",
                    ExceptionType = exception.GetType().Name,
                    StackTrace = exception.StackTrace,
                    Metadata = context ?? new Dictionary<string, object>(),
                    SessionId = GetSessionId()
                };

                _eventQueue.Enqueue(telemetryEvent);
                
                RecordMetric("errors.total", 1.0);
                RecordMetric($"errors.by_type.{exception.GetType().Name}", 1.0);
                
                if (operationName != null)
                {
                    RecordMetric($"errors.by_operation.{operationName}", 1.0);
                }

                _logger?.LogError(exception, "Recorded error in operation: {OperationName}", operationName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error recording error event");
            }
        }

        /// <summary>
        /// Get current performance summary
        /// </summary>
        public PerformanceSummary GetPerformanceSummary(TimeSpan? timeWindow = null)
        {
            var window = timeWindow ?? TimeSpan.FromHours(1);
            var cutoff = DateTime.UtcNow - window;

            lock (_metricsLock)
            {
                var summary = new PerformanceSummary
                {
                    TimeWindow = window,
                    SystemMetrics = CurrentSystemMetrics,
                    ApplicationMetrics = CurrentApplicationMetrics
                };

                // Calculate operation statistics
                var recentEvents = _eventQueue.Where(e => e.Timestamp >= cutoff).ToList();
                var operationEvents = recentEvents.Where(e => e.EventType == TelemetryEventType.Operation).ToList();

                summary.TotalOperations = operationEvents.Count;
                summary.SuccessfulOperations = operationEvents.Count(e => e.Success);
                summary.FailedOperations = summary.TotalOperations - summary.SuccessfulOperations;
                summary.SuccessRate = summary.TotalOperations > 0 
                    ? (double)summary.SuccessfulOperations / summary.TotalOperations 
                    : 1.0;

                if (operationEvents.Any())
                {
                    var durations = operationEvents.Select(e => e.Duration?.TotalMilliseconds ?? 0).ToList();
                    summary.AverageOperationTime = TimeSpan.FromMilliseconds(durations.Average());
                    summary.MedianOperationTime = TimeSpan.FromMilliseconds(durations.OrderBy(d => d).ElementAt(durations.Count / 2));
                    summary.MaxOperationTime = TimeSpan.FromMilliseconds(durations.Max());
                }

                // Calculate error statistics
                var errorEvents = recentEvents.Where(e => e.EventType == TelemetryEventType.Error).ToList();
                summary.TotalErrors = errorEvents.Count;
                summary.ErrorRate = summary.TotalOperations > 0 
                    ? (double)summary.TotalErrors / summary.TotalOperations 
                    : 0.0;

                // Top operations by volume
                summary.TopOperations = operationEvents
                    .GroupBy(e => e.Name)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .ToDictionary(g => g.Key, g => g.Count());

                return summary;
            }
        }

        /// <summary>
        /// Export telemetry data for analysis
        /// </summary>
        public string ExportTelemetryData(DateTime? startTime = null, DateTime? endTime = null, 
            TelemetryExportFormat format = TelemetryExportFormat.Json)
        {
            var start = startTime ?? DateTime.UtcNow.AddHours(-24);
            var end = endTime ?? DateTime.UtcNow;

            var events = _eventQueue
                .Where(e => e.Timestamp >= start && e.Timestamp <= end)
                .OrderBy(e => e.Timestamp)
                .ToList();

            var metrics = _metrics.Values
                .SelectMany(series => series.GetPointsInRange(start, end))
                .OrderBy(point => point.Timestamp)
                .ToList();

            var exportData = new TelemetryExport
            {
                StartTime = start,
                EndTime = end,
                Events = events,
                MetricPoints = metrics,
                SystemInfo = new
                {
                    MachineName = Environment.MachineName,
                    OSVersion = Environment.OSVersion.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    WorkingSet = Environment.WorkingSet,
                    ExportTime = DateTime.UtcNow
                }
            };

            return format switch
            {
                TelemetryExportFormat.Json => JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true }),
                TelemetryExportFormat.Csv => ExportToCsv(exportData),
                _ => throw new ArgumentException($"Unsupported export format: {format}")
            };
        }

        /// <summary>
        /// Clear old telemetry data
        /// </summary>
        public void CleanupOldData(TimeSpan retentionPeriod)
        {
            var cutoff = DateTime.UtcNow - retentionPeriod;
            
            // Clean up events
            var eventsToRemove = new List<TelemetryEvent>();
            while (_eventQueue.TryDequeue(out var eventItem))
            {
                if (eventItem.Timestamp >= cutoff)
                {
                    eventsToRemove.Add(eventItem);
                }
            }
            
            foreach (var eventItem in eventsToRemove)
            {
                _eventQueue.Enqueue(eventItem);
            }

            // Clean up metrics
            lock (_metricsLock)
            {
                foreach (var series in _metrics.Values)
                {
                    series.RemovePointsBefore(cutoff);
                }
                
                // Remove empty series
                var emptySeries = _metrics.Where(kvp => kvp.Value.IsEmpty).Select(kvp => kvp.Key).ToList();
                foreach (var key in emptySeries)
                {
                    _metrics.TryRemove(key, out _);
                }
            }

            _logger?.LogInformation("Cleaned up telemetry data older than {RetentionPeriod}", retentionPeriod);
        }

        private void CollectMetrics(object? state)
        {
            try
            {
                if (!_configService.Configuration.Monitoring.EnablePerformanceMonitoring)
                    return;

                // System metrics
                var systemMetrics = new SystemMetrics
                {
                    Timestamp = DateTime.UtcNow,
                    CpuUsagePercent = _cpuCounter?.NextValue() ?? 0,
                    AvailableMemoryMB = _memoryCounter?.NextValue() ?? 0,
                    WorkingSetMB = Environment.WorkingSet / (1024 * 1024),
                    GcMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024)
                };

                CurrentSystemMetrics = systemMetrics;

                // Application metrics
                var process = Process.GetCurrentProcess();
                var appMetrics = new ApplicationMetrics
                {
                    Timestamp = DateTime.UtcNow,
                    ThreadCount = process.Threads.Count,
                    HandleCount = process.HandleCount,
                    UpTime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime,
                    PrivateMemoryMB = process.PrivateMemorySize64 / (1024 * 1024),
                    VirtualMemoryMB = process.VirtualMemorySize64 / (1024 * 1024)
                };

                CurrentApplicationMetrics = appMetrics;

                // Record metrics
                RecordMetric("system.cpu_usage", systemMetrics.CpuUsagePercent);
                RecordMetric("system.available_memory_mb", systemMetrics.AvailableMemoryMB);
                RecordMetric("system.working_set_mb", systemMetrics.WorkingSetMB);
                RecordMetric("app.thread_count", appMetrics.ThreadCount);
                RecordMetric("app.handle_count", appMetrics.HandleCount);
                RecordMetric("app.private_memory_mb", appMetrics.PrivateMemoryMB);

                // Check for resource alerts
                CheckResourceAlerts(systemMetrics, appMetrics);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error collecting metrics");
            }
        }

        private void PerformHealthCheck(object? state)
        {
            try
            {
                if (!_configService.Configuration.Monitoring.EnableHealthMonitoring)
                    return;

                _logger?.LogDebug("Performing health check");

                var healthStatus = new HealthCheckResult
                {
                    Timestamp = DateTime.UtcNow,
                    IsHealthy = true,
                    Checks = new Dictionary<string, bool>()
                };

                // Memory health check
                var memoryUsagePercent = (CurrentSystemMetrics.WorkingSetMB / 
                    (CurrentSystemMetrics.AvailableMemoryMB + CurrentSystemMetrics.WorkingSetMB)) * 100;
                
                healthStatus.Checks["memory_usage"] = memoryUsagePercent < _configService.Configuration.Monitoring.MemoryUsageAlertThreshold;
                
                // CPU health check
                healthStatus.Checks["cpu_usage"] = CurrentSystemMetrics.CpuUsagePercent < _configService.Configuration.Monitoring.CpuUsageAlertThreshold;

                // Error rate health check
                var recentSummary = GetPerformanceSummary(TimeSpan.FromMinutes(15));
                healthStatus.Checks["error_rate"] = recentSummary.ErrorRate < 0.1; // Less than 10% error rate

                healthStatus.IsHealthy = healthStatus.Checks.Values.All(check => check);

                RecordMetric("health.overall", healthStatus.IsHealthy ? 1.0 : 0.0);
                RecordMetric("health.memory_usage_percent", memoryUsagePercent);

                if (!healthStatus.IsHealthy)
                {
                    var failedChecks = healthStatus.Checks.Where(kvp => !kvp.Value).Select(kvp => kvp.Key);
                    OnTelemetryAlert(new TelemetryEventArgs(
                        TelemetryAlertType.HealthAlert,
                        $"Health check failed: {string.Join(", ", failedChecks)}",
                        "Warning"));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error performing health check");
            }
        }

        private void CheckPerformanceAlerts(string operationName, TimeSpan duration, bool success)
        {
            // Check for slow operations (over 30 seconds)
            if (duration.TotalSeconds > 30)
            {
                OnTelemetryAlert(new TelemetryEventArgs(
                    TelemetryAlertType.PerformanceAlert,
                    $"Slow operation detected: {operationName} took {duration.TotalSeconds:F1}s",
                    "Warning"));
            }

            // Check for operation failures
            if (!success)
            {
                // Get recent failure rate for this operation
                var recentEvents = _eventQueue
                    .Where(e => e.Name == operationName && e.Timestamp > DateTime.UtcNow.AddMinutes(-15))
                    .ToList();

                if (recentEvents.Count >= 5)
                {
                    var failureRate = recentEvents.Count(e => !e.Success) / (double)recentEvents.Count;
                    if (failureRate > 0.5) // More than 50% failure rate
                    {
                        OnTelemetryAlert(new TelemetryEventArgs(
                            TelemetryAlertType.FailureAlert,
                            $"High failure rate for operation: {operationName} ({failureRate:P0} in last 15 minutes)",
                            "High"));
                    }
                }
            }
        }

        private void CheckResourceAlerts(SystemMetrics systemMetrics, ApplicationMetrics appMetrics)
        {
            // Memory usage alert
            if (systemMetrics.CpuUsagePercent > _configService.Configuration.Monitoring.MemoryUsageAlertThreshold)
            {
                OnTelemetryAlert(new TelemetryEventArgs(
                    TelemetryAlertType.ResourceAlert,
                    $"High memory usage: {systemMetrics.WorkingSetMB}MB working set",
                    "Warning"));
            }

            // CPU usage alert
            if (systemMetrics.CpuUsagePercent > _configService.Configuration.Monitoring.CpuUsageAlertThreshold)
            {
                OnTelemetryAlert(new TelemetryEventArgs(
                    TelemetryAlertType.ResourceAlert,
                    $"High CPU usage: {systemMetrics.CpuUsagePercent:F1}%",
                    "Warning"));
            }
        }

        private string GetSessionId()
        {
            // Simple session ID based on process start time
            return Process.GetCurrentProcess().StartTime.Ticks.ToString("X");
        }

        private string ExportToCsv(TelemetryExport data)
        {
            var csv = new StringBuilder();
            
            // CSV header
            csv.AppendLine("Type,Timestamp,Name,Value,Duration,Success,Severity,Description");
            
            // Events
            foreach (var eventItem in data.Events)
            {
                csv.AppendLine($"{eventItem.EventType},{eventItem.Timestamp:O},{eventItem.Name},,{eventItem.Duration?.TotalMilliseconds},{eventItem.Success},{eventItem.Severity},\"{eventItem.Description}\"");
            }
            
            // Metrics
            foreach (var point in data.MetricPoints)
            {
                csv.AppendLine($"Metric,{point.Timestamp:O},{point.MetricName},{point.Value},,,");
            }
            
            return csv.ToString();
        }

        private void OnTelemetryAlert(TelemetryEventArgs e)
        {
            TelemetryAlert?.Invoke(this, e);
        }

        public void Dispose()
        {
            _metricsCollectionTimer?.Dispose();
            _healthCheckTimer?.Dispose();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
        }
    }

    // Supporting classes for telemetry
    public class TelemetryEvent
    {
        public TelemetryEventType EventType { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool Success { get; set; }
        public string? Severity { get; set; }
        public string? ExceptionType { get; set; }
        public string? StackTrace { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string? SessionId { get; set; }
    }

    public class MetricPoint
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new();
        public string MetricName { get; set; } = "";
    }

    public class MetricSeries
    {
        private readonly Queue<MetricPoint> _points = new();
        private readonly object _lock = new();
        private readonly int _maxPoints = 1000;

        public string Name { get; }
        public bool IsEmpty => _points.Count == 0;

        public MetricSeries(string name, MetricPoint initialPoint)
        {
            Name = name;
            AddPoint(initialPoint);
        }

        public void AddPoint(MetricPoint point)
        {
            lock (_lock)
            {
                point.MetricName = Name;
                _points.Enqueue(point);
                
                // Keep only recent points
                while (_points.Count > _maxPoints)
                {
                    _points.Dequeue();
                }
            }
        }

        public List<MetricPoint> GetPointsInRange(DateTime start, DateTime end)
        {
            lock (_lock)
            {
                return _points.Where(p => p.Timestamp >= start && p.Timestamp <= end).ToList();
            }
        }

        public void RemovePointsBefore(DateTime cutoff)
        {
            lock (_lock)
            {
                var pointsToKeep = new Queue<MetricPoint>();
                while (_points.Count > 0)
                {
                    var point = _points.Dequeue();
                    if (point.Timestamp >= cutoff)
                    {
                        pointsToKeep.Enqueue(point);
                    }
                }
                
                _points.Clear();
                while (pointsToKeep.Count > 0)
                {
                    _points.Enqueue(pointsToKeep.Dequeue());
                }
            }
        }
    }

    public class SystemMetrics
    {
        public DateTime Timestamp { get; set; }
        public float CpuUsagePercent { get; set; }
        public float AvailableMemoryMB { get; set; }
        public long WorkingSetMB { get; set; }
        public long GcMemoryMB { get; set; }
    }

    public class ApplicationMetrics
    {
        public DateTime Timestamp { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public TimeSpan UpTime { get; set; }
        public long PrivateMemoryMB { get; set; }
        public long VirtualMemoryMB { get; set; }
    }

    public class PerformanceSummary
    {
        public TimeSpan TimeWindow { get; set; }
        public SystemMetrics SystemMetrics { get; set; } = new();
        public ApplicationMetrics ApplicationMetrics { get; set; } = new();
        public int TotalOperations { get; set; }
        public int SuccessfulOperations { get; set; }
        public int FailedOperations { get; set; }
        public double SuccessRate { get; set; }
        public int TotalErrors { get; set; }
        public double ErrorRate { get; set; }
        public TimeSpan AverageOperationTime { get; set; }
        public TimeSpan MedianOperationTime { get; set; }
        public TimeSpan MaxOperationTime { get; set; }
        public Dictionary<string, int> TopOperations { get; set; } = new();
    }

    public class HealthCheckResult
    {
        public DateTime Timestamp { get; set; }
        public bool IsHealthy { get; set; }
        public Dictionary<string, bool> Checks { get; set; } = new();
    }

    public class TelemetryExport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<TelemetryEvent> Events { get; set; } = new();
        public List<MetricPoint> MetricPoints { get; set; } = new();
        public object? SystemInfo { get; set; }
    }

    public class TelemetryEventArgs : EventArgs
    {
        public TelemetryAlertType AlertType { get; }
        public string Message { get; }
        public string Severity { get; }
        public DateTime Timestamp { get; } = DateTime.UtcNow;

        public TelemetryEventArgs(TelemetryAlertType alertType, string message, string severity)
        {
            AlertType = alertType;
            Message = message;
            Severity = severity;
        }
    }

    public enum TelemetryEventType
    {
        Operation,
        Error,
        Security,
        Performance,
        Health
    }

    public enum TelemetryAlertType
    {
        PerformanceAlert,
        HealthAlert,
        SecurityAlert,
        FailureAlert,
        ResourceAlert
    }

    public enum TelemetryExportFormat
    {
        Json,
        Csv
    }
}