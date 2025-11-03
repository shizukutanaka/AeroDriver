using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;

namespace AeroDriver.Core.Configuration;

[SupportedOSPlatform("windows")]
public sealed class ConfigurationManager
{
    private static ConfigurationManager? _instance;
    private static readonly object _lock = new();

    private readonly string _configPath;
    private readonly string _configDir;
    private readonly string _encryptedConfigPath;
    private readonly Dictionary<string, object> _settings;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConfigurationMode _mode;
    private readonly bool _encryptionEnabled;

    private ConfigurationManager(ConfigurationMode mode = ConfigurationMode.Personal)
    {
        _mode = mode;
        _configDir = mode == ConfigurationMode.Enterprise
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AeroDriver")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroDriver");

        try
        {
            Directory.CreateDirectory(_configDir);
        }
        catch (Exception ex)
        {
            throw new ConfigurationException($"Failed to create configuration directory: {ex.Message}", ex);
        }

        _configPath = Path.Combine(_configDir, mode == ConfigurationMode.Enterprise ? "enterprise_config.json" : "config.json");
        _encryptedConfigPath = Path.Combine(_configDir, "config.encrypted");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        _encryptionEnabled = ShouldEnableEncryption();
        _settings = LoadSettings();

        if (mode == ConfigurationMode.Enterprise)
        {
            ValidateEnterpriseSettings();
        }
    }

    public static ConfigurationManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ConfigurationManager();
                }
            }
            return _instance;
        }
    }

    public static ConfigurationManager GetInstance(ConfigurationMode mode)
    {
        lock (_lock)
        {
            _instance = new ConfigurationManager(mode);
            return _instance;
        }
    }

    #region Core Settings

    public bool VerboseLogging
    {
        get => GetValue("VerboseLogging", false);
        set => SetValue("VerboseLogging", value);
    }

    public bool AutomaticUpdates
    {
        get => GetValue("AutomaticUpdates", _mode == ConfigurationMode.Enterprise ? false : true);
        set => SetValue("AutomaticUpdates", value);
    }

    public bool CreateBackups
    {
        get => GetValue("CreateBackups", true);
        set => SetValue("CreateBackups", value);
    }

    public int MaxBackups
    {
        get => GetValue("MaxBackups", _mode == ConfigurationMode.Personal ? 10 : 5);
        set => SetValue("MaxBackups", Math.Clamp(value, 1, 20));
    }

    public int ScanIntervalSeconds
    {
        get => GetValue("ScanIntervalSeconds", 300);
        set => SetValue("ScanIntervalSeconds", Math.Max(60, value));
    }

    public bool EnableSecurityValidation
    {
        get => GetValue("EnableSecurityValidation", true);
        set => SetValue("EnableSecurityValidation", value);
    }

    public int DriverStaleThresholdDays
    {
        get => GetValue("DriverStaleThresholdDays", 180);
        set => SetValue("DriverStaleThresholdDays", Math.Clamp(value, 30, 3650));
    }

    #endregion

    #region Security Settings

    public bool MaximumSecurity
    {
        get => GetValue("MaximumSecurity", _mode == ConfigurationMode.Personal);
        set => SetValue("MaximumSecurity", value);
    }

    public bool VerifyAllSignatures
    {
        get => GetValue("VerifyAllSignatures", true);
        set => SetValue("VerifyAllSignatures", value);
    }

    public bool BlockUnsignedDrivers
    {
        get => GetValue("BlockUnsignedDrivers", _mode == ConfigurationMode.Personal);
        set => SetValue("BlockUnsignedDrivers", value);
    }

    public bool EnableRealTimeProtection
    {
        get => GetValue("EnableRealTimeProtection", _mode == ConfigurationMode.Personal);
        set => SetValue("EnableRealTimeProtection", value);
    }

    public bool AutoQuarantine
    {
        get => GetValue("AutoQuarantine", _mode == ConfigurationMode.Personal);
        set => SetValue("AutoQuarantine", value);
    }

    public bool RequireDriverSignatureValidation
    {
        get => GetValue("RequireDriverSignatureValidation", true);
        set => SetValue("RequireDriverSignatureValidation", value);
    }

    public bool EnableAuditLogging
    {
        get => GetValue("EnableAuditLogging", _mode == ConfigurationMode.Enterprise);
        set => SetValue("EnableAuditLogging", value);
    }

    public bool EnableEncryption
    {
        get => GetValue("EnableEncryption", true);
        set => SetValue("EnableEncryption", value);
    }

    #endregion

    #region Performance Settings

    public bool MaxPerformance
    {
        get => GetValue("MaxPerformance", _mode == ConfigurationMode.Personal);
        set => SetValue("MaxPerformance", value);
    }

    public bool EnableCaching
    {
        get => GetValue("EnableCaching", true);
        set => SetValue("EnableCaching", value);
    }

    public int CacheDurationMinutes
    {
        get => GetValue("CacheDurationMinutes", _mode == ConfigurationMode.Personal ? 10 : 5);
        set => SetValue("CacheDurationMinutes", Math.Clamp(value, 1, 60));
    }

    public bool ParallelScanning
    {
        get => GetValue("ParallelScanning", true);
        set => SetValue("ParallelScanning", value);
    }

    public int MaxParallelOperations
    {
        get => GetValue("MaxParallelOperations", _mode == ConfigurationMode.Personal ? 4 : 10);
        set => SetValue("MaxParallelOperations", Math.Clamp(value, 1, 100));
    }

    public int MaxConcurrentOperations
    {
        get => GetValue("MaxConcurrentOperations", 10);
        set => SetValue("MaxConcurrentOperations", Math.Clamp(value, 1, 100));
    }

    public int SessionTimeoutMinutes
    {
        get => GetValue("SessionTimeoutMinutes", 30);
        set => SetValue("SessionTimeoutMinutes", Math.Clamp(value, 5, 1440));
    }

    public int WmiConnectionPoolSize
    {
        get => GetValue("WmiConnectionPoolSize", 5);
        set => SetValue("WmiConnectionPoolSize", Math.Clamp(value, 1, 20));
    }

    public int OperationTimeoutSeconds
    {
        get => GetValue("OperationTimeoutSeconds", 30);
        set => SetValue("OperationTimeoutSeconds", Math.Clamp(value, 5, 300));
    }

    public bool EnablePerformanceMonitoring
    {
        get => GetValue("EnablePerformanceMonitoring", true);
        set => SetValue("EnablePerformanceMonitoring", value);
    }

    #endregion

    #region Backup Settings

    public bool AlwaysCreateBackup
    {
        get => GetValue("AlwaysCreateBackup", true);
        set => SetValue("AlwaysCreateBackup", value);
    }

    public bool CompressBackups
    {
        get => GetValue("CompressBackups", true);
        set => SetValue("CompressBackups", value);
    }

    public bool EncryptBackups
    {
        get => GetValue("EncryptBackups", _mode == ConfigurationMode.Personal);
        set => SetValue("EncryptBackups", value);
    }

    public int BackupRetentionDays
    {
        get => GetValue("BackupRetentionDays", _mode == ConfigurationMode.Personal ? 90 : 30);
        set => SetValue("BackupRetentionDays", Math.Clamp(value, 1, 365));
    }

    public bool CreateBackupsBeforeUpdate
    {
        get => GetValue("CreateBackupsBeforeUpdate", true);
        set => SetValue("CreateBackupsBeforeUpdate", value);
    }

    public int MaxBackupsPerDriver
    {
        get => GetValue("MaxBackupsPerDriver", 5);
        set => SetValue("MaxBackupsPerDriver", Math.Clamp(value, 1, 20));
    }

    #endregion

    #region Notification Settings

    public bool ShowDesktopNotifications
    {
        get => GetValue("ShowDesktopNotifications", _mode == ConfigurationMode.Personal);
        set => SetValue("ShowDesktopNotifications", value);
    }

    public bool NotifyOnSecurityIssues
    {
        get => GetValue("NotifyOnSecurityIssues", true);
        set => SetValue("NotifyOnSecurityIssues", value);
    }

    public bool NotifyOnDriverUpdates
    {
        get => GetValue("NotifyOnDriverUpdates", true);
        set => SetValue("NotifyOnDriverUpdates", value);
    }

    public bool NotifyOnOptimization
    {
        get => GetValue("NotifyOnOptimization", false);
        set => SetValue("NotifyOnOptimization", value);
    }

    #endregion

    #region Privacy Settings

    public bool MinimizeDataCollection
    {
        get => GetValue("MinimizeDataCollection", _mode == ConfigurationMode.Personal);
        set => SetValue("MinimizeDataCollection", value);
    }

    public bool EncryptLogs
    {
        get => GetValue("EncryptLogs", _mode == ConfigurationMode.Personal);
        set => SetValue("EncryptLogs", value);
    }

    public bool AutoDeleteOldLogs
    {
        get => GetValue("AutoDeleteOldLogs", true);
        set => SetValue("AutoDeleteOldLogs", value);
    }

    public int LogRetentionDays
    {
        get => GetValue("LogRetentionDays", 30);
        set => SetValue("LogRetentionDays", Math.Clamp(value, 1, 365));
    }

    #endregion

    #region Automation Settings

    public bool AutoScan
    {
        get => GetValue("AutoScan", _mode == ConfigurationMode.Personal);
        set => SetValue("AutoScan", value);
    }

    public int AutoScanIntervalHours
    {
        get => GetValue("AutoScanIntervalHours", 24);
        set => SetValue("AutoScanIntervalHours", Math.Clamp(value, 1, 168));
    }

    public bool AutoOptimize
    {
        get => GetValue("AutoOptimize", _mode == ConfigurationMode.Personal);
        set => SetValue("AutoOptimize", value);
    }

    public bool AutoCleanup
    {
        get => GetValue("AutoCleanup", _mode == ConfigurationMode.Personal);
        set => SetValue("AutoCleanup", value);
    }

    public int ScanIntervalMinutes
    {
        get => GetValue("ScanIntervalMinutes", 60);
        set => SetValue("ScanIntervalMinutes", Math.Clamp(value, 5, 1440));
    }

    #endregion

    #region Enterprise Settings

    public bool EnableComplianceReporting
    {
        get => GetValue("EnableComplianceReporting", _mode == ConfigurationMode.Enterprise);
        set => SetValue("EnableComplianceReporting", value);
    }

    public string? ActiveDirectoryDomain
    {
        get => GetValue<string?>("ActiveDirectoryDomain", null);
        set => SetValue("ActiveDirectoryDomain", value);
    }

    public string? ManagementServerUrl
    {
        get => GetValue<string?>("ManagementServerUrl", null);
        set => SetValue("ManagementServerUrl", value);
    }

    public string? SiemEndpoint
    {
        get => GetValue<string?>("SiemEndpoint", null);
        set => SetValue("SiemEndpoint", value);
    }

    public bool EnableCentralizedLogging
    {
        get => GetValue("EnableCentralizedLogging", false);
        set => SetValue("EnableCentralizedLogging", value);
    }

    public bool EnableMetricsExport
    {
        get => GetValue("EnableMetricsExport", false);
        set => SetValue("EnableMetricsExport", value);
    }

    #endregion

    #region Retry and Resilience Settings

    public int MaxRetryAttempts
    {
        get => GetValue("MaxRetryAttempts", 3);
        set => SetValue("MaxRetryAttempts", Math.Clamp(value, 0, 10));
    }

    public int RetryDelayMilliseconds
    {
        get => GetValue("RetryDelayMilliseconds", 1000);
        set => SetValue("RetryDelayMilliseconds", Math.Clamp(value, 100, 30000));
    }

    public int CircuitBreakerThreshold
    {
        get => GetValue("CircuitBreakerThreshold", 5);
        set => SetValue("CircuitBreakerThreshold", Math.Clamp(value, 3, 20));
    }

    public int CircuitBreakerTimeoutMinutes
    {
        get => GetValue("CircuitBreakerTimeoutMinutes", 1);
        set => SetValue("CircuitBreakerTimeoutMinutes", Math.Clamp(value, 1, 60));
    }

    #endregion

    #region Experimental Features

    public bool EnableExperimentalFeatures
    {
        get => GetValue("EnableExperimentalFeatures", false);
        set => SetValue("EnableExperimentalFeatures", value);
    }

    public bool EnableDebugMode
    {
        get => GetValue("EnableDebugMode", false);
        set => SetValue("EnableDebugMode", value);
    }

    public bool OneClickOptimization
    {
        get => GetValue("OneClickOptimization", _mode == ConfigurationMode.Personal);
        set => SetValue("OneClickOptimization", value);
    }

    public bool OneClickSecurity
    {
        get => GetValue("OneClickSecurity", _mode == ConfigurationMode.Personal);
        set => SetValue("OneClickSecurity", value);
    }

    public bool QuickScanEnabled
    {
        get => GetValue("QuickScanEnabled", true);
        set => SetValue("QuickScanEnabled", value);
    }

    #endregion

    #region Methods

    public void ApplySecurityPreset(SecurityLevel level)
    {
        switch (level)
        {
            case SecurityLevel.Maximum:
                MaximumSecurity = true;
                VerifyAllSignatures = true;
                BlockUnsignedDrivers = true;
                EnableRealTimeProtection = true;
                AutoQuarantine = true;
                AlwaysCreateBackup = true;
                EncryptBackups = true;
                EncryptLogs = true;
                break;

            case SecurityLevel.High:
                MaximumSecurity = true;
                VerifyAllSignatures = true;
                BlockUnsignedDrivers = false;
                EnableRealTimeProtection = true;
                AutoQuarantine = true;
                AlwaysCreateBackup = true;
                EncryptBackups = true;
                EncryptLogs = false;
                break;

            case SecurityLevel.Balanced:
                MaximumSecurity = false;
                VerifyAllSignatures = true;
                BlockUnsignedDrivers = false;
                EnableRealTimeProtection = true;
                AutoQuarantine = false;
                AlwaysCreateBackup = true;
                EncryptBackups = false;
                EncryptLogs = false;
                break;

            case SecurityLevel.Performance:
                MaximumSecurity = false;
                VerifyAllSignatures = false;
                BlockUnsignedDrivers = false;
                EnableRealTimeProtection = false;
                AutoQuarantine = false;
                AlwaysCreateBackup = false;
                EncryptBackups = false;
                EncryptLogs = false;
                break;
        }

        Save();
    }

    public void Reset()
    {
        lock (_lock)
        {
            _settings.Clear();
            Save();
        }
    }

    public void ExportToFile(string filePath)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(_settings, _jsonOptions);
            File.WriteAllText(filePath, json);
        }
    }

    public void ImportFromFile(string filePath)
    {
        lock (_lock)
        {
            var json = File.ReadAllText(filePath);
            var imported = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);

            if (imported != null)
            {
                _settings.Clear();
                foreach (var kvp in imported)
                {
                    _settings[kvp.Key] = kvp.Value;
                }

                if (_mode == ConfigurationMode.Enterprise)
                {
                    ValidateEnterpriseSettings();
                }

                Save();
            }
        }
    }

    public string GetSummary()
    {
        return $@"
AeroDriver Configuration Summary ({_mode} Mode)
================================================
Security Level: {(MaximumSecurity ? "Maximum" : "Standard")}
Automatic Backup: {(AlwaysCreateBackup ? "Enabled" : "Disabled")}
Real-time Protection: {(EnableRealTimeProtection ? "Enabled" : "Disabled")}
Auto Scan: {(AutoScan ? $"Enabled ({AutoScanIntervalHours}h interval)" : "Disabled")}
Auto Optimize: {(AutoOptimize ? "Enabled" : "Disabled")}
Max Backups: {MaxBackups}
Log Retention: {LogRetentionDays} days
Notifications: {(ShowDesktopNotifications ? "Enabled" : "Disabled")}
================================================
";
    }

    #endregion

    #region Private Methods

    private T GetValue<T>(string key, T defaultValue)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is JsonElement jsonElement)
                    {
                        return jsonElement.Deserialize<T>(_jsonOptions) ?? defaultValue;
                    }
                    if (value is T typedValue)
                    {
                        return typedValue;
                    }
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }
    }

    private void SetValue<T>(string key, T value)
    {
        lock (_lock)
        {
            _settings[key] = value!;
            Save();
        }
    }

    private Dictionary<string, object> LoadSettings()
    {
        try
        {
            if (File.Exists(_encryptedConfigPath) && _encryptionEnabled)
            {
                var encrypted = File.ReadAllBytes(_encryptedConfigPath);
                var decrypted = DecryptData(encrypted);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(decrypted, _jsonOptions)
                       ?? new Dictionary<string, object>();
            }
            else if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);

                if (_mode == ConfigurationMode.Enterprise && IsEncrypted(json))
                {
                    json = DecryptConfiguration(json);
                }

                return JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions)
                       ?? new Dictionary<string, object>();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to load configuration: {ex.Message}");
        }

        return new Dictionary<string, object>();
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, _jsonOptions);

            if (_mode == ConfigurationMode.Personal && (EncryptLogs || EncryptBackups))
            {
                var encrypted = EncryptData(json);
                File.WriteAllBytes(_encryptedConfigPath, encrypted);

                if (EnableDebugMode)
                {
                    File.WriteAllText(_configPath, json);
                }
            }
            else if (_mode == ConfigurationMode.Enterprise && _encryptionEnabled && EnableEncryption)
            {
                json = EncryptConfiguration(json);
                var tempPath = _configPath + ".tmp";
                var backupPath = _configPath + ".bak";

                File.WriteAllText(tempPath, json);

                if (File.Exists(_configPath))
                {
                    File.Copy(_configPath, backupPath, true);
                }

                File.Move(tempPath, _configPath, true);

                if (File.Exists(backupPath))
                {
                    try
                    {
                        File.Delete(backupPath);
                    }
                    catch
                    {
                        // Ignore backup cleanup errors
                    }
                }
            }
            else
            {
                File.WriteAllText(_configPath, json);
            }
        }
        catch (Exception ex)
        {
            throw new ConfigurationException($"Failed to save configuration: {ex.Message}", ex);
        }
    }

    private void ValidateEnterpriseSettings()
    {
        if (OperationTimeoutSeconds < 5)
        {
            throw new ConfigurationException("OperationTimeoutSeconds must be at least 5 seconds");
        }

        if (MaxConcurrentOperations < 1)
        {
            throw new ConfigurationException("MaxConcurrentOperations must be at least 1");
        }

        if (DriverStaleThresholdDays < 30)
        {
            throw new ConfigurationException("DriverStaleThresholdDays must be at least 30 days");
        }
    }

    private static bool ShouldEnableEncryption()
    {
        return !System.Diagnostics.Debugger.IsAttached;
    }

    private static bool IsEncrypted(string content)
    {
        return content.StartsWith("ENCRYPTED:");
    }

    private static string EncryptConfiguration(string plainText)
    {
        try
        {
            var entropy = GenerateEntropy();
            var data = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(data, entropy, DataProtectionScope.LocalMachine);
            var combined = new byte[entropy.Length + encrypted.Length];
            Buffer.BlockCopy(entropy, 0, combined, 0, entropy.Length);
            Buffer.BlockCopy(encrypted, 0, combined, entropy.Length, encrypted.Length);
            return "ENCRYPTED:" + Convert.ToBase64String(combined);
        }
        catch (Exception ex)
        {
            throw new ConfigurationException($"Failed to encrypt configuration: {ex.Message}", ex);
        }
    }

    private static string DecryptConfiguration(string encryptedText)
    {
        try
        {
            if (!encryptedText.StartsWith("ENCRYPTED:"))
            {
                return encryptedText;
            }

            var combined = Convert.FromBase64String(encryptedText.Substring(10));
            var entropy = new byte[32];
            var encrypted = new byte[combined.Length - 32];
            Buffer.BlockCopy(combined, 0, entropy, 0, 32);
            Buffer.BlockCopy(combined, 32, encrypted, 0, encrypted.Length);
            var decrypted = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            throw new ConfigurationException($"Failed to decrypt configuration: {ex.Message}", ex);
        }
    }

    private static byte[] GenerateEntropy()
    {
        var entropy = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(entropy);
        return entropy;
    }

    private static byte[] EncryptData(string data)
    {
        var dataBytes = Encoding.UTF8.GetBytes(data);
        return ProtectedData.Protect(dataBytes, null, DataProtectionScope.CurrentUser);
    }

    private static string DecryptData(byte[] encryptedData)
    {
        var decryptedBytes = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    #endregion
}

public enum ConfigurationMode
{
    Simplified,
    Personal,
    Enterprise
}

public enum SecurityLevel
{
    Maximum,
    High,
    Balanced,
    Performance
}

public class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message)
    {
    }

    public ConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
