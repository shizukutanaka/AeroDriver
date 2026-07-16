using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

                try
                {
                    // COMレイトバインディング: WUApiLibへの参照なしでWUAにアクセス
                    var sessionType = Type.GetTypeFromProgID(UpdateSessionProgId, throwOnError: true)!;
                    dynamic session = Activator.CreateInstance(sessionType)!;
                    dynamic searcher = session.CreateUpdateSearcher();

                    // 未インストールのドライバー更新を検索
                    // WUAクエリ言語: Type='Driver' AND IsInstalled=0
                    dynamic searchResult = searcher.Search("Type='Driver' AND IsInstalled=0");
                    dynamic updates = searchResult.Updates;

                    _logger.LogInformation("WUA から {Count} 件のドライバー更新を取得しました", (int)updates.Count);

                    for (int i = 0; i < (int)updates.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        dynamic update = updates.Item(i);
                        var driver = MapToDriverInfo(update);
                        if (driver != null) results.Add(driver);
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

                return results;
            }, cancellationToken);
        }

        public Task<DriverInfo?> FindDriverAsync(string hardwareId, CancellationToken cancellationToken = default)
        {
            return Task.Run<DriverInfo?>(() =>
            {
                if (string.IsNullOrWhiteSpace(hardwareId)) return null;

                try
                {
                    var sessionType = Type.GetTypeFromProgID(UpdateSessionProgId, throwOnError: true)!;
                    dynamic session = Activator.CreateInstance(sessionType)!;
                    dynamic searcher = session.CreateUpdateSearcher();

                    // HardwareID は WUA クエリでは直接フィルタできないため
                    // DriverClass は使えるが HardwareID は全件取得後フィルタする
                    dynamic searchResult = searcher.Search("Type='Driver' AND IsInstalled=0");
                    dynamic updates = searchResult.Updates;

                    for (int i = 0; i < (int)updates.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        dynamic update = updates.Item(i);

                        // IWindowsDriverUpdate の DriverHardwareID プロパティで照合
                        if (MatchesHardwareId(update, hardwareId))
                        {
                            return MapToDriverInfo(update);
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

                return null;
            }, cancellationToken);
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
                TrySet(() =>
                {
                    string hwId = (string)update.DriverHardwareID;
                    info.HardwareID = hwId;

                    // HardwareID から VEN/DEV を抽出して DeviceID の代替に使う
                    var m = Regex.Match(hwId, @"PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}", RegexOptions.IgnoreCase);
                    if (m.Success) info.DeviceID = m.Value;
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
