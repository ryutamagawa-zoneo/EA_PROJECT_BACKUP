// ============================================================
// CODE NAME (Extract / Draw Only)
// ============================================================
// SOURCE: EA_HL_DEV_010_034
// THIS  : EA_HL_DRAW_EXTRACT_034_012
// SCOPE : HIGHLOW描画のみ（Env=H1固定 / Entry=M5固定）
// NOTE  : リペイント無し化のみ許可されたため、Pivot計算の終端を「確定足（Count-2）」に限定
// ============================================================
// FIX LOG (034_012):
//   #1  バージョン名を034_012に統一、旧prefix(009/010/011)をクリーン対象に追加
//   #2  同値許容幅: Low側同値をHL（有利側）に変更（High側HHと対称化）
//   #3  OnStart時にLoadMoreHistoryで過去バーを確保（最大5回リトライ）
//   #4  _hlRedrawPending競合修正 → Running/Queued二段フラグに変更
//   #5  _hlClearCallCount を long に変更（オーバーフロー防止）
//   #6  upEntry&&downEntry同時成立時の明示コメント追加（TrendLess維持）
//   #7  （#14で置換）
//   #8  HL時間足 enum にコメント追加
//   #9  RemoveObject空振り呼び出し削除
//   #10 HL_Pivot struct維持（コメント補足）
//   #11 OnBarでM5/H1バー未更新時の無駄な再描画をスキップ
//   #12 デッドコード削除、Server.Timeに関する注記追加
//   #13 TrendLess突入後は新構造のみで新トレンド遷移を許可（旧構造での即時遷移を防止）
//   #14 トレンド崩壊判定をダウ理論の押し安値/戻り高値に変更
//       押し安値 = 最新HHの直前Low側ピボット（UpTrend break基準）
//       戻り高値 = 最新LLの直前High側ピボット（DownTrend break基準）
//       HH後に新たにHLが出来ても押し安値は更新されない。次のHH形成で初めて切り上がる
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using cAlgo.API;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class EA_HL_DRAW_EXTRACT_034_012 : Robot
    {
        private const string CODE_NAME = "EA_HL_DRAW_EXTRACT_034_012";

        // ============================================================
        // HighLow関連（パラメータ：034から抜き出し）
        // ============================================================
        #region HighLow関連

        [Parameter("HL_計算対象バー", Group = "HighLow関連", DefaultValue = 3000, MinValue = 200)]
        public int HL_計算対象バー { get; set; }

        [Parameter("HL_ログ：毎バー出す", Group = "HighLow関連", DefaultValue = false)]
        public bool HL_ログ毎バー出す { get; set; }

        [Parameter("HL_EA内部Pivot描画", Group = "HighLow関連", DefaultValue = false)]
        public bool HL_EA内部Pivot描画 { get; set; }

        [Parameter("HL_強制クリーン頻度(0=しない/1=毎回)", Group = "HighLow関連", DefaultValue = 50, MinValue = 0)]
        public int HL_強制クリーン頻度 { get; set; }

        #endregion

        #region HLエントリー関連

        // (#8) enum値コメント: M5=1, H1=3 は将来の時間足追加を想定した予約値
        public enum HL時間足
        {
            M5 = 1,  // 5分足
            H1 = 3   // 1時間足（2は将来用に予約）
        }

        [Parameter("HLロジック稼働（はい / いいえ）", Group = "HLエントリー関連", DefaultValue = true)]
        public bool HL_EntryEnabled { get; set; }

        [Parameter("ログ出力（はい / いいえ）", Group = "HLエントリー関連", DefaultValue = true)]
        public bool HL_LogOutput { get; set; }

        [Parameter("同値とみなす許容幅（ピップス）", Group = "HLエントリー関連", DefaultValue = 0.0, MinValue = 0.0)]
        public double HL_EqualTolerancePips { get; set; }

        public enum 環境足トレンドレス時_許可設定
        {
            許可 = 0,
            禁止 = 1
        }

        [Parameter("環境足トレンドレス時：許可／禁止", Group = "HLエントリー関連", DefaultValue = 環境足トレンドレス時_許可設定.許可)]
        public 環境足トレンドレス時_許可設定 環境足トレンドレス時 { get; set; }

        [Parameter("環境足とエントリー足が反対なら禁止：はい／いいえ", Group = "HLエントリー関連", DefaultValue = true)]
        public bool 環境足とエントリー足反対禁止 { get; set; }

        #endregion

        // ============================================================
        // HL描画パラメータ（034と同値・変更禁止領域）
        // ============================================================
        private const int HL_ZZ_Depth = 12;
        private const double HL_Points = 50.0;
        private const int HL_Backstep = 3;
        private const double HL_SuppressPoints = 0.0;
        private const bool HL_同値右優先 = true;
        private const int MaxBars = 500;
        private const int HL_RightBars_M5 = 3;
        private const int HL_RightBars_H1 = 3;
        private const int HL_LabelFontSize = 9;
        private const int HL_LabelYOffsetTicks = 1;
        private bool EnableProReport { get { return true; } }

        // Draw object name prefix
        private const string HL_DrawObjectPrefix = CODE_NAME + ".";

        // (#1) 旧prefix削除対象（運用：直近バージョン差し替え時のゴミ掃除用）
        //      009/010 の Extract 版 prefix も追加
        private const string HL_OldDrawObjectPrefix_010_034 = "EA_HL_DEV_010_034.";
        private const string HL_OldDrawObjectPrefix_010_033 = "EA_HL_DEV_010_033.";
        private const string HL_OldDrawObjectPrefix_EXTRACT_009 = "EA_HL_DRAW_EXTRACT_034_009.";
        private const string HL_OldDrawObjectPrefix_EXTRACT_010 = "EA_HL_DRAW_EXTRACT_034_010.";
        private const string HL_OldDrawObjectPrefix_EXTRACT_011 = "EA_HL_DRAW_EXTRACT_034_011.";
        private static readonly string[] HL_OldDrawObjectPrefixes = new[]
        {
            HL_OldDrawObjectPrefix_010_034,
            HL_OldDrawObjectPrefix_010_033,
            HL_OldDrawObjectPrefix_EXTRACT_009,
            HL_OldDrawObjectPrefix_EXTRACT_010,
            HL_OldDrawObjectPrefix_EXTRACT_011
        };

        private const int HL_ForceCleanLogNameLimit = 10;

        // (#3) LoadMoreHistory リトライ上限
        private const int HL_LoadHistoryMaxRetries = 5;

        // ============================================================
        // データ構造（034から抜き出し）
        // ============================================================
        // (#10) HL_Pivot は struct を維持。Kind を後から書き換えるため
        //       pivots[i] = p; によるコピー代入が必要な点に注意。
        public struct HL_Pivot
        {
            public int Index;
            public DateTime Time;
            public double Price;
            public int Side;   // 1=High, -1=Low
            public string Kind; // H/L/HH/HL/LH/LL
        }

        private enum TrendState
        {
            TrendLess = 0,
            UpTrend = 1,
            DownTrend = 2
        }

        private enum TrendLessContext
        {
            None = 0,
            AfterDownEnd = 1,
            AfterUpEnd = 2
        }

        // ============================================================
        // 状態
        // ============================================================
        private Bars _barsM5;
        private Bars _barsH1;

        private DateTime _lastPivotCalcBarTimeM5 = DateTime.MinValue;
        private DateTime _lastPivotCalcBarTimeH1 = DateTime.MinValue;

        private List<HL_Pivot> _m5Pivots = new List<HL_Pivot>();
        private List<HL_Pivot> _h1Pivots = new List<HL_Pivot>();

        private readonly List<string> _hlDrawNames = new List<string>(4096);

        // (#4) Running/Queued 二段フラグ（旧 _hlRedrawPending を置換）
        private bool _hlRedrawRunning = false;
        private bool _hlRedrawQueued = false;

        // (#5) long に変更（オーバーフロー防止）
        private long _hlClearCallCount = 0;

        // (#11) OnBar で M5/H1 のバー更新を検知するための時刻追跡
        private DateTime _lastOnBarCheckTimeM5 = DateTime.MinValue;
        private DateTime _lastOnBarCheckTimeH1 = DateTime.MinValue;

        private StackPanel _hlDowStatusPanel;
        private TextBlock _hlDowStatusTextM5;
        private TextBlock _hlDowStatusTextH1;
        private TextBlock _hlDowStatusTextEntryGate;

        private string _hlDowLastDrawnH1Text = "";
        private string _hlDowLastDrawnM5Text = "";
        private string _hlDowLastDrawnEntryGateText = "";

        private TrendState _hlDowStateM5 = TrendState.TrendLess;
        private TrendState _hlDowStateH1 = TrendState.TrendLess;

        private TrendLessContext _hlDowContextM5 = TrendLessContext.None;
        private TrendLessContext _hlDowContextH1 = TrendLessContext.None;

        private DateTime _hlDowContextStartTimeM5 = DateTime.MinValue;
        private DateTime _hlDowContextStartTimeH1 = DateTime.MinValue;

        private DateTime _hlDowLastProcessedBarTimeM5 = DateTime.MinValue;
        private DateTime _hlDowLastProcessedBarTimeH1 = DateTime.MinValue;

        private DateTime _hlDowLastLoggedBarTimeM5 = DateTime.MinValue;
        private DateTime _hlDowLastLoggedBarTimeH1 = DateTime.MinValue;
        private bool _entryGateLastDecisionInitialized = false;
        private bool _entryGateLastDecisionOk = false;
        private string _entryGateLastDecisionReason = "";

        // ============================================================
        // Lifecycle
        // ============================================================
        protected override void OnStart()
        {
            _barsM5 = MarketData.GetBars(TimeFrame.Minute5, SymbolName);
            _barsH1 = MarketData.GetBars(TimeFrame.Hour, SymbolName);

            // (#3) 起動時に過去バーを十分に読み込む
            int requiredBars = Math.Max(MaxBars, HL_計算対象バー);
            EnsureMinimumBars(_barsM5, requiredBars, "M5");
            EnsureMinimumBars(_barsH1, requiredBars, "H1");

            int failed;
            HL_ForceCleanDrawingsByPrefix(out failed);

            PrintEntryGateStartupLog();

            DateTime utcNow = UtcNow();
            HL_RecalculateAndProject();
            HL_RedrawPivots();
            EvaluateHlDowAndUpdateUi(utcNow, true);
        }

        protected override void OnStop()
        {
            HL_ClearDrawings(true);
            RemoveHlDowStatusDisplay();
        }

        protected override void OnBar()
        {
            // M5チャート運用想定。チャート足が何であっても、描画はM5/H1で更新する。
            DateTime utcNow = UtcNow();

            // (#11) M5/H1のどちらかに新しい確定足が出たかチェック
            bool m5Changed = HasNewClosedBar(_barsM5, ref _lastOnBarCheckTimeM5);
            bool h1Changed = HasNewClosedBar(_barsH1, ref _lastOnBarCheckTimeH1);

            HL_RecalculateAndProject();

            // (#11) バー更新があった場合のみ再描画
            if (m5Changed || h1Changed)
                HL_RedrawPivots();

            EvaluateHlDowAndUpdateUi(utcNow, false);
        }

        // (#11) 指定Barsに新しい確定足が出たか判定
        private bool HasNewClosedBar(Bars bars, ref DateTime lastCheckTime)
        {
            if (bars == null || bars.Count < 2)
                return false;

            DateTime closedTime = bars.OpenTimes[bars.Count - 2];
            if (closedTime == lastCheckTime)
                return false;

            lastCheckTime = closedTime;
            return true;
        }

        // (#3) 起動時に過去バーを読み込む（最大 HL_LoadHistoryMaxRetries 回リトライ）
        private void EnsureMinimumBars(Bars bars, int requiredCount, string label)
        {
            if (bars == null)
                return;

            for (int retry = 0; retry < HL_LoadHistoryMaxRetries && bars.Count < requiredCount; retry++)
            {
                int before = bars.Count;
                bars.LoadMoreHistory();
                int after = bars.Count;

                PrintLog("LOAD_HISTORY | {0} | retry={1}/{2} | before={3} | after={4} | target={5}",
                    label, retry + 1, HL_LoadHistoryMaxRetries, before, after, requiredCount);

                if (after <= before)
                    break; // これ以上の履歴が存在しない
            }
        }

        // ============================================================
        // Pivot計算（M5/H1のみ）
        // ============================================================
        private void HL_RecalculateAndProject()
        {
            if ((_barsM5 == null || _barsM5.Count < 10) &&
                (_barsH1 == null || _barsH1.Count < 10))
            {
                _m5Pivots = new List<HL_Pivot>();
                _h1Pivots = new List<HL_Pivot>();
                return;
            }

            int maxBars = Math.Max(MaxBars, HL_計算対象バー);

            HL_RebuildPivotsIfNeeded(_barsM5, HL_RightBars_M5, maxBars, ref _lastPivotCalcBarTimeM5, ref _m5Pivots);
            HL_RebuildPivotsIfNeeded(_barsH1, HL_RightBars_H1, maxBars, ref _lastPivotCalcBarTimeH1, ref _h1Pivots);
        }

        private void HL_RebuildPivotsIfNeeded(Bars bars, int rightBars, int maxBars, ref DateTime lastCalcBarTime, ref List<HL_Pivot> target)
        {
            if (bars == null || bars.Count < 10)
            {
                target = new List<HL_Pivot>();
                lastCalcBarTime = DateTime.MinValue;
                return;
            }

            int closedIndex = bars.Count - 2;
            if (closedIndex < 0 || closedIndex >= bars.Count)
                return;

            DateTime closedBarTime = bars.OpenTimes[closedIndex];
            if (target != null && target.Count > 0 && lastCalcBarTime == closedBarTime)
                return;

            target = HL_BuildPivots(bars, rightBars, maxBars);
            lastCalcBarTime = closedBarTime;
        }

        private string HL_FormatLastPivot(List<HL_Pivot> pivots)
        {
            if (pivots == null || pivots.Count == 0)
                return "none";

            var p = pivots[pivots.Count - 1];
            string type = HL_GetPivotDisplayText(p);
            return string.Format(
                CultureInfo.InvariantCulture,
                "idx={0},time={1},side={2},price={3},type={4}",
                p.Index,
                FormatStructureTimeForLog(p.Time),
                p.Side > 0 ? "H" : "L",
                p.Price,
                type
            );
        }

        private void PrintEntryGateStartupLog()
        {
            PrintLog("現段階はエントリー未実装、ゲート評価のみ");
            PrintLog(
                "ENTRY_PARAM | HL_EntryEnabled={0} | 環境足トレンドレス時={1} | 環境足とエントリー足反対禁止={2}",
                HL_EntryEnabled ? "true" : "false",
                環境足トレンドレス時,
                環境足とエントリー足反対禁止 ? "true" : "false");
        }

        private string HL_GetPivotDisplayText(HL_Pivot pivot)
        {
            string kind = NormalizePivotKind(pivot.Kind);
            if (pivot.Side == 1)
                return (kind == "HH" || kind == "LH") ? kind : "H";

            if (pivot.Side == -1)
                return (kind == "HL" || kind == "LL") ? kind : "L";

            return "NA";
        }

        private Color HL_GetPivotDisplayColor(HL_Pivot pivot, string text)
        {
            if (pivot.Side == 1)
            {
                if (text == "HH") return Color.Lime;
                if (text == "LH") return Color.Orange;
                return Color.Gray;
            }

            if (pivot.Side == -1)
            {
                if (text == "HL") return Color.Aqua;
                if (text == "LL") return Color.Red;
                return Color.Gray;
            }

            return Color.Gray;
        }

        private void HL_AssignPivotKindsByDisplayRule(List<HL_Pivot> pivots)
        {
            if (pivots == null || pivots.Count == 0)
                return;

            double tolerancePrice = HL_EqualTolerancePips * Symbol.PipSize;

            double? prevHigh = null;
            double? prevLow = null;

            for (int i = 0; i < pivots.Count; i++)
            {
                HL_Pivot p = pivots[i];
                if (p.Side == 1)
                {
                    if (!prevHigh.HasValue)
                    {
                        p.Kind = "H";
                    }
                    else if (Math.Abs(p.Price - prevHigh.Value) <= tolerancePrice)
                    {
                        // 許容幅内 = 同値とみなす → 有利側に倒す = HH
                        p.Kind = "HH";
                    }
                    else
                    {
                        p.Kind = p.Price > prevHigh.Value ? "HH" : "LH";
                    }
                    prevHigh = p.Price;
                }
                else if (p.Side == -1)
                {
                    if (!prevLow.HasValue)
                    {
                        p.Kind = "L";
                    }
                    else if (Math.Abs(p.Price - prevLow.Value) <= tolerancePrice)
                    {
                        // (#2) 許容幅内 = 同値とみなす → 有利側に倒す = HL（High側HHと対称）
                        p.Kind = "HL";
                    }
                    else
                    {
                        p.Kind = p.Price > prevLow.Value ? "HL" : "LL";
                    }
                    prevLow = p.Price;
                }
                else
                {
                    p.Kind = string.Empty;
                }

                pivots[i] = p;
            }
        }

        private List<HL_Pivot> HL_BuildPivots(Bars bars, int rightBars, int maxBars)
        {
            var pivots = new List<HL_Pivot>(1024);
            if (bars == null || bars.Count < 10)
                return pivots;

            // ============================================================
            // 【リペイント無し化（許可された修正点）】
            // endIndex を「未確定足(Count-1)」ではなく「確定足(Count-2)」に固定する
            // ============================================================
            int endIndex = bars.Count - 2;
            if (endIndex < 0) return pivots;

            int startIndex = Math.Max(0, endIndex - Math.Max(10, maxBars));
            int maxCandidate = Math.Max(startIndex, endIndex - Math.Max(0, rightBars));

            double deviation = HL_GetDeviationPrice(HL_Points);
            double suppress = HL_GetDeviationPrice(HL_SuppressPoints);
            HL_Pivot? last = null;

            for (int i = startIndex + HL_ZZ_Depth; i <= maxCandidate; i++)
            {
                bool isHigh = HL_IsPivotHigh(bars, i, startIndex, maxCandidate, HL_ZZ_Depth, rightBars, HL_同値右優先);
                bool isLow = HL_IsPivotLow(bars, i, startIndex, maxCandidate, HL_ZZ_Depth, rightBars, HL_同値右優先);

                if (isHigh && isLow)
                {
                    if (last.HasValue)
                    {
                        if (last.Value.Side == 1)
                            isHigh = false;
                        else
                            isLow = false;
                    }
                    else
                    {
                        isLow = false; // 初回はHigh側を採用
                    }
                }

                if (isHigh)
                {
                    var c = new HL_Pivot
                    {
                        Index = i,
                        Time = bars.OpenTimes[i],
                        Price = bars.HighPrices[i],
                        Side = 1,
                        Kind = string.Empty
                    };
                    if (HL_TryAcceptPivot(c, ref last, pivots, deviation, suppress, HL_Backstep))
                        continue;
                }

                if (isLow)
                {
                    var c = new HL_Pivot
                    {
                        Index = i,
                        Time = bars.OpenTimes[i],
                        Price = bars.LowPrices[i],
                        Side = -1,
                        Kind = string.Empty
                    };
                    HL_TryAcceptPivot(c, ref last, pivots, deviation, suppress, HL_Backstep);
                }
            }

            HL_AssignPivotKindsByDisplayRule(pivots);
            return pivots;
        }

        private double HL_GetDeviationPrice(double points)
        {
            return points <= 0.0 ? 0.0 : points * Symbol.TickSize;
        }

        private static bool HL_IsPivotHigh(Bars bars, int i, int startIndex, int maxCandidate, int depth, int rightBars, bool tieRight)
        {
            int left = Math.Max(startIndex, i - depth);
            int right = Math.Min(maxCandidate, i + Math.Max(0, rightBars));
            double h = bars.HighPrices[i];

            for (int k = left; k <= right; k++)
            {
                if (k == i)
                    continue;
                if (bars.HighPrices[k] > h)
                    return false;
                if (tieRight && k > i && bars.HighPrices[k] == h)
                    return false;
            }

            return true;
        }

        private static bool HL_IsPivotLow(Bars bars, int i, int startIndex, int maxCandidate, int depth, int rightBars, bool tieRight)
        {
            int left = Math.Max(startIndex, i - depth);
            int right = Math.Min(maxCandidate, i + Math.Max(0, rightBars));
            double l = bars.LowPrices[i];

            for (int k = left; k <= right; k++)
            {
                if (k == i)
                    continue;
                if (bars.LowPrices[k] < l)
                    return false;
                if (tieRight && k > i && bars.LowPrices[k] == l)
                    return false;
            }

            return true;
        }

        private static bool HL_TryAcceptPivot(HL_Pivot candidate, ref HL_Pivot? last, List<HL_Pivot> pivots, double deviation, double suppress, int backstep)
        {
            if (suppress > 0.0 && last.HasValue)
            {
                if (Math.Abs(candidate.Price - last.Value.Price) < suppress)
                    return false;
            }

            if (!last.HasValue)
            {
                pivots.Add(candidate);
                last = candidate;
                return true;
            }

            var prev = last.Value;

            if (candidate.Side == prev.Side)
            {
                if (backstep > 0 && (candidate.Index - prev.Index) <= backstep)
                {
                    bool moreExtreme =
                        (candidate.Side == 1 && candidate.Price >= prev.Price) ||
                        (candidate.Side == -1 && candidate.Price <= prev.Price);

                    if (moreExtreme)
                    {
                        pivots[pivots.Count - 1] = candidate;
                        last = candidate;
                        return true;
                    }
                    return false;
                }

                bool replace =
                    (candidate.Side == 1 && candidate.Price >= prev.Price) ||
                    (candidate.Side == -1 && candidate.Price <= prev.Price);

                if (replace)
                {
                    pivots[pivots.Count - 1] = candidate;
                    last = candidate;
                    return true;
                }

                return false;
            }

            if (deviation > 0.0 && Math.Abs(candidate.Price - prev.Price) < deviation)
                return false;

            pivots.Add(candidate);
            last = candidate;
            return true;
        }

        private static string NormalizePivotKind(string kind)
        {
            return string.IsNullOrWhiteSpace(kind) ? string.Empty : kind.Trim().ToUpperInvariant();
        }

        private static bool IsPivotKind(HL_Pivot pivot, string expected)
        {
            return string.Equals(NormalizePivotKind(pivot.Kind), expected, StringComparison.Ordinal);
        }

        // (#12) IsHighPivotKind / IsLowPivotKind / FormatStructureIndexForLog は
        //       呼び出し箇所がないため削除済み（将来エントリーロジック実装時に再追加可）

        private static int FindLatestPivotListIndexByKind(List<HL_Pivot> pivots, string kind, int startListIndexInclusive)
        {
            if (pivots == null || pivots.Count == 0 || startListIndexInclusive < 0)
                return -1;

            int start = Math.Min(startListIndexInclusive, pivots.Count - 1);
            for (int i = start; i >= 0; i--)
            {
                HL_Pivot p = pivots[i];
                if (IsPivotKind(p, kind))
                    return i;
            }

            return -1;
        }

        private static int FindLatestPivotListIndexBySide(List<HL_Pivot> pivots, int side, int startListIndexInclusive)
        {
            if (pivots == null || pivots.Count == 0 || startListIndexInclusive < 0)
                return -1;

            int start = Math.Min(startListIndexInclusive, pivots.Count - 1);
            for (int i = start; i >= 0; i--)
            {
                if (pivots[i].Side == side)
                    return i;
            }

            return -1;
        }

        // (#7→#14) FindLatestBreakPivotListIndex / HasFollowingPivotBySide は
        //      押し安値/戻り高値方式に変更したため削除済み

        private static List<HL_Pivot> GetConfirmedPivotsUpToTime(List<HL_Pivot> pivots, DateTime cutoffTime)
        {
            var result = new List<HL_Pivot>(pivots == null ? 0 : pivots.Count);
            if (pivots == null || pivots.Count == 0)
                return result;

            for (int i = 0; i < pivots.Count; i++)
            {
                HL_Pivot p = pivots[i];
                if (p.Time <= cutoffTime)
                    result.Add(p);
            }

            return result;
        }

        private void ResetHlDowStateMachine()
        {
            _hlDowStateM5 = TrendState.TrendLess;
            _hlDowStateH1 = TrendState.TrendLess;

            _hlDowContextM5 = TrendLessContext.None;
            _hlDowContextH1 = TrendLessContext.None;

            _hlDowContextStartTimeM5 = DateTime.MinValue;
            _hlDowContextStartTimeH1 = DateTime.MinValue;

            _hlDowLastProcessedBarTimeM5 = DateTime.MinValue;
            _hlDowLastProcessedBarTimeH1 = DateTime.MinValue;

            _hlDowLastLoggedBarTimeM5 = DateTime.MinValue;
            _hlDowLastLoggedBarTimeH1 = DateTime.MinValue;
        }

        private void EvaluateHlDowAndUpdateUi(DateTime utcNow, bool initializeOnStart)
        {
            if (initializeOnStart)
                ResetHlDowStateMachine();

            // MTFズレ防止: H1判定は常にH1時刻のpivotを直接使う
            List<HL_Pivot> h1Input = _h1Pivots ?? new List<HL_Pivot>();

            EvaluateOneTf("M5", _m5Pivots ?? new List<HL_Pivot>(), ResolveBarsForTf(HL時間足.M5), ref _hlDowStateM5, ref _hlDowContextM5, ref _hlDowContextStartTimeM5, ref _hlDowLastProcessedBarTimeM5, ref _hlDowLastLoggedBarTimeM5, utcNow);
            EvaluateOneTf("H1", h1Input, ResolveBarsForTf(HL時間足.H1), ref _hlDowStateH1, ref _hlDowContextH1, ref _hlDowContextStartTimeH1, ref _hlDowLastProcessedBarTimeH1, ref _hlDowLastLoggedBarTimeH1, utcNow);

            string entryGateReason;
            bool entryGateOk = EvaluateEntryGate(_hlDowStateH1, _hlDowStateM5, out entryGateReason);
            LogEntryGateDecisionIfNeeded(entryGateOk, entryGateReason, initializeOnStart);

            UpdateHlDowStatusDisplay(initializeOnStart, _hlDowStateM5, _hlDowStateH1, entryGateOk, entryGateReason);
        }

        private bool EvaluateEntryGate(TrendState envTrendState, TrendState entryTrendState, out string reason)
        {
            if (!HL_EntryEnabled)
            {
                reason = "ENTRY_DISABLED";
                return false;
            }

            if (envTrendState == TrendState.TrendLess &&
                環境足トレンドレス時 == 環境足トレンドレス時_許可設定.禁止)
            {
                reason = "ENV_TRENDLESS_BLOCKED";
                return false;
            }

            if (環境足とエントリー足反対禁止 &&
                IsOppositeTrend(envTrendState, entryTrendState))
            {
                reason = "ENV_ENTRY_CONFLICT";
                return false;
            }

            reason = "OK";
            return true;
        }

        private static bool IsOppositeTrend(TrendState envTrendState, TrendState entryTrendState)
        {
            return
                (envTrendState == TrendState.UpTrend && entryTrendState == TrendState.DownTrend) ||
                (envTrendState == TrendState.DownTrend && entryTrendState == TrendState.UpTrend);
        }

        private void LogEntryGateDecisionIfNeeded(bool entryGateOk, string reason, bool forceLog)
        {
            bool changed =
                !_entryGateLastDecisionInitialized ||
                _entryGateLastDecisionOk != entryGateOk ||
                !string.Equals(_entryGateLastDecisionReason, reason, StringComparison.Ordinal);

            if ((forceLog || changed) && HL_LogOutput)
            {
                PrintLog(
                    "ENTRY_GATE | Result={0} | Reason={1} | Env(H1)={2} | Entry(M5)={3}",
                    entryGateOk ? "OK" : "NG",
                    reason,
                    FormatHlDowTrendStateForLog(_hlDowStateH1),
                    FormatHlDowTrendStateForLog(_hlDowStateM5));
            }

            _entryGateLastDecisionInitialized = true;
            _entryGateLastDecisionOk = entryGateOk;
            _entryGateLastDecisionReason = reason;
        }

        private Bars ResolveBarsForTf(HL時間足 tf)
        {
            switch (tf)
            {
                case HL時間足.M5:
                    if ((_barsM5 == null || _barsM5.Count < 2) && Bars != null && Bars.TimeFrame == TimeFrame.Minute5)
                        return Bars;
                    return _barsM5;
                default:
                    if ((_barsH1 == null || _barsH1.Count < 2) && Bars != null && Bars.TimeFrame == TimeFrame.Hour)
                        return Bars;
                    return _barsH1;
            }
        }

        // Dow Theory trend state machine (non-repaint pivots). Close-only, strict inequality.
        private TrendState DetermineTrendFromDowStateMachine(
            ref TrendState state,
            ref TrendLessContext context,
            ref DateTime contextStartTime,
            List<HL_Pivot> pivots,
            Bars bars,
            DateTime currentBarTime,
            double currentClose,
            out HL_Pivot? refHigh,
            out HL_Pivot? lastHL,
            out HL_Pivot? lastLH,
            out HL_Pivot? refLow,
            out HL_Pivot? keyLH,
            out HL_Pivot? keyHL,
            out HL_Pivot? triggerHigh,
            out HL_Pivot? triggerLow,
            out int hIndex,
            out double? hPrice,
            out int lIndex,
            out double? lPrice,
            out int loIndex,
            out int hiIndex,
            out bool hlFormed,
            out bool lhFormed,
            out string reason)
        {
            refHigh = null;
            lastHL = null;
            lastLH = null;
            refLow = null;
            keyLH = null;
            keyHL = null;
            triggerHigh = null;
            triggerLow = null;
            hIndex = -1;
            hPrice = null;
            lIndex = -1;
            lPrice = null;
            loIndex = -1;
            hiIndex = -1;
            hlFormed = false;
            lhFormed = false;
            reason = "TL_STAY_NO_STRUCTURE";

            if (pivots == null || pivots.Count == 0 || bars == null || bars.Count < 3)
            {
                state = TrendState.TrendLess;
                reason = "TL_STAY_NO_PIVOT";
                return state;
            }

            int closedIndex = bars.Count - 2;
            if (closedIndex < 1)
            {
                state = TrendState.TrendLess;
                reason = "TL_STAY_NO_CLOSED_BAR";
                return state;
            }

            List<HL_Pivot> closedPivots = GetConfirmedPivotsUpToTime(pivots, currentBarTime);
            if (closedPivots.Count == 0)
            {
                state = TrendState.TrendLess;
                reason = "TL_STAY_NO_CONFIRMED_PIVOT";
                return state;
            }

            int lastIndex = closedPivots.Count - 1;
            int latestHlIdx = FindLatestPivotListIndexByKind(closedPivots, "HL", lastIndex);
            int latestLhIdx = FindLatestPivotListIndexByKind(closedPivots, "LH", lastIndex);

            if (latestHlIdx >= 0)
                lastHL = closedPivots[latestHlIdx];
            if (latestLhIdx >= 0)
                lastLH = closedPivots[latestLhIdx];

            // ============================================================
            // 押し安値（keyHL）/ 戻り高値（keyLH）の特定
            // ============================================================
            // ダウ理論におけるトレンド崩壊判定:
            //
            // ■ 押し安値（UpTrend break 用）
            //   = 最新HHの直前にあるLow側ピボット
            //   例: HL(A) → HH → HL(B)（現在形成中）
            //       押し安値 = HL(A)（HHの前のもの）
            //       HL(B) を下抜けても UpTrend 継続
            //       HL(A) を下抜けて初めて TrendLess
            //   HH が新たに形成されるたびに押し安値が切り上がる
            //
            // ■ 戻り高値（DownTrend break 用）
            //   = 最新LLの直前にあるHigh側ピボット
            //   同様の考え方（対称）
            // ============================================================
            int latestHhIdx = FindLatestPivotListIndexByKind(closedPivots, "HH", lastIndex);
            int latestLlIdx = FindLatestPivotListIndexByKind(closedPivots, "LL", lastIndex);

            // 押し安値: 最新HHの直前Low側ピボット
            if (latestHhIdx >= 0)
            {
                int pushLowIdx = FindLatestPivotListIndexBySide(closedPivots, -1, latestHhIdx - 1);
                if (pushLowIdx >= 0)
                    keyHL = closedPivots[pushLowIdx];
            }
            // HH未形成時（トレンド開始直後等）は最新HLをフォールバック
            if (!keyHL.HasValue && latestHlIdx >= 0)
                keyHL = closedPivots[latestHlIdx];

            // 戻り高値: 最新LLの直前High側ピボット
            if (latestLlIdx >= 0)
            {
                int returnHighIdx = FindLatestPivotListIndexBySide(closedPivots, 1, latestLlIdx - 1);
                if (returnHighIdx >= 0)
                    keyLH = closedPivots[returnHighIdx];
            }
            // LL未形成時（トレンド開始直後等）は最新LHをフォールバック
            if (!keyLH.HasValue && latestLhIdx >= 0)
                keyLH = closedPivots[latestLhIdx];

            HL_Pivot? refHighForUp = null;
            HL_Pivot? refLowForDown = null;

            if (latestHlIdx >= 0)
            {
                int refHighIdx = FindLatestPivotListIndexBySide(closedPivots, 1, latestHlIdx - 1);
                if (refHighIdx >= 0)
                {
                    refHighForUp = closedPivots[refHighIdx];
                    // 反転時も同じ考え方: HLの直前高値（LH/HH問わず）を上抜けでUp判定
                    refHigh = refHighForUp;
                    triggerHigh = refHighForUp;
                    triggerLow = lastHL;
                    hIndex = refHighForUp.Value.Index;
                    hPrice = refHighForUp.Value.Price;
                    lIndex = lastHL.Value.Index;
                    lPrice = lastHL.Value.Price;
                    hlFormed = true;
                }
            }

            if (latestLhIdx >= 0)
            {
                int refLowIdx = FindLatestPivotListIndexBySide(closedPivots, -1, latestLhIdx - 1);
                if (refLowIdx >= 0)
                {
                    refLowForDown = closedPivots[refLowIdx];
                    // 反転時も同じ考え方: LHの直前安値（HL/LL問わず）を下抜けでDown判定
                    refLow = refLowForDown;
                    loIndex = refLowForDown.Value.Index;
                    hiIndex = lastLH.Value.Index;
                    if (!triggerLow.HasValue) triggerLow = refLowForDown;
                    if (!triggerHigh.HasValue) triggerHigh = lastLH;
                    lhFormed = true;
                }
            }

            bool exitedFromUp = false;
            bool exitedFromDown = false;

            // 1) UpTrend -> TrendLess: 押し安値を下抜け
            if (state == TrendState.UpTrend)
            {
                if (keyHL.HasValue && currentClose < keyHL.Value.Price)
                {
                    state = TrendState.TrendLess;
                    exitedFromUp = true;
                    context = TrendLessContext.AfterUpEnd;
                    contextStartTime = currentBarTime;
                    reason = "UP_TO_TL_BY_PUSH_LOW_BREAK";
                }
                else
                {
                    reason = keyHL.HasValue ? "UP_HOLD_PUSH_LOW_NOT_BROKEN" : "UP_HOLD_NO_PUSH_LOW";
                    return state;
                }
            }

            // 2) DownTrend -> TrendLess: 戻り高値を上抜け
            if (state == TrendState.DownTrend)
            {
                if (keyLH.HasValue && currentClose > keyLH.Value.Price)
                {
                    state = TrendState.TrendLess;
                    exitedFromDown = true;
                    context = TrendLessContext.AfterDownEnd;
                    contextStartTime = currentBarTime;
                    reason = "DOWN_TO_TL_BY_RETURN_HIGH_BREAK";
                }
                else
                {
                    reason = keyLH.HasValue ? "DOWN_HOLD_RETURN_HIGH_NOT_BROKEN" : "DOWN_HOLD_NO_RETURN_HIGH";
                    return state;
                }
            }

            // 3) TrendLess -> UpTrend: HL成立 + その直前高値上抜け
            bool upEntry = hlFormed && refHighForUp.HasValue && currentClose > refHighForUp.Value.Price;
            // 4) TrendLess -> DownTrend: LH成立 + その直前安値下抜け
            bool downEntry = lhFormed && refLowForDown.HasValue && currentClose < refLowForDown.Value.Price;

            // ============================================================
            // TrendLess突入後の新構造フィルタ
            // ============================================================
            // トレンド崩壊 → TrendLess に入った後は、TrendLess突入時点より後に
            // 新たに形成されたピボット構造（HL/LH）のみで新トレンドへの遷移を許可する。
            // 例: UpTrend → TrendLess の場合、その後に新たにLHが形成され、
            //     そのLHの直前安値を下抜けて初めてDownTrendに遷移する。
            //     TrendLess突入前に既に存在していたLH/LL構造では遷移しない。
            // contextStartTime == MinValue の場合（初期状態・コンテキストなし）は
            // フィルタなし（既存構造での遷移を許可）。
            if (contextStartTime != DateTime.MinValue)
            {
                if (upEntry && lastHL.HasValue && lastHL.Value.Time <= contextStartTime)
                {
                    upEntry = false;
                    reason = "TL_BLOCKED_HL_BEFORE_CONTEXT";
                }
                if (downEntry && lastLH.HasValue && lastLH.Value.Time <= contextStartTime)
                {
                    downEntry = false;
                    if (string.IsNullOrEmpty(reason) || reason == "TL_STAY_NO_STRUCTURE")
                        reason = "TL_BLOCKED_LH_BEFORE_CONTEXT";
                }
            }

            if (upEntry && !downEntry)
            {
                state = TrendState.UpTrend;
                context = TrendLessContext.None;
                contextStartTime = DateTime.MinValue;
                reason = exitedFromDown ? "DOWN_TO_UP_BY_REF_HIGH_BREAK" : "TL_TO_UP_BY_REF_HIGH_BREAK";
                return state;
            }

            if (downEntry && !upEntry)
            {
                state = TrendState.DownTrend;
                context = TrendLessContext.None;
                contextStartTime = DateTime.MinValue;
                reason = exitedFromUp ? "UP_TO_DOWN_BY_REF_LOW_BREAK" : "TL_TO_DOWN_BY_REF_LOW_BREAK";
                return state;
            }

            // (#6) upEntry && downEntry 同時成立時は TrendLess 維持（どちらにも倒さない）
            if (upEntry && downEntry)
            {
                reason = exitedFromUp || exitedFromDown
                    ? "TL_STAY_BOTH_BREAK_AFTER_EXIT"
                    : "TL_STAY_BOTH_BREAK_CONFLICT";
                return state;
            }

            if (exitedFromUp || exitedFromDown)
            {
                // ブレイク直後に反対構造が未成立なら、そのバーはTrendLess維持
                return state;
            }

            // TrendLess維持: context/contextStartTimeは前回バーの値を保持する
            // （AfterUpEnd/AfterDownEndは新トレンド開始まで消さない）
            reason = (hlFormed || lhFormed) ? "TL_STAY_STRUCTURE_NO_BREAK" : "TL_STAY_NO_STRUCTURE";
            return state;
        }

        private void EvaluateOneTf(
            string tf,
            List<HL_Pivot> input,
            Bars bars,
            ref TrendState state,
            ref TrendLessContext context,
            ref DateTime contextStart,
            ref DateTime lastProcessedBarTime,
            ref DateTime lastLoggedBarTime,
            DateTime utcNow)
        {
            DateTime barTime;
            double close;
            if (!TryGetLastClosedBarInfo(bars, out barTime, out close))
                return;

            bool newBar = (barTime != lastProcessedBarTime);
            if (!newBar)
                return;

            lastProcessedBarTime = barTime;

            TrendState stateBefore = state;
            HL_Pivot? refHigh = null, lastHL = null, lastLH = null, refLow = null;
            HL_Pivot? keyLH = null, keyHL = null, triggerHigh = null, triggerLow = null;
            int hIndex = -1, lIndex = -1, loIndex = -1, hiIndex = -1;
            double? hPrice = null, lPrice = null;
            bool hlFormed = false, lhFormed = false;
            string reason = string.Empty;

            DetermineTrendFromDowStateMachine(
                ref state,
                ref context,
                ref contextStart,
                input,
                bars,
                barTime,
                close,
                out refHigh,
                out lastHL,
                out lastLH,
                out refLow,
                out keyLH,
                out keyHL,
                out triggerHigh,
                out triggerLow,
                out hIndex,
                out hPrice,
                out lIndex,
                out lPrice,
                out loIndex,
                out hiIndex,
                out hlFormed,
                out lhFormed,
                out reason);

            if (HL_LogOutput && barTime != lastLoggedBarTime)
            {
                lastLoggedBarTime = barTime;
                PrintHlDowDecisionLog(
                    tf,
                    barTime,
                    close,
                    keyLH,
                    keyHL,
                    triggerHigh,
                    triggerLow,
                    refHigh,
                    lastHL,
                    lastLH,
                    refLow,
                    stateBefore,
                    state,
                    reason,
                    utcNow,
                    hIndex,
                    hPrice,
                    lIndex,
                    lPrice,
                    loIndex,
                    hiIndex,
                    hlFormed,
                    lhFormed);
            }
        }

        private bool TryGetLastClosedBarInfo(Bars bars, out DateTime barTime, out double close)
        {
            barTime = DateTime.MinValue;
            close = 0.0;

            if (bars == null || bars.Count < 2)
                return false;

            int closedIndex = bars.Count - 2;
            if (closedIndex < 0 || closedIndex >= bars.Count)
                return false;

            barTime = bars.OpenTimes[closedIndex];
            close = bars.ClosePrices[closedIndex];
            return true;
        }

        private void PrintHlDowDecisionLog(
            string tf,
            DateTime barTime,
            double currentClose,
            HL_Pivot? keyLH,
            HL_Pivot? keyHL,
            HL_Pivot? triggerHigh,
            HL_Pivot? triggerLow,
            HL_Pivot? refHigh,
            HL_Pivot? lastHL,
            HL_Pivot? lastLH,
            HL_Pivot? refLow,
            TrendState stateBefore,
            TrendState stateAfter,
            string reason,
            DateTime utcNow,
            int hIndex,
            double? hPrice,
            int lIndex,
            double? lPrice,
            int loIndex,
            int hiIndex,
            bool hlFormed,
            bool lhFormed)
        {
            HL_Pivot? refHL = keyHL.HasValue ? keyHL : lastHL;
            HL_Pivot? refLH = keyLH.HasValue ? keyLH : lastLH;

            PrintLog(
                "HL_DOW[{0}] {1} -> {2} | bar={3} | refHigh({4},{5}) refHL({6},{7}) refLH({8},{9}) refLow({10},{11}) | close={12} | reason={13}{14} | refIdx High={15} HL={16} LH={17} Low={18} | refKind High={19} HL={20} LH={21} Low={22}",
                tf,
                FormatHlDowTrendStateForLog(stateBefore),
                FormatHlDowTrendStateForLog(stateAfter),
                FormatStructureTimeForLog(barTime),
                FormatPivotTimeForLog(refHigh),
                FormatStructurePriceForLog(refHigh),
                FormatPivotTimeForLog(refHL),
                FormatStructurePriceForLog(refHL),
                FormatPivotTimeForLog(refLH),
                FormatStructurePriceForLog(refLH),
                FormatPivotTimeForLog(refLow),
                FormatStructurePriceForLog(refLow),
                FormatStructurePriceForLog(currentClose),
                string.IsNullOrWhiteSpace(reason) ? "UNSPECIFIED" : reason.Trim(),
                BuildTimeTag(utcNow),
                FormatPivotIndexForLog(refHigh),
                FormatPivotIndexForLog(refHL),
                FormatPivotIndexForLog(refLH),
                FormatPivotIndexForLog(refLow),
                FormatPivotKindForLog(refHigh),
                FormatPivotKindForLog(refHL),
                FormatPivotKindForLog(refLH),
                FormatPivotKindForLog(refLow));
        }

        private string FormatStructurePriceForLog(HL_Pivot? pivot)
        {
            return pivot.HasValue
                ? pivot.Value.Price.ToString("F5", CultureInfo.InvariantCulture)
                : "NA";
        }

        private string FormatStructurePriceForLog(double value)
        {
            return value.ToString("F5", CultureInfo.InvariantCulture);
        }

        private string FormatStructurePriceForLog(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("F5", CultureInfo.InvariantCulture)
                : "NA";
        }

        private string FormatPivotTimeForLog(HL_Pivot? pivot)
        {
            return pivot.HasValue
                ? FormatStructureTimeForLog(pivot.Value.Time)
                : "NA";
        }

        private string FormatPivotIndexForLog(HL_Pivot? pivot)
        {
            return pivot.HasValue
                ? pivot.Value.Index.ToString(CultureInfo.InvariantCulture)
                : "NA";
        }

        private string FormatPivotKindForLog(HL_Pivot? pivot)
        {
            if (!pivot.HasValue)
                return "NA";

            string kind = NormalizePivotKind(pivot.Value.Kind);
            if (!string.IsNullOrEmpty(kind))
                return kind;

            return pivot.Value.Side == 1 ? "H" : (pivot.Value.Side == -1 ? "L" : "?");
        }

        private string FormatStructureTimeForLog(DateTime value)
        {
            return FmtLogTime(value);
        }

        private string FormatHlDowTrendStateForLog(TrendState state)
        {
            if (state == TrendState.UpTrend)
                return "UpTrend";
            if (state == TrendState.DownTrend)
                return "DownTrend";
            return "TrendLess";
        }

        private string FormatHlDowTrendState(TrendState state)
        {
            if (state == TrendState.UpTrend)
                return "UP TREND";
            if (state == TrendState.DownTrend)
                return "DOWN TREND";
            return "TREND LESS";
        }

        private Color ResolveHlDowStateColor(TrendState state)
        {
            if (state == TrendState.UpTrend)
                return Color.LightSkyBlue;
            if (state == TrendState.DownTrend)
                return Color.Red;
            return Color.Yellow;
        }

        private void EnsureHlDowStatusPanel()
        {
            if (_hlDowStatusPanel != null)
                return;

            if (Chart == null)
                return;

            _hlDowStatusPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 8)
            };

            _hlDowStatusTextM5 = new TextBlock
            {
                FontSize = 18,
                Margin = new Thickness(0, 0, 0, 0)
            };

            _hlDowStatusTextH1 = new TextBlock
            {
                FontSize = 18,
                Margin = new Thickness(0, 0, 0, 0)
            };

            _hlDowStatusTextEntryGate = new TextBlock
            {
                FontSize = 18,
                Margin = new Thickness(0, 0, 0, 0)
            };

            _hlDowStatusPanel.AddChild(_hlDowStatusTextM5);
            _hlDowStatusPanel.AddChild(_hlDowStatusTextH1);
            _hlDowStatusPanel.AddChild(_hlDowStatusTextEntryGate);

            Chart.AddControl(_hlDowStatusPanel);
        }

        private void UpdateHlDowStatusDisplay(bool force, TrendState m5State, TrendState h1State, bool entryGateOk, string entryGateReason)
        {
            string m5Line = "M5 : " + FormatHlDowTrendState(m5State);
            string h1Line = "H1 : " + FormatHlDowTrendState(h1State);
            string entryGateLine = entryGateOk
                ? "ENTRY GATE: OK"
                : "ENTRY GATE: NG (" + (string.IsNullOrWhiteSpace(entryGateReason) ? "UNSPECIFIED" : entryGateReason) + ")";

            if (!force &&
                string.Equals(_hlDowLastDrawnH1Text, h1Line, StringComparison.Ordinal) &&
                string.Equals(_hlDowLastDrawnM5Text, m5Line, StringComparison.Ordinal) &&
                string.Equals(_hlDowLastDrawnEntryGateText, entryGateLine, StringComparison.Ordinal))
            {
                return;
            }

            _hlDowLastDrawnH1Text = h1Line;
            _hlDowLastDrawnM5Text = m5Line;
            _hlDowLastDrawnEntryGateText = entryGateLine;

            BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (Chart == null)
                        return;

                    EnsureHlDowStatusPanel();

                    // (#9) 旧コードの Chart.RemoveObject(HLDOW_STATUS_NAME_H1/M5) は
                    //      対応するDrawObjectが存在しない空振り呼び出しだったため削除済み

                    if (_hlDowStatusTextM5 != null)
                    {
                        _hlDowStatusTextM5.Text = m5Line;
                        _hlDowStatusTextM5.ForegroundColor = ResolveHlDowStateColor(m5State);
                    }

                    if (_hlDowStatusTextH1 != null)
                    {
                        _hlDowStatusTextH1.Text = h1Line;
                        _hlDowStatusTextH1.ForegroundColor = ResolveHlDowStateColor(h1State);
                    }

                    if (_hlDowStatusTextEntryGate != null)
                    {
                        _hlDowStatusTextEntryGate.Text = entryGateLine;
                        _hlDowStatusTextEntryGate.ForegroundColor = entryGateOk ? Color.Lime : Color.OrangeRed;
                    }
                }
                catch
                {
                }
            });
        }

        private void RemoveHlDowStatusDisplay()
        {
            _hlDowLastDrawnH1Text = "";
            _hlDowLastDrawnM5Text = "";
            _hlDowLastDrawnEntryGateText = "";

            Action removeAction = () =>
            {
                try
                {
                    if (Chart == null)
                        return;

                    // (#9) 旧コードの Chart.RemoveObject 空振り呼び出し削除済み

                    if (_hlDowStatusPanel != null)
                    {
                        try
                        {
                            Chart.RemoveControl(_hlDowStatusPanel);
                        }
                        catch
                        {
                        }
                    }

                    _hlDowStatusPanel = null;
                    _hlDowStatusTextM5 = null;
                    _hlDowStatusTextH1 = null;
                    _hlDowStatusTextEntryGate = null;
                }
                catch
                {
                }
            };

            try
            {
                BeginInvokeOnMainThread(removeAction);
            }
            catch
            {
                removeAction();
            }
        }

        // (#12) 注意: Server.Time はブローカーのサーバー時間であり、
        //       ブローカーによっては UTC+2/+3 の場合があります。
        //       ログ時刻が実際のUTCとずれる可能性があります。
        private DateTime UtcNow()
        {
            return DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);
        }

        private DateTime ToChartDisplayTime(DateTime utc)
        {
            DateTime utcTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return utcTime + Application.UserTimeOffset;
        }

        private string FmtLogTime(DateTime utc)
        {
            if (utc == DateTime.MinValue)
                return "NA";

            return ToChartDisplayTime(utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private void PrintLog(string format, params object[] args)
        {
            string body = (args == null || args.Length == 0)
                ? format
                : string.Format(CultureInfo.InvariantCulture, format, args);

            Print("[{0}][{1}] {2}", CODE_NAME, FmtLogTime(UtcNow()), body);
        }

        private string BuildTimeTag(DateTime utc)
        {
            if (!EnableProReport)
                return string.Empty;

            return string.Format(CultureInfo.InvariantCulture, " | ChartTime={0}", FmtLogTime(utc));
        }

        // ============================================================
        // 描画名・対象判定（034から抜き出し）
        // ============================================================
        private static string HL_SanitizeNameToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "NA";

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= 'A' && c <= 'Z') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            return sb.ToString();
        }

        private string HL_BuildObjectName(string tf, string kind, int indexA, int sideA, int indexB, int sideB, int sequence)
        {
            string symbolToken = HL_SanitizeNameToken(SymbolName);
            string timeframeToken = HL_SanitizeNameToken(Bars != null ? Bars.TimeFrame.ToString() : "NA");
            string tfToken = HL_SanitizeNameToken(tf);
            string kindToken = HL_SanitizeNameToken(kind);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}.{2}.HL.{3}.{4}.{5}.{6}.{7}.{8}.{9}",
                HL_DrawObjectPrefix,
                symbolToken,
                timeframeToken,
                tfToken,
                kindToken,
                indexA,
                sideA,
                indexB,
                sideB,
                sequence);
        }

        private bool HL_IsDrawObjectTargetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (name.StartsWith(HL_DrawObjectPrefix, StringComparison.Ordinal) &&
                name.IndexOf(".HL.", StringComparison.Ordinal) >= 0)
                return true;

            for (int i = 0; i < HL_OldDrawObjectPrefixes.Length; i++)
            {
                string oldPrefix = HL_OldDrawObjectPrefixes[i];
                if (!string.IsNullOrEmpty(oldPrefix) &&
                    name.StartsWith(oldPrefix, StringComparison.Ordinal) &&
                    name.IndexOf(".HL.", StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        // ============================================================
        // 描画（Env=H1 / Entry=M5 固定）
        // ============================================================
        // (#4) Running/Queued 二段フラグ方式に変更
        //      Running中に新たなリクエストが来た場合はQueuedフラグを立て、
        //      完了後に再描画を実行する。これにより描画スキップを防止する。
        private void HL_RedrawPivots()
        {
            if (Chart == null)
                return;

            if (_hlRedrawRunning)
            {
                _hlRedrawQueued = true;
                return;
            }

            _hlRedrawRunning = true;
            _hlRedrawQueued = false;

            try
            {
                BeginInvokeOnMainThread(() =>
                {
                    int drawCount = 0;
                    try
                    {
                        bool shouldForceClean = HL_強制クリーン頻度 > 0 &&
                            (HL_強制クリーン頻度 == 1 || (_hlClearCallCount % HL_強制クリーン頻度) == 0);
                        HL_ClearDrawings(shouldForceClean);

                        if (HL_ログ毎バー出す)
                        {
                            PrintLog("HL_BAR | M5={0} | H1={1}",
                                HL_FormatLastPivot(_m5Pivots),
                                HL_FormatLastPivot(_h1Pivots));
                        }

                        if (HL_EA内部Pivot描画)
                        {
                            if (_m5Pivots != null && _m5Pivots.Count >= 2)
                                drawCount += HL_DrawSeries("M5", new List<HL_Pivot>(_m5Pivots), Color.Red);

                            if (_h1Pivots != null && _h1Pivots.Count >= 2)
                                drawCount += HL_DrawSeries("H1", new List<HL_Pivot>(_h1Pivots), Color.White);
                        }

                        if (HL_ログ毎バー出す)
                            PrintLog("HL_REDRAW | DrawCount={0}", drawCount);
                    }
                    catch (Exception ex)
                    {
                        if (HL_ログ毎バー出す)
                            PrintLog("HL_REDRAW_ERROR: {0}", ex.Message);
                    }
                    finally
                    {
                        _hlRedrawRunning = false;

                        // (#4) キュー済みリクエストがあれば再描画
                        if (_hlRedrawQueued)
                        {
                            _hlRedrawQueued = false;
                            HL_RedrawPivots();
                        }
                    }
                });
            }
            catch
            {
                _hlRedrawRunning = false;
            }
        }

        private int HL_DrawSeries(string tf, List<HL_Pivot> pivots, Color lineColor)
        {
            int drawCount = 0;
            for (int i = 0; i < pivots.Count - 1; i++)
            {
                var a = pivots[i];
                var b = pivots[i + 1];
                string lineName = HL_BuildObjectName(tf, "LINE", a.Index, a.Side, b.Index, b.Side, i);

                Chart.DrawTrendLine(lineName, a.Time, a.Price, b.Time, b.Price, lineColor, 1, LineStyle.Solid);
                _hlDrawNames.Add(lineName);
                drawCount++;
            }

            double yOffset = Math.Max(0, HL_LabelYOffsetTicks) * Symbol.TickSize;

            for (int i = 0; i < pivots.Count; i++)
            {
                var p = pivots[i];
                string text = HL_GetPivotDisplayText(p);
                Color textColor = HL_GetPivotDisplayColor(p, text);

                double y = p.Side == 1 ? p.Price + yOffset : p.Price - yOffset;
                string textName = HL_BuildObjectName(tf, "TEXT", p.Index, p.Side, p.Index, p.Side, i);
                var t = Chart.DrawText(textName, text, p.Time, y, textColor);
                t.FontSize = HL_LabelFontSize;
                t.VerticalAlignment = VerticalAlignment.Center;
                t.HorizontalAlignment = HorizontalAlignment.Center;
                _hlDrawNames.Add(textName);
                drawCount++;
            }

            return drawCount;
        }

        private void HL_ClearDrawings(bool forceClean = false)
        {
            if (Chart == null)
            {
                _hlDrawNames.Clear();
                return;
            }

            int removedCount = 0;
            int failedCount = 0;
            int loggedFailedNameCount = 0;

            for (int i = 0; i < _hlDrawNames.Count; i++)
            {
                string name = _hlDrawNames[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                try
                {
                    Chart.RemoveObject(name);
                    removedCount++;
                }
                catch
                {
                    failedCount++;
                    if (HL_ログ毎バー出す && loggedFailedNameCount < HL_ForceCleanLogNameLimit)
                    {
                        PrintLog("HL_CLEAR_REMOVE_FAILED | Name={0}", name);
                        loggedFailedNameCount++;
                    }
                }
            }

            _hlDrawNames.Clear();
            _hlClearCallCount++;

            int forceRemovedCount = 0;
            int forceFailedCount = 0;
            if (forceClean)
                forceRemovedCount = HL_ForceCleanDrawingsByPrefix(out forceFailedCount);

            if (HL_ログ毎バー出す)
            {
                PrintLog(
                    "HL_CLEAR_DRAWINGS | RemovedCount={0} FailedCount={1} | PrefixRemovedCount={2} PrefixFailedCount={3} | Force={4} Call={5}",
                    removedCount,
                    failedCount,
                    forceRemovedCount,
                    forceFailedCount,
                    forceClean ? "Y" : "N",
                    _hlClearCallCount);
            }
        }

        private int HL_ForceCleanDrawingsByPrefix(out int failedCount)
        {
            failedCount = 0;
            if (Chart == null)
                return 0;

            var names = new List<string>(256);
            try
            {
                foreach (var obj in Chart.Objects)
                {
                    if (obj == null)
                        continue;

                    string name = obj.Name;
                    if (HL_IsDrawObjectTargetName(name))
                        names.Add(name);
                }
            }
            catch
            {
                // 列挙失敗時もEA継続
            }

            int removedCount = 0;
            int loggedRemovedNameCount = 0;
            int loggedFailedNameCount = 0;

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name))
                    continue;

                try
                {
                    Chart.RemoveObject(name);
                    removedCount++;

                    if (HL_ログ毎バー出す && loggedRemovedNameCount < HL_ForceCleanLogNameLimit)
                    {
                        PrintLog("HL_FORCE_CLEAN_REMOVED | Name={0}", name);
                        loggedRemovedNameCount++;
                    }
                }
                catch
                {
                    failedCount++;
                    if (HL_ログ毎バー出す && loggedFailedNameCount < HL_ForceCleanLogNameLimit)
                    {
                        PrintLog("HL_FORCE_CLEAN_FAILED | Name={0}", name);
                        loggedFailedNameCount++;
                    }
                }
            }

            return removedCount;
        }
    }
}
