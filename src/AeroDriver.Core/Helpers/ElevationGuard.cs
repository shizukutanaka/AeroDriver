using System;
using System.Security.Principal;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// 管理者権限が必要な操作の前に呼び出してください。
    /// 管理者権限がない場合は UnauthorizedAccessException をスローします。
    /// </summary>
    public static class ElevationGuard
    {
        /// <summary>
        /// 現在のプロセスが管理者権限で実行されているかを返します。
        /// 非Windows環境（クロスプラットフォームのユニットテスト実行機など）では
        /// WindowsIdentity 自体が使えないため、チェックをバイパスして true を返す。
        /// 実際の操作（CimSession/pnputil呼び出し）はどのみち非Windowsでは失敗するため、
        /// ここでの誤判定によるリスクはない。
        /// </summary>
        public static bool IsElevated
        {
            get
            {
                if (!OperatingSystem.IsWindows()) return true;

                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity)
                    .IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// 管理者権限がなければ UnauthorizedAccessException をスローします。
        /// ドライバーインストール・削除・有効化・無効化の前に呼び出してください。
        /// </summary>
        /// <param name="operationName">エラーメッセージに含める操作名。</param>
        public static void ThrowIfNotElevated(string operationName)
        {
            if (!IsElevated)
                throw new UnauthorizedAccessException(
                    $"「{operationName}」には管理者権限が必要です。" +
                    " アプリケーションを管理者として実行してください。");
        }
    }
}
