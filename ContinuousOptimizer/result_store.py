"""
結果保存・ランキング表示モジュール
CSV保存 + コンソールランキング + Top N レポート
"""

import csv
import os
from datetime import datetime
from typing import Dict, Any, List, Optional


# ──────────────────────────────────────────────
# CSV 保存
# ──────────────────────────────────────────────

# ユーザーが見たい4指標 + 基本情報
METRIC_COLUMNS = [
    "NetProfit",
    "TotalTrades",
    "MaxDDPercent",
    "ProfitFactor",
]

INFO_COLUMNS = [
    "WinRate",
    "ROI",
    "InitialBalance",
    "EndingBalance",
    "MaxBalanceDD",
    "EA",
    "Symbol",
    "PeriodFromJst",
    "PeriodToJst",
    "html_path",
    "scanned_at",
]


def save_result_to_csv(csv_path: str, result: Dict[str, Any],
                       param_names: List[str]) -> None:
    """1件の結果をCSVに追記"""
    file_exists = os.path.isfile(csv_path)

    # ヘッダー構築
    headers = ["run_id"] + METRIC_COLUMNS + param_names + INFO_COLUMNS

    row = {
        "run_id": _make_run_id(result),
        "scanned_at": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
    }

    # 指標
    for col in METRIC_COLUMNS + INFO_COLUMNS:
        if col in result:
            row[col] = result[col]

    # パラメーター値
    params = result.get("parameters", {})
    for pname in param_names:
        row[pname] = params.get(pname, "")

    # ディレクトリ作成
    os.makedirs(os.path.dirname(csv_path) or ".", exist_ok=True)

    with open(csv_path, "a", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=headers, extrasaction="ignore")
        if not file_exists:
            writer.writeheader()
        writer.writerow(row)


def save_results_batch(csv_path: str, results: List[Dict[str, Any]],
                       param_names: List[str]) -> int:
    """複数結果を一括CSV保存"""
    count = 0
    for r in results:
        save_result_to_csv(csv_path, r, param_names)
        count += 1
    return count


# ──────────────────────────────────────────────
# CSV 読み込み
# ──────────────────────────────────────────────

def load_results_csv(csv_path: str) -> List[Dict[str, Any]]:
    """CSVからすべての結果を読み込む"""
    if not os.path.isfile(csv_path):
        return []

    results = []
    with open(csv_path, "r", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        for row in reader:
            # 数値型に変換
            for col in METRIC_COLUMNS:
                if col in row and row[col]:
                    try:
                        row[col] = float(row[col])
                    except ValueError:
                        pass
            results.append(row)
    return results


# ──────────────────────────────────────────────
# ランキング表示
# ──────────────────────────────────────────────

def print_ranking(results: List[Dict[str, Any]],
                  sort_by: str = "NetProfit",
                  ascending: bool = False,
                  top_n: int = 20,
                  param_names: Optional[List[str]] = None) -> None:
    """結果をソートしてコンソールに表示"""
    if not results:
        print("\n[INFO] 結果がありません。")
        return

    # ソート
    def sort_key(r):
        val = r.get(sort_by, 0)
        try:
            return float(val)
        except (ValueError, TypeError):
            return 0

    sorted_results = sorted(results, key=sort_key, reverse=not ascending)

    # 上位N件
    top = sorted_results[:top_n]

    # 表示
    sep = "=" * 100
    print(f"\n{sep}")
    print(f"  TOP {min(top_n, len(top))} 結果 (ソート: {sort_by} {'↑' if ascending else '↓'})")
    print(f"  全{len(results)}件中")
    print(sep)

    # ヘッダー行
    hdr = f"{'#':>4}  {'NetProfit':>12}  {'Trades':>7}  {'MaxDD%':>8}  {'PF':>7}  {'WinRate':>8}"
    if param_names:
        for pn in param_names[:6]:  # 最大6パラメーターまで表示
            short = pn[:12]
            hdr += f"  {short:>12}"
    print(hdr)
    print("-" * len(hdr))

    for i, r in enumerate(top):
        line = (
            f"{i+1:>4}  "
            f"{_fmt_float(r.get('NetProfit')):>12}  "
            f"{_fmt_int(r.get('TotalTrades')):>7}  "
            f"{_fmt_pct(r.get('MaxDDPercent')):>8}  "
            f"{_fmt_float(r.get('ProfitFactor')):>7}  "
            f"{_fmt_pct(r.get('WinRate')):>8}"
        )
        if param_names:
            params = r.get("parameters", r)  # parsed result or CSV row
            for pn in param_names[:6]:
                val = params.get(pn, "")
                line += f"  {str(val):>12}"
        print(line)

    print(sep)

    # ベスト表示
    if top:
        best = top[0]
        print(f"\n  BEST: NetProfit={_fmt_float(best.get('NetProfit'))}  "
              f"Trades={_fmt_int(best.get('TotalTrades'))}  "
              f"MaxDD%={_fmt_pct(best.get('MaxDDPercent'))}  "
              f"PF={_fmt_float(best.get('ProfitFactor'))}")


def save_top_csv(results: List[Dict[str, Any]],
                 csv_path: str,
                 sort_by: str = "NetProfit",
                 ascending: bool = False,
                 top_n: int = 20,
                 param_names: Optional[List[str]] = None) -> None:
    """Top N の結果を別CSVに保存"""
    def sort_key(r):
        val = r.get(sort_by, 0)
        try:
            return float(val)
        except (ValueError, TypeError):
            return 0

    sorted_results = sorted(results, key=sort_key, reverse=not ascending)
    top = sorted_results[:top_n]

    if not top:
        return

    # ヘッダー
    headers = ["rank"] + METRIC_COLUMNS
    if param_names:
        headers += param_names
    headers += ["WinRate", "ROI", "html_path"]

    os.makedirs(os.path.dirname(csv_path) or ".", exist_ok=True)

    with open(csv_path, "w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=headers, extrasaction="ignore")
        writer.writeheader()
        for i, r in enumerate(top):
            row = {"rank": i + 1}
            for col in METRIC_COLUMNS + ["WinRate", "ROI", "html_path"]:
                row[col] = r.get(col, "")
            if param_names:
                params = r.get("parameters", r)
                for pn in param_names:
                    row[pn] = params.get(pn, "")
            writer.writerow(row)

    print(f"[INFO] Top {len(top)} 結果を保存: {csv_path}")


# ──────────────────────────────────────────────
# ユーティリティ
# ──────────────────────────────────────────────

def get_tested_combos(csv_path: str, param_names: List[str]) -> set:
    """既にテスト済みのパラメーター組み合わせを返す（重複回避用）"""
    results = load_results_csv(csv_path)
    combos = set()
    for r in results:
        key = tuple(str(r.get(pn, "")) for pn in param_names)
        combos.add(key)
    return combos


def _make_run_id(result: Dict[str, Any]) -> str:
    """結果からユニークなrun_idを生成"""
    ea = result.get("EA", "unknown")
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    return f"{ea}_{ts}"


def _fmt_float(val, width=0) -> str:
    try:
        return f"{float(val):,.2f}"
    except (ValueError, TypeError):
        return str(val or "N/A")


def _fmt_int(val) -> str:
    try:
        return f"{int(float(val)):,}"
    except (ValueError, TypeError):
        return str(val or "N/A")


def _fmt_pct(val) -> str:
    try:
        return f"{float(val):.2f}%"
    except (ValueError, TypeError):
        return str(val or "N/A")


# ──────────────────────────────────────────────
# CLI テスト
# ──────────────────────────────────────────────
if __name__ == "__main__":
    import sys
    if len(sys.argv) < 2:
        print("使用法: python result_store.py <results.csv>")
        sys.exit(1)

    csv_path = sys.argv[1]
    results = load_results_csv(csv_path)
    print_ranking(results, sort_by="NetProfit", top_n=20)
