using System;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Services;
using AeroDriver.Core.Extensions;

namespace AeroDriver.UI
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
            
            // Build service provider
            var services = new ServiceCollection();
            ConfigureServices(services, configuration);
            var serviceProvider = services.BuildServiceProvider();
            
            // Initialize logging
            var logger = serviceProvider.GetRequiredService<ILogger<MainWindow>>();
            
            // Set up global exception handling
            Application.ThreadException += (sender, e) =>
            {
                logger.LogError(e.Exception, "Unhandled thread exception");
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{e.Exception.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var exception = e.ExceptionObject as Exception;
                logger.LogCritical(exception, "Unhandled domain exception");
                MessageBox.Show(
                    $"A critical error occurred:\n\n{exception?.Message}",
                    "Critical Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            
            // Run application
            try
            {
                var mainWindow = new MainWindow(serviceProvider);
                Application.Run(mainWindow);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Failed to start application");
                MessageBox.Show(
                    $"Failed to start AeroDriver:\n\n{ex.Message}",
                    "Startup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        
        private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Add configuration
            services.AddSingleton<IConfiguration>(configuration);
            
            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddDebug();
                builder.AddEventLog();
            });
            
            // Register core services
            services.AddAeroDriverCore();
            
            // Register UI-specific services
            services.AddSingleton<IConsoleOutput, WindowsFormsOutput>();
            
            // Override with UI-specific implementations if needed
            services.AddSingleton<ISettingsService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<SettingsService>>();
                var config = provider.GetRequiredService<IConfiguration>();
                return new SettingsService(logger, config);
            });
        }
    }
    
    // Windows Forms specific console output implementation
    public class WindowsFormsOutput : IConsoleOutput
    {
        private readonly ILogger<WindowsFormsOutput> _logger;
        
        public WindowsFormsOutput(ILogger<WindowsFormsOutput> logger)
        {
            _logger = logger;
        }
        
        public void WriteLine(string message)
        {
            _logger.LogInformation(message);
        }
        
        public void WriteError(string message)
        {
            _logger.LogError(message);
        }
        
        public void WriteWarning(string message)
        {
            _logger.LogWarning(message);
        }
        
        public void WriteSuccess(string message)
        {
            _logger.LogInformation($"[SUCCESS] {message}");
        }
        
        public void WriteInfo(string message)
        {
            _logger.LogInformation(message);
        }
        
        public void WriteDebug(string message)
        {
            _logger.LogDebug(message);
        }
        
        public void WriteProgress(string message, int percentage)
        {
            _logger.LogInformation($"{message} - {percentage}%");
        }
        
        public void Clear()
        {
            // Not applicable for Windows Forms
        }
        
        public void WriteTable(string[] headers, string[][] rows)
        {
            // Log as structured data
            _logger.LogInformation("Table data: {@Headers}, {@Rows}", headers, rows);
        }
        
        public void WriteJson(object data)
        {
            _logger.LogInformation("JSON data: {@Data}", data);
        }
        
        public void StartProgress(string message)
        {
            _logger.LogInformation($"Starting: {message}");
        }
        
        public void UpdateProgress(int percentage)
        {
            _logger.LogDebug($"Progress: {percentage}%");
        }
        
        public void CompleteProgress(string message = null)
        {
            _logger.LogInformation($"Completed: {message ?? "Operation finished"}");
        }
    }
}