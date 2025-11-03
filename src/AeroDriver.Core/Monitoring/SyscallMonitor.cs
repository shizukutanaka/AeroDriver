// 研究ベースの改善: システムコール監視エンジン
// 根拠: Kernel Syscall Interception, EDR Detection, Anomaly Detection
//      ドライバーの異常な動作パターン検出と実時間監視
// 優先度: P1 (高) - 動作異常検出クリティカル
// 出典: Windows Kernel Documentation, ETW Syscall Tracing, EDR Research

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Monitoring;

/// <summary>
/// システムコール監視エンジン
/// ドライバーの異常なシステムコール活動を検出
///
/// 機能:
/// 1. ダイレクトシステムコール検出 - EDR 回避技術の識別
/// 2. 異常検出 - 通常と異なるコールパターンの識別
/// 3. リアルタイム監視 - カーネルモードの活動追跡
/// 4. 脅威スコアリング - 動作の悪意度合いを計算
/// 5. 自動警告生成 - 異常検出時にアラート生成
/// </summary>
public class SyscallMonitor
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, SyscallStatistics> _driverProfiles;
    private readonly Dictionary<string, List<SyscallEvent>> _eventLogs;
    private readonly SyscallAnomalyDetector _anomalyDetector;

    public SyscallMonitor(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driverProfiles = new Dictionary<string, SyscallStatistics>();
        _eventLogs = new Dictionary<string, List<SyscallEvent>>();
        _anomalyDetector = new SyscallAnomalyDetector();

        _logger.LogInformation("SyscallMonitor initialized");
    }

    /// <summary>
    /// ドライバーのシステムコール監視を開始
    /// </summary>
    public async Task<string> StartMonitoringAsync(
        string driverName,
        string driverId,
        MonitoringLevel level = MonitoringLevel.Standard,
        CancellationToken ct = default)
    {
        _logger.LogInformation($"Starting syscall monitoring for {driverName} at level {level}");

        var sessionId = Guid.NewGuid().ToString();

        if (!_driverProfiles.ContainsKey(driverId))
        {
            _driverProfiles[driverId] = new SyscallStatistics
            {
                DriverName = driverName,
                DriverId = driverId,
                MonitoringLevel = level
            };
        }

        if (!_eventLogs.ContainsKey(driverId))
        {
            _eventLogs[driverId] = new List<SyscallEvent>();
        }

        // バックグラウンドで監視タスクを開始
        _ = MonitoringScanAsync(driverId, level, ct);

        return sessionId;
    }

    /// <summary>
    /// 監視スキャンを実行
    /// </summary>
    private async Task MonitoringScanAsync(
        string driverId,
        MonitoringLevel level,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // ETW またはシステムコールトレーシング経由でイベントを収集
                var events = await CollectSyscallEventsAsync(driverId, level, ct);

                foreach (var evt in events)
                {
                    _eventLogs[driverId].Add(evt);

                    // 異常検出
                    var anomaly = _anomalyDetector.DetectAnomaly(evt, _driverProfiles[driverId]);
                    if (anomaly != null)
                    {
                        _logger.LogWarning($"Anomaly detected: {anomaly.AnomalyType} - {anomaly.Description}");
                    }
                }

                // 統計情報を更新
                UpdateStatistics(driverId, events);

                await Task.Delay(100, ct); // サンプリング間隔
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Monitoring error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// システムコールイベントを収集
    /// </summary>
    private async Task<List<SyscallEvent>> CollectSyscallEventsAsync(
        string driverId,
        MonitoringLevel level,
        CancellationToken ct)
    {
        await Task.Delay(0, ct);

        var events = new List<SyscallEvent>();

        // シミュレートされたシステムコールイベント
        var syscallCounts = new Dictionary<uint, int>
        {
            { 0x00, 10 },  // NtQueryInformationFile
            { 0x01, 5 },   // NtSetInformationFile
            { 0x02, 2 },   // NtOpenFile
            { 0x03, 8 },   // NtCreateFile
            { 0x04, 3 },   // NtReadFile
            { 0x05, 12 },  // NtWriteFile
            { 0x06, 1 },   // NtDeleteFile
        };

        // モニタリングレベルに応じて詳細度を調整
        int detailLevel = level switch
        {
            MonitoringLevel.Minimal => 1,
            MonitoringLevel.Standard => 3,
            MonitoringLevel.Comprehensive => 5,
            MonitoringLevel.Aggressive => 7,
            _ => 3
        };

        foreach (var (syscallNum, count) in syscallCounts.Take(detailLevel))
        {
            for (int i = 0; i < count; i++)
            {
                events.Add(new SyscallEvent
                {
                    DriverId = driverId,
                    SyscallNumber = syscallNum,
                    SyscallName = GetSyscallName(syscallNum),
                    Timestamp = DateTime.UtcNow.AddSeconds(-Random.Shared.Next(5)),
                    CallerContext = "KernelMode",
                    ReturnValue = Random.Shared.Next(0, 256)
                });
            }
        }

        return events;
    }

    /// <summary>
    /// ダイレクトシステムコール検出
    /// </summary>
    public async Task<DirectSyscallDetectionResult> DetectDirectSyscallsAsync(
        string driverId,
        CancellationToken ct = default)
    {
        _logger.LogInformation($"Checking for direct syscalls from {driverId}");

        var result = new DirectSyscallDetectionResult
        {
            DriverId = driverId,
            AnalyzedAt = DateTime.UtcNow
        };

        try
        {
            if (!_eventLogs.TryGetValue(driverId, out var events))
            {
                result.DirectSyscallsDetected = false;
                return result;
            }

            // 直接システムコールの指標を分析
            var directCallIndicators = AnalyzeDirectCallPatterns(events);

            result.DirectSyscallsDetected = directCallIndicators.Count > 0;
            result.Indicators = directCallIndicators;

            if (result.DirectSyscallsDetected)
            {
                result.ThreatLevel = ThreatLevel.High;
                result.RecommendedAction = "Block driver - EDR evasion attempted";
                _logger.LogError("Direct syscalls detected - likely EDR evasion attempt");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Direct syscall detection failed: {ex.Message}");
            result.Exception = ex;
        }

        return result;
    }

    /// <summary>
    /// 異常検出結果を取得
    /// </summary>
    public async Task<AnomalyDetectionResult> GetAnomaliesAsync(
        string driverId,
        CancellationToken ct = default)
    {
        _logger.LogInformation($"Analyzing anomalies for {driverId}");

        var result = new AnomalyDetectionResult
        {
            DriverId = driverId,
            AnalyzedAt = DateTime.UtcNow,
            Anomalies = new List<SyscallAnomaly>()
        };

        try
        {
            if (!_eventLogs.TryGetValue(driverId, out var events) ||
                !_driverProfiles.TryGetValue(driverId, out var profile))
            {
                result.AnomalyScore = 0;
                result.ThreatLevel = ThreatLevel.None;
                return result;
            }

            // 異常を検出
            foreach (var evt in events.TakeLast(100)) // 最新100件のイベント
            {
                var anomaly = _anomalyDetector.DetectAnomaly(evt, profile);
                if (anomaly != null)
                {
                    result.Anomalies.Add(anomaly);
                }
            }

            // 異常スコアを計算
            result.AnomalyScore = CalculateAnomalyScore(result.Anomalies);

            // 脅威レベルを判定
            result.ThreatLevel = result.AnomalyScore switch
            {
                >= 80 => ThreatLevel.Critical,
                >= 60 => ThreatLevel.High,
                >= 40 => ThreatLevel.Medium,
                >= 20 => ThreatLevel.Low,
                _ => ThreatLevel.None
            };

            if (result.AnomalyScore > 0)
            {
                _logger.LogWarning($"Anomalies detected: score={result.AnomalyScore}, count={result.Anomalies.Count}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Anomaly detection failed: {ex.Message}");
            result.Exception = ex;
        }

        return result;
    }

    /// <summary>
    /// 脅威スコアを計算
    /// </summary>
    public async Task<ThreatScoringResult> CalculateThreatScoreAsync(
        string driverId,
        CancellationToken ct = default)
    {
        _logger.LogInformation($"Calculating threat score for {driverId}");

        var result = new ThreatScoringResult
        {
            DriverId = driverId,
            AnalyzedAt = DateTime.UtcNow,
            Factors = new Dictionary<string, double>()
        };

        try
        {
            if (!_eventLogs.TryGetValue(driverId, out var events))
            {
                result.OverallThreatScore = 0;
                result.ThreatLevel = ThreatLevel.None;
                return result;
            }

            // 各脅威要因を評価
            double frequencyScore = CalculateSyscallFrequencyScore(events);
            result.Factors["SystemcallFrequency"] = frequencyScore;

            double patternScore = AnalyzeSuspiciousPatterns(events);
            result.Factors["SuspiciousPatterns"] = patternScore;

            double privilegeScore = AnalyzePrivilegeEscalation(events);
            result.Factors["PrivilegeEscalation"] = privilegeScore;

            double evasionScore = AnalyzeEvasionTechniques(events);
            result.Factors["EvationTechniques"] = evasionScore;

            // 総合脅威スコア（重み付け平均）
            result.OverallThreatScore = (frequencyScore * 0.25) +
                                       (patternScore * 0.35) +
                                       (privilegeScore * 0.25) +
                                       (evasionScore * 0.15);

            result.ThreatLevel = result.OverallThreatScore switch
            {
                >= 75 => ThreatLevel.Critical,
                >= 60 => ThreatLevel.High,
                >= 40 => ThreatLevel.Medium,
                >= 20 => ThreatLevel.Low,
                _ => ThreatLevel.None
            };

            if (result.OverallThreatScore > 50)
            {
                _logger.LogWarning($"High threat detected: score={result.OverallThreatScore:F1}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Threat scoring failed: {ex.Message}");
            result.Exception = ex;
        }

        return result;
    }

    /// <summary>
    /// 統計情報を更新
    /// </summary>
    private void UpdateStatistics(string driverId, List<SyscallEvent> events)
    {
        if (!_driverProfiles.TryGetValue(driverId, out var stats))
            return;

        stats.TotalSyscalls += events.Count;
        stats.LastSampleTime = DateTime.UtcNow;

        foreach (var evt in events)
        {
            if (!stats.SyscallFrequency.ContainsKey(evt.SyscallName))
            {
                stats.SyscallFrequency[evt.SyscallName] = 0;
            }
            stats.SyscallFrequency[evt.SyscallName]++;
        }
    }

    /// <summary>
    /// ダイレクトコールパターンを分析
    /// </summary>
    private List<string> AnalyzeDirectCallPatterns(List<SyscallEvent> events)
    {
        var indicators = new List<string>();

        // 直接システムコール呼び出しの指標
        var suspiciousSyscalls = events
            .Where(e => IsSuspiciousSyscall(e.SyscallNumber))
            .ToList();

        if (suspiciousSyscalls.Count > 5)
        {
            indicators.Add($"Direct syscalls detected: {suspiciousSyscalls.Count} suspicious calls");
        }

        // Nt*関数の直接呼び出し
        var ntCalls = events
            .Where(e => e.SyscallName.StartsWith("Nt"))
            .ToList();

        if (ntCalls.Count > events.Count * 0.7) // 70%以上がNt*
        {
            indicators.Add("High percentage of Nt* direct calls");
        }

        return indicators;
    }

    /// <summary>
    /// 疑わしいパターンを分析
    /// </summary>
    private double AnalyzeSuspiciousPatterns(List<SyscallEvent> events)
    {
        double score = 0;

        var suspiciousCalls = events
            .Where(e => IsSuspiciousSyscall(e.SyscallNumber))
            .ToList();

        if (suspiciousCalls.Count > 0)
        {
            score += Math.Min(suspiciousCalls.Count * 5, 30);
        }

        return Math.Min(score, 100);
    }

    /// <summary>
    /// 権限昇格の試みを分析
    /// </summary>
    private double AnalyzePrivilegeEscalation(List<SyscallEvent> events)
    {
        double score = 0;

        var privEscCalls = events
            .Where(e => e.SyscallName.Contains("Privilege") ||
                       e.SyscallName.Contains("Token") ||
                       e.SyscallName.Contains("Process"))
            .ToList();

        if (privEscCalls.Count > 3)
        {
            score = Math.Min(privEscCalls.Count * 10, 40);
        }

        return Math.Min(score, 100);
    }

    /// <summary>
    /// 回避技術を分析
    /// </summary>
    private double AnalyzeEvasionTechniques(List<SyscallEvent> events)
    {
        double score = 0;

        // 反デバッギング コール
        var evasionCalls = events
            .Where(e => e.SyscallName.Contains("Debug") ||
                       e.SyscallName.Contains("Trace") ||
                       e.SyscallName.Contains("Detach"))
            .ToList();

        if (evasionCalls.Count > 0)
        {
            score = Math.Min(evasionCalls.Count * 15, 50);
        }

        return Math.Min(score, 100);
    }

    /// <summary>
    /// システムコール頻度スコアを計算
    /// </summary>
    private double CalculateSyscallFrequencyScore(List<SyscallEvent> events)
    {
        if (events.Count == 0) return 0;

        // 通常の頻度: 100-1000 calls/sec
        // 異常: > 10000 calls/sec (タイムベース正規化)
        var recentEvents = events.Where(e =>
            e.Timestamp > DateTime.UtcNow.AddSeconds(-1)).ToList();

        if (recentEvents.Count > 10000)
        {
            return 100; // 異常な頻度
        }
        else if (recentEvents.Count > 5000)
        {
            return 80;
        }
        else if (recentEvents.Count > 2000)
        {
            return 50;
        }

        return Math.Min(recentEvents.Count / 100.0, 30);
    }

    /// <summary>
    /// 異常スコアを計算
    /// </summary>
    private double CalculateAnomalyScore(List<SyscallAnomaly> anomalies)
    {
        if (anomalies.Count == 0) return 0;

        double score = 0;
        foreach (var anomaly in anomalies)
        {
            score += anomaly.SuspicionLevel switch
            {
                AnomalySuspicion.Low => 10,
                AnomalySuspicion.Medium => 25,
                AnomalySuspicion.High => 50,
                AnomalySuspicion.Critical => 100,
                _ => 0
            };
        }

        return Math.Min(score / anomalies.Count, 100);
    }

    /// <summary>
    /// 疑わしいシステムコール判定
    /// </summary>
    private bool IsSuspiciousSyscall(uint syscallNumber)
    {
        // 高リスクのシステムコール番号
        var suspiciousSyscalls = new[] { 0x50, 0x51, 0x52, 0xA0, 0xB0 };
        return suspiciousSyscalls.Contains(syscallNumber);
    }

    /// <summary>
    /// システムコール名を取得
    /// </summary>
    private string GetSyscallName(uint syscallNumber)
    {
        return syscallNumber switch
        {
            0x00 => "NtQueryInformationFile",
            0x01 => "NtSetInformationFile",
            0x02 => "NtOpenFile",
            0x03 => "NtCreateFile",
            0x04 => "NtReadFile",
            0x05 => "NtWriteFile",
            0x06 => "NtDeleteFile",
            0x07 => "NtQueryValueKey",
            0x08 => "NtSetValueKey",
            0x50 => "NtRaiseException",
            0x51 => "NtDebugActiveProcess",
            0x52 => "NtDebugContinue",
            0xA0 => "NtTerminateProcess",
            0xB0 => "NtLoadDriver",
            _ => $"Unknown_0x{syscallNumber:X2}"
        };
    }
}

/// <summary>
/// システムコール異常検出器
/// </summary>
public class SyscallAnomalyDetector
{
    private readonly Dictionary<string, int> _baselineFrequency = new();

    public SyscallAnomaly? DetectAnomaly(SyscallEvent evt, SyscallStatistics profile)
    {
        // 通常と異なるコールパターンを検出
        if (IsHighFrequency(evt))
        {
            return new SyscallAnomaly
            {
                AnomalyType = "HighFrequencySyscall",
                Description = $"Excessive calls to {evt.SyscallName}",
                SuspicionLevel = AnomalySuspicion.High,
                Timestamp = evt.Timestamp
            };
        }

        if (IsPrivilegedCall(evt))
        {
            return new SyscallAnomaly
            {
                AnomalyType = "PrivilegedSyscall",
                Description = $"Privileged syscall detected: {evt.SyscallName}",
                SuspicionLevel = AnomalySuspicion.Medium,
                Timestamp = evt.Timestamp
            };
        }

        if (IsUnexpectedTime(evt))
        {
            return new SyscallAnomaly
            {
                AnomalyType = "UnexpectedTiming",
                Description = "Syscall at unexpected time",
                SuspicionLevel = AnomalySuspicion.Low,
                Timestamp = evt.Timestamp
            };
        }

        return null;
    }

    private bool IsHighFrequency(SyscallEvent evt)
    {
        return evt.SyscallNumber >= 0x50; // 高リスク範囲
    }

    private bool IsPrivilegedCall(SyscallEvent evt)
    {
        return evt.SyscallName.Contains("Token") ||
               evt.SyscallName.Contains("Privilege") ||
               evt.SyscallName.Contains("Process");
    }

    private bool IsUnexpectedTime(SyscallEvent evt)
    {
        // 深夜の不審な活動
        return evt.Timestamp.Hour >= 22 || evt.Timestamp.Hour < 6;
    }
}

/// <summary>
/// システムコールイベント
/// </summary>
public class SyscallEvent
{
    public string DriverId { get; set; } = string.Empty;
    public uint SyscallNumber { get; set; }
    public string SyscallName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string CallerContext { get; set; } = string.Empty;
    public int ReturnValue { get; set; }
}

/// <summary>
/// システムコール統計
/// </summary>
public class SyscallStatistics
{
    public string DriverName { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public MonitoringLevel MonitoringLevel { get; set; }
    public long TotalSyscalls { get; set; }
    public DateTime LastSampleTime { get; set; }
    public Dictionary<string, int> SyscallFrequency { get; set; } = new();
}

/// <summary>
/// ダイレクトシステムコール検出結果
/// </summary>
public class DirectSyscallDetectionResult
{
    public string DriverId { get; set; } = string.Empty;
    public bool DirectSyscallsDetected { get; set; }
    public List<string> Indicators { get; set; } = new();
    public DateTime AnalyzedAt { get; set; }
    public ThreatLevel ThreatLevel { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

/// <summary>
/// 異常検出結果
/// </summary>
public class AnomalyDetectionResult
{
    public string DriverId { get; set; } = string.Empty;
    public List<SyscallAnomaly> Anomalies { get; set; } = new();
    public double AnomalyScore { get; set; }  // 0-100
    public DateTime AnalyzedAt { get; set; }
    public ThreatLevel ThreatLevel { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// システムコール異常
/// </summary>
public class SyscallAnomaly
{
    public string AnomalyType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AnomalySuspicion SuspicionLevel { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 異常疑惑度
/// </summary>
public enum AnomalySuspicion
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// 脅威スコアリング結果
/// </summary>
public class ThreatScoringResult
{
    public string DriverId { get; set; } = string.Empty;
    public double OverallThreatScore { get; set; }  // 0-100
    public Dictionary<string, double> Factors { get; set; } = new();
    public DateTime AnalyzedAt { get; set; }
    public ThreatLevel ThreatLevel { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// 脅威レベル
/// </summary>
public enum ThreatLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// モニタリングレベル
/// </summary>
public enum MonitoringLevel
{
    Minimal = 0,
    Standard = 1,
    Comprehensive = 2,
    Aggressive = 3
}
