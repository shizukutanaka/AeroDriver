using System.Diagnostics;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Exceptions;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// グローバルエラーハンドリングサービス
    /// </summary>
    public class ErrorHandlerService : IErrorHandler
    {
        private readonly ILogger<ErrorHandlerService>? _logger;
        private readonly ISimpleLogger? _simpleLogger;
        private readonly Dictionary<string, int> _errorCounts = new();
        private readonly object _lockObject = new();
        
        public ErrorHandlerService(ILogger<ErrorHandlerService>? logger = null, ISimpleLogger? simpleLogger = null)
        {
            _logger = logger;
            _simpleLogger = simpleLogger;
        }
        
        public async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            Func<Exception, T>? fallbackHandler = null)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger?.LogDebug("Starting operation: {OperationName}", operationName);
                var result = await operation().ConfigureAwait(false);
                
                stopwatch.Stop();
                _logger?.LogDebug("Operation {OperationName} completed in {ElapsedMs}ms", 
                    operationName, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await HandleErrorAsync(ex, operationName);
                
                if (fallbackHandler != null)
                {
                    _logger?.LogWarning("Executing fallback handler for {OperationName}", operationName);
                    return fallbackHandler(ex);
                }
                
                throw;
            }
        }
        
        public async Task ExecuteAsync(
            Func<Task> operation,
            string operationName,
            Action<Exception>? fallbackHandler = null)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger?.LogDebug("Starting operation: {OperationName}", operationName);
                await operation().ConfigureAwait(false);
                
                stopwatch.Stop();
                _logger?.LogDebug("Operation {OperationName} completed in {ElapsedMs}ms", 
                    operationName, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await HandleErrorAsync(ex, operationName);
                
                if (fallbackHandler != null)
                {
                    _logger?.LogWarning("Executing fallback handler for {OperationName}", operationName);
                    fallbackHandler(ex);
                }
                else
                {
                    throw;
                }
            }
        }
        
        public async Task HandleErrorAsync(Exception exception, string context)
        {
            var errorCode = GetErrorCode(exception);
            IncrementErrorCount(errorCode);
            
            // Log based on exception type
            switch (exception)
            {
                case InsufficientPrivilegesException:
                    _logger?.LogWarning(exception, "Insufficient privileges for {Context}", context);
                    _simpleLogger?.LogWarning($"Administrator privileges required for {context}");
                    break;
                    
                case DriverNotFoundException dnf:
                    _logger?.LogWarning("Driver not found: {DeviceId} in {Context}", dnf.DeviceId, context);
                    _simpleLogger?.LogWarning($"Driver not found: {dnf.DeviceId}");
                    break;
                    
                case BackupException be:
                    _logger?.LogError(exception, "Backup error in {Context}: {DeviceId}", context, be.DeviceId);
                    _simpleLogger?.LogError($"Backup failed for {be.DeviceId}: {be.Message}");
                    break;
                    
                case UpdateException ue:
                    _logger?.LogError(exception, "Update error in {Context}: {DeviceId}", context, ue.DeviceId);
                    _simpleLogger?.LogError($"Update failed for {ue.DeviceId}: {ue.Message}");
                    break;
                    
                case WmiException:
                    _logger?.LogError(exception, "WMI error in {Context}", context);
                    _simpleLogger?.LogError($"System query failed: {exception.Message}");
                    break;
                    
                case AeroDriverException ade:
                    _logger?.LogError(exception, "Application error in {Context}: {ErrorCode}", context, ade.ErrorCode);
                    _simpleLogger?.LogError($"Error [{ade.ErrorCode}]: {ade.Message}");
                    break;
                    
                case OperationCanceledException:
                    _logger?.LogInformation("Operation cancelled: {Context}", context);
                    _simpleLogger?.LogInfo($"Operation cancelled: {context}");
                    break;
                    
                case TimeoutException:
                    _logger?.LogWarning(exception, "Operation timeout in {Context}", context);
                    _simpleLogger?.LogWarning($"Operation timed out: {context}");
                    break;
                    
                default:
                    _logger?.LogError(exception, "Unexpected error in {Context}", context);
                    _simpleLogger?.LogError($"Unexpected error in {context}: {exception.Message}");
                    break;
            }
            
            await Task.CompletedTask;
        }
        
        public string GetErrorCode(Exception exception)
        {
            return exception switch
            {
                AeroDriverException ade => ade.ErrorCode,
                OperationCanceledException => "OPERATION_CANCELLED",
                TimeoutException => "TIMEOUT",
                UnauthorizedAccessException => "ACCESS_DENIED",
                IOException => "IO_ERROR",
                OutOfMemoryException => "OUT_OF_MEMORY",
                _ => "UNKNOWN_ERROR"
            };
        }
        
        public int GetErrorCount(string errorCode)
        {
            lock (_lockObject)
            {
                return _errorCounts.TryGetValue(errorCode, out var count) ? count : 0;
            }
        }
        
        public Dictionary<string, int> GetErrorStatistics()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, int>(_errorCounts);
            }
        }
        
        public void ResetErrorStatistics()
        {
            lock (_lockObject)
            {
                _errorCounts.Clear();
            }
        }
        
        private void IncrementErrorCount(string errorCode)
        {
            lock (_lockObject)
            {
                if (_errorCounts.ContainsKey(errorCode))
                {
                    _errorCounts[errorCode]++;
                }
                else
                {
                    _errorCounts[errorCode] = 1;
                }
            }
        }
    }
}