using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core;

namespace AeroDriver.Core.Security;

/// <summary>
/// コンプライアンスマネージャー
/// GDPR, HIPAA, SOXなどの規制遵守を管理
/// </summary>
public class ComplianceManager
{
    private readonly AuditTrail _auditTrail;
    private readonly ISimpleLogger _logger;
    private readonly ConcurrentDictionary<string, ComplianceConfiguration> _configurations = new();
    private readonly ConcurrentDictionary<string, ComplianceAuditTrail> _auditTrails = new();
    private readonly ConcurrentDictionary<string, DataClassificationResult> _dataClassifications = new();

    public ComplianceManager(AuditTrail auditTrail, ISimpleLogger logger)
    {
        _auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// コンプライアンス設定を構成
    /// </summary>
    public async Task<OperationResult> ConfigureComplianceAsync(ComplianceConfiguration config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Standard.ToString(), nameof(config.Standard));

        try
        {
            _configurations[config.Standard.ToString()] = config;

            await _auditTrail.RecordEventAsync(
                AuditAction.Create,
                $"ComplianceConfig:{config.Standard}",
                AuditResult.Success,
                $"Configured compliance for {config.Standard}",
                new Dictionary<string, string>
                {
                    ["Standard"] = config.Standard.ToString(),
                    ["RetentionDays"] = config.DataRetentionDays.ToString(),
                    ["EncryptionRequired"] = config.RequireEncryption.ToString()
                },
                cancellationToken);

            await _logger.LogInformationAsync($"Compliance configuration updated for {config.Standard}");
            return new OperationResult { Success = true, Message = $"Compliance configured for {config.Standard}" };
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync($"Failed to configure compliance for {config.Standard}", null, ex);
            return new OperationResult { Success = false, Message = $"Configuration failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// データアクセスを監査（GDPR対応）
    /// </summary>
    public async Task<ComplianceAuditResult> AuditDataAccessAsync(string entityId, string entityType, AuditAction action, string userId, string details, CancellationToken cancellationToken = default)
    {
        var auditEntry = new ComplianceAuditTrail
        {
            AuditId = Guid.NewGuid().ToString(),
            Standard = ComplianceStandard.GDPR,
            EntityId = entityId,
            EntityType = entityType,
            Action = action,
            Details = details,
            UserId = userId,
            IpAddress = await GetClientIpAddressAsync(),
            Timestamp = DateTime.UtcNow,
            IsCompliant = true
        };

        _auditTrails[auditEntry.AuditId] = auditEntry;

        // GDPR準拠チェック
        var gdprCompliance = await CheckGdprComplianceAsync(auditEntry, cancellationToken);
        if (!gdprCompliance.IsCompliant)
        {
            auditEntry.IsCompliant = false;
            auditEntry.ComplianceViolations.AddRange(gdprCompliance.Violations);
        }

        await _auditTrail.RecordEventAsync(
            AuditAction.Access,
            $"DataAudit:{entityId}",
            auditEntry.IsCompliant ? AuditResult.Success : AuditResult.Failure,
            details,
            new Dictionary<string, string>
            {
                ["EntityType"] = entityType,
                ["Action"] = action.ToString(),
                ["UserId"] = userId,
                ["IsCompliant"] = auditEntry.IsCompliant.ToString()
            },
            cancellationToken);

        return new ComplianceAuditResult
        {
            IsCompliant = auditEntry.IsCompliant,
            AuditId = auditEntry.AuditId,
            Violations = auditEntry.ComplianceViolations,
            Recommendations = GenerateRecommendations(auditEntry)
        };
    }

    /// <summary>
    /// データ分類を実行（HIPAA対応）
    /// </summary>
    public async Task<DataClassificationResult> ClassifyDataAsync(string dataContent, string dataType, CancellationToken cancellationToken = default)
    {
        var classification = new DataClassificationResult
        {
            ClassificationId = Guid.NewGuid().ToString(),
            DataType = dataType,
            ClassificationLevel = await DetermineClassificationLevelAsync(dataContent, dataType, cancellationToken),
            ContainsPersonalData = await DetectPersonalDataAsync(dataContent, cancellationToken),
            ContainsHealthData = await DetectHealthDataAsync(dataContent, cancellationToken),
            ContainsFinancialData = await DetectFinancialDataAsync(dataContent, cancellationToken),
            ClassifiedAt = DateTime.UtcNow
        };

        _dataClassifications[classification.ClassificationId] = classification;

        // HIPAA準拠チェック
        if (classification.ContainsHealthData && classification.ClassificationLevel < DataClassificationLevel.Restricted)
        {
            classification.RequiresEncryption = true;
            classification.ComplianceViolations.Add("Health data requires restricted classification and encryption");
        }

        return classification;
    }

    /// <summary>
    /// コンプライアンスレポートを生成（SOX対応）
    /// </summary>
    public async Task<ComplianceReport> GenerateComplianceReportAsync(ComplianceStandard standard, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var from = fromDate ?? DateTime.UtcNow.AddDays(-30);
        var to = toDate ?? DateTime.UtcNow;

        var relevantAudits = _auditTrails.Values
            .Where(a => a.Standard == standard && a.Timestamp >= from && a.Timestamp <= to)
            .ToList();

        var report = new ComplianceReport
        {
            Standard = standard,
            GeneratedAt = DateTime.UtcNow,
            ReportPeriod = new ReportPeriod { From = from, To = to },
            TotalAudits = relevantAudits.Count,
            CompliantAudits = relevantAudits.Count(a => a.IsCompliant),
            NonCompliantAudits = relevantAudits.Count(a => !a.IsCompliant),
            ComplianceRate = relevantAudits.Count > 0 ? (double)relevantAudits.Count(a => a.IsCompliant) / relevantAudits.Count * 100 : 100
        };

        // 違反の集計
        report.ViolationSummary = relevantAudits
            .Where(a => !a.IsCompliant)
            .SelectMany(a => a.ComplianceViolations)
            .GroupBy(v => v)
            .ToDictionary(g => g.Key, g => g.Count());

        // 推奨アクション
        report.Recommendations = GenerateComplianceRecommendations(report, relevantAudits);

        await _logger.LogInformationAsync($"Generated {standard} compliance report: {report.ComplianceRate:F1}% compliance rate");
        return report;
    }

    /// <summary>
    /// データ保持ポリシーに基づいて古いデータを削除
    /// </summary>
    public async Task<DataRetentionResult> ApplyDataRetentionPolicyAsync(CancellationToken cancellationToken = default)
    {
        var result = new DataRetentionResult { ProcessedAt = DateTime.UtcNow };

        foreach (var config in _configurations.Values)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-config.DataRetentionDays);

            // 古い監査ログの削除
            var oldAudits = _auditTrails.Values
                .Where(a => a.Timestamp < cutoffDate && a.Standard.ToString() == config.Standard.ToString())
                .ToList();

            foreach (var audit in oldAudits)
            {
                _auditTrails.TryRemove(audit.AuditId, out _);
                result.DeletedAuditCount++;
            }

            // 古いデータ分類の削除
            var oldClassifications = _dataClassifications.Values
                .Where(c => c.ClassifiedAt < cutoffDate)
                .ToList();

            foreach (var classification in oldClassifications)
            {
                _dataClassifications.TryRemove(classification.ClassificationId, out _);
                result.DeletedClassificationCount++;
            }
        }

        await _logger.LogInformationAsync($"Applied data retention policy: deleted {result.DeletedAuditCount} audits, {result.DeletedClassificationCount} classifications");
        return result;
    }

    private async Task<GdprComplianceCheck> CheckGdprComplianceAsync(ComplianceAuditTrail audit, CancellationToken cancellationToken)
    {
        var check = new GdprComplianceCheck { IsCompliant = true };

        // データ主体の権利確認
        if (audit.Action == AuditAction.Delete && !audit.Details.Contains("consent"))
        {
            check.IsCompliant = false;
            check.Violations.Add("Data deletion without proper consent verification");
        }

        // 処理の合法的根拠確認
        if (audit.Action == AuditAction.Access && string.IsNullOrEmpty(audit.Details))
        {
            check.IsCompliant = false;
            check.Violations.Add("Data access without documented legal basis");
        }

        // データ最小化の原則
        if (audit.Action == AuditAction.Export && !await IsDataMinimalAsync(audit.EntityId, cancellationToken))
        {
            check.IsCompliant = false;
            check.Violations.Add("Data export violates data minimization principle");
        }

        return check;
    }

    private async Task<DataClassificationLevel> DetermineClassificationLevelAsync(string dataContent, string dataType, CancellationToken cancellationToken)
    {
        // 機密データ検出ロジック
        if (await DetectHealthDataAsync(dataContent, cancellationToken))
            return DataClassificationLevel.Restricted;

        if (await DetectFinancialDataAsync(dataContent, cancellationToken))
            return DataClassificationLevel.Confidential;

        if (await DetectPersonalDataAsync(dataContent, cancellationToken))
            return DataClassificationLevel.Internal;

        return DataClassificationLevel.Public;
    }

    private async Task<bool> DetectPersonalDataAsync(string content, CancellationToken cancellationToken)
    {
        // 個人情報検出ロジック（メールアドレス、電話番号、IDなど）
        var personalDataPatterns = new[]
        {
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", // Email
            @"\b\d{3}-\d{3}-\d{4}\b", // Phone number (US format)
            @"\b\d{10}\b", // Phone number (digits only)
            @"\b[A-Za-z]{2}\d{6,8}\b" // ID patterns
        };

        return personalDataPatterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(content, pattern));
    }

    private async Task<bool> DetectHealthDataAsync(string content, CancellationToken cancellationToken)
    {
        // 健康データ検出ロジック（医療用語、診断コードなど）
        var healthKeywords = new[] { "diagnosis", "treatment", "medication", "ICD-10", "HIPAA", "PHI" };
        return healthKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> DetectFinancialDataAsync(string content, CancellationToken cancellationToken)
    {
        // 財務データ検出ロジック（クレジットカード、銀行口座など）
        var financialPatterns = new[]
        {
            @"\b\d{4}-\d{4}-\d{4}-\d{4}\b", // Credit card
            @"\b\d{16}\b", // Credit card (digits only)
            @"Account.*\d{8,12}", // Bank account
            @"Routing.*\d{9}" // Routing number
        };

        return financialPatterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(content, pattern));
    }

    private async Task<string> GetClientIpAddressAsync()
    {
        // 実際の実装ではクライアントIPを取得
        return "192.168.1.100"; // モック
    }

    private async Task<bool> IsDataMinimalAsync(string entityId, CancellationToken cancellationToken)
    {
        // データ最小化チェックロジック
        return true; // モック
    }

    private List<string> GenerateRecommendations(ComplianceAuditTrail audit)
    {
        var recommendations = new List<string>();

        if (!audit.IsCompliant)
        {
            switch (audit.Standard)
            {
                case ComplianceStandard.GDPR:
                    recommendations.Add("Review data processing consent procedures");
                    recommendations.Add("Implement data subject access request workflow");
                    recommendations.Add("Conduct data protection impact assessment");
                    break;

                case ComplianceStandard.HIPAA:
                    recommendations.Add("Encrypt all health information at rest and in transit");
                    recommendations.Add("Implement access controls for PHI");
                    recommendations.Add("Regular security awareness training for staff");
                    break;

                case ComplianceStandard.SOX:
                    recommendations.Add("Implement segregation of duties controls");
                    recommendations.Add("Regular audit trail reviews");
                    recommendations.Add("Access logging and monitoring");
                    break;
            }
        }

        return recommendations;
    }

    private List<string> GenerateComplianceRecommendations(ComplianceReport report, List<ComplianceAuditTrail> audits)
    {
        var recommendations = new List<string>();

        if (report.ComplianceRate < 95)
        {
            recommendations.Add("Conduct comprehensive compliance training for all staff");
            recommendations.Add("Implement automated compliance monitoring tools");
            recommendations.Add("Regular compliance audits and assessments");
        }

        if (report.NonCompliantAudits > 0)
        {
            recommendations.Add("Review and update compliance policies and procedures");
            recommendations.Add("Implement corrective actions for identified violations");
        }

        return recommendations;
    }
}

/// <summary>
/// コンプライアンス設定
/// </summary>
public class ComplianceConfiguration
{
    public ComplianceStandard Standard { get; set; }
    public int DataRetentionDays { get; set; } = 2555; // 7年（SOX準拠）
    public bool RequireEncryption { get; set; } = true;
    public bool RequireAccessLogging { get; set; } = true;
    public bool EnableAutomatedAudits { get; set; } = true;
    public Dictionary<string, string> CustomRequirements { get; set; } = new();
}

/// <summary>
/// GDPR準拠チェック結果
/// </summary>
public class GdprComplianceCheck
{
    public bool IsCompliant { get; set; } = true;
    public List<string> Violations { get; set; } = new();
}

/// <summary>
/// コンプライアンス監査結果
/// </summary>
public class ComplianceAuditResult
{
    public bool IsCompliant { get; set; }
    public string AuditId { get; set; } = string.Empty;
    public List<string> Violations { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// データ分類結果
/// </summary>
public class DataClassificationResult
{
    public string ClassificationId { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public DataClassificationLevel ClassificationLevel { get; set; }
    public bool ContainsPersonalData { get; set; }
    public bool ContainsHealthData { get; set; }
    public bool ContainsFinancialData { get; set; }
    public bool RequiresEncryption { get; set; }
    public List<string> ComplianceViolations { get; set; } = new();
    public DateTime ClassifiedAt { get; set; }
}

public enum DataClassificationLevel
{
    Public,
    Internal,
    Confidential,
    Restricted
}

/// <summary>
/// コンプライアンスレポート
/// </summary>
public class ComplianceReport
{
    public ComplianceStandard Standard { get; set; }
    public DateTime GeneratedAt { get; set; }
    public ReportPeriod ReportPeriod { get; set; } = new();
    public int TotalAudits { get; set; }
    public int CompliantAudits { get; set; }
    public int NonCompliantAudits { get; set; }
    public double ComplianceRate { get; set; }
    public Dictionary<string, int> ViolationSummary { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// レポート期間
/// </summary>
public class ReportPeriod
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

/// <summary>
/// データ保持結果
/// </summary>
public class DataRetentionResult
{
    public DateTime ProcessedAt { get; set; }
    public int DeletedAuditCount { get; set; }
    public int DeletedClassificationCount { get; set; }
}
