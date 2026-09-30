// resx のコンパイル → サテライトアセンブリ生成 → ResourceManager による解決までを
// 実際に走らせて検証する。offline-verify と同じ Check() 方式。
//
// AeroDriver.Languages は本体プロジェクトとしては NuGet(ProjectReference 先の Core)が
// restore できず build できないため、resx とサービス本体だけを同条件で切り出して検証する。
using System.Globalization;
using AeroDriver.Languages.Services;
using Microsoft.Extensions.Logging.Abstractions;

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); }
    else { fail++; Console.WriteLine($"  FAIL  {name}  {detail}"); }
}

// 検証対象の全キーは中立リソースから読む。
// 中立リソースが無いこと自体が欠陥なので、例外で落とさず明示的に報告する
// (以前は全10言語が culture 付きで中立が存在せず、publish でサテライトが
//  落ちると UI が全滅する状態だった)
const string NeutralResx = "../../src/AeroDriver.Languages/Resources/Strings.resx";
if (!File.Exists(NeutralResx))
{
    Check("中立リソース Strings.resx が存在する", false,
        "サテライトが落ちた環境で全ラベルが \"[キー名]\" になる");
    Console.WriteLine($"\nlang-run: {pass} passed, {fail} failed");
    return 1;
}
Check("中立リソース Strings.resx が存在する", true);

var keys = System.Xml.Linq.XDocument.Load(NeutralResx)
    .Root!.Elements("data").Select(d => d.Attribute("name")!.Value).ToList();

Console.WriteLine($"== リソース解決(全{keys.Count}キー × 10言語) ==");

var svc = new LanguageService(NullLogger<LanguageService>.Instance);
Check("SupportedCultures が10言語", svc.SupportedCultures.Count == 10, svc.SupportedCultures.Count.ToString());

foreach (var culture in svc.SupportedCultures)
{
    // GetString は解決に失敗すると "[キー名]" を返す(例外は出ない)。
    // サテライトアセンブリが生成されていない/落ちていると全滅するので、
    // プレースホルダーが1つも出ないことを確認する
    var missing = keys.Where(k => svc.GetString(k, culture) == $"[{k}]").ToList();
    Check($"{culture.Name}: 全キーが解決できる", missing.Count == 0,
        missing.Count == 0 ? "" : $"{missing.Count}件欠落: {string.Join(", ", missing.Take(3))}");
}

Console.WriteLine("== 翻訳が実際に言語ごとに異なること ==");
{
    // 全言語が同じ文字列を返すなら、サテライトが解決されず中立に落ちている疑い
    var scan = svc.SupportedCultures.Select(c => svc.GetString("Button_Scan", c)).Distinct().Count();
    Check("Button_Scan が言語ごとに異なる", scan >= 8, $"{scan} 種類しかない");

    Check("en-US は英語", svc.GetString("Button_Scan", new CultureInfo("en-US")) == "Scan",
        svc.GetString("Button_Scan", new CultureInfo("en-US")));
    Check("ja-JP は日本語", svc.GetString("Button_Scan", new CultureInfo("ja-JP")).Contains('ス'),
        svc.GetString("Button_Scan", new CultureInfo("ja-JP")));
}

Console.WriteLine("== 中立リソースへのフォールバック ==");
{
    // Strings.resx(中立)を作った目的そのもの。サテライトを持たないカルチャでも
    // "[キー名]" ではなく実際の英語が返ること。
    // 以前は全10言語が culture 付きで中立リソースが無く、この経路が全滅していた。
    foreach (var name in new[] { "es-MX", "en-GB", "pt-PT", "zh-TW" })
    {
        var v = svc.GetString("Button_Scan", new CultureInfo(name));
        Check($"{name}(サテライト無し)が中立リソースに落ちる", v == "Scan", v);
    }

    var inv = svc.GetString("Button_Scan", CultureInfo.InvariantCulture);
    Check("InvariantCulture でも解決できる", inv == "Scan", inv);
}

Console.WriteLine("== 未対応カルチャでの初期化 ==");
{
    // 起動時に OS の UI カルチャが未対応でも en-US にフォールバックする設計
    var original = Thread.CurrentThread.CurrentUICulture;
    try
    {
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("sv-SE");
        var s = new LanguageService(NullLogger<LanguageService>.Instance);
        Check("未対応カルチャ起動で en-US にフォールバック", s.CurrentCulture.Name == "en-US", s.CurrentCulture.Name);
        Check("フォールバック後も文字列が引ける", s.GetString("Button_Scan") == "Scan", s.GetString("Button_Scan"));
    }
    finally { Thread.CurrentThread.CurrentUICulture = original; }

    svc.SetCulture(new CultureInfo("de-DE"));
    Check("SetCulture が反映される", svc.CurrentCulture.Name == "de-DE");
    svc.SetCulture(new CultureInfo("sv-SE"));
    Check("未対応カルチャの SetCulture は en-US に落とす", svc.CurrentCulture.Name == "en-US");
}

Console.WriteLine();
Console.WriteLine($"lang-run: {pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;
