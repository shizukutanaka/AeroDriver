using System;

namespace AeroDriver.Core.Models
{
    /// <summary>
    /// ドライバーインストール1件の記録。「マシンが壊れる前に何が変わったか」を
    /// 後から追跡するための監査証跡。1エントリ = JSONL の1行。
    /// </summary>
    public sealed class InstallHistoryEntry
    {
        /// <summary>記録時刻(UTC)。ローカル時刻で記録するとタイムゾーン変更や夏時間で順序が壊れる。</summary>
        public DateTime TimestampUtc { get; set; }

        public string? DeviceId { get; set; }
        public string? DeviceName { get; set; }
        public string? HardwareId { get; set; }

        /// <summary>置き換えられた側のバージョン(判明している場合)。ロールバック判断の要。</summary>
        public string? FromVersion { get; set; }

        /// <summary>インストールしようとしたバージョン。</summary>
        public string? ToVersion { get; set; }

        /// <summary>更新の入手元(<c>DriverInfo.UpdateSource</c>)。</summary>
        public string? UpdateSource { get; set; }

        /// <summary><see cref="DriverInstallResult"/> の名前。失敗も記録する(失敗の履歴も証跡)。</summary>
        public string? Result { get; set; }

        /// <summary>成功したかどうか(<c>Result</c> の冗長表現だが、集計時の利便のため保持)。</summary>
        public bool Success { get; set; }

        /// <summary>
        /// このインストール用に作成されたシステム復元ポイントのシーケンス番号。
        /// null は「作成されなかった」(設定無効・非対応環境・24時間レート制限)。
        /// ユーザーが「この更新を取り消したい」ときに、どの復元ポイントへ戻ればよいかの手掛かりになる。
        /// </summary>
        public long? RestorePointSequence { get; set; }

        /// <summary>インストール前にドライバーファイルのバックアップが取られたか。</summary>
        public bool BackupCreated { get; set; }
    }
}
