using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Helpers;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    public class BackupService : IBackupService
    {
        private readonly ILogger<BackupService> _logger;
        private readonly ISettingsService _settings;
        private readonly string _backupRoot;
        // null 許容: 未登録(テスト等)なら復元時の照合はスキップされる
        private readonly VulnerableDriverBlocklist? _vulnerableDriverBlocklist;

        public BackupService(
            ILogger<BackupService> logger,
            ISettingsService settings,
            VulnerableDriverBlocklist? vulnerableDriverBlocklist = null)
            : this(logger, settings, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver", "Backups"), vulnerableDriverBlocklist)
        { }

        // テスト用: バックアップルートを外から指定できる
        protected BackupService(
            ILogger<BackupService> logger,
            ISettingsService settings,
            string backupRoot,
            VulnerableDriverBlocklist? vulnerableDriverBlocklist = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _backupRoot = backupRoot;
            _vulnerableDriverBlocklist = vulnerableDriverBlocklist;
            Directory.CreateDirectory(_backupRoot);
        }

        public async Task<bool> BackupDriverAsync(DriverInfo driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません", nameof(driver));

            try
            {
                var deviceDir = GetDeviceDirectory(driver.DeviceID);
                var backupDir = CreateUniqueBackupDirectory(deviceDir);
                var filesDir = Path.Combine(backupDir, "files");
                Directory.CreateDirectory(filesDir);

                // pnputil /export-driver: ドライバーストアから実際のパッケージ（INF + SYS + 全付属ファイル）を
                // コピーする。Windows 標準・無料。OEM 名（oemN.inf）がある場合のみ実行可能。
                bool exported = false;
                if (!string.IsNullOrEmpty(driver.InfName))
                    exported = await ExportDriverFilesAsync(driver.InfName, filesDir).ConfigureAwait(false);

                if (!exported)
                {
                    _logger.LogWarning(
                        "ドライバーファイルのエクスポートに失敗しました。メタデータのみバックアップします: {DeviceID}",
                        driver.DeviceID);
                    Directory.Delete(filesDir, true);
                }

                var meta = new
                {
                    driver.DeviceID,
                    driver.DeviceName,
                    driver.DriverVersion,
                    driver.InfName,
                    HasFiles = exported,
                    BackupTimeUtc = DateTime.UtcNow,
                };

                // 新規作成したバックアップディレクトリ内のメタデータ。途中まで書かれると
                // その世代が復元不能になるため、他の永続ファイルと同じく置換で書く
                var metaPath = Path.Combine(backupDir, "backup_info.json");
                // 一時ファイル名はプロセスごとに一意にする(固定名だと複数プロセスが衝突する)
                var metaTemp = metaPath + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllTextAsync(
                        metaTemp,
                        JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
                    File.Move(metaTemp, metaPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(metaTemp)) { try { File.Delete(metaTemp); } catch { } }
                }

                _logger.LogInformation("バックアップを作成しました: {BackupDir} (ファイル含む: {HasFiles})",
                    backupDir, exported);

                // ISettingsService.MaxBackupGenerations 未実装時は BackupService が
                // 常に固定3世代でクリーンアップしており、ユーザーが設定を変更しても
                // 一切反映されないバグだった。実際の設定値を参照するよう修正。
                await CleanupOldBackupsAsync(deviceDir, _settings.MaxBackupGenerations).ConfigureAwait(false);
                return true;
            }
            catch (ArgumentException)
            {
                // GetDeviceDirectory はパストラバーサルを検出すると ArgumentException を投げる。
                // これは呼び出し側の誤り(または攻撃入力)であって「バックアップに失敗した」とは
                // 意味が違う。握りつぶして false を返すと、同期版の HasBackup が例外を伝播するのと
                // 挙動が食い違い、呼び出し側は攻撃入力を通常の失敗と区別できなくなる。
                // 規則3の「キャンセルを握りつぶさない」と同じ理由で再スローする
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "バックアップ作成中にエラーが発生しました: {DeviceID}", driver.DeviceID);
                return false;
            }
        }

        /// <summary>
        /// pnputil /export-driver でドライバーストアから実ファイル一式をコピーします。
        /// </summary>
        private async Task<bool> ExportDriverFilesAsync(string infName, string destination)
        {
            var psi = new ProcessStartInfo("pnputil.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/export-driver");
            psi.ArgumentList.Add(infName);
            psi.ArgumentList.Add(destination);

            try
            {
                using var process = Process.Start(psi);
                if (process == null) return false;

                // 標準出力・標準エラーの両方をリダイレクトしているため、WaitForExitAsync を
                // 待つ前に読み取りを開始しておく必要がある。出力が OS のパイプバッファを
                // 超えると子プロセスが書き込みでブロックし、未読のまま待機するとデッドロックする。
                var stdOutTask = process.StandardOutput.ReadToEndAsync();
                var stdErrTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync().ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    var err = await stdErrTask.ConfigureAwait(false);
                    _logger.LogWarning("pnputil /export-driver 終了コード {Code}: {Error}",
                        process.ExitCode, err);
                    return false;
                }

                await stdOutTask.ConfigureAwait(false);
                return Directory.EnumerateFileSystemEntries(destination).Any();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "pnputil /export-driver の実行中にエラーが発生しました: {Inf}", infName);
                return false;
            }
        }

        public async Task<bool> RestoreDriverAsync(DriverInfo driver, string? backupVersion = null)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません", nameof(driver));

            try
            {
                var deviceDir = GetDeviceDirectory(driver.DeviceID);

                // FirstOrDefault() は string? を返す。直後に null 判定して早期 return するため
                // 実挙動は正しかったが、宣言が string で CS8600 が出ていた(宣言を実装に合わせる)
                string? backupDir;
                if (string.IsNullOrEmpty(backupVersion))
                {
                    backupDir = Directory.GetDirectories(deviceDir, "backup_*")
                        .OrderByDescending(d => d)
                        .FirstOrDefault();

                    if (backupDir == null)
                    {
                        _logger.LogWarning("復元可能なバックアップが見つかりません: {DeviceID}", driver.DeviceID);
                        return false;
                    }
                }
                else
                {
                    // "backup_" プレフィックスは先頭セグメントが単独の ".." になることは防ぐが、
                    // backupVersion 内部に埋め込まれた "../" までは防げない
                    // (例: "../../../../Windows/System32" → deviceDir の外へ脱出可能)。
                    // GetDeviceDirectory と同じ多層防御: 正規化後の絶対パスが
                    // deviceDir 配下に収まっていることを確認する。
                    backupDir = Path.GetFullPath(Path.Combine(deviceDir, $"backup_{backupVersion}"));
                    var normalizedDeviceDir = Path.GetFullPath(deviceDir) + Path.DirectorySeparatorChar;
                    if (!backupDir.StartsWith(normalizedDeviceDir, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException(
                            $"バックアップバージョンに無効な文字が含まれています: {backupVersion}", nameof(backupVersion));

                    if (!Directory.Exists(backupDir))
                    {
                        _logger.LogWarning("指定されたバックアップが見つかりません: {Version}", backupVersion);
                        return false;
                    }
                }

                var infoFile = Path.Combine(backupDir, "backup_info.json");
                if (File.Exists(infoFile))
                {
                    var info = await File.ReadAllTextAsync(infoFile).ConfigureAwait(false);
                    _logger.LogInformation("バックアップから復元中: {Info}", info);
                }

                var filesDir = Path.Combine(backupDir, "files");
                if (!Directory.Exists(filesDir))
                {
                    _logger.LogWarning(
                        "このバックアップにはドライバーファイルが含まれていません（メタデータのみ）: {BackupDir}",
                        backupDir);
                    return false;
                }

                var infPath = Directory.EnumerateFiles(filesDir, "*.inf", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (infPath == null)
                {
                    _logger.LogWarning("バックアップ内に INF ファイルが見つかりません: {BackupDir}", backupDir);
                    return false;
                }

                ElevationGuard.ThrowIfNotElevated("ドライバーの復元");

                // 復元は pnputil /add-driver で実ファイルを再登録するインストールと同義のため、
                // DriverService.InstallDriverUpdateWithResultAsync/InstallCustomDriverAsync と同じ
                // 既知の脆弱ドライバー(BYOVD)照合を適用する。バックアップ取得時点では
                // ブロックリストに存在しなかった(後日追加された)ドライバーを素通りさせないため。
                if (await IsAnyFileBlockedAsVulnerableAsync(filesDir).ConfigureAwait(false))
                {
                    _logger.LogWarning("既知の脆弱ドライバーを含むためバックアップからの復元を拒否しました: {BackupDir}", backupDir);
                    return false;
                }

                bool installed = await ReinstallDriverFileAsync(infPath).ConfigureAwait(false);
                if (installed)
                    _logger.LogInformation("ドライバーを復元しました: {BackupDir}", backupDir);
                else
                    _logger.LogError("ドライバー復元失敗（pnputil /add-driver）: {BackupDir}", backupDir);

                return installed;
            }
            catch (ArgumentException)
            {
                // BackupDriverAsync と同じ理由: パストラバーサルの検出結果を
                // 通常の復元失敗と混ぜない(backupVersion 経由の埋め込みトラバーサルを含む)
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー復元中にエラーが発生しました: {DeviceID}", driver.DeviceID);
                return false;
            }
        }

        /// <summary>
        /// バックアップディレクトリ配下の全ファイルを既知の脆弱ドライバー(LOLDriversリスト)と
        /// 照合する。ブロックリスト未登録(null)や照合自体の失敗はfalse(フェイルオープン)—
        /// DriverService.IsBlockedAsVulnerableAsync と同じ方針。
        /// </summary>
        private async Task<bool> IsAnyFileBlockedAsVulnerableAsync(string filesDir)
        {
            if (_vulnerableDriverBlocklist == null) return false;

            try
            {
                foreach (var file in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories))
                {
                    if (await _vulnerableDriverBlocklist.IsKnownVulnerableAsync(file).ConfigureAwait(false))
                    {
                        _logger.LogWarning(
                            "既知の脆弱ドライバー(BYOVD悪用実績あり)を検出しました: {Path}。" +
                            "詳細は https://www.loldrivers.io/ を参照してください", file);
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脆弱ドライバー照合中にエラーが発生しました(照合をスキップします): {Dir}", filesDir);
                return false;
            }
        }

        /// <summary>
        /// pnputil /add-driver でバックアップからドライバーストアへ再インストールします。
        /// </summary>
        private async Task<bool> ReinstallDriverFileAsync(string infPath)
        {
            var psi = new ProcessStartInfo("pnputil.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/add-driver");
            psi.ArgumentList.Add(infPath);
            psi.ArgumentList.Add("/install");

            using var process = Process.Start(psi);
            if (process == null) return false;

            // 標準出力・標準エラーの両方をリダイレクトしているため、片方だけを読み取って
            // 完了を待つと、未読のパイプがバッファを埋めた際に子プロセスがブロックし
            // デッドロックする。両ストリームの読み取りを並行して開始してから待機する。
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            var output = await stdOutTask.ConfigureAwait(false);
            await stdErrTask.ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            // 3010(再起動が必要)・1641(再起動開始済み)は pnputil の「成功」コード。
            // 0 以外を一律に失敗とすると、実際には復元できているのに失敗と報告してしまう
            var outcome = InstallerExitCode.Interpret(process.ExitCode);
            if (outcome == InstallerOutcome.SuccessRebootRequired)
            {
                _logger.LogInformation(
                    "ドライバーを復元しました。変更を有効にするには再起動が必要です (終了コード {ExitCode})",
                    process.ExitCode);
                return true;
            }

            if (outcome != InstallerOutcome.Success)
            {
                // 失敗時のみ pnputil の出力を残す（原因調査の手がかり）
                _logger.LogError(
                    "ドライバー復元失敗 (終了コード {ExitCode}): {Output}", process.ExitCode, output.Trim());
                return false;
            }

            // 成否の根拠は終了コードのみ。pnputil の出力メッセージは**ロケール依存**で、
            // 英語/日本語以外の Windows では成功時も "successfully"/"正常" を含まない。
            // かつてこれを必要条件にしていたため、他言語環境では復元に成功していても
            // 失敗と報告されていた（成功を失敗に変えることしかできない検査だった）。
            return true;
        }

        public async Task CleanupOldBackupsAsync(int maxGenerations)
        {
            if (maxGenerations < 1)
                throw new ArgumentOutOfRangeException(nameof(maxGenerations), "世代数は1以上を指定してください");

            foreach (var deviceDir in Directory.GetDirectories(_backupRoot))
                await CleanupOldBackupsAsync(deviceDir, maxGenerations).ConfigureAwait(false);
        }

        public bool HasBackup(DriverInfo driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません", nameof(driver));

            var deviceDir = GetDeviceDirectory(driver.DeviceID);
            return Directory.Exists(deviceDir) &&
                   Directory.GetDirectories(deviceDir, "backup_*").Length > 0;
        }

        public string[] GetAvailableBackups(DriverInfo driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (string.IsNullOrEmpty(driver.DeviceID))
                throw new ArgumentException("デバイスIDが指定されていません", nameof(driver));

            var deviceDir = GetDeviceDirectory(driver.DeviceID);
            if (!Directory.Exists(deviceDir)) return Array.Empty<string>();

            return Directory.GetDirectories(deviceDir, "backup_*")
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Select(n => n!["backup_".Length..])
                .OrderByDescending(v => v)
                .ToArray();
        }

        /// <summary>
        /// バックアップ世代のディレクトリを作る。名前は秒精度のタイムスタンプだが、
        /// 同一秒に2回バックアップすると名前が衝突し、2回目が1回目を上書きして
        /// **世代管理そのものが黙って壊れる**(`MaxBackupGenerations` も
        /// `GetAvailableBackups` も1件しか見えなくなる)。実際にテストスイートを
        /// 実行して初めてこの欠陥が観測できた。
        /// <para>
        /// 衝突時は `_002`, `_003` … の連番を付ける。復元側は
        /// <c>OrderByDescending(d =&gt; d)</c> の辞書順で新しい世代を選ぶが、
        /// 同一秒内では <c>backup_YYYY…</c> &lt; <c>backup_YYYY…_002</c> となり
        /// 辞書順と時系列が一致するため、既存の順序ロジックを変えずに済む。
        /// <b>連番は3桁ゼロ埋めが必須</b>: 埋めないと <c>_10</c> が <c>_2</c> より
        /// 辞書順で前に来て、同一秒に10件以上作ると順序が逆転する。
        /// </para>
        /// </summary>
        private static string CreateUniqueBackupDirectory(string deviceDir)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var baseDir = Path.Combine(deviceDir, $"backup_{timestamp}");
            if (!Directory.Exists(baseDir))
                return baseDir;

            for (int n = 2; n < 1000; n++)
            {
                var candidate = Path.Combine(deviceDir, $"backup_{timestamp}_{n:D3}");
                if (!Directory.Exists(candidate))
                    return candidate;
            }

            // 同一秒に1000回は現実的に起きない。ここに来たら想定外なので失敗させる
            throw new IOException($"同一秒のバックアップ世代が多すぎます: {baseDir}");
        }

        private string GetDeviceDirectory(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("デバイスIDが必要です", nameof(deviceId));

            var safe = string.Concat(deviceId.Split(Path.GetInvalidFileNameChars()));

            // Path.GetInvalidFileNameChars() には '.' が含まれないため、deviceId が
            // ".." 等の場合そのまま素通りしパストラバーサルを許してしまう
            // (例: --device-id ".." → _backupRoot の親ディレクトリを指してしまう)。
            // 多層防御として、正規化後の絶対パスが _backupRoot 配下に収まっている
            // ことを最終確認する。
            var dir = Path.GetFullPath(Path.Combine(_backupRoot, safe));
            var normalizedRoot = Path.GetFullPath(_backupRoot) + Path.DirectorySeparatorChar;
            if (!dir.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"デバイスIDに無効な文字が含まれています: {deviceId}", nameof(deviceId));

            Directory.CreateDirectory(dir);
            return dir;
        }

        private async Task CleanupOldBackupsAsync(string deviceDir, int maxGenerations)
        {
            var backups = Directory.GetDirectories(deviceDir, "backup_*")
                .OrderByDescending(d => d)
                .ToArray();

            foreach (var old in backups.Skip(maxGenerations))
            {
                try
                {
                    Directory.Delete(old, true);
                    _logger.LogInformation("古いバックアップを削除しました: {Dir}", old);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "バックアップ削除中にエラーが発生しました: {Dir}", old);
                }
            }

            await Task.CompletedTask;
        }
    }
}
