using System.Linq;
using AeroDriver.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace AeroDriver.Core.Tests.Helpers;

/// <summary>
/// WqlSanitizer はWQLインジェクション対策の最終防衛線であり、
/// CimSession がパラメーター化クエリをサポートしないために存在する。
/// セキュリティ上重要なコンポーネントだが、これまで専用テストが1件もなかった。
/// </summary>
public class WqlSanitizerTests
{
    // ──────────────────────────────────────────────
    // 正常な DeviceID はそのまま（バックスラッシュはエスケープされて）通る
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2204&SUBSYS_00000000")]
    [InlineData(@"USB\VID_0BDA&PID_8153&MI_00")]
    [InlineData(@"ACPI\GenuineIntel_-_Intel64_Family_6_Model_158")]
    [InlineData(@"SWD\WPDBUSENUM\{12345678-1234-1234-1234-123456789012}")]
    public void SanitizeDeviceId_ValidDeviceId_EscapesBackslashesAndReturns(string deviceId)
    {
        var result = WqlSanitizer.SanitizeDeviceId(deviceId);

        result.Should().Be(deviceId.Replace(@"\", @"\\"));
    }

    // ──────────────────────────────────────────────
    // WQLインジェクション攻撃はアローリストで拒否される
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("PCI\\VEN_10DE' OR '1'='1")]
    [InlineData("PCI\\VEN_10DE'; DROP TABLE Win32_PnPEntity; --")]
    [InlineData("PCI\\VEN_10DE' UNION SELECT * FROM Win32_Process --")]
    [InlineData("' OR 1=1 --")]
    [InlineData("DeviceID = '' OR ''='")]
    public void SanitizeDeviceId_InjectionAttempt_ThrowsArgumentException(string maliciousInput)
    {
        Action act = () => WqlSanitizer.SanitizeDeviceId(maliciousInput);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("PCI\\VEN_10DE DEV_2204")]  // スペースは許可リスト外
    [InlineData("PCI\\VEN_10DE;DEV_2204")]  // セミコロンは許可リスト外
    [InlineData("PCI\\VEN_10DE*")]          // ワイルドカードは許可リスト外
    [InlineData("PCI\\VEN_10DE\r\nDEV_2204")] // CRLFインジェクション
    public void SanitizeDeviceId_DisallowedCharacters_ThrowsArgumentException(string invalidInput)
    {
        Action act = () => WqlSanitizer.SanitizeDeviceId(invalidInput);

        act.Should().Throw<ArgumentException>();
    }

    // ──────────────────────────────────────────────
    // 空・null は明示的に拒否される
    // ──────────────────────────────────────────────

    [Fact]
    public void SanitizeDeviceId_Empty_ThrowsArgumentException()
    {
        Action act = () => WqlSanitizer.SanitizeDeviceId("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SanitizeDeviceId_Null_ThrowsArgumentException()
    {
        Action act = () => WqlSanitizer.SanitizeDeviceId(null!);

        act.Should().Throw<ArgumentException>();
    }

    // ──────────────────────────────────────────────
    // バックスラッシュは WQL リテラル内で正しく二重化される
    // ──────────────────────────────────────────────

    [Fact]
    public void SanitizeDeviceId_ValidId_DoublesEachBackslash()
    {
        // 入力に含まれるバックスラッシュは1個（PCI と VEN_10DE の間）
        var result = WqlSanitizer.SanitizeDeviceId(@"PCI\VEN_10DE");

        // WQL リテラルとして正しく埋め込むには、各バックスラッシュを2個に増やす必要がある
        result.Should().Be("PCI\\\\VEN_10DE");
        result.Count(c => c == '\\').Should().Be(2);
    }
}
