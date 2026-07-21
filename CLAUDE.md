# CLAUDE.md — AeroDriver 作業指示書(全モデル共通)

Windows用ドライバー管理ツール。`AeroDriver.Core`(net8.0、WMI/pnputil/WUA COM) の上に
CLI(`AeroDriver.CLI`)とWPF GUI(`AeroDriver.UI`、net8.0-windows)が乗る構成。10言語対応
(`AeroDriver.Languages`、19キー×10 resx)。

**最初に読むもの**: `docs/FEATURE_AUDIT.md`(実装済み/修正済み/未解決の引き継ぎ台帳)と
`docs/IMPROVEMENT_BACKLOG.md`(長所/短所/優先度付き改善タスク。推奨モデルラベル付き)。
着手するタスクに応じて、**Opus級は `docs/INSTRUCTIONS_OPUS.md`**(罠・設計背景・不変条件)、
**Sonnet級は `docs/INSTRUCTIONS_SONNET.md`**(手順書形式のタスク仕様)も読むこと。

## 絶対規則(違反PRは出さない)

1. **課金要素・テレメトリ禁止**。データソースとツールはWindows標準またはOSS/無料のみ
2. **Windows標準API優先**: `CimSession`(WMI)、`pnputil.exe`、WUA COM
3. **`OperationCanceledException`は再スロー**。`catch (Exception)`で握りつぶさない
4. **`ConfigureAwait(false)`** をライブラリ層(Core)全体で使用(UI層は `(true)` のまま)
5. **`ProcessStartInfo.ArgumentList`** を使う。文字列結合で引数を組み立てない
6. **宣言と実装を一致させる**: nullable注釈・XMLdoc・READMEは、実装がその通り動くことを
   確認してから書く。このリポジトリで最も繰り返し破られてきたルール
7. 外部入力(WMI文字列・ダウンロードURL・ユーザー指定パス)は信用しない:
   WQLは`WqlSanitizer`、パスは`Path.GetFullPath`正規化+ルート配下検証 or `Path.GetFileName`

## やってはいけない「近代化」(根拠は FEATURE_AUDIT.md §7)

- System.CommandLine は **2.0.0-beta4 固定**(beta5は破壊的変更、GA未達)
- `SYSLIB0057` pragma は**維持**(`X509CertificateLoader`はAuthenticode抽出不可)
- `Win32_PnPSignedDriver` は**継続利用**(レガシーだが非推奨化されていない)
- `AuthenticodeHelper` の **WinVerifyTrust P/Invoke を X509Chain だけに戻さない**
  (X509Chainは署名がファイルの現バイト列をカバーしているか検証できない)
- BYOVDブロックリスト照合(`VulnerableDriverBlocklist`)を全インストール/復元経路
  (DriverService×2・BackupService復元・PnpUtilDriverSource)から外さない

## 検証手順

- **dotnet SDKがある環境**: `dotnet build AeroDriver.sln && dotnet test` を必ず実行
  (注意: `AeroDriver.UI`はnet8.0-windowsのためWindowsが必要)
- **SDKが無い環境**では最低限の静的検証を行い、コミットメッセージに「ビルド未検証」と明記:
  - 波括弧/括弧バランス(python等で機械チェック)
  - リソースキー追加時は**全10言語**の`.resx`に追加し、XML妥当性とキーパリティを機械検証
  - XAMLの`{Binding XxxCommand}`名 ⇔ ViewModelの`[RelayCommand]`メソッド名の一致
  - DIライフタイム(Singleton→Scopedのcaptive dependencyを作らない)
- **重要**: 2026-07時点で全コードがビルド未検証(SDKなし環境で開発)。SDKが使えるなら
  まずビルドを通すことが最優先タスク(`IMPROVEMENT_BACKLOG.md` P0参照)

## 作業後の義務

- `docs/FEATURE_AUDIT.md` を更新する(実装した事実・確認済みの制約のみ。憶測は書かない)
- 改善タスクを消化したら `docs/IMPROVEMENT_BACKLOG.md` の該当項目に取り消し線+コミットSHA
- コミットは変更の単位で分割し、pushしてPR経由でmainへ

## モデル別の目安

- **Opus級**: セキュリティ関連・並行処理・COM相互運用・P/Invoke(バックログの [Opus] ラベル)
- **Sonnet級**: リソースキー追加・テスト追加・設定永続化・doc更新など仕様が確定した作業
  (バックログの [Sonnet] ラベル)
