#!/usr/bin/env python3
"""CLAUDE.md 規則5「ProcessStartInfo.ArgumentList を使う。文字列結合で引数を組み立てない」。

`Arguments` プロパティに文字列を組み立てて渡すと、パスに空白や引用符が含まれる場合に
引数が割れる/注入される。`ArgumentList` はトークンを個別に渡すのでエスケープ事故が起きない。

外部プロセス(pnputil.exe / expand.exe / msiexec.exe / インストーラー本体)を
ユーザー指定のパスで起動する製品なので、ここは security-critical。
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BAD_ARGUMENTS = re.compile(r'\bArguments\s*=')          # psi.Arguments = "..." / { Arguments = ... }
BAD_CTOR = re.compile(r'new\s+ProcessStartInfo\s*\([^)]*,')  # 第2引数に引数文字列を渡す形

violations = []
for base in ('src',):
    for dp, dn, fn in os.walk(os.path.join(ROOT, base)):
        dn[:] = [d for d in dn if d not in ('obj', 'bin')]
        for f in sorted(fn):
            if not f.endswith('.cs'):
                continue
            path = os.path.join(dp, f)
            rel = os.path.relpath(path, ROOT)
            for i, raw in enumerate(open(path, encoding='utf-8', errors='replace'), 1):
                line = raw.split('//')[0]
                if BAD_ARGUMENTS.search(line):
                    violations.append(f'{rel}:{i}: ProcessStartInfo.Arguments への代入 '
                                      '— ArgumentList.Add() を使うこと')
                if BAD_CTOR.search(line):
                    violations.append(f'{rel}:{i}: ProcessStartInfo(fileName, arguments) '
                                      '— 引数は ArgumentList.Add() で個別に渡すこと')

if violations:
    for v in violations:
        print(f'  {v}')
    sys.exit(1)

# 逆方向: 実際に ArgumentList が使われていること(プロセス起動が消えていないかの健全性確認)
count = 0
for dp, dn, fn in os.walk(os.path.join(ROOT, 'src')):
    dn[:] = [d for d in dn if d not in ('obj', 'bin')]
    for f in fn:
        if f.endswith('.cs'):
            count += open(os.path.join(dp, f), encoding='utf-8', errors='replace').read().count('ArgumentList.Add')
print(f'  ProcessStartInfo.Arguments への文字列組み立てはゼロ（ArgumentList.Add {count} 箇所）')
