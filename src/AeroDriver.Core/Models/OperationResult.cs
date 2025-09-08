namespace AeroDriver.Core.Models
{
    /// <summary>
    /// 操作結果を表すジェネリッククラス
    /// </summary>
    public class OperationResult<T>
    {
        public bool Success { get; }
        public T? Data { get; }
        public string? ErrorMessage { get; }
        public string? ErrorCode { get; }
        public Exception? Exception { get; }
        public DateTime Timestamp { get; }
        
        protected OperationResult(bool success, T? data, string? errorMessage, string? errorCode, Exception? exception)
        {
            Success = success;
            Data = data;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
            Exception = exception;
            Timestamp = DateTime.UtcNow;
        }
        
        public static OperationResult<T> CreateSuccess(T data)
        {
            return new OperationResult<T>(true, data, null, null, null);
        }
        
        public static OperationResult<T> CreateFailure(string errorMessage, string? errorCode = null)
        {
            return new OperationResult<T>(false, default, errorMessage, errorCode, null);
        }
        
        public static OperationResult<T> CreateFailure(Exception exception, string? errorCode = null)
        {
            return new OperationResult<T>(false, default, exception.Message, errorCode, exception);
        }
        
        public OperationResult<TNew> Map<TNew>(Func<T, TNew> mapper)
        {
            if (Success && Data != null)
            {
                return OperationResult<TNew>.CreateSuccess(mapper(Data));
            }
            return OperationResult<TNew>.CreateFailure(ErrorMessage ?? "Mapping failed", ErrorCode);
        }
        
        public async Task<OperationResult<TNew>> MapAsync<TNew>(Func<T, Task<TNew>> mapper)
        {
            if (Success && Data != null)
            {
                var newData = await mapper(Data);
                return OperationResult<TNew>.CreateSuccess(newData);
            }
            return OperationResult<TNew>.CreateFailure(ErrorMessage ?? "Mapping failed", ErrorCode);
        }
    }
    
    /// <summary>
    /// 戻り値のない操作結果
    /// </summary>
    public class OperationResult
    {
        public bool Success { get; }
        public string? ErrorMessage { get; }
        public string? ErrorCode { get; }
        public Exception? Exception { get; }
        public DateTime Timestamp { get; }
        
        protected OperationResult(bool success, string? errorMessage, string? errorCode, Exception? exception)
        {
            Success = success;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
            Exception = exception;
            Timestamp = DateTime.UtcNow;
        }
        
        public static OperationResult CreateSuccess()
        {
            return new OperationResult(true, null, null, null);
        }
        
        public static OperationResult CreateFailure(string errorMessage, string? errorCode = null)
        {
            return new OperationResult(false, errorMessage, errorCode, null);
        }
        
        public static OperationResult CreateFailure(Exception exception, string? errorCode = null)
        {
            return new OperationResult(false, exception.Message, errorCode, exception);
        }
        
        public static implicit operator bool(OperationResult result)
        {
            return result.Success;
        }
    }
}