#!/usr/bin/env python3
"""永続状態ファイルの書き込みがアトミックであることを機械検証する。

`File.WriteAllText/WriteAllLines/WriteAllBytes` は「**切り詰めてから書く**」。
途中でプロセスが落ちると空または前半だけのファイルが残る。実際に3箇所で
それが起きうる状態だった:

  - SettingsService: ユーザー設定が全損し、次回起動で黙って既定値に戻る
  - VulnerableDriverBlocklist: **壊れたキャッシュの mtime は新しい**ため TTL(7日)を
    通ってしまい、BYOVD 照合が空または不完全なまま最大7日間使われる
  - BackupService: そのバックアップ世代が復元不能になる

正しい形は「一時ファイルへ書いてから `File.Move(..., overwrite: true)` で置換」。
落ちても「前の正常な内容」か「ファイル無し」のどちらかになる。

例外: 追記(`AppendAll*`)。追記は切り詰めを伴わず、読み出し側が
途中行を読み飛ばす設計(InstallHistoryService)なので対象外。
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CORE = os.path.join(ROOT, 'src', 'AeroDriver.Core')
WRITE = re.compile(r'File\.WriteAll(?:Text|Lines|Bytes)(?:Async)?\s*\(')
MOVE = re.compile(r'File\.Move\s*\([^;]*overwrite:\s*true')
METHOD = re.compile(r'^\s*(?:public|private|protected|internal|static|async|override|virtual|partial|\s)+'
                    r'[\w<>?,\[\]\.]+\s+(\w+)\s*\(')

def enclosing_method(lines, idx):
    """idx 行を含むメソッドの (名前, 本文) を返す。波括弧の深さで範囲を決める。"""
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
            # コメント行を落としてから返す。コメント中の File.Move(overwrite: true) を
            # 実コードと誤認すると、Move を消しても検出できない(実際にそうなった)
            code = [l.split('//')[0] for l in lines[start:end + 1]
                    if not l.strip().startswith(('//', '///', '*', '/*'))]
            return m.group(1), '\n'.join(code)
    return None, ''

errors = []
checked = 0
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
            if not WRITE.search(line):
                continue
            checked += 1
            name, body = enclosing_method(lines, i)
            # 一時ファイル名が固定だと、複数プロセスが同じ一時ファイルを奪い合い、
            # 書き途中の内容を Move してしまう(アトミック化の意味が消える)
            if MOVE.search(body) and not re.search(r'Guid\.NewGuid\(\)', body):
                errors.append(
                    f'{rel}:{i + 1}: {name or "(不明)"}() の一時ファイル名が固定 — '
                    'Guid.NewGuid() で一意にすること(複数プロセスが衝突する)')
            if not MOVE.search(body):
                errors.append(
                    f'{rel}:{i + 1}: {name or "(不明)"}() が File.WriteAll* を使っているが '
                    'File.Move(..., overwrite: true) による置換が無い — '
                    '書き込み途中で落ちるとファイルが全損/破損する')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  永続書き込み {checked} 箇所すべてが一時ファイル経由の置換になっている')
