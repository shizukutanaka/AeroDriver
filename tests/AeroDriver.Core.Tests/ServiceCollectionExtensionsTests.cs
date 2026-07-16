using System.Net.Http;
using AeroDriver.Core;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AeroDriver.Core.Tests;

/// <summary>
/// ConfigureServices の DI 配線が実際に解決可能かを検証する。
/// これまで専用テストがなく、WhqlDatabaseService の HttpClient 未登録
/// （new HttpClient() を直接生成していた回帰）のような配線ミスは
/// 実行時（CLIやアプリ起動時）まで発覚しなかった。
/// コンストラクターはどのサービスも Windows専用APIを直接呼ばないため、
/// 生成自体はクロスプラットフォームで検証できる（実際のスキャン等の
/// メソッド呼び出しはWindows専用のままで良い）。
/// </summary>
public class ServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider()
        => new ServiceCollection().ConfigureServices().BuildServiceProvider();

    [Fact]
    public void ConfigureServices_ResolvesSingletonServices()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<ISettingsService>().Should().NotBeNull();
        provider.GetRequiredService<PciIdDatabase>().Should().NotBeNull();
    }

    [Fact]
    public void ConfigureServices_ResolvesScopedServices()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDriverService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IBackupService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IWhqlDatabaseService>().Should().NotBeNull();
    }

    [Fact]
    public void ConfigureServices_RegistersBothDriverUpdateSources()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IDriverUpdateSource>().ToList();

        sources.Should().HaveCount(2);
        sources.Should().Contain(s => s is PnpUtilDriverSource);
        sources.Should().Contain(s => s is WindowsUpdateAgentSource);
    }

    [Fact]
    public void ConfigureServices_HttpClientFactory_CreatesNamedClientsWithoutThrowing()
    {
        // WhqlDatabaseService の "new HttpClient()" 直接生成バグの回帰防止:
        // 各サービス名で名前付きクライアントが実際に生成できることを確認する
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        factory.CreateClient(nameof(DriverService)).Should().NotBeNull();
        factory.CreateClient(nameof(PciIdDatabase)).Should().NotBeNull();
        factory.CreateClient(nameof(WhqlDatabaseService)).Should().NotBeNull();
    }

    [Fact]
    public void ConfigureServices_SettingsService_IsSingletonAcrossScopes()
    {
        using var provider = BuildProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var a = scope1.ServiceProvider.GetRequiredService<ISettingsService>();
        var b = scope2.ServiceProvider.GetRequiredService<ISettingsService>();

        a.Should().BeSameAs(b);
    }
}
