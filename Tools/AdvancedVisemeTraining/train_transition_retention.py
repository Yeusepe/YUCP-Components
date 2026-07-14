#!/usr/bin/env python3
"""Derive avatar-portable viseme transition retention from the SPIRE EMA corpus.

The fitted values are dimensionless. They describe how much of an avatar's
authored *previous* viseme pose should remain during the beginning of the
current viseme, independently for jaw, lips, tongue tip, and tongue body.
No corpus coordinate or speaker-specific face geometry is shipped at runtime.
"""

from __future__ import annotations

import argparse
import _codecs
import hashlib
import io
import json
import math
import os
import pickle
import re
import sys
import threading
import time
import zipfile
import zlib
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Sequence

import numpy as np


SCRIPT_DIR = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_SOURCE_MANIFEST = SCRIPT_DIR / "source_manifest.json"
DEFAULT_MODEL_JSON = SCRIPT_DIR / "Generated" / "advanced_viseme_transition_retention.json"
DEFAULT_SELECTION_JSON = SCRIPT_DIR / "Generated" / "spire_selection_manifest.json"
DEFAULT_MODEL_CS = (
    REPOSITORY_ROOT
    / "Packages"
    / "com.yucp.components"
    / "Runtime"
    / "Components"
    / "Data"
    / "Generated"
    / "AdvancedVisemeTransitionRetention.generated.cs"
)

VISEMES = ("sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U")
VISEME_INDEX = {name: index for index, name in enumerate(VISEMES)}

GROUPS = (
    ("Jaw", (4, 5)),
    ("Lips", (0, 1, 2, 3)),
    ("TongueTip", (6, 7)),
    ("TongueBody", (8, 9, 10, 11)),
)

# ARPAbet/TIMIT phones to the closest Oculus/VRChat hard-viseme class. Closures
# retain their place of articulation. Diphthongs are expanded below so their
# articulatory movement is not collapsed into a single static class.
PHONE_TO_VISEME = {
    "sil": "sil",
    "h#": "sil",
    "pau": "sil",
    "epi": "sil",
    "sp": "sil",
    "q": "sil",
    "cl": "sil",
    "b": "PP",
    "p": "PP",
    "m": "PP",
    "em": "PP",
    "bcl": "PP",
    "pcl": "PP",
    "f": "FF",
    "v": "FF",
    "th": "TH",
    "dh": "TH",
    "d": "DD",
    "t": "DD",
    "dx": "DD",
    "dcl": "DD",
    "tcl": "DD",
    "g": "kk",
    "k": "kk",
    "ng": "kk",
    "eng": "kk",
    "gcl": "kk",
    "kcl": "kk",
    "ch": "CH",
    "jh": "CH",
    "sh": "CH",
    "zh": "CH",
    "s": "SS",
    "z": "SS",
    "n": "nn",
    "l": "nn",
    "nx": "nn",
    "en": "nn",
    "el": "nn",
    "r": "RR",
    "er": "RR",
    "axr": "RR",
    "aa": "aa",
    "ae": "aa",
    "ah": "aa",
    "ax": "aa",
    "ax-h": "aa",
    "eh": "E",
    "ih": "I",
    "ix": "I",
    "iy": "I",
    "ao": "O",
    "uh": "U",
    "uw": "U",
    "ux": "U",
    "w": "U",
    "y": "I",
}

DIPHTHONG_TO_VISEMES = {
    "aw": ("aa", "U"),
    "ay": ("aa", "I"),
    "ey": ("E", "I"),
    "ow": ("O", "U"),
    "oy": ("O", "I"),
}

CONTEXTUAL_GLOTTAL_PHONES = {"hh", "hv"}
# Filenames use several source/session markers (including f, m, and u). Their
# final four digits are the common MOCHA-TIMIT prompt index; the directory is
# the stable speaker identity.
ARCHIVE_ENTRY_RE = re.compile(r"^processed/spk(?P<speaker>\d+)/.+?(?P<prompt>\d{4})\.pt$", re.IGNORECASE)
NUMPY_PAYLOAD_KEY = "ema_trimmed_and_normalised_with_6_articulators"

EXPECTED_REPOSITORY = "SpireLab/SPIRE_EMA_CORPUS"
EXPECTED_REPOSITORY_REVISION = "55f21628de95514e3ff22eaccc75e1547d181297"
EXPECTED_ARCHIVE_NAME = "processed.zip"
EXPECTED_ARCHIVE_BYTES = 3_086_792_402
EXPECTED_ARCHIVE_SHA256 = "ea1c3440af2b69cef0765b97e1e533ea72dae6029c49a175e1a93761c4236d04"
EXPECTED_ARCHIVE_PT_ENTRIES = 17_480
EXPECTED_SAMPLE_RATE_HZ = 100.0

MAX_JSON_BYTES = 16 * 1024 * 1024
MAX_ARCHIVE_MEMBERS = 20_000
MAX_SELECTED_ENTRIES = 2_048
MAX_SELECTED_COMPRESSED_BYTES = 512 * 1024 * 1024
MAX_PT_BYTES = 2 * 1024 * 1024
MAX_INNER_ZIP_MEMBERS = 16
MAX_INNER_UNCOMPRESSED_BYTES = 4 * 1024 * 1024
MAX_PICKLE_BYTES = 2 * 1024 * 1024
MAX_FRAMES = 4_096
MAX_PHONES = 512
MAX_PHONE_LABEL_LENGTH = 32


@dataclass
class Segment:
    viseme: int
    start: int
    end: int


@dataclass
class Utterance:
    split: str
    speaker: int
    prompt: int
    source_entry: str
    ema: np.ndarray
    segments: list[Segment]


class RestrictedNumpyUnpickler(pickle.Unpickler):
    """Load the corpus' NumPy-only payload without permitting arbitrary globals."""

    def find_class(self, module: str, name: str) -> Any:
        key = (module, name)
        if key == ("numpy.core.multiarray", "_reconstruct"):
            # The corpus was written with NumPy 1.x. NumPy 2.x retains the
            # implementation under _core while accepting the old pickle name.
            return np._core.multiarray._reconstruct  # type: ignore[attr-defined]
        if key == ("numpy", "ndarray"):
            return np.ndarray
        if key == ("numpy", "dtype"):
            return np.dtype
        if key == ("_codecs", "encode"):
            return _codecs.encode
        raise pickle.UnpicklingError(f"Disallowed pickle global: {module}.{name}")


def load_json(path: Path) -> dict[str, Any]:
    if not path.is_file():
        raise FileNotFoundError(path)
    if path.stat().st_size > MAX_JSON_BYTES:
        raise ValueError(f"JSON file exceeds {MAX_JSON_BYTES} bytes: {path}")
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object in {path}")
    return value


def validate_source_manifest(source: dict[str, Any]) -> None:
    if source.get("schemaVersion") != 1:
        raise ValueError("Unsupported source manifest schemaVersion")
    dataset = source.get("dataset")
    subset = source.get("subset")
    derivation = source.get("derivation")
    if not isinstance(dataset, dict) or not isinstance(subset, dict) or not isinstance(derivation, dict):
        raise ValueError("Source manifest requires dataset, subset, and derivation objects")

    expected_dataset_values = {
        "repository": EXPECTED_REPOSITORY,
        "repositoryRevision": EXPECTED_REPOSITORY_REVISION,
        "processedArchive": EXPECTED_ARCHIVE_NAME,
        "processedArchiveBytes": EXPECTED_ARCHIVE_BYTES,
        "processedArchiveSha256": EXPECTED_ARCHIVE_SHA256,
        "license": "CC-BY-4.0",
    }
    for key, expected in expected_dataset_values.items():
        if dataset.get(key) != expected:
            raise ValueError(f"Source manifest {key} does not match the pinned constant")
    if float(dataset.get("sampleRateHz", -1.0)) != EXPECTED_SAMPLE_RATE_HZ:
        raise ValueError("Source manifest sampleRateHz does not match processed tensor timing")
    expected_sensors = [
        "UpperLipX", "UpperLipY", "LowerLipX", "LowerLipY", "JawX", "JawY",
        "TongueTipX", "TongueTipY", "TongueBodyX", "TongueBodyY",
        "TongueDorsumX", "TongueDorsumY",
    ]
    if dataset.get("sensorOrder") != expected_sensors:
        raise ValueError("Source manifest sensorOrder does not match the trainer constants")

    if derivation.get("visemeOrder") != list(VISEMES):
        raise ValueError("Source manifest viseme order does not match the runtime order")
    expected_groups = {name: list(columns) for name, columns in GROUPS}
    if derivation.get("articulatorGroups") != expected_groups:
        raise ValueError("Source manifest articulator groups do not match the trainer constants")
    maximum_window = int(derivation.get("maximumTransitionWindowFrames", 0))
    minimum_samples = int(derivation.get("minimumDirectTransitionSamples", 0))
    if not 1 <= maximum_window <= 64:
        raise ValueError("maximumTransitionWindowFrames must be in [1,64]")
    if not 1 <= minimum_samples <= 65_535:
        raise ValueError("minimumDirectTransitionSamples must be in [1,65535]")
    for key in ("decayFrameCandidates", "priorStrengthCandidates"):
        values = derivation.get(key)
        if not isinstance(values, list) or not values or len(values) > 64:
            raise ValueError(f"{key} must contain 1-64 values")
        if any(not math.isfinite(float(value)) or float(value) <= 0.0 for value in values):
            raise ValueError(f"{key} must contain finite positive values")
    if not isinstance(derivation.get("labelSemantics"), str) or not derivation["labelSemantics"].strip():
        raise ValueError("Source manifest must document labelSemantics")

    permutation = subset.get("promptPermutation")
    splits = subset.get("splits")
    if not isinstance(permutation, dict) or not isinstance(splits, list):
        raise ValueError("Source manifest subset requires promptPermutation and splits")
    modulus = int(permutation.get("modulus", 0))
    multiplier = int(permutation.get("multiplier", 0))
    offset = int(permutation.get("offset", -1))
    if modulus != 460 or not 0 <= offset < modulus or math.gcd(multiplier, modulus) != 1:
        raise ValueError("Invalid prompt permutation")
    if [split.get("name") for split in splits if isinstance(split, dict)] != ["fit", "development", "heldout"]:
        raise ValueError("Source manifest requires fit, development, and heldout splits in order")

    all_speakers: set[int] = set()
    all_ordinals: set[int] = set()
    requested_count = 0
    for split in splits:
        speakers = split.get("speakers")
        start = int(split.get("promptOrdinalStart", -1))
        count = int(split.get("promptOrdinalCount", 0))
        if not isinstance(speakers, list) or not speakers or count <= 0 or start < 0 or start + count > modulus:
            raise ValueError(f"Invalid subset split {split.get('name')!r}")
        normalized_speakers = [int(speaker) for speaker in speakers]
        if len(set(normalized_speakers)) != len(normalized_speakers):
            raise ValueError(f"Duplicate speaker in split {split['name']}")
        if any(speaker < 1 or speaker > 38 for speaker in normalized_speakers):
            raise ValueError(f"Speaker outside corpus range in split {split['name']}")
        if all_speakers.intersection(normalized_speakers):
            raise ValueError("Dataset splits must be speaker-disjoint")
        ordinals = set(range(start, start + count))
        if all_ordinals.intersection(ordinals):
            raise ValueError("Dataset splits must be sentence-disjoint")
        all_speakers.update(normalized_speakers)
        all_ordinals.update(ordinals)
        requested_count += len(normalized_speakers) * count
    if requested_count > MAX_SELECTED_ENTRIES:
        raise ValueError("Source manifest selects too many corpus entries")


def write_text_atomic(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    temporary.write_text(text, encoding="utf-8", newline="\n")
    os.replace(temporary, path)


def write_json_atomic(path: Path, value: Any) -> None:
    write_text_atomic(path, json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n")


def canonical_sha256(value: Any) -> str:
    payload = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def archive_url(source: dict[str, Any]) -> str:
    dataset = source["dataset"]
    return (
        "https://huggingface.co/datasets/"
        f"{dataset['repository']}/resolve/{dataset['repositoryRevision']}/"
        f"{dataset['processedArchive']}?download=true"
    )


def prompt_id(source: dict[str, Any], ordinal: int) -> int:
    permutation = source["subset"]["promptPermutation"]
    modulus = int(permutation["modulus"])
    multiplier = int(permutation["multiplier"])
    offset = int(permutation["offset"])
    if math.gcd(multiplier, modulus) != 1:
        raise ValueError("Prompt permutation multiplier must be coprime with its modulus")
    return ((ordinal * multiplier + offset) % modulus) + 1


def requested_samples(source: dict[str, Any]) -> list[dict[str, Any]]:
    requested: list[dict[str, Any]] = []
    seen: set[tuple[int, int]] = set()
    for split in source["subset"]["splits"]:
        start = int(split["promptOrdinalStart"])
        count = int(split["promptOrdinalCount"])
        for speaker in split["speakers"]:
            speaker = int(speaker)
            for ordinal in range(start, start + count):
                prompt = prompt_id(source, ordinal)
                key = (speaker, prompt)
                if key in seen:
                    raise ValueError(f"Duplicate selected sample spk{speaker}/prompt{prompt:04d}")
                seen.add(key)
                requested.append(
                    {
                        "split": str(split["name"]),
                        "speaker": speaker,
                        "prompt": prompt,
                        "promptOrdinal": ordinal,
                    }
                )
    return requested


def index_selected_entries(remote_zip: Any, source: dict[str, Any]) -> dict[str, Any]:
    by_key: dict[tuple[int, int], Any] = {}
    archive_pt_count = 0
    archive_infos = remote_zip.infolist()
    if len(archive_infos) > MAX_ARCHIVE_MEMBERS:
        raise ValueError(f"Archive has too many members: {len(archive_infos)}")
    for info in archive_infos:
        match = ARCHIVE_ENTRY_RE.match(info.filename)
        if match is None:
            continue
        archive_pt_count += 1
        key = (int(match.group("speaker")), int(match.group("prompt")))
        if key in by_key:
            raise ValueError(f"Duplicate archive entry for speaker/prompt {key}")
        by_key[key] = info

    entries: list[dict[str, Any]] = []
    for sample in requested_samples(source):
        key = (sample["speaker"], sample["prompt"])
        info = by_key.get(key)
        if info is None:
            raise FileNotFoundError(f"Selected sample is absent from archive: spk{key[0]}, prompt {key[1]:04d}")
        if not 0 < int(info.file_size) <= MAX_PT_BYTES:
            raise ValueError(f"Selected archive member exceeds size cap: {info.filename}")
        if int(info.compress_size) < 0:
            raise ValueError(f"Selected archive member has an invalid compressed size: {info.filename}")
        entries.append(
            {
                **sample,
                "entry": info.filename,
                "uncompressedBytes": int(info.file_size),
                "compressedBytes": int(info.compress_size),
                "crc32": f"{int(info.CRC):08x}",
            }
        )

    if archive_pt_count != EXPECTED_ARCHIVE_PT_ENTRIES:
        raise ValueError(f"Pinned archive has {archive_pt_count} .pt entries, expected {EXPECTED_ARCHIVE_PT_ENTRIES}")
    if len(entries) > MAX_SELECTED_ENTRIES:
        raise ValueError("Selected entry count exceeds safety cap")
    selected_compressed_bytes = sum(entry["compressedBytes"] for entry in entries)
    if selected_compressed_bytes > MAX_SELECTED_COMPRESSED_BYTES:
        raise ValueError("Selected compressed bytes exceed safety cap")

    dataset = source["dataset"]
    return {
        "schemaVersion": 1,
        "complete": False,
        "repository": dataset["repository"],
        "repositoryRevision": dataset["repositoryRevision"],
        "processedArchiveSha256": dataset["processedArchiveSha256"],
        "entryHashAlgorithm": "SHA-256",
        "selectionContentSha256": None,
        "archivePtEntryCount": archive_pt_count,
        "selectedEntryCount": len(entries),
        "selectedCompressedBytes": selected_compressed_bytes,
        "entries": entries,
    }


def file_digests(path: Path) -> tuple[str, str]:
    checksum = 0
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while True:
            block = stream.read(1024 * 1024)
            if not block:
                break
            checksum = zlib.crc32(block, checksum)
            digest.update(block)
    return f"{checksum & 0xFFFFFFFF:08x}", digest.hexdigest()


def selection_hash_input(selection: dict[str, Any]) -> dict[str, Any]:
    entry_fields = (
        "split",
        "speaker",
        "prompt",
        "promptOrdinal",
        "entry",
        "uncompressedBytes",
        "compressedBytes",
        "crc32",
        "sha256",
    )
    return {
        "schemaVersion": int(selection["schemaVersion"]),
        "repository": selection["repository"],
        "repositoryRevision": selection["repositoryRevision"],
        "processedArchiveSha256": selection["processedArchiveSha256"],
        "archivePtEntryCount": int(selection["archivePtEntryCount"]),
        "entries": [
            {field: entry[field] for field in entry_fields}
            for entry in selection["entries"]
        ],
    }


def finalize_selection_hashes(selection: dict[str, Any], cache_dir: Path) -> None:
    for entry in selection["entries"]:
        path = cache_path(cache_dir, entry)
        if not path.is_file() or path.stat().st_size != int(entry["uncompressedBytes"]):
            raise IOError(f"Missing or size-mismatched cached entry: {entry['entry']}")
        crc32, sha256 = file_digests(path)
        if crc32 != entry["crc32"]:
            raise IOError(f"CRC mismatch for cached entry: {entry['entry']}")
        entry["sha256"] = sha256
    selection["complete"] = True
    selection["selectionContentSha256"] = canonical_sha256(selection_hash_input(selection))


def validate_selection_hash(selection: dict[str, Any]) -> None:
    if selection.get("complete") is not True:
        raise ValueError("Selection manifest is incomplete; rerun fetch")
    if selection.get("entryHashAlgorithm") != "SHA-256":
        raise ValueError("Selection manifest must use per-entry SHA-256")
    entries = selection.get("entries")
    if not isinstance(entries, list) or not entries or len(entries) > MAX_SELECTED_ENTRIES:
        raise ValueError("Selection manifest entry count is outside safety limits")
    if int(selection.get("archivePtEntryCount", -1)) != EXPECTED_ARCHIVE_PT_ENTRIES:
        raise ValueError("Selection manifest archive entry count mismatch")
    compressed_total = 0
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("Selection entries must be objects")
        if not 0 < int(entry.get("uncompressedBytes", 0)) <= MAX_PT_BYTES:
            raise ValueError(f"Selection entry exceeds size cap: {entry.get('entry')!r}")
        compressed_bytes = int(entry.get("compressedBytes", -1))
        if compressed_bytes < 0:
            raise ValueError(f"Selection entry has invalid compressed size: {entry.get('entry')!r}")
        compressed_total += compressed_bytes
        if re.fullmatch(r"[0-9a-f]{8}", str(entry.get("crc32", ""))) is None:
            raise ValueError(f"Invalid CRC-32 for selection entry {entry.get('entry')!r}")
        digest = entry.get("sha256")
        if not isinstance(digest, str) or re.fullmatch(r"[0-9a-f]{64}", digest) is None:
            raise ValueError(f"Missing or invalid SHA-256 for selection entry {entry.get('entry')!r}")
    if compressed_total > MAX_SELECTED_COMPRESSED_BYTES:
        raise ValueError("Selection compressed bytes exceed safety cap")
    if int(selection.get("selectedCompressedBytes", -1)) != compressed_total:
        raise ValueError("Selection compressed byte count mismatch")
    expected = canonical_sha256(selection_hash_input(selection))
    if selection.get("selectionContentSha256") != expected:
        raise ValueError("Selection manifest content hash mismatch")


def cache_path(cache_dir: Path, entry: dict[str, Any]) -> Path:
    raw_entry = entry.get("entry")
    if not isinstance(raw_entry, str) or not raw_entry:
        raise ValueError("Cache entry path must be a non-empty string")
    relative = Path(raw_entry)
    if relative.is_absolute() or any(part in {"..", "."} for part in relative.parts):
        raise ValueError(f"Unsafe cache entry path: {raw_entry!r}")
    base = cache_dir.resolve(strict=False)
    candidate = (base / relative).resolve(strict=False)
    try:
        candidate.relative_to(base)
    except ValueError as error:
        raise ValueError(f"Cache entry escapes cache directory: {raw_entry!r}") from error
    return candidate


def cached_entry_is_valid(
    cache_dir: Path,
    entry: dict[str, Any],
    require_sha256: bool = False,
) -> bool:
    path = cache_path(cache_dir, entry)
    if not path.is_file() or path.stat().st_size != int(entry["uncompressedBytes"]):
        return False
    crc32, sha256 = file_digests(path)
    if crc32 != entry["crc32"]:
        return False
    expected_sha256 = entry.get("sha256")
    if require_sha256 and not isinstance(expected_sha256, str):
        return False
    return expected_sha256 is None or sha256 == expected_sha256


def fetch_subset(source: dict[str, Any], cache_dir: Path, workers: int) -> Path:
    validate_source_manifest(source)
    if not 1 <= workers <= 16:
        raise ValueError("workers must be in [1,16]")
    try:
        from remotezip import RemoteZip
    except ImportError as error:
        raise RuntimeError("Install Tools/AdvancedVisemeTraining/requirements.txt before fetching") from error

    url = archive_url(source)
    print(f"Indexing pinned archive: {url}", flush=True)
    index_zip = RemoteZip(url)
    try:
        selection = index_selected_entries(index_zip, source)
    finally:
        index_zip.close()

    selection_path = cache_dir / "selection_manifest.json"
    write_json_atomic(selection_path, selection)
    entries = selection["entries"]
    missing = [entry for entry in entries if not cached_entry_is_valid(cache_dir, entry)]
    selected_mb = selection["selectedCompressedBytes"] / (1024 * 1024)
    print(
        f"Selected {len(entries)} of {selection['archivePtEntryCount']} entries "
        f"({selected_mb:.1f} MiB compressed); {len(missing)} need download.",
        flush=True,
    )
    if not missing:
        finalize_selection_hashes(selection, cache_dir)
        write_json_atomic(selection_path, selection)
        return selection_path

    thread_state = threading.local()

    def get_remote() -> Any:
        remote = getattr(thread_state, "remote", None)
        if remote is None:
            remote = RemoteZip(url)
            thread_state.remote = remote
        return remote

    def fetch_one(entry: dict[str, Any]) -> tuple[str, int]:
        destination = cache_path(cache_dir, entry)
        destination.parent.mkdir(parents=True, exist_ok=True)
        last_error: Exception | None = None
        for attempt in range(4):
            try:
                blob = get_remote().read(entry["entry"])
                if len(blob) != int(entry["uncompressedBytes"]):
                    raise IOError(f"Unexpected uncompressed size for {entry['entry']}")
                checksum = f"{zlib.crc32(blob) & 0xFFFFFFFF:08x}"
                if checksum != entry["crc32"]:
                    raise IOError(f"CRC mismatch for {entry['entry']}: {checksum} != {entry['crc32']}")
                temporary = destination.with_name(
                    f".{destination.name}.{os.getpid()}.{threading.get_ident()}.part"
                )
                temporary.write_bytes(blob)
                os.replace(temporary, destination)
                return entry["entry"], len(blob)
            except Exception as error:  # network retry boundary
                last_error = error
                remote = getattr(thread_state, "remote", None)
                if remote is not None:
                    remote.close()
                    thread_state.remote = None
                if attempt < 3:
                    time.sleep(2**attempt)
        assert last_error is not None
        raise last_error

    completed = 0
    uncompressed_bytes = 0
    with ThreadPoolExecutor(max_workers=max(1, workers)) as executor:
        futures = [executor.submit(fetch_one, entry) for entry in missing]
        for future in as_completed(futures):
            _, size = future.result()
            completed += 1
            uncompressed_bytes += size
            if completed == len(missing) or completed % 25 == 0:
                print(
                    f"Fetched {completed}/{len(missing)} entries "
                    f"({uncompressed_bytes / (1024 * 1024):.1f} MiB unpacked this run).",
                    flush=True,
                )

    finalize_selection_hashes(selection, cache_dir)
    write_json_atomic(selection_path, selection)
    return selection_path


def restricted_load_pt(path: Path) -> dict[str, Any]:
    """Read the NumPy-only data.pkl member from a pinned corpus entry safely."""

    if not path.is_file() or not 0 < path.stat().st_size <= MAX_PT_BYTES:
        raise ValueError(f"Corpus entry is missing or exceeds the .pt size cap: {path}")
    with zipfile.ZipFile(path, "r") as archive:
        members = archive.infolist()
        if len(members) > MAX_INNER_ZIP_MEMBERS:
            raise ValueError(f"Too many inner ZIP members in {path}")
        if sum(int(member.file_size) for member in members) > MAX_INNER_UNCOMPRESSED_BYTES:
            raise ValueError(f"Inner ZIP uncompressed size exceeds cap in {path}")
        if any(member.flag_bits & 0x1 for member in members):
            raise ValueError(f"Encrypted inner ZIP members are not accepted in {path}")
        data_members = [member for member in members if member.filename.endswith("/data.pkl")]
        storage_members = [member.filename for member in members if "/data/" in member.filename]
        if len(data_members) != 1:
            raise ValueError(f"Expected one data.pkl in {path}, found {len(data_members)}")
        if storage_members:
            raise ValueError(f"Tensor storages are not accepted in restricted corpus entry {path}")
        data_member = data_members[0]
        if not 0 < int(data_member.file_size) <= MAX_PICKLE_BYTES:
            raise ValueError(f"data.pkl exceeds payload cap in {path}")
        payload = archive.read(data_member)
        if len(payload) != int(data_member.file_size):
            raise ValueError(f"Truncated data.pkl in {path}")

    value = RestrictedNumpyUnpickler(io.BytesIO(payload)).load()
    if not isinstance(value, dict):
        raise ValueError(f"Expected dictionary payload in {path}")
    return value


def normalize_phone(phone: str) -> str:
    return re.sub(r"\d+$", "", phone.strip().lower())


def basic_phone_visemes(phone: str) -> tuple[str, ...] | None:
    phone = normalize_phone(phone)
    if phone in DIPHTHONG_TO_VISEMES:
        return DIPHTHONG_TO_VISEMES[phone]
    mapped = PHONE_TO_VISEME.get(phone)
    return None if mapped is None else (mapped,)


def contextual_phone_visemes(phones: Sequence[str], index: int) -> tuple[str, ...]:
    phone = normalize_phone(phones[index])
    basic = basic_phone_visemes(phone)
    if basic is not None:
        return basic
    if phone in CONTEXTUAL_GLOTTAL_PHONES:
        for following in phones[index + 1 :]:
            candidate = basic_phone_visemes(following)
            if candidate is not None and candidate[0] != "sil":
                return (candidate[0],)
        return ("sil",)
    raise ValueError(f"Unsupported corpus phone: {phones[index]!r}")


def split_duration(visemes: Sequence[str], duration: int) -> list[tuple[str, int]]:
    if duration <= 0:
        return []
    if len(visemes) == 1 or duration == 1:
        return [(visemes[0], duration)]
    # The first target receives a slight majority so an onset-heavy hard-viseme
    # detector and our expansion agree on the transition direction.
    first = max(1, min(duration - 1, int(round(duration * 0.55))))
    return [(visemes[0], first), (visemes[1], duration - first)]


def build_segments(phones: Sequence[str], durations: Sequence[int], frame_count: int) -> list[Segment]:
    if len(phones) != len(durations):
        raise ValueError("Phone and duration arrays have different lengths")
    if sum(int(duration) for duration in durations) != frame_count:
        raise ValueError(
            f"Phone durations sum to {sum(int(duration) for duration in durations)}, expected {frame_count}"
        )

    segments: list[Segment] = []
    cursor = 0
    for index, duration_value in enumerate(durations):
        duration = int(duration_value)
        if duration < 0:
            raise ValueError("Negative phoneme duration")
        units = split_duration(contextual_phone_visemes(phones, index), duration)
        for viseme_name, unit_duration in units:
            viseme = VISEME_INDEX[viseme_name]
            end = cursor + unit_duration
            if segments and segments[-1].viseme == viseme:
                segments[-1].end = end
            else:
                segments.append(Segment(viseme=viseme, start=cursor, end=end))
            cursor = end
    if cursor != frame_count:
        raise ValueError(f"Expanded viseme durations sum to {cursor}, expected {frame_count}")
    return segments


def load_utterance(cache_dir: Path, entry: dict[str, Any]) -> Utterance:
    path = cache_path(cache_dir, entry)
    payload = restricted_load_pt(path)
    expected_keys = {NUMPY_PAYLOAD_KEY, "phonemes", "durations"}
    missing = expected_keys.difference(payload)
    if missing:
        raise ValueError(f"Missing keys {sorted(missing)} in {entry['entry']}")

    source_ema = payload[NUMPY_PAYLOAD_KEY]
    if not isinstance(source_ema, np.ndarray) or source_ema.dtype.hasobject or source_ema.dtype.kind not in {"f", "c"}:
        raise ValueError(f"EMA payload must be a non-object floating NumPy array in {entry['entry']}")
    if np.iscomplexobj(source_ema):
        raise ValueError(f"Complex EMA payload is not accepted in {entry['entry']}")
    ema = np.asarray(source_ema, dtype=np.float64)
    if ema.ndim != 2 or ema.shape[1] != 12:
        raise ValueError(f"Expected (frames, 12) EMA in {entry['entry']}, got {ema.shape}")
    if not 1 <= int(ema.shape[0]) <= MAX_FRAMES or ema.size > MAX_FRAMES * 12:
        raise ValueError(f"EMA frame count exceeds safety cap in {entry['entry']}")
    phones = payload["phonemes"]
    durations = payload["durations"]
    if (
        not isinstance(phones, (list, tuple))
        or not 1 <= len(phones) <= MAX_PHONES
        or not all(isinstance(phone, str) and len(phone) <= MAX_PHONE_LABEL_LENGTH for phone in phones)
    ):
        raise ValueError(f"Invalid phone labels in {entry['entry']}")
    if (
        not isinstance(durations, (list, tuple))
        or len(durations) != len(phones)
        or not all(isinstance(value, (int, np.integer)) and 0 <= int(value) <= MAX_FRAMES for value in durations)
    ):
        raise ValueError(f"Invalid phone durations in {entry['entry']}")
    segments = build_segments(phones, durations, int(ema.shape[0]))
    return Utterance(
        split=str(entry["split"]),
        speaker=int(entry["speaker"]),
        prompt=int(entry["prompt"]),
        source_entry=str(entry["entry"]),
        ema=ema,
        segments=segments,
    )


def load_selection(
    cache_dir: Path,
    source: dict[str, Any] | None = None,
) -> tuple[dict[str, Any], list[Utterance]]:
    selection_path = cache_dir / "selection_manifest.json"
    if not selection_path.is_file():
        raise FileNotFoundError(f"Run the fetch command first; missing {selection_path}")
    selection = load_json(selection_path)
    validate_selection_hash(selection)
    if int(selection.get("selectedEntryCount", -1)) != len(selection.get("entries", [])):
        raise ValueError("Selection manifest entry count mismatch")
    if source is not None:
        dataset = source["dataset"]
        if selection["repository"] != dataset["repository"]:
            raise ValueError("Cache selection was built from a different repository")
        if selection["repositoryRevision"] != dataset["repositoryRevision"]:
            raise ValueError("Cache selection was built from a different repository revision")
        if selection["processedArchiveSha256"] != dataset["processedArchiveSha256"]:
            raise ValueError("Cache selection was built from a different processed archive")
    utterances: list[Utterance] = []
    total = len(selection["entries"])
    for index, entry in enumerate(selection["entries"], start=1):
        if not cached_entry_is_valid(cache_dir, entry, require_sha256=True):
            raise IOError(f"Cached entry failed size/CRC/SHA-256 validation: {entry['entry']}")
        utterances.append(load_utterance(cache_dir, entry))
        if index == total or index % 100 == 0:
            print(f"Loaded and restricted-validated {index}/{total} entries.", flush=True)
    return selection, utterances


def split_utterances(utterances: Sequence[Utterance], split: str) -> list[Utterance]:
    result = [utterance for utterance in utterances if utterance.split == split]
    if not result:
        raise ValueError(f"No utterances found for split {split!r}")
    return result


def stable_segment_slice(segment: Segment) -> slice:
    duration = segment.end - segment.start
    trim = int(math.floor(duration * 0.30))
    start = segment.start + trim
    end = segment.end - trim
    if start >= end:
        midpoint = segment.start + max(0, duration // 2)
        start, end = midpoint, min(segment.end, midpoint + 1)
    return slice(start, end)


def estimate_centers(utterances: Sequence[Utterance]) -> tuple[list[np.ndarray], list[list[int]]]:
    samples: list[list[list[np.ndarray]]] = [
        [[] for _ in VISEMES] for _ in GROUPS
    ]
    for utterance in utterances:
        for segment in utterance.segments:
            segment_rows = utterance.ema[stable_segment_slice(segment)]
            for group_index, (_, columns) in enumerate(GROUPS):
                rows = segment_rows[:, columns]
                rows = rows[np.all(np.isfinite(rows), axis=1)]
                if rows.size:
                    samples[group_index][segment.viseme].append(rows)

    centers: list[np.ndarray] = []
    frame_counts: list[list[int]] = []
    for group_index, (_, columns) in enumerate(GROUPS):
        all_rows = [rows for viseme_rows in samples[group_index] for rows in viseme_rows]
        if not all_rows:
            raise ValueError(f"No finite center samples for group {GROUPS[group_index][0]}")
        global_center = np.median(np.concatenate(all_rows, axis=0), axis=0)
        group_centers = np.empty((len(VISEMES), len(columns)), dtype=np.float64)
        group_counts: list[int] = []
        for viseme in range(len(VISEMES)):
            viseme_rows = samples[group_index][viseme]
            if viseme_rows:
                merged = np.concatenate(viseme_rows, axis=0)
                group_centers[viseme] = np.median(merged, axis=0)
                group_counts.append(int(merged.shape[0]))
            else:
                group_centers[viseme] = global_center
                group_counts.append(0)
        centers.append(group_centers)
        frame_counts.append(group_counts)
    return centers, frame_counts


def transition_statistics(
    utterances: Sequence[Utterance],
    centers: Sequence[np.ndarray],
    decay_frames: Sequence[float],
    maximum_window_frames: int,
) -> tuple[list[np.ndarray], list[np.ndarray], np.ndarray]:
    numerators = [np.zeros((len(VISEMES), len(VISEMES)), dtype=np.float64) for _ in GROUPS]
    denominators = [np.zeros((len(VISEMES), len(VISEMES)), dtype=np.float64) for _ in GROUPS]
    event_counts = np.zeros((len(VISEMES), len(VISEMES)), dtype=np.int64)

    for utterance in utterances:
        for segment_index in range(1, len(utterance.segments)):
            previous = utterance.segments[segment_index - 1].viseme
            current_segment = utterance.segments[segment_index]
            current = current_segment.viseme
            if previous == current:
                continue
            event_counts[previous, current] += 1
            window = min(maximum_window_frames, current_segment.end - current_segment.start)
            if window <= 0:
                continue
            for group_index, (_, columns) in enumerate(GROUPS):
                direction = centers[group_index][previous] - centers[group_index][current]
                direction_energy = float(np.dot(direction, direction))
                if direction_energy < 1e-8:
                    continue
                for offset in range(window):
                    observed = utterance.ema[current_segment.start + offset, columns]
                    if not np.all(np.isfinite(observed)):
                        continue
                    kernel = math.exp(-offset / float(decay_frames[group_index]))
                    residual = observed - centers[group_index][current]
                    numerators[group_index][previous, current] += kernel * float(np.dot(direction, residual))
                    denominators[group_index][previous, current] += kernel * kernel * direction_energy
    return numerators, denominators, event_counts


def build_group_table(
    numerator: np.ndarray,
    denominator: np.ndarray,
    event_counts: np.ndarray,
    minimum_direct_samples: int,
    prior_strength: float,
) -> tuple[np.ndarray, np.ndarray]:
    off_diagonal = ~np.eye(len(VISEMES), dtype=bool)
    global_denominator = float(np.sum(denominator[off_diagonal]))
    global_retention = (
        float(np.clip(np.sum(numerator[off_diagonal]) / global_denominator, 0.0, 1.0))
        if global_denominator > 1e-12
        else 0.0
    )

    destination_retention = np.full(len(VISEMES), global_retention, dtype=np.float64)
    destination_has_data = np.zeros(len(VISEMES), dtype=bool)
    for current in range(len(VISEMES)):
        mask = np.ones(len(VISEMES), dtype=bool)
        mask[current] = False
        destination_denominator = float(np.sum(denominator[mask, current]))
        if destination_denominator > 1e-12:
            destination_retention[current] = float(
                np.clip(np.sum(numerator[mask, current]) / destination_denominator, 0.0, 1.0)
            )
            destination_has_data[current] = True

    table = np.zeros((len(VISEMES), len(VISEMES)), dtype=np.float64)
    backoff = np.full((len(VISEMES), len(VISEMES)), 3, dtype=np.uint8)
    for previous in range(len(VISEMES)):
        for current in range(len(VISEMES)):
            if previous == current:
                # Retaining a pose into itself is algebraically irrelevant; zero is
                # the stable, non-latching convention for the generated runtime API.
                table[previous, current] = 0.0
                backoff[previous, current] = 3
                continue
            count = int(event_counts[previous, current])
            if count >= minimum_direct_samples and denominator[previous, current] > 1e-12:
                direct = float(np.clip(numerator[previous, current] / denominator[previous, current], 0.0, 1.0))
                destination = float(destination_retention[current])
                direct_weight = count / (count + prior_strength)
                table[previous, current] = np.clip(
                    direct_weight * direct + (1.0 - direct_weight) * destination,
                    0.0,
                    1.0,
                )
                backoff[previous, current] = 0
            elif destination_has_data[current]:
                table[previous, current] = destination_retention[current]
                backoff[previous, current] = 1
            elif global_denominator > 1e-12:
                table[previous, current] = global_retention
                backoff[previous, current] = 2
            else:
                table[previous, current] = 0.0
                backoff[previous, current] = 3
    return table, backoff


def fit_model(
    utterances: Sequence[Utterance],
    centers: Sequence[np.ndarray],
    decay_frames: Sequence[float],
    prior_strengths: Sequence[float],
    maximum_window_frames: int,
    minimum_direct_samples: int,
) -> dict[str, Any]:
    numerators, denominators, event_counts = transition_statistics(
        utterances, centers, decay_frames, maximum_window_frames
    )
    return fit_model_from_statistics(
        numerators,
        denominators,
        event_counts,
        prior_strengths,
        minimum_direct_samples,
    )


def fit_model_from_statistics(
    numerators: Sequence[np.ndarray],
    denominators: Sequence[np.ndarray],
    event_counts: np.ndarray,
    prior_strengths: Sequence[float],
    minimum_direct_samples: int,
) -> dict[str, Any]:
    tables: list[np.ndarray] = []
    backoff: list[np.ndarray] = []
    for group_index in range(len(GROUPS)):
        table, levels = build_group_table(
            numerators[group_index],
            denominators[group_index],
            event_counts,
            minimum_direct_samples,
            float(prior_strengths[group_index]),
        )
        tables.append(table)
        backoff.append(levels)
    return {
        "retention": tables,
        "backoff": backoff,
        "eventCounts": event_counts,
    }


def evaluate_model(
    utterances: Sequence[Utterance],
    centers: Sequence[np.ndarray],
    model: dict[str, Any],
    decay_frames: Sequence[float],
    maximum_window_frames: int,
) -> dict[str, Any]:
    baseline_sse = np.zeros(len(GROUPS), dtype=np.float64)
    context_sse = np.zeros(len(GROUPS), dtype=np.float64)
    scalar_counts = np.zeros(len(GROUPS), dtype=np.int64)
    transition_events = 0

    for utterance in utterances:
        for segment_index in range(1, len(utterance.segments)):
            previous = utterance.segments[segment_index - 1].viseme
            current_segment = utterance.segments[segment_index]
            current = current_segment.viseme
            if previous == current:
                continue
            transition_events += 1
            window = min(maximum_window_frames, current_segment.end - current_segment.start)
            for group_index, (_, columns) in enumerate(GROUPS):
                direction = centers[group_index][previous] - centers[group_index][current]
                retention = float(model["retention"][group_index][previous, current])
                for offset in range(window):
                    observed = utterance.ema[current_segment.start + offset, columns]
                    if not np.all(np.isfinite(observed)):
                        continue
                    baseline = centers[group_index][current]
                    kernel = math.exp(-offset / float(decay_frames[group_index]))
                    contextual = baseline + retention * kernel * direction
                    baseline_error = observed - baseline
                    contextual_error = observed - contextual
                    baseline_sse[group_index] += float(np.dot(baseline_error, baseline_error))
                    context_sse[group_index] += float(np.dot(contextual_error, contextual_error))
                    scalar_counts[group_index] += len(columns)

    group_metrics: dict[str, Any] = {}
    for group_index, (name, _) in enumerate(GROUPS):
        count = int(scalar_counts[group_index])
        baseline_mse = float(baseline_sse[group_index] / count) if count else math.nan
        context_mse = float(context_sse[group_index] / count) if count else math.nan
        improvement = (
            100.0 * (baseline_mse - context_mse) / baseline_mse if baseline_mse > 0.0 else 0.0
        )
        group_metrics[name] = {
            "baselineMse": baseline_mse,
            "contextMse": context_mse,
            "relativeImprovementPercent": improvement,
            "evaluatedScalars": count,
        }

    total_count = int(np.sum(scalar_counts))
    total_baseline_mse = float(np.sum(baseline_sse) / total_count) if total_count else math.nan
    total_context_mse = float(np.sum(context_sse) / total_count) if total_count else math.nan
    total_improvement = (
        100.0 * (total_baseline_mse - total_context_mse) / total_baseline_mse
        if total_baseline_mse > 0.0
        else 0.0
    )
    return {
        "transitionEvents": transition_events,
        "overall": {
            "baselineMse": total_baseline_mse,
            "contextMse": total_context_mse,
            "relativeImprovementPercent": total_improvement,
            "evaluatedScalars": total_count,
        },
        "groups": group_metrics,
    }


def tune_hyperparameters(
    fit: Sequence[Utterance],
    development: Sequence[Utterance],
    centers: Sequence[np.ndarray],
    source: dict[str, Any],
) -> tuple[list[float], list[float], list[dict[str, Any]], dict[str, Any]]:
    derivation = source["derivation"]
    maximum_window = int(derivation["maximumTransitionWindowFrames"])
    minimum_samples = int(derivation["minimumDirectTransitionSamples"])
    decay_candidates = [float(value) for value in derivation["decayFrameCandidates"]]
    prior_candidates = [float(value) for value in derivation["priorStrengthCandidates"]]
    sample_rate_hz = float(source["dataset"]["sampleRateHz"])

    best: list[tuple[float, float, float]] = [(math.inf, decay_candidates[0], prior_candidates[0]) for _ in GROUPS]
    grid: list[dict[str, Any]] = []
    for decay in decay_candidates:
        decay_vector = [decay] * len(GROUPS)
        numerators, denominators, event_counts = transition_statistics(
            fit,
            centers,
            decay_vector,
            maximum_window,
        )
        for prior in prior_candidates:
            prior_vector = [prior] * len(GROUPS)
            candidate_model = fit_model_from_statistics(
                numerators,
                denominators,
                event_counts,
                prior_vector,
                minimum_samples,
            )
            metrics = evaluate_model(development, centers, candidate_model, decay_vector, maximum_window)
            grid_entry = {
                "decaySeconds": decay / sample_rate_hz,
                "priorStrength": prior,
                "groupContextMse": {
                    name: metrics["groups"][name]["contextMse"] for name, _ in GROUPS
                },
            }
            grid.append(grid_entry)
            for group_index, (name, _) in enumerate(GROUPS):
                score = float(metrics["groups"][name]["contextMse"])
                # Tuple comparison gives deterministic preference to the shorter
                # decay and weaker prior when scores are numerically identical.
                candidate = (score, decay, prior)
                if candidate < best[group_index]:
                    best[group_index] = candidate

    selected_decay = [value[1] for value in best]
    selected_prior = [value[2] for value in best]
    development_model = fit_model(
        fit,
        centers,
        selected_decay,
        selected_prior,
        maximum_window,
        minimum_samples,
    )
    development_metrics = evaluate_model(
        development, centers, development_model, selected_decay, maximum_window
    )
    return selected_decay, selected_prior, grid, development_metrics


def canonical_model_hash(model: dict[str, Any]) -> str:
    return canonical_sha256(model)


def rounded_matrix(matrix: np.ndarray, digits: int = 8) -> list[list[float]]:
    return [[round(float(value), digits) for value in row] for row in matrix]


def model_document(
    source: dict[str, Any],
    selection: dict[str, Any],
    model: dict[str, Any],
    decay_frames: Sequence[float],
    prior_strengths: Sequence[float],
    center_counts: Sequence[Sequence[int]],
    development_metrics: dict[str, Any],
    heldout_metrics: dict[str, Any],
    tuning_grid: Sequence[dict[str, Any]],
) -> dict[str, Any]:
    counts = model["eventCounts"]
    sample_rate_hz = float(source["dataset"]["sampleRateHz"])
    decay_seconds = [float(value) / sample_rate_hz for value in decay_frames]
    document: dict[str, Any] = {
        "schemaVersion": 1,
        "modelVersion": 1,
        "description": "Dimensionless previous-pose retention for Oculus/VRChat hard-viseme transitions.",
        "visemeOrder": list(VISEMES),
        "articulatorGroups": [name for name, _ in GROUPS],
        "sampleRateHz": sample_rate_hz,
        "decaySeconds": decay_seconds,
        "priorStrength": [float(value) for value in prior_strengths],
        "retention": {
            name: rounded_matrix(model["retention"][index]) for index, (name, _) in enumerate(GROUPS)
        },
        "sampleCounts": [[int(value) for value in row] for row in counts],
        "backoffLevel": {
            name: [[int(value) for value in row] for row in model["backoff"][index]]
            for index, (name, _) in enumerate(GROUPS)
        },
        "backoffMeaning": {
            "0": "direct transition estimate shrunk toward destination prior",
            "1": "destination-viseme prior",
            "2": "articulator-group global prior",
            "3": "forced zero for self transition or no usable data",
        },
        "centerFrameCounts": {
            name: [int(value) for value in center_counts[index]]
            for index, (name, _) in enumerate(GROUPS)
        },
        "modeling": {
            "transitionPrediction": "mu_current + retention[group,previous,current] * exp(-timeOffsetSeconds/decaySeconds[group]) * (mu_previous-mu_current)",
            "runtimeInterpretation": "Apply the dimensionless retention to the avatar's authored viseme/articulator poses; do not apply corpus EMA coordinates directly.",
            "observationSurrogate": source["derivation"]["labelSemantics"],
            "maximumTransitionWindowFrames": int(source["derivation"]["maximumTransitionWindowFrames"]),
            "minimumDirectTransitionSamples": int(source["derivation"]["minimumDirectTransitionSamples"]),
        },
        "evaluation": {
            "development": development_metrics,
            "heldoutSpeakerAndSentenceDisjoint": heldout_metrics,
            "metricScope": "Frames in the first transition window only; baseline is the current-viseme median pose.",
            "tuningGrid": list(tuning_grid),
        },
        "provenance": {
            "dataset": source["dataset"],
            "subset": source["subset"],
            "archivePtEntryCount": int(selection["archivePtEntryCount"]),
            "selectedEntryCount": int(selection["selectedEntryCount"]),
            "selectedCompressedBytes": int(selection["selectedCompressedBytes"]),
            "selectionContentSha256": selection["selectionContentSha256"],
            "entryHashAlgorithm": selection["entryHashAlgorithm"],
            "selectionManifest": "Tools/AdvancedVisemeTraining/Generated/spire_selection_manifest.json",
            "licenseNotice": "Derived from the SPIRE EMA Corpus, licensed CC BY 4.0. Attribution is required.",
            "archiveIntegrityCaveat": "Range fetching cannot independently recompute the complete 3.09 GB LFS object digest. The repository revision and LFS SHA-256 are pinned, and every selected uncompressed ZIP member is bound by SHA-256 plus ZIP CRC-32 in the canonical selection hash.",
        },
        "limitations": [
            "A hard viseme cannot recover phoneme identity inside merged classes such as PP (p/b/m) or nn (n/l).",
            "The table estimates coarticulatory carryover, not absolute tongue geometry or untracked tongue contact.",
            "The Kaldi forced-aligned ARPAbet-to-viseme mapping is a deterministic surrogate for hidden Oculus/VRChat classifier output, not labels emitted by VRChat.",
            "The held-out set contains unseen speakers and disjoint sentences but remains English speech from the corpus demographic.",
        ],
    }
    document["contentSha256"] = canonical_model_hash(document)
    return document


def format_float_rows(matrix: Sequence[Sequence[float]], indent: str = "            ") -> list[str]:
    lines: list[str] = []
    for previous, row in enumerate(matrix):
        values = ", ".join(f"{float(value):.8f}f" for value in row)
        lines.append(f"{indent}{values}, // {VISEMES[previous]}")
    return lines


def format_integer_rows(matrix: Sequence[Sequence[int]], suffix: str, indent: str = "            ") -> list[str]:
    lines: list[str] = []
    for previous, row in enumerate(matrix):
        values = ", ".join(f"{int(value)}{suffix}" for value in row)
        lines.append(f"{indent}{values}, // {VISEMES[previous]}")
    return lines


def generate_csharp(document: dict[str, Any]) -> str:
    lines = [
        "// <auto-generated />",
        "// Generated by Tools/AdvancedVisemeTraining/train_transition_retention.py.",
        "// Derived from the SPIRE EMA Corpus (Bandekar, Udupa, Ghosh), CC BY 4.0.",
        "// Source: https://huggingface.co/datasets/SpireLab/SPIRE_EMA_CORPUS",
        "// Paper: https://doi.org/10.21437/Interspeech.2024-1756",
        "// Kaldi forced-aligned ARPAbet was mapped as a surrogate for hidden VRChat classifier output.",
        "// This table contains dimensionless aggregate coefficients; it redistributes no raw trajectories.",
        "",
        "using System;",
        "",
        "namespace YUCP.Components",
        "{",
        "    public enum AdvancedVisemeArticulatorGroup : byte",
        "    {",
        "        Jaw = 0,",
        "        Lips = 1,",
        "        TongueTip = 2,",
        "        TongueBody = 3",
        "    }",
        "",
        "    /// <summary>Corpus-derived, avatar-portable coarticulation retention.</summary>",
        "    public static class AdvancedVisemeTransitionRetention",
        "    {",
        f"        public const int ModelVersion = {int(document['modelVersion'])};",
        "        public const int VisemeCount = 15;",
        "        public const int GroupCount = 4;",
        f"        public const string ContentSha256 = \"{document['contentSha256']}\";",
        "",
        "        private static readonly float[] DecaySecondValues =",
        "        {",
        "            " + ", ".join(f"{float(value):.8f}f" for value in document["decaySeconds"]),
        "        };",
        "",
        "        private static readonly float[] PriorStrengthValues =",
        "        {",
        "            " + ", ".join(f"{float(value):.8f}f" for value in document["priorStrength"]),
        "        };",
        "",
        "        // Flattened as [group, previous viseme, current viseme].",
        "        private static readonly float[] RetentionValues =",
        "        {",
    ]
    for group_name in document["articulatorGroups"]:
        lines.append(f"            // {group_name}: columns are {', '.join(VISEMES)}")
        lines.extend(format_float_rows(document["retention"][group_name]))
    lines.extend(
        [
            "        };",
            "",
            "        // Transition event counts are shared by all articulator groups.",
            "        private static readonly ushort[] SampleCountValues =",
            "        {",
        ]
    )
    sample_counts = document["sampleCounts"]
    if any(int(value) > 65535 for row in sample_counts for value in row):
        raise ValueError("Generated sample count does not fit UInt16")
    lines.extend(format_integer_rows(sample_counts, ""))
    lines.extend(
        [
            "        };",
            "",
            "        // 0=direct, 1=destination prior, 2=group prior, 3=forced zero/no data.",
            "        private static readonly byte[] BackoffLevelValues =",
            "        {",
        ]
    )
    for group_name in document["articulatorGroups"]:
        lines.append(f"            // {group_name}")
        lines.extend(format_integer_rows(document["backoffLevel"][group_name], ""))
    lines.extend(
        [
            "        };",
            "",
            "        public static float Retention(AdvancedVisemeArticulatorGroup group, int previousViseme, int currentViseme)",
            "        {",
            "            return RetentionValues[GroupIndex(group) * VisemeCount * VisemeCount + TransitionIndex(previousViseme, currentViseme)];",
            "        }",
            "",
            "        public static int SampleCount(int previousViseme, int currentViseme)",
            "        {",
            "            return SampleCountValues[TransitionIndex(previousViseme, currentViseme)];",
            "        }",
            "",
            "        public static byte BackoffLevel(AdvancedVisemeArticulatorGroup group, int previousViseme, int currentViseme)",
            "        {",
            "            return BackoffLevelValues[GroupIndex(group) * VisemeCount * VisemeCount + TransitionIndex(previousViseme, currentViseme)];",
            "        }",
            "",
            "        public static float DecaySeconds(AdvancedVisemeArticulatorGroup group)",
            "        {",
            "            return DecaySecondValues[GroupIndex(group)];",
            "        }",
            "",
            "        public static float PriorStrength(AdvancedVisemeArticulatorGroup group)",
            "        {",
            "            return PriorStrengthValues[GroupIndex(group)];",
            "        }",
            "",
            "        private static int GroupIndex(AdvancedVisemeArticulatorGroup group)",
            "        {",
            "            var value = (int)group;",
            "            if ((uint)value >= GroupCount) throw new ArgumentOutOfRangeException(nameof(group));",
            "            return value;",
            "        }",
            "",
            "        private static int TransitionIndex(int previousViseme, int currentViseme)",
            "        {",
            "            if ((uint)previousViseme >= VisemeCount) throw new ArgumentOutOfRangeException(nameof(previousViseme));",
            "            if ((uint)currentViseme >= VisemeCount) throw new ArgumentOutOfRangeException(nameof(currentViseme));",
            "            return previousViseme * VisemeCount + currentViseme;",
            "        }",
            "    }",
            "}",
            "",
        ]
    )
    return "\n".join(lines)


def validate_document(document: dict[str, Any]) -> None:
    if document["visemeOrder"] != list(VISEMES):
        raise ValueError("Unexpected viseme order")
    for group_name in document["articulatorGroups"]:
        retention = np.asarray(document["retention"][group_name], dtype=np.float64)
        backoff = np.asarray(document["backoffLevel"][group_name], dtype=np.int64)
        if retention.shape != (15, 15) or backoff.shape != (15, 15):
            raise ValueError(f"Invalid table shape for {group_name}")
        if not np.all(np.isfinite(retention)) or np.any(retention < 0.0) or np.any(retention > 1.0):
            raise ValueError(f"Retention values outside [0,1] for {group_name}")
        if not np.allclose(np.diag(retention), 0.0):
            raise ValueError(f"Self transitions must be zero for {group_name}")
        if np.any(backoff < 0) or np.any(backoff > 3):
            raise ValueError(f"Invalid backoff code for {group_name}")
    counts = np.asarray(document["sampleCounts"], dtype=np.int64)
    if counts.shape != (15, 15) or np.any(counts < 0):
        raise ValueError("Invalid sample count table")


def train(
    source: dict[str, Any],
    cache_dir: Path,
    output_json: Path,
    output_csharp: Path,
    output_selection: Path,
) -> dict[str, Any]:
    validate_source_manifest(source)
    selection, utterances = load_selection(cache_dir, source)
    dataset = source["dataset"]
    if selection["repositoryRevision"] != dataset["repositoryRevision"]:
        raise ValueError("Cache selection was built from a different repository revision")
    if selection["processedArchiveSha256"] != dataset["processedArchiveSha256"]:
        raise ValueError("Cache selection was built from a different processed archive")

    fit = split_utterances(utterances, "fit")
    development = split_utterances(utterances, "development")
    heldout = split_utterances(utterances, "heldout")
    fit_centers, _ = estimate_centers(fit)
    selected_decay, selected_prior, tuning_grid, development_metrics = tune_hyperparameters(
        fit, development, fit_centers, source
    )

    training = fit + development
    final_centers, center_counts = estimate_centers(training)
    derivation = source["derivation"]
    final_model = fit_model(
        training,
        final_centers,
        selected_decay,
        selected_prior,
        int(derivation["maximumTransitionWindowFrames"]),
        int(derivation["minimumDirectTransitionSamples"]),
    )
    heldout_metrics = evaluate_model(
        heldout,
        final_centers,
        final_model,
        selected_decay,
        int(derivation["maximumTransitionWindowFrames"]),
    )
    document = model_document(
        source,
        selection,
        final_model,
        selected_decay,
        selected_prior,
        center_counts,
        development_metrics,
        heldout_metrics,
        tuning_grid,
    )
    validate_document(document)
    write_json_atomic(output_selection, selection)
    write_json_atomic(output_json, document)
    write_text_atomic(output_csharp, generate_csharp(document))

    print(f"Model JSON: {output_json}", flush=True)
    print(f"Selection manifest: {output_selection}", flush=True)
    print(f"Runtime C#: {output_csharp}", flush=True)
    print(f"Content SHA-256: {document['contentSha256']}", flush=True)
    print(
        "Held-out transition MSE improvement: "
        f"{heldout_metrics['overall']['relativeImprovementPercent']:.3f}% overall",
        flush=True,
    )
    for group_name, _ in GROUPS:
        metrics = heldout_metrics["groups"][group_name]
        print(
            f"  {group_name}: {metrics['baselineMse']:.6f} -> {metrics['contextMse']:.6f} "
            f"({metrics['relativeImprovementPercent']:.3f}%)",
            flush=True,
        )
    return document


def default_cache_dir() -> Path:
    base = Path(os.environ.get("LOCALAPPDATA", os.environ.get("TEMP", str(SCRIPT_DIR))))
    return base / "YUCP" / "AdvancedVisemeTraining" / "SPIRE_EMA_CORPUS"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-manifest", type=Path, default=DEFAULT_SOURCE_MANIFEST)
    subparsers = parser.add_subparsers(dest="command", required=True)

    fetch_parser = subparsers.add_parser("fetch", help="Range-fetch and CRC-check the pinned corpus subset")
    fetch_parser.add_argument("--cache-dir", type=Path, default=default_cache_dir())
    fetch_parser.add_argument("--workers", type=int, default=4)

    train_parser = subparsers.add_parser("train", help="Fit, evaluate, and generate the model from a fetched cache")
    train_parser.add_argument("--cache-dir", type=Path, default=default_cache_dir())
    train_parser.add_argument("--output-json", type=Path, default=DEFAULT_MODEL_JSON)
    train_parser.add_argument("--output-csharp", type=Path, default=DEFAULT_MODEL_CS)
    train_parser.add_argument("--output-selection", type=Path, default=DEFAULT_SELECTION_JSON)

    all_parser = subparsers.add_parser("all", help="Fetch the subset, then train and generate")
    all_parser.add_argument("--cache-dir", type=Path, default=default_cache_dir())
    all_parser.add_argument("--workers", type=int, default=4)
    all_parser.add_argument("--output-json", type=Path, default=DEFAULT_MODEL_JSON)
    all_parser.add_argument("--output-csharp", type=Path, default=DEFAULT_MODEL_CS)
    all_parser.add_argument("--output-selection", type=Path, default=DEFAULT_SELECTION_JSON)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = build_parser().parse_args(argv)
    source = load_json(arguments.source_manifest.resolve())
    validate_source_manifest(source)
    cache_dir = arguments.cache_dir.resolve()
    if arguments.command in {"fetch", "all"}:
        fetch_subset(source, cache_dir, int(arguments.workers))
    if arguments.command in {"train", "all"}:
        train(
            source,
            cache_dir,
            arguments.output_json.resolve(),
            arguments.output_csharp.resolve(),
            arguments.output_selection.resolve(),
        )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("Interrupted.", file=sys.stderr)
        raise SystemExit(130)
