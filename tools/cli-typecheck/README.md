# cli-typecheck — CLI の C# を NuGet なしで型検査する

## これは何か

`AeroDriver.CLI/Program.cs` を、`System.CommandLine`(2.0.0-beta4)の**最小スタブ**に対して
実コンパイルするハーネス。`System.CommandLine` は NuGet がプロキシ遮断のため restore できず、
CLI のハンドラーロジックは長らく未コンパイルだった。

```bash
cd tools/cli-typecheck
dotnet build
```

## 何を検証できるか

- ハンドラーの型エラー・引数の不一致(`SetHandler` のアリティとオプションの型が合っているか)
- `IDriverService` / `IInstallHistoryService` / `ILanguageService` の変更に CLI が追従できているか
- `using` 不足、null 許容注釈の不整合

とくに `SetHandler` は**オプションの型と引数の型が一致しないとコンパイルが通らない**ため、
`--version`(string?)や `--limit`(int)を足したときの取り違えをここで検出できる。

## 何を検証**できない**か

- `System.CommandLine` の**実際のパース挙動**(スタブは形だけで解析しない)。
  オプション名の綴りやコマンドの登録漏れは実行しないと分からない
- beta4 の API が本当にこの形かは、実機ビルドで最終確認が必要

## スタブを更新すべきとき

`System.CommandLine` の新しい API を使い始めたら `Stubs.cs` に最小定義を足す。
足りなければコンパイルエラーになるので気付ける。
