#!/usr/bin/env python3
"""CLAUDE.md 絶対規則1「課金要素・テレメトリ禁止」を機械検証する。

規則3/4/5/8/9/10 には check-*.py があったが、規則1には無かった。
規則1は次の2点に分解でき、どちらも閉じた集合に対する照合なので機械化できる:

  1. 外部通信先: src の URL リテラルは www.loldrivers.io(無料の BYOVD リスト)と
     XAML スキーマ名前空間(通信しない)だけ。新しいホストを足すときは
     ここで許可リストを編集する = 「無料でテレメトリ無し」の明示的な確認を強制する
  2. パッケージ: テレメトリ SDK / 有償 UI 部品 / 有償化されたライブラリを拒否する。
     FluentAssertions は 8.x 以降が商用利用有償のため、メジャー 8 以上を拒否する
     (dependabot の ignore は自動更新を止めるだけで、手で上げた場合を止めない)
"""
import os, re, sys, xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'src')
TESTS = os.path.join(ROOT, 'tests')

ALLOWED_HOSTS = {
    'www.loldrivers.io',        # BYOVD ブロックリスト(無料・OSS)
    'schemas.microsoft.com',    # XAML 名前空間 URI。通信しない
}
DENIED_PACKAGE_PREFIXES = (
    'Microsoft.ApplicationInsights', 'Sentry', 'Datadog', 'NewRelic', 'Raygun',
    'Bugsnag', 'Segment', 'Mixpanel', 'Amplitude', 'Syncfusion', 'DevExpress',
    'Telerik', 'Infragistics', 'ComponentOne', 'Aspose',
)
# パッケージ名 -> 拒否するメジャーバージョンの下限
MAJOR_CEILING = {'FluentAssertions': 8}

errors = []

# 1. 外部ホスト
url_re = re.compile(r'https?://([^/"\'\s<>)]+)')
for base in (SRC,):
    for dirpath, dirs, files in os.walk(base):
        dirs[:] = [d for d in dirs if d not in ('obj', 'bin')]
        for f in files:
            if not f.endswith(('.cs', '.csproj', '.xaml', '.props')):
                continue
            full = os.path.join(dirpath, f)
            for lineno, line in enumerate(open(full, encoding='utf-8', errors='replace'), 1):
                for m in url_re.finditer(line):
                    host = m.group(1).lower()
                    if host not in ALLOWED_HOSTS:
                        rel = os.path.relpath(full, ROOT)
                        errors.append(f'{rel}:{lineno}: 許可されていない外部ホスト {host} '
                                      '— 無料でテレメトリ無しと確認したうえで ALLOWED_HOSTS に追加すること')

# 2. パッケージ
for base in (SRC, TESTS):
    for dirpath, dirs, files in os.walk(base):
        dirs[:] = [d for d in dirs if d not in ('obj', 'bin')]
        for f in files:
            if not f.endswith('.csproj'):
                continue
            full = os.path.join(dirpath, f)
            rel = os.path.relpath(full, ROOT)
            for e in ET.parse(full).getroot().iter('PackageReference'):
                name = e.get('Include') or ''
                ver = e.get('Version') or ''
                if name.startswith(DENIED_PACKAGE_PREFIXES):
                    errors.append(f'{rel}: {name} はテレメトリ/有償 SDK — 規則1違反')
                if name in MAJOR_CEILING:
                    m = re.match(r'(\d+)', ver)
                    if m and int(m.group(1)) >= MAJOR_CEILING[name]:
                        errors.append(f'{rel}: {name} {ver} — メジャー {MAJOR_CEILING[name]} 以降は商用利用が有償。'
                                      f' {MAJOR_CEILING[name]-1}.x に留めること')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  外部ホストは許可リスト内のみ({", ".join(sorted(ALLOWED_HOSTS))})。'
      f'拒否対象のパッケージなし')
