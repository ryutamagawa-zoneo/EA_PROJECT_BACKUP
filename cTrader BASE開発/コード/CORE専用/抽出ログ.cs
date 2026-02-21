2. Aフェーズの成果（確定事項）
2.1 保存済み成果物（最重要）

確定フルコード（保存資産）

CODE NAME：
ENTRY_FRAMEWORK_M5_ALL_001_004

位置づけ：

001の「エントリー・フレームワーク」だけを切り出した独立EA

今後のすべての戦略の“入口部品”として再利用可能

2.2 Entry Framework に含まれるもの（確定コア）

EMAクロス（確定足2本）

取引時間帯制御（JST）

同時ポジション数制限（MaxPositions）

最小SLガード（MinSL vs ATR）

ATR連動の SL / TP / ロット設計

エントリー決定ログ（EMA_SIGNAL / EMA_PACKET / BLOCK_E..）

2.3 明示的に削除・排除したもの（今後も含めない）

Pivot / タッチ / PP方向系ロジック

経済指標フィルター一式（CSV / NEWS_BLOCK 等）

※今後は 別途API取得コードを使う前提

BuyOnly / SellOnly

EMA20方向フィルター

あらゆる「人為的な方向制限」

👉
Entry Framework は“方向中立・環境適応型”として確定