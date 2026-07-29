"""Empirical implementations of the METHOD_SURVEY families A-M.

Every method is a reconstruct(viseme, voice, teacher, half) -> (N,15).
Params are fit on teacher[:half] only. All noise is seeded => deterministic.
Run:  python methods.py   -> prints ranked table + writes METHOD_RESULTS.md
"""
import math
import numpy as np
import scoreboard as sb

HZ = sb.FRAME_HZ
DT = 1.0 / HZ
RNG_SEED = 0


# ------------------------------------------------------------------ helpers
def idx_mean(viseme, teacher, half):
    m = np.zeros((15, 15))
    for k in range(15):
        sel = viseme[:half] == k
        m[k] = teacher[:half][sel].mean(0) if sel.any() else np.eye(15)[k]
    return m


def observer(y, tau=0.017, poles=2):
    a = 1 - math.exp(-DT / tau)
    for _ in range(poles):
        st = y[0].copy(); o = np.empty_like(y)
        for i in range(len(y)):
            st = st + a * (y[i] - st); o[i] = st
        y = o
    return y


def sharpen(x, gamma):
    """simplex temperature: raise to power, renormalise. gamma>1 peaks."""
    x = np.clip(x, 1e-9, None) ** gamma
    return x / x.sum(1, keepdims=True)


def phase(viseme):
    """frames since last switch, capped; and hold gate in [0,1]."""
    sw = sb._switches(viseme)
    ph = np.zeros(len(viseme))
    c = 99
    for i in range(len(viseme)):
        c = 0 if sw[i] else c + 1
        ph[i] = c
    return ph


def resid_stats(viseme, teacher, half):
    """per-index residual: std per channel + covariance (for coloured noise)."""
    im = idx_mean(viseme, teacher, half)
    r = teacher[:half] - im[viseme[:half]]
    cov = np.cov(r.T) + 1e-6 * np.eye(15)
    L = np.linalg.cholesky(cov)
    return im, r.std(0), cov, L


def zero_sum(e):
    return e - e.mean(1, keepdims=True)


def _calib_gain(e, im, viseme, half, target_hold=8.019):
    """scalar gain so the perturbation's per-frame speed in HOLD frames on the
    first half matches the teacher hold speed. speed is linear in gain."""
    ph = phase(viseme)[:half]
    sp = sb._speed(e[:half])
    hold = sp[ph >= 8]
    med = np.median(hold) if hold.size else np.median(sp[:half])
    return target_hold / max(med, 1e-6)


def finish(x):
    return sb._norm(x)


# ============================================================ FAMILY A
def A1_lotka_volterra(viseme, voice, teacher, half):
    """Winnerless competition / generalised Lotka-Volterra with asymmetric
    inhibition; winner biased toward current index. Autonomous itinerancy."""
    im = idx_mean(viseme, teacher, half)
    rng = np.random.default_rng(RNG_SEED)
    rho = 1.2 + 0.3 * rng.standard_normal((15, 15)); np.fill_diagonal(rho, 1.0)
    N = len(viseme); out = np.empty((N, 15)); x = im[viseme[0]].copy() + 1e-2
    for i in range(N):
        drive = 3.0 * im[viseme[i]]            # index pins the winner
        dx = x * (1.0 + drive - rho @ x)
        x = np.clip(x + 0.15 * dx, 1e-4, None)
        out[i] = x
    return finish(out)


def A3_kuramoto(viseme, voice, teacher, half):
    """Bank of phase oscillators 4-8 Hz; linear readout fit to residual."""
    N = len(viseme); K = 8
    freqs = np.linspace(4, 8, K)
    ph = 2 * np.pi * np.outer(np.arange(N), freqs) * DT
    feat = np.hstack([np.cos(ph), np.sin(ph)])                    # (N,2K)
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    r = teacher[:half] - im[viseme[:half]]
    W = np.linalg.lstsq(feat[:half], r, rcond=None)[0]
    return finish(im[viseme] + feat @ W)


def A4_fitzhugh(viseme, voice, teacher, half):
    """FitzHugh-Nagumo relaxation bank, one per channel, biased sub/supra
    threshold by the index target => endogenous sustained motion."""
    im = idx_mean(viseme, teacher, half)
    N = len(viseme); out = np.empty((N, 15))
    v = np.zeros(15); w = np.zeros(15); dt = 0.9
    for i in range(N):
        Iext = 0.5 + 0.8 * im[viseme[i]]
        v += dt * (v - v ** 3 / 3 - w + Iext)
        w += dt * 0.08 * (v + 0.7 - 0.8 * w)
        out[i] = im[viseme[i]] + 0.05 * v
    return finish(out)


def A7_lorenz(viseme, voice, teacher, half):
    """Lorenz attractor as bounded aperiodic modulation, projected to
    zero-sum channel perturbation gated everywhere. Amplitude fit to resid."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme)
    s, r, b, dt = 10., 28., 8/3., 0.01
    st = np.array([1., 1., 1.]); traj = np.empty((N, 3))
    for i in range(N):
        x, y, z = st
        st = st + dt * np.array([s*(y-x), x*(r-z)-y, x*y-b*z])
        traj[i] = st
    traj = (traj - traj.mean(0)) / traj.std(0)
    rng = np.random.default_rng(RNG_SEED)
    P = rng.standard_normal((3, 15))
    e = zero_sum(traj @ P)
    e *= (sd.mean() * 1.4) / e.std()
    return finish(im[viseme] + e)


def A9_echo_state(viseme, voice, teacher, half):
    """Echo state network, spectral radius >1, trained ridge readout."""
    N = len(viseme); R = 60
    rng = np.random.default_rng(RNG_SEED)
    Win = rng.uniform(-1, 1, (R, 17))
    W = rng.standard_normal((R, R))
    W *= 1.05 / max(abs(np.linalg.eigvals(W)))
    oh = np.eye(15)[viseme]
    u = np.hstack([oh, voice[:, None], np.ones((N, 1))])
    S = np.empty((N, R)); x = np.zeros(R)
    for i in range(N):
        x = np.tanh(Win @ u[i] + W @ x) * 0.7 + 0.3 * x
        S[i] = x
    F = np.hstack([S, np.ones((N, 1))])
    Wout = np.linalg.lstsq(F[:half].T @ F[:half] + 1e-2 * np.eye(R + 1),
                           F[:half].T @ teacher[:half], rcond=None)[0]
    return finish(F @ Wout)


# ============================================================ FAMILY B
def _coloured_noise(L, N, gain, seed=RNG_SEED):
    rng = np.random.default_rng(seed)
    z = rng.standard_normal((N, 15))
    return zero_sum((z @ L.T) * gain)


def B_ou_tangent(viseme, voice, teacher, half, gate_hold=False):
    """Tangent-space OU decoded through residual-cov chol (the requested B).
    Optionally gated to holds."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme)
    rng = np.random.default_rng(RNG_SEED)
    theta = 1 - math.exp(-DT / 0.05)
    e = np.zeros((N, 15)); st = np.zeros(15)
    for i in range(N):
        st = st - theta * st + math.sqrt(2 * theta) * (L @ rng.standard_normal(15))
        e[i] = st
    e = zero_sum(e); e *= 1.0
    if gate_hold:
        g = np.clip(phase(viseme) / 4.0, 0, 1)[:, None]
        e *= (0.4 + 0.6 * g)
    return finish(im[viseme] + e)


def B11_wright_fisher(viseme, voice, teacher, half):
    """Wright-Fisher / Jacobi diffusion: diffusion sqrt(x(1-x)) vanishes at
    boundary => constraint enforced by dynamics. Mean-reverts to index tgt."""
    im = idx_mean(viseme, teacher, half)
    N = len(viseme); out = np.empty((N, 15))
    rng = np.random.default_rng(RNG_SEED)
    x = im[viseme[0]].copy(); theta = 0.15; sig = 0.5
    for i in range(N):
        tgt = im[viseme[i]]
        drift = theta * (tgt - x)
        diff = sig * np.sqrt(np.clip(x * (1 - x), 0, None)) * rng.standard_normal(15) * math.sqrt(DT)
        x = np.clip(x + drift + diff, 0, None); x /= x.sum()
        out[i] = x
    return out


def B12_replicator(viseme, voice, teacher, half):
    """Stochastic replicator: multiplicative, simplex-preserving,
    structural anticorrelation from the mean-fitness term."""
    im = idx_mean(viseme, teacher, half)
    N = len(viseme); out = np.empty((N, 15))
    rng = np.random.default_rng(RNG_SEED)
    x = im[viseme[0]].copy() + 1e-3; x /= x.sum()
    for i in range(N):
        f = 4.0 * im[viseme[i]] + 0.4 * rng.standard_normal(15)
        x = x * np.exp(0.3 * (f - (x * f).sum()))
        x /= x.sum(); out[i] = x
    return out


def B15_ness(viseme, voice, teacher, half):
    """Non-equilibrium steady state: drift = gradient(symmetric) +
    circulation(antisymmetric). Stationary marginals, perpetual current."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme)
    rng = np.random.default_rng(RNG_SEED)
    Q = rng.standard_normal((15, 15)); Q = Q - Q.T           # antisymmetric
    e = np.zeros((N, 15)); st = np.zeros(15); k = 6.0
    for i in range(N):
        st = st + DT * (-k * st + 3.0 * (Q @ st)) + (L @ rng.standard_normal(15)) * math.sqrt(DT)
        st = np.clip(st, -1, 1)
        e[i] = st
    e = zero_sum(e)
    e *= _calib_gain(e, im, viseme, half)
    return finish(im[viseme] + e)


def B16_multiplicative(viseme, voice, teacher, half, gate_hold=False):
    """Signal-dependent (Harris-Wolpert) noise: variance scales with channel
    level => constant CV, matches std0.129/level0.638."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme); base = im[viseme]
    rng = np.random.default_rng(RNG_SEED)
    cv = 0.20
    e = zero_sum(base * (cv * rng.standard_normal((N, 15))))
    if gate_hold:
        g = np.clip(phase(viseme) / 4.0, 0, 1)[:, None]
        e *= (0.5 + 0.5 * g)
    return finish(base + e)


def B17_pink(viseme, voice, teacher, half):
    """1/f (pink) noise via Voss-McCartney sum of one-pole IIRs, coloured by
    residual cov, added to envelope."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme)
    rng = np.random.default_rng(RNG_SEED)
    taus = [0.01, 0.03, 0.09, 0.27]
    acc = np.zeros((N, 15))
    for tau in taus:
        a = 1 - math.exp(-DT / tau)
        w = rng.standard_normal((N, 15)); y = np.zeros(15)
        o = np.empty((N, 15))
        for i in range(N):
            y = y + a * (w[i] - y); o[i] = y / math.sqrt(a)
        acc += o
    e = zero_sum((acc @ L.T))
    e *= (sd.mean() * 1.3) / e.std()
    return finish(im[viseme] + e)


# ============================================================ FAMILY C
def C28_global_variance(viseme, voice, teacher, half):
    """Global-variance compensation on the naive observer output: rescale each
    channel's deviation from its mean to match teacher's per-channel GV."""
    im = idx_mean(viseme, teacher, half)
    y = observer(im[viseme])
    gv_t = teacher[:half].var(0)
    gv_y = y[:half].var(0)
    scale = np.sqrt(gv_t / np.maximum(gv_y, 1e-9))
    mu = y[:half].mean(0)
    return finish(mu + (y - mu) * scale)


def C30_fixed_lag(viseme, voice, teacher, half):
    """Spend the lag: 4-frame fixed-lag smoother that sees the switch before
    emitting => zero net lag, smooth. Non-causal by 4 frames only."""
    im = idx_mean(viseme, teacher, half)
    tgt = im[viseme]
    fwd = observer(tgt, tau=0.020, poles=2)
    # centre: average forward and backward passes to remove group delay
    bwd = observer(tgt[::-1], tau=0.020, poles=2)[::-1]
    return finish(0.5 * (fwd + bwd))


def C25_rslds(viseme, voice, teacher, half):
    """Per-index (regime) linear dynamics fit offline; runtime = one baked
    15x15 matrix selected by the index. rSLDS shadow."""
    im = idx_mean(viseme, teacher, half)
    N = len(viseme)
    A = np.zeros((15, 15, 15)); b = np.zeros((15, 15))
    for k in range(15):
        sel = np.where(viseme[:half] == k)[0]
        sel = sel[sel + 1 < half]
        if len(sel) < 20:
            A[k] = np.eye(15); continue
        X = np.hstack([teacher[:half][sel], np.ones((len(sel), 1))])
        Y = teacher[:half][sel + 1]
        M = np.linalg.lstsq(X, Y, rcond=None)[0]
        A[k] = M[:15].T; b[k] = M[15]
    out = np.empty((N, 15)); x = im[viseme[0]].copy()
    for i in range(N):
        k = viseme[i]
        x = A[k] @ x + b[k]
        out[i] = x
    return finish(out)


# ============================================================ FAMILY D
def D32_source_filter(viseme, voice, teacher, half):
    """Source-filter with MULTIPLICATIVE excitation: envelope * (1+excitation).
    Excitation = coloured endogenous noise scaled by voice."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme); env = im[viseme]
    exc = _coloured_noise(L / max(sd.mean(), 1e-6), N, 0.35)   # unit-ish shape
    exc *= (0.5 + 0.5 * voice[:, None])                        # voice modulates
    return finish(env * (1.0 + exc))


def D36_modulation(viseme, voice, teacher, half):
    """Modulation-domain: match cross-channel correlation (the load-bearing
    statistic) by driving noise through the residual cov chol, band-limited."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme)
    rng = np.random.default_rng(RNG_SEED)
    a = 1 - math.exp(-DT / 0.02)
    z = rng.standard_normal((N, 15)); y = np.zeros(15); o = np.empty((N, 15))
    for i in range(N):
        y = y + a * (z[i] - y); o[i] = y / math.sqrt(a)
    e = zero_sum(o @ L.T) * 1.1
    return finish(im[viseme] + e)


# ============================================================ FAMILY E
def E42_ssm_oscillator(viseme, voice, teacher, half):
    """Diagonal linear SSM = bank of undamped 2x2 rotations (complex eigs on
    unit circle): no equilibrium. Input-driven, linear readout fit."""
    N = len(viseme); K = 24
    rng = np.random.default_rng(RNG_SEED)
    freqs = np.linspace(1.5, 10.0, K)
    Win = rng.standard_normal((K, 17)) * 0.5
    oh = np.eye(15)[viseme]
    u = np.hstack([oh, voice[:, None], np.ones((N, 1))])
    re = np.zeros(K); imag = np.zeros(K); S = np.empty((N, 2 * K))
    dec = 0.999
    for i in range(N):
        drive = Win @ u[i]
        c = np.cos(2 * np.pi * freqs * DT); s = np.sin(2 * np.pi * freqs * DT)
        nre = dec * (c * re - s * imag) + 0.1 * drive
        nim = dec * (s * re + c * imag)
        re, imag = nre, nim
        S[i] = np.concatenate([re, imag])
    F = np.hstack([S, np.ones((N, 1))])
    Wout = np.linalg.lstsq(F[:half].T @ F[:half] + 1e-2 * np.eye(2 * K + 1),
                           F[:half].T @ teacher[:half], rcond=None)[0]
    return finish(F @ Wout)


def E45_osc_rnn(viseme, voice, teacher, half):
    """coRNN-style second-order oscillatory hidden state (provably no fixed
    point), linear readout fit."""
    N = len(viseme); H = 40
    rng = np.random.default_rng(RNG_SEED)
    Win = rng.standard_normal((H, 17)) * 0.4
    Wh = rng.standard_normal((H, H)) * (0.9 / math.sqrt(H))
    oh = np.eye(15)[viseme]
    u = np.hstack([oh, voice[:, None], np.ones((N, 1))])
    y = np.zeros(H); z = np.zeros(H); dt = 0.3; gamma = 1.0; eps = 0.5
    S = np.empty((N, H))
    for i in range(N):
        z = z + dt * (np.tanh(Wh @ y + Win @ u[i]) - gamma * y - eps * z)
        y = y + dt * z
        S[i] = y
    F = np.hstack([S, np.ones((N, 1))])
    Wout = np.linalg.lstsq(F[:half].T @ F[:half] + 1e-2 * np.eye(H + 1),
                           F[:half].T @ teacher[:half], rcond=None)[0]
    return finish(F @ Wout)


# ============================================================ FAMILY F
def F53_fisher_rao(viseme, voice, teacher, half):
    """Fisher-Rao (sqrt) geometry: interpolate targets along great circles on
    the sphere => concentration preserved (peaks stay peaked in transit)."""
    im = idx_mean(viseme, teacher, half)
    root = np.sqrt(np.clip(im[viseme], 0, None))
    y = observer(root, tau=0.020, poles=2)
    return finish(y ** 2)


def F52_ot_interp(viseme, voice, teacher, half):
    """Displacement (OT) interpolation between successive index targets using a
    baked ground metric on channel index: moves mass instead of cross-fading."""
    im = idx_mean(viseme, teacher, half)
    N = len(viseme)
    # blend weight = smoothed indicator of time since switch
    ph = phase(viseme)
    alpha = np.clip(ph / 6.0, 0, 1)
    out = np.empty((N, 15)); prev = im[viseme[0]].copy()
    cur_tgt = im[viseme[0]].copy()
    for i in range(N):
        if i > 0 and viseme[i] != viseme[i - 1]:
            prev = out[i - 1].copy(); cur_tgt = im[viseme[i]]
        a = alpha[i]
        # 1D-OT-like: sort-match mass between prev and cur by cumulative CDF
        out[i] = _ot_step(prev, cur_tgt, a)
    return finish(out)


def _ot_step(p, q, a):
    """cheap monotone transport interp between two 15-vectors via inverse-CDF."""
    p = p / max(p.sum(), 1e-9); q = q / max(q.sum(), 1e-9)
    cp = np.cumsum(p); cq = np.cumsum(q)
    grid = np.linspace(0, 1, 100, endpoint=False) + 0.005
    xp = np.searchsorted(cp, grid); xq = np.searchsorted(cq, grid)
    xp = np.clip(xp, 0, 14); xq = np.clip(xq, 0, 14)
    pos = (1 - a) * xp + a * xq
    out = np.zeros(15)
    lo = np.floor(pos).astype(int); frac = pos - lo
    for j in range(len(grid)):
        out[min(lo[j], 14)] += (1 - frac[j]) / len(grid)
        out[min(lo[j] + 1, 14)] += frac[j] / len(grid)
    return out


# ============================================================ FAMILY G
def G61_soft_attention(viseme, voice, teacher, half):
    """Branchless retrieval: baked exemplar keys/values from training frames;
    output = softmax-weighted sum over exemplars keyed by (onehot,voice)."""
    N = len(viseme)
    rng = np.random.default_rng(RNG_SEED)
    M = 64
    pick = rng.choice(half, size=min(M, half), replace=False)
    oh = np.eye(15)[viseme]
    q = np.hstack([oh, voice[:, None]])
    keys = q[pick]; vals = teacher[pick]
    logits = q @ keys.T * 4.0
    logits -= logits.max(1, keepdims=True)
    w = np.exp(logits); w /= w.sum(1, keepdims=True)
    return finish(w @ vals)


def G62_resid_resample(viseme, voice, teacher, half):
    """Nonparametric bootstrap of residual segments conditioned on index,
    overlap-added onto the envelope."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme)
    r = teacher[:half] - im[viseme[:half]]
    pool = {k: np.where(viseme[:half] == k)[0] for k in range(15)}
    rng = np.random.default_rng(RNG_SEED)
    e = np.zeros((N, 15))
    for i in range(N):
        p = pool[viseme[i]]
        if len(p):
            e[i] = r[rng.choice(p)]
    # light smoothing (overlap-add feel)
    e = observer(zero_sum(e), tau=0.008, poles=1)
    return finish(im[viseme] + e)


# ============================================================ FAMILY H
def H67_vae_ou(viseme, voice, teacher, half):
    """VAE-with-OU-prior shadow: decode index target plus a smooth OU latent
    passed through a fit linear map => 'sample not mean', temporally coherent."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme); D = 6
    rng = np.random.default_rng(RNG_SEED)
    a = 1 - math.exp(-DT / 0.06)
    lat = np.zeros((N, D)); s = np.zeros(D)
    for i in range(N):
        s = s + a * (rng.standard_normal(D) - s); lat[i] = s / math.sqrt(a)
    # fit decoder map latent->residual on first half
    r = teacher[:half] - im[viseme[:half]]
    W = np.linalg.lstsq(lat[:half], r, rcond=None)[0]
    # decoder underfits (latent random) -> add coloured noise for variance
    e = lat @ W + zero_sum(_coloured_noise(L, N, 0.6))
    return finish(im[viseme] + e)


# ============================================================ FAMILY I
def _pink_template(L, N, seed=RNG_SEED):
    """coloured 1/f perturbation, zero-sum, normalised so its own hold-region
    per-frame speed == 1.0 (so a downstream gain is in speed units)."""
    rng = np.random.default_rng(seed)
    acc = np.zeros((N, 15))
    for tau in [0.01, 0.03, 0.09, 0.27]:
        a = 1 - math.exp(-DT / tau)
        w = rng.standard_normal((N, 15)); y = np.zeros(15); o = np.empty((N, 15))
        for i in range(N):
            y = y + a * (w[i] - y); o[i] = y / math.sqrt(a)
        acc += o
    e = zero_sum(acc @ L.T)
    e /= max(np.median(sb._speed(e)), 1e-9)
    return e


def I_statmatch(viseme, voice, teacher, half):
    """Objective reframing (Family I): take the decoupled base (peaked envelope
    + widened transition + calibrated pink hold motion) and fit its knobs
    (sharpen gamma, transition tau, hold-speed target) by MINIMISING THE
    SCOREBOARD DISTANCE on the first half instead of MSE. Fits offline only."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme)
    pink = _pink_template(L, N)                       # unit hold-speed template

    def build(gamma, tau, hold_sp):
        env = observer(sharpen(im[viseme], gamma), tau=tau, poles=2)
        return finish(env + pink * hold_sp)

    # robust statistic-matching loss: weighted fractional error on the stable
    # up-weighted stats only (first-half xcorr/band targets are ~0 and would
    # blow up the raw scoreboard distance). Fit on first half only.
    tgt = sb.target(teacher[:half], viseme[:half])
    wl = {"hold": 3.0, "rev": 2.0, "coact": 2.0, "max": 1.0,
          "eff": 1.0, "entropy": 1.0, "trans": 1.0, "betw": 1.0}

    def loss(pred):
        s = sb.score(pred[:half], teacher[:half], viseme[:half])
        return sum(w * abs(s[k] - tgt[k]) / max(abs(tgt[k]), 1e-3)
                   for k, w in wl.items()) / sum(wl.values())

    best = None
    for gamma in [1.0, 1.4, 1.8, 2.2, 2.8]:
        for tau in [0.017, 0.030, 0.045, 0.065]:
            for hold_sp in [0.0, 4.0, 6.0, 8.0, 10.0]:
                l = loss(build(gamma, tau, hold_sp))
                if best is None or l < best[0]:
                    best = (l, gamma, tau, hold_sp)
    _, gamma, tau, hold_sp = best
    I_statmatch.fit = (gamma, tau, hold_sp)
    return build(gamma, tau, hold_sp)


# ============================================================ FAMILY J
def J76_zerolag_relax(viseme, voice, teacher, half):
    """Zero-lag transient: apply the switch instantly, put all smoothing on the
    RELEASE only (attack=0, long release). Attacks the lag directly."""
    im = idx_mean(viseme, teacher, half)
    tgt = im[viseme]; N = len(viseme)
    a_rel = 1 - math.exp(-DT / 0.05)
    y = tgt.copy(); st = tgt[0].copy()
    for i in range(N):
        up = tgt[i] > st
        st = np.where(up, tgt[i], st + a_rel * (tgt[i] - st))  # instant up, slow down
        y[i] = st
    return finish(y)


def J78_hawkes(viseme, voice, teacher, half):
    """Hawkes self-exciting intensity as a modulation source (no prediction
    claim): lambda(t) leaky-integrates switch events, drives noise amplitude."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme); sw = sb._switches(viseme)
    a = 1 - math.exp(-DT / 0.08); lam = 0.0; L_arr = np.empty(N)
    for i in range(N):
        lam = lam * (1 - a) + (1.0 if sw[i] else 0.0)
        L_arr[i] = lam
    mod = 0.4 + 1.2 * (L_arr / max(L_arr.max(), 1e-9))
    e = zero_sum(_coloured_noise(L, N, 1.3)) * mod[:, None]
    return finish(im[viseme] + e)


# ============================================================ FAMILY K
def K81_mass_spring(viseme, voice, teacher, half):
    """Underdamped mass-spring-damper per channel (zeta<1): overshoots and
    RINGS after each switch -> motion after input stops."""
    im = idx_mean(viseme, teacher, half)
    tgt = im[viseme]; N = len(viseme)
    wn = 55.0; zeta = 0.35
    x = tgt[0].copy(); v = np.zeros(15); out = np.empty((N, 15))
    for i in range(N):
        acc = wn * wn * (tgt[i] - x) - 2 * zeta * wn * v
        v += acc * DT; x += v * DT; out[i] = x
    return finish(out)


# ============================================================ FAMILY L
def L86_chowlin(viseme, voice, teacher, half):
    """Chow-Lin temporal disaggregation shadow: high-rate series consistent
    with index structure + voice indicator, smoothed by an IIR whose residual
    correlation matches measured lag-1 autocorr."""
    im = idx_mean(viseme, teacher, half)
    N = len(viseme)
    base = im[viseme]
    # regress a voice-driven correction on first half residual
    r = teacher[:half] - base[:half]
    X = np.hstack([voice[:half, None], np.ones((half, 1))])
    W = np.linalg.lstsq(X, r, rcond=None)[0]
    Xf = np.hstack([voice[:, None], np.ones((N, 1))])
    corr = Xf @ W
    y = observer(base + corr, tau=0.017, poles=1)
    return finish(y)


def L90_filterbank(viseme, voice, teacher, half):
    """Multi-rate band synthesis: low bands = channel envelope, high bands =
    coloured stochastic fill sized to the measured per-band energy budget."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme)
    env = observer(im[viseme], tau=0.025, poles=2)     # low band deterministic
    rng = np.random.default_rng(RNG_SEED)
    # two high bands of coloured noise
    hi = np.zeros((N, 15))
    for tau, g in [(0.006, 1.0), (0.02, 0.7)]:
        a = 1 - math.exp(-DT / tau)
        z = rng.standard_normal((N, 15)); y = np.zeros(15); o = np.empty((N, 15))
        for i in range(N):
            y = y + a * (z[i] - y); o[i] = (z[i] - y)      # high-pass
        hi += g * (o @ L.T)
    hi = zero_sum(hi); hi *= (sd.mean() * 1.3) / hi.std()
    return finish(env + hi)


# ============================================================ COMBINATIONS
def _decoupled(viseme, voice, teacher, half, source, gamma=2.0, tau=0.030):
    """The decoupled architecture: peaked+smoothed envelope (fixes max/eff/
    entropy/coact) + an endogenous motion source CALIBRATED to teacher hold
    speed (fixes hold/rev). `source` returns a raw zero-sum (N,15)."""
    im, sd, cov, L = resid_stats(viseme, teacher, half)
    N = len(viseme)
    env = observer(sharpen(im[viseme], gamma), tau=tau, poles=2)
    e = zero_sum(source(N, im, sd, L, viseme, voice))
    e *= _calib_gain(e, im, viseme, half)
    return finish(env + e)


def _src_pink(N, im, sd, L, v, vo):
    rng = np.random.default_rng(RNG_SEED); acc = np.zeros((N, 15))
    for tau in [0.01, 0.03, 0.09, 0.27]:
        a = 1 - math.exp(-DT / tau)
        w = rng.standard_normal((N, 15)); y = np.zeros(15); o = np.empty((N, 15))
        for i in range(N):
            y = y + a * (w[i] - y); o[i] = y / math.sqrt(a)
        acc += o
    return acc @ L.T


def _src_white(N, im, sd, L, v, vo):
    rng = np.random.default_rng(RNG_SEED)
    return rng.standard_normal((N, 15)) @ L.T


def _src_ou(N, im, sd, L, v, vo):
    rng = np.random.default_rng(RNG_SEED)
    a = 1 - math.exp(-DT / 0.05); y = np.zeros(15); o = np.empty((N, 15))
    for i in range(N):
        y = y + a * (rng.standard_normal(15) @ L.T - y); o[i] = y
    return o


def _src_mult(N, im, sd, L, v, vo):
    rng = np.random.default_rng(RNG_SEED)
    return im[v] * rng.standard_normal((N, 15))


def _src_lorenz(N, im, sd, L, v, vo):
    s, r, b, dt = 10., 28., 8/3., 0.01; st = np.array([1., 1., 1.]); tr = np.empty((N, 3))
    for i in range(N):
        x, y, z = st; st = st + dt * np.array([s*(y-x), x*(r-z)-y, x*y-b*z]); tr[i] = st
    tr = (tr - tr.mean(0)) / tr.std(0)
    P = np.random.default_rng(RNG_SEED).standard_normal((3, 15))
    return tr @ P


def Combo_sharp_pink(viseme, voice, teacher, half):
    return _decoupled(viseme, voice, teacher, half, _src_pink)


def Combo_sharp_white(viseme, voice, teacher, half):
    return _decoupled(viseme, voice, teacher, half, _src_white)


def Combo_sharp_ou(viseme, voice, teacher, half):
    return _decoupled(viseme, voice, teacher, half, _src_ou)


def Combo_sharp_mult(viseme, voice, teacher, half):
    return _decoupled(viseme, voice, teacher, half, _src_mult)


def Combo_sharp_lorenz(viseme, voice, teacher, half):
    return _decoupled(viseme, voice, teacher, half, _src_lorenz)


def Combo_wide_pink(viseme, voice, teacher, half):
    """wider transition (bigger tau) to push coact toward teacher's 5.26."""
    return _decoupled(viseme, voice, teacher, half, _src_pink, gamma=1.8, tau=0.050)


def _src_ou_slow(N, im, sd, L, v, vo):
    """slower OU (tau 0.11): fewer per-frame sign flips at equal hold speed =>
    lower reversal rate, closer to teacher's 165/s."""
    rng = np.random.default_rng(RNG_SEED)
    a = 1 - math.exp(-DT / 0.11); y = np.zeros(15); o = np.empty((N, 15))
    for i in range(N):
        y = y + a * (rng.standard_normal(15) @ L.T - y); o[i] = y
    return o


def Combo_tuned(viseme, voice, teacher, half):
    """Best decoupled: gamma tuned to hit teacher max/eff, slow OU to hit
    teacher reversal rate, calibrated to teacher hold speed."""
    return _decoupled(viseme, voice, teacher, half, _src_ou_slow, gamma=1.45, tau=0.035)


def _fhn_bank(N, bias, dt=0.9, detune=0.0):
    """FitzHugh-Nagumo relaxation bank, one unit per channel, biased per channel
    per frame by `bias` (N,15) = the index target. The per-channel bias is what
    desynchronizes the bank (high-target channels oscillate, low-target ones stay
    subthreshold). detune additionally spreads per-channel dt."""
    dts = dt * (1.0 + detune * (np.arange(15) / 14.0 - 0.5) * 2.0)
    v = np.zeros(15); w = np.zeros(15); out = np.empty((N, 15))
    for i in range(N):
        Iext = 0.5 + 0.8 * bias[i]
        v = v + dts * (v - v ** 3 / 3 - w + Iext)
        w = w + dts * 0.08 * (v + 0.7 - 0.8 * w)
        out[i] = v
    return out


def _kuramoto_bank(N, bias, detune=0.0):
    """Linear phase-oscillator bank 4-8 Hz, staggered phase per channel. Cheaper
    than FHN (no cubic). Gated by bias>threshold so quiet channels don't ring."""
    freqs = np.linspace(4.0, 8.0, 15)
    ph0 = 2 * np.pi * np.arange(15) / 15.0
    t = np.arange(N)[:, None] * DT
    osc = np.cos(2 * np.pi * freqs[None, :] * t + ph0[None, :])
    return osc * (bias > 0.05)          # only active channels oscillate


def Fusion(viseme, voice, teacher, half, amp=0.08, gamma=1.45, tau=0.035,
           gen="fhn", detune=0.0):
    """Decoupled fusion: sharpened+smoothed envelope (owns co-activation) +
    gated per-channel-biased relaxation bank as the hold generator (owns
    reversals). Zero-sum => moves mass on the simplex. Deterministic, no RNG."""
    im = idx_mean(viseme, teacher, half)
    N = len(viseme)
    env = observer(sharpen(im[viseme], gamma), tau=tau, poles=2)
    bias = im[viseme]
    raw = (_fhn_bank(N, bias, detune=detune) if gen == "fhn"
           else _kuramoto_bank(N, bias, detune=detune))
    osc = zero_sum(raw)
    gate = np.clip((phase(viseme) - 2) / 3.0, 0, 1)[:, None]
    return finish(env + amp * osc * gate)


def Fusion_fhn(viseme, voice, teacher, half):
    return Fusion(viseme, voice, teacher, half, amp=0.08, gen="fhn")


def Fusion_kuramoto(viseme, voice, teacher, half):
    return Fusion(viseme, voice, teacher, half, amp=0.06, gen="kur")


# ============================================================ registry
METHODS = [
    ("A1  lotka-volterra WTA", A1_lotka_volterra),
    ("A3  kuramoto bank", A3_kuramoto),
    ("A4  fitzhugh-nagumo", A4_fitzhugh),
    ("A7  lorenz chaos mod", A7_lorenz),
    ("A9  echo state net", A9_echo_state),
    ("B   OU tangent (ungated)", lambda v, vo, t, h: B_ou_tangent(v, vo, t, h, False)),
    ("B   OU tangent (hold-gate)", lambda v, vo, t, h: B_ou_tangent(v, vo, t, h, True)),
    ("B11 wright-fisher", B11_wright_fisher),
    ("B12 replicator", B12_replicator),
    ("B15 NESS circulation", B15_ness),
    ("B16 mult noise (ungated)", lambda v, vo, t, h: B16_multiplicative(v, vo, t, h, False)),
    ("B16 mult noise (hold-gate)", lambda v, vo, t, h: B16_multiplicative(v, vo, t, h, True)),
    ("B17 pink noise", B17_pink),
    ("C25 rSLDS regimes", C25_rslds),
    ("C28 global-variance comp", C28_global_variance),
    ("C30 fixed-lag smoother", C30_fixed_lag),
    ("D32 source-filter mult", D32_source_filter),
    ("D36 modulation xcorr", D36_modulation),
    ("E42 SSM oscillator bank", E42_ssm_oscillator),
    ("E45 oscillatory RNN", E45_osc_rnn),
    ("F52 OT interpolation", F52_ot_interp),
    ("F53 fisher-rao sqrt", F53_fisher_rao),
    ("G61 soft attention", G61_soft_attention),
    ("G62 residual resample", G62_resid_resample),
    ("H67 VAE-OU shadow", H67_vae_ou),
    ("I   stat-match refit", I_statmatch),
    ("J76 zero-lag relax", J76_zerolag_relax),
    ("J78 hawkes modulation", J78_hawkes),
    ("K81 mass-spring ring", K81_mass_spring),
    ("L86 chow-lin disagg", L86_chowlin),
    ("L90 filterbank synth", L90_filterbank),
    ("Cmb sharp+pink (calib)", Combo_sharp_pink),
    ("Cmb sharp+white (calib)", Combo_sharp_white),
    ("Cmb sharp+OU (calib)", Combo_sharp_ou),
    ("Cmb sharp+mult (calib)", Combo_sharp_mult),
    ("Cmb sharp+lorenz (calib)", Combo_sharp_lorenz),
    ("Cmb wide+pink (calib)", Combo_wide_pink),
    ("Cmb tuned (slowOU)", Combo_tuned),
    ("Fus FHN bank (0.08)", Fusion_fhn),
    ("Fus kuramoto bank", Fusion_kuramoto),
]


def naive(viseme, voice, teacher, half, tau=0.017):
    im = idx_mean(viseme, teacher, half)
    return observer(im[viseme], tau=tau, poles=2)


def _selfcheck():
    """The fusion only works because the per-channel index bias desynchronises
    the FHN bank. A scalar-bias bank synchronises (corr~1) and zero-sums to
    nothing => dead holds. Guard against that regression."""
    teacher, viseme, voice = sb.load(); half = len(viseme) // 2
    bias = idx_mean(viseme, teacher, half)[viseme]
    ph = phase(viseme); hold = ph >= 8
    biased = zero_sum(_fhn_bank(len(viseme), bias))[hold]
    scalar = zero_sum(_fhn_bank(len(viseme), np.full_like(bias, 0.5)))[hold]
    iu = np.triu_indices(15, 1)
    cb = np.nanmean(np.corrcoef(biased.T)[iu])
    cs = np.nanmean(np.corrcoef(scalar.T)[iu])
    assert cb < 0, f"index-biased FHN should anti-correlate, got {cb:.3f}"
    assert scalar.std() < 1e-6 or cs > 0.9, "scalar-bias bank should synchronise"
    print(f"selfcheck OK: index-biased off-diag corr={cb:.3f} (desync), "
          f"scalar-bias std={scalar.std():.2e} (collapses)")


if __name__ == "__main__":
    _selfcheck()
    teacher, viseme, voice = sb.load()
    t = sb.target(teacher, viseme)
    keys = ["lag", "hold", "rev", "coact", "trans", "betw", "eff", "max",
            "entropy", "xcorr", "b0_1", "b4_8", "b16_24", "rmse"]

    rows = []
    # baselines
    for label, fn in [("TEACHER (target)", None), ("naive tau=17ms", naive)] + METHODS:
        if fn is None:
            s, d = t, 0.0
        else:
            try:
                s, d = sb.split_score(fn, label)
            except Exception as ex:
                print(f"FAILED {label}: {ex}"); continue
        rows.append((d, label, s))
        print(f"{d:6.3f}  {label}")

    rows_sorted = sorted(rows, key=lambda r: r[0])

    # write markdown
    lines = ["# METHOD_RESULTS - empirical scoreboard ranking", "",
             "Fit on first half (teacher[:1270]), scored on second half. "
             "Distance lower=better; teacher=0, naive baseline=0.49.", "",
             "| rank | dist | method | " + " | ".join(keys) + " |",
             "|---|---|---|" + "|".join(["---"] * len(keys)) + "|"]
    rank = 0
    for d, label, s in rows_sorted:
        if label == "TEACHER (target)":
            tag = "  --"
        else:
            rank += 1; tag = f"{rank:4d}"
        cells = " | ".join(f"{s[k]:.3f}" for k in keys)
        lines.append(f"| {tag} | {d:.3f} | {label} | {cells} |")
    with open("METHOD_RESULTS.md", "w") as f:
        f.write("\n".join(lines) + "\n")
    print("\nwrote METHOD_RESULTS.md")
