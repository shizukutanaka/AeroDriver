#!/usr/bin/env python3
"""CLAUDE.md 規則3「OperationCanceledException は再スロー」を機械検証する。

キャンセルを catch(Exception) で握りつぶすと、**キャンセルが成功に化ける**。
実際に `PnpUtilDriverSource.RunPnpUtilAsync` がそうなっていた: ct を受けて
`ReadToEndAsync(ct)` を await するのに、OCE を握って `string.Empty` を返し、
呼び出し側の `ParseEnumOutput` がそれを「ドライバー0件」という**正常な結果**として
解釈していた。ユーザーがキャンセルすると一覧が空になって成功表示される。

検査対象は「キャンセルが実際に発生しうる catch」に絞る:
同じメソッド内で ct を渡した await があるものだけを見る(ct を受けない
純粋なヘルパーの catch は対象外)。
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CATCH_ALL = re.compile(r'catch\s*\(\s*(?:System\.)?Exception\b')
# ct を伴う await(cancellationToken / ct / token などの実引数)
AWAIT_CT = re.compile(r'await\s+[^;]*\b(cancellationToken|ct|token)\b')
METHOD = re.compile(r'^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal|static|async|sealed|override|virtual|partial|\s)+[\w<>?,\[\]\.]+\s+(\w+)\s*\(')

def methods(lines):
    """(開始行index, 終了行index, 名前, 本文) を返す。波括弧の深さで区切る。"""
    out = []
    i = 0
    while i < len(lines):
        m = METHOD.match(lines[i])
        if m and '(' in lines[i]:
            depth = 0
            started = False
            j = i
            while j < len(lines):
                depth += lines[j].count('{') - lines[j].count('}')
                if '{' in lines[j]:
                    started = True
                if started and depth <= 0:
                    break
                j += 1
            out.append((i, j, m.group(1), '\n'.join(lines[i:j + 1])))
            i = j + 1
        else:
            i += 1
    return out

errors = []
for dp, dn, fn in os.walk(os.path.join(ROOT, 'src')):
    dn[:] = [d for d in dn if d not in ('obj', 'bin')]
    for f in sorted(fn):
        if not f.endswith('.cs'):
            continue
        path = os.path.join(dp, f)
        rel = os.path.relpath(path, ROOT)
        lines = open(path, encoding='utf-8', errors='replace').read().split('\n')
        for start, end, name, body in methods(lines):
            if not AWAIT_CT.search(body):
                continue  # キャンセルが発生しえない
            for k in range(start, min(end + 1, len(lines))):
                line = lines[k]
                if not CATCH_ALL.search(line):
                    continue
                # 同じ catch 行の when 句で除外していれば OK
                if 'OperationCanceledException' in line:
                    continue
                # 直前に OCE の catch があれば OK(12行前まで見る)
                before = '\n'.join(lines[max(start, k - 12):k])
                if re.search(r'catch\s*\(\s*(?:System\.)?OperationCanceledException', before):
                    continue
                errors.append(
                    f'{rel}:{k + 1}: {name}() は ct 付き await を含むが、'
                    'catch(Exception) の前に OperationCanceledException の再スローが無い '
                    '— キャンセルが成功に化ける(CLAUDE.md 規則3)')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print('  ct 付き await を含む全メソッドで OperationCanceledException が再スローされている')
