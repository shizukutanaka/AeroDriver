using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AeroDriver.Core;
using AeroDriver.Core.Helpers;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Languages.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AeroDriver.CLI
{
    public static class Program
    {
        // スクリプト/CI から成否判定できるよう、POSIX 慣例に従った終了コードを返す
        private const int ExitSuccess = 0;
        private const int ExitFailure = 1;
        private const int ExitUsageError = 2;

        private static async Task<int> Main(string[] args)
        {
            // 10言語対応（日本語/中国語/韓国語/ロシア語等の非ASCII文字を含む）の出力を
            // Windowsコンソールの既定コードページ(地域依存の CP932/CP1252 等)で文字化け
            // させないため、明示的にUTF-8へ切り替える。標準出力がリダイレクトされている等
            // 一部の環境では設定に失敗しうるため、失敗しても起動は継続する。
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (IOException)
            {
                // リダイレクト/パイプ等でエンコーディング変更ができない環境。文字化けは
                // 許容し、アプリケーションの起動自体は継続する。
            }

            var services = new ServiceCollection().ConfigureServices();
            // AeroDriver.Languages: 10言語分のリソースがビルド済みだったが未接続だったため接続。
            // OS の UI カルチャに自動追従し、未対応言語は en-US にフォールバックする。
            services.AddSingleton<ILanguageService, LanguageService>();
            using var serviceProvider = services.BuildServiceProvider();

            var lang = serviceProvider.GetRequiredService<ILanguageService>();

            var rootCommand = new RootCommand($"{lang.GetString("AppName")} - {lang.GetString("AppDescription")}");

            var deviceIdOption = new Option<string?>("--device-id", "対象デバイスの DeviceID を指定します");

            // System.CommandLine beta4 の SetHandler は戻り値を直接返せないため、
            // 各ハンドラーの結果は Environment.ExitCode 経由でプロセス終了コードに反映する
            var scanCommand = new Command("scan", "システム内のドライバーをスキャンします");
            scanCommand.SetHandler(async () =>
                Environment.ExitCode = await RunScanAsync(serviceProvider));

            var installAllOption = new Option<bool>("--install-all",
                "確認された更新をインストール推奨順（チップセット→…→GPU）で一括インストールします（管理者権限が必要）");
            var updateCommand = new Command("update", "ドライバー更新を確認し、必要なら一覧表示します")
            { installAllOption };
            updateCommand.SetHandler(async (bool installAll) =>
                Environment.ExitCode = installAll
                    ? await RunInstallAllAsync(serviceProvider)
                    : await RunCheckUpdatesAsync(serviceProvider),
                installAllOption);

            var installCommand = new Command("install", "指定した DeviceID の更新をインストールします（管理者権限が必要）")
            { deviceIdOption };
            installCommand.SetHandler(async (string? deviceId) =>
                Environment.ExitCode = await RunInstallAsync(serviceProvider, deviceId),
                deviceIdOption);

            var backupVersionOption = new Option<string?>("--version",
                "復元するバックアップ世代（省略時は最新）。`backups` コマンドで一覧できます");
            var rollbackCommand = new Command("rollback", "指定した DeviceID をバックアップから復元します（管理者権限が必要）")
            { deviceIdOption, backupVersionOption };
            rollbackCommand.SetHandler(async (string? deviceId, string? version) =>
                Environment.ExitCode = await RunRollbackAsync(serviceProvider, deviceId, version),
                deviceIdOption, backupVersionOption);

            var backupsCommand = new Command("backups",
                "指定した DeviceID の復元可能なバックアップ世代を新しい順に一覧します")
            { deviceIdOption };
            backupsCommand.SetHandler(async (string? deviceId) =>
                Environment.ExitCode = await RunListBackupsAsync(serviceProvider, deviceId),
                deviceIdOption);

            var detailsCommand = new Command("details", "指定した DeviceID の詳細情報を表示します")
            { deviceIdOption };
            detailsCommand.SetHandler(async (string? deviceId) =>
                Environment.ExitCode = await RunDetailsAsync(serviceProvider, deviceId),
                deviceIdOption);

            var historyLimitOption = new Option<int>("--limit",
                () => 20, "表示する履歴の最大件数（0で全件）");
            var historyCommand = new Command("history",
                "ドライバーインストールの履歴（監査証跡）を新しい順に表示します")
            { historyLimitOption };
            historyCommand.SetHandler(async (int limit) =>
                Environment.ExitCode = await RunHistoryAsync(serviceProvider, limit),
                historyLimitOption);

            // config: 設定は Core が尊重しているのに、これまでどの UI からも変更できず
            // 設定ファイルを手編集するしかなかった。到達手段をここで用意する。
            // --set は繰り返し指定する（--set a=b --set c=d）。1トークンに複数値を
            // 許す AllowMultipleArgumentsPerToken は使わない: key=value が空白を含みうるため
            var configSetOption = new Option<string[]>("--set",
                "設定を変更します（key=value。複数指定するときは --set を繰り返す）");
            var configCommand = new Command("config",
                "設定を一覧表示します。--set key=value で変更します")
            { configSetOption };
            configCommand.SetHandler((string[] assignments) =>
                {
                    Environment.ExitCode = RunConfig(serviceProvider, assignments);
                    return Task.CompletedTask;
                },
                configSetOption);

            rootCommand.AddCommand(configCommand);
            rootCommand.AddCommand(scanCommand);
            rootCommand.AddCommand(updateCommand);
            rootCommand.AddCommand(installCommand);
            rootCommand.AddCommand(rollbackCommand);
            rootCommand.AddCommand(detailsCommand);
            rootCommand.AddCommand(historyCommand);
            rootCommand.AddCommand(backupsCommand);

            var parseResult = await rootCommand.InvokeAsync(args);
            // InvokeAsync はパースエラー等で非0を返す。ハンドラー内の失敗は Environment.ExitCode に
            // 設定済みのため、両者のうち「失敗を示す方」を最終終了コードとして採用する
            return parseResult != 0 ? parseResult : Environment.ExitCode;
        }

        /// <summary>
        /// 設定の一覧表示と変更。<c>--set</c> が無ければ現在値を表示するだけ。
        /// 変更は1つでも失敗したら**何も保存せず**使用法エラーで終了する
        /// (一部だけ適用されて設定が中途半端になる方が分かりにくいため)。
        /// </summary>
        private static int RunConfig(IServiceProvider serviceProvider, string[]? assignments)
        {
            using var scope = serviceProvider.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

            if (assignments == null || assignments.Length == 0)
            {
                Console.WriteLine("現在の設定:");
                foreach (var e in SettingsKeys.All)
                {
                    Console.WriteLine($"  {e.Name,-20} {e.Read(settings),-8} {e.Description}");
                    Console.WriteLine($"  {string.Empty,-20} {string.Empty,-8} 値: {e.ValueSyntax}");
                }
                Console.WriteLine();
                Console.WriteLine("変更例: aerodriver config --set restore-point=on --set backup-generations=5");
                return ExitSuccess;
            }

            // まず全件を検証してから保存する。1件でも不正なら設定ファイルは書き換えない。
            foreach (var a in assignments)
            {
                if (!SettingsKeys.TryApply(settings, a, out var error))
                {
                    Console.Error.WriteLine(error);
                    Console.Error.WriteLine("指定できるキー: " +
                        string.Join(", ", SettingsKeys.All.Select(e => e.Name)));
                    return ExitUsageError;
                }
            }

            settings.Save();
            foreach (var a in assignments)
            {
                SettingsKeys.TryParseAssignment(a, out var key, out _);
                var entry = SettingsKeys.Find(key)!;
                Console.WriteLine($"{entry.Name} = {entry.Read(settings)}");
            }
            return ExitSuccess;
        }

        private static async Task<int> RunScanAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();
            var lang = scope.ServiceProvider.GetRequiredService<ILanguageService>();

            try
            {
                Console.WriteLine(lang.GetString("Status_Scanning"));

                var progress = new Progress<DriverScanProgress>(p =>
                    Console.Write($"\r{p.Phase}: {p.Current} 件..."));

                var drivers = await driverService.GetAllDriversAsync(progress);
                Console.WriteLine();
                foreach (var d in drivers)
                {
                    var whqlLabel = d.IsWHQLCertified
                        ? "WHQL"
                        : lang.GetString("Driver_Status_NotWHQL");
                    var gpuTag = d.IsGraphicsDriver ? " [GPU]" : "";
                    Console.WriteLine($"{d.DeviceName,-40} {d.DriverVersion,-15} {whqlLabel}{gpuTag}");
                }

                Console.WriteLine($"\n{lang.GetString("Status_Complete")} ({drivers.Count})");
                return ExitSuccess;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(lang.GetString("Status_Error", ex.Message));
                logger.LogError(ex, "ドライバースキャン中にエラーが発生しました");
                return ExitFailure;
            }
        }

        /// <summary>
        /// インストール履歴（監査証跡）を新しい順に表示します。
        /// 「更新後に不具合が出た。何を戻せばよいか」に答えるためのコマンド。
        /// </summary>
        private static async Task<int> RunHistoryAsync(IServiceProvider serviceProvider, int limit)
        {
            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var history = scope.ServiceProvider.GetRequiredService<IInstallHistoryService>();

            try
            {
                var entries = await history.GetHistoryAsync(limit);
                if (entries.Count == 0)
                {
                    Console.WriteLine("インストール履歴はまだありません。");
                    return ExitSuccess;
                }

                foreach (var e in entries)
                {
                    // 記録はUTC。表示はユーザーのローカル時刻に変換する
                    var when = e.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    var mark = e.Success ? "OK  " : "NG  ";
                    var versions = string.IsNullOrEmpty(e.FromVersion)
                        ? e.ToVersion ?? "?"
                        : $"{e.FromVersion} -> {e.ToVersion}";

                    Console.WriteLine($"{when}  {mark}{e.DeviceName,-36} {versions}");
                    Console.WriteLine($"    結果: {e.Result}  ソース: {e.UpdateSource ?? "-"}  " +
                                      $"バックアップ: {(e.BackupCreated ? "あり" : "なし")}  " +
                                      $"復元ポイント: {(e.RestorePointSequence?.ToString() ?? "なし")}");
                    if (!string.IsNullOrEmpty(e.DeviceId))
                        Console.WriteLine($"    DeviceID: {e.DeviceId}");
                }

                Console.WriteLine($"\n{entries.Count} 件");
                return ExitSuccess;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"履歴の読み込みに失敗しました: {ex.Message}");
                logger.LogError(ex, "インストール履歴の読み込み中にエラーが発生しました");
                return ExitFailure;
            }
        }

        private static async Task<int> RunCheckUpdatesAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();
            var lang = scope.ServiceProvider.GetRequiredService<ILanguageService>();

            try
            {
                Console.WriteLine(lang.GetString("Status_Updating"));

                var updates = await driverService.CheckForUpdatesAsync();
                if (updates.Count == 0)
                {
                    Console.WriteLine(lang.GetString("Driver_Status_UpToDate"));
                    return ExitSuccess;
                }

                foreach (var u in updates)
                {
                    var label = lang.GetString("Driver_Status_UpdateAvailable", u.DriverVersion ?? "?");
                    Console.WriteLine($"{u.DeviceName,-40} {label} ({u.UpdateSource})  [DeviceID: {u.DeviceID}]");
                }

                Console.WriteLine($"\n{lang.GetString("Status_Complete")} ({updates.Count})");
                return ExitSuccess;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(lang.GetString("Status_Error", ex.Message));
                logger.LogError(ex, "更新確認中にエラーが発生しました");
                return ExitFailure;
            }
        }

        private static async Task<int> RunInstallAsync(IServiceProvider serviceProvider, string? deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                Console.Error.WriteLine("エラー: --device-id を指定してください。");
                return ExitUsageError;
            }

            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();
            var lang = scope.ServiceProvider.GetRequiredService<ILanguageService>();

            try
            {
                var updates = await driverService.CheckForUpdatesAsync();
                var target = updates.FirstOrDefault(u =>
                    string.Equals(u.DeviceID, deviceId, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    Console.Error.WriteLine($"DeviceID '{deviceId}' に対する更新が見つかりませんでした。");
                    return ExitFailure;
                }

                var result = await driverService.InstallDriverUpdateWithResultAsync(target);
                Console.WriteLine(DescribeInstallResult(result, target, lang));
                return result.IsSuccess() ? ExitSuccess : ExitFailure;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"権限エラー: {ex.Message}");
                return ExitFailure;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ドライバーインストール中にエラーが発生しました: {DeviceID}", deviceId);
                return ExitFailure;
            }
        }

        /// <summary>
        /// 確認された更新をインストール推奨順（<c>CheckForUpdatesAsync</c> が
        /// <c>DriverInstallOrder</c> で並べた順＝チップセット → … → GPU）で一括インストールする。
        /// </summary>
        private static async Task<int> RunInstallAllAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();
            var lang = scope.ServiceProvider.GetRequiredService<ILanguageService>();

            try
            {
                var updates = await driverService.CheckForUpdatesAsync();
                if (updates.Count == 0)
                {
                    Console.WriteLine(lang.GetString("Driver_Status_UpToDate"));
                    return ExitSuccess;
                }

                int success = 0, failed = 0, total = updates.Count;
                bool abortedForAdmin = false;
                for (int i = 0; i < updates.Count; i++)
                {
                    var target = updates[i];
                    Console.WriteLine($"[{i + 1}/{total}] {target.DeviceName} ...");
                    var result = await driverService.InstallDriverUpdateWithResultAsync(target);
                    Console.WriteLine("  " + DescribeInstallResult(result, target, lang));

                    if (result.IsSuccess())
                    {
                        success++;
                    }
                    else if (result == DriverInstallResult.AdminRequired)
                    {
                        // 環境要因のため残りも必ず失敗する → 即中断（同じ失敗をN回繰り返さない）
                        abortedForAdmin = true;
                        break;
                    }
                    else
                    {
                        failed++;
                    }
                }

                if (abortedForAdmin)
                {
                    int skipped = total - success - failed;
                    Console.Error.WriteLine(
                        $"\n{lang.GetString("Install_AdminRequired")}" +
                        $"（{lang.GetString("Status_Complete")}: {success} / {total}, {skipped}）");
                    return ExitFailure;
                }

                Console.WriteLine($"\n{lang.GetString("Status_Complete")}: {success} / {total}" +
                                  (failed > 0 ? $" ({lang.GetString("Status_Error")}: {failed})" : string.Empty));
                // 1件でも失敗があれば非0終了コード（スクリプトから成否を判定できるように）
                return failed == 0 ? ExitSuccess : ExitFailure;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"権限エラー: {ex.Message}");
                return ExitFailure;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "一括インストール中にエラーが発生しました");
                return ExitFailure;
            }
        }

        /// <summary>
        /// インストール結果をユーザー向けメッセージにします（install / update --install-all 共通）。
        /// 理由は引数なしのリソースキーで訳し、デバイス名はここで前置きする
        /// （翻訳側にプレースホルダーを置くと10言語のどれかで個数がずれた瞬間に実行時例外になる）。
        /// </summary>
        private static string DescribeInstallResult(
            DriverInstallResult result, DriverInfo target, ILanguageService lang)
        {
            var name = target.DeviceName ?? string.Empty;

            if (result == DriverInstallResult.Success)
                return $"{lang.GetString("Status_Complete")}: {name} {target.DriverVersion}";

            if (result == DriverInstallResult.SuccessRebootRequired)
                return $"{lang.GetString("Status_Complete")}: {name} {target.DriverVersion}"
                     + $" ({lang.GetString("Install_RebootRequired")})";

            var reason = lang.GetString(result switch
            {
                DriverInstallResult.AdminRequired         => "Install_AdminRequired",
                DriverInstallResult.NoDownloadUrl         => "Install_NoDownloadUrl",
                DriverInstallResult.InsecureDownloadUrl   => "Install_InsecureUrl",
                DriverInstallResult.DownloadFailed        => "Install_DownloadFailed",
                DriverInstallResult.SignatureInvalid      => "Install_SignatureInvalid",
                DriverInstallResult.KnownVulnerableDriver => "Install_KnownVulnerable",
                DriverInstallResult.InstallerFailed       => "Install_InstallerFailed",
                DriverInstallResult.Cancelled             => "Install_Cancelled",
                _                                         => "Install_UnknownError",
            });
            return string.IsNullOrEmpty(name) ? reason : $"{name}: {reason}";
        }

        /// <summary>
        /// 指定デバイスの復元可能なバックアップ世代を新しい順に一覧します。
        /// 世代が見えないと MaxBackupGenerations が保持している世代を選べないため、
        /// rollback --version と対で「復元を実際に使える」状態にするコマンド。
        /// </summary>
        private static async Task<int> RunListBackupsAsync(IServiceProvider serviceProvider, string? deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                Console.Error.WriteLine("エラー: --device-id を指定してください。");
                return ExitUsageError;
            }

            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();

            try
            {
                var backups = await driverService.GetAvailableBackupsAsync(deviceId);
                if (backups.Count == 0)
                {
                    Console.WriteLine($"バックアップがありません: {deviceId}");
                    return ExitSuccess;
                }

                Console.WriteLine($"復元可能なバックアップ ({backups.Count} 件、新しい順):");
                for (int i = 0; i < backups.Count; i++)
                {
                    // 世代名は backup_yyyyMMddHHmmss の日時部分。読みやすく整形して併記する
                    var v = backups[i];
                    var readable = DateTime.TryParseExact(v, "yyyyMMddHHmmss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal |
                        System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt)
                        ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                        : v;
                    var latestTag = i == 0 ? "  (最新)" : string.Empty;
                    Console.WriteLine($"  {v}   {readable}{latestTag}");
                }

                Console.WriteLine($"\n復元するには: rollback --device-id {deviceId} --version <世代>");
                return ExitSuccess;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"エラー: {ex.Message}");
                return ExitUsageError;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "バックアップ一覧の取得中にエラーが発生しました: {DeviceID}", deviceId);
                Console.Error.WriteLine($"バックアップ一覧の取得に失敗しました: {ex.Message}");
                return ExitFailure;
            }
        }

        private static async Task<int> RunRollbackAsync(
            IServiceProvider serviceProvider, string? deviceId, string? backupVersion = null)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                Console.Error.WriteLine("エラー: --device-id を指定してください。");
                return ExitUsageError;
            }

            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();

            try
            {
                bool success = await driverService.RollbackDriverAsync(deviceId, backupVersion);
                var which = backupVersion ?? "最新";
                Console.WriteLine(success
                    ? $"ロールバック完了: {deviceId} (世代: {which})"
                    : $"ロールバック失敗: {deviceId} (世代: {which})");
                return success ? ExitSuccess : ExitFailure;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"権限エラー: {ex.Message}");
                return ExitFailure;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ロールバック中にエラーが発生しました: {DeviceID}", deviceId);
                return ExitFailure;
            }
        }

        private static async Task<int> RunDetailsAsync(IServiceProvider serviceProvider, string? deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                Console.Error.WriteLine("エラー: --device-id を指定してください。");
                return ExitUsageError;
            }

            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();

            try
            {
                var detail = await driverService.GetDriverDetailsAsync(deviceId);
                if (detail == null)
                {
                    Console.Error.WriteLine($"DeviceID '{deviceId}' が見つかりませんでした。");
                    return ExitFailure;
                }

                Console.WriteLine($"DeviceName:    {detail.DeviceName}");
                Console.WriteLine($"DriverVersion: {detail.DriverVersion}");
                Console.WriteLine($"Manufacturer:  {detail.Manufacturer}");
                Console.WriteLine($"DeviceClass:   {detail.DeviceClass}{(detail.IsGraphicsDriver ? " [GPU]" : "")}");
                Console.WriteLine($"WHQL:          {(detail.IsWHQLCertified ? "はい" : "いいえ")}");
                Console.WriteLine($"Status:        {detail.Status} (StatusInfo={detail.StatusInfo})");

                if (!string.IsNullOrEmpty(detail.Description))
                    Console.WriteLine($"Description:   {detail.Description}");
                if (!string.IsNullOrEmpty(detail.ClassGuid))
                    Console.WriteLine($"ClassGuid:     {detail.ClassGuid}");

                if (!string.IsNullOrEmpty(detail.DriverPath))
                {
                    Console.WriteLine($"DriverPath:    {detail.DriverPath}");
                    Console.WriteLine($"DriverSize:    {detail.DriverSize:N0} bytes");
                }

                if (detail.CertificateInfo is { } cert)
                {
                    Console.WriteLine("\n--- Authenticode署名 ---");
                    Console.WriteLine($"Subject:       {cert.Subject}");
                    Console.WriteLine($"Issuer:        {cert.Issuer}");
                    Console.WriteLine($"ValidFrom:     {cert.ValidFrom}");
                    Console.WriteLine($"ValidTo:       {cert.ValidTo}");
                    Console.WriteLine($"信頼チェーン:  {(cert.IsTrustedChain ? "検証成功" : "検証失敗")}");
                }

                if (detail.Properties.Count > 0)
                {
                    Console.WriteLine("\n--- 生のWMIプロパティ ---");
                    foreach (var (key, value) in detail.Properties.OrderBy(p => p.Key))
                        Console.WriteLine($"{key,-32} {value}");
                }

                return ExitSuccess;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "詳細情報取得中にエラーが発生しました: {DeviceID}", deviceId);
                return ExitFailure;
            }
        }
    }
}
