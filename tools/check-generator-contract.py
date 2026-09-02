#!/usr/bin/env python3
"""CommunityToolkit.Mvvm のソースジェネレーター再現が本体と同期しているか検証する。

`tools/ui-typecheck` と `tools/ui-run` は `Generated.cs` で
`[ObservableProperty]` / `[RelayCommand]` の生成結果を**手で再現**している。
本物のジェネレーターはこの環境では動かせない(NuGet 遮断)ので、
再現が `MainViewModel.cs` と食い違っていないことを機械的に確かめる。

食い違うと何が起きるか:

  * 本体に `[ObservableProperty]` を足して `Generated.cs` に足し忘れると、
    ハーネスは**本物と違う形のクラス**を検証し続ける(XAML だけが参照している
    プロパティは、この環境では誰も参照しないのでコンパイルエラーにもならない)
  * 逆に本体から消して `Generated.cs` に残すと、ハーネスは通るのに
    Windows の実ビルドだけが壊れる

ジェネレーターの命名規約(公式ドキュメント):
  `[ObservableProperty] private T _fooBar;`      -> `public T FooBar`
  `[RelayCommand] private void Foo()`            -> `FooCommand`
  `[RelayCommand] private Task FooAsync()`       -> `FooCommand`  (Async は落ちる)
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VIEWMODEL = os.path.join(ROOT, 'src', 'AeroDriver.UI', 'ViewModels', 'MainViewModel.cs')
GENERATED = [
    os.path.join(ROOT, 'tools', 'ui-typecheck', 'Generated.cs'),
    os.path.join(ROOT, 'tools', 'ui-run', 'Generated.cs'),
]

FIELD = re.compile(r'^\s*(?:private|internal)\s+[\w<>?,\[\]\.]+\s+(\w+)\s*(?:=[^;]*)?;')
METHOD = re.compile(r'^\s*(?:private|internal)\s+(?:async\s+)?([\w<>?,\[\]\.]+)\s+(\w+)\s*\(')
NOTIFY = re.compile(r'NotifyCanExecuteChangedFor\(nameof\((\w+)\)\)')


def property_name(field: str) -> str:
    """`_fooBar` / `m_fooBar` / `fooBar` -> `FooBar`(ジェネレーターの規約)。"""
    name = field
    if name.startswith('m_'):
        name = name[2:]
    elif name.startswith('_'):
        name = name.lstrip('_')
    return name[:1].upper() + name[1:]


def command_name(method: str) -> str:
    """`ScanAsync` -> `ScanCommand`、`Cancel` -> `CancelCommand`。"""
    base = method[:-5] if method.endswith('Async') else method
    return base + 'Command'


def parse_viewmodel(path):
    """属性ブロックとその直後の宣言だけを対応付ける(離れた宣言と誤って結び付けない)。"""
    props, commands, notify_targets = {}, {}, set()
    pending = []
    for lineno, line in enumerate(open(path, encoding='utf-8'), 1):
        stripped = line.strip()
        if stripped.startswith('['):
            pending.append(stripped)
            continue
        if not stripped or stripped.startswith('//'):
            pending.clear()
            continue

        attrs = ' '.join(pending)
        has_observable = 'ObservableProperty' in attrs
        has_relay = 'RelayCommand' in attrs

        if has_observable:
            m = FIELD.match(line)
            if m:
                props[property_name(m.group(1))] = lineno
                notify_targets.update(NOTIFY.findall(attrs))
        elif has_relay:
            m = METHOD.match(line)
            if m:
                commands[command_name(m.group(2))] = (lineno, m.group(1))

        pending.clear()
    return props, commands, notify_targets


def declared_members(path):
    """`Generated.cs` が公開しているプロパティ/コマンド名。"""
    text = open(path, encoding='utf-8').read()
    # `public T Name` / `public T Name =>` / `public T Name {`
    return set(re.findall(r'^\s*public\s+[\w<>?,\[\]\.]+\s+(\w+)\s*(?:=>|\{|$)', text, re.M))


errors = []
props, commands, notify_targets = parse_viewmodel(VIEWMODEL)

if not props or not commands:
    errors.append('MainViewModel.cs から属性付きメンバーを1件も解析できなかった'
                  ' — このチェック自体が壊れている疑い')

# [NotifyCanExecuteChangedFor(nameof(XCommand))] の対象が実在するか
for target in sorted(notify_targets):
    if target not in commands:
        errors.append(f'NotifyCanExecuteChangedFor({target}) の対象コマンドが存在しない')

expected = set(props) | set(commands)
for gen in GENERATED:
    rel = os.path.relpath(gen, ROOT)
    declared = declared_members(gen)

    for name in sorted(expected - declared):
        kind = 'プロパティ' if name in props else 'コマンド'
        errors.append(f'{rel}: {kind} {name} が未再現'
                      ' — MainViewModel に足したら Generated.cs にも足すこと')

    # 再現側にしか無いメンバー(本体から消したのに残っている)
    for name in sorted(declared - expected):
        errors.append(f'{rel}: {name} は MainViewModel に対応する'
                      ' [ObservableProperty]/[RelayCommand] が無い'
                      ' — 本体から消したなら再現側も消すこと')

if errors:
    for e in errors:
        print(f'  {e}')
    sys.exit(1)
print(f'  [ObservableProperty] {len(props)} 件 / [RelayCommand] {len(commands)} 件 が'
      f' Generated.cs {len(GENERATED)} ファイルと一致')
