using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// 統合パフォーマンス監視サービス
    /// </summary>
    public class PerformanceMonitorService : IPerformanceMonitor, IDisposable
    {
        private readonly ConcurrentDictionary<string, OperationMetrics> _metrics;
        private readonly ConcurrentDictionary<string, MetricEntry> _systemMetrics;
        private readonly ILogger<PerformanceMonitorService>? _logger;
        private readonly Timer _reportTimer;
        private readonly Timer _systemMonitorTimer;
        private readonly object _lockObject = new();
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _memoryCounter;
        
        public PerformanceMonitorService(ILogger<PerformanceMonitorService>? logger = null)
        {
            _logger = logger;
            _metrics = new ConcurrentDictionary<string, OperationMetrics>();
            _systemMetrics = new ConcurrentDictionary<string, MetricEntry>();
            
            // 定期レポート（5分ごと）
            _reportTimer = new Timer(GenerateReport, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            // システムメトリクス監視（30秒ごと）
            _systemMonitorTimer = new Timer(CollectSystemMetrics, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            
            // パフォーマンスカウンター初期化
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to initialize performance counters");
            }
        }
        
        public IDisposable StartOperation(string operationName)
        {
            return new OperationScope(this, operationName);
        }
        
        public async Task<T> MeasureAsync<T>(string operationName, Func<Task<T>> operation)
        {
            using var scope = StartOperation(operationName);
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch
            {
                scope.MarkAsFailed();
                throw;
            }
        }
        
        public async Task MeasureAsync(string operationName, Func<Task> operation)
        {
            using var scope = StartOperation(operationName);
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch
            {
                scope.MarkAsFailed();
                throw;
            }
        }
        
        public void RecordOperation(string operationName, long elapsedMs, bool success)
        {
            var metrics = _metrics.GetOrAdd(operationName, _ => new OperationMetrics(operationName));
            metrics.Record(elapsedMs, success);
            
            // 警告閾値チェック
            if (elapsedMs > 5000)
            {
                _logger?.LogWarning("Slow operation detected: {OperationName} took {ElapsedMs}ms", 
                    operationName, elapsedMs);
            }
        }
        
        public OperationMetrics? GetMetrics(string operationName)
        {
            return _metrics.TryGetValue(operationName, out var metrics) ? metrics : null;
        }
        
        public Dictionary<string, OperationMetrics> GetAllMetrics()
        {
            return new Dictionary<string, OperationMetrics>(_metrics);
        }
        
        public PerformanceSummary GetSummary()
        {
            var allMetrics = GetAllMetrics();
            
            return new PerformanceSummary
            {
                TotalOperations = allMetrics.Sum(m => m.Value.TotalCount),
                TotalSuccessful = allMetrics.Sum(m => m.Value.SuccessCount),
                TotalFailed = allMetrics.Sum(m => m.Value.FailureCount),
                AverageResponseTimeMs = allMetrics.Any() 
                    ? allMetrics.Average(m => m.Value.AverageMs) 
                    : 0,
                SlowestOperation = allMetrics.OrderByDescending(m => m.Value.MaxMs)
                    .FirstOrDefault().Key ?? "",
                MostFrequentOperation = allMetrics.OrderByDescending(m => m.Value.TotalCount)
                    .FirstOrDefault().Key ?? "",
                GeneratedAt = DateTime.UtcNow
            };
        }
        
        public void Reset()
        {
            _metrics.Clear();
            _logger?.LogInformation("Performance metrics reset");
        }
        
        private void GenerateReport(object? state)
        {
            var summary = GetSummary();
            
            _logger?.LogInformation(
                "Performance Report - Total: {Total}, Success: {Success}, Failed: {Failed}, Avg Response: {AvgMs}ms",
                summary.TotalOperations,
                summary.TotalSuccessful,
                summary.TotalFailed,
                summary.AverageResponseTimeMs);
            
            // 詳細メトリクス（上位5つの遅い操作）
            var slowOperations = GetAllMetrics()
                .OrderByDescending(m => m.Value.AverageMs)
                .Take(5);
            
            foreach (var op in slowOperations)
            {
                _logger?.LogDebug(
                    "Slow operation: {Operation} - Avg: {AvgMs}ms, Max: {MaxMs}ms, Count: {Count}",
                    op.Key,
                    op.Value.AverageMs,
                    op.Value.MaxMs,
                    op.Value.TotalCount);
            }
        }
        
        public void RecordSystemMetrics()
        {
            CollectSystemMetrics(null);
        }
        
        public Dictionary<string, MetricEntry> GetCurrentMetrics()
        {
            return new Dictionary<string, MetricEntry>(_systemMetrics);
        }
        
        public MetricsSummary GetMetricsSummary()
        {
            var metrics = GetCurrentMetrics();
            var categories = metrics.GroupBy(m => m.Value.Category ?? "general")
                .ToDictionary(g => g.Key, g => g.Count());
            
            return new MetricsSummary
            {
                TotalMetrics = metrics.Count,
                CounterMetrics = metrics.Count(m => m.Value.Type == MetricType.Counter),
                GaugeMetrics = metrics.Count(m => m.Value.Type == MetricType.Gauge),
                TimerMetrics = metrics.Count(m => m.Value.Type == MetricType.Timer),
                Categories = categories
            };
        }
        
        private void CollectSystemMetrics(object? state)
        {
            try
            {
                var process = Process.GetCurrentProcess();
                
                // プロセスメトリクス
                _systemMetrics.AddOrUpdate("process.memory.mb",
                    new MetricEntry 
                    { 
                        Name = "process.memory.mb", 
                        Category = "process", 
                        Type = MetricType.Gauge, 
                        Value = process.WorkingSet64 / (1024.0 * 1024.0),
                        LastUpdated = DateTime.UtcNow
                    },
                    (k, existing) =>
                    {
                        existing.Value = process.WorkingSet64 / (1024.0 * 1024.0);
                        existing.LastUpdated = DateTime.UtcNow;
                        return existing;
                    });
                
                _systemMetrics.AddOrUpdate("process.threads",
                    new MetricEntry 
                    { 
                        Name = "process.threads", 
                        Category = "process", 
                        Type = MetricType.Gauge, 
                        Value = process.Threads.Count,
                        LastUpdated = DateTime.UtcNow
                    },
                    (k, existing) =>
                    {
                        existing.Value = process.Threads.Count;
                        existing.LastUpdated = DateTime.UtcNow;
                        return existing;
                    });
                
                // システムメトリクス（Windows）
                if (_cpuCounter != null)
                {
                    _systemMetrics.AddOrUpdate("system.cpu.percent",
                        new MetricEntry 
                        { 
                            Name = "system.cpu.percent", 
                            Category = "system", 
                            Type = MetricType.Gauge, 
                            Value = _cpuCounter.NextValue(),
                            LastUpdated = DateTime.UtcNow
                        },
                        (k, existing) =>
                        {
                            existing.Value = _cpuCounter.NextValue();
                            existing.LastUpdated = DateTime.UtcNow;
                            return existing;
                        });
                }
                
                if (_memoryCounter != null)
                {
                    _systemMetrics.AddOrUpdate("system.memory.available.mb",
                        new MetricEntry 
                        { 
                            Name = "system.memory.available.mb", 
                            Category = "system", 
                            Type = MetricType.Gauge, 
                            Value = _memoryCounter.NextValue(),
                            LastUpdated = DateTime.UtcNow
                        },
                        (k, existing) =>
                        {
                            existing.Value = _memoryCounter.NextValue();
                            existing.LastUpdated = DateTime.UtcNow;
                            return existing;
                        });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to collect system metrics");
            }
        }
        
        public void Dispose()
        {
            _reportTimer?.Dispose();
            _systemMonitorTimer?.Dispose();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
        }
        
        private class OperationScope : IDisposable
        {
            private readonly PerformanceMonitorService _monitor;
            private readonly string _operationName;
            private readonly Stopwatch _stopwatch;
            private bool _success = true;
            private bool _disposed;
            
            public OperationScope(PerformanceMonitorService monitor, string operationName)
            {
                _monitor = monitor;
                _operationName = operationName;
                _stopwatch = Stopwatch.StartNew();
            }
            
            public void MarkAsFailed()
            {
                _success = false;
            }
            
            public void Dispose()
            {
                if (!_disposed)
                {
                    _stopwatch.Stop();
                    _monitor.RecordOperation(_operationName, _stopwatch.ElapsedMilliseconds, _success);
                    _disposed = true;
                }
            }
        }
    }
}