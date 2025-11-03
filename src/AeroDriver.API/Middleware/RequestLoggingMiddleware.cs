using Microsoft.AspNetCore.Http;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AeroDriver.API.Middleware;

/// <summary>
/// Request logging middleware for audit and security monitoring
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        // Add request ID to response headers for tracing
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-ID"] = requestId;
            return Task.CompletedTask;
        });

        try
        {
            // Log incoming request
            LogRequest(context, requestId);

            // Capture original response body stream
            var originalBodyStream = context.Response.Body;

            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            // Execute the request
            await _next(context);

            stopwatch.Stop();

            // Log response
            LogResponse(context, requestId, stopwatch.ElapsedMilliseconds);

            // Copy response to original stream
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogError(context, requestId, ex, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private void LogRequest(HttpContext context, string requestId)
    {
        var request = context.Request;

        _logger.LogInformation(
            "HTTP {Method} {Path} started | RequestId: {RequestId} | IP: {IP} | User: {User} | UserAgent: {UserAgent}",
            request.Method,
            request.Path,
            requestId,
            context.Connection.RemoteIpAddress,
            context.User?.Identity?.Name ?? "Anonymous",
            request.Headers["User-Agent"].ToString()
        );

        // Log query string (sanitized)
        if (request.QueryString.HasValue)
        {
            _logger.LogDebug(
                "Query string: {QueryString} | RequestId: {RequestId}",
                SanitizeQueryString(request.QueryString.Value),
                requestId
            );
        }
    }

    private void LogResponse(HttpContext context, string requestId, long durationMs)
    {
        var response = context.Response;

        var logLevel = response.StatusCode >= 500 ? LogLevel.Error :
                      response.StatusCode >= 400 ? LogLevel.Warning :
                      LogLevel.Information;

        _logger.Log(
            logLevel,
            "HTTP {Method} {Path} completed | RequestId: {RequestId} | Status: {StatusCode} | Duration: {Duration}ms | Size: {Size}bytes",
            context.Request.Method,
            context.Request.Path,
            requestId,
            response.StatusCode,
            durationMs,
            response.Body.Length
        );

        // Log slow requests
        if (durationMs > 1000)
        {
            _logger.LogWarning(
                "Slow request detected | RequestId: {RequestId} | Path: {Path} | Duration: {Duration}ms",
                requestId,
                context.Request.Path,
                durationMs
            );
        }
    }

    private void LogError(HttpContext context, string requestId, Exception ex, long durationMs)
    {
        _logger.LogError(
            ex,
            "HTTP {Method} {Path} failed | RequestId: {RequestId} | Status: {StatusCode} | Duration: {Duration}ms | Error: {Error}",
            context.Request.Method,
            context.Request.Path,
            requestId,
            context.Response.StatusCode,
            durationMs,
            ex.Message
        );
    }

    private static string SanitizeQueryString(string queryString)
    {
        // Remove sensitive information from query string
        var sensitiveParams = new[] { "password", "token", "apikey", "secret", "key" };

        foreach (var param in sensitiveParams)
        {
            var pattern = $"{param}=[^&]*";
            queryString = System.Text.RegularExpressions.Regex.Replace(
                queryString,
                pattern,
                $"{param}=***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }

        return queryString;
    }
}

/// <summary>
/// Extension methods for request logging middleware
/// </summary>
public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestLoggingMiddleware>();
    }
}
