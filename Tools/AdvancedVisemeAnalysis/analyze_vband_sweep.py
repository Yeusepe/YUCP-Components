"""Is the band-passed-voice win real, and where is the band?

analyze_deterministic_channel.py found that conditioning on the syllabic band
of the voice scalar beats both the frozen per-index baseline and conditioning
on the voice level. The margin was small, so this checks it survives a
fit/score swap and locates the band rather than accepting the first guess.
"""

import itertools
import os

import numpy as np

from analyze_deterministic_channel import (
    CAPTURE_DIR, FRAME_SECONDS, OBSERVER_TAU, VISEMES,
    channel_features, load, one_pole, two_pole, metrics)


def fit_score(Xf, teacher, fit, score, between, lams):
    d_teach = np.diff(teacher, axis=0)
    sel = np.zeros(len(teacher), dtype=bool)
    sel[score] = True
    sel &= between
    s = sel[1:]

    A, B = Xf[fit], teacher[fit]
    gram, rhs = A.T @ A, A.T @ B
    scale = np.trace(gram) / gram.shape[0]
    best = None
    for lam in lams:
        coef = np.linalg.solve(gram + lam * scale * np.eye(gram.shape[0]), rhs)
        pred = Xf @ coef
        resid = d_teach[s] - np.diff(pred, axis=0)[s]
        r2 = 1.0 - resid.var(axis=0).sum() / d_teach[s].var(axis=0).sum()
        if best is None or r2 > best[0]:
            best = (float(r2), lam, pred)
    return best


LAMS = [1e-4, 1e-3, 1e-2, 1e-1, 3e-1, 1.0, 3.0, 10.0]


def main():
    cap = load(os.path.join(CAPTURE_DIR, "viseme_capture_20260720_223553.csv"))
    teacher = cap["teacher"]
    feat = channel_features(cap["viseme"], cap["voice"])
    n = len(teacher)
    half = n // 2
    between = feat["pulse"] < 0.5
    onehot = feat["onehot"]
    voice = cap["voice"]

    splits = {
        "first->second": (slice(0, half), slice(half, n)),
        "second->first": (slice(half, n), slice(0, half)),
    }

    print("baseline (per-index only, frozen between switches)")
    for name, (fit, score) in splits.items():
        r2, lam, pred = fit_score(two_pole(onehot, OBSERVER_TAU), teacher,
                                  fit, score, between, LAMS)
        m = metrics(pred[score], teacher[score])
        print(f"  {name:<15} motion-r2 {r2:+.4f}  rmse {m['rmse']:.4f}  "
              f"rev {m['rev']:7.2f}  still% {m['still%']:5.1f}")

    print("\nband sweep: v_fast(tau_f) - v_slow(tau_s), plus its rate")
    print(f"  {'tau_f':>6} {'tau_s':>6}   "
          f"{'r2 A':>8} {'r2 B':>8} {'mean':>8}  {'rmse':>7} {'rev':>7} {'still%':>7}")
    rows = []
    for tau_f, tau_s in itertools.product([0.025, 0.035, 0.045, 0.060, 0.080],
                                          [0.15, 0.25, 0.40, 0.70]):
        if tau_f >= tau_s:
            continue
        band = one_pole(voice, tau_f) - one_pole(voice, tau_s)
        rate = np.zeros(n)
        rate[1:] = (band[1:] - band[:-1]) / FRAME_SECONDS
        X = np.hstack([onehot,
                       onehot * band[:, None],
                       onehot * rate[:, None]])
        Xf = two_pole(X, OBSERVER_TAU)
        out = []
        for name, (fit, score) in splits.items():
            r2, lam, pred = fit_score(Xf, teacher, fit, score, between, LAMS)
            out.append((r2, pred, score))
        mean_r2 = 0.5 * (out[0][0] + out[1][0])
        m = metrics(out[0][1][out[0][2]], teacher[out[0][2]])
        rows.append((mean_r2, tau_f, tau_s, out[0][0], out[1][0], m))
        print(f"  {tau_f:6.3f} {tau_s:6.3f}   "
              f"{out[0][0]:+8.4f} {out[1][0]:+8.4f} {mean_r2:+8.4f}  "
              f"{m['rmse']:7.4f} {m['rev']:7.2f} {m['still%']:7.1f}")

    rows.sort(reverse=True)
    best = rows[0]
    print(f"\nbest band: tau_f={best[1]:.3f} tau_s={best[2]:.3f}  "
          f"mean motion-r2 {best[0]:+.4f}")
    m = best[5]
    print(f"  teacher rev {m['rev_teacher']:.2f}  still% {m['still%_teacher']:.1f}  "
          f"p50 {m['p50speed_teacher']:.3f}")


if __name__ == "__main__":
    main()
