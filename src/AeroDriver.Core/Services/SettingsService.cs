using System;
using System.IO;
using System.Text.Json;
using AeroDriver.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// アプリケーション設定を JSON ファイルで永続化します。
    /// %LOCALAPPDATA%\AeroDriver\settings.json に保存。
    /// </summary>
    public sealed class SettingsService : ISettingsService
    {
        private readonly ILogger<SettingsService> _logger;
        private readonly string _settingsPath;
        private SettingsData _data;

        public SettingsService(ILogger<SettingsService> logger)
            : this(logger, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver", "settings.json"))
        { }

        // テスト用: パスを外から注入できる
        internal SettingsService(ILogger<SettingsService> logger, string settingsPath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsPath = settingsPath;
            _data = Load();
        }

        public bool AutoUpdateEnabled
        {
            get => _data.AutoUpdateEnabled;
            set => _data = _data with { AutoUpdateEnabled = value };
        }

        public bool IncludeBetaDrivers
        {
            get => _data.IncludeBetaDrivers;
            set => _data = _data with { IncludeBetaDrivers = value };
        }

        public bool BackupEnabled
        {
            get => _data.BackupEnabled;
            set => _data = _data with { BackupEnabled = value };
        }

        public int MaxBackupGenerations
        {
            get => _data.MaxBackupGenerations;
            set => _data = _data with { MaxBackupGenerations = Math.Max(1, value) };
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                _logger.LogInformation("設定を保存しました: {Path}", _settingsPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "設定の保存に失敗しました: {Path}", _settingsPath);
            }
        }

        public void ResetToDefaults()
        {
            _data = SettingsData.Default;
            _logger.LogInformation("設定をデフォルトにリセットしました");
        }

        private SettingsData Load()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return SettingsData.Default;

                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<SettingsData>(json) ?? SettingsData.Default;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "設定の読み込みに失敗しました。デフォルト値を使用します");
                return SettingsData.Default;
            }
        }

        // record でイミュータブルな設定データを表現（with 式で更新）
        private sealed record SettingsData(
            bool AutoUpdateEnabled,
            bool IncludeBetaDrivers,
            bool BackupEnabled,
            int MaxBackupGenerations)
        {
            public static readonly SettingsData Default = new(
                AutoUpdateEnabled: true,
                IncludeBetaDrivers: false,
                BackupEnabled: true,
                MaxBackupGenerations: 3);
        }
    }
}
