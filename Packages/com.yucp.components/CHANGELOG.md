# Changelog

All notable changes to YUCP Components will be documented in this file.

## [Unreleased]

### Changed
- Every Advanced Viseme avatar-menu slider now starts at `50%`, where it reproduces the profile's current authored behavior exactly. Centered piecewise response maps give `0%` and `100%` deliberately separated physical endpoints instead of exposing already-maxed gains. The Simple page uses plain-language `Exaggeration`, `Snappiness`, `Soft Speech`, `Reaction Speed`, `Smooth Pauses`, `Clear Consonants`, `Follow My Face`, and `Tongue Motion` controls. Exaggeration can double low-volume speech gain toward the full authored viseme but saturates at that pose, so it becomes visibly dramatic without overdriving blendshapes. All generated tuning controls always use the compact remote transport (or the shared Parameter Compressor); legacy Local Only assets migrate automatically.
- Advanced Viseme's no-tracker decoder now feeds its corpus-fitted, duration-conditioned target into a persistent frame-rate-correct observer selected by `Snappiness`. Immediate state selection prevents restartable Animator transitions from freezing under continuous hard-viseme changes; the centered response blends a calm two-pole estimate, a responsive one-pole estimate, and the crisp learned target while preserving a nonnegative unit simplex. Linear articulation rows use the same temporal epoch. The hard index still cannot recover Oculus's hidden 15 weights; active face-tracking authority and source meshes are unchanged.
- Viseme Test Emulator analysis samples now expose an immutable, sample-timestamped copy of Oculus LipSync's continuous 15-weight output for editor-side teacher capture. Native sub-unit onset/release mass is preserved without normalization, malformed frames are marked non-exact, quiet native tails are collected only inside the requesting source's reference-counted lossless scope, and no microphone audio or avatar runtime data is stored.
- Beta Coarticulation now leads only from the slow reconstructed simplex toward its continuous fast observer. The generated Animator no longer pushes its fast viseme or group-articulation stages toward VRChat's raw one-hot winner, removing the label-edge pose step while preserving the existing publication epoch, silence hold, exact endpoints, face-tracking authority, and source meshes.
- Advanced Viseme's graph optimizer now interns congruent private parameters: an Alpern-Wegman-Zadeck partition refinement over complete write-site multisets (layer, state, full BlendTree context, curve keys, defaults) merges private AAPs that provably carry identical values on every frame, rewrites their readers, and lets liveness collect the duplicates. Direct children are treated as an unordered sum while threshold and normalized-Direct contexts keep their full sibling geometry, so no evaluation epoch moves. On the profiling baseline this removes 13 parameters and 60 curves (shared frame-rate alpha vectors, duplicated support magnitudes, duplicated signed splits); a 170,000-frame randomized causal replay across five frame rates reports exactly zero difference on every public parameter and physical output, and same-session interleaved captures measured a small consistent `Animator.ProcessGraph` win under a speech-pattern load.
- Advanced Viseme's generated Animator now performs epoch-preserving closed-world liveness and a topology-proven neutral-zero reduction after graph lowering. Private AAP curves outside the observable cone are removed, and redundant zero bindings are omitted only in the nonnegative Beta-retention observer while every BlendTree child, threshold, and feedback stage remains intact. The representative Beta fixture drops 52 private parameters and 451 of 4,958 Animator curves while retaining mixed-frame-rate replay parity; meshes remain untouched.
- Approximate Beta-retention models now have a build-time acceptance certificate covering active-binding savings, Animator staging, steady endpoints, mandatory phonetic constraints, held-out trajectory error, and a universal simplex coefficient bound. The exact commuted model remains the fallback and current default.

## [0.3.43] - 2026-07-16

### Added
- Parameter Compressor, an avatar-wide generic replacement transport for persistent VRChat Bool, Int, and Float settings. It inspects the final VRCFury-merged menu and FX controller, protects momentary and sensor-driven inputs, and reduces selected parameters to a delay-insensitive constant-weight Bool bus with exact framing, late-join replay, and atomic snapshot-block commits.
- A mixed enumerative codec and deterministic radix planner. Six Bool wires form 20 weight-three symbols; reserving one for synchronization leaves radix 19, so all 26 native 255-level Advanced Viseme settings fit in three payload digits per focused record without separate parameter-index bits.
- Simple and Advanced YUCP inspector modes, reusable compression profiles, per-parameter inclusion, update priority, precision, numeric range, atomic group metadata, reserved-space planning, and build summaries.
- EditMode proofs for constant-weight alphabets, torn-wire transitions, frame resynchronization, exact VRChat Float quantization, deterministic planning, and the six-wire `26 x 255` capacity result.
- An Automatic Gain switch for the Viseme Test Emulator, allowing creators to preserve the microphone's original level while keeping automatic boost as the default.

### Changed
- Advanced Viseme Reconstructor detects the generic Parameter Compressor and registers its saved avatar-menu settings with the shared final-asset planner instead of generating the private 13-bit tuning transport. Without the generic component, existing Compact Synced and Local Only behavior is unchanged.
- Parameter compression now runs immediately before VRCFury's compressor against cloned final assets. Successful YUCP plans make VRCFury naturally no-op; source controllers, expression assets, menus, prefabs, and meshes remain untouched.
- Advanced Viseme's generated Animator now uses exact projected observers, output-liveness pruning, sparse articulation lanes, shared constant folding, and canonical piecewise-linear maps. A full reference-avatar A/B reduced the enabled `ProcessGraph` plus `ProcessAnimations` markers by 1.8852 ms; the remaining conservative AVR-only delta measured 0.5633 ms. Source meshes and authored blendshapes are never rewritten.

## [0.3.42] - 2026-07-15

### Changed
- Advanced Viseme Reconstructor avatar-menu settings can now remain saved while sharing all applicable sliders through one 13-bit quantized transport instead of up to 208 independently synced Float bits. The wearer keeps full-precision immediate values; remote avatars receive 255 levels, prioritize the open radial, and continuously repair late-join or dropped settings without adding BlendTrees. A Local Only mode retains zero-bit tuning.
- Advanced Viseme Reconstructor profiles now expose a focused per-viseme editor with friendly sound chips, grouped jaw/lip/tongue percentages, precise articulator axes, modified indicators, and one-click reset. Per-sound trims are baked into calibrated or direct-pose correction math, so reducing a clipping `R` jaw does not weaken other visemes or that viseme's tongue detail.
- Phrase enrollment now uses one automatic recording stage: it starts the selected microphone, waits for confirmed speech, waits through a cancellable end pause, saves useful takes, and advances without Start, Stop, Continue, or Next clicks.
- The enrollment overlay now uses a compact animated speech visualizer and a consistent 4/8/12/16/24 spacing rhythm, with technical controls and optional calibration kept collapsed.
- Phrase enrollment now ends with a focused Done/Continue action, keeps Skip for now available throughout, and lets creators select any saved take to record again; SDK navigation and Build & Test no longer compete with the teaching task.
- Phrase matching now compiles a log-median multi-take timing profile plus at most two weighted one-confusion paths. Each optional path changes exactly one visually confusable Oculus winner, pays an explicit acceptance cost, and is removed when recorded ordinary speech or the avatar-wide state cap makes it unsafe.

### Fixed
- The generated phrase Animator now mirrors enrollment's 30 ms hard-Viseme cleanup with alphabet-pruned, driver-free `(committed, candidate)` states. A changed winner is recorded immediately but is published only at the first Animator evaluation on or after the 30 ms wall-clock threshold; a shorter bounce returns to the prior winner, while rapid `A-B-C` speech advances directly without a driver-state stall. Irrelevant winners share one published `Other` class but retain separate probation clocks, so two different short bounces cannot combine into a fake stable run. Calibrated timing boundaries and label-change quarantine prevent delayed observations or endless chatter from extending and restarting stale candidates; a fresh Natural Speech root is consumed directly from quarantine so low-FPS recovery does not lose it through an intermediate Ready frame. Intermediate natural cadences no longer need to copy one enrollment take exactly, and one phrase-wide observation allowance is applied before negative calibration so authoring and the generated Animator accept the same timing corridor. Observer proof satisfies minima at or below 30 ms, while longer learned minima remain unchanged instead of receiving a per-phone credit. Stabilization remains local and Animator-only with zero additional synced bits. Ambiguous learned deletions that would fire as the prefix of a longer pronunciation now fail closed without rejecting genuinely recorded short/long variants.
- Phrase matching now retains a bounded cross-take pronunciation lattice instead of requiring every live hard-Viseme sequence to equal one complete enrollment take. One repeatedly supported `A-B-A` classifier bounce may bridge otherwise observed contexts, inferred phones use an analyzer-block debounce floor rather than overfitted singleton duration minima, and the 32-state fitter prunes only optional bridges, never the creator's four recorded paths. This fixes live candidates that advanced partway but could never emit `Matched`; existing current-format recordings rebake the derived model automatically without another microphone take.
- Personalized phrase enrollment now preserves normal accent and coarticulation differences as bounded whole-sequence alternatives instead of repeatedly asking the wearer to imitate one averaged trace. Singleton changes remain correlated paths rather than permissive aliases, same-phrase prefix pronunciations share the generated trie safely, and only genuine 12-run or avatar-wide 32-state overflows request a different phrase.
- Phrase enrollment now accepts clean three-shape signatures for short words such as “Cube,” while rejecting signatures that only reach three through classifier flicker or collapse to two states after boundary cleanup. The wizard and compiler share the same stabilized runs, `Take N` diagnostics route to the correct slot, and optional re-recordings are preview-compiled without overwriting a known-good enrollment or entering an automatic retry loop.
- Phrase capture now derives onset, duration, and end-of-speech from analyzer sample clocks with a robust noise-floor Schmitt gate and viseme corroboration. Quiet speech can start the timer, short within-phrase pauses no longer save early, stalled analyzers retry once, and stale hard visemes cannot hold a take open.
- Missing phrase enrollment discovered by VRCFury Play Mode preprocessing now hands off to the restored Edit Mode avatar instead of aborting the avatar pipeline or saving creator data on a temporary Play Mode copy.

## [0.3.41] - 2026-07-14

### Added
- Viseme Phrase Trigger, a prefab-friendly personalized visual phrase matcher that learns four microphone takes without storing audio, compiles them into a compact duration-aware Animator automaton, and requires an Advanced Viseme Reconstructor on the avatar.
- A YUCP-styled enrollment workflow with microphone selection, sample-clock capture, per-take viseme chips, consistency diagnostics, safe draft recovery, one-click retakes, and optional ordinary-speech negative calibration.
- One-bit-per-phrase network event carriers. The local wearer toggles a stable hidden Bool on each accepted match, and every client reconstructs the public timed `Matched` pulse from carrier edges with late-join suppression.
- Reusable local `Confidence` and `Progress` outputs, Natural Speech and Paused Command matching modes, deterministic DTW enrollment tooling, exact baked-language replay validation, source-prefix discovery, homophene checks, parameter-budget validation, and VRCFury full-controller injection.

### Changed
- Advanced Viseme Reconstructor public parameter names now share a versioned contract with dependent YUCP speech components.
- Viseme Test Emulator analysis frames can be consumed through a lossless sample-clock capture scope, so enrollment remains deterministic even when Editor updates are backlogged.

### Fixed
- Phrase enrollment no longer loses complete analyzer frames when microphone input arrives faster than bounded live preview updates.
- Phrase timing clocks are isolated from parallel cooldown and edge-decoder layers, and low-frame-rate one-frame visemes use exit-time segments instead of stale animated clock values.
- Baked phrase matching now preserves calibrated alias/skip costs and whole-phrase duration bounds, preventing editor-only DTW scores from accepting a different language than the generated Animator.

## [0.3.40] - 2026-07-14

### Added
- Advanced Viseme Reconstructor adds a local-only `Speech Liveliness` slider in both Simple and Advanced tuning. It makes speech-only transitions quicker and more distinct through a bounded fast/slow observer blend, then fades the effect continuously to zero as face tracking becomes active; authored visemes are never overdriven beyond their convex animation hull.
- Advanced Viseme Reconstructor now supports direct VRCFury BlendShape Link targets, including tailored one-to-many mappings and root-level renderers, while preserving target-local authored viseme detail without adding expression parameters or synced bits.
- Custom Object Sync grouping system: add a Group ID field per component and automatically merge matching components into a single VRLabs Custom Object Sync rig to reduce parameter usage.
- Group-aware editing: changing the settings on any component automatically propagates the new values to every member of the same group, keeping builds consistent.
- Parameter budget now reflects the actual number of objects in the current group and surfaces the calculated sync cost plus group size summary.

### Changed
- Advanced Viseme Reconstructor now opens in a compact Simple tab with plain-language expression strength, quiet-speech detail, reaction speed, pause stability, pronunciation, tongue, transition, tracking, and avatar-menu controls. Its generated VRChat tuning menu now mirrors that workflow with Simple and Advanced branches backed by the same local parameters. The Advanced inspector and menu retain the complete rig and mathematical tuning surface, without adding synced bits or duplicate motion logic.
- Calibrated residual ownership now uses a low-rank basis correction with independent authority for identifiable articulator axes and conservative shared authority for rank-dependent axes. It replaces up to 15 per-viseme conflict morphs with at most two nonnegative 0..100 carriers per retained basis ray, avoiding VRChat's final blendshape clamp, and lets genuine native tongue tracking own tongue geometry while inferred or unsupported axes retain authored detail.
- Compatible controller-only face-tracking pose proxies now drive their calibrated visible axes directly while active, preserving the template's native local jaw response instead of passing it through AVR's speech observer. Tracking diagnostics continue to be published in parallel.
- Generated reconstruction math now batches simplex observers, voice products, viseme-to-articulator projections, residual weights, ownership projections, and linked-renderer residual poses into vector or matrix BlendTrees. Beta jaw and lip coarticulation is contracted exactly in articulator space instead of materializing redundant 15-weight groups.
- Beta tongue inference now contracts the fitted viseme/latent/output tensor before runtime evaluation, represents face-conditioned `PP <-> nn` inference as one exact rank-one update, and shares one multi-output frame-rate alpha lookup. This removes hundreds of scalar BlendTrees and dependency parameters without changing the fitted full-precision model.
- The hidden-phone logistic approximation now uses a 13-knot symmetric minimax table instead of 19 hand-spaced samples, reducing evaluated clips while lowering worst-case interpolation error from about 0.00748 to 0.00534.
- The runtime-facing `Contradiction Fade` label is now `Tracked Surface Yield`; the serialized control and parameter name remain stable.
- Custom Object Sync inspector now uses the standard YUCP styling, refreshed parameter budget card, and an updated grouping section that explains the new workflow.
- Custom Object Sync inspector flow reorganized with summary + card-based sections for easier tuning.
- Max Radius control now shows the computed meter range inline and explains the trade-off between coverage and parameter cost.
- Added an optional Scene view gizmo that visualizes travel radius, precision, and rotation when selecting a Custom Object Sync component.
- Custom Object Sync grouping is now opt-in via an “Enable Grouping” toggle; leave it off to mirror the original VRLabs per-object workflow, or enable it to share rigs intentionally.

### Removed
- Auto Grip Generator component, editor tooling, and preprocessing pipeline.

### Fixed
- One-sided signed tailored-template axes, such as a forward-only jaw pose, now use the neutral endpoint for the unsupported direction instead of failing the build.
- Direct tailored-template rendering and public articulation now use the same calibrated, confidence-weighted authority during tracker startup, remote playback, and tracker loss, preventing the visible pose and residual ownership from briefly disagreeing.
- VRCFury BlendShape Link integration now validates its reflected schema, Animator-path uniqueness, direct-link provenance, and include-all/fuzzy mappings after mesh generation, preventing root-path omissions, order-dependent chains, competing incoming writers, generated-name rediscovery, duplicate drives, and silent mapping drift.
- Primary and linked residual calibration now rejects geometrically nonlinear multi-frame blendshapes instead of claiming a linear/exact reconstruction that Unity cannot reproduce.
- Calibration now solves the box-constrained problem `0 <= C <= 1`; authored motion beyond a basis shape's usable 100% range remains in the exact residual instead of being clipped by VRChat. Near-dependent basis rays share authority only within their dependency group, while independent jaw, lip, and tongue rays remain independently responsive.
- Tailored coarticulation rays are projected into their valid unit interval, shared pose bindings that could sum beyond 100% are rejected, and signed articulation-only BlendShape Links receive target-local inverse geometry instead of losing their negative half to VRChat's clamp.
- Advanced Viseme builds now stage generated assets and roll back renderer meshes, lip-sync mode, VRCFury features, and profile diagnostics when any late validation or asset operation fails. Cached diagnostics and profile migration bookkeeping no longer change generated content hashes.
- Multi-component builds now validate the cumulative expression-parameter union and recheck the final post-VRCFury/Modular-Avatar descriptor against VRChat's 256-bit limit. Same-name tracking wires must also match the required network-sync contract. Transient generated face meshes use deterministic geometry-content hashes instead of Unity session IDs.

## [0.3.39] - 2026-07-13

### Added
- Viseme Test Emulator component with microphone selection, automatic Play Mode execution, continuous 15-weight Oculus descriptor output, dominant `Viseme`/continuous `Voice` parameter driving, Mouth & Jaw Tracking Control suppression, Gesture Manager integration, manual testing, and automatic Oculus LipSync plugin support.
- Full Unified Expressions tongue reconstruction and tracking (`TongueX`, `TongueY`, roll, arch, shape, and independent twists), including an optimized 57-bit FullTongue18 preset and automatic zero-cost reuse when a compatible VRCFT template already provides the channels.
- Reusable speech evidence outputs for vowel, bilabial, labiodental, sibilant, coronal, dorsal, rhotic, tongue-contact, M-compatible (`PP` = `p`/`b`/`m`), and N-compatible (`nn` = `n`/`l`) confidence. M and N are evidence signals, not phoneme classifiers.
- Separate Normal and Beta Coarticulation reconstruction modes. Beta uses a reproducibly trained, frame-rate-correct SPIRE EMA transition model with independent jaw, lip, tongue-tip, and tongue-body context; Normal remains the default and does not build the experimental graph.
- Separate SPIRE-trained Balanced and Quality visible-face tongue estimators for Beta mode. They infer only bounded tongue-tip advance and height through authored headroom, add no synced parameters, and automatically yield to native tongue motion.
- Confidence-gated Beta hidden-phone inference for the ambiguous `/m/` versus `/n,l/` case. Aperture, Balanced, and Quality models combine the actual Oculus winner history with tracked face dynamics, expose local `Speech/Hypothesis/{M,N,Confidence}` outputs, and only condition unobserved tongue priors.
- Zero-cost, exact-prefix reuse of an existing Float `SoftPalateClose` channel as optional oral-evidence confidence; no channel is generated or synced for it.
- A build-only complement-space `PP - nn` residual morph for calibrated meshes. Beta can transfer inferred interior/tongue detail while remaining orthogonal to every driven jaw/lip articulator axis; source meshes and parameter budgets are unchanged.
- Reproducible, pinned corpus-training tooling, held-out metrics, generated coefficient provenance, and CC BY 4.0 third-party notices.
- Saved, local radial-puppet tuning menus for speech, tracking, phonetics, and the full synthesized tongue rig. The component can expose only the selected groups, and every generated tuning parameter is unsynced (zero synced bits).
- Adaptive soft speech hangover with a causal leaky talkspurt-history observer, an unsynced `Speech/Talking` output, and a local `Silence Stability` slider. Established speech earns more protection from brief `sil` gaps; its existing viseme identity and pose gain are retained without amplifying quiet speech; real non-silence visemes still interrupt immediately; and `Voice` alone can never pin an old mouth pose.

### Changed
- Advanced Viseme Reconstructor now uses a progressive YUCP inspector with inline motion/profile sliders, selectable runtime-menu groups, mapping coverage, focused viseme and articulator editors, fit analysis, safe missing-only auto-mapping, and an integrated Viseme Test Emulator shortcut.
- Runtime tuning is wired into the observer and fusion graph: users can adjust frame-rate-correct speech/tracking response, voice sensitivity, quiet motion, remote trust, residual contradiction handling, phonetic assists, hidden-phone inference, authored detail, and each synthesized tongue axis without weakening exact settled local tracking authority.
- Advanced Viseme Reconstructor now gives settled active local measurements exact precedence on every measured visible axis. Compatible tailored VRCFT poses are calibrated as composite geometric axes, while the orthogonal authored viseme residual preserves tongue, teeth, interior-mouth, and other non-conflicting motion instead of erasing the entire viseme.
- Reused and generated VRCFT streams now pass through one One-Euro-inspired adaptive fast/slow observer (not an exact 1€ implementation) that suppresses stationary OSC/quantization jitter without restoring the former post-speech lag. Generic post-fusion mouth clamps no longer alter tailored template coordinates.
- Beta Coarticulation continuously mixes both axes of its trained transition table instead of hard-switching destination columns when VRChat changes the winning index.
- Direct-pose fallback now applies per-articulator corrections, so local/remote reliability, phonetic constraints, and inferred tongue motion affect the visible mesh without additive overextension.
- Auto tracking is now strictly reuse-only: it adds no synced parameters or duplicate menu toggle and falls back to speech reconstruction when no compatible installation is present. Full reconstructed articulation outputs no longer depend on the selected measurement preset.

### Fixed
- Reused face-tracking templates no longer suppress every authored viseme at full local tracking authority. The output decomposes `R = V - U C` into tracking-parallel conflict and orthogonal detail, then reconstructs `U z + d(R_perp + r R_parallel)p`. Exact tracked jaw/lip coordinates are preserved while tongue, teeth, and interior-mouth detail cannot be erased by ordinary face movement.
- Partial custom face-tracking templates no longer fabricate missing `/v2/` channels and pull those articulators toward zero.
- Custom-prefixed VRCFT tracking-active parameters are paired with their own source; both existing Float Animator gates and genuine Bool gates are supported without controller type conflicts.
- Activity gates from unrelated prefixes and conflicting Animator parameter declarations are rejected instead of being paired by traversal order.
- Authored non-neutral `sil` poses now participate in articulation, direct output, and residual reconstruction like the other 14 visemes.
- Reused-template ownership now accepts only complete, static, unit, separable target-face blendshape poses and rejects normalized, nested, 2D, shared-binding, bone, material, and cross-renderer mappings.
- Beta tongue inference now observes unfiltered calibrated tracking exactly once, uses an unscaled phonetic center, and applies phonetic tracking confidence once instead of squaring vowel attenuation.
- Quiet-speech Beta conditioning now uses speech presence for posterior authority and applies expressive gain once, avoiding squared voice attenuation. Tailored templates with only jaw/open/closed mouth semantics can use the Aperture posterior without fabricated protrusion channels.
- Hidden-phone training now reproduces the exact Beta group-center/common-fast observer, uses occurrence-count reliability rather than class-balanced pseudo-counts, and applies a model-specific empirical support gate before changing tongue priors.
- Hidden-phone eligibility now keeps every forced-phone occurrence in its denominator, so vowels, silence, stops, and unrelated consonants make the posterior abstain instead of inheriting confidence from a decaying `PP/nn` tail.
- Conservative generated factor envelopes prevent Animator BlendTree intermediate clipping while remaining algebraically equivalent to the trained model through the final output clamp.
- Articulator clip overrides no longer enter residual mode and then disappear from the generated output.
- Profile migration preserves authored `TongueY` values and intentionally removed bindings instead of restoring defaults on every validation pass.
- Duplicate articulator mappings to one driven blendshape now fail clearly instead of summing the same shape past its authored range.
- Voice-assisted speech activity no longer flickers the full reconstruction on transient `sil` frames, phonetic constraints use monotone soft projections, filtered strong visible contradictions fade only the tracker-parallel part of calibrated residual correctives, and unsupported tongue noise must be sustained before native capability latches.
- Short microphone-threshold crossings and VRChat `sil` gaps no longer collapse the viseme simplex for a frame. Confirmed speech earns a bounded release hold that grows with the talkspurt and always converges back to the authored silence pose.
- Coupled viseme and calibrated-residual fading no longer waits for an unrelated all-seven-channel ownership test, so a locally tracked `aa` cannot move an already measured aperture merely because another visible channel is absent.
- PP, FF, and sibilant projections now yield on locally measured target axes while remaining available for missing measurements and conservative remote fusion. One-sided signed templates keep their unsupported direction neutral, while distinct positive/negative poses are corrected as independent rays so Smile-to-Sad crossings cannot stack both shapes.

## [0.3.38] - 2026-07-12

### Added
- Advanced Viseme Reconstructor component with a frame-rate-correct, two-pole continuous viseme observer and 15 reusable soft viseme outputs.
- Unified jaw, lip, and tongue articulation outputs, signed motion velocity, speech onset/release, and smoothly fused VRCFaceTracking Unified Expressions v2 input.
- Adaptive VRCFaceTracking binary encoding with 25-bit Balanced8 and 39-bit Quality12 defaults, optional uniform 4-bit or full-float precision, build-time validation, and a manual tracking fallback toggle.
- Generic VRCFaceTracking installation discovery across descriptor, VRCFury, and Modular Avatar controller/parameter references, with decoded-proxy preference, partial-channel speech fallback, and custom pose extraction from tailored template clips.
- Nonnegative mesh-basis calibration with generated residual blendshapes, preserving authored Oculus viseme vertices, normals, and tangents exactly after decomposition.
- Reusable reconstruction profiles, a standard YUCP UI Toolkit inspector, automatic rig mapping, calibration diagnostics, VRCFury public-API controller injection, and Editor tests for observer, inspector, and residual math.

### Fixed
- Replaced the invalid integer-driven viseme BlendTrees with a 15-state integer decoder and internal Float driver, allowing all Oculus viseme indices to reconstruct correctly after VRCFury merging.
- Added compatible `ih`/`oh`/`ou` mesh-name resolution alongside shortened `i`/`o`/`u` names, including recovery from invalid Avatar Descriptor entries.
- Preserved built-in and VRCFaceTracking input parameter names as VRCFury globals so external tracking and OSC data reach the generated controller.

## [0.3.0] - 2024-10-31

### Added
- **Pakacage Guardian**: Production-ready version control system for Unity projects
  - **Unified Dashboard**: Single-window interface with three integrated tabs
    - **Overview Tab**: Repository status, quick actions, and recent activity timeline
    - **Commit Graph Tab**: Split-view with visual history and file change details
    - **Stashes Tab**: Complete stash management with apply/drop actions
  - **Full Diff Engine**: Complete file comparison system
    - Recursive tree comparison for detecting file changes
    - Line-by-line text diff with Myers algorithm
    - Color-coded change visualization (added/modified/deleted/renamed)
    - Dedicated diff viewer window with syntax highlighting
    - Binary file detection
  - **Content-Addressed Storage**: SHA-256 hashing with Deflate compression
  - **Automatic Snapshots**: Hooks into file save and Unity Package Manager events
  - **Visual Commit Graph**: Lane-based visualization with real-time updates
  - **Crash-Resistant**: Journal-based transactions ensure data integrity
  - **Deep Unity Integration**: Asset postprocessor and UPM event monitoring
  - **YUCP Brand Styling**: Dark theme (#090909) with teal accents (#36BFB1)
  - Guardian compatibility layer for migration from legacy systems
  - Localization support (English and Spanish)
- Repository initialization on first use
- Import Monitor with debounced events
- Settings asset for configuration
- .pgignore support for custom ignore patterns

### Changed
- Updated package description to include Pakacage Guardian
- Enhanced project safety with automatic backups

### Technical Details
- Core VCS engine in .NET Standard 2.1
- Deflate compression for all objects
- Index cache for fast snapshots (size + mtime tracking)
- Tree-based directory snapshots
- Commit objects with parent tracking
- Ref database with symbolic and direct refs
- Optional chunked storage for large files (>50MB)

## [0.2.9] - Previous Release

### Features
- Auto Body Hider with GPU-accelerated detection
- Symmetric Armature Auto-Link
- Closest Bone Auto-Link
- View Position & Head Auto-Link
- Auto UV Discard
- UV Discard Toggle
- Gesture Manager Input Emulator
- Avatar Optimizer Plugin integration

## Migration Notes

### From Legacy Guardian
If you were using a standalone Guardian package:
1. Pakacage Guardian will detect legacy data automatically
2. You'll be prompted to archive the old data
3. New snapshots will use the improved Pakacage Guardian system

### Upgrading from 0.2.x
- Pakacage Guardian is automatically available
- No manual setup required
- Repository initializes on first access

## Known Issues
- YAML-specific diff parsing for Unity scenes/prefabs (uses generic text diff currently)
- Command palette for keyboard shortcuts pending
- Comprehensive unit tests in development

## Future Plans
- Unity-specific YAML diff with object hierarchy visualization
- Command palette with fuzzy search (Ctrl/Cmd+K)
- Performance optimizations for repositories with 10,000+ commits
- Additional localization languages (Japanese, German, French)
- Optional Git interoperability for hybrid workflows
- Binary diff visualization for images/textures
- Merge conflict resolution UI
