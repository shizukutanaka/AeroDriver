using System.Net.Http;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureServices(this IServiceCollection services)
        {
            services.AddLogging(configure => configure.AddConsole());

            // 共有 HttpClient（サービス間で再利用）
            services.AddSingleton<HttpClient>();

            // PCI IDs データベース（シングルトン: ファイルキャッシュを共有）
            services.AddSingleton<PciIdDatabase>();

            // コアサービス
            services.AddScoped<IDriverService, DriverService>();
            services.AddScoped<IBackupService, BackupService>();
            services.AddScoped<IWhqlDatabaseService, WhqlDatabaseService>();

            // ドライバー更新ソース（複数登録 → IEnumerable<IDriverUpdateSource> で全取得可）
            services.AddScoped<IDriverUpdateSource, PnpUtilDriverSource>();
            services.AddScoped<IDriverUpdateSource, WindowsUpdateAgentSource>();

            return services;
        }
    }
}
