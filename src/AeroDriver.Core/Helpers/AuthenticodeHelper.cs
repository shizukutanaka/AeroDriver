using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AeroDriver.Core.Models;

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
        /// 持っているかを検証します。署名が存在しない、無効、またはファイル自体に
        /// アクセスできない場合はすべて false（フェイルクローズ）。
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
            // CreateFromSignedFile はファイルが実際に開けない場合（存在しない・
            // 権限不足・ダウンロード中に削除された等）に CryptographicException 以外
            // （FileNotFoundException / UnauthorizedAccessException / IOException）も
            // 投げうる。また Windows 専用のネイティブ暗号API（WinTrust）に依存するため
            // 非Windows環境では PlatformNotSupportedException を投げる場合もある。
            // 署名検証の目的上、いずれの失敗も「信頼できない」= false として
            // フェイルクローズさせる必要がある（想定外の例外を呼び出し元へ漏らして
            // インストール可否判定を誤らせてはならない）。
            catch (Exception ex) when (
                ex is CryptographicException or
                      IOException or
                      UnauthorizedAccessException or
                      ArgumentException or
                      PlatformNotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// ファイルの Authenticode 署名から発行者/サブジェクト/有効期間を読み取ります。
        /// 署名が存在しない、無効、またはファイル自体にアクセスできない場合は null
        /// （フェイルクローズ。<see cref="HasValidSignature"/> と同じ例外方針）。
        /// </summary>
        public static CertificateInfo? GetCertificateInfo(string filePath)
        {
            try
            {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile は .NET 9 で非推奨だが .NET 8 では利用可能
                using var cert = X509Certificate2.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                bool isTrusted = chain.Build(cert);

                return new CertificateInfo
                {
                    Issuer = cert.Issuer,
                    Subject = cert.Subject,
                    ValidFrom = cert.NotBefore.ToString("o"),
                    ValidTo = cert.NotAfter.ToString("o"),
                    IsWHQLSigned = isTrusted,
                };
            }
            catch (Exception ex) when (
                ex is CryptographicException or
                      IOException or
                      UnauthorizedAccessException or
                      ArgumentException or
                      PlatformNotSupportedException)
            {
                return null;
            }
        }
    }
}
