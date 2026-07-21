# Sonnet級モデル向け指示書

対象: `IMPROVEMENT_BACKLOG.md`の **[Sonnet]** ラベルタスク(仕様が確定していて、迷わず実行できる作業)。
共通規則は [/CLAUDE.md](../CLAUDE.md)。難易度の高い判断が要る [Opus] タスクは
[INSTRUCTIONS_OPUS.md](INSTRUCTIONS_OPUS.md) に分離してある。

各タスクは「触るファイルの全リスト」「変更内容」「受け入れ条件」「ハマりどころ」を手順書形式で記載。

---

## タスクA: テーマ/言語の永続化(短所5) — [P1 Sonnet]

GUIのテーマ・言語選択が再起動で消える。`ISettingsService`に保存して起動時に復元する。

**触るファイルと変更(この順のチェーンを全て変えないと壊れる)**:
1. `src/AeroDriver.Core/Services/SettingsService.cs`
   - `SettingsData` **positional record**(123-127行)に `string? ThemeName` と `string? CultureName` を追加。
   - `SettingsData.Default`(129-133行)に既定値を追加(例: `ThemeName: "Light", CultureName: null`)。
   - `SettingsJsonContext`(139-140行)は `[JsonSerializable(typeof(SettingsData))]` のまま変更不要
     (レコード全体を対象にしているため自動で追随)。
2. `src/AeroDriver.Core/Interfaces/ISettingsService.cs`
   - `string? ThemeName { get; set; }` と `string? CultureName { get; set; }` を追加。
3. `SettingsService.cs` のプロパティ群(38-78行と同じ形)
   - 2つのプロパティを **必ず `lock (_lock)` で読み書きし、setterで `Save()`** を呼ぶ(既存4プロパティと同一パターン)。
4. `src/AeroDriver.UI/App.xaml.cs`(`OnStartup`)
   - DIビルド後、`ISettingsService` を解決して `ThemeName`/`CultureName` を読み、
     `IThemeService.Apply(...)` と `ILanguageService.SetCulture(...)` で復元してから `MainWindow` を表示。
5. `src/AeroDriver.UI/ViewModels/MainViewModel.cs`
   - `OnSelectedThemeChanged`(既存)と `OnSelectedCultureChanged`(既存)で、切替時に
     `ISettingsService.ThemeName`/`CultureName` に保存。ViewModelに `ISettingsService` をコンストラクタ注入
     (登録は `ServiceCollectionExtensions` で `ISettingsService` が既にSingleton)。

**ハマりどころ**:
- **後方互換**: 既存の `settings.json` には新フィールドが無い。System.Text.Jsonのpositional record
  デシリアライズでは、JSONに無いコンストラクタ引数は型の既定値(`null`)になる → `Load()` は壊れないが、
  `ThemeName` が `null` の場合は `"Light"` 相当にフォールバックする分岐をApp側に入れる。
- `CultureName` を復元する際、`SupportedCultures` に無い値なら無視(`LanguageService.SetCulture` の
  挙動を確認)。

**受け入れ条件**: `tests/AeroDriver.Core.Tests/Services/SettingsServiceTests.cs` に
「ThemeName/CultureName を保存→別インスタンスで読み込むと復元される」テストを追加(既存テストの
`new SettingsService(logger, tempFile)` パターンを流用)。

---

## タスクB: 失敗メッセージのローカライズ(短所12) — [P2 Sonnet]

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

## タスクC: MainViewModelのユニットテスト(短所7) — [P2 Sonnet]

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

## タスクD: USB VID/PID対応(短所10) — [P2 Sonnet]

**対象**: `src/AeroDriver.Core/Services/WhqlDatabaseService.cs`(`FindDriverByHardwareIdAsync`)。
現在は `PCI\VEN_xxxx&DEV_xxxx` の正規表現のみ。`USB\VID_xxxx&PID_xxxx` の分岐を追加。
**受け入れ条件**: 既存 `WhqlDatabaseServiceTests` の形式で USB HardwareID のパーステストを追加。

---

## タスクE: JSON統一(短所8) — [P2 Sonnet]

**対象**: `WhqlDatabaseService.cs`(`using Newtonsoft.Json;`, 10行)を `System.Text.Json` に移行。
移行後、`Newtonsoft.Json` の `PackageReference` を各 `.csproj` から削除(`AeroDriver.Languages.csproj:14`
にもあるが未使用なら削除)。**ハマりどころ**: Newtonsoftの `JsonConvert.DeserializeObject<T>` の
null戻り値ハンドリング(FEATURE_AUDIT §4で過去に修正した箇所)を System.Text.Json でも維持する。

---

## 全タスク共通の締め

- 変更後 `docs/FEATURE_AUDIT.md` を更新し、`docs/IMPROVEMENT_BACKLOG.md` の該当項目に
  取り消し線+コミットSHAを付ける。
- コミット→push→PR→mainマージ。SDKが無ければ「ビルド未検証」と明記。
