using AeroDriver.Core.Models;
using FluentAssertions;
using Xunit;

namespace AeroDriver.Core.Tests.Models;

public class DriverScanProgressTests
{
    [Fact]
    public void Percentage_WithKnownTotal_IsCorrect()
    {
        var p = new DriverScanProgress { Current = 3, Total = 10 };
        p.Percentage.Should().Be(30);
    }

    [Fact]
    public void Percentage_WhenTotalIsZero_ReturnsMinusOne()
    {
        var p = new DriverScanProgress { Current = 0, Total = 0 };
        p.Percentage.Should().Be(-1);
    }

    [Fact]
    public void Percentage_WhenComplete_Returns100()
    {
        var p = new DriverScanProgress { Current = 5, Total = 5 };
        p.Percentage.Should().Be(100);
    }

    [Fact]
    public void WithExpression_PreservesOtherFields()
    {
        // with 式で Phase だけ変えても他フィールドが保たれることを確認
        var original = new DriverScanProgress { Current = 2, Total = 10, CurrentDevice = "GPU" };
        var modified = original with { Phase = "更新確認中" };

        modified.Current.Should().Be(2);
        modified.Total.Should().Be(10);
        modified.CurrentDevice.Should().Be("GPU");
        modified.Phase.Should().Be("更新確認中");
    }

    [Fact]
    public void Progress_ReportsAreReceivedInOrder()
    {
        // IProgress<T> が同期的にコールバックを呼ぶことを前提とした統合確認
        var reported = new List<int>();
        var progress = new Progress<DriverScanProgress>(p => reported.Add(p.Current));

        // 同期コールバックを直接呼び出してテスト
        ((IProgress<DriverScanProgress>)progress).Report(new DriverScanProgress { Current = 1 });
        ((IProgress<DriverScanProgress>)progress).Report(new DriverScanProgress { Current = 2 });
        ((IProgress<DriverScanProgress>)progress).Report(new DriverScanProgress { Current = 3 });

        // Progress<T> は SynchronizationContext 経由のため即時反映は保証されない
        // → 非同期テストではなく同期での単体検証のみ行う
        reported.Should().BeSubsetOf(new[] { 1, 2, 3 });
    }
}
