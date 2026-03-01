"""
cTrader Continuous Optimizer - メインエントリーポイント

モード:
  --mode scan     : cTrader標準レポート(BASE/1〜N) + PRO_*.html を一括スキャン
  --mode rank     : 既存 CSV からランキング表示
  --mode watch    : PRO レポートフォルダを監視し、新しいレポートを自動収集・CSV保存
  --mode generate : パラメーター組み合わせから .cbotset ファイルを一括生成
  --mode auto     : cbotset 生成 → cTrader 自動操作 → 結果収集 を∞ループ

使用法:
  python main.py --mode scan      ← まずこれを実行
  python main.py --mode rank
  python main.py --mode watch
  python main.py --mode generate
  python main.py --mode auto
"""

import argparse
import json
import os
import sys
import time
import glob
from datetime import datetime
from pathlib import Path

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)

from param_generator import (
    load_param_space, grid_search, random_search,
    GeneticOptimizer, format_combo
)
from cbotset_writer import generate_cbotset_file, generate_batch
from result_parser import parse_pro_html, scan_pro_reports, find_new_reports
from ctrader_report_parser import scan_ctrader_standard_folders, parse_ctrader_report_folder
from result_store import (
    save_result_to_csv, save_results_batch, load_results_csv,
    print_ranking, save_top_csv, get_tested_combos
)


def load_config(config_path: str = None) -> dict:
    if config_path is None:
        config_path = os.path.join(SCRIPT_DIR, "config.json")
    with open(config_path, "r", encoding="utf-8") as f:
        return json.load(f)


# ══════════════════════════════════════════════
# MODE: scan - 両フォーマット一括スキャン
# ══════════════════════════════════════════════

def mode_scan(config: dict):
    """
    cTrader標準レポート(BASE/1〜N) と PRO_*.html を両方スキャンしてCSV化
    """
    csv_path = config["results_csv"]
    sort_by = config.get("sort_by", "NetProfit")
    top_n = config.get("top_n", "20")
    top_csv = config.get("results_top_csv", "")

    space_path = os.path.join(SCRIPT_DIR, "param_space.json")
    space = load_param_space(space_path)
    param_names = list(space.keys())

    sep = "=" * 65
    print(f"\n{sep}")
    print(f"  cTrader Continuous Optimizer - スキャンモード (SCAN)")
    print(sep)

    all_results = []

    # ── A) cTrader標準レポート (BASE/1〜N) ──
    base_dir = config.get("ctrader_report_base_dir", "")
    if base_dir and os.path.isdir(base_dir):
        print(f"\n  [A] cTrader標準レポートをスキャン中...")
        print(f"      対象: {base_dir}")
        standard_results = scan_ctrader_standard_folders(base_dir)
        print(f"      検出: {len(standard_results)} 件 (取引あり)")
        all_results.extend(standard_results)
    else:
        print(f"\n  [A] cTrader標準レポート: 対象フォルダなし")
        if base_dir:
            print(f"      (フォルダが存在しません: {base_dir})")

    # ── B) カスタム PRO_*.html ──
    pro_folder = config.get("pro_report_folder", "")
    if pro_folder and os.path.isdir(pro_folder):
        print(f"\n  [B] PRO_*.html レポートをスキャン中...")
        print(f"      対象: {pro_folder}")
        pro_results = scan_pro_reports(pro_folder)
        print(f"      検出: {len(pro_results)} 件 (取引あり)")
        all_results.extend(pro_results)
    else:
        print(f"\n  [B] PRO_*.html レポート: 対象フォルダなし")

    print(f"\n  合計: {len(all_results)} 件")

    if not all_results:
        print("\n  [INFO] 結果が見つかりません。")
        print("         config.json の ctrader_report_base_dir を確認してください。")
        return

    # CSV 保存
    os.makedirs(os.path.dirname(csv_path) or ".", exist_ok=True)
    count = save_results_batch(csv_path, all_results, param_names)
    print(f"\n  CSV保存: {count} 件 -> {csv_path}")

    # ランキング表示（4指標）
    _print_4metric_ranking(all_results, sort_by=sort_by, top_n=int(top_n))

    # Top CSV 保存
    if top_csv:
        save_top_csv(all_results, top_csv, sort_by=sort_by,
                     top_n=int(top_n), param_names=param_names)
        print(f"  Top{top_n}保存: {top_csv}")


# ══════════════════════════════════════════════
# MODE: rank - ランキング表示
# ══════════════════════════════════════════════

def mode_rank(config: dict):
    csv_path = config["results_csv"]
    sort_by = config.get("sort_by", "NetProfit")
    top_n = int(config.get("top_n", 20))
    top_csv = config.get("results_top_csv", "")

    space_path = os.path.join(SCRIPT_DIR, "param_space.json")
    space = load_param_space(space_path)
    param_names = list(space.keys())

    results = load_results_csv(csv_path)
    if not results:
        print(f"[INFO] CSVが空またはありません: {csv_path}")
        print(f"       先に --mode scan を実行してください。")
        return

    _print_4metric_ranking(results, sort_by=sort_by, top_n=top_n)

    if top_csv:
        save_top_csv(results, top_csv, sort_by=sort_by,
                     top_n=top_n, param_names=param_names)


# ══════════════════════════════════════════════
# MODE: watch - PRO レポート監視
# ══════════════════════════════════════════════

def mode_watch(config: dict):
    pro_folder = config.get("pro_report_folder", "")
    csv_path = config["results_csv"]
    poll_interval = config.get("poll_interval_seconds", 5)
    sort_by = config.get("sort_by", "NetProfit")
    top_n = int(config.get("top_n", 20))

    space_path = os.path.join(SCRIPT_DIR, "param_space.json")
    space = load_param_space(space_path)
    param_names = list(space.keys())

    print("=" * 65)
    print("  cTrader Continuous Optimizer - 監視モード (WATCH)")
    print("=" * 65)
    print(f"  監視フォルダ: {pro_folder}")
    print(f"  ポーリング:   {poll_interval}秒間隔 | Ctrl+C で停止")
    print("=" * 65)

    last_check = time.time()
    total_collected = 0

    try:
        while True:
            new_files = find_new_reports(pro_folder, last_check)
            for html_path in new_files:
                result = parse_pro_html(html_path)
                if result and result["TotalTrades"] > 0:
                    save_result_to_csv(csv_path, result, param_names)
                    total_collected += 1
                    print(f"\n[NEW #{total_collected}] {os.path.basename(html_path)}")
                    _print_one_result(result)
            last_check = time.time()
            time.sleep(poll_interval)

    except KeyboardInterrupt:
        print(f"\n\n[STOP] 収集件数: {total_collected}")
        results = load_results_csv(csv_path)
        if results:
            _print_4metric_ranking(results, sort_by=sort_by, top_n=top_n)


# ══════════════════════════════════════════════
# MODE: generate - cbotset 一括生成
# ══════════════════════════════════════════════

def mode_generate(config: dict):
    template_path = config["cbotset_template"]
    output_dir = config["cbotset_output_dir"]
    search_mode = config.get("search_mode", "grid")
    max_iter = config.get("max_iterations", 0)
    seed = config.get("random_seed", 42)

    space_path = os.path.join(SCRIPT_DIR, "param_space.json")
    space = load_param_space(space_path)

    print("=" * 65)
    print("  cTrader Continuous Optimizer - cbotset 生成モード")
    print("=" * 65)
    print(f"  テンプレート: {template_path}")
    print(f"  出力先:       {output_dir}")
    print(f"  探索モード:   {search_mode}")

    if not os.path.isfile(template_path):
        print(f"\n[ERROR] テンプレートが見つかりません: {template_path}")
        sys.exit(1)

    if search_mode == "grid":
        combos = grid_search(space)
    elif search_mode == "random":
        n = max_iter if max_iter > 0 else 100
        combos = random_search(space, n=n, seed=seed)
    else:
        combos = grid_search(space)

    if max_iter > 0:
        combos = combos[:max_iter]

    print(f"  組み合わせ数: {len(combos):,}")

    # テスト済み除外
    csv_path = config.get("results_csv", "")
    param_names = list(space.keys())
    if csv_path and os.path.isfile(csv_path):
        tested = get_tested_combos(csv_path, param_names)
        before = len(combos)
        combos = [c for c in combos
                  if tuple(str(c.get(pn, "")) for pn in param_names) not in tested]
        if before - len(combos) > 0:
            print(f"  テスト済み除外: {before - len(combos):,} 件 | 残り: {len(combos):,} 件")

    if not combos:
        print("\n[INFO] 生成する組み合わせがありません。")
        return

    confirm = input(f"\n  {len(combos):,} 個の cbotset を生成しますか？ (y/n) > ")
    if confirm.strip().lower() != "y":
        return

    results = generate_batch(template_path, combos, output_dir)
    print(f"\n[OK] {len(results):,} ファイルを生成: {output_dir}")


# ══════════════════════════════════════════════
# MODE: auto - 完全自動ループ
# ══════════════════════════════════════════════

def mode_auto(config: dict):
    from ctrader_ui import CTraderController

    template_path = config["cbotset_template"]
    output_dir = config["cbotset_output_dir"]
    pro_folder = config.get("pro_report_folder", "")
    csv_path = config["results_csv"]
    top_csv = config.get("results_top_csv", "")
    timeout = config.get("backtest_timeout_seconds", 600)
    poll = config.get("poll_interval_seconds", 5)
    search_mode = config.get("search_mode", "grid")
    max_iter = config.get("max_iterations", 0)
    seed = config.get("random_seed", 42)
    sort_by = config.get("sort_by", "NetProfit")
    top_n = int(config.get("top_n", 20))
    coords_file = os.path.join(SCRIPT_DIR, config.get("ui_coords_file", "ui_coords.json"))

    space_path = os.path.join(SCRIPT_DIR, "param_space.json")
    space = load_param_space(space_path)
    param_names = list(space.keys())

    if not os.path.isfile(template_path):
        print(f"[ERROR] テンプレートが見つかりません: {template_path}")
        sys.exit(1)
    if not os.path.isfile(coords_file):
        print(f"[ERROR] UI座標ファイルが見つかりません: {coords_file}")
        print(f"        先に setup_coords.py を実行してください。")
        sys.exit(1)

    ctrl = CTraderController(coords_file)

    if search_mode == "grid":
        combos = grid_search(space)
    elif search_mode == "random":
        n = max_iter if max_iter > 0 else 100
        combos = random_search(space, n=n, seed=seed)
    else:
        combos = grid_search(space)

    if csv_path and os.path.isfile(csv_path):
        tested = get_tested_combos(csv_path, param_names)
        combos = [c for c in combos
                  if tuple(str(c.get(pn, "")) for pn in param_names) not in tested]

    print("=" * 65)
    print("  cTrader Continuous Optimizer - 完全自動モード (AUTO)")
    print("=" * 65)
    print(f"  残り組み合わせ: {len(combos):,} | Ctrl+C で停止")
    print("=" * 65)

    os.makedirs(output_dir, exist_ok=True)
    run_count = 0
    error_count = 0

    try:
        for i, combo in enumerate(combos):
            run_count += 1
            print(f"\n[{run_count}/{len(combos)}] {format_combo(combo)}")

            cbotset_path = generate_cbotset_file(template_path, combo, output_dir, i)
            before_time = time.time()
            success = ctrl.run_backtest_cycle(cbotset_path, pro_folder, timeout, poll)

            if success:
                time.sleep(2)
                new_files = find_new_reports(pro_folder, before_time)
                for html_path in new_files:
                    result = parse_pro_html(html_path)
                    if result and result["TotalTrades"] > 0:
                        save_result_to_csv(csv_path, result, param_names)
                        _print_one_result(result)
            else:
                error_count += 1
                if error_count >= 5:
                    print("[ERROR] 連続エラーが多いため停止。UI座標を確認してください。")
                    break

    except KeyboardInterrupt:
        print(f"\n\n[STOP] 実行: {run_count}  エラー: {error_count}")

    results = load_results_csv(csv_path)
    if results:
        _print_4metric_ranking(results, sort_by=sort_by, top_n=top_n)
        if top_csv:
            save_top_csv(results, top_csv, sort_by=sort_by, top_n=top_n, param_names=param_names)


# ══════════════════════════════════════════════
# 表示ユーティリティ
# ══════════════════════════════════════════════

def _print_4metric_ranking(results: list, sort_by: str = "NetProfit",
                            top_n: int = 20):
    """4指標ランキングをコンソールに表示"""
    if not results:
        print("\n[INFO] 結果がありません。")
        return

    def sort_key(r):
        try:
            return float(r.get(sort_by, 0) or 0)
        except (ValueError, TypeError):
            return 0

    sorted_r = sorted(results, key=sort_key, reverse=True)
    top = sorted_r[:top_n]

    sep = "=" * 75
    print(f"\n{sep}")
    print(f"  TOP {min(top_n, len(top))} 結果  (ソート: {sort_by} ↓)  全{len(results)}件中")
    print(sep)
    print(f"{'#':>4}  {'純利益':>12}  {'取引件数':>8}  {'最大EqDD%':>10}  {'PF':>7}  {'勝率':>8}  フォルダ/EA")
    print("-" * 75)

    for i, r in enumerate(top):
        label = r.get("FolderName") or r.get("EA") or ""
        print(
            f"{i+1:>4}  "
            f"{_ff(r.get('NetProfit')):>12}  "
            f"{_fi(r.get('TotalTrades')):>8}  "
            f"{_fp(r.get('MaxDDPercent')):>10}  "
            f"{_ff(r.get('ProfitFactor')):>7}  "
            f"{_fp(r.get('WinRate')):>8}  "
            f"{label}"
        )

    print(sep)
    if top:
        best = top[0]
        print(f"\n  BEST  純利益={_ff(best.get('NetProfit'))}  "
              f"取引={_fi(best.get('TotalTrades'))}  "
              f"最大EqDD%={_fp(best.get('MaxDDPercent'))}  "
              f"PF={_ff(best.get('ProfitFactor'))}")


def _print_one_result(r: dict):
    print(f"  純利益={_ff(r.get('NetProfit'))}  "
          f"取引={_fi(r.get('TotalTrades'))}  "
          f"最大EqDD%={_fp(r.get('MaxDDPercent'))}  "
          f"PF={_ff(r.get('ProfitFactor'))}")


def _ff(v, default="N/A") -> str:
    try:
        return f"{float(v):,.2f}"
    except (ValueError, TypeError):
        return default


def _fi(v, default="N/A") -> str:
    try:
        return f"{int(float(v)):,}"
    except (ValueError, TypeError):
        return default


def _fp(v, default="N/A") -> str:
    try:
        return f"{float(v):.2f}%"
    except (ValueError, TypeError):
        return default


# ══════════════════════════════════════════════
# エントリーポイント
# ══════════════════════════════════════════════

def main():
    parser = argparse.ArgumentParser(
        description="cTrader Continuous Optimizer",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
モード:
  scan     - cTrader標準レポート(BASE/1〜N) + PRO_*.html を一括スキャン ← まずこれ
  rank     - 既存CSVからランキング表示
  watch    - PRO_*.html フォルダを監視し自動収集 (手動BT向け)
  generate - パラメーター組み合わせの .cbotset を一括生成
  auto     - cbotset生成→cTrader操作→結果収集 を∞ループ
        """
    )
    parser.add_argument("--mode", "-m", required=True,
                        choices=["watch", "generate", "auto", "rank", "scan"],
                        help="動作モード")
    parser.add_argument("--config", "-c", default=None,
                        help="config.json のパス (省略時: 同ディレクトリの config.json)")

    args = parser.parse_args()
    config = load_config(args.config)

    if args.mode == "scan":
        mode_scan(config)
    elif args.mode == "rank":
        mode_rank(config)
    elif args.mode == "watch":
        mode_watch(config)
    elif args.mode == "generate":
        mode_generate(config)
    elif args.mode == "auto":
        mode_auto(config)


if __name__ == "__main__":
    main()
