using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace AeroDriver.API.Middleware;

/// <summary>
/// シンプルで効果的なレート制限ミドルウェア
/// DDoS攻撃や過負荷からAPIを保護
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, ClientRequestTracker> _clients = new();
    private readonly int _requestsPerMinute;
    private readonly int _requestsPerSecond;

    public RateLimitingMiddleware(RequestDelegate next, int requestsPerMinute = 60, int requestsPerSecond = 10)
    {
        _next = next;
        _requestsPerMinute = requestsPerMinute;
        _requestsPerSecond = requestsPerSecond;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            await _next(context);
            return;
        }

        var tracker = _clients.GetOrAdd(clientId, _ => new ClientRequestTracker());

        if (!tracker.AllowRequest(_requestsPerSecond, _requestsPerMinute))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = "60";
            await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
            return;
        }

        // クリーンアップ: 古いトラッカーを定期的に削除
        if (DateTime.UtcNow.Second == 0 && _clients.Count > 1000)
        {
            CleanupStaleTrackers();
        }

        await _next(context);
    }

    private static string GetClientIdentifier(HttpContext context)
    {
        // X-Forwarded-For ヘッダーをチェック (プロキシ/ロードバランサー対応)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ips.Length > 0)
            {
                return ips[0];
            }
        }

        // 接続元IPアドレスを使用
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void CleanupStaleTrackers()
    {
        var staleThreshold = DateTime.UtcNow.AddMinutes(-5);

        foreach (var kvp in _clients)
        {
            if (kvp.Value.LastRequestTime < staleThreshold)
            {
                _clients.TryRemove(kvp.Key, out _);
            }
        }
    }

    private class ClientRequestTracker
    {
        private readonly ConcurrentQueue<DateTime> _requestTimes = new();
        private DateTime _lastSecondCheck = DateTime.UtcNow;
        private int _requestsThisSecond;

        public DateTime LastRequestTime { get; private set; } = DateTime.UtcNow;

        public bool AllowRequest(int requestsPerSecond, int requestsPerMinute)
        {
            var now = DateTime.UtcNow;
            LastRequestTime = now;

            // 秒単位のレート制限チェック
            if (now - _lastSecondCheck < TimeSpan.FromSeconds(1))
            {
                if (_requestsThisSecond >= requestsPerSecond)
                {
                    return false;
                }
                _requestsThisSecond++;
            }
            else
            {
                _lastSecondCheck = now;
                _requestsThisSecond = 1;
            }

            // 分単位のレート制限チェック
            _requestTimes.Enqueue(now);

            // 1分以上前のリクエストを削除
            while (_requestTimes.TryPeek(out var oldestTime) && now - oldestTime > TimeSpan.FromMinutes(1))
            {
                _requestTimes.TryDequeue(out _);
            }

            return _requestTimes.Count <= requestsPerMinute;
        }
    }
}

/// <summary>
/// レート制限ミドルウェアの拡張メソッド
/// </summary>
public static class RateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder builder, int requestsPerMinute = 60, int requestsPerSecond = 10)
    {
        return builder.UseMiddleware<RateLimitingMiddleware>(requestsPerMinute, requestsPerSecond);
    }
}
