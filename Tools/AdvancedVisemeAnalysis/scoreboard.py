"""Perceptual-proxy scoreboard for viseme reconstruction methods.

The whole point: RMSE is anti-correlated with perceived quality here (the exact
conditional-mean table has the best RMSE and looks the worst). So methods are
scored on how well they MATCH the ground truth's motion statistics, not on how
close they sit to it pointwise.

A method is any function  reconstruct(viseme, voice, teacher_for_fitting) -> (N,15)
that returns a simplex-valued trajectory. `teacher` is passed so a method may
fit parameters offline; a method that peeks at teacher per-frame at eval time is
cheating and its score is meaningless — keep fitting and evaluation on disjoint
halves (fit on first half, score on second) exactly as `split_score` does.

`score(pred, teacher)` returns a dict of metrics. `target()` returns the same
dict computed teacher-vs-teacher (the numbers to match). `distance(pred, teacher)`
collapses the scoreboard into one scalar = mean absolute fractional deviation of
each statistic from the teacher's, so methods can be ranked. Lower is better.
RMSE is reported but NOT in the distance, deliberately.
"""

import numpy as np

FRAME_HZ = 48000.0 / 1024.0
STILL_THRESH = 0.05


def _norm(x):
    x = np.clip(x, 0, None)
    return x / np.maximum(x.sum(1, keepdims=True), 1e-9)


def _switches(viseme):
    sw = np.zeros(len(viseme), bool)
    sw[1:] = viseme[1:] != viseme[:-1]
    return sw


def _speed(x):
    return np.concatenate([[0.0], np.abs(np.diff(x, axis=0)).sum(1)]) * FRAME_HZ


def _bands(x, dt=1.0 / FRAME_HZ):
    # fraction of motion power per frequency band, averaged over channels
    dx = np.diff(x, axis=0)
    edges = [(0, 1), (1, 2), (2, 4), (4, 8), (8, 16), (16, 24)]
    P = np.abs(np.fft.rfft(dx - dx.mean(0), axis=0)) ** 2
    fr = np.fft.rfftfreq(len(dx), dt)
    tot = P.sum()
    return [float(P[(fr >= lo) & (fr < hi)].sum() / max(tot, 1e-12)) for lo, hi in edges]


def score(pred, teacher, viseme):
    pred = _norm(pred)
    sw = _switches(viseme)
    sw_idx = np.where(sw)[0]
    n = len(pred)

    # peakedness
    mx = pred.max(1).mean()
    eff = (1.0 / np.maximum((pred ** 2).sum(1), 1e-9)).mean()
    ent = -(pred * np.log(np.maximum(pred, 1e-12))).sum(1).mean()

    # motion
    sp = _speed(pred)
    d = np.diff(pred, axis=0)
    sg = np.sign(d)
    rev = (sg[1:] * sg[:-1] < 0).sum(1).mean() * FRAME_HZ

    # phase-conditioned motion (transient / between / hold)
    ph = np.full(n, 99)
    for t in sw_idx:
        for k in range(8):
            if t + k < n:
                ph[t + k] = min(ph[t + k], k)
    trans = np.median(sp[ph <= 2])
    betw = np.median(sp[(ph >= 3) & (ph <= 7)])
    hold = np.median(sp[ph == 99]) if (ph == 99).any() else 0.0

    # co-activation at switches
    co = []
    for t in sw_idx:
        a, b = viseme[t - 1], viseme[t]
        if a == b:
            continue
        w = slice(max(0, t - 12), min(n, t + 13))
        co.append(int(((pred[w, a] > 0.15) & (pred[w, b] > 0.15)).sum()))
    coact = float(np.mean(co)) if co else 0.0

    # cross-channel correlation of the residual against a per-index mean
    idx_mean = np.zeros((15, 15))
    for k in range(15):
        m = viseme == k
        if m.any():
            idx_mean[k] = pred[m].mean(0)
    resid = pred - idx_mean[viseme]
    C = np.corrcoef(resid.T)
    iu = np.triu_indices(15, 1)
    xcorr = float(np.nanmean(C[iu]))

    # lag vs teacher (global cross-correlation argmax, in frames)
    tt = _norm(teacher)
    cs = [np.corrcoef(np.roll(pred - pred.mean(0), -L, axis=0)[20:-20].ravel(),
                      (tt - tt.mean(0))[20:-20].ravel())[0, 1] for L in range(-8, 9)]
    lag = int(np.argmax(cs) - 8)

    bands = _bands(pred)
    rmse = float(np.sqrt(((pred - tt) ** 2).mean()))

    return {
        "max": float(mx), "eff": float(eff), "entropy": float(ent),
        "rev": float(rev), "trans": float(trans), "betw": float(betw),
        "hold": float(hold), "coact": coact, "xcorr": xcorr, "lag": lag,
        "b0_1": bands[0], "b1_2": bands[1], "b2_4": bands[2],
        "b4_8": bands[3], "b8_16": bands[4], "b16_24": bands[5],
        "rmse": rmse,
    }


# statistics that enter the ranking distance, with the ones the reviewer's
# complaint is about (hold, rev, coact, lag) up-weighted.
_WEIGHTS = {
    "hold": 3.0, "rev": 2.0, "coact": 2.0, "lag": 2.0,
    "trans": 1.0, "betw": 1.0, "eff": 1.0, "entropy": 1.0, "max": 1.0,
    "xcorr": 1.0, "b0_1": 0.5, "b1_2": 0.5, "b2_4": 0.5,
    "b4_8": 0.5, "b8_16": 0.5, "b16_24": 0.5,
}


def target(teacher, viseme):
    return score(teacher, teacher, viseme)


def distance(pred, teacher, viseme):
    s = score(pred, teacher, viseme)
    t = target(teacher, viseme)
    num = den = 0.0
    for k, w in _WEIGHTS.items():
        if k == "lag":
            dev = abs(s[k] - t[k]) / 4.0  # frames, normalized by ~one syllable
        else:
            dev = abs(s[k] - t[k]) / max(abs(t[k]), 1e-6)
        num += w * dev
        den += w
    return num / den


def load(path=None):
    import os
    if path is None:
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "..", "..", "VisemeCapture", "capture_clean.npz")
    z = np.load(path)
    return z["teacher"], z["viseme"].astype(int), z["voice"]


def split_score(reconstruct, label=""):
    """Fit on first half, score on second. reconstruct(viseme, voice, teacher_fit)."""
    teacher, viseme, voice = load()
    n = len(teacher)
    half = n // 2
    pred = reconstruct(viseme, voice, teacher, half)
    sc = slice(half, n)
    s = score(pred[sc], teacher[sc], viseme[sc])
    dist = distance(pred[sc], teacher[sc], viseme[sc])
    return s, dist


def print_row(label, s, t=None):
    keys = ["lag", "hold", "rev", "coact", "trans", "betw", "eff", "max",
            "entropy", "xcorr", "rmse"]
    hdr = "  ".join(f"{k:>7}" for k in keys)
    if t is not None:
        print(f"{'':<22}{hdr}")
        print(f"{'TARGET (teacher)':<22}" + "  ".join(f"{t[k]:7.3f}" for k in keys))
    print(f"{label:<22}" + "  ".join(f"{s[k]:7.3f}" for k in keys))


if __name__ == "__main__":
    teacher, viseme, voice = load()
    t = target(teacher, viseme)
    print_row("teacher vs teacher", t)
    print(f"\ndistance(teacher, teacher) = {distance(teacher, teacher, viseme):.4f}  (must be 0)")

    # naive baseline: per-index mean target, two-pole observer
    def naive(viseme, voice, teacher_fit, half, tau=0.017):
        import math
        idx_mean = np.zeros((15, 15))
        for k in range(15):
            m = viseme[:half] == k
            idx_mean[k] = teacher_fit[:half][m].mean(0) if m.any() else np.eye(15)[k]
        tgt = idx_mean[viseme]
        a = 1 - math.exp(-(1.0 / FRAME_HZ) / tau)
        y = tgt.copy()
        for _ in range(2):
            st = y[0].copy(); o = np.empty_like(y)
            for i in range(len(y)):
                st = st + a * (y[i] - st); o[i] = st
            y = o
        return _norm(y)

    s, dist = split_score(lambda v, vo, tf, h: naive(v, vo, tf, h))
    print()
    print_row("naive tau=17ms", s, t)
    print(f"distance = {dist:.4f}")
