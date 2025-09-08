namespace AeroDriver.Core.Interfaces
{
    public interface IErrorHandler
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, Func<Exception, T>? fallbackHandler = null);
        Task ExecuteAsync(Func<Task> operation, string operationName, Action<Exception>? fallbackHandler = null);
        Task HandleErrorAsync(Exception exception, string context);
        string GetErrorCode(Exception exception);
        int GetErrorCount(string errorCode);
        Dictionary<string, int> GetErrorStatistics();
        void ResetErrorStatistics();
    }
}