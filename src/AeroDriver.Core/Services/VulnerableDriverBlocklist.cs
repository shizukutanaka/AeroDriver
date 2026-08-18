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
    /// キャッシュは %LOCALAPPDATA% に置き、7日TTLで更新する。
    /// </summary>
    public class VulnerableDriverBlocklist
    {
        private readonly ILogger<VulnerableDriverBlocklist> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _cacheFile;
        private readonly TimeSpan _cacheLifetime = TimeSpan.FromDays(7);
        // フェイルオープン(空集合)時は短い間隔で再試行し、ネットワーク復旧を早く反映する。
        // 成功ロードは _cacheLifetime(7日)までプロセス内で固定でよい(リスト更新は日単位のため)。
        private static readonly TimeSpan FailOpenRetryInterval = TimeSpan.FromMinutes(15);

        // 読み取りは FrozenSet でロックレス O(1)。ただしTTLで再評価するためロード時刻も保持する
        // (初回ロード後プロセス生存中固定だと、空集合が残り続けて照合が永久にスキップされる)
        private FrozenSet<string>? _hashes;
        private DateTime _loadedAtUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _loadLock = new(1, 1);

        /// <summary>テスト用の時刻シーム(サブクラスで上書きしてTTL経過を再現できる)。</summary>
        protected virtual DateTime UtcNow => DateTime.UtcNow;

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

        /// <summary>
        /// メモリ上のロード結果がまだ有効か。空集合(フェイルオープン)は短TTL、
        /// 通常ロードは _cacheLifetime を適用する。
        /// </summary>
        private bool IsFresh()
        {
            if (_hashes == null) return false;
            var ttl = _hashes.Count == 0 ? FailOpenRetryInterval : _cacheLifetime;
            return (UtcNow - _loadedAtUtc) < ttl;
        }

        private async Task<FrozenSet<string>> EnsureLoadedAsync(CancellationToken ct)
        {
            if (IsFresh()) return _hashes!;

            await _loadLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 待機中に別スレッドがロード済みかもしれないので再チェック
                if (IsFresh()) return _hashes!;

                _hashes = await LoadHashesAsync(ct).ConfigureAwait(false);
                _loadedAtUtc = UtcNow;
                return _hashes;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>
        /// ファイルキャッシュ(有効なら)→ ダウンロード → キャッシュフォールバック → 空集合 の順で
        /// ハッシュ集合を取得する。呼び出し元(_loadLock 内)が _hashes/_loadedAtUtc に代入する。
        /// </summary>
        private async Task<FrozenSet<string>> LoadHashesAsync(CancellationToken ct)
        {
            if (File.Exists(_cacheFile) &&
                (UtcNow - File.GetLastWriteTimeUtc(_cacheFile)) < _cacheLifetime)
            {
                var cached = ParseSafe(await File.ReadAllTextAsync(_cacheFile, ct).ConfigureAwait(false));
                _logger.LogInformation("脆弱ドライバーリストをキャッシュから読み込みました ({Count} ハッシュ)", cached.Count);
                return cached;
            }

            try
            {
                _logger.LogInformation("脆弱ドライバーリストをダウンロードしています: {Url}", BlocklistUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);

                var content = await _httpClient.GetStringAsync(BlocklistUrl, ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(_cacheFile, content, ct).ConfigureAwait(false);
                var parsed = ParseSafe(content);
                _logger.LogInformation("脆弱ドライバーリストを更新しました ({Count} ハッシュ)", parsed.Count);
                return parsed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "脆弱ドライバーリストのダウンロードに失敗しました。キャッシュを使用します");

                if (File.Exists(_cacheFile))
                    return ParseSafe(await File.ReadAllTextAsync(_cacheFile, ct).ConfigureAwait(false));

                // 空 = 照合スキップ(フェイルオープン)。FailOpenRetryInterval 後に再試行される
                return FrozenSet<string>.Empty;
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
