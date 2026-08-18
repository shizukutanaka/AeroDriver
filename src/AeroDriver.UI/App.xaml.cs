using System.Linq;
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

            // 保存された選択を復元してから MainWindow を生成する。
            // 生成後に適用すると、初回描画が既定のテーマ/言語で一瞬表示されてしまう
            RestorePreferences(_serviceProvider);

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

        /// <summary>
        /// 前回終了時のテーマ/言語を復元します。設定が無い(初回起動)、値が不正、
        /// 対応外のカルチャの場合は何もせず既定のまま続行します。
        /// </summary>
        private static void RestorePreferences(IServiceProvider provider)
        {
            var settings = provider.GetRequiredService<ISettingsService>();
            var logger = provider.GetService<ILogger<App>>();

            var themeName = settings.ThemeName;
            if (!string.IsNullOrEmpty(themeName) &&
                Enum.TryParse<AppTheme>(themeName, ignoreCase: true, out var theme))
            {
                provider.GetRequiredService<IThemeService>().Apply(theme);
            }

            var cultureName = settings.CultureName;
            if (!string.IsNullOrEmpty(cultureName))
            {
                var lang = provider.GetRequiredService<ILanguageService>();
                // 対応外のカルチャが保存されていても落とさない。
                // SupportedCultures に無ければ無視して OS 既定のままにする
                var match = lang.SupportedCultures.FirstOrDefault(c =>
                    string.Equals(c.Name, cultureName, StringComparison.OrdinalIgnoreCase));
                if (match != null) lang.SetCulture(match);
                else logger?.LogInformation("保存されたカルチャ {Culture} は未対応のため無視します", cultureName);
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
