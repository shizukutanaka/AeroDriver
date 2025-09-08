using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    public interface IPerformanceMonitor
    {
        IDisposable StartOperation(string operationName);
        Task<T> MeasureAsync<T>(string operationName, Func<Task<T>> operation);
        Task MeasureAsync(string operationName, Func<Task> operation);
        void RecordOperation(string operationName, long elapsedMs, bool success);
        OperationMetrics? GetMetrics(string operationName);
        Dictionary<string, OperationMetrics> GetAllMetrics();
        PerformanceSummary GetSummary();
        void Reset();
    }
}