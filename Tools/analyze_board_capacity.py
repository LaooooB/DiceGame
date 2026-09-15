"""Reproducible capacity experiment, not a combat/win-rate simulation.

At each step merge the lowest-pip, then lowest-type eligible pair. A merge result
is rolled uniformly from SIX deck types. Otherwise summon one uniform 1-pip die.
Run without a slot cap until the first 6-pip result; the peak occupancy is the
smallest board supporting that exact path without recycling.

Energy, projectile skills, enemies, player hesitation, and retaining existing
6-pip dice are deliberately excluded. Both openings use the same fixed seed,
but diverge in random draws; percentages are independent scenario estimates.
"""
from __future__ import annotations
import argparse
import json
import math
import random
from collections import Counter
from pathlib import Path

CAPACITIES = (8, 12, 16, 18, 20, 22, 24, 26, 28, 31)


def experiment(trials: int, seed: int, opening: list[int]) -> dict:
    rng = random.Random(seed)
    hist: Counter[int] = Counter()
    pulls = 0
    for _ in range(trials):
        board = [0] * 36
        for kind in opening:
            board[kind] += 1
        occupancy = peak = len(opening)
        while True:
            pair = next((i for i in range(30) if board[i] >= 2), None)
            if pair is not None:
                board[pair] -= 2
                result_pip_index = pair // 6 + 1
                board[result_pip_index * 6 + rng.randrange(6)] += 1
                occupancy -= 1
                if result_pip_index == 5:
                    break
            else:
                board[rng.randrange(6)] += 1
                occupancy += 1
                peak = max(peak, occupancy)
                pulls += 1
        hist[peak] += 1
    def q(fraction: float) -> int:
        threshold = math.ceil(trials * fraction)
        total = 0
        for value in sorted(hist):
            total += hist[value]
            if total >= threshold:
                return value
        raise AssertionError("empty distribution")
    return {
        "trials": trials, "seed": seed, "opening_type_indices": opening,
        "mean_peak": sum(size * n for size, n in hist.items()) / trials,
        "max_peak_observed": max(hist), "mean_additional_summons": pulls / trials,
        "peak_percentiles": {str(p): q(p / 100) for p in (50, 90, 95, 99)},
        "capacity_results": [{"slots": c,
                              "first_six_before_jam": sum(n for peak, n in hist.items() if peak <= c),
                              "success_fraction": sum(n for peak, n in hist.items() if peak <= c) / trials}
                             for c in CAPACITIES if c >= len(opening)],
        "peak_histogram": dict(sorted(hist.items())),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trials", type=int, default=20000)
    parser.add_argument("--seed", type=int, default=20260915)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "Artifacts/board-capacity.json")
    args = parser.parse_args()
    if not 1 <= args.trials <= 1000000:
        parser.error("--trials must be between 1 and 1000000")
    report = {
        "method": "eager same-type/same-pip merge, uniform result and summon, six types; stop first six",
        "limitations": "Unlimited energy, no combat, no recycling. Not win rate or an all-run no-jam guarantee.",
        "no_pair_bucket_count_below_six": 6 * 5,
        "sufficient_slots_for_first_six_without_jam": 31,
        "chosen_slots": 24,
        "legacy_opening": experiment(args.trials, args.seed, [0, 0, 1]),
        "new_opening": experiment(args.trials, args.seed, [0, 0, 1] * 3),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    for name in ("legacy_opening", "new_opening"):
        print(name)
        for item in report[name]["capacity_results"]:
            print(f"  {item['slots']:2d} slots: {100*item['success_fraction']:7.3f}% first six before capacity jam")
    print(args.output)


if __name__ == "__main__":
    main()
