using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// PCI IDs データベース (pci-ids.ucw.cz) をローカルにキャッシュして
    /// ベンダーID・デバイスIDから名称を解決します。
    /// ライセンス: CC-BY-SA 3.0（帰属表示が必要）
    /// </summary>
    public sealed class PciIdDatabase
    {
        private readonly ILogger<PciIdDatabase> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _cacheFile;
        private readonly TimeSpan _cacheLifetime = TimeSpan.FromDays(7);

        // FrozenDictionary: 起動後は読み取りのみ → ロックレス O(1) ルックアップ
        // 通常の Dictionary より構築コストは高いが、50,000+ ベンダーへの繰り返し検索で元が取れる
        private FrozenDictionary<string, (string Name, FrozenDictionary<string, string> Devices)>? _db;

        // GitHub ミラー（ucw.czより安定している）
        private const string DatabaseUrl =
            "https://raw.githubusercontent.com/pciutils/pciids/master/pci.ids";

        public PciIdDatabase(ILogger<PciIdDatabase> logger, HttpClient httpClient)
            : this(logger, httpClient, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver", "pci.ids"))
        { }

        // テスト用: キャッシュファイルパスを外から指定できる
        protected PciIdDatabase(ILogger<PciIdDatabase> logger, HttpClient httpClient, string cacheFile)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AeroDriver/1.0");
            _cacheFile = cacheFile;
        }

        /// <summary>
        /// ベンダーIDからベンダー名を返します（例: "8086" → "Intel Corporation"）
        /// </summary>
        public async Task<string?> GetVendorNameAsync(string vendorId, CancellationToken ct = default)
        {
            var db = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var key = vendorId.ToUpperInvariant();
            return db.TryGetValue(key, out var entry) ? entry.Name : null;
        }

        /// <summary>
        /// ベンダーIDとデバイスIDからデバイス名を返します（例: "8086","0406" → "Core i5 GPU"）
        /// </summary>
        public async Task<string?> GetDeviceNameAsync(string vendorId, string deviceId, CancellationToken ct = default)
        {
            var db = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var vkey = vendorId.ToUpperInvariant();
            var dkey = deviceId.ToUpperInvariant();

            if (db.TryGetValue(vkey, out var entry) &&
                entry.Devices.TryGetValue(dkey, out var name))
                return name;

            return null;
        }

        /// <summary>
        /// ベンダー名からベンダーIDを逆引きします（大文字小文字無視）
        /// </summary>
        public async Task<string?> GetVendorIdByNameAsync(string vendorName, CancellationToken ct = default)
        {
            var db = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            foreach (var (id, entry) in db)
            {
                if (entry.Name.Contains(vendorName, StringComparison.OrdinalIgnoreCase))
                    return id;
            }
            return null;
        }

        /// <summary>DBを強制更新します（週次更新推奨）</summary>
        public async Task RefreshAsync(CancellationToken ct = default)
        {
            await DownloadAndParseAsync(ct).ConfigureAwait(false);
        }

        private async Task<FrozenDictionary<string, (string, FrozenDictionary<string, string>)>> EnsureLoadedAsync(CancellationToken ct)
        {
            if (_db != null) return _db;

            // キャッシュファイルが有効なら読み込む
            if (File.Exists(_cacheFile) &&
                (DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFile)) < _cacheLifetime)
            {
                _db = await ParseFileAsync(_cacheFile, ct).ConfigureAwait(false);
                _logger.LogInformation("PCI IDs をキャッシュから読み込みました ({Count} ベンダー)", _db.Count);
                return _db;
            }

            await DownloadAndParseAsync(ct).ConfigureAwait(false);
            return _db!;
        }

        private async Task DownloadAndParseAsync(CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("PCI IDs データベースをダウンロードしています: {Url}", DatabaseUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);

                var content = await _httpClient.GetStringAsync(DatabaseUrl, ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(_cacheFile, content, ct).ConfigureAwait(false);

                _db = Parse(content);
                _logger.LogInformation("PCI IDs を更新しました ({Count} ベンダー)", _db.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PCI IDs ダウンロード失敗。キャッシュを使用します");

                // ダウンロード失敗時でもキャッシュがあれば使う
                if (File.Exists(_cacheFile))
                    _db = await ParseFileAsync(_cacheFile, ct).ConfigureAwait(false);
                else
                    _db = new(); // 空でフォールバック
            }
        }

        private static async Task<FrozenDictionary<string, (string, FrozenDictionary<string, string>)>> ParseFileAsync(
            string path, CancellationToken ct)
        {
            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return Parse(content);
        }

        private static FrozenDictionary<string, (string, FrozenDictionary<string, string>)> Parse(string content)
        {
            // 中間バッファ: パース中は mutable Dictionary、完成後に FrozenDictionary へ変換
            var builder = new Dictionary<string, (string, Dictionary<string, string>)>(StringComparer.OrdinalIgnoreCase);
            string? currentVendorId = null;
            var currentDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? currentVendorName = null;

            foreach (var rawLine in content.AsSpan().EnumerateLines())
            {
                var line = rawLine.TrimEnd();

                // コメントと空行をスキップ
                if (line.IsEmpty || line[0] == '#') continue;

                // サブシステム行（2タブ）はスキップ
                if (line.Length >= 2 && line[0] == '\t' && line[1] == '\t') continue;

                if (line[0] == '\t')
                {
                    // デバイス行: "\tXXXX  Device Name"
                    if (currentVendorId == null) continue;
                    var s = line.TrimStart('\t');
                    var idx = s.IndexOf("  ");
                    if (idx < 0) continue;
                    var devId = s[..idx].ToString().ToUpperInvariant();
                    var devName = s[(idx + 2)..].ToString();
                    currentDevices[devId] = devName;
                }
                else
                {
                    // ベンダー行: "XXXX  Vendor Name"
                    if (currentVendorId != null && currentVendorName != null)
                    {
                        builder[currentVendorId] = (currentVendorName, currentDevices);
                        currentDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }

                    var s = line.ToString();
                    var idx = s.IndexOf("  ", StringComparison.Ordinal);
                    if (idx < 0) { currentVendorId = null; continue; }

                    currentVendorId = s[..idx].ToUpperInvariant();
                    currentVendorName = s[(idx + 2)..];
                }
            }

            // 最後のベンダーを追加
            if (currentVendorId != null && currentVendorName != null)
                builder[currentVendorId] = (currentVendorName, currentDevices);

            // FrozenDictionary に変換: 以降は読み取り専用、ロックレス O(1) ルックアップ
            return builder.ToFrozenDictionary(
                kvp => kvp.Key,
                kvp => (kvp.Value.Item1, kvp.Value.Item2.ToFrozenDictionary(
                    StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
