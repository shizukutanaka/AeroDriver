# tests-run — xunit テストスイートを xunit 無しで実行する

## これは何か

`tests/AeroDriver.Core.Tests` の **187 ケースを実際に実行する**ハーネス。

```bash
cd tools/tests-run
dotnet run
```

## なぜ必要か

NuGet が遮断されているため xunit は restore できず、テストスイートは
**一度も実行されていなかった**。`tools/tests-typecheck` が型検査はしていたが、
表明は何も評価せず自分自身を返すスタブだったので「テストが通るか」は
まったく分からない状態だった。

**xunit はテストを走らせる手段であって目的ではない。** `MainViewModel` に対して
同じ論法で `tools/ui-run` を作ったのと同様に、ここでは表明を本物にし、
NSubstitute のモックを BCL の `DispatchProxy` で再現して、テストを実行する。

- `TestRuntime.cs` — 動く xunit / FluentAssertions / NSubstitute。
  表明は満たされなければ `AssertionFailedException` を投げる**本物**
- `Program.cs` — `[Fact]`/`[Theory]` をリフレクションで探し、
  コンストラクター(セットアップ)と `IAsyncLifetime` を尊重して各ケースを実行する

## 最初の実行で見つかったもの

一度も走らせていなかったスイートを走らせたら、**11件が失敗した**。
うちハーネス側のバグは1件だけで、残り10件は実在の欠陥だった:

| 件数 | 内容 |
|---|---|
| 5 | `var info = _sut.MapToDriverInfo(update)` — 引数が `dynamic` なので呼び出しが動的束縛になり `info` も `dynamic` になる。拡張メソッドは `dynamic` に実行時束縛されないため `.Should()` が `RuntimeBinderException` で落ちる。**Windows の `dotnet test` でも同じく落ちる** |
| 3 | バックアップ世代名が秒精度で、同一秒の2回目が1回目を**黙って上書き**していた。`MaxBackupGenerations` も `GetAvailableBackups` も1件しか見えず、世代管理が機能していなかった |
| 2 | `BackupDriverAsync` / `RestoreDriverAsync` の `catch (Exception)` がパストラバーサル検出の `ArgumentException` を握りつぶして `false` を返していた。同期版の `HasBackup` は伝播しており**挙動が食い違っていた** |
| 1 | イベント発火の表明がコメントと矛盾していた(実装は失敗時も発火するのが契約) |

## 何を検証**できない**か(重要)

- **実 WMI / 実 pnputil / 実 WUA COM**。`CimSession` はスタブなので、
  それらに依存するテストは非Windows経路(グレースフルデグラデーション)を通る
- **プラットフォーム差**。`Path.GetInvalidFileNameChars()` は Windows と Linux で
  中身が違うため、パス正規化の挙動は完全には一致しない
- **xunit 本体の機能**。`[Collection]`、`ClassFixture`、並列実行、`ITestOutputHelper` は
  未実装(このスイートが使っていないため)
- **NSubstitute の高度な機能**。`When()`/`Do()`/`Arg.Is(述語)` は未実装
- **表明ラムダ内の型検査**。述語は `Func<dynamic, bool>` で受けている
  (`tests-typecheck` と同じ理由: オーバーロード解決で要素型を推論できない)

したがって Windows 実機での `dotnet test`(= `tools/verify-windows.ps1`)は依然として
必要。このハーネスは「実機で出るはずの失敗を前倒しで潰す」ためのもの。

## ランタイムを更新すべきとき

テストが新しい表明メソッドや NSubstitute の API を使い始めたら `TestRuntime.cs` に足す
(足さないとコンパイルエラーになるので気付ける)。**表明を追加するときは、
必ず「満たされないときに投げる」ことを確認すること** — 何もしない表明は
テストを緑にするだけで何も検証しない。
