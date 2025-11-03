using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;

namespace AeroDriver.Core;

/// <summary>
/// 軽量WMI接続プール（メモリ効率重視）
/// </summary>
[SupportedOSPlatform("windows")]
public static class WmiConnectionPool
{
    private static readonly ConcurrentDictionary<string, ScopeEntry> _scopes = new();
    private static readonly object _lock = new();
    private static DateTime _lastCleanup = DateTime.Now;
    private static readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _scopeTimeout = TimeSpan.FromMinutes(10);

    private class ScopeEntry
    {
        public ManagementScope Scope { get; set; }
        public DateTime LastUsed { get; set; }
        public int UsageCount { get; set; }

        public ScopeEntry(ManagementScope scope)
        {
            Scope = scope;
            LastUsed = DateTime.Now;
            UsageCount = 1;
        }
    }

    /// <summary>
    /// 高速WMI範囲取得（プール使用）
    /// </summary>
    public static ManagementScope GetScope(string namespacePath = @"\\.\root\cimv2")
    {
        // 定期クリーンアップ
        if (DateTime.Now - _lastCleanup > _cleanupInterval)
        {
            CleanupConnections();
        }

        var entry = _scopes.AddOrUpdate(
            namespacePath,
            path =>
            {
                var scope = new ManagementScope(path);
                try
                {
                    scope.Connect();
                }
                catch
                {
                    // 接続失敗時は新しいスコープを返す
                    scope = new ManagementScope(path);
                }
                return new ScopeEntry(scope);
            },
            (path, existingEntry) =>
            {
                existingEntry.LastUsed = DateTime.Now;
                existingEntry.UsageCount++;

                // Reconnect if scope is disconnected
                if (!existingEntry.Scope.IsConnected)
                {
                    try
                    {
                        existingEntry.Scope.Connect();
                    }
                    catch
                    {
                        // Create new scope on connection failure
                        var newScope = new ManagementScope(path);
                        try
                        {
                            newScope.Connect();
                        }
                        catch
                        {
                            // Return disconnected scope, will retry later
                        }
                        return new ScopeEntry(newScope);
                    }
                }

                return existingEntry;
            });

        return entry.Scope;
    }

    /// <summary>
    /// 高速WMIクエリ実行
    /// </summary>
    public static ManagementObjectCollection ExecuteQuery(string query, string namespacePath = @"\\.\root\cimv2")
    {
        var scope = GetScope(namespacePath);
        var objectQuery = new ObjectQuery(query);
        using var searcher = new ManagementObjectSearcher(scope, objectQuery);
        return searcher.Get();
    }

    /// <summary>
    /// 軽量接続クリーンアップ
    /// </summary>
    public static void CleanupConnections()
    {
        lock (_lock)
        {
            try
            {
                var now = DateTime.Now;

                // Remove stale connections (not used in last 10 minutes)
                var staleKeys = _scopes
                    .Where(kvp => now - kvp.Value.LastUsed > _scopeTimeout)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in staleKeys)
                {
                    if (_scopes.TryRemove(key, out var entry))
                    {
                        // Properly dispose of scope resources if needed
                        try
                        {
                            // ManagementScope doesn't implement IDisposable
                            // Just clear the reference
                            entry.Scope = null;
                        }
                        catch
                        {
                            // Ignore disposal errors
                        }
                    }
                }

                // スマートクリーンアップ：メモリ使用量に基づく
                var process = Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / (1024 * 1024);

                // 100MB以上使用時は積極的にクリーンアップ
                int maxConnections = memoryMB > 100 ? 5 : 15;

                if (_scopes.Count > maxConnections)
                {
                    // Remove least recently used connections
                    var lruKeys = _scopes
                        .OrderBy(kvp => kvp.Value.LastUsed)
                        .Take(_scopes.Count - maxConnections)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in lruKeys)
                    {
                        if (_scopes.TryRemove(key, out var entry))
                        {
                            entry.Scope = null;
                        }
                    }
                }

                _lastCleanup = now;

                // メモリプレッシャーが高い場合のみGen0 GCを実行
                if (memoryMB > 200)
                {
                    GC.Collect(0, GCCollectionMode.Optimized, false);
                }
                else if (memoryMB > 300)
                {
                    // 非常に高いメモリ使用時のみGen1まで実行
                    GC.Collect(1, GCCollectionMode.Optimized, false);
                }
            }
            catch
            {
                // クリーンアップ失敗は無視
            }
        }
    }

    /// <summary>
    /// 全接続強制クリア
    /// </summary>
    public static void ClearAll()
    {
        lock (_lock)
        {
            foreach (var entry in _scopes.Values)
            {
                try
                {
                    // Clear scope reference
                    entry.Scope = null;
                }
                catch { }
            }
            _scopes.Clear();
            _lastCleanup = DateTime.Now;
        }
    }

    /// <summary>
    /// 接続統計取得
    /// </summary>
    public static (int TotalConnections, int ActiveConnections, DateTime LastCleanup) GetStatistics()
    {
        lock (_lock)
        {
            var activeConnections = _scopes.Count(kvp => kvp.Value.Scope?.IsConnected == true);
            return (_scopes.Count, activeConnections, _lastCleanup);
        }
    }
}