# Advanced Viseme Reconstructor Runtime Optimization Research

Last updated: 2026-07-16

## Purpose

This report evaluates mathematical and compiler techniques that could reduce the runtime cost of YUCP Advanced Viseme Reconstructor (AVR) without changing source meshes, relying on custom scripts at runtime, or weakening the avatar's ordinary face-tracking response.

The source catalog contains **119 unique primary papers or official engine/platform documents**. Duplicate papers, mirrors, secondary summaries, and search-result pages were removed. A paper being listed is not an endorsement: every technique is classified as accepted for implementation, accepted for a prototype, conditional on an error/cost test, background evidence only, or rejected under the current deployment constraints.

## Current evidence and target

The reference capture that motivated this review reported approximately:

- `Animator.ProcessGraph`: 1.6337 ms attributable to the enabled AVR workload;
- `Animator.ProcessAnimations`: 1.5426 ms attributable to the enabled AVR workload;
- 1,578 recursively referenced clips;
- 459 blend trees;
- 2,978 curve references in the AVR CPU graph;
- 3,598 clips in the fully merged controller.

These values are a local reference, not universal device guarantees. They show that curve sampling and blending dominate, so merely reducing layer count or serialized asset size is insufficient. The engineering target is to remove at least **1.0–1.5 ms** from the reference avatar while keeping visible error small and jaw tracking responsive.

No cited paper can guarantee a Unity/VRChat millisecond saving. Upside estimates below are engineering hypotheses derived from the measured graph size, Unity's documented evaluation model, and the reductions achieved in the cited domains. Every estimate must be accepted or rejected by the profiler protocol in this report.

### Implemented result (2026-07-16)

The accepted exact transforms were implemented and replayed before considering an approximate default:

- commute the shared causal observer through demanded static articulation projections;
- replace each dense retained-context projection with a selected transition row followed by the same EMA, retaining an explicit one-frame stage delay so Animator feedback timing is unchanged;
- prune provably inactive articulator lanes and output-only intermediates by mouth-ownership and calibrated-output topology;
- canonicalize collinear `Simple1D` knots and fold constant siblings with the same Direct-tree weight;
- preserve the complete public parameter contract as zero-default declarations when a lane is not generated.

The standalone numerical screen replayed **2,154** random and adversarial traces containing **223,802 frames** at 15, 30, 60, 90, and 144 FPS. A four-term event/Volterra approximation was rejected because its adversarial maximum error reached `0.2404`; flat low-rank-plus-sparse decompositions were rejected because their lowered Animator cost did not beat the exact realization. The stage-preserving exact realization matched the prior generated Unity trace with maximum absolute difference `1.48e-7` and RMS difference `2.99e-8`.

After a full 2-4 minute VRCFury/Gesture Manager rebuild of the reference avatar, a fresh 120-frame enabled capture reported:

- `Animator.ProcessGraph`: `2.6020 ms`;
- `Animator.ProcessAnimations`: `2.2450 ms`.

The paired ablation set only **2,103 AVR-provenance clip inputs** to zero, retained the one FT/AVR-fused clip, and left the rest of the avatar graph running. Its 120-frame capture reported `2.3342 ms` and `1.9495 ms`, respectively. The current conservative AVR-only delta is therefore:

- `ProcessGraph`: **0.2678 ms**;
- `ProcessAnimations`: **0.2955 ms**;
- combined: **0.5633 ms**.

Relative to the earlier enabled capture of the same reference harness (`3.4954 ms` graph and `3.2368 ms` animations), the two enabled markers fell by **1.8852 ms combined**. Editor captures are not device guarantees, and the ablation delta is a lower bound because 273 bindings in the shared FT/AVR clip remained enabled. No source mesh, calibrated mesh, blendshape frame, normal, or tangent was modified by this optimization.

### Exact liveness continuation (2026-07-17)

The lowered Animator now receives a second, epoch-preserving closed-world pass. It traces every physical curve, public parameter, state transition, parameter driver, state-time control, and private feedback dependency backward through BlendTree weights. It removes only private AAP curves outside that observable cone; it never composes, reorders, or shortens an Animator feedback stage. Empty non-normalized Direct branches are then pruned and nested state machines are traversed recursively.

On the representative generated Beta/Balanced8 fixture, closed-world liveness removed **52 private parameters** and **147 Animator curves**. A second exact rule removes 304 constant-zero bindings from the nonnegative Beta-retention observer only. Unity's runtime test proves that missing bindings contribute neutral zero inside `Simple1D` and non-normalized Direct trees; the optimizer additionally requires every reachable use to stay in that proven topology, keeps every child/knot, and requires a nonzero writer for the same property in the state. Signed and affine articulation cones are explicitly excluded after the broad form failed replay. Together the two accepted passes remove **451 of 4,958 Animator curves** (about 9.1%), leaving 4,507, while the mixed 15/60/144 FPS generated-controller replay remains within its `2e-5` to `3e-5` staging tolerances.

The same Unity session then ran two 120-frame post-generation A/B captures with four hidden standalone AVR Animators to amplify the signal. The full graph's two principal Animator markers averaged **6.7142 ms** after the pass, versus **7.1063 ms** before it: a **0.3921 ms** aggregate reduction, or about **0.0980 ms per AVR instance** in this Editor harness (**5.5%**). Isolating the Math layer averaged **4.4586 ms** after versus **5.1299 ms** before: **0.6713 ms** aggregate, about **0.1678 ms per instance** (**13.1%**). Property writes remained about `0.012 ms` for all four instances. `AnimationClipPlayable.EvaluateClip` still occurred 250,080 times over the full 120-frame capture, confirming that this pass makes sampled clips narrower but does not remove their active evaluations. The result is accepted as an exact incremental improvement, but it does **not** satisfy the 1.0-1.5 ms target. A paired capture of the fully merged VRCFury controller remains required before treating these Editor numbers as an in-game saving.

A build-time reduction certificate now prevents an approximate Beta model from being selected merely because it is small on disk. A candidate must lower estimated active bindings, preserve Animator epochs/endpoints/mandatory constraints, pass held-out replay limits (`RMS <= 0.01`, `p99 <= 0.025`, `max <= 0.05`, velocity `RMS <= 0.02`), and satisfy the universal simplex bound

```text
abs(c^T (R-Rhat) f) <= max_ij abs(R_ij-Rhat_ij).
```

The first shared nonnegative-CP student was deliberately rejected. With structured group-sparse rank-one patches and a strict 240-binding cap, the best tested candidate (`H=12`) still produced `0.06261` RMS, `0.18103` p99, and `0.34694` maximum retention error across the 223,802-frame replay. The exact commuted teacher therefore remains the generated default.

### Operation-local neutral realization (2026-07-18)

The neutral-zero pass now also recognizes an exact operation-local case outside the specialized Beta observer. A removable binding must target an AVR-private Float with a bitwise positive-zero default; its curve must be one flat positive-zero key at time zero; and a nonzero clip for the same property must be a direct sibling at the same `Simple1D` or unnormalized `Direct` BlendTree site. Every occurrence of a shared clip must pass, synced layers and external clips are rejected, and the folded root binder remains intact. This is a reachable-state, closed-world optimization: externally injecting arbitrary values into `_Internal` parameters is outside the contract and intentionally fails the adversarial sentinel replay.

In the final VRCFury-merged Balanced8/Beta fixture, the pass reduced the controller from **5,047 to 4,453 Animator curves** and from **2,336 to 2,248 clips**, with the same 667 BlendTrees and 1,147 parameters. Paired manual evaluation at 90 FPS measured:

- local active: `2.789522 ms` to `2.526170 ms`, a **0.263352 ms** difference between medians;
- remote active: `2.856343 ms` to `2.530743 ms`, a **0.325600 ms** difference between medians;
- local idle: a **0.269136 ms** median saving;
- remote idle: a **0.270559 ms** median saving.

The paired merged-controller replay compared 1,080 shared Float parameters and produced **zero bit mismatches** for local and remote streams at 15, 24, 25, 30, 45, 60, 90, and 144 FPS. The optimizer model version was advanced so stable generated-asset hashes rebuild existing avatars automatically. These Editor measurements are directional rather than headset guarantees, but they isolate the final merged Animator graph and preserve all reachable tested outputs exactly.

## Non-negotiable deployment constraints

1. The shipping result must be an ordinary VRChat avatar Animator generated through supported VRCFury APIs.
2. Custom `MonoBehaviour`, `IAnimationJob`, Burst job, custom Playable, native plugin, neural runtime, or companion application is unavailable on the uploaded avatar.
3. Source meshes and authored blendshapes must remain untouched. Mesh compression, residual-shape rewriting, skinning decomposition, and custom GPU deformation are out of scope.
4. Raw face-tracking jaw and mouth motion must not wait behind speech hangover or a slow observer.
5. Exact modes must reproduce the current generated parameter/rig result within floating-point tolerance. Approximate modes require an explicit error budget and replay certificate.
6. VRCFury Direct Tree optimization must not silently flatten away a layer whose zero weight is being used as a runtime computation gate.
7. Public parameters that can be consumed dynamically by OSC or an unknown controller cannot be removed without an explicit contract or creator opt-in.

## Decision labels

| Label | Meaning |
|---|---|
| **Accept** | Exact or platform-supported change suitable for the default implementation after regression tests. |
| **Prototype** | Strong candidate, but Animator lowering or measured benefit is not yet proven. |
| **Conditional** | Use only when build-time analysis proves an error/cost threshold for this avatar. |
| **Background** | Supports the compiler or measurement strategy but is not itself a shipping transform. |
| **Reject** | Requires scripts, mesh changes, shaders, external decoding, or adds more Animator work than it removes. |

## Technique decision matrix

The expected upside ranges are deliberately conservative and are not additive; several techniques eliminate the same work.

| Technique | Mathematical/engineering operation | AVR applicability | Deployment constraints | Expected upside on the reference graph | Decision |
|---|---|---|---|---:|---|
| Integer-domain vector supernode | Evaluate all static functions of hard `Viseme` over its 15 reachable integers and emit one multi-output lookup clip bank | Replaces repeated one-hot spikes and downstream constant maps; exact on reachable inputs | The blend parameter must retain integer semantics; unreachable fractional values are explicitly outside the equivalence domain | 0.6–1.2 ms | **Accept** |
| Projection before observation | For a common scalar LTI filter `H`, use `M(Hu)=H(Mu)` and filter demanded pose statistics directly | Avoids reconstructing all 15 weights before the articulator projection | Nonlinear PP/FF/SS/CH gates and externally consumed viseme weights must be included in the demanded statistics | 0.4–1.0 ms alone; 1.2–2.0 ms with the vector supernode | **Accept** |
| Closed-world liveness and observability don't-cares | Trace actual rig writes and controller reads backward; remove computations that cannot affect them | Removes unused velocities, diagnostics, beta branches, and public weights | Unknown OSC/dynamic consumers must remain roots or require creator opt-in | 0–1.6 ms, workload dependent | **Accept** with conservative roots |
| Constraint-aware equality saturation | Apply exact affine, Boolean, simplex, integer-domain, and mode rewrites, then extract using a Unity-specific curve cost | Finds nonlocal fusions missed by greedy batching | Extraction cost must count active clips and curve bindings, not just nodes; rewrites require equivalence tests | 0.3–1.0 ms after existing batching | **Prototype** |
| Zero-weight mode specialization | Put speech, beta, and diagnostic work on independent non-base layers and drive inactive weights to zero | Unity documents that a zero-weight layer update is skipped | Keep raw tracking on an always-on path; initialize observer state on activation; prevent VRCFury flattening | 1.5–2.8 ms in silent/tracker-only frames; much less while speaking | **Accept** after post-merge validation |
| Output-sensitive exact realization | Form the minimal state needed for the currently demanded output map rather than all public channels | Can reduce observer dimension exactly when the demanded output rank is below 15 | No benefit if all 15 viseme weights remain live; generated realization must lower to fewer active curves | 0–1.2 ms | **Prototype** |
| Balanced, Hankel, Loewner, or rational model reduction | Fit fewer stable poles/states with certified finite-, frequency-, or output-weighted error | May collapse observer and tracking-confidence dynamics jointly | Static full rank does not imply useful dynamic reduction, but a low-order fit is accepted only if Animator curve cost falls and replay bounds pass | 0.3–1.0 ms | **Conditional** |
| Weighted-automaton minimization | Treat hard visemes as a finite alphabet and factor the whole input/output Hankel behavior | Captures shared history across modes instead of reducing each scalar channel independently | Continuous `Voice`/tracking inputs require a hybrid partition; compiled state count must remain below the original curve cost | 0.4–1.2 ms | **Prototype** |
| K-sparse simplex/native pose bank | Keep the largest `K` viseme weights or approximate the observer by current/interrupted/next native pose transitions | Could replace dense per-channel observer evaluation with two or three active pose clips | Approximate; pathological rapid phoneme cycles may spread mass across many visemes; build must certify error | 1.2–2.5 ms while speaking | **Conditional experimental mode** |
| Shared-knot vector PWL atlas | Fit many maps of the same input with one minimax/DP-selected knot set and multi-output clips | Extends current exact map batching when union breakpoints are too expensive | Perceptual and mandatory-closure errors need channel-specific bounds | 0.2–0.8 ms | **Conditional** |
| Native layer confidence fusion | Put speech below tracker override layers; use a few reliability groups and native layer-weight fades | Can replace repeated per-channel interpolation/smoothing trees | Layer weights are not arbitrary per-frame parameters; reliability quantization and fade-shape error must be bounded | 0.3–1.0 ms | **Prototype** |
| Event-triggered/multirate slow path | Update coarticulation, diagnostics, and slow confidence logic only on viseme/mode events or a lower-rate clock | Preserves a fast raw jaw path while reducing slow-path duty cycle | Animator sample-and-hold and accumulated-time semantics are difficult; built-in parameters cannot generally be copied by Parameter Driver | 0.4–1.5 ms average, highly workload dependent | **Prototype** |
| Tensor/CUR/TT factorization | Factor stage × mode × viseme × channel coefficient tensors, favoring actual authored atoms | CP/full-matrix rank failure does not rule out hierarchical or output-sensitive separability | A smaller serialized factorization is rejected if its decoder increases active curves | 0–1.0 ms | **Conditional** |
| Quantization to induce CSE | Round coefficients under sensitivity bounds so more clips/maps become identical and shareable | Useful after exact graph reduction; also helps synced settings | Quantization by itself mostly saves storage/network bits; accept only when it reduces sampled curves | 0.1–0.7 ms | **Conditional** |
| Empirical graph autotuning | Generate several equivalent layouts and benchmark the real Unity markers | Avoids assuming that node count predicts Mecanim cost | Must use a bounded CLI-driven test, preserve deterministic assets, and avoid hour-long runs | Enables other savings; no direct standalone saving | **Accept** as build/test infrastructure |
| SIMD/jobs/custom Playables | Evaluate the observer as a packed native/job kernel | Technically ideal in an unrestricted Unity application | VRChat removes custom scripts/jobs and accepts only ordinary avatar controllers | Potentially large but undeployable | **Reject** |
| GPU/mesh/skinning compression | Change blendshape/skinning representation or deform on the GPU | Can reduce deformation cost in other engines | Violates the no-mesh-change rule and duplicates VRCFury/engine mesh work | Potentially large but out of scope | **Reject** |
| Neural/SSM runtime decoder | Distill controller behavior into an S4/S5/neural state-space model | Valuable as an offline analysis oracle | No shippable inference runtime; an Animator implementation is accepted only if converted to a smaller certified linear graph | Unknown | **Reject as runtime; Background offline** |

## Recommended compiler architecture

### Stage 1: semantic IR and demand roots

Represent generated math before creating clips. Every node should record:

- input domain: Boolean, hard integer, bounded float, signed float, or simplex coordinate;
- affine/PWL/nonlinear classification;
- update regime: always, local-only, tracker-active, talking, beta-only, or diagnostics-only;
- visible and public consumers;
- error policy: exact, perceptual tolerance, or diagnostic tolerance;
- estimated Unity cost: active child clips, curve bindings, mutable parameters, and layers.

Roots are actual rig writes, parameters read by merged controllers/Blendshape Link, networking outputs, and explicitly requested public diagnostics. Unknown dynamic consumers remain roots.

### Stage 2: exact domain compilation

`Viseme` is one of fifteen integers. For every purely static subgraph `F(Viseme)`, evaluate `F(0)` through `F(14)` at build time and emit one vector-valued clip bank. This replaces the repeated three-point equality spikes currently needed to create one-hot values.

The equivalence claim is restricted to the reachable integer domain. A test must compare every generated output for all fifteen inputs before the old graph is removed.

### Stage 3: commute common filters through static maps

For the two-pole observer, let `H` be the common scalar filter applied to each viseme and `M` be the demanded linear output map:

```text
p = H u
y = M p = M(Hu) = H(Mu)
```

Bake the fifteen columns of `M` into the vector lookup and filter `y` directly. Add every linear statistic required by later nonlinear constraints to `y`. If a public viseme weight is genuinely consumed, keep that coordinate; otherwise do not reconstruct it merely because it exists in the full parameter contract.

### Stage 4: mode partition and zero-cost inactive regimes

Use a cheap control layer to manage separate computation layers:

1. always-on raw local/remote tracker passthrough;
2. speech observer and phonetic constraints;
3. beta coarticulation/tongue inference;
4. optional diagnostics and velocities.

The speech layer remains active during the configured hangover, then reaches exactly zero. Beta and diagnostic layers remain zero unless requested. The base layer cannot be controlled by `VRCAnimatorLayerControl`, so gateable work must not be placed there.

Activation must warm-start from the current hard viseme and tracking state. Post-VRCFury validation must assert that the independent layers and their controls survived controller merging.

### Stage 5: cost-aware exact extraction

Apply verified rewrite rules such as constant folding, affine composition, interpolation fusion, selector distribution, repeated-map sharing, simplex identities, unreachable-domain deletion, and shared multi-output clip packing. Use equality saturation or a bounded alternative to avoid rewrite-order dependence.

The extraction objective should be fitted to measured Unity cost:

```text
cost = a * activeClipSamples
     + b * activeCurveBindings
     + c * ProcessAnimationWrites
     + d * stateMachineWork
     + e * layerOverhead
```

The coefficients are obtained from small generated-controller calibration experiments on the pinned Unity version. Serialized node count is only a tie-breaker.

### Stage 6: certified approximate candidates

Only after the exact compiler is measured should the build evaluate balanced truncation, ERA/Ho-Kalman, Loewner/AAA/RKFIT, WFA minimization, shared-knot PWL, TT/Tucker/CUR, or K-sparse pose-bank candidates. A candidate is retained only when:

1. it lowers the predicted and measured active curve cost;
2. poles remain stable and fades remain monotone where required;
3. mandatory PP/FF/SS/CH constraints pass exactly or within their stricter bound;
4. the maximum replay error passes, not merely average error;
5. jaw/mouth tracking latency does not regress by more than one rendered frame.

## Error bounds worth implementing

### Sparse simplex pose error

Let `p` be a nonnegative viseme simplex, retain a set with mass `1-d`, and renormalize it to `pHat`. For any scalar authored pose channel with values in `[aMin,aMax]`:

```text
abs(dot(a,pHat) - dot(a,p)) <= d * (aMax - aMin)
```

This gives a direct avatar-specific build bound for top-K truncation. Vector/mesh-visible bounds can use the maximum channel range or a calibrated perceptual norm. The method must be rejected on a profile whose rapid-speech corpus produces excessive discarded mass.

### Reliability quantization

For `z = (1-g)s + g f`, quantizing `g` to spacing `deltaG` gives:

```text
abs(zHat-z) <= 0.5 * deltaG * abs(f-s)
```

This supports a small number of native layer reliability groups while preserving exact `g=0` and `g=1` endpoints.

### Balanced truncation

Use the relevant finite-horizon, frequency-limited, switched, or bilinear bound rather than an unconstrained global norm. The perceptually relevant input band is speech/facial motion, while the validation horizon should include tracker acquisition/loss and the full speech hangover.

## Profiler and correctness protocol

All runtime candidates must use the Unity CLI loop and the designated project. No computer-use automation is required.

1. Allow the known 2–4 minute Play Mode/domain-reload startup to complete; do not treat startup time as the benchmark.
2. Warm the running controller for a fixed interval, then capture equal-duration windows for baseline and candidate.
3. Record median and p95 `Animator.ProcessGraph`, `Animator.ProcessAnimation`, state-machine update, total frame time, active clips, and curve writes.
4. Run at least these regimes: silent/no tracker, tracker idle, continuous jaw sweep, ordinary speech, rapid viseme interruption, quiet speech around hangover, tracker acquisition/loss, beta on/off, local and remote.
5. Replay identical parameter traces at 15, 30, 60, 90, 120, and 144 FPS.
6. Exact transforms must match every public and visible output within `1e-5` in the deterministic harness.
7. Approximate transforms must report maximum, p95, and RMS error per output family; averages alone are insufficient.
8. Accept a performance change only if the reference configuration improves by at least 1.0 ms combined and no representative regime regresses materially.
9. Verify generated-asset hashes, source-asset preservation, VRCFury merge behavior, and rebuild idempotence.

## Source catalog

The catalog is grouped by the technique it informs. `Expected role` means the evidence's likely contribution to AVR, not a performance claim made by that source.

### Official Unity, VRChat, and VRCFury deployment evidence (17)

| ID | Primary/official source | Technique and AVR applicability | Decision |
|---|---|---|---|
| U01 | Unity, [Mecanim Performance and Optimization](https://docs.unity3d.com/Manual/MecanimPeformanceandOptimization.html) | Confirms that played animations/blend trees dominate and a zero-weight layer is skipped. Direct evidence for mode-gated layers and constant-curve cleanup. | **Accept** |
| U02 | Unity 2022.3, [Common Profiler Markers / Animation markers](https://docs.unity3d.com/2022.3/Documentation/Manual/profiler-markers.html#animation-markers) | Defines `Animators.ProcessGraph` as evaluating properties across connected clips and `Animator.ProcessAnimations` as blending active-clip properties; it also states that `OnStateMachineEnter`/`OnStateMachineExit` listeners constrain state-machine evaluation to the main thread. Establishes the profiler markers and callback audit. | **Accept** as measurement basis |
| U03 | Unity, [Blend Trees](https://docs.unity3d.com/Manual/class-BlendTree.html) | Defines the native evaluator available to uploaded avatars. Supports vector lookup banks and native interpolation but supplies no arbitrary custom operator. | **Background** |
| U04 | Unity, [Animation Layers](https://docs.unity3d.com/Manual/AnimationLayers.html) | Defines override/additive weighting used by the proposed tracker/speech layer fusion and inactive-layer partition. | **Accept** |
| U05 | Unity, [Animation Clip import and compression](https://docs.unity.cn/Manual/class-AnimationClip.html) | Shows that key reduction and dense/stream choices affect runtime memory and curve evaluation. Useful for generated-clip representation, but clip compression alone does not remove graph work. | **Conditional** |
| U06 | Unity, [PlayableGraph](https://docs.unity3d.com/ScriptReference/Playables.PlayableGraph.html) | Provides the conceptual graph-evaluation model used to reason about active inputs and topology. Custom graphs cannot replace the avatar Animator in VRChat. | **Background** |
| U07 | Unity, [AnimationScriptPlayable](https://docs.unity3d.com/ScriptReference/Animations.AnimationScriptPlayable.html) | Demonstrates that a custom packed animation kernel would be possible in an unrestricted Unity application. | **Reject** for shipping avatar |
| U08 | Unity, [AnimationScriptPlayable.SetProcessInputs](https://docs.unity3d.com/ScriptReference/Animations.AnimationScriptPlayable.SetProcessInputs.html) | Direct evidence that not processing inactive inputs can save graph work; motivates ordinary-Animator layer gating. The API itself is unavailable after upload. | **Background; Reject API** |
| U09 | Unity, [IAnimationJob](https://docs.unity3d.com/ScriptReference/Animations.IAnimationJob.html) | SIMD/Burst-friendly route for the observer in a normal Unity application. VRChat cannot retain the required script/job. | **Reject** |
| U10 | VRChat, [Allowed Avatar Components](https://creators.vrchat.com/avatars/whitelisted-avatar-components/whitelisted-avatar-components/) | Authoritative reason custom scripts, jobs, plugins, and arbitrary decoders cannot be part of the shipping optimization. | **Constraint** |
| U11 | VRChat, [Playable Layers](https://creators.vrchat.com/avatars/playable-layers/) | Confirms playable controllers are combined into one Animator and only Animation Controllers are supported. Requires evaluating AVR after merge, not in isolation. | **Constraint** |
| U12 | VRChat, [Animator Parameters](https://creators.vrchat.com/avatars/animator-parameters/) | Defines hard `Viseme`, `Voice`, `IsLocal`, types, and synchronization. The 15-value integer domain enables exact finite-domain compilation. | **Accept** |
| U13 | VRChat, [State Behaviors](https://creators.vrchat.com/avatars/state-behaviors/) | `VRCAnimatorLayerControl` can fade and retain a non-base layer's weight. Provides the supported mechanism for zero-weight gates and coarse native confidence fusion. | **Accept** |
| U14 | VRChat, [Avatar Performance Ranking](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/) | Explains why a statically acceptable avatar can still have expensive Animator evaluation; runtime profiler evidence remains necessary. | **Background** |
| U15 | VRCFury, [Direct Tree Optimizer](https://vrcfury.com/components/other/) | Existing baseline that merges nonconflicting layers. Generated gates must be protected and checked after this pass; ordinary layer-count reduction is already largely covered. | **Accept with merge audit** |
| U16 | VRCFury, [Write Defaults and Direct Trees](https://vrcfury.com/technical/wd/) | Establishes correctness hazards when lowering to Direct/Additive trees. Exact optimization cannot change authored reset/retention semantics. | **Constraint** |
| U17 | VRCFury, [Full Controller](https://vrcfury.com/components/full-controller/) | Public supported injection path for the optimized generated controller and global parameters. | **Accept** |

### Exact graph compilation, liveness, and automata (32)

| ID | Primary source | Technique and AVR applicability | Decision |
|---|---|---|---|
| C01 | Tate et al., [Equality Saturation: A New Approach to Optimization](https://www.cs.cornell.edu/~lerner/papers/popl09.html) (2009) | Apply exact rewrites without destructive ordering, then globally extract the lowest measured Animator-cost expression. | **Prototype** |
| C02 | Willsey et al., [egg: Fast and Extensible Equality Saturation](https://doi.org/10.1145/3434304) (2021) | E-class analyses can carry AVR ranges, signedness, reachable viseme values, output bindings, and exactness policies during rewrite search. | **Prototype** |
| C03 | Wang et al., [SPORES: Sum-Product Optimization via Relational Equality Saturation](https://doi.org/10.14778/3407790.3407799) (2020) | Jointly finds factorization, sparsity, and common-subexpression rewrites in sum-product expressions analogous to viseme-to-articulator projection. | **Prototype** |
| C04 | Yang et al., [TENSAT: Equality Saturation for Tensor Graph Superoptimization](https://proceedings.mlsys.org/paper_files/paper/2021/file/cc427d934a7f6c0663e5923f49eba531-Paper.pdf) (2021) | Supports nonlocal graph rewrites and cost-based/ILP extraction. AVR's cost must model sampled clips and curves rather than tensor FLOPs. | **Prototype** |
| C05 | Smith et al., [Pure Tensor Program Rewriting via Access Patterns](https://doi.org/10.1145/3460945.3464953) (2021) | A pure access-pattern IR suggests representing Animator property streams independently of their eventual clip/tree layout, exposing vector packing. | **Background** |
| C06 | VanHattum et al., [Vectorization for Digital Signal Processors via Equality Saturation](https://doi.org/10.1145/3445814.3446707) (2021) | Demonstrates symbolic evaluation plus equality saturation for irregular lane packing; supports multi-output clip supernodes when Unity measurements favor them. | **Prototype** |
| C07 | Zhang et al., [Better Together: Unifying Datalog and Equality Saturation](https://arxiv.org/abs/2304.04332) (2023) | Dataflow/lattice analyses alongside equality saturation are a strong model for demand, binding-union, and range facts. | **Prototype** |
| C08 | [Optimizing Tensor Computation Graphs with Equality Saturation and Monte Carlo Tree Search](https://arxiv.org/abs/2410.05534) (2024) | Guided scheduling is relevant only if AVR's bounded rewrite set causes e-graph blowup. | **Conditional** |
| C09 | Schkufza, Sharma, and Aiken, [Stochastic Superoptimization](https://arxiv.org/abs/1211.0557) (2013) | A correctness-plus-cost search may find compact straight-line motifs missed by manual rules, but every result needs deterministic validation. | **Background** |
| C10 | Sasnauskas et al., [Souper: A Synthesizing Superoptimizer](https://arxiv.org/abs/1711.04422) (2017) | SMT-backed synthesis is suitable for small scalar/vector AVR motifs after domain constraints are declared. | **Prototype** |
| C11 | Lopes et al., [Alive2: Bounded Translation Validation for LLVM](https://doi.org/10.1145/3453483.3454030) (2021) | Provides the model for automatically proving each old/new AVR IR graph equivalent over its bounded domains. | **Accept** as validation design |
| C12 | Panchekha et al., [Automatically Improving Accuracy for Floating Point Expressions](https://pldi15.sigplan.org/details/pldi2015-papers/5/Automatically-Improving-Accuracy-for-Floating-Point-Expressions) (2015) | Exact-looking algebraic alternatives can have different floating error. Extraction needs an error objective and numerical regression, not operation count alone. | **Accept** as correctness constraint |
| C13 | Cong, Liu, and Zhang, [Behavior-Level Observability Don't-Cares and Application to Low-Power Behavioral Synthesis](https://llvm.org/pubs/2009-08-ISLPED.pdf) (2009) | Backward observability on a dataflow graph justifies removing internal values that cannot affect public or rig sinks. | **Accept** |
| C14 | Mishchenko and Brayton, [SAT-Based Complete Don't-Care Computation for Network Optimization](https://arxiv.org/abs/0710.4695) (2008) | Complete care-set reasoning is stronger than local dead-code elimination when branches reconverge. | **Prototype** |
| C15 | Mishchenko et al., [Scalable Don't-Care-Based Logic Optimization and Resynthesis](https://doi.org/10.1145/1508128.1508152) (2009) | Supports bounded local resynthesis under explicit care sets, suitable for generated AVR subgraphs. | **Prototype** |
| C16 | Arts, Berkelaar, and van Eijk, [Computing Observability Don't Cares Efficiently Through Polarization](https://doi.org/10.1109/43.709395) (1998) | Exact propagation through reconvergent fanout maps to multiple articulation paths that later rejoin one output. | **Accept** conceptually |
| C17 | Marakkalage et al., [Scalable Sequential Optimization Under Observability Don't Cares](https://arxiv.org/abs/2311.09967) (2024) | Extends observability reasoning to stateful logic; relevant to observer lanes whose state is invisible in some modes. | **Prototype** |
| C18 | Hopcroft, [An n log n Algorithm for Minimizing States in a Finite Automaton](https://i.stanford.edu/TR/CS-TR-71-190.html) (1971) | Partition refinement merges behaviorally equivalent decoder/control states after unreachable states are removed. | **Accept** |
| C19 | Valmari, [Fast Brief Practical DFA Minimization](https://doi.org/10.1016/j.ipl.2011.12.004) (2012) | Practical state minimization including reachability removal for generated Animator state machines. | **Accept** |
| C20 | Kiefer et al., [On the Complexity of Equivalence and Minimisation for Q-weighted Automata](https://doi.org/10.2168/LMCS-9(1:8)2013) (2013) | Rational weighted-automaton equivalence is a close formal analogue to affine state/BlendTree behavior. | **Prototype** |
| C21 | Ésik and Maletti, [Notes on Equivalence and Minimization of Weighted Automata](https://arxiv.org/abs/2009.01217) (2020) | Linear-algebraic weighted-state contraction informs exact output-sensitive realization. | **Prototype** |
| C22 | Bryant, [Graph-Based Algorithms for Boolean Function Manipulation](https://doi.org/10.1109/TC.1986.1676819) (1986) | ROBDD reduction merges isomorphic decisions and removes redundant tests; useful for shared mode/selector DAGs. | **Accept** for Boolean subgraphs |
| C23 | Bryant, [Chain Reduction for Binary and Zero-Suppressed Decision Diagrams](https://arxiv.org/abs/1710.06500) (2018) | Compresses repeated decision chains; potentially useful for nested binary tracking and phonetic gates. | **Conditional** |
| C24 | Jia et al., [TASO: Optimizing Deep Learning Computation with Automatic Generation of Graph Substitutions](https://doi.org/10.1145/3341301.3359630) (2019) | Automatically generates and verifies graph substitutions, then performs cost-guided search. AVR can use the method without adopting a neural runtime. | **Prototype** |
| C25 | Goharshady, Lam, and Parreaux, [Fast and Optimal Extraction for Sparse Equality Graphs](https://doi.org/10.1145/3689801) (2024) | AVR's generated arithmetic graph is sparse enough that optimal low-treewidth extraction may be practical at build time. | **Prototype** |
| C26 | Coward, Drane, and Constantinides, [ROVER: RTL Optimization via Verified E-Graph Rewriting](https://arxiv.org/abs/2406.12421) (2024) | Verified mixed-precision arithmetic rewrites reinforce the need for proof-producing or replay-validated AVR transforms. | **Background** |
| C27 | Coward et al., [Constraint-Aware E-Graph Rewriting for Hardware Performance Optimization](https://doi.org/10.1109/TCAD.2024.3483096) (2024) | Value ranges and control constraints expose optimizations unavailable to unconstrained algebra. This directly supports finite `Viseme`-domain compilation. | **Accept** as compiler principle |
| C28 | Barthels, Psarras, and Bientinesi, [Linnea: Automatic Generation of Efficient Linear Algebra Programs](https://doi.org/10.1145/3446632) (2021) | Best-first search over algebraic evaluation orders can choose between direct-output filtering and a factored basis using a Unity cost model. | **Prototype** |
| C29 | Bilgili and Yurdakul, [Common Subexpression-based Compression and Multiplication of Sparse Constant Matrices](https://arxiv.org/abs/2303.16106) (2023) | Cross-column CSE may reduce the static projection, but is retained only when it reduces active Animator curves rather than adding intermediates. | **Conditional** |
| C30 | Wegman and Zadeck, [Constant Propagation with Conditional Branches](https://doi.org/10.1145/103135.103136) (1991) | Foundation for constant folding, unreachable-mode deletion, and liveness specialization in the generated IR. | **Accept** |
| C31 | Larsen and Amarasinghe, [Exploiting Superword Level Parallelism with Multimedia Instruction Sets](https://doi.org/10.1145/358438.349320) (2000) | Supports grouping identical channel operations into vector clips; Unity, not AVR code, ultimately performs the native packed work. | **Background** |
| C32 | Püschel et al., [SPIRAL: Code Generation for DSP Transforms](https://doi.org/10.1109/JPROC.2004.840306) (2005) | Algebraic search plus empirical platform tuning is the closest model for selecting among equivalent generated Animator layouts. | **Accept** as architecture |

### Dynamic realization, tensor structure, sparsity, and certification (55)

| ID | Primary source | Technique and AVR applicability | Decision |
|---|---|---|---|
| M01 | Moore, [Principal Component Analysis in Linear Systems: Controllability, Observability, and Model Reduction](https://doi.org/10.1109/TAC.1981.1102568) (1981) | Classical balanced truncation. Compute observer Hankel singular values after demand pruning; a small certified tail would justify fewer states. | **Prototype** |
| M02 | Glover, [All Optimal Hankel-Norm Approximations of Linear Multivariable Systems](https://doi.org/10.1080/00207178408933239) (1984) | Optimal causal Hankel approximation and error bounds for a shared observer realization. | **Conditional** |
| M03 | Gugercin, Antoulas, and Beattie, [H2 Model Reduction for Large-Scale Linear Dynamical Systems](https://doi.org/10.1137/060666123) (2008) | IRKA can fit common stable poles across outputs; useful if the resulting controller has fewer sampled stages. | **Prototype** |
| M04 | [Balanced Truncation for Nuclear Hankel Operators](https://doi.org/10.1137/110846981) (2014) | Supplies an `H-infinity`-style tail certificate for approximation, stronger than mean replay error. | **Conditional** |
| M05 | [H2-Optimal Model Reduction via Projected Nonlinear Least Squares](https://doi.org/10.1137/19M1247863) (2020) | Jointly optimizes a rational observer rather than reducing each scalar pole independently. | **Conditional** |
| M06 | [Frequency-Limited Balanced Truncation](https://doi.org/10.1137/15M1030911) (2016) | Weight reduction error toward the facial/speech band instead of irrelevant high frequencies. | **Conditional** |
| M07 | [An Output Error Bound for Time-Limited Balanced Truncation](https://doi.org/10.1016/j.sysconle.2018.08.004) (2018) | Certifies error on the finite horizon relevant to phoneme transitions and tracker fades. | **Conditional** |
| M08 | [Time-Limited Balanced Truncation for Large Systems](https://doi.org/10.1007/s10444-018-9608-6) (2018) | Practical offline finite-horizon reduction for the build-time analysis tool. | **Conditional** |
| M09 | [Type II Balanced Truncation for Deterministic Bilinear Control Systems](https://doi.org/10.1137/17M1147962) (2018) | AVR gates multiply state by tracking/speech confidence, making a bilinear model more faithful than plain LTI reduction. | **Conditional** |
| M10 | [H2-Quasi-Optimal Model Reduction for Quadratic-Bilinear Systems](https://doi.org/10.1137/16M1098280) (2018) | Candidate reduction for multiplicative speech/tracker fusion through low-order Volterra structure. | **Conditional** |
| M11 | [Balanced Truncation of Infinite-Dimensional Bilinear and Stochastic Systems with Explicit Error Bounds](https://doi.org/10.1007/s00498-019-0234-8) (2019) | Supports explicit bounds for gate-dependent observer approximations. | **Background** |
| M12 | [Balanced Truncation of State and Gradient Covariance, CoBRAS](https://doi.org/10.1137/22M1513228) (2023) | Gradient weighting may preserve low-energy modes that strongly affect visible mouth outputs. | **Prototype** |
| M13 | [Balanced Truncation for Linear Switched Systems](https://doi.org/10.1016/j.nahs.2013.03.007) (2013) | Provides mode-specific stable reduction and `L2` bounds for tracking/no-tracking regimes. | **Conditional** |
| M14 | [Balanced Truncation for Linear Switched Systems with Coupled Gramians](https://doi.org/10.1007/s10444-018-9610-z) (2018) | A common basis across AVR modes may reduce duplicated observer states without discontinuities. | **Conditional** |
| M15 | [Time-Varying Gramian Model Reduction for Linear Switched Systems](https://doi.org/10.1016/j.ifacol.2020.12.1580) (2020) | Targets known tracking acquisition/release schedules rather than arbitrary switching. | **Conditional** |
| M16 | [Midpoint-Based Balanced Truncation for Switched Systems](https://doi.org/10.1109/TAC.2023.3269721) (2024) | Relevant to preserving short cross-fade boundary behavior between modes. | **Conditional** |
| M17 | Juang and Pappa, [An Eigensystem Realization Algorithm for Modal Parameter Identification and Model Reduction](https://doi.org/10.2514/3.20031) (1985) | Block-Hankel analysis of the existing controller can reveal its minimum input/output realization from impulse traces. | **Prototype** |
| M18 | [Tangential Interpolation-Based Eigensystem Realization Algorithm for MIMO Systems](https://doi.org/10.1080/13873954.2016.1198389) (2016) | Compresses correlated mouth outputs without requiring all output directions equally. | **Prototype** |
| M19 | [Eigensystem Realization Algorithm Using Randomized SVD](https://doi.org/10.1137/20M1327616) (2021) | Makes large block-Hankel screening tractable with matrix-error control. | **Background** |
| M20 | [Revisiting Ho-Kalman-Based System Identification](https://doi.org/10.1109/TAC.2021.3083651) (2022) | Helps distinguish true causal rank from numerical low rank before claiming a reducible observer. | **Prototype** |
| M21 | Mayo and Antoulas, [A Framework for the Solution of the Generalized Realization Problem](https://doi.org/10.1016/j.laa.2007.03.008) (2007) | The Loewner framework constructs a reduced transfer model directly from response samples. | **Prototype** |
| M22 | Nakatsukasa, Sète, and Trefethen, [The AAA Algorithm for Rational Approximation](https://doi.org/10.1137/16M1106122) (2018) | Discovers shared poles from sampled responses; accept only after stability and worst-case replay certification. | **Prototype** |
| M23 | Berljafa and Güttel, [The RKFIT Algorithm for Nonlinear Rational Approximation](https://doi.org/10.1137/15M1025426) (2017) | Fits a shared denominator across multiple AVR response channels. | **Prototype** |
| M24 | Balle, Carreras, Luque, and Quattoni, [Spectral Learning of Weighted Automata](https://doi.org/10.1007/s10994-013-5416-x) (2014) | Hard visemes form a finite alphabet; behavioral Hankel factorization may share history across phoneme sequences. | **Prototype** |
| M25 | Balle et al., [Singular Value Automata and Approximate Minimization](https://doi.org/10.1017/S0960129519000094) (2019) | Canonical weighted-automaton truncation with bounds is a particularly strong fit for the discrete viseme path. | **Prototype, high priority** |
| M26 | [Optimal Spectral-Norm Approximate Minimization of Weighted Finite Automata](https://doi.org/10.4230/LIPIcs.ICALP.2021.118) (2021) | Supplies optimal one-letter AAK reduction; a constrained 15-symbol extension must be tested rather than assumed. | **Background / research** |
| M27 | De Lathauwer, De Moor, and Vandewalle, [A Multilinear Singular Value Decomposition](https://doi.org/10.1137/S0895479896305696) (2000) | Analyze stage × mode × viseme × output ranks instead of flattening every coefficient into one matrix. | **Conditional** |
| M28 | Grasedyck, [Hierarchical Singular Value Decomposition of Tensors](https://doi.org/10.1137/090764189) (2010) | Hierarchical Tucker structure maps more naturally to nested blend trees than a single global low-rank factorization. | **Conditional** |
| M29 | Oseledets, [Tensor-Train Decomposition](https://doi.org/10.1137/090752286) (2011) | Tensor trains allow different separator ranks; failure of CP or a flat SVD does not reject this structure. | **Conditional** |
| M30 | [Streaming Tensor Train Approximation](https://doi.org/10.1137/22M1515045) (2023) | Efficiently screens a large generated coefficient tensor offline; it does not itself guarantee a cheaper Animator decoder. | **Background** |
| M31 | [Low-Rank Tucker Approximation from Streaming Data](https://doi.org/10.1137/19M1257718) (2020) | Fast randomized screening for multilinear structure before spending build time on an exact factorization. | **Background** |
| M32 | Drineas, Mahoney, and Muthukrishnan, [Relative-Error CUR Matrix Decompositions](https://doi.org/10.1137/07070471X) (2008) | Selects actual viseme/output atoms, which are easier to lower to authored clips than arbitrary dense singular vectors. | **Conditional** |
| M33 | Mahoney, Maggioni, and Drineas, [Tensor-CUR Decompositions](https://doi.org/10.1137/060665336) (2008) | Preserves actual authored subtensors and may yield directly compilable bases. | **Conditional** |
| M34 | Martinsson and Tropp, [Randomized Numerical Linear Algebra: Foundations and Algorithms / Practical Sketching Algorithms for Low-Rank Approximation](https://doi.org/10.1137/17M1111590) (2017) | Stable randomized screening with a priori bounds; useful in the build analyzer, not the runtime. | **Background** |
| M35 | Musco and Musco, [Randomized Block Krylov Methods for Stronger and Faster Approximate Singular Value Decomposition](https://proceedings.neurips.cc/paper_files/paper/2015/hash/1efa39bcaec6f3900149160693694536-Abstract.html) (2015) | Gap-independent low-rank screening for large generated matrices. | **Background** |
| M36 | [Randomized Low-Rank Approximation Beyond Gaussian Random Matrices](https://doi.org/10.1137/23M1593255) (2024) | Sparse/bounded sketch guarantees improve offline analysis cost but do not change Animator deployment. | **Background** |
| M37 | [Perturbation Analysis of CUR Decompositions](https://doi.org/10.1137/19M128394X) (2020) | Tests whether selected authored atoms remain stable under build-time floating-point perturbations. | **Background** |
| M38 | Nakatsukasa, [Fast and Stable Randomized Low-Rank Matrix Approximation](https://arxiv.org/abs/2009.11392) (2020) | Generalized Nyström approximation is useful only if the resulting graph has fewer active curves. | **Conditional** |
| M39 | Aharon, Elad, and Bruckstein, [K-SVD: An Algorithm for Designing Overcomplete Dictionaries for Sparse Representation](https://doi.org/10.1109/TSP.2006.881199) (2006) | Could learn a sparse dictionary of articulation vectors; arbitrary dictionary decoding is rejected unless clip/curve count falls. | **Conditional** |
| M40 | Brunton, Proctor, and Kutz, [Discovering Governing Equations from Data by Sparse Identification of Nonlinear Dynamical Systems](https://doi.org/10.1073/pnas.1517384113) (2016) | SINDy can identify a sparse offline interaction library from recorded AVR traces. It does not replace exact analytical simplification. | **Conditional** |
| M41 | Brunton, Proctor, and Kutz, [Sparse Identification of Nonlinear Dynamics with Control](https://arxiv.org/abs/1605.06682) (2016) | Treats viseme, `Voice`, and tracking as exogenous controls while searching for a sparse behavioral model. | **Conditional** |
| M42 | [Provable Filter Pruning](https://openreview.net/forum?id=BJxkOlSYDH) (2020) | Importance-sampling guarantees inspire sensitivity-guided removal, but neural filters are not the same as Animator curves. | **Background** |
| M43 | [Data-Independent Structured Pruning via Coresets](https://arxiv.org/abs/2008.08316) (2020) | Uniform additive error on a bounded input domain is the relevant standard for pruning generated branches. | **Background** |
| M44 | [Robust Error Bounds for Quantised and Pruned Neural Networks](https://proceedings.mlr.press/v144/li21a.html) (2021) | Semidefinite worst-case certification is a model for bounding an approximate AVR graph over all bounded inputs. | **Accept** as certification reference |
| M45 | Nagel et al., [Up or Down? Adaptive Rounding for Post-Training Quantization, AdaRound](https://proceedings.mlr.press/v119/nagel20a.html) (2020) | Sensitivity-aware coefficient rounding may deliberately create exact duplicate maps and increase CSE. | **Conditional** |
| M46 | Dong et al., [HAWQ-V2: Hessian Aware Trace-Weighted Quantization](https://proceedings.neurips.cc/paper_files/paper/2020/hash/d77c703536718b95308130ff2e5cf9ee-Abstract.html) (2020) | Suggests mixed precision based on visible-output sensitivity rather than uniform tuning precision. | **Conditional** |
| M47 | Xiao et al., [SmoothQuant: Accurate and Efficient Post-Training Quantization for Large Language Models](https://proceedings.mlr.press/v202/xiao23c.html) (2023) | Exact diagonal rescaling can move difficult ranges between stages before coefficient quantization; only useful if it increases sharing. | **Conditional** |
| M48 | Egiazarian et al., [AQLM: Additive Quantization of Language Models](https://proceedings.mlr.press/v235/egiazarian24a.html) (2024) | Shared additive codebooks are compact in memory, but an Animator codebook decoder is accepted only if active curve count decreases. | **Reject by default; Conditional offline** |
| M49 | Tseng et al., [QuIP#: Even Better LLM Quantization with Hadamard Incoherence and Lattice Codebooks](https://proceedings.mlr.press/v235/tseng24a.html) (2024) | Incoherence rotations improve quantization elsewhere, but the inverse transform would normally add more Animator operations. | **Reject** |
| M50 | Gu, Goel, and Ré, [Efficiently Modeling Long Sequences with Structured State Spaces, S4](https://arxiv.org/abs/2111.00396) (2022) | Normal-plus-low-rank state matrices are useful as an offline shared-pole analysis target, not as a neural runtime. | **Background** |
| M51 | Gupta, Gu, and Berant, [Diagonal State Spaces Are as Effective as Structured State Spaces](https://papers.nips.cc/paper_files/paper/2022/hash/9156b0f6dfa9bbd18c79cc459ef5d61c-Abstract-Conference.html) (2022) | A diagonal realization lowers well to independent scalar filters only if an AVR-specific error certificate passes. | **Conditional** |
| M52 | Smith, Warrington, and Linderman, [Simplified State Space Layers for Sequence Modeling, S5](https://openreview.net/forum?id=Ai8Hw3AXqks) (2023) | One MIMO observer replacing many SISO observers is conceptually close to projection-before-observation and output-sensitive realization. | **Prototype** |
| M53 | [Robustifying State-Space Models via Approximate Diagonalization](https://proceedings.iclr.cc/paper_files/paper/2024/hash/8e3b10c517340ea86e37efe088fbca8d-Abstract-Conference.html) (2024) | Backward-stable perturb-then-diagonalize methods inform numerical safeguards for an approximate observer basis. | **Background** |
| M54 | Bick et al., [Transformers to State Space Models: Distilling Quadratic Knowledge to Subquadratic Models, MOHAWK](https://proceedings.neurips.cc/paper_files/paper/2024/hash/3848fef259495bfd04d60cdc5c1b4db7-Abstract-Conference.html) (2024) | Progressive behavioral distillation could generate an offline candidate, but no learned inference runtime can ship on an avatar. | **Reject runtime; Background offline** |
| M55 | [Structured Sparse Transition Matrices for State Tracking](https://openreview.net/forum?id=RDbuSCWhad) (2025) | Provably compact finite-state tracking is a strong lead for a hybrid hard-viseme/history model that still compiles to ordinary states. | **Prototype, high priority** |

### Animation runtime, temporal sparsity, PWL approximation, and rejected deformation paths (15)

| ID | Primary/official source | Technique and AVR applicability | Decision |
|---|---|---|---|
| A01 | Reach and North, [The Signals and Systems Approach to Animation](https://arxiv.org/abs/1703.00521) (2017) | Treats interruption-safe animation as filtering. Directly supports observer commutation, finite impulse analysis, and objective transition-error tests. | **Accept** as mathematical basis |
| A02 | Bollo, [Inertialization: High-Performance Animation Transitions in Gears of War](https://www.gdcvault.com/play/1025165/Inertialization) (2018) | Shows why evaluating both source and target during a transition is expensive and motivates a sparse native pose-transition prototype. Unity's avatar Animator does not expose the same postprocess. | **Prototype concept** |
| A03 | Kyrillidis et al., [Sparse Projections onto the Simplex](https://proceedings.mlr.press/v28/kyrillidis13.html) (2013) | Provides efficient top-K nonnegative normalized projection, the mathematical core of bounded sparse viseme weights. | **Conditional** |
| A04 | Heemels, Johansson, and Tabuada, [An Introduction to Event-Triggered and Self-Triggered Control](https://kth.diva-portal.org/smash/get/diva2%3A586391/FULLTEXT02) (2012) | Supports computing slow branches only when the system needs attention. Animator sample-and-hold limitations make this a prototype rather than a default change. | **Prototype** |
| A05 | Vaidyanathan, [Multirate Digital Filters, Filter Banks, Polyphase Networks, and Applications](https://authors.library.caltech.edu/records/x720m-mr760) (1990) | Mathematical basis for separating a frame-rate raw-tracking path from slower coarticulation/diagnostic updates. | **Prototype** |
| A06 | Troeng and Fält, [A Refined Algorithm for Curve Fitting by Segmented Straight Lines](https://arxiv.org/abs/1806.11041) (2018) | Exact dynamic programming for few-breakpoint continuous PWL approximations; suitable for build-time shared-knot atlas generation. | **Conditional** |
| A07 | Camponogara and de Conto, [Models and Algorithms for Optimal Piecewise-Linear Function Approximation](https://doi.org/10.1155/2015/876862) (2015) | Optimizes segment count against approximation error and supplies the cost/error formulation for PWL maps. | **Conditional** |
| A08 | ozz-animation, [Animation Runtime](https://guillaumeblanc.github.io/ozz-animation/documentation/animation_runtime/) | Primary runtime implementation evidence for SoA sampling, forward caches, and packed pose blending. AVR cannot replace Unity's evaluator, but multi-output clips align with these principles. | **Background** |
| A09 | Frechette, [Animation Compression: Advanced Quantization](https://nfrechette.github.io/2017/03/12/anim_compression_advanced_quantization/) (2017) | Per-track variable bit rate and error budgets support sensitivity-aware generated coefficient quantization. Unity clip storage savings are insufficient unless sampled work also falls. | **Conditional** |
| A10 | Kavan et al., [Compressed Skinning for Facial Blendshapes](https://arxiv.org/abs/2406.11597) (2024) | Sparse skinning representation can accelerate facial deformation in an unrestricted pipeline. It rewrites the mesh representation and is explicitly out of scope. | **Reject** |
| A11 | Seo et al., [Compression and Direct Manipulation of Complex Blendshape Models](https://doi.org/10.1145/2070781.2024198) (2011) | HSS/GPU blendshape compression is strong evidence that deformation data can be compressed, but it changes mesh/GPU processing rather than controller overhead. | **Reject** |
| A12 | Sattler, Sarlette, and Klein, [Simple and Efficient Compression of Animation Sequences](https://doi.org/10.2312/SCA/SCA05/209-218) (2005) | Clustered PCA compresses animated geometry. It is not an ordinary-Animator controller transform and would touch mesh animation data. | **Reject** |
| A13 | Tournier et al., [Motion Compression Using Principal Geodesics Analysis](https://doi.org/10.1111/j.1467-8659.2009.01375.x) (2009) | Nonlinear pose-manifold compression is useful background for perceptual error but has no cheap avatar-side decoder. | **Reject runtime; Background offline** |
| A14 | Starke et al., [Learned Motion Matching](https://static-wordpress.ubisoft.com/montreal.ubisoft.com/wp-content/uploads/2020/07/09154101/Learned_Motion_Matching.pdf) (2020) | Learned compact pose selection supports the idea of a small transition bank, but its inference/search runtime cannot ship in VRChat. | **Reject runtime; Background** |
| A15 | Jégou, Douze, and Schmid, [Product Quantization for Nearest Neighbor Search](https://doi.org/10.1109/TPAMI.2010.57) (2011) | Codebooks could compress pose vectors, but decoding them in blend trees normally adds sampled curves. Retain only as an offline clustering tool. | **Reject runtime; Background offline** |

## Consolidated conclusions

### What is most likely to clear the 1 ms target

The strongest exact package is:

1. backward liveness/observability analysis;
2. one finite-domain, multi-output `Viseme` lookup;
3. projection of hard visemes into demanded sufficient statistics before the common observer;
4. zero-weight separation of speech, beta, and diagnostic regimes;
5. empirical extraction using actual Unity curve/clip costs.

These changes attack the measured causes directly: active clip samples, curve bindings, intermediates, and work performed in inactive modes. They do not depend on a low-rank assumption and do not alter a mesh.

### Why generic low-rank compression is not the first implementation

If all fifteen public viseme weights and all diagnostics remain observable, the static map can be full rank and an exact low-rank factorization cannot help. A factorization can also reduce serialized coefficients while increasing the number of active blend-tree stages. The correct order is therefore:

1. determine which outputs are actually observable;
2. commute the common filter through the demanded map;
3. measure the minimal realization of that input/output behavior;
4. retain a factorization only if its lowered Animator has fewer sampled curves.

Weighted-automaton, ERA/Ho-Kalman, Loewner, balanced, and tensor methods remain valuable after this output-sensitive reduction.

### Why quantization alone will not solve the frame-rate loss

Bit packing, codebooks, and mixed precision primarily reduce network or storage size. The reference profiler points to dense evaluation. Quantization is valuable when it causes coefficients, knot sets, states, or clips to become exactly shareable and therefore removes runtime samples. A smaller asset with the same active graph is not accepted as a CPU optimization.

### Why mesh and GPU papers are documented but rejected

The deformation literature can deliver large gains in a custom engine, but those methods change the blendshape/skinning basis, mesh data, shader, or runtime decoder. That conflicts with the explicit no-mesh requirement and overlaps optimizations already performed elsewhere in the avatar pipeline. AVR optimization should remain at the generated behavior/controller level.

## Implementation recommendation

Implement the exact compiler stages first and require a measured 1.0 ms improvement before introducing an approximate default. In parallel, build two isolated experimental prototypes:

- an output-sensitive weighted-automaton/ERA realization;
- a K-sparse native transition bank with a discarded-simplex-mass certificate.

Neither experimental path should replace the current exact mode until it passes the complete replay, responsiveness, VRCFury merge, and profiler protocol above.
