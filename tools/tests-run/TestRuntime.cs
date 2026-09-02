// xunit / FluentAssertions / NSubstitute の **動く** 最小実装。
//
// tools/tests-typecheck は同じ3つをスタブ化してテストコードを型検査するが、
// 表明は何も評価せず自分自身を返すだけで、テストは一度も走っていなかった。
// xunit はテストを走らせる「手段」であって目的ではない — ViewModel に対して
// 同じ論法で ui-run を作ったのと同様に、ここでは表明を本物にし、
// NSubstitute のモックを BCL の DispatchProxy で再現して、
// **テストスイートを実際に実行する**。
//
// 実装範囲は tests/ が実際に使う面に限る(Received()/When() 等は未使用なので無い)。
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)]
    public class FactAttribute : Attribute
    {
        public string? DisplayName { get; set; }
        public string? Skip { get; set; }
    }

    public sealed class TheoryAttribute : FactAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataAttribute : Attribute
    {
        public object?[] Data { get; }

        public InlineDataAttribute(params object?[] data)
            // [InlineData(null)] は C# の params 規則により配列そのものが null になる。
            // xunit と同じく「null 引数1個」として扱う(配列が空になると引数なし呼び出しになり
            // TargetParameterCountException で落ちる)
            => Data = data ?? new object?[] { null };
    }

    /// <summary>テストクラスの非同期セットアップ/後始末。</summary>
    public interface IAsyncLifetime
    {
        Task InitializeAsync();
        Task DisposeAsync();
    }
}

namespace FluentAssertions
{
    /// <summary>表明が満たされなかったときに投げる例外。</summary>
    public sealed class AssertionFailedException : Exception
    {
        public AssertionFailedException(string message) : base(message) { }
    }

    internal static class Fail
    {
        public static void If(bool condition, string message, string because)
        {
            if (!condition)
                throw new AssertionFailedException(
                    message + (string.IsNullOrEmpty(because) ? "" : $" (理由: {because})"));
        }

        public static string Show(object? v) => v switch
        {
            null => "null",
            string s => $"\"{s}\"",
            System.Collections.IEnumerable e and not string =>
                "[" + string.Join(", ", e.Cast<object?>().Take(8).Select(Show)) + "]",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "null",
        };

        /// <summary>xunit/FluentAssertions と同じく、値の等価性で比較する。</summary>
        public static bool Eq(object? a, object? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            // 数値は型が違っても値が同じなら等しいとみなす(InlineData の int と long など)
            if (a is IConvertible && b is IConvertible && a is not string && b is not string)
            {
                try
                {
                    return Convert.ToDecimal(a, CultureInfo.InvariantCulture)
                        == Convert.ToDecimal(b, CultureInfo.InvariantCulture);
                }
                catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
                {
                    // 数値として比較できない型(bool と string 等)は通常の等価比較へ落とす
                }
            }
            if (a is System.Collections.IEnumerable ea and not string &&
                b is System.Collections.IEnumerable eb and not string)
                return ea.Cast<object?>().SequenceEqual(eb.Cast<object?>());
            return a.Equals(b);
        }
    }

    public class Assertions<T>
    {
        private readonly T _subject;
        public Assertions(T subject) => _subject = subject;

        private IEnumerable<object?> Items()
        {
            if (_subject is System.Collections.IEnumerable e and not string)
                return e.Cast<object?>();
            throw new AssertionFailedException(
                $"コレクション向けの表明を非コレクション({typeof(T).Name})に適用した");
        }

        public Assertions<T> Be(object? expected, string because = "", params object[] args)
        {
            Fail.If(Fail.Eq(_subject, expected),
                $"期待 {Fail.Show(expected)} だが実際は {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> NotBe(object? unexpected, string because = "", params object[] args)
        {
            Fail.If(!Fail.Eq(_subject, unexpected),
                $"{Fail.Show(unexpected)} でないはずが一致した", because);
            return this;
        }

        public Assertions<T> BeNull(string because = "", params object[] args)
        {
            Fail.If(_subject is null, $"null のはずが {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> NotBeNull(string because = "", params object[] args)
        {
            Fail.If(_subject is not null, "null であってはならない", because);
            return this;
        }

        public Assertions<T> BeTrue(string because = "", params object[] args)
        {
            Fail.If(_subject is true, $"true のはずが {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> BeFalse(string because = "", params object[] args)
        {
            Fail.If(_subject is false, $"false のはずが {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> BeSameAs(object? expected, string because = "", params object[] args)
        {
            Fail.If(ReferenceEquals(_subject, expected), "同一インスタンスであるべき", because);
            return this;
        }

        public Assertions<T> BePositive(string because = "", params object[] args)
        {
            var d = Convert.ToDecimal(_subject, CultureInfo.InvariantCulture);
            Fail.If(d > 0, $"正の数のはずが {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> BeLessThan(object? expected, string because = "", params object[] args)
        {
            var a = Convert.ToDecimal(_subject, CultureInfo.InvariantCulture);
            var b = Convert.ToDecimal(expected, CultureInfo.InvariantCulture);
            Fail.If(a < b, $"{Fail.Show(_subject)} < {Fail.Show(expected)} のはず", because);
            return this;
        }

        public Assertions<T> NotBeNullOrWhiteSpace(string because = "", params object[] args)
        {
            Fail.If(!string.IsNullOrWhiteSpace(_subject as string),
                $"空でない文字列のはずが {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> BeEmpty(string because = "", params object[] args)
        {
            Fail.If(!Items().Any(), $"空のはずが {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> HaveCount(int expected, string because = "", params object[] args)
        {
            var n = Items().Count();
            Fail.If(n == expected, $"{expected} 件のはずが {n} 件: {Fail.Show(_subject)}", because);
            return this;
        }

        // string 向け
        public Assertions<T> Contain(string expected, string because = "", params object[] args)
        {
            if (_subject is string s)
            {
                Fail.If(s.Contains(expected, StringComparison.Ordinal),
                    $"\"{expected}\" を含むはずが {Fail.Show(_subject)}", because);
                return this;
            }
            Fail.If(Items().Any(x => Fail.Eq(x, expected)),
                $"{Fail.Show(expected)} を含むはずが {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> NotContain(string unexpected, string because = "", params object[] args)
        {
            if (_subject is string s)
            {
                Fail.If(!s.Contains(unexpected, StringComparison.Ordinal),
                    $"\"{unexpected}\" を含んではならない: {Fail.Show(_subject)}", because);
                return this;
            }
            Fail.If(!Items().Any(x => Fail.Eq(x, unexpected)),
                $"{Fail.Show(unexpected)} を含んではならない", because);
            return this;
        }

        // 述語版。型検査ハーネスと同じく Func<dynamic,bool> で受ける
        // (C# のオーバーロード解決では Should<T>(this T) が
        //  Should<TItem>(this IEnumerable<TItem>) より優先され要素型を推論できないため)
        public Assertions<T> Contain(Func<dynamic, bool> predicate, string because = "", params object[] args)
        {
            Fail.If(Items().Any(x => predicate(x!)),
                $"述語を満たす要素が無い: {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> NotContain(Func<dynamic, bool> predicate, string because = "", params object[] args)
        {
            Fail.If(!Items().Any(x => predicate(x!)),
                $"述語を満たす要素があってはならない: {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> OnlyContain(Func<dynamic, bool> predicate, string because = "", params object[] args)
        {
            Fail.If(Items().All(x => predicate(x!)),
                $"全要素が述語を満たすはず: {Fail.Show(_subject)}", because);
            return this;
        }

        public Assertions<T> ContainInOrder(params object?[] expected)
        {
            var actual = Items().ToList();
            int i = 0;
            foreach (var item in actual)
                if (i < expected.Length && Fail.Eq(item, expected[i])) i++;
            Fail.If(i == expected.Length,
                $"{Fail.Show(expected)} をこの順で含むはずが {Fail.Show(_subject)}", "");
            return this;
        }

        public Assertions<T> BeSubsetOf(System.Collections.IEnumerable superset, string because = "", params object[] args)
        {
            var super = superset.Cast<object?>().ToList();
            var missing = Items().Where(x => !super.Any(y => Fail.Eq(x, y))).ToList();
            Fail.If(missing.Count == 0,
                $"部分集合のはずが余分な要素 {Fail.Show(missing)}", because);
            return this;
        }
    }

    public class ActionAssertions
    {
        private readonly Action _action;
        public ActionAssertions(Action action) => _action = action;

        public ActionAssertions Throw<TException>(string because = "", params object[] args)
            where TException : Exception
        {
            try
            {
                _action();
            }
            catch (TException)
            {
                return this;
            }
            catch (Exception ex)
            {
                throw new AssertionFailedException(
                    $"{typeof(TException).Name} を期待したが {ex.GetType().Name}: {ex.Message}");
            }
            throw new AssertionFailedException($"{typeof(TException).Name} を期待したが例外が出なかった");
        }

        public ActionAssertions NotThrow(string because = "", params object[] args)
        {
            try
            {
                _action();
            }
            catch (Exception ex)
            {
                throw new AssertionFailedException($"例外が出ないはずが {ex.GetType().Name}: {ex.Message}");
            }
            return this;
        }
    }

    public class AsyncAssertions
    {
        private readonly Func<Task> _func;
        public AsyncAssertions(Func<Task> func) => _func = func;

        public async Task ThrowAsync<TException>(string because = "", params object[] args)
            where TException : Exception
        {
            try
            {
                await _func().ConfigureAwait(false);
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertionFailedException(
                    $"{typeof(TException).Name} を期待したが {ex.GetType().Name}: {ex.Message}");
            }
            throw new AssertionFailedException($"{typeof(TException).Name} を期待したが例外が出なかった");
        }

        public async Task NotThrowAsync(string because = "", params object[] args)
        {
            try
            {
                await _func().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new AssertionFailedException($"例外が出ないはずが {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public static class AssertionExtensions
    {
        public static ActionAssertions Should(this Action subject) => new(subject);
        public static AsyncAssertions Should(this Func<Task> subject) => new(subject);
        public static Assertions<T> Should<T>(this T subject) => new(subject);
    }
}

namespace NSubstitute
{
    /// <summary>直前に呼ばれたモックのメンバー。`.Returns()` はこれを見て設定する。</summary>
    internal sealed class LastCall
    {
        [ThreadStatic] public static LastCall? Current;
        public required SubstituteProxy Proxy { get; init; }
        public required string Key { get; init; }
    }

    /// <summary>
    /// DispatchProxy による NSubstitute 相当。tests/ が実際に使う面のみ:
    /// メンバー呼び出し → `.Returns(値)` / `.Returns(関数)` / `.ThrowsAsync(例外)`、
    /// および `Arg.Any&lt;T&gt;()` によるワイルドカード。
    /// `Received()` 等は tests/ が使っていないので実装しない。
    /// </summary>
    public class SubstituteProxy : DispatchProxy
    {
        internal readonly Dictionary<string, Func<object?[]?, object?>> Config = new();
        /// <summary>実際に呼ばれたメンバーと引数。`Received()` の検証に使う。</summary>
        internal readonly List<(string Name, object?[] Args)> Calls = new();
        /// <summary>非 null の間は「次の1呼び出しは検証」の意味(`Received(n)` が設定する)。</summary>
        internal int? ExpectedCount;

        internal static string KeyOf(MethodInfo m, object?[]? args, bool wildcard) =>
            wildcard || args is null || args.Length == 0
                ? m.Name
                : m.Name + "(" + string.Join(",", args.Select(a => a?.ToString() ?? "null")) + ")";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;

            bool wildcard = Arg.ConsumeMatchers();
            var exact = KeyOf(targetMethod, args, wildcard: false);
            var loose = targetMethod.Name;

            // Received(n) / DidNotReceive() の直後の呼び出しは「実行」ではなく「検証」
            if (ExpectedCount is int expected)
            {
                ExpectedCount = null;
                var matched = Calls.Where(c => c.Name == loose)
                    .Count(c => wildcard || ArgsEqual(c.Args, args));
                if (matched != expected)
                    throw new FluentAssertions.AssertionFailedException(
                        $"{loose} の呼び出し回数が期待 {expected} に対して実際 {matched}");
                return Default(targetMethod.ReturnType);
            }

            Calls.Add((loose, args ?? Array.Empty<object?>()));

            // 設定済みなら返す。引数付きの設定を優先し、無ければメソッド名だけの設定を使う
            LastCall.Current = new LastCall { Proxy = this, Key = wildcard ? loose : exact };
            if (Config.TryGetValue(exact, out var f) || Config.TryGetValue(loose, out f))
                return f(args);

            return Default(targetMethod.ReturnType);
        }

        private static bool ArgsEqual(object?[] a, object?[]? b)
        {
            b ??= Array.Empty<object?>();
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!Equals(a[i], b[i])) return false;
            return true;
        }

        /// <summary>未設定メンバーの既定値。Task 系は完了済みタスクを返す
        /// (`.Returns()` 拡張が Task&lt;T&gt; に対して定義されているため、null では設定できない)。</summary>
        internal static object? Default(Type t)
        {
            if (t == typeof(void)) return null;
            if (t == typeof(Task)) return Task.CompletedTask;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = t.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(inner).Invoke(null, new[] { Default(inner) });
            }
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }

    public static class Substitute
    {
        public static T For<T>() where T : class
        {
            if (!typeof(T).IsInterface)
                throw new NotSupportedException(
                    $"{typeof(T).Name}: このハーネスはインターフェースのモックのみ対応する");
            return DispatchProxy.Create<T, SubstituteProxy>();
        }
    }

    /// <summary>引数マッチャー。呼び出し1回分のワイルドカード指定として記録する。</summary>
    public static class Arg
    {
        [ThreadStatic] private static int _pending;

        public static T Any<T>()
        {
            _pending++;
            return default!;
        }

        internal static bool ConsumeMatchers()
        {
            bool had = _pending > 0;
            _pending = 0;
            return had;
        }
    }

    public sealed class ConfiguredCall { }

    public class CallInfo
    {
        private readonly object?[]? _args;
        internal CallInfo(object?[]? args) => _args = args;
        public T Arg<T>() => _args is null ? default! : _args.OfType<T>().FirstOrDefault()!;
        public object? this[int index] => _args?[index];
    }

    public static class SubstituteExtensions
    {
        private static ConfiguredCall Configure(Func<object?[]?, object?> factory)
        {
            var call = LastCall.Current
                ?? throw new InvalidOperationException(
                    ".Returns() の直前にモックのメンバー呼び出しがない");
            call.Proxy.Config[call.Key] = factory;
            // 設定のための呼び出しは「実際の呼び出し」ではない。Received() の回数に
            // 数えないよう、直前に記録した1件を取り消す(NSubstitute と同じ挙動)
            if (call.Proxy.Calls.Count > 0)
                call.Proxy.Calls.RemoveAt(call.Proxy.Calls.Count - 1);
            LastCall.Current = null;
            return new ConfiguredCall();
        }

        public static ConfiguredCall Returns<T>(this T value, T returnThis, params T[] returnThese)
            => Configure(_ => returnThis);

        // Task<T> を返すメンバーに T の値をそのまま設定できるようにする
        public static ConfiguredCall Returns<T>(this Task<T> value, T returnThis, params T[] returnThese)
            => Configure(_ => Task.FromResult(returnThis));

        public static ConfiguredCall Returns<T>(this Task<T> value, Func<CallInfo, T> returnThis)
            => Configure(args =>
            {
                // 関数が例外を投げる設定(キャンセル再現など)は、その場で伝播させる
                var result = returnThis(new CallInfo(args));
                return Task.FromResult(result);
            });

        /// <summary>次の1呼び出しを「n 回呼ばれたか」の検証にする(実行はしない)。</summary>
        public static T Received<T>(this T substitute, int requiredNumberOfCalls) where T : class
        {
            Proxy(substitute).ExpectedCount = requiredNumberOfCalls;
            return substitute;
        }

        public static T Received<T>(this T substitute) where T : class
            => substitute.Received(1);

        public static T DidNotReceive<T>(this T substitute) where T : class
            => substitute.Received(0);

        private static SubstituteProxy Proxy(object substitute)
            => substitute as SubstituteProxy
               ?? throw new InvalidOperationException(
                   "Received()/DidNotReceive() は Substitute.For<T>() で作ったモックにのみ使える");
    }
}

namespace NSubstitute.ExceptionExtensions
{
    public static class ExceptionExtensions
    {
        public static ConfiguredCall Throws<T>(this T value, Exception ex)
            => Configure(_ => throw ex);

        public static ConfiguredCall ThrowsAsync<T>(this Task<T> value, Exception ex)
            => Configure(_ => throw ex);

        private static ConfiguredCall Configure(Func<object?[]?, object?> factory)
        {
            var call = LastCall.Current
                ?? throw new InvalidOperationException(
                    ".Throws() の直前にモックのメンバー呼び出しがない");
            call.Proxy.Config[call.Key] = factory;
            // 設定のための呼び出しは Received() の回数に数えない(Returns と同じ)
            if (call.Proxy.Calls.Count > 0)
                call.Proxy.Calls.RemoveAt(call.Proxy.Calls.Count - 1);
            LastCall.Current = null;
            return new ConfiguredCall();
        }
    }
}
