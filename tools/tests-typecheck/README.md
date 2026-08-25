# tests-typecheck — テストコードを Core の現在の API と突き合わせる

## これは何か

`tests/AeroDriver.Core.Tests`(2,186行)を **xunit / FluentAssertions / NSubstitute の
最小スタブ**に対して実コンパイルするハーネス。

```bash
cd tools/tests-typecheck
dotnet build
```

## なぜ必要か

テストプロジェクトは NuGet(xunit 他)が restore できないため、この環境では
**一度もコンパイルされていなかった**。Core 側は実コンパイルによってビルド不能な欠陥が
複数見つかっており、テストコードが Core の API 変更に追従できているかは
まったく分からない状態だった。特にこのセッションでは Core から複数の API を
削除しているため、取り残しがあれば Windows 実機で初めて発覚することになる。

## 何を検証できるか

- テストが参照する Core の型・メソッド・シグネチャが**実在するか**
- 削除・改名した API を参照したまま残っているテストの検出
- `using` の不足、名前空間の不整合

`VersionHelper.Compare` を改名すると 14 件のエラーとして検出されることを確認済み。

## 何を検証**できない**か(重要)

- **テストの実行**。表明(`Should().Be(...)`)は中身を評価せず自身を返すだけ。
  「テストが通るか」はまったく分からない
- **表明ラムダの中身の型検査**。`Contain(m => m.Contains("..."))` のような述語は
  `Func<dynamic, bool>` として受けている。C# のオーバーロード解決では
  `Should<T>(this T)` が `Should<TItem>(this IEnumerable<TItem>)` より優先され、
  要素型を推論させられないため。ラムダの外側は通常どおり型検査される
- **NSubstitute の実挙動**。`Substitute.For<T>()` は `null!` を返すだけ

したがって Windows 実機での `dotnet test`(= `tools/verify-windows.ps1`)は依然として必要。
このハーネスは「実機で出るはずのエラーのうち、型に起因するものを前倒しで潰す」ためのもの。

## スタブを更新すべきとき

テストが新しい表明メソッドや NSubstitute の API を使い始めたら `TestStubs.cs` に足す
(足さないとコンパイルエラーになるので気付ける)。
