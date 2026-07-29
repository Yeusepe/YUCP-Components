"""Offline rank/cone audit for the AVR visible-tongue and hidden-phone models.

This script intentionally does not load Unity.  It proves the exact matrix ranks,
constructs a non-negative cone basis for the bilinear tongue coefficients, reports
conservative errors for rank truncation and coefficient pruning, and audits the
epoch-preserving two-output product accumulator used by the Animator builder.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
GENERATED = ROOT / "Tools" / "AdvancedVisemeTraining" / "Generated"
VISIBLE_MODELS = (
    GENERATED / "advanced_viseme_visible_tongue_residual_balanced.json",
    GENERATED / "advanced_viseme_visible_tongue_residual.json",
)
PHONE_MODEL = GENERATED / "advanced_viseme_hidden_phone_posterior.json"
SEED = 7


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def safe_feature_bounds(feature_count: int) -> np.ndarray:
    channel_count = feature_count // 3
    return np.concatenate(
        (np.ones(channel_count), np.full(channel_count * 2, 2.0))
    )


def output_bounds(model: dict, feature_safe: np.ndarray) -> np.ndarray:
    visible = np.asarray(model["visibleProjection"], dtype=np.float64)
    mix = np.asarray(model["visemeMix"], dtype=np.float64)
    bias = np.asarray(model["visemeBias"], dtype=np.float64)
    output = np.asarray(model["outputProjection"], dtype=np.float64)
    latent = np.sum(feature_safe[:, None] * np.abs(visible), axis=0) * 1.0001
    tongue = np.max(
        np.abs(bias) + np.einsum("k,vkt->vt", latent, np.abs(mix)), axis=0
    ) * 1.0001
    return np.einsum("t,to->o", tongue, np.abs(output)) * 1.0001


def choose_cone_basis(rows: np.ndarray, rng: np.random.Generator) -> np.ndarray:
    """Find four well-conditioned dual-cone rays with rows @ basis >= 0."""
    normalized = rows / np.linalg.norm(rows, axis=1, keepdims=True)
    samples = rng.normal(size=(500_000, rows.shape[1]))
    samples /= np.linalg.norm(samples, axis=1, keepdims=True)
    margins = np.min(normalized @ samples.T, axis=0)
    feasible = samples[margins >= 1e-4]
    feasible_margins = margins[margins >= 1e-4]
    if len(feasible) < rows.shape[1]:
        raise RuntimeError("The learned coefficient rows do not share a usable cone")

    candidate_count = min(30_000, len(feasible))
    candidates = feasible[rng.choice(len(feasible), candidate_count, replace=False)]
    starts = np.concatenate(
        (
            np.argsort(feasible_margins)[-30:],
            rng.choice(len(feasible), 70, replace=False),
        )
    )
    best_score = -np.inf
    best_basis: np.ndarray | None = None
    for start in starts:
        columns = [feasible[start]]
        while len(columns) < rows.shape[1]:
            q = np.linalg.qr(np.column_stack(columns), mode="reduced")[0]
            residual = candidates - candidates @ q @ q.T
            columns.append(candidates[np.linalg.norm(residual, axis=1).argmax()])
        basis = np.column_stack(columns)
        singular = np.linalg.svd(basis, compute_uv=False)
        score = singular[-1] / singular[0]
        if score > best_score:
            best_score = score
            best_basis = basis
    assert best_basis is not None
    return best_basis


def visible_audit(path: Path, rng: np.random.Generator) -> dict:
    document = load(path)
    model = document["model"]
    visible = np.asarray(model["visibleProjection"], dtype=np.float64)
    mix = np.asarray(model["visemeMix"], dtype=np.float64)
    output = np.asarray(model["outputProjection"], dtype=np.float64)
    tensor = np.einsum("fl,vlt,to->vfo", visible, mix, output)
    feature_safe = safe_feature_bounds(visible.shape[0])
    latent_bounds = (
        np.sum(feature_safe[:, None] * np.abs(visible), axis=0) * 1.0001
    )

    feature_unfold = tensor.transpose(1, 0, 2).reshape(visible.shape[0], -1)
    viseme_unfold = tensor.reshape(tensor.shape[0], -1)
    feature_singular = np.linalg.svd(feature_unfold, compute_uv=False)
    viseme_singular = np.linalg.svd(viseme_unfold, compute_uv=False)

    u, singular, vt = np.linalg.svd(feature_unfold, full_matrices=False)
    rank3 = (u[:, :3] * singular[:3]) @ vt[:3]
    rank3_tensor = rank3.reshape(
        tensor.shape[1], tensor.shape[0], tensor.shape[2]
    ).transpose(1, 0, 2)
    rank3_delta = tensor - rank3_tensor
    rank3_safe_error = np.max(
        np.sum(np.abs(rank3_delta) * feature_safe[None, :, None], axis=1), axis=0
    )

    coefficient_rows = np.einsum("vlt,to->vol", mix, output)
    basis = choose_cone_basis(coefficient_rows.reshape(-1, 4), rng)
    transformed_visible = visible @ np.linalg.inv(basis).T
    transformed_coefficients = np.einsum("vol,lk->vok", coefficient_rows, basis)
    transformed_bounds = (
        np.sum(feature_safe[:, None] * np.abs(transformed_visible), axis=0) * 1.0001
    )
    conservative_outputs = output_bounds(model, feature_safe)
    legal_weights = (
        transformed_coefficients
        * transformed_bounds[None, None, :]
        / conservative_outputs[None, :, None]
    )
    reconstructed = np.einsum(
        "fk,vok->vfo", transformed_visible, transformed_coefficients
    )

    # The production graph keeps the affine minimum term at its legacy epoch and
    # fuses only range * unitMix * latent into two accumulator AAPs.  Reconstruct
    # its normalized constants exactly and compare the only changed float32
    # association: range * (unit * latent) versus unit * (range * latent).
    contracted = (
        coefficient_rows.transpose(0, 2, 1)
        * latent_bounds[None, :, None]
        / conservative_outputs[None, None, :]
    )
    contracted_minimum = contracted.min(axis=0)
    contracted_range = contracted.max(axis=0) - contracted_minimum
    contracted_unit = np.divide(
        contracted - contracted_minimum[None, :, :],
        contracted_range[None, :, :],
        out=np.zeros_like(contracted),
        where=contracted_range[None, :, :] > 1e-8,
    )
    recovered_contracted = (
        contracted_minimum[None, :, :]
        + contracted_range[None, :, :] * contracted_unit
    )
    signed_handoff_limit = 2.0
    float_safety_margin = 1e-4
    conservative_handoff_magnitude = 2.0 * np.max(
        np.sum(
            np.abs(contracted_range[None, :, :] * contracted_unit), axis=1
        ),
        axis=0,
    )
    if not np.all(
        conservative_handoff_magnitude
        < signed_handoff_limit - float_safety_margin
    ):
        raise AssertionError(
            f"{path.name}: fused tongue accumulator can clamp during signed handoff"
        )

    sample_count = 100_000
    sample_visemes = rng.dirichlet(np.ones(15), size=sample_count).astype(np.float32)
    sample_latent = rng.uniform(
        -1.0, 1.0, size=(sample_count, visible.shape[1])
    ).astype(np.float32)
    unit_mix = np.einsum(
        "nv,vlo->nlo",
        sample_visemes,
        contracted_unit.astype(np.float32),
        dtype=np.float32,
    )
    ranges32 = contracted_range.astype(np.float32)
    legacy_products = unit_mix * sample_latent[:, :, None]
    legacy_accumulator = np.sum(
        ranges32[None, :, :] * legacy_products,
        axis=1,
        dtype=np.float32,
    )
    fused_accumulator = np.sum(
        unit_mix * (ranges32[None, :, :] * sample_latent[:, :, None]),
        axis=1,
        dtype=np.float32,
    )
    accumulator_delta = np.abs(legacy_accumulator - fused_accumulator)

    prune = {}
    for epsilon in (0.0005, 0.001, 0.002, 0.005, 0.01):
        removed = legal_weights < epsilon
        raw_error = np.max(
            np.sum(np.where(removed, legal_weights, 0.0), axis=2)
            * conservative_outputs[None, :],
            axis=0,
        )
        prune[str(epsilon)] = {
            "removedOf120": int(removed.sum()),
            "worstRawOutputError": raw_error.tolist(),
            "worstFinalErrorAtMaximumAuthority": (
                raw_error * np.asarray((0.30, 0.65))
            ).tolist(),
        }

    return {
        "model": path.name,
        "featureCount": int(visible.shape[0]),
        "featureModeRank": int(np.linalg.matrix_rank(feature_unfold, tol=1e-12)),
        "featureModeSingularValues": feature_singular.tolist(),
        "visemeModeRank": int(np.linalg.matrix_rank(viseme_unfold, tol=1e-12)),
        "visemeModeSingularValues": viseme_singular.tolist(),
        "rank3WorstSafeRawOutputError": rank3_safe_error.tolist(),
        "rank3WorstFinalErrorAtMaximumAuthority": (
            rank3_safe_error * np.asarray((0.30, 0.65))
        ).tolist(),
        "coneBasis": basis.tolist(),
        "coneBasisConditionNumber": float(np.linalg.cond(basis)),
        "minimumTransformedCoefficient": float(transformed_coefficients.min()),
        "maximumLegalDirectWeight": float(legal_weights.max()),
        "maximumReconstructionError": float(np.max(np.abs(tensor - reconstructed))),
        "epochExactProductAccumulator": {
            "maximumAffineCoefficientReconstructionError": float(
                np.max(np.abs(contracted - recovered_contracted))
            ),
            "minimumUnitWeight": float(contracted_unit.min()),
            "maximumUnitWeight": float(contracted_unit.max()),
            "conservativeSignedHandoffMagnitude": (
                conservative_handoff_magnitude.tolist()
            ),
            "signedHandoffSafetyLimit": (
                signed_handoff_limit - float_safety_margin
            ),
            "signedHandoffFits": True,
            "maximumFloat32AssociationDelta": float(accumulator_delta.max()),
            "meanFloat32AssociationDelta": float(accumulator_delta.mean()),
            "legacyProductAapOutputs": int(visible.shape[1] * output.shape[1]),
            "fusedAccumulatorAapOutputs": int(output.shape[1]),
            "removedFinalSignedRows": int(
                visible.shape[1] * output.shape[1] - output.shape[1]
            ),
            "estimatedActiveClipReduction": 12,
            "estimatedActiveCurveReduction": 12,
        },
        "coefficientPruning": prune,
    }


def phone_audit() -> dict:
    models = load(PHONE_MODEL)["models"]
    result = {}
    for name, model in models.items():
        coefficients = np.asarray(model["coefficient"], dtype=np.float64)
        result[name] = {
            "shape": list(coefficients.shape),
            "rank": int(np.linalg.matrix_rank(coefficients, tol=1e-12)),
            "uniqueRows": int(len(np.unique(coefficients, axis=0))),
            "maximumRowDifference": float(
                np.max(np.abs(coefficients - coefficients[0]))
            ),
        }
    return result


def support_audit() -> dict:
    phone = load(PHONE_MODEL)["models"]["Balanced"]
    tongue = load(VISIBLE_MODELS[0])["model"]
    phone_p995 = np.asarray(phone["featureAbsP995"], dtype=np.float64)
    tongue_p995 = np.asarray(tongue["featureAbsP995"], dtype=np.float64)
    safe = safe_feature_bounds(len(phone_p995))
    return {
        "constantStageZeroFactorsPerConfidence": int(
            np.count_nonzero(np.isclose(phone_p995, safe, atol=1e-12))
        ),
        "tongueEnvelopeIsNeverLooserThanPhoneForDynamicStages": bool(
            np.all(tongue_p995[3:] <= phone_p995[3:])
        ),
        "phoneP995": phone_p995.tolist(),
        "tongueP995": tongue_p995.tolist(),
    }


def main() -> None:
    rng = np.random.default_rng(SEED)
    report = {
        "hiddenPhone": phone_audit(),
        "visibleTongue": [visible_audit(path, rng) for path in VISIBLE_MODELS],
        "support": support_audit(),
    }
    print(json.dumps(report, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
