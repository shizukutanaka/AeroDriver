// 研究ベースの改善: ドライバー互換性マトリックス
// 根拠: Windows Hardware Compatibility Program (WHCP)
//      複雑な環境でのドライバー互換性を体系的に管理
// 優先度: P1 (高) - テスト効率化
// 出典: Microsoft Hardware Compatibility Program, HLK (Hardware Lab Kit)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Validation;

/// <summary>
/// ドライバー互換性マトリックス
/// ハードウェア、OS、およびドライバーバージョン間の互換性を管理
///
/// マトリックス構造:
/// [ハードウェア ID] × [OS version] × [Driver version] = 互換性レベル
///
/// 互換性レベル:
/// - Certified (WHQL)
/// - Tested
/// - Compatible (推奨)
/// - Untested
/// - Incompatible
/// </summary>
public class DriverCompatibilityMatrix
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, HardwareProfile> _hardwareProfiles;
    private readonly Dictionary<string, OperatingSystemVersion> _osVersions;
    private readonly Dictionary<string, List<CompatibilityEntry>> _compatibilityTable;

    public DriverCompatibilityMatrix(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hardwareProfiles = new Dictionary<string, HardwareProfile>();
        _osVersions = new Dictionary<string, OperatingSystemVersion>();
        _compatibilityTable = new Dictionary<string, List<CompatibilityEntry>>();

        InitializeCompatibilityMatrix();
    }

    /// <summary>
    /// ドライバーとハードウェア/OS組み合わせの互換性を検査
    /// </summary>
    public async Task<CompatibilityMatrixResult> CheckCompatibilityAsync(
        string driverName,
        string driverVersion,
        string hardwareId,
        string osVersion,
        CancellationToken ct = default)
    {
        _logger.LogInformation($"Checking compatibility: {driverName} v{driverVersion} on {hardwareId} / {osVersion}");

        var result = new CompatibilityMatrixResult
        {
            DriverName = driverName,
            DriverVersion = driverVersion,
            HardwareId = hardwareId,
            OSVersion = osVersion,
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            // ハードウェアプロファイルを取得
            if (!_hardwareProfiles.TryGetValue(hardwareId, out var hardware))
            {
                result.CompatibilityLevel = CompatibilityLevel.Untested;
                result.Message = $"Hardware profile not found: {hardwareId}";
                return result;
            }

            // OS バージョンを取得
            if (!_osVersions.TryGetValue(osVersion, out var os))
            {
                result.CompatibilityLevel = CompatibilityLevel.Untested;
                result.Message = $"OS version not found: {osVersion}";
                return result;
            }

            // 互換性テーブルから該当するエントリを検索
            var key = $"{driverName}:{driverVersion}:{hardwareId}:{osVersion}";

            CompatibilityEntry? entry = null;
            if (_compatibilityTable.TryGetValue(driverName, out var entries))
            {
                entry = entries.FirstOrDefault(e =>
                    e.DriverVersion == driverVersion &&
                    e.HardwareId == hardwareId &&
                    e.OSVersion == osVersion);
            }

            if (entry == null)
            {
                // テスト結果がない場合は判定を実行
                result.CompatibilityLevel = await EvaluateCompatibilityAsync(
                    driverName, driverVersion, hardware, os, ct);
                result.Message = $"Evaluated compatibility: {result.CompatibilityLevel}";
            }
            else
            {
                result.CompatibilityLevel = entry.CompatibilityLevel;
                result.Message = entry.Notes;
                result.TestedDate = entry.TestedDate;
                result.TestResult = entry.TestResult;
            }

            // 推奨アクションを生成
            result.Recommendation = GenerateRecommendation(result.CompatibilityLevel);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Compatibility check error: {ex.Message}");
            result.CompatibilityLevel = CompatibilityLevel.Untested;
            result.Message = $"Error: {ex.Message}";
            result.Exception = ex;
        }

        return result;
    }

    /// <summary>
    /// 互換性を評価
    /// </summary>
    private async Task<CompatibilityLevel> EvaluateCompatibilityAsync(
        string driverName,
        string driverVersion,
        HardwareProfile hardware,
        OperatingSystemVersion os,
        CancellationToken ct)
    {
        var level = CompatibilityLevel.Untested;

        // Check 1: OS サポート
        if (!hardware.SupportedOSVersions.Contains(os.Version))
        {
            return CompatibilityLevel.Incompatible;
        }

        // Check 2: ドライバーアーキテクチャ
        if (!DoesArchitectureMatch(hardware.Architecture, driverVersion))
        {
            return CompatibilityLevel.Incompatible;
        }

        // Check 3: チップセット互換性
        if (!IsChipsetCompatible(hardware.ChipsetId, driverName))
        {
            return CompatibilityLevel.Incompatible;
        }

        // Check 4: WHQL認証
        var hasWhqlCertification = await CheckWhqlCertificationAsync(driverName, driverVersion, ct);
        if (hasWhqlCertification)
        {
            return CompatibilityLevel.Certified;
        }

        // Check 5: テスト済み
        var hasBeeenTested = await CheckIfTestedAsync(driverName, driverVersion, hardware, os, ct);
        if (hasBeeenTested)
        {
            return CompatibilityLevel.Tested;
        }

        // デフォルト: 互換性が予想される
        return CompatibilityLevel.Compatible;
    }

    /// <summary>
    /// アーキテクチャマッチングをチェック
    /// </summary>
    private bool DoesArchitectureMatch(string hardwareArch, string driverVersion)
    {
        // ドライバーバージョンにアーキテクチャ情報が含まれているか確認
        return driverVersion.Contains("x64") || driverVersion.Contains("x86") ||
               driverVersion.Contains("arm64") || driverVersion.Contains(hardwareArch);
    }

    /// <summary>
    /// チップセット互換性をチェック
    /// </summary>
    private bool IsChipsetCompatible(string chipsetId, string driverName)
    {
        // チップセットとドライバーの対応を確認
        var chipsetDriverMap = new Dictionary<string, List<string>>
        {
            ["Intel-Z890"] = new() { "Intel Graphics", "Intel Chipset" },
            ["AMD-X870"] = new() { "AMD Radeon", "AMD Chipset" },
            ["NVIDIA-A800"] = new() { "NVIDIA Graphics" }
        };

        if (chipsetDriverMap.TryGetValue(chipsetId, out var compatibleDrivers))
        {
            return compatibleDrivers.Any(d => driverName.Contains(d));
        }

        return true;  // デフォルト: 互換と判定
    }

    /// <summary>
    /// WHQL認証を確認
    /// </summary>
    private async Task<bool> CheckWhqlCertificationAsync(string driverName, string driverVersion, CancellationToken ct)
    {
        // 実環境ではMicrosoftのWHQLデータベースをクエリ
        var certifiedDrivers = new Dictionary<string, string>
        {
            ["Intel Graphics 770"] = "670.00",
            ["NVIDIA RTX 5090"] = "566.36",
            ["AMD Radeon RX 9070"] = "24.10"
        };

        var key = $"{driverName}";
        return certifiedDrivers.TryGetValue(key, out var certifiedVersion) &&
               driverVersion.CompareTo(certifiedVersion) >= 0;
    }

    /// <summary>
    /// テスト済みかどうかを確認
    /// </summary>
    private async Task<bool> CheckIfTestedAsync(
        string driverName,
        string driverVersion,
        HardwareProfile hardware,
        OperatingSystemVersion os,
        CancellationToken ct)
    {
        if (!_compatibilityTable.TryGetValue(driverName, out var entries))
        {
            return false;
        }

        return entries.Any(e =>
            e.DriverVersion == driverVersion &&
            e.HardwareId == hardware.Id &&
            e.OSVersion == os.Version &&
            e.CompatibilityLevel == CompatibilityLevel.Tested);
    }

    /// <summary>
    /// 推奨アクションを生成
    /// </summary>
    private string GenerateRecommendation(CompatibilityLevel level)
    {
        return level switch
        {
            CompatibilityLevel.Certified => "✓ WHQL certified - safe to deploy immediately",
            CompatibilityLevel.Tested => "✓ Tested and verified - safe to deploy",
            CompatibilityLevel.Compatible => "⚠ Compatible but untested - test in canary ring first",
            CompatibilityLevel.Untested => "⚠ Untested combination - high caution recommended",
            CompatibilityLevel.Incompatible => "✗ INCOMPATIBLE - do not deploy",
            _ => "? Unknown compatibility"
        };
    }

    /// <summary>
    /// テスト結果を記録
    /// </summary>
    public async Task RecordTestResultAsync(
        string driverName,
        string driverVersion,
        string hardwareId,
        string osVersion,
        CompatibilityLevel level,
        TestResultDetails result,
        CancellationToken ct = default)
    {
        var entry = new CompatibilityEntry
        {
            DriverName = driverName,
            DriverVersion = driverVersion,
            HardwareId = hardwareId,
            OSVersion = osVersion,
            CompatibilityLevel = level,
            TestedDate = DateTime.UtcNow,
            TestResult = result,
            Notes = $"Tested on {Environment.MachineName} at {DateTime.UtcNow:O}"
        };

        if (!_compatibilityTable.ContainsKey(driverName))
        {
            _compatibilityTable[driverName] = new List<CompatibilityEntry>();
        }

        _compatibilityTable[driverName].Add(entry);
        _logger.LogInformation($"Compatibility result recorded: {driverName} v{driverVersion} on {hardwareId}");
    }

    /// <summary>
    /// 互換性マトリックスを初期化
    /// </summary>
    private void InitializeCompatibilityMatrix()
    {
        // ハードウェアプロファイルを定義
        _hardwareProfiles["NVIDIA-RTX5090"] = new HardwareProfile
        {
            Id = "NVIDIA-RTX5090",
            Name = "NVIDIA GeForce RTX 5090",
            Vendor = "NVIDIA",
            Architecture = "CUDA",
            ChipsetId = "GB202",
            SupportedOSVersions = new List<string> { "Windows 11 24H2", "Windows Server 2025" },
            MaxMemory = 32768
        };

        _hardwareProfiles["Intel-Z890"] = new HardwareProfile
        {
            Id = "Intel-Z890",
            Name = "Intel Z890 Chipset",
            Vendor = "Intel",
            Architecture = "x64",
            ChipsetId = "Z890",
            SupportedOSVersions = new List<string> { "Windows 11 24H2", "Windows 10 22H2" },
            MaxMemory = 262144
        };

        _hardwareProfiles["AMD-X870"] = new HardwareProfile
        {
            Id = "AMD-X870",
            Name = "AMD X870 Chipset",
            Vendor = "AMD",
            Architecture = "x64",
            ChipsetId = "X870",
            SupportedOSVersions = new List<string> { "Windows 11 24H2", "Windows Server 2025" },
            MaxMemory = 262144
        };

        // OS バージョンを定義
        _osVersions["Windows 11 24H2"] = new OperatingSystemVersion
        {
            Version = "Windows 11 24H2",
            Build = 26100,
            ReleaseDate = new DateTime(2024, 12, 3),
            DriverIsolationRequired = true,
            CodeIntegrityRequired = true
        };

        _osVersions["Windows 10 22H2"] = new OperatingSystemVersion
        {
            Version = "Windows 10 22H2",
            Build = 19045,
            ReleaseDate = new DateTime(2022, 10, 25),
            DriverIsolationRequired = false,
            CodeIntegrityRequired = false
        };

        _osVersions["Windows Server 2025"] = new OperatingSystemVersion
        {
            Version = "Windows Server 2025",
            Build = 26100,
            ReleaseDate = new DateTime(2024, 11, 19),
            DriverIsolationRequired = true,
            CodeIntegrityRequired = true
        };

        // 互換性エントリを追加
        var nvidiaEntries = new List<CompatibilityEntry>
        {
            new()
            {
                DriverName = "NVIDIA Graphics",
                DriverVersion = "566.36",
                HardwareId = "NVIDIA-RTX5090",
                OSVersion = "Windows 11 24H2",
                CompatibilityLevel = CompatibilityLevel.Certified,
                TestedDate = new DateTime(2024, 12, 10),
                Notes = "WHQL certified driver"
            }
        };

        _compatibilityTable["NVIDIA Graphics"] = nvidiaEntries;

        _logger.LogInformation("Compatibility matrix initialized with hardware profiles and OS versions");
    }
}

/// <summary>
/// 互換性マトリックス結果
/// </summary>
public class CompatibilityMatrixResult
{
    public string DriverName { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public string OSVersion { get; set; } = string.Empty;
    public CompatibilityLevel CompatibilityLevel { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public DateTime? TestedDate { get; set; }
    public TestResultDetails? TestResult { get; set; }
    public DateTime CheckedAt { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// 互換性レベル
/// </summary>
public enum CompatibilityLevel
{
    /// <summary>WHQL認証済み - 本番環境展開可能</summary>
    Certified = 4,

    /// <summary>テスト済み - 互換性確認済み</summary>
    Tested = 3,

    /// <summary>互換性あり - テスト未実施</summary>
    Compatible = 2,

    /// <summary>テスト未実施 - 不確定</summary>
    Untested = 1,

    /// <summary>非互換 - 展開不可</summary>
    Incompatible = 0
}

/// <summary>
/// ハードウェアプロファイル
/// </summary>
public class HardwareProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string ChipsetId { get; set; } = string.Empty;
    public List<string> SupportedOSVersions { get; set; } = new();
    public long MaxMemory { get; set; }
    public List<string> RequiredFeatures { get; set; } = new();
}

/// <summary>
/// OSバージョン
/// </summary>
public class OperatingSystemVersion
{
    public string Version { get; set; } = string.Empty;
    public int Build { get; set; }
    public DateTime ReleaseDate { get; set; }
    public bool DriverIsolationRequired { get; set; }
    public bool CodeIntegrityRequired { get; set; }
    public bool HVCIRequired { get; set; }
}

/// <summary>
/// 互換性エントリ
/// </summary>
public class CompatibilityEntry
{
    public string DriverName { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public string OSVersion { get; set; } = string.Empty;
    public CompatibilityLevel CompatibilityLevel { get; set; }
    public DateTime TestedDate { get; set; }
    public TestResultDetails? TestResult { get; set; }
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// テスト結果詳細
/// </summary>
public class TestResultDetails
{
    public bool Passed { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> TestCases { get; set; } = new();
    public List<string> FailedTests { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
