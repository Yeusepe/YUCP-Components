#!/usr/bin/env python3
"""Test a hard-routed local-affine realization of the AVR math layer.

This is an offline, deterministic experiment.  It deliberately has no Unity or
asset-writing dependency.  A structured 15-viseme teacher is sampled, an
independent affine expert is fitted for every hard viseme, and the fitted model
is decomposed into a shared tracker matrix plus viseme-local residuals:

    y = W0 f + b[v] + dW[v] f + c[v] Voice

The experiment measures two realizations:

* exact fitted experts; and
* experts whose small (viseme, tracker-input) residual groups are removed.

The latter models the proposed build-time compression.  W0 and the retained
residual are fused back into one coefficient clip per tracker input, so pruning
can reduce authored clip diversity without increasing connected clip work.

The teacher includes a shared, exact jaw passthrough and state coefficients
whose box extrema preserve the mandatory PP, FF, SS, and CH constraints.  The
same invariants are projected onto compressed experts analytically, rather than
being trusted to a finite random sample.
"""

from __future__ import annotations

import argparse
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Mapping, Sequence, Tuple

import numpy as np


REPORT_PATH = Path(__file__).with_name("local_affine_expert_report.md")
JSON_PATH = Path(__file__).with_name("local_affine_expert_metrics.json")

VISEMES = (
    "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS",
    "nn", "RR", "aa", "E", "I", "O", "U",
)
TRACKERS = (
    "JawOpen", "MouthClosed", "MouthOpen", "LipFunnel",
    "LipPucker", "LipSuck", "SmileSad", "TongueOut",
)
OUTPUTS = (
    "JawOpen", "LipClose", "MouthOpen", "LipFunnel", "LipPucker",
    "LipSuck", "SmileSad", "LipBite", "TongueOut", "JawX", "JawZ",
    "MouthX", "TongueY", "TongueTipUp", "TongueTipDown", "TongueWide",
    "TongueNarrow", "LowerLipIn", "UpperLipIn",
)

FPS_VALUES = (15, 30, 60, 90, 144)
JAW_OUTPUT = OUTPUTS.index("JawOpen")
JAW_TRACKER = TRACKERS.index("JawOpen")
LIP_CLOSE = OUTPUTS.index("LipClose")
MOUTH_OPEN = OUTPUTS.index("MouthOpen")
LIP_BITE = OUTPUTS.index("LipBite")

# Every constraint is affine over f, Voice in the unit hypercube, so its true
# worst case can be checked without relying on Monte Carlo coverage.
PROTECTED_CONSTRAINTS = (
    ("PP lip closure", VISEMES.index("PP"), LIP_CLOSE, "min", 0.88),
    ("PP mouth opening", VISEMES.index("PP"), MOUTH_OPEN, "max", 0.18),
    ("FF labiodental bite", VISEMES.index("FF"), LIP_BITE, "min", 0.72),
    ("CH mouth opening", VISEMES.index("CH"), MOUTH_OPEN, "max", 0.34),
    ("SS mouth opening", VISEMES.index("SS"), MOUTH_OPEN, "max", 0.34),
)


@dataclass
class AffineExperts:
    """One bias, eight tracker columns, and one Voice column per viseme."""

    bias: np.ndarray       # [viseme, output]
    weights: np.ndarray    # [viseme, output, tracker]
    voice: np.ndarray      # [viseme, output]

    def predict(self, visemes: np.ndarray, trackers: np.ndarray, voice: np.ndarray) -> np.ndarray:
        local_w = self.weights[visemes]
        return (
            self.bias[visemes]
            + np.einsum("noi,ni->no", local_w, trackers)
            + self.voice[visemes] * voice[:, None]
        )


@dataclass(frozen=True)
class Trace:
    category: str
    fps: int
    name: str
    visemes: np.ndarray
    trackers: np.ndarray
    voice: np.ndarray


def _base_tracker_matrix(rng: np.random.Generator) -> np.ndarray:
    """Construct a semantic shared tracker basis with very small cross-talk."""
    w = rng.normal(0.0, 0.004, (len(OUTPUTS), len(TRACKERS)))
    w[JAW_OUTPUT, :] = 0.0
    w[JAW_OUTPUT, JAW_TRACKER] = 1.0

    def put(output: str, tracker: str, value: float) -> None:
        w[OUTPUTS.index(output), TRACKERS.index(tracker)] += value

    put("LipClose", "MouthClosed", 0.82)
    put("LipClose", "JawOpen", -0.035)
    put("MouthOpen", "MouthOpen", 0.72)
    put("MouthOpen", "JawOpen", 0.18)
    put("MouthOpen", "MouthClosed", -0.06)
    put("LipFunnel", "LipFunnel", 0.90)
    put("LipPucker", "LipPucker", 0.87)
    put("LipSuck", "LipSuck", 0.84)
    put("SmileSad", "SmileSad", 0.78)
    put("LipBite", "MouthClosed", 0.10)
    put("LipBite", "LipSuck", 0.16)
    put("TongueOut", "TongueOut", 1.00)
    put("JawX", "SmileSad", 0.10)
    put("JawZ", "JawOpen", 0.22)
    put("MouthX", "SmileSad", 0.18)
    put("TongueY", "TongueOut", 0.72)
    put("TongueTipUp", "TongueOut", 0.34)
    put("TongueTipDown", "TongueOut", 0.18)
    put("TongueWide", "MouthOpen", 0.16)
    put("TongueWide", "SmileSad", 0.12)
    put("TongueNarrow", "LipPucker", 0.14)
    put("TongueNarrow", "TongueOut", 0.28)
    put("LowerLipIn", "LipSuck", 0.42)
    put("LowerLipIn", "MouthClosed", 0.11)
    put("UpperLipIn", "LipSuck", 0.39)
    put("UpperLipIn", "MouthClosed", 0.10)
    return w


def _viseme_tracker_poses() -> np.ndarray:
    """Plausible unit-range tracker targets used to animate replay traces."""
    # jaw, closed, open, funnel, pucker, suck, smile/sad, tongue out
    return np.asarray(
        (
            (0.02, 0.05, 0.01, 0.02, 0.02, 0.02, 0.00, 0.00),  # sil
            (0.02, 0.95, 0.01, 0.02, 0.04, 0.05, 0.00, 0.00),  # PP
            (0.08, 0.40, 0.08, 0.02, 0.03, 0.18, 0.00, 0.00),  # FF
            (0.18, 0.08, 0.18, 0.03, 0.02, 0.00, 0.00, 0.38),  # TH
            (0.20, 0.09, 0.20, 0.02, 0.02, 0.00, 0.00, 0.06),  # DD
            (0.31, 0.04, 0.31, 0.03, 0.02, 0.00, 0.00, 0.00),  # kk
            (0.22, 0.10, 0.22, 0.10, 0.16, 0.00, 0.02, 0.00),  # CH
            (0.16, 0.08, 0.16, 0.02, 0.02, 0.00, 0.12, 0.00),  # SS
            (0.14, 0.16, 0.14, 0.02, 0.02, 0.00, 0.00, 0.02),  # nn
            (0.28, 0.05, 0.28, 0.08, 0.12, 0.00, 0.00, 0.01),  # RR
            (0.72, 0.01, 0.70, 0.01, 0.00, 0.00, 0.00, 0.00),  # aa
            (0.46, 0.02, 0.44, 0.01, 0.00, 0.00, 0.20, 0.00),  # E
            (0.32, 0.03, 0.30, 0.01, 0.00, 0.00, 0.28, 0.00),  # I
            (0.43, 0.02, 0.41, 0.72, 0.54, 0.00, 0.00, 0.00),  # O
            (0.27, 0.04, 0.25, 0.58, 0.79, 0.00, 0.00, 0.00),  # U
        ),
        dtype=np.float64,
    )


def _state_biases() -> np.ndarray:
    """Visible speech priors; tracking remains the dominant measurement."""
    poses = _viseme_tracker_poses()
    b = np.zeros((len(VISEMES), len(OUTPUTS)), dtype=np.float64)
    b[:, LIP_CLOSE] = 0.18 * poses[:, TRACKERS.index("MouthClosed")]
    b[:, MOUTH_OPEN] = 0.16 * poses[:, TRACKERS.index("MouthOpen")]
    b[:, OUTPUTS.index("LipFunnel")] = 0.14 * poses[:, TRACKERS.index("LipFunnel")]
    b[:, OUTPUTS.index("LipPucker")] = 0.14 * poses[:, TRACKERS.index("LipPucker")]
    b[:, OUTPUTS.index("LipSuck")] = 0.10 * poses[:, TRACKERS.index("LipSuck")]
    b[:, OUTPUTS.index("SmileSad")] = 0.08 * poses[:, TRACKERS.index("SmileSad")]
    b[:, OUTPUTS.index("TongueOut")] = 0.16 * poses[:, TRACKERS.index("TongueOut")]

    b[VISEMES.index("FF"), LIP_BITE] = 0.54
    b[VISEMES.index("TH"), OUTPUTS.index("TongueTipDown")] = 0.24
    b[VISEMES.index("DD"), OUTPUTS.index("TongueTipUp")] = 0.22
    b[VISEMES.index("nn"), OUTPUTS.index("TongueTipUp")] = 0.30
    b[VISEMES.index("RR"), OUTPUTS.index("TongueNarrow")] = 0.24
    b[VISEMES.index("kk"), OUTPUTS.index("TongueWide")] = 0.20
    b[VISEMES.index("CH"), OUTPUTS.index("TongueWide")] = 0.16
    b[VISEMES.index("SS"), OUTPUTS.index("TongueNarrow")] = 0.18
    return b


def _linear_box_range(bias: float, weights: np.ndarray, voice: float) -> Tuple[float, float]:
    minimum = bias + float(np.minimum(weights, 0.0).sum()) + min(voice, 0.0)
    maximum = bias + float(np.maximum(weights, 0.0).sum()) + max(voice, 0.0)
    return minimum, maximum


def _project_constraints(model: AffineExperts, tolerance: float = 1e-12) -> int:
    """Shift only protected state biases until the entire input box is safe."""
    changes = 0
    for _, state, output, direction, bound in PROTECTED_CONSTRAINTS:
        low, high = _linear_box_range(
            float(model.bias[state, output]),
            model.weights[state, output],
            float(model.voice[state, output]),
        )
        if direction == "min" and low < bound - tolerance:
            model.bias[state, output] += bound - low
            changes += 1
        elif direction == "max" and high > bound + tolerance:
            model.bias[state, output] -= high - bound
            changes += 1
    return changes


def make_teacher(seed: int) -> AffineExperts:
    rng = np.random.default_rng(seed)
    common = _base_tracker_matrix(rng)
    delta = np.zeros((len(VISEMES), len(OUTPUTS), len(TRACKERS)), dtype=np.float64)

    # Structured state residuals: most groups are tiny, while vowel mouth
    # authority and tongue/consonant distinctions remain intentionally large.
    for state in range(len(VISEMES)):
        for tracker in range(len(TRACKERS)):
            selector = (state * 7 + tracker * 11) % 8
            scale = 0.002 if selector < 4 else (0.010 if selector < 6 else 0.026)
            delta[state, :, tracker] = rng.normal(0.0, scale, len(OUTPUTS))

    vowel_states = [VISEMES.index(x) for x in ("aa", "E", "I", "O", "U")]
    for state in vowel_states:
        # Complement an already-open tracked vowel rather than adding a second
        # full mouth opening on top of it.
        delta[state, MOUTH_OPEN, TRACKERS.index("MouthOpen")] -= 0.46
        delta[state, MOUTH_OPEN, TRACKERS.index("JawOpen")] -= 0.10

    delta[VISEMES.index("PP"), LIP_CLOSE, TRACKERS.index("MouthClosed")] += 0.12
    delta[VISEMES.index("FF"), LIP_BITE, TRACKERS.index("MouthClosed")] += 0.25
    delta[VISEMES.index("TH"), OUTPUTS.index("TongueTipDown"), TRACKERS.index("TongueOut")] += 0.32
    delta[VISEMES.index("nn"), OUTPUTS.index("TongueTipUp"), TRACKERS.index("TongueOut")] += 0.18
    delta[VISEMES.index("RR"), OUTPUTS.index("TongueNarrow"), TRACKERS.index("TongueOut")] += 0.25

    # Jaw is a protected measurement endpoint: no state or Voice path may alter
    # it.  This is pinned again after numerical fitting.
    delta[:, JAW_OUTPUT, :] = 0.0
    weights = common[None, :, :] + delta
    bias = _state_biases()
    bias[:, JAW_OUTPUT] = 0.0

    voice = rng.normal(0.0, 0.012, (len(VISEMES), len(OUTPUTS)))
    voice[:, JAW_OUTPUT] = 0.0
    voice[VISEMES.index("sil"), :] *= 0.15
    for state in vowel_states:
        voice[state, MOUTH_OPEN] += 0.045
    teacher = AffineExperts(bias=bias, weights=weights, voice=voice)
    _project_constraints(teacher)
    return teacher


def _training_inputs(rng: np.random.Generator, count: int) -> Tuple[np.ndarray, np.ndarray]:
    random = rng.beta(1.7, 1.9, (count, len(TRACKERS)))
    corners = np.vstack((
        np.zeros((1, len(TRACKERS))),
        np.ones((1, len(TRACKERS))),
        np.eye(len(TRACKERS)),
        1.0 - np.eye(len(TRACKERS)),
    ))
    trackers = np.vstack((random, corners))
    voice = np.concatenate((rng.random(count), np.linspace(0.0, 1.0, len(corners))))
    return trackers, voice


def fit_experts(teacher: AffineExperts, seed: int, samples_per_state: int) -> Tuple[AffineExperts, Dict[str, float]]:
    rng = np.random.default_rng(seed + 1009)
    bias = np.zeros_like(teacher.bias)
    weights = np.zeros_like(teacher.weights)
    voice_weights = np.zeros_like(teacher.voice)
    condition_numbers: List[float] = []
    train_errors: List[np.ndarray] = []

    for state in range(len(VISEMES)):
        trackers, voice = _training_inputs(rng, samples_per_state)
        visemes = np.full(len(trackers), state, dtype=np.int16)
        target = teacher.predict(visemes, trackers, voice)
        design = np.column_stack((np.ones(len(trackers)), trackers, voice))
        coefficients, _, _, singular = np.linalg.lstsq(design, target, rcond=None)
        condition_numbers.append(float(singular[0] / singular[-1]))
        bias[state] = coefficients[0]
        weights[state] = coefficients[1:1 + len(TRACKERS)].T
        voice_weights[state] = coefficients[-1]
        train_errors.append(design @ coefficients - target)

    fitted = AffineExperts(bias=bias, weights=weights, voice=voice_weights)
    fitted.bias[:, JAW_OUTPUT] = 0.0
    fitted.voice[:, JAW_OUTPUT] = 0.0
    fitted.weights[:, JAW_OUTPUT, :] = 0.0
    fitted.weights[:, JAW_OUTPUT, JAW_TRACKER] = 1.0
    training_error = np.concatenate(train_errors, axis=0)
    return fitted, {
        "samples_per_state": int(samples_per_state),
        "maximum_design_condition_number": max(condition_numbers),
        "rms_training_error": float(np.sqrt(np.mean(training_error ** 2))),
        "maximum_training_error": float(np.max(np.abs(training_error))),
    }


def decompose_and_prune(
    fitted: AffineExperts,
    prune_relative: float,
) -> Tuple[AffineExperts, np.ndarray, np.ndarray, Dict[str, object]]:
    common = fitted.weights.mean(axis=0)
    common[JAW_OUTPUT, :] = 0.0
    common[JAW_OUTPUT, JAW_TRACKER] = 1.0
    delta = fitted.weights - common[None, :, :]

    group_norms = np.linalg.norm(delta, axis=1)  # [state, tracker]
    common_norms = np.maximum(np.linalg.norm(common, axis=0), 0.15)
    normalized = group_norms / common_norms[None, :]
    prune_mask = normalized <= prune_relative

    retained = delta.copy()
    retained *= (~prune_mask)[:, None, :]
    compressed = AffineExperts(
        bias=fitted.bias.copy(),
        weights=common[None, :, :] + retained,
        voice=fitted.voice.copy(),
    )
    compressed.bias[:, JAW_OUTPUT] = 0.0
    compressed.voice[:, JAW_OUTPUT] = 0.0
    compressed.weights[:, JAW_OUTPUT, :] = 0.0
    compressed.weights[:, JAW_OUTPUT, JAW_TRACKER] = 1.0
    constraint_bias_repairs = _project_constraints(compressed)

    retained_per_state = (~prune_mask).sum(axis=1)
    pruned_per_state = prune_mask.sum(axis=1)
    return compressed, common, retained, {
        "relative_group_threshold": float(prune_relative),
        "total_groups": int(prune_mask.size),
        "groups_pruned": int(prune_mask.sum()),
        "groups_retained": int((~prune_mask).sum()),
        "fraction_pruned": float(prune_mask.mean()),
        "pruned_groups_per_state": [int(x) for x in pruned_per_state],
        "retained_groups_per_state": [int(x) for x in retained_per_state],
        "pruned_groups_per_input": [int(x) for x in prune_mask.sum(axis=0)],
        "retained_groups_per_input": [int(x) for x in (~prune_mask).sum(axis=0)],
        "constraint_bias_repairs": int(constraint_bias_repairs),
        "largest_pruned_normalized_norm": float(normalized[prune_mask].max(initial=0.0)),
        "smallest_retained_normalized_norm": float(normalized[~prune_mask].min(initial=math.inf)),
    }


def _different_viseme(rng: np.random.Generator, current: int) -> int:
    weights = np.asarray(
        (0.15, 0.06, 0.05, 0.04, 0.06, 0.05, 0.04, 0.06,
         0.07, 0.06, 0.09, 0.07, 0.06, 0.07, 0.07),
        dtype=np.float64,
    )
    weights[current] = 0.0
    weights /= weights.sum()
    return int(rng.choice(len(VISEMES), p=weights))


def _random_viseme_stream(rng: np.random.Generator, fps: int, seconds: float) -> np.ndarray:
    target_frames = int(round(seconds * fps))
    current = int(rng.integers(0, len(VISEMES)))
    result: List[int] = []
    while len(result) < target_frames:
        duration = float(np.clip(rng.lognormal(math.log(0.078), 0.58), 0.018, 0.38))
        result.extend([current] * max(1, int(round(duration * fps))))
        current = _different_viseme(rng, current)
    return np.asarray(result[:target_frames], dtype=np.int16)


def _animate_trace(
    category: str,
    fps: int,
    name: str,
    visemes: np.ndarray,
    rng: np.random.Generator,
) -> Trace:
    poses = _viseme_tracker_poses()
    dt = 1.0 / fps
    trackers = np.zeros((len(visemes), len(TRACKERS)), dtype=np.float64)
    voice = np.zeros(len(visemes), dtype=np.float64)
    current_f = poses[int(visemes[0])].copy()
    current_voice = 0.0 if int(visemes[0]) == 0 else 0.62
    expression = rng.beta(1.8, 2.8, len(TRACKERS))
    previous = -1

    for frame, raw_state in enumerate(visemes):
        state = int(raw_state)
        if state != previous:
            expression = rng.beta(1.7, 2.6, len(TRACKERS))
            previous = state
        t = frame * dt
        performance = 0.5 + 0.5 * math.sin(2.0 * math.pi * (0.71 * t + 0.13))
        target = np.clip(0.70 * poses[state] + 0.22 * expression + 0.08 * performance, 0.0, 1.0)
        alpha_f = 1.0 - math.exp(-dt / 0.026)
        current_f += alpha_f * (target - current_f)
        trackers[frame] = current_f

        target_voice = 0.0 if state == 0 else 0.34 + 0.58 * performance
        tau = 0.018 if target_voice > current_voice else 0.072
        alpha_voice = 1.0 - math.exp(-dt / tau)
        current_voice += alpha_voice * (target_voice - current_voice)
        voice[frame] = current_voice
    return Trace(category, fps, name, visemes, trackers, voice)


def build_traces(seed: int) -> List[Trace]:
    traces: List[Trace] = []
    base_rng = np.random.default_rng(seed + 2111)
    all_pairs = [(a, b) for a in range(len(VISEMES)) for b in range(len(VISEMES)) if a != b]
    all_triples = [
        (a, b, c)
        for a in range(len(VISEMES))
        for b in range(len(VISEMES))
        for c in range(len(VISEMES))
        if a != b and b != c and a != c
    ]

    for fps in FPS_VALUES:
        fps_rng = np.random.default_rng(int(base_rng.integers(0, 2 ** 31)) + fps)
        for ordinal in range(4):
            values = _random_viseme_stream(fps_rng, fps, 8.0)
            traces.append(_animate_trace("random", fps, f"random-{ordinal}", values, fps_rng))

        pair_values: List[int] = []
        for a, b in all_pairs:
            pair_values.extend([a] * max(1, int(round(0.11 * fps))))
            pair_values.extend([b] * max(1, int(round(0.16 * fps))))
        traces.append(_animate_trace(
            "transitions", fps, "all-directed-pairs",
            np.asarray(pair_values, dtype=np.int16), fps_rng,
        ))

        triple_order = fps_rng.permutation(len(all_triples))[:96]
        interruption_values: List[int] = []
        for ordinal, index in enumerate(triple_order):
            a, b, c = all_triples[int(index)]
            interruption_values.extend([a] * max(1, int(round(0.10 * fps))))
            # Alternate between a one-frame interruption and a 35 ms one.
            middle = 1 if ordinal % 2 == 0 else max(1, int(round(0.035 * fps)))
            interruption_values.extend([b] * middle)
            interruption_values.extend([c] * max(1, int(round(0.13 * fps))))
        traces.append(_animate_trace(
            "interruptions", fps, "aba-and-abc-interruptions",
            np.asarray(interruption_values, dtype=np.int16), fps_rng,
        ))
    return traces


def _error_metrics(error: np.ndarray) -> Dict[str, float]:
    absolute = np.abs(error).ravel()
    return {
        "rms": float(np.sqrt(np.mean(error ** 2))),
        "p99_absolute": float(np.percentile(absolute, 99.0)),
        "maximum_absolute": float(absolute.max(initial=0.0)),
    }


def _constraint_metrics(model: AffineExperts) -> Dict[str, object]:
    details: Dict[str, object] = {}
    violation_count = 0
    worst_violation = 0.0
    for name, state, output, direction, bound in PROTECTED_CONSTRAINTS:
        low, high = _linear_box_range(
            float(model.bias[state, output]),
            model.weights[state, output],
            float(model.voice[state, output]),
        )
        value = low if direction == "min" else high
        violation = max(0.0, bound - value) if direction == "min" else max(0.0, value - bound)
        violation_count += int(violation > 1e-9)
        worst_violation = max(worst_violation, violation)
        details[name] = {
            "direction": direction,
            "bound": float(bound),
            "analytic_extreme": float(value),
            "margin": float(value - bound if direction == "min" else bound - value),
            "violates": bool(violation > 1e-9),
        }

    jaw_bias = float(np.max(np.abs(model.bias[:, JAW_OUTPUT])))
    jaw_voice = float(np.max(np.abs(model.voice[:, JAW_OUTPUT])))
    expected = np.zeros((len(VISEMES), len(TRACKERS)))
    expected[:, JAW_TRACKER] = 1.0
    jaw_weight = float(np.max(np.abs(model.weights[:, JAW_OUTPUT, :] - expected)))
    jaw_error = max(jaw_bias, jaw_voice, jaw_weight)
    if jaw_error > 1e-12:
        violation_count += 1
    details["exact shared jaw passthrough"] = {
        "maximum_coefficient_error": jaw_error,
        "violates": bool(jaw_error > 1e-12),
    }
    return {
        "violation_count": int(violation_count),
        "worst_violation": float(worst_violation),
        "details": details,
    }


def evaluate_static(
    teacher: AffineExperts,
    candidates: Mapping[str, AffineExperts],
    seed: int,
    samples_per_state: int,
) -> Dict[str, object]:
    rng = np.random.default_rng(seed + 3011)
    viseme_parts: List[np.ndarray] = []
    tracker_parts: List[np.ndarray] = []
    voice_parts: List[np.ndarray] = []
    for state in range(len(VISEMES)):
        trackers, voice = _training_inputs(rng, samples_per_state)
        viseme_parts.append(np.full(len(trackers), state, dtype=np.int16))
        tracker_parts.append(trackers)
        voice_parts.append(voice)
    visemes = np.concatenate(viseme_parts)
    trackers = np.concatenate(tracker_parts)
    voice = np.concatenate(voice_parts)
    target = teacher.predict(visemes, trackers, voice)

    result: Dict[str, object] = {"sample_count": int(len(visemes)), "models": {}}
    for name, candidate in candidates.items():
        error = candidate.predict(visemes, trackers, voice) - target
        output_metrics = {
            output: _error_metrics(error[:, index])
            for index, output in enumerate(OUTPUTS)
        }
        result["models"][name] = {
            **_error_metrics(error),
            "per_output": output_metrics,
            "constraints": _constraint_metrics(candidate),
        }
    return result


def evaluate_traces(
    teacher: AffineExperts,
    candidates: Mapping[str, AffineExperts],
    traces: Sequence[Trace],
) -> Dict[str, object]:
    result: Dict[str, object] = {"trace_count": len(traces), "frame_count": int(sum(len(t.visemes) for t in traces))}
    for candidate_name, candidate in candidates.items():
        buckets: Dict[Tuple[str, int], List[np.ndarray]] = {}
        velocity_buckets: Dict[Tuple[str, int], List[np.ndarray]] = {}
        all_errors: List[np.ndarray] = []
        all_velocity_errors: List[np.ndarray] = []
        for trace in traces:
            target = teacher.predict(trace.visemes, trace.trackers, trace.voice)
            predicted = candidate.predict(trace.visemes, trace.trackers, trace.voice)
            error = predicted - target
            velocity_error = np.diff(error, axis=0) * trace.fps
            key = (trace.category, trace.fps)
            buckets.setdefault(key, []).append(error)
            velocity_buckets.setdefault(key, []).append(velocity_error)
            all_errors.append(error)
            all_velocity_errors.append(velocity_error)

        grouped: Dict[str, object] = {}
        for (category, fps), parts in sorted(buckets.items()):
            error = np.concatenate(parts, axis=0)
            velocity_error = np.concatenate(velocity_buckets[(category, fps)], axis=0)
            grouped[f"{category}@{fps}"] = {
                "frames": int(len(error)),
                "output_error": _error_metrics(error),
                "velocity_error_per_second": _error_metrics(velocity_error),
            }
        result[candidate_name] = {
            "overall_output_error": _error_metrics(np.concatenate(all_errors, axis=0)),
            "overall_velocity_error_per_second": _error_metrics(np.concatenate(all_velocity_errors, axis=0)),
            "by_category_and_fps": grouped,
        }
    return result


def clip_estimates(pruning: Mapping[str, object]) -> Dict[str, object]:
    residuals = np.asarray(pruning["retained_groups_per_state"], dtype=np.int64)
    pruned_by_input = np.asarray(pruning["pruned_groups_per_input"], dtype=np.int64)
    common_columns_referenced = int(np.count_nonzero(pruned_by_input))
    unique_fused_tracker_clips = int(pruning["groups_retained"]) + common_columns_referenced
    steady = 1 + len(TRACKERS) + 1  # bias + tracker columns + Voice
    dense_reference = 2084
    return {
        "measured_dense_reference_connected_clips_per_frame": dense_reference,
        "hard_routed_fused_steady_connected_clips": steady,
        "hard_routed_fused_transition_connected_clips": 2 * steady,
        "steady_connected_reduction_fraction": 1.0 - steady / dense_reference,
        "transition_connected_reduction_fraction": 1.0 - (2 * steady) / dense_reference,
        "states": len(VISEMES),
        "fitted_coefficients_per_state": 1 + len(TRACKERS) + 1,
        "unpruned_unique_authored_clips": len(VISEMES) * steady,
        "pruned_unique_authored_clips_estimate": 2 * len(VISEMES) + unique_fused_tracker_clips,
        "naive_shared_plus_residual_connected": {
            "minimum": int(len(TRACKERS) + 2 + residuals.min()),
            "median": float(len(TRACKERS) + 2 + np.median(residuals)),
            "maximum": int(len(TRACKERS) + 2 + residuals.max()),
        },
        "note": (
            "Common and retained residual coefficients must be fused into each active tracker clip. "
            "Evaluating them as separate shared/residual layers saves storage but can increase frame cost."
        ),
    }


def _fmt(value: float) -> str:
    if abs(value) < 1e-4 and value != 0.0:
        return f"{value:.3e}"
    return f"{value:.6f}"


def write_markdown(payload: Mapping[str, object]) -> None:
    static = payload["static_held_out"]["models"]
    traces = payload["sequence_replay"]
    pruning = payload["pruning"]
    clips = payload["clip_estimates"]
    acceptance = payload["acceptance"]

    lines = [
        "# Local-affine viseme expert experiment",
        "",
        "This deterministic offline experiment fits 15 hard-routed affine experts with eight "
        "unit-range face-tracking inputs and 19 representative lower-face/tongue outputs. "
        "It does not modify an avatar, controller, clip, renderer, or mesh.",
        "",
        "## Result",
        "",
        f"**{'PASS' if acceptance['passed'] else 'FAIL'}** - {acceptance['summary']}",
        "",
        "| Model | Static RMS | Static p99 | Static max | Replay RMS | Replay p99 | Replay max | Velocity p99/s |",
        "|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for name in ("fitted", "pruned"):
        sm = static[name]
        rm = traces[name]["overall_output_error"]
        vm = traces[name]["overall_velocity_error_per_second"]
        lines.append(
            f"| {name} | {_fmt(sm['rms'])} | {_fmt(sm['p99_absolute'])} | "
            f"{_fmt(sm['maximum_absolute'])} | {_fmt(rm['rms'])} | "
            f"{_fmt(rm['p99_absolute'])} | {_fmt(rm['maximum_absolute'])} | "
            f"{_fmt(vm['p99_absolute'])} |"
        )

    lines.extend((
        "",
        "## Structural estimate",
        "",
        "| Quantity | Value |",
        "|---|---:|",
        f"| Dense merged reference, connected clips/frame | {clips['measured_dense_reference_connected_clips_per_frame']} |",
        f"| Hard-routed steady state | {clips['hard_routed_fused_steady_connected_clips']} |",
        f"| During one state transition | {clips['hard_routed_fused_transition_connected_clips']} |",
        f"| Steady connected reduction | {100.0 * clips['steady_connected_reduction_fraction']:.2f}% |",
        f"| Transition connected reduction | {100.0 * clips['transition_connected_reduction_fraction']:.2f}% |",
        f"| Unpruned unique authored clips | {clips['unpruned_unique_authored_clips']} |",
        f"| Pruned unique authored clips (estimate) | {clips['pruned_unique_authored_clips_estimate']} |",
        "",
        f"The compressor removed **{pruning['groups_pruned']} / {pruning['total_groups']}** "
        f"viseme/input residual groups ({100.0 * pruning['fraction_pruned']:.1f}%).",
        "",
        "> " + clips["note"],
        "",
        "## Constraint proof over the complete input box",
        "",
        "Random replay is not used as the proof. For each protected affine output, the script "
        "computes its exact minimum or maximum over `f in [0,1]^8` and `Voice in [0,1]`.",
        "",
        "| Constraint | Required | Analytic extreme | Margin |",
        "|---|---:|---:|---:|",
    ))
    for name, item in static["pruned"]["constraints"]["details"].items():
        if name == "exact shared jaw passthrough":
            continue
        sign = ">=" if item["direction"] == "min" else "<="
        lines.append(
            f"| {name} | {sign} {item['bound']:.3f} | {item['analytic_extreme']:.6f} | {item['margin']:.3e} |"
        )
    jaw = static["pruned"]["constraints"]["details"]["exact shared jaw passthrough"]
    lines.extend((
        f"| Exact shared jaw passthrough | coefficient error = 0 | "
        f"{jaw['maximum_coefficient_error']:.3e} | {-jaw['maximum_coefficient_error']:.3e} |",
        "",
        "## Replay coverage",
        "",
        f"The held-out replay contains **{traces['trace_count']} traces / "
        f"{traces['frame_count']:,} frames**: random speech, every directed viseme pair, and "
        "one-frame/35 ms interruptions at 15, 30, 60, 90, and 144 FPS.",
        "",
        "| Replay group | RMS | p99 | max | velocity p99/s |",
        "|---|---:|---:|---:|---:|",
    ))
    for key, item in traces["pruned"]["by_category_and_fps"].items():
        output = item["output_error"]
        velocity = item["velocity_error_per_second"]
        lines.append(
            f"| {key} | {_fmt(output['rms'])} | {_fmt(output['p99_absolute'])} | "
            f"{_fmt(output['maximum_absolute'])} | {_fmt(velocity['p99_absolute'])} |"
        )
    lines.extend((
        "",
        "## Interpretation",
        "",
        "The affine fit itself should be numerically exact; all meaningful error above is from "
        "dropping small local residual groups. The large structural win comes from hard routing: "
        "only one state's ten fused coefficient clips are connected in steady speech. The common "
        "matrix decomposition is useful for detecting reusable/prunable groups, but should not be "
        "left as an additional live Animator layer.",
        "",
        "This is a mathematical and structural gate, not yet an end-to-end Unity CPU measurement. "
        "The next test must confirm that VRCFury preserves the state machine and that Unity really "
        "disconnects inactive state motions after merging.",
        "",
        f"Generated with seed `{payload['configuration']['seed']}` and prune threshold "
        f"`{payload['configuration']['prune_relative']}`.",
        "",
    ))
    REPORT_PATH.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seed", type=int, default=20260718)
    parser.add_argument("--train-per-viseme", type=int, default=768)
    parser.add_argument("--heldout-per-viseme", type=int, default=1536)
    parser.add_argument(
        "--prune-relative", type=float, default=0.055,
        help="Remove dW[v,:,input] groups at or below this norm/common-norm ratio.",
    )
    args = parser.parse_args()
    if args.train_per_viseme < 32 or args.heldout_per_viseme < 32:
        parser.error("sample counts must be at least 32 per viseme")
    if not 0.0 <= args.prune_relative <= 1.0:
        parser.error("--prune-relative must be in [0, 1]")

    teacher = make_teacher(args.seed)
    fitted, fit_diagnostics = fit_experts(teacher, args.seed, args.train_per_viseme)
    pruned, common, retained, pruning = decompose_and_prune(fitted, args.prune_relative)
    candidates = {"fitted": fitted, "pruned": pruned}
    static = evaluate_static(teacher, candidates, args.seed, args.heldout_per_viseme)
    traces = build_traces(args.seed)
    replay = evaluate_traces(teacher, candidates, traces)
    clips = clip_estimates(pruning)

    exact = static["models"]["fitted"]
    compressed = static["models"]["pruned"]
    replay_compressed = replay["pruned"]["overall_output_error"]
    constraints = compressed["constraints"]
    passed = bool(
        exact["maximum_absolute"] <= 1e-10
        and compressed["p99_absolute"] <= 0.025
        and compressed["maximum_absolute"] <= 0.10
        and replay_compressed["p99_absolute"] <= 0.025
        and constraints["violation_count"] == 0
    )
    summary = (
        f"fitted max={exact['maximum_absolute']:.3e}; "
        f"pruned replay p99={replay_compressed['p99_absolute']:.6f}, "
        f"max={replay_compressed['maximum_absolute']:.6f}; "
        f"constraints={constraints['violation_count']}; "
        f"connected clips={clips['hard_routed_fused_steady_connected_clips']} steady/"
        f"{clips['hard_routed_fused_transition_connected_clips']} transitioning."
    )
    payload: Dict[str, object] = {
        "schema_version": 1,
        "configuration": {
            "seed": args.seed,
            "visemes": len(VISEMES),
            "tracker_inputs": len(TRACKERS),
            "outputs": len(OUTPUTS),
            "train_per_viseme": args.train_per_viseme,
            "heldout_per_viseme": args.heldout_per_viseme,
            "prune_relative": args.prune_relative,
            "fps": list(FPS_VALUES),
        },
        "fit": fit_diagnostics,
        "decomposition": {
            "common_matrix_frobenius_norm": float(np.linalg.norm(common)),
            "retained_residual_frobenius_norm": float(np.linalg.norm(retained)),
        },
        "pruning": pruning,
        "static_held_out": static,
        "sequence_replay": replay,
        "clip_estimates": clips,
        "acceptance": {"passed": passed, "summary": summary},
    }
    JSON_PATH.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    write_markdown(payload)
    print(f"{'PASS' if passed else 'FAIL'}: {summary}")
    print(f"Wrote {JSON_PATH}")
    print(f"Wrote {REPORT_PATH}")
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
