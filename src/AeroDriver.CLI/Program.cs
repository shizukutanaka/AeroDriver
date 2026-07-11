using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AeroDriver.Core;
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

            var updateCommand = new Command("update", "ドライバー更新を確認し、必要なら一覧表示します");
            updateCommand.SetHandler(async () =>
                Environment.ExitCode = await RunCheckUpdatesAsync(serviceProvider));

            var installCommand = new Command("install", "指定した DeviceID の更新をインストールします（管理者権限が必要）")
            { deviceIdOption };
            installCommand.SetHandler(async (string? deviceId) =>
                Environment.ExitCode = await RunInstallAsync(serviceProvider, deviceId),
                deviceIdOption);

            var rollbackCommand = new Command("rollback", "指定した DeviceID をバックアップから復元します（管理者権限が必要）")
            { deviceIdOption };
            rollbackCommand.SetHandler(async (string? deviceId) =>
                Environment.ExitCode = await RunRollbackAsync(serviceProvider, deviceId),
                deviceIdOption);

            var detailsCommand = new Command("details", "指定した DeviceID の詳細情報を表示します")
            { deviceIdOption };
            detailsCommand.SetHandler(async (string? deviceId) =>
                Environment.ExitCode = await RunDetailsAsync(serviceProvider, deviceId),
                deviceIdOption);

            rootCommand.AddCommand(scanCommand);
            rootCommand.AddCommand(updateCommand);
            rootCommand.AddCommand(installCommand);
            rootCommand.AddCommand(rollbackCommand);
            rootCommand.AddCommand(detailsCommand);

            var parseResult = await rootCommand.InvokeAsync(args);
            // InvokeAsync はパースエラー等で非0を返す。ハンドラー内の失敗は Environment.ExitCode に
            // 設定済みのため、両者のうち「失敗を示す方」を最終終了コードとして採用する
            return parseResult != 0 ? parseResult : Environment.ExitCode;
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
                Console.WriteLine(result switch
                {
                    DriverInstallResult.Success => $"インストール完了: {target.DeviceName} {target.DriverVersion}",
                    DriverInstallResult.AdminRequired => "インストール失敗: 管理者権限が必要です。アプリケーションを管理者として実行してください。",
                    DriverInstallResult.NoDownloadUrl => "インストール失敗: ダウンロードURLがありません。",
                    DriverInstallResult.InsecureDownloadUrl => "インストール失敗: ダウンロードURLがHTTPSではありません。",
                    DriverInstallResult.DownloadFailed => "インストール失敗: ダウンロードに失敗しました。ネットワーク接続を確認してください。",
                    DriverInstallResult.SignatureInvalid => "インストール失敗: インストーラーの署名が無効です。",
                    DriverInstallResult.InstallerFailed => $"インストール失敗: {target.DeviceName}",
                    DriverInstallResult.Cancelled => "インストールがキャンセルされました。",
                    _ => $"インストール失敗: 不明なエラー ({target.DeviceName})",
                });
                return result == DriverInstallResult.Success ? ExitSuccess : ExitFailure;
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

        private static async Task<int> RunRollbackAsync(IServiceProvider serviceProvider, string? deviceId)
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
                bool success = await driverService.RollbackDriverAsync(deviceId);
                Console.WriteLine(success ? $"ロールバック完了: {deviceId}" : $"ロールバック失敗: {deviceId}");
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
                    Console.WriteLine($"信頼チェーン:  {(cert.IsWHQLSigned ? "検証成功" : "検証失敗")}");
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
