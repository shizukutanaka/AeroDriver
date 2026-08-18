// Microsoft.Management.Infrastructure (WMI) の最小スタブ。
// このパッケージは NuGet がプロキシ遮断で restore できず、Windows 専用でもあるため、
// DriverService / WdacHelper はこの環境では本来コンパイルできない。
// 実際の WMI 動作は検証できないが、両ファイルの型整合はこれで検査できる。
using System.Collections;

namespace Microsoft.Management.Infrastructure
{
    public class CimProperty
    {
        public string Name { get; set; } = "";
        public object? Value { get; set; }
        public static CimProperty Create(string name, object? value, CimFlags flags) => new();
    }

    [Flags] public enum CimFlags { None = 0, Property = 1 }

    public class CimKeyedCollection<T> : IEnumerable<T>
    {
        public T? this[string name] => default;
        public IEnumerator<T> GetEnumerator() => Enumerable.Empty<T>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class CimInstance : IDisposable
    {
        public CimInstance(string className) { }
        public CimKeyedCollection<CimProperty> CimInstanceProperties { get; } = new();
        public void Dispose() { }
    }

    public class CimMethodResult { public CimProperty? ReturnValue { get; set; } }

    public class CimMethodParameter
    {
        public static CimMethodParameter Create(string name, object? value, CimFlags flags) => null!;
    }

    public class CimMethodParametersCollection : ICollection<CimMethodParameter>
    {
        public int Count => 0; public bool IsReadOnly => false;
        public void Add(CimMethodParameter item) { }
        public void Clear() { } public bool Contains(CimMethodParameter i) => false;
        public void CopyTo(CimMethodParameter[] a, int i) { } public bool Remove(CimMethodParameter i) => false;
        public IEnumerator<CimMethodParameter> GetEnumerator() => Enumerable.Empty<CimMethodParameter>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class CimSession : IDisposable
    {
        public static CimSession Create(string? computerName) => new();
        public IEnumerable<CimInstance> QueryInstances(string ns, string dialect, string query)
            => Enumerable.Empty<CimInstance>();
        public CimMethodResult InvokeMethod(string ns, CimInstance instance, string methodName,
            CimMethodParametersCollection? parameters) => new();
        public void Dispose() { }
    }
}
namespace Microsoft.Management.Infrastructure.Options { }

// Microsoft.Extensions.Http.Resilience(NuGet)の拡張メソッド。
// 実際のレジリエンス動作は検証できないが、登録コードの型整合は確認できる。
namespace Microsoft.Extensions.DependencyInjection
{
    public static class ResilienceHttpClientBuilderExtensions
    {
        public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder b) => b;
    }
}
