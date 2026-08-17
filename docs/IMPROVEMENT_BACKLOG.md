# AeroDriver 長所・短所・改善バックログ

2026-07時点の棚卸し。短所は**行番号まで確認済みの事実のみ**を記載(憶測なし)。
改善タスクには優先度と推奨モデル([Opus]=複雑/セキュリティ、[Sonnet]=仕様確定済みの作業)を付与。
消化したら該当項目に取り消し線とコミットSHAを追記すること。作業規則は `/CLAUDE.md` 参照。

---

## 長所(維持すべきもの)

| 長所 | 根拠 |
|------|------|
| セキュリティ多層防御 | `WinVerifyTrust`による真正Authenticode検証(`AuthenticodeHelper.cs`)、BYOVDブロックリスト全経路適用(`VulnerableDriverBlocklist.cs`+DriverService/BackupService/PnpUtilDriverSource)、WQLサニタイズ(`WqlSanitizer.cs`)、パストラバーサル対策3件、HTTPS強制、TOCTOU対策(ダウンロード〜実行間のFileShare.Readロック)、`ElevationGuard` |
| 引き継ぎ文化 | `docs/FEATURE_AUDIT.md`が実装/修正/未解決を台帳化。「宣言と実装の一致」規律 |
| テスト容易設計 | protectedコンストラクタでキャッシュパス注入(PciIdDatabase/VulnerableDriverBlocklist/BackupService)、純粋関数化(`DriverInstallOrder`/`VersionHelper`/`WqlSanitizer`)、GUIも`IFileDialogService`/`IThemeService`で抽象化 |
| 性能配慮 | `FrozenDictionary/Set`、`ArrayPool`、`[LoggerMessage]`、JSONソースジェネレーション、BoundedChannelバックプレッシャー |
| ロジック共有 | CLI/GUIが同一Coreサービスを消費(例: 一括インストール順序は`CheckForUpdatesAsync`1箇所で決まり両UIに反映) |
| ローカライズ | 10言語×19キー、パリティ機械検証済み。GUIは言語即時切替対応 |

---

## 短所(確認済みの事実)

行番号まで確認した事実のみ。解決済みの項目も「何が問題だったか」の記録として残す。

### 未解決

1. **大部分がビルド未検証**(最重要)。ただし状況は改善: Linux環境に .NET SDK 8 を
   `apt install dotnet-sdk-8.0` で導入でき、**BCLのみに依存する純粋ロジックは実コンパイル+実行で
   検証済み**(`tools/offline-verify`、**52アサーション全通過**)。ILogger 依存のサービス
   (`InstallHistoryService`/`SettingsService`/`AuthenticodeHelper` 等)も ASP.NET Core 共有
   フレームワーク経由で検証済み。
   **未検証のまま残るもの**: WMI(`Microsoft.Management.Infrastructure`)依存の `DriverService`/
   `BackupService`/`PnpUtilDriverSource`、および NuGet(api.nuget.org)がプロキシ遮断のため
   restore できない外部パッケージ依存(CLI の `System.CommandLine`、WPF の
   `CommunityToolkit.Mvvm`、テストの xunit)。
   特にWPF XAML+CommunityToolkit.Mvvmソースジェネレーターはコンパイルエラーリスクが高い
2. **CI不在**。GitHub Appトークンに`workflows`権限がなく`.github/workflows/build.yml`をpushできない
   (YAML本文は`FEATURE_AUDIT.md` §5に用意済み)
3. **テーマ/言語が永続化されない**: `ISettingsService`に該当キーがなく、GUIの選択は再起動で消える
4. **MainViewModelのテストが0本**(設計はモック可能なのに未着手)
5. **キャッシュ実装の三重複**: PciIdDatabase/VulnerableDriverBlocklist/WhqlDatabaseService が
   同型のダウンロード→LOCALAPPDATA→TTLパターンを個別実装
6. **DriverInstallOrderはヒューリスティック**: DeviceClass優先度のみで、INF内の実依存関係は見ない
7. **メッセージのローカライズ不整合**: `MainViewModel.DescribeResult` と CLI `DescribeInstallResult` は
   成功時の接頭辞だけ翻訳し、失敗理由の本文はハードコードの日本語
8. **`DisableDriverAsync` がUIから到達不能**: 実装済み(ブートクリティカル保護付き)だが CLI/GUI に導線なし
9. **`RunAsync`の`_cts`に再入ガードがない**(`MainViewModel`): 多重起動はCanExecuteで防いでいるが、
   将来コマンドをプログラム的に直接呼ぶ改修を入れると`_cts`が上書きされうる(現状は問題なし・要注意点)

### 解決済み(記録)

| # | 問題 | 解決 |
|---|------|------|
| A | ブロックリストのTTLがプロセス生存中無視され、フェイルオープンの空集合が固定化 | 643cb9e: `_loadedAtUtc` で再評価。空集合は15分で再試行 |
| B | WUA COMのRCWを解放せずGC任せ | 643cb9e: `ReleaseCom` で逆順解放 |
| C | 一括インストールがAdminRequiredでも全件試行し全件失敗 | 643cb9e: 即中断して1回だけ通知 |
| D | 「バックアップ」ボタンが実際にはカスタムインストールを実行 | bda8420: `Button_CustomInstall` を全10言語に追加し分離 |
| E | `Settings_CreateRestorePoint` が実体のない約束 | bda8420: `SystemRestoreHelper`(`SRSetRestorePointW`) |
| F | バックアップが書き込み専用(一覧・世代選択が不能) | bda8420: `GetAvailableBackupsAsync` と世代指定 `RollbackDriverAsync` |
| G | 死に設定 `AutoUpdateEnabled` / `IncludeBetaDrivers` | c6b49dc: 消費箇所を実装。ベータ判定は `IUpdate::IsBeta` |
| H | インストール履歴/監査証跡なし | f1227cf: `InstallHistoryService`(JSONL追記)、CLI `history` |
| I | JSONライブラリ混在(Newtonsoft + System.Text.Json) | 6bee763: STJ へ統一し Newtonsoft 参照を全削除 |
| J | USB非対応の更新照合(PCI決め打ち) | 36d710c: `HardwareIdParser` で PCI/USB 双方に対応 |
| O | `AuthenticodeHelper.GetCertificateInfo` が**コンパイル不能**。`CreateFromSignedFile` は基底 `X509Certificate` を返すため `var` で `NotBefore`/`NotAfter` が解決できず CS1061 | `X509Certificate2` に明示的に包み直す。実コンパイルで発見 |
| N | `Directory.Build.props` のXMLコメント内に `--`(`dotnet --info`)があり不正XML。**全プロジェクトのビルドが即失敗する状態だった** | コメント文言を修正。全 props/targets/csproj/resx/config/xaml のXML妥当性を一括検証 |
| M | ドライバーDLに上限がなく、`ArrayPool.Rent(Content-Length)` がサーバー申告値でLOHに巨大配列を確保しうる+`(int)`キャストで2GB超が負値化 | ストリーミングを固定81920チャンクに変更、実バイト数で4GiB上限、long のまま判定 |
| L | 再起動要求(3010/1641)を失敗と誤判定。ドライバーは3010で終わることが多く、成功が失敗と表示され更新一覧に残り続けた | `InstallerExitCode` で解釈。`DriverInstallResult.SuccessRebootRequired` を追加 |
| K | 署名検証の失敗理由が全て「署名が無効」で、オフライン時に誤診断 | `DescribeVerificationFailure` で原因を区別(**フェイルクローズは維持**) |

---

## 改善タスク

> [Opus]タスクの罠と設計背景は [INSTRUCTIONS_OPUS.md](INSTRUCTIONS_OPUS.md)、
> [Sonnet]タスクの手順書は [INSTRUCTIONS_SONNET.md](INSTRUCTIONS_SONNET.md) を参照。

### P0 — 人間の作業が必要(モデルでは完結不可)

- [ ] **Windows実機で `dotnet build AeroDriver.sln && dotnet test`** を実行し、コンパイルエラーを
  修正する(修正自体は [Opus] に委任可)。受け入れ条件: ビルド成功+全テストpass
- [ ] **CI YAMLの手動push**: `FEATURE_AUDIT.md` §5のYAMLを`workflows`権限のあるアカウントで
  `.github/workflows/build.yml`に追加。受け入れ条件: mainでActionsが緑

### P1 — 高価値・要注意 [Opus]

- [x] ~~**ブロックリストTTLのプロセス内再評価**(短所3)~~ 完了(643cb9e): `EnsureLoadedAsync`で`_hashes`と併せて
  ロード時刻を保持し、TTL超過なら`_loadLock`内で再ロード。フェイルオープンの空集合は
  短い再試行間隔(例: 15分)にする。対象: `src/AeroDriver.Core/Services/VulnerableDriverBlocklist.cs`。
  受け入れ条件: 既存`VulnerableDriverBlocklistTests`がpassし、「空集合が再試行される」テストを追加
- [x] ~~**一括インストールのAdminRequired早期中断**(短所6)~~ 完了(643cb9e): 1件目が`AdminRequired`なら残りをスキップし
  「管理者権限が必要」を1回だけ表示。対象: `MainViewModel.InstallAllUpdatesAsync`、
  CLI `Program.RunInstallAllAsync`。受け入れ条件: 非管理者実行時にN回ではなく1回で失敗を報告
- [x] ~~**WUA RCWの明示解放**(短所4)~~ 完了(643cb9e): `SearchUpdatesAsync`/`FindDriverAsync`のCOMオブジェクトを
  try/finallyで`Marshal.FinalReleaseComObject`。dynamic経由のRCW解放は罠が多いためOpus推奨。
  受け入れ条件: 既存の「COM不在環境でグレースフル」テストがpassのまま

### P1 — 仕様確定済み [Sonnet]

- [ ] **テーマ/言語の永続化**(短所5): `ISettingsService`/`SettingsData`に`ThemeName`と`CultureName`を
  追加(JSONソースジェネレーションの`SettingsJsonContext`更新を忘れない)。GUI起動時に復元、
  変更時に保存。受け入れ条件: `SettingsServiceTests`に新キーの保存/復元テスト追加

### P2 — 品質向上 [Sonnet]

- [ ] **MainViewModelのユニットテスト**(短所7): `AeroDriver.UI.Tests`プロジェクト新設
  (注意: 過去に幽霊参照事故あり。slnへの追加を確実に)。`IFileDialogService`/`IThemeService`/
  `IServiceScopeFactory`をNSubstituteでモックし、Scan/InstallAll/言語切替の状態遷移を検証
- [x] ~~**USB VID/PID対応**(短所10)~~ 完了(36d710c): `HardwareIdParser` を新設し
  `WhqlDatabaseService` と `WindowsUpdateAgentSource` の双方から利用。複合USBの `&MI_xx` も保持
- [x] ~~**JSON統一**(短所8)~~ 完了: `WhqlDatabaseService` を System.Text.Json ソースジェネレーションへ
  移行。`AeroDriver.Core` と `AeroDriver.Languages`(未使用だった)双方の Newtonsoft 参照を削除
- [ ] **失敗メッセージのローカライズ**(短所12): `DriverInstallResult`各値のメッセージを
  リソースキー化(`Install_Result_AdminRequired`等)して全10言語に追加し、`DescribeResult`/
  `DescribeInstallResult`を`ILanguageService`経由に。受け入れ条件: 非日本語カルチャで
  失敗理由が翻訳表示される

### P3 — リファクタリング(急がない)

- [ ] キャッシュ基盤の共通化(短所9): `CachedRemoteFile`等の基底に3実装を集約
- [ ] GUI: 一括インストール完了時の結果サマリーダイアログ(成功/失敗の内訳一覧)
- [ ] INFベースの真の依存解決(短所11)は費用対効果を検討してから
