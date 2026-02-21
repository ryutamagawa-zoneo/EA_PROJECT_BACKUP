using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HIVE_PivotBounce_v0_1_5 : Robot
    {
        // ============================================================
        // CODE NAME (file base name / versioned)
        // ============================================================
        private const string CODE_NAME = "HIVE_PivotBounce_v0_1_5_XAUUSD_M5";
        private const string BOT_LABEL = "PIVOT_BOUNCE_v0_1_5";
        // ============================================================

        // ================= PARAMETERS =================

        [Parameter("Risk Per Trade ($)", DefaultValue = 1000.0, MinValue = 0.0)]
        public double RiskDollars { get; set; }

        // User pips: 1 PIPS = $0.1. Internal pip (Symbol.PipSize=0.01) => x10
        [Parameter("Risk Buffer Pips (PIPS=0.1$)", DefaultValue = 50.0, MinValue = 0.0)]
        public double RiskBufferPips { get; set; }

        [Parameter("Emergency Close Mult", DefaultValue = 1.2, MinValue = 1.0)]
        public double EmergencyCloseMult { get; set; }

        [Parameter("Max Positions", DefaultValue = 1, MinValue = 1)]
        public int MaxPositions { get; set; }

        [Parameter("BE Move Trigger ($)", DefaultValue = 1000.0, MinValue = 0.0)]
        public double BreakevenTriggerDollars { get; set; }

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

        [Parameter("Min SL (PIPS=0.1$)", DefaultValue = 20.0, MinValue = 0.0)]
        public double MinSLPips { get; set; }

        [Parameter("Min SL ATR Period", DefaultValue = 14, MinValue = 1)]
        public int MinSlAtrPeriod { get; set; }

        [Parameter("Min SL ATR Mult", DefaultValue = 0.5, MinValue = 0.0)]
        public double MinSlAtrMult { get; set; }

        // ===== News Filter (stub list) =====
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

        // ================= DIAG counters (minimal) =================
        private long _diagOnTick;
        private long _diagOnBar;
        private long _diagEmergencyClosed;

        // Emergency close: per-position latch (avoid multi-close storms)
        private readonly HashSet<long> _emergencyCloseRequested = new HashSet<long>();

        private enum TradingWindowState { AllowNewEntries = 0, HoldOnly = 1, ForceFlat = 2 }

        // ============================================================
        // Phase2 Instrumentation (B): Entry meta + MFE/MAE + Exit Class
        // ============================================================

        private sealed class EntryMeta
        {
            public long PositionId;
            public TradeType TradeType;
            public DateTime EntryTimeUtc;
            public double EntryPrice;
            public double SlPrice;
            public double TpPrice;
            public string PivotLevel;
            public string ReasonTag;
        }

        private sealed class MfeMae
        {
            public double MfeDollars;
            public double MaeDollars;
        }

        private readonly Dictionary<long, EntryMeta> _metaByPosId = new Dictionary<long, EntryMeta>();
        private readonly Dictionary<long, MfeMae> _mfeMaeByPosId = new Dictionary<long, MfeMae>();

        // ============================================================

        protected override void OnStart()
        {
            _jstTz = ResolveTokyoTimeZone();
            _nyTz = ResolveNewYorkTimeZone();

            _atrMinSl = Indicators.AverageTrueRange(MinSlAtrPeriod, MovingAverageType.Simple);

            // frequent supervision
            Timer.Start(1);

            // Instrumentation hook
            Positions.Closed += OnPositionClosed;

            // EconomicCalendar (UTC stub)
            string economicCalendarRaw = @"
""DateTime,Event,Importance""
""2025-01-10 13:30:00,Non-Farm Payrolls,High""
""2025-01-15 13:30:00,CPI m/m,High""
""2025-01-29 19:00:00,FOMC Statement,High""
";
            LoadEconomicCalendarUtc(economicCalendarRaw);

            Print(
                "Started | CodeName={0} | Label={1} | Symbol={2} | PipSize={3} PipValue(1lot)={4} UnitsPerLot={5}",
                CODE_NAME,
                BOT_LABEL,
                SymbolName,
                Symbol.PipSize.ToString("G17", CultureInfo.InvariantCulture),
                Symbol.PipValue.ToString("G17", CultureInfo.InvariantCulture),
                Symbol.QuantityToVolumeInUnits(1.0).ToString("G17", CultureInfo.InvariantCulture)
            );
        }

        protected override void OnStop()
        {
            Positions.Closed -= OnPositionClosed;
        }

        protected override void OnTick()
        {
            _diagOnTick++;

            ApplyBreakevenMoveIfNeeded();

            // IMPORTANT: emergency close check should be OnTick-first (highest frequency).
            ApplyEmergencyCloseIfNeeded();

            // Instrumentation: track MFE/MAE per open position
            UpdateMfeMaeForOpenPositions();
        }

        protected override void OnBar()
        {
            _diagOnBar++;

            if (Bars.Count < 50)
                return;

            DateTime utcNow = Server.Time;

            if (EnableTradingWindowFilter)
            {
                DateTime jstNow = ToJst(utcNow);
                TradingWindowState state = GetTradingWindowState(jstNow);

                if (state == TradingWindowState.ForceFlat)
                    return;

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

        // ================= EMERGENCY CLOSE =================

        private void ApplyEmergencyCloseIfNeeded()
        {
            double risk = Math.Max(0.0, RiskDollars);
            if (risk <= 0.0)
                return;

            double mult = Math.Max(1.0, EmergencyCloseMult);
            double threshold = -risk * mult; // e.g. -1200

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;

                // if already requested once, don't spam
                if (_emergencyCloseRequested.Contains(p.Id))
                    continue;

                if (p.NetProfit <= threshold)
                {
                    _emergencyCloseRequested.Add(p.Id);

                    var res = ClosePosition(p);
                    _diagEmergencyClosed++;

                    Print(
                        "EMERGENCY_CLOSE | CodeName={0} | PosId={1} | NetProfit$={2} <= Thr$={3}",
                        CODE_NAME,
                        p.Id,
                        p.NetProfit.ToString("F2", CultureInfo.InvariantCulture),
                        threshold.ToString("F2", CultureInfo.InvariantCulture)
                    );

                    // If close fails, allow retry later (but don't loop in same tick)
                    if (res == null || !res.IsSuccessful)
                    {
                        // remove latch so it can try again in next ticks
                        _emergencyCloseRequested.Remove(p.Id);
                    }
                }
            }
        }

        // ================= BREAKEVEN =================

        private void ApplyBreakevenMoveIfNeeded()
        {
            double trigger = Math.Max(0.0, BreakevenTriggerDollars);
            if (trigger <= 0.0)
                return;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                if (p.NetProfit < trigger) continue;

                double entry = p.EntryPrice;

                if (p.TradeType == TradeType.Buy)
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value >= entry)
                        continue;

                    // Use ProtectionType to avoid obsolete warning
                    ModifyPosition(p, entry, p.TakeProfit, ProtectionType.Absolute);
                }
                else
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value <= entry)
                        continue;

                    // Use ProtectionType to avoid obsolete warning
                    ModifyPosition(p, entry, p.TakeProfit, ProtectionType.Absolute);
                }
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
            if (minuteBars == null || minuteBars.Count < 10)
                return false;

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
            // buffer for touch/bounce trigger (in PRICE)
            double bounceBufferPrice = (PivotBufferPips * 10.0) * Symbol.PipSize;

            const int WAIT_BARS_AFTER_TOUCH = 3;

            if (!SymbolInfoTick(out double bid, out double ask))
                return;

            if (UseS1R1Only)
            {
                if (IsBuyBounceAfterWait(_s1, bounceBufferPrice, WAIT_BARS_AFTER_TOUCH))
                    PlaceTrade(TradeType.Buy, ask, _s1 - bounceBufferPrice, _pp, "PIVOT_BOUNCE_S1_TP_PP");

                else if (IsSellBounceAfterWait(_r1, bounceBufferPrice, WAIT_BARS_AFTER_TOUCH))
                    PlaceTrade(TradeType.Sell, bid, _r1 + bounceBufferPrice, _pp, "PIVOT_BOUNCE_R1_TP_PP");

                return;
            }

            // (extended omitted here; keep your existing block if you use it)
        }

        private bool IsBuyBounceAfterWait(double supportPrice, double bufferPrice, int waitBars)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0) return false;

            if (waitBars < 0) waitBars = 0;
            if (signalIndex < waitBars) return false;

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
            if (lastTouchIndex < 0) return false;
            if (signalIndex - lastTouchIndex < waitBars) return false;

            return Bars.ClosePrices[signalIndex] > touchLine;
        }

        private bool IsSellBounceAfterWait(double resistancePrice, double bufferPrice, int waitBars)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0) return false;

            if (waitBars < 0) waitBars = 0;
            if (signalIndex < waitBars) return false;

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
            if (lastTouchIndex < 0) return false;
            if (signalIndex - lastTouchIndex < waitBars) return false;

            return Bars.ClosePrices[signalIndex] < touchLine;
        }

        // ================= EXECUTION (VARIABLE LOTS) =================

        private void PlaceTrade(TradeType type, double entry, double stop, double tpTargetPrice, string reasonTag)
        {
            double riskDollars = Math.Max(0.0, RiskDollars);
            if (riskDollars <= 0.0)
                return;

            if (Account.Balance <= 0)
                return;

            double slDistancePrice = Math.Abs(entry - stop);
            if (slDistancePrice <= 0.0)
                return;

            double slPips = slDistancePrice / Symbol.PipSize;
            if (slPips <= 0.0)
                return;

            // --------- Min SL guard (hybrid) ----------
            double minSlPipsFromPips = Math.Max(0.0, MinSLPips) * 10.0; // user pips -> internal
            double minSlPriceFromPips = minSlPipsFromPips * Symbol.PipSize;

            double atrValue = (_atrMinSl != null && _atrMinSl.Result != null && _atrMinSl.Result.Count > 0)
                ? _atrMinSl.Result.LastValue
                : 0.0;

            double minSlPriceFromAtr = Math.Max(0.0, MinSlAtrMult) * atrValue;
            double minSlPriceFinal = Math.Max(minSlPriceFromPips, minSlPriceFromAtr);

            if (minSlPriceFinal > 0.0 && slDistancePrice < minSlPriceFinal)
                return;

            // --------- Risk Buffer: size on (SL + buffer) ----------
            double bufferPipsInternal = Math.Max(0.0, RiskBufferPips) * 10.0; // user pips -> internal
            double sizingPips = slPips + bufferPipsInternal;
            if (sizingPips <= 0.0)
                return;

            // Core sizing
            double volumeUnitsRaw = riskDollars / (sizingPips * Symbol.PipValue);

            long volumeInUnits = (long)Symbol.NormalizeVolumeInUnits(volumeUnitsRaw, RoundingMode.Down);
            if (volumeInUnits < Symbol.VolumeInUnitsMin)
                return;

            // Max lots cap
            if (MaxLotsCap > 0.0)
            {
                long maxUnits = (long)Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MaxLotsCap), RoundingMode.Down);
                if (maxUnits > 0 && volumeInUnits > maxUnits)
                    volumeInUnits = maxUnits;
            }

            // TP validity check
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
                return;

            double lots = Symbol.VolumeInUnitsToQuantity(volumeInUnits);
            double expectedAtSl = slPips * Symbol.PipValue * lots;

            Print(
                "RISK_EXPECTED | CodeName={0} | Type={1} | SLPips={2} | BufferPips(User)={3} | SizingPips(Internal)={4} | VolUnits={5} | Lots={6} | PipSize={7} | PipValue(1lot)={8} | UnitsPerLot={9} | ExpectedAtSL$={10} | Risk$={11} | Tag={12}",
                CODE_NAME,
                type,
                slPips.ToString("F2", CultureInfo.InvariantCulture),
                RiskBufferPips.ToString("F2", CultureInfo.InvariantCulture),
                sizingPips.ToString("F2", CultureInfo.InvariantCulture),
                volumeInUnits,
                lots.ToString("F4", CultureInfo.InvariantCulture),
                Symbol.PipSize.ToString("G17", CultureInfo.InvariantCulture),
                Symbol.PipValue.ToString("G17", CultureInfo.InvariantCulture),
                Symbol.QuantityToVolumeInUnits(1.0).ToString("G17", CultureInfo.InvariantCulture),
                expectedAtSl.ToString("F2", CultureInfo.InvariantCulture),
                riskDollars.ToString("F2", CultureInfo.InvariantCulture),
                reasonTag
            );

            TradeResult result = ExecuteMarketOrder(
                type,
                SymbolName,
                volumeInUnits,
                BOT_LABEL,
                slPips,
                tpPipsFromTarget
            );

            if (result == null || !result.IsSuccessful || result.Position == null)
                return;

            // remove any stale emergency latch possibility
            _emergencyCloseRequested.Remove(result.Position.Id);

            // Instrumentation: store entry meta + initialize MFE/MAE trackers
            long posId = result.Position.Id;

            string pivotLevel = InferPivotLevelFromTag(reasonTag);

            var meta = new EntryMeta
            {
                PositionId = posId,
                TradeType = type,
                EntryTimeUtc = DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc),
                EntryPrice = entry,
                SlPrice = stop,
                TpPrice = tpTargetPrice,
                PivotLevel = pivotLevel,
                ReasonTag = reasonTag
            };

            _metaByPosId[posId] = meta;

            if (!_mfeMaeByPosId.ContainsKey(posId))
                _mfeMaeByPosId[posId] = new MfeMae { MfeDollars = 0.0, MaeDollars = 0.0 };

            Print(
                "ENTRY_TRACK | CodeName={0} | PosId={1} | Type={2} | EntryUTC={3:o} | EntryPrice={4} | SL={5} | TP={6} | Pivot={7} | Tag={8}",
                CODE_NAME,
                posId,
                type,
                meta.EntryTimeUtc,
                entry.ToString("G17", CultureInfo.InvariantCulture),
                stop.ToString("G17", CultureInfo.InvariantCulture),
                tpTargetPrice.ToString("G17", CultureInfo.InvariantCulture),
                pivotLevel,
                reasonTag
            );
        }

        private string InferPivotLevelFromTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return "NA";

            string u = tag.ToUpperInvariant();
            if (u.Contains("S1")) return "S1";
            if (u.Contains("R1")) return "R1";
            if (u.Contains("S2")) return "S2";
            if (u.Contains("R2")) return "R2";
            if (u.Contains("S3")) return "S3";
            if (u.Contains("R3")) return "R3";
            if (u.Contains("S4")) return "S4";
            if (u.Contains("R4")) return "R4";
            if (u.Contains("PP")) return "PP";
            return "NA";
        }

        // ============================================================
        // Instrumentation: MFE/MAE tracking
        // ============================================================

        private void UpdateMfeMaeForOpenPositions()
        {
            if (!SymbolInfoTick(out double bid, out double ask))
                return;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;

                long id = p.Id;

                if (!_mfeMaeByPosId.TryGetValue(id, out MfeMae mm))
                {
                    mm = new MfeMae { MfeDollars = 0.0, MaeDollars = 0.0 };
                    _mfeMaeByPosId[id] = mm;
                }

                double lots = Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits);

                // unrealized $ by current tick (bid/ask)
                double unrealDollars;
                if (p.TradeType == TradeType.Buy)
                {
                    double pips = (bid - p.EntryPrice) / Symbol.PipSize;
                    unrealDollars = pips * Symbol.PipValue * lots;
                }
                else
                {
                    double pips = (p.EntryPrice - ask) / Symbol.PipSize;
                    unrealDollars = pips * Symbol.PipValue * lots;
                }

                double mfeCandidate = Math.Max(0.0, unrealDollars);
                double maeCandidate = Math.Max(0.0, -unrealDollars);

                if (mfeCandidate > mm.MfeDollars) mm.MfeDollars = mfeCandidate;
                if (maeCandidate > mm.MaeDollars) mm.MaeDollars = maeCandidate;
            }
        }

        // ============================================================
        // Instrumentation: Close classification + unified close log
        // ============================================================

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (args == null || args.Position == null)
                return;

            Position p = args.Position;
            if (p.SymbolName != SymbolName)
                return;

            long posId = p.Id;

            DateTime closeUtc = GetCloseTimeSafe(args, p);
            DateTime jstClose = ToJst(closeUtc);

            // Meta
            _metaByPosId.TryGetValue(posId, out EntryMeta meta);

            double entryPrice = (meta != null) ? meta.EntryPrice : p.EntryPrice;
            double slPrice = (meta != null) ? meta.SlPrice : (p.StopLoss.HasValue ? p.StopLoss.Value : double.NaN);
            double tpPrice = (meta != null) ? meta.TpPrice : (p.TakeProfit.HasValue ? p.TakeProfit.Value : double.NaN);
            string pivotLevel = (meta != null) ? meta.PivotLevel : "NA";
            string tag = (meta != null) ? meta.ReasonTag : "NA";
            DateTime entryUtc = (meta != null) ? meta.EntryTimeUtc : DateTime.MinValue;

            // MFE/MAE
            _mfeMaeByPosId.TryGetValue(posId, out MfeMae mm);
            double mfe = (mm != null) ? mm.MfeDollars : 0.0;
            double mae = (mm != null) ? mm.MaeDollars : 0.0;

            double holdSeconds = 0.0;
            if (entryUtc != DateTime.MinValue)
                holdSeconds = Math.Max(0.0, (closeUtc - entryUtc).TotalSeconds);

            // Close price (event/position/tick fallback)
            string closePriceSource;
            double closePrice = GetClosePriceSafeWithSource(args, p, out closePriceSource);

            // classify
            string exitClass = ClassifyExit(jstClose, closePrice, slPrice, tpPrice);

            // Distance info (for diagnosis: near-TP/near-SL)
            double tpDistPips = double.NaN;
            double slDistPips = double.NaN;

            if (!double.IsNaN(tpPrice) && tpPrice > 0.0)
                tpDistPips = Math.Abs(tpPrice - entryPrice) / Symbol.PipSize;

            if (!double.IsNaN(slPrice) && slPrice > 0.0)
                slDistPips = Math.Abs(entryPrice - slPrice) / Symbol.PipSize;

            Print(
                "CLOSE_DIAG | CodeName={0} | PosId={1} | Type={2} | HoldSec={3} | Net$={4} | MFE$={5} | MAE$={6} | EntryUTC={7:o} | CloseUTC={8:o} | CloseJST={9:yyyy-MM-dd HH:mm:ss} | EntryPrice={10} | ClosePrice={11} | ClosePriceSource={12} | SL={13} | TP={14} | TpDistPips={15} | SlDistPips={16} | Pivot={17} | ExitClass={18} | Tag={19}",
                CODE_NAME,
                posId,
                p.TradeType,
                holdSeconds.ToString("F0", CultureInfo.InvariantCulture),
                p.NetProfit.ToString("F2", CultureInfo.InvariantCulture),
                mfe.ToString("F2", CultureInfo.InvariantCulture),
                mae.ToString("F2", CultureInfo.InvariantCulture),
                entryUtc,
                closeUtc,
                jstClose,
                entryPrice.ToString("G17", CultureInfo.InvariantCulture),
                double.IsNaN(closePrice) ? "NA" : closePrice.ToString("G17", CultureInfo.InvariantCulture),
                closePriceSource,
                double.IsNaN(slPrice) ? "NA" : slPrice.ToString("G17", CultureInfo.InvariantCulture),
                double.IsNaN(tpPrice) ? "NA" : tpPrice.ToString("G17", CultureInfo.InvariantCulture),
                double.IsNaN(tpDistPips) ? "NA" : tpDistPips.ToString("F2", CultureInfo.InvariantCulture),
                double.IsNaN(slDistPips) ? "NA" : slDistPips.ToString("F2", CultureInfo.InvariantCulture),
                pivotLevel,
                exitClass,
                tag
            );

            // cleanup
            _metaByPosId.Remove(posId);
            _mfeMaeByPosId.Remove(posId);
            _emergencyCloseRequested.Remove(posId);
        }

        // --- ClosePrice getter with source ---
        private double GetClosePriceSafeWithSource(PositionClosedEventArgs args, Position p, out string source)
        {
            source = "NA";

            // 1) Try args via reflection (expanded candidates)
            double v;

            if (TryGetDoubleProp(args, new[]
            {
                "ClosingPrice","ClosePrice","Price","ExecutionPrice","FillPrice","DealPrice","FilledPrice",
                "ClosingDealPrice","CloseDealPrice","AveragePrice","AvgPrice"
            }, out v))
            {
                if (v > 0.0)
                {
                    source = "Event";
                    return v;
                }
            }

            // 2) Try position via reflection (expanded candidates)
            if (TryGetDoubleProp(p, new[]
            {
                "ClosingPrice","ClosePrice","Price","ExecutionPrice","FillPrice","DealPrice","FilledPrice",
                "ClosingDealPrice","CloseDealPrice","AveragePrice","AvgPrice"
            }, out v))
            {
                if (v > 0.0)
                {
                    source = "Position";
                    return v;
                }
            }

            // 3) Tick fallback (規律: SymbolInfoTickを使用)
            if (SymbolInfoTick(out double bid, out double ask))
            {
                // If we cannot know exact fill, use side-appropriate reference
                if (p != null)
                {
                    if (p.TradeType == TradeType.Buy)
                    {
                        source = "TickFallback(Bid)";
                        return bid;
                    }

                    source = "TickFallback(Ask)";
                    return ask;
                }

                // Unknown -> prefer mid, but keep simple: bid
                source = "TickFallback(Bid)";
                return bid;
            }

            source = "NA";
            return double.NaN;
        }

        private bool TryGetDoubleProp(object obj, string[] propNames, out double value)
        {
            value = double.NaN;
            if (obj == null || propNames == null || propNames.Length == 0)
                return false;

            try
            {
                var t = obj.GetType();
                for (int i = 0; i < propNames.Length; i++)
                {
                    var pr = t.GetProperty(propNames[i]);
                    if (pr == null) continue;

                    var raw = pr.GetValue(obj, null);
                    if (raw == null) continue;

                    if (raw is double d)
                    {
                        value = d;
                        return true;
                    }

                    // some APIs might expose decimal
                    if (raw is decimal dc)
                    {
                        value = (double)dc;
                        return true;
                    }

                    // or string
                    if (raw is string s)
                    {
                        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double ds))
                        {
                            value = ds;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private string ClassifyExit(DateTime jstClose, double closePrice, double slPrice, double tpPrice)
        {
            // Force close window classification (JST) near configured time
            if (IsNearForceCloseJst(jstClose))
                return "FORCE_CLOSE_WINDOW";

            // If we cannot read closePrice, we cannot do price-match classification
            if (double.IsNaN(closePrice) || closePrice <= 0.0)
                return "OTHER_EXIT";

            // price match tolerance: 2 pips
            double tol = 2.0 * Symbol.PipSize;

            bool slValid = !double.IsNaN(slPrice) && slPrice > 0.0;
            bool tpValid = !double.IsNaN(tpPrice) && tpPrice > 0.0;

            if (slValid && Math.Abs(closePrice - slPrice) <= tol)
                return "STOP_LOSS_MATCH";

            if (tpValid && Math.Abs(closePrice - tpPrice) <= tol)
                return "TAKE_PROFIT_MATCH";

            return "OTHER_EXIT";
        }

        private bool IsNearForceCloseJst(DateTime jstTime)
        {
            // classify within +/- 60 seconds around configured (ForceCloseHourJst:ForceCloseMinuteJst:00)
            try
            {
                var target = new DateTime(jstTime.Year, jstTime.Month, jstTime.Day, ForceCloseHourJst, ForceCloseMinuteJst, 0, DateTimeKind.Unspecified);
                var delta = jstTime - target;
                double sec = Math.Abs(delta.TotalSeconds);
                return sec <= 60.0;
            }
            catch
            {
                return false;
            }
        }

        private DateTime GetCloseTimeSafe(PositionClosedEventArgs args, Position p)
        {
            try
            {
                if (args != null)
                {
                    var t = args.GetType();
                    var pr = t.GetProperty("ClosingTime") ?? t.GetProperty("Time");
                    if (pr != null)
                    {
                        var v = pr.GetValue(args, null);
                        if (v is DateTime dt)
                            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                    }
                }

                if (p != null)
                {
                    var t2 = p.GetType();
                    var pr2 = t2.GetProperty("ClosingTime") ?? t2.GetProperty("CloseTime");
                    if (pr2 != null)
                    {
                        var v2 = pr2.GetValue(p, null);
                        if (v2 is DateTime dt2)
                            return DateTime.SpecifyKind(dt2, DateTimeKind.Utc);
                    }
                }
            }
            catch
            {
                // ignore
            }

            return DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);
        }

        // ================= TIME / FILTERS =================

        protected override void OnTimer()
        {
            if (!EnableTradingWindowFilter)
                return;

            DateTime utcNow = Server.Time;
            DateTime jstNow = ToJst(utcNow);

            TradingWindowState state = GetTradingWindowState(jstNow);

            if (state == TradingWindowState.ForceFlat)
            {
                // Guard: if there is no open position on this symbol, do nothing (prevents log spam + needless calls)
                if (!HasOpenPositionsOnThisSymbol())
                    return;

                CloseAllPositionsOnThisSymbol("FORCE_CLOSE_WINDOW(JST)");
            }
        }

        private bool HasOpenPositionsOnThisSymbol()
        {
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                return true;
            }
            return false;
        }

        private void CloseAllPositionsOnThisSymbol(string reason)
        {
            int closeRequested = 0;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;

                ClosePosition(p);
                closeRequested++;
            }

            // Print only if we actually attempted to close at least one position
            if (closeRequested > 0)
            {
                Print(
                    "FORCE CLOSE | CodeName={0} | Symbol={1} | Reason={2} | CloseRequested={3}",
                    CODE_NAME,
                    SymbolName,
                    reason,
                    closeRequested
                );
            }
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

        private bool IsInRangeCircular(int nowMin, int startMin, int endMin)
        {
            if (startMin == endMin) return false;

            if (startMin < endMin)
                return nowMin >= startMin && nowMin < endMin;

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
            string[] candidateIds = new[] { "Tokyo Standard Time", "Asia/Tokyo" };
            foreach (string id in candidateIds)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            }
            return TimeZoneInfo.Utc;
        }

        private TimeZoneInfo ResolveNewYorkTimeZone()
        {
            string[] candidateIds = new[] { "Eastern Standard Time", "America/New_York" };
            foreach (string id in candidateIds)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            }
            return null;
        }

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
            if (_highImpactEventsUtc.Count == 0) return false;

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

        // ================= PRICE =================
        // 規律: 価格取得は必ず SymbolInfoTick を使用すること

        private bool SymbolInfoTick(out double bid, out double ask)
        {
            bid = 0.0;
            ask = 0.0;

            try
            {
                bid = Symbol.Bid;
                ask = Symbol.Ask;
                return bid > 0.0 && ask > 0.0;
            }
            catch
            {
                return false;
            }
        }
    }
}
