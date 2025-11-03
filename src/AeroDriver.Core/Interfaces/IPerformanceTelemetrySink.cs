using System;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Services;

namespace AeroDriver.Core.Interfaces;

/// <summary>
/// パフォーマンス監視結果をテレメトリ基盤に送信するためのシンク。
/// </summary>
public interface IPerformanceTelemetrySink
{
    Task ReportSummaryAsync(string correlationId, PerformanceMonitoringSnapshot snapshot, CancellationToken cancellationToken);
    Task ReportAlertAsync(string correlationId, MonitoringAlert alert, CancellationToken cancellationToken);
    Task ReportFailureAsync(string correlationId, string context, Exception exception, CancellationToken cancellationToken);
}
