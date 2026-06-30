using System.Net.Http;
using System.Net.Sockets;
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

            // IHttpClientFactory + 標準レジリエンス（リトライ・サーキットブレーカー・タイムアウト）
            // Microsoft.Extensions.Http.Resilience が提供する AddStandardResilienceHandler() を使用:
            //   - 最大3回の指数バックオフリトライ
            //   - サーキットブレーカー（失敗率 10%超で遮断）
            //   - 合計タイムアウト 30 秒
            services.AddHttpClient(nameof(DriverService))
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    })
                    .AddStandardResilienceHandler();
            services.AddHttpClient(nameof(PciIdDatabase))
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    })
                    .AddStandardResilienceHandler();
            services.AddHttpClient(nameof(WhqlDatabaseService))
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    })
                    .AddStandardResilienceHandler();

            // PCI IDs データベース（シングルトン: ファイルキャッシュを共有）
            services.AddSingleton<PciIdDatabase>(sp =>
                new PciIdDatabase(
                    sp.GetRequiredService<ILogger<PciIdDatabase>>(),
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PciIdDatabase))));

            // コアサービス
            services.AddSingleton<ISettingsService, SettingsService>();  // 設定はアプリ全体で共有
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
