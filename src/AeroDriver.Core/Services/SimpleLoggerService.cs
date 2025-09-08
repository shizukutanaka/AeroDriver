using System.Collections.Concurrent;
using System.Text;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Services
{
    public class SimpleLoggerService : ISimpleLogger, IDisposable
    {
        private readonly ConcurrentQueue<LogEntry> _logEntries;
        private readonly object _fileLock = new();
        private readonly string _logFilePath;
        private readonly int _maxMemoryEntries;
        private readonly Timer _flushTimer;

        public SimpleLoggerService(int maxMemoryEntries = 1000)
        {
            _logEntries = new ConcurrentQueue<LogEntry>();
            _maxMemoryEntries = maxMemoryEntries;

            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appDataPath, "AeroDriver", "logs");
            Directory.CreateDirectory(logDir);
            
            _logFilePath = Path.Combine(logDir, $"aerodriver_{DateTime.Now:yyyyMMdd}.log");

            // Flush logs to file every 30 seconds
            _flushTimer = new Timer(FlushToFile, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public void LogInfo(string message)
        {
            LogInternal(LogLevel.Info, message, null);
        }

        public void LogWarning(string message)
        {
            LogInternal(LogLevel.Warning, message, null);
        }

        public void LogError(string message)
        {
            LogInternal(LogLevel.Error, message, null);
        }

        public void LogError(Exception ex, string message)
        {
            LogInternal(LogLevel.Error, message, ex);
        }

        public void LogDebug(string message)
        {
            LogInternal(LogLevel.Debug, message, null);
        }

        public async Task<string[]> GetRecentLogsAsync(int count = 50)
        {
            var entries = _logEntries.ToArray()
                .TakeLast(count)
                .Select(FormatLogEntry)
                .ToArray();
            
            return await Task.FromResult(entries).ConfigureAwait(false);
        }

        public async Task ClearLogsAsync()
        {
            while (_logEntries.TryDequeue(out _)) { }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async Task SaveLogsToFileAsync(string filePath)
        {
            var logs = await GetRecentLogsAsync(int.MaxValue);
            await File.WriteAllLinesAsync(filePath, logs);
        }

        private void LogInternal(LogLevel level, string message, Exception? exception)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message,
                Exception = exception
            };

            _logEntries.Enqueue(entry);

            // Keep memory usage under control
            while (_logEntries.Count > _maxMemoryEntries && _logEntries.TryDequeue(out _)) { }

            // For errors and warnings, also write to console immediately
            if (level >= LogLevel.Warning)
            {
                Console.WriteLine(FormatLogEntry(entry));
            }
        }

        private void FlushToFile(object? state)
        {
            try
            {
                if (_logEntries.IsEmpty)
                    return;

                var entries = new List<LogEntry>();
                while (_logEntries.TryDequeue(out var entry))
                {
                    entries.Add(entry);
                }

                if (!entries.Any())
                    return;

                var logLines = entries.Select(FormatLogEntry);
                
                lock (_fileLock)
                {
                    File.AppendAllLines(_logFilePath, logLines);
                }

                // Re-queue recent entries for memory access
                var recentEntries = entries.TakeLast(Math.Min(_maxMemoryEntries / 2, entries.Count));
                foreach (var entry in recentEntries)
                {
                    _logEntries.Enqueue(entry);
                }
            }
            catch (Exception ex)
            {
                // Avoid infinite loops - just write to console if file logging fails
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR: Failed to flush logs to file: {ex.Message}");
            }
        }

        private static string FormatLogEntry(LogEntry entry)
        {
            var sb = new StringBuilder();
            sb.Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
            sb.Append($"{entry.Level.ToString().ToUpperInvariant().PadRight(7)} ");
            sb.Append(entry.Message);

            if (entry.Exception != null)
            {
                sb.AppendLine();
                sb.Append($"    Exception: {entry.Exception.GetType().Name}: {entry.Exception.Message}");
                if (!string.IsNullOrEmpty(entry.Exception.StackTrace))
                {
                    sb.AppendLine();
                    sb.Append($"    Stack: {entry.Exception.StackTrace.Split('\n').FirstOrDefault()?.Trim()}");
                }
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            _flushTimer?.Dispose();
            FlushToFile(null); // Final flush
        }

        private class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; } = "";
            public Exception? Exception { get; set; }
        }

        private enum LogLevel
        {
            Debug = 0,
            Info = 1,
            Warning = 2,
            Error = 3
        }
    }
}