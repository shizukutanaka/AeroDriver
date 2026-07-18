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
        // null 許容: 未登録(テスト等)なら照合はスキップされる
        private readonly VulnerableDriverBlocklist? _vulnerableDriverBlocklist;
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
            IHttpClientFactory httpClientFactory,
            VulnerableDriverBlocklist? vulnerableDriverBlocklist = null)
        {
            _vulnerableDriverBlocklist = vulnerableDriverBlocklist;
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
                        using (inst) // CimInstance はネイティブMIハンドルを保持するIDisposable
                        {
                            linkedCts.Token.ThrowIfCancellationRequested();
                            var driver = MapCimInstance(inst);
                            // WriteAsync: channel が満杯なら非同期で待機 → バックプレッシャー
                            await channel.Writer.WriteAsync(driver, linkedCts.Token).ConfigureAwait(false);
                        }
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
            var deviceClass = inst.CimInstanceProperties["DeviceClass"]?.Value?.ToString();
            var driver = new DriverInfo
            {
                DeviceID           = inst.CimInstanceProperties["DeviceID"]?.Value?.ToString(),
                DeviceName         = inst.CimInstanceProperties["DeviceName"]?.Value?.ToString(),
                DriverVersion      = inst.CimInstanceProperties["DriverVersion"]?.Value?.ToString(),
                DriverProviderName = inst.CimInstanceProperties["DriverProviderName"]?.Value?.ToString(),
                InfName            = inst.CimInstanceProperties["InfName"]?.Value?.ToString(),
                HardwareID         = inst.CimInstanceProperties["HardwareID"]?.Value?.ToString(),
                IsWHQLCertified    = inst.CimInstanceProperties["IsSigned"]?.Value is bool signed && signed,
                DeviceClass        = deviceClass,
                // Win32_PnPSignedDriver.DeviceClass は "DISPLAY" を返す（大文字・小文字の揺れあり）
                IsGraphicsDriver   = string.Equals(deviceClass, "DISPLAY", StringComparison.OrdinalIgnoreCase),
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
                // 文字列の辞書順ではなく VersionHelper.Compare（数値としてのバージョン比較）で判定する。
                // 辞書順だと "9.5.1" が "10.2.0" より大きいと判定されてしまう
                updates = updates
                    .GroupBy(u => u.HardwareID, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(u => u.DriverVersion,
                                     Comparer<string>.Create(VersionHelper.Compare)).First())
                    .ToList();

                // インストール推奨順に並べ替える（チップセット/ストレージ → … → GPU の順）。
                // 一括インストール時に依存関係の土台が先に入るようにするため
                updates = DriverInstallOrder.Sort(updates).ToList();

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
            catch (OperationCanceledException)
            {
                // ユーザーによる明示的キャンセルは個別ソースの技術的失敗とは異なり、
                // 「このソースは0件」に矮小化せず呼び出し元（CheckForUpdatesAsync）へ伝播させる
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Source}] クエリ中にエラーが発生しました", source.SourceName);
                return Array.Empty<DriverInfo>();
            }
        }

        public async Task<bool> InstallDriverUpdateAsync(DriverInfo driverUpdate, CancellationToken cancellationToken = default)
        {
            var result = await InstallDriverUpdateWithResultAsync(driverUpdate, cancellationToken).ConfigureAwait(false);
            return result == DriverInstallResult.Success;
        }

        /// <summary>
        /// ドライバーをインストールし、失敗理由を区別できる詳細結果を返します。
        /// UI 側はこれを見て「再試行」「管理者として再起動」「手動確認」など適切な導線を選べます。
        /// </summary>
        public async Task<DriverInstallResult> InstallDriverUpdateWithResultAsync(
            DriverInfo driverUpdate, CancellationToken cancellationToken = default)
        {
            if (driverUpdate == null) throw new ArgumentNullException(nameof(driverUpdate));

            if (!ElevationGuard.IsElevated)
            {
                _logger.LogWarning("管理者権限がないためインストールを開始できません: {DeviceID}", driverUpdate.DeviceID);
                return DriverInstallResult.AdminRequired;
            }

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

                if (!driverUpdate.IsWHQLCertified)
                {
                    _logger.LogWarning(
                        "WHQL非認定ドライバーです: {DeviceName}。2026年4月以降、Windows 11 24H2+ では" +
                        "クロス署名プログラム廃止により、WHCP署名のないカーネルドライバーはロードがブロックされます",
                        driverUpdate.DeviceName);
                }

                if (_settingsService.BackupEnabled)
                    await _backupService.BackupDriverAsync(driverUpdate);

                if (string.IsNullOrEmpty(driverUpdate.DownloadUrl))
                {
                    _logger.LogWarning("ダウンロードURLが指定されていません: {DeviceID}", driverUpdate.DeviceID);
                    UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, false, "ダウンロードURLが指定されていません"));
                    return DriverInstallResult.NoDownloadUrl;
                }

                // HTTPS以外は拒否: HTTP経由だと中間者攻撃でダウンロード内容を差し替えられる
                // （インストーラーが任意コード実行に直結するため特に重要）
                if (!Uri.TryCreate(driverUpdate.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
                    downloadUri.Scheme != Uri.UriSchemeHttps)
                {
                    _logger.LogWarning(
                        "ダウンロードURLがHTTPSではないため拒否しました: {Url}", driverUpdate.DownloadUrl);
                    UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, false, "ダウンロードURLがHTTPSではありません"));
                    return DriverInstallResult.InsecureDownloadUrl;
                }

                var tempPath = Path.Combine(Path.GetTempPath(), $"aerodriver_{Guid.NewGuid():N}.tmp");
                try
                {
                    HttpResponseMessage response;
                    try
                    {
                        response = await _httpClient.GetAsync(driverUpdate.DownloadUrl, cancellationToken);
                        response.EnsureSuccessStatusCode();
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogError(ex, "ドライバーダウンロード失敗: {Url}", driverUpdate.DownloadUrl);
                        UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, false, ex.Message));
                        return DriverInstallResult.DownloadFailed;
                    }

                    using (response)
                    {
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
                    }

                    // TOCTOU対策: ダウンロード完了直後から署名検証・インストール実行完了まで
                    // FileShare.Read（書き込み共有なし）のハンドルを保持し続けることで、
                    // 同一ユーザーで動作する別プロセスによる「検証後・実行前」のファイル差し替えを防ぐ。
                    // 読み取り共有は許可しているため、自プロセス内の再オープンや Process.Start による
                    // イメージ読み込みは引き続き可能。
                    using var lockStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                    // 既知の脆弱ドライバー(BYOVD悪用実績あり)との照合。
                    // Authenticode 署名が有効でも脆弱なドライバーは存在するため、署名検証とは独立した層
                    if (await IsBlockedAsVulnerableAsync(tempPath, cancellationToken).ConfigureAwait(false))
                    {
                        UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, false, "既知の脆弱ドライバーです"));
                        return DriverInstallResult.KnownVulnerableDriver;
                    }

                    var installResult = await InstallFromFileAsync(tempPath, driverUpdate.InstallerType, cancellationToken);
                    bool success = installResult == DriverInstallResult.Success;
                    UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, success));
                    return installResult;
                }
                finally
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
            catch (OperationCanceledException)
            {
                // 既存の規約に合わせ、キャンセルは呼び出し元に伝播させる（CheckForUpdatesAsync 等と統一）
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバーインストール中にエラーが発生しました: {DeviceID}", driverUpdate.DeviceID);
                UpdatesInstalled?.Invoke(this, new UpdatesInstalledEventArgs(driverUpdate, false, ex.Message));
                return DriverInstallResult.UnknownError;
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ロールバック中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        // ブートクリティカルな PnP デバイスクラス GUID（無効化すると起動不能になりうる）
        // Microsoft の Device Class GUID 一覧（公開・無料）より抜粋
        private static readonly HashSet<string> BootCriticalClassGuids = new(StringComparer.OrdinalIgnoreCase)
        {
            "{4D36E967-E325-11CE-BFC1-08002BE10318}", // DiskDrive
            "{4D36E97B-E325-11CE-BFC1-08002BE10318}", // SCSIAdapter
            "{4D36E97D-E325-11CE-BFC1-08002BE10318}", // System
            "{4D36E966-E325-11CE-BFC1-08002BE10318}", // Computer
            "{4D36E97E-E325-11CE-BFC1-08002BE10318}", // Volume/VolumeSnapshot
        };

        public async Task<bool> DisableDriverAsync(string deviceId, bool force = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));
            ElevationGuard.ThrowIfNotElevated("ドライバーの無効化");

            try
            {
                if (!force && await IsBootCriticalAsync(deviceId, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogWarning(
                        "ブートクリティカルなデバイスのため無効化を拒否しました（force=true で強制可能）: {DeviceID}",
                        deviceId);
                    return false;
                }

                _logger.LogInformation("ドライバーを無効化します: {DeviceID}", deviceId);
                bool result = await Task.Run(() => SetDriverState(deviceId, enable: false, cancellationToken), cancellationToken);
                _logger.LogInformation("ドライバー無効化 {Result}: {DeviceID}", result ? "成功" : "失敗", deviceId);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー無効化中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        private static Task<bool> IsBootCriticalAsync(string deviceId, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                // Task.Run の CancellationToken は開始前のキャンセルしか防げないため、
                // デリゲート内でも明示的にチェックする
                ct.ThrowIfCancellationRequested();

                var safeId = WqlSanitizer.SanitizeDeviceId(deviceId);
                using var session = CimSession.Create(null);
                var instances = session.QueryInstances(
                    @"root\cimv2", "WQL",
                    $"SELECT ClassGuid FROM Win32_PnPEntity WHERE DeviceID = '{safeId}'");

                foreach (var inst in instances)
                {
                    using (inst) // CimInstance はネイティブMIハンドルを保持するIDisposable
                    {
                        ct.ThrowIfCancellationRequested();
                        var classGuid = inst.CimInstanceProperties["ClassGuid"]?.Value?.ToString();
                        if (classGuid != null && BootCriticalClassGuids.Contains(classGuid))
                            return true;
                    }
                }

                return false;
            }, ct);
        }

        public async Task<bool> EnableDriverAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceId)) throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));
            ElevationGuard.ThrowIfNotElevated("ドライバーの有効化");

            try
            {
                _logger.LogInformation("ドライバーを有効化します: {DeviceID}", deviceId);
                bool result = await Task.Run(() => SetDriverState(deviceId, enable: true, cancellationToken), cancellationToken);
                _logger.LogInformation("ドライバー有効化 {Result}: {DeviceID}", result ? "成功" : "失敗", deviceId);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー有効化中にエラーが発生しました: {DeviceID}", deviceId);
                return false;
            }
        }

        /// <summary>
        /// Win32_PnPEntity.ConfigManagerErrorCode からデバイス状態を判定します。
        /// 0=正常, 22=ユーザーにより無効化, その他非0=エラー, プロパティ取得不可=不明。
        /// コード一覧は Microsoft 公開のデバイスマネージャーエラーコード（無料・公開情報）に基づく。
        /// </summary>
        private static int GetStatusInfo(CimSession session, string safeId)
        {
            try
            {
                var instances = session.QueryInstances(
                    @"root\cimv2", "WQL",
                    $"SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID = '{safeId}'");

                foreach (var inst in instances)
                {
                    using (inst) // CimInstance はネイティブMIハンドルを保持するIDisposable
                    {
                        if (!int.TryParse(
                            inst.CimInstanceProperties["ConfigManagerErrorCode"]?.Value?.ToString(),
                            out int errCode))
                            continue;

                        return errCode switch
                        {
                            0 => 1,  // 正常
                            22 => 4, // ユーザーにより無効化されたデバイス
                            _ => 3,  // その他のエラーコード
                        };
                    }
                }
            }
            catch (Exception)
            {
                // 状態判定に失敗しても詳細取得自体は継続する（不明のまま返す）
            }

            return 0; // 不明
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
                        using (inst) // CimInstance はネイティブMIハンドルを保持するIDisposable
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
                                ClassGuid          = Prop("ClassGuid")?.Value?.ToString(),
                                Description        = Prop("Description")?.Value?.ToString(),
                                Status             = Prop("Status")?.Value?.ToString(),
                            };
                            detail.IsGraphicsDriver = string.Equals(
                                detail.DeviceClass, "DISPLAY", StringComparison.OrdinalIgnoreCase);

                            // Win32_PnPSignedDriver が公開する全プロパティを生データとして保持する。
                            // 上記で個別マッピングした項目以外にも DriverInstalled/DeviceID 等
                            // 診断に有用な情報が含まれるため、無加工でそのまま渡す
                            foreach (var property in inst.CimInstanceProperties)
                            {
                                if (property.Value is not null)
                                    detail.Properties[property.Name] = property.Value.ToString() ?? string.Empty;
                            }

                            if (DateTime.TryParse(Prop("DriverDate")?.Value?.ToString(), out var date))
                                detail.DriverDate = date;

                            // ConfigManagerErrorCode は Win32_PnPSignedDriver には存在しない
                            // （Win32_PnPEntity 固有のプロパティ）ため、別途取得する。
                            detail.StatusInfo = GetStatusInfo(session, safeId);

                            // DriverName は Win32_PnPSignedDriver 上では実体ファイル（.sys 等）への
                            // フルパスを指す。実在すればサイズ取得・署名検証・INF本文の読み取りに使う。
                            detail.DriverPath = Prop("DriverName")?.Value?.ToString();
                            PopulateFileDerivedInfo(detail);

                            return detail;
                        }
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

        /// <summary>
        /// <see cref="DriverDetailInfo.DriverPath"/> が実在するファイルを指している場合に限り、
        /// そこから導出できる情報（サイズ・Authenticode署名・同ディレクトリのINF本文）を埋める。
        /// ファイルアクセス系の例外はすべて握りつぶし、該当フィールドを未設定のまま返す
        /// （詳細情報の欠落であって致命的エラーではないため、ここで全体を失敗させない）。
        /// </summary>
        private static void PopulateFileDerivedInfo(DriverDetailInfo detail)
        {
            if (string.IsNullOrEmpty(detail.DriverPath) || !File.Exists(detail.DriverPath))
                return;

            try
            {
                detail.DriverSize = new FileInfo(detail.DriverPath).Length;
                detail.CertificateInfo = AuthenticodeHelper.GetCertificateInfo(detail.DriverPath);

                if (!string.IsNullOrEmpty(detail.InfName))
                {
                    // InfName は WMI 由来の未検証文字列。".." 等のディレクトリトラバーサルを含んでいても
                    // ドライバーディレクトリ外へ出られないよう、ファイル名部分のみを使用する
                    var infFileName = Path.GetFileName(detail.InfName);
                    var infPath = Path.Combine(Path.GetDirectoryName(detail.DriverPath) ?? string.Empty, infFileName);
                    if (File.Exists(infPath))
                        detail.InfContent = File.ReadAllText(infPath);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                      UnauthorizedAccessException or
                      System.Security.SecurityException or
                      ArgumentException) // InfName(WMI由来の未検証文字列)がPath.Combineに渡せない文字を含む場合
            {
                // ファイルアクセス失敗はベストエフォート項目の欠落として扱う
            }
        }

        public async Task<bool> InstallCustomDriverAsync(string driverPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(driverPath)) throw new ArgumentException("ドライバーパスが必要です", nameof(driverPath));
            ElevationGuard.ThrowIfNotElevated("カスタムドライバーのインストール");
            if (!File.Exists(driverPath)) throw new FileNotFoundException("ドライバーファイルが見つかりません", driverPath);

            try
            {
                _logger.LogInformation("カスタムドライバーをインストールします: {Path}", driverPath);

                if (await IsBlockedAsVulnerableAsync(driverPath, cancellationToken).ConfigureAwait(false))
                    return false;

                string ext = Path.GetExtension(driverPath).ToLowerInvariant();
                var installResult = await InstallFromFileAsync(driverPath, ext.TrimStart('.'), cancellationToken);
                return installResult == DriverInstallResult.Success;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "カスタムドライバーインストール中にエラーが発生しました: {Path}", driverPath);
                return false;
            }
        }

        public int CompareVersions(string version1, string version2) => VersionHelper.Compare(version1, version2);

        private static bool SetDriverState(string deviceId, bool enable, CancellationToken ct)
        {
            // Task.Run の CancellationToken は開始前のキャンセルしか防げないため、
            // デリゲート内でも明示的にチェックする
            ct.ThrowIfCancellationRequested();

            var safeId = WqlSanitizer.SanitizeDeviceId(deviceId);
            using var session = CimSession.Create(null);
            var instances = session.QueryInstances(
                @"root\cimv2", "WQL",
                $"SELECT * FROM Win32_PnPEntity WHERE DeviceID = '{safeId}'");

            foreach (var inst in instances)
            {
                using (inst) // CimInstance はネイティブMIハンドルを保持するIDisposable
                {
                    ct.ThrowIfCancellationRequested();
                    var result = session.InvokeMethod(inst, enable ? "Enable" : "Disable", null);

                    // 呼び出しが非nullを返しても、CIMメソッドの ReturnValue が 0 (成功) とは限らない。
                    // Win32_PnPEntity.Enable/Disable: 0=成功, 非0=各種失敗コード（権限不足・デバイス使用中等）
                    // CimMethodResult.ReturnValue は object 型でボックス化された生の戻り値を保持する
                    return result?.ReturnValue is uint code && code == 0;
                }
            }

            return false;
        }

        /// <summary>
        /// ファイルが既知の脆弱ドライバー(LOLDriversリスト)ならtrueを返しログに記録する。
        /// ブロックリスト未登録(null)や照合自体の失敗はfalse(フェイルオープン)—
        /// 照合はAuthenticode検証に対する追加層であり、単独でインストール可否を決めない。
        /// </summary>
        private async Task<bool> IsBlockedAsVulnerableAsync(string filePath, CancellationToken ct)
        {
            if (_vulnerableDriverBlocklist == null) return false;

            try
            {
                if (await _vulnerableDriverBlocklist.IsKnownVulnerableAsync(filePath, ct).ConfigureAwait(false))
                {
                    _logger.LogWarning(
                        "既知の脆弱ドライバー(BYOVD悪用実績あり)のためインストールを拒否しました: {Path}。" +
                        "詳細は https://www.loldrivers.io/ を参照してください", filePath);
                    return true;
                }
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脆弱ドライバー照合中にエラーが発生しました(照合をスキップします): {Path}", filePath);
                return false;
            }
        }

        private async Task<DriverInstallResult> InstallFromFileAsync(string filePath, string? installerType, CancellationToken ct)
        {
            var ext = (installerType ?? Path.GetExtension(filePath)).ToLowerInvariant().TrimStart('.');

            // EXE/MSI は任意コード実行そのものなので Authenticode 署名を必須にする。
            // HTTPS で配信元の完全性は守られても、ファイル自体の発行元検証は別問題。
            if (ext is "exe" or "msi" && !AuthenticodeHelper.HasValidSignature(filePath))
            {
                _logger.LogWarning("Authenticode 署名が無効または存在しないためインストールを拒否しました: {Path}", filePath);
                return DriverInstallResult.SignatureInvalid;
            }

            // cab はドライバーパッケージ（.inf 等）を格納したキャビネットで pnputil に直接渡せないため、
            // expand.exe で展開してから中の .inf をインストールする
            if (ext == "cab")
                return await InstallFromCabAsync(filePath, ct).ConfigureAwait(false);

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
                    return DriverInstallResult.InstallerFailed;
            }

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return DriverInstallResult.InstallerFailed;

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0 ? DriverInstallResult.Success : DriverInstallResult.InstallerFailed;
        }

        /// <summary>
        /// .cab 形式のドライバーパッケージ（WindowsUpdateAgentSource が InstallerType="cab" として
        /// 供給する）を一時ディレクトリへ展開し、含まれる .inf を pnputil でインストールします。
        /// </summary>
        private async Task<DriverInstallResult> InstallFromCabAsync(string cabPath, CancellationToken ct)
        {
            var extractDir = Path.Combine(Path.GetTempPath(), $"aerodriver_cab_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractDir);
            try
            {
                var expandPsi = new System.Diagnostics.ProcessStartInfo("expand.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                expandPsi.ArgumentList.Add("-F:*");
                expandPsi.ArgumentList.Add(cabPath);
                expandPsi.ArgumentList.Add(extractDir);

                using (var expandProcess = System.Diagnostics.Process.Start(expandPsi))
                {
                    if (expandProcess == null) return DriverInstallResult.InstallerFailed;
                    await expandProcess.WaitForExitAsync(ct);
                    if (expandProcess.ExitCode != 0)
                    {
                        _logger.LogWarning("CABの展開に失敗しました (ExitCode={ExitCode}): {Path}", expandProcess.ExitCode, cabPath);
                        return DriverInstallResult.InstallerFailed;
                    }
                }

                var infPath = Directory.EnumerateFiles(extractDir, "*.inf", SearchOption.AllDirectories).FirstOrDefault();
                if (infPath == null)
                {
                    _logger.LogWarning("展開したCAB内に.infが見つかりませんでした: {Path}", cabPath);
                    return DriverInstallResult.InstallerFailed;
                }

                var psi = new System.Diagnostics.ProcessStartInfo("pnputil.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("/add-driver");
                psi.ArgumentList.Add(infPath);
                psi.ArgumentList.Add("/install");

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null) return DriverInstallResult.InstallerFailed;

                await process.WaitForExitAsync(ct);
                return process.ExitCode == 0 ? DriverInstallResult.Success : DriverInstallResult.InstallerFailed;
            }
            finally
            {
                // 展開先の一時ディレクトリはベストエフォートで削除する（失敗しても致命的ではない）
                try { Directory.Delete(extractDir, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
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
