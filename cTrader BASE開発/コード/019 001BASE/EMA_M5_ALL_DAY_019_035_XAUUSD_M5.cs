// ============================================================
// CODE NAME (Project Constitution compliant)
// ============================================================
// BASE: EMA_M5_ALL_DAY_019_029_XAUUSD_M5
// THIS: EMA_M5_ALL_DAY_019_035_XAUUSD_M5
// ARCHIVE_ID: 019_029 (file export marker; no runtime effect)
// ============================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Reflection;
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
        // BASE: EMA_M5_ALL_DAY_019_029_XAUUSD_M5
        // THIS: EMA_M5_ALL_DAY_019_035_XAUUSD_M5
        private const string CODE_NAME = "EMA_M5_ALL_DAY_019_035_XAUUSD_M5";
        private const string BOT_LABEL = "EMA_M5_ALL_DAY_019_035_XAUUSD_M5";
        const int EMA_PERIOD_FIXED = 20;

        // ============================================================
        // 001MODE=ON 固定（封印値）: 001のエントリー/SL/TPはパラメーターから影響を受けない
        // 影響許可：資金管理・ロット制御 / 経済指標 / 取引時間帯 / 緊急クローズ
        // ============================================================
        private const int SEALED001_EMA_PERIOD = 20;
        private const double SEALED001_MAX_SPREAD_PIPS = 0.0;

        private const double SEALED001_MIN_SL_PIPS = 30.0;
        private const int SEALED001_MIN_SL_ATR_PERIOD = 14;
        private const double SEALED001_MIN_SL_ATR_MULT = 0.5;

        private const double SEALED001_RR = 1.0;
        private const double SEALED001_MIN_TP_DISTANCE_PIPS = 0.0;


        // ============================================================
        // PROレポート（HTML＋JSON埋め込み）用データ
        // ============================================================
        private double _proInitialBalance;
        private readonly List<ProClosedTrade> _proClosedTrades = new List<ProClosedTrade>();


        // ============================================================
        // PROレポート / PARAMスナップショット（状態）
        // ============================================================
        private bool _proReportWritten = false;
        private string _proReportLastSavedPath = null;

        private string _paramSnapshotJson = null;
        private readonly List<ParamSnapshotEntry> _paramSnapshotEntries = new List<ParamSnapshotEntry>();

        private enum ProSession
        {
            Unknown = 0,
            Tokyo = 1,
            Europe = 2,
            NewYork = 3
        }

        private sealed class ProClosedTrade
        {
            public DateTime CloseTimeUtc { get; set; }
            public double NetProfit { get; set; }
            public string SymbolName { get; set; }
        }

        private sealed class ParamSnapshotEntry
        {
            public string PropertyName { get; set; }
            public string DisplayName { get; set; }
            public string GroupName { get; set; }
            public string TypeName { get; set; }
            public string ValueText { get; set; }
        }


        // ============================================================
        // パラメーター
        // ============================================================

        #region 資金管理・ロット制御


        public enum エントリーモード
        {
            [Display(Name = "001")]
            Mode001 = 0,

            [Display(Name = "EMA")]
            EMA = 1
        }

        public enum 口座通貨
        {
            USD = 0,
            JPY = 1
        }


        public enum 方向判定価格ソース
        {
            [Display(Name = "終値")]
            終値 = 0,

            [Display(Name = "始値")]
            始値 = 1,

            [Display(Name = "HL2")]
            HL2 = 2,

            [Display(Name = "Typical")]
            Typical = 3
        }

        [Parameter("口座通貨（USD/JPY）", Group = "資金管理・ロット制御", DefaultValue = 口座通貨.USD)]
        public 口座通貨 AccountCurrency { get; set; }

        [Parameter("１トレードのリスク額", Group = "資金管理・ロット制御", DefaultValue = 1000.0, MinValue = 0.0)]
        public double RiskDollars { get; set; }

        [Parameter("１トレードのリスク（％）", Group = "資金管理・ロット制御", DefaultValue = 0.0, MinValue = 0.0, MaxValue = 100.0)]
        public double RiskPercent { get; set; }

        [Parameter("PROレポート出力", Group = "資金管理・ロット制御", DefaultValue = true)]
        public bool EnableProReport { get; set; }

        [Parameter("PROレポート保存先フォルダ", Group = "資金管理・ロット制御", DefaultValue = "D:\\保管庫")]
        public string ProReportOutputFolder { get; set; }

        [Parameter("リスク計算バッファ（PIPS）", Group = "資金管理・ロット制御", DefaultValue = 50.0, MinValue = 0.0)]
        public double RiskBufferPips { get; set; }

        [Parameter("想定スリッページ（PIPS）", Group = "資金管理・ロット制御", DefaultValue = 50.0, MinValue = 0.0)]
        public double SlipAllowancePips { get; set; }

        [Parameter("スリッページ許容をロット計算に含める", Group = "資金管理・ロット制御", DefaultValue = false)]
        public bool IncludeSlipAllowanceInSizing { get; set; }

        [Parameter("緊急クローズ倍率", Group = "資金管理・ロット制御", DefaultValue = 1.2, MinValue = 1.0)]
        public double EmergencyCloseMult { get; set; }

        [Parameter("最大ポジション数", Group = "資金管理・ロット制御", DefaultValue = 1, MinValue = 1)]
        public int MaxPositions { get; set; }

        [Parameter("最大ロット数（0=無制限）", Group = "資金管理・ロット制御", DefaultValue = 2.5, MinValue = 0.0)]
        public double MaxLotsCap { get; set; }

        #endregion


        #region 001制御・エントリー

        [Parameter("001MODE", Group = "001制御・エントリー", DefaultValue = true)]
        public bool Enable001Mode { get; set; }

        [Parameter("エントリーモード（001・EMA）", Group = "001制御・エントリー", DefaultValue = エントリーモード.Mode001)]
        public エントリーモード EntryMode { get; set; }

        [Parameter("001入口抑制構造を有効化", Group = "001制御・エントリー", DefaultValue = true)]
        public bool Enable001EntrySuppressionStructure { get; set; }

        [Parameter("EMAモードで001入口再現を有効化", Group = "001制御・エントリー", DefaultValue = false)]
        public bool EnableEma001EntryReplay { get; set; }

        [Parameter("RR緩和構造を有効化", Group = "001制御・エントリー", DefaultValue = true)]
        public bool EnableRrRelaxStructure { get; set; }

        #endregion


        #region 001入口露出（パラメータ再現）

        [Parameter("方向判定デッドゾーン幅（PIPS）", Group = "001入口露出（パラメータ再現）", DefaultValue = 10.0, MinValue = 0.0)]
        public double DirectionDeadzonePips { get; set; }

        [Parameter("方向判定ヒステリシス比率", Group = "001入口露出（パラメータ再現）", DefaultValue = 0.6, MinValue = 0.0, MaxValue = 1.0)]
        public double DirectionHysteresisExitEnterRatio { get; set; }

        [Parameter("方向状態最短維持バー数", Group = "001入口露出（パラメータ再現）", DefaultValue = 2, MinValue = 0)]
        public int DirectionStateMinHoldBars { get; set; }

        [Parameter("方向判定価格ソース", Group = "001入口露出（パラメータ再現）", DefaultValue = 方向判定価格ソース.終値)]
        public 方向判定価格ソース DirectionPriceSource { get; set; }

        [Parameter("最小RR緩和を有効化", Group = "001入口露出（パラメータ再現）", DefaultValue = true)]
        public bool EnableMinRrRelax { get; set; }

        [Parameter("最小RR緩和有効バー数", Group = "001入口露出（パラメータ再現）", DefaultValue = 6, MinValue = 0)]
        public int MinRrRelaxWindowBars { get; set; }

        [Parameter("緩和後最小RR比率", Group = "001入口露出（パラメータ再現）", DefaultValue = 0.7, MinValue = 0.0)]
        public double MinRrRelaxedRatio { get; set; }

        #endregion



        private bool IsEntryMode001()
        {
            return EntryMode == エントリーモード.Mode001;
        }
        private bool Use001FixedModeEquivalent()
        {
            // 001MODE=True と同等の「固定SL/TP/RR経路」を使う条件
            // ※TryEntryFramework001を使うわけではない（入口決定は別）
            return Enable001Mode || (IsEntryMode001() && Enable001EntrySuppressionStructure);
        }

        private bool IsEntryModeEMA()
        {
            return EntryMode == エントリーモード.EMA;
        }
        // 入口ルートの最終決定を追跡（ログのスパム防止：変化した時だけ出す）
        private string _lastEntryRoute = "";

        private void LogEntryRouteOnce(string route)
        {
            if (_lastEntryRoute == route) return;
            _lastEntryRoute = route;
            Print("ENTRY_ROUTE=" + route);
        }


        #region エントリー関連

        [Parameter("最大スプレッド（PIPS）", Group = "エントリー関連", DefaultValue = 0.0, MinValue = 0.0)]
        public double MaxSpreadPips { get; set; }

        [Parameter("エントリー最大距離（PIPS）", Group = "エントリー関連", DefaultValue = 50.0, MinValue = 0.0)]
        public double EntryMaxDistancePips { get; set; }

        [Parameter("再接近監視バー数", Group = "エントリー関連", DefaultValue = 36, MinValue = 1)]
        public int ReapproachWindowBars { get; set; }

        [Parameter("再接近最大距離（PIPS）", Group = "エントリー関連", DefaultValue = 40.0, MinValue = 0.0)]
        public double ReapproachMaxDistancePips { get; set; }

        [Parameter("EMA期間", Group = "エントリー関連", DefaultValue = 20, MinValue = 1)]
        public int EmaPeriod { get; set; }

        [Parameter("EMA判定ログ出力", Group = "エントリー関連", DefaultValue = false)]
        public bool EnableEmaDecisionLog { get; set; }

        [Parameter("エントリー方式（はい=EMAクロス / いいえ=上なら買い・下なら売り）", Group = "エントリー関連", DefaultValue = true)]
        public bool EntryTypeEmaCross { get; set; }

        [Parameter("最小保有時間（分）", Group = "エントリー関連", DefaultValue = 5, MinValue = 0)]
        public int MinHoldMinutes { get; set; }

        [Parameter("最低RR比", Group = "エントリー関連", DefaultValue = 1.0, MinValue = 0.0)]
        public double MinRRRatio { get; set; }

        #endregion


        #region SL関連・共通

        public enum SL方式
        {
            固定 = 0,
            ATR = 1,
            構造 = 2
        }

        [Parameter("SLを使用", Group = "SL関連・共通", DefaultValue = true)]
        public bool UseStopLoss { get; set; }

        [Parameter("SL方式（固定/ATR/構造）", Group = "SL関連・共通", DefaultValue = SL方式.固定)]
        public SL方式 SlMode { get; set; }

        [Parameter("最小SL（PIPS）", Group = "SL関連・共通", DefaultValue = 20.0, MinValue = 0.0)]
        public double MinSLPips { get; set; }

        [Parameter("最大SL（PIPS）", Group = "SL関連・共通", DefaultValue = 100.0, MinValue = 0.0)]
        public double MaxSlPipsInput { get; set; }

        #endregion


        #region SL関連・ATR

        [Parameter("SL/ATR期間", Group = "SL関連・ATR", DefaultValue = 14, MinValue = 1)]
        public int MinSlAtrPeriod { get; set; }

        [Parameter("SL/ATR倍率", Group = "SL関連・ATR", DefaultValue = 0.5, MinValue = 0.0)]
        public double MinSlAtrMult { get; set; }

        #endregion


        #region SL関連・構造SL

        [Parameter("スイング判定 左右本数", Group = "SL関連・構造SL", DefaultValue = 2, MinValue = 1)]
        public int SwingLR { get; set; }

        [Parameter("スイング探索本数", Group = "SL関連・構造SL", DefaultValue = 80, MinValue = 10)]
        public int SwingLookback { get; set; }

        [Parameter("構造SLバッファ（PIPS）", Group = "SL関連・構造SL", DefaultValue = 100.0, MinValue = 0.0)]
        public double StructureSlBufferPips { get; set; }

        [Parameter("構造SLが見つからない場合はエントリーしない", Group = "SL関連・構造SL", DefaultValue = true)]
        public bool BlockEntryIfNoStructureSl { get; set; }

        #endregion


        #region TP関連・共通

        public enum TP方式
        {
            SL倍率 = 0,
            固定 = 1,
            ATR = 2,
            構造 = 3
        }

        [Parameter("TPを使用", Group = "TP関連・共通", DefaultValue = true)]
        public bool EnableTakeProfit { get; set; }

        [Parameter("TP方式（ATR/構造/固定/SL倍率）", Group = "TP関連・共通", DefaultValue = TP方式.SL倍率)]
        public TP方式 TpMode { get; set; }

        [Parameter("最小TP距離（PIPS）", Group = "TP関連・共通", DefaultValue = 0.0, MinValue = 0.0)]
        public double MinTpDistancePips { get; set; }

        [Parameter("TP倍率（SL×倍率）", Group = "TP関連・共通", DefaultValue = 1.0, MinValue = 0.0)]
        public double TpMultiplier { get; set; }

        [Parameter("建値移動トリガー", Group = "TP関連・共通", DefaultValue = 1000.0, MinValue = 0.0)]
        public double BreakevenTriggerDollars { get; set; }

        #endregion


        #region TP関連・ATR

        [Parameter("TP/ATR期間", Group = "TP関連・ATR", DefaultValue = 14, MinValue = 1)]
        public int TpAtrPeriod { get; set; }

        [Parameter("TP/ATR倍率", Group = "TP関連・ATR", DefaultValue = 2.0, MinValue = 0.0)]
        public double TpAtrMult { get; set; }

        #endregion


        #region TP関連・構造TP

        [Parameter("TP構造スイング判定 左右本数（H1）", Group = "TP関連・構造TP", DefaultValue = 2, MinValue = 1)]
        public int TpSwingLR { get; set; }

        [Parameter("TP構造スイング探索本数（H1）", Group = "TP関連・構造TP", DefaultValue = 200, MinValue = 10)]
        public int TpSwingLookback { get; set; }

        [Parameter("構造TPバッファ（PIPS）", Group = "TP関連・構造TP", DefaultValue = 50.0, MinValue = 0.0)]
        public double StructureTpBufferPips { get; set; }

        #endregion


        #region TP関連・構造ブースト

        [Parameter("構造TPブースト１", Group = "TP関連・構造ブースト", DefaultValue = false)]
        public bool EnableStructureTpBoost { get; set; }

        [Parameter("構造TPブースト1開始R", Group = "TP関連・構造ブースト", DefaultValue = 0.9, MinValue = 0.0)]
        public double StructureTpBoostStartRStage1 { get; set; }

        [Parameter("構造TP第2段ブースト２", Group = "TP関連・構造ブースト", DefaultValue = true)]
        public bool EnableStructureTpBoostStage2 { get; set; }

        [Parameter("構造TPブースト2開始R", Group = "TP関連・構造ブースト", DefaultValue = 1.8, MinValue = 0.0)]
        public double StructureTpBoostStartRStage2 { get; set; }

        #endregion


        #region TP関連・ブースト２出口

        [Parameter("フラクタル左右本数（M15）", Group = "TP関連・ブースト２出口", DefaultValue = 2, MinValue = 1)]
        public int Stage2ExitFractalLR { get; set; }

        [Parameter("スイング割れ判定終値確定", Group = "TP関連・ブースト２出口", DefaultValue = true)]
        public bool Stage2ExitUseCloseConfirm { get; set; }

        #endregion


        #region TP関連・固定TP

        public enum 固定_SL倍率_TP
        {
            固定 = 0,
            SL倍率 = 1
        }

        [Parameter("固定 / SL倍率 TP", Group = "TP関連・固定TP", DefaultValue = 固定_SL倍率_TP.固定)]
        public 固定_SL倍率_TP FixedOrSlMultTp { get; set; }

        [Parameter("固定TP（PIPS）", Group = "TP関連・固定TP", DefaultValue = 0.0, MinValue = 0.0)]
        public double FixedTpPips { get; set; }

        #endregion


        #region 方向・判定補助

        [Parameter("EMA方向フィルター", Group = "方向・判定補助", DefaultValue = false)]
        public bool EnableEmaDirectionFilter { get; set; }

        #endregion


        #region ログ

        [Parameter("デバッグログ（JST併記）", Group = "ログ", DefaultValue = false)]
        public bool EnableDebugLogJst { get; set; }

        #endregion


        #region 経済指標フィルター（UTC）

        [Parameter("バックテスト用", Group = "経済指標フィルター（UTC）", DefaultValue = false)]
        public bool UseNewsBacktest2025 { get; set; }

        [Parameter("フォワード用（FRED API）", Group = "経済指標フィルター（UTC）", DefaultValue = false)]
        public bool UseNewsForwardFRED { get; set; }

        [Parameter("FRED APIキー", Group = "経済指標フィルター（UTC）", DefaultValue = "")]
        public string FredApiKey { get; set; }

        [Parameter("指標前の停止時間（分）", Group = "経済指標フィルター（UTC）", DefaultValue = 60)]
        public int MinutesBeforeNews { get; set; }

        [Parameter("指標後の停止時間（分）", Group = "経済指標フィルター（UTC）", DefaultValue = 60)]
        public int MinutesAfterNews { get; set; }

        #endregion


        #region 取引時間帯（JST）

        [Parameter("取引時間制御を有効にする", Group = "取引時間帯（JST）", DefaultValue = true)]
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
        private AverageTrueRange _atrMinSl001;
        private AverageTrueRange _atrTp;
        private Bars _barsH1;
        private ExponentialMovingAverage _ema001;
        private ExponentialMovingAverage _ema;

        private TimeZoneInfo _jstTz;

        // ============================================================
        // Compliance Layer 連携：取引許可ゲート（公開API）
        //  - BASE は判断しない（従うだけ）
        //  - Compliance は本ゲート以外で取引制御しない前提
        // ============================================================

        public bool TradePermission { get; private set; } = true;
        public string TradeStopReason { get; private set; } = "";
        public DateTime? TradeStopUntil { get; private set; } = null;


        /// <summary>
        /// 取引許可ゲートを更新する（変更経路は本メソッドに一本化）
        /// </summary>
        public void SetTradePermission(bool allow, string reason, DateTime? until)
        {
            ApplyTradePermission(allow, reason, until);
        }

        /// <summary>
        /// 取引停止（allow=false）ショートカット
        /// </summary>
        public void SetTradePermission(string reason, DateTime? until)
        {
            ApplyTradePermission(false, reason, until);
        }

        /// <summary>
        /// 取引許可へ戻す（allow=true）
        /// </summary>
        public void ClearTradePermission()
        {
            ApplyTradePermission(true, "", null);
        }

        private void ApplyTradePermission(bool allow, string reason, DateTime? until)
        {
            reason = reason ?? "";

            bool changed =
                (TradePermission != allow) ||
                (!string.Equals(TradeStopReason ?? "", reason, StringComparison.Ordinal)) ||
                (TradeStopUntil != until);

            TradePermission = allow;
            TradeStopReason = reason;
            TradeStopUntil = until;

            if (!changed)
                return;

            // 状態変更時のみ識別ログ（CODE NAME / BotLabel / 理由 / 期限）
            string untilStr = TradeStopUntil.HasValue ? TradeStopUntil.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "null";
            Print("TRADE_GATE_CHANGED | CodeName={0} | BotLabel={1} | Allow={2} | Reason={3} | Until={4}{5}",
                CODE_NAME, BOT_LABEL, TradePermission ? "true" : "false",
                string.IsNullOrWhiteSpace(TradeStopReason) ? "NA" : TradeStopReason,
                untilStr,
                BuildTimeTag(UtcNow()));
        }


        private readonly HashSet<long> _emergencyCloseRequested = new HashSet<long>();
        private readonly HashSet<long> _structureTpBoostedPosIds = new HashSet<long>(); // Stage1
        private readonly HashSet<long> _structureTpBoostedPosIdsStage2 = new HashSet<long>(); // Stage2


        // ============================================================
        // Stage2 判定（Single Source of Truth）
        // ============================================================
        private bool IsStage2(Position p)
        {
            if (p == null) return false;
            return _structureTpBoostedPosIdsStage2.Contains(p.Id);
        }

        // ============================================================
        // Stage2 Exit（状態変化）関連
        // ============================================================
        private Bars _barsM15;
        private Bars _barsD1;

        // Stage2 Exit: M15 Fractal swing-break (PositionId 단位)
        private readonly Dictionary<long, int> _s2StartM15IndexByPosId = new Dictionary<long, int>();
        private readonly Dictionary<long, double> _s2SwingLevelByPosId = new Dictionary<long, double>();
        private int _lastProcessedM15ClosedIndexForS2 = -1;


        // ATH（D1終値ブレイク）中の100ドルラウンド “ファーストタッチ” 管理
        private readonly HashSet<int> _athRoundFirstTouchedLevels = new HashSet<int>();


        private readonly Dictionary<long, string> _closeInitiatorByPosId = new Dictionary<long, string>();
        // ============================================================
        // 最小保有時間（001MODE用）
        // ============================================================
        // 001MODE: MinRR緩和（SET準拠）
        // ============================================================
        // NOTE:
        // - 031/032 のパラメータ欄は温存する（001MODE=ON のときのみ、SET値で内部上書き）
        // - 001.cbotset（ユーザー提供）に基づく値
        private const bool SET_EnableMinRrRelax = true;
        private const int SET_MinRrRelaxWindowBars = 6;
        private const double SET_MinRrRelaxedRatio = 0.7;
        private const double SET_MinRRRatio = 1.0;

        // ============================================================
        // ============================================================
        // 構造TPブースト（条件付き） 固定パラメータ（Phase I 仕様：二段）
        // ============================================================
        // Stage1: 2-200-50
        private const int SET_BoostStructureTpLR = 2;
        private const int SET_BoostStructureTpLookbackH1 = 200;
        private const double SET_BoostStructureTpBufferPips = 50.0;

        // Stage2: 2-300-50
        private const int SET_BoostStructureTpLookbackH1_Stage2 = 300;
        private const double SET_BoostStructureTpBufferPips_Stage2 = 50.0;


        // ============================================================
        // エントリー距離制限（001MODE用：距離 + 再接近） [001.cbotset準拠]
        // ============================================================
        private const double SET_EntryMaxDistancePips = 50.0;
        private const int SET_ReapproachWindowBars = 36;
        private const double SET_ReapproachMaxDistancePips = 40.0;

        private bool _rrRelaxPendingActive = false;
        private int _rrRelaxOriginBarIndex = -1; // Bars index (last closed bar index) at which pending was set
        private TradeType _rrRelaxPlannedType = TradeType.Buy;

        // ============================================================
        private bool _stopRequestedByRiskFailure = false;
        // ============================================================
        // 001MODE（001実在コード由来の入口抑制構造）※パラメータ追加なし
        // 参照元：EMA_M5_ALL_DAY_018_001（内部CODE_NAMEは問わない。ロジック事実のみ移植）
        // ============================================================

        private enum LineSideState { Neutral = 0, Above = 1, Below = 2 }

        // --- 001 defaults (fixed, no parameters in 031) ---
        private const double _001_DirectionDeadzonePips = 10.0;
        private const double _001_DirectionHysteresisExitEnterRatio = 0.6;
        private const int _001_DirectionStateMinHoldBars = 2;

        private const double _001_EntryMaxDistancePips = 50.0;
        private const int _001_ReapproachWindowBars = 36;
        private const double _001_ReapproachMaxDistancePips = 40.0;

        // --- EMA20 side state (hysteresis + minimum hold bars) ---
        private LineSideState _ema20SideState = LineSideState.Neutral;
        private int _ema20LastStateChangeIndex = -1;

        // --- Distance / reapproach pending (EMA cross only) ---
        private bool _ema20ReapproachPending = false;
        private int _ema20PendingCreatedSignalIndex = -1;
        private TradeType _ema20PendingTradeType = TradeType.Buy;
        private string _ema20PendingReasonTag = "";


        private enum RiskMode { Amount = 0, Percent = 1 }

        private enum SymbolCategory { Metal = 0, Fx = 1 }
        private SymbolCategory _symbolCategory = SymbolCategory.Fx;
        private int _pipsScale = 1;


        private enum TradingWindowState { AllowNewEntries = 0, HoldOnly = 1, ForceFlat = 2 }

        // --- Debug / block-state tracking (for JST log tagging and throttled logs) ---
        private TradingWindowState _lastTradingWindowState = TradingWindowState.AllowNewEntries;
        private TradingWindowState _lastTradingWindowStateTick = TradingWindowState.AllowNewEntries;
        private bool _wasNewsBlocked = false;
        private bool _wasSpreadBlocked = false;
        private DateTime _lastSkipByGatesLogUtc = DateTime.MinValue;
        private const int SKIP_BY_GATES_LOG_INTERVAL_MINUTES = 5;

        private int _tradeStartMinJst;
        private int _tradeEndMinJst;
        private int _forceFlatMinJst;

        private int _effectiveForceFlatMinJst;
        private DateTime _effectiveForceFlatComputedJstDate = DateTime.MinValue;
        private int _effectiveForceFlatLastLoggedMinJst = -1;

        private const int MARKET_CLOSE_BUFFER_MINUTES = 15; // 固定（要件：15分）

        private const int MARKET_FOLLOW_FORCE_FLAT_TILL_CLOSE_MINUTES = 15; // 要件：TimeTillClose<=15分で即ForceFlat
        private const int MARKET_FOLLOW_TICK_STALL_MINUTES = 5; // 要件：5分無音で異常

        private DateTime _lastTickUtc = DateTime.MinValue;
        private readonly HashSet<long> _marketCloseCloseRequested = new HashSet<long>();

        // ============================================================
        // Market Monitor (MarketHours / Tick Stall) - Logging only
        // ============================================================
        private bool? _mmLastIsOpened = null;
        private int _mmLastTillCloseBucket = int.MinValue;
        private bool _mmLastTickStall = false;
        private bool _mmStartupSnapshotLogged = false;



        // --- News provider split (future API-ready) ---

        // --- Anti-spam / anti-repeat controls (broker-friendly) ---
        // Key: $"{posId}|{barOpenUtcTicks}|{action}"
        private readonly HashSet<string> _oncePerBarActionGuard = new HashSet<string>();

        // ============================================================
        // ライフサイクル
        // ============================================================

        protected override void OnStart()
        {
            // PROレポート用 初期残高保存
            _proInitialBalance = Account.Balance;

            // ------------------------------------------------------------
            // Stage2 Exit 用インジケータ準備（M15 EMA20 / D1 Bars）
            // ------------------------------------------------------------
            _barsM15 = MarketData.GetBars(TimeFrame.Minute15); _barsD1 = MarketData.GetBars(TimeFrame.Daily);


            Positions.Closed += OnPositionsClosedForProReport;

            _jstTz = ResolveTokyoTimeZone();
            // ============================
            // Instance Stamp (one-shot)
            // ============================
            ValidateNewsModeOrStop();
            PrintInstanceStamp();
            PrintParameterSnapshot();

            if (Use001FixedModeEquivalent())

            {
                Print("001_FIXED_MODE_ON | CodeName={0} | Symbol={1} | EMA={2} | MaxSpreadPips={3} | MinSLPips={4} | ATR={5}*{6} | RR={7} | MinTpPips={8}",
                    CODE_NAME, SymbolName, SEALED001_EMA_PERIOD, SEALED001_MAX_SPREAD_PIPS, SEALED001_MIN_SL_PIPS, SEALED001_MIN_SL_ATR_PERIOD, SEALED001_MIN_SL_ATR_MULT, SEALED001_RR, SEALED001_MIN_TP_DISTANCE_PIPS);
            }

            _atrMinSl = Indicators.AverageTrueRange(Math.Max(1, MinSlAtrPeriod), MovingAverageType.Simple);
            _atrMinSl001 = Indicators.AverageTrueRange(SEALED001_MIN_SL_ATR_PERIOD, MovingAverageType.Simple);
            _atrTp = Indicators.AverageTrueRange(Math.Max(1, TpAtrPeriod), MovingAverageType.Simple);
            _barsH1 = MarketData.GetBars(TimeFrame.Hour);
            _ema = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EMA_PERIOD_FIXED);

            _ema001 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, Enable001Mode ? SEALED001_EMA_PERIOD : Math.Max(1, EmaPeriod));
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

            _lastTickUtc = UtcNow();
            Positions.Closed += OnPositionClosed;

            DateTime utcNow = UtcNow();

            MarketMonitorLogSnapshot(utcNow, "StartSnapshot");

            Print(
                "Started | CodeName={0} | Label={1} | Symbol={2} | PipSize={3} PipValue={4} | Window(JST) {5}-{6} ForceFlat={7} | " +
                "AccountCcy={8} RiskAmt={9} RiskPct={10} BufferPips={11} EmergMult={12} | MinSL(PIPS)={13} ATR({14})*{15} | MinTP(PIPS)={16} | MinRR_Filter={17} | EMA={18} | " +
                "NewsMode={19} Before={20} After={21} | SpreadMax(PIPS)={22}{23} | Mode={24} PipsScale={25} SlipAllow={26} SlipAllowInt={27} | TPmult={28}",
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
                EMA_PERIOD_FIXED,
                (UseNewsBacktest2025 ? "BACKTEST_2025" : (UseNewsForwardFRED ? "FORWARD_FRED" : "DISABLED")),
                Math.Max(0, MinutesBeforeNews),
                Math.Max(0, MinutesAfterNews),
                MaxSpreadPips.ToString("F2", CultureInfo.InvariantCulture),
                BuildTimeTag(utcNow),
                _symbolCategory,
                _pipsScale,
                FormatUserPipsWithDollar(SlipAllowancePips),
                InputPipsToInternalPips(SlipAllowancePips).ToString("F1", CultureInfo.InvariantCulture),
                Math.Max(0.0, TpMultiplier).ToString("F2", CultureInfo.InvariantCulture),
                    BuildTimeTag(UtcNow())

            );
        }

        protected override void OnStop()
        {

            // Stage2 state cleanup (safety)
            _structureTpBoostedPosIdsStage2.Clear();
            _s2StartM15IndexByPosId.Clear();
            _s2SwingLevelByPosId.Clear();
            _lastProcessedM15ClosedIndexForS2 = -1;

            // PROレポート出力（Backtest/Forward共通）
            try
            {
                if (EnableProReport)
                    ExportProReportHtmlOnce();
            }
            catch (Exception ex)
            {
                Print("PRO_REPORT_ERROR: " + ex.Message);
            }

            try
            {
                Positions.Closed -= OnPositionsClosedForProReport;
            }
            catch { }

            Positions.Closed -= OnPositionClosed;
        }

        protected override void OnTick()
        {
            _lastTickUtc = UtcNow();

            DateTime utcNow = UtcNow();

            bool allowNewEntries = true;
            bool allowPositionManagement = true;
            TradingWindowState state = TradingWindowState.AllowNewEntries;

            // Trading window filter: separate "new entries" and "position management".
            // - AllowNewEntries: controls entry logic (handled in OnBar).
            // - AllowPositionManagement: controls management logic (BE/TP boost/emergency close).
            //   We KEEP management active during HoldOnly to preserve Stage1/Stage2 observability.
            if (EnableTradingWindowFilter)
            {
                state = GetTradingWindowState(ToJst(utcNow));
                allowNewEntries = (state == TradingWindowState.AllowNewEntries);
                allowPositionManagement = (state == TradingWindowState.AllowNewEntries || state == TradingWindowState.HoldOnly);

                if (EnableDebugLogJst && state != _lastTradingWindowStateTick)
                {
                    Print("TIME_WINDOW_STATE | CodeName={0} | Label={1} | Symbol={2} | State={3} | AllowNewEntries={4} | AllowPositionManagement={5}{6}",
                        CODE_NAME, BOT_LABEL, SymbolName, state, allowNewEntries, allowPositionManagement, BuildTimeTag(utcNow));
                    _lastTradingWindowStateTick = state;
                }

                if (!allowPositionManagement)
                {
                    if (HasOpenBotPositions())
                        PrintSkipByGates("TIME_WINDOW", state.ToString(), utcNow);

                    return;
                }
            }

            // Position management (allowed during HoldOnly)
            // 001MODE=ON はエントリー/SL/TP固定のため、通常のTP更新/建値移動/構造TPブーストは混入させない
            if (!Enable001Mode)
            {
                ApplyBreakevenMoveIfNeeded();
                ApplyStructureTpBoostIfNeeded();
            }

            // 資金管理レイヤー：緊急クローズは常に有効
            ApplyEmergencyCloseIfNeeded();
        }

        protected override void OnBar()
        {
            if (Bars == null || Bars.Count < 50)
                return;

            DateTime utcNow = UtcNow();

            // reset per-bar guards
            _oncePerBarActionGuard.Clear();


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

            if (Enable001Mode
        || (Enable001EntrySuppressionStructure
            && (IsEntryMode001()
            || (EntryMode == エントリーモード.EMA && EnableEma001EntryReplay))))
            {
                LogEntryRouteOnce("Framework001");
                TryEntryFramework001(utcNow);
                return;
            }


            if (IsEntryMode001())
            {
                LogEntryRouteOnce("Param001");
                TryEntrySignal001_WithParamExit();
                return;
            }
            LogEntryRouteOnce("EMA");

            TryEmaEntry();
        }

        protected override void OnTimer()
        {
            DateTime utcNow = DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);
            DateTime jstNow = ToJst(utcNow);

            // Market monitor (logging only; no forced actions)
            MarketMonitorOnTimer(utcNow);

            // 取引時間制御が無効（OFF）の場合：
            //  - 日跨ぎ/週跨ぎを許可するため、時間起因の決済/停止ロジックは一切実行しない
            //  - MarketHours追従（早期閉場/週末）も無効
            if (!EnableTradingWindowFilter)
                return;

            // 早期閉場耐性：当日市場クローズに追従した ForceFlat 時刻を更新
            UpdateEffectiveForceFlatTime(utcNow);

            // 市場追従 ForceFlat（早期閉場・休場・ティック停止耐性）
            EnforceMarketFollowForceFlat(utcNow);

            TradingWindowState state = GetTradingWindowState(jstNow);
            if (state != TradingWindowState.ForceFlat)
                return;

            // 取引時間帯（ForceFlat）到達時の強制決済は CloseReason=MarketClose
            CloseAllPositionsForMarketClose("ForceFlatTime", null);
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
                acct = Account.Asset?.Name;
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
                if (IsMinHoldActive(p))
                    continue;

                if (_emergencyCloseRequested.Contains(p.Id))
                    continue;

                if (p.NetProfit <= threshold)
                {
                    _emergencyCloseRequested.Add(p.Id);

                    _closeInitiatorByPosId[p.Id] = "EMERGENCY_CLOSE";

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
        // ============================
        // TPブースト（条件付き：含み益R到達で構造TPへ切替）
        // - 001探索OFFの挙動を壊さず、伸び相場のみ上積みするためのブースト
        // - EnableStructureTpBoost=OFF の場合は完全に無効（既存挙動と同等）
        // ============================
        private void ApplyStructureTpBoostIfNeeded()
        {
            if (!EnableStructureTpBoost)
                return;

            // 001MODE=OFF のみ対象（001MODE=ONは固定SLTPのため無効）
            if (Enable001Mode)
                return;
            // TPを使わない設定なら何もしない
            if (!EffectiveEnableTakeProfit())
                return;


            // TP方式≠構造TP の場合：構造TPブーストは完全無効（到達不能化）
            if (TpMode != TP方式.構造)
                return;
            double r1 = Math.Max(0.0, StructureTpBoostStartRStage1);
            double r2 = Math.Max(0.0, StructureTpBoostStartRStage2);

            // r2 < r1 は設定ミスなので自動補正（同値扱い）
            if (r2 + 1e-12 < r1)
                r2 = r1;

            DateTime utcNow = UtcNow();

            foreach (var p in Positions)
            {
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                if (p.Label != BOT_LABEL) continue;

                // Stage2まで適用済みなら何もしない
                if (_structureTpBoostedPosIdsStage2.Contains(p.Id))
                    continue;

                // SL未設定ならR計算不可なのでスキップ
                if (!p.StopLoss.HasValue)
                    continue;

                double slAbs = p.StopLoss.Value;
                double slDist = Math.Abs(p.EntryPrice - slAbs);
                if (slDist <= Symbol.PipSize * 0.1)
                    continue;

                // 現在価格とエントリー価格の差分（価格距離）
                // Buy: Bid - Entry, Sell: Entry - Ask
                double profitDist = p.TradeType == TradeType.Buy
                    ? (Symbol.Bid - p.EntryPrice)
                    : (p.EntryPrice - Symbol.Ask);

                double unrealizedR = profitDist / slDist;

                bool stage1Active = _structureTpBoostedPosIds.Contains(p.Id);
                bool stage2Active = _structureTpBoostedPosIdsStage2.Contains(p.Id);

                // ------------------------------------------------------------
                // Stage1
                // ------------------------------------------------------------
                if (!stage1Active)
                {
                    if (unrealizedR + 1e-12 < r1)
                        continue;

                    // REACHED (always)
                    TryPrintOncePerBar(p.Id, "S1_REACHED",
                        string.Format(CultureInfo.InvariantCulture,
                            "STRUCT_TP_S1_REACHED | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | TriggerR>={5} | UnrealizedR={6}{7}",
                            CODE_NAME, BOT_LABEL, SymbolName, p.TradeType, p.Id,
                            r1.ToString("F2", CultureInfo.InvariantCulture),
                            unrealizedR.ToString("F2", CultureInfo.InvariantCulture),
                            BuildTimeTag(utcNow)));

                    // Stage1: 構造TP算出（固定：2-200-50）
                    if (!TryGetStructureTakeProfitBoostStage(p.TradeType, p.EntryPrice, SET_BoostStructureTpLookbackH1, SET_BoostStructureTpBufferPips, out double tpAbs))
                        continue;

                    // ALREADY_SET (same TP)
                    if (p.TakeProfit.HasValue && Math.Abs(p.TakeProfit.Value - tpAbs) <= (Symbol.PipSize * 0.1))
                    {
                        _structureTpBoostedPosIds.Add(p.Id);

                        TryPrintOncePerBar(p.Id, "S1_ALREADY_SET",
                            string.Format(CultureInfo.InvariantCulture,
                                "STRUCT_TP_S1_ALREADY_SET | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | UnrealizedR={5}{6}{7}",
                                CODE_NAME, BOT_LABEL, SymbolName, p.TradeType, p.Id,
                                unrealizedR.ToString("F2", CultureInfo.InvariantCulture),
                                BuildTpDiag(p, tpAbs),
                                BuildTimeTag(utcNow)));

                        continue;
                    }

                    bool modified = TryModifyPositionOncePerBar(p, p.StopLoss, tpAbs, "STRUCTURE_TP_STAGE1");

                    if (modified)
                    {
                        _structureTpBoostedPosIds.Add(p.Id);

                        TryPrintOncePerBar(p.Id, "S1_MODIFY_OK",
                            string.Format(CultureInfo.InvariantCulture,
                                "STRUCT_TP_S1_MODIFY_OK | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | UnrealizedR={5}{6} | LookbackH1={7} | BufferPips={8}{9}",
                                CODE_NAME, BOT_LABEL, SymbolName, p.TradeType, p.Id,
                                unrealizedR.ToString("F2", CultureInfo.InvariantCulture),
                                BuildTpDiag(p, tpAbs),
                                SET_BoostStructureTpLookbackH1,
                                SET_BoostStructureTpBufferPips.ToString("F1", CultureInfo.InvariantCulture),
                                BuildTimeTag(utcNow)));
                    }
                    else
                    {
                        // Modify failed or was skipped by guards (e.g., time-gate)
                        TryPrintOncePerBar(p.Id, "S1_MODIFY_FAIL",
                            string.Format(CultureInfo.InvariantCulture,
                                "STRUCT_TP_S1_MODIFY_FAIL | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | UnrealizedR={5}{6} | LookbackH1={7} | BufferPips={8} | Reason=ModifySkippedOrFailed{9}",
                                CODE_NAME, BOT_LABEL, SymbolName, p.TradeType, p.Id,
                                unrealizedR.ToString("F2", CultureInfo.InvariantCulture),
                                BuildTpDiag(p, tpAbs),
                                SET_BoostStructureTpLookbackH1,
                                SET_BoostStructureTpBufferPips.ToString("F1", CultureInfo.InvariantCulture),
                                BuildTimeTag(utcNow)));
                    }

                    continue;
                }

                // ------------------------------------------------------------
                // Stage2（Exitモード：TPは設定しない）
                // ------------------------------------------------------------
                if (!stage2Active)
                {
                    if (!EnableStructureTpBoostStage2)
                        continue;

                    if (unrealizedR + 1e-12 < r2)
                        continue;

                    _structureTpBoostedPosIdsStage2.Add(p.Id);

                    // Stage2 Exit (Fractal swing) context init
                    int s2StartM15Index = GetM15IndexAtTime(utcNow);
                    _s2StartM15IndexByPosId[p.Id] = s2StartM15Index;
                    _s2SwingLevelByPosId.Remove(p.Id);


                    // Stage2 Mode ON (always)
                    TryPrintOncePerBar(p.Id, "S2_MODE_ON",
                        string.Format(CultureInfo.InvariantCulture,
                            "STRUCT_TP_S2_MODE_ON | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | TriggerR>={5} | UnrealizedR={6}{7}",
                            CODE_NAME, BOT_LABEL, SymbolName, p.TradeType, p.Id,
                            r2.ToString("F2", CultureInfo.InvariantCulture),
                            unrealizedR.ToString("F2", CultureInfo.InvariantCulture),
                            BuildTimeTag(utcNow)));

                    continue;
                }
                else
                {
                    // ============================================================
                    // Stage2 Exit 条件（優先順：ATHラウンド → M15 EMA20）
                    // ============================================================

                    // 1) ATH（D1終値ブレイク）中：100ドルラウンドナンバー ファーストタッチ即決済
                    if (IsAthModeByDailyCloseBreakout(out double d1Close, out DateTime d1Time))
                    {
                        if (TryGetNextRoundNumberLevel100(p.TradeType, out int rnLevel))
                        {
                            bool allowFirst = IsRoundNumberFirstTouchAllowed(rnLevel);

                            if (allowFirst)
                            {
                                bool touched = (p.TradeType == TradeType.Buy)
                                    ? (Symbol.Bid >= rnLevel)
                                    : (Symbol.Ask <= rnLevel);

                                if (touched)
                                {
                                    MarkRoundNumberFirstTouched(rnLevel);

                                    string logLine = string.Format(CultureInfo.InvariantCulture,
                                        "STRUCT_S2_EXIT_ATH_RN_FIRST_TOUCH | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | D1CloseTimeUtc={5:yyyy-MM-dd} | D1Close={6} | RN={7} | Price={8}{9}",
                                        CODE_NAME, BOT_LABEL, SymbolName, p.TradeType, p.Id,
                                        d1Time, d1Close.ToString("F2", CultureInfo.InvariantCulture),
                                        rnLevel,
                                        (p.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask).ToString("F2", CultureInfo.InvariantCulture),
                                        BuildTimeTag(utcNow));

                                    _closeInitiatorByPosId[p.Id] = "S2_EXIT_ATH_RN_FIRST_TOUCH";
                                    TryClosePositionOncePerBar(p, "S2_EXIT_ATH_RN_FIRST_TOUCH", logLine);
                                    continue;
                                }
                            }
                        }
                    }
                    // 2) Stage2 Exit：M15 フラクタル（スイング割れ）※確定足の終値で判定
                    if (TryStage2SwingBreakExit(p, utcNow))
                    {
                        continue;
                    }

                    continue;
                }

            }
        }

        private bool TryGetStructureTakeProfitBoostStage(TradeType type, double entry, int lookbackH1, double bufferPips, out double tpAbs)
        {
            tpAbs = 0.0;

            int lr = Math.Max(1, SET_BoostStructureTpLR);
            int lookback = Math.Max(10, lookbackH1);

            if (_barsH1 == null || _barsH1.Count < lookback + (lr * 2) + 5)
                return false;

            double bufferInput = Math.Max(0.0, bufferPips);
            double bufferInternalPips = InputPipsToInternalPips(bufferInput);
            double bufferPrice = bufferInternalPips * Symbol.PipSize;

            if (type == TradeType.Buy)
            {
                if (!TryFindLastSwingHighOnBars(_barsH1, lr, lookback, out double swingHigh))
                    return false;

                tpAbs = swingHigh - bufferPrice;

                // 最小TP距離適用（共通パラメータの考え方を踏襲）
                double minTpPrice = PipsToPrice(Math.Max(0.0, MinTpDistancePips));
                if (minTpPrice > 0.0)
                {
                    double dist = Math.Abs(tpAbs - entry);
                    if (dist < minTpPrice)
                        tpAbs = entry + minTpPrice;
                }

                // entry未満になるような異常値は拒否
                if (tpAbs <= entry)
                    return false;

                return true;
            }
            else
            {
                if (!TryFindLastSwingLowOnBars(_barsH1, lr, lookback, out double swingLow))
                    return false;

                tpAbs = swingLow + bufferPrice;

                double minTpPrice = PipsToPrice(Math.Max(0.0, MinTpDistancePips));
                if (minTpPrice > 0.0)
                {
                    double dist = Math.Abs(tpAbs - entry);
                    if (dist < minTpPrice)
                        tpAbs = entry - minTpPrice;
                }

                if (tpAbs >= entry)
                    return false;

                return true;
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

                    TryModifyPositionOncePerBar(p, entry, p.TakeProfit, "BREAKEVEN");
                }
                else
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value <= entry)
                        continue;

                    TryModifyPositionOncePerBar(p, entry, p.TakeProfit, "BREAKEVEN");
                }
            }
        }


        // ============================================================
        // 最小保有時間（001MODE用）
        // ============================================================
        private bool IsMinHoldActive(Position p)
        {
            if (p == null) return false;
            if (!Enable001Mode) return false;
            int minHold = Math.Max(0, MinHoldMinutes);
            if (minHold <= 0) return false;

            // 最小保有時間は「EAによる裁量クローズ抑制」のみ。SL/TPの即時有効は常に必須。
            // Position.EntryTime（Robot(TimeZone=UTC)）を起点に経過時間で判定する。
            DateTime now = UtcNow();
            TimeSpan held = now - p.EntryTime;
            return held.TotalMinutes < minHold;
        }

        // ============================================================
        // 001MODE: MinRR緩和（SET準拠）ユーティリティ
        // ============================================================
        private void ClearRrRelaxPending(string reason)
        {
            if (!_rrRelaxPendingActive)
                return;

            _rrRelaxPendingActive = false;
            _rrRelaxOriginBarIndex = -1;

            if (EnableDebugLogJst)
            {
                Print("001_RR_PENDING_CLEAR | CodeName={0} | Symbol={1} | Reason={2}{3}",
                    CODE_NAME,
                    SymbolName,
                    string.IsNullOrWhiteSpace(reason) ? "NA" : reason,
                    BuildTimeTag(UtcNow()));
            }
        }


        // ============================================================
        // 001MODE：Direction（ヒステリシス + 状態維持）/ 距離・再接近
        // ============================================================

        private LineSideState UpdateLineSideState(LineSideState prev, double value, double refPrice, double epsEnter, double epsExit)
        {
            // epsEnter: enter threshold, epsExit: exit threshold (hysteresis)
            if (prev == LineSideState.Above)
            {
                if (value < refPrice - epsExit)
                    return LineSideState.Below;
                return LineSideState.Above;
            }

            if (prev == LineSideState.Below)
            {
                if (value > refPrice + epsExit)
                    return LineSideState.Above;
                return LineSideState.Below;
            }

            // Neutral -> decide by enter threshold
            if (value > refPrice + epsEnter)
                return LineSideState.Above;

            if (value < refPrice - epsEnter)
                return LineSideState.Below;

            return LineSideState.Neutral;
        }

        private bool Use001EntrySuppressionStructure()
        {
            // 001MODE に依存せず、入口抑制構造を適用できるようにする
            return Enable001Mode || Enable001EntrySuppressionStructure;
        }

        private bool Use001RrRelaxStructure()
        {
            // 001MODE=ON の場合は SET 値を採用
            if (Enable001Mode)
                return SET_EnableMinRrRelax;

            // パラメータ再現（001MODE=OFF）側は独立スイッチ + パラメータで制御
            return EnableRrRelaxStructure && EnableMinRrRelax;
        }

        private double GetDirectionPriceBySource(int signalIndex, double fallbackClose)
        {
            try
            {
                if (signalIndex < 0 || signalIndex >= Bars.Count)
                    return fallbackClose;

                switch (DirectionPriceSource)
                {
                    case 方向判定価格ソース.始値:
                        return Bars.OpenPrices[signalIndex];
                    case 方向判定価格ソース.HL2:
                        return (Bars.HighPrices[signalIndex] + Bars.LowPrices[signalIndex]) * 0.5;
                    case 方向判定価格ソース.Typical:
                        return (Bars.HighPrices[signalIndex] + Bars.LowPrices[signalIndex] + Bars.ClosePrices[signalIndex]) / 3.0;
                    case 方向判定価格ソース.終値:
                    default:
                        return Bars.ClosePrices[signalIndex];
                }
            }
            catch
            {
                return fallbackClose;
            }
        }




        private bool DirectionAllows001ModeEmaEntry(int signalIndex, double close, double ema, TradeType intended)
        {
            if (!Use001EntrySuppressionStructure())
                return true;

            // 001MODE=ON: 固定値（001.cbotset準拠）
            // 001MODE=OFF: 露出パラメータ（パラメータ再現）
            double enterPips = Enable001Mode ? Math.Max(0.0, _001_DirectionDeadzonePips) : Math.Max(0.0, DirectionDeadzonePips);
            double ratio = Enable001Mode ? Math.Max(0.0, _001_DirectionHysteresisExitEnterRatio) : Math.Max(0.0, DirectionHysteresisExitEnterRatio);
            int minHoldBars = Enable001Mode ? Math.Max(0, _001_DirectionStateMinHoldBars) : Math.Max(0, DirectionStateMinHoldBars);

            // Direction判定に使う価格（001MODE=ON は従来どおり close、OFF は PriceSource に従う）
            double value = Enable001Mode ? close : GetDirectionPriceBySource(signalIndex, close);

            // Convert to price thresholds (handle METAL pips scaling via InputPipsToInternalPips)
            double epsEnterPrice = InputPipsToInternalPips(enterPips) * Symbol.PipSize;
            double epsExitPrice = epsEnterPrice * ratio;

            // Compute desired side (enter + hysteresis)
            LineSideState desired = UpdateLineSideState(_ema20SideState, value, ema, epsEnterPrice, epsExitPrice);

            // Apply min-hold bars on state transitions
            if (desired != _ema20SideState)
            {
                int barsSince = (_ema20LastStateChangeIndex >= 0) ? (signalIndex - _ema20LastStateChangeIndex) : int.MaxValue;
                if (barsSince < minHoldBars)
                {
                    desired = _ema20SideState;
                }
                else
                {
                    _ema20SideState = desired;
                    _ema20LastStateChangeIndex = signalIndex;
                }
            }

            // Gate by intended direction
            if (intended == TradeType.Buy && _ema20SideState != LineSideState.Above)
                return false;

            if (intended == TradeType.Sell && _ema20SideState != LineSideState.Below)
                return false;

            return true;
        }



        private bool Is001ModeDistanceOkOrSetPending(int signalIndex, double close, double ema, TradeType intended, out string distanceReason)
        {
            distanceReason = "OK";

            if (!Use001EntrySuppressionStructure())
                return true;

            // 001MODE=ON: 001.cbotset準拠の固定値を適用（パラメータUIは温存）
            // 001MODE=OFF: 露出パラメータ（エントリー関連）を適用
            double maxDistPips = Enable001Mode ? Math.Max(0.0, SET_EntryMaxDistancePips) : Math.Max(0.0, EntryMaxDistancePips);
            int windowBars = Enable001Mode ? Math.Max(1, SET_ReapproachWindowBars) : Math.Max(1, ReapproachWindowBars);
            double reapproachMaxPips = Enable001Mode ? Math.Max(0.0, SET_ReapproachMaxDistancePips) : Math.Max(0.0, ReapproachMaxDistancePips);

            double maxDistPrice = InputPipsToInternalPips(maxDistPips) * Symbol.PipSize;
            if (maxDistPrice <= 0.0)
                return true;

            double dist = Math.Abs(close - ema);
            if (dist <= maxDistPrice)
                return true;

            // Too far -> set pending reapproach
            _ema20ReapproachPending = true;
            _ema20PendingCreatedSignalIndex = signalIndex;
            _ema20PendingTradeType = intended;
            _ema20PendingReasonTag = "EMA_CROSS";

            distanceReason = "PENDING_REAPPROACH_SET";

            if (EnableDebugLogJst)
            {
                double distPips = dist / Symbol.PipSize;
                Print("001_DIST_PENDING_SET | CodeName={0} | Symbol={1} | Type={2} | DistPips={3} | MaxDistPips={4} | WindowBars={5} | ReapproachMaxPips={6}{7}",
                    CODE_NAME,
                    SymbolName,
                    intended,
                    distPips.ToString("F1", CultureInfo.InvariantCulture),
                    maxDistPips.ToString("F1", CultureInfo.InvariantCulture),
                    windowBars,
                    reapproachMaxPips.ToString("F1", CultureInfo.InvariantCulture),
                    BuildTimeTag(UtcNow()));
            }

            return false;
        }




        private bool TryConsume001ModeReapproachSignal(int signalIndex, double close, double ema, out TradeType intended, out string reasonTag)
        {
            intended = TradeType.Buy;
            reasonTag = "NA";

            if (!Use001EntrySuppressionStructure())
                return false;

            if (!_ema20ReapproachPending || _ema20PendingCreatedSignalIndex < 0)
                return false;

            int window = Enable001Mode ? Math.Max(1, SET_ReapproachWindowBars) : Math.Max(1, ReapproachWindowBars);
            double maxPips = Enable001Mode ? Math.Max(0.0, SET_ReapproachMaxDistancePips) : Math.Max(0.0, ReapproachMaxDistancePips);

            int ageBars = signalIndex - _ema20PendingCreatedSignalIndex;

            if (ageBars > window)
            {
                // expired
                _ema20ReapproachPending = false;
                _ema20PendingCreatedSignalIndex = -1;

                if (EnableDebugLogJst)
                    Print("001_REAPPROACH_PENDING_EXPIRED | CodeName={0} | Symbol={1} | AgeBars={2} | Window={3}{4}",
                        CODE_NAME,
                        SymbolName,
                        ageBars,
                        window,
                        BuildTimeTag(UtcNow()));

                return false;
            }

            double maxDistPrice = InputPipsToInternalPips(maxPips) * Symbol.PipSize;
            double dist = Math.Abs(close - ema);

            if (dist > maxDistPrice)
                return false;

            // consume
            intended = _ema20PendingTradeType;
            reasonTag = string.IsNullOrWhiteSpace(_ema20PendingReasonTag) ? "EMA_CROSS_REAPPROACH" : (_ema20PendingReasonTag + "_REAPPROACH");

            _ema20ReapproachPending = false;
            _ema20PendingCreatedSignalIndex = -1;

            if (EnableDebugLogJst)
            {
                double distPips = dist / Symbol.PipSize;
                Print("001_REAPPROACH_CONSUMED | CodeName={0} | Symbol={1} | Type={2} | DistPips={3} | MaxPips={4} | AgeBars={5}/{6}{7}",
                    CODE_NAME,
                    SymbolName,
                    intended,
                    distPips.ToString("F1", CultureInfo.InvariantCulture),
                    maxPips.ToString("F1", CultureInfo.InvariantCulture),
                    ageBars,
                    window,
                    BuildTimeTag(UtcNow()));
            }

            return true;
        }
        // ============================================================
        // ENTRY_FRAMEWORK_001 (移植) : Enable001Mode == true のときのみ使用
        // - EMAクロス（確定足2本）
        // - 方向フィルタ無視（仕様）
        // - News filter は外側ガード（OnBar側）で適用済み
        // - pips単位はEA全体で035方式（_pipsScale）に統一
        // ============================================================
        private void TryEntryFramework001(DateTime utcNow)
        {
            if (_ema001 == null || _ema001.Result == null || _ema001.Result.Count < 3)
                return;

            int i1 = Bars.Count - 2; // last closed bar index
            int i2 = Bars.Count - 3;
            if (i2 < 0)
                return;

            double close1 = Bars.ClosePrices[i1];
            double close2 = Bars.ClosePrices[i2];

            double ema1 = _ema001.Result[i1];
            double ema2 = _ema001.Result[i2];

            if (ema1 == 0.0 || ema2 == 0.0)
                return;

            bool crossUp = (close2 <= ema2) && (close1 > ema1);
            bool crossDown = (close2 >= ema2) && (close1 < ema1);

            if (!crossUp && !crossDown)
                return;

            TradeType type = crossUp ? TradeType.Buy : TradeType.Sell;

            // Spread gate
            double bid = Symbol.Bid;
            double ask = Symbol.Ask;
            double spreadPrice = ask - bid;
            double spreadInternalPips = spreadPrice / Symbol.PipSize;

            double maxSpreadInternalPips = InputPipsToInternalPips(SEALED001_MAX_SPREAD_PIPS);
            if (maxSpreadInternalPips > 0.0 && spreadInternalPips > maxSpreadInternalPips)
            {
                if (EnableEmaDecisionLog)
                {
                    Print("BLOCK_E01_SPREAD | CodeName={0} | Symbol={1} | SpreadPips={2:F2} | MaxSpreadPips={3:F2}{4}",
                        CODE_NAME, SymbolName, spreadInternalPips, maxSpreadInternalPips, BuildTimeTag(utcNow));
                }
                return;
            }

            if (EnableEmaDecisionLog)
            {
                Print(
                    "EMA_SIGNAL | CodeName={0} | Symbol={1} | UtcNow={2:o} | Cross={3} | Type={4} | Close2={5} EMA2={6} | Close1={7} EMA1={8} | Bid={9} Ask={10} | SpreadPips={11:F2}",
                    CODE_NAME,
                    SymbolName,
                    DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
                    crossUp ? "UP" : "DOWN",
                    type,
                    close2,
                    ema2,
                    close1,
                    ema1,
                    bid,
                    ask,
                    spreadInternalPips
                );
            }

            // Risk amount
            if (_stopRequestedByRiskFailure)
                return;

            double riskAmount = GetRiskAmountInAccountCurrency(out RiskMode mode);
            if (riskAmount <= 0.0)
                return;

            // SL distance: max(MinSL from pips, MinSL from ATR)
            double minSlInternalPips = InputPipsToInternalPips(SEALED001_MIN_SL_PIPS);
            double minSlPriceFromPips = minSlInternalPips * Symbol.PipSize;

            double atrValue = (_atrMinSl001 != null && _atrMinSl001.Result != null && _atrMinSl001.Result.Count > 0)
                ? _atrMinSl001.Result.LastValue
                : 0.0;

            double minSlPriceFromAtr = Math.Max(0.0, SEALED001_MIN_SL_ATR_MULT) * atrValue;
            double slDistancePrice = Math.Max(minSlPriceFromPips, minSlPriceFromAtr);

            if (slDistancePrice <= 0.0)
            {
                if (EnableEmaDecisionLog)
                    Print("BLOCK_E02_SLDIST0 | CodeName={0} | Symbol={1} | SlDistPrice={2}{3}", CODE_NAME, SymbolName, slDistancePrice, BuildTimeTag(utcNow));
                return;
            }

            double entry = (type == TradeType.Buy) ? ask : bid;
            double stop = (type == TradeType.Buy) ? (entry - slDistancePrice) : (entry + slDistancePrice);

            double slInternalPips = slDistancePrice / Symbol.PipSize;
            if (slInternalPips <= 0.0)
                return;

            // TP target by RR
            double rr = SEALED001_RR;
            double tpTargetPrice = (type == TradeType.Buy) ? (entry + slDistancePrice * rr) : (entry - slDistancePrice * rr);

            double tpInternalPips = Math.Abs(tpTargetPrice - entry) / Symbol.PipSize;

            double minTpInternalPips = InputPipsToInternalPips(SEALED001_MIN_TP_DISTANCE_PIPS);
            if (minTpInternalPips > 0.0 && tpInternalPips < minTpInternalPips)
            {
                if (EnableEmaDecisionLog)
                {
                    Print("SKIP_TP_TOO_CLOSE | CodeName={0} | Symbol={1} | Type={2} | TpPips={3:F2} | MinTpPips={4:F2}{5}",
                        CODE_NAME, SymbolName, type, tpInternalPips, minTpInternalPips, BuildTimeTag(utcNow));
                }
                return;
            }

            // Volume sizing
            double bufferInternalPips = InputPipsToInternalPips(RiskBufferPips);
            double slipInternalPips = InputPipsToInternalPips(SlipAllowancePips);

            bool includeSlipInSizing = IncludeSlipAllowanceInSizing; // 001MODE only (this path)
            double sizingPips = slInternalPips + bufferInternalPips + (includeSlipInSizing ? slipInternalPips : 0.0);
            if (sizingPips <= 0.0)
                return;

            if (EnableEmaDecisionLog)
            {
                Print(
                    "EMA_PACKET | CodeName={0} | Symbol={1} | UtcNow={2:o} | Type={3} | Entry={4} | Stop={5} | SlPips={6:F2} | TP={7} | TpPips={8:F2} | RR={9:F2} | ATR={10} | Risk={11} | BufferInt={12:F2} | SlipInt={13:F2} | UseSlipInSizing={14} | SizingPips={15:F2}{16}",
                    CODE_NAME,
                    SymbolName,
                    DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
                    type,
                    entry,
                    stop,
                    slInternalPips,
                    tpTargetPrice,
                    tpInternalPips,
                    rr,
                    atrValue,
                    riskAmount,
                    bufferInternalPips,
                    slipInternalPips,
                    (includeSlipInSizing ? "YES" : "NO"),
                    sizingPips,
                    BuildTimeTag(utcNow)
                );
            }
            double volumeUnitsRaw = riskAmount / (sizingPips * Symbol.PipValue);
            long volumeInUnits = (long)Symbol.NormalizeVolumeInUnits(volumeUnitsRaw, RoundingMode.Down);

            if (volumeInUnits < Symbol.VolumeInUnitsMin)
            {
                if (EnableEmaDecisionLog)
                    Print("BLOCK_E03_VOLMIN | CodeName={0} | Symbol={1} | VolumeUnits={2} < Min={3}{4}",
                        CODE_NAME, SymbolName, volumeInUnits, Symbol.VolumeInUnitsMin, BuildTimeTag(utcNow));
                return;
            }

            if (MaxLotsCap > 0.0)
            {
                long maxUnits = (long)Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MaxLotsCap), RoundingMode.Down);
                if (maxUnits > 0 && volumeInUnits > maxUnits)
                    volumeInUnits = maxUnits;
            }

            // 001MODE: SL/TP always enabled immediately
            TradeResult res = ExecuteMarketOrder(type, SymbolName, volumeInUnits, BOT_LABEL, slInternalPips, tpInternalPips);
            if (res == null || !res.IsSuccessful || res.Position == null)
            {
                if (EnableEmaDecisionLog)
                    Print("BLOCK_E04_ORDERFAIL | CodeName={0} | Symbol={1} | Type={2}{3}", CODE_NAME, SymbolName, type, BuildTimeTag(utcNow));
                return;
            }

            // 評価基盤ログ：建玉確定
            PrintEntryCore(res.Position);

        }


        // ============================================================
        // 001シグナルのみ（SL/TPはパラメーター世界）
        //  - 001MODE=OFF かつ エントリーモード=001 のとき使用
        // ============================================================
        private void TryEntrySignal001_WithParamExit()
        {
            // 001シグナルのみ（SL/TPはパラメーター世界）
            //  - 001MODE=OFF かつ エントリーモード=001 のとき使用
            if (Enable001Mode)
                return;

            if (!IsEntryMode001())
                return;

            if (_ema001 == null || _ema001.Result == null || _ema001.Result.Count < 3)
                return;

            int i1 = Bars.Count - 2; // last closed bar index
            int i2 = Bars.Count - 3;
            if (i2 < 0)
                return;

            double close1 = Bars.ClosePrices[i1];
            double close2 = Bars.ClosePrices[i2];

            double ema1 = _ema001.Result[i1];
            double ema2 = _ema001.Result[i2];

            if (ema1 == 0.0 || ema2 == 0.0)
                return;

            bool crossUp = (close2 <= ema2) && (close1 > ema1);
            bool crossDown = (close2 >= ema2) && (close1 < ema1);

            TradeType? plannedType = null;
            string reasonTag = "NA";

            // ============================================================
            // 001入口抑制構造（Direction + 距離 + 再接近）を 001MODE=OFF 側にも適用
            //  - エントリーは「EMAクロス確定足」または「再接近（ウィンドウ内）」のみ
            // ============================================================
            int signalIndex = i1;

            // 1) EMAクロスがあれば優先（pendingはリセット）
            if (crossUp || crossDown)
            {
                plannedType = crossUp ? TradeType.Buy : TradeType.Sell;
                reasonTag = "001_EMA_CROSS";

                // new cross -> reset pending
                ClearRrRelaxPending("NEW_CROSS");
                _ema20ReapproachPending = false;
                _ema20PendingCreatedSignalIndex = -1;

                // Direction gate (stateful)
                if (!DirectionAllows001ModeEmaEntry(signalIndex, close1, ema1, plannedType.Value))
                {
                    return;
                }

                // Distance gate (set pending if too far)
                if (!Is001ModeDistanceOkOrSetPending(signalIndex, close1, ema1, plannedType.Value, out string distReason))
                {
                    return;
                }
            }
            else
            {
                // 2) クロスが無い場合は、再接近シグナルのみ許可
                if (!TryConsume001ModeReapproachSignal(signalIndex, close1, ema1, out TradeType intended, out string reapReason))
                    return;

                plannedType = intended;
                reasonTag = reapReason;

                if (!DirectionAllows001ModeEmaEntry(signalIndex, close1, ema1, plannedType.Value))
                {
                    return;
                }
            }

            if (plannedType == null)
                return;

            // Execute with parameter-based SL/TP
            ExecuteEntryWithPlannedType(plannedType.Value, reasonTag, close1, ema1, i1);
        }


        // ============================================================
        // 共通エントリー執行（パラメーター世界）
        //  - TryEmaEntry / TryEntrySignal001_WithParamExit から共通利用
        // ============================================================
        private void ExecuteEntryWithPlannedType(TradeType plannedType, string reasonTag, double close1, double ema1, int i1)
        {
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

            TradeType type = plannedType;
            double entry = (type == TradeType.Buy) ? ask : bid;

            if (EnableEmaDirectionFilter)
            {
                if (type == TradeType.Buy && close1 <= ema1)
                    return;
                if (type == TradeType.Sell && close1 >= ema1)
                    return;
            }

            // SL算出（構造=スイング or 最小SL）
            double intendedSlDistancePrice = 0.0;
            double stop = 0.0;

            if (SlMode == SL方式.構造)
            {
                if (!TryGetStructureStop(type, entry, out stop, out intendedSlDistancePrice))
                {
                    if (EnableDebugLogJst)
                        Print("STRUCTURE_SL_BLOCK | CodeName={0} | Symbol={1} | Type={2} | Action=NO_ENTRY | Reason=NO_VALID_SWING{3}",
                            CODE_NAME, SymbolName, type, BuildTimeTag(UtcNow()));

                    if (BlockEntryIfNoStructureSl)
                        return;
                }
                else
                {
                    double effSlPipsInternal = intendedSlDistancePrice / Symbol.PipSize;
                    double maxSlInternal = InputPipsToInternalPips(Math.Max(0.0, MaxSlPipsInput));

                    if (maxSlInternal > 0.0 && effSlPipsInternal > maxSlInternal)
                    {
                        if (EnableDebugLogJst)
                            Print("STRUCTURE_SL_BLOCK | CodeName={0} | Symbol={1} | Type={2} | Action=NO_ENTRY | Reason=MAX_SL_EXCEEDED | EffSLpips={3} MaxSLpips={4}{5}",
                                CODE_NAME, SymbolName, type,
                                effSlPipsInternal.ToString("F1", CultureInfo.InvariantCulture),
                                maxSlInternal.ToString("F1", CultureInfo.InvariantCulture),
                                BuildTimeTag(UtcNow()));
                        return;
                    }
                }
            }

            if (SlMode != SL方式.構造 || intendedSlDistancePrice <= 0.0)
            {
                double minSlPrice = GetMinSlPrice();
                if (minSlPrice <= 0.0)
                    return;

                stop = (type == TradeType.Buy) ? entry - minSlPrice : entry + minSlPrice;
                intendedSlDistancePrice = minSlPrice;
            }

            bool useTpEntry = EffectiveEnableTakeProfit();

            double tpDistance = 0.0;

            if (useTpEntry)
            {
                // TP方式（ATR/構造/固定/SL倍率）
                switch (TpMode)
                {
                    case TP方式.固定:
                        tpDistance = PipsToPrice(Math.Max(0.0, FixedTpPips));
                        break;

                    case TP方式.ATR:
                        {
                            double atr = (_atrTp != null && _atrTp.Result != null && _atrTp.Result.Count > 0) ? _atrTp.Result.LastValue : 0.0;
                            tpDistance = Math.Max(0.0, TpAtrMult) * atr;
                        }
                        break;

                    case TP方式.構造:
                        {
                            if (!TryGetStructureTakeProfit(type, entry, out double tpAbs))
                            {
                                double atr = (_atrTp != null && _atrTp.Result != null && _atrTp.Result.Count > 0) ? _atrTp.Result.LastValue : 0.0;
                                tpDistance = Math.Max(0.0, TpAtrMult) * atr;
                            }
                            else
                            {
                                tpDistance = Math.Abs(tpAbs - entry);
                            }
                        }
                        break;

                    case TP方式.SL倍率:
                    default:
                        {
                            double tpMult = Math.Max(0.0, TpMultiplier);
                            tpDistance = intendedSlDistancePrice * tpMult;
                        }
                        break;
                }

                double minTpPrice = PipsToPrice(Math.Max(0.0, MinTpDistancePips));
                if (minTpPrice > 0.0 && tpDistance < minTpPrice)
                    tpDistance = minTpPrice;

                // RRフィルタ
                double expRr = (intendedSlDistancePrice > 0.0) ? (tpDistance / intendedSlDistancePrice) : 0.0;
                double effectiveMinRr = Math.Max(0.0, MinRRRatio);

                // 001 RR緩和 Pending（001MODE=ON: SET / OFF: 露出パラメータ）
                if (Use001RrRelaxStructure() && _rrRelaxPendingActive)
                {
                    double relaxed = Use001FixedModeEquivalent() ? SET_MinRrRelaxedRatio : MinRrRelaxedRatio;

                    effectiveMinRr = Math.Max(0.0, relaxed);
                }

                if (effectiveMinRr > 0.0 && expRr + 1e-12 < effectiveMinRr)
                {
                    if (EnableDebugLogJst)
                        Print("RR_FILTER_BLOCK | CodeName={0} | Symbol={1} | Type={2} | Action=NO_ENTRY | ExpRR={3} | MinRR={4}{5}",
                            CODE_NAME,
                            SymbolName,
                            type,
                            expRr.ToString("F2", CultureInfo.InvariantCulture),
                            effectiveMinRr.ToString("F2", CultureInfo.InvariantCulture),
                            BuildTimeTag(UtcNow()));
                    // RR不足: 001 RR緩和構造が有効なら Pending を開始して見送り（次バー以降、緩和閾値で再評価）
                    if (Use001RrRelaxStructure() && !_rrRelaxPendingActive)
                    {
                        _rrRelaxPendingActive = true;
                        _rrRelaxOriginBarIndex = i1;
                        _rrRelaxPlannedType = type;

                        if (EnableDebugLogJst)
                        {
                            Print("001_RR_PENDING_SET | CodeName={0} | Symbol={1} | Type={2} | ExpRR={3} | MinRR={4} | WindowBars={5}{6}",
                                CODE_NAME,
                                SymbolName,
                                type,
                                expRr.ToString("F2", CultureInfo.InvariantCulture),
                                effectiveMinRr.ToString("F2", CultureInfo.InvariantCulture),
                                Use001FixedModeEquivalent() ? SET_MinRrRelaxWindowBars : MinRrRelaxWindowBars,

                                BuildTimeTag(UtcNow()));
                        }
                    }

                    return;
                }
            }

            // 固定 / SL倍率 TP ガード（UIは維持、ロジック側で整合）
            if (TpMode == TP方式.固定 && FixedOrSlMultTp == 固定_SL倍率_TP.SL倍率)
            {
                // 矛盾：TP方式=固定 なのに SL倍率が選ばれている -> 固定優先
                if (EnableDebugLogJst)
                    Print("TP_GUARD | CodeName={0} | Symbol={1} | Action=FORCE_FIXED | Reason=TpModeFixedButSelectorSlMult{2}",
                        CODE_NAME, SymbolName, BuildTimeTag(UtcNow()));
            }
            if (TpMode == TP方式.SL倍率 && FixedOrSlMultTp == 固定_SL倍率_TP.固定)
            {
                // 矛盾：TP方式=SL倍率 なのに 固定が選ばれている -> SL倍率優先
                if (EnableDebugLogJst)
                    Print("TP_GUARD | CodeName={0} | Symbol={1} | Action=FORCE_SLMULT | Reason=TpModeSlMultButSelectorFixed{2}",
                        CODE_NAME, SymbolName, BuildTimeTag(UtcNow()));
            }

            double tp = (type == TradeType.Buy) ? entry + tpDistance : entry - tpDistance;

            PlaceTrade(type, entry, stop, tp, reasonTag);
        }


        private void TryEmaEntry()
        {
            if (_ema == null || _ema.Result == null || _ema.Result.Count < 3)
                return;

            int i1 = Bars.Count - 2; // last closed bar index
            int i2 = Bars.Count - 3;
            if (i2 < 0)
                return;

            double close1 = Bars.ClosePrices[i1];
            double close2 = Bars.ClosePrices[i2];

            double ema1 = _ema.Result[i1];
            double ema2 = _ema.Result[i2];

            bool crossUp = close2 <= ema2 && close1 > ema1;
            bool crossDown = close2 >= ema2 && close1 < ema1;

            // Entry type selection
            bool useCrossEntry = Enable001Mode || EntryTypeEmaCross;

            TradeType? plannedType = null;

            // ============================================================
            // 001MODE: MinRR緩和（SET準拠） - Pending管理
            // ============================================================
            bool isRrPending = false;

            if (Use001RrRelaxStructure())
            {
                int window = Enable001Mode ? Math.Max(0, SET_MinRrRelaxWindowBars) : Math.Max(0, MinRrRelaxWindowBars);

                if (_rrRelaxPendingActive)
                {
                    int ageBars = i1 - _rrRelaxOriginBarIndex;
                    if (window <= 0 || ageBars > window)
                    {
                        ClearRrRelaxPending("EXPIRED");
                    }
                }

                // Pending中は「クロス待ち」をせず、同方向の再判定を行う
                if (_rrRelaxPendingActive)
                {
                    plannedType = _rrRelaxPlannedType;
                    isRrPending = true;
                }
            }

            string reasonTag = "NA";

            // ============================================================
            // 001MODE：001実在コード由来の入口抑制構造（Direction + 距離 + 再接近）
            //  - エントリーは「EMAクロス確定足」または「再接近（ウィンドウ内）」のみ
            //  - 方向は EMA20 の状態維持（ヒステリシス + 最短維持バー）
            //  - クロス直後の乖離が大きい場合は「再接近待ち」に回す
            // ============================================================
            if (Use001EntrySuppressionStructure())
            {
                int signalIndex = i1;

                // 1) EMAクロスがあれば優先（pendingはリセット）
                if (crossUp || crossDown)
                {
                    plannedType = crossUp ? TradeType.Buy : TradeType.Sell;
                    reasonTag = "EMA_CROSS";

                    // new cross -> reset pending
                    _ema20ReapproachPending = false;
                    _ema20PendingCreatedSignalIndex = -1;

                    // Direction gate (stateful)
                    if (!DirectionAllows001ModeEmaEntry(signalIndex, close1, ema1, plannedType.Value))
                    {
                        return;
                    }

                    // Distance gate (set pending if too far)
                    if (!Is001ModeDistanceOkOrSetPending(signalIndex, close1, ema1, plannedType.Value, out string distReason))
                    {
                        return;
                    }
                }
                else
                {
                    // 2) クロスが無い場合は、再接近シグナルのみ許可
                    if (!TryConsume001ModeReapproachSignal(signalIndex, close1, ema1, out TradeType intended, out string reapReason))
                        return;

                    plannedType = intended;
                    reasonTag = reapReason;

                    if (!DirectionAllows001ModeEmaEntry(signalIndex, close1, ema1, plannedType.Value))
                    {
                        return;
                    }
                }
            }
            else
            {
                // ============================================================
                // 通常：クロス or レジーム（上なら買い／下なら売り）
                // ============================================================
                if (useCrossEntry)
                {
                    if (!isRrPending)
                    {
                        if (!crossUp && !crossDown)
                            return;

                        plannedType = crossUp ? TradeType.Buy : TradeType.Sell;
                        reasonTag = "EMA_CROSS";
                    }
                    else
                    {
                        // Pending中は同方向の再判定
                        reasonTag = "EMA_CROSS_PENDING";
                    }
                }
                else
                {
                    if (close1 > ema1)
                        plannedType = TradeType.Buy;
                    else if (close1 < ema1)
                        plannedType = TradeType.Sell;
                    else
                        return;

                    reasonTag = "EMA_REGIME";
                }
            }

            if (plannedType == null)
                return;

            ExecuteEntryWithPlannedType(plannedType.Value, reasonTag, close1, ema1, i1);
        }


        private bool EffectiveEnableStopLoss()
        {
            // 001MODE=ON のときは 001 固定（常にON）
            if (Enable001Mode)
                return true;

            return UseStopLoss;
        }

        private bool EffectiveEnableTakeProfit()
        {
            if (Enable001Mode)
                return true;

            return EnableTakeProfit;
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


        // ===== 020 ADD: 構造（スイング）SL（復帰） =====

        private bool TryGetStructureStop(TradeType type, double entry, out double stop, out double slDistancePrice)
        {
            stop = 0.0;
            slDistancePrice = 0.0;

            int lr = Math.Max(1, SwingLR);
            int lookback = Math.Max(10, SwingLookback);

            // 必要バー数チェック（左右LR + 探索範囲 + 余裕）
            if (Bars == null || Bars.Count < lookback + (lr * 2) + 5)
                return false;

            double bufferInput = Math.Max(0.0, StructureSlBufferPips);
            double bufferInternalPips = InputPipsToInternalPips(bufferInput);
            double bufferPrice = bufferInternalPips * Symbol.PipSize;

            if (type == TradeType.Buy)
            {
                if (!TryFindLastSwingLow(lr, lookback, out double swingLow))
                    return false;

                stop = swingLow - bufferPrice;
                slDistancePrice = Math.Abs(entry - stop);
                return slDistancePrice > 0.0;
            }
            else
            {
                if (!TryFindLastSwingHigh(lr, lookback, out double swingHigh))
                    return false;

                stop = swingHigh + bufferPrice;
                slDistancePrice = Math.Abs(stop - entry);
                return slDistancePrice > 0.0;
            }
        }

        private bool TryFindLastSwingLow(int lr, int lookback, out double swingLow)
        {
            swingLow = 0.0;

            int start = Bars.Count - lr - 2;
            int end = Math.Max(lr, Bars.Count - lookback);

            for (int i = start; i >= end; i--)
            {
                double v = Bars.LowPrices[i];
                bool isSwing = true;

                for (int j = 1; j <= lr; j++)
                {
                    if (Bars.LowPrices[i - j] <= v || Bars.LowPrices[i + j] <= v)
                    {
                        isSwing = false;
                        break;
                    }
                }

                if (isSwing)
                {
                    swingLow = v;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindLastSwingHigh(int lr, int lookback, out double swingHigh)
        {
            swingHigh = 0.0;

            int start = Bars.Count - lr - 2;
            int end = Math.Max(lr, Bars.Count - lookback);

            for (int i = start; i >= end; i--)
            {
                double v = Bars.HighPrices[i];
                bool isSwing = true;

                for (int j = 1; j <= lr; j++)
                {
                    if (Bars.HighPrices[i - j] >= v || Bars.HighPrices[i + j] >= v)
                    {
                        isSwing = false;
                        break;
                    }
                }

                if (isSwing)
                {
                    swingHigh = v;
                    return true;
                }
            }

            return false;
        }


        // ============================================================
        // 019_002 ADD: 構造TP（H1スイング） + フォールバック（ATR）
        // ============================================================

        private bool TryGetStructureTakeProfit(TradeType type, double entry, out double tpAbs)
        {
            tpAbs = 0.0;

            int lr = Math.Max(1, TpSwingLR);
            int lookback = Math.Max(10, TpSwingLookback);

            if (_barsH1 == null || _barsH1.Count < lookback + (lr * 2) + 5)
                return false;

            double bufferInput = Math.Max(0.0, StructureTpBufferPips);
            double bufferInternalPips = InputPipsToInternalPips(bufferInput);
            double bufferPrice = bufferInternalPips * Symbol.PipSize;

            if (type == TradeType.Buy)
            {
                if (!TryFindLastSwingHighOnBars(_barsH1, lr, lookback, out double swingHigh))
                    return false;

                // Buy: 高値の少し手前に置く（到達率重視）
                tpAbs = swingHigh - bufferPrice;
                if (tpAbs <= entry)
                    return false;

                return true;
            }
            else
            {
                if (!TryFindLastSwingLowOnBars(_barsH1, lr, lookback, out double swingLow))
                    return false;

                // Sell: 安値の少し手前に置く（到達率重視）
                tpAbs = swingLow + bufferPrice;
                if (tpAbs >= entry)
                    return false;

                return true;
            }
        }

        private bool TryFindLastSwingLowOnBars(Bars bars, int lr, int lookback, out double swingLow)
        {
            swingLow = 0.0;

            if (bars == null)
                return false;

            int start = bars.Count - lr - 2;
            int end = Math.Max(lr, bars.Count - lookback);

            for (int i = start; i >= end; i--)
            {
                double v = bars.LowPrices[i];
                bool isSwing = true;

                for (int j = 1; j <= lr; j++)
                {
                    if (bars.LowPrices[i - j] <= v || bars.LowPrices[i + j] <= v)
                    {
                        isSwing = false;
                        break;
                    }
                }

                if (isSwing)
                {
                    swingLow = v;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindLastSwingHighOnBars(Bars bars, int lr, int lookback, out double swingHigh)
        {
            swingHigh = 0.0;

            if (bars == null)
                return false;

            int start = bars.Count - lr - 2;
            int end = Math.Max(lr, bars.Count - lookback);

            for (int i = start; i >= end; i--)
            {
                double v = bars.HighPrices[i];
                bool isSwing = true;

                for (int j = 1; j <= lr; j++)
                {
                    if (bars.HighPrices[i - j] >= v || bars.HighPrices[i + j] >= v)
                    {
                        isSwing = false;
                        break;
                    }
                }

                if (isSwing)
                {
                    swingHigh = v;
                    return true;
                }
            }

            return false;
        }


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
            // Compliance gate：新規注文（保留注文含む発行直前）で必ず参照
            if (!TradePermission)
                return;

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

            bool useSl = EffectiveEnableStopLoss();
            bool useTp = EffectiveEnableTakeProfit();

            double intendedSlDistancePrice = Math.Abs(entry - stop);
            if (intendedSlDistancePrice <= 0.0)
                return;

            double intendedSlPips = intendedSlDistancePrice / Symbol.PipSize;
            if (intendedSlPips <= 0.0)
                return;

            double tpPipsFromTarget = 0.0;
            if (useTp)
            {
                tpPipsFromTarget = Math.Abs(tpTargetPrice - entry) / Symbol.PipSize;
                if (tpPipsFromTarget <= 0.0)
                    return;
            }

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

                bool includeSlipInSizing = (!Enable001Mode) || IncludeSlipAllowanceInSizing;
                double sizingPips = effectiveSlPips + bufferPips + (includeSlipInSizing ? slipInternalPips : 0.0);
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

                TradeResult openRes;

                if (Enable001Mode)
                {
                    // 001MODE本体は不変。探索モード時のみSL/TPのON/OFFを反映。
                    double? slPipsToSend = useSl ? (double?)effectiveSlPips : null;
                    double? tpPipsToSend = useTp ? (double?)tpPipsFromTarget : null;
                    openRes = ExecuteMarketOrder(type, SymbolName, volumeInUnits, BOT_LABEL, slPipsToSend, tpPipsToSend);
                }
                else
                {
                    openRes = ExecuteMarketOrder(type, SymbolName, volumeInUnits, BOT_LABEL);
                }
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
                TradeResult modRes = null;

                // SL/TPは探索モード時のみON/OFFを反映。エントリー後にAbsolute指定で再設定して、スリッページ後でも価格整合を取る。
                if (useSl || useTp)
                {
                    double? slAbs = useSl ? (double?)slPrice : null;
                    double? tpAbs = useTp ? (double?)tpPrice : null;
                    modRes = ModifyPosition(pos, slAbs, tpAbs, ProtectionType.Absolute);
                }

                if (EnableDebugLogJst)
                {
                    Print(
                        "POST_ENTRY | CodeName={0} | Symbol={1} | PosId={2} | SL_Attached={3} | TP_Attached={4}{5}",
                        CODE_NAME,
                        SymbolName,
                        pos.Id,
                        (pos.StopLoss.HasValue ? pos.StopLoss.Value.ToString("F5", CultureInfo.InvariantCulture) : "null"),
                        (pos.TakeProfit.HasValue ? pos.TakeProfit.Value.ToString("F5", CultureInfo.InvariantCulture) : "null"),
                        BuildTimeTag(UtcNow())
                    );
                }

                // 評価基盤ログ：建玉確定（SL/TPをAbsolute補正した最終値を記録）
                PrintEntryCore(pos);


                bool slOk = !useSl || (pos.StopLoss.HasValue && pos.StopLoss.Value > 0.0);
                if (!slOk)
                {
                    // If SL is still not set after modify, close immediately to prevent uncontrolled risk.
                    _closeInitiatorByPosId[pos.Id] = "SL_SET_FAILED_CLOSE";
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

                double expRrLog = (effectiveSlPips > 0.0) ? (tpPipsFromTarget / effectiveSlPips) : 0.0;

                bool slCalcOk = (intendedSlPips > 0.0);
                bool tpCalcOk = (tpPipsFromTarget > 0.0);

                Print(
                    "ENTRY | CodeName={0} | Symbol={1} | Type={2} | VolUnits={3} | Entry={4} | IntendedSLpips={5} EffSLpips={6} | TPpips={7} | RiskMode={8} RiskInput={9}({10}) Balance={11} | ExpLoss={12} | Reason={13} | SL_Calc={14} SL_Attach={15} | TP_Calc={16} TP_Attach={17} | SlipAllow={18} SlipAllowInt={19} | ExpLossAtSL={20} | ExpRR={21} | MinRR={22} | TPmult={23}{24}",
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
                    (slCalcOk ? "OK" : "NG"),
                    (useSl ? "ON" : "OFF"),
                    (tpCalcOk ? "OK" : "NG"),
                    (useTp ? "ON" : "OFF"),
                    FormatUserPipsWithDollar(slipInputPips),
                    slipInternalPips.ToString("F1", CultureInfo.InvariantCulture),
                    expectedLossAtSl.ToString("F2", CultureInfo.InvariantCulture),
                    expRrLog.ToString("F2", CultureInfo.InvariantCulture),
                    Math.Max(0.0, MinRRRatio).ToString("F2", CultureInfo.InvariantCulture),
                    Math.Max(0.0, TpMultiplier).ToString("F2", CultureInfo.InvariantCulture),
                    BuildTimeTag(UtcNow())

                );
                Print(
                    "ENTRY_MODE | CodeName={0} | Symbol={1} | 001MODE={2} | EntryType={3}{4}",
                    CODE_NAME,
                    SymbolName,
                    (Enable001Mode ? "ON" : "OFF"),
                    (reasonTag == "EMA_CROSS" ? "CROSS" : (reasonTag == "EMA_REGIME" ? "REGIME" : "OTHER")),
                    BuildTimeTag(UtcNow())
                );

                Print(
                    "SLTP_MODE | CodeName={0} | Symbol={1} | 001MODE={2} | Path={3}{4}",
                    CODE_NAME,
                    SymbolName,
                    (Use001FixedModeEquivalent() ? "ON" : "OFF"),
                    (Use001FixedModeEquivalent() ? "001" : "022"),

                    BuildTimeTag(UtcNow())
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
            _effectiveForceFlatMinJst = _forceFlatMinJst;
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
            bool inForceFlat = IsInTimeWindow(nowMin, _effectiveForceFlatMinJst, _tradeStartMinJst);
            if (inForceFlat)
                return TradingWindowState.ForceFlat;

            bool inTradeWindow = IsInTimeWindow(nowMin, _tradeStartMinJst, _tradeEndMinJst);
            if (inTradeWindow)
                return TradingWindowState.AllowNewEntries;

            return TradingWindowState.HoldOnly;
        }

        // ============================================================
        // 市場クローズ追従 ForceFlat（早期閉場耐性）
        // ============================================================
        private void UpdateEffectiveForceFlatTime(DateTime utcNow)
        {
            // デフォルト：固定 ForceFlat（JST）
            _effectiveForceFlatMinJst = _forceFlatMinJst;

            try
            {
                if (Symbol == null || Symbol.MarketHours == null)
                    return;

                // MarketHours が提供する「現在セッションのクローズまでの残り時間」を使用
                if (!Symbol.MarketHours.IsOpened())
                    return;

                TimeSpan tillClose = Symbol.MarketHours.TimeTillClose();
                if (tillClose <= TimeSpan.Zero)
                    return;

                // 異常値ガード（週末跨ぎ等の「次回クローズ」までが長すぎるケース）
                if (tillClose > TimeSpan.FromHours(18))
                    return;

                DateTime utcClose = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).Add(tillClose);
                DateTime jstClose = ToJst(utcClose);
                DateTime jstCloseMinus = jstClose.AddMinutes(-MARKET_CLOSE_BUFFER_MINUTES);

                // 固定 ForceFlat（JST）は「クローズ日の同日」に置く
                int ffH = _forceFlatMinJst / 60;
                int ffM = _forceFlatMinJst % 60;
                DateTime fixedForceFlatJst = new DateTime(jstClose.Year, jstClose.Month, jstClose.Day, ffH, ffM, 0);

                DateTime effectiveJst = (jstCloseMinus < fixedForceFlatJst) ? jstCloseMinus : fixedForceFlatJst;
                int effectiveMin = effectiveJst.Hour * 60 + effectiveJst.Minute;

                _effectiveForceFlatMinJst = effectiveMin;

                // 1日1回（JST日付）かつ変更時のみログ（スパム抑制）
                DateTime jstNow = ToJst(utcNow);
                DateTime jstDate = jstNow.Date;

                if (_effectiveForceFlatComputedJstDate != jstDate || _effectiveForceFlatLastLoggedMinJst != _effectiveForceFlatMinJst)
                {
                    _effectiveForceFlatComputedJstDate = jstDate;
                    _effectiveForceFlatLastLoggedMinJst = _effectiveForceFlatMinJst;

                    // fixed と同値のときは静かに（差が出た場合のみログ）
                    if (_effectiveForceFlatMinJst != _forceFlatMinJst)
                    {
                        string fixedStr = string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", ffH, ffM);
                        string effStr = string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", _effectiveForceFlatMinJst / 60, _effectiveForceFlatMinJst % 60);
                        string closeStr = jstClose.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                        Print("MARKET_CLOSE_ADJUST | CodeName={0} | BotLabel={1} | FixedForceFlat(JST)={2} | EffectiveForceFlat(JST)={3} | MarketClose(JST)={4} | BufferMin={5}{6}",
                            CODE_NAME, BOT_LABEL, fixedStr, effStr, closeStr, MARKET_CLOSE_BUFFER_MINUTES, BuildTimeTag(UtcNow()));
                    }
                }
            }
            catch
            {
                // 安全側：固定時刻のまま運用（例外で落とさない）
                _effectiveForceFlatMinJst = _forceFlatMinJst;
            }
        }

        // ============================================================

        private bool IsInTimeWindow(int nowMin, int startMin, int endMin)
        {
            if (startMin == endMin)
                return true;

            if (startMin < endMin)
                return nowMin >= startMin && nowMin < endMin;

            // crosses midnight
            return nowMin >= startMin || nowMin < endMin;
        }


        // ============================================================
        // 市場追従 ForceFlat（早期閉場・休場・ティック停止耐性）
        // ============================================================

        // ============================================================
        // Market Monitor (Logging only) - no trading behavior changes
        // ============================================================
        private void MarketMonitorLogSnapshot(DateTime utcNow, string trigger)
        {
            try
            {
                bool isOpened = false;
                TimeSpan? tillClose = null;

                try
                {
                    isOpened = Symbol.MarketHours.IsOpened();
                    tillClose = Symbol.MarketHours.TimeTillClose();
                }
                catch
                {
                    // Some symbols/brokers may not provide MarketHours in backtest; keep defaults.
                    isOpened = false;
                    tillClose = null;
                }

                double lastTickAgeMin = (_lastTickUtc == DateTime.MinValue) ? double.PositiveInfinity
                    : Math.Max(0, (utcNow - _lastTickUtc).TotalMinutes);

                string tillCloseStr = (tillClose.HasValue) ? ((int)Math.Round(tillClose.Value.TotalMinutes)).ToString(CultureInfo.InvariantCulture) : "NA";

                Print(
                    "MARKET_MONITOR_STATE | CodeName={0} | BotLabel={1} | Trigger={2} | Utc={3:yyyy-MM-dd HH:mm:ss} | Jst={4:yyyy-MM-dd HH:mm:ss} | IsOpened={5} | TimeTillCloseMin={6} | LastTickAgeMin={7:F1}",
                    CODE_NAME,
                    BOT_LABEL,
                    trigger,
                    utcNow,
                    ToJst(utcNow),
                    isOpened,
                    tillCloseStr,
                    lastTickAgeMin
                );
            }
            catch
            {
                // Logging must never break runtime.
            }
        }

        private int MarketMonitorBucketForTillClose(TimeSpan? tillClose)
        {
            if (!tillClose.HasValue)
                return int.MaxValue;

            int m = (int)Math.Floor(tillClose.Value.TotalMinutes);

            // Bucket thresholds (crossing only logs)
            if (m <= 5) return 5;
            if (m <= 15) return 15;
            if (m <= 30) return 30;
            if (m <= 60) return 60;
            return 999;
        }

        private void MarketMonitorOnTimer(DateTime utcNow)
        {
            // One-shot snapshot after start (even if no state changes occur)
            if (!_mmStartupSnapshotLogged)
            {
                _mmStartupSnapshotLogged = true;
                MarketMonitorLogSnapshot(utcNow, "Startup");
            }

            bool isOpened;
            TimeSpan? tillClose;

            try
            {
                isOpened = Symbol.MarketHours.IsOpened();
                tillClose = Symbol.MarketHours.TimeTillClose();
            }
            catch
            {
                // Not available in some backtests/symbols.
                return;
            }

            bool tickStall = false;
            if (_lastTickUtc != DateTime.MinValue)
            {
                tickStall = (utcNow - _lastTickUtc).TotalMinutes >= MARKET_FOLLOW_TICK_STALL_MINUTES;
            }

            // IsOpened change
            if (_mmLastIsOpened == null || _mmLastIsOpened.Value != isOpened)
            {
                _mmLastIsOpened = isOpened;
                MarketMonitorLogSnapshot(utcNow, "IsOpenedChange");
            }

            // TimeTillClose bucket crossing
            int bucket = MarketMonitorBucketForTillClose(tillClose);
            if (_mmLastTillCloseBucket == int.MinValue)
            {
                _mmLastTillCloseBucket = bucket;
            }
            else if (_mmLastTillCloseBucket != bucket)
            {
                _mmLastTillCloseBucket = bucket;
                MarketMonitorLogSnapshot(utcNow, "TillCloseCross");
            }

            // Tick stall toggle
            if (_mmLastTickStall != tickStall)
            {
                _mmLastTickStall = tickStall;
                MarketMonitorLogSnapshot(utcNow, tickStall ? "TickStallOn" : "TickStallOff");
            }
        }


        private void EnforceMarketFollowForceFlat(DateTime utcNow)
        {
            // ポジションが無ければ判定コストを抑える
            bool hasPos = false;
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                var p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                if (p.Label != BOT_LABEL) continue;
                hasPos = true;
                break;
            }
            if (!hasPos)
                return;

            string trigger;
            TimeSpan? tillClose;
            if (!ShouldTriggerMarketFollowForceFlat(utcNow, out trigger, out tillClose))
                return;

            CloseAllPositionsForMarketClose(trigger, tillClose);
        }

        private bool ShouldTriggerMarketFollowForceFlat(DateTime utcNow, out string trigger, out TimeSpan? tillClose)
        {
            trigger = null;
            tillClose = null;

            bool isOpened;
            TimeSpan ttc;

            try
            {
                isOpened = Symbol.MarketHours.IsOpened();
            }
            catch
            {
                // MarketHoursが取得不能な場合はティック停止のみで判断（保守的）
                isOpened = true;
            }

            // ① 市場クローズ（休場含む）
            if (!isOpened)
            {
                trigger = "IsOpenedFalse";
                return true;
            }

            // ② TimeTillClose <= 15分
            try
            {
                ttc = Symbol.MarketHours.TimeTillClose();
                tillClose = ttc;

                if (ttc <= TimeSpan.FromMinutes(MARKET_FOLLOW_FORCE_FLAT_TILL_CLOSE_MINUTES))
                {
                    trigger = "TimeTillClose";
                    return true;
                }
            }
            catch
            {
                // TimeTillCloseが取れない場合は継続（ティック停止へ）
            }

            // ③ ティック停止（5分無音）
            if (_lastTickUtc != DateTime.MinValue)
            {
                var silent = utcNow - _lastTickUtc;
                if (silent >= TimeSpan.FromMinutes(MARKET_FOLLOW_TICK_STALL_MINUTES))
                {
                    trigger = "TickStall";
                    return true;
                }
            }

            return false;
        }

        private void CloseAllPositionsForMarketClose(string trigger, TimeSpan? timeTillClose)
        {
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                if (p.Label != BOT_LABEL) continue;

                if (_marketCloseCloseRequested.Contains(p.Id))
                    continue;

                _marketCloseCloseRequested.Add(p.Id);

                _closeInitiatorByPosId[p.Id] = "FORCE_CLOSE:MARKET_CLOSE";

                ClosePosition(p);

                string ttcStr = timeTillClose.HasValue
                    ? timeTillClose.Value.TotalMinutes.ToString("F2", CultureInfo.InvariantCulture)
                    : "null";

                Print(
                    "MARKET_FOLLOW_FORCE_FLAT | CodeName={0} | Label={1} | PosId={2} | Trigger={3} | TimeTillCloseMin={4} | Reason=MarketClose{5}",
                    CODE_NAME,
                    BOT_LABEL,
                    p.Id,
                    string.IsNullOrWhiteSpace(trigger) ? "NA" : trigger,
                    ttcStr,
                    BuildTimeTag(UtcNow())
                );
            }
        }

        private void CloseAllPositionsForSymbol(string reason)
        {
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                if (p.Label != BOT_LABEL) continue;
                _closeInitiatorByPosId[p.Id] = "FORCE_CLOSE:" + (string.IsNullOrWhiteSpace(reason) ? "NA" : reason);
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


        // ============================================================
        // 評価基盤ログ（ENTRY_CORE / CLOSE_CORE）
        // ============================================================

        private void PrintEntryCore(Position pos)
        {
            try
            {
                if (pos == null) return;
                if (!string.Equals(pos.Label, BOT_LABEL, StringComparison.Ordinal)) return;

                string sl = pos.StopLoss.HasValue ? pos.StopLoss.Value.ToString("F5", CultureInfo.InvariantCulture) : "null";
                string tp = pos.TakeProfit.HasValue ? pos.TakeProfit.Value.ToString("F5", CultureInfo.InvariantCulture) : "null";

                Print(
                    "ENTRY_CORE | CodeName={0} | PosId={1} | Side={2} | Entry={3} | VolUnits={4} | Sym={5} | SL={6} | TP={7}{8}",
                    CODE_NAME,
                    pos.Id,
                    pos.TradeType,
                    pos.EntryPrice.ToString("F5", CultureInfo.InvariantCulture),
                    pos.VolumeInUnits,
                    pos.SymbolName,
                    sl,
                    tp,
                    BuildTimeTag(UtcNow())
                );
            }
            catch
            {
                // 評価ログ失敗で取引を止めない
            }
        }

        private void PrintCloseCore(Position pos, string closeReason, double closePrice)
        {
            try
            {
                if (pos == null) return;
                if (!string.Equals(pos.Label, BOT_LABEL, StringComparison.Ordinal)) return;

                string sl = pos.StopLoss.HasValue ? pos.StopLoss.Value.ToString("F5", CultureInfo.InvariantCulture) : "null";
                string tp = pos.TakeProfit.HasValue ? pos.TakeProfit.Value.ToString("F5", CultureInfo.InvariantCulture) : "null";

                string closePx = double.IsNaN(closePrice) || double.IsInfinity(closePrice)
                    ? "null"
                    : closePrice.ToString("F5", CultureInfo.InvariantCulture);

                Print(
                    "CLOSE_CORE | CodeName={0} | PosId={1} | CloseReason={2} | ClosePrice={3} | Sym={4} | SL={5} | TP={6}{7}",
                    CODE_NAME,
                    pos.Id,
                    string.IsNullOrWhiteSpace(closeReason) ? "NA" : closeReason,
                    closePx,
                    pos.SymbolName,
                    sl,
                    tp,
                    BuildTimeTag(UtcNow())
                );
            }
            catch
            {
                // 評価ログ失敗で取引を止めない
            }
        }

        private double PipsToPrice(double pips)
        {
            return pips * Symbol.PipSize;
        }

        private string NormalizeCloseReason(PositionCloseReason reason, string initiator)
        {
            // ForceFlat / ForceClose / MarketClose を優先（評価基盤：CloseReasonの正規化）
            if (!string.IsNullOrWhiteSpace(initiator) && initiator.StartsWith("FORCE_CLOSE:", StringComparison.Ordinal))
            {
                string r = initiator.Substring("FORCE_CLOSE:".Length);

                if (string.Equals(r, "MARKET_CLOSE", StringComparison.Ordinal))
                    return "MarketClose";

                // 時間系強制を含め、FORCE_FLAT で始まるものは ForceFlat に統一
                if (r.StartsWith("FORCE_FLAT", StringComparison.Ordinal))
                    return "ForceFlat";

                return "ForceClose";
            }

            switch (reason)
            {
                case PositionCloseReason.StopLoss:
                    return "SL";
                case PositionCloseReason.TakeProfit:
                    return "TP";
                case PositionCloseReason.Closed:
                    return "Closed";
                default:
                    return reason.ToString();
            }
        }


        private double TryGetClosePriceFromDeals(Position p)
        {
            try
            {
                if (p == null || p.Deals == null || p.Deals.Count == 0)
                    return double.NaN;

                Deal lastClosing = null;

                for (int i = 0; i < p.Deals.Count; i++)
                {
                    Deal d = p.Deals[i];
                    if (d == null) continue;
                    if (!d.ExecutionPrice.HasValue) continue;

                    // Closing deal を優先（なければ最後のExecutionPriceを採用）
                    if (d.PositionImpact.HasValue && d.PositionImpact.Value == DealPositionImpact.Closing)
                        lastClosing = d;
                    else if (lastClosing == null)
                        lastClosing = d;
                }

                if (lastClosing == null || !lastClosing.ExecutionPrice.HasValue)
                    return double.NaN;

                return lastClosing.ExecutionPrice.Value;
            }
            catch
            {
                return double.NaN;
            }
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


        // ============================
        // Instance / Mode Guards
        // ============================
        private void ValidateNewsModeOrStop()
        {
            if (UseNewsBacktest2025 && UseNewsForwardFRED)
            {
                Print("NEWS_MODE_ERROR | CodeName={0} | Both UseNewsBacktest2025 and UseNewsForwardFRED are enabled. Stopping cBot to avoid mixed mode.",
                    CODE_NAME);
                Stop();
            }
        }

        private void PrintInstanceStamp()
        {
            DateTime utcNow = UtcNow();
            string envMode = DetectEnvironmentMode();
            string tradeWindow = string.Format(CultureInfo.InvariantCulture, "{0}-{1} ForceFlat={2}", TradeStartTimeJst, TradeEndTimeJst, ForceFlatTimeJst);

            Print(
                "INSTANCE | CodeName={0} | Label={1} | Symbol={2} | TF={3} | AccountNo={4} | Mode={5} | NewsBacktest2025={6} NewsForwardFRED={7} | Window(JST)={8}{9}",
                CODE_NAME,
                BOT_LABEL,
                SymbolName,
                TimeFrame,
                SafeAccountNumber(),
                envMode,
                UseNewsBacktest2025 ? "ON" : "OFF",
                UseNewsForwardFRED ? "ON" : "OFF",
                tradeWindow,
                BuildTimeTag(utcNow)
            );
        }

        // ============================================================
        // PARAM SNAPSHOT (OnStart one-shot) - for backtest reproducibility
        // ============================================================
        private void PrintParameterSnapshot()
        {
            try
            {
                DateTime utcNow = UtcNow();
                DateTime jstNow = ToJst(utcNow);

                var root = new Dictionary<string, object>();
                root["schema"] = "PARAM_SNAPSHOT_v1";
                root["code_name"] = CODE_NAME;
                root["bot_label"] = BOT_LABEL;
                root["symbol"] = SymbolName;
                root["timeframe"] = TimeFrame.ToString();
                root["utc"] = utcNow.ToString("o", CultureInfo.InvariantCulture);
                root["jst"] = jstNow.ToString("o", CultureInfo.InvariantCulture);

                var list = new List<object>();
                var props = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var p in props)
                {
                    var attrs = p.GetCustomAttributes(typeof(ParameterAttribute), true);
                    if (attrs == null || attrs.Length == 0)
                        continue;

                    var a = (ParameterAttribute)attrs[0];

                    object val = null;
                    try
                    {
                        val = p.GetValue(this, null);
                    }
                    catch
                    {
                        // ignore
                    }

                    var item = new Dictionary<string, object>();
                    item["prop"] = p.Name;
                    item["name"] = a.Name;
                    item["group"] = a.Group;
                    item["type"] = p.PropertyType.Name;

                    // keep value as string for stable log parsing across types
                    item["value"] = val == null ? null : val.ToString();

                    list.Add(item);

                    // For PRO report (readable table)
                    _paramSnapshotEntries.Add(new ParamSnapshotEntry
                    {
                        PropertyName = p.Name,
                        DisplayName = a.Name,
                        GroupName = a.Group,
                        TypeName = p.PropertyType.Name,
                        ValueText = item["value"] == null ? "" : item["value"].ToString()
                    });
                }

                root["params"] = list;

                string json = SimpleJson(root);

                // cache for PRO report (HTML/embedded JSON)
                _paramSnapshotJson = json;
                _paramSnapshotEntries.Sort((a, b) =>
                {
                    int g = string.Compare(a.GroupName ?? "", b.GroupName ?? "", StringComparison.Ordinal);
                    if (g != 0) return g;
                    return string.Compare(a.DisplayName ?? "", b.DisplayName ?? "", StringComparison.Ordinal);
                });

                Print("PARAM_SNAPSHOT_BEGIN | CodeName={0} | Label={1}", CODE_NAME, BOT_LABEL);
                Print("PARAM_JSON={0}", json);
                Print("PARAM_SNAPSHOT_END | CodeName={0} | Label={1}", CODE_NAME, BOT_LABEL);
            }
            catch (Exception ex)
            {
                Print("PARAM_SNAPSHOT_ERROR | CodeName={0} | Label={1} | {2}", CODE_NAME, BOT_LABEL, ex.Message);
            }
        }


        private string DetectEnvironmentMode()
        {
            try
            {
                var prop = GetType().GetProperty("IsBacktesting");
                if (prop != null && prop.PropertyType == typeof(bool))
                {
                    bool isBt = (bool)prop.GetValue(this, null);
                    return isBt ? "BACKTEST" : "FORWARD";
                }
            }
            catch
            {
                // ignore
            }

            // Fallback: infer from configured news mode (for diagnostics only)
            if (UseNewsBacktest2025 && !UseNewsForwardFRED) return "BACKTEST_PARAM";
            if (!UseNewsBacktest2025 && UseNewsForwardFRED) return "FORWARD_PARAM";
            if (!UseNewsBacktest2025 && !UseNewsForwardFRED) return "MODELESS";
            return "MIXED_PARAM";
        }

        private string SafeAccountNumber()
        {
            try
            {
                return Account != null ? Account.Number.ToString(CultureInfo.InvariantCulture) : "NA";
            }
            catch
            {
                return "NA";
            }
        }

        // ============================
        // Broker-friendly throttling for Modify/Cancel
        // ============================
        private bool TryModifyPositionOncePerBar(Position p, double? stopLossPrice, double? takeProfitPrice, string action)
        {
            if (p == null)
                return false;

            // Guard: do not touch positions outside "AllowNewEntries" when window filter is enabled.
            if (EnableTradingWindowFilter)
            {
                TradingWindowState state = GetTradingWindowState(ToJst(UtcNow()));
                if (state != TradingWindowState.AllowNewEntries)
                    return false;
            }

            // Validate protection levels against current prices to avoid server-side errors.
            if (!IsValidProtectionLevels(p.TradeType, stopLossPrice, takeProfitPrice))
                return false;

            long barTicks = 0;
            try
            {
                if (Bars != null)
                    barTicks = Bars.OpenTimes.LastValue.Ticks;
            }
            catch
            {
                barTicks = 0;
            }

            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", p.Id, barTicks, action ?? "MOD");
            if (_oncePerBarActionGuard.Contains(key))
                return false;

            _oncePerBarActionGuard.Add(key);

            var res = ModifyPosition(p, stopLossPrice, takeProfitPrice, ProtectionType.Absolute);
            return res != null && res.IsSuccessful;
        }


        // ============================================================
        // Stage2 Exit: Close helper (once per bar)
        // ============================================================
        private bool TryClosePositionOncePerBar(Position p, string actionTag, string logLine)
        {
            if (p == null)
                return false;

            long barTicks = Bars != null && Bars.Count > 0 ? Bars.OpenTimes.LastValue.Ticks : 0;
            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", p.Id, barTicks, actionTag ?? "CLOSE");
            if (_oncePerBarActionGuard.Contains(key))
                return false;

            _oncePerBarActionGuard.Add(key);

            // Log first (so even if close fails we can see attempt)
            if (!string.IsNullOrWhiteSpace(logLine))
                Print(logLine);

            var res = ClosePosition(p);
            return res != null && res.IsSuccessful;
        }

        // ============================================================
        // Stage2 Exit: ATH 判定（D1 終値の過去最高更新）
        // ============================================================
        private bool IsAthModeByDailyCloseBreakout(out double lastClosedDailyClose, out DateTime lastClosedDailyTimeUtc)
        {
            lastClosedDailyClose = double.NaN;
            lastClosedDailyTimeUtc = DateTime.MinValue;

            if (_barsD1 == null || _barsD1.Count < 3)
                return false;

            int lastClosedIndex = _barsD1.Count - 2; // last closed bar
            lastClosedDailyClose = _barsD1.ClosePrices[lastClosedIndex];
            lastClosedDailyTimeUtc = _barsD1.OpenTimes[lastClosedIndex];

            double maxPrev = double.MinValue;
            for (int i = 0; i <= lastClosedIndex - 1; i++)
            {
                double c = _barsD1.ClosePrices[i];
                if (c > maxPrev) maxPrev = c;
            }

            return lastClosedDailyClose > maxPrev + (Symbol.PipSize * 0.0); // no epsilon
        }

        // ============================================================
        // Stage2 Exit: 100ドルラウンドナンバー（ファーストタッチ）
        // ============================================================
        private bool TryGetNextRoundNumberLevel100(TradeType type, out int level)
        {
            level = 0;

            double px = type == TradeType.Buy ? Symbol.Bid : Symbol.Ask;
            if (double.IsNaN(px) || px <= 0)
                return false;

            if (type == TradeType.Buy)
            {
                level = ((int)Math.Floor(px / 100.0) + 1) * 100;
                return true;
            }
            else
            {
                level = ((int)Math.Ceiling(px / 100.0) - 1) * 100;
                return true;
            }
        }

        private bool IsRoundNumberFirstTouchAllowed(int level)
        {
            return !_athRoundFirstTouchedLevels.Contains(level);
        }

        private void MarkRoundNumberFirstTouched(int level)
        {
            if (!_athRoundFirstTouchedLevels.Contains(level))
                _athRoundFirstTouchedLevels.Add(level);
        }

        // ============================================================
        // Stage2 Exit: M15 EMA20 タッチ
        // ============================================================

        // ============================================================
        // Stage2 Exit: M15 フラクタル（スイング割れ）  ※確定足の終値で判定
        // ============================================================

        private int GetM15IndexAtTime(DateTime utcTime)
        {
            if (_barsM15 == null || _barsM15.Count < 2)
                return -1;

            // Find the bar index whose OpenTime <= utcTime < next OpenTime
            // (cTrader Bars.OpenTimes is UTC)
            int last = _barsM15.Count - 1;
            for (int i = last; i >= 0; i--)
            {
                DateTime ot = _barsM15.OpenTimes[i];
                if (ot <= utcTime)
                    return i;
            }
            return 0;
        }

        private void UpdateStage2SwingLevelsIfNewM15ClosedBar(DateTime utcNow)
        {
            if (_barsM15 == null)
                return;

            int count = _barsM15.Count;
            if (count < (Stage2ExitFractalLR * 2 + 3))
                return;

            // last closed bar index (the last bar is forming)
            int lastClosedIndex = count - 2;
            if (lastClosedIndex <= 0)
                return;

            // process only once per newly closed M15 bar
            if (lastClosedIndex == _lastProcessedM15ClosedIndexForS2)
                return;

            _lastProcessedM15ClosedIndexForS2 = lastClosedIndex;

            // Confirm one new pivot when a new bar closes.
            int lr = Math.Max(1, Stage2ExitFractalLR);
            int pivotIndex = lastClosedIndex - lr;

            if (pivotIndex - lr < 0)
                return;

            // Ensure right-side bars are closed (up to lastClosedIndex)
            if (pivotIndex + lr > lastClosedIndex)
                return;

            bool isPivotLow = true;
            bool isPivotHigh = true;

            double pivotLow = _barsM15.LowPrices[pivotIndex];
            double pivotHigh = _barsM15.HighPrices[pivotIndex];

            for (int k = pivotIndex - lr; k <= pivotIndex + lr; k++)
            {
                if (k == pivotIndex) continue;

                if (_barsM15.LowPrices[k] <= pivotLow) isPivotLow = false;
                if (_barsM15.HighPrices[k] >= pivotHigh) isPivotHigh = false;

                if (!isPivotLow && !isPivotHigh) break;
            }

            if (!isPivotLow && !isPivotHigh)
                return;

            // Update swing level only for Stage2-active positions that started before this pivot was formed.
            foreach (var kv in _s2StartM15IndexByPosId)
            {
                long posId = kv.Key;
                int s2StartIndex = kv.Value;

                if (s2StartIndex < 0)
                    continue;

                // Pivot must be formed after Stage2 started (or at least not before it)
                if (pivotIndex < s2StartIndex)
                    continue;

                // Need the position object to know side; if not found, skip (will be cleaned on close)
                Position pFound = null;
                foreach (var pp in Positions)
                {
                    if (pp != null && pp.Id == posId && pp.Label == BOT_LABEL && pp.SymbolName == SymbolName)
                    {
                        pFound = pp;
                        break;
                    }
                }
                if (pFound == null)
                    continue;

                if (pFound.TradeType == TradeType.Buy && isPivotLow)
                {
                    double prevSwing;
                    bool hadPrev = _s2SwingLevelByPosId.TryGetValue(posId, out prevSwing);

                    _s2SwingLevelByPosId[posId] = pivotLow;

                    if (!hadPrev || Math.Abs(prevSwing - pivotLow) > (Symbol.TickSize * 0.5))
                    {
                        DateTime pivotBarJst = ToJst(_barsM15.OpenTimes[pivotIndex]);
                        DateTime nowJst = ToJst(Server.TimeInUtc);

                        string logLine = string.Format(CultureInfo.InvariantCulture,
                            "S2_SWING_SET | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | TF=M15 | LR={5} | Swing={6} | PivotBarJst={7:yyyy-MM-dd HH:mm:ss} | NowJst={8:yyyy-MM-dd HH:mm:ss}",
                            CODE_NAME, BOT_LABEL, SymbolName, pFound.TradeType, posId,
                            lr,
                            pivotLow.ToString("F2", CultureInfo.InvariantCulture),
                            pivotBarJst,
                            nowJst);

                        Print(logLine);
                    }
                }
                else if (pFound.TradeType == TradeType.Sell && isPivotHigh)
                {
                    double prevSwing;
                    bool hadPrev = _s2SwingLevelByPosId.TryGetValue(posId, out prevSwing);

                    _s2SwingLevelByPosId[posId] = pivotHigh;

                    if (!hadPrev || Math.Abs(prevSwing - pivotHigh) > (Symbol.TickSize * 0.5))
                    {
                        DateTime pivotBarJst = ToJst(_barsM15.OpenTimes[pivotIndex]);
                        DateTime nowJst = ToJst(Server.TimeInUtc);

                        string logLine = string.Format(CultureInfo.InvariantCulture,
                            "S2_SWING_SET | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | TF=M15 | LR={5} | Swing={6} | PivotBarJst={7:yyyy-MM-dd HH:mm:ss} | NowJst={8:yyyy-MM-dd HH:mm:ss}",
                            CODE_NAME, BOT_LABEL, SymbolName, pFound.TradeType, posId,
                            lr,
                            pivotHigh.ToString("F2", CultureInfo.InvariantCulture),
                            pivotBarJst,
                            nowJst);

                        Print(logLine);
                    }
                }
            }
        }

        private bool TryStage2SwingBreakExit(Position p, DateTime utcNow)
        {
            if (p == null)
                return false;

            if (!EnableStructureTpBoostStage2)
                return false;

            // Stage2 only (Single Source of Truth)
            if (!_structureTpBoostedPosIdsStage2.Contains(p.Id))
                return false;

            if (_barsM15 == null || _barsM15.Count < 3)
                return false;

            // Make sure swing levels are updated on new closed M15 bar.
            UpdateStage2SwingLevelsIfNewM15ClosedBar(utcNow);

            double swing;
            if (!_s2SwingLevelByPosId.TryGetValue(p.Id, out swing))
                return false;

            if (!Stage2ExitUseCloseConfirm)
                return false; // this version is specified to use Close-confirm only

            int lastClosedIndex = _barsM15.Count - 2;
            if (lastClosedIndex <= 0)
                return false;

            double close = _barsM15.ClosePrices[lastClosedIndex];

            bool breakDown = (p.TradeType == TradeType.Buy) && (close < swing);
            bool breakUp = (p.TradeType == TradeType.Sell) && (close > swing);

            if (!breakDown && !breakUp)
                return false;

            string logLine = string.Format(CultureInfo.InvariantCulture,
                "S2_EXIT_SWING_BREAK | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | LR={5} | Swing={6} | Close={7}{8}",
                CODE_NAME, BOT_LABEL, SymbolName, p.TradeType, p.Id,
                Math.Max(1, Stage2ExitFractalLR),
                swing.ToString("F2", CultureInfo.InvariantCulture),
                close.ToString("F2", CultureInfo.InvariantCulture),
                BuildTimeTag(utcNow));

            _closeInitiatorByPosId[p.Id] = "S2_EXIT_SWING_BREAK";
            TryClosePositionOncePerBar(p, "S2_EXIT_SWING_BREAK", logLine);
            return true;
        }


        // ============================================================
        // Analysis helpers (Stage1/Stage2 observability)
        // ============================================================
        private bool HasOpenBotPositions()
        {
            foreach (var p in Positions)
            {
                if (p == null) continue;
                if (p.SymbolName != SymbolName) continue;
                if (p.Label != BOT_LABEL) continue;
                return true;
            }
            return false;
        }

        private void PrintSkipByGates(string gate, string detail, DateTime utcNow)
        {
            // Throttle to avoid log flood
            if ((utcNow - _lastSkipByGatesLogUtc).TotalMinutes < SKIP_BY_GATES_LOG_INTERVAL_MINUTES)
                return;

            _lastSkipByGatesLogUtc = utcNow;

            Print("STRUCT_TP_SKIP_BY_GATES | CodeName={0} | Label={1} | Symbol={2} | Gate={3} | Detail={4}{5}",
                CODE_NAME,
                BOT_LABEL,
                SymbolName,
                gate ?? "-",
                detail ?? "-",
                BuildTimeTag(utcNow));
        }

        private bool TryPrintOncePerBar(long positionId, string actionKey, string message)
        {
            long barTicks = 0;
            try
            {
                if (Bars != null)
                    barTicks = Bars.OpenTimes.LastValue.Ticks;
            }
            catch
            {
                barTicks = 0;
            }

            string key = string.Format(CultureInfo.InvariantCulture, "LOG|{0}|{1}|{2}", positionId, barTicks, actionKey ?? "LOG");
            if (_oncePerBarActionGuard.Contains(key))
                return false;

            _oncePerBarActionGuard.Add(key);
            Print(message);
            return true;
        }

        private string BuildTpDiag(Position p, double tpAbs)
        {
            if (p == null)
                return " | TpNow=- | TpCalc=- | TpDiffPips=-";

            double? tpNow = p.TakeProfit;
            double tpNowAbs = tpNow.HasValue ? tpNow.Value : double.NaN;

            // Diff in pips (signed, positive means tpCalc is further in profit direction)
            double diffPips;
            if (double.IsNaN(tpNowAbs))
                diffPips = double.NaN;
            else
                diffPips = (tpAbs - tpNowAbs) / Symbol.PipSize;

            string sTpNow = tpNow.HasValue ? tpNowAbs.ToString("F5", CultureInfo.InvariantCulture) : "-";
            string sTpCalc = tpAbs.ToString("F5", CultureInfo.InvariantCulture);
            string sDiff = double.IsNaN(diffPips) ? "-" : diffPips.ToString("F1", CultureInfo.InvariantCulture);

            return string.Format(CultureInfo.InvariantCulture, " | TpNow={0} | TpCalc={1} | TpDiffPips={2}", sTpNow, sTpCalc, sDiff);
        }

        private bool IsValidProtectionLevels(TradeType type, double? sl, double? tp)
        {
            double ask = Symbol.Ask;
            double bid = Symbol.Bid;

            if (type == TradeType.Buy)
            {
                if (sl.HasValue && sl.Value >= bid)
                    return false;
                if (tp.HasValue && tp.Value <= ask)
                    return false;
            }
            else
            {
                if (sl.HasValue && sl.Value <= ask)
                    return false;
                if (tp.HasValue && tp.Value >= bid)
                    return false;
            }

            return true;
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

            _structureTpBoostedPosIds.Remove(p.Id);
            _structureTpBoostedPosIdsStage2.Remove(p.Id);
            double lots = Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits);

            string initiator;
            if (!_closeInitiatorByPosId.TryGetValue(p.Id, out initiator))
                initiator = "NA";
            else
                _closeInitiatorByPosId.Remove(p.Id);

            string closeReason = NormalizeCloseReason(args.Reason, initiator);
            double closePrice = TryGetClosePriceFromDeals(p);

            // 評価基盤ログ：クローズ確定（SL/TPは置いた価格を記録）
            PrintCloseCore(p, closeReason, closePrice);

            Print(
                "CLOSE | CodeName={0} | PosId={1} | Type={2} | Lots={3} | Gross={4} | Net={5} | Pips={6} | CloseReason={7} | Initiator={8}{9}",
                CODE_NAME,
                p.Id,
                p.TradeType,
                lots.ToString("F2", CultureInfo.InvariantCulture),
                p.GrossProfit.ToString("F2", CultureInfo.InvariantCulture),
                p.NetProfit.ToString("F2", CultureInfo.InvariantCulture),
                p.Pips.ToString("F1", CultureInfo.InvariantCulture),
                closeReason,
                initiator,
                BuildTimeTag(UtcNow())
            );
        }

        // ============================================================
        // PROレポート（分析フェーズ：PRO）出力
        // ============================================================

        private void OnPositionsClosedForProReport(PositionClosedEventArgs args)
        {
            try
            {
                var p = args.Position;
                if (p == null)
                    return;

                // 集計対象：BotLabel一致のみ（仕様：A）
                if (!string.Equals(p.Label, BOT_LABEL, StringComparison.Ordinal))
                    return;

                _proClosedTrades.Add(new ProClosedTrade
                {
                    CloseTimeUtc = UtcNow(),
                    NetProfit = p.NetProfit,
                    SymbolName = p.SymbolName
                });
            }
            catch
            {
                // 収集失敗はレポートに影響しうるが、取引ロジックを止めない
            }
        }

        private void ExportProReportHtmlOnce()
        {
            // 1テスト=1ファイル保証：OnStopが複数回呼ばれても、正常PROは1回だけ出す
            if (_proReportWritten)
            {
                Print("PRO_REPORT_ALREADY_WRITTEN | CodeName={0} | BotLabel={1}", CODE_NAME, BOT_LABEL);
                return;
            }

            bool saved = false;
            string savedPath = null;

            try
            {
                saved = ExportProReportHtml(out savedPath);
            }
            catch (Exception ex)
            {
                Print("PRO_REPORT_ERROR | CodeName={0} | BotLabel={1} | Msg={2}", CODE_NAME, BOT_LABEL, ex.Message);
                return;
            }

            if (saved)
            {
                _proReportWritten = true;
                _proReportLastSavedPath = savedPath;
            }
        }

        private bool ExportProReportHtml(out string savedPath)
        {
            savedPath = null;

            // 仕様：テスト期間は「最初～最後のクローズ」（出せない場合は [ERR]）
            DateTime? fromUtc = null;
            DateTime? toUtc = null;

            foreach (var t in _proClosedTrades)
            {
                if (fromUtc == null || t.CloseTimeUtc < fromUtc.Value)
                    fromUtc = t.CloseTimeUtc;
                if (toUtc == null || t.CloseTimeUtc > toUtc.Value)
                    toUtc = t.CloseTimeUtc;
            }

            var endingBalance = Account.Balance;
            var endingEquity = Account.Equity;

            double netProfit = 0.0;
            double grossProfit = 0.0;
            double grossLossAbs = 0.0;

            int wins = 0;
            int losses = 0;
            int totalTrades = _proClosedTrades.Count;
            // 有効性チェック：ゴミPRO（0 trades / [ERR]）は出力しない（探索・集計汚染防止）
            if (totalTrades <= 0)
            {
                Print("PRO_REPORT_SKIPPED_INVALID | CodeName={0} | BotLabel={1} | Reason=TotalTradesZero", CODE_NAME, BOT_LABEL);
                return false;
            }


            // セッション別
            double tokyoPnl = 0.0;
            double europePnl = 0.0;
            double nyPnl = 0.0;
            int tokyoCount = 0;
            int europeCount = 0;
            int nyCount = 0;

            // 時系列ソート（クローズ時刻）
            var sorted = new List<ProClosedTrade>(_proClosedTrades);
            sorted.Sort((a, b) => a.CloseTimeUtc.CompareTo(b.CloseTimeUtc));

            // ピーク・DD計算（Balance曲線：初期残高 + 累積純損益）
            double balance = _proInitialBalance;
            double peakBalance = _proInitialBalance;
            DateTime? peakReachedUtc = null;

            double ddMax = 0.0;
            double ddPeakBalance = _proInitialBalance;
            DateTime? ddPeakUtc = null;
            double ddBottomBalance = _proInitialBalance;
            DateTime? ddBottomUtc = null;

            double runningPeak = _proInitialBalance;
            DateTime? runningPeakUtc = null;

            foreach (var t in sorted)
            {
                netProfit += t.NetProfit;
                if (t.NetProfit >= 0)
                {
                    grossProfit += t.NetProfit;
                    wins++;
                }
                else
                {
                    grossLossAbs += Math.Abs(t.NetProfit);
                    losses++;
                }

                var session = GetProSession(ToJst(t.CloseTimeUtc));
                switch (session)
                {
                    case ProSession.Tokyo:
                        tokyoPnl += t.NetProfit;
                        tokyoCount++;
                        break;
                    case ProSession.Europe:
                        europePnl += t.NetProfit;
                        europeCount++;
                        break;
                    case ProSession.NewYork:
                        nyPnl += t.NetProfit;
                        nyCount++;
                        break;
                }

                // Balance曲線更新（クローズ時点）
                balance = _proInitialBalance + netProfit;

                // ピーク
                if (balance > peakBalance)
                {
                    peakBalance = balance;
                    peakReachedUtc = t.CloseTimeUtc;
                }

                // DD（Balance DD）
                if (balance > runningPeak)
                {
                    runningPeak = balance;
                    runningPeakUtc = t.CloseTimeUtc;
                }
                else
                {
                    var dd = runningPeak - balance;
                    if (dd > ddMax)
                    {
                        ddMax = dd;
                        ddPeakBalance = runningPeak;
                        ddPeakUtc = runningPeakUtc;
                        ddBottomBalance = balance;
                        ddBottomUtc = t.CloseTimeUtc;
                    }
                }
            }

            double roi = 0.0;
            if (_proInitialBalance > 0)
                roi = (endingBalance - _proInitialBalance) / _proInitialBalance * 100.0;

            double winRate = 0.0;
            if (totalTrades > 0)
                winRate = (double)wins / totalTrades * 100.0;

            string pfText = "[ERR]";
            double pf = double.NaN;
            if (grossLossAbs > 0)
            {
                pf = grossProfit / grossLossAbs;
                pfText = pf.ToString("0.00", CultureInfo.InvariantCulture);
            }
            else if (grossProfit > 0 && grossLossAbs == 0)
            {
                // 全勝ケース
                pfText = "INF";
            }
            else if (grossProfit == 0 && grossLossAbs == 0)
            {
                pfText = "[ERR]";
            }

            // 整合チェック
            double sessionSum = tokyoPnl + europePnl + nyPnl;
            double sessionDiff = sessionSum - netProfit;

            int countSum = wins + losses;
            int countDiff = countSum - totalTrades;

            string sessionCheck = Math.Abs(sessionDiff) < 0.0000001 ? "OK" : "NG";
            string countCheck = countDiff == 0 ? "OK" : "NG";

            // テスト期間（JST表示）
            string fromText;
            string toText;
            string periodText;

            if (fromUtc == null || toUtc == null)
            {
                fromText = "[ERR]";
                toText = "[ERR]";
                periodText = "[ERR]";

                // テスト期間が確定できない場合は無効として出力しない
                Print("PRO_REPORT_SKIPPED_INVALID | CodeName={0} | BotLabel={1} | Reason=PeriodErr", CODE_NAME, BOT_LABEL);
                return false;
            }
            else
            {
                var fromJst = ToJst(fromUtc.Value);
                var toJst = ToJst(toUtc.Value);
                fromText = fromJst.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " JST";
                toText = toJst.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " JST";
                var span = toJst - fromJst;
                periodText = FormatSpan(span);
            }

            // クリティカルポイント（JST）
            double peakProfit = peakBalance - _proInitialBalance;
            string peakReachedText = peakReachedUtc == null ? "[ERR]" : ToJst(peakReachedUtc.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " JST";
            double peakToFinal = endingBalance - peakBalance;

            string ddPeakTimeText = ddPeakUtc == null ? "[ERR]" : ToJst(ddPeakUtc.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " JST";
            string ddBottomTimeText = ddBottomUtc == null ? "[ERR]" : ToJst(ddBottomUtc.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " JST";

            // EA名・銘柄
            string eaName = CODE_NAME;
            string symbolName = SymbolName;

            // 口座資産名（warning対策：Account.Asset.Name）
            string accountAsset = null;
            try
            {
                accountAsset = Account.Asset?.Name;
            }
            catch
            {
                accountAsset = null;
            }

            // JSON（同梱用）
            var jsonObj = new Dictionary<string, object>
            {
                ["SchemaVersion"] = 1,
                ["Spec"] = "PRO_REPORT_SPEC_v1",
                ["EA"] = eaName,
                ["Symbol"] = symbolName,
                ["AccountAsset"] = accountAsset ?? "",
                ["PeriodFromJst"] = fromText,
                ["PeriodToJst"] = toText,
                ["PeriodSpan"] = periodText,
                ["InitialBalance"] = _proInitialBalance,
                ["EndingBalance"] = endingBalance,
                ["EndingEquity"] = endingEquity,
                ["NetProfit"] = netProfit,
                ["ROI"] = roi,
                ["TotalTrades"] = totalTrades,
                ["Wins"] = wins,
                ["Losses"] = losses,
                ["WinRate"] = winRate,
                ["ProfitFactor"] = double.IsNaN(pf) ? (object)pfText : pf,
                ["Session"] = new Dictionary<string, object>
                {
                    ["Tokyo"] = new Dictionary<string, object> { ["Pnl"] = tokyoPnl, ["Count"] = tokyoCount },
                    ["Europe"] = new Dictionary<string, object> { ["Pnl"] = europePnl, ["Count"] = europeCount },
                    ["NewYork"] = new Dictionary<string, object> { ["Pnl"] = nyPnl, ["Count"] = nyCount }
                },
                ["Checks"] = new Dictionary<string, object>
                {
                    ["SessionSum"] = new Dictionary<string, object> { ["Result"] = sessionCheck, ["Diff"] = sessionDiff },
                    ["CountSum"] = new Dictionary<string, object> { ["Result"] = countCheck, ["Diff"] = countDiff }
                },
                ["Critical"] = new Dictionary<string, object>
                {
                    ["PeakBalance"] = peakBalance,
                    ["PeakProfit"] = peakProfit,
                    ["PeakReachedJst"] = peakReachedText,
                    ["PeakToFinal"] = peakToFinal,
                    ["MaxBalanceDD"] = ddMax,
                    ["DdPeakBalance"] = ddPeakBalance,
                    ["DdPeakTimeJst"] = ddPeakTimeText,
                    ["DdBottomBalance"] = ddBottomBalance,
                    ["DdBottomTimeJst"] = ddBottomTimeText,
                    ["DdCheck"] = ddPeakBalance - ddBottomBalance
                }
            };


            // パラメーター（OnStart時点）を同梱
            if (!string.IsNullOrEmpty(_paramSnapshotJson) && _paramSnapshotEntries.Count > 0)
            {
                var plist = new List<Dictionary<string, object>>();
                foreach (var p in _paramSnapshotEntries)
                {
                    var pi = new Dictionary<string, object>
                    {
                        ["property"] = p.PropertyName,
                        ["name"] = p.DisplayName,
                        ["group"] = p.GroupName,
                        ["type"] = p.TypeName,
                        ["value"] = p.ValueText
                    };
                    plist.Add(pi);
                }
                jsonObj["parameters"] = plist;
            }
            else
            {
                jsonObj["parameters"] = new List<Dictionary<string, object>>();
            }

            string json = SimpleJson(jsonObj);

            // HTML生成
            var html = new System.Text.StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"ja\">");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"utf-8\">");
            html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            html.AppendLine("<title>PRO_REPORT - " + EscapeHtml(eaName) + "</title>");
            html.AppendLine("<style>body{font-family:Arial,Helvetica,sans-serif;line-height:1.4;margin:16px;}h2{margin-top:24px;}pre{background:#f6f6f6;padding:12px;overflow:auto;}table{border-collapse:collapse;}td,th{border:1px solid #ddd;padding:6px 8px;}</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("<h1>分析フェーズ：PRO（HTML＋結果）</h1>");
            // パラメーター（OnStart時点）
            html.AppendLine("<h2>【0】パラメーター（テスト実行時点）</h2>");
            html.AppendLine("<table>");
            html.AppendLine("<tr><th>表示名</th><th>グループ</th><th>型</th><th>値</th><th>内部名</th></tr>");
            if (_paramSnapshotEntries.Count == 0)
            {
                html.AppendLine("<tr><td colspan=\"5\">[ERR] パラメータースナップショットが取得できませんでした</td></tr>");
            }
            else
            {
                foreach (var p in _paramSnapshotEntries)
                {
                    html.AppendLine("<tr>"
                        + "<td>" + EscapeHtml(p.DisplayName) + "</td>"
                        + "<td>" + EscapeHtml(p.GroupName) + "</td>"
                        + "<td>" + EscapeHtml(p.TypeName) + "</td>"
                        + "<td>" + EscapeHtml(p.ValueText) + "</td>"
                        + "<td>" + EscapeHtml(p.PropertyName) + "</td>"
                        + "</tr>");
                }
            }
            html.AppendLine("</table>");
            html.AppendLine("<pre>※ parameters は HTML末尾の JSON（script#pro_report_json）にも同梱されています。</pre>");


            html.AppendLine("<h2>【1】概要ステータス（確定）</h2>");
            html.AppendLine("<pre>");
            html.AppendLine("- 対象EA：" + eaName);
            html.AppendLine("- 銘柄：" + symbolName);
            html.AppendLine("- テスト期間：" + fromText + " – " + toText + "（" + periodText + "）");
            html.AppendLine("- 初期残高：" + _proInitialBalance.ToString("0.00", CultureInfo.InvariantCulture));
            html.AppendLine("- 最終残高（Ending Balance）：" + endingBalance.ToString("0.00", CultureInfo.InvariantCulture));
            html.AppendLine("- 最終エクイティ（Ending Equity）：" + endingEquity.ToString("0.00", CultureInfo.InvariantCulture));
            html.AppendLine("- 純利益（確定）：" + netProfit.ToString("0.00", CultureInfo.InvariantCulture));
            html.AppendLine("- ROI（最終）：" + roi.ToString("0.00", CultureInfo.InvariantCulture) + "%");
            html.AppendLine("- 総トレード数：" + totalTrades);
            html.AppendLine("- 勝ち：" + wins + "／負け：" + losses + "（勝率：" + winRate.ToString("0.00", CultureInfo.InvariantCulture) + "%）");
            html.AppendLine("- Profit Factor（全体）：" + pfText);
            html.AppendLine("</pre>");

            html.AppendLine("<h2>【2】セッション別損益（history全件を再集計して確定）</h2>");
            html.AppendLine("<pre>");
            html.AppendLine("- 東京：" + tokyoPnl.ToString("0.00", CultureInfo.InvariantCulture) + "（" + tokyoCount + "件）");
            html.AppendLine("- 欧州：" + europePnl.ToString("0.00", CultureInfo.InvariantCulture) + "（" + europeCount + "件）");
            html.AppendLine("- NY ：" + nyPnl.ToString("0.00", CultureInfo.InvariantCulture) + "（" + nyCount + "件）");
            html.AppendLine("");
            html.AppendLine("整合チェック（PROで必須）");
            html.AppendLine("1) 東京+欧州+NY = 純利益（差分：" + sessionDiff.ToString("0.00", CultureInfo.InvariantCulture) + "） → " + sessionCheck);
            html.AppendLine("2) 勝ち+負け = 総トレード数（差分：" + countDiff + "） → " + countCheck);
            html.AppendLine("</pre>");

            html.AppendLine("<h2>【3】クリティカルポイント（日時＋金額で確定）</h2>");
            html.AppendLine("<pre>");
            html.AppendLine("3-1. ピーク利益（ピーク残高）");
            html.AppendLine("- ピーク残高：" + peakBalance.ToString("0.00", CultureInfo.InvariantCulture));
            html.AppendLine("- ピーク利益：" + peakProfit.ToString("0.00", CultureInfo.InvariantCulture) + "（対 初期残高）");
            html.AppendLine("- 到達日時（JST、該当トレードのクローズ時刻）：" + peakReachedText);
            html.AppendLine("- ピーク→最終差：" + peakToFinal.ToString("0.00", CultureInfo.InvariantCulture));
            html.AppendLine("");
            html.AppendLine("3-2. 最大ドローダウン（Balance DD）");
            html.AppendLine("- 最大DD：" + ddMax.ToString("0.00", CultureInfo.InvariantCulture));
            html.AppendLine("- ピーク時残高：" + ddPeakBalance.ToString("0.00", CultureInfo.InvariantCulture) + "（JST：" + ddPeakTimeText + "）");
            html.AppendLine("- ボトム時残高：" + ddBottomBalance.ToString("0.00", CultureInfo.InvariantCulture) + "（JST：" + ddBottomTimeText + "）");
            html.AppendLine("- DD検算：" + ddPeakBalance.ToString("0.00", CultureInfo.InvariantCulture) + "-" + ddBottomBalance.ToString("0.00", CultureInfo.InvariantCulture) + "=" + (ddPeakBalance - ddBottomBalance).ToString("0.00", CultureInfo.InvariantCulture));
            html.AppendLine("</pre>");

            html.AppendLine("<h2>JSON（同梱）</h2>");
            html.AppendLine("<script type=\"application/json\" id=\"pro_report_json\">");
            html.AppendLine(json);
            html.AppendLine("</script>");
            html.AppendLine("<pre>JSONは HTML内の script#pro_report_json に同梱されています。</pre>");

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            // 出力
            // 出力ファイル名（コード名と混同しない：PRO_RESULT_019_013_YYYYMMDD_HHMM.html）
            var jstNowForName = ToJst(DateTime.UtcNow);
            var stamp = jstNowForName.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
            var baseName = "PRO_RESULT_019_013_" + stamp;
            var fileName = baseName + ".html";
            string dir = ProReportOutputFolder;
            if (string.IsNullOrWhiteSpace(dir))
                dir = @"D:\保管庫";

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch
            {
                // フォールバック：MyDocuments\\cTrader\\Reports
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "cTrader", "Reports");
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch
                {
                    // これ以上はEA側で確定できないため、保存失敗として扱う
                }
            }

            var fullPath = Path.Combine(dir, fileName);

            // 衝突回避：同一分に複数テストを保存した場合でも上書きしない
            if (File.Exists(fullPath))
            {
                for (int n = 1; n < 1000; n++)
                {
                    var candidate = Path.Combine(dir, baseName + "_" + n.ToString("00", CultureInfo.InvariantCulture) + ".html");
                    if (!File.Exists(candidate))
                    {
                        fullPath = candidate;
                        break;
                    }
                }
            }

            try
            {
                File.WriteAllText(fullPath, html.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Print("PRO_REPORT_SAVE_FAILED | CodeName={0} | BotLabel={1} | Path={2} | Msg={3}", CODE_NAME, BOT_LABEL, fullPath, ex.Message);
                return false;
            }

            savedPath = fullPath;
            Print("PRO_REPORT_SAVED | CodeName={0} | BotLabel={1} | Path={2}", CODE_NAME, BOT_LABEL, fullPath);
            return true;
        }

        private ProSession GetProSession(DateTime jst)
        {
            // 仕様（JST / クローズ時刻で分類）
            // 東京：06:00–14:59
            // 欧州：15:00–20:59
            // NY ：21:00–05:59（跨ぎ）
            var t = jst.TimeOfDay;

            if (t >= new TimeSpan(6, 0, 0) && t <= new TimeSpan(14, 59, 59))
                return ProSession.Tokyo;

            if (t >= new TimeSpan(15, 0, 0) && t <= new TimeSpan(20, 59, 59))
                return ProSession.Europe;

            // NY（21:00–23:59:59）or（00:00–05:59:59）
            if (t >= new TimeSpan(21, 0, 0) || t <= new TimeSpan(5, 59, 59))
                return ProSession.NewYork;

            return ProSession.Unknown;
        }


        private string FormatSpan(TimeSpan span)
        {
            if (span.TotalSeconds < 0)
                return "[ERR]";

            int days = (int)span.TotalDays;
            int hours = span.Hours;
            int minutes = span.Minutes;

            if (days > 0)
                return days + "d " + hours + "h " + minutes + "m";

            if (hours > 0)
                return hours + "h " + minutes + "m";

            return minutes + "m";
        }

        private string EscapeHtml(string s)
        {
            if (s == null)
                return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
        }

        // 超軽量JSON（依存追加なし）
        private string SimpleJson(object obj)
        {
            if (obj == null)
                return "null";

            if (obj is string str)
                return "\"" + EscapeJson(str) + "\"";

            if (obj is bool b)
                return b ? "true" : "false";

            if (obj is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d))
                    return "\"" + d.ToString(CultureInfo.InvariantCulture) + "\"";
                return d.ToString("R", CultureInfo.InvariantCulture);
            }

            if (obj is int i)
                return i.ToString(CultureInfo.InvariantCulture);

            if (obj is long l)
                return l.ToString(CultureInfo.InvariantCulture);

            if (obj is Dictionary<string, object> dict)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("{");
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("\"" + EscapeJson(kv.Key) + "\":");
                    sb.Append(SimpleJson(kv.Value));
                }
                sb.Append("}");
                return sb.ToString();
            }

            if (obj is IEnumerable<object> list)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("[");
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(SimpleJson(item));
                }
                sb.Append("]");
                return sb.ToString();
            }

            // 数値系（decimal等）
            if (obj is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return "\"" + EscapeJson(obj.ToString()) + "\"";
        }

        private string EscapeJson(string s)
        {
            if (s == null)
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }


    }
}