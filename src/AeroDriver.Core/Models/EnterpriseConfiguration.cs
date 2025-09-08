using System.Text.Json.Serialization;

namespace AeroDriver.Core.Models
{
    /// <summary>
    /// Enterprise-grade configuration model with validation and security controls
    /// </summary>
    public class EnterpriseConfiguration
    {
        /// <summary>
        /// Security configuration settings
        /// </summary>
        public SecuritySettings Security { get; set; } = new();
        
        /// <summary>
        /// Operational configuration settings
        /// </summary>
        public OperationalSettings Operations { get; set; } = new();
        
        /// <summary>
        /// Audit and compliance settings
        /// </summary>
        public AuditSettings Audit { get; set; } = new();
        
        /// <summary>
        /// Performance and monitoring settings
        /// </summary>
        public MonitoringSettings Monitoring { get; set; } = new();
        
        /// <summary>
        /// Backup and recovery settings
        /// </summary>
        public BackupSettings Backup { get; set; } = new();
        
        /// <summary>
        /// Localization settings
        /// </summary>
        public LocalizationSettings Localization { get; set; } = new();
        
        /// <summary>
        /// Configuration metadata
        /// </summary>
        public ConfigurationMetadata Metadata { get; set; } = new();
    }
    
    public class SecuritySettings
    {
        /// <summary>
        /// Require administrator privileges for all operations
        /// </summary>
        public bool RequireAdministratorPrivileges { get; set; } = true;
        
        /// <summary>
        /// Enable WHQL signature verification
        /// </summary>
        public bool EnforceWhqlSignatures { get; set; } = true;
        
        /// <summary>
        /// Maximum allowed file size for driver files (bytes)
        /// </summary>
        public long MaxDriverFileSize { get; set; } = 500 * 1024 * 1024; // 500MB
        
        /// <summary>
        /// Allowed driver file extensions
        /// </summary>
        public string[] AllowedDriverExtensions { get; set; } = { ".inf", ".sys", ".cat", ".dll", ".exe" };
        
        /// <summary>
        /// Enable system integrity validation
        /// </summary>
        public bool EnableSystemIntegrityCheck { get; set; } = true;
        
        /// <summary>
        /// Timeout for security operations (milliseconds)
        /// </summary>
        public int SecurityOperationTimeout { get; set; } = 30000; // 30 seconds
        
        /// <summary>
        /// Enable audit logging for security events
        /// </summary>
        public bool EnableSecurityAuditing { get; set; } = true;
        
        /// <summary>
        /// Block operations in safe mode
        /// </summary>
        public bool BlockOperationsInSafeMode { get; set; } = true;
    }
    
    public class OperationalSettings
    {
        /// <summary>
        /// Maximum concurrent operations
        /// </summary>
        public int MaxConcurrentOperations { get; set; } = Environment.ProcessorCount;
        
        /// <summary>
        /// Default operation timeout (milliseconds)
        /// </summary>
        public int DefaultOperationTimeout { get; set; } = 120000; // 2 minutes
        
        /// <summary>
        /// Automatic retry attempts for failed operations
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;
        
        /// <summary>
        /// Retry delay between attempts (milliseconds)
        /// </summary>
        public int RetryDelayMs { get; set; } = 5000; // 5 seconds
        
        /// <summary>
        /// Enable dry-run mode by default
        /// </summary>
        public bool DefaultDryRun { get; set; } = false;
        
        /// <summary>
        /// Require confirmation for destructive operations
        /// </summary>
        public bool RequireConfirmation { get; set; } = true;
        
        /// <summary>
        /// Cache expiration time (minutes)
        /// </summary>
        public int CacheExpirationMinutes { get; set; } = 10;
    }
    
    public class AuditSettings
    {
        /// <summary>
        /// Enable comprehensive audit logging
        /// </summary>
        public bool EnableAuditLogging { get; set; } = true;
        
        /// <summary>
        /// Audit log file path (if null, uses default location)
        /// </summary>
        public string? AuditLogPath { get; set; }
        
        /// <summary>
        /// Maximum audit log file size (bytes)
        /// </summary>
        public long MaxAuditLogSize { get; set; } = 100 * 1024 * 1024; // 100MB
        
        /// <summary>
        /// Number of audit log files to retain
        /// </summary>
        public int AuditLogRetentionCount { get; set; } = 10;
        
        /// <summary>
        /// Include sensitive information in audit logs
        /// </summary>
        public bool IncludeSensitiveInfo { get; set; } = false;
        
        /// <summary>
        /// Audit log level (Information, Warning, Error, Critical)
        /// </summary>
        public string AuditLogLevel { get; set; } = "Information";
        
        /// <summary>
        /// Enable real-time audit event notifications
        /// </summary>
        public bool EnableRealTimeAuditAlerts { get; set; } = true;
    }
    
    public class MonitoringSettings
    {
        /// <summary>
        /// Enable performance monitoring
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; } = true;
        
        /// <summary>
        /// Performance metric collection interval (seconds)
        /// </summary>
        public int MetricCollectionInterval { get; set; } = 60;
        
        /// <summary>
        /// Enable system health monitoring
        /// </summary>
        public bool EnableHealthMonitoring { get; set; } = true;
        
        /// <summary>
        /// Health check interval (minutes)
        /// </summary>
        public int HealthCheckInterval { get; set; } = 30;
        
        /// <summary>
        /// Enable resource usage monitoring
        /// </summary>
        public bool EnableResourceMonitoring { get; set; } = true;
        
        /// <summary>
        /// Memory usage alert threshold (percentage)
        /// </summary>
        public int MemoryUsageAlertThreshold { get; set; } = 85;
        
        /// <summary>
        /// CPU usage alert threshold (percentage)
        /// </summary>
        public int CpuUsageAlertThreshold { get; set; } = 80;
    }
    
    public class BackupSettings
    {
        /// <summary>
        /// Enable automatic backups before driver operations
        /// </summary>
        public bool EnableAutomaticBackup { get; set; } = true;
        
        /// <summary>
        /// Maximum number of backup generations to retain
        /// </summary>
        public int MaxBackupGenerations { get; set; } = 5;
        
        /// <summary>
        /// Custom backup directory path
        /// </summary>
        public string? CustomBackupPath { get; set; }
        
        /// <summary>
        /// Enable backup compression
        /// </summary>
        public bool EnableBackupCompression { get; set; } = true;
        
        /// <summary>
        /// Enable backup integrity verification
        /// </summary>
        public bool EnableBackupVerification { get; set; } = true;
        
        /// <summary>
        /// Backup retention period (days, 0 = indefinite)
        /// </summary>
        public int BackupRetentionDays { get; set; } = 90;
    }
    
    public class LocalizationSettings
    {
        /// <summary>
        /// Preferred language code (e.g., "en-US", "ja-JP")
        /// </summary>
        public string PreferredLanguage { get; set; } = "auto";
        
        /// <summary>
        /// Fallback language if preferred is not available
        /// </summary>
        public string FallbackLanguage { get; set; } = "en-US";
        
        /// <summary>
        /// Enable right-to-left text support
        /// </summary>
        public bool EnableRightToLeftSupport { get; set; } = false;
        
        /// <summary>
        /// Date and time format preference
        /// </summary>
        public string DateTimeFormat { get; set; } = "auto";
        
        /// <summary>
        /// Number format preference
        /// </summary>
        public string NumberFormat { get; set; } = "auto";
    }
    
    public class ConfigurationMetadata
    {
        /// <summary>
        /// Configuration schema version
        /// </summary>
        public string SchemaVersion { get; set; } = "2.0";
        
        /// <summary>
        /// Configuration created timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Configuration last modified timestamp
        /// </summary>
        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// User who last modified the configuration
        /// </summary>
        public string? LastModifiedBy { get; set; }
        
        /// <summary>
        /// Configuration source (file, registry, environment)
        /// </summary>
        public string Source { get; set; } = "file";
        
        /// <summary>
        /// Configuration change history
        /// </summary>
        public List<ConfigurationChange> ChangeHistory { get; set; } = new();
        
        /// <summary>
        /// Configuration validation errors
        /// </summary>
        [JsonIgnore]
        public List<string> ValidationErrors { get; set; } = new();
    }
    
    public class ConfigurationChange
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? User { get; set; }
        public string? Property { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? Reason { get; set; }
    }
}