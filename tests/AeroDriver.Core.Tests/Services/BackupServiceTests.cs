using AeroDriver.Core.Models;
using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

/// <summary>
/// BackupService のロジックをテストします。ファイルシステムへの書き込みは一時ディレクトリで実施。
/// </summary>
public class BackupServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly BackupService _sut;

    public BackupServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aerodriver_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _sut = new TestableBackupService(NullLogger<BackupService>.Instance, _tempRoot);
    }

    public void Dispose() => Directory.Delete(_tempRoot, true);

    private static DriverInfo MakeDriver(string deviceId = "PCI\\VEN_8086&DEV_0001")
        => new() { DeviceID = deviceId, DeviceName = "Test Device", DriverVersion = "1.0.0.0" };

    // --- HasBackup ---

    [Fact]
    public void HasBackup_NoBakedUp_ReturnsFalse()
    {
        _sut.HasBackup(MakeDriver()).Should().BeFalse();
    }

    [Fact]
    public async Task HasBackup_AfterBackup_ReturnsTrue()
    {
        var driver = MakeDriver();
        await _sut.BackupDriverAsync(driver);
        _sut.HasBackup(driver).Should().BeTrue();
    }

    // --- GetAvailableBackups ---

    [Fact]
    public async Task GetAvailableBackups_ReturnsTimestampSortedDescending()
    {
        var driver = MakeDriver();
        await _sut.BackupDriverAsync(driver);
        await Task.Delay(10); // タイムスタンプが異なるよう少し待つ
        await _sut.BackupDriverAsync(driver);

        var backups = _sut.GetAvailableBackups(driver);
        backups.Should().HaveCount(2);
        // 降順（新しい順）を確認
        string.CompareOrdinal(backups[0], backups[1]).Should().BePositive();
    }

    // --- CleanupOldBackupsAsync ---

    [Fact]
    public async Task CleanupOldBackupsAsync_ExcessBackupsAreRemoved()
    {
        var driver = MakeDriver();
        for (int i = 0; i < 5; i++)
        {
            await _sut.BackupDriverAsync(driver);
            await Task.Delay(5);
        }

        await _sut.CleanupOldBackupsAsync(3);

        _sut.GetAvailableBackups(driver).Should().HaveCount(3);
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_WithinLimit_NothingRemoved()
    {
        var driver = MakeDriver();
        await _sut.BackupDriverAsync(driver);
        await _sut.CleanupOldBackupsAsync(3);
        _sut.GetAvailableBackups(driver).Should().HaveCount(1);
    }

    // --- RestoreDriverAsync ---

    [Fact]
    public async Task RestoreDriverAsync_NoBackup_ReturnsFalse()
    {
        var result = await _sut.RestoreDriverAsync(MakeDriver("NOBACKUP\\DEV_0000"));
        result.Should().BeFalse();
    }

    // pnputil /export-driver が利用できない環境（テスト実行機に対象 INF がない、または非Windows）では
    // バックアップはメタデータのみとなり、実ファイルが伴わない復元は安全側に倒して false を返すべき。
    [Fact]
    public async Task RestoreDriverAsync_MetadataOnlyBackup_ReturnsFalse()
    {
        var driver = MakeDriver();
        await _sut.BackupDriverAsync(driver);
        var result = await _sut.RestoreDriverAsync(driver);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreDriverAsync_SpecificVersion_MetadataOnly_ReturnsFalse()
    {
        var driver = MakeDriver();
        await _sut.BackupDriverAsync(driver);
        var versions = _sut.GetAvailableBackups(driver);
        versions.Should().HaveCount(1);

        var result = await _sut.RestoreDriverAsync(driver, versions[0]);
        result.Should().BeFalse();
    }

    // --- ArgumentNullException ガード ---

    [Fact]
    public void HasBackup_NullDriver_Throws()
    {
        Action act = () => _sut.HasBackup(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task BackupDriverAsync_EmptyDeviceId_Throws()
    {
        Func<Task> act = () => _sut.BackupDriverAsync(new DriverInfo { DeviceID = "" });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // テスト用サブクラス: バックアップルートを一時ディレクトリに向ける
    private sealed class TestableBackupService : BackupService
    {
        public TestableBackupService(
            Microsoft.Extensions.Logging.ILogger<BackupService> logger,
            string backupRoot)
            : base(logger, backupRoot) { }
    }
}
