#!/usr/bin/env python3
"""README の2つのセキュリティ主張を機械的に強制する。

**主張1: 「BYOVD ブロックリストを全インストール/復元経路で照合する」**

CLAUDE.md には「経路を増やすときは必ずここも通すこと」と書いてあるだけで、
何も強制していなかった。実際に一度破られている: `.cab` の照合はコンテナ自体の
ハッシュに対して行われ、**展開後の中身は一度も照合されないまま** pnputil に
渡されていた(LOLDrivers が公開するのは `.sys` の SHA256 でコンテナのものではない)。

このリポジトリで繰り返し起きた失敗は「規則は書かれているが強制されていない」。
だからここでは、**ドライバーストアへ書き込む/インストーラーを起動するメソッドを
機械的に列挙し、それぞれが照合を通っていること**を要求する。新しい経路を足すと
このチェックが落ちるので、登録と照合を同時に強制できる。

**主張2: 「HTTPS-only downloads」**

`DownloadUrl` を使って HTTP 要求を出すメソッドは、同じメソッド内で
`Uri.UriSchemeHttps` を確認していなければならない。中間者攻撃で
ダウンロード内容を差し替えられるため。
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERVICES = os.path.join(ROOT, 'src', 'AeroDriver.Core', 'Services')

# インストーラー/ドライバーストアへの書き込みを起動する痕跡
INSTALLER_MARKERS = [
    re.compile(r'ArgumentList\.Add\("/add-driver"\)'),
    re.compile(r'ProcessStartInfo\("msiexec\.exe"\)'),
    # exe インストーラーは対象ファイル自身を起動する
    re.compile(r'ProcessStartInfo\(filePath\)'),
]

# BYOVD 照合の呼び出し
BLOCKLIST_CALL = re.compile(
    r'(IsBlockedAsVulnerableAsync|IsAnyExtractedFileBlockedAsync|IsAnyFileBlockedAsVulnerableAsync)\s*\(')

# 各インストール経路が、どのメソッドで照合を通っているか。
# 「そのメソッド自身」または「呼び出し元」を明示的に登録させることで、
# 経路を足したときに必ずここを触ることになる。
GUARDED_PATHS = {
    # メソッド名 -> 照合が行われるメソッド名(自分自身か、必ず通る呼び出し元)
    'InstallFromFileAsync':  'InstallDriverUpdateWithResultAsync/InstallCustomDriverAsync',
    'InstallFromCabAsync':   'InstallFromCabAsync',
    'ReinstallDriverFileAsync': 'RestoreDriverAsync',
}

# 照合を実際に行っているメソッド(上の右辺が指す先)
GUARD_SITES = {
    'InstallDriverUpdateWithResultAsync': os.path.join(SERVICES, 'DriverService.cs'),
    'InstallCustomDriverAsync':           os.path.join(SERVICES, 'DriverService.cs'),
    'InstallFromCabAsync':                os.path.join(SERVICES, 'DriverService.cs'),
    'RestoreDriverAsync':                 os.path.join(SERVICES, 'BackupService.cs'),
}

# 列挙専用で、ドライバーストアへ書き込まないファイル
ENUMERATION_ONLY = {'PnpUtilDriverSource.cs'}


def method_spans(path):
    """メソッド名 -> その本文の行範囲。中括弧の深さで区切る素朴な実装。"""
    lines = open(path, encoding='utf-8').read().splitlines()
    decl = re.compile(
        r'^\s*(?:public|private|internal|protected)[\w\s<>?,\[\]\.]*?\s(\w+)\s*\([^;]*$')
    spans = {}
    i = 0
    while i < len(lines):
        m = decl.match(lines[i])
        if not m:
            i += 1
            continue
        name = m.group(1)
        # 本文の開始({)を探す
        j = i
        while j < len(lines) and '{' not in lines[j]:
            j += 1
            if j - i > 6:
                break
        if j >= len(lines) or '{' not in lines[j]:
            i += 1
            continue
        depth = 0
        k = j
        while k < len(lines):
            depth += lines[k].count('{') - lines[k].count('}')
            if depth <= 0:
                break
            k += 1
        spans.setdefault(name, (i, k))
        i = k + 1
    return lines, spans


errors = []

# --- 主張1: 全インストール経路が照合を通っているか ---
for filename in sorted(os.listdir(SERVICES)):
    if not filename.endswith('.cs') or filename in ENUMERATION_ONLY:
        continue
    path = os.path.join(SERVICES, filename)
    lines, spans = method_spans(path)

    for name, (start, end) in spans.items():
        body = '\n'.join(lines[start:end + 1])
        if not any(p.search(body) for p in INSTALLER_MARKERS):
            continue
        if name not in GUARDED_PATHS:
            errors.append(
                f'{filename}: {name} がインストーラー/ドライバーストア書き込みを起動するが、'
                'GUARDED_PATHS に登録されていない '
                '— 新しい経路には BYOVD 照合を通し、ここにも登録すること')

for method, guard in GUARDED_PATHS.items():
    for site in guard.split('/'):
        path = GUARD_SITES.get(site)
        if path is None:
            errors.append(f'GUARDED_PATHS[{method}] が指す {site} が GUARD_SITES に無い')
            continue
        lines, spans = method_spans(path)
        if site not in spans:
            errors.append(f'{os.path.basename(path)}: 照合を行うはずの {site} が存在しない')
            continue
        s, e = spans[site]
        if not BLOCKLIST_CALL.search('\n'.join(lines[s:e + 1])):
            errors.append(
                f'{os.path.basename(path)}: {site} が BYOVD 照合を呼んでいない '
                f'({method} の経路が無防備になる)')

# --- 主張2: HTTPS 強制 ---
driver_service = os.path.join(SERVICES, 'DriverService.cs')
lines, spans = method_spans(driver_service)
for name, (start, end) in spans.items():
    body = '\n'.join(lines[start:end + 1])
    if 'DownloadUrl' not in body or 'GetAsync' not in body:
        continue
    if 'UriSchemeHttps' not in body:
        errors.append(
            f'DriverService.cs: {name} が DownloadUrl でHTTP要求を出すのに '
            'Uri.UriSchemeHttps の確認が無い — 中間者攻撃で内容を差し替えられる')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  インストール経路 {len(GUARDED_PATHS)} 本すべてが BYOVD 照合を通り、'
      'ダウンロードは HTTPS を強制している')
