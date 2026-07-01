using System;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using AeroDriver.Core;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AeroDriver.CLI
{
    public static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var services = new ServiceCollection().ConfigureServices();
            using var serviceProvider = services.BuildServiceProvider();

            var rootCommand = new RootCommand("AeroDriver - Windows ドライバー管理ツール（無料・オープンソース）");

            var deviceIdOption = new Option<string?>("--device-id", "対象デバイスの DeviceID を指定します");

            var scanCommand = new Command("scan", "システム内のドライバーをスキャンします");
            scanCommand.SetHandler(async () => await RunScanAsync(serviceProvider));

            var updateCommand = new Command("update", "ドライバー更新を確認し、必要なら一覧表示します");
            updateCommand.SetHandler(async () => await RunCheckUpdatesAsync(serviceProvider));

            var installCommand = new Command("install", "指定した DeviceID の更新をインストールします（管理者権限が必要）")
            { deviceIdOption };
            installCommand.SetHandler(async (string? deviceId) => await RunInstallAsync(serviceProvider, deviceId),
                deviceIdOption);

            var rollbackCommand = new Command("rollback", "指定した DeviceID をバックアップから復元します（管理者権限が必要）")
            { deviceIdOption };
            rollbackCommand.SetHandler(async (string? deviceId) => await RunRollbackAsync(serviceProvider, deviceId),
                deviceIdOption);

            rootCommand.AddCommand(scanCommand);
            rootCommand.AddCommand(updateCommand);
            rootCommand.AddCommand(installCommand);
            rootCommand.AddCommand(rollbackCommand);

            return await rootCommand.InvokeAsync(args);
        }

        private static async Task RunScanAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();

            try
            {
                var progress = new Progress<DriverScanProgress>(p =>
                    Console.Write($"\r{p.Phase}: {p.Current} 件..."));

                var drivers = await driverService.GetAllDriversAsync(progress);
                Console.WriteLine();
                foreach (var d in drivers)
                    Console.WriteLine($"{d.DeviceName,-40} {d.DriverVersion,-15} {(d.IsWHQLCertified ? "WHQL" : "未署名")}");

                Console.WriteLine($"\n合計 {drivers.Count} 件のドライバーを検出しました。");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ドライバースキャン中にエラーが発生しました");
            }
        }

        private static async Task RunCheckUpdatesAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();

            try
            {
                var updates = await driverService.CheckForUpdatesAsync();
                if (updates.Count == 0)
                {
                    Console.WriteLine("利用可能な更新はありません。");
                    return;
                }

                foreach (var u in updates)
                    Console.WriteLine($"{u.DeviceName,-40} → {u.DriverVersion,-15} ({u.UpdateSource})  [DeviceID: {u.DeviceID}]");

                Console.WriteLine($"\n{updates.Count} 件の更新が利用可能です。'aerodriver install --device-id <ID>' でインストールできます。");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新確認中にエラーが発生しました");
            }
        }

        private static async Task RunInstallAsync(IServiceProvider serviceProvider, string? deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                Console.Error.WriteLine("エラー: --device-id を指定してください。");
                return;
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
                    return;
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
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"権限エラー: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ドライバーインストール中にエラーが発生しました: {DeviceID}", deviceId);
            }
        }

        private static async Task RunRollbackAsync(IServiceProvider serviceProvider, string? deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                Console.Error.WriteLine("エラー: --device-id を指定してください。");
                return;
            }

            using var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<IDriverService>>();
            var driverService = scope.ServiceProvider.GetRequiredService<IDriverService>();

            try
            {
                bool success = await driverService.RollbackDriverAsync(deviceId);
                Console.WriteLine(success ? $"ロールバック完了: {deviceId}" : $"ロールバック失敗: {deviceId}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"権限エラー: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ロールバック中にエラーが発生しました: {DeviceID}", deviceId);
            }
        }
    }
}
