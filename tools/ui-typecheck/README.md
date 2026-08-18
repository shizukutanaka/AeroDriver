# ui-typecheck — WPF層のC#を Linux で型検査する

## これは何か

`AeroDriver.UI` の**手書きC#コード**(`MainViewModel.cs` / `App.xaml.cs` / `MainWindow.xaml.cs`)を、
WPF と CommunityToolkit.Mvvm の**最小スタブ**に対して実コンパイルするハーネス。

`AeroDriver.UI` 本体は `net8.0-windows` かつ NuGet(`CommunityToolkit.Mvvm`)が必要なため、
この環境ではビルドできない。その結果 GUI 層は長らく**一度もコンパイルされていない**状態だった
(Core 側では実コンパイルによってビルド不能な欠陥が5件見つかっており、GUI に同種の欠陥が
残っている可能性が高かった)。

```bash
cd tools/ui-typecheck
dotnet build
```

## 何を検証できるか

- 型エラー・メソッドシグネチャの不一致・存在しないメンバーへの参照
- `IDriverService` / `ISettingsService` / `ILanguageService` など**インターフェース側の変更に
  ViewModel が追従できているか**(引数追加やメソッド削除の取りこぼし検出)
- `using` の不足、null 許容注釈の不整合

`Generated.cs` には、CommunityToolkit のソースジェネレーターが生成する**はず**のメンバー
(`[ObservableProperty]` → プロパティ、`[RelayCommand]` → コマンド)を手書きで再現してある。
つまり「手書きコードがジェネレーターの契約と整合するか」を検査している。

## 何を検証**できない**か(重要)

- **XAML のコンパイル**(`MainWindow.xaml` / `App.xaml`)。WPF の XAML コンパイラは Windows 専用
- **ソースジェネレーターの実際の出力**。`Generated.cs` は規約に基づく再現であって本物ではない
- 実行時の振る舞い(バインディング解決、Dispatcher、テーマ辞書の差し替え)

したがって **Windows 実機での `dotnet build AeroDriver.sln` は依然として必要**。
このハーネスは「実機ビルドで出るはずのエラーのうち、型に起因するものを前倒しで潰す」ためのもの。

## スタブを更新すべきとき

- ViewModel に `[ObservableProperty]` / `[RelayCommand]` を足したら `Generated.cs` にも追加する
  (追加し忘れると、そのメンバーを参照する箇所でコンパイルエラーになるので気付ける)
- WPF の新しい型を使い始めたら `Stubs.cs` に最小定義を足す

## XAML との整合性チェック

XAML 自体はコンパイルできないが、束縛名の一致は機械的に確認できる:

```bash
# {Binding XxxCommand} ⇔ [RelayCommand] メソッド名、{Binding XxxText} ⇔ プロパティ
grep -oP 'Command="\{Binding \K[A-Za-z]+(?=Command\})' ../../src/AeroDriver.UI/MainWindow.xaml
```
