#!/usr/bin/env bash
# この環境で可能な検証をすべて実行する。
# Windows 実機の `dotnet build AeroDriver.sln && dotnet test` の代替ではなく、
# そこで出るはずのエラーを前倒しで潰すためのもの(各ツールの README に限界を明記)。
set -uo pipefail
cd "$(dirname "$0")"
fail=0

run() {
    printf '\n=== %s ===\n' "$1"
    if (cd "$2" && shift 2 && "$@" 2>&1 | tail -n "${TAIL:-6}"); then :; else fail=1; fi
}

# 1. 純粋ロジックの実コンパイル+実行(アサーション)
run "offline-verify: Core の実コンパイル+実行" offline-verify dotnet run -v q --nologo

# 2. ViewModel の実コンパイル+実行(コマンドを実ハンドラーに配線して振る舞いを検証)
run "ui-run: MainViewModel の実行検証" ui-run dotnet run -v q --nologo

# 3/4/5. スタブに対する型検査(出力は成否のみで十分)
for t in core-typecheck ui-typecheck cli-typecheck; do
    printf '\n=== %s: 型検査 ===\n' "$t"
    if out=$(cd "$t" && dotnet build -v q --nologo 2>&1); then
        echo "  Build succeeded"
    else
        echo "$out" | grep -E "error" | sed 's|.*/src/|src/|' | sed 's| \[/.*||' | sort -u | head -10
        fail=1
    fi
done

# 6. XAML の束縛名 ⇔ ViewModel メンバーの一致
printf '\n=== XAML 束縛 ⇔ ViewModel ===\n'
vm=../src/AeroDriver.UI/ViewModels/MainViewModel.cs
bind=0
# {Binding XxxCommand} ⇔ [RelayCommand] メソッド (ScanAsync -> ScanCommand, Cancel -> CancelCommand)
for c in $(grep -ohP '\{Binding \K[A-Za-z]+(?=Command\})' ../src/AeroDriver.UI/*.xaml | sort -u); do
    grep -qE "private (async Task|void) ${c}(Async)?\(" "$vm" || { echo "  ${c}Command: 対応する [RelayCommand] メソッドが無い"; bind=1; fail=1; }
done
# {Binding Xxx} ⇔ ViewModel のプロパティ / [ObservableProperty] フィールド (_xxx)、
# または ItemsSource の要素型(DriverInfo / DriverDetailInfo / CertificateInfo)のプロパティ
models=../src/AeroDriver.Core/Models/DriverInfo.cs
for b in $(grep -ohP '\{Binding \K[A-Za-z]+(?=[},])' ../src/AeroDriver.UI/*.xaml | grep -v 'Command$' | sort -u); do
    low=$(echo "${b:0:1}" | tr 'A-Z' 'a-z')${b:1}
    grep -qE "(public [^ ]+ $b|private [^ ]+ _$low;|private [^ ]+ _$low =)" "$vm" && continue
    grep -qE "public [^ ]+ $b" "$models" && continue
    echo "  $b: ViewModel にも Models にも対応メンバーが無い"; bind=1; fail=1
done
# BindingProxy 経由 {Binding Data.Xxx, Source={StaticResource Proxy}} ⇔ ViewModel のプロパティ
for b in $(grep -ohP '\{Binding Data\.\K[A-Za-z]+' ../src/AeroDriver.UI/*.xaml | sort -u); do
    grep -qE "public [^ ]+ $b" "$vm" || { echo "  Data.$b: ViewModel に対応プロパティが無い"; bind=1; fail=1; }
done
# Proxy を使うなら Window.Resources に BindingProxy が居ること
if grep -q 'StaticResource Proxy' ../src/AeroDriver.UI/MainWindow.xaml \
   && ! grep -q 'BindingProxy x:Key="Proxy"' ../src/AeroDriver.UI/MainWindow.xaml; then
    echo "  StaticResource Proxy を参照しているが BindingProxy が定義されていない"; bind=1; fail=1
fi
[ $bind -eq 0 ] && echo "  全束縛が ViewModel と一致"

# 6b. XAML にユーザー可視のハードコード文字列を残さない(10言語対応の宣言を守る)
# 属性値の日本語を検出する。XMLコメント内は対象外
printf '\n=== XAML のハードコード文字列 ===\n'
hard=$(grep -nP '="[^"]*[ぁ-んァ-ヶ一-龠][^"]*"' ../src/AeroDriver.UI/*.xaml \
       | grep -vP "^\S+:\s*<!--" || true)
if [ -n "$hard" ]; then
    echo "$hard" | sed 's|^|  |'
    echo "  → ILanguageService 経由のラベルに置き換えること"
    fail=1
else
    echo "  ユーザー可視文字列は全てリソース経由"
fi

# 7. XML 妥当性(不正な props でビルドが即死した実績があるため必ず見る)
printf '\n=== XML 妥当性 ===\n'
bad=0
while IFS= read -r f; do
    python3 -c "import xml.dom.minidom,sys;xml.dom.minidom.parse('$f')" 2>/dev/null || { echo "  BAD $f"; bad=1; fail=1; }
done < <(find .. \( -name "*.props" -o -name "*.csproj" -o -name "*.resx" -o -name "*.config" -o -name "*.xaml" \) ! -path "*/obj/*" ! -path "*/bin/*")
[ $bad -eq 0 ] && echo "  全件妥当"

# 8. リソースキーのパリティ(1言語でも欠けると実行時に "[キー名]" が出る)
printf '\n=== リソースキーのパリティ(10言語) ===\n'
miss=0
for k in $(grep -ohP 'GetString\("\K[^"]+' ../src/AeroDriver.UI/ViewModels/MainViewModel.cs ../src/AeroDriver.CLI/Program.cs | sort -u); do
    n=$(grep -l "name=\"$k\"" ../src/AeroDriver.Languages/Resources/Strings.*.resx 2>/dev/null | wc -l)
    [ "$n" -eq 10 ] || { echo "  $k: $n/10"; miss=1; fail=1; }
done
[ $miss -eq 0 ] && echo "  使用中の全キーが 10/10"

printf '\n%s\n' "$([ $fail -eq 0 ] && echo '=== すべて成功 ===' || echo '=== 失敗あり ===')"
exit $fail
