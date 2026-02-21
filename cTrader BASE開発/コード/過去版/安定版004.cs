using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HIVE_PivotBounce_v0_1_0 : Robot
    {
        private const string CODE_NAME = "PIVOT_BOUNCE_M5_ALL_DAY_001";

        // ================= PARAMETERS =================

        [Parameter("Risk Per Trade ($)", DefaultValue = 1000.0, MinValue = 0.0)]
        public double RiskDollars { get; set; }

        [Parameter("Risk Reward", DefaultValue = 0)]
        public double RiskReward { get; set; }

        [Parameter("Max Positions", DefaultValue = 1, MinValue = 1)]
        public int MaxPositions { get; set; }

        [Parameter("BE Move Trigger ($)", DefaultValue = 1000.0, MinValue = 0.0)]
        public double BreakevenTriggerDollars { get; set; }

        // --- Risk controls ---
        [Parameter("Risk Buffer Pips (ct pips)", DefaultValue = 150.0, MinValue = 0.0)]
        public double RiskBufferPips { get; set; }

        [Parameter("Emergency Close Mult", DefaultValue = 1.1, MinValue = 1.0)]
        public double EmergencyCloseMult { get; set; }

        [Parameter("Emergency Close Retry (max)", DefaultValue = 10, MinValue = 1)]
        public int EmergencyCloseRetryMax { get; set; }

        [Parameter("Emergency Close Retry Interval (sec)", DefaultValue = 1, MinValue = 1)]
        public int EmergencyCloseRetryIntervalSec { get; set; }

        // ===== Pivot =====

        [Parameter("Pivot Bounce Buffer (PIPS=0.1$)", DefaultValue = 2.0, MinValue = 0.0)]
        public double PivotBufferPips { get; set; }

        [Parameter("Use S1/R1 Bounce Only", DefaultValue = true)]
        public bool UseS1R1Only { get; set; }

        // ===== Trading Window (JST) =====
        [Parameter("Enable Trading Window (JST)", DefaultValue = true)]
        public bool EnableTradingWindowFilter { get; set; }

        [Parameter("Trade Start Hour (JST)", DefaultValue = 9, MinValue = 0, MaxValue = 23)]
        public int TradeStartHourJst { get; set; }

        [Parameter("Trade Start Minute (JST)", DefaultValue = 15, MinValue = 0, MaxValue = 59)]
        public int TradeStartMinuteJst { get; set; }

        [Parameter("Trade End Hour (JST)", DefaultValue = 2, MinValue = 0, MaxValue = 23)]
        public int TradeEndHourJst { get; set; }

        [Parameter("Trade End Minute (JST)", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TradeEndMinuteJst { get; set; }

        [Parameter("Force Close Hour (JST)", DefaultValue = 2, MinValue = 0, MaxValue = 23)]
        public int ForceCloseHourJst { get; set; }

        [Parameter("Force Close Minute (JST)", DefaultValue = 50, MinValue = 0, MaxValue = 59)]
        public int ForceCloseMinuteJst { get; set; }

        // ===== Guards =====
        [Parameter("Max Lots Cap (0=Off)", DefaultValue = 2.5, MinValue = 0.0)]
        public double MaxLotsCap { get; set; }

        [Parameter("Min SL (PIPS=0.1$)", DefaultValue = 50.0, MinValue = 0.0)]
        public double MinSLPips { get; set; }

        [Parameter("Min SL ATR Period", DefaultValue = 14, MinValue = 1)]
        public int MinSlAtrPeriod { get; set; }

        [Parameter("Min SL ATR Mult", DefaultValue = 0.5, MinValue = 0.0)]
        public double MinSlAtrMult { get; set; }

        // ===== News Filter =====
        [Parameter("Enable News Filter", DefaultValue = true)]
        public bool EnableNewsFilter { get; set; }

        [Parameter("Minutes Before News", DefaultValue = 60)]
        public int MinutesBeforeNews { get; set; }

        [Parameter("Minutes After News", DefaultValue = 60)]
        public int MinutesAfterNews { get; set; }

        // ================= STATE =================

        private readonly List<DateTime> _highImpactEventsUtc = new List<DateTime>();
        private AverageTrueRange _atrMinSl;

        private TimeZoneInfo _jstTz;
        private TimeZoneInfo _nyTz;

        private DateTime _currentPivotSessionStartUtc = DateTime.MinValue;
        private bool _hasPivot;

        private double _pp, _r1, _r2, _r3, _r4, _s1, _s2, _s3, _s4;

        // ================= DIAG =================
        private long _diagOnStart, _diagOnStop, _diagOnTick, _diagOnTimer, _diagOnBar;

        private long _diagOnBarRet_BarsCount;
        private long _diagOnBarRet_ForceFlat;
        private long _diagOnBarRet_NotAllowNewEntries;
        private long _diagOnBarRet_NewsWindow;
        private long _diagOnBarRet_MaxPositions;
        private long _diagOnBarRet_NoPivot;

        private long _diagPivotUpdateCalls;
        private long _diagTryPivotBounceEntryCalled;

        private long _diagPlaceTradeCalled;
        private long _diagPlaceTradeRet_RiskDollarLE0;
        private long _diagPlaceTradeRet_BalanceLE0;
        private long _diagPlaceTradeRet_SLDistLE0;
        private long _diagPlaceTradeRet_SLPipsLE0;
        private long _diagPlaceTradeRet_SLBelowMin;
        private long _diagPlaceTradeRet_VolBelowMin;
        private long _diagPlaceTradeRet_VolAboveCap;
        private long _diagCapApplied;
        private long _diagPlaceTradeRet_TPInvalidNoFallback;

        private long _diagExecuteCalled;
        private long _diagSuccess;

        private long _diagEmergencyClosed;

        // CLOSE_REASON
        private long _diagCloseReason_StopLoss;
        private long _diagCloseReason_TakeProfit;
        private long _diagCloseReason_ManualOrOther;
        private long _diagCloseReason_Unknown;

        // RISK_DIAG (corrected Expected)
        private long _diagRiskTracked;
        private long _diagRiskClosed;
        private long _diagRiskLossTrades;
        private long _diagRiskActualLossOver1200;
        private double _diagRiskMaxExpectedLossAtSl = double.NegativeInfinity;
        private double _diagRiskMaxActualLoss = double.NegativeInfinity;
        private DateTime _diagRiskMaxActualLossTimeUtc = DateTime.MinValue;

        private readonly Dictionary<long, double> _expectedLossAtSlByPositionId = new Dictionary<long, double>();
        private readonly List<double> _stopLossMultSamples = new List<double>();
        private double _diagMultP50 = double.NaN, _diagMultP90 = double.NaN, _diagMultP95 = double.NaN, _diagMultP99 = double.NaN, _diagMultMax = double.NaN;
        private long _diagStopClosed;

        // Emergency close retry queue
        private readonly Dictionary<long, int> _emgRetryLeftByPosId = new Dictionary<long, int>();
        private DateTime _lastEmergencySweepUtc = DateTime.MinValue;

        private enum TradingWindowState { AllowNewEntries = 0, HoldOnly = 1, ForceFlat = 2 }

        protected override void OnStart()
        {
            _diagOnStart++;

            _jstTz = ResolveTokyoTimeZone();
            _nyTz = ResolveNewYorkTimeZone();

            _atrMinSl = Indicators.AverageTrueRange(MinSlAtrPeriod, MovingAverageType.Simple);

            Timer.Start(1);
            Positions.Closed += OnPositionClosed;

            // --- minimal symbol facts ---
            double unitsPerLot = SafeUnitsPerLot();
            Print("Started | CodeName={0} | Symbol={1} | PipSize={2} PipValue={3} UnitsPerLot={4}",
                CODE_NAME, SymbolName,
                Symbol.PipSize.ToString("G17", CultureInfo.InvariantCulture),
                Symbol.PipValue.ToString("G17", CultureInfo.InvariantCulture),
                unitsPerLot.ToString("G17", CultureInfo.InvariantCulture));

            // Calendar stub
            string economicCalendarRaw = @"
""DateTime,Event,Importance""
""2025-01-10 13:30:00,Non-Farm Payrolls,High""
""2025-01-15 13:30:00,CPI m/m,High""
""2025-01-29 19:00:00,FOMC Statement,High""
""2025-02-07 13:30:00,Non-Farm Payrolls,High""
""2025-02-12 13:30:00,CPI m/m,High""
""2025-03-07 13:30:00,Non-Farm Payrolls,High""
""2025-03-12 12:30:00,CPI m/m,High""
""2025-03-19 18:00:00,FOMC Statement,High""
""2025-04-04 12:30:00,Non-Farm Payrolls,High""
""2025-04-10 12:30:00,CPI m/m,High""
""2025-05-02 12:30:00,Non-Farm Payrolls,High""
""2025-05-07 18:00:00,FOMC Statement,High""
""2025-05-14 12:30:00,CPI m/m,High""
""2025-06-06 12:30:00,Non-Farm Payrolls,High""
""2025-06-11 12:30:00,CPI m/m,High""
""2025-06-18 18:00:00,FOMC Statement,High""
""2025-07-04 12:30:00,Non-Farm Payrolls,High""
""2025-07-10 12:30:00,CPI m/m,High""
""2025-07-30 18:00:00,FOMC Statement,High""
""2025-08-01 12:30:00,Non-Farm Payrolls,High""
""2025-08-13 12:30:00,CPI m/m,High""
""2025-09-05 12:30:00,Non-Farm Payrolls,High""
""2025-09-10 12:30:00,CPI m/m,High""
""2025-09-17 18:00:00,FOMC Statement,High""
""2025-10-03 12:30:00,Non-Farm Payrolls,High""
""2025-10-15 12:30:00,CPI m/m,High""
""2025-10-29 18:00:00,FOMC Statement,High""
""2025-11-07 13:30:00,Non-Farm Payrolls,High""
""2025-11-12 13:30:00,CPI m/m,High""
""2025-12-05 13:30:00,Non-Farm Payrolls,High""
""2025-12-10 13:30:00,CPI m/m,High""
""2025-12-17 19:00:00,FOMC Statement,High""
";
            LoadEconomicCalendarUtc(economicCalendarRaw);
        }

        protected override void OnStop()
        {
            _diagOnStop++;
            Positions.Closed -= OnPositionClosed;

            if (_stopLossMultSamples.Count > 0)
            {
                var sorted = new List<double>(_stopLossMultSamples);
                sorted.Sort();
                _diagMultP50 = PercentileFromSorted(sorted, 0.50);
                _diagMultP90 = PercentileFromSorted(sorted, 0.90);
                _diagMultP95 = PercentileFromSorted(sorted, 0.95);
                _diagMultP99 = PercentileFromSorted(sorted, 0.99);
                _diagMultMax = sorted[sorted.Count - 1];
            }

            Print(
                "DIAG_SUMMARY | CodeName={0} | Symbol={1} | OnStart={2} OnStop={3} OnTick={4} OnTimer={5} OnBar={6} | " +
                "OnBarRet_BarsCount={7} ForceFlat={8} NotAllowNewEntries={9} NewsWindow={10} MaxPos={11} NoPivot={12} | " +
                "PivotUpdateCalls={13} TryEntryCalled={14} | PlaceTradeCalled={15} Ret_Risk$<=0={16} Ret_Balance<=0={17} Ret_SLDist<=0={18} Ret_SLPips<=0={19} Ret_SLBelowMin={20} Ret_VolBelowMin={21} Ret_VolAboveCap={22} CapApplied={23} Ret_TPInvalidNoFallback={24} | " +
                "ExecuteCalled={25} Success={26} | EmergencyClosed={27} | " +
                "RISK_DIAG Tracked={28} Closed={29} LossTrades={30} ActualLoss>1200={31} MaxExpectedAtSL$={32} MaxActualLoss$={33} MaxActualLossTimeUTC={34:o} | " +
                "STOP_DIAG StopClosed={35} Mult_P50={36} P90={37} P95={38} P99={39} Max={40} | " +
                "CLOSE_REASON StopLoss={41} TakeProfit={42} Other={43} Unknown={44}",
                CODE_NAME, SymbolName,
                _diagOnStart, _diagOnStop, _diagOnTick, _diagOnTimer, _diagOnBar,
                _diagOnBarRet_BarsCount, _diagOnBarRet_ForceFlat, _diagOnBarRet_NotAllowNewEntries, _diagOnBarRet_NewsWindow, _diagOnBarRet_MaxPositions, _diagOnBarRet_NoPivot,
                _diagPivotUpdateCalls, _diagTryPivotBounceEntryCalled,
                _diagPlaceTradeCalled, _diagPlaceTradeRet_RiskDollarLE0, _diagPlaceTradeRet_BalanceLE0, _diagPlaceTradeRet_SLDistLE0, _diagPlaceTradeRet_SLPipsLE0, _diagPlaceTradeRet_SLBelowMin, _diagPlaceTradeRet_VolBelowMin, _diagPlaceTradeRet_VolAboveCap, _diagCapApplied, _diagPlaceTradeRet_TPInvalidNoFallback,
                _diagExecuteCalled, _diagSuccess,
                _diagEmergencyClosed,
                _diagRiskTracked, _diagRiskClosed, _diagRiskLossTrades, _diagRiskActualLossOver1200,
                (_diagRiskMaxExpectedLossAtSl > double.NegativeInfinity ? _diagRiskMaxExpectedLossAtSl : double.NaN),
                (_diagRiskMaxActualLoss > double.NegativeInfinity ? _diagRiskMaxActualLoss : double.NaN),
                (_diagRiskMaxActualLossTimeUtc != DateTime.MinValue ? _diagRiskMaxActualLossTimeUtc : DateTime.MinValue),
                _diagStopClosed, _diagMultP50, _diagMultP90, _diagMultP95, _diagMultP99, _diagMultMax,
                _diagCloseReason_StopLoss, _diagCloseReason_TakeProfit, _diagCloseReason_ManualOrOther, _diagCloseReason_Unknown
            );
        }

        protected override void OnTimer()
        {
            _diagOnTimer++;

            // ---- Emergency close sweep (with retry) ----
            EmergencyCloseSweep();

            if (!EnableTradingWindowFilter)
                return;

            DateTime utcNow = Server.Time;
            DateTime jstNow = ToJst(utcNow);
            TradingWindowState state = GetTradingWindowState(jstNow);

            if (state == TradingWindowState.ForceFlat)
            {
                _diagOnBarRet_ForceFlat++;
                CloseAllPositionsOnThisSymbol("FORCE_CLOSE_WINDOW(JST)");
            }
        }

        protected override void OnTick()
        {
            _diagOnTick++;
            ApplyBreakevenMoveIfNeeded();
        }

        protected override void OnBar()
        {
            _diagOnBar++;

            if (Bars.Count < 50)
            {
                _diagOnBarRet_BarsCount++;
                return;
            }

            DateTime utcNow = Server.Time;

            if (EnableTradingWindowFilter)
            {
                DateTime jstNow = ToJst(utcNow);
                TradingWindowState state = GetTradingWindowState(jstNow);

                if (state == TradingWindowState.ForceFlat)
                {
                    _diagOnBarRet_ForceFlat++;
                    return;
                }

                if (state != TradingWindowState.AllowNewEntries)
                {
                    _diagOnBarRet_NotAllowNewEntries++;
                    return;
                }
            }

            if (EnableNewsFilter && IsInNewsWindow(utcNow))
            {
                _diagOnBarRet_NewsWindow++;
                return;
            }

            if (Positions.Count >= MaxPositions)
            {
                _diagOnBarRet_MaxPositions++;
                return;
            }

            UpdateDailyPivotIfNeeded(utcNow);

            if (!_hasPivot)
            {
                _diagOnBarRet_NoPivot++;
                return;
            }

            TryPivotBounceEntry();
        }

        // ================= Emergency Close =================
        private void EmergencyCloseSweep()
        {
            DateTime utcNow = Server.Time;

            if (_lastEmergencySweepUtc != DateTime.MinValue)
            {
                if ((utcNow - _lastEmergencySweepUtc).TotalSeconds < Math.Max(1, EmergencyCloseRetryIntervalSec))
                    return;
            }
            _lastEmergencySweepUtc = utcNow;

            double risk = Math.Max(0.0, RiskDollars);
            if (risk <= 0.0)
                return;

            double thr = -risk * Math.Max(1.0, EmergencyCloseMult);

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;

                // trigger
                if (p.NetProfit <= thr)
                {
                    long id = p.Id;

                    if (!_emgRetryLeftByPosId.ContainsKey(id))
                        _emgRetryLeftByPosId[id] = Math.Max(1, EmergencyCloseRetryMax);

                    if (_emgRetryLeftByPosId[id] <= 0)
                        continue;

                    _emgRetryLeftByPosId[id]--;

                    ClosePosition(p);
                    _diagEmergencyClosed++;

                    Print("EMERGENCY_CLOSE | CodeName={0} | PosId={1} | NetProfit$={2} <= Thr$={3} | RetryLeft={4}",
                        CODE_NAME, id,
                        p.NetProfit.ToString("F2", CultureInfo.InvariantCulture),
                        thr.ToString("F2", CultureInfo.InvariantCulture),
                        _emgRetryLeftByPosId[id]);
                }
            }
        }

        // ================= Position Closed DIAG =================
        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (args == null) return;
            Position p = args.Position;
            if (p == null) return;

            string reason = GetCloseReasonSafe(args, p);

            if (reason == "StopLoss") _diagCloseReason_StopLoss++;
            else if (reason == "TakeProfit") _diagCloseReason_TakeProfit++;
            else if (reason == "Unknown") _diagCloseReason_Unknown++;
            else _diagCloseReason_ManualOrOther++;

            _diagRiskClosed++;

            double actualLoss = Math.Max(0.0, -p.NetProfit);
            if (actualLoss > 0.0)
            {
                _diagRiskLossTrades++;
                if (actualLoss > 1200.0) _diagRiskActualLossOver1200++;

                if (actualLoss > _diagRiskMaxActualLoss)
                {
                    _diagRiskMaxActualLoss = actualLoss;
                    _diagRiskMaxActualLossTimeUtc = DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);
                }
            }

            if (reason == "StopLoss")
            {
                if (_expectedLossAtSlByPositionId.TryGetValue(p.Id, out double exp) && exp > 0.0)
                {
                    double loss = Math.Max(0.0, -p.NetProfit);
                    if (loss > 0.0)
                    {
                        _stopLossMultSamples.Add(loss / exp);
                        _diagStopClosed++;
                    }
                }
            }

            _expectedLossAtSlByPositionId.Remove(p.Id);
            _emgRetryLeftByPosId.Remove(p.Id);
        }

        // ================= BREAKEVEN =================
        private void ApplyBreakevenMoveIfNeeded()
        {
            double trigger = Math.Max(0.0, BreakevenTriggerDollars);
            if (trigger <= 0.0) return;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                if (p.NetProfit < trigger) continue;

                double entry = p.EntryPrice;

                if (p.TradeType == TradeType.Buy)
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value >= entry) continue;
                    ModifyPosition(p, entry, p.TakeProfit);
                }
                else
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value <= entry) continue;
                    ModifyPosition(p, entry, p.TakeProfit);
                }
            }
        }

        // ================= PIVOT =================
        private void UpdateDailyPivotIfNeeded(DateTime utcNow)
        {
            _diagPivotUpdateCalls++;

            if (_nyTz == null)
            {
                _hasPivot = false;
                return;
            }

            DateTime sessionStartUtc = GetNySessionStartUtc(utcNow);
            if (sessionStartUtc == _currentPivotSessionStartUtc && _hasPivot)
                return;

            DateTime prevStartUtc = sessionStartUtc.AddDays(-1);

            if (TryGetSessionHlc(prevStartUtc, sessionStartUtc, out double high, out double low, out double close))
            {
                _currentPivotSessionStartUtc = sessionStartUtc;

                _pp = (high + low + close) / 3.0;

                _r1 = 2.0 * _pp - low;
                _s1 = 2.0 * _pp - high;

                double range = high - low;

                _r2 = _pp + range;
                _s2 = _pp - range;

                _r3 = high + 2.0 * (_pp - low);
                _s3 = low - 2.0 * (high - _pp);

                _r4 = _r3 + range;
                _s4 = _s3 - range;

                _hasPivot = true;
            }
            else
            {
                _currentPivotSessionStartUtc = sessionStartUtc;
                _hasPivot = false;
            }
        }

        private DateTime GetNySessionStartUtc(DateTime utcNow)
        {
            DateTime utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            DateTime nyLocal = TimeZoneInfo.ConvertTimeFromUtc(utc, _nyTz);

            DateTime nySessionStartLocal;

            if (nyLocal.Hour > 17 || (nyLocal.Hour == 17 && nyLocal.Minute >= 0))
                nySessionStartLocal = new DateTime(nyLocal.Year, nyLocal.Month, nyLocal.Day, 17, 0, 0, DateTimeKind.Unspecified);
            else
            {
                DateTime d = nyLocal.Date.AddDays(-1);
                nySessionStartLocal = new DateTime(d.Year, d.Month, d.Day, 17, 0, 0, DateTimeKind.Unspecified);
            }

            DateTime nySessionStartUtc = TimeZoneInfo.ConvertTimeToUtc(nySessionStartLocal, _nyTz);
            return DateTime.SpecifyKind(nySessionStartUtc, DateTimeKind.Utc);
        }

        private bool TryGetSessionHlc(DateTime startUtc, DateTime endUtc, out double high, out double low, out double close)
        {
            high = double.MinValue;
            low = double.MaxValue;
            close = 0.0;

            Bars minuteBars = MarketData.GetBars(TimeFrame.Minute);
            if (minuteBars == null || minuteBars.Count < 10) return false;

            bool hasAny = false;

            for (int i = 0; i < minuteBars.Count; i++)
            {
                DateTime t = minuteBars.OpenTimes[i];
                if (t < startUtc) continue;
                if (t >= endUtc) break;

                double h = minuteBars.HighPrices[i];
                double l = minuteBars.LowPrices[i];

                if (!hasAny)
                {
                    hasAny = true;
                    high = h;
                    low = l;
                    close = minuteBars.ClosePrices[i];
                }
                else
                {
                    if (h > high) high = h;
                    if (l < low) low = l;
                    close = minuteBars.ClosePrices[i];
                }
            }

            return hasAny;
        }

        // ================= ENTRY =================
        private void TryPivotBounceEntry()
        {
            _diagTryPivotBounceEntryCalled++;

            double bufferPrice = (PivotBufferPips * 10.0) * Symbol.PipSize;
            const int WAIT_BARS_AFTER_TOUCH = 3;

            if (!SymbolInfoTick(out double bid, out double ask))
                return;

            if (UseS1R1Only)
            {
                if (IsBuyBounceAfterWait(_s1, bufferPrice, WAIT_BARS_AFTER_TOUCH))
                {
                    PlaceTrade(TradeType.Buy, ask, _s1 - bufferPrice, _pp, "PIVOT_BOUNCE_S1_TP_PP");
                    return;
                }

                if (IsSellBounceAfterWait(_r1, bufferPrice, WAIT_BARS_AFTER_TOUCH))
                {
                    PlaceTrade(TradeType.Sell, bid, _r1 + bufferPrice, _pp, "PIVOT_BOUNCE_R1_TP_PP");
                    return;
                }
                return;
            }
        }

        private bool IsBuyBounceAfterWait(double supportPrice, double bufferPrice, int waitBars)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0) return false;

            double touchLine = supportPrice + bufferPrice;

            int lastTouchIndex = -1;
            for (int i = signalIndex; i >= 0; i--)
            {
                if (Bars.LowPrices[i] <= touchLine) { lastTouchIndex = i; break; }
            }
            if (lastTouchIndex < 0) return false;
            if (signalIndex - lastTouchIndex < waitBars) return false;

            return Bars.ClosePrices[signalIndex] > touchLine;
        }

        private bool IsSellBounceAfterWait(double resistancePrice, double bufferPrice, int waitBars)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0) return false;

            double touchLine = resistancePrice - bufferPrice;

            int lastTouchIndex = -1;
            for (int i = signalIndex; i >= 0; i--)
            {
                if (Bars.HighPrices[i] >= touchLine) { lastTouchIndex = i; break; }
            }
            if (lastTouchIndex < 0) return false;
            if (signalIndex - lastTouchIndex < waitBars) return false;

            return Bars.ClosePrices[signalIndex] < touchLine;
        }

        // ================= EXECUTION (FIXED PipValue semantics) =================
        private void PlaceTrade(TradeType type, double entry, double stop, double tpTargetPrice, string reasonTag)
        {
            _diagPlaceTradeCalled++;

            double riskDollars = Math.Max(0.0, RiskDollars);
            if (riskDollars <= 0.0) { _diagPlaceTradeRet_RiskDollarLE0++; return; }

            double balance = Account.Balance;
            if (balance <= 0.0) { _diagPlaceTradeRet_BalanceLE0++; return; }

            double slDistancePrice = Math.Abs(entry - stop);
            if (slDistancePrice <= 0.0) { _diagPlaceTradeRet_SLDistLE0++; return; }

            double slPips = slDistancePrice / Symbol.PipSize;
            if (slPips <= 0.0) { _diagPlaceTradeRet_SLPipsLE0++; return; }

            // ---- Min SL guard ----
            double minSlPipsFromPips = Math.Max(0.0, MinSLPips) * 10.0;
            double minSlPriceFromPips = minSlPipsFromPips * Symbol.PipSize;

            double atrValue = (_atrMinSl != null && _atrMinSl.Result != null && _atrMinSl.Result.Count > 0) ? _atrMinSl.Result.LastValue : 0.0;
            double minSlPriceFromAtr = atrValue * Math.Max(0.0, MinSlAtrMult);

            double minSlPriceFinal = Math.Max(minSlPriceFromPips, minSlPriceFromAtr);
            if (minSlPriceFinal > 0.0 && slDistancePrice < minSlPriceFinal)
            {
                _diagPlaceTradeRet_SLBelowMin++;
                return;
            }

            // ---- sizing pips includes RiskBufferPips (ct pips) ----
            double sizingPips = slPips + Math.Max(0.0, RiskBufferPips);

            // ---- AUTO-DETECT PipValue semantics and compute volume consistently ----
            long volumeInUnits = ComputeVolumeUnits_ByAutoPipValueSemantics(riskDollars, sizingPips, out double pipValueForVolume, out double expectedAtSl);

            if (volumeInUnits < Symbol.VolumeInUnitsMin)
            {
                _diagPlaceTradeRet_VolBelowMin++;
                return;
            }

            // ---- Max lots cap ----
            if (MaxLotsCap > 0.0)
            {
                double maxUnitsNorm = Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MaxLotsCap), RoundingMode.Down);
                long maxUnits = (long)maxUnitsNorm;

                if (maxUnits > 0 && volumeInUnits > maxUnits)
                {
                    _diagPlaceTradeRet_VolAboveCap++;
                    _diagCapApplied++;
                    volumeInUnits = maxUnits;

                    // recompute expected with capped volume
                    expectedAtSl = sizingPips * pipValueForVolume;
                }
            }

            // ---- TP validity (same rule as your base) ----
            double tpPipsFromTarget = 0.0;
            bool targetValid = false;

            if (type == TradeType.Buy && tpTargetPrice > entry)
            {
                tpPipsFromTarget = (tpTargetPrice - entry) / Symbol.PipSize;
                targetValid = tpPipsFromTarget > 0;
            }
            else if (type == TradeType.Sell && tpTargetPrice < entry)
            {
                tpPipsFromTarget = (entry - tpTargetPrice) / Symbol.PipSize;
                targetValid = tpPipsFromTarget > 0;
            }

            if (!targetValid)
            {
                _diagPlaceTradeRet_TPInvalidNoFallback++;
                return;
            }

            double tpPips = tpPipsFromTarget;

            _diagExecuteCalled++;

            TradeResult result = ExecuteMarketOrder(
                type,
                SymbolName,
                volumeInUnits,
                "PIVOT_BOUNCE_v0_1_0",
                slPips,
                tpPips
            );

            if (result != null && result.IsSuccessful)
            {
                _diagSuccess++;

                _diagRiskTracked++;

                // track expected for STOP_DIAG (corrected)
                if (result.Position != null)
                {
                    _expectedLossAtSlByPositionId[result.Position.Id] = expectedAtSl;
                    if (expectedAtSl > _diagRiskMaxExpectedLossAtSl)
                        _diagRiskMaxExpectedLossAtSl = expectedAtSl;
                }

                Print("RISK_EXPECTED | CodeName={0} | Type={1} | SLPips={2} | BufferPips={3} | SizingPips={4} | VolUnits={5} | Lots={6} | PipValueForVol={7} | ExpectedAtSL$={8} | Risk$={9}",
                    CODE_NAME,
                    type,
                    slPips.ToString("F2", CultureInfo.InvariantCulture),
                    Math.Max(0.0, RiskBufferPips).ToString("F2", CultureInfo.InvariantCulture),
                    sizingPips.ToString("F2", CultureInfo.InvariantCulture),
                    volumeInUnits,
                    Symbol.VolumeInUnitsToQuantity(volumeInUnits).ToString("F4", CultureInfo.InvariantCulture),
                    pipValueForVolume.ToString("G17", CultureInfo.InvariantCulture),
                    expectedAtSl.ToString("F2", CultureInfo.InvariantCulture),
                    riskDollars.ToString("F2", CultureInfo.InvariantCulture)
                );
            }
        }

        /// <summary>
        /// Computes volume units by auto-detecting whether Symbol.PipValue is per-lot or per-unit.
        /// Returns pipValueForVolume = $ per pip for the computed volume.
        /// expectedAtSl is computed as sizingPips * pipValueForVolume (i.e., expected loss at SL+buffer in dollars).
        /// </summary>
        private long ComputeVolumeUnits_ByAutoPipValueSemantics(double riskDollars, double sizingPips, out double pipValueForVolume, out double expectedAtSl)
        {
            pipValueForVolume = 0.0;
            expectedAtSl = 0.0;

            double pv = Symbol.PipValue;
            double unitsPerLot = SafeUnitsPerLot();
            if (pv <= 0.0 || unitsPerLot <= 0.0 || sizingPips <= 0.0)
                return 0;

            // Candidate A: PipValue is per-LOT
            // risk = sizingPips * pv * (volUnits / unitsPerLot)
            double volUnitsA = (riskDollars * unitsPerLot) / (sizingPips * pv);

            // Candidate B: PipValue is per-UNIT
            // risk = sizingPips * pv * volUnits
            double volUnitsB = riskDollars / (sizingPips * pv);

            // Normalize each
            long normA = (long)Symbol.NormalizeVolumeInUnits(volUnitsA);
            long normB = (long)Symbol.NormalizeVolumeInUnits(volUnitsB);

            // Compute implied expected loss under each model (using same sizingPips)
            double pvForA = pv * (normA / unitsPerLot); // $/pip for normA
            double expA = sizingPips * pvForA;

            double pvForB = pv * normB;                 // $/pip for normB
            double expB = sizingPips * pvForB;

            // Choose the model whose expected loss is closer to riskDollars (before caps).
            double da = Math.Abs(expA - riskDollars);
            double db = Math.Abs(expB - riskDollars);

            if (double.IsNaN(da) || double.IsInfinity(da)) da = double.MaxValue;
            if (double.IsNaN(db) || double.IsInfinity(db)) db = double.MaxValue;

            if (da <= db)
            {
                pipValueForVolume = pvForA;
                expectedAtSl = expA;
                return normA;
            }
            else
            {
                pipValueForVolume = pvForB;
                expectedAtSl = expB;
                return normB;
            }
        }

        private double SafeUnitsPerLot()
        {
            try
            {
                double u = Symbol.QuantityToVolumeInUnits(1.0);
                if (u > 0.0 && !double.IsNaN(u) && !double.IsInfinity(u))
                    return u;
            }
            catch { }
            return 1.0;
        }

        // ================= FORCE CLOSE =================
        private void CloseAllPositionsOnThisSymbol(string reason)
        {
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                ClosePosition(p);
            }
            Print("FORCE CLOSE | CodeName={0} | Symbol={1} | Reason={2}", CODE_NAME, SymbolName, reason);
        }

        // ================= TRADING WINDOW (JST) =================
        private TradingWindowState GetTradingWindowState(DateTime jstNow)
        {
            int nowMin = jstNow.Hour * 60 + jstNow.Minute;
            int startMin = NormalizeMinutes(TradeStartHourJst, TradeStartMinuteJst);
            int endMin = NormalizeMinutes(TradeEndHourJst, TradeEndMinuteJst);
            int forceMin = NormalizeMinutes(ForceCloseHourJst, ForceCloseMinuteJst);

            if (IsInRangeCircular(nowMin, startMin, endMin)) return TradingWindowState.AllowNewEntries;
            if (IsInRangeCircular(nowMin, endMin, forceMin)) return TradingWindowState.HoldOnly;
            return TradingWindowState.ForceFlat;
        }

        private bool IsInRangeCircular(int nowMin, int startMin, int endMin)
        {
            if (startMin == endMin) return false;
            if (startMin < endMin) return nowMin >= startMin && nowMin < endMin;
            return nowMin >= startMin || nowMin < endMin;
        }

        private int NormalizeMinutes(int hour, int minute)
        {
            int h = Math.Max(0, Math.Min(23, hour));
            int m = Math.Max(0, Math.Min(59, minute));
            return h * 60 + m;
        }

        private DateTime ToJst(DateTime utcNow)
        {
            DateTime utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            return _jstTz == null ? utc : TimeZoneInfo.ConvertTimeFromUtc(utc, _jstTz);
        }

        private TimeZoneInfo ResolveTokyoTimeZone()
        {
            string[] ids = { "Tokyo Standard Time", "Asia/Tokyo" };
            foreach (var id in ids)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            }
            return TimeZoneInfo.Utc;
        }

        private TimeZoneInfo ResolveNewYorkTimeZone()
        {
            string[] ids = { "Eastern Standard Time", "America/New_York" };
            foreach (var id in ids)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            }
            return null;
        }

        // ================= NEWS FILTER =================
        private void LoadEconomicCalendarUtc(string raw)
        {
            _highImpactEventsUtc.Clear();
            if (string.IsNullOrWhiteSpace(raw)) return;

            string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string lineRaw in lines)
            {
                string line = lineRaw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Contains("DateTime") && line.Contains("Event") && line.Contains("Importance")) continue;

                if (line.Length >= 2 && line[0] == '"' && line[line.Length - 1] == '"')
                    line = line.Substring(1, line.Length - 2);

                string[] parts = line.Split(new[] { ',' }, 3);
                if (parts.Length < 1) continue;

                string dtText = parts[0].Trim();
                if (TryParseUtcDateTime(dtText, out DateTime dtUtc))
                {
                    if (!_highImpactEventsUtc.Contains(dtUtc))
                        _highImpactEventsUtc.Add(dtUtc);
                }
            }
            _highImpactEventsUtc.Sort();
        }

        private bool TryParseUtcDateTime(string text, out DateTime utc)
        {
            utc = default(DateTime);
            string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm" };

            if (DateTime.TryParseExact(
                text, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime parsed))
            {
                utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                return true;
            }
            return false;
        }

        private bool IsInNewsWindow(DateTime utcNow)
        {
            if (_highImpactEventsUtc.Count == 0) return false;

            int before = Math.Max(0, MinutesBeforeNews);
            int after = Math.Max(0, MinutesAfterNews);

            foreach (var e in _highImpactEventsUtc)
            {
                DateTime start = e.AddMinutes(-before);
                DateTime end = e.AddMinutes(after);
                if (utcNow >= start && utcNow <= end) return true;
            }
            return false;
        }

        // ================= PRICE FETCH =================
        private bool SymbolInfoTick(out double bid, out double ask)
        {
            bid = 0.0; ask = 0.0;
            try
            {
                bid = Symbol.Bid;
                ask = Symbol.Ask;
                return bid > 0.0 && ask > 0.0;
            }
            catch { return false; }
        }

        // ================= STOP_DIAG helpers =================
        private double PercentileFromSorted(List<double> sortedAsc, double p01)
        {
            if (sortedAsc == null || sortedAsc.Count == 0) return double.NaN;
            if (p01 <= 0.0) return sortedAsc[0];
            if (p01 >= 1.0) return sortedAsc[sortedAsc.Count - 1];

            double idx = (sortedAsc.Count - 1) * p01;
            int i0 = (int)Math.Floor(idx);
            int i1 = (int)Math.Ceiling(idx);
            if (i0 == i1) return sortedAsc[i0];

            double w = idx - i0;
            return sortedAsc[i0] * (1.0 - w) + sortedAsc[i1] * w;
        }

        // ================= CLOSE_REASON helpers =================
        private string GetCloseReasonSafe(PositionClosedEventArgs args, Position p)
        {
            try
            {
                if (args != null)
                {
                    var ta = args.GetType();
                    var pa = ta.GetProperty("Reason") ?? ta.GetProperty("CloseReason") ?? ta.GetProperty("ClosingReason");
                    if (pa != null)
                    {
                        object v = pa.GetValue(args, null);
                        if (v != null) return NormalizeCloseReason(v.ToString());
                    }
                }
                if (p != null)
                {
                    var t = p.GetType();
                    var pp = t.GetProperty("CloseReason") ?? t.GetProperty("Reason") ?? t.GetProperty("ClosingReason");
                    if (pp != null)
                    {
                        object v = pp.GetValue(p, null);
                        if (v != null) return NormalizeCloseReason(v.ToString());
                    }
                }
            }
            catch { }
            return "Unknown";
        }

        private string NormalizeCloseReason(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Unknown";
            string s = raw.Trim();
            if (s.IndexOf("StopLoss", StringComparison.OrdinalIgnoreCase) >= 0) return "StopLoss";
            if (s.IndexOf("TakeProfit", StringComparison.OrdinalIgnoreCase) >= 0) return "TakeProfit";
            return "Other";
        }
    }
}
