using AeroDriver.Core.Helpers;
using AeroDriver.Core.Models;
using FluentAssertions;
using Xunit;

namespace AeroDriver.Core.Tests.Helpers;

/// <summary>
/// DriverInstallOrder はデバイスクラスに基づく純粋なヒューリスティック順序付け。
/// WMI 等の副作用がないため完全にテスト可能。
/// </summary>
public class DriverInstallOrderTests
{
    private static DriverInfo Drv(string name, string? deviceClass, bool isGpu = false)
        => new() { DeviceName = name, DeviceClass = deviceClass, IsGraphicsDriver = isGpu };

    [Fact]
    public void Sort_ChipsetInstallsBeforeGpu()
    {
        var input = new[]
        {
            Drv("GPU", "DISPLAY", isGpu: true),
            Drv("Chipset", "SYSTEM"),
        };

        var sorted = DriverInstallOrder.Sort(input);

        sorted[0].DeviceName.Should().Be("Chipset");
        sorted[1].DeviceName.Should().Be("GPU");
    }

    [Fact]
    public void Sort_FullPipeline_OrdersByClassPriority()
    {
        var input = new[]
        {
            Drv("Display", "DISPLAY", isGpu: true),
            Drv("Network", "NET"),
            Drv("Storage", "HDC"),
            Drv("System", "SYSTEM"),
            Drv("Unknown", "SOMETHINGELSE"),
        };

        var sorted = DriverInstallOrder.Sort(input);

        sorted.Select(d => d.DeviceName).Should().ContainInOrder(
            "System", "Storage", "Network", "Unknown", "Display");
    }

    [Fact]
    public void GetPriority_UnknownClass_UsesDefaultBetweenBaseAndGpu()
    {
        int system = DriverInstallOrder.GetPriority(Drv("s", "SYSTEM"));
        int unknown = DriverInstallOrder.GetPriority(Drv("u", "MYSTERYCLASS"));
        int gpu = DriverInstallOrder.GetPriority(Drv("g", "DISPLAY"));

        system.Should().BeLessThan(unknown);
        unknown.Should().BeLessThan(gpu);
    }

    [Fact]
    public void GetPriority_NullDeviceClassButGpuFlag_TreatedAsGpu()
    {
        // DeviceClass が取れなくても IsGraphicsDriver フラグで GPU を最後に回す
        int gpuByFlag = DriverInstallOrder.GetPriority(Drv("g", deviceClass: null, isGpu: true));
        int gpuByClass = DriverInstallOrder.GetPriority(Drv("g", "DISPLAY"));

        gpuByFlag.Should().Be(gpuByClass);
    }

    [Fact]
    public void GetPriority_ClassMatch_IsCaseInsensitive()
    {
        DriverInstallOrder.GetPriority(Drv("s", "system"))
            .Should().Be(DriverInstallOrder.GetPriority(Drv("s", "SYSTEM")));
    }

    [Fact]
    public void Sort_SamePriority_PreservesInputOrder()
    {
        // 同一優先度（どちらも NET）は入力順を維持する（安定ソート）
        var input = new[]
        {
            Drv("Net-A", "NET"),
            Drv("Net-B", "NET"),
        };

        var sorted = DriverInstallOrder.Sort(input);

        sorted[0].DeviceName.Should().Be("Net-A");
        sorted[1].DeviceName.Should().Be("Net-B");
    }

    [Fact]
    public void Sort_EmptyInput_ReturnsEmpty()
    {
        DriverInstallOrder.Sort(System.Array.Empty<DriverInfo>()).Should().BeEmpty();
    }
}
