namespace AeroDriver.Core.Models
{
    /// <summary>
    /// ドライバーインストールの詳細な結果。
    /// bool だけでは「なぜ失敗したか」がUI/呼び出し元に伝わらないため、
    /// 原因ごとに区別して復旧行動（再試行・権限昇格・手動確認）をUIが判断できるようにする。
    /// </summary>
    public enum DriverInstallResult
    {
        Success = 0,
        AdminRequired,
        NoDownloadUrl,
        InsecureDownloadUrl,
        DownloadFailed,
        SignatureInvalid,
        InstallerFailed,
        Cancelled,
        UnknownError,
    }
}
