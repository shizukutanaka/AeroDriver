// 研究ベースの改善: CodeQL静的コード分析統合
// 根拠: Windows Hardware Compatibility Program - CodeQL分析は必須要件
//      WHCP driver certification前の脆弱性検出と修正
// 優先度: P0 (最高) - 認定クリティカル
// 出典: Microsoft Learn CodeQL Documentation, WHCP Driver Security Requirements

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Analysis;

/// <summary>
/// CodeQL静的コード分析エンジン
/// Windows WHCP認定前のドライバーソースコード脆弱性検出
///
/// 機能:
/// 1. CodeQL分析実行 - 自動脆弱性検出
/// 2. SARIF形式レポート - 構造化結果
/// 3. Must-Fix検出 - 必須修正項目の特定
/// 4. WHCP合致性検証 - 認定要件チェック
/// </summary>
public class CodeQLAnalyzer
{
    private readonly ILogger _logger;
    private readonly string _codeqlPath;
    private readonly string _queriesDatabasePath;

    public CodeQLAnalyzer(ILogger logger, string codeqlPath = "", string queriesDatabasePath = "")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _codeqlPath = codeqlPath ?? "codeql";
        _queriesDatabasePath = queriesDatabasePath ?? GetDefaultQueriesPath();

        _logger.LogInformation("CodeQLAnalyzer initialized");
    }

    /// <summary>
    /// ドライバーソースコードをCodeQL分析
    /// </summary>
    public async Task<CodeQLAnalysisResult> AnalyzeDriverSourceAsync(
        string driverName,
        string sourceCodePath,
        string driverVersion,
        CancellationToken ct = default)
    {
        _logger.LogInformation($"Starting CodeQL analysis for {driverName}");

        var result = new CodeQLAnalysisResult
        {
            DriverName = driverName,
            DriverVersion = driverVersion,
            SourceCodePath = sourceCodePath,
            AnalyzedAt = DateTime.UtcNow
        };

        try
        {
            // CodeQL分析を実行
            var analysisOutput = await ExecuteCodeQLAnalysisAsync(sourceCodePath, ct);

            // SARIF形式で結果をパース
            result.SarifReport = ParseSarifOutput(analysisOutput);

            // 脆弱性を分類
            ClassifyVulnerabilities(result);

            // WHCP合致性を検証
            ValidateWHCPCompliance(result);

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"CodeQL analysis failed: {ex.Message}");
            result.Success = false;
            result.Exception = ex;
        }

        return result;
    }

    /// <summary>
    /// CodeQL分析を実行
    /// </summary>
    private async Task<string> ExecuteCodeQLAnalysisAsync(
        string sourceCodePath,
        CancellationToken ct)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = _codeqlPath,
            Arguments = BuildCodeQLArguments(sourceCodePath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start CodeQL process");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(10)); // 10分タイムアウト

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                throw new InvalidOperationException($"CodeQL analysis failed: {error}");
            }

            return await outputTask;
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            throw new TimeoutException("CodeQL analysis timed out");
        }
    }

    /// <summary>
    /// CodeQL引数を構築
    /// </summary>
    private string BuildCodeQLArguments(string sourceCodePath)
    {
        return $"database create " +
               $"--language=cpp " +
               $"--source-root=\"{sourceCodePath}\" " +
               $"--overwrite " +
               $"codeql_db && " +
               $"codeql query run " +
               $"--database=codeql_db " +
               $"--search-path=\"{_queriesDatabasePath}\" " +
               $"-- " +
               $"windows/driver/security/queries.ql " +
               $"--format=sarif-latest";
    }

    /// <summary>
    /// SARIF形式の出力をパース
    /// </summary>
    private SarifReport ParseSarifOutput(string sarifJson)
    {
        var report = new SarifReport
        {
            Violations = new List<CodeQLViolation>()
        };

        try
        {
            using var doc = JsonDocument.Parse(sarifJson);
            var root = doc.RootElement;

            // runs -> results を取得
            if (root.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
            {
                foreach (var run in runs.EnumerateArray())
                {
                    if (run.TryGetProperty("results", out var results) &&
                        results.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var result in results.EnumerateArray())
                        {
                            var violation = ParseViolation(result);
                            if (violation != null)
                            {
                                report.Violations.Add(violation);
                            }
                        }
                    }
                }
            }

            report.TotalViolations = report.Violations.Count;
            report.MustFixCount = report.Violations.Count(v => v.IsMustFix);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to parse SARIF: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// 違反をパース
    /// </summary>
    private CodeQLViolation? ParseViolation(JsonElement result)
    {
        if (!result.TryGetProperty("ruleId", out var ruleId))
            return null;

        var violation = new CodeQLViolation
        {
            RuleId = ruleId.GetString() ?? "Unknown",
            Message = ExtractMessage(result),
            Location = ExtractLocation(result),
            Severity = ExtractSeverity(result)
        };

        // Must-Fix判定
        violation.IsMustFix = IsMustFixViolation(violation.RuleId);

        return violation;
    }

    /// <summary>
    /// メッセージを抽出
    /// </summary>
    private string ExtractMessage(JsonElement result)
    {
        if (result.TryGetProperty("message", out var message) &&
            message.TryGetProperty("text", out var text))
        {
            return text.GetString() ?? "Unknown violation";
        }

        return "Unknown violation";
    }

    /// <summary>
    /// 場所を抽出
    /// </summary>
    private string ExtractLocation(JsonElement result)
    {
        if (result.TryGetProperty("locations", out var locations) &&
            locations.ValueKind == JsonValueKind.Array)
        {
            foreach (var location in locations.EnumerateArray())
            {
                if (location.TryGetProperty("physicalLocation", out var physical) &&
                    physical.TryGetProperty("artifactLocation", out var artifact) &&
                    artifact.TryGetProperty("uri", out var uri))
                {
                    return uri.GetString() ?? "Unknown location";
                }
            }
        }

        return "Unknown location";
    }

    /// <summary>
    /// 重大度を抽出
    /// </summary>
    private ViolationSeverity ExtractSeverity(JsonElement result)
    {
        if (result.TryGetProperty("level", out var level))
        {
            return level.GetString() switch
            {
                "error" => ViolationSeverity.Error,
                "warning" => ViolationSeverity.Warning,
                "note" => ViolationSeverity.Note,
                _ => ViolationSeverity.Note
            };
        }

        return ViolationSeverity.Warning;
    }

    /// <summary>
    /// 脆弱性を分類
    /// </summary>
    private void ClassifyVulnerabilities(CodeQLAnalysisResult result)
    {
        foreach (var violation in result.SarifReport.Violations)
        {
            violation.Category = ClassifyViolation(violation.RuleId);

            _logger.LogWarning(
                $"CodeQL violation found: {violation.RuleId} " +
                $"({violation.Category}) " +
                $"at {violation.Location}");
        }
    }

    /// <summary>
    /// WHCP合致性を検証
    /// </summary>
    private void ValidateWHCPCompliance(CodeQLAnalysisResult result)
    {
        // Must-Fix違反が存在する場合はWHCP不合致
        if (result.SarifReport.MustFixCount > 0)
        {
            result.WHCPCompliant = false;
            result.BlockingReason = $"{result.SarifReport.MustFixCount} must-fix violations found";
            result.RecommendedAction = "Fix all must-fix violations before WHCP submission";
            _logger.LogError($"WHCP non-compliant: {result.BlockingReason}");
        }
        else
        {
            result.WHCPCompliant = true;
            result.RecommendedAction = "Driver meets WHCP CodeQL requirements";
        }

        // 全体的な脆弱性スコアを計算
        result.VulnerabilityScore = CalculateVulnerabilityScore(result.SarifReport);
    }

    /// <summary>
    /// 脆弱性スコアを計算
    /// </summary>
    private double CalculateVulnerabilityScore(SarifReport report)
    {
        double score = 0;

        foreach (var violation in report.Violations)
        {
            score += violation.Severity switch
            {
                ViolationSeverity.Error => 10,
                ViolationSeverity.Warning => 5,
                ViolationSeverity.Note => 1,
                _ => 1
            };

            if (violation.IsMustFix)
            {
                score *= 2; // Must-Fix違反は2倍のペナルティ
            }
        }

        return Math.Min(score, 100);
    }

    /// <summary>
    /// 違反を分類
    /// </summary>
    private ViolationCategory ClassifyViolation(string ruleId)
    {
        return ruleId switch
        {
            // メモリ安全性
            var id when id.Contains("buffer") || id.Contains("overflow") => ViolationCategory.MemorySafety,
            var id when id.Contains("null") || id.Contains("pointer") => ViolationCategory.MemorySafety,

            // 競合条件
            var id when id.Contains("race") || id.Contains("concurrency") => ViolationCategory.RaceCondition,

            // リソースリーク
            var id when id.Contains("leak") || id.Contains("resource") => ViolationCategory.ResourceLeak,

            // 入力検証
            var id when id.Contains("validation") || id.Contains("injection") => ViolationCategory.InputValidation,

            // 権限管理
            var id when id.Contains("privilege") || id.Contains("permissions") => ViolationCategory.PrivilegeManagement,

            // デフォルト
            _ => ViolationCategory.Other
        };
    }

    /// <summary>
    /// Must-Fix判定
    /// </summary>
    private bool IsMustFixViolation(string ruleId)
    {
        // Must-Fix違反のパターン
        var mustFixPatterns = new[]
        {
            "windows/driver/memory",      // メモリ安全性
            "windows/driver/overflow",    // バッファオーバーフロー
            "windows/driver/use-after-free", // Use-After-Free
            "windows/driver/null-pointer",   // NULL ポインタ
            "windows/driver/race-condition", // 競合条件
            "windows/driver/privilege"       // 権限昇格
        };

        return mustFixPatterns.Any(pattern => ruleId.Contains(pattern));
    }

    /// <summary>
    /// デフォルトクエリパスを取得
    /// </summary>
    private static string GetDefaultQueriesPath()
    {
        var codeqlHome = Environment.GetEnvironmentVariable("CODEQL_HOME");
        return codeqlHome != null
            ? Path.Combine(codeqlHome, "ql", "cpp", "ql", "src")
            : "/opt/codeql/ql/cpp/ql/src";
    }
}

/// <summary>
/// CodeQL分析結果
/// </summary>
public class CodeQLAnalysisResult
{
    public string DriverName { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public string SourceCodePath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime AnalyzedAt { get; set; }
    public SarifReport SarifReport { get; set; } = new();
    public bool WHCPCompliant { get; set; }
    public string BlockingReason { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public double VulnerabilityScore { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// SARIF形式レポート
/// </summary>
public class SarifReport
{
    public List<CodeQLViolation> Violations { get; set; } = new();
    public int TotalViolations { get; set; }
    public int MustFixCount { get; set; }
}

/// <summary>
/// CodeQL違反
/// </summary>
public class CodeQLViolation
{
    public string RuleId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public ViolationSeverity Severity { get; set; }
    public ViolationCategory Category { get; set; }
    public bool IsMustFix { get; set; }
}

/// <summary>
/// 違反の重大度
/// </summary>
public enum ViolationSeverity
{
    Note = 0,
    Warning = 1,
    Error = 2
}

/// <summary>
/// 違反のカテゴリ
/// </summary>
public enum ViolationCategory
{
    MemorySafety = 0,
    RaceCondition = 1,
    ResourceLeak = 2,
    InputValidation = 3,
    PrivilegeManagement = 4,
    Other = 5
}
