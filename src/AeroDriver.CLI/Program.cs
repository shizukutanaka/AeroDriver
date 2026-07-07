using System;
using System.CommandLine;
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

            rootCommand.AddCommand(scanCommand);
            rootCommand.AddCommand(updateCommand);
            rootCommand.AddCommand(installCommand);
            rootCommand.AddCommand(rollbackCommand);

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
    }
}
