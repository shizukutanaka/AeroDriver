using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AeroDriver.Core;
using AeroDriver.Core.Services;
using AeroDriver.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace AeroDriver.UI
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = Services.GetRequiredService<MainWindow>();
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Services
            services.AddSingleton<ISimpleLogger, SimpleLogger>();
            services.AddSingleton<CoreDriverService>();

            // Views (as transient so they are created new each time if needed, though MainWindow is singleton by nature here)
            services.AddTransient<DashboardView>();
            services.AddTransient<DriversView>();
            services.AddTransient<LogsView>();

            // Main Window
            services.AddSingleton<MainWindow>();
        }
    }
}
