using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AeroDriver.Core.Interfaces;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// 設定項目の名前 ⇔ <see cref="ISettingsService"/> のプロパティを結ぶ表。
    /// <para>
    /// 設定は Core が尊重しているのに GUI/CLI のどこからも変更できず、
    /// 設定ファイルを手で編集するしかない状態が続いていた。UI 側に
    /// <c>if (key == "...")</c> の連鎖を書くと二重管理になるため、
    /// 「どの設定が存在し、どう読み書きするか」をここ1箇所に集約する。
    /// </para>
    /// UI 非依存の純粋ロジックなので <c>tools/offline-verify</c> で実行検証できる。
    /// </summary>
    public static class SettingsKeys
    {
        /// <summary>設定1件の定義。</summary>
        public sealed class Entry
        {
            public required string Name { get; init; }
            public required string Description { get; init; }
            /// <summary>現在値を表示用文字列にする。</summary>
            public required Func<ISettingsService, string> Read { get; init; }
            /// <summary>文字列を解釈して書き込む。解釈できなければ false（値は変更しない）。</summary>
            public required Func<ISettingsService, string, bool> Write { get; init; }
            /// <summary>
            /// 値が受理できるかだけを判定する（<b>書き込まない</b>）。
            /// 複数の代入をまとめて適用する経路で「1件でも不正なら何も変更しない」を
            /// 実現するために必要。<see cref="Write"/> は検証と適用が同一操作なので、
            /// これが無いと先行する代入が適用済みのまま後続で失敗しうる。
            /// </summary>
            public required Func<string, bool> IsValid { get; init; }
            /// <summary>取りうる値の説明（ヘルプ表示用）。</summary>
            public required string ValueSyntax { get; init; }
        }

        /// <summary>
        /// 真偽値の解釈。<c>true/false</c> に加えて <c>on/off</c>・<c>yes/no</c>・<c>1/0</c> を受け付ける。
        /// CLI では <c>--set backup=on</c> のような書き方が自然なため。
        /// </summary>
        public static bool TryParseBool(string? text, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(text)) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "true": case "on": case "yes": case "y": case "1":
                    value = true; return true;
                case "false": case "off": case "no": case "n": case "0":
                    value = false; return true;
                default:
                    return false;
            }
        }

        private static string Fmt(bool b) => b ? "true" : "false";

        /// <summary>保持世代数として受理できるか(1 以上の整数)。</summary>
        private static bool IsValidGenerationCount(string? text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 1;

        /// <summary>変更可能な全設定。表示順は重要度順。</summary>
        public static IReadOnlyList<Entry> All { get; } = new List<Entry>
        {
            new()
            {
                Name = "restore-point",
                Description = "インストール前に Windows のシステム復元ポイントを作成する",
                ValueSyntax = "true|false",
                Read = s => Fmt(s.CreateRestorePoint),
                Write = (s, v) => { if (!TryParseBool(v, out var b)) return false; s.CreateRestorePoint = b; return true; },
                IsValid = v => TryParseBool(v, out _),
            },
            new()
            {
                Name = "backup",
                Description = "インストール前に現在のドライバーをバックアップする",
                ValueSyntax = "true|false",
                Read = s => Fmt(s.BackupEnabled),
                Write = (s, v) => { if (!TryParseBool(v, out var b)) return false; s.BackupEnabled = b; return true; },
                IsValid = v => TryParseBool(v, out _),
            },
            new()
            {
                Name = "backup-generations",
                Description = "保持するバックアップ世代数（1 以上）",
                ValueSyntax = "1 以上の整数",
                Read = s => s.MaxBackupGenerations.ToString(CultureInfo.InvariantCulture),
                Write = (s, v) =>
                {
                    if (!IsValidGenerationCount(v, out var n)) return false;
                    s.MaxBackupGenerations = n;
                    return true;
                },
                IsValid = v => IsValidGenerationCount(v, out _),
            },
            new()
            {
                Name = "auto-check",
                Description = "GUI 起動時に更新確認を自動実行する",
                ValueSyntax = "true|false",
                Read = s => Fmt(s.AutoUpdateEnabled),
                Write = (s, v) => { if (!TryParseBool(v, out var b)) return false; s.AutoUpdateEnabled = b; return true; },
                IsValid = v => TryParseBool(v, out _),
            },
            new()
            {
                Name = "include-beta",
                Description = "ベータ（プレリリース）版のドライバーも更新候補に含める",
                ValueSyntax = "true|false",
                Read = s => Fmt(s.IncludeBetaDrivers),
                Write = (s, v) => { if (!TryParseBool(v, out var b)) return false; s.IncludeBetaDrivers = b; return true; },
                IsValid = v => TryParseBool(v, out _),
            },
        };

        /// <summary>名前から設定を引く（大文字小文字を区別しない）。無ければ null。</summary>
        public static Entry? Find(string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? null
                : All.FirstOrDefault(e => string.Equals(e.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// <c>key=value</c> 形式を分解します。<c>=</c> が無い、キーが空、
        /// 未知のキーの場合は false。値に <c>=</c> が含まれてもよいよう最初の1つで分割する。
        /// </summary>
        public static bool TryParseAssignment(string? text, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;

            int i = text.IndexOf('=');
            if (i <= 0) return false;

            key = text[..i].Trim();
            value = text[(i + 1)..].Trim();
            return key.Length > 0;
        }

        /// <summary>
        /// <c>key=value</c> を解釈して設定に適用します。適用できたら true。
        /// 未知のキー・解釈できない値では**何も変更せず** false を返す。
        /// </summary>
        /// <summary>
        /// 代入を<b>適用せずに</b>受理可能か判定する。複数件をまとめて適用する経路が
        /// 「1件でも不正なら何も変更しない」を守るために、全件をこれで先に検証すること。
        /// </summary>
        public static bool TryValidate(string? assignment, out string error)
        {
            if (!TryParseAssignment(assignment, out var key, out var value))
            {
                error = "書式が正しくありません。key=value の形式で指定してください。";
                return false;
            }

            var entry = Find(key);
            if (entry == null)
            {
                error = $"未知の設定キーです: {key}";
                return false;
            }

            if (!entry.IsValid(value))
            {
                error = $"{entry.Name} に指定できない値です: {value} (期待: {entry.ValueSyntax})";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool TryApply(ISettingsService settings, string? assignment, out string error)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (!TryParseAssignment(assignment, out var key, out var value))
            {
                error = "書式が正しくありません。key=value の形式で指定してください。";
                return false;
            }

            var entry = Find(key);
            if (entry == null)
            {
                error = $"未知の設定キーです: {key}";
                return false;
            }

            if (!entry.Write(settings, value))
            {
                error = $"{entry.Name} に指定できない値です: {value} (期待: {entry.ValueSyntax})";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
