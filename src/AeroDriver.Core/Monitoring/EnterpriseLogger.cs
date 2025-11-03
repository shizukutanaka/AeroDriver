using System.Collections.Concurrent;
using System.Text.Json;

namespace AeroDriver.Core.Monitoring;

/// <summary>
/// エンタープライズグレードのログ管理システム
/// リアルタイム監視、ログ相関分析、パフォーマンス追跡機能を提供
/// </summary>
public class EnterpriseLogger : ISimpleLogger, IDisposable
{
    private readonly string _logDirectory;
    private readonly int _maxLogFiles;
    private readonly long _maxLogFileSize;
    private readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private readonly List<ILogSink> _sinks = new();
    private readonly Timer _flushTimer;
    private readonly Timer _cleanupTimer;
    private readonly object _syncLock = new();
    private bool _disposed;
    private readonly JsonSerializerOptions _jsonOptions;

    public EnterpriseLogger(string logDirectory = null, int maxLogFiles = 10, long maxLogFileSize = 10 * 1024 * 1024)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AeroDriver",
            "Logs");

        Directory.CreateDirectory(_logDirectory);

        _maxLogFiles = maxLogFiles;
        _maxLogFileSize = maxLogFileSize;

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // 定期的にログをフラッシュ
        _flushTimer = new Timer(_ => FlushLogs(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        // 古いログファイルをクリーンアップ
        _cleanupTimer = new Timer(_ => CleanupOldLogs(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        // プロセス終了時のログフラッシュを登録
        AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushLogs();
    }

    /// <summary>
    /// ログシンクを登録
    /// </summary>
    public void RegisterSink(ILogSink sink)
    {
        lock (_syncLock)
        {
            if (!_sinks.Contains(sink))
            {
                _sinks.Add(sink);
            }
        }
    }

    /// <summary>
    /// ログシンクを登録解除
    /// </summary>
    public void UnregisterSink(ILogSink sink)
    {
        lock (_syncLock)
        {
            _sinks.Remove(sink);
        }
    }

    /// <summary>
    /// 情報ログを記録
    /// </summary>
    public void LogInformation(string message)
    {
        Log(LogLevel.Information, message, null);
    }

    /// <summary>
    /// 警告ログを記録
    /// </summary>
    public void LogWarning(string message)
    {
        Log(LogLevel.Warning, message, null);
    }

    /// <summary>
    /// エラーログを記録
    /// </summary>
    public void LogError(string message)
    {
        Log(LogLevel.Error, message, null);
    }

    /// <summary>
    /// セキュリティイベントを記録
    /// </summary>
    public async Task LogSecurityEventAsync(string eventType, string description)
    {
        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Security,
            Category = "Security",
            Message = description,
            EventType = eventType,
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId,
            UserIdentity = GetCurrentUserIdentity()
        };

        await WriteLogEntryAsync(logEntry);
    }

    /// <summary>
    /// パフォーマンスメトリクスを記録
    /// </summary>
    public async Task LogPerformanceMetricAsync(string metricName, double value, Dictionary<string, object> metadata = null)
    {
        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Performance,
            Category = "Performance",
            Message = $"{metricName}: {value}",
            Metadata = metadata ?? new Dictionary<string, object>(),
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId,
            UserIdentity = GetCurrentUserIdentity()
        };

        // メタデータにメトリクス情報を追加
        logEntry.Metadata["metricName"] = metricName;
        logEntry.Metadata["metricValue"] = value;

        await WriteLogEntryAsync(logEntry);
    }

    /// <summary>
    /// 構造化ログを記録
    /// </summary>
    public async Task LogStructuredAsync(LogLevel level, string category, string message,
        Dictionary<string, object> metadata = null, string eventType = null)
    {
        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message,
            Metadata = metadata ?? new Dictionary<string, object>(),
            EventType = eventType,
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId,
            UserIdentity = GetCurrentUserIdentity()
        };

        await WriteLogEntryAsync(logEntry);
    }

    /// <summary>
    /// ログエントリを記録
    /// </summary>
    private void Log(LogLevel level, string message, Exception exception)
    {
        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = "General",
            Message = message,
            Exception = exception?.ToString(),
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId,
            UserIdentity = GetCurrentUserIdentity()
        };

        _logQueue.Enqueue(logEntry);
    }

    /// <summary>
    /// ログエントリを非同期で書き込み
    /// </summary>
    private async Task WriteLogEntryAsync(LogEntry logEntry)
    {
        _logQueue.Enqueue(logEntry);

        // シンクに即時送信
        await DispatchToSinksAsync(logEntry);

        // ログファイルにはキュー経由で書き込み
    }

    /// <summary>
    /// ログをフラッシュ
    /// </summary>
    private async void FlushLogs()
    {
        if (_logQueue.IsEmpty) return;

        var entries = new List<LogEntry>();
        while (_logQueue.TryDequeue(out var entry))
        {
            entries.Add(entry);
        }

        if (entries.Count == 0) return;

        await WriteLogsToFileAsync(entries);
    }

    /// <summary>
    /// ログをファイルに書き込み
    /// </summary>
    private async Task WriteLogsToFileAsync(List<LogEntry> entries)
    {
        var logFilePath = GetCurrentLogFilePath();

        try
        {
            await _writeSemaphore.WaitAsync();

            var jsonLines = entries.Select(entry => JsonSerializer.Serialize(entry, _jsonOptions));
            await File.AppendAllLinesAsync(logFilePath, jsonLines);

            // ファイルサイズチェックとローテーション
            await CheckAndRotateLogFileAsync(logFilePath);
        }
        catch (Exception ex)
        {
            // ログ書き込みエラーはコンソールに出力
            Console.Error.WriteLine($"Failed to write log entry: {ex.Message}");
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// 現在のログファイルパスを取得
    /// </summary>
    private string GetCurrentLogFilePath()
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(_logDirectory, $"aeroDriver_{date}.log");
    }

    /// <summary>
    /// ログファイルのサイズチェックとローテーション
    /// </summary>
    private async Task CheckAndRotateLogFileAsync(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < _maxLogFileSize) return;

            // 現在のログファイルをローテーション
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var rotatedPath = Path.Combine(_logDirectory, $"aeroDriver_{timestamp}.log");

            // 非同期でファイル移動（他のプロセスが読み取り中の可能性があるため）
            await Task.Run(() => File.Move(filePath, rotatedPath));

            // 古いログファイルをクリーンアップ
            await CleanupOldLogsAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to rotate log file: {ex.Message}");
        }
    }

    /// <summary>
    /// 古いログファイルをクリーンアップ
    /// </summary>
    private async void CleanupOldLogs()
    {
        await CleanupOldLogsAsync();
    }

    /// <summary>
    /// 古いログファイルをクリーンアップ（非同期）
    /// </summary>
    private async Task CleanupOldLogsAsync()
    {
        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "aeroDriver_*.log")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            if (logFiles.Count <= _maxLogFiles) return;

            var filesToDelete = logFiles.Skip(_maxLogFiles).ToList();
            await Task.Run(() =>
            {
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to delete old log file {file.Name}: {ex.Message}");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to cleanup old log files: {ex.Message}");
        }
    }

    /// <summary>
    /// シンクにログを送信
    /// </summary>
    private async Task DispatchToSinksAsync(LogEntry logEntry)
    {
        var sinks = _sinks.ToList(); // スレッドセーフなコピー

        foreach (var sink in sinks)
        {
            try
            {
                await sink.WriteLogAsync(logEntry);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to write to log sink: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 現在のユーザーIDを取得
    /// </summary>
    private static string GetCurrentUserIdentity()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return identity.Name ?? "Unknown";
        }
        catch
        {
            return Environment.UserName ?? "Unknown";
        }
    }

    /// <summary>
    /// ログエントリを検索
    /// </summary>
    public async Task<List<LogEntry>> SearchLogsAsync(LogSearchCriteria criteria)
    {
        var results = new List<LogEntry>();

        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "aeroDriver_*.log")
                .OrderByDescending(f => f)
                .ToList();

            foreach (var logFile in logFiles)
            {
                if (results.Count >= criteria.MaxResults) break;

                var entries = await ReadLogEntriesAsync(logFile, criteria);
                results.AddRange(entries);

                if (results.Count >= criteria.MaxResults)
                {
                    results = results.Take(criteria.MaxResults).ToList();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to search logs: {ex.Message}");
        }

        return results.OrderByDescending(e => e.Timestamp).ToList();
    }

    /// <summary>
    /// ログエントリをファイルから読み込み
    /// </summary>
    private async Task<List<LogEntry>> ReadLogEntriesAsync(string filePath, LogSearchCriteria criteria)
    {
        var entries = new List<LogEntry>();

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<LogEntry>(line, _jsonOptions);
                    if (entry == null) continue;

                    // 検索条件に一致するかチェック
                    if (MatchesCriteria(entry, criteria))
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // 無効なログ行はスキップ
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read log file {filePath}: {ex.Message}");
        }

        return entries;
    }

    /// <summary>
    /// ログエントリが検索条件に一致するかチェック
    /// </summary>
    private static bool MatchesCriteria(LogEntry entry, LogSearchCriteria criteria)
    {
        if (criteria.StartTime.HasValue && entry.Timestamp < criteria.StartTime.Value) return false;
        if (criteria.EndTime.HasValue && entry.Timestamp > criteria.EndTime.Value) return false;
        if (criteria.MinLevel.HasValue && entry.Level < criteria.MinLevel.Value) return false;
        if (!string.IsNullOrEmpty(criteria.Category) && !entry.Category.Equals(criteria.Category, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(criteria.EventType) && !entry.EventType?.Equals(criteria.EventType, StringComparison.OrdinalIgnoreCase) == true) return false;
        if (!string.IsNullOrEmpty(criteria.MessagePattern) && !entry.Message.Contains(criteria.MessagePattern, StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    /// <summary>
    /// ログ統計を取得
    /// </summary>
    public async Task<LogStatistics> GetLogStatisticsAsync()
    {
        var stats = new LogStatistics();

        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "aeroDriver_*.log");
            stats.TotalFiles = logFiles.Length;
            stats.TotalSize = logFiles.Sum(f => new FileInfo(f).Length);

            // 最新のログファイルから統計を収集
            if (logFiles.Length > 0)
            {
                var latestFile = logFiles.OrderByDescending(f => f).First();
                var entries = await ReadLogEntriesAsync(latestFile, new LogSearchCriteria { MaxResults = 1000 });

                stats.TotalEntries = entries.Count;
                stats.EntriesByLevel = entries.GroupBy(e => e.Level)
                    .ToDictionary(g => g.Key, g => g.Count());
                stats.EntriesByCategory = entries.GroupBy(e => e.Category)
                    .ToDictionary(g => g.Key, g => g.Count());
                stats.LastEntryTime = entries.OrderByDescending(e => e.Timestamp).FirstOrDefault()?.Timestamp;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to get log statistics: {ex.Message}");
        }

        return stats;
    }

    public void Dispose()
    {
        if (_disposed) return;

        FlushLogs();
        _flushTimer?.Dispose();
        _cleanupTimer?.Dispose();
        _writeSemaphore.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ログエントリ
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? EventType { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public int ThreadId { get; set; }
    public string UserIdentity { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// ログレベル
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    Security = 6,
    Performance = 7
}

/// <summary>
/// ログ検索条件
/// </summary>
public class LogSearchCriteria
{
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public LogLevel? MinLevel { get; set; }
    public string? Category { get; set; }
    public string? EventType { get; set; }
    public string? MessagePattern { get; set; }
    public int MaxResults { get; set; } = 1000;
}

/// <summary>
/// ログ統計情報
/// </summary>
public class LogStatistics
{
    public int TotalFiles { get; set; }
    public long TotalSize { get; set; }
    public int TotalEntries { get; set; }
    public DateTime? LastEntryTime { get; set; }
    public Dictionary<LogLevel, int> EntriesByLevel { get; set; } = new();
    public Dictionary<string, int> EntriesByCategory { get; set; } = new();
}

/// <summary>
/// ログシンクインターフェース
/// </summary>
public interface ILogSink
{
    Task WriteLogAsync(LogEntry logEntry);
}

/// <summary>
/// コンソールログシンク
/// </summary>
public class ConsoleLogSink : ILogSink
{
    public async Task WriteLogAsync(LogEntry logEntry)
    {
        var color = logEntry.Level switch
        {
            LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Security => ConsoleColor.Magenta,
            _ => ConsoleColor.Gray
        };

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;

        Console.WriteLine($"[{logEntry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{logEntry.Level}] {logEntry.Category}: {logEntry.Message}");

        Console.ForegroundColor = originalColor;

        if (!string.IsNullOrEmpty(logEntry.Exception))
        {
            Console.WriteLine($"  Exception: {logEntry.Exception}");
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// ファイルログシンク
/// </summary>
public class FileLogSink : ILogSink
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileLogSink(string filePath)
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task WriteLogAsync(LogEntry logEntry)
    {
        var json = JsonSerializer.Serialize(logEntry, _jsonOptions);
        await File.AppendAllTextAsync(_filePath, json + Environment.NewLine);
    }
}

/// <summary>
/// データベースログシンク（将来の実装用）
/// </summary>
public class DatabaseLogSink : ILogSink
{
    public async Task WriteLogAsync(LogEntry logEntry)
    {
        // データベースへのログ保存を実装
        // ここでは簡易的な実装を示す
        await Task.CompletedTask;
    }
}
