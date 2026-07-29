#!/usr/bin/env python3
"""Can an animator crossfade realise the (prev, current, age) error floor?

analyze_reconstruction_limits.py showed the MMSE floor for the causal
statistic (previous winner, current winner, time since switch) sits well below
the per-winner static row that ships today. That floor was measured with a
free 15x15x12 lookup table, which is not what the Animator can evaluate.

The Animator CAN evaluate a state crossfade: during a transition it blends the
outgoing state's motion with the incoming state's motion over a fixed
duration. Written out, that is exactly

    w(age) = (1 - s(age/T)) * C[prev](prev_age + age) + s(age/T) * C[cur](age)

with C the per-winner age-conditioned trajectory the decoder states already
emit, s the blend profile and T the transition duration. So the crossfade is
not a cosmetic smoothing device: it is the mechanism that carries the previous
winner into the estimate, and T is how long the previous winner is allowed to
matter.

Sweep T and the blend profile, score on held-out, and compare against both the
no-prev baseline and the table floor. If the crossfade closes most of the gap
the fix is a build-time constant, not new runtime math.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

import analyze_reconstruction_limits as limits

FRAME_SECONDS = limits.FRAME_SECONDS
AGE_BINS = limits.AGE_BINS
VISEME_COUNT = limits.VISEME_COUNT

# Candidate transition durations in milliseconds. 72 ms is the value the
# corpus dynamics model was fitted against and which the builder declares but
# never applies.
DURATION_MS = (
    0.0, 21.3, 42.7, 64.0, 72.0, 85.3, 106.7, 128.0, 149.3, 170.7, 192.0,
    213.3, 256.0, 320.0,
)


def blend_profiles() -> dict:
    """s(u) on [0, 1]. Unity's own crossfade is the linear one."""
    return {
        "linear": lambda u: u,
        "smoothstep": lambda u: u * u * (3.0 - 2.0 * u),
        "quintic": lambda u: u * u * u * (u * (u * 6.0 - 15.0) + 10.0),
    }


def features(record: dict) -> dict:
    """Per-frame causal features including the previous run's final age."""
    winners = record["winners"]
    length = len(winners)
    previous = np.empty(length, dtype=np.int64)
    age = np.empty(length, dtype=np.int64)
    previous_age = np.zeros(length, dtype=np.int64)

    previous[0] = winners[0]
    age[0] = 0
    run_start = 0
    for index in range(1, length):
        if winners[index] != winners[index - 1]:
            previous[index] = winners[index - 1]
            age[index] = 0
            # Age the outgoing state had reached when it was interrupted.
            previous_age[index] = index - 1 - run_start
            run_start = index
        else:
            previous[index] = previous[index - 1]
            age[index] = age[index - 1] + 1
            previous_age[index] = previous_age[index - 1]

    return {
        "continuous": record["continuous"],
        "winner": winners,
        "previous": previous,
        "age": age,
        "previousAge": previous_age,
    }


def gather(records: list[dict], split: str) -> dict:
    parts = [features(r) for r in records if r["split"] == split]
    if not parts:
        raise ValueError(f"Split '{split}' is empty")
    return {
        key: np.concatenate([p[key] for p in parts], axis=0)
        for key in ("continuous", "winner", "previous", "age", "previousAge")
    }


def winner_age_table(fit: dict) -> np.ndarray:
    """C[winner, ageBin]: the trajectory a decoder state already emits."""
    age_bin = np.minimum(fit["age"], AGE_BINS - 1)
    keys = fit["winner"] * AGE_BINS + age_bin
    cells = VISEME_COUNT * AGE_BINS
    means, counts = limits.cell_means(keys, fit["continuous"], cells)

    # Back sparse cells off to the winner's overall mean, then to the global
    # mean, so a rare (winner, age) pair cannot inject noise.
    winner_means, winner_counts = limits.cell_means(
        fit["winner"], fit["continuous"], VISEME_COUNT
    )
    global_mean = fit["continuous"].mean(axis=0)
    table = means.reshape(VISEME_COUNT, AGE_BINS, VISEME_COUNT)
    counts = counts.reshape(VISEME_COUNT, AGE_BINS)
    for winner in range(VISEME_COUNT):
        fallback = (
            winner_means[winner]
            if winner_counts[winner] >= limits.MIN_CELL_FRAMES
            else global_mean
        )
        for bin_index in range(AGE_BINS):
            if counts[winner, bin_index] < limits.MIN_CELL_FRAMES:
                table[winner, bin_index] = fallback
    return table


def crossfade_prediction(
    evaluate: dict,
    table: np.ndarray,
    duration_frames: float,
    profile,
    running_previous: bool,
) -> np.ndarray:
    age = evaluate["age"]
    winner = evaluate["winner"]
    previous = evaluate["previous"]
    previous_age = evaluate["previousAge"]

    age_bin = np.minimum(age, AGE_BINS - 1)
    current = table[winner, age_bin]

    if running_previous:
        # Unity keeps the outgoing state playing during the blend, so its own
        # trajectory continues to advance while it fades out.
        outgoing_bin = np.minimum(previous_age + 1 + age, AGE_BINS - 1)
    else:
        outgoing_bin = np.minimum(previous_age, AGE_BINS - 1)
    outgoing = table[previous, outgoing_bin]

    if duration_frames <= 0.0:
        return current
    fraction = np.clip(age / duration_frames, 0.0, 1.0)
    weight = profile(fraction)[:, None]
    return (1.0 - weight) * outgoing + weight * current


def evaluate_all(records: list[dict]) -> dict:
    fit = gather(records, "fit")
    evaluate = gather(records, "heldout")
    truth = evaluate["continuous"]
    table = winner_age_table(fit)

    transition = evaluate["age"] < limits.TRANSITION_HORIZON_FRAMES
    everything = np.ones(len(truth), bool)

    baseline = crossfade_prediction(
        evaluate, table, 0.0, lambda u: u, False
    )
    baseline_score = {
        "all": limits.score(truth, baseline, everything),
        "transition": limits.score(truth, baseline, transition),
    }

    # The free lookup-table floor, recomputed here so both numbers come from
    # one code path and are directly comparable.
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
            "ageBin": np.minimum(evaluate["age"], AGE_BINS - 1),
        },
        "prev_winner_age",
    )
    floor_score = {
        "all": limits.score(truth, floor_prediction, everything),
        "transition": limits.score(truth, floor_prediction, transition),
    }

    sweep = []
    for name, profile in blend_profiles().items():
        for running in (False, True):
            for duration_ms in DURATION_MS:
                duration_frames = duration_ms / 1e3 / FRAME_SECONDS
                prediction = crossfade_prediction(
                    evaluate, table, duration_frames, profile, running
                )
                sweep.append(
                    {
                        "profile": name,
                        "runningPrevious": running,
                        "durationMs": duration_ms,
                        "all": limits.score(truth, prediction, everything),
                        "transition": limits.score(
                            truth, prediction, transition
                        ),
                    }
                )

    best = min(sweep, key=lambda entry: entry["all"]["mse"])
    best_transition = min(
        sweep, key=lambda entry: entry["transition"]["mse"]
    )
    closed = (
        1.0
        - (best["all"]["mse"] - floor_score["all"]["mse"])
        / (baseline_score["all"]["mse"] - floor_score["all"]["mse"])
    )
    return {
        "baseline": baseline_score,
        "floor": floor_score,
        "sweep": sweep,
        "best": best,
        "bestTransition": best_transition,
        "gapClosedFraction": float(closed),
    }


def report(result: dict) -> str:
    lines = ["# Transition crossfade against the (prev, current, age) floor\n"]
    base = result["baseline"]
    floor = result["floor"]
    lines.append(
        f"- No-prev baseline (winner + age only): RMSE "
        f"{base['all']['rmse']:.5f} all, "
        f"{base['transition']['rmse']:.5f} transition"
    )
    lines.append(
        f"- Lookup-table floor (prev + current + age): RMSE "
        f"{floor['all']['rmse']:.5f} all, "
        f"{floor['transition']['rmse']:.5f} transition\n"
    )

    lines.append("## Best crossfade\n")
    best = result["best"]
    lines.append(
        f"- **{best['profile']}**, duration **{best['durationMs']:.1f} ms**, "
        f"outgoing state {'running' if best['runningPrevious'] else 'frozen'}"
    )
    lines.append(
        f"- RMSE {best['all']['rmse']:.5f} all, "
        f"{best['transition']['rmse']:.5f} transition"
    )
    lines.append(
        f"- Closes **{result['gapClosedFraction'] * 100:.1f}%** of the "
        f"baseline-to-floor gap\n"
    )

    lines.append("## Duration sweep (frozen outgoing state)\n")
    lines.append("| profile | duration ms | RMSE all | RMSE transition |")
    lines.append("|---|---|---|---|")
    for entry in result["sweep"]:
        if entry["runningPrevious"]:
            continue
        lines.append(
            f"| {entry['profile']} | {entry['durationMs']:.1f} "
            f"| {entry['all']['rmse']:.5f} "
            f"| {entry['transition']['rmse']:.5f} |"
        )
    lines.append("")
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=limits.DEFAULT_CACHE)
    parser.add_argument(
        "--json", type=Path,
        default=Path(__file__).with_name("transition_crossfade.json"),
    )
    parser.add_argument(
        "--markdown", type=Path,
        default=Path(__file__).with_name("transition_crossfade.md"),
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
