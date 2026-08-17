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
                // 注意: CreateFromSignedFile は X509Certificate（基底型）で宣言された静的メソッドで、
                // 戻り値も X509Certificate。X509Certificate2 経由で呼んでも戻り値は昇格しないため、
                // NotBefore/NotAfter を読むには X509Certificate2 へ明示的に包み直す必要がある
#pragma warning disable SYSLIB0057 // CreateFromSignedFile は .NET 9 で非推奨だが .NET 8 では利用可能
                using var signedFileCert = X509Certificate.CreateFromSignedFile(filePath);
                using var cert = new X509Certificate2(signedFileCert);
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

        // WinVerifyTrust の戻り値は LONG（HRESULT マクロで判定してはいけない、と公式に注記あり）。
        // 成功は 0 のみ。以下は信頼プロバイダーが返す代表的な失敗コード（winerror.h / softpub.h）で、
        // 「なぜ失敗したか」をユーザーに正しく伝えるために使う。判定自体は 0 以外すべて拒否のまま。
        private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
        private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
        private const int TRUST_E_SUBJECT_NOT_TRUSTED = unchecked((int)0x800B0004);
        private const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);
        private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
        private const int CERT_E_CHAINING = unchecked((int)0x800B010A);
        private const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
        private const int CERT_E_REVOKED = unchecked((int)0x800B010C);
        private const int CERT_E_REVOCATION_FAILURE = unchecked((int)0x800B010E);
        /// <summary>Windows 以外で検証を試みた場合に返す値（信頼プロバイダーが存在しない）。</summary>
        public const int TRUST_E_PROVIDER_UNKNOWN_STATUS = unchecked((int)0x800B0001);

        /// <summary>
        /// WinVerifyTrust の戻り値を、原因が分かる説明文に変換します。
        ///
        /// 特に <c>CERT_E_REVOCATION_FAILURE</c> と <c>CERT_E_REVOKED</c> の区別が重要です。
        /// 前者は「失効状態を<b>確認できなかった</b>」（多くはネットワーク不通）で署名自体は
        /// 壊れていない可能性が高く、後者は「証明書が実際に<b>失効させられている</b>」という
        /// 深刻な状態です。両方を「署名が無効」と表示すると、オフライン環境のユーザーが
        /// 正常なドライバーを不正だと誤解します（ネットワークドライバーの導入時など、
        /// ネットワークが無い状態でのインストールは実際によくある場面です）。
        /// </summary>
        public static string DescribeVerificationFailure(int status) => status switch
        {
            0 => "署名は有効です",
            TRUST_E_NOSIGNATURE => "ファイルに Authenticode 署名がありません",
            TRUST_E_BAD_DIGEST => "署名がファイルの現在の内容と一致しません（改ざんまたは破損の可能性）",
            TRUST_E_EXPLICIT_DISTRUST => "この署名は明示的に信頼しない設定になっています",
            TRUST_E_SUBJECT_NOT_TRUSTED => "署名者が信頼されていません",
            CERT_E_UNTRUSTEDROOT => "証明書チェーンのルートが信頼されていません",
            CERT_E_CHAINING => "証明書チェーンを構築できませんでした",
            CERT_E_EXPIRED => "証明書の有効期限が切れており、有効なタイムスタンプもありません",
            CERT_E_REVOKED => "証明書が失効しています（発行元によって取り消された証明書です）",
            CERT_E_REVOCATION_FAILURE =>
                "証明書の失効状態を確認できませんでした（ネットワーク未接続の可能性）。" +
                "署名自体が無効とは限りませんが、安全のためインストールは中止します",
            _ => $"署名検証に失敗しました (0x{status:X8})",
        };

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
        private static bool VerifyTrust(string filePath) => VerifyTrustStatus(filePath) == 0;

        /// <summary>
        /// <see cref="VerifyTrust"/> と同じ検証を行い、WinVerifyTrust の生の戻り値を返します。
        /// 0 が成功。失敗理由を <see cref="DescribeVerificationFailure"/> で説明したい場合に使います。
        /// Windows 以外では <see cref="TRUST_E_PROVIDER_UNKNOWN_STATUS"/> を返します（フェイルクローズ）。
        /// </summary>
        public static int VerifyTrustStatus(string filePath)
        {
            if (!OperatingSystem.IsWindows())
                return TRUST_E_PROVIDER_UNKNOWN_STATUS;

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

                return result; // 0 (ERROR_SUCCESS) = 検証成功（信頼できる署名）
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
    }
}
