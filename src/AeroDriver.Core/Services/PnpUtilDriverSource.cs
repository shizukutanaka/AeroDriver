using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// pnputil.exe (Windows 標準ユーティリティ) を使ってドライバーストアを列挙します。
    /// WMI と異なりドライバーストアに存在する全パッケージを取得できます。
    /// 無料・Windows 標準・管理者権限不要（列挙のみ）。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class PnpUtilDriverSource : IDriverUpdateSource
    {
        private readonly ILogger<PnpUtilDriverSource> _logger;
        // null 許容: 未登録(テスト等)なら照合はスキップされる
        private readonly VulnerableDriverBlocklist? _vulnerableDriverBlocklist;

        public string SourceName => "pnputil";

        public PnpUtilDriverSource(
            ILogger<PnpUtilDriverSource> logger,
            VulnerableDriverBlocklist? vulnerableDriverBlocklist = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _vulnerableDriverBlocklist = vulnerableDriverBlocklist;
        }

        public async Task<IReadOnlyList<DriverInfo>> SearchUpdatesAsync(CancellationToken cancellationToken = default)
        {
            // pnputil はインストール済みドライバーのみ列挙するため
            // 「更新」はドライバーストア内の全エントリとして返す
            return await GetInstalledDriversAsync(cancellationToken);
        }

        public async Task<DriverInfo?> FindDriverAsync(string hardwareId, CancellationToken cancellationToken = default)
        {
            var all = await GetInstalledDriversAsync(cancellationToken);
            foreach (var d in all)
            {
                if (string.Equals(d.HardwareID, hardwareId, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            return null;
        }

        /// <summary>
        /// pnputil /enum-drivers /all でドライバーストア全体を列挙します
        /// </summary>
        public async Task<IReadOnlyList<DriverInfo>> GetInstalledDriversAsync(
            CancellationToken cancellationToken = default)
        {
            var output = await RunPnpUtilAsync("/enum-drivers /all", cancellationToken);
            return ParseEnumOutput(output);
        }

        /// <summary>
        /// pnputil /add-driver でINFパッケージを追加します（管理者権限が必要）
        /// </summary>
        public async Task<bool> AddDriverAsync(string infPath, CancellationToken cancellationToken = default)
        {
            // DriverService.InstallDriverUpdateWithResultAsync/InstallCustomDriverAsync と同じ
            // 既知の脆弱ドライバー(BYOVD)照合。この経路からもドライバーストアへの追加が
            // 可能なため、単一のチョークポイントに頼らずここでも適用する
            if (_vulnerableDriverBlocklist != null)
            {
                try
                {
                    if (await _vulnerableDriverBlocklist.IsKnownVulnerableAsync(infPath, cancellationToken).ConfigureAwait(false))
                    {
                        _logger.LogWarning(
                            "既知の脆弱ドライバー(BYOVD悪用実績あり)のため追加を拒否しました: {Path}", infPath);
                        return false;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "脆弱ドライバー照合中にエラーが発生しました(照合をスキップします): {Path}", infPath);
                }
            }

            // ArgumentList: 文字列結合ではなく引数トークンを個別指定 → インジェクション不可
            var output = await RunPnpUtilAsync(["/add-driver", infPath, "/install"], cancellationToken)
                .ConfigureAwait(false);
            bool success = output.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                           output.Contains("正常", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("pnputil /add-driver {Path}: {Result}", infPath, success ? "成功" : "失敗");
            return success;
        }

        /// <summary>
        /// pnputil /delete-driver でドライバーパッケージを削除します（管理者権限が必要）
        /// </summary>
        public async Task<bool> DeleteDriverAsync(string oemInfName, CancellationToken cancellationToken = default)
        {
            var output = await RunPnpUtilAsync(["/delete-driver", oemInfName, "/force"], cancellationToken)
                .ConfigureAwait(false);
            bool success = output.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                           output.Contains("正常", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("pnputil /delete-driver {Inf}: {Result}", oemInfName, success ? "成功" : "失敗");
            return success;
        }

        private async Task<string> RunPnpUtilAsync(string[] args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo("pnputil.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            try
            {
                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("pnputil.exe の起動に失敗しました");

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                if (process.ExitCode != 0)
                {
                    var err = await process.StandardError.ReadToEndAsync(ct);
                    _logger.LogWarning("pnputil 終了コード {Code}: {Error}", process.ExitCode, err);
                }

                return output;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "pnputil.exe の実行中にエラーが発生しました: {Args}",
                    string.Join(" ", args));
                return string.Empty;
            }
        }

        // テストから呼べるよう protected に（sealed でないため override 可能）
    protected virtual IReadOnlyList<DriverInfo> ParseEnumOutputPublic(string output)
        => ParseEnumOutput(output);

    private IReadOnlyList<DriverInfo> ParseEnumOutput(string output)
        {
            var drivers = new List<DriverInfo>();
            if (string.IsNullOrWhiteSpace(output)) return drivers;

            // pnputil 出力は空行区切りのエントリ
            // 例:
            // Published Name:     oem0.inf
            // Original Name:      nvlddmkm.inf
            // Provider Name:      NVIDIA
            // Class Name:         Display adapters
            // Class GUID:         {4D36E968-E325-11CE-BFC1-08002BE10318}
            // Driver Version:     10/27/2023 31.0.15.3699
            // Signer Name:        Microsoft Windows Hardware Compatibility Publisher

            var blocks = output.Split(
                new[] { "\r\n\r\n", "\n\n" },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var block in blocks)
            {
                var driver = ParseBlock(block);
                if (driver != null) drivers.Add(driver);
            }

            _logger.LogInformation("pnputil から {Count} 件のドライバーを取得しました", drivers.Count);
            return drivers;
        }

        private static DriverInfo? ParseBlock(string block)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in block.Split('\n'))
            {
                var colon = line.IndexOf(':');
                if (colon < 0) continue;
                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (!string.IsNullOrEmpty(key)) fields[key] = value;
            }

            if (!fields.TryGetValue("Published Name", out var published)) return null;

            var info = new DriverInfo
            {
                InfName = published,
                DriverProviderName = fields.GetValueOrDefault("Provider Name"),
                DeviceClass = fields.GetValueOrDefault("Class Name"),
                UpdateSource = "pnputil",
                IsWHQLCertified = fields.TryGetValue("Signer Name", out var signer) &&
                                  signer.Contains("Microsoft Windows Hardware Compatibility",
                                      StringComparison.OrdinalIgnoreCase),
            };

            // ドライバーバージョンと日付を分離 "MM/DD/YYYY X.X.X.X"
            if (fields.TryGetValue("Driver Version", out var verLine))
            {
                var parts = verLine.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    if (DateTime.TryParse(parts[0], out var date)) info.DriverDate = date;
                    info.DriverVersion = parts[1];
                }
            }

            return info;
        }
    }
}

