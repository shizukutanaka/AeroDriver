#!/usr/bin/env python3
"""リソース文字列と、その使われ方の整合を検査する。

パリティ検査(キーの有無)と未使用キー検査はキーの**名前**しか見ておらず、
**値**と**呼び出し方**の不整合は素通りしていた。実際にそこで実害が出た:

  `Status_Error` = "An error occurred: {0}" を 13箇所が引数なしで呼び、後ろに
  `: 詳細` を自前で連結していた。ResourceManager.GetString は書式化しないので
  **`{0}` がリテラルのまま全10言語で画面に出ていた**(GUI のタブ見出しは常時)。

CLAUDE.md の方針は「翻訳側にプレースホルダーを持たせない。書式の組み立ては
呼び出し側に閉じ込める」。ここではそれを機械的に強制する。
"""
import glob, os, re, sys, xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, 'src', 'AeroDriver.Languages', 'Resources')
NEUTRAL = os.path.join(RES, 'Strings.resx')

# 各言語で en-US と同じ綴りになるのが正当な値(固有名詞・同綴語)。
# ここに載っていない一致は「未翻訳の疑い」として数える。
LEGITIMATE_SAME = {'AppName', 'Column_Version', 'Column_Source', 'Detail_Signature',
                   'Detail_Status', 'Button_Backup', 'Error_Title'}
UNTRANSLATED_RATIO = 0.10  # 全体の1割を超えて一致したら未翻訳の混入を疑う

def values(path):
    return {d.get('name'): (d.find('value').text or '')
            for d in ET.parse(path).getroot().findall('data')}

def placeholders(text):
    return sorted(set(re.findall(r'\{(\d+)[^}]*\}', text)))

errors = []
neutral = values(NEUTRAL)

# 1. リソース値にプレースホルダーを持たせない
for path in sorted(glob.glob(os.path.join(RES, 'Strings*.resx'))):
    for key, text in values(path).items():
        if placeholders(text):
            errors.append(f'{os.path.basename(path)}: {key} が値にプレースホルダーを含む'
                          ' — 書式の組み立ては呼び出し側に閉じ込めること')

# 2. 全言語でプレースホルダー集合が中立リソースと一致する(1で禁じたが多層で守る)
for path in sorted(glob.glob(os.path.join(RES, 'Strings.*-*.resx'))):
    v = values(path)
    for key, text in neutral.items():
        if placeholders(v.get(key, '')) != placeholders(text):
            errors.append(f'{os.path.basename(path)}: {key} のプレースホルダーが中立と不一致')

# 3. 未翻訳の混入(en-US と同一の値が多すぎないか)
for path in sorted(glob.glob(os.path.join(RES, 'Strings.*-*.resx'))):
    v = values(path)
    same = [k for k, t in v.items()
            if k not in LEGITIMATE_SAME and t == neutral.get(k) and len(t) > 3]
    if len(same) > len(neutral) * UNTRANSLATED_RATIO:
        errors.append(f'{os.path.basename(path)}: en-US と同一の値が {len(same)} 件 '
                      f'— 未翻訳の混入を疑う: {same[:5]}')

# 4. 呼び出し側: 引数付き GetString はプレースホルダーを要求するが、値には持たせない
#    方針なので、引数付きの呼び出し自体が存在してはならない
src = os.path.join(ROOT, 'src')
for dirpath, dirs, files in os.walk(src):
    dirs[:] = [d for d in dirs if d not in ('obj', 'bin')]
    for f in files:
        if not f.endswith('.cs'):
            continue
        full = os.path.join(dirpath, f)
        rel = os.path.relpath(full, ROOT)
        for i, line in enumerate(open(full, encoding='utf-8', errors='replace'), 1):
            for m in re.finditer(r'GetString\("([^"]+)"\s*,', line):
                errors.append(f'{rel}:{i}: GetString("{m.group(1)}", ...) '
                              '— 引数付き呼び出しは使わない。'
                              '$"{GetString(key)}: {value}" の形で呼び出し側が組み立てること')
            # 存在しないキーの参照(タイポ)も同時に見る
            for m in re.finditer(r'GetString\("([^"]+)"\s*\)', line):
                if m.group(1) not in neutral:
                    errors.append(f'{rel}:{i}: 存在しないリソースキー "{m.group(1)}"')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  {len(neutral)} キー × 10言語。プレースホルダー無し・呼び出し形式・翻訳実体とも健全')
