namespace AeroDriver.Core.Models
{
    /// <summary>
    /// ドライバースキャン・更新確認の進捗情報
    /// </summary>
    public sealed class DriverScanProgress
    {
        /// <summary>処理済み件数</summary>
        public int Current { get; init; }

        /// <summary>全件数（不明な場合は 0）</summary>
        public int Total { get; init; }

        /// <summary>現在処理中のデバイス名</summary>
        public string? CurrentDevice { get; init; }

        /// <summary>フェーズ名（"Scanning", "Checking updates" 等）</summary>
        public string Phase { get; init; } = string.Empty;

        /// <summary>0〜100 のパーセンテージ（Total が 0 の場合は -1）</summary>
        public int Percentage => Total > 0 ? Current * 100 / Total : -1;
    }
}
