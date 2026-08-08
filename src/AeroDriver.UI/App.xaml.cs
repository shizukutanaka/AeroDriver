using System.Windows;
using System.Windows.Threading;
using AeroDriver.Core;
using AeroDriver.Core.Interfaces;
using AeroDriver.Languages.Services;
using AeroDriver.UI.Services;
using AeroDriver.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AeroDriver.UI
{
    /// <summary>
    /// アプリケーションのエントリーポイント。CLI と同じ <see cref="ServiceCollectionExtensions.ConfigureServices"/>
    /// でコアサービスを構成し、その上に UI 固有の登録（ILanguageService / MainViewModel / MainWindow）を重ねる。
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection().ConfigureServices();

            // CLI (Program.cs) と同じく、ILanguageService は UI 層で登録する（コア層は言語に依存しない）
            services.AddSingleton<ILanguageService, LanguageService>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            // ハンドルされない UI 例外でプロセスごと落とさず、ユーザーに提示してログに残す
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var window = _serviceProvider.GetRequiredService<MainWindow>();
            window.Show();

            // ISettingsService.AutoUpdateEnabled（既定 true）が有効なら、起動時に
            // 更新確認を一度だけ自動実行する。ウィンドウ表示後に投げるため起動は遅延しない。
            // インストールは行わない（確認のみ）: 無人での自動インストールは
            // ユーザーの明示的な同意なしにシステムを変更することになるため
            var settings = _serviceProvider.GetRequiredService<ISettingsService>();
            if (settings.AutoUpdateEnabled)
            {
                var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
                _ = viewModel.CheckUpdatesCommand.ExecuteAsync(null);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _serviceProvider?.GetService<ILogger<App>>()?
                .LogError(e.Exception, "UIスレッドで未処理の例外が発生しました");

            MessageBox.Show(
                e.Exception.Message,
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}
