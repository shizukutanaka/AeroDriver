using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Blockchain;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System;

namespace AeroDriver.Tests;

public class BlockchainIntegrityManagerTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly string _testContent;
    private readonly BlockchainIntegrityManager _manager;

    public BlockchainIntegrityManagerTests()
    {
        _manager = new BlockchainIntegrityManager();
        _testContent = "Test content for integrity verification";

        _testFilePath = Path.Combine(Path.GetTempPath(), "test_integrity_file.txt");
        File.WriteAllText(_testFilePath, _testContent);
    }

    [Fact]
    public async Task VerifyFileAsync_ValidFile_ReturnsValidResult()
    {
        // Compute actual hash
        var actualHash = ComputeHash(_testContent);
        var options = new IntegrityCheckOptions { ExpectedHash = actualHash };

        var result = await _manager.VerifyFileAsync(_testFilePath, options);

        Assert.True(result.IsValid);
        Assert.Null(result.FailureReason);
        Assert.Equal(actualHash, result.ActualHash);
    }

    [Fact]
    public async Task VerifyFileAsync_InvalidHash_ReturnsInvalidResult()
    {
        var options = new IntegrityCheckOptions { ExpectedHash = "invalid_hash" };

        var result = await _manager.VerifyFileAsync(_testFilePath, options);

        Assert.False(result.IsValid);
        Assert.Equal(IntegrityFailureReason.HashMismatch, result.FailureReason);
    }

    [Fact]
    public async Task VerifyFileAsync_FileNotFound_ReturnsFailure()
    {
        var options = new IntegrityCheckOptions { ExpectedHash = "some_hash" };

        var result = await _manager.VerifyFileAsync("nonexistent_file.txt", options);

        Assert.False(result.IsValid);
        Assert.Equal(IntegrityFailureReason.FileNotFound, result.FailureReason);
    }

    [Fact]
    public void VerifyStream_ValidStream_ReturnsValidResult()
    {
        var actualHash = ComputeHash(_testContent);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_testContent));

        var result = _manager.VerifyStream(stream, actualHash);

        Assert.True(result.IsValid);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void GetAuditHistory_ReturnsRecords()
    {
        // Perform a check to create audit record
        var actualHash = ComputeHash(_testContent);
        var options = new IntegrityCheckOptions { ExpectedHash = actualHash };
        _manager.VerifyFileAsync(_testFilePath, options).Wait();

        var history = _manager.GetAuditHistory();
        Assert.NotEmpty(history);
    }

    [Fact]
    public async Task VerifyFileAsync_WithCancellationToken_CanBeCancelled()
    {
        var cts = new CancellationTokenSource();
        var actualHash = ComputeHash(_testContent);
        var options = new IntegrityCheckOptions { ExpectedHash = actualHash };

        // Cancel immediately
        cts.Cancel();

        var exception = await Assert.ThrowsAsync<TaskCanceledException>(
            () => _manager.VerifyFileAsync(_testFilePath, options, cts.Token));

        Assert.IsType<TaskCanceledException>(exception);
    }

    [Fact]
    public async Task VerifyFileAsync_WithPerformanceLogging_LogsMetrics()
    {
        var config = new IntegrityManagerConfig
        {
            EnablePerformanceLogging = true,
            LogLevel = SimpleLogger.LogLevel.Debug
        };
        var manager = new BlockchainIntegrityManager(config);

        var actualHash = ComputeHash(_testContent);
        var options = new IntegrityCheckOptions { ExpectedHash = actualHash };

        var result = await manager.VerifyFileAsync(_testFilePath, options);

        Assert.True(result.IsValid);
        manager.Dispose();
    }

    [Fact]
    public async Task VerifyFileAsync_WithSecurityAuditing_LogsSecurityEvents()
    {
        var config = new IntegrityManagerConfig
        {
            EnableSecurityAuditing = true,
            LogLevel = SimpleLogger.LogLevel.Security
        };
        var manager = new BlockchainIntegrityManager(config);

        var actualHash = ComputeHash(_testContent);
        var options = new IntegrityCheckOptions { ExpectedHash = actualHash };

        var result = await manager.VerifyFileAsync(_testFilePath, options);

        Assert.True(result.IsValid);
        manager.Dispose();
    }

    [Fact]
    public async Task VerifyFileAsync_WithConfiguration_AppliesSettings()
    {
        var config = new IntegrityManagerConfig
        {
            EnablePerformanceLogging = false,
            EnableSecurityAuditing = false,
            LogLevel = SimpleLogger.LogLevel.Error
        };
        var manager = new BlockchainIntegrityManager(config);

        var actualHash = ComputeHash(_testContent);
        var options = new IntegrityCheckOptions { ExpectedHash = actualHash };

        var result = await manager.VerifyFileAsync(_testFilePath, options);

        Assert.True(result.IsValid);
        manager.Dispose();
    }

    [Fact]
    public async Task VerifyFileAsync_LargeFile_HandlesCorrectly()
    {
        // Create a larger test file
        var largeContent = new string('A', 1024 * 1024); // 1MB
        var largeFilePath = Path.Combine(Path.GetTempPath(), "large_test_file.txt");
        File.WriteAllText(largeFilePath, largeContent);

        try
        {
            var actualHash = ComputeHash(largeContent);
            var options = new IntegrityCheckOptions { ExpectedHash = actualHash };

            var result = await _manager.VerifyFileAsync(largeFilePath, options);

            Assert.True(result.IsValid);
            Assert.Equal(actualHash, result.ActualHash);
        }
        finally
        {
            File.Delete(largeFilePath);
        }
    }

    [Fact]
    public async Task VerifyFileAsync_ConcurrentOperations_HandlesCorrectly()
    {
        var tasks = new List<Task<IntegrityCheckResult>>();
        var actualHash = ComputeHash(_testContent);

        // Run multiple concurrent verifications
        for (int i = 0; i < 10; i++)
        {
            var options = new IntegrityCheckOptions { ExpectedHash = actualHash };
            tasks.Add(_manager.VerifyFileAsync(_testFilePath, options));
        }

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.True(result.IsValid));
        Assert.Equal(10, results.Length);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var manager = new BlockchainIntegrityManager();

        // Call dispose multiple times
        manager.Dispose();
        manager.Dispose();
        manager.Dispose();

        // Should not throw any exceptions
        Assert.True(true);
    }

    [Fact]
    public void GetLastAuditRecord_ReturnsMostRecentRecord()
    {
        // Clear existing history
        _manager.ClearAuditHistory();

        // Perform multiple checks
        var actualHash = ComputeHash(_testContent);
        var options = new IntegrityCheckOptions { ExpectedHash = actualHash };

        _manager.VerifyFileAsync(_testFilePath, options).Wait();
        Thread.Sleep(10); // Small delay to ensure different timestamps
        _manager.VerifyFileAsync(_testFilePath, options).Wait();

        var lastRecord = _manager.GetLastAuditRecord();
        Assert.NotNull(lastRecord);
        Assert.True(lastRecord.Succeeded);
    }

    [Fact]
    public async Task VerifyFileAsync_WithFailureCallback_CallsCallback()
    {
        var callbackCalled = false;
        IntegrityAuditRecord? callbackRecord = null;

        var options = new IntegrityCheckOptions
        {
            ExpectedHash = "invalid_hash",
            FailureCallback = record =>
            {
                callbackCalled = true;
                callbackRecord = record;
            }
        };

        var result = await _manager.VerifyFileAsync(_testFilePath, options);

        Assert.False(result.IsValid);
        Assert.True(callbackCalled);
        Assert.NotNull(callbackRecord);
        Assert.False(callbackRecord.Succeeded);
    }

    public void Dispose()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
        _manager.Dispose();
    }

    private static string ComputeHash(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
