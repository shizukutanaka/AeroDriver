# Sonnet級モデル向け指示書

対象: `IMPROVEMENT_BACKLOG.md`の **[Sonnet]** ラベルタスク(仕様が確定していて、迷わず実行できる作業)。
共通規則は [/CLAUDE.md](../CLAUDE.md)。

各タスクは「触るファイルの全リスト」「変更内容」「受け入れ条件」「ハマりどころ」を手順書形式で記載。

---

## タスクA: MainViewModelのユニットテスト(短所7) — [P2 Sonnet]

**手順**:
1. `tests/AeroDriver.UI.Tests/AeroDriver.UI.Tests.csproj` を新設(`net8.0-windows`、
   `AeroDriver.UI` を参照、xunit/NSubstitute/FluentAssertions は既存テストプロジェクトの版に合わせる)。
2. **`AeroDriver.sln` への追加を確実に**(過去に「幽霊プロジェクト参照」でビルドが壊れた事故あり。
   FEATURE_AUDIT.md §4参照)。GUIDと構成マッピングを既存プロジェクトと同形式で追記。
3. `IServiceScopeFactory`/`ILanguageService`/`IFileDialogService`/`IThemeService`/`ILogger<MainViewModel>` を
   NSubstituteでモックし、`IDriverService` もモックしてスコープから返す。
4. 検証する状態遷移: `ScanCommand` 実行で `InstalledDrivers` が満たされる / `IsBusy` が実行中true→完了false /
   `InstallAllUpdatesAsync` が成功項目を除去 / 言語切替でラベルプロパティの `PropertyChanged` が発火。

**ハマりどころ**: `System.Progress<T>` はテスト環境に SynchronizationContext が無いとThreadPoolで
コールバックする。テストでは進捗の同期検証を避け、最終状態を検証する。

---

## 全タスク共通の締め

- 変更後 `docs/FEATURE_AUDIT.md` を更新し、`docs/IMPROVEMENT_BACKLOG.md` の該当項目に
  取り消し線+コミットSHAを付ける。
- コミット→push→PR→mainマージ。SDKが無ければ「ビルド未検証」と明記。
