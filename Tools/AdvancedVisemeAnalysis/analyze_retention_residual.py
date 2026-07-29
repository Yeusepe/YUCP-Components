#!/usr/bin/env python3
"""How should the previous winner enter the estimate?

analyze_transition_crossfade.py falsified the obvious answer. A state crossfade
blends C[prev] into C[cur] on a fixed schedule, but C[cur, age] is already the
age-conditioned CONDITIONAL MEAN, so it already contains the averaged pull of
whatever preceded it. Blending toward the outgoing state double-counts that,
and the sweep is monotonically worse for every duration and every profile.

A conditional mean cannot be improved by further filtering that adds no
information. The previous winner is information, so it has to be INDEXED, not
blended. The Animator cannot afford 225 distinct trajectories, but it can
afford a rank-1 additive correction:

    w(age) ~ C[cur, age] + d(age) * Delta[prev, cur]

Delta is the residual the pair leaves at the switch instant (a constant vector
per ordered pair, exactly the shape of the retention rows the semantics decoder
already selects) and d(age) is a single scalar decay curve on state time,
shared by every pair. Both are build-time data; runtime cost is one extra
additive term per channel.

Fit Delta and d on 'fit', score on 'heldout', and report the fitted decay so
the retention time constant comes from the corpus rather than from taste.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

import analyze_reconstruction_limits as limits
import analyze_transition_crossfade as crossfade

FRAME_SECONDS = limits.FRAME_SECONDS
AGE_BINS = limits.AGE_BINS
VISEME_COUNT = limits.VISEME_COUNT

# Frames used to estimate the switch-instant residual. One frame is noisy and
# two still sits inside the transition, so the anchor stays causal and local.
ANCHOR_FRAMES = 2
# An ordered pair needs this many anchor frames before it earns its own row.
MIN_PAIR_FRAMES = 12


def fit_residual_model(fit: dict, table: np.ndarray) -> dict:
    age = fit["age"]
    age_bin = np.minimum(age, AGE_BINS - 1)
    baseline = table[fit["winner"], age_bin]
    residual = fit["continuous"] - baseline

    # Delta[prev, cur]: mean residual over the first frames after the switch.
    anchor = age < ANCHOR_FRAMES
    pair_keys = fit["previous"] * VISEME_COUNT + fit["winner"]
    delta, counts = limits.cell_means(
        pair_keys[anchor], residual[anchor], VISEME_COUNT ** 2
    )
    # Pairs that are too rare to estimate contribute nothing rather than noise.
    delta[counts < MIN_PAIR_FRAMES] = 0.0

    # d(age): one scalar per age bin, least squares of residual on Delta.
    pair_delta = delta[pair_keys]
    decay = np.zeros(AGE_BINS, dtype=np.float64)
    for bin_index in range(AGE_BINS):
        mask = age_bin == bin_index
        if not np.any(mask):
            continue
        target = residual[mask]
        basis = pair_delta[mask]
        denominator = float((basis * basis).sum())
        if denominator <= 1e-12:
            continue
        decay[bin_index] = float((target * basis).sum() / denominator)

    return {
        "delta": delta,
        "decay": decay,
        "pairCounts": counts,
    }


def predict(evaluate: dict, table: np.ndarray, model: dict) -> np.ndarray:
    age_bin = np.minimum(evaluate["age"], AGE_BINS - 1)
    baseline = table[evaluate["winner"], age_bin]
    pair_keys = evaluate["previous"] * VISEME_COUNT + evaluate["winner"]
    correction = model["delta"][pair_keys] * model["decay"][age_bin][:, None]
    return baseline + correction


def evaluate_all(records: list[dict]) -> dict:
    fit = crossfade.gather(records, "fit")
    evaluate = crossfade.gather(records, "heldout")
    truth = evaluate["continuous"]
    table = crossfade.winner_age_table(fit)

    transition = evaluate["age"] < limits.TRANSITION_HORIZON_FRAMES
    everything = np.ones(len(truth), bool)

    age_bin_eval = np.minimum(evaluate["age"], AGE_BINS - 1)
    baseline = table[evaluate["winner"], age_bin_eval]
    baseline_score = {
        "all": limits.score(truth, baseline, everything),
        "transition": limits.score(truth, baseline, transition),
    }

    floor_prediction = limits.predict(
        {
            "continuous": fit["continuous"],
            "winner": fit["winner"],
            "previous": fit["previous"],
            "ageBin": np.minimum(fit["age"], AGE_BINS - 1),
        },
        {
            "winner": evaluate["winner"],
            "previous": evaluate["previous"],
            "ageBin": age_bin_eval,
        },
        "prev_winner_age",
    )
    floor_score = {
        "all": limits.score(truth, floor_prediction, everything),
        "transition": limits.score(truth, floor_prediction, transition),
    }

    model = fit_residual_model(fit, table)
    prediction = predict(evaluate, table, model)
    model_score = {
        "all": limits.score(truth, prediction, everything),
        "transition": limits.score(truth, prediction, transition),
    }

    gap = baseline_score["all"]["mse"] - floor_score["all"]["mse"]
    closed = (
        1.0 - (model_score["all"]["mse"] - floor_score["all"]["mse"]) / gap
        if gap > 0 else float("nan")
    )
    gap_transition = (
        baseline_score["transition"]["mse"] - floor_score["transition"]["mse"]
    )
    closed_transition = (
        1.0
        - (model_score["transition"]["mse"] - floor_score["transition"]["mse"])
        / gap_transition
        if gap_transition > 0 else float("nan")
    )

    decay = model["decay"]
    # Where the fitted decay crosses half and a tenth: the corpus's own answer
    # to "how long does the previous viseme matter".
    ages_ms = np.arange(AGE_BINS) * FRAME_SECONDS * 1e3

    return {
        "baseline": baseline_score,
        "floor": floor_score,
        "model": model_score,
        "gapClosedFraction": float(closed),
        "gapClosedFractionTransition": float(closed_transition),
        "decay": decay.tolist(),
        "decayAgesMs": ages_ms.tolist(),
        "activePairs": int(
            np.count_nonzero(model["pairCounts"] >= MIN_PAIR_FRAMES)
        ),
        "deltaMagnitude": float(np.abs(model["delta"]).max()),
    }


def report(result: dict) -> str:
    lines = ["# Rank-1 retention residual against the floor\n"]
    lines.append(
        f"- Baseline C[cur, age]:            RMSE "
        f"{result['baseline']['all']['rmse']:.5f} all, "
        f"{result['baseline']['transition']['rmse']:.5f} transition"
    )
    lines.append(
        f"- **C[cur, age] + d(age)*Delta:    RMSE "
        f"{result['model']['all']['rmse']:.5f} all, "
        f"{result['model']['transition']['rmse']:.5f} transition**"
    )
    lines.append(
        f"- Lookup-table floor:              RMSE "
        f"{result['floor']['all']['rmse']:.5f} all, "
        f"{result['floor']['transition']['rmse']:.5f} transition\n"
    )
    lines.append(
        f"Gap closed: **{result['gapClosedFraction'] * 100:.1f}% overall**, "
        f"**{result['gapClosedFractionTransition'] * 100:.1f}% in transitions**."
    )
    lines.append(
        f"Active ordered pairs: {result['activePairs']} of 225; "
        f"largest Delta component {result['deltaMagnitude']:.4f}.\n"
    )

    lines.append("## Fitted decay d(age)\n")
    lines.append("| age ms | d |")
    lines.append("|---|---|")
    for age_ms, value in zip(result["decayAgesMs"], result["decay"]):
        lines.append(f"| {age_ms:.1f} | {value:.4f} |")
    lines.append("")
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=limits.DEFAULT_CACHE)
    parser.add_argument(
        "--json", type=Path,
        default=Path(__file__).with_name("retention_residual.json"),
    )
    parser.add_argument(
        "--markdown", type=Path,
        default=Path(__file__).with_name("retention_residual.md"),
    )
    args = parser.parse_args(argv)

    records = limits.load_utterances(args.cache)
    result = evaluate_all(records)
    args.json.write_text(
        json.dumps(result, indent=2, sort_keys=True), encoding="utf-8"
    )
    text = report(result)
    args.markdown.write_text(text, encoding="utf-8")
    print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
