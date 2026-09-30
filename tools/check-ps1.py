#!/usr/bin/env python3
"""verify-windows.ps1 を pwsh 無しで静的に検査する。

この環境には pwsh が無く、受け入れ試験のスクリプトは**一度も構文検査されていない**。
最初に実行する人がスクリプト側のバグを踏むのを減らすため、実際に使っている構文に
絞って機械検証する。完全なパーサーではないが、「無検証」よりははるかに良い。

検査するもの:
  1. 括弧・波括弧・角括弧の均衡
  2. 引用符の均衡(行単位。エスケープ済みは除外)
  3. Check の戻り値を必ず受けていること($null = か変数代入)
     — 受けないと True/False が出力に混ざり、結果が読めなくなる
  4. -When に渡す変数が、それより前で定義されていること
  5. 使っているコマンドレットが既知のものだけであること(綴り間違いの検出)
  6. param() ブロックが CmdletBinding の直後にあること
  7. Start-Process したプロセスを必ず終了させていること
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PS1 = os.path.join(ROOT, 'tools', 'verify-windows.ps1')

KNOWN_CMDLETS = {
    'Write-Host', 'Write-Verbose', 'Write-Error', 'Split-Path', 'Join-Path',
    'Test-Path', 'Start-Sleep', 'Start-Process', 'Remove-Item', 'Out-String',
    'Out-Null', 'Select-Object', 'Resolve-Path', 'Get-Content', 'ForEach-Object',
    'Where-Object', 'Measure-Object', 'Get-Command', 'New-Item',
    # 自前の関数
    'Check', 'Invoke-Checked',
}

errors = []
text = open(PS1, encoding='utf-8').read()
lines = text.split('\n')

# コメントとヒアドキュメントを除いた本文(構文検査の対象)
body_lines = []
in_help = False
for l in lines:
    if l.strip().startswith('<#'):
        in_help = True
    if in_help:
        if '#>' in l:
            in_help = False
        continue
    body_lines.append(l.split('#')[0] if not l.strip().startswith('#') else '')
body = '\n'.join(body_lines)

# 1. 括弧の均衡
for op, cl, name in [('{', '}', '波括弧'), ('(', ')', '丸括弧'), ('[', ']', '角括弧')]:
    if body.count(op) != body.count(cl):
        errors.append(f'{name}が不均衡: {op}={body.count(op)} {cl}={body.count(cl)}')

# 2. 引用符の均衡(行ごと。PowerShell では文字列は原則1行に収まる)
for i, l in enumerate(body_lines, 1):
    if l.count("'") % 2 or l.count('"') % 2:
        # 逐語 `" や '' のエスケープを除いた上で判定
        stripped = l.replace("''", '').replace('`"', '').replace('""', '')
        if stripped.count("'") % 2 or stripped.count('"') % 2:
            errors.append(f'{i}行目: 引用符が閉じていない: {l.strip()[:60]}')

# 3. Check の戻り値を受けているか
for i, l in enumerate(body_lines, 1):
    if re.match(r'\s*Check\s+"', l):
        errors.append(f'{i}行目: Check の戻り値を受けていない '
                      '($null = か変数代入にすること。出力に True/False が混ざる)')

# 4. -When の変数が定義済みか
assigned = {}
for i, l in enumerate(body_lines, 1):
    m = re.search(r'\$(\w+)\s*=\s*Check', l)
    if m:
        assigned[m.group(1)] = i
    # foreach のループ変数も定義とみなす
    m2 = re.search(r'foreach\s*\(\s*\$(\w+)\s+in\b', l)
    if m2:
        assigned[m2.group(1)] = i
for i, l in enumerate(body_lines, 1):
    for m in re.finditer(r'-When\s+\$(\w+)', l):
        v = m.group(1)
        if v not in assigned:
            errors.append(f'{i}行目: -When ${v} が未定義')
        elif assigned[v] > i:
            errors.append(f'{i}行目: -When ${v} が定義({assigned[v]}行目)より前で使われている')

# 5. 未知のコマンドレット(Verb-Noun 形式の呼び出し)
for i, l in enumerate(body_lines, 1):
    for m in re.finditer(r'(?<![\w\-$.])([A-Z][a-z]+-[A-Z][A-Za-z]+)', l):
        name = m.group(1)
        if name not in KNOWN_CMDLETS:
            errors.append(f'{i}行目: 未知のコマンドレット {name}'
                          '(綴り間違いか、KNOWN_CMDLETS への追加漏れ)')

# 6. param() が CmdletBinding の直後
if '[CmdletBinding()]' in body:
    idx = body.index('[CmdletBinding()]')
    after = body[idx + len('[CmdletBinding()]'):].lstrip()
    if not after.startswith('param('):
        errors.append('[CmdletBinding()] の直後が param( ではない')

# 7. Start-Process したら必ず終了させる
if 'Start-Process' in body and not re.search(r'\.Kill\(\)', body):
    errors.append('Start-Process したプロセスを Kill していない '
                  '(検証後にプロセスが残る)')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
n = len(re.findall(r'Check\s+"', body))
print(f'  verify-windows.ps1 の静的検査 OK({n} 検査 / 構文・戻り値規約・変数定義順)')
