using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HIVE_RiskFramework_v1_2_0 : Robot
    {
        // ================= PARAMETERS =================

        [Parameter("Risk Per Trade (%)", DefaultValue = 0.5)]
        public double RiskPercent { get; set; }

        [Parameter("Risk Reward", DefaultValue = 1.5)]
        public double RiskReward { get; set; }

        [Parameter("Equal High/Low Tolerance (pips)", DefaultValue = 2)]
        public double EqualTolerancePips { get; set; }

        [Parameter("Enable News Filter", DefaultValue = true)]
        public bool EnableNewsFilter { get; set; }

        [Parameter("Minutes Before News", DefaultValue = 60)]
        public int MinutesBeforeNews { get; set; }

        [Parameter("Minutes After News", DefaultValue = 60)]
        public int MinutesAfterNews { get; set; }

        // ===== Trading Window (JST) =====

        [Parameter("Enable Trading Window (JST)", DefaultValue = true)]
        public bool EnableTradingWindowFilter { get; set; }

        [Parameter("Trade Start Hour (JST)", DefaultValue = 8, MinValue = 0, MaxValue = 23)]
        public int TradeStartHourJst { get; set; }

        [Parameter("Trade Start Minute (JST)", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TradeStartMinuteJst { get; set; }

        [Parameter("Trade End Hour (JST)", DefaultValue = 17, MinValue = 0, MaxValue = 23)]
        public int TradeEndHourJst { get; set; }

        [Parameter("Trade End Minute (JST)", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TradeEndMinuteJst { get; set; }

        [Parameter("Force Close Hour (JST)", DefaultValue = 23, MinValue = 0, MaxValue = 23)]
        public int ForceCloseHourJst { get; set; }

        [Parameter("Force Close Minute (JST)", DefaultValue = 50, MinValue = 0, MaxValue = 59)]
        public int ForceCloseMinuteJst { get; set; }

        // ===== Existing general constraints =====

        [Parameter("Max Positions", DefaultValue = 1, MinValue = 1)]
        public int MaxPositions { get; set; }

        // ===== Guards (parameterized) =====

        [Parameter("Max Lots Cap (0=Off)", DefaultValue = 0.0, MinValue = 0.0)]
        public double MaxLotsCap { get; set; }

        [Parameter("Min SL Pips", DefaultValue = 50.0, MinValue = 0.0)]
        public double MinSLPips { get; set; }

        [Parameter("Min SL ATR Period", DefaultValue = 14, MinValue = 1)]
        public int MinSlAtrPeriod { get; set; }

        [Parameter("Min SL ATR Mult", DefaultValue = 0.5, MinValue = 0.0)]
        public double MinSlAtrMult { get; set; }

        // ================= STATE =================

        private bool _pendingBullishSweep;
        private bool _pendingBearishSweep;

        private readonly List<DateTime> _highImpactEventsUtc = new List<DateTime>();

        private AverageTrueRange _atrMinSl;

        private TimeZoneInfo _jstTz;

        // ================= CODE NAME (logging) =================
        private const string CODE_NAME = "LIQSweep_FVG_M5_ALL_DAY_003";
        private const string SESSION_SCOPE = "UNSPEC"; // ALL/TOKYO/EU_NY is not implemented in this cBot yet.

        // ================= LIFECYCLE =================

        protected override void OnStart()
        {
            _jstTz = ResolveTokyoTimeZone();

            _atrMinSl = Indicators.AverageTrueRange(MinSlAtrPeriod, MovingAverageType.Simple);

            // Start timer for force-close supervision.
            // (Timer interval is seconds)
            Timer.Start(1);

            // EconomicCalendar is treated as UTC.
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
                "Started. cBot={0} Symbol={1} JST={2} EventsLoaded={3} | Window(JST) Start={4:D2}:{5:D2} End={6:D2}:{7:D2} ForceClose={8:D2}:{9:D2} | Guard MinSLPips={10} ATRPeriod={11} ATRMult={12} MaxLotsCap={13}",
                nameof(HIVE_RiskFramework_v1_2_0),
                SymbolName,
                _jstTz != null ? _jstTz.DisplayName : "NULL",
                _highImpactEventsUtc.Count,
                TradeStartHourJst, TradeStartMinuteJst,
                TradeEndHourJst, TradeEndMinuteJst,
                ForceCloseHourJst, ForceCloseMinuteJst,
                MinSLPips.ToString("F2", CultureInfo.InvariantCulture),
                MinSlAtrPeriod,
                MinSlAtrMult.ToString("F2", CultureInfo.InvariantCulture),
                MaxLotsCap.ToString("F2", CultureInfo.InvariantCulture)
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

        protected override void OnBar()
        {
            if (Bars.Count < 20)
                return;

            if (Positions.Count >= MaxPositions)
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

            DetectLiquiditySweep();

            if (_pendingBullishSweep)
                TryBullishFvgEntry();

            if (_pendingBearishSweep)
                TryBearishFvgEntry();
        }

        // ================= LIQUIDITY SWEEP =================

        private void DetectLiquiditySweep()
        {
            double tolerance = EqualTolerancePips * Symbol.PipSize;

            // Equal lows → bullish sweep
            double l1 = Bars.LowPrices.Last(3);
            double l2 = Bars.LowPrices.Last(5);

            if (Math.Abs(l1 - l2) <= tolerance)
            {
                if (Bars.LowPrices.Last(1) < l1 && Bars.ClosePrices.Last(1) > l1)
                {
                    _pendingBullishSweep = true;
                    _pendingBearishSweep = false;
                }
            }

            // Equal highs → bearish sweep
            double h1 = Bars.HighPrices.Last(3);
            double h2 = Bars.HighPrices.Last(5);

            if (Math.Abs(h1 - h2) <= tolerance)
            {
                if (Bars.HighPrices.Last(1) > h1 && Bars.ClosePrices.Last(1) < h1)
                {
                    _pendingBearishSweep = true;
                    _pendingBullishSweep = false;
                }
            }
        }

        // ================= ENTRY ON FVG =================

        private void TryBullishFvgEntry()
        {
            // Bullish FVG: Low[1] > High[3]
            if (Bars.LowPrices.Last(1) <= Bars.HighPrices.Last(3))
                return;

            double entry = Symbol.Ask;

            double stop = FindRecentSwingLow();
            if (stop >= entry)
                return;

            PlaceTrade(TradeType.Buy, entry, stop);
            _pendingBullishSweep = false;
        }

        private void TryBearishFvgEntry()
        {
            // Bearish FVG: High[1] < Low[3]
            if (Bars.HighPrices.Last(1) >= Bars.LowPrices.Last(3))
                return;

            double entry = Symbol.Bid;

            double stop = FindRecentSwingHigh();
            if (stop <= entry)
                return;

            PlaceTrade(TradeType.Sell, entry, stop);
            _pendingBearishSweep = false;
        }

        // ================= EXECUTION =================

        private void PlaceTrade(TradeType type, double entry, double stop)
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

            // ===== (2) Min SL distance guard (Hybrid): max(MinSLPips, ATR(Period)*Mult) =====

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

            // ===== (1) Max lots cap guard (0=Off) =====
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

            double tpPips = slPips * RiskReward;

            ExecuteMarketOrder(
                type,
                SymbolName,
                volumeInUnits,
                "HRF_v1_2_0_LIQ_FVG",
                slPips,
                tpPips
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
                "GUARD SKIP | CodeName={0} | SessionScope={1} | TradeType={2} | Entry={3} | SL={4} | SLDistPips={5} | MinPips={6} | MinAtrPips={7} | MinFinalPips={8} | PlannedLots={9} | Reason={10}",
                CODE_NAME,
                SESSION_SCOPE,
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

        // ================= FORCE CLOSE (Symbol-wide, option B) =================

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
        }

        // ================= TRADING WINDOW (JST) =================

        private enum TradingWindowState
        {
            AllowNewEntries = 0,
            HoldOnly = 1,
            ForceFlat = 2
        }

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

        // ================= UTILITIES =================

        private double FindRecentSwingLow()
        {
            double low = double.MaxValue;
            for (int i = 2; i <= 10; i++)
                low = Math.Min(low, Bars.LowPrices.Last(i));
            return low;
        }

        private double FindRecentSwingHigh()
        {
            double high = double.MinValue;
            for (int i = 2; i <= 10; i++)
                high = Math.Max(high, Bars.HighPrices.Last(i));
            return high;
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
    }
}
