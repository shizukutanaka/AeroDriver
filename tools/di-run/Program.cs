// ServiceCollectionExtensions.ConfigureServices() を実際に実行し、
// DI コンテナが成立しているかを検証する。offline-verify と同じ Check() 方式。
//
// DI の解決失敗・captive dependency は**実行時にしか出ない**。このプロジェクトでは
// これまで一度も実行されていなかった(ServiceCollectionExtensionsTests は xunit のため
// この環境では走らない)。
using AeroDriver.Core;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Services;
using Microsoft.Extensions.DependencyInjection;

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); }
    else { fail++; Console.WriteLine($"  FAIL  {name}  {detail}"); }
}

Console.WriteLine("== ConfigureServices の実行 ==");
ServiceCollection services = new();
try
{
    services.ConfigureServices();
    Check("ConfigureServices が例外なく完了する", true);
}
catch (Exception ex)
{
    Check("ConfigureServices が例外なく完了する", false, ex.ToString());
    Console.WriteLine($"\ndi-run: {pass} passed, {fail} failed");
    return 1;
}

Console.WriteLine("== コンテナの構築と検証 ==");
// ValidateOnBuild:  全登録の依存が解決可能かをビルド時に総当たりで検証する
// ValidateScopes:   Scoped をルートから解決したり Singleton に注入したりすると失敗させる
//                   (= captive dependency の検出。CLAUDE.md の手動チェック項目だった)
ServiceProvider provider;
try
{
    provider = services.BuildServiceProvider(new ServiceProviderOptions
    {
        ValidateOnBuild = true,
        ValidateScopes = true,
    });
    Check("ValidateOnBuild + ValidateScopes でコンテナを構築できる", true);
}
catch (Exception ex)
{
    Check("ValidateOnBuild + ValidateScopes でコンテナを構築できる", false, ex.Message);
    Console.WriteLine($"\ndi-run: {pass} passed, {fail} failed");
    return 1;
}

using (provider)
{
    Console.WriteLine("== スコープ内での解決 ==");
    using (var scope = provider.CreateScope())
    {
        var sp = scope.ServiceProvider;
        T? Resolve<T>(string label) where T : class
        {
            try
            {
                var v = sp.GetRequiredService<T>();
                Check($"{label} を解決できる", v != null);
                return v;
            }
            catch (Exception ex)
            {
                Check($"{label} を解決できる", false, ex.Message);
                return null;
            }
        }

        Resolve<IDriverService>(nameof(IDriverService));
        Resolve<IBackupService>(nameof(IBackupService));
        Resolve<ISettingsService>(nameof(ISettingsService));
        Resolve<IInstallHistoryService>(nameof(IInstallHistoryService));
        Resolve<VulnerableDriverBlocklist>(nameof(VulnerableDriverBlocklist));

        // 更新ソースは IEnumerable で全件受け取る設計(DriverService のコンストラクタ)
        var sources = sp.GetServices<IDriverUpdateSource>().ToList();
        Check("IDriverUpdateSource が2件登録されている", sources.Count == 2, $"got {sources.Count}");
        Check("PnpUtilDriverSource が含まれる", sources.Any(s => s is PnpUtilDriverSource));
        Check("WindowsUpdateAgentSource が含まれる", sources.Any(s => s is WindowsUpdateAgentSource));
    }

    Console.WriteLine("== ライフタイム ==");
    using (var s1 = provider.CreateScope())
    using (var s2 = provider.CreateScope())
    {
        var set1 = s1.ServiceProvider.GetRequiredService<ISettingsService>();
        var set2 = s2.ServiceProvider.GetRequiredService<ISettingsService>();
        Check("ISettingsService は Singleton(別スコープでも同一)", ReferenceEquals(set1, set2));

        var hist1 = s1.ServiceProvider.GetRequiredService<IInstallHistoryService>();
        var hist2 = s2.ServiceProvider.GetRequiredService<IInstallHistoryService>();
        Check("IInstallHistoryService は Singleton(追記の直列化のため)", ReferenceEquals(hist1, hist2));

        var blk1 = s1.ServiceProvider.GetRequiredService<VulnerableDriverBlocklist>();
        var blk2 = s2.ServiceProvider.GetRequiredService<VulnerableDriverBlocklist>();
        Check("VulnerableDriverBlocklist は Singleton(キャッシュ共有のため)", ReferenceEquals(blk1, blk2));

        var drv1 = s1.ServiceProvider.GetRequiredService<IDriverService>();
        var drv2 = s2.ServiceProvider.GetRequiredService<IDriverService>();
        Check("IDriverService は Scoped(別スコープでは別インスタンス)", !ReferenceEquals(drv1, drv2));

        var drv1b = s1.ServiceProvider.GetRequiredService<IDriverService>();
        Check("IDriverService は同一スコープ内では同一インスタンス", ReferenceEquals(drv1, drv1b));
    }

    Console.WriteLine("== captive dependency ガードが効いていること ==");
    // ValidateScopes が有効なら、ルートプロバイダーから Scoped は解決できない。
    // これが通ってしまうと上の検証全体が骨抜きになるので、ガード自体を検証する。
    bool guarded = false;
    try
    {
        provider.GetRequiredService<IDriverService>();
    }
    catch (InvalidOperationException)
    {
        guarded = true;
    }
    Check("ルートから Scoped を解決しようとすると失敗する", guarded,
        "ValidateScopes が効いていない — 上のライフタイム検証が無意味になる");
}

Console.WriteLine();
Console.WriteLine($"di-run: {pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;
