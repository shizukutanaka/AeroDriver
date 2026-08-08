using AeroDriver.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace AeroDriver.Core.Tests.Helpers;

/// <summary>
/// Windows の標準ハードウェアIDフォーマット(Windows Driver Docs の device identifier 仕様)に
/// 対する解析テスト。USB を扱えなかった回帰の再発防止を兼ねる。
/// </summary>
public class HardwareIdParserTests
{
    // ── PCI ──

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_1234")]
    [InlineData(@"PCI\VEN_8086&DEV_1234&SUBSYS_00000000")]
    [InlineData(@"PCI\VEN_8086&DEV_1234&SUBSYS_00000000&REV_03")]
    [InlineData(@"PCI\VEN_8086&DEV_1234&REV_03")]
    public void TryParse_PciVariants_ExtractCoreId(string hardwareId)
    {
        HardwareIdParser.TryParse(hardwareId, out var parsed).Should().BeTrue();
        parsed!.Bus.Should().Be("PCI");
        parsed.VendorId.Should().Be("8086");
        parsed.ProductId.Should().Be("1234");
        parsed.CoreId.Should().Be(@"PCI\VEN_8086&DEV_1234");
    }

    // ── USB (これまで常に失敗していたケース) ──

    [Fact]
    public void TryParse_UsbSingleInterface_ExtractsVidPid()
    {
        HardwareIdParser.TryParse(@"USB\VID_046D&PID_C52B", out var parsed).Should().BeTrue();
        parsed!.Bus.Should().Be("USB");
        parsed.VendorId.Should().Be("046D");
        parsed.ProductId.Should().Be("C52B");
        parsed.InterfaceNumber.Should().BeNull();
        parsed.CoreId.Should().Be(@"USB\VID_046D&PID_C52B");
    }

    [Fact]
    public void TryParse_UsbWithRevision_IgnoresRevInCoreId()
    {
        // REV は4桁。MI(2桁)と取り違えてはいけない
        HardwareIdParser.TryParse(@"USB\VID_046D&PID_C52B&REV_0100", out var parsed).Should().BeTrue();
        parsed!.InterfaceNumber.Should().BeNull("REV は インターフェース番号ではない");
        parsed.CoreId.Should().Be(@"USB\VID_046D&PID_C52B");
        parsed.QualifiedId.Should().Be(@"USB\VID_046D&PID_C52B");
    }

    [Fact]
    public void TryParse_UsbCompositeDevice_CapturesInterfaceNumber()
    {
        // 複合デバイスはインターフェースごとに別ドライバーが割り当たるため MI を保持する
        HardwareIdParser.TryParse(@"USB\VID_046D&PID_C52B&MI_01", out var parsed).Should().BeTrue();
        parsed!.InterfaceNumber.Should().Be("01");
        parsed.CoreId.Should().Be(@"USB\VID_046D&PID_C52B");
        parsed.QualifiedId.Should().Be(@"USB\VID_046D&PID_C52B&MI_01");
    }

    [Fact]
    public void TryParse_UsbCompositeWithRevAndMi_CapturesBothCorrectly()
    {
        HardwareIdParser.TryParse(@"USB\VID_1234&PID_5678&REV_0100&MI_02", out var parsed).Should().BeTrue();
        parsed!.VendorId.Should().Be("1234");
        parsed.ProductId.Should().Be("5678");
        parsed.QualifiedId.Should().Be(@"USB\VID_1234&PID_5678&MI_02");
    }

    // ── 正規化・異常系 ──

    [Fact]
    public void TryParse_LowercaseInput_NormalizesToUpper()
    {
        HardwareIdParser.TryParse(@"usb\vid_046d&pid_c52b", out var parsed).Should().BeTrue();
        parsed!.VendorId.Should().Be("046D");
        parsed.ProductId.Should().Be("C52B");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"ACPI\PNP0A03")]           // 対応外バス
    [InlineData(@"PCI\VEN_808&DEV_1234")]   // VEN が3桁 = 不正
    [InlineData(@"USB\VID_046D")]           // PID が無い
    public void TryParse_UnsupportedOrMalformed_ReturnsFalse(string? hardwareId)
    {
        HardwareIdParser.TryParse(hardwareId, out var parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }
}
