using System;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// ダウンロードした実行ファイル(EXE/MSI/CAB)の Authenticode 署名を検証します。
    /// Windows 標準の証明書検証機構のみを使用（無料・追加ライブラリ不要）。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class AuthenticodeHelper
    {
        /// <summary>
        /// ファイルが有効な Authenticode 署名（信頼された証明書チェーン）を
        /// 持っているかを検証します。署名が存在しない、または無効な場合は false。
        /// </summary>
        public static bool HasValidSignature(string filePath)
        {
            try
            {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile は .NET 9 で非推奨だが .NET 8 では利用可能
                using var cert = X509Certificate2.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

                return chain.Build(cert);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // 署名なし、または破損した署名
                return false;
            }
        }
    }
}
