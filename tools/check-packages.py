#!/usr/bin/env python3
"""各 csproj の PackageReference と、実際にソースが使う名前空間の対応を検査する。

この環境では NuGet が遮断されていて restore できないため、参照の過不足は
**Windows 実機に行って初めて発覚する**。実際に2種類の事故が起きていた:

  1. `Microsoft.Management.Infrastructure`(CimSession)を使っているのに
     PackageReference が無く、DriverService / WdacHelper が CS0246 で
     コンパイルできない状態だった(レガシー System.Management から移行した際に
     旧パッケージを外して新パッケージを足し忘れていた)
  2. 使っていないパッケージ(Microsoft.Extensions.Localization /
     Microsoft.Xaml.Behaviors.Wpf)が残っていた

名前空間 -> パッケージの対応表は「BCL に無く NuGet が必要なもの」だけを持つ。
ProjectReference 経由で推移的に入るものは対象外(親を辿って許容する)。
"""
import os, re, sys, xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 名前空間の接頭辞 -> それを満たしうる NuGet パッケージ(いずれか1つあればよい)。
# 例: ILogger<T> は Microsoft.Extensions.Logging 名前空間だが、型を提供しているのは
#     Microsoft.Extensions.Logging.Abstractions パッケージ。ロガーを「使うだけ」の
#     ライブラリは Abstractions だけを参照するのが正しい。
NS_TO_PACKAGE = {
    'Microsoft.Management.Infrastructure': {'Microsoft.Management.Infrastructure'},
    'System.CommandLine':                  {'System.CommandLine'},
    'CommunityToolkit.Mvvm':               {'CommunityToolkit.Mvvm'},
    'Microsoft.Extensions.DependencyInjection': {
        'Microsoft.Extensions.DependencyInjection',
        'Microsoft.Extensions.DependencyInjection.Abstractions'},
    'Microsoft.Extensions.Logging': {
        'Microsoft.Extensions.Logging',
        'Microsoft.Extensions.Logging.Abstractions'},
    'Microsoft.Extensions.Localization': {'Microsoft.Extensions.Localization'},
}
# ソースの using では現れないが、実行時/ビルド時に必要なもの(誤検出させない)
ALWAYS_OK = {
    'Microsoft.Extensions.Logging.Abstractions',   # Logging に含意される
    'Microsoft.Extensions.Logging.Console',        # プロバイダー登録は拡張メソッド経由
    'Microsoft.Extensions.Http',                   # AddHttpClient / IHttpClientFactory
                                                   # (using には現れず拡張メソッド経由で使う)
    'Microsoft.Extensions.Http.Resilience',        # AddStandardResilienceHandler
    'Microsoft.NET.Test.Sdk', 'xunit', 'xunit.runner.visualstudio',
    'FluentAssertions', 'NSubstitute',
}

def packages(csproj):
    return {e.get('Include') for e in ET.parse(csproj).getroot().iter('PackageReference')}

def project_refs(csproj):
    out = []
    for e in ET.parse(csproj).getroot().iter('ProjectReference'):
        out.append(os.path.normpath(os.path.join(
            os.path.dirname(csproj), e.get('Include').replace('\\', os.sep))))
    return out

def used_namespaces(csproj):
    d = os.path.dirname(csproj)
    ns = set()
    for dirpath, dirs, files in os.walk(d):
        dirs[:] = [x for x in dirs if x not in ('obj', 'bin')]
        for f in files:
            if not f.endswith('.cs'):
                continue
            for line in open(os.path.join(dirpath, f), encoding='utf-8', errors='replace'):
                m = re.match(r'\s*using\s+(?:static\s+)?([A-Za-z0-9_.]+)\s*;', line)
                if m:
                    ns.add(m.group(1))
    return ns

def available(csproj, seen=None):
    """自分と、ProjectReference を辿った先の PackageReference の合計。"""
    seen = seen if seen is not None else set()
    if csproj in seen:
        return set()
    seen.add(csproj)
    acc = packages(csproj)
    for ref in project_refs(csproj):
        if os.path.isfile(ref):
            acc |= available(ref, seen)
    return acc

errors = []
projects = []
for base in ('src', 'tests'):
    d = os.path.join(ROOT, base)
    for dirpath, _dirs, files in os.walk(d):
        projects += [os.path.join(dirpath, f) for f in files if f.endswith('.csproj')]

for csproj in sorted(projects):
    name = os.path.basename(csproj)
    declared = packages(csproj)
    reachable = available(csproj)
    ns = used_namespaces(csproj)

    # 不足: 使っている名前空間に対応するパッケージが(推移的にも)無い
    for prefix, candidates in NS_TO_PACKAGE.items():
        used = any(n == prefix or n.startswith(prefix + '.') for n in ns)
        if used and not (candidates & reachable):
            errors.append(f'{name}: {prefix} を使っているが '
                          f'{" / ".join(sorted(candidates))} のいずれも参照していない')

    # 過剰: 宣言しているのに使っていない
    for pkg in sorted(declared - ALWAYS_OK):
        prefixes = [p for p, q in NS_TO_PACKAGE.items() if pkg in q]
        if not prefixes:
            continue
        if not any(n == p or n.startswith(p + '.') for n in ns for p in prefixes):
            errors.append(f'{name}: {pkg} を宣言しているが使っていない')

    # 使っていない ProjectReference(実体のない結合)
    # 参照先のルート名前空間の型を1つも使っていなければ、その参照は宣言だけの嘘。
    # 依存グラフを読む人を惑わせるうえ、ビルド順にも無駄な制約を作る。
    for ref in project_refs(csproj):
        if not os.path.isfile(ref):
            continue
        ref_ns = os.path.splitext(os.path.basename(ref))[0]   # 例: AeroDriver.Core
        src = ''
        d = os.path.dirname(csproj)
        for dirpath, dirs, files in os.walk(d):
            dirs[:] = [x for x in dirs if x not in ('obj', 'bin')]
            for f in files:
                if f.endswith('.cs'):
                    src += open(os.path.join(dirpath, f), encoding='utf-8', errors='replace').read()
        if ref_ns not in src:
            errors.append(f'{name}: {ref_ns} への ProjectReference が使われていない')

    # バージョン指定漏れ
    for e in ET.parse(csproj).getroot().iter('PackageReference'):
        if not e.get('Version') and not e.get('VersionOverride'):
            errors.append(f'{name}: {e.get("Include")} に Version が無い')

# --- 配布(publish)でローカライズが死なないための設定 ---
# 10言語対応は publish の設定ひとつで無言のうちに壊れる。
#   * InvariantGlobalization=true -> ICU が落ち CultureInfo が解決できない
#   * SatelliteResourceLanguages を絞る -> サテライトアセンブリが落ち、
#     GetString() が全て "[キー名]" を返す
# 出荷される実行可能プロジェクト(OutputType が Exe/WinExe)にのみ課す。
for csproj in sorted(projects):
    root = ET.parse(csproj).getroot()
    name = os.path.basename(csproj)
    out = [e.text for e in root.iter('OutputType')]
    if not any(o in ('Exe', 'WinExe') for o in out if o):
        continue
    props = {e.tag: (e.text or '').strip() for pg in root.iter('PropertyGroup') for e in pg}
    if props.get('InvariantGlobalization', '').lower() != 'false':
        errors.append(f'{name}: InvariantGlobalization を false と明示すること'
                      ' (true だと ICU が落ちて10言語対応が成果物で死ぬ)')
    if 'SatelliteResourceLanguages' in props:
        errors.append(f'{name}: SatelliteResourceLanguages を指定してはいけない'
                      ' (絞るとサテライトが落ち GetString が "[キー名]" を返す)')

# 中立リソースが存在すること(サテライトが落ちても英語で動く保険)
langdir = os.path.join(ROOT, 'src', 'AeroDriver.Languages', 'Resources')
if os.path.isdir(langdir):
    if not os.path.isfile(os.path.join(langdir, 'Strings.resx')):
        errors.append('AeroDriver.Languages: 中立リソース Strings.resx が無い'
                      ' (全てサテライトになり publish で落ちると UI が全滅する)')
    lang_csproj = os.path.join(ROOT, 'src', 'AeroDriver.Languages', 'AeroDriver.Languages.csproj')
    text = open(lang_csproj, encoding='utf-8').read()
    if '<NeutralLanguage>' not in text:
        errors.append('AeroDriver.Languages: NeutralLanguage が未設定')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  {len(projects)} プロジェクト。パッケージ参照・配布設定・中立リソースとも健全')
