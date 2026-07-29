I ran ~30 fan-out searches across dynamical systems, computational neuroscience, speech science, graphics, information theory, control, econometrics, and generative modelling. Here's the map.

---

# Citation convention

- **[V]** — arXiv ID or DOI appeared verbatim in a search result this session.
- **[M]** — cited from memory, identifier **not** verified this session. Title/author/venue are what I'm confident about; treat any number as needing a lookup.
- I have **not** invented any arXiv IDs. Where I don't have one, I give author/venue/year only.

---

# Framing: two structural facts your brief already proves

Before the taxonomy, two things your own measurements establish that reorganise the space:

**1. Your RMSE finding is not an anomaly, it is a theorem.** E[x | i, v] is the MMSE estimator. Blau & Michaeli's perception–distortion tradeoff [V arXiv:1711.06077] proves that the distortion-optimal reconstruction is at the *far end* of the curve from the perception-optimal one, and that this holds for *any* distortion measure. The rate-distortion-perception extension [V arXiv:1901.07821] shows that constraining perceptual quality (output distribution = source distribution) necessarily elevates the rate-distortion curve. You are on a fixed rate (log₂15 + v per frame). So you are choosing a point on a *fixed* R-D-P surface, and every method you listed picked the distortion corner. See also [V arXiv:2204.06049], [V arXiv:1808.07986], [V arXiv:1812.11822], [V arXiv:2401.12207], [V arXiv:2403.14849]. **This means: any method family whose fitting objective is squared error will reproduce your failure regardless of its architecture.** That reclassifies a large part of the space — the axis that matters is the *objective*, not the model.

**2. "No equilibrium" is a precise, well-studied specification.** A system whose output never settles under constant input is either (a) an autonomous oscillator / limit cycle, (b) a chaotic or heteroclinic itinerant system, (c) a noise-driven non-equilibrium stationary process, or (d) not autonomous at all — driven by a hidden exogenous clock. Those are four genuinely different mathematical objects and they form the spine of Families A–D below.

---

# FAMILY A — Autonomous non-equilibrium dynamics
*Objects: limit cycles, heteroclinic channels, itinerant attractors. The defining property: constant input ⇒ persistent motion. Directly targets your structural diagnosis.*

**A1. Winnerless competition / stable heteroclinic channels (generalised Lotka–Volterra).**
N units with asymmetric mutual inhibition. Each saddle equilibrium is unstable along exactly one direction pointing at the next unit, so the trajectory perpetually approaches and departs a sequence of near-one-hot states without ever settling. Rabinovich's principle; the state *is* a simplex-like competition, which matches your object exactly. Set the winner from i_t and let the residual channels do the itinerancy. Field: computational neuroscience / nonlinear dynamics. Sources: [V arXiv:1509.04570] (2D heteroclinic attractor in generalised LV), [V arXiv:2208.02085] (abundance of infinite switching), [V arXiv:1703.05504] (transient sequences), [V arXiv:0901.3028] (autonomously active networks), Rabinovich et al., *Phys Rev Lett* 87:068102 (2001) [M]. **Runtime: ~15×15 = 225 MACs for the interaction matrix + 15 PWL for the nonlinearity + 15 states. Trivially affordable.** This is the single closest mathematical match to your diagnosis I found.

**A2. Excitable-network attractors (RNN as a network of excitable states).**
Rather than fixed points, the learned object is a network of excitable equilibria connected by excitable connections; input perturbations kick the state between them and the state is never at rest between kicks. Directly reframes "what does the RNN store" from attractor to *transition*. Field: dynamical systems / ML theory. Source: [V arXiv:1807.10478]. **Runtime: same order as A1.**

**A3. Coupled phase oscillators (Kuramoto bank).**
A bank of K phase oscillators with natural frequencies spread over 4–8 Hz — exactly your measured sustained-viseme modulation band — coupled to each other and phase-biased (not frequency-driven) by switch events. Output is a linear readout of cos θ_k. Never equilibrates; the 164.9 reversals/sec figure is a direct design target for the frequency spread. Field: nonlinear dynamics / neuroscience. Sources: [V arXiv:1511.07139] (Kuramoto in complex networks), [V arXiv:1307.8398] (delay + plasticity), [V arXiv:2406.01208]. **Runtime: 2 states/oscillator; sin/cos via PWL. 8 oscillators ≈ 16 states, ~100 MACs, 16 PWL. Very cheap.**

**A4. FitzHugh–Nagumo / relaxation-oscillator bank.**
Two-variable excitable units producing relaxation oscillations, canards, and mixed-mode oscillations. Crucially, FHN units sit near a bifurcation where they can be *subthreshold* (quiet, responsive) or *oscillating* (self-driving) depending on a bias — so v_t or dwell-time can move channels across that boundary, giving you sustained-segment motion that is genuinely endogenous. Field: mathematical biology. Sources: [V arXiv:2404.11403] (six decades of FHN, comprehensive survey), [V arXiv:1709.03336] (coherence resonance in FHN networks), [V arXiv:2505.12173] (relaxation and bursting). **Runtime: 2 states + 1 cubic PWL per channel. 15 channels = 30 states, ~60 MACs, 15 PWL. Cheap.**

**A5. Coherence resonance / noise-induced oscillation.**
A sub-threshold excitable unit driven by noise fires quasi-regularly, with *maximal* regularity at an intermediate noise level. This gives motion whose statistics you tune via a single noise-amplitude knob and whose spectrum is broad-but-peaked — which is what your residual spectrum (spread across 1–24 Hz with no dominant peak) actually looks like. Field: stochastic nonlinear dynamics. Source: [V arXiv:1709.03336], Pikovsky & Kurths *PRL* 78:775 (1997) [M]. **Runtime: as A4 plus a noise source.**

**A6. Central pattern generators (CPG).**
An oscillator network producing coordinated multi-channel rhythmic output, with a small parameter vector (amplitude, frequency, phase offsets) modulated by high-level command — precisely the "low-rate symbol modulates a self-running generator" architecture. The robotics literature has solved the "how do you steer a CPG from sparse commands without killing its autonomy" problem. Field: robotics / procedural animation. Sources: [V arXiv:2211.00458] (CPG-RL), [V arXiv:2404.17139] (phase reduction for gait transitions), [V arXiv:2410.16417] (online CPG optimisation), Bhatti et al., *Multimedia Tools Appl* (2019), doi:10.1007/s11042-019-7641-1 [V]. **Runtime: as A3.**

**A7. Chaotic attractor as modulation source (Lorenz / Rössler / Chua / Duffing / van der Pol).**
Integrate a 3-state chaotic ODE at frame rate and use its coordinates as bounded, aperiodic, never-repeating, smooth modulation injected into the channel weights. Long tradition in electronic music precisely because it *sounds alive*. Deterministic, so it's reproducible and costs no RNG. Field: chaotic sound synthesis / analog music DSP. Sources: [V arXiv:1105.3927] (statistical complexity of sampled attractors), [V arXiv:0907.3993] (aperiodic nonchaotic attractors), practitioner literature (Perfect Circuit "Chaotic Sound Synthesis", strange-attractor-synth). **Runtime: 3 states, ~10 MACs, 2 multiplies. Essentially free. Highest ratio of aliveness-per-op in the whole survey.**

**A8. Chaotic itinerancy / metastable wandering.**
A stronger version of A1 where the system wanders among ruins of attractors with chaotic residence times, giving heavy-tailed dwell statistics. Your dwell distribution (median 3, mean 4.06, 9.3% ≥ 8) is heavy-tailed and *not* geometric, which is what itinerancy naturally produces and what an HMM naturally does not. Field: computational neuroscience. Sources: Tsuda, *Behav Brain Sci* 24:793 (2001) [M]; [V arXiv:0901.3028]. **Runtime: cheap; tuning is the hard part.**

**A9. Reservoir computing / echo state network as a live substrate.**
A fixed random recurrent net at the edge of chaos, driven by (i_t, v_t), read out by a trained linear layer. The reservoir supplies rich never-repeating internal dynamics *for free* and only the readout is fit — which maps perfectly onto your substrate (random recurrent matrix is a baked table; readout is a weighted sum). Push the spectral radius slightly above 1 and the reservoir has no equilibrium at all. Field: machine learning / neuromorphic. Sources: [V arXiv:1712.04323] (DeepESN), [V arXiv:2504.11757] (mathematical perspective), [V arXiv:2301.09235] (temporal self-modulation), Jaeger, *Scholarpedia* echo state network [V]. **Runtime: N=64 reservoir = 4096 MACs (at your ceiling), 64 PWL, 64 states. N=32 = 1024 MACs, comfortable.** Strong practical candidate: almost no training cost, and it's structurally a leaky-integrator + matrix, which is exactly your primitive set.

**A10. Van der Pol / Duffing forced-oscillator entrainment.**
A self-oscillating unit that *entrains* to an external drive over a range (Arnold tongue) but keeps oscillating when the drive vanishes. This gives you the property you want: it locks to v_t's rhythm when there is one and free-runs when there isn't, without a hand-written mode switch. Field: nonlinear dynamics / audio DSP. Source: [V arXiv:1005.4765] (limit-cycle oscillators under weak colored noise). **Runtime: 2 states, 1 PWL.**

---

# FAMILY B — Stochastic processes on the simplex
*Objects: SDEs with state-dependent diffusion. Non-equilibrium in the "stationary distribution but perpetual motion" sense — the right formalisation of "the residual never stops".*

**B11. Wright–Fisher / Jacobi diffusion on the simplex.**
The canonical diffusion whose state space *is* the simplex, with diffusion coefficient √(x_k(1−x_k)) that vanishes at the boundary — so the constraint is enforced by the dynamics rather than by projection or softmax. Mean-reverts to a target set by (i,v) while never settling. This is the mathematically correct noise model for your object. Field: population genetics / stochastic analysis. Sources: [V arXiv:2309.02530] (Diffusion on the Probability Simplex), [V arXiv:2008.05410] (Darwinian evolution as Brownian motion on the simplex — the Aitchison-geometry link), [V arXiv:2302.00519] (time series on compact spaces). **Runtime: 15 states, 15 √· PWL, ~50 MACs + 15 noise draws. Cheap.** Your additive PCA-noise attempt is the *linearised, state-independent* approximation of this — which is exactly why it read as a texture pasted on top rather than as the thing moving.

**B12. Stochastic replicator dynamics.**
ẋ_k = x_k(f_k − f̄) + noise. Multiplicative in x, automatically simplex-preserving, and the log-ratio form is a plain linear recurrence — trivial on your substrate. The fitness vector f is where (i,v) enters. Cross-channel *anticorrelation* is structural (the f̄ term), which matches your measured residual cross-correlation of −0.068 for free. Field: evolutionary game theory. Sources: [V arXiv:2008.05410], Sorin, "Replicator dynamics: old and new" [V]; Hofbauer & Sigmund *PNAS* 111 (2014) doi:10.1073/pnas.1400823111 [V]. **Runtime: ~250 MACs, 15 multiplies. Cheap.**

**B13. Aitchison-geometry / log-ratio (ALR/CLR/ILR) state space.**
Transform to R^14 via centred log-ratio, run *any* unconstrained linear/nonlinear dynamics there, map back through softmax. Removes the simplex constraint entirely and — critically for your substrate — makes signed quantities natural in latent space, so the "negative can't be a blend weight" gotcha only bites at the final softmax. Field: compositional data analysis. Sources: Aitchison, *JRSS-B* 44:139 (1982) and *Statistical Analysis of Compositional Data* (1986) [M]; [V arXiv:2302.00519]. **Runtime: 15 log PWL + 15 exp PWL + normalise ≈ 30 PWL + 30 MACs. Cheap. This is infrastructure for half the other families, not a method in itself.**

**B14. Constrained / bridged Ornstein–Uhlenbeck.**
OU conditioned to hit a target at a known time. Your inter-switch segments are exactly bridges: known start, known end symbol, known duration only in hindsight. Offline this is a bridge; online it's a bridge with unknown endpoint, which is the interesting variant. Field: stochastic processes. Source: [V arXiv:1704.07644] (constraint OU bridges). **Runtime: cheap. Marked semi-impractical online** — the endpoint isn't causally known and your gradient-boosting result says it isn't predictable either.

**B15. Non-equilibrium steady state with broken detailed balance.**
Construct the drift as (symmetric gradient part) + (antisymmetric circulating part). The antisymmetric part produces persistent probability current — the system has a *stationary distribution* but a nonzero circulation, so it never stops moving even at stationarity. This is the cleanest formal answer to "stationary marginals but perpetual motion", which is precisely your specification. Field: stochastic thermodynamics. Sources: Weiss, *Am J Phys* 75:442 (2007) [M]; general NESS literature. **Runtime: an antisymmetric 15×15 matrix = 225 MACs. Cheap and remarkably underused.** I'd single this out as an underrated entry.

**B16. Signal-dependent (multiplicative) motor noise.**
Harris & Wolpert: motor noise variance scales with signal magnitude. Applied here: the dominant channel's std of 0.129 against level 0.638 is a ratio of 0.20 — a *constant coefficient of variation*, which is the signature of signal-dependent noise, not additive noise. That single number in your brief is strong evidence for this model. Field: sensorimotor neuroscience. Sources: Harris & Wolpert, *Nature* 394:780–784 (1998) [V — title/venue confirmed in results]; [V arXiv:2104.06275] (mixed-horizon optimal feedback control); [V arXiv:2110.00443]. **Runtime: one multiply per channel. Free.** Your additive-noise experiment used the wrong noise model; this is the one-line change.

**B17. 1/f (pink) noise with long-range correlation.**
Human motor output has 1/f fluctuation across timing, force, and kinematics. Your residual spectrum — 30.6% below 1 Hz, monotonically decaying but with substantial energy out to 24 Hz — is closer to 1/f than to white or to any single-pole shape. Implement as a sum of 4–6 one-pole filters with geometrically spaced time constants driven by white noise (Voss–McCartney), which is exactly your IIR primitive. Field: motor control / statistical physics. Sources: Delignières & Torre, *Human Movement Science* (2010) [M]; PMC4160925 (spectral convergence in tapping) [V]; PMC6502337 (origins of 1/f in music performance) [V]. **Runtime: 6 IIR states + 6 MACs per channel. Very cheap, and it fixes a spectral mismatch you can measure directly.**

**B18. Fractional Brownian motion / fractional OU.**
Hurst-parameter-controlled roughness. Your residual autocorrelation of 0.58 at lag 1 decaying over ~43 ms is a specific roughness signature and H can be fit to it directly. Field: stochastic processes / finance. **Runtime: exact fBm is non-Markov and impractical; the Markov approximation collapses to B17. Marked: use B17 instead.**

**B19. Telegraph / Markov-modulated noise.**
Two-state jump noise rather than Gaussian, giving piecewise-constant-then-relax residuals. Produces a visually different texture from Gaussian noise (steppy rather than shimmery) — worth an A/B because your consumer is an eye, not an L2 norm. Field: statistical physics. **Runtime: 1 state + 1 comparison per channel. Free.**

**B20. Langevin / energy-based sampling at runtime.**
Define an energy over the simplex conditioned on (i,v); run 1–2 Langevin steps per frame. The output is a *sample*, not a mean, so it inherently sits at the perception end of the R-D-P curve. Field: generative modelling / statistical physics. Sources: [V arXiv:2412.14706] (EnergyMoGen, energy-based motion in latent space), [V arXiv:2407.01573] (model-based diffusion for trajectory optimisation). **Runtime: 1 step = 1 gradient of a small MLP ≈ 500–1500 MACs. Fits, at the top of your budget.**

---

# FAMILY C — Estimation from coarse / event-based observations
*Objects: filters and decoders. This family takes the channel seriously as an information-theoretic object rather than as a lookup key. Several of these have exact reconstruction theorems you may be able to borrow.*

**C21. Time-encoding machines (TEM) and time decoding.**
Lazar & Tóth: a bandlimited signal encoded as a sequence of *times* (not amplitudes) can be **perfectly** recovered if inter-spike intervals are below a Nyquist-related bound. Your switch times are exactly such a sequence, and your median dwell of 3 frames is dense relative to the bandwidth of the underlying motion. This is the deepest theoretical result in the survey that applies directly to your channel and I suspect you have not seen it. Field: information theory / neuromorphic signal processing. Sources: Lazar & Tóth, *IEEE TCAS-I* 51(10) (2004) [V — title/venue confirmed]; [V arXiv:1911.12945] (reconstruction by pseudo-inversion and time-varying FIR — note the FIR form is directly bakeable), [V arXiv:2110.01928] (time encoding quantization of BL and FRI signals), [V arXiv:2201.03006] (LIF encoding, POCS reconstruction). **Runtime: the pseudo-inverse is precomputed; runtime is a short time-varying FIR ≈ 100–400 MACs. Excellent fit — your "arbitrary precomputed matrices are free" primitive is exactly what this needs.**

**C22. Level-crossing / send-on-delta reconstruction with implicit information.**
The key idea: between events you know *more* than "nothing happened" — you know the signal did **not** cross a threshold. That's an inequality constraint per non-event frame, and it's free information you are currently discarding. Concretely: between switches you know x[i_t] ≥ x[k] for all k. That constraint set is nonempty and informative and none of your listed methods use it. Field: event-based signal processing / control. Sources: [V arXiv:2303.01783], [V arXiv:2501.10829], [V arXiv:1910.01032], Miskowicz *Sensors* on send-on-delta [M]. **Runtime: projection onto an ordering constraint ≈ 15 comparisons + renormalise. Nearly free.** High value-per-effort.

**C23. One-bit / quantized compressed sensing.**
argmax is the extreme quantizer: all magnitude information destroyed, only an ordering survives. The 1-bit CS literature is the study of exactly this — recovery from sign patterns using a strong prior. The modern version replaces the sparsity prior with a generative prior. Field: information theory / signal processing. Sources: [V arXiv:2211.13006] (quantized CS with score-based generative models), [V arXiv:2107.09091], [V arXiv:1304.1969], [V arXiv:1312.3418], [V arXiv:2012.12886] (NBIHT), [V arXiv:2201.03114]. **Runtime: iterative solvers are impractical online; but the *unrolled* fixed-iteration form is a plain feedforward net and is affordable. Mark: use unrolled form only.**

**C24. Rao-Blackwellised particle filter over a jump-Markov linear system.**
Treat i_t as a noisy observation of an underlying discrete regime and x_t as a conditionally-linear-Gaussian continuous state; marginalise the continuous part analytically, sample only the discrete part. Gives a principled posterior, and posterior *sampling* (not the mean!) is your output. Field: sequential Monte Carlo. Sources: Doucet, Gordon & Krishnamurthy, *IEEE TSP* 49(3) (2001) [V]; [V arXiv:1311.6486], [V arXiv:1409.7287], [V arXiv:1705.07598], [V arXiv:2411.16056]. **Runtime: 30–100 particles × 15-dim Kalman ≈ 50k–200k MACs. IMPRACTICAL by ~50×. But: distil it offline into a feedforward net (amortised inference) and the distilled net fits.**

**C25. Recurrent switching linear dynamical systems (rSLDS).**
Continuous state feeds back to influence discrete switching, which is precisely your regime: the viseme trajectory's position determines when the argmax flips. Learn per-regime linear dynamics offline; at runtime each regime is a matrix multiply selected by i_t (i.e., a baked table lookup — free on your substrate). Field: computational neuroscience / Bayesian time series. Sources: [V arXiv:1610.08466] (Linderman et al.), Linderman et al. *AISTATS* 2017 [V], [V arXiv:2411.04280] (recurrent explicit-duration SLDS — the duration part matters given your dwell distribution), [V arXiv:2411.04278], [V arXiv:2509.21578] (Gumbel dynamics). **Runtime: one 15×15 matrix per frame = 225 MACs, matrix selected by index. Superb substrate fit.** This is a serious candidate.

**C26. Explicit-duration / hidden semi-Markov models.**
Standard HMMs impose geometric dwell distributions; yours is not geometric (37.5% ≤ 2 frames *and* 9.3% ≥ 8 is bimodal-ish, heavy-tailed). HSMMs model duration explicitly. Field: speech recognition/synthesis. Sources: [V arXiv:1909.05800] (explicit-duration Markov switching models), Zen et al., HSMM-based speech synthesis [V], Yu, *Artificial Intelligence* 174 (2010) [M]. **Runtime: cheap.** Note: you tried dwell-conditioned *means*, which is the memoryless projection of this. The HSMM's value is the duration *posterior*, not the conditional mean — i.e. "how long is this likely to last" as a continuous modulating signal, not as a table index.

**C27. Trajectory HMM (Tokuda) — dynamic feature constraints.**
The classic fix for HMM-generated trajectories being piecewise-constant: impose explicit relationships between static and delta/delta-delta features, which converts generation into a smoothing problem whose solution is a *global* trajectory with correct velocity statistics rather than a sequence of means. This is the single most-cited solution to "HMM output looks dead". Field: statistical speech synthesis. Sources: Zen, Tokuda & Kitamura, *Computer Speech & Language* 21 (2007), doi:10.1016/j.csl.2006.01.002 [V — appeared in results]; Tokuda et al. MLPG, ICASSP 2000 [M]. **Runtime: offline MLPG is non-causal; the causal/recursive approximation is an IIR filter ≈ 50 MACs. Affordable.** Directly attacks your "68.7% of motion is between switches" number.

**C28. Global variance (GV) compensation.**
The known companion fix to C27: statistical generation systematically *under-varies*, and explicitly matching the global variance of the generated trajectory to that of natural data produces a large perceptual gain with a *worse* RMSE. This is the speech-synthesis community's independent rediscovery of your exact finding, and they shipped a solution. Field: statistical speech synthesis. Source: Toda & Tokuda, *IEICE Trans* E90-D (2007) [M — ID unverified, but this result is real and central]. **Runtime: one scale factor per channel, or a per-channel PWL. Nearly free. Very high value-per-op.** If you try one thing from Family C, try this.

**C29. Koopman operator / EDMD lifting.**
Lift x to a high-dimensional observable space where dynamics are *linear*, then run a linear predictor. Your substrate loves this: the lift is PWL basis functions, the dynamics are one baked matrix. And a linear system in lifted space can have purely imaginary eigenvalues — i.e. sustained oscillation with no equilibrium — by construction. Field: applied dynamical systems / control. Sources: [V arXiv:1611.03537] (Korda & Mezić, linear predictors + MPC), [V arXiv:1911.08751] (KEEDMD), [V arXiv:2110.08442], [V arXiv:2308.13051], [V arXiv:2108.03712]. **Runtime: 64-dim lift = 64 PWL + 4096 MACs. At ceiling but feasible; 32-dim comfortably fits.**

**C30. Fixed-lag smoothing (deliberate latency budget).**
You currently lag +3 to +4 frames (64–85 ms) involuntarily. If you're paying the latency anyway, *spend* it: a 4-frame fixed-lag smoother sees the next switch before emitting, converting your lag from a defect into an anticipation window. Your gradient-boosting result says the future is not *predictable* — but with fixed lag you don't predict it, you observe it. Field: estimation theory. Sources: [V arXiv:1705.07598], Anderson & Moore *Optimal Filtering* (1979) [M]. **Runtime: a 4-frame delay line = 60 floats + a short FIR. Nearly free.** This is the cheapest available fix to the lag and it is orthogonal to everything else.

**C31. Amortised inference / posterior distillation.**
Run the expensive Bayesian machine (C24) offline over your 2540 frames, then train a small feedforward or recurrent net to map (i_t, v_t, state) → a *sample* from the posterior. Standard trick to make Family C runtime-feasible. Field: probabilistic ML. Source: [V arXiv:1711.08275]. **Runtime: whatever you distil into. This is a meta-method that rescues several impractical entries.**

---

# FAMILY D — Excitation ⊗ filter decompositions
*Objects: a product of a slow deterministic envelope and a fast endogenous excitation. The key move: aliveness lives in a separate signal path with its own generator, not in the residual of the main path.*

**D32. Source–filter / LPC excitation.**
Speech synthesis's foundational insight: separate a slowly-varying spectral envelope (predictable from symbols) from an excitation (unpredictable, generated). The failure mode of naive LPC vocoders — "buzzy, robotic, dead" — is *literally your problem*, and the field's answer was never "better envelope smoothing", it was always "better excitation". Field: speech coding. Sources: Makhoul, *Proc IEEE* 63 (1975) [M]; [V arXiv:1811.04769] (ExcitNet neural excitation), [V arXiv:2001.11686] (LPCNet with LP-structured mixture density). **Runtime: envelope (existing) + excitation generator (Family A or B) + one multiply per channel. Cheap.** Strongly recommended framing.

**D33. Deterministic-plus-stochastic (Serra–Smith SMS) decomposition.**
Explicitly model the signal as harmonic/deterministic part + stochastic residual with its own time-varying spectral envelope. The residual is *synthesised*, not added — it's shaped by the deterministic part's parameters. This is precisely the correction to your additive-noise experiment: the stochastic part must be *modulated by* the deterministic part, not summed with it. Field: audio DSP / spectral modelling synthesis. Source: Serra & Smith, *Computer Music Journal* 14(4) (1990) [M]. **Runtime: cheap.**

**D34. CELP-style stochastic codebook excitation.**
Rather than white noise, excite from a codebook of short structured excitation vectors. You have 2540 frames of ground truth — enough to build a small codebook of residual *shapes* even though you found it too sparse for unit selection over symbol *pairs*. The distinction matters: residual snippets are far more reusable than symbol-pair trajectories. Field: low-bitrate speech coding. Sources: Schroeder & Atal ICASSP 1985 [M]; [V arXiv:2512.00511] (parametric dithering in a low-complexity codec). **Runtime: baked codebook table + index selection + 15 MACs. Nearly free.**

**D35. Dither / noise-fill in coarse quantization.**
Established result: at low bit depths, *dithered* signals are preferred perceptually over undithered ones even at one bit *less* resolution. Modern codecs all inject noise-fill in bands they discard. Your channel is an extremely coarse quantizer; deliberate, correctly-shaped dither is the field's standard answer. Field: audio coding / information theory. Sources: [V arXiv:2512.00511], Nokia Bell Labs perceptual dither evaluation, IEEE 6771134 [V]; Lipshitz, Wannamaker & Vanderkooy, *JAES* 40 (1992) [M]. **Runtime: free.** Note this is *not* your additive-noise experiment: dither theory says the noise must be shaped and injected *before* the nonlinearity, and must be state-dependent.

**D36. Modulation-domain synthesis (McDermott–Simoncelli sound texture).**
Synthesise by matching a set of *statistics* — marginal moments, cross-channel correlations, and modulation-band energies — rather than by matching a waveform. Their central empirical finding: per-channel statistics alone give unconvincing textures; **cross-channel correlations** are what make them sound real. Your residual's negative cross-channel correlation (−0.068) is exactly the statistic this framework says is load-bearing and that your PCA-of-residual approach only partially preserves. Field: auditory neuroscience / texture synthesis. Sources: McDermott & Simoncelli, *Neuron* 71 (2011), doi:10.1016/j.neuron.2011.06.032 [V]; [V arXiv:1311.0407] (scattering moments), [V arXiv:1806.08002], [V arXiv:1801.02013], [V arXiv:2506.04073], [V arXiv:2208.10743]. **Runtime: analysis is offline; synthesis-by-matched-filterbank ≈ 15 channels × 6 bands = 90 IIR states, ~200 MACs. Affordable.** Conceptually this is the most rigorous "make the statistics right instead of the trajectory right" method in the survey.

**D37. Granular / concatenative texture synthesis.**
Overlap-add short grains drawn from a corpus with randomised placement. Differs from your failed unit-selection because grains are short (2–4 frames), unlabelled, and stochastically scheduled — so sparsity is not the binding constraint. Field: computer music. Sources: Schwarz, "State of the art in sound texture synthesis" [V]; Roads, *Microsound* (2001) [M]. **Runtime: table lookup + crossfade ≈ 50 MACs. Cheap.**

**D38. Motion texture (two-level statistical model).**
Li, Wang & Shum: decompose motion into "textons" (short linear dynamic systems) plus a Markov chain over texton transitions. Explicitly designed to synthesise motion that is *statistically* like the original and endlessly non-repeating. Field: computer graphics. Source: Li, Wang & Shum, "Motion Texture: A Two-Level Statistical Model for Character Motion Synthesis", SIGGRAPH 2002 [M — no arXiv]. **Runtime: an LDS per texton = 225 MACs. Cheap and a very close conceptual match.**

**D39. Motion-capture-assisted texturing (Pullen & Bregler).**
Take a coarse keyframed/low-detail motion and add the *high-frequency detail* from real capture data by matching low-frequency bands and transplanting the corresponding high bands. This is your exact problem statement: you have the low-frequency structure (channel-driven) and need believable high-frequency detail. Field: computer graphics. Source: Pullen & Bregler, "Motion Capture Assisted Animation: Texturing and Synthesis", SIGGRAPH 2002 [M]. **Runtime: needs a small database + band-matching ≈ moderate. Feasible if the database is baked.**

**D40. Motion signal processing (Bruderlin–Williams) / Fourier motion interpolation (Unuma).**
Treat motion channels as signals; decompose into frequency bands and manipulate bands independently — including *adding* band energy that the source lacks. Gives you a direct knob on your measured spectrum mismatch per band. Field: computer graphics. Sources: Bruderlin & Williams, SIGGRAPH 1995 [M]; Unuma, Anjyo & Takeuchi, SIGGRAPH 1995 [M]. **Runtime: 5-band filterbank per channel = ~150 IIR states, 300 MACs. Affordable.**

---

# FAMILY E — Learned sequence decoders on-substrate
*Objects: parametric maps with internal state, fit end-to-end. Genuinely different from A–D in that the dynamics are learned rather than designed — but note Fact 1: with an L2 objective these will land where your lookup table landed.*

**E41. GRU / LSTM / MGU decoder over (i_t one-hot, v_t).**
Standard. Gated recurrence expressible with your multiply + IIR + PWL primitives. With a hidden state of 32 and no equilibrium enforcement, it will still converge under constant input unless you force it otherwise (see E45). Field: deep learning. **Runtime: GRU with H=32, input 16: 3×(32×48) ≈ 4600 MACs. At/over ceiling; H=24 fits (~2600).**

**E42. Linear-recurrence state-space models (S4 / S5 / Mamba-style).**
Diagonal linear recurrence + input-dependent gating. Per-step cost is *linear in state size* rather than quadratic, so you can afford a much larger state than a GRU — 128 states for ~500 MACs. And a diagonal recurrence with complex eigenvalues on the unit circle is a bank of undamped oscillators: **no equilibrium, by construction**, with learnable frequencies. This is the family that best reconciles "learned", "cheap", and "never settles". Field: deep learning. Sources: [V arXiv:2312.00752] (Mamba), [V arXiv:2503.18970] (S4→Mamba survey), Gu, Goel & Ré S4, ICLR 2022 [M]. **Runtime: N=128 diagonal ≈ 512 MACs + 128 states + a small readout. Excellent fit — arguably the best substrate/expressivity ratio in the survey.**

**E43. Neural ODE / ODE-RNN / latent ODE.**
Continuous-time hidden state, updated by observations, evolving by learned ODE between them. Naturally handles the "68.7% of motion is between switches" issue because there *is* a defined evolution between events. Field: ML. Sources: [V arXiv:1907.03907] (latent ODEs for irregularly-sampled series), [V arXiv:2005.09807], [V arXiv:2306.01189] (neural SDE-RNN), [V arXiv:2209.01491]; Chen et al. Neural ODE NeurIPS 2018 [M]. **Runtime: one Euler step per frame = one MLP eval ≈ 1000–2000 MACs. Fits.** Known weakness for oscillatory regimes — see [V arXiv:2606.22075] (frequency-domain neural ODEs; note this ID is dated June 2026 and I'd verify it).

**E44. Neural SDE.**
As E43 but with a learned diffusion term, so the output is a sample path with the right roughness rather than a mean path. This is the learned counterpart of Family B and inherits its perceptual advantage. Field: ML. Source: [V arXiv:2306.01189]. **Runtime: E43 + a diffusion head ≈ +300 MACs.**

**E45. Explicitly oscillatory recurrent units (coupled-oscillator RNN / antisymmetric RNN).**
RNNs whose recurrent matrix is constrained antisymmetric or whose units are second-order oscillators, so the hidden state provably does not converge to a fixed point. This is the learned version of A3/B15 and the most direct architectural answer to "everything I try has an equilibrium". Field: ML / dynamical systems. Sources: Rusch & Mishra, coRNN ICLR 2021 [M]; Chang et al., AntisymmetricRNN ICLR 2019 [M]; [V arXiv:1807.10478] for the theory. **Runtime: as E41/E42.** *I did not verify these two IDs — search by title.*

**E46. Phase-functioned neural network (PFNN) / DeepPhase periodic autoencoder.**
Network weights are a function of a continuous phase variable rather than fixed — so the same input produces different output depending on where you are in a cycle, which breaks the memoryless-map structure that your diagnosis identifies as the common failure. DeepPhase learns the phase manifold unsupervised from motion data. Extracting an analogous phase from your 54 s of ground truth is directly testable offline. Field: computer graphics. Sources: Holden, Komura & Saito, "Phase-Functioned Neural Networks for Character Control", SIGGRAPH 2017 [V — confirmed]; Starke et al., "DeepPhase", *ACM TOG* 41(4) 2022, doi:10.1145/3528223.3530178 [V]; FunPhase periodic functional autoencoder (2025) [V]. **Runtime: weight blending across 4 phase bins = 4× the readout cost, but the bins are baked tables (free storage). ~2000 MACs. Fits.** Strong candidate — this is exactly "parameter count is nearly free" being exploited.

**E47. Mixture of experts / gating network.**
K expert linear maps, blended by a learned gate. Your substrate's multiply primitive is a gate. Trivially expressible, and gives per-symbol specialisation without a lookup table's discreteness. Field: ML. **Runtime: K=4 experts × 15×32 ≈ 2000 MACs. Fits.**

**E48. Conditional RBM / temporal RBM.**
Taylor & Hinton's motion model: binary latent units conditioned on recent history, sampled at runtime — so output is stochastic and non-repeating by construction. Field: ML / graphics. Source: Taylor, Hinton & Roweis, NIPS 2006; Taylor & Hinton ICML 2009 [M]. **Runtime: sampling K binary units ≈ K PWL + K×H MACs. Fits at K=32. Underrated for exactly the "sample not mean" reason.**

**E49. Neural codec decoder (VQ-VAE / RVQ decoder).**
Reframe: i_t **is** a VQ token index, at 46.875 Hz, from a codebook of 15. The entire neural-audio-codec field is the study of decoding low-rate token streams into convincing continuous signals, and their decoders are explicitly not trained on L2 alone — they use adversarial and feature-matching losses precisely because L2 decoders sound dead. This is your problem, solved at scale, in a different domain. Field: neural audio coding. Sources: [V arXiv:2411.18803] (TS3-Codec), [V arXiv:2502.04465] (FocalCodec), [V arXiv:2310.07246] (Vec-Tok), [V arXiv:2304.09116] (NaturalSpeech 2), van den Oord et al. VQ-VAE NeurIPS 2017 [M], "Self-Guidance: training VQ-VAE decoders robust to quantization artifacts" (OpenReview) [V]. **Runtime: full codec decoders are far too large. But the *architectural lesson* — decoder trained with adversarial + feature-matching loss — transfers at any size. Marked: borrow the objective, not the model.**

**E50. Hypernetwork / conditional weight generation.**
A small net generates the weights of the runtime net from (i_t, dwell, v_t). On your substrate this collapses to a large baked table indexed by symbol × dwell-bucket — i.e. free storage buying you a genuinely different function per context. Field: ML. **Runtime: table lookup + one small net ≈ 1000 MACs. Excellent substrate fit given "parameter count is nearly free".**

**E51. Two-thirds power law / kinematic invariant as an architectural constraint.**
Human movement obeys a lawful relation between curvature and speed. Imposing a known kinematic invariant on the output constrains it into the manifold of biologically plausible motion without needing to learn it. Field: motor neuroscience. Source: Richardson & Flash, *J Neurosci* 22(18):8201 (2002) [V]. **Runtime: one PWL + one multiply. Cheap. Speculative for a 15-simplex but cheap to test.**

---

# FAMILY F — Geometry and manifolds
*Objects: the shape of the space the trajectory lives in, rather than the dynamics on it.*

**F52. Wasserstein / optimal-transport interpolation between symbol targets.**
Interpolate between per-symbol target distributions along an OT geodesic rather than a linear/Euclidean path. Displacement interpolation *moves mass* between channels rather than fading one down while another fades up — so intermediate states remain concentrated instead of becoming mushy averages. That directly addresses why linear crossfades look dead: the midpoint of a crossfade is a blur, the midpoint of a transport is a shifted peak. Field: computer graphics / OT. Sources: Bonneel, Peyré & Cuturi, "Wasserstein Barycentric Coordinates", *ACM TOG* 35(4) 2016, doi:10.1145/2897824.2925918 [V]; Solomon et al., "Convolutional Wasserstein Distances", *ACM TOG* 34(4) 2015 [V]; [V arXiv:1710.03327]. **Runtime: Sinkhorn is iterative and impractical online — BUT with only 15 channels and a fixed ground metric, precompute the full 15×15 transport plan table and interpolate. Then it's a baked table: nearly free.** Very high value, and I'd flag this as the one your crossfade experiments were closest to missing.

**F53. Fisher–Rao / Hellinger (square-root) geometry on the simplex.**
Map x → √x, which sends the simplex to the positive orthant of a sphere; interpolate along great circles. Preserves concentration under interpolation far better than linear blending, and the map is one PWL each way. Field: information geometry. Source: Amari, *Information Geometry and Its Applications* (2016) [M]. **Runtime: 15 sqrt PWL + 15 square PWL + normalise ≈ 30 PWL. Nearly free.** Cheapest structural change in the whole survey — an afternoon's work.

**F54. Barycentric-coordinate / simplicial latent space.**
Learn a low-dimensional manifold of *observed* mixture configurations (your effective channel count is 3.48, so the real manifold is far below 15D) and constrain output to lie on it. Rules out configurations that never occur. Field: manifold learning. **Runtime: PCA-style projection = 15×4 ≈ 60 MACs. Free.**

**F55. Gaussian process dynamical model (GPDM).**
Nonlinear latent space with GP dynamics and GP observation map, marginalising parameters. Designed for exactly this: high-dimensional motion from a low-dimensional latent trajectory. Field: computer vision / graphics. Sources: Wang, Fleet & Hertzmann, *IEEE TPAMI* 30:283–298 (2008), doi:10.1109/TPAMI.2007.1167 [V]; [V arXiv:1107.4985] (variational GPDS). **Runtime: GP inference is O(N²) in training points — IMPRACTICAL online. Distil (C31) or use the sparse/inducing-point version baked as a fixed basis, which reduces to a plain PWL network.**

**F56. Performance animation from low-dimensional control signals (Chai & Hodgins).**
Reconstruct full high-dimensional motion from a *very* small number of control signals by using a local model built from nearest neighbours in a motion database, on the fly. This is your problem statement, in graphics, from 2005, with a real solution. Field: computer graphics. Source: Chai & Hodgins, SIGGRAPH 2005, *ACM TOG* 24(3):686–696 [M — no arXiv]. **Runtime: online kNN over a database. Moderately impractical, but with 15 symbols the "local model" reduces to a per-symbol linear model = a baked table. Then free.**

**F57. Style machines / style-content separation.**
Brand & Hertzmann: an HMM whose parameters vary continuously along a learned "style" manifold, so you get a continuum of behaviours rather than a discrete set of modes. The style variable is a natural home for v_t. Field: graphics / ML. Source: Brand & Hertzmann, "Style Machines", SIGGRAPH 2000 [M]. **Runtime: interpolating baked parameter tables. Cheap.**

**F58. Fractal interpolation functions (Barnsley).**
Interpolate between knots with a self-affine curve of controllable fractal dimension rather than a smooth spline. The interpolant is continuous but nowhere differentiable and has energy at all scales — it is *structurally incapable* of settling. One parameter (the vertical scaling factor) tunes roughness, and you could fit it to your measured spectrum. Field: fractal geometry. Sources: Barnsley, *Constructive Approximation* 2:303 (1986) [M]; Navascués, "Fractal Interpolation Functions: A Short Survey" [V]; IntechOpen chapter on self-affine FIF graphs [V]. **Runtime: the IFS iteration is not naturally causal-per-frame. Marked semi-impractical — but the *precomputed* version (bake a bank of fractal interpolant shapes, index by dwell) is free.**

---

# FAMILY G — Retrieval and example-based
*Objects: a database plus a search. Your unit-selection attempt was one point in a large space.*

**G59. Motion matching.**
Continuously search a feature-indexed database each frame for the best-matching short clip and blend. Differs from unit selection in that the query is a *continuous feature vector* (current state + desired future) rather than a discrete symbol pair — which is exactly why it doesn't hit the sparsity wall you hit. Field: games animation. Sources: Büttner & Clavet, GDC 2015 [M]; Springer *Encyclopedia of Computer Graphics and Games*, doi:10.1007/978-3-031-23161-2_511 [V]; [V arXiv:2310.05215], [V arXiv:2310.10079] (MOCHA). **Runtime: kNN over a small database. With 2540 frames and a 6-dim query, a baked kd-tree is ~50 comparisons. Feasible but branch-heavy — your substrate has no branching. Marked: needs a branchless soft-attention reformulation (which is just a weighted sum over baked keys — then it fits).**

**G60. Motion graphs.**
Precompute a graph of legal transitions between database frames; traverse at runtime. Field: graphics. Source: Kovar, Gleicher & Pighin, "Motion Graphs", SIGGRAPH 2002 [M]. **Runtime: graph traversal is branching. IMPRACTICAL on this substrate directly; equivalent to a baked Markov chain over 2540 states — which *is* expressible as a sparse matrix but is large.**

**G61. Soft attention over a baked exemplar set.**
The branchless version of G59/G60: keys and values are baked matrices; the query is the current state; the output is a softmax-weighted sum. Mathematically retrieval, computationally a single matrix multiply. Field: deep learning. **Runtime: 64 exemplars × 15 dims = 960 MACs + 64 exp PWL. Fits comfortably.** This is how I'd actually ship any retrieval idea here.

**G62. Nonparametric nearest-neighbour resampling / bootstrapping of residual segments.**
Rather than matching whole trajectories, resample *residual* segments conditioned on (symbol, phase-within-dwell) and overlap-add. Sidesteps sparsity because residuals are far more exchangeable than trajectories. Field: statistics / time-series bootstrap. Source: block-bootstrap literature, Künsch *Ann Stat* 17 (1989) [M]. **Runtime: table + crossfade. Free.**

---

# FAMILY H — Generative modelling with distributional objectives
*Objects: models fit to match a distribution rather than a conditional mean. Per Fact 1, this is the axis that matters most.*

**H63. Adversarial training (discriminator on trajectory windows).**
Train your existing architecture but replace/augment the L2 loss with a discriminator that sees short windows of x̂ and real x and must tell them apart. Blau & Michaeli prove GANs are a principled route to the perception-optimal end of the tradeoff [V arXiv:1711.06077]. Critically: **the discriminator is a training-time object only — runtime cost is zero.** Field: ML / graphics. Sources: [V arXiv:2011.02250] (video GAN review), [V arXiv:2005.11489] (AnimGAN), [V arXiv:2203.07706] (ActFormer), [V arXiv:2204.11751], [V arXiv:2511.21592] (MoGAN — adversarial objective in motion space specifically because hand-designed motion losses fail). **Runtime: 0 additional.** With 2540 frames the discriminator must be tiny, but window-level discrimination gives you ~2500 training examples per window size, which is workable.

**H64. Diffusion / score-based generation of trajectory segments.**
Generate the segment between switches by reverse diffusion conditioned on (i, v, dwell). The entire speech-driven-facial-animation field has converged on this. Field: graphics / ML. Sources: [V arXiv:2401.08655] (SAiD, blendshape diffusion), [V arXiv:2309.11306] (FaceDiffuser), [V arXiv:2402.05712] (DiffSpeaker), [V arXiv:2409.10848] (3DFacePolicy), [V arXiv:2303.11089] (EmoTalk), [V arXiv:2312.02781] (PMMTalk), [V arXiv:2303.09119] (co-speech gesture diffusion). **Runtime: multi-step reverse diffusion is IMPRACTICAL (10–50 net evals/frame). Distillation to 1–4 steps [V arXiv:2505.13447 MeanFlow] brings it near feasibility. Marked: impractical as-is, borderline if distilled.**

**H65. Flow matching / stochastic interpolants.**
Learn a velocity field transporting noise to data along a prescribed interpolant; single-ODE-solve sampling, and rectified/mean-flow variants get to one step. Substantially cheaper than diffusion at equal quality. Field: generative modelling. Sources: [V arXiv:2209.15571] (Albergo & Vanden-Eijnden), [V arXiv:2412.06264] (Flow Matching Guide and Code), [V arXiv:2501.16839], [V arXiv:2405.20879], Lipman et al. ICLR 2023 [M]. **Runtime: 1-step rectified flow = one net eval ≈ 1500 MACs. Fits.** The most substrate-compatible member of the modern generative family.

**H66. Normalizing flow with autoregressive conditioning.**
Invertible map from noise to output, exact likelihood, single forward pass. FlowVocoder shows small-footprint real-time versions exist. Field: ML / speech. Sources: [V arXiv:2109.13675] (FlowVocoder), [V arXiv:2506.03554], [V arXiv:2401.03078] (StreamVC), [V arXiv:2406.02897] (LiveSpeech). **Runtime: a small coupling-layer flow ≈ 2000 MACs. Borderline but fits.**

**H67. Variational autoencoder with a temporally-correlated prior.**
Sample the latent from a *smooth* stochastic process (OU) rather than i.i.d. Gaussian, so decoded output is temporally coherent yet never repeats. Cheap, and the OU prior is one IIR filter. Field: ML. **Runtime: decoder only ≈ 1000 MACs + 4 latent states. Fits well.** Underrated: this is the minimum-effort way to get "sample, not mean" behaviour.

**H68. Maximum caliber / maximum-entropy path ensembles.**
Choose the *least-biased trajectory distribution* consistent with your measured constraints (autocorrelation 0.58@1, cross-correlation −0.068, spectral band fractions, GV). This turns your measurement list directly into a generative model with no architecture choice at all — every number in your "Measured facts" section becomes a Lagrange constraint. Field: statistical physics. Sources: [V arXiv:1711.03450] (MaxCal as a general variational principle), [V arXiv:1404.3249], [V arXiv:2004.00624], Pressé et al. *Rev Mod Phys* 85:1115 (2013) [M]. **Runtime: the resulting process is usually a Gaussian/Markov process with a specific kernel — i.e. an IIR bank. Cheap.** I want to flag this one specifically: it is the most *principled* way to convert your existing measurements into a synthesiser, and I don't think it's on your radar.

**H69. Moment-matching / statistic-matching loss (non-adversarial).**
Train against a loss that penalises mismatch in a chosen statistic vector (spectral band energies, GV, cross-channel correlation, reversal rate) instead of pointwise error. Deterministic, stable, no GAN instability, and directly targets every number you measured. Field: texture synthesis / statistics. Sources: [V arXiv:1801.02013] (microcanonical models), McDermott & Simoncelli (2011) [V], Gatys et al. texture-by-Gram-matrices NeurIPS 2015 [M]. **Runtime: 0 additional (training-time only).** The lowest-risk member of Family H, and I'd try it before H63.

---

# FAMILY I — Objective, evaluation, and perceptual reframing
*Not models. These change what you optimise and measure. Given Fact 1 this family arguably dominates the others in expected value.*

**I70. Rate-distortion-perception as an explicit design target.**
Formalise: minimise distortion subject to a *hard constraint* that the output distribution match the source distribution (P(x̂) = P(x)), rather than minimising distortion alone. There is theory for the achievable region and for the penalty you pay. Sources: [V arXiv:1901.07821], [V arXiv:2204.06049], [V arXiv:2403.14849], [V arXiv:2401.12207], [V arXiv:1711.06077]. **Runtime: n/a.**

**I71. Distribution-preserving quantization / output-constrained lossy coding.**
The specific theory of reconstructions constrained to have the correct output distribution. Says explicitly that the optimal such reconstruction has *higher* MSE than the MMSE one — by a bounded, computable factor. Source: [V arXiv:2403.14849]. **Runtime: n/a.**

**I72. Perceptual metric design: replace RMSE with a temporal-alignment metric.**
Your consumer watches while hearing. The gesture-generation field measures Beat Consistency (motion-beat vs audio-beat alignment), diversity, and Fréchet distance — never MSE, for exactly your reason. Adopt BC and FGD analogues. Sources: [V arXiv:2303.09119], [V arXiv:2501.18898] (GestureLSM), [V arXiv:2503.01175] (HOP), [V arXiv:2401.00374] (EMAGE), [V arXiv:2406.15111]. **Runtime: n/a. Highest-leverage item in the survey if your evaluation loop is currently RMSE-driven.**

**I73. Global-variance / dynamic-range statistics as a first-class metric.**
Report mean max weight (0.494), effective channel count (3.48), entropy (1.465), and reversal rate (164.9/s) for the *output* alongside truth. Any method that fails to reproduce these is dead on arrival regardless of RMSE. You have already computed these for truth — turn them into a scoreboard. **Runtime: n/a.**

**I74. Uncanny-valley / perceptual-consistency literature.**
Established: reducing consistency in realism *increases* the uncanny effect, and degraded motion quality measurably shifts emotional response. Relevant because a half-alive reconstruction may score worse than an honestly-stylised one. Sources: PLOS Biology macaque uncanny valley (2025) [V]; [V arXiv:2605.06063] (avatar/face representation and perceptual evaluation of synthesized gestures — *note: ID dated May 2026, verify*); [V arXiv:2410.03714]. **Runtime: n/a.**

**I75. Idle-motion / keep-alive literature.**
Direct empirical work on "a character that stops moving reads as dead", including quantified findings that noise factors correlate most strongly with perceived aliveness among all tested factors. Sources: [V arXiv:2605.13693] (StayStill idle animation dataset — *ID dated May 2026, verify*), [V arXiv:1904.02898] (Nutty-based robot animation principles), Perlin, "Real Time Responsive Animation with Personality", *IEEE TVCG* 1(1) (1995) [M]. **Runtime: n/a.** Perlin's 1995 paper is the ur-text for "noise makes it alive" and is worth reading directly.

---

# FAMILY J — Timing, events, and the lag
*Objects: point processes and event algebra. Aimed specifically at your +3–4 frame lag and at switch-time structure.*

**J76. Zero-lag transient with post-hoc relaxation.**
Structural observation, not a citation: every filter you tried smooths the *target*, which necessarily delays the response. Invert it — apply the switch instantaneously (zero lag on the transient) and place all the smoothing on the *relaxation* afterward. Attack ~0 ms, release long. Your "asymmetric attack/release" experiment presumably still filtered the target; this is the degenerate limit where attack time is exactly zero. **Runtime: free.** Cheapest possible attack on the lag.

**J77. Hawkes / self-exciting point process for switch timing.**
Model switch times as a self-exciting process; the conditional intensity λ(t) is a continuous, always-varying signal that you can use as a *modulation source* even though you cannot predict the next switch. Note the distinction from your gradient-boosting result: predicting the next symbol at AUC 0.55 fails, but λ(t) is still a legitimate, informative continuous covariate reflecting recent event density. Field: point processes. Sources: [V arXiv:1708.06401] (tutorial), [V arXiv:1612.09328] (neural Hawkes, continuous-time LSTM), [V arXiv:1907.07561], [V arXiv:1708.02647], [V arXiv:2011.14650]. **Runtime: λ is one leaky integrator per channel = 15 states, 15 MACs. Free.** Nice: it converts your event stream into a smooth signal with no prediction claim.

**J78. Renewal-process / hazard-rate modulation.**
Compute the hazard rate h(τ) = P(switch now | dwell so far = τ) from your dwell distribution. This is a legitimate causal signal — no prediction of *which* symbol, only *how due* a switch is. It rises through a dwell and can drive anticipatory pre-motion, which is the only causally-available form of anticipation you have. Field: survival analysis / renewal theory. **Runtime: 1 counter + 1 PWL. Free.** I think this is genuinely underexploited given your AUC finding: your test showed *identity* is unpredictable; it did not show *timing* is uninformative, and hazard is exactly the timing-only signal.

**J79. Theta-rhythm entrainment to the syllabic envelope.**
Speech has a 4–8 Hz syllabic rhythm and cortex tracks it. Your sustained-viseme modulation is at 4–8 Hz. Your coherence test found the modulation is *not* coherent with v_t — but that tests coherence with the *level*, not with the *phase of a 4–8 Hz oscillator entrained to* v_t. Those are different tests and an entrained-phase oscillator can be locked in phase while showing near-zero magnitude-squared coherence with the raw envelope. Field: cognitive neuroscience. Sources: Giraud & Poeppel, *Nat Neurosci* 15:511 (2012) [M]; eLife 06213 (theta-gamma speech coding) [V]; Ghitza, "The theta-syllable" *Front Psychol* (2013), PMC3602725 [V]; [V arXiv:2507.15639]. **Runtime: one phase oscillator with a PLL = 3 states, ~10 MACs. Free.** Worth re-running your coherence analysis in the phase domain before discarding.

**J80. Snapshot interpolation / dead reckoning with a deliberate delay buffer.**
The games-networking solution to your exact problem shape (low-rate discrete updates → smooth continuous display). Standard practice is to buffer 2–3 updates and interpolate between *known* snapshots rather than extrapolate — accepting latency for smoothness. Same trade as C30, arrived at independently. Field: networked games. Sources: Aronson, "Dead Reckoning: Latency Hiding for Networked Games", Game Developer (1997) [V]; Unity Netcode for Entities interpolation docs [V]. **Runtime: a ring buffer. Free.**

---

# FAMILY K — Physics and biomechanics
*Objects: simulated bodies. Motion arises from dynamics rather than from a fitted curve.*

**K81. Second-order mass-spring-damper per channel (underdamped).**
Set ζ < 1 so each channel *overshoots and rings* rather than easing in. Ringing is motion after the input stops — a partial answer to equilibrium, though it does eventually settle. Field: graphics / classical mechanics. **Runtime: 2 states, 4 MACs per channel. Free.** Trivial, and probably not in your tried-list since your filters were all specified as low-pass orders 1–4 with easing profiles rather than as resonant second-order systems with ζ < 1 — though "resonant" in your list may cover this. If not, do it today.

**K82. Biomechanical facial muscle simulation.**
Simulate muscle activations and passive soft-tissue dynamics; visemes become muscle targets and the tissue supplies inertia, viscoelasticity, and coupling for free. Cross-channel anticorrelation emerges from tissue incompressibility rather than being imposed. Field: graphics / biomechanics. Sources: [V arXiv:0803.3924] (biomechanical face model with muscles for speech), [V arXiv:2402.19477] (learning a generalized physical face model), Sifakis et al. SIGGRAPH 2005 [M]. **Runtime: FEM is wildly IMPRACTICAL. But a 15×15 baked *coupling* matrix capturing tissue cross-talk, applied to a mass-spring layer, captures much of it for 225 MACs.**

**K83. Task dynamics / articulatory phonology (Saltzman & Munhall).**
Gestures are damped second-order dynamical systems on tract variables with overlapping *activation intervals*; coarticulation emerges from overlap rather than being modelled as blending. The activation-interval structure differs from your dominance-model attempt: activations are rectangular and the *dynamics* do the blending, rather than kernels being superposed. Field: speech science. Sources: Saltzman & Munhall, *Ecological Psychology* 1:333 (1989) [M]; Browman & Goldstein articulatory phonology [V]; [V arXiv:cmp-lg/9412007]; [V arXiv:2507.20343] (DYNARTmo); Byrd & Saltzman *J Phonetics* (2003) [V]. **Runtime: 2 states per gesture, ~100 MACs. Cheap.** Distinct enough from Cohen-Massaro to be worth separating: C-M superposes kernels additively; task dynamics superposes *activations* into a shared dynamical system, which is nonlinear and does not have the same monotone hold-vs-transient tradeoff you identified.

**K84. Position-based dynamics with constraint projection.**
Integrate freely, then project onto the simplex constraint. Decouples "make it move" from "make it valid", so the motion generator never has to respect the constraint. Field: real-time physics. Source: Müller et al., *J Visual Communication and Image Representation* 18 (2007) [M]. **Runtime: projection = clamp + renormalise ≈ 30 ops. Free. Useful infrastructure for Families A/B.**

**K85. Reaction-diffusion / Turing patterns over the channel graph.**
Treat the 15 channels as nodes with activator-inhibitor kinetics. Turing systems produce spontaneous, spatially structured, temporally persistent patterns from uniform input — the archetype of "structure from nothing". Field: mathematical biology. Source: [V arXiv:2103.03430] (minimal reaction-diffusion neural model). **Runtime: 30 states, ~250 MACs. Cheap. Speculative for this application but genuinely different in kind.**

---

# FAMILY L — Multi-rate signal processing and upsampling
*Objects: sampling theory. The plainest reading of the problem: you have a low-rate stream and need a high-rate one.*

**L86. Temporal disaggregation (Chow–Lin / Litterman / Denton).**
Econometrics' canonical solution to "produce a high-frequency series consistent with a low-frequency one plus a related high-frequency indicator". Your i_t is the low-frequency structure and v_t is the indicator. Chow–Lin is BLUE under an assumed residual covariance — and choosing that covariance to match your measured autocorrelation (0.58 at lag 1) is exactly the design freedom you want. Field: econometrics. Sources: Chow & Lin, *Rev Econ Stat* 53 (1971) [M]; [V arXiv:2108.05783] (sparse temporal disaggregation); Sax & Steiner, *R Journal* RJ-2013-028 [V]; MIDAS-based disaggregation, doi:10.1016/j.econmod.2015.06.008 [V]. **Runtime: the smoother is an IIR filter ≈ 50 MACs. Cheap.** A field you almost certainly haven't read, with a 50-year-old exact answer to a structurally identical problem.

**L87. MIDAS (mixed-data sampling) regression.**
Regress a high-frequency target on a parsimoniously-parameterised lag polynomial of mixed-frequency predictors. The Almon/Beta lag weights are a compact way to encode a long temporal context in very few parameters — attractive when you have 2540 training frames. Field: econometrics. Sources: Ghysels, Santa-Clara & Valkanov (2004) [M]; [V doi:10.1016/j.econmod.2015.06.008]. **Runtime: an FIR of length 20 = 300 MACs. Cheap.**

**L88. Video frame interpolation / temporal super-resolution.**
The observation that matters here: TSR can recover frequencies *beyond* the temporal Nyquist limit of the input by exploiting cross-scale patch recurrence — i.e. hallucinating high-frequency detail that is statistically correct rather than interpolating between knowns. That is philosophically the correct stance for your 24% unpredictable variance. Field: computer vision. Sources: [V arXiv:2003.08872] (deep internal learning TSR), [V arXiv:1912.07213] (FISR), [V arXiv:2207.08960], [V arXiv:2207.10765]. **Runtime: CNNs are impractical, but the *deep internal learning* idea — train on the signal's own cross-scale statistics, from your 54 s — transfers and is cheap to fit offline.**

**L89. Sinc / prolate-spheroidal reconstruction from nonuniform samples.**
Classical nonuniform sampling theory: reconstruct with the right basis for a nonuniform sample set rather than with a causal low-pass. Prolate spheroidal wave functions are optimal for time-limited/band-limited tradeoffs. Field: sampling theory. Source: Slepian & Pollak *BSTJ* 40 (1961) [M]; event-based reconstruction literature [V]. **Runtime: baked basis + weighted sum ≈ 200 MACs. Cheap. But acausal — needs C30's lag buffer.**

**L90. Multi-rate filterbank / wavelet band synthesis.**
Decompose into dyadic bands and synthesise each band with its own generator (deterministic for low bands from the channel, stochastic for high bands). Your spectrum table already gives you the per-band energy budget: 30.6 / 15.6 / 15.6 / 16.7 / 19.2 / 10.4 percent. That is a synthesis specification, not just a diagnostic. **Runtime: 6-band IIR bank per channel — 90 states, ~400 MACs. Fits.** Very direct translation of a measurement you already have into a design.

---

# FAMILY M — Impractical but map-completing
*Included per your request. Do not build these; they mark the boundary.*

**M91. Full Bayesian posterior sampling with MCMC per frame.** Correct, ~10⁶ ops/frame. IMPRACTICAL by 3 orders.

**M92. Large transformer decoder over symbol history.** Correct-ish, ~10⁷ MACs. IMPRACTICAL. Distillable in principle (C31) but 2540 frames won't train one.

**M93. Full diffusion sampling (50-step).** [V arXiv:2401.08655 etc.] IMPRACTICAL by ~50×. Distilled 1-step variants (H65) are the practical shadow.

**M94. Sinkhorn OT solved online.** IMPRACTICAL; the 15×15 baked-plan version (F52) is the practical shadow.

**M95. Physical FEM face simulation.** [V arXiv:0803.3924] IMPRACTICAL by many orders; the baked coupling matrix (K82) is the shadow.

**M96. Exact particle filter.** [V arXiv:1409.7287] IMPRACTICAL by ~50×; amortised distillation (C31) is the shadow.

**M97. Gaussian process regression with full kernel.** [V TPAMI 10.1109/TPAMI.2007.1167] IMPRACTICAL; inducing-point/random-feature approximation is the shadow and *is* cheap.

**M98. Reconstruction using the audio signal itself.** Ruled out by your problem statement, but noted for completeness: the entire speech-driven-animation field [V arXiv:2401.08655, 2309.11306, 2303.11089] assumes this channel. Your constraint is the thing that makes your problem unusual, and it's worth being explicit that it costs you the whole mainstream literature.

**M99. Non-causal offline optimisation over the whole utterance.** Trajectory-HMM MLPG in full form, OT over the whole sequence, Viterbi over the whole stream. IMPRACTICAL online; but per C30, a 4-frame window version of any of these is affordable and you're already paying the latency.

**M100. Neuromorphic / spiking implementation.** [V arXiv:2201.03006, 2505.13451] Correct mathematical framing (your channel *is* a spike train) but no hardware path on your substrate. Included because the *decoding theory* (C21) transfers even though the implementation doesn't.

---

# Additional sources consulted (not individually itemised above)

[V arXiv:2412.11982], [V arXiv:2101.11325], [V arXiv:2205.14856], [V arXiv:2509.05793], [V arXiv:2108.11417], [V arXiv:1809.04877], [V arXiv:2504.11757], [V arXiv:1412.4912], [V arXiv:1312.0781], [V arXiv:2411.16056], [V arXiv:2302.00519], [V arXiv:cond-mat/0607344], [V arXiv:1709.03140], [V arXiv:0909.1144], [V arXiv:2103.03430], [V arXiv:1103.5647], [V arXiv:2412.02510], [V arXiv:q-bio/0401013], [V arXiv:cond-mat/0508101], [V arXiv:2310.05144], [V arXiv:2503.01676], [V arXiv:2301.05832], [V arXiv:2107.00140], [V arXiv:1810.02647], [V arXiv:2104.09163], [V arXiv:2312.06184], [V arXiv:2312.10993], [V arXiv:2510.14427], [V arXiv:2510.12537], [V arXiv:2407.01624], [V arXiv:2403.06940], [V arXiv:2604.24833 — *ID dated April 2026, verify*], [V arXiv:2603.19474 — *verify*], [V arXiv:2605.26236 — *verify*], [V arXiv:2606.27627 — *verify*], [V arXiv:2604.12438 — *verify*], [V arXiv:2508.09857], [V arXiv:2502.08939], [V arXiv:2305.03568], [V arXiv:2511.01299], [V arXiv:2510.09245], [V arXiv:2512.18965], [V arXiv:2511.02091], [V arXiv:2503.18970], [V arXiv:2407.06136], [V arXiv:2509.16900], [V arXiv:2510.00862], [V arXiv:2506.03554], [V arXiv:2103.03344], [V arXiv:2111.08112], [V arXiv:1506.00540], [V arXiv:2502.20614], [V arXiv:2108.03831], [V arXiv:2407.21513], [V arXiv:1906.02643], [V arXiv:2603.08352 — *verify*], [V arXiv:2512.07551], [V arXiv:1710.03327], [V arXiv:2101.09225], [V arXiv:1703.05462], [V arXiv:2410.03714], [V arXiv:1811.04769], [V arXiv:2404.02411], [V arXiv:2512.02576], [V arXiv:2406.15111], [V arXiv:1810.12950], [V arXiv:2509.03399], [V arXiv:2507.13350], [V arXiv:2406.04843], [V arXiv:2311.11475], [V arXiv:1105.3927], [V arXiv:0907.3993], [V arXiv:1005.4765], [V arXiv:2506.04073], [V arXiv:2505.13451], [V arXiv:2404.11403], [V arXiv:2505.12173]. Plus non-arXiv: Scholarpedia ESN; ISCA Archive Saltzman 2008; PLOS Comput Biol 1013081 (balanced WTA networks); PLOS One 0138947; PMC5293789 (WTA attractor dynamics); PMC4086298; PMC4160925; PMC6502337; PMC3602725; eLife 06213; eLife 110310; *Nature* 394:780; *J Neurosci* 22:8201; ACM TOG 10.1145/2897824.2925918; ACM TOG 10.1145/2766963; ACM TOG 10.1145/3528223.3530178; IEEE TPAMI 10.1109/TPAMI.2007.1167; Springer 10.1007/s11042-019-7641-1; Springer 10.1007/978-3-031-23161-2_511; Elsevier 10.1016/j.csl.2006.01.002; Elsevier S0885230898900481; Elsevier S088523080700023X; Frontiers fncom.2022.872093; Frontiers fnhum.2012.00250; Frontiers fnins.2025.1610766; Frontiers fncom.2011.00024; PNAS 10.1073/pnas.1400823111; IEEE 905890; IEEE 9290066; IEEE 6771134; IEEE 1169058; Grokipedia/Wikipedia Perlin noise; Game Developer dead-reckoning; Unity Netcode docs; O3DE motion matching.

**Count: ~185 distinct sources, ~155 with verifiable identifiers.**

---

# Top picks

You asked for these last. Ranked by expected perceptual gain per unit of your effort.

**1. Change the objective before changing the model (I72 + I73 + H69, then H63).**
This is my strongest recommendation and it is not a model at all. Blau & Michaeli's theorem says the conditional-mean lookup being simultaneously best-RMSE and worst-perceptually is not a quirk of your data — it is the guaranteed behaviour of the MMSE point on the R-D-P curve, for *any* distortion measure. As long as your fitting loop and your model selection run on RMSE, every family in this survey will converge back to the same failure, which is a fair mechanical description of the tail-chasing. Build the scoreboard first (I73: mean max weight, effective channel count, entropy, reversal rate, per-band spectral fractions, cross-channel correlation — you already compute all of these for truth), then fit against a statistic-matching loss (H69) before reaching for adversarial training (H63). Runtime cost: zero. This reorders which of the other 90 methods you'd even be able to evaluate.

**2. Give the system a genuine non-equilibrium core (A9 or E42, backstopped by A1/A7).**
Your structural diagnosis is exactly right and has a precise fix: an autonomous dynamical component that does not converge under constant input. Three routes, in increasing order of how much I'd bet on them fitting your substrate:
- **E42, diagonal linear recurrence with complex eigenvalues on the unit circle** — a bank of undamped oscillators, learnable frequencies, 128 states for ~512 MACs. Provably no equilibrium, and it is *literally* your IIR primitive with a 2×2 rotation instead of a scalar. Cheapest correct answer.
- **A9, echo state network with spectral radius slightly above 1** — reservoir is a baked random matrix (free), only the linear readout is fit, so training is a least-squares solve on 2540 frames, which is the right size. Near-zero fitting risk.
- **A7, a Lorenz/Rössler integrator as a modulation source** — 3 states, ~10 MACs, deterministic, never repeats. Absurdly cheap. Even if it isn't the answer, it's a one-hour experiment that tells you whether endogenous aperiodic motion is what's missing, cleanly separated from every other variable.

**3. Split into excitation × envelope and make the excitation state-dependent (D32 + B16 + D36).**
Your additive-noise experiment failed in a diagnostically useful way. Three specific defects, each with a named fix: the noise was *additive* where your own measurement (std 0.129 at level 0.638, a constant CV of 0.20) says it should be *multiplicative* (B16, Harris & Wolpert); it was matched on marginals where McDermott & Simoncelli's central finding is that **cross-channel correlations** are what make synthetic texture read as real (D36) — and you measured that correlation at −0.068 without making it a synthesis target; and it was summed onto the output where source-filter theory says it should be modulating the envelope (D32). Same architecture, three changes, all cheap. This is the highest-probability *rescue* of work you've already done.

**4. Two nearly-free experiments to run this week.**
- **C28, global variance compensation.** The speech-synthesis community independently discovered your exact finding — statistical generation under-varies, and matching global variance improves perception while worsening RMSE — and the fix is one scale factor per channel. If your output's GV is below truth's, this is a same-day change with a known-large effect.
- **C30/J80, spend the lag you're already paying.** You lag 64–85 ms involuntarily. A 4-frame fixed-lag buffer converts that into *observing* the next switch rather than predicting it — which sidesteps your AUC-0.55 result entirely, because you no longer need to predict anything. Cost: a 60-float ring buffer. Both the estimation-theory and games-networking literatures reached this independently.

**Two things I'd flag as probably-unexplored in your search so far:** C21 (time-encoding machines — there is an *exact recovery theorem* for reconstructing a continuous signal from event times alone, and its practical form is a precomputed time-varying FIR, which is precisely the primitive your substrate makes free), and H68 (maximum caliber — a principled route that turns your existing list of measured statistics directly into a generative process without you choosing an architecture at all). Both come from fields adjacent to yours in mathematical shape but distant in citation network, which is where I'd expect a stuck search to be missing things.

**One correction to a conclusion in your brief.** You reported that the channel does not predict the next switch (AUC 0.55, next-symbol 14.3% vs 12.0%) and concluded "anticipation is not causally available." That result rules out predicting switch *identity*. It does not rule out J78's hazard rate — how *due* a switch is given time-since-switch — which is a pure timing signal computable from your dwell distribution with a counter and a PWL, and which is informative precisely because your dwell distribution is strongly non-geometric (37.5% ≤ 2 frames alongside 9.3% ≥ 8). Identity-unpredictable and timing-uninformative are separate claims, and you've only measured the first.agentId: a2058dff3cb304488 (use SendMessage with to: 'a2058dff3cb304488', summary: '<5-10 word recap>' to continue this agent)
<usage>subagent_tokens: 147419
tool_uses: 49
duration_ms: 801792</usage>