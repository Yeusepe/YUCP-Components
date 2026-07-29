#!/usr/bin/env python3
"""Fit the separable retention pull  w += d(age) * (f[prev] - g[cur]),
deployed with exact renormalization.

Validated in Tools/AdvancedVisemeAnalysis. The additive correction leaves the
simplex, so the runtime pipeline is: additive pull -> clamp (Unity Direct
blend weights clamp at zero) -> EXACT renormalization via a Direct BlendTree
with Normalize Blend Values enabled (native division by the weight sum).
Held-out RMSE 0.05113 all / 0.05646 transition with the sil h-fold exclusion
(the sil decoder state is trajectory-static by contract), vs the convex-only
form 0.05296 / 0.05958 and baseline 0.05751 / 0.06699.

Model pieces (all corpus-fitted on the Stein-shrunken residual table):
  - f rows (PreviousRow): carried across switches by a vector EMA at
    PullResponseSeconds; runtime term is PullScale * (ema - f[cur]).
  - h rows (FoldedCurrentCorrection = f - g): the (cur, age)-only remainder
    -d(age) * (g[cur] - f[cur]), folded into decoder trajectory curves at
    build time for trajectory-dynamic winners.
  - Rows are centered (sum-zero) so the linear part is sum-preserving; the
    renormalizer absorbs the clamp-induced remainder exactly.

Emits Generated/advanced_viseme_retention_pull.json and
AdvancedVisemeRetentionPull.generated.cs.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR.parent / "AdvancedVisemeAnalysis"))

import analyze_reconstruction_limits as limits  # noqa: E402
import analyze_transition_crossfade as crossfade  # noqa: E402
import analyze_retention_residual as residual  # noqa: E402
import analyze_transition_refinements as refine  # noqa: E402

REPOSITORY_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_JSON = SCRIPT_DIR / "Generated" / "advanced_viseme_retention_pull.json"
DEFAULT_CSHARP = (
    REPOSITORY_ROOT
    / "Packages" / "com.yucp.components" / "Runtime" / "Components"
    / "Data" / "Generated" / "AdvancedVisemeRetentionPull.generated.cs"
)

SHRINKAGE_N0 = 5.0
ALS_ITERATIONS = 200
VISEME_COUNT = limits.VISEME_COUNT


def fit(records: list[dict]) -> dict:
    fit_split = crossfade.gather(records, "fit")
    table = crossfade.winner_age_table(fit_split)
    table_traj, counts = refine.residual_trajectory_table(fit_split, table)
    alpha, decay, basis, _, _ = refine.factored_parts(fit_split, table)
    shrunk = refine.shrunk_correction(
        table_traj, counts, alpha, decay, basis, SHRINKAGE_N0
    )

    switch_residual = shrunk[:, :, 0, :]
    weights = counts.sum(axis=2) + 1.0

    f = np.zeros((VISEME_COUNT, VISEME_COUNT))
    g = np.zeros((VISEME_COUNT, VISEME_COUNT))
    for _ in range(ALS_ITERATIONS):
        f = (
            (weights[:, :, None] * (switch_residual + g[None, :, :])).sum(1)
            / weights.sum(1)[:, None]
        )
        g = (
            (weights[:, :, None] * (f[:, None, :] - switch_residual)).sum(0)
            / weights.sum(0)[:, None]
        )

    # Sum-zero projection: the true residuals are sum-zero, so this removes
    # only an off-manifold component and keeps the linear pull sum-neutral.
    f -= f.mean(axis=1, keepdims=True)
    g -= g.mean(axis=1, keepdims=True)

    separable = f[:, None, :] - g[None, :, :]
    bins = limits.AGE_BINS
    d = np.zeros(bins)
    denominator = float((separable * separable).sum())
    for b in range(bins):
        if denominator > 1e-12:
            d[b] = float(
                (shrunk[:, :, b, :] * separable).sum() / denominator
            )

    ratio = float(np.clip(d[1:8] / np.maximum(d[:7], 1e-9), 0.0, 1.0).mean())
    scale = float(d[0])
    tau_seconds = float(limits.FRAME_SECONDS / -np.log(ratio))

    explained = float(
        1.0
        - (
            (weights[:, :, None] * (switch_residual - separable) ** 2).sum()
            / (weights[:, :, None] * switch_residual ** 2).sum()
        )
    )
    return {
        "previousRows": f,
        "currentRows": g,
        "pullScale": scale,
        "pullResponseSeconds": tau_seconds,
        "separableVarianceExplained": explained,
    }


def build_document(model: dict) -> dict:
    payload = {
        "schemaVersion": 3,
        "modelVersion": 3,
        "visemeCount": VISEME_COUNT,
        "shrinkageN0": SHRINKAGE_N0,
        "pullScale": model["pullScale"],
        "pullResponseSeconds": model["pullResponseSeconds"],
        "frameSeconds": limits.FRAME_SECONDS,
        "separableVarianceExplained": model["separableVarianceExplained"],
        "previousRows": [
            [float(v) for v in row] for row in model["previousRows"]
        ],
        "currentRows": [
            [float(v) for v in row] for row in model["currentRows"]
        ],
    }
    digest = hashlib.sha256(
        json.dumps(payload, sort_keys=True).encode("utf-8")
    ).hexdigest()
    payload["contentSha256"] = digest
    return payload


def float_literal(value: float) -> str:
    return f"{value:.9f}f"


def generate_csharp(document: dict, output: Path) -> None:
    names = "sil PP FF TH DD kk CH SS nn RR aa E I O U".split()

    def table(rows: list) -> str:
        lines = []
        for index, row in enumerate(rows):
            lines.append(f"            // {names[index]}")
            lines.append(
                "            "
                + ", ".join(float_literal(v) for v in row)
                + ","
            )
        return "\n".join(lines)

    text = f"""// <auto-generated>
// Trained by Tools/AdvancedVisemeTraining/train_retention_pull.py.
// Separable retention pull with exact renormalization:
//   w += d(age) * (f[previous] - g[current]),  then clamp + renormalize.
// Fitted on the SPIRE EMA Corpus paired Oculus extraction (CC BY 4.0).
// </auto-generated>
using System;

namespace YUCP.Components
{{
    public static class AdvancedVisemeRetentionPull
    {{
        public const int ModelVersion = {document["modelVersion"]};
        public const int VisemeCount = {document["visemeCount"]};
        public const string ContentSha256 = "{document["contentSha256"]}";

        /// <summary>Correction gain at the switch instant.</summary>
        public const float PullScale = {float_literal(document["pullScale"])};

        /// <summary>Exponential decay time constant of the pull.</summary>
        public const float PullResponseSeconds = {float_literal(document["pullResponseSeconds"])};

        // f[previous winner, channel]: carried across switches by the EMA.
        // Rows are centered (sum-zero), so the linear pull is sum-neutral.
        private static readonly float[] PreviousRowValues =
        {{
{table(document["previousRows"])}
        }};

        // g[current winner, channel]: the pull anchor of the incoming state.
        private static readonly float[] CurrentRowValues =
        {{
{table(document["currentRows"])}
        }};

        public static float PreviousRow(int winner, int channel)
        {{
            return PreviousRowValues[Index(winner, channel)];
        }}

        public static float CurrentRow(int winner, int channel)
        {{
            return CurrentRowValues[Index(winner, channel)];
        }}

        /// <summary>
        /// The (current, age)-only part of the pull, folded into decoder
        /// trajectory curves at build time: correction = Decay(age) * this,
        /// applied only to trajectory-dynamic winners.
        /// </summary>
        public static float FoldedCurrentCorrection(int winner, int channel)
        {{
            return PreviousRowValues[Index(winner, channel)]
                   - CurrentRowValues[Index(winner, channel)];
        }}

        public static float Decay(float ageSeconds)
        {{
            if (ageSeconds <= 0f) return PullScale;
            return PullScale *
                   (float)Math.Exp(-ageSeconds / PullResponseSeconds);
        }}

        private static int Index(int winner, int channel)
        {{
            if ((uint)winner >= VisemeCount)
                throw new ArgumentOutOfRangeException(nameof(winner));
            if ((uint)channel >= VisemeCount)
                throw new ArgumentOutOfRangeException(nameof(channel));
            return winner * VisemeCount + channel;
        }}
    }}
}}
"""
    output.write_text(text, encoding="utf-8", newline="\n")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=limits.DEFAULT_CACHE)
    parser.add_argument("--json", type=Path, default=DEFAULT_JSON)
    parser.add_argument("--csharp", type=Path, default=DEFAULT_CSHARP)
    args = parser.parse_args(argv)

    records = limits.load_utterances(args.cache)
    model = fit(records)
    document = build_document(model)

    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(
        json.dumps(document, indent=2, sort_keys=True), encoding="utf-8"
    )
    generate_csharp(document, args.csharp)

    print(
        f"pullScale={document['pullScale']:.4f} "
        f"tau={document['pullResponseSeconds'] * 1e3:.1f} ms "
        f"separable={document['separableVarianceExplained'] * 100:.1f}% "
        f"sha={document['contentSha256'][:12]}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
