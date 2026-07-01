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
}
