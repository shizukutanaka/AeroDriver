using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Services;

namespace AeroDriver.Core.Extensions
{
    /// <summary>
    /// サービスコレクション拡張メソッド
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// AeroDriverのコアサービスを登録
        /// </summary>
        public static IServiceCollection AddAeroDriverCore(this IServiceCollection services)
        {
            // Core Services
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<ICacheService, CacheService>();
            services.AddSingleton<ISimpleLogger, SimpleLoggerService>();
            services.AddSingleton<IErrorHandler, ErrorHandlerService>();
            services.AddSingleton<IPerformanceMonitor, PerformanceMonitorService>();
            
            // New Enhanced Services
            services.AddSingleton<MetricsCollectionService>();
            services.AddSingleton<SystemResourceMonitor>();
            services.AddSingleton<MaintenanceService>();
            services.AddScoped<DriverValidationService>();
            services.AddScoped<ErrorRecoveryService>();
            
            // Business Services
            services.AddScoped<IBackupService, BackupService>();
            services.AddScoped<IWhqlDatabaseService, WhqlDatabaseService>();
            services.AddScoped<IDriverService, DriverService>();
            services.AddScoped<ISystemHealthService, SystemHealthService>();
            services.AddScoped<ICleanupService, CleanupService>();
            services.AddScoped<IReportExportService, ReportExportService>();
            
            // Background Services
            services.AddSingleton<IBackgroundTaskService, BackgroundTaskService>();
            services.AddScoped<IAutoUpdateService, AutoUpdateService>();
            
            return services;
        }
        
        /// <summary>
        /// ロギングを設定
        /// </summary>
        public static IServiceCollection AddAeroDriverLogging(this IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.AddConsole()
                       .AddDebug()
                       .SetMinimumLevel(LogLevel.Information);
            });
            
            return services;
        }
        
        /// <summary>
        /// バックグラウンドサービスを登録
        /// </summary>
        public static IServiceCollection AddAeroDriverBackgroundServices(this IServiceCollection services)
        {
            // Enhanced monitoring services
            services.AddSingleton<SystemResourceMonitor>();
            services.AddSingleton<MaintenanceService>();
            
            // Future background services
            // services.AddHostedService<AutoUpdateCheckService>();
            // services.AddHostedService<HealthMonitorService>();
            
            return services;
        }
        
        /// <summary>
        /// 開発環境用の設定
        /// </summary>
        public static IServiceCollection AddAeroDriverDevelopment(this IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            
            return services;
        }
        
        /// <summary>
        /// 高度な機能とモニタリングを追加
        /// </summary>
        public static IServiceCollection AddAeroDriverAdvanced(this IServiceCollection services)
        {
            // Enhanced services for production environments
            services.AddSingleton<MetricsCollectionService>();
            services.AddSingleton<SystemResourceMonitor>();
            services.AddSingleton<MaintenanceService>();
            services.AddScoped<DriverValidationService>();
            services.AddScoped<ErrorRecoveryService>();
            
            return services;
        }
        
        /// <summary>
        /// 軽量モードサービスを登録（リソース制約環境用）
        /// </summary>
        public static IServiceCollection AddAeroDriverLightweight(this IServiceCollection services)
        {
            // Essential services only
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<ICacheService, CacheService>();
            services.AddSingleton<ISimpleLogger, SimpleLoggerService>();
            services.AddSingleton<IErrorHandler, ErrorHandlerService>();
            
            // Basic business services
            services.AddScoped<IDriverService, DriverService>();
            services.AddScoped<IWhqlDatabaseService, WhqlDatabaseService>();
            
            // Basic validation only
            services.AddScoped<DriverValidationService>();
            
            return services;
        }
    }
}