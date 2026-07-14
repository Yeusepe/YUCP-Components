# Changelog

All notable changes to YUCP Components will be documented in this file.

## [Unreleased]

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

