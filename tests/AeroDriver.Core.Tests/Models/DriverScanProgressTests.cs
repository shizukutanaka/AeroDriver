using System.Threading;
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
        // Progress<T> は構築時の SynchronizationContext を捕まえ、そこへ Post する。
        // コンテキストが無いと ThreadPool へ投げるため、コールバックは
        // **アサーションと並行に走り、スレッド安全でない List<int> を壊す**
        // (実際にこのテストは再ビルド直後の初回実行でだけ落ちる形でフレークしていた)。
        // 順序を検証したいのだから、順序が保証されるコンテキストを用意して決定的にする。
        var previous = SynchronizationContext.Current;
        try
        {
            var context = new QueueingSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);

            var reported = new List<int>();
            var progress = new Progress<DriverScanProgress>(p => reported.Add(p.Current));
            var sink = (IProgress<DriverScanProgress>)progress;

            sink.Report(new DriverScanProgress { Current = 1 });
            sink.Report(new DriverScanProgress { Current = 2 });
            sink.Report(new DriverScanProgress { Current = 3 });

            // 同一スレッドで投入順に実行する。ここまでは何も走っていない
            context.DrainAll();

            reported.Should().HaveCount(3);
            reported.Should().ContainInOrder(1, 2, 3);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    /// Post されたコールバックを順に貯め、明示的に呼ばれたときだけ同一スレッドで実行する。
    /// これにより Progress&lt;T&gt; の通知順を決定的に検証できる。
    /// </summary>
    private sealed class QueueingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void DrainAll()
        {
            while (_queue.Count > 0)
            {
                var (callback, state) = _queue.Dequeue();
                callback(state);
            }
        }
    }
}
