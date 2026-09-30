#!/usr/bin/env python3
"""検証したファイルと実行するファイルの同一性(TOCTOU)を機械検証する。

インストール経路は「BYOVD照合 → 署名検証 → 実行」の順に同じファイルを3回開く。
その間に別プロセスが差し替えられると、**照合を通過した無害なファイルの代わりに
既知の脆弱ドライバーがインストールされる**。防御は FileShare.Read（書き込み共有なし）の
ハンドルを検証開始から実行完了まで保持し続けること。

ダウンロード経路には最初からこの防御があったが、カスタムインストール経路には
無かった（ユーザーが選ぶ任意のパスで、Downloads 等の書き込み可能な場所に
あり得るため、むしろこちらの方が危険だった）。非対称が再発しないよう強制する。
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TARGET = os.path.join(ROOT, 'src', 'AeroDriver.Core', 'Services', 'DriverService.cs')

# 「検証してから実行する」経路。ここでは必ずロックを保持していなければならない。
GUARDED_METHODS = ['InstallDriverUpdateWithResultAsync', 'InstallCustomDriverAsync']
LOCK = re.compile(r'new\s+FileStream\([^)]*FileAccess\.Read\s*,\s*FileShare\.Read')
VERIFY = re.compile(r'IsBlockedAsVulnerableAsync|IsAnyExtractedFileBlockedAsync')

src = open(TARGET, encoding='utf-8', errors='replace').read()
lines = src.split('\n')
errors = []

for name in GUARDED_METHODS:
    # メソッド本体を波括弧の深さで切り出す
    start = next((i for i, l in enumerate(lines) if re.search(r'\b' + name + r'\s*\(', l)
                  and re.search(r'(public|private|internal|protected)', l)), None)
    if start is None:
        errors.append(f'{name}: メソッドが見つからない（改名したらこのチェックも更新すること）')
        continue
    depth, started, end = 0, False, start
    for j in range(start, len(lines)):
        depth += lines[j].count('{') - lines[j].count('}')
        if '{' in lines[j]:
            started = True
        if started and depth <= 0:
            end = j
            break
    body = '\n'.join(lines[start:end + 1])

    verify = VERIFY.search(body)
    if not verify:
        errors.append(f'{name}: BYOVD照合の呼び出しが無い — 検証経路から外れている')
        continue
    lock = LOCK.search(body)
    if not lock:
        errors.append(
            f'{name}: FileShare.Read のロックを保持していない — '
            '照合を通過した後にファイルを差し替えられる（TOCTOU）')
        continue
    # ロックは「最初の」照合より前に取得されていること（後だと意味がない）。
    # 位置はマッチオブジェクトの start() で見る（文字列検索だと同じ字面の
    # 別の箇所を拾って順序判定が壊れる — 実際に一度そうなった）
    if lock.start() > verify.start():
        errors.append(f'{name}: ロックの取得が BYOVD照合より後 — 照合前の差し替えを防げない')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  検証→実行の全経路({len(GUARDED_METHODS)}件)で FileShare.Read ロックを照合前に保持している')
