using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AeroDriver.Core.Services;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Service;

public class Program
{
    public static void Main(string[] args)
    {
        IHost host = Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureServices(services =>
            {
                // Logging
                services.AddSingleton<ISimpleLogger, SimpleLogger>();

                // Core dependencies
                services.AddSingleton<IDriverRepository, DriverRepository>();
                services.AddSingleton<ISecurityService, SecurityService>();
                services.AddSingleton<ErrorHandlingService>();

                // Production background services
                services.AddSingleton<FileTelemetryService>();
                services.AddSingleton<ITelemetryService>(sp => sp.GetRequiredService<FileTelemetryService>());
                services.AddSingleton<IPerformanceTelemetrySink>(sp => sp.GetRequiredService<FileTelemetryService>());
                services.AddSingleton<IAutoUpdateService, FileAutoUpdateService>();

                // Monitoring services
                services.AddSingleton<PerformanceMonitoringService>();

                // Core facade using abstract dependencies
                services.AddSingleton<CoreDriverService>();

                services.AddHostedService<AeroDriverWorker>();
            })
            .Build();

        host.Run();
    }
}
