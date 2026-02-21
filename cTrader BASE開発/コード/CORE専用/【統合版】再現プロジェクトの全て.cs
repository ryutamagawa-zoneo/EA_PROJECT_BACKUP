【開発部屋 引き継ぎ書】
001入口再現・整理確定版
0. 本部屋の目的（固定）

本部屋の目的は 001入口ロジックを「再現可能な構造」として確定させ、事故なく運用できる形に整理することである。

001MODE=True の入口挙動を
001MODE=False（パラメータ再現）でも完全一致させる

実装は最小・安全に行い、再議論・再設計を不要にする

ログ確認や判断を人に依存しない運用を確立する

※ 最適化・性能改善・001超え探索は対象外

1. 用語定義（曖昧さ排除）
Entry Route（入口ルート）

「どの入口関数を通るか」を指す。
ロジックの中身ではなく 評価順序・状態更新の起点を決める概念。

Framework001
TryEntryFramework001() を通るルート
→ 001MODE=True と完全同一の入口挙動

Param001
パラメータ再現用の入口ルート
→ Framework001は通らない

EMA
通常のEMA入口ルート

2. 入口ルート決定の唯一ルール（最重要）

入口ルートは、以下の 1行の論理式のみで決定する。

UseFramework001Route =
    Enable001Mode
    OR (EntryMode == Mode001 AND Enable001EntrySuppressionStructure == true)


この式が true の場合
→ 必ず Framework001 ルートに入る

例外・追加条件・別分岐は禁止

3. 入口ルートの優先順位（固定）

Framework001

条件：UseFramework001Route == true

処理：TryEntryFramework001() を呼び、即 return

Param001

条件：UseFramework001Route == false かつ EntryMode == Mode001

処理：パラメータ系入口（TryEntrySignal001_WithParamExit 等）

EMA

上記に該当しない場合

4. Enable001Mode の正式な位置づけ

Enable001Mode は
入口ロジックの中身を切り替えるスイッチではない

正式な役割は
Framework001 入口ルートを強制するマスタースイッチ

内部ロジックで Enable001Mode による個別分岐は禁止

5. 001再現モードの正式定義（公式）

以下をすべて満たす状態を 001再現モードと定義する。

Enable001Mode = false

EntryMode = Mode001

Enable001EntrySuppressionStructure = true

EnableRrRelaxStructure = true

Direction / Reapproach / RR パラメータは 001相当のデフォルト

この状態では：

Framework001 ルートに入る

Enable001Mode=True と 入口が完全一致

TEST-2 合格状態と同義

6. パラメータ一覧（日本語・グループ別）
グループA：001再現（入口ルート制御）
名称	推奨（001再現）	影響
001固定モードを有効化	OFF	Framework001 強制
エントリーモード	Mode001	入口候補
001入口抑制構造を有効化	ON	Framework001 条件
RR緩和構造を有効化	ON	RR Pending
グループB：方向状態（Direction）
名称	推奨値	影響
方向判定デッドゾーン幅	10.0	Neutral帯
方向判定ヒステリシス比率	0.6	状態戻り
方向状態最短維持バー数	2	遷移抑制
方向判定価格ソース	終値	判定入力
グループC：距離制限・再接近
名称	推奨値	影響
エントリー最大距離	50.0	即時可否
再接近待機有効バー数	36	Pending期限
再接近成立距離	40.0	消費条件
グループD：RR緩和
名称	推奨値	影響
通常時最小RR比率	1.0	基準RR
最小RR緩和を有効化	ON	Pending
緩和後最小RR比率	0.7	緩和RR
最小RR緩和有効バー数	6	期限
RR緩和中再評価方式	同方向・毎バー	再評価
7. テスト手順（固定）
TEST-1（基準）

Enable001Mode = true

入口件数・時刻・方向を基準として保存

TEST-2（再現）

Enable001Mode = false

001再現モード設定

エントリー時刻＋方向が TEST-1 と完全一致すること

※ 損益・PF・勝率は見ない

8. 運用規約（事故防止）

ログ確認は 人が行わない
→ 出力一式をAIに渡す

修正は 差分パッチ方式のみ

1変更＝1目的

エラーが大量（100件以上）になったら即ロールバック

正本は
ビルド成功＋TEST-1/2成立版のみ

9. 禁止事項（再議論防止）

Enable001Mode による個別ロジック分岐

入口関数の追加・分岐増設

「001専用」という名目での挙動変更

入口一致が取れる前の最適化議論

10. 本書の効力

本書は 入口再現に関する最終確定文書

再議論・再設計は禁止

次フェーズ（最適化・整理・削除）は 別スコープ