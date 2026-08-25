# ui-run — MainViewModel を「実行」して検証する

## これは何か

`AeroDriver.UI` の `MainViewModel` を実際にインスタンス化し、コマンドを実行して
状態遷移を検証するハーネス。`tools/offline-verify` と同じ `Check()` 方式で、
非0終了コードで失敗を報告する。

```bash
cd tools/ui-run
dotnet run
```

## なぜ xunit ではないのか

NuGet がこの環境では遮断されており xunit を restore できない。しかし xunit は手段であって
目的ではない。`partial class` は同じ型の private メンバーにアクセスできるため、
ジェネレーター再現ファイル (`Generated.cs`) のコマンドを**実 private ハンドラーと
実 CanExecute 述語へ配線**すれば、ViewModel はそのまま実行できる。

`tools/ui-typecheck` との違いはここだけ:

| | ui-typecheck | ui-run |
|---|---|---|
| コマンド | inert な `StubAsyncCommand` | `RealAsyncCommand`(デリゲートを実行) |
| 目的 | 型検査(App.xaml.cs / MainWindow.xaml.cs も含む) | 振る舞いの実行検証 |

DI コンテナは**本物**の `Microsoft.Extensions.DependencyInjection`(ASP.NET Core 共有
フレームワーク経由)を使うため、「`IDriverService` は Scoped なので操作ごとに
`CreateScope()` する」という ViewModel の前提もそのまま検証される。
モックは手書き(`Mocks.cs`)。

## 何を検証しているか

- Scan / CheckUpdates → コレクションが埋まる、累積しない、完了後 `IsBusy == false`
- 例外経路 → `IsBusy` が確実に戻り、メッセージはリソースキー経由
- **InstallAll で `AdminRequired` → 1件目で中断し2件目を呼ばない**(呼び出し回数で検証)
- InstallAll で個別失敗(署名不正・既知脆弱)→ 中断せず最後まで継続し、成功分のみ除去
- `SuccessRebootRequired` を成功として扱い、再起動件数を集計する
- `DescribeResult` が全失敗理由についてリソースキー経由(ハードコード文字列が出ない)
- CanExecute 述語(未選択時の Backup/Rollback/Details 不可、更新0件時の InstallAll 不可)
- 選択変更で詳細ペインがクリアされる
- カスタムインストールでダイアログキャンセル時にインストールを呼ばない
- 言語切替 → ラベル群の `PropertyChanged` 発火 + `ISettingsService.CultureName` に保存
- テーマ切替 → `IThemeService.Apply` + `ThemeName` 保存

## 何を検証**できない**か

- **XAML**(バインディング解決・スタイル・テーマ辞書の差し替え)。Windows 必須
- **ソースジェネレーターの実際の出力**。`Generated.cs` は規約に基づく再現
- **実 WMI / 実 pnputil**。`IDriverService` はモック
- `Progress<T>` の同期的な進捗反映。`SynchronizationContext` が無いと ThreadPool 経由に
  なるため、進捗の途中経過ではなく**最終状態のみ**を検証している

したがって Windows 実機での `dotnet build AeroDriver.sln && dotnet test` は依然として必要。

## 更新すべきとき

ViewModel に `[RelayCommand]` / `[ObservableProperty]` を足したら `Generated.cs` に、
インターフェースにメンバーを足したら `Mocks.cs` に追加する(忘れるとコンパイルエラーで気付ける)。
