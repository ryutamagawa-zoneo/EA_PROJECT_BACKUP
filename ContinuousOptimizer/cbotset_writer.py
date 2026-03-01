"""
.cbotset ファイル書き換えモジュール
テンプレート cbotset を読み込み、指定パラメーターを差し替えて新ファイルを出力
"""

import json
import os
import copy
from typing import Dict, Any


def load_cbotset(path: str) -> Dict[str, Any]:
    """cbotset (JSON) を読み込む"""
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def write_cbotset(data: Dict[str, Any], path: str) -> None:
    """cbotset (JSON) を書き出す"""
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def apply_params(template: Dict[str, Any],
                 param_combo: Dict[str, Any]) -> Dict[str, Any]:
    """
    テンプレートの Parameters セクションに param_combo の値を適用。
    型を元のテンプレート値に合わせる。
    """
    result = copy.deepcopy(template)
    params = result.get("Parameters", {})

    for key, new_val in param_combo.items():
        if key not in params:
            print(f"[WARN] パラメーター '{key}' はテンプレートに存在しません。スキップ。")
            continue

        orig_val = params[key]

        # 型変換
        if isinstance(orig_val, bool):
            if isinstance(new_val, str):
                params[key] = new_val.lower() in ("true", "1", "yes")
            else:
                params[key] = bool(new_val)
        elif isinstance(orig_val, int):
            params[key] = int(round(float(new_val)))
        elif isinstance(orig_val, float):
            params[key] = float(new_val)
        else:
            params[key] = new_val

    result["Parameters"] = params
    return result


def generate_cbotset_file(template_path: str,
                          param_combo: Dict[str, Any],
                          output_dir: str,
                          index: int) -> str:
    """
    テンプレートにパラメーターを適用して新 cbotset ファイルを生成。
    戻り値: 出力ファイルパス
    """
    template = load_cbotset(template_path)
    modified = apply_params(template, param_combo)

    filename = f"opt_{index:06d}.cbotset"
    output_path = os.path.join(output_dir, filename)
    write_cbotset(modified, output_path)

    return output_path


def generate_batch(template_path: str,
                   combos: list,
                   output_dir: str,
                   start_index: int = 0) -> list:
    """
    複数のパラメーター組み合わせから cbotset ファイルを一括生成。
    戻り値: [(index, combo, output_path), ...]
    """
    results = []
    for i, combo in enumerate(combos):
        idx = start_index + i
        path = generate_cbotset_file(template_path, combo, output_dir, idx)
        results.append((idx, combo, path))
    return results


# ──────────────────────────────────────────────
# CLI テスト
# ──────────────────────────────────────────────
if __name__ == "__main__":
    import sys

    if len(sys.argv) < 2:
        print("使用法: python cbotset_writer.py <template.cbotset> [output_dir]")
        print("  テンプレート内容を表示し、テスト書き出しを行います。")
        sys.exit(1)

    template_path = sys.argv[1]
    output_dir = sys.argv[2] if len(sys.argv) > 2 else "./test_cbotset"

    template = load_cbotset(template_path)
    params = template.get("Parameters", {})

    print(f"\n=== テンプレート: {template_path} ===")
    print(f"  Symbol: {template.get('Chart', {}).get('Symbol', 'N/A')}")
    print(f"  Period: {template.get('Chart', {}).get('Period', 'N/A')}")
    print(f"  パラメーター数: {len(params)}")
    print(f"\n  主要パラメーター:")
    for key in ["EmaPeriod", "DirectionDeadzonePips", "MinRRRatio",
                 "FixedSLPips", "TpMultiplier", "ReapproachWindowBars"]:
        if key in params:
            print(f"    {key} = {params[key]}")

    # テスト書き出し
    test_combo = {"EmaPeriod": 30, "MinRRRatio": 1.5}
    path = generate_cbotset_file(template_path, test_combo, output_dir, 0)
    print(f"\n  テスト出力: {path}")
