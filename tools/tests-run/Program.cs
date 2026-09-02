// xunit テストスイートを xunit 無しで実行する。
//
// [Fact]/[Theory] の付いたメソッドをリフレクションで探し、テストクラスを
// 生成(コンストラクターがセットアップ)し、IAsyncLifetime があれば
// InitializeAsync/DisposeAsync を呼び、各テストを実際に走らせる。
// 表明は TestRuntime.cs の本物の実装で、満たされなければ例外を投げる。
using System.Reflection;
using Xunit;

int pass = 0, fail = 0, skipped = 0;
var failures = new List<string>();

var testClasses = typeof(Program).Assembly.GetTypes()
    .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Tests", StringComparison.Ordinal))
    .Where(t => t.GetMethods().Any(m => m.GetCustomAttribute<FactAttribute>() != null))
    .OrderBy(t => t.FullName, StringComparer.Ordinal)
    .ToList();

foreach (var type in testClasses)
{
    Console.WriteLine($"== {type.Name} ==");

    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m.GetCustomAttribute<FactAttribute>() != null)
        .OrderBy(m => m.Name, StringComparer.Ordinal)
        .ToList();

    foreach (var method in methods)
    {
        var fact = method.GetCustomAttribute<FactAttribute>()!;
        if (!string.IsNullOrEmpty(fact.Skip))
        {
            skipped++;
            Console.WriteLine($"  SKIP  {method.Name}  ({fact.Skip})");
            continue;
        }

        // [Theory] は [InlineData] ごとに1ケース。[Fact] は引数なしの1ケース
        var inline = method.GetCustomAttributes<InlineDataAttribute>().ToList();
        var cases = inline.Count > 0
            ? inline.Select(d => d.Data).ToList()
            : new List<object?[]> { Array.Empty<object?>() };

        foreach (var caseArgs in cases)
        {
            var label = caseArgs.Length == 0
                ? method.Name
                : $"{method.Name}({string.Join(", ", caseArgs.Select(a => a?.ToString() ?? "null"))})";

            object? instance = null;
            try
            {
                // コンストラクターがセットアップを兼ねる(xunit と同じくケースごとに新規生成)
                instance = Activator.CreateInstance(type);

                if (instance is IAsyncLifetime lifetime)
                    await lifetime.InitializeAsync().ConfigureAwait(false);

                var result = method.Invoke(instance, caseArgs.Length == 0 ? null : caseArgs);
                if (result is Task task)
                    await task.ConfigureAwait(false);

                pass++;
                Console.WriteLine($"  PASS  {label}");
            }
            catch (Exception ex)
            {
                // リフレクション経由の例外は TargetInvocationException に包まれる
                var real = ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException
                    : ex;
                fail++;
                Console.WriteLine($"  FAIL  {label}");
                Console.WriteLine($"        {real.GetType().Name}: {real.Message}");
                failures.Add($"{type.Name}.{label}: {real.GetType().Name}: {real.Message}");
            }
            finally
            {
                try
                {
                    if (instance is IAsyncLifetime lifetime)
                        await lifetime.DisposeAsync().ConfigureAwait(false);
                    if (instance is IDisposable disposable)
                        disposable.Dispose();
                }
                catch (Exception ex)
                {
                    // 後始末の失敗はテスト結果を左右しないが、握りつぶさず見えるようにする
                    Console.WriteLine($"        (後始末で {ex.GetType().Name}: {ex.Message})");
                }
            }
        }
    }
}

Console.WriteLine();
if (failures.Count > 0)
{
    Console.WriteLine("失敗一覧:");
    foreach (var f in failures)
        Console.WriteLine($"  {f}");
    Console.WriteLine();
}
Console.WriteLine($"tests-run: {pass} passed, {fail} failed, {skipped} skipped "
                  + $"({testClasses.Count} クラス)");
return fail == 0 ? 0 : 1;
