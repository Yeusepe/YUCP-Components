#!/usr/bin/env python3
"""Refinement ladder for the transition model, run in order:

  1. Empirical-Bayes shrinkage of the per-pair residual table toward the
     factored alpha*(P[prev]-C[cur,0]) prediction, with per-cell weights
     lambda = n/(n+n0). Stein-type: should dominate both endpoints.
  2. Functional PCA on the residual trajectories: R[p,c](age) expanded in K
     shared age eigenfunctions with per-pair coefficient vectors, replacing
     the separable Delta*d(age) assumption.
  3. Aitchison-geometry refit: the baked tables fitted in isometric log-ratio
     coordinates (where the simplex is a vector space) and exported pointwise.

Hyperparameters (n0, K, ilr epsilon) are selected on the 'development' split;
all reported scores come from 'heldout'. The reference lines are the shipped
baseline C[cur, age], the explicit fitted pull, and the free lookup table.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

import analyze_reconstruction_limits as limits
import analyze_transition_crossfade as crossfade
import analyze_retention_residual as residual

FRAME_SECONDS = limits.FRAME_SECONDS
AGE_BINS = limits.AGE_BINS
VISEME_COUNT = limits.VISEME_COUNT
MIN_PAIR = residual.MIN_PAIR_FRAMES


# --------------------------------------------------------------------------
# Shared pieces
# --------------------------------------------------------------------------

def residual_trajectory_table(fit: dict, table: np.ndarray) -> tuple:
    """R[p, c, ageBin, ch]: mean residual trajectory per ordered pair,
    with per-(p, c, ageBin) frame counts."""
    age_bin = np.minimum(fit["age"], AGE_BINS - 1)
    baseline = table[fit["winner"], age_bin]
    resid = fit["continuous"] - baseline

    keys = (
        (fit["previous"] * VISEME_COUNT + fit["winner"]) * AGE_BINS + age_bin
    )
    cells = VISEME_COUNT * VISEME_COUNT * AGE_BINS
    means, counts = limits.cell_means(keys, resid, cells)
    return (
        means.reshape(VISEME_COUNT, VISEME_COUNT, AGE_BINS, VISEME_COUNT),
        counts.reshape(VISEME_COUNT, VISEME_COUNT, AGE_BINS),
    )


def factored_parts(fit: dict, table: np.ndarray) -> tuple:
    """The validated factored model pieces: alpha, d(age), basis[p, c, ch]."""
    model = residual.fit_residual_model(fit, table)
    decay = np.asarray(model["decay"])
    P = table[:, -1, :]
    C0 = table[:, 0, :]
    basis = P[:, None, :] - C0[None, :, :]

    delta = model["delta"].reshape(VISEME_COUNT, VISEME_COUNT, VISEME_COUNT)
    ok = model["pairCounts"].reshape(VISEME_COUNT, VISEME_COUNT) >= MIN_PAIR
    alpha = float(
        (delta[ok] * basis[ok]).sum() / (basis[ok] * basis[ok]).sum()
    )
    return alpha, decay, basis, delta, model["pairCounts"].reshape(
        VISEME_COUNT, VISEME_COUNT
    )


def scores(truth, prediction, transition):
    everything = np.ones(len(truth), bool)
    return {
        "all": limits.score(truth, prediction, everything),
        "transition": limits.score(truth, prediction, transition),
    }


def apply_pair_age_model(evaluate: dict, table: np.ndarray, correction):
    """correction[p, c, ageBin, ch] added to the baseline trajectory."""
    age_bin = np.minimum(evaluate["age"], AGE_BINS - 1)
    baseline = table[evaluate["winner"], age_bin]
    return baseline + correction[
        evaluate["previous"], evaluate["winner"], age_bin
    ]


# --------------------------------------------------------------------------
# 1. Empirical-Bayes shrinkage
# --------------------------------------------------------------------------

def shrunk_correction(
    table_traj: np.ndarray,
    counts: np.ndarray,
    alpha: float,
    decay: np.ndarray,
    basis: np.ndarray,
    n0: float,
) -> np.ndarray:
    """lambda-weighted blend of the free table and the factored prediction."""
    factored = (
        alpha * basis[:, :, None, :] * decay[None, None, :, None]
    )
    lam = counts / (counts + n0)
    return (
        lam[..., None] * table_traj + (1.0 - lam[..., None]) * factored
    )


# --------------------------------------------------------------------------
# 2. Functional PCA over age
# --------------------------------------------------------------------------

def fpca_correction(
    table_traj: np.ndarray,
    counts: np.ndarray,
    alpha: float,
    decay: np.ndarray,
    basis: np.ndarray,
    n0: float,
    k: int,
) -> np.ndarray:
    """Rank-k expansion along the age axis of the SHRUNK residual table.

    Unfold to (pair*channel, ageBin), weight rows by evidence, SVD, keep k
    age eigenfunctions, project the shrunk table onto them. k=1 with a fixed
    exponential is the separable model; free k generalizes it.
    """
    shrunk = shrunk_correction(table_traj, counts, alpha, decay, basis, n0)
    matrix = shrunk.transpose(0, 1, 3, 2).reshape(-1, AGE_BINS)
    weights = np.sqrt(
        np.repeat(counts.sum(axis=2).reshape(-1), VISEME_COUNT) + 1.0
    )
    _, _, vt = np.linalg.svd(matrix * weights[:, None], full_matrices=False)
    phi = vt[:k]  # k x AGE_BINS age eigenfunctions
    coefficients = matrix @ phi.T  # projections, evidence-free is fine here
    reconstructed = coefficients @ phi
    return reconstructed.reshape(
        VISEME_COUNT, VISEME_COUNT, VISEME_COUNT, AGE_BINS
    ).transpose(0, 1, 3, 2)


# --------------------------------------------------------------------------
# 3. Aitchison (ilr) refit of the baked tables
# --------------------------------------------------------------------------

def ilr_basis() -> np.ndarray:
    """Orthonormal Helmert-style contrast matrix, (V-1) x V."""
    v = VISEME_COUNT
    basis = np.zeros((v - 1, v))
    for i in range(1, v):
        basis[i - 1, :i] = 1.0 / i
        basis[i - 1, i] = -1.0
        basis[i - 1] *= np.sqrt(i / (i + 1.0))
    return basis


def to_ilr(weights: np.ndarray, epsilon: float) -> np.ndarray:
    padded = np.clip(weights, epsilon, None)
    padded = padded / padded.sum(axis=-1, keepdims=True)
    log = np.log(padded)
    return log @ ILR.T


def from_ilr(coords: np.ndarray) -> np.ndarray:
    log = coords @ ILR
    exp = np.exp(log - log.max(axis=-1, keepdims=True))
    return exp / exp.sum(axis=-1, keepdims=True)


ILR = ilr_basis()


def ilr_tables(fit: dict, epsilon: float) -> tuple:
    """Winner-age trajectory table and pair-age residual table, both fitted
    as conditional means in ilr coordinates."""
    coords = to_ilr(fit["continuous"], epsilon)
    dim = VISEME_COUNT - 1

    age_bin = np.minimum(fit["age"], AGE_BINS - 1)
    keys = fit["winner"] * AGE_BINS + age_bin
    means, counts = limits.cell_means(keys, coords, VISEME_COUNT * AGE_BINS)
    winner_means, winner_counts = limits.cell_means(
        fit["winner"], coords, VISEME_COUNT
    )
    global_mean = coords.mean(axis=0)
    table = means.reshape(VISEME_COUNT, AGE_BINS, dim)
    table_counts = counts.reshape(VISEME_COUNT, AGE_BINS)
    for winner in range(VISEME_COUNT):
        fallback = (
            winner_means[winner]
            if winner_counts[winner] >= limits.MIN_CELL_FRAMES
            else global_mean
        )
        for bin_index in range(AGE_BINS):
            if table_counts[winner, bin_index] < limits.MIN_CELL_FRAMES:
                table[winner, bin_index] = fallback

    resid = coords - table[fit["winner"], age_bin]
    pair_keys = (
        (fit["previous"] * VISEME_COUNT + fit["winner"]) * AGE_BINS + age_bin
    )
    pair_means, pair_counts = limits.cell_means(
        pair_keys, resid, VISEME_COUNT * VISEME_COUNT * AGE_BINS
    )
    pair_table = pair_means.reshape(
        VISEME_COUNT, VISEME_COUNT, AGE_BINS, dim
    )
    pair_counts = pair_counts.reshape(VISEME_COUNT, VISEME_COUNT, AGE_BINS)
    return table, pair_table, pair_counts


def ilr_prediction(
    evaluate: dict,
    table: np.ndarray,
    pair_table: np.ndarray,
    pair_counts: np.ndarray,
    n0: float,
) -> np.ndarray:
    age_bin = np.minimum(evaluate["age"], AGE_BINS - 1)
    base = table[evaluate["winner"], age_bin]
    lam = pair_counts / (pair_counts + n0)
    correction = (
        lam[..., None] * pair_table
    )[evaluate["previous"], evaluate["winner"], age_bin]
    return from_ilr(base + correction)


# --------------------------------------------------------------------------

def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=limits.DEFAULT_CACHE)
    parser.add_argument(
        "--json", type=Path,
        default=Path(__file__).with_name("transition_refinements.json"),
    )
    parser.add_argument(
        "--markdown", type=Path,
        default=Path(__file__).with_name("transition_refinements.md"),
    )
    args = parser.parse_args(argv)

    records = limits.load_utterances(args.cache)
    fit = crossfade.gather(records, "fit")
    dev = crossfade.gather(records, "development")
    evaluate = crossfade.gather(records, "heldout")
    truth = evaluate["continuous"]
    transition = evaluate["age"] < limits.TRANSITION_HORIZON_FRAMES

    table = crossfade.winner_age_table(fit)
    table_traj, counts = residual_trajectory_table(fit, table)
    alpha, decay, basis, delta, pair_counts = factored_parts(fit, table)

    results = {}

    # References.
    age_bin = np.minimum(evaluate["age"], AGE_BINS - 1)
    baseline = table[evaluate["winner"], age_bin]
    results["baseline"] = scores(truth, baseline, transition)

    explicit = baseline + (
        delta[evaluate["previous"], evaluate["winner"]]
        * decay[age_bin][:, None]
    )
    results["explicitPull"] = scores(truth, explicit, transition)

    free_table = apply_pair_age_model(evaluate, table, table_traj)
    results["freeTable"] = scores(truth, free_table, transition)

    def dev_mse(correction) -> float:
        prediction = apply_pair_age_model(dev, table, correction)
        return limits.score(
            dev["continuous"], prediction, np.ones(len(dev["winner"]), bool)
        )["mse"]

    # ---- 1. Shrinkage: pick n0 on development.
    n0_grid = (2.0, 5.0, 10.0, 20.0, 40.0, 80.0, 160.0, 320.0)
    n0_best = min(
        n0_grid,
        key=lambda n0: dev_mse(
            shrunk_correction(table_traj, counts, alpha, decay, basis, n0)
        ),
    )
    shrunk = shrunk_correction(
        table_traj, counts, alpha, decay, basis, n0_best
    )
    results["shrunk"] = scores(
        truth, apply_pair_age_model(evaluate, table, shrunk), transition
    )
    results["shrunk"]["n0"] = n0_best

    # ---- 2. FPCA on the shrunk table: pick K on development.
    k_grid = (1, 2, 3, 4, 6)
    k_best = min(
        k_grid,
        key=lambda k: dev_mse(
            fpca_correction(
                table_traj, counts, alpha, decay, basis, n0_best, k
            )
        ),
    )
    fpca = fpca_correction(
        table_traj, counts, alpha, decay, basis, n0_best, k_best
    )
    results["fpca"] = scores(
        truth, apply_pair_age_model(evaluate, table, fpca), transition
    )
    results["fpca"]["k"] = k_best

    # ---- 3. ilr refit: pick epsilon and n0 on development.
    ilr_grid = [
        (eps, n0)
        for eps in (1e-4, 1e-3, 1e-2)
        for n0 in (10.0, 40.0, 160.0)
    ]

    def ilr_dev_mse(eps: float, n0: float) -> float:
        t, p, c = ilr_tables(fit, eps)
        prediction = ilr_prediction(dev, t, p, c, n0)
        return limits.score(
            dev["continuous"], prediction, np.ones(len(dev["winner"]), bool)
        )["mse"]

    eps_best, ilr_n0_best = min(
        ilr_grid, key=lambda pair: ilr_dev_mse(*pair)
    )
    ilr_t, ilr_p, ilr_c = ilr_tables(fit, eps_best)
    results["ilr"] = scores(
        truth,
        ilr_prediction(evaluate, ilr_t, ilr_p, ilr_c, ilr_n0_best),
        transition,
    )
    results["ilr"]["epsilon"] = eps_best
    results["ilr"]["n0"] = ilr_n0_best

    # ---- Report.
    order = [
        ("baseline", "baseline C[cur, age] (ships today)"),
        ("explicitPull", "explicit fitted pull (previous best)"),
        ("freeTable", "free lookup table (reference)"),
        ("shrunk", f"1. shrinkage (n0={results['shrunk']['n0']:.0f})"),
        ("fpca", f"2. FPCA on shrunk (K={results['fpca']['k']})"),
        (
            "ilr",
            f"3. ilr refit (eps={results['ilr']['epsilon']:g}, "
            f"n0={results['ilr']['n0']:.0f})",
        ),
    ]
    lines = ["# Transition refinement ladder (held-out)\n"]
    lines.append("| model | RMSE all | RMSE transition |")
    lines.append("|---|---|---|")
    for key, label in order:
        entry = results[key]
        lines.append(
            f"| {label} | {entry['all']['rmse']:.5f} "
            f"| {entry['transition']['rmse']:.5f} |"
        )
    text = "\n".join(lines) + "\n"

    args.json.write_text(
        json.dumps(residual.__dict__.get("json_safe", lambda v: v)(results),
                   indent=2, sort_keys=True, default=float),
        encoding="utf-8",
    )
    args.markdown.write_text(text, encoding="utf-8")
    print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
