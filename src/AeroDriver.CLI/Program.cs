using AeroDriver.CLI.Core;
using AeroDriver.Core.Configuration;

namespace AeroDriver.CLI;

/// <summary>
/// エントリポイント（Carmack式：パフォーマンス重視、実用的）
/// - 高速起動
/// - メモリ効率
/// - 最小限の初期化
/// </summary>
public class Program
{
    /// <summary>
    /// メインエントリポイント（Carmack式：パフォーマンス最適化）
    /// </summary>
    private static async Task<int> Main(string[] args)
    {
        // Carmack式：高速化設定を最初に適用
        PerformanceSettings.ApplyOptimizations();
        
        // Carmack式：バックグラウンドメモリ管理（メモリリーク防止）
        _ = Task.Run(MemoryMaintenanceLoop);
        
        // 最適化されたエントリーポイントに処理を委譲
        return await ProgramOptimized.Main(args);
    }
    
    /// <summary>
    /// バックグラウンドメモリ管理（Carmack式：定期的GC最適化）
    /// </summary>
    private static async Task MemoryMaintenanceLoop()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(10));
            PerformanceSettings.CheckAndCleanupMemory(150 * 1024 * 1024);
        }
    }








}
