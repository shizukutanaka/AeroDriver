using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Security;

/// <summary>
/// 監査イベントシンクのインターフェース
/// 監査イベントを外部システム（SIEM、ログサーバーなど）に送信します
/// </summary>
public interface IAuditEventSink
{
    /// <summary>
    /// 監査イベントを処理します
    /// </summary>
    Task ProcessEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数の監査イベントをバッチ処理します
    /// </summary>
    Task ProcessEventsAsync(AuditEvent[] auditEvents, CancellationToken cancellationToken = default);

    /// <summary>
    /// シンクの健全性をチェックします
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// シンクのリソースを解放します
    /// </summary>
    Task DisposeAsync();
}
