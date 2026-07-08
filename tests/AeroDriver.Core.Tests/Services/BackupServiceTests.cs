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

    // --- パストラバーサル対策 ---
    // Path.GetInvalidFileNameChars() には '.' が含まれないため、DeviceID が ".." そのものの
    // 場合、区切り文字を含まないためサニタイズ処理を素通りしバックアップルート外を
    // 指してしまう(スラッシュを含む入力は Split 処理自体で区切り文字が失われ、結果的に
    // 安全な文字列へ変換されるため、素の ".." のみが実際に危険なケースとなる)。

    [Fact]
    public async Task BackupDriverAsync_BareParentDirDeviceId_ThrowsInsteadOfEscapingBackupRoot()
    {
        Func<Task> act = () => _sut.BackupDriverAsync(new DriverInfo { DeviceID = ".." });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void HasBackup_BareParentDirDeviceId_ThrowsInsteadOfEscapingBackupRoot()
    {
        Action act = () => _sut.HasBackup(new DriverInfo { DeviceID = ".." });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task BackupDriverAsync_SlashContainingDeviceId_StaysWithinBackupRoot()
    {
        // スラッシュ等の区切り文字を含む入力は Split+Concat で区切りが失われ、
        // 意図せず変な名前のフォルダにはなるが、バックアップルート外には出ない
        var driver = new DriverInfo { DeviceID = "../../etc" };

        var result = await _sut.BackupDriverAsync(driver);

        result.Should().BeTrue();
        Directory.GetDirectories(_tempRoot, "*", SearchOption.AllDirectories)
            .Should().OnlyContain(d => d.StartsWith(_tempRoot));
    }

    // RestoreDriverAsync の backupVersion にも同種のパストラバーサルがあった:
    // "backup_" プレフィックスは先頭セグメントが単独の ".." になることは防ぐが、
    // backupVersion 内部に埋め込まれた "../" までは防げない
    // (例: "../../../../Windows/System32" が deviceDir の外を指してしまう)。

    [Fact]
    public async Task RestoreDriverAsync_BackupVersionWithEmbeddedTraversal_ThrowsInsteadOfEscaping()
    {
        var driver = MakeDriver();
        await _sut.BackupDriverAsync(driver); // deviceDir を実在させる

        Func<Task> act = () => _sut.RestoreDriverAsync(driver, "../../../../etc");

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
