"""
パラメーター組み合わせ生成器
Grid Search / Random Search / 遺伝的アルゴリズム
"""

import json
import math
import random
from itertools import product
from pathlib import Path
from typing import List, Dict, Any, Optional


def load_param_space(path: str) -> Dict[str, Any]:
    """param_space.json を読み込む"""
    with open(path, "r", encoding="utf-8") as f:
        raw = json.load(f)
    # _comment 等のメタキーを除外
    return {k: v for k, v in raw.items() if not k.startswith("_")}


def _build_axis(spec: dict) -> list:
    """1パラメーターの探索軸を生成"""
    ptype = spec.get("type", "float")

    # values 列挙があればそちらを優先
    if "values" in spec:
        return list(spec["values"])

    if ptype == "bool":
        return [True, False]

    if ptype == "enum":
        return list(spec.get("values", []))

    lo = spec["min"]
    hi = spec["max"]
    step = spec["step"]

    vals = []
    v = lo
    while v <= hi + 1e-9:
        if ptype == "int":
            vals.append(int(round(v)))
        else:
            vals.append(round(v, 6))
        v += step
    return vals


def _count_grid(space: Dict[str, Any]) -> int:
    """Grid Search の全組み合わせ数を計算"""
    total = 1
    for spec in space.values():
        total *= len(_build_axis(spec))
    return total


# ──────────────────────────────────────────────
# Grid Search
# ──────────────────────────────────────────────
def grid_search(space: Dict[str, Any]) -> List[Dict[str, Any]]:
    """全パラメーター組み合わせを返す"""
    names = list(space.keys())
    axes = [_build_axis(space[n]) for n in names]
    combos = []
    for vals in product(*axes):
        combo = dict(zip(names, vals))
        combos.append(combo)
    return combos


# ──────────────────────────────────────────────
# Random Search
# ──────────────────────────────────────────────
def random_search(space: Dict[str, Any], n: int, seed: int = 42) -> List[Dict[str, Any]]:
    """ランダムにn個の組み合わせを返す"""
    rng = random.Random(seed)
    names = list(space.keys())
    axes = [_build_axis(space[nm]) for nm in names]
    combos = []
    seen = set()
    for _ in range(n * 10):  # 重複排除のため多めに試行
        vals = tuple(rng.choice(ax) for ax in axes)
        if vals not in seen:
            seen.add(vals)
            combos.append(dict(zip(names, vals)))
        if len(combos) >= n:
            break
    return combos


# ──────────────────────────────────────────────
# 遺伝的アルゴリズム (簡易版)
# ──────────────────────────────────────────────
class GeneticOptimizer:
    """遺伝的アルゴリズムで最適なパラメーターを探索"""

    def __init__(self, space: Dict[str, Any], pop_size: int = 30,
                 elite_ratio: float = 0.2, mutation_rate: float = 0.15,
                 seed: int = 42):
        self.space = space
        self.names = list(space.keys())
        self.axes = {n: _build_axis(space[n]) for n in self.names}
        self.pop_size = pop_size
        self.elite_count = max(1, int(pop_size * elite_ratio))
        self.mutation_rate = mutation_rate
        self.rng = random.Random(seed)
        self.population: List[Dict[str, Any]] = []
        self.fitness_history: List[Dict[str, Any]] = []

    def init_population(self) -> List[Dict[str, Any]]:
        """初期集団を生成"""
        self.population = []
        for _ in range(self.pop_size):
            individual = {}
            for name in self.names:
                individual[name] = self.rng.choice(self.axes[name])
            self.population.append(individual)
        return list(self.population)

    def evolve(self, fitness_scores: List[float]) -> List[Dict[str, Any]]:
        """適応度に基づいて次世代を生成"""
        # ソート (降順 = 適応度が高い順)
        paired = list(zip(self.population, fitness_scores))
        paired.sort(key=lambda x: x[1], reverse=True)

        # エリート保持
        elites = [p[0] for p in paired[:self.elite_count]]

        # 次世代
        new_pop = list(elites)

        # 残りは交叉 + 突然変異
        while len(new_pop) < self.pop_size:
            p1 = self._tournament_select(paired)
            p2 = self._tournament_select(paired)
            child = self._crossover(p1, p2)
            child = self._mutate(child)
            new_pop.append(child)

        self.population = new_pop
        return list(self.population)

    def _tournament_select(self, paired, k=3):
        """トーナメント選択"""
        candidates = self.rng.sample(paired, min(k, len(paired)))
        return max(candidates, key=lambda x: x[1])[0]

    def _crossover(self, p1, p2):
        """一様交叉"""
        child = {}
        for name in self.names:
            child[name] = p1[name] if self.rng.random() < 0.5 else p2[name]
        return child

    def _mutate(self, individual):
        """突然変異: ランダムに値を変更"""
        mutated = dict(individual)
        for name in self.names:
            if self.rng.random() < self.mutation_rate:
                mutated[name] = self.rng.choice(self.axes[name])
        return mutated


# ──────────────────────────────────────────────
# ユーティリティ
# ──────────────────────────────────────────────
def estimate_grid_size(param_space_path: str) -> int:
    """Grid Search の組み合わせ数を見積もる"""
    space = load_param_space(param_space_path)
    return _count_grid(space)


def format_combo(combo: Dict[str, Any]) -> str:
    """パラメーター組み合わせを読みやすい文字列に"""
    parts = [f"{k}={v}" for k, v in combo.items()]
    return " | ".join(parts)


if __name__ == "__main__":
    import sys
    space_path = sys.argv[1] if len(sys.argv) > 1 else "param_space.json"
    space = load_param_space(space_path)

    total = _count_grid(space)
    print(f"=== パラメーター探索空間 ===")
    for name, spec in space.items():
        axis = _build_axis(spec)
        print(f"  {name}: {len(axis)} 値 ({axis[0]} ~ {axis[-1]})")
    print(f"\n  Grid Search 全組み合わせ数: {total:,}")
    print(f"  1回5分の場合の推定時間: {total * 5 / 60:.1f} 時間")
