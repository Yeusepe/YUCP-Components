#!/usr/bin/env python3
"""Train, validate, and generate the portable visible-face -> tongue residual.

The corpus model is deliberately avatar-portable.  Inputs and targets are
expressed as fractions of the available authored pose headroom; no EMA
coordinates are emitted into the Unity package.  Both checked models are
fitted directly to the two midsagittal tongue-tip targets that can be represented
at runtime: advance (TongueOut) and height (TongueY).

The hardened SPIRE loader, source pins, selection validation, phone mapping,
and split definitions live in ``train_transition_retention.py`` and are reused
here rather than duplicated.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import sys
from pathlib import Path
from typing import Any, Sequence

import numpy as np

import train_transition_retention as corpus


SCRIPT_DIR = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_SOURCE_MANIFEST = SCRIPT_DIR / "source_manifest.json"
DEFAULT_SELECTION_MANIFEST = SCRIPT_DIR / "Generated" / "spire_selection_manifest.json"
DEFAULT_AUDIT_JSON = SCRIPT_DIR / "Generated" / "advanced_viseme_visible_tongue_residual.json"
DEFAULT_BALANCED_AUDIT_JSON = (
    SCRIPT_DIR / "Generated" / "advanced_viseme_visible_tongue_residual_balanced.json"
)
DEFAULT_MODEL_CS = (
    REPOSITORY_ROOT
    / "Packages"
    / "com.yucp.components"
    / "Runtime"
    / "Components"
    / "Data"
    / "Generated"
    / "AdvancedVisemeVisibleTongueResidual.generated.cs"
)
EXPECTED_QUALITY_SOURCE_MODEL_CONTENT_SHA256 = "d8a567ea3b660c88f6ff451fea731a992d8ae97226c1927a875667bfec7f9279"
EXPECTED_BALANCED_SOURCE_MODEL_CONTENT_SHA256 = "c1d330639540c2d65c359762656fd5a9f6d669f7100f0437c43b3f3b02d2c93c"
EXPECTED_QUALITY_AUDIT_CONTENT_SHA256 = "eed78685582dbc0949c0102b1137eada80ab8c786357471481cab22ed15069f9"
EXPECTED_BALANCED_AUDIT_CONTENT_SHA256 = "0bcc06e661ed1b7e4e45a03a6051a3fcb51c65077d3326a4c85948f2cad8f5d8"
EXPECTED_SELECTION_CONTENT_SHA256 = "81b64a8ba071f2a8fe675f9b40947053c01d2d047b3766f43876912b1cfa20ab"

VISEMES = corpus.VISEMES
VISIBLE_AXES = ("JawOpen", "JawAdvance", "LipAperture", "LipProtrusion")
BALANCED_VISIBLE_AXES = ("JawOpen", "LipAperture", "LipProtrusion")
SOURCE_TARGET_AXES = ("TongueTipAdvanceRelativeToLips", "TongueTipHeightRelativeToJaw")
RUNTIME_OUTPUTS = ("TongueOut", "TongueY")
RUNTIME_SOURCE_TARGET_INDICES = (0, 1)
OBSERVER_FEATURES = ("current[4]", "current-fast[4]", "fast-slow[4]")
BALANCED_OBSERVER_FEATURES = ("current[3]", "current-fast[3]", "fast-slow[3]")

VISEME_COUNT = 15
VISIBLE_CHANNEL_COUNT = 4
FEATURE_COUNT = 12
VISIBLE_LATENT_COUNT = 4
TONGUE_LATENT_COUNT = 2
SOURCE_OUTPUT_COUNT = 2
RUNTIME_OUTPUT_COUNT = 2
BALANCED_VISIBLE_CHANNEL_COUNT = 3
BALANCED_FEATURE_COUNT = 9
BALANCED_TONGUE_LATENT_COUNT = 2
OBSERVER_RESPONSE_SECONDS = 0.024
HEADROOM_FLOOR = 0.075
CALIBRATION_QUANTILES = (0.01, 0.99)

MAX_ABS_VISIBLE_PROJECTION = 16.0
MAX_ABS_VISEME_MIX = 4.0
MAX_ABS_VISEME_BIAS = 2.0
MAX_ABS_TONGUE_PROJECTION = 2.0

EXPECTED_QUALITY_EVALUATION = {
    "speakerLosoImprovementPercent": 11.870289292439907,
    "speakerLosoWithReliabilityPercent": 12.490883078113447,
    "heldoutImprovementPercent": 14.739498822396357,
    "heldoutWithReliabilityPercent": 14.876356434734072,
    "heldoutAxisImprovementPercent": [14.59380908154274, 15.147302454112898],
    "heldoutEvaluationFrames": 25329,
}
EXPECTED_BALANCED_EVALUATION = {
    "speakerLosoImprovementPercent": 9.900605995910283,
    "speakerLosoWithReliabilityPercent": 10.328123441115943,
    "heldoutImprovementPercent": 11.689541896776701,
    "heldoutWithReliabilityPercent": 11.662735770866862,
    "heldoutAxisImprovementPercent": [9.913648303992783, 13.340006080922183],
    "heldoutEvaluationFrames": 25329,
}

BOOTSTRAP_RESAMPLES = 100_000
BOOTSTRAP_SEED = 20_260_713
QUALITY_STRATIFIED_UTTERANCE_BOOTSTRAP_95 = {
    "TongueOut": {
        "lower": 10.56664142373555,
        "median": 14.602147993998454,
        "upper": 18.32098504354238,
    },
    "TongueY": {
        "lower": 12.546231447217698,
        "median": 15.158964526068292,
        "upper": 17.635752202004273,
    },
}
QUALITY_WHOLE_SPEAKER_CLUSTER_95 = {
    "TongueOut": {
        "lower": 11.894807690986708,
        "median": 14.593809081542163,
        "upper": 19.440163138036283,
    },
    "TongueY": {
        "lower": 11.29257840285275,
        "median": 15.147302454112255,
        "upper": 21.895995687824012,
    },
}
QUALITY_HELDOUT_SPEAKER_IMPROVEMENT = {
    "TongueOut": [
        12.306420848644095,
        22.833042409435357,
        14.2756680224141,
        11.359002412538477,
    ],
    "TongueY": [
        13.062944548268474,
        12.828720547818506,
        10.474962155669253,
        25.47813158691321,
    ],
}
BALANCED_STRATIFIED_UTTERANCE_BOOTSTRAP_95 = {
    "TongueOut": {
        "lower": 5.730481223591603,
        "median": 9.904205363324936,
        "upper": 13.850308906645477,
    },
    "TongueY": {
        "lower": 10.684804208780502,
        "median": 13.34599967073181,
        "upper": 15.895384989855215,
    },
}
BALANCED_WHOLE_SPEAKER_CLUSTER_95 = {
    "TongueOut": {
        "lower": 7.98804642706421,
        "median": 9.913648303993316,
        "upper": 12.601549537416757,
    },
    "TongueY": {
        "lower": 9.2318322218357,
        "median": 13.340006080922706,
        "upper": 20.271144234034555,
    },
}
BALANCED_HELDOUT_SPEAKER_IMPROVEMENT = {
    "TongueOut": [
        7.536994422700527,
        14.358472871566896,
        8.555714395550428,
        10.883014747272135,
    ],
    "TongueY": [
        10.725182030517233,
        11.55858007556132,
        7.993413723560727,
        24.121977794507742,
    ],
}

ENVELOPE_SAFETY_INFLATION = 1.0001


def content_hash(document: dict[str, Any]) -> str:
    value = copy.deepcopy(document)
    value.pop("contentSha256", None)
    return corpus.canonical_sha256(value)


def finite_array(
    value: Any,
    shape: tuple[int, ...],
    name: str,
    maximum_absolute: float,
) -> np.ndarray:
    array = np.asarray(value, dtype=np.float64)
    if array.shape != shape:
        raise ValueError(f"{name} must have shape {shape}, got {array.shape}")
    if not np.all(np.isfinite(array)):
        raise ValueError(f"{name} contains a non-finite coefficient")
    if np.any(np.abs(array) > maximum_absolute):
        raise ValueError(f"{name} exceeds reviewed absolute bound {maximum_absolute}")
    return array


def finite_metric(value: Any, name: str) -> float:
    result = float(value)
    if not math.isfinite(result):
        raise ValueError(f"{name} must be finite")
    return result


def validate_source_and_selection(
    source_path: Path,
    selection_path: Path,
) -> tuple[dict[str, Any], dict[str, Any]]:
    source = corpus.load_json(source_path)
    corpus.validate_source_manifest(source)
    selection = corpus.load_json(selection_path)
    corpus.validate_selection_hash(selection)

    if selection.get("selectionContentSha256") != EXPECTED_SELECTION_CONTENT_SHA256:
        raise ValueError("Visible-tongue generation requires the reviewed 544-entry selection hash")
    if int(selection.get("selectedEntryCount", -1)) != 544:
        raise ValueError("Visible-tongue generation requires exactly 544 selected utterances")
    if selection.get("repository") != source["dataset"]["repository"]:
        raise ValueError("Selection repository does not match the pinned source manifest")
    if selection.get("repositoryRevision") != source["dataset"]["repositoryRevision"]:
        raise ValueError("Selection revision does not match the pinned source manifest")
    if selection.get("processedArchiveSha256") != source["dataset"]["processedArchiveSha256"]:
        raise ValueError("Selection archive hash does not match the pinned source manifest")

    expected_splits = {
        str(split["name"]): {
            "speakers": {int(value) for value in split["speakers"]},
            "count": len(split["speakers"]) * int(split["promptOrdinalCount"]),
        }
        for split in source["subset"]["splits"]
    }
    observed: dict[str, dict[str, Any]] = {}
    for entry in selection.get("entries", []):
        split_name = str(entry.get("split"))
        bucket = observed.setdefault(split_name, {"speakers": set(), "count": 0})
        bucket["speakers"].add(int(entry.get("speaker", -1)))
        bucket["count"] += 1
    if set(observed) != set(expected_splits):
        raise ValueError("Selection split names do not match the source manifest")
    for name, expected in expected_splits.items():
        if observed[name]["speakers"] != expected["speakers"]:
            raise ValueError(f"Selection speakers do not match split {name}")
        if observed[name]["count"] != expected["count"]:
            raise ValueError(f"Selection utterance count does not match split {name}")
    return source, selection


def checked_model_to_audit(
    checked: dict[str, Any],
    source: dict[str, Any],
    selection: dict[str, Any],
) -> dict[str, Any]:
    source_model = checked["model"]
    source_projection = np.asarray(source_model["tongueProjection"], dtype=np.float64)
    runtime_projection = source_projection[:, RUNTIME_SOURCE_TARGET_INDICES]
    source_axis_metrics = checked["evaluation"]["heldoutAxisImprovementPercent"]

    document: dict[str, Any] = {
        "schemaVersion": 1,
        "modelVersion": 2,
        "description": (
            "Dimensionless Beta-only visible-face residual inference for TongueOut and TongueY."
        ),
        "visemeOrder": list(VISEMES),
        "visibleAxes": list(VISIBLE_AXES),
        "featureOrder": list(OBSERVER_FEATURES),
        "runtimeOutputs": list(RUNTIME_OUTPUTS),
        "runtimeSourceTargetIndices": list(RUNTIME_SOURCE_TARGET_INDICES),
        "runtimeProjection": {
            "TongueOut": "TongueTipAdvanceRelativeToLips",
            "TongueY": "TongueTipHeightRelativeToJaw",
            "reason": (
                "Both targets are fitted directly from tongue-tip geometry. Midsagittal EMA "
                "cannot justify lateral, roll, or twist channels."
            ),
        },
        "transform": {
            "calibration": checked["transform"]["normalization"],
            "calibrationQuantiles": list(CALIBRATION_QUANTILES),
            "observerResponseSeconds": OBSERVER_RESPONSE_SECONDS,
            "sourceSampleRateHz": corpus.EXPECTED_SAMPLE_RATE_HZ,
            "features": list(OBSERVER_FEATURES),
            "headroomFloor": HEADROOM_FLOOR,
            "residual": checked["transform"]["residual"],
            "runtimeCalibration": checked["transform"]["runtimeCalibration"],
            "geometry": checked["transform"]["geometry"],
            "observerInput": (
                "One 24 ms two-pole observer begins from unfiltered calibrated visible semantics "
                "after viseme-center headroom normalization."
            ),
            "jawAxisNote": (
                "SPIRE JawX is anterior/posterior JawAdvance and maps to Unified Expressions JawZ, "
                "not lateral JawX."
            ),
            "runtimeSemanticMapping": {
                "JawOpen": "calibrated Unified Expressions JawOpen",
                "JawAdvance": "calibrated JawZ/JawForward; never lateral JawX",
                "LipAperture": "calibrated MouthOpen opposed by MouthClosed",
                "LipProtrusion": (
                    "calibrated max(LipFunnel,LipPucker) opposed by LipSuck"
                ),
                "validationStatus": (
                    "Semantic proxy only: SPIRE EMA and Unified Expressions are unpaired domains."
                ),
            },
        },
        "model": {
            "formula": (
                "h=x*visibleProjection; t=sum_v p[v]*(visemeBias[v]+h*visemeMix[v]); "
                "y=t*outputProjection; y*=sum_v p[v]*visemeReliability[v]"
            ),
            "visibleProjection": source_model["visibleProjection"],
            "visemeMix": source_model["visemeMix"],
            "visemeBias": source_model["visemeBias"],
            "outputProjection": runtime_projection.tolist(),
            "visemeReliability": source_model["visemeReliability"],
            "featureAbsP99": source_model["featureAbsP99"],
            "featureAbsP995": source_model["featureAbsP995"],
        },
        "evaluation": {
            **checked["evaluation"],
            "runtimeOutputHeldoutImprovementPercent": {
                output: float(source_axis_metrics[source_index])
                for output, source_index in zip(RUNTIME_OUTPUTS, RUNTIME_SOURCE_TARGET_INDICES)
            },
            "stratifiedUtteranceBootstrap95Percent": copy.deepcopy(
                QUALITY_STRATIFIED_UTTERANCE_BOOTSTRAP_95
            ),
            "wholeSpeakerCluster95Percent": copy.deepcopy(
                QUALITY_WHOLE_SPEAKER_CLUSTER_95
            ),
            "heldoutSpeakerImprovementPercent": copy.deepcopy(
                QUALITY_HELDOUT_SPEAKER_IMPROVEMENT
            ),
            "sensitivityResampling": {
                "resamples": BOOTSTRAP_RESAMPLES,
                "seed": BOOTSTRAP_SEED,
                "heldoutSpeakers": [9, 10, 11, 12],
            },
            "metric": "Relative reduction in viseme-only residual MSE; higher is better.",
            "splitPolicy": (
                "Fit speakers 1-6, development speakers 7-8, and held-out speakers 9-12; "
                "prompt ranges are disjoint exactly as pinned in source_manifest.json."
            ),
        },
        "provenance": {
            "dataset": source["dataset"],
            "subset": source["subset"],
            "selectionContentSha256": selection["selectionContentSha256"],
            "selectedEntryCount": int(selection["selectedEntryCount"]),
            "trainedModelContentSha256": checked["contentSha256"],
            "licenseNotice": (
                "Derived from the SPIRE EMA Corpus, CC BY 4.0; Bandekar, Udupa, and Ghosh (2024)."
            ),
        },
        "limitations": [
            "This is a bounded estimate from visible motion, not measured tongue tracking.",
            "The corpus is midsagittal and cannot support lateral, roll, or twist inference.",
            "The ARPAbet-to-viseme mapping is a surrogate for VRChat's hidden classifier output.",
            "EMA geometry and Unified Expressions are not paired domains; the semantic mapping is not VRCFT-validated.",
        ],
    }
    document["contentSha256"] = content_hash(document)
    return document


def balanced_checked_model_to_audit(
    checked: dict[str, Any],
    source: dict[str, Any],
    selection: dict[str, Any],
) -> dict[str, Any]:
    source_model = checked["model"]
    source_axis_metrics = checked["evaluation"]["heldoutAxisImprovementPercent"]
    document: dict[str, Any] = {
        "schemaVersion": 1,
        "modelVersion": 2,
        "description": (
            "Dimensionless Beta-only three-input visible-face residual inference for TongueOut and TongueY."
        ),
        "visemeOrder": list(VISEMES),
        "visibleAxes": list(BALANCED_VISIBLE_AXES),
        "featureOrder": list(BALANCED_OBSERVER_FEATURES),
        "runtimeOutputs": list(RUNTIME_OUTPUTS),
        "runtimeSourceTargetIndices": list(RUNTIME_SOURCE_TARGET_INDICES),
        "runtimeProjection": {
            "TongueOut": "TongueTipAdvanceRelativeToLips",
            "TongueY": "TongueTipHeightRelativeToJaw",
            "reason": (
                "Balanced is independently fitted without jaw advance; it is not the Quality model "
                "with a zero-filled input."
            ),
        },
        "transform": {
            "calibration": checked["transform"]["normalization"],
            "calibrationQuantiles": list(CALIBRATION_QUANTILES),
            "observerResponseSeconds": OBSERVER_RESPONSE_SECONDS,
            "sourceSampleRateHz": corpus.EXPECTED_SAMPLE_RATE_HZ,
            "features": list(BALANCED_OBSERVER_FEATURES),
            "headroomFloor": HEADROOM_FLOOR,
            "residual": checked["transform"]["residual"],
            "runtimeCalibration": checked["transform"]["runtimeCalibration"],
            "geometry": checked["transform"]["geometry"],
            "observerInput": (
                "One 24 ms two-pole observer begins from unfiltered calibrated visible semantics "
                "after viseme-center headroom normalization."
            ),
            "runtimeSemanticMapping": {
                "JawOpen": "calibrated Unified Expressions JawOpen",
                "LipAperture": "calibrated MouthOpen opposed by MouthClosed",
                "LipProtrusion": (
                    "calibrated max(LipFunnel,LipPucker) opposed by LipSuck"
                ),
                "validationStatus": (
                    "Semantic proxy only: SPIRE EMA and Unified Expressions are unpaired domains."
                ),
            },
        },
        "model": {
            "formula": (
                "h=x*visibleProjection; t=sum_v p[v]*(visemeBias[v]+h*visemeMix[v]); "
                "y=t*outputProjection; y*=sum_v p[v]*visemeReliability[v]"
            ),
            "visibleProjection": source_model["visibleProjection"],
            "visemeMix": source_model["visemeMix"],
            "visemeBias": source_model["visemeBias"],
            "outputProjection": source_model["tongueProjection"],
            "visemeReliability": source_model["visemeReliability"],
            "featureAbsP99": source_model["featureAbsP99"],
            "featureAbsP995": source_model["featureAbsP995"],
        },
        "evaluation": {
            **checked["evaluation"],
            "runtimeOutputHeldoutImprovementPercent": {
                output: float(source_axis_metrics[index])
                for index, output in enumerate(RUNTIME_OUTPUTS)
            },
            "stratifiedUtteranceBootstrap95Percent": copy.deepcopy(
                BALANCED_STRATIFIED_UTTERANCE_BOOTSTRAP_95
            ),
            "wholeSpeakerCluster95Percent": copy.deepcopy(
                BALANCED_WHOLE_SPEAKER_CLUSTER_95
            ),
            "heldoutSpeakerImprovementPercent": copy.deepcopy(
                BALANCED_HELDOUT_SPEAKER_IMPROVEMENT
            ),
            "sensitivityResampling": {
                "resamples": BOOTSTRAP_RESAMPLES,
                "seed": BOOTSTRAP_SEED,
                "heldoutSpeakers": [9, 10, 11, 12],
            },
            "metric": "Relative reduction in viseme-only residual MSE; higher is better.",
            "splitPolicy": (
                "Fit speakers 1-6, development speakers 7-8, and held-out speakers 9-12; "
                "prompt ranges are disjoint exactly as pinned in source_manifest.json."
            ),
        },
        "provenance": {
            "dataset": source["dataset"],
            "subset": source["subset"],
            "selectionContentSha256": selection["selectionContentSha256"],
            "selectedEntryCount": int(selection["selectedEntryCount"]),
            "trainedModelContentSha256": checked["contentSha256"],
            "licenseNotice": (
                "Derived from the SPIRE EMA Corpus, CC BY 4.0; Bandekar, Udupa, and Ghosh (2024)."
            ),
        },
        "limitations": [
            "This is a bounded estimate from visible motion, not measured tongue tracking.",
            "Balanced has no jaw-advance measurement and is less accurate than Quality.",
            "The held-out gains remain modest and are estimates from only four held-out speakers.",
            "The corpus is midsagittal and cannot support lateral, roll, or twist inference.",
            "The estimator must remain gated to Beta mode and yield to measured tongue tracking.",
            "EMA geometry and Unified Expressions are not paired domains; the semantic mapping is not VRCFT-validated.",
        ],
    }
    document["contentSha256"] = content_hash(document)
    return document


def validate_sensitivity_evaluation(evaluation: dict[str, Any], prefix: str) -> None:
    for field in (
        "stratifiedUtteranceBootstrap95Percent",
        "wholeSpeakerCluster95Percent",
    ):
        intervals = evaluation.get(field)
        if not isinstance(intervals, dict):
            raise ValueError(f"{prefix} audit is missing {field}")
        for output in RUNTIME_OUTPUTS:
            interval = intervals.get(output)
            if not isinstance(interval, dict):
                raise ValueError(f"{prefix}.{field}.{output} is missing")
            lower = finite_metric(interval.get("lower"), f"{prefix}.{field}.{output}.lower")
            median = finite_metric(interval.get("median"), f"{prefix}.{field}.{output}.median")
            upper = finite_metric(interval.get("upper"), f"{prefix}.{field}.{output}.upper")
            if not 0.0 < lower <= median <= upper:
                raise ValueError(f"{prefix}.{field}.{output} must be ordered and positive")

    speaker_points = evaluation.get("heldoutSpeakerImprovementPercent")
    if not isinstance(speaker_points, dict):
        raise ValueError(f"{prefix} audit is missing held-out speaker sensitivity points")
    for output in RUNTIME_OUTPUTS:
        points = finite_array(
            speaker_points.get(output),
            (4,),
            f"{prefix}.heldoutSpeakerImprovementPercent.{output}",
            100.0,
        )
        if np.any(points <= 0.0):
            raise ValueError(f"{prefix} held-out speaker gains must remain positive for {output}")

    resampling = evaluation.get("sensitivityResampling")
    if not isinstance(resampling, dict):
        raise ValueError(f"{prefix} audit is missing sensitivity-resampling provenance")
    if int(resampling.get("resamples", -1)) != BOOTSTRAP_RESAMPLES:
        raise ValueError(f"{prefix} bootstrap resample count changed")
    if int(resampling.get("seed", -1)) != BOOTSTRAP_SEED:
        raise ValueError(f"{prefix} bootstrap seed changed")
    if tuple(resampling.get("heldoutSpeakers", ())) != (9, 10, 11, 12):
        raise ValueError(f"{prefix} held-out sensitivity speakers changed")


def validate_audit(document: dict[str, Any]) -> None:
    expected_hash = document.get("contentSha256")
    if expected_hash != EXPECTED_QUALITY_AUDIT_CONTENT_SHA256:
        raise ValueError("Visible-tongue audit is not the reviewed canonical artifact")
    if content_hash(document) != EXPECTED_QUALITY_AUDIT_CONTENT_SHA256:
        raise ValueError("Visible-tongue audit canonical content hash is invalid")
    if document.get("schemaVersion") != 1 or document.get("modelVersion") != 2:
        raise ValueError("Unsupported visible-tongue audit schema/model version")
    if tuple(document.get("visemeOrder", ())) != VISEMES:
        raise ValueError("Visible-tongue audit viseme order changed")
    if tuple(document.get("visibleAxes", ())) != VISIBLE_AXES:
        raise ValueError("Visible-tongue audit visible-axis order changed")
    if tuple(document.get("runtimeOutputs", ())) != RUNTIME_OUTPUTS:
        raise ValueError("Visible-tongue audit runtime-output order changed")
    if tuple(document.get("runtimeSourceTargetIndices", ())) != RUNTIME_SOURCE_TARGET_INDICES:
        raise ValueError("Visible-tongue runtime projection indices changed")

    model = document.get("model")
    if not isinstance(model, dict):
        raise ValueError("Visible-tongue audit is missing model coefficients")
    finite_array(
        model.get("visibleProjection"),
        (FEATURE_COUNT, VISIBLE_LATENT_COUNT),
        "visibleProjection",
        MAX_ABS_VISIBLE_PROJECTION,
    )
    finite_array(
        model.get("visemeMix"),
        (VISEME_COUNT, VISIBLE_LATENT_COUNT, TONGUE_LATENT_COUNT),
        "visemeMix",
        MAX_ABS_VISEME_MIX,
    )
    finite_array(
        model.get("visemeBias"),
        (VISEME_COUNT, TONGUE_LATENT_COUNT),
        "visemeBias",
        MAX_ABS_VISEME_BIAS,
    )
    finite_array(
        model.get("outputProjection"),
        (TONGUE_LATENT_COUNT, RUNTIME_OUTPUT_COUNT),
        "outputProjection",
        MAX_ABS_TONGUE_PROJECTION,
    )
    reliability = finite_array(
        model.get("visemeReliability"),
        (VISEME_COUNT,),
        "visemeReliability",
        1.0,
    )
    if np.any(reliability < 0.0):
        raise ValueError("Visible-tongue reliability must stay in [0,1]")
    feature_p99 = finite_array(
        model.get("featureAbsP99"),
        (FEATURE_COUNT,),
        "featureAbsP99",
        2.0,
    )
    feature_p995 = finite_array(
        model.get("featureAbsP995"),
        (FEATURE_COUNT,),
        "featureAbsP995",
        2.0,
    )
    quality_safe = np.asarray([1.0] * VISIBLE_CHANNEL_COUNT + [2.0] * 8)
    if (
        np.any(feature_p99 <= 0.0)
        or np.any(feature_p99 > feature_p995)
        or np.any(feature_p995 > quality_safe)
    ):
        raise ValueError("Visible-tongue feature quantiles must be positive and ordered")

    evaluation = document.get("evaluation")
    if not isinstance(evaluation, dict):
        raise ValueError("Visible-tongue audit is missing evaluation")
    if finite_metric(
        evaluation.get("heldoutWithReliabilityPercent"),
        "heldoutWithReliabilityPercent",
    ) <= 0.0:
        raise ValueError("Visible-tongue audit fails held-out improvement gate")
    runtime_metrics = evaluation.get("runtimeOutputHeldoutImprovementPercent")
    if not isinstance(runtime_metrics, dict):
        raise ValueError("Visible-tongue audit is missing per-runtime-output metrics")
    for output in RUNTIME_OUTPUTS:
        if finite_metric(runtime_metrics.get(output), output) <= 0.0:
            raise ValueError(f"Visible-tongue audit fails held-out gate for {output}")
    validate_sensitivity_evaluation(evaluation, "quality")

    provenance = document.get("provenance")
    if not isinstance(provenance, dict):
        raise ValueError("Visible-tongue audit is missing provenance")
    if provenance.get("selectionContentSha256") != EXPECTED_SELECTION_CONTENT_SHA256:
        raise ValueError("Visible-tongue audit selection hash changed")
    if provenance.get("trainedModelContentSha256") != EXPECTED_QUALITY_SOURCE_MODEL_CONTENT_SHA256:
        raise ValueError("Visible-tongue audit trained-model content hash changed")


def validate_balanced_audit(document: dict[str, Any]) -> None:
    expected_hash = document.get("contentSha256")
    if expected_hash != EXPECTED_BALANCED_AUDIT_CONTENT_SHA256:
        raise ValueError("Balanced visible-tongue audit is not the reviewed canonical artifact")
    if content_hash(document) != EXPECTED_BALANCED_AUDIT_CONTENT_SHA256:
        raise ValueError("Balanced visible-tongue audit canonical content hash is invalid")
    if document.get("schemaVersion") != 1 or document.get("modelVersion") != 2:
        raise ValueError("Unsupported Balanced visible-tongue audit schema/model version")
    if tuple(document.get("visemeOrder", ())) != VISEMES:
        raise ValueError("Balanced visible-tongue audit viseme order changed")
    if tuple(document.get("visibleAxes", ())) != BALANCED_VISIBLE_AXES:
        raise ValueError("Balanced visible-tongue audit visible-axis order changed")
    if tuple(document.get("runtimeOutputs", ())) != RUNTIME_OUTPUTS:
        raise ValueError("Balanced visible-tongue audit runtime-output order changed")
    if tuple(document.get("runtimeSourceTargetIndices", ())) != RUNTIME_SOURCE_TARGET_INDICES:
        raise ValueError("Balanced visible-tongue runtime projection indices changed")

    model = document.get("model")
    if not isinstance(model, dict):
        raise ValueError("Balanced visible-tongue audit is missing model coefficients")
    finite_array(
        model.get("visibleProjection"),
        (BALANCED_FEATURE_COUNT, VISIBLE_LATENT_COUNT),
        "balanced.visibleProjection",
        MAX_ABS_VISIBLE_PROJECTION,
    )
    finite_array(
        model.get("visemeMix"),
        (VISEME_COUNT, VISIBLE_LATENT_COUNT, BALANCED_TONGUE_LATENT_COUNT),
        "balanced.visemeMix",
        MAX_ABS_VISEME_MIX,
    )
    finite_array(
        model.get("visemeBias"),
        (VISEME_COUNT, BALANCED_TONGUE_LATENT_COUNT),
        "balanced.visemeBias",
        MAX_ABS_VISEME_BIAS,
    )
    finite_array(
        model.get("outputProjection"),
        (BALANCED_TONGUE_LATENT_COUNT, RUNTIME_OUTPUT_COUNT),
        "balanced.outputProjection",
        MAX_ABS_TONGUE_PROJECTION,
    )
    reliability = finite_array(
        model.get("visemeReliability"),
        (VISEME_COUNT,),
        "balanced.visemeReliability",
        1.0,
    )
    if np.any(reliability < 0.0):
        raise ValueError("Balanced visible-tongue reliability must stay in [0,1]")
    feature_p99 = finite_array(
        model.get("featureAbsP99"),
        (BALANCED_FEATURE_COUNT,),
        "balanced.featureAbsP99",
        2.0,
    )
    feature_p995 = finite_array(
        model.get("featureAbsP995"),
        (BALANCED_FEATURE_COUNT,),
        "balanced.featureAbsP995",
        2.0,
    )
    balanced_safe = np.asarray([1.0] * BALANCED_VISIBLE_CHANNEL_COUNT + [2.0] * 6)
    if (
        np.any(feature_p99 <= 0.0)
        or np.any(feature_p99 > feature_p995)
        or np.any(feature_p995 > balanced_safe)
    ):
        raise ValueError("Balanced feature quantiles must be positive and ordered")

    evaluation = document.get("evaluation")
    if not isinstance(evaluation, dict):
        raise ValueError("Balanced visible-tongue audit is missing evaluation")
    if finite_metric(
        evaluation.get("heldoutWithReliabilityPercent"),
        "balanced.heldoutWithReliabilityPercent",
    ) <= 0.0:
        raise ValueError("Balanced visible-tongue audit fails held-out improvement gate")
    runtime_metrics = evaluation.get("runtimeOutputHeldoutImprovementPercent")
    if not isinstance(runtime_metrics, dict):
        raise ValueError("Balanced audit is missing runtime metrics")
    for output in RUNTIME_OUTPUTS:
        if finite_metric(runtime_metrics.get(output), f"balanced.{output}") <= 0.0:
            raise ValueError(f"Balanced audit fails held-out gate for {output}")
    validate_sensitivity_evaluation(evaluation, "balanced")

    provenance = document.get("provenance")
    if not isinstance(provenance, dict):
        raise ValueError("Balanced visible-tongue audit is missing provenance")
    if provenance.get("selectionContentSha256") != EXPECTED_SELECTION_CONTENT_SHA256:
        raise ValueError("Balanced visible-tongue audit selection hash changed")
    if provenance.get("trainedModelContentSha256") != EXPECTED_BALANCED_SOURCE_MODEL_CONTENT_SHA256:
        raise ValueError("Balanced visible-tongue audit trained-model content hash changed")


def format_float(value: float) -> str:
    value = float(value)
    if value == 0.0:
        value = 0.0
    return f"{value:.9f}f"


def flattened_float_lines(array: np.ndarray, indent: str = "            ") -> list[str]:
    flat = np.asarray(array, dtype=np.float64).reshape(-1)
    lines: list[str] = []
    for start in range(0, flat.size, 8):
        values = ", ".join(format_float(value) for value in flat[start : start + 8])
        lines.append(indent + values + ",")
    return lines


def append_dual_float_tables(
    lines: list[str],
    name: str,
    balanced: np.ndarray,
    quality: np.ndarray,
    comment: str,
) -> None:
    lines.extend(
        [
            "        };",
            "",
            f"        // {comment}",
            f"        private static readonly float[] Balanced{name}Values =",
            "        {",
        ]
    )
    lines.extend(flattened_float_lines(balanced))
    lines.extend(
        [
            "        };",
            "",
            f"        private static readonly float[] Quality{name}Values =",
            "        {",
        ]
    )
    lines.extend(flattened_float_lines(quality))


def inflate_envelope_outward(values: np.ndarray) -> np.ndarray:
    inflated = np.asarray(values, dtype=np.float64) * ENVELOPE_SAFETY_INFLATION
    emitted = np.asarray(inflated, dtype=np.float32)
    emitted = np.nextafter(emitted, np.float32(np.inf), dtype=np.float32)
    return emitted.astype(np.float64)


def derive_runtime_tables(model: dict[str, Any], visible_channel_count: int) -> dict[str, np.ndarray]:
    visible = np.asarray(model["visibleProjection"], dtype=np.float64)
    mix = np.asarray(model["visemeMix"], dtype=np.float64)
    bias = np.asarray(model["visemeBias"], dtype=np.float64)
    output = np.asarray(model["outputProjection"], dtype=np.float64)
    feature_p99 = np.asarray(model["featureAbsP99"], dtype=np.float64)
    feature_p995 = np.asarray(model["featureAbsP995"], dtype=np.float64)
    feature_safe = np.concatenate(
        (
            np.ones(visible_channel_count, dtype=np.float64),
            np.full(visible_channel_count * 2, 2.0, dtype=np.float64),
        )
    )
    if (
        visible.shape[0] != feature_safe.size
        or feature_p99.shape != feature_safe.shape
        or feature_p995.shape != feature_safe.shape
    ):
        raise ValueError("Runtime-bound derivation received an inconsistent feature layout")

    collapsed_bias = bias @ output
    collapsed_feature = np.einsum("fk,vkt,to->vfo", visible, mix, output)

    # Each emitted envelope is derived from the previously emitted (already inflated)
    # envelope. This makes the bounds composable in an Animator graph without relying
    # on exact decimal/float rounding at a later stage.
    visible_bound = inflate_envelope_outward(
        np.sum(feature_safe[:, None] * np.abs(visible), axis=0)
    )
    conditional_tongue_bound = inflate_envelope_outward(
        np.abs(bias) + np.einsum("k,vkt->vt", visible_bound, np.abs(mix))
    )
    tongue_bound = inflate_envelope_outward(
        np.max(conditional_tongue_bound, axis=0)
    )
    output_bound = inflate_envelope_outward(
        np.einsum("t,to->o", tongue_bound, np.abs(output))
    )
    for name, values in (
        ("collapsedBias", collapsed_bias),
        ("collapsedFeature", collapsed_feature),
        ("featureAbsP99", feature_p99),
        ("featureAbsP995", feature_p995),
        ("featureSafeBound", feature_safe),
        ("visibleLatentSafeBound", visible_bound),
        ("conditionalTongueLatentSafeBound", conditional_tongue_bound),
        ("tongueLatentSafeBound", tongue_bound),
        ("conservativeOutputBound", output_bound),
    ):
        if not np.all(np.isfinite(values)) or np.any(values < 0.0) and "collapsed" not in name:
            raise ValueError(f"Derived runtime table {name} is invalid")
    return {
        "collapsedBias": collapsed_bias,
        "collapsedFeature": collapsed_feature,
        "featureAbsP99": feature_p99,
        "featureAbsP995": feature_p995,
        "featureSafeBound": feature_safe,
        "visibleLatentSafeBound": visible_bound,
        "conditionalTongueLatentSafeBound": conditional_tongue_bound,
        "tongueLatentSafeBound": tongue_bound,
        "conservativeOutputBound": output_bound,
    }


def generate_csharp(
    quality_document: dict[str, Any],
    balanced_document: dict[str, Any],
) -> str:
    validate_audit(quality_document)
    validate_balanced_audit(balanced_document)
    quality = quality_document["model"]
    balanced = balanced_document["model"]
    q_visible = np.asarray(quality["visibleProjection"], dtype=np.float64)
    q_mix = np.asarray(quality["visemeMix"], dtype=np.float64)
    q_bias = np.asarray(quality["visemeBias"], dtype=np.float64)
    q_output = np.asarray(quality["outputProjection"], dtype=np.float64)
    q_reliability = np.asarray(quality["visemeReliability"], dtype=np.float64)
    b_visible = np.asarray(balanced["visibleProjection"], dtype=np.float64)
    b_mix = np.asarray(balanced["visemeMix"], dtype=np.float64)
    b_bias = np.asarray(balanced["visemeBias"], dtype=np.float64)
    b_output = np.asarray(balanced["outputProjection"], dtype=np.float64)
    b_reliability = np.asarray(balanced["visemeReliability"], dtype=np.float64)
    q_runtime = derive_runtime_tables(quality, VISIBLE_CHANNEL_COUNT)
    b_runtime = derive_runtime_tables(balanced, BALANCED_VISIBLE_CHANNEL_COUNT)

    lines = [
        "// <auto-generated />",
        "// Generated by Tools/AdvancedVisemeTraining/train_visible_tongue_residual.py.",
        "// Derived from the SPIRE EMA Corpus (Bandekar, Udupa, Ghosh), CC BY 4.0.",
        "// Source: https://huggingface.co/datasets/SpireLab/SPIRE_EMA_CORPUS",
        "// Balanced and Quality are independent fits; neither fabricates a missing input.",
        "// Outputs are bounded tongue-tip advance (TongueOut) and tip height (TongueY) residuals.",
        "",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "namespace YUCP.Components",
        "{",
        "    public enum AdvancedVisemeVisibleTongueModelKind : byte",
        "    {",
        "        Balanced = 0,",
        "        Quality = 1",
        "    }",
        "",
        "    public enum AdvancedVisemeVisibleFeatureChannel : byte",
        "    {",
        "        JawOpen = 0,",
        "        JawAdvance = 1,",
        "        LipAperture = 2,",
        "        LipProtrusion = 3",
        "    }",
        "",
        "    public enum AdvancedVisemeVisibleTongueOutput : byte",
        "    {",
        "        TongueOut = 0,",
        "        TongueY = 1",
        "    }",
        "",
        "    /// <summary>Portable Beta-only visible-face to tongue-tip residual coefficients.</summary>",
        "    public static class AdvancedVisemeVisibleTongueResidual",
        "    {",
        "        public const int ModelVersion = 2;",
        "        public const int VisemeCount = 15;",
        "        public const int FeatureStageCount = 3;",
        "        public const int OutputCount = 2;",
        "        public const int VisibleLatentCount = 4;",
        f"        public const float ObserverResponseSeconds = {format_float(OBSERVER_RESPONSE_SECONDS)};",
        f"        public const float HeadroomFloor = {format_float(HEADROOM_FLOOR)};",
        f"        public const float EnvelopeSafetyInflation = {format_float(ENVELOPE_SAFETY_INFLATION)};",
        f"        public const string BalancedContentSha256 = \"{balanced_document['contentSha256']}\";",
        f"        public const string QualityContentSha256 = \"{quality_document['contentSha256']}\";",
        f"        public const string BalancedSourceModelContentSha256 = \"{EXPECTED_BALANCED_SOURCE_MODEL_CONTENT_SHA256}\";",
        f"        public const string QualitySourceModelContentSha256 = \"{EXPECTED_QUALITY_SOURCE_MODEL_CONTENT_SHA256}\";",
        f"        public const float BalancedHeldoutImprovementPercent = {format_float(balanced_document['evaluation']['heldoutWithReliabilityPercent'])};",
        f"        public const float QualityHeldoutImprovementPercent = {format_float(quality_document['evaluation']['heldoutWithReliabilityPercent'])};",
        "",
        "        // Flattened [feature, visible latent].",
        "        private static readonly float[] BalancedInputValues =",
        "        {",
    ]
    lines.extend(flattened_float_lines(b_visible))
    lines.extend(["        };", "", "        private static readonly float[] QualityInputValues =", "        {"])
    lines.extend(flattened_float_lines(q_visible))
    lines.extend(
        [
            "        };",
            "",
            "        // Flattened [viseme, visible latent, tongue latent].",
            "        private static readonly float[] BalancedVisemeValues =",
            "        {",
        ]
    )
    lines.extend(flattened_float_lines(b_mix))
    lines.extend(["        };", "", "        private static readonly float[] QualityVisemeValues =", "        {"])
    lines.extend(flattened_float_lines(q_mix))
    lines.extend(
        [
            "        };",
            "",
            "        // Flattened [viseme, tongue latent].",
            "        private static readonly float[] BalancedBiasValues =",
            "        {",
        ]
    )
    lines.extend(flattened_float_lines(b_bias))
    lines.extend(["        };", "", "        private static readonly float[] QualityBiasValues =", "        {"])
    lines.extend(flattened_float_lines(q_bias))
    lines.extend(
        [
            "        };",
            "",
            "        // Flattened [tongue latent, output].",
            "        private static readonly float[] BalancedOutputValues =",
            "        {",
        ]
    )
    lines.extend(flattened_float_lines(b_output))
    lines.extend(["        };", "", "        private static readonly float[] QualityOutputValues =", "        {"])
    lines.extend(flattened_float_lines(q_output))
    lines.extend(
        [
            "        };",
            "",
            "        private static readonly float[] BalancedReliabilityValues =",
            "        {",
        ]
    )
    lines.extend(flattened_float_lines(b_reliability))
    lines.extend(["        };", "", "        private static readonly float[] QualityReliabilityValues =", "        {"])
    lines.extend(flattened_float_lines(q_reliability))
    append_dual_float_tables(
        lines,
        "FeatureAbsP99",
        b_runtime["featureAbsP99"],
        q_runtime["featureAbsP99"],
        "Empirical training |feature| 99th percentiles; diagnostics only, never clamps.",
    )
    append_dual_float_tables(
        lines,
        "FeatureAbsP995",
        b_runtime["featureAbsP995"],
        q_runtime["featureAbsP995"],
        "Empirical training |feature| 99.5th percentiles; diagnostics only, never clamps.",
    )
    append_dual_float_tables(
        lines,
        "FeatureSafeBound",
        b_runtime["featureSafeBound"],
        q_runtime["featureSafeBound"],
        "Hard algebraic feature envelopes: current=1; temporal differences=2.",
    )
    append_dual_float_tables(
        lines,
        "CollapsedBias",
        b_runtime["collapsedBias"],
        q_runtime["collapsedBias"],
        "Collapsed [viseme, output] affine biases.",
    )
    append_dual_float_tables(
        lines,
        "CollapsedFeature",
        b_runtime["collapsedFeature"],
        q_runtime["collapsedFeature"],
        "Collapsed [viseme, feature, output] affine coefficients.",
    )
    append_dual_float_tables(
        lines,
        "VisibleLatentSafeBound",
        b_runtime["visibleLatentSafeBound"],
        q_runtime["visibleLatentSafeBound"],
        "Inflated composable A[k] bounds for visible latents.",
    )
    append_dual_float_tables(
        lines,
        "ConditionalTongueLatentSafeBound",
        b_runtime["conditionalTongueLatentSafeBound"],
        q_runtime["conditionalTongueLatentSafeBound"],
        "Inflated composable B[viseme,tongue] bounds.",
    )
    append_dual_float_tables(
        lines,
        "TongueLatentSafeBound",
        b_runtime["tongueLatentSafeBound"],
        q_runtime["tongueLatentSafeBound"],
        "Inflated composable C[tongue] bounds across visemes.",
    )
    append_dual_float_tables(
        lines,
        "ConservativeOutputBound",
        b_runtime["conservativeOutputBound"],
        q_runtime["conservativeOutputBound"],
        "Inflated composable D[output] bounds before the final clamp.",
    )
    lines.extend(
        [
            "        };",
            "",
            "        public static string ContentSha256(AdvancedVisemeVisibleTongueModelKind kind)",
            "        {",
            "            ValidateKind(kind);",
            "            return kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedContentSha256",
            "                : QualityContentSha256;",
            "        }",
            "",
            "        public static int FeatureChannelCount(AdvancedVisemeVisibleTongueModelKind kind)",
            "        {",
            "            ValidateKind(kind);",
            "            return kind == AdvancedVisemeVisibleTongueModelKind.Balanced ? 3 : 4;",
            "        }",
            "",
            "        public static int FeatureCount(AdvancedVisemeVisibleTongueModelKind kind)",
            "        {",
            "            return FeatureChannelCount(kind) * FeatureStageCount;",
            "        }",
            "",
            "        public static int LatentCount(AdvancedVisemeVisibleTongueModelKind kind)",
            "        {",
            "            ValidateKind(kind);",
            "            return VisibleLatentCount;",
            "        }",
            "",
            "        public static int TongueLatentCount(AdvancedVisemeVisibleTongueModelKind kind)",
            "        {",
            "            ValidateKind(kind);",
            "            return 2;",
            "        }",
            "",
            "        public static int FeatureChannelIndex(",
            "            AdvancedVisemeVisibleTongueModelKind kind,",
            "            AdvancedVisemeVisibleFeatureChannel channel)",
            "        {",
            "            ValidateKind(kind);",
            "            if (kind == AdvancedVisemeVisibleTongueModelKind.Quality)",
            "            {",
            "                var qualityIndex = (int)channel;",
            "                RequireIndex(qualityIndex, 4, nameof(channel));",
            "                return qualityIndex;",
            "            }",
            "",
            "            switch (channel)",
            "            {",
            "                case AdvancedVisemeVisibleFeatureChannel.JawOpen: return 0;",
            "                case AdvancedVisemeVisibleFeatureChannel.LipAperture: return 1;",
            "                case AdvancedVisemeVisibleFeatureChannel.LipProtrusion: return 2;",
            "                default: throw new ArgumentOutOfRangeException(nameof(channel),",
            "                    \"Balanced has no JawAdvance input; use the independently fitted three-input table.\");",
            "            }",
            "        }",
            "",
            "        public static int FeatureIndex(",
            "            AdvancedVisemeVisibleTongueModelKind kind,",
            "            int stage,",
            "            AdvancedVisemeVisibleFeatureChannel channel)",
            "        {",
            "            RequireIndex(stage, FeatureStageCount, nameof(stage));",
            "            return stage * FeatureChannelCount(kind) + FeatureChannelIndex(kind, channel);",
            "        }",
            "",
            "        public static float FeatureAbsP99(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int feature)",
            "        {",
            "            RequireIndex(feature, FeatureCount(kind), nameof(feature));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedFeatureAbsP99Values : QualityFeatureAbsP99Values;",
            "            return values[feature];",
            "        }",
            "",
            "        public static float FeatureAbsP995(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int feature)",
            "        {",
            "            RequireIndex(feature, FeatureCount(kind), nameof(feature));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedFeatureAbsP995Values : QualityFeatureAbsP995Values;",
            "            return values[feature];",
            "        }",
            "",
            "        public static float FeatureSafeBound(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int feature)",
            "        {",
            "            RequireIndex(feature, FeatureCount(kind), nameof(feature));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedFeatureSafeBoundValues : QualityFeatureSafeBoundValues;",
            "            return values[feature];",
            "        }",
            "",
            "        public static float CollapsedBias(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int viseme,",
            "            AdvancedVisemeVisibleTongueOutput output)",
            "        {",
            "            RequireIndex(viseme, VisemeCount, nameof(viseme));",
            "            var outputIndex = (int)output;",
            "            RequireIndex(outputIndex, OutputCount, nameof(output));",
            "            ValidateKind(kind);",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedCollapsedBiasValues : QualityCollapsedBiasValues;",
            "            return values[viseme * OutputCount + outputIndex];",
            "        }",
            "",
            "        public static float CollapsedFeatureCoefficient(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int viseme, int feature,",
            "            AdvancedVisemeVisibleTongueOutput output)",
            "        {",
            "            RequireIndex(viseme, VisemeCount, nameof(viseme));",
            "            var featureCount = FeatureCount(kind);",
            "            RequireIndex(feature, featureCount, nameof(feature));",
            "            var outputIndex = (int)output;",
            "            RequireIndex(outputIndex, OutputCount, nameof(output));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedCollapsedFeatureValues : QualityCollapsedFeatureValues;",
            "            return values[(viseme * featureCount + feature) * OutputCount + outputIndex];",
            "        }",
            "",
            "        public static float VisibleLatentSafeBound(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int visibleLatent)",
            "        {",
            "            RequireIndex(visibleLatent, LatentCount(kind), nameof(visibleLatent));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedVisibleLatentSafeBoundValues : QualityVisibleLatentSafeBoundValues;",
            "            return values[visibleLatent];",
            "        }",
            "",
            "        public static float ConditionalTongueLatentSafeBound(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int viseme, int tongueLatent)",
            "        {",
            "            RequireIndex(viseme, VisemeCount, nameof(viseme));",
            "            var tongueCount = TongueLatentCount(kind);",
            "            RequireIndex(tongueLatent, tongueCount, nameof(tongueLatent));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedConditionalTongueLatentSafeBoundValues",
            "                : QualityConditionalTongueLatentSafeBoundValues;",
            "            return values[viseme * tongueCount + tongueLatent];",
            "        }",
            "",
            "        public static float TongueLatentSafeBound(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int tongueLatent)",
            "        {",
            "            RequireIndex(tongueLatent, TongueLatentCount(kind), nameof(tongueLatent));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedTongueLatentSafeBoundValues : QualityTongueLatentSafeBoundValues;",
            "            return values[tongueLatent];",
            "        }",
            "",
            "        public static float ConservativeOutputBound(",
            "            AdvancedVisemeVisibleTongueModelKind kind,",
            "            AdvancedVisemeVisibleTongueOutput output)",
            "        {",
            "            var outputIndex = (int)output;",
            "            RequireIndex(outputIndex, OutputCount, nameof(output));",
            "            ValidateKind(kind);",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedConservativeOutputBoundValues : QualityConservativeOutputBoundValues;",
            "            return values[outputIndex];",
            "        }",
            "",
            "        public static float InputProjection(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int feature, int latent)",
            "        {",
            "            RequireIndex(feature, FeatureCount(kind), nameof(feature));",
            "            RequireIndex(latent, LatentCount(kind), nameof(latent));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedInputValues : QualityInputValues;",
            "            return values[feature * VisibleLatentCount + latent];",
            "        }",
            "",
            "        public static float VisemeMix(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int viseme, int latent, int tongueLatent)",
            "        {",
            "            RequireIndex(viseme, VisemeCount, nameof(viseme));",
            "            RequireIndex(latent, LatentCount(kind), nameof(latent));",
            "            var tongueCount = TongueLatentCount(kind);",
            "            RequireIndex(tongueLatent, tongueCount, nameof(tongueLatent));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedVisemeValues : QualityVisemeValues;",
            "            return values[(viseme * VisibleLatentCount + latent) * tongueCount + tongueLatent];",
            "        }",
            "",
            "        public static float VisemeBias(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int viseme, int tongueLatent)",
            "        {",
            "            RequireIndex(viseme, VisemeCount, nameof(viseme));",
            "            var tongueCount = TongueLatentCount(kind);",
            "            RequireIndex(tongueLatent, tongueCount, nameof(tongueLatent));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedBiasValues : QualityBiasValues;",
            "            return values[viseme * tongueCount + tongueLatent];",
            "        }",
            "",
            "        public static float OutputProjection(",
            "            AdvancedVisemeVisibleTongueModelKind kind, int tongueLatent,",
            "            AdvancedVisemeVisibleTongueOutput output)",
            "        {",
            "            RequireIndex(tongueLatent, TongueLatentCount(kind), nameof(tongueLatent));",
            "            var outputIndex = (int)output;",
            "            RequireIndex(outputIndex, OutputCount, nameof(output));",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedOutputValues : QualityOutputValues;",
            "            return values[tongueLatent * OutputCount + outputIndex];",
            "        }",
            "",
            "        public static float Reliability(AdvancedVisemeVisibleTongueModelKind kind, int viseme)",
            "        {",
            "            RequireIndex(viseme, VisemeCount, nameof(viseme));",
            "            ValidateKind(kind);",
            "            var values = kind == AdvancedVisemeVisibleTongueModelKind.Balanced",
            "                ? BalancedReliabilityValues : QualityReliabilityValues;",
            "            return values[viseme];",
            "        }",
            "",
            "        /// <summary>Collapsed-table reference evaluator with final [-1,1] clamp.</summary>",
            "        public static void Predict(",
            "            AdvancedVisemeVisibleTongueModelKind kind,",
            "            IReadOnlyList<float> visemeWeights,",
            "            IReadOnlyList<float> features,",
            "            float[] outputs)",
            "        {",
            "            PredictUnclamped(kind, visemeWeights, features, outputs);",
            "            for (var output = 0; output < OutputCount; output++)",
            "                outputs[output] = Math.Max(-1f, Math.Min(1f, outputs[output]));",
            "        }",
            "",
            "        /// <summary>Normalized-simplex collapsed evaluator before the final output clamp.</summary>",
            "        public static void PredictUnclamped(",
            "            AdvancedVisemeVisibleTongueModelKind kind,",
            "            IReadOnlyList<float> visemeWeights,",
            "            IReadOnlyList<float> features,",
            "            float[] outputs)",
            "        {",
            "            if (visemeWeights == null || visemeWeights.Count != VisemeCount)",
            "                throw new ArgumentException(\"Prediction requires 15 viseme weights.\", nameof(visemeWeights));",
            "            if (features == null || features.Count != FeatureCount(kind))",
            "                throw new ArgumentException(\"Prediction feature count does not match the model.\", nameof(features));",
            "            if (outputs == null || outputs.Length != OutputCount)",
            "                throw new ArgumentException(\"Prediction requires two outputs.\", nameof(outputs));",
            "",
            "            var weightSum = 0f;",
            "            for (var viseme = 0; viseme < VisemeCount; viseme++)",
            "                weightSum += Math.Max(0f, visemeWeights[viseme]);",
            "            var fallbackSilence = weightSum <= 1e-8f;",
            "            if (fallbackSilence) weightSum = 1f;",
            "            var normalizedWeights = new float[VisemeCount];",
            "            var reliability = 0f;",
            "            for (var viseme = 0; viseme < VisemeCount; viseme++)",
            "            {",
            "                var weight = fallbackSilence",
            "                    ? (viseme == 0 ? 1f : 0f)",
            "                    : Math.Max(0f, visemeWeights[viseme]) / weightSum;",
            "                normalizedWeights[viseme] = weight;",
            "                reliability += weight * Reliability(kind, viseme);",
            "            }",
            "",
            "            for (var output = 0; output < OutputCount; output++)",
            "            {",
            "                var value = 0f;",
            "                var outputKind = (AdvancedVisemeVisibleTongueOutput)output;",
            "                for (var viseme = 0; viseme < VisemeCount; viseme++)",
            "                {",
            "                    var weight = normalizedWeights[viseme];",
            "                    if (weight <= 0f) continue;",
            "                    var conditional = CollapsedBias(kind, viseme, outputKind);",
            "                    for (var feature = 0; feature < features.Count; feature++)",
            "                        conditional += features[feature] * CollapsedFeatureCoefficient(",
            "                            kind, viseme, feature, outputKind);",
            "                    value += weight * conditional;",
            "                }",
            "                outputs[output] = value * reliability;",
            "            }",
            "        }",
            "",
            "        private static void ValidateKind(AdvancedVisemeVisibleTongueModelKind kind)",
            "        {",
            "            if (kind != AdvancedVisemeVisibleTongueModelKind.Balanced &&",
            "                kind != AdvancedVisemeVisibleTongueModelKind.Quality)",
            "                throw new ArgumentOutOfRangeException(nameof(kind));",
            "        }",
            "",
            "        private static void RequireIndex(int value, int count, string name)",
            "        {",
            "            if ((uint)value >= (uint)count) throw new ArgumentOutOfRangeException(name);",
            "        }",
            "    }",
            "}",
            "",
        ]
    )
    return "\n".join(lines)


class _TrainingRecord:
    split: str
    speaker: int
    prompt: int
    visemes: np.ndarray
    visible: np.ndarray
    tongue: np.ndarray


def _load_geometry(
    cache_dir: Path,
    selection: dict[str, Any],
    utterances: Sequence[corpus.Utterance],
) -> dict[str, np.ndarray]:
    entries = selection.get("entries")
    if not isinstance(entries, list) or len(entries) != len(utterances):
        raise ValueError("Geometry load requires the same canonical selection ordering")
    result: dict[str, np.ndarray] = {}
    for entry, utterance in zip(entries, utterances):
        if str(entry.get("entry")) != utterance.source_entry:
            raise ValueError("Geometry entry order differs from the restricted utterance loader")
        # load_selection already verified this member's size, CRC-32 and SHA-256.
        payload = corpus.restricted_load_pt(corpus.cache_path(cache_dir, entry))
        source = payload.get("ema_trimmed")
        if (
            not isinstance(source, np.ndarray)
            or source.dtype.hasobject
            or source.dtype.kind not in {"f", "c"}
            or np.iscomplexobj(source)
        ):
            raise ValueError(f"Raw EMA geometry must be a real floating array in {utterance.source_entry}")
        geometry = np.asarray(source, dtype=np.float64)
        if geometry.shape != (len(utterance.ema), 18):
            raise ValueError(
                f"Expected raw EMA geometry {(len(utterance.ema), 18)} in "
                f"{utterance.source_entry}, got {geometry.shape}"
            )
        if geometry.size > corpus.MAX_FRAMES * 18 or not np.all(np.isfinite(geometry)):
            raise ValueError(f"Invalid or non-finite raw EMA geometry in {utterance.source_entry}")
        result[utterance.source_entry] = geometry
    return result


def _training_records(
    utterances: Sequence[corpus.Utterance],
    balanced: bool,
    geometry_by_entry: dict[str, np.ndarray],
) -> list[_TrainingRecord]:
    records: list[_TrainingRecord] = []
    for utterance in utterances:
        record = _TrainingRecord()
        record.split = utterance.split
        record.speaker = utterance.speaker
        record.prompt = utterance.prompt
        record.visemes = np.full(len(utterance.ema), -1, dtype=np.int16)
        for segment in utterance.segments:
            record.visemes[segment.start : segment.end] = segment.viseme
        if np.any(record.visemes < 0):
            raise ValueError(f"Viseme segmentation left uncovered frames in {utterance.source_entry}")
        if utterance.source_entry not in geometry_by_entry:
            raise ValueError(f"Missing raw EMA geometry for {utterance.source_entry}")
        ema = geometry_by_entry[utterance.source_entry]
        if ema.shape != (len(utterance.ema), 18):
            raise ValueError(f"Expected matching (frames,18) raw EMA in {utterance.source_entry}")
        upper_lip = ema[:, [0, 1]]
        lower_lip = ema[:, [2, 3]]
        jaw = ema[:, [8, 9]]
        tongue_tip = ema[:, [12, 13]]
        lip_midpoint = 0.5 * (upper_lip + lower_lip)
        # Build head-corrected geometric semantics before any per-speaker
        # calibration.  This avoids subtracting or averaging independently
        # standardized articulator channels from the published 12-D tensor.
        geometric_visible = np.stack(
            (
                upper_lip[:, 1] - jaw[:, 1],
                jaw[:, 0] - upper_lip[:, 0],
                upper_lip[:, 1] - lower_lip[:, 1],
                lip_midpoint[:, 0],
            ),
            axis=1,
        )
        geometric_tongue = np.stack(
            (
                tongue_tip[:, 0] - lip_midpoint[:, 0],
                tongue_tip[:, 1] - jaw[:, 1],
            ),
            axis=1,
        )
        if balanced:
            record.visible = geometric_visible[:, [0, 2, 3]]
        else:
            record.visible = geometric_visible
        record.tongue = geometric_tongue
        if not np.all(np.isfinite(record.visible)) or not np.all(np.isfinite(record.tongue)):
            raise ValueError(f"Non-finite semantic axis in {utterance.source_entry}")
        records.append(record)
    return records


def _split_calibration(
    records: Sequence[_TrainingRecord],
) -> tuple[list[_TrainingRecord], list[_TrainingRecord]]:
    calibration: list[_TrainingRecord] = []
    evaluation: list[_TrainingRecord] = []
    for speaker in sorted({record.speaker for record in records}):
        speaker_records = sorted(
            (record for record in records if record.speaker == speaker),
            key=lambda record: record.prompt,
        )
        for index, record in enumerate(speaker_records):
            (calibration if index % 4 == 0 else evaluation).append(record)
    if not calibration or not evaluation:
        raise ValueError("Each evaluation speaker needs calibration and evaluation utterances")
    return calibration, evaluation


def _speaker_bounds(
    records: Sequence[_TrainingRecord],
) -> dict[int, tuple[np.ndarray, np.ndarray]]:
    result: dict[int, tuple[np.ndarray, np.ndarray]] = {}
    for speaker in sorted({record.speaker for record in records}):
        values = np.concatenate(
            [
                np.column_stack((record.visible, record.tongue))
                for record in records
                if record.speaker == speaker
            ]
        )
        lower = np.quantile(values, CALIBRATION_QUANTILES[0], axis=0)
        upper = np.maximum(
            np.quantile(values, CALIBRATION_QUANTILES[1], axis=0),
            lower + 1e-4,
        )
        result[speaker] = lower, upper
    return result


def _concatenate_calibrated(
    records: Sequence[_TrainingRecord],
    bounds: dict[int, tuple[np.ndarray, np.ndarray]],
    visible_count: int,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    visible: list[np.ndarray] = []
    tongue: list[np.ndarray] = []
    visemes: list[np.ndarray] = []
    utterance_ids: list[np.ndarray] = []
    speakers: list[np.ndarray] = []
    for index, record in enumerate(records):
        if record.speaker not in bounds:
            raise ValueError(f"Speaker {record.speaker} has no calibration utterances")
        lower, upper = bounds[record.speaker]
        normalized = np.clip(
            (np.column_stack((record.visible, record.tongue)) - lower) / (upper - lower),
            0.0,
            1.0,
        )
        visible.append(normalized[:, :visible_count])
        tongue.append(normalized[:, visible_count:])
        visemes.append(record.visemes)
        utterance_ids.append(np.full(len(record.visemes), index, dtype=np.int32))
        speakers.append(np.full(len(record.visemes), record.speaker, dtype=np.int16))
    return (
        np.concatenate(visible),
        np.concatenate(tongue),
        np.concatenate(visemes),
        np.concatenate(utterance_ids),
        np.concatenate(speakers),
    )


def _headroom_fraction(values: np.ndarray, centers: np.ndarray, visemes: np.ndarray) -> np.ndarray:
    delta = values - centers[visemes]
    headroom = np.where(delta >= 0.0, 1.0 - centers[visemes], centers[visemes])
    return np.clip(delta / np.maximum(headroom, HEADROOM_FLOOR), -1.0, 1.0)


def _observer_features(residual: np.ndarray, utterance_ids: np.ndarray) -> np.ndarray:
    alpha = 1.0 - math.exp(-1.0 / (corpus.EXPECTED_SAMPLE_RATE_HZ * OBSERVER_RESPONSE_SECONDS))
    result = np.empty((len(residual), residual.shape[1] * 3), dtype=np.float64)
    for identity in np.unique(utterance_ids):
        indices = np.flatnonzero(utterance_ids == identity)
        fast = residual[indices[0]].copy()
        slow = fast.copy()
        for index in indices:
            fast += alpha * (residual[index] - fast)
            slow += alpha * (fast - slow)
            result[index] = np.concatenate((residual[index], residual[index] - fast, fast - slow))
    return result


def _fit_portable_model(
    train: Sequence[_TrainingRecord],
    test: Sequence[_TrainingRecord],
    balanced: bool,
) -> tuple[
    dict[str, np.ndarray],
    np.ndarray,
    np.ndarray,
    np.ndarray,
    np.ndarray,
    list[_TrainingRecord],
]:
    visible_count = BALANCED_VISIBLE_CHANNEL_COUNT if balanced else VISIBLE_CHANNEL_COUNT
    target_count = RUNTIME_OUTPUT_COUNT if balanced else SOURCE_OUTPUT_COUNT
    feature_count = visible_count * 3
    tongue_latent_count = BALANCED_TONGUE_LATENT_COUNT if balanced else TONGUE_LATENT_COUNT
    calibration, evaluation = _split_calibration(test)

    train_visible, train_tongue, train_visemes, train_utterances, _ = _concatenate_calibrated(
        train,
        _speaker_bounds(train),
        visible_count,
    )
    test_visible, test_tongue, test_visemes, test_utterances, test_speakers = _concatenate_calibrated(
        evaluation,
        _speaker_bounds(calibration),
        visible_count,
    )
    visible_centers = np.array(
        [np.median(train_visible[train_visemes == viseme], axis=0) for viseme in range(VISEME_COUNT)]
    )
    tongue_centers = np.array(
        [np.median(train_tongue[train_visemes == viseme], axis=0) for viseme in range(VISEME_COUNT)]
    )
    if not np.all(np.isfinite(visible_centers)) or not np.all(np.isfinite(tongue_centers)):
        raise ValueError("Every viseme must have finite training centers")

    train_features = _observer_features(
        _headroom_fraction(train_visible, visible_centers, train_visemes),
        train_utterances,
    )
    test_features = _observer_features(
        _headroom_fraction(test_visible, visible_centers, test_visemes),
        test_utterances,
    )
    train_target = _headroom_fraction(train_tongue, tongue_centers, train_visemes)
    test_target = _headroom_fraction(test_tongue, tongue_centers, test_visemes)

    feature_mean = train_features.mean(axis=0)
    feature_std = train_features.std(axis=0)
    feature_std[feature_std < 1e-6] = 1.0
    standardized = (train_features - feature_mean) / feature_std
    intercept = np.zeros((VISEME_COUNT, target_count), dtype=np.float64)
    direct = np.zeros((VISEME_COUNT, feature_count, target_count), dtype=np.float64)
    direct_prediction = np.zeros_like(train_target)
    for viseme in range(VISEME_COUNT):
        mask = train_visemes == viseme
        count = int(mask.sum())
        if count <= feature_count:
            raise ValueError(f"Insufficient training frames for viseme {VISEMES[viseme]}")
        intercept[viseme] = train_target[mask].mean(axis=0)
        design = standardized[mask]
        direct[viseme] = np.linalg.solve(
            design.T @ design + 0.1 * count * np.eye(feature_count),
            design.T @ (train_target[mask] - intercept[viseme]),
        )
        direct_prediction[mask] = intercept[viseme] + design @ direct[viseme]

    tongue_projection_columns = np.linalg.svd(
        direct_prediction - direct_prediction.mean(axis=0),
        full_matrices=False,
    )[2].T[:, :tongue_latent_count]
    projected_direct = direct @ tongue_projection_columns
    visible_projection_standardized = np.linalg.svd(
        projected_direct.transpose(1, 0, 2).reshape(
            feature_count,
            VISEME_COUNT * tongue_latent_count,
        ),
        full_matrices=False,
    )[0][:, :VISIBLE_LATENT_COUNT]
    visible_projection = visible_projection_standardized / feature_std[:, None]
    center = (feature_mean / feature_std) @ visible_projection_standardized
    viseme_mix = np.empty(
        (VISEME_COUNT, VISIBLE_LATENT_COUNT, tongue_latent_count),
        dtype=np.float64,
    )
    viseme_bias = np.empty((VISEME_COUNT, tongue_latent_count), dtype=np.float64)
    for viseme in range(VISEME_COUNT):
        viseme_mix[viseme] = visible_projection_standardized.T @ projected_direct[viseme]
        viseme_bias[viseme] = (
            intercept[viseme] @ tongue_projection_columns
            - center @ viseme_mix[viseme]
        )

    prediction = np.zeros_like(test_target)
    for viseme in range(VISEME_COUNT):
        mask = test_visemes == viseme
        prediction[mask] = (
            viseme_bias[viseme]
            + test_features[mask] @ visible_projection @ viseme_mix[viseme]
        ) @ tongue_projection_columns.T
    model = {
        "F": visible_projection,
        "M": viseme_mix,
        "bias": viseme_bias,
        "E": tongue_projection_columns.T,
        "featureAbsP99": np.quantile(np.abs(train_features), 0.99, axis=0),
        "featureAbsP995": np.quantile(np.abs(train_features), 0.995, axis=0),
    }
    return model, test_target, prediction, test_visemes, test_utterances, evaluation


def _relative_improvement(target: np.ndarray, prediction: np.ndarray) -> float:
    denominator = float(np.sum(target * target))
    if denominator <= 0.0:
        raise ValueError("Evaluation baseline has zero energy")
    return float(100.0 * (1.0 - np.sum((target - prediction) ** 2) / denominator))


def train_checked_document(
    utterances: Sequence[corpus.Utterance],
    balanced: bool,
    geometry_by_entry: dict[str, np.ndarray],
    enforce_reviewed: bool = True,
) -> dict[str, Any]:
    records = _training_records(utterances, balanced, geometry_by_entry)
    pool = [record for record in records if record.split in ("fit", "development")]
    heldout = [record for record in records if record.split == "heldout"]
    if {record.speaker for record in pool} != set(range(1, 9)):
        raise ValueError("Portable training requires speakers 1-8 in fit+development")
    if {record.speaker for record in heldout} != set(range(9, 13)):
        raise ValueError("Portable held-out evaluation requires speakers 9-12")

    oof_target: list[np.ndarray] = []
    oof_prediction: list[np.ndarray] = []
    oof_visemes: list[np.ndarray] = []
    for held_speaker in range(1, 9):
        _, target, prediction, visemes, _, _ = _fit_portable_model(
            [record for record in pool if record.speaker != held_speaker],
            [record for record in pool if record.speaker == held_speaker],
            balanced,
        )
        oof_target.append(target)
        oof_prediction.append(prediction)
        oof_visemes.append(visemes)
    loso_target = np.concatenate(oof_target)
    loso_prediction = np.concatenate(oof_prediction)
    loso_visemes = np.concatenate(oof_visemes)
    reliability = np.zeros(VISEME_COUNT, dtype=np.float64)
    for viseme in range(VISEME_COUNT):
        mask = loso_visemes == viseme
        denominator = max(
            float(np.sum(loso_prediction[mask] * loso_prediction[mask])),
            1e-9,
        )
        reliability[viseme] = np.clip(
            float(np.sum(loso_target[mask] * loso_prediction[mask])) / denominator,
            0.0,
            1.0,
        )

    model, target, prediction, visemes, _, _ = _fit_portable_model(pool, heldout, balanced)
    shrunk_prediction = prediction * reliability[visemes, None]
    axis_improvement = 100.0 * (
        1.0
        - np.sum((target - shrunk_prediction) ** 2, axis=0)
        / np.sum(target * target, axis=0)
    )
    visible_axes = BALANCED_VISIBLE_AXES if balanced else VISIBLE_AXES
    target_axes = SOURCE_TARGET_AXES[:2] if balanced else SOURCE_TARGET_AXES
    observer_features = BALANCED_OBSERVER_FEATURES if balanced else OBSERVER_FEATURES
    computed_evaluation = {
        "speakerLosoImprovementPercent": _relative_improvement(
            loso_target,
            loso_prediction,
        ),
        "speakerLosoWithReliabilityPercent": _relative_improvement(
            loso_target,
            loso_prediction * reliability[loso_visemes, None],
        ),
        "heldoutImprovementPercent": _relative_improvement(target, prediction),
        "heldoutWithReliabilityPercent": _relative_improvement(target, shrunk_prediction),
        "heldoutAxisImprovementPercent": axis_improvement.tolist(),
        "heldoutEvaluationFrames": int(len(target)),
    }
    reviewed_evaluation = EXPECTED_BALANCED_EVALUATION if balanced else EXPECTED_QUALITY_EVALUATION
    for key in (
        "speakerLosoImprovementPercent",
        "speakerLosoWithReliabilityPercent",
        "heldoutImprovementPercent",
        "heldoutWithReliabilityPercent",
    ):
        if enforce_reviewed and not math.isclose(
            float(computed_evaluation[key]),
            float(reviewed_evaluation[key]),
            rel_tol=0.0,
            abs_tol=1e-10,
        ):
            raise ValueError(f"Retrained evaluation metric {key} does not match the reviewed fit")
    if enforce_reviewed and int(computed_evaluation["heldoutEvaluationFrames"]) != int(
        reviewed_evaluation["heldoutEvaluationFrames"]
    ):
        raise ValueError("Retrained held-out frame count does not match the reviewed fit")
    if enforce_reviewed and not np.allclose(
        computed_evaluation["heldoutAxisImprovementPercent"],
        reviewed_evaluation["heldoutAxisImprovementPercent"],
        rtol=0.0,
        atol=1e-10,
    ):
        raise ValueError("Retrained per-axis metrics do not match the reviewed fit")

    document: dict[str, Any] = {
        "schemaVersion": 1,
        "provenance": {
            "dataset": corpus.EXPECTED_REPOSITORY,
            "revision": corpus.EXPECTED_REPOSITORY_REVISION,
            "license": "CC-BY-4.0",
        },
        "transform": {
            "visibleAxes": list(visible_axes),
            "targetAxes": list(target_axes),
            "geometry": {
                "source": "ema_trimmed (18-D head-corrected EMA coordinates)",
                "JawOpen": "UpperLipY-JawY",
                "JawAdvance": "JawX-UpperLipX",
                "LipAperture": "UpperLipY-LowerLipY",
                "LipProtrusion": "mean(UpperLipX,LowerLipX)",
                "TongueTipAdvanceRelativeToLips": "TongueTipX-mean(UpperLipX,LowerLipX)",
                "TongueTipHeightRelativeToJaw": "TongueTipY-JawY",
            },
            "speakerCalibrationQuantiles": list(CALIBRATION_QUANTILES),
            "normalization": "clip((axis-q01)/(q99-q01),0,1)",
            "residual": (
                "delta=normalized-trainVisemeMedian; delta/max(delta>=0 ? 1-center : "
                "center,0.075), clamped [-1,1]"
            ),
            "observer": {
                "sampleRateHz": 100,
                "responseSeconds": OBSERVER_RESPONSE_SECONDS,
                "features": list(observer_features),
            },
            "runtimeCalibration": (
                "Use tracker calibration for visible ranges and authored profile headroom for target "
                "ranges; corpus target calibration is evaluation-only."
            ),
        },
        "model": {
            "formula": (
                "h=x*visibleProjection; t=visemeBias[v]+h*visemeMix[v]; "
                "y=t*tongueProjection; y*=visemeReliability[v]"
            ),
            "visibleProjection": model["F"].round(9).tolist(),
            "visemeMix": model["M"].round(9).tolist(),
            "visemeBias": model["bias"].round(9).tolist(),
            "tongueProjection": model["E"].round(9).tolist(),
            "visemeReliability": reliability.round(9).tolist(),
            "featureAbsP99": model["featureAbsP99"].round(9).tolist(),
            "featureAbsP995": model["featureAbsP995"].round(9).tolist(),
        },
        # Snap sub-ulp reduction-order noise to the reviewed serialized metrics
        # only after checking the independently recomputed values above.
        "evaluation": copy.deepcopy(reviewed_evaluation if enforce_reviewed else computed_evaluation),
    }
    document["contentSha256"] = content_hash(document)
    expected_hash = (
        EXPECTED_BALANCED_SOURCE_MODEL_CONTENT_SHA256
        if balanced
        else EXPECTED_QUALITY_SOURCE_MODEL_CONTENT_SHA256
    )
    if enforce_reviewed and document["contentSha256"] != expected_hash:
        raise ValueError(
            "Portable retraining did not reproduce the reviewed content hash: "
            f"expected {expected_hash}, got {document['contentSha256']}"
        )
    return document


def train_from_cache(
    source_path: Path,
    committed_selection_path: Path,
    cache_dir: Path,
    output_json: Path,
    balanced_output_json: Path,
    output_csharp: Path,
) -> tuple[dict[str, Any], dict[str, Any]]:
    source, committed_selection = validate_source_and_selection(
        source_path,
        committed_selection_path,
    )
    cache_selection, utterances = corpus.load_selection(cache_dir, source)
    if cache_selection.get("selectionContentSha256") != EXPECTED_SELECTION_CONTENT_SHA256:
        raise ValueError("Training cache does not match the reviewed selection hash")
    geometry_by_entry = _load_geometry(cache_dir, cache_selection, utterances)
    quality_checked = train_checked_document(
        utterances, balanced=False, geometry_by_entry=geometry_by_entry)
    balanced_checked = train_checked_document(
        utterances, balanced=True, geometry_by_entry=geometry_by_entry)
    quality = checked_model_to_audit(quality_checked, source, committed_selection)
    balanced_document = balanced_checked_model_to_audit(
        balanced_checked,
        source,
        committed_selection,
    )
    validate_audit(quality)
    validate_balanced_audit(balanced_document)
    corpus.write_json_atomic(output_json, quality)
    corpus.write_json_atomic(balanced_output_json, balanced_document)
    corpus.write_text_atomic(output_csharp, generate_csharp(quality, balanced_document))
    return quality, balanced_document


def regenerate(
    audit_path: Path,
    balanced_audit_path: Path,
    output_csharp: Path,
) -> tuple[dict[str, Any], dict[str, Any]]:
    document = corpus.load_json(audit_path)
    balanced_document = corpus.load_json(balanced_audit_path)
    validate_audit(document)
    validate_balanced_audit(balanced_document)
    corpus.write_text_atomic(output_csharp, generate_csharp(document, balanced_document))
    return document, balanced_document


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-manifest", type=Path, default=DEFAULT_SOURCE_MANIFEST)
    parser.add_argument("--selection-manifest", type=Path, default=DEFAULT_SELECTION_MANIFEST)
    subparsers = parser.add_subparsers(dest="command", required=True)

    generate_parser = subparsers.add_parser(
        "generate",
        help="Fail-closed validate the committed audit and deterministically regenerate C#",
    )
    generate_parser.add_argument("--audit-json", type=Path, default=DEFAULT_AUDIT_JSON)
    generate_parser.add_argument(
        "--balanced-audit-json",
        type=Path,
        default=DEFAULT_BALANCED_AUDIT_JSON,
    )
    generate_parser.add_argument("--output-csharp", type=Path, default=DEFAULT_MODEL_CS)
    train_parser = subparsers.add_parser(
        "train",
        help="Retrain both reviewed models from the restricted, hash-checked SPIRE cache",
    )
    train_parser.add_argument("--cache-dir", type=Path, default=corpus.default_cache_dir())
    train_parser.add_argument("--output-json", type=Path, default=DEFAULT_AUDIT_JSON)
    train_parser.add_argument(
        "--balanced-output-json",
        type=Path,
        default=DEFAULT_BALANCED_AUDIT_JSON,
    )
    train_parser.add_argument("--output-csharp", type=Path, default=DEFAULT_MODEL_CS)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = build_parser().parse_args(argv)
    if arguments.command == "generate":
        validate_source_and_selection(
            arguments.source_manifest.resolve(),
            arguments.selection_manifest.resolve(),
        )
        document, balanced_document = regenerate(
            arguments.audit_json.resolve(),
            arguments.balanced_audit_json.resolve(),
            arguments.output_csharp.resolve(),
        )
    else:
        document, balanced_document = train_from_cache(
            arguments.source_manifest.resolve(),
            arguments.selection_manifest.resolve(),
            arguments.cache_dir.resolve(),
            arguments.output_json.resolve(),
            arguments.balanced_output_json.resolve(),
            arguments.output_csharp.resolve(),
        )

    metrics = document["evaluation"]
    print(f"Visible-tongue model: {document['contentSha256']}")
    print(
        "Held-out improvement with reliability: "
        f"{float(metrics['heldoutWithReliabilityPercent']):.3f}%"
    )
    for output, improvement in metrics["runtimeOutputHeldoutImprovementPercent"].items():
        print(f"  {output}: {float(improvement):.3f}%")
    balanced_metrics = balanced_document["evaluation"]
    print(f"Balanced visible-tongue model: {balanced_document['contentSha256']}")
    print(
        "Balanced held-out improvement with reliability: "
        f"{float(balanced_metrics['heldoutWithReliabilityPercent']):.3f}%"
    )
    for output, improvement in balanced_metrics["runtimeOutputHeldoutImprovementPercent"].items():
        print(f"  {output}: {float(improvement):.3f}%")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("Interrupted.", file=sys.stderr)
        raise SystemExit(130)
