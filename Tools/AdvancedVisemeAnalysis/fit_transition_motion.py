#!/usr/bin/env python3
"""Fit the decoder transition duration by MOTION metrics, not RMSE.

Target: the original continuous Oculus weights. Given: their argmax stream.
The research verdict (staircasing theory, trend filtering, L1 camera paths,
window of visibility): the reconstruction must spread displacement
continuously across transitions as piecewise-polynomial motion with knots at
switch events. The runtime realization is the existing pairwise interruptible
crossfade between decoder trajectory states; this script fits its duration T
on the corpus by matching the ORIGINAL's motion statistics:

  - motion energy ratio      (sum of per-frame L1 speed vs original; 1 = match)
  - speed-distribution W1    (Wasserstein-1 between per-frame speed dists)
  - displacement concentration (fraction of total motion in the fastest 10%
    of frames; stairs concentrate displacement, the original spreads it)
  - spectral log-distance    (mean log-PSD distance of channels, 0.5-8 Hz)
  - argmax lag               (frames after a switch until reconstruction's
    argmax matches the observed winner — sync diagnostic)
  - RMSE                     (reported only, never optimized)

Simulates the shipped pipeline per candidate T: decoder table C[w, age] with
a destination-interruptible linear crossfade (outgoing trajectory continues
advancing, Unity semantics), followed by the two-pole observer at the shipped
response constant. Scores on the held-out split.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

import analyze_reconstruction_limits as limits
import analyze_transition_crossfade as crossfade

FRAME = limits.FRAME_SECONDS
AB = limits.AGE_BINS
OBSERVER_TAU = 0.017  # shipped visemeResponseSeconds

DURATIONS_MS = (0.0, 21.3, 42.7, 64.0, 72.0, 85.3, 106.7, 128.0, 149.3,
                170.7, 213.3)


def observer(signal: np.ndarray, tau: float) -> np.ndarray:
    alpha = 1.0 - np.exp(-FRAME / tau)
    fast = signal[0].copy()
    slow = signal[0].copy()
    out = np.empty_like(signal)
    for t in range(len(signal)):
        fast += alpha * (signal[t] - fast)
        slow += alpha * (fast - slow)
        out[t] = slow
    return out


def simulate(rec: dict, table: np.ndarray, duration_s: float) -> np.ndarray:
    """Raw decoder output under a destination-interruptible crossfade."""
    winners = rec["winners"]
    n = len(winners)
    raw = np.empty((n, 15))
    cur = winners[0]
    prev = cur
    age = 0
    prev_age = 0
    for t in range(n):
        if t > 0 and winners[t] != winners[t - 1]:
            prev, cur = cur, winners[t]
            prev_age = age + 1
            age = 0
        elif t > 0:
            age += 1
            prev_age += 1
        target = table[cur, min(age, AB - 1)]
        if duration_s <= 0.0:
            raw[t] = target
            continue
        u = min(1.0, age * FRAME / duration_s)
        outgoing = table[prev, min(prev_age, AB - 1)]
        raw[t] = (1.0 - u) * outgoing + u * target
    return raw


def speed(x: np.ndarray) -> np.ndarray:
    return np.abs(np.diff(x, axis=0)).sum(axis=1)


def spectrum(x: np.ndarray) -> tuple:
    total = None
    for ch in range(15):
        p = limits.welch_psd(x[:, ch], 64)
        if p.size:
            total = p if total is None else total + p
    freqs = np.fft.rfftfreq(64, d=FRAME)
    return freqs[1:], total[1:]


def metrics(recon: np.ndarray, orig: np.ndarray, winners: np.ndarray) -> dict:
    s_r, s_o = speed(recon), speed(orig)
    energy_ratio = float(s_r.sum() / max(s_o.sum(), 1e-9))

    q = np.linspace(0.01, 0.99, 99)
    w1 = float(np.abs(np.quantile(s_r, q) - np.quantile(s_o, q)).mean())

    def concentration(s):
        s = np.sort(s)[::-1]
        k = max(1, len(s) // 10)
        return float(s[:k].sum() / max(s.sum(), 1e-9))

    f, p_r = spectrum(recon)
    _, p_o = spectrum(orig)
    band = (f >= 0.5) & (f <= 8.0)
    sld = float(np.abs(np.log10(p_r[band] + 1e-12)
                       - np.log10(p_o[band] + 1e-12)).mean())

    lags = []
    change = np.flatnonzero(np.diff(winners)) + 1
    amax = recon.argmax(axis=1)
    for c in change:
        w = winners[c]
        lag = 0
        while c + lag < len(winners) and amax[c + lag] != w and lag < 20:
            lag += 1
        lags.append(lag)

    rmse = float(np.sqrt(((recon - orig) ** 2).mean()))
    return {
        "energyRatio": energy_ratio,
        "speedW1": w1,
        "concentrationRecon": concentration(s_r),
        "concentrationOrig": concentration(s_o),
        "spectralLogDist": sld,
        "argmaxLagMeanMs": float(np.mean(lags) * FRAME * 1e3),
        "rmse": rmse,
    }


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=limits.DEFAULT_CACHE)
    parser.add_argument(
        "--json", type=Path,
        default=Path(__file__).with_name("transition_motion_fit.json"))
    args = parser.parse_args(argv)

    records = limits.load_utterances(args.cache)
    fit_split = crossfade.gather(records, "fit")
    table = crossfade.winner_age_table(fit_split)
    heldout = [r for r in records if r["split"] == "heldout"]

    results = {}
    for duration_ms in DURATIONS_MS:
        recon_all, orig_all, winners_all = [], [], []
        for rec in heldout:
            raw = simulate(rec, table, duration_ms / 1e3)
            recon_all.append(observer(raw, OBSERVER_TAU))
            orig_all.append(rec["continuous"])
            winners_all.append(rec["winners"])
        m = metrics(np.concatenate(recon_all), np.concatenate(orig_all),
                    np.concatenate(winners_all))
        results[f"{duration_ms:.1f}"] = m
        print(f"T={duration_ms:6.1f} ms | energy {m['energyRatio']:.3f} "
              f"| W1 {m['speedW1']:.5f} | conc {m['concentrationRecon']:.3f} "
              f"(orig {m['concentrationOrig']:.3f}) "
              f"| specLD {m['spectralLogDist']:.3f} "
              f"| lag {m['argmaxLagMeanMs']:5.1f} ms | rmse {m['rmse']:.5f}")

    args.json.write_text(json.dumps(results, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
