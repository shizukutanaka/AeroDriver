# cli-run — CLI のハンドラーと終了コードを実行して検証する

## これは何か

`AeroDriver.CLI` を実際に動かすハーネス。

```bash
cd tools/cli-run
dotnet run
```

## なぜ必要か

`tools/cli-typecheck` の System.CommandLine スタブは `InvokeAsync` が常に 0 を返し
`SetHandler` が何もしないため、**CLI のハンドラーは一度も実行されていなかった**。
引数検証も終了コードの配線も、型が通るというだけで動作は未確認だった。

CLI は非Windowsで早期に停止する(対応OS判定)。したがって `Main` 経由では
ハンドラーに到達できない。そこで2段構えにしている:

1. `Main` を実際に呼び、**OS ガードが全コマンドを止める**ことを確認する
2. ハンドラーは `private static` なのでリフレクションで直接呼び、引数検証・
   終了コード・出力を検証する(`ui-run` が private ハンドラーへ配線しているのと
   同じ手法。製品側に検証用の口は開けない)

DI は本物(`ConfigureServices` を実際に通す)。設定と履歴は一時 `HOME` に隔離する。

## 最初の実行で見つかったもの

`config --set a=b --set 不正` が「1件でも不正なら何も変更しない」と宣言しながら、
**先行する代入をメモリ上の Singleton に適用済みにしていた**。`TryApply` は検証と
適用が同一操作なので、それを検証に流用したのが原因。設定ファイルは書き換わらないが、
プロセス内の設定は変わってしまう(GUI と CLI が共有する Singleton)。

`SettingsKeys.TryValidate`(適用せずに判定する)を追加し、全件を先に検証してから
適用するようにした。

## 何を検証**できない**か(重要)

- **System.CommandLine そのもののパース挙動**。`CommandLineRuntime.cs` は
  **代役のパーサー**であって本物ではない。ここで検証されるのは CLI 側の
  ハンドラーと終了コードの配線(製品自身のコード)
- **実 WMI / 実 pnputil に触れるハンドラー**(`RunScanAsync` 等)の本体。
  非Windowsでは OS ガードで止まり、直接呼んでも WMI スタブ止まり
- **ローカライズの実解決**。キー名をそのまま返す実装を使う
  (resx の実解決は `tools/lang-run` が担当)

Windows 実機での CLI スモーク(`tools/verify-windows.ps1`)は引き続き必要。
