Mode & Route Spec（入口モード／ルート仕様・確定）
1. 用語定義（曖昧さ排除）
Entry Route（入口ルート）

入口判定に入る 関数レベルの経路を指す。
ロジック内容ではなく、「どの入口関数を通るか」を定義する。

Framework001
TryEntryFramework001() を通る入口ルート
→ 001MODE=True と同一の評価順序・状態更新・Pending管理

Param001
パラメータ再現用の入口ルート
→ Direction / Reapproach / RR などは同一設計だが、Framework001は通らない

EMA
通常のEMA入口ルート

2. 入口ルート決定の唯一ルール（最重要）
入口ルートは、以下の 1行の論理式のみで決定する：
UseFramework001Route =
    Enable001Mode
    OR (EntryMode == Mode001 AND Enable001EntrySuppressionStructure == true)

この式が true の場合：

必ず Framework001 ルートに入る

他の条件は一切見ない

この式が false の場合：

次の優先順位判定に進む（下記）

⚠️ このルール以外で入口ルートを分岐させてはならない
（再議論・例外・分岐追加は禁止）

3. 入口ルートの優先順位（固定）

入口判定は 必ずこの順番で行う。

Framework001

条件：UseFramework001Route == true

処理：TryEntryFramework001() を呼び、即 return

Param001

条件：UseFramework001Route == false かつ EntryMode == Mode001

処理：TryEntrySignal001_WithParamExit() 等のパラメータ系入口

EMA

上記すべてに該当しない場合

処理：通常EMA入口

4. Enable001Mode の正式な意味（再定義）
Enable001Mode は 入口ロジックの中身を切り替えるスイッチではない

正式な役割：
「Framework001 入口ルートを強制するマスタースイッチ」

今後の扱い：

UI上は残してもよい

ただし 内部的には Entry Route 選択専用

5. 「001再現モード」の正式定義（文章化）

以下をすべて満たす状態を 001再現モードと定義する：

Enable001Mode == false

EntryMode == Mode001

Enable001EntrySuppressionStructure == true

EnableRrRelaxStructure == true

Direction / Reapproach / RR パラメータは 001相当のデフォルト値

この状態では：

Framework001 ルートに入る

001MODE=True と入口は完全一致する

TEST-2 合格状態と同義

6. 禁止事項（事故防止）

以下は禁止：

Enable001Mode の有無で

Direction / Pending / RR の 個別挙動を変える

EntryMode と無関係に

Framework001 を直接呼ぶ

「001MODE専用ロジック」という名目で

入口関数を増やす／分岐を増やす

7. このSpecの効力

STEP4/STEP5 の成果を 構造的に固定するための文書

次フェーズ（Parameter Map / 運用手順）の前提

再議論禁止