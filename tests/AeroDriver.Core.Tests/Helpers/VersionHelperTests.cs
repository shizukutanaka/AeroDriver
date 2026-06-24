using AeroDriver.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace AeroDriver.Core.Tests.Helpers;

public class VersionHelperTests
{
    // --- Compare ---

    [Theory]
    [InlineData("1.0.0.0", "1.0.0.0", 0)]
    [InlineData("2.0.0.0", "1.0.0.0", 1)]
    [InlineData("1.0.0.0", "2.0.0.0", -1)]
    [InlineData("1.2.3.4", "1.2.3.3", 1)]
    [InlineData("1.2.3.3", "1.2.3.4", -1)]
    [InlineData("10.0.0.0", "9.0.0.0", 1)]    // 桁数が増えても正しく比較できる
    [InlineData("1.0",      "1.0.0.0", 0)]     // 短いバージョンは 0 埋め
    [InlineData("1.0.1",    "1.0",     1)]
    public void Compare_ReturnsExpectedSign(string v1, string v2, int expectedSign)
    {
        int result = VersionHelper.Compare(v1, v2);
        Math.Sign(result).Should().Be(expectedSign);
    }

    [Theory]
    [InlineData(null, null, 0)]
    [InlineData(null, "1.0.0.0", -1)]
    [InlineData("1.0.0.0", null, 1)]
    [InlineData("", "", 0)]
    [InlineData("", "1.0", -1)]
    public void Compare_HandlesNullAndEmpty(string? v1, string? v2, int expectedSign)
    {
        int result = VersionHelper.Compare(v1!, v2!);
        Math.Sign(result).Should().Be(expectedSign);
    }

    [Fact]
    public void Compare_CommaDelimiterWorksLikeDot()
    {
        // Windows ドライバーバージョンはカンマ区切りの場合がある
        VersionHelper.Compare("1,2,3,4", "1.2.3.3").Should().BePositive();
        VersionHelper.Compare("1,2,3,4", "1.2.3.4").Should().Be(0);
    }

    // --- IsNewer ---

    [Theory]
    [InlineData("2.0.0.0", "1.0.0.0", true)]
    [InlineData("1.0.0.0", "2.0.0.0", false)]
    [InlineData("1.0.0.0", "1.0.0.0", false)]
    public void IsNewer_ReturnsExpectedResult(string candidate, string installed, bool expected)
    {
        VersionHelper.IsNewer(candidate, installed).Should().Be(expected);
    }
}
