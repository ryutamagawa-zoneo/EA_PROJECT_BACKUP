// ============================================================
// CODE NAME (Project Constitution compliant)
// ============================================================
// BASE: EMA_M5_ALL_DAY_018_008_XAUUSD_M5
// THIS: EMA_M5_ALL_DAY_018_019_XAUUSD_M5
// ARCHIVE_ID: 018 (file export marker; no runtime effect)
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class EA_DEV_FIRST : Robot
    {
        // ============================================================
        // CODE NAME (Project Constitution compliant)
        // ============================================================
        // BASE: EMA_M5_ALL_DAY_018_007_XAUUSD_M5
        // THIS: EMA_M5_ALL_DAY_018_008_XAUUSD_M5
        private const string CODE_NAME = "EMA_M5_ALL_DAY_018_019_XAUUSD_M5";
        private const string BOT_LABEL = "EMA_M5_ALL_DAY_018_019_XAUUSD_M5";
        // ============================================================

        // ============================================================
        // パラメーター
        // ============================================================

        #region 資金管理・ロット制御

        public enum 口座通貨
        {
            USD = 0,
            JPY = 1
        }

        [Parameter("口座通貨（USD/JPY）", Group = "資金管理・ロット制御", DefaultValue = 口座通貨.USD)]
        public 口座通貨 AccountCurrency { get; set; }

        [Parameter("１トレードのリスク額", Group = "資金管理・ロット制御", DefaultValue = 1000.0, MinValue = 0.0)]
        public double RiskDollars { get; set; }

        [Parameter("１トレードのリスク（％）", Group = "資金管理・ロット制御", DefaultValue = 0.0, MinValue = 0.0, MaxValue = 100.0)]
        public double RiskPercent { get; set; }

        [Parameter("リスク計算バッファ（PIPS）", Group = "資金管理・ロット制御", DefaultValue = 50.0, MinValue = 0.0)]
        public double RiskBufferPips { get; set; }

        
        [Parameter("想定スリッページ（PIPS）（貴金属=あなたPIPS/$相当）", Group = "資金管理・ロット制御", DefaultValue = 50.0, MinValue = 0.0)]
        public double SlipAllowancePips { get; set; }

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

        #region リスクリワード

        [Parameter("最低RR比", Group = "リスクリワード", DefaultValue = 1.0, MinValue = 0.0)]
        public double MinRRRatio { get; set; }

        #endregion
        #region ログ

        [Parameter("デバッグログ（JST併記）", Group = "ログ", DefaultValue = false)]
        public bool EnableDebugLogJst { get; set; }

        #endregion

        #region 経済指標フィルター（UTC）

        [Parameter("バックテスト用（2025固定データを使用）", Group = "経済指標フィルター（UTC）", DefaultValue = false)]
        public bool UseNewsBacktest2025 { get; set; }

        [Parameter("フォワード用（FRED APIで取得）", Group = "経済指標フィルター（UTC）", DefaultValue = false)]
        public bool UseNewsForwardFRED { get; set; }

        [Parameter("FRED APIキー", Group = "経済指標フィルター（UTC）", DefaultValue = "")]
        public string FredApiKey { get; set; }

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
        private bool _stopRequestedByRiskFailure = false;

        private enum RiskMode { Amount = 0, Percent = 1 }

        private enum SymbolCategory { Metal = 0, Fx = 1 }
        private SymbolCategory _symbolCategory = SymbolCategory.Fx;
        private int _pipsScale = 1;


        private enum TradingWindowState { AllowNewEntries = 0, HoldOnly = 1, ForceFlat = 2 }

        // --- Debug / block-state tracking (for JST log tagging and throttled logs) ---
        private TradingWindowState _lastTradingWindowState = TradingWindowState.AllowNewEntries;
        private bool _wasNewsBlocked = false;
        private bool _wasSpreadBlocked = false;
        private int _tradeStartMinJst;
        private int _tradeEndMinJst;
        private int _forceFlatMinJst;

        // --- News provider split (future API-ready) ---
        // ============================================================
        // ライフサイクル
        // ============================================================

        protected override void OnStart()
        {
            _jstTz = ResolveTokyoTimeZone();

            _atrMinSl = Indicators.AverageTrueRange(Math.Max(1, MinSlAtrPeriod), MovingAverageType.Simple);
            _ema = Indicators.ExponentialMovingAverage(Bars.ClosePrices, Math.Max(1, EmaPeriod));

            ResolveTradingWindowMinutesOrDefaults();

            // Symbol category (METAL vs FX) and pips input scale
            string sym = (SymbolName ?? "").Trim().ToUpperInvariant();
            _symbolCategory = (sym.Contains("XAU") || sym.Contains("XAG")) ? SymbolCategory.Metal : SymbolCategory.Fx;
            _pipsScale = (_symbolCategory == SymbolCategory.Metal) ? 10 : 1;

            // News provider (backtest scaffold). Future: swap to API provider.

            ValidateAccountCurrencyOrStop();
            if (_stopRequestedByRiskFailure)
                return;

            ValidateRiskInputsOrStop();
            if (_stopRequestedByRiskFailure)
                return;

            Timer.Start(1);
            Positions.Closed += OnPositionClosed;

            DateTime utcNow = UtcNow();

            Print(
                "Started | CodeName={0} | Label={1} | Symbol={2} | PipSize={3} PipValue={4} | Window(JST) {5}-{6} ForceFlat={7} | " +
                "AccountCcy={8} RiskAmt={9} RiskPct={10} BufferPips={11} EmergMult={12} | MinSL(PIPS)={13} ATR({14})*{15} | MinTP(PIPS)={16} | MinRR={17} | EMA={18} | " +
                "NewsMode={19} Before={20} After={21} | SpreadMax(PIPS)={22}{23} | Mode={24} PipsScale={25} SlipAllow={26} SlipAllowInt={27}",
                CODE_NAME,
                BOT_LABEL,
                SymbolName,
                Symbol.PipSize.ToString("G17", CultureInfo.InvariantCulture),
                Symbol.PipValue.ToString("G17", CultureInfo.InvariantCulture),
                TradeStartTimeJst,
                TradeEndTimeJst,
                ForceFlatTimeJst,
                AccountCurrency,
                RiskDollars.ToString("F2", CultureInfo.InvariantCulture),
                RiskPercent.ToString("F2", CultureInfo.InvariantCulture),
                RiskBufferPips.ToString("F2", CultureInfo.InvariantCulture),
                EmergencyCloseMult.ToString("F2", CultureInfo.InvariantCulture),
                MinSLPips.ToString("F2", CultureInfo.InvariantCulture),
                MinSlAtrPeriod,
                MinSlAtrMult.ToString("F2", CultureInfo.InvariantCulture),
                MinTpDistancePips.ToString("F2", CultureInfo.InvariantCulture),
                Math.Max(0.0, MinRRRatio).ToString("F2", CultureInfo.InvariantCulture),
                EmaPeriod,
                (UseNewsBacktest2025 ? "BACKTEST_2025" : (UseNewsForwardFRED ? "FORWARD_FRED" : "DISABLED")),
                Math.Max(0, MinutesBeforeNews),
                Math.Max(0, MinutesAfterNews),
                MaxSpreadPips.ToString("F2", CultureInfo.InvariantCulture),
                BuildTimeTag(utcNow),
                _symbolCategory,
                _pipsScale,
                FormatUserPipsWithDollar(SlipAllowancePips),
                InputPipsToInternalPips(SlipAllowancePips).ToString("F1", CultureInfo.InvariantCulture)

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

            DateTime utcNow = UtcNow();

            if (EnableTradingWindowFilter)
            {
                DateTime jstNow = ToJst(utcNow);
                TradingWindowState state = GetTradingWindowState(jstNow);

                if (EnableDebugLogJst && state != _lastTradingWindowState)
                {
                    Print(
                        "TIME_BLOCK | CodeName={0} | Symbol={1} | State={2}{3}",
                        CODE_NAME,
                        SymbolName,
                        state,
                        BuildTimeTag(utcNow)
                    );
                    _lastTradingWindowState = state;
                }

                if (state != TradingWindowState.AllowNewEntries)
                    return;

                // NEWS MODULE (UTC) gate (new entries only)
                News_InitOrRefresh(utcNow);
                if (!IsNewEntryAllowed(utcNow, out string newsReason))
                {
                    if (EnableDebugLogJst && !_wasNewsBlocked)
                    {
                        Print("NEWS_BLOCK | CodeName={0} | Symbol={1} | Reason={2} | BeforeMin={3} AfterMin={4}{5}",
                            CODE_NAME, SymbolName, newsReason, Math.Max(0, MinutesBeforeNews), Math.Max(0, MinutesAfterNews), BuildTimeTag(utcNow));
                        _wasNewsBlocked = true;
                    }
                    return;
                }
                _wasNewsBlocked = false;


                // Reset block-flag when entries are allowed again
                _lastTradingWindowState = TradingWindowState.AllowNewEntries;
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


        private void ValidateAccountCurrencyOrStop()
        {
            // Manual selection must match actual account currency to prevent fatal risk misinterpretation.
            string acct = null;
            try
            {
                acct = Account.Currency;
            }
            catch
            {
                acct = null;
            }

            if (string.IsNullOrWhiteSpace(acct))
            {
                _stopRequestedByRiskFailure = true;
                Print(
                    "RISK_FATAL | CodeName={0} | Action=STOP | Reason=CURRENCY_UNKNOWN | ParamAccountCcy={1}{2}",
                    CODE_NAME,
                    AccountCurrency,
                    BuildTimeTag(UtcNow())
                );
                Stop();
                return;
            }

            string expected = (AccountCurrency == 口座通貨.USD) ? "USD" : "JPY";

            // Normalize common variants (e.g., 'JPY', 'USD')
            acct = acct.Trim().ToUpperInvariant();

            if (!string.Equals(acct, expected, StringComparison.OrdinalIgnoreCase))
            {
                _stopRequestedByRiskFailure = true;
                Print(
                    "RISK_FATAL | CodeName={0} | Action=STOP | Reason=CURRENCY_MISMATCH | Account={1} Param={2}{3}",
                    CODE_NAME,
                    acct,
                    expected,
                    BuildTimeTag(UtcNow())
                );
                Stop();
                return;
            }
        }

        private void ValidateRiskInputsOrStop()
        {
            double amt = Math.Max(0.0, RiskDollars);
            double pct = Math.Max(0.0, RiskPercent);

            // D) 併用禁止
            if (amt > 0.0 && pct > 0.0)
            {
                _stopRequestedByRiskFailure = true;
                Print(
                    "RISK_FATAL | CodeName={0} | Action=STOP | Reason=BOTH_AMOUNT_AND_PERCENT_SET | RiskAmt={1}({2}) RiskPct={3}{4}",
                    CODE_NAME,
                    amt.ToString("F2", CultureInfo.InvariantCulture),
                    AccountCurrency,
                    pct.ToString("F2", CultureInfo.InvariantCulture),
                    BuildTimeTag(UtcNow())
                );
                Stop();
                return;
            }

            if (amt <= 0.0 && pct <= 0.0)
            {
                _stopRequestedByRiskFailure = true;
                Print(
                    "RISK_FATAL | CodeName={0} | Action=STOP | Reason=NO_RISK_INPUT | RiskAmt={1}({2}) RiskPct={3}{4}",
                    CODE_NAME,
                    amt.ToString("F2", CultureInfo.InvariantCulture),
                    AccountCurrency,
                    pct.ToString("F2", CultureInfo.InvariantCulture),
                    BuildTimeTag(UtcNow())
                );
                Stop();
                return;
            }
        }

        private double GetRiskAmountInAccountCurrency(out RiskMode mode)
        {
            double amt = Math.Max(0.0, RiskDollars);
            double pct = Math.Max(0.0, RiskPercent);

            if (pct > 0.0)
            {
                mode = RiskMode.Percent;
                double pct01 = pct / 100.0;
                double bal = Math.Max(0.0, Account.Balance);
                return bal * pct01;
            }

            mode = RiskMode.Amount;
            return amt;
        }

        private void ApplyEmergencyCloseIfNeeded()
        {
            double risk = GetRiskAmountInAccountCurrency(out RiskMode _);
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
                        "EMERGENCY_CLOSE | CodeName={0} | PosId={1} | NetProfit={2} <= Thr={3}{4}",
                        CODE_NAME,
                        p.Id,
                        p.NetProfit.ToString("F2", CultureInfo.InvariantCulture),
                        threshold.ToString("F2", CultureInfo.InvariantCulture)
                    ,
                        BuildTimeTag(UtcNow())
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
            {
                if (EnableDebugLogJst && !_wasSpreadBlocked)
                {
                    double spreadPips = (ask - bid) / Symbol.PipSize;
                    Print(
                        "SPREAD_BLOCK | CodeName={0} | Symbol={1} | SpreadPips={2} | MaxPips={3}{4}",
                        CODE_NAME,
                        SymbolName,
                        spreadPips.ToString("F2", CultureInfo.InvariantCulture),
                        Math.Max(0.0, MaxSpreadPips).ToString("F2", CultureInfo.InvariantCulture),
                        BuildTimeTag(UtcNow())
                    );
                    _wasSpreadBlocked = true;
                }
                return;
            }

            _wasSpreadBlocked = false;

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
            // NEWS MODULE gate (safety): do not place orders inside news window / safe mode
            DateTime utcNow = UtcNow();
            News_InitOrRefresh(utcNow);
            if (!IsNewEntryAllowed(utcNow, out string newsReason))
            {
                if (EnableDebugLogJst)
                    Print("NEWS_BLOCK | CodeName={0} | Symbol={1} | Reason={2}{3}", CODE_NAME, SymbolName, newsReason, BuildTimeTag(utcNow));
                return;
            }

            if (_stopRequestedByRiskFailure)
                return;

            double riskAmount = GetRiskAmountInAccountCurrency(out RiskMode mode);
            if (riskAmount <= 0.0)
                return;

            double intendedSlDistancePrice = Math.Abs(entry - stop);
            if (intendedSlDistancePrice <= 0.0)
                return;

            double intendedSlPips = intendedSlDistancePrice / Symbol.PipSize;
            if (intendedSlPips <= 0.0)
                return;

            double tpPipsFromTarget = Math.Abs(tpTargetPrice - entry) / Symbol.PipSize;
            if (tpPipsFromTarget <= 0.0)
                return;

            double bufferPips = Math.Max(0.0, RiskBufferPips);

            double slipInputPips = Math.Max(0.0, SlipAllowancePips);
            double slipInternalPips = InputPipsToInternalPips(slipInputPips);

            // Attempt to ensure SL is accepted by the broker: if SL cannot be set, close immediately and retry with wider SL.
            const int maxAttempts = 5;
            const double stepPips = 10.0;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                double effectiveSlPips = intendedSlPips + (attempt * stepPips);
                if (effectiveSlPips <= 0.0)
                    continue;

                double sizingPips = effectiveSlPips + bufferPips + slipInternalPips;
                if (sizingPips <= 0.0)
                    continue;

                double volumeUnitsRaw = riskAmount / (sizingPips * Symbol.PipValue);
                long volumeInUnits = (long)Symbol.NormalizeVolumeInUnits(volumeUnitsRaw, RoundingMode.Down);
                if (volumeInUnits < Symbol.VolumeInUnitsMin)
                    return;

                if (MaxLotsCap > 0.0)
                {
                    long maxUnits = (long)Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MaxLotsCap), RoundingMode.Down);
                    if (maxUnits > 0 && volumeInUnits > maxUnits)
                        volumeInUnits = maxUnits;
                }

                TradeResult openRes = ExecuteMarketOrder(type, SymbolName, volumeInUnits, BOT_LABEL);
                if (openRes == null || !openRes.IsSuccessful || openRes.Position == null)
                    return;

                Position pos = openRes.Position;
                _emergencyCloseRequested.Remove(pos.Id);

                // Recompute absolute SL/TP from actual entry price
                double entryPrice = pos.EntryPrice;
                double slPriceDistance = effectiveSlPips * Symbol.PipSize;
                double slPrice = (type == TradeType.Buy) ? (entryPrice - slPriceDistance) : (entryPrice + slPriceDistance);
                double tpPriceDistance = tpPipsFromTarget * Symbol.PipSize;
                double tpPrice = (type == TradeType.Buy) ? (entryPrice + tpPriceDistance) : (entryPrice - tpPriceDistance);

                TradeResult modRes = ModifyPosition(pos, slPrice, tpPrice, ProtectionType.Absolute);

                bool slOk = pos.StopLoss.HasValue && pos.StopLoss.Value > 0.0;
                if (!slOk)
                {
                    // If SL is still not set after modify, close immediately to prevent uncontrolled risk.
                    ClosePosition(pos);

                    if (EnableDebugLogJst)
                    {
                        Print(
                            "SL_SET_FAILED | CodeName={0} | Symbol={1} | Attempt={2}/{3} | PosId={4} | IntendedSLpips={5} EffSLpips={6} | VolUnits={7} | ModOk={8} | Action=CLOSE_IMMEDIATELY | Reason={9}{10}",
                            CODE_NAME,
                            SymbolName,
                            attempt + 1,
                            maxAttempts,
                            pos.Id,
                            intendedSlPips.ToString("F1", CultureInfo.InvariantCulture),
                            effectiveSlPips.ToString("F1", CultureInfo.InvariantCulture),
                            volumeInUnits,
                            (modRes != null && modRes.IsSuccessful),
                            string.IsNullOrWhiteSpace(reasonTag) ? "NA" : reasonTag,
                            BuildTimeTag(UtcNow())
                        );
                    }

                    continue;
                }

                // Expected loss (at SL) in account currency (excludes commissions/slippage)
                double expectedLossAtSl = effectiveSlPips * Symbol.PipValue * volumeInUnits;

                double expectedLossWithSlip = sizingPips * Symbol.PipValue * volumeInUnits;

                Print(
                    "ENTRY | CodeName={0} | Symbol={1} | Type={2} | VolUnits={3} | Entry={4} | IntendedSLpips={5} EffSLpips={6} | TPpips={7} | RiskMode={8} RiskInput={9}({10}) Balance={11} | ExpLoss={12} | Reason={13} | SL_Set=OK{14} | SlipAllow={15} SlipAllowInt={16} | ExpLossAtSL={17}",
                    CODE_NAME,
                    SymbolName,
                    type,
                    volumeInUnits,
                    entryPrice.ToString("F5", CultureInfo.InvariantCulture),
                    intendedSlPips.ToString("F1", CultureInfo.InvariantCulture),
                    effectiveSlPips.ToString("F1", CultureInfo.InvariantCulture),
                    tpPipsFromTarget.ToString("F1", CultureInfo.InvariantCulture),
                    mode,
                    (mode == RiskMode.Percent ? RiskPercent.ToString("F2", CultureInfo.InvariantCulture) : RiskDollars.ToString("F2", CultureInfo.InvariantCulture)),
                    AccountCurrency,
                    Account.Balance.ToString("F2", CultureInfo.InvariantCulture),
                    expectedLossWithSlip.ToString("F2", CultureInfo.InvariantCulture),
                    string.IsNullOrWhiteSpace(reasonTag) ? "NA" : reasonTag,
                    BuildTimeTag(UtcNow()),
                    FormatUserPipsWithDollar(slipInputPips),
                    slipInternalPips.ToString("F1", CultureInfo.InvariantCulture),
                    expectedLossAtSl.ToString("F2", CultureInfo.InvariantCulture)

                );

                return;
            }
        }

        // ============================================================
        // ===== NEWS MODULE START =====================================
        // 経済指標フィルター（UTC）  ※移植可能ユニット
        // 外部公開は IsNewEntryAllowed() のみ
        // ============================================================

        // FRED Release IDs (fixed set)
        private const int FRED_RID_NFP = 50;   // Employment Situation
        private const int FRED_RID_CPI = 10;   // Consumer Price Index
        private const int FRED_RID_PCE = 54;   // Personal Income and Outlays (PCE/PCEPI etc.)
        private const int FRED_RID_FOMC = 101; // FOMC Press Release

        private List<DateTime> _newsEventsUtc = new List<DateTime>();
        private DateTime _newsLoadedUtcDate = DateTime.MinValue;
        private bool _newsBacktestLoaded = false;

        private bool _newsSafeMode = false;
        private DateTime _newsSafeUtcDate = DateTime.MinValue;
        private string _newsSafeReason = "";

        private TimeZoneInfo _easternTz;

        private void News_InitOrRefresh(DateTime utcNow, bool force = false)
        {
            // mode conflict
            if (UseNewsBacktest2025 && UseNewsForwardFRED)
            {
                Print("NEWS_FATAL | CodeName={0} | Symbol={1} | Action=STOP | Reason=MODE_CONFLICT (Backtest=ON & Forward=ON){2}",
                    CODE_NAME, SymbolName, BuildTimeTag(utcNow));
                Stop();
                return;
            }

            // disabled
            if (!UseNewsBacktest2025 && !UseNewsForwardFRED)
            {
                //副作用ゼロ：何もしない（イベントも空でOK）
                _newsEventsUtc.Clear();
                _newsSafeMode = false;
                return;
            }

            // Backtest mode: load once
            if (UseNewsBacktest2025)
            {
                if (_newsBacktestLoaded && !force)
                    return;

                _newsEventsUtc = LoadBacktestCalendarOrFallback(out string source, out string detail);
                _newsBacktestLoaded = true;
                _newsLoadedUtcDate = utcNow.Date;

                Print("NEWS_SOURCE | CodeName={0} | Symbol={1} | Mode=BACKTEST_2025 | Source={2} | Detail={3}{4}",
                    CODE_NAME, SymbolName, source, detail, BuildTimeTag(utcNow));

                _newsSafeMode = false;
                return;
            }

            // Forward mode: refresh on UTC date change (or force)
            DateTime todayUtc = utcNow.Date;
            if (!force && _newsLoadedUtcDate.Date == todayUtc)
                return;

            _easternTz = _easternTz ?? ResolveEasternTimeZone();
            if (_easternTz == null)
            {
                // cannot resolve eastern tz -> SAFE MODE
                _newsSafeMode = true;
                _newsSafeUtcDate = todayUtc;
                _newsSafeReason = "EASTERN_TZ_NOT_FOUND";
                _newsEventsUtc.Clear();

                Print("NEWS_SAFE_MODE | CodeName={0} | Symbol={1} | Reason={2}{3}",
                    CODE_NAME, SymbolName, _newsSafeReason, BuildTimeTag(utcNow));
                _newsLoadedUtcDate = todayUtc;
                return;
            }

            if (string.IsNullOrWhiteSpace(FredApiKey))
            {
                _newsSafeMode = true;
                _newsSafeUtcDate = todayUtc;
                _newsSafeReason = "APIKEY_EMPTY";
                _newsEventsUtc.Clear();

                Print("NEWS_SAFE_MODE | CodeName={0} | Symbol={1} | Reason={2}{3}",
                    CODE_NAME, SymbolName, _newsSafeReason, BuildTimeTag(utcNow));
                _newsLoadedUtcDate = todayUtc;
                return;
            }

            if (!TryLoadFredEventsForDate(todayUtc, out List<DateTime> eventsUtc, out string err))
            {
                _newsSafeMode = true;
                _newsSafeUtcDate = todayUtc;
                _newsSafeReason = "FRED_FAIL:" + (string.IsNullOrWhiteSpace(err) ? "NA" : err);
                _newsEventsUtc.Clear();

                Print("NEWS_SAFE_MODE | CodeName={0} | Symbol={1} | Reason={2}{3}",
                    CODE_NAME, SymbolName, _newsSafeReason, BuildTimeTag(utcNow));
                _newsLoadedUtcDate = todayUtc;
                return;
            }

            _newsEventsUtc = eventsUtc ?? new List<DateTime>();
            _newsLoadedUtcDate = todayUtc;
            _newsSafeMode = false;

            Print("NEWS_SOURCE | CodeName={0} | Symbol={1} | Mode=FORWARD_FRED | Events={2}{3}",
                CODE_NAME, SymbolName, _newsEventsUtc.Count, BuildTimeTag(utcNow));
        }

        // 外部公開：新規エントリー可否（ニュースのみ）
        private bool IsNewEntryAllowed(DateTime utcNow, out string blockReason)
        {
            blockReason = "OK";

            // disabled
            if (!UseNewsBacktest2025 && !UseNewsForwardFRED)
                return true;

            // conflict handled by init (Stop), but keep guard
            if (UseNewsBacktest2025 && UseNewsForwardFRED)
            {
                blockReason = "MODE_CONFLICT";
                return false;
            }

            // SAFE MODE: block new entries for the day
            if (_newsSafeMode && _newsSafeUtcDate.Date == utcNow.Date)
            {
                blockReason = "SAFE_MODE:" + (string.IsNullOrWhiteSpace(_newsSafeReason) ? "NA" : _newsSafeReason);
                return false;
            }

            if (_newsEventsUtc == null || _newsEventsUtc.Count == 0)
                return true;

            int before = Math.Max(0, MinutesBeforeNews);
            int after = Math.Max(0, MinutesAfterNews);

            for (int i = 0; i < _newsEventsUtc.Count; i++)
            {
                DateTime e = _newsEventsUtc[i];
                DateTime start = e.AddMinutes(-before);
                DateTime end = e.AddMinutes(after);

                if (utcNow >= start && utcNow <= end)
                {
                    blockReason = "NEWS_WINDOW";
                    return false;
                }
            }

            return true;
        }

        private List<DateTime> LoadBacktestCalendarOrFallback(out string source, out string detail)
        {
            source = "NA";
            detail = "NA";

            try
            {
                // 1) algo directory relative file (preferred)
                if (TryReadCalendarTextFromPath(CalendarFileName(), out string txt, out string usedPath))
                {
                    List<DateTime> ev = ParseEconomicCalendarCsvToUtcList(txt, out int total, out int kept);
                    source = "FILE";
                    detail = usedPath + $" | Total={total} Kept={kept}";
                    return ev;
                }
            }
            catch
            {
                // ignore; fallback below
            }

            // fallback: built-in minimal set for 2025 (NFP/CPI/FOMC)
            source = "FALLBACK";
            detail = "BUILTIN_2025_MIN";
            return ParseEconomicCalendarCsvToUtcList(BUILTIN_2025_CALENDAR_CSV, out _, out _);
        }

        private string CalendarFileName()
        {
            // fixed name for now (keeps behavior stable)
            return "EconomicCalendar.txt";
        }

        private bool TryReadCalendarTextFromPath(string fileName, out string text, out string usedPath)
        {
            text = null;
            usedPath = "NA";

            // Try relative (algo directory)
            try
            {
                string t = File.ReadAllText(fileName);
                text = t;
                usedPath = "ALGO_DIR:" + fileName;
                return true;
            }
            catch
            {
                // continue
            }

            // Try common documents locations
            string doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(doc))
            {
                string[] candidates = new string[]
                {
                    Path.Combine(doc, "cAlgo", "Data", "cBots", GetType().Name, fileName),
                    Path.Combine(doc, "cAlgo", "Data", "cBots", fileName),
                    Path.Combine(doc, "cAlgo", "Data", fileName),
                    Path.Combine(doc, "cAlgo", fileName),
                    Path.Combine(doc, "cTrader", "Data", "cBots", GetType().Name, fileName),
                    Path.Combine(doc, fileName)
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    try
                    {
                        if (File.Exists(candidates[i]))
                        {
                            text = File.ReadAllText(candidates[i]);
                            usedPath = candidates[i];
                            return true;
                        }
                    }
                    catch
                    {
                        // continue
                    }
                }
            }

            return false;
        }

        private List<DateTime> ParseEconomicCalendarCsvToUtcList(string csv, out int totalLines, out int keptLines)
        {
            totalLines = 0;
            keptLines = 0;

            List<DateTime> list = new List<DateTime>();
            if (string.IsNullOrWhiteSpace(csv))
                return list;

            string[] lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // header line skip
                if (line.StartsWith("Date", StringComparison.OrdinalIgnoreCase))
                    continue;

                totalLines++;

                // CSV: datetime,event,importance (event may contain commas in rare cases; keep simple)
                string[] parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                string dtStr = parts[0].Trim();
                string eventStr = parts[1].Trim();
                string impStr = (parts.Length >= 3 ? parts[2].Trim() : "");

                if (!IsHighImportance(impStr))
                    continue;

                if (!IsTargetBacktestEvent(eventStr))
                    continue;

                if (!TryParseUtcDateTime(dtStr, out DateTime utc))
                    continue;

                if (utc.Year != 2025)
                    continue;

                list.Add(utc);
                keptLines++;
            }

            list.Sort();
            return list;
        }

        private bool IsHighImportance(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return true; // allow if missing

            return s.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsTargetBacktestEvent(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            string u = s.ToUpperInvariant();
            if (u.Contains("NON-FARM") || u.Contains("NFP"))
                return true;
            if (u.Contains("CPI"))
                return true;
            if (u.Contains("FOMC"))
                return true;

            return false;
        }

        private bool TryParseUtcDateTime(string s, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            // Accept "yyyy-MM-dd HH:mm:ss"
            if (!DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dt))
                return false;

            utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return true;
        }

        private bool TryLoadFredEventsForDate(DateTime utcDate, out List<DateTime> eventsUtc, out string error)
        {
            eventsUtc = new List<DateTime>();
            error = "";

            string dateStr = utcDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            int[] rids = new int[] { FRED_RID_NFP, FRED_RID_CPI, FRED_RID_PCE, FRED_RID_FOMC };
            for (int i = 0; i < rids.Length; i++)
            {
                if (!TryFredHasReleaseOnDate(rids[i], dateStr, out bool hasRelease, out string err))
                {
                    error = err;
                    return false;
                }
                if (!hasRelease)
                    continue;

                DateTime eUtc = GetDefaultReleaseTimeUtcFromRid(utcDate, rids[i]);
                eventsUtc.Add(eUtc);
            }

            eventsUtc.Sort();
            return true;
        }

        private bool TryFredHasReleaseOnDate(int rid, string dateStr, out bool hasRelease, out string error)
        {
            hasRelease = false;
            error = "";

            try
            {
                string url =
                    "https://api.stlouisfed.org/fred/release/dates" +
                    "?release_id=" + rid.ToString(CultureInfo.InvariantCulture) +
                    "&realtime_start=" + dateStr +
                    "&realtime_end=" + dateStr +
                    "&api_key=" + Uri.EscapeDataString(FredApiKey ?? "") +
                    "&file_type=json";

                var resp = Http.Get(url);
                if (resp == null)
                {
                    error = "NULL_RESPONSE";
                    return false;
                }
                if (!resp.IsSuccessful)
                {
                    error = "HTTP_" + resp.StatusCode.ToString(CultureInfo.InvariantCulture);
                    return false;
                }
                string res = resp.Body;
                if (string.IsNullOrWhiteSpace(res))
                {
                    error = "EMPTY_RESPONSE";
                    return false;
                }

                // minimal JSON parse: look for "date":"YYYY-MM-DD"
                // If any match equals dateStr -> release exists
                int idx = 0;
                while (true)
                {
                    int p = res.IndexOf("\"date\"", idx, StringComparison.OrdinalIgnoreCase);
                    if (p < 0) break;
                    int q = res.IndexOf(":", p);
                    if (q < 0) break;
                    int s = res.IndexOf("\"", q + 1);
                    if (s < 0) break;
                    int e = res.IndexOf("\"", s + 1);
                    if (e < 0) break;
                    string d = res.Substring(s + 1, e - s - 1);
                    if (d == dateStr)
                    {
                        hasRelease = true;
                        return true;
                    }
                    idx = e + 1;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name;
                return false;
            }
        }

        private DateTime GetDefaultReleaseTimeUtcFromRid(DateTime utcDate, int rid)
        {
            // Use US/Eastern time with DST handling
            int hour = 8;
            int minute = 30;

            if (rid == FRED_RID_FOMC)
            {
                hour = 14;
                minute = 0;
            }

            DateTime easternLocal = new DateTime(utcDate.Year, utcDate.Month, utcDate.Day, hour, minute, 0, DateTimeKind.Unspecified);
            DateTime utc = TimeZoneInfo.ConvertTimeToUtc(easternLocal, _easternTz);
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        private TimeZoneInfo ResolveEasternTimeZone()
        {
            try
            {
                // Windows
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                return null;
            }
        }

        // Built-in calendar (CSV) for 2025 minimal set: DateTime(UTC),Event,Importance
        // NOTE: Used only when EconomicCalendar.txt cannot be found/read in Backtest mode.
        private const string BUILTIN_2025_CALENDAR_CSV = @"
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

        // ============================================================
        // ===== NEWS MODULE END =======================================
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

                Print("FORCE_CLOSE | CodeName={0} | PosId={1} | Reason={2}{3}", CODE_NAME, p.Id, string.IsNullOrWhiteSpace(reason) ? "NA" : reason, BuildTimeTag(UtcNow()));
            }
        }

        // ============================================================
        // 共通ユーティリティ
        // ============================================================

        private DateTime UtcNow()
        {
            return DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);
        }

        private string BuildTimeTag(DateTime utc)
        {
            if (!EnableDebugLogJst)
                return string.Empty;

            DateTime jst = ToJst(utc);
            string u = utc.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
            string j = jst.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, " | Utc={0} | Jst={1}", u, j);
        }

        private double PipsToPrice(double pips)
        {
            return pips * Symbol.PipSize;
        }

        private double InputPipsToInternalPips(double inputPips)
        {
            double v = Math.Max(0.0, inputPips);
            return v * _pipsScale;
        }

        private string FormatUserPipsWithDollar(double userPips)
        {
            double p = Math.Max(0.0, userPips);
            if (_symbolCategory == SymbolCategory.Metal)
            {
                double dollars = p / 10.0;
                return string.Format(CultureInfo.InvariantCulture, "{0}PIPS(${1})",
                    p.ToString("F1", CultureInfo.InvariantCulture),
                    dollars.ToString("F2", CultureInfo.InvariantCulture));
            }
            return string.Format(CultureInfo.InvariantCulture, "{0}PIPS",
                p.ToString("F1", CultureInfo.InvariantCulture));
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
                "CLOSE | CodeName={0} | PosId={1} | Type={2} | Lots={3} | Gross={4} | Net={5} | Pips={6}{7}",
                CODE_NAME,
                p.Id,
                p.TradeType,
                lots.ToString("F2", CultureInfo.InvariantCulture),
                p.GrossProfit.ToString("F2", CultureInfo.InvariantCulture),
                p.NetProfit.ToString("F2", CultureInfo.InvariantCulture),
                p.Pips.ToString("F1", CultureInfo.InvariantCulture)
            ,
                BuildTimeTag(UtcNow())
            );
        }
    }
}