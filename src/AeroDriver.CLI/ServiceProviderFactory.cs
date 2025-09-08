using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Extensions;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.CLI
{
    /// <summary>
    /// サービスプロバイダーファクトリー
    /// </summary>
    public static class ServiceProviderFactory
    {
        /// <summary>
        /// DIコンテナを構築
        /// </summary>
        public static ServiceProvider CreateServiceProvider(bool verboseLogging = false)
        {
            var services = new ServiceCollection();
            
            // Configuration
            var configuration = BuildConfiguration();
            services.AddSingleton<IConfiguration>(configuration);
            
            // Bind configuration to strongly typed options
            services.Configure<AppConfiguration>(configuration.GetSection("AeroDriver"));
            
            // Core services registration
            services.AddAeroDriverCore();
            
            // Logging configuration
            if (verboseLogging)
            {
                services.AddLogging(builder =>
                {
                    builder.AddConfiguration(configuration.GetSection("Logging"))
                           .AddConsole()
                           .AddDebug()
                           .SetMinimumLevel(LogLevel.Debug);
                });
            }
            else
            {
                services.AddLogging(builder =>
                {
                    builder.AddConfiguration(configuration.GetSection("Logging"))
                           .AddConsole()
                           .AddDebug();
                });
            }
            
            return services.BuildServiceProvider();
        }
        
        /// <summary>
        /// 設定を構築
        /// </summary>
        private static IConfiguration BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables("AERODRIVER_");
            
            return builder.Build();
        }
        
        /// <summary>
        /// すべてのサービスを初期化
        /// </summary>
        public static async Task InitializeServicesAsync(ServiceProvider serviceProvider)
        {
            // キャッシュサービスの初期化
            var cacheService = serviceProvider.GetService<ICacheService>();
            cacheService?.Clear();
            
            // 設定サービスの初期化
            var settingsService = serviceProvider.GetService<ISettingsService>();
            settingsService?.Reload();
            
            // パフォーマンスモニターの初期化
            var performanceMonitor = serviceProvider.GetService<IPerformanceMonitor>();
            performanceMonitor?.Reset();
            
            await Task.CompletedTask;
        }
    }
}