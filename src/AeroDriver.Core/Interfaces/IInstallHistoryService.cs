using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Interfaces
{
    /// <summary>
    /// ドライバーインストールの監査証跡。「いつ・何が・どのバージョンから何に変わったか」を
    /// 追記のみで記録します。ドライバー更新後に不具合が出たとき、何を戻せばよいかを答えるための機能。
    /// テレメトリではありません(ローカルのファイルにのみ記録し、送信は一切行いません)。
    /// </summary>
    public interface IInstallHistoryService
    {
        /// <summary>
        /// 1件記録します。記録の失敗はインストール自体を失敗させません
        /// (監査は可用性層: 記録できないことを理由に本来の操作を止めない)。
        /// </summary>
        Task RecordAsync(InstallHistoryEntry entry, CancellationToken cancellationToken = default);

        /// <summary>
        /// 記録を新しい順に返します。壊れた行(クラッシュで途中まで書かれた行など)は
        /// 読み飛ばし、残りの履歴は失いません。
        /// </summary>
        /// <param name="limit">返す最大件数。0以下なら全件。</param>
        Task<IReadOnlyList<InstallHistoryEntry>> GetHistoryAsync(
            int limit = 0, CancellationToken cancellationToken = default);
    }
}
