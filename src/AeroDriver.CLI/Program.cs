using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;

namespace AeroDriver.CLI
{
    /// <summary>
    /// CLIアプリケーションのエントリーポイント
    /// </summary>
    public class Program
    {
        private static ServiceProvider? _serviceProvider;
        private static ILogger<Program>? _logger;
        
        public static async Task<int> Main(string[] args)
        {
            try
            {
                // コマンドラインオプションを解析
                var options = CommandLineOptions.Parse(args);
                
                // ヘルプまたはバージョン表示
                if (options.Help)
                {
                    CommandLineOptions.ShowHelp();
                    return 0;
                }
                
                if (options.Version)
                {
                    CommandLineOptions.ShowVersion();
                    return 0;
                }
                
                // DIコンテナを構築
                _serviceProvider = ServiceProviderFactory.CreateServiceProvider(options.Verbose);
                _logger = _serviceProvider.GetService<ILogger<Program>>();
                
                // サービスを初期化
                await ServiceProviderFactory.InitializeServicesAsync(_serviceProvider);
                
                if (!options.Silent)
                {
                    Console.WriteLine("AeroDriver CLI");
                    Console.WriteLine("==============");
                }
                
                // コマンドを実行
                var result = await ExecuteCommandAsync(options);
                
                return result;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                _logger?.LogCritical(ex, "Fatal error occurred");
                return 1;
            }
            finally
            {
                _serviceProvider?.Dispose();
            }
        }
        
        private static async Task<int> ExecuteCommandAsync(CommandLineOptions options)
        {
            if (_serviceProvider == null)
                return 1;
            
            try
            {
                var commandExecutor = new CommandExecutor(_serviceProvider, options);
                
                switch (options.Command)
                {
                    case "":
                    case "list":
                        await commandExecutor.ListDriversAsync();
                        break;
                        
                    case "auto":
                        await commandExecutor.AutoModeAsync();
                        break;
                        
                    case "scan":
                        await commandExecutor.ScanForUpdatesAsync();
                        break;
                        
                    case "update":
                        if (options.Arguments.Length > 0)
                        {
                            await commandExecutor.UpdateDriverAsync(options.Arguments[0]);
                        }
                        else
                        {
                            Console.WriteLine("Error: Device ID required for update command");
                            return 1;
                        }
                        break;
                        
                    case "backup":
                        if (options.Arguments.Length > 0)
                        {
                            await commandExecutor.BackupDriverAsync(options.Arguments[0]);
                        }
                        else
                        {
                            Console.WriteLine("Error: Device ID required for backup command");
                            return 1;
                        }
                        break;
                        
                    case "rollback":
                        if (options.Arguments.Length > 0)
                        {
                            await commandExecutor.RollbackDriverAsync(options.Arguments[0]);
                        }
                        else
                        {
                            Console.WriteLine("Error: Device ID required for rollback command");
                            return 1;
                        }
                        break;
                        
                    case "fix":
                        await commandExecutor.FixDriverIssuesAsync();
                        break;
                        
                    case "diag":
                    case "diagnostics":
                        await commandExecutor.RunDiagnosticsAsync();
                        break;
                        
                    case "info":
                        await commandExecutor.ShowSystemInfoAsync();
                        break;
                        
                    case "health":
                        await commandExecutor.ShowHealthReportAsync();
                        break;
                        
                    case "cleanup":
                        var cleanupType = options.Arguments.Length > 0 ? options.Arguments[0] : "all";
                        await commandExecutor.RunCleanupAsync(cleanupType);
                        break;
                        
                    case "cache":
                        var cacheAction = options.Arguments.Length > 0 ? options.Arguments[0] : "clear";
                        await commandExecutor.ManageCacheAsync(cacheAction);
                        break;
                        
                    case "report":
                        var reportType = options.Arguments.Length > 0 ? options.Arguments[0] : "quick";
                        await commandExecutor.GenerateReportAsync(reportType);
                        break;
                        
                    case "logs":
                        var logFilter = options.Arguments.Length > 0 ? options.Arguments[0] : "recent";
                        await commandExecutor.ViewLogsAsync(logFilter);
                        break;
                        
                    case "settings":
                        await commandExecutor.ShowSettingsAsync();
                        break;
                        
                    case "autoupdate":
                        var action = options.Arguments.Length > 0 ? options.Arguments[0] : "status";
                        await commandExecutor.ManageAutoUpdateAsync(action);
                        break;
                        
                    case "monitor":
                        await commandExecutor.ShowPerformanceMonitorAsync();
                        break;
                        
                    default:
                        Console.WriteLine($"Unknown command: {options.Command}");
                        Console.WriteLine("Use --help for available commands");
                        return 1;
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                var errorHandler = _serviceProvider.GetService<IErrorHandler>();
                if (errorHandler != null)
                {
                    await errorHandler.HandleErrorAsync(ex, $"Command: {options.Command}");
                }
                
                if (!options.Silent)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                }
                
                _logger?.LogError(ex, "Error executing command: {Command}", options.Command);
                return 1;
            }
        }
    }
}