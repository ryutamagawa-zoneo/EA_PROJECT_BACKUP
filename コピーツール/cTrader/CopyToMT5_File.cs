using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using cAlgo.API;

namespace cAlgo.Robots
{
    /*
      COPIER-FILE-MVP-0002 (cTrader)

      IMPORTANT:
      - MT5 EA reads from MT5 "Common Files" (FILE_COMMON).
      - For the file-copier to work, set BaseDir to the same Common Files folder path used by MT5:
          ...\Terminal\Common\Files\Copier\Log
        (You can locate it from MT5: File -> Open Data Folder, then go up to "Terminal" and open "Common\Files")

      Output:
        <BaseDir>\outbox\ctrader_to_mt5_YYYYMMDD.log

      This cBot only emits:
        ENTRY (with master_balance) and MODIFY (SL/TP) and CLOSE (full close)
    */

    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class CopyToMT5_File : Robot
    {
        [Parameter("BaseDir", DefaultValue = @"C:\Users\Public\Documents\MetaQuotes\Terminal\Common\Files\Copier\Log")]
        public string BaseDir { get; set; }

        [Parameter("Master Symbol", DefaultValue = "XAUUSD")]
        public string MasterSymbol { get; set; }

        private string OutboxDir => Path.Combine(BaseDir, "outbox");
        private readonly Dictionary<long, int> _seq = new Dictionary<long, int>();
        private readonly Dictionary<long, double> _lastSl = new Dictionary<long, double>();
        private readonly Dictionary<long, double> _lastTp = new Dictionary<long, double>();

        protected override void OnStart()
        {
            Directory.CreateDirectory(OutboxDir);

            Positions.Opened += e => EmitEntry(e.Position);
            Positions.Modified += e => EmitModifySlTp(e.Position);
            Positions.Closed += e => EmitClose(e.Position);
        }

        private string TodayStr()
        {
            var t = Server.Time;
            return $"{t:yyyyMMdd}";
        }

        private string OutboxFilePath()
        {
            return Path.Combine(OutboxDir, $"ctrader_to_mt5_{TodayStr()}.log");
        }

        private int NextSeq(long positionId)
        {
            if (!_seq.ContainsKey(positionId)) _seq[positionId] = 0;
            _seq[positionId]++;
            return _seq[positionId];
        }

        private static string Num(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);

        private void AppendLine(string line)
        {
            var path = OutboxFilePath();
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var sw = new StreamWriter(fs))
            {
                sw.WriteLine(line);
                sw.Flush();
                fs.Flush(true);
            }
        }

        private static string Side(Position p) => p.TradeType == TradeType.Buy ? "BUY" : "SELL";

        private double ToLotsApprox(Position p)
        {
            // MVP: stable "lot-like" using step count for the symbol.
            var stepUnits = p.Symbol.VolumeInUnitsMin;
            if (stepUnits <= 0) return 0;
            var lots = p.VolumeInUnits / stepUnits;
            return Math.Round(lots, 2, MidpointRounding.ToZero);
        }

        private void EmitEntry(Position p)
        {
            if (p.SymbolName != MasterSymbol) return;

            var seq = NextSeq(p.Id);
            var eventId = $"{p.Id}-{seq}";

            var sl = p.StopLoss ?? 0.0;
            var tp = p.TakeProfit ?? 0.0;

            var lots = ToLotsApprox(p);
            if (lots <= 0) return;

            var masterBalance = Account.Balance;

            var json =
                "{\"type\":\"ENTRY\"," +
                $"\"event_id\":\"{eventId}\"," +
                $"\"master_position_id\":{p.Id}," +
                $"\"symbol\":\"{p.SymbolName}\"," +
                $"\"side\":\"{Side(p)}\"," +
                $"\"volume\":{Num(lots)}," +
                $"\"sl\":{Num(sl)}," +
                $"\"tp\":{Num(tp)}," +
                $"\"master_balance\":{Num(masterBalance)}" +
                "}";

            AppendLine(json);

            _lastSl[p.Id] = sl;
            _lastTp[p.Id] = tp;
        }

        private void EmitModifySlTp(Position p)
        {
            if (p.SymbolName != MasterSymbol) return;

            var sl = p.StopLoss ?? 0.0;
            var tp = p.TakeProfit ?? 0.0;

            var prevSl = _lastSl.TryGetValue(p.Id, out var a) ? a : 0.0;
            var prevTp = _lastTp.TryGetValue(p.Id, out var b) ? b : 0.0;

            if (Math.Abs(sl - prevSl) < 1e-9 && Math.Abs(tp - prevTp) < 1e-9) return;

            var seq = NextSeq(p.Id);
            var eventId = $"{p.Id}-{seq}";

            var json =
                "{\"type\":\"MODIFY\"," +
                $"\"event_id\":\"{eventId}\"," +
                $"\"master_position_id\":{p.Id}," +
                $"\"sl\":{Num(sl)}," +
                $"\"tp\":{Num(tp)}" +
                "}";

            AppendLine(json);

            _lastSl[p.Id] = sl;
            _lastTp[p.Id] = tp;
        }

        private void EmitClose(Position p)
        {
            if (p.SymbolName != MasterSymbol) return;

            var seq = NextSeq(p.Id);
            var eventId = $"{p.Id}-{seq}";

            // Full close request on MT5
            var json =
                "{\"type\":\"CLOSE\"," +
                $"\"event_id\":\"{eventId}\"," +
                $"\"master_position_id\":{p.Id}," +
                $"\"close_volume\":999999" +
                "}";

            AppendLine(json);

            _lastSl.Remove(p.Id);
            _lastTp.Remove(p.Id);
        }
    }
}
