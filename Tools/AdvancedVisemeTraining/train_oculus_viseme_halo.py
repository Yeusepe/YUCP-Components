#!/usr/bin/env python3
"""Train and generate a compact hard-Oculus-viseme shape halo.

VRChat exposes only Oculus' dominant viseme index, while the native Oculus
analyzer computes a continuous 15-weight vector.  This trainer learns the
conditional barycenter of that continuous *shape* for every hard winner. Raw
Oculus weight mass is kept for diagnostics, but it is never used as a fitting
feature or emitted into the runtime package.

The shipping table is B(h) = (1-h) I + h C.  C is the conditional barycenter
table and h is selected on development utterances with the documented
TV-aware objective after the exact current 24 ms two-pole observer and the
default visible render lead, ``1.0 * 0.85 = 0.85``. The decoder publishes a
static target row; the shared interruptible observer owns all time evolution.
"""

from __future__ import annotations

import argparse
import copy
import ctypes
import hashlib
import math
import os
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Sequence

import numpy as np

import train_hidden_phone_posterior as ovr_source
import train_transition_retention as corpus


SCRIPT_DIR = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_SOURCE_MANIFEST = SCRIPT_DIR / "source_manifest.json"
DEFAULT_PROCESSED_SELECTION = SCRIPT_DIR / "Generated" / "spire_selection_manifest.json"
DEFAULT_AUDIO_MANIFEST = SCRIPT_DIR / "Generated" / "spire_audio_selection_manifest.json"
DEFAULT_AUDIT_JSON = SCRIPT_DIR / "Generated" / "advanced_viseme_oculus_halo.json"
DEFAULT_MODEL_CS = (
    REPOSITORY_ROOT
    / "Packages"
    / "com.yucp.components"
    / "Runtime"
    / "Components"
    / "Data"
    / "Generated"
    / "AdvancedVisemeOculusHalo.generated.cs"
)
DEFAULT_OVR_DLL = ovr_source.DEFAULT_OVR_DLL

MODEL_VERSION = 3
VISEMES = corpus.VISEMES
VISEME_COUNT = len(VISEMES)
SILENCE_INDEX = 0

OBSERVER_RESPONSE_SECONDS = 0.024
DEFAULT_SPEECH_LIVELINESS = 1.0
MAXIMUM_SPEECH_LIVELINESS_LEAD = 0.85
EVALUATION_LIVELINESS = (
    DEFAULT_SPEECH_LIVELINESS * MAXIMUM_SPEECH_LIVELINESS_LEAD
)
TV_FLOOR_RATIO = 0.90
TV_PENALTY_WEIGHT = 8.0
HALO_STRENGTH_CANDIDATES = tuple(index / 100.0 for index in range(101))
CANONICAL_REFERENCE_HALO_STRENGTH = 0.79
CANONICAL_REFERENCE_TOP_K = 5
TOP_K_CANDIDATES = (2, 3, 4, 5, VISEME_COUNT)
DENSE_TOP_K = VISEME_COUNT
MIN_OVERALL_GAIN_RETENTION = 0.90
MIN_TRANSITION_GAIN_RETENTION = 0.90
MIN_VELOCITY_GAIN_RETENTION = 0.90
TRANSITION_WINDOW_BLOCKS = 4
SIMPLEX_CULLING_EPSILON = 3e-4
RENDER_RATE_FPS = (15, 30, 60, 90, 144)
MIN_RENDER_RATE_GAIN_RETENTION = 0.85

# Rejected age-conditioned trajectory experiment.  It remains in this trainer
# for reproducible audit work, but the shipping model deliberately does not use
# a winner-local clock: restarting a canned curve on every hard-viseme edge was
# visibly less continuous than changing the target of the shared live observer.
TRAJECTORY_CONTROL_POINT_COUNT = 4
TRAJECTORY_DURATION_BLOCKS = 6
TRAJECTORY_DURATION_SECONDS = (
    TRAJECTORY_DURATION_BLOCKS
    * ovr_source.ANALYSIS_BUFFER_SAMPLES
    / ovr_source.ANALYSIS_SAMPLE_RATE
)
TRAJECTORY_POSE_TRANSITION_BOOST = 2.0
TRAJECTORY_DERIVATIVE_WEIGHT = 0.65
TRAJECTORY_RENDER_RATE_FIT_WEIGHT = 0.35
TRAJECTORY_RIDGE_RATIO = 2.5e-3
TRAJECTORY_ENDPOINT_RIDGE_RATIO = 2.0e-2
TRAJECTORY_PROJECTED_GRADIENT_STEPS = 2048
TRAJECTORY_PROJECTED_GRADIENT_TOLERANCE = 1e-10
TRAJECTORY_WINNER_MARGIN = 1e-4
TRAJECTORY_CURVATURE_RATIO_CANDIDATES = (
    0.0,
    0.000625,
    0.00125,
    0.0025,
    0.005,
    0.01,
    0.02,
    0.04,
    0.08,
    0.16,
    0.32,
    0.64,
)
TRAJECTORY_TERMINAL_RATIO_MULTIPLIERS = (0.0, 0.25, 1.0)
MAX_TRAJECTORY_SECONDARY_REVERSAL = 0.02
MAX_TRAJECTORY_RELATIVE_WINNER_REVERSAL = 0.035
MAX_TRAJECTORY_PROGRESS_REVERSAL = 0.03
MAX_TRAJECTORY_SETTLEMENT_ERROR = 1e-3
MIN_TRAJECTORY_DEVELOPMENT_MSE_GAIN = 0.0025
MIN_TRAJECTORY_DEVELOPMENT_DERIVATIVE_GAIN = 0.0025
MIN_TRAJECTORY_DEVELOPMENT_TRANSITION_GAIN = 0.0025
MAX_TRAJECTORY_RENDER_TV_ERROR_RELAXATION = 0.01
MAX_TRAJECTORY_PER_VISEME_MSE_REGRESSION = 0.05

EXPECTED_PROCESSED_SELECTION_SHA256 = (
    "81b64a8ba071f2a8fe675f9b40947053c01d2d047b3766f43876912b1cfa20ab"
)
EXPECTED_AUDIO_SELECTION_SHA256 = (
    "6e45ccf1971a87716ff02c467ef6bbc4b9d5ba92131941dc126cc201147a228d"
)
EXPECTED_OVR_DLL_SHA256 = (
    "2318c42eb806753e340b426d0b83dd3278f5b8d3b851ccf90a5be0ea8e1d2cd3"
)

# Filled after the first reviewed extraction.  Keeping these as explicit pins
# turns an otherwise self-consistent retrain with a different native analyzer
# result into a visible failure.
EXPECTED_CONTINUOUS_SEQUENCE_SHA256 = (
    "771c4ee8c0473cc266f35dcb95c8db252ba782285a46edb10b575a8302019f7f"
)
EXPECTED_DOMINANT_SEQUENCE_SHA256 = (
    "159d0ea8bd06281bec5b8f396d5dc4907fe2b26b74fd6c162042a2d6059cbf90"
)

EXPECTED_UTTERANCE_COUNTS = {
    "fit": 384,
    "development": 32,
    "heldout": 128,
}
MIN_NON_SILENCE_FIT_FRAMES = 512
MAX_WEIGHT_MASS = float(VISEME_COUNT)
MASS_EPSILON = 1e-12
TABLE_SUM_TOLERANCE = 1e-6


@dataclass(frozen=True)
class OculusUtterance:
    split: str
    speaker: int
    prompt: int
    prompt_ordinal: int
    audio_entry: str
    continuous: np.ndarray
    winners: np.ndarray
    delays_ms: np.ndarray


@dataclass(frozen=True)
class ExtractionAudit:
    dll_version: str
    continuous_sequence_sha256: str
    dominant_sequence_sha256: str
    frame_delay_ms_counts: dict[str, int]


@dataclass(frozen=True)
class CardinalityCandidate:
    top_k: int
    centers: np.ndarray
    halo_strength: float
    metrics: dict[str, Any]
    objective: dict[str, float]
    grid: list[dict[str, Any]]


def render_lead_derivation() -> str:
    return (
        "defaultSpeechLiveliness * maximumSpeechLivelinessLead "
        f"= {EVALUATION_LIVELINESS:g}"
    )


def cardinality_selection_key(
    candidate: CardinalityCandidate,
) -> tuple[int, float]:
    return candidate.top_k, candidate.halo_strength


def cardinality_tie_break_description() -> str:
    return "smallest accepted TopK, then smaller h"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def audit_hash_input(document: dict[str, Any]) -> dict[str, Any]:
    value = copy.deepcopy(document)
    value.pop("contentSha256", None)
    return value


def table_hash_input(document: dict[str, Any]) -> dict[str, Any]:
    return {
        "modelVersion": document["modelVersion"],
        "visemeOrder": document["visemeOrder"],
        "selectedHaloStrength": document["model"]["selectedHaloStrength"],
        "topK": document["model"]["topK"],
        "weights": document["model"]["weights"],
    }


def trajectory_hash_input(document: dict[str, Any]) -> dict[str, Any]:
    return {
        "modelVersion": document["modelVersion"],
        "visemeOrder": document["visemeOrder"],
        "durationSeconds": document["model"]["trajectoryDurationSeconds"],
        "controlPointCount": document["model"]["trajectoryControlPointCount"],
        "controlPoints": document["model"]["trajectoryControlPoints"],
    }


def validate_inputs(
    source_path: Path,
    processed_path: Path,
    audio_path: Path,
    cache_dir: Path,
    dll_path: Path,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    source = corpus.load_json(source_path)
    corpus.validate_source_manifest(source)
    processed = corpus.load_json(processed_path)
    corpus.validate_selection_hash(processed)
    audio = corpus.load_json(audio_path)
    ovr_source.validate_audio_manifest(audio, processed, cache_dir)

    if processed.get("selectionContentSha256") != EXPECTED_PROCESSED_SELECTION_SHA256:
        raise ValueError("Oculus halo requires the reviewed processed selection hash")
    if audio.get("selectionContentSha256") != EXPECTED_AUDIO_SELECTION_SHA256:
        raise ValueError("Oculus halo requires the reviewed paired-audio selection hash")
    if int(processed.get("selectedEntryCount", -1)) != 544:
        raise ValueError("Oculus halo requires exactly 544 selected utterances")
    if len(audio.get("entries", ())) != 544:
        raise ValueError("Oculus halo requires exactly 544 paired audio entries")
    if sha256_file(dll_path) != EXPECTED_OVR_DLL_SHA256:
        raise ValueError("Installed OVRLipSync.dll differs from the reviewed binary")

    observed_counts: dict[str, int] = {}
    for entry in audio["entries"]:
        split = str(entry.get("split", ""))
        observed_counts[split] = observed_counts.get(split, 0) + 1
    if observed_counts != EXPECTED_UTTERANCE_COUNTS:
        raise ValueError(
            f"Audio split counts differ from the reviewed selection: {observed_counts}"
        )
    return source, processed, audio


def analyze_continuous(
    oculus: ovr_source.OculusLipSync,
    samples_16k: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Run the exact pinned OVR pipeline and copy every native 15-weight frame."""

    if oculus.reset_context(oculus.context.value) != 0:
        raise RuntimeError("Oculus context reset failed")
    if oculus.send_signal(
        oculus.context.value,
        ovr_source.OVR_SIGNAL_SMOOTHING,
        ovr_source.OVR_SMOOTHING,
        0,
    ) != 0:
        raise RuntimeError("Oculus smoothing reset failed")

    source_position = np.arange(len(samples_16k), dtype=np.float64)
    target_count = int(
        round(
            len(samples_16k)
            * ovr_source.ANALYSIS_SAMPLE_RATE
            / ovr_source.SOURCE_SAMPLE_RATE
        )
    )
    target_position = (
        np.arange(target_count, dtype=np.float64)
        * ovr_source.SOURCE_SAMPLE_RATE
        / ovr_source.ANALYSIS_SAMPLE_RATE
    )
    samples = np.interp(
        target_position, source_position, samples_16k
    ).astype(np.float32)
    padded_count = int(
        math.ceil(len(samples) / ovr_source.ANALYSIS_BUFFER_SAMPLES)
        * ovr_source.ANALYSIS_BUFFER_SAMPLES
    )
    padded = np.zeros(padded_count, dtype=np.float32)
    padded[: len(samples)] = samples

    frame_count = padded_count // ovr_source.ANALYSIS_BUFFER_SAMPLES
    weights = np.empty((frame_count, VISEME_COUNT), dtype=np.float32)
    winners = np.empty(frame_count, dtype=np.uint8)
    delays_ms = np.empty(frame_count, dtype=np.int16)
    visemes = (ctypes.c_float * VISEME_COUNT)()
    frame_number = ctypes.c_int()
    frame_delay = ctypes.c_int()
    laughter = ctypes.c_float()

    for index in range(frame_count):
        start = index * ovr_source.ANALYSIS_BUFFER_SAMPLES
        frame = padded[start : start + ovr_source.ANALYSIS_BUFFER_SAMPLES]
        result = oculus.process_frame(
            oculus.context.value,
            frame.ctypes.data_as(ctypes.c_void_p),
            ovr_source.ANALYSIS_BUFFER_SAMPLES,
            ovr_source.OVR_AUDIO_F32_MONO,
            ctypes.byref(frame_number),
            ctypes.byref(frame_delay),
            visemes,
            VISEME_COUNT,
            ctypes.byref(laughter),
            None,
            0,
        )
        if result != 0:
            raise RuntimeError(f"Oculus ProcessFrameEx failed with {result}")
        # The native array is reused on the next call.  Assignment performs the
        # required owning copy into the stable float32 matrix.
        weights[index] = np.ctypeslib.as_array(visemes)
        winners[index] = int(np.argmax(weights[index]))
        if not np.iinfo(np.int16).min <= frame_delay.value <= np.iinfo(np.int16).max:
            raise ValueError("Oculus frame delay does not fit the audited Int16 representation")
        delays_ms[index] = frame_delay.value

    if not np.all(np.isfinite(weights)):
        raise ValueError("Oculus emitted a non-finite continuous weight")
    if np.any(weights < 0.0) or np.any(weights > 1.0):
        raise ValueError("Oculus emitted a continuous weight outside [0,1]")
    mass = weights.sum(axis=1, dtype=np.float64)
    if np.any(mass <= MASS_EPSILON) or np.any(mass > MAX_WEIGHT_MASS):
        raise ValueError("Oculus continuous weight mass is outside the reviewed safe range")
    return weights, winners, delays_ms


def update_extraction_digest(
    digest: hashlib._Hash,
    entry_name: str,
    weights: np.ndarray,
    winners: np.ndarray,
    delays_ms: np.ndarray,
) -> None:
    encoded = entry_name.encode("utf-8")
    digest.update(struct.pack("<I", len(encoded)))
    digest.update(encoded)
    digest.update(struct.pack("<I", len(weights)))
    digest.update(np.asarray(weights, dtype="<f4").tobytes(order="C"))
    digest.update(np.asarray(winners, dtype=np.uint8).tobytes(order="C"))
    digest.update(np.asarray(delays_ms, dtype="<i2").tobytes(order="C"))


def audit_extracted_records(
    records: Sequence[OculusUtterance], dll_version: str
) -> ExtractionAudit:
    continuous_digest = hashlib.sha256(b"YUCP Oculus halo continuous extraction v1\0")
    dominant_digest = hashlib.sha256(b"YUCP Oculus halo dominant extraction v1\0")
    delay_counts: dict[int, int] = {}
    for record in records:
        update_extraction_digest(
            continuous_digest,
            record.audio_entry,
            record.continuous,
            record.winners,
            record.delays_ms,
        )
        encoded = record.audio_entry.encode("utf-8")
        dominant_digest.update(struct.pack("<I", len(encoded)))
        dominant_digest.update(encoded)
        dominant_digest.update(struct.pack("<I", len(record.winners)))
        dominant_digest.update(record.winners.tobytes(order="C"))
        for value, count in zip(*np.unique(record.delays_ms, return_counts=True)):
            key = int(value)
            delay_counts[key] = delay_counts.get(key, 0) + int(count)
    continuous_sha = continuous_digest.hexdigest()
    dominant_sha = dominant_digest.hexdigest()
    if (
        EXPECTED_CONTINUOUS_SEQUENCE_SHA256
        and continuous_sha != EXPECTED_CONTINUOUS_SEQUENCE_SHA256
    ):
        raise ValueError("Continuous Oculus extraction differs from the reviewed sequence")
    if (
        EXPECTED_DOMINANT_SEQUENCE_SHA256
        and dominant_sha != EXPECTED_DOMINANT_SEQUENCE_SHA256
    ):
        raise ValueError("Dominant Oculus extraction differs from the reviewed sequence")
    return ExtractionAudit(
        dll_version=dll_version,
        continuous_sequence_sha256=continuous_sha,
        dominant_sequence_sha256=dominant_sha,
        frame_delay_ms_counts={
            str(key): delay_counts[key] for key in sorted(delay_counts)
        },
    )


def extraction_cache_path(cache_dir: Path) -> Path:
    return cache_dir.resolve() / "oculus_halo_continuous_extraction_v1.npz"


def load_extraction_cache(
    cache_dir: Path, audio_manifest: dict[str, Any]
) -> tuple[list[OculusUtterance], ExtractionAudit] | None:
    path = extraction_cache_path(cache_dir)
    if not path.is_file():
        return None
    try:
        with np.load(path, allow_pickle=False, max_header_size=1 << 20) as data:
            if int(data["formatVersion"]) != 1:
                raise ValueError("unsupported format version")
            offsets = np.asarray(data["offsets"], dtype=np.int64)
            continuous = np.asarray(data["continuous"], dtype=np.float32)
            winners = np.asarray(data["winners"], dtype=np.uint8)
            delays = np.asarray(data["delaysMs"], dtype=np.int16)
            splits = np.asarray(data["splits"])
            speakers = np.asarray(data["speakers"], dtype=np.int16)
            prompts = np.asarray(data["prompts"], dtype=np.int16)
            ordinals = np.asarray(data["promptOrdinals"], dtype=np.int16)
            entries = np.asarray(data["audioEntries"])
            dll_version = str(np.asarray(data["dllVersion"]).item())
        manifest_entries = audio_manifest["entries"]
        count = len(manifest_entries)
        if (
            offsets.shape != (count + 1,)
            or offsets[0] != 0
            or np.any(np.diff(offsets) <= 0)
            or offsets[-1] != len(continuous)
            or continuous.shape != (len(winners), VISEME_COUNT)
            or delays.shape != winners.shape
            or any(len(values) != count for values in (
                splits, speakers, prompts, ordinals, entries
            ))
        ):
            raise ValueError("cached array dimensions are invalid")
        records: list[OculusUtterance] = []
        for index, entry in enumerate(manifest_entries):
            expected = (
                str(entry["split"]),
                int(entry["speaker"]),
                int(entry["prompt"]),
                int(entry["promptOrdinal"]),
                str(entry["entry"]),
            )
            cached = (
                str(splits[index]),
                int(speakers[index]),
                int(prompts[index]),
                int(ordinals[index]),
                str(entries[index]),
            )
            if cached != expected:
                raise ValueError(f"metadata differs at utterance {index}")
            start = int(offsets[index])
            end = int(offsets[index + 1])
            records.append(OculusUtterance(
                split=cached[0],
                speaker=cached[1],
                prompt=cached[2],
                prompt_ordinal=cached[3],
                audio_entry=cached[4],
                continuous=continuous[start:end].copy(),
                winners=winners[start:end].copy(),
                delays_ms=delays[start:end].copy(),
            ))
        audit = audit_extracted_records(records, dll_version)
        print(f"Loaded {count} hash-verified Oculus analyses from {path}.", flush=True)
        return records, audit
    except (KeyError, OSError, ValueError) as error:
        print(f"Ignoring invalid Oculus analysis cache {path}: {error}", flush=True)
        return None


def save_extraction_cache(
    cache_dir: Path,
    records: Sequence[OculusUtterance],
    audit: ExtractionAudit,
) -> None:
    path = extraction_cache_path(cache_dir)
    path.parent.mkdir(parents=True, exist_ok=True)
    offsets = np.zeros(len(records) + 1, dtype=np.int64)
    for index, record in enumerate(records):
        offsets[index + 1] = offsets[index] + len(record.winners)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("wb") as stream:
        np.savez_compressed(
            stream,
            formatVersion=np.asarray(1, dtype=np.int16),
            offsets=offsets,
            continuous=np.concatenate(
                [record.continuous for record in records], axis=0
            ).astype(np.float32, copy=False),
            winners=np.concatenate(
                [record.winners for record in records]
            ).astype(np.uint8, copy=False),
            delaysMs=np.concatenate(
                [record.delays_ms for record in records]
            ).astype(np.int16, copy=False),
            splits=np.asarray([record.split for record in records]),
            speakers=np.asarray([record.speaker for record in records], dtype=np.int16),
            prompts=np.asarray([record.prompt for record in records], dtype=np.int16),
            promptOrdinals=np.asarray(
                [record.prompt_ordinal for record in records], dtype=np.int16
            ),
            audioEntries=np.asarray([record.audio_entry for record in records]),
            dllVersion=np.asarray(audit.dll_version),
        )
    os.replace(temporary, path)
    print(f"Cached hash-verified Oculus analyses at {path}.", flush=True)


def extract_utterances(
    cache_dir: Path,
    audio_manifest: dict[str, Any],
    dll_path: Path,
) -> tuple[list[OculusUtterance], ExtractionAudit]:
    cached = load_extraction_cache(cache_dir, audio_manifest)
    if cached is not None:
        return cached
    records: list[OculusUtterance] = []

    with ovr_source.OculusLipSync(dll_path) as oculus:
        dll_version = oculus.version()
        total = len(audio_manifest["entries"])
        for index, entry in enumerate(audio_manifest["entries"], start=1):
            audio_path = ovr_source._audio_path(cache_dir, str(entry["entry"]))
            samples = ovr_source.read_pcm16_mono(audio_path)
            weights, winners, delays_ms = analyze_continuous(oculus, samples)
            records.append(
                OculusUtterance(
                    split=str(entry["split"]),
                    speaker=int(entry["speaker"]),
                    prompt=int(entry["prompt"]),
                    prompt_ordinal=int(entry["promptOrdinal"]),
                    audio_entry=str(entry["entry"]),
                    continuous=weights,
                    winners=winners,
                    delays_ms=delays_ms,
                )
            )
            if index == total or index % 25 == 0:
                print(f"Oculus analyzed {index}/{total} utterances.", flush=True)

    audit = audit_extracted_records(records, dll_version)
    save_extraction_cache(cache_dir, records, audit)
    return records, audit


def records_for_split(
    records: Sequence[OculusUtterance], split: str
) -> list[OculusUtterance]:
    result = [record for record in records if record.split == split]
    if len(result) != EXPECTED_UTTERANCE_COUNTS[split]:
        raise ValueError(f"Unexpected {split} utterance count: {len(result)}")
    return result


def normalized_shape(record: OculusUtterance) -> tuple[np.ndarray, np.ndarray]:
    mass = record.continuous.sum(axis=1, dtype=np.float64)
    if np.any(mass <= MASS_EPSILON):
        raise ValueError(f"Zero Oculus mass in {record.audio_entry}")
    shape = record.continuous.astype(np.float64) / mass[:, None]
    return shape, mass


def fit_conditional_barycenters(
    records: Sequence[OculusUtterance],
) -> tuple[np.ndarray, np.ndarray]:
    sums = np.zeros((VISEME_COUNT, VISEME_COUNT), dtype=np.float64)
    counts = np.zeros(VISEME_COUNT, dtype=np.int64)
    for record in records:
        shape, _ = normalized_shape(record)
        for winner in range(VISEME_COUNT):
            mask = record.winners == winner
            count = int(np.count_nonzero(mask))
            if count:
                sums[winner] += shape[mask].sum(axis=0, dtype=np.float64)
                counts[winner] += count
    if np.any(counts[1:] < MIN_NON_SILENCE_FIT_FRAMES):
        raise ValueError(
            "Insufficient non-silence fit frames: "
            + ", ".join(
                f"{VISEMES[index]}={counts[index]}" for index in range(1, VISEME_COUNT)
            )
        )
    centers = np.zeros_like(sums)
    for winner in range(1, VISEME_COUNT):
        centers[winner] = sums[winner] / float(counts[winner])
        centers[winner] /= centers[winner].sum(dtype=np.float64)
    centers[SILENCE_INDEX, SILENCE_INDEX] = 1.0
    return centers, counts


def project_vector_to_simplex(values: np.ndarray) -> np.ndarray:
    """Euclidean projection onto the unit simplex (Duchi/Kyrillidis step)."""

    source = np.asarray(values, dtype=np.float64)
    if source.ndim != 1 or len(source) == 0 or not np.all(np.isfinite(source)):
        raise ValueError("Simplex projection requires one finite vector")
    ordered = np.sort(source)[::-1]
    cumulative = np.cumsum(ordered, dtype=np.float64) - 1.0
    active = ordered - cumulative / np.arange(1, len(ordered) + 1) > 0.0
    indices = np.flatnonzero(active)
    if len(indices) == 0:
        raise ValueError("Simplex projection found no active coordinate")
    rho = int(indices[-1])
    theta = cumulative[rho] / float(rho + 1)
    projected = np.maximum(source - theta, 0.0)
    projected /= projected.sum(dtype=np.float64)
    return projected


def project_conditional_barycenters_top_k(
    centers: np.ndarray, top_k: int
) -> np.ndarray:
    """Exact Euclidean k-sparse simplex projection, row by row.

    For a fixed cardinality the optimal support is the k largest coordinates.
    The retained vector is then projected onto the unit simplex, which adds a
    shared offset here because the discarded coordinates carried positive mass.
    """

    if centers.shape != (VISEME_COUNT, VISEME_COUNT):
        raise ValueError("Conditional barycenter table must be 15 x 15")
    if not 1 <= top_k <= VISEME_COUNT:
        raise ValueError("Top-k cardinality must be in [1,15]")
    result = np.zeros_like(centers, dtype=np.float64)
    result[SILENCE_INDEX, SILENCE_INDEX] = 1.0
    indices = np.arange(VISEME_COUNT)
    for winner in range(1, VISEME_COUNT):
        row = centers[winner]
        # lexsort makes equal-value support selection deterministic by preferring
        # the lower Oculus index after sorting primarily by descending weight.
        support = np.lexsort((indices, -row))[:top_k]
        if winner not in support:
            raise ValueError(
                f"{VISEMES[winner]} diagonal is not in its exact top-{top_k} support"
            )
        projected = project_vector_to_simplex(row[support])
        if np.any(projected <= 0.0):
            raise ValueError("Reviewed top-k projection unexpectedly lost a retained coordinate")
        result[winner, support] = projected
        if int(np.argmax(result[winner])) != winner:
            raise ValueError(
                f"{VISEMES[winner]} is not the maximum after top-{top_k} projection"
            )
        if np.count_nonzero(result[winner] == result[winner, winner]) != 1:
            raise ValueError(
                f"{VISEMES[winner]} is not a unique maximum after top-{top_k} projection"
            )
    return result


def repair_float32_simplex(row: np.ndarray, diagonal: int) -> np.ndarray:
    value = np.asarray(row, dtype=np.float64)
    if value.shape != (VISEME_COUNT,) or not np.all(np.isfinite(value)):
        raise ValueError("Halo row is malformed")
    value = np.maximum(value, 0.0)
    total = float(value.sum(dtype=np.float64))
    if total <= MASS_EPSILON:
        raise ValueError("Halo row has no mass")
    result = np.asarray(value / total, dtype=np.float32)
    residual = 1.0 - float(result.sum(dtype=np.float64))
    result[diagonal] = np.float32(float(result[diagonal]) + residual)
    if result[diagonal] < 0.0:
        raise ValueError("Float32 simplex repair made the diagonal negative")
    return result


def halo_table(centers: np.ndarray, strength: float) -> np.ndarray:
    if centers.shape != (VISEME_COUNT, VISEME_COUNT):
        raise ValueError("Conditional barycenter table must be 15 x 15")
    if not 0.0 <= strength <= 1.0:
        raise ValueError("Halo strength must be in [0,1]")
    identity = np.eye(VISEME_COUNT, dtype=np.float64)
    blended = (1.0 - strength) * identity + strength * centers
    table = np.empty_like(blended, dtype=np.float32)
    for winner in range(VISEME_COUNT):
        table[winner] = repair_float32_simplex(blended[winner], winner)
    table[SILENCE_INDEX].fill(0.0)
    table[SILENCE_INDEX, SILENCE_INDEX] = 1.0
    validate_table(table)
    return table


def validate_table(table: np.ndarray) -> None:
    value = np.asarray(table)
    if value.shape != (VISEME_COUNT, VISEME_COUNT):
        raise ValueError("Oculus halo table must be 15 x 15")
    if not np.all(np.isfinite(value)) or np.any(value < 0.0) or np.any(value > 1.0):
        raise ValueError("Oculus halo table contains a value outside [0,1]")
    row_sums = value.astype(np.float64).sum(axis=1)
    if np.max(np.abs(row_sums - 1.0)) > TABLE_SUM_TOLERANCE:
        raise ValueError("Oculus halo rows must sum to one")
    expected_silence = np.zeros(VISEME_COUNT, dtype=np.float32)
    expected_silence[SILENCE_INDEX] = 1.0
    if not np.array_equal(value[SILENCE_INDEX].astype(np.float32), expected_silence):
        raise ValueError("Oculus halo silence row must be bit-exact one-hot silence")
    for winner in range(1, VISEME_COUNT):
        maximum = float(np.max(value[winner]))
        if float(value[winner, winner]) != maximum:
            raise ValueError(f"{VISEMES[winner]} is not the maximum of its halo row")
        if np.count_nonzero(value[winner] == maximum) != 1:
            raise ValueError(f"{VISEMES[winner]} does not have a unique halo-row maximum")


def sparsify_simplex_coordinates(values: np.ndarray) -> np.ndarray:
    """Match AdvancedVisemeMath.SparsifySimplexCoordinate exactly on [0,1]."""

    sanitized = np.clip(np.asarray(values, dtype=np.float64), 0.0, 1.0)
    return np.where(
        sanitized <= SIMPLEX_CULLING_EPSILON,
        0.0,
        (sanitized - SIMPLEX_CULLING_EPSILON)
        / (1.0 - SIMPLEX_CULLING_EPSILON),
    )


def observe(winners: np.ndarray, table: np.ndarray) -> np.ndarray:
    alpha = 1.0 - math.exp(
        -(
            ovr_source.ANALYSIS_BUFFER_SAMPLES
            / ovr_source.ANALYSIS_SAMPLE_RATE
        )
        / OBSERVER_RESPONSE_SECONDS
    )
    fast = np.zeros(VISEME_COUNT, dtype=np.float64)
    fast[SILENCE_INDEX] = 1.0
    slow = fast.copy()
    output = np.empty((len(winners), VISEME_COUNT), dtype=np.float64)
    source = table.astype(np.float64, copy=False)
    for index, winner in enumerate(winners):
        # Generated layer order is Decoder -> Math.  The decoder publishes
        # sparsified copies of the previous exact observer state; Math consumes
        # those copies for the visible liveliness mix and only then advances the
        # private recurrence from this frame's decoded winner.
        emitted_fast = sparsify_simplex_coordinates(fast)
        emitted_slow = sparsify_simplex_coordinates(slow)
        output[index] = (
            (1.0 - EVALUATION_LIVELINESS) * emitted_slow
            + EVALUATION_LIVELINESS * emitted_fast
        )
        target = source[int(winner)]
        fast += alpha * (target - fast)
        slow += alpha * (fast - slow)
    return output


def cubic_trajectory_basis(age_seconds: float) -> np.ndarray:
    phase = min(1.0, max(0.0, age_seconds / TRAJECTORY_DURATION_SECONDS))
    inverse = 1.0 - phase
    return np.asarray(
        (
            inverse * inverse * inverse,
            3.0 * inverse * inverse * phase,
            3.0 * inverse * phase * phase,
            phase * phase * phase,
        ),
        dtype=np.float64,
    )


def trajectory_target(
    control_points: np.ndarray, winner: int, age_seconds: float
) -> np.ndarray:
    if control_points.shape != (
        VISEME_COUNT,
        TRAJECTORY_CONTROL_POINT_COUNT,
        VISEME_COUNT,
    ):
        raise ValueError("Oculus trajectory control-point tensor has the wrong shape")
    return cubic_trajectory_basis(age_seconds) @ control_points[int(winner)]


def project_trajectory_pose(
    values: np.ndarray, support: np.ndarray, winner: int
) -> np.ndarray:
    if winner not in support:
        raise ValueError(f"Trajectory support for {VISEMES[winner]} omits its winner")
    result = np.zeros(VISEME_COUNT, dtype=np.float64)
    result[support] = project_vector_to_simplex(values[support])
    other = np.delete(result, winner)
    maximum_other = float(other.max(initial=0.0))
    diagonal = float(result[winner])
    if diagonal <= maximum_other + TRAJECTORY_WINNER_MARGIN:
        denominator = 1.0 - diagonal + maximum_other
        authority = (
            (maximum_other - diagonal + TRAJECTORY_WINNER_MARGIN) / denominator
            if denominator > 0.0
            else 1.0
        )
        authority = min(1.0, max(0.0, authority))
        result *= 1.0 - authority
        result[winner] += authority
    result /= result.sum(dtype=np.float64)
    return result


def trajectory_normal_equations(
    records: Sequence[OculusUtterance],
) -> tuple[np.ndarray, np.ndarray]:
    parameter_count = VISEME_COUNT * TRAJECTORY_CONTROL_POINT_COUNT
    normal = np.zeros((parameter_count, parameter_count), dtype=np.float64)
    target = np.zeros((parameter_count, VISEME_COUNT), dtype=np.float64)
    block_seconds = (
        ovr_source.ANALYSIS_BUFFER_SAMPLES / ovr_source.ANALYSIS_SAMPLE_RATE
    )
    alpha = 1.0 - math.exp(-block_seconds / OBSERVER_RESPONSE_SECONDS)

    for record in records:
        teacher, _ = normalized_shape(record)
        fast = np.zeros(parameter_count, dtype=np.float64)
        fast[TRAJECTORY_CONTROL_POINT_COUNT - 1] = 1.0
        slow = fast.copy()
        previous_feature: np.ndarray | None = None
        previous_teacher: np.ndarray | None = None
        current_winner = -1
        age_seconds = 0.0
        for frame, winner_value in enumerate(record.winners):
            winner = int(winner_value)
            if winner != current_winner:
                current_winner = winner
                age_seconds = 0.0

            feature = (1.0 - EVALUATION_LIVELINESS) * slow + EVALUATION_LIVELINESS * fast
            transition = age_seconds < TRANSITION_WINDOW_BLOCKS * block_seconds
            pose_weight = 1.0 + (
                TRAJECTORY_POSE_TRANSITION_BOOST if transition else 0.0
            )
            normal += pose_weight * np.outer(feature, feature)
            target += pose_weight * np.outer(feature, teacher[frame])

            if previous_feature is not None and previous_teacher is not None:
                feature_delta = feature - previous_feature
                teacher_delta = teacher[frame] - previous_teacher
                normal += TRAJECTORY_DERIVATIVE_WEIGHT * np.outer(
                    feature_delta, feature_delta
                )
                target += TRAJECTORY_DERIVATIVE_WEIGHT * np.outer(
                    feature_delta, teacher_delta
                )

            source = np.zeros(parameter_count, dtype=np.float64)
            start = winner * TRAJECTORY_CONTROL_POINT_COUNT
            source[start : start + TRAJECTORY_CONTROL_POINT_COUNT] = (
                cubic_trajectory_basis(age_seconds)
            )
            fast += alpha * (source - fast)
            slow += alpha * (fast - slow)
            previous_feature = feature
            previous_teacher = teacher[frame]
            age_seconds += block_seconds

    return normal, target


def trajectory_render_rate_normal_equations(
    records: Sequence[OculusUtterance], frames_per_second: int
) -> tuple[np.ndarray, np.ndarray, int]:
    if frames_per_second <= 0:
        raise ValueError("Trajectory fitting render rate must be positive")
    parameter_count = VISEME_COUNT * TRAJECTORY_CONTROL_POINT_COUNT
    normal = np.zeros((parameter_count, parameter_count), dtype=np.float64)
    target = np.zeros((parameter_count, VISEME_COUNT), dtype=np.float64)
    delta_time = 1.0 / float(frames_per_second)
    block_time = (
        ovr_source.ANALYSIS_BUFFER_SAMPLES / ovr_source.ANALYSIS_SAMPLE_RATE
    )
    alpha = 1.0 - math.exp(-delta_time / OBSERVER_RESPONSE_SECONDS)
    fitted_frames = 0

    for record in records:
        teacher_shapes, _ = normalized_shape(record)
        availability = (
            np.arange(1, len(record.winners) + 1, dtype=np.float64) * block_time
        )
        end_time = availability[-1] + block_time
        render_times = np.arange(
            delta_time,
            end_time + delta_time * 0.25,
            delta_time,
            dtype=np.float64,
        )
        fast = np.zeros(parameter_count, dtype=np.float64)
        fast[TRAJECTORY_CONTROL_POINT_COUNT - 1] = 1.0
        slow = fast.copy()
        previous_feature: np.ndarray | None = None
        previous_teacher: np.ndarray | None = None
        state_winner = SILENCE_INDEX
        state_age_seconds = 0.0
        for render_time in render_times:
            result_index = int(
                np.searchsorted(availability, render_time, side="right") - 1
            )
            winner = (
                SILENCE_INDEX
                if result_index < 0
                else int(record.winners[result_index])
            )
            if winner != state_winner:
                state_winner = winner
                state_age_seconds = 0.0

            feature = (1.0 - EVALUATION_LIVELINESS) * slow + EVALUATION_LIVELINESS * fast
            if result_index >= 0:
                teacher = teacher_shapes[result_index]
                transition = (
                    state_age_seconds < TRANSITION_WINDOW_BLOCKS * block_time
                )
                pose_weight = 1.0 + (
                    TRAJECTORY_POSE_TRANSITION_BOOST if transition else 0.0
                )
                normal += pose_weight * np.outer(feature, feature)
                target += pose_weight * np.outer(feature, teacher)
                fitted_frames += 1
                if previous_feature is not None and previous_teacher is not None:
                    feature_delta = feature - previous_feature
                    teacher_delta = teacher - previous_teacher
                    normal += TRAJECTORY_DERIVATIVE_WEIGHT * np.outer(
                        feature_delta, feature_delta
                    )
                    target += TRAJECTORY_DERIVATIVE_WEIGHT * np.outer(
                        feature_delta, teacher_delta
                    )
                previous_feature = feature
                previous_teacher = teacher

            source = np.zeros(parameter_count, dtype=np.float64)
            start = winner * TRAJECTORY_CONTROL_POINT_COUNT
            source[start : start + TRAJECTORY_CONTROL_POINT_COUNT] = (
                cubic_trajectory_basis(state_age_seconds)
            )
            fast += alpha * (source - fast)
            slow += alpha * (fast - slow)
            state_age_seconds += delta_time

    return normal, target, fitted_frames


def trajectory_combined_normal_equations(
    records: Sequence[OculusUtterance],
) -> tuple[np.ndarray, np.ndarray]:
    normal, target = trajectory_normal_equations(records)
    native_frames = sum(len(record.winners) for record in records)
    for frames_per_second in RENDER_RATE_FPS:
        render_normal, render_target, render_frames = (
            trajectory_render_rate_normal_equations(records, frames_per_second)
        )
        if render_frames <= 0:
            raise ValueError("Trajectory render-rate fitting produced no frames")
        authority = (
            TRAJECTORY_RENDER_RATE_FIT_WEIGHT
            * float(native_frames)
            / float(render_frames)
        )
        normal += authority * render_normal
        target += authority * render_target
    return normal, target


def fit_trajectory_control_points(
    records: Sequence[OculusUtterance],
    static_table: np.ndarray,
    curvature_ratio: float,
    terminal_ratio: float,
    combined_normal_equations: tuple[np.ndarray, np.ndarray] | None = None,
) -> tuple[np.ndarray, dict[str, Any]]:
    if static_table.shape != (VISEME_COUNT, VISEME_COUNT):
        raise ValueError("Static trajectory support table must be 15 x 15")
    if curvature_ratio not in TRAJECTORY_CURVATURE_RATIO_CANDIDATES:
        raise ValueError("Trajectory curvature ratio is outside the reviewed grid")
    allowed_terminal_ratios = {
        curvature_ratio * multiplier
        for multiplier in TRAJECTORY_TERMINAL_RATIO_MULTIPLIERS
    }
    if terminal_ratio not in allowed_terminal_ratios:
        raise ValueError("Trajectory terminal ratio is outside the reviewed grid")
    if combined_normal_equations is None:
        combined_normal_equations = trajectory_combined_normal_equations(records)
    normal = combined_normal_equations[0].copy()
    target = combined_normal_equations[1].copy()
    parameter_count = VISEME_COUNT * TRAJECTORY_CONTROL_POINT_COUNT
    baseline = np.repeat(
        static_table[:, None, :], TRAJECTORY_CONTROL_POINT_COUNT, axis=1
    ).reshape(parameter_count, VISEME_COUNT)
    scale = max(1.0, float(np.trace(normal)) / float(parameter_count))
    regularization = np.full(
        parameter_count, TRAJECTORY_RIDGE_RATIO * scale, dtype=np.float64
    )
    for winner in range(VISEME_COUNT):
        endpoint = (
            winner * TRAJECTORY_CONTROL_POINT_COUNT
            + TRAJECTORY_CONTROL_POINT_COUNT
            - 1
        )
        regularization[endpoint] += TRAJECTORY_ENDPOINT_RIDGE_RATIO * scale
    curvature = np.zeros((parameter_count, parameter_count), dtype=np.float64)
    terminal = np.zeros_like(curvature)
    second_difference = np.asarray(
        ((1.0, -2.0, 1.0, 0.0), (0.0, 1.0, -2.0, 1.0)),
        dtype=np.float64,
    )
    integrated_metric = np.asarray(
        ((1.0, 0.5), (0.5, 1.0)), dtype=np.float64
    )
    # Integral_0^1 ||B''(t)||^2 dt, normalized by its average
    # diagonal (24), has a two-dimensional straight-line nullspace.
    local_curvature = (
        12.0
        * second_difference.T
        @ integrated_metric
        @ second_difference
        / 24.0
    )
    terminal_slope = np.asarray((0.0, 0.0, -1.0, 1.0), dtype=np.float64)
    local_terminal = 2.0 * np.outer(terminal_slope, terminal_slope)
    for winner in range(VISEME_COUNT):
        start = winner * TRAJECTORY_CONTROL_POINT_COUNT
        indices = np.arange(start, start + TRAJECTORY_CONTROL_POINT_COUNT)
        curvature[np.ix_(indices, indices)] += local_curvature
        terminal[np.ix_(indices, indices)] += local_terminal
    system = (
        normal
        + np.diag(regularization)
        + curvature_ratio * scale * curvature
        + terminal_ratio * scale * terminal
    )
    right = target + regularization[:, None] * baseline
    supports = [
        np.flatnonzero(static_table[winner] > 0.0)
        for winner in range(VISEME_COUNT)
    ]

    controls = np.linalg.solve(system, right)

    def project_all(values: np.ndarray) -> np.ndarray:
        projected = values.copy()
        for winner in range(VISEME_COUNT):
            for control in range(TRAJECTORY_CONTROL_POINT_COUNT):
                row = winner * TRAJECTORY_CONTROL_POINT_COUNT + control
                projected[row] = project_trajectory_pose(
                    projected[row], supports[winner], winner
                )
        projected[:TRAJECTORY_CONTROL_POINT_COUNT] = 0.0
        projected[:TRAJECTORY_CONTROL_POINT_COUNT, SILENCE_INDEX] = 1.0
        return projected

    controls = project_all(controls)
    largest_eigenvalue = float(np.linalg.eigvalsh(system)[-1])
    if not math.isfinite(largest_eigenvalue) or largest_eigenvalue <= 0.0:
        raise ValueError("Trajectory normal system is not positive definite")
    projected_steps = 0
    projected_delta = math.inf
    for projected_steps in range(1, TRAJECTORY_PROJECTED_GRADIENT_STEPS + 1):
        gradient = system @ controls - right
        next_controls = project_all(controls - gradient / largest_eigenvalue)
        denominator = max(1.0, float(np.linalg.norm(controls)))
        projected_delta = float(
            np.linalg.norm(next_controls - controls) / denominator
        )
        controls = next_controls
        if projected_delta <= TRAJECTORY_PROJECTED_GRADIENT_TOLERANCE:
            break

    residual = system @ controls - right
    tensor = controls.reshape(
        VISEME_COUNT, TRAJECTORY_CONTROL_POINT_COUNT, VISEME_COUNT
    )
    validate_trajectory_control_points(tensor, static_table)
    diagnostics = {
        "durationSeconds": TRAJECTORY_DURATION_SECONDS,
        "controlPointCount": TRAJECTORY_CONTROL_POINT_COUNT,
        "normalTrace": float(np.trace(normal)),
        "normalConditionNumber": float(np.linalg.cond(system)),
        "projectedGradientSteps": projected_steps,
        "projectedGradientMaximumSteps": TRAJECTORY_PROJECTED_GRADIENT_STEPS,
        "projectedGradientRelativeDelta": projected_delta,
        "projectedGradientTolerance": TRAJECTORY_PROJECTED_GRADIENT_TOLERANCE,
        "projectedFirstOrderResidualRms": float(np.sqrt(np.mean(np.square(residual)))),
        "poseTransitionBoost": TRAJECTORY_POSE_TRANSITION_BOOST,
        "derivativeWeight": TRAJECTORY_DERIVATIVE_WEIGHT,
        "renderRateFitWeightPerRate": TRAJECTORY_RENDER_RATE_FIT_WEIGHT,
        "renderRateFitFps": list(RENDER_RATE_FPS),
        "ridgeRatio": TRAJECTORY_RIDGE_RATIO,
        "endpointRidgeRatio": TRAJECTORY_ENDPOINT_RIDGE_RATIO,
        "curvatureRatio": curvature_ratio,
        "terminalSlopeRatio": terminal_ratio,
        "curvaturePenalty": "normalized exact integrated squared cubic Bezier curvature",
        "terminalSlopePenalty": "normalized squared P3-P2 clamp discontinuity",
        "maximumEndpointL1DriftFromStatic": float(np.max(np.sum(
            np.abs(tensor[:, -1, :] - static_table), axis=1
        ))),
    }
    return tensor, diagnostics


def validate_trajectory_control_points(
    control_points: np.ndarray, static_table: np.ndarray
) -> None:
    expected = (
        VISEME_COUNT,
        TRAJECTORY_CONTROL_POINT_COUNT,
        VISEME_COUNT,
    )
    if control_points.shape != expected or not np.all(np.isfinite(control_points)):
        raise ValueError("Oculus trajectory control points must be one finite 15 x 4 x 15 tensor")
    if np.any(control_points < -1e-12) or np.any(control_points > 1.0 + 1e-12):
        raise ValueError("Oculus trajectory control point lies outside [0,1]")
    if not np.allclose(control_points.sum(axis=2), 1.0, atol=TABLE_SUM_TOLERANCE):
        raise ValueError("Oculus trajectory control points must lie on the unit simplex")
    expected_silence = np.zeros(VISEME_COUNT, dtype=np.float64)
    expected_silence[SILENCE_INDEX] = 1.0
    if not np.array_equal(control_points[SILENCE_INDEX], np.tile(
        expected_silence, (TRAJECTORY_CONTROL_POINT_COUNT, 1)
    )):
        raise ValueError("Oculus silence trajectory must remain bit-exact one-hot")
    for winner in range(1, VISEME_COUNT):
        static_support = static_table[winner] > 0.0
        for control in range(TRAJECTORY_CONTROL_POINT_COUNT):
            row = control_points[winner, control]
            if np.any(row[~static_support] != 0.0):
                raise ValueError(
                    f"{VISEMES[winner]} trajectory escaped its reviewed sparse support"
                )
            if int(np.argmax(row)) != winner:
                raise ValueError(
                    f"{VISEMES[winner]} is not dominant at control point {control}"
                )


def observe_trajectory(
    winners: np.ndarray, control_points: np.ndarray, delta_time: float | None = None
) -> np.ndarray:
    step = (
        ovr_source.ANALYSIS_BUFFER_SAMPLES / ovr_source.ANALYSIS_SAMPLE_RATE
        if delta_time is None
        else float(delta_time)
    )
    alpha = 1.0 - math.exp(-step / OBSERVER_RESPONSE_SECONDS)
    fast = np.zeros(VISEME_COUNT, dtype=np.float64)
    fast[SILENCE_INDEX] = 1.0
    slow = fast.copy()
    output = np.empty((len(winners), VISEME_COUNT), dtype=np.float64)
    current_winner = -1
    age_seconds = 0.0
    for index, winner_value in enumerate(winners):
        winner = int(winner_value)
        if winner != current_winner:
            current_winner = winner
            age_seconds = 0.0
        emitted_fast = sparsify_simplex_coordinates(fast)
        emitted_slow = sparsify_simplex_coordinates(slow)
        output[index] = (
            (1.0 - EVALUATION_LIVELINESS) * emitted_slow
            + EVALUATION_LIVELINESS * emitted_fast
        )
        target = trajectory_target(control_points, winner, age_seconds)
        fast += alpha * (target - fast)
        slow += alpha * (fast - slow)
        age_seconds += step
    return output


def evaluate_predictions(records: Sequence[OculusUtterance], predictor: Any) -> dict[str, Any]:
    squared_error = 0.0
    absolute_error = 0.0
    raw_squared_error = 0.0
    derivative_squared_error = 0.0
    transition_squared_error = 0.0
    predicted_tv_sum = 0.0
    teacher_tv_sum = 0.0
    derivative_elements = 0
    transition_elements = 0
    elements = 0
    frames = 0
    correct = 0
    per_winner_squared = np.zeros(VISEME_COUNT, dtype=np.float64)
    per_winner_elements = np.zeros(VISEME_COUNT, dtype=np.int64)

    for record in records:
        teacher, mass = normalized_shape(record)
        predicted = predictor(record)
        error = predicted - teacher
        squared_error += float(np.square(error).sum(dtype=np.float64))
        absolute_error += float(np.abs(error).sum(dtype=np.float64))
        raw_error = predicted * mass[:, None] - record.continuous.astype(np.float64)
        raw_squared_error += float(np.square(raw_error).sum(dtype=np.float64))
        elements += error.size
        frames += len(error)
        correct += int(np.count_nonzero(np.argmax(predicted, axis=1) == record.winners))
        if len(error) > 1:
            predicted_delta = np.diff(predicted, axis=0)
            teacher_delta = np.diff(teacher, axis=0)
            predicted_tv_sum += float(np.abs(predicted_delta).sum(dtype=np.float64))
            teacher_tv_sum += float(np.abs(teacher_delta).sum(dtype=np.float64))
            derivative_squared_error += float(
                np.square(predicted_delta - teacher_delta).sum(dtype=np.float64)
            )
            derivative_elements += predicted_delta.size
            transition_mask = np.zeros(len(error), dtype=bool)
            change_frames = np.flatnonzero(record.winners[1:] != record.winners[:-1]) + 1
            for change_frame in change_frames:
                end = min(len(error), int(change_frame) + TRANSITION_WINDOW_BLOCKS)
                transition_mask[int(change_frame) : end] = True
            if np.any(transition_mask):
                transition_error = error[transition_mask]
                transition_squared_error += float(
                    np.square(transition_error).sum(dtype=np.float64)
                )
                transition_elements += transition_error.size
        for winner in range(VISEME_COUNT):
            mask = record.winners == winner
            count = int(np.count_nonzero(mask))
            if count:
                per_winner_squared[winner] += float(
                    np.square(error[mask]).sum(dtype=np.float64)
                )
                per_winner_elements[winner] += count * VISEME_COUNT

    if (
        elements == 0
        or derivative_elements == 0
        or transition_elements == 0
        or teacher_tv_sum <= 0.0
    ):
        raise ValueError("Evaluation set does not contain an informative trajectory")
    mse = squared_error / elements
    predicted_tv = predicted_tv_sum / derivative_elements
    teacher_tv = teacher_tv_sum / derivative_elements
    per_winner = {
        VISEMES[index]: {
            "frames": int(per_winner_elements[index] // VISEME_COUNT),
            "mse": float(per_winner_squared[index] / per_winner_elements[index]),
        }
        for index in range(VISEME_COUNT)
        if per_winner_elements[index] > 0
    }
    return {
        "frames": frames,
        "mse": mse,
        "rmse": math.sqrt(mse),
        "brier": squared_error / frames,
        "meanTotalVariationDistance": 0.5 * absolute_error / frames,
        "top1Agreement": correct / frames,
        "predictedTemporalVariation": predicted_tv,
        "teacherTemporalVariation": teacher_tv,
        "temporalVariationRatio": predicted_tv / teacher_tv,
        "derivativeMse": derivative_squared_error / derivative_elements,
        "transitionWindowBlocks": TRANSITION_WINDOW_BLOCKS,
        "transitionMse": transition_squared_error / transition_elements,
        "rawMassOracleMse": raw_squared_error / elements,
        "perWinner": per_winner,
    }


def evaluate(
    records: Sequence[OculusUtterance], table: np.ndarray
) -> dict[str, Any]:
    return evaluate_predictions(records, lambda record: observe(record.winners, table))


def evaluate_trajectory(
    records: Sequence[OculusUtterance], control_points: np.ndarray
) -> dict[str, Any]:
    return evaluate_predictions(
        records, lambda record: observe_trajectory(record.winners, control_points)
    )


def evaluate_render_rate_zoh_with_target(
    records: Sequence[OculusUtterance],
    frames_per_second: int,
    target_at_age: Any,
) -> dict[str, Any]:
    """Replay the public Animator path with native OVR results held at render FPS."""

    if frames_per_second <= 0:
        raise ValueError("Render rate must be positive")
    delta_time = 1.0 / float(frames_per_second)
    block_time = (
        ovr_source.ANALYSIS_BUFFER_SAMPLES / ovr_source.ANALYSIS_SAMPLE_RATE
    )
    alpha = 1.0 - math.exp(-delta_time / OBSERVER_RESPONSE_SECONDS)
    squared_error = 0.0
    derivative_squared_error = 0.0
    transition_squared_error = 0.0
    predicted_tv_sum = 0.0
    teacher_tv_sum = 0.0
    elements = 0
    derivative_elements = 0
    transition_elements = 0
    frames = 0
    correct = 0
    minimum_prediction = math.inf
    maximum_prediction = -math.inf

    for record in records:
        teacher_shapes, _ = normalized_shape(record)
        availability = (
            np.arange(1, len(record.winners) + 1, dtype=np.float64) * block_time
        )
        change_indices = np.flatnonzero(
            record.winners[1:] != record.winners[:-1]
        ) + 1
        change_times = availability[change_indices]
        # Extend one native block after the last result so every copied teacher
        # frame owns a complete zero-order-hold interval.
        end_time = availability[-1] + block_time
        render_times = np.arange(
            delta_time,
            end_time + delta_time * 0.25,
            delta_time,
            dtype=np.float64,
        )

        fast = np.zeros(VISEME_COUNT, dtype=np.float64)
        fast[SILENCE_INDEX] = 1.0
        slow = fast.copy()
        previous_prediction: np.ndarray | None = None
        previous_teacher: np.ndarray | None = None
        state_winner = SILENCE_INDEX
        state_age_seconds = 0.0
        for render_time in render_times:
            result_index = int(
                np.searchsorted(availability, render_time, side="right") - 1
            )
            winner = (
                SILENCE_INDEX
                if result_index < 0
                else int(record.winners[result_index])
            )
            if winner != state_winner:
                state_winner = winner
                state_age_seconds = 0.0

            emitted_fast = sparsify_simplex_coordinates(fast)
            emitted_slow = sparsify_simplex_coordinates(slow)
            prediction = (
                (1.0 - EVALUATION_LIVELINESS) * emitted_slow
                + EVALUATION_LIVELINESS * emitted_fast
            )
            target = target_at_age(winner, state_age_seconds)
            fast += alpha * (target - fast)
            slow += alpha * (fast - slow)
            state_age_seconds += delta_time

            # The pre-roll before the first complete native block establishes
            # exact silence state but carries no observed Oculus teacher target.
            if result_index < 0:
                continue
            teacher = teacher_shapes[result_index]
            error = prediction - teacher
            squared_error += float(np.square(error).sum(dtype=np.float64))
            elements += VISEME_COUNT
            frames += 1
            correct += int(int(np.argmax(prediction)) == winner)
            minimum_prediction = min(minimum_prediction, float(prediction.min()))
            maximum_prediction = max(maximum_prediction, float(prediction.max()))

            if previous_prediction is not None and previous_teacher is not None:
                predicted_delta = prediction - previous_prediction
                teacher_delta = teacher - previous_teacher
                predicted_tv_sum += float(np.abs(predicted_delta).sum(dtype=np.float64))
                teacher_tv_sum += float(np.abs(teacher_delta).sum(dtype=np.float64))
                derivative_squared_error += float(
                    np.square(predicted_delta - teacher_delta).sum(dtype=np.float64)
                )
                derivative_elements += VISEME_COUNT

            latest_change = int(
                np.searchsorted(change_times, render_time, side="right") - 1
            )
            if (
                latest_change >= 0
                and render_time
                < change_times[latest_change]
                + TRANSITION_WINDOW_BLOCKS * block_time
                - 1e-12
            ):
                transition_squared_error += float(
                    np.square(error).sum(dtype=np.float64)
                )
                transition_elements += VISEME_COUNT

            previous_prediction = prediction
            previous_teacher = teacher

    if (
        elements == 0
        or derivative_elements == 0
        or transition_elements == 0
        or teacher_tv_sum <= 0.0
    ):
        raise ValueError("Render-rate replay did not contain an informative trajectory")
    mse = squared_error / elements
    return {
        "framesPerSecond": frames_per_second,
        "frames": frames,
        "mse": mse,
        "rmse": math.sqrt(mse),
        "brier": squared_error / frames,
        "top1Agreement": correct / frames,
        "transitionWindowBlocks": TRANSITION_WINDOW_BLOCKS,
        "transitionMse": transition_squared_error / transition_elements,
        "derivativeMse": derivative_squared_error / derivative_elements,
        "predictedTemporalVariation": predicted_tv_sum / derivative_elements,
        "teacherTemporalVariation": teacher_tv_sum / derivative_elements,
        "temporalVariationRatio": predicted_tv_sum / teacher_tv_sum,
        "minimumPrediction": minimum_prediction,
        "maximumPrediction": maximum_prediction,
        "finiteAndBounded": (
            math.isfinite(minimum_prediction)
            and math.isfinite(maximum_prediction)
            and minimum_prediction >= -1e-12
            and maximum_prediction <= 1.0 + 1e-12
        ),
        "availability": (
            "native block n becomes available at (n+1)*1024/48000; winner and "
            "normalized teacher shape are held until the next native block"
        ),
        "preRoll": "one-hot silence before the first block; excluded from scoring",
    }


def evaluate_render_rate_zoh(
    records: Sequence[OculusUtterance],
    table: np.ndarray,
    frames_per_second: int,
) -> dict[str, Any]:
    source = table.astype(np.float64, copy=False)
    return evaluate_render_rate_zoh_with_target(
        records,
        frames_per_second,
        lambda winner, _age_seconds: source[int(winner)],
    )


def evaluate_trajectory_render_rate_zoh(
    records: Sequence[OculusUtterance],
    control_points: np.ndarray,
    frames_per_second: int,
) -> dict[str, Any]:
    return evaluate_render_rate_zoh_with_target(
        records,
        frames_per_second,
        lambda winner, age_seconds: trajectory_target(
            control_points, int(winner), float(age_seconds)
        ),
    )


def render_rate_sweep(
    records: Sequence[OculusUtterance],
    identity_table: np.ndarray,
    selected_table: np.ndarray,
    dense_table: np.ndarray,
) -> dict[str, Any]:
    rows: list[dict[str, Any]] = []
    failures: list[str] = []
    for frames_per_second in RENDER_RATE_FPS:
        identity = evaluate_render_rate_zoh(
            records, identity_table, frames_per_second
        )
        selected = evaluate_render_rate_zoh(
            records, selected_table, frames_per_second
        )
        dense = evaluate_render_rate_zoh(
            records, dense_table, frames_per_second
        )
        mse_retention = gain_retention(
            float(identity["mse"]),
            float(selected["mse"]),
            float(dense["mse"]),
        )
        transition_retention = gain_retention(
            float(identity["transitionMse"]),
            float(selected["transitionMse"]),
            float(dense["transitionMse"]),
        )
        beats_identity = (
            float(selected["mse"]) < float(identity["mse"])
            and float(selected["transitionMse"])
            < float(identity["transitionMse"])
            and float(selected["derivativeMse"])
            < float(identity["derivativeMse"])
        )
        finite_and_bounded = all(
            bool(metrics["finiteAndBounded"])
            for metrics in (identity, selected, dense)
        )
        passed = (
            beats_identity
            and finite_and_bounded
            and mse_retention >= MIN_RENDER_RATE_GAIN_RETENTION
            and transition_retention >= MIN_RENDER_RATE_GAIN_RETENTION
        )
        if not passed:
            failures.append(
                f"{frames_per_second} FPS: beatsIdentity={beats_identity}, "
                f"finite={finite_and_bounded}, mseRetention={mse_retention:.6f}, "
                f"transitionRetention={transition_retention:.6f}"
            )
        rows.append(
            {
                "framesPerSecond": frames_per_second,
                "identity": identity,
                "selected": selected,
                "dense": dense,
                "selectedMseGainRetentionVersusDense": mse_retention,
                "selectedTransitionGainRetentionVersusDense": transition_retention,
                "selectedBeatsIdentityOnAllErrorMetrics": beats_identity,
                "allPredictionsFiniteAndBounded": finite_and_bounded,
                "passesQualityThresholds": passed,
            }
        )
    return {
        "rates": list(RENDER_RATE_FPS),
        "minimumMseAndTransitionGainRetention": MIN_RENDER_RATE_GAIN_RETENTION,
        "rows": rows,
        "passed": not failures,
        "failures": failures,
        "selectionUse": (
            "Development sweep is a post-selection quality gate only. It does not "
            "retune top-k or halo strength. Heldout sweep is reporting only."
        ),
    }


def objective_metrics(metrics: dict[str, Any], identity_mse: float) -> dict[str, float]:
    mse_ratio = float(metrics["mse"]) / identity_mse
    tv_ratio = float(metrics["temporalVariationRatio"])
    tv_shortfall = max(0.0, TV_FLOOR_RATIO - tv_ratio)
    objective = mse_ratio + TV_PENALTY_WEIGHT * tv_shortfall * tv_shortfall
    return {
        "mseRatioToIdentity": mse_ratio,
        "temporalVariationRatio": tv_ratio,
        "temporalVariationShortfall": tv_shortfall,
        "objective": objective,
    }


def tune_halo_strength(
    centers: np.ndarray,
    development_records: Sequence[OculusUtterance],
) -> tuple[float, dict[str, Any], dict[str, float], list[dict[str, Any]]]:
    identity_metrics = evaluate(development_records, halo_table(centers, 0.0))
    identity_mse = float(identity_metrics["mse"])
    grid: list[dict[str, Any]] = []
    best_strength = 0.0
    best_objective = math.inf
    best_metrics: dict[str, Any] | None = None
    best_objective_metrics: dict[str, float] | None = None
    for strength in HALO_STRENGTH_CANDIDATES:
        table = halo_table(centers, strength)
        metrics = evaluate(development_records, table)
        objective = objective_metrics(metrics, identity_mse)
        row = {
            "haloStrength": strength,
            **objective,
            "mse": float(metrics["mse"]),
            "brier": float(metrics["brier"]),
            "rmse": float(metrics["rmse"]),
            "top1Agreement": float(metrics["top1Agreement"]),
            "transitionMse": float(metrics["transitionMse"]),
            "derivativeMse": float(metrics["derivativeMse"]),
            "predictedTemporalVariation": float(metrics["predictedTemporalVariation"]),
            "teacherTemporalVariation": float(metrics["teacherTemporalVariation"]),
        }
        grid.append(row)
        value = float(objective["objective"])
        # Candidates are traversed from smallest to largest.  Strictly-lower
        # replacement therefore makes exact ties select less halo.
        if value < best_objective:
            best_objective = value
            best_strength = strength
            best_metrics = metrics
            best_objective_metrics = objective
    if best_metrics is None or best_objective_metrics is None:
        raise AssertionError("Halo-strength grid produced no candidate")
    return best_strength, best_metrics, best_objective_metrics, grid


def gain_retention(identity: float, candidate: float, dense: float) -> float:
    dense_gain = identity - dense
    if dense_gain <= 0.0:
        return -math.inf
    return (identity - candidate) / dense_gain


def support_statistics(table: np.ndarray) -> dict[str, Any]:
    positive = np.count_nonzero(table > 0.0, axis=1)
    live = np.count_nonzero(table > SIMPLEX_CULLING_EPSILON, axis=1)
    return {
        "float32PositiveSupport": {
            "minimum": int(positive.min()),
            "maximum": int(positive.max()),
            "mean": float(positive.mean()),
            "nonSilenceMaximum": int(positive[1:].max()),
            "nonSilenceMean": float(positive[1:].mean()),
        },
        "supportAboveSimplexCullingEpsilon": {
            "epsilon": SIMPLEX_CULLING_EPSILON,
            "minimum": int(live.min()),
            "maximum": int(live.max()),
            "mean": float(live.mean()),
            "nonSilenceMaximum": int(live[1:].max()),
            "nonSilenceMean": float(live[1:].mean()),
        },
    }


def select_cardinality_and_strength(
    fit_records: Sequence[OculusUtterance],
    development_records: Sequence[OculusUtterance],
) -> tuple[
    CardinalityCandidate,
    np.ndarray,
    list[dict[str, Any]],
]:
    dense_centers, counts = fit_conditional_barycenters(fit_records)
    candidates: list[CardinalityCandidate] = []
    for top_k in TOP_K_CANDIDATES:
        centers = project_conditional_barycenters_top_k(dense_centers, top_k)
        strength, metrics, objective, grid = tune_halo_strength(
            centers, development_records
        )
        candidates.append(
            CardinalityCandidate(
                top_k=top_k,
                centers=centers,
                halo_strength=strength,
                metrics=metrics,
                objective=objective,
                grid=grid,
            )
        )

    dense = next(candidate for candidate in candidates if candidate.top_k == DENSE_TOP_K)
    identity_metrics = evaluate(
        development_records, halo_table(dense.centers, 0.0)
    )
    reports: list[dict[str, Any]] = []
    accepted: list[CardinalityCandidate] = []
    for candidate in candidates:
        overall_retention = gain_retention(
            float(identity_metrics["mse"]),
            float(candidate.metrics["mse"]),
            float(dense.metrics["mse"]),
        )
        transition_retention = gain_retention(
            float(identity_metrics["transitionMse"]),
            float(candidate.metrics["transitionMse"]),
            float(dense.metrics["transitionMse"]),
        )
        velocity_retention = gain_retention(
            float(identity_metrics["derivativeMse"]),
            float(candidate.metrics["derivativeMse"]),
            float(dense.metrics["derivativeMse"]),
        )
        no_metric_worse_than_identity = (
            float(candidate.metrics["mse"]) <= float(identity_metrics["mse"])
            and float(candidate.metrics["transitionMse"])
            <= float(identity_metrics["transitionMse"])
            and float(candidate.metrics["derivativeMse"])
            <= float(identity_metrics["derivativeMse"])
        )
        satisfies_tv_floor = (
            float(candidate.metrics["temporalVariationRatio"])
            >= TV_FLOOR_RATIO - 1e-12
        )
        is_accepted = (
            overall_retention >= MIN_OVERALL_GAIN_RETENTION
            and transition_retention >= MIN_TRANSITION_GAIN_RETENTION
            and velocity_retention >= MIN_VELOCITY_GAIN_RETENTION
            and no_metric_worse_than_identity
        )
        if is_accepted:
            accepted.append(candidate)
        reports.append(
            {
                "topK": candidate.top_k,
                "selectedHaloStrength": candidate.halo_strength,
                "developmentMetrics": candidate.metrics,
                "developmentObjective": candidate.objective,
                "overallMseGainRetentionVersusDense": overall_retention,
                "transitionMseGainRetentionVersusDense": transition_retention,
                "velocityMseGainRetentionVersusDense": velocity_retention,
                "noMetricWorseThanIdentity": no_metric_worse_than_identity,
                "temporalVariationFloorDiagnostic": satisfies_tv_floor,
                "acceptedForShipping": is_accepted,
                "support": support_statistics(
                    halo_table(candidate.centers, candidate.halo_strength)
                ),
                "developmentGrid": candidate.grid,
            }
        )

    if accepted:
        selected = min(accepted, key=cardinality_selection_key)
    else:
        # The dense candidate remains the conservative fallback even if a future
        # extraction fails the sparse acceptance gate.  The audit makes that
        # failure explicit rather than silently selecting an approximation.
        selected = dense
    return selected, counts, reports


def mass_statistics(records: Sequence[OculusUtterance]) -> dict[str, Any]:
    masses = np.concatenate(
        [record.continuous.sum(axis=1, dtype=np.float64) for record in records]
    )
    quantiles = (0.0, 0.001, 0.01, 0.05, 0.5, 0.95, 0.99, 0.999, 1.0)
    values = np.quantile(masses, quantiles)
    return {
        "frames": int(len(masses)),
        "minimum": float(masses.min()),
        "maximum": float(masses.max()),
        "mean": float(masses.mean()),
        "quantiles": {
            f"p{quantile * 100:g}": float(value)
            for quantile, value in zip(quantiles, values)
        },
        "nonPositiveFrames": int(np.count_nonzero(masses <= MASS_EPSILON)),
        "usage": "Diagnostics only; fitting normalizes each positive frame by its own mass.",
    }


def split_summary(records: Sequence[OculusUtterance]) -> dict[str, Any]:
    winners = np.concatenate([record.winners for record in records])
    return {
        "utterances": len(records),
        "speakers": sorted({record.speaker for record in records}),
        "frames": int(len(winners)),
        "winnerFrameCounts": {
            VISEMES[index]: int(count)
            for index, count in enumerate(np.bincount(winners, minlength=VISEME_COUNT))
        },
    }


def relative_gain(baseline: dict[str, Any], candidate: dict[str, Any], key: str) -> float:
    reference = float(baseline[key])
    if reference <= 0.0 or not math.isfinite(reference):
        raise ValueError(f"Trajectory baseline metric {key} is not positive and finite")
    value = float(candidate[key])
    if not math.isfinite(value):
        raise ValueError(f"Trajectory candidate metric {key} is not finite")
    return 1.0 - value / reference


def valley_prominence(values: np.ndarray) -> float:
    values = np.asarray(values, dtype=np.float64)
    if values.ndim != 1 or len(values) < 3:
        raise ValueError("Valley-prominence trace must be one-dimensional")
    prefix_max = np.maximum.accumulate(values)
    suffix_max = np.maximum.accumulate(values[::-1])[::-1]
    valleys = np.minimum(prefix_max - values, suffix_max - values)
    return max(0.0, float(np.max(valleys[1:-1], initial=0.0)))


def held_transition_secondary_reversal(
    control_points: np.ndarray,
) -> dict[str, Any]:
    worst = {
        "winnerAmplitude": 0.0,
        "relativeWinnerAmplitude": 0.0,
        "progressAmplitude": 0.0,
        "settlementError": 0.0,
        "winnerFramesPerSecond": 0,
        "winner": VISEMES[SILENCE_INDEX],
        "relativeWinnerFramesPerSecond": 0,
        "relativeWinner": VISEMES[SILENCE_INDEX],
        "progressFramesPerSecond": 0,
        "progressWinner": VISEMES[SILENCE_INDEX],
        "settlementFramesPerSecond": 0,
        "settlementWinner": VISEMES[SILENCE_INDEX],
    }
    audit_rates = RENDER_RATE_FPS + (1000,)
    for frames_per_second in audit_rates:
        delta_time = 1.0 / float(frames_per_second)
        alpha = 1.0 - math.exp(-delta_time / OBSERVER_RESPONSE_SECONDS)
        frame_count = int(math.ceil(
            (
                TRAJECTORY_DURATION_SECONDS
                + 12.0 * OBSERVER_RESPONSE_SECONDS
            )
            / delta_time
        ))
        for winner in range(1, VISEME_COUNT):
            fast = np.zeros(VISEME_COUNT, dtype=np.float64)
            fast[SILENCE_INDEX] = 1.0
            slow = fast.copy()
            trace = np.empty((frame_count, VISEME_COUNT), dtype=np.float64)
            age_seconds = 0.0
            for frame in range(frame_count):
                emitted_fast = sparsify_simplex_coordinates(fast)
                emitted_slow = sparsify_simplex_coordinates(slow)
                trace[frame] = (
                    (1.0 - EVALUATION_LIVELINESS) * emitted_slow
                    + EVALUATION_LIVELINESS * emitted_fast
                )
                target = trajectory_target(
                    control_points, winner, age_seconds
                )
                fast += alpha * (target - fast)
                slow += alpha * (fast - slow)
                age_seconds += delta_time
            equilibrium = sparsify_simplex_coordinates(
                control_points[winner, -1]
            )
            winner_amplitude = valley_prominence(trace[:, winner])
            endpoint_winner_amplitude = max(
                1e-12,
                abs(float(equilibrium[winner] - trace[0, winner])),
            )
            relative_winner = winner_amplitude / endpoint_winner_amplitude
            direction = equilibrium - trace[0]
            denominator = float(direction @ direction)
            progress = (
                (trace - trace[0]) @ direction / denominator
                if denominator > 0.0
                else np.zeros(frame_count, dtype=np.float64)
            )
            progress_amplitude = valley_prominence(progress)
            settlement_error = float(
                np.max(np.abs(trace[-1] - equilibrium), initial=0.0)
            )
            if winner_amplitude > float(worst["winnerAmplitude"]):
                worst["winnerAmplitude"] = winner_amplitude
                worst["winnerFramesPerSecond"] = frames_per_second
                worst["winner"] = VISEMES[winner]
            if relative_winner > float(worst["relativeWinnerAmplitude"]):
                worst["relativeWinnerAmplitude"] = relative_winner
                worst["relativeWinnerFramesPerSecond"] = frames_per_second
                worst["relativeWinner"] = VISEMES[winner]
            if progress_amplitude > float(worst["progressAmplitude"]):
                worst["progressAmplitude"] = progress_amplitude
                worst["progressFramesPerSecond"] = frames_per_second
                worst["progressWinner"] = VISEMES[winner]
            if settlement_error > float(worst["settlementError"]):
                worst["settlementError"] = settlement_error
                worst["settlementFramesPerSecond"] = frames_per_second
                worst["settlementWinner"] = VISEMES[winner]
    return worst


def trajectory_comparison(
    records: Sequence[OculusUtterance],
    static_table: np.ndarray,
    control_points: np.ndarray,
) -> dict[str, Any]:
    baseline = evaluate(records, static_table)
    candidate = evaluate_trajectory(records, control_points)
    render_rows: list[dict[str, Any]] = []
    for frames_per_second in RENDER_RATE_FPS:
        static_render = evaluate_render_rate_zoh(
            records, static_table, frames_per_second
        )
        dynamic_render = evaluate_trajectory_render_rate_zoh(
            records, control_points, frames_per_second
        )
        render_rows.append(
            {
                "framesPerSecond": frames_per_second,
                "static": static_render,
                "dynamic": dynamic_render,
                "mseGain": relative_gain(static_render, dynamic_render, "mse"),
                "transitionMseGain": relative_gain(
                    static_render, dynamic_render, "transitionMse"
                ),
                "derivativeMseGain": relative_gain(
                    static_render, dynamic_render, "derivativeMse"
                ),
            }
        )
    return {
        "static": baseline,
        "dynamic": candidate,
        "mseGain": relative_gain(baseline, candidate, "mse"),
        "transitionMseGain": relative_gain(
            baseline, candidate, "transitionMse"
        ),
        "derivativeMseGain": relative_gain(
            baseline, candidate, "derivativeMse"
        ),
        "temporalVariationErrorReduction": (
            abs(float(baseline["temporalVariationRatio"]) - 1.0)
            - abs(float(candidate["temporalVariationRatio"]) - 1.0)
        ),
        "heldTransitionSecondaryReversal": (
            held_transition_secondary_reversal(control_points)
        ),
        "renderRates": render_rows,
    }


def trajectory_development_gate_failures(
    comparison: dict[str, Any],
) -> list[str]:
    failures: list[str] = []
    native_thresholds = (
        ("mseGain", MIN_TRAJECTORY_DEVELOPMENT_MSE_GAIN),
        ("transitionMseGain", MIN_TRAJECTORY_DEVELOPMENT_TRANSITION_GAIN),
        ("derivativeMseGain", MIN_TRAJECTORY_DEVELOPMENT_DERIVATIVE_GAIN),
    )
    for key, minimum in native_thresholds:
        value = float(comparison.get(key, math.nan))
        if not math.isfinite(value) or value < minimum:
            failures.append(f"native {key} {value:.6f} < {minimum:.6f}")
    tv_reduction = float(
        comparison.get("temporalVariationErrorReduction", math.nan)
    )
    if not math.isfinite(tv_reduction) or tv_reduction < 0.0:
        failures.append(
            "native temporal-variation error did not improve: "
            f"{tv_reduction:.6f}"
        )
    reversal = comparison.get("heldTransitionSecondaryReversal", {})
    reversal_amplitude = float(reversal.get("winnerAmplitude", math.nan))
    relative_reversal = float(
        reversal.get("relativeWinnerAmplitude", math.nan)
    )
    progress_reversal = float(reversal.get("progressAmplitude", math.nan))
    settlement_error = float(reversal.get("settlementError", math.nan))
    if (
        not math.isfinite(reversal_amplitude)
        or reversal_amplitude > MAX_TRAJECTORY_SECONDARY_REVERSAL
    ):
        failures.append(
            "held-transition secondary reversal "
            f"{reversal_amplitude:.6f} > "
            f"{MAX_TRAJECTORY_SECONDARY_REVERSAL:.6f} "
            f"({reversal.get('winnerFramesPerSecond')} FPS, "
            f"{reversal.get('winner')})"
        )
    if (
        not math.isfinite(relative_reversal)
        or relative_reversal > MAX_TRAJECTORY_RELATIVE_WINNER_REVERSAL
    ):
        failures.append(
            "relative held-transition winner reversal "
            f"{relative_reversal:.6f} > "
            f"{MAX_TRAJECTORY_RELATIVE_WINNER_REVERSAL:.6f} "
            f"({reversal.get('relativeWinnerFramesPerSecond')} FPS, "
            f"{reversal.get('relativeWinner')})"
        )
    if (
        not math.isfinite(progress_reversal)
        or progress_reversal > MAX_TRAJECTORY_PROGRESS_REVERSAL
    ):
        failures.append(
            "held-transition progress reversal "
            f"{progress_reversal:.6f} > "
            f"{MAX_TRAJECTORY_PROGRESS_REVERSAL:.6f} "
            f"({reversal.get('progressFramesPerSecond')} FPS, "
            f"{reversal.get('progressWinner')})"
        )
    if (
        not math.isfinite(settlement_error)
        or settlement_error > MAX_TRAJECTORY_SETTLEMENT_ERROR
    ):
        failures.append(
            "held-transition settlement error "
            f"{settlement_error:.6f} > "
            f"{MAX_TRAJECTORY_SETTLEMENT_ERROR:.6f} "
            f"({reversal.get('settlementFramesPerSecond')} FPS, "
            f"{reversal.get('settlementWinner')})"
        )

    render_rows = comparison.get("renderRates")
    if not isinstance(render_rows, list) or len(render_rows) != len(RENDER_RATE_FPS):
        failures.append("trajectory render-rate audit is incomplete")
    else:
        for row, expected_fps in zip(render_rows, RENDER_RATE_FPS):
            fps = int(row.get("framesPerSecond", -1))
            if fps != expected_fps:
                failures.append(
                    f"trajectory render-rate audit expected {expected_fps}, got {fps}"
                )
                continue
            for key, minimum in (
                ("mseGain", 0.0),
                ("transitionMseGain", MIN_TRAJECTORY_DEVELOPMENT_TRANSITION_GAIN),
                ("derivativeMseGain", MIN_TRAJECTORY_DEVELOPMENT_DERIVATIVE_GAIN),
            ):
                value = float(row.get(key, math.nan))
                if not math.isfinite(value) or value < minimum:
                    failures.append(
                        f"{fps} FPS {key} {value:.6f} < {minimum:.6f}"
                    )
            static_tv_error = abs(
                float(row.get("static", {}).get(
                    "temporalVariationRatio", math.nan
                )) - 1.0
            )
            dynamic_tv_error = abs(
                float(row.get("dynamic", {}).get(
                    "temporalVariationRatio", math.nan
                )) - 1.0
            )
            if (
                not math.isfinite(static_tv_error)
                or not math.isfinite(dynamic_tv_error)
                or dynamic_tv_error
                > static_tv_error + MAX_TRAJECTORY_RENDER_TV_ERROR_RELAXATION
            ):
                failures.append(
                    f"{fps} FPS temporal-variation error {dynamic_tv_error:.6f} "
                    f"exceeds {static_tv_error:.6f} + "
                    f"{MAX_TRAJECTORY_RENDER_TV_ERROR_RELAXATION:.6f}"
                )

    static_rows = comparison.get("static", {}).get("perWinner", {})
    dynamic_rows = comparison.get("dynamic", {}).get("perWinner", {})
    for viseme in VISEMES:
        static_mse = float(static_rows.get(viseme, {}).get("mse", math.nan))
        dynamic_mse = float(dynamic_rows.get(viseme, {}).get("mse", math.nan))
        if not math.isfinite(static_mse) or not math.isfinite(dynamic_mse):
            failures.append(f"{viseme} per-winner MSE is missing or non-finite")
            continue
        allowed = static_mse * (1.0 + MAX_TRAJECTORY_PER_VISEME_MSE_REGRESSION)
        if dynamic_mse > allowed:
            failures.append(
                f"{viseme} MSE regressed by "
                f"{100.0 * (dynamic_mse / static_mse - 1.0):.3f}%"
            )
    return failures


def trajectory_candidate_objective(comparison: dict[str, Any]) -> float:
    rows = [
        (comparison["static"], comparison["dynamic"]),
        *[
            (row["static"], row["dynamic"])
            for row in comparison["renderRates"]
        ],
    ]
    qualities: list[float] = []
    for static, dynamic in rows:
        mse_ratio = float(dynamic["mse"]) / float(static["mse"])
        derivative_ratio = (
            float(dynamic["derivativeMse"])
            / float(static["derivativeMse"])
        )
        transition_ratio = (
            float(dynamic["transitionMse"])
            / float(static["transitionMse"])
        )
        static_tv_error = abs(float(static["temporalVariationRatio"]) - 1.0)
        dynamic_tv_error = abs(float(dynamic["temporalVariationRatio"]) - 1.0)
        tv_relaxation = max(0.0, dynamic_tv_error - static_tv_error)
        qualities.append(
            0.50 * mse_ratio
            + 0.25 * derivative_ratio
            + 0.25 * transition_ratio
            + 0.05
            * (tv_relaxation / MAX_TRAJECTORY_RENDER_TV_ERROR_RELAXATION) ** 2
        )
    reversal = comparison["heldTransitionSecondaryReversal"]
    winner_reversal = float(reversal["winnerAmplitude"])
    progress_reversal = float(reversal["progressAmplitude"])
    return (
        max(qualities)
        + 0.25 * float(np.mean(qualities))
        + 0.05
        * (winner_reversal / MAX_TRAJECTORY_SECONDARY_REVERSAL) ** 2
        + 0.05
        * (progress_reversal / MAX_TRAJECTORY_PROGRESS_REVERSAL) ** 2
    )


def _build_rejected_age_trajectory_experiment(
    source: dict[str, Any],
    processed: dict[str, Any],
    audio: dict[str, Any],
    records: Sequence[OculusUtterance],
    extraction: ExtractionAudit,
) -> dict[str, Any]:
    fit_records = records_for_split(records, "fit")
    development_records = records_for_split(records, "development")
    heldout_records = records_for_split(records, "heldout")
    selected_candidate, fit_counts, cardinality_reports = select_cardinality_and_strength(
        fit_records, development_records
    )

    final_dense_centers, final_counts = fit_conditional_barycenters(
        list(fit_records) + list(development_records)
    )
    final_centers = project_conditional_barycenters_top_k(
        final_dense_centers, selected_candidate.top_k
    )
    selected_strength = selected_candidate.halo_strength
    final_table = halo_table(final_centers, selected_strength)
    identity_table = halo_table(final_centers, 0.0)
    full_halo_table = halo_table(final_centers, 1.0)
    development_static_table = halo_table(
        selected_candidate.centers, selected_candidate.halo_strength
    )
    fit_normal_equations = trajectory_combined_normal_equations(fit_records)
    trajectory_candidate_reports: list[dict[str, Any]] = []
    accepted_trajectory_candidates: list[
        tuple[
            float,
            float,
            np.ndarray,
            dict[str, Any],
            dict[str, Any],
            float,
            int,
        ]
    ] = []
    candidate_index = 0
    for curvature_ratio in TRAJECTORY_CURVATURE_RATIO_CANDIDATES:
        terminal_multipliers = (
            (0.0,)
            if curvature_ratio == 0.0
            else TRAJECTORY_TERMINAL_RATIO_MULTIPLIERS
        )
        for terminal_multiplier in terminal_multipliers:
            terminal_ratio = curvature_ratio * terminal_multiplier
            candidate_controls, candidate_diagnostics = (
                fit_trajectory_control_points(
                    fit_records,
                    development_static_table,
                    curvature_ratio,
                    terminal_ratio,
                    fit_normal_equations,
                )
            )
            candidate_comparison = trajectory_comparison(
                development_records,
                development_static_table,
                candidate_controls,
            )
            candidate_failures = trajectory_development_gate_failures(
                candidate_comparison
            )
            candidate_objective = trajectory_candidate_objective(
                candidate_comparison
            )
            trajectory_candidate_reports.append(
                {
                    "candidateIndex": candidate_index,
                    "curvatureRatio": curvature_ratio,
                    "terminalSlopeRatio": terminal_ratio,
                    "objective": candidate_objective,
                    "mseGain": candidate_comparison["mseGain"],
                    "transitionMseGain": candidate_comparison[
                        "transitionMseGain"
                    ],
                    "derivativeMseGain": candidate_comparison[
                        "derivativeMseGain"
                    ],
                    "temporalVariationErrorReduction": candidate_comparison[
                        "temporalVariationErrorReduction"
                    ],
                    "heldTransitionSecondaryReversal": candidate_comparison[
                        "heldTransitionSecondaryReversal"
                    ],
                    "renderRateGains": [
                        {
                            "framesPerSecond": row["framesPerSecond"],
                            "mseGain": row["mseGain"],
                            "transitionMseGain": row["transitionMseGain"],
                            "derivativeMseGain": row["derivativeMseGain"],
                        }
                        for row in candidate_comparison["renderRates"]
                    ],
                    "passed": not candidate_failures,
                    "failures": candidate_failures,
                }
            )
            print(
                "Trajectory candidate "
                f"k={curvature_ratio:g}, terminal={terminal_ratio:g}: "
                f"mse={100.0 * float(candidate_comparison['mseGain']):+.2f}%, "
                f"d1={100.0 * float(candidate_comparison['derivativeMseGain']):+.2f}%, "
                f"reversal={float(candidate_comparison['heldTransitionSecondaryReversal']['winnerAmplitude']):.4f}, "
                + ("PASS" if not candidate_failures else "reject"),
                flush=True,
            )
            if not candidate_failures:
                accepted_trajectory_candidates.append(
                    (
                        curvature_ratio,
                        terminal_ratio,
                        candidate_controls,
                        candidate_diagnostics,
                        candidate_comparison,
                        candidate_objective,
                        candidate_index,
                    )
                )
            candidate_index += 1
    if not accepted_trajectory_candidates:
        raise ValueError(
            "Age-conditioned trajectory failed its development gate: "
            + " | ".join(
                f"curvature={report['curvatureRatio']}, "
                f"terminal={report['terminalSlopeRatio']}: "
                + "; ".join(report["failures"])
                for report in trajectory_candidate_reports
            )
        )
    (
        selected_curvature_ratio,
        selected_terminal_ratio,
        development_control_points,
        development_fit_diagnostics,
        development_trajectory,
        _,
        _,
    ) = min(
        accepted_trajectory_candidates,
        key=lambda candidate: (
            candidate[5],
            float(candidate[4]["heldTransitionSecondaryReversal"][
                "winnerAmplitude"
            ]),
            -candidate[0],
            -candidate[1],
            candidate[6],
        ),
    )
    final_normal_equations = trajectory_combined_normal_equations(
        list(fit_records) + list(development_records)
    )
    final_control_points, final_fit_diagnostics = fit_trajectory_control_points(
        list(fit_records) + list(development_records),
        final_table,
        selected_curvature_ratio,
        selected_terminal_ratio,
        final_normal_equations,
    )
    heldout_trajectory = trajectory_comparison(
        heldout_records, final_table, final_control_points
    )
    dense_development_report = next(
        report for report in cardinality_reports if int(report["topK"]) == DENSE_TOP_K
    )
    dense_table = halo_table(
        project_conditional_barycenters_top_k(final_dense_centers, DENSE_TOP_K),
        float(dense_development_report["selectedHaloStrength"]),
    )
    development_render_sweep = render_rate_sweep(
        development_records, identity_table, final_table, dense_table
    )
    if not development_render_sweep["passed"]:
        raise ValueError(
            "Selected sparse halo failed the development render-rate quality gate: "
            + "; ".join(development_render_sweep["failures"])
        )
    heldout_identity = evaluate(heldout_records, identity_table)
    heldout_model = evaluate(heldout_records, final_table)
    heldout_full = evaluate(heldout_records, full_halo_table)
    heldout_objective = objective_metrics(
        heldout_model, float(heldout_identity["mse"])
    )
    heldout_render_sweep = render_rate_sweep(
        heldout_records, identity_table, final_table, dense_table
    )
    selected_development_report = next(
        report
        for report in cardinality_reports
        if int(report["topK"]) == selected_candidate.top_k
    )
    heldout_cardinalities: list[dict[str, Any]] = []
    for report in cardinality_reports:
        top_k = int(report["topK"])
        strength = float(report["selectedHaloStrength"])
        centers = project_conditional_barycenters_top_k(final_dense_centers, top_k)
        table = halo_table(centers, strength)
        metrics = evaluate(heldout_records, table)
        heldout_cardinalities.append(
            {
                "topK": top_k,
                "haloStrength": strength,
                "metrics": metrics,
                "objective": objective_metrics(
                    metrics, float(heldout_identity["mse"])
                ),
                "support": support_statistics(table),
            }
        )

    document: dict[str, Any] = {
        "schemaVersion": 2,
        "modelVersion": MODEL_VERSION,
        "contentSha256": None,
        "tableSha256": None,
        "trajectorySha256": None,
        "description": (
            "Causal winner-age-conditioned Oculus continuous-viseme trajectory halo. "
            "The cubic control poses are avatar-portable probability simplexes and "
            "contain no audio."
        ),
        "visemeOrder": list(VISEMES),
        "provenance": {
            "dataset": source["dataset"],
            "subset": source["subset"],
            "processedSelectionContentSha256": processed["selectionContentSha256"],
            "audioSelectionContentSha256": audio["selectionContentSha256"],
            "audioArchiveSha256": ovr_source.EXPECTED_AUDIO_ARCHIVE_SHA256,
            "trainerSha256": sha256_file(Path(__file__).resolve()),
            "reusedOvrTrainerSha256": sha256_file(
                Path(ovr_source.__file__).resolve()
            ),
            "reusedCorpusTrainerSha256": sha256_file(
                Path(corpus.__file__).resolve()
            ),
            "ovrLipSyncDllSha256": EXPECTED_OVR_DLL_SHA256,
            "ovrLipSyncVersion": extraction.dll_version,
            "continuousSequenceSha256": extraction.continuous_sequence_sha256,
            "dominantSequenceSha256": extraction.dominant_sequence_sha256,
            "frameDelayMsCounts": extraction.frame_delay_ms_counts,
            "licenseNotice": (
                "Derived from the SPIRE EMA Corpus paired audio, CC BY 4.0; "
                "Bandekar, Udupa, and Ghosh (2024)."
            ),
        },
        "analysis": {
            "sourceAudio": "16 kHz mono PCM16",
            "resampling": "deterministic NumPy linear interpolation 16 kHz -> 48 kHz",
            "sampleRateHz": ovr_source.ANALYSIS_SAMPLE_RATE,
            "bufferSamples": ovr_source.ANALYSIS_BUFFER_SAMPLES,
            "bufferSeconds": (
                ovr_source.ANALYSIS_BUFFER_SAMPLES
                / ovr_source.ANALYSIS_SAMPLE_RATE
            ),
            "provider": "Enhanced",
            "providerValue": ovr_source.OVR_PROVIDER_ENHANCED,
            "visemeSmoothing": ovr_source.OVR_SMOOTHING,
            "lastBuffer": "zero padded to one complete 1024-sample native analysis block",
            "winner": "NumPy argmax of the copied native 15-weight frame; ties choose the first index",
            "target": "continuousWeights / sum(continuousWeights)",
            "rawWeightMass": {
                split: mass_statistics(records_for_split(records, split))
                for split in EXPECTED_UTTERANCE_COUNTS
            },
        },
        "training": {
            "splitPolicy": (
                "Fit, development, and heldout remain speaker- and sentence-disjoint "
                "according to source_manifest.json; no frame-level random split is used."
            ),
            "splits": {
                split: split_summary(records_for_split(records, split))
                for split in EXPECTED_UTTERANCE_COUNTS
            },
            "fit": (
                "C[j] is the unweighted conditional barycenter of normalized native "
                "Oculus shapes whose hard winner is j. B(h)=(1-h)I+hC. The age "
                "trajectory is then fitted end-to-end through the deployed two-pole "
                "observer with pose and derivative normal equations."
            ),
            "silenceConstraint": "B[0] is forced bit-exact to [1,0,...,0].",
            "observer": {
                "responseSeconds": OBSERVER_RESPONSE_SECONDS,
                "liveliness": EVALUATION_LIVELINESS,
                "livelinessDerivation": {
                    "defaultSpeechLiveliness": DEFAULT_SPEECH_LIVELINESS,
                    "maximumSpeechLivelinessLead": MAXIMUM_SPEECH_LIVELINESS_LEAD,
                    "speechRenderLead": render_lead_derivation(),
                },
                "clockSeconds": (
                    ovr_source.ANALYSIS_BUFFER_SAMPLES
                    / ovr_source.ANALYSIS_SAMPLE_RATE
                ),
                "formula": (
                    "emitFast=S(fast); emitSlow=S(slow); output=(1-r)*emitSlow+r*emitFast; "
                    "a=1-exp(-dt/tau); fast+=a*(B[winner]-fast); slow+=a*(fast-slow)"
                ),
                "sparseEmission": {
                    "epsilon": SIMPLEX_CULLING_EPSILON,
                    "formula": (
                        "S(x)=0 when x<=epsilon, otherwise "
                        "S(x)=(x-epsilon)/(1-epsilon) on sanitized [0,1]"
                    ),
                    "feedback": "S is applied only to emitted fast/slow copies, never to observer recurrence.",
                },
                "animatorPublicationPhase": (
                    "Decoder runs before Math: visible sparse emissions observe the previous "
                    "exact fast/slow state, then Math advances recurrence from the current decoded row."
                ),
                "selectionOutput": (
                    "The fitted objective scores the public sparse simplex. Articulation "
                    "projection separately consumes the unsparsified filtered halo and is "
                    "not substituted for the public-simplex target in this trainer."
                ),
                "reset": "fast and slow begin at exact one-hot silence for every utterance",
            },
            "selectionObjective": {
                "formula": (
                    "J(h)=MSE(h)/MSE(identity)+8*max(0,0.90-TV(h)/TV(teacher))^2"
                ),
                "mse": "mean squared error over every normalized frame and all 15 channels",
                "temporalVariation": (
                    "mean absolute first difference over all within-utterance frame edges "
                    "and all 15 channels; utterance boundaries are excluded"
                ),
                "candidateStrengths": list(HALO_STRENGTH_CANDIDATES),
                "tieBreak": "smaller h (less halo and more responsiveness)",
                "fitFrameCounts": {
                    VISEMES[index]: int(fit_counts[index])
                    for index in range(VISEME_COUNT)
                },
                "topKCandidates": list(TOP_K_CANDIDATES),
                "sparseProjection": (
                    "For each row, retain the k largest conditional-barycenter coordinates, "
                    "then apply the exact Euclidean projection of that retained vector onto "
                    "the unit simplex. The shared projection offset, not proportional "
                    "renormalization, is the MSE-optimal fixed-support solution."
                ),
                "cardinalityAcceptance": {
                    "overallMseGainRetentionVersusDense": MIN_OVERALL_GAIN_RETENTION,
                    "fourBlockTransitionMseGainRetentionVersusDense": MIN_TRANSITION_GAIN_RETENTION,
                    "velocityMseGainRetentionVersusDense": MIN_VELOCITY_GAIN_RETENTION,
                    "mustNotRegressVersusIdentity": [
                        "mse",
                        "transitionMse",
                        "derivativeMse",
                    ],
                    "temporalVariationPolicy": (
                        "No separate hard TV gate; the TV shortfall is already penalized "
                        "inside J(h)."
                    ),
                    "selection": "smallest accepted k, then smaller h",
                },
                "cardinalityDevelopmentReports": cardinality_reports,
                "cardinalitySelection": {
                    "selectedTopK": selected_candidate.top_k,
                    "selectedCandidatePassedAllSparseGates": bool(
                        selected_development_report["acceptedForShipping"]
                    ),
                    "denseFallbackUsed": not bool(
                        selected_development_report["acceptedForShipping"]
                    ),
                    "reason": (
                        "smallest cardinality passing every development gate"
                        if selected_development_report["acceptedForShipping"]
                        else "no cardinality passed every development gate; retained dense teacher table"
                    ),
                },
                "selectedHaloStrength": selected_strength,
                "selectedTopK": selected_candidate.top_k,
                "canonicalReferenceHaloStrength": CANONICAL_REFERENCE_HALO_STRENGTH,
                "canonicalReferenceTopK": CANONICAL_REFERENCE_TOP_K,
                "matchesCanonicalReference": math.isclose(
                    selected_strength,
                    CANONICAL_REFERENCE_HALO_STRENGTH,
                    rel_tol=0.0,
                    abs_tol=1e-12,
                ) and selected_candidate.top_k == CANONICAL_REFERENCE_TOP_K,
            },
            "ageConditionedTrajectory": {
                "basis": "single cubic Bernstein/Bezier segment",
                "durationBlocks": TRAJECTORY_DURATION_BLOCKS,
                "durationSeconds": TRAJECTORY_DURATION_SECONDS,
                "controlPointCount": TRAJECTORY_CONTROL_POINT_COUNT,
                "causalFeatures": ["hardWinner", "elapsedWinnerRunTime"],
                "simplexGuarantee": (
                    "Every control pose is a nonnegative unit simplex and Bernstein "
                    "basis weights are nonnegative and sum to one."
                ),
                "shapeConstraint": (
                    "Control-polygon second differences are quadratically "
                    "regularized; every candidate must also pass an exact "
                    "multi-rate held-transition secondary-reversal bound."
                ),
                "support": (
                    "Every control pose reuses its selected static TopK support."
                ),
                "longHoldEndpoint": (
                    "The final control pose is bit-identical to the reviewed static halo."
                ),
                "developmentFit": development_fit_diagnostics,
                "finalFit": final_fit_diagnostics,
                "selectedCurvatureRatio": selected_curvature_ratio,
                "selectedTerminalSlopeRatio": selected_terminal_ratio,
                "curvatureCandidateReports": trajectory_candidate_reports,
                "developmentGate": {
                    "minimumMseGain": MIN_TRAJECTORY_DEVELOPMENT_MSE_GAIN,
                    "minimumDerivativeMseGain": (
                        MIN_TRAJECTORY_DEVELOPMENT_DERIVATIVE_GAIN
                    ),
                    "minimumTransitionMseGain": (
                        MIN_TRAJECTORY_DEVELOPMENT_TRANSITION_GAIN
                    ),
                    "minimumPerRateMseGain": 0.0,
                    "maximumRenderTemporalVariationErrorRelaxation": (
                        MAX_TRAJECTORY_RENDER_TV_ERROR_RELAXATION
                    ),
                    "maximumPerVisemeMseRegression": (
                        MAX_TRAJECTORY_PER_VISEME_MSE_REGRESSION
                    ),
                    "maximumHeldTransitionSecondaryReversal": (
                        MAX_TRAJECTORY_SECONDARY_REVERSAL
                    ),
                    "maximumRelativeWinnerReversal": (
                        MAX_TRAJECTORY_RELATIVE_WINNER_REVERSAL
                    ),
                    "maximumProgressReversal": (
                        MAX_TRAJECTORY_PROGRESS_REVERSAL
                    ),
                    "maximumSettlementError": (
                        MAX_TRAJECTORY_SETTLEMENT_ERROR
                    ),
                    "passed": True,
                },
            },
        },
        "model": {
            "orientation": "weights[hardWinner][outputViseme]",
            "selectedHaloStrength": selected_strength,
            "topK": selected_candidate.top_k,
            "conditionalBarycenterWeights": final_centers.astype(np.float32).tolist(),
            "weights": final_table.tolist(),
            "trajectoryOrientation": (
                "trajectoryControlPoints[hardWinner][controlPoint][outputViseme]"
            ),
            "trajectoryDurationSeconds": TRAJECTORY_DURATION_SECONDS,
            "trajectoryControlPointCount": TRAJECTORY_CONTROL_POINT_COUNT,
            "trajectoryControlPoints": final_control_points.astype(
                np.float32
            ).tolist(),
            "fitPlusDevelopmentFrameCounts": {
                VISEMES[index]: int(final_counts[index])
                for index in range(VISEME_COUNT)
            },
            "invariants": {
                "nonnegative": True,
                "rowStochasticTolerance": TABLE_SUM_TOLERANCE,
                "silenceRowBitExact": True,
                "ownWinnerIsUniqueRowMaximum": True,
                "support": support_statistics(final_table),
            },
        },
        "evaluation": {
            "developmentRenderRateQualityGate": development_render_sweep,
            "heldoutIdentity": heldout_identity,
            "heldoutSelectedHalo": {
                **heldout_model,
                **heldout_objective,
                "mseImprovementPercent": 100.0
                * (
                    1.0
                    - float(heldout_model["mse"])
                    / float(heldout_identity["mse"])
                ),
            },
            "heldoutSelectedTopKAtFullHaloStrength": heldout_full,
            "heldoutCardinalityReports": heldout_cardinalities,
            "heldoutRenderRateReport": heldout_render_sweep,
            "developmentAgeConditionedTrajectory": development_trajectory,
            "heldoutAgeConditionedTrajectory": heldout_trajectory,
            "heldoutUse": (
                "Reported once after development selection; it is not used to choose h "
                "or modify coefficients."
            ),
        },
        "limitations": [
            "The trajectory estimates a causal conditional mean; it cannot recover phone identity discarded by the hard winner.",
            "The first trajectory model uses current winner and elapsed run time only. It contains no future-phone, face-tracking, or runtime audio feature.",
            "The selected SPIRE subset is English and does not establish universal language or accent coverage.",
            "Oculus smoothing is already present in the teacher; selection is therefore evaluated after the exact additional avatar observer.",
            "Raw native weight mass is diagnostic only and must not be presented as a recovered VRChat parameter.",
        ],
    }
    document["tableSha256"] = corpus.canonical_sha256(table_hash_input(document))
    document["trajectorySha256"] = corpus.canonical_sha256(
        trajectory_hash_input(document)
    )
    document["contentSha256"] = corpus.canonical_sha256(audit_hash_input(document))
    _validate_rejected_age_trajectory_document(document)
    return document


def _validate_rejected_age_trajectory_document(document: dict[str, Any]) -> None:
    if document.get("schemaVersion") != 2 or document.get("modelVersion") != MODEL_VERSION:
        raise ValueError("Unsupported Oculus halo document version")
    if document.get("visemeOrder") != list(VISEMES):
        raise ValueError("Oculus halo viseme order differs from runtime")
    model = document.get("model")
    if not isinstance(model, dict):
        raise ValueError("Oculus halo document has no model")
    strength = float(model.get("selectedHaloStrength", math.nan))
    if strength not in HALO_STRENGTH_CANDIDATES:
        raise ValueError("Selected Oculus halo strength is not in the reviewed grid")
    top_k = int(model.get("topK", -1))
    if top_k not in TOP_K_CANDIDATES:
        raise ValueError("Selected Oculus halo cardinality is not in the reviewed set")
    table = np.asarray(model.get("weights"), dtype=np.float32)
    validate_table(table)
    if int(model.get("trajectoryControlPointCount", -1)) != (
        TRAJECTORY_CONTROL_POINT_COUNT
    ):
        raise ValueError("Oculus trajectory control-point count differs from runtime")
    if not math.isclose(
        float(model.get("trajectoryDurationSeconds", math.nan)),
        TRAJECTORY_DURATION_SECONDS,
        rel_tol=0.0,
        abs_tol=1e-12,
    ):
        raise ValueError("Oculus trajectory duration differs from runtime")
    trajectory = np.asarray(model.get("trajectoryControlPoints"), dtype=np.float32)
    validate_trajectory_control_points(trajectory.astype(np.float64), table)
    trajectory_training = document.get("training", {}).get(
        "ageConditionedTrajectory", {}
    )
    selected_curvature = float(
        trajectory_training.get("selectedCurvatureRatio", math.nan)
    )
    if selected_curvature not in TRAJECTORY_CURVATURE_RATIO_CANDIDATES:
        raise ValueError("Oculus trajectory curvature ratio is outside its grid")
    selected_terminal = float(
        trajectory_training.get("selectedTerminalSlopeRatio", math.nan)
    )
    if selected_terminal not in {
        selected_curvature * multiplier
        for multiplier in TRAJECTORY_TERMINAL_RATIO_MULTIPLIERS
    }:
        raise ValueError("Oculus trajectory terminal ratio is outside its grid")
    support = np.count_nonzero(table > 0.0, axis=1)
    if np.any(support[1:] > top_k):
        raise ValueError("Oculus halo table exceeds its declared TopK support")
    if top_k < VISEME_COUNT and np.any(support[1:] != top_k):
        raise ValueError("Sparse Oculus halo table did not retain exactly TopK coordinates")
    expected_table_hash = corpus.canonical_sha256(table_hash_input(document))
    if document.get("tableSha256") != expected_table_hash:
        raise ValueError("Oculus halo table hash mismatch")
    expected_trajectory_hash = corpus.canonical_sha256(
        trajectory_hash_input(document)
    )
    if document.get("trajectorySha256") != expected_trajectory_hash:
        raise ValueError("Oculus trajectory hash mismatch")
    expected_content_hash = corpus.canonical_sha256(audit_hash_input(document))
    if document.get("contentSha256") != expected_content_hash:
        raise ValueError("Oculus halo audit content hash mismatch")
    for section in ("heldoutIdentity", "heldoutSelectedHalo"):
        metrics = document.get("evaluation", {}).get(section)
        if not isinstance(metrics, dict):
            raise ValueError(f"Missing {section} evaluation")
        for key in ("mse", "rmse", "brier", "top1Agreement"):
            if not math.isfinite(float(metrics.get(key, math.nan))):
                raise ValueError(f"{section}.{key} must be finite")
    development_trajectory = document.get("evaluation", {}).get(
        "developmentAgeConditionedTrajectory"
    )
    if not isinstance(development_trajectory, dict):
        raise ValueError("Missing development trajectory evaluation")
    trajectory_failures = trajectory_development_gate_failures(
        development_trajectory
    )
    if trajectory_failures:
        raise ValueError(
            "Oculus trajectory development audit failed: "
            + "; ".join(trajectory_failures)
        )


def float_literal(value: float) -> str:
    result = np.float32(value)
    if not np.isfinite(result):
        raise ValueError("Cannot emit a non-finite C# coefficient")
    if result == 0.0:
        result = np.float32(0.0)
    return f"{result:.9f}f"


def _generate_rejected_age_trajectory_csharp(
    document: dict[str, Any], model_path: Path
) -> None:
    _validate_rejected_age_trajectory_document(document)
    table = np.asarray(document["model"]["weights"], dtype=np.float32)
    trajectory = np.asarray(
        document["model"]["trajectoryControlPoints"], dtype=np.float32
    )
    lines = [
        "// <auto-generated>",
        "// Trained by Tools/AdvancedVisemeTraining/train_oculus_viseme_halo.py.",
        "// Source: SPIRE EMA Corpus paired audio, CC BY 4.0, pinned revision 55f21628de95514e3ff22eaccc75e1547d181297.",
        "// Bandekar, Udupa, and Ghosh (2024), doi:10.21437/Interspeech.2024-1756.",
        "// Compact normalized shape trajectories only; no source audio or per-frame trace is embedded.",
        "// </auto-generated>",
        "using System;",
        "",
        "namespace YUCP.Components",
        "{",
        "    public static class AdvancedVisemeOculusHalo",
        "    {",
        f"        public const int ModelVersion = {MODEL_VERSION};",
        f"        public const int VisemeCount = {VISEME_COUNT};",
        f"        public const int TopK = {int(document['model']['topK'])};",
        f"        public const int TrajectoryControlPointCount = {TRAJECTORY_CONTROL_POINT_COUNT};",
        f"        public const string ContentSha256 = \"{document['contentSha256']}\";",
        f"        public const string TableSha256 = \"{document['tableSha256']}\";",
        f"        public const string TrajectorySha256 = \"{document['trajectorySha256']}\";",
        f"        public const float ObserverResponseSeconds = {float_literal(OBSERVER_RESPONSE_SECONDS)};",
        f"        public const float EvaluationLiveliness = {float_literal(EVALUATION_LIVELINESS)};",
        f"        public const float HaloStrength = {float_literal(document['model']['selectedHaloStrength'])};",
        f"        public const float TrajectoryDurationSeconds = {float_literal(TRAJECTORY_DURATION_SECONDS)};",
        "",
        "        // Flattened as [hard winner, output viseme].",
        "        private static readonly float[] WeightValues =",
        "        {",
    ]
    for winner, row in enumerate(table):
        lines.append(f"            // {VISEMES[winner]}")
        lines.append("            " + ", ".join(float_literal(value) for value in row) + ",")
    lines.extend(
        [
            "        };",
            "",
            "        // Flattened as [hard winner, Bezier control point, output viseme].",
            "        private static readonly float[] TrajectoryControlPointValues =",
            "        {",
        ]
    )
    for winner, controls in enumerate(trajectory):
        lines.append(f"            // {VISEMES[winner]}")
        for control, row in enumerate(controls):
            lines.append(
                f"            // control {control}\n            "
                + ", ".join(float_literal(value) for value in row)
                + ","
            )
    lines.extend(
        [
            "        };",
            "",
            "        public static float Weight(int hardWinner, int outputViseme)",
            "        {",
            "            RequireIndex(hardWinner, nameof(hardWinner));",
            "            RequireIndex(outputViseme, nameof(outputViseme));",
            "            return WeightValues[hardWinner * VisemeCount + outputViseme];",
            "        }",
            "",
            "        public static float TrajectoryControlPointWeight(",
            "            int hardWinner, int controlPoint, int outputViseme)",
            "        {",
            "            RequireIndex(hardWinner, nameof(hardWinner));",
            "            RequireIndex(outputViseme, nameof(outputViseme));",
            "            if ((uint)controlPoint >= TrajectoryControlPointCount)",
            "                throw new ArgumentOutOfRangeException(nameof(controlPoint));",
            "            return TrajectoryControlPointValues[",
            "                (hardWinner * TrajectoryControlPointCount + controlPoint) *",
            "                VisemeCount + outputViseme];",
            "        }",
            "",
            "        public static float TrajectoryWeight(",
            "            int hardWinner, int outputViseme, float elapsedSeconds)",
            "        {",
            "            RequireIndex(hardWinner, nameof(hardWinner));",
            "            RequireIndex(outputViseme, nameof(outputViseme));",
            "            if (hardWinner == 0)",
            "                return outputViseme == 0 ? 1f : 0f;",
            "            var phase = elapsedSeconds <= 0f ? 0f :",
            "                elapsedSeconds >= TrajectoryDurationSeconds ? 1f :",
            "                elapsedSeconds / TrajectoryDurationSeconds;",
            "            var inverse = 1f - phase;",
            "            var b0 = inverse * inverse * inverse;",
            "            var b1 = 3f * inverse * inverse * phase;",
            "            var b2 = 3f * inverse * phase * phase;",
            "            var b3 = phase * phase * phase;",
            "            return",
            "                b0 * TrajectoryControlPointWeight(hardWinner, 0, outputViseme) +",
            "                b1 * TrajectoryControlPointWeight(hardWinner, 1, outputViseme) +",
            "                b2 * TrajectoryControlPointWeight(hardWinner, 2, outputViseme) +",
            "                b3 * TrajectoryControlPointWeight(hardWinner, 3, outputViseme);",
            "        }",
            "",
            "        public static bool HasDynamicTrajectory(int hardWinner)",
            "        {",
            "            RequireIndex(hardWinner, nameof(hardWinner));",
            "            for (var output = 0; output < VisemeCount; output++)",
            "            {",
            "                var first = TrajectoryControlPointWeight(hardWinner, 0, output);",
            "                for (var control = 1; control < TrajectoryControlPointCount; control++)",
            "                    if (TrajectoryControlPointWeight(hardWinner, control, output) != first)",
            "                        return true;",
            "            }",
            "            return false;",
            "        }",
            "",
            "        private static void RequireIndex(int value, string name)",
            "        {",
            "            if ((uint)value >= VisemeCount) throw new ArgumentOutOfRangeException(name);",
            "        }",
            "    }",
            "}",
            "",
        ]
    )
    corpus.write_text_atomic(model_path, "\n".join(lines))


def build_document(
    source: dict[str, Any],
    processed: dict[str, Any],
    audio: dict[str, Any],
    records: Sequence[OculusUtterance],
    extraction: ExtractionAudit,
) -> dict[str, Any]:
    """Fit the shipping static target table and its held-out audit.

    Time evolution intentionally remains outside this model.  At runtime every
    hard winner selects one simplex row and the existing shared two-pole
    observer approaches it from its live state without resetting on an edge.
    """

    fit_records = records_for_split(records, "fit")
    development_records = records_for_split(records, "development")
    heldout_records = records_for_split(records, "heldout")
    selected_candidate, fit_counts, cardinality_reports = (
        select_cardinality_and_strength(fit_records, development_records)
    )

    # Selection uses fit -> development only.  Once frozen, the published
    # barycenters are refitted on fit+development and heldout remains untouched.
    fit_dense_centers, _ = fit_conditional_barycenters(fit_records)
    final_dense_centers, final_counts = fit_conditional_barycenters(
        list(fit_records) + list(development_records)
    )
    selected_strength = selected_candidate.halo_strength
    final_centers = project_conditional_barycenters_top_k(
        final_dense_centers, selected_candidate.top_k
    )
    final_table = halo_table(final_centers, selected_strength)
    identity_table = halo_table(final_centers, 0.0)
    full_halo_table = halo_table(final_centers, 1.0)

    dense_development_report = next(
        report
        for report in cardinality_reports
        if int(report["topK"]) == DENSE_TOP_K
    )
    dense_strength = float(dense_development_report["selectedHaloStrength"])
    development_identity_table = halo_table(selected_candidate.centers, 0.0)
    development_selected_table = halo_table(
        selected_candidate.centers, selected_strength
    )
    development_dense_table = halo_table(
        project_conditional_barycenters_top_k(fit_dense_centers, DENSE_TOP_K),
        dense_strength,
    )
    development_render_sweep = render_rate_sweep(
        development_records,
        development_identity_table,
        development_selected_table,
        development_dense_table,
    )
    if not development_render_sweep["passed"]:
        raise ValueError(
            "Selected sparse halo failed the development render-rate quality gate: "
            + "; ".join(development_render_sweep["failures"])
        )

    final_dense_table = halo_table(
        project_conditional_barycenters_top_k(final_dense_centers, DENSE_TOP_K),
        dense_strength,
    )
    heldout_identity = evaluate(heldout_records, identity_table)
    heldout_model = evaluate(heldout_records, final_table)
    heldout_full = evaluate(heldout_records, full_halo_table)
    heldout_objective = objective_metrics(
        heldout_model, float(heldout_identity["mse"])
    )
    heldout_render_sweep = render_rate_sweep(
        heldout_records, identity_table, final_table, final_dense_table
    )
    selected_development_report = next(
        report
        for report in cardinality_reports
        if int(report["topK"]) == selected_candidate.top_k
    )
    heldout_cardinalities: list[dict[str, Any]] = []
    for report in cardinality_reports:
        top_k = int(report["topK"])
        strength = float(report["selectedHaloStrength"])
        centers = project_conditional_barycenters_top_k(
            final_dense_centers, top_k
        )
        table = halo_table(centers, strength)
        metrics = evaluate(heldout_records, table)
        heldout_cardinalities.append(
            {
                "topK": top_k,
                "haloStrength": strength,
                "metrics": metrics,
                "objective": objective_metrics(
                    metrics, float(heldout_identity["mse"])
                ),
                "support": support_statistics(table),
            }
        )

    document: dict[str, Any] = {
        "schemaVersion": 3,
        "modelVersion": MODEL_VERSION,
        "contentSha256": None,
        "tableSha256": None,
        "description": (
            "Sparse conditional Oculus continuous-viseme shape target. The "
            "runtime shared interruptible observer owns all time evolution."
        ),
        "visemeOrder": list(VISEMES),
        "provenance": {
            "dataset": source["dataset"],
            "subset": source["subset"],
            "processedSelectionContentSha256": processed[
                "selectionContentSha256"
            ],
            "audioSelectionContentSha256": audio["selectionContentSha256"],
            "audioArchiveSha256": ovr_source.EXPECTED_AUDIO_ARCHIVE_SHA256,
            "trainerSha256": sha256_file(Path(__file__).resolve()),
            "reusedOvrTrainerSha256": sha256_file(
                Path(ovr_source.__file__).resolve()
            ),
            "reusedCorpusTrainerSha256": sha256_file(
                Path(corpus.__file__).resolve()
            ),
            "ovrLipSyncDllSha256": EXPECTED_OVR_DLL_SHA256,
            "ovrLipSyncVersion": extraction.dll_version,
            "continuousSequenceSha256": extraction.continuous_sequence_sha256,
            "dominantSequenceSha256": extraction.dominant_sequence_sha256,
            "frameDelayMsCounts": extraction.frame_delay_ms_counts,
            "licenseNotice": (
                "Derived from the SPIRE EMA Corpus paired audio, CC BY 4.0; "
                "Bandekar, Udupa, and Ghosh (2024)."
            ),
        },
        "analysis": {
            "sourceAudio": "16 kHz mono PCM16",
            "resampling": "deterministic NumPy linear interpolation 16 kHz -> 48 kHz",
            "sampleRateHz": ovr_source.ANALYSIS_SAMPLE_RATE,
            "bufferSamples": ovr_source.ANALYSIS_BUFFER_SAMPLES,
            "bufferSeconds": (
                ovr_source.ANALYSIS_BUFFER_SAMPLES
                / ovr_source.ANALYSIS_SAMPLE_RATE
            ),
            "provider": "Enhanced",
            "providerValue": ovr_source.OVR_PROVIDER_ENHANCED,
            "visemeSmoothing": ovr_source.OVR_SMOOTHING,
            "lastBuffer": (
                "zero padded to one complete 1024-sample native analysis block"
            ),
            "winner": (
                "NumPy argmax of the copied native 15-weight frame; ties choose "
                "the first index"
            ),
            "target": "continuousWeights / sum(continuousWeights)",
            "rawWeightMass": {
                split: mass_statistics(records_for_split(records, split))
                for split in EXPECTED_UTTERANCE_COUNTS
            },
        },
        "training": {
            "splitPolicy": (
                "Fit, development, and heldout remain speaker- and "
                "sentence-disjoint according to source_manifest.json."
            ),
            "splits": {
                split: split_summary(records_for_split(records, split))
                for split in EXPECTED_UTTERANCE_COUNTS
            },
            "fit": (
                "C[j] is the unweighted conditional barycenter of normalized "
                "native Oculus shapes whose hard winner is j. B(h)=(1-h)I+hC."
            ),
            "silenceConstraint": "B[0] is forced bit-exact to [1,0,...,0].",
            "observer": {
                "responseSeconds": OBSERVER_RESPONSE_SECONDS,
                "liveliness": EVALUATION_LIVELINESS,
                "livelinessDerivation": {
                    "defaultSpeechLiveliness": DEFAULT_SPEECH_LIVELINESS,
                    "maximumSpeechLivelinessLead": (
                        MAXIMUM_SPEECH_LIVELINESS_LEAD
                    ),
                    "speechRenderLead": render_lead_derivation(),
                },
                "formula": (
                    "emitFast=S(fast); emitSlow=S(slow); "
                    "output=(1-r)*emitSlow+r*emitFast; "
                    "a=1-exp(-dt/tau); fast+=a*(B[winner]-fast); "
                    "slow+=a*(fast-slow)"
                ),
                "interruption": (
                    "A hard-winner edge changes only B[winner]. Fast and slow "
                    "remain live and are never reset by a viseme transition."
                ),
                "tracking": (
                    "The render lead is multiplied by 1-trackingBlend and is "
                    "therefore exactly zero for authoritative face tracking."
                ),
                "sparseEmission": {
                    "epsilon": SIMPLEX_CULLING_EPSILON,
                    "formula": (
                        "S(x)=0 when x<=epsilon, otherwise "
                        "S(x)=(x-epsilon)/(1-epsilon) on sanitized [0,1]"
                    ),
                    "feedback": (
                        "S is applied only to emitted copies, never to observer "
                        "recurrence."
                    ),
                },
                "reset": (
                    "fast and slow begin at exact one-hot silence only at "
                    "controller initialization"
                ),
            },
            "selectionObjective": {
                "formula": (
                    "J(h)=MSE(h)/MSE(identity)+8*max(0,0.90-TV(h)/TV(teacher))^2"
                ),
                "candidateStrengths": list(HALO_STRENGTH_CANDIDATES),
                "topKCandidates": list(TOP_K_CANDIDATES),
                "tieBreak": cardinality_tie_break_description(),
                "sparseProjection": (
                    "Retain the k largest conditional-barycenter coordinates, "
                    "then apply exact Euclidean projection onto that simplex."
                ),
                "cardinalityAcceptance": {
                    "overallMseGainRetentionVersusDense": (
                        MIN_OVERALL_GAIN_RETENTION
                    ),
                    "fourBlockTransitionMseGainRetentionVersusDense": (
                        MIN_TRANSITION_GAIN_RETENTION
                    ),
                    "velocityMseGainRetentionVersusDense": (
                        MIN_VELOCITY_GAIN_RETENTION
                    ),
                    "minimumRenderRateGainRetention": (
                        MIN_RENDER_RATE_GAIN_RETENTION
                    ),
                },
                "fitFrameCounts": {
                    VISEMES[index]: int(fit_counts[index])
                    for index in range(VISEME_COUNT)
                },
                "cardinalityDevelopmentReports": cardinality_reports,
                "cardinalitySelection": {
                    "selectedTopK": selected_candidate.top_k,
                    "selectedCandidatePassedAllSparseGates": bool(
                        selected_development_report["acceptedForShipping"]
                    ),
                    "denseFallbackUsed": not bool(
                        selected_development_report["acceptedForShipping"]
                    ),
                },
                "selectedHaloStrength": selected_strength,
                "selectedTopK": selected_candidate.top_k,
                "canonicalReferenceHaloStrength": (
                    CANONICAL_REFERENCE_HALO_STRENGTH
                ),
                "canonicalReferenceTopK": CANONICAL_REFERENCE_TOP_K,
                "matchesCanonicalReference": (
                    math.isclose(
                        selected_strength,
                        CANONICAL_REFERENCE_HALO_STRENGTH,
                        rel_tol=0.0,
                        abs_tol=1e-12,
                    )
                    and selected_candidate.top_k
                    == CANONICAL_REFERENCE_TOP_K
                ),
            },
        },
        "model": {
            "orientation": "weights[hardWinner][outputViseme]",
            "selectedHaloStrength": selected_strength,
            "topK": selected_candidate.top_k,
            "conditionalBarycenterWeights": final_centers.astype(
                np.float32
            ).tolist(),
            "weights": final_table.tolist(),
            "fitPlusDevelopmentFrameCounts": {
                VISEMES[index]: int(final_counts[index])
                for index in range(VISEME_COUNT)
            },
            "invariants": {
                "nonnegative": True,
                "rowStochasticTolerance": TABLE_SUM_TOLERANCE,
                "silenceRowBitExact": True,
                "ownWinnerIsUniqueRowMaximum": True,
                "support": support_statistics(final_table),
            },
        },
        "evaluation": {
            "developmentRenderRateQualityGate": development_render_sweep,
            "heldoutIdentity": heldout_identity,
            "heldoutSelectedHalo": {
                **heldout_model,
                **heldout_objective,
                "mseImprovementPercent": 100.0
                * (
                    1.0
                    - float(heldout_model["mse"])
                    / float(heldout_identity["mse"])
                ),
            },
            "heldoutSelectedTopKAtFullHaloStrength": heldout_full,
            "heldoutCardinalityReports": heldout_cardinalities,
            "heldoutRenderRateReport": heldout_render_sweep,
            "heldoutUse": (
                "Reported once after development selection; it is not used to "
                "choose h or modify coefficients."
            ),
        },
        "limitations": [
            "The target estimates a causal conditional mean; it cannot recover phone identity discarded by the hard winner.",
            "No avatar-only method can infer genuine future-phone anticipation from the current VRChat viseme index.",
            "The selected SPIRE subset is English and does not establish universal language or accent coverage.",
            "Oculus smoothing is already present in the teacher; selection is evaluated after the exact additional avatar observer.",
            "Raw native weight mass is diagnostic only and is not a recovered VRChat parameter.",
        ],
    }
    document["tableSha256"] = corpus.canonical_sha256(
        table_hash_input(document)
    )
    document["contentSha256"] = corpus.canonical_sha256(
        audit_hash_input(document)
    )
    validate_document(document)
    return document


def validate_document(document: dict[str, Any]) -> None:
    if document.get("schemaVersion") != 3 or document.get("modelVersion") != 3:
        raise ValueError("Unsupported Oculus halo document version")
    if document.get("visemeOrder") != list(VISEMES):
        raise ValueError("Oculus halo viseme order differs from runtime")
    model = document.get("model")
    if not isinstance(model, dict):
        raise ValueError("Oculus halo document has no model")
    strength = float(model.get("selectedHaloStrength", math.nan))
    if strength not in HALO_STRENGTH_CANDIDATES:
        raise ValueError("Selected Oculus halo strength is outside the grid")
    top_k = int(model.get("topK", -1))
    if top_k not in TOP_K_CANDIDATES:
        raise ValueError("Selected Oculus halo cardinality is outside the grid")
    table = np.asarray(model.get("weights"), dtype=np.float32)
    validate_table(table)
    support = np.count_nonzero(table > 0.0, axis=1)
    if np.any(support[1:] > top_k):
        raise ValueError("Oculus halo table exceeds its declared TopK support")
    if top_k < VISEME_COUNT and np.any(support[1:] != top_k):
        raise ValueError("Sparse Oculus halo rows must retain exactly TopK values")
    if "trajectoryControlPoints" in model or "trajectorySha256" in document:
        raise ValueError("Shipping Oculus halo must not contain a local trajectory")
    if document.get("tableSha256") != corpus.canonical_sha256(
        table_hash_input(document)
    ):
        raise ValueError("Oculus halo table hash mismatch")
    if document.get("contentSha256") != corpus.canonical_sha256(
        audit_hash_input(document)
    ):
        raise ValueError("Oculus halo audit content hash mismatch")
    development_gate = document.get("evaluation", {}).get(
        "developmentRenderRateQualityGate"
    )
    if not isinstance(development_gate, dict) or not development_gate.get(
        "passed", False
    ):
        raise ValueError("Oculus halo development render-rate gate failed")
    for section in ("heldoutIdentity", "heldoutSelectedHalo"):
        metrics = document.get("evaluation", {}).get(section)
        if not isinstance(metrics, dict):
            raise ValueError(f"Missing {section} evaluation")
        for key in ("mse", "rmse", "brier", "top1Agreement"):
            if not math.isfinite(float(metrics.get(key, math.nan))):
                raise ValueError(f"{section}.{key} must be finite")


def generate_csharp(document: dict[str, Any], model_path: Path) -> None:
    validate_document(document)
    table = np.asarray(document["model"]["weights"], dtype=np.float32)
    lines = [
        "// <auto-generated>",
        "// Trained by Tools/AdvancedVisemeTraining/train_oculus_viseme_halo.py.",
        "// Source: SPIRE EMA Corpus paired audio, CC BY 4.0, pinned revision 55f21628de95514e3ff22eaccc75e1547d181297.",
        "// Bandekar, Udupa, and Ghosh (2024), doi:10.21437/Interspeech.2024-1756.",
        "// Compact normalized shape table only; no source audio or per-frame trace is embedded.",
        "// </auto-generated>",
        "using System;",
        "",
        "namespace YUCP.Components",
        "{",
        "    public static class AdvancedVisemeOculusHalo",
        "    {",
        f"        public const int ModelVersion = {MODEL_VERSION};",
        f"        public const int VisemeCount = {VISEME_COUNT};",
        f"        public const int TopK = {int(document['model']['topK'])};",
        f"        public const string ContentSha256 = \"{document['contentSha256']}\";",
        f"        public const string TableSha256 = \"{document['tableSha256']}\";",
        f"        public const float ObserverResponseSeconds = {float_literal(OBSERVER_RESPONSE_SECONDS)};",
        f"        public const float EvaluationLiveliness = {float_literal(EVALUATION_LIVELINESS)};",
        f"        public const float HaloStrength = {float_literal(document['model']['selectedHaloStrength'])};",
        "",
        "        // Flattened as [hard winner, output viseme].",
        "        private static readonly float[] WeightValues =",
        "        {",
    ]
    for winner, row in enumerate(table):
        lines.append(f"            // {VISEMES[winner]}")
        lines.append(
            "            "
            + ", ".join(float_literal(value) for value in row)
            + ","
        )
    lines.extend(
        [
            "        };",
            "",
            "        public static float Weight(int hardWinner, int outputViseme)",
            "        {",
            "            RequireIndex(hardWinner, nameof(hardWinner));",
            "            RequireIndex(outputViseme, nameof(outputViseme));",
            "            return WeightValues[hardWinner * VisemeCount + outputViseme];",
            "        }",
            "",
            "        private static void RequireIndex(int value, string name)",
            "        {",
            "            if ((uint)value >= VisemeCount)",
            "                throw new ArgumentOutOfRangeException(name);",
            "        }",
            "    }",
            "}",
            "",
        ]
    )
    corpus.write_text_atomic(model_path, "\n".join(lines))


def default_cache_dir() -> Path:
    local = os.environ.get("LOCALAPPDATA")
    if not local:
        raise RuntimeError("LOCALAPPDATA is not defined; pass --cache-dir")
    return Path(local) / "YUCP" / "AdvancedVisemeTraining" / "SPIRE_EMA_CORPUS"


def train(args: argparse.Namespace) -> dict[str, Any]:
    source, processed, audio = validate_inputs(
        args.source_manifest,
        args.processed_selection,
        args.audio_manifest,
        args.cache_dir,
        args.ovr_dll,
    )
    records, extraction = extract_utterances(
        args.cache_dir, audio, args.ovr_dll
    )
    document = build_document(
        source, processed, audio, records, extraction
    )
    corpus.write_json_atomic(args.audit_json, document)
    generate_csharp(document, args.model_cs)
    selected = float(document["model"]["selectedHaloStrength"])
    print(f"Audit JSON: {args.audit_json}", flush=True)
    print(f"Runtime C#: {args.model_cs}", flush=True)
    print(f"Content SHA-256: {document['contentSha256']}", flush=True)
    print(f"Table SHA-256: {document['tableSha256']}", flush=True)
    print(f"Continuous extraction SHA-256: {extraction.continuous_sequence_sha256}", flush=True)
    print(f"Dominant extraction SHA-256: {extraction.dominant_sequence_sha256}", flush=True)
    selected_top_k = int(document["model"]["topK"])
    print(f"Selected TopK / halo strength: {selected_top_k} / {selected:.2f}", flush=True)
    if not (
        math.isclose(
            selected,
            CANONICAL_REFERENCE_HALO_STRENGTH,
            rel_tol=0.0,
            abs_tol=1e-12,
        )
        and selected_top_k == CANONICAL_REFERENCE_TOP_K
    ):
        print(
            "WARNING: deterministic selection differs from the canonical reference "
            f"TopK={CANONICAL_REFERENCE_TOP_K}, "
            f"h={CANONICAL_REFERENCE_HALO_STRENGTH:.2f}.",
            file=sys.stderr,
            flush=True,
        )
    return document


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("train", "all", "generate"))
    parser.add_argument("--cache-dir", type=Path, default=default_cache_dir())
    parser.add_argument("--source-manifest", type=Path, default=DEFAULT_SOURCE_MANIFEST)
    parser.add_argument(
        "--processed-selection", type=Path, default=DEFAULT_PROCESSED_SELECTION
    )
    parser.add_argument("--audio-manifest", type=Path, default=DEFAULT_AUDIO_MANIFEST)
    parser.add_argument("--audit-json", type=Path, default=DEFAULT_AUDIT_JSON)
    parser.add_argument("--model-cs", type=Path, default=DEFAULT_MODEL_CS)
    parser.add_argument("--ovr-dll", type=Path, default=DEFAULT_OVR_DLL)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    args.ovr_dll = args.ovr_dll.resolve()
    if args.command == "generate":
        document = corpus.load_json(args.audit_json)
        validate_document(document)
        generate_csharp(document, args.model_cs)
        print(f"Runtime C#: {args.model_cs}", flush=True)
        return 0
    train(args)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
