"""Fit the syllabic-rate response and emit the generated runtime table.

The channel gives an argmax index and a voice scalar. A target conditioned only
on the index is constant between switches, so the reconstruction freezes and
staircases. 68.7% of the reference motion happens during those intervals.

Conditioning on the voice LEVEL does not help - measured worse than the frozen
baseline. Conditioning on the RATE of the syllabic band does: it is the only
every-frame quantity in the channel that is both non-constant between switches
and phase-locked to the audio the listener actually hears.

The feature is built exactly as the graph builds it, from three one-pole
smooths, because fitting a form the graph cannot evaluate is what broke the
previous three offline fits:

    a    = onepole(voice, T1)          parallel, both from voice
    b    = onepole(voice, T2)
    band = a - b                       syllabic band-pass
    c    = onepole(band, TD)
    rate = (band - c) / TD             high-pass, approximates d(band)/dt

Slopes are regressed on the residual of the ACTUAL decoder output taken from a
baseline replay, so they correct what the graph really produces. Rows are
projected to sum zero so the correction is simplex preserving.
"""

import csv
import hashlib
import os

import numpy as np

from analyze_deterministic_channel import (
    CAPTURE_DIR, FRAME_SECONDS, OBSERVER_TAU, VISEMES,
    channel_features, load, one_pole, two_pole)

T1, T2, TD = 0.060, 0.550, 0.022
RIDGE = 0.3
# The graph rectifies the rate through two blend-tree curves that saturate
# here, because Unity clamps a negative Direct blend parameter to zero. The
# clamp is modelled so this fit sees exactly what the graph will evaluate.
RATE_CLAMP = 2.5

NAMES = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS",
         "nn", "RR", "aa", "E", "I", "O", "U"]

OUT_CS = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Packages", "com.yucp.components", "Runtime", "Components", "Data",
    "Generated", "AdvancedVisemeSyllabicResponse.generated.cs")


def syllabic_rate(voice):
    a = one_pole(voice, T1)
    b = one_pole(voice, T2)
    band = a - b
    c = one_pole(band, TD)
    rate = (band - c) / TD
    return band, np.clip(rate, -RATE_CLAMP, RATE_CLAMP)


def load_probe(path, suffix, n):
    """A single probe_* column from a replay, if that replay carried one."""
    if not os.path.exists(path):
        return None
    with open(path, newline="") as handle:
        rows = list(csv.reader(handle))
    header = rows[0]
    cols = [i for i, name in enumerate(header)
            if name.startswith("probe_") and name.endswith(suffix)]
    if not cols:
        return None
    data = np.array([[float(cell) for cell in row] for row in rows[1:] if row])
    return data[:n, cols[0]] if len(data) >= n else None


def load_replay_base(path, n):
    """The decoder output the graph currently produces, for the same capture."""
    with open(path, newline="") as handle:
        rows = list(csv.reader(handle))
    header = rows[0]
    cols = [i for i, name in enumerate(header) if name.startswith("avr_")]
    if len(cols) != VISEMES:
        return None
    data = np.array([[float(cell) for cell in row] for row in rows[1:] if row])
    return data[:n, cols] if len(data) >= n else None


def main():
    capture = os.path.join(CAPTURE_DIR, "viseme_capture_20260720_223553.csv")
    cap = load(capture)
    teacher = cap["teacher"]
    n = len(teacher)
    feat = channel_features(cap["viseme"], cap["voice"])
    onehot = feat["onehot"]
    band, rate = syllabic_rate(cap["voice"])

    # The gain-0 arm of the in-graph sweep, which is the decoder output the
    # correction actually sits on top of. Fitting against an older replay taken
    # under a different flag set silently fits the wrong base.
    replay = os.path.join(CAPTURE_DIR, "replayS0_viseme_capture_20260720_223553.csv")
    base = load_replay_base(replay, n) if os.path.exists(replay) else None

    # Prefer the rate the graph actually computes, read back from the probe
    # columns, over the offline reconstruction of it. The graph steps its
    # high-pass at render rate (90 Hz) while this script steps at the analysis
    # rate, so the offline rate is ~2.1x smaller and slopes fitted against it
    # are applied to a signal twice their fitted scale. Fitting against the
    # measured node removes the model/graph gap by construction.
    probe = os.path.join(CAPTURE_DIR, "replayS75_viseme_capture_20260720_223553.csv")
    measured = load_probe(probe, "Voice/Syllabic/Rate", n)
    if measured is not None:
        rate = np.clip(measured, -RATE_CLAMP, RATE_CLAMP)
        print(f"using measured in-graph rate (std {rate.std():.4f})")
    else:
        print("no probe column found; falling back to the offline rate")
    if base is None:
        print("no baseline replay found; fitting against the per-index mean")
        Xb = two_pole(onehot, OBSERVER_TAU)
        coef, *_ = np.linalg.lstsq(Xb, teacher, rcond=1e-8)
        base = Xb @ coef
    else:
        print(f"fitting against actual decoder output from {os.path.basename(replay)}")

    residual = teacher - base

    # Regressors are filtered by the observer, because the correction is
    # injected pre-observer and must be fitted as the graph will see it.
    Xr = two_pole(onehot * rate[:, None], OBSERVER_TAU)

    gram = Xr.T @ Xr
    rhs = Xr.T @ residual
    scale = np.trace(gram) / gram.shape[0]
    slope = np.linalg.solve(gram + RIDGE * scale * np.eye(gram.shape[0]), rhs)

    # Sum-zero rows: the correction moves mass between visemes and never
    # changes the simplex total.
    slope -= slope.mean(axis=1, keepdims=True)

    pred = base + Xr @ slope
    before = np.sqrt(((base - teacher) ** 2).mean())
    after = np.sqrt(((pred - teacher) ** 2).mean())

    def motion(x):
        d = np.diff(x, axis=0)
        sp = np.abs(d).sum(axis=1) / FRAME_SECONDS
        s = np.sign(d)
        return (float(np.median(sp)), float(100.0 * (sp < 0.05).mean()),
                float((s[1:] * s[:-1] < 0).sum(axis=1).mean() / FRAME_SECONDS))

    print(f"\n{'':16}{'rmse':>8}{'p50':>9}{'still%':>9}{'rev':>9}")
    for label, x in (("baseline", base), ("+syllabic", pred), ("teacher", teacher)):
        p50, still, rev = motion(x)
        rmse = np.sqrt(((x - teacher) ** 2).mean())
        print(f"{label:16}{rmse:8.4f}{p50:9.3f}{still:9.2f}{rev:9.2f}")

    print(f"\nrate feature: std {rate.std():.4f}  p99 |rate| {np.percentile(np.abs(rate), 99):.4f}")
    print(f"slope: max |row| {np.abs(slope).max():.4f}  row sums {np.abs(slope.sum(axis=1)).max():.2e}")

    emit(slope, band, rate)
    print(f"\nwrote {OUT_CS}")


def emit(slope, band, rate):
    body = []
    for winner in range(VISEMES):
        body.append(f"            // {NAMES[winner]}")
        row = ", ".join(f"{slope[winner, c]:.9f}f" for c in range(VISEMES))
        body.append(f"            {row},")
    values = "\n".join(body)
    digest = hashlib.sha256(values.encode()).hexdigest()

    text = f"""// <auto-generated>
// Fitted by Tools/AdvancedVisemeAnalysis/fit_syllabic_response.py from a
// VisemeTestRecorder capture. Do not edit by hand.
//
// A target conditioned only on the argmax index is constant between switches,
// so the observer settles and the reconstruction staircases. 68.7% of the
// reference motion occurs during those intervals. The voice LEVEL does not
// recover it (measured worse than the frozen baseline); the RATE of the
// syllabic band does, and unlike generated noise it is phase-locked to the
// audio the listener hears.
//
//   a    = onepole(Voice, T1)      parallel, both taken from Voice
//   b    = onepole(Voice, T2)
//   band = a - b
//   c    = onepole(band, TD)
//   rate = (band - c) / TD
//   target = decoderBase + rate * Slope[winner]
//
// Rows are projected to sum zero, so the correction moves mass between
// visemes and preserves the simplex total exactly; the renormalizer
// downstream only repairs clamping.
//
// The cascade form (b taken from a rather than from Voice) measures no better
// than the frozen baseline. The parallel band is load bearing.
// </auto-generated>

namespace YUCP.Components
{{
    public static class AdvancedVisemeSyllabicResponse
    {{
        public const int ModelVersion = 1;
        public const int VisemeCount = {VISEMES};
        public const string ContentSha256 = "{digest}";

        public const float BandFastSeconds = {T1:.9f}f;
        public const float BandSlowSeconds = {T2:.9f}f;
        public const float RateSeconds = {TD:.9f}f;

        // Slope[winner, channel]
        private static readonly float[] SlopeValues =
        {{
{values}
        }};

        public static float Slope(int winner, int channel)
        {{
            if (winner < 0 || winner >= VisemeCount) return 0f;
            if (channel < 0 || channel >= VisemeCount) return 0f;
            return SlopeValues[winner * VisemeCount + channel];
        }}
    }}
}}
"""
    os.makedirs(os.path.dirname(OUT_CS), exist_ok=True)
    with open(OUT_CS, "w", newline="\n") as handle:
        handle.write(text)


if __name__ == "__main__":
    main()
