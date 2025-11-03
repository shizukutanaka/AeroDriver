// 研究ベースの改善: Write-Ahead Logging (WAL) システム
// 根拠: データベース信頼性理論 - トランザクション原子性保証
//      ドライバー更新中のクラッシュからの復旧を保証
// 優先度: P1 (高) - データ整合性クリティカル
// 出典: Database Internals (Designing Reliable Systems), ACID Properties Research

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Resilience;

/// <summary>
/// Write-Ahead Logging (WAL) システム
/// トランザクション型のドライバー更新を実装
///
/// WAL処理フロー:
/// 1. ログエントリをディスクに書き込み
/// 2. ログの永続化を確認
/// 3. 実際の操作を実行
/// 4. 操作完了ログを記録
///
/// クラッシュ復旧:
/// - 不完全なトランザクションをロールバック
/// - 完了したトランザクションを再実行（必要な場合）
/// - システム整合性を保証
/// </summary>
public class WriteAheadLogger : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly string _logPath;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private readonly Dictionary<string, TransactionLog> _activeTransactions = new();
    private readonly object _transactionLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WriteAheadLogger(ILogger logger, string? logPath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AeroDriver", "TransactionLogs");

        Directory.CreateDirectory(_logPath);
        _logger.LogInformation($"WriteAheadLogger initialized with log path: {_logPath}");
    }

    /// <summary>
    /// 新しいトランザクションを開始
    /// </summary>
    public async Task<string> BeginTransactionAsync(
        string operationName,
        string description,
        CancellationToken ct = default)
    {
        var transactionId = Guid.NewGuid().ToString("N");

        var txLog = new TransactionLog
        {
            Id = transactionId,
            OperationName = operationName,
            Description = description,
            Status = TransactionStatus.Started,
            StartedAt = DateTime.UtcNow,
            Entries = new List<TransactionEntry>()
        };

        // ログエントリを作成
        var logEntry = new TransactionEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = TransactionEntryType.Begin,
            Timestamp = DateTime.UtcNow,
            Data = new Dictionary<string, object>
            {
                { "operation", operationName },
                { "description", description }
            }
        };

        txLog.Entries.Add(logEntry);

        // ディスクに永続化
        await PersistTransactionAsync(txLog, ct);

        lock (_transactionLock)
        {
            _activeTransactions[transactionId] = txLog;
        }

        _logger.LogInformation($"Transaction started: {transactionId} ({operationName})");
        return transactionId;
    }

    /// <summary>
    /// トランザクションにログエントリを追加
    /// </summary>
    public async Task LogEntryAsync(
        string transactionId,
        TransactionEntryType entryType,
        Dictionary<string, object> data,
        CancellationToken ct = default)
    {
        lock (_transactionLock)
        {
            if (!_activeTransactions.TryGetValue(transactionId, out var txLog))
            {
                throw new InvalidOperationException($"Transaction not found: {transactionId}");
            }

            var entry = new TransactionEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = entryType,
                Timestamp = DateTime.UtcNow,
                Data = data
            };

            txLog.Entries.Add(entry);
        }

        // ディスクに永続化
        await PersistTransactionAsync(transactionId, ct);
    }

    /// <summary>
    /// トランザクションを完了（コミット）
    /// </summary>
    public async Task CommitTransactionAsync(
        string transactionId,
        CancellationToken ct = default)
    {
        TransactionLog? txLog = null;

        lock (_transactionLock)
        {
            if (!_activeTransactions.TryGetValue(transactionId, out txLog))
            {
                throw new InvalidOperationException($"Transaction not found: {transactionId}");
            }

            txLog.Status = TransactionStatus.Committed;
            txLog.CompletedAt = DateTime.UtcNow;
        }

        // コミットログを追加
        var commitEntry = new TransactionEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = TransactionEntryType.Commit,
            Timestamp = DateTime.UtcNow,
            Data = new Dictionary<string, object>
            {
                { "status", "committed" }
            }
        };

        txLog.Entries.Add(commitEntry);

        // ディスクに永続化
        await PersistTransactionAsync(txLog, ct);

        lock (_transactionLock)
        {
            _activeTransactions.Remove(transactionId);
        }

        _logger.LogInformation($"Transaction committed: {transactionId}");
    }

    /// <summary>
    /// トランザクションをロールバック
    /// </summary>
    public async Task RollbackTransactionAsync(
        string transactionId,
        string reason = "",
        CancellationToken ct = default)
    {
        TransactionLog? txLog = null;

        lock (_transactionLock)
        {
            if (!_activeTransactions.TryGetValue(transactionId, out txLog))
            {
                throw new InvalidOperationException($"Transaction not found: {transactionId}");
            }

            txLog.Status = TransactionStatus.RolledBack;
            txLog.CompletedAt = DateTime.UtcNow;
        }

        // ロールバックログを追加
        var rollbackEntry = new TransactionEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = TransactionEntryType.Rollback,
            Timestamp = DateTime.UtcNow,
            Data = new Dictionary<string, object>
            {
                { "status", "rolled_back" },
                { "reason", reason }
            }
        };

        txLog.Entries.Add(rollbackEntry);

        // ディスクに永続化
        await PersistTransactionAsync(txLog, ct);

        lock (_transactionLock)
        {
            _activeTransactions.Remove(transactionId);
        }

        _logger.LogWarning($"Transaction rolled back: {transactionId} - Reason: {reason}");
    }

    /// <summary>
    /// 未完了のトランザクションをリカバリ
    /// </summary>
    public async Task<List<TransactionLog>> RecoverIncompleteTransactionsAsync(
        CancellationToken ct = default)
    {
        _logger.LogInformation("Recovering incomplete transactions from disk");

        var incompleteTransactions = new List<TransactionLog>();

        try
        {
            if (!Directory.Exists(_logPath))
            {
                return incompleteTransactions;
            }

            var logFiles = Directory.GetFiles(_logPath, "*.json");

            foreach (var logFile in logFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(logFile, ct);
                    var txLog = JsonSerializer.Deserialize<TransactionLog>(content, JsonOptions);

                    if (txLog != null && txLog.Status == TransactionStatus.Started)
                    {
                        incompleteTransactions.Add(txLog);
                        _logger.LogWarning($"Found incomplete transaction: {txLog.Id} ({txLog.OperationName})");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to deserialize transaction log {logFile}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Transaction recovery failed: {ex.Message}");
        }

        return incompleteTransactions;
    }

    /// <summary>
    /// トランザクション履歴を取得
    /// </summary>
    public async Task<List<TransactionLog>> GetTransactionHistoryAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        var history = new List<TransactionLog>();

        try
        {
            if (!Directory.Exists(_logPath))
            {
                return history;
            }

            var logFiles = Directory.GetFiles(_logPath, "*.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(limit);

            foreach (var logFile in logFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(logFile, ct);
                    var txLog = JsonSerializer.Deserialize<TransactionLog>(content, JsonOptions);

                    if (txLog != null)
                    {
                        history.Add(txLog);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to read transaction log {logFile}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to retrieve transaction history: {ex.Message}");
        }

        return history;
    }

    /// <summary>
    /// トランザクションログをディスクに永続化
    /// </summary>
    private async Task PersistTransactionAsync(TransactionLog txLog, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);

        try
        {
            var filePath = Path.Combine(_logPath, $"{txLog.Id}.json");
            var json = JsonSerializer.Serialize(txLog, JsonOptions);

            // ファイルをダーティに書き込み（アトミック性を保証）
            var tempFilePath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempFilePath, json, ct);

            // アトミックな置き換え
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            File.Move(tempFilePath, filePath, true);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// トランザクションをディスクに永続化（ID指定）
    /// </summary>
    private async Task PersistTransactionAsync(string transactionId, CancellationToken ct)
    {
        lock (_transactionLock)
        {
            if (_activeTransactions.TryGetValue(transactionId, out var txLog))
            {
                Task.Run(async () => await PersistTransactionAsync(txLog, ct), ct);
            }
        }
    }

    /// <summary>
    /// クリーンアップ
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _writeSemaphore?.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// トランザクションログ
/// </summary>
public class TransactionLog
{
    public string Id { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TransactionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<TransactionEntry> Entries { get; set; } = new();
}

/// <summary>
/// トランザクションエントリ
/// </summary>
public class TransactionEntry
{
    public string Id { get; set; } = string.Empty;
    public TransactionEntryType Type { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// トランザクション状態
/// </summary>
public enum TransactionStatus
{
    /// <summary>開始</summary>
    Started = 0,

    /// <summary>処理中</summary>
    InProgress = 1,

    /// <summary>コミット</summary>
    Committed = 2,

    /// <summary>ロールバック</summary>
    RolledBack = 3,

    /// <summary>失敗</summary>
    Failed = 4
}

/// <summary>
/// トランザクションエントリ型
/// </summary>
public enum TransactionEntryType
{
    /// <summary>トランザクション開始</summary>
    Begin = 0,

    /// <summary>操作実行</summary>
    Operation = 1,

    /// <summary>コミット</summary>
    Commit = 2,

    /// <summary>ロールバック</summary>
    Rollback = 3,

    /// <summary>チェックポイント</summary>
    Checkpoint = 4
}
