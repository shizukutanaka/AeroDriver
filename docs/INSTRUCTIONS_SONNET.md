# Sonnet級モデル向け指示書

対象: `IMPROVEMENT_BACKLOG.md`の **[Sonnet]** ラベルタスク(仕様が確定していて、迷わず実行できる作業)。
共通規則は [/CLAUDE.md](../CLAUDE.md)。

各タスクは「触るファイルの全リスト」「変更内容」「受け入れ条件」「ハマりどころ」を手順書形式で記載。

---

## タスクA: MainViewModelのユニットテスト(短所7) — **完了(`tools/ui-run`)**

この環境では NuGet が遮断されており xunit/NSubstitute を restore できないため、
**xunit ではなく `tools/ui-run` で同じ検証を実行済み**(73アサーション(記録時点の値。現在の件数は tools/verify-all.sh の出力を参照)全通過)。
ジェネレーター再現側(`Generated.cs`)のコマンドを実 private ハンドラーと実 CanExecute 述語へ
配線し、手書きモック(`Mocks.cs`)と**本物の `Microsoft.Extensions.DependencyInjection`** で
ViewModel を実際に走らせる方式。`tools/verify-all.sh` から自動実行される。

**NuGet が使える環境で xunit 版を作る場合**(任意。優先度は低い):

1. `tests/AeroDriver.UI.Tests/AeroDriver.UI.Tests.csproj` を新設(`net8.0-windows`、
   `AeroDriver.UI` を参照、xunit/NSubstitute/FluentAssertions は既存テストプロジェクトの版に合わせる)。
2. **`AeroDriver.sln` への追加を確実に**(過去に「幽霊プロジェクト参照」でビルドが壊れた事故あり。
   FEATURE_AUDIT.md §4参照)。GUIDと構成マッピングを既存プロジェクトと同形式で追記。
3. 検証項目は `tools/ui-run/Program.cs` をそのまま移植できる。モックも `tools/ui-run/Mocks.cs` が
   NSubstitute の代わりにそのまま使える(手書きなので依存ゼロ)。

**ハマりどころ**: `System.Progress<T>` はテスト環境に SynchronizationContext が無いとThreadPoolで
コールバックする。テストでは進捗の同期検証を避け、最終状態を検証する(`ui-run` もそうしている)。

---

## 全タスク共通の締め

- 変更後 `docs/FEATURE_AUDIT.md` を更新し、`docs/IMPROVEMENT_BACKLOG.md` の該当項目に
  取り消し線+コミットSHAを付ける。
- コミット→push→PR→mainマージ。SDKが無ければ「ビルド未検証」と明記。
