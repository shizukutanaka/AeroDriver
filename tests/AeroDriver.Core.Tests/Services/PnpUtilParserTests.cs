using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

/// <summary>
/// PnpUtilDriverSource のパーサーロジックをテストします。
/// OS 依存なし・ネットワーク不要。
/// </summary>
public class PnpUtilParserTests
{
    // pnputil /enum-drivers /all の典型的な出力（英語 Windows 10/11）
    private const string SampleOutput = """
        Microsoft PnP Utility

        Published Name:     oem0.inf
        Original Name:      nvlddmkm.inf
        Provider Name:      NVIDIA
        Class Name:         Display adapters
        Class GUID:         {4D36E968-E325-11CE-BFC1-08002BE10318}
        Driver Version:     10/27/2023 31.0.15.3699
        Signer Name:        Microsoft Windows Hardware Compatibility Publisher

        Published Name:     oem1.inf
        Original Name:      netathr10x.inf
        Provider Name:      Qualcomm
        Class Name:         Network adapters
        Class GUID:         {4D36E972-E325-11CE-BFC1-08002BE10318}
        Driver Version:     09/01/2022 12.0.0.1
        Signer Name:        Microsoft Windows Hardware Compatibility Publisher

        Published Name:     oem2.inf
        Original Name:      custom_unsigned.inf
        Provider Name:      SomeVendor
        Class Name:         Unknown
        Class GUID:         {00000000-0000-0000-0000-000000000000}
        Driver Version:     01/01/2020 1.0.0.0
        Signer Name:

        """;

    [Fact]
    public void ParseOutput_ReturnsCorrectCount()
    {
        var source = new TestablePnpUtilSource();
        var drivers = source.ParsePublic(SampleOutput);
        drivers.Should().HaveCount(3);
    }

    [Fact]
    public void ParseOutput_ExtractsDriverVersion()
    {
        var source = new TestablePnpUtilSource();
        var drivers = source.ParsePublic(SampleOutput);
        drivers[0].DriverVersion.Should().Be("31.0.15.3699");
    }

    [Fact]
    public void ParseOutput_ExtractsDriverDate()
    {
        var source = new TestablePnpUtilSource();
        var drivers = source.ParsePublic(SampleOutput);
        drivers[0].DriverDate.Should().Be(new DateTime(2023, 10, 27));
    }

    [Fact]
    public void ParseOutput_ExtractsProviderName()
    {
        var source = new TestablePnpUtilSource();
        var drivers = source.ParsePublic(SampleOutput);
        drivers[0].DriverProviderName.Should().Be("NVIDIA");
        drivers[1].DriverProviderName.Should().Be("Qualcomm");
    }

    [Fact]
    public void ParseOutput_DetectsWhqlSignedDriver()
    {
        var source = new TestablePnpUtilSource();
        var drivers = source.ParsePublic(SampleOutput);
        drivers[0].IsWHQLCertified.Should().BeTrue();
        drivers[1].IsWHQLCertified.Should().BeTrue();
        drivers[2].IsWHQLCertified.Should().BeFalse(); // 空の Signer
    }

    [Fact]
    public void ParseOutput_ExtractsInfName()
    {
        var source = new TestablePnpUtilSource();
        var drivers = source.ParsePublic(SampleOutput);
        drivers[0].InfName.Should().Be("oem0.inf");
    }

    [Fact]
    public void ParseOutput_EmptyInput_ReturnsEmpty()
    {
        var source = new TestablePnpUtilSource();
        source.ParsePublic(string.Empty).Should().BeEmpty();
        source.ParsePublic("   ").Should().BeEmpty();
    }

    [Fact]
    public void ParseOutput_HeaderOnlyInput_ReturnsEmpty()
    {
        var source = new TestablePnpUtilSource();
        source.ParsePublic("Microsoft PnP Utility\n\n").Should().BeEmpty();
    }

    // PnpUtilDriverSource のパーサーを公開するテスト専用サブクラス
    private sealed class TestablePnpUtilSource : PnpUtilDriverSource
    {
        public TestablePnpUtilSource()
            : base(NullLogger<PnpUtilDriverSource>.Instance) { }

        public IReadOnlyList<AeroDriver.Core.Models.DriverInfo> ParsePublic(string output)
            => ParseEnumOutputPublic(output);
    }
}
