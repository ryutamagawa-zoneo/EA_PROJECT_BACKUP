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
        // ============================================================
        // CODE NAME (for your file naming / traceability)
        // ============================================================
        private const string CODE_NAME = "PIVOT_BOUNCE_M5_ALL_DAY_001";
        // ============================================================

        // ================= PARAMETERS =================

        [Parameter("Risk Per Trade (%)", DefaultValue = 0.5)]
        public double RiskPercent { get; set; }

        // FIX: RiskReward is unused. DefaultValue must be 0.
        [Parameter("Risk Reward", DefaultValue = 0)]
        public double RiskReward { get; set; }

        [Parameter("Max Positions", DefaultValue = 1, MinValue = 1)]
        public int MaxPositions { get; set; }

        // ===== SL Breakeven Move (Profit in $) =====

        [Parameter("BE Move Trigger ($)", DefaultValue = 1000.0, MinValue = 0.0)]
        public double BreakevenTriggerDollars { get; set; }

        // ===== Pivot (Daily, NY Close @ 17:00 NY) =====

        // NOTE (Unit Alignment for XAUUSD):
        // This parameter is treated as "User PIPS" where 1 PIPS = $0.1 (common FX trader convention for Gold).
        // Internally, cTrader "pip" is typically $0.01, so we convert by x10 when used.
        [Parameter("Pivot Bounce Buffer (PIPS=0.1$)", DefaultValue = 2.0, MinValue = 0.0)]
        public double PivotBufferPips { get; set; }

        [Parameter("Use S1/R1 Bounce Only", DefaultValue = true)]
        public bool UseS1R1Only { get; set; }

        // ===== Trading Window (JST) =====

        [Parameter("Enable Trading Window (JST)", DefaultValue = true)]
        public bool EnableTradingWindowFilter { get; set; }

        // DEFAULTS: Start 09:15
        [Parameter("Trade Start Hour (JST)", DefaultValue = 9, MinValue = 0, MaxValue = 23)]
        public int TradeStartHourJst { get; set; }

        [Parameter("Trade Start Minute (JST)", DefaultValue = 15, MinValue = 0, MaxValue = 59)]
        public int TradeStartMinuteJst { get; set; }

        // DEFAULTS: End 02:00
        [Parameter("Trade End Hour (JST)", DefaultValue = 2, MinValue = 0, MaxValue = 23)]
        public int TradeEndHourJst { get; set; }

        [Parameter("Trade End Minute (JST)", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TradeEndMinuteJst { get; set; }

        // DEFAULTS: ForceClose 02:50
        [Parameter("Force Close Hour (JST)", DefaultValue = 2, MinValue = 0, MaxValue = 23)]
        public int ForceCloseHourJst { get; set; }

        [Parameter("Force Close Minute (JST)", DefaultValue = 50, MinValue = 0, MaxValue = 59)]
        public int ForceCloseMinuteJst { get; set; }

        // ===== Guards (parameterized) =====

        // DEFAULTS: MaxLot 2.5
        [Parameter("Max Lots Cap (0=Off)", DefaultValue = 2.5, MinValue = 0.0)]
        public double MaxLotsCap { get; set; }

        // NOTE (Unit Alignment for XAUUSD):
        // This parameter is treated as "User PIPS" where 1 PIPS = $0.1 (common FX trader convention for Gold).
        // Internally, cTrader "pip" is typically $0.01, so we convert by x10 when used.
        [Parameter("Min SL (PIPS=0.1$)", DefaultValue = 50.0, MinValue = 0.0)]
        public double MinSLPips { get; set; }

        [Parameter("Min SL ATR Period", DefaultValue = 14, MinValue = 1)]
        public int MinSlAtrPeriod { get; set; }

        [Parameter("Min SL ATR Mult", DefaultValue = 0.5, MinValue = 0.0)]
        public double MinSlAtrMult { get; set; }

        // ===== Optional News Filter (UTC list stub) =====

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

        private DateTime _currentPivotSessionStartUtc = DateTime.MinValue; // NY 17:00 boundary (current session start)
        private bool _hasPivot;

        // Classic Pivot Levels (Extended)
        private double _pp;
        private double _r1;
        private double _r2;
        private double _r3;
        private double _r4;
        private double _s1;
        private double _s2;
        private double _s3;
        private double _s4;

        // ================= DIAG (MINIMAL COUNTERS ONLY) =================

        private long _diagOnStart;
        private long _diagOnStop;
        private long _diagOnTick;
        private long _diagOnTimer;
        private long _diagOnBar;

        private long _diagOnBarRet_BarsCount;
        private long _diagOnBarRet_ForceFlat;
        private long _diagOnBarRet_NotAllowNewEntries;
        private long _diagOnBarRet_NewsWindow;
        private long _diagOnBarRet_MaxPositions;
        private long _diagOnBarRet_NoPivot;

        private long _diagPivotUpdateCalls;
        private long _diagTryPivotBounceEntryCalled;

        private long _diagPlaceTradeCalled;
        private long _diagPlaceTradeRet_RiskPctLE0;
        private long _diagPlaceTradeRet_BalanceLE0;
        private long _diagPlaceTradeRet_RiskDollarLE0;
        private long _diagPlaceTradeRet_SLDistLE0;
        private long _diagPlaceTradeRet_SLPipsLE0;
        private long _diagPlaceTradeRet_SLBelowMin;
        private long _diagPlaceTradeRet_VolBelowMin;
        private long _diagPlaceTradeRet_VolAboveCap;
        private long _diagPlaceTradeRet_TPInvalidNoFallback;

        private long _diagExecuteCalled;
        private long _diagSuccess;

        // ===== DIAG EXT (TPInvalid Buy/Sell + Tag breakdown + Samples) =====

        private long _diagTPInvalid_Buy;
        private long _diagTPInvalid_Sell;

        // Tag: S1 / R1 only (minimal)
        private long _diagTagCalled_S1;
        private long _diagTagTPInvalid_S1;
        private long _diagTagExec_S1;
        private long _diagTagSucc_S1;

        private long _diagTagCalled_R1;
        private long _diagTagTPInvalid_R1;
        private long _diagTagExec_R1;
        private long _diagTagSucc_R1;

        // Samples: R1 Sell TPInvalid
        private long _diagR1Sell_TPInv_Samples;
        private double _diagR1Sell_TPInv_MinEntry = double.PositiveInfinity;
        private double _diagR1Sell_TPInv_MaxEntry = double.NegativeInfinity;
        private double _diagR1Sell_TPInv_MinPP = double.PositiveInfinity;
        private double _diagR1Sell_TPInv_MaxPP = double.NegativeInfinity;
        private double _diagR1Sell_TPInv_MinR1 = double.PositiveInfinity;
        private double _diagR1Sell_TPInv_MaxR1 = double.NegativeInfinity;
        private DateTime _diagR1Sell_TPInv_FirstSessionStartUtc = DateTime.MinValue;
        private DateTime _diagR1Sell_TPInv_LastSessionStartUtc = DateTime.MinValue;

        // Samples: S1 Buy TPInvalid
        private long _diagS1Buy_TPInv_Samples;
        private double _diagS1Buy_TPInv_MinEntry = double.PositiveInfinity;
        private double _diagS1Buy_TPInv_MaxEntry = double.NegativeInfinity;
        private double _diagS1Buy_TPInv_MinPP = double.PositiveInfinity;
        private double _diagS1Buy_TPInv_MaxPP = double.NegativeInfinity;
        private double _diagS1Buy_TPInv_MinS1 = double.PositiveInfinity;
        private double _diagS1Buy_TPInv_MaxS1 = double.NegativeInfinity;
        private DateTime _diagS1Buy_TPInv_FirstSessionStartUtc = DateTime.MinValue;
        private DateTime _diagS1Buy_TPInv_LastSessionStartUtc = DateTime.MinValue;

        // ================= ENUMS =================

        private enum TradingWindowState
        {
            AllowNewEntries = 0,
            HoldOnly = 1,
            ForceFlat = 2
        }

        private enum PivotTouchLevel
        {
            None = 0,
            PP = 1,
            R1 = 2,
            R2 = 3,
            R3 = 4,
            R4 = 5,
            S1 = 6,
            S2 = 7,
            S3 = 8,
            S4 = 9
        }

        private enum DiagTag
        {
            None = 0,
            S1 = 1,
            R1 = 2
        }

        // ================= LIFECYCLE =================

        protected override void OnStart()
        {
            _diagOnStart++;

            _jstTz = ResolveTokyoTimeZone();
            _nyTz = ResolveNewYorkTimeZone();

            _atrMinSl = Indicators.AverageTrueRange(MinSlAtrPeriod, MovingAverageType.Simple);

            // Force-close supervision (seconds)
            Timer.Start(1);

            // EconomicCalendar is treated as UTC (stub list, High-impact only).
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

            Print(
                "Started. cBot={0} Symbol={1} | CodeName={2} | Window(JST) Start={3:D2}:{4:D2} End={5:D2}:{6:D2} ForceClose={7:D2}:{8:D2} | Pivot=Classic(Daily NY17:00) | Guards: MinSLPips={9} ATRPeriod={10} ATRMult={11} MaxLotsCap={12} | BETrigger$={13}",
                nameof(HIVE_PivotBounce_v0_1_0),
                SymbolName,
                CODE_NAME,
                TradeStartHourJst, TradeStartMinuteJst,
                TradeEndHourJst, TradeEndMinuteJst,
                ForceCloseHourJst, ForceCloseMinuteJst,
                MinSLPips.ToString("F2", CultureInfo.InvariantCulture),
                MinSlAtrPeriod,
                MinSlAtrMult.ToString("F2", CultureInfo.InvariantCulture),
                MaxLotsCap.ToString("F2", CultureInfo.InvariantCulture),
                BreakevenTriggerDollars.ToString("F2", CultureInfo.InvariantCulture)
            );
        }

        protected override void OnStop()
        {
            _diagOnStop++;

            Print(
                "DIAG_SUMMARY | CodeName={0} | Symbol={1} | OnStart={2} OnStop={3} OnTick={4} OnTimer={5} OnBar={6} | OnBarRet_BarsCount={7} OnBarRet_ForceFlat={8} OnBarRet_NotAllowNewEntries={9} OnBarRet_NewsWindow={10} OnBarRet_MaxPositions={11} OnBarRet_NoPivot={12} | PivotUpdateCalls={13} TryPivotBounceEntryCalled={14} | PlaceTradeCalled={15} PlaceTradeRet_RiskPct<=0={16} PlaceTradeRet_Balance<=0={17} PlaceTradeRet_Risk$<=0={18} PlaceTradeRet_SLDist<=0={19} PlaceTradeRet_SLPips<=0={20} PlaceTradeRet_SLBelowMin={21} PlaceTradeRet_VolBelowMin={22} PlaceTradeRet_VolAboveCap={23} PlaceTradeRet_TPInvalidNoFallback(RR=0)={24} | TPInvalid_Buy={25} TPInvalid_Sell={26} | ExecuteCalled={27} Success={28} | TagCalled[S1]={29} TagTPInvalid[S1]={30} TagExec[S1]={31} TagSucc[S1]={32} | TagCalled[R1]={33} TagTPInvalid[R1]={34} TagExec[R1]={35} TagSucc[R1]={36} | R1Sell_TPInv_Samples={37} | R1Sell_TPInv_MinEntry={38} R1Sell_TPInv_MaxEntry={39} | R1Sell_TPInv_MinPP={40} R1Sell_TPInv_MaxPP={41} | R1Sell_TPInv_MinR1={42} R1Sell_TPInv_MaxR1={43} | R1Sell_TPInv_FirstSessionStartUTC={44:o} R1Sell_TPInv_LastSessionStartUTC={45:o} | S1Buy_TPInv_Samples={46} | S1Buy_TPInv_MinEntry={47} S1Buy_TPInv_MaxEntry={48} | S1Buy_TPInv_MinPP={49} S1Buy_TPInv_MaxPP={50} | S1Buy_TPInv_MinS1={51} S1Buy_TPInv_MaxS1={52} | S1Buy_TPInv_FirstSessionStartUTC={53:o} S1Buy_TPInv_LastSessionStartUTC={54:o}",
                CODE_NAME,
                SymbolName,
                _diagOnStart,
                _diagOnStop,
                _diagOnTick,
                _diagOnTimer,
                _diagOnBar,
                _diagOnBarRet_BarsCount,
                _diagOnBarRet_ForceFlat,
                _diagOnBarRet_NotAllowNewEntries,
                _diagOnBarRet_NewsWindow,
                _diagOnBarRet_MaxPositions,
                _diagOnBarRet_NoPivot,
                _diagPivotUpdateCalls,
                _diagTryPivotBounceEntryCalled,
                _diagPlaceTradeCalled,
                _diagPlaceTradeRet_RiskPctLE0,
                _diagPlaceTradeRet_BalanceLE0,
                _diagPlaceTradeRet_RiskDollarLE0,
                _diagPlaceTradeRet_SLDistLE0,
                _diagPlaceTradeRet_SLPipsLE0,
                _diagPlaceTradeRet_SLBelowMin,
                _diagPlaceTradeRet_VolBelowMin,
                _diagPlaceTradeRet_VolAboveCap,
                _diagPlaceTradeRet_TPInvalidNoFallback,
                _diagTPInvalid_Buy,
                _diagTPInvalid_Sell,
                _diagExecuteCalled,
                _diagSuccess,
                _diagTagCalled_S1,
                _diagTagTPInvalid_S1,
                _diagTagExec_S1,
                _diagTagSucc_S1,
                _diagTagCalled_R1,
                _diagTagTPInvalid_R1,
                _diagTagExec_R1,
                _diagTagSucc_R1,
                _diagR1Sell_TPInv_Samples,
                (_diagR1Sell_TPInv_Samples > 0 ? _diagR1Sell_TPInv_MinEntry : double.NaN),
                (_diagR1Sell_TPInv_Samples > 0 ? _diagR1Sell_TPInv_MaxEntry : double.NaN),
                (_diagR1Sell_TPInv_Samples > 0 ? _diagR1Sell_TPInv_MinPP : double.NaN),
                (_diagR1Sell_TPInv_Samples > 0 ? _diagR1Sell_TPInv_MaxPP : double.NaN),
                (_diagR1Sell_TPInv_Samples > 0 ? _diagR1Sell_TPInv_MinR1 : double.NaN),
                (_diagR1Sell_TPInv_Samples > 0 ? _diagR1Sell_TPInv_MaxR1 : double.NaN),
                (_diagR1Sell_TPInv_Samples > 0 ? _diagR1Sell_TPInv_FirstSessionStartUtc : DateTime.MinValue),
                (_diagR1Sell_TPInv_Samples > 0 ? _diagR1Sell_TPInv_LastSessionStartUtc : DateTime.MinValue),
                _diagS1Buy_TPInv_Samples,
                (_diagS1Buy_TPInv_Samples > 0 ? _diagS1Buy_TPInv_MinEntry : double.NaN),
                (_diagS1Buy_TPInv_Samples > 0 ? _diagS1Buy_TPInv_MaxEntry : double.NaN),
                (_diagS1Buy_TPInv_Samples > 0 ? _diagS1Buy_TPInv_MinPP : double.NaN),
                (_diagS1Buy_TPInv_Samples > 0 ? _diagS1Buy_TPInv_MaxPP : double.NaN),
                (_diagS1Buy_TPInv_Samples > 0 ? _diagS1Buy_TPInv_MinS1 : double.NaN),
                (_diagS1Buy_TPInv_Samples > 0 ? _diagS1Buy_TPInv_MaxS1 : double.NaN),
                (_diagS1Buy_TPInv_Samples > 0 ? _diagS1Buy_TPInv_FirstSessionStartUtc : DateTime.MinValue),
                (_diagS1Buy_TPInv_Samples > 0 ? _diagS1Buy_TPInv_LastSessionStartUtc : DateTime.MinValue)
            );
        }

        protected override void OnTimer()
        {
            _diagOnTimer++;

            if (!EnableTradingWindowFilter)
                return;

            DateTime utcNow = Server.Time;
            DateTime jstNow = ToJst(utcNow);

            TradingWindowState state = GetTradingWindowState(jstNow);

            if (state == TradingWindowState.ForceFlat)
            {
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

                // ForceFlat is handled in OnTimer, but keep it safe here too.
                if (state == TradingWindowState.ForceFlat)
                {
                    _diagOnBarRet_ForceFlat++;
                    return;
                }

                // After End, new entries are prohibited.
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

        // ================= BREAKEVEN (SL move to entry by $ profit) =================

        private void ApplyBreakevenMoveIfNeeded()
        {
            double trigger = Math.Max(0.0, BreakevenTriggerDollars);
            if (trigger <= 0.0)
                return;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null)
                    continue;

                if (p.SymbolName != SymbolName)
                    continue;

                if (p.NetProfit < trigger)
                    continue;

                double entry = p.EntryPrice;

                if (p.TradeType == TradeType.Buy)
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value >= entry)
                        continue;

                    TryModifyStopLossTo(p, entry, "BE_MOVE_BUY");
                }
                else
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value <= entry)
                        continue;

                    TryModifyStopLossTo(p, entry, "BE_MOVE_SELL");
                }
            }
        }

        private void TryModifyStopLossTo(Position p, double newSlPrice, string reason)
        {
            double? tp = p.TakeProfit;

            var result = ModifyPosition(p, newSlPrice, tp);
            if (result != null && result.IsSuccessful)
            {
                Print(
                    "BE MOVE | CodeName={0} | Reason={1} | TradeType={2} | Entry={3} | NewSL={4} | NetProfit$={5}",
                    CODE_NAME,
                    reason,
                    p.TradeType,
                    p.EntryPrice.ToString("G17", CultureInfo.InvariantCulture),
                    newSlPrice.ToString("G17", CultureInfo.InvariantCulture),
                    p.NetProfit.ToString("F2", CultureInfo.InvariantCulture)
                );
            }
        }

        // ================= PIVOT (Classic Extended) =================

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

            // We need previous session H/L/C to compute today's pivots.
            DateTime prevStartUtc = sessionStartUtc.AddDays(-1);

            if (TryGetSessionHlc(prevStartUtc, sessionStartUtc, out double high, out double low, out double close))
            {
                _currentPivotSessionStartUtc = sessionStartUtc;

                // Classic Pivot
                _pp = (high + low + close) / 3.0;

                _r1 = 2.0 * _pp - low;
                _s1 = 2.0 * _pp - high;

                double range = high - low;

                _r2 = _pp + range;
                _s2 = _pp - range;

                // Classic extensions
                _r3 = high + 2.0 * (_pp - low);
                _s3 = low - 2.0 * (high - _pp);

                _r4 = _r3 + range;
                _s4 = _s3 - range;

                _hasPivot = true;

                Print(
                    "PivotUpdated | CodeName={0} | SessionStartUTC={1:o} | PrevHLC H={2} L={3} C={4} | PP={5} R1={6} R2={7} R3={8} R4={9} | S1={10} S2={11} S3={12} S4={13}",
                    CODE_NAME,
                    _currentPivotSessionStartUtc,
                    high.ToString("G17", CultureInfo.InvariantCulture),
                    low.ToString("G17", CultureInfo.InvariantCulture),
                    close.ToString("G17", CultureInfo.InvariantCulture),
                    _pp.ToString("G17", CultureInfo.InvariantCulture),
                    _r1.ToString("G17", CultureInfo.InvariantCulture),
                    _r2.ToString("G17", CultureInfo.InvariantCulture),
                    _r3.ToString("G17", CultureInfo.InvariantCulture),
                    _r4.ToString("G17", CultureInfo.InvariantCulture),
                    _s1.ToString("G17", CultureInfo.InvariantCulture),
                    _s2.ToString("G17", CultureInfo.InvariantCulture),
                    _s3.ToString("G17", CultureInfo.InvariantCulture),
                    _s4.ToString("G17", CultureInfo.InvariantCulture)
                );
            }
            else
            {
                _currentPivotSessionStartUtc = sessionStartUtc;
                _hasPivot = false;

                Print(
                    "PivotUpdateFailed | CodeName={0} | SessionStartUTC={1:o} | Reason=SessionBarsNotFoundOrInsufficient",
                    CODE_NAME,
                    sessionStartUtc
                );
            }
        }

        // NY rollover: 17:00 NY local time
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

        // Collect H/L/C from Minute bars in [startUtc, endUtc)
        private bool TryGetSessionHlc(DateTime startUtc, DateTime endUtc, out double high, out double low, out double close)
        {
            high = double.MinValue;
            low = double.MaxValue;
            close = 0.0;

            // Use Minute bars for accurate session aggregation
            Bars minuteBars = MarketData.GetBars(TimeFrame.Minute);

            if (minuteBars == null || minuteBars.Count < 10)
                return false;

            bool hasAny = false;

            for (int i = 0; i < minuteBars.Count; i++)
            {
                DateTime t = minuteBars.OpenTimes[i];

                if (t < startUtc)
                    continue;

                if (t >= endUtc)
                    break;

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

            if (!hasAny)
                return false;

            if (high <= double.MinValue / 2.0 || low >= double.MaxValue / 2.0)
                return false;

            return true;
        }

        // ================= ENTRY: PIVOT BOUNCE =================

        private void TryPivotBounceEntry()
        {
            _diagTryPivotBounceEntryCalled++;

            // Convert "User PIPS (0.1$)" to internal cTrader pips (0.01$) by x10.
            double bufferPrice = (PivotBufferPips * 10.0) * Symbol.PipSize;

            // NOTE:
            // Entry trigger should wait 3 closed candles after the most recent touch of the line.
            // Signal evaluation is based on the last closed bar (index Count-2 / Last(1)).
            const int WAIT_BARS_AFTER_TOUCH = 3;

            if (!SymbolInfoTick(out double bid, out double ask))
                return;

            if (UseS1R1Only)
            {
                // BUY bounce at S1 -> TP to PP
                if (IsBuyBounceAfterWait(_s1, bufferPrice, WAIT_BARS_AFTER_TOUCH))
                {
                    double entry = ask;
                    double stop = _s1 - bufferPrice;
                    double tpTarget = _pp;

                    PlaceTrade(TradeType.Buy, entry, stop, tpTarget, "PIVOT_BOUNCE_S1_TP_PP");
                    return;
                }

                // SELL bounce at R1 -> TP to PP
                if (IsSellBounceAfterWait(_r1, bufferPrice, WAIT_BARS_AFTER_TOUCH))
                {
                    double entry = bid;
                    double stop = _r1 + bufferPrice;
                    double tpTarget = _pp;

                    PlaceTrade(TradeType.Sell, entry, stop, tpTarget, "PIVOT_BOUNCE_R1_TP_PP");
                    return;
                }

                return;
            }

            // Extended: attempt deeper levels first (S4 -> S3 -> S2 -> S1) and (R4 -> R3 -> R2 -> R1)
            // TP is always "next line" toward the center.

            // BUY side
            if (IsBuyBounceAfterWait(_s4, bufferPrice, WAIT_BARS_AFTER_TOUCH))
            {
                PlaceTrade(TradeType.Buy, ask, _s4 - bufferPrice, _s3, "PIVOT_BOUNCE_S4_TP_S3");
                return;
            }
            if (IsBuyBounceAfterWait(_s3, bufferPrice, WAIT_BARS_AFTER_TOUCH))
            {
                PlaceTrade(TradeType.Buy, ask, _s3 - bufferPrice, _s2, "PIVOT_BOUNCE_S3_TP_S2");
                return;
            }
            if (IsBuyBounceAfterWait(_s2, bufferPrice, WAIT_BARS_AFTER_TOUCH))
            {
                PlaceTrade(TradeType.Buy, ask, _s2 - bufferPrice, _s1, "PIVOT_BOUNCE_S2_TP_S1");
                return;
            }
            if (IsBuyBounceAfterWait(_s1, bufferPrice, WAIT_BARS_AFTER_TOUCH))
            {
                PlaceTrade(TradeType.Buy, ask, _s1 - bufferPrice, _pp, "PIVOT_BOUNCE_S1_TP_PP");
                return;
            }

            // SELL side
            if (IsSellBounceAfterWait(_r4, bufferPrice, WAIT_BARS_AFTER_TOUCH))
            {
                PlaceTrade(TradeType.Sell, bid, _r4 + bufferPrice, _r3, "PIVOT_BOUNCE_R4_TP_R3");
                return;
            }
            if (IsSellBounceAfterWait(_r3, bufferPrice, WAIT_BARS_AFTER_TOUCH))
            {
                PlaceTrade(TradeType.Sell, bid, _r3 + bufferPrice, _r2, "PIVOT_BOUNCE_R3_TP_R2");
                return;
            }
            if (IsSellBounceAfterWait(_r2, bufferPrice, WAIT_BARS_AFTER_TOUCH))
            {
                PlaceTrade(TradeType.Sell, bid, _r2 + bufferPrice, _r1, "PIVOT_BOUNCE_R2_TP_R1");
                return;
            }
            if (IsSellBounceAfterWait(_r1, bufferPrice, WAIT_BARS_AFTER_TOUCH))
            {
                PlaceTrade(TradeType.Sell, bid, _r1 + bufferPrice, _pp, "PIVOT_BOUNCE_R1_TP_PP");
                return;
            }
        }

        // Entry trigger evaluates on last closed bar (signalIndex = Bars.Count - 2).
        // It requires that the most recent touch happened at least waitBars BEFORE that signal bar.
        private bool IsBuyBounceAfterWait(double supportPrice, double bufferPrice, int waitBars)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0)
                return false;

            // Need enough history for the wait condition
            if (waitBars < 0)
                waitBars = 0;

            int minRequiredIndex = waitBars;
            if (signalIndex < minRequiredIndex)
                return false;

            double touchLine = supportPrice + bufferPrice;

            // Find the most recent touch up to the signal bar
            int lastTouchIndex = -1;
            for (int i = signalIndex; i >= 0; i--)
            {
                if (Bars.LowPrices[i] <= touchLine)
                {
                    lastTouchIndex = i;
                    break;
                }
            }

            if (lastTouchIndex < 0)
                return false;

            // Must wait at least N closed bars AFTER the touch
            if (signalIndex - lastTouchIndex < waitBars)
                return false;

            // Bounce confirmation on the signal bar: close back above the line
            return Bars.ClosePrices[signalIndex] > touchLine;
        }

        private bool IsSellBounceAfterWait(double resistancePrice, double bufferPrice, int waitBars)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0)
                return false;

            if (waitBars < 0)
                waitBars = 0;

            int minRequiredIndex = waitBars;
            if (signalIndex < minRequiredIndex)
                return false;

            double touchLine = resistancePrice - bufferPrice;

            // Find the most recent touch up to the signal bar
            int lastTouchIndex = -1;
            for (int i = signalIndex; i >= 0; i--)
            {
                if (Bars.HighPrices[i] >= touchLine)
                {
                    lastTouchIndex = i;
                    break;
                }
            }

            if (lastTouchIndex < 0)
                return false;

            // Must wait at least N closed bars AFTER the touch
            if (signalIndex - lastTouchIndex < waitBars)
                return false;

            // Bounce confirmation on the signal bar: close back below the line
            return Bars.ClosePrices[signalIndex] < touchLine;
        }

        // ================= EXECUTION =================

        private void PlaceTrade(TradeType type, double entry, double stop, double tpTargetPrice, string reasonTag)
        {
            _diagPlaceTradeCalled++;

            DiagTag tag = GetDiagTagFromReason(reasonTag);
            IncrementTagCalled(tag);

            if (RiskPercent <= 0)
            {
                _diagPlaceTradeRet_RiskPctLE0++;
                return;
            }

            double balance = Account.Balance;
            if (balance <= 0)
            {
                _diagPlaceTradeRet_BalanceLE0++;
                return;
            }

            double riskDollars = balance * (RiskPercent / 100.0);
            if (riskDollars <= 0)
            {
                _diagPlaceTradeRet_RiskDollarLE0++;
                return;
            }

            double slDistancePrice = Math.Abs(entry - stop);
            if (slDistancePrice <= 0)
            {
                _diagPlaceTradeRet_SLDistLE0++;
                PrintGuardSkip(type, entry, stop, 0.0, 0.0, 0.0, 0.0, 0.0, "SL_DISTANCE_NON_POSITIVE");
                return;
            }

            double slPips = slDistancePrice / Symbol.PipSize;
            if (slPips <= 0)
            {
                _diagPlaceTradeRet_SLPipsLE0++;
                PrintGuardSkip(type, entry, stop, slPips, 0.0, 0.0, 0.0, 0.0, "SL_PIPS_NON_POSITIVE");
                return;
            }

            // ===== Min SL distance guard (Hybrid): max(MinSLPips, ATR(Period)*Mult) =====
            // Convert "User PIPS (0.1$)" to internal cTrader pips (0.01$) by x10.
            double minSlPipsFromPips = Math.Max(0.0, MinSLPips) * 10.0;
            double minSlPriceFromPips = minSlPipsFromPips * Symbol.PipSize;

            double atrValue = 0.0;
            if (_atrMinSl != null && _atrMinSl.Result != null && _atrMinSl.Result.Count > 0)
                atrValue = _atrMinSl.Result.LastValue;

            double atrMult = Math.Max(0.0, MinSlAtrMult);
            double minSlPriceFromAtr = atrValue * atrMult;

            double minSlPriceFinal = Math.Max(minSlPriceFromPips, minSlPriceFromAtr);

            if (minSlPriceFinal > 0.0 && slDistancePrice < minSlPriceFinal)
            {
                _diagPlaceTradeRet_SLBelowMin++;

                double minFinalPips = minSlPriceFinal / Symbol.PipSize;
                PrintGuardSkip(
                    type,
                    entry,
                    stop,
                    slPips,
                    minSlPipsFromPips,
                    minSlPriceFromAtr / Symbol.PipSize,
                    minFinalPips,
                    0.0,
                    "SL_BELOW_MIN_GUARD"
                );
                return;
            }
            // ===============================================================

            double volumeUnitsRaw = riskDollars / (slPips * Symbol.PipValue);

            double normalized = Symbol.NormalizeVolumeInUnits(volumeUnitsRaw);
            long volumeInUnits = (long)normalized;

            if (volumeInUnits < Symbol.VolumeInUnitsMin)
            {
                _diagPlaceTradeRet_VolBelowMin++;
                return;
            }

            // ===== Max lots cap guard (0=Off) =====
            if (MaxLotsCap > 0.0)
            {
                double maxUnitsNorm = Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MaxLotsCap), RoundingMode.Down);
                long maxUnits = (long)maxUnitsNorm;

                if (maxUnits > 0 && volumeInUnits > maxUnits)
                {
                    _diagPlaceTradeRet_VolAboveCap++;

                    double plannedLots = Symbol.VolumeInUnitsToQuantity(volumeInUnits);
                    double capLots = Symbol.VolumeInUnitsToQuantity(maxUnits);

                    PrintGuardSkip(
                        type,
                        entry,
                        stop,
                        slPips,
                        minSlPipsFromPips,
                        minSlPriceFromAtr / Symbol.PipSize,
                        minSlPriceFinal > 0.0 ? (minSlPriceFinal / Symbol.PipSize) : 0.0,
                        plannedLots,
                        "VOLUME_ABOVE_MAX_LOTS_CAP (CapLots=" + capLots.ToString("F2", CultureInfo.InvariantCulture) + ")"
                    );
                    return;
                }
            }
            // ===============================================================

            // TP: next line target (absolute price). If invalid side, fallback to RR-based TP.
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

            double tpPips;

            // FIX: RR=0 means no fallback. Skip the trade if TP target is invalid.
            if (targetValid)
                tpPips = tpPipsFromTarget;
            else
            {
                _diagPlaceTradeRet_TPInvalidNoFallback++;

                if (type == TradeType.Buy)
                    _diagTPInvalid_Buy++;
                else
                    _diagTPInvalid_Sell++;

                IncrementTagTPInvalid(tag);

                CaptureTpInvalidSamples(type, entry, tpTargetPrice, reasonTag);

                PrintGuardSkip(
                    type,
                    entry,
                    stop,
                    slPips,
                    minSlPipsFromPips,
                    minSlPriceFromAtr / Symbol.PipSize,
                    minSlPriceFinal > 0.0 ? (minSlPriceFinal / Symbol.PipSize) : 0.0,
                    Symbol.VolumeInUnitsToQuantity(volumeInUnits),
                    "TP_TARGET_INVALID_NO_FALLBACK (RR=0)"
                );
                return;
            }

            // ===== LOG (minimal) =====
            Print(
                "RISK_CHECK | Type={0} | Entry={1} | Stop={2} | SLPips={3} | Risk$={4} | VolRaw={5} | VolUnits={6}",
                type,
                entry,
                stop,
                slPips,
                riskDollars,
                volumeUnitsRaw,
                volumeInUnits
            );

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
                IncrementTagSuccess(tag);
            }
            IncrementTagExec(tag);

            Print(
                "ENTRY | CodeName={0} | Reason={1} | Type={2} | Entry={3} | SL={4} | SLPips={5} | TPpips={6} | VolUnits={7}",
                CODE_NAME,
                reasonTag,
                type,
                entry.ToString("G17", CultureInfo.InvariantCulture),
                stop.ToString("G17", CultureInfo.InvariantCulture),
                slPips.ToString("F2", CultureInfo.InvariantCulture),
                tpPips.ToString("F2", CultureInfo.InvariantCulture),
                volumeInUnits
            );
        }

        private void PrintGuardSkip(
            TradeType type,
            double entry,
            double stop,
            double slPips,
            double minFromPips,
            double minFromAtr,
            double minFinal,
            double plannedLots,
            string reason)
        {
            Print(
                "GUARD SKIP | CodeName={0} | TradeType={1} | Entry={2} | SL={3} | SLDistPips={4} | MinPips={5} | MinAtrPips={6} | MinFinalPips={7} | PlannedLots={8} | Reason={9}",
                CODE_NAME,
                type,
                entry.ToString("G17", CultureInfo.InvariantCulture),
                stop.ToString("G17", CultureInfo.InvariantCulture),
                slPips.ToString("F2", CultureInfo.InvariantCulture),
                minFromPips.ToString("F2", CultureInfo.InvariantCulture),
                minFromAtr.ToString("F2", CultureInfo.InvariantCulture),
                minFinal.ToString("F2", CultureInfo.InvariantCulture),
                plannedLots.ToString("F2", CultureInfo.InvariantCulture),
                reason
            );
        }

        // ================= DIAG HELPERS (NO LOGIC CHANGE) =================

        private DiagTag GetDiagTagFromReason(string reasonTag)
        {
            if (string.IsNullOrWhiteSpace(reasonTag))
                return DiagTag.None;

            if (reasonTag.Contains("S1_TP_PP"))
                return DiagTag.S1;

            if (reasonTag.Contains("R1_TP_PP"))
                return DiagTag.R1;

            return DiagTag.None;
        }

        private void IncrementTagCalled(DiagTag tag)
        {
            if (tag == DiagTag.S1) _diagTagCalled_S1++;
            else if (tag == DiagTag.R1) _diagTagCalled_R1++;
        }

        private void IncrementTagTPInvalid(DiagTag tag)
        {
            if (tag == DiagTag.S1) _diagTagTPInvalid_S1++;
            else if (tag == DiagTag.R1) _diagTagTPInvalid_R1++;
        }

        private void IncrementTagExec(DiagTag tag)
        {
            if (tag == DiagTag.S1) _diagTagExec_S1++;
            else if (tag == DiagTag.R1) _diagTagExec_R1++;
        }

        private void IncrementTagSuccess(DiagTag tag)
        {
            if (tag == DiagTag.S1) _diagTagSucc_S1++;
            else if (tag == DiagTag.R1) _diagTagSucc_R1++;
        }

        private void CaptureTpInvalidSamples(TradeType type, double entry, double tpTargetPrice, string reasonTag)
        {
            // We only capture the two key cases for physical confirmation:
            // - R1 Sell with TP target = PP invalid
            // - S1 Buy with TP target = PP invalid
            // (We DO NOT alter any trade logic; this is observation only.)

            DiagTag tag = GetDiagTagFromReason(reasonTag);
            DateTime sess = _currentPivotSessionStartUtc;

            if (tag == DiagTag.R1 && type == TradeType.Sell)
            {
                _diagR1Sell_TPInv_Samples++;
                UpdateMinMax(ref _diagR1Sell_TPInv_MinEntry, ref _diagR1Sell_TPInv_MaxEntry, entry);
                UpdateMinMax(ref _diagR1Sell_TPInv_MinPP, ref _diagR1Sell_TPInv_MaxPP, _pp);
                UpdateMinMax(ref _diagR1Sell_TPInv_MinR1, ref _diagR1Sell_TPInv_MaxR1, _r1);
                UpdateFirstLastSession(ref _diagR1Sell_TPInv_FirstSessionStartUtc, ref _diagR1Sell_TPInv_LastSessionStartUtc, sess);
                return;
            }

            if (tag == DiagTag.S1 && type == TradeType.Buy)
            {
                _diagS1Buy_TPInv_Samples++;
                UpdateMinMax(ref _diagS1Buy_TPInv_MinEntry, ref _diagS1Buy_TPInv_MaxEntry, entry);
                UpdateMinMax(ref _diagS1Buy_TPInv_MinPP, ref _diagS1Buy_TPInv_MaxPP, _pp);
                UpdateMinMax(ref _diagS1Buy_TPInv_MinS1, ref _diagS1Buy_TPInv_MaxS1, _s1);
                UpdateFirstLastSession(ref _diagS1Buy_TPInv_FirstSessionStartUtc, ref _diagS1Buy_TPInv_LastSessionStartUtc, sess);
                return;
            }
        }

        private void UpdateMinMax(ref double min, ref double max, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                return;

            if (v < min) min = v;
            if (v > max) max = v;
        }

        private void UpdateFirstLastSession(ref DateTime first, ref DateTime last, DateTime sessionStartUtc)
        {
            if (sessionStartUtc == DateTime.MinValue)
                return;

            if (first == DateTime.MinValue) first = sessionStartUtc;
            last = sessionStartUtc;
        }

        // ================= FORCE CLOSE (Symbol-wide, B) =================

        private void CloseAllPositionsOnThisSymbol(string reason)
        {
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null)
                    continue;

                if (p.SymbolName != SymbolName)
                    continue;

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

            if (IsInRangeCircular(nowMin, startMin, endMin))
                return TradingWindowState.AllowNewEntries;

            if (IsInRangeCircular(nowMin, endMin, forceMin))
                return TradingWindowState.HoldOnly;

            return TradingWindowState.ForceFlat;
        }

        // Inclusive at start, exclusive at end: [start, end)
        // Circular range on 0..1439.
        private bool IsInRangeCircular(int nowMin, int startMin, int endMin)
        {
            if (startMin == endMin)
                return false;

            if (startMin < endMin)
                return nowMin >= startMin && nowMin < endMin;

            // Overnight: start..1440 and 0..end
            return nowMin >= startMin || nowMin < endMin;
        }

        private int NormalizeMinutes(int hour, int minute)
        {
            int h = hour;
            int m = minute;

            if (h < 0) h = 0;
            if (h > 23) h = 23;
            if (m < 0) m = 0;
            if (m > 59) m = 59;

            return h * 60 + m;
        }

        private DateTime ToJst(DateTime utcNow)
        {
            DateTime utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

            if (_jstTz == null)
                return utc;

            return TimeZoneInfo.ConvertTimeFromUtc(utc, _jstTz);
        }

        private TimeZoneInfo ResolveTokyoTimeZone()
        {
            string[] candidateIds = new[] { "Tokyo Standard Time", "Asia/Tokyo" };

            foreach (string id in candidateIds)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch
                {
                }
            }

            return TimeZoneInfo.Utc;
        }

        private TimeZoneInfo ResolveNewYorkTimeZone()
        {
            string[] candidateIds = new[] { "Eastern Standard Time", "America/New_York" };

            foreach (string id in candidateIds)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch
                {
                }
            }

            return null;
        }

        // ================= NEWS FILTER (UTC) =================

        private void LoadEconomicCalendarUtc(string raw)
        {
            _highImpactEventsUtc.Clear();

            if (string.IsNullOrWhiteSpace(raw))
                return;

            string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (string lineRaw in lines)
            {
                string line = lineRaw.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.Contains("DateTime") && line.Contains("Event") && line.Contains("Importance"))
                    continue;

                if (line.Length >= 2 && line[0] == '"' && line[line.Length - 1] == '"')
                    line = line.Substring(1, line.Length - 2);

                string[] parts = line.Split(new[] { ',' }, 3);
                if (parts.Length < 1)
                    continue;

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

            string[] formats =
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm"
            };

            if (DateTime.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
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
            if (_highImpactEventsUtc.Count == 0)
                return false;

            int before = Math.Max(0, MinutesBeforeNews);
            int after = Math.Max(0, MinutesAfterNews);

            for (int i = 0; i < _highImpactEventsUtc.Count; i++)
            {
                DateTime e = _highImpactEventsUtc[i];
                DateTime start = e.AddMinutes(-before);
                DateTime end = e.AddMinutes(after);

                if (utcNow >= start && utcNow <= end)
                    return true;
            }

            return false;
        }

        // ================= PRICE FETCH (SymbolInfoTick rule compliance) =================
        // NOTE:
        // In your cTrader build, Symbol.Tick is an EVENT, so use Symbol.Bid/Symbol.Ask snapshots.

        private bool SymbolInfoTick(out double bid, out double ask)
        {
            bid = 0.0;
            ask = 0.0;

            try
            {
                bid = Symbol.Bid;
                ask = Symbol.Ask;

                if (bid <= 0.0 || ask <= 0.0)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
