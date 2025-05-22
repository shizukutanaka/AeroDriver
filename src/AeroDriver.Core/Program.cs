using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Services;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core
{
    public static class Program
    {
        /// <summary>
        /// 依存関係を登録する
        /// </summary>
        public static IServiceCollection ConfigureServices(this IServiceCollection services)
        {
            // ロギングの設定
            services.AddLogging(configure => configure.AddConsole())
                .AddTransient<DriverService>();
            
            // サービスの登録
            services.AddSingleton<IWhqlDatabaseService, WhqlDatabaseService>();
            
            // 設定サービスの登録（実装が必要な場合はここに追加）
            services.AddSingleton<ISettingsService, DummySettingsService>();
            
            // バックアップサービスの登録（実装が必要な場合はここに追加）
            services.AddSingleton<IBackupService, DummyBackupService>();
            
            return services;
        }
    }
    
    // ダミーの設定サービス（実装が必要な場合は適切なクラスに置き換えてください）
    public class DummySettingsService : ISettingsService
    {
        public bool AutoUpdateEnabled { get; set; } = true;
        public bool IncludeBetaDrivers { get; set; } = false;
        public bool BackupEnabled { get; set; } = true;
        public int MaxBackupGenerations { get; set; } = 3;
        
        public void Save()
        {
            // 実装が必要な場合はここに追加
        }
        
        public void ResetToDefaults()
        {
            AutoUpdateEnabled = true;
            IncludeBetaDrivers = false;
            BackupEnabled = true;
            MaxBackupGenerations = 3;
        }
    }
    
    // ダミーのバックアップサービス（実装が必要な場合は適切なクラスに置き換えてください）
    public class DummyBackupService : IBackupService
    {
        public Task<bool> BackupDriverAsync(DriverInfo driver)
        {
            return Task.FromResult(true);
        }
        
        public Task<bool> RestoreDriverAsync(DriverInfo driver, string backupVersion = null)
        {
            return Task.FromResult(true);
        }
        
        public Task CleanupOldBackupsAsync(int maxGenerations)
        {
            return Task.CompletedTask;
        }
        
        public bool HasBackup(DriverInfo driver)
        {
            return false;
        }
        
        public string[] GetAvailableBackups(DriverInfo driver)
        {
            return new string[0];
        }
    }
}
