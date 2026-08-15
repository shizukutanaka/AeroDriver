namespace AeroDriver.Core.Helpers
{
    /// <summary>インストーラー/pnputil の終了コードを解釈した結果。</summary>
    public enum InstallerOutcome
    {
        /// <summary>完了。追加の操作は不要。</summary>
        Success,

        /// <summary>
        /// 完了したが、変更を有効にするには再起動が必要(または再起動が既に開始されている)。
        /// <b>成功である</b>。失敗として扱ってはいけない。
        /// </summary>
        SuccessRebootRequired,

        /// <summary>
        /// 該当するデバイスが無い、または既により新しいドライバーが入っている(pnputil の 259)。
        /// 失敗だが「壊れた」わけではないため、原因を区別してログに出す。
        /// </summary>
        NoMatchingDevices,

        /// <summary>失敗。</summary>
        Failed,
    }

    /// <summary>
    /// msiexec / pnputil / ドライバーインストーラーの終了コードを解釈します。
    ///
    /// 「0 だけが成功」という判定は誤りです。公式仕様では以下がいずれも<b>成功</b>を意味します
    /// (Windows Installer エラーコード仕様、および pnputil の戻り値仕様):
    /// <list type="bullet">
    /// <item><c>0</c> ERROR_SUCCESS — 完了</item>
    /// <item><c>3010</c> ERROR_SUCCESS_REBOOT_REQUIRED — 「完了。再起動が必要」</item>
    /// <item><c>1641</c> ERROR_SUCCESS_REBOOT_INITIATED — 「完了。再起動を開始した」</item>
    /// </list>
    /// ドライバーのインストールは <c>3010</c> で終わることが非常に多く、これを失敗と誤判定すると
    /// 「実際には成功しているのに失敗と表示され、更新一覧に残り続けて再インストールを誘発する」
    /// という実害が出ます。
    ///
    /// 純粋関数なのでプロセス実行なしに全ケースをテストできます。
    /// </summary>
    public static class InstallerExitCode
    {
        /// <summary>ERROR_SUCCESS。</summary>
        public const int Success = 0;

        /// <summary>ERROR_SUCCESS_REBOOT_INITIATED — 成功。再起動が開始されている。</summary>
        public const int SuccessRebootInitiated = 1641;

        /// <summary>ERROR_SUCCESS_REBOOT_REQUIRED — 成功。再起動が必要。</summary>
        public const int SuccessRebootRequired = 3010;

        /// <summary>
        /// ERROR_NO_MORE_ITEMS — pnputil が「該当デバイスなし、または既により新しいドライバーあり」
        /// を示すときに返す。成功ではない。
        /// </summary>
        public const int NoMoreItems = 259;

        /// <summary>終了コードを解釈します。</summary>
        public static InstallerOutcome Interpret(int exitCode) => exitCode switch
        {
            Success => InstallerOutcome.Success,
            // 再起動「必要」と「開始済み」はどちらも成功。呼び出し側での扱いも同じでよいため
            // ひとつの Outcome にまとめる(区別が必要になったら分ければよい)
            SuccessRebootRequired => InstallerOutcome.SuccessRebootRequired,
            SuccessRebootInitiated => InstallerOutcome.SuccessRebootRequired,
            NoMoreItems => InstallerOutcome.NoMatchingDevices,
            _ => InstallerOutcome.Failed,
        };

        /// <summary>インストールが成功したとみなせるか(再起動要求を含む)。</summary>
        public static bool IsSuccess(InstallerOutcome outcome) =>
            outcome is InstallerOutcome.Success or InstallerOutcome.SuccessRebootRequired;

        /// <summary>終了コードが成功を示すかを直接判定します。</summary>
        public static bool IsSuccess(int exitCode) => IsSuccess(Interpret(exitCode));
    }
}
