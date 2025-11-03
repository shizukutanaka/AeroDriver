using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace AeroDriver.API.Middleware;

/// <summary>
/// Authentication middleware for AeroDriver API
/// Provides multiple authentication methods for enterprise environments
/// </summary>
public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationMiddleware> _logger;
    private readonly AuthenticationConfiguration _config;

    public AuthenticationMiddleware(
        RequestDelegate next,
        ILogger<AuthenticationMiddleware> _logger,
        AuthenticationConfiguration config)
    {
        _next = next;
        this._logger = _logger;
        _config = config;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for health check endpoints
        if (IsPublicEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Check if authentication is disabled (development only)
        if (!_config.RequireAuthentication)
        {
            _logger.LogWarning("Authentication is disabled. This should only be used in development.");
            await _next(context);
            return;
        }

        try
        {
            // Try Windows Authentication first (recommended for enterprise)
            if (_config.EnableWindowsAuthentication && await TryWindowsAuthentication(context))
            {
                await _next(context);
                return;
            }

            // Try API Key authentication
            if (_config.EnableApiKeyAuthentication && await TryApiKeyAuthentication(context))
            {
                await _next(context);
                return;
            }

            // Try Basic Authentication
            if (_config.EnableBasicAuthentication && await TryBasicAuthentication(context))
            {
                await _next(context);
                return;
            }

            // No valid authentication found
            _logger.LogWarning("Unauthorized access attempt from {IP} to {Path}",
                context.Connection.RemoteIpAddress, context.Request.Path);

            context.Response.StatusCode = 401;
            context.Response.Headers.Append("WWW-Authenticate", "Windows, ApiKey, Basic");
            await context.Response.WriteAsync("Authentication required");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error occurred");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Authentication error");
        }
    }

    private static bool IsPublicEndpoint(PathString path)
    {
        var publicPaths = new[]
        {
            "/api/health",
            "/api/alive",
            "/api/ready",
            "/swagger"
        };

        return publicPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> TryWindowsAuthentication(HttpContext context)
    {
        if (context.User?.Identity is WindowsIdentity windowsIdentity && windowsIdentity.IsAuthenticated)
        {
            // Validate user is in allowed groups
            if (_config.AllowedWindowsGroups.Any())
            {
                var isAuthorized = _config.AllowedWindowsGroups.Any(group =>
                    windowsIdentity.Groups?.Any(g =>
                        g.Translate(typeof(NTAccount)).Value.Equals(group, StringComparison.OrdinalIgnoreCase)) == true);

                if (!isAuthorized)
                {
                    _logger.LogWarning("User {User} not in allowed Windows groups",
                        windowsIdentity.Name);
                    return false;
                }
            }

            _logger.LogInformation("Windows authentication successful for {User}",
                windowsIdentity.Name);

            SetAuthenticatedUser(context, windowsIdentity.Name, "Windows");
            return true;
        }

        return false;
    }

    private async Task<bool> TryApiKeyAuthentication(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("X-API-Key", out var apiKey))
        {
            return false;
        }

        var keyValue = apiKey.ToString();
        if (string.IsNullOrWhiteSpace(keyValue))
        {
            return false;
        }

        // Validate API key
        if (_config.ValidApiKeys.Contains(keyValue, StringComparer.Ordinal))
        {
            _logger.LogInformation("API key authentication successful from {IP}",
                context.Connection.RemoteIpAddress);

            SetAuthenticatedUser(context, "ApiKeyUser", "ApiKey");
            return true;
        }

        _logger.LogWarning("Invalid API key attempt from {IP}",
            context.Connection.RemoteIpAddress);
        return false;
    }

    private async Task<bool> TryBasicAuthentication(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return false;
        }

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var encodedCredentials = headerValue.Substring("Basic ".Length).Trim();
            var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
            var credentials = decodedCredentials.Split(':', 2);

            if (credentials.Length != 2)
            {
                return false;
            }

            var username = credentials[0];
            var password = credentials[1];

            // Validate credentials
            if (_config.BasicAuthenticationValidator?.Invoke(username, password) == true)
            {
                _logger.LogInformation("Basic authentication successful for {User}", username);
                SetAuthenticatedUser(context, username, "Basic");
                return true;
            }

            _logger.LogWarning("Invalid basic authentication attempt for {User} from {IP}",
                username, context.Connection.RemoteIpAddress);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Basic authentication parsing error");
            return false;
        }
    }

    private static void SetAuthenticatedUser(HttpContext context, string username, string authenticationType)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.AuthenticationMethod, authenticationType),
            new Claim("AuthenticatedAt", DateTime.UtcNow.ToString("O"))
        };

        var identity = new ClaimsIdentity(claims, authenticationType);
        context.User = new ClaimsPrincipal(identity);
    }
}

/// <summary>
/// Configuration for authentication middleware
/// </summary>
public class AuthenticationConfiguration
{
    /// <summary>
    /// Require authentication for API access (disable only for development)
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// Enable Windows Authentication (recommended for enterprise)
    /// </summary>
    public bool EnableWindowsAuthentication { get; set; } = true;

    /// <summary>
    /// Enable API Key authentication
    /// </summary>
    public bool EnableApiKeyAuthentication { get; set; } = true;

    /// <summary>
    /// Enable Basic Authentication (use with HTTPS only)
    /// </summary>
    public bool EnableBasicAuthentication { get; set; } = false;

    /// <summary>
    /// Allowed Windows groups (empty = allow all authenticated users)
    /// </summary>
    public List<string> AllowedWindowsGroups { get; set; } = new();

    /// <summary>
    /// Valid API keys for authentication
    /// </summary>
    public HashSet<string> ValidApiKeys { get; set; } = new();

    /// <summary>
    /// Custom validator for basic authentication
    /// </summary>
    public Func<string, string, bool>? BasicAuthenticationValidator { get; set; }

    /// <summary>
    /// Create default configuration for production use
    /// </summary>
    public static AuthenticationConfiguration CreateProduction()
    {
        return new AuthenticationConfiguration
        {
            RequireAuthentication = true,
            EnableWindowsAuthentication = true,
            EnableApiKeyAuthentication = true,
            EnableBasicAuthentication = false, // Disable basic auth in production
            AllowedWindowsGroups = new List<string>
            {
                "BUILTIN\\Administrators",
                "Domain Admins"
            }
        };
    }

    /// <summary>
    /// Create development configuration (authentication disabled)
    /// </summary>
    public static AuthenticationConfiguration CreateDevelopment()
    {
        return new AuthenticationConfiguration
        {
            RequireAuthentication = false,
            EnableWindowsAuthentication = false,
            EnableApiKeyAuthentication = false,
            EnableBasicAuthentication = false
        };
    }
}

/// <summary>
/// Extension methods for authentication middleware
/// </summary>
public static class AuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseAeroDriverAuthentication(
        this IApplicationBuilder builder,
        AuthenticationConfiguration? config = null)
    {
        config ??= AuthenticationConfiguration.CreateProduction();
        return builder.UseMiddleware<AuthenticationMiddleware>(config);
    }
}
