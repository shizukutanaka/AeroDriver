using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using AeroDriver.Core.Events;
using AeroDriver.Core.Helpers;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Services
{
    public partial class DriverService : IDriverService, IDisposable
    {
        private readonly ILogger<DriverService> _logger;
        private readonly ISettingsService _settingsService;
        private readonly IBackupService _backupService;
        private readonly IReadOnlyList<IDriverUpdateSource> _updateSources;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        // WMIキャッシュ — SemaphoreSlim(1,1) で async-safe な排他制御
        // lock() は await をまたげないため使用不可
        private List<DriverInfo>? _cachedDrivers;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
        // WMIスキャン単体のタイムアウト（ユーザーキャンセルとリンク）
        private static readonly TimeSpan WmiScanTimeout = TimeSpan.FromSeconds(60);

        public event EventHandler<UpdatesAvailableEventArgs>? UpdatesAvailable;
        public event EventHandler<UpdatesInstalledEventArgs>? UpdatesInstalled;

        public DriverService(
            ILogger<DriverService> logger,
            ISettingsService settingsService,
            IBackupService backupService,
            IEnumerable<IDriverUpdateSource> updateSources,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _updateSources = (updateSources ?? throw new ArgumentNullException(nameof(updateSources)))
                             .ToList().AsReadOnly();
            _httpClient = (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)))
                          .CreateClient(nameof(DriverService));
        }

        public async Task<List<DriverInfo>> GetAllDriversAsync(
            IProgress<DriverScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // キャッシュヒット確認（ロック前の軽量チェック）
            if (progress == null && _cachedDrivers != null && DateTime.UtcNow < _cacheExpiry)
            {
                LogCacheHit(_logger, _cachedDrivers.Count);
                return new List<DriverInfo>(_cachedDrivers);
            }

            // SemaphoreSlim(1,1) で async-safe 排他 — lock() は await をまたげない
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                // ダブルチェック: 待機中に別スレッドがキャッシュを更新した可能性
                if (progress == null && _cachedDrivers != null && DateTime.UtcNow < _cacheExpiry)
                    return new List<DriverInfo>(_cachedDrivers);

                return await ScanDriversAsync(progress, cancellationToken);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task<List<DriverInfo>> ScanDriversAsync(
            IProgress<DriverScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            var drivers = new List<DriverInfo>();

            int count = 0;
            await foreach (var driver in StreamAllDriversAsync(cancellationToken).ConfigureAwait(false))
            {
                drivers.Add(driver);
                count++;
                progress?.Report(new DriverScanProgress
                {
                    Phase = "スキャン中",
                    Current = count,
                    Total = 0,
                    CurrentDevice = driver.DeviceName,
                });
            }

            _cachedDrivers = drivers;
            _cacheExpiry = DateTime.UtcNow.Add(CacheTtl);
            LogDriversFound(_logger, drivers.Count);
            return drivers;
        }

        /// <summary>
        /// WMI をストリーミングで列挙する IAsyncEnumerable 実装。
        /// BoundedChannel(256) + Wait モードでバックプレッシャーを適用し
        /// メモリ使用量を上限に抑えます。
        /// </summary>
        public async IAsyncEnumerable<DriverInfo> StreamAllDriversAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            // BoundedChannel: producer が consumer より速い場合に WriteAsync をブロック
            // FullMode.Wait = 消費されるまで生産者スレッドを非同期待機させる（スレッドは解放される）
            // capacity=256: メモリ上限の目安（DriverInfo ~200B × 256 ≒ 50KB）
            var channel = Channel.CreateBounded<DriverInfo>(new BoundedChannelOptions(256)
            {
                FullMode    = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = true,
            });

            using var timeoutCts = new CancellationTokenSource(WmiScanTimeout);
            using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            // 生産者: WMI 列挙 → BoundedChannel に書き込み（満杯なら await で待機）
            var producer = Task.Run(async () =>
            {
                try
                {
                    using var session = CimSession.Create(null);
                    var instances = session.QueryInstances(
                        @"root\cimv2", "WQL",
                        "SELECT * FROM Win32_PnPSignedDriver WHERE DriverVersion IS NOT NULL");

                    foreach (var inst in instances)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();
                        var driver = MapCimInstance(inst);
                        // WriteAsync: channel が満杯なら非同期で待機 → バックプレッシャー
                        await channel.Writer.WriteAsync(driver, linkedCts.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, linkedCts.Token);

            // 消費者: IAsyncEnumerable として yield return でストリーミング
            await foreach (var driver in channel.Reader.ReadAllAsync(linkedCts.Token).ConfigureAwait(false))
            {
                yield return driver;
            }

            // 生産者の例外（タイムアウト含む）を再スロー
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested
                                                      && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"WMIドライバースキャンが {WmiScanTimeout.TotalSeconds} 秒でタイムアウトしました");
            }
        }

        private static DriverInfo MapCimInstance(CimInstance inst)
        {
            var driver = new DriverInfo
            {
                DeviceID           = inst.CimInstanceProperties["DeviceID"]?.Value?.ToString(),
                DeviceName         = inst.CimInstanceProperties["DeviceName"]?.Value?.ToString(),
                DriverVersion      = inst.CimInstanceProperties["DriverVersion"]?.Value?.ToString(),
                DriverProviderName = inst.CimInstanceProperties["DriverProviderName"]?.Value?.ToString(),
                InfName            = inst.CimInstanceProperties["InfName"]?.Value?.ToString(),
                HardwareID         = inst.CimInstanceProperties["HardwareID"]?.Value?.ToString(),
                IsWHQLCertified    = inst.CimInstanceProperties["IsSigned"]?.Value is bool signed && signed,
            };
            if (DateTime.TryParse(
                inst.CimInstanceProperties["DriverDate"]?.Value?.ToString(), out var date))
                driver.DriverDate = date;
            return driver;
        }

        public async Task<List<DriverInfo>> CheckForUpdatesAsync(
            IProgress<DriverScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var updates = new List<DriverInfo>();

            try
            {
                // フェーズ 1: インストール済みドライバーをスキャン
                var scanProgress = progress == null ? null : new Progress<DriverScanProgress>(p =>
                    progress.Report(p with { Phase = $"ドライバー検出: {p.Phase}" }));

                var installed = await GetAllDriversAsync(scanProgress, cancellationToken);

                // HardwareID でインデックス化（照合用）
                var installedByHwId = installed
                    .Where(d => !string.IsNullOrEmpty(d.HardwareID))
                    .ToDictionary(d => d.HardwareID!, StringComparer.OrdinalIgnoreCase);

                // フェーズ 2: 全データソースに並列クエリ
                LogQueryingSources(_logger, _updateSources.Count);

                progress?.Report(new DriverScanProgress
                {
                    Phase = "更新確認中",
                    Current = 0,
                    Total = _updateSources.Count,
                });

                int sourcesDone = 0;
                var sourceTasks = _updateSources.Select(async s =>
                {
                    var result = await QuerySourceAsync(s, cancellationToken);
                    var done = Interlocked.Increment(ref sourcesDone);
                    progress?.Report(new DriverScanProgress
                    {
                        Phase = "更新確認中",
                        Current = done,
                        Total = _updateSources.Count,
                        CurrentDevice = s.SourceName,
                    });
                    return result;
                });

                var allCandidates = (await Task.WhenAll(sourceTasks))
                    .SelectMany(x => x)
                    .ToList();

                LogCandidatesFound(_logger, allCandidates.Count);

                // フェーズ 3: インストール済みと照合して「新しいバージョン」のみ返す
                foreach (var candidate in allCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(candidate.HardwareID)) continue;
                    if (!installedByHwId.TryGetValue(candidate.HardwareID, out var current)) continue;
                    if (!VersionHelper.IsNewer(candidate.DriverVersion, current.DriverVersion)) continue;

                    candidate.DeviceID = current.DeviceID;
                    candidate.DeviceName ??= current.DeviceName;
                    updates.Add(candidate);
                }

                // 重複除去（同じ HardwareID で最新バージョンのみ残す）
                updates = updates
                    .GroupBy(u => u.HardwareID, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(u => u.DriverVersion,
                                     StringComparer.OrdinalIgnoreCase).First())
                    .ToList();

                if (updates.Count > 0)
                    UpdatesAvailable?.Invoke(this, new UpdatesAvailableEventArgs(updates));

                LogUpdatesFound(_logger, updates.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー更新確認中にエラーが発生しました");
            }

            return updates;
        }

        private async Task<IReadOnlyList<DriverInfo>> QuerySourceAsync(
            IDriverUpdateSource source, CancellationToken ct)
        {
            try
            {
                var results = await source.SearchUpdatesAsync(ct);
                _logger.LogInformation("  [{Source}] {Count} 件", source.SourceName, results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Source}] クエリ中にエラーが発生しました", source.SourceName);
                return Array.Empty<DriverInfo>();
            }
        }

        public async Task<bool> InstallDriverUpdateAsync(DriverInfo driverUpdate, CancellationToken cancellationToken = default)
        {
            if (driverUpdate == null) throw new ArgumentNullException(nameof(driverUpdate));
            ElevationGuard.ThrowIfNotElevated("ドライバーのインストール");

            try
            {
                _logger.LogInformation("ドライバーをインストールします: {DeviceName} {Version}",
                    driverUpdate.DeviceName, driverUpdate.DriverVersion);

                // WDAC 事前チェック: カーネル強制モードでは非WHQL署名ドライバーがブロックされる
                // 2026年4月以降 Windows 11 24H2+ でクロス署名プログラム廃止に伴い必須
                var wdac = WdacHelper.GetStatus(_logger);
                if (wdac.IsKernelEnforced && !driverUpdate.IsWHQLCertified)
                {
                    _logger.LogWarning(
                        "WDAC カーネル強制モードが有効です。WHQL非認定ドライバーはブロックされる可能性があります: {DeviceName}",
                        driverUpdate.DeviceName);
                }
                else if (wdac.IsAuditMode)
                {
                    _logger.LogInformation("WDAC 監査モード中: ドライバーインストールはログに記録されます");
                }

                if (_settingsService.BackupEnabled)
                    await _backupService.BackupDriverAsync(driverUpdate);

                if (string.IsNullOrEmpty(driverUpdate.DownloadUrl))
                {
                    _logger.LogWarning("ダウンロードURLが指定されていません: {DeviceID}", driverUpdate.DeviceID);
                    return false;
                }

                var tempPath = Path.Combine(Path.GetTempPath(), $"aerodriver_{Guid.NewGuid():N}.tmp");
                try
                {
                    var response = await _httpClient.GetAsync(driverUpdate.DownloadUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    // ArrayPool でダウンロードバッファを再利用: ReadAsByteArrayAsync は LOH に大きな配列を確保するため
                    var contentLength = (int)(response.Content.Headers.ContentLength ?? 4 * 1024 * 1024);
                    var buffer = ArrayPool<byte>.Shared.Rent(contentLength);
                    try
                    {
                        using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                            bufferSize: 81920, useAsync: true);
                        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        int read;
                        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                            await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    bool success = await InstallFromFileAsync(tempPath, driverUpdate.InstallerType, cancellationToken);
                    UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, success));
                    return success;
                }
                finally
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバーインストール中にエラーが発生しました: {DeviceID}", driverUpdate.DeviceID);
                UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, false, ex.Message));
                return false;
            }
        }

        public async Task<bool> RollbackDriverAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));
            ElevationGuard.ThrowIfNotElevated("ドライバーのロールバック");

            try
            {
                _logger.LogInformation("ドライバーをロールバックします: {DeviceID}", deviceId);

                var driver = new DriverInfo { DeviceID = deviceId };

                if (!_backupService.HasBackup(driver))
                {
                    _logger.LogWarning("デバイス {DeviceID} のバックアップが見つかりません", deviceId);
                    return false;
                }

                bool result = await _backupService.RestoreDriverAsync(driver);

                if (result)
                    _logger.LogInformation("ロールバック完了: {DeviceID}", deviceId);
                else
                    _logger.LogError("ロールバック失敗: {DeviceID}", deviceId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ロールバック中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        public async Task<bool> DisableDriverAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));
            ElevationGuard.ThrowIfNotElevated("ドライバーの無効化");

            try
            {
                _logger.LogInformation("ドライバーを無効化します: {DeviceID}", deviceId);
                bool result = await Task.Run(() => SetDriverState(deviceId, enable: false), cancellationToken);
                _logger.LogInformation("ドライバー無効化 {Result}: {DeviceID}", result ? "成功" : "失敗", deviceId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー無効化中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        public async Task<bool> EnableDriverAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));
            ElevationGuard.ThrowIfNotElevated("ドライバーの有効化");

            try
            {
                _logger.LogInformation("ドライバーを有効化します: {DeviceID}", deviceId);
                bool result = await Task.Run(() => SetDriverState(deviceId, enable: true), cancellationToken);
                _logger.LogInformation("ドライバー有効化 {Result}: {DeviceID}", result ? "成功" : "失敗", deviceId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー有効化中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        public async Task<DriverDetailInfo?> GetDriverDetailsAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));

            try
            {
                // アローリスト検証 + WQL エスケープ（WqlSanitizer で多層防御）
                var safeId = WqlSanitizer.SanitizeDeviceId(deviceId);

                return await Task.Run(() =>
                {
                    using var session = CimSession.Create(null);
                    var instances = session.QueryInstances(
                        @"root\cimv2", "WQL",
                        $"SELECT * FROM Win32_PnPSignedDriver WHERE DeviceID = '{safeId}'");

                    foreach (var inst in instances)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        CimProperty? Prop(string name) => inst.CimInstanceProperties[name];

                        var detail = new DriverDetailInfo
                        {
                            DeviceID           = Prop("DeviceID")?.Value?.ToString(),
                            DeviceName         = Prop("DeviceName")?.Value?.ToString(),
                            DriverVersion      = Prop("DriverVersion")?.Value?.ToString(),
                            DriverProviderName = Prop("DriverProviderName")?.Value?.ToString(),
                            InfName            = Prop("InfName")?.Value?.ToString(),
                            HardwareID         = Prop("HardwareID")?.Value?.ToString(),
                            IsWHQLCertified    = Prop("IsSigned")?.Value is bool signed && signed,
                            Manufacturer       = Prop("Manufacturer")?.Value?.ToString(),
                            DeviceClass        = Prop("DeviceClass")?.Value?.ToString(),
                            ClassGuid          = Prop("DeviceClassGUID")?.Value?.ToString(),
                            Description        = Prop("Description")?.Value?.ToString(),
                            Status             = Prop("Status")?.Value?.ToString(),
                        };

                        if (DateTime.TryParse(Prop("DriverDate")?.Value?.ToString(), out var date))
                            detail.DriverDate = date;

                        if (int.TryParse(Prop("ConfigManagerErrorCode")?.Value?.ToString(), out int errCode))
                            detail.StatusInfo = errCode == 0 ? 1 : 3;

                        return detail;
                    }

                    return null;
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー詳細取得中にエラーが発生しました: {DeviceID}", deviceId);
                return null;
            }
        }

        public async Task<bool> InstallCustomDriverAsync(string driverPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(driverPath)) throw new ArgumentException("ドライバーパスが必要です", nameof(driverPath));
            if (!File.Exists(driverPath)) throw new FileNotFoundException("ドライバーファイルが見つかりません", driverPath);
            ElevationGuard.ThrowIfNotElevated("カスタムドライバーのインストール");

            try
            {
                _logger.LogInformation("カスタムドライバーをインストールします: {Path}", driverPath);
                string ext = Path.GetExtension(driverPath).ToLowerInvariant();
                return await InstallFromFileAsync(driverPath, ext.TrimStart('.'), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "カスタムドライバーインストール中にエラーが発生しました: {Path}", driverPath);
                return false;
            }
        }

        public int CompareVersions(string version1, string version2) => VersionHelper.Compare(version1, version2);

        private static bool SetDriverState(string deviceId, bool enable)
        {
            var safeId = WqlSanitizer.SanitizeDeviceId(deviceId);
            using var session = CimSession.Create(null);
            var instances = session.QueryInstances(
                @"root\cimv2", "WQL",
                $"SELECT * FROM Win32_PnPEntity WHERE DeviceID = '{safeId}'");

            foreach (var inst in instances)
            {
                var result = session.InvokeMethod(inst, enable ? "Enable" : "Disable", null);
                return result != null;
            }

            return false;
        }

        private async Task<bool> InstallFromFileAsync(string filePath, string? installerType, CancellationToken ct)
        {
            var ext = (installerType ?? Path.GetExtension(filePath)).ToLowerInvariant().TrimStart('.');

            // ArgumentList を使用して cmd.exe 経由のシェルを排除 → コマンドインジェクション不可
            System.Diagnostics.ProcessStartInfo psi;
            switch (ext)
            {
                case "inf":
                    psi = new System.Diagnostics.ProcessStartInfo("pnputil.exe")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    psi.ArgumentList.Add("/add-driver");
                    psi.ArgumentList.Add(filePath);
                    psi.ArgumentList.Add("/install");
                    break;

                case "exe":
                    psi = new System.Diagnostics.ProcessStartInfo(filePath)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    psi.ArgumentList.Add("/quiet");
                    psi.ArgumentList.Add("/norestart");
                    break;

                case "msi":
                    psi = new System.Diagnostics.ProcessStartInfo("msiexec.exe")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    psi.ArgumentList.Add("/i");
                    psi.ArgumentList.Add(filePath);
                    psi.ArgumentList.Add("/quiet");
                    psi.ArgumentList.Add("/norestart");
                    break;

                default:
                    _logger.LogWarning("未対応のインストーラー形式: {Type}", ext);
                    return false;
            }

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }

        // [LoggerMessage] source generation: ホットパスでのボクシング・文字列アロケーションをゼロにする
        // コンパイル時にIL生成 → LogLevel有効チェックがインライン化され無効時は完全ノーコスト
        [LoggerMessage(Level = LogLevel.Debug,
            Message = "WMIキャッシュを返します ({Count} 件)")]
        private static partial void LogCacheHit(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "{Count} 件のドライバーを検出しました")]
        private static partial void LogDriversFound(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "{Count} 件のデータソースに更新を問い合わせます")]
        private static partial void LogQueryingSources(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "データソース合計 {Count} 件の候補を取得")]
        private static partial void LogCandidatesFound(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "{Count} 件の更新が見つかりました")]
        private static partial void LogUpdatesFound(ILogger logger, int count);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
                _cacheLock.Dispose();
            // HttpClient は IHttpClientFactory が管理するため Dispose しない
            _disposed = true;
        }
    }
}
