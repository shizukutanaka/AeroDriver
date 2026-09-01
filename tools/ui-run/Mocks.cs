// 手書きモック。外部モックライブラリは NuGet が使えないため使用しない。
using System.Globalization;
using System.Runtime.CompilerServices;
using AeroDriver.Core.Events;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Languages.Services;
using AeroDriver.UI.Services;

namespace AeroDriver.UiRun
{
    /// <summary>戻り値をシナリオごとに差し込める IDriverService。呼び出し回数も記録する。</summary>
    public sealed class MockDriverService : IDriverService
    {
        public event EventHandler<UpdatesAvailableEventArgs>? UpdatesAvailable;
        public event EventHandler<UpdatesInstalledEventArgs>? UpdatesInstalled;

        public List<DriverInfo> Installed { get; set; } = new();
        public List<DriverInfo> Updates { get; set; } = new();
        /// <summary>InstallDriverUpdateWithResultAsync が順に返す結果。尽きたら最後の値を繰り返す。</summary>
        public Queue<DriverInstallResult> InstallResults { get; } = new();
        public List<string> InstallCalls { get; } = new();
        public bool BackupReturns { get; set; } = true;
        public bool RollbackReturns { get; set; } = true;
        public bool CustomInstallReturns { get; set; } = true;
        public List<string> CustomInstallCalls { get; } = new();
        public DriverDetailInfo? Detail { get; set; }
        public Exception? ThrowOnScan { get; set; }
        /// <summary>
        /// 非null にすると GetAllDriversAsync はこの TCS が完了するまで待ち、
        /// その間キャンセルを監視する。実行中の操作を Cancel ボタンで
        /// 中断する経路を検証するために使う。
        /// </summary>
        public TaskCompletionSource? ScanGate { get; set; }
        public int DisposeCount { get; private set; }
        private DriverInstallResult _lastResult = DriverInstallResult.Success;

        public async Task<List<DriverInfo>> GetAllDriversAsync(
            IProgress<DriverScanProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (ThrowOnScan != null) throw ThrowOnScan;
            if (ScanGate != null)
            {
                // 実サービスと同じく、キャンセルされたら OperationCanceledException を投げる
                var cancelled = new TaskCompletionSource();
                using var reg = cancellationToken.Register(() => cancelled.TrySetResult());
                var winner = await Task.WhenAny(ScanGate.Task, cancelled.Task).ConfigureAwait(false);
                if (winner == cancelled.Task)
                    cancellationToken.ThrowIfCancellationRequested();
            }
            progress?.Report(new DriverScanProgress { Phase = "scan", Current = Installed.Count, Total = Installed.Count });
            return new List<DriverInfo>(Installed);
        }

        public async IAsyncEnumerable<DriverInfo> StreamAllDriversAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var d in Installed) { await Task.Yield(); yield return d; }
        }

        public Task<List<DriverInfo>> CheckForUpdatesAsync(
            IProgress<DriverScanProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DriverInfo>(Updates));

        public Task<bool> InstallDriverUpdateAsync(DriverInfo d, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<DriverInstallResult> InstallDriverUpdateWithResultAsync(DriverInfo d, CancellationToken ct = default)
        {
            InstallCalls.Add(d.DeviceID ?? d.DeviceName ?? "(null)");
            if (InstallResults.Count > 0) _lastResult = InstallResults.Dequeue();
            return Task.FromResult(_lastResult);
        }

        public Task<bool> RollbackDriverAsync(string deviceId, CancellationToken ct = default)
            => Task.FromResult(RollbackReturns);

        public Task<bool> RollbackDriverAsync(string deviceId, string? backupVersion, CancellationToken ct = default)
            => Task.FromResult(RollbackReturns);

        public Task<bool> BackupDriverAsync(string deviceId, CancellationToken ct = default)
            => Task.FromResult(BackupReturns);

        public Task<IReadOnlyList<string>> GetAvailableBackupsAsync(string deviceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<DriverDetailInfo?> GetDriverDetailsAsync(string deviceId, CancellationToken ct = default)
            => Task.FromResult(Detail);

        public Task<bool> InstallCustomDriverAsync(string driverPath, CancellationToken ct = default)
        {
            CustomInstallCalls.Add(driverPath);
            return Task.FromResult(CustomInstallReturns);
        }

        public int CompareVersions(string a, string b) => string.CompareOrdinal(a, b);

        public void Dispose() => DisposeCount++;

        public void RaiseEventsForCompilerWarningSuppression()
        {
            UpdatesAvailable?.Invoke(this, null!);
            UpdatesInstalled?.Invoke(this, null!);
        }
    }

    /// <summary>リソースキー名をそのまま返す。ハードコード文字列の混入を検出しやすくする。</summary>
    public sealed class MockLanguageService : ILanguageService
    {
        public List<string> RequestedKeys { get; } = new();
        public CultureInfo CurrentCulture { get; private set; } = new CultureInfo("en-US");
        public IReadOnlyList<CultureInfo> SupportedCultures { get; } =
            new[] { new CultureInfo("en-US"), new CultureInfo("ja-JP") };

        public string GetString(string name) { RequestedKeys.Add(name); return $"[{name}]"; }
        public string GetString(string name, CultureInfo culture) => GetString(name);
        public string GetString(string name, params object[] args) => GetString(name);
        public void SetCulture(CultureInfo culture) => CurrentCulture = culture;
    }

    public sealed class MockThemeService : IThemeService
    {
        public List<AppTheme> Applied { get; } = new();
        public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;
        public IReadOnlyList<AppTheme> AvailableThemes { get; } = new[] { AppTheme.Light, AppTheme.Dark };
        public void Apply(AppTheme theme) { Applied.Add(theme); CurrentTheme = theme; }
    }

    public sealed class MockFileDialogService : IFileDialogService
    {
        public string? PathToReturn { get; set; }
        public int CallCount { get; private set; }
        public string? PickDriverFile() { CallCount++; return PathToReturn; }
    }

    /// <summary>書き込みを記録するだけの設定サービス。</summary>
    public sealed class MockSettingsService : ISettingsService
    {
        public bool AutoUpdateEnabled { get; set; }
        public bool IncludeBetaDrivers { get; set; }
        public bool BackupEnabled { get; set; }
        public int MaxBackupGenerations { get; set; }
        public bool CreateRestorePoint { get; set; }
        public string? ThemeName { get; set; }
        public string? CultureName { get; set; }
        public int SaveCount { get; private set; }
        /// <summary>設定保存が失敗する環境を再現する。</summary>
        public Exception? ThrowOnSave { get; set; }
        public void Save()
        {
            SaveCount++;
            if (ThrowOnSave != null) throw ThrowOnSave;
        }
        public void ResetToDefaults() { }
    }
}
