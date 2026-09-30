#!/usr/bin/env python3
"""WQL とパス組み立てのインジェクション対策を機械検証する。

CLAUDE.md 規則8「外部入力(WMI文字列・ダウンロードURL・ユーザー指定パス)は信用しない:
WQL は WqlSanitizer、パスは Path.GetFullPath 正規化+ルート配下検証 or Path.GetFileName」。

この2つは**一度手で確認して「問題なし」と記録しただけ**で、機械検証が無かった。
このリポジトリで繰り返し起きたのは「規則は書かれているが強制されていない」ことによる
すり抜けなので、手で確認した不変条件こそ固定しておく。

検査:
  1. WQL 文字列(SELECT ... WHERE)に補間があるなら、その値は sanitize 済みでなければならない
  2. 外部入力から作るパスは Path.GetFullPath 正規化+ルート配下検証、または Path.GetFileName を通す
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CORE = os.path.join(ROOT, 'src', 'AeroDriver.Core')

# WQL クエリ文字列(補間の有無を見る)
WQL = re.compile(r'"\s*SELECT\s', re.I)
INTERP = re.compile(r'\{(\w+)')
# sanitize 済みとみなす変数名(WqlSanitizer の戻り値に付ける慣習)
SAFE_NAMES = re.compile(r'^safe', re.I)

METHOD = re.compile(r'^\s*(?:public|private|protected|internal|static|async|override|virtual|partial|\s)+'
                    r'[\w<>?,\[\]\.]+\s+(\w+)\s*\(')

def enclosing(lines, idx):
    for start in range(idx, -1, -1):
        m = METHOD.match(lines[start])
        if not m:
            continue
        depth, started, end = 0, False, start
        for j in range(start, len(lines)):
            depth += lines[j].count('{') - lines[j].count('}')
            if '{' in lines[j]:
                started = True
            if started and depth <= 0:
                end = j
                break
        if start <= idx <= end:
            code = [l.split('//')[0] for l in lines[start:end + 1]
                    if not l.strip().startswith(('//', '///', '*', '/*'))]
            return m.group(1), '\n'.join(code)
    return None, ''


errors = []
wql_sites = 0
for dp, dn, fn in os.walk(CORE):
    dn[:] = [d for d in dn if d not in ('obj', 'bin')]
    for f in sorted(fn):
        if not f.endswith('.cs'):
            continue
        path = os.path.join(dp, f)
        rel = os.path.relpath(path, ROOT)
        lines = open(path, encoding='utf-8', errors='replace').read().split('\n')
        for i, raw in enumerate(lines, 1):
            line = raw.split('//')[0]
            if not WQL.search(line):
                continue
            wql_sites += 1
            # 補間が無い定数クエリは安全
            if '$"' not in line and '$@"' not in line:
                continue
            name, body = enclosing(lines, i - 1)
            for var in INTERP.findall(line):
                if not SAFE_NAMES.match(var):
                    errors.append(
                        f'{rel}:{i}: WQL に非サニタイズ値 {{{var}}} を埋め込んでいる — '
                        'WqlSanitizer.SanitizeDeviceId を通し safe* の名前で受けること')
                    continue
                # 名前が safe* でも、実際に WqlSanitizer を通っていなければ意味がない。
                # 変数名だけを見る検査は「safeId = deviceId」で素通りする(実際にそうなった)。
                # 同じメソッド内、または引数として受け取っている場合は呼び出し元が保証する
                takes_safe_param = re.search(r'\(\s*[^)]*\b' + re.escape(var) + r'\b[^)]*\)',
                                             (body.split('\n')[0] if body else ''))
                if 'WqlSanitizer' not in body and not takes_safe_param:
                    errors.append(
                        f'{rel}:{i}: {name or "(不明)"}() は {{{var}}} を WQL に埋め込むが '
                        'WqlSanitizer を通していない(名前だけ safe* でも中身は保証されない)')

# パス組み立て: Path.Combine に外部由来の値を渡す箇所で、正規化/検証が同じメソッド内にあること
PATH_COMBINE = re.compile(r'Path\.Combine\s*\(')
GUARD = re.compile(r'Path\.GetFullPath|Path\.GetFileName|SanitizeDeviceId|StartsWith\(')
METHOD = re.compile(r'^\s*(?:public|private|protected|internal|static|async|override|virtual|partial|\s)+'
                    r'[\w<>?,\[\]\.]+\s+(\w+)\s*\(')
# 外部入力を示す引数名
EXTERNAL = re.compile(r'\b(deviceId|driverPath|backupVersion|filePath|infPath|infName)\b')


path_sites = 0
for dp, dn, fn in os.walk(CORE):
    dn[:] = [d for d in dn if d not in ('obj', 'bin')]
    for f in sorted(fn):
        if not f.endswith('.cs'):
            continue
        path = os.path.join(dp, f)
        rel = os.path.relpath(path, ROOT)
        lines = open(path, encoding='utf-8', errors='replace').read().split('\n')
        for i, raw in enumerate(lines):
            line = raw.split('//')[0]
            if not PATH_COMBINE.search(line) or not EXTERNAL.search(line):
                continue
            path_sites += 1
            name, body = enclosing(lines, i)
            if not GUARD.search(body):
                errors.append(
                    f'{rel}:{i + 1}: {name or "(不明)"}() が外部入力からパスを組み立てているが '
                    '正規化/検証(Path.GetFullPath+ルート配下検証 or Path.GetFileName)が無い')

# --- 破壊的操作(削除)のパス由来を検査する ---
# 未検証のパスに対する Directory.Delete(recursive) は取り返しがつかない。
# 現状の4箇所はすべて安全(検証済みディレクトリ配下 / 列挙結果 / Guid 由来の一時パス)
# だが、それを保証するものが何も無かった。表の空欄はここにもあった。
DELETE = re.compile(r'(Directory|File)\.Delete\s*\(')
# 安全な出所: パス検証を通っている / 列挙結果を消している / 自分で作った一時パス
SAFE_ORIGIN = re.compile(
    r'Path\.GetFullPath|Path\.GetFileName|SanitizeDeviceId|StartsWith\(|'
    r'Directory\.GetDirectories|Directory\.GetFiles|Directory\.EnumerateFiles|'
    r'Path\.GetTempPath|Guid\.NewGuid')

delete_sites = 0
for dp, dn, fn in os.walk(CORE):
    dn[:] = [d for d in dn if d not in ('obj', 'bin')]
    for f in sorted(fn):
        if not f.endswith('.cs'):
            continue
        path = os.path.join(dp, f)
        rel = os.path.relpath(path, ROOT)
        lines = open(path, encoding='utf-8', errors='replace').read().split('\n')
        for i, raw in enumerate(lines):
            line = raw.split('//')[0]
            if not DELETE.search(line):
                continue
            delete_sites += 1
            name, body = enclosing(lines, i)
            if not SAFE_ORIGIN.search(body):
                errors.append(
                    f'{rel}:{i + 1}: {name or "(不明)"}() が削除するパスの出所を保証していない — '
                    'パス検証済み / 列挙結果 / 自前の一時パス のいずれかであること')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  WQL {wql_sites} 箇所・パス組み立て {path_sites} 箇所・削除 {delete_sites} 箇所とも'
      'サニタイズ/正規化/出所保証を通っている')
