// ============================================================
// CODE NAME (Project Constitution compliant)
// ============================================================
// BASE: EMA_M5_ALL_DAY_018_002_XAUUSD_M5
// THIS: EMA_M5_ALL_DAY_018_003_XAUUSD_M5
// ARCHIVE_ID: 003 (file export marker; no runtime effect)
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class EA_DEV_FIRST : Robot
    {
        // ============================================================
        // CODE NAME (Project Constitution compliant)
        // ============================================================
        // BASE: EMA_M5_ALL_DAY_018_002_XAUUSD_M5
        // THIS: EMA_M5_ALL_DAY_018_003_XAUUSD_M5
        private const string CODE_NAME = "EMA_M5_ALL_DAY_018_003_XAUUSD_M5";
        private const string BOT_LABEL = "EMA_M5_ALL_DAY_018_003_XAUUSD_M5";
        // ============================================================

        // ============================================================
        // パラメーター
        // ============================================================

        #region 資金管理・ロット制御

        [Parameter("１トレードのリスク額", Group = "資金管理・ロット制御", DefaultValue = 1000.0, MinValue = 0.0)]
        public double RiskDollars { get; set; }

        [Parameter("リスク計算バッファ（PIPS）", Group = "資金管理・ロット制御", DefaultValue = 50.0, MinValue = 0.0)]
        public double RiskBufferPips { get; set; }

        [Parameter("緊急クローズ倍率", Group = "資金管理・ロット制御", DefaultValue = 1.2, MinValue = 1.0)]
        public double EmergencyCloseMult { get; set; }

        [Parameter("最大ポジション数", Group = "資金管理・ロット制御", DefaultValue = 1, MinValue = 1)]
        public int MaxPositions { get; set; }

        [Parameter("最大ロット数（0=無制限）", Group = "資金管理・ロット制御", DefaultValue = 2.5, MinValue = 0.0)]
        public double MaxLotsCap { get; set; }

        [Parameter("建値移動トリガー", Group = "資金管理・ロット制御", DefaultValue = 1000.0, MinValue = 0.0)]
        public double BreakevenTriggerDollars { get; set; }

        #endregion

        #region エントリー関連

        [Parameter("最大スプレッド（PIPS）（0=無効）", Group = "エントリー関連", DefaultValue = 0.0, MinValue = 0.0)]
        public double MaxSpreadPips { get; set; }

        #endregion

        #region ストップロス関連

        [Parameter("最小SL（PIPS）", Group = "ストップロス関連", DefaultValue = 20.0, MinValue = 0.0)]
        public double MinSLPips { get; set; }

        [Parameter("最小SL用ATR期間", Group = "ストップロス関連", DefaultValue = 14, MinValue = 1)]
        public int MinSlAtrPeriod { get; set; }

        [Parameter("最小SL用ATR倍率", Group = "ストップロス関連", DefaultValue = 0.5, MinValue = 0.0)]
        public double MinSlAtrMult { get; set; }

        #endregion

        #region 利確（TP）関連

        [Parameter("最小TP距離（PIPS）", Group = "利確（TP）関連", DefaultValue = 0.0, MinValue = 0.0)]
        public double MinTpDistancePips { get; set; }

        #endregion

        #region 方向フィルタ

        [Parameter("Buy Only（買いのみ）", Group = "方向フィルタ", DefaultValue = false)]
        public bool BuyOnly { get; set; }

        [Parameter("Sell Only（売りのみ）", Group = "方向フィルタ", DefaultValue = false)]
        public bool SellOnly { get; set; }

        [Parameter("EMA方向フィルター（はい・いいえ）", Group = "方向フィルタ", DefaultValue = false)]
        public bool EnableEmaDirectionFilter { get; set; }

        #endregion

        #region 各ロジック（EMA）

        [Parameter("EMA期間", Group = "各ロジック（EMA）", DefaultValue = 20, MinValue = 1)]
        public int EmaPeriod { get; set; }

        #endregion

        #region 保有時間制御

        [Parameter("最小保有時間（分）", Group = "保有時間制御", DefaultValue = 5, MinValue = 0)]
        public int MinHoldMinutes { get; set; }

        #endregion

        #region リスクリワード

        [Parameter("最低RR比", Group = "リスクリワード", DefaultValue = 1.0, MinValue = 0.0)]
        public double MinRRRatio { get; set; }

        #endregion

        #region 経済指標フィルター（UTC）

        [Parameter("経済指標フィルターを有効にする（はい・いいえ）", Group = "経済指標フィルター（UTC）", DefaultValue = true)]
        public bool EnableNewsFilter { get; set; }

        [Parameter("指標前の停止時間（分）", Group = "経済指標フィルター（UTC）", DefaultValue = 60)]
        public int MinutesBeforeNews { get; set; }

        [Parameter("指標後の再開時間（分）", Group = "経済指標フィルター（UTC）", DefaultValue = 60)]
        public int MinutesAfterNews { get; set; }

        #endregion

        #region 取引時間帯（JST）

        [Parameter("取引時間制御を有効にする（はい・いいえ）", Group = "取引時間帯（JST）", DefaultValue = true)]
        public bool EnableTradingWindowFilter { get; set; }

        [Parameter("取引開始（JST）", Group = "取引時間帯（JST）", DefaultValue = "09:15")]
        public string TradeStartTimeJst { get; set; }

        [Parameter("取引終了（JST）", Group = "取引時間帯（JST）", DefaultValue = "02:00")]
        public string TradeEndTimeJst { get; set; }

        [Parameter("強制フラット（JST）", Group = "取引時間帯（JST）", DefaultValue = "02:50")]
        public string ForceFlatTimeJst { get; set; }

        #endregion

        // ============================================================
        // 状態
        // ============================================================

        private AverageTrueRange _atrMinSl;
        private ExponentialMovingAverage _ema;

        private TimeZoneInfo _jstTz;

        private readonly HashSet<long> _emergencyCloseRequested = new HashSet<long>();
        private bool _stopRequestedByNewsFailure = false;

        private enum TradingWindowState { AllowNewEntries = 0, HoldOnly = 1, ForceFlat = 2 }
        private int _tradeStartMinJst;
        private int _tradeEndMinJst;
        private int _forceFlatMinJst;

        // --- News provider split (future API-ready) ---
        private interface INewsProvider
        {
            bool TryGetHighImpactEventsUtc(out List<DateTime> eventsUtc, out string error);
        }

        private sealed class HardcodedUsdHighImpactNewsProvider2025 : INewsProvider
        {
            public bool TryGetHighImpactEventsUtc(out List<DateTime> eventsUtc, out string error)
            {
                eventsUtc = new List<DateTime>();
                error = null;

                try
                {
                    // NOTE: backtest scaffold only (2025 USD key events). Future: replace with API provider.
                    string raw = @"
DateTime,Event,Importance
2025-01-10 13:30:00,Non-Farm Payrolls,High
2025-01-15 13:30:00,CPI m/m,High
2025-01-29 19:00:00,FOMC Statement,High
2025-02-07 13:30:00,Non-Farm Payrolls,High
2025-02-12 13:30:00,CPI m/m,High
2025-03-07 13:30:00,Non-Farm Payrolls,High
2025-03-12 12:30:00,CPI m/m,High
2025-03-19 18:00:00,FOMC Statement,High
2025-04-04 12:30:00,Non-Farm Payrolls,High
2025-04-10 12:30:00,CPI m/m,High
2025-05-02 12:30:00,Non-Farm Payrolls,High
2025-05-07 18:00:00,FOMC Statement,High
2025-05-14 12:30:00,CPI m/m,High
2025-06-06 12:30:00,Non-Farm Payrolls,High
2025-06-11 12:30:00,CPI m/m,High
2025-06-18 18:00:00,FOMC Statement,High
2025-07-04 12:30:00,Non-Farm Payrolls,High
2025-07-10 12:30:00,CPI m/m,High
2025-07-30 18:00:00,FOMC Statement,High
2025-08-01 12:30:00,Non-Farm Payrolls,High
2025-08-13 12:30:00,CPI m/m,High
2025-09-05 12:30:00,Non-Farm Payrolls,High
2025-09-10 12:30:00,CPI m/m,High
2025-09-17 18:00:00,FOMC Statement,High
2025-10-03 12:30:00,Non-Farm Payrolls,High
2025-10-15 12:30:00,CPI m/m,High
2025-10-29 18:00:00,FOMC Statement,High
2025-11-07 13:30:00,Non-Farm Payrolls,High
2025-11-12 13:30:00,CPI m/m,High
2025-12-05 13:30:00,Non-Farm Payrolls,High
2025-12-10 13:30:00,CPI m/m,High
2025-12-17 19:00:00,FOMC Statement,High
";
                    ParseHighImpactCsvUtc(raw, eventsUtc);
                    eventsUtc.Sort();
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.GetType().Name + ": " + ex.Message;
                    eventsUtc = null;
                    return false;
                }
            }

            private static void ParseHighImpactCsvUtc(string raw, List<DateTime> outEventsUtc)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                var unique = new HashSet<string>();
                string[] lines = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim().Trim('\r');
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("DateTime", StringComparison.OrdinalIgnoreCase)) continue;

                    string[] parts = line.Split(new[] { ',' }, 3);
                    if (parts.Length < 1) continue;

                    string dtText = parts[0].Trim();
                    string imp = parts.Length >= 3 ? (parts[2] ?? "").Trim() : "";

                    if (!imp.ToUpperInvariant().Contains("HIGH"))
                        continue;

                    if (!TryParseUtcDateTime(dtText, out DateTime dtUtc))
                        continue;

                    string key = dtUtc.ToString("o");
                    if (!unique.Add(key)) continue;

                    outEventsUtc.Add(dtUtc);
                }
            }

            private static bool TryParseUtcDateTime(string text, out DateTime utc)
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
        }

        private INewsProvider _newsProvider;
        private List<DateTime> _highImpactEventsUtc = new List<DateTime>();

        // ============================================================
        // ライフサイクル
        // ============================================================

        protected override void OnStart()
        {
            _jstTz = ResolveTokyoTimeZone();

            _atrMinSl = Indicators.AverageTrueRange(Math.Max(1, MinSlAtrPeriod), MovingAverageType.Simple);
            _ema = Indicators.ExponentialMovingAverage(Bars.ClosePrices, Math.Max(1, EmaPeriod));

            ResolveTradingWindowMinutesOrDefaults();

            // News provider (backtest scaffold). Future: swap to API provider.
            _newsProvider = new HardcodedUsdHighImpactNewsProvider2025();
            LoadNewsOrStop();

            Timer.Start(1);
            Positions.Closed += OnPositionClosed;

            Print(
                "Started | CodeName={0} | Label={1} | Symbol={2} | PipSize={3} PipValue={4} | Window(JST) {5}-{6} ForceFlat={7} | " +
                "Risk={8} BufferPips={9} EmergMult={10} | MinSL(PIPS)={11} ATR({12})*{13} | MinTP(PIPS)={14} | MinRR={15} | EMA={16} | " +
                "News={17} Before={18} After={19} | SpreadMax(PIPS)={20}",
                CODE_NAME,
                BOT_LABEL,
                SymbolName,
                Symbol.PipSize.ToString("G17", CultureInfo.InvariantCulture),
                Symbol.PipValue.ToString("G17", CultureInfo.InvariantCulture),
                TradeStartTimeJst,
                TradeEndTimeJst,
                ForceFlatTimeJst,
                RiskDollars.ToString("F2", CultureInfo.InvariantCulture),
                RiskBufferPips.ToString("F2", CultureInfo.InvariantCulture),
                EmergencyCloseMult.ToString("F2", CultureInfo.InvariantCulture),
                MinSLPips.ToString("F2", CultureInfo.InvariantCulture),
                MinSlAtrPeriod,
                MinSlAtrMult.ToString("F2", CultureInfo.InvariantCulture),
                MinTpDistancePips.ToString("F2", CultureInfo.InvariantCulture),
                Math.Max(0.0, MinRRRatio).ToString("F2", CultureInfo.InvariantCulture),
                EmaPeriod,
                EnableNewsFilter,
                Math.Max(0, MinutesBeforeNews),
                Math.Max(0, MinutesAfterNews),
                MaxSpreadPips.ToString("F2", CultureInfo.InvariantCulture)
            );
        }

        protected override void OnStop()
        {
            Positions.Closed -= OnPositionClosed;
        }

        protected override void OnTick()
        {
            ApplyBreakevenMoveIfNeeded();
            ApplyEmergencyCloseIfNeeded();
        }

        protected override void OnBar()
        {
            if (Bars == null || Bars.Count < 50)
                return;

            DateTime utcNow = DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);

            if (EnableTradingWindowFilter)
            {
                DateTime jstNow = ToJst(utcNow);
                TradingWindowState state = GetTradingWindowState(jstNow);
                if (state != TradingWindowState.AllowNewEntries)
                    return;
            }

            if (EnableNewsFilter)
            {
                if (_stopRequestedByNewsFailure)
                    return;

                if (IsInNewsWindow(utcNow))
                {
                    Print(
                        "NEWS_BLOCK | CodeName={0} | Symbol={1} | UtcNow={2:o} | BeforeMin={3} AfterMin={4}",
                        CODE_NAME,
                        SymbolName,
                        utcNow,
                        Math.Max(0, MinutesBeforeNews),
                        Math.Max(0, MinutesAfterNews)
                    );
                    return;
                }
            }

            if (Positions.Count >= Math.Max(1, MaxPositions))
                return;

            TryEmaEntry();
        }

        protected override void OnTimer()
        {
            if (!EnableTradingWindowFilter)
                return;

            DateTime utcNow = DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);
            DateTime jstNow = ToJst(utcNow);

            TradingWindowState state = GetTradingWindowState(jstNow);
            if (state != TradingWindowState.ForceFlat)
                return;

            CloseAllPositionsForSymbol("FORCE_FLAT");
        }

        // ============================================================
        // 資金計算・ロット制御
        // ============================================================

        private void ApplyEmergencyCloseIfNeeded()
        {
            double risk = Math.Max(0.0, RiskDollars);
            if (risk <= 0.0)
                return;

            double mult = Math.Max(1.0, EmergencyCloseMult);
            double threshold = -risk * mult;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                if (p.Label != BOT_LABEL) continue;

                if (_emergencyCloseRequested.Contains(p.Id))
                    continue;

                if (p.NetProfit <= threshold)
                {
                    _emergencyCloseRequested.Add(p.Id);

                    var res = ClosePosition(p);

                    Print(
                        "EMERGENCY_CLOSE | CodeName={0} | PosId={1} | NetProfit={2} <= Thr={3}",
                        CODE_NAME,
                        p.Id,
                        p.NetProfit.ToString("F2", CultureInfo.InvariantCulture),
                        threshold.ToString("F2", CultureInfo.InvariantCulture)
                    );

                    if (res == null || !res.IsSuccessful)
                        _emergencyCloseRequested.Remove(p.Id);
                }
            }
        }

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
                if (p.Label != BOT_LABEL) continue;
                if (p.NetProfit < trigger) continue;

                double entry = p.EntryPrice;

                if (p.TradeType == TradeType.Buy)
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value >= entry)
                        continue;

                    ModifyPosition(p, entry, p.TakeProfit, ProtectionType.Absolute);
                }
                else
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value <= entry)
                        continue;

                    ModifyPosition(p, entry, p.TakeProfit, ProtectionType.Absolute);
                }
            }
        }

        // ============================================================
        // エントリー関連
        // ============================================================

        private void TryEmaEntry()
        {
            if (_ema == null || _ema.Result == null || _ema.Result.Count < 3)
                return;

            int i1 = Bars.Count - 2;
            int i2 = Bars.Count - 3;
            if (i2 < 0) return;

            double close1 = Bars.ClosePrices[i1];
            double close2 = Bars.ClosePrices[i2];

            double ema1 = _ema.Result[i1];
            double ema2 = _ema.Result[i2];

            bool crossUp = close2 <= ema2 && close1 > ema1;
            bool crossDown = close2 >= ema2 && close1 < ema1;

            if (!crossUp && !crossDown)
                return;

            if (!SymbolInfoTick(out double bid, out double ask))
                return;

            if (!PassesSpreadFilter(bid, ask))
                return;

            TradeType type = crossUp ? TradeType.Buy : TradeType.Sell;
            double entry = (type == TradeType.Buy) ? ask : bid;

            if (BuyOnly && type == TradeType.Sell)
                return;

            if (SellOnly && type == TradeType.Buy)
                return;

            if (EnableEmaDirectionFilter)
            {
                if (type == TradeType.Buy && close1 <= ema1)
                    return;
                if (type == TradeType.Sell && close1 >= ema1)
                    return;
            }

            double minSlPrice = GetMinSlPrice();
            if (minSlPrice <= 0.0)
                return;

            double stop = (type == TradeType.Buy) ? entry - minSlPrice : entry + minSlPrice;

            double rr = Math.Max(1.0, Math.Max(0.0, MinRRRatio));
            double tpDistance = minSlPrice * rr;

            double minTpPrice = PipsToPrice(Math.Max(0.0, MinTpDistancePips));
            if (minTpPrice > 0.0 && tpDistance < minTpPrice)
                tpDistance = minTpPrice;

            double tp = (type == TradeType.Buy) ? entry + tpDistance : entry - tpDistance;

            PlaceTrade(type, entry, stop, tp, "EMA_CROSS");
        }

        private bool PassesSpreadFilter(double bid, double ask)
        {
            double max = Math.Max(0.0, MaxSpreadPips);
            if (max <= 0.0)
                return true;

            double spreadPips = (ask - bid) / Symbol.PipSize;
            return spreadPips <= max;
        }

        // ============================================================
        // SL関連
        // ============================================================

        private double GetMinSlPrice()
        {
            double pips = Math.Max(0.0, MinSLPips);
            double priceFromPips = PipsToPrice(pips);

            double atr = (_atrMinSl != null && _atrMinSl.Result != null && _atrMinSl.Result.Count > 0)
                ? _atrMinSl.Result.LastValue
                : 0.0;

            double priceFromAtr = Math.Max(0.0, MinSlAtrMult) * atr;

            return Math.Max(priceFromPips, priceFromAtr);
        }

        // ============================================================
        // TP関連 / リスクリワード / 注文実行
        // ============================================================

        private void PlaceTrade(TradeType type, double entry, double stop, double tpTargetPrice, string reasonTag)
        {
            double riskDollars = Math.Max(0.0, RiskDollars);
            if (riskDollars <= 0.0)
                return;

            double slDistancePrice = Math.Abs(entry - stop);
            if (slDistancePrice <= 0.0)
                return;

            double slPips = slDistancePrice / Symbol.PipSize;
            if (slPips <= 0.0)
                return;

            double bufferPips = Math.Max(0.0, RiskBufferPips);
            double sizingPips = slPips + bufferPips;
            if (sizingPips <= 0.0)
                return;

            double volumeUnitsRaw = riskDollars / (sizingPips * Symbol.PipValue);
            long volumeInUnits = (long)Symbol.NormalizeVolumeInUnits(volumeUnitsRaw, RoundingMode.Down);
            if (volumeInUnits < Symbol.VolumeInUnitsMin)
                return;

            if (MaxLotsCap > 0.0)
            {
                long maxUnits = (long)Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MaxLotsCap), RoundingMode.Down);
                if (maxUnits > 0 && volumeInUnits > maxUnits)
                    volumeInUnits = maxUnits;
            }

            double tpPipsFromTarget = Math.Abs(tpTargetPrice - entry) / Symbol.PipSize;
            if (tpPipsFromTarget <= 0.0)
                return;

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

            _emergencyCloseRequested.Remove(result.Position.Id);

            Print(
                "ENTRY | CodeName={0} | Symbol={1} | Type={2} | VolUnits={3} | Entry={4} | SLpips={5} | TPpips={6} | Reason={7}",
                CODE_NAME,
                SymbolName,
                type,
                volumeInUnits,
                entry.ToString("F5", CultureInfo.InvariantCulture),
                slPips.ToString("F1", CultureInfo.InvariantCulture),
                tpPipsFromTarget.ToString("F1", CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(reasonTag) ? "NA" : reasonTag
            );
        }

        // ============================================================
        // 経済指標フィルター（UTC）
        // ============================================================

        private void LoadNewsOrStop()
        {
            if (!EnableNewsFilter)
                return;

            if (_newsProvider == null)
            {
                StopByNewsFailure("NEWS_PROVIDER_NULL");
                return;
            }

            if (!_newsProvider.TryGetHighImpactEventsUtc(out List<DateTime> eventsUtc, out string error))
            {
                StopByNewsFailure(string.IsNullOrWhiteSpace(error) ? "NEWS_PROVIDER_FAIL" : error);
                return;
            }

            _highImpactEventsUtc = eventsUtc ?? new List<DateTime>();
            _highImpactEventsUtc.Sort();

            Print("ECON_LOADED | CodeName={0} | Count={1} | BeforeMin={2} AfterMin={3}", CODE_NAME, _highImpactEventsUtc.Count, Math.Max(0, MinutesBeforeNews), Math.Max(0, MinutesAfterNews));
        }

        private void StopByNewsFailure(string reason)
        {
            if (_stopRequestedByNewsFailure)
                return;

            _stopRequestedByNewsFailure = true;

            Print("NEWS_FATAL | CodeName={0} | Symbol={1} | Action=STOP | Reason={2}", CODE_NAME, SymbolName, string.IsNullOrWhiteSpace(reason) ? "NA" : reason);

            Stop();
        }

        private bool IsInNewsWindow(DateTime utcNow)
        {
            if (_highImpactEventsUtc == null || _highImpactEventsUtc.Count == 0)
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

        // ============================================================
        // 取引時間帯（JST）
        // ============================================================

        private void ResolveTradingWindowMinutesOrDefaults()
        {
            _tradeStartMinJst = ParseHhMmToMinutes(TradeStartTimeJst, 9 * 60 + 15);
            _tradeEndMinJst = ParseHhMmToMinutes(TradeEndTimeJst, 2 * 60);
            _forceFlatMinJst = ParseHhMmToMinutes(ForceFlatTimeJst, 2 * 60 + 50);
        }

        private int ParseHhMmToMinutes(string hhmm, int fallback)
        {
            if (string.IsNullOrWhiteSpace(hhmm))
                return fallback;

            string[] parts = hhmm.Trim().Split(':');
            if (parts.Length != 2)
                return fallback;

            if (!int.TryParse(parts[0], out int hh))
                return fallback;
            if (!int.TryParse(parts[1], out int mm))
                return fallback;

            hh = Math.Max(0, Math.Min(23, hh));
            mm = Math.Max(0, Math.Min(59, mm));

            return hh * 60 + mm;
        }

        private TradingWindowState GetTradingWindowState(DateTime jstNow)
        {
            int nowMin = jstNow.Hour * 60 + jstNow.Minute;

            // Priority (robust for midnight-crossing windows):
            // 1) ForceFlat  : ForceFlat -> TradeStart
            // 2) New entries: TradeStart -> TradeEnd
            // 3) Otherwise  : HoldOnly
            bool inForceFlat = IsInTimeWindow(nowMin, _forceFlatMinJst, _tradeStartMinJst);
            if (inForceFlat)
                return TradingWindowState.ForceFlat;

            bool inTradeWindow = IsInTimeWindow(nowMin, _tradeStartMinJst, _tradeEndMinJst);
            if (inTradeWindow)
                return TradingWindowState.AllowNewEntries;

            return TradingWindowState.HoldOnly;
        }

        private bool IsInTimeWindow(int nowMin, int startMin, int endMin)
        {
            if (startMin == endMin)
                return true;

            if (startMin < endMin)
                return nowMin >= startMin && nowMin < endMin;

            // crosses midnight
            return nowMin >= startMin || nowMin < endMin;
        }

        private void CloseAllPositionsForSymbol(string reason)
        {
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                if (p.Label != BOT_LABEL) continue;

                ClosePosition(p);

                Print("FORCE_CLOSE | CodeName={0} | PosId={1} | Reason={2}", CODE_NAME, p.Id, string.IsNullOrWhiteSpace(reason) ? "NA" : reason);
            }
        }

        // ============================================================
        // 共通ユーティリティ
        // ============================================================

        private double PipsToPrice(double pips)
        {
            return pips * Symbol.PipSize;
        }

        private DateTime ToJst(DateTime utc)
        {
            if (_jstTz == null)
                return utc;

            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _jstTz);
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

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            Position p = args.Position;
            if (p == null) return;
            if (p.SymbolName != SymbolName) return;
            if (p.Label != BOT_LABEL) return;

            double lots = Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits);

            Print(
                "CLOSE | CodeName={0} | PosId={1} | Type={2} | Lots={3} | Gross={4} | Net={5} | Pips={6}",
                CODE_NAME,
                p.Id,
                p.TradeType,
                lots.ToString("F2", CultureInfo.InvariantCulture),
                p.GrossProfit.ToString("F2", CultureInfo.InvariantCulture),
                p.NetProfit.ToString("F2", CultureInfo.InvariantCulture),
                p.Pips.ToString("F1", CultureInfo.InvariantCulture)
            );
        }
    }
}