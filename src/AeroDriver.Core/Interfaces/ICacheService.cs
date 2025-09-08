namespace AeroDriver.Core.Interfaces
{
    public interface ICacheService
    {
        void Set<T>(string key, T value, TimeSpan? expiry = null);
        T? Get<T>(string key);
        bool TryGet<T>(string key, out T? value);
        void Remove(string key);
        void Clear();
        void ClearExpired();
        
        // Performance monitoring
        int Count { get; }
        bool HasKey(string key);
        
        // Async operations for heavy objects
        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
        Task<T?> GetAsync<T>(string key);
        
        // Cache statistics
        (int Total, int Expired, long MemoryUsage) GetStatistics();
    }
}