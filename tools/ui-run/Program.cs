// MainViewModel を実際にインスタンス化してコマンドを実行し、状態遷移を検証する。
// offline-verify と同じ Check() 方式。
using System.Globalization;
using System.Linq;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.UI.Services;
using AeroDriver.UI.ViewModels;
using AeroDriver.UiRun;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); }
    else { fail++; Console.WriteLine($"  FAIL  {name}  {detail}"); }
}

// テスト対象一式を組み立てる。DI コンテナは本物 (Microsoft.Extensions.DependencyInjection)
// を使うため、IDriverService が Scoped 解決されるという ViewModel の前提もそのまま検証される。
(MainViewModel vm, MockDriverService drv, MockLanguageService lang,
 MockThemeService theme, MockSettingsService settings, MockFileDialogService dialog) Build()
{
    var drv = new MockDriverService();
    var services = new ServiceCollection();
    services.AddScoped<IDriverService>(_ => drv);
    var provider = services.BuildServiceProvider();
    var lang = new MockLanguageService();
    var theme = new MockThemeService();
    var settings = new MockSettingsService();
    var dialog = new MockFileDialogService();
    var vm = new MainViewModel(
        provider.GetRequiredService<IServiceScopeFactory>(),
        lang, dialog, theme, settings,
        NullLogger<MainViewModel>.Instance);
    return (vm, drv, lang, theme, settings, dialog);
}

static DriverInfo D(string id, string name, string? version = "1.0")
    => new() { DeviceID = id, DeviceName = name, DriverVersion = version };

Console.WriteLine("== Scan ==");
{
    var (vm, drv, _, _, _, _) = Build();
    drv.Installed = new List<DriverInfo> { D("A", "GPU"), D("B", "NIC") };
    await vm.ScanCommand.ExecuteAsync(null);
    Check("InstalledDrivers が埋まる", vm.InstalledDrivers.Count == 2, $"got {vm.InstalledDrivers.Count}");
    Check("完了後 IsBusy == false", !vm.IsBusy);
    Check("Scan 中は CanExecute が落ちるので完了後は復帰", vm.ScanCommand.CanExecute(null));

    // 2回目でクリアされること(累積しない)
    await vm.ScanCommand.ExecuteAsync(null);
    Check("再スキャンで累積しない", vm.InstalledDrivers.Count == 2, $"got {vm.InstalledDrivers.Count}");
}

Console.WriteLine("== Scan 例外時 ==");
{
    var (vm, drv, _, _, _, _) = Build();
    drv.ThrowOnScan = new InvalidOperationException("boom");
    await vm.ScanCommand.ExecuteAsync(null);
    Check("例外でも IsBusy が戻る", !vm.IsBusy);
    Check("例外はリソースキー経由で通知", vm.StatusMessage.StartsWith("[Status_Error]"), vm.StatusMessage);
}

Console.WriteLine("== CheckUpdates ==");
{
    var (vm, drv, _, _, _, _) = Build();
    Check("更新0件なら InstallAll は不可", !vm.InstallAllUpdatesCommand.CanExecute(null));
    drv.Updates = new List<DriverInfo> { D("A", "Chipset"), D("B", "GPU") };
    await vm.CheckUpdatesCommand.ExecuteAsync(null);
    Check("AvailableUpdates が埋まる", vm.AvailableUpdates.Count == 2, $"got {vm.AvailableUpdates.Count}");
    Check("更新ありなら InstallAll が可能", vm.InstallAllUpdatesCommand.CanExecute(null));
}

Console.WriteLine("== InstallAll: 成功と再起動要求 ==");
{
    var (vm, drv, _, _, _, _) = Build();
    drv.Updates = new List<DriverInfo> { D("A", "Chipset"), D("B", "GPU") };
    await vm.CheckUpdatesCommand.ExecuteAsync(null);
    drv.InstallResults.Enqueue(DriverInstallResult.Success);
    drv.InstallResults.Enqueue(DriverInstallResult.SuccessRebootRequired);
    await vm.InstallAllUpdatesCommand.ExecuteAsync(null);
    Check("2件とも実行された", drv.InstallCalls.Count == 2, string.Join(",", drv.InstallCalls));
    Check("SuccessRebootRequired も成功扱いで一覧から除去", vm.AvailableUpdates.Count == 0,
        $"remaining {vm.AvailableUpdates.Count}");
    Check("成功件数が 2/2", vm.StatusMessage.Contains("2 / 2"), vm.StatusMessage);
    Check("再起動要求はリソースキー経由で集計 (ハードコード日本語なし)",
        vm.StatusMessage.Contains("1: [Install_RebootRequired]"), vm.StatusMessage);
    Check("空になったら InstallAll は不可に戻る", !vm.InstallAllUpdatesCommand.CanExecute(null));
}

Console.WriteLine("== InstallAll: AdminRequired で即中断 (PR #28 のロジック初の実行検証) ==");
{
    var (vm, drv, _, _, _, _) = Build();
    drv.Updates = new List<DriverInfo> { D("A", "Chipset"), D("B", "GPU"), D("C", "NIC") };
    await vm.CheckUpdatesCommand.ExecuteAsync(null);
    drv.InstallResults.Enqueue(DriverInstallResult.AdminRequired);
    drv.InstallResults.Enqueue(DriverInstallResult.Success);
    await vm.InstallAllUpdatesCommand.ExecuteAsync(null);
    Check("1件目で中断し2件目を呼ばない", drv.InstallCalls.Count == 1,
        $"calls={drv.InstallCalls.Count} ({string.Join(",", drv.InstallCalls)})");
    Check("一覧は3件のまま", vm.AvailableUpdates.Count == 3, $"got {vm.AvailableUpdates.Count}");
    Check("AdminRequired を1回だけ通知", vm.StatusMessage.StartsWith("[Install_AdminRequired]"), vm.StatusMessage);
    Check("スキップ件数を通知", vm.StatusMessage.Contains("0 / 3"), vm.StatusMessage);
}

Console.WriteLine("== InstallAll: 個別失敗は継続 ==");
{
    var (vm, drv, _, _, _, _) = Build();
    drv.Updates = new List<DriverInfo> { D("A", "Chipset"), D("B", "GPU"), D("C", "NIC") };
    await vm.CheckUpdatesCommand.ExecuteAsync(null);
    drv.InstallResults.Enqueue(DriverInstallResult.SignatureInvalid);
    drv.InstallResults.Enqueue(DriverInstallResult.KnownVulnerableDriver);
    drv.InstallResults.Enqueue(DriverInstallResult.Success);
    await vm.InstallAllUpdatesCommand.ExecuteAsync(null);
    Check("失敗しても最後まで実行", drv.InstallCalls.Count == 3, $"calls={drv.InstallCalls.Count}");
    Check("成功した1件のみ除去", vm.AvailableUpdates.Count == 2, $"got {vm.AvailableUpdates.Count}");
    Check("失敗件数を通知", vm.StatusMessage.Contains("[Status_Error]") && vm.StatusMessage.Contains("2"),
        vm.StatusMessage);
}

Console.WriteLine("== InstallSelected ==");
{
    var (vm, drv, _, _, _, _) = Build();
    drv.Updates = new List<DriverInfo> { D("A", "GPU", "2.0") };
    await vm.CheckUpdatesCommand.ExecuteAsync(null);
    Check("未選択なら不可", !vm.InstallSelectedCommand.CanExecute(null));
    vm.SelectedUpdate = vm.AvailableUpdates[0];
    Check("選択したら可能", vm.InstallSelectedCommand.CanExecute(null));
    drv.InstallResults.Enqueue(DriverInstallResult.Success);
    await vm.InstallSelectedCommand.ExecuteAsync(null);
    Check("成功で一覧から除去", vm.AvailableUpdates.Count == 0);
    Check("成功メッセージにバージョンが載る", vm.StatusMessage.Contains("2.0"), vm.StatusMessage);
}

Console.WriteLine("== DescribeResult: 全失敗理由がリソースキー経由 ==");
{
    var (vm, drv, lang, _, _, _) = Build();
    var failures = new[]
    {
        (DriverInstallResult.AdminRequired, "Install_AdminRequired"),
        (DriverInstallResult.NoDownloadUrl, "Install_NoDownloadUrl"),
        (DriverInstallResult.InsecureDownloadUrl, "Install_InsecureUrl"),
        (DriverInstallResult.DownloadFailed, "Install_DownloadFailed"),
        (DriverInstallResult.SignatureInvalid, "Install_SignatureInvalid"),
        (DriverInstallResult.KnownVulnerableDriver, "Install_KnownVulnerable"),
        (DriverInstallResult.InstallerFailed, "Install_InstallerFailed"),
        (DriverInstallResult.Cancelled, "Install_Cancelled"),
        (DriverInstallResult.UnknownError, "Install_UnknownError"),
    };
    foreach (var (result, key) in failures)
    {
        var (vm2, drv2, _, _, _, _) = Build();
        drv2.Updates = new List<DriverInfo> { D("A", "GPU") };
        await vm2.CheckUpdatesCommand.ExecuteAsync(null);
        vm2.SelectedUpdate = vm2.AvailableUpdates[0];
        drv2.InstallResults.Enqueue(result);
        await vm2.InstallSelectedCommand.ExecuteAsync(null);
        Check($"{result} -> {key}", vm2.StatusMessage.Contains($"[{key}]"), vm2.StatusMessage);
        Check($"{result} は一覧に残る", vm2.AvailableUpdates.Count == 1);
    }
    Check("SuccessRebootRequired は再起動キーを添える",
        await DescribeRebootAsync(), "");

    async Task<bool> DescribeRebootAsync()
    {
        var (v, d, _, _, _, _) = Build();
        d.Updates = new List<DriverInfo> { D("A", "GPU") };
        await v.CheckUpdatesCommand.ExecuteAsync(null);
        v.SelectedUpdate = v.AvailableUpdates[0];
        d.InstallResults.Enqueue(DriverInstallResult.SuccessRebootRequired);
        await v.InstallSelectedCommand.ExecuteAsync(null);
        return v.StatusMessage.Contains("[Install_RebootRequired]");
    }
}

Console.WriteLine("== Backup / Rollback / Details ==");
{
    var (vm, drv, _, _, _, _) = Build();
    drv.Installed = new List<DriverInfo> { D("A", "GPU") };
    await vm.ScanCommand.ExecuteAsync(null);
    Check("未選択なら Backup 不可", !vm.BackupSelectedCommand.CanExecute(null));
    Check("未選択なら Rollback 不可", !vm.RollbackSelectedCommand.CanExecute(null));
    Check("未選択なら Details 不可", !vm.ShowDetailsCommand.CanExecute(null));

    vm.SelectedInstalledDriver = vm.InstalledDrivers[0];
    Check("選択で Backup 可能", vm.BackupSelectedCommand.CanExecute(null));

    await vm.BackupSelectedCommand.ExecuteAsync(null);
    Check("バックアップ成功", vm.StatusMessage.StartsWith("[Status_Complete]"), vm.StatusMessage);

    drv.RollbackReturns = false;
    await vm.RollbackSelectedCommand.ExecuteAsync(null);
    Check("ロールバック失敗を通知", vm.StatusMessage.StartsWith("[Status_Error]"), vm.StatusMessage);

    drv.Detail = new DriverDetailInfo { DeviceID = "A", DeviceName = "GPU detail" };
    await vm.ShowDetailsCommand.ExecuteAsync(null);
    Check("詳細を取得して保持", vm.SelectedDetail?.DeviceName == "GPU detail", vm.SelectedDetail?.DeviceName ?? "(null)");

    // 選択が変わったら詳細はクリアされる
    vm.InstalledDrivers.Add(D("B", "NIC"));
    vm.SelectedInstalledDriver = vm.InstalledDrivers[1];
    Check("選択変更で詳細がクリアされる", vm.SelectedDetail == null);
}

Console.WriteLine("== カスタムインストール ==");
{
    var (vm, drv, _, _, _, dialog) = Build();
    dialog.PathToReturn = null;
    await vm.InstallCustomDriverCommand.ExecuteAsync(null);
    Check("ダイアログキャンセルならインストールしない", drv.CustomInstallCalls.Count == 0);

    dialog.PathToReturn = @"C:\drivers\foo.inf";
    await vm.InstallCustomDriverCommand.ExecuteAsync(null);
    Check("選択したパスをそのまま渡す",
        drv.CustomInstallCalls.Count == 1 && drv.CustomInstallCalls[0] == @"C:\drivers\foo.inf",
        string.Join(",", drv.CustomInstallCalls));
}

Console.WriteLine("== 言語切替 ==");
{
    var (vm, _, lang, _, settings, _) = Build();
    var changed = new List<string>();
    vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");
    vm.SelectedCulture = new CultureInfo("ja-JP");
    Check("ILanguageService のカルチャが切り替わる", lang.CurrentCulture.Name == "ja-JP", lang.CurrentCulture.Name);
    Check("設定に永続化される", settings.CultureName == "ja-JP", settings.CultureName ?? "(null)");
    foreach (var label in new[]
    {
        "ScanButtonText", "CheckUpdatesButtonText", "InstallButtonText", "UpdateAllButtonText",
        "RollbackButtonText", "CustomInstallButtonText", "BackupButtonText",
        "InstalledTabHeader", "UpdatesTabHeader", "LanguageLabel", "ThemeLabel",
    })
        Check($"{label} の PropertyChanged が発火", changed.Contains(label));

    Check("Cultures は ILanguageService から", vm.Cultures.Count == 2);

    // XAML から束縛される全ラベルが言語切替で再評価されること。
    // 以前は列ヘッダーと詳細ペインが XAML に日本語直書きで、切替しても変わらなかった
    foreach (var label in new[]
    {
        "CreateRestorePointLabel", "BackupBeforeInstallLabel", "IncludeBetaLabel", "AutoCheckLabel",
        "CancelButtonText", "ColumnDeviceNameText", "ColumnVersionText", "ColumnProviderText",
        "ColumnSourceText", "DetailTitleText", "DetailHintText", "DetailSignatureText",
        "DetailManufacturerText", "DetailClassText", "DetailStatusText", "DetailPathText",
        "DetailSizeText", "DetailValidToText", "DetailTrustedChainText",
    })
        Check($"{label} の PropertyChanged が発火", changed.Contains(label));

    // ラベルは全てリソースキー経由(ハードコードが混ざっていない)
    Check("全ラベルがリソース経由",
        new[] { vm.ColumnDeviceNameText, vm.DetailTitleText, vm.CancelButtonText, vm.DetailSizeText }
            .All(t => t.StartsWith("[") && t.EndsWith("]")),
        vm.ColumnDeviceNameText);
}

Console.WriteLine("== 設定トグル(以前は設定ファイル手編集しか手段が無かった) ==");
{
    var (vm, _, _, _, settings, _) = Build();
    settings.CreateRestorePoint = false;
    vm.CreateRestorePointEnabled = true;
    Check("復元ポイント設定を書き込む", settings.CreateRestorePoint);
    Check("即座に保存する", settings.SaveCount == 1, settings.SaveCount.ToString());
    Check("読み出しが設定と一致", vm.CreateRestorePointEnabled);

    vm.BackupBeforeInstall = false;
    Check("バックアップ設定を書き込む", !settings.BackupEnabled);
    vm.IncludeBetaDrivers = true;
    Check("ベータ設定を書き込む", settings.IncludeBetaDrivers);
    vm.AutoCheckOnStartup = false;
    Check("自動確認設定を書き込む", !settings.AutoUpdateEnabled);
    Check("変更ごとに保存される", settings.SaveCount == 4, settings.SaveCount.ToString());

    // 保存に失敗しても現在のセッションには反映済み(可用性層はフェイルオープン)
    var (vm2, _, _, _, settings2, _) = Build();
    settings2.ThrowOnSave = new IOException("disk full");
    vm2.CreateRestorePointEnabled = true;
    Check("保存失敗でも値は反映され例外は伝播しない", settings2.CreateRestorePoint);
}

Console.WriteLine("== テーマ切替 ==");
{
    var (vm, _, _, theme, settings, _) = Build();
    Check("Themes は IThemeService から", vm.Themes.Count == 2);
    vm.SelectedTheme = AppTheme.Dark;
    Check("IThemeService.Apply が呼ばれる", theme.Applied.Count == 1 && theme.Applied[0] == AppTheme.Dark);
    Check("テーマ名が永続化される", settings.ThemeName == "Dark", settings.ThemeName ?? "(null)");
}

Console.WriteLine("== Cancel ==");
{
    var (vm, _, _, _, _, _) = Build();
    Check("非実行中は Cancel 不可", !vm.CancelCommand.CanExecute(null));
    vm.CancelCommand.Execute(null); // _cts == null でも例外にならないこと
    Check("実行中でない Cancel は無害", true);
}

Console.WriteLine();
Console.WriteLine($"ui-run: {pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;
