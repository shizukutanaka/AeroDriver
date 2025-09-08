using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Services
{
    public class SettingsService : ISettingsService
    {
        private const string SettingsFileName = "settings.json";
        private readonly string _settingsPath;
        private DateTime _lastModified;
        
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public bool AutoUpdateEnabled { get; set; } = false;
        public bool IncludeBetaDrivers { get; set; } = false;
        public bool BackupEnabled { get; set; } = true;
        public int MaxBackupGenerations { get; set; } = 3;

        // New enhanced settings
        public bool VerboseLogging { get; set; } = false;
        public int CacheExpirationMinutes { get; set; } = 10;
        public bool AutoCleanupEnabled { get; set; } = true;
        public int HealthCheckIntervalHours { get; set; } = 24;
        public string PreferredLanguage { get; set; } = "auto";

        public SettingsService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "AeroDriver");
            
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            _settingsPath = Path.Combine(appFolder, SettingsFileName);
            LoadSettings();
        }

        public void Save()
        {
            try
            {
                var settingsData = CreateSettingsObject();
                var json = JsonSerializer.Serialize(settingsData, SerializerOptions);
                File.WriteAllText(_settingsPath, json);
                _lastModified = DateTime.UtcNow;
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"Cannot save settings - insufficient permissions: {ex.Message}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new InvalidOperationException($"Settings directory not found: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save settings: {ex.Message}", ex);
            }
        }
        
        private SettingsData CreateSettingsObject()
        {
            return new SettingsData
            {
                AutoUpdateEnabled = AutoUpdateEnabled,
                IncludeBetaDrivers = IncludeBetaDrivers,
                BackupEnabled = BackupEnabled,
                MaxBackupGenerations = MaxBackupGenerations,
                VerboseLogging = VerboseLogging,
                CacheExpirationMinutes = CacheExpirationMinutes,
                AutoCleanupEnabled = AutoCleanupEnabled,
                HealthCheckIntervalHours = HealthCheckIntervalHours,
                PreferredLanguage = PreferredLanguage,
                LastModified = DateTime.UtcNow
            };
        }

        public void ResetToDefaults()
        {
            AutoUpdateEnabled = false;
            IncludeBetaDrivers = false;
            BackupEnabled = true;
            MaxBackupGenerations = 3;
            VerboseLogging = false;
            CacheExpirationMinutes = 10;
            AutoCleanupEnabled = true;
            HealthCheckIntervalHours = 24;
            PreferredLanguage = "auto";
            Save();
        }

        public bool HasChanged()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return false;

                var fileModified = File.GetLastWriteTimeUtc(_settingsPath);
                return fileModified > _lastModified;
            }
            catch
            {
                return false;
            }
        }

        public void Reload()
        {
            if (HasChanged())
            {
                LoadSettings();
            }
        }

        public Dictionary<string, object> GetAllSettings()
        {
            return new Dictionary<string, object>
            {
                [nameof(AutoUpdateEnabled)] = AutoUpdateEnabled,
                [nameof(IncludeBetaDrivers)] = IncludeBetaDrivers,
                [nameof(BackupEnabled)] = BackupEnabled,
                [nameof(MaxBackupGenerations)] = MaxBackupGenerations,
                [nameof(VerboseLogging)] = VerboseLogging,
                [nameof(CacheExpirationMinutes)] = CacheExpirationMinutes,
                [nameof(AutoCleanupEnabled)] = AutoCleanupEnabled,
                [nameof(HealthCheckIntervalHours)] = HealthCheckIntervalHours,
                [nameof(PreferredLanguage)] = PreferredLanguage
            };
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    Save(); // Create default settings file
                    return;
                }

                var json = File.ReadAllText(_settingsPath);
                var settingsData = JsonSerializer.Deserialize<SettingsData>(json, SerializerOptions);
                
                if (settingsData != null)
                {
                    ApplySettingsData(settingsData);
                }
                else
                {
                    ResetToDefaults();
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Settings file is corrupted: {ex.Message}", ex);
            }
            catch (FileNotFoundException)
            {
                Save(); // Create default settings file
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"Cannot read settings - insufficient permissions: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load settings: {ex.Message}", ex);
            }
        }
        
        private void ApplySettingsData(SettingsData data)
        {
            AutoUpdateEnabled = data.AutoUpdateEnabled;
            IncludeBetaDrivers = data.IncludeBetaDrivers;
            BackupEnabled = data.BackupEnabled;
            MaxBackupGenerations = Math.Max(1, data.MaxBackupGenerations); // Validate minimum
            VerboseLogging = data.VerboseLogging;
            CacheExpirationMinutes = Math.Max(1, data.CacheExpirationMinutes); // Validate minimum
            AutoCleanupEnabled = data.AutoCleanupEnabled;
            HealthCheckIntervalHours = Math.Max(1, data.HealthCheckIntervalHours); // Validate minimum
            PreferredLanguage = string.IsNullOrWhiteSpace(data.PreferredLanguage) ? "auto" : data.PreferredLanguage;
            _lastModified = data.LastModified;
        }
        
        private class SettingsData
        {
            public bool AutoUpdateEnabled { get; set; }
            public bool IncludeBetaDrivers { get; set; }
            public bool BackupEnabled { get; set; } = true;
            public int MaxBackupGenerations { get; set; } = 3;
            public bool VerboseLogging { get; set; }
            public int CacheExpirationMinutes { get; set; } = 10;
            public bool AutoCleanupEnabled { get; set; } = true;
            public int HealthCheckIntervalHours { get; set; } = 24;
            public string PreferredLanguage { get; set; } = "auto";
            public DateTime LastModified { get; set; }
        }
    }
}