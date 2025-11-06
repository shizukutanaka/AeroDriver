// 研究ベースの改善: SBOM生成と供給チェーン透明性
// 根拠: CISA/NIST Supply Chain Transparency - Executive Order 14028
//      SBOMは現在業界標準、連邦政府は必須化
// 優先度: P0 (最高) - コンプライアンス・透明性クリティカル
// 出典: SPDX Project, CycloneDX Specification, NIST SW Supply Chain Framework

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.SupplyChain;

/// <summary>
/// SBOM生成エンジン
/// SPDX/CycloneDX形式での供給チェーン完全透明性
///
/// 機能:
/// 1. SBOM生成 - SPDX/CycloneDX形式
/// 2. 依存関係追跡 - コンポーネント関係図
/// 3. ライセンス分析 - コンプライアンス検証
/// 4. VEX統合 - 脆弱性の対応可能性評価
/// </summary>
public class SBOMGenerator
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, DriverSBOM> _sboms;

    public SBOMGenerator(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sboms = new Dictionary<string, DriverSBOM>();

        _logger.LogInformation("SBOMGenerator initialized for SPDX/CycloneDX generation");
    }

    /// <summary>
    /// ドライバーSBOMを生成
    /// </summary>
    public async Task<DriverSBOM> GenerateSBOMAsync(
        string driverId,
        string driverName,
        string driverVersion,
        string sourceCodePath,
        List<string> dependencies,
        CancellationToken ct = default)
    {
        _logger.LogInformation($"Generating SBOM for {driverName} v{driverVersion}");

        var sbom = new DriverSBOM
        {
            SpecVersion = "SPDX-2.3",
            CreatedAt = DateTime.UtcNow,
            DataLicense = "CC0-1.0",
            DriverComponent = new SBOMComponent
            {
                Type = "application",
                Name = driverName,
                Version = driverVersion,
                DownloadLocation = $"https://github.com/aerodriver/{driverName.ToLower()}",
                FilesAnalyzed = true
            }
        };

        try
        {
            // メインドライバーコンポーネントをスキャン
            await AnalyzeDriverComponentAsync(sbom.DriverComponent, sourceCodePath, ct);

            // 依存関係を処理
            sbom.Dependencies = await ResolveDependenciesAsync(dependencies, ct);

            // ライセンス情報を抽出
            ExtractLicenseInformation(sbom);

            // 関係を構築
            BuildComponentRelationships(sbom);

            // チェックサムを計算
            CalculateChecksums(sbom);

            _sboms[driverId] = sbom;

            _logger.LogInformation(
                $"SBOM generated: {sbom.DriverComponent.Name} " +
                $"with {sbom.Dependencies.Count} dependencies, " +
                $"unique licenses: {sbom.LicenseInformation.Count}");

            return sbom;
        }
        catch (Exception ex)
        {
            _logger.LogError($"SBOM generation failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// ドライバーコンポーネントを分析
    /// </summary>
    private async Task AnalyzeDriverComponentAsync(
        SBOMComponent component,
        string sourceCodePath,
        CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(sourceCodePath))
            {
                _logger.LogWarning($"Source path not found: {sourceCodePath}");
                return;
            }

            var files = Directory.GetFiles(sourceCodePath, "*.*", SearchOption.AllDirectories);
            component.Files = new List<SBOMFile>();

            foreach (var filePath in files.Take(100)) // 最大100ファイル分析
            {
                if (ct.IsCancellationRequested) break;

                var fileInfo = new FileInfo(filePath);
                var sbomFile = new SBOMFile
                {
                    FileName = fileInfo.Name,
                    FilePath = filePath.Replace(sourceCodePath, "").TrimStart(Path.DirectorySeparatorChar),
                    FileSize = fileInfo.Length,
                    FileType = Path.GetExtension(fileInfo.Name).TrimStart('.')
                };

                // ファイルハッシュを計算
                sbomFile.Hashes = new Dictionary<string, string>
                {
                    { "SHA1", ComputeFileHash(filePath, HashAlgorithmName.SHA1) },
                    { "SHA256", ComputeFileHash(filePath, HashAlgorithmName.SHA256) }
                };

                component.Files.Add(sbomFile);
            }

            component.FileCount = component.Files.Count;

            _logger.LogInformation($"Analyzed {component.Files.Count} files for {component.Name}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Component analysis error: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 依存関係を解決
    /// </summary>
    private async Task<List<SBOMComponent>> ResolveDependenciesAsync(
        List<string> dependencyNames,
        CancellationToken ct)
    {
        var dependencies = new List<SBOMComponent>();

        // 標準的な Windows ドライバー依存関係を定義
        var knownDependencies = new Dictionary<string, (string version, string license)>
        {
            { "ntdll.dll", ("10.0", "Microsoft Proprietary") },
            { "kernel32.dll", ("10.0", "Microsoft Proprietary") },
            { "user32.dll", ("10.0", "Microsoft Proprietary") },
            { "setupapi.dll", ("10.0", "Microsoft Proprietary") },
            { "winusb.dll", ("10.0", "Microsoft Proprietary") },
            { "hid.dll", ("10.0", "Microsoft Proprietary") },
            { "wdm.sys", ("Kernel", "Microsoft Proprietary") }
        };

        foreach (var depName in dependencyNames)
        {
            if (ct.IsCancellationRequested) break;

            if (knownDependencies.TryGetValue(depName, out var depInfo))
            {
                dependencies.Add(new SBOMComponent
                {
                    Type = "library",
                    Name = depName,
                    Version = depInfo.version,
                    License = depInfo.license,
                    Supplier = "Microsoft",
                    DownloadLocation = $"https://microsoft.com/win32api/{depName}",
                    Scope = "required"
                });
            }
        }

        return await Task.FromResult(dependencies);
    }

    /// <summary>
    /// ライセンス情報を抽出
    /// </summary>
    private void ExtractLicenseInformation(DriverSBOM sbom)
    {
        var licenses = new HashSet<string>();

        // ドライバー本体のライセンス
        if (!string.IsNullOrEmpty(sbom.DriverComponent.License))
        {
            licenses.Add(sbom.DriverComponent.License);
        }

        // 依存関係のライセンス
        foreach (var dep in sbom.Dependencies)
        {
            if (!string.IsNullOrEmpty(dep.License))
            {
                licenses.Add(dep.License);
            }
        }

        sbom.LicenseInformation = licenses.ToDictionary(
            l => l,
            l => DetermineLicenseCompatibility(l)
        );

        _logger.LogInformation($"Extracted {licenses.Count} unique licenses");
    }

    /// <summary>
    /// ライセンス互換性を判定
    /// </summary>
    private LicenseCompatibility DetermineLicenseCompatibility(string license)
    {
        return license.ToLower() switch
        {
            var l when l.Contains("mit") => LicenseCompatibility.Permissive,
            var l when l.Contains("apache") => LicenseCompatibility.Permissive,
            var l when l.Contains("bsd") => LicenseCompatibility.Permissive,
            var l when l.Contains("gpl") => LicenseCompatibility.Copyleft,
            var l when l.Contains("agpl") => LicenseCompatibility.StrongCopyleft,
            var l when l.Contains("proprietary") => LicenseCompatibility.Proprietary,
            var l when l.Contains("commercial") => LicenseCompatibility.Commercial,
            _ => LicenseCompatibility.Unknown
        };
    }

    /// <summary>
    /// コンポーネント関係を構築
    /// </summary>
    private void BuildComponentRelationships(DriverSBOM sbom)
    {
        sbom.Relationships = new List<SBOMRelationship>();

        // ドライバーがすべての依存関係を「depends on」
        foreach (var dep in sbom.Dependencies)
        {
            sbom.Relationships.Add(new SBOMRelationship
            {
                From = sbom.DriverComponent.Name,
                To = dep.Name,
                RelationType = "DEPENDS_ON",
                Description = $"{sbom.DriverComponent.Name} depends on {dep.Name}"
            });
        }

        _logger.LogInformation($"Built {sbom.Relationships.Count} relationships");
    }

    /// <summary>
    /// チェックサムを計算
    /// </summary>
    private void CalculateChecksums(DriverSBOM sbom)
    {
        // ドライバーコンポーネント自体のメタチェックサム
        var componentJson = JsonSerializer.Serialize(sbom.DriverComponent);
        sbom.DriverComponent.ChecksumSHA256 = ComputeStringHash(
            componentJson, HashAlgorithmName.SHA256);

        _logger.LogInformation(
            $"Checksums calculated: {sbom.DriverComponent.ChecksumSHA256}");
    }

    /// <summary>
    /// SBOM を SPDX JSON 形式で出力
    /// </summary>
    public async Task<string> ExportAsSPDXJsonAsync(
        string driverId,
        CancellationToken ct = default)
    {
        if (!_sboms.TryGetValue(driverId, out var sbom))
        {
            throw new InvalidOperationException("SBOM not found");
        }

        var spdxDocument = new
        {
            spdxVersion = sbom.SpecVersion,
            dataLicense = sbom.DataLicense,
            SPDXID = $"SPDXRef-{sbom.DriverComponent.Name}",
            name = sbom.DriverComponent.Name,
            documentNamespace = $"https://aerodriver.io/sbom/{sbom.DriverComponent.Name}/{DateTime.UtcNow.Ticks}",
            creationInfo = new
            {
                created = sbom.CreatedAt.ToString("o"),
                creators = new[] { "Tool: AeroDriver-SBOMGenerator-1.0" },
                licenseListVersion = "3.21"
            },
            packages = new[]
            {
                new
                {
                    SPDXID = $"SPDXRef-{sbom.DriverComponent.Name}",
                    name = sbom.DriverComponent.Name,
                    versionInfo = sbom.DriverComponent.Version,
                    downloadLocation = sbom.DriverComponent.DownloadLocation,
                    filesAnalyzed = sbom.DriverComponent.FilesAnalyzed,
                    licenseConcluded = sbom.DriverComponent.License ?? "NOASSERTION",
                    licenseDeclared = sbom.DriverComponent.License ?? "NOASSERTION",
                    copyrightText = "NOASSERTION",
                    externalRefs = sbom.Dependencies.Select((dep, idx) => new
                    {
                        referenceCategory = "PACKAGE-MANAGER",
                        referenceType = "npm",
                        referenceLocator = $"{dep.Name}@{dep.Version}"
                    }).ToArray()
                }
            },
            relationships = sbom.Relationships.Select(r => new
            {
                spdxElementId = $"SPDXRef-{r.From}",
                relationshipType = r.RelationType,
                relatedSpdxElement = $"SPDXRef-{r.To}"
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(spdxDocument, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return await Task.FromResult(json);
    }

    /// <summary>
    /// SBOM を CycloneDX XML 形式で出力
    /// </summary>
    public async Task<string> ExportAsCycloneDXAsync(
        string driverId,
        CancellationToken ct = default)
    {
        if (!_sboms.TryGetValue(driverId, out var sbom))
        {
            throw new InvalidOperationException("SBOM not found");
        }

        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<bom xmlns=\"http://cyclonedx.org/schema/bom/1.4\" version=\"1\">");
        xml.AppendLine("  <metadata>");
        xml.AppendLine($"    <timestamp>{sbom.CreatedAt:o}</timestamp>");
        xml.AppendLine("    <tools>");
        xml.AppendLine("      <tool>");
        xml.AppendLine("        <vendor>AeroDriver</vendor>");
        xml.AppendLine("        <name>SBOMGenerator</name>");
        xml.AppendLine("        <version>1.0</version>");
        xml.AppendLine("      </tool>");
        xml.AppendLine("    </tools>");
        xml.AppendLine("  </metadata>");
        xml.AppendLine("  <components>");

        // ドライバーコンポーネント
        xml.AppendLine("    <component type=\"application\">");
        xml.AppendLine($"      <name>{sbom.DriverComponent.Name}</name>");
        xml.AppendLine($"      <version>{sbom.DriverComponent.Version}</version>");
        xml.AppendLine($"      <description>Windows driver component</description>");
        if (!string.IsNullOrEmpty(sbom.DriverComponent.License))
        {
            xml.AppendLine($"      <licenses>");
            xml.AppendLine($"        <license><name>{sbom.DriverComponent.License}</name></license>");
            xml.AppendLine($"      </licenses>");
        }
        xml.AppendLine("    </component>");

        // 依存関係
        foreach (var dep in sbom.Dependencies)
        {
            xml.AppendLine("    <component type=\"library\">");
            xml.AppendLine($"      <name>{dep.Name}</name>");
            xml.AppendLine($"      <version>{dep.Version}</version>");
            if (!string.IsNullOrEmpty(dep.License))
            {
                xml.AppendLine($"      <licenses>");
                xml.AppendLine($"        <license><name>{dep.License}</name></license>");
                xml.AppendLine($"      </licenses>");
            }
            xml.AppendLine("    </component>");
        }

        xml.AppendLine("  </components>");
        xml.AppendLine("</bom>");

        return await Task.FromResult(xml.ToString());
    }

    /// <summary>
    /// VEX (Vulnerability Exploitability eXchange) を生成
    /// </summary>
    public async Task<DriverVEX> GenerateVEXAsync(
        string driverId,
        List<CVEInfo> vulnerabilities,
        CancellationToken ct = default)
    {
        if (!_sboms.TryGetValue(driverId, out var sbom))
        {
            throw new InvalidOperationException("SBOM not found");
        }

        var vex = new DriverVEX
        {
            SpecVersion = "VEX-1.0",
            CreatedAt = DateTime.UtcNow,
            TargetComponent = sbom.DriverComponent.Name,
            Statements = new List<VEXStatement>()
        };

        foreach (var cve in vulnerabilities)
        {
            var statement = new VEXStatement
            {
                CVE = cve.CveId,
                Component = cve.AffectedComponent,
                Status = DetermineExploitability(cve, sbom),
                JustificationReason = GenerateJustification(cve, sbom),
                AnalysisAt = DateTime.UtcNow
            };

            vex.Statements.Add(statement);
        }

        _logger.LogInformation(
            $"VEX generated with {vex.Statements.Count} exploitability assessments");

        return await Task.FromResult(vex);
    }

    /// <summary>
    /// 脆弱性の対応可能性を判定
    /// </summary>
    private VEXStatus DetermineExploitability(CVEInfo cve, DriverSBOM sbom)
    {
        // ドライバーに影響する依存関係かチェック
        var isDependency = sbom.Dependencies.Any(d => d.Name == cve.AffectedComponent);

        if (!isDependency)
        {
            return VEXStatus.NotAffected; // 依存関係にない場合
        }

        return cve.CVSS >= 9.0 ? VEXStatus.Affected : VEXStatus.Affected;
    }

    /// <summary>
    /// VEX 正当化理由を生成
    /// </summary>
    private string GenerateJustification(CVEInfo cve, DriverSBOM sbom)
    {
        var reasons = new List<string>();

        if (cve.CVSS >= 9.0)
        {
            reasons.Add($"CRITICAL: CVSS {cve.CVSS} - requires immediate mitigation");
        }
        else if (cve.CVSS >= 7.0)
        {
            reasons.Add($"HIGH: CVSS {cve.CVSS} - prioritized remediation");
        }

        if (!string.IsNullOrEmpty(cve.ExploitPublicly))
        {
            reasons.Add($"Exploit publicly available: {cve.ExploitPublicly}");
        }

        if (sbom.Dependencies.Any(d => d.Name == cve.AffectedComponent && d.Version == cve.AffectedVersion))
        {
            reasons.Add($"Version {cve.AffectedVersion} is currently in use");
        }
        else
        {
            reasons.Add("Vulnerable version not detected in supply chain");
        }

        return string.Join(" | ", reasons);
    }

    /// <summary>
    /// ドライバー SBOM 統計を取得
    /// </summary>
    public SBOMStatistics GetSBOMStatistics(string driverId)
    {
        if (!_sboms.TryGetValue(driverId, out var sbom))
        {
            return new SBOMStatistics { DriverId = driverId };
        }

        return new SBOMStatistics
        {
            DriverId = driverId,
            DriverName = sbom.DriverComponent.Name,
            DriverVersion = sbom.DriverComponent.Version,
            CreatedAt = sbom.CreatedAt,
            TotalComponents = 1 + sbom.Dependencies.Count,
            TotalDependencies = sbom.Dependencies.Count,
            UniqueLicenses = sbom.LicenseInformation.Count,
            FilesAnalyzed = sbom.DriverComponent.FileCount,
            RelationshipsCount = sbom.Relationships.Count,
            CopyleftDependencies = sbom.Dependencies.Count(d => d.License?.Contains("GPL") ?? false)
        };
    }
}

/// <summary>
/// ドライバー SBOM
/// </summary>
public class DriverSBOM
{
    public string SpecVersion { get; set; } = "SPDX-2.3";
    public DateTime CreatedAt { get; set; }
    public string DataLicense { get; set; } = "CC0-1.0";
    public SBOMComponent DriverComponent { get; set; } = new();
    public List<SBOMComponent> Dependencies { get; set; } = new();
    public Dictionary<string, LicenseCompatibility> LicenseInformation { get; set; } = new();
    public List<SBOMRelationship> Relationships { get; set; } = new();
}

/// <summary>
/// SBOM コンポーネント
/// </summary>
public class SBOMComponent
{
    public string Type { get; set; } = string.Empty; // application, library
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string DownloadLocation { get; set; } = string.Empty;
    public string Scope { get; set; } = "required"; // required, optional, excluded
    public bool FilesAnalyzed { get; set; }
    public int FileCount { get; set; }
    public List<SBOMFile> Files { get; set; } = new();
    public string ChecksumSHA256 { get; set; } = string.Empty;
}

/// <summary>
/// SBOM ファイル
/// </summary>
public class SBOMFile
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;
    public Dictionary<string, string> Hashes { get; set; } = new();
}

/// <summary>
/// SBOM 関係
/// </summary>
public class SBOMRelationship
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string RelationType { get; set; } = "DEPENDS_ON";
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// ライセンス互換性
/// </summary>
public enum LicenseCompatibility
{
    Permissive,         // MIT, Apache, BSD
    Copyleft,           // GPL v2/v3
    StrongCopyleft,     // AGPL
    Proprietary,        // Microsoft, Closed-source
    Commercial,         // Commercial licenses
    Unknown
}

/// <summary>
/// ドライバー VEX (Vulnerability Exploitability Exchange)
/// </summary>
public class DriverVEX
{
    public string SpecVersion { get; set; } = "VEX-1.0";
    public DateTime CreatedAt { get; set; }
    public string TargetComponent { get; set; } = string.Empty;
    public List<VEXStatement> Statements { get; set; } = new();
}

/// <summary>
/// VEX ステートメント
/// </summary>
public class VEXStatement
{
    public string CVE { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public VEXStatus Status { get; set; }
    public string JustificationReason { get; set; } = string.Empty;
    public DateTime AnalysisAt { get; set; }
}

/// <summary>
/// VEX ステータス
/// </summary>
public enum VEXStatus
{
    NotAffected,    // 脆弱性は影響しない
    Affected,       // 脆弱性は影響する
    Fixed,          // 脆弱性は修正済み
    Unknown         // 不明
}

/// <summary>
/// CVE 情報
/// </summary>
public class CVEInfo
{
    public string CveId { get; set; } = string.Empty;
    public string AffectedComponent { get; set; } = string.Empty;
    public string AffectedVersion { get; set; } = string.Empty;
    public double CVSS { get; set; }
    public string ExploitPublicly { get; set; } = string.Empty;
}

/// <summary>
/// SBOM 統計
/// </summary>
public class SBOMStatistics
{
    public string DriverId { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int TotalComponents { get; set; }
    public int TotalDependencies { get; set; }
    public int UniqueLicenses { get; set; }
    public int FilesAnalyzed { get; set; }
    public int RelationshipsCount { get; set; }
    public int CopyleftDependencies { get; set; }
}

/// <summary>
/// ハッシュ計算ユーティリティ
/// </summary>
public static class HashUtilities
{
    public static string ComputeFileHash(string filePath, HashAlgorithmName algorithm)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var hash = algorithm.Name switch
            {
                "SHA1" => SHA1.Create(),
                "SHA256" => SHA256.Create(),
                _ => SHA256.Create()
            };

            var hashBytes = hash.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return "error";
        }
    }

    public static string ComputeStringHash(string input, HashAlgorithmName algorithm)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        using var hash = algorithm.Name switch
        {
            "SHA1" => SHA1.Create(),
            "SHA256" => SHA256.Create(),
            _ => SHA256.Create()
        };

        var hashBytes = hash.ComputeHash(bytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}

// Extension methods for convenience
public static class SBOMExtensions
{
    public static string ComputeFileHash(this string filePath, HashAlgorithmName algorithm)
    {
        return HashUtilities.ComputeFileHash(filePath, algorithm);
    }

    public static string ComputeStringHash(this string input, HashAlgorithmName algorithm)
    {
        return HashUtilities.ComputeStringHash(input, algorithm);
    }
}
