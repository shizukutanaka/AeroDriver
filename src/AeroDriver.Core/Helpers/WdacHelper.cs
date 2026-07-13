using System;
using System.Runtime.Versioning;
using Microsoft.Management.Infrastructure;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// Windows Defender Application Control (WDAC) / Device Guard の状態を検出します。
    ///
    /// 背景: 2026年4月より Windows 11 24H2/25H2/26H1 および Windows Server 2025 では
    /// クロス署名ドライバープログラムの信頼が段階的に廃止されます。
    /// カーネルが強制モード (EnforcementMode=1) の場合、
    /// WHQL 署名のないドライバーはインストールできません。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class WdacHelper
    {
        /// <summary>
        /// カーネルの CI (Code Integrity) ポリシーが強制モードかどうかを確認します。
        /// CimSession を使用して Win32_DeviceGuard を照会します。
        /// </summary>
        public static WdacStatus GetStatus(ILogger? logger = null)
        {
            try
            {
                using var session = CimSession.Create(null);
                var instances = session.QueryInstances(
                    @"root\Microsoft\Windows\DeviceGuard",
                    "WQL",
                    "SELECT * FROM Win32_DeviceGuard");

                foreach (var inst in instances)
                {
                    using (inst) // CimInstance はネイティブMIハンドルを保持するIDisposable
                    {
                        // CodeIntegrityPolicyEnforcementStatus:
                        //   0 = Off, 1 = AuditMode, 2 = EnforcementMode
                        var ciStatus = inst.CimInstanceProperties["CodeIntegrityPolicyEnforcementStatus"]?.Value;
                        int enforcement = ciStatus is uint u ? (int)u : (ciStatus is int i ? i : 0);

                        // UsermodeCodeIntegrityPolicyEnforcementStatus も確認
                        var umStatus = inst.CimInstanceProperties["UsermodeCodeIntegrityPolicyEnforcementStatus"]?.Value;
                        int umEnforcement = umStatus is uint uu ? (int)uu : (umStatus is int ii ? ii : 0);

                        return new WdacStatus
                        {
                            KernelModeEnforcement = (WdacEnforcementMode)enforcement,
                            UserModeEnforcement   = (WdacEnforcementMode)umEnforcement,
                        };
                    }
                }

                // Win32_DeviceGuard が取得できない場合 = WDAC 無効
                return WdacStatus.Disabled;
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "WDAC状態の取得に失敗しました（WDAC未構成の可能性）");
                return WdacStatus.Disabled;
            }
        }
    }

    public enum WdacEnforcementMode
    {
        Off         = 0,
        AuditMode   = 1,
        Enforcement = 2,
    }

    public sealed class WdacStatus
    {
        public WdacEnforcementMode KernelModeEnforcement { get; init; }
        public WdacEnforcementMode UserModeEnforcement   { get; init; }

        /// <summary>カーネルモードが強制モードの場合 true</summary>
        public bool IsKernelEnforced => KernelModeEnforcement == WdacEnforcementMode.Enforcement;

        /// <summary>監査モード（ログのみ、インストール自体は通る）の場合 true</summary>
        public bool IsAuditMode => KernelModeEnforcement == WdacEnforcementMode.AuditMode;

        public static readonly WdacStatus Disabled = new()
        {
            KernelModeEnforcement = WdacEnforcementMode.Off,
            UserModeEnforcement   = WdacEnforcementMode.Off,
        };
    }
}
