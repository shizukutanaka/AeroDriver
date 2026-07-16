using System.Net.Http;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

/// <summary>
/// BackupService のロジックをテストします。ファイルシステムへの書き込みは一時ディレクトリで実施。
/// </summary>
public class BackupServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ISettingsService _settings;
    private readonly BackupService _sut;

    public BackupServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aerodriver_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _settings = Substitute.For<ISettingsService>();
        _settings.MaxBackupGenerations.Returns(3); // SettingsData.Default と同じ既定値

        _sut = new TestableBackupService(NullLogger<BackupService>.Instance, _settings, _tempRoot);
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

    // BackupDriverAsync は毎回自動でクリーンアップを走らせるが、以前は
    // ISettingsService.MaxBackupGenerations を無視してハードコードされた3世代を
    // 常に使っていた(ユーザーが設定を変更しても一切反映されないバグ)。
    // 実際に注入した設定値が使われることを検証する。
    [Fact]
    public async Task BackupDriverAsync_AutoCleanup_HonorsInjectedMaxBackupGenerationsSetting()
    {
        _settings.MaxBackupGenerations.Returns(2);
        var driver = MakeDriver();

        for (int i = 0; i < 5; i++)
        {
            await _sut.BackupDriverAsync(driver);
            await Task.Delay(5);
        }

        _sut.GetAvailableBackups(driver).Should().HaveCount(2);
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

    // --- RestoreDriverAsync: 脆弱ドライバー照合(ロールバック経由のバイパス防止) ---

    [Fact]
    public async Task RestoreDriverAsync_BackupContainsKnownVulnerableFile_BlocksBeforeReinstallAttempt()
    {
        var driver = MakeDriver();
        var logger = new RecordingLogger<BackupService>();
        var sut = await CreateBackupWithFilesAsync(driver, blocklistMatches: true, logger: logger);

        var result = await sut.RestoreDriverAsync(driver);

        result.Should().BeFalse();
        // ブロックリスト一致の警告が出ていること = pnputil を試す前にここで止まったことの証拠
        // (pnputil が無いテスト環境では通っても失敗するため、result==false だけでは
        // 「ブロックリストで拒否された」のか「pnputil起動失敗」なのか区別できない)
        logger.Messages.Should().Contain(m => m.Contains("既知の脆弱ドライバー"));
        logger.Messages.Should().NotContain(m => m.Contains("復元失敗（pnputil"));
    }

    [Fact]
    public async Task RestoreDriverAsync_NoVulnerableDriverBlocklistRegistered_ProceedsPastBlocklistCheck()
    {
        // ブロックリスト未登録(既定コンストラクタ相当)なら照合はスキップされ、
        // 通常どおり pnputil 呼び出しに進む(pnputilが無いテスト環境では最終的に失敗するが、
        // ブロックリストの警告ログが一切出ないことで「照合自体が実行されなかった」ことを確認する)
        var driver = MakeDriver();
        var logger = new RecordingLogger<BackupService>();
        var sut = await CreateBackupWithFilesAsync(driver, blocklistMatches: false, injectBlocklist: false, logger: logger);

        var result = await sut.RestoreDriverAsync(driver);

        result.Should().BeFalse();
        logger.Messages.Should().NotContain(m => m.Contains("既知の脆弱ドライバー"));
    }

    // バックアップの files/ 配下に実ファイルを作り、必要ならブロックリスト一致のハッシュを持たせる
    private async Task<BackupService> CreateBackupWithFilesAsync(
        DriverInfo driver, bool blocklistMatches, bool injectBlocklist = true, ILogger<BackupService>? logger = null)
    {
        await _sut.BackupDriverAsync(driver); // deviceDir とメタデータを作る

        var deviceDir = Directory.GetDirectories(_tempRoot).Single();
        var backupDir = Directory.GetDirectories(deviceDir, "backup_*").Single();
        var filesDir = Path.Combine(backupDir, "files");
        Directory.CreateDirectory(filesDir);
        await File.WriteAllTextAsync(Path.Combine(filesDir, "test.inf"), "; dummy inf");
        var driverFile = Path.Combine(filesDir, "test.sys");
        await File.WriteAllTextAsync(driverFile, "dummy driver bytes");

        VulnerableDriverBlocklist? blocklist = null;
        if (injectBlocklist)
        {
            var sha256 = blocklistMatches
                ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(driverFile)))
                : "0000000000000000000000000000000000000000000000000000000000000000";

            var cacheFile = Path.Combine(_tempRoot, $"blocklist_{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(cacheFile, $$"""
                [{"KnownVulnerableSamples":[{"SHA256":"{{sha256}}"}]}]
                """);
            File.SetLastWriteTimeUtc(cacheFile, DateTime.UtcNow);

            blocklist = new TestableVulnerableDriverBlocklist(
                NullLogger<VulnerableDriverBlocklist>.Instance,
                new HttpClient(new NotImplementedHandler()),
                cacheFile);
        }

        return new TestableBackupService(
            logger ?? NullLogger<BackupService>.Instance, _settings, _tempRoot, blocklist);
    }

    // ログメッセージを検証用に記録するだけの最小実装
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class TestableVulnerableDriverBlocklist : VulnerableDriverBlocklist
    {
        public TestableVulnerableDriverBlocklist(
            Microsoft.Extensions.Logging.ILogger<VulnerableDriverBlocklist> logger,
            HttpClient client,
            string cacheFile)
            : base(logger, client, cacheFile) { }
    }

    private sealed class NotImplementedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new NotImplementedException("テスト中はHTTPを呼んではいけません");
    }

    // テスト用サブクラス: バックアップルートを一時ディレクトリに向ける
    private sealed class TestableBackupService : BackupService
    {
        public TestableBackupService(
            Microsoft.Extensions.Logging.ILogger<BackupService> logger,
            ISettingsService settings,
            string backupRoot,
            VulnerableDriverBlocklist? vulnerableDriverBlocklist = null)
            : base(logger, settings, backupRoot, vulnerableDriverBlocklist) { }
    }
}
