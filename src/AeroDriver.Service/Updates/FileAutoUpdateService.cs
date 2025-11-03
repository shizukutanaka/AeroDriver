using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Service;

public sealed class FileAutoUpdateService : IAutoUpdateService, IDisposable
{
    private readonly ILogger<FileAutoUpdateService> _logger;
    private readonly ITelemetryService _telemetryService;
    private readonly SemaphoreSlim _checkSemaphore = new(1, 1);
    private readonly string _updateDirectory;
    private readonly string _manifestPath;
    private readonly string _historyPath;
    private Timer? _timer;
    private int _intervalHours;
    private bool _disposed;
    private const string DefaultManifestFileName = "update-manifest.json";

    public FileAutoUpdateService(ILogger<FileAutoUpdateService> logger, ITelemetryService telemetryService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));

        var baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeroDriver", "updates");
        Directory.CreateDirectory(baseDirectory);

        _updateDirectory = baseDirectory;
        _manifestPath = Path.Combine(_updateDirectory, DefaultManifestFileName);
        _historyPath = Path.Combine(_updateDirectory, "update-history.jsonl");
    }

    public void StartAutoUpdateCheck(int intervalHours)
    {
        ThrowIfDisposed();

        _intervalHours = Math.Max(1, intervalHours);
        _timer?.Dispose();
        _timer = new Timer(async _ => await ExecuteScheduledCheckAsync(), null, TimeSpan.Zero, TimeSpan.FromHours(_intervalHours));
        _logger.LogInformation("File auto-update check started (interval: {Interval} hours)", _intervalHours);
    }

    public async Task<AutoUpdateResult> CheckForUpdatesAsync()
    {
        ThrowIfDisposed();

        if (!await _checkSemaphore.WaitAsync(TimeSpan.FromSeconds(1)))
        {
            return new AutoUpdateResult(false, "Update check already in progress");
        }

        try
        {
            var manifest = await LoadManifestAsync();
            if (manifest == null)
            {
                return await FailAsync("Update manifest not found or invalid");
            }

            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
            if (!Version.TryParse(manifest.Version, out var manifestVersion))
            {
                return await FailAsync($"Invalid version format in manifest: {manifest.Version}");
            }

            if (manifestVersion <= currentVersion)
            {
                _logger.LogInformation("No updates available. Current version {CurrentVersion}, manifest version {ManifestVersion}", currentVersion, manifestVersion);
                await RecordTelemetryAsync("AutoUpdateCheck", new Dictionary<string, object>
                {
                    ["CurrentVersion"] = currentVersion.ToString(),
                    ["ManifestVersion"] = manifestVersion.ToString(),
                    ["Status"] = "NoUpdates"
                });

                return new AutoUpdateResult(false, "No newer version available");
            }

            if (!string.IsNullOrWhiteSpace(manifest.PackagePath))
            {
                var packageFullPath = Path.IsPathRooted(manifest.PackagePath)
                    ? manifest.PackagePath
                    : Path.Combine(_updateDirectory, manifest.PackagePath);

                if (!File.Exists(packageFullPath))
                {
                    return await FailAsync($"Update package not found: {packageFullPath}");
                }

                if (!string.IsNullOrWhiteSpace(manifest.Sha256))
                {
                    var hash = await ComputeSha256Async(packageFullPath);
                    if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        return await FailAsync("Update package hash mismatch");
                    }
                }
            }

            await RecordUpdateHistoryAsync(manifest);
            await RecordTelemetryAsync("AutoUpdateAvailable", new Dictionary<string, object>
            {
                ["CurrentVersion"] = currentVersion.ToString(),
                ["ManifestVersion"] = manifestVersion.ToString(),
                ["ReleaseNotes"] = manifest.ReleaseNotes ?? string.Empty
            });

            _logger.LogInformation("Update available. Current version {CurrentVersion} -> {ManifestVersion}", currentVersion, manifestVersion);
            return new AutoUpdateResult(true, $"Update available: {manifestVersion}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-update check failed");
            return await FailAsync("Auto-update check failed: " + ex.Message);
        }
        finally
        {
            _checkSemaphore.Release();
        }
    }

    public void StopAutoUpdateCheck()
    {
        if (_disposed)
        {
            return;
        }

        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
        _logger.LogInformation("File auto-update check stopped");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAutoUpdateCheck();
        _checkSemaphore.Dispose();
        _disposed = true;
    }

    private async Task ExecuteScheduledCheckAsync()
    {
        var result = await CheckForUpdatesAsync();
        if (!result.Success)
        {
            _logger.LogDebug("Scheduled auto-update check result: {Message}", result.Message);
        }
    }

    private async Task<AutoUpdateManifest?> LoadManifestAsync()
    {
        try
        {
            var manifestPath = ResolveManifestPath();
            if (!File.Exists(manifestPath))
            {
                _logger.LogWarning("Update manifest not found at {Path}", manifestPath);
                return null;
            }

            await using var stream = File.OpenRead(manifestPath);
            return await JsonSerializer.DeserializeAsync<AutoUpdateManifest>(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read update manifest");
            return null;
        }
    }

    private string ResolveManifestPath()
    {
        var customPath = Environment.GetEnvironmentVariable("AERODRIVER_UPDATE_MANIFEST");
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            return customPath;
        }

        return _manifestPath;
    }

    private async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    private async Task RecordUpdateHistoryAsync(AutoUpdateManifest manifest)
    {
        try
        {
            var historyEntry = new Dictionary<string, object>
            {
                ["Timestamp"] = DateTime.UtcNow,
                ["Version"] = manifest.Version,
                ["ReleaseNotes"] = manifest.ReleaseNotes ?? string.Empty,
                ["PackagePath"] = manifest.PackagePath ?? string.Empty
            };

            var line = JsonSerializer.Serialize(historyEntry, _jsonOptions);
            await File.AppendAllTextAsync(_historyPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record update history");
        }
    }

    private async Task RecordTelemetryAsync(string eventName, Dictionary<string, object> properties)
    {
        try
        {
            await _telemetryService.RecordEventAsync(eventName, properties);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to record auto-update telemetry {Event}", eventName);
        }
    }

    private async Task<AutoUpdateResult> FailAsync(string message)
    {
        await RecordTelemetryAsync("AutoUpdateFailure", new Dictionary<string, object>
        {
            ["Message"] = message,
            ["Timestamp"] = DateTime.UtcNow
        });

        return new AutoUpdateResult(false, message);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileAutoUpdateService));
        }
    }

    private sealed record AutoUpdateManifest
    {
        public string Version { get; init; } = "0.0.0";
        public string? PackagePath { get; init; }
        public string? Sha256 { get; init; }
        public string? ReleaseNotes { get; init; }
    }
}
