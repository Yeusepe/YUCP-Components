# METHOD_RESULTS - empirical scoreboard ranking

Fit on first half (teacher[:1270]), scored on second half. Distance lower=better; teacher=0, naive baseline=0.49.

| rank | dist | method | lag | hold | rev | coact | trans | betw | eff | max | entropy | xcorr | b0_1 | b4_8 | b16_24 | rmse |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
|   -- | 0.000 | TEACHER (target) | 0.000 | 8.019 | 164.838 | 5.259 | 15.684 | 11.550 | 3.475 | 0.494 | 1.465 | -0.068 | 0.014 | 0.391 | 0.079 | 0.000 |
|    1 | 0.167 | Fus FHN bank (0.08) | 1.000 | 6.202 | 194.931 | 5.902 | 14.290 | 11.191 | 3.715 | 0.455 | 1.695 | -0.066 | 0.017 | 0.338 | 0.125 | 0.079 |
|    2 | 0.200 | Fus kuramoto bank | 2.000 | 8.205 | 211.714 | 5.822 | 13.949 | 9.139 | 3.308 | 0.507 | 1.640 | -0.060 | 0.024 | 0.378 | 0.048 | 0.076 |
|    3 | 0.237 | Cmb tuned (slowOU) | 1.000 | 6.139 | 266.093 | 5.205 | 16.402 | 9.784 | 3.535 | 0.488 | 1.660 | -0.061 | 0.024 | 0.344 | 0.078 | 0.076 |
|    4 | 0.283 | Cmb sharp+OU (calib) | 1.000 | 5.362 | 258.515 | 5.421 | 21.965 | 9.738 | 2.358 | 0.625 | 1.194 | -0.060 | 0.019 | 0.378 | 0.070 | 0.085 |
|    5 | 0.305 | Cmb sharp+pink (calib) | 1.000 | 5.630 | 299.179 | 5.502 | 22.313 | 9.924 | 2.300 | 0.633 | 1.169 | -0.060 | 0.019 | 0.372 | 0.074 | 0.086 |
|    6 | 0.335 | Cmb sharp+white (calib) | 1.000 | 6.306 | 356.886 | 5.562 | 23.254 | 10.453 | 2.233 | 0.643 | 1.128 | -0.060 | 0.019 | 0.369 | 0.080 | 0.087 |
|    7 | 0.343 | Cmb wide+pink (calib) | 2.000 | 6.348 | 307.386 | 6.485 | 14.289 | 11.761 | 3.003 | 0.531 | 1.448 | -0.058 | 0.042 | 0.255 | 0.094 | 0.090 |
|    8 | 0.360 | C25 rSLDS regimes | 0.000 | 4.675 | 83.547 | 1.128 | 16.397 | 8.885 | 4.073 | 0.410 | 1.551 | -0.068 | 0.014 | 0.288 | 0.177 | 0.166 |
|    9 | 0.364 | Cmb sharp+mult (calib) | 1.000 | 5.787 | 373.263 | 5.559 | 21.537 | 9.827 | 2.197 | 0.652 | 1.092 | -0.060 | 0.017 | 0.338 | 0.131 | 0.090 |
|   10 | 0.446 | B15 NESS circulation | 0.000 | 7.452 | 152.639 | 0.000 | 8.244 | 7.506 | 4.302 | 0.449 | 1.933 | -0.060 | 0.002 | 0.219 | 0.337 | 0.080 |
|   11 | 0.448 | C30 fixed-lag smoother | 0.000 | 0.318 | 110.275 | 2.556 | 7.167 | 2.295 | 5.487 | 0.379 | 2.159 | -0.060 | 0.014 | 0.350 | 0.122 | 0.068 |
|   12 | 0.448 | A9  echo state net | 0.000 | 1.032 | 211.085 | 2.357 | 20.644 | 4.932 | 4.161 | 0.448 | 1.859 | -0.067 | 0.007 | 0.283 | 0.220 | 0.068 |
|   13 | 0.451 | F53 fisher-rao sqrt | 1.000 | 0.001 | 149.460 | 2.145 | 15.380 | 1.212 | 5.550 | 0.380 | 2.157 | -0.061 | 0.010 | 0.394 | 0.087 | 0.073 |
|   14 | 0.461 | A4  fitzhugh-nagumo | 0.000 | 5.934 | 147.020 | 0.872 | 8.021 | 5.516 | 3.923 | 0.466 | 1.623 | -0.068 | 0.003 | 0.189 | 0.352 | 0.089 |
|   15 | 0.464 | C28 global-variance comp | 1.000 | 0.000 | 107.983 | 2.424 | 19.554 | 1.009 | 3.403 | 0.509 | 1.735 | -0.062 | 0.008 | 0.363 | 0.129 | 0.066 |
|   16 | 0.466 | I   stat-match refit | 1.000 | 0.000 | 113.121 | 2.808 | 20.035 | 1.123 | 2.802 | 0.573 | 1.559 | -0.061 | 0.008 | 0.364 | 0.130 | 0.070 |
|   17 | 0.478 | B11 wright-fisher | 2.000 | 7.521 | 348.457 | 4.232 | 11.170 | 9.167 | 6.670 | 0.296 | 2.186 | -0.063 | 0.025 | 0.216 | 0.247 | 0.095 |
|   18 | 0.484 | E42 SSM oscillator bank | 0.000 | 14.132 | 198.036 | 0.101 | 14.122 | 14.192 | 10.073 | 0.167 | 2.427 | -0.064 | 0.001 | 0.385 | 0.013 | 0.137 |
|   19 | 0.490 | naive tau=17ms | 1.000 | 0.000 | 114.193 | 2.010 | 14.392 | 0.785 | 5.092 | 0.397 | 2.110 | -0.062 | 0.008 | 0.363 | 0.128 | 0.070 |
|   20 | 0.494 | J76 zero-lag relax | 0.000 | 0.098 | 152.492 | 4.054 | 6.326 | 2.362 | 5.031 | 0.391 | 2.098 | -0.064 | 0.008 | 0.242 | 0.264 | 0.067 |
|   21 | 0.504 | L86 chow-lin disagg | 0.000 | 0.329 | 173.637 | 1.242 | 10.332 | 0.642 | 4.636 | 0.428 | 2.028 | -0.060 | 0.005 | 0.283 | 0.225 | 0.070 |
|   22 | 0.562 | B16 mult noise (hold-gate) | 0.000 | 9.808 | 435.738 | 0.024 | 11.415 | 9.914 | 4.359 | 0.461 | 1.999 | -0.043 | 0.002 | 0.177 | 0.377 | 0.079 |
|   23 | 0.566 | B16 mult noise (ungated) | 0.000 | 9.808 | 440.692 | 0.081 | 18.150 | 10.877 | 4.452 | 0.460 | 1.990 | -0.048 | 0.002 | 0.171 | 0.392 | 0.080 |
|   24 | 0.599 | D32 source-filter mult | 0.000 | 11.574 | 445.756 | 0.155 | 18.743 | 12.122 | 4.431 | 0.455 | 1.996 | -0.042 | 0.002 | 0.169 | 0.394 | 0.081 |
|   25 | 0.601 | A3  kuramoto bank | 0.000 | 3.199 | 260.992 | 0.000 | 3.802 | 3.128 | 4.221 | 0.461 | 2.005 | -0.067 | 0.003 | 0.194 | 0.349 | 0.077 |
|   26 | 0.603 | E45 oscillatory RNN | 3.000 | 6.189 | 67.540 | 1.744 | 8.561 | 8.899 | 7.484 | 0.241 | 2.179 | -0.066 | 0.016 | 0.181 | 0.008 | 0.117 |
|   27 | 0.621 | Cmb sharp+lorenz (calib) | 1.000 | 1.396 | 51.977 | 1.061 | 6.197 | 2.982 | 4.966 | 0.345 | 1.807 | -0.057 | 0.080 | 0.339 | 0.048 | 0.139 |
|   28 | 0.647 | A7  lorenz chaos mod | 0.000 | 0.761 | 115.154 | 0.939 | 2.387 | 1.131 | 5.092 | 0.369 | 1.940 | -0.057 | 0.005 | 0.187 | 0.356 | 0.098 |
|   29 | 0.650 | F52 OT interpolation | 3.000 | 0.000 | 143.213 | 0.323 | 5.625 | 11.562 | 7.207 | 0.301 | 2.282 | -0.060 | 0.009 | 0.263 | 0.202 | 0.116 |
|   30 | 0.685 | B   OU tangent (hold-gate) | 0.000 | 20.025 | 326.979 | 0.458 | 19.055 | 20.065 | 4.643 | 0.412 | 1.908 | -0.068 | 0.002 | 0.185 | 0.363 | 0.090 |
|   31 | 0.709 | B   OU tangent (ungated) | 0.000 | 20.088 | 304.724 | 0.764 | 27.241 | 20.871 | 4.962 | 0.380 | 1.894 | -0.068 | 0.002 | 0.186 | 0.363 | 0.094 |
|   32 | 0.723 | G61 soft attention | 0.000 | 0.103 | 252.674 | 0.000 | 0.372 | 0.096 | 6.333 | 0.354 | 2.212 | -0.065 | 0.003 | 0.192 | 0.342 | 0.091 |
|   33 | 0.733 | K81 mass-spring ring | 0.000 | 0.002 | 402.467 | 0.010 | 10.214 | 0.962 | 3.915 | 0.487 | 1.926 | -0.057 | 0.002 | 0.148 | 0.344 | 0.083 |
|   34 | 0.906 | B17 pink noise | 0.000 | 26.329 | 334.446 | 0.912 | 32.416 | 25.845 | 5.212 | 0.358 | 1.908 | -0.068 | 0.002 | 0.145 | 0.416 | 0.098 |
|   35 | 0.911 | D36 modulation xcorr | 0.000 | 26.068 | 354.520 | 0.643 | 33.821 | 26.718 | 4.816 | 0.394 | 1.896 | -0.068 | 0.002 | 0.159 | 0.398 | 0.091 |
|   36 | 0.912 | H67 VAE-OU shadow | 0.000 | 24.327 | 423.095 | 0.340 | 31.120 | 25.632 | 4.469 | 0.430 | 1.911 | -0.068 | 0.002 | 0.150 | 0.422 | 0.084 |
|   37 | 0.986 | J78 hawkes modulation | 0.000 | 23.755 | 404.167 | 0.687 | 43.285 | 30.537 | 4.845 | 0.392 | 1.904 | -0.068 | 0.001 | 0.123 | 0.467 | 0.090 |
|   38 | 1.212 | G62 residual resample | 0.000 | 37.516 | 429.084 | 1.761 | 45.143 | 37.235 | 3.614 | 0.470 | 1.586 | -0.066 | 0.001 | 0.121 | 0.473 | 0.099 |
|   39 | 1.332 | A1  lotka-volterra WTA | 8.000 | 0.202 | 95.746 | 1.549 | 0.585 | 0.624 | 1.519 | 0.793 | 0.503 | 0.122 | 0.241 | 0.277 | 0.068 | 0.224 |
|   40 | 1.423 | B12 replicator | 8.000 | 0.006 | 247.092 | 0.192 | 0.000 | 0.001 | 1.131 | 0.945 | 0.128 | 0.286 | 0.167 | 0.217 | 0.111 | 0.247 |
|   41 | 1.518 | L90 filterbank synth | 1.000 | 44.596 | 434.777 | 2.714 | 48.779 | 45.465 | 5.958 | 0.305 | 1.988 | -0.068 | 0.001 | 0.054 | 0.595 | 0.100 |

## Findings

**Decisive variable #1 was amplitude calibration.** Noise/oscillator methods injected
at the residual *marginal* std (~0.07) overshoot hold speed 3-5x (rows near bottom: hold
20-44 vs target 8.0) because marginal std is not a per-frame speed budget. Calibrating a
single scalar gain so hold-region per-frame speed matches teacher (fit on first half)
moved the same sources from bottom to top.

**Decisive variable #2 (red-team result) was replacing broadband noise with a STRUCTURED
hold generator.** The noise combos own co-activation (peaked+smoothed envelope) but
over-reverse (rev 260-370 vs teacher 165) - visible shimmer. Structured autonomous cores
(A4 FHN rev 147, B15 NESS rev 153) get reversals right but kill co-activation (0.9, 0.0).
The **fusion** unites them: decoupled envelope (owns co-activation) + gated per-channel
FitzHugh-Nagumo relaxation bank (owns reversals). Result `Fus FHN bank`, dist 0.167 -
the new top, rev 195 (vs noise's 266), coact 5.90, hold 6.20, eff 3.72, max 0.46, and
xcorr -0.066 vs teacher -0.068. Deterministic (no RNG). Seed-robust by construction.

### Red-team of the fusion (all four checks)
1. **Distance 0.167 reproduced independently.** Faithful reconstruction (envelope from
   Combo_tuned + A4's index-biased FHN bank, gate=clip((phase-2)/3,0,1), amp 0.08).
2. **Coherence risk is REAL but the construction already dodges it.** A *scalar*-bias FHN
   bank synchronises perfectly (off-diag corr = 1.0, std 2.6e-16 after zero-sum) -> dead
   holds (hold 0.12, dist 0.400). The fusion survives ONLY because A4 biases each channel
   by its index target (`Iext = 0.5 + 0.8*target`): high-target channels oscillate,
   low-target ones stay subthreshold, so ~70% are active at any moment at different
   effective phases. Measured off-diag corr in holds = **-0.069, matching teacher's
   -0.068**. No lockstep, no throb. A `_selfcheck()` in methods.py asserts this invariant
   so a future swap to scalar bias fails loudly instead of silently killing holds.
3. **rev 195 vs 165: the excess is FHN relaxation, and it will not cleanly come down.**
   Envelope-only gives rev 103; the FHN adds ~92. Slowing FHN dt cuts rev to a floor of
   ~179 but collapses hold (6.2->2.4) and worsens distance to 0.22, because slow
   relaxation barely moves within a 3-8 frame hold. Smoothing the envelope (bigger tau)
   drops rev to 186 but wrecks distance (0.33) via lag/blur. 195 is the sweet spot; the
   architecture cannot hit 165 at usable hold motion - same tension as the noise version,
   just milder (195 vs 266). Structured relaxation is spikier per unit speed than
   teacher's true hold motion.
4. **FitzHugh beats a plain oscillator bank - the cubic earns its keep.** At MATCHED hold
   (~6): FHN rev 195 / dist 0.167 vs kuramoto rev 206 / dist 0.249. FHN's relaxation has
   quiescent intervals between spikes, so fewer reversals per unit hold motion than a pure
   sinusoid. Kuramoto's raw-distance 0.200 in the table is achieved only at amp 0.06 where
   hold OVERSHOOTS to 8.2 (gaming eff/max); honestly matched, it is clearly worse on the
   reversal metric that matters. Keep FHN.

### Runtime of the fusion (substrate terms), per frame
- envelope: 15x15 index-mean lookup (baked, 0 MACs) + sharpen (15 pow PWL + norm) +
  2-pole observer (4 MACs/ch = 60 MACs, 30 states)
- FHN hold gen: per channel, v += dt*(v - v^3/3 - w + Iext); w += dt*0.08*(v+0.7-0.8w).
  = **1 cubic PWL/ch (15 PWL)** + ~6 MACs/ch (90 MACs) + **2 state floats/ch (30 states)**
- gate: 1 counter + 1 PWL (clip) ; zero-sum (15) + amp multiply (15)
- **Total ~180 MACs, ~30 PWL, ~62 state floats, ZERO RNG draws.** Far under the 4096-MAC
  budget - cheaper than the noise version (~330 MACs) AND deterministic.

### Proxy-gaming flags (distance is a proxy)
- The fusion still over-reverses (195 vs 165). Milder than the noise version but present;
  a reviewer's eye may still catch residual shimmer. This is the honest ceiling.
- Kuramoto's table rank (0.200) is inflated by hold-overshoot; discount it.
- C25 rSLDS (0.360) remains the "dead but tidy" high-ranker (coact 1.13, rev 84).

### What I'd wire into the runtime first
1. Decoupled skeleton `sharpen(index-mean) -> 2-pole observer` (~60 MACs) - fixes
   mushiness (max/eff/entropy) and gives co-activation for free. Ship first.
2. Add the **index-biased FHN bank**, gated to holds, amp as the live tuning knob
   (start ~0.06, raise by eye). Deterministic, ~120 extra MACs + 15 cubic PWL, no RNG.
   Preferred over the noise generator: lower reversals, no seed dependence, cheaper.
3. Keep the coherence invariant (index bias, not scalar) - it is load-bearing; the
   `_selfcheck()` guards it.
