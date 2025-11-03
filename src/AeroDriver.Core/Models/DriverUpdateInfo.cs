namespace AeroDriver.Core.Models
{
    /// <summary>
    /// ドライバー更新情報
    /// 新しいドライバーバージョンへの更新時に使用される情報
    /// </summary>
    public class DriverUpdateInfo
    {
        /// <summary>
        /// ドライバー名
        /// </summary>
        public string DriverName { get; set; } = string.Empty;

        /// <summary>
        /// 新バージョン
        /// </summary>
        public string DriverVersion { get; set; } = string.Empty;

        /// <summary>
        /// デバイスID
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// デバイス名
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// 現在のバージョン
        /// </summary>
        public string CurrentVersion { get; set; } = string.Empty;

        /// <summary>
        /// ドライバーペイロード（バイナリデータ）
        /// CrowdStrike事件対策: ペイロード検証に使用
        /// </summary>
        public byte[]? Payload { get; set; }

        /// <summary>
        /// WHQL認証済みかどうか
        /// </summary>
        public bool IsWHQLCertified { get; set; }

        /// <summary>
        /// リリース日
        /// </summary>
        public DateTime ReleaseDate { get; set; }

        /// <summary>
        /// 更新の優先度
        /// </summary>
        public UpdatePriority Priority { get; set; }

        /// <summary>
        /// 変更ログ
        /// </summary>
        public string ChangeLog { get; set; } = string.Empty;

        /// <summary>
        /// ダウンロードURL
        /// </summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>
        /// ペイロードハッシュ（整合性確認用）
        /// </summary>
        public string PayloadHash { get; set; } = string.Empty;

        /// <summary>
        /// ハッシュアルゴリズム（SHA256など）
        /// </summary>
        public string HashAlgorithm { get; set; } = "SHA256";

        /// <summary>
        /// デジタル署名
        /// </summary>
        public string DigitalSignature { get; set; } = string.Empty;
    }
}
