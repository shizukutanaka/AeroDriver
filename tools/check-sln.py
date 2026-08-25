#!/usr/bin/env python3
"""AeroDriver.sln の静的健全性チェック。

過去に2種類の事故が起きている:
  1. NestedProjects で全プロジェクトが「自分自身の親」として登録されており、
     親チェーンを辿る MSBuild の GetUniqueProjectName() が無限再帰して
     **スタックオーバーフローで即死**していた(dotnet build AeroDriver.sln が
     コンパイル以前に落ちる状態)
  2. GUID に 16進でない文字(G/H)が含まれていた
  3. 「幽霊プロジェクト参照」— sln が実在しない csproj を指す

いずれも Windows 実機に行ってから発覚すると1往復無駄になるため、ここで潰す。
"""
import re, sys, os

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sln = os.path.join(root, 'AeroDriver.sln')
s = open(sln, encoding='utf-8-sig').read()
errors = []

projects = re.findall(
    r'^Project\("\{([0-9A-Fa-f-]+)\}"\)\s*=\s*"([^"]+)",\s*"([^"]+)",\s*"\{([^}]+)\}"',
    s, re.M)
if not projects:
    errors.append('Project エントリが1つも解析できない')

guids = {}
for _type, name, path, guid in projects:
    # 実在チェック(幽霊プロジェクト参照)
    full = os.path.join(root, path.replace('\\', os.sep))
    if not os.path.isfile(full):
        errors.append(f'{name}: csproj が実在しない -> {path}')
    # GUID の妥当性
    if not re.fullmatch(r'[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}', guid):
        errors.append(f'{name}: GUID が16進として不正 -> {{{guid}}}')
    if guid in guids:
        errors.append(f'{name}: GUID が {guids[guid]} と重複 -> {{{guid}}}')
    guids[guid] = name

# 自己参照ネスト(無限再帰の原因)
nested = re.search(r'GlobalSection\(NestedProjects\)(.*?)EndGlobalSection', s, re.S)
if nested:
    for child, parent in re.findall(r'\{([^}]+)\}\s*=\s*\{([^}]+)\}', nested.group(1)):
        if child.lower() == parent.lower():
            errors.append(f'NestedProjects: {{{child}}} が自分自身を親にしている'
                          ' (MSBuild が無限再帰する)')
        elif parent not in guids:
            errors.append(f'NestedProjects: 親 {{{parent}}} に対応する Project エントリが無い')

# ディスク上の csproj が sln に載っているか(載せ忘れは Windows でだけ発覚する)
on_disk = set()
for base in ('src', 'tests'):
    d = os.path.join(root, base)
    if not os.path.isdir(d): continue
    for dirpath, _dirs, files in os.walk(d):
        for f in files:
            if f.endswith('.csproj'):
                on_disk.add(os.path.relpath(os.path.join(dirpath, f), root))
in_sln = {p.replace('\\', os.sep) for _t, _n, p, _g in projects}
for missing in sorted(on_disk - in_sln):
    errors.append(f'sln に載っていない csproj: {missing}')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  {len(projects)} プロジェクト。GUID・実在・ネストとも健全')
