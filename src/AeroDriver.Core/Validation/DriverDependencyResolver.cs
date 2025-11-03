// 研究ベースの改善: ドライバー依存性解決と競合検出システム
// 根拠: Graph Theory & Dependency Resolution - デバイスドライバー間の依存関係管理
//      互いに矛盾するドライバーバージョンの検出と自動解決
// 優先度: P1 (高) - システム安定性クリティカル
// 出典: Device Manager Documentation, Windows Hardware Compatibility, Dependency Graph Algorithms

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Validation;

/// <summary>
/// ドライバー依存性解決システム
/// ドライバー間の依存関係を管理し、競合を自動検出・解決
///
/// アルゴリズム:
/// 1. 依存性グラフを構築
/// 2. サイクル（循環依存）を検出
/// 3. 互換性マトリックスをチェック
/// 4. 競合するバージョンを検出
/// 5. 最適な解決策を提案
/// </summary>
public class DriverDependencyResolver
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, DriverNode> _driverGraph;
    private readonly Dictionary<string, List<CompatibilityConstraint>> _constraints;

    public DriverDependencyResolver(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driverGraph = new Dictionary<string, DriverNode>();
        _constraints = new Dictionary<string, List<CompatibilityConstraint>>();

        _logger.LogInformation("DriverDependencyResolver initialized");
    }

    /// <summary>
    /// ドライバーセットの依存性を検証
    /// </summary>
    public async Task<DependencyResolutionResult> ResolveDependenciesAsync(
        IEnumerable<DriverSpecification> drivers,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Resolving driver dependencies");

        var result = new DependencyResolutionResult
        {
            AnalyzedAt = DateTime.UtcNow
        };

        try
        {
            // グラフを構築
            BuildDependencyGraph(drivers.ToList());

            // サイクルを検出
            var cycles = DetectCycles();
            if (cycles.Count > 0)
            {
                result.HasCycles = true;
                result.Cycles = cycles;
                result.Message = $"Found {cycles.Count} circular dependencies";
                _logger.LogWarning(result.Message);
            }

            // 競合を検出
            var conflicts = DetectConflicts(drivers.ToList());
            result.Conflicts = conflicts;

            if (conflicts.Count > 0)
            {
                result.HasConflicts = true;
                result.Message = $"Found {conflicts.Count} incompatibilities";
                _logger.LogWarning(result.Message);
            }

            // 解決策を提案
            result.ResolutionSuggestions = SuggestResolutions(conflicts, cycles);

            result.IsResolvable = result.ResolutionSuggestions.Count > 0;
            result.Success = true;

            _logger.LogInformation($"Dependency analysis complete: {cycles.Count} cycles, {conflicts.Count} conflicts");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Dependency resolution failed: {ex.Message}");
            result.Success = false;
            result.Message = $"Error: {ex.Message}";
            result.Exception = ex;
        }

        return result;
    }

    /// <summary>
    /// 依存性グラフを構築
    /// </summary>
    private void BuildDependencyGraph(List<DriverSpecification> drivers)
    {
        _driverGraph.Clear();

        foreach (var driver in drivers)
        {
            var node = new DriverNode
            {
                Id = driver.DriverName,
                Name = driver.DriverName,
                Version = driver.Version,
                Dependencies = driver.Dependencies ?? new List<string>(),
                IncomingEdges = new List<string>(),
                OutgoingEdges = driver.Dependencies ?? new List<string>()
            };

            _driverGraph[driver.DriverName] = node;
        }

        // 逆方向のエッジを設定
        foreach (var driver in drivers)
        {
            if (driver.Dependencies != null)
            {
                foreach (var dep in driver.Dependencies)
                {
                    if (_driverGraph.TryGetValue(dep, out var depNode))
                    {
                        depNode.IncomingEdges.Add(driver.DriverName);
                    }
                }
            }
        }

        _logger.LogInformation($"Built dependency graph with {_driverGraph.Count} nodes");
    }

    /// <summary>
    /// 循環依存を検出 (DFS)
    /// </summary>
    private List<List<string>> DetectCycles()
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var node in _driverGraph.Keys)
        {
            if (!visited.Contains(node))
            {
                var cycle = new List<string>();
                if (DFS_DetectCycle(node, visited, recursionStack, cycle))
                {
                    cycles.Add(cycle);
                }
            }
        }

        return cycles;
    }

    /// <summary>
    /// DFSでサイクルを検出（ヘルパー関数）
    /// </summary>
    private bool DFS_DetectCycle(
        string current,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> path)
    {
        visited.Add(current);
        recursionStack.Add(current);
        path.Add(current);

        if (_driverGraph.TryGetValue(current, out var node))
        {
            foreach (var dependent in node.OutgoingEdges)
            {
                if (!visited.Contains(dependent))
                {
                    if (DFS_DetectCycle(dependent, visited, recursionStack, path))
                    {
                        return true;
                    }
                }
                else if (recursionStack.Contains(dependent))
                {
                    // サイクル検出
                    var cycleStart = path.IndexOf(dependent);
                    path = new List<string>(path.Skip(cycleStart));
                    return true;
                }
            }
        }

        recursionStack.Remove(current);
        return false;
    }

    /// <summary>
    /// ドライバー間の競合を検出
    /// </summary>
    private List<DriverConflict> DetectConflicts(List<DriverSpecification> drivers)
    {
        var conflicts = new List<DriverConflict>();

        // ペアワイズ比較
        for (int i = 0; i < drivers.Count; i++)
        {
            for (int j = i + 1; j < drivers.Count; j++)
            {
                var conflict = CheckCompatibility(drivers[i], drivers[j]);
                if (conflict != null)
                {
                    conflicts.Add(conflict);
                }
            }
        }

        // バージョン競合を検出
        var groupedByName = drivers.GroupBy(d => GetBaseDriverName(d.DriverName));
        foreach (var group in groupedByName)
        {
            if (group.Count() > 1)
            {
                // 同じドライバーの複数バージョンが存在
                var versions = group.Select(g => g.Version).Distinct().ToList();
                if (versions.Count > 1)
                {
                    conflicts.Add(new DriverConflict
                    {
                        Driver1 = group.First().DriverName,
                        Driver2 = group.Last().DriverName,
                        ConflictType = ConflictType.VersionMismatch,
                        Severity = ConflictSeverity.High,
                        Description = $"Multiple versions of {group.Key}: {string.Join(", ", versions)}",
                        RecommendedAction = $"Update all {group.Key} drivers to the same version"
                    });
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// 2つのドライバーの互換性をチェック
    /// </summary>
    private DriverConflict? CheckCompatibility(
        DriverSpecification driver1,
        DriverSpecification driver2)
    {
        // Chipset レベルの競合チェック
        if (driver1.ChipsetId != null && driver2.ChipsetId != null &&
            driver1.ChipsetId != driver2.ChipsetId)
        {
            // 同じハードウェアに異なるチップセットドライバー
            if (driver1.DeviceType == driver2.DeviceType &&
                driver1.HardwareId == driver2.HardwareId)
            {
                return new DriverConflict
                {
                    Driver1 = driver1.DriverName,
                    Driver2 = driver2.DriverName,
                    ConflictType = ConflictType.ChipsetMismatch,
                    Severity = ConflictSeverity.Critical,
                    Description = $"Chipset mismatch: {driver1.ChipsetId} vs {driver2.ChipsetId}",
                    RecommendedAction = "Uninstall one of the conflicting drivers"
                };
            }
        }

        // メモリ要件の競合
        if (driver1.RequiredMemoryMB > 0 && driver2.RequiredMemoryMB > 0)
        {
            var totalMemory = driver1.RequiredMemoryMB + driver2.RequiredMemoryMB;
            if (totalMemory > 4096) // 4GB以上必要
            {
                return new DriverConflict
                {
                    Driver1 = driver1.DriverName,
                    Driver2 = driver2.DriverName,
                    ConflictType = ConflictType.ResourceConstraint,
                    Severity = ConflictSeverity.High,
                    Description = $"Combined memory requirement: {totalMemory}MB may exceed available resources",
                    RecommendedAction = "Ensure sufficient system memory or optimize driver memory usage"
                };
            }
        }

        // 明示的な非互換性リスト
        if (IsExplicitlyIncompatible(driver1.DriverName, driver2.DriverName))
        {
            return new DriverConflict
            {
                Driver1 = driver1.DriverName,
                Driver2 = driver2.DriverName,
                ConflictType = ConflictType.ExplicitIncompatibility,
                Severity = ConflictSeverity.Critical,
                Description = "These drivers are known to be incompatible",
                RecommendedAction = "Consult vendor documentation for compatibility matrix"
            };
        }

        return null;
    }

    /// <summary>
    /// 解決策を提案
    /// </summary>
    private List<ResolutionSuggestion> SuggestResolutions(
        List<DriverConflict> conflicts,
        List<List<string>> cycles)
    {
        var suggestions = new List<ResolutionSuggestion>();

        // 各競合に対して解決策を提案
        foreach (var conflict in conflicts)
        {
            switch (conflict.ConflictType)
            {
                case ConflictType.VersionMismatch:
                    suggestions.Add(new ResolutionSuggestion
                    {
                        Priority = 1,
                        Action = "UpdateToCommonVersion",
                        Description = $"Update {conflict.Driver2} to match version of {conflict.Driver1}",
                        Confidence = 0.95
                    });
                    break;

                case ConflictType.ChipsetMismatch:
                    suggestions.Add(new ResolutionSuggestion
                    {
                        Priority = 0,
                        Action = "UninstallConflicting",
                        Description = $"Uninstall {conflict.Driver2} to resolve chipset conflict",
                        Confidence = 0.90
                    });
                    break;

                case ConflictType.ResourceConstraint:
                    suggestions.Add(new ResolutionSuggestion
                    {
                        Priority = 2,
                        Action = "OptimizeMemory",
                        Description = "Optimize driver memory usage or upgrade system RAM",
                        Confidence = 0.75
                    });
                    break;
            }
        }

        // 循環依存の解決
        foreach (var cycle in cycles)
        {
            suggestions.Add(new ResolutionSuggestion
            {
                Priority = 0,
                Action = "ReorderInstallation",
                Description = $"Reorder installation sequence to break cycle: {string.Join(" -> ", cycle)}",
                Confidence = 0.80
            });
        }

        // 優先度でソート
        return suggestions.OrderBy(s => s.Priority).ThenByDescending(s => s.Confidence).ToList();
    }

    /// <summary>
    /// 基本ドライバー名を取得
    /// </summary>
    private string GetBaseDriverName(string driverName)
    {
        // バージョン情報を削除
        var parts = driverName.Split(' ');
        return parts[0];
    }

    /// <summary>
    /// 明示的な非互換性をチェック
    /// </summary>
    private bool IsExplicitlyIncompatible(string driver1, string driver2)
    {
        // 既知の非互換組み合わせ
        var incompatiblePairs = new[]
        {
            ("NVIDIA Graphics", "AMD Radeon"),
            ("Intel Graphics", "Intel Integrated Graphics"),
        };

        foreach (var (drv1, drv2) in incompatiblePairs)
        {
            if ((driver1.Contains(drv1) && driver2.Contains(drv2)) ||
                (driver1.Contains(drv2) && driver2.Contains(drv1)))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// ドライバー仕様
/// </summary>
public class DriverSpecification
{
    public string DriverName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public string? ChipsetId { get; set; }
    public List<string>? Dependencies { get; set; }
    public int RequiredMemoryMB { get; set; }
}

/// <summary>
/// ドライバーノード（グラフ用）
/// </summary>
public class DriverNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<string> Dependencies { get; set; } = new();
    public List<string> IncomingEdges { get; set; } = new();
    public List<string> OutgoingEdges { get; set; } = new();
}

/// <summary>
/// ドライバー競合
/// </summary>
public class DriverConflict
{
    public string Driver1 { get; set; } = string.Empty;
    public string Driver2 { get; set; } = string.Empty;
    public ConflictType ConflictType { get; set; }
    public ConflictSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

/// <summary>
/// 競合型
/// </summary>
public enum ConflictType
{
    VersionMismatch = 0,
    ChipsetMismatch = 1,
    ResourceConstraint = 2,
    ExplicitIncompatibility = 3,
    DependencyConflict = 4
}

/// <summary>
/// 競合重大度
/// </summary>
public enum ConflictSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// 依存性解決結果
/// </summary>
public class DependencyResolutionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool HasCycles { get; set; }
    public bool HasConflicts { get; set; }
    public bool IsResolvable { get; set; }
    public List<List<string>> Cycles { get; set; } = new();
    public List<DriverConflict> Conflicts { get; set; } = new();
    public List<ResolutionSuggestion> ResolutionSuggestions { get; set; } = new();
    public DateTime AnalyzedAt { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// 解決策の提案
/// </summary>
public class ResolutionSuggestion
{
    public int Priority { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

/// <summary>
/// 互換性制約
/// </summary>
public class CompatibilityConstraint
{
    public string DriverName { get; set; } = string.Empty;
    public string MinimumVersion { get; set; } = string.Empty;
    public string MaximumVersion { get; set; } = string.Empty;
}
