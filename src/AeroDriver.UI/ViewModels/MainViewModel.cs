using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Languages.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AeroDriver.UI.ViewModels
{
    /// <summary>
    /// メイン画面のViewModel。CommunityToolkit.Mvvm のソースジェネレーター
    /// ([ObservableProperty]/[RelayCommand]) を使用。長時間処理は CancellationToken で
    /// キャンセル可能にし、実行中は IsBusy でコマンドを無効化する。
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILanguageService _lang;
        private readonly ILogger<MainViewModel> _logger;
        private CancellationTokenSource? _cts;

        public ObservableCollection<DriverInfo> InstalledDrivers { get; } = new();
        public ObservableCollection<DriverInfo> AvailableUpdates { get; } = new();

        // ローカライズ済みラベル（現在カルチャの文字列を ILanguageService から取得）
        public string ScanButtonText => _lang.GetString("Button_Scan");
        public string CheckUpdatesButtonText => _lang.GetString("Button_Update");
        public string InstallButtonText => _lang.GetString("Button_Update");
        public string RollbackButtonText => _lang.GetString("Button_Restore");
        public string InstalledTabHeader => _lang.GetString("Button_Scan");
        public string UpdatesTabHeader => _lang.GetString("Driver_Status_UpdateAvailable");

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
        [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
        [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(RollbackSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
        private DriverInfo? _selectedUpdate;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RollbackSelectedCommand))]
        private DriverInfo? _selectedInstalledDriver;

        public MainViewModel(
            IServiceScopeFactory scopeFactory,
            ILanguageService lang,
            ILogger<MainViewModel> logger)
        {
            _scopeFactory = scopeFactory;
            _lang = lang;
            _logger = logger;
        }

        private bool CanRun() => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task ScanAsync()
        {
            await RunAsync(_lang.GetString("Status_Scanning"), async (driverService, progress, ct) =>
            {
                var drivers = await driverService.GetAllDriversAsync(progress, ct).ConfigureAwait(true);
                InstalledDrivers.Clear();
                foreach (var d in drivers)
                    InstalledDrivers.Add(d);
                StatusMessage = $"{_lang.GetString("Status_Complete")} ({drivers.Count})";
            }).ConfigureAwait(true);
        }

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task CheckUpdatesAsync()
        {
            await RunAsync(_lang.GetString("Status_Updating"), async (driverService, progress, ct) =>
            {
                var updates = await driverService.CheckForUpdatesAsync(progress, ct).ConfigureAwait(true);
                AvailableUpdates.Clear();
                foreach (var u in updates)
                    AvailableUpdates.Add(u);
                StatusMessage = $"{_lang.GetString("Status_Complete")} ({updates.Count})";
            }).ConfigureAwait(true);
        }

        private bool CanInstall() => !IsBusy && SelectedUpdate != null;

        [RelayCommand(CanExecute = nameof(CanInstall))]
        private async Task InstallSelectedAsync()
        {
            var target = SelectedUpdate;
            if (target == null) return;

            await RunAsync(_lang.GetString("Status_Updating"), async (driverService, _, ct) =>
            {
                var result = await driverService.InstallDriverUpdateWithResultAsync(target, ct).ConfigureAwait(true);
                StatusMessage = DescribeResult(result, target);
                if (result == DriverInstallResult.Success)
                    AvailableUpdates.Remove(target);
            }).ConfigureAwait(true);
        }

        private bool CanRollback() => !IsBusy && SelectedInstalledDriver?.DeviceID != null;

        [RelayCommand(CanExecute = nameof(CanRollback))]
        private async Task RollbackSelectedAsync()
        {
            var target = SelectedInstalledDriver;
            if (target?.DeviceID == null) return;

            await RunAsync(_lang.GetString("Button_Restore"), async (driverService, _, ct) =>
            {
                bool ok = await driverService.RollbackDriverAsync(target.DeviceID, ct).ConfigureAwait(true);
                StatusMessage = ok
                    ? $"{_lang.GetString("Status_Complete")}: {target.DeviceName}"
                    : $"{_lang.GetString("Status_Error")}: {target.DeviceName}";
            }).ConfigureAwait(true);
        }

        private bool CanCancel() => IsBusy;

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel() => _cts?.Cancel();

        /// <summary>
        /// 共通の実行ラッパー: IsBusy 制御、スコープ生成（IDriverService は Scoped 登録のため
        /// 操作ごとに新しいスコープで解決する）、進捗のUIスレッドへの反映、例外・キャンセル処理。
        /// </summary>
        private async Task RunAsync(
            string startMessage,
            System.Func<IDriverService, System.IProgress<DriverScanProgress>, CancellationToken, Task> operation)
        {
            IsBusy = true;
            StatusMessage = startMessage;
            _cts = new CancellationTokenSource();

            var progress = new System.Progress<DriverScanProgress>(p =>
                StatusMessage = string.IsNullOrEmpty(p.CurrentDevice)
                    ? $"{p.Phase}: {p.Current}"
                    : $"{p.Phase}: {p.Current} - {p.CurrentDevice}");

            try
            {
                // IDriverService は Scoped 登録。using スコープで解決し、操作完了後に破棄する
                using var scope = _scopeFactory.CreateScope();
                var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();
                await operation(driverService, progress, _cts.Token).ConfigureAwait(true);
            }
            catch (System.OperationCanceledException)
            {
                StatusMessage = _lang.GetString("Status_Error") + " (キャンセルされました)";
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "操作中にエラーが発生しました");
                StatusMessage = $"{_lang.GetString("Status_Error")}: {ex.Message}";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                IsBusy = false;
            }
        }

        private string DescribeResult(DriverInstallResult result, DriverInfo target) => result switch
        {
            DriverInstallResult.Success => $"{_lang.GetString("Status_Complete")}: {target.DeviceName} {target.DriverVersion}",
            DriverInstallResult.AdminRequired => "管理者権限が必要です。管理者として実行してください。",
            DriverInstallResult.NoDownloadUrl => "ダウンロードURLがありません。",
            DriverInstallResult.InsecureDownloadUrl => "ダウンロードURLがHTTPSではありません。",
            DriverInstallResult.DownloadFailed => "ダウンロードに失敗しました。",
            DriverInstallResult.SignatureInvalid => "インストーラーの署名が無効です。",
            DriverInstallResult.KnownVulnerableDriver => "既知の脆弱ドライバー(BYOVD)のためブロックしました。",
            DriverInstallResult.InstallerFailed => $"インストールに失敗しました: {target.DeviceName}",
            DriverInstallResult.Cancelled => "キャンセルされました。",
            _ => $"不明なエラー: {target.DeviceName}",
        };
    }
}
