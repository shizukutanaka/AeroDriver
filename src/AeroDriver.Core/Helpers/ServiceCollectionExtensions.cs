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

            // IHttpClientFactory（ソケット枯渇を防ぐ）
            services.AddHttpClient(nameof(DriverService))
                    .ConfigureHttpClient(c => c.Timeout = System.TimeSpan.FromSeconds(60));
            services.AddHttpClient(nameof(PciIdDatabase))
                    .ConfigureHttpClient(c => c.Timeout = System.TimeSpan.FromSeconds(30));

            // PCI IDs データベース（シングルトン: ファイルキャッシュを共有）
            services.AddSingleton<PciIdDatabase>(sp =>
                new PciIdDatabase(
                    sp.GetRequiredService<ILogger<PciIdDatabase>>(),
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PciIdDatabase))));

            // コアサービス
            services.AddScoped<IDriverService, DriverService>();
            services.AddScoped<IBackupService, BackupService>();
            services.AddScoped<IWhqlDatabaseService, WhqlDatabaseService>();

            // ドライバー更新ソース（IEnumerable<IDriverUpdateSource> で全取得可能）
            services.AddScoped<IDriverUpdateSource, PnpUtilDriverSource>();
            services.AddScoped<IDriverUpdateSource, WindowsUpdateAgentSource>();

            return services;
        }
    }
}
