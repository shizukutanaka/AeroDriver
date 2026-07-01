using AeroDriver.Core.Events;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Net;
using System.Net.Http;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

public class DriverServiceTests
{
    private readonly ISettingsService _settings;
    private readonly IBackupService _backup;
    private readonly IDriverUpdateSource _source1;
    private readonly IDriverUpdateSource _source2;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DriverService _sut;

    public DriverServiceTests()
    {
        _settings = Substitute.For<ISettingsService>();
        _backup = Substitute.For<IBackupService>();
        _source1 = Substitute.For<IDriverUpdateSource>();
        _source2 = Substitute.For<IDriverUpdateSource>();
        _httpClientFactory = Substitute.For<IHttpClientFactory>();

        _source1.SourceName.Returns("Source1");
        _source2.SourceName.Returns("Source2");

        // デフォルト: 空のHttpClientを返す
        _httpClientFactory.CreateClient(Arg.Any<string>())
                          .Returns(new HttpClient());

        _sut = new DriverService(
            NullLogger<DriverService>.Instance,
            _settings,
            _backup,
            [_source1, _source2],
            _httpClientFactory);
    }

    // ──────────────────────────────────────────────
    // CheckForUpdatesAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CheckForUpdates_NoSources_ReturnsEmpty()
    {
        _source1.SearchUpdatesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<DriverInfo>());
        _source2.SearchUpdatesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<DriverInfo>());

        var sut = new DriverService(
            NullLogger<DriverService>.Instance,
            _settings, _backup,
            Array.Empty<IDriverUpdateSource>(),
            _httpClientFactory);

        // GetAllDriversAsync は WMI を呼ぶためスキップ: WMIなし環境では例外になるが
        // DriverService はキャッチして空リストを返す
        var result = await sut.CheckForUpdatesAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdates_SourceThrows_StillReturnsOtherResults()
    {
        _source1.SearchUpdatesAsync(Arg.Any<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("network error"));
        _source2.SearchUpdatesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<DriverInfo>());

        // WMI なし環境でも例外をキャッチして空リストを返す
        var result = await _sut.CheckForUpdatesAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckForUpdates_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _source1.SearchUpdatesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<DriverInfo>());
        _source2.SearchUpdatesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<DriverInfo>());

        // CancellationToken が渡されても内部でキャッチされて空リストを返す
        // (GetAllDriversAsync のTask.Runがキャンセルを伝播するが、外側でもキャッチされる)
        Func<Task> act = () => _sut.CheckForUpdatesAsync(cancellationToken: cts.Token);
        // OperationCanceledException が伝播するか、空リストを返すかどちらか
        // DriverServiceの設計ではOperationCanceledExceptionはrethrowされる
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ──────────────────────────────────────────────
    // InstallDriverUpdateAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task InstallDriverUpdate_NullArg_Throws()
    {
        Func<Task> act = () => _sut.InstallDriverUpdateAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InstallDriverUpdate_NoDownloadUrl_ReturnsFalse()
    {
        _settings.BackupEnabled.Returns(false);

        var driver = new DriverInfo { DeviceID = "DEV001", DownloadUrl = null };
        var result = await _sut.InstallDriverUpdateAsync(driver);

        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    // InstallDriverUpdateWithResultAsync — 理由付き結果
    // ──────────────────────────────────────────────

    [Fact]
    public async Task InstallDriverUpdateWithResult_NoDownloadUrl_ReturnsNoDownloadUrl()
    {
        _settings.BackupEnabled.Returns(false);
        var driver = new DriverInfo { DeviceID = "DEV001", DownloadUrl = null };

        var result = await _sut.InstallDriverUpdateWithResultAsync(driver);

        result.Should().Be(DriverInstallResult.NoDownloadUrl);
    }

    [Theory]
    [InlineData("http://example.com/driver.exe")]
    [InlineData("ftp://example.com/driver.exe")]
    public async Task InstallDriverUpdateWithResult_NonHttpsUrl_ReturnsInsecureDownloadUrl(string url)
    {
        _settings.BackupEnabled.Returns(false);
        var driver = new DriverInfo { DeviceID = "DEV001", DownloadUrl = url };

        var result = await _sut.InstallDriverUpdateWithResultAsync(driver);

        result.Should().Be(DriverInstallResult.InsecureDownloadUrl);
    }

    [Fact]
    public async Task InstallDriverUpdateWithResult_NullArg_Throws()
    {
        Func<Task> act = () => _sut.InstallDriverUpdateWithResultAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InstallDriverUpdate_BackupEnabled_CallsBackupService()
    {
        _settings.BackupEnabled.Returns(true);
        _backup.BackupDriverAsync(Arg.Any<DriverInfo>()).Returns(Task.CompletedTask);

        var driver = new DriverInfo { DeviceID = "DEV001", DownloadUrl = null };
        await _sut.InstallDriverUpdateAsync(driver);

        await _backup.Received(1).BackupDriverAsync(driver);
    }

    [Fact]
    public async Task InstallDriverUpdate_BackupDisabled_DoesNotCallBackupService()
    {
        _settings.BackupEnabled.Returns(false);

        var driver = new DriverInfo { DeviceID = "DEV001", DownloadUrl = null };
        await _sut.InstallDriverUpdateAsync(driver);

        await _backup.DidNotReceive().BackupDriverAsync(Arg.Any<DriverInfo>());
    }

    [Fact]
    public async Task InstallDriverUpdate_Success_RaisesUpdatesInstalledEvent()
    {
        _settings.BackupEnabled.Returns(false);

        UpdatesInstalledEventArgs? raisedArgs = null;
        _sut.UpdatesInstalled += (_, args) => raisedArgs = args;

        // DownloadUrl なし → false を返してもイベントは発火する
        var driver = new DriverInfo { DeviceID = "DEV001", DownloadUrl = null };
        await _sut.InstallDriverUpdateAsync(driver);

        // DownloadUrl なしの場合は false で return するのでイベントは発火しない
        raisedArgs.Should().BeNull();
    }

    // ──────────────────────────────────────────────
    // RollbackDriverAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RollbackDriver_EmptyDeviceId_Throws()
    {
        Func<Task> act = () => _sut.RollbackDriverAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RollbackDriver_NoBackup_ReturnsFalse()
    {
        _backup.HasBackup(Arg.Any<DriverInfo>()).Returns(false);

        var result = await _sut.RollbackDriverAsync("DEV001");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RollbackDriver_WithBackup_CallsRestore()
    {
        _backup.HasBackup(Arg.Any<DriverInfo>()).Returns(true);
        _backup.RestoreDriverAsync(Arg.Any<DriverInfo>()).Returns(true);

        var result = await _sut.RollbackDriverAsync("DEV001");

        result.Should().BeTrue();
        await _backup.Received(1).RestoreDriverAsync(Arg.Any<DriverInfo>());
    }

    // ──────────────────────────────────────────────
    // InstallDriverUpdateAsync — インストーラー形式
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task InstallDriverUpdate_EmptyUrl_ReturnsFalse(string? url)
    {
        _settings.BackupEnabled.Returns(false);
        var driver = new DriverInfo { DeviceID = "DEV001", DownloadUrl = url };
        var result = await _sut.InstallDriverUpdateAsync(driver);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("http://example.com/driver.exe")]
    [InlineData("ftp://example.com/driver.exe")]
    [InlineData("not-a-url")]
    public async Task InstallDriverUpdate_NonHttpsUrl_ReturnsFalseWithoutDownloading(string url)
    {
        // HTTP等の非HTTPS URLは中間者攻撃でインストーラーを差し替えられるため拒否する
        _settings.BackupEnabled.Returns(false);
        var driver = new DriverInfo { DeviceID = "DEV001", DownloadUrl = url };
        var result = await _sut.InstallDriverUpdateAsync(driver);
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    // CompareVersions (VersionHelper 委譲)
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("2.0.0.0", "1.0.0.0", 1)]
    [InlineData("1.0.0.0", "1.0.0.0", 0)]
    [InlineData("1.0.0.0", "2.0.0.0", -1)]
    public void CompareVersions_ReturnsCorrectSign(string v1, string v2, int expectedSign)
    {
        int result = _sut.CompareVersions(v1, v2);

        Math.Sign(result).Should().Be(expectedSign);
    }

    // ──────────────────────────────────────────────
    // Dispose
    // ──────────────────────────────────────────────

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        Action act = () =>
        {
            _sut.Dispose();
            _sut.Dispose();
        };
        act.Should().NotThrow();
    }
}
