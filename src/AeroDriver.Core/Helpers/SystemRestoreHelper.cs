using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// Windows のシステム復元ポイントを作成します(srrestoreptapi.h / SrClient.dll)。
    /// ドライバーインストール前のOSレベルの安全網。無料・Windows標準。
    ///
    /// 公式仕様(MicrosoftDocs/sdk-api: nf-srrestoreptapi-srsetrestorepointa)に基づく重要な制約:
    /// - <b>クライアントSKU専用</b>。Windows Server は "None supported"(サーバーでは常に失敗する)
    /// - セーフモードでは動作しない。システムの保護(System Restore)が無効なら失敗する
    /// - 管理者権限が必要
    /// - <b>Windows 8以降はレート制限</b>: 直近24時間以内に復元ポイントがあると新規作成をスキップする
    ///   (HKLM\...\SystemRestore\SystemRestorePointCreationFrequency で変更可能)。
    ///   このためAPIが成功しても新しい復元ポイントが増えないことがあり、それは異常ではない
    /// - 公式ドキュメントは「ロード時の動的リンクを避け LoadLibrary/GetProcAddress を使え」と指示している。
    ///   .NET の DllImport は初回呼び出し時に遅延解決され、DLL/エントリポイント不在は
    ///   DllNotFoundException / EntryPointNotFoundException として捕捉できるため、
    ///   下記のように必ず捕捉してフェイルオープンする
    ///
    /// 失敗しても例外は投げず false を返す(可用性層のフェイルオープン: 復元ポイントを作れないことを
    /// 理由にインストール機能全体を止めない。ただし警告ログで必ず可視化する)。
    /// 署名検証やBYOVD照合のようなセキュリティ判定はフェイルクローズであり、方針が異なる。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class SystemRestoreHelper
    {
        // srrestoreptapi.h: #define MAX_DESC_W 256
        private const int MaxDescriptionW = 256;

        // dwEventType
        private const uint BeginSystemChange = 100;
        private const uint EndSystemChange = 101;

        // dwRestorePtType
        private const uint DeviceDriverInstall = 10;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RestorePointInfoW
        {
            public uint dwEventType;
            public uint dwRestorePtType;
            public long llSequenceNumber;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxDescriptionW)]
            public string szDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StateMgrStatus
        {
            public uint nStatus;
            public long llSequenceNumber;
        }

        [DllImport("SrClient.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SRSetRestorePointW(
            ref RestorePointInfoW pRestorePtSpec,
            out StateMgrStatus pSMgrStatus);

        /// <summary>
        /// ドライバーインストール用の復元ポイント作成を開始します。
        /// 成功した場合はシーケンス番号を返し、<see cref="EndRestorePoint"/> に渡して完了させます。
        /// 失敗・非対応環境では null を返します(呼び出し側は処理を継続してよい)。
        /// </summary>
        /// <param name="description">復元ポイントの説明(256文字を超える場合は切り詰めます)</param>
        public static long? BeginRestorePoint(ILogger logger, string description)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (!OperatingSystem.IsWindows())
            {
                logger.LogDebug("Windows以外のためシステム復元ポイントをスキップします");
                return null;
            }

            // szDescription は固定長バッファ。終端分を残して切り詰める
            var desc = description ?? string.Empty;
            if (desc.Length > MaxDescriptionW - 1)
                desc = desc.Substring(0, MaxDescriptionW - 1);

            var info = new RestorePointInfoW
            {
                dwEventType = BeginSystemChange,
                dwRestorePtType = DeviceDriverInstall,
                llSequenceNumber = 0, // 開始時は0。成功すると status にシーケンス番号が返る
                szDescription = desc,
            };

            try
            {
                if (SRSetRestorePointW(ref info, out var status))
                {
                    logger.LogInformation(
                        "システム復元ポイントを作成しました (シーケンス番号: {Sequence}): {Description}",
                        status.llSequenceNumber, desc);
                    return status.llSequenceNumber;
                }

                // Windows 8以降は直近24時間以内に復元ポイントがあるとスキップされる。
                // システムの保護が無効・セーフモード・Server SKU でも失敗する
                logger.LogWarning(
                    "システム復元ポイントを作成できませんでした (nStatus: {Status})。" +
                    "システムの保護が無効、直近24時間以内に作成済み、または非対応環境の可能性があります",
                    status.nStatus);
                return null;
            }
            catch (Exception ex) when (
                ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
            {
                // SrClient.dll が無い環境(Server SKU / Nano / 一部のWindows構成)
                logger.LogWarning(ex,
                    "システム復元APIが利用できない環境のため復元ポイントをスキップします");
                return null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "システム復元ポイントの作成中にエラーが発生しました");
                return null;
            }
        }

        /// <summary>
        /// <see cref="BeginRestorePoint"/> で開始した復元ポイントを完了させます。
        /// BEGIN と対で呼ばないと復元ポイントが未完了のまま残るため、finally 等で必ず呼ぶこと。
        /// </summary>
        public static void EndRestorePoint(ILogger logger, long sequenceNumber)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (!OperatingSystem.IsWindows() || sequenceNumber == 0) return;

            var info = new RestorePointInfoW
            {
                dwEventType = EndSystemChange,
                dwRestorePtType = DeviceDriverInstall,
                // 完了時は BEGIN で得たシーケンス番号を必ず渡す(対応付けのため)
                llSequenceNumber = sequenceNumber,
                szDescription = string.Empty,
            };

            try
            {
                if (!SRSetRestorePointW(ref info, out var status))
                {
                    logger.LogWarning(
                        "システム復元ポイントの完了処理に失敗しました (シーケンス番号: {Sequence}, nStatus: {Status})",
                        sequenceNumber, status.nStatus);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "システム復元ポイントの完了処理中にエラーが発生しました (シーケンス番号: {Sequence})",
                    sequenceNumber);
            }
        }
    }
}
