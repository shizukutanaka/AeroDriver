using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Languages.Services;
using AeroDriver.UI.Services;
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
        private readonly IFileDialogService _fileDialog;
        private readonly IThemeService _themeService;
        private readonly ISettingsService _settings;
        private readonly ILogger<MainViewModel> _logger;
        private CancellationTokenSource? _cts;

        public ObservableCollection<DriverInfo> InstalledDrivers { get; } = new();
        public ObservableCollection<DriverInfo> AvailableUpdates { get; } = new();

        /// <summary>言語切替コンボボックス用。ILanguageService が公開する対応カルチャ。</summary>
        public IReadOnlyList<CultureInfo> Cultures => _lang.SupportedCultures;

        /// <summary>テーマ切替コンボボックス用。</summary>
        public IReadOnlyList<AppTheme> Themes => _themeService.AvailableThemes;

        // ローカライズ済みラベル（現在カルチャの文字列を ILanguageService から取得）。
        // 言語切替時は OnSelectedCultureChanged で全ラベルの PropertyChanged を発火する。
        public string ScanButtonText => _lang.GetString("Button_Scan");
        public string CheckUpdatesButtonText => _lang.GetString("Button_Update");
        public string InstallButtonText => _lang.GetString("Button_Update");
        public string UpdateAllButtonText => _lang.GetString("Button_UpdateAll");
        public string RollbackButtonText => _lang.GetString("Button_Restore");
        // Button_Backup は「バックアップ」と訳されているため、カスタムインストールに流用しない
        // (安全網を期待したユーザーがインストールに誘導される事故になる)
        public string CustomInstallButtonText => _lang.GetString("Button_CustomInstall");
        public string BackupButtonText => _lang.GetString("Button_Backup");
        public string InstalledTabHeader => _lang.GetString("Button_Scan");
        public string UpdatesTabHeader => _lang.GetString("Driver_Status_UpdateAvailable");
        public string LanguageLabel => _lang.GetString("Settings_Language");
        public string ThemeLabel => _lang.GetString("Settings_Theme");
        public string CreateRestorePointLabel => _lang.GetString("Settings_CreateRestorePoint");
        public string BackupBeforeInstallLabel => _lang.GetString("Settings_BackupBeforeInstall");
        public string IncludeBetaLabel => _lang.GetString("Settings_IncludeBeta");
        public string AutoCheckLabel => _lang.GetString("Settings_CheckForUpdates");

        // DataGrid の列ヘッダーと詳細ペインのラベル。以前は XAML に日本語が直書きされており、
        // 非日本語環境では UI が半分しか翻訳されていなかった。BindingProxy 経由で束縛する。
        public string CancelButtonText => _lang.GetString("Button_Cancel");
        public string ColumnDeviceNameText => _lang.GetString("Column_DeviceName");
        public string ColumnVersionText => _lang.GetString("Column_Version");
        public string ColumnProviderText => _lang.GetString("Column_Provider");
        public string ColumnSourceText => _lang.GetString("Column_Source");
        public string DetailTitleText => _lang.GetString("Detail_Title");
        public string DetailHintText => _lang.GetString("Detail_Hint");
        public string DetailSignatureText => _lang.GetString("Detail_Signature");
        public string DetailManufacturerText => _lang.GetString("Detail_Manufacturer");
        public string DetailClassText => _lang.GetString("Detail_Class");
        public string DetailStatusText => _lang.GetString("Detail_Status");
        public string DetailPathText => _lang.GetString("Detail_Path");
        public string DetailSizeText => _lang.GetString("Detail_Size");
        public string DetailValidToText => _lang.GetString("Detail_ValidTo");
        public string DetailTrustedChainText => _lang.GetString("Detail_TrustedChain");

        // 設定トグル。Core は以前からこれらを尊重していたが、GUI/CLI のどこからも
        // 変更できず設定ファイルを手編集するしかなかった。ここで到達可能にする。
        // 変更は即座に保存する（明示的な「保存」ボタンを持たない設計）。保存に失敗しても
        // 実行中のセッションには値が反映されているため処理は継続する（可用性層はフェイルオープン）。
        public bool CreateRestorePointEnabled
        {
            get => _settings.CreateRestorePoint;
            set { _settings.CreateRestorePoint = value; SaveSettings(); OnPropertyChanged(nameof(CreateRestorePointEnabled)); }
        }

        public bool BackupBeforeInstall
        {
            get => _settings.BackupEnabled;
            set { _settings.BackupEnabled = value; SaveSettings(); OnPropertyChanged(nameof(BackupBeforeInstall)); }
        }

        public bool IncludeBetaDrivers
        {
            get => _settings.IncludeBetaDrivers;
            set { _settings.IncludeBetaDrivers = value; SaveSettings(); OnPropertyChanged(nameof(IncludeBetaDrivers)); }
        }

        public bool AutoCheckOnStartup
        {
            get => _settings.AutoUpdateEnabled;
            set { _settings.AutoUpdateEnabled = value; SaveSettings(); OnPropertyChanged(nameof(AutoCheckOnStartup)); }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.Save();
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception ex)
            {
                // 設定が保存できなくても現在のセッションには反映済み。機能全体を止めない
                _logger.LogWarning(ex, "設定の保存に失敗しました");
            }
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
        [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
        [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(InstallAllUpdatesCommand))]
        [NotifyCanExecuteChangedFor(nameof(RollbackSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(BackupSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(InstallCustomDriverCommand))]
        [NotifyCanExecuteChangedFor(nameof(ShowDetailsCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
        private DriverInfo? _selectedUpdate;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RollbackSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(BackupSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ShowDetailsCommand))]
        private DriverInfo? _selectedInstalledDriver;

        /// <summary>選択中インストール済みドライバーの詳細（詳細ペインにバインド）。</summary>
        [ObservableProperty]
        private DriverDetailInfo? _selectedDetail;

        [ObservableProperty]
        private CultureInfo? _selectedCulture;

        [ObservableProperty]
        private AppTheme _selectedTheme;

        public MainViewModel(
            IServiceScopeFactory scopeFactory,
            ILanguageService lang,
            IFileDialogService fileDialog,
            IThemeService themeService,
            ISettingsService settings,
            ILogger<MainViewModel> logger)
        {
            _scopeFactory = scopeFactory;
            _lang = lang;
            _fileDialog = fileDialog;
            _themeService = themeService;
            _settings = settings;
            _logger = logger;
            _selectedCulture = _lang.CurrentCulture;
            _selectedTheme = themeService.CurrentTheme;
        }

        // テーマ切替: SelectedTheme が変わったら ThemeService に反映
        partial void OnSelectedThemeChanged(AppTheme value)
        {
            _themeService.Apply(value);
            // 選択を永続化する。保存できなくてもテーマ自体は適用済みなので処理は継続する
            _settings.ThemeName = value.ToString();
        }

        // 言語切替: SelectedCulture が変わったら実際のカルチャを切り替え、
        // ローカライズ済みラベルすべての再評価を促す
        partial void OnSelectedCultureChanged(CultureInfo? value)
        {
            if (value == null) return;
            _lang.SetCulture(value);
            _settings.CultureName = value.Name;
            OnPropertyChanged(nameof(ScanButtonText));
            OnPropertyChanged(nameof(CheckUpdatesButtonText));
            OnPropertyChanged(nameof(InstallButtonText));
            OnPropertyChanged(nameof(UpdateAllButtonText));
            OnPropertyChanged(nameof(RollbackButtonText));
            OnPropertyChanged(nameof(CustomInstallButtonText));
            OnPropertyChanged(nameof(BackupButtonText));
            OnPropertyChanged(nameof(InstalledTabHeader));
            OnPropertyChanged(nameof(UpdatesTabHeader));
            OnPropertyChanged(nameof(LanguageLabel));
            OnPropertyChanged(nameof(ThemeLabel));
            OnPropertyChanged(nameof(CreateRestorePointLabel));
            OnPropertyChanged(nameof(BackupBeforeInstallLabel));
            OnPropertyChanged(nameof(IncludeBetaLabel));
            OnPropertyChanged(nameof(AutoCheckLabel));
            OnPropertyChanged(nameof(CancelButtonText));
            OnPropertyChanged(nameof(ColumnDeviceNameText));
            OnPropertyChanged(nameof(ColumnVersionText));
            OnPropertyChanged(nameof(ColumnProviderText));
            OnPropertyChanged(nameof(ColumnSourceText));
            OnPropertyChanged(nameof(DetailTitleText));
            OnPropertyChanged(nameof(DetailHintText));
            OnPropertyChanged(nameof(DetailSignatureText));
            OnPropertyChanged(nameof(DetailManufacturerText));
            OnPropertyChanged(nameof(DetailClassText));
            OnPropertyChanged(nameof(DetailStatusText));
            OnPropertyChanged(nameof(DetailPathText));
            OnPropertyChanged(nameof(DetailSizeText));
            OnPropertyChanged(nameof(DetailValidToText));
            OnPropertyChanged(nameof(DetailTrustedChainText));
        }

        // 選択が変わったら以前の詳細表示はクリアする（明示的に「詳細」を押すまで空）
        partial void OnSelectedInstalledDriverChanged(DriverInfo? value) => SelectedDetail = null;

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

            // AvailableUpdates.Count が変わったので「すべて更新」の実行可否を再評価
            InstallAllUpdatesCommand.NotifyCanExecuteChanged();
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
                if (result.IsSuccess())
                    AvailableUpdates.Remove(target);
            }).ConfigureAwait(true);
        }

        private bool CanInstallAll() => !IsBusy && AvailableUpdates.Count > 0;

        [RelayCommand(CanExecute = nameof(CanInstallAll))]
        private async Task InstallAllUpdatesAsync()
        {
            // AvailableUpdates は CheckForUpdatesAsync が DriverInstallOrder で並べた
            // インストール推奨順（チップセット → … → GPU）。この順で逐次インストールする。
            // 成功した項目は一覧から取り除き、途中でキャンセルされたら中断する。
            if (AvailableUpdates.Count == 0) return;

            await RunAsync(_lang.GetString("Status_Updating"), async (driverService, _, ct) =>
            {
                var queue = AvailableUpdates.ToList(); // 反復中に一覧を変更するためスナップショット
                int success = 0, failed = 0, total = queue.Count;
                int rebootRequired = 0; // 成功のうち再起動が必要だった件数
                bool abortedForAdmin = false;

                for (int i = 0; i < queue.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = queue[i];
                    StatusMessage = $"{_lang.GetString("Status_Updating")} ({i + 1}/{total}): {target.DeviceName}";

                    var result = await driverService.InstallDriverUpdateWithResultAsync(target, ct).ConfigureAwait(true);
                    if (result.IsSuccess())
                    {
                        AvailableUpdates.Remove(target);
                        success++;
                        if (result == DriverInstallResult.SuccessRebootRequired) rebootRequired++;
                    }
                    else if (result == DriverInstallResult.AdminRequired)
                    {
                        // AdminRequired は環境要因。残り全件も必ず同じ理由で失敗するため、
                        // N回繰り返さず即中断して1回だけ通知する
                        abortedForAdmin = true;
                        break;
                    }
                    else
                    {
                        // SignatureInvalid/KnownVulnerable/DownloadFailed 等は当該1件固有 → 継続
                        failed++;
                    }
                }

                if (abortedForAdmin)
                {
                    int skipped = total - success - failed;
                    StatusMessage =
                        $"{_lang.GetString("Install_AdminRequired")} " +
                        $"({_lang.GetString("Status_Complete")}: {success} / {total}, {skipped})";
                }
                else
                {
                    StatusMessage = $"{_lang.GetString("Status_Complete")}: {success} / {total}" +
                                    (failed > 0 ? $" ({_lang.GetString("Status_Error")}: {failed})" : string.Empty) +
                                    (rebootRequired > 0 ? $" — {rebootRequired}: {_lang.GetString("Install_RebootRequired")}" : string.Empty);
                }
            }).ConfigureAwait(true);

            InstallAllUpdatesCommand.NotifyCanExecuteChanged();
        }

        private bool CanBackup() => !IsBusy && SelectedInstalledDriver?.DeviceID != null;

        [RelayCommand(CanExecute = nameof(CanBackup))]
        private async Task BackupSelectedAsync()
        {
            var target = SelectedInstalledDriver;
            if (target?.DeviceID == null) return;

            await RunAsync(_lang.GetString("Button_Backup"), async (driverService, _, ct) =>
            {
                bool ok = await driverService.BackupDriverAsync(target.DeviceID, ct).ConfigureAwait(true);
                StatusMessage = ok
                    ? $"{_lang.GetString("Status_Complete")}: {target.DeviceName}"
                    : $"{_lang.GetString("Status_Error")}: {target.DeviceName}";
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

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task InstallCustomDriverAsync()
        {
            var path = _fileDialog.PickDriverFile();
            if (string.IsNullOrEmpty(path)) return; // キャンセル

            await RunAsync(_lang.GetString("Status_Updating"), async (driverService, _, ct) =>
            {
                bool ok = await driverService.InstallCustomDriverAsync(path, ct).ConfigureAwait(true);
                StatusMessage = ok
                    ? $"{_lang.GetString("Status_Complete")}: {path}"
                    : $"{_lang.GetString("Status_Error")}: {path}";
            }).ConfigureAwait(true);
        }

        private bool CanShowDetails() => !IsBusy && SelectedInstalledDriver?.DeviceID != null;

        [RelayCommand(CanExecute = nameof(CanShowDetails))]
        private async Task ShowDetailsAsync()
        {
            var target = SelectedInstalledDriver;
            if (target?.DeviceID == null) return;

            await RunAsync(_lang.GetString("Status_Scanning"), async (driverService, _, ct) =>
            {
                var detail = await driverService.GetDriverDetailsAsync(target.DeviceID, ct).ConfigureAwait(true);
                SelectedDetail = detail;
                StatusMessage = detail != null
                    ? $"{_lang.GetString("Status_Complete")}: {detail.DeviceName}"
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
                // キャンセルはユーザーが選んだ結果でありエラーではない。
                // 以前は Status_Error + 日本語直書きの連結で、非日本語環境では半分だけ翻訳された
                StatusMessage = _lang.GetString("Status_Cancelled");
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

        /// <summary>
        /// インストール結果をユーザー向けメッセージにします。
        /// 理由メッセージは引数なしのリソースキーにし、デバイス名はここで前置きする。
        /// 翻訳側にプレースホルダーを持たせると、10言語のどれか1つで個数がずれた瞬間に
        /// 実行時例外になるため、書式の組み立ては呼び出し側に閉じ込めておく。
        /// </summary>
        private string DescribeResult(DriverInstallResult result, DriverInfo target)
        {
            var name = target.DeviceName ?? string.Empty;

            // WHQL非認定は結果の成否と独立した警告。README が「WHQL未認定なら警告する」と
            // 謳っているのに、以前はログにしか出しておらず、コンソールを持たない
            // WinExe の GUI ではユーザーが一生見られなかった
            var whql = target.IsWHQLCertified
                ? string.Empty
                : $" — {_lang.GetString("Warning_NotWhqlCertified")}";

            if (result == DriverInstallResult.Success)
                return $"{_lang.GetString("Status_Complete")}: {name} {target.DriverVersion}{whql}";

            if (result == DriverInstallResult.SuccessRebootRequired)
                return $"{_lang.GetString("Status_Complete")}: {name} {target.DriverVersion}"
                     + $" — {_lang.GetString("Install_RebootRequired")}{whql}";

            var reason = _lang.GetString(ResultResourceKey(result));
            return (string.IsNullOrEmpty(name) ? reason : $"{name}: {reason}") + whql;
        }

        /// <summary>失敗理由に対応するリソースキー。</summary>
        private static string ResultResourceKey(DriverInstallResult result) => result switch
        {
            DriverInstallResult.AdminRequired        => "Install_AdminRequired",
            DriverInstallResult.NoDownloadUrl        => "Install_NoDownloadUrl",
            DriverInstallResult.InsecureDownloadUrl  => "Install_InsecureUrl",
            DriverInstallResult.DownloadFailed       => "Install_DownloadFailed",
            DriverInstallResult.SignatureInvalid     => "Install_SignatureInvalid",
            DriverInstallResult.KnownVulnerableDriver=> "Install_KnownVulnerable",
            DriverInstallResult.InstallerFailed      => "Install_InstallerFailed",
            DriverInstallResult.Cancelled            => "Install_Cancelled",
            _                                        => "Install_UnknownError",
        };
    }
}
