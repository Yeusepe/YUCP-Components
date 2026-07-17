#!/usr/bin/env python3
"""Numerically compare compact AVR beta-retention realizations.

This is an offline research harness.  It does not modify package assets.  It
parses the generated transition-retention table, reproduces the Animator's
piecewise-linear frame-time alpha lookup, and compares:

* the current dense bilinear observer, c.T @ R @ f;
* isolated three-exponential transition kernels truncated to K recent events;
* a stronger event-state (second-order/Volterra) K-event realization;
* two exact switched realizations; and
* shared-right-subspace low-rank weighted-automaton approximations.

The generated Markdown and JSON files are analysis artifacts, not production
inputs.
"""

from __future__ import annotations

import argparse
import json
import math
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Mapping, MutableMapping, Sequence, Tuple

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
MODEL_PATH = ROOT / (
    "Packages/com.yucp.components/Runtime/Components/Data/Generated/"
    "AdvancedVisemeTransitionRetention.generated.cs"
)
REPORT_PATH = Path(__file__).with_name("transition_kernel_report.md")
JSON_PATH = Path(__file__).with_name("transition_kernel_metrics.json")

VISEME_NAMES = (
    "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS",
    "nn", "RR", "aa", "E", "I", "O", "U",
)
GROUP_NAMES = ("Jaw", "Lips", "TongueTip", "TongueBody")
FPS_VALUES = (15, 30, 60, 90, 144)
ALPHA_SAMPLE_TIMES = np.asarray(
    (0.0, 1 / 240, 1 / 144, 1 / 90, 1 / 60,
     1 / 45, 1 / 30, 1 / 20, 0.1, 0.25),
    dtype=np.float64,
)
FAST_RESPONSE_SECONDS = 0.024
EYE = np.eye(15, dtype=np.float64)
DIFFERENCE = EYE[:, None, :] - EYE[None, :, :]


def _array_from_csharp(text: str, name: str) -> np.ndarray:
    match = re.search(
        rf"\b{name}\s*=\s*\{{(?P<body>.*?)\}}\s*;",
        text,
        flags=re.DOTALL,
    )
    if not match:
        raise RuntimeError(f"Could not find C# array {name} in {MODEL_PATH}")
    body = re.sub(r"//[^\r\n]*", "", match.group("body"))
    numbers = re.findall(
        r"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?[fF]?",
        body,
    )
    return np.asarray([float(value.rstrip("fF")) for value in numbers])


def load_model() -> Tuple[np.ndarray, np.ndarray]:
    text = MODEL_PATH.read_text(encoding="utf-8")
    decay = _array_from_csharp(text, "DecaySecondValues")
    values = _array_from_csharp(text, "RetentionValues")
    expected = len(GROUP_NAMES) * len(VISEME_NAMES) ** 2
    if decay.size != len(GROUP_NAMES) or values.size != expected:
        raise RuntimeError(
            f"Unexpected generated model shape: decay={decay.size}, "
            f"retention={values.size}, expected={expected}"
        )
    return values.reshape(len(GROUP_NAMES), len(VISEME_NAMES), len(VISEME_NAMES)), decay


def animator_alpha(delta_time: float, response_seconds: float) -> float:
    """Match AlphaFromDeltaTime's sampled Simple1D interpolation."""
    samples = 1.0 - np.exp(-ALPHA_SAMPLE_TIMES / response_seconds)
    return float(np.interp(delta_time, ALPHA_SAMPLE_TIMES, samples))


def one_hot(index: int, dtype=np.float64) -> np.ndarray:
    if dtype == np.float64:
        return EYE[index].copy()
    return EYE[index].astype(dtype, copy=True)


@dataclass(frozen=True)
class Trace:
    category: str
    fps: int
    name: str
    values: np.ndarray


def _different_viseme(rng: np.random.Generator, current: int) -> int:
    # A deliberately broad alphabet distribution: silence is common, vowels are
    # modestly favored, and every consonant remains reachable.
    weights = np.asarray(
        (0.14, 0.06, 0.05, 0.04, 0.06, 0.05, 0.04, 0.06,
         0.07, 0.06, 0.09, 0.07, 0.06, 0.08, 0.07),
        dtype=np.float64,
    )
    weights[current] = 0.0
    weights /= weights.sum()
    return int(rng.choice(len(VISEME_NAMES), p=weights))


def random_trace(rng: np.random.Generator, fps: int, seconds: float, name: str) -> Trace:
    target_frames = int(round(seconds * fps))
    current = int(rng.integers(0, len(VISEME_NAMES)))
    values: List[int] = []
    while len(values) < target_frames:
        # Median 76 ms, long right tail, clamped to phoneme-like extremes.
        duration = float(np.clip(rng.lognormal(math.log(0.076), 0.62), 0.018, 0.42))
        hold = max(1, int(round(duration * fps)))
        values.extend([current] * hold)
        current = _different_viseme(rng, current)
    return Trace("random", fps, name, np.asarray(values[:target_frames], dtype=np.int16))


def alternating_trace(fps: int, p: int, q: int, hold: int, ordinal: int) -> Trace:
    warm = max(2, int(round(0.24 * fps)))
    values = [p] * warm
    for index in range(12):
        values.extend(([q] if index % 2 == 0 else [p]) * hold)
    values.extend([q] * warm)
    return Trace(
        "adversarial", fps,
        f"alt-{VISEME_NAMES[p]}-{VISEME_NAMES[q]}-h{hold}-{ordinal}",
        np.asarray(values, dtype=np.int16),
    )


def interruption_trace(fps: int, p: int, q: int, r: int, hold: int, ordinal: int) -> Trace:
    warm = max(2, int(round(0.20 * fps)))
    destination = max(1, int(round(0.075 * fps)))
    values = [p] * warm
    for _ in range(5):
        values.extend([q] * hold)
        values.extend([r] * destination)
        values.extend([p] * destination)
    values.extend([r] * warm)
    return Trace(
        "adversarial", fps,
        f"interrupt-{VISEME_NAMES[p]}-{VISEME_NAMES[q]}-{VISEME_NAMES[r]}-h{hold}-{ordinal}",
        np.asarray(values, dtype=np.int16),
    )


def cycle_trace(fps: int, triple: Sequence[int], hold: int, ordinal: int) -> Trace:
    warm = max(2, int(round(0.18 * fps)))
    values = [triple[0]] * warm
    for _ in range(9):
        for value in triple:
            values.extend([value] * hold)
    values.extend([triple[-1]] * warm)
    return Trace(
        "adversarial", fps,
        "cycle-" + "-".join(VISEME_NAMES[x] for x in triple) + f"-h{hold}-{ordinal}",
        np.asarray(values, dtype=np.int16),
    )


def build_traces(seed: int, quick: bool) -> List[Trace]:
    rng = np.random.default_rng(seed)
    traces: List[Trace] = []
    pair_count = 18 if quick else 48
    triple_count = 24 if quick else 80
    random_count = 3 if quick else 10
    random_seconds = 5.0 if quick else 10.0

    all_pairs = [(p, q) for p in range(15) for q in range(15) if p != q]
    all_triples = [
        (p, q, r) for p in range(15) for q in range(15) for r in range(15)
        if p != q and q != r and p != r
    ]
    for fps in FPS_VALUES:
        for index in range(random_count):
            traces.append(random_trace(rng, fps, random_seconds, f"random-{index}"))

        pair_order = rng.permutation(len(all_pairs))[:pair_count]
        holds = sorted(set((1, 2, max(1, round(0.03 * fps)),
                            max(1, round(0.06 * fps)), max(1, round(0.12 * fps)))))
        for ordinal, pair_index in enumerate(pair_order):
            p, q = all_pairs[int(pair_index)]
            for hold in holds:
                traces.append(alternating_trace(fps, p, q, int(hold), ordinal))

        triple_order = rng.permutation(len(all_triples))[:triple_count]
        interruption_holds = sorted(set((1, 2, max(1, round(0.025 * fps)))))
        cycle_holds = sorted(set((1, 2, max(1, round(0.04 * fps)))))
        for ordinal, triple_index in enumerate(triple_order):
            triple = all_triples[int(triple_index)]
            for hold in interruption_holds:
                traces.append(interruption_trace(fps, *triple, int(hold), ordinal))
            if ordinal < max(8, triple_count // 3):
                for hold in cycle_holds:
                    traces.append(cycle_trace(fps, triple, int(hold), ordinal))
    return traces


def training_moments(
    retention: np.ndarray,
    decay_seconds: np.ndarray,
    seed: int,
    quick: bool,
) -> Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    rng = np.random.default_rng(seed + 991)
    context_second = np.zeros((4, 15, 15), dtype=np.float64)
    fast_second = np.zeros((15, 15), dtype=np.float64)
    counts = np.zeros(4, dtype=np.int64)
    stored_contexts: List[np.ndarray] = []
    stored_fast: List[np.ndarray] = []
    per_fps = 2 if quick else 6
    seconds = 5.0 if quick else 9.0
    for fps in FPS_VALUES:
        af = 1.0 - animator_alpha(1.0 / fps, FAST_RESPONSE_SECONDS)
        ag = np.asarray([
            1.0 - animator_alpha(1.0 / fps, float(tau)) for tau in decay_seconds
        ])
        for index in range(per_fps):
            trace = random_trace(rng, fps, seconds, f"train-{index}").values
            f = one_hot(int(trace[0]))
            c = np.repeat(f[None, :], 4, axis=0)
            trace_contexts = np.zeros((len(trace), 4, 15), dtype=np.float64)
            trace_fast = np.zeros((len(trace), 15), dtype=np.float64)
            for frame, value in enumerate(trace):
                u = one_hot(int(value))
                f = af * f + (1.0 - af) * u
                c = ag[:, None] * c + (1.0 - ag[:, None]) * u
                trace_contexts[frame] = c
                trace_fast[frame] = f
                fast_second += np.outer(f, f)
                for group in range(4):
                    context_second[group] += np.outer(c[group], c[group])
                    counts[group] += 1
            stored_contexts.append(trace_contexts)
            stored_fast.append(trace_fast)
    # fast_second was added once per group-counting loop iteration, but its
    # absolute scale is irrelevant to whitening; normalize by actual samples.
    sample_count = int(counts[0])
    fast_second /= sample_count
    context_second /= counts[:, None, None]
    ridge = 1e-8
    fast_second += np.eye(15) * ridge
    context_second += np.eye(15)[None, :, :] * ridge
    return (
        context_second,
        fast_second,
        np.concatenate(stored_contexts, axis=0),
        np.concatenate(stored_fast, axis=0),
    )


def _sqrt_and_inverse(matrix: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    values, vectors = np.linalg.eigh((matrix + matrix.T) * 0.5)
    values = np.maximum(values, 1e-10)
    root = (vectors * np.sqrt(values)) @ vectors.T
    inverse = (vectors * (1.0 / np.sqrt(values))) @ vectors.T
    return root, inverse


def joint_right_approximations(
    retention: np.ndarray,
    ranks: Iterable[int],
    context_second: np.ndarray,
    fast_second: np.ndarray,
) -> Dict[str, np.ndarray]:
    result: Dict[str, np.ndarray] = {}
    stacked = retention.reshape(4 * 15, 15)
    u, singular, vt = np.linalg.svd(stacked, full_matrices=False)
    for rank in ranks:
        approx = (u[:, :rank] * singular[:rank]) @ vt[:rank]
        result[f"joint-svd-r{rank}"] = approx.reshape(4, 15, 15)

    fast_root, fast_inverse = _sqrt_and_inverse(fast_second)
    weighted_blocks = []
    context_roots: List[np.ndarray] = []
    context_inverses: List[np.ndarray] = []
    for group in range(4):
        c_root, c_inverse = _sqrt_and_inverse(context_second[group])
        context_roots.append(c_root)
        context_inverses.append(c_inverse)
        weighted_blocks.append(c_root @ retention[group] @ fast_root)
    weighted = np.vstack(weighted_blocks)
    u, singular, vt = np.linalg.svd(weighted, full_matrices=False)
    for rank in ranks:
        approx_weighted = (u[:, :rank] * singular[:rank]) @ vt[:rank]
        blocks = []
        for group in range(4):
            block = approx_weighted[group * 15:(group + 1) * 15]
            blocks.append(context_inverses[group] @ block @ fast_inverse)
        result[f"weighted-joint-svd-r{rank}"] = np.asarray(blocks)
    return result


DECAY_GROUPS = ((0, 2), (1, 3))


def decay_stack_svd_approximations(
    retention: np.ndarray,
    ranks: Iterable[int],
) -> Dict[str, np.ndarray]:
    """Share one right basis only between groups with the same context pole."""
    result = {f"decay-svd-r{rank}": np.zeros_like(retention) for rank in ranks}
    for groups in DECAY_GROUPS:
        stacked = np.vstack([retention[group] for group in groups])
        u, singular, vt = np.linalg.svd(stacked, full_matrices=False)
        for rank in ranks:
            approx = (u[:, :rank] * singular[:rank]) @ vt[:rank]
            for offset, group in enumerate(groups):
                result[f"decay-svd-r{rank}"][group] = approx[offset * 15:(offset + 1) * 15]
    return result


def _ridge15(features: np.ndarray, target: np.ndarray) -> np.ndarray:
    gram = features.T @ features
    scale = max(float(np.trace(gram)) / gram.shape[0], 1e-12)
    gram.flat[:: gram.shape[0] + 1] += 1e-6 * scale
    return np.linalg.solve(gram, features.T @ target)


def trajectory_weighted_decay_approximations(
    retention: np.ndarray,
    ranks: Sequence[int],
    contexts: np.ndarray,
    fast: np.ndarray,
    seed: int,
    quick: bool,
) -> Dict[str, np.ndarray]:
    """Greedy coupled-output low-rank fit on legal filtered trajectories.

    Each rank-one component is fitted to the remaining scalar output error, not
    to coefficient Frobenius error.  Groups sharing a context pole share the
    right/fast factor.  Alternating solves stay 15-dimensional, avoiding the
    ill-conditioned 15*r block normal equations of a conventional ALS fit.
    """
    rng = np.random.default_rng(seed + 1771)
    max_samples = 3500 if quick else 10000
    if len(fast) > max_samples:
        sample_indices = np.sort(rng.choice(len(fast), max_samples, replace=False))
        contexts = contexts[sample_indices]
        fast = fast[sample_indices]

    max_rank = max(ranks)
    output = {
        f"trajectory-decay-r{rank}": np.zeros_like(retention) for rank in ranks
    }
    for groups in DECAY_GROUPS:
        targets = {
            group: np.einsum(
                "ni,ij,nj->n", contexts[:, group, :], retention[group], fast,
                optimize=True,
            )
            for group in groups
        }
        residual_outputs = {group: targets[group].copy() for group in groups}
        coefficient_residual = np.vstack([retention[group] for group in groups])
        a_components: Dict[int, List[np.ndarray]] = {group: [] for group in groups}
        b_components: List[np.ndarray] = []

        for component in range(max_rank):
            # A coefficient-SVD direction is only an initializer.  Every solve
            # below is weighted by the coupled legal (c,f) trajectories.
            _, _, vt = np.linalg.svd(coefficient_residual, full_matrices=False)
            b = vt[0].copy()
            a_values = {group: np.zeros(15, dtype=np.float64) for group in groups}
            iterations = 5 if quick else 8
            for _ in range(iterations):
                fast_coordinate = fast @ b
                for group in groups:
                    features = contexts[:, group, :] * fast_coordinate[:, None]
                    a_values[group] = _ridge15(features, residual_outputs[group])

                shared_features = []
                shared_targets = []
                for group in groups:
                    context_coordinate = contexts[:, group, :] @ a_values[group]
                    shared_features.append(fast * context_coordinate[:, None])
                    shared_targets.append(residual_outputs[group])
                b = _ridge15(np.vstack(shared_features), np.concatenate(shared_targets))
                norm = float(np.linalg.norm(b))
                if norm < 1e-12:
                    break
                b /= norm
                for group in groups:
                    a_values[group] *= norm

            b_components.append(b.copy())
            for group in groups:
                a_components[group].append(a_values[group].copy())
                contribution = (
                    (contexts[:, group, :] @ a_values[group]) * (fast @ b)
                )
                residual_outputs[group] -= contribution

            coefficient_residual = np.vstack([
                retention[group] - sum(
                    np.outer(a_components[group][index], b_components[index])
                    for index in range(len(b_components))
                )
                for group in groups
            ])
            rank = component + 1
            if rank in ranks:
                name = f"trajectory-decay-r{rank}"
                for group in groups:
                    output[name][group] = sum(
                        np.outer(a_components[group][index], b_components[index])
                        for index in range(rank)
                    )
    return output


def sparse_residual_corrections(
    retention: np.ndarray,
    approximations: Mapping[str, np.ndarray],
    ranks: Sequence[int],
    source_prefix: str,
    threshold: float = 0.01,
) -> Tuple[Dict[str, np.ndarray], Dict[int, dict]]:
    """Correct coefficients above threshold for a universal simplex bound.

    Because c_i*f_j are nonnegative and sum to one, leaving every coefficient
    residual in [-threshold,+threshold] proves |c.T E f| <= threshold for every
    legal pair of simplex states, not only for sampled trajectories.
    """
    corrected: Dict[str, np.ndarray] = {}
    metadata: Dict[int, dict] = {}
    for rank in ranks:
        source_name = f"{source_prefix}-r{rank}"
        source = approximations[source_name]
        residual = retention - source
        mask = np.abs(residual) > threshold
        model = source + np.where(mask, residual, 0.0)
        name = f"{source_prefix}-r{rank}-sparse01"
        corrected[name] = model
        remaining = retention - model
        metadata[rank] = {
            "correction_count": int(np.count_nonzero(mask)),
            "remaining_coefficient_max": float(np.max(np.abs(remaining))),
            "universal_output_bound": threshold,
        }
    return corrected, metadata


def simulate_trace(
    trace: Trace,
    retention: np.ndarray,
    decay_seconds: np.ndarray,
    approximations: Mapping[str, np.ndarray],
) -> Tuple[
    np.ndarray,
    Dict[str, np.ndarray],
    np.ndarray,
    np.ndarray,
    np.ndarray,
    Dict[str, np.ndarray],
]:
    values = trace.values
    count = len(values)
    dt = 1.0 / trace.fps
    af = 1.0 - animator_alpha(dt, FAST_RESPONSE_SECONDS)
    ag = np.asarray([
        1.0 - animator_alpha(dt, float(tau)) for tau in decay_seconds
    ])

    first = int(values[0])
    f = one_hot(first)
    c = np.repeat(f[None, :], 4, axis=0)
    # Exact switched scalar recurrence state y = c.T R f.
    switched = np.asarray([retention[g, first, first] for g in range(4)])
    # Exact commuted projection z = c.T R, maintained without c->R projection.
    projected = retention[:, first, :].copy()
    # Explicit same-Direct-state staging model.  Current generated math updates
    # c, then separately projects c->z, then separately contracts z with f; each
    # sibling samples the parameter values present at evaluation start.
    staged_c = np.repeat(f[None, :], 4, axis=0)
    staged_f = f.copy()
    staged_projected = retention[:, first, :].copy()
    direct_projected = retention[:, first, :].copy()
    delayed_projected = retention[:, first, :].copy()
    events: List[Tuple[int, int, int]] = []
    previous = first

    reference = np.zeros((count, 4), dtype=np.float64)
    predictions: Dict[str, np.ndarray] = {
        f"isolated-k{k}": np.zeros((count, 4), dtype=np.float64) for k in range(1, 5)
    }
    predictions.update({
        f"volterra-k{k}": np.zeros((count, 4), dtype=np.float64) for k in range(1, 5)
    })
    predictions["exact-switched-recurrence"] = np.zeros((count, 4), dtype=np.float64)
    predictions["exact-commuted-projection"] = np.zeros((count, 4), dtype=np.float64)
    for name in approximations:
        predictions[name] = np.zeros((count, 4), dtype=np.float64)

    contexts = np.zeros((count, 4, 15), dtype=np.float64)
    fast_states = np.zeros((count, 15), dtype=np.float64)
    staging_reference = np.zeros((count, 4), dtype=np.float64)
    staging_predictions = {
        "direct-commuted": np.zeros((count, 4), dtype=np.float64),
        "copy-stage-preserving": np.zeros((count, 4), dtype=np.float64),
    }

    for frame, raw_value in enumerate(values):
        value = int(raw_value)
        if value != previous:
            events.append((previous, value, frame))
            previous = value
        u = EYE[value]

        # Outputs sampled from the pre-update parameters of this Animator frame.
        staging_reference[frame] = np.einsum(
            "gi,gi->g", staged_projected, np.repeat(staged_f[None, :], 4, axis=0)
        )
        staging_predictions["direct-commuted"][frame] = np.einsum(
            "gi,gi->g", direct_projected, np.repeat(staged_f[None, :], 4, axis=0)
        )
        staging_predictions["copy-stage-preserving"][frame] = np.einsum(
            "gi,gi->g", delayed_projected, np.repeat(staged_f[None, :], 4, axis=0)
        )
        next_staged_projected = np.einsum(
            "gi,gij->gj", staged_c, retention, optimize=True
        )
        next_direct_projected = (
            ag[:, None] * direct_projected
            + (1.0 - ag)[:, None] * retention[:, value, :]
        )
        next_delayed_projected = direct_projected.copy()
        staged_c = ag[:, None] * staged_c + (1.0 - ag)[:, None] * u
        staged_f = af * staged_f + (1.0 - af) * u
        staged_projected = next_staged_projected
        direct_projected = next_direct_projected
        delayed_projected = next_delayed_projected

        # Recurrence uses the previous observer state and current hard symbol.
        next_switched = np.empty(4, dtype=np.float64)
        for group in range(4):
            column = float(c[group] @ retention[group, :, value])
            row = float(retention[group, value, :] @ f)
            next_switched[group] = (
                ag[group] * af * switched[group]
                + ag[group] * (1.0 - af) * column
                + (1.0 - ag[group]) * af * row
                + (1.0 - ag[group]) * (1.0 - af) * retention[group, value, value]
            )

        f = af * f + (1.0 - af) * u
        c = ag[:, None] * c + (1.0 - ag[:, None]) * u
        projected = ag[:, None] * projected + (
            (1.0 - ag)[:, None] * retention[:, value, :]
        )
        switched = next_switched

        contexts[frame] = c
        fast_states[frame] = f
        for group in range(4):
            reference[frame, group] = float(c[group] @ retention[group] @ f)
            predictions["exact-switched-recurrence"][frame, group] = switched[group]
            predictions["exact-commuted-projection"][frame, group] = float(projected[group] @ f)

        # K-recent-event models. Event age is one on the first destination frame.
        # Accumulating newest-to-oldest produces every K prefix without redoing
        # the K-1 younger events.
        recent = list(reversed(events[-4:]))
        f_hat = u.copy()
        c_hat = np.repeat(u[None, :], 4, axis=0)
        additive = retention[:, value, value].copy()
        for event_index, (p, q, event_frame) in enumerate(recent):
            age = frame - event_frame + 1
            difference = DIFFERENCE[p, q]
            bf = af ** age
            f_hat += bf * difference
            isolated_f = EYE[q] + bf * difference
            for group in range(4):
                bg = ag[group] ** age
                c_hat[group] += bg * difference
                isolated_c = EYE[q] + bg * difference
                additive[group] += (
                    float(isolated_c @ retention[group] @ isolated_f)
                    - retention[group, q, q]
                )
            k = event_index + 1
            predictions[f"isolated-k{k}"][frame] = additive
            predictions[f"volterra-k{k}"][frame] = np.einsum(
                "gi,gij,j->g", c_hat, retention, f_hat, optimize=True
            )
        # Before K transitions exist, all larger histories are identical to the
        # complete available history.
        if len(recent) < 4:
            source_k = max(1, len(recent))
            if len(recent) == 0:
                baseline = retention[:, value, value]
                for k in range(1, 5):
                    predictions[f"isolated-k{k}"][frame] = baseline
                    predictions[f"volterra-k{k}"][frame] = baseline
            else:
                for k in range(source_k + 1, 5):
                    predictions[f"isolated-k{k}"][frame] = predictions[
                        f"isolated-k{source_k}"][frame]
                    predictions[f"volterra-k{k}"][frame] = predictions[
                        f"volterra-k{source_k}"][frame]

    for name, approx in approximations.items():
        predictions[name][:] = np.einsum(
            "ngi,gij,nj->ng", contexts, approx, fast_states, optimize=True
        )
    return (
        reference,
        predictions,
        contexts,
        fast_states,
        staging_reference,
        staging_predictions,
    )


class ErrorStore:
    def __init__(self) -> None:
        self.values: MutableMapping[str, MutableMapping[str, List[List[np.ndarray]]]] = defaultdict(
            lambda: defaultdict(lambda: [[] for _ in GROUP_NAMES])
        )
        self.worst: Dict[Tuple[str, int], dict] = {}

    def add(self, label: str, trace: Trace, reference: np.ndarray,
            predictions: Mapping[str, np.ndarray]) -> None:
        for method, prediction in predictions.items():
            absolute = np.abs(prediction - reference)
            for group in range(4):
                self.values[label][method][group].append(absolute[:, group].astype(np.float32))
                index = int(np.argmax(absolute[:, group]))
                error = float(absolute[index, group])
                key = (method, group)
                if error > self.worst.get(key, {}).get("error", -1.0):
                    self.worst[key] = {
                        "error": error,
                        "trace": trace.name,
                        "category": trace.category,
                        "fps": trace.fps,
                        "frame": index,
                        "viseme": VISEME_NAMES[int(trace.values[index])],
                        "reference": float(reference[index, group]),
                        "prediction": float(prediction[index, group]),
                    }

    def metrics(self, labels: Iterable[str], method: str, group: int) -> dict:
        arrays: List[np.ndarray] = []
        for label in labels:
            arrays.extend(self.values[label][method][group])
        values = np.concatenate(arrays).astype(np.float64) if arrays else np.asarray([], dtype=np.float64)
        if values.size == 0:
            return {"count": 0, "rms": math.nan, "p99": math.nan, "max": math.nan}
        return {
            "count": int(values.size),
            "rms": float(np.sqrt(np.mean(values * values))),
            "p99": float(np.quantile(values, 0.99)),
            "max": float(np.max(values)),
        }


def modal_projection_binding_count(retention: np.ndarray, groups: Sequence[int]) -> int:
    total = 0
    for group in groups:
        for current in range(15):
            values = [float(retention[group, previous, current]) for previous in range(15)]
            counts = Counter(values)
            # Match the builder's preference: maximum savings, then occurrence
            # count, with zero winning only an otherwise exact tie.
            baseline = sorted(
                counts,
                key=lambda value: (
                    -(counts[value] - (0 if value == 0.0 else 1)),
                    -counts[value],
                    0 if value == 0.0 else 1,
                ),
            )[0]
            total += sum(value != baseline for value in values)
            total += int(baseline != 0.0)
    return total


def cost_estimates(
    retention: np.ndarray,
    ranks: Sequence[int],
    sparse_metadata: Mapping[int, dict],
    svd_sparse_metadata: Mapping[int, dict],
) -> dict:
    # Runtime-active estimates assume a hard Viseme value selects one Simple1D
    # child.  Serialized graph size and transition-history bookkeeping are called
    # out separately in the report.
    dense_projection = (
        modal_projection_binding_count(retention, (0, 2))
        + modal_projection_binding_count(retention, (1, 3))
    )
    dense = {
        "active_curve_bindings": 120 + dense_projection + 124,
        "active_clip_references": 64 + 32 + 92,
        "context_state_floats": 90,
        "projection_curve_bindings": dense_projection,
        "notes": "30 context coordinates plus 60 staged projected coordinates; normal-speech path, shared alpha lookup, and downstream lead math excluded.",
    }
    commuted = {
        "active_curve_bindings": 4 * (30 + 14 + 16),
        "active_clip_references": 4 * (16 + 1 + 16),
        "context_state_floats": 60,
        "notes": "Exact mathematical c^T R state projection, but advances context one Animator frame versus the current three-sibling pipeline.",
    }
    commuted_stage_preserving = {
        "active_curve_bindings": 4 * (30 + 14 + 30 + 16),
        "active_clip_references": 4 * (16 + 1 + 16 + 16),
        "context_state_floats": 120,
        "notes": "Adds one 15-vector copy per group so same-Direct-state reads retain current c->z->r frame staging.",
    }
    switched = {
        "active_curve_bindings": 120 + 4 * (14 + 14 + 2),
        "active_clip_references": 64 + 4 * (14 + 14 + 2),
        "context_state_floats": 30,
        "scalar_retention_states": 4,
        "notes": "Exact scalar recurrence lower bound; coefficient lookup curves excluded.",
    }
    low_rank = {}
    decay_low_rank = {}
    sparse_low_rank = {}
    svd_sparse_low_rank = {}
    for rank in ranks:
        low_rank[str(rank)] = {
            "active_curve_bindings": 19 * rank + 4,
            "active_clip_references": 9 * rank + 14,
            "state_floats": 5 * rank,
            "notes": "Shared right basis: four context embeddings, one shared fast embedding, four dot products.",
        }
        decay_low_rank[str(rank)] = {
            "active_curve_bindings": 22 * rank + 4,
            "active_clip_references": 10 * rank + 16,
            "state_floats": 6 * rank,
            "notes": "Two right bases, one per context decay; four context and two fast embeddings.",
        }
        corrections = int(sparse_metadata[rank]["correction_count"])
        sparse_low_rank[str(rank)] = {
            "active_curve_bindings": 22 * rank + 4 + corrections,
            "active_clip_references": 10 * rank + 16 + corrections,
            "state_floats": 6 * rank,
            "sparse_corrections": corrections,
            "remaining_coefficient_max": sparse_metadata[rank]["remaining_coefficient_max"],
            "universal_output_bound": sparse_metadata[rank]["universal_output_bound"],
            "notes": "Decay-shared trajectory fit plus exact residual entries above 0.01.",
        }
        svd_corrections = int(svd_sparse_metadata[rank]["correction_count"])
        svd_sparse_low_rank[str(rank)] = {
            "active_curve_bindings": 22 * rank + 4 + svd_corrections,
            "active_clip_references": 10 * rank + 16 + svd_corrections,
            "state_floats": 6 * rank,
            "sparse_corrections": svd_corrections,
            "remaining_coefficient_max": svd_sparse_metadata[rank]["remaining_coefficient_max"],
            "universal_output_bound": svd_sparse_metadata[rank]["universal_output_bound"],
            "notes": "Decay-shared coefficient SVD plus exact residual entries above 0.01.",
        }
    event = {}
    for k in range(1, 5):
        event[str(k)] = {
            "isolated_unique_exponential_terms": 5 * k,
            "volterra_unique_exponential_terms": 2 * k * k + 3 * k,
            "notes": "Arithmetic basis only; hard transition lookup and K-slot history shifting are not included.",
        }
    return {
        "dense-current": dense,
        "exact-commuted-projection-direct": commuted,
        "exact-commuted-projection-stage-preserving": commuted_stage_preserving,
        "exact-switched-recurrence": switched,
        "joint-low-rank": low_rank,
        "decay-low-rank": decay_low_rank,
        "sparse-decay-low-rank": sparse_low_rank,
        "sparse-decay-svd": svd_sparse_low_rank,
        "event-kernels": event,
    }


def format_number(value: float) -> str:
    if value == 0:
        return "0"
    if abs(value) < 1e-4:
        return f"{value:.2e}"
    return f"{value:.6f}"


def write_reports(
    traces: Sequence[Trace],
    store: ErrorStore,
    methods: Sequence[str],
    costs: dict,
    ranks: Sequence[int],
    seed: int,
    staging_store: ErrorStore,
) -> None:
    labels = sorted(store.values)
    random_labels = [label for label in labels if label.startswith("random-")]
    adversarial_labels = [label for label in labels if label.startswith("adversarial-")]
    all_labels = labels

    metrics_json: dict = {
        "seed": seed,
        "trace_count": len(traces),
        "frame_count": int(sum(len(trace.values) for trace in traces)),
        "fps": list(FPS_VALUES),
        "groups": list(GROUP_NAMES),
        "metrics": {},
        "worst": {},
        "cost_estimates": costs,
        "animator_staging": {},
    }
    for scope, scope_labels in (
        ("all", all_labels), ("random", random_labels), ("adversarial", adversarial_labels)
    ):
        metrics_json["metrics"][scope] = {}
        for method in methods:
            metrics_json["metrics"][scope][method] = {
                GROUP_NAMES[group]: store.metrics(scope_labels, method, group)
                for group in range(4)
            }
    for fps in FPS_VALUES:
        scope = f"fps-{fps}"
        fps_labels = [label for label in labels if label.endswith(f"-{fps}")]
        metrics_json["metrics"][scope] = {}
        for method in methods:
            metrics_json["metrics"][scope][method] = {
                GROUP_NAMES[group]: store.metrics(fps_labels, method, group)
                for group in range(4)
            }
    for (method, group), worst in store.worst.items():
        metrics_json["worst"].setdefault(method, {})[GROUP_NAMES[group]] = worst
    for method in ("direct-commuted", "copy-stage-preserving"):
        metrics_json["animator_staging"][method] = {
            GROUP_NAMES[group]: staging_store.metrics(all_labels, method, group)
            for group in range(4)
        }

    JSON_PATH.write_text(json.dumps(metrics_json, indent=2), encoding="utf-8")

    lines: List[str] = []
    lines.append("# Advanced Viseme transition-kernel reduction experiment")
    lines.append("")
    lines.append(
        f"Deterministic seed `{seed}`; {len(traces):,} traces; "
        f"{metrics_json['frame_count']:,} frames; FPS {', '.join(map(str, FPS_VALUES))}."
    )
    lines.append("")
    lines.append(
        "The reference is the generated four-group retention table evaluated as "
        "`cᵀ R f`, with the same piecewise-linear frame-time alpha lookup emitted "
        "by `AlphaFromDeltaTime` and the default 24 ms fast-viseme pole. Errors "
        "are absolute normalized-retention errors. The transient-silence freeze "
        "gate is intentionally excluded; it is an orthogonal selector that can "
        "wrap every realization identically."
    )
    lines.append("")
    lines.append("## Aggregate error across random and adversarial streams")
    lines.append("")
    lines.append("| Method | Group | RMS | p99 | Max |")
    lines.append("|---|---:|---:|---:|---:|")
    for method in methods:
        for group in range(4):
            metric = metrics_json["metrics"]["all"][method][GROUP_NAMES[group]]
            lines.append(
                f"| `{method}` | {GROUP_NAMES[group]} | {format_number(metric['rms'])} | "
                f"{format_number(metric['p99'])} | {format_number(metric['max'])} |"
            )
    lines.append("")
    lines.append("## Random versus adversarial p99")
    lines.append("")
    lines.append("| Method | Group | Random p99 | Adversarial p99 |")
    lines.append("|---|---:|---:|---:|")
    for method in methods:
        for group in range(4):
            random_metric = metrics_json["metrics"]["random"][method][GROUP_NAMES[group]]
            adversarial_metric = metrics_json["metrics"]["adversarial"][method][GROUP_NAMES[group]]
            lines.append(
                f"| `{method}` | {GROUP_NAMES[group]} | {format_number(random_metric['p99'])} | "
                f"{format_number(adversarial_metric['p99'])} |"
            )
    lines.append("")
    lines.append("## Structural runtime cost estimate")
    lines.append("")
    lines.append(
        "These are active-path curve/clip counts for the beta-retention block only, "
        "not measured milliseconds. They exclude shared frame-time lookup, upstream "
        "fast-viseme observation, downstream lead arithmetic, and VRCFury rewrites."
    )
    lines.append("")
    lines.append("| Realization | Active curves | Active clips | Dynamic state | Relative curves |")
    lines.append("|---|---:|---:|---:|---:|")
    dense_curves = costs["dense-current"]["active_curve_bindings"]
    for key in (
        "dense-current",
        "exact-commuted-projection-direct",
        "exact-commuted-projection-stage-preserving",
        "exact-switched-recurrence",
    ):
        item = costs[key]
        state = item.get("context_state_floats", 0) + item.get("scalar_retention_states", 0)
        lines.append(
            f"| `{key}` | {item['active_curve_bindings']} | {item['active_clip_references']} | "
            f"{state} floats | {item['active_curve_bindings'] / dense_curves:.1%} |"
        )
    for rank in ranks:
        item = costs["decay-low-rank"][str(rank)]
        lines.append(
            f"| `trajectory-decay-r{rank}` | {item['active_curve_bindings']} | "
            f"{item['active_clip_references']} | {item['state_floats']} floats | "
            f"{item['active_curve_bindings'] / dense_curves:.1%} |"
        )
        sparse = costs["sparse-decay-low-rank"][str(rank)]
        lines.append(
            f"| `trajectory-decay-r{rank}-sparse01` | {sparse['active_curve_bindings']} | "
            f"{sparse['active_clip_references']} | {sparse['state_floats']} floats + "
            f"{sparse['sparse_corrections']} residuals | "
            f"{sparse['active_curve_bindings'] / dense_curves:.1%} |"
        )
        svd_sparse = costs["sparse-decay-svd"][str(rank)]
        lines.append(
            f"| `decay-svd-r{rank}-sparse01` | {svd_sparse['active_curve_bindings']} | "
            f"{svd_sparse['active_clip_references']} | {svd_sparse['state_floats']} floats + "
            f"{svd_sparse['sparse_corrections']} residuals | "
            f"{svd_sparse['active_curve_bindings'] / dense_curves:.1%} |"
        )
    lines.append("")
    lines.append("## Animator-frame staging replay")
    lines.append("")
    lines.append(
        "All current context update, context projection, and destination contraction "
        "motions are siblings in one Direct state. Under normal Animator feedback "
        "semantics they read the parameters present at evaluation start: `c` updates "
        "for the next frame, `z` sees the previous `c`, and retention sees the previous "
        "`z` and `f`. A direct projected-state EMA removes the `c -> z` pipeline delay."
    )
    lines.append("")
    lines.append("| Replacement | Group | RMS versus current staging | p99 | Max |")
    lines.append("|---|---:|---:|---:|---:|")
    for method in ("direct-commuted", "copy-stage-preserving"):
        for group in range(4):
            metric = metrics_json["animator_staging"][method][GROUP_NAMES[group]]
            lines.append(
                f"| `{method}` | {GROUP_NAMES[group]} | {format_number(metric['rms'])} | "
                f"{format_number(metric['p99'])} | {format_number(metric['max'])} |"
            )
    lines.append("")
    lines.append(
        "Therefore a strict replay-compatible replacement requires the extra projected-"
        "vector copy (or an experimentally verified equivalent layer boundary). The "
        "copy raises the estimate from 240 curves / 132 clips to 360 curves / 196 clips, "
        "still 64% fewer active curve bindings than the estimated current block. It has "
        "slightly more clip references, so the expected win specifically depends on the "
        "previously observed dense-curve sampling bottleneck and must be profiled."
    )
    lines.append("")
    lines.append("## Exact identities tested")
    lines.append("")
    lines.append("For hard symbol `v`, context decay `a`, and fast decay `d`:")
    lines.append("")
    lines.append("```text")
    lines.append("c' = a c + (1-a) e_v")
    lines.append("f' = d f + (1-d) e_v")
    lines.append("y' = ad y + a(1-d) cᵀR e_v + (1-a)d e_vᵀR f + (1-a)(1-d)R_vv")
    lines.append("z' = a z + (1-a) R[v,:],  where z = cᵀR;  y' = z' f'")
    lines.append("```")
    lines.append("")
    lines.append(
        "The first is an exact switched scalar recurrence. The second commutes the "
        "linear context observer through the matrix, so it updates one selected "
        "authored row instead of sampling the full dense tensor. Neither truncates "
        "history or changes the learned table."
    )
    lines.append("")
    lines.append("## Event-kernel basis size")
    lines.append("")
    lines.append("| K | Isolated exponentials | Volterra exponentials |")
    lines.append("|---:|---:|---:|")
    for k in range(1, 5):
        item = costs["event-kernels"][str(k)]
        lines.append(
            f"| {k} | {item['isolated_unique_exponential_terms']} | "
            f"{item['volterra_unique_exponential_terms']} |"
        )
    lines.append("")
    lines.append(
        "These counts are only the shared exponential arithmetic basis. An Animator "
        "implementation must also store/shift K transition identities and select "
        "the corresponding table coefficients, so they are not comparable to the "
        "curve counts above without a concrete compiler."
    )
    lines.append("")
    lines.append("## Sparse residual guarantee")
    lines.append("")
    lines.append(
        "For every trajectory-weighted decay-rank model, coefficients whose residual "
        "magnitude exceeds 0.01 are restored exactly. Since `c_i f_j >= 0` and "
        "`sum_ij c_i f_j = 1`, the remaining output error is a convex combination "
        "of coefficient residuals and is therefore universally at most 0.01 for "
        "any legal simplex states."
    )
    lines.append("")
    lines.append("| Family | Rank | Corrections | Remaining coefficient max | Active curves | Active clips | Curves below dense? |")
    lines.append("|---|---:|---:|---:|---:|---:|---:|")
    for family, key in (
        ("trajectory", "sparse-decay-low-rank"),
        ("coefficient SVD", "sparse-decay-svd"),
    ):
        for rank in ranks:
            sparse = costs[key][str(rank)]
            lines.append(
                f"| {family} | {rank} | {sparse['sparse_corrections']} | "
                f"{format_number(sparse['remaining_coefficient_max'])} | "
                f"{sparse['active_curve_bindings']} | {sparse['active_clip_references']} | "
                f"{'yes' if sparse['active_curve_bindings'] < dense_curves else 'no'} |"
            )
    lines.append("")
    lines.append("## Worst observed cases")
    lines.append("")
    lines.append("| Method | Group | Error | FPS | Trace | Frame | Ref | Pred |")
    lines.append("|---|---:|---:|---:|---|---:|---:|---:|")
    for method in methods:
        for group in range(4):
            worst = store.worst[(method, group)]
            lines.append(
                f"| `{method}` | {GROUP_NAMES[group]} | {format_number(worst['error'])} | "
                f"{worst['fps']} | `{worst['trace']}` | {worst['frame']} | "
                f"{format_number(worst['reference'])} | {format_number(worst['prediction'])} |"
            )
    lines.append("")
    lines.append("## Interpretation guardrails")
    lines.append("")
    lines.append(
        "- The exact rewrites should be implemented and profiled before accepting an "
        "approximation; they target the dense-curve bottleneck without behavioral error."
    )
    lines.append(
        "- A hard-viseme Simple1D selector is assumed to sample one threshold child. "
        "Unity/VRChat profiling must verify zero-weight branch pruning and the real "
        "cost after VRCFury flattening."
    )
    lines.append(
        "- Low-rank models need a perceptual acceptance threshold and avatar corpus "
        "validation; normalized retention error is not itself a mesh-space error."
    )
    lines.append(
        "- Animator float precision, transient-silence hold, and intra-frame parameter "
        "write ordering require a generated-controller equivalence test before shipping."
    )
    REPORT_PATH.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--seed", type=int, default=730241)
    parser.add_argument("--quick", action="store_true")
    args = parser.parse_args()

    retention, decay_seconds = load_model()
    ranks = (2, 4, 6, 8, 10, 12)
    context_second, fast_second, training_contexts, training_fast = training_moments(
        retention, decay_seconds, args.seed, args.quick
    )
    # Keep the second moments in the artifact's experimental provenance even
    # though the coupled trajectory fit below supersedes separable whitening.
    _ = (context_second, fast_second)
    approximations = decay_stack_svd_approximations(retention, ranks)
    approximations.update(trajectory_weighted_decay_approximations(
        retention, ranks, training_contexts, training_fast, args.seed, args.quick
    ))
    sparse_models, sparse_metadata = sparse_residual_corrections(
        retention, approximations, ranks, "trajectory-decay"
    )
    approximations.update(sparse_models)
    svd_sparse_models, svd_sparse_metadata = sparse_residual_corrections(
        retention, approximations, ranks, "decay-svd"
    )
    approximations.update(svd_sparse_models)
    traces = build_traces(args.seed, args.quick)
    store = ErrorStore()
    staging_store = ErrorStore()
    for index, trace in enumerate(traces, start=1):
        (
            reference,
            predictions,
            _,
            _,
            staging_reference,
            staging_predictions,
        ) = simulate_trace(
            trace, retention, decay_seconds, approximations
        )
        label = f"{trace.category}-{trace.fps}"
        store.add(label, trace, reference, predictions)
        staging_store.add(label, trace, staging_reference, staging_predictions)
        if index % 250 == 0 or index == len(traces):
            print(f"simulated {index}/{len(traces)} traces", flush=True)

    methods = (
        *(f"isolated-k{k}" for k in range(1, 5)),
        *(f"volterra-k{k}" for k in range(1, 5)),
        "exact-switched-recurrence",
        "exact-commuted-projection",
        *(f"decay-svd-r{rank}" for rank in ranks),
        *(f"decay-svd-r{rank}-sparse01" for rank in ranks),
        *(f"trajectory-decay-r{rank}" for rank in ranks),
        *(f"trajectory-decay-r{rank}-sparse01" for rank in ranks),
    )
    costs = cost_estimates(retention, ranks, sparse_metadata, svd_sparse_metadata)
    write_reports(traces, store, methods, costs, ranks, args.seed, staging_store)
    print(f"wrote {REPORT_PATH}")
    print(f"wrote {JSON_PATH}")


if __name__ == "__main__":
    main()
