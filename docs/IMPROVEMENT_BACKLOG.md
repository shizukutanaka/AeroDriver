# AeroDriver 長所・短所・改善バックログ

2026-07時点の棚卸し。短所は**行番号まで確認済みの事実のみ**を記載(憶測なし)。
改善タスクには優先度と推奨モデル([Opus]=複雑/セキュリティ、[Sonnet]=仕様確定済みの作業)を付与。
消化したら該当項目に取り消し線とコミットSHAを追記すること。作業規則は `/CLAUDE.md` 参照。

---

## 長所(維持すべきもの)

| 長所 | 根拠 |
|------|------|
| セキュリティ多層防御 | `WinVerifyTrust`による真正Authenticode検証(`AuthenticodeHelper.cs`)、BYOVDブロックリスト全経路適用(`VulnerableDriverBlocklist.cs`+DriverService×2/CAB展開後/BackupService復元)、WQLサニタイズ(`WqlSanitizer.cs`)、パストラバーサル対策3件、HTTPS強制、TOCTOU対策(ダウンロード〜実行間のFileShare.Readロック)、`ElevationGuard` |
| 引き継ぎ文化 | `docs/FEATURE_AUDIT.md`が実装/修正/未解決を台帳化。「宣言と実装の一致」規律 |
| テスト容易設計 | protectedコンストラクタでキャッシュパス注入(VulnerableDriverBlocklist/BackupService)、純粋関数化(`DriverInstallOrder`/`VersionHelper`/`WqlSanitizer`)、GUIも`IFileDialogService`/`IThemeService`で抽象化 |
| 性能配慮 | `FrozenDictionary/Set`、`ArrayPool`、`[LoggerMessage]`、JSONソースジェネレーション、BoundedChannelバックプレッシャー |
| ロジック共有 | CLI/GUIが同一Coreサービスを消費(例: 一括インストール順序は`CheckForUpdatesAsync`1箇所で決まり両UIに反映) |
| ローカライズ | 10言語×60キー。パリティ・未使用キー・ハードコード混入を機械検証済み。GUIは言語即時切替対応 |

---

## ソクラテス問答による検証(2026-08-26)

製品自身の主張に問いを立て、コードに対して実測で答える。清潔だった箇所も記録する
(「調べて問題なし」と「調べていない」を区別するため)。

| 問い | 検証方法 | 答え |
|------|----------|------|
| 「ユーザーが読む散文はすべてリソース経由」は本当か? | 全 UI ソースを日本語リテラルで走査 | **否。** XAML と CLI は機械検証済みで清潔だが、UI 層の **.cs がチェックの盲点**で3件残っていた: キャンセルメッセージ(日本語直書き+エラー扱い)、未処理例外ダイアログのキャプション、ファイル選択ダイアログのタイトル/フィルター名 → 修正し、UI .cs 用のチェックを verify-all.sh に追加 |
| 「Cancel ボタンは効く」と言えるか? | ui-run の検証範囲を精査 | **実行中の操作を実際に中断する経路は一度も実行されていなかった**(CanExecute とダイアログキャンセルのみ)。TCS ゲート付きモックで初実行 — 中断・状態復帰・再実行可能性の8アサーションが全て通った。ロジック自体は正しかった |
| 既存のリソースキーで賄えるか? | 各キーの実文言を確認 | 否。`Install_Cancelled` はインストール固有の文、`Status_Error` はプレースホルダー付きでキャプションに不適。汎用キー5個を10言語に追加 |
| 検証ツール自体は環境に依存しないか? | 素のコンテナ(locale 未設定)で verify-all.sh を実行 | **否。** `LANG` 未設定だと `grep -P` が UTF-8 の境界を誤認し、EM DASH(U+2014)を CJK と誤検出して偽陽性を出す。`LC_ALL=C.UTF-8` をスクリプトに固定 |
| 10言語の翻訳は本当にその言語で書かれているか? | 全値を en-US と比較し、日中韓露は期待する文字体系の有無を走査 | **是。** 一致は各言語1〜4件のみで、`AppName`(固有名詞)や `Version`/`Source`(仏独伊葡で同綴)など正当なものだけ。`check-resources.py` で継続監視する |
| プレースホルダーは全言語で整合しているか? | 65キー×10言語で `{0}` の有無・個数を突き合わせ | **是。** 不整合ゼロ |
| では逆に、プレースホルダーを持つキーは常に引数付きで呼ばれているか? | 呼び出し側を走査 | **否。ここが穴だった。** `Status_Error` と `Driver_Status_UpdateAvailable` の2キーが**13箇所で引数なしで呼ばれ**、`{0}` がリテラルのまま全10言語で画面に出ていた(GUI のタブ見出しは常時「更新があります: {0}」)。パリティ検査はキー名しか見ておらず素通りしていた |
| `catch (Exception)` で握りつぶしている箇所は、本当に可用性層だけか? | ct 付き await を含むメソッドの catch を全走査し、OCE 再スローの有無を確認 | **否。** `PnpUtilDriverSource.RunPnpUtilAsync` が ct 付き await を持ちながら OCE を握りつぶし `string.Empty` を返していた。呼び出し側は空出力を「ドライバー0件」という**正常な結果**として解釈するため、**キャンセルが成功に化けていた**。他3件は ct を受けないヘルパーで誤検出 |
| WQL サニタイズは全ての外部入力経路に適用されているか? | WQL 組み立て4箇所と `WqlSanitizer` 適用箇所を突き合わせ | **是。** 変数を埋め込む2箇所はどちらもサニタイズ済みの `safeId` 経由(`GetStatusInfo` は同一メソッド内で正規化済みの値を受け取る)。残る2箇所は定数クエリ。**問題なし** |
| TOCTOU 対策は全ての「検証してから実行する」経路にあるか? | ダウンロード経路とカスタムインストール経路を比較 | **否。** ダウンロード経路は BYOVD照合〜実行完了まで `FileShare.Read` ロックを保持するが、**カスタムインストール経路には無かった**。この経路が扱うのはユーザーが選んだ任意のパスで Downloads 等の書き込み可能な場所にあり得るため、むしろ危険度は高い。照合通過後に非昇格の攻撃者プロセスが脆弱ドライバーへ差し替えられ、昇格済みの本プロセスがそれをインストールしうる |
| Singleton の可変状態は競合しないか? | `SettingsService` / `InstallHistoryService` / `VulnerableDriverBlocklist` の同期を確認 | **是。** 設定は全プロパティが `lock`、履歴は `SemaphoreSlim` で追記を直列化、ブロックリストは `_loadLock`。`DriverService.Dispose` も健全(`CimSession` は `using`、`HttpClient` はファクトリ管理で正しく Dispose しない)。**問題なし** |
| README が謳う「WHQL未認定なら警告する」は、ユーザーに届いているか? | 警告の出力先を追跡 | **否。** 警告は `_logger.LogWarning` にしか出ておらず、**GUI は `WinExe` でコンソールを持たないためユーザーは一生見られなかった**。一覧の WHQL チェックボックス列は受動的な表示でありインストール時の警告ではない。README の主張が実装されていない典型的な規則6違反 |
| **なぜ機械検証は毎回すり抜けられたのか?**(問い方そのものを疑う) | CLAUDE.md の絶対規則9件を「機械検証があるか」で分類 | **規則4と5だけ検証が無かった。** そこを測ると規則4(`ConfigureAwait(false)`)に **29件の実違反**。Core は WPF の UI スレッドから呼ばれるため、付け忘れは不要なマーシャリングと、呼び出し側が `.Result`/`.Wait()` した場合の**デッドロック**を招く。規則5(`ArgumentList`)は違反ゼロだった。**発見済み欠陥4件はすべて「文書に書かれているが強制されていない主張」だった** — 個別に問うより、未強制の主張を列挙する方が速い |
| README の主張(CLI 8コマンド・GUI 11機能)は実装と一致しているか? | 各コマンド登録と XAML 束縛を機械的に突き合わせ | **是。** CLI 8コマンドすべて登録済み、GUI の11束縛すべて XAML に実在。**問題なし** |
| 逆に、実装されているのに UI から到達できない機能は無いか? | `IDriverService` の全メンバーを UI/CLI の消費者と突き合わせ | **否、3件あった。** `InstallDriverUpdateAsync`(bool版)と `CompareVersions` は**自分自身のテストだけが消費者**、`StreamAllDriversAsync` は「消費者がペースを制御」と謳いながら外部消費者ゼロ。`WhqlDatabaseService` 835行を削除したのと同じ構造 |
| **受け入れ条件そのものは、完成を判定できる内容か?** | `verify-windows.ps1` の検査項目を、これまで発見した欠陥と突き合わせ | **否。3つの穴があった。** (1) `dotnet publish` を一度も実行しておらず、**「配布するとローカライズが死ぬ」欠陥(AD)を受け入れ試験が検出できない**。(2) GUI を起動しない — XAML はビルドを通っても起動時に落ちうる(リソース辞書・DI・コンバーター)。(3) サテライトアセンブリの有無を見ていない。**受け入れ試験が通っても製品が壊れている状態がありえた** |
| **「pwsh が無いから検証できない」は本当か?** (自分の主張を疑う) | pwsh の入手経路を全て試し、駄目なら代替を探す | **半分は否。** pwsh は入手不能(apt に無く、GitHub API はこのリポジトリ以外 403、NuGet も遮断)。しかし「**構文検査できない**」の方は覆せた — 実際に使っている構文に絞った静的検査なら書ける。完全なパーサーではないが「無検証」よりはるかに良い。**「不可能」は入手の話であって、検証の話ではなかった** |
| Windows 実機の16検査のうち、本当に Windows でしか確かめられないのはどれか? | 各検査を Linux で代替できるか個別に検討 | **サテライトアセンブリの生成と10言語解決は `tools/lang-run` が既に Linux で証明していた**(publish 成果物の構造だけが Windows 固有)。その過程で **CS8600 警告が `AeroDriver.Languages` に隠れていた**ことが判明 — 私の「CS警告ゼロ」確認は Core だけで、他層を見ていなかった |
| なぜその警告は隠れていたのか? | ビルドの挙動を確認 | **`dotnet run` / `dotnet build` はインクリメンタル**で、一度通ると再コンパイルされず**警告が再出力されない**。「警告ゼロ」を主張するには `--no-incremental` が要る。検証スクリプトが自分の主張を裏切っていた |
| CLI の「スクリプトから成否判定できる終了コード」は全経路で成立しているか? | 全9ハンドラーの終了コードを走査 | **是。** 最初の走査で `RunInstallAsync` / `RunRollbackAsync` に成功経路が無いように見えたが、**私の正規表現が三項演算子(`result.IsSuccess() ? ExitSuccess : ExitFailure`)を拾えていなかっただけ**。コードは正しかった。**問題なし**(測定側の誤りだったことも記録に残す — 「欠陥を見つけた」と早合点しないため) |
| 時間経過に対して安全か(履歴は追記のみ。年単位で動かしたら?) | 上限とローテーションの有無を確認 | **上限は実装されていた**(5 MiB で古い半分を切り捨て)。**しかし一度も実行されていなかった** — 年単位で使えば必ず通る経路で、壊れていれば監査証跡を全損する。実際に 5 MiB を超えさせて実行検証したところ**ロジックは正しかった**(新しい半分が残る / 空にならない / 一時ファイルを残さない) |
| その検証は本当に効いているか?(自分のテストを疑う) | 実装を意図的に壊して検出できるか確認 | **最初は否。** 「新しい方が残る」を先頭1件だけで見ていたため、`Skip` を `Take` に変えても検出できなかった(Trim は append より先に走るので、どちらの実装でも直前の追記が先頭に来る)。各行に通し番号を入れて識別可能にし、3方向の変異すべてで検出するようにした |
| Windows 専用製品を非対応OSで起動したらどうなるか? | OS 判定の有無を確認 | **否、判定が一切無かった。** CLI は `net8.0` を対象にしているため Linux/macOS でも**起動できてしまう**。WMI が無いのでスキャンは「0 件のドライバーを検出しました」という**成功に見える誤った結果**を返し、ユーザーは「このマシンにドライバーが無い」と解釈しかねない。README の Development 節は OS の断り無く `dotnet run` を案内しており、開発者が実際に踏む経路でもあった |
| 永続ファイルの書き込みは中断に耐えるか? | `File.WriteAll*` の全呼び出しを走査 | **否、3箇所が非アトミックだった。** `File.WriteAllText` は「切り詰めてから書く」ため、途中で落ちると空/前半だけのファイルが残る。**最も重いのは BYOVD ブロックリストのキャッシュ** — 壊れたファイルの mtime は新しいので TTL(7日)検査を通ってしまい、照合が空または不完全なまま**最大7日間**使われる(空集合は15分ごとに再読込するが、同じ壊れたファイルを読むだけで回復しない)。履歴の切り詰めは temp+Move で正しく保護されていたのに、他がその方針から漏れていた |
| システムを変更する全操作が昇格を要求しているか? | 変更操作と `ElevationGuard` を突き合わせ | **是。** 到達可能な入口(`DriverService` の install/backup/rollback)はすべて `ElevationGuard.ThrowIfNotElevated` を通る。`BackupService` の内部実装に無いのは、昇格済み経路からのみ呼ばれるため。**問題なし** |
| ダウンロードのサイズ上限は全経路にあるか? | HTTP 取得箇所を全走査 | **否。** ドライバー本体には 4 GiB 上限(Content-Length の申告と実バイト数の両方を検査)があったのに、**BYOVD ブロックリストの取得だけが `GetStringAsync` で無制限**にメモリへ展開していた。配信元が乗っ取られたり応答が肥大すると OOM でプロセスごと落ちる。防御が片方の経路にしかない、このリポジトリで繰り返し見つかった構造 |
| **直前の自分の修正は正しかったか?**(自分の成果物を疑う) | temp+Move に変えた3箇所を複数プロセスの観点で見直す | **否、不完全だった。** 一時ファイル名が固定(`.tmp`)だったため、**GUI と CLI が同時に保存すると同じ一時ファイルを奪い合い、書き途中の内容を Move してしまう** — アトミック化が防ぐはずだった破損そのもの。`DriverService` のダウンロードは元から `Guid.NewGuid()` を使っており、そこだけ正しかった。一意名にすると今度は失敗時に溜まるので `finally` での後始末も必要 |
| **「作り手側の作業が尽きた」と言える根拠は作れるか?**(自分の停止基準を疑う) | 危険な操作と機械検証の対応表を作り、空欄を探す | **2つ空いていた。** WQL への文字列埋め込みとパス組み立ては **Q10 で手で確認して「問題なし」と記録しただけ**で、再発を防ぐものが無かった。このリポジトリで繰り返し起きたのは「規則は書かれているが強制されていない」ことによるすり抜けなので、**手で確認した不変条件こそ固定すべきだった**。`tools/check-injection.py` で両方を強制 |
| ドキュメントの数値は実測と一致するか? | README/CLAUDE.md の件数を実測と突き合わせ | **否。** README「130/111 assertions」→実測 152/123、CLAUDE.md「60キー」→67。散文に数値を書く限り必ず再発する構造なので、**生きた文書から件数を追放**し `check-docs.py` で書けなくした。docs/ の日付付き記録は「その時点の事実」として対象外 |
| 絶対規則(1〜10)はすべて機械検証されているか? | 規則一覧と `check-*.py` を突き合わせ | **否。** 規則3/4/5/8/9/10 は機械化済みだったが**規則1(課金・テレメトリ禁止)に無かった**。外部ホスト許可リスト+テレメトリ/有償パッケージ拒否リスト+FluentAssertions メジャー上限で機械化(`check-rule1.py`)。規則2/6/7 は判断を要する方針で誤検出の害が上回るため**意図的に機械化しない** |
| 「完成の受け入れ条件」は本当に製品の要件か? | P0 の2項目を「製品の機能/安全性に寄与するか」で分類 | **半分は否。** CI YAML push は製品の機能でも安全性の層でもなく、既存の検証スクリプトを自動で回すだけのインフラ。マスクの「自動化は最後、必要が証明されてから」に照らして **P0 から削除**。Windows 実機での `verify-windows.ps1` だけが真の受け入れ試験として残る |
| **一度も実行されていなかったテストは、実行すれば信頼できるか?**(緑になった自分の成果を疑う) | 再ビルドを強制して繰り返し実行し、揺らぎを探す | **否。** `Progress_ReportsAreReceivedInOrder` が**再ビルド直後の初回だけ落ちる**フレークだった。`Progress<T>` は SynchronizationContext が無いと ThreadPool へ Post するため、コールバックがアサーションと並行に走り**スレッド安全でない `List<int>` を壊す**。しかも「順序」を名乗りながら順序は保証されず、表明も `BeSubsetOf`(空でも通る)でほぼ何も検証していなかった。順序を保証するコンテキストを用意して決定的にし、`ContainInOrder(1,2,3)` + `HaveCount(3)` に強めた。10回の強制再ビルドで揺らぎゼロを確認 |
| **CLI のハンドラーは実行されているか?**(型検査で満足していないか) | `cli-typecheck` のスタブ挙動を確認し、実際に動くパーサーで走らせる | **否。** スタブの `InvokeAsync` は常に 0 を返し `SetHandler` は何もしないため、**引数検証も終了コードの配線も一度も実行されていなかった**。代役パーサーと private ハンドラーへの直接呼び出しで36項目を実行検証し、`config --set` の **partial apply** を発見(下記 AZ) |
| **「xunit が restore できない」は本当にテストを実行できない理由か?**(手段と目的を分ける) | `DispatchProxy` と本物の表明で xunit 無しにスイートを走らせてみる | **否。** xunit はテストを走らせる**手段**であって目的ではない。BCL だけで 187 ケースを実行でき、**一度も走っていなかったスイートから11件の失敗が出た**。うちハーネス側のバグは1件で、残り10件は実在の欠陥(`dynamic` への `.Should()` は Windows でも落ちる / バックアップ世代の秒精度衝突 / パストラバーサル例外の握りつぶし / イベント表明の矛盾) |
| **人間の貢献者が最初に読む文書は、実際のワークフローと一致するか?**(機械ではなく人に向けた面を疑う) | `CONTRIBUTING.md` を `verify-all.sh` / `verify-windows.ps1` / CLAUDE.md の絶対規則と突き合わせ | **否。** 29チェックの検証スクリプトも Windows の受け入れ試験も絶対規則も**一度も出てこず**、`dotnet restore && build && test` だけが書かれていた。さらに「Core はクロスプラットフォームで単体テストできる」は**事実に反する**(テストは NuGet を要し Windows でしか走らない)。機械向けの整合はすべて固めたのに、**人向けの入口だけが乖離していた** |
| ライセンス表記は LICENSE / csproj / README で一致するか? | 3箇所を突き合わせ | **是。** すべて「2025 Shizuku Tanaka / MIT」。問題なし |
| xunit テストと offline-verify の主張は矛盾しないか? | `VersionHelper` を標本に両者の期待値を比較 | **是。** xunit は `==1/-1/0`、offline-verify は `<0/==0` を要求するが `Int32.CompareTo` は正確に -1/0/1 を返すので両立する。**標本検査であり全数ではない**(全数は Windows の `dotnet test` が担う) |
| 受け入れ試験は README の配布手順と同じ成果物を検証しているか? | `verify-windows.ps1` と README の Installation を突き合わせ | **是。** `dotnet publish -r win-x64`(CLI/GUI)とサテライトアセンブリの存在確認が入っている。問題なし |
| 巻き戻った環境での検証は信用できるか? | 古いスナップショットに対する走査結果を main と突き合わせ | **否。** コンテナ復元直後の走査は修正済みの欠陥を「回帰」として誤検出した。必ず `git ls-remote` で真の main を確認してから検証すること(この教訓自体を記録) |

## 短所(確認済みの事実)

行番号まで確認した事実のみ。解決済みの項目も「何が問題だったか」の記録として残す。

### この環境で到達できない2項目の分解(2026-08-26)

残る未チェック2項目について、「何についての不可能か」を分解した結果を記録する。
「不可能」と一括りにせず、覆せる部分は覆した。

| 主張 | 検証 | 結論 |
|------|------|------|
| Windows 実機が無い | wine / WindowsDesktop ターゲティングパック / Windows コンテナを探索 | **不可能で確定**(ハードウェアと OS の問題) |
| NuGet が使えない | `api.nuget.org` 等を再実測(コンテナ入替後も) | **403 で確定**。非公式ミラー経由は**採らない**(セキュリティツールのサプライチェーンを崩さない) |
| CI が使えない | Contents API / Actions API / 素の `git push` / `add_repo` の4経路 | **不可能で確定**。`git push` は明示的に `refusing to allow a GitHub App to create or update workflow ... without workflows permission` を返す |
| ~~受け入れ試験スクリプトを検証できない~~ | pwsh の入手を試し、駄目なら代替を探す | **覆した。** 入手は不可能だが、実際に使う構文に絞った静的検査(`tools/check-ps1.py`)は書ける。**「不可能」は入手の話であって検証の話ではなかった** |
| ~~サテライトの生成を確認できない~~ | 受け入れ試験の16検査を個別に分解 | **覆した。** `tools/lang-run` が Linux で生成と10言語解決を証明済み。Windows 固有なのは publish 成果物の構造だけ |

### 開発側の作業は完了(2026-08-26 宣言)

**モデル/開発者側で実行可能なタスクは残っていない。** 未チェックの2項目はどちらも
「この環境の権限/OS では原理的に実行できない」ことを複数経路の実測で確定済みの
**人間の作業**であり、それぞれ1コマンド/1ファイル追加に帰着させてある:

1. Windows 実機で `pwsh -File tools/verify-windows.ps1` → `0 failed` が受け入れ条件
2. `workflows` 権限のあるアカウントで CI YAML を push

製品コードは: この環境で実行可能な全コードが実行済み(offline-verify / ui-run /
di-run / lang-run)、全C#がコンパイラを通過済み、リソースは65キー×10言語で
パリティ機械検証済み、配布設定はローカライズを壊さないことを機械検証済み、
`.sln` と `PackageReference` の健全性も機械検証済み。verify-all.sh の全チェックは
変異テストで「壊すと検出される」ことを確認してある。

### 未解決

**実装可能なギャップは全て解消済み**。残るのは (a) この環境では原理的に到達できないもの、
(b) 意図的に選択した設計、のいずれか。「未着手のタスク」は残っていない。

#### (a) 環境制約でここでは実行できない

1. **Windows実機での `dotnet build AeroDriver.sln && dotnet test`**(最優先)。
   Linux に .NET SDK 8 は導入でき、**Core の24ファイルは実コンパイル+実行で検証済み**
   (`tools/offline-verify`、**130アサーション全通過**)。
   **WPF層(`tools/ui-typecheck`)と CLI(`tools/cli-typecheck`)の手書きC#も型検査済み**
   — スタブに対する実コンパイルで、**プロジェクト内の全C#が何らかの形でコンパイラを通った**。
   **WMI依存の `DriverService`/`WdacHelper` も型検査済み**(`tools/core-typecheck`)。
   到達できないのは **実行時の挙動**のみ: XAML のコンパイル(Windows専用)、ソースジェネレーターの
   実出力、実WMIクエリ、System.CommandLine の実パース。
   **`MainViewModel` は `tools/ui-run` で実行検証済み**(101アサーション全通過)。
   **DI コンテナは `tools/di-run` で実行検証済み**(16アサーション。captive dependency なし)。
   **コンバーターも `tools/ui-run` で実行検証済み**。
   これでこの環境で実行可能なコードはすべて実行された — 残るのは実 WMI / 実 WPF /
   実 System.CommandLine を必要とする部分だけ。
   さらに `tools/verify-all.sh` が GUI/CLI へのハードコード文字列の混入、XAML 束縛名と
   ViewModel メンバーの一致、未使用リソースキー、`.sln` の健全性、`PackageReference` の
   過不足、配布設定を機械検証する(各チェックは意図的に壊して検出できることを確認済み)
2. **CI 不在**: GitHub App トークンに `workflows` 権限がなく push 不可(YAML は `FEATURE_AUDIT.md` §5)。
   **2026-08-24 に実地検証済み** — 使い捨てブランチへの Contents API 書き込みが
   `403 Resource not accessible by integration` で拒否された。伝聞ではなく実測の不可能

#### (b) 意図的な設計判断(欠陥ではない)

4. **`DriverInstallOrder` はヒューリスティック**: `DeviceClass` 優先度のみで INF の実依存は見ない。
   INF ベースの真の依存解決は費用対効果が見合わないと判断
5. **バックアップ名が秒精度**: 同一デバイスを同一秒に2回バックアップすると混ざる。
   通常経路は1デバイス1回で、命名変更は3箇所が依存する辞書順の互換性を要するため据え置き
6. **`RunAsync` の `_cts` に再入ガードなし**: 多重起動は CanExecute で防いでおり現状問題なし。
   将来コマンドを直接呼ぶ改修を入れる場合の注意点として記録

### 解決済み(記録)

| # | 問題 | 解決 |
|---|------|------|
| A | ブロックリストのTTLがプロセス生存中無視され、フェイルオープンの空集合が固定化 | 643cb9e: `_loadedAtUtc` で再評価。空集合は15分で再試行 |
| B | WUA COMのRCWを解放せずGC任せ | 643cb9e: `ReleaseCom` で逆順解放 |
| C | 一括インストールがAdminRequiredでも全件試行し全件失敗 | 643cb9e: 即中断して1回だけ通知 |
| D | 「バックアップ」ボタンが実際にはカスタムインストールを実行 | bda8420: `Button_CustomInstall` を全10言語に追加し分離 |
| E | `Settings_CreateRestorePoint` が実体のない約束 | bda8420: `SystemRestoreHelper`(`SRSetRestorePointW`) |
| F | バックアップが書き込み専用(一覧・世代選択が不能) | bda8420: `GetAvailableBackupsAsync` と世代指定 `RollbackDriverAsync` |
| G | 死に設定 `AutoUpdateEnabled` / `IncludeBetaDrivers` | c6b49dc: 消費箇所を実装。ベータ判定は `IUpdate::IsBeta` |
| H | インストール履歴/監査証跡なし | f1227cf: `InstallHistoryService`(JSONL追記)、CLI `history` |
| I | JSONライブラリ混在(Newtonsoft + System.Text.Json) | 6bee763: STJ へ統一し Newtonsoft 参照を全削除 |
| J | USB非対応の更新照合(PCI決め打ち) | 36d710c: `HardwareIdParser` で PCI/USB 双方に対応 |
| T | `SetDriverState` が `CimMethodResult.ReturnValue` を `object` と誤認(コメントにも明記)。実際の型は `CimMethodParameter` で `is uint` は **CS8121 でコンパイル不能**。Disable/Enable 削除後は呼び出し元ゼロの孤児でもあった | 直さず削除(30行)。誰も呼ばないコードのコンパイルエラーを直すのは無駄 |
| S | `GetAvailableBackupsAsync` を PR #10 で追加したが**消費者を繋いでいなかった**(API だけ生えた状態)。「バックアップが書き込み専用」の解決を報告済みだったが未完成 | CLI `backups` コマンドと `rollback --version` を追加して実際に世代選択できるようにした |
| R | `WhqlDatabaseService`(Windows Update CatalogのHTMLスクレイピング)と `PciIdDatabase` が**本番コードから一切呼ばれていないデッドコード**。DI登録と自身のテストのみが参照 | 835行を削除。更新取得は WUA COM(公式API)、デバイス名は WMI が既に提供しており機能重複 |
| Q | `PnpUtilDriverSource` の `/enum-drivers /all` 呼び出しが `string[]` 引数に単一文字列を渡し CS1503。**コンパイル不能**、かつ引数分割の規則にも違反 | `["/enum-drivers", "/all"]` に修正 |
| P | `PciIdDatabase` が**コンパイル不能**。タプル要素名をフィールドだけで宣言し全メソッドシグネチャで落としていたため `entry.Name`/`.Devices` が解決不能(CS1061)、さらに `FrozenDictionary` に `new()`(CS0144) | 全シグネチャで要素名を統一し、空値は `.Empty` に |
| O | `AuthenticodeHelper.GetCertificateInfo` が**コンパイル不能**。`CreateFromSignedFile` は基底 `X509Certificate` を返すため `var` で `NotBefore`/`NotAfter` が解決できず CS1061 | `X509Certificate2` に明示的に包み直す。実コンパイルで発見 |
| N | `Directory.Build.props` のXMLコメント内に `--`(`dotnet --info`)があり不正XML。**全プロジェクトのビルドが即失敗する状態だった** | コメント文言を修正。全 props/targets/csproj/resx/config/xaml のXML妥当性を一括検証 |
| M | ドライバーDLに上限がなく、`ArrayPool.Rent(Content-Length)` がサーバー申告値でLOHに巨大配列を確保しうる+`(int)`キャストで2GB超が負値化 | ストリーミングを固定81920チャンクに変更、実バイト数で4GiB上限、long のまま判定 |
| L | 再起動要求(3010/1641)を失敗と誤判定。ドライバーは3010で終わることが多く、成功が失敗と表示され更新一覧に残り続けた | `InstallerExitCode` で解釈。`DriverInstallResult.SuccessRebootRequired` を追加 |
| BA | **`Progress_ReportsAreReceivedInOrder` がデータ競合でフレークしていた**。`Progress<T>` は ThreadPool へ Post するので、コールバックがアサーションと並行にスレッド安全でない `List<int>` を書き換える。再ビルド直後の初回実行でだけ落ちるため見つけにくい(**Windows の `dotnet test` でも同じく起こりうる**)。加えてテスト名は「順序」を謳うのに表明は `BeSubsetOf` で、空リストでも通る状態だった | 投入順に貯めて明示的に流す `SynchronizationContext` をテスト内に用意し、`Progress<T>` の構築前に設定して決定的にした。表明も `ContainInOrder(1,2,3)` + `HaveCount(3)` に強化。10回の強制再ビルドで揺らぎゼロを確認 |
| AZ | **`config --set` が「1件でも不正なら何も変更しない」と宣言しながら partial apply していた**。`SettingsKeys.TryApply` は検証と適用が同一操作なので、それを検証ループに流用すると先行する代入がメモリ上の Singleton に適用済みのまま後続で失敗する。設定ファイルは書き換わらないが、**プロセス内の設定(GUI と CLI が共有する Singleton)は変わってしまう** | `SettingsKeys.TryValidate`(適用せずに判定する)と `Entry.IsValid` を追加し、全件を先に検証してから適用する2パスに変更。`offline-verify` に「TryValidate は値を書き換えない」「判定が TryApply と一致する」を追加 |
| AY | **`dynamic` を引数に取るメソッドの戻り値を `var` で受けており、`.Should()` が実行時に落ちる**テストが5件。引数が `dynamic` だと呼び出し全体が動的束縛になり、拡張メソッドは `dynamic` に実行時束縛されない。**Windows の `dotnet test` でも同じく `RuntimeBinderException` で落ちる**が、スイートが一度も実行されていなかったため誰も気付かなかった | 戻り値を `DriverInfo?` で明示的に受けて静的束縛へ戻す。理由をクラス冒頭に1度だけ注記 |
| AX | **バックアップ世代名が秒精度で、同一秒の2回目が1回目を黙って上書き**していた。`MaxBackupGenerations` も `GetAvailableBackups` も1件しか見えず、**世代管理そのものが機能していなかった**。バックログでは「通常経路は1デバイス1回」として据え置いていたが、テストを実行して初めて実害が観測された | 衝突時に `_2`, `_3` … の連番を付ける。`backup_X` < `backup_X_2` なので既存の `OrderByDescending` の辞書順と時系列が一致し、順序ロジックは変更不要 |
| AW | **`BackupDriverAsync` / `RestoreDriverAsync` の `catch (Exception)` がパストラバーサル検出の `ArgumentException` を握りつぶして `false` を返していた**。同期版の `HasBackup` は伝播しており、同じクラスの中で挙動が食い違っていた。呼び出し側は攻撃入力を通常の失敗と区別できない | `catch (ArgumentException) { throw; }` を追加。規則3の「キャンセルを握りつぶさない」と同じ理由 |
| AV | イベント発火の表明が**自分のコメントと矛盾**していた(先頭で「false を返してもイベントは発火する」と書きながら `BeNull()` を表明)。実装は全失敗経路で発火しており、`UpdatesInstalledEventArgs` は `IsSuccess` を持つので失敗通知が設計上の契約 | 表明を実装(と自分のコメント)に合わせ、`NotBeNull()` + `IsSuccess == false` を検証 |
| AU | **`CONTRIBUTING.md` が実ワークフローとまったく繋がっていなかった**。`tools/verify-all.sh`(29チェック)も `tools/verify-windows.ps1`(受け入れ試験)も CLAUDE.md の絶対規則も一度も登場せず、書かれていたのは `dotnet restore && build && test` のみ。さらに「`AeroDriver.Core` はクロスプラットフォームで単体テストできる」は事実に反していた(xunit は NuGet を要し Windows でしか走らない)。機械向けの整合は固めきったのに、**人が最初に読む入口だけが規則6違反のまま残っていた** | 2経路(任意プラットフォームの `verify-all.sh` / Windows の `verify-windows.ps1`)を軸に全面改稿し、絶対規則・純粋ロジックは offline-verify へ・新しいチェックは変異テストで確かめる慣習を明記。`check-docs.py` に**必須参照チェック**を追加し、3つの参照のいずれかが消えたら失敗するようにした(4方向の変異で検出確認) |
| AT | 規則8(WQL サニタイズ / パス正規化)が**手動確認のみで機械検証されていなかった**。Q10 で「問題なし」と記録した不変条件が、何にも守られていない状態で残っていた | `tools/check-injection.py` を新設。WQL への非サニタイズ値の埋め込みと、外部入力からのパス組み立てにおける正規化/検証の欠落を検出。3方向の変異で確認。**最初の実装は変数名(`safe*`)しか見ておらず `safeId = deviceId` で素通りした**ため、実際に `WqlSanitizer` を通っているかを見るよう修正 |
| AS | **前ラウンドで入れた temp+Move が不完全だった**。一時ファイル名が固定のため、複数プロセス(GUI + CLI)が同時保存すると衝突し、書き途中の内容を Move しうる | 4箇所すべて `Environment.ProcessId` + `Guid.NewGuid()` の一意名にし、`finally` で後始末(一意名は失敗時に溜まるため)。`check-atomic-writes.py` に「一時ファイル名が一意であること」を追加。固定名に戻す変異と後始末を消す変異の両方で検出確認 |
| AR | **BYOVD ブロックリストの取得にサイズ上限が無かった**。`GetStringAsync` が応答全体を無制限にメモリへ展開する。ドライバー本体のダウンロードには 4 GiB 上限があったのに、この経路だけ漏れていた | 64 MiB 上限付きのストリーム読み取りに変更(Content-Length の申告と実バイト数の両方を検査。ドライバー DL と同じ方針)。`tools/check-download-limits.py` を新設し `GetStringAsync`/`GetByteArrayAsync` を禁止、HTTP を使うファイルに上限定数を要求。2方向の変異で検出確認 |
| AQ | **永続ファイルの書き込み3箇所が非アトミック**。`File.WriteAllText` は切り詰めてから書くため、中断で全損/破損する。(1) 設定 → ユーザー設定が黙って既定値に戻る (2) **BYOVD キャッシュ → 壊れても mtime が新しいので TTL(7日)を通り、照合が空/不完全なまま最大7日間使われる** (3) バックアップメタデータ → その世代が復元不能。`InstallHistoryService` の切り詰めだけが temp+Move で正しく、他が漏れていた | 3箇所とも temp+Move に統一。`tools/check-atomic-writes.py` を新設し `File.WriteAll*` に `File.Move(overwrite: true)` を伴うことを機械検証(追記は対象外)。4方向の変異すべてで検出確認 |
| AP | **Windows 専用ツールに OS 判定が無かった**。CLI は `net8.0` のため Linux/macOS でも起動でき、スキャンが「0 件検出」という成功に見える誤った結果を返す(README の Development 節は OS の断り無く `dotnet run` を案内していた) | `PlatformGuard` を新設し CLI 起動直後に翻訳済みの理由付きで早期終了。`Error_WindowsOnly` を10言語に追加。`offline-verify` で実行検証(この環境は Linux なので非対応側の分岐が実際に走る)。ガードを外すと未使用キー検出が捕まえることも確認 |
| AO | インストール履歴の切り詰め(5 MiB 上限)が**実装されているのに一度も実行されていなかった**。年単位で使えば必ず通る経路で、壊れていれば監査証跡を全損する | `offline-verify` に実行検証を追加(10アサーション)。実際に 5 MiB を超えさせて確認 — **ロジックは正しかった**。ただし最初のアサーションは弱く、`Skip`→`Take` の変異を検出できなかった。各行に通し番号を入れて修正し、3方向(古い方を残す/切り詰め無効化/全消し)すべてで検出するようにした |
| AN | `AeroDriver.Languages` に CS8600 警告(`GetString` の `string?` を `string` で受ける)。実挙動は `??` で正しかったが宣言が不一致(規則6)。**インクリメンタルビルドのため警告が再出力されず隠れていた** — 検証スクリプトが「警告ゼロ」を主張しながらそれを確かめられない状態だった | 宣言を `string?` に修正。`verify-all.sh` の型検査ループと実行ハーネス用 `run` ヘルパーの両方を `--no-incremental` にし、**CS 警告を失敗として扱う**ようにした。警告を戻すと exit=1 になることを確認 |
| AM | 受け入れ試験のスクリプトが **pwsh 不在を理由に一度も検査されていなかった**。実行する人がスクリプト側のバグを踏むリスクを、こちらが負わずに丸投げしていた | `tools/check-ps1.py` を新設し、pwsh の有無に依らず必ず走る静的検査を追加。括弧/引用符の均衡・`Check` の戻り値規約・`-When` 変数の定義順・コマンドレット名の綴り・`Start-Process` の後始末を検証。4方向の変異テストで検出確認 |
| AL | **受け入れ条件(`verify-windows.ps1`)自体に穴**があり、通っても製品が壊れている状態がありえた。`dotnet publish` を実行せず、サテライトアセンブリを確認せず、GUI を起動していなかった。過去に修正した「配布するとローカライズが死ぬ」欠陥(AD)をこの試験は検出できない | publish(CLI/GUI 両方)・9言語サテライトの存在確認・配布物の実行・GUI 起動生存確認を追加(16検査)。**「非英語カルチャで翻訳を確認する」検査は書いたが削除した** — `LanguageService` は OS のユーザーカルチャを見るため環境変数では切り替わらず、動かない検査を残す方が有害だから。この限界は README に明記した |
| AK | `IDriverService` に**投機的な API が3件**。`InstallDriverUpdateAsync`(bool版)と `CompareVersions` は本番の消費者がゼロで、自分自身のテストだけが生かしていた(`WhqlDatabaseService` 835行削除と同じ構造)。`StreamAllDriversAsync` は「消費者がペースを制御」と謳いながら外部消費者ゼロ | 前2つは interface と実装から削除し、テストは実 API(`InstallDriverUpdateWithResultAsync` / `VersionHelper.Compare`)へ振り替えて**カバレッジを落とさずに**契約を狭めた。`StreamAllDriversAsync` は `GetAllDriversAsync` が内部で使うため private 化して interface からのみ外した |
| AJ | 規則4(`ConfigureAwait(false)`)に **Core 29箇所の違反**。規則は CLAUDE.md にあったが強制されていなかった。UI スレッドへの不要なマーシャリングと、呼び出し側がブロックした場合のデッドロックを招く | 全箇所に付与。`tools/check-configureawait.py` と `tools/check-processargs.py`(規則5)を新設し、**絶対規則9件すべてに機械検証が付いた状態**にした。自動挿入が3箇所で誤位置に入ったが型検査が捕捉、検出器のバグ2件(文末判定・行数上限)も変異テストで発見して修正 |
| AI | README の「WHQL未認定なら警告する」が **GUI ユーザーに届いていなかった**。警告は `_logger` にしか出ず、`WinExe` の GUI はコンソールを持たない。規則6(宣言と実装の一致)違反 | 両UIの `Describe*` に1箇所で警告を追加(成功・再起動要求・失敗の全経路)。`Warning_NotWhqlCertified` を10言語に追加。ui-run に4アサーションを追加し、警告を消すと3件失敗することを確認。WDAC 状態を UI へ配線する案は、そのための部品が増えるだけなので**採らなかった**(WDAC の詳細はログに残る) |
| AH | **カスタムインストール経路に TOCTOU 対策が無かった**。ダウンロード経路は BYOVD照合〜インストール実行完了まで `FileShare.Read`(書き込み共有なし)のハンドルを保持するが、`InstallCustomDriverAsync` は照合と実行の間にファイルを差し替えられる状態だった。ユーザーが選ぶ任意のパス(Downloads 等の書き込み可能な場所)を扱うため、BYOVD ブロックリストを素通りさせられる | 同じロックを照合前に取得するよう修正。排他読み取りできない場合は**フェイルクローズ**(規則7)で中止する。`tools/check-toctou.py` を新設し「検証→実行の全経路でロックを照合前に保持していること」を機械検証(ロック削除・順序反転の2方向で検出確認) |
| AG | `PnpUtilDriverSource.RunPnpUtilAsync` が `OperationCanceledException` を握りつぶし `string.Empty` を返していた(CLAUDE.md 規則3違反)。呼び出し側の `ParseEnumOutput` は空出力を「ドライバー0件」として扱うため、**ユーザーがキャンセルすると一覧が空になって成功表示される**。規則3は文章では定められていたが機械検証が無かった | OCE を再スローするよう修正。`tools/check-cancellation.py` を新設し、「ct 付き await を含むメソッドの `catch(Exception)` は OCE を再スローしていること」を機械検証(ct を受けないヘルパーは対象外)。再スロー削除・`when` 句除去の2方向で検出確認 |
| AH | 絶対規則1(課金・テレメトリ禁止)だけ機械検証が無く、方針として書かれているだけだった。dependabot の ignore は自動更新を止めるだけで、手で有償版に上げた場合や新しい通信先を足した場合を止める層が無かった | `tools/check-rule1.py`: src の URL ホストを許可リスト(loldrivers / XAML スキーマ)で照合、テレメトリ・有償 SDK の接頭辞を拒否、FluentAssertions メジャー≥8 を拒否。変異3方向すべて検出確認 |
| AG | README「130/111 assertions」CLAUDE.md「60キー」「73アサーション」が実測(152/123、67キー)から乖離。ハーネスを足すたびに手で追随しており、4回以上ずれた(規則6違反が構造的に再発) | 生きた文書(README/CLAUDE.md)から件数を追放し、`tools/check-docs.py` で件数の直書きを禁止。件数は verify-all.sh の出力だけが語る |
| AF | `Status_Error` / `Driver_Status_UpdateAvailable` が値に `{0}` を持つのに**13箇所で引数なしで呼ばれ**、後ろに `: 詳細` を自前連結していた。`ResourceManager.GetString` は書式化しないため **`{0}` がリテラルのまま全10言語で画面に出ていた**(GUI のタブ見出しは常時表示)。CLAUDE.md は「翻訳側にプレースホルダーを持たせない」と定めており `Install_*` はその方針だったが、この2キーだけ旧設計のまま取り残され、呼び出し側だけが新方針に移行して**方針の混在が実害になった** | 2キーをプレースホルダー無しに書き換え(全10言語)、引数付き呼び出し3箇所も自前連結に統一して**全16箇所を単一形式に**。`tools/check-resources.py` を新設し、値のプレースホルダー・引数付き呼び出し・存在しないキー・未翻訳の混入を機械検証(3方向とも変異テストで検出確認) |
| AE | UI 層 .cs に日本語直書きが3件(キャンセルメッセージ・例外ダイアログキャプション・ファイル選択ダイアログ)。XAML/CLI のチェックは盲点だった。キャンセルは `Status_Error` に連結されており**エラー扱い**にもなっていた | 汎用キー5個×10言語を追加して修正。verify-all.sh に UI .cs チェックを追加(ログの日本語は慣習として許容)。変異テストで検出確認済み |
| K | 署名検証の失敗理由が全て「署名が無効」で、オフライン時に誤診断 | `DescribeVerificationFailure` で原因を区別(**フェイルクローズは維持**) |
| AF | **`AeroDriver.Languages` が `AeroDriver.Core` を `ProjectReference` していたが、Core の型を1つも使っていなかった**。「リソース束がドライバーエンジンに依存する」という実体のない結合で、依存グラフを読む人を惑わせビルド順にも無駄な制約を作っていた。`ILogger<T>` だけがこの参照経由で推移的に入っていた | 参照を削除し `Microsoft.Extensions.Logging.Abstractions` を直接依存として宣言。`check-packages.py` に**未使用 ProjectReference の検出**を追加 |
| AE | **`AeroDriver.Languages` がコンパイルできなかった**。`using AeroDriver.Languages.Resources;` がコード上に存在しない名前空間を参照(SDK スタイルでは resx から型付きクラスは自動生成されない)。CS0234。しかも `ResourceManager` は文字列でベース名を受けるので最初から未使用の using だった。10言語対応の中核がビルド不能で、`dotnet build AeroDriver.sln` は Windows でもここで落ちていた | 未使用 using を削除。`tools/lang-run` を新設し、resx コンパイル → サテライト生成 → 解決とフォールバックを実行検証(24アサーション) |
| AD | **配布すると10言語対応が無言で死ぬ構成だった**。10言語すべてが `Strings.<culture>.resx` でサテライトアセンブリになっており中立リソースが無い。`GetString()` は失敗を `"[キー名]"` にフォールバックするため例外も出ず、ボタンが `[Button_Scan]` の UI が出荷される。publish 設定自体も存在しなかった | `Strings.en-US.resx` → `Strings.resx`(中立)+ `NeutralLanguage=en-US`。実行可能プロジェクトに `InvariantGlobalization=false` を明示。`check-packages.py` が中立リソースの欠落・`InvariantGlobalization=true`・`SatelliteResourceLanguages` の指定を検出 |
| AC | **テストコード2,186行が一度もコンパイルされていなかった**。`src/` は型検査していたが `tests/` だけ盲点で、削除した API を参照したまま残っていても Windows 実機でしか発覚しない状態 | `tools/tests-typecheck` で xunit/FluentAssertions/NSubstitute の最小スタブに対して実コンパイル。0エラー(取り残しなし)。`VersionHelper.Compare` の改名で14件検出できることを確認 |
| AB | **`AeroDriver.Core` が `CimSession` を使うのに `Microsoft.Management.Infrastructure` の PackageReference が無かった**。BCL ではなく NuGet パッケージなので `DriverService`/`WdacHelper` が CS0246 でコンパイルできない。レガシー `System.Management` から移行した際に旧パッケージを外して新パッケージを足し忘れていた(csproj のコメントは「移行済み」と書いてあった)。あわせて未使用パッケージ2件(`Microsoft.Extensions.Localization` / `Microsoft.Xaml.Behaviors.Wpf`) | 不足を追加し未使用を削除。`tools/check-packages.py` を新設し、ソースが使う名前空間と PackageReference の過不足を機械検証(ProjectReference 経由の推移的解決も考慮) |
| AA | **`dotnet build AeroDriver.sln` が Windows でも即死する状態だった**。`NestedProjects` で全プロジェクトが自分自身を親として登録されており、親チェーンを辿る MSBuild の `GetUniqueProjectName()` が無限再帰して**スタックオーバーフロー**。加えて GUID 2件が16進として不正。P0 の「Windows実機でビルド」はコンパイル以前に死んでいた | 自己参照ネストを削除し GUID を修正。修正後は解析を通過し、残る失敗は `NU1301`(NuGet が 403)のみ。`tools/check-sln.py` を新設して verify-all.sh から検出 |
| Z | **BYOVD照合が `.cab` の中身に届いていなかった**。照合はコンテナ自体のハッシュに対して行われていたが、LOLDrivers が公開するのはドライバーバイナリ(`.sys`)の SHA256 であってコンテナのハッシュではない。**CAB で包むだけで照合をすり抜けられた**(`.cab` は README が明記する対応形式) | `InstallFromCabAsync` の展開後、pnputil 呼び出し前に展開ディレクトリ配下の全ファイルを照合し、1つでも一致すれば `KnownVulnerableDriver` を返す。`.exe`/`.msi` の内部ドライバーは静的に展開できないため意図的な限界として FEATURE_AUDIT に明記 |
| Y | CLI も同様に、`Console` 出力の27箇所が日本語直書きだった(GetString 経由は19箇所のみ)。GUI と同じく「10言語対応」が非日本語ユーザーには成立していなかった | 散文14キーを全10言語に追加。`details`/`history` の構造化ダンプは WMI プロパティ名に合わせて**英語で統一**(localize すべきものと識別子を分ける)。`verify-all.sh` に CLI 版のハードコード検出も追加 |
| X | GUI が「10言語対応・ライブ言語切替」と謳っていたが、`MainWindow.xaml` に**日本語が20箇所直書き**されていた(列ヘッダー・キャンセル・詳細ペインのラベル全部)。非日本語環境では UI が半分しか翻訳されていなかった | リソースキー15個を全10言語に追加。列ヘッダーは `BindingProxy`(Freezable)経由で束縛。`verify-all.sh` に「XAML にハードコード文字列を残さない」チェックを追加して再発を防止 |
| W | 設定5件(復元ポイント/バックアップ/世代数/起動時確認/ベータ)を Core は尊重していたのに、**GUI にも CLI にも変更手段が無く**設定ファイルを手編集するしかなかった | `SettingsKeys` に定義を集約し、CLI `config --set key=value` と GUI ツールバーのトグルから到達可能にした |
| V | `PnpUtilDriverSource.AddDriverAsync`/`DeleteDriverAsync` が消費者ゼロの死にコード。かつ `BackupService` の復元成否判定が pnputil の**ロケール依存な出力文字列**を必要条件にしており、英語/日本語以外の Windows では成功しても失敗と報告していた | 死にコードを削除して列挙専用に。成否は終了コードのみを根拠にする |
| U | `MainViewModel.InstallAllUpdatesAsync` の一括完了メッセージだけ**日本語がハードコード**されていた(`{n} 件は再起動が必要です`)。単体インストール経路は `Install_RebootRequired` キー経由で、同じ事象が経路によって翻訳されたりされなかったりしていた | `ILanguageService.GetString("Install_RebootRequired")` に統一。`tools/ui-run` の実行検証で発見 |

---

## 改善タスク

> 仕様が確定したタスクの手順は [INSTRUCTIONS_SONNET.md](INSTRUCTIONS_SONNET.md) を参照。

### P0 — 人間の作業が必要(モデルでは完結不可)

- [ ] **Windows実機で `tools/verify-windows.ps1` を実行**(restore/build/test に加え、
  System.CommandLine の実パースと実WMIのスモークまで1コマンドで回る)。
  受け入れ条件: `verify-windows: N passed, 0 failed`。
  **前提だった2つの致命的欠陥は解消済み**: `.sln` の自己参照ネストによる MSBuild の
  スタックオーバーフローと不正GUID(表 AA)、および WMI パッケージ参照の欠落(表 AB)。
  どちらも Windows と無関係の理由でビルドを殺していた。残る障壁は NuGet 到達性のみ
- [x] ~~**CI YAMLの手動push**~~ → **P0 から外し、任意の自動化に再分類**(2026-09-01、
  マスク・アルゴリズム ステップ1/5)。CI は製品の機能でも安全性の層でもなく、
  `tools/verify-all.sh`(Linux)と `tools/verify-windows.ps1`(Windows)が既に行う検証を
  **push ごとに自動で走らせる**だけのもの。「自動化は最後。プロセスが正しいと証明されてから」の
  原則に照らすと、受け入れ条件に置くこと自体が誤りだった。依存の追随は dependabot(nuget)が
  担い、YAML 自体は `FEATURE_AUDIT.md` §5 に完成品として置いてあるので、`workflows` 権限を
  持つ人がいつでも足せる。**製品の完成には不要**

### P1 — 高価値・要注意 [Opus]

- [x] ~~**ブロックリストTTLのプロセス内再評価**(短所3)~~ 完了(643cb9e): `EnsureLoadedAsync`で`_hashes`と併せて
  ロード時刻を保持し、TTL超過なら`_loadLock`内で再ロード。フェイルオープンの空集合は
  短い再試行間隔(例: 15分)にする。対象: `src/AeroDriver.Core/Services/VulnerableDriverBlocklist.cs`。
  受け入れ条件: 既存`VulnerableDriverBlocklistTests`がpassし、「空集合が再試行される」テストを追加
- [x] ~~**一括インストールのAdminRequired早期中断**(短所6)~~ 完了(643cb9e): 1件目が`AdminRequired`なら残りをスキップし
  「管理者権限が必要」を1回だけ表示。対象: `MainViewModel.InstallAllUpdatesAsync`、
  CLI `Program.RunInstallAllAsync`。受け入れ条件: 非管理者実行時にN回ではなく1回で失敗を報告
- [x] ~~**WUA RCWの明示解放**(短所4)~~ 完了(643cb9e): `SearchUpdatesAsync`/`FindDriverAsync`のCOMオブジェクトを
  try/finallyで`Marshal.FinalReleaseComObject`。dynamic経由のRCW解放は罠が多いためOpus推奨。
  受け入れ条件: 既存の「COM不在環境でグレースフル」テストがpassのまま

### P1 — 仕様確定済み [Sonnet]

- [x] ~~**テーマ/言語の永続化**(短所3)~~ 完了: `ThemeName`/`CultureName` を追加。
  `tools/offline-verify` で永続化を実行検証(既存キーが壊れないことも確認)

### P2 — 品質向上 [Sonnet]

- [x] ~~**MainViewModelのユニットテスト**(短所7)~~ 完了: 手段は xunit ではなく `tools/ui-run`。
  NuGet が遮断されているため xunit/NSubstitute は restore できないが、**xunit は手段であって目的ではない**。
  ジェネレーター再現側のコマンドを実 private ハンドラーへ配線し、手書きモックと**本物の DI コンテナ**で
  ViewModel を実際に走らせる方式に切り替えて 73 アサーションを実行検証(Scan/InstallAll/AdminRequired 早期中断/
  言語・テーマ切替/CanExecute/失敗メッセージのリソースキー経由)。NuGet が使える環境で xunit 版を
  作る場合も、検証項目は `tools/ui-run/Program.cs` をそのまま移植できる
- [x] ~~**USB VID/PID対応**(短所10)~~ 完了(36d710c): `HardwareIdParser` を新設。
  現在の利用者は `WindowsUpdateAgentSource`(`WhqlDatabaseService` は後にデッドコードとして削除)
- [x] ~~**JSON統一**(短所8)~~ 完了: Newtonsoft 参照を全削除(その後 `WhqlDatabaseService` 自体を
  デッドコードとして削除したため、この移行作業自体が不要だった)
- [x] ~~**失敗メッセージのローカライズ**(短所7)~~ 完了: `Install_*` 10キーを全10言語に追加し、
  GUI/CLI 双方を `ILanguageService` 経由に。理由は引数なしキーにしてプレースホルダー不一致を構造的に排除

### P3 — リファクタリング(急がない)

- [x] ~~GUI: 一括インストール完了時の結果サマリーダイアログ(成功/失敗の内訳一覧)~~
  **要求ごと削除**(2026-08-26、マスク・アルゴリズム ステップ1-2)。同じ情報が既に2面で
  見える: (1) 成功項目は一覧から除去されるため、**残っている項目=失敗した項目**として
  GUI 上で見分けられる。(2) 件数サマリーはステータスバーに、項目ごとの結果は
  インストール履歴(JSONL / `history` コマンド)に記録済み。3つ目の表示面を
  モーダルで足すのは状態の重複であり、XAML はこの環境で検証できないため
  リスクだけ増える。存在すべきでない部品は作らない
- [x] ~~INFベースの真の依存解決(短所11)~~ **検討の結果、やらないと決定**(2026-08-26)。
  Windows は INF の依存グラフを取得する公開 API を提供しておらず、自前の INF パースは
  仕様外の解釈リスクを抱える。現行の `DeviceClass` 優先度ヒューリスティック
  (チップセット/ストレージ/バス → … → GPU)+逐次インストール+項目単位の失敗継続で、
  実際に問題になる順序依存(土台が先)は既にカバーされている。
  設計判断 (b)4 として維持し、これは「未完了のタスク」ではなく「意図的な非機能」とする
