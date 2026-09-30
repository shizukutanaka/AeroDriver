#!/usr/bin/env python3
"""生きた文書(README / CLAUDE.md / CONTRIBUTING.md)を実態から乖離させない。

2種類の乖離を機械的に止める。

**(a) 件数の直書き**
「130 assertions」「60キー」のような数値は、ハーネスやリソースが増えるたびに
手で追随する必要があり、実際に4回以上ずれた(README 130→152、CLAUDE.md 60→67 など)。
規則6「宣言と実装を一致させる」の違反が構造的に再発する形なので、追随し続けるより
**追随が要らない形**にする: 生きた文書からは数値を追放し、件数は
`tools/verify-all.sh` の実行結果だけが語る。

**(b) 貢献者向け文書が実ワークフローに触れていない**
`CONTRIBUTING.md` は人間の貢献者が最初に読む文書なのに、`tools/verify-all.sh` も
`tools/verify-windows.ps1` も `CLAUDE.md` の絶対規則も**一度も出てこない**状態だった。
書かれていたのは `dotnet restore && build && test` だけで、しかも
「Core はクロスプラットフォームで単体テストできる」という事実に反する記述があった
(テストは NuGet を要し Windows でしか走らない)。必須参照の存在を強制して再発を防ぐ。

docs/*.md は日付付きの記録なので対象外(バックログの「その時点で N 件」は事実)。
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LIVE_DOCS = ['README.md', 'CLAUDE.md', 'CONTRIBUTING.md']
PATTERNS = [
    (re.compile(r'\b\d+\s*(assertions?|アサーション)'), '件数(assertions/アサーション)'),
    (re.compile(r'\b\d+\s*キー(?!ワード)'), 'キー数'),
]
# 貢献者が最初に読む文書が実ワークフローから外れないよう、言及を必須にする
REQUIRED_MENTIONS = {
    'CONTRIBUTING.md': [
        ('tools/verify-all.sh', 'PR 前に回す検証スクリプト'),
        ('tools/verify-windows.ps1', 'Windows 実機での受け入れ試験'),
        ('CLAUDE.md', '絶対規則の所在'),
    ],
}

errors = []
for doc in LIVE_DOCS:
    path = os.path.join(ROOT, doc)
    text = open(path, encoding='utf-8').read()

    for lineno, line in enumerate(text.splitlines(), 1):
        for pat, label in PATTERNS:
            m = pat.search(line)
            if m:
                errors.append(f'{doc}:{lineno}: {label}の直書き "{m.group(0)}" '
                              '— 生きた文書に件数を書かない(verify-all.sh の出力が正)')

    for needle, why in REQUIRED_MENTIONS.get(doc, []):
        if needle not in text:
            errors.append(f'{doc}: "{needle}" への言及が無い({why})'
                          ' — 貢献者向け文書を実ワークフローから乖離させない')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print('  ' + ' / '.join(LIVE_DOCS) + ': 件数の直書きなし、必須参照あり')
