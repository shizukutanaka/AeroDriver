using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// 既知の脆弱ドライバー(BYOVD攻撃に悪用されるもの)のSHA256ブロックリスト。
    /// データソースは LOLDrivers プロジェクトの公式JSON(無料・機械可読)。
    /// Microsoftの脆弱ドライバーブロックリストはHVCI有効時しか強制されず、更新も
    /// 年1〜2回と遅い(CVE-2025-59033参照)ため、インストーラー側での自衛層として追加。
    /// キャッシュ方式は <see cref="PciIdDatabase"/> と同じ(%LOCALAPPDATA%、7日TTL)。
    /// </summary>
    public class VulnerableDriverBlocklist
    {
        private readonly ILogger<VulnerableDriverBlocklist> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _cacheFile;
        private readonly TimeSpan _cacheLifetime = TimeSpan.FromDays(7);

        // 起動後は読み取りのみ → FrozenSet でロックレス O(1) 照合
        private FrozenSet<string>? _hashes;
        private readonly SemaphoreSlim _loadLock = new(1, 1);

        private const string BlocklistUrl = "https://www.loldrivers.io/api/drivers.json";

        public VulnerableDriverBlocklist(ILogger<VulnerableDriverBlocklist> logger, HttpClient httpClient)
            : this(logger, httpClient, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver", "loldrivers.json"))
        { }

        // テスト用: キャッシュファイルパスを外から指定できる
        protected VulnerableDriverBlocklist(ILogger<VulnerableDriverBlocklist> logger, HttpClient httpClient, string cacheFile)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AeroDriver/1.0");
            _cacheFile = cacheFile;
        }

        /// <summary>
        /// ファイルのSHA256が既知の脆弱ドライバーリストに含まれるかを返します。
        /// リストが取得できない場合(ネットワーク断+キャッシュ無し)は false
        /// (フェイルオープン)。照合の欠落でインストール機能全体を殺さないためだが、
        /// その旨を警告ログで明示する。
        /// </summary>
        public async Task<bool> IsKnownVulnerableAsync(string filePath, CancellationToken ct = default)
        {
            var hashes = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (hashes.Count == 0)
            {
                _logger.LogWarning("脆弱ドライバーリストが利用できないため照合をスキップします: {Path}", filePath);
                return false;
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, useAsync: true);
            var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
            return hashes.Contains(Convert.ToHexString(hash));
        }

        private async Task<FrozenSet<string>> EnsureLoadedAsync(CancellationToken ct)
        {
            if (_hashes != null) return _hashes;

            await _loadLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_hashes != null) return _hashes;

                if (File.Exists(_cacheFile) &&
                    (DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFile)) < _cacheLifetime)
                {
                    _hashes = ParseSafe(await File.ReadAllTextAsync(_cacheFile, ct).ConfigureAwait(false));
                    _logger.LogInformation("脆弱ドライバーリストをキャッシュから読み込みました ({Count} ハッシュ)", _hashes.Count);
                    return _hashes;
                }

                try
                {
                    _logger.LogInformation("脆弱ドライバーリストをダウンロードしています: {Url}", BlocklistUrl);
                    Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);

                    var content = await _httpClient.GetStringAsync(BlocklistUrl, ct).ConfigureAwait(false);
                    await File.WriteAllTextAsync(_cacheFile, content, ct).ConfigureAwait(false);
                    _hashes = ParseSafe(content);
                    _logger.LogInformation("脆弱ドライバーリストを更新しました ({Count} ハッシュ)", _hashes.Count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "脆弱ドライバーリストのダウンロードに失敗しました。キャッシュを使用します");

                    if (File.Exists(_cacheFile))
                        _hashes = ParseSafe(await File.ReadAllTextAsync(_cacheFile, ct).ConfigureAwait(false));
                    else
                        _hashes = FrozenSet<string>.Empty; // 空 = 照合スキップ(フェイルオープン)
                }

                return _hashes;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>
        /// LOLDrivers JSON から全サンプルのSHA256を抽出する。
        /// 構造: [{ "KnownVulnerableSamples": [{ "SHA256": "..." }, ...] }, ...]
        /// 破損JSONは空集合を返す(フェイルオープン。例外を照合呼び出し元に漏らさない)。
        /// </summary>
        private FrozenSet<string> ParseSafe(string json)
        {
            try
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var doc = JsonDocument.Parse(json);

                foreach (var driver in doc.RootElement.EnumerateArray())
                {
                    if (!driver.TryGetProperty("KnownVulnerableSamples", out var samples) ||
                        samples.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var sample in samples.EnumerateArray())
                    {
                        if (sample.TryGetProperty("SHA256", out var sha) &&
                            sha.ValueKind == JsonValueKind.String &&
                            sha.GetString() is { Length: 64 } hex)
                        {
                            result.Add(hex);
                        }
                    }
                }

                return result.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "脆弱ドライバーリストのJSONが不正です。照合をスキップします");
                return FrozenSet<string>.Empty;
            }
        }
    }
}
