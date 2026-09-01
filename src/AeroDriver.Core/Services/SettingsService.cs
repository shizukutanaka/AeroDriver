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
    // partial: 入れ子の SettingsJsonContext は JSON ソースジェネレーターが生成する partial クラスであり、
    // それを内包する型自身も partial でないと生成コードを差し込めない
    public sealed partial class SettingsService : ISettingsService
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

        public bool CreateRestorePoint
        {
            get { lock (_lock) return _data.CreateRestorePoint; }
            set
            {
                lock (_lock) _data = _data with { CreateRestorePoint = value };
                Save();
            }
        }

        public string? ThemeName
        {
            get { lock (_lock) return _data.ThemeName; }
            set
            {
                lock (_lock) _data = _data with { ThemeName = value };
                Save();
            }
        }

        public string? CultureName
        {
            get { lock (_lock) return _data.CultureName; }
            set
            {
                lock (_lock) _data = _data with { CultureName = value };
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

                // 一時ファイルに書いてから置換する(InstallHistoryService の切り詰めと同じ方針)。
                // File.WriteAllText は「切り詰めてから書く」ので、書き込み途中で電源断や
                // プロセス終了が起きると**空/途中までの JSON**が残る。Load() はそれを
                // 読めずに既定値へ落ちるため、ユーザーの設定が黙って全損する。
                // File.Move(overwrite: true) は同一ボリューム上ではアトミックに置換される
                var tempPath = _settingsPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _settingsPath, overwrite: true);
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
        // 既存の settings.json には後続フィールドが存在しないが、positional record の
        // デシリアライズでは欠けた引数が型の既定値になるため読み込みは壊れない。
        // ただし bool の既定は false なので、既定 true にしたい項目は Load() 後に補正せず
        // 「false = 明示的に無効化」と解釈できる設計にしておくこと。
        private sealed record SettingsData(
            bool AutoUpdateEnabled,
            bool IncludeBetaDrivers,
            bool BackupEnabled,
            int MaxBackupGenerations,
            bool CreateRestorePoint = true,
            // GUI の選択を再起動後も保つ。null = 未設定(初回起動時は OS 既定に従う)
            string? ThemeName = null,
            string? CultureName = null)
        {
            public static readonly SettingsData Default = new(
                AutoUpdateEnabled: true,
                IncludeBetaDrivers: false,
                BackupEnabled: true,
                MaxBackupGenerations: 3,
                CreateRestorePoint: true,
                ThemeName: null,
                CultureName: null);
        }

        // JsonSerializerContext: Source Generation でリフレクション不要なシリアライザーを生成
        // WriteIndented = true でファイルを人間が読める形式に保つ
        [JsonSourceGenerationOptions(WriteIndented = true)]
        [JsonSerializable(typeof(SettingsData))]
        private sealed partial class SettingsJsonContext : JsonSerializerContext { }
    }
}
