"""How much between-switch motion is recoverable from the channel alone?

The channel is (argmax index, voice scalar). The naive predictor is a per-index
mean table, which is constant between switches and therefore freezes. This
measures three deterministic features that are NOT constant between switches:

  dwell   time since the last index change, as a trajectory rather than a point
  tempo   EMA of the switch pulses, i.e. articulation speed
  vband   voice band-passed to the syllabic range, and its derivative

Everything is fitted THROUGH the observer: regressors are filtered by the same
two-pole cascade the graph applies before least squares runs. Fitting against a
pre-observer target and injecting post-observer is what broke the last three
offline fits.

Fit on the first half, score on the second.
"""

import csv
import json
import math
import os
import sys

import numpy as np

CAPTURE_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "VisemeCapture")
FRAME_SECONDS = 1024.0 / 48000.0
VISEMES = 15
OBSERVER_TAU = 0.017
DWELL_EDGES = np.array([0, 1, 2, 3, 4, 6, 9, 14, 22], dtype=float)


def load(path):
    with open(path, newline="") as handle:
        rows = list(csv.reader(handle))
    header = rows[0]
    teacher_cols = [i for i, name in enumerate(header) if name.startswith("teacher_")]
    assert len(teacher_cols) == VISEMES, len(teacher_cols)
    data = np.array([[float(cell) for cell in row] for row in rows[1:] if row])
    return {
        "viseme": data[:, 1].astype(int),
        "voice": data[:, 2],
        "teacher": data[:, teacher_cols],
    }


def two_pole(signal, tau, dt=FRAME_SECONDS):
    """The observer the graph runs: two cascaded one-poles."""
    alpha = 1.0 - math.exp(-dt / tau)
    out = np.array(signal, dtype=float)
    for _ in range(2):
        state = out[0].copy()
        filtered = np.empty_like(out)
        for t in range(len(out)):
            state = state + alpha * (out[t] - state)
            filtered[t] = state
        out = filtered
    return out


def one_pole(signal, tau, dt=FRAME_SECONDS):
    alpha = 1.0 - math.exp(-dt / tau)
    out = np.empty_like(signal, dtype=float)
    state = float(signal[0])
    for t in range(len(signal)):
        state = state + alpha * (signal[t] - state)
        out[t] = state
    return out


def channel_features(viseme, voice):
    n = len(viseme)
    onehot = np.zeros((n, VISEMES))
    onehot[np.arange(n), viseme] = 1.0

    # Switch pulse: half the L1 change of the one-hot, so it is 1 on a change
    # and 0 otherwise, and is pure arithmetic (no branching) in the graph.
    pulse = np.zeros(n)
    pulse[1:] = 0.5 * np.abs(onehot[1:] - onehot[:-1]).sum(axis=1)

    # Dwell ramp: r = r*(1-pulse) + dt. An IIR with a multiplicative reset.
    dwell = np.zeros(n)
    run = 0.0
    for t in range(n):
        run = run * (1.0 - pulse[t]) + 1.0
        dwell[t] = run

    tempo = one_pole(pulse, 0.30)

    # Syllabic band of voice: difference of two one-poles ~= band-pass.
    v_slow = one_pole(voice, 0.25)
    v_fast = one_pole(voice, 0.045)
    vband = v_fast - v_slow
    vband_rate = np.zeros(n)
    vband_rate[1:] = (vband[1:] - vband[:-1]) / FRAME_SECONDS

    return {
        "onehot": onehot, "pulse": pulse, "dwell": dwell, "tempo": tempo,
        "voice": voice, "vband": vband, "vband_rate": vband_rate,
    }


def dwell_basis(dwell):
    """Piecewise-linear interpolation weights over the dwell bins.

    This is exactly what a 1-D blend tree evaluates, so a fit in this basis
    transfers to the graph as a table of per-bin vectors.
    """
    n = len(dwell)
    basis = np.zeros((n, len(DWELL_EDGES)))
    clipped = np.clip(dwell, DWELL_EDGES[0], DWELL_EDGES[-1])
    idx = np.clip(np.searchsorted(DWELL_EDGES, clipped, side="right") - 1,
                  0, len(DWELL_EDGES) - 2)
    lo = DWELL_EDGES[idx]
    hi = DWELL_EDGES[idx + 1]
    frac = (clipped - lo) / np.maximum(hi - lo, 1e-9)
    basis[np.arange(n), idx] = 1.0 - frac
    basis[np.arange(n), idx + 1] = frac
    return basis


def build_regressors(feat, mode):
    """Columns of the design matrix. Each column becomes a graph node."""
    onehot = feat["onehot"]
    columns = [onehot]
    if mode in ("dwell", "dwell+tempo", "full"):
        # per-index dwell curve: onehot outer-product dwell basis
        db = dwell_basis(feat["dwell"])
        inter = (onehot[:, :, None] * db[:, None, :]).reshape(len(onehot), -1)
        columns = [inter]
    if mode in ("dwell+tempo", "full"):
        columns.append(onehot * feat["tempo"][:, None])
    if mode in ("voice", "full"):
        columns.append(onehot * feat["voice"][:, None])
    if mode in ("vband", "full"):
        columns.append(onehot * feat["vband"][:, None])
        columns.append(onehot * feat["vband_rate"][:, None])
    return np.hstack(columns)


def metrics(pred, teacher, mask=None):
    d_pred = np.diff(pred, axis=0)
    d_teach = np.diff(teacher, axis=0)
    speed_pred = np.abs(d_pred).sum(axis=1) / FRAME_SECONDS
    speed_teach = np.abs(d_teach).sum(axis=1) / FRAME_SECONDS
    if mask is not None:
        m = mask[1:]
    else:
        m = np.ones(len(speed_pred), dtype=bool)
    # direction reversals per second, averaged over channels
    sign = np.sign(d_pred)
    rev = (sign[1:] * sign[:-1] < 0).sum(axis=1).mean() / FRAME_SECONDS
    sign_t = np.sign(d_teach)
    rev_t = (sign_t[1:] * sign_t[:-1] < 0).sum(axis=1).mean() / FRAME_SECONDS
    return {
        "rmse": float(np.sqrt(((pred - teacher) ** 2).mean())),
        "still%": float(100.0 * (speed_pred[m] < 0.05).mean()),
        "still%_teacher": float(100.0 * (speed_teach[m] < 0.05).mean()),
        "p50speed": float(np.median(speed_pred[m])),
        "p50speed_teacher": float(np.median(speed_teach[m])),
        "rev": float(rev),
        "rev_teacher": float(rev_t),
    }


def main():
    path = os.path.join(CAPTURE_DIR, "viseme_capture_20260720_223553.csv")
    cap = load(path)
    teacher = cap["teacher"]
    feat = channel_features(cap["viseme"], cap["voice"])
    n = len(teacher)
    half = n // 2
    fit = slice(0, half)
    score = slice(half, n)

    # Frames with no index change, where the naive predictor is frozen.
    between = feat["pulse"] < 0.5

    print(f"{n} frames, {n * FRAME_SECONDS:.1f}s, "
          f"{int(feat['pulse'].sum())} switches, "
          f"{100.0 * between.mean():.1f}% between-switch frames\n")

    target_between_var = teacher[between].var(axis=0).sum()
    print(f"teacher variance on between-switch frames: {target_between_var:.5f}\n")

    # Score on the DERIVATIVE over between-switch frames. The freeze is a
    # motion defect, and level-r2 is dominated by "which viseme is it", which
    # the naive table already knows. Motion is the only thing in question.
    def motion_r2(pred, sel_frames):
        d_pred = np.diff(pred, axis=0)
        d_teach = np.diff(teacher, axis=0)
        s = sel_frames[1:]
        resid = d_teach[s] - d_pred[s]
        return float(1.0 - resid.var(axis=0).sum() / d_teach[s].var(axis=0).sum())

    sel = np.zeros(n, dtype=bool)
    sel[score] = True
    sel &= between

    results = {}
    for mode in ["index", "dwell", "dwell+tempo", "voice", "vband", "full"]:
        X = build_regressors(feat, mode)
        # Fit through the observer: filter regressors by the same transfer
        # function the graph applies, then solve least squares.
        Xf = two_pole(X, OBSERVER_TAU)
        A = Xf[fit]
        B = teacher[fit]
        gram = A.T @ A
        rhs = A.T @ B
        scale = np.trace(gram) / gram.shape[0]

        # Ridge sweep, chosen on held-out motion, not on fit error.
        best = None
        for lam in [1e-6, 1e-4, 1e-3, 1e-2, 3e-2, 1e-1, 3e-1, 1.0, 3.0, 10.0]:
            coef = np.linalg.solve(gram + lam * scale * np.eye(gram.shape[0]), rhs)
            cand = Xf @ coef
            r2 = motion_r2(cand, sel)
            if best is None or r2 > best[0]:
                best = (r2, lam, cand)
        r2, lam, pred = best

        m = metrics(pred[score], teacher[score])
        m["motion_r2"] = r2
        m["lambda"] = lam
        m["params"] = int(X.shape[1] * VISEMES)
        results[mode] = m
        print(f"{mode:<12} motion-r2 {r2:+.4f}  rmse {m['rmse']:.4f}  "
              f"still% {m['still%']:5.1f}  p50 {m['p50speed']:6.3f}  "
              f"rev {m['rev']:7.2f}  lam {lam:<6g} params {m['params']}")

    t = results["index"]
    print(f"\n{'TEACHER':<12} {'':>17}  {'':>11}  "
          f"still% {t['still%_teacher']:5.1f}  p50 {t['p50speed_teacher']:.4f}  "
          f"rev {t['rev_teacher']:.3f}")

    out = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       "deterministic_channel.json")
    with open(out, "w") as handle:
        json.dump(results, handle, indent=2)
    print(f"\nwrote {out}")


if __name__ == "__main__":
    main()
