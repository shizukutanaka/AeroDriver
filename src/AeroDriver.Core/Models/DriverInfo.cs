using System;
using System.Collections.Generic;

namespace AeroDriver.Core.Models
{
    /// <summary>
    /// ドライバー情報を表すクラス
    /// </summary>
    public class DriverInfo
    {
        /// <summary>
        /// 内部ID
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// デバイスID
        /// </summary>
        public string DeviceID { get; set; }
        
        /// <summary>
        /// デバイス名
        /// </summary>
        public string DeviceName { get; set; }
        
        /// <summary>
        /// ドライバーバージョン
        /// </summary>
        public string DriverVersion { get; set; }
        
        /// <summary>
        /// ドライバープロバイダー名
        /// </summary>
        public string DriverProviderName { get; set; }
        
        /// <summary>
        /// ドライバー日付
        /// </summary>
        public DateTime DriverDate { get; set; }
        
        /// <summary>
        /// INFファイル名
        /// </summary>
        public string InfName { get; set; }
        
        /// <summary>
        /// WHQL認証済みかどうか
        /// </summary>
        public bool IsWHQLCertified { get; set; }
        
        /// <summary>
        /// ダウンロードURL
        /// </summary>
        public string DownloadUrl { get; set; }
        
        /// <summary>
        /// ハードウェアID
        /// </summary>
        public string HardwareID { get; set; }
        
        /// <summary>
        /// グラフィックスドライバーかどうか
        /// </summary>
        public bool IsGraphicsDriver { get; set; }
        
        /// <summary>
        /// 更新ソース (メーカーサイト、Windows Update Catalogなど)
        /// </summary>
        public string UpdateSource { get; set; }
        
        /// <summary>
        /// インストーラータイプ (INF, EXE, MSI, ZIPなど)
        /// </summary>
        public string InstallerType { get; set; }
        
        /// <summary>
        /// デバイスクラス
        /// </summary>
        public string DeviceClass { get; set; }
        
        /// <summary>
        /// 問題があるドライバーかどうか
        /// </summary>
        public bool HasProblem { get; set; }
    }
    
    /// <summary>
    /// ドライバーの詳細情報を表すクラス
    /// </summary>
    public class DriverDetailInfo : DriverInfo
    {
        /// <summary>
        /// ドライバーのインストールパス
        /// </summary>
        public string DriverPath { get; set; }
        
        /// <summary>
        /// ドライバーサイズ (バイト)
        /// </summary>
        public long DriverSize { get; set; }
        
        /// <summary>
        /// ドライバーの説明
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// デバイスのステータス
        /// </summary>
        public string Status { get; set; }
        
        /// <summary>
        /// デバイスのステータス情報
        /// 0: 不明, 1: 正常, 2: 警告, 3: エラー, 4: 無効
        /// </summary>
        public int StatusInfo { get; set; }
        
        /// <summary>
        /// デバイスクラスGUID
        /// </summary>
        public string ClassGuid { get; set; }
        
        /// <summary>
        /// デバイスクラス
        /// </summary>
        public string DeviceClass { get; set; }
        
        /// <summary>
        /// メーカー名
        /// </summary>
        public string Manufacturer { get; set; }
        
        /// <summary>
        /// INFファイルの内容
        /// </summary>
        public string InfContent { get; set; }
        
        /// <summary>
        /// ドライバープロパティ
        /// </summary>
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// 証明書情報
        /// </summary>
        public CertificateInfo CertificateInfo { get; set; }
    }
    
    /// <summary>
    /// 証明書情報を表すクラス
    /// </summary>
    public class CertificateInfo
    {
        /// <summary>
        /// 発行者
        /// </summary>
        public string Issuer { get; set; }
        
        /// <summary>
        /// 署名者
        /// </summary>
        public string Subject { get; set; }
        
        /// <summary>
        /// 有効期間の開始
        /// </summary>
        public string ValidFrom { get; set; }
        
        /// <summary>
        /// 有効期間の終了
        /// </summary>
        public string ValidTo { get; set; }
        
        /// <summary>
        /// WHQL署名があるかどうか
        /// </summary>
        public bool IsWHQLSigned { get; set; }
    }
}
