using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// ダウンロードした実行ファイル(EXE/MSI/CAB)の Authenticode 署名を検証します。
    /// Windows 標準の証明書検証機構のみを使用（無料・追加ライブラリ不要）。
    /// 信頼判定そのものはネイティブ WinVerifyTrust API（wintrust.dll）で行う。
    /// X509Certificate2.CreateFromSignedFile + X509Chain.Build は、ファイルの証明書テーブルから
    /// 証明書を抽出してそのチェーンを検証するだけで、PKCS#7署名が実際にファイルの現在の
    /// バイト列を対象にしているかや、コード署名用EKUを持つかは確認しない。そのため
    /// 「切り出した証明書は正当だが、ファイル自体は改ざんされている／別ファイルの証明書
    /// テーブルを移植された」ケースを見逃してしまう。WinVerifyTrust は署名対象ハッシュと
    /// ファイルの実ハッシュの一致・チェーン信頼・失効・EKUをまとめて検証するため、これを使う。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class AuthenticodeHelper
    {
        /// <summary>
        /// ファイルが有効な Authenticode 署名（信頼された証明書チェーン、かつ署名が
        /// ファイルの現在のバイト列を実際にカバーしていること）を持っているかを検証します。
        /// 署名が存在しない、無効、またはファイル自体にアクセスできない場合はすべて
        /// false（フェイルクローズ）。
        /// </summary>
        public static bool HasValidSignature(string filePath)
        {
            try
            {
                return VerifyTrust(filePath);
            }
            // WinVerifyTrust 自体はファイルの信頼状態を戻り値（HRESULT相当）で返すため
            // 通常は例外を投げない。ただし呼び出し前後の管理コード（Marshal操作等）や
            // 想定外の実行環境に起因する例外は、署名検証の目的上「信頼できない」= false
            // としてフェイルクローズさせる必要がある（想定外の例外を呼び出し元へ漏らして
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
        /// IsTrustedChain は <see cref="HasValidSignature"/> と同じ WinVerifyTrust による
        /// 実検証結果を反映する（証明書テーブルの抽出とチェーン構築だけの簡易判定ではない）。
        /// </summary>
        public static CertificateInfo? GetCertificateInfo(string filePath)
        {
            try
            {
                // Issuer/Subject/有効期間の表示用メタデータ抽出のみに使用。信頼判定には使わない。
#pragma warning disable SYSLIB0057 // CreateFromSignedFile は .NET 9 で非推奨だが .NET 8 では利用可能
                using var cert = X509Certificate2.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057

                return new CertificateInfo
                {
                    Issuer = cert.Issuer,
                    Subject = cert.Subject,
                    ValidFrom = cert.NotBefore.ToString("o"),
                    ValidTo = cert.NotAfter.ToString("o"),
                    IsTrustedChain = VerifyTrust(filePath),
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

        // ---- ネイティブ WinVerifyTrust (wintrust.dll) 相互運用 ----
        // X509Certificate2/X509Chain による簡易チェーン検証では
        // 「署名が実際に現在のファイルバイトを対象としているか」「コード署名用EKUを
        // 持つか」を確認できないため、真正な Authenticode 検証には本来の
        // Windows API である WinVerifyTrust を呼び出す必要がある。

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_WHOLECHAIN = 1;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;

        // WINTRUST_ACTION_GENERIC_VERIFY_V2: Authenticode 署名検証用の標準ポリシーGUID
        private static readonly Guid WinTrustActionGenericVerifyV2 =
            new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
            ref WINTRUST_DATA pWVTData);

        /// <summary>
        /// WinVerifyTrust を用いて、ファイルの Authenticode 署名がそのファイルの現在の
        /// バイト列を実際にカバーしており、かつ信頼された証明書チェーン（オンライン失効
        /// チェック込み）で検証できるかを確認します。戻り値0（ERROR_SUCCESS）のみ true。
        /// Windows 以外の環境では常に false（フェイルクローズ）。
        /// </summary>
        private static bool VerifyTrust(string filePath)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = fileInfoPtr,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = 0,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero,
                };

                var hwnd = new IntPtr(-1); // INVALID_HANDLE_VALUE: UIなしで呼ぶ際の慣例値
                int result = WinVerifyTrust(hwnd, WinTrustActionGenericVerifyV2, ref data);

                // WinVerifyTrust は VERIFY 呼び出し後の data.hWVTStateData（ref経由で書き戻された
                // 状態ハンドル）を、検証結果の成否に関わらず必ず WTD_STATEACTION_CLOSE で
                // 解放する必要がある（そのままハンドル値は変更せず、アクションのみ切り替える）
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(hwnd, WinTrustActionGenericVerifyV2, ref data);

                return result == 0; // ERROR_SUCCESS = 検証成功（信頼できる署名）
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
    }
}
