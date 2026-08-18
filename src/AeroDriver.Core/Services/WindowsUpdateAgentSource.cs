using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Helpers;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// Windows Update Agent API (wuapi.dll) を使ってドライバー更新を取得します。
    /// COMレイトバインディングを使用するためビルド時にWUApiLibへの参照は不要です。
    /// 無料・公式・スクレイピング不要。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsUpdateAgentSource : IDriverUpdateSource
    {
        private readonly ILogger<WindowsUpdateAgentSource> _logger;

        // WUA COM ProgID
        private const string UpdateSessionProgId = "Microsoft.Update.Session";

        public string SourceName => "Windows Update Agent";

        public WindowsUpdateAgentSource(ILogger<WindowsUpdateAgentSource> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IReadOnlyList<DriverInfo>> SearchUpdatesAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run<IReadOnlyList<DriverInfo>>(() =>
            {
                var results = new List<DriverInfo>();

                // RCWを finally で確実に解放するため object で保持する(dynamic を直接
                // Marshal に渡すと意図しない RCW を掴む/例外になることがある)。
                // 各 update はマッピング完了後に解放する: 早すぎる解放は読み取り中の
                // プロパティを壊す。外側は逆順に解放する
                object? session = null, searcher = null, searchResult = null, updates = null;
                try
                {
                    // COMレイトバインディング: WUApiLibへの参照なしでWUAにアクセス
                    var sessionType = Type.GetTypeFromProgID(UpdateSessionProgId, throwOnError: true)!;
                    session = Activator.CreateInstance(sessionType)!;
                    dynamic dsession = session;
                    searcher = dsession.CreateUpdateSearcher();
                    dynamic dsearcher = searcher;

                    // 未インストールのドライバー更新を検索
                    // WUAクエリ言語: Type='Driver' AND IsInstalled=0
                    searchResult = dsearcher.Search("Type='Driver' AND IsInstalled=0");
                    dynamic dsearchResult = searchResult;
                    updates = dsearchResult.Updates;
                    dynamic dupdates = updates;

                    int count = (int)dupdates.Count;
                    _logger.LogInformation("WUA から {Count} 件のドライバー更新を取得しました", count);

                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        object update = dupdates.Item(i);
                        try
                        {
                            // マッピング完了後に解放する(早すぎる解放は読み取り中のプロパティを壊す)
                            var driver = MapToDriverInfo(update);
                            if (driver != null) results.Add(driver);
                        }
                        finally
                        {
                            ReleaseCom(update);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x80070422))
                {
                    // Windows Update サービスが無効
                    _logger.LogWarning("Windows Update サービスが無効です (0x80070422)");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Windows Update Agent からのドライバー取得中にエラーが発生しました");
                }
                finally
                {
                    // 逆順に解放(非Windows/COM未生成なら全て null で no-op)
                    ReleaseCom(updates);
                    ReleaseCom(searchResult);
                    ReleaseCom(searcher);
                    ReleaseCom(session);
                }

                return results;
            }, cancellationToken);
        }

        public Task<DriverInfo?> FindDriverAsync(string hardwareId, CancellationToken cancellationToken = default)
        {
            return Task.Run<DriverInfo?>(() =>
            {
                if (string.IsNullOrWhiteSpace(hardwareId)) return null;

                object? session = null, searcher = null, searchResult = null, updates = null;
                try
                {
                    var sessionType = Type.GetTypeFromProgID(UpdateSessionProgId, throwOnError: true)!;
                    session = Activator.CreateInstance(sessionType)!;
                    dynamic dsession = session;
                    searcher = dsession.CreateUpdateSearcher();
                    dynamic dsearcher = searcher;

                    // HardwareID は WUA クエリでは直接フィルタできないため
                    // DriverClass は使えるが HardwareID は全件取得後フィルタする
                    searchResult = dsearcher.Search("Type='Driver' AND IsInstalled=0");
                    dynamic dsearchResult = searchResult;
                    updates = dsearchResult.Updates;
                    dynamic dupdates = updates;

                    int count = (int)dupdates.Count;
                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        object update = dupdates.Item(i);
                        try
                        {
                            // IWindowsDriverUpdate の DriverHardwareID プロパティで照合。
                            // MapToDriverInfo は managed な DriverInfo を返すため update 解放後も安全
                            if (MatchesHardwareId(update, hardwareId))
                                return MapToDriverInfo(update);
                        }
                        finally
                        {
                            ReleaseCom(update);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WUA でのドライバー検索中にエラーが発生しました: {HardwareId}", hardwareId);
                }
                finally
                {
                    ReleaseCom(updates);
                    ReleaseCom(searchResult);
                    ReleaseCom(searcher);
                    ReleaseCom(session);
                }

                return null;
            }, cancellationToken);
        }

        /// <summary>
        /// COM RCW を明示解放します。WUAをポーリングし続けるとネイティブリソースが
        /// GC待ちで滞留するため、使い終わった参照はここで手放します。
        /// null・非COMオブジェクト・二重解放は安全に無視します(解放失敗で機能を止めない)。
        /// </summary>
        private static void ReleaseCom(object? comObject)
        {
            if (comObject == null) return;
            try
            {
                if (Marshal.IsComObject(comObject))
                    Marshal.FinalReleaseComObject(comObject);
            }
            catch (Exception)
            {
                // 解放失敗はログにも値しない(プロセス終了時にどのみち回収される)
            }
        }

        private static bool MatchesHardwareId(dynamic update, string targetId)
        {
            try
            {
                // IWindowsDriverUpdate には DriverHardwareID プロパティがある
                string hwId = update.DriverHardwareID;
                return string.Equals(hwId, targetId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // internal: WUA COM オブジェクトを持ち込めないテスト環境からも
        // マッピングロジック単体を検証できるようにする(dynamic な ExpandoObject 等で代用)
        internal DriverInfo? MapToDriverInfo(dynamic update)
        {
            try
            {
                var info = new DriverInfo
                {
                    DeviceName = (string)update.Title,
                    IsWHQLCertified = true, // Windows Update 経由はすべてMicrosoft署名済み
                    UpdateSource = SourceName,
                };

                // IWindowsDriverUpdate 固有プロパティ（キャストできなければスキップ）。
                // DriverVerVersion がバージョン文字列そのもの。DriverVerDate は日付であり、
                // DriverVersion に日付文字列を入れてしまうと下流のバージョン比較が壊れるため誤用しないこと
                // （DriverDate には別途 DriverVerDate を正しく使っている、下記参照）
                TrySet(() => info.DriverVersion = (string)update.DriverVerVersion);
                TrySet(() => info.DriverProviderName = (string)update.DriverProvider);

                // IUpdate::IsBeta（Windows XP 以降で利用可能）がベータ版判定の正式なシグナル。
                // 取得できない場合は false のまま＝製品版扱いにする（安全側: ベータを
                // 誤ってブロックするより、既定でベータを除外する設定側で守る）
                TrySet(() => info.IsBeta = (bool)update.IsBeta);
                TrySet(() =>
                {
                    string hwId = (string)update.DriverHardwareID;
                    info.HardwareID = hwId;

                    // HardwareID から正規形を取り出して DeviceID の代替に使う。
                    // PCI(VEN/DEV)だけでなく USB(VID/PID)も解決する。複合USBデバイスは
                    // インターフェースごとにドライバーが割り当たるため MI まで含めた形を使う
                    if (HardwareIdParser.TryParse(hwId, out var parsedId) && parsedId != null)
                        info.DeviceID = parsedId.QualifiedId;
                });

                // ダウンロードURL（最初のコンテンツのみ）
                TrySet(() =>
                {
                    dynamic content = update.DownloadContents.Item(0);
                    info.DownloadUrl = (string)content.DownloadUrl;

                    string url = info.DownloadUrl.ToLowerInvariant();
                    info.InstallerType = url.EndsWith(".exe") ? "exe"
                        : url.EndsWith(".msi") ? "msi"
                        : url.EndsWith(".cab") ? "cab"
                        : "inf";
                });

                // DriverDate
                TrySet(() =>
                {
                    object verDate = update.DriverVerDate;
                    if (verDate is DateTime dt) info.DriverDate = dt;
                });

                return info;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WUA 更新エントリのマッピング中にエラーが発生しました");
                return null;
            }
        }

        private static void TrySet(Action action)
        {
            try { action(); } catch { /* WUA プロパティは実装によっては存在しない */ }
        }
    }
}
