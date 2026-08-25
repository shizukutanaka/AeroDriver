# di-run — DI コンテナを「実行」して検証する

## これは何か

`ServiceCollectionExtensions.ConfigureServices()` を実際に呼び、コンテナを構築して
サービスを解決するハーネス。`tools/offline-verify` と同じ `Check()` 方式で、
非0終了コードで失敗を報告する。

```bash
cd tools/di-run
dotnet run
```

## なぜ必要か

**DI の解決失敗と captive dependency は実行時にしか出ない。** 型検査では絶対に見つからず、
GUI/CLI を起動して初めて `InvalidOperationException` で落ちる。

`CLAUDE.md` は「DIライフタイム(Singleton→Scopedのcaptive dependencyを作らない)」を
静的チェック項目として挙げていたが自動化されておらず、`ServiceCollectionExtensionsTests.cs`
は xunit のためこの環境では走らない。つまり `ConfigureServices` は**一度も実行されて
いなかった**。

WMI と `AddStandardResilienceHandler` のスタブは `tools/core-typecheck/Stubs.cs` を共有する。
`Microsoft.Extensions.DependencyInjection` / `.Http` / `.Logging` は ASP.NET Core 共有
フレームワークに実在するため、**`AddHttpClient` とコンテナは本物が動く**。

## 何を検証できるか

- `ConfigureServices()` が例外なく完了する
- `BuildServiceProvider(ValidateOnBuild: true, ValidateScopes: true)` が成功する
  — 全登録の依存が解決可能か総当たりで検証し、captive dependency を検出する
- 主要サービス(`IDriverService` / `IBackupService` / `ISettingsService` /
  `IInstallHistoryService` / `VulnerableDriverBlocklist`)がスコープ内で実際に解決できる
- `IEnumerable<IDriverUpdateSource>` が2件(`DriverService` はこの形で受け取る)
- ライフタイムが意図どおり(設定・履歴・ブロックリストは Singleton、
  `IDriverService` は Scoped)
- `ValidateScopes` のガード自体が効いていること
  — これが効いていないと上のライフタイム検証が骨抜きになるため明示的に確認する

登録を1つ消す / `ISettingsService` を Scoped に変える の両方で失敗を検出できることを
確認済み。

## 何を検証**できない**か(重要)

- **実 WMI**。`CimSession` はスタブなので `DriverService` は構築できるだけ
- **実 HTTP**。`AddHttpClient` は本物だが通信はしない
- **レジリエンスポリシー**。`AddStandardResilienceHandler` は no-op スタブ
  (リトライ・サーキットブレーカー・タイムアウトの挙動は未検証)
- **UI 層の DI**。`App.xaml.cs` の追加登録は WPF が必要なため対象外

## 副作用に注意

`SettingsService` / `InstallHistoryService` / `BackupService` の既定コンストラクタは
`Environment.GetFolderPath(LocalApplicationData)` を使うため、実行すると
`~/.local/share/AeroDriver/` 配下にディレクトリが作られる(実際に `Backups/` ができる)。
アプリ自身のディレクトリなので無害だが、完全に副作用なしではない。
