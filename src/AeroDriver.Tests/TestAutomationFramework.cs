// This file has been created as part of test automation feature implementation
// It provides automated test execution, result collection, and reporting capabilities

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace AeroDriver.Tests;

/// <summary>
/// テスト自動化フレームワーク
/// テストの発見、実行、結果収集、レポート生成を自動化
/// </summary>
public static class TestAutomationFramework
{
    private static readonly List<TestResult> _testResults = new();
    private static readonly object _resultsLock = new();
    private static TestExecutionContext? _currentContext;
    private static readonly Timer _progressTimer;
    private static bool _isRunning;

    static TestAutomationFramework()
    {
        _progressTimer = new Timer(_ => ReportProgress(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 自動テスト実行を開始
    /// </summary>
    public static async Task<TestExecutionResult> RunAutomatedTestsAsync(TestExecutionOptions options = null)
    {
        options ??= TestExecutionOptions.Default;

        if (_isRunning)
            throw new InvalidOperationException("Test execution is already in progress");

        _isRunning = true;
        var startTime = DateTime.UtcNow;

        try
        {
            _currentContext = new TestExecutionContext
            {
                StartTime = startTime,
                Options = options,
                TestAssembly = typeof(TestAutomationFramework).Assembly
            };

            // テストの発見
            var testCases = await DiscoverTestsAsync(options);

            if (!testCases.Any())
            {
                return new TestExecutionResult
                {
                    Success = true,
                    Message = "No tests found",
                    ExecutionTime = TimeSpan.Zero
                };
            }

            // プログレスレポートを開始
            if (options.ReportProgress)
            {
                _progressTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));
            }

            // テスト実行
            var executionResult = await ExecuteTestsAsync(testCases, options);

            // 結果レポート生成
            var report = GenerateTestReport(executionResult, startTime);

            // レポート保存
            if (!string.IsNullOrEmpty(options.ReportFilePath))
            {
                await SaveTestReportAsync(report, options.ReportFilePath);
            }

            return new TestExecutionResult
            {
                Success = executionResult.Failed == 0,
                Message = $"Tests completed: {executionResult.Passed} passed, {executionResult.Failed} failed, {executionResult.Skipped} skipped",
                ExecutionTime = DateTime.UtcNow - startTime,
                TestReport = report
            };
        }
        finally
        {
            _isRunning = false;
            _progressTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _currentContext = null;
        }
    }

    /// <summary>
    /// テストケースを発見
    /// </summary>
    private static async Task<List<TestCaseInfo>> DiscoverTestsAsync(TestExecutionOptions options)
    {
        var testCases = new List<TestCaseInfo>();

        try
        {
            var assembly = typeof(TestAutomationFramework).Assembly;
            var testTypes = assembly.GetTypes()
                .Where(t => t.GetMethods().Any(m => m.GetCustomAttributes(typeof(FactAttribute), false).Any() ||
                                                  m.GetCustomAttributes(typeof(TheoryAttribute), false).Any()))
                .ToList();

            foreach (var testType in testTypes)
            {
                // フィルタ適用
                if (!string.IsNullOrEmpty(options.TestClassFilter) &&
                    !testType.FullName!.Contains(options.TestClassFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var methods = testType.GetMethods()
                    .Where(m => m.GetCustomAttributes(typeof(FactAttribute), false).Any() ||
                               m.GetCustomAttributes(typeof(TheoryAttribute), false).Any())
                    .ToList();

                foreach (var method in methods)
                {
                    // フィルタ適用
                    if (!string.IsNullOrEmpty(options.TestMethodFilter) &&
                        !method.Name.Contains(options.TestMethodFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var testCase = new TestCaseInfo
                    {
                        TestClass = testType,
                        TestMethod = method,
                        TestName = $"{testType.FullName}.{method.Name}",
                        Categories = GetTestCategories(method),
                        IsTheory = method.GetCustomAttributes(typeof(TheoryAttribute), false).Any()
                    };

                    testCases.Add(testCase);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Test discovery error: {ex.Message}");
        }

        return testCases;
    }

    /// <summary>
    /// テストを実行
    /// </summary>
    private static async Task<TestExecutionSummary> ExecuteTestsAsync(List<TestCaseInfo> testCases, TestExecutionOptions options)
    {
        var summary = new TestExecutionSummary();
        var semaphore = new SemaphoreSlim(options.MaxParallelTests);

        var tasks = testCases.Select(async testCase =>
        {
            await semaphore.WaitAsync();

            try
            {
                var result = await ExecuteSingleTestAsync(testCase, options);
                lock (_resultsLock)
                {
                    _testResults.Add(result);
                    UpdateSummary(summary, result);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return summary;
    }

    /// <summary>
    /// 単一のテストを実行
    /// </summary>
    private static async Task<TestResult> ExecuteSingleTestAsync(TestCaseInfo testCase, TestExecutionOptions options)
    {
        var result = new TestResult
        {
            TestName = testCase.TestName,
            TestClass = testCase.TestClass.FullName!,
            TestMethod = testCase.TestMethod.Name,
            StartTime = DateTime.UtcNow,
            Categories = testCase.Categories
        };

        try
        {
            // テストインスタンス作成
            var instance = Activator.CreateInstance(testCase.TestClass);

            // タイムアウト付き実行
            var executionTask = ExecuteTestMethodAsync(instance, testCase.TestMethod);

            if (options.TestTimeout > TimeSpan.Zero)
            {
                var timeoutTask = Task.Delay(options.TestTimeout);
                var completedTask = await Task.WhenAny(executionTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    result.Status = TestStatus.Timeout;
                    result.Message = $"Test timed out after {options.TestTimeout}";
                    result.EndTime = DateTime.UtcNow;
                    return result;
                }
            }

            await executionTask;
            result.Status = TestStatus.Passed;
            result.Message = "Test passed";
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Failed;
            result.Message = ex.Message;
            result.Exception = ex;
            result.StackTrace = ex.StackTrace;
        }

        result.EndTime = DateTime.UtcNow;
        result.Duration = result.EndTime - result.StartTime;

        return result;
    }

    /// <summary>
    /// テストメソッドを実行
    /// </summary>
    private static async Task ExecuteTestMethodAsync(object instance, MethodInfo method)
    {
        var task = method.Invoke(instance, null) as Task;
        if (task != null)
        {
            await task;
        }
    }

    /// <summary>
    /// テストカテゴリを取得
    /// </summary>
    private static List<string> GetTestCategories(MethodInfo method)
    {
        var categories = new List<string>();

        // Trait属性からカテゴリを取得
        var traits = method.GetCustomAttributes<TraitAttribute>();
        foreach (var trait in traits)
        {
            if (trait.Name == "Category")
            {
                categories.Add(trait.Value);
            }
        }

        return categories;
    }

    /// <summary>
    /// 要約を更新
    /// </summary>
    private static void UpdateSummary(TestExecutionSummary summary, TestResult result)
    {
        switch (result.Status)
        {
            case TestStatus.Passed:
                summary.Passed++;
                break;
            case TestStatus.Failed:
                summary.Failed++;
                break;
            case TestStatus.Timeout:
            case TestStatus.Error:
                summary.Failed++;
                break;
            case TestStatus.Skipped:
                summary.Skipped++;
                break;
        }
    }

    /// <summary>
    /// テストレポートを生成
    /// </summary>
    private static TestReport GenerateTestReport(TestExecutionSummary execution, DateTime startTime)
    {
        var report = new TestReport
        {
            GeneratedAt = DateTime.UtcNow,
            ExecutionStartTime = startTime,
            ExecutionEndTime = DateTime.UtcNow,
            TotalTests = execution.Total,
            PassedTests = execution.Passed,
            FailedTests = execution.Failed,
            SkippedTests = execution.Skipped,
            ExecutionTime = DateTime.UtcNow - startTime,
            TestResults = _testResults.ToList()
        };

        // 統計計算
        report.SuccessRate = report.TotalTests > 0 ? (double)report.PassedTests / report.TotalTests * 100 : 0;

        // カテゴリ別統計
        report.CategoryStatistics = report.TestResults
            .SelectMany(r => r.Categories.Select(c => new { Category = c, Result = r }))
            .GroupBy(x => x.Category)
            .ToDictionary(
                g => g.Key,
                g => new CategoryStats
                {
                    Total = g.Count(),
                    Passed = g.Count(x => x.Result.Status == TestStatus.Passed),
                    Failed = g.Count(x => x.Result.Status != TestStatus.Passed)
                });

        // パフォーマンス統計
        var completedTests = report.TestResults.Where(r => r.Status != TestStatus.Skipped).ToList();
        if (completedTests.Any())
        {
            report.AverageTestTime = TimeSpan.FromMilliseconds(
                completedTests.Average(r => r.Duration.TotalMilliseconds));
            report.MaxTestTime = completedTests.Max(r => r.Duration);
            report.MinTestTime = completedTests.Min(r => r.Duration);
        }

        return report;
    }

    /// <summary>
    /// テストレポートを保存
    /// </summary>
    private static async Task SaveTestReportAsync(TestReport report, string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save test report: {ex.Message}");
        }
    }

    /// <summary>
    /// プログレスをレポート
    /// </summary>
    private static void ReportProgress()
    {
        if (_currentContext == null) return;

        lock (_resultsLock)
        {
            var completed = _testResults.Count;
            var total = _currentContext?.TotalTests ?? 0;
            var progress = total > 0 ? (double)completed / total * 100 : 0;

            Debug.WriteLine($"Test Progress: {completed}/{total} ({progress:F1}%) completed");
        }
    }

    /// <summary>
    /// テスト実行オプション
    /// </summary>
    public class TestExecutionOptions
    {
        public static readonly TestExecutionOptions Default = new();

        public string? TestClassFilter { get; set; }
        public string? TestMethodFilter { get; set; }
        public int MaxParallelTests { get; set; } = Environment.ProcessorCount;
        public TimeSpan TestTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public bool ReportProgress { get; set; } = true;
        public string? ReportFilePath { get; set; }
        public bool IncludeStackTraces { get; set; } = true;
    }

    /// <summary>
    /// テスト実行結果
    /// </summary>
    public class TestExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public TimeSpan ExecutionTime { get; set; }
        public TestReport? TestReport { get; set; }
    }

    /// <summary>
    /// テスト実行コンテキスト
    /// </summary>
    private class TestExecutionContext
    {
        public DateTime StartTime { get; set; }
        public TestExecutionOptions Options { get; set; } = null!;
        public Assembly TestAssembly { get; set; } = null!;
        public int TotalTests => 0; // 実際には動的に計算
    }

    /// <summary>
    /// テストケース情報
    /// </summary>
    private class TestCaseInfo
    {
        public Type TestClass { get; set; } = null!;
        public MethodInfo TestMethod { get; set; } = null!;
        public string TestName { get; set; } = string.Empty;
        public List<string> Categories { get; set; } = new();
        public bool IsTheory { get; set; }
    }

    /// <summary>
    /// テスト実行サマリー
    /// </summary>
    private class TestExecutionSummary
    {
        public int Total => Passed + Failed + Skipped;
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
    }

    /// <summary>
    /// テスト結果
    /// </summary>
    public class TestResult
    {
        public string TestName { get; set; } = string.Empty;
        public string TestClass { get; set; } = string.Empty;
        public string TestMethod { get; set; } = string.Empty;
        public TestStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public Exception? Exception { get; set; }
        public string? StackTrace { get; set; }
        public List<string> Categories { get; set; } = new();
    }

    /// <summary>
    /// テストステータス
    /// </summary>
    public enum TestStatus
    {
        None,
        Passed,
        Failed,
        Skipped,
        Timeout,
        Error
    }

    /// <summary>
    /// テストレポート
    /// </summary>
    public class TestReport
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime ExecutionStartTime { get; set; }
        public DateTime ExecutionEndTime { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public int SkippedTests { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan AverageTestTime { get; set; }
        public TimeSpan MaxTestTime { get; set; }
        public TimeSpan MinTestTime { get; set; }
        public Dictionary<string, CategoryStats> CategoryStatistics { get; set; } = new();
        public List<TestResult> TestResults { get; set; } = new();
    }

    /// <summary>
    /// カテゴリ統計
    /// </summary>
    public class CategoryStats
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
    }
}
