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

        public BackupService(ILogger<BackupService> logger, ISettingsService settings)
            : this(logger, settings, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver", "Backups"))
        { }

        // テスト用: バックアップルートを外から指定できる
        protected BackupService(ILogger<BackupService> logger, ISettingsService settings, string backupRoot)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _backupRoot = backupRoot;
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
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var backupDir = Path.Combine(deviceDir, $"backup_{timestamp}");
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

                await File.WriteAllTextAsync(
                    Path.Combine(backupDir, "backup_info.json"),
                    JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

                _logger.LogInformation("バックアップを作成しました: {BackupDir} (ファイル含む: {HasFiles})",
                    backupDir, exported);

                // ISettingsService.MaxBackupGenerations 未実装時は BackupService が
                // 常に固定3世代でクリーンアップしており、ユーザーが設定を変更しても
                // 一切反映されないバグだった。実際の設定値を参照するよう修正。
                await CleanupOldBackupsAsync(deviceDir, _settings.MaxBackupGenerations);
                return true;
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

                await process.WaitForExitAsync().ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    var err = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    _logger.LogWarning("pnputil /export-driver 終了コード {Code}: {Error}",
                        process.ExitCode, err);
                    return false;
                }

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

                string backupDir;
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
                    var info = await File.ReadAllTextAsync(infoFile);
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

                bool installed = await ReinstallDriverFileAsync(infPath).ConfigureAwait(false);
                if (installed)
                    _logger.LogInformation("ドライバーを復元しました: {BackupDir}", backupDir);
                else
                    _logger.LogError("ドライバー復元失敗（pnputil /add-driver）: {BackupDir}", backupDir);

                return installed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドライバー復元中にエラーが発生しました: {DeviceID}", driver.DeviceID);
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

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            return process.ExitCode == 0 &&
                   (output.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("正常", StringComparison.OrdinalIgnoreCase));
        }

        public async Task CleanupOldBackupsAsync(int maxGenerations)
        {
            if (maxGenerations < 1)
                throw new ArgumentOutOfRangeException(nameof(maxGenerations), "世代数は1以上を指定してください");

            foreach (var deviceDir in Directory.GetDirectories(_backupRoot))
                await CleanupOldBackupsAsync(deviceDir, maxGenerations);
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
