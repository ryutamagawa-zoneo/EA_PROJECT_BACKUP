#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
AI駆動型ベイズ最適化エンジン  EA_BASE_HL_MIX_022.cs / 023.cs 専用

====== 使い方 ======

【ステップ0】 既存の最良結果をDBに登録（初回のみ）

  python ai_optimizer.py add-result
    --cbotset "D:\\ChatGPT EA Development\\...\\100492-3269-12.54%%_022.cbotset"
    --net-profit 100492 --trades 3269 --max-dd 12.54 --pf 1.62

  python ai_optimizer.py add-result
    --cbotset "D:\\ChatGPT EA Development\\...\\98317-3287-13.85%%_021.cbotset"
    --net-profit 98317 --trades 3287 --max-dd 13.85 --pf 1.58

【ステップ1】 次のパラメーターを提案してcbotsetを出力

  python ai_optimizer.py suggest --write
  python ai_optimizer.py suggest --write --output ai_next_params_5.cbotset

【ステップ2】 cTraderでバックテスト実行
  -> 新しいフォルダが BASE/97 のように作成される

【ステップ3】 結果を自動取得

  python ai_optimizer.py report-folder 97

【ステップ4】 状況確認

  python ai_optimizer.py status

-> ステップ1〜3を繰り返す
"""

import json
import os
import sys
import math
import random
import argparse
import copy
from typing import Dict, Any, List, Optional, Tuple
from datetime import datetime

SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
AI_DB_FILE   = os.path.join(SCRIPT_DIR, "ai_results.json")
NEXT_CBOTSET = os.path.join(SCRIPT_DIR, "ai_next_params.cbotset")

# ================================================================
#  パラメーター空間定義
#  (name, type, min, max, default)
#  ※ デフォルト値 = 100492-3269-12.54%_022.cbotset の値
#  ※ 023追加分 = Entry Quality Gate 023 パラメーター (11個)
# ================================================================
PARAM_SPACE: List[Tuple] = [
    # --- CORE入口露出 ---
    ("DirectionDeadzonePips",            "float", 0.5,   30.0,  1.5),
    ("DirectionHysteresisExitEnterRatio","float", 0.05,   0.95, 0.4),
    ("DirectionStateMinHoldBars",        "int",   0,       8,   0),
    # --- エントリー ---
    ("EntryMaxDistancePips",             "float", 15.0, 150.0,  60.0),
    ("ReapproachWindowBars",             "int",   5,    150,    50),
    ("ReapproachMaxDistancePips",        "float", 5.0,  100.0,  20.0),
    ("CrossCandleMinBodyPips",           "float", 1.0,   25.0,  9.0),
    ("MinRRRatio",                       "float", 0.0,    2.5,  0.0),
    # --- SL/TP ---
    ("FixedSLPips",                      "float", 15.0, 120.0,  40.0),
    ("MinSLPips",                        "float", 5.0,   80.0,  40.0),
    ("MaxHoldMinutes",                   "int",   0,    240,    30),
    ("FixedTpPips",                      "float", 100.0, 500.0, 150.0),
    ("TpMultiplier",                     "float", 0.8,    4.0,  1.0),
    # --- RSI ---
    ("SampleRsi_\u671f\u9593",           "int",   5,    100,    46),
    ("SampleRsi_\u58f2\u308a\u95be\u5024","float",60.0,  95.0,  80.0),
    ("SampleRsi_\u8cb7\u3044\u95be\u5024","float",10.0,  45.0,  30.0),
    # --- ブレイクアウト ---
    ("SampleBreakout_\u671f\u9593",      "int",   5,     80,    30),
    ("SampleBreakout_\u30d0\u30f3\u30c9\u9ad8Pips","float",5.0,80.0,20.0),
    ("SampleBreakout_\u4fdd\u3061\u5408\u3044\u9023\u7d9a\u672c\u6570","int",1,10,3),
    # --- 経済指標 ---
    ("MinutesBeforeNews",                "int",   0,     90,    30),
    ("MinutesAfterNews",                 "int",   0,     90,    30),
    ("MinImpactLevel",                   "int",   1,      3,    3),
    # --- エントリーフィルター（現在OFF -> AIが探索） ---
    ("UseHLFilter",                      "bool",  0,    1,    0),
    ("HL_EqualTolerancePips",            "float", 5.0,  50.0, 20.0),
    ("EnableAtrEnvGate",                 "bool",  0,    1,    0),
    ("AtrEnvMinPips",                    "float", 0.0,  30.0,  0.0),
    ("AtrEnvMaxPips",                    "float", 0.0,  80.0,  0.0),
    ("EnableEma10Ema20DirectionDecision","bool",  0,    1,    0),
    ("Ema10Ema20FastPeriod",             "int",   3,    20,   10),
    ("Ema10Ema20SlowPeriod",             "int",   10,   60,   20),
    # --- SL管理（現在OFF -> AIが探索） ---
    ("EnableBreakevenMove",              "bool",  0,    1,    0),
    ("BreakevenTriggerPips",             "int",   15,  150,   50),
    ("EnableStepSlMove",                 "bool",  0,    1,    0),
    ("StepSlStage1TriggerPips",          "int",   20,  100,   40),
    ("StepSlStage1MovePips",             "int",   5,    60,   20),
    # --- TP管理（現在OFF -> AIが探索） ---
    ("EnableStructureTpBoost",           "bool",  0,    1,    0),
    ("StructureTpBoostStartRStage1",     "float", 0.4,  1.5,  0.9),
    ("EnableTpBoostBranchExit",          "bool",  0,    1,    0),
    ("PartialClosePercent",              "float", 0.2,  0.8,  0.5),
    # ================================================================
    #  023 新規パラメーター: Entry Quality Gate 023 (11個)
    # ================================================================
    ("EnableEntryQualityGate023",        "bool",  0,    1,    1),
    ("EntryQualityMinScore023",          "int",   0,    7,    4),
    ("EntryQualityMinBodyRatio023",      "float", 0.0,  1.0,  0.35),
    ("EntryQualityMinCloseLocation023",  "float", 0.0,  1.0,  0.60),
    ("EntryQualityCurrentSlopeBars023",  "int",   1,    20,   2),
    ("EntryQualityCurrentSlopeMinPips023","float",0.0,  10.0, 1.0),
    ("UseEntryQualityM15Filter023",      "bool",  0,    1,    1),
    ("UseEntryQualityH1Filter023",       "bool",  0,    1,    1),
    ("EntryQualityMtfSlopeBars023",      "int",   1,    20,   2),
    ("EntryQualityMtfSlopeMinPips023",   "float", 0.0,  10.0, 1.0),
    ("EntryQualityBlockOnMtfConflict023","bool",  0,    1,    1),
]

N_PARAMS = len(PARAM_SPACE)

# ================================================================
#  ベースライン（最良既知パラメーター  100492-3269-12.54%_022 より）
#  ※ 023新規パラメーターはデフォルト値を設定
# ================================================================
BEST_KNOWN_PARAMS: Dict = {
    "DirectionDeadzonePips":             1.5,
    "DirectionHysteresisExitEnterRatio": 0.4,
    "DirectionStateMinHoldBars":         0,
    "EntryMaxDistancePips":              60.0,
    "ReapproachWindowBars":              50,
    "ReapproachMaxDistancePips":         20.0,
    "CrossCandleMinBodyPips":            9.0,
    "MinRRRatio":                        0.0,
    "FixedSLPips":                       40.0,
    "MinSLPips":                         40.0,
    "MaxHoldMinutes":                    30,
    "FixedTpPips":                       150.0,
    "TpMultiplier":                      1.0,
    "SampleRsi_\u671f\u9593":            46,
    "SampleRsi_\u58f2\u308a\u95be\u5024":80.0,
    "SampleRsi_\u8cb7\u3044\u95be\u5024":30.0,
    "SampleBreakout_\u671f\u9593":       30,
    "SampleBreakout_\u30d0\u30f3\u30c9\u9ad8Pips":20.0,
    "SampleBreakout_\u4fdd\u3061\u5408\u3044\u9023\u7d9a\u672c\u6570":3,
    "MinutesBeforeNews":                 30,
    "MinutesAfterNews":                  30,
    "MinImpactLevel":                    3,
    # OFF parameters (default = disabled)
    "UseHLFilter":                       False,
    "HL_EqualTolerancePips":             20.0,
    "EnableAtrEnvGate":                  False,
    "AtrEnvMinPips":                     0.0,
    "AtrEnvMaxPips":                     0.0,
    "EnableEma10Ema20DirectionDecision":  False,
    "Ema10Ema20FastPeriod":              10,
    "Ema10Ema20SlowPeriod":              20,
    "EnableBreakevenMove":               False,
    "BreakevenTriggerPips":              50,
    "EnableStepSlMove":                  False,
    "StepSlStage1TriggerPips":           40,
    "StepSlStage1MovePips":              20,
    "EnableStructureTpBoost":            False,
    "StructureTpBoostStartRStage1":      0.9,
    "EnableTpBoostBranchExit":           False,
    "PartialClosePercent":               0.5,
    # --- 023 新規パラメーター (Entry Quality Gate 023) ---
    "EnableEntryQualityGate023":         True,
    "EntryQualityMinScore023":           4,
    "EntryQualityMinBodyRatio023":       0.35,
    "EntryQualityMinCloseLocation023":   0.60,
    "EntryQualityCurrentSlopeBars023":   2,
    "EntryQualityCurrentSlopeMinPips023":1.0,
    "UseEntryQualityM15Filter023":       True,
    "UseEntryQualityH1Filter023":        True,
    "EntryQualityMtfSlopeBars023":       2,
    "EntryQualityMtfSlopeMinPips023":    1.0,
    "EntryQualityBlockOnMtfConflict023": True,
}


# ================================================================
#  ユーティリティ
# ================================================================

def _open_json(path: str) -> Dict:
    """UTF-8 BOM 有無を問わず JSON を読み込む"""
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def params_to_vector(params: Dict) -> List[float]:
    """パラメーターを [0, 1] の正規化ベクトルに変換"""
    v = []
    for name, ptype, pmin, pmax, default in PARAM_SPACE:
        val = float(params.get(name, default))
        v.append((val - pmin) / (pmax - pmin + 1e-12))
    return v


def vector_to_params(v: List[float]) -> Dict:
    """正規化ベクトルをパラメーター辞書に逆変換"""
    params = {}
    for i, (name, ptype, pmin, pmax, default) in enumerate(PARAM_SPACE):
        raw = v[i] * (pmax - pmin) + pmin
        raw = max(pmin, min(pmax, raw))
        if ptype == "bool":
            params[name] = bool(int(round(raw)))
        elif ptype == "int":
            params[name] = int(round(raw))
        else:
            params[name] = round(raw, 4)
    return params


def compute_score(net_profit: float, trades: int,
                  max_dd: float, pf: float) -> float:
    """
    4指標から複合スコアを計算（高いほど良い）

    score = (NetProfit / MaxDD) * PF * (Trades/500)^0.3

    ・純利益をMaxDDで割る  ->  リスク調整済みリターン
    ・PFを乗算             ->  エッジの質
    ・取引件数のべき乗     ->  統計的信頼性ボーナス
    """
    if max_dd <= 0.0:
        max_dd = 0.01
    if trades <= 0:
        return float("-inf")
    if net_profit <= 0:
        return net_profit / max_dd        # ネガティブスコア（ペナルティ）

    trade_factor = (trades / 500.0) ** 0.3
    score = (net_profit / max_dd) * max(pf, 0.1) * trade_factor
    return round(score, 4)


def load_db() -> List[Dict]:
    if not os.path.isfile(AI_DB_FILE):
        return []
    with open(AI_DB_FILE, "r", encoding="utf-8") as f:
        return json.load(f)


def save_db(db: List[Dict]) -> None:
    with open(AI_DB_FILE, "w", encoding="utf-8") as f:
        json.dump(db, f, ensure_ascii=False, indent=2)
    print("  [DB] %d 件を保存 -> %s" % (len(db), AI_DB_FILE))


def euclidean_distance(v1: List[float], v2: List[float]) -> float:
    return math.sqrt(sum((a - b) ** 2 for a, b in zip(v1, v2)))


# ================================================================
#  パラメーター生成
# ================================================================

def generate_random_params() -> Dict:
    params = {}
    for name, ptype, pmin, pmax, default in PARAM_SPACE:
        if ptype == "bool":
            params[name] = bool(random.randint(0, 1))
        elif ptype == "int":
            params[name] = random.randint(int(pmin), int(pmax))
        else:
            params[name] = round(random.uniform(pmin, pmax), 4)
    return params


def perturb_params(base_params: Dict, scale: float = 0.15) -> Dict:
    """
    既存パラメーターをガウス変動させる
    scale: レンジ幅に対する標準偏差の割合 (0.15 = 15%)
    """
    params = {}
    for name, ptype, pmin, pmax, default in PARAM_SPACE:
        base_val = float(base_params.get(name, default))
        range_size = pmax - pmin
        delta = random.gauss(0.0, scale * range_size)
        new_val = base_val + delta
        new_val = max(pmin, min(pmax, new_val))
        if ptype == "bool":
            # boolean: scale が大きいほどフリップしやすい
            base_bool = bool(round(float(base_params.get(name, default))))
            flip_prob = min(scale * 1.5, 0.5)
            params[name] = bool(1 - int(base_bool)) if random.random() < flip_prob else base_bool
        elif ptype == "int":
            params[name] = int(round(new_val))
        else:
            params[name] = round(new_val, 4)
    return params


def latin_hypercube_sample(n: int) -> List[List[float]]:
    """
    ラテン超方格サンプリング: 空間をまんべんなく探索するための初期サンプル
    """
    dims = N_PARAMS
    intervals = [list(range(n)) for _ in range(dims)]
    for d in range(dims):
        random.shuffle(intervals[d])

    samples = []
    for i in range(n):
        point = [(intervals[d][i] + random.random()) / n for d in range(dims)]
        samples.append(point)
    return samples


# ================================================================
#  サロゲートモデル（GP または ヒューリスティック）
# ================================================================

def _gp_suggest(db: List[Dict]) -> Dict:
    """
    ガウス過程回帰 + 期待改善量 (EI) による次候補選択
    scikit-learn / scipy が必要
    """
    import numpy as np
    from sklearn.gaussian_process import GaussianProcessRegressor
    from sklearn.gaussian_process.kernels import Matern, WhiteKernel
    from scipy.stats import norm

    # 学習データ準備
    X_train = np.array([params_to_vector(r["params"]) for r in db])
    y_train = np.array([r["score"] for r in db], dtype=float)

    # GP フィット
    kernel = (
        Matern(nu=2.5, length_scale_bounds=(1e-3, 10.0))
        + WhiteKernel(noise_level=1.0, noise_level_bounds=(1e-5, 1e2))
    )
    gp = GaussianProcessRegressor(
        kernel=kernel,
        n_restarts_optimizer=10,
        normalize_y=True,
        random_state=42,
    )
    gp.fit(X_train, y_train)

    # 候補点生成（LHS + ベスト周辺 + 純ランダム）
    n_lhs     = 800
    n_exploit = 1000
    n_random  = 1200
    candidates: List[Dict] = []

    for v in latin_hypercube_sample(n_lhs):
        candidates.append(vector_to_params(v))

    top5 = sorted(db, key=lambda x: x["score"], reverse=True)[:5]
    for _ in range(n_exploit):
        base  = random.choice(top5)
        scale = random.uniform(0.05, 0.25)
        candidates.append(perturb_params(base["params"], scale=scale))

    for _ in range(n_random):
        candidates.append(generate_random_params())

    # EI 計算
    X_cand          = np.array([params_to_vector(p) for p in candidates])
    y_pred, sigma   = gp.predict(X_cand, return_std=True)

    y_best = float(np.max(y_train))
    xi     = 0.01   # 大きいほど探索重視
    with np.errstate(divide="ignore", invalid="ignore"):
        Z  = (y_pred - y_best - xi) / (sigma + 1e-9)
        ei = (y_pred - y_best - xi) * norm.cdf(Z) + sigma * norm.pdf(Z)
        ei[sigma < 1e-10] = 0.0

    best_idx  = int(np.argmax(ei))
    best_pred = float(y_pred[best_idx])
    best_ei   = float(ei[best_idx])
    best_sig  = float(sigma[best_idx])

    print("  [GP] 予測スコア=%.2f  EI=%.4f  sigma=%.2f" % (best_pred, best_ei, best_sig))
    print("  [GP] 現在最良スコア=%.2f" % y_best)

    return candidates[best_idx]


def _heuristic_suggest(db: List[Dict]) -> Dict:
    """
    GP 利用不可のときのフォールバック
    加重近傍補間スコア + 多様性ボーナスで候補を評価
    """
    candidates: List[Dict] = []
    top5 = sorted(db, key=lambda x: x["score"], reverse=True)[:5]

    for v in latin_hypercube_sample(600):
        candidates.append(vector_to_params(v))

    for _ in range(900):
        base  = random.choice(top5)
        scale = random.uniform(0.08, 0.30)
        candidates.append(perturb_params(base["params"], scale=scale))

    for _ in range(500):
        candidates.append(generate_random_params())

    tested_vecs   = [params_to_vector(r["params"]) for r in db]
    tested_scores = [r["score"] for r in db]
    max_s   = max(tested_scores)
    min_s   = min(tested_scores)
    range_s = max_s - min_s + 1e-8

    best_candidate = None
    best_acq       = float("-inf")

    for p in candidates:
        v         = params_to_vector(p)
        distances = [euclidean_distance(v, tv) for tv in tested_vecs]
        min_dist  = min(distances)
        weights   = [1.0 / (d + 0.01) for d in distances]
        total_w   = sum(weights)
        interp    = sum(w * s for w, s in zip(weights, tested_scores)) / total_w

        interp_norm = (interp - min_s) / range_s
        diversity   = min_dist / math.sqrt(N_PARAMS)
        acq         = interp_norm + 0.35 * diversity

        if acq > best_acq:
            best_acq       = acq
            best_candidate = p

    print("  [Heuristic] 最良取得値=%.4f" % best_acq)
    return best_candidate  # type: ignore


def suggest_next_params(db: List[Dict]) -> Dict:
    """
    次にテストすべきパラメーターを提案するメイン関数

    データ数が少ない場合:
      0件       -> ベースライン(既知最良)を微小変動
      1〜4件    -> ベスト周辺をより広めに探索
      5件以上   -> GP (Bayesian) で EI 最大化
    """
    if len(db) == 0:
        print("  [INFO] データ0件: 既知最良パラメーターを微小変動して提案")
        return perturb_params(BEST_KNOWN_PARAMS, scale=0.08)

    best = max(db, key=lambda x: x["score"])

    if len(db) < 5:
        print("  [INFO] データ%d件: ベスト周辺を中程度探索" % len(db))
        return perturb_params(best["params"], scale=0.18)

    # GP チャレンジ
    try:
        import sklearn  # noqa: F401
        import scipy    # noqa: F401
        print("  [INFO] データ%d件: Gaussian Process (Bayesian) で提案" % len(db))
        return _gp_suggest(db)
    except ImportError:
        print("  [WARN] scikit-learn/scipy が未インストール -> ヒューリスティックを使用")
        print("  ヒント: pip install scikit-learn scipy numpy")
        return _heuristic_suggest(db)
    except Exception as e:
        print("  [WARN] GP失敗 (%s) -> ヒューリスティックに切替" % e)
        return _heuristic_suggest(db)


# ================================================================
#  cbotset 書き出し
# ================================================================

def write_cbotset(params: Dict, template_path: str, output_path: str) -> bool:
    """テンプレートcbotsetに最適化パラメーターを上書きして保存"""
    if not os.path.isfile(template_path):
        print("  [ERROR] テンプレートが見つかりません: %s" % template_path)
        return False

    template = _open_json(template_path)

    for name, val in params.items():
        template["Parameters"][name] = val

    # バックテスト中はPROレポート不要
    template["Parameters"]["EnableProReport"] = False

    os.makedirs(os.path.dirname(output_path) if os.path.dirname(output_path) else ".", exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(template, f, ensure_ascii=False, indent=2)

    print("  [OK] cbotset 書き出し完了: %s" % output_path)
    return True


# ================================================================
#  コマンド実装
# ================================================================

def cmd_status(db: List[Dict]) -> None:
    """結果一覧と最良パラメーターを表示"""
    print("")
    print("=" * 65)
    print("  AI Bayesian Optimizer - ステータス")
    print("=" * 65)

    if not db:
        print("")
        print("  まだ結果がありません。")
        print("  python ai_optimizer.py add-result --cbotset <path> ...")
        print("  で既存の結果を登録してください。")
        return

    sorted_db = sorted(db, key=lambda x: x["score"], reverse=True)
    best = sorted_db[0]

    print("")
    print("  総テスト数: %d" % len(db))
    print("")
    print("  [最良結果]")
    print("    スコア      : %12.2f" % best["score"])
    print("    純利益      : %s" % "{:12,.2f}".format(best.get("net_profit", 0)))
    print("    取引件数    : %6d"     % best.get("trades", 0))
    print("    最大DD%%    : %8.2f %%" % best.get("max_dd", 0))
    print("    PF          : %8.4f"   % best.get("pf", 0))
    print("    登録日時    : %s"       % best.get("timestamp", "?"))

    print("")
    print("  [最良パラメーター (最適化対象 %d個)]" % N_PARAMS)
    print("  %-42s  %12s" % ("パラメーター名", "値"))
    print("  " + "-" * 56)
    for name, ptype, pmin, pmax, default in PARAM_SPACE:
        val = best["params"].get(name, default)
        print("  %-42s  %12s" % (name, str(val)))

    print("")
    print("  [スコア上位 Top-10]")
    print("  %3s  %10s  %12s  %6s  %7s  %6s  %s" % (
        "#", "スコア", "純利益", "取引", "DD%", "PF", "日時"
    ))
    print("  " + "-" * 72)
    for i, r in enumerate(sorted_db[:10]):
        print("  %3d  %10.2f  %12s  %6d  %7.2f%%  %6.3f  %s" % (
            i + 1,
            r["score"],
            "{:,.0f}".format(r.get("net_profit", 0)),
            r.get("trades", 0),
            r.get("max_dd", 0),
            r.get("pf", 0),
            r.get("timestamp", "")[:16],
        ))
    print("")


def cmd_suggest(db: List[Dict], template_path: str, write: bool,
                output_path: str = "") -> None:
    """次にテストすべきパラメーターを提案し、オプションでcbotsetを出力"""
    print("")
    print("=" * 65)
    print("  次のテストパラメーターを計算中...")
    print("=" * 65)

    next_params = suggest_next_params(db)

    # 現在最良のパラメーター
    best_params: Dict = {}
    if db:
        best_entry  = max(db, key=lambda x: x["score"])
        best_params = best_entry["params"]

    print("")
    print("  [提案パラメーター (%d個)]" % N_PARAMS)
    print("  %-42s  %12s  %12s" % ("パラメーター名", "提案値", "現在最良"))
    print("  " + "-" * 72)
    for name, ptype, pmin, pmax, default in PARAM_SPACE:
        val  = next_params.get(name, default)
        best = best_params.get(name, default)
        diff = "  <<<" if str(val) != str(best) else ""
        print("  %-42s  %12s  %12s%s" % (name, str(val), str(best), diff))

    if write:
        out = output_path if output_path else NEXT_CBOTSET
        print("")
        print("  cbotsetファイルを生成中...")
        success = write_cbotset(next_params, template_path, out)
        if success:
            print("")
            print("  +----------------------------------------------------------+")
            print("  |  出力ファイル:                                            |")
            print("  |  %-56s|" % out)
            print("  +----------------------------------------------------------+")
            print("")
            print("  [次の手順]")
            print("  1. cTrader を開いてバックテスト画面を表示")
            print("  2. cBot 設定（歯車）-> 「インポート」")
            print("  3. 上記ファイルを選択して開く")
            print("  4. バックテスト開始 [Play]")
            print("  5. 完了後に結果を報告:")
            print("     python ai_optimizer.py report-folder <BASEフォルダ番号>")
    else:
        print("")
        print("  ※ cbotsetを生成するには --write オプションを追加してください")
        print("     python ai_optimizer.py suggest --write")
        print("     python ai_optimizer.py suggest --write --output ai_next_params_5.cbotset")


def cmd_add_result(
    db: List[Dict],
    params: Dict,
    net_profit: float,
    trades: int,
    max_dd: float,
    pf: float,
    note: str = ""
) -> None:
    """結果をDBに手動追加"""
    score = compute_score(net_profit, trades, max_dd, pf)
    entry = {
        "params":     params,
        "net_profit": net_profit,
        "trades":     trades,
        "max_dd":     max_dd,
        "pf":         pf,
        "score":      score,
        "timestamp":  datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "note":       note,
    }
    db.append(entry)
    save_db(db)

    print("")
    print("  [OK] 結果を追加しました (#%d):" % len(db))
    print("    スコア    : %12.2f"   % score)
    print("    純利益    : %s" % "{:12,.0f}".format(net_profit))
    print("    取引件数  : %6d"      % trades)
    print("    最大DD%%  : %8.2f %%" % max_dd)
    print("    PF        : %8.4f"    % pf)

    if len(db) >= 2:
        best = max(db, key=lambda x: x["score"])
        if entry is best:
            print("")
            print("  *** 現在のベストスコアを更新！ ***")


def cmd_report_folder(
    db: List[Dict],
    folder_arg: str,
    base_dir: str,
    pf_override: Optional[float]
) -> None:
    """BASEフォルダのレポートを自動解析して結果をDB追加"""
    # パス解決
    if folder_arg.isdigit():
        folder_path = os.path.join(base_dir, folder_arg)
    else:
        folder_path = folder_arg

    if not os.path.isdir(folder_path):
        print("  [ERROR] フォルダが見つかりません: %s" % folder_path)
        return

    # ctrader_report_parser で解析
    sys.path.insert(0, SCRIPT_DIR)
    try:
        from ctrader_report_parser import parse_ctrader_report_folder
    except ImportError:
        print("  [ERROR] ctrader_report_parser.py が見つかりません")
        return

    result = parse_ctrader_report_folder(folder_path)
    if not result:
        print("  [ERROR] レポート解析失敗: %s" % folder_path)
        return

    # PARAM_SPACE に含まれるパラメーターだけ抽出
    raw_params = result.get("parameters", {})
    params = {
        name: raw_params[name]
        for name, *_ in PARAM_SPACE
        if name in raw_params
    }

    if not params:
        print("  [WARN] parameters.cbotset からパラメーターを取得できませんでした。")

    net_profit = result["NetProfit"]
    trades     = result["TotalTrades"]
    max_dd     = result["MaxDDPercent"]
    pf         = pf_override if pf_override is not None else result["ProfitFactor"]

    print("")
    print("  フォルダ解析結果: %s" % os.path.basename(folder_path))
    print("    純利益   : %s" % "{:12,.2f}".format(net_profit))
    print("    取引件数 : %6d"     % trades)
    print("    最大DD%% : %8.2f %%" % max_dd)
    print("    PF       : %8.4f"   % pf)
    print("    パラメーター数: %d"  % len(params))

    note = "folder:" + os.path.basename(folder_path)
    cmd_add_result(db, params, net_profit, trades, max_dd, pf, note)


# ================================================================
#  メイン
# ================================================================

def _load_config() -> Dict:
    config_path = os.path.join(SCRIPT_DIR, "config.json")
    if os.path.isfile(config_path):
        with open(config_path, "r", encoding="utf-8-sig") as f:
            return json.load(f)
    return {}


def main() -> None:
    parser = argparse.ArgumentParser(
        description="AI Bayesian Optimizer for EA_BASE_HL_MIX_023.cs",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    sub = parser.add_subparsers(dest="command", metavar="COMMAND")

    # status
    sub.add_parser("status", help="結果一覧と最良パラメーターを表示")

    # suggest
    p_sug = sub.add_parser("suggest", help="次のパラメーターを提案")
    p_sug.add_argument("--write",    action="store_true",
                       help="cbotsetファイルを出力する")
    p_sug.add_argument("--template", default="",
                       help="テンプレートcbotsetパス（省略時はconfig.jsonを使用）")
    p_sug.add_argument("--output",   default="",
                       help="出力cbotsetファイル名 (例: ai_next_params_5.cbotset)  "
                            "省略時は ai_next_params.cbotset  "
                            "相対パスの場合はスクリプトと同じフォルダに保存")

    # add-result
    p_add = sub.add_parser("add-result", help="過去の結果を手動でDBに追加")
    p_add.add_argument("--cbotset",    required=True,
                       help="パラメーターを含む .cbotset ファイルのパス")
    p_add.add_argument("--net-profit", type=float, required=True,
                       help="純利益 (例: 100492)")
    p_add.add_argument("--trades",     type=int,   required=True,
                       help="取引件数 (例: 3269)")
    p_add.add_argument("--max-dd",     type=float, required=True,
                       help="最大有効証拠金DD%% (例: 12.54)")
    p_add.add_argument("--pf",         type=float, required=True,
                       help="プロフィットファクター (例: 1.62)")
    p_add.add_argument("--note",       default="",
                       help="メモ（省略可）")

    # report-folder
    p_rep = sub.add_parser("report-folder",
                           help="BASEフォルダのレポートを自動解析して追加")
    p_rep.add_argument("folder",
                       help="フォルダ番号 (例: 97) またはフルパス")
    p_rep.add_argument("--pf",   type=float, default=None,
                       help="PF値を手動指定（report.htmlに値がない場合）")

    args = parser.parse_args()

    if args.command is None:
        parser.print_help()
        return

    config = _load_config()

    default_template = config.get(
        "cbotset_template",
        r"D:\ChatGPT EA Development\プロジェクト\パラメーター欄保存庫\HighLow\100492-3269-12.54%_023_template.cbotset",
    )
    base_dir = config.get(
        "ctrader_report_base_dir",
        r"D:\ChatGPT EA Development\プロジェクト\パラメーター欄保存庫\BASE",
    )

    db = load_db()

    if args.command == "status":
        cmd_status(db)

    elif args.command == "suggest":
        tmpl = args.template if args.template else default_template

        # --output の処理: 相対パスならスクリプトフォルダに配置
        out_arg = args.output if args.output else ""
        if out_arg:
            if not os.path.isabs(out_arg):
                out_path = os.path.join(SCRIPT_DIR, out_arg)
            else:
                out_path = out_arg
        else:
            out_path = NEXT_CBOTSET

        cmd_suggest(db, tmpl, write=args.write, output_path=out_path)

    elif args.command == "add-result":
        cbotset_path = args.cbotset
        if not os.path.isfile(cbotset_path):
            print("[ERROR] cbotsetファイルが見つかりません: %s" % cbotset_path)
            return
        cbotset_data = _open_json(cbotset_path)
        all_params = cbotset_data.get("Parameters", {})
        params = {
            name: all_params[name]
            for name, *_ in PARAM_SPACE
            if name in all_params
        }
        if not params:
            print("[WARN] cbotsetにPARAM_SPACEのパラメーターが含まれていません。")
        note = args.note or os.path.basename(cbotset_path)
        cmd_add_result(
            db, params,
            net_profit=args.net_profit,
            trades=args.trades,
            max_dd=args.max_dd,
            pf=args.pf,
            note=note,
        )

    elif args.command == "report-folder":
        cmd_report_folder(db, args.folder, base_dir, args.pf)

    else:
        parser.print_help()


if __name__ == "__main__":
    main()
