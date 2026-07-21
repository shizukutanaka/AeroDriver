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

1. **全コードがビルド未検証**(最重要)。開発環境に.NET SDKがなく静的検証のみ。
   特にWPF XAML+CommunityToolkit.Mvvmソースジェネレーターはコンパイルエラーリスクが高い
2. **CI不在**。GitHub Appトークンに`workflows`権限がなく`.github/workflows/build.yml`をpushできない
   (YAML本文は`FEATURE_AUDIT.md` §5に用意済み)
3. **ブロックリストのTTLがプロセス生存中無視される**: `VulnerableDriverBlocklist.cs:72,77`の
   `if (_hashes != null) return _hashes;`により、初回ロード結果(フェイルオープンの空集合を含む)が
   プロセス終了まで固定。長時間稼働時に7日TTL・ネットワーク復旧が反映されない
4. **WUA COMのRCW未解放**: `WindowsUpdateAgentSource.cs`は`Microsoft.Update.Session`等を
   `Marshal.ReleaseComObject`せずGC任せ(反復ポーリングでネイティブリソースが滞留しうる)
5. **テーマ/言語が永続化されない**: `ISettingsService`に該当キーがなく、GUIの選択は再起動で消える
6. **一括インストールのAdminRequired非効率**: 管理者権限がない場合も全件を順に試行して全件失敗する
   (`MainViewModel.InstallAllUpdatesAsync` / CLI `RunInstallAllAsync`)
7. **MainViewModelのテストが0本**(設計はモック可能なのに未着手)
8. **JSONライブラリ混在**: `WhqlDatabaseService`はNewtonsoft、他はSystem.Text.Json
9. **キャッシュ実装の三重複**: PciIdDatabase/VulnerableDriverBlocklist/(WhqlDatabaseServiceの
   キャッシュ)が同型のダウンロード→LOCALAPPDATA→TTLパターンを個別実装
10. **USB非対応の更新照合**: `WhqlDatabaseService.FindDriverByHardwareIdAsync`はPCI VEN/DEVのみ。
    USB VID/PIDデバイスは常に「見つからない」
11. **DriverInstallOrderはヒューリスティック**: DeviceClass優先度のみで、INF内の実依存関係は見ない
12. **メッセージのローカライズ不整合**: `MainViewModel.DescribeResult`(`MainViewModel.cs:309-321`)と
    CLI `Program.DescribeInstallResult`は、成功時の接頭辞だけ`ILanguageService.GetString`で翻訳し、
    失敗理由の本文はハードコードの日本語。非日本語UIでも失敗メッセージが日本語で出る
13. **`RunAsync`の`_cts`はコマンドのCanExecute(`IsBusy`)にのみ依存**(`MainViewModel.cs:272-307`):
    多重起動はCanExecuteで防いでいるが、`_cts`自体に再入ガードがない。将来コマンドを
    プログラム的に直接呼ぶ改修を入れると`_cts`が上書きされうる(現状は問題なし・要注意点)

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

- [ ] **ブロックリストTTLのプロセス内再評価**(短所3): `EnsureLoadedAsync`で`_hashes`と併せて
  ロード時刻を保持し、TTL超過なら`_loadLock`内で再ロード。フェイルオープンの空集合は
  短い再試行間隔(例: 15分)にする。対象: `src/AeroDriver.Core/Services/VulnerableDriverBlocklist.cs`。
  受け入れ条件: 既存`VulnerableDriverBlocklistTests`がpassし、「空集合が再試行される」テストを追加
- [ ] **一括インストールのAdminRequired早期中断**(短所6): 1件目が`AdminRequired`なら残りをスキップし
  「管理者権限が必要」を1回だけ表示。対象: `MainViewModel.InstallAllUpdatesAsync`、
  CLI `Program.RunInstallAllAsync`。受け入れ条件: 非管理者実行時にN回ではなく1回で失敗を報告
- [ ] **WUA RCWの明示解放**(短所4): `SearchUpdatesAsync`/`FindDriverAsync`のCOMオブジェクトを
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
- [ ] **USB VID/PID対応**(短所10): `WhqlDatabaseService`の正規表現に`USB\VID_xxxx&PID_xxxx`分岐を追加
- [ ] **JSON統一**(短所8): `WhqlDatabaseService`のNewtonsoftをSystem.Text.Jsonに移行し
  パッケージ参照を削除(`AeroDriver.Languages`のNewtonsoft参照も未使用なら削除)
- [ ] **失敗メッセージのローカライズ**(短所12): `DriverInstallResult`各値のメッセージを
  リソースキー化(`Install_Result_AdminRequired`等)して全10言語に追加し、`DescribeResult`/
  `DescribeInstallResult`を`ILanguageService`経由に。受け入れ条件: 非日本語カルチャで
  失敗理由が翻訳表示される

### P3 — リファクタリング(急がない)

- [ ] キャッシュ基盤の共通化(短所9): `CachedRemoteFile`等の基底に3実装を集約
- [ ] GUI: 一括インストール完了時の結果サマリーダイアログ(成功/失敗の内訳一覧)
- [ ] INFベースの真の依存解決(短所11)は費用対効果を検討してから
