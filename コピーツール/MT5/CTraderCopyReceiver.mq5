#property strict
#include <Trade/Trade.mqh>
CTrade trade;
// 触る必要のない内部設定（パラメーター欄には表示しない）
const int   連続エラー上限 = 20; // この回数を超えたらコピー処理を停止（EAは削除しない）
int  g_error_count = 0;
bool g_disabled = false;

/*
  COPIER-FILE-MVP-0002 (MT5)

  IMPORTANT (MT5 file access):
  - Standard FileOpen() cannot read arbitrary paths like D:\...
  - This EA uses FILE_COMMON and reads/writes under MT5 "Common Files" folder:
      ...\Terminal\Common\Files\<ベースフォルダ>\...

  Set ベースフォルダ to something like:
      Copier\Log
  Then create:
      <CommonFiles>\Copier\Log\outbox
      <CommonFiles>\Copier\Log\processed
      <CommonFiles>\Copier\Log\state
      <CommonFiles>\Copier\Log\config
*/
input string ベースフォルダ = "Copier\\Log"; // Common\Files 配下のベースパス
input string 受信者ID = "XM_001"; // 口座識別子（state/processed の分離に使用）
input int    監視間隔ミリ秒  = 100; // ログ監視間隔（ms）
input long   マジックナンバー   = 20260213; // ポジション識別（EAのマジック）

// 触る必要のない内部設定（パラメーター欄には表示しない）
const bool   起動時に追いつき読み込み = false; // false: EOF開始（推奨）
const bool   残高比率スケール使用 = true; // 従来の残高比率スケールを使用（base_lot算出に影響）
const double ロット倍率     = 1.0; // base_lot に掛ける倍率
const double 比率下限クランプ     = 0.1; // 残高比率の下限
const double 比率上限クランプ     = 10.0; // 残高比率の上限
input double 一回のトレードリスク固定額 = 0.0;  // 口座通貨の金額。0より大きい場合はこの固定額を優先
input double 一回のトレードリスク割合パーセント = 0.0;  // 口座残高に対する％（例：10.0=10%）。固定額が0のとき使用
input double 最大ロット = 0.0;  // 事故防止の上限ロット（0は制限なしだが非推奨）
input int    最大同時ポジション数 = 1;  // 口座全体の最大同時ポジション数（0は無制限）

// ===== Internal state =====
string g_date;
string g_outboxFile, g_doneFile, g_failFile, g_linkFile, g_cursorFile, g_mapFile;
long   g_lastPos = 0;

string done_ids[];
string master_ids[];
ulong  mt5_tickets[];

// ===== Utilities =====
string Trim(string s){ StringTrimLeft(s); StringTrimRight(s); return s; }

string TodayStr()
{
   MqlDateTime t; TimeToStruct(TimeLocal(), t);
   return StringFormat("%04d%02d%02d", t.year, t.mon, t.day);
}

string PathJoin(string a, string b)
{
   if(StringLen(a)==0) return b;
   if(StringSubstr(a, StringLen(a)-1, 1)=="\\") return a + b;
   return a + "\\" + b;
}

bool IsValidReceiverId(const string &id)
{
   if(StringLen(id)==0) return false;
   if(StringLen(id)>32) return false;
   // Windows filename forbidden chars: \ / : * ? " < > |
   string bad="\\/:*?\"<>|";
   for(int i=0;i<(int)StringLen(id);i++)
   {
      ushort c=(ushort)StringGetCharacter(id,i);
      if(c<=32) return false; // control/space
      if(StringFind(bad, CharToString((uchar)c))>=0) return false;
   }
   return true;
}

string ReceiverPrefix()
{
   // Used in filenames only
   return 受信者ID;
}

// File exists in FILE_COMMON space
bool FileExistsCommon(const string rel)
{
   ResetLastError();
   int h = FileOpen(rel, FILE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON);
   if(h==INVALID_HANDLE) return false;
   FileClose(h);
   return true;
}

// JSON extractor: supports "key":"value" or "key":number
string JsonGetString(const string &line, const string &key)
{
   string pat="\""+key+"\"";
   int i=StringFind(line, pat);
   if(i<0) return "";
   i=StringFind(line, ":", i);
   if(i<0) return "";
   i++;
   while(i<(int)StringLen(line) && (StringGetCharacter(line,i)==' ')) i++;

   if(i<(int)StringLen(line) && StringGetCharacter(line,i)=='\"')
   {
      i++;
      int j=StringFind(line, "\"", i);
      if(j<0) return "";
      return StringSubstr(line, i, j-i);
   }

   int j1=StringFind(line, ",", i);
   int j2=StringFind(line, "}", i);
   int j=-1;
   if(j1<0) j=j2;
   else if(j2<0) j=j1;
   else j=MathMin(j1,j2);
   if(j<0) return "";
   string v=StringSubstr(line, i, j-i);
   return Trim(v);
}
double JsonGetDouble(const string &line, const string &key, double def=0.0)
{
   string s=JsonGetString(line,key);
   if(s=="") return def;
   return (double)StringToDouble(s);
}
long JsonGetLong(const string &line, const string &key, long def=0)
{
   string s=JsonGetString(line,key);
   if(s=="") return def;
   return (long)StringToInteger(s);
}

// ===== Processed IDs =====
bool HasDone(const string &event_id)
{
   for(int i=0;i<ArraySize(done_ids);i++)
      if(done_ids[i]==event_id) return true;
   return false;
}

void AppendDone(const string &event_id)
{
   int n=ArraySize(done_ids);
   ArrayResize(done_ids, n+1);
   done_ids[n]=event_id;

   int h=FileOpen(g_doneFile, FILE_WRITE|FILE_TXT|FILE_ANSI|FILE_COMMON|FILE_READ);
   if(h==INVALID_HANDLE) return;
   FileSeek(h, 0, SEEK_END);
   FileWrite(h, event_id);
   FileClose(h);
}




void CleanupOrphanSLTPObjects()
{
   // Delete Copier_SL_/Copier_TP_ objects whose ticket no longer exists as an open position.
   for(int i=ObjectsTotal(0, 0, -1)-1; i>=0; --i)
   {
      string name = ObjectName(0, i, 0, -1);
      bool isSL = (StringFind(name, "Copier_SL_") == 0);
      bool isTP = (StringFind(name, "Copier_TP_") == 0);
      if(!isSL && !isTP) continue;

      string prefix = isSL ? "Copier_SL_" : "Copier_TP_";
      string sTicket = StringSubstr(name, StringLen(prefix));
      long t = (long)StringToInteger(sTicket);
      if(t <= 0) continue;

      ulong ticket = (ulong)t;
      if(!PositionSelectByTicket(ticket))
      {
         // Remove both lines for this ticket (even if one is missing)
         ObjectDelete(0, "Copier_SL_" + (string)ticket);
         ObjectDelete(0, "Copier_TP_" + (string)ticket);
      }
   }
}


// ===== Handlers =====
void AppendFail(const string &event_id, const string &why, const string &line)
{
   // Best-effort: record failures so they don't block the pipeline
   int h=FileOpen(g_failFile, FILE_WRITE|FILE_TXT|FILE_ANSI|FILE_COMMON);
   if(h==INVALID_HANDLE) return;
   FileSeek(h, 0, SEEK_END);
   FileWrite(h, event_id + " | " + why + " | " + line);
   FileClose(h);
}


long CommonFileSize(const string &path)
{
   int h=FileOpen(path, FILE_READ|FILE_BIN|FILE_COMMON);
   if(h==INVALID_HANDLE) return 0;
   long sz=(long)FileSize(h);
   FileClose(h);
   return sz;
}

bool LoadDone()
{
   ArrayResize(done_ids, 0);
   if(!FileExistsCommon(g_doneFile)) return true;

   int h=FileOpen(g_doneFile, FILE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON);
   if(h==INVALID_HANDLE) return false;

   while(!FileIsEnding(h))
   {
      string line=Trim(FileReadString(h));
      long pos_after_line = (long)FileTell(h);
      if(line=="") { g_lastPos = pos_after_line; continue; }
      int n=ArraySize(done_ids);
      ArrayResize(done_ids, n+1);
      done_ids[n]=line;
   }
   FileClose(h);
   return true;
}


bool LoadCursor()
{
   g_lastPos = 0;

   // If no cursor exists for this receiver/day, decide whether to catch up or start from EOF.
   if(!FileExistsCommon(g_cursorFile))
   {
      if(!起動時に追いつき読み込み)
      {
         if(FileExistsCommon(g_outboxFile))
            g_lastPos = CommonFileSize(g_outboxFile); // start from end (skip history)
      }
      return true;
   }

   int h=FileOpen(g_cursorFile, FILE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON);
   if(h==INVALID_HANDLE) return false;

   string line=Trim(FileReadString(h));
   FileClose(h);
   if(line=="")
   {
      if(!起動時に追いつき読み込み && FileExistsCommon(g_outboxFile))
         g_lastPos = CommonFileSize(g_outboxFile);
      return true;
   }

   int comma=StringFind(line, ",");
   if(comma<0)
   {
      if(!起動時に追いつき読み込み && FileExistsCommon(g_outboxFile))
         g_lastPos = CommonFileSize(g_outboxFile);
      return true;
   }

   string d=Trim(StringSubstr(line,0,comma));
   string p=Trim(StringSubstr(line,comma+1));
   if(d==g_date)
   {
      g_lastPos = (long)StringToInteger(p);
   }
   else
   {
      // Cursor is from another day; optionally start from EOF of today's outbox.
      if(!起動時に追いつき読み込み && FileExistsCommon(g_outboxFile))
         g_lastPos = CommonFileSize(g_outboxFile);
   }
   return true;
}


void SaveCursor()
{
   int h=FileOpen(g_cursorFile, FILE_WRITE|FILE_TXT|FILE_ANSI|FILE_COMMON);
   if(h==INVALID_HANDLE) return;
   FileWrite(h, g_date + "," + (string)g_lastPos);
   FileClose(h);
}

// ===== Link map =====
int FindLinkIndex(const string &master_id)
{
   for(int i=0;i<ArraySize(master_ids);i++)
      if(master_ids[i]==master_id) return i;
   return -1;
}

void SaveLinks()
{
   int h=FileOpen(g_linkFile, FILE_WRITE|FILE_TXT|FILE_ANSI|FILE_COMMON);
   if(h==INVALID_HANDLE) return;

   FileWrite(h, "master_position_id,mt5_ticket");
   for(int i=0;i<ArraySize(master_ids);i++)
      FileWrite(h, master_ids[i] + "," + (string)mt5_tickets[i]);
   FileClose(h);
}

void UpsertLink(const string &master_id, const ulong ticket)
{
   int idx=FindLinkIndex(master_id);
   if(idx<0)
   {
      int n=ArraySize(master_ids);
      ArrayResize(master_ids, n+1);
      ArrayResize(mt5_tickets, n+1);
      master_ids[n]=master_id;
      mt5_tickets[n]=ticket;
   }
   else
   {
      mt5_tickets[idx]=ticket;
   }
   SaveLinks();
}


void RemoveLinkIndex(const int idx)
{
   int n=ArraySize(master_ids);
   if(idx<0 || idx>=n) return;
   for(int i=idx;i<n-1;i++)
   {
      master_ids[i]=master_ids[i+1];
      mt5_tickets[i]=mt5_tickets[i+1];
   }
   ArrayResize(master_ids, n-1);
   ArrayResize(mt5_tickets, n-1);
   SaveLinks();
}

bool LoadLinks()
{
   ArrayResize(master_ids, 0);
   ArrayResize(mt5_tickets, 0);

   if(!FileExistsCommon(g_linkFile)) return true;

   int h=FileOpen(g_linkFile, FILE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON);
   if(h==INVALID_HANDLE) return false;

   bool first=true;
   while(!FileIsEnding(h))
   {
      string line=Trim(FileReadString(h));
      long pos_after_line = (long)FileTell(h);
      if(line=="") { g_lastPos = pos_after_line; continue; }
      if(first){ first=false; continue; }

      int comma=StringFind(line, ",");
      if(comma<0) continue;

      string mid=Trim(StringSubstr(line, 0, comma));
      string tk =Trim(StringSubstr(line, comma+1));

      int n=ArraySize(master_ids);
      ArrayResize(master_ids, n+1);
      ArrayResize(mt5_tickets, n+1);
      master_ids[n]=mid;
      mt5_tickets[n]=(ulong)StringToInteger(tk);
   }
   FileClose(h);
   return true;
}

// ===== Symbol map (minimal one-line json) =====
string MapSymbol(const string &src)
{
   if(!FileExistsCommon(g_mapFile))
   {
      if(src=="XAUUSD") return "GOLD";
      return src;
   }

   int h=FileOpen(g_mapFile, FILE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON);
   if(h==INVALID_HANDLE)
   {
      if(src=="XAUUSD") return "GOLD";
      return src;
   }
   string s=FileReadString(h);
   FileClose(h);

   string key="\""+src+"\"";
   int i=StringFind(s, key);
   if(i<0)
   {
      if(src=="XAUUSD") return "GOLD";
      return src;
   }
   i=StringFind(s, ":", i);
   if(i<0) return src;
   i++;
   while(i<(int)StringLen(s) && (StringGetCharacter(s,i)==' ')) i++;
   if(i<(int)StringLen(s) && StringGetCharacter(s,i)=='\"')
   {
      i++;
      int j=StringFind(s, "\"", i);
      if(j<0) return src;
      return StringSubstr(s, i, j-i);
   }
   return src;
}

// ===== Lot scaling =====
double NormalizeVolumeBySymbol(const string sym, double vol)
{
   double vmin=SymbolInfoDouble(sym, SYMBOL_VOLUME_MIN);
   double vmax=SymbolInfoDouble(sym, SYMBOL_VOLUME_MAX);
   double step=SymbolInfoDouble(sym, SYMBOL_VOLUME_STEP);

   if(vmin<=0 || vmax<=0 || step<=0) return vol;

   vol = MathMax(vmin, MathMin(vmax, vol));

   double k=MathFloor((vol - vmin) / step + 1e-12);
   double out=vmin + k*step;
   out = MathMax(vmin, MathMin(vmax, out));
   return out;
}

double CalcScaledLotByBalance(const string sym, double masterLot, double masterBalance)
{
   if(!残高比率スケール使用 || masterBalance<=0.0)
      return NormalizeVolumeBySymbol(sym, masterLot);

   double myBal=AccountInfoDouble(ACCOUNT_BALANCE);
   if(myBal<=0.0)
      return NormalizeVolumeBySymbol(sym, masterLot);

   double ratio=myBal/masterBalance;
   ratio=MathMax(比率下限クランプ, MathMin(比率上限クランプ, ratio));

   double lot=masterLot*ratio*ロット倍率;
   return NormalizeVolumeBySymbol(sym, lot);
}


// ===== ローカル資金管理（SLからロットを逆算） =====
double CalcLotByRiskFromSL(const string sym, const string side, const double sl_price)
{
   // リスク金額（口座残高基準）
   double bal = AccountInfoDouble(ACCOUNT_BALANCE);
   if(bal <= 0.0) return 0.0;

   // リスク％の安全クランプ（入力10.0は10%の意味）
   double rp = 一回のトレードリスク割合パーセント;
   if(rp < 0.0) rp = 0.0;
   if(rp > 100.0) rp = 100.0;

   double risk_money = 0.0;
   if(一回のトレードリスク固定額 > 0.0)
      risk_money = 一回のトレードリスク固定額;
   else if(一回のトレードリスク割合パーセント > 0.0)
      risk_money = bal * (rp / 100.0); // 10.0 => 10%
   else
      return 0.0; // 未設定（この場合は外側で「制御なし」として扱う）

   // 成り行き前提：現在価格からSL距離を算出
   double bid=0.0, ask=0.0;
   MqlTick tick;
   if(SymbolInfoTick(sym, tick))
   {
      bid = tick.bid;
      ask = tick.ask;
   }
   else
   {
      bid = SymbolInfoDouble(sym, SYMBOL_BID);
      ask = SymbolInfoDouble(sym, SYMBOL_ASK);
   }
   if(bid<=0.0 || ask<=0.0) return 0.0;

   double entry_price = (side=="BUY") ? ask : bid;
   double dist = MathAbs(entry_price - sl_price);
   if(dist <= 0.0) return 0.0;

   // 1ロットでSL到達した場合の損失額（口座通貨）
   // tick_value/tick_size 方式は銘柄/口座通貨で誤差や破綻が出やすいため、
   // MT5標準の損益計算（OrderCalcProfit）で厳密に求める。
   double profit_1lot = 0.0;
   ENUM_ORDER_TYPE ot = (side=="BUY") ? ORDER_TYPE_BUY : ORDER_TYPE_SELL;
   if(!OrderCalcProfit(ot, sym, 1.0, entry_price, sl_price, profit_1lot))
      return 0.0;

   double loss_per_1lot = MathAbs(profit_1lot);
   if(loss_per_1lot <= 0.0) return 0.0;

   double lot = risk_money / loss_per_1lot;
   return lot;
}

// ===== Error policy: do NOT remove EA (log + disable after threshold) =====
void Fail(const string why, const string line)
{
   g_error_count++;
   Print("COPIER ERROR(", g_error_count, "): ", why, " / line=", line);
   Print("   LastError=", GetLastError(),
         " Retcode=", trade.ResultRetcode(),
         " Desc=", trade.ResultRetcodeDescription());
   if(g_error_count >= 連続エラー上限)
   {
      g_disabled = true;
      Print("COPIER DISABLED after too many errors. EA will remain attached but will not execute trades.");
   }
}


// ===== SL/TP application (ticket-based; hedging safe) =====
double NormalizePrice(const string sym, double price)
{
   int digits=(int)SymbolInfoInteger(sym, SYMBOL_DIGITS);
   return NormalizeDouble(price, digits);
}

bool ApplySLTPByTicket(const ulong ticket, double sl, double tp)
{
   if(sl<=0.0 && tp<=0.0) return true;

   if(!PositionSelectByTicket(ticket))
   {
      // Already closed (or not found). Remove chart lines and unlink so we don't leave stale objects.
      RemoveSLTPForTicket(ticket);
      return true;
   }
   string sym = PositionGetString(POSITION_SYMBOL);

   long type = (long)PositionGetInteger(POSITION_TYPE);
   double point = SymbolInfoDouble(sym, SYMBOL_POINT);
   int stops_level = (int)SymbolInfoInteger(sym, SYMBOL_TRADE_STOPS_LEVEL);
   double min_dist = (double)stops_level * point;

   double bid=0, ask=0;
   SymbolInfoDouble(sym, SYMBOL_BID, bid);
   SymbolInfoDouble(sym, SYMBOL_ASK, ask);

   // Direction sanity + StopLevel distance correction.
   if(type == POSITION_TYPE_BUY)
   {
      if(sl>0.0)
      {
         // SL must be below bid
         if(sl >= bid) sl = 0.0;
         else if(min_dist>0.0 && (bid - sl) < min_dist) sl = bid - min_dist;
      }
      if(tp>0.0)
      {
         // TP must be above ask
         if(tp <= ask) tp = 0.0;
         else if(min_dist>0.0 && (tp - ask) < min_dist) tp = ask + min_dist;
      }
   }
   else if(type == POSITION_TYPE_SELL)
   {
      if(sl>0.0)
      {
         // SL must be above ask
         if(sl <= ask) sl = 0.0;
         else if(min_dist>0.0 && (sl - ask) < min_dist) sl = ask + min_dist;
      }
      if(tp>0.0)
      {
         // TP must be below bid
         if(tp >= bid) tp = 0.0;
         else if(min_dist>0.0 && (bid - tp) < min_dist) tp = bid - min_dist;
      }
   }

   if(sl>0.0) sl = NormalizePrice(sym, sl);
   if(tp>0.0) tp = NormalizePrice(sym, tp);

   MqlTradeRequest req;
   MqlTradeResult  res;
   ZeroMemory(req);
   ZeroMemory(res);

   req.action  = TRADE_ACTION_SLTP;
   req.position= ticket;
   req.symbol  = sym;
   req.sl      = sl;
   req.tp      = tp;
   req.magic   = マジックナンバー;

   bool ok = OrderSend(req, res);
   if(!ok || (res.retcode!=TRADE_RETCODE_DONE && res.retcode!=TRADE_RETCODE_DONE_PARTIAL))
   {
      Print("ApplySLTPByTicket failed. ticket=", ticket, " sym=", sym, " sl=", sl, " tp=", tp,
            " retcode=", res.retcode, " comment=", res.comment);
      return false;
   }
   return true;
}

// ===== SL/TP visualizer (chart objects) =====
string LevelNameSL(ulong ticket){ return "Copier_SL_" + (string)ticket; }
string LevelNameTP(ulong ticket){ return "Copier_TP_" + (string)ticket; }

void UpsertHLine(const string name, double price, const string text)
{
   if(price <= 0.0)
   {
      if(ObjectFind(0, name) >= 0) ObjectDelete(0, name);
      return;
   }

   if(ObjectFind(0, name) < 0)
   {
      ObjectCreate(0, name, OBJ_HLINE, 0, 0, price);
      ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
      ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
   }
   ObjectSetDouble(0, name, OBJPROP_PRICE, price);
   ObjectSetString(0, name, OBJPROP_TEXT, text);
}

void DrawSLTPForTicket(ulong ticket, const string symbol, double sl, double tp)
{
   int digits=(int)SymbolInfoInteger(symbol, SYMBOL_DIGITS);
   UpsertHLine(LevelNameSL(ticket), sl, symbol + " SL " + DoubleToString(sl, digits));
   UpsertHLine(LevelNameTP(ticket), tp, symbol + " TP " + DoubleToString(tp, digits));
}

void RemoveSLTPForTicket(ulong ticket)
{
   string nsl=LevelNameSL(ticket);
   string ntp=LevelNameTP(ticket);
   if(ObjectFind(0, nsl) >= 0) ObjectDelete(0, nsl);
   if(ObjectFind(0, ntp) >= 0) ObjectDelete(0, ntp);
}

void RemoveAllSLTPObjects()
{
   for(int i=ObjectsTotal(0, 0, -1)-1; i>=0; --i)
   {
      string name=ObjectName(0, i, 0, -1);
      if(StringFind(name, "Copier_SL_") == 0 || StringFind(name, "Copier_TP_") == 0)
         ObjectDelete(0, name);
   }
}


// ===== Handlers =====
bool HandleEntry(const string &line)
{
   string event_id=JsonGetString(line, "event_id");
   long mid=JsonGetLong(line, "master_position_id", 0);
   string master=(string)mid;

   string sym_src=JsonGetString(line, "symbol");
   string side=JsonGetString(line, "side"); StringToUpper(side);

   double vol_master=JsonGetDouble(line, "volume", 0.0);
   double sl=JsonGetDouble(line, "sl", 0.0);
   double tp=JsonGetDouble(line, "tp", 0.0);
   double master_bal=JsonGetDouble(line, "master_balance", 0.0);

   if(event_id=="" || mid<=0 || vol_master<=0.0) return false;

   string sym=MapSymbol(sym_src);
   if(!SymbolSelect(sym, true)) return false;


   // 安全装置：最大同時ポジション数（口座全体）
   if(最大同時ポジション数>0 && PositionsTotal()>=最大同時ポジション数)
   {
      PrintFormat("[BLOCK_MAX_POS] event_id=%s symbol=%s positions=%d limit=%d",
                  event_id, sym, PositionsTotal(), 最大同時ポジション数);
      return true; // failにせずイベントは消化する
   }

   double base_vol=CalcScaledLotByBalance(sym, vol_master, master_bal);
   if(base_vol<=0.0) return false;

   // ローカル資金管理：SLからロットを自動計算し、min方式で安全側を採用
   double vol = base_vol;
   double risk_vol = CalcLotByRiskFromSL(sym, side, sl);
   if(risk_vol > 0.0) vol = MathMin(base_vol, risk_vol);

   // 事故防止：最大ロット上限
   if(最大ロット > 0.0) vol = MathMin(vol, 最大ロット);

   vol = NormalizeVolumeBySymbol(sym, vol);
   if(vol<=0.0) return false;

   trade.SetExpertMagicNumber(マジックナンバー);
   trade.SetDeviationInPoints(50);
   string comment="ct_master="+master;

   bool ok=false;
   if(side=="BUY") ok=trade.Buy(vol, sym, 0.0, 0.0, 0.0, comment);
   else if(side=="SELL") ok=trade.Sell(vol, sym, 0.0, 0.0, 0.0, comment);
   else return false;

   if(!ok) return false;

   // Link
   ulong linked=0;
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      ulong t=PositionGetTicket(i);
      if(t==0) continue;
      if(!PositionSelectByTicket(t)) continue;

      if(PositionGetString(POSITION_SYMBOL)!=sym) continue;
      if((long)PositionGetInteger(POSITION_MAGIC)!=マジックナンバー) continue;

      string pcom=PositionGetString(POSITION_COMMENT);
      if(StringFind(pcom, master)>=0) { linked=t; break; }
   }
   if(linked==0) return false;

   UpsertLink(master, linked);

   // Apply SL/TP after fill (prevents invalid stops on entry)
   bool sltp_ok = ApplySLTPByTicket(linked, sl, tp);
   if(sltp_ok) DrawSLTPForTicket(linked, sym, sl, tp);
   else        Print("SLTP not applied (will keep position without SL/TP). ticket=", linked);

   // reset consecutive error counter on success
   return true;
}

bool HandleModify(const string &line)
{
   long mid=JsonGetLong(line, "master_position_id", 0);
   if(mid<=0) return false;
   string master=(string)mid;

   int idx=FindLinkIndex(master);
   if(idx<0) return false;

   ulong ticket=mt5_tickets[idx];
   double sl=JsonGetDouble(line, "sl", 0.0);
   double tp=JsonGetDouble(line, "tp", 0.0);

   if(!PositionSelectByTicket(ticket)) return false;

   trade.SetExpertMagicNumber(マジックナンバー);
   string sym=PositionGetString(POSITION_SYMBOL);
   bool ok=ApplySLTPByTicket(ticket, sl, tp);
   if(ok) { DrawSLTPForTicket(ticket, sym, sl, tp); g_error_count = 0; }
   return ok;
}

bool HandleClose(const string &line)
{
   long mid=JsonGetLong(line, "master_position_id", 0);
   if(mid<=0) return false;
   string master=(string)mid;

   int idx=FindLinkIndex(master);
   if(idx<0) return false;

   ulong ticket=mt5_tickets[idx];
   double close_vol=JsonGetDouble(line, "close_volume", 0.0);
   if(close_vol<=0.0) return false;

   if(!PositionSelectByTicket(ticket)) return false;

   trade.SetExpertMagicNumber(マジックナンバー);

   double cur=PositionGetDouble(POSITION_VOLUME);

   if(close_vol >= cur - 1e-10)
   {
      bool ok=trade.PositionClose(ticket);
      if(ok) { RemoveSLTPForTicket(ticket); RemoveLinkIndex(idx); g_error_count = 0; }
      return ok;
   }
   {
      bool ok=trade.PositionClosePartial(ticket, close_vol);
      // Keep lines for partial close
      return ok;
   }
}

// ===== Outbox processing =====
void RefreshPathsForToday()
{
   g_date=TodayStr();
   g_outboxFile = PathJoin(PathJoin(ベースフォルダ, "outbox"), "ctrader_to_mt5_" + g_date + ".log");
   string pref = ReceiverPrefix();
   g_doneFile   = PathJoin(PathJoin(ベースフォルダ, "processed"), pref + "_done_" + g_date + ".log");
   g_failFile   = PathJoin(PathJoin(ベースフォルダ, "processed"), pref + "_fail_" + g_date + ".log");
   g_linkFile   = PathJoin(PathJoin(ベースフォルダ, "state"), pref + "_link_map.csv");
   g_cursorFile = PathJoin(PathJoin(ベースフォルダ, "state"), pref + "_cursor.dat");

   // symbol_map: allow receiver-specific override; fallback to default
   string specific = PathJoin(PathJoin(ベースフォルダ, "config"), "symbol_map_" + pref + ".json");
   if(FileExistsCommon(specific)) g_mapFile = specific;
   else g_mapFile = PathJoin(PathJoin(ベースフォルダ, "config"), "symbol_map.json");

   LoadDone();
   LoadLinks();
   LoadCursor();
}

void ProcessOutbox()
{
   string now=TodayStr();
   if(now!=g_date) RefreshPathsForToday();

   if(!FileExistsCommon(g_outboxFile)) return;

   int h=FileOpen(g_outboxFile, FILE_READ|FILE_TXT|FILE_ANSI|FILE_COMMON);
   if(h==INVALID_HANDLE) return;

   FileSeek(h, g_lastPos, SEEK_SET);

   while(!FileIsEnding(h))
   {
      string line=Trim(FileReadString(h));
      long pos_after_line = (long)FileTell(h);
      if(line=="") { g_lastPos = pos_after_line; continue; }

      string event_id=JsonGetString(line, "event_id");
      if(event_id=="" || HasDone(event_id)) continue;

      string type=JsonGetString(line, "type");
      bool ok=false;

      if(type=="ENTRY") ok=HandleEntry(line);
      else if(type=="MODIFY") ok=HandleModify(line);
      else if(type=="CLOSE") ok=HandleClose(line);

      if(!ok)
      {
         // Advance cursor so a single bad event does not block the whole queue
         g_lastPos = pos_after_line;
         AppendFail(event_id, "EXECUTION_FAILED", line);
         // Mark as done to prevent infinite reprocessing loops
         AppendDone(event_id);
         Fail("EXECUTION_FAILED", line);
         continue;
      }

      AppendDone(event_id);
   }

   g_lastPos=(long)FileTell(h);
   FileClose(h);
   SaveCursor();
}

int OnInit()
{
   g_date="";
   if(!IsValidReceiverId(受信者ID))
   {
      Print("Invalid 受信者ID. Must be 1-32 chars and not contain \\ / : * ? \" < > | or spaces.");
      return INIT_FAILED;
   }
   trade.SetExpertMagicNumber(マジックナンバー);
   RefreshPathsForToday();

   // Show MT5 native trade levels (optional, but helpful)
   ChartSetInteger(0, CHART_SHOW_TRADE_LEVELS, true);

   EventSetMillisecondTimer(監視間隔ミリ秒);
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   Print("OnDeinit reason=", reason);
   EventKillTimer();
   RemoveAllSLTPObjects();
}

void OnTimer(){
   if(g_disabled) return;
   ProcessOutbox();
   CleanupOrphanSLTPObjects();
 }