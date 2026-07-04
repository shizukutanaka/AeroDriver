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

### ドライバー検出・更新 (`src/AeroDriver.Core/Services/DriverService.cs`)
- `CimSession`(現行WMI API)によるドライバー列挙。`ManagementObjectSearcher`は完全廃止済み
- `IAsyncEnumerable<DriverInfo> StreamAllDriversAsync`: `BoundedChannel(256)+Wait`でバックプレッシャー制御
- `SemaphoreSlim(1,1)`による非同期セーフなキャッシュ(TTL 30秒)
- 更新ソース: `PnpUtilDriverSource`(pnputil.exe)、`WindowsUpdateAgentSource`(WUA COM)、`WhqlDatabaseService`(Windows Update Catalog)

### バックアップ/復元 (`src/AeroDriver.Core/Services/BackupService.cs`)
- `pnputil /export-driver`で実際のドライバーファイル一式(INF+SYS+関連ファイル)をコピー
- `pnputil /add-driver /install`で実際に再インストールして復元
- 3世代自動ローテーション

### セキュリティ多層防御
- `src/AeroDriver.Core/Helpers/ElevationGuard.cs`: 管理者権限チェック(非Windowsではバイパス)
- `src/AeroDriver.Core/Helpers/WqlSanitizer.cs`: WQLインジェクション対策(アローリスト+エスケープ)
- `src/AeroDriver.Core/Helpers/AuthenticodeHelper.cs`: EXE/MSIのAuthenticode署名検証必須化
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

---

## 3. 死んでいるAPI(宣言のみで未使用、読み書きゼロ)

`src/AeroDriver.Core/Models/DriverInfo.cs` の `DriverDetailInfo` クラス(および基底の`DriverInfo`)に
以下のプロパティが存在するが、**セッション中盤時点では代入も参照も一度も行われていなかった**:

- ~~`IsGraphicsDriver`~~ → **解消済み**。`DriverService.MapCimInstance`/`GetDriverDetailsAsync`で
  `DeviceClass == "DISPLAY"`から算出するよう実装し、CLIの`scan`出力で`[GPU]`タグとして表示するようにした
- `DriverPath`
- `DriverSize`
- `InfContent`
- `Properties`
- `CertificateInfo`

上記5件は削除していない。将来GUIを実装する際に使う想定の先行宣言と思われるため、実装時に
中身を埋めるか、不要と判断されれば削除すること。

`src/AeroDriver.Languages/Resources/` 配下の8言語(`de-DE`/`es-ES`/`fr-FR`/`it-IT`/`ko-KR`/
`pt-BR`/`ru-RU`/`zh-CN`)の`.resx`は、XMLスキーマの骨格のみで`<data>`要素が0件。
`LanguageService.SupportedCultures`からは意図的に除外済み(空言語への切り替えで
`[KeyName]`がユーザーに表示されるのを防ぐため)。

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
| シェルインジェクション | `PnpUtilDriverSource.cs`, `DriverService.cs` | 文字列結合の引数→`ProcessStartInfo.ArgumentList`に変更 |
| コンパイル不能コード | `AeroDriver.Core/Program.cs`(削除済み) | `DriverInfo`/`Task`のusing不足で存在自体がビルドを壊していた重複ファイル |

---

## 5. 未解決事項(人間の判断待ち)

### README.mdのBTC寄付セクション
「フリーソフトなので絶対お金をかけない」という開発方針と、実在するBTCアドレスを掲載した
寄付募集セクションが矛盾している。これは技術的な誤りではなく事業判断のため、AIが無断で
削除せず現状維持。**ユーザーの意思決定が必要**。

### CIワークフロー未反映
`.github/workflows/build.yml`を作成したが、このセッションのGitHub Appトークンには
`workflows`権限がなくpushできなかった。以下の内容を手動で追加する必要がある:

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
