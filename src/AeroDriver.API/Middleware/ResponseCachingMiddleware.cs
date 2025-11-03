using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AeroDriver.API.Middleware;

/// <summary>
/// Response caching middleware for high-performance API responses
/// Implements intelligent caching with cache invalidation support
/// </summary>
public class ResponseCachingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ResponseCacheOptions _options;
    private static readonly ConcurrentDictionary<string, CachedResponse> _cache = new();
    private static readonly System.Threading.Timer _cleanupTimer;

    static ResponseCachingMiddleware()
    {
        _cleanupTimer = new System.Threading.Timer(
            CleanupExpiredEntries,
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5)
        );
    }

    public ResponseCachingMiddleware(RequestDelegate next, ResponseCacheOptions? options = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? ResponseCacheOptions.Default;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || !ShouldCache(context))
        {
            await _next(context);
            return;
        }

        var cacheKey = GenerateCacheKey(context);

        // Try to serve from cache
        if (_cache.TryGetValue(cacheKey, out var cachedResponse) && !cachedResponse.IsExpired())
        {
            context.Response.Headers.Add("X-Cache", "HIT");
            context.Response.Headers.Add("X-Cache-Age", cachedResponse.Age.TotalSeconds.ToString("F0"));
            context.Response.StatusCode = cachedResponse.StatusCode;
            context.Response.ContentType = cachedResponse.ContentType;

            foreach (var header in cachedResponse.Headers)
            {
                context.Response.Headers.Add(header.Key, header.Value);
            }

            await context.Response.Body.WriteAsync(cachedResponse.Body);
            return;
        }

        // Cache miss - capture response
        context.Response.Headers.Add("X-Cache", "MISS");

        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        // Only cache successful GET requests
        if (context.Request.Method == "GET" && context.Response.StatusCode == 200)
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            var responseBytes = responseBody.ToArray();

            var cached = new CachedResponse
            {
                StatusCode = context.Response.StatusCode,
                ContentType = context.Response.ContentType ?? "application/json",
                Body = responseBytes,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(_options.DefaultDuration),
                Headers = context.Response.Headers
                    .Where(h => !h.Key.StartsWith("X-Cache"))
                    .ToDictionary(h => h.Key, h => h.Value.ToString())
            };

            _cache.TryAdd(cacheKey, cached);
        }

        responseBody.Seek(0, SeekOrigin.Begin);
        await responseBody.CopyToAsync(originalBodyStream);
    }

    private bool ShouldCache(HttpContext context)
    {
        // Only cache GET requests
        if (context.Request.Method != "GET")
        {
            return false;
        }

        // Don't cache authenticated requests (could leak sensitive data)
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            return false;
        }

        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Don't cache these endpoints
        if (_options.ExcludedPaths.Any(p => path.Contains(p.ToLowerInvariant())))
        {
            return false;
        }

        // Only cache specific endpoints
        if (_options.CachedPaths.Any())
        {
            return _options.CachedPaths.Any(p => path.Contains(p.ToLowerInvariant()));
        }

        return true;
    }

    private static string GenerateCacheKey(HttpContext context)
    {
        var key = new StringBuilder();
        key.Append(context.Request.Method);
        key.Append('|');
        key.Append(context.Request.Path);
        key.Append('|');
        key.Append(context.Request.QueryString);

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(key.ToString()));
        return Convert.ToBase64String(hash);
    }

    private static void CleanupExpiredEntries(object? state)
    {
        try
        {
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.IsExpired())
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    public static void ClearCache()
    {
        _cache.Clear();
    }

    public static void InvalidateCache(string pattern)
    {
        var keysToRemove = _cache.Keys
            .Where(k => k.Contains(pattern))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private class CachedResponse
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public byte[] Body { get; set; } = Array.Empty<byte>();
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new();

        public bool IsExpired() => DateTimeOffset.UtcNow >= ExpiresAt;
        public TimeSpan Age => DateTimeOffset.UtcNow - CreatedAt;
    }
}

public class ResponseCacheOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(5);
    public string[] CachedPaths { get; set; } = Array.Empty<string>();
    public string[] ExcludedPaths { get; set; } = new[]
    {
        "/health",
        "/ready",
        "/alive",
        "/metrics"
    };

    public static ResponseCacheOptions Default => new()
    {
        Enabled = true,
        DefaultDuration = TimeSpan.FromMinutes(5),
        CachedPaths = new[]
        {
            "/api/drivers",
            "/api/system/status",
            "/api/performance"
        },
        ExcludedPaths = new[]
        {
            "/health",
            "/ready",
            "/alive",
            "/metrics",
            "/api/drivers/scan",
            "/api/drivers/update"
        }
    };
}
