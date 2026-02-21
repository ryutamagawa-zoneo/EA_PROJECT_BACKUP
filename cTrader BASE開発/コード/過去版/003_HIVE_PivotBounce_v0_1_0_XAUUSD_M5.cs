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

        [Parameter("Risk Reward", DefaultValue = 1.5)]
        public double RiskReward { get; set; }

        [Parameter("Max Positions", DefaultValue = 1, MinValue = 1)]
        public int MaxPositions { get; set; }

        // ===== SL Breakeven Move (Profit in $) =====

        [Parameter("BE Move Trigger ($)", DefaultValue = 1000.0, MinValue = 0.0)]
        public double BreakevenTriggerDollars { get; set; }

        // ===== Pivot (Daily, NY Close @ 17:00 NY) =====

        [Parameter("Pivot Bounce Buffer (pips)", DefaultValue = 2.0, MinValue = 0.0)]
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

        [Parameter("Min SL Pips", DefaultValue = 50.0, MinValue = 0.0)]
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

        // ================= LIFECYCLE =================

        protected override void OnStart()
        {
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

        protected override void OnTimer()
        {
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
            ApplyBreakevenMoveIfNeeded();
        }

        protected override void OnBar()
        {
            if (Bars.Count < 50)
                return;

            DateTime utcNow = Server.Time;

            if (EnableTradingWindowFilter)
            {
                DateTime jstNow = ToJst(utcNow);
                TradingWindowState state = GetTradingWindowState(jstNow);

                // ForceFlat is handled in OnTimer, but keep it safe here too.
                if (state == TradingWindowState.ForceFlat)
                    return;

                // After End, new entries are prohibited.
                if (state != TradingWindowState.AllowNewEntries)
                    return;
            }

            if (EnableNewsFilter && IsInNewsWindow(utcNow))
                return;

            if (Positions.Count >= MaxPositions)
                return;

            UpdateDailyPivotIfNeeded(utcNow);

            if (!_hasPivot)
                return;

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
            double bufferPrice = PivotBufferPips * Symbol.PipSize;

            // Use last closed bar (Last(1)) for confirmation
            double lastLow = Bars.LowPrices.Last(1);
            double lastHigh = Bars.HighPrices.Last(1);
            double lastClose = Bars.ClosePrices.Last(1);

            if (!SymbolInfoTick(out double bid, out double ask))
                return;

            if (UseS1R1Only)
            {
                // BUY bounce at S1 -> TP to PP
                if (IsBuyBounce(lastLow, lastClose, _s1, bufferPrice))
                {
                    double entry = ask;
                    double stop = _s1 - bufferPrice;
                    double tpTarget = _pp;

                    PlaceTrade(TradeType.Buy, entry, stop, tpTarget, "PIVOT_BOUNCE_S1_TP_PP");
                    return;
                }

                // SELL bounce at R1 -> TP to PP
                if (IsSellBounce(lastHigh, lastClose, _r1, bufferPrice))
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
            if (IsBuyBounce(lastLow, lastClose, _s4, bufferPrice))
            {
                PlaceTrade(TradeType.Buy, ask, _s4 - bufferPrice, _s3, "PIVOT_BOUNCE_S4_TP_S3");
                return;
            }
            if (IsBuyBounce(lastLow, lastClose, _s3, bufferPrice))
            {
                PlaceTrade(TradeType.Buy, ask, _s3 - bufferPrice, _s2, "PIVOT_BOUNCE_S3_TP_S2");
                return;
            }
            if (IsBuyBounce(lastLow, lastClose, _s2, bufferPrice))
            {
                PlaceTrade(TradeType.Buy, ask, _s2 - bufferPrice, _s1, "PIVOT_BOUNCE_S2_TP_S1");
                return;
            }
            if (IsBuyBounce(lastLow, lastClose, _s1, bufferPrice))
            {
                PlaceTrade(TradeType.Buy, ask, _s1 - bufferPrice, _pp, "PIVOT_BOUNCE_S1_TP_PP");
                return;
            }

            // SELL side
            if (IsSellBounce(lastHigh, lastClose, _r4, bufferPrice))
            {
                PlaceTrade(TradeType.Sell, bid, _r4 + bufferPrice, _r3, "PIVOT_BOUNCE_R4_TP_R3");
                return;
            }
            if (IsSellBounce(lastHigh, lastClose, _r3, bufferPrice))
            {
                PlaceTrade(TradeType.Sell, bid, _r3 + bufferPrice, _r2, "PIVOT_BOUNCE_R3_TP_R2");
                return;
            }
            if (IsSellBounce(lastHigh, lastClose, _r2, bufferPrice))
            {
                PlaceTrade(TradeType.Sell, bid, _r2 + bufferPrice, _r1, "PIVOT_BOUNCE_R2_TP_R1");
                return;
            }
            if (IsSellBounce(lastHigh, lastClose, _r1, bufferPrice))
            {
                PlaceTrade(TradeType.Sell, bid, _r1 + bufferPrice, _pp, "PIVOT_BOUNCE_R1_TP_PP");
                return;
            }
        }

        private bool IsBuyBounce(double lastLow, double lastClose, double supportPrice, double bufferPrice)
        {
            // touched/penetrated support (low <= support + buffer) and closed back above (close > support + buffer)
            return lastLow <= (supportPrice + bufferPrice) && lastClose > (supportPrice + bufferPrice);
        }

        private bool IsSellBounce(double lastHigh, double lastClose, double resistancePrice, double bufferPrice)
        {
            // touched/penetrated resistance (high >= resistance - buffer) and closed back below (close < resistance - buffer)
            return lastHigh >= (resistancePrice - bufferPrice) && lastClose < (resistancePrice - bufferPrice);
        }

        // ================= EXECUTION =================

        private void PlaceTrade(TradeType type, double entry, double stop, double tpTargetPrice, string reasonTag)
        {
            if (RiskPercent <= 0)
                return;

            double balance = Account.Balance;
            if (balance <= 0)
                return;

            double riskDollars = balance * (RiskPercent / 100.0);
            if (riskDollars <= 0)
                return;

            double slDistancePrice = Math.Abs(entry - stop);
            if (slDistancePrice <= 0)
            {
                PrintGuardSkip(type, entry, stop, 0.0, 0.0, 0.0, 0.0, 0.0, "SL_DISTANCE_NON_POSITIVE");
                return;
            }

            double slPips = slDistancePrice / Symbol.PipSize;
            if (slPips <= 0)
            {
                PrintGuardSkip(type, entry, stop, slPips, 0.0, 0.0, 0.0, 0.0, "SL_PIPS_NON_POSITIVE");
                return;
            }

            // ===== Min SL distance guard (Hybrid): max(MinSLPips, ATR(Period)*Mult) =====
            double minSlPipsFromPips = Math.Max(0.0, MinSLPips);
            double minSlPriceFromPips = minSlPipsFromPips * Symbol.PipSize;

            double atrValue = 0.0;
            if (_atrMinSl != null && _atrMinSl.Result != null && _atrMinSl.Result.Count > 0)
                atrValue = _atrMinSl.Result.LastValue;

            double atrMult = Math.Max(0.0, MinSlAtrMult);
            double minSlPriceFromAtr = atrValue * atrMult;

            double minSlPriceFinal = Math.Max(minSlPriceFromPips, minSlPriceFromAtr);

            if (minSlPriceFinal > 0.0 && slDistancePrice < minSlPriceFinal)
            {
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
                return;

            // ===== Max lots cap guard (0=Off) =====
            if (MaxLotsCap > 0.0)
            {
                double maxUnitsNorm = Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MaxLotsCap), RoundingMode.Down);
                long maxUnits = (long)maxUnitsNorm;

                if (maxUnits > 0 && volumeInUnits > maxUnits)
                {
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
            if (targetValid)
                tpPips = tpPipsFromTarget;
            else
                tpPips = slPips * RiskReward;

            ExecuteMarketOrder(
                type,
                SymbolName,
                volumeInUnits,
                "PIVOT_BOUNCE_v0_1_0",
                slPips,
                tpPips
            );

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
