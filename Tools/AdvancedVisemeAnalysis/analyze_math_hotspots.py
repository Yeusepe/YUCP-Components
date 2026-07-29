"""Offline active-path cost audit for a generated YUCP AVR Math layer.

This uses the same synchronous AAP replay model as ``avr_replay.py``, but
instruments each direct child of the Math root.  A leaf clip reference is
counted whenever its path weight is positive.  Every non-empty float curve in
that clip is then counted as one active curve binding.  These are structural
cost proxies, not milliseconds.

The input generator deliberately models the two upstream operations omitted by
``ControllerProgram``: selecting the current viseme decoder state and sampling
the continuously increasing time parameter.  It also emits correlated,
quantized face-tracking inputs so the audit does not rely on independent random
floats that a real avatar could never produce.
"""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from avr_congruence import Analyzer  # noqa: E402


DEFAULT_CONTROLLER = Path("Assets/__YUCP_AVR_Profile_CompactHoldBaselineAAP.controller")
DEFAULT_JSON = HERE / "math_hotspot_metrics.json"
DEFAULT_REPORT = HERE / "math_hotspot_report.md"
FPS_VALUES = (15, 30, 60, 90, 144)
PREFIX = "YUCP/AdvancedViseme/"
INTERNAL = PREFIX + "_Internal/"


def parameter_defaults(analyzer: Analyzer) -> dict[str, float]:
    # Generated AVR computational parameters are floats/AAPs.  Keep the other
    # fields as a defensive fallback so this diagnostic remains useful if a
    # future controller contains a Bool or Int input.
    values: dict[str, float] = {}
    for item in analyzer.controller["m_AnimatorParameters"]:
        parameter_type = int(item.get("m_Type", 1))
        if parameter_type == 1:
            value = item.get("m_DefaultFloat", 0)
        elif parameter_type == 3:
            value = item.get("m_DefaultInt", 0)
        else:
            value = item.get("m_DefaultBool", 0)
        values[item["m_Name"]] = float(value or 0)
    return values


def motion_name(analyzer: Analyzer, motion_id: int) -> str:
    motion = analyzer.trees.get(motion_id) or analyzer.clips.get(motion_id) or {}
    return str(motion.get("m_Name") or f"motion-{motion_id}")


def populated_float_curves(clip: dict) -> list[dict]:
    return [
        curve
        for curve in (clip.get("m_FloatCurves") or [])
        if (curve.get("curve") or {}).get("m_Curve")
    ]


def semantic_group(attribute: str) -> str:
    if any(token in attribute for token in (
        "/FrameTime", "/LastTime", "/Alpha/", "/_Internal/Time"
    )):
        return "timing-and-alpha"
    if "/BetaCoarticulation/" in attribute:
        return "beta-coarticulation"
    if "/PhonePosterior/" in attribute or "/Corpus/" in attribute:
        return "phone-posterior-and-corpus"
    if "/TongueInference/" in attribute:
        return "tongue-inference"
    if "/Tracking/" in attribute or attribute.endswith("/Speech/TrackingBlend"):
        return "tracking-decode-and-observer"
    if any(token in attribute for token in (
        "/Constraint/", "/Velocity/", "/Evidence/", "/Max/"
    )) or any(attribute.endswith("/Speech/" + suffix) for suffix in (
        "Bilabial", "Labiodental", "Coronal", "Dorsal", "Rhotic"
    )):
        return "constraints-evidence-and-velocity"
    if "/Articulation/" in attribute:
        return "articulation-fusion-and-render"
    if "/Viseme/" in attribute:
        return "viseme-observer-and-render"
    if "/Voice/" in attribute or "/Speech/" in attribute or "/Hangover/" in attribute:
        return "voice-presence-and-hangover"
    return "misc-control"


def family_name(name: str) -> str:
    """Collapse only obvious generated index/channel repetitions."""
    if "RetentionProjected/" in name and " = 1" in name:
        return "Beta retention: context-to-projected copies"
    if name.startswith("Vector product by ") and "/SparseFast" in name:
        return "Beta retention: projected-by-fast contractions"
    if name.startswith("Tongue inference contracted output sum signed row"):
        return "Tongue inference: contracted output row sums"
    if name.startswith("Tongue inference model hidden unit "):
        return "Tongue inference: hidden units"
    if name.startswith("Phone posterior "):
        return "Phone posterior"
    if name.startswith("Corpus "):
        return "Corpus projection/contraction"
    # Make scalar bit decoders and repeated vector maps visible as families,
    # without merging unrelated operations that merely have numeric suffixes.
    if "/BinaryMagnitude = " in name:
        channel = name.split("/Tracking/", 1)[-1].split("/BinaryMagnitude", 1)[0]
        return f"Tracking quantized decode: {channel}"
    return name


@dataclass
class PathCost:
    clips: int = 0
    curves: int = 0
    group_clips: Counter = field(default_factory=Counter)
    group_curves: Counter = field(default_factory=Counter)

    def add(self, other: "PathCost") -> None:
        self.clips += other.clips
        self.curves += other.curves
        self.group_clips.update(other.group_clips)
        self.group_curves.update(other.group_curves)


class MotionEvaluator:
    def __init__(self, analyzer: Analyzer):
        self.a = analyzer

    def evaluate(
        self,
        motion_id: int,
        weight: float,
        params: dict[str, float],
        writes: dict[str, float],
        count: bool = True,
    ) -> PathCost:
        cost = PathCost()
        if not (weight > 0.0) or not math.isfinite(weight):
            return cost
        clip = self.a.clips.get(motion_id)
        if clip is not None:
            curves = populated_float_curves(clip)
            if count:
                cost.clips = 1
                cost.curves = len(curves)
                represented_groups: set[str] = set()
                for curve in curves:
                    group = semantic_group(str(curve.get("attribute") or ""))
                    cost.group_curves[group] += 1
                    represented_groups.add(group)
                for group in represented_groups:
                    cost.group_clips[group] += 1
            for curve in curves:
                keys = curve["curve"]["m_Curve"]
                value = float(keys[0]["value"])
                # Generated math clips are constant.  Fail instead of silently
                # turning this structural replay into a time-sampling guess.
                if any(float(key["value"]) != value for key in keys[1:]):
                    raise AssertionError(
                        f"non-constant curve in {clip.get('m_Name')}"
                    )
                if int(curve.get("classID", 0)) == 95 and not (curve.get("path") or ""):
                    target = str(curve["attribute"])
                    writes[target] = writes.get(target, 0.0) + weight * value
            return cost

        tree = self.a.trees.get(motion_id)
        if tree is None:
            return cost
        children = tree.get("m_Childs", [])
        blend_type = int(tree.get("m_BlendType", 0))
        if blend_type == 4:
            if tree.get("m_NormalizedBlendValues", 0):
                raise AssertionError("normalized Direct tree unsupported")
            for child in children:
                child_weight = max(
                    0.0,
                    float(params.get(child.get("m_DirectBlendParameter", ""), 0.0)),
                )
                if child_weight > 0.0:
                    cost.add(self.evaluate(
                        int(child["m_Motion"]["fileID"]),
                        weight * child_weight,
                        params,
                        writes,
                        count,
                    ))
            return cost

        if blend_type != 0:
            raise AssertionError(f"unsupported blend type {blend_type}")
        if not children:
            return cost
        x = float(params.get(tree.get("m_BlendParameter", ""), 0.0))
        thresholds = np.asarray(
            [float(child.get("m_Threshold", 0.0)) for child in children],
            dtype=np.float64,
        )
        if x <= thresholds[0] or len(children) == 1:
            return self.evaluate(
                int(children[0]["m_Motion"]["fileID"]), weight, params, writes, count
            )
        if x >= thresholds[-1]:
            return self.evaluate(
                int(children[-1]["m_Motion"]["fileID"]), weight, params, writes, count
            )
        high = int(np.searchsorted(thresholds, x, side="right"))
        low = high - 1
        span = float(thresholds[high] - thresholds[low])
        fraction = 0.0 if span == 0.0 else float((x - thresholds[low]) / span)
        if fraction < 1.0:
            cost.add(self.evaluate(
                int(children[low]["m_Motion"]["fileID"]),
                weight * (1.0 - fraction),
                params,
                writes,
                count,
            ))
        if fraction > 0.0:
            cost.add(self.evaluate(
                int(children[high]["m_Motion"]["fileID"]),
                weight * fraction,
                params,
                writes,
                count,
            ))
        return cost


def selected_math_root(analyzer: Analyzer) -> tuple[dict, int]:
    for layer in analyzer.controller["m_AnimatorLayers"]:
        if layer.get("m_Name") != "YUCP AVR Math":
            continue
        machine = analyzer.machines[int(layer["m_StateMachine"]["fileID"])]
        states = machine.get("m_ChildStates", [])
        if len(states) != 1:
            raise AssertionError("Math layer is no longer a single-state program")
        state = analyzer.states[int(states[0]["m_State"]["fileID"])]
        root_id = int(state["m_Motion"]["fileID"])
        root = analyzer.trees[root_id]
        if int(root.get("m_BlendType", 0)) != 4:
            raise AssertionError("Math root is no longer a Direct tree")
        return root, root_id
    raise AssertionError("YUCP AVR Math layer not found")


def decoder_motions(analyzer: Analyzer) -> list[int]:
    for layer in analyzer.controller["m_AnimatorLayers"]:
        if layer.get("m_Name") != "YUCP AVR Viseme Decoder":
            continue
        machine = analyzer.machines[int(layer["m_StateMachine"]["fileID"])]
        motions: list[int] = []
        for child in machine.get("m_ChildStates", []):
            state = analyzer.states[int(child["m_State"]["fileID"])]
            motions.append(int(state["m_Motion"]["fileID"]))
        if len(motions) != 15:
            raise AssertionError(f"expected 15 decoder states, got {len(motions)}")
        return motions
    raise AssertionError("YUCP AVR Viseme Decoder layer not found")


# Coarse Oculus-viseme articulator targets.  They do not try to reconstruct a
# particular avatar mesh; they only make face-tracking activity correlated with
# the same speech symbol instead of producing impossible independent channels.
ARTICULATOR_TARGETS = np.asarray([
    # jaw, closed, open, funnel, pucker, suck, smile/sad, tongue out
    [0.02, 0.05, 0.02, 0.02, 0.02, 0.02,  0.00, 0.00],  # sil
    [0.05, 0.95, 0.02, 0.03, 0.10, 0.15,  0.00, 0.00],  # PP
    [0.10, 0.25, 0.08, 0.03, 0.02, 0.02, -0.02, 0.00],  # FF
    [0.14, 0.12, 0.10, 0.02, 0.02, 0.02,  0.00, 0.12],  # TH
    [0.16, 0.15, 0.12, 0.02, 0.02, 0.02,  0.08, 0.00],  # DD
    [0.28, 0.08, 0.24, 0.02, 0.02, 0.02,  0.00, 0.00],  # kk
    [0.18, 0.10, 0.15, 0.10, 0.04, 0.02,  0.00, 0.00],  # CH
    [0.10, 0.12, 0.08, 0.03, 0.02, 0.02,  0.08, 0.00],  # SS
    [0.08, 0.40, 0.05, 0.02, 0.02, 0.02,  0.02, 0.00],  # nn
    [0.18, 0.10, 0.14, 0.10, 0.10, 0.02,  0.00, 0.00],  # RR
    [0.78, 0.02, 0.82, 0.02, 0.02, 0.02, -0.05, 0.00],  # aa
    [0.48, 0.03, 0.52, 0.02, 0.02, 0.02,  0.18, 0.00],  # E
    [0.30, 0.04, 0.32, 0.04, 0.03, 0.02,  0.28, 0.00],  # I
    [0.58, 0.02, 0.62, 0.55, 0.42, 0.02, -0.02, 0.00],  # O
    [0.32, 0.03, 0.34, 0.42, 0.68, 0.04, -0.02, 0.00],  # U
], dtype=np.float64)


TRACKING_BITS = {
    "JawOpen": 4,
    "LipClose": 2,
    "MouthOpen": 3,
    "LipFunnel": 3,
    "LipPucker": 3,
    "LipSuck": 2,
    "SmileSad": 3,
    "TongueOut": 2,
}
TRACKING_INPUT_NAMES = {
    "JawOpen": "JawOpen",
    "LipClose": "MouthClosed",
    "MouthOpen": "MouthOpen",
    "LipFunnel": "LipFunnel",
    "LipPucker": "LipPucker",
    "LipSuck": "LipSuck",
    "SmileSad": "SmileSad",
    "TongueOut": "TongueOut",
}


def write_quantized_tracking(
    params: dict[str, float], values: np.ndarray, active: bool
) -> None:
    base = "YUCP/TestFaceTracking/v2/"
    params["YUCP/TestFaceTracking/LipTrackingActive"] = 1.0 if active else 0.0
    for channel_index, channel in enumerate(TRACKING_BITS):
        bit_count = TRACKING_BITS[channel]
        value = float(values[channel_index]) if active else 0.0
        negative = channel == "SmileSad" and value < 0.0
        magnitude = max(0.0, min(1.0, abs(value) if channel == "SmileSad" else value))
        quantized = int(round(magnitude * ((1 << bit_count) - 1)))
        input_name = TRACKING_INPUT_NAMES[channel]
        for bit in range(bit_count):
            params[f"{base}{input_name}{1 << bit}"] = float((quantized >> bit) & 1)
        if channel == "SmileSad":
            params[f"{base}SmileSadNegative"] = 1.0 if negative and active else 0.0


@dataclass
class InputFrame:
    viseme: int
    voice: float
    tracking: np.ndarray
    tracking_active: bool


def realistic_frames(
    fps: int, seconds: float, seed: int, tracking_active: bool
) -> Iterable[InputFrame]:
    rng = np.random.default_rng(seed)
    frame_count = int(round(fps * seconds))
    phone = 0
    phone_frames = 0
    utterance_frames = 0
    silence_frames = int(round(rng.uniform(0.2, 0.7) * fps))
    voice = 0.0
    face = ARTICULATOR_TARGETS[0].copy()
    for _ in range(frame_count):
        if silence_frames > 0:
            silence_frames -= 1
            phone = 0
            if silence_frames == 0:
                utterance_frames = int(round(rng.uniform(1.4, 4.0) * fps))
                phone_frames = 0
        elif utterance_frames > 0:
            utterance_frames -= 1
            phone_frames -= 1
            if phone_frames <= 0:
                # Vowels get slightly more dwell time than consonants.
                phone = int(rng.integers(1, 15))
                duration = rng.uniform(0.075, 0.19) if phone >= 10 else rng.uniform(0.045, 0.14)
                phone_frames = max(1, int(round(duration * fps)))
            if utterance_frames == 0:
                silence_frames = int(round(rng.uniform(0.22, 1.0) * fps))

        voice_target = 0.0 if phone == 0 else float(rng.uniform(0.3, 0.9))
        voice_alpha = 1.0 - math.exp(-1.0 / (fps * (0.018 if voice_target > voice else 0.075)))
        voice += voice_alpha * (voice_target - voice)
        target = ARTICULATOR_TARGETS[phone]
        # Face tracking remains deliberately responsive (about 18 ms) while
        # carrying small natural variation that is independent of Voice.
        face_alpha = 1.0 - math.exp(-1.0 / (fps * 0.018))
        face += face_alpha * (target - face)
        tracked = np.clip(face + rng.normal(0.0, 0.012, size=8), -1.0, 1.0)
        yield InputFrame(phone, voice, tracked, tracking_active)


def stress_frames(fps: int, seconds: float, seed: int) -> Iterable[InputFrame]:
    rng = np.random.default_rng(seed)
    frame_count = int(round(fps * seconds))
    phone = 0
    hold = 0
    face = np.zeros(8, dtype=np.float64)
    for _ in range(frame_count):
        hold -= 1
        if hold <= 0:
            phone = int(rng.integers(0, 15))
            hold = int(rng.integers(1, max(2, int(round(0.055 * fps)) + 1)))
        voice = float(rng.beta(1.5, 1.5))
        target = np.asarray([
            rng.random(), rng.random(), rng.random(), rng.random(),
            rng.random(), rng.random(), rng.uniform(-1.0, 1.0), rng.random(),
        ])
        face += 0.45 * (target - face)
        yield InputFrame(phone, voice, face.copy(), True)


def dependency_sets(analyzer: Analyzer, motion_id: int) -> tuple[set[str], set[str]]:
    reads: set[str] = set()
    writes: set[str] = set()
    seen: set[int] = set()

    def walk(current: int) -> None:
        if current in seen:
            return
        seen.add(current)
        clip = analyzer.clips.get(current)
        if clip is not None:
            for curve in populated_float_curves(clip):
                if int(curve.get("classID", 0)) == 95 and not (curve.get("path") or ""):
                    writes.add(str(curve["attribute"]))
            return
        tree = analyzer.trees.get(current)
        if tree is None:
            return
        if int(tree.get("m_BlendType", 0)) == 4:
            for child in tree.get("m_Childs", []):
                reads.add(str(child.get("m_DirectBlendParameter", "")))
                walk(int(child["m_Motion"]["fileID"]))
        else:
            reads.add(str(tree.get("m_BlendParameter", "")))
            for child in tree.get("m_Childs", []):
                walk(int(child["m_Motion"]["fileID"]))

    walk(motion_id)
    return reads, writes


def run_trace(
    analyzer: Analyzer,
    evaluator: MotionEvaluator,
    root: dict,
    decoders: list[int],
    frames: Iterable[InputFrame],
    fps: int,
    scenario: str,
    aggregate: dict,
) -> None:
    params = parameter_defaults(analyzer)
    params["IsLocal"] = 1.0
    params["YUCP/AdvancedViseme/FaceTrackingEnabled"] = 1.0
    params["YUCP/AdvancedViseme/_Internal/Speech/Hangover/UpdateAuthority"] = 1.0
    time_seconds = 0.0
    children = root.get("m_Childs", [])

    for frame in frames:
        time_seconds += 1.0 / fps
        params["YUCP/AdvancedViseme/_Internal/Time"] = time_seconds
        params["Voice"] = frame.voice
        params["YUCP/AdvancedViseme/_Internal/Viseme/Index"] = float(frame.viseme)
        write_quantized_tracking(params, frame.tracking, frame.tracking_active)

        # Decoder is an earlier Animator layer.  Its state selection is the hard
        # Oculus index; all decoder writes commit before Math reads them.
        decoder_writes: dict[str, float] = {}
        evaluator.evaluate(
            decoders[frame.viseme], 1.0, params, decoder_writes, count=False
        )
        params.update(decoder_writes)

        frame_total = PathCost()
        frame_groups_clips: Counter = Counter()
        frame_groups_curves: Counter = Counter()
        math_writes: dict[str, float] = {}
        for child_index, child in enumerate(children):
            root_weight = max(
                0.0,
                float(params.get(child.get("m_DirectBlendParameter", ""), 0.0)),
            )
            child_cost = evaluator.evaluate(
                int(child["m_Motion"]["fileID"]),
                root_weight,
                params,
                math_writes,
                count=True,
            )
            frame_total.add(child_cost)
            child_stats = aggregate["children"][child_index]
            child_stats["clips"] += child_cost.clips
            child_stats["curves"] += child_cost.curves
            child_stats["active_frames"] += int(child_cost.clips > 0)
            child_stats["max_clips"] = max(child_stats["max_clips"], child_cost.clips)
            child_stats["max_curves"] = max(child_stats["max_curves"], child_cost.curves)
            child_stats["group_clips"].update(child_cost.group_clips)
            child_stats["group_curves"].update(child_cost.group_curves)
            frame_groups_clips.update(child_cost.group_clips)
            frame_groups_curves.update(child_cost.group_curves)

        params.update(math_writes)
        aggregate["frames"] += 1
        aggregate["total_clips"] += frame_total.clips
        aggregate["total_curves"] += frame_total.curves
        aggregate["frame_clips"].append(frame_total.clips)
        aggregate["frame_curves"].append(frame_total.curves)
        aggregate["scenarios"][scenario]["frames"] += 1
        aggregate["scenarios"][scenario]["clips"] += frame_total.clips
        aggregate["scenarios"][scenario]["curves"] += frame_total.curves
        aggregate["scenarios"][scenario]["group_clips"].update(frame_groups_clips)
        aggregate["scenarios"][scenario]["group_curves"].update(frame_groups_curves)
        aggregate["groups_clips"].update(frame_groups_clips)
        aggregate["groups_curves"].update(frame_groups_curves)


def percentile(values: list[int], q: float) -> float:
    return float(np.quantile(np.asarray(values, dtype=np.float64), q))


def analyze(controller: Path, seconds: float, seeds: int) -> dict:
    analyzer = Analyzer(str(controller))
    evaluator = MotionEvaluator(analyzer)
    root, _ = selected_math_root(analyzer)
    decoders = decoder_motions(analyzer)
    children = root.get("m_Childs", [])
    child_metadata = []
    all_math_writes: set[str] = set()
    dependency_data = []
    for index, child in enumerate(children):
        motion_id = int(child["m_Motion"]["fileID"])
        reads, writes = dependency_sets(analyzer, motion_id)
        reads.add(str(child.get("m_DirectBlendParameter", "")))
        all_math_writes.update(writes)
        dependency_data.append((reads, writes))
        child_metadata.append({
            "index": index,
            "name": motion_name(analyzer, motion_id),
            "family": family_name(motion_name(analyzer, motion_id)),
            "weight_parameter": str(child.get("m_DirectBlendParameter", "")),
            "motion_id": motion_id,
        })

    aggregate = {
        "frames": 0,
        "total_clips": 0,
        "total_curves": 0,
        "frame_clips": [],
        "frame_curves": [],
        "groups_clips": Counter(),
        "groups_curves": Counter(),
        "scenarios": defaultdict(lambda: {
            "frames": 0,
            "clips": 0,
            "curves": 0,
            "group_clips": Counter(),
            "group_curves": Counter(),
        }),
        "children": [
            {
                "clips": 0,
                "curves": 0,
                "active_frames": 0,
                "max_clips": 0,
                "max_curves": 0,
                "group_clips": Counter(),
                "group_curves": Counter(),
            }
            for _ in children
        ],
    }

    for seed_offset in range(seeds):
        for fps in FPS_VALUES:
            seed = 913_781 + 10_007 * seed_offset + fps
            run_trace(
                analyzer, evaluator, root, decoders,
                realistic_frames(fps, seconds, seed, True),
                fps, "realistic-tracking", aggregate,
            )
            run_trace(
                analyzer, evaluator, root, decoders,
                realistic_frames(fps, seconds, seed, False),
                fps, "realistic-no-tracking", aggregate,
            )
            run_trace(
                analyzer, evaluator, root, decoders,
                stress_frames(fps, seconds * 0.5, seed + 37),
                fps, "randomized-stress", aggregate,
            )

    frames = aggregate["frames"]
    result_children = []
    family_totals: dict[str, dict[str, float]] = defaultdict(
        lambda: {"clips": 0.0, "curves": 0.0, "active_frames": 0.0, "members": 0.0}
    )
    for meta, raw, (reads, writes) in zip(
        child_metadata, aggregate["children"], dependency_data
    ):
        item = dict(meta)
        item.update({
            "clips_per_frame": raw["clips"] / frames,
            "curves_per_frame": raw["curves"] / frames,
            "active_frame_fraction": raw["active_frames"] / frames,
            "max_clips_in_frame": raw["max_clips"],
            "max_curves_in_frame": raw["max_curves"],
            "read_count": len(reads),
            "write_count": len(writes),
            "intra_math_read_count": len(reads & all_math_writes),
            "feedback_read_count": len(reads & writes),
            "intra_math_reads": sorted(reads & all_math_writes),
            "semantic_clips_per_frame": {
                group: count / frames for group, count in raw["group_clips"].items()
            },
            "semantic_curves_per_frame": {
                group: count / frames for group, count in raw["group_curves"].items()
            },
        })
        result_children.append(item)
        family = family_totals[meta["family"]]
        family["clips"] += raw["clips"]
        family["curves"] += raw["curves"]
        family["active_frames"] += raw["active_frames"]
        family["members"] += 1

    groups = {}
    for group in sorted(set(aggregate["groups_clips"]) | set(aggregate["groups_curves"])):
        groups[group] = {
            "clips_per_frame": aggregate["groups_clips"][group] / frames,
            "curves_per_frame": aggregate["groups_curves"][group] / frames,
        }
    families = []
    for name, raw in family_totals.items():
        families.append({
            "name": name,
            "members": int(raw["members"]),
            "clips_per_frame": raw["clips"] / frames,
            "curves_per_frame": raw["curves"] / frames,
            # This is sum of member activity, intentionally not a union.
            "member_active_frames_per_frame": raw["active_frames"] / frames,
        })

    scenarios = {}
    for name, raw in aggregate["scenarios"].items():
        scenarios[name] = {
            "frames": raw["frames"],
            "clips_per_frame": raw["clips"] / raw["frames"],
            "curves_per_frame": raw["curves"] / raw["frames"],
            "semantic_groups": {
                group: {
                    "clips_per_frame": raw["group_clips"][group] / raw["frames"],
                    "curves_per_frame": raw["group_curves"][group] / raw["frames"],
                }
                for group in sorted(set(raw["group_clips"]) | set(raw["group_curves"]))
            },
        }

    return {
        "controller": str(controller),
        "model": {
            "math_top_level_children": len(children),
            "clips": len(analyzer.clips),
            "blend_trees": len(analyzer.trees),
            "parameters": len(analyzer.controller["m_AnimatorParameters"]),
        },
        "replay": {
            "fps": list(FPS_VALUES),
            "seconds_per_trace": seconds,
            "seed_count": seeds,
            "frames": frames,
            "clips_per_frame": aggregate["total_clips"] / frames,
            "curves_per_frame": aggregate["total_curves"] / frames,
            "clips_p95": percentile(aggregate["frame_clips"], 0.95),
            "curves_p95": percentile(aggregate["frame_curves"], 0.95),
            "clips_max": max(aggregate["frame_clips"]),
            "curves_max": max(aggregate["frame_curves"]),
            "scenarios": scenarios,
        },
        "semantic_groups": dict(sorted(
            groups.items(), key=lambda pair: pair[1]["curves_per_frame"], reverse=True
        )),
        "families": sorted(families, key=lambda item: item["curves_per_frame"], reverse=True),
        "children": sorted(
            result_children, key=lambda item: item["curves_per_frame"], reverse=True
        ),
        "accounting_note": (
            "Semantic clip counts count a leaf once for every semantic group it binds; "
            "therefore group clip counts are not additive. Curve counts are additive."
        ),
    }


def markdown_report(result: dict) -> str:
    replay = result["replay"]
    lines = [
        "# YUCP AVR Math active-path hotspot audit",
        "",
        f"Controller: `{result['controller']}`",
        "",
        (
            f"Deterministic synchronous AAP replay: {replay['frames']:,} frames at "
            f"{', '.join(map(str, replay['fps']))} FPS. Mean Math work was "
            f"**{replay['clips_per_frame']:.2f} active clip references** and "
            f"**{replay['curves_per_frame']:.2f} active curve bindings** per frame "
            f"(p95 {replay['clips_p95']:.0f}/{replay['curves_p95']:.0f})."
        ),
        "",
        "## Scenarios",
        "",
        "| Scenario | Frames | Clips/frame | Curves/frame |",
        "|---|---:|---:|---:|",
    ]
    for name, item in replay["scenarios"].items():
        lines.append(
            f"| {name} | {item['frames']:,} | {item['clips_per_frame']:.2f} | "
            f"{item['curves_per_frame']:.2f} |"
        )
    lines.extend([
        "",
        "## Semantic output groups",
        "",
        "| Group | Clips/frame | Curves/frame | Curve share |",
        "|---|---:|---:|---:|",
    ])
    total_curves = replay["curves_per_frame"]
    for name, item in result["semantic_groups"].items():
        lines.append(
            f"| {name} | {item['clips_per_frame']:.2f} | "
            f"{item['curves_per_frame']:.2f} | "
            f"{item['curves_per_frame'] / total_curves:.1%} |"
        )
    lines.extend([
        "",
        "## Largest generated families",
        "",
        "| Family | Members | Clips/frame | Curves/frame | Curve share |",
        "|---|---:|---:|---:|---:|",
    ])
    for item in result["families"][:25]:
        lines.append(
            f"| {item['name']} | {item['members']} | {item['clips_per_frame']:.2f} | "
            f"{item['curves_per_frame']:.2f} | "
            f"{item['curves_per_frame'] / total_curves:.1%} |"
        )
    lines.extend([
        "",
        "## Largest individual Math-root children",
        "",
        "| # | Child | Active | Clips/frame | Curves/frame | Intra-Math reads |",
        "|---:|---|---:|---:|---:|---:|",
    ])
    for item in result["children"][:35]:
        lines.append(
            f"| {item['index']} | {item['name']} | {item['active_frame_fraction']:.1%} | "
            f"{item['clips_per_frame']:.2f} | {item['curves_per_frame']:.2f} | "
            f"{item['intra_math_read_count']} |"
        )
    lines.extend([
        "",
        "## Interpretation",
        "",
        "- A clip reference is counted only when every weight on its evaluated path is positive.",
        "- A curve binding is one populated float curve in that active leaf clip.",
        "- Every Math child is a sibling in one Direct tree. An `intra-Math read` therefore observes the previous frame's value, not a sibling's current write.",
        "- This report ranks structural sampling work. It does not convert a curve or clip count into milliseconds.",
        "- " + result["accounting_note"],
    ])
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--controller", type=Path, default=DEFAULT_CONTROLLER)
    parser.add_argument("--seconds", type=float, default=8.0)
    parser.add_argument("--seeds", type=int, default=3)
    parser.add_argument("--json", type=Path, default=DEFAULT_JSON)
    parser.add_argument("--report", type=Path, default=DEFAULT_REPORT)
    args = parser.parse_args()
    result = analyze(args.controller, args.seconds, args.seeds)
    args.json.write_text(json.dumps(result, indent=2), encoding="utf-8")
    args.report.write_text(markdown_report(result), encoding="utf-8")
    print(json.dumps(result["replay"], indent=2))
    print(f"wrote {args.json}")
    print(f"wrote {args.report}")


if __name__ == "__main__":
    main()
