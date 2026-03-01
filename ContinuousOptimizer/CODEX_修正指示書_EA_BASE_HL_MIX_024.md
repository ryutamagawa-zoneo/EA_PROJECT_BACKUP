# CODEX 修正指示書：EA_BASE_HL_MIX_023 → EA_BASE_HL_MIX_024

**作成日**: 2026-03-01
**対象ファイル（入力）**: `EA_BASE_HL_MIX_023.cs`
**出力ファイル名**: `EA_BASE_HL_MIX_024.cs`
**目的**: AI最適化の探索空間を拡張するため、C#コードにパラメーター追加・改修を行う

---

## 変更概要（5項目）

| # | 変更内容 | 種別 |
|---|---------|------|
| 1 | コード番号 023 → 024 に全置換 | 必須 |
| 2 | 取引時間帯を文字列→整数パラメーターに変換 | C#変更 |
| 3 | PartialClose に R倍率トリガーモードを追加 | C#変更 |
| 4 | EMAスロープフィルターを最適化対象として明示 | 確認のみ |
| 5 | ATR-TP モードを最適化対象として明示 | 確認のみ |

---

## 修正 1：コード番号の全置換（必須）

ファイル内の以下の文字列をすべて置換する。

| 置換前 | 置換後 |
|--------|--------|
| `EA_BASE_HL_MIX_023` | `EA_BASE_HL_MIX_024` |
| `"EA_BASE_HL_MIX_023"` | `"EA_BASE_HL_MIX_024"` |
| コメント行の `023` | `024`（コード識別子として使用されている箇所のみ） |

具体的な置換箇所：

```csharp
// 変更前
// THIS: EA_BASE_HL_MIX_023
public class EA_BASE_HL_MIX_023 : Robot
private const string CODE_NAME = "EA_BASE_HL_MIX_023";
private const string BOT_LABEL = "EA_BASE_HL_MIX_023";

// 変更後
// THIS: EA_BASE_HL_MIX_024
public class EA_BASE_HL_MIX_024 : Robot
private const string CODE_NAME = "EA_BASE_HL_MIX_024";
private const string BOT_LABEL = "EA_BASE_HL_MIX_024";
```

**注意**: `023エントリー品質改善` グループのパラメーター名（`EnableEntryQualityGate023` 等）は変更しない。

---

## 修正 2：取引時間帯パラメーターを整数型に変換

### 背景
現在の `TradeStartTimeJst`（文字列 `"09:15"` など）はAI最適化が困難。
整数型パラメーターに変換することで、Bayesian最適化が時間帯を自由に探索できるようにする。

### 変更箇所 A：パラメーター宣言の変更

**変更前**（`Group = "取引時間帯（JST）"` セクション）:
```csharp
[Parameter("取引時間制御を有効にする", Group = "取引時間帯（JST）", DefaultValue = true)]
public bool EnableTradingWindowFilter { get; set; }

[Parameter("取引開始（JST）", Group = "取引時間帯（JST）", DefaultValue = "09:15")]
public string TradeStartTimeJst { get; set; }

[Parameter("取引終了（JST）", Group = "取引時間帯（JST）", DefaultValue = "23:55")]
public string TradeEndTimeJst { get; set; }

[Parameter("強制フラット（JST）", Group = "取引時間帯（JST）", DefaultValue = "23:55")]
public string ForceFlatTimeJst { get; set; }
```

**変更後**（文字列パラメーターを削除し、整数パラメーターを追加）:
```csharp
[Parameter("取引時間制御を有効にする", Group = "取引時間帯（JST）", DefaultValue = true)]
public bool EnableTradingWindowFilter { get; set; }

[Parameter("取引開始 時（JST）", Group = "取引時間帯（JST）", DefaultValue = 9, MinValue = 0, MaxValue = 23)]
public int TradeStartHourJst { get; set; }

[Parameter("取引開始 分（JST）", Group = "取引時間帯（JST）", DefaultValue = 15, MinValue = 0, MaxValue = 59)]
public int TradeStartMinuteJst { get; set; }

[Parameter("取引終了 時（JST）", Group = "取引時間帯（JST）", DefaultValue = 23, MinValue = 0, MaxValue = 23)]
public int TradeEndHourJst { get; set; }

[Parameter("取引終了 分（JST）", Group = "取引時間帯（JST）", DefaultValue = 55, MinValue = 0, MaxValue = 59)]
public int TradeEndMinuteJst { get; set; }

[Parameter("強制フラット 時（JST）", Group = "取引時間帯（JST）", DefaultValue = 23, MinValue = 0, MaxValue = 23)]
public int ForceFlatHourJst { get; set; }

[Parameter("強制フラット 分（JST）", Group = "取引時間帯（JST）", DefaultValue = 55, MinValue = 0, MaxValue = 59)]
public int ForceFlatMinuteJst { get; set; }
```

### 変更箇所 B：OnStart() 内の時間初期化ロジックの変更

`OnStart()` 内またはフィールド初期化の箇所で `ParseHhMmToMinutes()` を呼び出している行を変更。

**変更前**:
```csharp
_tradeStartMinJst = ParseHhMmToMinutes(TradeStartTimeJst, 9 * 60 + 15);
_tradeEndMinJst   = ParseHhMmToMinutes(TradeEndTimeJst, 23 * 60 + 55);
_forceFlatMinJst  = ParseHhMmToMinutes(ForceFlatTimeJst, 23 * 60 + 55);
```

**変更後**:
```csharp
_tradeStartMinJst = TradeStartHourJst * 60 + TradeStartMinuteJst;
_tradeEndMinJst   = TradeEndHourJst * 60 + TradeEndMinuteJst;
_forceFlatMinJst  = ForceFlatHourJst * 60 + ForceFlatMinuteJst;
```

### 変更箇所 C：文字列 "TradeStartTimeJst"/"TradeEndTimeJst"/"ForceFlatTimeJst" の参照を削除

文字列型プロパティの宣言が削除されたので、他の箇所でこれらの文字列プロパティを参照している箇所があれば削除または置換する。
`ParseHhMmToMinutes()` メソッド自体は残してよい（他の用途で使われている可能性があるため）。

---

## 修正 3：PartialClose に R倍率トリガーモードを追加

### 背景
現在の `PartialCloseTriggerPips`（固定pips）に加え、「SLの何倍の利益で部分利確するか」（R倍率）をトリガーに使えるモードを追加する。
これにより、SLの大小に関わらず動的な部分利確が実現できる。

### 変更箇所 A：新パラメーターの追加

`EnablePartialClose`・`PartialCloseTriggerPips`・`PartialClosePercentSLEvent` の近くに以下を追加:

```csharp
// PartialCloseTriggerMode は既存（0=pips固定）
// 以下のモード 1（R倍率）を追加するための新パラメーター

[Parameter("部分利確 Rトリガー倍率（Mode=1時）", Group = "SLイベント管理", DefaultValue = 0.5, MinValue = 0.1, MaxValue = 3.0)]
public double PartialCloseTriggerR { get; set; }
```

`PartialCloseTriggerMode` のコメントを更新:
```csharp
[Parameter("部分利確 トリガー方式（0=固定pips, 1=R倍率）", Group = "SLイベント管理", DefaultValue = 0)]
public int PartialCloseTriggerMode { get; set; }
```

### 変更箇所 B：ApplyPartialCloseIfNeeded() の修正

`ApplyPartialCloseIfNeeded()` メソッド内で、トリガー判定部分を以下のように変更する。

**変更前**（triggerPips による判定ロジック）:
```csharp
private bool ApplyPartialCloseIfNeeded()
{
    if (!EnablePartialClose)
        return false;

    int triggerPips = Math.Max(0, PartialCloseTriggerPips);
    if (triggerPips <= 0)
        return false;

    // ...
    // ループ内でポジションの現在利益をpipsで計算し、triggerPips を超えたら部分決済
```

**変更後**（Mode 0: pips固定 / Mode 1: R倍率 分岐を追加）:

`ApplyPartialCloseIfNeeded()` メソッド内のポジションループで、各ポジションのトリガー判定部分を以下のように変更する:

```csharp
// ====== 変更後のトリガー判定ロジック（ポジションループ内）======

double triggerDistancePrice = 0.0;

if (PartialCloseTriggerMode == 1)
{
    // Mode 1: R倍率モード
    // SL幅 × PartialCloseTriggerR が利益に達したら部分利確
    double slDistance = 0.0;
    if (position.StopLoss.HasValue)
    {
        slDistance = Math.Abs(position.EntryPrice - position.StopLoss.Value);
    }
    else
    {
        // SL未設定の場合はスキップ
        continue;
    }
    double rMultiplier = Math.Max(0.1, PartialCloseTriggerR);
    triggerDistancePrice = slDistance * rMultiplier;
}
else
{
    // Mode 0: 固定pips（従来動作）
    int triggerPips = Math.Max(0, PartialCloseTriggerPips);
    if (triggerPips <= 0)
        continue;
    triggerDistancePrice = Symbol.PipSize * triggerPips;
}

// 現在の含み益（エントリーからの距離）を計算
double currentProfitDistance = 0.0;
if (position.TradeType == TradeType.Buy)
    currentProfitDistance = Symbol.Bid - position.EntryPrice;
else
    currentProfitDistance = position.EntryPrice - Symbol.Ask;

if (currentProfitDistance < triggerDistancePrice)
    continue;

// 以降は従来通りの部分決済ロジックを使用
// （PartialClosePercentSLEvent に基づいてボリュームを計算して決済）
```

**注意**: 既存のループ構造を維持しつつ、トリガー判定部分のみを上記の分岐に差し替えること。
既存の `_partialCloseExecuted` フラグ管理・ログ出力・リトライロジックは変更しない。

---

## 修正 4：EMAスロープフィルターの確認（変更なし・確認のみ）

以下のパラメーターが 023 にすでに存在することを確認:

```csharp
[Parameter("EMA傾き方向判定を使用", Group = "方向・判定補助", DefaultValue = false)]
public bool EnableEmaSlopeDirectionDecision { get; set; }

[Parameter("EMA傾き判定 本数", Group = "方向・判定補助", DefaultValue = 1, MinValue = 1)]
public int EmaSlopeLookbackBars { get; set; }

[Parameter("EMA傾き 最小有効差分（PIPS）", Group = "方向・判定補助", DefaultValue = 0.0, MinValue = 0.0)]
public double EmaSlopeMinPips { get; set; }
```

**C#コードの変更は不要**。これらは ai_optimizer.py の PARAM_SPACE に追加するだけで対応する。

---

## 修正 5：ATR-TP モードの確認（変更なし・確認のみ）

以下のパラメーターが 023 にすでに存在することを確認:

```csharp
[Parameter("TP方式（ATR/構造/固定/SL倍率）", Group = "TP関連・共通", DefaultValue = TP方式.SL倍率)]
public TP方式 TpMode { get; set; }

[Parameter("TP/ATR倍率", Group = "TP関連・ATR", DefaultValue = 2.0, MinValue = 0.0)]
public double TpAtrMult { get; set; }
```

**C#コードの変更は不要**。TpMode と TpAtrMult を ai_optimizer.py の PARAM_SPACE に追加するだけで対応する。

---

## 修正 6：「024エントリー品質改善」グループの追加（任意・推奨）

023の EntryQualityGate がバックテストで壊滅的結果を招いたことから、024ではより軽量なフィルターとして「勢い確認フィルター」を追加する。

### 新パラメーター（クラス内フィールド宣言の 023品質ゲートブロック直後に追加）

```csharp
// ============================================================
// 024 勢い確認フィルター
// ============================================================
[Parameter("024勢いフィルター有効", Group = "024勢い確認フィルター", DefaultValue = false)]
public bool EnableMomentumFilter024 { get; set; }

[Parameter("024 ATR倍率フィルター（ATR×倍率 < BodyPips でブロック）", Group = "024勢い確認フィルター", DefaultValue = 0.5, MinValue = 0.0, MaxValue = 3.0)]
public double MomentumAtrBodyRatio024 { get; set; }

[Parameter("024 ATR期間", Group = "024勢い確認フィルター", DefaultValue = 14, MinValue = 1, MaxValue = 50)]
public int MomentumAtrPeriod024 { get; set; }
```

### 実装場所

エントリー判定の最終段階（`PassesEntryQualityGate023()` の呼び出し直後）に以下を追加:

```csharp
// 024 勢いフィルター
if (EnableMomentumFilter024)
{
    if (!PassesMomentumFilter024(type, signalBarIndex, out string momentumBlockReason))
    {
        EmitExecuteSkipJsonl("MOMENTUM_FILTER_024_BLOCK", type, signalBarIndex, null, null, null, momentumBlockReason);
        return false;
    }
}
```

### PassesMomentumFilter024() メソッド（新規追加）

クラス内の適切な位置（`PassesEntryQualityGate023()` メソッドの近く）に以下のメソッドを追加:

```csharp
private bool PassesMomentumFilter024(TradeType type, int barIndex, out string blockReason)
{
    blockReason = "NA";

    if (!EnableMomentumFilter024)
        return true;

    if (_atr == null || _atr.Result == null || barIndex < 0 || barIndex >= _atr.Result.Count)
        return true;

    double atrValue = _atr.Result[barIndex];
    if (atrValue <= 0.0)
        return true;

    // シグナルバーの実体サイズを取得
    if (Bars == null || barIndex >= Bars.Count)
        return true;

    double open  = Bars.OpenPrices[barIndex];
    double close = Bars.ClosePrices[barIndex];
    double bodySize = Math.Abs(close - open);

    // ローソク足の実体が ATR×倍率 未満なら弱い勢いとしてブロック
    double threshold = atrValue * Math.Max(0.0, MomentumAtrBodyRatio024);
    if (bodySize < threshold)
    {
        blockReason = string.Format(
            CultureInfo.InvariantCulture,
            "Body={0:F5} < ATR×{1:F2}={2:F5}",
            bodySize, MomentumAtrBodyRatio024, threshold);
        return false;
    }

    return true;
}
```

### ATRインジケーター変数の確認

024コード内で `_atr` という ATR インジケーター変数が既に使用されているか確認すること。
もし存在しない場合は、`OnStart()` 内で以下を追加して初期化する:

```csharp
_atr = Indicators.AverageTrueRange(MomentumAtrPeriod024, MovingAverageType.Simple);
```

そしてフィールド宣言に:
```csharp
private AverageTrueRange _atr;
```

**注意**: 既存の `_atrTp` (TP用ATR) とは別物として扱う。`_atrMinSl` など既存の ATR 変数を流用する場合は period が異なる可能性があるため確認すること。

---

## 変更後の最終確認チェックリスト

- [ ] クラス名が `EA_BASE_HL_MIX_024` になっている
- [ ] `CODE_NAME` と `BOT_LABEL` の定数が `"EA_BASE_HL_MIX_024"` になっている
- [ ] `TradeStartTimeJst`（文字列型）のプロパティ宣言が削除されている
- [ ] `TradeEndTimeJst`（文字列型）のプロパティ宣言が削除されている
- [ ] `ForceFlatTimeJst`（文字列型）のプロパティ宣言が削除されている
- [ ] `TradeStartHourJst`, `TradeStartMinuteJst`, `TradeEndHourJst`, `TradeEndMinuteJst`, `ForceFlatHourJst`, `ForceFlatMinuteJst`（int型）が追加されている
- [ ] `OnStart()` 内で整数型パラメーターを使って `_tradeStartMinJst` 等が計算されている
- [ ] `PartialCloseTriggerR`（double型）パラメーターが追加されている
- [ ] `ApplyPartialCloseIfNeeded()` が Mode 0/1 で分岐している
- [ ] `EnableMomentumFilter024`、`MomentumAtrBodyRatio024`、`MomentumAtrPeriod024` が追加されている（任意）
- [ ] `PassesMomentumFilter024()` メソッドが追加されている（任意）
- [ ] コンパイルエラーがない（cTrader 上でビルド確認）

---

## ai_optimizer.py への追加変更（別途対応）

C# 変更完了後、以下を `ai_optimizer.py` の PARAM_SPACE に追加する：

```python
# 既存パラメーターの範囲拡張
# MinRRRatio: 現在 (0.0, 1.5, 0.0) に範囲拡張済みか確認

# 新規追加パラメーター
("TradeStartHourJst",   "int",   7,  12, 9),      # 取引開始時（JST）
("TradeStartMinuteJst", "int",   0,  59, 15),     # 取引開始分
("TradeEndHourJst",     "int",  20,  23, 23),     # 取引終了時（JST）
("TradeEndMinuteJst",   "int",  30,  59, 55),     # 取引終了分

("EnablePartialClose",          "bool", 0, 1,  0),    # 部分利確 ON/OFF
("PartialCloseTriggerPips",     "int",  20, 100, 50),  # 部分利確 pipsトリガー
("PartialClosePercentSLEvent",  "float", 20.0, 60.0, 30.0),  # 部分利確 %
("PartialCloseTriggerMode",     "int",  0, 1, 0),      # 0=pips, 1=R倍率
("PartialCloseTriggerR",        "float", 0.3, 1.5, 0.5),  # R倍率トリガー

("EnableEmaSlopeDirectionDecision", "bool", 0, 1, 0),  # EMAスロープフィルター
("EmaSlopeLookbackBars",            "int",  1, 10, 3),
("EmaSlopeMinPips",                 "float", 0.0, 10.0, 2.0),

("TpMode",      "int",   0, 3, 0),        # 0=SL倍率, 1=固定, 2=ATR, 3=構造
("TpAtrMult",   "float", 1.0, 5.0, 2.0),  # ATR-TP倍率

("EnableMomentumFilter024",     "bool",  0, 1, 0),      # 勢いフィルター
("MomentumAtrBodyRatio024",     "float", 0.1, 1.5, 0.5), # ATR×倍率
```

また、`BEST_KNOWN_PARAMS` を更新し、`config.json` の `cbot_name` を `EA_BASE_HL_MIX_024` に変更、テンプレートも 024 用に作成すること。

---

## 参考：現在のベストパラメーター（テスト#29, Score=31,406）

テスト#71-75のベースとして使用した #29 の主要パラメーター:

| パラメーター | 値 |
|-------------|-----|
| DirectionDeadzonePips | 2.0893 |
| MaxHoldMinutes | 37 |
| FixedSLPips | 45.6412 |
| FixedTpPips | 154.7709 |
| TpMultiplier | 1.0031 |
| ReapproachWindowBars | 49 |
| SampleBreakout_期間 | 35 |
| SampleRsi_期間 | 40 |
| MinutesBeforeNews | 27 |
| MinutesAfterNews | 12 |
| MinImpactLevel | 2 |

NetProfit=120,744 / Trades=3,001 / MaxDD=7.24% / PF=1.10
