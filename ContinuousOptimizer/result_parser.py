"""
PRO_xxx.html 解析モジュール
cBot が出力する PRO レポート HTML から4指標を抽出：
  - 純利益 (NetProfit)
  - 取引件数 (TotalTrades)
  - 最大ドローダウン％ (MaxDDPercent)
  - PF (ProfitFactor)
"""

import json
import re
import os
import glob
from pathlib import Path
from typing import Dict, Any, Optional, List
from datetime import datetime


def parse_pro_html(html_path: str) -> Optional[Dict[str, Any]]:
    """
    PRO_xxx.html から JSON を抽出し、主要指標を返す。

    Returns:
        {
            "html_path": str,
            "NetProfit": float,
            "TotalTrades": int,
            "MaxDDPercent": float,    # MaxBalanceDD / PeakBalance * 100
            "ProfitFactor": float,
            "WinRate": float,
            "Wins": int,
            "Losses": int,
            "InitialBalance": float,
            "EndingBalance": float,
            "ROI": float,
            "MaxBalanceDD": float,
            "PeakBalance": float,
            "EA": str,
            "Symbol": str,
            "PeriodFromJst": str,
            "PeriodToJst": str,
            "parameters": dict,  # パラメーター名→値
        }
    """
    try:
        with open(html_path, "r", encoding="utf-8") as f:
            content = f.read()
    except Exception as e:
        print(f"[WARN] HTML 読み込み失敗: {html_path} -> {e}")
        return None

    # JSON を <script id="pro_report_json"> から抽出
    match = re.search(
        r'<script[^>]*id="pro_report_json"[^>]*>(.*?)</script>',
        content,
        re.DOTALL
    )
    if not match:
        print(f"[WARN] JSON未検出: {html_path}")
        return None

    try:
        data = json.loads(match.group(1).strip())
    except json.JSONDecodeError as e:
        print(f"[WARN] JSON解析失敗: {html_path} -> {e}")
        return None

    # 主要指標抽出
    net_profit = _safe_float(data.get("NetProfit"))
    total_trades = _safe_int(data.get("TotalTrades"))
    pf_raw = data.get("ProfitFactor")
    profit_factor = _safe_float(pf_raw) if not isinstance(pf_raw, str) else 0.0
    win_rate = _safe_float(data.get("WinRate"))
    wins = _safe_int(data.get("Wins"))
    losses = _safe_int(data.get("Losses"))
    initial_balance = _safe_float(data.get("InitialBalance"))
    ending_balance = _safe_float(data.get("EndingBalance"))
    roi = _safe_float(data.get("ROI"))

    # クリティカルポイントからDD取得
    critical = data.get("Critical", {})
    max_balance_dd = _safe_float(critical.get("MaxBalanceDD"))
    peak_balance = _safe_float(critical.get("PeakBalance"))
    dd_peak_balance = _safe_float(critical.get("DdPeakBalance"))

    # 最大DD％ = MaxBalanceDD / DdPeakBalance * 100 (DDが発生した時点のピーク残高基準)
    if dd_peak_balance > 0 and max_balance_dd > 0:
        max_dd_percent = round(max_balance_dd / dd_peak_balance * 100, 2)
    elif initial_balance > 0 and max_balance_dd > 0:
        max_dd_percent = round(max_balance_dd / initial_balance * 100, 2)
    else:
        max_dd_percent = 0.0

    # パラメーター抽出
    params = {}
    for p in data.get("parameters", []):
        prop_name = p.get("property", "")
        value_text = p.get("value", "")
        if prop_name:
            params[prop_name] = value_text

    return {
        "html_path": html_path,
        "NetProfit": net_profit,
        "TotalTrades": total_trades,
        "MaxDDPercent": max_dd_percent,
        "ProfitFactor": profit_factor,
        "WinRate": win_rate,
        "Wins": wins,
        "Losses": losses,
        "InitialBalance": initial_balance,
        "EndingBalance": ending_balance,
        "ROI": roi,
        "MaxBalanceDD": max_balance_dd,
        "PeakBalance": peak_balance,
        "EA": data.get("EA", ""),
        "Symbol": data.get("Symbol", ""),
        "PeriodFromJst": data.get("PeriodFromJst", ""),
        "PeriodToJst": data.get("PeriodToJst", ""),
        "parameters": params,
    }


def scan_pro_reports(base_folder: str, pattern: str = "**/PRO_*.html") -> List[Dict[str, Any]]:
    """
    指定フォルダ以下の全 PRO_xxx.html をスキャンして解析。

    Returns:
        解析結果のリスト（最新順）
    """
    results = []
    search_path = os.path.join(base_folder, pattern)
    html_files = glob.glob(search_path, recursive=True)

    for html_path in html_files:
        parsed = parse_pro_html(html_path)
        if parsed and parsed["TotalTrades"] > 0:
            results.append(parsed)

    # 更新日時の降順でソート
    results.sort(key=lambda r: os.path.getmtime(r["html_path"]), reverse=True)
    return results


def find_new_reports(base_folder: str, after_time: float,
                     pattern: str = "**/PRO_*.html") -> List[str]:
    """
    after_time (unix timestamp) 以降に更新された PRO HTML のパスを返す
    """
    search_path = os.path.join(base_folder, pattern)
    html_files = glob.glob(search_path, recursive=True)
    new_files = []
    for f in html_files:
        if os.path.getmtime(f) > after_time:
            new_files.append(f)
    return sorted(new_files, key=os.path.getmtime)


def _safe_float(val, default=0.0) -> float:
    if val is None:
        return default
    try:
        return float(val)
    except (ValueError, TypeError):
        return default


def _safe_int(val, default=0) -> int:
    if val is None:
        return default
    try:
        return int(val)
    except (ValueError, TypeError):
        return default


# ──────────────────────────────────────────────
# CLI テスト
# ──────────────────────────────────────────────
if __name__ == "__main__":
    import sys

    if len(sys.argv) < 2:
        print("使用法: python result_parser.py <PRO_xxx.html または フォルダパス>")
        sys.exit(1)

    target = sys.argv[1]

    if os.path.isfile(target):
        result = parse_pro_html(target)
        if result:
            print(f"\n=== PRO レポート解析結果 ===")
            print(f"  EA:            {result['EA']}")
            print(f"  期間:          {result['PeriodFromJst']} ~ {result['PeriodToJst']}")
            print(f"  純利益:        {result['NetProfit']:.2f}")
            print(f"  取引件数:      {result['TotalTrades']}")
            print(f"  最大DD(%):     {result['MaxDDPercent']:.2f}%")
            print(f"  PF:            {result['ProfitFactor']:.2f}")
            print(f"  勝率:          {result['WinRate']:.2f}%")
            print(f"  ROI:           {result['ROI']:.2f}%")
            print(f"\n  パラメーター数: {len(result['parameters'])}")
        else:
            print("[ERROR] 解析に失敗しました")
    elif os.path.isdir(target):
        reports = scan_pro_reports(target)
        print(f"\n=== {len(reports)} 件の PRO レポートを検出 ===")
        for i, r in enumerate(reports[:10]):
            print(f"  {i+1}. NetProfit={r['NetProfit']:>10.2f}  "
                  f"Trades={r['TotalTrades']:>4}  "
                  f"MaxDD={r['MaxDDPercent']:>6.2f}%  "
                  f"PF={r['ProfitFactor']:>5.2f}  "
                  f"EA={r['EA']}")
