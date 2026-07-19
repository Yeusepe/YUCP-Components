#!/usr/bin/env python3
"""Train the direct, interruptible Oculus viseme reconstruction trajectory.

VRChat exposes only the dominant Oculus viseme.  The model therefore cannot
recover Meta's hidden weights exactly, but it can learn the causal conditional
trajectory of those weights.  Runtime renders this trajectory directly while
face tracking is inactive; it does not hide the learned motion behind another
two-pole observer.

The first 75 percent of each state-local trajectory is a cubic Bezier simplex
curve.  A positive linear continuation occupies the final 25 percent.  Unity's
pairwise fixed-duration state transitions provide interruptible convex blending
between trajectories, so every rendered frame remains finite, nonnegative, and
sums to one.
"""

from __future__ import annotations

import argparse
import copy
import math
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Sequence

import numpy as np

import train_oculus_viseme_halo as halo
import train_transition_retention as corpus


SCRIPT_DIR = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_CACHE = (
    Path(os.environ.get("LOCALAPPDATA", "."))
    / "YUCP"
    / "AdvancedVisemeTraining"
    / "SPIRE_EMA_CORPUS"
    / "oculus_halo_continuous_extraction_v1.npz"
)
DEFAULT_HALO_JSON = SCRIPT_DIR / "Generated" / "advanced_viseme_oculus_halo.json"
DEFAULT_AUDIT_JSON = (
    SCRIPT_DIR / "Generated" / "advanced_viseme_oculus_dynamics.json"
)
DEFAULT_MODEL_CS = (
    REPOSITORY_ROOT
    / "Packages"
    / "com.yucp.components"
    / "Runtime"
    / "Components"
    / "Data"
    / "Generated"
    / "AdvancedVisemeOculusDynamics.generated.cs"
)

SCHEMA_VERSION = 2
MODEL_VERSION = 2
OBSERVER_RESPONSE_SECONDS = 0.017
DEFAULT_SPEECH_LIVELINESS = 0.0
MAXIMUM_SPEECH_LIVELINESS_LEAD = 0.20
EVALUATION_LIVELINESS = 0.0
TRAJECTORY_DURATION_SECONDS = 0.224
TRAJECTORY_CORE_RATIO = 0.75
TRAJECTORY_CONTROL_POINT_COUNT = 5
TARGET_CROSSFADE_SECONDS = 0.072
TRAJECTORY_CURVATURE_RATIO = 0.32
TRAJECTORY_TERMINAL_RATIO = 0.32
TAIL_SEAM_RATIO = 0.32
TAIL_TERMINAL_RATIO = 0.08
RENDER_RATE_FPS = (15, 30, 60, 90, 144)
FALSE_PLATEAU_PREDICTED_L1 = 0.004
FALSE_PLATEAU_TEACHER_L1 = 0.012


def configure_shared_model() -> None:
    """Pin the reusable fitter to the exact deployed transfer function."""

    halo.OBSERVER_RESPONSE_SECONDS = OBSERVER_RESPONSE_SECONDS
    halo.DEFAULT_SPEECH_LIVELINESS = DEFAULT_SPEECH_LIVELINESS
    halo.MAXIMUM_SPEECH_LIVELINESS_LEAD = MAXIMUM_SPEECH_LIVELINESS_LEAD
    halo.EVALUATION_LIVELINESS = EVALUATION_LIVELINESS
    # The older reusable fitter remains pinned to its four-control observer
    # contract.  The direct five-control model below owns its exact basis and
    # Unity transition simulation instead of mutating that shared contract.
    halo.RENDER_RATE_FPS = RENDER_RATE_FPS


def load_records(cache_path: Path) -> list[halo.OculusUtterance]:
    if not cache_path.is_file():
        raise FileNotFoundError(
            f"Missing reviewed Oculus extraction cache: {cache_path}. "
            "Run train_oculus_viseme_halo.py train first."
        )
    with np.load(
        cache_path, allow_pickle=False, max_header_size=1 << 20
    ) as data:
        offsets = np.asarray(data["offsets"], dtype=np.int64)
        continuous = np.asarray(data["continuous"], dtype=np.float64)
        winners = np.asarray(data["winners"], dtype=np.int64)
        splits = np.asarray(data["splits"]).astype(str)

    if offsets.ndim != 1 or len(offsets) != len(splits) + 1:
        raise ValueError("Oculus extraction cache offsets are malformed")
    if continuous.shape != (int(offsets[-1]), halo.VISEME_COUNT):
        raise ValueError("Oculus extraction cache continuous tensor is malformed")
    if winners.shape != (int(offsets[-1]),):
        raise ValueError("Oculus extraction cache winner sequence is malformed")

    records: list[halo.OculusUtterance] = []
    for index, split in enumerate(splits):
        start, end = int(offsets[index]), int(offsets[index + 1])
        records.append(
            halo.OculusUtterance(
                split=str(split),
                speaker=index,
                prompt=index,
                prompt_ordinal=0,
                audio_entry=f"cached/{index}",
                continuous=continuous[start:end],
                winners=winners[start:end],
                delays_ms=np.zeros(end - start, dtype=np.int32),
            )
        )

    counts = {
        split: sum(record.split == split for record in records)
        for split in halo.EXPECTED_UTTERANCE_COUNTS
    }
    if counts != halo.EXPECTED_UTTERANCE_COUNTS:
        raise ValueError(
            f"Oculus extraction cache split counts differ: {counts}"
        )
    return records


BLOCK_SECONDS = (
    halo.ovr_source.ANALYSIS_BUFFER_SAMPLES
    / halo.ovr_source.ANALYSIS_SAMPLE_RATE
)
BOUNDARY_SHRINKAGE = 24.0


def trajectory_basis(age_seconds: float) -> np.ndarray:
    """Positive partition-of-unity basis used by the Unity curve."""

    age = max(0.0, float(age_seconds))
    core_seconds = TRAJECTORY_DURATION_SECONDS * TRAJECTORY_CORE_RATIO
    tail_seconds = TRAJECTORY_DURATION_SECONDS - core_seconds
    result = np.zeros(TRAJECTORY_CONTROL_POINT_COUNT, dtype=np.float64)
    if age <= core_seconds:
        phase = min(1.0, age / core_seconds)
        inverse = 1.0 - phase
        result[:4] = (
            inverse**3,
            3.0 * inverse * inverse * phase,
            3.0 * inverse * phase * phase,
            phase**3,
        )
        return result
    phase = min(1.0, (age - core_seconds) / tail_seconds)
    result[3] = 1.0 - phase
    result[4] = phase
    return result


class FeatureNode:
    def value(self, now: float) -> np.ndarray:
        raise NotImplementedError


@dataclass(frozen=True)
class StateNode(FeatureNode):
    winner: int
    started: float

    def value(self, now: float) -> np.ndarray:
        result = np.zeros(
            halo.VISEME_COUNT * TRAJECTORY_CONTROL_POINT_COUNT,
            dtype=np.float64,
        )
        start = self.winner * TRAJECTORY_CONTROL_POINT_COUNT
        result[start : start + TRAJECTORY_CONTROL_POINT_COUNT] = (
            trajectory_basis(now - self.started)
        )
        return result


@dataclass(frozen=True)
class TransitionNode(FeatureNode):
    source: FeatureNode
    destination: StateNode
    started: float

    def value(self, now: float) -> np.ndarray:
        phase = min(
            1.0,
            max(0.0, (now - self.started) / TARGET_CROSSFADE_SECONDS),
        )
        if phase <= 0.0:
            return self.source.value(now)
        if phase >= 1.0:
            return self.destination.value(now)
        return (
            (1.0 - phase) * self.source.value(now)
            + phase * self.destination.value(now)
        )


def collapse_node(node: FeatureNode, now: float) -> FeatureNode:
    if (
        isinstance(node, TransitionNode)
        and now >= node.started + TARGET_CROSSFADE_SECONDS
    ):
        return node.destination
    return node


def target_feature_record(record: halo.OculusUtterance) -> np.ndarray:
    parameter_count = halo.VISEME_COUNT * TRAJECTORY_CONTROL_POINT_COUNT
    features = np.empty(
        (len(record.winners), parameter_count), dtype=np.float64
    )
    node: FeatureNode = StateNode(
        halo.SILENCE_INDEX, -TRAJECTORY_DURATION_SECONDS
    )
    current_winner = halo.SILENCE_INDEX
    for frame, winner_value in enumerate(record.winners):
        now = frame * BLOCK_SECONDS
        node = collapse_node(node, now)
        winner = int(winner_value)
        if winner != current_winner:
            node = TransitionNode(node, StateNode(winner, now), now)
            current_winner = winner
        features[frame] = node.value(now)
    return features


def fixed_supports(static_table: np.ndarray) -> list[np.ndarray]:
    return [
        np.flatnonzero(static_table[winner] > 0.0)
        for winner in range(halo.VISEME_COUNT)
    ]


def fit_boundary_anchors(
    records: Sequence[halo.OculusUtterance],
    static_table: np.ndarray,
    supports: Sequence[np.ndarray],
) -> tuple[np.ndarray, np.ndarray]:
    sums = np.zeros_like(static_table, dtype=np.float64)
    counts = np.zeros(halo.VISEME_COUNT, dtype=np.int64)
    for record in records:
        teacher, _ = halo.normalized_shape(record)
        previous = int(record.winners[0])
        for frame in range(1, len(record.winners)):
            winner = int(record.winners[frame])
            if winner != previous and winner != halo.SILENCE_INDEX:
                sums[winner] += teacher[frame]
                counts[winner] += 1
            previous = winner

    anchors = static_table.copy()
    anchors[halo.SILENCE_INDEX] = 0.0
    anchors[halo.SILENCE_INDEX, halo.SILENCE_INDEX] = 1.0
    for winner in range(1, halo.VISEME_COUNT):
        empirical = (
            sums[winner] / float(counts[winner])
            if counts[winner] > 0
            else static_table[winner]
        )
        reliability = counts[winner] / (
            counts[winner] + BOUNDARY_SHRINKAGE
        )
        estimate = (
            reliability * empirical
            + (1.0 - reliability) * static_table[winner]
        )
        anchors[winner] = halo.project_trajectory_pose(
            estimate, supports[winner], winner
        )
    return anchors, counts


def direct_normal_equations(
    records: Sequence[halo.OculusUtterance],
) -> tuple[np.ndarray, np.ndarray]:
    parameter_count = halo.VISEME_COUNT * TRAJECTORY_CONTROL_POINT_COUNT
    normal = np.zeros((parameter_count, parameter_count), dtype=np.float64)
    target = np.zeros(
        (parameter_count, halo.VISEME_COUNT), dtype=np.float64
    )
    transition_window = halo.TRANSITION_WINDOW_BLOCKS * BLOCK_SECONDS
    for record in records:
        features = target_feature_record(record)
        teacher, _ = halo.normalized_shape(record)
        ages = np.empty(len(record.winners), dtype=np.float64)
        current = -1
        age = 0.0
        for frame, winner_value in enumerate(record.winners):
            winner = int(winner_value)
            if winner != current:
                current = winner
                age = 0.0
            ages[frame] = age
            age += BLOCK_SECONDS
        weights = np.where(
            ages < transition_window,
            1.0 + halo.TRAJECTORY_POSE_TRANSITION_BOOST,
            1.0,
        )
        weighted_features = features * np.sqrt(weights)[:, None]
        weighted_teacher = teacher * np.sqrt(weights)[:, None]
        normal += weighted_features.T @ weighted_features
        target += weighted_features.T @ weighted_teacher
        if len(features) > 1:
            feature_delta = np.diff(features, axis=0)
            teacher_delta = np.diff(teacher, axis=0)
            normal += halo.TRAJECTORY_DERIVATIVE_WEIGHT * (
                feature_delta.T @ feature_delta
            )
            target += halo.TRAJECTORY_DERIVATIVE_WEIGHT * (
                feature_delta.T @ teacher_delta
            )
    return normal, target


def validate_direct_controls(
    controls: np.ndarray,
    static_table: np.ndarray,
) -> None:
    expected = (
        halo.VISEME_COUNT,
        TRAJECTORY_CONTROL_POINT_COUNT,
        halo.VISEME_COUNT,
    )
    if controls.shape != expected or not np.all(np.isfinite(controls)):
        raise ValueError("Direct trajectory control tensor is malformed")
    supports = fixed_supports(static_table)
    for winner in range(halo.VISEME_COUNT):
        for control in range(TRAJECTORY_CONTROL_POINT_COUNT):
            row = controls[winner, control]
            if np.any(row < -1e-8) or not math.isclose(
                float(row.sum()), 1.0, abs_tol=1e-7
            ):
                raise ValueError("Direct trajectory escaped the simplex")
            if int(np.argmax(row)) != winner:
                raise ValueError("Direct trajectory lost winner dominance")
            outside = np.setdiff1d(
                np.arange(halo.VISEME_COUNT), supports[winner]
            )
            if np.any(np.abs(row[outside]) > 1e-8):
                raise ValueError("Direct trajectory escaped fixed support")


def fit_direct_controls(
    records: Sequence[halo.OculusUtterance],
    static_table: np.ndarray,
) -> tuple[np.ndarray, dict[str, Any]]:
    supports = fixed_supports(static_table)
    anchors, boundary_counts = fit_boundary_anchors(
        records, static_table, supports
    )
    normal, target = direct_normal_equations(records)
    control_count = TRAJECTORY_CONTROL_POINT_COUNT
    parameter_count = halo.VISEME_COUNT * control_count
    baseline = np.repeat(
        static_table[:, None, :], control_count, axis=1
    ).reshape(parameter_count, halo.VISEME_COUNT)
    scale = max(1.0, float(np.trace(normal)) / float(parameter_count))
    regularization = np.full(
        parameter_count,
        halo.TRAJECTORY_RIDGE_RATIO * scale,
        dtype=np.float64,
    )
    for winner in range(halo.VISEME_COUNT):
        regularization[(winner + 1) * control_count - 1] += (
            halo.TRAJECTORY_ENDPOINT_RIDGE_RATIO * scale
        )

    penalty = np.zeros((parameter_count, parameter_count), dtype=np.float64)
    second_difference = np.asarray(
        ((1.0, -2.0, 1.0, 0.0), (0.0, 1.0, -2.0, 1.0)),
        dtype=np.float64,
    )
    integrated_metric = np.asarray(((1.0, 0.5), (0.5, 1.0)))
    cubic_curvature = (
        12.0
        * second_difference.T
        @ integrated_metric
        @ second_difference
        / 24.0
    )
    tail_ratio = (
        (1.0 - TRAJECTORY_CORE_RATIO) / TRAJECTORY_CORE_RATIO
    )
    seam = np.asarray(
        (0.0, 0.0, 3.0 * tail_ratio, -(1.0 + 3.0 * tail_ratio), 1.0)
    )
    terminal = np.asarray((0.0, 0.0, 0.0, -1.0, 1.0))
    local_tail = (
        TAIL_SEAM_RATIO * np.outer(seam, seam)
        + TAIL_TERMINAL_RATIO * 2.0 * np.outer(terminal, terminal)
    )
    for winner in range(halo.VISEME_COUNT):
        start = winner * control_count
        first_four = np.arange(start, start + 4)
        penalty[np.ix_(first_four, first_four)] += (
            TRAJECTORY_CURVATURE_RATIO * cubic_curvature
        )
        indices = np.arange(start, start + control_count)
        penalty[np.ix_(indices, indices)] += local_tail

    system = normal + np.diag(regularization) + scale * penalty
    right = target + regularization[:, None] * baseline
    fixed = np.zeros(parameter_count, dtype=bool)
    fixed[:control_count] = True
    fixed_values = baseline.copy()
    fixed_values[:control_count] = 0.0
    fixed_values[:control_count, halo.SILENCE_INDEX] = 1.0
    for winner in range(1, halo.VISEME_COUNT):
        row = winner * control_count
        fixed[row] = True
        fixed_values[row] = anchors[winner]

    unknown = np.flatnonzero(~fixed)
    fixed_indices = np.flatnonzero(fixed)
    reduced = system[np.ix_(unknown, unknown)]
    reduced_right = (
        right[unknown]
        - system[np.ix_(unknown, fixed_indices)] @ fixed_values[fixed_indices]
    )
    controls = fixed_values.copy()
    controls[unknown] = np.linalg.solve(reduced, reduced_right)

    def project(values: np.ndarray) -> np.ndarray:
        result = values.copy()
        for winner in range(1, halo.VISEME_COUNT):
            for control in range(control_count):
                row = winner * control_count + control
                if not fixed[row]:
                    result[row] = halo.project_trajectory_pose(
                        result[row], supports[winner], winner
                    )
        result[fixed] = fixed_values[fixed]
        return result

    controls = project(controls)
    largest_eigenvalue = float(np.linalg.eigvalsh(reduced)[-1])
    relative_delta = math.inf
    steps = 0
    for steps in range(1, halo.TRAJECTORY_PROJECTED_GRADIENT_STEPS + 1):
        gradient = system @ controls - right
        candidate = controls.copy()
        candidate[unknown] -= gradient[unknown] / largest_eigenvalue
        candidate = project(candidate)
        relative_delta = float(
            np.linalg.norm(candidate - controls)
            / max(1.0, float(np.linalg.norm(controls)))
        )
        controls = candidate
        if relative_delta <= halo.TRAJECTORY_PROJECTED_GRADIENT_TOLERANCE:
            break

    tensor = controls.reshape(
        halo.VISEME_COUNT,
        TRAJECTORY_CONTROL_POINT_COUNT,
        halo.VISEME_COUNT,
    )
    validate_direct_controls(tensor, static_table)
    return tensor, {
        "conditionNumber": float(np.linalg.cond(reduced)),
        "projectedGradientSteps": steps,
        "projectedGradientRelativeDelta": relative_delta,
        "boundaryCounts": boundary_counts.tolist(),
    }


def render_direct_record(
    record: halo.OculusUtterance,
    controls: np.ndarray,
    fps: int,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    dt = 1.0 / float(fps)
    availability = (
        np.arange(1, len(record.winners) + 1, dtype=np.float64)
        * BLOCK_SECONDS
    )
    times = np.arange(dt, availability[-1] + dt * 0.25, dt)
    teacher_shapes, _ = halo.normalized_shape(record)
    flat = controls.reshape(
        halo.VISEME_COUNT * TRAJECTORY_CONTROL_POINT_COUNT,
        halo.VISEME_COUNT,
    )
    node: FeatureNode = StateNode(
        halo.SILENCE_INDEX, -TRAJECTORY_DURATION_SECONDS
    )
    current_winner = halo.SILENCE_INDEX
    age = 0.0
    predictions: list[np.ndarray] = []
    teachers: list[np.ndarray] = []
    ages: list[float] = []
    for now in times:
        source_index = int(
            np.searchsorted(availability, now, side="right") - 1
        )
        winner = (
            halo.SILENCE_INDEX
            if source_index < 0
            else int(record.winners[source_index])
        )
        node = collapse_node(node, now)
        if winner != current_winner:
            node = TransitionNode(node, StateNode(winner, now), now)
            current_winner = winner
            age = 0.0
        if source_index >= 0:
            predictions.append(
                halo.sparsify_simplex_coordinates(node.value(now) @ flat)
            )
            teachers.append(teacher_shapes[source_index])
            ages.append(age)
        age += dt
    return (
        np.asarray(predictions),
        np.asarray(teachers),
        np.asarray(ages),
    )


def direct_motion_metrics(
    records: Sequence[halo.OculusUtterance],
    controls: np.ndarray,
) -> dict[str, Any]:
    per_rate: dict[str, Any] = {}
    transition_window = halo.TRANSITION_WINDOW_BLOCKS * BLOCK_SECONDS
    for fps in RENDER_RATE_FPS:
        squared = np.zeros(4, dtype=np.float64)
        elements = np.zeros(4, dtype=np.int64)
        transition_squared = 0.0
        transition_elements = 0
        plateaus = 0
        edges = 0
        maximum_step = 0.0
        steps: list[np.ndarray] = []
        predicted_variation = 0.0
        teacher_variation = 0.0
        for record in records:
            predicted, teacher, ages = render_direct_record(
                record, controls, fps
            )
            error = predicted - teacher
            squared[0] += float(np.square(error).sum())
            elements[0] += error.size
            mask = ages < transition_window
            if np.any(mask):
                transition_squared += float(np.square(error[mask]).sum())
                transition_elements += int(np.count_nonzero(mask)) * halo.VISEME_COUNT
            for order in (1, 2, 3):
                if len(predicted) <= order:
                    continue
                delta_error = (
                    np.diff(predicted, n=order, axis=0)
                    - np.diff(teacher, n=order, axis=0)
                )
                squared[order] += float(np.square(delta_error).sum())
                elements[order] += delta_error.size
            if len(predicted) > 1:
                predicted_step = np.abs(np.diff(predicted, axis=0)).sum(axis=1)
                teacher_step = np.abs(np.diff(teacher, axis=0)).sum(axis=1)
                steps.append(predicted_step)
                maximum_step = max(maximum_step, float(predicted_step.max()))
                predicted_variation += float(predicted_step.sum())
                teacher_variation += float(teacher_step.sum())
                plateaus += int(np.count_nonzero(
                    (predicted_step < FALSE_PLATEAU_PREDICTED_L1)
                    & (teacher_step > FALSE_PLATEAU_TEACHER_L1)
                ))
                edges += len(predicted_step)
        all_steps = np.concatenate(steps)
        per_rate[str(fps)] = {
            "poseMse": float(squared[0] / elements[0]),
            "transitionPoseMse": float(
                transition_squared / transition_elements
            ),
            "d1Mse": float(squared[1] / elements[1]),
            "d2Mse": float(squared[2] / elements[2]),
            "d3Mse": float(squared[3] / elements[3]),
            "p999StepL1": float(np.quantile(all_steps, 0.999)),
            "maximumStepL1": maximum_step,
            "falsePlateauRate": plateaus / edges,
            "temporalVariationRatio": (
                predicted_variation / teacher_variation
            ),
        }
    aggregate = {
        key: float(np.mean([
            float(per_rate[str(fps)][key])
            for fps in RENDER_RATE_FPS
            if fps != 15
        ]))
        for key in (
            "poseMse",
            "transitionPoseMse",
            "d1Mse",
            "d2Mse",
            "d3Mse",
            "p999StepL1",
            "maximumStepL1",
            "falsePlateauRate",
            "temporalVariationRatio",
        )
    }
    return {"perRate": per_rate, "aggregate30To144": aggregate}


def static_prediction(
    winners: np.ndarray,
    table: np.ndarray,
    response_seconds: float,
    lead: float,
) -> np.ndarray:
    step = (
        halo.ovr_source.ANALYSIS_BUFFER_SAMPLES
        / halo.ovr_source.ANALYSIS_SAMPLE_RATE
    )
    alpha = 1.0 - math.exp(-step / response_seconds)
    fast = np.zeros(halo.VISEME_COUNT, dtype=np.float64)
    fast[halo.SILENCE_INDEX] = 1.0
    slow = fast.copy()
    output = np.empty((len(winners), halo.VISEME_COUNT), dtype=np.float64)
    for index, winner_value in enumerate(winners):
        output[index] = (
            (1.0 - lead) * halo.sparsify_simplex_coordinates(slow)
            + lead * halo.sparsify_simplex_coordinates(fast)
        )
        target = table[int(winner_value)]
        fast += alpha * (target - fast)
        slow += alpha * (fast - slow)
    return output


def motion_metrics(
    records: Sequence[halo.OculusUtterance],
    table: np.ndarray,
    mode: str,
    controls: np.ndarray | None = None,
) -> dict[str, float]:
    totals = {
        "mse": 0.0,
        "d1Mse": 0.0,
        "d2Mse": 0.0,
        "d3Mse": 0.0,
        "mseElements": 0,
        "d1Elements": 0,
        "d2Elements": 0,
        "d3Elements": 0,
        "falsePlateaus": 0,
        "edges": 0,
        "predictedVariation": 0.0,
        "teacherVariation": 0.0,
        "maximumStepL1": 0.0,
    }
    for record in records:
        teacher, _ = halo.normalized_shape(record)
        if mode == "current":
            predicted = static_prediction(
                record.winners, table, 0.024, 0.85
            )
        elif mode == "critical":
            predicted = static_prediction(
                record.winners, table, OBSERVER_RESPONSE_SECONDS, 0.0
            )
        elif mode == "dynamic" and controls is not None:
            predicted = halo.observe_trajectory(record.winners, controls)
        else:
            raise ValueError(f"Unsupported motion metric mode: {mode}")

        error = predicted - teacher
        totals["mse"] += float(np.square(error).sum())
        totals["mseElements"] += error.size
        for order, key, count_key in (
            (1, "d1Mse", "d1Elements"),
            (2, "d2Mse", "d2Elements"),
            (3, "d3Mse", "d3Elements"),
        ):
            if len(predicted) <= order:
                continue
            predicted_delta = np.diff(predicted, n=order, axis=0)
            teacher_delta = np.diff(teacher, n=order, axis=0)
            totals[key] += float(
                np.square(predicted_delta - teacher_delta).sum()
            )
            totals[count_key] += predicted_delta.size

        if len(predicted) > 1:
            predicted_delta = np.abs(np.diff(predicted, axis=0)).sum(axis=1)
            teacher_delta = np.abs(np.diff(teacher, axis=0)).sum(axis=1)
            totals["falsePlateaus"] += int(
                np.count_nonzero(
                    (predicted_delta < FALSE_PLATEAU_PREDICTED_L1)
                    & (teacher_delta > FALSE_PLATEAU_TEACHER_L1)
                )
            )
            totals["edges"] += len(predicted_delta)
            totals["predictedVariation"] += float(predicted_delta.sum())
            totals["teacherVariation"] += float(teacher_delta.sum())
            totals["maximumStepL1"] = max(
                totals["maximumStepL1"], float(predicted_delta.max())
            )

    return {
        "mse": totals["mse"] / totals["mseElements"],
        "d1Mse": totals["d1Mse"] / totals["d1Elements"],
        "d2Mse": totals["d2Mse"] / totals["d2Elements"],
        "d3Mse": totals["d3Mse"] / totals["d3Elements"],
        "falsePlateauRate": totals["falsePlateaus"] / totals["edges"],
        "temporalVariationRatio": (
            totals["predictedVariation"] / totals["teacherVariation"]
        ),
        "maximumStepL1": totals["maximumStepL1"],
    }


def relative_gain(
    baseline: dict[str, float], candidate: dict[str, float], key: str
) -> float:
    return 1.0 - float(candidate[key]) / float(baseline[key])


def metric_gains(
    baseline: dict[str, float], candidate: dict[str, float]
) -> dict[str, float]:
    return {
        key + "Gain": relative_gain(baseline, candidate, key)
        for key in (
            "mse",
            "d1Mse",
            "d2Mse",
            "d3Mse",
            "falsePlateauRate",
            "maximumStepL1",
        )
    }


def json_safe(value: Any) -> Any:
    if isinstance(value, dict):
        return {str(key): json_safe(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [json_safe(item) for item in value]
    if isinstance(value, np.ndarray):
        return value.tolist()
    if isinstance(value, (np.floating, float)):
        result = float(value)
        if not math.isfinite(result):
            raise ValueError("Audit contains a non-finite float")
        return result
    if isinstance(value, (np.bool_, bool)):
        return bool(value)
    if isinstance(value, (np.integer, int)):
        return int(value)
    if value is None or isinstance(value, str):
        return value
    raise TypeError(f"Unsupported audit value: {type(value).__name__}")


def model_hash_input(document: dict[str, Any]) -> dict[str, Any]:
    return {
        "modelVersion": document["modelVersion"],
        "visemeOrder": document["visemeOrder"],
        "sourceHaloTableSha256": document["sourceHaloTableSha256"],
        "durationSeconds": document["model"]["durationSeconds"],
        "coreDurationSeconds": document["model"]["coreDurationSeconds"],
        "targetCrossfadeSeconds": document["model"]["targetCrossfadeSeconds"],
        "controlPointCount": document["model"]["controlPointCount"],
        "controlPoints": document["model"]["controlPoints"],
    }


def content_hash_input(document: dict[str, Any]) -> dict[str, Any]:
    value = copy.deepcopy(document)
    value.pop("contentSha256", None)
    return value


def validate_document(document: dict[str, Any]) -> None:
    if document.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError("Unsupported Oculus dynamics schema")
    if document.get("modelVersion") != MODEL_VERSION:
        raise ValueError("Unsupported Oculus dynamics model")
    if document.get("visemeOrder") != list(halo.VISEMES):
        raise ValueError("Oculus dynamics viseme order differs")
    model = document.get("model")
    if not isinstance(model, dict):
        raise ValueError("Oculus dynamics document has no model")
    controls = np.asarray(model.get("controlPoints"), dtype=np.float64)
    static_table = np.asarray(model.get("sourceHaloWeights"), dtype=np.float64)
    halo.validate_table(static_table)
    validate_direct_controls(controls, static_table)
    if int(model.get("controlPointCount", -1)) != TRAJECTORY_CONTROL_POINT_COUNT:
        raise ValueError("Oculus dynamics control-point count differs")
    if not math.isclose(
        float(model.get("durationSeconds", math.nan)),
        TRAJECTORY_DURATION_SECONDS,
        rel_tol=0.0,
        abs_tol=1e-9,
    ):
        raise ValueError("Oculus dynamics duration differs")
    if not math.isclose(
        float(model.get("coreDurationSeconds", math.nan)),
        TRAJECTORY_DURATION_SECONDS * TRAJECTORY_CORE_RATIO,
        rel_tol=0.0,
        abs_tol=1e-9,
    ):
        raise ValueError("Oculus dynamics core duration differs")
    if not math.isclose(
        float(model.get("targetCrossfadeSeconds", math.nan)),
        TARGET_CROSSFADE_SECONDS,
        rel_tol=0.0,
        abs_tol=1e-9,
    ):
        raise ValueError("Oculus dynamics transition duration differs")
    if document.get("modelSha256") != corpus.canonical_sha256(
        model_hash_input(document)
    ):
        raise ValueError("Oculus dynamics model hash mismatch")
    if document.get("contentSha256") != corpus.canonical_sha256(
        content_hash_input(document)
    ):
        raise ValueError("Oculus dynamics content hash mismatch")


def build_document(
    halo_document: dict[str, Any],
    records: Sequence[halo.OculusUtterance],
) -> dict[str, Any]:
    halo.validate_document(halo_document)
    static_table = np.asarray(
        halo_document["model"]["weights"], dtype=np.float64
    )
    fit = [record for record in records if record.split == "fit"]
    development = [
        record for record in records if record.split == "development"
    ]
    heldout = [record for record in records if record.split == "heldout"]

    # Hyperparameters were frozen by nested leave-speaker-pair-out CV inside
    # the fit speakers.  Development now serves only as an external gate; it
    # cannot change duration, transition time, or basis family.
    development_controls, development_diagnostics = fit_direct_controls(
        fit, static_table
    )
    development_dynamic = direct_motion_metrics(
        development, development_controls
    )
    development_aggregate = development_dynamic["aggregate30To144"]
    development_limits = {
        "poseMse": 0.0080,
        "d1Mse": 0.0030,
        "d2Mse": 0.0055,
        "d3Mse": 0.0130,
        "p999StepL1": 0.70,
        "maximumStepL1": 0.85,
        "falsePlateauRate": 0.0275,
    }
    failed = [
        f"{key}={float(development_aggregate[key]):.6f} > {maximum:.6f}"
        for key, maximum in development_limits.items()
        if float(development_aggregate[key]) > maximum
    ]
    if failed:
        raise ValueError(
            "Direct Oculus dynamics failed the frozen development gate: "
            + "; ".join(failed)
        )

    final_controls, final_diagnostics = fit_direct_controls(
        fit + development, static_table
    )
    heldout_dynamic = direct_motion_metrics(heldout, final_controls)

    document: dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "modelVersion": MODEL_VERSION,
        "contentSha256": None,
        "modelSha256": None,
        "sourceHaloContentSha256": halo_document["contentSha256"],
        "sourceHaloTableSha256": halo_document["tableSha256"],
        "description": (
            "Causal boundary-anchored Oculus simplex trajectories rendered "
            "directly through interruptible Unity state transitions."
        ),
        "visemeOrder": list(halo.VISEMES),
        "training": {
            "splitPolicy": (
                "Nested speaker CV froze topology and timing inside fit; fit "
                "estimates coefficients; development gates once; heldout reports once."
            ),
            "durationSeconds": TRAJECTORY_DURATION_SECONDS,
            "coreDurationSeconds": (
                TRAJECTORY_DURATION_SECONDS * TRAJECTORY_CORE_RATIO
            ),
            "targetCrossfadeSeconds": TARGET_CROSSFADE_SECONDS,
            "curvatureRatio": TRAJECTORY_CURVATURE_RATIO,
            "tailSeamRatio": TAIL_SEAM_RATIO,
            "tailTerminalRatio": TAIL_TERMINAL_RATIO,
            "renderRatesFps": list(RENDER_RATE_FPS),
            "basis": (
                "cubic Bernstein simplex over the first 75 percent, then a "
                "positive linear continuation to a fifth simplex control"
            ),
            "interruption": (
                "Pairwise fixed-duration destination-interruptible Unity "
                "transitions convexly blend the still-advancing source and destination."
            ),
            "developmentLimits": development_limits,
            "developmentFitDiagnostics": development_diagnostics,
            "finalFitDiagnostics": final_diagnostics,
        },
        "model": {
            "orientation": "controlPoints[hardWinner][controlPoint][outputViseme]",
            "durationSeconds": TRAJECTORY_DURATION_SECONDS,
            "coreDurationSeconds": (
                TRAJECTORY_DURATION_SECONDS * TRAJECTORY_CORE_RATIO
            ),
            "targetCrossfadeSeconds": TARGET_CROSSFADE_SECONDS,
            "controlPointCount": TRAJECTORY_CONTROL_POINT_COUNT,
            "controlPoints": final_controls.astype(np.float32).tolist(),
            "sourceHaloWeights": static_table.astype(np.float32).tolist(),
            "invariants": {
                "nonnegative": True,
                "simplex": True,
                "winnerDominant": True,
                "staticTopKSupportReused": True,
                "silenceBitExact": True,
                "positivePartitionOfUnityBasis": True,
                "directNoTrackingRender": True,
            },
        },
        "evaluation": {
            "development": {
                "dynamic": development_dynamic,
                "status": "external gate after nested-CV timing selection",
            },
            "heldout": {
                "dynamic": heldout_dynamic,
                "use": "report-only after development selection",
            },
        },
        "limitations": [
            "The hard winner discards the native 15-float frame, so exact inversion is impossible.",
            "Elapsed winner time recovers a conditional trajectory, not genuine future-phone anticipation.",
            "The reviewed corpus is English and does not establish universal language coverage.",
            "A one-frame Animator animated-parameter publication delay remains unavoidable.",
        ],
    }
    document = json_safe(document)
    document["modelSha256"] = corpus.canonical_sha256(
        model_hash_input(document)
    )
    document["contentSha256"] = corpus.canonical_sha256(
        content_hash_input(document)
    )
    validate_document(document)
    return document


def float_literal(value: float) -> str:
    result = np.float32(value)
    if not np.isfinite(result):
        raise ValueError("Cannot emit a non-finite coefficient")
    if result == 0.0:
        result = np.float32(0.0)
    return f"{result:.9f}f"


def generate_csharp(document: dict[str, Any], output: Path) -> None:
    validate_document(document)
    controls = np.asarray(
        document["model"]["controlPoints"], dtype=np.float32
    )
    lines = [
        "// <auto-generated>",
        "// Trained by Tools/AdvancedVisemeTraining/train_oculus_viseme_dynamics.py.",
        "// Source: SPIRE EMA Corpus paired audio, CC BY 4.0, pinned revision 55f21628de95514e3ff22eaccc75e1547d181297.",
        "// Compact normalized trajectories only; no audio or per-frame trace is embedded.",
        "// </auto-generated>",
        "using System;",
        "",
        "namespace YUCP.Components",
        "{",
        "    public static class AdvancedVisemeOculusDynamics",
        "    {",
        f"        public const int ModelVersion = {MODEL_VERSION};",
        f"        public const int VisemeCount = {halo.VISEME_COUNT};",
        f"        public const int ControlPointCount = {TRAJECTORY_CONTROL_POINT_COUNT};",
        f"        public const string ContentSha256 = \"{document['contentSha256']}\";",
        f"        public const string ModelSha256 = \"{document['modelSha256']}\";",
        f"        public const string SourceHaloTableSha256 = \"{document['sourceHaloTableSha256']}\";",
        f"        public const float TrajectoryDurationSeconds = {float_literal(TRAJECTORY_DURATION_SECONDS)};",
        f"        public const float TrajectoryCoreDurationSeconds = {float_literal(TRAJECTORY_DURATION_SECONDS * TRAJECTORY_CORE_RATIO)};",
        f"        public const float TargetCrossfadeSeconds = {float_literal(TARGET_CROSSFADE_SECONDS)};",
        f"        public const float ObserverResponseSeconds = {float_literal(OBSERVER_RESPONSE_SECONDS)};",
        f"        public const float EvaluationLiveliness = {float_literal(EVALUATION_LIVELINESS)};",
        "",
        "        // Flattened as [hard winner, control point, output viseme].",
        "        private static readonly float[] ControlPointValues =",
        "        {",
    ]
    for winner, winner_controls in enumerate(controls):
        lines.append(f"            // {halo.VISEMES[winner]}")
        for control, row in enumerate(winner_controls):
            lines.append(f"            // control {control}")
            lines.append(
                "            "
                + ", ".join(float_literal(value) for value in row)
                + ","
            )
    lines.extend(
        [
            "        };",
            "",
            "        public static float Weight(int hardWinner, int controlPoint, int outputViseme)",
            "        {",
            "            RequireIndex(hardWinner, nameof(hardWinner), VisemeCount);",
            "            RequireIndex(controlPoint, nameof(controlPoint), ControlPointCount);",
            "            RequireIndex(outputViseme, nameof(outputViseme), VisemeCount);",
            "            return ControlPointValues[(hardWinner * ControlPointCount + controlPoint) * VisemeCount + outputViseme];",
            "        }",
            "",
            "        public static bool HasDynamicTrajectory(int hardWinner)",
            "        {",
            "            RequireIndex(hardWinner, nameof(hardWinner), VisemeCount);",
            "            for (var output = 0; output < VisemeCount; output++)",
            "            {",
            "                var first = Weight(hardWinner, 0, output);",
            "                for (var control = 1; control < ControlPointCount; control++)",
            "                    if (Weight(hardWinner, control, output) != first)",
            "                        return true;",
            "            }",
            "            return false;",
            "        }",
            "",
            "        private static void RequireIndex(int value, string name, int count)",
            "        {",
            "            if ((uint)value >= count) throw new ArgumentOutOfRangeException(name);",
            "        }",
            "    }",
            "}",
            "",
        ]
    )
    corpus.write_text_atomic(output, "\n".join(lines))


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("command", choices=("train", "generate"))
    result.add_argument("--cache", type=Path, default=DEFAULT_CACHE)
    result.add_argument("--halo-json", type=Path, default=DEFAULT_HALO_JSON)
    result.add_argument("--audit-json", type=Path, default=DEFAULT_AUDIT_JSON)
    result.add_argument("--model-cs", type=Path, default=DEFAULT_MODEL_CS)
    return result


def main(argv: Sequence[str] | None = None) -> int:
    configure_shared_model()
    args = parser().parse_args(argv)
    if args.command == "generate":
        document = corpus.load_json(args.audit_json)
        generate_csharp(document, args.model_cs)
        print(f"Runtime C#: {args.model_cs}", flush=True)
        return 0

    halo_document = corpus.load_json(args.halo_json)
    records = load_records(args.cache)
    document = build_document(halo_document, records)
    corpus.write_json_atomic(args.audit_json, document)
    generate_csharp(document, args.model_cs)
    print(f"Audit JSON: {args.audit_json}", flush=True)
    print(f"Runtime C#: {args.model_cs}", flush=True)
    print(f"Content SHA-256: {document['contentSha256']}", flush=True)
    print(f"Model SHA-256: {document['modelSha256']}", flush=True)
    print(
        "Heldout direct trajectory metrics:",
        document["evaluation"]["heldout"]["dynamic"][
            "aggregate30To144"
        ],
        flush=True,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
