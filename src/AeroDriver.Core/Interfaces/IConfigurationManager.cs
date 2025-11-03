using System.Text.Json;
using System.Threading.Tasks;

namespace AeroDriver.Core;

/// <summary>
/// Interface for configuration management
/// </summary>
public interface IConfigurationManager
{
    T GetValue<T>(string key, T defaultValue = default!);
    Task SetValueAsync<T>(string key, T value);
    Task ExportConfigurationAsync(string filePath);
    Task ImportConfigurationAsync(string filePath);
    Task<Dictionary<string, string>> ValidateConfigurationAsync();
    Task ResetToDefaultsAsync();
}
