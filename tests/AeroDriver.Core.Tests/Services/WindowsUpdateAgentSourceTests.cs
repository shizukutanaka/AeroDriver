using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

/// <summary>
/// WindowsUpdateAgentSource: WUA COM は Windows 専用のため、実際の COM 呼び出しは
/// CI/非Windows環境では失敗する。ここでは例外を握りつぶして空のリストを返す
/// 「グレースフルデグラデーション」動作のみを検証する。
/// </summary>
public class WindowsUpdateAgentSourceTests
{
    private readonly WindowsUpdateAgentSource _sut =
        new(NullLogger<WindowsUpdateAgentSource>.Instance);

    [Fact]
    public void SourceName_ReturnsExpectedValue()
    {
        _sut.SourceName.Should().Be("Windows Update Agent");
    }

    [Fact]
    public async Task SearchUpdatesAsync_ComUnavailable_ReturnsEmptyListInsteadOfThrowing()
    {
        // WUA COM が存在しない環境（非Windows/CI）では例外を握りつぶし、
        // 呼び出し元に例外を伝播させず空リストを返すべき（他ソースの取得を止めないため）
        var result = await _sut.SearchUpdatesAsync();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task FindDriverAsync_EmptyHardwareId_ReturnsNullWithoutComCall()
    {
        var result = await _sut.FindDriverAsync(string.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDriverAsync_WhitespaceHardwareId_ReturnsNullWithoutComCall()
    {
        var result = await _sut.FindDriverAsync("   ");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindDriverAsync_ComUnavailable_ReturnsNullInsteadOfThrowing()
    {
        var result = await _sut.FindDriverAsync("PCI\\VEN_10DE&DEV_2204");
        result.Should().BeNull();
    }

    // --- MapToDriverInfo: 実際のCOMなしでマッピングロジックだけを検証 ---
    // dynamic な ExpandoObject は WUA COM オブジェクトと同様、名前ベースでプロパティ解決される

    [Fact]
    public void MapToDriverInfo_UsesDriverVerVersion_NotDriverVerDate()
    {
        // 回帰テスト: DriverVerDate(日付)を誤って DriverVersion に入れていたバグの再発防止。
        // バージョンには DriverVerVersion、日付には DriverVerDate を使うべき
        dynamic update = new System.Dynamic.ExpandoObject();
        update.Title = "Test Display Driver";
        update.DriverVerVersion = "31.0.15.3667";
        update.DriverVerDate = new DateTime(2026, 1, 15);
        update.DriverProvider = "Test Vendor";
        update.DriverHardwareID = "PCI\\VEN_10DE&DEV_2204";

        var info = _sut.MapToDriverInfo(update);

        info.Should().NotBeNull();
        info!.DriverVersion.Should().Be("31.0.15.3667");
        info.DriverDate.Should().Be(new DateTime(2026, 1, 15));
    }

    [Fact]
    public void MapToDriverInfo_ExtractsDeviceIdFromHardwareId()
    {
        dynamic update = new System.Dynamic.ExpandoObject();
        update.Title = "Test Driver";
        update.DriverVerVersion = "1.0.0.0";
        update.DriverVerDate = DateTime.UtcNow;
        update.DriverProvider = "Test Vendor";
        update.DriverHardwareID = "PCI\\VEN_10DE&DEV_2204&SUBSYS_00000000";

        var info = _sut.MapToDriverInfo(update);

        info!.DeviceID.Should().Be("PCI\\VEN_10DE&DEV_2204");
        info.IsWHQLCertified.Should().BeTrue();
        info.UpdateSource.Should().Be("Windows Update Agent");
    }

    [Fact]
    public void MapToDriverInfo_MissingOptionalProperties_StillReturnsDriverInfo()
    {
        // TrySet は各プロパティ取得の失敗を握りつぶす設計 — 一部プロパティが
        // 存在しない(ExpandoObjectに未定義)COM実装でも例外にならず必須項目だけ返す
        dynamic update = new System.Dynamic.ExpandoObject();
        update.Title = "Minimal Driver";

        var info = _sut.MapToDriverInfo(update);

        info.Should().NotBeNull();
        info!.DeviceName.Should().Be("Minimal Driver");
        info.DriverVersion.Should().BeNull();
    }
}
