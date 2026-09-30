#!/usr/bin/env python3
"""CLAUDE.md 規則4「ライブラリ層(Core)の await は ConfigureAwait(false)」を機械検証する。

Core は WPF の UI スレッドから呼ばれる。ConfigureAwait(false) を付けないと
各 await が SynchronizationContext を捕捉して UI スレッドへ戻るため、
(1) 不要なマーシャリングで遅くなる (2) 呼び出し側が一度でも .Result/.Wait() で
ブロックすると**デッドロックする**(WPF の古典的な事故)。

規則は CLAUDE.md に明記されていたが強制されておらず、実際に多数すり抜けていた。

対象外にするもの:
  - コメント/文字列リテラル中の "await"
  - `await foreach` の宣言行(`ConfigureAwait` は `WithCancellation` と共に
    別行に書かれることがあるため、直後の数行も見る)
  - `Task.CompletedTask` / `Task.Yield()`(コンテキストを捕捉しない、または
    捕捉しても害がないもの)
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CORE = os.path.join(ROOT, 'src', 'AeroDriver.Core')
AWAIT = re.compile(r'(^|[^\w.])await\s')
EXEMPT = re.compile(r'Task\.Yield\(\)|Task\.CompletedTask')

def strip_comment(line):
    # 行コメントを落とす(文字列中の // は考慮しない — Core に該当例は無い)
    i = line.find('//')
    return line[:i] if i >= 0 else line

violations = []
for dp, dn, fn in os.walk(CORE):
    dn[:] = [d for d in dn if d not in ('obj', 'bin')]
    for f in sorted(fn):
        if not f.endswith('.cs'):
            continue
        path = os.path.join(dp, f)
        rel = os.path.relpath(path, ROOT)
        lines = open(path, encoding='utf-8', errors='replace').read().split('\n')
        in_block_comment = False
        for i, raw in enumerate(lines):
            # ブロックコメントを跨ぐ
            if in_block_comment:
                if '*/' in raw:
                    in_block_comment = False
                continue
            if '/*' in raw and '*/' not in raw:
                in_block_comment = True
                continue
            line = strip_comment(raw)
            if not AWAIT.search(line) or EXEMPT.search(line):
                continue
            # 文の終端を括弧の深さで決める。ラムダを含む await は本体に ';' が
            # 何度も現れるため、「最初の ';' まで」では途中で切れてしまう
            # (実際にそれで await Task.Run(...) を誤検出した)
            stmt = line
            depth = line.count('(') - line.count(')') + line.count('{') - line.count('}')
            j = i
            while (depth > 0 or ';' not in stmt) and j + 1 < len(lines) and j - i < 200:
                j += 1
                nxt = strip_comment(lines[j])
                stmt += ' ' + nxt
                depth += nxt.count('(') - nxt.count(')') + nxt.count('{') - nxt.count('}')
            if 'ConfigureAwait' in stmt:
                continue
            violations.append(f'{rel}:{i + 1}: {line.strip()[:80]}')

if violations:
    print(f'  ConfigureAwait(false) の無い await: {len(violations)} 件')
    for v in violations[:20]:
        print(f'    {v}')
    if len(violations) > 20:
        print(f'    ... 他 {len(violations) - 20} 件')
    sys.exit(1)
print('  Core の全 await が ConfigureAwait を伴っている')
