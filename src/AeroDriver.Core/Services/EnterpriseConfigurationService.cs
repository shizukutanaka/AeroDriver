using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AeroDriver.Core.Models;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Helpers;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// Enterprise-grade configuration management service with validation and security
    /// </summary>
    public class EnterpriseConfigurationService : IDisposable
    {
        private readonly ILogger<EnterpriseConfigurationService>? _logger;
        private readonly string _configurationPath;
        private readonly string _backupPath;
        private EnterpriseConfiguration _configuration;
        private readonly FileSystemWatcher? _fileWatcher;
        private readonly object _configLock = new();
        
        public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
        
        public EnterpriseConfiguration Configuration 
        { 
            get 
            { 
                lock (_configLock)
                {
                    return _configuration;
                }
            } 
        }

        public EnterpriseConfigurationService(ILogger<EnterpriseConfigurationService>? logger = null, 
            string? customConfigPath = null)
        {
            _logger = logger;
            
            var configDir = customConfigPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AeroDriver", "Enterprise");
                
            Directory.CreateDirectory(configDir);
            
            _configurationPath = Path.Combine(configDir, "enterprise-config.json");
            _backupPath = Path.Combine(configDir, "Backups");
            Directory.CreateDirectory(_backupPath);
            
            _configuration = LoadConfiguration();
            
            try
            {
                // Watch for external configuration changes
                _fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(_configurationPath)!)
                {
                    Filter = Path.GetFileName(_configurationPath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _fileWatcher.Changed += OnConfigurationFileChanged;
                _fileWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not set up configuration file watcher");
            }
        }

        public EnterpriseConfiguration LoadConfiguration()
        {
            lock (_configLock)
            {
                try
                {
                    if (!File.Exists(_configurationPath))
                    {
                        _logger?.LogInformation("Configuration file not found, creating default configuration");
                        var defaultConfig = CreateDefaultConfiguration();
                        SaveConfigurationInternal(defaultConfig);
                        return defaultConfig;
                    }

                    var json = File.ReadAllText(_configurationPath);
                    var config = JsonSerializer.Deserialize<EnterpriseConfiguration>(json, GetJsonOptions());
                    
                    if (config == null)
                    {
                        _logger?.LogWarning("Configuration deserialization returned null, using default");
                        return CreateDefaultConfiguration();
                    }

                    ValidateConfiguration(config);
                    _logger?.LogInformation("Configuration loaded successfully from {Path}", _configurationPath);
                    return config;
                }
                catch (JsonException ex)
                {
                    _logger?.LogError(ex, "Invalid JSON in configuration file, using default configuration");
                    return CreateDefaultConfiguration();
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger?.LogError(ex, "Access denied reading configuration file");
                    throw new InvalidOperationException("Cannot access configuration file - insufficient permissions", ex);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error loading configuration, using default");
                    return CreateDefaultConfiguration();
                }
            }
        }

        public void SaveConfiguration(EnterpriseConfiguration? configuration = null)
        {
            lock (_configLock)
            {
                var configToSave = configuration ?? _configuration;
                ValidateConfiguration(configToSave);
                
                try
                {
                    CreateBackup();
                    SaveConfigurationInternal(configToSave);
                    _configuration = configToSave;
                    
                    _logger?.LogInformation("Configuration saved successfully to {Path}", _configurationPath);
                    OnConfigurationChanged(new ConfigurationChangedEventArgs("Configuration saved manually"));
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error saving configuration");
                    throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
                }
            }
        }

        public void UpdateSetting<T>(string section, string property, T value, string? reason = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(section);
            ArgumentException.ThrowIfNullOrEmpty(property);
            
            lock (_configLock)
            {
                var oldValue = GetSettingValue(section, property);
                SetSettingValue(section, property, value);
                
                _configuration.Metadata.LastModifiedAt = DateTime.UtcNow;
                _configuration.Metadata.LastModifiedBy = Environment.UserName;
                
                _configuration.Metadata.ChangeHistory.Add(new ConfigurationChange
                {
                    Timestamp = DateTime.UtcNow,
                    User = Environment.UserName,
                    Property = $"{section}.{property}",
                    OldValue = oldValue?.ToString(),
                    NewValue = value?.ToString(),
                    Reason = reason ?? "Manual update"
                });
                
                SaveConfiguration();
                _logger?.LogInformation("Configuration setting updated: {Section}.{Property} = {Value}", 
                    section, property, value);
            }
        }

        public T GetSetting<T>(string section, string property, T defaultValue)
        {
            ArgumentException.ThrowIfNullOrEmpty(section);
            ArgumentException.ThrowIfNullOrEmpty(property);
            
            lock (_configLock)
            {
                try
                {
                    var value = GetSettingValue(section, property);
                    if (value is T typedValue)
                        return typedValue;
                    
                    // Try to convert
                    if (value != null && typeof(T) != typeof(object))
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    
                    return defaultValue;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error getting configuration setting {Section}.{Property}, using default", 
                        section, property);
                    return defaultValue;
                }
            }
        }

        public void ResetToDefaults(string? reason = null)
        {
            lock (_configLock)
            {
                _logger?.LogWarning("Resetting configuration to defaults. Reason: {Reason}", reason ?? "Manual reset");
                CreateBackup();
                
                var defaultConfig = CreateDefaultConfiguration();
                defaultConfig.Metadata.ChangeHistory.Add(new ConfigurationChange
                {
                    Timestamp = DateTime.UtcNow,
                    User = Environment.UserName,
                    Property = "ALL",
                    OldValue = "Custom configuration",
                    NewValue = "Default configuration",
                    Reason = reason ?? "Reset to defaults"
                });
                
                SaveConfigurationInternal(defaultConfig);
                _configuration = defaultConfig;
                
                OnConfigurationChanged(new ConfigurationChangedEventArgs("Configuration reset to defaults"));
            }
        }

        public ValidationResult ValidateConfiguration(EnterpriseConfiguration? config = null)
        {
            var configToValidate = config ?? _configuration;
            var result = new ValidationResult();
            
            // Security validation
            ValidateSecuritySettings(configToValidate.Security, result);
            
            // Operational validation
            ValidateOperationalSettings(configToValidate.Operations, result);
            
            // Audit validation
            ValidateAuditSettings(configToValidate.Audit, result);
            
            // Monitoring validation
            ValidateMonitoringSettings(configToValidate.Monitoring, result);
            
            // Backup validation
            ValidateBackupSettings(configToValidate.Backup, result);
            
            configToValidate.Metadata.ValidationErrors.Clear();
            configToValidate.Metadata.ValidationErrors.AddRange(result.Errors);
            
            if (!result.IsValid)
            {
                _logger?.LogWarning("Configuration validation failed with {ErrorCount} errors", result.Errors.Count);
            }
            
            return result;
        }

        public string ExportConfiguration(bool includeMetadata = false, bool includeSecrets = false)
        {
            lock (_configLock)
            {
                var exportConfig = new EnterpriseConfiguration
                {
                    Security = _configuration.Security,
                    Operations = _configuration.Operations,
                    Audit = _configuration.Audit,
                    Monitoring = _configuration.Monitoring,
                    Backup = _configuration.Backup,
                    Localization = _configuration.Localization,
                    Metadata = includeMetadata ? _configuration.Metadata : new ConfigurationMetadata()
                };
                
                if (!includeSecrets)
                {
                    // Remove sensitive information
                    exportConfig.Audit.AuditLogPath = null;
                    exportConfig.Backup.CustomBackupPath = null;
                }
                
                return JsonSerializer.Serialize(exportConfig, GetJsonOptions());
            }
        }

        public void ImportConfiguration(string json, bool validateOnly = false)
        {
            ArgumentException.ThrowIfNullOrEmpty(json);
            
            var importedConfig = JsonSerializer.Deserialize<EnterpriseConfiguration>(json, GetJsonOptions());
            if (importedConfig == null)
                throw new InvalidOperationException("Failed to deserialize configuration");
            
            var validation = ValidateConfiguration(importedConfig);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException($"Configuration validation failed: {string.Join(", ", validation.Errors)}");
            }
            
            if (!validateOnly)
            {
                lock (_configLock)
                {
                    CreateBackup();
                    importedConfig.Metadata.LastModifiedAt = DateTime.UtcNow;
                    importedConfig.Metadata.LastModifiedBy = Environment.UserName;
                    
                    SaveConfigurationInternal(importedConfig);
                    _configuration = importedConfig;
                    
                    _logger?.LogInformation("Configuration imported successfully");
                    OnConfigurationChanged(new ConfigurationChangedEventArgs("Configuration imported"));
                }
            }
        }

        private EnterpriseConfiguration CreateDefaultConfiguration()
        {
            return new EnterpriseConfiguration
            {
                Metadata = new ConfigurationMetadata
                {
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow,
                    LastModifiedBy = Environment.UserName,
                    Source = "default"
                }
            };
        }

        private void SaveConfigurationInternal(EnterpriseConfiguration configuration)
        {
            var json = JsonSerializer.Serialize(configuration, GetJsonOptions());
            File.WriteAllText(_configurationPath, json);
            SetFilePermissions(_configurationPath);
        }

        private void SetFilePermissions(string filePath)
        {
            try
            {
                // Set appropriate file permissions for enterprise security
                var fileInfo = new FileInfo(filePath);
                fileInfo.Attributes |= FileAttributes.ReadOnly;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not set file permissions for {FilePath}", filePath);
            }
        }

        private void CreateBackup()
        {
            if (!File.Exists(_configurationPath)) return;
            
            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var backupFile = Path.Combine(_backupPath, $"config-backup-{timestamp}.json");
                File.Copy(_configurationPath, backupFile);
                
                // Clean up old backups (keep last 10)
                var backupFiles = Directory.GetFiles(_backupPath, "config-backup-*.json")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Skip(10);
                    
                foreach (var file in backupFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Could not delete old backup file {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not create configuration backup");
            }
        }

        private JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
        }

        private object? GetSettingValue(string section, string property)
        {
            var sectionObject = section.ToLowerInvariant() switch
            {
                "security" => _configuration.Security,
                "operations" => _configuration.Operations,
                "audit" => _configuration.Audit,
                "monitoring" => _configuration.Monitoring,
                "backup" => _configuration.Backup,
                "localization" => _configuration.Localization,
                "metadata" => _configuration.Metadata,
                _ => null
            };

            if (sectionObject == null) return null;
            
            var propertyInfo = sectionObject.GetType().GetProperty(property);
            return propertyInfo?.GetValue(sectionObject);
        }

        private void SetSettingValue(string section, string property, object? value)
        {
            var sectionObject = section.ToLowerInvariant() switch
            {
                "security" => _configuration.Security,
                "operations" => _configuration.Operations,
                "audit" => _configuration.Audit,
                "monitoring" => _configuration.Monitoring,
                "backup" => _configuration.Backup,
                "localization" => _configuration.Localization,
                "metadata" => _configuration.Metadata,
                _ => null
            };

            if (sectionObject == null) return;
            
            var propertyInfo = sectionObject.GetType().GetProperty(property);
            if (propertyInfo != null && propertyInfo.CanWrite)
            {
                propertyInfo.SetValue(sectionObject, value);
            }
        }

        private void ValidateSecuritySettings(SecuritySettings security, ValidationResult result)
        {
            if (security.MaxDriverFileSize < 1024 * 1024) // 1MB minimum
                result.AddError("MaxDriverFileSize must be at least 1MB");
                
            if (security.SecurityOperationTimeout < 5000) // 5 seconds minimum
                result.AddError("SecurityOperationTimeout must be at least 5 seconds");
                
            if (security.AllowedDriverExtensions == null || security.AllowedDriverExtensions.Length == 0)
                result.AddError("AllowedDriverExtensions cannot be empty");
        }

        private void ValidateOperationalSettings(OperationalSettings operations, ValidationResult result)
        {
            if (operations.MaxConcurrentOperations < 1)
                result.AddError("MaxConcurrentOperations must be at least 1");
                
            if (operations.DefaultOperationTimeout < 10000) // 10 seconds minimum
                result.AddError("DefaultOperationTimeout must be at least 10 seconds");
                
            if (operations.MaxRetryAttempts < 0)
                result.AddError("MaxRetryAttempts cannot be negative");
        }

        private void ValidateAuditSettings(AuditSettings audit, ValidationResult result)
        {
            if (audit.MaxAuditLogSize < 1024 * 1024) // 1MB minimum
                result.AddError("MaxAuditLogSize must be at least 1MB");
                
            if (audit.AuditLogRetentionCount < 1)
                result.AddError("AuditLogRetentionCount must be at least 1");
        }

        private void ValidateMonitoringSettings(MonitoringSettings monitoring, ValidationResult result)
        {
            if (monitoring.MetricCollectionInterval < 10)
                result.AddError("MetricCollectionInterval must be at least 10 seconds");
                
            if (monitoring.HealthCheckInterval < 1)
                result.AddError("HealthCheckInterval must be at least 1 minute");
        }

        private void ValidateBackupSettings(BackupSettings backup, ValidationResult result)
        {
            if (backup.MaxBackupGenerations < 1)
                result.AddError("MaxBackupGenerations must be at least 1");
                
            if (backup.BackupRetentionDays < 0)
                result.AddError("BackupRetentionDays cannot be negative");
        }

        private void OnConfigurationFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Debounce file change events
                System.Threading.Thread.Sleep(100);
                
                _logger?.LogInformation("Configuration file changed externally, reloading");
                var newConfig = LoadConfiguration();
                
                lock (_configLock)
                {
                    _configuration = newConfig;
                }
                
                OnConfigurationChanged(new ConfigurationChangedEventArgs("Configuration file changed externally"));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reloading configuration after file change");
            }
        }

        private void OnConfigurationChanged(ConfigurationChangedEventArgs e)
        {
            ConfigurationChanged?.Invoke(this, e);
        }

        public void Dispose()
        {
            _fileWatcher?.Dispose();
        }
    }

    public class ConfigurationChangedEventArgs : EventArgs
    {
        public string Reason { get; }
        public DateTime Timestamp { get; } = DateTime.UtcNow;

        public ConfigurationChangedEventArgs(string reason)
        {
            Reason = reason;
        }
    }

    public class ValidationResult
    {
        public List<string> Errors { get; } = new();
        public bool IsValid => Errors.Count == 0;

        public void AddError(string error)
        {
            Errors.Add(error);
        }
    }
}