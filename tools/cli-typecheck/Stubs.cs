using System.Globalization;
// System.CommandLine (2.0.0-beta4) の最小スタブ。
// NuGet がプロキシ遮断で restore できないため、CLI ハンドラーの手書きロジックを
// 型検査する目的で必要な API 面だけを再現する。
namespace System.CommandLine
{
    public class Symbol { public string? Description { get; set; } }

    public class Option<T> : Symbol
    {
        public Option(string name, string? description = null) { }
        public Option(string name, Func<T> getDefaultValue, string? description = null) { }
    }

    public class Command : Symbol, System.Collections.IEnumerable
    {
        public Command(string name, string? description = null) { }
        public void Add(Symbol s) { }
        public System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
        public void AddCommand(Command c) { }

        public void SetHandler(Func<Task> handler) { }
        public void SetHandler<T>(Func<T, Task> handler, Option<T> o1) { }
        public void SetHandler<T1, T2>(Func<T1, T2, Task> handler, Option<T1> o1, Option<T2> o2) { }
    }

    public class RootCommand : Command
    {
        public RootCommand(string? description = null) : base("root", description) { }
        public Task<int> InvokeAsync(string[] args) => Task.FromResult(0);
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
}
namespace AeroDriver.Core
{
    public static class ServiceCollectionExtensions
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection ConfigureServices(
            this Microsoft.Extensions.DependencyInjection.IServiceCollection services) => services;
    }
}
namespace AeroDriver.Languages.Services
{
    public sealed class LanguageService : ILanguageService
    {
        public string GetString(string n) => n;
        public string GetString(string n, System.Globalization.CultureInfo c) => n;
        public string GetString(string n, params object[] a) => n;
        public System.Globalization.CultureInfo CurrentCulture => System.Globalization.CultureInfo.InvariantCulture;
        public void SetCulture(System.Globalization.CultureInfo c) { }
        public IReadOnlyList<System.Globalization.CultureInfo> SupportedCultures { get; } = new List<System.Globalization.CultureInfo>();
    }
}
