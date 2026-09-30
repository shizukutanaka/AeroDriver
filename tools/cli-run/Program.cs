// CLI を実際に動かして、OS ガードとハンドラー・終了コードの配線を検証する。
// offline-verify と同じ Check() 方式。
//
// CLI は非Windowsで早期に停止する(PR #77)。したがって Main 経由では
// ハンドラーに到達できない。そこで2段構えにする:
//   1. Main を実際に呼び、OS ガードが全コマンドを止めることを確認する
//   2. ハンドラーは private static なのでリフレクションで直接呼び、
//      引数検証・終了コード・出力を検証する(ui-run が private ハンドラーへ
//      配線しているのと同じ手法。製品側に検証用の口は開けない)
using System.Reflection;
using AeroDriver.Core;
using AeroDriver.Core.Interfaces;
using AeroDriver.Languages.Services;
using Microsoft.Extensions.DependencyInjection;

internal static class CliRunHarness
{
    private static int _pass, _fail;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {name}  {detail}"); }
    }

    private static readonly Type Cli = typeof(AeroDriver.CLI.Program);

    private static MethodInfo Method(string name) =>
        Cli.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"AeroDriver.CLI.Program.{name} が見つからない");

    /// <summary>標準出力と標準エラーを捕まえて任意の処理を走らせる。</summary>
    private static async Task<(T Value, string Out, string Err)> CaptureAsync<T>(Func<Task<T>> body)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var value = await body().ConfigureAwait(false);
            return (value, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    private static Task<(int, string, string)> InvokeMainAsync(params string[] argv)
        => CaptureAsync(async () =>
        {
            Environment.ExitCode = 0;
            var task = (Task<int>)Method("Main").Invoke(null, new object[] { argv })!;
            int exit = await task.ConfigureAwait(false);
            Environment.ExitCode = 0;
            return exit;
        });

    private static Task<(int, string, string)> InvokeHandlerAsync(string name, params object?[] args)
        => CaptureAsync(async () =>
        {
            var result = Method(name).Invoke(null, args);
            return result is Task<int> t ? await t.ConfigureAwait(false) : (int)result!;
        });

    private static async Task<int> Main()
    {
        // 設定と履歴を実ホームに書かないよう隔離する
        var sandbox = Path.Combine(Path.GetTempPath(), $"aerodriver_clirun_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        Environment.SetEnvironmentVariable("HOME", sandbox);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(sandbox, "share"));

        try
        {
            Console.WriteLine("== OS ガード(非対応OSでは何も実行しない) ==");
            foreach (var argv in new[]
                     {
                         new[] { "scan" }, new[] { "update" }, new[] { "history" },
                         new[] { "config" }, new[] { "install" }, new[] { "--help" },
                     })
            {
                var (exit, _, err) = await InvokeMainAsync(argv);
                Check($"'{string.Join(' ', argv)}' は非対応OSで失敗する", exit != 0, exit.ToString());
                Check($"'{string.Join(' ', argv)}' の理由がリソース経由",
                    err.Contains("[Error_WindowsOnly]"), err.Trim());
            }

            // 以降はハンドラーを直接呼ぶ。DI は本物(ConfigureServices を実際に通す)
            var services = new ServiceCollection().ConfigureServices();
            services.AddSingleton<ILanguageService, LanguageService>();
            using var provider = services.BuildServiceProvider();

            Console.WriteLine("== config: 一覧 ==");
            {
                var (exit, output, _) = await InvokeHandlerAsync("RunConfig", provider, null);
                Check("引数なしは終了コード0", exit == 0, exit.ToString());
                Check("全設定キーを一覧する",
                    output.Contains("restore-point") && output.Contains("backup-generations")
                    && output.Contains("include-beta") && output.Contains("auto-check")
                    && output.Contains("backup"),
                    output);
                Check("見出しがリソース経由", output.Contains("[Cli_CurrentSettings]"), output);
            }

            Console.WriteLine("== config --set: 受理と拒否 ==");
            {
                var settings = provider.GetRequiredService<ISettingsService>();

                settings.CreateRestorePoint = true;
                var (exit, output, _) = await InvokeHandlerAsync(
                    "RunConfig", provider, new[] { "restore-point=off" });
                Check("正しい代入は終了コード0", exit == 0, exit.ToString());
                Check("設定に反映される", !settings.CreateRestorePoint);
                Check("変更後の値を表示する", output.Contains("restore-point = false"), output);

                var (exit2, _, err2) = await InvokeHandlerAsync(
                    "RunConfig", provider, new[] { "backup-generations=0" });
                Check("範囲外の値は使用法エラー(2)", exit2 == 2, exit2.ToString());
                Check("拒否理由を stderr に出す", err2.Trim().Length > 0, "(空)");

                var (exit3, _, err3) = await InvokeHandlerAsync(
                    "RunConfig", provider, new[] { "nope=1" });
                Check("未知のキーは使用法エラー(2)", exit3 == 2, exit3.ToString());
                Check("指定できるキーを案内する", err3.Contains("[Cli_ValidKeys]"), err3);

                var (exit4, _, _) = await InvokeHandlerAsync(
                    "RunConfig", provider, new[] { "restore-point" });
                Check("= が無い代入は使用法エラー(2)", exit4 == 2, exit4.ToString());

                // 「1件でも不正なら何も保存しない」という宣言を実際に確かめる
                settings.BackupEnabled = true;
                var (exit5, _, _) = await InvokeHandlerAsync(
                    "RunConfig", provider, new[] { "backup=off", "nope=1" });
                Check("1件でも不正なら使用法エラー(2)", exit5 == 2, exit5.ToString());
                Check("1件でも不正なら他の変更も保存しない", settings.BackupEnabled,
                    "backup が off になってしまった");
            }

            Console.WriteLine("== device-id を要求するコマンド ==");
            {
                foreach (var (handler, arity) in new[]
                         {
                             ("RunInstallAsync", 2), ("RunListBackupsAsync", 2),
                             ("RunDetailsAsync", 2), ("RunRollbackAsync", 3),
                         })
                {
                    object?[] args = arity == 2
                        ? new object?[] { provider, null }
                        : new object?[] { provider, null, null };
                    var (exit, _, err) = await InvokeHandlerAsync(handler, args);
                    Check($"{handler}: --device-id 無しは使用法エラー(2)", exit == 2, exit.ToString());
                    Check($"{handler}: 理由がリソース経由",
                        err.Contains("[Cli_DeviceIdRequired]"), err.Trim());
                }
            }

            Console.WriteLine("== history(空でも成功する) ==");
            {
                var (exit, output, _) = await InvokeHandlerAsync("RunHistoryAsync", provider, 20);
                Check("履歴が空でも終了コード0", exit == 0, exit.ToString());
                Check("空である旨をリソース経由で伝える", output.Contains("[Cli_NoHistory]"), output);

                var (exit2, _, _) = await InvokeHandlerAsync("RunHistoryAsync", provider, 0);
                Check("--limit 0(全件)でも終了コード0", exit2 == 0, exit2.ToString());
            }

            Console.WriteLine();
            Console.WriteLine($"cli-run: {_pass} passed, {_fail} failed");
            return _fail == 0 ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
