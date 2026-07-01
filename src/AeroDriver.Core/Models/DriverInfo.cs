using System.Collections.Generic;

namespace AeroDriver.Core.Models
{
    public class DriverInfo
    {
        public string? DeviceID { get; set; }
        public string? DeviceName { get; set; }
        public string? DriverVersion { get; set; }
        public string? DriverProviderName { get; set; }
        public System.DateTime DriverDate { get; set; }
        public string? InfName { get; set; }
        public bool IsWHQLCertified { get; set; }
        public string? DownloadUrl { get; set; }
        public string? HardwareID { get; set; }
        public bool IsGraphicsDriver { get; set; }
        public string? UpdateSource { get; set; }
        public string? InstallerType { get; set; }
        public string? DeviceClass { get; set; }

        /// <summary>カタログ検索等で使う内部ID（Windows Update Catalog の updateId 等）</summary>
        public string? Id { get; set; }
    }

    public class DriverDetailInfo : DriverInfo
    {
        public string? DriverPath { get; set; }
        public long DriverSize { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }

        /// <summary>
        /// Win32_PnPEntity.ConfigManagerErrorCode に基づくデバイス状態。
        /// 0: 不明（コード取得不可）, 1: 正常（コード0）, 3: エラー（0/22以外の非0コード）,
        /// 4: 無効（コード22＝ユーザーにより無効化）。
        /// 2（警告）は ConfigManagerErrorCode に対応する概念が存在しないため未使用。
        /// </summary>
        public int StatusInfo { get; set; }

        public string? ClassGuid { get; set; }
        public string? Manufacturer { get; set; }
        public string? InfContent { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new();
        public CertificateInfo? CertificateInfo { get; set; }
    }

    public class CertificateInfo
    {
        public string? Issuer { get; set; }
        public string? Subject { get; set; }
        public string? ValidFrom { get; set; }
        public string? ValidTo { get; set; }
        public bool IsWHQLSigned { get; set; }
    }
}
