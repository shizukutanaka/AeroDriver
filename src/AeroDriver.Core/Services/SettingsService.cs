using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        // ISettingsService は AddSingleton 登録（アプリ全体で共有）のため、
        // _data の読み取り-更新-書き込みを複数スレッドから同時に行っても
        // 片方の変更が失われないよう lock で排他する
        private readonly object _lock = new();
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
            get { lock (_lock) return _data.AutoUpdateEnabled; }
            set
            {
                lock (_lock) _data = _data with { AutoUpdateEnabled = value };
                // 設定変更を都度永続化する（呼び出し側が明示的に Save() を呼ばなくても
                // プロセス終了時に変更が失われないようにする）
                Save();
            }
        }

        public bool IncludeBetaDrivers
        {
            get { lock (_lock) return _data.IncludeBetaDrivers; }
            set
            {
                lock (_lock) _data = _data with { IncludeBetaDrivers = value };
                Save();
            }
        }

        public bool BackupEnabled
        {
            get { lock (_lock) return _data.BackupEnabled; }
            set
            {
                lock (_lock) _data = _data with { BackupEnabled = value };
                Save();
            }
        }

        public int MaxBackupGenerations
        {
            get { lock (_lock) return _data.MaxBackupGenerations; }
            set
            {
                lock (_lock) _data = _data with { MaxBackupGenerations = Math.Max(1, value) };
                Save();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                SettingsData snapshot;
                lock (_lock) snapshot = _data;
                // Source Generation: リフレクション不要 → AOT互換・起動時間短縮
                var json = JsonSerializer.Serialize(snapshot, SettingsJsonContext.Default.SettingsData);
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
            lock (_lock) _data = SettingsData.Default;
            _logger.LogInformation("設定をデフォルトにリセットしました");
            Save();
        }

        private SettingsData Load()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return SettingsData.Default;

                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsData)
                       ?? SettingsData.Default;
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

        // JsonSerializerContext: Source Generation でリフレクション不要なシリアライザーを生成
        // WriteIndented = true でファイルを人間が読める形式に保つ
        [JsonSourceGenerationOptions(WriteIndented = true)]
        [JsonSerializable(typeof(SettingsData))]
        private sealed partial class SettingsJsonContext : JsonSerializerContext { }
    }
}
