// System.CommandLine (2.0.0-beta4) の **動く** 最小実装と、CLI が必要とする周辺の実装。
//
// tools/cli-typecheck の同名スタブは `InvokeAsync` が常に 0 を返し `SetHandler` が
// 何もしないため、**CLI のハンドラーは一度も実行されていなかった**。
// ここでは実際に argv を解釈してハンドラーへ配送し、終了コードを返す。
//
// 重要な限界: これは System.CommandLine そのものではなく**代役のパーサー**である。
// したがって System.CommandLine の実パース挙動は依然として未検証で、
// ここで検証されるのは **CLI 側のハンドラーと終了コードの配線**(製品自身のコード)。
using System.Globalization;

namespace System.CommandLine
{
    public abstract class Symbol
    {
        public string Name { get; protected set; } = string.Empty;
        public string? Description { get; set; }
    }

    public abstract class OptionBase : Symbol
    {
        /// <summary>argv から取り出した文字列(複数回指定は複数要素)を型付きの値にする。</summary>
        internal abstract object? Materialize(List<string> raw, bool present);
        /// <summary>フラグ(値を取らない)か。bool のオプションだけが true。</summary>
        internal abstract bool IsFlag { get; }
    }

    public sealed class Option<T> : OptionBase
    {
        private readonly Func<T>? _defaultValueFactory;

        public Option(string name, string? description = null)
        {
            Name = name;
            Description = description;
        }

        public Option(string name, Func<T> getDefaultValue, string? description = null)
            : this(name, description)
            => _defaultValueFactory = getDefaultValue;

        internal override bool IsFlag => typeof(T) == typeof(bool);

        internal override object? Materialize(List<string> raw, bool present)
        {
            var t = typeof(T);

            if (t == typeof(bool))
                return present;

            if (t == typeof(string[]))
                return raw.ToArray();

            if (!present || raw.Count == 0)
                return _defaultValueFactory is not null ? _defaultValueFactory() : default(T);

            var last = raw[^1];

            if (t == typeof(int))
            {
                if (!int.TryParse(last, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    throw new CommandLineParseException($"オプション {Name} の値が整数ではありません: {last}");
                return n;
            }

            return last; // string / string?
        }
    }

    /// <summary>argv の解釈に失敗した(未知のコマンド、値の欠落など)。</summary>
    public sealed class CommandLineParseException : Exception
    {
        public CommandLineParseException(string message) : base(message) { }
    }

    public class Command : Symbol, Collections.IEnumerable
    {
        internal readonly List<OptionBase> Options = new();
        internal readonly List<Command> Subcommands = new();
        internal Func<Dictionary<OptionBase, object?>, Task>? Handler;

        public Command(string name, string? description = null)
        {
            Name = name;
            Description = description;
        }

        // コレクション初期化子 `new Command("x") { opt1, opt2 }` を受けるための Add
        public void Add(Symbol s)
        {
            if (s is OptionBase o) Options.Add(o);
            else if (s is Command c) Subcommands.Add(c);
        }

        public Collections.IEnumerator GetEnumerator() => Options.GetEnumerator();

        public void AddCommand(Command c) => Subcommands.Add(c);

        public void SetHandler(Func<Task> handler)
            => Handler = _ => handler();

        public void SetHandler<T>(Func<T, Task> handler, Option<T> o1)
            => Handler = values => handler((T)values[o1]!);

        public void SetHandler<T1, T2>(Func<T1, T2, Task> handler, Option<T1> o1, Option<T2> o2)
            => Handler = values => handler((T1)values[o1]!, (T2)values[o2]!);

        /// <summary>このコマンドの引数を解釈してハンドラーを実行する。</summary>
        internal async Task<int> RunAsync(string[] args, int index, TextWriter error)
        {
            // サブコマンドがあれば先に振り分ける
            if (index < args.Length && !args[index].StartsWith('-'))
            {
                var sub = Subcommands.FirstOrDefault(c =>
                    string.Equals(c.Name, args[index], StringComparison.Ordinal));
                if (sub is null)
                {
                    error.WriteLine($"'{args[index]}' は認識されないコマンドです。");
                    return 1;
                }
                return await sub.RunAsync(args, index + 1, error).ConfigureAwait(false);
            }

            if (Handler is null)
            {
                // ハンドラーが無い(ルートを引数なしで呼んだ等)ときはヘルプ相当
                WriteHelp(Console.Out);
                return index >= args.Length ? 1 : 0;
            }

            var raw = new Dictionary<OptionBase, List<string>>();
            var present = new HashSet<OptionBase>();
            foreach (var o in Options)
                raw[o] = new List<string>();

            for (int i = index; i < args.Length; i++)
            {
                var token = args[i];
                var opt = Options.FirstOrDefault(o =>
                    string.Equals(o.Name, token, StringComparison.Ordinal));
                if (opt is null)
                {
                    error.WriteLine($"'{token}' は認識されないオプションです。");
                    return 1;
                }

                present.Add(opt);
                if (opt.IsFlag) continue;

                if (i + 1 >= args.Length)
                {
                    error.WriteLine($"オプション {opt.Name} に値が必要です。");
                    return 1;
                }
                raw[opt].Add(args[++i]);
            }

            var values = new Dictionary<OptionBase, object?>();
            foreach (var o in Options)
            {
                try
                {
                    values[o] = o.Materialize(raw[o], present.Contains(o));
                }
                catch (CommandLineParseException ex)
                {
                    error.WriteLine(ex.Message);
                    return 1;
                }
            }

            await Handler(values).ConfigureAwait(false);
            return 0;
        }

        internal void WriteHelp(TextWriter output)
        {
            output.WriteLine(Description ?? Name);
            if (Subcommands.Count > 0)
            {
                output.WriteLine();
                output.WriteLine("Commands:");
                foreach (var c in Subcommands)
                    output.WriteLine($"  {c.Name,-12} {c.Description}");
            }
            if (Options.Count > 0)
            {
                output.WriteLine();
                output.WriteLine("Options:");
                foreach (var o in Options)
                    output.WriteLine($"  {o.Name,-16} {o.Description}");
            }
        }
    }

    public class RootCommand : Command
    {
        public RootCommand(string? description = null) : base("aerodriver", description) { }

        public async Task<int> InvokeAsync(string[] args)
        {
            // --help / -h はどの階層でも受ける(System.CommandLine と同じ)
            if (args.Length == 0 || args.Any(a => a is "--help" or "-h" or "-?"))
            {
                Command target = this;
                foreach (var a in args)
                {
                    var sub = target.Subcommands.FirstOrDefault(c =>
                        string.Equals(c.Name, a, StringComparison.Ordinal));
                    if (sub is not null) target = sub;
                }
                target.WriteHelp(Console.Out);
                return 0;
            }

            return await RunAsync(args, 0, Console.Error).ConfigureAwait(false);
        }
    }
}

namespace AeroDriver.Languages.Services
{
    public interface ILanguageService
    {
        string GetString(string name);
        string GetString(string name, CultureInfo culture);
        string GetString(string name, params object[] args);
        CultureInfo CurrentCulture { get; }
        void SetCulture(CultureInfo culture);
        IReadOnlyList<CultureInfo> SupportedCultures { get; }
    }

    /// <summary>
    /// キー名をそのまま返すローカライズ実装。resx の実解決は
    /// <c>tools/lang-run</c> が別途検証しているので、ここでは
    /// 「散文がリソース経由か」を出力から判別できることを優先する。
    /// </summary>
    public sealed class LanguageService : ILanguageService
    {
        public string GetString(string name) => $"[{name}]";
        public string GetString(string name, CultureInfo culture) => GetString(name);
        public string GetString(string name, params object[] args) => GetString(name);
        public CultureInfo CurrentCulture { get; private set; } = new("en-US");
        public void SetCulture(CultureInfo culture) => CurrentCulture = culture;
        public IReadOnlyList<CultureInfo> SupportedCultures { get; } =
            new[] { new CultureInfo("en-US"), new CultureInfo("ja-JP") };
    }
}
