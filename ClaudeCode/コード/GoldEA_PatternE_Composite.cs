// =============================================================================
// Gold EA Pattern E - 複合マルチフィルター戦略
// Platform: cTrader (cAlgo API)
// Symbol: XAUUSD (Gold)
// Environment TF: H1 | Entry TF: M5
// Version: 1.0.0
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.None, TimeZone = TimeZones.TokyoStandardTime)]
    public class GoldEA_PatternE_Composite : Robot
    {
        // =================================================================
        // パラメータ: リスク管理
        // =================================================================
        [Parameter("--- Risk Management ---")]
        public string RiskHeader { get; set; }

        [Parameter("Risk Per Trade (%)", Group = "Risk", DefaultValue = 1.5, MinValue = 0.1, MaxValue = 5.0, Step = 0.1)]
        public double RiskPercent { get; set; }

        [Parameter("Max Concurrent Positions", Group = "Risk", DefaultValue = 1, MinValue = 1, MaxValue = 3)]
        public int MaxPositions { get; set; }

        [Parameter("Daily Max Loss (%)", Group = "Risk", DefaultValue = 5.0, MinValue = 1.0, MaxValue = 10.0)]
        public double DailyMaxLossPercent { get; set; }

        [Parameter("Max Spread (pips)", Group = "Risk", DefaultValue = 50, MinValue = 10, MaxValue = 100)]
        public double MaxSpreadPips { get; set; }

        // =================================================================
        // パラメータ: H1 環境フィルター
        // =================================================================
        [Parameter("--- H1 Environment Filter ---")]
        public string H1Header { get; set; }

        [Parameter("H1 EMA Fast", Group = "H1 Filter", DefaultValue = 20, MinValue = 5, MaxValue = 50)]
        public int H1_EMA_Fast { get; set; }

        [Parameter("H1 EMA Mid", Group = "H1 Filter", DefaultValue = 50, MinValue = 20, MaxValue = 100)]
        public int H1_EMA_Mid { get; set; }

        [Parameter("H1 EMA Slow", Group = "H1 Filter", DefaultValue = 200, MinValue = 100, MaxValue = 300)]
        public int H1_EMA_Slow { get; set; }

        [Parameter("H1 MACD Fast", Group = "H1 Filter", DefaultValue = 12)]
        public int H1_MACD_Fast { get; set; }

        [Parameter("H1 MACD Slow", Group = "H1 Filter", DefaultValue = 26)]
        public int H1_MACD_Slow { get; set; }

        [Parameter("H1 MACD Signal", Group = "H1 Filter", DefaultValue = 9)]
        public int H1_MACD_Signal { get; set; }

        [Parameter("H1 Dow Pivot Lookback", Group = "H1 Filter", DefaultValue = 5, MinValue = 3, MaxValue = 10)]
        public int H1_PivotN { get; set; }

        // =================================================================
        // パラメータ: M5 エントリートリガー
        // =================================================================
        [Parameter("--- M5 Entry Trigger ---")]
        public string M5Header { get; set; }

        [Parameter("M5 EMA Fast", Group = "M5 Entry", DefaultValue = 9, MinValue = 3, MaxValue = 20)]
        public int M5_EMA_Fast { get; set; }

        [Parameter("M5 EMA Slow", Group = "M5 Entry", DefaultValue = 21, MinValue = 10, MaxValue = 50)]
        public int M5_EMA_Slow { get; set; }

        [Parameter("M5 RSI Period", Group = "M5 Entry", DefaultValue = 14)]
        public int M5_RSI_Period { get; set; }

        [Parameter("M5 RSI Buy Low", Group = "M5 Entry", DefaultValue = 35, MinValue = 20, MaxValue = 45)]
        public double M5_RSI_BuyLow { get; set; }

        [Parameter("M5 RSI Buy High", Group = "M5 Entry", DefaultValue = 50, MinValue = 40, MaxValue = 60)]
        public double M5_RSI_BuyHigh { get; set; }

        [Parameter("M5 RSI Sell Low", Group = "M5 Entry", DefaultValue = 50, MinValue = 40, MaxValue = 60)]
        public double M5_RSI_SellLow { get; set; }

        [Parameter("M5 RSI Sell High", Group = "M5 Entry", DefaultValue = 65, MinValue = 55, MaxValue = 80)]
        public double M5_RSI_SellHigh { get; set; }

        [Parameter("Fibo Retrace Min (%)", Group = "M5 Entry", DefaultValue = 38.2, MinValue = 23.6, MaxValue = 50.0)]
        public double FiboMin { get; set; }

        [Parameter("Fibo Retrace Max (%)", Group = "M5 Entry", DefaultValue = 61.8, MinValue = 50.0, MaxValue = 78.6)]
        public double FiboMax { get; set; }

        [Parameter("Bullish Confirm Bars", Group = "M5 Entry", DefaultValue = 3, MinValue = 1, MaxValue = 5)]
        public int ConfirmBars { get; set; }

        // =================================================================
        // パラメータ: SLTP管理
        // =================================================================
        [Parameter("--- SL/TP Management ---")]
        public string SLTPHeader { get; set; }

        [Parameter("ATR Period", Group = "SLTP", DefaultValue = 14)]
        public int ATR_Period { get; set; }

        [Parameter("SL ATR Multiplier", Group = "SLTP", DefaultValue = 2.0, MinValue = 1.0, MaxValue = 4.0, Step = 0.1)]
        public double SL_ATR_Multi { get; set; }

        [Parameter("TP1 ATR Multiplier (50%)", Group = "SLTP", DefaultValue = 2.0, MinValue = 1.0, MaxValue = 4.0, Step = 0.1)]
        public double TP1_ATR_Multi { get; set; }

        [Parameter("TP2 ATR Multiplier (30%)", Group = "SLTP", DefaultValue = 4.0, MinValue = 2.0, MaxValue = 8.0, Step = 0.1)]
        public double TP2_ATR_Multi { get; set; }

        [Parameter("Trailing ATR Multiplier (20%)", Group = "SLTP", DefaultValue = 1.5, MinValue = 0.5, MaxValue = 3.0, Step = 0.1)]
        public double Trail_ATR_Multi { get; set; }

        [Parameter("Break Even After TP1", Group = "SLTP", DefaultValue = true)]
        public bool BreakEvenAfterTP1 { get; set; }

        // =================================================================
        // パラメータ: 時間フィルター
        // =================================================================
        [Parameter("--- Session Filter (JST) ---")]
        public string SessionHeader { get; set; }

        [Parameter("Enable Session Filter", Group = "Session", DefaultValue = true)]
        public bool EnableSessionFilter { get; set; }

        [Parameter("London Start (JST Hour)", Group = "Session", DefaultValue = 15, MinValue = 0, MaxValue = 23)]
        public int LondonStartJST { get; set; }

        [Parameter("NY End (JST Hour)", Group = "Session", DefaultValue = 5, MinValue = 0, MaxValue = 23)]
        public int NYEndJST { get; set; }

        // =================================================================
        // 内部変数
        // =================================================================
        private Bars _h1Bars;
        private Bars _m5Bars;

        // H1 インジケーター
        private ExponentialMovingAverage _h1EmaFast;
        private ExponentialMovingAverage _h1EmaMid;
        private ExponentialMovingAverage _h1EmaSlow;
        private MacdCrossOver _h1Macd;

        // M5 インジケーター
        private ExponentialMovingAverage _m5EmaFast;
        private ExponentialMovingAverage _m5EmaSlow;
        private RelativeStrengthIndex _m5Rsi;
        private AverageTrueRange _m5Atr;

        // ダウ理論用ピボットバッファ
        private List<PivotPoint> _h1Pivots = new List<PivotPoint>();

        // トレード管理
        private double _dailyStartBalance;
        private DateTime _lastTradeDate;
        private string _botLabel;

        // 分割決済管理
        private Dictionary<string, PositionState> _positionStates = new Dictionary<string, PositionState>();

        // =================================================================
        // 構造体定義
        // =================================================================
        private enum TrendDirection
        {
            Up,
            Down,
            None
        }

        private struct PivotPoint
        {
            public int Index;
            public double Price;
            public bool IsHigh;
            public DateTime Time;
        }

        private class PositionState
        {
            public bool TP1Hit;
            public bool TP2Hit;
            public double OriginalVolume;
            public double TP1Price;
            public double TP2Price;
            public double EntryPrice;
            public TradeType Direction;
        }

        // =================================================================
        // OnStart
        // =================================================================
        protected override void OnStart()
        {
            _botLabel = "GoldEA_PE_" + SymbolName;
            _dailyStartBalance = Account.Balance;
            _lastTradeDate = Server.Time.Date;

            // H1バーの取得
            _h1Bars = MarketData.GetBars(TimeFrame.Hour);
            _m5Bars = MarketData.GetBars(TimeFrame.Minute5);

            // H1インジケーター初期化
            _h1EmaFast = Indicators.ExponentialMovingAverage(_h1Bars.ClosePrices, H1_EMA_Fast);
            _h1EmaMid = Indicators.ExponentialMovingAverage(_h1Bars.ClosePrices, H1_EMA_Mid);
            _h1EmaSlow = Indicators.ExponentialMovingAverage(_h1Bars.ClosePrices, H1_EMA_Slow);
            _h1Macd = Indicators.MacdCrossOver(_h1Bars.ClosePrices, H1_MACD_Fast, H1_MACD_Slow, H1_MACD_Signal);

            // M5インジケーター初期化
            _m5EmaFast = Indicators.ExponentialMovingAverage(_m5Bars.ClosePrices, M5_EMA_Fast);
            _m5EmaSlow = Indicators.ExponentialMovingAverage(_m5Bars.ClosePrices, M5_EMA_Slow);
            _m5Rsi = Indicators.RelativeStrengthIndex(_m5Bars.ClosePrices, M5_RSI_Period);
            _m5Atr = Indicators.AverageTrueRange(_m5Bars, ATR_Period, MovingAverageType.Exponential);

            // H1ピボット初期構築
            BuildH1Pivots();

            // バー更新イベント
            _h1Bars.BarOpened += OnH1BarOpened;
            _m5Bars.BarOpened += OnM5BarOpened;

            Print("=== Gold EA Pattern E Composite Started ===");
            Print($"Symbol: {SymbolName} | Risk: {RiskPercent}% | MaxPos: {MaxPositions}");
        }

        // =================================================================
        // H1バー確定時: ピボット更新
        // =================================================================
        private void OnH1BarOpened(BarOpenedEventArgs args)
        {
            BuildH1Pivots();
        }

        // =================================================================
        // M5バー確定時: メインロジック
        // =================================================================
        private void OnM5BarOpened(BarOpenedEventArgs args)
        {
            // 日次リセット
            CheckDailyReset();

            // 日次最大損失チェック
            if (IsDailyLossLimitHit())
            {
                return;
            }

            // ポジション管理（分割決済・トレーリング）
            ManageOpenPositions();

            // 最大ポジション数チェック
            if (GetActivePositionCount() >= MaxPositions)
            {
                return;
            }

            // セッションフィルター
            if (EnableSessionFilter && !IsWithinTradingSession())
            {
                return;
            }

            // スプレッドフィルター
            if (Symbol.Spread / Symbol.PipSize > MaxSpreadPips)
            {
                return;
            }

            // === H1 環境判定（3重フィルター） ===
            TrendDirection h1Trend = GetH1Environment();

            if (h1Trend == TrendDirection.None)
            {
                return;
            }

            // === M5 エントリートリガー ===
            int m5Index = _m5Bars.Count - 2; // 確定済みバー

            if (h1Trend == TrendDirection.Up && CheckBuyTrigger(m5Index))
            {
                ExecuteBuy(m5Index);
            }
            else if (h1Trend == TrendDirection.Down && CheckSellTrigger(m5Index))
            {
                ExecuteSell(m5Index);
            }
        }

        // =================================================================
        // H1 環境判定: 3重フィルター
        // =================================================================
        private TrendDirection GetH1Environment()
        {
            int h1Index = _h1Bars.Count - 2; // 確定済みH1バー

            if (h1Index < H1_EMA_Slow + 10)
                return TrendDirection.None;

            // --- フィルター1: EMAパーフェクトオーダー ---
            double emaFast = _h1EmaFast.Result[h1Index];
            double emaMid = _h1EmaMid.Result[h1Index];
            double emaSlow = _h1EmaSlow.Result[h1Index];
            double h1Close = _h1Bars.ClosePrices[h1Index];

            bool emaBullish = emaFast > emaMid && emaMid > emaSlow && h1Close > emaSlow;
            bool emaBearish = emaFast < emaMid && emaMid < emaSlow && h1Close < emaSlow;

            // --- フィルター2: ダウ理論トレンド ---
            TrendDirection dowTrend = GetDowTheoryTrend();

            // --- フィルター3: MACDゼロラインフィルター ---
            double macdLine = _h1Macd.MACD[h1Index];
            double macdSignal = _h1Macd.Signal[h1Index];

            bool macdBullish = macdLine > 0 && macdLine > macdSignal;
            bool macdBearish = macdLine < 0 && macdLine < macdSignal;

            // === 3重一致判定 ===
            if (emaBullish && dowTrend == TrendDirection.Up && macdBullish)
            {
                return TrendDirection.Up;
            }

            if (emaBearish && dowTrend == TrendDirection.Down && macdBearish)
            {
                return TrendDirection.Down;
            }

            return TrendDirection.None;
        }

        // =================================================================
        // ダウ理論トレンド判定
        // =================================================================
        private TrendDirection GetDowTheoryTrend()
        {
            if (_h1Pivots.Count < 4)
                return TrendDirection.None;

            // 直近4つのピボットを取得（新しい順）
            var recentPivots = _h1Pivots.OrderByDescending(p => p.Index).Take(4).OrderBy(p => p.Index).ToList();

            // ピボットから高値・安値を分離
            var highs = recentPivots.Where(p => p.IsHigh).OrderBy(p => p.Index).ToList();
            var lows = recentPivots.Where(p => !p.IsHigh).OrderBy(p => p.Index).ToList();

            if (highs.Count < 2 || lows.Count < 2)
                return TrendDirection.None;

            var lastTwoHighs = highs.Skip(highs.Count - 2).ToList();
            var lastTwoLows = lows.Skip(lows.Count - 2).ToList();

            // 高値切り上げ & 安値切り上げ = Uptrend
            bool higherHighs = lastTwoHighs[1].Price > lastTwoHighs[0].Price;
            bool higherLows = lastTwoLows[1].Price > lastTwoLows[0].Price;

            // 高値切り下げ & 安値切り下げ = Downtrend
            bool lowerHighs = lastTwoHighs[1].Price < lastTwoHighs[0].Price;
            bool lowerLows = lastTwoLows[1].Price < lastTwoLows[0].Price;

            if (higherHighs && higherLows)
                return TrendDirection.Up;

            if (lowerHighs && lowerLows)
                return TrendDirection.Down;

            return TrendDirection.None;
        }

        // =================================================================
        // H1 ピボット構築
        // =================================================================
        private void BuildH1Pivots()
        {
            _h1Pivots.Clear();

            int barsCount = _h1Bars.Count;
            int startIndex = Math.Max(H1_PivotN, 0);
            int endIndex = barsCount - 1 - H1_PivotN;

            for (int i = startIndex; i <= endIndex; i++)
            {
                if (IsSwingHigh(i, H1_PivotN))
                {
                    _h1Pivots.Add(new PivotPoint
                    {
                        Index = i,
                        Price = _h1Bars.HighPrices[i],
                        IsHigh = true,
                        Time = _h1Bars.OpenTimes[i]
                    });
                }

                if (IsSwingLow(i, H1_PivotN))
                {
                    _h1Pivots.Add(new PivotPoint
                    {
                        Index = i,
                        Price = _h1Bars.LowPrices[i],
                        IsHigh = false,
                        Time = _h1Bars.OpenTimes[i]
                    });
                }
            }
        }

        private bool IsSwingHigh(int index, int n)
        {
            double high = _h1Bars.HighPrices[index];
            for (int i = 1; i <= n; i++)
            {
                if (index - i < 0 || index + i >= _h1Bars.Count)
                    return false;
                if (_h1Bars.HighPrices[index - i] >= high || _h1Bars.HighPrices[index + i] >= high)
                    return false;
            }
            return true;
        }

        private bool IsSwingLow(int index, int n)
        {
            double low = _h1Bars.LowPrices[index];
            for (int i = 1; i <= n; i++)
            {
                if (index - i < 0 || index + i >= _h1Bars.Count)
                    return false;
                if (_h1Bars.LowPrices[index - i] <= low || _h1Bars.LowPrices[index + i] <= low)
                    return false;
            }
            return true;
        }

        // =================================================================
        // M5 Buyトリガー判定
        // =================================================================
        private bool CheckBuyTrigger(int m5Index)
        {
            if (m5Index < 2)
                return false;

            // 条件1: フィボナッチ・リトレースメントゾーン
            if (!IsInFiboZoneBuy(m5Index))
                return false;

            // 条件2: RSIフィルター（押し目域）
            double rsiValue = _m5Rsi.Result[m5Index];
            if (rsiValue < M5_RSI_BuyLow || rsiValue > M5_RSI_BuyHigh)
                return false;

            // 条件3: EMA短期順方向（EMA9 > EMA21）
            if (_m5EmaFast.Result[m5Index] <= _m5EmaSlow.Result[m5Index])
                return false;

            // 条件4: 直近N本以内に陽線確定（反転確認）
            if (!HasBullishConfirmation(m5Index))
                return false;

            return true;
        }

        // =================================================================
        // M5 Sellトリガー判定
        // =================================================================
        private bool CheckSellTrigger(int m5Index)
        {
            if (m5Index < 2)
                return false;

            // 条件1: フィボナッチ・リトレースメントゾーン
            if (!IsInFiboZoneSell(m5Index))
                return false;

            // 条件2: RSIフィルター（戻り域）
            double rsiValue = _m5Rsi.Result[m5Index];
            if (rsiValue < M5_RSI_SellLow || rsiValue > M5_RSI_SellHigh)
                return false;

            // 条件3: EMA短期逆方向（EMA9 < EMA21）
            if (_m5EmaFast.Result[m5Index] >= _m5EmaSlow.Result[m5Index])
                return false;

            // 条件4: 直近N本以内に陰線確定
            if (!HasBearishConfirmation(m5Index))
                return false;

            return true;
        }

        // =================================================================
        // フィボナッチ・リトレースメント判定
        // =================================================================
        private bool IsInFiboZoneBuy(int m5Index)
        {
            // 直近のスイングHigh/Lowを検出（M5, 簡易版）
            double swingHigh = double.MinValue;
            double swingLow = double.MaxValue;
            int lookback = 60; // M5で60本 = 5時間分

            int startIdx = Math.Max(0, m5Index - lookback);
            for (int i = startIdx; i <= m5Index; i++)
            {
                if (_m5Bars.HighPrices[i] > swingHigh)
                    swingHigh = _m5Bars.HighPrices[i];
                if (_m5Bars.LowPrices[i] < swingLow)
                    swingLow = _m5Bars.LowPrices[i];
            }

            double range = swingHigh - swingLow;
            if (range <= 0)
                return false;

            double currentPrice = _m5Bars.ClosePrices[m5Index];

            // Buy: 上昇トレンドの押し目 → High→Lowへのリトレース
            // フィボレベル = swingHigh - range * (fiboPercent / 100)
            double fiboLow = swingHigh - range * (FiboMax / 100.0);
            double fiboHigh = swingHigh - range * (FiboMin / 100.0);

            return currentPrice >= fiboLow && currentPrice <= fiboHigh;
        }

        private bool IsInFiboZoneSell(int m5Index)
        {
            double swingHigh = double.MinValue;
            double swingLow = double.MaxValue;
            int lookback = 60;

            int startIdx = Math.Max(0, m5Index - lookback);
            for (int i = startIdx; i <= m5Index; i++)
            {
                if (_m5Bars.HighPrices[i] > swingHigh)
                    swingHigh = _m5Bars.HighPrices[i];
                if (_m5Bars.LowPrices[i] < swingLow)
                    swingLow = _m5Bars.LowPrices[i];
            }

            double range = swingHigh - swingLow;
            if (range <= 0)
                return false;

            double currentPrice = _m5Bars.ClosePrices[m5Index];

            // Sell: 下降トレンドの戻り → Low→Highへのリトレース
            double fiboLow = swingLow + range * (FiboMin / 100.0);
            double fiboHigh = swingLow + range * (FiboMax / 100.0);

            return currentPrice >= fiboLow && currentPrice <= fiboHigh;
        }

        // =================================================================
        // 陽線/陰線確認
        // =================================================================
        private bool HasBullishConfirmation(int m5Index)
        {
            for (int i = 0; i < ConfirmBars; i++)
            {
                int idx = m5Index - i;
                if (idx < 0) return false;
                if (_m5Bars.ClosePrices[idx] > _m5Bars.OpenPrices[idx])
                    return true;
            }
            return false;
        }

        private bool HasBearishConfirmation(int m5Index)
        {
            for (int i = 0; i < ConfirmBars; i++)
            {
                int idx = m5Index - i;
                if (idx < 0) return false;
                if (_m5Bars.ClosePrices[idx] < _m5Bars.OpenPrices[idx])
                    return true;
            }
            return false;
        }

        // =================================================================
        // トレード執行: Buy
        // =================================================================
        private void ExecuteBuy(int m5Index)
        {
            double atrValue = _m5Atr.Result[m5Index];
            if (atrValue <= 0) return;

            double entryPrice = Symbol.Ask;
            double slDistance = atrValue * SL_ATR_Multi;
            double slPrice = entryPrice - slDistance;
            double tp1Price = entryPrice + atrValue * TP1_ATR_Multi;
            double tp2Price = entryPrice + atrValue * TP2_ATR_Multi;

            // SLをピップスに変換
            double slPips = slDistance / Symbol.PipSize;

            // ロットサイズ計算
            double volume = CalculateVolume(slPips);
            if (volume <= 0) return;

            // TP1をピップスに変換（初期TPはTP2に設定、TP1は手動管理）
            double tp2Pips = (tp2Price - entryPrice) / Symbol.PipSize;

            var result = ExecuteMarketOrder(
                TradeType.Buy,
                SymbolName,
                volume,
                _botLabel,
                slPips,
                tp2Pips,
                "PE_Buy"
            );

            if (result.IsSuccessful)
            {
                var position = result.Position;
                _positionStates[position.Id.ToString()] = new PositionState
                {
                    TP1Hit = false,
                    TP2Hit = false,
                    OriginalVolume = volume,
                    TP1Price = tp1Price,
                    TP2Price = tp2Price,
                    EntryPrice = entryPrice,
                    Direction = TradeType.Buy
                };

                Print($"[BUY] Entry={entryPrice:F2} SL={slPrice:F2} TP1={tp1Price:F2} TP2={tp2Price:F2} Vol={volume}");
            }
        }

        // =================================================================
        // トレード執行: Sell
        // =================================================================
        private void ExecuteSell(int m5Index)
        {
            double atrValue = _m5Atr.Result[m5Index];
            if (atrValue <= 0) return;

            double entryPrice = Symbol.Bid;
            double slDistance = atrValue * SL_ATR_Multi;
            double slPrice = entryPrice + slDistance;
            double tp1Price = entryPrice - atrValue * TP1_ATR_Multi;
            double tp2Price = entryPrice - atrValue * TP2_ATR_Multi;

            double slPips = slDistance / Symbol.PipSize;
            double volume = CalculateVolume(slPips);
            if (volume <= 0) return;

            double tp2Pips = (entryPrice - tp2Price) / Symbol.PipSize;

            var result = ExecuteMarketOrder(
                TradeType.Sell,
                SymbolName,
                volume,
                _botLabel,
                slPips,
                tp2Pips,
                "PE_Sell"
            );

            if (result.IsSuccessful)
            {
                var position = result.Position;
                _positionStates[position.Id.ToString()] = new PositionState
                {
                    TP1Hit = false,
                    TP2Hit = false,
                    OriginalVolume = volume,
                    TP1Price = tp1Price,
                    TP2Price = tp2Price,
                    EntryPrice = entryPrice,
                    Direction = TradeType.Sell
                };

                Print($"[SELL] Entry={entryPrice:F2} SL={slPrice:F2} TP1={tp1Price:F2} TP2={tp2Price:F2} Vol={volume}");
            }
        }

        // =================================================================
        // ポジション管理: 分割決済 + トレーリングストップ
        // =================================================================
        private void ManageOpenPositions()
        {
            var positions = Positions.Where(p => p.Label == _botLabel && p.SymbolName == SymbolName).ToList();

            foreach (var position in positions)
            {
                string posId = position.Id.ToString();

                if (!_positionStates.ContainsKey(posId))
                    continue;

                var state = _positionStates[posId];
                double currentPrice = position.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;

                // === TP1到達: 50%決済 + ブレイクイーブン ===
                if (!state.TP1Hit)
                {
                    bool tp1Reached = false;

                    if (state.Direction == TradeType.Buy && currentPrice >= state.TP1Price)
                        tp1Reached = true;
                    else if (state.Direction == TradeType.Sell && currentPrice <= state.TP1Price)
                        tp1Reached = true;

                    if (tp1Reached)
                    {
                        // 50%決済
                        double closeVolume = Symbol.NormalizeVolumeInUnits(state.OriginalVolume * 0.5, RoundingMode.Down);
                        if (closeVolume >= Symbol.VolumeInUnitsMin)
                        {
                            ClosePosition(position, closeVolume);
                            Print($"[TP1 HIT] Closed 50% of position {posId}");
                        }

                        // ブレイクイーブン設定
                        if (BreakEvenAfterTP1)
                        {
                            double bePips = 0;
                            ModifyPosition(position, position.EntryPrice, position.TakeProfit);
                            Print($"[BE SET] Position {posId} SL moved to breakeven");
                        }

                        state.TP1Hit = true;
                    }
                }

                // === TP1到達後: トレーリングストップ ===
                if (state.TP1Hit && !state.TP2Hit)
                {
                    int m5Index = _m5Bars.Count - 2;
                    double atrValue = _m5Atr.Result[m5Index];
                    double trailDistance = atrValue * Trail_ATR_Multi;

                    if (state.Direction == TradeType.Buy)
                    {
                        double newSl = currentPrice - trailDistance;
                        if (position.StopLoss.HasValue && newSl > position.StopLoss.Value)
                        {
                            ModifyPosition(position, newSl, position.TakeProfit);
                        }
                    }
                    else // Sell
                    {
                        double newSl = currentPrice + trailDistance;
                        if (position.StopLoss.HasValue && newSl < position.StopLoss.Value)
                        {
                            ModifyPosition(position, newSl, position.TakeProfit);
                        }
                    }
                }
            }
        }

        // =================================================================
        // ロットサイズ計算（リスクベース）
        // =================================================================
        private double CalculateVolume(double slPips)
        {
            if (slPips <= 0) return 0;

            double riskAmount = Account.Balance * (RiskPercent / 100.0);
            double pipValue = Symbol.PipValue;

            if (pipValue <= 0) return 0;

            double volumeInUnits = riskAmount / (slPips * pipValue);

            // 正規化
            volumeInUnits = Symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.Down);

            // 最小ロット確認
            if (volumeInUnits < Symbol.VolumeInUnitsMin)
                return 0;

            // 最大ロット制限
            if (volumeInUnits > Symbol.VolumeInUnitsMax)
                volumeInUnits = Symbol.VolumeInUnitsMax;

            return volumeInUnits;
        }

        // =================================================================
        // セッションフィルター
        // =================================================================
        private bool IsWithinTradingSession()
        {
            int currentHour = Server.Time.Hour;

            // JST換算（Robot TimeZone = TokyoStandardTime に設定済み）
            // ロンドン15:00〜翌5:00 JST
            if (LondonStartJST < NYEndJST)
            {
                // 同日内（例: 8:00〜20:00）
                return currentHour >= LondonStartJST && currentHour < NYEndJST;
            }
            else
            {
                // 日跨ぎ（例: 15:00〜翌5:00）
                return currentHour >= LondonStartJST || currentHour < NYEndJST;
            }
        }

        // =================================================================
        // 日次リセット
        // =================================================================
        private void CheckDailyReset()
        {
            if (Server.Time.Date != _lastTradeDate)
            {
                _dailyStartBalance = Account.Balance;
                _lastTradeDate = Server.Time.Date;
                Print($"[DAILY RESET] Balance: {_dailyStartBalance:F2}");
            }
        }

        // =================================================================
        // 日次最大損失チェック
        // =================================================================
        private bool IsDailyLossLimitHit()
        {
            double dailyLoss = _dailyStartBalance - Account.Balance;
            double maxLoss = _dailyStartBalance * (DailyMaxLossPercent / 100.0);

            if (dailyLoss >= maxLoss)
            {
                Print($"[DAILY LOSS LIMIT] Loss={dailyLoss:F2} / Max={maxLoss:F2}");
                return true;
            }
            return false;
        }

        // =================================================================
        // アクティブポジション数
        // =================================================================
        private int GetActivePositionCount()
        {
            return Positions.Count(p => p.Label == _botLabel && p.SymbolName == SymbolName);
        }

        // =================================================================
        // クリーンアップ: ポジションクローズ時
        // =================================================================
        protected override void OnStop()
        {
            _h1Bars.BarOpened -= OnH1BarOpened;
            _m5Bars.BarOpened -= OnM5BarOpened;
            Print("=== Gold EA Pattern E Composite Stopped ===");
        }
    }
}
