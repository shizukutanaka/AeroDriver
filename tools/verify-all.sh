#!/usr/bin/env bash
# この環境で可能な検証をすべて実行する。
# Windows 実機の `dotnet build AeroDriver.sln && dotnet test` の代替ではなく、
# そこで出るはずのエラーを前倒しで潰すためのもの(各ツールの README に限界を明記)。
set -uo pipefail
cd "$(dirname "$0")"
# 日本語検出の grep -P は locale 未設定だと UTF-8 のマルチバイト境界を誤認し、
# EM DASH(U+2014)等を CJK と誤検出する(locale の無い素のコンテナで実際に発生)。
# 環境に依存させない
export LC_ALL=C.UTF-8
fail=0

run() {
    printf '\n=== %s ===\n' "$1"
    if (cd "$2" && shift 2 && "$@" 2>&1 | tail -n "${TAIL:-6}"); then :; else fail=1; fi
}

# 1. 純粋ロジックの実コンパイル+実行(アサーション)
run "offline-verify: Core の実コンパイル+実行" offline-verify dotnet run -v q --nologo

# 2. ViewModel の実コンパイル+実行(コマンドを実ハンドラーに配線して振る舞いを検証)
run "ui-run: MainViewModel の実行検証" ui-run dotnet run -v q --nologo

# 2c. ローカライズ基盤の実行検証(resx コンパイル → サテライト生成 → 解決とフォールバック)
run "lang-run: ローカライズ基盤の実行検証" lang-run dotnet run -v q --nologo

# 2b. DI コンテナの実行検証(解決不能サービス・captive dependency は実行時にしか出ない)
run "di-run: DI コンテナの実行検証" di-run dotnet run -v q --nologo

# 3-6. スタブに対する型検査(テストコードもここで Core の API と突き合わせる)(出力は成否のみで十分)
for t in core-typecheck ui-typecheck cli-typecheck tests-typecheck; do
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
printf '\n=== CLI のハードコード文字列 ===\n'
# Console 出力に翻訳されない散文を残さない。構造化ダンプ(details/history)の
# フィールド名は WMI プロパティ名に合わせて英語で統一する方針なので日本語だけを見る
cli_hard=$(grep -nP 'Console\.[^;]*[ぁ-んァ-ヶ一-龠]' ../src/AeroDriver.CLI/Program.cs || true)
if [ -n "$cli_hard" ]; then
    echo "$cli_hard" | sed 's|^|  |'
    echo "  → ILanguageService 経由にするか、構造化ダンプなら英語に統一すること"
    fail=1
else
    echo "  Console 出力にハードコード文字列なし"
fi

printf '\n=== UI 層 .cs のハードコード文字列 ===\n'
# ViewModel/サービス/App の C# にユーザー可視の日本語散文を残さない。
# ログ(_logger./Log 系)は開発者向けの慣習として日本語を許容する。コメントも対象外
ui_hard=$(grep -rnP '"[^"]*[ぁ-んァ-ヶ一-龠][^"]*"' ../src/AeroDriver.UI --include='*.cs' \
          | grep -vP '_logger\.|\.Log[A-Za-z]*\(|^\s*//|:\s*//|///' || true)
if [ -n "$ui_hard" ]; then
    echo "$ui_hard" | sed 's|^|  |'
    echo "  → ILanguageService 経由のラベル/メッセージに置き換えること"
    fail=1
else
    echo "  ユーザー可視文字列は全てリソース経由(ログの日本語は対象外)"
fi

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

# 6c. AeroDriver.sln の健全性(Windows 実機でだけ発覚する事故を前倒しで潰す)
printf '\n=== AeroDriver.sln の健全性 ===\n'
python3 check-sln.py || fail=1

# 6d. PackageReference の過不足(NuGet が restore できないため実ビルドでは検出できない)
printf '\n=== PackageReference の過不足 ===\n'
python3 check-packages.py || fail=1

# 6e. verify-windows.ps1 の構文検査(pwsh があるときだけ)
# この環境では Windows 実機検証は走らせられないが、**スクリプト自体の構文**は
# PowerShell の公式パーサーで検査できる。pwsh は公式 GitHub Releases から入る:
#   curl -sSL -o /tmp/pwsh.tar.gz https://github.com/PowerShell/PowerShell/releases/download/v7.4.6/powershell-7.4.6-linux-x64.tar.gz
#   (同リリースの hashes.sha256 で SHA256 を照合すること)
#   mkdir -p /opt/pwsh && tar -xzf /tmp/pwsh.tar.gz -C /opt/pwsh && chmod +x /opt/pwsh/pwsh
printf '\n=== verify-windows.ps1 の静的検査 ===\n'
# pwsh の有無に依らず必ず走る。実際に使っている構文に絞った自前の検査で、
# 括弧・引用符の均衡、Check の戻り値規約、-When 変数の定義順、
# コマンドレット名の綴り、Start-Process の後始末を見る
python3 check-ps1.py || fail=1

printf '\n=== verify-windows.ps1 の構文(pwsh がある場合) ===\n'
PWSH=$(command -v pwsh || echo /opt/pwsh/pwsh)
if [ -x "$PWSH" ]; then
    if out=$("$PWSH" -NoProfile -Command '
        $e=$null; $t=$null
        [System.Management.Automation.Language.Parser]::ParseFile(
            (Resolve-Path verify-windows.ps1), [ref]$t, [ref]$e) | Out-Null
        if ($e) { $e | ForEach-Object { "  {0}:{1} {2}" -f $_.Extent.StartLineNumber, $_.Extent.StartColumnNumber, $_.Message }; exit 1 }
        "  構文エラーなし ({0} トークン)" -f $t.Count' 2>&1); then
        echo "$out"
        # Windows 以外では即座に中断して終了コード1を返すこと(そこだけは実行検証できる)
        # ガードが壊れていると restore/build に進んで数分かかるため timeout で切る
        timeout 30 "$PWSH" -NoProfile -File verify-windows.ps1 >/dev/null 2>&1
        rc=$?
        if [ $rc -eq 0 ]; then
            echo "  非Windowsで成功してしまった(ガードが効いていない)"; fail=1
        elif [ $rc -eq 124 ]; then
            echo "  非Windowsで即座に中断しなかった(ガードが効いていない)"; fail=1
        else
            echo "  非Windowsでは中断する(ガード動作を確認)"
        fi
    else
        echo "$out"; fail=1
    fi
else
    echo "  pwsh が無いためスキップ(構文未検証)"
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
    n=$(grep -l "name=\"$k\"" ../src/AeroDriver.Languages/Resources/Strings.resx ../src/AeroDriver.Languages/Resources/Strings.*-*.resx 2>/dev/null | wc -l)
    [ "$n" -eq 10 ] || { echo "  $k: $n/10"; miss=1; fail=1; }
done
[ $miss -eq 0 ] && echo "  使用中の全キーが 10/10"

# 9. 未使用リソースキー(翻訳コストだけ払って誰も表示しないキーを溜めない)
printf '\n=== ConfigureAwait(CLAUDE.md 規則4) ===\n'
python3 check-configureawait.py || fail=1

printf '\n=== プロセス引数の組み立て(CLAUDE.md 規則5) ===\n'
python3 check-processargs.py || fail=1

printf '\n=== 検証→実行の同一性(TOCTOU) ===\n'
python3 check-toctou.py || fail=1

printf '\n=== キャンセルの伝播(CLAUDE.md 規則3) ===\n'
python3 check-cancellation.py || fail=1

printf '\n=== リソース値と呼び出し形式の整合 ===\n'
python3 check-resources.py || fail=1

printf '\n=== 未使用リソースキー ===\n'
orphan=0
for k in $(python3 -c "
import xml.etree.ElementTree as ET
for d in ET.parse('../src/AeroDriver.Languages/Resources/Strings.resx').getroot().findall('data'):
    print(d.get('name'))
"); do
    grep -rq "\"$k\"" ../src --include=*.cs || { echo "  未使用: $k"; orphan=1; fail=1; }
done
[ $orphan -eq 0 ] && echo "  全キーに使用箇所あり"

printf '\n%s\n' "$([ $fail -eq 0 ] && echo '=== すべて成功 ===' || echo '=== 失敗あり ===')"
exit $fail
