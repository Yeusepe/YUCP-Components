#!/usr/bin/env python3
"""Train a causal visible-face posterior for Oculus merged phone classes.

The runtime receives only Oculus' winning viseme.  This trainer pairs the
installed Oculus LipSync classifier with SPIRE's synchronized audio, forced
phone labels, and head-corrected EMA.  It learns a portable face
log-likelihood ratio for M-compatible versus N/L-compatible articulation and
combines it with a Dirichlet-smoothed prior for each actual Oculus winner.

P/B stops are deliberately not negative examples: bilabial closure makes them
look like M.  A separate all-phone eligibility model makes stops, vowels, and
unrelated consonants lower the emitted expert reliability so the avatar can
abstain instead of hallucinating a discarded M-versus-N/L distinction.
"""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import math
import os
import re
import sys
import wave
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Sequence

import numpy as np

import train_transition_retention as corpus


SCRIPT_DIR = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_SOURCE_MANIFEST = SCRIPT_DIR / "source_manifest.json"
DEFAULT_AUDIO_MANIFEST = SCRIPT_DIR / "Generated" / "spire_audio_selection_manifest.json"
DEFAULT_AUDIT_JSON = SCRIPT_DIR / "Generated" / "advanced_viseme_hidden_phone_posterior.json"
DEFAULT_TRANSITION_AUDIT = SCRIPT_DIR / "Generated" / "advanced_viseme_transition_retention.json"
DEFAULT_MODEL_CS = (
    REPOSITORY_ROOT
    / "Packages"
    / "com.yucp.components"
    / "Runtime"
    / "Components"
    / "Data"
    / "Generated"
    / "AdvancedVisemeHiddenPhonePosterior.generated.cs"
)
DEFAULT_OVR_DLL = REPOSITORY_ROOT / "Assets" / "Oculus" / "LipSync" / "Plugins" / "Win64" / "OVRLipSync.dll"

MODEL_VERSION = 1
VISEMES = corpus.VISEMES
VISEME_COUNT = len(VISEMES)
BALANCED_AXES = ("JawOpen", "LipAperture", "LipProtrusion")
QUALITY_AXES = ("JawOpen", "JawAdvance", "LipAperture", "LipProtrusion")
APERTURE_AXES = ("JawOpen", "LipAperture")
FEATURE_STAGES = ("current", "current-fast", "fast-slow")
OBSERVER_RESPONSE_SECONDS = 0.024
HEADROOM_FLOOR = 0.075
CALIBRATION_QUANTILES = (0.02, 0.98)

M_PHONES = frozenset(("m", "em"))
NL_PHONES = frozenset(("n", "nx", "en", "l", "el"))
STOP_PHONES = frozenset(("p", "b", "pcl", "bcl"))
TARGET_NAMES = ("NL", "M", "Stop")
TARGET_NL = 0
TARGET_M = 1
TARGET_STOP = 2

EXPECTED_AUDIO_ARCHIVE = "audios.zip"
EXPECTED_AUDIO_ARCHIVE_BYTES = 1_656_697_958
EXPECTED_AUDIO_ARCHIVE_SHA256 = "e07eaee117126ebc852593805b865798800a0438dd97cb423f50d1d2b1de2baf"
EXPECTED_AUDIO_WAV_ENTRIES = 17_480
EXPECTED_TRANSITION_CONTENT_SHA256 = "7c48b4fbd137425589323e27abedbfed88edc5b25293936c7951da7a9e0c7d61"
MAX_AUDIO_BYTES = 2 * 1024 * 1024
MAX_AUDIO_ARCHIVE_MEMBERS = 20_000

SOURCE_SAMPLE_RATE = 16_000
ANALYSIS_SAMPLE_RATE = 48_000
ANALYSIS_BUFFER_SAMPLES = 1_024
OVR_PROVIDER_ENHANCED = 1
OVR_SIGNAL_SMOOTHING = 3
OVR_SMOOTHING = 70
OVR_AUDIO_F32_MONO = 2
DIRICHLET_ALPHA = 1.0
RIDGE_CANDIDATES = (0.05, 0.2, 1.0, 4.0, 16.0)
RUNTIME_FEATURE_CLAMP = 2.0

AUDIO_ENTRY_RE = re.compile(r"^audios/spk(?P<speaker>\d+)/.+?(?P<prompt>\d{4})\.wav$", re.IGNORECASE)


@dataclass
class Record:
    split: str
    speaker: int
    prompt: int
    prompt_ordinal: int
    source_entry: str
    audio_entry: str
    visible_quality: np.ndarray
    targets: np.ndarray
    occurrence: np.ndarray
    oculus: np.ndarray


@dataclass(frozen=True)
class TransitionModel:
    groups: tuple[str, ...]
    decay_seconds: np.ndarray
    retention: np.ndarray
    content_sha256: str
    file_sha256: str


class OculusLipSync:
    """Small deterministic ctypes wrapper around the installed Windows DLL."""

    def __init__(self, dll_path: Path) -> None:
        if os.name != "nt":
            raise RuntimeError("Oculus extraction requires the installed Windows OVRLipSync.dll")
        if not dll_path.is_file():
            raise FileNotFoundError(dll_path)
        if hasattr(os, "add_dll_directory"):
            self._dll_directory = os.add_dll_directory(str(dll_path.parent))
        else:
            self._dll_directory = None
        self.dll = ctypes.WinDLL(str(dll_path))
        self._bind()
        result = self.initialize(ANALYSIS_SAMPLE_RATE, ANALYSIS_BUFFER_SAMPLES)
        if result != 0:
            raise RuntimeError(f"ovrLipSyncDll_Initialize failed with {result}")
        self.context = ctypes.c_uint32(0)
        result = self.create_context(
            ctypes.byref(self.context), OVR_PROVIDER_ENHANCED, ANALYSIS_SAMPLE_RATE, False
        )
        if result != 0 or self.context.value == 0:
            self.shutdown()
            raise RuntimeError(f"ovrLipSyncDll_CreateContextEx failed with {result}")
        result = self.send_signal(
            self.context.value, OVR_SIGNAL_SMOOTHING, OVR_SMOOTHING, 0
        )
        if result != 0:
            self.close()
            raise RuntimeError(f"setting Oculus viseme smoothing failed with {result}")

    def _bind(self) -> None:
        self.initialize = self.dll.ovrLipSyncDll_Initialize
        self.initialize.argtypes = (ctypes.c_int, ctypes.c_int)
        self.initialize.restype = ctypes.c_int
        self.shutdown = self.dll.ovrLipSyncDll_Shutdown
        self.shutdown.argtypes = ()
        self.shutdown.restype = None
        self.create_context = self.dll.ovrLipSyncDll_CreateContextEx
        self.create_context.argtypes = (
            ctypes.POINTER(ctypes.c_uint32), ctypes.c_int, ctypes.c_int, ctypes.c_bool
        )
        self.create_context.restype = ctypes.c_int
        self.destroy_context = self.dll.ovrLipSyncDll_DestroyContext
        self.destroy_context.argtypes = (ctypes.c_uint32,)
        self.destroy_context.restype = ctypes.c_int
        self.reset_context = self.dll.ovrLipSyncDll_ResetContext
        self.reset_context.argtypes = (ctypes.c_uint32,)
        self.reset_context.restype = ctypes.c_int
        self.send_signal = self.dll.ovrLipSyncDll_SendSignal
        self.send_signal.argtypes = (ctypes.c_uint32, ctypes.c_int, ctypes.c_int, ctypes.c_int)
        self.send_signal.restype = ctypes.c_int
        self.process_frame = self.dll.ovrLipSyncDll_ProcessFrameEx
        self.process_frame.argtypes = (
            ctypes.c_uint32,
            ctypes.c_void_p,
            ctypes.c_uint32,
            ctypes.c_int,
            ctypes.POINTER(ctypes.c_int),
            ctypes.POINTER(ctypes.c_int),
            ctypes.POINTER(ctypes.c_float),
            ctypes.c_int,
            ctypes.POINTER(ctypes.c_float),
            ctypes.c_void_p,
            ctypes.c_int,
        )
        self.process_frame.restype = ctypes.c_int
        self.get_version = self.dll.ovrLipSyncDll_GetVersion
        self.get_version.argtypes = (
            ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int)
        )
        self.get_version.restype = ctypes.c_void_p

    def version(self) -> str:
        major, minor, patch = ctypes.c_int(), ctypes.c_int(), ctypes.c_int()
        self.get_version(ctypes.byref(major), ctypes.byref(minor), ctypes.byref(patch))
        return f"{major.value}.{minor.value}.{patch.value}"

    def analyze(self, samples_16k: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        if self.reset_context(self.context.value) != 0:
            raise RuntimeError("Oculus context reset failed")
        if self.send_signal(self.context.value, OVR_SIGNAL_SMOOTHING, OVR_SMOOTHING, 0) != 0:
            raise RuntimeError("Oculus smoothing reset failed")

        source_position = np.arange(len(samples_16k), dtype=np.float64)
        target_count = int(round(len(samples_16k) * ANALYSIS_SAMPLE_RATE / SOURCE_SAMPLE_RATE))
        target_position = np.arange(target_count, dtype=np.float64) * SOURCE_SAMPLE_RATE / ANALYSIS_SAMPLE_RATE
        samples = np.interp(target_position, source_position, samples_16k).astype(np.float32)
        padded_count = int(math.ceil(len(samples) / ANALYSIS_BUFFER_SAMPLES) * ANALYSIS_BUFFER_SAMPLES)
        padded = np.zeros(padded_count, dtype=np.float32)
        padded[: len(samples)] = samples

        count = padded_count // ANALYSIS_BUFFER_SAMPLES
        winners = np.empty(count, dtype=np.uint8)
        times = np.empty(count, dtype=np.float64)
        delays = np.empty(count, dtype=np.int16)
        visemes = (ctypes.c_float * VISEME_COUNT)()
        frame_number = ctypes.c_int()
        frame_delay = ctypes.c_int()
        laughter = ctypes.c_float()
        for index in range(count):
            frame = padded[index * ANALYSIS_BUFFER_SAMPLES : (index + 1) * ANALYSIS_BUFFER_SAMPLES]
            result = self.process_frame(
                self.context.value,
                frame.ctypes.data_as(ctypes.c_void_p),
                ANALYSIS_BUFFER_SAMPLES,
                OVR_AUDIO_F32_MONO,
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
            winners[index] = int(np.argmax(np.ctypeslib.as_array(visemes)))
            delays[index] = frame_delay.value
            # The result becomes causally available after this complete DSP buffer.
            times[index] = ((index + 1) * ANALYSIS_BUFFER_SAMPLES - frame_delay.value * 48.0) / ANALYSIS_SAMPLE_RATE
        return times, winners, delays

    def close(self) -> None:
        if getattr(self, "context", None) is not None and self.context.value:
            self.destroy_context(self.context.value)
            self.context.value = 0
        if getattr(self, "shutdown", None) is not None:
            self.shutdown()
        if self._dll_directory is not None:
            self._dll_directory.close()
            self._dll_directory = None

    def __enter__(self) -> "OculusLipSync":
        return self

    def __exit__(self, *_: Any) -> None:
        self.close()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_transition_model(path: Path) -> TransitionModel:
    document = corpus.load_json(path)
    groups = tuple(document.get("articulatorGroups", ()))
    if groups != ("Jaw", "Lips", "TongueTip", "TongueBody"):
        raise ValueError("Transition audit group order differs from the runtime enum")
    if document.get("visemeOrder") != list(VISEMES):
        raise ValueError("Transition audit viseme order differs from the runtime")
    content_sha256 = str(document.get("contentSha256", ""))
    if content_sha256 != EXPECTED_TRANSITION_CONTENT_SHA256:
        raise ValueError("Transition audit is not the pinned runtime table")
    decays = np.asarray(document.get("decaySeconds"), dtype=np.float64)
    source_retention = document.get("retention")
    if decays.shape != (4,) or not isinstance(source_retention, dict):
        raise ValueError("Invalid transition timing/table shape")
    retention = np.asarray([source_retention[group] for group in groups], dtype=np.float64)
    if retention.shape != (4, VISEME_COUNT, VISEME_COUNT):
        raise ValueError("Transition retention must be [4,15,15]")
    if (
        not np.all(np.isfinite(decays))
        or not np.all(np.isfinite(retention))
        or np.any(decays <= 0.0)
        or np.any(retention < 0.0)
        or np.any(retention > 1.0)
    ):
        raise ValueError("Transition audit contains invalid values")
    return TransitionModel(
        groups=groups,
        decay_seconds=decays,
        retention=retention,
        content_sha256=content_sha256,
        file_sha256=sha256_file(path),
    )


def beta_runtime_weights(
    winners: np.ndarray,
    transition: TransitionModel,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Simulate the runtime Beta graph at the corpus' 100 Hz frame clock.

    Returns Jaw slow, Lips slow, and common fast simplexes.  State starts from
    Oculus silence exactly like the generated Animator parameters.
    """

    alpha = 1.0 - math.exp(-1.0 / (corpus.EXPECTED_SAMPLE_RATE_HZ * OBSERVER_RESPONSE_SECONDS))
    raw = np.zeros(VISEME_COUNT, dtype=np.float64)
    raw[0] = 1.0
    fast = raw.copy()
    slow = raw.copy()
    contexts = np.repeat(raw[None, :], len(transition.groups), axis=0)
    outputs = np.empty((len(winners), len(transition.groups), VISEME_COUNT), dtype=np.float64)
    common_fast = np.empty((len(winners), VISEME_COUNT), dtype=np.float64)
    for frame_index, winner in enumerate(winners):
        raw.fill(0.0)
        raw[int(winner)] = 1.0
        fast += alpha * (raw - fast)
        slow += alpha * (fast - slow)
        retentions = np.empty(len(transition.groups), dtype=np.float64)
        for group in range(len(transition.groups)):
            context_alpha = 1.0 - math.exp(
                -1.0 / (corpus.EXPECTED_SAMPLE_RATE_HZ * transition.decay_seconds[group])
            )
            contexts[group] += context_alpha * (raw - contexts[group])
            # Runtime continuously mixes previous-context and destination axes.
            retentions[group] = contexts[group] @ transition.retention[group] @ fast
            lead = np.clip(1.0 - retentions[group], 0.0, 1.0)
            outputs[frame_index, group] = slow + lead * (fast - slow)
        mean_lead = np.clip(1.0 - float(np.mean(retentions)), 0.0, 1.0)
        common_fast[frame_index] = fast + mean_lead * (raw - fast)
    # Floating accumulation should preserve the simplex, but normalize exactly
    # to match the blend-tree interpretation and make audits robust to roundoff.
    outputs /= np.maximum(outputs.sum(axis=2, keepdims=True), 1e-12)
    common_fast /= np.maximum(common_fast.sum(axis=1, keepdims=True), 1e-12)
    return outputs[:, 0], outputs[:, 1], common_fast


def audio_archive_url(source: dict[str, Any]) -> str:
    dataset = source["dataset"]
    return (
        f"https://huggingface.co/datasets/{dataset['repository']}/resolve/"
        f"{dataset['repositoryRevision']}/{EXPECTED_AUDIO_ARCHIVE}?download=true"
    )


def audio_entry_from_processed(entry: str) -> str:
    path = Path(entry)
    if len(path.parts) != 3 or path.parts[0].lower() != "processed" or path.suffix.lower() != ".pt":
        raise ValueError(f"Unexpected processed member path: {entry!r}")
    return str(Path("audios") / path.parts[1] / path.with_suffix(".wav").name).replace("\\", "/")


def _audio_path(cache_dir: Path, entry: str) -> Path:
    relative = Path(entry)
    if relative.is_absolute() or any(part in {".", ".."} for part in relative.parts):
        raise ValueError(f"Unsafe audio member: {entry!r}")
    base = cache_dir.resolve(strict=False)
    result = (base / relative).resolve(strict=False)
    result.relative_to(base)
    return result


def _audio_manifest_hash_input(document: dict[str, Any]) -> dict[str, Any]:
    return {
        "schemaVersion": document["schemaVersion"],
        "repository": document["repository"],
        "repositoryRevision": document["repositoryRevision"],
        "audioArchiveSha256": document["audioArchiveSha256"],
        "processedSelectionContentSha256": document["processedSelectionContentSha256"],
        "entries": document["entries"],
    }


def validate_audio_manifest(document: dict[str, Any], processed: dict[str, Any], cache_dir: Path) -> None:
    if document.get("schemaVersion") != 1 or document.get("complete") is not True:
        raise ValueError("Audio selection manifest is incomplete or unsupported")
    if document.get("repositoryRevision") != corpus.EXPECTED_REPOSITORY_REVISION:
        raise ValueError("Audio selection repository revision differs from the pinned processed data")
    if document.get("audioArchiveSha256") != EXPECTED_AUDIO_ARCHIVE_SHA256:
        raise ValueError("Audio selection archive hash is not pinned")
    if document.get("processedSelectionContentSha256") != processed["selectionContentSha256"]:
        raise ValueError("Audio and processed selections differ")
    entries = document.get("entries")
    if not isinstance(entries, list) or len(entries) != len(processed["entries"]):
        raise ValueError("Audio selection entry count differs from processed selection")
    for audio, source in zip(entries, processed["entries"]):
        if audio.get("processedEntry") != source["entry"]:
            raise ValueError("Audio selection ordering differs from processed selection")
        path = _audio_path(cache_dir, str(audio.get("entry", "")))
        if not path.is_file() or path.stat().st_size != int(audio.get("uncompressedBytes", -1)):
            raise IOError(f"Missing or size-mismatched audio: {path}")
        crc32, digest = corpus.file_digests(path)
        if crc32 != audio.get("crc32") or digest != audio.get("sha256"):
            raise IOError(f"Audio integrity check failed: {path}")
    expected = corpus.canonical_sha256(_audio_manifest_hash_input(document))
    if document.get("selectionContentSha256") != expected:
        raise ValueError("Audio selection content hash mismatch")


def build_audio_manifest(source: dict[str, Any], processed: dict[str, Any], cache_dir: Path) -> dict[str, Any]:
    try:
        from remotezip import RemoteZip
    except ImportError as error:
        raise RuntimeError("Install Tools/AdvancedVisemeTraining/requirements.txt") from error
    url = audio_archive_url(source)
    print(f"Indexing pinned audio archive: {url}", flush=True)
    with RemoteZip(url) as archive:
        infos = archive.infolist()
    if len(infos) > MAX_AUDIO_ARCHIVE_MEMBERS:
        raise ValueError("Pinned audio archive exceeds member safety cap")
    wavs = [info for info in infos if AUDIO_ENTRY_RE.match(info.filename)]
    if len(wavs) != EXPECTED_AUDIO_WAV_ENTRIES:
        raise ValueError(f"Pinned archive has {len(wavs)} WAVs, expected {EXPECTED_AUDIO_WAV_ENTRIES}")
    by_name = {info.filename.lower(): info for info in wavs}
    entries: list[dict[str, Any]] = []
    for source_entry in processed["entries"]:
        name = audio_entry_from_processed(source_entry["entry"])
        info = by_name.get(name.lower())
        if info is None:
            raise FileNotFoundError(f"No paired audio for {source_entry['entry']}")
        if not 44 < int(info.file_size) <= MAX_AUDIO_BYTES:
            raise ValueError(f"Paired audio size outside safety limits: {name}")
        path = _audio_path(cache_dir, info.filename)
        if not path.is_file() or path.stat().st_size != int(info.file_size):
            raise FileNotFoundError(
                f"Missing {path}; populate the selected audios.zip subset before training"
            )
        crc32, digest = corpus.file_digests(path)
        expected_crc = f"{int(info.CRC):08x}"
        if crc32 != expected_crc:
            raise IOError(f"CRC mismatch for {path}")
        entries.append(
            {
                "split": source_entry["split"],
                "speaker": source_entry["speaker"],
                "prompt": source_entry["prompt"],
                "promptOrdinal": source_entry["promptOrdinal"],
                "processedEntry": source_entry["entry"],
                "entry": info.filename,
                "uncompressedBytes": int(info.file_size),
                "compressedBytes": int(info.compress_size),
                "crc32": expected_crc,
                "sha256": digest,
            }
        )
    document: dict[str, Any] = {
        "schemaVersion": 1,
        "complete": True,
        "repository": corpus.EXPECTED_REPOSITORY,
        "repositoryRevision": corpus.EXPECTED_REPOSITORY_REVISION,
        "audioArchive": EXPECTED_AUDIO_ARCHIVE,
        "audioArchiveBytes": EXPECTED_AUDIO_ARCHIVE_BYTES,
        "audioArchiveSha256": EXPECTED_AUDIO_ARCHIVE_SHA256,
        "archiveWavEntryCount": EXPECTED_AUDIO_WAV_ENTRIES,
        "processedSelectionContentSha256": processed["selectionContentSha256"],
        "selectedEntryCount": len(entries),
        "selectedCompressedBytes": sum(entry["compressedBytes"] for entry in entries),
        "selectedUncompressedBytes": sum(entry["uncompressedBytes"] for entry in entries),
        "entries": entries,
    }
    document["selectionContentSha256"] = corpus.canonical_sha256(_audio_manifest_hash_input(document))
    validate_audio_manifest(document, processed, cache_dir)
    return document


def read_pcm16_mono(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as stream:
        if (
            stream.getnchannels() != 1
            or stream.getsampwidth() != 2
            or stream.getframerate() != SOURCE_SAMPLE_RATE
            or stream.getcomptype() != "NONE"
        ):
            raise ValueError(f"Expected 16 kHz mono PCM16 WAV: {path}")
        count = stream.getnframes()
        if not 1 <= count <= SOURCE_SAMPLE_RATE * 30:
            raise ValueError(f"Audio duration outside safety limits: {path}")
        payload = stream.readframes(count)
    if len(payload) != count * 2:
        raise IOError(f"Truncated WAV payload: {path}")
    return np.frombuffer(payload, dtype="<i2").astype(np.float32) / 32768.0


def phone_targets(phones: Sequence[str], durations: Sequence[int], frame_count: int) -> tuple[np.ndarray, np.ndarray]:
    targets = np.full(frame_count, -1, dtype=np.int8)
    occurrences = np.full(frame_count, -1, dtype=np.int32)
    cursor = 0
    occurrence = 0
    for raw_phone, raw_duration in zip(phones, durations):
        duration = int(raw_duration)
        phone = corpus.normalize_phone(raw_phone)
        target = (
            TARGET_M if phone in M_PHONES else
            TARGET_NL if phone in NL_PHONES else
            TARGET_STOP if phone in STOP_PHONES else -1
        )
        end = cursor + duration
        targets[cursor:end] = target
        # Every forced-phone occurrence receives an id, including vowels,
        # silence, and consonants outside the M/N/L/P/B training subset.  The
        # full set is required to estimate P(hypothesis eligible | winner)
        # without conditioning its denominator on the classes of interest.
        occurrences[cursor:end] = occurrence
        occurrence += 1
        cursor = end
    if cursor != frame_count:
        raise ValueError("Phone durations do not cover the trimmed EMA")
    return targets, occurrences


def visible_semantics(geometry: np.ndarray) -> np.ndarray:
    upper_lip = geometry[:, [0, 1]]
    lower_lip = geometry[:, [2, 3]]
    jaw = geometry[:, [8, 9]]
    lip_midpoint = 0.5 * (upper_lip + lower_lip)
    return np.stack(
        (
            upper_lip[:, 1] - jaw[:, 1],
            jaw[:, 0] - upper_lip[:, 0],
            upper_lip[:, 1] - lower_lip[:, 1],
            lip_midpoint[:, 0],
        ),
        axis=1,
    )


def load_records(
    cache_dir: Path,
    processed: dict[str, Any],
    audio_manifest: dict[str, Any],
    dll_path: Path,
) -> tuple[list[Record], dict[str, Any]]:
    records: list[Record] = []
    extraction_digest = hashlib.sha256()
    delay_counts: dict[int, int] = {}
    with OculusLipSync(dll_path) as oculus:
        dll_version = oculus.version()
        total = len(processed["entries"])
        for index, (entry, audio_entry) in enumerate(
            zip(processed["entries"], audio_manifest["entries"]), start=1
        ):
            if not corpus.cached_entry_is_valid(cache_dir, entry, require_sha256=True):
                raise IOError(f"Processed cache integrity failed: {entry['entry']}")
            payload = corpus.restricted_load_pt(corpus.cache_path(cache_dir, entry))
            geometry_value = payload.get("ema_trimmed")
            phones = payload.get("phonemes")
            durations = payload.get("durations")
            begin_end = payload.get("begin_end")
            if not isinstance(geometry_value, np.ndarray) or geometry_value.dtype.hasobject:
                raise ValueError(f"Missing real ema_trimmed in {entry['entry']}")
            geometry = np.asarray(geometry_value, dtype=np.float64)
            if geometry.ndim != 2 or geometry.shape[1] != 18 or not np.all(np.isfinite(geometry)):
                raise ValueError(f"Invalid ema_trimmed geometry in {entry['entry']}")
            if not isinstance(phones, (list, tuple)) or not isinstance(durations, (list, tuple)):
                raise ValueError(f"Missing phone alignment in {entry['entry']}")
            if not (
                isinstance(begin_end, (list, tuple))
                and len(begin_end) == 2
                and all(isinstance(value, (int, np.integer)) for value in begin_end)
            ):
                raise ValueError(f"Invalid begin_end in {entry['entry']}")
            begin, end = (int(begin_end[0]), int(begin_end[1]))
            if end - begin != len(geometry):
                raise ValueError(f"begin_end does not match trimmed EMA in {entry['entry']}")
            targets, occurrences = phone_targets(phones, durations, len(geometry))
            audio_path = _audio_path(cache_dir, audio_entry["entry"])
            samples = read_pcm16_mono(audio_path)
            if end > int(math.ceil(len(samples) * corpus.EXPECTED_SAMPLE_RATE_HZ / SOURCE_SAMPLE_RATE)) + 1:
                raise ValueError(f"EMA trim exceeds paired WAV duration: {entry['entry']}")
            times, winners, delays = oculus.analyze(samples)
            for value, count in zip(*np.unique(delays, return_counts=True)):
                delay_counts[int(value)] = delay_counts.get(int(value), 0) + int(count)
            # Pair each EMA frame with the newest Oculus result already available.
            ema_times = (begin + np.arange(len(geometry), dtype=np.float64) + 0.5) / corpus.EXPECTED_SAMPLE_RATE_HZ
            result_indices = np.searchsorted(times, ema_times, side="right") - 1
            result_indices = np.clip(result_indices, 0, len(winners) - 1)
            frame_winners = winners[result_indices].astype(np.int16)
            extraction_digest.update(entry["entry"].encode("utf-8"))
            extraction_digest.update(frame_winners.astype("<i2", copy=False).tobytes())
            records.append(
                Record(
                    split=str(entry["split"]),
                    speaker=int(entry["speaker"]),
                    prompt=int(entry["prompt"]),
                    prompt_ordinal=int(entry["promptOrdinal"]),
                    source_entry=str(entry["entry"]),
                    audio_entry=str(audio_entry["entry"]),
                    visible_quality=visible_semantics(geometry),
                    targets=targets,
                    occurrence=occurrences,
                    oculus=frame_winners,
                )
            )
            if index == total or index % 25 == 0:
                print(f"Oculus analyzed {index}/{total} synchronized utterances.", flush=True)
    return records, {
        "dllVersion": dll_version,
        "frameDelayMsCounts": {str(key): delay_counts[key] for key in sorted(delay_counts)},
        "dominantSequenceSha256": extraction_digest.hexdigest(),
    }


def calibration_partition(records: Sequence[Record]) -> tuple[list[Record], list[Record]]:
    calibration: list[Record] = []
    evaluation: list[Record] = []
    for speaker in sorted({record.speaker for record in records}):
        ordered = sorted((record for record in records if record.speaker == speaker), key=lambda value: value.prompt)
        for index, record in enumerate(ordered):
            (calibration if index % 4 == 0 else evaluation).append(record)
    return calibration, evaluation


def speaker_bounds(records: Sequence[Record]) -> dict[int, tuple[np.ndarray, np.ndarray]]:
    result: dict[int, tuple[np.ndarray, np.ndarray]] = {}
    for speaker in sorted({record.speaker for record in records}):
        values = np.concatenate([record.visible_quality for record in records if record.speaker == speaker])
        lower = np.quantile(values, CALIBRATION_QUANTILES[0], axis=0)
        upper = np.maximum(np.quantile(values, CALIBRATION_QUANTILES[1], axis=0), lower + 1e-4)
        result[speaker] = lower, upper
    return result


def normalize_records(records: Sequence[Record], bounds: dict[int, tuple[np.ndarray, np.ndarray]]) -> list[np.ndarray]:
    normalized: list[np.ndarray] = []
    for record in records:
        if record.speaker not in bounds:
            raise ValueError(f"No tracker-range calibration for speaker {record.speaker}")
        lower, upper = bounds[record.speaker]
        normalized.append(np.clip((record.visible_quality - lower) / (upper - lower), 0.0, 1.0))
    return normalized


def viseme_centers(records: Sequence[Record], normalized: Sequence[np.ndarray]) -> np.ndarray:
    global_center = np.median(np.concatenate(normalized), axis=0)
    centers = np.empty((VISEME_COUNT, len(QUALITY_AXES)), dtype=np.float64)
    for viseme in range(VISEME_COUNT):
        values = [value[record.oculus == viseme] for record, value in zip(records, normalized) if np.any(record.oculus == viseme)]
        centers[viseme] = np.median(np.concatenate(values), axis=0) if values else global_center
    return centers


def observer_features(values: np.ndarray, center: np.ndarray) -> np.ndarray:
    if center.shape != values.shape:
        raise ValueError("Observer center trajectory must match visible tracking values")
    delta = values - center
    headroom = np.where(delta >= 0.0, 1.0 - center, center)
    residual = np.clip(delta / np.maximum(headroom, HEADROOM_FLOOR), -1.0, 1.0)
    alpha = 1.0 - math.exp(-1.0 / (corpus.EXPECTED_SAMPLE_RATE_HZ * OBSERVER_RESPONSE_SECONDS))
    # Generated Animator parameters reset to zero. Leading neutral/silence has
    # already settled these states in live use; zero is also the conservative
    # utterance boundary for the trimmed corpus.
    fast = np.zeros(residual.shape[1], dtype=np.float64)
    slow = np.zeros(residual.shape[1], dtype=np.float64)
    features = np.empty((len(residual), residual.shape[1] * 3), dtype=np.float64)
    for index, current in enumerate(residual):
        fast += alpha * (current - fast)
        slow += alpha * (fast - slow)
        features[index] = np.concatenate((current, current - fast, fast - slow))
    # The generated Animator uses Term.Signed for every channel.  Match its
    # exact saturation during fitting and evaluation, including current-stage
    # inputs whose physical training range is normally only [-1,1].
    return np.clip(features, -RUNTIME_FEATURE_CLAMP, RUNTIME_FEATURE_CLAMP)


@dataclass
class Dataset:
    x_aperture: np.ndarray
    x_balanced: np.ndarray
    x_quality: np.ndarray
    y: np.ndarray
    winners: np.ndarray
    observation_weights: np.ndarray
    occurrence: np.ndarray
    speakers: np.ndarray
    entries: np.ndarray


def make_dataset(
    records: Sequence[Record],
    bounds: dict[int, tuple[np.ndarray, np.ndarray]],
    centers: np.ndarray,
    transition: TransitionModel,
    center_mode: str = "beta",
) -> Dataset:
    normalized = normalize_records(records, bounds)
    quality: list[np.ndarray] = []
    balanced: list[np.ndarray] = []
    aperture: list[np.ndarray] = []
    y: list[np.ndarray] = []
    winners: list[np.ndarray] = []
    observation_weights: list[np.ndarray] = []
    occurrence: list[np.ndarray] = []
    speakers: list[np.ndarray] = []
    entries: list[np.ndarray] = []
    occurrence_offset = 0
    for entry_index, (record, values) in enumerate(zip(records, normalized)):
        jaw_slow, lips_slow, common_fast = beta_runtime_weights(record.oculus, transition)
        if center_mode == "beta":
            center = np.empty_like(values)
            center[:, 0] = jaw_slow @ centers[:, 0]
            center[:, 1] = jaw_slow @ centers[:, 1]
            center[:, 2] = lips_slow @ centers[:, 2]
            center[:, 3] = lips_slow @ centers[:, 3]
        elif center_mode == "hard":
            center = centers[record.oculus]
        else:
            raise ValueError(f"Unknown center mode: {center_mode}")
        q = observer_features(values, center)
        # Each observer stage is contiguous; remove JawAdvance from each stage.
        b = np.concatenate((q[:, 0:1], q[:, 2:4], q[:, 4:5], q[:, 6:8], q[:, 8:9], q[:, 10:12]), axis=1)
        a = q[:, [0, 2, 4, 6, 8, 10]]
        quality.append(q)
        balanced.append(b)
        aperture.append(a)
        y.append(record.targets)
        winners.append(record.oculus)
        observation_weights.append(common_fast)
        local_occurrence = record.occurrence
        occurrence.append(local_occurrence + occurrence_offset)
        occurrence_offset += int(local_occurrence.max(initial=-1)) + 1
        speakers.append(np.full(len(record.targets), record.speaker, dtype=np.int16))
        entries.append(np.full(len(record.targets), entry_index, dtype=np.int32))
    return Dataset(
        x_aperture=np.concatenate(aperture),
        x_balanced=np.concatenate(balanced),
        x_quality=np.concatenate(quality),
        y=np.concatenate(y),
        winners=np.concatenate(winners),
        observation_weights=np.concatenate(observation_weights),
        occurrence=np.concatenate(occurrence),
        speakers=np.concatenate(speakers),
        entries=np.concatenate(entries),
    )


def hypothesis_eligible_mask(targets: np.ndarray) -> np.ndarray:
    return (targets == TARGET_NL) | (targets == TARGET_M)


def occurrence_balanced_weights(dataset: Dataset, include_stop: bool = False) -> np.ndarray:
    mask = dataset.y >= 0 if include_stop else hypothesis_eligible_mask(dataset.y)
    weights = np.zeros(len(dataset.y), dtype=np.float64)
    for occurrence in np.unique(dataset.occurrence[mask]):
        indices = np.flatnonzero(mask & (dataset.occurrence == occurrence))
        weights[indices] = 1.0 / len(indices)
    classes = (TARGET_NL, TARGET_M, TARGET_STOP) if include_stop else (TARGET_NL, TARGET_M)
    for target in classes:
        total = weights[dataset.y == target].sum()
        if total > 0.0:
            weights[dataset.y == target] *= 1.0 / total
    weights *= len(classes) / max(weights.sum(), 1e-12)
    return weights


def occurrence_prevalence_weights(dataset: Dataset) -> np.ndarray:
    """Give every forced-phone occurrence one unnormalized unit of mass."""
    weights = np.zeros(len(dataset.y), dtype=np.float64)
    for occurrence in np.unique(dataset.occurrence):
        indices = np.flatnonzero(dataset.occurrence == occurrence)
        if len(indices):
            weights[indices] = 1.0 / len(indices)
    return weights


def sigmoid(value: np.ndarray) -> np.ndarray:
    return np.where(value >= 0.0, 1.0 / (1.0 + np.exp(-value)), np.exp(value) / (1.0 + np.exp(value)))


def fit_logistic(x: np.ndarray, y: np.ndarray, weights: np.ndarray, ridge: float) -> np.ndarray:
    design = np.column_stack((np.ones(len(x)), x))
    beta = np.zeros(design.shape[1], dtype=np.float64)
    penalty = np.eye(design.shape[1], dtype=np.float64) * ridge
    penalty[0, 0] = ridge * 0.05
    for _ in range(80):
        probability = sigmoid(design @ beta)
        gradient = design.T @ (weights * (probability - y)) + penalty @ beta
        curvature = weights * probability * (1.0 - probability)
        hessian = design.T @ (curvature[:, None] * design) + penalty
        step = np.linalg.solve(hessian + np.eye(len(beta)) * 1e-9, gradient)
        beta -= step
        if float(np.max(np.abs(step))) < 1e-9:
            break
    if not np.all(np.isfinite(beta)):
        raise ValueError("Non-finite logistic fit")
    return beta


def expert_priors(dataset: Dataset) -> tuple[np.ndarray, np.ndarray, dict[str, Any]]:
    # Count fractional *phone occurrences*, not frames and not class-normalized
    # training mass. An occurrence contributes one count distributed across
    # the actual Oculus winners it spans. M/NL likelihoods use only their two
    # eligible classes; eligibility reliability uses every forced phone in its
    # denominator, including P/B stops, vowels, silence, and other consonants.
    counts_by_class = np.zeros((2, VISEME_COUNT), dtype=np.float64)
    counts_by_family = np.zeros((4, VISEME_COUNT), dtype=np.float64)
    counts_by_eligibility = np.zeros((2, VISEME_COUNT), dtype=np.float64)
    for occurrence in np.unique(dataset.occurrence):
        indices = np.flatnonzero(dataset.occurrence == occurrence)
        if not len(indices):
            continue
        target_values = np.unique(dataset.y[indices])
        if len(target_values) != 1 or int(target_values[0]) not in (-1, TARGET_NL, TARGET_M, TARGET_STOP):
            raise ValueError("A phone occurrence crossed target classes")
        target = int(target_values[0])
        # Estimate the table for each actual Oculus winner. Runtime then mixes
        # these per-winner likelihoods with Beta common.fast.
        histogram = np.bincount(dataset.winners[indices], minlength=VISEME_COUNT).astype(np.float64)
        fractional = histogram / histogram.sum()
        family = target if target >= 0 else 3
        eligible = target in (TARGET_NL, TARGET_M)
        counts_by_family[family] += fractional
        counts_by_eligibility[int(eligible)] += fractional
        if eligible:
            counts_by_class[target] += fractional

    # The binary face fit is class-balanced, so the Oculus term must also be a
    # likelihood ratio P(winner|M)/P(winner|NL), not a duplicated corpus prior.
    likelihood = np.empty((2, VISEME_COUNT), dtype=np.float64)
    for target in (TARGET_NL, TARGET_M):
        likelihood[target] = (counts_by_class[target] + DIRICHLET_ALPHA) / (
            counts_by_class[target].sum() + DIRICHLET_ALPHA * VISEME_COUNT
        )
    bias = np.zeros(VISEME_COUNT, dtype=np.float64)
    reliability = np.zeros(VISEME_COUNT, dtype=np.float64)
    by_viseme: dict[str, Any] = {}
    for viseme in range(VISEME_COUNT):
        selected = dataset.winners == viseme
        class_counts = counts_by_class[:, viseme]
        family_counts = counts_by_family[:, viseme]
        eligibility_counts = counts_by_eligibility[:, viseme]
        raw_counts = np.array(
            [
                int(np.sum(selected & (dataset.y == TARGET_NL))),
                int(np.sum(selected & (dataset.y == TARGET_M))),
                int(np.sum(selected & (dataset.y == TARGET_STOP))),
                int(np.sum(selected & (dataset.y < 0))),
            ]
        )
        eligibility_posterior = eligibility_counts + DIRICHLET_ALPHA
        bias[viseme] = math.log(likelihood[TARGET_M, viseme] / likelihood[TARGET_NL, viseme])
        reliability[viseme] = eligibility_posterior[1] / eligibility_posterior.sum()
        by_viseme[VISEMES[viseme]] = {
            "fractionalOccurrenceCounts": family_counts.tolist(),
            "rawFrameCounts": raw_counts.tolist(),
            "mVsNlLogLikelihoodRatio": float(bias[viseme]),
            "eligibilityFractionalOccurrenceCounts": {
                "ineligible": float(eligibility_counts[0]),
                "eligible": float(eligibility_counts[1]),
            },
            "eligibilityPosteriorAlpha": [
                float(eligibility_posterior[0]),
                float(eligibility_posterior[1]),
            ],
            "eligibilityPosterior": {
                "alphaEligible": float(eligibility_posterior[1]),
                "betaIneligible": float(eligibility_posterior[0]),
                "mean": float(reliability[viseme]),
                "effectiveOccurrenceSupport": float(eligibility_counts.sum()),
            },
            "reliability": float(reliability[viseme]),
        }
    global_family_counts = counts_by_family.sum(axis=1)
    global_eligible = float(global_family_counts[TARGET_NL] + global_family_counts[TARGET_M])
    global_total = float(global_family_counts.sum())
    audit: dict[str, Any] = {
        "fractionalOccurrenceCountOrder": ["NL", "M", "Stop", "Other"],
        "eligibilityDefinition": "M or N/L forced-phone occurrence; P/B stops and every other forced phone are ineligible",
        "eligibilityBetaPrior": {"alphaEligible": DIRICHLET_ALPHA, "betaIneligible": DIRICHLET_ALPHA},
        "globalFractionalOccurrenceCounts": global_family_counts.tolist(),
        "globalEligibilityRate": global_eligible / max(global_total, 1e-12),
        "minimumPerWinnerFractionalOccurrenceSupport": float(
            np.min(counts_by_eligibility.sum(axis=0))
        ),
        "byViseme": by_viseme,
    }
    return bias, reliability, audit


def binary_metrics(y: np.ndarray, probability: np.ndarray, weights: np.ndarray) -> dict[str, float | int]:
    mask = weights > 0.0
    y = y[mask].astype(np.float64)
    probability = np.clip(probability[mask], 1e-7, 1.0 - 1e-7)
    weights = weights[mask]
    weights = weights / weights.sum()
    prediction = probability >= 0.5
    tp = float(weights[(prediction == 1) & (y == 1)].sum())
    fp = float(weights[(prediction == 1) & (y == 0)].sum())
    fn = float(weights[(prediction == 0) & (y == 1)].sum())
    precision = tp / max(tp + fp, 1e-12)
    recall = tp / max(tp + fn, 1e-12)
    bins = np.minimum((probability * 10).astype(int), 9)
    ece = 0.0
    for bin_index in range(10):
        selected = bins == bin_index
        mass = float(weights[selected].sum())
        if mass > 0.0:
            ece += mass * abs(float(np.sum(weights[selected] * probability[selected]) / mass) - float(np.sum(weights[selected] * y[selected]) / mass))
    return {
        "frames": int(len(y)),
        "brier": float(np.sum(weights * (probability - y) ** 2)),
        "nll": float(-np.sum(weights * (y * np.log(probability) + (1.0 - y) * np.log1p(-probability)))),
        "ece10": float(ece),
        "accuracy": float(np.sum(weights * (prediction == y))),
        "precisionM": precision,
        "recallM": recall,
        "f1M": 2.0 * precision * recall / max(precision + recall, 1e-12),
    }


def evaluate(
    dataset: Dataset,
    features: np.ndarray,
    face_beta: np.ndarray,
    priors: np.ndarray,
    reliability: np.ndarray,
    eligibility_global_prior: float,
) -> dict[str, Any]:
    weights = occurrence_balanced_weights(dataset, include_stop=False)
    eligible = hypothesis_eligible_mask(dataset.y)
    mixed_prior = dataset.observation_weights @ priors
    mixed_reliability = dataset.observation_weights @ reliability
    logits = face_beta[0] + features @ face_beta[1:] + mixed_prior
    probability = sigmoid(logits)
    prior_probability = sigmoid(mixed_prior)
    metrics = binary_metrics(dataset.y, probability, weights)
    baseline = binary_metrics(dataset.y, prior_probability, weights)
    metrics["relativeNllImprovementOverOculusObservation"] = (
        float(baseline["nll"]) - float(metrics["nll"])
    ) / max(float(baseline["nll"]), 1e-12)
    metrics["relativeBrierImprovementOverOculusObservation"] = (
        float(baseline["brier"]) - float(metrics["brier"])
    ) / max(float(baseline["brier"]), 1e-12)
    metrics["reliableCoverageAt025"] = float(
        weights[eligible & (mixed_reliability >= 0.25)].sum() / max(weights[eligible].sum(), 1e-12)
    )
    eligibility_weights = occurrence_prevalence_weights(dataset)
    eligibility_audit = binary_metrics(
        eligible.astype(np.int8), mixed_reliability, eligibility_weights
    )
    eligibility_audit["occurrences"] = int(len(np.unique(dataset.occurrence)))
    eligibility_audit["empiricalEligibilityRate"] = float(
        np.sum(eligibility_weights * eligible) / max(eligibility_weights.sum(), 1e-12)
    )
    eligibility_audit["meanPredictedEligibility"] = float(
        np.sum(eligibility_weights * mixed_reliability)
        / max(eligibility_weights.sum(), 1e-12)
    )
    eligibility_baseline = binary_metrics(
        eligible.astype(np.int8),
        np.full(len(dataset.y), eligibility_global_prior, dtype=np.float64),
        eligibility_weights,
    )
    eligibility_audit["trainingGlobalPrior"] = float(eligibility_global_prior)
    eligibility_audit["globalPriorBaseline"] = eligibility_baseline
    eligibility_audit["relativeNllImprovementOverGlobalPrior"] = (
        float(eligibility_baseline["nll"]) - float(eligibility_audit["nll"])
    ) / max(float(eligibility_baseline["nll"]), 1e-12)
    eligibility_audit["relativeBrierImprovementOverGlobalPrior"] = (
        float(eligibility_baseline["brier"]) - float(eligibility_audit["brier"])
    ) / max(float(eligibility_baseline["brier"]), 1e-12)
    eligibility_by_winner: dict[str, Any] = {}
    for viseme in range(VISEME_COUNT):
        selected = dataset.winners == viseme
        local_weights = eligibility_weights * selected
        support = float(local_weights.sum())
        if support <= 0.0:
            continue
        local = binary_metrics(eligible.astype(np.int8), mixed_reliability, local_weights)
        local["fractionalOccurrenceSupport"] = support
        local["empiricalEligibilityRate"] = float(
            np.sum(local_weights * eligible) / support
        )
        local["meanPredictedEligibility"] = float(
            np.sum(local_weights * mixed_reliability) / support
        )
        eligibility_by_winner[VISEMES[viseme]] = local
    eligibility_audit["byHardOculusWinner"] = eligibility_by_winner
    audits: dict[str, Any] = {}
    for viseme in range(VISEME_COUNT):
        selected = eligible & (dataset.winners == viseme)
        if not np.any(selected):
            continue
        local_weights = weights * selected
        audits[VISEMES[viseme]] = binary_metrics(dataset.y, probability, local_weights)
    cross = eligible & (dataset.winners == corpus.VISEME_INDEX["nn"])
    m_cross = cross & (dataset.y == TARGET_M)
    correction = {
        "mFramesEmittedAsNn": int(m_cross.sum()),
        "nlFramesEmittedAsPp": int(np.sum(eligible & (dataset.winners == corpus.VISEME_INDEX["PP"]) & (dataset.y == TARGET_NL))),
        "mAsNnPredictedMAt050": int(np.sum(m_cross & (probability >= 0.5))),
        "mAsNnMeanPosterior": float(np.mean(probability[m_cross])) if np.any(m_cross) else None,
    }
    return {
        "model": metrics,
        "oculusObservationBaseline": baseline,
        "eligibilityReliability": eligibility_audit,
        "byOculusWinner": audits,
        "crossConfusion": correction,
    }


def features_for_kind(dataset: Dataset, kind: str) -> np.ndarray:
    if kind == "Aperture":
        return dataset.x_aperture
    if kind == "Balanced":
        return dataset.x_balanced
    if kind == "Quality":
        return dataset.x_quality
    raise ValueError(f"Unknown hidden-phone model kind: {kind}")


def axes_for_kind(kind: str) -> tuple[str, ...]:
    if kind == "Aperture":
        return APERTURE_AXES
    if kind == "Balanced":
        return BALANCED_AXES
    if kind == "Quality":
        return QUALITY_AXES
    raise ValueError(f"Unknown hidden-phone model kind: {kind}")


def feature_safe_bounds(kind: str) -> np.ndarray:
    axis_count = len(axes_for_kind(kind))
    # A headroom-normalized current residual is intrinsically in [-1, 1].
    # Differences between two such observer stages can span [-2, 2].
    return np.concatenate(
        (
            np.ones(axis_count, dtype=np.float64),
            np.full(axis_count * 2, RUNTIME_FEATURE_CLAMP, dtype=np.float64),
        )
    )


def train_kind(
    kind: str,
    fit: Dataset,
    development: Dataset,
    heldout: Dataset,
) -> dict[str, Any]:
    fit_x = features_for_kind(fit, kind)
    development_x = features_for_kind(development, kind)
    heldout_x = features_for_kind(heldout, kind)
    fit_weights = occurrence_balanced_weights(fit, include_stop=False)
    binary = hypothesis_eligible_mask(fit.y)
    best: tuple[float, float, np.ndarray, np.ndarray] | None = None
    fit_priors, fit_reliability, fit_prior_audit = expert_priors(fit)
    dev_weights = occurrence_balanced_weights(development, include_stop=False)
    dev_binary = hypothesis_eligible_mask(development.y)
    for ridge in RIDGE_CANDIDATES:
        beta = fit_logistic(fit_x[binary], fit.y[binary].astype(np.float64), fit_weights[binary], ridge)
        face_log_likelihood = beta[0] + development_x @ beta[1:]
        hard_log_likelihood = development.observation_weights @ fit_priors
        calibration_x = np.column_stack((face_log_likelihood, hard_log_likelihood))
        calibration = fit_logistic(
            calibration_x[dev_binary],
            development.y[dev_binary].astype(np.float64),
            dev_weights[dev_binary],
            0.05,
        )
        # Both factors are independently evidential.  A negative fitted slope
        # indicates sampling noise/overfit and is replaced by abstention from
        # that factor rather than reversing its physical meaning.
        calibration[1:] = np.maximum(calibration[1:], 0.0)
        probability = sigmoid(
            calibration[0]
            + calibration[1] * face_log_likelihood
            + calibration[2] * hard_log_likelihood
        )
        nll = float(binary_metrics(development.y, probability, dev_weights)["nll"])
        candidate = (nll, ridge, beta, calibration)
        if best is None or candidate[0] < best[0]:
            best = candidate
    assert best is not None
    selected_ridge = best[1]
    calibration = best[3]

    # Final coefficients use fit+development; held-out speakers/sentences are untouched.
    combined = concatenate_datasets((fit, development))
    combined_x = features_for_kind(combined, kind)
    combined_weights = occurrence_balanced_weights(combined, include_stop=False)
    combined_binary = hypothesis_eligible_mask(combined.y)
    beta = fit_logistic(
        combined_x[combined_binary],
        combined.y[combined_binary].astype(np.float64),
        combined_weights[combined_binary],
        selected_ridge,
    )
    priors, reliability, prior_audit = expert_priors(combined)
    biases = calibration[0] + calibration[1] * beta[0] + calibration[2] * priors
    shared_coefficient = calibration[1] * beta[1:]
    # Kept in expert-shaped storage for the simple generated API.  Every row is
    # bit-identical and the Animator may algebraically collapse it to one
    # visible affine term plus a simplex-weighted bias prior.
    coefficients = np.repeat(shared_coefficient[None, :], VISEME_COUNT, axis=0)
    # Preserve the model-domain OOD statistic over the original M/NL/P/B
    # subset; unrelated phones participate only in eligibility reliability.
    feature_abs_p995 = np.quantile(np.abs(combined_x[combined.y >= 0]), 0.995, axis=0)
    safe = feature_safe_bounds(kind)
    if safe.shape != (coefficients.shape[1],):
        raise ValueError(f"Feature-bound shape mismatch for {kind}")
    conservative_bound = float(np.max(np.abs(biases) + np.sum(np.abs(coefficients) * safe[None, :], axis=1)))
    return {
        "kind": kind,
        "featureAxes": list(axes_for_kind(kind)),
        "featureStages": list(FEATURE_STAGES),
        "featureCount": int(coefficients.shape[1]),
        "selectedRidge": selected_ridge,
        "developmentFactorCalibration": {
            "intercept": float(calibration[0]),
            "faceLogLikelihoodScale": float(calibration[1]),
            "oculusLogLikelihoodScale": float(calibration[2]),
        },
        "bias": biases.tolist(),
        "coefficient": coefficients.tolist(),
        "featureAbsP995": feature_abs_p995.tolist(),
        "featureSafeBound": safe.tolist(),
        "reliability": reliability.tolist(),
        "conservativeLogitBound": conservative_bound,
        "expertAudit": prior_audit,
        "development": evaluate(
            development,
            development_x,
            np.concatenate(
                ((calibration[0] + calibration[1] * best[2][0],), calibration[1] * best[2][1:])
            ),
            calibration[2] * fit_priors,
            fit_reliability,
            fit_prior_audit["globalEligibilityRate"],
        ),
        "heldout": evaluate(
            heldout,
            heldout_x,
            np.concatenate(((calibration[0] + calibration[1] * beta[0],), calibration[1] * beta[1:])),
            calibration[2] * priors,
            reliability,
            prior_audit["globalEligibilityRate"],
        ),
    }


def concatenate_datasets(datasets: Sequence[Dataset]) -> Dataset:
    occurrence: list[np.ndarray] = []
    offset = 0
    for dataset in datasets:
        occurrence.append(dataset.occurrence + offset)
        offset += int(dataset.occurrence.max(initial=-1)) + 1
    return Dataset(
        x_aperture=np.concatenate([value.x_aperture for value in datasets]),
        x_balanced=np.concatenate([value.x_balanced for value in datasets]),
        x_quality=np.concatenate([value.x_quality for value in datasets]),
        y=np.concatenate([value.y for value in datasets]),
        winners=np.concatenate([value.winners for value in datasets]),
        observation_weights=np.concatenate([value.observation_weights for value in datasets]),
        occurrence=np.concatenate(occurrence),
        speakers=np.concatenate([value.speakers for value in datasets]),
        entries=np.concatenate([value.entries for value in datasets]),
    )


def _audit_hash_input(document: dict[str, Any]) -> dict[str, Any]:
    value = dict(document)
    value.pop("contentSha256", None)
    return value


def train(
    cache_dir: Path,
    source_path: Path,
    audio_manifest_path: Path,
    audit_path: Path,
    model_path: Path,
    dll_path: Path,
    transition_path: Path,
) -> None:
    source = corpus.load_json(source_path)
    corpus.validate_source_manifest(source)
    processed = corpus.load_json(cache_dir / "selection_manifest.json")
    corpus.validate_selection_hash(processed)
    transition = load_transition_model(transition_path)
    if audio_manifest_path.is_file():
        audio_manifest = corpus.load_json(audio_manifest_path)
        try:
            validate_audio_manifest(audio_manifest, processed, cache_dir)
        except Exception:
            audio_manifest = build_audio_manifest(source, processed, cache_dir)
            corpus.write_json_atomic(audio_manifest_path, audio_manifest)
    else:
        audio_manifest = build_audio_manifest(source, processed, cache_dir)
        corpus.write_json_atomic(audio_manifest_path, audio_manifest)

    records, oculus_audit = load_records(cache_dir, processed, audio_manifest, dll_path)
    fit_records = [record for record in records if record.split == "fit"]
    development_records = [record for record in records if record.split == "development"]
    heldout_records = [record for record in records if record.split == "heldout"]
    development_calibration, development_evaluation = calibration_partition(development_records)
    heldout_calibration, heldout_evaluation = calibration_partition(heldout_records)

    fit_bounds = speaker_bounds(fit_records)

    final_training_records = fit_records + development_records
    final_bounds = speaker_bounds(final_training_records)
    final_centers = viseme_centers(
        final_training_records, normalize_records(final_training_records, final_bounds)
    )
    # Rebuild both train partitions around the same final centers for the final fit.
    fit_final = make_dataset(fit_records, fit_bounds, final_centers, transition)
    development_final = make_dataset(
        development_evaluation, speaker_bounds(development_calibration), final_centers, transition
    )
    heldout_dataset = make_dataset(
        heldout_evaluation, speaker_bounds(heldout_calibration), final_centers, transition
    )

    aperture = train_kind("Aperture", fit_final, development_final, heldout_dataset)
    balanced = train_kind("Balanced", fit_final, development_final, heldout_dataset)
    quality = train_kind("Quality", fit_final, development_final, heldout_dataset)

    # End-to-end hard-center controls quantify the exact consequence of the
    # former train/runtime mismatch. They keep the same common.fast Oculus
    # observation, splits, calibration, and fitting procedure; only the speech
    # center trajectory changes.
    fit_hard = make_dataset(fit_records, fit_bounds, final_centers, transition, "hard")
    development_hard = make_dataset(
        development_evaluation, speaker_bounds(development_calibration),
        final_centers, transition, "hard"
    )
    heldout_hard = make_dataset(
        heldout_evaluation, speaker_bounds(heldout_calibration),
        final_centers, transition, "hard"
    )
    hard_aperture = train_kind("Aperture", fit_hard, development_hard, heldout_hard)
    hard_balanced = train_kind("Balanced", fit_hard, development_hard, heldout_hard)
    hard_quality = train_kind("Quality", fit_hard, development_hard, heldout_hard)

    def parity_metrics(model: dict[str, Any]) -> dict[str, Any]:
        return {
            key: model["heldout"]["model"][key]
            for key in ("nll", "brier", "ece10", "accuracy", "f1M")
        }

    def cross_domain_metrics(
        model: dict[str, Any], dataset: Dataset, kind: str
    ) -> dict[str, Any]:
        features = features_for_kind(dataset, kind)
        coefficients = np.asarray(model["coefficient"], dtype=np.float64)
        biases = np.asarray(model["bias"], dtype=np.float64)
        reliability = np.asarray(model["reliability"], dtype=np.float64)
        evaluated = evaluate(
            dataset,
            features,
            np.concatenate(((0.0,), coefficients[0])),
            biases,
            reliability,
            float(model["expertAudit"]["globalEligibilityRate"]),
        )
        return {
            key: evaluated["model"][key]
            for key in ("nll", "brier", "ece10", "accuracy", "f1M")
        }

    beta_parity = {
        "description": "The in-domain control changes only the visible speech center from exact Beta group-slow trajectories to the former hard-winner center; both use Beta common.fast observation mixing. The cross-domain row then applies the hard-center-trained emitted coefficients directly to exact-Beta heldout features, quantifying the actual former train/runtime mismatch.",
        "Aperture": {
            "exactBetaRuntimeCenter": parity_metrics(aperture),
            "hardWinnerCenterControl": parity_metrics(hard_aperture),
            "hardCenterModelAppliedToExactBetaFeatures": cross_domain_metrics(
                hard_aperture, heldout_dataset, "Aperture"
            ),
        },
        "Balanced": {
            "exactBetaRuntimeCenter": parity_metrics(balanced),
            "hardWinnerCenterControl": parity_metrics(hard_balanced),
            "hardCenterModelAppliedToExactBetaFeatures": cross_domain_metrics(
                hard_balanced, heldout_dataset, "Balanced"
            ),
        },
        "Quality": {
            "exactBetaRuntimeCenter": parity_metrics(quality),
            "hardWinnerCenterControl": parity_metrics(hard_quality),
            "hardCenterModelAppliedToExactBetaFeatures": cross_domain_metrics(
                hard_quality, heldout_dataset, "Quality"
            ),
        },
    }
    for kind in ("Aperture", "Balanced", "Quality"):
        exact = beta_parity[kind]["exactBetaRuntimeCenter"]
        hard = beta_parity[kind]["hardWinnerCenterControl"]
        beta_parity[kind]["exactMinusHard"] = {
            key: float(exact[key]) - float(hard[key]) for key in exact
        }
        cross = beta_parity[kind]["hardCenterModelAppliedToExactBetaFeatures"]
        beta_parity[kind]["crossDomainMinusExact"] = {
            key: float(cross[key]) - float(exact[key]) for key in exact
        }
    document: dict[str, Any] = {
        "schemaVersion": 1,
        "modelVersion": MODEL_VERSION,
        "contentSha256": None,
        "provenance": {
            "dataset": source["dataset"],
            "processedSelectionContentSha256": processed["selectionContentSha256"],
            "audioSelectionContentSha256": audio_manifest["selectionContentSha256"],
            "audioArchiveSha256": EXPECTED_AUDIO_ARCHIVE_SHA256,
            "trainerSha256": sha256_file(Path(__file__).resolve()),
            "ovrLipSyncDllSha256": sha256_file(dll_path),
            "ovrLipSync": oculus_audit,
            "transitionRetentionContentSha256": transition.content_sha256,
            "transitionRetentionAuditSha256": transition.file_sha256,
        },
        "analysis": {
            "sourceAudio": "16 kHz mono PCM16",
            "resampling": "deterministic linear interpolation 16 kHz -> 48 kHz",
            "sampleRateHz": ANALYSIS_SAMPLE_RATE,
            "bufferSamples": ANALYSIS_BUFFER_SAMPLES,
            "bufferSeconds": ANALYSIS_BUFFER_SAMPLES / ANALYSIS_SAMPLE_RATE,
            "provider": "Enhanced",
            "providerValue": OVR_PROVIDER_ENHANCED,
            "visemeSmoothing": OVR_SMOOTHING,
            "timing": "Oculus result is timestamped at DSP-buffer completion minus reported frameDelay; each EMA frame uses only the newest result already available.",
            "emaTrim": "begin_end is applied in the original synchronized 100 Hz/audio clock before pairing after resampling.",
            "betaRuntimeParity": {
                "clockHz": corpus.EXPECTED_SAMPLE_RATE_HZ,
                "resetSimplex": "raw/viseme-fast/viseme-slow/context start at sil=1 and all other weights=0; visible residual observer poles start at 0",
                "visemeObserver": "a=1-exp(-dt/0.024); fast+=a*(raw-fast); slow+=a*(fast-slow)",
                "contextObserver": "for each group g, a_g=1-exp(-dt/decay_g); context_g+=a_g*(raw-context_g)",
                "retention": "r_g = sum_previous sum_current context_g[previous] * R_g[previous,current] * fast[current]",
                "groupSlow": "lead_g=clamp(1-r_g,0,1); groupSlow_g=slow+lead_g*(fast-slow); Jaw axes use Jaw group and lip axes use Lips group",
                "commonFastObservation": "meanRetention=mean_g(r_g); commonFast=fast+clamp(1-meanRetention,0,1)*(raw-fast); generated biases/reliabilities are simplex-mixed with commonFast",
                "strength": 1.0,
                "groupOrder": list(transition.groups),
                "decaySeconds": transition.decay_seconds.tolist(),
            },
        },
        "training": {
            "labels": {
                "MCompatible": sorted(M_PHONES),
                "NLCompatible": sorted(NL_PHONES),
                "StopAmbiguity": sorted(STOP_PHONES),
                "Other": "every other forced-phone occurrence, including vowels, unrelated consonants, and silence",
            },
            "posteriorSemantics": "positive logit is M-compatible; negative is N/L-compatible",
            "factorization": "class-balanced shared visible-face log-likelihood ratio plus a Beta-common.fast mixture of Dirichlet-smoothed per-winner log P(Oculus winner|M)/P(Oculus winner|N/L)",
            "counting": "Logistic fit/evaluation use class-balanced M/NL occurrence weights. Per-winner Oculus likelihood uses unnormalized M/NL fractional occurrence counts. Eligibility Reliability uses every forced-phone occurrence in its denominator: each occurrence contributes exactly one count distributed across its hard winners. Runtime/evaluation simplex-mix both tables with exact Beta common.fast.",
            "reliabilitySemantics": "Dirichlet-smoothed P(the M-versus-N/L hypothesis is eligible | observed hard Oculus winner), with M/N/L eligible and P/B stops plus all other forced phones ineligible",
            "stopHandling": "P/B stops never become N negatives; like vowels, silence, and unrelated consonants, they lower eligibility Reliability",
            "dirichletAlpha": DIRICHLET_ALPHA,
            "observerResponseSeconds": OBSERVER_RESPONSE_SECONDS,
            "headroomFloor": HEADROOM_FLOOR,
            "runtimeFeatureClamp": [-RUNTIME_FEATURE_CLAMP, RUNTIME_FEATURE_CLAMP],
            "calibrationQuantiles": list(CALIBRATION_QUANTILES),
            "ridgeCandidates": list(RIDGE_CANDIDATES),
            "splits": {
                "fit": {"speakers": sorted({record.speaker for record in fit_records}), "utterances": len(fit_records)},
                "development": {"speakers": sorted({record.speaker for record in development_records}), "calibrationUtterances": len(development_calibration), "evaluationUtterances": len(development_evaluation)},
                "heldout": {"speakers": sorted({record.speaker for record in heldout_records}), "calibrationUtterances": len(heldout_calibration), "evaluationUtterances": len(heldout_evaluation)},
            },
        },
        "models": {"Aperture": aperture, "Balanced": balanced, "Quality": quality},
        "hardCenterParityControl": beta_parity,
        "limitations": [
            "The corpus supplies EMA and forced phone labels, not Unified Expressions; runtime channel calibration is a domain-transfer proxy.",
            "A closed mouth is evidence for bilabial place, not proof of /m/: /p/ and /b/ share closure, so Reliability must gate any correction.",
            "The hard Oculus winner and visible face cannot guarantee recovery of a phone distinction that was discarded; outputs are calibrated compatibility posteriors, never measurements.",
            "Only causal current and observer-history features are used; there is no future-phone lookahead.",
        ],
    }
    document["contentSha256"] = corpus.canonical_sha256(_audit_hash_input(document))
    corpus.write_json_atomic(audit_path, document)
    generate_cs(document, model_path)
    print(f"Wrote {audit_path}", flush=True)
    print(f"Wrote {model_path}", flush=True)


def float_literal(value: float) -> str:
    if not math.isfinite(value):
        raise ValueError("Cannot emit non-finite C# coefficient")
    return f"{np.float32(value):.9f}f"


def emit_array(lines: list[str], name: str, values: Iterable[float], indent: str = "        ") -> None:
    flattened = list(values)
    lines.append(f"{indent}private static readonly float[] {name} =")
    lines.append(f"{indent}{{")
    for start in range(0, len(flattened), 8):
        lines.append(indent + "    " + ", ".join(float_literal(value) for value in flattened[start : start + 8]) + ",")
    lines.append(f"{indent}}};")
    lines.append("")


def generate_cs(document: dict[str, Any], model_path: Path) -> None:
    expected = corpus.canonical_sha256(_audit_hash_input(document))
    if document.get("contentSha256") != expected:
        raise ValueError("Audit JSON content hash mismatch")
    models = document["models"]
    aperture = models["Aperture"]
    balanced = models["Balanced"]
    quality = models["Quality"]
    lines = [
        "// <auto-generated>",
        "// Trained by Tools/AdvancedVisemeTraining/train_hidden_phone_posterior.py.",
        "// Source: SPIRE EMA Corpus, CC BY 4.0, pinned revision 55f21628de95514e3ff22eaccc75e1547d181297.",
        "// Bandekar, Udupa, and Ghosh (2024), doi:10.21437/Interspeech.2024-1756.",
        "// Positive logit / share above 0.5 favors M-compatible; negative logit / share below 0.5 favors N/L-compatible.",
        "// This is an abstaining statistical posterior, not recovered ground-truth phoneme identity.",
        "// </auto-generated>",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "namespace YUCP.Components",
        "{",
        "    public enum AdvancedVisemeHiddenPhoneModelKind",
        "    {",
        "        Aperture = 0,",
        "        Balanced = 1,",
        "        Quality = 2,",
        "    }",
        "",
        "    public static class AdvancedVisemeHiddenPhonePosterior",
        "    {",
        f"        public const int ModelVersion = {int(document['modelVersion'])};",
        f"        public const string ContentSha256 = \"{document['contentSha256']}\";",
        f"        public const float ObserverResponseSeconds = {float_literal(OBSERVER_RESPONSE_SECONDS)};",
        "        public const int VisemeCount = 15;",
        "        private const int ApertureFeatureCount = 6;",
        "        private const int BalancedFeatureCount = 9;",
        "        private const int QualityFeatureCount = 12;",
        "",
    ]
    emit_array(lines, "ApertureBiasValues", aperture["bias"])
    emit_array(lines, "BalancedBiasValues", balanced["bias"])
    emit_array(lines, "QualityBiasValues", quality["bias"])
    emit_array(lines, "ApertureCoefficientValues", np.asarray(aperture["coefficient"]).ravel())
    emit_array(lines, "BalancedCoefficientValues", np.asarray(balanced["coefficient"]).ravel())
    emit_array(lines, "QualityCoefficientValues", np.asarray(quality["coefficient"]).ravel())
    emit_array(lines, "ApertureReliabilityValues", aperture["reliability"])
    emit_array(lines, "BalancedReliabilityValues", balanced["reliability"])
    emit_array(lines, "QualityReliabilityValues", quality["reliability"])
    emit_array(lines, "ApertureFeatureAbsP995Values", aperture["featureAbsP995"])
    emit_array(lines, "BalancedFeatureAbsP995Values", balanced["featureAbsP995"])
    emit_array(lines, "QualityFeatureAbsP995Values", quality["featureAbsP995"])
    emit_array(lines, "ApertureFeatureSafeBoundValues", aperture["featureSafeBound"])
    emit_array(lines, "BalancedFeatureSafeBoundValues", balanced["featureSafeBound"])
    emit_array(lines, "QualityFeatureSafeBoundValues", quality["featureSafeBound"])
    lines.extend(
        [
            "        public static int FeatureCount(AdvancedVisemeHiddenPhoneModelKind kind)",
            "        {",
            "            switch (kind)",
            "            {",
            "                case AdvancedVisemeHiddenPhoneModelKind.Aperture: return ApertureFeatureCount;",
            "                case AdvancedVisemeHiddenPhoneModelKind.Balanced: return BalancedFeatureCount;",
            "                case AdvancedVisemeHiddenPhoneModelKind.Quality: return QualityFeatureCount;",
            "                default: throw new ArgumentOutOfRangeException(nameof(kind));",
            "            }",
            "        }",
            "",
            "        public static float Bias(AdvancedVisemeHiddenPhoneModelKind kind, int viseme)",
            "        {",
            "            RequireIndex(viseme, VisemeCount, nameof(viseme));",
            "            switch (kind)",
            "            {",
            "                case AdvancedVisemeHiddenPhoneModelKind.Aperture: return ApertureBiasValues[viseme];",
            "                case AdvancedVisemeHiddenPhoneModelKind.Balanced: return BalancedBiasValues[viseme];",
            "                case AdvancedVisemeHiddenPhoneModelKind.Quality: return QualityBiasValues[viseme];",
            "                default: throw new ArgumentOutOfRangeException(nameof(kind));",
            "            }",
            "        }",
            "",
            "        public static float Coefficient(AdvancedVisemeHiddenPhoneModelKind kind, int viseme, int feature)",
            "        {",
            "            RequireIndex(viseme, VisemeCount, nameof(viseme));",
            "            var count = FeatureCount(kind);",
            "            RequireIndex(feature, count, nameof(feature));",
            "            float[] values;",
            "            switch (kind)",
            "            {",
            "                case AdvancedVisemeHiddenPhoneModelKind.Aperture: values = ApertureCoefficientValues; break;",
            "                case AdvancedVisemeHiddenPhoneModelKind.Balanced: values = BalancedCoefficientValues; break;",
            "                case AdvancedVisemeHiddenPhoneModelKind.Quality: values = QualityCoefficientValues; break;",
            "                default: throw new ArgumentOutOfRangeException(nameof(kind));",
            "            }",
            "            return values[viseme * count + feature];",
            "        }",
            "",
            "        public static float Reliability(AdvancedVisemeHiddenPhoneModelKind kind, int viseme)",
            "        {",
            "            RequireIndex(viseme, VisemeCount, nameof(viseme));",
            "            switch (kind)",
            "            {",
            "                case AdvancedVisemeHiddenPhoneModelKind.Aperture: return ApertureReliabilityValues[viseme];",
            "                case AdvancedVisemeHiddenPhoneModelKind.Balanced: return BalancedReliabilityValues[viseme];",
            "                case AdvancedVisemeHiddenPhoneModelKind.Quality: return QualityReliabilityValues[viseme];",
            "                default: throw new ArgumentOutOfRangeException(nameof(kind));",
            "            }",
            "        }",
            "",
            "        public static float FeatureAbsP995(AdvancedVisemeHiddenPhoneModelKind kind, int feature)",
            "        {",
            "            RequireIndex(feature, FeatureCount(kind), nameof(feature));",
            "            switch (kind)",
            "            {",
            "                case AdvancedVisemeHiddenPhoneModelKind.Aperture: return ApertureFeatureAbsP995Values[feature];",
            "                case AdvancedVisemeHiddenPhoneModelKind.Balanced: return BalancedFeatureAbsP995Values[feature];",
            "                case AdvancedVisemeHiddenPhoneModelKind.Quality: return QualityFeatureAbsP995Values[feature];",
            "                default: throw new ArgumentOutOfRangeException(nameof(kind));",
            "            }",
            "        }",
            "",
            "        public static float FeatureSafeBound(AdvancedVisemeHiddenPhoneModelKind kind, int feature)",
            "        {",
            "            RequireIndex(feature, FeatureCount(kind), nameof(feature));",
            "            switch (kind)",
            "            {",
            "                case AdvancedVisemeHiddenPhoneModelKind.Aperture: return ApertureFeatureSafeBoundValues[feature];",
            "                case AdvancedVisemeHiddenPhoneModelKind.Balanced: return BalancedFeatureSafeBoundValues[feature];",
            "                case AdvancedVisemeHiddenPhoneModelKind.Quality: return QualityFeatureSafeBoundValues[feature];",
            "                default: throw new ArgumentOutOfRangeException(nameof(kind));",
            "            }",
            "        }",
            "",
            "        public static float ConservativeLogitBound(AdvancedVisemeHiddenPhoneModelKind kind)",
            "        {",
            "            switch (kind)",
            "            {",
            f"                case AdvancedVisemeHiddenPhoneModelKind.Aperture: return {float_literal(aperture['conservativeLogitBound'])};",
            f"                case AdvancedVisemeHiddenPhoneModelKind.Balanced: return {float_literal(balanced['conservativeLogitBound'])};",
            f"                case AdvancedVisemeHiddenPhoneModelKind.Quality: return {float_literal(quality['conservativeLogitBound'])};",
            "                default: throw new ArgumentOutOfRangeException(nameof(kind));",
            "            }",
            "        }",
            "",
            "        public static float PredictLogit(AdvancedVisemeHiddenPhoneModelKind kind, IReadOnlyList<float> visemeWeights, IReadOnlyList<float> features)",
            "        {",
            "            if (visemeWeights == null || visemeWeights.Count != VisemeCount)",
            "                throw new ArgumentException(\"Prediction requires 15 viseme weights.\", nameof(visemeWeights));",
            "            var featureCount = FeatureCount(kind);",
            "            if (features == null || features.Count != featureCount)",
            "                throw new ArgumentException(\"Prediction feature count does not match the model.\", nameof(features));",
            "            var sum = 0f;",
            "            for (var viseme = 0; viseme < VisemeCount; viseme++) sum += Math.Max(0f, visemeWeights[viseme]);",
            "            var fallback = sum <= 1e-8f;",
            "            if (fallback) sum = 1f;",
            "            var logit = 0f;",
            "            for (var viseme = 0; viseme < VisemeCount; viseme++)",
            "            {",
            "                var weight = fallback ? (viseme == 0 ? 1f : 0f) : Math.Max(0f, visemeWeights[viseme]) / sum;",
            "                if (weight <= 0f) continue;",
            "                var expert = Bias(kind, viseme);",
            "                for (var feature = 0; feature < featureCount; feature++)",
            "                {",
            "                    var safeBound = FeatureSafeBound(kind, feature);",
            "                    expert += Math.Max(-safeBound, Math.Min(safeBound, features[feature])) * Coefficient(kind, viseme, feature);",
            "                }",
            "                logit += weight * expert;",
            "            }",
            "            var bound = ConservativeLogitBound(kind);",
            "            return Math.Max(-bound, Math.Min(bound, logit));",
            "        }",
            "",
            "        public static float PredictShare(AdvancedVisemeHiddenPhoneModelKind kind, IReadOnlyList<float> visemeWeights, IReadOnlyList<float> features)",
            "        {",
            "            var logit = PredictLogit(kind, visemeWeights, features);",
            "            return logit >= 0f ? 1f / (1f + (float)Math.Exp(-logit)) : (float)Math.Exp(logit) / (1f + (float)Math.Exp(logit));",
            "        }",
            "",
            "        private static void ValidateKind(AdvancedVisemeHiddenPhoneModelKind kind)",
            "        {",
            "            switch (kind)",
            "            {",
            "                case AdvancedVisemeHiddenPhoneModelKind.Aperture:",
            "                case AdvancedVisemeHiddenPhoneModelKind.Balanced:",
            "                case AdvancedVisemeHiddenPhoneModelKind.Quality:",
            "                    return;",
            "                default:",
            "                    throw new ArgumentOutOfRangeException(nameof(kind));",
            "            }",
            "        }",
            "",
            "        private static void RequireIndex(int value, int count, string name)",
            "        {",
            "            if ((uint)value >= (uint)count) throw new ArgumentOutOfRangeException(name);",
            "        }",
            "    }",
            "}",
        ]
    )
    corpus.write_text_atomic(model_path, "\n".join(lines) + "\n")


def default_cache_dir() -> Path:
    local = os.environ.get("LOCALAPPDATA")
    if not local:
        raise RuntimeError("LOCALAPPDATA is not defined; pass --cache-dir")
    return Path(local) / "YUCP" / "AdvancedVisemeTraining" / "SPIRE_EMA_CORPUS"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("manifest", "train", "all", "generate"))
    parser.add_argument("--cache-dir", type=Path, default=default_cache_dir())
    parser.add_argument("--source-manifest", type=Path, default=DEFAULT_SOURCE_MANIFEST)
    parser.add_argument("--audio-manifest", type=Path, default=DEFAULT_AUDIO_MANIFEST)
    parser.add_argument("--audit-json", type=Path, default=DEFAULT_AUDIT_JSON)
    parser.add_argument("--model-cs", type=Path, default=DEFAULT_MODEL_CS)
    parser.add_argument("--ovr-dll", type=Path, default=DEFAULT_OVR_DLL)
    parser.add_argument("--transition-audit", type=Path, default=DEFAULT_TRANSITION_AUDIT)
    args = parser.parse_args()
    if args.command == "generate":
        document = corpus.load_json(args.audit_json)
        # A generator-only safety/API change must update the provenance and
        # model content hash without pretending the fitted coefficients were
        # retrained.  Repeating this operation is byte-idempotent.
        document["provenance"]["trainerSha256"] = sha256_file(Path(__file__).resolve())
        document["contentSha256"] = corpus.canonical_sha256(_audit_hash_input(document))
        corpus.write_json_atomic(args.audit_json, document)
        generate_cs(document, args.model_cs)
        return
    if args.command == "manifest":
        source = corpus.load_json(args.source_manifest)
        corpus.validate_source_manifest(source)
        processed = corpus.load_json(args.cache_dir / "selection_manifest.json")
        corpus.validate_selection_hash(processed)
        audio_manifest = build_audio_manifest(source, processed, args.cache_dir)
        corpus.write_json_atomic(args.audio_manifest, audio_manifest)
        print(f"Wrote {args.audio_manifest}")
        return
    train(
        args.cache_dir,
        args.source_manifest,
        args.audio_manifest,
        args.audit_json,
        args.model_cs,
        args.ovr_dll,
        args.transition_audit,
    )


if __name__ == "__main__":
    main()
