using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// 設定ファイルの管理ヘルパー
    /// </summary>
    public static class ConfigurationHelper
    {
        private const string DefaultConfigFileName = "aerodriver.config.json";
        
        /// <summary>
        /// デフォルト設定を作成
        /// </summary>
        public static Dictionary<string, object> CreateDefaultConfiguration()
        {
            return new Dictionary<string, object>
            {
                ["general"] = new Dictionary<string, object>
                {
                    ["auto_backup"] = true,
                    ["cache_duration_minutes"] = 30,
                    ["max_concurrent_downloads"] = 3,
                    ["whql_only"] = true,
                    ["language"] = "auto"
                },
                ["cleanup"] = new Dictionary<string, object>
                {
                    ["auto_cleanup_days"] = 30,
                    ["max_backup_count"] = 5,
                    ["clean_temp_files"] = true
                },
                ["logging"] = new Dictionary<string, object>
                {
                    ["level"] = "Information",
                    ["max_log_files"] = 10,
                    ["max_log_size_mb"] = 50
                },
                ["performance"] = new Dictionary<string, object>
                {
                    ["enable_caching"] = true,
                    ["cache_max_items"] = 1000,
                    ["scan_timeout_seconds"] = 300
                }
            };
        }
        
        /// <summary>
        /// 設定ファイルを読み込み
        /// </summary>
        public static async Task<Dictionary<string, object>> LoadConfigurationAsync(string? configPath = null, ILogger? logger = null)
        {
            var filePath = configPath ?? GetDefaultConfigPath();
            
            try
            {
                if (!File.Exists(filePath))
                {
                    logger?.LogInformation("Configuration file not found, creating default: {FilePath}", filePath);
                    var defaultConfig = CreateDefaultConfiguration();
                    await SaveConfigurationAsync(defaultConfig, filePath, logger);
                    return defaultConfig;
                }
                
                var jsonContent = await File.ReadAllTextAsync(filePath);
                var config = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent) ?? new Dictionary<string, object>();
                
                logger?.LogDebug("Configuration loaded from: {FilePath}", filePath);
                return config;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to load configuration from: {FilePath}, using defaults", filePath);
                return CreateDefaultConfiguration();
            }
        }
        
        /// <summary>
        /// 設定ファイルを保存
        /// </summary>
        public static async Task SaveConfigurationAsync(Dictionary<string, object> config, string? configPath = null, ILogger? logger = null)
        {
            var filePath = configPath ?? GetDefaultConfigPath();
            
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var jsonContent = JsonSerializer.Serialize(config, options);
                await File.WriteAllTextAsync(filePath, jsonContent);
                
                logger?.LogDebug("Configuration saved to: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to save configuration to: {FilePath}", filePath);
                throw;
            }
        }
        
        /// <summary>
        /// 設定値を取得（型安全）
        /// </summary>
        public static T GetValue<T>(Dictionary<string, object> config, string section, string key, T defaultValue)
        {
            try
            {
                if (config.TryGetValue(section, out var sectionObj) && 
                    sectionObj is JsonElement sectionElement &&
                    sectionElement.TryGetProperty(key, out var valueElement))
                {
                    return JsonSerializer.Deserialize<T>(valueElement.GetRawText()) ?? defaultValue;
                }
                
                // Fallback: direct dictionary access for in-memory config
                if (config.TryGetValue(section, out var sectionDict) && 
                    sectionDict is Dictionary<string, object> dict &&
                    dict.TryGetValue(key, out var value))
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
            }
            catch
            {
                // Ignore conversion errors and return default
            }
            
            return defaultValue;
        }
        
        /// <summary>
        /// デフォルト設定ファイルパスを取得
        /// </summary>
        private static string GetDefaultConfigPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, "AeroDriver", DefaultConfigFileName);
        }
    }
}