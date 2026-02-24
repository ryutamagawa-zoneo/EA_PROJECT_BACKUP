// ============================================================
// CODE NAME (Project Constitution compliant)
// ============================================================
// BASE: EMA_M5_ALL_DAY_020_079_XAUUSD_M5
// THIS: EA_BASE_HL_MIX
// INTEGRATED: 079 (BASE) + 019 (HighLow Trend + Drawing)
// ============================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class EA_BASE_HL_MIX : Robot
    {
        // ============================================================
        // CODE NAME (fixed)
        // ============================================================
        private const string CODE_NAME = "EA_BASE_HL_MIX";
        private const string BOT_LABEL = "EA_BASE_HL_MIX";

// ============================================================
// 多重エントリー防止ゲート（MaxPositions 強制）
// ============================================================
private bool _entryInProgress = false;
private int _lastEntryBarIndex = int.MinValue;
private DateTime _lastEntryAttemptUtc = DateTime.MinValue;
private DateTime _entryGuardUntilUtc = DateTime.MinValue; // 発注直後の更新遅延対策クールダウン


private long ClampVolumeByMaxLots(long volumeInUnits)
{
    try
    {
        if (MaxLotsCap <= 0.0)
            return volumeInUnits;

        long maxUnits = (long)Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MaxLotsCap), RoundingMode.Down);
        if (maxUnits > 0 && volumeInUnits > maxUnits)
            return maxUnits;

        return volumeInUnits;
    }
    catch
    {
        // 何かあっても安全側（元の値）で返す
        return volumeInUnits;
    }
}

private bool ShouldBlockNewEntry(TradeType type, int decisionBarIndex, string reasonTag)
{
    try
    {
        // 既存ポジション数（Label + Symbol）
        if (MaxPositions >= 1)
        {
            int posCount = 0;
            try
            {
                var posArr = Positions.FindAll(BOT_LABEL, SymbolName);
                posCount = (posArr != null) ? posArr.Length : 0;
            }
            catch { posCount = 0; }

            if (posCount >= MaxPositions)
            {
                EmitExecuteSkipJsonl("MAX_POSITIONS", type, decisionBarIndex, null, null, null, null);
                return true;
            }
        }

        // 保留注文がある場合も新規発注を拒否（Label + Symbol）
        int pendCount = 0;
        try
        {
            pendCount = PendingOrders.Count(o => o != null && o.Label == BOT_LABEL && o.SymbolName == SymbolName);
        }
        catch { pendCount = 0; }

        if (pendCount > 0)
        {
            EmitExecuteSkipJsonl("PENDING_EXISTS", type, decisionBarIndex, null, null, null, null);
            return true;
        }

        // 発注中（同Tick/同Timer多重）を拒否
        if (_entryInProgress)
        {
            EmitExecuteSkipJsonl("ENTRY_IN_PROGRESS", type, decisionBarIndex, null, null, null, null);
            return true;
        }

        // 発注直後クールダウン（反映遅延対策）
        var nowUtc = Server.Time; // Robot(TimeZone=UTC)
        if (_entryGuardUntilUtc != DateTime.MinValue && nowUtc < _entryGuardUntilUtc)
        {
            EmitExecuteSkipJsonl("ENTRY_GUARD_COOLDOWN", type, decisionBarIndex, null, null, null, null);
            return true;
        }

        // 同一バーでの多重発注を拒否
        if (decisionBarIndex == _lastEntryBarIndex)
        {
            EmitExecuteSkipJsonl("DUP_BAR", type, decisionBarIndex, null, null, null, null);
            return true;
        }

        // 短時間（ミリ秒）多重発注を拒否
        if (_lastEntryAttemptUtc != DateTime.MinValue)
        {
            var dt = nowUtc - _lastEntryAttemptUtc;
            if (dt.TotalMilliseconds >= 0 && dt.TotalMilliseconds < 800)
            {
                EmitExecuteSkipJsonl("DUP_TIME", type, decisionBarIndex, null, null, null, null);
                return true;
            }
        }

        return false;
    }
    catch
    {
        // 何かあっても「止める」側（事故防止）
        EmitExecuteSkipJsonl("ENTRY_GUARD_ERROR", type, decisionBarIndex, null, null, null, null);
        return true;
    }
}

private void MarkEntryAttempt(int decisionBarIndex, DateTime nowUtc)
{
    _lastEntryBarIndex = decisionBarIndex;
    _lastEntryAttemptUtc = nowUtc;
    _entryGuardUntilUtc = nowUtc.AddMilliseconds(800);
}


        private string GetCodeNumberTag()
        {
            // CODE_NAME から「020_068」等のコード番号タグを抽出する（ファイル名短縮用）
            try
            {
                var parts = CODE_NAME.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 1 < parts.Length; i++)
                {
                    string a = parts[i];
                    string b = parts[i + 1];
                    if (a.Length == 3 && b.Length == 3 && a.All(char.IsDigit) && b.All(char.IsDigit))
                        return a + "_" + b;
                }
            }
            catch { }
            return "000_000";
        }

        const int EMA_PERIOD_FIXED = 20;
        private const int SLイベント優先順位_固定値 = 1;

        // ============================================================
        // SYMBOL 正規化（XAUUSD=GOLD / XAGUSD=SILVER 対応）
        //  - 目的：ブローカー差（GOLD, SILVER表記）でも同一商品として稼働させる
        //  - 影響範囲：SymbolName 比較 + シンボルカテゴリ/スケール判定
        // ============================================================
        private string NormalizeSymbolName(string name)
        {
            // 観測のみ：後工程突合用の正規化
            // 要件：XAU系は "XAUUSD" / "GOLD" / prefix/suffix付き（例: XAUUSDm, GOLD., mGOLD 等）を同一扱いで symbol_norm="XAUUSD"
            //       XAG系は "XAGUSD" / "SILVER" / prefix/suffix付きも symbol_norm="XAGUSD"
            //       その他は大文字Trimのまま返す（取引ロジックには影響させない）
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string raw = name.Trim();
            string up = raw.ToUpperInvariant();

            // 英数字だけに畳み込み（"GOLD.", "XAUUSDm" 等を安定判定）
            var sb = new StringBuilder(up.Length);
            for (int i = 0; i < up.Length; i++)
            {
                char c = up[i];
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
            }
            string canon = sb.ToString();

            if (canon.Contains("XAUUSD") || canon.Contains("GOLD"))
                return "XAUUSD";

            if (canon.Contains("XAGUSD") || canon.Contains("SILVER"))
                return "XAGUSD";

            return up;
        }

        private bool IsSameSymbolNormalized(string a, string b)
        {
            return NormalizeSymbolName(a) == NormalizeSymbolName(b);
        }


        // ============================================================
        // EMA_SNAPSHOT JSONL output helpers (pure JSONL; no prefix)
        // ============================================================
        private string BuildEmaSnapshotLogPath()
        {
            // 形式混在/途切れ防止のため、スナップショット専用JSONLに追記する（観測のみ）
            // PROレポート「はい」の場合は、PROセッションフォルダへ同梱する
            const string fixedDir = @"D:\ChatGPT EA Development\プロジェクト\保管庫";

            string dir = fixedDir;
            if (EnableProReport)
            {
                string proDir = ResolveProOutputDirectory();
                if (!string.IsNullOrWhiteSpace(proDir))
                    dir = proDir;
            }

            if (string.IsNullOrWhiteSpace(dir))
                dir = ".";

            string file = string.Format(CultureInfo.InvariantCulture, "{0}_EMA_SNAPSHOT.jsonl", CODE_NAME);
            return Path.Combine(dir, file);
        }

        private void EnsureDirectoryForFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return;

                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch
            {
                // 観測ログ用。例外で取引ロジックを止めない。
            }
        }

        private void AppendJsonlLine(string filePath, string jsonLine)
        {
            // PROレポートOFF時は書き込みしない（3引数版との整合）
            if (!EnableProReport)
                return;

            // 途中改行/途切れ防止：必ず1行=1JSONで追記する
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(jsonLine))
                return;

            string line = jsonLine.Replace("\r", "").Replace("\n", "") + "\n";

            try
            {
                lock (_emaSnapshotFileLock)
                {
                    File.AppendAllText(filePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // 観測ログ用。例外で取引ロジックを止めない。
            }
        }

        private static string JsonBool(bool value) => value ? "true" : "false";

        private static string JsonDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "null";
        }

        private static string JsonEsc(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string PlannedTypeText(TradeType? type)
        {
            if (!type.HasValue) return "None";
            return type.Value == TradeType.Buy ? "Buy" : "Sell";
        }

        private static string DirStateText(LineSideState state)
        {
            if (state == LineSideState.Above) return "Above";
            if (state == LineSideState.Below) return "Below";
            return "Neutral";
        }

        private static string ResolveEntryDecisionBlockReason(bool entryAllowed, string reasonTag)
        {
            if (entryAllowed)
                return "NONE";

            string tag = (reasonTag ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(tag))
                return "OTHER";

            if (tag.Contains("TIME"))
                return "TIME_FILTER";

            if (tag.Contains("NEWS"))
                return "NEWS_FILTER";

            if (tag.Contains("DIR"))
                return "DIR_FILTER";

            if (tag.Contains("DIST") || tag.Contains("SPREAD") || tag.Contains("SLDIST") || tag.Contains("MIN_SL"))
                return "DIST_FILTER";

            if (tag.Contains("HOLD") || tag.Contains("COOLDOWN") || tag.Contains("REENTRY") || tag.Contains("PENDING") || tag.Contains("STATE"))
                return "STATE_HOLD";

            return "OTHER";
        }

        private long SafeBarOpenTimeUtcMs(int index)
        {
            if (Bars == null || index < 0 || index >= Bars.Count)
                return 0L;
            return ToUnixTimeMillisecondsUtc(Bars.OpenTimes[index]);
        }

        private void StopEntryDecisionLogging(string reason, Exception ex = null)
        {
            _entryDecisionLogStopped = true;
            _entryDecisionLogEnabled = false;
            if (ex == null)
                Print("ENTRY_DECISION_LOG_STOP | CodeName={0} | Reason={1}", CODE_NAME, reason);
            else
                Print("ENTRY_DECISION_LOG_STOP | CodeName={0} | Reason={1} | Error={2}", CODE_NAME, reason, ex.Message);
        }

        private void InitializeEntryDecisionLogging()
        {
            _entryDecisionLogEnabled = false;
            _entryDecisionLogStopped = false;
            _lastEntryDecisionBarIndex = int.MinValue;
            _entryDecisionJsonlPath = null;
            _pendingStateJsonlPath = null;

            if (!EnableProReport)
                return;

            try
            {
                string _dir = ResolveProOutputDirectory();
                    if (string.IsNullOrWhiteSpace(_dir))
                        throw new Exception("PRO_OUTPUT_DIR_NULL");
            }
            catch (Exception ex)
            {
                StopEntryDecisionLogging("DIR_CREATE_FAIL", ex);
                return;
            }

            _entryDecisionJsonlPath = Path.Combine(ResolveProOutputDirectory(), "ENTRY_DECISION_" + GetCodeNumberTag() + ".jsonl");
            _pendingStateJsonlPath = Path.Combine(ResolveProOutputDirectory(), "PENDING_STATE_" + GetCodeNumberTag() + ".jsonl");
            _entryDecisionLogEnabled = true;

            Print("ENTRY_DECISION_LOG_INIT | CodeName={0} | DecisionPath={1} | PendingPath={2}",
                CODE_NAME, _entryDecisionJsonlPath, _pendingStateJsonlPath);
        }

        private void AppendEntryDecisionJsonlLine(string filePath, string jsonLine, string eventName)
        {
            if (!_entryDecisionLogEnabled || _entryDecisionLogStopped)
                return;

            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(jsonLine))
                return;

            string line = jsonLine.Replace("\r", "").Replace("\n", "") + "\n";

            try
            {
                lock (_entryDecisionLogFileLock)
                {
                    File.AppendAllText(filePath, line, new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                StopEntryDecisionLogging("WRITE_FAIL_" + eventName, ex);
            }
        }

        private void StopObservationLogging(string reason, Exception ex = null)
        {
            _observationLogStopped = true;
            _observationLogEnabled = false;
            if (ex == null)
                Print("OBS_JSONL_LOG_STOP | CodeName={0} | Reason={1}", CODE_NAME, reason);
            else
                Print("OBS_JSONL_LOG_STOP | CodeName={0} | Reason={1} | Error={2}", CODE_NAME, reason, ex.Message);
        }

        private string BuildObservationLogPath(string prefix)
        {
            string stamp = string.IsNullOrWhiteSpace(_proRunId)
                ? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                : _proRunId;

            return Path.Combine(ResolveProOutputDirectory(), prefix + "_" + GetCodeNumberTag() + ".jsonl");
        }

        private void InitializeObservationJsonlLogging()
        {
            _observationLogEnabled = false;
            _observationLogStopped = false;
            _eaInitJsonlPath = null;
            _barCloseJsonlPath = null;
            _executeJsonlPath = null;
            _atrEnvEvalJsonlPath = null;
            _lastBarCloseIndex = int.MinValue;

            if (!EnableProReport)
                return;

            try
            {
                string _dir = ResolveProOutputDirectory();
                    if (string.IsNullOrWhiteSpace(_dir))
                        throw new Exception("PRO_OUTPUT_DIR_NULL");
            }
            catch (Exception ex)
            {
                StopObservationLogging("DIR_CREATE_FAIL", ex);
                return;
            }

            _eaInitJsonlPath = BuildObservationLogPath("EA_INIT");
            _barCloseJsonlPath = BuildObservationLogPath("BAR_CLOSE");
            _executeJsonlPath = BuildObservationLogPath("EXECUTE");
            _atrEnvEvalJsonlPath = Path.Combine(ResolveProOutputDirectory(), "ATR_ENV_" + GetCodeNumberTag() + ".jsonl");
            _observationLogEnabled = true;

            Print("OBS_JSONL_LOG_INIT | CodeName={0} | EaInitPath={1} | BarClosePath={2} | ExecutePath={3}",
                CODE_NAME, _eaInitJsonlPath, _barCloseJsonlPath, _executeJsonlPath);
        }

        private void AppendJsonlLine(string filePath, string jsonLine, string eventName)
        {
            if (!_observationLogEnabled || _observationLogStopped)
                return;

            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(jsonLine))
                return;

            string line = jsonLine.Replace("\r", "").Replace("\n", "") + "\n";

            try
            {
                lock (_observationJsonlFileLock)
                {
                    File.AppendAllText(filePath, line, new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                StopObservationLogging("WRITE_FAIL_" + eventName, ex);
            }
        }

        private Dictionary<string, object> BuildCommonEventRoot(string eventName)
        {
            var root = new Dictionary<string, object>();
            root["event"] = eventName;
            root["code_name"] = CODE_NAME;
            root["run_id"] = string.IsNullOrWhiteSpace(_proRunId) ? "" : _proRunId;
            root["symbol_norm"] = NormalizeSymbolName(SymbolName);
            root["timeframe"] = (Bars != null ? Bars.TimeFrame.ToString() : "NA");
            return root;
        }

        private Dictionary<string, object> BuildEaInitParamsObject()
        {
            var paramObj = new Dictionary<string, object>();
            var props = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(x => x.MetadataToken)
                .ToArray();

            foreach (var p in props)
            {
                var attrs = p.GetCustomAttributes(typeof(ParameterAttribute), true);
                if (attrs == null || attrs.Length == 0)
                    continue;

                object val = null;
                try
                {
                    val = p.GetValue(this, null);
                }
                catch
                {
                    val = null;
                }

                string valueText = (val == null ? null : val.ToString());
                paramObj[p.Name] = MaskSensitiveParameterValue(p.Name, valueText);
            }

            return paramObj;
        }

        private string MaskSensitiveParameterValue(string propertyName, string valueText)
        {
            if (string.IsNullOrEmpty(valueText))
                return valueText;

            if (string.Equals(propertyName, "CalendarApiKey", StringComparison.Ordinal))
                return "REDACTED";

            return valueText;
        }

        private void EmitEaInitJsonl()
        {
            if (!_observationLogEnabled || _observationLogStopped || !EnableProReport)
                return;

            try
            {
                DateTime utcNow = UtcNow();

                if (!EnableTradingWindowFilter)
                {
                    UpdateStopOverlay(false, "", utcNow, null);
                }

                var root = BuildCommonEventRoot("EA_INIT");
                root["time_utc_ms"] = ToUnixTimeMillisecondsUtc(utcNow);
                root["symbol_raw"] = SymbolName ?? string.Empty;

                var flags = new Dictionary<string, object>();
                flags["EnableProReport"] = EnableProReport;
                flags["EnableEmaDirectionFilter"] = EnableEmaDirectionFilter;
                flags["EnableTradingWindowFilter"] = EnableTradingWindowFilter;
                root["flags"] = flags;

                root["params"] = BuildEaInitParamsObject();
                AppendJsonlLine(_eaInitJsonlPath, SimpleJson(root), "EA_INIT");
            }
            catch (Exception ex)
            {
                Print("EA_INIT_LOG_ERROR | CodeName={0} | Error={1}", CODE_NAME, ex.Message);
            }
        }

        private void EmitBarCloseJsonl(int barIndex)
        {
            if (!_observationLogEnabled || _observationLogStopped || !EnableProReport)
                return;

            if (Bars == null || barIndex < 0 || barIndex >= Bars.Count)
                return;

            if (barIndex == _lastBarCloseIndex)
                return;

            _lastBarCloseIndex = barIndex;

            var root = BuildCommonEventRoot("BAR_CLOSE");
            root["bar_index"] = barIndex;
            root["bar_time_utc_ms"] = SafeBarOpenTimeUtcMs(barIndex);
            root["open"] = Bars.OpenPrices[barIndex];
            root["high"] = Bars.HighPrices[barIndex];
            root["low"] = Bars.LowPrices[barIndex];
            root["close"] = Bars.ClosePrices[barIndex];

            if (Symbol != null && Symbol.PipSize > 0.0)
            {
                double spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
                if (spreadPips >= 0.0)
                    root["spread"] = spreadPips;
            }

            AppendJsonlLine(_barCloseJsonlPath, SimpleJson(root), "BAR_CLOSE");
        }

        private void EmitExecuteSkipJsonl(string reason, TradeType plannedType, int barIndex, double? close1, double? ema1, double? bid, double? ask)
        {
            if (!_observationLogEnabled || _observationLogStopped || !EnableProReport)
                return;

            var root = BuildCommonEventRoot("EXECUTE_SKIP");
            root["bar_time_utc_ms"] = SafeBarOpenTimeUtcMs(barIndex);
            root["plannedType"] = PlannedTypeText(plannedType);
            root["reason"] = string.IsNullOrWhiteSpace(reason) ? "NA" : reason;
            root["close1"] = close1.HasValue ? (object)close1.Value : null;
            root["ema1"] = ema1.HasValue ? (object)ema1.Value : null;

            if (bid.HasValue && ask.HasValue && Symbol != null && Symbol.PipSize > 0.0)
                root["spread"] = (ask.Value - bid.Value) / Symbol.PipSize;

            AppendJsonlLine(_executeJsonlPath, SimpleJson(root), "EXECUTE_SKIP");
        }

        
        private void EmitAtrEnvEvalJsonl(TradeType plannedType, int decisionBarIndex, double atrUserPrice, double atrInternalPips, double atrUserPips)
        {
            if (!EnableProReport)
                return;

            if (string.IsNullOrWhiteSpace(_atrEnvEvalJsonlPath))
                return;

            try
            {
                var root = new Dictionary<string, object>();
                root["event"] = "ATR_ENV_EVAL";

                // bar time (UTC ms)
                long barOpenUtcMs = (Bars != null && decisionBarIndex >= 0 && decisionBarIndex < Bars.Count)
                    ? ToUnixTimeMillisecondsUtc(Bars.OpenTimes[decisionBarIndex])
                    : 0L;

                long barCloseUtcMs = 0L;
                if (Bars != null && decisionBarIndex >= 0 && decisionBarIndex + 1 < Bars.Count)
                    barCloseUtcMs = ToUnixTimeMillisecondsUtc(Bars.OpenTimes[decisionBarIndex + 1]);
                else
                    barCloseUtcMs = barOpenUtcMs;

                root["bar_open_utc_ms"] = barOpenUtcMs;
                root["bar_close_utc_ms"] = barCloseUtcMs;

                root["symbol"] = SymbolName ?? string.Empty;
                root["timeframe"] = (TimeFrame != null) ? TimeFrame.ToString() : string.Empty;
                root["plannedType"] = plannedType.ToString();

                root["digits"] = (Symbol != null) ? Symbol.Digits : 0;
                root["point"] = (Symbol != null) ? Symbol.TickSize : 0.0;
                root["pipsScale"] = _pipsScale;

                // ATR (user scale)
                root["atr_user_price"] = atrUserPrice;
                root["atr_user_pips"] = atrUserPips;

                root["min_pips"] = AtrEnvMinPips;
                root["max_pips"] = AtrEnvMaxPips;

                string result = "PASS";
                if (AtrEnvMinPips > 0.0 && atrUserPips > 0.0 && atrUserPips < AtrEnvMinPips)
                    result = "BLOCK_MIN";
                else if (AtrEnvMaxPips > 0.0 && atrUserPips > 0.0 && atrUserPips > AtrEnvMaxPips)
                    result = "BLOCK_MAX";

                root["atr_env_result"] = result;

                // ATR shifts (price/pips) - align with the same bar indexing used for the gate
                AppendAtrShift(root, decisionBarIndex, 0);
                AppendAtrShift(root, decisionBarIndex - 1, 1);
                AppendAtrShift(root, decisionBarIndex - 2, 2);

                // OHLC / prevClose / TR (manual) shifts
                AppendOhlcShift(root, decisionBarIndex, 0);
                AppendOhlcShift(root, decisionBarIndex - 1, 1);
                AppendOhlcShift(root, decisionBarIndex - 2, 2);

                AppendJsonlLine(_atrEnvEvalJsonlPath, SimpleJson(root));
            }
            catch
            {
                // no-throw (log only)
            }
        }

        private void AppendAtrShift(Dictionary<string, object> root, int barIndex, int shift)
        {
            try
            {
                double atrPrice = 0.0;
                if (_atrEnvGate != null && _atrEnvGate.Result != null && barIndex >= 0 && Bars != null && barIndex < Bars.Count)
                    atrPrice = _atrEnvGate.Result[barIndex];

                double atrInternalPips = (Symbol != null && Symbol.PipSize > 0.0) ? atrPrice / Symbol.PipSize : 0.0;
                double atrUserPips = InternalPipsToInputPips(atrInternalPips);

                root["atr_shift" + shift + "_price"] = atrPrice;
                root["atr_shift" + shift + "_pips"] = atrUserPips;
            }
            catch
            {
                root["atr_shift" + shift + "_price"] = 0.0;
                root["atr_shift" + shift + "_pips"] = 0.0;
            }
        }

        private void AppendOhlcShift(Dictionary<string, object> root, int barIndex, int shift)
        {
            double o = 0.0, h = 0.0, l = 0.0, c = 0.0;
            double prevClose = 0.0;
            double trManual = 0.0;

            try
            {
                if (Bars == null || barIndex < 0 || barIndex >= Bars.Count)
                {
                    root["ohlc_shift" + shift + "_open"] = 0.0;
                    root["ohlc_shift" + shift + "_high"] = 0.0;
                    root["ohlc_shift" + shift + "_low"] = 0.0;
                    root["ohlc_shift" + shift + "_close"] = 0.0;

                    root["prevClose_shift" + shift] = 0.0;
                    root["tr_manual_shift" + shift] = 0.0;
                    return;
                }

                o = Bars.OpenPrices[barIndex];
                h = Bars.HighPrices[barIndex];
                l = Bars.LowPrices[barIndex];
                c = Bars.ClosePrices[barIndex];

                int prevIndex = barIndex - 1;
                if (prevIndex >= 0 && prevIndex < Bars.Count)
                    prevClose = Bars.ClosePrices[prevIndex];

                double tr1 = h - l;
                double tr2 = Math.Abs(h - prevClose);
                double tr3 = Math.Abs(l - prevClose);
                trManual = Math.Max(tr1, Math.Max(tr2, tr3));

                root["ohlc_shift" + shift + "_open"] = o;
                root["ohlc_shift" + shift + "_high"] = h;
                root["ohlc_shift" + shift + "_low"] = l;
                root["ohlc_shift" + shift + "_close"] = c;

                root["prevClose_shift" + shift] = prevClose;
                root["tr_manual_shift" + shift] = trManual;
            }
            catch
            {
                root["ohlc_shift" + shift + "_open"] = 0.0;
                root["ohlc_shift" + shift + "_high"] = 0.0;
                root["ohlc_shift" + shift + "_low"] = 0.0;
                root["ohlc_shift" + shift + "_close"] = 0.0;

                root["prevClose_shift" + shift] = 0.0;
                root["tr_manual_shift" + shift] = 0.0;
            }
        }

private void EmitExecuteSendJsonl(TradeType plannedType, int barIndex, long volumeUnits, double slPipsToSend, double tpPipsToSend)
        {
            if (!_observationLogEnabled || _observationLogStopped || !EnableProReport)
                return;

            var root = BuildCommonEventRoot("EXECUTE_SEND");
            root["bar_time_utc_ms"] = SafeBarOpenTimeUtcMs(barIndex);
            root["plannedType"] = PlannedTypeText(plannedType);
            root["volumeUnits"] = volumeUnits;
            root["slPipsToSend"] = slPipsToSend;
            root["tpPipsToSend"] = tpPipsToSend;
            AppendJsonlLine(_executeJsonlPath, SimpleJson(root), "EXECUTE_SEND");
        }

        private void EmitExecuteResultJsonl(TradeType plannedType, int barIndex, TradeResult result)
        {
            if (!_observationLogEnabled || _observationLogStopped || !EnableProReport)
                return;

            bool success = result != null && result.IsSuccessful && result.Position != null;

            var root = BuildCommonEventRoot("EXECUTE_RESULT");
            root["bar_time_utc_ms"] = SafeBarOpenTimeUtcMs(barIndex);
            root["plannedType"] = PlannedTypeText(plannedType);
            root["success"] = success;
            root["positionId"] = success ? (object)result.Position.Id : null;
            root["error"] = success ? null : (result == null ? "NULL_RESULT" : result.Error.ToString());
            AppendJsonlLine(_executeJsonlPath, SimpleJson(root), "EXECUTE_RESULT");
        }

        // JSONL sample lines (reference only; runtime output is pure JSONL, 1 line = 1 event):
        // {"event":"EA_INIT","code_name":"EMA_M5_ALL_DAY_020_074_XAUUSD_M5","run_id":"20260210_120000_000","symbol_raw":"XAUUSD","symbol_norm":"XAUUSD","timeframe":"Minute5","flags":{"EnableProReport":true},"params":{"RiskPercent":"0.50"}}
        // {"event":"BAR_CLOSE","code_name":"EMA_M5_ALL_DAY_020_074_XAUUSD_M5","run_id":"20260210_120000_000","symbol_norm":"XAUUSD","timeframe":"Minute5","bar_index":1234,"bar_time_utc_ms":1739145600000,"open":2910.1,"high":2912.4,"low":2909.8,"close":2911.6,"spread":2.1}
        // {"event":"EXECUTE_SKIP","code_name":"EMA_M5_ALL_DAY_020_074_XAUUSD_M5","run_id":"20260210_120000_000","symbol_norm":"XAUUSD","timeframe":"Minute5","bar_time_utc_ms":1739145600000,"plannedType":"Buy","reason":"SPREAD_BLOCK","close1":2911.6,"ema1":2911.2,"spread":3.4}
        // {"event":"EXECUTE_SEND","code_name":"EMA_M5_ALL_DAY_020_074_XAUUSD_M5","run_id":"20260210_120000_000","symbol_norm":"XAUUSD","timeframe":"Minute5","bar_time_utc_ms":1739145600000,"plannedType":"Buy","volumeUnits":1000,"slPipsToSend":35.0,"tpPipsToSend":35.0}
        // {"event":"EXECUTE_RESULT","code_name":"EMA_M5_ALL_DAY_020_074_XAUUSD_M5","run_id":"20260210_120000_000","symbol_norm":"XAUUSD","timeframe":"Minute5","bar_time_utc_ms":1739145600000,"plannedType":"Buy","success":true,"positionId":12345678,"error":null}

        private void EmitEntryDecisionAndPendingState(
            int i1,
            int i2,
            double? close1,
            double? close2,
            double? ema1,
            double? ema2,
            bool crossUp,
            bool crossDown,
            TradeType? plannedType,
            bool entryAllowed,
            string reasonTag,
            bool rrPendingUsed,
            bool reapproachConsumed)
        {
            if (!_entryDecisionLogEnabled || _entryDecisionLogStopped || !EnableProReport)
                return;

            if (i1 == _lastEntryDecisionBarIndex)
                return;

            _lastEntryDecisionBarIndex = i1;

            long barTimeUtcMs = SafeBarOpenTimeUtcMs(i1);
            string safeReason = string.IsNullOrWhiteSpace(reasonTag) ? "NA" : reasonTag;
            string blockReason = ResolveEntryDecisionBlockReason(entryAllowed, safeReason);

            string entryJson =
                "{"
                + "\"event\":\"ENTRY_DECISION\","
                + "\"bar_time_utc_ms\":" + barTimeUtcMs.ToString(CultureInfo.InvariantCulture) + ","
                + "\"close_1\":" + JsonDouble(close1) + ","
                + "\"close_2\":" + JsonDouble(close2) + ","
                + "\"ema_1\":" + JsonDouble(ema1) + ","
                + "\"ema_2\":" + JsonDouble(ema2) + ","
                + "\"crossUp\":" + JsonBool(crossUp) + ","
                + "\"crossDown\":" + JsonBool(crossDown) + ","
                + "\"plannedType\":\"" + PlannedTypeText(plannedType) + "\","
                + "\"entry_allowed\":" + JsonBool(entryAllowed) + ","
                + "\"reasonTag\":\"" + JsonEsc(safeReason) + "\","
                + "\"block_reason\":\"" + JsonEsc(blockReason) + "\","
                + "\"rrPendingUsed\":" + JsonBool(rrPendingUsed) + ","
                + "\"reapproachConsumed\":" + JsonBool(reapproachConsumed) + ","
                + "\"dir_state\":\"" + DirStateText(_ema20SideState) + "\","
                + "\"dir_hold_remaining\":" + _ema20DirHoldRemaining.ToString(CultureInfo.InvariantCulture) + ","
                + "\"reap_pending_active\":" + JsonBool(_ema20ReapproachPending) + ","
                + "\"reap_created_index\":" + _ema20PendingCreatedSignalIndex.ToString(CultureInfo.InvariantCulture) + ","
                + "\"reap_planned\":\"" + PlannedTypeText(_ema20ReapproachPending ? (TradeType?)_ema20PendingTradeType : null) + "\","
                + "\"rr_pending_active\":" + JsonBool(_rrRelaxPendingActive) + ","
                + "\"rr_created_index\":" + _rrRelaxOriginBarIndex.ToString(CultureInfo.InvariantCulture) + ","
                + "\"rr_planned\":\"" + PlannedTypeText(_rrRelaxPendingActive ? (TradeType?)_rrRelaxPlannedType : null) + "\""
                + "}";

            string pendingJson =
                "{"
                + "\"event\":\"PENDING_STATE\","
                + "\"bar_time_utc_ms\":" + barTimeUtcMs.ToString(CultureInfo.InvariantCulture) + ","
                + "\"reap\":{"
                + "\"active\":" + JsonBool(_ema20ReapproachPending) + ","
                + "\"createdSignalIndex\":" + _ema20PendingCreatedSignalIndex.ToString(CultureInfo.InvariantCulture) + ","
                + "\"plannedType\":\"" + PlannedTypeText(_ema20ReapproachPending ? (TradeType?)_ema20PendingTradeType : null) + "\","
                + "\"reasonTag\":\"" + JsonEsc(_ema20PendingReasonTag) + "\""
                + "},"
                + "\"rr\":{"
                + "\"active\":" + JsonBool(_rrRelaxPendingActive) + ","
                + "\"createdSignalIndex\":" + _rrRelaxOriginBarIndex.ToString(CultureInfo.InvariantCulture) + ","
                + "\"plannedType\":\"" + PlannedTypeText(_rrRelaxPendingActive ? (TradeType?)_rrRelaxPlannedType : null) + "\","
                + "\"reasonTag\":\"" + JsonEsc(_rrRelaxReasonTag) + "\""
                + "},"
                + "\"dir\":{"
                + "\"state\":\"" + DirStateText(_ema20SideState) + "\","
                + "\"holdRemaining\":" + _ema20DirHoldRemaining.ToString(CultureInfo.InvariantCulture)
                + "}"
                + "}";

            AppendEntryDecisionJsonlLine(_entryDecisionJsonlPath, entryJson, "ENTRY_DECISION");
            AppendEntryDecisionJsonlLine(_pendingStateJsonlPath, pendingJson, "PENDING_STATE");
        }

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
        private string _proRunId = null;
        // PRO出力：稼働1回につき1フォルダ固定（PROレポート・OHLC等を同梱）
        private string _proSessionOutputDir = null;
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
            [Display(Name = "CORE")]
            ModeCORE = 0,

            [Display(Name = "EMA")]
            EMA = 1
        }

        public enum 口座通貨
        {
            USD = 0,
            JPY = 1
        }


        

        public enum 停止対象重要度
        {
            [Display(Name = "中・高")]
            中高 = 2,

            [Display(Name = "高")]
            高 = 3
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

        [Parameter("PROレポート等出力", Group = "資金管理・ロット制御", DefaultValue = true)]
        public bool EnableProReport { get; set; }

        [Parameter("PROレポート保存先フォルダ", Group = "資金管理・ロット制御", DefaultValue = "D:\\ChatGPT EA Development\\プロジェクト\\保管庫")]
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


        #region CORE制御・エントリー

        [Parameter("エントリーモード（CORE・EMA）", Group = "CORE制御・エントリー", DefaultValue = エントリーモード.ModeCORE)]
        public エントリーモード EntryMode { get; set; }

        [Parameter("CORE入口抑制構造を有効化", Group = "CORE制御・エントリー", DefaultValue = true)]
        public bool EnableCOREEntrySuppressionStructure { get; set; }
        [Parameter("RR緩和構造を有効化", Group = "CORE制御・エントリー", DefaultValue = true)]
        public bool EnableRrRelaxStructure { get; set; }

        #endregion


        #region CORE入口露出（パラメータ再現）

        [Parameter("方向判定デッドゾーン幅（PIPS）", Group = "CORE入口露出（パラメータ再現）", DefaultValue = 10.0, MinValue = 0.0)]
        public double DirectionDeadzonePips { get; set; }

        [Parameter("方向判定ヒステリシス比率", Group = "CORE入口露出（パラメータ再現）", DefaultValue = 0.6, MinValue = 0.0, MaxValue = 1.0)]
        public double DirectionHysteresisExitEnterRatio { get; set; }

        [Parameter("方向状態最短維持バー数", Group = "CORE入口露出（パラメータ再現）", DefaultValue = 2, MinValue = 0)]
        public int DirectionStateMinHoldBars { get; set; }

        [Parameter("方向判定価格ソース", Group = "CORE入口露出（パラメータ再現）", DefaultValue = 方向判定価格ソース.終値)]
        public 方向判定価格ソース DirectionPriceSource { get; set; }

        [Parameter("最小RR緩和を有効化", Group = "CORE入口露出（パラメータ再現）", DefaultValue = true)]
        public bool EnableMinRrRelax { get; set; }

        [Parameter("最小RR緩和有効バー数", Group = "CORE入口露出（パラメータ再現）", DefaultValue = 6, MinValue = 0)]
        public int MinRrRelaxWindowBars { get; set; }

        [Parameter("緩和後最小RR比率", Group = "CORE入口露出（パラメータ再現）", DefaultValue = 0.7, MinValue = 0.0)]
        public double MinRrRelaxedRatio { get; set; }

        #endregion



        private bool IsEntryModeCORE()
        {
            return EntryMode == エントリーモード.ModeCORE;
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

        [Parameter("エントリー禁止距離（下限）pips", Group = "エントリー関連", DefaultValue = 0.0, MinValue = 0.0)]
        public double EntryDeadZoneMinPips { get; set; }

        [Parameter("エントリー禁止距離（上限）pips", Group = "エントリー関連", DefaultValue = 0.0, MinValue = 0.0)]
        public double EntryDeadZoneMaxPips { get; set; }

        [Parameter("再接近監視バー数", Group = "エントリー関連", DefaultValue = 36, MinValue = 1)]
        public int ReapproachWindowBars { get; set; }

        [Parameter("再接近最大距離（PIPS）", Group = "エントリー関連", DefaultValue = 40.0, MinValue = 0.0)]
        public double ReapproachMaxDistancePips { get; set; }

        [Parameter("EMA期間", Group = "エントリー関連", DefaultValue = 20, MinValue = 1)]
        public int EmaPeriod { get; set; }
        [Parameter("エントリー方式（はい=EMAクロス / いいえ=上なら買い・下なら売り）", Group = "エントリー関連", DefaultValue = true)]
        public bool EntryTypeEmaCross { get; set; }


        [Parameter("クロス足の最小実体サイズ（PIPS）", Group = "エントリー関連", DefaultValue = 5.0, MinValue = 0.0)]
        public double CrossCandleMinBodyPips { get; set; }



        [Parameter("最低RR比", Group = "エントリー関連", DefaultValue = 1.0, MinValue = 0.0)]
        public double MinRRRatio { get; set; }

        #endregion

        // ============================================================
        // [ADD] HighLow（HL）トレンド関連パラメータ
        // ============================================================
        #region HighLowトレンド関連

        [Parameter("フィルターを使用", Group = "HighLowトレンド関連", DefaultValue = true)]
        public bool UseHLFilter { get; set; }

        [Parameter("計算対象バー", Group = "HighLowトレンド関連", DefaultValue = 500, MinValue = 200)]
        public int HL_計算対象バー { get; set; }

        [Parameter("ログ：毎バー出す", Group = "HighLowトレンド関連", DefaultValue = false)]
        public bool HL_ログ毎バー出す { get; set; }

        [Parameter("EA内部Pivot描画", Group = "HighLowトレンド関連", DefaultValue = true)]
        public bool HL_EA内部Pivot描画 { get; set; }

        [Parameter("強制クリーン頻度（0=しない）", Group = "HighLowトレンド関連", DefaultValue = 1, MinValue = 0)]
        public int HL_強制クリーン頻度 { get; set; }

        [Parameter("チャート描画", Group = "HighLowトレンド関連", DefaultValue = true)]
        public bool HL_描画検証ログ { get; set; }

        [Parameter("ログ出力", Group = "HighLowトレンド関連", DefaultValue = false)]
        public bool HL_LogOutput { get; set; }

        [Parameter("同値とみなす許容幅", Group = "HighLowトレンド関連", DefaultValue = 20.0, MinValue = 0.0)]
        public double HL_EqualTolerancePips { get; set; }

        #endregion


        #region 時間SL関連

        // ============================================================
        // 時間SL関連（最大保有時間で強制決済）
        //   時間SLは「最大保有」での強制クローズであり、別概念として実装する。
        // ============================================================
        [Parameter("時間SLを使用", Group = "時間SL関連", DefaultValue = false)]
        public bool UseTimeStop { get; set; }

        [Parameter("最大保有時間（分）", Group = "時間SL関連", DefaultValue = 0, MinValue = 0)]
        public int MaxHoldMinutes { get; set; }

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

        [Parameter("固定SL（PIPS）", Group = "SL関連・共通", DefaultValue = 30.0, MinValue = 0.0)]
        public double FixedSLPips { get; set; }

        [Parameter("最小SL（PIPS）", Group = "SL関連・共通", DefaultValue = 20.0, MinValue = 0.0)]
        public double MinSLPips { get; set; }

        [Parameter("最大SL（PIPS）", Group = "SL関連・共通", DefaultValue = 100.0, MinValue = 0.0)]
        public double MaxSlPipsInput { get; set; }


        #endregion


        #region SLイベント管理

        [Parameter("SLイベント管理を有効化", Group = "SLイベント管理", DefaultValue = true)]
        public bool EnableSLEventManagement { get; set; }

        // 同時成立時の処理順（入力方式で切替）
        // 0: 建値移動→部分利確→段階的SL
        // 1: 建値移動→段階的SL→部分利確
        // 2: 部分利確→建値移動→段階的SL
        // 3: 部分利確→段階的SL→建値移動
        // 4: 段階的SL→建値移動→部分利確
        // 5: 段階的SL→部分利確→建値移動
        // デバッグ：SLイベント判定ログ（挙動は変えない）
        [Parameter("SLイベント判定ログ間隔（秒）", Group = "SLイベント管理", DefaultValue = 60, MinValue = 1)]
        public int SLEventCheckLogIntervalSeconds { get; set; }

        [Parameter("SLイベント判定ログ上限（回/Pos）", Group = "SLイベント管理", DefaultValue = 30, MinValue = 1)]
        public int SLEventCheckLogMaxPerPosition { get; set; }

        // ----------------------------
        // 建値移動
        // ----------------------------
        [Parameter("建値移動を有効化", Group = "SLイベント管理", DefaultValue = false)]
        public bool EnableBreakevenMove { get; set; }

        [Parameter("建値移動 発動pips", Group = "SLイベント管理", DefaultValue = 150, MinValue = 0)]
        public int BreakevenTriggerPips { get; set; }

        [Parameter("建値移動SLオフセット（PIPS）", Group = "SLイベント管理", DefaultValue = 0.2, MinValue = 0.0)]
        public double BreakevenSLOffsetPips { get; set; }

        // ----------------------------
        // 部分利確（SLイベント管理系：TPブーストとは別系統）
        // ----------------------------
        [Parameter("部分利確を使う", Group = "SLイベント管理", DefaultValue = false)]
        public bool EnablePartialClose { get; set; }

        [Parameter("部分利確 発動pips", Group = "SLイベント管理", DefaultValue = 200, MinValue = 0)]
        public int PartialCloseTriggerPips { get; set; }

        [Parameter("部分利確 決済割合（%）", Group = "SLイベント管理", DefaultValue = 30.0, MinValue = 1.0, MaxValue = 99.0)]
        public double PartialClosePercentSLEvent { get; set; }

        // ----------------------------
        // 段階的SL（2段階固定）
        // ----------------------------
        [Parameter("段階的SLを使う", Group = "SLイベント管理", DefaultValue = false)]
        public bool EnableStepSlMove { get; set; }

        [Parameter("段階的SL 第1段階 発動pips", Group = "SLイベント管理", DefaultValue = 200, MinValue = 0)]
        public int StepSlStage1TriggerPips { get; set; }

        [Parameter("段階的SL 第1段階 SL移動pips", Group = "SLイベント管理", DefaultValue = 50, MinValue = 0)]
        public int StepSlStage1MovePips { get; set; }

        [Parameter("段階的SL 第2段階 発動pips", Group = "SLイベント管理", DefaultValue = 400, MinValue = 0)]
        public int StepSlStage2TriggerPips { get; set; }

        [Parameter("段階的SL 第2段階 SL移動pips", Group = "SLイベント管理", DefaultValue = 200, MinValue = 0)]
        public int StepSlStage2MovePips { get; set; }

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
        #endregion


        #region TP関連・ATR

        [Parameter("TP/ATR期間", Group = "TP関連・ATR", DefaultValue = 14, MinValue = 1)]
        public int TpAtrPeriod { get; set; }

        [Parameter("TP/ATR倍率", Group = "TP関連・ATR", DefaultValue = 2.0, MinValue = 0.0)]
        public double TpAtrMult { get; set; }

        #endregion


        #region TP関連・構造TP


        [Parameter("TP構造時間足（分）", Group = "TP関連・構造TP", DefaultValue = 60, MinValue = 1)]
        public int TpStructureTimeframeMinutes { get; set; }

        [Parameter("TP構造スイング判定 左右本数", Group = "TP関連・構造TP", DefaultValue = 2, MinValue = 1)]
        public int TpSwingLR { get; set; }

        [Parameter("TP構造スイング探索本数", Group = "TP関連・構造TP", DefaultValue = 200, MinValue = 10)]
        public int TpSwingLookback { get; set; }

        [Parameter("構造TPバッファ（PIPS）", Group = "TP関連・構造TP", DefaultValue = 50.0, MinValue = 0.0)]
        public double StructureTpBufferPips { get; set; }

        #endregion


        #region TP関連ブースト

        [Parameter("構造TPブースト１", Group = "TP関連ブースト", DefaultValue = false)]
        public bool EnableStructureTpBoost { get; set; }

        [Parameter("構造TPブースト1開始R", Group = "TP関連ブースト", DefaultValue = 0.9, MinValue = 0.0, Step = 0.01)]
        public double StructureTpBoostStartRStage1 { get; set; }

        [Parameter("構造TP第2段ブースト２", Group = "TP関連ブースト", DefaultValue = true)]
        public bool EnableStructureTpBoostStage2 { get; set; }

        [Parameter("構造TPブースト2開始R", Group = "TP関連ブースト", DefaultValue = 1.8, MinValue = 0.0, Step = 0.01)]
        public double StructureTpBoostStartRStage2 { get; set; }



        // ============================================================
        // TPブースト連動・分岐出口（方式A：構造TP再計算）
        // - ブースト検出時のみ、TP30到達で部分利確し、残りを構造TPへ伸ばす
        // - 通常時（ブーストなし）は既存挙動を一切変更しない
        // ============================================================
        [Parameter("TPブースト分岐出口を使用", Group = "EXIT・TPブースト分岐", DefaultValue = false)]
        public bool EnableTpBoostBranchExit { get; set; }

        [Parameter("部分利確を使用", Group = "EXIT・TPブースト分岐", DefaultValue = true)]
        public bool PartialCloseOnTpBoost { get; set; }

        [Parameter("部分利確割合", Group = "EXIT・TPブースト分岐", DefaultValue = 0.50, MinValue = 0.10, MaxValue = 0.90)]
        public double PartialClosePercent { get; set; }

        public enum PartialCloseTriggerModeEnum
        {
            OnTpHit = 0,
            OnBoostDetect = 1
        }

        [Parameter("部分利確トリガー", Group = "EXIT・TPブースト分岐", DefaultValue = PartialCloseTriggerModeEnum.OnTpHit)]
        public PartialCloseTriggerModeEnum PartialCloseTriggerMode { get; set; }

        [Parameter("1ポジション1回のみ適用", Group = "EXIT・TPブースト分岐", DefaultValue = true)]
        public bool BranchExitOnlyOncePerPosition { get; set; }
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



        [Parameter("EMA傾き方向判定を使用", Group = "方向・判定補助", DefaultValue = false)]
        public bool EnableEmaSlopeDirectionDecision { get; set; }

        [Parameter("EMA傾き判定 本数", Group = "方向・判定補助", DefaultValue = 1, MinValue = 1)]
        public int EmaSlopeLookbackBars { get; set; }

        [Parameter("EMA傾き 最小有効差分（PIPS）", Group = "方向・判定補助", DefaultValue = 0.0, MinValue = 0.0)]
        public double EmaSlopeMinPips { get; set; }
        #endregion

        #region EMA10/EMA20方向判定

        public enum EMA10EMA20判定モード
        {
            状態 = 0,
            クロス = 1
        }

        public enum EMA10EMA20同値時の扱い
        {
            両方向許可 = 0,
            両方向禁止 = 1
        }

        [Parameter("EMA10/EMA20方向判定を使用（はい／いいえ）", Group = "EMA10/EMA20方向判定", DefaultValue = false)]
        public bool EnableEma10Ema20DirectionDecision { get; set; }

        [Parameter("EMA短期期間", Group = "EMA10/EMA20方向判定", DefaultValue = 10, MinValue = 1)]
        public int Ema10Ema20FastPeriod { get; set; }

        [Parameter("EMA長期期間", Group = "EMA10/EMA20方向判定", DefaultValue = 20, MinValue = 1)]
        public int Ema10Ema20SlowPeriod { get; set; }

        [Parameter("EMA10/EMA20判定モード", Group = "EMA10/EMA20方向判定", DefaultValue = EMA10EMA20判定モード.状態)]
        public EMA10EMA20判定モード Ema10Ema20DecisionMode { get; set; }

        [Parameter("EMA10/EMA20クロス有効バー数", Group = "EMA10/EMA20方向判定", DefaultValue = 6, MinValue = 1)]
        public int Ema10Ema20CrossValidBars { get; set; }

        [Parameter("EMA10/EMA20同値デッドゾーン（PIPS）", Group = "EMA10/EMA20方向判定", DefaultValue = 0.0, MinValue = 0.0)]
        public double Ema10Ema20EqualDeadzonePips { get; set; }

        [Parameter("EMA10/EMA20同値時の扱い", Group = "EMA10/EMA20方向判定", DefaultValue = EMA10EMA20同値時の扱い.両方向許可)]
        public EMA10EMA20同値時の扱い Ema10Ema20EqualHandling { get; set; }

        [Parameter("EMA10/EMA20 傾き一致を使用（はい／いいえ）", Group = "EMA10/EMA20方向判定", DefaultValue = false)]
        public bool EnableEma10Ema20SlopeAgreement { get; set; }

        [Parameter("EMA10/EMA20 傾き判定 差分本数", Group = "EMA10/EMA20方向判定", DefaultValue = 6, MinValue = 1, MaxValue = 50)]
        public int Ema10Ema20SlopeBars { get; set; }

        [Parameter("EMA10/EMA20 傾き 最小有効差分（PIPS）", Group = "EMA10/EMA20方向判定", DefaultValue = 0.0, MinValue = 0.0)]
        public double Ema10Ema20SlopeMinPips { get; set; }

        #endregion


        #region 環境ゲート（ATR）

        [Parameter("ATR環境ゲート有効", Group = "環境ゲート（ATR）", DefaultValue = false)]
        public bool EnableAtrEnvGate { get; set; }

        [Parameter("ATR期間", Group = "環境ゲート（ATR）", DefaultValue = 14, MinValue = 1)]
        public int AtrEnvPeriod { get; set; }

        [Parameter("ATR下限（PIPS）0=無効", Group = "環境ゲート（ATR）", DefaultValue = 0.0, MinValue = 0.0)]
        public double AtrEnvMinPips { get; set; }

        [Parameter("ATR上限（PIPS）0=無効", Group = "環境ゲート（ATR）", DefaultValue = 0.0, MinValue = 0.0)]
        public double AtrEnvMaxPips { get; set; }

        #endregion

        #region ログ
        #endregion


        #region 経済指標フィルター（UTC）

        [Parameter("バックテスト用", Group = "経済指標フィルター（UTC）", DefaultValue = false)]
        public bool UseNewsBacktest2025 { get; set; }

        [Parameter("フォワード用", Group = "経済指標フィルター（UTC）", DefaultValue = false)]
        public bool UseNewsForward { get; set; }

        [Parameter("停止対象重要度", Group = "経済指標フィルター（UTC）", DefaultValue = 停止対象重要度.中高)]
        public 停止対象重要度 MinImpactLevel { get; set; }

        [Parameter("指標前の停止時間（分）", Group = "経済指標フィルター（UTC）", DefaultValue = 15)]
        public int MinutesBeforeNews { get; set; }

        [Parameter("指標後の停止時間（分）", Group = "経済指標フィルター（UTC）", DefaultValue = 15)]
        public int MinutesAfterNews { get; set; }

        [Parameter("経済指標APIキー", Group = "経済指標フィルター（UTC）", DefaultValue = "")]
        public string CalendarApiKey { get; set; }

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
        // [ADD] HL 定数・構造体・enum（019から移植）
        // ============================================================
        private const int HL_ZZ_Depth = 12;
        private const double HL_Points = 50.0;
        private const int HL_Backstep = 3;
        private const double HL_SuppressPoints = 0.0;
        private const bool HL_同値右優先 = true;
        private const int HL_MaxBars = 500;
        private const int HL_RightBars_M5 = 3;
        private const int HL_RightBars_H1 = 3;
        private const int HL_LabelFontSize = 9;
        private const int HL_LabelYOffsetTicks = 1;
        private const int HL_LoadHistoryMaxRetries = 5;
        private const int HL_ForceCleanLogNameLimit = 10;
        private string HL_DrawObjectPrefix { get { return CODE_NAME + "."; } }

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

        public enum HL時間足
        {
            M5 = 1,
            H1 = 3
        }

        // ============================================================
        // [ADD] HL 状態変数（019から移植）
        // ============================================================
        private Bars _hlBarsM5;
        private Bars _hlBarsH1;

        private DateTime _hlLastPivotCalcBarTimeM5 = DateTime.MinValue;
        private DateTime _hlLastPivotCalcBarTimeH1 = DateTime.MinValue;

        private List<HL_Pivot> _m5Pivots = new List<HL_Pivot>();
        private List<HL_Pivot> _h1Pivots = new List<HL_Pivot>();

        private bool _hlRedrawRunning = false;
        private bool _hlRedrawScheduled = false;
        private bool _hlRedrawQueued = false;
        private long _hlDrawGeneration = 0;
        private long _hlRequestedDrawGeneration = 0;
        private long _hlRenderedDrawGeneration = 0;
        private long _hlClearCallCount = 0;
        private int _hlCleanTick = 0;

        private DateTime _hlLastOnBarCheckTimeM5 = DateTime.MinValue;
        private DateTime _hlLastOnBarCheckTimeH1 = DateTime.MinValue;

        private StackPanel _hlDowStatusPanel;
        private TextBlock _hlDowStatusTextM5;
        private TextBlock _hlDowStatusTextH1;
        private string _hlDowLastDrawnH1Text = "";
        private string _hlDowLastDrawnM5Text = "";

        private TrendState _hlDowStateM5 = TrendState.TrendLess;
        private TrendState _hlDowStateH1 = TrendState.TrendLess;

        private TrendLessContext _hlDowContextM5 = TrendLessContext.None;
        private TrendLessContext _hlDowContextH1 = TrendLessContext.None;

        private DateTime _hlDowContextStartTimeM5 = DateTime.MinValue;
        private DateTime _hlDowContextStartTimeH1 = DateTime.MinValue;

        private HL_Pivot? _hlDowDefenseLowM5 = null;
        private HL_Pivot? _hlDowDefenseLowH1 = null;

        private HL_Pivot? _hlDowTrendLessRefHighM5 = null;
        private HL_Pivot? _hlDowTrendLessRefHighH1 = null;

        private bool _hlDowTrendLessArmedHLM5 = false;
        private bool _hlDowTrendLessArmedHLH1 = false;

        private HL_Pivot? _hlDowAfterUpKeyHighM5 = null;
        private HL_Pivot? _hlDowAfterUpKeyHighH1 = null;

        private HL_Pivot? _hlDowAfterUpKeyLowM5 = null;
        private HL_Pivot? _hlDowAfterUpKeyLowH1 = null;

        private bool _hlDowAfterUpHasHighM5 = false;
        private bool _hlDowAfterUpHasHighH1 = false;

        private HL_Pivot? _hlDowTrendKeyHLM5 = null;
        private HL_Pivot? _hlDowTrendKeyHLH1 = null;

        private HL_Pivot? _hlDowTrendKeyLHM5 = null;
        private HL_Pivot? _hlDowTrendKeyLHH1 = null;

        private DateTime _hlDowTrendKeyHLUpdatedBarTimeM5 = DateTime.MinValue;
        private DateTime _hlDowTrendKeyHLUpdatedBarTimeH1 = DateTime.MinValue;

        private DateTime _hlDowTrendKeyLHUpdatedBarTimeM5 = DateTime.MinValue;
        private DateTime _hlDowTrendKeyLHUpdatedBarTimeH1 = DateTime.MinValue;

        private DateTime _hlDowLastProcessedBarTimeM5 = DateTime.MinValue;
        private DateTime _hlDowLastProcessedBarTimeH1 = DateTime.MinValue;

        private DateTime _hlDowLastLoggedBarTimeM5 = DateTime.MinValue;
        private DateTime _hlDowLastLoggedBarTimeH1 = DateTime.MinValue;

        // [ADD] HL_READY flag: false until HL initialization completes
        private bool _hlReady = false;

        // ============================================================
        // 状態（079 既存）
        // ============================================================
        private AverageTrueRange _atrMinSl;
        private AverageTrueRange _atrTp;

        private AverageTrueRange _atrEnvGate; // ATR環境ゲート（新規エントリー許可判定用）
        private Bars _barsTpStructure;
        private ExponentialMovingAverage _emaFramework;
        private ExponentialMovingAverage _ema;
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private int _emaFastPeriodApplied = -1;
        private int _emaSlowPeriodApplied = -1;
        private bool _ema10Ema20InitDone = false;
        private string _ema10Ema20LastGateLogKey = "";


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
        private readonly HashSet<long> _missingProtectionCloseRequested = new HashSet<long>();
        private readonly HashSet<long> _beAppliedPosIds = new HashSet<long>(); // 建値移動は1回のみ（内部固定）

        private readonly HashSet<long> _partialCloseAppliedPosIds = new HashSet<long>();
        private readonly HashSet<long> _stepSlStage1AppliedPosIds = new HashSet<long>();
        private readonly HashSet<long> _stepSlStage2AppliedPosIds = new HashSet<long>();

        private bool _slEventExecutedThisTick = false;
        // SLイベント判定ログ（Posごとに間引き）
        private readonly Dictionary<long, DateTime> _slEventCheckLastLogUtc = new Dictionary<long, DateTime>();
        private readonly Dictionary<long, int> _slEventCheckLogCount = new Dictionary<long, int>();

        private readonly HashSet<long> _timeStopCloseRequested = new HashSet<long>();
        private readonly HashSet<long> _structureTpBoostedPosIds = new HashSet<long>(); // Stage1
        private readonly HashSet<long> _structureTpBoostedPosIdsStage2 = new HashSet<long>(); // Stage2

        // ============================================================
        // Stage2 判定（Single Source of Truth）
        // ============================================================
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
        // TPブースト分岐出口 状態管理（PositionId単位）
        // ============================================================
        private readonly Dictionary<long, bool> _tpBoostDetectedByPosId = new Dictionary<long, bool>();
        private readonly Dictionary<long, bool> _tpBoostPartialClosedByPosId = new Dictionary<long, bool>();
        private readonly Dictionary<long, double> _tpBoostBaseTpPriceByPosId = new Dictionary<long, double>(); // TP30相当のトリガー価格（価格）
        private readonly Dictionary<long, double?> _tpBoostOldTpByPosId = new Dictionary<long, double?>();
        private readonly Dictionary<long, double?> _tpBoostNewTpByPosId = new Dictionary<long, double?>();

        private bool _rrRelaxPendingActive = false;
        private int _rrRelaxOriginBarIndex = -1; // Bars index (last closed bar index) at which pending was set
        private TradeType _rrRelaxPlannedType = TradeType.Buy;
        private string _rrRelaxReasonTag = "";

        // ============================================================
        private bool _stopRequestedByRiskFailure = false;
        // ============================================================
        // CORE（CORE実在コード由来の入口抑制構造）※パラメータ追加なし
        // 参照元：EMA_M5_ALL_DAY_018_CORE（内部CODE_NAMEは問わない。ロジック事実のみ移植）
        // ============================================================

        private enum LineSideState { Neutral = 0, Above = 1, Below = 2 }


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

// --- Chart overlay (stop state) ---
private string StopOverlayName { get { return CODE_NAME + ".EA_STOP_OVERLAY_CENTER"; } }
private bool _stopOverlayVisible = false;
private string _stopOverlayLastText = "";

private void RemoveLegacyStopOverlayObjects()
{
    try
    {
        string legacy078 = "EMA_M5_ALL_DAY_020_" + "078_XAUUSD_M5.EA_STOP_OVERLAY_CENTER";
        string legacy077 = "EMA_M5_ALL_DAY_020_" + "077_XAUUSD_M5.EA_STOP_OVERLAY_CENTER";
        Chart.RemoveObject(legacy078);
        Chart.RemoveObject(legacy077);
    }
    catch
    {
        // no-op
    }
}

private void UpdateStopOverlay(bool show, string reason, DateTime utcNow, DateTime? untilUtc)
{
    try
    {
        string stopOverlayName = StopOverlayName;

        if (!show)
        {
            Chart.RemoveObject(stopOverlayName);
            RemoveLegacyStopOverlayObjects();

            if (_stopOverlayVisible)
            {
                _stopOverlayVisible = false;
                _stopOverlayLastText = "";
            }
            return;
        }

        string text = "停止中";

        // countdown
        if (untilUtc.HasValue)
        {
            TimeSpan rem = untilUtc.Value - utcNow;
            if (rem < TimeSpan.Zero) rem = TimeSpan.Zero;
            text += "\n解除まで " + FormatCountdown(rem);
        }

        if (!string.IsNullOrWhiteSpace(reason))
            text += "\n" + reason;

        if (_stopOverlayVisible && string.Equals(_stopOverlayLastText, text, StringComparison.Ordinal))
            return;

        Chart.RemoveObject(stopOverlayName);

        object obj = Chart.DrawStaticText(stopOverlayName, text, VerticalAlignment.Center, HorizontalAlignment.Center, Color.Red);
        TrySetStaticTextStyle(obj, 80, true);

        _stopOverlayVisible = true;
        _stopOverlayLastText = text;
    }
    catch
    {
        // no-op（表示失敗してもEA継続）
    }
}

private static string FormatCountdown(TimeSpan ts)
{
    if (ts.TotalDays >= 1.0)
        return ((int)ts.TotalDays).ToString(CultureInfo.InvariantCulture) + "d " + ts.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" + ts.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + ts.Seconds.ToString("00", CultureInfo.InvariantCulture);

    return ts.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" + ts.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + ts.Seconds.ToString("00", CultureInfo.InvariantCulture);
}

private void TrySetStaticTextStyle(object obj, int fontSize, bool bold)
{
    if (obj == null) return;

    try
    {
        var t = obj.GetType();

        var pFont = t.GetProperty("FontSize");
        if (pFont != null && pFont.CanWrite)
            pFont.SetValue(obj, fontSize, null);

        var pBold = t.GetProperty("IsBold");
        if (pBold != null && pBold.CanWrite)
            pBold.SetValue(obj, bold, null);
    }
    catch
    {
        // no-op
    }
}

private DateTime? ComputeTradingWindowUnblockUtc(DateTime utcNow)
{
    if (!EnableTradingWindowFilter)
        return null;

    DateTime jstNow = ToJst(utcNow);
    DateTime jstTarget = jstNow.Date.AddMinutes(_tradeStartMinJst);

    if (jstTarget <= jstNow)
        jstTarget = jstTarget.AddDays(1);

    try
    {
        if (_jstTz != null)
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(jstTarget, DateTimeKind.Unspecified), _jstTz);
    }
    catch { }

    // fallback: JST = UTC+9
    return DateTime.SpecifyKind(jstTarget, DateTimeKind.Unspecified).AddHours(-9);
}

private DateTime? ComputeNewsUnblockUtc(DateTime utcNow)
{
    if (!UseNewsBacktest2025 && !UseNewsForward)
        return null;
    if (UseNewsBacktest2025 && UseNewsForward)
        return null;

    int before = Math.Max(0, MinutesBeforeNews);
    int after = Math.Max(0, MinutesAfterNews);

    if (UseNewsBacktest2025)
    {
        if (_newsEventsUtc == null || _newsEventsUtc.Count == 0)
            return null;

        for (int i = 0; i < _newsEventsUtc.Count; i++)
        {
            DateTime e = _newsEventsUtc[i];
            DateTime start = e.AddMinutes(-before);
            DateTime end = e.AddMinutes(after);

            if (utcNow >= start && utcNow <= end)
                return end;
        }

        return null;
    }

    if (_newsForwardEvents == null || _newsForwardEvents.Count == 0)
        return null;

    HashSet<string> targetCurrencies = GetTargetCurrenciesForSymbol(SymbolName);
    int minImpact = GetEffectiveMinImpactLevel();

    for (int i = 0; i < _newsForwardEvents.Count; i++)
    {
        NewsEvent ev = _newsForwardEvents[i];
        if (!IsForwardNewsEventMatch(ev, targetCurrencies, minImpact))
            continue;

        DateTime e = ev.UtcTime;
        DateTime start = e.AddMinutes(-before);
        DateTime end = e.AddMinutes(after);

        if (utcNow >= start && utcNow <= end)
            return end;
    }

    return null;
}

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

        // EMA_SNAPSHOT duplicate guard (same bar_time_utc_ms should never be emitted twice)
        private long _lastEmaSnapshotBarTimeUtcMs = long.MinValue;

        // EMA_SNAPSHOT JSONL output (pure JSONL; no prefix) - observation only
        private string _emaSnapshotJsonlPath = null;
        private readonly object _emaSnapshotFileLock = new object();

        // ENTRY_DECISION / PENDING_STATE JSONL output (PROレポート・OHLC出力=はい のときのみ)
        private const string ENTRY_DECISION_FIXED_DIR = @"D:\ChatGPT EA Development\プロジェクト\保管庫";
        private string _entryDecisionJsonlPath = null;
        private string _pendingStateJsonlPath = null;
        private bool _entryDecisionLogEnabled = false;
        private bool _entryDecisionLogStopped = false;
        private readonly object _entryDecisionLogFileLock = new object();
        private string _eaInitJsonlPath = null;
        private string _barCloseJsonlPath = null;
        private string _executeJsonlPath = null;
        private string _atrEnvEvalJsonlPath = null;
        private bool _observationLogEnabled = false;
        private bool _observationLogStopped = false;
        private readonly object _observationJsonlFileLock = new object();
        private int _lastEntryDecisionBarIndex = int.MinValue;
        private int _lastBarCloseIndex = int.MinValue;
        private int _ema20DirHoldRemaining = 0;

        // ============================================================
        // ライフサイクル
        // ============================================================

        protected override void OnStart()
        {
            // PROレポート用 初期残高保存
            _proInitialBalance = Account.Balance;

            // PROレポート：同一テスト内のファイル名固定用ID
            _proRunId = ToJst(DateTime.UtcNow).ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            RemoveLegacyStopOverlayObjects();

            // ENTRY_DECISION / PENDING_STATE JSONL init（PROレポート・OHLC出力=はい のときのみ）
            InitializeEntryDecisionLogging();

            // EMA_SNAPSHOT JSONL (pure) output path init (observation only)
            _emaSnapshotJsonlPath = BuildEmaSnapshotLogPath();
            EnsureDirectoryForFile(_emaSnapshotJsonlPath);

            // EA_INIT / BAR_CLOSE / EXECUTE_* JSONL init（PROレポート・OHLC出力=はい のときのみ）
            InitializeObservationJsonlLogging();
            EmitEaInitJsonl();

            // ------------------------------------------------------------
            // Stage2 Exit 用インジケータ準備（M15 EMA20 / D1 Bars）
            // ------------------------------------------------------------
            _barsM15 = MarketData.GetBars(TimeFrame.Minute15); _barsD1 = MarketData.GetBars(TimeFrame.Daily);

            _jstTz = ResolveTokyoTimeZone();
            // ============================
            // Instance Stamp (one-shot)
            // ============================
            ValidateNewsModeOrStop();
            PrintInstanceStamp();
            PrintParameterSnapshot();

            _atrMinSl = Indicators.AverageTrueRange(Math.Max(1, MinSlAtrPeriod), MovingAverageType.Simple);
            _atrTp = Indicators.AverageTrueRange(Math.Max(1, TpAtrPeriod), MovingAverageType.Simple);
            _atrEnvGate = Indicators.AverageTrueRange(Math.Max(1, AtrEnvPeriod), MovingAverageType.Simple);
            _barsTpStructure = MarketData.GetBars(ResolveTpStructureTimeFrame());
            _ema = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EMA_PERIOD_FIXED);

            _emaFramework = Indicators.ExponentialMovingAverage(Bars.ClosePrices, Math.Max(1, EmaPeriod));
            string ema10Ema20InitReasonOnStart;
            InitEma10Ema20IndicatorsIfNeeded(out ema10Ema20InitReasonOnStart);
            ResolveTradingWindowMinutesOrDefaults();

            // Symbol category (METAL vs FX) and pips input scale
            string symNorm = NormalizeSymbolName(SymbolName);
            _symbolCategory = (symNorm == "XAUUSD" || symNorm == "XAGUSD") ? SymbolCategory.Metal : SymbolCategory.Fx;
            _pipsScale = (symNorm == "XAUUSD") ? 10 : 1; // XAU=10, XAG=1, その他=1

            // News provider (backtest scaffold). Future: swap to API provider.

            ValidateAccountCurrencyOrStop();
            if (_stopRequestedByRiskFailure)
                return;

            ValidateRiskInputsOrStop();
            if (_stopRequestedByRiskFailure)
                return;

            // ============================================================
            // [ADD] HL初期化（UseHLFilter=true のときのみ）
            // ============================================================
            if (UseHLFilter)
            {
                try
                {
                    _hlBarsM5 = MarketData.GetBars(TimeFrame.Minute5, SymbolName);
                    _hlBarsH1 = MarketData.GetBars(TimeFrame.Hour, SymbolName);

                    int requiredBars = Math.Max(HL_MaxBars, HL_計算対象バー);
                    HL_EnsureMinimumBars(_hlBarsM5, requiredBars, "HL_M5");
                    HL_EnsureMinimumBars(_hlBarsH1, requiredBars, "HL_H1");

                    bool m5Ok = (_hlBarsM5 != null && _hlBarsM5.Count >= requiredBars);
                    bool h1Ok = (_hlBarsH1 != null && _hlBarsH1.Count >= requiredBars);

                    if (m5Ok && h1Ok)
                    {
                        int failedClean;
                        HL_ForceCleanDrawingsByPrefix(out failedClean);

                        HL_RecalculateAndProject();
                        HL_RequestPivotRedraw("ON_START_HL");
                        HL_EvaluateHlDowAndUpdateUi(UtcNow(), true);
                        _hlReady = true;
                        Print("HL_INIT_OK | CodeName={0} | M5Bars={1} | H1Bars={2} | StateM5={3} | StateH1={4}",
                            CODE_NAME,
                            _hlBarsM5 != null ? _hlBarsM5.Count : 0,
                            _hlBarsH1 != null ? _hlBarsH1.Count : 0,
                            HL_FormatTrendStateForLog(_hlDowStateM5),
                            HL_FormatTrendStateForLog(_hlDowStateH1));
                    }
                    else
                    {
                        _hlReady = false;
                        Print("HL_INIT_NOT_READY | CodeName={0} | M5Bars={1} | H1Bars={2} | Required={3}",
                            CODE_NAME,
                            _hlBarsM5 != null ? _hlBarsM5.Count : 0,
                            _hlBarsH1 != null ? _hlBarsH1.Count : 0,
                            requiredBars);
                    }
                }
                catch (Exception exHl)
                {
                    _hlReady = false;
                    Print("HL_INIT_ERROR | CodeName={0} | Error={1}", CODE_NAME, exHl.Message);
                }
            }

            Timer.Start(1);

            _lastTickUtc = UtcNow();

            _slEventExecutedThisTick = false;
            Positions.Closed += OnPositionClosed;
            // PROレポート用ハンドラ（ProReport有効時のみ登録）
            if (EnableProReport)
            {
                Positions.Closed += OnPositionsClosedForProReport;
            }

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
                (UseNewsBacktest2025 ? "BACKTEST_2025" : (UseNewsForward ? "FORWARD_API" : "DISABLED")),
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
            // [ADD] HL cleanup
            if (UseHLFilter)
            {
                try
                {
                    int failedClean;
                    HL_ForceCleanDrawingsByPrefix(out failedClean);
                    HL_RemoveHlDowStatusDisplay();
                }
                catch { }
            }

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

            // SLイベント：同一Tick内の多重適用防止フラグはTickごとにリセット
            _slEventExecutedThisTick = false;

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

                if (EnableProReport && state != _lastTradingWindowStateTick)
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

            // 030 Safety: if any position exists without BOTH SL and TP, close immediately (all modes)
            MissingProtectionImmediateClose();
            // Position management (allowed during HoldOnly)
            ApplySLEventManagementIfNeeded();
            ApplyStructureTpBoostIfNeeded();
            ApplyTpBoostBranchPartialCloseIfNeeded();

            // 資金管理レイヤー：緊急クローズは常に有効
            ApplyTimeStopIfNeeded();

            // 資金管理レイヤー：緊急クローズは常に有効
            ApplyEmergencyCloseIfNeeded();
        }

        protected override void OnBar()
        {
            if (Bars == null || Bars.Count < 50)
                return;

            // ============================================================
            // [ADD] HL更新（UseHLFilter=true のときのみ）
            // ============================================================
            if (UseHLFilter)
            {
                try
                {
                    DateTime hlUtcNow = UtcNow();
                    bool m5Changed = HL_HasNewClosedBar(_hlBarsM5, ref _hlLastOnBarCheckTimeM5);
                    bool h1Changed = HL_HasNewClosedBar(_hlBarsH1, ref _hlLastOnBarCheckTimeH1);

                    HL_RecalculateAndProject();

                    if (HL_EA内部Pivot描画 && (m5Changed || h1Changed))
                        HL_RequestPivotRedraw(m5Changed && h1Changed ? "ON_BAR_M5_H1" : (m5Changed ? "ON_BAR_M5" : "ON_BAR_H1"));

                    HL_EvaluateHlDowAndUpdateUi(hlUtcNow, false);
                }
                catch (Exception exHlBar)
                {
                    Print("HL_ONBAR_ERROR | CodeName={0} | Error={1}", CODE_NAME, exHlBar.Message);
                }
            }

            int barCloseIndex = Bars.Count - 2; // last closed bar only
            EmitBarCloseJsonl(barCloseIndex);

            string ema10Ema20InitReasonOnBar;
            InitEma10Ema20IndicatorsIfNeeded(out ema10Ema20InitReasonOnBar);

            DateTime utcNow = UtcNow();

            void EmitGateBlockedDecision(string reasonTag)
            {
                int i1g = Bars.Count - 2;
                int i2g = Bars.Count - 3;
                if (i2g < 0)
                    return;

                double c1 = Bars.ClosePrices[i1g];
                double c2 = Bars.ClosePrices[i2g];
                double? e1 = (_ema != null && _ema.Result != null && _ema.Result.Count > i1g) ? (double?)_ema.Result[i1g] : null;
                double? e2 = (_ema != null && _ema.Result != null && _ema.Result.Count > i2g) ? (double?)_ema.Result[i2g] : null;
                bool cup = e1.HasValue && e2.HasValue && (c2 <= e2.Value) && (c1 > e1.Value);
                bool cdown = e1.HasValue && e2.HasValue && (c2 >= e2.Value) && (c1 < e1.Value);

                EmitEntryDecisionAndPendingState(
                    i1g,
                    i2g,
                    c1,
                    c2,
                    e1,
                    e2,
                    cup,
                    cdown,
                    null,
                    false,
                    reasonTag,
                    false,
                    false);
            }

            // reset per-bar guards
            _oncePerBarActionGuard.Clear();

            // ============================================================
            // EMA_SNAPSHOT (JSONL) - once-per-bar
            //  - Trigger: OnBar (M5 new bar confirmed)
            //  - Bars referenced: i1=last closed, i2=previous closed (no forming bar)
            //  - Output: pure JSONL to dedicated file (no prefix). No OHLC-derived EMA recomputation.
            if (TimeFrame == TimeFrame.Minute5)
            {
                // PROレポート出力が「はい」の時のみスナップショットを出す（観測のみ）
                if (!EnableProReport)
                {
                    // no-op
                }
                else
                {
                    int i1 = Bars.Count - 2; // last closed bar
                    int i2 = Bars.Count - 3; // previous closed bar
                    if (i2 >= 0)
                    {
                        long barTimeUtcMs = ToUnixTimeMillisecondsUtc(Bars.OpenTimes[i1]);

                        // once-per-bar guard (defensive): never emit duplicate bar_time_utc_ms
                        if (barTimeUtcMs != _lastEmaSnapshotBarTimeUtcMs)
                        {
                            _lastEmaSnapshotBarTimeUtcMs = barTimeUtcMs;

                            // time_utc_ms is the NEW bar start time (i0 open time) for stable 300000ms delta on M5
                            DateTime snapTimeUtc = Bars.OpenTimes.LastValue;

                            string line = BuildEmaSnapshotJsonl(snapTimeUtc, i1, i2);
                            AppendJsonlLine(_emaSnapshotJsonlPath, line);
                        }
                    }
                }
            }

            if (EnableTradingWindowFilter)
            {
                DateTime jstNow = ToJst(utcNow);
                TradingWindowState state = GetTradingWindowState(jstNow);

                if (EnableProReport && state != _lastTradingWindowState)
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
                {
                    DateTime? untilUtc = ComputeTradingWindowUnblockUtc(utcNow);
                    UpdateStopOverlay(true, "取引時間", utcNow, untilUtc);

                    EmitGateBlockedDecision("TIME_WINDOW_BLOCK");
                    return;
                }

                // NEWS MODULE (UTC) gate (new entries only) (UTC) gate (new entries only)
                News_InitOrRefresh(utcNow);
                if (!IsNewEntryAllowed(utcNow, out string newsReason))
                {
                    DateTime? untilUtc = ComputeNewsUnblockUtc(utcNow);

                    if (EnableProReport && !_wasNewsBlocked)
                    {
                        string untilText = untilUtc.HasValue ? untilUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "NA";
                        Print("NEWS_BLOCK | CodeName={0} | Symbol={1} | Reason={2} | BeforeMin={3} AfterMin={4} | UntilUtc={5}{6}",
                            CODE_NAME, SymbolName, newsReason, Math.Max(0, MinutesBeforeNews), Math.Max(0, MinutesAfterNews), untilText, BuildTimeTag(utcNow));
                        _wasNewsBlocked = true;
                    }

                    UpdateStopOverlay(true, "経済指標", utcNow, untilUtc);

                    EmitGateBlockedDecision("NEWS_BLOCK");
                    return;
                }
                _wasNewsBlocked = false;

                // clear overlay when both gates allow new entries
                UpdateStopOverlay(false, "", utcNow, null);


                // Reset block-flag when entries are allowed again
                _lastTradingWindowState = TradingWindowState.AllowNewEntries;
            }


            if ((Positions.FindAll(BOT_LABEL, SymbolName)?.Length ?? 0) >= Math.Max(1, MaxPositions))
            {
                return;
            }


            // ============================================================
            // STEP2: Framework入口の呼び出し
            // ============================================================
            if (IsCOREModeEnabled())
            {
                LogEntryRouteOnce("Framework");
                TryEntryFramework(utcNow);
                return;
            }
            LogEntryRouteOnce("EMA");
            TryEmaEntry();
        }

        protected override void OnTimer()
        {
            DateTime utcNow = UtcNow();
            DateTime jstNow = ToJst(utcNow);

            // Market monitor (logging only; no forced actions)
            MarketMonitorOnTimer(utcNow);

            // NEWS refresh (for overlay countdown even when no ticks)
            News_InitOrRefresh(utcNow);

            bool timeBlocked = false;

            // ------------------------------------------------------------
            // Trading Window gate / ForceFlat (time-based) (JST)
            // ------------------------------------------------------------
            if (EnableTradingWindowFilter)
            {
                // 早期閉場耐性：当日市場クローズに追従した ForceFlat 時刻を更新
                UpdateEffectiveForceFlatTime(utcNow);

                // 市場追従 ForceFlat（早期閉場・休場・ティック停止耐性）
                EnforceMarketFollowForceFlat(utcNow);

                TradingWindowState state = GetTradingWindowState(jstNow);

                if (state != TradingWindowState.AllowNewEntries)
                {
                    timeBlocked = true;
                    DateTime? untilUtc = ComputeTradingWindowUnblockUtc(utcNow);
                    UpdateStopOverlay(true, "取引時間", utcNow, untilUtc);
                }

                // ForceFlat 到達時の強制決済は CloseReason=MarketClose
                if (state == TradingWindowState.ForceFlat)
                {
                    CloseAllPositionsForMarketClose("ForceFlatTime", null);
                }
            }

            // ------------------------------------------------------------
            // NEWS gate (new entries only) (UTC)
            // ------------------------------------------------------------
            if (!timeBlocked)
            {
                if (!IsNewEntryAllowed(utcNow, out string newsReason))
                {
                    DateTime? untilUtc = ComputeNewsUnblockUtc(utcNow);
                    UpdateStopOverlay(true, "経済指標", utcNow, untilUtc);
                }
                else
                {
                    // clear overlay when both gates allow new entries
                    UpdateStopOverlay(false, "", utcNow, null);
                }
            }
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

        void ApplyTimeStopIfNeeded()
        {
            if (!UseTimeStop)
                return;

            int maxHold = Math.Max(0, MaxHoldMinutes);
            if (maxHold <= 0)
                return;

            DateTime now = UtcNow();

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
                if (p.Label != BOT_LABEL) continue;

                if (_timeStopCloseRequested.Contains(p.Id))
                    continue;

                TimeSpan held = now - p.EntryTime;
                if (held.TotalMinutes >= maxHold)
                {
                    _timeStopCloseRequested.Add(p.Id);

                    _closeInitiatorByPosId[p.Id] = "TIME_STOP";

                    var res = ClosePosition(p);

                    Print(
                        "TIME_STOP_CLOSE | CodeName={0} | PosId={1} | HeldMin={2} >= MaxHoldMin={3}{4}",
                        CODE_NAME,
                        p.Id,
                        held.TotalMinutes.ToString("F1", CultureInfo.InvariantCulture),
                        maxHold,
                        BuildTimeTag(now)
                    );
                }
            }
        }

        void ApplyEmergencyCloseIfNeeded()
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
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
                if (p.Label != BOT_LABEL) continue;
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

        // ============================
        // Safety: SL/TP missing protection immediate close (all modes)
        // - If any position is created without BOTH SL and TP, immediately close it.
        // - This does NOT "fix" SL/TP (no re-attach). It exits to comply with fixed-SL policy.
        // ============================
        private void MissingProtectionImmediateClose()
        {
            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
                if (p.Label != BOT_LABEL) continue;

                // Must have BOTH SL and TP at all times (entry-time mandatory).
                bool missing = (!p.StopLoss.HasValue) || (!p.TakeProfit.HasValue);
                if (!missing)
                    continue;

                if (_missingProtectionCloseRequested.Contains(p.Id))
                    continue;

                _missingProtectionCloseRequested.Add(p.Id);
                _closeInitiatorByPosId[p.Id] = "MISSING_PROTECTION";

                var res = ClosePosition(p);

                Print(
                    "MISSING_PROTECTION_CLOSE | CodeName={0} | PosId={1} | SL={2} | TP={3}{4}",
                    CODE_NAME,
                    p.Id,
                    (p.StopLoss.HasValue ? p.StopLoss.Value.ToString("F5", CultureInfo.InvariantCulture) : "null"),
                    (p.TakeProfit.HasValue ? p.TakeProfit.Value.ToString("F5", CultureInfo.InvariantCulture) : "null"),
                    BuildTimeTag(UtcNow())
                );

                if (res == null || !res.IsSuccessful)
                    _missingProtectionCloseRequested.Remove(p.Id);
            }
        }

        // TPブースト（条件付き：含み益R到達で構造TPへ切替）
        // - CORE探索OFFの挙動を壊さず、伸び相場のみ上積みするためのブースト
        // - EnableStructureTpBoost=OFF の場合は完全に無効（既存挙動と同等）
        // ============================
        private void ApplyStructureTpBoostIfNeeded()
        {
            if (!EnableStructureTpBoost)
                return;

            // TPを使わない設定なら何もしない
            if (!EffectiveEnableTakeProfit())
                return;


            // TP方式が「構造TP」または「固定TP」以外の場合：構造TPブーストは無効
            if (TpMode != TP方式.構造 && TpMode != TP方式.固定)
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
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
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

                    // Stage1: 構造TP算出（通常の構造TPパラメータを使用）
                    if (!TryGetStructureTakeProfit(p.TradeType, p.EntryPrice, out double tpAbs))
                        continue;

                    double? oldTpBefore = p.TakeProfit;

                    // Boost-only: enforce minimum TP distance
                    tpAbs = ApplyMinTpDistanceBoostOnly(p.TradeType, p.EntryPrice, tpAbs);

                    // Boost semantics: only EXTEND TP (never tighten)
                    if (p.TakeProfit.HasValue)
                    {
                        double eps = Symbol.PipSize * 0.1;
                        if (p.TradeType == TradeType.Buy)
                        {
                            if (tpAbs <= p.TakeProfit.Value + eps)
                                continue;
                        }
                        else
                        {
                            if (tpAbs >= p.TakeProfit.Value - eps)
                                continue;
                        }
                    }

                    // ALREADY_SET (same TP)
                    if (p.TakeProfit.HasValue && Math.Abs(p.TakeProfit.Value - tpAbs) <= (Symbol.PipSize * 0.1))
                    {
                        _structureTpBoostedPosIds.Add(p.Id);


                        // TPブースト分岐出口：ブースト検出を記録（1回のみ）
                        if (EnableTpBoostBranchExit)
                        {
                            bool alreadyDetected = _tpBoostDetectedByPosId.TryGetValue(p.Id, out bool det) && det;
                            if (!BranchExitOnlyOncePerPosition || !alreadyDetected)
                            {
                                _tpBoostDetectedByPosId[p.Id] = true;
                                _tpBoostPartialClosedByPosId[p.Id] = false;

                                double baseTp;
                                if (_tpBoostBaseTpPriceByPosId.TryGetValue(p.Id, out double storedBaseTp))
                                    baseTp = storedBaseTp;
                                else if (oldTpBefore.HasValue)
                                    baseTp = oldTpBefore.Value;
                                else
                                    baseTp = p.EntryPrice; // fallback
                                _tpBoostBaseTpPriceByPosId[p.Id] = baseTp;

                                _tpBoostOldTpByPosId[p.Id] = oldTpBefore;
                                _tpBoostNewTpByPosId[p.Id] = tpAbs;

                                if (EnableProReport)
                                {
                                    Print("TP_BOOST_DETECTED | CodeName={0} | PosId={1} | Stage=1 | BaseTpPrice={2} | OldTP={3} | NewTP={4}{5}",
                                        CODE_NAME, p.Id,
                                        baseTp.ToString(CultureInfo.InvariantCulture),
                                        (oldTpBefore.HasValue ? oldTpBefore.Value.ToString(CultureInfo.InvariantCulture) : "null"),
                                        tpAbs.ToString(CultureInfo.InvariantCulture),
                                        BuildTimeTag(utcNow));
                                }
                            }
                        }

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


                        // TPブースト分岐出口：ブースト検出を記録（1回のみ）
                        if (EnableTpBoostBranchExit)
                        {
                            bool alreadyDetected = _tpBoostDetectedByPosId.TryGetValue(p.Id, out bool det) && det;
                            if (!BranchExitOnlyOncePerPosition || !alreadyDetected)
                            {
                                _tpBoostDetectedByPosId[p.Id] = true;
                                _tpBoostPartialClosedByPosId[p.Id] = false;

                                double baseTp;
                                if (_tpBoostBaseTpPriceByPosId.TryGetValue(p.Id, out double storedBaseTp))
                                    baseTp = storedBaseTp;
                                else if (oldTpBefore.HasValue)
                                    baseTp = oldTpBefore.Value;
                                else
                                    baseTp = p.EntryPrice; // fallback
                                _tpBoostBaseTpPriceByPosId[p.Id] = baseTp;

                                _tpBoostOldTpByPosId[p.Id] = oldTpBefore;
                                _tpBoostNewTpByPosId[p.Id] = tpAbs;

                                if (EnableProReport)
                                {
                                    Print("TP_BOOST_DETECTED | CodeName={0} | PosId={1} | Stage=1 | BaseTpPrice={2} | OldTP={3} | NewTP={4}{5}",
                                        CODE_NAME, p.Id,
                                        baseTp.ToString(CultureInfo.InvariantCulture),
                                        (oldTpBefore.HasValue ? oldTpBefore.Value.ToString(CultureInfo.InvariantCulture) : "null"),
                                        tpAbs.ToString(CultureInfo.InvariantCulture),
                                        BuildTimeTag(utcNow));
                                }
                            }
                        }

                        TryPrintOncePerBar(p.Id, "S1_MODIFY_OK",
                            string.Format(CultureInfo.InvariantCulture,
                                "STRUCT_TP_S1_MODIFY_OK | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | UnrealizedR={5}{6} | Lookback={7} | BufferPips={8}{9}",
                                CODE_NAME, BOT_LABEL, SymbolName, p.TradeType, p.Id,
                                unrealizedR.ToString("F2", CultureInfo.InvariantCulture),
                                BuildTpDiag(p, tpAbs),
                                TpSwingLookback,
                                StructureTpBufferPips.ToString("F1", CultureInfo.InvariantCulture),
                                BuildTimeTag(utcNow)));
                    }
                    else
                    {
                        // Modify failed or was skipped by guards (e.g., time-gate)
                        TryPrintOncePerBar(p.Id, "S1_MODIFY_FAIL",
                            string.Format(CultureInfo.InvariantCulture,
                                "STRUCT_TP_S1_MODIFY_FAIL | CodeName={0} | Label={1} | Symbol={2} | Type={3} | PositionId={4} | UnrealizedR={5}{6} | Lookback={7} | BufferPips={8} | Reason=ModifySkippedOrFailed{9}",
                                CODE_NAME, BOT_LABEL, SymbolName, p.TradeType, p.Id,
                                unrealizedR.ToString("F2", CultureInfo.InvariantCulture),
                                BuildTpDiag(p, tpAbs),
                                TpSwingLookback,
                                StructureTpBufferPips.ToString("F1", CultureInfo.InvariantCulture),
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

                    // Stage2 Exit (Fractal swing) context init
                    int s2StartM15Index = GetM15IndexAtTime(utcNow);

                    if (_structureTpBoostedPosIdsStage2.Add(p.Id))
                    {
                        if (EnableProReport)
                        {
                            Print("TP_BOOST_DETECTED | CodeName={0} | PosId={1} | Stage=2 | StartM15Index={2} | FractalLR={3} | UseCloseConfirm={4} | TriggerR>={5} | UnrealizedR={6}{7}",
                                CODE_NAME,
                                p.Id,
                                s2StartM15Index,
                                Math.Max(1, Stage2ExitFractalLR),
                                (Stage2ExitUseCloseConfirm ? "YES" : "NO"),
                                r2.ToString("F2", CultureInfo.InvariantCulture),
                                unrealizedR.ToString("F2", CultureInfo.InvariantCulture),
                                BuildTimeTag(utcNow));
                        }
                    }

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

        // ============================================================
        // TPブースト分岐出口（方式A）
        // - ブースト検出済みのポジションのみ、TP30相当到達で部分利確を実行
        // - その後のTPは既存の構造TPブーストで既に延長済み（ApplyStructureTpBoostIfNeeded内）
        // ============================================================
        private void ApplyTpBoostBranchPartialCloseIfNeeded()
        {
            if (!EnableTpBoostBranchExit || !PartialCloseOnTpBoost)
                return;

            foreach (var p in Positions)
            {
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName))
                    continue;

                if (!_tpBoostDetectedByPosId.TryGetValue(p.Id, out bool detected) || !detected)
                    continue;

                if (BranchExitOnlyOncePerPosition &&
                    _tpBoostPartialClosedByPosId.TryGetValue(p.Id, out bool alreadyClosed) && alreadyClosed)
                    continue;

                // Trigger mode: OnBoostDetect はブースト検出直後に部分利確（即時）
                if (PartialCloseTriggerMode == PartialCloseTriggerModeEnum.OnBoostDetect)
                {
                    TryPartialClose(p, "OnBoostDetect");
                    continue;
                }

                // OnTpHit: 固定TP(pips)相当の到達で部分利確（TP自体は構造TPへ延長されている前提）
                if (!_tpBoostBaseTpPriceByPosId.TryGetValue(p.Id, out double baseTpPrice))
                    continue;

                bool reached = false;
                if (p.TradeType == TradeType.Buy)
                    reached = Symbol.Bid >= baseTpPrice;
                else
                    reached = Symbol.Ask <= baseTpPrice;

                if (reached)
                    TryPartialClose(p, "OnTpHit");
            }
        }

        private void TryPartialClose(Position p, string trigger)
        {
            if (p == null)
                return;
            long currentUnits = (long)p.VolumeInUnits;
            if (currentUnits <= 0)
                return;

            // 量の丸め（Min/Step に揃える）
            long targetUnitsRaw = (long)Math.Round(currentUnits * PartialClosePercent, MidpointRounding.AwayFromZero);
            long minUnits = (long)Symbol.VolumeInUnitsMin;
            long stepUnits = (long)Symbol.VolumeInUnitsStep;

            if (stepUnits <= 0)
                stepUnits = minUnits;

            long targetUnits = (targetUnitsRaw / stepUnits) * stepUnits;

            // 最低数量未満ならスキップ
            if (targetUnits < minUnits)
            {
                if (EnableProReport)
                    Print("PARTIAL_CLOSE_SKIPPED | CodeName={0} | PosId={1} | Reason=VolumeTooSmall | CurrentUnits={2} | TargetRaw={3} | TargetUnits={4} | MinUnits={5} | StepUnits={6}",
                        CODE_NAME, p.Id, currentUnits, targetUnitsRaw, targetUnits, minUnits, stepUnits);
                return;
            }

            // 全量になってしまう場合はスキップ（部分利確の意味がない）
            if (targetUnits >= currentUnits)
            {
                if (EnableProReport)
                    Print("PARTIAL_CLOSE_SKIPPED | CodeName={0} | PosId={1} | Reason=TargetIsFull | CurrentUnits={2} | TargetUnits={3}",
                        CODE_NAME, p.Id, currentUnits, targetUnits);
                return;
            }

            _closeInitiatorByPosId[p.Id] = "PARTIAL_CLOSE:TP_BOOST:" + trigger;

            var res = ClosePosition(p, targetUnits);

            if (EnableProReport)
            {
                Print("PARTIAL_CLOSE_APPLIED | CodeName={0} | PosId={1} | Trigger={2} | CloseUnits={3} | CurrentUnits={4} | Bid={5} | Ask={6}",
                    CODE_NAME, p.Id, trigger, targetUnits, currentUnits, Symbol.Bid, Symbol.Ask);
            }

            if (res != null && res.IsSuccessful)
            {
                _tpBoostPartialClosedByPosId[p.Id] = true;
            }
            else
            {
                if (EnableProReport)
                    Print("PARTIAL_CLOSE_FAILED | CodeName={0} | PosId={1} | Trigger={2} | CloseUnits={3} | Error={4}",
                        CODE_NAME, p.Id, trigger, targetUnits, res == null ? "NULL" : res.Error.ToString());
            }
        }


        // ============================================================
        // SLイベント：建値移動（BE）
        // - SLイベント管理を有効化=はい かつ 建値移動を有効化=はい の場合のみ評価
        // - 建値移動トリガー>0 のとき、含み益USD到達で SL を建値（＋オフセット）へ移動
        // - 建値移動は1回のみ（内部固定：_beAppliedPosIds）
        // - TPは変更しない（TPは別ロジックで自由に変更可）
        // ============================================================
        private bool RequestMoveStopLoss(Position p, double newStopLoss, string eventType, DateTime utcNow)
        {
            if (p == null)
                return false;

            // SLイベント管理OFFなら何もしない
            if (!EnableSLEventManagement)
                return false;

            // SL未設定は内部ルールで即時クローズ対象（別処理）だが、ここでも安全に拒否
            if (!p.StopLoss.HasValue)
                return false;

            double currentSl = p.StopLoss.Value;

            // 「SLを消す」は禁止
            if (double.IsNaN(newStopLoss) || double.IsInfinity(newStopLoss))
                return false;

            // 不利方向への移動は禁止（リスク悪化を許さない）
            if (p.TradeType == TradeType.Buy)
            {
                if (newStopLoss + Symbol.PipSize * 0.01 < currentSl)
                    return false;
            }
            else
            {
                if (newStopLoss - Symbol.PipSize * 0.01 > currentSl)
                    return false;
            }

            // 価格正規化
            double slNorm = NormalizePrice(newStopLoss);

            // TPは現状維持
            double? tp = p.TakeProfit;

            // 反映（SLのみ更新、TPは現状値を再送）
            TradeResult modRes = ModifyPosition(p, slNorm, tp, ProtectionType.Absolute);

            bool ok = (modRes != null && modRes.IsSuccessful);

            if (EnableProReport)
            {
                TryPrintOncePerBar(p.Id, ok ? "SL_EVENT_APPLIED" : "SL_EVENT_SKIPPED",
                    string.Format(CultureInfo.InvariantCulture,
                        ok
                            ? "SL_EVENT_APPLIED | CodeName={0} | Symbol={1} | PosId={2} | Event={3} | OldSL={4} | NewSL={5} | TP={6}{7}"
                            : "SL_EVENT_SKIPPED | CodeName={0} | Symbol={1} | PosId={2} | Event={3} | OldSL={4} | NewSL={5} | TP={6} | Reason=ModifyFailed{7}",
                        CODE_NAME,
                        SymbolName,
                        p.Id,
                        string.IsNullOrWhiteSpace(eventType) ? "NA" : eventType,
                        currentSl.ToString("F5", CultureInfo.InvariantCulture),
                        slNorm.ToString("F5", CultureInfo.InvariantCulture),
                        (tp.HasValue ? tp.Value.ToString("F5", CultureInfo.InvariantCulture) : "null"),
                        BuildTimeTag(utcNow)));
            }

            return ok;
        }


        private void ApplySLEventManagementIfNeeded()
        {
            // マスターOFFなら無効
            if (!EnableSLEventManagement)
                return;

            DateTime utcNow = UtcNow();

            // デバッグ：SLイベント判定ログ（挙動は変えない）
            LogSLEventCheckForAllPositions(utcNow, "SLEVENT_TICK");

            // 同一Tickで複数イベントを連続適用しない（衝突防止の保険）
            if (_slEventExecutedThisTick)
                return;

            int mode = SLイベント優先順位_固定値;

            // 優先順位に従って「成立した最初の1つ」だけ実行する
            bool applied = false;
            switch (mode)
            {
                case 0:
                    applied = ApplyBreakevenMoveIfNeeded_Internal();
                    if (!applied) applied = ApplyPartialCloseIfNeeded();
                    if (!applied) applied = ApplyStepSlMoveIfNeeded();
                    break;

                case 1:
                    applied = ApplyBreakevenMoveIfNeeded_Internal();
                    if (!applied) applied = ApplyStepSlMoveIfNeeded();
                    if (!applied) applied = ApplyPartialCloseIfNeeded();
                    break;

                case 2:
                    applied = ApplyPartialCloseIfNeeded();
                    if (!applied) applied = ApplyBreakevenMoveIfNeeded_Internal();
                    if (!applied) applied = ApplyStepSlMoveIfNeeded();
                    break;

                case 3:
                    applied = ApplyPartialCloseIfNeeded();
                    if (!applied) applied = ApplyStepSlMoveIfNeeded();
                    if (!applied) applied = ApplyBreakevenMoveIfNeeded_Internal();
                    break;

                case 4:
                    applied = ApplyStepSlMoveIfNeeded();
                    if (!applied) applied = ApplyBreakevenMoveIfNeeded_Internal();
                    if (!applied) applied = ApplyPartialCloseIfNeeded();
                    break;

                case 5:
                default:
                    applied = ApplyStepSlMoveIfNeeded();
                    if (!applied) applied = ApplyPartialCloseIfNeeded();
                    if (!applied) applied = ApplyBreakevenMoveIfNeeded_Internal();
                    break;
            }

            if (applied)
                _slEventExecutedThisTick = true;
        }

        private void LogSLEventCheckForAllPositions(DateTime utcNow, string phase)
        {
            if (!EnableProReport)
                return;

            int intervalSec = Math.Max(1, SLEventCheckLogIntervalSeconds);
            int maxPerPos = Math.Max(1, SLEventCheckLogMaxPerPosition);

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
                if (p.Label != BOT_LABEL) continue;

                int cnt;
                if (_slEventCheckLogCount.TryGetValue(p.Id, out cnt))
                {
                    if (cnt >= maxPerPos)
                        continue;
                }
                else
                {
                    cnt = 0;
                }

                DateTime lastUtc;
                if (_slEventCheckLastLogUtc.TryGetValue(p.Id, out lastUtc))
                {
                    if ((utcNow - lastUtc).TotalSeconds < intervalSec)
                        continue;
                }

                _slEventCheckLastLogUtc[p.Id] = utcNow;
                _slEventCheckLogCount[p.Id] = cnt + 1;

                TryPrintSLEventCheck(p, utcNow, phase);
            }
        }

        private void TryPrintSLEventCheck(Position p, DateTime utcNow, string phase)
        {
            try
            {
                double internalPips = p.Pips;
                double userPips = InternalPipsToInputPips(internalPips);

                bool beEnabled = EnableBreakevenMove && BreakevenTriggerPips > 0;
                bool pcEnabled = EnablePartialClose && PartialCloseTriggerPips > 0;
                bool stepEnabled = EnableStepSlMove && (StepSlStage1TriggerPips > 0 || StepSlStage2TriggerPips > 0);

                bool beApplied = _beAppliedPosIds.Contains(p.Id);
                bool pcApplied = _partialCloseAppliedPosIds.Contains(p.Id);
                bool s1Applied = _stepSlStage1AppliedPosIds.Contains(p.Id);
                bool s2Applied = _stepSlStage2AppliedPosIds.Contains(p.Id);

                bool beReached = beEnabled && !beApplied && (userPips + 1e-12 >= BreakevenTriggerPips);
                bool pcReached = pcEnabled && !pcApplied && (userPips + 1e-12 >= PartialCloseTriggerPips);
                bool s1Reached = stepEnabled && StepSlStage1TriggerPips > 0 && !s1Applied && (userPips + 1e-12 >= StepSlStage1TriggerPips);
                bool s2Reached = stepEnabled && StepSlStage2TriggerPips > 0 && !s2Applied && (userPips + 1e-12 >= StepSlStage2TriggerPips);

                Print(string.Format(CultureInfo.InvariantCulture,
                    "SLEVENT_CHECK | Phase={0} | CodeName={1} | Symbol={2} | Scale={3} | TrendActive={4} | Priority={5} | PosId={6} | Type={7} | PipsInternal={8} | PipsUser={9} | BE({10}:{11}:{12}:{13}) | PC({14}:{15}:{16}:{17}:{18}) | S1({19}:{20}:{21}:{22}:{23}) | S2({24}:{25}:{26}:{27}:{28}) | Entry={29} | SL={30} | TP={31}{32}",
                    string.IsNullOrWhiteSpace(phase) ? "NA" : phase,
                    CODE_NAME,
                    SymbolName,
                    _pipsScale.ToString(CultureInfo.InvariantCulture),
                    "false",
                    SLイベント優先順位_固定値.ToString(CultureInfo.InvariantCulture),
                    p.Id,
                    p.TradeType.ToString(),
                    internalPips.ToString("F1", CultureInfo.InvariantCulture),
                    userPips.ToString("F1", CultureInfo.InvariantCulture),

                    // BE
                    beEnabled ? "ON" : "OFF",
                    BreakevenTriggerPips.ToString(CultureInfo.InvariantCulture),
                    beReached ? "reached" : "-",
                    beApplied ? "applied" : "-",

                    // PC
                    pcEnabled ? "ON" : "OFF",
                    PartialCloseTriggerPips.ToString(CultureInfo.InvariantCulture),
                    PartialClosePercentSLEvent.ToString("F1", CultureInfo.InvariantCulture),
                    pcReached ? "reached" : "-",
                    pcApplied ? "applied" : "-",

                    // S1
                    EnableStepSlMove ? "ON" : "OFF",
                    StepSlStage1TriggerPips.ToString(CultureInfo.InvariantCulture),
                    StepSlStage1MovePips.ToString(CultureInfo.InvariantCulture),
                    s1Reached ? "reached" : "-",
                    s1Applied ? "applied" : "-",

                    // S2
                    EnableStepSlMove ? "ON" : "OFF",
                    StepSlStage2TriggerPips.ToString(CultureInfo.InvariantCulture),
                    StepSlStage2MovePips.ToString(CultureInfo.InvariantCulture),
                    s2Reached ? "reached" : "-",
                    s2Applied ? "applied" : "-",

                    p.EntryPrice.ToString("F5", CultureInfo.InvariantCulture),
                    (p.StopLoss.HasValue ? p.StopLoss.Value.ToString("F5", CultureInfo.InvariantCulture) : "null"),
                    (p.TakeProfit.HasValue ? p.TakeProfit.Value.ToString("F5", CultureInfo.InvariantCulture) : "null"),
                    BuildTimeTag(utcNow)
                ));
            }
            catch
            {
                // no-op
            }
        }
        private bool ApplyBreakevenMoveIfNeeded_Internal()
        {
            // 建値移動OFFなら無効
            if (!EnableBreakevenMove)
                return false;

            int triggerPips = Math.Max(0, BreakevenTriggerPips);
            if (triggerPips <= 0)
                return false;

            double offsetPips = Math.Max(0.0, BreakevenSLOffsetPips);

            DateTime utcNow = UtcNow();

            bool anyApplied = false;

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
                if (p.Label != BOT_LABEL) continue;

                // SL/TP欠損は別ルールで即時クローズ対象（監視処理で処理される）
                if (!p.StopLoss.HasValue || !p.TakeProfit.HasValue)
                    continue;

                // 建値移動は1回のみ（内部固定）
                if (_beAppliedPosIds.Contains(p.Id))
                    continue;

                double pips = InternalPipsToInputPips(p.Pips);
                bool reached = (pips + 1e-12 >= triggerPips);

                if (EnableProReport)
                {
                    TryPrintOncePerBar(p.Id, "BE_CHECK",
                        string.Format(CultureInfo.InvariantCulture,
                            "BE_CHECK | CodeName={0} | Symbol={1} | PosId={2} | Type={3} | Pips={4} | TriggerPips={5} | Reached={6} | Entry={7} | SL={8} | TP={9} | OffsetPips={10}{11}",
                            CODE_NAME,
                            SymbolName,
                            p.Id,
                            p.TradeType,
                            pips.ToString("F1", CultureInfo.InvariantCulture),
                            triggerPips.ToString(CultureInfo.InvariantCulture),
                            reached ? "true" : "false",
                            p.EntryPrice.ToString("F5", CultureInfo.InvariantCulture),
                            (p.StopLoss.HasValue ? p.StopLoss.Value.ToString("F5", CultureInfo.InvariantCulture) : "null"),
                            (p.TakeProfit.HasValue ? p.TakeProfit.Value.ToString("F5", CultureInfo.InvariantCulture) : "null"),
                            offsetPips.ToString("F2", CultureInfo.InvariantCulture),
                            BuildTimeTag(utcNow)));
                }

                if (!reached)
                    continue;

                double entry = p.EntryPrice;

                // 既に建値以上/以下に来ている場合は「既にBE済み扱い」にして内部フラグを立てる
                if (p.TradeType == TradeType.Buy)
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value + Symbol.PipSize * 0.01 >= entry)
                    {
                        _beAppliedPosIds.Add(p.Id);
                        continue;
                    }

                    double offsetPrice = UserPipsToPrice(offsetPips);
                    double newSl = entry + offsetPrice;
                    bool ok = RequestMoveStopLoss(p, newSl, "BE", utcNow);
                    if (ok)
                    {
                        _beAppliedPosIds.Add(p.Id);
                        anyApplied = true;
                        break; // 1Tickで多重適用を避ける
                    }
                }
                else
                {
                    if (p.StopLoss.HasValue && p.StopLoss.Value - Symbol.PipSize * 0.01 <= entry)
                    {
                        _beAppliedPosIds.Add(p.Id);
                        continue;
                    }

                    double offsetPrice = UserPipsToPrice(offsetPips);
                    double newSl = entry - offsetPrice;
                    bool ok = RequestMoveStopLoss(p, newSl, "BE", utcNow);
                    if (ok)
                    {
                        _beAppliedPosIds.Add(p.Id);
                        anyApplied = true;
                        break; // 1Tickで多重適用を避ける
                    }
                }
            }

            return anyApplied;
        }

        private bool ApplyPartialCloseIfNeeded()
        {
            if (!EnablePartialClose)
                return false;

            int triggerPips = Math.Max(0, PartialCloseTriggerPips);
            if (triggerPips <= 0)
                return false;

            double pct = PartialClosePercentSLEvent;
            if (double.IsNaN(pct) || double.IsInfinity(pct))
                return false;

            // 1〜99%
            pct = Math.Max(1.0, Math.Min(99.0, pct));

            DateTime utcNow = UtcNow();

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
                if (p.Label != BOT_LABEL) continue;

                if (_partialCloseAppliedPosIds.Contains(p.Id))
                    continue;

                // 含み益pipsで判定（負け側は実行しない）
                double pips = InternalPipsToInputPips(p.Pips);
                bool reached = (pips + 1e-12 >= triggerPips);
                if (!reached)
                    continue;

                double vol = p.VolumeInUnits;
                if (vol <= 0)
                    continue;

                double rawClose = vol * (pct / 100.0);
                double volToClose = Symbol.NormalizeVolumeInUnits(rawClose, RoundingMode.ToNearest);

                // 最低単位未満は不成立扱い
                if (volToClose < Symbol.VolumeInUnitsMin)
                    continue;

                // 残高0になるのを避ける（完全決済は本イベントの目的外）
                if (volToClose >= vol)
                {
                    double maxClose = vol - Symbol.VolumeInUnitsMin;
                    maxClose = Symbol.NormalizeVolumeInUnits(maxClose, RoundingMode.ToNearest);
                    if (maxClose < Symbol.VolumeInUnitsMin)
                        continue;
                    volToClose = maxClose;
                }

                if (EnableProReport)
                {
                    TryPrintOncePerBar(p.Id, "PC_CHECK",
                        string.Format(CultureInfo.InvariantCulture,
                            "PC_CHECK | CodeName={0} | Symbol={1} | PosId={2} | Pips={3} | TriggerPips={4} | Percent={5} | Volume={6} | CloseVol={7}{8}",
                            CODE_NAME, SymbolName, p.Id,
                            pips.ToString("F1", CultureInfo.InvariantCulture),
                            triggerPips.ToString(CultureInfo.InvariantCulture),
                            pct.ToString("F1", CultureInfo.InvariantCulture),
                            vol.ToString(CultureInfo.InvariantCulture),
                            volToClose.ToString(CultureInfo.InvariantCulture),
                            BuildTimeTag(utcNow)));
                }

                TradeResult res = ClosePosition(p, volToClose);
                bool ok = (res != null && res.IsSuccessful);

                if (ok)
                {
                    _partialCloseAppliedPosIds.Add(p.Id);

                    if (EnableProReport)
                    {
                        TryPrintOncePerBar(p.Id, "PC_APPLIED",
                            string.Format(CultureInfo.InvariantCulture,
                                "PC_APPLIED | CodeName={0} | Symbol={1} | PosId={2} | CloseVol={3} | Percent={4} | TriggerPips={5}{6}",
                                CODE_NAME, SymbolName, p.Id,
                                volToClose.ToString(CultureInfo.InvariantCulture),
                                pct.ToString("F1", CultureInfo.InvariantCulture),
                                triggerPips.ToString(CultureInfo.InvariantCulture),
                                BuildTimeTag(utcNow)));
                    }

                    return true; // 1Tickで多重適用を避ける
                }
                else
                {
                    if (EnableProReport)
                    {
                        TryPrintOncePerBar(p.Id, "PC_SKIPPED",
                            string.Format(CultureInfo.InvariantCulture,
                                "PC_SKIPPED | CodeName={0} | Symbol={1} | PosId={2} | Reason=CloseFailed{3}",
                                CODE_NAME, SymbolName, p.Id, BuildTimeTag(utcNow)));
                    }
                }
            }

            return false;
        }

        private bool ApplyStepSlMoveIfNeeded()
        {
            if (!EnableStepSlMove)
                return false;

            int trig1 = Math.Max(0, StepSlStage1TriggerPips);
            int trig2 = Math.Max(0, StepSlStage2TriggerPips);
            int move1 = Math.Max(0, StepSlStage1MovePips);
            int move2 = Math.Max(0, StepSlStage2MovePips);

            if (trig1 <= 0 && trig2 <= 0)
                return false;

            DateTime utcNow = UtcNow();

            for (int i = Positions.Count - 1; i >= 0; i--)
            {
                Position p = Positions[i];
                if (p == null) continue;
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
                if (p.Label != BOT_LABEL) continue;

                if (!p.StopLoss.HasValue)
                    continue;

                double pips = InternalPipsToInputPips(p.Pips);
                if (pips < 0)
                    continue;

                double entry = p.EntryPrice;

                // 第2段階を優先（同一ポジで上位段階を先に）
                if (trig2 > 0 && pips + 1e-12 >= trig2 && !_stepSlStage2AppliedPosIds.Contains(p.Id))
                {
                    double newSl = (p.TradeType == TradeType.Buy)
                        ? entry + UserPipsToPrice(move2)
                        : entry - UserPipsToPrice(move2);

                    bool ok = RequestMoveStopLoss(p, newSl, "STEP2", utcNow);
                    if (ok)
                    {
                        _stepSlStage2AppliedPosIds.Add(p.Id);
                        return true;
                    }
                }

                if (trig1 > 0 && pips + 1e-12 >= trig1 && !_stepSlStage1AppliedPosIds.Contains(p.Id))
                {
                    double newSl = (p.TradeType == TradeType.Buy)
                        ? entry + UserPipsToPrice(move1)
                        : entry - UserPipsToPrice(move1);

                    bool ok = RequestMoveStopLoss(p, newSl, "STEP1", utcNow);
                    if (ok)
                    {
                        _stepSlStage1AppliedPosIds.Add(p.Id);
                        return true;
                    }
                }
            }

            return false;
        }

        private void ApplyBreakevenMoveIfNeeded()
        {
            // 互換用：旧呼び出しが残っていてもpips基準で動作させる
            if (!EnableSLEventManagement)
                return;

            ApplyBreakevenMoveIfNeeded_Internal();
        }

        // ============================================================
        // CORE入口: 有効判定ユーティリティ（統一ゲート）
        // ============================================================
        private bool IsCOREModeEnabled()
        {
            // 入口ルーティング判定用：エントリーモードがCOREであるかを返す。
            return EntryMode == エントリーモード.ModeCORE;
        }


        // ============================================================
        // CORE: MinRR緩和（SET準拠）ユーティリティ
        // ============================================================
        private void ClearRrRelaxPending(string reason)
        {
            if (!_rrRelaxPendingActive)
                return;

            _rrRelaxPendingActive = false;
            _rrRelaxOriginBarIndex = -1;
            _rrRelaxReasonTag = "";

            if (EnableProReport)
            {
                Print("CORE_RR_PENDING_CLEAR | CodeName={0} | Symbol={1} | Reason={2}{3}",
                    CODE_NAME,
                    SymbolName,
                    string.IsNullOrWhiteSpace(reason) ? "NA" : reason,
                    BuildTimeTag(UtcNow()));
            }
        }


        // ============================================================
        // CORE：Direction（ヒステリシス + 状態維持）/ 距離・再接近
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

        private bool UseCOREEntrySuppressionStructure()
        {
            return EnableCOREEntrySuppressionStructure;
        }

        private bool UseCORERrRelaxStructure()
        {
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




        private bool DirectionAllowsCOREModeEmaEntry(int signalIndex, double close, double ema, TradeType intended)
        {
            bool useSuppression = UseCOREEntrySuppressionStructure();
            if (!useSuppression)
            {
                _ema20DirHoldRemaining = 0;
                return true;
            }

            double enterPips = Math.Max(0.0, DirectionDeadzonePips);
            double ratio = Math.Max(0.0, DirectionHysteresisExitEnterRatio);
            int minHoldBars = Math.Max(0, DirectionStateMinHoldBars);

            double value = GetDirectionPriceBySource(signalIndex, close);

            double epsEnterPrice = InputPipsToPrice(enterPips);
            double epsExitPrice = epsEnterPrice * ratio;

            LineSideState desired = UpdateLineSideState(_ema20SideState, value, ema, epsEnterPrice, epsExitPrice);

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

            if (minHoldBars > 0 && _ema20LastStateChangeIndex >= 0)
            {
                int barsSinceLast = signalIndex - _ema20LastStateChangeIndex;
                _ema20DirHoldRemaining = Math.Max(0, minHoldBars - barsSinceLast);
            }
            else
            {
                _ema20DirHoldRemaining = 0;
            }

            if (intended == TradeType.Buy && _ema20SideState != LineSideState.Above)
                return false;

            if (intended == TradeType.Sell && _ema20SideState != LineSideState.Below)
                return false;

            return true;
        }


        private bool TryGetEntryDeadZoneRangeUserPips(out double minPips, out double maxPips)
        {
            minPips = Math.Max(0.0, EntryDeadZoneMinPips);
            maxPips = Math.Max(0.0, EntryDeadZoneMaxPips);

            return maxPips > minPips;
        }

        private bool IsEntryDistanceInDeadZoneByInternalPips(double distInternalPips, out double distUserPips)
        {
            // GOLD(XAUUSD)でもUI入力値の意味を一定にするため、
            // 比較前に internal pips -> UI pips へ戻してから判定する。
            distUserPips = InternalPipsToInputPips(Math.Max(0.0, distInternalPips));

            if (!TryGetEntryDeadZoneRangeUserPips(out double minPips, out double maxPips))
                return false;

            return distUserPips >= minPips && distUserPips < maxPips;
        }

        private bool IsEntryDistanceInDeadZoneByPrice(double priceA, double priceB, out double distUserPips)
        {
            double distInternalPips = Math.Abs(priceA - priceB) / Symbol.PipSize;
            return IsEntryDistanceInDeadZoneByInternalPips(distInternalPips, out distUserPips);
        }



        private bool IsCOREModeDistanceOkOrSetPending(int signalIndex, double close, double ema, TradeType intended, out string distanceReason)
        {
            distanceReason = "OK";

            if (!UseCOREEntrySuppressionStructure())
                return true;

            // CORE: CORE.cbotset準拠の固定値を適用（パラメータUIは温存）
            // CORE: 露出パラメータ（エントリー関連）を適用
            double maxDistPips = Math.Max(0.0, EntryMaxDistancePips);
            int windowBars = Math.Max(1, ReapproachWindowBars);
            double reapproachMaxPips = Math.Max(0.0, ReapproachMaxDistancePips);

            double dist = Math.Abs(close - ema);
            if (IsEntryDistanceInDeadZoneByPrice(close, ema, out double distUserPips))
            {
                distanceReason = "ENTRY_DEAD_ZONE";

                if (EnableProReport)
                {
                    Print("ENTRY_DEAD_ZONE_HIT | CodeName={0} | Symbol={1} | Mode=CORE_CROSS | Type={2} | DistUserPips={3} | DeadZone=[{4},{5}){6}",
                        CODE_NAME,
                        SymbolName,
                        intended,
                        distUserPips.ToString("F1", CultureInfo.InvariantCulture),
                        EntryDeadZoneMinPips.ToString("F1", CultureInfo.InvariantCulture),
                        EntryDeadZoneMaxPips.ToString("F1", CultureInfo.InvariantCulture),
                        BuildTimeTag(UtcNow()));
                }

                return false;
            }

            double maxDistPrice = InputPipsToPrice(maxDistPips);
            if (maxDistPrice <= 0.0)
                return true;

            if (dist <= maxDistPrice)
                return true;

            // Too far -> set pending reapproach
            _ema20ReapproachPending = true;
            _ema20PendingCreatedSignalIndex = signalIndex;
            _ema20PendingTradeType = intended;
            _ema20PendingReasonTag = "EMA_CROSS";

            distanceReason = "DIST_TOO_FAR_PENDING_SET";

            if (EnableProReport)
            {
                double distPips = dist / Symbol.PipSize;
                Print("CORE_DIST_PENDING_SET | CodeName={0} | Symbol={1} | Type={2} | DistPips={3} | MaxDistPips={4} | WindowBars={5} | ReapproachMaxPips={6}{7}",
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




        private bool TryConsumeCOREModeReapproachSignal(int signalIndex, double close, double ema, out TradeType intended, out string reasonTag)
        {
            intended = TradeType.Buy;
            reasonTag = "NA";

            if (!UseCOREEntrySuppressionStructure())
                return false;

            if (!_ema20ReapproachPending || _ema20PendingCreatedSignalIndex < 0)
                return false;

            int window = Math.Max(1, ReapproachWindowBars);
            double maxPips = Math.Max(0.0, ReapproachMaxDistancePips);

            int ageBars = signalIndex - _ema20PendingCreatedSignalIndex;

            if (ageBars > window)
            {
                // expired
                _ema20ReapproachPending = false;
                _ema20PendingCreatedSignalIndex = -1;
                _ema20PendingReasonTag = "";

                if (EnableProReport)
                    Print("CORE_REAPPROACH_PENDING_EXPIRED | CodeName={0} | Symbol={1} | AgeBars={2} | Window={3}{4}",
                        CODE_NAME,
                        SymbolName,
                        ageBars,
                        window,
                        BuildTimeTag(UtcNow()));

                return false;
            }

            double maxDistPrice = InputPipsToPrice(maxPips);
            double dist = Math.Abs(close - ema);

            if (IsEntryDistanceInDeadZoneByPrice(close, ema, out double distUserPips))
            {
                if (EnableProReport)
                {
                    Print("ENTRY_DEAD_ZONE_HIT | CodeName={0} | Symbol={1} | Mode=CORE_REAPPROACH | DistUserPips={2} | DeadZone=[{3},{4}) | Action=KEEP_PENDING{5}",
                        CODE_NAME,
                        SymbolName,
                        distUserPips.ToString("F1", CultureInfo.InvariantCulture),
                        EntryDeadZoneMinPips.ToString("F1", CultureInfo.InvariantCulture),
                        EntryDeadZoneMaxPips.ToString("F1", CultureInfo.InvariantCulture),
                        BuildTimeTag(UtcNow()));
                }

                return false;
            }

            if (dist > maxDistPrice)
                return false;

            // consume
            intended = _ema20PendingTradeType;
            reasonTag = string.IsNullOrWhiteSpace(_ema20PendingReasonTag) ? "EMA_CROSS_REAPPROACH" : (_ema20PendingReasonTag + "_REAPPROACH");

            _ema20ReapproachPending = false;
            _ema20PendingCreatedSignalIndex = -1;
            _ema20PendingReasonTag = "";

            if (EnableProReport)
            {
                double distPips = dist / Symbol.PipSize;
                Print("CORE_REAPPROACH_CONSUMED | CodeName={0} | Symbol={1} | Type={2} | DistPips={3} | MaxPips={4} | AgeBars={5}/{6}{7}",
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
        // ENTRY_FRAMEWORK（EMA確定足クロス）
        // ============================================================
        private void TryEntryFramework(DateTime utcNow)
        {
            if (_emaFramework == null || _emaFramework.Result == null || _emaFramework.Result.Count < 3)
                return;

            int i1 = Bars.Count - 2;
            int i2 = Bars.Count - 3;
            if (i2 < 0)
                return;

            double close1 = Bars.ClosePrices[i1];
            double close2 = Bars.ClosePrices[i2];

            double ema1 = _emaFramework.Result[i1];
            double ema2 = _emaFramework.Result[i2];

            if (ema1 == 0.0 || ema2 == 0.0)
                return;

            bool crossUp = (close2 <= ema2) && (close1 > ema1);
            bool crossDown = (close2 >= ema2) && (close1 < ema1);
            if (!crossUp && !crossDown)
                return;

            TradeType type = crossUp ? TradeType.Buy : TradeType.Sell;

            Print("CORE_TEST_ENTRY_SIGNAL | Mode=Framework | Type={0} | Reason={1} | Close2={2} EMA2={3} | Close1={4} EMA1={5} | BarIndex={6}{7}",
                type,
                crossUp ? "EMA_CROSS_UP" : "EMA_CROSS_DOWN",
                close2,
                ema2,
                close1,
                ema1,
                i1,
                BuildTimeTag(utcNow)
            );

            Print(
                "ENTRY_MODE | CodeName={0} | Symbol={1} | EntryMode={2} | EntryType=CROSS | Dir={3}{4}",
                CODE_NAME,
                SymbolName,
                EntryMode,
                (type == TradeType.Buy ? "BUY" : "SELL"),
                BuildTimeTag(utcNow)
            );

            Print(
                "SLTP_MODE | CodeName={0} | Symbol={1} | Exit=Param{2}",
                CODE_NAME,
                SymbolName,
                BuildTimeTag(utcNow)
            );

            ExecuteEntryWithPlannedType(type, "FRAMEWORK_EMA_CROSS", close1, ema1, i1);
        }


        // ============================================================
        // COREシグナルのみ（SL/TPはパラメーター世界）
        //  - CORE かつ エントリーモード=CORE のとき使用
        // ============================================================
        // ============================================================
        // 共通エントリー執行（パラメーター世界）
        //  - TryEmaEntry / TryEntrySignalCORE_WithParamExit から共通利用
        // ============================================================

        // ============================================================
        // EMA20傾き（差分方式）で最終方向を決定（方向・判定補助）
        //  - EnableEmaSlopeDirectionDecision == true のときのみ有効
        //  - EMA20 = _ema（EMA_PERIOD_FIXED=20）を使用
        //  - abs(差分Pips) < EmaSlopeMinPips のときは plannedType を維持
        // ============================================================
        private TradeType DecideFinalDirectionByEmaSlope(TradeType plannedType, int signalBarIndex)
        {
            if (!EnableEmaSlopeDirectionDecision)
                return plannedType;

            if (_ema == null || _ema.Result == null)
                return plannedType;

            int iNow = signalBarIndex;
            int iPrev = iNow - Math.Max(1, EmaSlopeLookbackBars);

            if (iPrev < 0 || iNow < 0 || iNow >= _ema.Result.Count)
                return plannedType;

            double emaNow = _ema.Result[iNow];
            double emaPrev = _ema.Result[iPrev];

            if (emaNow == 0.0 || emaPrev == 0.0)
                return plannedType;

            double diffPips = (emaNow - emaPrev) / Symbol.PipSize;

            if (Math.Abs(diffPips) < Math.Max(0.0, EmaSlopeMinPips))
                return plannedType;

            return diffPips > 0 ? TradeType.Buy : TradeType.Sell;
        }

        private bool InitEma10Ema20IndicatorsIfNeeded(out string reason)
        {
            reason = "OK";

            if (!EnableEma10Ema20DirectionDecision)
            {
                reason = "DISABLED";
                return false;
            }

            int fastPeriod = Math.Max(1, Ema10Ema20FastPeriod);
            int slowPeriod = Math.Max(1, Ema10Ema20SlowPeriod);

            if (Bars == null || Bars.ClosePrices == null)
            {
                reason = "BARS_UNAVAILABLE";
                return false;
            }

            bool needRecreate =
                !_ema10Ema20InitDone ||
                _emaFast == null ||
                _emaSlow == null ||
                _emaFastPeriodApplied != fastPeriod ||
                _emaSlowPeriodApplied != slowPeriod;

            if (needRecreate)
            {
                _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, fastPeriod);
                _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, slowPeriod);
                _emaFastPeriodApplied = fastPeriod;
                _emaSlowPeriodApplied = slowPeriod;
                _ema10Ema20InitDone = true;
            }

            if (_emaFast == null || _emaSlow == null || _emaFast.Result == null || _emaSlow.Result == null)
            {
                reason = "EMA_NOT_READY";
                return false;
            }

            int minBarsRequired = Math.Max(fastPeriod, slowPeriod) + 2;
            if (Bars == null || Bars.Count < minBarsRequired)
            {
                reason = "BARS_NOT_READY";
                return false;
            }

            if (_emaFast.Result.Count < Bars.Count || _emaSlow.Result.Count < Bars.Count)
            {
                reason = "EMA_RESULT_NOT_READY";
                return false;
            }

            return true;
        }

        private void LogEma10Ema20GateBypass(int i1, string reason)
        {
            string dedupeKey = "BYPASS|" + i1.ToString(CultureInfo.InvariantCulture) + "|" + (reason ?? "NA");
            if (string.Equals(_ema10Ema20LastGateLogKey, dedupeKey, StringComparison.Ordinal))
                return;

            _ema10Ema20LastGateLogKey = dedupeKey;
            Print("EMA10_20_GATE_BYPASS | CodeName={0} | Symbol={1} | Reason={2}{3}",
                CODE_NAME,
                SymbolName,
                reason ?? "NA",
                BuildTimeTag(UtcNow()));
        }

        private void LogEma10Ema20GateBlocked(int i1,
                                              TradeType finalType,
                                              string modeText,
                                              double diffPips,
                                              bool equalZone,
                                              string crossInfo,
                                              string reason)
        {
            string dedupeKey =
                "BLOCK|" + i1.ToString(CultureInfo.InvariantCulture) + "|" +
                (reason ?? "NA") + "|" + finalType.ToString() + "|" + (modeText ?? "NA");
            if (string.Equals(_ema10Ema20LastGateLogKey, dedupeKey, StringComparison.Ordinal))
                return;

            _ema10Ema20LastGateLogKey = dedupeKey;
            Print(
                "EMA10_20_GATE_BLOCK | CodeName={0} | Symbol={1} | Mode={2} | FinalType={3} | DiffPips={4} | EqualZone={5} | EqualHandling={6} | CrossInfo={7} | Reason={8}{9}",
                CODE_NAME,
                SymbolName,
                modeText ?? "NA",
                finalType,
                diffPips.ToString("F4", CultureInfo.InvariantCulture),
                equalZone ? "YES" : "NO",
                Ema10Ema20EqualHandling,
                crossInfo ?? "NA",
                reason ?? "NA",
                BuildTimeTag(UtcNow()));
        }

        
        private bool CheckEma10Ema20SlopeAgreement(TradeType type, int i1, double diffPips, string modeText, string crossInfoForLog)
        {
            if (!EnableEma10Ema20SlopeAgreement)
                return true;

            if (Bars == null || i1 < 0 || i1 >= Bars.Count)
                return true;

            if (!InitEma10Ema20IndicatorsIfNeeded(out string initReason))
            {
                LogEma10Ema20GateBypass(i1, initReason);
                return true;
            }

            int slopeBars = Math.Max(1, Ema10Ema20SlopeBars);
            int back = i1 - slopeBars;
            if (back < 0)
            {
                LogEma10Ema20GateBypass(i1, "SLOPE_BACKBAR_INVALID");
                return true;
            }

            if (_emaFast == null || _emaSlow == null || _emaFast.Result == null || _emaSlow.Result == null)
            {
                LogEma10Ema20GateBypass(i1, "SLOPE_EMA_NULL");
                return true;
            }

            if (_emaFast.Result.Count <= i1 || _emaSlow.Result.Count <= i1 || _emaFast.Result.Count <= back || _emaSlow.Result.Count <= back)
            {
                LogEma10Ema20GateBypass(i1, "SLOPE_EMA_INDEX_OUT_OF_RANGE");
                return true;
            }

            if (Symbol == null || Symbol.PipSize <= 0.0)
            {
                LogEma10Ema20GateBypass(i1, "SLOPE_PIPSIZE_INVALID");
                return true;
            }

            double fastNow = _emaFast.Result[i1];
            double fastPrev = _emaFast.Result[back];
            double slowNow = _emaSlow.Result[i1];
            double slowPrev = _emaSlow.Result[back];

            double fastSlopePips = (fastNow - fastPrev) / Symbol.PipSize;
            double slowSlopePips = (slowNow - slowPrev) / Symbol.PipSize;

            double minValidPips = Math.Max(0.0, Ema10Ema20SlopeMinPips);

            double fastSign = Math.Abs(fastSlopePips) < minValidPips ? 0.0 : fastSlopePips;
            double slowSign = Math.Abs(slowSlopePips) < minValidPips ? 0.0 : slowSlopePips;

            bool ok;
            if (type == TradeType.Buy)
                ok = (fastSign > 0.0) && (slowSign > 0.0);
            else
                ok = (fastSign < 0.0) && (slowSign < 0.0);

            if (!ok)
            {
                string slopeInfo = "SLOPE_F" + fastSlopePips.ToString("0.###", CultureInfo.InvariantCulture)
                                 + "_S" + slowSlopePips.ToString("0.###", CultureInfo.InvariantCulture);

                LogEma10Ema20GateBlocked(
                    i1,
                    type,
                    modeText ?? "NA",
                    diffPips,
                    false,
                    (crossInfoForLog ?? "NA") + "|" + slopeInfo,
                    "SLOPE_MISMATCH");
                return false;
            }

            return true;
        }

private bool PassesEma10Ema20DirectionDecision(TradeType type, int i1)
        {
            if (!EnableEma10Ema20DirectionDecision)
                return true;

            int fastPeriod = Math.Max(1, Ema10Ema20FastPeriod);
            int slowPeriod = Math.Max(1, Ema10Ema20SlowPeriod);

            if (fastPeriod >= slowPeriod)
            {
                LogEma10Ema20GateBlocked(
                    i1,
                    type,
                    Ema10Ema20DecisionMode.ToString(),
                    0.0,
                    false,
                    "NA",
                    "PERIOD_ORDER_INVALID");
                return false;
            }

            if (i1 < 1 || Bars == null || i1 >= Bars.Count)
            {
                LogEma10Ema20GateBypass(i1, "BAR_INDEX_INVALID");
                return true;
            }

            if (!InitEma10Ema20IndicatorsIfNeeded(out string initReason))
            {
                LogEma10Ema20GateBypass(i1, initReason);
                return true;
            }

            if (_emaFast.Result.Count <= i1 || _emaSlow.Result.Count <= i1)
            {
                LogEma10Ema20GateBypass(i1, "EMA_INDEX_OUT_OF_RANGE");
                return true;
            }

            if (Symbol.PipSize <= 0.0)
            {
                LogEma10Ema20GateBypass(i1, "PIPSIZE_INVALID");
                return true;
            }

            double fast = _emaFast.Result[i1];
            double slow = _emaSlow.Result[i1];
            double diffPips = (fast - slow) / Symbol.PipSize;

            double deadzonePips = Math.Max(0.0, Ema10Ema20EqualDeadzonePips);
            bool equalZone = Math.Abs(diffPips) <= deadzonePips;
            if (equalZone)
            {
                if (Ema10Ema20EqualHandling == EMA10EMA20同値時の扱い.両方向許可)
                    return true;

                LogEma10Ema20GateBlocked(
                    i1,
                    type,
                    Ema10Ema20DecisionMode.ToString(),
                    diffPips,
                    true,
                    "NA",
                    "EQUAL_ZONE_BLOCK");
                return false;
            }

            if (Ema10Ema20DecisionMode == EMA10EMA20判定モード.状態)
            {
                bool allowByState = (diffPips > 0.0 && type == TradeType.Buy) ||
                                    (diffPips < 0.0 && type == TradeType.Sell);
                if (!allowByState)
                {
                    LogEma10Ema20GateBlocked(
                        i1,
                        type,
                        Ema10Ema20DecisionMode.ToString(),
                        diffPips,
                        false,
                        "NA",
                        "STATE_MISMATCH");
                }
                if (!allowByState)
                    return false;

                return CheckEma10Ema20SlopeAgreement(type, i1, diffPips, Ema10Ema20DecisionMode.ToString(), "NA");
            }

            int crossWindow = Math.Max(1, Ema10Ema20CrossValidBars);
            TradeType? lastCrossType = null;
            for (int offset = 0; offset < crossWindow; offset++)
            {
                int curr = i1 - offset;
                int prev = curr - 1;
                if (prev < 0 || curr >= _emaFast.Result.Count || curr >= _emaSlow.Result.Count)
                    break;

                double fastPrev = _emaFast.Result[prev];
                double slowPrev = _emaSlow.Result[prev];
                double fastCurr = _emaFast.Result[curr];
                double slowCurr = _emaSlow.Result[curr];

                bool goldenCross = (fastPrev <= slowPrev) && (fastCurr > slowCurr);
                bool deadCross = (fastPrev >= slowPrev) && (fastCurr < slowCurr);

                if (goldenCross)
                {
                    lastCrossType = TradeType.Buy;
                    break;
                }

                if (deadCross)
                {
                    lastCrossType = TradeType.Sell;
                    break;
                }
            }

            if (!lastCrossType.HasValue)
                return true;

            bool allowByCross = (lastCrossType.Value == type);
            if (!allowByCross)
            {
                LogEma10Ema20GateBlocked(
                    i1,
                    type,
                    Ema10Ema20DecisionMode.ToString(),
                    diffPips,
                    false,
                    lastCrossType.Value == TradeType.Buy ? "GOLDEN_CROSS" : "DEAD_CROSS",
                    "CROSS_MISMATCH");
            }

            if (!allowByCross)
                return false;

            return CheckEma10Ema20SlopeAgreement(type, i1, diffPips, Ema10Ema20DecisionMode.ToString(), lastCrossType.Value == TradeType.Buy ? "GOLDEN_CROSS" : "DEAD_CROSS");
        }

        private void ExecuteEntryWithPlannedType(TradeType plannedType, string reasonTag, double close1, double ema1, int i1)
        {
            if (!SymbolInfoTick(out double bid, out double ask))
            {
                EmitExecuteSkipJsonl("TICK_UNAVAILABLE", plannedType, i1, close1, ema1, null, null);
                return;
            }

            if (!PassesSpreadFilter(bid, ask))
            {
                if (EnableProReport && !_wasSpreadBlocked)
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
                EmitExecuteSkipJsonl("SPREAD_BLOCK", plannedType, i1, close1, ema1, bid, ask);
                return;
            }

            _wasSpreadBlocked = false;

            TradeType type = plannedType;

            // 方向・判定補助：EMA20傾き方向判定（差分方式）
            type = DecideFinalDirectionByEmaSlope(type, i1);

            double entry = (type == TradeType.Buy) ? ask : bid;
            if (IsEntryDistanceInDeadZoneByPrice(entry, ema1, out double distUserPips))
            {
                if (EnableProReport)
                {
                    Print("ENTRY_DEAD_ZONE_HIT | CodeName={0} | Symbol={1} | Mode=FINAL_EXECUTE | Type={2} | DistUserPips={3} | DeadZone=[{4},{5}){6}",
                        CODE_NAME,
                        SymbolName,
                        type,
                        distUserPips.ToString("F1", CultureInfo.InvariantCulture),
                        EntryDeadZoneMinPips.ToString("F1", CultureInfo.InvariantCulture),
                        EntryDeadZoneMaxPips.ToString("F1", CultureInfo.InvariantCulture),
                        BuildTimeTag(UtcNow()));
                }

                EmitExecuteSkipJsonl("ENTRY_DEAD_ZONE", type, i1, close1, ema1, bid, ask);
                return;
            }

            if (EnableEmaDirectionFilter)
            {
                if (type == TradeType.Buy && close1 <= ema1)
                {
                    EmitExecuteSkipJsonl("EMA_DIR_FILTER", type, i1, close1, ema1, bid, ask);
                    return;
                }
                if (type == TradeType.Sell && close1 >= ema1)
                {
                    EmitExecuteSkipJsonl("EMA_DIR_FILTER", type, i1, close1, ema1, bid, ask);
                    return;
                }
            }

            // 最終ゲート：既存の方向決定・既存フィルター後にEMA10/EMA20方向判定を適用
            if (!PassesEma10Ema20DirectionDecision(type, i1))
            {
                EmitExecuteSkipJsonl("EMA10EMA20_BLOCK", type, i1, close1, ema1, bid, ask);
                return;
            }

            // SL算出（構造=スイング or 最小SL）
            double intendedSlDistancePrice = 0.0;
            double stop = 0.0;

            if (SlMode == SL方式.構造)
            {
                if (!TryGetStructureStop(type, entry, out stop, out intendedSlDistancePrice))
                {
                    if (EnableProReport)
                        Print("STRUCTURE_SL_BLOCK | CodeName={0} | Symbol={1} | Type={2} | Action=NO_ENTRY | Reason=NO_VALID_SWING{3}",
                            CODE_NAME, SymbolName, type, BuildTimeTag(UtcNow()));

                    if (BlockEntryIfNoStructureSl)
                    {
                        EmitExecuteSkipJsonl("STRUCTURE_SL_NO_SWING", type, i1, close1, ema1, bid, ask);
                        return;
                    }
                }
                else
                {
                    double effSlPipsInternal = intendedSlDistancePrice / Symbol.PipSize;
                    double maxSlInternal = InputPipsToInternalPips(Math.Max(0.0, MaxSlPipsInput));

                    if (maxSlInternal > 0.0 && effSlPipsInternal > maxSlInternal)
                    {
                        if (EnableProReport)
                            Print("STRUCTURE_SL_BLOCK | CodeName={0} | Symbol={1} | Type={2} | Action=NO_ENTRY | Reason=MAX_SL_EXCEEDED | EffSLpips={3} MaxSLpips={4}{5}",
                                CODE_NAME, SymbolName, type,
                                effSlPipsInternal.ToString("F1", CultureInfo.InvariantCulture),
                                maxSlInternal.ToString("F1", CultureInfo.InvariantCulture),
                                BuildTimeTag(UtcNow()));
                        EmitExecuteSkipJsonl("MAX_SL_EXCEEDED", type, i1, close1, ema1, bid, ask);
                        return;
                    }
                }
            }

            if (SlMode != SL方式.構造 || intendedSlDistancePrice <= 0.0)
            {
                // 固定 / ATR の“元距離”を作る（最小SLはここでは使わない）
                double baseDistancePrice = 0.0;

                if (SlMode == SL方式.固定)
                {
                    // ※新パラメータ「固定SL（PIPS）」を price に変換して使う
                    baseDistancePrice = UserPipsToPrice(FixedSLPips);
                }
                else if (SlMode == SL方式.ATR)
                {
                    double atr = (_atrMinSl != null && _atrMinSl.Result != null && _atrMinSl.Result.Count > 0)
                        ? _atrMinSl.Result.LastValue
                        : 0.0;

                    baseDistancePrice = Math.Max(0.0, MinSlAtrMult) * atr;
                }
                else
                {
                    baseDistancePrice = 0.0;
                }

                if (baseDistancePrice <= 0.0)
                {
                    EmitExecuteSkipJsonl("SLTP_INVALID", type, i1, close1, ema1, bid, ask);
                    return;
                }

                stop = (type == TradeType.Buy) ? entry - baseDistancePrice : entry + baseDistancePrice;
                intendedSlDistancePrice = baseDistancePrice;
            }


            // 【共通】最小SL（PIPS）を最終SL距離の下限として適用（0なら無効）
            double minSlInputPips = Math.Max(0.0, MinSLPips);
            if (minSlInputPips > 0.0)
            {
                // 入力pips → 内部pips（5桁/3桁補正）→ 価格差 に変換
                double minSlInternalPips = InputPipsToInternalPips(minSlInputPips);
                double minSlPrice = minSlInternalPips * Symbol.PipSize;

                // intendedSlDistancePrice は「entry から stop までの距離(価格)」が入っている前提
                if (minSlPrice > 0.0 && intendedSlDistancePrice > 0.0 && intendedSlDistancePrice < minSlPrice)
                {
                    stop = (type == TradeType.Buy) ? entry - minSlPrice : entry + minSlPrice;
                    intendedSlDistancePrice = minSlPrice;
                }
            }

            bool useTpEntry = EffectiveEnableTakeProfit();

            double tpDistance = 0.0;

            if (useTpEntry)
            {
                // TP方式（ATR/構造/固定/SL倍率）
                switch (TpMode)
                {
                    case TP方式.固定:
                        tpDistance = UserPipsToPrice(FixedTpPips);
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

                double minTpPrice = UserPipsToPrice(MinTpDistancePips);
                if (minTpPrice > 0.0 && tpDistance < minTpPrice)
                    tpDistance = minTpPrice;

                // RRフィルタ
                double expRr = (intendedSlDistancePrice > 0.0) ? (tpDistance / intendedSlDistancePrice) : 0.0;
                double effectiveMinRr = Math.Max(0.0, MinRRRatio);

                // CORE RR緩和 Pending（CORE: SET / OFF: 露出パラメータ）
                if (UseCORERrRelaxStructure() && _rrRelaxPendingActive)
                {
                    double relaxed = MinRrRelaxedRatio;

                    effectiveMinRr = Math.Max(0.0, relaxed);
                }

                if (effectiveMinRr > 0.0 && expRr + 1e-12 < effectiveMinRr)
                {
                    if (EnableProReport)
                        Print("RR_FILTER_BLOCK | CodeName={0} | Symbol={1} | Type={2} | Action=NO_ENTRY | ExpRR={3} | MinRR={4}{5}",
                            CODE_NAME,
                            SymbolName,
                            type,
                            expRr.ToString("F2", CultureInfo.InvariantCulture),
                            effectiveMinRr.ToString("F2", CultureInfo.InvariantCulture),
                            BuildTimeTag(UtcNow()));
                    // RR不足: CORE RR緩和構造が有効なら Pending を開始して見送り（次バー以降、緩和閾値で再評価）
                    if (UseCORERrRelaxStructure() && !_rrRelaxPendingActive)
                    {
                        _rrRelaxPendingActive = true;
                        _rrRelaxOriginBarIndex = i1;
                        _rrRelaxPlannedType = type;
                        _rrRelaxReasonTag = "RR_FILTER_BLOCK";

                        if (EnableProReport)
                        {
                            Print("CORE_RR_PENDING_SET | CodeName={0} | Symbol={1} | Type={2} | ExpRR={3} | MinRR={4} | WindowBars={5}{6}",
                                CODE_NAME,
                                SymbolName,
                                type,
                                expRr.ToString("F2", CultureInfo.InvariantCulture),
                                effectiveMinRr.ToString("F2", CultureInfo.InvariantCulture),
                                MinRrRelaxWindowBars,

                                BuildTimeTag(UtcNow()));
                        }
                    }

                    EmitExecuteSkipJsonl("RR_FILTER_BLOCK", type, i1, close1, ema1, bid, ask);
                    return;
                }
            }

            // 固定 / SL倍率 TP ガード（UIは維持、ロジック側で整合）
            if (TpMode == TP方式.固定 && FixedOrSlMultTp == 固定_SL倍率_TP.SL倍率)
            {
                // 矛盾：TP方式=固定 なのに SL倍率が選ばれている -> 固定優先
                if (EnableProReport)
                    Print("TP_GUARD | CodeName={0} | Symbol={1} | Action=FORCE_FIXED | Reason=TpModeFixedButSelectorSlMult{2}",
                        CODE_NAME, SymbolName, BuildTimeTag(UtcNow()));
            }
            if (TpMode == TP方式.SL倍率 && FixedOrSlMultTp == 固定_SL倍率_TP.固定)
            {
                // 矛盾：TP方式=SL倍率 なのに 固定が選ばれている -> SL倍率優先
                if (EnableProReport)
                    Print("TP_GUARD | CodeName={0} | Symbol={1} | Action=FORCE_SLMULT | Reason=TpModeSlMultButSelectorFixed{2}",
                        CODE_NAME, SymbolName, BuildTimeTag(UtcNow()));
            }

            double tp = (type == TradeType.Buy) ? entry + tpDistance : entry - tpDistance;

            PlaceTrade(type, entry, stop, tp, reasonTag, i1);
        }


        private void TryEmaEntry()
        {
            int i1 = Bars.Count - 2; // last closed bar index
            int i2 = Bars.Count - 3;
            double? close1 = null;
            double? close2 = null;
            double? ema1 = null;
            double? ema2 = null;
            bool crossUp = false;
            bool crossDown = false;

            TradeType? plannedType = null;
            bool isRrPending = false;
            bool reapproachConsumed = false;
            string reasonTag = "NA";

            void EmitAndReturn(string finalReason, bool entryAllowed)
            {
                EmitEntryDecisionAndPendingState(
                    i1,
                    i2,
                    close1,
                    close2,
                    ema1,
                    ema2,
                    crossUp,
                    crossDown,
                    plannedType,
                    entryAllowed,
                    finalReason,
                    isRrPending,
                    reapproachConsumed);
            }

            if (i2 < 0)
            {
                EmitAndReturn("NO_BARS", false);
                return;
            }

            close1 = Bars.ClosePrices[i1];
            close2 = Bars.ClosePrices[i2];

            if (_ema == null || _ema.Result == null || _ema.Result.Count < 3)
            {
                EmitAndReturn("NO_EMA", false);
                return;
            }

            ema1 = _ema.Result[i1];
            ema2 = _ema.Result[i2];

            crossUp = close2.Value <= ema2.Value && close1.Value > ema1.Value;
            crossDown = close2.Value >= ema2.Value && close1.Value < ema1.Value;

            // ============================================================
            // EMAモード：クロス足の実体フィルタ（追加）
            //  - エントリーモードEMA かつ EMAクロス成立時のみ適用
            //  - abs(Close - Open) を PIPS換算し、指定値未満ならエントリーしない
            // ============================================================
            if (EntryMode == エントリーモード.EMA && EntryTypeEmaCross && (crossUp || crossDown))
            {
                double bodyPips = Math.Abs(Bars.ClosePrices[i1] - Bars.OpenPrices[i1]) / Symbol.PipSize;
                if (bodyPips < CrossCandleMinBodyPips)
                {
                    EmitAndReturn("CROSS_BODY_TOO_SMALL", false);
                    return;
                }
            }


            // Entry type selection
            bool useCrossEntry = EntryTypeEmaCross;

            // ============================================================
            // CORE: MinRR緩和（SET準拠） - Pending管理
            // ============================================================
            if (UseCORERrRelaxStructure())
            {
                int window = Math.Max(0, MinRrRelaxWindowBars);

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

            // ============================================================
            // CORE：CORE実在コード由来の入口抑制構造（Direction + 距離 + 再接近）
            //  - エントリーは「EMAクロス確定足」または「再接近（ウィンドウ内）」のみ
            //  - 方向は EMA20 の状態維持（ヒステリシス + 最短維持バー）
            //  - クロス直後の乖離が大きい場合は「再接近待ち」に回す
            // ============================================================
            if (UseCOREEntrySuppressionStructure())
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
                    _ema20PendingReasonTag = "";

                    // Direction gate (stateful)
                    if (!DirectionAllowsCOREModeEmaEntry(signalIndex, close1.Value, ema1.Value, plannedType.Value))
                    {
                        EmitAndReturn("DIR_BLOCK", false);
                        return;
                    }

                    // Distance gate (set pending if too far)
                    if (!IsCOREModeDistanceOkOrSetPending(signalIndex, close1.Value, ema1.Value, plannedType.Value, out string distReason))
                    {
                        EmitAndReturn(string.IsNullOrWhiteSpace(distReason) ? "DIST_TOO_FAR_PENDING_SET" : distReason, false);
                        return;
                    }
                }
                else
                {
                    // 2) クロスが無い場合は、再接近シグナルのみ許可
                    if (!TryConsumeCOREModeReapproachSignal(signalIndex, close1.Value, ema1.Value, out TradeType intended, out string reapReason))
                    {
                        EmitAndReturn("NO_SIGNAL", false);
                        return;
                    }

                    plannedType = intended;
                    reasonTag = reapReason;
                    reapproachConsumed = true;

                    if (!DirectionAllowsCOREModeEmaEntry(signalIndex, close1.Value, ema1.Value, plannedType.Value))
                    {
                        EmitAndReturn("DIR_BLOCK", false);
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
                        {
                            EmitAndReturn("NO_SIGNAL", false);
                            return;
                        }

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
                    if (close1.Value > ema1.Value)
                        plannedType = TradeType.Buy;
                    else if (close1.Value < ema1.Value)
                        plannedType = TradeType.Sell;
                    else
                    {
                        EmitAndReturn("NO_SIGNAL", false);
                        return;
                    }

                    reasonTag = "EMA_REGIME";
                }
            }

            if (plannedType == null)
            {
                EmitAndReturn("NO_SIGNAL", false);
                return;
            }

            // ============================================================
            // [ADD] HLゲート挿入位置（EntryMode=EMA のときのみ適用）
            // ============================================================
            if (UseHLFilter && EntryMode == エントリーモード.EMA)
            {
                if (!_hlReady)
                {
                    EmitAndReturn("HL_NOT_READY", false);
                    return;
                }

                bool allowLong = false;
                bool allowShort = false;
                string hlBlockReason = "";
                HL_ResolveEntryPermission(_hlDowStateH1, _hlDowStateM5, out allowLong, out allowShort, out hlBlockReason);

                if (!allowLong && !allowShort)
                {
                    EmitAndReturn("HL_NO_ENTRY", false);
                    return;
                }

                if (plannedType.Value == TradeType.Buy && !allowLong)
                {
                    EmitAndReturn("HL_BLOCK_LONG", false);
                    return;
                }

                if (plannedType.Value == TradeType.Sell && !allowShort)
                {
                    EmitAndReturn("HL_BLOCK_SHORT", false);
                    return;
                }
            }
            // ============================================================
            // [END] HLゲート挿入位置
            // ============================================================

            EmitAndReturn(reasonTag, true);

            ExecuteEntryWithPlannedType(plannedType.Value, reasonTag, close1.Value, ema1.Value, i1);
        }


        private bool EffectiveEnableStopLoss()
        {
            return UseStopLoss;
        }

        private bool EffectiveEnableTakeProfit()
        {
            return EnableTakeProfit;
        }


        private bool PassesSpreadFilter(double bid, double ask)
        {
            double maxInputPips = Math.Max(0.0, MaxSpreadPips);
            if (maxInputPips <= 0.0)
                return true;

            // spreadPips is internal(pips) in cTrader terms. Convert input(UI) pips -> internal pips (METAL only x10)
            double maxInternalPips = InputPipsToInternalPips(maxInputPips);
            double spreadInternalPips = (ask - bid) / Symbol.PipSize;

            return spreadInternalPips <= maxInternalPips;
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
        // 019_002 ADD: 構造TP（スイング） + フォールバック（ATR）
        // ============================================================

        private double ApplyMinTpDistanceBoostOnly(TradeType type, double entry, double tpAbs)
        {
            double minInput = Math.Max(0.0, MinTpDistancePips);
            if (minInput <= 0.0)
                return tpAbs;

            double minInternalPips = InputPipsToInternalPips(minInput);
            double minPrice = minInternalPips * Symbol.PipSize;

            // If the computed TP is too close to entry, push it outward.
            if (type == TradeType.Buy)
            {
                if ((tpAbs - entry) < minPrice)
                    tpAbs = entry + minPrice;
            }
            else
            {
                if ((entry - tpAbs) < minPrice)
                    tpAbs = entry - minPrice;
            }

            return tpAbs;
        }

        bool TryGetStructureTakeProfit(TradeType type, double entry, out double tpAbs)
        {
            tpAbs = 0.0;

            int lr = Math.Max(1, TpSwingLR);
            int lookback = Math.Max(10, TpSwingLookback);

            if (_barsTpStructure == null || _barsTpStructure.Count < lookback + (lr * 2) + 5)
                return false;

            double bufferInput = Math.Max(0.0, StructureTpBufferPips);
            double bufferInternalPips = InputPipsToInternalPips(bufferInput);
            double bufferPrice = bufferInternalPips * Symbol.PipSize;

            if (type == TradeType.Buy)
            {
                if (!TryFindLastSwingHighOnBars(_barsTpStructure, lr, lookback, out double swingHigh))
                    return false;

                // Buy: 高値の少し手前に置く（到達率重視）
                tpAbs = swingHigh - bufferPrice;
                if (tpAbs <= entry)
                    return false;

                return true;
            }
            else
            {
                if (!TryFindLastSwingLowOnBars(_barsTpStructure, lr, lookback, out double swingLow))
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


        private double GetMinSlFloorPrice()
        {
            double pips = Math.Max(0.0, MinSLPips);
            return UserPipsToPrice(pips);
        }

        private double GetMaxSlCapPrice()
        {
            double pips = Math.Max(0.0, MaxSlPipsInput);
            if (pips <= 0.0) return 0.0; // 0なら上限なし
            return UserPipsToPrice(pips);
        }

        // ============================================================
        // TP関連 / リスクリワード / 注文実行
        // ============================================================

        private bool IsSlTpDistanceRelatedFailure(TradeResult openRes)
        {
            if (openRes == null || openRes.IsSuccessful)
                return false;

            string errText = "";
            try
            {
                errText = openRes.Error.ToString();
            }
            catch
            {
                errText = "";
            }

            if (string.IsNullOrWhiteSpace(errText))
                return false;

            string up = errText.ToUpperInvariant();
            return up.Contains("STOP") || up.Contains("TAKE") || up.Contains("SL") || up.Contains("TP") || up.Contains("DIST");
        }

        private void PlaceTrade(TradeType type, double entry, double stop, double tpTargetPrice, string reasonTag, int decisionBarIndex)
        {
            // Compliance gate：新規注文（保留注文含む発行直前）で必ず参照
            if (!TradePermission)
            {
                EmitExecuteSkipJsonl("TIME_BLOCK", type, decisionBarIndex, null, null, null, null);
                return;
            }



            // MaxPositions / 多重発注防止（必須）
            if (ShouldBlockNewEntry(type, decisionBarIndex, reasonTag))
                return;

            // ATR Environment gate (entry permission only; independent from SPL/TP ATR settings)
            if (EnableAtrEnvGate)
            {
                double atr = (_atrEnvGate != null && _atrEnvGate.Result != null && _atrEnvGate.Result.Count > 0)
                        ? _atrEnvGate.Result.LastValue
                        : 0.0;

                double atrInternalPips = (Symbol != null && Symbol.PipSize > 0) ? atr / Symbol.PipSize : 0.0;
                double atrInputPips = InternalPipsToInputPips(atrInternalPips);

                // ATR_ENV_EVAL (JSONL) : only when PRO report / OHLC output is enabled
                if (EnableProReport)
                    EmitAtrEnvEvalJsonl(type, decisionBarIndex, atr, atrInternalPips, atrInputPips);

                if (AtrEnvMinPips > 0.0 && atrInputPips > 0.0 && atrInputPips < AtrEnvMinPips)
                {
                    if (EnableProReport)
                        Print("ATR_ENV_GATE_BLOCK | ATR(pips)={0:F2} < Min={1:F2}{2}", atrInputPips, AtrEnvMinPips, BuildTimeTag(UtcNow()));
                    EmitExecuteSkipJsonl("ATR_ENV_BLOCK_MIN", type, decisionBarIndex, null, null, null, null);
                    return;
                }

                if (AtrEnvMaxPips > 0.0 && atrInputPips > 0.0 && atrInputPips > AtrEnvMaxPips)
                {
                    if (EnableProReport)
                        Print("ATR_ENV_GATE_BLOCK | ATR(pips)={0:F2} > Max={1:F2}{2}", atrInputPips, AtrEnvMaxPips, BuildTimeTag(UtcNow()));
                    EmitExecuteSkipJsonl("ATR_ENV_BLOCK_MAX", type, decisionBarIndex, null, null, null, null);
                    return;
                }
            }

            // NEWS MODULE gate (safety): do not place orders inside news window / safe mode
            DateTime utcNow = UtcNow();
            News_InitOrRefresh(utcNow);
            if (!IsNewEntryAllowed(utcNow, out string newsReason))
            {
                if (EnableProReport)
                    Print("NEWS_BLOCK | CodeName={0} | Symbol={1} | Reason={2}{3}", CODE_NAME, SymbolName, newsReason, BuildTimeTag(utcNow));
                EmitExecuteSkipJsonl("NEWS_BLOCK", type, decisionBarIndex, null, null, null, null);
                return;
            }

            if (_stopRequestedByRiskFailure)
            {
                EmitExecuteSkipJsonl("RISK_BLOCK", type, decisionBarIndex, null, null, null, null);
                return;
            }

            double riskAmount = GetRiskAmountInAccountCurrency(out RiskMode mode);
            if (riskAmount <= 0.0)
            {
                EmitExecuteSkipJsonl("RISK_BLOCK", type, decisionBarIndex, null, null, null, null);
                return;
            }

            bool useSl = EffectiveEnableStopLoss();
            bool useTp = EffectiveEnableTakeProfit();

            if (!useSl)
            {
                EmitExecuteSkipJsonl("SL_DISABLED_BLOCK", type, decisionBarIndex, null, null, null, null);
                return;
            }

            if (!useTp)
            {
                EmitExecuteSkipJsonl("TP_DISABLED_BLOCK", type, decisionBarIndex, null, null, null, null);
                return;
            }

            // 030 policy: SL/TP must be attached at ENTRY for all modes (no post-attach).

            double intendedSlDistancePrice = Math.Abs(entry - stop);
            if (intendedSlDistancePrice <= 0.0)
            {
                EmitExecuteSkipJsonl("SLTP_INVALID", type, decisionBarIndex, null, null, null, null);
                return;
            }

            double intendedSlPips = intendedSlDistancePrice / Symbol.PipSize;
            if (intendedSlPips <= 0.0)
            {
                EmitExecuteSkipJsonl("SLTP_INVALID", type, decisionBarIndex, null, null, null, null);
                return;
            }

            double tpPipsFromTarget = 0.0;
            if (useTp)
            {
                tpPipsFromTarget = Math.Abs(tpTargetPrice - entry) / Symbol.PipSize;
                if (tpPipsFromTarget <= 0.0)
                {
                    EmitExecuteSkipJsonl("SLTP_INVALID", type, decisionBarIndex, null, null, null, null);
                    return;
                }
            }

            double bufferPips = Math.Max(0.0, RiskBufferPips);

            double slipInputPips = Math.Max(0.0, SlipAllowancePips);
            double slipInternalPips = InputPipsToInternalPips(slipInputPips);

            // Attempt to ensure SL is accepted by the broker: if SL cannot be set, close immediately and retry with wider SL.
            const int maxAttempts = 5;
            const double stepPips = 10.0;


            int i1 = (Bars != null && Bars.Count > 0) ? (Bars.Count - 1) : 0;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                double effectiveSlPips = intendedSlPips + (attempt * stepPips);
                if (effectiveSlPips <= 0.0)
                    continue;

                bool includeSlipInSizing = IncludeSlipAllowanceInSizing;
                double sizingPips = effectiveSlPips + bufferPips + (includeSlipInSizing ? slipInternalPips : 0.0);
                if (sizingPips <= 0.0)
                    continue;

                double volumeUnitsRaw = riskAmount / (sizingPips * Symbol.PipValue);
                long volumeInUnits = (long)Symbol.NormalizeVolumeInUnits(volumeUnitsRaw, RoundingMode.Down);
                if (volumeInUnits < Symbol.VolumeInUnitsMin)
                {
                    EmitExecuteSkipJsonl("VOLUME_MIN_BLOCK", type, decisionBarIndex, null, null, null, null);
                    return;
                }

                volumeInUnits = ClampVolumeByMaxLots(volumeInUnits);

                // 030 policy: All entries must attach BOTH SL and TP at order placement (no post-attach).
                TradeResult openRes;

                double slPipsToSend = effectiveSlPips;
                double tpPipsToSend = tpPipsFromTarget;

                // Guard: both SL/TP must be meaningful (>0) to comply with fixed-SL + entry-time SLTP policy.
                if (slPipsToSend <= 0.0 || tpPipsToSend <= 0.0)
                {
                    EmitExecuteSkipJsonl("SLTP_INVALID", type, decisionBarIndex, null, null, null, null);
                    return;
                }

                _entryInProgress = true;
                TradeResult openResLocal = null;
                try
                {
                    volumeInUnits = ClampVolumeByMaxLots(volumeInUnits);

                    // 最終直前ガード（反映遅延対策）
                    if (MaxPositions >= 1)
                    {
                        int posCountNow = 0;
                        try { var posArrNow = Positions.FindAll(BOT_LABEL, SymbolName); posCountNow = (posArrNow != null) ? posArrNow.Length : 0; } catch { posCountNow = 0; }
                        if (posCountNow >= MaxPositions)
                        {
                            EmitExecuteSkipJsonl("MAX_POSITIONS_FINAL", type, decisionBarIndex, null, null, null, null);
                            return;
                        }
                    }

                    MarkEntryAttempt(decisionBarIndex, utcNow);
                    EmitExecuteSendJsonl(type, decisionBarIndex, volumeInUnits, slPipsToSend, tpPipsToSend);
                    openResLocal = ExecuteMarketOrder(type, SymbolName, volumeInUnits, BOT_LABEL, slPipsToSend, tpPipsToSend);
                }
                catch (Exception)
                {
                    EmitExecuteSkipJsonl("EXECUTE_EXCEPTION", type, decisionBarIndex, null, null, null, null);
                }
                finally
                {
                    _entryInProgress = false;
                }

                openRes = openResLocal;
                EmitExecuteResultJsonl(type, decisionBarIndex, openRes);

                if (openRes == null || !openRes.IsSuccessful || openRes.Position == null)
                {
                    if (IsSlTpDistanceRelatedFailure(openRes))
                        continue;

                    return;
                }


                Position pos = openRes.Position;

                // Safety: if broker created a position without BOTH SL and TP, close immediately (no re-attach).
                if (!pos.StopLoss.HasValue || !pos.TakeProfit.HasValue)
                {
                    _missingProtectionCloseRequested.Add(pos.Id);
                    _closeInitiatorByPosId[pos.Id] = "MISSING_PROTECTION_POSTENTRY";
                    ClosePosition(pos);
                    return;
                }
                _emergencyCloseRequested.Remove(pos.Id);

                // 030 policy: Do NOT modify SL/TP after entry. SL/TP must be attached at entry-time.
                // (TP may be managed later by logic, but SL must remain fixed.)
                // 評価基盤ログ：建玉確定（SL/TPをAbsolute補正した最終値を記録）
                PrintEntryCore(pos);


                bool slOk = !useSl || (pos.StopLoss.HasValue && pos.StopLoss.Value > 0.0);
                if (!slOk)
                {
                    // If SL is still not set after modify, close immediately to prevent uncontrolled risk.
                    _closeInitiatorByPosId[pos.Id] = "SL_ATTACH_FAILED_CLOSE";
                    ClosePosition(pos);

                    if (EnableProReport)
                    {
                        Print(
                            "SL_ATTACH_FAILED | CodeName={0} | Symbol={1} | Attempt={2}/{3} | PosId={4} | IntendedSLpips={5} EffSLpips={6} | VolUnits={7} | ModOk={8} | Action=CLOSE_IMMEDIATELY | Reason={9}{10}",
                            CODE_NAME,
                            SymbolName,
                            attempt + 1,
                            maxAttempts,
                            pos.Id,
                            intendedSlPips.ToString("F1", CultureInfo.InvariantCulture),
                            effectiveSlPips.ToString("F1", CultureInfo.InvariantCulture),
                            volumeInUnits,
                            false,
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
                    pos.EntryPrice.ToString("F5", CultureInfo.InvariantCulture),
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
                    "ENTRY_MODE | CodeName={0} | Symbol={1} | EntryMode={2} | EntryType={3}{4}",
                    CODE_NAME,
                    SymbolName,
                    EntryMode,
                    (reasonTag == "EMA_CROSS" ? "CROSS" : (reasonTag == "EMA_REGIME" ? "REGIME" : "OTHER")),
                    BuildTimeTag(UtcNow())
                );

                Print(
                    "SLTP_MODE | CodeName={0} | Symbol={1} | Exit=Param{2}",
                    CODE_NAME,
                    SymbolName,
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

        private sealed class NewsEvent
        {
            public DateTime UtcTime { get; set; }
            public string Currency { get; set; }
            public int Importance { get; set; }
            public string Title { get; set; }
        }

        private List<DateTime> _newsEventsUtc = new List<DateTime>();
        private List<NewsEvent> _newsForwardEvents = new List<NewsEvent>();
        private DateTime _newsLoadedUtcDate = DateTime.MinValue;
        private bool _newsBacktestLoaded = false;
        private Dictionary<string, TimeZoneInfo> _tzCache = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);

        private void News_InitOrRefresh(DateTime utcNow, bool force = false)
        {
            // mode conflict
            if (UseNewsBacktest2025 && UseNewsForward)
            {
Stop();
                return;
            }

            // disabled
            if (!UseNewsBacktest2025 && !UseNewsForward)
            {
                _newsEventsUtc.Clear();
                _newsForwardEvents.Clear();
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

                _newsForwardEvents.Clear();
                return;
            }

            // Forward mode: refresh on UTC date change (or force)
            DateTime todayUtc = utcNow.Date;
            if (!force && _newsLoadedUtcDate.Date == todayUtc)
                return;

            if (TryLoadCalendarEventsForDate(todayUtc, out List<NewsEvent> eventsUtc, out string err))
            {
                _newsEventsUtc.Clear();
                _newsForwardEvents = eventsUtc ?? new List<NewsEvent>();
                _newsLoadedUtcDate = todayUtc;
                {
                // forward diagnostics (one-line)
                HashSet<string> targetCurrencies = GetTargetCurrenciesForSymbol(SymbolName);
                int minImpact = GetEffectiveMinImpactLevel();
                NewsEvent next = FindNextRelevantForwardEventUtc(utcNow);
                string ccy = (targetCurrencies == null || targetCurrencies.Count == 0) ? "NA" : string.Join(",", targetCurrencies);
                string nextEventUtc = (next == null) ? "NA" : next.UtcTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                string nextUnblockUtc = (next == null) ? "NA" : next.UtcTime.AddMinutes(Math.Max(0, MinutesAfterNews)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

                Print("NEWS_SOURCE | CodeName={0} | Symbol={1} | Mode=FORWARD_API | UtcDate={2:yyyy-MM-dd} | Events={3} | MinImpact={4} | TargetCcy={5} | NextEventUtc={6} | NextUnblockUtc={7}{8}",
                    CODE_NAME, SymbolName, todayUtc, _newsForwardEvents.Count, minImpact, ccy, nextEventUtc, nextUnblockUtc, BuildTimeTag(utcNow));
            }
            }
            else
            {
                _newsEventsUtc.Clear();
                _newsForwardEvents = BuildFallbackTemplateEventsUtc(todayUtc);
                _newsLoadedUtcDate = todayUtc;
                {
                // forward diagnostics (fallback)
                HashSet<string> targetCurrencies = GetTargetCurrenciesForSymbol(SymbolName);
                int minImpact = GetEffectiveMinImpactLevel();
                NewsEvent next = FindNextRelevantForwardEventUtc(utcNow);
                string ccy = (targetCurrencies == null || targetCurrencies.Count == 0) ? "NA" : string.Join(",", targetCurrencies);
                string nextEventUtc = (next == null) ? "NA" : next.UtcTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                string nextUnblockUtc = (next == null) ? "NA" : next.UtcTime.AddMinutes(Math.Max(0, MinutesAfterNews)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                string reason = string.IsNullOrWhiteSpace(err) ? "NA" : err;

                Print("NEWS_FALLBACK | CodeName={0} | Symbol={1} | Mode=FORWARD_API | UtcDate={2:yyyy-MM-dd} | Reason={3} | Events={4} | MinImpact={5} | TargetCcy={6} | NextEventUtc={7} | NextUnblockUtc={8}{9}",
                    CODE_NAME, SymbolName, todayUtc, reason, _newsForwardEvents.Count, minImpact, ccy, nextEventUtc, nextUnblockUtc, BuildTimeTag(utcNow));
            }
            }
        }

        // 外部公開：新規エントリー可否（ニュースのみ）
        private bool IsNewEntryAllowed(DateTime utcNow, out string blockReason)
        {
            blockReason = "OK";

            // disabled
            if (!UseNewsBacktest2025 && !UseNewsForward)
                return true;

            // conflict handled by init (Stop), but keep guard
            if (UseNewsBacktest2025 && UseNewsForward)
            {
                blockReason = "MODE_CONFLICT";
                return false;
            }

            int before = Math.Max(0, MinutesBeforeNews);
            int after = Math.Max(0, MinutesAfterNews);

            if (UseNewsBacktest2025)
            {
                if (_newsEventsUtc == null || _newsEventsUtc.Count == 0)
                    return true;

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

            if (_newsForwardEvents == null || _newsForwardEvents.Count == 0)
                return true;

            HashSet<string> targetCurrencies = GetTargetCurrenciesForSymbol(SymbolName);
            int minImpact = GetEffectiveMinImpactLevel();

            for (int i = 0; i < _newsForwardEvents.Count; i++)
            {
                NewsEvent ev = _newsForwardEvents[i];
                if (!IsForwardNewsEventMatch(ev, targetCurrencies, minImpact))
                    continue;

                DateTime e = ev.UtcTime;
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

        private int GetEffectiveMinImpactLevel()
        {
            return (int)MinImpactLevel;
        }

        
        private NewsEvent FindNextRelevantForwardEventUtc(DateTime utcNow)
        {
            if (_newsForwardEvents == null || _newsForwardEvents.Count == 0)
                return null;

            HashSet<string> targetCurrencies = GetTargetCurrenciesForSymbol(SymbolName);
            int minImpact = GetEffectiveMinImpactLevel();

            NewsEvent best = null;
            for (int i = 0; i < _newsForwardEvents.Count; i++)
            {
                NewsEvent ev = _newsForwardEvents[i];
                if (!IsForwardNewsEventMatch(ev, targetCurrencies, minImpact))
                    continue;

                if (ev.UtcTime < utcNow)
                    continue;

                if (best == null || ev.UtcTime < best.UtcTime)
                    best = ev;
            }
            return best;
        }

private bool IsForwardNewsEventMatch(NewsEvent ev, HashSet<string> targetCurrencies, int minImpact)
        {
            if (ev == null)
                return false;
            if (ev.UtcTime == DateTime.MinValue)
                return false;
            if (ev.Importance < minImpact)
                return false;

            string ccy = string.IsNullOrWhiteSpace(ev.Currency) ? "" : ev.Currency.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(ccy))
                return false;

            if (targetCurrencies == null || targetCurrencies.Count == 0)
                return false;

            return targetCurrencies.Contains(ccy);
        }

        private HashSet<string> GetTargetCurrenciesForSymbol(string symbolName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string symNorm = NormalizeSymbolName(symbolName);
            if (string.Equals(symNorm, "XAUUSD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(symNorm, "XAGUSD", StringComparison.OrdinalIgnoreCase))
            {
                result.Add("USD");
                return result;
            }

            if (!string.IsNullOrWhiteSpace(symNorm) && symNorm.Length == 6)
            {
                bool allLetters = true;
                for (int i = 0; i < symNorm.Length; i++)
                {
                    char c = symNorm[i];
                    if (c < 'A' || c > 'Z')
                    {
                        allLetters = false;
                        break;
                    }
                }

                if (allLetters)
                {
                    result.Add(symNorm.Substring(0, 3));
                    result.Add(symNorm.Substring(3, 3));
                    return result;
                }
            }

            result.Add("USD");
            return result;
        }

        private List<NewsEvent> BuildFallbackTemplateEventsUtc(DateTime utcDate)
        {
            List<NewsEvent> list = new List<NewsEvent>();
            HashSet<string> targets = GetTargetCurrenciesForSymbol(SymbolName);

            if (targets.Contains("USD"))
            {
                AddFallbackTemplateEvent(list, utcDate, "USD", 8, 30, "TEMPLATE_USD_0830");
                AddFallbackTemplateEvent(list, utcDate, "USD", 10, 0, "TEMPLATE_USD_1000");
                AddFallbackTemplateEvent(list, utcDate, "USD", 14, 0, "TEMPLATE_USD_1400");
                AddFallbackTemplateEvent(list, utcDate, "USD", 14, 30, "TEMPLATE_USD_1430");
            }
            if (targets.Contains("JPY"))
                AddFallbackTemplateEvent(list, utcDate, "JPY", 8, 50, "TEMPLATE_JPY_0850");
            if (targets.Contains("EUR"))
                AddFallbackTemplateEvent(list, utcDate, "EUR", 11, 0, "TEMPLATE_EUR_1100");
            if (targets.Contains("GBP"))
                AddFallbackTemplateEvent(list, utcDate, "GBP", 7, 0, "TEMPLATE_GBP_0700");

            list.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
            return list;
        }

        private void AddFallbackTemplateEvent(List<NewsEvent> list, DateTime utcDate, string currency, int hour, int minute, string title)
        {
            if (list == null)
                return;

            TimeZoneInfo tz = ResolveTimeZoneForCurrency(currency);
            if (tz == null)
                return;

            try
            {
                DateTime local = new DateTime(utcDate.Year, utcDate.Month, utcDate.Day, hour, minute, 0, DateTimeKind.Unspecified);
                DateTime utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
                list.Add(new NewsEvent
                {
                    UtcTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc),
                    Currency = currency,
                    Importance = 3,
                    Title = title
                });
            }
            catch
            {
                // skip this template slot if conversion fails
            }
        }

        private TimeZoneInfo ResolveTimeZoneForCurrency(string ccy)
        {
            if (string.IsNullOrWhiteSpace(ccy))
                return null;

            string key = ccy.Trim().ToUpperInvariant();

            if (_tzCache != null && _tzCache.TryGetValue(key, out TimeZoneInfo cached))
                return cached;

            string windowsId = null;
            string ianaId = null;

            switch (key)
            {
                case "USD":
                    windowsId = "Eastern Standard Time";
                    ianaId = "America/New_York";
                    break;
                case "GBP":
                    windowsId = "GMT Standard Time";
                    ianaId = "Europe/London";
                    break;
                case "EUR":
                    windowsId = "W. Europe Standard Time";
                    ianaId = "Europe/Berlin";
                    break;
                case "JPY":
                    windowsId = "Tokyo Standard Time";
                    ianaId = "Asia/Tokyo";
                    break;
                default:
                    return null;
            }

            TimeZoneInfo tz = TryFindTimeZoneById(windowsId);
            if (tz == null)
                tz = TryFindTimeZoneById(ianaId);

            if (_tzCache == null)
                _tzCache = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);
            _tzCache[key] = tz;

            return tz;
        }

        private TimeZoneInfo TryFindTimeZoneById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch
            {
                return null;
            }
        }

        private bool TryLoadCalendarEventsForDate(DateTime utcDate, out List<NewsEvent> events, out string error)
        {
            events = new List<NewsEvent>();
            error = "";

            if (string.IsNullOrWhiteSpace(CalendarApiKey))
            {
                error = "APIKEY_EMPTY";
                return false;
            }

            // RapidAPI Economic Calendar API
            // Host: economic-calendar-api.p.rapidapi.com
            // Path: /calendar/history/next-month
            // Key: Header (X-RapidAPI-Key), NOT query string
            const string host = "economic-calendar-api.p.rapidapi.com";

            try
            {
                // NOTE: next-month を取得しつつ、旧仕様（指定日utcDate）に合わせて当日分だけ抽出する
                string url =
                    "https://" + host + "/calendar/history/next-month" +
                    "?countryCode=" + Uri.EscapeDataString("US") +
                    "&volatility=" + Uri.EscapeDataString("HIGH"); // まずは検証固定（後でEA設定に接続）

                using (var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler()))
                {
                    client.Timeout = TimeSpan.FromSeconds(12);

                    // headers
                    client.DefaultRequestHeaders.TryAddWithoutValidation("X-RapidAPI-Key", CalendarApiKey);
                    client.DefaultRequestHeaders.TryAddWithoutValidation("X-RapidAPI-Host", host);

                    using (var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, new Uri(url)))
                    using (var res = client.Send(req))
                    {
                        int status = (int)res.StatusCode;

                        // body
                        string body = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                        if (status != 200)
                        {
                            error = "HTTP_" + status.ToString(CultureInfo.InvariantCulture);
                            return false;
                        }
                        if (string.IsNullOrWhiteSpace(body))
                        {
                            error = "EMPTY_RESPONSE";
                            return false;
                        }

                        // parse json
                        try
                        {
                            using (var doc = System.Text.Json.JsonDocument.Parse(body))
                            {
                                var root = doc.RootElement;

                                // success / totalEvents は存在しない場合もあるので必須扱いにしない
                                // events 配列だけ読めればOK
                                System.Text.Json.JsonElement eventsEl;

                                if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("events", out eventsEl) && eventsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    DateTime day = utcDate.Date; // UTC
                                    for (int i = 0; i < eventsEl.GetArrayLength(); i++)
                                    {
                                        var ev = eventsEl[i];
                                        if (ev.ValueKind != System.Text.Json.JsonValueKind.Object)
                                            continue;

                                        // dateUtc
                                        if (!ev.TryGetProperty("dateUtc", out var dateUtcEl) || dateUtcEl.ValueKind != System.Text.Json.JsonValueKind.String)
                                            continue;

                                        string dateUtcText = dateUtcEl.GetString() ?? "";
                                        if (string.IsNullOrWhiteSpace(dateUtcText))
                                            continue;

                                        if (!DateTime.TryParse(dateUtcText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime eventUtc))
                                            continue;

                                        // 指定日だけ抽出（UTC日付一致）
                                        if (eventUtc.Date != day)
                                            continue;

                                        // currencyCode
                                        string currency = "";
                                        if (ev.TryGetProperty("currencyCode", out var curEl) && curEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                            currency = (curEl.GetString() ?? "");

                                        // name
                                        string title = "";
                                        if (ev.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                            title = (nameEl.GetString() ?? "");

                                        // volatility -> importance (0/1/2)
                                        int importance = 0;
                                        string vol = "";
                                        if (ev.TryGetProperty("volatility", out var volEl) && volEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                            vol = (volEl.GetString() ?? "").Trim().ToUpperInvariant();

                                        if (vol == "HIGH") importance = 2;
                                        else if (vol == "MEDIUM") importance = 1;
                                        else importance = 0;

                                        // 旧仕様に合わせて Currency は必須。空ならスキップ。
                                        currency = (currency ?? "").Trim().ToUpperInvariant();
                                        if (string.IsNullOrWhiteSpace(currency))
                                            continue;

                                        // NONE/LOW は停止対象外（0）なので events に入れても無害だが、ここで除外しておく
                                        if (importance == 0)
                                            continue;

                                        events.Add(new NewsEvent
                                        {
                                            UtcTime = DateTime.SpecifyKind(eventUtc, DateTimeKind.Utc),
                                            Currency = currency,
                                            Importance = importance,
                                            Title = title ?? ""
                                        });
                                    }
                                }
                                else
                                {
                                    // API成功だが events が無い/空：正常扱い（停止なし）
                                    // 呼び出し側で NEWS_SOURCE を出す想定なので、ここは成功で返す
                                    events.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
                                    return true;
                                }
                            }
                        }
                        catch
                        {
                            error = "JSON_FAIL";
                            return false;
                        }

                        events.Sort((a, b) => a.UtcTime.CompareTo(b.UtcTime));
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name;
                return false;
            }
        }

        private List<string> ExtractTopLevelJsonObjects(string json)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
                return list;

            bool inString = false;
            bool escape = false;
            int depth = 0;
            int start = -1;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }
                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }
                    if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                        start = i;
                    depth++;
                    continue;
                }

                if (c == '}')
                {
                    if (depth <= 0)
                        continue;

                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        list.Add(json.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }

            return list;
        }

        private bool TryGetJsonStringField(string jsonObject, string fieldName, out string value)
        {
            value = null;
            if (!TryGetJsonRawField(jsonObject, fieldName, out string raw))
                return false;

            raw = raw == null ? "" : raw.Trim();
            if (raw.Length == 0)
                return false;

            if (raw[0] == '"')
                return TryParseJsonStringLiteral(raw, out value);

            value = raw;
            return true;
        }

        private bool TryGetJsonRawField(string jsonObject, string fieldName, out string raw)
        {
            raw = null;
            if (string.IsNullOrWhiteSpace(jsonObject) || string.IsNullOrWhiteSpace(fieldName))
                return false;

            string keyToken = "\"" + fieldName + "\"";
            int searchFrom = 0;

            while (searchFrom < jsonObject.Length)
            {
                int keyPos = jsonObject.IndexOf(keyToken, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (keyPos < 0)
                    return false;

                int colonPos = jsonObject.IndexOf(':', keyPos + keyToken.Length);
                if (colonPos < 0)
                    return false;

                int valueStart = colonPos + 1;
                while (valueStart < jsonObject.Length && char.IsWhiteSpace(jsonObject[valueStart]))
                    valueStart++;

                if (valueStart >= jsonObject.Length)
                    return false;

                if (jsonObject[valueStart] == '"')
                {
                    if (!TryFindJsonStringLiteralEnd(jsonObject, valueStart, out int endExclusive))
                        return false;

                    raw = jsonObject.Substring(valueStart, endExclusive - valueStart);
                    return true;
                }

                int valueEnd = valueStart;
                while (valueEnd < jsonObject.Length)
                {
                    char c = jsonObject[valueEnd];
                    if (c == ',' || c == '}' || c == ']')
                        break;
                    valueEnd++;
                }

                raw = jsonObject.Substring(valueStart, valueEnd - valueStart).Trim();
                return !string.IsNullOrWhiteSpace(raw);
            }

            return false;
        }

        private bool TryFindJsonStringLiteralEnd(string s, int startQuoteIndex, out int endExclusive)
        {
            endExclusive = -1;
            if (string.IsNullOrEmpty(s) || startQuoteIndex < 0 || startQuoteIndex >= s.Length || s[startQuoteIndex] != '"')
                return false;

            bool escape = false;
            for (int i = startQuoteIndex + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    endExclusive = i + 1;
                    return true;
                }
            }

            return false;
        }

        private bool TryParseJsonStringLiteral(string literal, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(literal) || literal.Length < 2 || literal[0] != '"' || literal[literal.Length - 1] != '"')
                return false;

            StringBuilder sb = new StringBuilder(literal.Length);
            for (int i = 1; i < literal.Length - 1; i++)
            {
                char c = literal[i];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i + 1 >= literal.Length - 1)
                    return false;

                char n = literal[++i];
                switch (n)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 >= literal.Length)
                            return false;
                        int h1 = HexToInt(literal[i + 1]);
                        int h2 = HexToInt(literal[i + 2]);
                        int h3 = HexToInt(literal[i + 3]);
                        int h4 = HexToInt(literal[i + 4]);
                        if (h1 < 0 || h2 < 0 || h3 < 0 || h4 < 0)
                            return false;
                        int code = (h1 << 12) | (h2 << 8) | (h3 << 4) | h4;
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default:
                        sb.Append(n);
                        break;
                }
            }

            value = sb.ToString();
            return true;
        }

        private int HexToInt(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
            if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
            return -1;
        }

        private bool TryParseCalendarImportance(string rawValue, out int importance)
        {
            importance = 0;
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            string s = rawValue.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                string decoded;
                if (!TryParseJsonStringLiteral(s, out decoded))
                    return false;
                s = decoded == null ? "" : decoded.Trim();
            }

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int imp))
            {
                if (imp < 1) imp = 1;
                if (imp > 3) imp = 3;
                importance = imp;
                return true;
            }

            if (s.IndexOf("HIGH", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importance = 3;
                return true;
            }
            if (s.IndexOf("MED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importance = 2;
                return true;
            }
            if (s.IndexOf("LOW", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importance = 1;
                return true;
            }

            return false;
        }

        private bool TryParseCalendarApiUtcDateTime(string s, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            string t = s.Trim();

            if (t.StartsWith("/Date(", StringComparison.OrdinalIgnoreCase))
            {
                int p1 = t.IndexOf('(');
                int p2 = t.IndexOf(')', p1 + 1);
                if (p1 >= 0 && p2 > p1)
                {
                    string inner = t.Substring(p1 + 1, p2 - p1 - 1);
                    int k = 0;
                    if (k < inner.Length && (inner[k] == '+' || inner[k] == '-'))
                        k++;
                    while (k < inner.Length && char.IsDigit(inner[k]))
                        k++;
                    string msText = inner.Substring(0, k);

                    if (long.TryParse(msText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
                    {
                        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        utc = epoch.AddMilliseconds(ms);
                        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
                        return true;
                    }
                }
            }

            if (!DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dt))
                return false;

            utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return true;
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


        private TimeFrame ResolveTpStructureTimeFrame()
        {
            int m = TpStructureTimeframeMinutes;

            // 許可値以外は60分へフォールバック（暴走防止）
            if (m == 15)
                return TimeFrame.Minute15;
            if (m == 30)
                return TimeFrame.Minute30;
            if (m == 60)
                return TimeFrame.Hour;
            if (m == 240)
                return TimeFrame.Hour4;
            if (m == 1440)
                return TimeFrame.Daily;

            return TimeFrame.Hour;
        }

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
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
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
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
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

        // ============================================================
        // 共通ユーティリティ
        // ============================================================

        private DateTime UtcNow()
        {
            return DateTime.SpecifyKind(Server.Time, DateTimeKind.Utc);
        }


        private double NormalizePrice(double price)
        {
            if (Symbol == null || Symbol.TickSize <= 0)
                return price;

            // Snap to tick size
            return Math.Round(price / Symbol.TickSize) * Symbol.TickSize;
        }

        private string BuildTimeTag(DateTime utc)
        {
            if (!EnableProReport)
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

        // ユーザー入力PIPS → internalPips → price への統一変換
        private double UserPipsToPrice(double userPips)
        {
            // userPips は UI 入力値（例：30, 80, 200）
            // internalPips は Metal(XAUUSD) では ×10
            double internalPips = InputPipsToInternalPips(Math.Max(0.0, userPips));
            return internalPips * Symbol.PipSize;
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

        private double InputPipsToPrice(double inputPips)
        {
            return InputPipsToInternalPips(inputPips) * Symbol.PipSize;
        }


        // internalPips（Position.Pips等）→ UI入力基準PIPS（あなた基準）
        private double InternalPipsToInputPips(double internalPips)
        {
            double scale = Math.Max(1.0, (double)_pipsScale);
            return internalPips / scale;
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
            DateTime u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

            // まず TimeZoneInfo を優先。取得できない環境では UTC+9 にフォールバック。
            if (_jstTz != null)
            {
                try { return TimeZoneInfo.ConvertTimeFromUtc(u, _jstTz); } catch { }
            }

            // fallback: JST = UTC+9
            return u.AddHours(9);
        }

        private static long ToUnixTimeMillisecondsUtc(DateTime utc)
        {
            DateTime u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            long ms = (long)(u - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            return ms;
        }

        private string BuildEmaSnapshotJsonl(DateTime snapTimeUtc, int i1, int i2)
        {
            // NOTE:
            // - OHLCからEMAを再計算してはならない（内部で参照しているEMAインスタンスの値そのものを出す）
            // - i1/i2 は確定足のみ（forming bar 参照禁止）
            // - 数値は InvariantCulture / 丸め禁止（doubleをそのまま）

            long timeUtcMs = ToUnixTimeMillisecondsUtc(snapTimeUtc);

            // bar_time_utc_ms は i1（直近確定足）のバー開始時刻
            DateTime barTimeUtc = Bars.OpenTimes[i1];
            long barTimeUtcMs = ToUnixTimeMillisecondsUtc(barTimeUtc);

            double close1 = Bars.ClosePrices[i1];
            double close2 = Bars.ClosePrices[i2];

            // EMAは「本体が内部で参照しているEMA値」を優先して出す（新規EMA生成禁止）
            double? ema1 = (_emaFramework != null && _emaFramework.Result != null && _emaFramework.Result.Count > i1)
                ? (double?)_emaFramework.Result[i1]
                : ((_ema != null && _ema.Result != null && _ema.Result.Count > i1) ? (double?)_ema.Result[i1] : null);

            double? ema2 = (_emaFramework != null && _emaFramework.Result != null && _emaFramework.Result.Count > i2)
                ? (double?)_emaFramework.Result[i2]
                : ((_ema != null && _ema.Result != null && _ema.Result.Count > i2) ? (double?)_ema.Result[i2] : null);

            string symRaw = SymbolName ?? string.Empty;
            string symNorm = NormalizeSymbolName(symRaw);

            static string Esc(string s) => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            static string D(double? v) => v.HasValue ? v.Value.ToString("R", CultureInfo.InvariantCulture) : "null";

            // 固定9キー（欠落ゼロが前提。nullになる場合でもキーは出す）
            string json =
                "{"
                + "\"event\":\"EMA_SNAPSHOT\","
                + "\"time_utc_ms\":" + timeUtcMs.ToString(CultureInfo.InvariantCulture) + ","
                + "\"bar_time_utc_ms\":" + barTimeUtcMs.ToString(CultureInfo.InvariantCulture) + ","
                + "\"close_1\":" + close1.ToString("R", CultureInfo.InvariantCulture) + ","
                + "\"close_2\":" + close2.ToString("R", CultureInfo.InvariantCulture) + ","
                + "\"ema_1\":" + D(ema1) + ","
                + "\"ema_2\":" + D(ema2) + ","
                + "\"symbol_raw\":\"" + Esc(symRaw) + "\","
                + "\"symbol_norm\":\"" + Esc(symNorm) + "\""
                + "}";

            return json;
        }


        // ============================
        // Instance / Mode Guards
        // ============================
        private void ValidateNewsModeOrStop()
        {
            if (UseNewsBacktest2025 && UseNewsForward)
            {
                Print("NEWS_MODE_ERROR | CodeName={0} | Both UseNewsBacktest2025 and UseNewsForward are enabled. Stopping cBot to avoid mixed mode.",
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
                "INSTANCE | CodeName={0} | Label={1} | Symbol={2} | TF={3} | AccountNo={4} | Mode={5} | NewsBacktest2025={6} NewsForward={7} | Window(JST)={8}{9}",
                CODE_NAME,
                BOT_LABEL,
                SymbolName,
                TimeFrame,
                SafeAccountNumber(),
                envMode,
                UseNewsBacktest2025 ? "ON" : "OFF",
                UseNewsForward ? "ON" : "OFF",
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
                var props = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(x => x.MetadataToken).ToArray();
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
                    string valueText = val == null ? null : val.ToString();
                    string maskedValueText = MaskSensitiveParameterValue(p.Name, valueText);
                    item["value"] = maskedValueText;

                    list.Add(item);

                    // For PRO report (readable table)
                    _paramSnapshotEntries.Add(new ParamSnapshotEntry
                    {
                        PropertyName = p.Name,
                        DisplayName = a.Name,
                        GroupName = a.Group,
                        TypeName = p.PropertyType.Name,
                        ValueText = string.IsNullOrEmpty(maskedValueText) ? "" : maskedValueText
                    });
                }

                root["params"] = list;

                string json = SimpleJson(root);

                // cache for PRO report (HTML/embedded JSON)
                _paramSnapshotJson = json;

                // IMPORTANT: keep the same order as cTrader parameter UI (declaration/metadata order).
                // Do NOT sort by group/name here.
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
            if (UseNewsBacktest2025 && !UseNewsForward) return "BACKTEST_PARAM";
            if (!UseNewsBacktest2025 && UseNewsForward) return "FORWARD_PARAM";
            if (!UseNewsBacktest2025 && !UseNewsForward) return "MODELESS";
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


            // 030 safety: positions must always have BOTH SL and TP. If missing, close immediately.
            if (!p.StopLoss.HasValue || !p.TakeProfit.HasValue)
            {
                if (!_missingProtectionCloseRequested.Contains(p.Id))
                {
                    _missingProtectionCloseRequested.Add(p.Id);
                    _closeInitiatorByPosId[p.Id] = "MISSING_PROTECTION_MANAGE";
                    ClosePosition(p);
                }
                return false;
            }

            // 030 policy: SL is immutable after entry. Ignore/override any SL change requests.
            stopLossPrice = p.StopLoss;

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
                    if (pp != null && pp.Id == posId && pp.Label == BOT_LABEL && IsSameSymbolNormalized(pp.SymbolName, SymbolName))
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
                if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) continue;
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
            // Windows / Linux どちらでも動くように候補を試す
            string[] candidateIds = new[] { "Tokyo Standard Time", "Asia/Tokyo" };
            foreach (string id in candidateIds)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            }

            // 見つからない場合は null（ToJst 内で UTC+9 フォールバック）
            return null;
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
            if (!IsSameSymbolNormalized(p.SymbolName, SymbolName)) return;
            if (p.Label != BOT_LABEL) return;

            // 031: 建値移動（BE）1回のみフラグをクリーンアップ
            _beAppliedPosIds.Remove(p.Id);

            // RR緩和Pending: ポジションクローズで状態リセット（不要な緩和残存を防止）
            if (_rrRelaxPendingActive)
            {
                _rrRelaxPendingActive = false;
                _rrRelaxOriginBarIndex = -1;
            }

            _structureTpBoostedPosIds.Remove(p.Id);
            _structureTpBoostedPosIdsStage2.Remove(p.Id);

            _tpBoostDetectedByPosId.Remove(p.Id);
            _tpBoostPartialClosedByPosId.Remove(p.Id);
            _tpBoostBaseTpPriceByPosId.Remove(p.Id);
            _tpBoostOldTpByPosId.Remove(p.Id);
            _tpBoostNewTpByPosId.Remove(p.Id);
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

            // cTrader整合: 最終残高 - 初期残高
            double netProfit = endingBalance - _proInitialBalance;

            double sumTradeNetProfit = 0.0;
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
                sumTradeNetProfit += t.NetProfit;
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
                balance = _proInitialBalance + sumTradeNetProfit;

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
                ["InitialBalance"] = Round2(_proInitialBalance),
                ["EndingBalance"] = Round2(endingBalance),
                ["EndingEquity"] = Round2(endingEquity),
                ["NetProfit"] = Round2(netProfit),
                ["ROI"] = Round2(roi),
                ["TotalTrades"] = totalTrades,
                ["Wins"] = wins,
                ["Losses"] = losses,
                ["WinRate"] = Round2(winRate),
                ["ProfitFactor"] = double.IsNaN(pf) ? (object)pfText : Round4(pf),
                ["Session"] = new Dictionary<string, object>
                {
                    ["Tokyo"] = new Dictionary<string, object> { ["Pnl"] = Round2(tokyoPnl), ["Count"] = tokyoCount },
                    ["Europe"] = new Dictionary<string, object> { ["Pnl"] = Round2(europePnl), ["Count"] = europeCount },
                    ["NewYork"] = new Dictionary<string, object> { ["Pnl"] = Round2(nyPnl), ["Count"] = nyCount }
                },
                ["Checks"] = new Dictionary<string, object>
                {
                    ["SessionSum"] = new Dictionary<string, object> { ["Result"] = sessionCheck, ["Diff"] = sessionDiff },
                    ["CountSum"] = new Dictionary<string, object> { ["Result"] = countCheck, ["Diff"] = countDiff }
                },
                ["Critical"] = new Dictionary<string, object>
                {
                    ["PeakBalance"] = Round2(peakBalance),
                    ["PeakProfit"] = Round2(peakProfit),
                    ["PeakReachedJst"] = peakReachedText,
                    ["PeakToFinal"] = Round2(peakToFinal),
                    ["MaxBalanceDD"] = Round2(ddMax),
                    ["DdPeakBalance"] = Round2(ddPeakBalance),
                    ["DdPeakTimeJst"] = ddPeakTimeText,
                    ["DdBottomBalance"] = Round2(ddBottomBalance),
                    ["DdBottomTimeJst"] = ddBottomTimeText,
                    ["DdCheck"] = Round2(ddPeakBalance - ddBottomBalance)
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
            html.AppendLine("<style>body{font-family:Arial,Helvetica,sans-serif;line-height:1.4;margin:16px;}h2{margin-top:24px;}pre{background:#f6f6f6;padding:12px;overflow:auto;}table{border-collapse:collapse;}td,th{border:1px solid #ddd;padding:6px 8px;} .netprofit_box{border:2px solid #222;padding:14px 16px;margin:12px 0;background:#fafafa;} .netprofit_label{font-size:14px;color:#333;margin-bottom:6px;} .netprofit_value{font-size:34px;font-weight:800;letter-spacing:0.5px;} .netprofit_pos{color:#0a7a2f;} .netprofit_neg{color:#b00020;}</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("<h1>分析フェーズ：PRO（HTML＋結果）</h1>");
            // パラメーター（OnStart時点）
            html.AppendLine("<h2>【0】パラメーター（テスト実行時点）</h2>");
            html.AppendLine("<table>");
            html.AppendLine("<tr><th style=\"width:55%\">表示名</th><th style=\"width:45%\">値</th></tr>");

            if (_paramSnapshotEntries.Count == 0)
            {
                html.AppendLine("<tr><td colspan=\"2\">[ERR] パラメータースナップショットが取得できませんでした</td></tr>");
            }
            else
            {
                string lastGroup = null;

                foreach (var p in _paramSnapshotEntries)
                {
                    var g = p.GroupName ?? "";

                    // group header row (only when group changes, preserving UI order)
                    if (!string.Equals(g, lastGroup, StringComparison.Ordinal))
                    {
                        lastGroup = g;
                        html.AppendLine("<tr>"
                            + "<td colspan=\"2\" style=\"font-weight:bold;font-size:15px;background:#f0f0f0;padding:8px;\">"
                            + EscapeHtml(string.IsNullOrWhiteSpace(g) ? "[NO_GROUP]" : g)
                            + "</td>"
                            + "</tr>");
                    }

                    // parameter row (表示名 + 値) / 内部名は補助表示
                    html.AppendLine("<tr>"
                        + "<td style=\"padding-left:16px;\">"
                        + EscapeHtml(p.DisplayName)
                        + "<div style=\"font-size:11px;color:#666;margin-top:2px;\">"
                        + EscapeHtml(p.PropertyName)
                        + "</div>"
                        + "</td>"
                        + "<td><strong>" + EscapeHtml(p.ValueText) + "</strong></td>"
                        + "</tr>");
                }
            }

            html.AppendLine("</table>");
            html.AppendLine("<pre>※ parameters は HTML末尾の JSON（script#pro_report_json）にも同梱されています。</pre>");
            html.AppendLine("<h2>【1】概要ステータス（確定）</h2>");
            // 最終損益（最優先表示）: 最終残高 - 初期残高（cTrader整合）
            string npClass = netProfit >= 0 ? "netprofit_value netprofit_pos" : "netprofit_value netprofit_neg";
            html.AppendLine("<div class=\"netprofit_box\">"
                + "<div class=\"netprofit_label\">最終損益（確定：最終残高 − 初期残高）</div>"
                + "<div class=\"" + npClass + "\">" + netProfit.ToString("0.00", CultureInfo.InvariantCulture) + "</div>"
                + "</div>");
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
            // NOTE: ファイル名は「短縮名 + コード番号」で固定（日時は入れない）
            string codeTag = GetCodeNumberTag();

            var fileName = "PRO_" + codeTag + ".html";

            // PROレポート「はい」の場合、セッション出力フォルダ（cTrader_コード番号_mmdd_HHmm）へ同梱する
            string dir = ResolveProOutputDirectory();
            if (string.IsNullOrWhiteSpace(dir))
                dir = ".";

            var fullPath = Path.Combine(dir, fileName);
            try
            {
                File.WriteAllText(fullPath, html.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Print("PRO_REPORT_SAVE_FAILED | CodeName={0} | BotLabel={1} | Path={2} | Msg={3}", CODE_NAME, BOT_LABEL, fullPath, ex.Message);
                return false;
            }


            // OHLC CSV（PRO HTMLと同名・拡張子のみ.csv）
            try
            {
                string ohlcCsvPath = Path.Combine(Path.GetDirectoryName(fullPath), "OHLC_" + GetCodeNumberTag() + ".csv");
                bool ohlcOk = WriteOhlcCsv(ohlcCsvPath);
                if (ohlcOk)
                    Print("OHLC_CSV_SAVED | CodeName={0} | BotLabel={1} | Path={2}", CODE_NAME, BOT_LABEL, ohlcCsvPath);
                else
                    Print("OHLC_CSV_SAVE_FAILED | CodeName={0} | BotLabel={1} | Path={2}", CODE_NAME, BOT_LABEL, ohlcCsvPath);
            }
            catch (Exception ex2)
            {
                // 失敗してもバックテストは継続
                Print("OHLC_CSV_SAVE_FAILED | CodeName={0} | BotLabel={1} | Path={2} | Msg={3}", CODE_NAME, BOT_LABEL, fullPath, ex2.Message);
            }

            savedPath = fullPath;
            Print("PRO_REPORT_SAVED | CodeName={0} | BotLabel={1} | Path={2}", CODE_NAME, BOT_LABEL, fullPath);
            return true;
        }


        private bool WriteOhlcCsv(string csvPath)
        {
            // PROレポート・OHLC出力が ON のときだけ呼ばれる前提
            // バックテスト実行足（Bars）を基準に、同期間・同銘柄・同時間足のOHLCをCSV出力する
            try
            {
                // Bars が未初期化の場合は失敗扱い（ただしバックテストは継続）
                if (Bars == null || Bars.Count <= 0)
                    return false;

                // 実行足と同一のTimeFrameでBarsを取得（再現性優先）
                var tf = Bars.TimeFrame;
                var bars = MarketData.GetBars(tf, SymbolName);
                if (bars == null || bars.Count <= 0)
                    return false;

                var sb = new System.Text.StringBuilder(bars.Count * 40);
                sb.AppendLine("Time,Open,High,Low,Close");

                for (int i = 0; i < bars.Count; i++)
                {
                    DateTime t = bars.OpenTimes[i];
                    sb.Append(t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    sb.Append(",");
                    sb.Append(bars.OpenPrices[i].ToString("R", CultureInfo.InvariantCulture));
                    sb.Append(",");
                    sb.Append(bars.HighPrices[i].ToString("R", CultureInfo.InvariantCulture));
                    sb.Append(",");
                    sb.Append(bars.LowPrices[i].ToString("R", CultureInfo.InvariantCulture));
                    sb.Append(",");
                    sb.AppendLine(bars.ClosePrices[i].ToString("R", CultureInfo.InvariantCulture));
                }

                File.WriteAllText(csvPath, sb.ToString(), System.Text.Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
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


        // ############################################################
        // [ADD] HighLow（019）移植メソッド群 - START
        // ############################################################

        // ============================================================
        // HL Entry Permission (sell_buy.txt ルール)
        // ============================================================
        private void HL_ResolveEntryPermission(TrendState h1, TrendState m5, out bool allowLong, out bool allowShort, out string reason)
        {
            allowLong = false;
            allowShort = false;
            reason = "";

            // UpTrend × UpTrend → Long のみ
            if (h1 == TrendState.UpTrend && m5 == TrendState.UpTrend) { allowLong = true; reason = "UP_UP"; return; }
            // UpTrend × TrendLess → Long のみ
            if (h1 == TrendState.UpTrend && m5 == TrendState.TrendLess) { allowLong = true; reason = "UP_TL"; return; }
            // UpTrend × DownTrend → エントリーしない
            if (h1 == TrendState.UpTrend && m5 == TrendState.DownTrend) { reason = "UP_DOWN_NOENTRY"; return; }
            // TrendLess × UpTrend → Long のみ
            if (h1 == TrendState.TrendLess && m5 == TrendState.UpTrend) { allowLong = true; reason = "TL_UP"; return; }
            // TrendLess × TrendLess → 両方無効
            if (h1 == TrendState.TrendLess && m5 == TrendState.TrendLess) { reason = "TL_TL_NOENTRY"; return; }
            // TrendLess × DownTrend → Short のみ
            if (h1 == TrendState.TrendLess && m5 == TrendState.DownTrend) { allowShort = true; reason = "TL_DOWN"; return; }
            // DownTrend × UpTrend → エントリーしない
            if (h1 == TrendState.DownTrend && m5 == TrendState.UpTrend) { reason = "DOWN_UP_NOENTRY"; return; }
            // DownTrend × TrendLess → Short のみ
            if (h1 == TrendState.DownTrend && m5 == TrendState.TrendLess) { allowShort = true; reason = "DOWN_TL"; return; }
            // DownTrend × DownTrend → Short のみ
            if (h1 == TrendState.DownTrend && m5 == TrendState.DownTrend) { allowShort = true; reason = "DOWN_DOWN"; return; }

            reason = "UNKNOWN";
        }

        private string HL_FormatTrendStateForLog(TrendState state)
        {
            if (state == TrendState.UpTrend) return "UpTrend";
            if (state == TrendState.DownTrend) return "DownTrend";
            return "TrendLess";
        }

        private string HL_FormatHlDowTrendState(TrendState state)
        {
            if (state == TrendState.UpTrend) return "UP TREND";
            if (state == TrendState.DownTrend) return "DOWN TREND";
            return "TREND LESS";
        }

        private Color HL_ResolveHlDowStateColor(TrendState state)
        {
            if (state == TrendState.UpTrend) return Color.LightSkyBlue;
            if (state == TrendState.DownTrend) return Color.Red;
            return Color.Yellow;
        }

        // ============================================================
        // HL History loading
        // ============================================================
        private void HL_EnsureMinimumBars(Bars bars, int requiredCount, string label)
        {
            if (bars == null) return;
            for (int retry = 0; retry < HL_LoadHistoryMaxRetries && bars.Count < requiredCount; retry++)
            {
                int before = bars.Count;
                bars.LoadMoreHistory();
                int after = bars.Count;
                Print("HL_LOAD_HISTORY | {0} | retry={1}/{2} | before={3} | after={4} | target={5}",
                    label, retry + 1, HL_LoadHistoryMaxRetries, before, after, requiredCount);
                if (after <= before) break;
            }
        }

        // ============================================================
        // HL Bar change detection
        // ============================================================
        private bool HL_HasNewClosedBar(Bars bars, ref DateTime lastCheckTime)
        {
            if (bars == null || bars.Count < 2) return false;
            DateTime closedTime = bars.OpenTimes[bars.Count - 2];
            if (closedTime == lastCheckTime) return false;
            lastCheckTime = closedTime;
            return true;
        }

        // ============================================================
        // HL Pivot Calculation
        // ============================================================
        private void HL_RecalculateAndProject()
        {
            if ((_hlBarsM5 == null || _hlBarsM5.Count < 10) && (_hlBarsH1 == null || _hlBarsH1.Count < 10))
            {
                _m5Pivots = new List<HL_Pivot>();
                _h1Pivots = new List<HL_Pivot>();
                return;
            }
            int maxBars = Math.Max(HL_MaxBars, HL_計算対象バー);
            HL_RebuildPivotsIfNeeded(_hlBarsM5, HL_RightBars_M5, maxBars, ref _hlLastPivotCalcBarTimeM5, ref _m5Pivots);
            HL_RebuildPivotsIfNeeded(_hlBarsH1, HL_RightBars_H1, maxBars, ref _hlLastPivotCalcBarTimeH1, ref _h1Pivots);
        }

        private void HL_RebuildPivotsIfNeeded(Bars bars, int rightBars, int maxBars, ref DateTime lastCalcBarTime, ref List<HL_Pivot> target)
        {
            if (bars == null || bars.Count < 10) { target = new List<HL_Pivot>(); lastCalcBarTime = DateTime.MinValue; return; }
            int closedIndex = bars.Count - 2;
            if (closedIndex < 0 || closedIndex >= bars.Count) return;
            DateTime closedBarTime = bars.OpenTimes[closedIndex];
            if (target != null && target.Count > 0 && lastCalcBarTime == closedBarTime) return;
            target = HL_BuildPivots(bars, rightBars, maxBars);
            lastCalcBarTime = closedBarTime;
        }

        private List<HL_Pivot> HL_BuildPivots(Bars bars, int rightBars, int maxBars)
        {
            var pivots = new List<HL_Pivot>(1024);
            if (bars == null || bars.Count < 10) return pivots;
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
                    if (last.HasValue) { if (last.Value.Side == 1) isHigh = false; else isLow = false; }
                    else { isLow = false; }
                }
                if (isHigh)
                {
                    var c = new HL_Pivot { Index = i, Time = bars.OpenTimes[i], Price = bars.HighPrices[i], Side = 1, Kind = string.Empty };
                    if (HL_TryAcceptPivot(c, ref last, pivots, deviation, suppress, HL_Backstep)) continue;
                }
                if (isLow)
                {
                    var c = new HL_Pivot { Index = i, Time = bars.OpenTimes[i], Price = bars.LowPrices[i], Side = -1, Kind = string.Empty };
                    HL_TryAcceptPivot(c, ref last, pivots, deviation, suppress, HL_Backstep);
                }
            }
            HL_AssignPivotKindsByDisplayRule(pivots);
            return pivots;
        }

        private double HL_GetDeviationPrice(double points) { return points <= 0.0 ? 0.0 : points * Symbol.TickSize; }

        private static bool HL_IsPivotHigh(Bars bars, int i, int startIndex, int maxCandidate, int depth, int rightBars, bool tieRight)
        {
            int left = Math.Max(startIndex, i - depth);
            int right = Math.Min(maxCandidate, i + Math.Max(0, rightBars));
            double h = bars.HighPrices[i];
            for (int k = left; k <= right; k++)
            {
                if (k == i) continue;
                if (bars.HighPrices[k] > h) return false;
                if (tieRight && k > i && bars.HighPrices[k] == h) return false;
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
                if (k == i) continue;
                if (bars.LowPrices[k] < l) return false;
                if (tieRight && k > i && bars.LowPrices[k] == l) return false;
            }
            return true;
        }

        private static bool HL_TryAcceptPivot(HL_Pivot candidate, ref HL_Pivot? last, List<HL_Pivot> pivots, double deviation, double suppress, int backstep)
        {
            if (suppress > 0.0 && last.HasValue) { if (Math.Abs(candidate.Price - last.Value.Price) < suppress) return false; }
            if (!last.HasValue) { pivots.Add(candidate); last = candidate; return true; }
            var prev = last.Value;
            if (candidate.Side == prev.Side)
            {
                if (backstep > 0 && (candidate.Index - prev.Index) <= backstep)
                {
                    bool moreExtreme = (candidate.Side == 1 && candidate.Price >= prev.Price) || (candidate.Side == -1 && candidate.Price <= prev.Price);
                    if (moreExtreme) { pivots[pivots.Count - 1] = candidate; last = candidate; return true; }
                    return false;
                }
                bool replace = (candidate.Side == 1 && candidate.Price >= prev.Price) || (candidate.Side == -1 && candidate.Price <= prev.Price);
                if (replace) { pivots[pivots.Count - 1] = candidate; last = candidate; return true; }
                return false;
            }
            if (deviation > 0.0 && Math.Abs(candidate.Price - prev.Price) < deviation) return false;
            pivots.Add(candidate); last = candidate; return true;
        }

        private void HL_AssignPivotKindsByDisplayRule(List<HL_Pivot> pivots)
        {
            if (pivots == null || pivots.Count == 0) return;
            double tolerancePrice = InputPipsToPrice(HL_EqualTolerancePips);
            double? prevHigh = null;
            double? prevLow = null;
            for (int i = 0; i < pivots.Count; i++)
            {
                HL_Pivot p = pivots[i];
                if (p.Side == 1)
                {
                    if (!prevHigh.HasValue) p.Kind = "H";
                    else if (Math.Abs(p.Price - prevHigh.Value) <= tolerancePrice) p.Kind = "H"; // 同値=更新なし
                    else p.Kind = p.Price > prevHigh.Value ? "HH" : "LH";
                    prevHigh = p.Price;
                }
                else if (p.Side == -1)
                {
                    if (!prevLow.HasValue) p.Kind = "L";
                    else if (Math.Abs(p.Price - prevLow.Value) <= tolerancePrice) p.Kind = "L"; // 同値=更新なし
                    else p.Kind = p.Price > prevLow.Value ? "HL" : "LL";
                    prevLow = p.Price;
                }
                else { p.Kind = string.Empty; }
                pivots[i] = p;
            }
        }

        private static string HL_NormalizePivotKind(string kind) { return string.IsNullOrWhiteSpace(kind) ? string.Empty : kind.Trim().ToUpperInvariant(); }
        private static bool HL_IsPivotKind(HL_Pivot pivot, string expected) { return string.Equals(HL_NormalizePivotKind(pivot.Kind), expected, StringComparison.Ordinal); }

        private static bool HL_IsSamePivot(HL_Pivot? a, HL_Pivot? b)
        {
            if (!a.HasValue && !b.HasValue) return true;
            if (!a.HasValue || !b.HasValue) return false;
            HL_Pivot x = a.Value; HL_Pivot y = b.Value;
            return x.Index == y.Index && x.Time == y.Time && x.Side == y.Side && x.Price.Equals(y.Price)
                && string.Equals(HL_NormalizePivotKind(x.Kind), HL_NormalizePivotKind(y.Kind), StringComparison.Ordinal);
        }

        private static int HL_FindLatestPivotListIndexByKind(List<HL_Pivot> pivots, string kind, int startListIndexInclusive)
        {
            if (pivots == null || pivots.Count == 0 || startListIndexInclusive < 0) return -1;
            int start = Math.Min(startListIndexInclusive, pivots.Count - 1);
            for (int i = start; i >= 0; i--) { if (HL_IsPivotKind(pivots[i], kind)) return i; }
            return -1;
        }

        private static int HL_FindLatestPivotListIndexBySide(List<HL_Pivot> pivots, int side, int startListIndexInclusive)
        {
            if (pivots == null || pivots.Count == 0 || startListIndexInclusive < 0) return -1;
            int start = Math.Min(startListIndexInclusive, pivots.Count - 1);
            for (int i = start; i >= 0; i--) { if (pivots[i].Side == side) return i; }
            return -1;
        }

        private static List<HL_Pivot> HL_GetConfirmedPivotsUpToTime(List<HL_Pivot> pivots, DateTime cutoffTime)
        {
            var result = new List<HL_Pivot>(pivots == null ? 0 : pivots.Count);
            if (pivots == null || pivots.Count == 0) return result;
            for (int i = 0; i < pivots.Count; i++) { if (pivots[i].Time <= cutoffTime) result.Add(pivots[i]); }
            return result;
        }

        // ============================================================
        // HL Dow State Machine
        // ============================================================
        private void HL_ResetHlDowStateMachine()
        {
            _hlDowStateM5 = TrendState.TrendLess; _hlDowStateH1 = TrendState.TrendLess;
            _hlDowContextM5 = TrendLessContext.None; _hlDowContextH1 = TrendLessContext.None;
            _hlDowContextStartTimeM5 = DateTime.MinValue; _hlDowContextStartTimeH1 = DateTime.MinValue;
            _hlDowDefenseLowM5 = null; _hlDowDefenseLowH1 = null;
            _hlDowTrendLessRefHighM5 = null; _hlDowTrendLessRefHighH1 = null;
            _hlDowTrendLessArmedHLM5 = false; _hlDowTrendLessArmedHLH1 = false;
            _hlDowAfterUpKeyHighM5 = null; _hlDowAfterUpKeyHighH1 = null;
            _hlDowAfterUpKeyLowM5 = null; _hlDowAfterUpKeyLowH1 = null;
            _hlDowAfterUpHasHighM5 = false; _hlDowAfterUpHasHighH1 = false;
            _hlDowTrendKeyHLM5 = null; _hlDowTrendKeyHLH1 = null;
            _hlDowTrendKeyLHM5 = null; _hlDowTrendKeyLHH1 = null;
            _hlDowTrendKeyHLUpdatedBarTimeM5 = DateTime.MinValue; _hlDowTrendKeyHLUpdatedBarTimeH1 = DateTime.MinValue;
            _hlDowTrendKeyLHUpdatedBarTimeM5 = DateTime.MinValue; _hlDowTrendKeyLHUpdatedBarTimeH1 = DateTime.MinValue;
            _hlDowLastProcessedBarTimeM5 = DateTime.MinValue; _hlDowLastProcessedBarTimeH1 = DateTime.MinValue;
            _hlDowLastLoggedBarTimeM5 = DateTime.MinValue; _hlDowLastLoggedBarTimeH1 = DateTime.MinValue;
        }

        private void HL_EvaluateHlDowAndUpdateUi(DateTime utcNow, bool initializeOnStart)
        {
            if (initializeOnStart) HL_ResetHlDowStateMachine();
            List<HL_Pivot> h1Input = _h1Pivots ?? new List<HL_Pivot>();
            HL_EvaluateOneTf("M5", _m5Pivots ?? new List<HL_Pivot>(), HL_ResolveBarsForTf(HL時間足.M5), ref _hlDowStateM5, ref _hlDowContextM5, ref _hlDowContextStartTimeM5, ref _hlDowDefenseLowM5, ref _hlDowTrendLessRefHighM5, ref _hlDowTrendLessArmedHLM5, ref _hlDowAfterUpKeyHighM5, ref _hlDowAfterUpKeyLowM5, ref _hlDowAfterUpHasHighM5, ref _hlDowTrendKeyHLM5, ref _hlDowTrendKeyLHM5, ref _hlDowTrendKeyHLUpdatedBarTimeM5, ref _hlDowTrendKeyLHUpdatedBarTimeM5, ref _hlDowLastProcessedBarTimeM5, ref _hlDowLastLoggedBarTimeM5, utcNow);
            HL_EvaluateOneTf("H1", h1Input, HL_ResolveBarsForTf(HL時間足.H1), ref _hlDowStateH1, ref _hlDowContextH1, ref _hlDowContextStartTimeH1, ref _hlDowDefenseLowH1, ref _hlDowTrendLessRefHighH1, ref _hlDowTrendLessArmedHLH1, ref _hlDowAfterUpKeyHighH1, ref _hlDowAfterUpKeyLowH1, ref _hlDowAfterUpHasHighH1, ref _hlDowTrendKeyHLH1, ref _hlDowTrendKeyLHH1, ref _hlDowTrendKeyHLUpdatedBarTimeH1, ref _hlDowTrendKeyLHUpdatedBarTimeH1, ref _hlDowLastProcessedBarTimeH1, ref _hlDowLastLoggedBarTimeH1, utcNow);
            HL_UpdateHlDowStatusDisplay(initializeOnStart, _hlDowStateM5, _hlDowStateH1);
        }

        private Bars HL_ResolveBarsForTf(HL時間足 tf)
        {
            switch (tf)
            {
                case HL時間足.M5:
                    if ((_hlBarsM5 == null || _hlBarsM5.Count < 2) && Bars != null && Bars.TimeFrame == TimeFrame.Minute5) return Bars;
                    return _hlBarsM5;
                default:
                    if ((_hlBarsH1 == null || _hlBarsH1.Count < 2) && Bars != null && Bars.TimeFrame == TimeFrame.Hour) return Bars;
                    return _hlBarsH1;
            }
        }

        private static void HL_ClearAfterUpKeys(ref HL_Pivot? afterUpKeyHigh, ref HL_Pivot? afterUpKeyLow, ref bool afterUpHasHigh)
        { afterUpKeyHigh = null; afterUpKeyLow = null; afterUpHasHigh = false; }

        private static void HL_ClearTrendLessSharedKeys(ref HL_Pivot? defenseLow, ref HL_Pivot? refHigh, ref bool armedHL)
        { defenseLow = null; refHigh = null; armedHL = false; }

        private bool HL_TryGetLastClosedBarInfo(Bars bars, out DateTime barTime, out double close)
        {
            barTime = DateTime.MinValue; close = 0.0;
            if (bars == null || bars.Count < 2) return false;
            int closedIndex = bars.Count - 2;
            if (closedIndex < 0 || closedIndex >= bars.Count) return false;
            barTime = bars.OpenTimes[closedIndex];
            close = bars.ClosePrices[closedIndex];
            return true;
        }

        private void HL_EvaluateOneTf(string tf, List<HL_Pivot> input, Bars bars,
            ref TrendState state, ref TrendLessContext context, ref DateTime contextStart,
            ref HL_Pivot? defenseLow, ref HL_Pivot? trendLessRefHigh, ref bool trendLessArmedHL,
            ref HL_Pivot? afterUpKeyHigh, ref HL_Pivot? afterUpKeyLow, ref bool afterUpHasHigh,
            ref HL_Pivot? trendKeyHL, ref HL_Pivot? trendKeyLH,
            ref DateTime trendKeyHLUpdatedBarTime, ref DateTime trendKeyLHUpdatedBarTime,
            ref DateTime lastProcessedBarTime, ref DateTime lastLoggedBarTime, DateTime utcNow)
        {
            DateTime barTime; double close;
            if (!HL_TryGetLastClosedBarInfo(bars, out barTime, out close)) return;
            bool newBar = (barTime != lastProcessedBarTime);
            if (!newBar) return;
            lastProcessedBarTime = barTime;

            TrendState stateBefore = state;
            HL_Pivot? refHigh = null, lastHL = null, lastLH = null, refLow = null;
            HL_Pivot? keyLH = null, keyHL = null, triggerHigh = null, triggerLow = null;
            int hIndex = -1, lIndex = -1, loIndex = -1, hiIndex = -1;
            double? hPrice = null, lPrice = null;
            bool hlFormed = false, lhFormed = false;
            string reason = string.Empty;

            HL_DetermineTrendFromDowStateMachine(
                ref state, ref context, ref contextStart, ref defenseLow, ref trendLessRefHigh, ref trendLessArmedHL,
                ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh,
                ref trendKeyHL, ref trendKeyLH, ref trendKeyHLUpdatedBarTime, ref trendKeyLHUpdatedBarTime,
                input, bars, barTime, close,
                out refHigh, out lastHL, out lastLH, out refLow, out keyLH, out keyHL,
                out triggerHigh, out triggerLow, out hIndex, out hPrice, out lIndex, out lPrice,
                out loIndex, out hiIndex, out hlFormed, out lhFormed, out reason);

            if (!HL_LogOutput) return;
            if (barTime == lastLoggedBarTime) return;
            if (!HL_ログ毎バー出す && stateBefore == state) return;

            lastLoggedBarTime = barTime;
            Print("HL_DOW[{0}] {1} -> {2} | bar={3} | close={4} | reason={5}",
                tf, HL_FormatTrendStateForLog(stateBefore), HL_FormatTrendStateForLog(state),
                barTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                close.ToString("F5", CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(reason) ? "UNSPECIFIED" : reason.Trim());
        }

        private TrendState HL_DetermineTrendFromDowStateMachine(
            ref TrendState state, ref TrendLessContext context, ref DateTime contextStartTime,
            ref HL_Pivot? defenseLow, ref HL_Pivot? trendLessRefHigh, ref bool trendLessArmedHL,
            ref HL_Pivot? afterUpKeyHigh, ref HL_Pivot? afterUpKeyLow, ref bool afterUpHasHigh,
            ref HL_Pivot? trendKeyHL, ref HL_Pivot? trendKeyLH,
            ref DateTime trendKeyHLUpdatedBarTime, ref DateTime trendKeyLHUpdatedBarTime,
            List<HL_Pivot> pivots, Bars bars, DateTime currentBarTime, double currentClose,
            out HL_Pivot? refHigh, out HL_Pivot? lastHL, out HL_Pivot? lastLH, out HL_Pivot? refLow,
            out HL_Pivot? keyLH, out HL_Pivot? keyHL, out HL_Pivot? triggerHigh, out HL_Pivot? triggerLow,
            out int hIndex, out double? hPrice, out int lIndex, out double? lPrice,
            out int loIndex, out int hiIndex, out bool hlFormed, out bool lhFormed, out string reason)
        {
            TrendLessContext contextBefore = context;
            refHigh = null; lastHL = null; lastLH = null; refLow = null;
            keyLH = null; keyHL = null; triggerHigh = null; triggerLow = null;
            hIndex = -1; hPrice = null; lIndex = -1; lPrice = null;
            loIndex = -1; hiIndex = -1; hlFormed = false; lhFormed = false;
            reason = "TL_STAY_NO_STRUCTURE";

            if (pivots == null || pivots.Count == 0 || bars == null || bars.Count < 3)
            {
                state = TrendState.TrendLess; context = TrendLessContext.None; contextStartTime = DateTime.MinValue;
                HL_ClearTrendLessSharedKeys(ref defenseLow, ref trendLessRefHigh, ref trendLessArmedHL);
                HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh);
                trendKeyHL = null; trendKeyLH = null;
                trendKeyHLUpdatedBarTime = DateTime.MinValue; trendKeyLHUpdatedBarTime = DateTime.MinValue;
                reason = "TL_STAY_NO_PIVOT"; return state;
            }

            int closedIndex = bars.Count - 2;
            if (closedIndex < 1)
            {
                state = TrendState.TrendLess; context = TrendLessContext.None; contextStartTime = DateTime.MinValue;
                HL_ClearTrendLessSharedKeys(ref defenseLow, ref trendLessRefHigh, ref trendLessArmedHL);
                HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh);
                trendKeyHL = null; trendKeyLH = null;
                trendKeyHLUpdatedBarTime = DateTime.MinValue; trendKeyLHUpdatedBarTime = DateTime.MinValue;
                reason = "TL_STAY_NO_CLOSED_BAR"; return state;
            }

            List<HL_Pivot> closedPivots = HL_GetConfirmedPivotsUpToTime(pivots, currentBarTime);
            if (closedPivots.Count == 0)
            {
                state = TrendState.TrendLess; context = TrendLessContext.None; contextStartTime = DateTime.MinValue;
                HL_ClearTrendLessSharedKeys(ref defenseLow, ref trendLessRefHigh, ref trendLessArmedHL);
                HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh);
                trendKeyHL = null; trendKeyLH = null;
                trendKeyHLUpdatedBarTime = DateTime.MinValue; trendKeyLHUpdatedBarTime = DateTime.MinValue;
                reason = "TL_STAY_NO_CONFIRMED_PIVOT"; return state;
            }

            int lastIndex = closedPivots.Count - 1;
            int latestHighIdx = HL_FindLatestPivotListIndexBySide(closedPivots, 1, lastIndex);
            int latestLowIdx = HL_FindLatestPivotListIndexBySide(closedPivots, -1, lastIndex);
            int latestHlIdx = HL_FindLatestPivotListIndexByKind(closedPivots, "HL", lastIndex);
            int latestLhIdx = HL_FindLatestPivotListIndexByKind(closedPivots, "LH", lastIndex);
            int latestLlIdx = HL_FindLatestPivotListIndexByKind(closedPivots, "LL", lastIndex);

            HL_Pivot? latestHighPivot = latestHighIdx >= 0 ? closedPivots[latestHighIdx] : (HL_Pivot?)null;
            HL_Pivot? latestLowPivot = latestLowIdx >= 0 ? closedPivots[latestLowIdx] : (HL_Pivot?)null;
            HL_Pivot? latestLlPivot = latestLlIdx >= 0 ? closedPivots[latestLlIdx] : (HL_Pivot?)null;

            if (latestHlIdx >= 0) lastHL = closedPivots[latestHlIdx];
            if (latestLhIdx >= 0) lastLH = closedPivots[latestLhIdx];
            hlFormed = lastHL.HasValue; lhFormed = lastLH.HasValue;

            if (latestHighPivot.HasValue) { hIndex = latestHighPivot.Value.Index; hPrice = latestHighPivot.Value.Price; }
            if (lastHL.HasValue) { lIndex = lastHL.Value.Index; lPrice = lastHL.Value.Price; }
            if (latestLowPivot.HasValue) loIndex = latestLowPivot.Value.Index;
            if (lastLH.HasValue) hiIndex = lastLH.Value.Index;

            // UpTrend
            if (state == TrendState.UpTrend)
            {
                if (!HL_IsSamePivot(trendKeyHL, lastHL)) { trendKeyHL = lastHL; trendKeyHLUpdatedBarTime = currentBarTime; }
                keyHL = trendKeyHL; triggerLow = keyHL;
                bool canBreakUp = keyHL.HasValue && trendKeyHLUpdatedBarTime != currentBarTime;
                if (canBreakUp && currentClose < keyHL.Value.Price)
                {
                    state = TrendState.TrendLess; context = TrendLessContext.AfterUpEnd; contextStartTime = currentBarTime;
                    defenseLow = null; trendLessRefHigh = latestHighPivot; trendLessArmedHL = false;
                    HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh);
                    refHigh = trendLessRefHigh; refLow = null; triggerHigh = trendLessRefHigh;
                    reason = "UP_TO_TL_BY_KEYHL_BREAK"; return state;
                }
                if (keyHL.HasValue && trendKeyHLUpdatedBarTime == currentBarTime) reason = "UP_HOLD_KEYHL_UPDATED_THIS_BAR";
                else reason = keyHL.HasValue ? "UP_HOLD_KEYHL_NOT_BROKEN" : "UP_HOLD_NO_KEYHL";
                refHigh = latestHighPivot; refLow = keyHL; triggerHigh = null; return state;
            }

            // DownTrend
            if (state == TrendState.DownTrend)
            {
                if (!HL_IsSamePivot(trendKeyLH, lastLH)) { trendKeyLH = lastLH; trendKeyLHUpdatedBarTime = currentBarTime; }
                keyLH = trendKeyLH; triggerHigh = keyLH;
                bool canBreakDown = keyLH.HasValue && trendKeyLHUpdatedBarTime != currentBarTime;
                if (canBreakDown && currentClose > keyLH.Value.Price)
                {
                    state = TrendState.TrendLess; context = TrendLessContext.AfterDownEnd; contextStartTime = currentBarTime;
                    defenseLow = latestLlPivot; trendLessRefHigh = latestHighPivot; trendLessArmedHL = false;
                    HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh);
                    refHigh = trendLessRefHigh; refLow = defenseLow; triggerHigh = trendLessRefHigh; triggerLow = defenseLow;
                    reason = defenseLow.HasValue ? "DOWN_TO_TL_BY_KEYLH_BREAK" : "DOWN_TO_TL_BY_KEYLH_BREAK_NO_LL"; return state;
                }
                if (keyLH.HasValue && trendKeyLHUpdatedBarTime == currentBarTime) reason = "DOWN_HOLD_KEYLH_UPDATED_THIS_BAR";
                else reason = keyLH.HasValue ? "DOWN_HOLD_KEYLH_NOT_BROKEN" : "DOWN_HOLD_NO_KEYLH";
                refHigh = keyLH; refLow = latestLowPivot; triggerLow = null; return state;
            }

            // TrendLess
            if (state != TrendState.TrendLess) state = TrendState.TrendLess;

            if (context == TrendLessContext.None)
            {
                defenseLow = null;
                HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh);
                if (latestHighPivot.HasValue) trendLessRefHigh = latestHighPivot;
                else if (!trendLessRefHigh.HasValue) trendLessRefHigh = null;
                trendLessArmedHL = lastHL.HasValue;
            }
            else
            {
                if (contextStartTime == DateTime.MinValue) contextStartTime = currentBarTime;
                HL_Pivot? updatedRefHigh = trendLessRefHigh;
                bool updatedArmedHL = false;
                HL_Pivot? updatedAfterUpKeyHigh = null;
                HL_Pivot? updatedAfterUpKeyLow = null;
                bool updatedAfterUpHasHigh = false;

                for (int i = 0; i < closedPivots.Count; i++)
                {
                    HL_Pivot p = closedPivots[i];
                    if (p.Time <= contextStartTime) continue;
                    if (p.Side == 1) { if (!updatedRefHigh.HasValue || p.Price > updatedRefHigh.Value.Price) updatedRefHigh = p; }
                    if (HL_IsPivotKind(p, "HL")) updatedArmedHL = true;
                    if (context == TrendLessContext.AfterUpEnd)
                    {
                        if (p.Side == 1) { updatedAfterUpKeyHigh = p; updatedAfterUpHasHigh = true; updatedAfterUpKeyLow = null; }
                        else if (updatedAfterUpHasHigh) { if (!updatedAfterUpKeyLow.HasValue || p.Price < updatedAfterUpKeyLow.Value.Price) updatedAfterUpKeyLow = p; }
                    }
                }
                trendLessRefHigh = updatedRefHigh; trendLessArmedHL = updatedArmedHL;
                if (context == TrendLessContext.AfterUpEnd)
                { afterUpKeyHigh = updatedAfterUpKeyHigh; afterUpKeyLow = updatedAfterUpKeyLow; afterUpHasHigh = updatedAfterUpHasHigh; }
                else { HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh); }
            }

            refHigh = trendLessRefHigh;
            refLow = context == TrendLessContext.AfterDownEnd ? defenseLow
                : (context == TrendLessContext.AfterUpEnd ? afterUpKeyLow : latestLowPivot);
            triggerHigh = context == TrendLessContext.AfterUpEnd && afterUpKeyHigh.HasValue ? afterUpKeyHigh : trendLessRefHigh;
            triggerLow = context == TrendLessContext.AfterDownEnd ? defenseLow
                : (context == TrendLessContext.AfterUpEnd ? afterUpKeyLow : lastHL);
            if (refHigh.HasValue) { hIndex = refHigh.Value.Index; hPrice = refHigh.Value.Price; }
            if (refLow.HasValue) loIndex = refLow.Value.Index;

            // TrendLess->UpTrend
            if (trendLessArmedHL && trendLessRefHigh.HasValue && currentClose > trendLessRefHigh.Value.Price)
            {
                state = TrendState.UpTrend; context = TrendLessContext.None; contextStartTime = DateTime.MinValue;
                trendKeyHL = lastHL; trendKeyHLUpdatedBarTime = currentBarTime;
                HL_ClearTrendLessSharedKeys(ref defenseLow, ref trendLessRefHigh, ref trendLessArmedHL);
                HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh);
                reason = contextBefore == TrendLessContext.AfterDownEnd ? "TL_AFTERDOWN_TO_UP_BY_REFHIGH_BREAK"
                    : (contextBefore == TrendLessContext.AfterUpEnd ? "TL_AFTERUP_TO_UP_BY_REFHIGH_BREAK" : "TL_TO_UP_BY_REFHIGH_BREAK");
                return state;
            }

            // Context-specific Down transitions
            if (context == TrendLessContext.AfterDownEnd)
            {
                if (defenseLow.HasValue && currentClose < defenseLow.Value.Price)
                {
                    state = TrendState.DownTrend; context = TrendLessContext.None; contextStartTime = DateTime.MinValue;
                    trendKeyLH = lastLH; trendKeyLHUpdatedBarTime = currentBarTime;
                    HL_ClearTrendLessSharedKeys(ref defenseLow, ref trendLessRefHigh, ref trendLessArmedHL);
                    HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh);
                    reason = "TL_AFTERDOWN_TO_DOWN_BY_DEFENSELOW_BREAK"; return state;
                }
                reason = defenseLow.HasValue ? "TL_AFTERDOWN_STAY_WAIT_BREAK" : "TL_AFTERDOWN_STAY_NO_DEFENSELOW"; return state;
            }

            if (context == TrendLessContext.AfterUpEnd)
            {
                if (afterUpKeyHigh.HasValue && afterUpKeyLow.HasValue && currentClose < afterUpKeyLow.Value.Price)
                {
                    state = TrendState.DownTrend; context = TrendLessContext.None; contextStartTime = DateTime.MinValue;
                    trendKeyLH = lastLH; trendKeyLHUpdatedBarTime = currentBarTime;
                    HL_ClearTrendLessSharedKeys(ref defenseLow, ref trendLessRefHigh, ref trendLessArmedHL);
                    HL_ClearAfterUpKeys(ref afterUpKeyHigh, ref afterUpKeyLow, ref afterUpHasHigh);
                    reason = "TL_AFTERUP_TO_DOWN_BY_AFTERUP_KEYLOW_BREAK"; return state;
                }
                if (!afterUpHasHigh || !afterUpKeyHigh.HasValue) reason = "TL_AFTERUP_STAY_WAIT_KEYHIGH";
                else if (!afterUpKeyLow.HasValue) reason = "TL_AFTERUP_STAY_WAIT_KEYLOW";
                else reason = "TL_AFTERUP_STAY_KEYLOW_NOT_BROKEN";
                return state;
            }

            reason = trendLessArmedHL ? "TL_STAY_ARMED_WAIT_REFHIGH_BREAK" : "TL_STAY_WAIT_HL";
            return state;
        }

        // ============================================================
        // HL UI Display
        // ============================================================
        private void HL_EnsureHlDowStatusPanel()
        {
            if (_hlDowStatusPanel != null) return;
            if (Chart == null) return;
            _hlDowStatusPanel = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 8, 8) };
            _hlDowStatusTextM5 = new TextBlock { FontSize = 18, Margin = new Thickness(0, 0, 0, 0) };
            _hlDowStatusTextH1 = new TextBlock { FontSize = 18, Margin = new Thickness(0, 0, 0, 0) };
            _hlDowStatusPanel.AddChild(_hlDowStatusTextM5);
            _hlDowStatusPanel.AddChild(_hlDowStatusTextH1);
            Chart.AddControl(_hlDowStatusPanel);
        }

        private void HL_UpdateHlDowStatusDisplay(bool force, TrendState m5State, TrendState h1State)
        {
            if (!HL_描画検証ログ)
            {
                HL_RemoveHlDowStatusDisplay();
                return;
            }
            string m5Line = "M5 : " + HL_FormatHlDowTrendState(m5State);
            string h1Line = "H1 : " + HL_FormatHlDowTrendState(h1State);
            if (!force && string.Equals(_hlDowLastDrawnH1Text, h1Line, StringComparison.Ordinal)
                && string.Equals(_hlDowLastDrawnM5Text, m5Line, StringComparison.Ordinal)) return;
            _hlDowLastDrawnH1Text = h1Line; _hlDowLastDrawnM5Text = m5Line;
            BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (Chart == null) return;
                    HL_EnsureHlDowStatusPanel();
                    if (_hlDowStatusTextM5 != null) { _hlDowStatusTextM5.Text = m5Line; _hlDowStatusTextM5.ForegroundColor = HL_ResolveHlDowStateColor(m5State); }
                    if (_hlDowStatusTextH1 != null) { _hlDowStatusTextH1.Text = h1Line; _hlDowStatusTextH1.ForegroundColor = HL_ResolveHlDowStateColor(h1State); }
                }
                catch { }
            });
        }

        private void HL_RemoveHlDowStatusDisplay()
        {
            _hlDowLastDrawnH1Text = ""; _hlDowLastDrawnM5Text = "";
            Action removeAction = () =>
            {
                try
                {
                    if (Chart == null) return;
                    if (_hlDowStatusPanel != null) { try { Chart.RemoveControl(_hlDowStatusPanel); } catch { } }
                    _hlDowStatusPanel = null; _hlDowStatusTextM5 = null; _hlDowStatusTextH1 = null;
                }
                catch { }
            };
            try { BeginInvokeOnMainThread(removeAction); } catch { removeAction(); }
        }

        // ============================================================
        // HL Drawing methods
        // ============================================================
        private void HL_RequestPivotRedraw(string reason)
        {
            if (!HL_描画検証ログ) return;
            if (Chart == null) return;
            _hlDrawGeneration++;
            _hlRequestedDrawGeneration = _hlDrawGeneration;
            _hlRedrawQueued = true;
            if (_hlRedrawRunning || _hlRedrawScheduled) return;
            _hlRedrawScheduled = true;
            try { BeginInvokeOnMainThread(HL_ProcessPivotRedrawQueue); }
            catch { _hlRedrawScheduled = false; }
        }

        private void HL_ProcessPivotRedrawQueue()
        {
            if (Chart == null) { _hlRedrawScheduled = false; _hlRedrawQueued = false; _hlRedrawRunning = false; return; }
            if (_hlRedrawRunning) return;
            _hlRedrawScheduled = false; _hlRedrawRunning = true;
            try
            {
                while (_hlRedrawQueued)
                {
                    _hlRedrawQueued = false;
                    long generationToDraw = _hlRequestedDrawGeneration;
                    int drawCount = 0;
                    try
                    {
                        HL_ClearDrawingsByPrefix("HL_PRE_DRAW", generationToDraw);
                        if (HL_描画検証ログ && HL_EA内部Pivot描画)
                        {
                            if (_m5Pivots != null && _m5Pivots.Count >= 2)
                                drawCount += HL_DrawSeries("M5", new List<HL_Pivot>(_m5Pivots), Color.Red, generationToDraw);
                            if (_h1Pivots != null && _h1Pivots.Count >= 2)
                                drawCount += HL_DrawSeries("H1", new List<HL_Pivot>(_h1Pivots), Color.White, generationToDraw);
                        }
                        _hlRenderedDrawGeneration = generationToDraw;
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                _hlRedrawRunning = false;
                if (_hlRedrawQueued && !_hlRedrawScheduled)
                {
                    _hlRedrawScheduled = true;
                    try { BeginInvokeOnMainThread(HL_ProcessPivotRedrawQueue); } catch { _hlRedrawScheduled = false; }
                }
            }
        }

        private int HL_DrawSeries(string tf, List<HL_Pivot> pivots, Color lineColor, long generation)
        {
            int drawCount = 0;
            for (int i = 0; i < pivots.Count - 1; i++)
            {
                var a = pivots[i]; var b = pivots[i + 1];
                string lineName = HL_BuildObjectName(tf, "LINE", a.Index, a.Side, b.Index, b.Side, generation, i);
                Chart.DrawTrendLine(lineName, a.Time, a.Price, b.Time, b.Price, lineColor, 1, LineStyle.Solid);
                drawCount++;
            }
            double yOffset = Math.Max(0, HL_LabelYOffsetTicks) * Symbol.TickSize;
            for (int i = 0; i < pivots.Count; i++)
            {
                var p = pivots[i];
                string text = HL_GetPivotDisplayText(p);
                Color textColor = HL_GetPivotDisplayColor(p, text);
                double y = p.Side == 1 ? p.Price + yOffset : p.Price - yOffset;
                string textName = HL_BuildObjectName(tf, "TEXT", p.Index, p.Side, p.Index, p.Side, generation, i);
                var t = Chart.DrawText(textName, text, p.Time, y, textColor);
                t.FontSize = HL_LabelFontSize; t.VerticalAlignment = VerticalAlignment.Center; t.HorizontalAlignment = HorizontalAlignment.Center;
                drawCount++;
            }
            return drawCount;
        }

        private string HL_GetPivotDisplayText(HL_Pivot pivot)
        {
            string kind = HL_NormalizePivotKind(pivot.Kind);
            if (pivot.Side == 1) return (kind == "HH" || kind == "LH") ? kind : "H";
            if (pivot.Side == -1) return (kind == "HL" || kind == "LL") ? kind : "L";
            return "NA";
        }

        private Color HL_GetPivotDisplayColor(HL_Pivot pivot, string text)
        {
            if (pivot.Side == 1) { if (text == "HH") return Color.Lime; if (text == "LH") return Color.Orange; return Color.Gray; }
            if (pivot.Side == -1) { if (text == "HL") return Color.Aqua; if (text == "LL") return Color.Red; return Color.Gray; }
            return Color.Gray;
        }

        private void HL_ClearDrawingsByPrefix(string reason, long generation)
        {
            if (Chart == null) return;
            int freq = Math.Max(0, HL_強制クリーン頻度);
            if (freq == 0)
            {
                _hlClearCallCount++;
                return;
            }

            bool shouldForceClean = (freq == 1) || ((++_hlCleanTick % freq) == 0);
            if (shouldForceClean)
            {
                int failedCount;
                HL_ForceCleanDrawingsByPrefix(out failedCount);
            }
            _hlClearCallCount++;
        }

        private int HL_ForceCleanDrawingsByPrefix(out int failedCount)
        {
            failedCount = 0;
            if (Chart == null) return 0;
            var names = new List<string>(256);
            try { foreach (var obj in Chart.Objects) { if (obj == null) continue; string name = obj.Name; if (HL_IsDrawObjectTargetName(name)) names.Add(name); } }
            catch { }
            int removedCount = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i]; if (string.IsNullOrEmpty(name)) continue;
                try { Chart.RemoveObject(name); removedCount++; }
                catch { failedCount++; }
            }
            return removedCount;
        }

        private static string HL_SanitizeNameToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "NA";
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        private string HL_BuildObjectName(string tf, string kind, int indexA, int sideA, int indexB, int sideB, long generation, int sequence)
        {
            string symbolToken = HL_SanitizeNameToken(SymbolName);
            string timeframeToken = HL_SanitizeNameToken(Bars != null ? Bars.TimeFrame.ToString() : "NA");
            string tfToken = HL_SanitizeNameToken(tf);
            string kindToken = HL_SanitizeNameToken(kind);
            return string.Format(CultureInfo.InvariantCulture, "{0}{1}.{2}.HL.GEN{3}.{4}.{5}.{6}.{7}.{8}.{9}.{10}",
                HL_DrawObjectPrefix, symbolToken, timeframeToken, generation, tfToken, kindToken, indexA, sideA, indexB, sideB, sequence);
        }

        private bool HL_IsDrawObjectTargetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.StartsWith(HL_DrawObjectPrefix, StringComparison.Ordinal) && name.IndexOf(".HL.", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        private static string HL_BuildTailPivotSignature(List<HL_Pivot> pivots)
        {
            if (pivots == null || pivots.Count < 2) return string.Empty;
            HL_Pivot prev = pivots[pivots.Count - 2]; HL_Pivot last = pivots[pivots.Count - 1];
            return string.Format(CultureInfo.InvariantCulture, "COUNT={0}|P={1}:{2:O}:{3:R}:{4}:{5}|L={6}:{7:O}:{8:R}:{9}:{10}",
                pivots.Count, prev.Index, prev.Time, prev.Price, prev.Side, HL_NormalizePivotKind(prev.Kind),
                last.Index, last.Time, last.Price, last.Side, HL_NormalizePivotKind(last.Kind));
        }

        private string ResolveProOutputDirectory()
        {
            // PROレポート「はい」のときの出力先（指定フォルダ配下に「稼働1回=1フォルダ」を生成し、その中に同梱）
            // フォルダ名：cTrader_{コード番号}_mmdd_HHmm  ※Windows不可文字「:」は使用しない
            if (!string.IsNullOrWhiteSpace(_proSessionOutputDir))
                return _proSessionOutputDir;

            string baseDir = ProReportOutputFolder;
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = @"D:\ChatGPT EA Development\プロジェクト\保管庫";

            try
            {
                Directory.CreateDirectory(baseDir);
            }
            catch
            {
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "cTrader", "Reports");
                try
                {
                    Directory.CreateDirectory(baseDir);
                }
                catch
                {
                    return null;
                }
            }

            try
            {
                string tag = GetCodeNumberTag();
                string mmdd_hhmm = ToJst(DateTime.UtcNow).ToString("MMdd_HHmm", CultureInfo.InvariantCulture);
                string sub = "cTrader_" + tag + "_" + mmdd_hhmm;
                string dir = Path.Combine(baseDir, sub);
                Directory.CreateDirectory(dir);
                _proSessionOutputDir = dir;
                return dir;
            }
            catch
            {
                return baseDir;
            }
        }

        private static double Round2(double v)
        {
            return Math.Round(v, 2, MidpointRounding.AwayFromZero);
        }

        private static double Round4(double v)
        {
            return Math.Round(v, 4, MidpointRounding.AwayFromZero);
        }

        // ############################################################
        // [END] HighLow（019）移植メソッド群
        // ############################################################

    }
}
