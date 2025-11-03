using System;
using System.IO;
using System.Threading;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Logging
{
    /// <summary>
    /// シンプルなファイルロガー実装
    /// 研究ベースの高度な機能で使用
    /// </summary>
    public class SimpleLogger : ISimpleLogger, ILogger
    {
        private readonly string _logFilePath;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private const int MaxLogSizeMB = 10;

        public SimpleLogger()
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver", "Logs");
            Directory.CreateDirectory(logDir);

            _logFilePath = Path.Combine(logDir, $"AeroDriver_{DateTime.Now:yyyyMMdd}.log");
        }

        public SimpleLogger(string logFilePath)
        {
            _logFilePath = logFilePath;
            var dir = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public void LogDebug(string message, Exception? exception = null)
        {
            WriteLog("DEBUG", message, exception);
        }

        public void LogInformation(string message, Exception? exception = null)
        {
            WriteLog("INFO", message, exception);
        }

        public void LogWarning(string message, Exception? exception = null)
        {
            WriteLog("WARN", message, exception);
        }

        public void LogError(string message, Exception? exception = null)
        {
            WriteLog("ERROR", message, exception);
        }

        public void LogCritical(string message, Exception? exception = null)
        {
            WriteLog("CRITICAL", message, exception);
        }

        // ILogger implementation (compatibility)
        public void LogDebug(string message, object? context = null)
        {
            WriteLog("DEBUG", FormatWithContext(message, context), null);
        }

        public void LogInformation(string message, object? context = null)
        {
            WriteLog("INFO", FormatWithContext(message, context), null);
        }

        public void LogWarning(string message, object? context = null)
        {
            WriteLog("WARN", FormatWithContext(message, context), null);
        }

        public void LogError(string message, object? context = null, Exception? exception = null)
        {
            WriteLog("ERROR", FormatWithContext(message, context), exception);
        }

        public void LogCritical(string message, object? context = null, Exception? exception = null)
        {
            WriteLog("CRITICAL", FormatWithContext(message, context), exception);
        }

        // Async implementations for ILogger
        public async Task LogDebugAsync(string message, object? context = null, CancellationToken cancellationToken = default)
        {
            await WriteLogAsync("DEBUG", FormatWithContext(message, context), null, cancellationToken);
        }

        public async Task LogInformationAsync(string message, object? context = null, CancellationToken cancellationToken = default)
        {
            await WriteLogAsync("INFO", FormatWithContext(message, context), null, cancellationToken);
        }

        public async Task LogWarningAsync(string message, object? context = null, CancellationToken cancellationToken = default)
        {
            await WriteLogAsync("WARN", FormatWithContext(message, context), null, cancellationToken);
        }

        public async Task LogErrorAsync(string message, object? context = null, Exception? exception = null, CancellationToken cancellationToken = default)
        {
            await WriteLogAsync("ERROR", FormatWithContext(message, context), exception, cancellationToken);
        }

        public async Task LogCriticalAsync(string message, object? context = null, Exception? exception = null, CancellationToken cancellationToken = default)
        {
            await WriteLogAsync("CRITICAL", FormatWithContext(message, context), exception, cancellationToken);
        }

        private string FormatWithContext(string message, object? context)
        {
            if (context == null)
                return message;

            return $"{message} | Context: {System.Text.Json.JsonSerializer.Serialize(context)}";
        }

        private void WriteLog(string level, string message, Exception? exception)
        {
            try
            {
                _writeLock.Wait();
                try
                {
                    CheckAndRotateLog();

                    var logEntry = FormatLogEntry(level, message, exception);
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch
            {
                // Suppress logging errors to avoid cascading failures
            }
        }

        private async Task WriteLogAsync(string level, string message, Exception? exception, CancellationToken ct)
        {
            try
            {
                await _writeLock.WaitAsync(ct);
                try
                {
                    CheckAndRotateLog();

                    var logEntry = FormatLogEntry(level, message, exception);
                    await File.AppendAllTextAsync(_logFilePath, logEntry + Environment.NewLine, ct);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch
            {
                // Suppress logging errors to avoid cascading failures
            }
        }

        private string FormatLogEntry(string level, string message, Exception? exception)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var entry = $"[{timestamp}] [{level}] {message}";

            if (exception != null)
            {
                entry += $"{Environment.NewLine}Exception: {exception.GetType().Name}: {exception.Message}";
                entry += $"{Environment.NewLine}StackTrace: {exception.StackTrace}";
            }

            return entry;
        }

        private void CheckAndRotateLog()
        {
            if (!File.Exists(_logFilePath))
                return;

            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length > MaxLogSizeMB * 1024 * 1024)
            {
                var archivePath = $"{_logFilePath}.{DateTime.Now:yyyyMMddHHmmss}.old";
                File.Move(_logFilePath, archivePath);
            }
        }

        public void Dispose()
        {
            _writeLock?.Dispose();
        }
    }
}
