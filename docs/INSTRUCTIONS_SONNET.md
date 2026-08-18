# Sonnet級モデル向け指示書

対象: `IMPROVEMENT_BACKLOG.md`の **[Sonnet]** ラベルタスク(仕様が確定していて、迷わず実行できる作業)。
共通規則は [/CLAUDE.md](../CLAUDE.md)。

各タスクは「触るファイルの全リスト」「変更内容」「受け入れ条件」「ハマりどころ」を手順書形式で記載。

---

## タスクA: 失敗メッセージのローカライズ(短所12) — [P2 Sonnet]

`MainViewModel.DescribeResult`(`MainViewModel.cs:309-321`)と CLI `Program.DescribeInstallResult` は
成功接頭辞だけ翻訳し、失敗理由がハードコード日本語。全て `ILanguageService` 経由にする。

**手順**:
1. `DriverInstallResult` の各値に対応するリソースキーを決める(例: `Install_AdminRequired`,
   `Install_NoDownloadUrl`, `Install_InsecureUrl`, `Install_DownloadFailed`, `Install_SignatureInvalid`,
   `Install_KnownVulnerable`, `Install_InstallerFailed`, `Install_Cancelled`, `Install_UnknownError`)。
2. **全10言語の `.resx`** (`src/AeroDriver.Languages/Resources/Strings.*.resx`)に同じキーで `<data>` を追加。
   Pythonで一括挿入し、`xml.dom.minidom` で妥当性、キー数パリティ(現在19→28キー)を機械検証すること。
3. `DescribeResult`/`DescribeInstallResult` を `_lang.GetString("Install_...")` 呼び出しに置換。
   デバイス名等の埋め込みは `GetString(key, args)` オーバーロード(既存)を使う。

**ハマりどころ**: 1言語でもキーが欠けると `GetString` が `"[キー名]"` を表示する。挿入後に
`grep -c 'name="Install_AdminRequired"' Strings.*.resx` で10ファイル全てにあることを必ず確認。

---

## タスクB: MainViewModelのユニットテスト(短所7) — [P2 Sonnet]

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
