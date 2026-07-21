# Opus級モデル向け指示書

対象: `IMPROVEMENT_BACKLOG.md`の **[Opus]** ラベルタスク(セキュリティ・並行処理・COM相互運用・P/Invoke)。
共通規則は [/CLAUDE.md](../CLAUDE.md)、全体像は [FEATURE_AUDIT.md](FEATURE_AUDIT.md)。

このドキュメントは「なぜ難しいか・罠・不変条件」を伝えるためのもの。手順の細部より**判断の背景**を重視する。

---

## 共通の心構え

- このリポジトリは **ビルド未検証**。SDKがあるなら着手前に `dotnet build AeroDriver.sln && dotnet test` で
  ベースラインを取る。無いなら静的検証し、コミットに「ビルド未検証」と明記(CLAUDE.md §検証手順)。
- セキュリティ判定は **フェイルクローズ**(署名検証: 不明なら拒否)、可用性層は **フェイルオープン**
  (ブロックリスト取得不可: 警告して通す)。この非対称は**意図的**。混同して「統一」しないこと。
- `OperationCanceledException` は常に再スロー。`catch (Exception)` で握り潰さない。

---

## タスク1: ブロックリストTTLのプロセス内再評価(短所3)

**対象**: `src/AeroDriver.Core/Services/VulnerableDriverBlocklist.cs`(`EnsureLoadedAsync`, 72-107行付近)

**現状の問題**: `if (_hashes != null) return _hashes;`(72,77行)で初回ロード結果がプロセス終了まで固定。
フェイルオープンの空集合(`FrozenSet<string>.Empty`)もTTL・ネットワーク復旧を無視して残り続ける。

**なぜ難しいか / 罠**:
- `PciIdDatabase`(姉妹クラス)は同じ「初回ロード後固定」設計で**意図的に問題ない**(PCI IDは頻繁に
  変わらない)。だが脆弱ドライバーリストは**セキュリティ層**なので鮮度が重要。両者を一律に直さないこと。
- `_loadLock`(SemaphoreSlim)内で再ロードする際、`await`をまたぐので `lock` は使えない(既存が
  SemaphoreSlimなのはこのため)。二重再入に注意。
- ロード時刻の保持は `DateTime.UtcNow` を使うが、この開発環境では `Date.Now`/`new Date()` 系が
  ワークフロー実行時に禁止される点に注意(実コードでは通常通り可)。

**設計指針**:
- `_hashes` と併せて `_loadedAtUtc` を保持。`EnsureLoadedAsync` の速攻リターン条件を
  「`_hashes != null && 経過 < TTL`」に。成功ロードは7日TTL、**空集合(フェイルオープン)は短TTL**
  (例: 15分)にして復旧を早く反映。
- 受け入れ条件: 既存 `VulnerableDriverBlocklistTests` がpass。「空集合ロード後に短TTL経過で再ロードされる」
  テストを追加(protectedコンストラクタでキャッシュパス+時刻を注入できる形にするか、時刻抽象を渡す)。

---

## タスク2: 一括インストールのAdminRequired早期中断(短所6)

**対象**: `src/AeroDriver.UI/ViewModels/MainViewModel.cs`(`InstallAllUpdatesAsync`, 176-211行) と
`src/AeroDriver.CLI/Program.cs`(`RunInstallAllAsync`)

**現状の問題**: ループ内で各更新を順に `InstallDriverUpdateWithResultAsync` するが、1件目が
`AdminRequired` を返しても残り全件を試行し、全件 `AdminRequired` で失敗する(N回同じ失敗)。

**なぜ[Opus]か**: 「どの失敗で中断し、どの失敗で継続すべきか」の判断が必要。
- `AdminRequired` → 環境要因。残り全部も必ず失敗する → **即中断**して1回だけ通知。
- `SignatureInvalid`/`KnownVulnerableDriver`/`DownloadFailed` → **その1件固有** → 継続して次へ。
- `Cancelled` → ユーザー操作 → 中断(既存の `ct.ThrowIfCancellationRequested` が担う)。

**不変条件**: 成功項目を `AvailableUpdates` から除去する挙動は維持。中断時も「何件成功/何件スキップ」を
正確に集計・表示する。CLI側は「1件でも失敗で非0終了コード」を維持。

---

## タスク3: WUA COMのRCW明示解放(短所4)

**対象**: `src/AeroDriver.Core/Services/WindowsUpdateAgentSource.cs`(`SearchUpdatesAsync` 40-70行, `FindDriverAsync`)

**現状**: `Type.GetTypeFromProgID` + `Activator.CreateInstance` で `dynamic session/searcher/searchResult/updates/update`
を生成するが、`Marshal.ReleaseComObject`/`FinalReleaseComObject` せずGC任せ。反復ポーリングでネイティブ
リソースが滞留しうる。

**なぜ[Opus]か / 罠(最重要)**:
- **`dynamic` 経由のRCW解放は罠だらけ**。`Marshal.FinalReleaseComObject` に `dynamic` を直接渡すと
  意図しないRCWを掴む/例外になることがある。`object` にキャストしてから渡す。
- ループ内の `updates.Item(i)` が返す各 `update` も個別のRCW。解放対象。
- **早すぎる解放**は、まだ読み取り中のプロパティ(`MapToDriverInfo` 内)を壊す。マッピング完了後に解放する順序。
- 非Windows/COM不在環境では既存が「例外を握ってグレースフルに空返し」する設計。この経路を壊さないこと
  (既存テスト `WindowsUpdateAgentSourceTests` が守っている)。
- `try/finally` で `null` チェックしつつ逆順に解放。`ReleaseComObject` は参照カウントを1減らすだけなので、
  確実に手放すなら `FinalReleaseComObject`、ただし共有される可能性があるものには使わない。

**着手前に読む順**: `WindowsUpdateAgentSourceTests.cs`(保証すべき挙動) → `MapToDriverInfo`(解放タイミングの制約)
→ 実装本体。

---

## 新規調査タスク(このセッションで未着手)

- **GUI層のスレッド安全性の実機確認**: `MainViewModel.RunAsync` は `Progress<T>` と `ConfigureAwait(true)` で
  UIスレッドマーシャリングを行う設計。静的には正しいが、`StreamAllDriversAsync` のプロデューサー
  (`Task.Run`)からの `IProgress.Report` がUIスレッドに乗るかは**実機で要確認**(WPF SynchronizationContext)。
- **P0の実機ビルド**: SDKのあるWindowsで全プロジェクトをビルドし、特にWPF XAML +
  CommunityToolkit.Mvvm のソースジェネレーター整合(コマンド名 ⇔ `[RelayCommand]`)を検証。
