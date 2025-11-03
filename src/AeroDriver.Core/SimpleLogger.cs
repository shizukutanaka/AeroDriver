using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace AeroDriver.Core;

/// <summary>
/// Minimal logger abstraction used across the AeroDriver codebase for high-sensitivity environments.
/// The implementation favours deterministic behaviour, thread-safety, and controlled I/O.
/// </summary>
public interface ISimpleLogger
{
    void LogInformation(string message, object? context = null);
    void LogWarning(string message, object? context = null);
    void LogError(string message, object? context = null, Exception? exception = null);
    void LogDebug(string message, object? context = null);

    Task InfoAsync(string message, string? context = null, CancellationToken cancellationToken = default);
    Task WarningAsync(string message, string? context = null, CancellationToken cancellationToken = default);
    Task ErrorAsync(string message, string? context = null, Exception? exception = null, CancellationToken cancellationToken = default);
    Task DebugAsync(string message, string? context = null, CancellationToken cancellationToken = default);
    Task LogSecurityEventAsync(string eventType, string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight logger that writes to the console and to rolling daily log files under the AeroDriver data directory.
/// Designed to avoid external dependencies while providing deterministic output that can be consumed by SIEM tools.
/// </summary>
public sealed class SimpleLogger : ISimpleLogger, IDisposable
{
    private const int MaxPendingWrites = 10_000;
    private const int FlushBatchSize = 512;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetentionCheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan LogRetentionPeriod = TimeSpan.FromDays(30);

    private const string DefaultPrefix = "[AeroDriver]";
    private readonly string _componentPrefix;
    private readonly object _fileLock = new();
    private readonly object _retentionLock = new();
    private readonly ConcurrentQueue<(string Path, string Message)> _pendingWrites = new();
    private readonly Timer _flushTimer;
    private readonly string _logDirectory;
    private readonly string _securityLogDirectory;
    private DateTime _lastRetentionCheckUtc = DateTime.MinValue;
    private int _isFlushing;
    private bool _disposed;

    public SimpleLogger(string? componentName = null)
    {
        _componentPrefix = string.IsNullOrWhiteSpace(componentName)
            ? DefaultPrefix
            : $"{DefaultPrefix}[{componentName}]";

        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }

        _logDirectory = Path.Combine(baseDirectory, "AeroDriver", "Logs");
        _securityLogDirectory = Path.Combine(_logDirectory, "Security");

        Directory.CreateDirectory(_logDirectory);
        Directory.CreateDirectory(_securityLogDirectory);

        _flushTimer = new Timer(_ => FlushPendingWrites(), null, FlushInterval, FlushInterval);
    }

    public void LogInformation(string message, object? context = null)
        => Write(LogLevel.Information, message, context);

    public void LogWarning(string message, object? context = null)
        => Write(LogLevel.Warning, message, context);

    public void LogError(string message, object? context = null, Exception? exception = null)
        => Write(LogLevel.Error, message, context, exception);

    public void LogError(string message, Exception exception)
        => Write(LogLevel.Error, message, null, exception);

    public void LogInformation(string message)
        => LogInformation(message, null);

    public void LogWarning(string message)
        => LogWarning(message, null);

    public void LogDebug(string message)
        => LogDebug(message, null);

    public void LogDebug(string message, object? context = null)
        => Write(LogLevel.Debug, message, context);

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
        var securityLine = FormatLogLine(LogLevel.Security, $"{eventType}: {message}");
        QueueWrite(Path.Combine(_securityLogDirectory, GetDailyFileName("security")), securityLine);
        Console.WriteLine(securityLine);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _flushTimer.Dispose();
        FlushPendingWrites();
    }

    private void Write(LogLevel level, string message, object? context, Exception? exception = null)
    {
        var formattedMessage = FormatLogLine(level, message, context, exception);
        if (level >= LogLevel.Error)
        {
            Console.Error.WriteLine(formattedMessage);
        }
        else
        {
            Console.WriteLine(formattedMessage);
        }

        QueueWrite(Path.Combine(_logDirectory, GetDailyFileName("application")), formattedMessage);
    }

    private void QueueWrite(string path, string message)
    {
        if (_disposed)
        {
            return;
        }

        if (_pendingWrites.Count >= MaxPendingWrites)
        {
            // Drop oldest entries to keep memory bounded
            var dropped = 0;
            while (_pendingWrites.Count >= MaxPendingWrites && _pendingWrites.TryDequeue(out _))
            {
                dropped++;
            }

            if (dropped > 0)
            {
                var warning = FormatLogLine(LogLevel.Warning, $"Dropped {dropped} pending log entries due to queue limit", null);
                Console.Error.WriteLine(warning);
            }
        }

        _pendingWrites.Enqueue((path, message + Environment.NewLine));
        MaybeEnforceRetention();
    }

    private void FlushPendingWrites()
    {
        if (Interlocked.Exchange(ref _isFlushing, 1) == 1)
        {
            return;
        }

        try
        {
            var processed = 0;

            while (processed < FlushBatchSize && _pendingWrites.TryDequeue(out var entry))
            {
                processed++;

                try
                {
                    var directory = Path.GetDirectoryName(entry.Path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    lock (_fileLock)
                    {
                        File.AppendAllText(entry.Path, entry.Message, Encoding.UTF8);
                    }
                }
                catch
                {
                    // ログ出力の失敗はアプリケーションフローに影響させない
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isFlushing, 0);
        }
    }

    private string FormatLogLine(LogLevel level, string message, object? context = null, Exception? exception = null)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var levelLabel = level.ToString().ToUpperInvariant().PadRight(8);
        var line = new StringBuilder()
            .Append(timestamp)
            .Append(' ')
            .Append(levelLabel)
            .Append(' ')
            .Append(_componentPrefix)
            .Append(' ')
            .Append(message);

        if (context != null)
        {
            line.Append(" | ").Append(context);
        }

        if (exception != null)
        {
            line.Append(" | Exception: ").Append(exception.GetType().Name).Append(" - ").Append(exception.Message);
        }

        return line.ToString();
    }

    private static string GetDailyFileName(string prefix)
    {
        var dateStamp = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"{prefix}_{dateStamp}.log";
    }

    private void MaybeEnforceRetention()
    {
        var now = DateTime.UtcNow;
        if (now - _lastRetentionCheckUtc < RetentionCheckInterval)
        {
            return;
        }

        lock (_retentionLock)
        {
            if (now - _lastRetentionCheckUtc < RetentionCheckInterval)
            {
                return;
            }

            try
            {
                EnforceRetention(_logDirectory, "*.log");
                EnforceRetention(_securityLogDirectory, "*.log");
            }
            catch
            {
                // Retention failures do not interrupt logging
            }
            finally
            {
                _lastRetentionCheckUtc = now;
            }
        }
    }

    private static void EnforceRetention(string directory, string searchPattern)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, searchPattern))
        {
            try
            {
                var creation = File.GetCreationTimeUtc(file);
                if (DateTime.UtcNow - creation > LogRetentionPeriod)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Ignore retention deletion errors
            }
        }
    }

    private enum LogLevel
    {
        Debug,
        Information,
        Warning,
        Error,
        Security
    }

    #endregion

    #region Enhanced Logging Features

    private static readonly ConcurrentDictionary<string, LogFilter> _logFilters = new();
    private static readonly ConcurrentQueue<LogEntry> _logHistory = new();
    private static readonly ConcurrentDictionary<LogLevel, LogStatistics> _logStatistics = new();
    private static LogLevel _minimumLogLevel = LogLevel.Debug;
    private static bool _structuredLoggingEnabled = true;
    private static int _maxLogHistorySize = 10000;
    private static readonly object _logStatsLock = new();

    /// <summary>
    /// 構造化ログを有効/無効化
    /// </summary>
    public static void SetStructuredLogging(bool enabled)
    {
        _structuredLoggingEnabled = enabled;
    }

    /// <summary>
    /// 最小ログレベルを設定
    /// </summary>
    public static void SetMinimumLogLevel(LogLevel level)
    {
        _minimumLogLevel = level;
    }

    /// <summary>
    /// ログフィルタを追加
    /// </summary>
    public static void AddLogFilter(string filterName, Func<LogEntry, bool> filterPredicate, LogFilterAction action = LogFilterAction.Include)
    {
        if (string.IsNullOrWhiteSpace(filterName))
            throw new ArgumentNullException(nameof(filterName));

        _logFilters[filterName] = new LogFilter
        {
            Name = filterName,
            Predicate = filterPredicate,
            Action = action,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// ログフィルタを削除
    /// </summary>
    public static bool RemoveLogFilter(string filterName)
    {
        return _logFilters.TryRemove(filterName, out _);
    }

    /// <summary>
    /// 構造化ログを出力
    /// </summary>
    public void LogStructured(LogLevel level, string message, object? context = null, Exception? exception = null, Dictionary<string, object>? additionalData = null)
    {
        if (level < _minimumLogLevel)
            return;

        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message,
            Context = context?.ToString(),
            Exception = exception,
            AdditionalData = additionalData ?? new Dictionary<string, object>(),
            Component = _componentPrefix,
            ThreadId = Environment.CurrentManagedThreadId,
            ProcessId = Process.GetCurrentProcess().Id
        };

        // フィルタ適用
        if (ShouldFilterLogEntry(logEntry))
            return;

        // ログ履歴に追加
        _logHistory.Enqueue(logEntry);
        while (_logHistory.Count > _maxLogHistorySize)
        {
            _logHistory.TryDequeue(out _);
        }

        // 統計更新
        UpdateLogStatistics(logEntry);

        // 構造化ログ出力
        WriteStructuredLog(logEntry);
    }

    /// <summary>
    /// パフォーマンスログを出力
    /// </summary>
    public void LogPerformance(string operation, TimeSpan duration, Dictionary<string, object>? metrics = null)
    {
        var performanceData = new Dictionary<string, object>
        {
            ["operation"] = operation,
            ["duration_ms"] = duration.TotalMilliseconds,
            ["duration"] = duration.ToString()
        };

        if (metrics != null)
        {
            foreach (var kvp in metrics)
            {
                performanceData[$"metric_{kvp.Key}"] = kvp.Value;
            }
        }

        LogStructured(LogLevel.Information, $"Performance: {operation}", null, null, performanceData);
    }

    /// <summary>
    /// ログ検索を実行
    /// </summary>
    public IEnumerable<LogEntry> SearchLogs(LogSearchCriteria criteria)
    {
        var allLogs = _logHistory.ToArray();

        var query = allLogs.AsQueryable();

        if (criteria.StartTime.HasValue)
            query = query.Where(log => log.Timestamp >= criteria.StartTime.Value);

        if (criteria.EndTime.HasValue)
            query = query.Where(log => log.Timestamp <= criteria.EndTime.Value);

        if (criteria.Level.HasValue)
            query = query.Where(log => log.Level == criteria.Level.Value);

        if (!string.IsNullOrEmpty(criteria.MessageContains))
            query = query.Where(log => log.Message.Contains(criteria.MessageContains, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(criteria.ContextContains))
            query = query.Where(log => log.Context?.Contains(criteria.ContextContains, StringComparison.OrdinalIgnoreCase) == true);

        if (criteria.ExceptionType != null)
            query = query.Where(log => log.Exception?.GetType() == criteria.ExceptionType);

        return query
            .OrderByDescending(log => log.Timestamp)
            .Take(criteria.MaxResults);
    }

    /// <summary>
    /// ログ統計を取得
    /// </summary>
    public LogStatisticsReport GetLogStatistics()
    {
        lock (_logStatsLock)
        {
            var report = new LogStatisticsReport
            {
                GeneratedAt = DateTime.UtcNow,
                TotalLogEntries = _logHistory.Count,
                StatisticsByLevel = _logStatistics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                ActiveFilters = _logFilters.Count,
                StructuredLoggingEnabled = _structuredLoggingEnabled,
                MinimumLogLevel = _minimumLogLevel
            };

            // 最近のログ分析
            var recentLogs = _logHistory.Where(log =>
                (DateTime.UtcNow - log.Timestamp) < TimeSpan.FromHours(1)).ToArray();

            report.RecentLogCount = recentLogs.Length;
            report.RecentErrorCount = recentLogs.Count(log => log.Level >= LogLevel.Error);

            // ログレベル分布
            report.LogLevelDistribution = recentLogs
                .GroupBy(log => log.Level)
                .ToDictionary(g => g.Key, g => g.Count());

            return report;
        }
    }

    /// <summary>
    /// ログをエクスポート
    /// </summary>
    public async Task ExportLogsAsync(string filePath, LogExportFormat format = LogExportFormat.Json)
    {
        var logs = _logHistory.ToArray();

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

        switch (format)
        {
            case LogExportFormat.Json:
                await ExportAsJsonAsync(logs, writer);
                break;
            case LogExportFormat.Csv:
                await ExportAsCsvAsync(logs, writer);
                break;
            case LogExportFormat.Text:
                await ExportAsTextAsync(logs, writer);
                break;
        }
    }

    private bool ShouldFilterLogEntry(LogEntry entry)
    {
        foreach (var filter in _logFilters.Values)
        {
            if (filter.Predicate(entry))
            {
                return filter.Action == LogFilterAction.Exclude;
            }
        }
        return false;
    }

    private void UpdateLogStatistics(LogEntry entry)
    {
        lock (_logStatsLock)
        {
            var stats = _logStatistics.GetOrAdd(entry.Level, _ => new LogStatistics
            {
                Level = entry.Level
            });

            stats.Count++;
            stats.LastOccurrence = entry.Timestamp;

            if (entry.Exception != null)
            {
                stats.ExceptionCount++;
            }
        }
    }

    private void WriteStructuredLog(LogEntry entry)
    {
        if (!_structuredLoggingEnabled)
        {
            // 従来のログ形式で出力
            Write(entry.Level, entry.Message, entry.Context, entry.Exception);
            return;
        }

        var logData = new Dictionary<string, object>
        {
            ["timestamp"] = entry.Timestamp.ToString("O"),
            ["level"] = entry.Level.ToString(),
            ["message"] = entry.Message,
            ["component"] = entry.Component,
            ["threadId"] = entry.ThreadId,
            ["processId"] = entry.ProcessId
        };

        if (!string.IsNullOrEmpty(entry.Context))
            logData["context"] = entry.Context;

        if (entry.Exception != null)
        {
            logData["exception"] = new
            {
                type = entry.Exception.GetType().FullName,
                message = entry.Exception.Message,
                stackTrace = entry.Exception.StackTrace
            };
        }

        // 追加データをマージ
        foreach (var kvp in entry.AdditionalData)
        {
            logData[kvp.Key] = kvp.Value;
        }

        var jsonLog = JsonSerializer.Serialize(logData, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        // 構造化ログをファイルに書き込み
        var structuredLogPath = GetStructuredLogFilePath();
        var structuredMessage = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} {entry.Component} {jsonLog}";

        _pendingWrites.Enqueue((structuredLogPath, structuredMessage));
    }

    private string GetStructuredLogFilePath()
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Path.Combine(_logDirectory, $"structured-{date}.log");
    }

    private async Task ExportAsJsonAsync(LogEntry[] logs, StreamWriter writer)
    {
        await writer.WriteLineAsync("[");
        for (int i = 0; i < logs.Length; i++)
        {
            var log = logs[i];
            var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
            await writer.WriteAsync(json);
            if (i < logs.Length - 1)
                await writer.WriteLineAsync(",");
        }
        await writer.WriteLineAsync("]");
    }

    private async Task ExportAsCsvAsync(LogEntry[] logs, StreamWriter writer)
    {
        await writer.WriteLineAsync("Timestamp,Level,Message,Context,Component,ThreadId,ProcessId,ExceptionType");

        foreach (var log in logs)
        {
            var line = $"{log.Timestamp:O},{log.Level},{EscapeCsv(log.Message)},{EscapeCsv(log.Context)},{EscapeCsv(log.Component)},{log.ThreadId},{log.ProcessId},{EscapeCsv(log.Exception?.GetType().FullName)}";
            await writer.WriteLineAsync(line);
        }
    }

    private async Task ExportAsTextAsync(LogEntry[] logs, StreamWriter writer)
    {
        foreach (var log in logs)
        {
            await writer.WriteLineAsync($"{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{log.Level}] {log.Component} {log.Message}");
            if (!string.IsNullOrEmpty(log.Context))
                await writer.WriteLineAsync($"  Context: {log.Context}");
            if (log.Exception != null)
                await writer.WriteLineAsync($"  Exception: {log.Exception.GetType().Name}: {log.Exception.Message}");
        }
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    /// <summary>
    /// ログフィルタ
    /// </summary>
    private class LogFilter
    {
        public string Name { get; set; } = string.Empty;
        public Func<LogEntry, bool> Predicate { get; set; } = null!;
        public LogFilterAction Action { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// ログフィルタアクション
    /// </summary>
    public enum LogFilterAction
    {
        Include,
        Exclude
    }

    /// <summary>
    /// ログエントリ
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Context { get; set; }
        public Exception? Exception { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
        public string Component { get; set; } = string.Empty;
        public int ThreadId { get; set; }
        public int ProcessId { get; set; }
    }

    /// <summary>
    /// ログ検索条件
    /// </summary>
    public class LogSearchCriteria
    {
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public LogLevel? Level { get; set; }
        public string? MessageContains { get; set; }
        public string? ContextContains { get; set; }
        public Type? ExceptionType { get; set; }
        public int MaxResults { get; set; } = 1000;
    }

    /// <summary>
    /// ログ統計
    /// </summary>
    public class LogStatistics
    {
        public LogLevel Level { get; set; }
        public int Count { get; set; }
        public int ExceptionCount { get; set; }
        public DateTime LastOccurrence { get; set; }
    }

    /// <summary>
    /// ログ統計レポート
    /// </summary>
    public class LogStatisticsReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalLogEntries { get; set; }
        public int RecentLogCount { get; set; }
        public int RecentErrorCount { get; set; }
        public Dictionary<LogLevel, LogStatistics> StatisticsByLevel { get; set; } = new();
        public Dictionary<LogLevel, int> LogLevelDistribution { get; set; } = new();
        public int ActiveFilters { get; set; }
        public bool StructuredLoggingEnabled { get; set; }
        public LogLevel MinimumLogLevel { get; set; }
    }

    /// <summary>
    /// ログエクスポート形式
    /// </summary>
    public enum LogExportFormat
    {
        Json,
        Csv,
        Text
    }

    #endregion