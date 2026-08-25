# lang-run — ローカライズ基盤を「実行」して検証する

## これは何か

`.resx` のコンパイル → サテライトアセンブリ生成 → `ResourceManager` による解決と
フォールバックまでを実際に走らせるハーネス。`tools/offline-verify` と同じ `Check()` 方式。

```bash
cd tools/lang-run
dotnet run
```

## なぜ必要か

`AeroDriver.Languages` は `AeroDriver.Core` を `ProjectReference` しており、Core の NuGet が
restore できないためこの環境では**プロジェクトとしてビルドできない**。その結果、
10言語対応の中核が一度も検証されていなかった。

resx のコンパイルとサテライト生成は **.NET SDK 自体の機能で NuGet を必要としない**。
resx と `LanguageService.cs` だけを同条件(同じ `RootNamespace` / `AssemblyName` /
`NeutralLanguage`、`Resources/` 配下へのリンク)で切り出せば、実際に動かせる。

このハーネスを作ったことで **`AeroDriver.Languages` がそもそもコンパイルできない**
という欠陥が見つかった(`using AeroDriver.Languages.Resources;` が、コード上に存在しない
名前空間を参照していた。`ResourceManager` はベース名を文字列で受けるので、この using は
未使用でもあった)。

## 何を検証できるか

- 全60キー × 10言語がすべて解決できる(`GetString` が `"[キー名]"` を返さない)
- 翻訳が実際に言語ごとに異なる(サテライトが解決されず中立に落ちていないこと)
- **中立リソースへのフォールバック** — `es-MX` / `en-GB` / `pt-PT` / `zh-TW` のように
  サテライトを持たないカルチャでも `"[キー名]"` ではなく実際の英語が返ること。
  `Strings.resx` を中立リソースにした目的そのもの
- 未対応カルチャで起動したとき `en-US` にフォールバックすること
- `SetCulture` の反映と、未対応カルチャ指定時の `en-US` への切り戻し

中立リソースを culture 付きに戻す / ある言語のキーを1つ削る の両方で失敗を検出できることを
確認済み。

## 何を検証**できない**か

- **本体プロジェクトとしてのビルド**。`AeroDriver.Languages.csproj` 自体は Core の NuGet が
  必要なため、この環境では依然としてビルドできない。ここで検証しているのは
  「resx と `LanguageService` の組み合わせ」であって csproj の設定全体ではない
- **publish 後の成果物**。`InvariantGlobalization` やトリミングの影響は
  `tools/check-packages.py` が静的に検査している
- GUI/CLI から見た表示(それぞれ `ui-run` / `cli-typecheck` の担当)
