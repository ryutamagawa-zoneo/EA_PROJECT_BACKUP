// ============================================================
// CODE NAME (Project Constitution compliant)
// ============================================================
// BASE: PIVOT_BOUNCE_M5_ALL_DAY_017_013
// THIS: PIVOT_BOUNCE_M5_ALL_DAY_017_014
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HIVE_PivotBounce_v0_1_7 : Robot
    {
        // ============================================================
        // CODE NAME (Project Constitution compliant)
        // ============================================================
        // BASE: PIVOT_BOUNCE_M5_ALL_DAY_017_013
        // THIS: PIVOT_BOUNCE_M5_ALL_DAY_017_014
        private const string CODE_NAME = "PIVOT_BOUNCE_M5_ALL_DAY_017_014";
        private const string BOT_LABEL = "PIVOT_BOUNCE_v0_1_7_014";
        // ============================================================

        // ================= PARAMETERS =================

        // ===== 資金管理・ロット制御 =====

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

        // ===== ストップロス関連 =====

        [Parameter("最小SL（PIPS）", Group = "ストップロス関連", DefaultValue = 20.0, MinValue = 0.0)]
        public double MinSLPips { get; set; }

        [Parameter("最小SL用ATR期間", Group = "ストップロス関連", DefaultValue = 14, MinValue = 1)]
        public int MinSlAtrPeriod { get; set; }

        [Parameter("最小SL用ATR倍率", Group = "ストップロス関連", DefaultValue = 0.5, MinValue = 0.0)]
        public double MinSlAtrMult { get; set; }

        // ===== 利確（TP）関連 =====

        [Parameter("最小TP距離（PIPS）", Group = "利確（TP）関連", DefaultValue = 0.0, MinValue = 0.0)]
        public double MinTpDistancePips { get; set; }

        // ===== エントリー距離・再接近制御 =====

        [Parameter("エントリー最大距離（PIPS）", Group = "エントリー距離・再接近制御", DefaultValue = 50.0, MinValue = 0.0)]
        public double EntryMaxDistancePips { get; set; }

        [Parameter("再接近監視バー数", Group = "エントリー距離・再接近制御", DefaultValue = 36, MinValue = 1)]
        public int ReapproachWindowBars { get; set; }

        [Parameter("再接近最大距離（PIPS）", Group = "エントリー距離・再接近制御", DefaultValue = 40.0, MinValue = 0.0)]
        public double ReapproachMaxDistancePips { get; set; }

        // ===== 方向フィルタ =====

        // MODIFY(017_005): DefaultValue 20.0 -> 10.0
        [Parameter("方向判定デッドゾーン（PIPS）", Group = "方向フィルタ", DefaultValue = 10.0, MinValue = 0.0)]
        public double DirectionDeadzonePips { get; set; }

        // ADD(017_005): Hysteresis ratio parameter (Exit/Enter)
        [Parameter("方向判定ヒステリシス比率（EXIT/ENTER）", Group = "方向フィルタ", DefaultValue = 0.6, MinValue = 0.0)]
        public double DirectionHysteresisExitEnterRatio { get; set; }

        // ADD(017_006): Minimum hold bars after direction state change
        [Parameter("方向状態の最短維持バー数", Group = "方向フィルタ", DefaultValue = 2, MinValue = 0)]
        public int DirectionStateMinHoldBars { get; set; }

        // ADD(017_007): PP line-specific direction control (apply/disable + min hold override)
        [Parameter("PP用 Direction を有効化（はい・いいえ）", Group = "方向フィルタ", DefaultValue = true)]
        public bool EnablePpDirectionFilter { get; set; }

        [Parameter("PP用 方向状態の最短維持バー数", Group = "方向フィルタ", DefaultValue = 2, MinValue = 0)]
        public int PpDirectionStateMinHoldBars { get; set; }

        // ===== ピボット・タッチ判定 =====

        [Parameter("タッチ判定バッファ（PIPS）", Group = "ピボット・タッチ判定", DefaultValue = 2.0, MinValue = 0.0)]
        public double PivotBufferPips { get; set; }

        [Parameter("検証ログ出力（はい・いいえ）", Group = "ピボット・タッチ判定", DefaultValue = true)]
        public bool EnablePivotHitLog { get; set; }

        // ===== Pivot Day Rollover (NY) =====
        // パラメータ欄は削除（コード内固定）
        private const int PIVOT_ROLLOVER_HOUR_NY_FIXED = 17;
        private const int PIVOT_ROLLOVER_MINUTE_NY_FIXED = 0;

        // ===== Pivot Safeguard =====
        // ピボット計算が不成立・異常の場合は、EA強制停止（Stop）しない（NO_STOPでリトライ）
        [Parameter("ピボット異常時の停止（はい・いいえ）", Group = "ピボット・タッチ判定", DefaultValue = true)]
        public bool StopOnInvalidPivot { get; set; }

        [Parameter("タッチ有効期限（バー数）", Group = "ピボット・タッチ判定", DefaultValue = 60, MinValue = 1)]
        public int TouchExpireBars { get; set; }

        [Parameter("タッチ結果判定ルックバック（バー数）", Group = "ピボット・タッチ判定", DefaultValue = 20, MinValue = 2)]
        public int TouchResultLookbackBars { get; set; }

        [Parameter("最大タッチ足レンジ（PIPS）", Group = "ピボット・タッチ判定", DefaultValue = 200.0, MinValue = 0.0)]
        public double MaxTouchCandleRangePips { get; set; }

        [Parameter("最大連続ピボット欠損（0=無制限）", Group = "ピボット・タッチ判定", DefaultValue = 200, MinValue = 0)]
        public int MaxConsecutivePivotMissing { get; set; }

        // ===== 保有・時間制御 =====

        [Parameter("最小保有時間（分）", Group = "保有・時間制御", DefaultValue = 5, MinValue = 0)]
        public int MinHoldMinutes { get; set; }

        // ===== リスクリワード・フィルタ =====

        [Parameter("最低RR比", Group = "リスクリワード・フィルタ", DefaultValue = 1.0, MinValue = 0.0)]
        public double MinRRRatio { get; set; }

        [Parameter("MinRR緩和を有効にする（はい・いいえ）", Group = "リスクリワード・フィルタ", DefaultValue = true)]
        public bool EnableMinRrRelax { get; set; }

        [Parameter("MinRR緩和の猶予（バー数）", Group = "リスクリワード・フィルタ", DefaultValue = 6, MinValue = 0)]
        public int MinRrRelaxWindowBars { get; set; }

        [Parameter("緩和後の最低RR比", Group = "リスクリワード・フィルタ", DefaultValue = 0.7, MinValue = 0.0)]
        public double MinRrRelaxedRatio { get; set; }

        // ===== News Filter (UTC) =====  (UI規律: 取引時間帯（JST）の直上)

        [Parameter("経済指標フィルターを有効にする（はい・いいえ）", Group = "経済指標フィルター（UTC）", DefaultValue = true)]
        public bool EnableNewsFilter { get; set; }

        [Parameter("指標前の停止時間（分）", Group = "経済指標フィルター（UTC）", DefaultValue = 60)]
        public int MinutesBeforeNews { get; set; }

        [Parameter("指標後の再開時間（分）", Group = "経済指標フィルター（UTC）", DefaultValue = 60)]
        public int MinutesAfterNews { get; set; }

        // ===== Trading Window (JST) =====  (UI規律: 最下段)

        [Parameter("取引時間制御を有効にする（はい・いいえ）", Group = "取引時間帯（JST）", DefaultValue = true)]
        public bool EnableTradingWindowFilter { get; set; }

        [Parameter("取引開始（JST）", Group = "取引時間帯（JST）", DefaultValue = "09:15")]
        public string TradeStartTimeJst { get; set; }

        [Parameter("取引終了（JST）", Group = "取引時間帯（JST）", DefaultValue = "02:00")]
        public string TradeEndTimeJst { get; set; }

        [Parameter("強制フラット（JST）", Group = "取引時間帯（JST）", DefaultValue = "02:50")]
        public string ForceFlatTimeJst { get; set; }
        // ================= STATE =================

        private readonly List<DateTime> _highImpactEventsUtc = new List<DateTime>();
        private readonly List<EconomicEventItem> _highImpactEventItemsUtc = new List<EconomicEventItem>();

        private AverageTrueRange _atrMinSl;

        private TimeZoneInfo _jstTz;
        private TimeZoneInfo _nyTz;

        private DateTime _currentPivotSessionStartUtc = DateTime.MinValue;
        private bool _hasPivot;

        private double _pp, _r1, _r2, _r3, _r4, _s1, _s2, _s3, _s4;

        // Pivot cache (last valid)
        private bool _hasPivotCached;
        private double _ppCached, _r1Cached, _r2Cached, _r3Cached, _r4Cached, _s1Cached, _s2Cached, _s3Cached, _s4Cached;
        private DateTime _pivotCachedSessionStartUtc = DateTime.MinValue;
        private int _consecutivePivotMissing = 0;

        // ===== Outside S3/R3 Mode (for physical verification) =====
        private enum OutsideS3R3Mode
        {
            Inside = 0,
            BelowS3 = 1,
            AboveR3 = 2
        }

        private OutsideS3R3Mode _outsideMode = OutsideS3R3Mode.Inside;
        private DateTime _outsideModeLastChangeUtc = DateTime.MinValue;

        // ===== Re-entry Countertrend Mode (activated when price returns inside from outside S3/R3) =====
        private enum ReentryCounterMode
        {
            None = 0,
            AllowLongOnly = 1,
            AllowShortOnly = 2
        }

        private ReentryCounterMode _reentryMode = ReentryCounterMode.None;
        private DateTime _reentryModeLastChangeUtc = DateTime.MinValue;


        // Emergency close: per-position latch (avoid multi-close storms)
        private readonly HashSet<long> _emergencyCloseRequested = new HashSet<long>();

        private enum TradingWindowState { AllowNewEntries = 0, HoldOnly = 1, ForceFlat = 2 }

        // --- Trading window minutes (JST) resolved from string params ---
        private int _tradeStartMinJst = 0;
        private int _tradeEndMinJst = 0;
        private int _forceFlatMinJst = 0;

        // ============================================================
        // Phase2: Entry meta (MinHold gate support) + MFE/MAE
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

            // 017_014: Touch outcome snapshot for S2/S3/R2/R3 (evaluated at entry, logged at close)
            public TouchOutcome TouchS2;
            public TouchOutcome TouchS3;
            public TouchOutcome TouchR2;
            public TouchOutcome TouchR3;
        }

        private sealed class MfeMae
        {
            public double MfeDollars;
            public double MaeDollars;
        }

        private sealed class TouchOutcome
        {
            public bool Touched;
            public string Result; // Bounce / Break / None
            public DateTime TouchBarTimeUtc;
        }

        private sealed class EconomicEventItem
        {
            public DateTime UtcTime;
            public string EventName;
            public string Importance;
        }


        private readonly Dictionary<long, EntryMeta> _metaByPosId = new Dictionary<long, EntryMeta>();
        private readonly Dictionary<long, MfeMae> _mfeMaeByPosId = new Dictionary<long, MfeMae>();

        // ============================================================
        // Direction Filter (Fixed ε + hysteresis + time hold)
        // ============================================================

        private enum LineSideState
        {
            Neutral = 0,
            Above = 1,
            Below = 2
        }

        private LineSideState _stateS1 = LineSideState.Neutral;
        private LineSideState _stateR1 = LineSideState.Neutral;

        // ADD(017_006): last state change index for time-hold gating
        private int _lastStateChangeIndexS1 = -1;
        private int _lastStateChangeIndexR1 = -1;

        // ADD(017_007): PP state + last change index (PP only)
        private LineSideState _statePP = LineSideState.Neutral;
        private int _lastStateChangeIndexPP = -1;

        // ============================================================
        // Latest touch (ONE-LINE) memory
        // ============================================================

        private enum PivotLine
        {
            None = 0,
            PP = 1,
            S1 = 2,
            R1 = 3,
            S2 = 4,
            R2 = 5,
            S3 = 6,
            R3 = 7,
            S4 = 8,
            R4 = 9
        }

        private PivotLine _lastTouchedLine = PivotLine.None;
        private double _lastTouchedLinePrice = 0.0;
        private int _lastTouchedSignalIndex = -1;
        private DateTime _lastTouchedTimeUtc = DateTime.MinValue;

        // ============================================================
        // Entry distance staging (Pending re-approach)
        // ============================================================

        private bool _pendingReapproachActive = false;
        private int _pendingCreatedSignalIndex = -1;
        private PivotLine _pendingLine = PivotLine.None;
        private double _pendingLinePrice = 0.0;
        private TradeType _pendingTradeType = TradeType.Buy;
        private double _pendingStopPrice = 0.0;
        private double _pendingTpTargetPrice = 0.0;
        private string _pendingReasonTag = "NA";

        // ============================================================

        protected override void OnStart()
        {
            _jstTz = ResolveTokyoTimeZone();
            _nyTz = ResolveNewYorkTimeZone();

            _atrMinSl = Indicators.AverageTrueRange(MinSlAtrPeriod, MovingAverageType.Simple);

            ResolveTradingWindowMinutesOrDefaults();

            Timer.Start(1);

            Positions.Closed += OnPositionClosed;

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
                "Started | CodeName={0} | Label={1} | Symbol={2} | Window(JST) {3}-{4} ForceFlat={5} | " +
                "Guards: MinSL(PIPS)={6} ATR({7})*{8} | Risk={9} BufferPips={10} EmergMult={11} | News={12} Before={13} After={14} | " +
                "MinHoldMin={15} | MinTP(PIPS)={16} | DirDeadzone(PIPS)={17} | DirHystRatio={18} | DirMinHoldBars={19} | " +
                "EntryMaxDist(PIPS)={20} | ReapproachWinBars={21} | ReapproachMaxDist(PIPS)={22} | " +
                "TouchExpireBars={23} | MaxTouchRange(PIPS)={24} | PivotRollover(UTC)=00:00(FIXED) | StopOnInvalidPivot={25} | MaxPivotMissing={26} | MinRR={27}",
                CODE_NAME,
                BOT_LABEL,
                SymbolName,
                TradeStartTimeJst,
                TradeEndTimeJst,
                ForceFlatTimeJst,
                MinSLPips.ToString("F2", CultureInfo.InvariantCulture),
                MinSlAtrPeriod,
                MinSlAtrMult.ToString("F2", CultureInfo.InvariantCulture),
                RiskDollars.ToString("F2", CultureInfo.InvariantCulture),
                RiskBufferPips.ToString("F2", CultureInfo.InvariantCulture),
                EmergencyCloseMult.ToString("F2", CultureInfo.InvariantCulture),
                EnableNewsFilter,
                MinutesBeforeNews,
                MinutesAfterNews,
                MinHoldMinutes,
                MinTpDistancePips.ToString("F2", CultureInfo.InvariantCulture),
                DirectionDeadzonePips.ToString("F2", CultureInfo.InvariantCulture),
                Math.Max(0.0, DirectionHysteresisExitEnterRatio).ToString("F2", CultureInfo.InvariantCulture),
                Math.Max(0, DirectionStateMinHoldBars),
                EntryMaxDistancePips.ToString("F2", CultureInfo.InvariantCulture),
                ReapproachWindowBars,
                ReapproachMaxDistancePips.ToString("F2", CultureInfo.InvariantCulture),
                TouchExpireBars,
                MaxTouchCandleRangePips.ToString("F2", CultureInfo.InvariantCulture),
                StopOnInvalidPivot,
                MaxConsecutivePivotMissing,
                Math.Max(0.0, MinRRRatio).ToString("F2", CultureInfo.InvariantCulture)
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
            UpdateMfeMaeForOpenPositions();
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

                if (state == TradingWindowState.ForceFlat)
                    return;

                if (state != TradingWindowState.AllowNewEntries)
                    return;
            }
            if (EnableNewsFilter && IsInNewsWindow(utcNow))
            {
                Print(
                    "NEWS_BLOCK | CodeName={0} | Symbol={1} | UtcNow={2:o} | BeforeMin={3} AfterMin={4}",
                    CODE_NAME,
                    SymbolName,
                    DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
                    Math.Max(0, MinutesBeforeNews),
                    Math.Max(0, MinutesAfterNews)
                );
                return;
            }

            if (Positions.Count >= MaxPositions)
                return;

            UpdateDailyPivotIfNeeded(utcNow);
            if (!_hasPivot)
                return;

            if (SymbolInfoTick(out double bidOB, out double askOB))
                UpdateOutsideModeIfNeeded(bidOB, askOB, utcNow);

            UpdateLatestTouchIfNeeded();
            ExpireTouchIfNeeded();

            // (A) Pending re-approach check first
            if (ProcessPendingReapproachIfAny())
                return;

            // (B) Normal entry evaluation (may set pending)
            TryPivotBounceEntry_LatestTouchOnly();
        }

        // ================= OnTimer (ForceClose supervisor) =================

        protected override void OnTimer()
        {
            if (!EnableTradingWindowFilter)
                return;

            DateTime utcNow = Server.Time;
            DateTime jstNow = ToJst(utcNow);

            TradingWindowState state = GetTradingWindowState(jstNow);

            if (state == TradingWindowState.ForceFlat)
            {
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

        // ================= EMERGENCY CLOSE =================

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

        // ================= PIVOT (Classic Extended) =================

        private void UpdateDailyPivotIfNeeded(DateTime utcNow)
        {

            DateTime sessionStartUtc = GetDailyRolloverStartUtc_UTC0(utcNow);

            if (sessionStartUtc != _currentPivotSessionStartUtc)
            {
                ResetS3R3StateForNewSession(sessionStartUtc, utcNow, "DAILY_ROLLOVER");
            }

            if (sessionStartUtc == _currentPivotSessionStartUtc && _hasPivot)
                return;

            DateTime prevStartUtc = sessionStartUtc.AddDays(-1);

            bool gotHlc = TryGetSessionHlcWithFallback(prevStartUtc, sessionStartUtc, out double high, out double low, out double close, out string source);

            if (!gotHlc)
            {
                _currentPivotSessionStartUtc = sessionStartUtc;
                _hasPivot = false;

                _consecutivePivotMissing++;

                Print(
                    "PIVOT_HLC_MISSING | CodeName={0} | Symbol={1} | PrevStartUtc={2:o} | StartUtc={3:o} | MinuteBars may be insufficient",
                    CODE_NAME,
                    SymbolName,
                    DateTime.SpecifyKind(prevStartUtc, DateTimeKind.Utc),
                    DateTime.SpecifyKind(sessionStartUtc, DateTimeKind.Utc)
                );

                Print(
                    "PIVOT_MISSING_CONSEC | CodeName={0} | Count={1} | Max={2}",
                    CODE_NAME,
                    _consecutivePivotMissing,
                    Math.Max(0, MaxConsecutivePivotMissing)
                );

                int maxMiss = Math.Max(0, MaxConsecutivePivotMissing);
                if (maxMiss > 0 && _consecutivePivotMissing > maxMiss)
                {
                    Print(
                        "PIVOT_MISSING_LIMIT_REACHED | CodeName={0} | Count={1} > Max={2} | Action=NO_ENTRY",
                        CODE_NAME,
                        _consecutivePivotMissing,
                        maxMiss
                    );
                    _hasPivot = false;
                    return;
                }

                if (_hasPivotCached)
                {
                    ApplyPivotCacheAsActive("HLC_MISSING");
                    Print(
                        "PIVOT_CACHED_USED | CodeName={0} | Reason=HLC_MISSING | CachedSessionStartUtc={1:o} | MissingConsec={2}",
                        CODE_NAME,
                        DateTime.SpecifyKind(_pivotCachedSessionStartUtc, DateTimeKind.Utc),
                        _consecutivePivotMissing
                    );
                }
                else
                {
                    Print(
                        "PIVOT_WAIT_RETRY | CodeName={0} | Reason=HLC_MISSING | Action=NO_STOP",
                        CODE_NAME
                    );
                }

                return;
            }

            Print(
                "PIVOT_HLC_SOURCE | CodeName={0} | Symbol={1} | Source={2} | PrevStartUtc={3:o} | StartUtc={4:o} | High={5} Low={6} Close={7}",
                CODE_NAME,
                SymbolName,
                source,
                DateTime.SpecifyKind(prevStartUtc, DateTimeKind.Utc),
                DateTime.SpecifyKind(sessionStartUtc, DateTimeKind.Utc),
                high.ToString("F2", CultureInfo.InvariantCulture),
                low.ToString("F2", CultureInfo.InvariantCulture),
                close.ToString("F2", CultureInfo.InvariantCulture)
            );

            // --- Compute pivot ---
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

            // --- Validate pivot ---
            if (!IsPivotValid(high, low, close))
            {
                _currentPivotSessionStartUtc = sessionStartUtc;
                _hasPivot = false;

                _consecutivePivotMissing++;

                Print(
                    "PIVOT_INVALID | CodeName={0} | Symbol={1} | PrevStartUtc={2:o} | StartUtc={3:o} | High={4} Low={5} Close={6} | PP={7} S1={8} R1={9} | Range={10}",
                    CODE_NAME,
                    SymbolName,
                    DateTime.SpecifyKind(prevStartUtc, DateTimeKind.Utc),
                    DateTime.SpecifyKind(sessionStartUtc, DateTimeKind.Utc),
                    high.ToString("F2", CultureInfo.InvariantCulture),
                    low.ToString("F2", CultureInfo.InvariantCulture),
                    close.ToString("F2", CultureInfo.InvariantCulture),
                    _pp.ToString("F2", CultureInfo.InvariantCulture),
                    _s1.ToString("F2", CultureInfo.InvariantCulture),
                    _r1.ToString("F2", CultureInfo.InvariantCulture),
                    range.ToString("F2", CultureInfo.InvariantCulture)
                );

                Print(
                    "PIVOT_MISSING_CONSEC | CodeName={0} | Count={1} | Max={2}",
                    CODE_NAME,
                    _consecutivePivotMissing,
                    Math.Max(0, MaxConsecutivePivotMissing)
                );

                int maxMiss = Math.Max(0, MaxConsecutivePivotMissing);
                if (maxMiss > 0 && _consecutivePivotMissing > maxMiss)
                {
                    Print(
                        "PIVOT_MISSING_LIMIT_REACHED | CodeName={0} | Count={1} > Max={2} | Action=NO_ENTRY",
                        CODE_NAME,
                        _consecutivePivotMissing,
                        maxMiss
                    );
                    _hasPivot = false;
                    return;
                }

                if (_hasPivotCached)
                {
                    ApplyPivotCacheAsActive("INVALID_RELATION");
                    Print(
                        "PIVOT_CACHED_USED | CodeName={0} | Reason=INVALID_RELATION | CachedSessionStartUtc={1:o} | MissingConsec={2}",
                        CODE_NAME,
                        DateTime.SpecifyKind(_pivotCachedSessionStartUtc, DateTimeKind.Utc),
                        _consecutivePivotMissing
                    );
                }
                else
                {
                    Print(
                        "PIVOT_WAIT_RETRY | CodeName={0} | Reason=INVALID_RELATION | Action=NO_STOP",
                        CODE_NAME
                    );
                }

                return;
            }

            _consecutivePivotMissing = 0;

            _currentPivotSessionStartUtc = sessionStartUtc;
            _hasPivot = true;

            // Cache pivot
            CacheCurrentPivot(sessionStartUtc);

            ResetTouch("PIVOT_UPDATED");
            ResetPendingReapproach("PIVOT_UPDATED");

            Print(
                "PIVOT_UPDATED | CodeName={0} | Symbol={1} | PrevStartUtc={2:o} | StartUtc={3:o} | High={4} Low={5} Close={6} | PP={7} S1={8} R1={9} | Range={10}",
                CODE_NAME,
                SymbolName,
                DateTime.SpecifyKind(prevStartUtc, DateTimeKind.Utc),
                DateTime.SpecifyKind(sessionStartUtc, DateTimeKind.Utc),
                high.ToString("F2", CultureInfo.InvariantCulture),
                low.ToString("F2", CultureInfo.InvariantCulture),
                close.ToString("F2", CultureInfo.InvariantCulture),
                _pp.ToString("F2", CultureInfo.InvariantCulture),
                _s1.ToString("F2", CultureInfo.InvariantCulture),
                _r1.ToString("F2", CultureInfo.InvariantCulture),
                range.ToString("F2", CultureInfo.InvariantCulture)
            );

            if (EnablePivotHitLog)
                PrintPivotSnapshot(sessionStartUtc, prevStartUtc, source, high, low, close, range);

        }

        private void PrintPivotSnapshot(DateTime sessionStartUtc, DateTime prevStartUtc, string source, double high, double low, double close, double range)
        {
            Print(
                "PIVOT_SNAPSHOT | CodeName={0} | Symbol={1} | Source={2} | PrevStartUtc={3:o} | StartUtc={4:o} | High={5} Low={6} Close={7} | PP={8} S1={9} S2={10} S3={11} S4={12} | R1={13} R2={14} R3={15} R4={16} | Range={17}",
                CODE_NAME,
                SymbolName,
                string.IsNullOrWhiteSpace(source) ? "NA" : source,
                DateTime.SpecifyKind(prevStartUtc, DateTimeKind.Utc),
                DateTime.SpecifyKind(sessionStartUtc, DateTimeKind.Utc),
                high.ToString("F2", CultureInfo.InvariantCulture),
                low.ToString("F2", CultureInfo.InvariantCulture),
                close.ToString("F2", CultureInfo.InvariantCulture),
                _pp.ToString("F2", CultureInfo.InvariantCulture),
                _s1.ToString("F2", CultureInfo.InvariantCulture),
                _s2.ToString("F2", CultureInfo.InvariantCulture),
                _s3.ToString("F2", CultureInfo.InvariantCulture),
                _s4.ToString("F2", CultureInfo.InvariantCulture),
                _r1.ToString("F2", CultureInfo.InvariantCulture),
                _r2.ToString("F2", CultureInfo.InvariantCulture),
                _r3.ToString("F2", CultureInfo.InvariantCulture),
                _r4.ToString("F2", CultureInfo.InvariantCulture),
                range.ToString("F2", CultureInfo.InvariantCulture)
            );
        }

        private void CacheCurrentPivot(DateTime sessionStartUtc)
        {
            _hasPivotCached = true;
            _pivotCachedSessionStartUtc = sessionStartUtc;

            _ppCached = _pp;
            _r1Cached = _r1;
            _r2Cached = _r2;
            _r3Cached = _r3;
            _r4Cached = _r4;
            _s1Cached = _s1;
            _s2Cached = _s2;
            _s3Cached = _s3;
            _s4Cached = _s4;
        }

        private void ApplyPivotCacheAsActive(string reason)
        {
            _pp = _ppCached;
            _r1 = _r1Cached;
            _r2 = _r2Cached;
            _r3 = _r3Cached;
            _r4 = _r4Cached;
            _s1 = _s1Cached;
            _s2 = _s2Cached;
            _s3 = _s3Cached;
            _s4 = _s4Cached;

            _hasPivot = true;

            Print(
                "PIVOT_CACHED_APPLIED | CodeName={0} | Reason={1} | CachedSessionStartUtc={2:o}",
                CODE_NAME,
                string.IsNullOrWhiteSpace(reason) ? "NA" : reason,
                DateTime.SpecifyKind(_pivotCachedSessionStartUtc, DateTimeKind.Utc)
            );
        }


        // ================= TOUCH RESULT (017_014) =================

        private TouchOutcome EvaluateTouchOutcome(double level, bool isSupport, int lookbackBars)
        {
            var o = new TouchOutcome
            {
                Touched = false,
                Result = "None",
                TouchBarTimeUtc = DateTime.MinValue
            };

            try
            {
                int count = Bars == null ? 0 : Bars.Count;
                int lastClosed = count - 2;
                if (lastClosed < 1)
                    return o;

                int n = Math.Max(2, lookbackBars);
                double tol = Math.Max(0.0, PivotBufferPips) * Symbol.PipSize;

                int touchIdx = -1;
                for (int i = 0; i < n; i++)
                {
                    int idx = lastClosed - i;
                    if (idx < 0) break;

                    double low = Bars.LowPrices[idx];
                    double high = Bars.HighPrices[idx];

                    if (isSupport)
                    {
                        if (low <= level + tol)
                        {
                            touchIdx = idx;
                            break;
                        }
                    }
                    else
                    {
                        if (high >= level - tol)
                        {
                            touchIdx = idx;
                            break;
                        }
                    }
                }

                if (touchIdx < 0)
                    return o;

                o.Touched = true;
                DateTime t = Bars.OpenTimes[touchIdx];
                o.TouchBarTimeUtc = DateTime.SpecifyKind(t, DateTimeKind.Utc);

                if (touchIdx == lastClosed)
                {
                    o.Result = "None";
                    return o;
                }

                int afterIdx = touchIdx + 1;
                double afterClose = Bars.ClosePrices[afterIdx];

                if (isSupport)
                    o.Result = afterClose > level ? "Bounce" : "Break";
                else
                    o.Result = afterClose < level ? "Bounce" : "Break";

                return o;
            }
            catch
            {
                return o;
            }
        }

        private bool TryGetSessionHlcWithFallback(DateTime startUtc, DateTime endUtc, out double high, out double low, out double close, out string source)
        {
            high = double.MinValue;
            low = double.MaxValue;
            close = 0.0;
            source = "NA";

            if (TryGetSessionHlc(TimeFrame.Minute, startUtc, endUtc, out high, out low, out close))
            {
                source = "M1";
                return true;
            }

            if (TryGetSessionHlc(TimeFrame.Minute5, startUtc, endUtc, out high, out low, out close))
            {
                source = "M5";
                Print(
                    "PIVOT_FALLBACK_USED | CodeName={0} | Symbol={1} | Fallback=M5 | PrevStartUtc={2:o} | StartUtc={3:o}",
                    CODE_NAME,
                    SymbolName,
                    DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
                    DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)
                );
                return true;
            }

            return false;
        }

        // DAILY Pivot day boundary is fixed at UTC 00:00 (server time = UTC)
        private DateTime GetDailyRolloverStartUtc_UTC0(DateTime utcNow)
        {
            DateTime utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            return new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
        }


        private void ResetS3R3StateForNewSession(DateTime sessionStartUtc, DateTime utcNow, string reason)
        {
            _outsideMode = OutsideS3R3Mode.Inside;
            _outsideModeLastChangeUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

            _reentryMode = ReentryCounterMode.None;
            _reentryModeLastChangeUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

            Print(
                "S3R3_STATE_RESET | CodeName={0} | Symbol={1} | UtcNow={2:o} | SessionStartUtc={3:o} | Reason={4}",
                CODE_NAME,
                SymbolName,
                DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
                DateTime.SpecifyKind(sessionStartUtc, DateTimeKind.Utc),
                string.IsNullOrWhiteSpace(reason) ? "NA" : reason
            );
        }

        private void UpdateOutsideModeIfNeeded(double bid, double ask, DateTime utcNow)
        {
            if (!_hasPivot)
                return;

            double mid = (bid + ask) * 0.5;

            OutsideS3R3Mode next = OutsideS3R3Mode.Inside;
            if (mid < _s3)
                next = OutsideS3R3Mode.BelowS3;
            else if (mid > _r3)
                next = OutsideS3R3Mode.AboveR3;

            if (next == _outsideMode)
                return;

            OutsideS3R3Mode prev = _outsideMode;
            _outsideMode = next;
            _outsideModeLastChangeUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

            // Outside -> Inside triggers re-entry countertrend allowance
            if (prev == OutsideS3R3Mode.BelowS3 && next == OutsideS3R3Mode.Inside)
            {
                _reentryMode = ReentryCounterMode.AllowLongOnly;
                _reentryModeLastChangeUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

                Print(
                    "REENTRY_INSIDE_FROM_S3 | CodeName={0} | Symbol={1} | UtcNow={2:o} | Mode=AllowLongOnly | Mid={3} | S3={4} | R3={5}",
                    CODE_NAME,
                    SymbolName,
                    DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
                    mid.ToString("F2", CultureInfo.InvariantCulture),
                    _s3.ToString("F2", CultureInfo.InvariantCulture),
                    _r3.ToString("F2", CultureInfo.InvariantCulture)
                );
            }
            else if (prev == OutsideS3R3Mode.AboveR3 && next == OutsideS3R3Mode.Inside)
            {
                _reentryMode = ReentryCounterMode.AllowShortOnly;
                _reentryModeLastChangeUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

                Print(
                    "REENTRY_INSIDE_FROM_R3 | CodeName={0} | Symbol={1} | UtcNow={2:o} | Mode=AllowShortOnly | Mid={3} | S3={4} | R3={5}",
                    CODE_NAME,
                    SymbolName,
                    DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
                    mid.ToString("F2", CultureInfo.InvariantCulture),
                    _s3.ToString("F2", CultureInfo.InvariantCulture),
                    _r3.ToString("F2", CultureInfo.InvariantCulture)
                );
            }

            // Inside -> Outside (or Outside -> Outside) clears re-entry allowance
            if (next != OutsideS3R3Mode.Inside)
            {
                if (_reentryMode != ReentryCounterMode.None)
                {
                    _reentryMode = ReentryCounterMode.None;
                    _reentryModeLastChangeUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
                }
            }

            Print(
                "OUTSIDE_MODE_CHANGED | CodeName={0} | Symbol={1} | UtcNow={2:o} | Prev={3} | Next={4} | Mid={5} | S3={6} | R3={7}",
                CODE_NAME,
                SymbolName,
                DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
                prev.ToString(),
                next.ToString(),
                mid.ToString("F2", CultureInfo.InvariantCulture),
                _s3.ToString("F2", CultureInfo.InvariantCulture),
                _r3.ToString("F2", CultureInfo.InvariantCulture)
            );
        }

        private bool OutsideModeAllowsEntry(TradeType intended)
        {
            if (_outsideMode == OutsideS3R3Mode.Inside)
                return true;

            // Rule (017_013): while outside S3/R3, prohibit ALL new entries
            return false;
        }

        private void PrintSkipOutsideMode(TradeType intended, double entryPrice, double bid, double ask)
        {
            Print(
                "SKIP_OUTSIDE_MODE | CodeName={0} | Symbol={1} | Mode={2} | Intended={3} | Entry={4} | Bid={5} | Ask={6} | S3={7} | R3={8} | ModeSinceUtc={9:o}",
                CODE_NAME,
                SymbolName,
                _outsideMode.ToString(),
                intended.ToString(),
                entryPrice.ToString("F2", CultureInfo.InvariantCulture),
                bid.ToString("F2", CultureInfo.InvariantCulture),
                ask.ToString("F2", CultureInfo.InvariantCulture),
                _s3.ToString("F2", CultureInfo.InvariantCulture),
                _r3.ToString("F2", CultureInfo.InvariantCulture),
                DateTime.SpecifyKind(_outsideModeLastChangeUtc, DateTimeKind.Utc)
            );
        }

        private bool ReentryModeAllowsEntry(TradeType intended)
        {
            if (_reentryMode == ReentryCounterMode.None)
                return true;

            if (_reentryMode == ReentryCounterMode.AllowLongOnly)
                return intended == TradeType.Buy;

            if (_reentryMode == ReentryCounterMode.AllowShortOnly)
                return intended == TradeType.Sell;

            return true;
        }

        private void PrintSkipReentryMode(TradeType intended, double entryPrice, double bid, double ask)
        {
            Print(
                "SKIP_REENTRY_COUNTER_ONLY | CodeName={0} | Symbol={1} | Mode={2} | Intended={3} | Entry={4} | Bid={5} | Ask={6} | S3={7} | R3={8} | ModeSinceUtc={9:o}",
                CODE_NAME,
                SymbolName,
                _reentryMode.ToString(),
                intended.ToString(),
                entryPrice.ToString("F2", CultureInfo.InvariantCulture),
                bid.ToString("F2", CultureInfo.InvariantCulture),
                ask.ToString("F2", CultureInfo.InvariantCulture),
                _s3.ToString("F2", CultureInfo.InvariantCulture),
                _r3.ToString("F2", CultureInfo.InvariantCulture),
                DateTime.SpecifyKind(_reentryModeLastChangeUtc, DateTimeKind.Utc)
            );
        }

        private string GetPivotBandLabel(double price)
        {
            if (price <= _s3) return "BELOW_S3";
            if (price < _s2) return "S3_TO_S2";
            if (price < _s1) return "S2_TO_S1";
            if (price < _pp) return "S1_TO_PP";
            if (price < _r1) return "PP_TO_R1";
            if (price < _r2) return "R1_TO_R2";
            if (price < _r3) return "R2_TO_R3";
            return "ABOVE_R3";
        }

        private string GetNearestS2S3R2R3(double price, out double distToS2Price, out double distToS3Price, out double distToR2Price, out double distToR3Price)
        {
            distToS2Price = Math.Abs(price - _s2);
            distToS3Price = Math.Abs(price - _s3);
            distToR2Price = Math.Abs(price - _r2);
            distToR3Price = Math.Abs(price - _r3);

            double best = distToS2Price;
            string name = "S2";

            if (distToS3Price < best) { best = distToS3Price; name = "S3"; }
            if (distToR2Price < best) { best = distToR2Price; name = "R2"; }
            if (distToR3Price < best) { best = distToR3Price; name = "R3"; }

            return name;
        }


        private bool TryGetSessionHlc(TimeFrame tf, DateTime startUtc, DateTime endUtc, out double high, out double low, out double close)
        {
            high = double.MinValue;
            low = double.MaxValue;
            close = 0.0;

            Bars bars = MarketData.GetBars(tf);
            if (bars == null || bars.Count < 10)
                return false;

            bool hasAny = false;

            for (int i = 0; i < bars.Count; i++)
            {
                DateTime t = bars.OpenTimes[i];
                if (t < startUtc) continue;
                if (t >= endUtc) break;

                double h = bars.HighPrices[i];
                double l = bars.LowPrices[i];

                if (!hasAny)
                {
                    hasAny = true;
                    high = h;
                    low = l;
                    close = bars.ClosePrices[i];
                }
                else
                {
                    if (h > high) high = h;
                    if (l < low) low = l;
                    close = bars.ClosePrices[i];
                }
            }

            return hasAny;
        }

        private bool IsPivotValid(double high, double low, double close)
        {
            if (double.IsNaN(high) || double.IsNaN(low) || double.IsNaN(close)) return false;
            if (double.IsNaN(_pp) || double.IsNaN(_s1) || double.IsNaN(_r1)) return false;

            if (high <= low) return false;

            if (!(_s1 < _pp && _pp < _r1)) return false;

            double range = high - low;
            if (range <= 0.0) return false;

            return true;
        }

        // ================= LATEST TOUCH =================

        private void ResetTouch(string reason)
        {
            _lastTouchedLine = PivotLine.None;
            _lastTouchedLinePrice = 0.0;
            _lastTouchedSignalIndex = -1;
            _lastTouchedTimeUtc = DateTime.MinValue;

            Print(
                "TOUCH_RESET | CodeName={0} | Symbol={1} | Reason={2}",
                CODE_NAME,
                SymbolName,
                string.IsNullOrWhiteSpace(reason) ? "NA" : reason
            );
        }

        private void ExpireTouchIfNeeded()
        {
            if (_lastTouchedLine == PivotLine.None)
                return;

            int expireBars = Math.Max(1, TouchExpireBars);
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0 || _lastTouchedSignalIndex < 0)
                return;

            int barsAgo = signalIndex - _lastTouchedSignalIndex;
            if (barsAgo >= expireBars)
                ResetTouch("TOUCH_EXPIRED");
        }

        private void UpdateLatestTouchIfNeeded()
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0)
                return;

            double bufferPrice = (PivotBufferPips * 10.0) * Symbol.PipSize;

            double high = Bars.HighPrices[signalIndex];
            double low = Bars.LowPrices[signalIndex];
            double close = Bars.ClosePrices[signalIndex];

            double maxRangePrice = (Math.Max(0.0, MaxTouchCandleRangePips) * 10.0) * Symbol.PipSize;
            if (maxRangePrice > 0.0)
            {
                double range = high - low;
                if (range > maxRangePrice)
                {
                    Print(
                        "SKIP_TOUCH_TOO_VOLATILE | CodeName={0} | Symbol={1} | SignalIndex={2} | Range={3} > MaxRange={4} | High={5} Low={6} Close={7} | Buffer={8}",
                        CODE_NAME,
                        SymbolName,
                        signalIndex,
                        range.ToString("F2", CultureInfo.InvariantCulture),
                        maxRangePrice.ToString("F2", CultureInfo.InvariantCulture),
                        high.ToString("F2", CultureInfo.InvariantCulture),
                        low.ToString("F2", CultureInfo.InvariantCulture),
                        close.ToString("F2", CultureInfo.InvariantCulture),
                        bufferPrice.ToString("G17", CultureInfo.InvariantCulture)
                    );
                    return;
                }
            }

            PivotLine touched = ResolveTouchedLineByClose(signalIndex, bufferPrice);
            if (touched == PivotLine.None)
                return;

            double linePrice = GetLinePrice(touched);

            _lastTouchedLine = touched;
            _lastTouchedLinePrice = linePrice;
            _lastTouchedSignalIndex = signalIndex;
            _lastTouchedTimeUtc = DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);


            if (EnablePivotHitLog)
            {
                double distPips = Math.Abs(close - linePrice) / Symbol.PipSize;

                Print(
                    "HIT_LEVEL | CodeName={0} | Symbol={1} | UTC={2:yyyy-MM-dd HH:mm:ss} | Level={3} | LinePrice={4} | Close={5} | DistPips={6:F2} | BufferPips={7:F2}",
                    CODE_NAME,
                    SymbolName,
                    _lastTouchedTimeUtc,
                    touched,
                    linePrice.ToString("F2", CultureInfo.InvariantCulture),
                    close.ToString("F2", CultureInfo.InvariantCulture),
                    distPips,
                    PivotBufferPips
                );
            }

            // If a different line becomes latest, pending should be invalidated
            if (_pendingReapproachActive && _pendingLine != touched)
                ResetPendingReapproach("LINE_CHANGED_BY_TOUCH_UPDATE");

            Print(
                "TOUCH_UPDATED | CodeName={0} | Symbol={1} | SignalIndex={2} | Line={3} | LinePrice={4} | High={5} Low={6} Close={7} | Buffer={8}",
                CODE_NAME,
                SymbolName,
                signalIndex,
                touched.ToString(),
                linePrice.ToString("F2", CultureInfo.InvariantCulture),
                high.ToString("F2", CultureInfo.InvariantCulture),
                low.ToString("F2", CultureInfo.InvariantCulture),
                close.ToString("F2", CultureInfo.InvariantCulture),
                bufferPrice.ToString("G17", CultureInfo.InvariantCulture)
            );
        }

        private PivotLine ResolveTouchedLineByClose(int signalIndex, double bufferPrice)
        {
            double close = Bars.ClosePrices[signalIndex];

            List<PivotLine> touchedSupports = new List<PivotLine>();
            List<PivotLine> touchedRes = new List<PivotLine>();

            // PP is treated as both support & resistance candidate, but touch is only true when candle range crosses proximity band
            AddSupportTouchIf(signalIndex, bufferPrice, PivotLine.PP, ref touchedSupports);
            AddResistanceTouchIf(signalIndex, bufferPrice, PivotLine.PP, ref touchedRes);
                AddSupportTouchIf(signalIndex, bufferPrice, PivotLine.S1, ref touchedSupports);
                AddSupportTouchIf(signalIndex, bufferPrice, PivotLine.S2, ref touchedSupports);
                AddSupportTouchIf(signalIndex, bufferPrice, PivotLine.S3, ref touchedSupports);
                AddSupportTouchIf(signalIndex, bufferPrice, PivotLine.S4, ref touchedSupports);

                AddResistanceTouchIf(signalIndex, bufferPrice, PivotLine.R1, ref touchedRes);
                AddResistanceTouchIf(signalIndex, bufferPrice, PivotLine.R2, ref touchedRes);
                AddResistanceTouchIf(signalIndex, bufferPrice, PivotLine.R3, ref touchedRes);
                AddResistanceTouchIf(signalIndex, bufferPrice, PivotLine.R4, ref touchedRes);

            PivotLine supportCandidate = PivotLine.None;
            PivotLine resistanceCandidate = PivotLine.None;

            if (touchedSupports.Count > 0)
                supportCandidate = ChooseSupportByClose(touchedSupports, close);

            if (touchedRes.Count > 0)
                resistanceCandidate = ChooseResistanceByClose(touchedRes, close);

            if (supportCandidate == PivotLine.None && resistanceCandidate == PivotLine.None)
                return PivotLine.None;

            if (supportCandidate != PivotLine.None && resistanceCandidate == PivotLine.None)
                return supportCandidate;

            if (supportCandidate == PivotLine.None && resistanceCandidate != PivotLine.None)
                return resistanceCandidate;

            double sPrice = GetLinePrice(supportCandidate);
            double rPrice = GetLinePrice(resistanceCandidate);

            double ds = Math.Abs(close - sPrice);
            double dr = Math.Abs(close - rPrice);

            return ds <= dr ? supportCandidate : resistanceCandidate;
        }

        // Candle range [Low, High] must cross band [line-buffer, line+buffer]
        private void AddSupportTouchIf(int signalIndex, double bufferPrice, PivotLine line, ref List<PivotLine> list)
        {
            double linePrice = GetLinePrice(line);
            if (linePrice <= 0.0) return;

            double low = Bars.LowPrices[signalIndex];
            double high = Bars.HighPrices[signalIndex];

            if (low <= (linePrice + bufferPrice) && high >= (linePrice - bufferPrice))
                list.Add(line);
        }

        private void AddResistanceTouchIf(int signalIndex, double bufferPrice, PivotLine line, ref List<PivotLine> list)
        {
            double linePrice = GetLinePrice(line);
            if (linePrice <= 0.0) return;

            double low = Bars.LowPrices[signalIndex];
            double high = Bars.HighPrices[signalIndex];

            if (low <= (linePrice + bufferPrice) && high >= (linePrice - bufferPrice))
                list.Add(line);
        }

        private PivotLine ChooseSupportByClose(List<PivotLine> supportsTouched, double close)
        {
            PivotLine best = PivotLine.None;
            double bestPrice = double.MinValue;

            for (int i = 0; i < supportsTouched.Count; i++)
            {
                PivotLine l = supportsTouched[i];
                double p = GetLinePrice(l);

                if (close >= p)
                {
                    if (p > bestPrice)
                    {
                        bestPrice = p;
                        best = l;
                    }
                }
            }

            if (best != PivotLine.None)
                return best;

            PivotLine deepest = PivotLine.None;
            double minPrice = double.MaxValue;

            for (int i = 0; i < supportsTouched.Count; i++)
            {
                PivotLine l = supportsTouched[i];
                double p = GetLinePrice(l);
                if (p < minPrice)
                {
                    minPrice = p;
                    deepest = l;
                }
            }
            return deepest;
        }

        private PivotLine ChooseResistanceByClose(List<PivotLine> resistTouched, double close)
        {
            PivotLine best = PivotLine.None;
            double bestPrice = double.MaxValue;

            for (int i = 0; i < resistTouched.Count; i++)
            {
                PivotLine l = resistTouched[i];
                double p = GetLinePrice(l);

                if (close <= p)
                {
                    if (p < bestPrice)
                    {
                        bestPrice = p;
                        best = l;
                    }
                }
            }

            if (best != PivotLine.None)
                return best;

            PivotLine highest = PivotLine.None;
            double maxPrice = double.MinValue;

            for (int i = 0; i < resistTouched.Count; i++)
            {
                PivotLine l = resistTouched[i];
                double p = GetLinePrice(l);
                if (p > maxPrice)
                {
                    maxPrice = p;
                    highest = l;
                }
            }
            return highest;
        }

        private double GetLinePrice(PivotLine line)
        {
            switch (line)
            {
                case PivotLine.PP: return _pp;
                case PivotLine.S1: return _s1;
                case PivotLine.R1: return _r1;
                case PivotLine.S2: return _s2;
                case PivotLine.R2: return _r2;
                case PivotLine.S3: return _s3;
                case PivotLine.R3: return _r3;
                case PivotLine.S4: return _s4;
                case PivotLine.R4: return _r4;
                default: return 0.0;
            }
        }

        private bool IsSupportLine(PivotLine line)
        {
            return line == PivotLine.PP || line == PivotLine.S1 || line == PivotLine.S2 || line == PivotLine.S3 || line == PivotLine.S4;
        }

        private bool IsResistanceLine(PivotLine line)
        {
            return line == PivotLine.PP || line == PivotLine.R1 || line == PivotLine.R2 || line == PivotLine.R3 || line == PivotLine.R4;
        }

        // ================= ENTRY (LATEST TOUCH ONLY) =================

        private void TryPivotBounceEntry_LatestTouchOnly()
        {
            if (_lastTouchedLine == PivotLine.None)
                return;

            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0)
                return;

            const int WAIT_BARS_AFTER_TOUCH = 3;

            int barsSinceTouch = signalIndex - _lastTouchedSignalIndex;
            if (barsSinceTouch < WAIT_BARS_AFTER_TOUCH)
                return;

            double bufferPrice = (PivotBufferPips * 10.0) * Symbol.PipSize;
            double touchLineSupport = _lastTouchedLinePrice + bufferPrice;
            double touchLineRes = _lastTouchedLinePrice - bufferPrice;

            double close = Bars.ClosePrices[signalIndex];

            if (!SymbolInfoTick(out double bid, out double ask))
                return;

            if (IsSupportLine(_lastTouchedLine))
            {
                if (close <= touchLineSupport)
                    return;

                if (!TryResolveTpTarget(_lastTouchedLine, TradeType.Buy, out double tpTarget, out string tpLineName))
                    return;

                if (!DirectionAllowsEntry(_lastTouchedLine.ToString(), _lastTouchedLinePrice, TradeType.Buy))
                {
                    PrintSkipDirection(_lastTouchedLine.ToString(), _lastTouchedLinePrice, TradeType.Buy, ask, bid);
                    return;
                }

                double rr;
                if (!PassesMinRrOrLog(_lastTouchedLine, TradeType.Buy, ask, _lastTouchedLinePrice - bufferPrice, tpTarget, _lastTouchedSignalIndex, out rr))
                    return;

                if (!IsEntryDistanceOkOrSetPending(
                        TradeType.Buy,
                        "Buy",
                        _lastTouchedLine,
                        _lastTouchedLinePrice,
                        ask,
                        _lastTouchedLinePrice - bufferPrice,
                        tpTarget,
                        "PIVOT_BOUNCE_" + _lastTouchedLine.ToString() + "_TP_" + tpLineName))
                {
                    return;
                }

                Print(
                    "TP_TARGET | CodeName={0} | Symbol={1} | Line={2} | Intended=Buy | TpLine={3} | TpTarget={4} | EntryPrice={5} | RR={6} | MinRR={7}",
                    CODE_NAME,
                    SymbolName,
                    _lastTouchedLine.ToString(),
                    tpLineName,
                    tpTarget.ToString("F2", CultureInfo.InvariantCulture),
                    ask.ToString("F2", CultureInfo.InvariantCulture),
                    rr.ToString("F3", CultureInfo.InvariantCulture),
                    Math.Max(0.0, MinRRRatio).ToString("F3", CultureInfo.InvariantCulture)
                );

                if (!OutsideModeAllowsEntry(TradeType.Buy))
                {
                    PrintSkipOutsideMode(TradeType.Buy, ask, bid, ask);
                    return;
                }

                if (!ReentryModeAllowsEntry(TradeType.Buy))
                {
                    PrintSkipReentryMode(TradeType.Buy, ask, bid, ask);
                    return;
                }

                PlaceTrade(TradeType.Buy, ask, _lastTouchedLinePrice - bufferPrice, tpTarget, "PIVOT_BOUNCE_" + _lastTouchedLine.ToString() + "_TP_" + tpLineName);
                ResetPendingReapproach("ENTRY_EXECUTED");
                return;
            }

            if (IsResistanceLine(_lastTouchedLine))
            {
                if (close >= touchLineRes)
                    return;

                if (!TryResolveTpTarget(_lastTouchedLine, TradeType.Sell, out double tpTarget, out string tpLineName))
                    return;

                if (!DirectionAllowsEntry(_lastTouchedLine.ToString(), _lastTouchedLinePrice, TradeType.Sell))
                {
                    PrintSkipDirection(_lastTouchedLine.ToString(), _lastTouchedLinePrice, TradeType.Sell, ask, bid);
                    return;
                }

                double rr;
                if (!PassesMinRrOrLog(_lastTouchedLine, TradeType.Sell, bid, _lastTouchedLinePrice + bufferPrice, tpTarget, _lastTouchedSignalIndex, out rr))
                    return;

                if (!IsEntryDistanceOkOrSetPending(
                        TradeType.Sell,
                        "Sell",
                        _lastTouchedLine,
                        _lastTouchedLinePrice,
                        bid,
                        _lastTouchedLinePrice + bufferPrice,
                        tpTarget,
                        "PIVOT_BOUNCE_" + _lastTouchedLine.ToString() + "_TP_" + tpLineName))
                {
                    return;
                }

                Print(
                    "TP_TARGET | CodeName={0} | Symbol={1} | Line={2} | Intended=Sell | TpLine={3} | TpTarget={4} | EntryPrice={5} | RR={6} | MinRR={7}",
                    CODE_NAME,
                    SymbolName,
                    _lastTouchedLine.ToString(),
                    tpLineName,
                    tpTarget.ToString("F2", CultureInfo.InvariantCulture),
                    bid.ToString("F2", CultureInfo.InvariantCulture),
                    rr.ToString("F3", CultureInfo.InvariantCulture),
                    Math.Max(0.0, MinRRRatio).ToString("F3", CultureInfo.InvariantCulture)
                );

                if (!OutsideModeAllowsEntry(TradeType.Sell))
                {
                    PrintSkipOutsideMode(TradeType.Sell, bid, bid, ask);
                    return;
                }

                if (!ReentryModeAllowsEntry(TradeType.Sell))
                {
                    PrintSkipReentryMode(TradeType.Sell, bid, bid, ask);
                    return;
                }

                PlaceTrade(TradeType.Sell, bid, _lastTouchedLinePrice + bufferPrice, tpTarget, "PIVOT_BOUNCE_" + _lastTouchedLine.ToString() + "_TP_" + tpLineName);
                ResetPendingReapproach("ENTRY_EXECUTED");
                return;
            }
        }

        private bool PassesMinRrOrLog(PivotLine line, TradeType type, double entryPrice, double stopPrice, double tpTargetPrice, int originSignalIndex, out double rr)
        {
            rr = 0.0;

            double normalMinRr = Math.Max(0.0, MinRRRatio);
            if (normalMinRr <= 0.0)
                return true;

            int currentSignalIndex = Bars.Count - 2;

            int barsSinceOrigin = int.MaxValue;
            if (originSignalIndex >= 0 && currentSignalIndex >= 0)
                barsSinceOrigin = currentSignalIndex - originSignalIndex;

            bool relaxActive = false;
            double effectiveMinRr = normalMinRr;

            if (EnableMinRrRelax)
            {
                int windowBars = Math.Max(0, MinRrRelaxWindowBars);
                if (barsSinceOrigin >= 0 && barsSinceOrigin <= windowBars)
                {
                    relaxActive = true;
                    effectiveMinRr = Math.Max(0.0, MinRrRelaxedRatio);
                }
            }

            double slDist = Math.Abs(entryPrice - stopPrice);
            double tpDist = Math.Abs(tpTargetPrice - entryPrice);

            if (slDist <= 0.0 || tpDist <= 0.0)
                return false;

            rr = tpDist / slDist;

            if (rr + 1e-12 < effectiveMinRr)
            {
                Print(
                    "SKIP_MIN_RR | CodeName={0} | Symbol={1} | Line={2} | TradeType={3} | Entry={4} | Stop={5} | TpTarget={6} | RR={7} | MinRR={8} | EffectiveMinRR={9} | Mode={10} | OriginIndex={11} | NowIndex={12} | BarsSinceOrigin={13}",
                    CODE_NAME,
                    SymbolName,
                    line.ToString(),
                    type.ToString(),
                    entryPrice.ToString("F2", CultureInfo.InvariantCulture),
                    stopPrice.ToString("F2", CultureInfo.InvariantCulture),
                    tpTargetPrice.ToString("F2", CultureInfo.InvariantCulture),
                    rr.ToString("F3", CultureInfo.InvariantCulture),
                    normalMinRr.ToString("F3", CultureInfo.InvariantCulture),
                    effectiveMinRr.ToString("F3", CultureInfo.InvariantCulture),
                    relaxActive ? "RELAX" : "NORMAL",
                    originSignalIndex,
                    currentSignalIndex,
                    barsSinceOrigin
                );
                return false;
            }

            return true;
        }

        private void ResetPendingReapproach(string reason)
        {
            if (_pendingReapproachActive)
            {
                Print(
                    "PENDING_REAPPROACH_CLEARED | CodeName={0} | Symbol={1} | Reason={2} | PendingLine={3} | CreatedIndex={4}",
                    CODE_NAME,
                    SymbolName,
                    string.IsNullOrWhiteSpace(reason) ? "NA" : reason,
                    _pendingLine.ToString(),
                    _pendingCreatedSignalIndex
                );
            }

            _pendingReapproachActive = false;
            _pendingCreatedSignalIndex = -1;
            _pendingLine = PivotLine.None;
            _pendingLinePrice = 0.0;
            _pendingStopPrice = 0.0;
            _pendingTpTargetPrice = 0.0;
            _pendingReasonTag = "NA";
        }

        private bool IsEntryDistanceOkOrSetPending(
            TradeType type,
            string sideText,
            PivotLine line,
            double linePrice,
            double currentPrice,
            double stopPrice,
            double tpTargetPrice,
            string reasonTag)
        {
            double maxDistPrice = (Math.Max(0.0, EntryMaxDistancePips) * 10.0) * Symbol.PipSize;
            if (maxDistPrice <= 0.0)
                return true;

            double dist = Math.Abs(currentPrice - linePrice);
            if (dist <= maxDistPrice)
                return true;

            int signalIndex = Bars.Count - 2;

            _pendingReapproachActive = true;
            _pendingCreatedSignalIndex = signalIndex;
            _pendingLine = line;
            _pendingLinePrice = linePrice;
            _pendingTradeType = type;
            _pendingStopPrice = stopPrice;
            _pendingTpTargetPrice = tpTargetPrice;
            _pendingReasonTag = string.IsNullOrWhiteSpace(reasonTag) ? "NA" : reasonTag;

            Print(
                "PENDING_REAPPROACH_SET | CodeName={0} | Symbol={1} | Line={2} | Intended={3} | CurrentPrice={4} | LinePrice={5} | Dist={6} | EntryMaxDist={7} | WindowBars={8} | ReapproachMaxDist={9} | CreatedIndex={10}",
                CODE_NAME,
                SymbolName,
                line.ToString(),
                sideText,
                currentPrice.ToString("F2", CultureInfo.InvariantCulture),
                linePrice.ToString("F2", CultureInfo.InvariantCulture),
                dist.ToString("G17", CultureInfo.InvariantCulture),
                maxDistPrice.ToString("G17", CultureInfo.InvariantCulture),
                Math.Max(1, ReapproachWindowBars),
                GetReapproachMaxDistPrice().ToString("G17", CultureInfo.InvariantCulture),
                signalIndex
            );

            return false;
        }

        private double GetReapproachMaxDistPrice()
        {
            double pips = Math.Max(0.0, ReapproachMaxDistancePips);
            if (pips <= 0.0)
                pips = Math.Max(0.0, EntryMaxDistancePips);

            return (pips * 10.0) * Symbol.PipSize;
        }

        private bool ProcessPendingReapproachIfAny()
        {
            if (!_pendingReapproachActive)
                return false;

            if (Positions.Count >= MaxPositions)
                return false;

            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0)
                return false;

            int windowBars = Math.Max(1, ReapproachWindowBars);
            int barsSincePending = signalIndex - _pendingCreatedSignalIndex;

            if (barsSincePending > windowBars)
            {
                Print(
                    "PENDING_REAPPROACH_EXPIRED | CodeName={0} | Symbol={1} | PendingLine={2} | Intended={3} | BarsSince={4} > WindowBars={5} | CreatedIndex={6} | NowIndex={7}",
                    CODE_NAME,
                    SymbolName,
                    _pendingLine.ToString(),
                    _pendingTradeType.ToString(),
                    barsSincePending,
                    windowBars,
                    _pendingCreatedSignalIndex,
                    signalIndex
                );
                ResetPendingReapproach("EXPIRED");
                return false;
            }

            if (_lastTouchedLine != PivotLine.None && _lastTouchedLine != _pendingLine)
            {
                Print(
                    "PENDING_REAPPROACH_EXPIRED | CodeName={0} | Symbol={1} | PendingLine={2} | LatestLine={3} | Reason=LINE_CHANGED",
                    CODE_NAME,
                    SymbolName,
                    _pendingLine.ToString(),
                    _lastTouchedLine.ToString()
                );
                ResetPendingReapproach("LINE_CHANGED");
                return false;
            }

            if (!SymbolInfoTick(out double bid, out double ask))
                return false;

            double currentPrice = _pendingTradeType == TradeType.Buy ? ask : bid;
            double dist = Math.Abs(currentPrice - _pendingLinePrice);
            double maxDist = GetReapproachMaxDistPrice();

            if (maxDist <= 0.0)
            {
                ResetPendingReapproach("REAPPROACH_DISABLED");
                return false;
            }

            if (dist > maxDist)
                return false;

            if (!DirectionAllowsEntry(_pendingLine.ToString(), _pendingLinePrice, _pendingTradeType))
            {
                PrintSkipDirection(_pendingLine.ToString(), _pendingLinePrice, _pendingTradeType, ask, bid);
                return false;
            }

            if (_pendingTradeType == TradeType.Buy && _pendingTpTargetPrice <= currentPrice)
            {
                Print(
                    "SKIP_TP_TARGET_INVALID | CodeName={0} | Symbol={1} | TradeType=Buy | Entry={2} | TpTarget={3} | Reason=TP_NOT_FORWARD | PendingLine={4}",
                    CODE_NAME,
                    SymbolName,
                    currentPrice.ToString("F2", CultureInfo.InvariantCulture),
                    _pendingTpTargetPrice.ToString("F2", CultureInfo.InvariantCulture),
                    _pendingLine.ToString()
                );
                ResetPendingReapproach("TP_NOT_FORWARD");
                return false;
            }

            if (_pendingTradeType == TradeType.Sell && _pendingTpTargetPrice >= currentPrice)
            {
                Print(
                    "SKIP_TP_TARGET_INVALID | CodeName={0} | Symbol={1} | TradeType=Sell | Entry={2} | TpTarget={3} | Reason=TP_NOT_FORWARD | PendingLine={4}",
                    CODE_NAME,
                    SymbolName,
                    currentPrice.ToString("F2", CultureInfo.InvariantCulture),
                    _pendingTpTargetPrice.ToString("F2", CultureInfo.InvariantCulture),
                    _pendingLine.ToString()
                );
                ResetPendingReapproach("TP_NOT_FORWARD");
                return false;
            }

            double rr;
            if (!PassesMinRrOrLog(_pendingLine, _pendingTradeType, currentPrice, _pendingStopPrice, _pendingTpTargetPrice, _pendingCreatedSignalIndex, out rr))
            {
                ResetPendingReapproach("MIN_RR_BLOCK");
                return false;
            }

            Print(
                "PENDING_REAPPROACH_TRIGGER | CodeName={0} | Symbol={1} | PendingLine={2} | Intended={3} | CurrentPrice={4} | LinePrice={5} | Dist={6} <= MaxDist={7} | BarsSince={8}/{9} | RR={10} | MinRR={11}",
                CODE_NAME,
                SymbolName,
                _pendingLine.ToString(),
                _pendingTradeType.ToString(),
                currentPrice.ToString("F2", CultureInfo.InvariantCulture),
                _pendingLinePrice.ToString("F2", CultureInfo.InvariantCulture),
                dist.ToString("G17", CultureInfo.InvariantCulture),
                maxDist.ToString("G17", CultureInfo.InvariantCulture),
                barsSincePending,
                windowBars,
                rr.ToString("F3", CultureInfo.InvariantCulture),
                Math.Max(0.0, MinRRRatio).ToString("F3", CultureInfo.InvariantCulture)
            );

            PlaceTrade(_pendingTradeType, currentPrice, _pendingStopPrice, _pendingTpTargetPrice, _pendingReasonTag);

            ResetPendingReapproach("TRIGGERED");
            return true;
        }

        private bool TryResolveTpTarget(PivotLine entryLine, TradeType type, out double tpTargetPrice, out string tpLineName)
        {
            tpTargetPrice = 0.0;
            tpLineName = "NA";

            PivotLine tpLine = PivotLine.None;

            if (entryLine == PivotLine.PP)
            {
                tpLine = (type == TradeType.Buy) ? PivotLine.R1 : PivotLine.S1;
            }
            else
            {
                if (type == TradeType.Buy)
                {
                    switch (entryLine)
                    {
                        case PivotLine.S4: tpLine = PivotLine.S3; break;
                        case PivotLine.S3: tpLine = PivotLine.S2; break;
                        case PivotLine.S2: tpLine = PivotLine.S1; break;
                        case PivotLine.S1: tpLine = PivotLine.PP; break;
                        default: tpLine = PivotLine.None; break;
                    }
                }
                else
                {
                    switch (entryLine)
                    {
                        case PivotLine.R4: tpLine = PivotLine.R3; break;
                        case PivotLine.R3: tpLine = PivotLine.R2; break;
                        case PivotLine.R2: tpLine = PivotLine.R1; break;
                        case PivotLine.R1: tpLine = PivotLine.PP; break;
                        default: tpLine = PivotLine.None; break;
                    }
                }
            }

            if (tpLine == PivotLine.None)
            {
                Print(
                    "SKIP_TP_TARGET_INVALID | CodeName={0} | Symbol={1} | EntryLine={2} | TradeType={3} | Reason=NO_TARGET_LINE",
                    CODE_NAME,
                    SymbolName,
                    entryLine.ToString(),
                    type.ToString()
                );
                return false;
            }

            tpTargetPrice = GetLinePrice(tpLine);
            tpLineName = tpLine.ToString();

            if (tpTargetPrice <= 0.0)
            {
                Print(
                    "SKIP_TP_TARGET_INVALID | CodeName={0} | Symbol={1} | EntryLine={2} | TradeType={3} | TpLine={4} | Reason=TP_PRICE_INVALID",
                    CODE_NAME,
                    SymbolName,
                    entryLine.ToString(),
                    type.ToString(),
                    tpLineName
                );
                return false;
            }

            return true;
        }

        // ================= DIRECTION FILTER =================

        private bool DirectionAllowsEntry(string lineName, double linePrice, TradeType intended)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0)
                return false;

            double close = Bars.ClosePrices[signalIndex];

            double epsEnterPrice = (Math.Max(0.0, DirectionDeadzonePips) * 10.0) * Symbol.PipSize;

            double ratio = Math.Max(0.0, DirectionHysteresisExitEnterRatio);
            double epsExitPrice = epsEnterPrice * ratio;

            if (epsEnterPrice <= 0.0)
            {
                if (intended == TradeType.Buy)
                    return close >= linePrice;
                return close <= linePrice;
            }

            int minHoldBars = Math.Max(0, DirectionStateMinHoldBars);

            if (lineName == "PP")
            {
                if (!EnablePpDirectionFilter)
                    return true;

                int ppMinHoldBars = Math.Max(0, PpDirectionStateMinHoldBars);

                LineSideState prev = _statePP;
                LineSideState next = UpdateLineSideState(_statePP, close, linePrice, epsEnterPrice, epsExitPrice);

                if (next != prev)
                    _lastStateChangeIndexPP = signalIndex;

                _statePP = next;

                if (ppMinHoldBars > 0 && _lastStateChangeIndexPP >= 0)
                {
                    int barsSince = signalIndex - _lastStateChangeIndexPP;
                    if (barsSince < ppMinHoldBars)
                        return false;
                }

                return intended == TradeType.Buy ? (_statePP == LineSideState.Above) : (_statePP == LineSideState.Below);
            }

            if (lineName == "S1")
            {
                LineSideState prev = _stateS1;
                LineSideState next = UpdateLineSideState(_stateS1, close, linePrice, epsEnterPrice, epsExitPrice);

                if (next != prev)
                    _lastStateChangeIndexS1 = signalIndex;

                _stateS1 = next;

                if (minHoldBars > 0 && _lastStateChangeIndexS1 >= 0)
                {
                    int barsSince = signalIndex - _lastStateChangeIndexS1;
                    if (barsSince < minHoldBars)
                        return false;
                }

                return intended == TradeType.Buy ? (_stateS1 == LineSideState.Above) : (_stateS1 == LineSideState.Below);
            }

            if (lineName == "R1")
            {
                LineSideState prev = _stateR1;
                LineSideState next = UpdateLineSideState(_stateR1, close, linePrice, epsEnterPrice, epsExitPrice);

                if (next != prev)
                    _lastStateChangeIndexR1 = signalIndex;

                _stateR1 = next;

                if (minHoldBars > 0 && _lastStateChangeIndexR1 >= 0)
                {
                    int barsSince = signalIndex - _lastStateChangeIndexR1;
                    if (barsSince < minHoldBars)
                        return false;
                }

                return intended == TradeType.Buy ? (_stateR1 == LineSideState.Above) : (_stateR1 == LineSideState.Below);
            }

            LineSideState tmp = LineSideState.Neutral;
            tmp = UpdateLineSideState(tmp, close, linePrice, epsEnterPrice, epsExitPrice);
            return intended == TradeType.Buy ? (tmp == LineSideState.Above) : (tmp == LineSideState.Below);
        }

        private LineSideState UpdateLineSideState(LineSideState current, double close, double linePrice, double epsEnterPrice, double epsExitPrice)
        {
            double upperEnter = linePrice + epsEnterPrice;
            double lowerEnter = linePrice - epsEnterPrice;

            double upperExit = linePrice + epsExitPrice;
            double lowerExit = linePrice - epsExitPrice;

            if (current == LineSideState.Neutral)
            {
                if (close >= upperEnter) return LineSideState.Above;
                if (close <= lowerEnter) return LineSideState.Below;
                return LineSideState.Neutral;
            }

            if (current == LineSideState.Above)
            {
                if (close <= lowerExit)
                {
                    if (close <= lowerEnter) return LineSideState.Below;
                    return LineSideState.Neutral;
                }
                return LineSideState.Above;
            }

            if (close >= upperExit)
            {
                if (close >= upperEnter) return LineSideState.Above;
                return LineSideState.Neutral;
            }
            return LineSideState.Below;
        }

        private void PrintSkipDirection(string lineName, double linePrice, TradeType intended, double ask, double bid)
        {
            int signalIndex = Bars.Count - 2;
            if (signalIndex < 0)
                return;

            double close = Bars.ClosePrices[signalIndex];

            double epsEnterPrice = (Math.Max(0.0, DirectionDeadzonePips) * 10.0) * Symbol.PipSize;
            double ratio = Math.Max(0.0, DirectionHysteresisExitEnterRatio);
            double epsExitPrice = epsEnterPrice * ratio;

            string stateText = "NA";
            if (lineName == "S1") stateText = _stateS1.ToString();
            else if (lineName == "R1") stateText = _stateR1.ToString();

            Print(
                "SKIP_DIRECTION | CodeName={0} | Symbol={1} | Line={2} | Intended={3} | Close={4} | LinePrice={5} | EpsEnterPrice={6} | EpsExitPrice={7} | State={8} | Ask={9} | Bid={10}",
                CODE_NAME,
                SymbolName,
                lineName,
                intended,
                close.ToString("F2", CultureInfo.InvariantCulture),
                linePrice.ToString("F2", CultureInfo.InvariantCulture),
                epsEnterPrice.ToString("G17", CultureInfo.InvariantCulture),
                epsExitPrice.ToString("G17", CultureInfo.InvariantCulture),
                stateText,
                ask.ToString("F2", CultureInfo.InvariantCulture),
                bid.ToString("F2", CultureInfo.InvariantCulture)
            );
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

            double minSlPipsFromPips = Math.Max(0.0, MinSLPips) * 10.0;
            double minSlPriceFromPips = minSlPipsFromPips * Symbol.PipSize;

            double atrValue = (_atrMinSl != null && _atrMinSl.Result != null && _atrMinSl.Result.Count > 0)
                ? _atrMinSl.Result.LastValue
                : 0.0;

            double minSlPriceFromAtr = Math.Max(0.0, MinSlAtrMult) * atrValue;
            double minSlPriceFinal = Math.Max(minSlPriceFromPips, minSlPriceFromAtr);

            if (minSlPriceFinal > 0.0 && slDistancePrice < minSlPriceFinal)
                return;

            double bufferPipsInternal = Math.Max(0.0, RiskBufferPips) * 10.0;
            double sizingPips = slPips + bufferPipsInternal;
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
                Print(
                    "SKIP_TP_TARGET_INVALID | CodeName={0} | Symbol={1} | TradeType={2} | Entry={3} | TpTarget={4} | ReasonTag={5} | Reason=TP_NOT_FORWARD",
                    CODE_NAME,
                    SymbolName,
                    type,
                    entry.ToString("F2", CultureInfo.InvariantCulture),
                    tpTargetPrice.ToString("F2", CultureInfo.InvariantCulture),
                    string.IsNullOrWhiteSpace(reasonTag) ? "NA" : reasonTag
                );
                return;
            }

            double minTpInternalPips = Math.Max(0.0, MinTpDistancePips) * 10.0;
            if (minTpInternalPips > 0.0 && tpPipsFromTarget < minTpInternalPips)
            {
                Print(
                    "SKIP_TP_TOO_CLOSE | CodeName={0} | Symbol={1} | TradeType={2} | Entry={3} | TpTarget={4} | TpPips={5} | MinTpPips={6} | PipSize={7} | ReasonTag={8}",
                    CODE_NAME,
                    SymbolName,
                    type,
                    entry.ToString("F2", CultureInfo.InvariantCulture),
                    tpTargetPrice.ToString("F2", CultureInfo.InvariantCulture),
                    tpPipsFromTarget.ToString("F1", CultureInfo.InvariantCulture),
                    minTpInternalPips.ToString("F1", CultureInfo.InvariantCulture),
                    Symbol.PipSize.ToString("G17", CultureInfo.InvariantCulture),
                    string.IsNullOrWhiteSpace(reasonTag) ? "NA" : reasonTag
                );
                return;
            }

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

            if (EnablePivotHitLog)
            {
                meta.TouchS2 = EvaluateTouchOutcome(_s2, true, TouchResultLookbackBars);
                meta.TouchS3 = EvaluateTouchOutcome(_s3, true, TouchResultLookbackBars);
                meta.TouchR2 = EvaluateTouchOutcome(_r2, false, TouchResultLookbackBars);
                meta.TouchR3 = EvaluateTouchOutcome(_r3, false, TouchResultLookbackBars);
            }

            _metaByPosId[posId] = meta;

            if (EnablePivotHitLog)
            {
                double distToS2Price, distToS3Price, distToR2Price, distToR3Price;
                string nearest = GetNearestS2S3R2R3(entry, out distToS2Price, out distToS3Price, out distToR2Price, out distToR3Price);

                double distToS2Pips = distToS2Price / Symbol.PipSize;
                double distToS3Pips = distToS3Price / Symbol.PipSize;
                double distToR2Pips = distToR2Price / Symbol.PipSize;
                double distToR3Pips = distToR3Price / Symbol.PipSize;

                string band = GetPivotBandLabel(entry);

                Print(
                    "ENTRY_PIVOT_CONTEXT | CodeName={0} | Symbol={1} | PosId={2} | EntryTimeUtc={3:o} | TradeType={4} | Entry={5} | Band={6} | Nearest={7} | S2={8} S3={9} R2={10} R3={11} | dS2Price={12} dS2Pips={13:F2} | dS3Price={14} dS3Pips={15:F2} | dR2Price={16} dR2Pips={17:F2} | dR3Price={18} dR3Pips={19:F2} | Mode={20}",
                    CODE_NAME,
                    SymbolName,
                    posId,
                    meta.EntryTimeUtc,
                    type.ToString(),
                    entry.ToString("F2", CultureInfo.InvariantCulture),
                    band,
                    nearest,
                    _s2.ToString("F2", CultureInfo.InvariantCulture),
                    _s3.ToString("F2", CultureInfo.InvariantCulture),
                    _r2.ToString("F2", CultureInfo.InvariantCulture),
                    _r3.ToString("F2", CultureInfo.InvariantCulture),
                    distToS2Price.ToString("G17", CultureInfo.InvariantCulture),
                    distToS2Pips,
                    distToS3Price.ToString("G17", CultureInfo.InvariantCulture),
                    distToS3Pips,
                    distToR2Price.ToString("G17", CultureInfo.InvariantCulture),
                    distToR2Pips,
                    distToR3Price.ToString("G17", CultureInfo.InvariantCulture),
                    distToR3Pips,
                    _outsideMode.ToString()
                );
            }

            if (!_mfeMaeByPosId.ContainsKey(posId))
                _mfeMaeByPosId[posId] = new MfeMae { MfeDollars = 0.0, MaeDollars = 0.0 };
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

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (args == null || args.Position == null)
                return;

            Position p = args.Position;
            if (p.SymbolName != SymbolName)
                return;

            long posId = p.Id;

            if (EnablePivotHitLog && _metaByPosId.TryGetValue(posId, out EntryMeta meta))
            {
                DateTime closeTimeUtc = DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);
                double net = p.NetProfit;

                Print(
                    "TOUCH_RESULT | CodeName={0} | PosId={1} | CloseTimeUtc={2:o} | Level=S2 | Touched={3} | Result={4} | TouchBarTimeUtc={5:o} | Net={6}",
                    CODE_NAME,
                    posId,
                    closeTimeUtc,
                    meta.TouchS2 != null && meta.TouchS2.Touched ? "Y" : "N",
                    meta.TouchS2 != null ? meta.TouchS2.Result : "None",
                    meta.TouchS2 != null ? DateTime.SpecifyKind(meta.TouchS2.TouchBarTimeUtc, DateTimeKind.Utc) : DateTime.MinValue,
                    net.ToString("F2", CultureInfo.InvariantCulture)
                );

                Print(
                    "TOUCH_RESULT | CodeName={0} | PosId={1} | CloseTimeUtc={2:o} | Level=S3 | Touched={3} | Result={4} | TouchBarTimeUtc={5:o} | Net={6}",
                    CODE_NAME,
                    posId,
                    closeTimeUtc,
                    meta.TouchS3 != null && meta.TouchS3.Touched ? "Y" : "N",
                    meta.TouchS3 != null ? meta.TouchS3.Result : "None",
                    meta.TouchS3 != null ? DateTime.SpecifyKind(meta.TouchS3.TouchBarTimeUtc, DateTimeKind.Utc) : DateTime.MinValue,
                    net.ToString("F2", CultureInfo.InvariantCulture)
                );

                Print(
                    "TOUCH_RESULT | CodeName={0} | PosId={1} | CloseTimeUtc={2:o} | Level=R2 | Touched={3} | Result={4} | TouchBarTimeUtc={5:o} | Net={6}",
                    CODE_NAME,
                    posId,
                    closeTimeUtc,
                    meta.TouchR2 != null && meta.TouchR2.Touched ? "Y" : "N",
                    meta.TouchR2 != null ? meta.TouchR2.Result : "None",
                    meta.TouchR2 != null ? DateTime.SpecifyKind(meta.TouchR2.TouchBarTimeUtc, DateTimeKind.Utc) : DateTime.MinValue,
                    net.ToString("F2", CultureInfo.InvariantCulture)
                );

                Print(
                    "TOUCH_RESULT | CodeName={0} | PosId={1} | CloseTimeUtc={2:o} | Level=R3 | Touched={3} | Result={4} | TouchBarTimeUtc={5:o} | Net={6}",
                    CODE_NAME,
                    posId,
                    closeTimeUtc,
                    meta.TouchR3 != null && meta.TouchR3.Touched ? "Y" : "N",
                    meta.TouchR3 != null ? meta.TouchR3.Result : "None",
                    meta.TouchR3 != null ? DateTime.SpecifyKind(meta.TouchR3.TouchBarTimeUtc, DateTimeKind.Utc) : DateTime.MinValue,
                    net.ToString("F2", CultureInfo.InvariantCulture)
                );
            }

            _metaByPosId.Remove(posId);
            _mfeMaeByPosId.Remove(posId);
            _emergencyCloseRequested.Remove(posId);
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

            int startMin = _tradeStartMinJst;
            int endMin = _tradeEndMinJst;
            int forceMin = _forceFlatMinJst;

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

        private void ResolveTradingWindowMinutesOrDefaults()
        {
            const string DEF_START = "09:15";
            const string DEF_END = "02:00";
            const string DEF_FORCE = "02:50";

            string startText = string.IsNullOrWhiteSpace(TradeStartTimeJst) ? DEF_START : TradeStartTimeJst.Trim();
            string endText = string.IsNullOrWhiteSpace(TradeEndTimeJst) ? DEF_END : TradeEndTimeJst.Trim();
            string forceText = string.IsNullOrWhiteSpace(ForceFlatTimeJst) ? DEF_FORCE : ForceFlatTimeJst.Trim();

            bool okStart = TryParseHmToMinutes(startText, out int startMin);
            bool okEnd = TryParseHmToMinutes(endText, out int endMin);
            bool okForce = TryParseHmToMinutes(forceText, out int forceMin);

            if (!okStart)
            {
                Print("TIME_PARSE_FAIL | CodeName={0} | Field=TradeStartTimeJst | Text={1} | Fallback={2}", CODE_NAME, startText, DEF_START);
                TryParseHmToMinutes(DEF_START, out startMin);
            }

            if (!okEnd)
            {
                Print("TIME_PARSE_FAIL | CodeName={0} | Field=TradeEndTimeJst | Text={1} | Fallback={2}", CODE_NAME, endText, DEF_END);
                TryParseHmToMinutes(DEF_END, out endMin);
            }

            if (!okForce)
            {
                Print("TIME_PARSE_FAIL | CodeName={0} | Field=ForceFlatTimeJst | Text={1} | Fallback={2}", CODE_NAME, forceText, DEF_FORCE);
                TryParseHmToMinutes(DEF_FORCE, out forceMin);
            }

            _tradeStartMinJst = startMin;
            _tradeEndMinJst = endMin;
            _forceFlatMinJst = forceMin;
        }

        private bool TryParseHmToMinutes(string text, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string t = text.Trim();
            string[] parts = t.Split(':');
            if (parts.Length != 2)
                return false;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m))
                return false;

            if (h < 0 || h > 23) return false;
            if (m < 0 || m > 59) return false;

            minutes = h * 60 + m;
            return true;
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

        // ================= NEWS FILTER (UTC) =================

        private void LoadEconomicCalendarUtc(string raw)
        {
            _highImpactEventsUtc.Clear();
            _highImpactEventItemsUtc.Clear();

            if (string.IsNullOrWhiteSpace(raw))
            {
                Print("ECON_PARSED | CodeName={0} | UniqueHighImpactCount=0", CODE_NAME);
                Print("ECON_LOADED | CodeName={0} | Count=0 | BeforeMin={1} AfterMin={2}", CODE_NAME, Math.Max(0, MinutesBeforeNews), Math.Max(0, MinutesAfterNews));
                return;
            }

            var uniqueKeys = new HashSet<string>();

            string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string lineRaw in lines)
            {
                string line = lineRaw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.Contains("DateTime") && line.Contains("Event") && line.Contains("Importance"))
                    continue;

                if (line.Length >= 2 && line[0] == '"' && line[line.Length - 1] == '"')
                    line = line.Substring(1, line.Length - 2);

                string[] parts = line.Split(new[] { ',' }, 3);
                if (parts.Length < 1) continue;

                string dtText = parts[0].Trim();
                string eventName = parts.Length >= 2 ? parts[1].Trim() : "";
                string importance = parts.Length >= 3 ? parts[2].Trim() : "";

                if (!TryParseUtcDateTime(dtText, out DateTime dtUtc))
                    continue;

                // High Impact only (case-insensitive contains 'HIGH')
                string impUpper = (importance ?? "").ToUpperInvariant();
                if (!impUpper.Contains("HIGH"))
                    continue;

                string key = dtUtc.ToString("o") + "|" + eventName + "|" + importance;
                if (!uniqueKeys.Add(key))
                    continue;

                if (!_highImpactEventsUtc.Contains(dtUtc))
                    _highImpactEventsUtc.Add(dtUtc);

                _highImpactEventItemsUtc.Add(new EconomicEventItem
                {
                    UtcTime = dtUtc,
                    EventName = eventName,
                    Importance = importance
                });
            }

            _highImpactEventsUtc.Sort();
            _highImpactEventItemsUtc.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));

            Print("ECON_PARSED | CodeName={0} | UniqueHighImpactCount={1}", CODE_NAME, _highImpactEventsUtc.Count);
            Print("ECON_LOADED | CodeName={0} | Count={1} | BeforeMin={2} AfterMin={3}", CODE_NAME, _highImpactEventsUtc.Count, Math.Max(0, MinutesBeforeNews), Math.Max(0, MinutesAfterNews));

            // 017_014: Emit event list (UTC) for physical confirmation in analysis
            for (int i = 0; i < _highImpactEventItemsUtc.Count; i++)
            {
                EconomicEventItem e = _highImpactEventItemsUtc[i];
                Print("HIGH_IMPACT_EVENT | CodeName={0} | UtcTime={1:yyyy-MM-dd HH:mm} | Event={2} | Importance={3}", CODE_NAME, e.UtcTime, string.IsNullOrWhiteSpace(e.EventName) ? "NA" : e.EventName, string.IsNullOrWhiteSpace(e.Importance) ? "NA" : e.Importance);
            }
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
