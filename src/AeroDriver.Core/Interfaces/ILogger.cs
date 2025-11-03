using System;
using System.Threading.Tasks;

namespace AeroDriver.Core.Interfaces
{
    /// <summary>
    /// 標準的なロガーインターフェース
    /// </summary>
    public interface ILogger
    {
        void LogDebug(string message, object? context = null);
        void LogInformation(string message, object? context = null);
        void LogWarning(string message, object? context = null);
        void LogError(string message, object? context = null, Exception? exception = null);
        void LogCritical(string message, object? context = null, Exception? exception = null);

        Task LogDebugAsync(string message, object? context = null, CancellationToken cancellationToken = default);
        Task LogInformationAsync(string message, object? context = null, CancellationToken cancellationToken = default);
        Task LogWarningAsync(string message, object? context = null, CancellationToken cancellationToken = default);
        Task LogErrorAsync(string message, object? context = null, Exception? exception = null, CancellationToken cancellationToken = default);
        Task LogCriticalAsync(string message, object? context = null, Exception? exception = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// エラーハンドリングインターフェース
    /// </summary>
    public interface IErrorHandler
    {
        Task HandleErrorAsync(Exception exception, string? context = null, ErrorSeverity severity = ErrorSeverity.Error);
        Task<bool> TryExecuteAsync(Func<Task> operation, string? context = null, ErrorSeverity severity = ErrorSeverity.Error);
        Task<T?> TryExecuteAsync<T>(Func<Task<T>> operation, string? context = null, ErrorSeverity severity = ErrorSeverity.Error);
        void LogMetrics();
    }

    /// <summary>
    /// エラーの重大度
    /// </summary>
    public enum ErrorSeverity
    {
        Debug,
        Information,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// リトライポリシーインターフェース
    /// </summary>
    public interface IRetryPolicy
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);
        Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default);
    }
}
