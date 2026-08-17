# offline-verify — NuGet が使えない環境での実コンパイル+実行検証

## これは何か

`AeroDriver.Core` のうち**外部パッケージに依存しない純粋ロジック**だけを集めて
コンパイルし、実際に走らせて振る舞いを確認するための小さなハーネス。

このリポジトリは長らく「ビルド未検証(静的検証のみ)」の状態で開発されてきた。
NuGet(api.nuget.org)に到達できない環境でも、**BCLのみに依存するファイルは
本物のコンパイラで検証できる**。Python等で正規表現の「ミラー」を書いて検証する
より遥かに強い保証が得られる(ミラーと実装がずれていたら意味がないため)。

## 使い方

```bash
cd tools/offline-verify
dotnet run
```

全アサーションが通れば終了コード0、1件でも落ちれば1を返す。

`nuget.config` で `<clear />` してパッケージソースを空にしてあるため、
ネットワークに出ずに restore が完了する(`PackageReference` はゼロ)。

`Microsoft.Extensions.*`(ILogger 等)は **ASP.NET Core 共有フレームワークに同梱**されている。
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` で参照しているため、
NuGet に到達できなくてもロギング依存のサービスまで検証できる。

## カバー範囲

`verify.csproj` の `<Compile Include>` に列挙したファイルのみ:

- `Helpers/HardwareIdParser.cs` — PCI/USB ハードウェアID解析(複合USBの `&MI_xx` 含む)
- `Helpers/InstallerExitCode.cs` — 3010/1641/259 等の終了コード解釈
- `Helpers/VersionHelper.cs` — 数値としてのバージョン比較
- `Helpers/DriverInstallOrder.cs` — インストール順序(チップセット→…→GPU)
- `Helpers/WqlSanitizer.cs` — WQLインジェクションのアローリストを攻撃文字列で実検証
- `Models/` の POCO 3件
- `Services/InstallHistoryService.cs` — JSONL追記、破損行のスキップ(壊れた行を実際に混ぜて検証)
- `Services/SettingsService.cs` — 設定の永続化と既定値
- `Services/VulnerableDriverBlocklist.cs` — コンパイルのみ(HTTP は叩かない)
- `Helpers/AuthenticodeHelper.cs` — 検証失敗理由の説明、非Windowsでのフェイルクローズ
- `Helpers/SystemRestoreHelper.cs` — 非Windowsでの no-op
- `Helpers/ElevationGuard.cs`
- `Services/PciIdDatabase.cs` / `WhqlDatabaseService.cs` — コンパイルのみ(HTTP は叩かない)
- `Services/PnpUtilDriverSource.cs` — `/enum-drivers` 出力のパースを実データで検証
- `Services/WindowsUpdateAgentSource.cs` — コンパイルのみ(COM は叩かない)
- `Services/BackupService.cs` — パストラバーサル対策と世代保持を実検証(pnputil 自体は叩かない)

### パストラバーサル検証で確認した不変条件

守るべき性質は「例外を投げること」ではなく **どんな入力でもバックアップルート外に出ないこと**。
区切り文字は `Path.GetInvalidFileNameChars()` で除去されるため `"../escaped"` は
`"..escaped"` という無害な単一名に潰れてルート内に収まる(例外は出ないが安全)。
一方 `".."` は除去対象の文字を含まないため素通りし、正規化後のルート配下チェックで弾かれる。
**両方の経路があるので、「例外が出るか」だけを見るテストは誤判定する**。

**カバーしないもの**: WMI(`Microsoft.Management.Infrastructure`)に依存する `DriverService` と
`WdacHelper`、外部パッケージ依存(`System.CommandLine` の CLI、
`CommunityToolkit.Mvvm` の WPF、xunit のテストプロジェクト)。
これらは引き続き Windows 実機での `dotnet build AeroDriver.sln && dotnet test` が必要。

## 純粋ロジックを追加したら

`verify.csproj` に `<Compile Include>` を1行足し、`Program.cs` にアサーションを
追加すること。

入れられる条件: `using` が BCL(`System.*`)・`AeroDriver.*`・`Microsoft.Extensions.*` のみ。
`Microsoft.Management.Infrastructure`(WMI)やその他の NuGet パッケージを使うファイルは
restore できないため入れられない。
