using System;
using System.Text.RegularExpressions;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// Windows のハードウェアIDを解析し、カタログ検索や照合に使える正規形を取り出します。
    ///
    /// 対応する標準フォーマット(Windows Driver Docs の device identifier 仕様に準拠):
    /// <list type="bullet">
    /// <item>PCI: <c>PCI\VEN_v(4)&amp;DEV_d(4)</c>(以降に <c>&amp;SUBSYS_s(8)</c>、<c>&amp;REV_r(2)</c> が続きうる)</item>
    /// <item>USB 単一インターフェース: <c>USB\VID_v(4)&amp;PID_d(4)</c>(<c>&amp;REV_r(4)</c> が続きうる)</item>
    /// <item>USB 複合デバイス: <c>USB\VID_v(4)&amp;PID_d(4)&amp;MI_z(2)</c>
    ///       (MI はインターフェース番号で<b>2桁</b>。REV の4桁と桁数が違う点に注意)</item>
    /// </list>
    ///
    /// バス種別ごとにフィールド名が異なる(PCI は VEN/DEV、USB は VID/PID)ため、
    /// 「VEN_ を探して見つからなければ諦める」実装では USB デバイスが常に未対応になります。
    /// </summary>
    public static class HardwareIdParser
    {
        // PCI\VEN_8086&DEV_1234... — 先頭のバス接頭辞は省略されている場合もあるため任意扱い
        private static readonly Regex PciPattern = new(
            @"(?:^|\\|\b)VEN_(?<vendor>[0-9A-F]{4})&DEV_(?<product>[0-9A-F]{4})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // USB\VID_046D&PID_C52B
        private static readonly Regex UsbPattern = new(
            @"(?:^|\\|\b)VID_(?<vendor>[0-9A-F]{4})&PID_(?<product>[0-9A-F]{4})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // MI(複合デバイスのインターフェース番号、2桁)は独立して探す。
        // Windows は USB\VID_x&PID_x&REV_x&MI_x のように REV を挟んだ形も生成するため、
        // VID/PID の直後に連結する前提で書くと MI を取りこぼす
        private static readonly Regex UsbInterfacePattern = new(
            @"&MI_(?<mi>[0-9A-F]{2})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>解析済みのハードウェアID。</summary>
        public sealed record ParsedHardwareId(
            string Bus,
            string VendorId,
            string ProductId,
            string? InterfaceNumber)
        {
            /// <summary>
            /// バス修飾された正規形。カタログ検索クエリにそのまま使えます。
            /// 例: <c>PCI\VEN_8086&amp;DEV_1234</c> / <c>USB\VID_046D&amp;PID_C52B</c>
            /// </summary>
            public string CoreId => Bus == "USB"
                ? $"USB\\VID_{VendorId}&PID_{ProductId}"
                : $"PCI\\VEN_{VendorId}&DEV_{ProductId}";

            /// <summary>
            /// 複合USBデバイスのインターフェースまで含めた形。インターフェース番号が無い場合は
            /// <see cref="CoreId"/> と同じ。複合デバイスはインターフェースごとに別のドライバーが
            /// 割り当たるため、厳密な照合ではこちらを使います。
            /// </summary>
            public string QualifiedId => InterfaceNumber == null
                ? CoreId
                : $"{CoreId}&MI_{InterfaceNumber}";
        }

        /// <summary>
        /// ハードウェアIDを解析します。PCI と USB のどちらでもない、または
        /// ベンダー/プロダクトIDを抽出できない場合は false を返します。
        /// </summary>
        public static bool TryParse(string? hardwareId, out ParsedHardwareId? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(hardwareId)) return false;

            // USB を先に判定する: USB\... に VEN_/DEV_ が現れることはないが、
            // 判定順を固定しておくことで将来フィールドが増えても挙動が安定する
            var usb = UsbPattern.Match(hardwareId);
            if (usb.Success)
            {
                var mi = UsbInterfacePattern.Match(hardwareId);
                parsed = new ParsedHardwareId(
                    Bus: "USB",
                    VendorId: usb.Groups["vendor"].Value.ToUpperInvariant(),
                    ProductId: usb.Groups["product"].Value.ToUpperInvariant(),
                    InterfaceNumber: mi.Success ? mi.Groups["mi"].Value.ToUpperInvariant() : null);
                return true;
            }

            var pci = PciPattern.Match(hardwareId);
            if (pci.Success)
            {
                parsed = new ParsedHardwareId(
                    Bus: "PCI",
                    VendorId: pci.Groups["vendor"].Value.ToUpperInvariant(),
                    ProductId: pci.Groups["product"].Value.ToUpperInvariant(),
                    InterfaceNumber: null);
                return true;
            }

            return false;
        }
    }
}
