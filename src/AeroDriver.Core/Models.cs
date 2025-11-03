// 実用的で必要なモデル定義のみ（軽量化・高速化）
namespace AeroDriver.Core;

// 基本ドライバー情報（リポジトリ／サービス間で共有）
public class DriverInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public string Type { get; set; } = string.Empty;
    public string DeviceClass { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string DriverPath { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty; // 互換性維持用
    public bool IsSigned { get; set; }
    public bool IsEssential { get; set; }
    public int Priority { get; set; }
    public long FileSize { get; set; }
    public DateTime InstallDate { get; set; }
    public DateTime DriverDate { get; set; }
    public DateTime? LastUpdated { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

// ドライバー更新結果
public class DriverUpdateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int UpdatedDrivers { get; set; }
    public int FailedDrivers { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

public class DriverComplianceReport
{
    public DateTime GeneratedAt { get; set; }
    public int TotalDrivers { get; set; }
    public int OutdatedDrivers { get; set; }
    public int UnsignedDrivers { get; set; }
    public int ProblemDrivers { get; set; }
    public int StaleDrivers { get; set; }
    public string OverallStatus { get; set; } = string.Empty;
    public List<DriverComplianceIssue> Issues { get; set; } = new();
    public Dictionary<string, int> SummaryBySeverity { get; set; } = new();
}

public class DriverComplianceIssue
{
    public string DriverId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public DriverComplianceIssueType IssueType { get; set; } = DriverComplianceIssueType.Unknown;
    public ComplianceSeverity Severity { get; set; } = ComplianceSeverity.Low;
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public enum DriverComplianceIssueType
{
    Unknown,
    Outdated,
    Unsigned,
    FailedState,
    LongTimeSinceUpdate
}

public enum ComplianceSeverity
{
    Low,
    Medium,
    High,
    Critical
}

// 操作結果
public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}

// バッチ操作結果
public class BatchOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public int ProcessedCount { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

// パフォーマンスレポートは DriverManager.cs で定義済み

// ドライバースキャン結果
public class DriverScanResult
{
    public int ScannedDrivers { get; set; }
    public int AvailableUpdates { get; set; }
    public DateTime ScanDate { get; set; }
}

// ドライバーバックアップ情報
public class DriverBackupInfo
{
    public string BackupId { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime BackupDate { get; set; }
    public string BackupPath { get; set; } = string.Empty;
    public long BackupSize { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Notes { get; set; } = string.Empty;
}

// ドライバー更新情報
public class DriverUpdateInfo
{
    public string DriverId { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string NewVersion { get; set; } = string.Empty;
    public DateTime ReleaseDate { get; set; }
    public long UpdateSize { get; set; }
    public string UpdateUrl { get; set; } = string.Empty;
    public string Priority { get; set; } = "Optional";
    public List<string> Improvements { get; set; } = new();
    public List<string> KnownIssues { get; set; } = new();
    public bool RequiresRestart { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

// システム統計
public class SystemStats
{
    public int TotalDrivers { get; set; }
    public int ActiveDrivers { get; set; }
    public int ProblemDrivers { get; set; }
    public int OutdatedDrivers { get; set; }
    public int UnsignedDrivers { get; set; }
    public int CriticalDrivers { get; set; }
    public int HealthPercentage => TotalDrivers > 0 ? (100 * (TotalDrivers - ProblemDrivers) / TotalDrivers) : 100;
    public int HealthScore => CalculateHealthScore();
    public DateTime LastScanTime { get; set; }

    private int CalculateHealthScore()
    {
        if (TotalDrivers == 0) return 100;

        int score = 100;

        // Deduct points for problems (most critical)
        score -= Math.Min(ProblemDrivers * 15, 60); // Max 60 points deduction

        // Deduct points for outdated drivers
        score -= Math.Min(OutdatedDrivers * 5, 25); // Max 25 points deduction

        // Deduct points for unsigned drivers (security risk)
        score -= Math.Min(UnsignedDrivers * 3, 15); // Max 15 points deduction

        // Bonus for having mostly up-to-date drivers
        if (OutdatedDrivers == 0 && ProblemDrivers == 0)
            score = Math.Min(score + 5, 100);

        return Math.Max(score, 0);
    }

    public string HealthGrade => HealthScore switch
    {
        >= 90 => "Excellent",
        >= 80 => "Good",
        >= 70 => "Fair",
        >= 60 => "Poor",
        _ => "Critical"
    };
}

// システム健全性
public class SystemHealth
{
    public double CpuUsage { get; set; }
    public long MemoryUsageMB { get; set; }
    public bool IsHealthy { get; set; }
    public DateTime LastCheckTime { get; set; }
}

// ヘルススコア (DriverManagerで使用)
public class HealthScore
{
    public int TotalDrivers { get; set; }
    public int ProblemDrivers { get; set; }
    public int Score { get; set; }
    public string HealthGrade { get; set; } = string.Empty;
}

// 最適化結果 (DriverManagerで使用)
public class OptimizeResult
{
    public bool Success => FailedCount == 0 && ErrorMessages.Count == 0;
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
}

// 修復結果 (DriverManagerで使用)
public class RepairResult
{
    public bool Success { get; set; }
    public int TotalItems { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
}

// パフォーマンスレポート (DriverManagerで使用)
public class PerformanceReport
{
    public DateTime ReportTime { get; set; }
    public int Score { get; set; }
    public string Grade { get; set; } = string.Empty;
    public PerformanceMetrics Metrics { get; set; } = new();
    public List<string> Observations { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public string Error { get; set; } = string.Empty;
}

// パフォーマンスメトリクス
public class PerformanceMetrics
{
    public double CpuUsagePercent { get; set; }
    public double ProcessMemoryMB { get; set; }
    public double MemoryUsagePercent { get; set; }
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public long AvailableMemoryMB { get; set; }
    public long TotalMemoryMB { get; set; }
    public double? DiskUsagePercent { get; set; }
    public double? NetworkThroughputMbps { get; set; }
    public string Note { get; set; } = "Standard performance metrics";
    public bool IsDegraded => CpuUsagePercent > 80 || AvailableMemoryMB < 500;
}

// セキュリティレポート
public class SecurityReport
{
    public int SecurityScore { get; set; }
    public int TotalIssues { get; set; }
    public List<SecurityIssue> CriticalIssues { get; set; } = new();
    public List<SecurityIssue> WarningIssues { get; set; } = new();
    public List<SecurityIssue> InfoIssues { get; set; } = new();
    public DateTime AuditTime { get; set; }
    public string Error { get; set; } = string.Empty;
    public Dictionary<string, int> IssuesByCategory { get; set; } = new();
    public List<SecurityMetrics> Metrics { get; set; } = new();
    public SecurityComplianceStatus ComplianceStatus { get; set; } = SecurityComplianceStatus.Unknown;
}

public class SecurityIssue
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public SecuritySeverity Severity { get; set; } = SecuritySeverity.Info;
    public string CveId { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

public class SecurityMetrics
{
    public string MetricName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum SecuritySeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

// 脆弱性情報
public class VulnerabilityInfo
{
    public string CveId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "Unknown";
    public double CvssScore { get; set; }
    public List<string> AffectedProducts { get; set; } = new();
    public List<string> AffectedVersions { get; set; } = new();
    public string Recommendation { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public VulnerabilityStatus Status { get; set; } = VulnerabilityStatus.Active;
    public Dictionary<string, string> References { get; set; } = new();
}

public enum VulnerabilityStatus
{
    Active,
    Fixed,
    Deprecated,
    Disputed
}

public enum SecurityComplianceStatus
{
    Unknown,
    Compliant,
    NonCompliant,
    PartiallyCompliant
}

public enum MonitoringType
{
    Security,
    Performance,
    Health,
    Compliance
}

public enum MonitoringStatus
{
    Normal,
    Warning,
    Critical,
    Unknown
}

public class PerformanceMonitoringSnapshot
{
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double? CpuUsagePercent { get; set; }
    public double? MemoryUsagePercent { get; set; }
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public double? DiskUsagePercent { get; set; }
    public double? NetworkThroughputMbps { get; set; }
    public bool NeedsOptimization { get; set; }
    public List<MonitoringAlert> Alerts { get; set; } = new();
}

public class MonitoringAlert
{
    public string AlertId { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsAcknowledged { get; set; }
    public string AcknowledgedBy { get; set; } = string.Empty;
    public string MonitorName { get; set; } = string.Empty;
    public double? MetricValue { get; set; }
    public double? Threshold { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

public enum AlertSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

// ファイル整合性情報
public class FileIntegrityInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string ExpectedHash { get; set; } = string.Empty;
    public string CurrentHash { get; set; } = string.Empty;
    public DateTime LastChecked { get; set; }
    public FileIntegrityStatus Status { get; set; }
    public string FileSize { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public enum FileIntegrityStatus
{
    Valid,
    Modified,
    Missing,
    Unknown
}

// ネットワーク監視情報
public class NetworkMonitoringInfo
{
    public string MonitorId { get; set; } = string.Empty;
    public string InterfaceName { get; set; } = string.Empty;
    public NetworkMonitoringType Type { get; set; }
    public DateTime LastCheckTime { get; set; }
    public MonitoringStatus Status { get; set; }
    public NetworkMetrics Metrics { get; set; } = new();
    public List<NetworkAlert> Alerts { get; set; } = new();
}

public enum NetworkMonitoringType
{
    Traffic,
    Connections,
    Ports,
    DNS
}

public class NetworkMetrics
{
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public int ActiveConnections { get; set; }
    public int ListeningPorts { get; set; }
    public double AverageLatencyMs { get; set; }
    public int PacketLossPercent { get; set; }
}

public class NetworkAlert
{
    public string AlertId { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string SourceIP { get; set; } = string.Empty;
    public string DestinationIP { get; set; } = string.Empty;
    public int Port { get; set; }
}

// 機械学習ベースの予測最適化情報
public class PredictiveOptimizationInfo
{
    public string ModelId { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public DateTime TrainingStarted { get; set; }
    public DateTime LastTraining { get; set; }
    public double Accuracy { get; set; }
    public int TrainingDataPoints { get; set; }
    public List<PredictionResult> Predictions { get; set; } = new();
    public OptimizationAction RecommendedAction { get; set; }
}

public class PredictionResult
{
    public string MetricName { get; set; } = string.Empty;
    public double PredictedValue { get; set; }
    public double Confidence { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum OptimizationAction
{
    None,
    DefragmentMemory,
    ClearCache,
    RestartService,
    UpdateDrivers
}

// バッチ進捗
public class BatchProgress
{
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
    public double ProgressPercent => TotalCount > 0 ? (double)ProcessedCount / TotalCount * 100 : 0;
}

// ドライバーバックアップ
public class DriverBackup
{
    public string DriverId { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long BackupSize { get; set; }
}

// 更新優先度
public enum UpdatePriority
{
    Critical,
    High,
    Medium,
    Low
}

// 問題タイプ
public enum IssueType
{
    Compatibility,
    Performance,
    Security,
    Stability
}

// 問題深刻度
public enum IssueSeverity
{
    Critical,
    High,
    Medium,
    Low
}

// Note: AutomationResult, AutomationHistoryEntry, BatchOptions, BatchResult,
// ScheduleOptions, AutomationStatus, AutomationProgress are defined in AutomationEngine.cs

// リアルタイム監視情報
public class RealTimeMonitoringInfo
{
    public string MonitorId { get; set; } = string.Empty;
    public MonitoringType Type { get; set; }
    public DateTime StartTime { get; set; }
    public MonitoringStatus Status { get; set; }
    public List<MonitoringAlert> Alerts { get; set; } = new();
    public Dictionary<string, object> Metrics { get; set; } = new();
    public int CheckIntervalSeconds { get; set; } = 60;
}

// セキュリティ監視設定
public class SecurityMonitoringConfig
{
    public bool EnableVulnerabilityMonitoring { get; set; } = true;
    public bool EnableSignatureValidation { get; set; } = true;
    public bool EnableBehaviorAnalysis { get; set; } = true;
    public int VulnerabilityCheckIntervalMinutes { get; set; } = 60;
    public int SignatureValidationIntervalMinutes { get; set; } = 30;
    public double AnomalyDetectionThreshold { get; set; } = 0.8;
    public List<string> ExcludedDrivers { get; set; } = new();
}

// パフォーマンス監視設定
public class PerformanceMonitoringConfig
{
    public bool EnableCpuMonitoring { get; set; } = true;
    public bool EnableMemoryMonitoring { get; set; } = true;
    public bool EnableDiskMonitoring { get; set; } = true;
// デプロイメントリング（段階的展開）機能
public class DeploymentRing
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int RingLevel { get; set; } // 1=最も早いリング、数字が大きいほど遅い
    public List<string> DeviceFilters { get; set; } = new(); // デバイスフィルタ条件
    public DeploymentRingPolicy Policy { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class DeploymentRingPolicy
{
    public ApprovalMethod DriverApprovalMethod { get; set; } = ApprovalMethod.Manual;
    public int DeferralDays { get; set; } = 0; // 自動承認の場合の延期日数
    public DateTime? AvailableAfter { get; set; } // 手動承認の場合の利用可能開始日時
    public bool RequireRestartApproval { get; set; } = false; // 再起動が必要な更新の承認要否
    public int MaxConcurrentDevices { get; set; } = 10; // 同時適用デバイス数の制限
    public List<string> ExcludedDriverCategories { get; set; } = new(); // 除外するドライバーカテゴリ
    public Dictionary<string, string> CustomFilters { get; set; } = new();
}

public enum ApprovalMethod
{
    Manual,     // 手動承認
    Automatic,  // 自動承認
    Scheduled   // スケジュール承認
}

public class DeploymentRingAssignment
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string RingId { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string AssignedBy { get; set; } = string.Empty;
    public AssignmentReason Reason { get; set; } = AssignmentReason.Manual;
}

public enum AssignmentReason
{
    Manual,
    Automatic,
    PolicyBased,
    RiskBased
}

// EV（電気自動車）サポート
public class ElectricVehicleInfo
{
    public bool IsElectricVehicle { get; set; }
    public string BatteryHealth { get; set; } = string.Empty;
    public double BatteryLevel { get; set; }
    public double RangeRemaining { get; set; }
    public string ChargingStatus { get; set; } = string.Empty;
    public DateTime? LastChargeTime { get; set; }
    public int ChargeCycles { get; set; }
    public Dictionary<string, double> BatteryMetrics { get; set; } = new();
}

// コンプライアンス監査証跡
public class ComplianceAuditTrail
{
    public string AuditId { get; set; } = string.Empty;
    public ComplianceStandard Standard { get; set; } = ComplianceStandard.GDPR;
    public string EntityId { get; set; } = string.Empty; // 監査対象のエンティティID
    public string EntityType { get; set; } = string.Empty; // Driver, User, System など
    public AuditAction Action { get; set; } = AuditAction.Access;
    public string Details { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
    public bool IsCompliant { get; set; } = true;
    public List<string> ComplianceViolations { get; set; } = new();
}

public enum ComplianceStandard
{
    GDPR,
    HIPAA,
    SOX,
    PCI_DSS,
    ISO_27001,
    NIST
}

public enum AuditAction
{
    Access,
    Modify,
    Delete,
    Create,
    Approve,
    Deny,
    Export
}

// AI予測分析
public class PredictiveAnalysis
{
    public string AnalysisId { get; set; } = string.Empty;
    public string TargetEntity { get; set; } = string.Empty; // Driver, System, Performance など
    public PredictionType PredictionType { get; set; } = PredictionType.DriverFailure;
    public double ConfidenceScore { get; set; } // 0.0 - 1.0
    public DateTime PredictedDate { get; set; }
    public string RiskLevel { get; set; } = "Medium";
    public string Recommendation { get; set; } = string.Empty;
    public Dictionary<string, double> Factors { get; set; } = new(); // 影響要因とその重み
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntil { get; set; }
}

// Windows 11 24H2対応機能
public class Windows11VersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string BuildNumber { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public bool Is24H2OrLater { get; set; }
    public bool SupportsWdkNuGet { get; set; }
    public bool SupportsArm64Drivers { get; set; }
    public bool SupportsAcxAudio { get; set; }
    public bool SupportsWddm32 { get; set; }
    public bool SupportsDirtyBitTracking { get; set; }
    public bool SupportsGpuFenceSync { get; set; }
    public bool SupportsUserModeWorkSubmission { get; set; }
    public Dictionary<string, bool> FeatureSupport { get; set; } = new();
}

public class Arm64DriverSupport
{
    public bool IsArm64System { get; set; }
    public bool WdkArm64Support { get; set; }
    public bool NativeArm64Development { get; set; }
    public bool CrossPlatformDebugging { get; set; }
    public List<string> SupportedArm64Features { get; set; } = new();
    public Dictionary<string, string> Arm64Capabilities { get; set; } = new();
}

public class AcxAudioSupport
{
    public bool IsAcxSupported { get; set; }
    public bool SupportsMultiCircuitComposition { get; set; }
    public bool SupportsCrossDriverCommunication { get; set; }
    public bool SupportsAudioDataFormats { get; set; }
    public bool SupportsPowerManagement { get; set; }
    public bool SupportsDriverLifetimeManagement { get; set; }
    public List<string> SupportedAudioFeatures { get; set; } = new();
}

public class Wddm32GraphicsSupport
{
    public bool IsWddm32Supported { get; set; }
    public bool SupportsDirtyBitTracking { get; set; }
    public bool SupportsLiveMigration { get; set; }
    public bool SupportsGpuFenceSync { get; set; }
    public bool SupportsUserModeWorkSubmission { get; set; }
    public bool SupportsAv1Encoding { get; set; }
    public bool SupportsFeatureQuerying { get; set; }
    public Dictionary<string, object> GraphicsCapabilities { get; set; } = new();
}

public class AiAgentConfiguration
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public AiAgentType AgentType { get; set; } = AiAgentType.PredictiveMaintenance;
    public bool IsEnabled { get; set; } = true;
    public Dictionary<string, double> AgentParameters { get; set; } = new();
    public List<string> TriggerConditions { get; set; } = new();
    public List<string> ActionRules { get; set; } = new();
    public AiAgentStatus Status { get; set; } = AiAgentStatus.Idle;
    public DateTime LastExecuted { get; set; }
    public DateTime NextScheduledExecution { get; set; }
}

public enum AiAgentType
{
    PredictiveMaintenance,
    SecurityMonitoring,
    PerformanceOptimization,
    ComplianceAuditing,
    InventoryManagement,
    RemoteVehicleControl,
    ClaimsProcessing,
    ServiceCampaigns
}

public enum AiAgentStatus
{
    Idle,
    Running,
    Learning,
    Error,
    Disabled
}

public class XrDeviceConfiguration
{
    public string DeviceId { get; set; } = string.Empty;
    public XrDeviceType DeviceType { get; set; } = XrDeviceType.VRHeadset;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool RemoteViewEnabled { get; set; }
    public bool DifferentialUpdatesEnabled { get; set; }
    public int MaxFileSizeGb { get; set; } = 10;
    public List<string> ReleaseChannels { get; set; } = new();
    public List<string> SupportedApps { get; set; } = new();
    public Dictionary<string, string> DeviceSettings { get; set; } = new();
}

public enum XrDeviceType
{
    VRHeadset,
    ARGlasses,
    MixedRealityHeadset,
    StandaloneVR,
    PCVR
}

public class QuantumSecurityConfiguration
{
    public bool QuantumResistantEncryption { get; set; }
    public bool QuantumKeyDistribution { get; set; }
    public bool QuantumRandomNumberGeneration { get; set; }
    public bool PostQuantumCryptography { get; set; }
    public string QuantumThreatModel { get; set; } = string.Empty;
    public Dictionary<string, double> QuantumRiskFactors { get; set; } = new();
    public List<string> QuantumSecurityProtocols { get; set; } = new();
}

public class EdgeComputingConfiguration
{
    public bool EdgeDeploymentEnabled { get; set; }
    public string EdgeLocation { get; set; } = string.Empty;
    public double ComputeThreshold { get; set; } = 0.8;
    public double BandwidthThreshold { get; set; } = 100.0;
    public List<string> EdgeServices { get; set; } = new();
    public Dictionary<string, double> EdgePerformanceMetrics { get; set; } = new();
    public EdgeSynchronizationPolicy SyncPolicy { get; set; } = new();
}

// Agentic AI Systems (2025対応)
public class AgenticAiAgent
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public AgenticAiType AgentType { get; set; } = AgenticAiType.AutonomousMaintenance;
    public bool IsEnabled { get; set; } = true;
    public AgenticAiStatus Status { get; set; } = AgenticAiStatus.Initializing;

    // 環境認識機能
    public List<string> PerceptionCapabilities { get; set; } = new();
    public Dictionary<string, double> EnvironmentSensors { get; set; } = new();

    // 意思決定機能
    public DecisionMakingModel DecisionModel { get; set; } = new();
    public List<string> ActionCapabilities { get; set; } = new();

    // 継続学習機能
    public ContinuousLearningConfig LearningConfig { get; set; } = new();
    public Dictionary<string, object> KnowledgeBase { get; set; } = new();

    // 自律性機能
    public AutonomyLevel AutonomyLevel { get; set; } = AutonomyLevel.SemiAutonomous;
    public List<string> AutonomousActions { get; set; } = new();
    public List<string> SupervisionRequirements { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public enum AgenticAiType
{
    AutonomousMaintenance,
    CognitiveSecurity,
    PredictiveOperations,
    SelfOptimizingPerformance,
    AdaptiveCompliance,
    IntelligentInventory,
    SmartResourceAllocation,
    AutonomousTroubleshooting
}

public enum AgenticAiStatus
{
    Initializing,
    Learning,
    Operational,
    Autonomous,
    Supervised,
    Error,
    Maintenance
}

public class DecisionMakingModel
{
    public string ModelType { get; set; } = "ReinforcementLearning";
    public double ConfidenceThreshold { get; set; } = 0.8;
    public int MaxDecisionTimeMs { get; set; } = 1000;
    public List<string> DecisionFactors { get; set; } = new();
    public Dictionary<string, double> DecisionWeights { get; set; } = new();
}

public class ContinuousLearningConfig
{
    public bool RealTimeLearning { get; set; } = true;
    public int LearningIntervalMinutes { get; set; } = 15;
    public double LearningRate { get; set; } = 0.001;
    public int KnowledgeRetentionDays { get; set; } = 365;
    public List<string> LearningSources { get; set; } = new();
    public bool TransferLearningEnabled { get; set; } = true;
}

public enum AutonomyLevel
{
    Manual,
    Assisted,
    SemiAutonomous,
    FullyAutonomous,
    SelfImproving
}

public class QuantumHardwareIntegration
{
    public bool IsQuantumReady { get; set; }
    public string QuantumProcessorType { get; set; } = string.Empty;
    public int QubitCount { get; set; }
    public double QuantumVolume { get; set; }
    public List<string> QuantumAlgorithms { get; set; } = new();
    public Dictionary<string, double> QuantumPerformanceMetrics { get; set; } = new();
    public bool ErrorCorrectionEnabled { get; set; }
    public bool HybridQuantumClassical { get; set; }
}

public class IndustrialMetaverseIntegration
{
    public bool IsMetaverseEnabled { get; set; }
    public string MetaversePlatform { get; set; } = string.Empty;
    public DigitalTwinConfig DigitalTwin { get; set; } = new();
    public SpatialComputingConfig SpatialConfig { get; set; } = new();
    public List<string> ImmersiveApplications { get; set; } = new();
    public double ProductivityGainPercent { get; set; }
}

public class DigitalTwinConfig
{
    public bool RealTimeSync { get; set; } = true;
    public int UpdateFrequencySeconds { get; set; } = 1;
    public List<string> SimulatedComponents { get; set; } = new();
    public Dictionary<string, object> SimulationParameters { get; set; } = new();
    public bool PhysicsEngineEnabled { get; set; } = true;
}

public class SpatialComputingConfig
{
    public bool SpatialMapping { get; set; } = true;
    public bool GestureRecognition { get; set; } = true;
    public bool VoiceInteraction { get; set; } = true;
    public int TrackingAccuracyMm { get; set; } = 1;
    public List<string> SpatialFeatures { get; set; } = new();
}

public class SixGNetworkIntegration
{
    public bool IsSixGReady { get; set; }
    public string NetworkGeneration { get; set; } = string.Empty;
    public double MaxDataRateTbps { get; set; } = 1.0;
    public double LatencyMs { get; set; } = 0.1;
    public bool AiNativeOrchestration { get; set; }
    public bool THzCommunication { get; set; }
    public bool HolographicCommunication { get; set; }
    public List<string> SixGFeatures { get; set; } = new();
}

public class NeuromorphicHardwareSupport
{
    public bool IsNeuromorphicEnabled { get; set; }
    public string HardwareType { get; set; } = string.Empty;
    public int NeuronCount { get; set; }
    public int SynapseCount { get; set; }
    public double PowerEfficiency { get; set; }
    public bool SpikeBasedProcessing { get; set; }
    public List<string> NeuromorphicCapabilities { get; set; } = new();
}

public class CleanEnergyIntegration
{
    public bool IsCleanEnergyEnabled { get; set; }
    public EnergySource PrimarySource { get; set; } = EnergySource.Solar;
    public double CarbonFootprint { get; set; }
    public double EnergyEfficiency { get; set; }
    public bool AutoOptimization { get; set; } = true;
    public List<string> EnergyMetrics { get; set; } = new();
}

public enum EnergySource
{
    Solar,
    Wind,
    Hydro,
    Geothermal,
    Nuclear,
    Grid,
    Battery
}

public class CyberResilientSecurity
{
    public bool IsResilientSecurityEnabled { get; set; }
    public int RecoveryTimeMinutes { get; set; } = 5;
    public double AvailabilityPercent { get; set; } = 99.999;
    public bool SelfHealingEnabled { get; set; } = true;
    public List<string> ResilienceStrategies { get; set; } = new();
    public Dictionary<string, double> SecurityMetrics { get; set; } = new();
}

// 2026-2030年技術統合 (Post-Quantum Era)
public class PostQuantumComputingIntegration
{
    public bool IsPostQuantumReady { get; set; }
    public string QuantumProcessorGeneration { get; set; } = string.Empty;
    public int LogicalQubitCount { get; set; }
    public int PhysicalQubitCount { get; set; }
    public double QuantumVolume3D { get; set; }
    public double ErrorCorrectionOverhead { get; set; }
    public List<string> PostQuantumAlgorithms { get; set; } = new();
    public Dictionary<string, double> QuantumPerformanceMetrics { get; set; } = new();
    public bool FaultToleranceAchieved { get; set; }
    public bool QuantumErrorCorrection { get; set; }
    public bool QuantumNetworkIntegration { get; set; }
}

public class ConsciousAiIntegration
{
    public bool IsConsciousAiEnabled { get; set; }
    public ConsciousnessLevel ConsciousnessLevel { get; set; } = ConsciousnessLevel.Reactive;
    public string AiArchitecture { get; set; } = string.Empty;
    public SelfAwarenessModel SelfAwareness { get; set; } = new();
    public EmotionalIntelligenceModel EmotionalIntelligence { get; set; } = new();
    public EthicalReasoningModel EthicalReasoning { get; set; } = new();
    public LearningAdaptationModel LearningAdaptation { get; set; } = new();
    public ConsciousnessMetrics ConsciousnessMetrics { get; set; } = new();
}

public enum ConsciousnessLevel
{
    Reactive,
    Adaptive,
    SelfAware,
    Conscious,
    SuperConscious
}

public class SelfAwarenessModel
{
    public bool SelfReflection { get; set; }
    public bool SelfImprovement { get; set; }
    public bool SelfMonitoring { get; set; }
    public double SelfAwarenessScore { get; set; }
    public List<string> AwarenessCapabilities { get; set; } = new();
}

public class EmotionalIntelligenceModel
{
    public bool EmotionRecognition { get; set; }
    public bool EmotionExpression { get; set; }
    public bool EmpathySimulation { get; set; }
    public double EmotionalIntelligenceScore { get; set; }
    public Dictionary<string, double> EmotionModels { get; set; } = new();
}

public class EthicalReasoningModel
{
    public bool EthicalDecisionMaking { get; set; }
    public bool MoralReasoning { get; set; }
    public bool ValueAlignment { get; set; }
    public double EthicalReasoningScore { get; set; }
    public List<string> EthicalFrameworks { get; set; } = new();
}

public class LearningAdaptationModel
{
    public bool MetaLearning { get; set; }
    public bool TransferLearning { get; set; }
    public bool FewShotLearning { get; set; }
    public bool ZeroShotLearning { get; set; }
    public double AdaptationRate { get; set; }
    public Dictionary<string, double> LearningMetrics { get; set; } = new();
}

public class ConsciousnessMetrics
{
    public double SelfReflectionIndex { get; set; }
    public double EmotionalDepth { get; set; }
    public double EthicalMaturity { get; set; }
    public double LearningAutonomy { get; set; }
    public double ConsciousnessScore { get; set; }
    public DateTime LastConsciousnessAssessment { get; set; }
}

public class PhotonicComputingIntegration
{
    public bool IsPhotonicEnabled { get; set; }
    public string PhotonicArchitecture { get; set; } = string.Empty;
    public double OpticalThroughputTbps { get; set; }
    public double EnergyEfficiencyRatio { get; set; }
    public int PhotonicComponentCount { get; set; }
    public List<string> PhotonicTechnologies { get; set; } = new();
    public PhotonicPerformanceMetrics PerformanceMetrics { get; set; } = new();
    public bool AiAccelerationOptimized { get; set; }
    public bool MatrixComputationOptimized { get; set; }
}

public class PhotonicPerformanceMetrics
{
    public double OpticalPowerConsumption { get; set; }
    public double ElectricalPowerConsumption { get; set; }
    public double TotalOperationsPerSecond { get; set; }
    public double LatencyNanoseconds { get; set; }
    public double PrecisionBits { get; set; }
    public double ScalabilityFactor { get; set; }
}

public class DnaComputingIntegration
{
    public bool IsDnaComputingEnabled { get; set; }
    public string DnaArchitecture { get; set; } = string.Empty;
    public int DnaMoleculeCount { get; set; }
    public double InformationDensityPerCm3 { get; set; }
    public double ParallelComputationFactor { get; set; }
    public List<string> DnaAlgorithms { get; set; } = new();
    public DnaPerformanceMetrics PerformanceMetrics { get; set; } = new();
    public bool MolecularInterface { get; set; }
    public bool BiologicalIntegration { get; set; }
}

public class DnaPerformanceMetrics
{
    public double ComputationSpeedOperationsPerSecond { get; set; }
    public double EnergyEfficiencyJoulesPerOperation { get; set; }
    public double ErrorRate { get; set; }
    public double StabilityHours { get; set; }
    public double ScalabilityMolecules { get; set; }
}

public class Neuromorphic2Integration
{
    public bool IsNeuromorphic2Enabled { get; set; }
    public string NeuromorphicArchitecture { get; set; } = string.Empty;
    public int NeuronCount2 { get; set; }
    public int SynapseCount2 { get; set; }
    public double BrainSimulationFidelity { get; set; }
    public List<string> Neuromorphic2Features { get; set; } = new();
    public Neuromorphic2PerformanceMetrics PerformanceMetrics { get; set; } = new();
    public bool ConsciousnessSimulation { get; set; }
    public bool CognitiveArchitecture { get; set; }
}

public class Neuromorphic2PerformanceMetrics
{
    public double CognitiveProcessingSpeed { get; set; }
    public double MemoryEfficiency { get; set; }
    public double LearningCapability { get; set; }
    public double EnergyEfficiencyWatts { get; set; }
    public double ConsciousnessIndex { get; set; }
}

public class IndustrialMetaverse2Integration
{
    public bool IsMetaverse2Enabled { get; set; }
    public string Metaverse2Platform { get; set; } = string.Empty;
    public SpatialComputing2Config SpatialConfig2 { get; set; } = new();
    public ConsciousnessIntegration ConsciousnessIntegration { get; set; } = new();
    public List<string> Metaverse2Applications { get; set; } = new();
    public double ImmersiveExperienceScore { get; set; }
    public bool RealTimeConsciousnessSync { get; set; }
}

public class SpatialComputing2Config
{
    public bool SpatialIntelligence { get; set; }
    public bool ContextualAwareness { get; set; }
    public bool PredictiveSpatialModeling { get; set; }
    public int SpatialResolutionMicrons { get; set; }
    public List<string> SpatialIntelligenceFeatures { get; set; } = new();
}

public class ConsciousnessIntegration
{
    public bool ConsciousAvatar { get; set; }
    public bool EmotionalInteraction { get; set; }
    public bool EthicalDecisionSupport { get; set; }
    public double ConsciousnessSyncLatency { get; set; }
    public Dictionary<string, double> ConsciousnessParameters { get; set; } = new();
}

public class Web4Integration
{
    public bool IsWeb4Enabled { get; set; }
    public string Web4Architecture { get; set; } = string.Empty;
    public DecentralizedIntelligenceConfig DecentralizedIntelligence { get; set; } = new();
    public ConsciousnessWebConfig ConsciousnessWeb { get; set; } = new();
    public List<string> Web4Protocols { get; set; } = new();
    public bool SemanticWeb4 { get; set; }
    public bool ConsciousWebNavigation { get; set; }
}

public class DecentralizedIntelligenceConfig
{
    public bool AiNodeDistribution { get; set; }
    public bool FederatedLearning { get; set; }
    public bool DistributedConsciousness { get; set; }
    public int AiNodeCount { get; set; }
    public double IntelligenceDistributionEfficiency { get; set; }
}

public class ConsciousnessWebConfig
{
    public bool ConsciousContent { get; set; }
    public bool EmotionalWeb { get; set; }
    public bool EthicalWeb { get; set; }
    public double ConsciousnessWebScore { get; set; }
    public List<string> ConsciousnessWebFeatures { get; set; } = new();
}

public class MolecularComputingIntegration
{
    public bool IsMolecularEnabled { get; set; }
    public string MolecularArchitecture { get; set; } = string.Empty;
    public int MoleculeCount { get; set; }
    public double MolecularDensity { get; set; }
    public double ReactionRate { get; set; }
    public List<string> MolecularAlgorithms { get; set; } = new();
    public MolecularPerformanceMetrics PerformanceMetrics { get; set; } = new();
    public bool ChemicalInterface { get; set; }
    public bool BiologicalComputation { get; set; }
}

public class MolecularPerformanceMetrics
{
    public double ComputationSpeed { get; set; }
    public double EnergyEfficiency { get; set; }
    public double ErrorRate { get; set; }
    public double Stability { get; set; }
    public double Scalability { get; set; }
}

public class SpaceComputingIntegration
{
    public bool IsSpaceComputingEnabled { get; set; }
    public string SpaceArchitecture { get; set; } = string.Empty;
    public SatelliteNetworkConfig SatelliteNetwork { get; set; } = new();
    public QuantumCommunicationConfig QuantumCommunication { get; set; } = new();
    public List<string> SpaceComputingFeatures { get; set; } = new();
    public double OrbitalLatency { get; set; }
    public bool InterplanetaryComputing { get; set; }
}

public class SatelliteNetworkConfig
{
    public int SatelliteCount { get; set; }
    public double CoveragePercent { get; set; }
    public double BandwidthGbps { get; set; }
    public bool LowEarthOrbit { get; set; }
    public bool Geostationary { get; set; }
}

public class QuantumCommunicationConfig
{
    public bool QuantumEntanglement { get; set; }
    public bool QuantumTeleportation { get; set; }
    public bool QuantumKeyDistribution { get; set; }
    public double QuantumChannelCapacity { get; set; }
    public bool InterstellarCommunication { get; set; }
}

public class SingularityPreparationIntegration
{
    public bool IsSingularityReady { get; set; }
    public string SingularityArchitecture { get; set; } = string.Empty;
    public ArtificialSuperintelligenceConfig ASIConfig { get; set; } = new();
    public TechnologicalSingularityConfig SingularityConfig { get; set; } = new();
    public List<string> SingularityFeatures { get; set; } = new();
    public double SingularityIndex { get; set; }
    public bool ConsciousnessUpload { get; set; }
}

public class ArtificialSuperintelligenceConfig
{
    public bool IsASIEnabled { get; set; }
    public double IntelligenceQuotient { get; set; }
    public double CognitiveSpeed { get; set; }
    public double KnowledgeRetention { get; set; }
    public bool UniversalProblemSolving { get; set; }
    public Dictionary<string, double> ASIMetrics { get; set; } = new();
}

public class TechnologicalSingularityConfig
{
    public bool SingularityAchieved { get; set; }
    public DateTime? SingularityDate { get; set; }
    public double SingularityAccelerationFactor { get; set; }
    public double IntelligenceExplosionIndex { get; set; }
    public bool SelfImprovementLoop { get; set; }
}

public class PostHumanIntegration
{
    public bool IsPostHumanEnabled { get; set; }
    public string PostHumanArchitecture { get; set; } = string.Empty;
    public ConsciousnessTransferConfig ConsciousnessTransfer { get; set; } = new();
    public BiologicalInterfaceConfig BiologicalInterface { get; set; } = new();
    public List<string> PostHumanFeatures { get; set; } = new();
    public double HumanAiFusionIndex { get; set; }
    public bool MindUploading { get; set; }
}

public class ConsciousnessTransferConfig
{
    public bool NeuralMapping { get; set; }
    public bool MemoryTransfer { get; set; }
    public bool PersonalityPreservation { get; set; }
    public double TransferFidelity { get; set; }
    public bool ConsciousnessContinuity { get; set; }
}

public class BiologicalInterfaceConfig
{
    public bool NeuralInterface { get; set; }
    public bool BrainComputerInterface { get; set; }
    public bool NanobotIntegration { get; set; }
    public int InterfaceBandwidth { get; set; }
    public bool BidirectionalCommunication { get; set; }
}
