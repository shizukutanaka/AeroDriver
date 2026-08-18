# core-typecheck — Core 全体を WMI スタブに対して型検査する

## これは何か

`AeroDriver.Core` の**全C#ファイル**を、`Microsoft.Management.Infrastructure`(WMI)と
`Microsoft.Extensions.Http.Resilience` の**最小スタブ**に対して実コンパイルする。

`tools/offline-verify` は BCL/ILogger のみに依存するファイルを実行まで検証するが、
WMI に依存する `DriverService`(Core 最大のファイル)と `WdacHelper` は対象外だった。
このハーネスはその穴を埋める。

```bash
cd tools/core-typecheck
dotnet build
```

## 何を検証できるか

- Core 全ファイルの型整合(`DriverService` を含む)
- WMI API の**使い方の型的な誤り**。実際にこれで
  `CimMethodResult.ReturnValue` を `object` と誤認したコードを検出した
  (実際の型は `CimMethodParameter` で、`is uint` パターンは CS8121 になる)

## 何を検証**できない**か

- **実際の WMI 動作**。スタブは常に空を返すので、クエリの綴りや WMI クラス名の誤り、
  プロパティ名の間違いは検出できない
- スタブのシグネチャが本物と一致している保証。**ここが一致していないと検証自体が嘘になる**ため、
  API の形を変えるときは公式リファレンスで裏を取ること
  (`CimMethodResult.ReturnValue` の型もそうやって確認した)

Windows 実機での `dotnet build AeroDriver.sln` は引き続き必要。
