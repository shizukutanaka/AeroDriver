namespace AeroDriver.Core.Interfaces
{
    public interface ISimpleLogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogError(Exception ex, string message);
        void LogDebug(string message);
        Task<string[]> GetRecentLogsAsync(int count = 50);
        Task ClearLogsAsync();
        Task SaveLogsToFileAsync(string filePath);
    }
}