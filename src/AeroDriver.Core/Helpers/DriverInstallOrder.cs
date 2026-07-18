using System.Collections.Generic;
using System.Linq;
using AeroDriver.Core.Models;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// 複数ドライバーを一括インストールする際の推奨順序を決めるヒューリスティック。
    /// チップセット/システム基盤 → ストレージ → バス(USB) → ネットワーク → その他 → GPU(表示)
    /// の順に並べる。GPU ドライバーはチップセットやバスが先に用意されている前提で動くことが
    /// 多いため最後に回す（例: 「チップセット before GPU」）。
    ///
    /// 判定は <see cref="DriverInfo.DeviceClass"/>（Win32_PnPSignedDriver.DeviceClass の値、
    /// 大文字小文字の揺れあり）に基づく純粋関数。WMI 等の副作用は持たないためテスト容易。
    /// </summary>
    public static class DriverInstallOrder
    {
        // DeviceClass 文字列 → 優先度（小さいほど先にインストール）。
        // 未知のクラスは DefaultPriority（その他扱い、GPU より前）にフォールバックする。
        private static readonly Dictionary<string, int> ClassPriority = new(System.StringComparer.OrdinalIgnoreCase)
        {
            // チップセット/システム基盤（PCI バス・ブリッジ等）を最優先
            ["SYSTEM"]      = 0,
            ["COMPUTER"]    = 0,
            ["PROCESSOR"]   = 1,
            // ストレージコントローラー（後続デバイスの土台）
            ["HDC"]         = 2, // ハードディスクコントローラー
            ["SCSIADAPTER"] = 2,
            ["DISKDRIVE"]   = 3,
            // バス
            ["USB"]         = 4,
            // 入力・ネットワーク等の一般周辺機器
            ["NET"]         = 5,
            ["HIDCLASS"]    = 6,
            ["KEYBOARD"]    = 6,
            ["MOUSE"]       = 6,
            // 音声はGPUより前で問題ない
            ["MEDIA"]       = 7,
            // GPU/ディスプレイは最後（チップセット・バス依存のため）
            ["DISPLAY"]     = 100,
        };

        private const int DefaultPriority = 50; // 未知クラス = その他（GPU より前、基盤より後）

        /// <summary>
        /// ドライバー群をインストール推奨順に並べ替えて返す。
        /// 同一優先度内の相対順序は入力順を維持する（OrderBy の安定ソート）。
        /// </summary>
        public static IReadOnlyList<DriverInfo> Sort(IEnumerable<DriverInfo> drivers)
            => drivers.OrderBy(GetPriority).ToList();

        /// <summary>
        /// 単一ドライバーのインストール優先度（小さいほど先）。DeviceClass が未設定/未知の
        /// 場合は <see cref="DefaultPriority"/>。
        /// </summary>
        public static int GetPriority(DriverInfo driver)
        {
            // DeviceClass 優先。未設定でも DISPLAY 相当なら GPU 扱いにする補助判定を入れる
            if (!string.IsNullOrEmpty(driver.DeviceClass) &&
                ClassPriority.TryGetValue(driver.DeviceClass, out var p))
                return p;

            // DeviceClass が取れないケースの保険: IsGraphicsDriver フラグで GPU を最後に回す
            if (driver.IsGraphicsDriver)
                return ClassPriority["DISPLAY"];

            return DefaultPriority;
        }
    }
}
