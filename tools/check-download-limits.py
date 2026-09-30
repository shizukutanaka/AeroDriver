#!/usr/bin/env python3
"""外部からのダウンロードに必ずサイズ上限があることを機械検証する。

`HttpClient.GetStringAsync` / `GetByteArrayAsync` は**応答全体を無制限に
メモリへ展開する**。配信元が乗っ取られたり応答が肥大したりすると OOM で
プロセスごと落ちる。

ドライバー本体のダウンロードには最初から 4 GiB の上限(Content-Length の申告と
実バイト数の両方を検査)があったが、**BYOVD ブロックリストの取得だけが
`GetStringAsync` で無制限**だった。防御が片方の経路にしかない、という
このリポジトリで繰り返し見つかった構造。
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CORE = os.path.join(ROOT, 'src', 'AeroDriver.Core')
UNBOUNDED = re.compile(r'\.(GetStringAsync|GetByteArrayAsync)\s*\(')
# 上限を持つ読み取りの目印(Content-Length 検査 + 実バイト数の累計検査)
BOUND = re.compile(r'Max\w*Bytes')

errors = []
downloads = 0
for dp, dn, fn in os.walk(CORE):
    dn[:] = [d for d in dn if d not in ('obj', 'bin')]
    for f in sorted(fn):
        if not f.endswith('.cs'):
            continue
        path = os.path.join(dp, f)
        rel = os.path.relpath(path, ROOT)
        text = open(path, encoding='utf-8', errors='replace').read()
        lines = text.split('\n')
        for i, raw in enumerate(lines, 1):
            line = raw.split('//')[0]
            if UNBOUNDED.search(line):
                errors.append(f'{rel}:{i}: {line.strip()[:70]} '
                              '— 応答を無制限にメモリへ展開する。上限付きの読み取りにすること')
        # HTTP を使うファイルには上限定数があること
        if 'HttpClient' in text and ('GetAsync' in text or 'GetStringAsync' in text):
            downloads += 1
            if not BOUND.search(text):
                errors.append(f'{rel}: HTTP ダウンロードを行うが Max*Bytes の上限定数が無い')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  HTTP ダウンロードを行う {downloads} ファイルすべてに明示的なサイズ上限がある')
