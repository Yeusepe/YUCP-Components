#!/usr/bin/env python3
"""Measure what the hard Viseme index can and cannot recover.

Two questions, both answered against the reviewed Oculus extraction cache
(continuous 15-simplex trajectories paired with the argmax winner sequence
VRChat actually transmits):

  B. Identifiability. VRChat destroys the continuous shape and keeps only the
     argmax. Argmax switches are level crossings of pairwise differences, so
     time-encoding theory (Lazar/Toth) says the trajectory is recoverable from
     crossing times alone when consecutive crossings are closer than the
     Nyquist interval of the underlying signal. Compare the measured switch
     rate against twice the measured trajectory bandwidth.

  G. Error floor. Conditional expectation is the MMSE estimator for a given
     sigma-algebra, so the empirical conditional mean over a causal statistic
     is the exact floor for every estimator measurable with respect to it.
     Evaluate a ladder of statistics to see what each additional conditioning
     term actually buys, and where the floor stops moving.

Fit cells on the 'fit' split, report on 'heldout'. No new dependencies.
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path

import numpy as np

VISEME_COUNT = 15
VISEME_NAMES = (
    "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS",
    "nn", "RR", "aa", "E", "I", "O", "U",
)

# OVRLipSync analysis block: 1024 samples at 48 kHz.
ANALYSIS_SAMPLE_RATE = 48_000
ANALYSIS_BUFFER_SAMPLES = 1_024
FRAME_SECONDS = ANALYSIS_BUFFER_SAMPLES / ANALYSIS_SAMPLE_RATE
FRAME_RATE_HZ = 1.0 / FRAME_SECONDS

DEFAULT_CACHE = (
    Path(os.environ.get("LOCALAPPDATA", "."))
    / "YUCP"
    / "AdvancedVisemeTraining"
    / "SPIRE_EMA_CORPUS"
    / "oculus_halo_continuous_extraction_v1.npz"
)

# Age bins in frames since the last winner change; the final bin absorbs every
# older frame. Ten frames is 213 ms, past any plausible transition window.
AGE_EDGES = tuple(range(11))
AGE_BINS = len(AGE_EDGES) + 1
# A cell must carry this many fit frames before it is trusted; sparser cells
# back off to the next coarser statistic instead of memorising noise.
MIN_CELL_FRAMES = 40
TRANSITION_HORIZON_FRAMES = 5  # ~107 ms, the region where stair-steps live.


def load_utterances(cache_path: Path) -> list[dict]:
    if not cache_path.is_file():
        raise FileNotFoundError(
            f"Missing Oculus extraction cache: {cache_path}. "
            "Run train_oculus_viseme_halo.py train first."
        )
    with np.load(cache_path, allow_pickle=False, max_header_size=1 << 20) as data:
        offsets = np.asarray(data["offsets"], dtype=np.int64)
        continuous = np.asarray(data["continuous"], dtype=np.float64)
        winners = np.asarray(data["winners"], dtype=np.int64)
        splits = np.asarray(data["splits"]).astype(str)

    if continuous.shape != (int(offsets[-1]), VISEME_COUNT):
        raise ValueError("Continuous tensor is malformed")
    if winners.shape != (int(offsets[-1]),):
        raise ValueError("Winner sequence is malformed")

    records = []
    for index, split in enumerate(splits):
        start, end = int(offsets[index]), int(offsets[index + 1])
        if end - start < 8:
            continue
        records.append(
            {
                "split": str(split),
                "continuous": continuous[start:end],
                "winners": winners[start:end],
            }
        )
    return records


# --------------------------------------------------------------------------
# B. Identifiability
# --------------------------------------------------------------------------

def dwell_runs(winners: np.ndarray) -> np.ndarray:
    """Lengths, in frames, of maximal constant-winner runs."""
    if len(winners) == 0:
        return np.zeros(0, dtype=np.int64)
    change = np.flatnonzero(np.diff(winners)) + 1
    bounds = np.concatenate(([0], change, [len(winners)]))
    return np.diff(bounds)


def welch_psd(signal: np.ndarray, segment: int) -> np.ndarray:
    """One-sided periodogram averaged over Hann-windowed half-overlap blocks."""
    if len(signal) < segment:
        return np.zeros(0)
    window = np.hanning(segment)
    norm = (window ** 2).sum()
    step = segment // 2
    accum = None
    count = 0
    for start in range(0, len(signal) - segment + 1, step):
        block = signal[start:start + segment]
        block = block - block.mean()
        spectrum = np.fft.rfft(block * window)
        power = (spectrum.real ** 2 + spectrum.imag ** 2) / norm
        accum = power if accum is None else accum + power
        count += 1
    if accum is None or count == 0:
        return np.zeros(0)
    return accum / count


def measure_bandwidth(records: list[dict], segment: int = 64) -> dict:
    """Pooled PSD of the continuous channels and its energy quantiles."""
    total = None
    freqs = np.fft.rfftfreq(segment, d=FRAME_SECONDS)
    for record in records:
        continuous = record["continuous"]
        for channel in range(VISEME_COUNT):
            power = welch_psd(continuous[:, channel], segment)
            if power.size == 0:
                continue
            total = power if total is None else total + power
    if total is None:
        raise ValueError("No utterance was long enough to estimate a spectrum")

    # Drop DC: the static mean carries no motion and would dominate.
    spectrum = total[1:]
    axis = freqs[1:]
    cumulative = np.cumsum(spectrum) / spectrum.sum()

    def quantile(fraction: float) -> float:
        index = int(np.searchsorted(cumulative, fraction))
        return float(axis[min(index, len(axis) - 1)])

    return {
        "frequenciesHz": axis.tolist(),
        "cumulativeEnergy": cumulative.tolist(),
        "f50Hz": quantile(0.50),
        "f90Hz": quantile(0.90),
        "f95Hz": quantile(0.95),
        "f99Hz": quantile(0.99),
        "nyquistHz": FRAME_RATE_HZ / 2.0,
    }


def identifiability(records: list[dict]) -> dict:
    runs = np.concatenate([dwell_runs(r["winners"]) for r in records])
    frames = sum(len(r["winners"]) for r in records)
    switches = sum(int(np.count_nonzero(np.diff(r["winners"]))) for r in records)
    seconds = frames * FRAME_SECONDS
    switch_rate = switches / seconds

    # Silence dominates the corpus and inflates dwell; report speech-only too.
    speech_runs = np.concatenate(
        [
            dwell_runs(r["winners"])[
                np.asarray(
                    [
                        r["winners"][idx] != 0
                        for idx in np.concatenate(
                            ([0], np.flatnonzero(np.diff(r["winners"])) + 1)
                        )
                    ]
                )
            ]
            for r in records
        ]
    )

    band = measure_bandwidth(records)
    nyquist_rate_95 = 2.0 * band["f95Hz"]
    nyquist_rate_99 = 2.0 * band["f99Hz"]

    # Invert the density condition: the switch rate supports reconstruction up
    # to B* = rate/2, so the energy below B* is the share of the trajectory the
    # token stream can determine at all. The remainder needs measurement.
    identifiable_hz = switch_rate / 2.0
    identifiable_energy = float(
        np.interp(
            identifiable_hz,
            np.asarray(band["frequenciesHz"]),
            np.asarray(band["cumulativeEnergy"]),
        )
    )

    return {
        "frames": int(frames),
        "seconds": float(seconds),
        "switches": int(switches),
        "switchRateHz": float(switch_rate),
        "dwellFramesMean": float(runs.mean()),
        "dwellMsMean": float(runs.mean() * FRAME_SECONDS * 1e3),
        "dwellMsMedian": float(np.median(runs) * FRAME_SECONDS * 1e3),
        "dwellMsP10": float(np.percentile(runs, 10) * FRAME_SECONDS * 1e3),
        "dwellMsP90": float(np.percentile(runs, 90) * FRAME_SECONDS * 1e3),
        "speechDwellMsMean": float(speech_runs.mean() * FRAME_SECONDS * 1e3),
        "speechDwellMsMedian": float(
            np.median(speech_runs) * FRAME_SECONDS * 1e3
        ),
        "bandwidth": band,
        "nyquistRate95Hz": float(nyquist_rate_95),
        "nyquistRate99Hz": float(nyquist_rate_99),
        "densityRatio95": float(switch_rate / nyquist_rate_95),
        "densityRatio99": float(switch_rate / nyquist_rate_99),
        "identifiableBandwidthHz": float(identifiable_hz),
        "identifiableEnergyFraction": identifiable_energy,
    }


# --------------------------------------------------------------------------
# G. Conditional-mean error floor
# --------------------------------------------------------------------------

def utterance_features(record: dict) -> dict:
    winners = record["winners"]
    length = len(winners)
    previous = np.empty(length, dtype=np.int64)
    age = np.empty(length, dtype=np.int64)

    previous[0] = winners[0]
    age[0] = 0
    for index in range(1, length):
        if winners[index] != winners[index - 1]:
            previous[index] = winners[index - 1]
            age[index] = 0
        else:
            previous[index] = previous[index - 1]
            age[index] = age[index - 1] + 1

    age_bin = np.minimum(age, AGE_BINS - 1)
    return {
        "continuous": record["continuous"],
        "winner": winners,
        "previous": previous,
        "age": age,
        "ageBin": age_bin,
    }


def gather(records: list[dict], split: str) -> dict:
    parts = [utterance_features(r) for r in records if r["split"] == split]
    if not parts:
        raise ValueError(f"Split '{split}' is empty")
    return {
        key: np.concatenate([p[key] for p in parts], axis=0)
        for key in ("continuous", "winner", "previous", "age", "ageBin")
    }


def cell_means(keys: np.ndarray, values: np.ndarray, cells: int) -> tuple:
    """Per-cell mean and count; empty cells return zeros."""
    counts = np.bincount(keys, minlength=cells).astype(np.float64)
    sums = np.zeros((cells, values.shape[1]), dtype=np.float64)
    np.add.at(sums, keys, values)
    safe = np.maximum(counts, 1.0)[:, None]
    return sums / safe, counts


def predict(fit: dict, evaluate: dict, statistic: str) -> np.ndarray:
    """Conditional mean over `statistic`, backing off on sparse cells."""
    target = fit["continuous"]

    def keys_for(source: dict, name: str) -> tuple[np.ndarray, int]:
        winner = source["winner"]
        previous = source["previous"]
        age_bin = source["ageBin"]
        if name == "winner":
            return winner, VISEME_COUNT
        if name == "winner_age":
            return winner * AGE_BINS + age_bin, VISEME_COUNT * AGE_BINS
        if name == "prev_winner":
            return previous * VISEME_COUNT + winner, VISEME_COUNT ** 2
        if name == "prev_winner_age":
            return (
                (previous * VISEME_COUNT + winner) * AGE_BINS + age_bin,
                (VISEME_COUNT ** 2) * AGE_BINS,
            )
        raise ValueError(name)

    if statistic == "constant":
        return np.broadcast_to(
            target.mean(axis=0), (len(evaluate["winner"]), VISEME_COUNT)
        ).copy()

    ladder = {
        "winner": ["winner"],
        "winner_age": ["winner_age", "winner"],
        "prev_winner": ["prev_winner", "winner"],
        "prev_winner_age": ["prev_winner_age", "prev_winner", "winner"],
    }[statistic]

    prediction = np.broadcast_to(
        target.mean(axis=0), (len(evaluate["winner"]), VISEME_COUNT)
    ).copy()
    # Walk coarse -> fine so a dense fine cell overwrites its coarse backoff.
    for name in reversed(ladder):
        fit_keys, cells = keys_for(fit, name)
        eval_keys, _ = keys_for(evaluate, name)
        means, counts = cell_means(fit_keys, target, cells)
        trusted = counts >= MIN_CELL_FRAMES
        mask = trusted[eval_keys]
        prediction[mask] = means[eval_keys[mask]]
    return prediction


def score(truth: np.ndarray, prediction: np.ndarray, mask: np.ndarray) -> dict:
    if not np.any(mask):
        return {"rmse": float("nan"), "r2": float("nan"), "frames": 0}
    residual = truth[mask] - prediction[mask]
    mse = float((residual ** 2).mean())
    variance = float(((truth[mask] - truth[mask].mean(axis=0)) ** 2).mean())
    return {
        "rmse": float(np.sqrt(mse)),
        "mse": mse,
        "r2": float(1.0 - mse / variance) if variance > 0 else float("nan"),
        "frames": int(np.count_nonzero(mask)),
    }


def error_floor(records: list[dict]) -> dict:
    fit = gather(records, "fit")
    evaluate = gather(records, "heldout")
    truth = evaluate["continuous"]

    transition = evaluate["age"] < TRANSITION_HORIZON_FRAMES
    steady = ~transition

    statistics = [
        ("constant", "global mean (no information)"),
        ("winner", "current winner only (today's static halo row)"),
        ("winner_age", "winner + time since switch"),
        ("prev_winner", "previous + current winner (retention pair)"),
        ("prev_winner_age", "previous + current + age (HSMM statistic)"),
    ]

    results = []
    for name, description in statistics:
        prediction = predict(fit, evaluate, name)
        results.append(
            {
                "statistic": name,
                "description": description,
                "all": score(truth, prediction, np.ones(len(truth), bool)),
                "transition": score(truth, prediction, transition),
                "steady": score(truth, prediction, steady),
            }
        )
    return {
        "fitFrames": int(len(fit["winner"])),
        "evalFrames": int(len(truth)),
        "transitionFrames": int(np.count_nonzero(transition)),
        "transitionHorizonMs": TRANSITION_HORIZON_FRAMES * FRAME_SECONDS * 1e3,
        "ladder": results,
    }


# --------------------------------------------------------------------------

def report(identity: dict, floor: dict) -> str:
    lines = []
    lines.append("# Advanced Viseme reconstruction limits\n")
    lines.append(
        f"Corpus: {identity['frames']} frames "
        f"({identity['seconds']:.1f} s) at {FRAME_RATE_HZ:.3f} Hz "
        f"({FRAME_SECONDS * 1e3:.2f} ms per frame).\n"
    )

    lines.append("## B. Identifiability from switch times\n")
    band = identity["bandwidth"]
    lines.append(f"- Winner switches: {identity['switches']}")
    lines.append(f"- Switch rate: **{identity['switchRateHz']:.2f} Hz**")
    lines.append(
        f"- Dwell: mean {identity['dwellMsMean']:.1f} ms, "
        f"median {identity['dwellMsMedian']:.1f} ms "
        f"(p10 {identity['dwellMsP10']:.1f}, p90 {identity['dwellMsP90']:.1f})"
    )
    lines.append(
        f"- Dwell excluding silence: mean "
        f"{identity['speechDwellMsMean']:.1f} ms, median "
        f"{identity['speechDwellMsMedian']:.1f} ms"
    )
    lines.append(
        f"- Trajectory bandwidth: f50 {band['f50Hz']:.2f} Hz, "
        f"f90 {band['f90Hz']:.2f} Hz, f95 {band['f95Hz']:.2f} Hz, "
        f"f99 {band['f99Hz']:.2f} Hz"
    )
    lines.append(
        f"- Nyquist rate at f95: {identity['nyquistRate95Hz']:.2f} Hz -> "
        f"density ratio **{identity['densityRatio95']:.2f}x**"
    )
    lines.append(
        f"- Nyquist rate at f99: {identity['nyquistRate99Hz']:.2f} Hz -> "
        f"density ratio **{identity['densityRatio99']:.2f}x**\n"
    )
    verdict = (
        "switch times are dense enough to determine the trajectory"
        if identity["densityRatio95"] >= 1.0
        else "switch times are TOO SPARSE to determine the full trajectory"
    )
    lines.append(f"Verdict (95% energy): {verdict}.\n")
    lines.append(
        f"Identifiable band: the {identity['switchRateHz']:.2f} Hz switch rate "
        f"supports reconstruction to **{identity['identifiableBandwidthHz']:.2f} Hz**, "
        f"which carries **{identity['identifiableEnergyFraction'] * 100:.1f}%** of "
        f"the trajectory energy. The remaining "
        f"{(1 - identity['identifiableEnergyFraction']) * 100:.1f}% is faster than "
        f"the token stream can express and needs real tracking.\n"
    )

    lines.append("## G. Conditional-mean error floor (held-out)\n")
    lines.append(
        f"Transition frames = age < {floor['transitionHorizonMs']:.0f} ms "
        f"({floor['transitionFrames']} of {floor['evalFrames']}).\n"
    )
    lines.append(
        "| conditioning statistic | RMSE all | R2 all | RMSE transition "
        "| RMSE steady |"
    )
    lines.append("|---|---|---|---|---|")
    for entry in floor["ladder"]:
        lines.append(
            f"| {entry['description']} | {entry['all']['rmse']:.5f} "
            f"| {entry['all']['r2']:.4f} "
            f"| {entry['transition']['rmse']:.5f} "
            f"| {entry['steady']['rmse']:.5f} |"
        )
    lines.append("")

    base = next(e for e in floor["ladder"] if e["statistic"] == "winner")
    best = next(
        e for e in floor["ladder"] if e["statistic"] == "prev_winner_age"
    )
    gain = 1.0 - best["all"]["mse"] / base["all"]["mse"]
    gain_transition = (
        1.0 - best["transition"]["mse"] / base["transition"]["mse"]
    )
    lines.append(
        f"Headroom over today's static per-winner row: "
        f"**{gain * 100:.1f}% MSE overall**, "
        f"**{gain_transition * 100:.1f}% MSE in transitions**.\n"
    )
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=DEFAULT_CACHE)
    parser.add_argument(
        "--json", type=Path,
        default=Path(__file__).with_name("reconstruction_limits.json"),
    )
    parser.add_argument(
        "--markdown", type=Path,
        default=Path(__file__).with_name("reconstruction_limits.md"),
    )
    args = parser.parse_args(argv)

    records = load_utterances(args.cache)
    identity = identifiability(records)
    floor = error_floor(records)

    args.json.write_text(
        json.dumps(
            {"identifiability": identity, "errorFloor": floor},
            indent=2, sort_keys=True,
        ),
        encoding="utf-8",
    )
    text = report(identity, floor)
    args.markdown.write_text(text, encoding="utf-8")
    print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
