using System;
using System.Collections.Generic;
using System.IO;
using cAlgo.API;

namespace cAlgo.Robots
{
    // cTrader(cBot) -> MT5(EA) file-based copier (minimal)
    // Outputs JSON Lines into:
    //   <BaseDir>\outbox\ctrader_to_mt5_YYYYMMDD.log
    //
    // Test assumption:
    //   cTrader Symbol = XAUUSD
    //   MT5 Symbol     = GOLD (mapped on MT5 side via symbol_map.json)
    //
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class CopyToMT5_File : Robot
    {
        // ===== Settings =====
        [Parameter("BaseDir", DefaultValue = @"D:\ChatGPT EA Development\プロジェクト\コピーツール\Log")]
        public string BaseDir { get; set; }

        [Parameter("Symbol (cTrader)", DefaultValue = "XAUUSD")]
        public string MasterSymbol { get; set; }

        // ===== Internals =====
        private string OutboxDir => Path.Combine(BaseDir, "outbox");

        // PositionId -> sequence number for event_id
        private readonly Dictionary<long, int> _seq = new Dictionary<long, int>();

        // For detecting partial closes & SL/TP changes
        private readonly Dictionary<long, double> _lastVolumeUnits = new Dictionary<long, double>();
        private readonly Dictionary<long, double> _lastSl = new Dictionary<long, double>();
        private readonly Dictionary<long, double> _lastTp = new Dictionary<long, double>();

        protected override void OnStart()
        {
            Directory.CreateDirectory(OutboxDir);

            Positions.Opened += OnPosOpened;
            Positions.Closed += OnPosClosed;
            Positions.Modified += OnPosModified;
        }

        private string TodayStr()
        {
            var t = Server.Time; // cTrader server time
            return $"{t:yyyyMMdd}";
        }

        private string OutboxFilePath()
        {
            return Path.Combine(OutboxDir, $"ctrader_to_mt5_{TodayStr()}.log");
        }

        private int NextSeq(long positionId)
        {
            if (!_seq.ContainsKey(positionId))
                _seq[positionId] = 0;

            _seq[positionId]++;
            return _seq[positionId];
        }

        private void AppendLine(string line)
        {
            var path = OutboxFilePath();

            // Allow MT5 to read while we write
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var sw = new StreamWriter(fs))
            {
                sw.WriteLine(line);
                sw.Flush();
                fs.Flush(true); // best-effort immediate flush
            }
        }

        private static string Side(Position p) => p.TradeType == TradeType.Buy ? "BUY" : "SELL";

        private double ToLotsApprox(Position p)
        {
            // IMPORTANT:
            // Emit "volume" as LOT quantity comparable to MT5 lots.
            // Convert cTrader VolumeInUnits -> Quantity (lot-like) using Symbol helper.
            try
            {
                var qty = p.Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits);
                return Math.Round(qty, 2, MidpointRounding.ToZero);
            }
            catch
            {
                // Fallback: step-count approach
                var stepUnits = p.Symbol.VolumeInUnitsMin;
                if (stepUnits <= 0) return 0;
                var lots = p.VolumeInUnits / stepUnits;
                return Math.Round(lots, 2, MidpointRounding.ToZero);
            }
        }

        private void EmitEntry(Position p)
        {
            if (p.SymbolName != MasterSymbol)
                return;

            var seq = NextSeq(p.Id);
            var eventId = $"{p.Id}-{seq}";

            var sl = p.StopLoss.HasValue ? p.StopLoss.Value : 0.0;
            var tp = p.TakeProfit.HasValue ? p.TakeProfit.Value : 0.0;

            var masterBalance = Account.Balance;
            var lots = ToLotsApprox(p);
            if (lots <= 0)
                return;

            var json =
                $"{{\"type\":\"ENTRY\",\"event_id\":\"{eventId}\",\"master_position_id\":{p.Id},\"symbol\":\"{p.SymbolName}\"," +
                $"\"side\":\"{Side(p)}\",\"volume\":{lots:F2},\"sl\":{sl},\"tp\":{tp},\"master_balance\":{masterBalance}}}";

            AppendLine(json);

            _lastVolumeUnits[p.Id] = p.VolumeInUnits;
            _lastSl[p.Id] = sl;
            _lastTp[p.Id] = tp;
        }

        private void EmitModify(Position p)
        {
            if (p.SymbolName != MasterSymbol)
                return;

            var sl = p.StopLoss.HasValue ? p.StopLoss.Value : 0.0;
            var tp = p.TakeProfit.HasValue ? p.TakeProfit.Value : 0.0;

            var prevSl = _lastSl.ContainsKey(p.Id) ? _lastSl[p.Id] : 0.0;
            var prevTp = _lastTp.ContainsKey(p.Id) ? _lastTp[p.Id] : 0.0;

            if (Math.Abs(sl - prevSl) < 1e-9 && Math.Abs(tp - prevTp) < 1e-9)
                return;

            var seq = NextSeq(p.Id);
            var eventId = $"{p.Id}-{seq}";

            var json = $"{{\"type\":\"MODIFY\",\"event_id\":\"{eventId}\",\"master_position_id\":{p.Id},\"sl\":{sl},\"tp\":{tp}}}";
            AppendLine(json);

            _lastSl[p.Id] = sl;
            _lastTp[p.Id] = tp;
        }

        private void EmitPartialCloseIfNeeded(Position p)
        {
            if (p.SymbolName != MasterSymbol)
                return;

            var prevUnits = _lastVolumeUnits.ContainsKey(p.Id) ? _lastVolumeUnits[p.Id] : p.VolumeInUnits;

            // If volume decreased, treat as partial close
            if (p.VolumeInUnits < prevUnits - 1e-6)
            {
                var deltaUnits = prevUnits - p.VolumeInUnits;
                var stepUnits = p.Symbol.VolumeInUnitsMin;
                if (stepUnits > 0)
                {
                    var closeLots = deltaUnits / stepUnits;
                    closeLots = Math.Round(closeLots, 2, MidpointRounding.ToZero);

                    if (closeLots > 0)
                    {
                        var seq = NextSeq(p.Id);
                        var eventId = $"{p.Id}-{seq}";

                        var json = $"{{\"type\":\"CLOSE\",\"event_id\":\"{eventId}\",\"master_position_id\":{p.Id},\"close_volume\":{closeLots:F2}}}";
                        AppendLine(json);
                    }
                }
            }

            _lastVolumeUnits[p.Id] = p.VolumeInUnits;
        }

        private void EmitCloseAll(PositionClosedEventArgs e)
        {
            var p = e.Position;
            if (p.SymbolName != MasterSymbol)
                return;

            // On Closed event, remaining volume is 0, so send a "big" close_volume to force full close on MT5
            var seq = NextSeq(p.Id);
            var eventId = $"{p.Id}-{seq}";

            var json = $"{{\"type\":\"CLOSE\",\"event_id\":\"{eventId}\",\"master_position_id\":{p.Id},\"close_volume\":999999}}";
            AppendLine(json);

            _lastVolumeUnits.Remove(p.Id);
            _lastSl.Remove(p.Id);
            _lastTp.Remove(p.Id);
        }

        private void OnPosOpened(PositionOpenedEventArgs e)
        {
            EmitEntry(e.Position);
        }

        private void OnPosModified(PositionModifiedEventArgs e)
        {
            var p = e.Position;
            EmitPartialCloseIfNeeded(p);
            EmitModify(p);
        }

        private void OnPosClosed(PositionClosedEventArgs e)
        {
            EmitCloseAll(e);
        }
    }
}
