"""
cTrader 標準バックテストレポート解析モジュール
パラメーター欄保存庫\BASE\1〜N の構造を解析

各フォルダ構成:
  report.html       - cTrader標準レポート（JSON埋め込み）
  events.json       - トレードイベント一覧
  parameters.cbotset - このバックテストで使用したパラメーター
  log.txt           - ログ

抽出指標:
  純利益 (NetProfit)
  取引件数 (TotalTrades) ← events.json のクローズ済みイベント数
  最大有効証拠金ドローダウン% (MaxEquityDrawdownPercent) ← report.html
  PF (ProfitFactor) ← report.html
"""

import json
import os
import re
import glob
from typing import Dict, Any, Optional, List


def parse_ctrader_report_folder(folder_path: str) -> Optional[Dict[str, Any]]:
    """
    cTrader標準バックテストフォルダを解析して指標を返す

    Args:
        folder_path: 例 D:\\...\\パラメーター欄保存庫\\BASE\\1

    Returns:
        {
            "source": "ctrader_standard",
            "folder_path": str,
            "NetProfit": float,
            "TotalTrades": int,
            "MaxDDPercent": float,     # 最大有効証拠金DD%
            "MaxBalanceDDPercent": float,
            "ProfitFactor": float,
            "WinRate": float,
            "parameters": dict,        # cbotset から取得
            ...
        }
    """
    report_path = os.path.join(folder_path, "report.html")
    events_path = os.path.join(folder_path, "events.json")
    params_path = os.path.join(folder_path, "parameters.cbotset")

    if not os.path.isfile(report_path):
        return None

    # ── report.html からメトリクス取得 ──
    try:
        with open(report_path, "r", encoding="utf-8") as f:
            html = f.read()
    except Exception:
        return None

    net_profit = _extract_float(html, r'"netProfit":\s*([\-0-9.]+)')
    max_eq_dd_pct = _extract_float(html, r'maxEquityDrawdownPercent":\s*([\-0-9.]+)')
    max_bal_dd_pct = _extract_float(html, r'[Dd]rawdownPercent":\s*([\-0-9.]+)')
    pf_raw = _extract_float(html, r'"profitFactor"[^}]*?"all":\s*([\-0-9.]+)', dotall=True)
    starting_balance = _extract_float(html, r'"startingBalance":\s*([\-0-9.]+)')
    ending_balance = _extract_float(html, r'[Ee]ndingBalance":\s*([\-0-9.]+)')
    ending_equity = _extract_float(html, r'[Ee]ndingEquity":\s*([\-0-9.]+)')

    # %表示に変換（cTraderは0〜1の小数で持つことが多い）
    if 0 < max_eq_dd_pct <= 1.0:
        max_eq_dd_pct = round(max_eq_dd_pct * 100, 4)
    if 0 < max_bal_dd_pct <= 1.0:
        max_bal_dd_pct = round(max_bal_dd_pct * 100, 4)

    # ── events.json から取引件数取得 ──
    total_trades = 0
    wins = 0
    losses = 0
    gross_profit = 0.0
    gross_loss = 0.0

    if os.path.isfile(events_path):
        try:
            with open(events_path, "r", encoding="utf-8") as f:
                events = json.load(f)

            closed = [e for e in events if e.get("closePrice") is not None]
            total_trades = len(closed)

            for e in closed:
                gp = e.get("grossProfit", 0) or 0
                if gp >= 0:
                    wins += 1
                    gross_profit += gp
                else:
                    losses += 1
                    gross_loss += abs(gp)

        except Exception:
            pass

    win_rate = round(wins / total_trades * 100, 2) if total_trades > 0 else 0.0
    if gross_loss > 0:
        pf_from_events = round(gross_profit / gross_loss, 4)
    else:
        pf_from_events = 0.0

    # PFは report.html 優先、なければ events.json から計算
    profit_factor = pf_raw if pf_raw > 0 else pf_from_events

    # ── parameters.cbotset からパラメーター取得 ──
    params = {}
    if os.path.isfile(params_path):
        try:
            with open(params_path, "r", encoding="utf-8") as f:
                cbotset = json.load(f)
            params = cbotset.get("Parameters", {})
        except Exception:
            pass

    roi = 0.0
    if starting_balance > 0:
        roi = round((ending_balance - starting_balance) / starting_balance * 100, 4)

    folder_name = os.path.basename(folder_path)

    return {
        "source": "ctrader_standard",
        "folder_path": folder_path,
        "html_path": report_path,
        "NetProfit": round(net_profit, 2),
        "TotalTrades": total_trades,
        "MaxDDPercent": max_eq_dd_pct,           # 最大有効証拠金DD%（ユーザー要望値）
        "MaxBalanceDDPercent": max_bal_dd_pct,
        "ProfitFactor": round(profit_factor, 4),
        "WinRate": win_rate,
        "Wins": wins,
        "Losses": losses,
        "GrossProfit": round(gross_profit, 2),
        "GrossLoss": round(gross_loss, 2),
        "StartingBalance": starting_balance,
        "EndingBalance": ending_balance,
        "EndingEquity": ending_equity,
        "ROI": roi,
        "FolderName": folder_name,
        "parameters": params,
    }


def scan_ctrader_standard_folders(base_dir: str,
                                   min_trades: int = 1) -> List[Dict[str, Any]]:
    """
    BASE/1〜N のような番号付きフォルダを再帰的にスキャン

    Args:
        base_dir: スキャン対象ディレクトリ（例: パラメーター欄保存庫\BASE）
        min_trades: 最低取引件数（これ未満はスキップ）

    Returns:
        解析結果のリスト
    """
    results = []

    # report.html を持つすべてのフォルダを検索
    pattern = os.path.join(base_dir, "**/report.html")
    report_files = glob.glob(pattern, recursive=True)

    for report_path in report_files:
        folder = os.path.dirname(report_path)
        result = parse_ctrader_report_folder(folder)
        if result and result["TotalTrades"] >= min_trades:
            results.append(result)

    # NetProfit 降順でソート
    results.sort(key=lambda r: r["NetProfit"], reverse=True)
    return results


def _extract_float(text: str, pattern: str, default: float = 0.0,
                   dotall: bool = False) -> float:
    flags = re.DOTALL if dotall else 0
    m = re.search(pattern, text, flags)
    if m:
        try:
            return float(m.group(1))
        except ValueError:
            pass
    return default


# ──────────────────────────────────────────────
# CLI テスト
# ──────────────────────────────────────────────
if __name__ == "__main__":
    import sys

    if len(sys.argv) < 2:
        print("使用法:")
        print("  python ctrader_report_parser.py <フォルダパス>")
        print("  例: python ctrader_report_parser.py \"D:\\...\\BASE\\1\"")
        print("  例: python ctrader_report_parser.py \"D:\\...\\BASE\"  (一括スキャン)")
        sys.exit(1)

    target = sys.argv[1]

    if os.path.isfile(os.path.join(target, "report.html")):
        # 単一フォルダ
        result = parse_ctrader_report_folder(target)
        if result:
            print(f"\n=== cTrader 標準レポート解析 ===")
            print(f"  フォルダ:           {target}")
            print(f"  純利益:             {result['NetProfit']:>12,.2f}")
            print(f"  取引件数:           {result['TotalTrades']:>6}")
            print(f"  最大有効証拠金DD%:  {result['MaxDDPercent']:>8.4f}%")
            print(f"  PF:                 {result['ProfitFactor']:>8.4f}")
            print(f"  勝率:               {result['WinRate']:>8.2f}%")
            print(f"  ROI:                {result['ROI']:>8.4f}%")
            print(f"\n  パラメーター数: {len(result['parameters'])}")
        else:
            print("[ERROR] 解析失敗")
    else:
        # フォルダ一括スキャン
        results = scan_ctrader_standard_folders(target)
        print(f"\n=== {len(results)} 件のバックテスト結果 (取引あり) ===")
        print(f"{'#':>4}  {'NetProfit':>12}  {'Trades':>7}  {'MaxEqDD%':>9}  {'PF':>7}  {'WinRate':>8}")
        print("-" * 65)
        for i, r in enumerate(results[:20]):
            print(f"{i+1:>4}  "
                  f"{r['NetProfit']:>12,.2f}  "
                  f"{r['TotalTrades']:>7}  "
                  f"{r['MaxDDPercent']:>8.2f}%  "
                  f"{r['ProfitFactor']:>7.2f}  "
                  f"{r['WinRate']:>7.2f}%")
