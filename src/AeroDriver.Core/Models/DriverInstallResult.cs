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
        /// <summary>ダウンロードしたファイルが既知の脆弱ドライバー(LOLDriversリスト)と一致した</summary>
        KnownVulnerableDriver,
        InstallerFailed,
        Cancelled,
        UnknownError,

        /// <summary>
        /// インストールは成功したが、変更を有効にするには再起動が必要
        /// (終了コード 3010 / 1641)。<b>成功として扱うこと</b>。
        /// 既存の値の並びを変えないよう末尾に追加している。
        /// </summary>
        SuccessRebootRequired,
    }

    public static class DriverInstallResultExtensions
    {
        /// <summary>
        /// インストールが成功したとみなせるか。<see cref="DriverInstallResult.SuccessRebootRequired"/> は
        /// 「成功。ただし再起動が必要」であり成功に含めます。
        /// <c>== DriverInstallResult.Success</c> と直接比較すると再起動要求を失敗と誤判定するため、
        /// 成否判定は必ずこのメソッドを使うこと。
        /// </summary>
        public static bool IsSuccess(this DriverInstallResult result) =>
            result is DriverInstallResult.Success or DriverInstallResult.SuccessRebootRequired;
    }
}
