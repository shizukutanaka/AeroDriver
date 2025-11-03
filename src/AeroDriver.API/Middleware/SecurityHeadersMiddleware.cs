using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace AeroDriver.API.Middleware;

/// <summary>
/// Security headers middleware to protect against common web vulnerabilities
/// Implements OWASP security best practices
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Security headers to prevent common attacks
        var headers = context.Response.Headers;

        // Prevent clickjacking attacks
        headers["X-Frame-Options"] = "DENY";

        // Prevent MIME type sniffing
        headers["X-Content-Type-Options"] = "nosniff";

        // Enable XSS protection in browsers
        headers["X-XSS-Protection"] = "1; mode=block";

        // Enforce HTTPS
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";

        // Content Security Policy - restrictive policy
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none';";

        // Referrer Policy - don't leak URLs
        headers["Referrer-Policy"] = "no-referrer";

        // Permissions Policy - disable unnecessary features
        headers["Permissions-Policy"] =
            "geolocation=(), " +
            "microphone=(), " +
            "camera=(), " +
            "payment=(), " +
            "usb=(), " +
            "magnetometer=(), " +
            "gyroscope=(), " +
            "accelerometer=()";

        // Remove server information disclosure
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        headers.Remove("X-AspNet-Version");
        headers.Remove("X-AspNetMvc-Version");

        // Add custom security header for AeroDriver
        headers["X-AeroDriver-Security"] = "enabled";

        // Add cache control for sensitive endpoints
        if (context.Request.Path.StartsWithSegments("/api") &&
            !context.Request.Path.StartsWithSegments("/api/health"))
        {
            headers["Cache-Control"] = "no-store, no-cache, must-revalidate, private";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for security headers middleware
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
