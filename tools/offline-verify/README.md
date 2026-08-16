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

## カバー範囲

`verify.csproj` の `<Compile Include>` に列挙したファイルのみ:

- `Helpers/HardwareIdParser.cs` — PCI/USB ハードウェアID解析(複合USBの `&MI_xx` 含む)
- `Helpers/InstallerExitCode.cs` — 3010/1641/259 等の終了コード解釈
- `Helpers/VersionHelper.cs` — 数値としてのバージョン比較
- `Helpers/DriverInstallOrder.cs` — インストール順序(チップセット→…→GPU)
- `Helpers/WqlSanitizer.cs`
- `Models/` の POCO 3件

**カバーしないもの**: `Microsoft.Extensions.*` や WMI/COM に依存するサービス層、
WPF(`net8.0-windows`)。これらは引き続き Windows 実機での
`dotnet build AeroDriver.sln && dotnet test` が必要。

## 純粋ロジックを追加したら

`verify.csproj` に `<Compile Include>` を1行足し、`Program.cs` にアサーションを
追加すること。BCL以外の `using` があるファイルはここには入れられない。
