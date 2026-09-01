using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Services
{
    /// <summary>
    /// インストール履歴を JSONL(1行1JSON)で追記記録します。
    ///
    /// なぜ JSONL で「追記のみ」なのか:
    /// - 単一のJSON配列にすると保存のたびに全体を書き直す必要があり、書き込み途中で
    ///   電源断・クラッシュが起きるとファイル全体が壊れて<b>過去の履歴を丸ごと失う</b>。
    ///   ドライバー更新はまさにシステムが不安定になりうる操作なので、この失敗様式は致命的
    /// - 追記なら既存バイト列に触れないため、事故が起きても壊れるのは最後の1行だけ。
    ///   読み出し側は壊れた行だけを読み飛ばして残りを復元できる(<see cref="GetHistoryAsync"/>)
    ///
    /// 記録先は %LOCALAPPDATA%\AeroDriver\install-history.jsonl。外部送信は一切しません。
    /// </summary>
    // partial: 入れ子の HistoryJsonContext は JSON ソースジェネレーターが生成する partial クラスであり、
    // それを内包する型自身も partial でないと生成コードを差し込めない
    public sealed partial class InstallHistoryService : IInstallHistoryService
    {
        private readonly ILogger<InstallHistoryService> _logger;
        private readonly string _historyFile;
        // 追記の直列化。複数のインストールが並行しても行が混ざらないようにする
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        /// <summary>履歴ファイルの上限。超えたら古い方を切り捨てる(無制限に肥大させない)。</summary>
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MiB

        public InstallHistoryService(ILogger<InstallHistoryService> logger)
            : this(logger, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AeroDriver", "install-history.jsonl"))
        { }

        // テスト用: 記録先を外から指定できる
        internal InstallHistoryService(ILogger<InstallHistoryService> logger, string historyFile)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _historyFile = historyFile;
        }

        public async Task RecordAsync(InstallHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_historyFile)!);
                await TrimIfTooLargeAsync(cancellationToken).ConfigureAwait(false);

                var json = JsonSerializer.Serialize(entry, HistoryJsonContext.Default.InstallHistoryEntry);

                // 改行は必ず '\n' に固定する。環境依存の改行だと行分割の解釈がぶれる
                await File.AppendAllTextAsync(_historyFile, json + "\n", Encoding.UTF8, cancellationToken)
                          .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 監査記録の失敗でインストールを失敗扱いにしない(可用性層のフェイルオープン)
                _logger.LogWarning(ex, "インストール履歴の記録に失敗しました: {Path}", _historyFile);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<IReadOnlyList<InstallHistoryEntry>> GetHistoryAsync(
            int limit = 0, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_historyFile)) return Array.Empty<InstallHistoryEntry>();

            var entries = new List<InstallHistoryEntry>();
            int corrupt = 0;

            try
            {
                foreach (var line in await File.ReadAllLinesAsync(_historyFile, cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var entry = JsonSerializer.Deserialize(line, HistoryJsonContext.Default.InstallHistoryEntry);
                        if (entry != null) entries.Add(entry);
                    }
                    catch (JsonException)
                    {
                        // 途中まで書かれた行など。この行だけ捨てて残りは活かす
                        corrupt++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "インストール履歴の読み込みに失敗しました: {Path}", _historyFile);
                return entries;
            }

            if (corrupt > 0)
                _logger.LogWarning("インストール履歴の {Count} 行が破損していたため読み飛ばしました", corrupt);

            // 新しい順
            entries.Reverse();
            return limit > 0 ? entries.Take(limit).ToList() : entries;
        }

        /// <summary>
        /// 上限を超えたら後半(新しい方)だけを残して書き直す。
        /// 切り詰めは追記より危険な操作なので、通常運用ではほぼ発生しないサイズに上限を置いている。
        /// </summary>
        private async Task TrimIfTooLargeAsync(CancellationToken ct)
        {
            try
            {
                if (!File.Exists(_historyFile)) return;
                if (new FileInfo(_historyFile).Length < MaxFileSizeBytes) return;

                var lines = await File.ReadAllLinesAsync(_historyFile, ct).ConfigureAwait(false);
                var keep = lines.Skip(lines.Length / 2).ToArray(); // 新しい半分を残す

                // 一時ファイルに書いてから置換する(書き込み途中の電源断で履歴を全損させない)
                // 一時ファイル名はプロセスごとに一意にする(固定名だと複数プロセスが衝突する)
                var tempFile = _historyFile + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllLinesAsync(tempFile, keep, Encoding.UTF8, ct).ConfigureAwait(false);
                    File.Move(tempFile, _historyFile, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempFile)) { try { File.Delete(tempFile); } catch { } }
                }

                _logger.LogInformation(
                    "インストール履歴が上限に達したため古い {Count} 件を削除しました",
                    lines.Length - keep.Length);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "インストール履歴の切り詰めに失敗しました(記録は継続します)");
            }
        }

        // Source Generation: リフレクション不要 → AOT互換(リポジトリ既存の SettingsJsonContext と同方針)
        [JsonSourceGenerationOptions(WriteIndented = false)]
        [JsonSerializable(typeof(InstallHistoryEntry))]
        private sealed partial class HistoryJsonContext : JsonSerializerContext { }
    }
}
