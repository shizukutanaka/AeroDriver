using System;
using System.Threading.Tasks;
using AeroDriver.Core.Services;
using Xunit;

namespace AeroDriver.Core.Tests
{
    public class CacheServiceTests : IDisposable
    {
        private readonly CacheService _cacheService;

        public CacheServiceTests()
        {
            _cacheService = new CacheService();
        }

        [Fact]
        public void Set_And_Get_String_Value()
        {
            // Arrange
            const string key = "test_key";
            const string value = "test_value";

            // Act
            _cacheService.Set(key, value);
            var result = _cacheService.Get<string>(key);

            // Assert
            Assert.Equal(value, result);
        }

        [Fact]
        public void Set_And_TryGet_String_Value()
        {
            // Arrange
            const string key = "test_key";
            const string value = "test_value";

            // Act
            _cacheService.Set(key, value);
            var success = _cacheService.TryGet<string>(key, out var result);

            // Assert
            Assert.True(success);
            Assert.Equal(value, result);
        }

        [Fact]
        public void Get_NonExistent_Key_Returns_Default()
        {
            // Act
            var result = _cacheService.Get<string>("non_existent_key");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void TryGet_NonExistent_Key_Returns_False()
        {
            // Act
            var success = _cacheService.TryGet<string>("non_existent_key", out var result);

            // Assert
            Assert.False(success);
            Assert.Null(result);
        }

        [Fact]
        public void Set_With_Custom_Expiry()
        {
            // Arrange
            const string key = "test_key";
            const string value = "test_value";
            var expiry = TimeSpan.FromMilliseconds(100);

            // Act
            _cacheService.Set(key, value, expiry);
            var immediateResult = _cacheService.Get<string>(key);

            // Wait for expiry
            Task.Delay(150).Wait();
            var expiredResult = _cacheService.Get<string>(key);

            // Assert
            Assert.Equal(value, immediateResult);
            Assert.Null(expiredResult);
        }

        [Fact]
        public void Set_Different_Types()
        {
            // Arrange
            const string stringKey = "string_key";
            const string stringValue = "test";
            const string intKey = "int_key";
            const int intValue = 42;

            // Act
            _cacheService.Set(stringKey, stringValue);
            _cacheService.Set(intKey, intValue);

            var stringResult = _cacheService.Get<string>(stringKey);
            var intResult = _cacheService.Get<int>(intKey);

            // Assert
            Assert.Equal(stringValue, stringResult);
            Assert.Equal(intValue, intResult);
        }

        [Fact]
        public void Remove_Existing_Key()
        {
            // Arrange
            const string key = "test_key";
            const string value = "test_value";

            // Act
            _cacheService.Set(key, value);
            var beforeRemove = _cacheService.Get<string>(key);
            _cacheService.Remove(key);
            var afterRemove = _cacheService.Get<string>(key);

            // Assert
            Assert.Equal(value, beforeRemove);
            Assert.Null(afterRemove);
        }

        [Fact]
        public void Clear_Removes_All_Items()
        {
            // Arrange
            _cacheService.Set("key1", "value1");
            _cacheService.Set("key2", "value2");

            // Act
            var beforeClear1 = _cacheService.Get<string>("key1");
            var beforeClear2 = _cacheService.Get<string>("key2");
            
            _cacheService.Clear();
            
            var afterClear1 = _cacheService.Get<string>("key1");
            var afterClear2 = _cacheService.Get<string>("key2");

            // Assert
            Assert.Equal("value1", beforeClear1);
            Assert.Equal("value2", beforeClear2);
            Assert.Null(afterClear1);
            Assert.Null(afterClear2);
        }

        [Fact]
        public void Set_Null_Or_Empty_Key_Ignored()
        {
            // Act & Assert - Should not throw
            _cacheService.Set(null, "value");
            _cacheService.Set("", "value");
            _cacheService.Set(string.Empty, "value");
        }

        [Fact]
        public void ClearExpired_Removes_Only_Expired_Items()
        {
            // Arrange
            _cacheService.Set("persistent", "value1", TimeSpan.FromMinutes(1));
            _cacheService.Set("expiring", "value2", TimeSpan.FromMilliseconds(50));

            // Wait for one item to expire
            Task.Delay(100).Wait();

            // Act
            _cacheService.ClearExpired();

            // Assert
            Assert.Equal("value1", _cacheService.Get<string>("persistent"));
            Assert.Null(_cacheService.Get<string>("expiring"));
        }

        [Fact]
        public void Type_Mismatch_Returns_Default()
        {
            // Arrange
            const string key = "test_key";
            const string value = "string_value";

            // Act
            _cacheService.Set(key, value);
            var intResult = _cacheService.Get<int>(key);
            var success = _cacheService.TryGet<int>(key, out var tryGetResult);

            // Assert
            Assert.Equal(0, intResult); // Default for int
            Assert.False(success);
            Assert.Equal(0, tryGetResult);
        }

        [Fact]
        public void Count_ReturnsCorrectItemCount()
        {
            // Arrange & Act
            Assert.Equal(0, _cacheService.Count);
            
            _cacheService.Set("key1", "value1");
            Assert.Equal(1, _cacheService.Count);
            
            _cacheService.Set("key2", "value2");
            Assert.Equal(2, _cacheService.Count);
            
            _cacheService.Remove("key1");
            Assert.Equal(1, _cacheService.Count);
            
            _cacheService.Clear();
            Assert.Equal(0, _cacheService.Count);
        }

        [Fact]
        public void HasKey_ReturnsCorrectStatus()
        {
            // Arrange
            const string key = "test_key";
            
            // Act & Assert
            Assert.False(_cacheService.HasKey(key));
            
            _cacheService.Set(key, "value");
            Assert.True(_cacheService.HasKey(key));
            
            _cacheService.Remove(key);
            Assert.False(_cacheService.HasKey(key));
        }

        [Fact]
        public async Task SetAsync_And_GetAsync_WorkCorrectly()
        {
            // Arrange
            const string key = "async_key";
            const string value = "async_value";

            // Act
            await _cacheService.SetAsync(key, value);
            var result = await _cacheService.GetAsync<string>(key);

            // Assert
            Assert.Equal(value, result);
        }

        [Fact]
        public void GetStatistics_ReturnsValidData()
        {
            // Arrange
            _cacheService.Set("key1", "value1");
            _cacheService.Set("key2", "value2");

            // Act
            var (total, expired, memoryUsage) = _cacheService.GetStatistics();

            // Assert
            Assert.Equal(2, total);
            Assert.Equal(0, expired); // Items should not be expired immediately
            Assert.True(memoryUsage > 0);
        }

        [Fact]
        public void CacheEviction_WorksWithMaxItems()
        {
            // Arrange - Create cache with small limit
            using var smallCache = new CacheService(maxItems: 2);
            
            // Act - Add more items than the limit
            smallCache.Set("key1", "value1");
            smallCache.Set("key2", "value2");
            smallCache.Set("key3", "value3"); // Should trigger eviction
            
            // Assert - Should only have recent items
            Assert.Equal(2, smallCache.Count);
        }

        public void Dispose()
        {
            _cacheService?.Dispose();
        }
    }
}