# Reconstruction from argmax observations — literature survey

209 sources across 8 independent mathematical framings. None searched with
domain terms (no "viseme", no "vrchat", no biology, no face/speech in the
query strings; a few classic results surfaced from speech-adjacent venues and
are included on their generic math content).

**Abstract problem as searched:** hidden smooth `x_t` on the probability
simplex in R^15; observed only `i_t = argmax_k x_t[k]` plus scalar magnitude
`v_t`; reconstruct continuous `x̂_t` in a few hundred flops/frame with no
neural inference at runtime. Naive reconstruction is piecewise-constant
between index switches (staircase).

Items marked *(unverified)* had a title, ID, or author list that could not be
confirmed past a search snippet.

---

## THE CONVERGENT RESULT

Five unrelated literatures independently prove the same theorem:

> The conditional-mean estimator is variance-deficient by exactly the
> estimation variance. The fix is not a better filter — it is to output a
> **posterior sample** rather than the posterior mean, i.e. to add back a
> random field with exactly the missing covariance.

| Field | Name of the result | Source |
|---|---|---|
| Geostatistics | Conditioning by kriging / conditional simulation | §6 #31 |
| Image restoration | Perception–distortion tradeoff | §2 #1 |
| Data assimilation | Stochastic vs deterministic EnKF update | §6 #21 |
| Statistical synthesis | Global variance / modulation spectrum compensation | §4 #4, #5 |
| Quantization theory | Dithered quantization | §1 #14, §2 #8 |
| Missing data | Multiple imputation under-disperses if single | §2 #21 |

This is the formal justification for generating motion statistically. It also
explains the measured result that the exact conditional-mean table scored
*worse* than a cruder filter: the mean is the wrong functional, not a
badly-fitted one.

## THE DIAGNOSED FIX FOR THE JITTER

Rice's formula (§5 #1) gives the expected direction-reversal rate of a
stationary Gaussian process in closed form:

```
reversal rate = (1/π) · sqrt(λ4 / λ2)      λ_k = k-th spectral moment
```

Measured excess is 2.2×, therefore the generator's `λ4/λ2` is **4.84× too
large**. Cause is identified: single-pole (OU / EMA-filtered white noise) has
non-differentiable sample paths, so λ4 is unbounded — a one-pole noise
generator *cannot* have a correct reversal rate at any time constant. The
Matérn state-space family (§5 #12, #13) fixes it: smoothness order ν ≥ 5/2
(3-pole) gives finite λ4, and ν is exactly the knob that sets it. Runtime is
one small matvec plus a Gaussian draw per frame.

The measured spectrum is bimodal (31% below 1 Hz, 19% at 8–16 Hz), so a single
Matérn will not fit — a sum of one long-τ Matérn and one damped-oscillator
(celerite, §5 #17) term stays linear state-space and stays cheap.

No source was found treating crossing statistics *under a simplex/sum-to-zero
constraint*; the constrained-GP papers (§5 #25, #26) handle the constraint and
Rice handles the crossings, but the composition appears unstudied.

---

# §1 — Coarse / sign / argmax / ordinal measurement reconstruction

1. arXiv:1104.3160 — Robust 1-Bit Compressive Sensing via Binary Stable Embeddings of Sparse Vectors — sign measurements preserve direction, destroy magnitude; mirrors the index/magnitude split
2. arXiv:1202.1212 — Robust 1-bit compressed sensing and sparse logistic regression: A convex programming approach
3. Plan & Vershynin, One-Bit Compressed Sensing by Linear Programming, Comm. Pure Appl. Math. 66(8) 2013 — consistency-set view of "all x compatible with argmax = i_t"
4. arXiv:1208.6279 — One-bit compressed sensing with non-Gaussian measurements
5. arXiv:1404.6853 — One-bit compressive sensing with norm estimation — the theoretical role of the magnitude side channel
6. arXiv:1009.3145 — Universal Rate-Efficient Scalar Quantization — non-monotone index maps carry more information than naive decoding extracts
7. **arXiv:1405.7094 — Error bounds for consistent reconstruction: random polytopes and coverage processes** — MSE constants for interior-point decoding, with explicit d³ dimension dependence
8. **arXiv:1406.0022 — Error Decay of (almost) Consistent Signal Estimations from Quantized Gaussian Random Projections**
9. Goyal, Vetterli, Thao, Quantized Overcomplete Expansions in R^N, IEEE T-IT 44(1) 1998 — origin of the O(1/r) vs O(1/r²) gap
10. arXiv:1307.2136 — Near-Optimal Encoding for Sigma-Delta Quantization of Finite Frame Expansions
11. **Chou & Güntürk, Distributed Noise-Shaping Quantization I: Beta Duals of Finite Frames, Constructive Approximation 44, 2016** — linear decoder, one matvec/step, running residual prevents freezing
12. Güntürk, One-bit sigma-delta quantization with exponential accuracy, CPAM 56(11) 2003 — error feedback beats memoryless quantization
13. arXiv:1001.5079 — An Optimal Family of Exponentially Accurate One-Bit Sigma-Delta Quantization Schemes
14. Randomly dithered quantization and sigma-delta noise shaping for finite frames, ACHA 2008 *(authorship unverified)*
15. Dirksen, Quantized Compressed Sensing: A Survey, Springer 2019 — best entry point
16. arXiv:1405.1194 — Quantization and Compressive Sensing
17. arXiv:2401.08402 — Uniform Recovery Guarantees for Quantized Corrupted Sensing Using Structured or Generative Priors
18. arXiv:2306.15758 — On the reconstruction of bandlimited signals from random samples quantized via noise-shaping — closest formal analogue: smooth-in-time signal through a coarse quantizer
19. arXiv:1911.07525 — On one-stage recovery for ΣΔ-quantized compressed sensing
20. arXiv:1105.6368 — Message-Passing Estimation from Quantized Samples
21. arXiv:2003.00083 — Nonparametric Estimation in the Dynamic Bradley-Terry Model
22. arXiv:2307.16642 — A Spectral Approach for the Dynamic Bradley-Terry Model
23. Maystre & Grossglauser, Fast and Accurate Inference of Plackett–Luce Models, NeurIPS 2015 — top-1 choice observation yields spectral estimator of the latent simplex vector
24. arXiv:2308.02918 — Spectral Ranking Inferences based on General Multiway Comparisons
25. arXiv:2110.01515 — A Review of the Gumbel-max Trick and its Extensions
26. arXiv:2406.02180 — On The Statistical Representation Properties Of The Perturb-Softmax And Perturb-Argmax Distributions — identifiability from argmax observations
27. Ribeiro, Giannakis, Roumeliotis, SOI-KF, IEEE T-SP 54(12) 2006
28. arXiv:1704.02641 — Quantized Innovations Bayesian Filtering
29. arXiv:2107.03344 — Time Encoding of Finite-Rate-of-Innovation Signals
30. arXiv:1802.04672 — Delta-Ramp Encoder for Amplitude Sampling and its Interpretation as Time Encoding
31. Reconstruction of Signals from Level-Crossing Samples Using Implicit Information *(venue unverified)* — POCS on "signal stayed between levels", direct analogue of "x stayed in argmax cell k"
32. Regularized signal reconstruction for level-crossing sampling using Slepian functions, Signal Processing 92(4) 2012

# §2 — Why the mean is the wrong estimator

1. arXiv:1711.06077 — The Perception-Distortion Tradeoff (Blau & Michaeli, CVPR 2018)
2. arXiv:2211.08944 — Reasons for the Superiority of Stochastic Estimators over Deterministic Ones (ICML 2023) — observation-consistent + perfect-perceptual ⇒ must be a posterior sampler
3. arXiv:2410.00418 — Posterior-Mean Rectified Flow — keep the mean table, add a transport step
4. arXiv:2306.02342 — Deep Optimal Transport: A Practical Algorithm for Photo-realistic Image Restoration
5. **arXiv:2306.02400 — Perceptual Kalman Filters: Online State Estimation under a Perfect Perceptual-Quality Constraint** — causal, closed-form, linear-Gaussian; greedy per-frame is provably wrong
6. arXiv:2310.16047 — From Posterior Sampling to Meaningful Diversity in Image Restoration
7. arXiv:1309.2915 — Randomized Quantization and Source Coding with Constrained Output Distribution
8. arXiv:1811.06856 — Estimation from Quantized Gaussian Measurements: When and How to Use Dither
9. arXiv:2106.02782 — On Perceptual Lossy Compression — cost of perfect perceptual quality is exactly 2×MMSE
10. arXiv:1901.07821 — Rethinking Lossy Compression: The Rate-Distortion-Perception Tradeoff
11. arXiv:2106.10311 — Universal Rate-Distortion-Perception Representations for Lossy Compression
12. arXiv:2202.04147 — The Rate-Distortion-Perception Tradeoff: The Role of Common Randomness
13. arXiv:2204.06049 — On the Rate-Distortion-Perception Function
14. arXiv:2006.14200 — SRFlow: Learning the Super-Resolution Space with Normalizing Flow
15. arXiv:2204.02028 — A Generative Deep Learning Approach to Stochastic Downscaling of Precipitation Forecasts
16. arXiv:2309.04452 — Postprocessing of Ensemble Weather Forecasts Using Permutation-invariant Neural Networks
17. **Schefzik & Möller, Ensemble calibration with preserved correlations (ECC), QJRMS 2017** — sample marginals then rank-reorder; on K=15 that is a sort
18. A Similarity-Based Implementation of the Schaake Shuffle, Mon. Wea. Rev. 144(5) 2016 — borrow a real trajectory's rank structure from a corpus
19. Scheuerer et al., Preferential selection of dates in the Schaake shuffle, WRR 2017
20. Gneiting & Raftery, Strictly Proper Scoring Rules, Prediction, and Estimation, JASA 2007 — MSE is proper only for the mean functional
21. arXiv:1801.04058 — Multiple Imputation: A Review
22. Beyond MMSE: Enhancing PnP Restoration with ProxiMAP *(ID unverified)*

# §3 — Simplex-constrained processes

1. **arXiv:2405.14664 — Fisher Flow Matching for Generative Modeling over Discrete Data** — square-root/sphere map gives closed-form simplex geodesics
2. **arXiv:1305.4571 — Optimal filtering and the dual process** (Bernoulli 2014) — finite-dimensional closed-form filters for Wright–Fisher/Dirichlet signals
3. **arXiv:2305.10699 — Dirichlet Diffusion Score Model** — Jacobi diffusion stays exactly on the simplex, provably never absorbs/freezes
4. arXiv:2210.14784 — Categorical SDEs with Simplex Diffusion — closed-form noncentral-χ² transitions
5. arXiv:2402.05841 — Dirichlet Flow Matching — naive linear interpolation on the simplex is pathological
6. arXiv:2309.02530 — Diffusion on the Probability Simplex — softmax of OU; cheapest construction here
7. arXiv:2510.27480 — Simplex-to-Euclidean Bijections for Categorical Flow Matching
8. arXiv:1506.06998 — Exact simulation of the Wright-Fisher diffusion
9. arXiv:1909.11626 — Exact simulation of coupled Wright-Fisher diffusions
10. arXiv:2410.11429 — Filtering coupled Wright–Fisher diffusions
11. arXiv:1411.4944 — Filtering hidden Markov measures *(title unverified)*
12. arXiv:1302.0115 — A Bayesian nonparametric approach to modeling market share dynamics — compositional time series prior
13. arXiv:2201.05197 — Aitchison's Compositional Data Analysis 40 Years On
14. Egozcue et al., Isometric Logratio Transformations for Compositional Data Analysis, Math. Geology 35(3) 2003
15. arXiv:2302.00519 — Time series models on compact spaces
16. arXiv:2404.07586 — State-Space Modeling of Shape-constrained Functional Time Series
17. arXiv:2402.18130 — Sequential Change-point Detection for Compositional Time Series
18. arXiv:1309.1541 — Projection onto the probability simplex — O(d log d) exact
19. arXiv:1611.00712 — The Concrete Distribution — temperature = "how peaked is the composition given its argmax"
20. arXiv:1611.01144 — Categorical Reparameterization with Gumbel-Softmax
21. arXiv:1911.01876 — Information Geometry of the Probability Simplex: A Short Course
22. arXiv:2106.13477 — Hessian informed mirror descent — iterates provably stay in the relative interior, never hit a face
23. arXiv:2507.15264 — Interior mirror descent flow *(title unverified)* — reduces to replicator dynamics
24. arXiv:2506.00485 — Information Geometry on the ℓ²-Simplex via the q-Root Transform
25. EWF: simulating exact paths of the Wright–Fisher diffusion, Bioinformatics 39(1) 2023

# §4 — Trajectory generation from discrete symbols (the over-smoothing literature)

1. Tokuda et al., Speech parameter generation algorithms, ICASSP 2000 — MLPG; dynamic-feature constraints turn a state sequence into a smooth trajectory
2. Zen, Tokuda, Kitamura, Reformulating the HMM as a trajectory model, Computer Speech & Language 2007 — identifies piecewise-constant-per-state as the *cause* of the staircase
3. Zen et al., An introduction of trajectory model into HMM-based synthesis, SSW5 2004
4. Toda & Tokuda, A Speech Parameter Generation Algorithm Considering Global Variance, IEICE E90-D(5) 2007 — the canonical over-smoothing fix
5. Takamichi et al., Parameter generation algorithm considering modulation spectrum, ICASSP 2015 — match the whole temporal modulation spectrum, not just variance
6. Takamichi et al., Postfilters to Modify the Modulation Spectrum, IEEE/ACM TASLP 2016 — cheap post-hoc filter on an already-generated trajectory
7. Wu & Wang, Minimum Generation Error Training, ICASSP 2006
8. arXiv:1602.06727 — Improving Trajectory Modelling for DNN-based Synthesis with MGE Training
9. arXiv:1709.08041 — Statistical Parametric Synthesis Incorporating GANs — over-smoothing as distribution divergence
10. arXiv:1704.03626 — Sampling-based speech parameter generation using moment-matching networks
11. **Zen & Sak, Unidirectional LSTM-RNN with Recurrent Output Layer for Low-Latency Synthesis, ICASSP 2015** — recurrent output layer = cheap recursive replacement for batch MLPG, zero lookahead
12. arXiv:1606.06061 — Fast, Compact, and High Quality LSTM-RNN Based Synthesizers for Mobile Devices
13. **Wang, Takaki, Yamagishi, Autoregressive Neural F0 Model, IEEE/ACM TASLP 26 2018** — shallow autoregression: previous output fed back, one-step recursion, no batch solve
14. arXiv:1804.02549 — A comparison of recent waveform generation and acoustic modeling methods
15. Yu, Hidden semi-Markov models, Artificial Intelligence 174(2) 2010
16. **arXiv:2004.08561 — A New Smoothing Algorithm for Jump Markov Linear Systems** — exact two-filter recursive smoothing across discrete regime switches
17. arXiv:2004.08565 — Bayesian Parameter Identification for Jump Markov Linear Systems
18. arXiv:2503.13973 — Identification of non-causal systems with random switching modes *(authors unverified)*
19. Kovar, Gleicher, Pighin, Motion Graphs, ACM TOG 2002 — concatenative alternative
20. Li, Wang, Shum, Motion texture: a two-level statistical model, ACM TOG 21(3) 2002 — discrete textons + continuous LDS within each
21. Sarawagi & Cohen, Semi-Markov CRFs, NIPS 2004
22. Paraschos et al., Probabilistic Movement Primitives, NIPS 2013 — distribution over trajectories; blending without variance collapse
23. Paraschos et al., Using probabilistic movement primitives in robotics, Autonomous Robots 42, 2018
24. Ling, Richmond, Yamagishi, An analysis of HMM-based prediction of articulatory movements, Speech Communication 2010
25. Efficient Implementation of Global Variance Compensation, 2016 *(venue unverified)*
26. arXiv:1906.08977 — Singing Voice Synthesis Using Deep Autoregressive Neural Networks

# §5 — Prescribed-statistics noise synthesis (the jitter fix)

1. **Rice's formula** — E[crossings] = (t/π)·√(λ₂/λ₀)·exp(−y²/2λ₀); applied to the derivative process gives reversal rate = (1/π)√(λ₄/λ₂)
2. arXiv:2205.08742 — Kac-Rice formula: A contemporary overview
3. Gaussian Integrals and Rice Series in Crossing Distributions, Statistical Science 34(1) 2019
4. **Kedem, Time Series Analysis by Higher Order Crossings, IEEE Press 1994** — exact spectral representation of crossing counts; discrete-time form of the reversal diagnostic
5. arXiv:2007.14220 — Effective computations of joint excursion times for stationary Gaussian processes
6. Zero-Crossing Rates of Some Non-Gaussian Processes, UMD dissertation
7. Kasdin, Discrete Simulation of Colored Noise and 1/f^α Power Law Noise Generation, Proc. IEEE 83(5) 1995
8. Kasdin & Walter, Discrete simulation of power law noise, IEEE Freq. Control Symp.
9. Burkardt, COLORED_NOISE reference implementations
10. Fast and Exact Synthesis for 1-D fBm and fGn, IEEE SPL — Davies–Harte circulant embedding
11. arXiv:1604.00362 — Fast and exact simulation of complex-valued stationary Gaussian processes — validity conditions incl. covariances negative at all nonzero lags
12. **Hartikainen & Särkkä, Kalman Filtering and Smoothing Solutions to Temporal GP Regression Models, IEEE MLSP 2010** — Matérn kernels as exact finite-dimensional SDEs
13. **Särkkä, Solin, Hartikainen, GP Regression Through Kalman Filtering, IEEE SPM 30(4) 2013** — readable spectral-density → state-space recipe
14. Solin & Särkkä, Mixture representation of the Matérn class *(version unverified)* — explicit F, L, Q per smoothness order
15. arXiv:2107.07098 — Hida-Matérn Kernel — control derivative variance λ₂ explicitly while holding λ₀ fixed
16. arXiv:2003.05554 / JMLR 22 — A general linear-time inference method for GPs on one dimension
17. **arXiv:1703.09710 — celerite** — sums of exponentials and damped oscillators, linear-time; fits a bimodal spectrum
18. Kelly et al., CARMA, ApJ 788:33 2014 — analytic PSD, Kalman recursions; fit then run forward as generator
19. arXiv:2408.15081 — Simulating CARMA Processes Driven By Tempered α-Stable Lévy Processes
20. **arXiv:1210.0312 — Modeling stationary data by generalised Ornstein-Uhlenbeck processes** — sampled OU(p) is *not* AR(p) for p>1; direct warning that a single-pole filter cannot give the right reversal statistics
21. **Habets, Cohen, Gannot, Generating nonstationary multisensor signals under a spatial coherence constraint, JASA 124(5) 2008** — fixed mixing matrix induces prescribed cross-channel correlation
22. Generating coherence-constrained multisensor signals using balanced mixing, JASA 149(3) 2021
23. Portilla & Simoncelli, A Parametric Texture Model Based on Joint Statistics, IJCV 40(1) 2000 — iterative projection onto statistical constraint sets
24. McDermott & Simoncelli, Sound Texture Perception via Statistics of the Auditory Periphery, Neuron 71(5) 2011 — marginals alone fail; cross-channel correlation is not optional
25. arXiv:2202.01793 — Incorporating Sum Constraints into Multitask Gaussian Processes — project the covariance, not the samples
26. arXiv:1703.00787 — Linearly constrained Gaussian Processes

# §6 — Filtering under coarse measurement; why filters freeze

1. arXiv:1704.02641 — Quantized Innovations Bayesian Filtering
2. Ribeiro et al., SOI-KF, IEEE T-SP 2006
3. arXiv:2112.07828 — On Recursive State Estimation for Linear State-Space Models Having Quantized Output Data
4. arXiv:2509.07837 — Filtering in Multivariate Systems with Quantized Measurements using a Gaussian Mixture-Based Indicator Approximation
5. arXiv:2507.17284 — State Estimation with 1-Bit Observations: Bussgang Meets Kalman
6. Multiple-Level Quantized Innovation Kalman Filter (MLQ-KF), IFAC
7. Quantized innovations Kalman filter: stability and modification with scaling quantization, JZUS-C
8. Allik et al., The Tobit Kalman Filter, IEEE TAC 2015 — recursive unbiased update for censored/set-valued measurement
9. arXiv:1911.06190 — An Improved Tobit Kalman Filter with Adaptive Censoring Limits
10. arXiv:2002.08597 — Kalman Filtering With Censored Measurements
11. arXiv:1604.00217 — Moving horizon estimation with binary sensors *(title unverified)* — observability under threshold-only output
12. arXiv:2504.03885 — Sparsity-Promoting Reachability Analysis and Optimization of Constrained Zonotopes
13. arXiv:2309.13889 — Resilient State Estimation via Input and State Interval Observer Synthesis
14. **Marck & Sijs, Event-based State Estimation with Negative Information** — updating from the sole knowledge that the measurement did *not* change
15. State estimation considering negative information with switching Kalman and ellipsoidal filtering, IEEE 2016
16. Miśkowicz, Send-On-Delta Concept, Sensors 6(1) 2006 — absence of a report bounds the signal to a band
17. arXiv:1703.08342 — Event-based State Estimation: An Emulation-based Approach
18. Morzfeld et al., What the collapse of the EnKF tells us about particle filters, Tellus A 69 2017 *(authorship unverified)*
19. **Anderson, Spatially and temporally varying adaptive covariance inflation, Tellus A 61(1) 2009** — innovation-driven online inflation
20. arXiv:2110.06769 — Dynamical effects of inflation in ensemble-based data assimilation
21. **Sakov & Oke, A deterministic formulation of the EnKF, Tellus A 60(2) 2008** — stochastic update is variance-preserving by construction; deterministic is not
22. Anderson, Square Root and Perturbed Observation Ensemble Generation Techniques, MWR 141(7) 2013 *(authorship unverified)*
23. arXiv:1811.06856 — Estimation from Quantized Gaussian Measurements: When and How to Use Dither
24. Bayesian Parameter Estimation Using Single-Bit Dithered Quantization, IEEE T-SP — added noise *improves* estimation from 1-bit data
25. arXiv:1306.3875 — Roughening Methods to Prevent Sample Impoverishment in the Particle PHD Filter
26. arXiv:1308.2443 — Fight sample degeneracy and impoverishment in particle filters
27. arXiv:1502.03697 — Nonlinear state space smoothing using the conditional particle filter — draws trajectories, so output stays as rough as the process
28. arXiv:2205.13898 — Conditional particle filters with bridge backward sampling — fixes degradation under weakly informative observations
29. arXiv:2101.03612 — Randomized maximum likelihood based posterior sampling
30. Kriging, Splines, Conditional Simulation, Bayesian Inversion and Ensemble Kalman Filtering, Springer 2018
31. **Hadavand & Deutsch, Conditioning by Kriging, Geostatistics Lessons** — the optimal estimator's variance is deficient by exactly the estimation variance; add a correctly-covarianced random field to restore it
32. arXiv:2412.05136 — Recursive Projection-Free Identification with Binary-Valued Observations

# §7 — Staircasing and higher-order regularization

1. **Ring, Structural properties of solutions to total variation regularization problems, ESAIM M2AN 34(4) 2000** — the theorem: 1-D TV solutions are constant a.e.
2. arXiv:1402.0091 — Some remarks on the staircasing phenomenon in TV-based denoising
3. Chambolle & Lions, Image recovery via total variation minimization, Numer. Math. 76(2) 1997 — origin of infimal convolution of first/second order
4. **Bredies, Kunisch, Pock, Total Generalized Variation, SIAM J. Imaging Sci. 3(3) 2010** — TGV² admits piecewise-*linear* minimizers
5. Chan, Marquina, Mulet, High-order total variation-based image restoration, SIAM J. Sci. Comput. 22(2) 2000
6. arXiv:1504.01956 — Infimal convolution regularisation functionals of BV and L^p spaces
7. **Kim, Koh, Boyd, Gorinevsky, ℓ₁ Trend Filtering, SIAM Review 51(2) 2009** — second-difference ℓ₁ ⇒ piecewise-linear, O(n)
8. arXiv:1304.2986 — Adaptive piecewise polynomial estimation via trend filtering (Ann. Statist. 2014)
9. arXiv:2003.03886 — Divided Differences, Falling Factorials, and Discrete Splines
10. Wang, Smola, Tibshirani, The Falling Factorial Basis and Its Statistical Applications, ICML 2014 — O(n) primitives
11. Eilers, A perfect smoother, Analytical Chemistry 75(14) 2003 — order-2/3 difference penalty in ~10 lines
12. arXiv:2306.06932 — Whittaker–Henderson smoothing revisited
13. **BIS WP 1033, The Holt-Winters filter and the one-sided HP filter** — second-difference penalty has an exact constant-cost causal Kalman implementation
14. **Wecker & Ansley, The signal extraction approach to nonlinear regression and spline smoothing, JASA 78 1983** (with Kohn & Ansley O(n)) — smoothing splines *are* a state-space model
15. **Weinert, Fast compact algorithms and software for spline smoothing via spectral factorization concepts, Automatica 37 2001** *(title unverified)* — spline smoother collapses to a fixed-coefficient IIR pair
16. Blake & Zisserman, Visual Reconstruction, MIT Press 1987; + Carriero, Leaci, Tomarelli, SIAM J. Math. Anal.
17. arXiv:2211.12785 — Smoothing splines for discontinuous signals — names Blake–Zisserman's "gradient limit" as the first-order failure mode
18. arXiv:1803.06156 — Smoothing for signals with discontinuities using higher order Mumford–Shah models
19. Condat, A direct algorithm for 1-D total variation denoising, IEEE SPL 20(11) 2013 — non-iterative forward-sweeping template
20. Casiez, Roussel, Vogel, 1€ Filter, CHI 2012 — the practical baseline any recursive scheme must beat
21. TI SLYT681 — Bessel vs Butterworth group delay; governs whether a multi-pole smoother pre-rings
22. Lindgren, Rue, Lindström, The SPDE approach, JRSS-B 73(4) 2011 — bi-Laplacian priors as sparse GMRFs

# §8 — Expensive to fit, trivial to evaluate

1. **arXiv:1606.01299 — RAISR** — hash local context to a bucket, apply that bucket's precomputed linear filter
2. **arXiv:2302.03213 — LUT-NN** — learned operators as centroid lookup into precomputed tables
3. arXiv:2303.01469 — Consistency Models — generative trajectory collapsed to one evaluation
4. arXiv:2505.13447 — Mean Flows for One-step Generative Modeling
5. arXiv:2502.07579 — Single-Step Consistent Diffusion Samplers
6. arXiv:2510.16983 — One-step Diffusion Models with Bregman Density Ratio Matching
7. arXiv:2510.20771 — AlphaFlow: Understanding and Improving MeanFlow Models
8. arXiv:2505.13358 — One-Step Offline Distillation via Koopman Modeling — sampling trajectory as a literal linear operator
9. **arXiv:1611.03537 — Korda & Mezić, Linear predictors for nonlinear dynamical systems** — lifted linear dynamics; runtime is one matvec
10. arXiv:1911.08751 — EDMD with Learned Koopman Eigenfunctions
11. arXiv:2504.11757 — Dynamics and Computational Principles of Echo State Networks
12. arXiv:2403.19806 — Feature-Based Echo-State Networks
13. arXiv:2009.04614 — End-to-end Kernel Learning via Generative Random Fourier Features
14. arXiv:2404.03050 — ANOVA-boosting for Random Fourier Features
15. arXiv:2404.00008 — Best free knot linear spline approximation and its application to neural networks
16. arXiv:2402.11224 — Neural Networks with Low-Precision Polynomial Approximations
17. arXiv:2501.19135 — Tensor-Train Decomposition based Compression
18. arXiv:2411.06346 — Activation Map Compression through Tensor Decomposition
19. arXiv:2303.17478 — A Bayesian Dirichlet Auto-Regressive Moving Average Model for Compositional Time Series — simplex-valid AR, a handful of multiply-adds
20. arXiv:2507.14132 — A Bayesian Dirichlet ARCH Model — simplex-valid variance channel driven by a scalar
21. arXiv:2312.03406 — A Differentiable Sparse Vector Quantization for Spatio-Temporal Forecasting
22. arXiv:2104.05778 — Efficient Space-time Video Super Resolution
23. arXiv:2501.10658 — LUT-DLA: Lookup Table as Efficient Extreme Low-Bit Accelerator
24. arXiv:2412.09726 — The Unreasonable Effectiveness of Gaussian Score Approximation *(ID unverified)* — learned scores track a low-rank Gaussian mixture score
