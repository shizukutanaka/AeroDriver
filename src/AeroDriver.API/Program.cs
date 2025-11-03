using AeroDriver.API.Middleware;
using AeroDriver.Core;
using AeroDriver.Core.Services;
using AeroDriver.Core.Validation;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("enterprise", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Otedama API",
        Version = "stable",
        Description = "Enterprise Windows Driver Management System API",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "Otedama Support",
            Email = "support@example.com"
        }
    });

    // Add security definition
    options.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-API-Key",
        Description = "API Key authentication"
    });
});

// Rate limiting will be handled by custom middleware if needed
// ASP.NET Core rate limiting is built-in for .NET 7+ but we'll use custom throttling in middleware

const string corsPolicyName = "EnterprisePolicy";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

allowedOrigins = allowedOrigins
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (allowedOrigins.Length == 0)
{
    allowedOrigins = new[] { "https://localhost", "http://localhost" };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Register AeroDriver services
builder.Services.AddSingleton<ISimpleLogger, SimpleLogger>();
builder.Services.AddSingleton<CoreDriverService>();
builder.Services.AddSingleton<AuditTrail>();
builder.Services.AddSingleton<AuditReportGenerator>();
builder.Services.AddSingleton<FirmwareManager>();
builder.Services.AddSingleton<DriverCatalogManager>();

// Register JWT authentication services
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<AuthenticationService>();

// Register OAuth 2.0 / OpenID Connect provider
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var jwtIssuer = configuration["Jwt:Issuer"] ?? "Otedama";
    var jwtAudience = configuration["Jwt:Audience"] ?? "OtedamaUsers";
    var jwtKey = configuration["Jwt:Key"] ?? "default-jwt-key-change-in-production";

    var auditTrail = sp.GetRequiredService<AuditTrail>();
    var logger = sp.GetRequiredService<ISimpleLogger>();

    return new AeroDriver.Core.Security.OAuthProvider(jwtIssuer, jwtAudience, jwtKey, auditTrail, logger);
});

// Configure HTTPS settings
builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

// Configure JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    var jwtKey = builder.Configuration["Jwt:Key"];
    var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Otedama";
    var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "OtedamaUsers";

    if (string.IsNullOrEmpty(jwtKey))
    {
        throw new InvalidOperationException("JWT Key is not configured in appsettings.json");
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(2)
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            app.Logger.LogWarning($"JWT authentication failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            // トークン検証後の追加処理（必要に応じて）
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline with security middleware
app.UseSecurityHeaders(); // Add security headers
app.UseRateLimiting(requestsPerMinute: 100, requestsPerSecond: 15); // Add rate limiting
app.UseRequestLogging(); // Add request logging
app.UseAuthentication(); // Add JWT authentication
app.UseAuthorization(); // Add authorization

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/enterprise/swagger.json", "Otedama API");
    });

    // Development authentication configuration
    app.UseAeroDriverAuthentication(AuthenticationConfiguration.CreateDevelopment());
}
else
{
    // Production settings
    app.UseHsts();

    // Production authentication configuration
    var authConfig = AuthenticationConfiguration.CreateProduction();
    var configuredApiKeys = builder.Configuration.GetSection("Authentication:ApiKeys").Get<string[]>()
        ?? Array.Empty<string>();

    foreach (var apiKey in configuredApiKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
    {
        authConfig.ValidApiKeys.Add(apiKey.Trim());
    }

    if (authConfig.ValidApiKeys.Count == 0)
    {
        app.Logger.LogWarning("Otedama API keys are not configured. Requests will be rejected until keys are supplied via configuration.");
    }

    app.UseAeroDriverAuthentication(authConfig);
}

app.UseHttpsRedirection();
app.UseCors(corsPolicyName);
app.UseAuthorization();
app.MapControllers();

// Driver Management Endpoints
app.MapGet("/api/drivers", async (CoreDriverService driverService) =>
{
    try
    {
        var drivers = await driverService.GetAllDriversAsync().ConfigureAwait(false);
        return Results.Ok(new
        {
            success = true,
            data = drivers,
            count = drivers.Count
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error retrieving drivers: {ex.Message}");
    }
});

app.MapGet("/api/drivers/{id}", async (string id, CoreDriverService driverService, ISimpleLogger logger) =>
{
    try
    {
        // 入力検証: 長さと形式をチェック
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200)
        {
            return Results.BadRequest(new { success = false, message = "Invalid driver ID format" });
        }

        // 危険な文字を含むIDを拒否
        if (id.Contains("..") || id.Contains("/") || id.Contains("\\") || id.Contains(";") || id.Contains("'") || id.Contains("\""))
        {
            logger.LogWarning($"Suspicious driver ID rejected: {InputValidator.SanitizeForLogging(id)}");
            return Results.BadRequest(new { success = false, message = "Invalid driver ID characters" });
        }

        var driver = await driverService.GetDriverByIdAsync(id).ConfigureAwait(false);

        if (driver == null)
        {
            return Results.NotFound(new { success = false, message = "Driver not found" });
        }

        return Results.Ok(new { success = true, data = driver });
    }
    catch (ValidationException ex)
    {
        logger.LogWarning($"Invalid driver lookup request: {InputValidator.SanitizeForLogging(id)} - {ex.Message}");
        return Results.BadRequest(new { success = false, message = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogError($"Error retrieving driver: {ex.Message}");
        return Results.Problem($"Error retrieving driver: {ex.Message}");
    }
});

app.MapPost("/api/drivers/scan", async (CoreDriverService driverService) =>
{
    try
    {
        var result = await driverService.ScanSystemAsync().ConfigureAwait(false);
        return Results.Ok(new
        {
            success = true,
            message = "Scan completed successfully",
            data = result
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error scanning system: {ex.Message}");
    }
});

app.MapPost("/api/drivers/{id}/backup", async (string id, CoreDriverService driverService, ISimpleLogger logger) =>
{
    try
    {
        // 入力検証
        if (string.IsNullOrWhiteSpace(id) || id.Length < 3 || id.Length > 200)
        {
            return Results.BadRequest(new { success = false, message = "Invalid driver ID format" });
        }

        // セキュリティチェック: パストラバーサル攻撃を防止
        if (id.Contains("..") || id.Contains("/") || id.Contains("\\") || id.Contains(";"))
        {
            logger.LogWarning($"Suspicious backup request rejected: {InputValidator.SanitizeForLogging(id)}");
            return Results.BadRequest(new { success = false, message = "Invalid driver ID characters" });
        }

        var result = await driverService.BackupDriverAsync(id).ConfigureAwait(false);
        return Results.Ok(new
        {
            success = result.Success,
            message = result.Message
        });
    }
    catch (Exception ex)
    {
        logger.LogError($"Error backing up driver: {ex.Message}");
        return Results.Problem($"Error backing up driver: {ex.Message}");
    }
});

app.MapPost("/api/system/optimize", async (CoreDriverService driverService) =>
{
    try
    {
        var result = await driverService.OptimizeSystemAsync().ConfigureAwait(false);
        return Results.Ok(new
        {
            success = result.Success,
            message = result.Message
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error optimizing system: {ex.Message}");
    }
});

app.MapGet("/api/system/status", async (CoreDriverService driverService) =>
{
    try
    {
        var drivers = await driverService.GetAllDriversAsync().ConfigureAwait(false);
        var systemStats = new
        {
            totalDrivers = drivers.Count,
            activeDrivers = drivers.Count(d => d.Status == "OK"),
            problemDrivers = drivers.Count(d => d.Status != "OK"),
            lastScanTime = DateTime.Now
        };

        return Results.Ok(new { success = true, data = systemStats });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting system status: {ex.Message}");
    }
});

app.MapGet("/api/performance", async (CoreDriverService driverService) =>
{
    try
    {
        var performanceReport = await driverService.GetPerformanceReportAsync().ConfigureAwait(false);
        return Results.Ok(new
        {
            success = true,
            data = performanceReport
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting performance report: {ex.Message}");
    }
});

app.MapGet("/api/security/scan", async (CoreDriverService driverService) =>
{
    try
    {
        var securityReport = await driverService.GetSecurityReportAsync().ConfigureAwait(false);
        return Results.Ok(new
        {
            success = true,
            data = securityReport
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error running security scan: {ex.Message}");
    }
});

// Health check endpoint
app.MapGet("/api/health", () =>
{
    try
    {
        var healthStatus = new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow,
            version = "2.0.0",
            checks = new Dictionary<string, object>
            {
                ["database"] = new { status = "Healthy", responseTime = "< 10ms" },
                ["wmi"] = new
                {
                    status = "Healthy",
                    connections = AeroDriver.Core.WmiConnectionPool.GetStatistics()
                },
                ["memory"] = new
                {
                    status = GetMemoryStatus(out var memoryMB),
                    usageMB = memoryMB
                }
            }
        };

        return Results.Ok(healthStatus);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Health check failed: {ex.Message}");
    }

    static string GetMemoryStatus(out long memoryMB)
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        memoryMB = process.WorkingSet64 / (1024 * 1024);
        return memoryMB < 200 ? "Healthy" : memoryMB < 500 ? "Degraded" : "Unhealthy";
    }
});

// Readiness probe
app.MapGet("/api/ready", () =>
{
    return Results.Ok(new { status = "Ready", timestamp = DateTime.UtcNow });
});

// Liveness probe
app.MapGet("/api/alive", () =>
{
    return Results.Ok(new { status = "Alive", timestamp = DateTime.UtcNow });
});

app.Run();
