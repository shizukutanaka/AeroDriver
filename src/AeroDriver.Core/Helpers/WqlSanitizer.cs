using System;
using System.Text.RegularExpressions;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// WQL クエリへの文字列埋め込みを安全にします。
    /// CimSession はパラメーター化クエリをサポートしないため、
    /// 埋め込み前にアローリストで検証します。
    /// </summary>
    public static class WqlSanitizer
    {
        // Windows デバイス ID に現れる文字のみ許可
        // 例: "PCI\VEN_10DE&DEV_2204&SUBSYS_..." or "USB\VID_0bda&PID_8153&..."
        private static readonly Regex AllowedDeviceId =
            new(@"^[A-Za-z0-9\\_\-&\.#\{\}]+$", RegexOptions.Compiled);

        /// <summary>
        /// DeviceID を WQL 文字列リテラルとして安全に埋め込みます。
        /// アローリスト外の文字が含まれる場合は ArgumentException をスローします。
        /// </summary>
        public static string SanitizeDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentException("DeviceID を空にすることはできません", nameof(deviceId));

            if (!AllowedDeviceId.IsMatch(deviceId))
                throw new ArgumentException(
                    $"DeviceID に無効な文字が含まれています: {deviceId}", nameof(deviceId));

            // アローリスト通過後も WQL リテラルエスケープを適用（多層防御）
            return deviceId.Replace("\\", "\\\\").Replace("'", "\\'");
        }
    }
}
