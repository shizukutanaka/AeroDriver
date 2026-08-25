// xunit / FluentAssertions / NSubstitute の最小スタブ。
// テストを「実行」するためではなく、**テストコードが Core の現在の API と
// 整合しているか**を実コンパイラで検査するためのもの。
// 表明の中身は評価しない(全メソッドが自分自身を返すだけ)。
using System.Linq.Expressions;

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)]
    public class FactAttribute : Attribute
    {
        public string? DisplayName { get; set; }
        public string? Skip { get; set; }
    }

    public sealed class TheoryAttribute : FactAttribute { }

    /// <summary>xunit のテストクラス用の非同期セットアップ/後始末。</summary>
    public interface IAsyncLifetime
    {
        Task InitializeAsync();
        Task DisposeAsync();
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataAttribute : Attribute
    {
        public InlineDataAttribute(params object?[] data) { }
    }
}

namespace FluentAssertions
{
    /// <summary>
    /// 表明の最小面。中身は評価せず自身を返すだけ。
    /// <para>
    /// 述語を取るメソッドの引数を <c>Func&lt;dynamic, bool&gt;</c> にしているのは、
    /// C# のオーバーロード解決では <c>Should&lt;T&gt;(this T)</c> が
    /// <c>Should&lt;TItem&gt;(this IEnumerable&lt;TItem&gt;)</c> より優先され、
    /// 要素型を推論させられないため。結果として**ラムダの中身だけは型検査されない**。
    /// このハーネスの目的は「テストコードが Core の現在の API と整合しているか」の
    /// 検査なので、この割り切りで目的は満たせる(README に明記)。
    /// </para>
    /// </summary>
    public class Assertions<T>
    {
        public Assertions<T> Be(object? expected, string because = "", params object[] args) => this;
        public Assertions<T> NotBe(object? unexpected, string because = "", params object[] args) => this;
        public Assertions<T> BeNull(string because = "", params object[] args) => this;
        public Assertions<T> NotBeNull(string because = "", params object[] args) => this;
        public Assertions<T> BeTrue(string because = "", params object[] args) => this;
        public Assertions<T> BeFalse(string because = "", params object[] args) => this;
        public Assertions<T> BeSameAs(object? expected, string because = "", params object[] args) => this;
        public Assertions<T> BePositive(string because = "", params object[] args) => this;
        public Assertions<T> BeLessThan(object? expected, string because = "", params object[] args) => this;
        public Assertions<T> NotBeNullOrWhiteSpace(string because = "", params object[] args) => this;
        public Assertions<T> BeEmpty(string because = "", params object[] args) => this;
        public Assertions<T> HaveCount(int expected, string because = "", params object[] args) => this;
        public Assertions<T> Contain(string expected, string because = "", params object[] args) => this;
        public Assertions<T> NotContain(string unexpected, string because = "", params object[] args) => this;
        public Assertions<T> Contain(Func<dynamic, bool> predicate, string because = "", params object[] args) => this;
        public Assertions<T> NotContain(Func<dynamic, bool> predicate, string because = "", params object[] args) => this;
        public Assertions<T> OnlyContain(Func<dynamic, bool> predicate, string because = "", params object[] args) => this;
        public Assertions<T> ContainInOrder(params object?[] expected) => this;
        public Assertions<T> BeSubsetOf(System.Collections.IEnumerable superset, string because = "", params object[] args) => this;
    }

    public class ActionAssertions
    {
        public ActionAssertions Throw<TException>(string because = "", params object[] args) where TException : Exception => this;
        public ActionAssertions NotThrow(string because = "", params object[] args) => this;
    }

    public class AsyncAssertions
    {
        public Task ThrowAsync<TException>(string because = "", params object[] args) where TException : Exception => Task.CompletedTask;
        public Task NotThrowAsync(string because = "", params object[] args) => Task.CompletedTask;
    }

    public static class AssertionExtensions
    {
        public static ActionAssertions Should(this Action subject) => new();
        public static AsyncAssertions Should(this Func<Task> subject) => new();
        public static Assertions<T> Should<T>(this T subject) => new();
    }
}

namespace NSubstitute
{
    public static class Substitute
    {
        /// <summary>
        /// 型検査専用。実行時プロキシは生成しないので null! を返す。
        /// このハーネスはテストを実行しない(コンパイルだけ)。
        /// </summary>
        public static T For<T>(params object[] constructorArguments) where T : class => null!;
    }

    public static class SubstituteExtensions
    {
        public static ConfiguredCall Returns<T>(this T value, T returnThis, params T[] returnThese) => new();
        // async メンバーのスタブ化: Task<T> を返すメソッドに T の値をそのまま渡せる
        public static ConfiguredCall Returns<T>(this Task<T> value, T returnThis, params T[] returnThese) => new();
        public static ConfiguredCall Returns<T>(this Task<T> value, Func<CallInfo, T> returnThis) => new();
        public static T Received<T>(this T substitute) where T : class => substitute;
        public static T Received<T>(this T substitute, int requiredNumberOfCalls) where T : class => substitute;
        public static T DidNotReceive<T>(this T substitute) where T : class => substitute;
    }

    public class ConfiguredCall
    {
        public ConfiguredCall AndDoes(Action<CallInfo> callback) => this;
    }

    public class CallInfo
    {
        public T Arg<T>() => default!;
        public object? this[int index] => null;
    }

    public static class Arg
    {
        public static T Any<T>() => default!;
        public static T Is<T>(T value) => default!;
        public static T Is<T>(Expression<Predicate<T>> predicate) => default!;
    }
}

namespace NSubstitute.ExceptionExtensions
{
    public static class ExceptionExtensions
    {
        public static ConfiguredCall Throws<T>(this T value, Exception ex) => new();
        public static ConfiguredCall ThrowsAsync<T>(this T value, Exception ex) => new();
    }
}
