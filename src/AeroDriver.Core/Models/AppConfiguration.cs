namespace AeroDriver.Core.Models
{
    /// <summary>
    /// アプリケーション全体の設定を管理するモデル
    /// </summary>
    public class AppConfiguration
    {
        // Driver Settings
        public DriverSettings Driver { get; set; } = new();
        
        // Backup Settings
        public BackupSettings Backup { get; set; } = new();
        
        // Performance Settings
        public PerformanceSettings Performance { get; set; } = new();
        
        // System Settings  
        public SystemSettings System { get; set; } = new();
        
        // Advanced Settings
        public AdvancedSettings Advanced { get; set; } = new();
    }
    
    public class DriverSettings
    {
        public bool AutoUpdateEnabled { get; set; } = false;
        public bool IncludeBetaDrivers { get; set; } = false;
        public bool OnlyWHQLCertified { get; set; } = true;
        public int ScanIntervalHours { get; set; } = 24;
        public string[] ExcludedDeviceIds { get; set; } = Array.Empty<string>();
    }
    
    public class BackupSettings
    {
        public bool Enabled { get; set; } = true;
        public int MaxGenerations { get; set; } = 3;
        public string BackupPath { get; set; } = "";
        public bool CompressBackups { get; set; } = false;
        public int RetentionDays { get; set; } = 30;
    }
    
    public class PerformanceSettings
    {
        public int MaxParallelOperations { get; set; } = 4;
        public int CacheExpirationMinutes { get; set; } = 10;
        public int TimeoutSeconds { get; set; } = 300;
        public bool UseLowPriorityMode { get; set; } = false;
        public int MaxMemoryUsageMB { get; set; } = 512;
    }
    
    public class SystemSettings
    {
        public bool VerboseLogging { get; set; } = false;
        public string LogLevel { get; set; } = "Information";
        public string PreferredLanguage { get; set; } = "auto";
        public bool StartMinimized { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
    }
    
    public class AdvancedSettings
    {
        public bool AutoCleanupEnabled { get; set; } = true;
        public int HealthCheckIntervalHours { get; set; } = 24;
        public bool TelemetryEnabled { get; set; } = false;
        public bool ExperimentalFeatures { get; set; } = false;
        public string ProxyUrl { get; set; } = "";
    }
}