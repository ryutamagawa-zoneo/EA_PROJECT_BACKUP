// ==================================================================================================
//  PivotClassic_DW_PP_R1R4_S1S4
//  Classic Pivot (PP / R1-R4 / S1-S4) with DAILY / WEEKLY switch
//  Enhancements:
//   - Draws ~1 year of historical pivot levels (step/stair style per period)
//   - Each period is drawn as a horizontal segment from period start to next period start
//   - Labels are shown only for the CURRENT period (right edge) to avoid clutter
//  Fixes retained:
//   - No Symbol.DigitsFormat usage
//   - No ChartText.X usage
// ==================================================================================================

using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class PivotClassic_DW_PP_R1R4_S1S4 : Indicator
    {
        public enum PivotPeriodType
        {
            Daily,
            Weekly
        }

        [Parameter("Pivot Period", DefaultValue = PivotPeriodType.Daily, Group = "Pivot")]
        public PivotPeriodType PivotPeriod { get; set; }

        [Parameter("Show PP", DefaultValue = true, Group = "Display")]
        public bool ShowPP { get; set; }

        [Parameter("Show R1-R3", DefaultValue = true, Group = "Display")]
        public bool ShowR1R3 { get; set; }

        [Parameter("Show S1-S3", DefaultValue = true, Group = "Display")]
        public bool ShowS1S3 { get; set; }

        [Parameter("Show R4", DefaultValue = true, Group = "Display")]
        public bool ShowR4 { get; set; }

        [Parameter("Show S4", DefaultValue = true, Group = "Display")]
        public bool ShowS4 { get; set; }

        [Parameter("Show Labels (Current Only)", DefaultValue = true, Group = "Display")]
        public bool ShowLabels { get; set; }

        [Parameter("History Days", DefaultValue = 365, MinValue = 30, MaxValue = 2000, Group = "History")]
        public int HistoryDays { get; set; }

        [Parameter("Line Thickness", DefaultValue = 1, MinValue = 1, MaxValue = 5, Group = "Style")]
        public int LineThickness { get; set; }

        [Parameter("Line Style", DefaultValue = LineStyle.Solid, Group = "Style")]
        public LineStyle LineStyle { get; set; }

        [Parameter("PP Color", DefaultValue = "Gold", Group = "Style")]
        public string PPColor { get; set; }

        [Parameter("R Color", DefaultValue = "Tomato", Group = "Style")]
        public string RColor { get; set; }

        [Parameter("S Color", DefaultValue = "DeepSkyBlue", Group = "Style")]
        public string SColor { get; set; }

        private Bars _pivotBars;

        private readonly string _prefix = "PivotClassic_DW_";

        private readonly List<string> _drawnObjectNames = new List<string>();

        private DateTime _lastRebuildPivotKeyTime = DateTime.MinValue;
        private bool _initialized;

        protected override void Initialize()
        {
            _pivotBars = GetPivotBars();
            _initialized = false;
        }

        public override void Calculate(int index)
        {
            if (index < 2)
                return;

            if (_pivotBars == null || _pivotBars.Count < 3)
                return;

            if (_pivotBars.SymbolName != SymbolName)
                _pivotBars = GetPivotBars();

            var pivotKeyTime = GetCurrentPivotKeyTime();
            if (pivotKeyTime == DateTime.MinValue)
                return;

            bool needRebuild = !_initialized || pivotKeyTime != _lastRebuildPivotKeyTime;

            if (needRebuild)
            {
                RebuildAllHistory();
                _lastRebuildPivotKeyTime = pivotKeyTime;
                _initialized = true;
            }
            else
            {
                // Keep current labels near right edge (only current period labels exist).
                UpdateCurrentLabelsOnly();
            }
        }

        private Bars GetPivotBars()
        {
            switch (PivotPeriod)
            {
                case PivotPeriodType.Weekly:
                    return MarketData.GetBars(TimeFrame.Weekly, SymbolName);
                case PivotPeriodType.Daily:
                default:
                    return MarketData.GetBars(TimeFrame.Daily, SymbolName);
            }
        }

        private DateTime GetCurrentPivotKeyTime()
        {
            if (_pivotBars == null || _pivotBars.Count < 2)
                return DateTime.MinValue;

            // current (forming) pivot bar open time
            return _pivotBars.OpenTimes[_pivotBars.Count - 1];
        }

        private void RebuildAllHistory()
        {
            RemoveAllDrawnObjects();

            if (_pivotBars == null || _pivotBars.Count < 3)
                return;

            DateTime earliest = Server.Time.AddDays(-HistoryDays);

            // We draw segments for pivot periods k where:
            // pivots for period k are computed from previous bar (k-1)
            // segment time span is [OpenTime[k], OpenTime[k+1]) for k < last
            // and [OpenTime[last], chart-right] for k == last (current forming)
            int lastPivotIndex = _pivotBars.Count - 1; // current forming
            int startK = FindFirstPivotIndexAtOrAfter(earliest);

            // Need k-1 valid
            if (startK < 1)
                startK = 1;

            // Ensure we have at least previous bar and current
            if (lastPivotIndex < 1)
                return;

            var ppColor = Color.FromName(PPColor);
            var rColor = Color.FromName(RColor);
            var sColor = Color.FromName(SColor);

            for (int k = startK; k <= lastPivotIndex; k++)
            {
                int prev = k - 1;

                double prevHigh = _pivotBars.HighPrices[prev];
                double prevLow = _pivotBars.LowPrices[prev];
                double prevClose = _pivotBars.ClosePrices[prev];

                if (double.IsNaN(prevHigh) || double.IsNaN(prevLow) || double.IsNaN(prevClose))
                    continue;

                // Classic Pivot
                double pp = (prevHigh + prevLow + prevClose) / 3.0;

                double r1 = 2.0 * pp - prevLow;
                double s1 = 2.0 * pp - prevHigh;

                double range = prevHigh - prevLow;

                double r2 = pp + range;
                double s2 = pp - range;

                double r3 = prevHigh + 2.0 * (pp - prevLow);
                double s3 = prevLow - 2.0 * (prevHigh - pp);

                double r4 = r3 + range;
                double s4 = s3 - range;

                DateTime t1 = _pivotBars.OpenTimes[k];
                DateTime t2 = GetPeriodEndTime(k, lastPivotIndex);

                // Draw stair-step segments (horizontal per period)
                if (ShowPP)
                    DrawSegment("PP", t1, t2, pp, ppColor, isCurrent: k == lastPivotIndex);

                if (ShowR1R3)
                {
                    DrawSegment("R1", t1, t2, r1, rColor, isCurrent: k == lastPivotIndex);
                    DrawSegment("R2", t1, t2, r2, rColor, isCurrent: k == lastPivotIndex);
                    DrawSegment("R3", t1, t2, r3, rColor, isCurrent: k == lastPivotIndex);
                }

                if (ShowS1S3)
                {
                    DrawSegment("S1", t1, t2, s1, sColor, isCurrent: k == lastPivotIndex);
                    DrawSegment("S2", t1, t2, s2, sColor, isCurrent: k == lastPivotIndex);
                    DrawSegment("S3", t1, t2, s3, sColor, isCurrent: k == lastPivotIndex);
                }

                if (ShowR4)
                    DrawSegment("R4", t1, t2, r4, rColor, isCurrent: k == lastPivotIndex);

                if (ShowS4)
                    DrawSegment("S4", t1, t2, s4, sColor, isCurrent: k == lastPivotIndex);
            }
        }

        private int FindFirstPivotIndexAtOrAfter(DateTime earliest)
        {
            // Linear scan is fine (daily bars ~ few thousand). Keeps code simple/stable.
            for (int i = 0; i < _pivotBars.Count; i++)
            {
                if (_pivotBars.OpenTimes[i] >= earliest)
                    return i;
            }
            return 0;
        }

        private DateTime GetPeriodEndTime(int k, int lastPivotIndex)
        {
            if (k < lastPivotIndex)
                return _pivotBars.OpenTimes[k + 1];

            // For current forming period: use chart right edge time
            int rightIndex = GetRightEdgeBarIndex();
            if (rightIndex < 0)
                rightIndex = Bars.Count - 1;
            if (rightIndex < 0)
                return Server.Time;

            return Bars.OpenTimes[rightIndex];
        }

        private void DrawSegment(string level, DateTime t1, DateTime t2, double price, Color color, bool isCurrent)
        {
            // Name per segment: level + start date key => allows stair-step history
            string key = t1.ToString("yyyyMMddHHmm");
            string lineName = _prefix + level + "_Line_" + key;

            var existing = Chart.FindObject(lineName) as ChartTrendLine;
            if (existing == null)
            {
                var line = Chart.DrawTrendLine(lineName, t1, price, t2, price, color);
                line.Thickness = LineThickness;
                line.LineStyle = LineStyle;
                _drawnObjectNames.Add(lineName);
            }
            else
            {
                existing.Time1 = t1;
                existing.Y1 = price;
                existing.Time2 = t2;
                existing.Y2 = price;
                existing.Color = color;
                existing.Thickness = LineThickness;
                existing.LineStyle = LineStyle;
            }

            // Labels only for CURRENT period to avoid excessive clutter/objects
            if (ShowLabels && isCurrent)
            {
                string labelName = _prefix + level + "_Label_Current";
                DrawOrUpdateCurrentLabel(labelName, level, price, color);
            }
        }

        private void DrawOrUpdateCurrentLabel(string name, string shortLabel, double price, Color color)
        {
            // Remove & redraw to reposition at right edge (ChartText has no X property in this build)
            RemoveChartObject(name);

            int rightIndex = GetRightEdgeBarIndex();
            if (rightIndex < 0)
                rightIndex = Bars.Count - 1;
            if (rightIndex < 0)
                return;

            DateTime t = Bars.OpenTimes[rightIndex];
            string text = shortLabel + "  " + FormatPrice(price);

            Chart.DrawText(name, text, t, price, color);
            _drawnObjectNames.Add(name);
        }

        private void UpdateCurrentLabelsOnly()
        {
            if (!ShowLabels)
                return;

            // Easiest and reliable with this cTrader build:
            // remove and rebuild current labels using latest pivot values already drawn (lines remain).
            // We rebuild only labels by forcing a small label refresh using the current period pivots.
            // To ensure correctness without caching complexities, rebuild all history only on period change,
            // and refresh labels here by re-drawing label objects based on the current period pivots.

            // Find current period pivot index and compute pivots from previous bar
            int lastPivotIndex = _pivotBars.Count - 1;
            if (lastPivotIndex < 1)
                return;

            int prev = lastPivotIndex - 1;

            double prevHigh = _pivotBars.HighPrices[prev];
            double prevLow = _pivotBars.LowPrices[prev];
            double prevClose = _pivotBars.ClosePrices[prev];

            if (double.IsNaN(prevHigh) || double.IsNaN(prevLow) || double.IsNaN(prevClose))
                return;

            double pp = (prevHigh + prevLow + prevClose) / 3.0;

            double r1 = 2.0 * pp - prevLow;
            double s1 = 2.0 * pp - prevHigh;

            double range = prevHigh - prevLow;

            double r2 = pp + range;
            double s2 = pp - range;

            double r3 = prevHigh + 2.0 * (pp - prevLow);
            double s3 = prevLow - 2.0 * (prevHigh - pp);

            double r4 = r3 + range;
            double s4 = s3 - range;

            var ppColor = Color.FromName(PPColor);
            var rColor = Color.FromName(RColor);
            var sColor = Color.FromName(SColor);

            // Remove current labels then redraw only those enabled
            RemoveChartObject(_prefix + "PP_Label_Current");
            RemoveChartObject(_prefix + "R1_Label_Current");
            RemoveChartObject(_prefix + "R2_Label_Current");
            RemoveChartObject(_prefix + "R3_Label_Current");
            RemoveChartObject(_prefix + "R4_Label_Current");
            RemoveChartObject(_prefix + "S1_Label_Current");
            RemoveChartObject(_prefix + "S2_Label_Current");
            RemoveChartObject(_prefix + "S3_Label_Current");
            RemoveChartObject(_prefix + "S4_Label_Current");

            if (ShowPP)
                DrawOrUpdateCurrentLabel(_prefix + "PP_Label_Current", "PP", pp, ppColor);

            if (ShowR1R3)
            {
                DrawOrUpdateCurrentLabel(_prefix + "R1_Label_Current", "R1", r1, rColor);
                DrawOrUpdateCurrentLabel(_prefix + "R2_Label_Current", "R2", r2, rColor);
                DrawOrUpdateCurrentLabel(_prefix + "R3_Label_Current", "R3", r3, rColor);
            }

            if (ShowS1S3)
            {
                DrawOrUpdateCurrentLabel(_prefix + "S1_Label_Current", "S1", s1, sColor);
                DrawOrUpdateCurrentLabel(_prefix + "S2_Label_Current", "S2", s2, sColor);
                DrawOrUpdateCurrentLabel(_prefix + "S3_Label_Current", "S3", s3, sColor);
            }

            if (ShowR4)
                DrawOrUpdateCurrentLabel(_prefix + "R4_Label_Current", "R4", r4, rColor);

            if (ShowS4)
                DrawOrUpdateCurrentLabel(_prefix + "S4_Label_Current", "S4", s4, sColor);
        }

        private int GetRightEdgeBarIndex()
        {
            int last = Bars.Count - 1;
            if (last < 0)
                return -1;

            try
            {
                int right = Chart.LastVisibleBarIndex;
                if (right >= 0 && right <= last)
                    return right;
            }
            catch
            {
                // ignored
            }

            return last;
        }

        private string FormatPrice(double price)
        {
            return price.ToString("F" + Symbol.Digits);
        }

        private void RemoveAllDrawnObjects()
        {
            for (int i = 0; i < _drawnObjectNames.Count; i++)
            {
                RemoveChartObject(_drawnObjectNames[i]);
            }
            _drawnObjectNames.Clear();
        }

        private void RemoveChartObject(string name)
        {
            var obj = Chart.FindObject(name);
            if (obj != null)
                Chart.RemoveObject(name);
        }
    }
}
