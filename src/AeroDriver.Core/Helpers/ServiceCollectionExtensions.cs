using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core
{
    /// <summary>
    /// IServiceCollectionの拡張メソッドを提供するクラス
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// AeroDriverのサービスをDIコンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection ConfigureServices(this IServiceCollection services)
        {
            // ロギングの設定
            services.AddLogging(configure => configure.AddConsole());
            
            // サービスの登録
            services.AddScoped<IDriverService, DriverService>();
            services.AddScoped<IBackupService, BackupService>();
            services.AddScoped<IWhqlDatabaseService, WhqlDatabaseService>();
            
            return services;
        }
    }
}
