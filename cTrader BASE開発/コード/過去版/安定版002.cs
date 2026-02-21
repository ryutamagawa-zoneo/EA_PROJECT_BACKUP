using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HIVE_PivotBounce_v0_1_1 : Robot
    {
        // ============================================================
        // CODE NAME (for traceability)
        // ============================================================
        private const string CODE_NAME = "PIVOT_BOUNCE_M5_ALL_DAY_001";
        // ============================================================

        // ================= PARAMETERS =================

        // FIX: Fixed $ risk per trade (no compounding).
        [Parameter("Risk Per Trade ($)", DefaultValue = 1000.0, MinValue = 0.0)]
        public double RiskDollars { get; set; }

        // Safety: reduce volume by assuming extra pips beyond SL (spread/slippage/gap)
        // This is INTERNAL cTrader pips (usually 0.01$ on XAUUSD).
        [Parameter("Risk Buffer Pips (internal)", DefaultValue = 0.0, MinValue = 0.0)]
        public double RiskBufferPips { get; set; }

        // Safety: if floating loss exceeds Risk$ * this multiplier, emergency close.
        [Parameter("Emergency Close Mult", DefaultValue = 1.20, MinValue = 1.0)]
        public double EmergencyCloseMult { get; set; }

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

        // ===== Guards (parameterized) =====
        [Parameter("Max Lots Cap (0=Off)", DefaultValue = 2.5, MinValue = 0.0)]
        public double MaxLotsCap { get; set; }

        // NOTE (Unit Alignment for XAUUSD):
        // This parameter is treated as "User PIPS" where 1 PIPS = $0.1
        // Internally, cTrader pip is typically $0.01 so convert by x10.
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

        private DateTime _currentPivotSessionStartUtc = DateTime.MinValue;
        private bool _hasPivot;

        // Classic Pivot Levels (Extended)
        private double _pp, _r1, _r2, _r3, _r4, _s1, _s2, _s3, _s4;

        // ================= DIAG (MINIMAL COUNTERS ONLY) =================
        private long _diagOnStart, _diagOnStop, _diagOnTick, _diagOnTimer, _diagOnBar;
        private long _diagOnBarRet_BarsCount, _diagOnBarRet_ForceFlat, _diagOnBarRet_NotAllowNewEntries, _diagOnBarRet_NewsWindow, _diagOnBarRet_MaxPositions, _diagOnBarRet_NoPivot;
        private long _diagPivotUpdateCalls, _diagTryPivotBounceEntryCalled;

        private long _diagPlaceTradeCalled;
        private long _diagPlaceTradeRet_RiskDollarLE0, _diagPlaceTradeRet_BalanceLE0, _diagPlaceTradeRet_SLDistLE0, _diagPlaceTradeRet_SLPipsLE0, _diagPlaceTradeRet_SLBelowMin, _diagPlaceTradeRet_VolBelowMin, _diagPlaceTradeRet_VolAboveCap, _diagPlaceTradeRet_TPInvalidNoFallback;
        private long _diagExecuteCalled, _diagSuccess;

        private long _diagTPInvalid_Buy, _diagTPInvalid_Sell;

        private long _diagEmergencyClosed; // NEW: emergency cut count

        // ================= RISK/STOP DIAG (robust & consistent) =================

        private class RiskTrack
        {
            public double RiskDollars;
            public double SlPips;                 // actual SL distance pips (internal)
            public double ExpectedLossAtSl;       // = SlPips * PipValue * VolumeInUnits
            public long VolumeInUnits;
            public TradeType Type;
            public double EntryPrice;
            public double StopPrice;
            public double TpTargetPrice;
            public DateTime OpenTimeUtc;
        }

        private readonly Dictionary<long, RiskTrack> _riskByPositionId = new Dictionary<long, RiskTrack>();

        private long _diagRiskTracked, _diagRiskClosed, _diagRiskLossTrades;
        private long _diagRiskActualLossOver1200;
        private double _diagRiskMaxExpectedLossAtSl = double.NegativeInfinity;
        private double _diagRiskMaxActualLoss = double.NegativeInfinity;
        private DateTime _diagRiskMaxActualLossTimeUtc = DateTime.MinValue;

        private long _diagStopClosed;
        private readonly List<double> _stopLossMultSamples = new List<double>();
        private double _diagMultP50 = double.NaN, _diagMultP90 = double.NaN, _diagMultP95 = double.NaN, _diagMultP99 = double.NaN, _diagMultMax = double.NaN;

        private long _diagCloseReason_StopLoss, _diagCloseReason_TakeProfit, _diagCloseReason_Other, _diagCloseReason_Unknown;

        // ================= ENUMS =================
        private enum TradingWindowState { AllowNewEntries = 0, HoldOnly = 1, ForceFlat = 2 }

        // ================= LIFECYCLE =================

        protected override void OnStart()
        {
            _diagOnStart++;

            _jstTz = ResolveTokyoTimeZone();
            _nyTz = ResolveNewYorkTimeZone();

            _atrMinSl = Indicators.AverageTrueRange(MinSlAtrPeriod, MovingAverageType.Simple);

            Timer.Start(1);
            Positions.Closed += OnPositionClosed;

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
                "Started. cBot={0} Symbol={1} | CodeName={2} | Risk$={3} RiskBufferPips={4} EmergencyMult={5} | Window(JST) Start={6:D2}:{7:D2} End={8:D2}:{9:D2} ForceClose={10:D2}:{11:D2} | Guards: MinSLPips(user)={12} ATRPeriod={13} ATRMult={14} MaxLotsCap={15} | BETrigger$={16}",
                nameof(HIVE_PivotBounce_v0_1_1),
                SymbolName,
                CODE_NAME,
                RiskDollars.ToString("F2", CultureInfo.InvariantCulture),
                RiskBufferPips.ToString("F2", CultureInfo.InvariantCulture),
                EmergencyCloseMult.ToString("F2", CultureInfo.InvariantCulture),
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
                "OnBarRet_BarsCount={7} OnBarRet_ForceFlat={8} OnBarRet_NotAllowNewEntries={9} OnBarRet_NewsWindow={10} OnBarRet_MaxPositions={11} OnBarRet_NoPivot={12} | " +
                "PivotUpdateCalls={13} TryPivotBounceEntryCalled={14} | " +
                "PlaceTradeCalled={15} PlaceTradeRet_Risk$<=0={16} PlaceTradeRet_Balance<=0={17} PlaceTradeRet_SLDist<=0={18} PlaceTradeRet_SLPips<=0={19} PlaceTradeRet_SLBelowMin={20} PlaceTradeRet_VolBelowMin={21} PlaceTradeRet_VolAboveCap={22} PlaceTradeRet_TPInvalidNoFallback(RR=0)={23} | " +
                "TPInvalid_Buy={24} TPInvalid_Sell={25} | ExecuteCalled={26} Success={27} | EmergencyClosed={28} | " +
                "RISK_DIAG Tracked={29} Closed={30} LossTrades={31} ActualLoss>1200={32} MaxExpectedLossAtSL$={33} MaxActualLoss$={34} MaxActualLossTimeUTC={35:o} | " +
                "STOP_DIAG StopClosed={36} Mult_P50={37} P90={38} P95={39} P99={40} Max={41} | " +
                "CLOSE_REASON StopLoss={42} TakeProfit={43} Other={44} Unknown={45}",
                CODE_NAME,
                SymbolName,
                _diagOnStart, _diagOnStop, _diagOnTick, _diagOnTimer, _diagOnBar,
                _diagOnBarRet_BarsCount, _diagOnBarRet_ForceFlat, _diagOnBarRet_NotAllowNewEntries, _diagOnBarRet_NewsWindow, _diagOnBarRet_MaxPositions, _diagOnBarRet_NoPivot,
                _diagPivotUpdateCalls, _diagTryPivotBounceEntryCalled,
                _diagPlaceTradeCalled, _diagPlaceTradeRet_RiskDollarLE0, _diagPlaceTradeRet_BalanceLE0, _diagPlaceTradeRet_SLDistLE0, _diagPlaceTradeRet_SLPipsLE0,
                _diagPlaceTradeRet_SLBelowMin, _diagPlaceTradeRet_VolBelowMin, _diagPlaceTradeRet_VolAboveCap, _diagPlaceTradeRet_TPInvalidNoFallback,
                _diagTPInvalid_Buy, _diagTPInvalid_Sell,
                _diagExecuteCalled, _diagSuccess,
                _diagEmergencyClosed,
                _diagRiskTracked, _diagRiskClosed, _diagRiskLossTrades, _diagRiskActualLossOver1200,
                (_diagRiskMaxExpectedLossAtSl > double.NegativeInfinity ? _diagRiskMaxExpectedLossAtSl : double.NaN),
                (_diagRiskMaxActualLoss > double.NegativeInfinity ? _diagRiskMaxActualLoss : double.NaN),
                (_diagRiskMaxActualLossTimeUtc != DateTime.MinValue ? _diagRiskMaxActualLossTimeUtc : DateTime.MinValue),
                _diagStopClosed,
                _diagMultP50, _diagMultP90, _diagMultP95, _diagMultP99, _diagMultMax,
                _diagCloseReason_StopLoss, _diagCloseReason_TakeProfit, _diagCloseReason_Other, _diagCloseReason_Unknown
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
            ApplyEmergencyLossCutIfNeeded();
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

        // ================= EMERGENCY LOSS CUT =================

        private void ApplyEmergencyLossCutIfNeeded()
        {
            double risk = Math.Max(0.0, RiskDollars);
            if (risk <= 0.0)
                return;

            double mult = Math.Max(1.0, EmergencyCloseMult);
            double threshold = -risk * mult;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null)
                    continue;

                if (p.SymbolName != SymbolName)
                    continue;

                if (p.NetProfit <= threshold)
                {
                    _diagEmergencyClosed++;
                    ClosePosition(p);
                    Print(
                        "EMERGENCY_CLOSE | CodeName={0} | Type={1} | NetProfit$={2} <= Threshold$={3} | Mult={4}",
                        CODE_NAME,
                        p.TradeType,
                        p.NetProfit.ToString("F2", CultureInfo.InvariantCulture),
                        threshold.ToString("F2", CultureInfo.InvariantCulture),
                        mult.ToString("F2", CultureInfo.InvariantCulture)
                    );
                }
            }
        }

        // ================= POSITION CLOSED (DIAG) =================

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (args == null)
                return;

            Position p = args.Position;
            if (p == null)
                return;

            if (p.SymbolName != SymbolName)
                return;

            string reason = GetCloseReasonSafe(args, p);

            if (reason == "StopLoss") _diagCloseReason_StopLoss++;
            else if (reason == "TakeProfit") _diagCloseReason_TakeProfit++;
            else if (reason == "Unknown") _diagCloseReason_Unknown++;
            else _diagCloseReason_Other++;

            long posId = p.Id;

            if (_riskByPositionId.TryGetValue(posId, out RiskTrack tr))
            {
                _diagRiskClosed++;

                double actualLoss = Math.Max(0.0, -p.NetProfit);
                if (actualLoss > 0.0)
                {
                    _diagRiskLossTrades++;

                    if (actualLoss > 1200.0)
                        _diagRiskActualLossOver1200++;

                    if (tr.ExpectedLossAtSl > _diagRiskMaxExpectedLossAtSl)
                        _diagRiskMaxExpectedLossAtSl = tr.ExpectedLossAtSl;

                    if (actualLoss > _diagRiskMaxActualLoss)
                    {
                        _diagRiskMaxActualLoss = actualLoss;
                        _diagRiskMaxActualLossTimeUtc = DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);
                    }
                }

                // Stop-loss multiplier sample (only if closed by stop)
                if (reason == "StopLoss" && tr.ExpectedLossAtSl > 0.0)
                {
                    double actualLossAtClose = Math.Max(0.0, -p.NetProfit);
                    if (actualLossAtClose > 0.0)
                    {
                        double mult = actualLossAtClose / tr.ExpectedLossAtSl;
                        _stopLossMultSamples.Add(mult);
                        _diagStopClosed++;

                        Print(
                            "STOP_DIAG_SAMPLE | PosId={0} | ExpectedLossAtSL$={1} | ActualLoss$={2} | Mult={3:F3} | SLpips={4:F2} | VolUnits={5}",
                            posId,
                            tr.ExpectedLossAtSl.ToString("F2", CultureInfo.InvariantCulture),
                            actualLossAtClose.ToString("F2", CultureInfo.InvariantCulture),
                            mult,
                            tr.SlPips,
                            tr.VolumeInUnits
                        );
                    }
                }

                _riskByPositionId.Remove(posId);
            }
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

            double bufferPrice = (PivotBufferPips * 10.0) * Symbol.PipSize;
            const int WAIT_BARS_AFTER_TOUCH = 3;

            if (!SymbolInfoTick(out double bid, out double ask))
                return;

            if (UseS1R1Only)
            {
                if (IsBuyBounceAfterWait(_s1, bufferPrice, WAIT_BARS_AFTER_TOUCH))
                {
                    double entry = ask;
                    double stop = _s1 - bufferPrice;
                    double tpTarget = _pp;

                    PlaceTrade(TradeType.Buy, entry, stop, tpTarget, "PIVOT_BOUNCE_S1_TP_PP");
                    return;
                }

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

            // Extended (unchanged)
            if (IsBuyBounceAfterWait(_s4, bufferPrice, WAIT_BARS_AFTER_TOUCH)) { PlaceTrade(TradeType.Buy, ask, _s4 - bufferPrice, _s3, "PIVOT_BOUNCE_S4_TP_S3"); return; }
            if (IsBuyBounceAfterWait(_s3, bufferPrice, WAIT_BARS_AFTER_TOUCH)) { PlaceTrade(TradeType.Buy, ask, _s3 - bufferPrice, _s2, "PIVOT_BOUNCE_S3_TP_S2"); return; }
            if (IsBuyBounceAfterWait(_s2, bufferPrice, WAIT_BARS_AFTER_TOUCH)) { PlaceTrade(TradeType.Buy, ask, _s2 - bufferPrice, _s1, "PIVOT_BOUNCE_S2_TP_S1"); return; }
            if (IsBuyBounceAfterWait(_s1, bufferPrice, WAIT_BARS_AFTER_TOUCH)) { PlaceTrade(TradeType.Buy, ask, _s1 - bufferPrice, _pp, "PIVOT_BOUNCE_S1_TP_PP"); return; }

            if (IsSellBounceAfterWait(_r4, bufferPrice, WAIT_BARS_AFTER_TOUCH)) { PlaceTrade(TradeType.Sell, bid, _r4 + bufferPrice, _r3, "PIVOT_BOUNCE_R4_TP_R3"); return; }
            if (IsSellBounceAfterWait(_r3, bufferPrice, WAIT_BARS_AFTER_TOUCH)) { PlaceTrade(TradeType.Sell, bid, _r3 + bufferPrice, _r2, "PIVOT_BOUNCE_R3_TP_R2"); return; }
            if (IsSellBounceAfterWait(_r2, bufferPrice, WAIT_BARS_AFTER_TOUCH)) { PlaceTrade(TradeType.Sell, bid, _r2 + bufferPrice, _r1, "PIVOT_BOUNCE_R2_TP_R1"); return; }
            if (IsSellBounceAfterWait(_r1, bufferPrice, WAIT_BARS_AFTER_TOUCH)) { PlaceTrade(TradeType.Sell, bid, _r1 + bufferPrice, _pp, "PIVOT_BOUNCE_R1_TP_PP"); return; }
        }

        private bool IsBuyBounceAfterWait(double supportPrice, double bufferPrice, int waitBars)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0)
                return false;

            if (waitBars < 0) waitBars = 0;
            if (signalIndex < waitBars)
                return false;

            double touchLine = supportPrice + bufferPrice;

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

            if (signalIndex - lastTouchIndex < waitBars)
                return false;

            return Bars.ClosePrices[signalIndex] > touchLine;
        }

        private bool IsSellBounceAfterWait(double resistancePrice, double bufferPrice, int waitBars)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0)
                return false;

            if (waitBars < 0) waitBars = 0;
            if (signalIndex < waitBars)
                return false;

            double touchLine = resistancePrice - bufferPrice;

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

            if (signalIndex - lastTouchIndex < waitBars)
                return false;

            return Bars.ClosePrices[signalIndex] < touchLine;
        }

        // ================= EXECUTION =================

        private void PlaceTrade(TradeType type, double entry, double stop, double tpTargetPrice, string reasonTag)
        {
            _diagPlaceTradeCalled++;

            double riskDollars = Math.Max(0.0, RiskDollars);
            if (riskDollars <= 0.0)
            {
                _diagPlaceTradeRet_RiskDollarLE0++;
                return;
            }

            double balance = Account.Balance;
            if (balance <= 0)
            {
                _diagPlaceTradeRet_BalanceLE0++;
                return;
            }

            double slDistancePrice = Math.Abs(entry - stop);
            if (slDistancePrice <= 0)
            {
                _diagPlaceTradeRet_SLDistLE0++;
                PrintGuardSkip(type, entry, stop, 0.0, "SL_DISTANCE_NON_POSITIVE");
                return;
            }

            double slPips = slDistancePrice / Symbol.PipSize;
            if (slPips <= 0)
            {
                _diagPlaceTradeRet_SLPipsLE0++;
                PrintGuardSkip(type, entry, stop, slPips, "SL_PIPS_NON_POSITIVE");
                return;
            }

            // ===== Min SL distance guard (Hybrid): max(MinSLPips, ATR(Period)*Mult) =====
            double minSlPipsFromUser = Math.Max(0.0, MinSLPips) * 10.0; // user(0.1$) -> internal(0.01$)
            double minSlPriceFromUser = minSlPipsFromUser * Symbol.PipSize;

            double atrValue = 0.0;
            if (_atrMinSl != null && _atrMinSl.Result != null && _atrMinSl.Result.Count > 0)
                atrValue = _atrMinSl.Result.LastValue;

            double atrMult = Math.Max(0.0, MinSlAtrMult);
            double minSlPriceFromAtr = atrValue * atrMult;

            double minSlPriceFinal = Math.Max(minSlPriceFromUser, minSlPriceFromAtr);

            if (minSlPriceFinal > 0.0 && slDistancePrice < minSlPriceFinal)
            {
                _diagPlaceTradeRet_SLBelowMin++;
                PrintGuardSkip(type, entry, stop, slPips, "SL_BELOW_MIN_GUARD");
                return;
            }

            // ===== RISK BUFFER (for slippage/spread/gap) =====
            double bufferPips = Math.Max(0.0, RiskBufferPips);
            double effectiveSlPipsForSizing = slPips + bufferPips;

            // IMPORTANT:
            // Keep the ORIGINAL sizing model (volume in units) to avoid breaking the known-working behavior:
            // volumeUnitsRaw = risk / (pips * pipValue)
            double volumeUnitsRaw = riskDollars / (effectiveSlPipsForSizing * Symbol.PipValue);

            double normalized = Symbol.NormalizeVolumeInUnits(volumeUnitsRaw);
            long volumeInUnits = (long)normalized;

            if (volumeInUnits < Symbol.VolumeInUnitsMin)
            {
                _diagPlaceTradeRet_VolBelowMin++;
                PrintGuardSkip(type, entry, stop, slPips, "VOLUME_BELOW_MIN");
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
                    PrintGuardSkip(type, entry, stop, slPips, "VOLUME_ABOVE_MAX_LOTS_CAP");
                    return;
                }
            }

            // TP validity (RR fallback disabled)
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
                if (type == TradeType.Buy) _diagTPInvalid_Buy++; else _diagTPInvalid_Sell++;

                PrintGuardSkip(type, entry, stop, slPips, "TP_TARGET_INVALID_NO_FALLBACK (RR=0)");
                return;
            }

            double tpPips = tpPipsFromTarget;

            // ---- Expected Loss at SL (CONSISTENT with sizing model) ----
            double expectedLossAtSl = CalcExpectedLossAtSlDollars(slPips, volumeInUnits);
            if (expectedLossAtSl > _diagRiskMaxExpectedLossAtSl)
                _diagRiskMaxExpectedLossAtSl = expectedLossAtSl;

            Print(
                "RISK_CHECK | Reason={0} | Type={1} | Entry={2} | Stop={3} | SLPips={4:F2} | BufferPips={5:F2} | EffSLPips={6:F2} | Risk$={7:F2} | VolRaw={8} | VolUnits={9} | ExpectedLossAtSL$={10}",
                reasonTag,
                type,
                entry.ToString("G17", CultureInfo.InvariantCulture),
                stop.ToString("G17", CultureInfo.InvariantCulture),
                slPips,
                bufferPips,
                effectiveSlPipsForSizing,
                riskDollars,
                volumeUnitsRaw.ToString("G17", CultureInfo.InvariantCulture),
                volumeInUnits,
                expectedLossAtSl.ToString("F2", CultureInfo.InvariantCulture)
            );

            _diagExecuteCalled++;

            TradeResult result = ExecuteMarketOrder(
                type,
                SymbolName,
                volumeInUnits,
                "PIVOT_BOUNCE_v0_1_1",
                slPips,
                tpPips
            );

            if (result != null && result.IsSuccessful)
            {
                _diagSuccess++;

                // Track for later diagnosis (close multipliers etc.)
                if (result.Position != null)
                {
                    long posId = result.Position.Id;

                    _diagRiskTracked++;

                    _riskByPositionId[posId] = new RiskTrack
                    {
                        RiskDollars = riskDollars,
                        SlPips = slPips,
                        ExpectedLossAtSl = expectedLossAtSl,
                        VolumeInUnits = volumeInUnits,
                        Type = type,
                        EntryPrice = entry,
                        StopPrice = stop,
                        TpTargetPrice = tpTargetPrice,
                        OpenTimeUtc = DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc)
                    };
                }

                Print(
                    "ENTRY | CodeName={0} | Reason={1} | Type={2} | Entry={3} | SLPrice={4} | SLPips={5:F2} | TPpips={6:F2} | VolUnits={7} | Risk$={8:F2} | ExpectedLossAtSL$={9}",
                    CODE_NAME,
                    reasonTag,
                    type,
                    entry.ToString("G17", CultureInfo.InvariantCulture),
                    stop.ToString("G17", CultureInfo.InvariantCulture),
                    slPips,
                    tpPips,
                    volumeInUnits,
                    riskDollars,
                    expectedLossAtSl.ToString("F2", CultureInfo.InvariantCulture)
                );
            }
        }

        private void PrintGuardSkip(TradeType type, double entry, double stop, double slPips, string reason)
        {
            Print(
                "GUARD SKIP | CodeName={0} | TradeType={1} | Entry={2} | SL={3} | SLPips={4} | Reason={5}",
                CODE_NAME,
                type,
                entry.ToString("G17", CultureInfo.InvariantCulture),
                stop.ToString("G17", CultureInfo.InvariantCulture),
                slPips.ToString("F2", CultureInfo.InvariantCulture),
                reason
            );
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

        private bool IsInRangeCircular(int nowMin, int startMin, int endMin)
        {
            if (startMin == endMin)
                return false;

            if (startMin < endMin)
                return nowMin >= startMin && nowMin < endMin;

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
            if (_jstTz == null) return utc;
            return TimeZoneInfo.ConvertTimeFromUtc(utc, _jstTz);
        }

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

        private TimeZoneInfo ResolveTokyoTimeZone()
        {
            string[] candidateIds = new[] { "Tokyo Standard Time", "Asia/Tokyo" };

            foreach (string id in candidateIds)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch { }
            }

            return TimeZoneInfo.Utc;
        }

        private TimeZoneInfo ResolveNewYorkTimeZone()
        {
            string[] candidateIds = new[] { "Eastern Standard Time", "America/New_York" };

            foreach (string id in candidateIds)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch { }
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

        // ================= PRICE FETCH =================

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

        // ================= EXPECTED LOSS (CONSISTENT) =================
        // ExpectedLossAtSL$ = SLPips * PipValue * VolumeInUnits
        // This matches the SAME model used for sizing: risk = pips * pipValue * volume.
        private double CalcExpectedLossAtSlDollars(double slPips, long volumeInUnits)
        {
            if (slPips <= 0.0 || volumeInUnits <= 0)
                return 0.0;

            double expected = slPips * Symbol.PipValue * volumeInUnits;

            if (double.IsNaN(expected) || double.IsInfinity(expected) || expected <= 0.0)
                return 0.0;

            return expected;
        }

        private double PercentileFromSorted(List<double> sortedAsc, double p01)
        {
            if (sortedAsc == null || sortedAsc.Count == 0)
                return double.NaN;

            if (p01 <= 0.0) return sortedAsc[0];
            if (p01 >= 1.0) return sortedAsc[sortedAsc.Count - 1];

            double idx = (sortedAsc.Count - 1) * p01;
            int i0 = (int)Math.Floor(idx);
            int i1 = (int)Math.Ceiling(idx);

            if (i0 == i1)
                return sortedAsc[i0];

            double w = idx - i0;
            return sortedAsc[i0] * (1.0 - w) + sortedAsc[i1] * w;
        }

        // ================= CLOSE_REASON HELPERS =================

        private string GetCloseReasonSafe(PositionClosedEventArgs args, Position p)
        {
            try
            {
                if (args != null)
                {
                    var ta = args.GetType();

                    var pa = ta.GetProperty("Reason");
                    if (pa != null)
                    {
                        object v = pa.GetValue(args, null);
                        if (v != null) return NormalizeCloseReason(v.ToString());
                    }

                    var pa2 = ta.GetProperty("CloseReason");
                    if (pa2 != null)
                    {
                        object v = pa2.GetValue(args, null);
                        if (v != null) return NormalizeCloseReason(v.ToString());
                    }

                    var pa3 = ta.GetProperty("ClosingReason");
                    if (pa3 != null)
                    {
                        object v = pa3.GetValue(args, null);
                        if (v != null) return NormalizeCloseReason(v.ToString());
                    }
                }

                if (p != null)
                {
                    var t = p.GetType();

                    var pp = t.GetProperty("CloseReason");
                    if (pp != null)
                    {
                        object v = pp.GetValue(p, null);
                        if (v != null) return NormalizeCloseReason(v.ToString());
                    }

                    var pp2 = t.GetProperty("Reason");
                    if (pp2 != null)
                    {
                        object v = pp2.GetValue(p, null);
                        if (v != null) return NormalizeCloseReason(v.ToString());
                    }

                    var pp3 = t.GetProperty("ClosingReason");
                    if (pp3 != null)
                    {
                        object v = pp3.GetValue(p, null);
                        if (v != null) return NormalizeCloseReason(v.ToString());
                    }
                }
            }
            catch
            {
            }

            return "Unknown";
        }

        private string NormalizeCloseReason(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Unknown";

            string s = raw.Trim();

            if (s.IndexOf("StopLoss", StringComparison.OrdinalIgnoreCase) >= 0) return "StopLoss";
            if (s.IndexOf("TakeProfit", StringComparison.OrdinalIgnoreCase) >= 0) return "TakeProfit";

            return "Other";
        }
    }
}
