#!/usr/bin/env python3
"""生きた文書(README.md / CLAUDE.md)に具体的な件数を書かせない。

「130 assertions」「60キー」のような数値は、ハーネスやリソースが増えるたびに
手で追随する必要があり、実際に4回以上ずれた(README 130→152、CLAUDE.md 60→67 など)。
規則6「宣言と実装を一致させる」の違反が構造的に再発する形なので、追随し続けるより
**追随が要らない形**にする: 生きた文書からは数値を追放し、件数は
`tools/verify-all.sh` の実行結果だけが語る。

docs/*.md は日付付きの記録なので対象外(バックログの「その時点で N 件」は事実)。
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LIVE_DOCS = ['README.md', 'CLAUDE.md']
PATTERNS = [
    (re.compile(r'\b\d+\s*(assertions?|アサーション)'), '件数(assertions/アサーション)'),
    (re.compile(r'\b\d+\s*キー(?!ワード)'), 'キー数'),
]

errors = []
for doc in LIVE_DOCS:
    path = os.path.join(ROOT, doc)
    for lineno, line in enumerate(open(path, encoding='utf-8'), 1):
        for pat, label in PATTERNS:
            m = pat.search(line)
            if m:
                errors.append(f'{doc}:{lineno}: {label}の直書き "{m.group(0)}" '
                              '— 生きた文書に件数を書かない(verify-all.sh の出力が正)')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print('  README.md / CLAUDE.md に件数の直書きなし')
