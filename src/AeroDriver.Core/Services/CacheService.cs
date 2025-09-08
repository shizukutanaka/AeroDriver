using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Models;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Services
{
    public class CacheService : ICacheService, IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheItem> _cache;
        private readonly ILogger<CacheService>? _logger;
        private readonly Timer _cleanupTimer;
        private readonly int _maxItems;
        private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);

        public CacheService(ILogger<CacheService>? logger = null, int maxItems = 1000)
        {
            _cache = new ConcurrentDictionary<string, CacheItem>();
            _logger = logger;
            _maxItems = maxItems;
            
            // Cleanup timer to remove expired items
            _cleanupTimer = new Timer(CleanupCallback, null, CleanupInterval, CleanupInterval);
        }

        public void Set<T>(string key, T value, TimeSpan? expiry = null)
        {
            if (string.IsNullOrEmpty(key))
                return;

            // Enforce cache size limit
            if (_cache.Count >= _maxItems)
            {
                EvictOldestItems();
            }

            var expiryTime = expiry.HasValue 
                ? DateTime.UtcNow.Add(expiry.Value)
                : DateTime.UtcNow.Add(DefaultExpiry);

            var cacheItem = new CacheItem(value, expiryTime);
            _cache.AddOrUpdate(key, cacheItem, (_, _) => cacheItem);

            _logger?.LogTrace("Cache item set: {Key} (expires: {Expiry})", key, expiryTime);
        }

        public T? Get<T>(string key)
        {
            TryGet<T>(key, out var value);
            return value;
        }

        public bool TryGet<T>(string key, out T? value)
        {
            value = default;

            if (string.IsNullOrEmpty(key) || !_cache.TryGetValue(key, out var cacheItem))
                return false;

            if (cacheItem.IsExpired)
            {
                _cache.TryRemove(key, out _);
                _logger?.LogTrace("Cache item expired and removed: {Key}", key);
                return false;
            }

            if (cacheItem.Value is T castValue)
            {
                cacheItem.LastUsed = DateTime.UtcNow;
                value = castValue;
                _logger?.LogTrace("Cache hit: {Key}", key);
                return true;
            }

            _logger?.LogTrace("Cache miss (type mismatch): {Key}", key);
            return false;
        }

        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            _cache.TryRemove(key, out _);
            _logger?.LogTrace("Cache item removed: {Key}", key);
        }

        public void Clear()
        {
            _cache.Clear();
            _logger?.LogTrace("Cache cleared");
        }

        public void ClearExpired()
        {
            var expiredCount = 0;
            var keysToRemove = new List<string>();
            var now = DateTime.UtcNow;
            
            foreach (var kvp in _cache)
            {
                if (kvp.Value.ExpiresAt <= now)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_cache.TryRemove(key, out _))
                {
                    expiredCount++;
                }
            }

            if (expiredCount > 0)
            {
                _logger?.LogTrace("Cleared {Count} expired cache items", expiredCount);
            }
        }

        private void CleanupCallback(object? state)
        {
            try
            {
                ClearExpired();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during cache cleanup");
            }
        }

        private class CacheItem
        {
            private volatile DateTime _lastUsed;
            
            public object? Value { get; }
            public DateTime ExpiresAt { get; }
            public DateTime CreatedAt { get; }
            public DateTime LastUsed 
            { 
                get => _lastUsed;
                set => _lastUsed = value;
            }
            public bool IsExpired => DateTime.UtcNow > ExpiresAt;

            public CacheItem(object? value, DateTime expiresAt)
            {
                Value = value;
                ExpiresAt = expiresAt;
                CreatedAt = DateTime.UtcNow;
                _lastUsed = DateTime.UtcNow;
            }
        }

        // New performance monitoring methods
        public int Count => _cache.Count;
        
        public bool HasKey(string key)
        {
            return !string.IsNullOrEmpty(key) && _cache.ContainsKey(key) && !_cache[key].IsExpired;
        }
        
        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            await Task.Run(() => Set(key, value, expiry));
        }
        
        public async Task<T?> GetAsync<T>(string key)
        {
            return await Task.FromResult(Get<T>(key));
        }
        
        public (int Total, int Expired, long MemoryUsage) GetStatistics()
        {
            var items = _cache.ToList();
            var total = items.Count;
            var expired = items.Count(kvp => kvp.Value.IsExpired);
            var memoryUsage = GC.GetTotalMemory(false);
            
            return (total, expired, memoryUsage);
        }
        
        private void EvictOldestItems(int itemsToEvict = 100)
        {
            // Use array-based sorting for better memory efficiency
            var candidates = new KeyValuePair<string, CacheItem>[_cache.Count];
            var index = 0;
            
            foreach (var kvp in _cache)
            {
                if (index >= candidates.Length) break;
                candidates[index++] = kvp;
            }
            
            // Sort by LastUsed (LRU) for better cache performance
            Array.Sort(candidates, 0, index, (x, y) => x.Value.LastUsed.CompareTo(y.Value.LastUsed));
            
            var actualEvictions = Math.Min(itemsToEvict, index);
            var evictedCount = 0;
            
            for (int i = 0; i < actualEvictions; i++)
            {
                if (_cache.TryRemove(candidates[i].Key, out _))
                {
                    evictedCount++;
                }
            }
            
            _logger?.LogTrace("Evicted {Count} LRU cache items", evictedCount);
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            Clear();
        }
    }

    public static class CacheKeys
    {
        public const string AllDrivers = "drivers:all";
        public const string AvailableUpdates = "updates:available";
        public const string SystemInfo = "system:info";
        
        public static string DriverInfo(string deviceId) => $"driver:{deviceId}";
        public static string DriverUpdates(string deviceId) => $"updates:{deviceId}";
    }
}