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

# 2/3. スタブに対する型検査(出力は成否のみで十分)
for t in ui-typecheck cli-typecheck; do
    printf '\n=== %s: 型検査 ===\n' "$t"
    if out=$(cd "$t" && dotnet build -v q --nologo 2>&1); then
        echo "  Build succeeded"
    else
        echo "$out" | grep -E "error" | sed 's|.*/src/|src/|' | sed 's| \[/.*||' | sort -u | head -10
        fail=1
    fi
done

# 4. XML 妥当性(不正な props でビルドが即死した実績があるため必ず見る)
printf '\n=== XML 妥当性 ===\n'
bad=0
while IFS= read -r f; do
    python3 -c "import xml.dom.minidom,sys;xml.dom.minidom.parse('$f')" 2>/dev/null || { echo "  BAD $f"; bad=1; fail=1; }
done < <(find .. \( -name "*.props" -o -name "*.csproj" -o -name "*.resx" -o -name "*.config" -o -name "*.xaml" \) ! -path "*/obj/*" ! -path "*/bin/*")
[ $bad -eq 0 ] && echo "  全件妥当"

# 5. リソースキーのパリティ(1言語でも欠けると実行時に "[キー名]" が出る)
printf '\n=== リソースキーのパリティ(10言語) ===\n'
miss=0
for k in $(grep -ohP 'GetString\("\K[^"]+' ../src/AeroDriver.UI/ViewModels/MainViewModel.cs ../src/AeroDriver.CLI/Program.cs | sort -u); do
    n=$(grep -l "name=\"$k\"" ../src/AeroDriver.Languages/Resources/Strings.*.resx 2>/dev/null | wc -l)
    [ "$n" -eq 10 ] || { echo "  $k: $n/10"; miss=1; fail=1; }
done
[ $miss -eq 0 ] && echo "  使用中の全キーが 10/10"

printf '\n%s\n' "$([ $fail -eq 0 ] && echo '=== すべて成功 ===' || echo '=== 失敗あり ===')"
exit $fail
