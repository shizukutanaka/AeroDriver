# AeroDriver 機能監査ドキュメント

## このドキュメントの目的と読み方

このファイルは、AI(Claude Opus/Sonnet等のモデル問わず)および人間のコントリビューターが
**コードを再調査せずに**「何が実装済みで、何が死んでいて、何が未着手か」を把握するための
引き継ぎ資料です。ソクラテス式監査(繰り返しの自己検証セッション)を通じて確認された事実のみを
記載しています。憶測や希望的観測は含みません。

各項目には該当ファイルパスを付記しています。記載内容とコードの実態が乖離していたら、
このドキュメントよりコードを信じてください(このドキュメント自体もいずれ古くなります)。

---

## 1. 実装済み機能(動作確認された事実)

### CLI (`src/AeroDriver.CLI/Program.cs`)
- `scan`: `CimSession`経由でインストール済みドライバーを列挙し進捗表示
- `update`: 全`IDriverUpdateSource`に問い合わせ、更新候補を一覧表示
- `install --device-id <id>`: `DriverInstallResult` enumで失敗理由を区別して表示
- `rollback --device-id <id>`: バックアップから実ファイル復元
- `details --device-id <id>`: 個別マッピング済みフィールド+生のWMIプロパティ全件を表示

### ドライバー検出・更新 (`src/AeroDriver.Core/Services/DriverService.cs`)
- `CimSession`(現行WMI API)によるドライバー列挙。`ManagementObjectSearcher`は完全廃止済み
- `IAsyncEnumerable<DriverInfo> StreamAllDriversAsync`: `BoundedChannel(256)+Wait`でバックプレッシャー制御
- `SemaphoreSlim(1,1)`による非同期セーフなキャッシュ(TTL 30秒)
- 更新ソース: `PnpUtilDriverSource`(pnputil.exe)、`WindowsUpdateAgentSource`(WUA COM)、`WhqlDatabaseService`(Windows Update Catalog)

### バックアップ/復元 (`src/AeroDriver.Core/Services/BackupService.cs`)
- `pnputil /export-driver`で実際のドライバーファイル一式(INF+SYS+関連ファイル)をコピー
- `pnputil /add-driver /install`で実際に再インストールして復元
- `ISettingsService.MaxBackupGenerations`(既定3)に基づく自動ローテーション

### セキュリティ多層防御
- `src/AeroDriver.Core/Helpers/ElevationGuard.cs`: 管理者権限チェック(非Windowsではバイパス)
- `src/AeroDriver.Core/Helpers/WqlSanitizer.cs`: WQLインジェクション対策(アローリスト+エスケープ)
- `src/AeroDriver.Core/Helpers/AuthenticodeHelper.cs`: EXE/MSIのAuthenticode署名検証必須化。
  ネイティブ`WinVerifyTrust`(wintrust.dll)をP/Invokeで呼び出して検証する(旧実装は
  `X509Certificate2.CreateFromSignedFile`+`X509Chain.Build`のみで、埋め込み証明書の
  チェーン検証はできてもPKCS#7署名が実際にファイルの現バイト列を対象にしているかや
  コード署名EKUの有無は確認できていなかった)。X509系APIは表示用メタデータ
  (Issuer/Subject/有効期間)抽出にのみ残している
- `src/AeroDriver.Core/Services/VulnerableDriverBlocklist.cs`: LOLDriversの無料SHA256
  リストで既知の脆弱ドライバー(BYOVD)をブロック。`InstallDriverUpdateWithResultAsync`
  /`InstallCustomDriverAsync`/`BackupService.RestoreDriverAsync`/
  `PnpUtilDriverSource.AddDriverAsync`の全インストール/再インストール経路に適用済み
- `DriverService.InstallDriverUpdateAsync`: ダウンロードURLのHTTPS強制
- `DriverService.DisableDriverAsync`: ブートクリティカルなPnPクラス(DiskDrive/SCSIAdapter/System/Computer/Volume)の無効化を`force`なしで拒否
- 全`Process.Start`呼び出しは`ArgumentList`使用(シェル経由のコマンドインジェクション不可)
- `src/AeroDriver.Core/Helpers/WdacHelper.cs`: WDAC/DeviceGuard強制モード検出

### エラー診断
- `src/AeroDriver.Core/Models/DriverInstallResult.cs`: インストール失敗理由を区別するenum
  (AdminRequired/NoDownloadUrl/InsecureDownloadUrl/DownloadFailed/SignatureInvalid/InstallerFailed/Cancelled/UnknownError)
- `DriverService.InstallDriverUpdateWithResultAsync`が本体、既存の`bool`版は薄いラッパー

### 多言語基盤
- `src/AeroDriver.Languages/Services/LanguageService.cs`: リソースベースの文字列取得+カルチャフォールバック
- CLIに接続済み(`AeroDriver.CLI.csproj`が`AeroDriver.Languages`を参照)
- **実際に翻訳済みなのは ja-JP と en-US のみ**(詳細は下記セクション3)

### パフォーマンス最適化
- `PciIdDatabase`: `FrozenDictionary`によるロックフリーO(1)ルックアップ(50,000+ベンダー)
- `DriverService`: `ArrayPool<byte>`によるダウンロードバッファ再利用(LOHフラグメント防止)
- `[LoggerMessage]`ソースジェネレーターによるホットパスのログ最適化
- `SettingsService`: `JsonSerializerContext`ソースジェネレーション(リフレクション不要)

---

## 2. 存在しない機能(過去にREADME等で虚偽記載されていたもの)

以下はすべて修正済みだが、「かつてこう謳われていたが実装がなかった」記録として残す。
将来同じ機能を「復活」させる際に、ゼロから設計する必要があることの参考情報。

| 機能 | 旧README記載 | 実態 |
|------|--------------|------|
| GUI | スクリーンショット3枚を掲載 | `src/AeroDriver.UI`は`.csproj`のみでコード0行 |
| 多言語対応 | 「300以上の言語」 | ja-JP/en-USの2言語のみ翻訳済み |
| テーマ | 「30+テーマ」 | コードベースに一切存在しない |
| FontService | アーキテクチャ図に記載 | 存在しない |
| NotificationService | アーキテクチャ図に記載 | 存在しない |
| QRCoder依存 | 必須パッケージとして記載 | どの.csprojにも存在しない |
| CI | ビルドステータスバッジ | `.github/workflows/`が存在しなかった |
| MITライセンス | バッジ+本文で明言 | `LICENSE`ファイルが存在しなかった(今回追加) |
| BTC寄付セクション | 実在するBTCアドレスを掲載 | 「絶対お金をかけない」方針と矛盾していたため、ユーザー承認を得て削除済み |

---

## 3. 死んでいるAPI(宣言のみで未使用、読み書きゼロ)

`src/AeroDriver.Core/Models/DriverInfo.cs` の `DriverDetailInfo` クラス(および基底の`DriverInfo`)に
以下のプロパティが存在するが、**セッション中盤時点では代入も参照も一度も行われていなかった**:

- ~~`IsGraphicsDriver`~~ → **解消済み**。`DriverService.MapCimInstance`/`GetDriverDetailsAsync`で
  `DeviceClass == "DISPLAY"`から算出するよう実装し、CLIの`scan`出力で`[GPU]`タグとして表示するようにした
- ~~`Properties`~~ → **解消済み**。`GetDriverDetailsAsync`が`Win32_PnPSignedDriver`から
  取得する全`CimInstanceProperties`をそのまま格納するようにし、新設したCLIの
  `details --device-id <id>`コマンドで実際に表示するようにした
- ~~`DriverPath`/`DriverSize`/`InfContent`/`CertificateInfo`~~ → **解消済み**。
  `Win32_PnPSignedDriver.DriverName`が実体ファイル(.sys等)へのフルパスを返すことを利用し、
  `GetDriverDetailsAsync`内の`PopulateFileDerivedInfo`で
  `DriverPath`(そのパス)・`DriverSize`(`FileInfo.Length`)・
  `CertificateInfo`(新設した`AuthenticodeHelper.GetCertificateInfo`でAuthenticode署名の
  発行者/サブジェクト/有効期間/信頼チェーン検証結果を取得)・
  `InfContent`(同ディレクトリの`InfName`ファイルが実在すれば本文を読み取り)を埋めるようにした。
  ファイルアクセス系の例外はベストエフォート項目の欠落として握りつぶし、詳細取得全体は失敗させない。

~~`src/AeroDriver.Languages/Resources/` 配下の8言語の`.resx`が空~~ → **解消済み**。
de-DE/es-ES/fr-FR/it-IT/ko-KR/pt-BR/ru-RU/zh-CN の8言語すべてに en-US と同じ18キーの
翻訳を追加し、`LanguageService.SupportedCultures`を10言語に復元した。
(補足: 空だった8ファイルはresheaderのアセンブリ名まで`...`と壊れていたため全面書き直した)

### セキュリティ系ヘルパーのテストカバレッジ状況

| ヘルパー | テスト | 備考 |
|---------|--------|------|
| `WqlSanitizer` | ✅ `WqlSanitizerTests.cs` | 純粋・静的なので完全にテスト可能 |
| `ElevationGuard` | ✅ `ElevationGuardTests.cs` | 非Windows側のバイパス経路のみ検証(Windows側は環境依存のため未検証) |
| `WdacHelper` | ✅ `WdacHelperTests.cs` | 非Windows環境での`WdacStatus.Disabled`フォールバックのみ検証。実際のCIポリシー読み取りはWindows実機でしか検証不可 |
| `AuthenticodeHelper` | 🟡 `AuthenticodeHelperTests.cs`(部分) | フェイルクローズ経路(ファイル不在/不正形式)のみ検証。「実際に有効な署名を持つ」正常系は実署名バイナリが必要でテスト環境に用意できないため未検証。検証本体を`WinVerifyTrust`(wintrust.dll、非Windowsでは`OperatingSystem.IsWindows()`チェックで即false)に置き換えたが、フェイルクローズ経路のテストは経路が変わっただけで結果は変わらないため既存テストのまま通る |

---

## 4. 修正済みバグ一覧(宣言と実装の不一致が原因だったもの)

| バグ | 該当ファイル | 内容 |
|------|------------|------|
| `SetDriverState`成功判定 | `DriverService.cs` | `InvokeMethod != null`のみ確認、実際の`ReturnValue`(成功/失敗コード)未検証 |
| `StatusInfo`常時0 | `DriverService.cs` | 誤ったWMIクラス(`Win32_PnPSignedDriver`)から`ConfigManagerErrorCode`取得を試みており、常にnull→常に不明のまま。`Win32_PnPEntity`への別クエリで修正 |
| キャンセル伝播不統一 | `DriverService.cs` | Rollback/Enable/Disable/InstallCustomDriver/QuerySourceAsync(更新元への並列問い合わせ)が`OperationCanceledException`を握りつぶし`false`や空配列を返していた。全`catch (Exception)`を横断的に洗い出し、Install/GetDriverDetailsと同じ再スローパターンに統一 |
| null注釈の虚偽 | `WhqlDatabaseService.cs` | `Task<DriverInfo>`(非null)と宣言しつつ4箇所でnull返却。インターフェースの`Task<DriverInfo?>`と不一致 |
| キャッシュ破損時のNRE | `WhqlDatabaseService.cs` | `JsonConvert.DeserializeObject`のnull戻り値を未チェックで`.CacheTime`にアクセス |
| 幽霊プロジェクト参照 | `AeroDriver.sln` | `AeroDriver.UI.Tests`がディスク上に存在せず、`dotnet build`が確実に失敗する状態だった |
| WQLインジェクション | `DriverService.cs` | 手動`Replace('\\'/'\'')`のみだったものをアローリスト検証(`WqlSanitizer`)に強化 |
| HttpClientソケット枯渇 | `WhqlDatabaseService.cs` | `new HttpClient()`直接生成→`IHttpClientFactory`経由に変更 |
| 署名検証の例外漏れ | `AuthenticodeHelper.cs` | `CryptographicException`のみ捕捉していたが、ファイル不在/権限不足/非Windows環境では`FileNotFoundException`/`IOException`/`PlatformNotSupportedException`等も投げられうる。フェイルクローズ(いずれもfalse)するよう捕捉範囲を拡大 |
| シェルインジェクション | `PnpUtilDriverSource.cs`, `DriverService.cs` | 文字列結合の引数→`ProcessStartInfo.ArgumentList`に変更 |
| コンパイル不能コード | `AeroDriver.Core/Program.cs`(削除済み) | `DriverInfo`/`Task`のusing不足で存在自体がビルドを壊していた重複ファイル |
| **パストラバーサル(2件)** | `BackupService.cs` | ①`GetDeviceDirectory`が`Path.GetInvalidFileNameChars()`(`.`を含まない)でのみサニタイズしており、`DeviceID=".."`が素通りしてバックアップルート外を指せた。CLIの`rollback --device-id ..`から到達可能。②`RestoreDriverAsync`の`backupVersion`も同種: `"backup_"`プレフィックスは先頭セグメントの単独`".."`は防ぐが、内部に埋め込まれた`"../"`(例: `"../../../../Windows/System32"`)は防げず、`deviceDir`の外を指せた。両方とも`Path.GetFullPath`で正規化しルート配下かを検証する多層防御を追加 |
| コンソール文字化け | `AeroDriver.CLI/Program.cs` | 10言語対応を追加したのに`Console.OutputEncoding`未設定のままで、Windows既定コードページでは中国語/韓国語/ロシア語等が文字化けしていた。起動時にUTF-8へ明示的に切り替え |
| nullable注釈の虚偽(2件目) | `IBackupService.cs`, `DriverUpdateEvents.cs` | `RestoreDriverAsync(..., string backupVersion = null)`と`UpdatesInstalledEventArgs.ErrorMessage`が非null宣言のままnullを扱っていた。`string?`に修正し、`InstalledDriver`にも欠けていたnullガードを追加 |
| **設定が反映されない** | `BackupService.cs` | `ISettingsService.MaxBackupGenerations`をユーザーが変更しても、`BackupService`はハードコードされた`DefaultMaxGenerations = 3`を常に使っており一切反映されなかった。`BackupService`に`ISettingsService`を注入し実際の設定値を参照するよう修正 |
| 誤ったWMIプロパティ名 | `DriverService.cs` | `GetDriverDetailsAsync`が`ClassGuid`ではなく存在しない`DeviceClassGUID`を読んでおり`ClassGuid`が常にnullだった |
| `SignatureInvalid`が到達不能 | `DriverService.cs` | `InstallFromFileAsync`が`bool`を返しており、Authenticode検証失敗は汎用の`InstallerFailed`に潰されていた。CLIに専用メッセージがあるのに一度も表示されない状態。`DriverInstallResult`を返すよう変更 |
| バージョン比較が辞書順 | `DriverService.CheckForUpdatesAsync`, `WhqlDatabaseService.FindDriverByHardwareIdAsync` | 同一HardwareIDの重複排除/カタログ検索の「最新」選定が`StringComparer.OrdinalIgnoreCase`(文字列辞書順)で行われており、`"10.2.0"`が`"9.5.1"`に負けるなど数値として誤った順序になっていた。`VersionHelper.Compare`使用に統一 |
| CimInstance未破棄 | `DriverService.cs`(複数箇所) | `CimSession.QueryInstances`が返す`CimInstance`が`using`されておらずネイティブMIハンドルがリークしていた(`WdacHelper.GetStatus`で先に修正した同種バグの横展開) |
| プロセス出力パイプデッドロック | `BackupService.cs`(`ExportDriverFilesAsync`, `ReinstallDriverFileAsync`) | 標準出力・標準エラーの両方をリダイレクトしながら片方しか読まずに`WaitForExitAsync`を待っており、出力がOSパイプバッファを超えると子プロセスがブロックしデッドロックしうる状態だった。両ストリームを並行読み取りしてから待機するよう修正 |
| INFファイル名のパストラバーサル | `DriverService.PopulateFileDerivedInfo` | WMI由来の`InfName`をそのまま`Path.Combine`していたため`".."`を含む値でドライバーディレクトリ外のファイルを`InfContent`に読み込めた(不正な文字によるクラッシュ防止とは別問題)。`Path.GetFileName`でファイル名部分のみ抽出するよう修正 |
| 簡易署名検証 | `AuthenticodeHelper.cs` | `X509Certificate2.CreateFromSignedFile`+`X509Chain.Build`だけでは、ファイルの証明書テーブルから証明書を抜き出してチェーン検証するだけで、PKCS#7署名が実際に現在のファイルバイト列を対象にしているかを確認できていなかった。ネイティブ`WinVerifyTrust`(wintrust.dll)による本来のAuthenticode検証に置き換え |
| ログの引数取り違え | `LanguageService.cs` | 非対応カルチャからのフォールバック時、`_currentCulture`をen-USに再代入した*後*に`_currentCulture.Name`をログに使っており、「非対応だったカルチャ」欄が常に`en-US`と表示され診断不能になっていた |

---

## 5. 未解決事項(人間の判断待ち)

### CIワークフロー未反映
`.github/workflows/build.yml`を作成したが、このセッションのGitHub Appトークンには
`workflows`権限がなくpushできなかった(複数セッションで再確認済み。過去に一度は
コミットされたがpush時にリジェクトされ、README のBuild Statusバッジだけが
ワークフロー不在のまま残り続けて「常にno status」で表示される状態になっていたため、
バッジ自体をREADMEから削除して整合を取った)。以下の内容を手動で追加する必要がある:

```yaml
name: Build

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    # AeroDriver.UI targets net8.0-windows (WPF) and AeroDriver.Core uses
    # Windows-only APIs (CimSession, pnputil, WDAC) — windows-latest is
    # required for the whole solution to build and test correctly.
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore AeroDriver.sln

      - name: Build
        run: dotnet build AeroDriver.sln --no-restore --configuration Release

      - name: Test
        run: dotnet test tests/AeroDriver.Core.Tests/AeroDriver.Core.Tests.csproj --no-build --configuration Release
```

### AeroDriver.UI の扱い
空のWPFプロジェクト。依存関係は`CommunityToolkit.Mvvm`に最新化済みだが、実装は未着手。
「フルGUIを実装する」か「正式に削除する」かは規模の大きい意思決定のため保留中。

---

## 6. 設計上の制約(変更してはいけないこと)

- **絶対に課金要素を追加しない**: 有料API、有料SaaS依存、テレメトリ送信は禁止。全データソース・
  ツールはWindows標準またはOSS/無料でなければならない
- **Windows標準API優先**: `CimSession`(WMI)、`pnputil.exe`、Windows Update Agent COM — 
  サードパーティ商用ライブラリより優先する
- **`OperationCanceledException`は再スローする**: `catch (Exception)`で握りつぶさず、
  呼び出し元に伝播させる(セクション4のバグ参照)
- **`ConfigureAwait(false)`をライブラリコード全体で使用**
- **`ArgumentList`を使う**: プロセス起動時に文字列結合で引数を組み立てない(シェルインジェクション対策)
- **宣言と実装を一致させる**: nullable注釈、XMLドキュメントコメント、README等の対外的な
  宣言は、実装が実際にその通り動くことを検証してから書く。これがこのセッションで最も
  繰り返し破られていたルールであり、最優先で守ること

---

## 7. 外部環境の事実(2026-07時点のWeb調査で確認)

依存関係の更新やAPI移行を検討する前に必ずここを読むこと。
「新しいAPIに移行すべき」という直感が誤りであるケースが複数ある。

- **System.CommandLine は 2.0.0-beta4 に固定**: beta5 は `SetHandler`→`SetAction` 等の
  破壊的変更を含み、GA(安定版)にも未達。GA後に別作業として移行する。
  (https://github.com/dotnet/command-line-api/releases)
- **`SYSLIB0057` の pragma 抑制は公式推奨パターン**: 後継の `X509CertificateLoader` は
  Authenticode 署名からの証明書抽出に**対応していない**ため、
  `X509Certificate2.CreateFromSignedFile` の継続利用が Microsoft の案内どおり。
  pragma を「負債」と見なして移行してはいけない。
  (https://learn.microsoft.com/dotnet/fundamentals/syslib-diagnostics/syslib0057)
- **`Win32_PnPSignedDriver` はレガシーだが非推奨化されていない**: 現行の `CimSession`
  経由での利用に問題なし。将来移行するなら CfgMgr32 API + DEVPKEY だが、現に動作して
  おり移行の理由がない。
- **WUA COM API は継続サポート**: Windows Update Catalog の無料公式 REST API は存在
  しないため、現行の WUA COM + Catalog スクレイピングの組み合わせが正しいアプローチ。
- **2026年4月: クロス署名ドライバーの信頼廃止**(Windows 11 24H2/25H2/26H1, Server 2025):
  WHCP署名または明示的許可リストのみがロード可能になる。`InstallDriverUpdateWithResultAsync`
  はWHQL非認定ドライバーに対しこの旨を警告ログで通知する。
  (https://techcommunity.microsoft.com/blog/windows-itpro-blog/)
- **CVE-2025-59033 / BYOVD対策**: Microsoft の脆弱ドライバーブロックリストは HVCI 有効時
  しか強制されず更新も遅い。このため `VulnerableDriverBlocklist`(LOLDrivers の無料JSON、
  https://www.loldrivers.io/api/drivers.json)によるインストーラー側の SHA256 照合層を追加済み。
  照合はフェイルオープン(リスト取得不可時は警告ログを出してスキップ)であり、
  Authenticode 検証(フェイルクローズ)の代替ではなく追加層である点に注意。
