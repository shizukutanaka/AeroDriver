# CLAUDE.md — AeroDriver 作業指示書(全モデル共通)

Windows用ドライバー管理ツール。`AeroDriver.Core`(net8.0、WMI/pnputil/WUA COM) の上に
CLI(`AeroDriver.CLI`)とWPF GUI(`AeroDriver.UI`、net8.0-windows)が乗る構成。10言語対応
(`AeroDriver.Languages`、29キー×10 resx)。

**最初に読むもの**: `docs/FEATURE_AUDIT.md`(実装済み/修正済み/未解決の引き継ぎ台帳)と
`docs/IMPROVEMENT_BACKLOG.md`(長所/短所/優先度付き改善タスク。推奨モデルラベル付き)。
仕様が確定した残タスクの手順は `docs/INSTRUCTIONS_SONNET.md` にある。

## 絶対規則(違反PRは出さない)

1. **課金要素・テレメトリ禁止**。データソースとツールはWindows標準またはOSS/無料のみ
2. **Windows標準API優先**: `CimSession`(WMI)、`pnputil.exe`、WUA COM
3. **`OperationCanceledException`は再スロー**。`catch (Exception)`で握りつぶさない
4. **`ConfigureAwait(false)`** をライブラリ層(Core)全体で使用(UI層は `(true)` のまま)
5. **`ProcessStartInfo.ArgumentList`** を使う。文字列結合で引数を組み立てない
6. **宣言と実装を一致させる**: nullable注釈・XMLdoc・READMEは、実装がその通り動くことを
   確認してから書く。このリポジトリで最も繰り返し破られてきたルール
7. **セキュリティ判定はフェイルクローズ、可用性層はフェイルオープン**。この非対称は意図的:
   署名検証・BYOVD照合は「確認できなければ拒否」(検証不能な物を通さない)。
   復元ポイント作成・ブロックリスト取得・履歴記録は「失敗しても本処理を止めない」
   (安全網が使えないことを理由に機能全体を殺さない)。**一方に合わせて「統一」しないこと**
8. 外部入力(WMI文字列・ダウンロードURL・ユーザー指定パス)は信用しない:
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
- **まず `tools/verify-all.sh` を実行する**。この環境で可能な検証(Core の実コンパイル+実行、
  WPF/CLI の型検査、XML妥当性、リソースキーのパリティ)を一括で回す。**変更後は必ず通すこと**
- **SDKが無いと思ったらまず導入を試すこと**: Ubuntu なら `apt-get update && apt-get install -y dotnet-sdk-8.0`
  で入る(2026-08時点で確認済み)。ただし NuGet はプロキシ遮断されるため、外部パッケージに依存する
  プロジェクトは restore できない。**BCLのみに依存する純粋ロジックは `tools/offline-verify` で
  実コンパイル+実行検証できる**(`cd tools/offline-verify && dotnet run`)。新しい純粋ロジックを
  足したらここにも追加すること。**WPF層の手書きC#は `tools/ui-typecheck` で型検査できる**
  (WPF/CommunityToolkit の最小スタブに対して実コンパイル。XAML とジェネレーター実出力は対象外)。
  **CLI は `tools/cli-typecheck`**(System.CommandLine の最小スタブ。実パース挙動は対象外)
- **それでも検証できない部分**では静的検証を行い、コミットメッセージに「ビルド未検証」と明記:
  - 波括弧/括弧バランス(python等で機械チェック)
  - リソースキー追加時は**全10言語**の`.resx`に追加し、XML妥当性とキーパリティを機械検証
  - XAMLの`{Binding XxxCommand}`名 ⇔ ViewModelの`[RelayCommand]`メソッド名の一致
  - DIライフタイム(Singleton→Scopedのcaptive dependencyを作らない)
- **重要**: WMI依存(`DriverService`/`WdacHelper`)・XAMLコンパイル・ソースジェネレーターの
  実出力・System.CommandLine の実パース挙動は依然未検証。Windows実機で
  `dotnet build AeroDriver.sln && dotnet test` を通すことが最優先タスク
  (`IMPROVEMENT_BACKLOG.md` P0参照)
- **XMLコメントに連続ハイフンを書かないこと**。`Directory.Build.props` に
  `dotnet --info` と書かれていたせいでファイルが不正XMLになり、
  全プロジェクトのビルドが即失敗する状態になっていた実績がある

## 作業後の義務

- `docs/FEATURE_AUDIT.md` を更新する(実装した事実・確認済みの制約のみ。憶測は書かない)
- 改善タスクを消化したら `docs/IMPROVEMENT_BACKLOG.md` の該当項目に取り消し線+コミットSHA
- コミットは変更の単位で分割し、pushしてPR経由でmainへ

## モデル別の目安

- **Opus級**: セキュリティ関連・並行処理・COM相互運用・P/Invoke(バックログの [Opus] ラベル)
- **Sonnet級**: リソースキー追加・テスト追加・設定永続化・doc更新など仕様が確定した作業
  (バックログの [Sonnet] ラベル)
