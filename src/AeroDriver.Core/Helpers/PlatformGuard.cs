using System;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// 対応OSの判定。
    /// <para>
    /// AeroDriver は WMI(<c>CimSession</c>)・<c>pnputil.exe</c>・WUA COM に依存する
    /// Windows 専用ツール。しかし CLI は <c>net8.0</c> を対象にしているため、
    /// Linux/macOS でも**起動できてしまう**。ガードが無いと、スキャンが
    /// 「0 件のドライバーを検出しました」という**成功に見える誤った結果**を返し、
    /// ユーザーは「このマシンにドライバーが無い」と解釈しかねない。
    /// README の Development 節は OS の断り無く <c>dotnet run</c> を案内しているので、
    /// 開発者が実際に踏む経路でもある。
    /// </para>
    /// UI 非依存の純粋ロジックなので <c>tools/offline-verify</c> で実行検証できる。
    /// </summary>
    public static class PlatformGuard
    {
        /// <summary>現在のOSでこの製品が動作可能か。</summary>
        public static bool IsSupportedPlatform() => OperatingSystem.IsWindows();

        /// <summary>
        /// 対応OSでなければ理由を返す(呼び出し側がローカライズ済みの前置きと
        /// 組み合わせて表示する)。対応OSなら null。
        /// </summary>
        public static string? DescribeUnsupportedPlatform()
        {
            if (IsSupportedPlatform()) return null;

            // OS 名は環境依存の識別子なので翻訳しない(details/history の
            // 構造化ダンプと同じ方針)。前置きの散文だけを呼び出し側が翻訳する
            return Environment.OSVersion.Platform + " / " + Environment.OSVersion.VersionString;
        }
    }
}
