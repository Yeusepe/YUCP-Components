# YUCP Components

Advanced VRChat avatar components with VRCFury integration and Pakacage Guardian VCS.

## Features

### Version Control
- **Pakacage Guardian** - Content-addressed version control system for Unity projects
  - Automatic snapshots on file save and package manager events
  - Visual commit graph with timeline
  - Fast rollback to any previous state
  - Stash management
  - Deep Unity integration

### Armature Components
- **Symmetric Armature Auto-Link** - Automatically attach objects to left/right body parts
- **Closest Bone Auto-Link** - Find and attach to nearest bone (including extra bones like ears, tails)
- **View Position & Head Auto-Link** - Position objects at avatar view position
- **Blendshape Markdown** - Organize blendshapes into markdown-style nested sections inside the native `SkinnedMeshRenderer` inspector

### Mesh Components
- **Auto Body Hider** - Automatically hide body parts covered by clothing
  - GPU-accelerated detection
  - Multiple detection algorithms (Raycast, Proximity, Hybrid, Smart, Manual)
  - Poiyomi and FastFur UV support with multi-clothing coordination
  - Layered clothing optimization

### Facial Animation
- **Viseme Test Emulator** - Preview the avatar descriptor's real VRChat lip-sync behavior in Unity from a selected microphone or 15 manual viseme buttons. It starts automatically with Play Mode, applies all 15 continuous Oculus weights to descriptor blendshapes while publishing the dominant `Viseme` and continuous `Voice`, and honors Mouth & Jaw Animator Tracking Control just like VRChat. It also supports jaw flaps, `Viseme Parameter Only`, Gesture Manager, and automatic restoration when stopped. If the licensed Oculus LipSync Unity plugin is installed, Auto uses it directly; otherwise a local real-time classifier is used.
- **Advanced Viseme Reconstructor** - Converts VRChat's hard Oculus viseme index into 15 continuous, simplex-preserving weights.
  - Publishes reusable jaw, lip, tongue, velocity, energy, onset, and release Animator parameters.
  - Smoothly fuses VRCFaceTracking Unified Expressions v2 data with an automatic audio-viseme fallback.
  - Uses VRCFaceTracking binary parameters by default: Balanced8 costs up to 25 synced bits and Quality12 up to 39 bits.
  - Reconstructs a full authored tongue pose; Beta can infer bounded tongue-tip advance and height from visible face tracking without adding synced parameters.
  - Reuses current Unified Expressions channels from compatible tailored VRCFT templates without depending on a specific template package.
  - Can decompose authored visemes into a shared tracking basis plus exact residual blendshapes without modifying the source mesh.

## Installation

### Via VCC (Recommended)

1. Add this VPM repository to your VRChat Creator Companion:
   ```
   http://vpm.yucp.club/index.json
   ```

2. Open your avatar project in VCC
3. Click "Manage Project"
4. Find "YUCP Components" and click "+" to install
5. VRCFury will be installed automatically as a dependency

### Manual Installation

1. Download the latest `.unitypackage` from [Releases](https://github.com/Yeusepe/YUCP-Components/releases)
2. Import into your Unity project
3. Install VRCFury from https://vrcfury.com/download

## Dependencies

This package requires:
- **VRCFury** (automatically installed via VPM)
- **VRChat SDK3 Avatars** (automatically installed via VPM)
- Unity 2022.3.x

## Usage

### Pakacage Guardian

Access Pakacage Guardian via `Tools > YUCP > Pakacage Guardian`:
- **Unified Interface**: Single window with tabbed navigation and YUCP brand styling
- **Overview Tab**: Repository status, quick actions, and recent activity
- **Commit Graph Tab**: Visual timeline with file changes and diff viewer
- **Stashes Tab**: Manage automatic and manual snapshots
- **Full Diff Engine**: Line-by-line comparison for text files

### Package Manager

Access Package Manager via `Tools > YUCP > Package Manager`:
- **Custom Import UI**: Beautiful package import window with banner, metadata, and product links
- **Read-Only Metadata Display**: View package information (icon, author, description, links) during import
- **Future**: Full package management system for downloading and updating packages

### Avatar Components

1. Add YUCP components to your avatar from `Component > YUCP` menu
2. Configure component settings in the Inspector
3. Build your avatar - components process automatically
4. No manual setup needed - VRCFury handles all integration

For `Blendshape Markdown`, add the component to the renderer you want to organize, configure heading rules such as `# Title`, `==Body/Head==`, or `|---Section---|`, and then use the native `SkinnedMeshRenderer` inspector to browse the grouped foldouts.

For `Advanced Viseme Reconstructor`, add the component anywhere under the avatar descriptor, assign the face renderer or use the descriptor renderer, and optionally create a reusable reconstruction profile. The default `Auto` input mode reuses a compatible decoded/proxy Unified Expressions stream from an existing VRCFury or Modular Avatar installation. If none exists, `Auto` stays speech-only and adds **zero synced bits**; choose Balanced8, Quality12, or FullTongue18 only when this component should create new tracking inputs. Use `Outputs Only` to consume the generated `YUCP/AdvancedViseme/...` parameters without changing the avatar's mouth configuration.

The inspector keeps normal setup compact and puts customization into remembered foldouts. `Motion Tuning` edits the reusable profile defaults; `Avatar Menu` can generate saved radial sliders under `YUCP/Viseme Settings`; and `Rig & Calibration` provides mapping coverage, missing-only auto-mapping, explicit remapping, fit analysis, focused viseme/articulator editing, and a Viseme Test Emulator shortcut. The runtime menu is divided into Speech, Tracking, Phonetics, and Tongue submenus, each within VRChat's eight-control limit. Its Float parameters are deliberately local, saved, and unsynced, so enabling every applicable slider costs **zero synced parameter bits**. `Silence Stability` controls the causal speech-memory release: the center uses the profile value, zero is an exact bypass, and the upper half retains an established talkspurt longer. Profile defaults remain the deterministic behavior seen where a local menu preference is unavailable.

Runtime controls modify separate terms instead of stacking another mouth pose. Speech and tracking smoothness interpolate among frame-rate-correct observer poles; voice sensitivity changes expressive energy; silence stability changes only how strongly an established talkspurt can resist a transient `sil`; remote trust affects only remote fusion; contradiction and authored-detail controls affect calibrated residuals; phonetic controls scale PP/FF/sibilant projections; and tongue controls scale only synthesized speech/inference axes. A settled active local face-tracking measurement still has exact authority on its own coordinate, so an open tracked mouth is not pulled by an `aa` viseme and native tongue tracking is not weakened by a tuning slider.

The silence decision is causal and deliberately asymmetric, adapting the short/long overhang used by [WebRTC VAD](https://webrtc.googlesource.com/src/+/refs/heads/master/common_audio/vad/vad_core.c) and the burst-confirmation plus hangover pattern in [3GPP/ETSI VAD](https://www.etsi.org/deliver/etsi_ts/146000_146099/146032/11.00.00_60/ts_146032v110000p.pdf). A leaky observer updates speech history with `h <- h + (1-exp(-dt/tau)) (target-h)`: non-`sil` charges it quickly and `sil` releases it at the configured rate. Its stored center and endpoint responses are frame-rate-correct; intermediate upper-half menu values continuously interpolate those coefficients. A continuous linear 0.35-to-0.55 history ramp turns that memory into hold authority, so a long talkspurt earns more protection than a short sound without an Animator countdown or feedback state machine. `Voice` is deliberately excluded from this decision and therefore cannot pin the mouth; it still drives live speech energy, onset, release, and expressive amplitude. At full confidence the existing fast simplex and its already-computed speech-pose gain are frozen, while partial confidence blends continuously into the normal release and the slow stage keeps settling. This retains the pose that was actually present instead of forcing quiet speech larger. Any real non-`sil` index bypasses the hold immediately. All 15 weights remain nonnegative and sum to one, face tracking continues updating, and genuine silence converges normally to the authored `sil` pose.

The component cannot recover the original Oculus classifier weights because VRChat exposes only the winning `Viseme` index. Instead, it publishes an interruptible, frame-rate-correct continuous estimate. With the default prefix, the unsynced global output contract is:

- `YUCP/AdvancedViseme/Viseme/{sil,PP,FF,TH,DD,kk,CH,SS,nn,RR,aa,E,I,O,U}` for the normalized 15-weight reconstruction.
- `YUCP/AdvancedViseme/Articulation/{JawOpen,LipClose,MouthOpen,LipFunnel,LipPucker,LipSuck,SmileSad,LipBite,TongueOut,JawX,JawZ,MouthX,TongueY,TongueX,TongueRoll,TongueArchY,TongueShape,TongueTwistRight,TongueTwistLeft}` for the complete reconstructed lower-face basis.
- `YUCP/AdvancedViseme/Velocity/...` for signed articulator velocity and `Speech/{Energy,Onset,Release,Talking,TrackingBlend,Vowel,Bilabial,Labiodental,Sibilant,Coronal,Dorsal,Rhotic,TongueContact,M,N}` for speech-state and phonetic evidence.
- In Beta mode with compatible jaw/aperture tracking, `YUCP/AdvancedViseme/Speech/Hypothesis/{M,N,Confidence}` reports the confidence-gated hidden-phone posterior used by the tongue prior. These local Animator outputs are not synced inputs.

`Speech/M` and `Speech/N` are compatibility evidence, not phoneme classifiers. `M` reports evidence compatible with the merged Oculus `PP` class (`p`/`b`/`m`), while `N` reports evidence compatible with the merged `nn` class (`n`/`l`); VRChat's winning viseme index cannot recover the distinctions inside either class.

Beta's `Speech/Hypothesis` output is deliberately narrower and probabilistic. It combines the causal Oculus observation history with measured jaw opening and lip aperture to estimate whether the ambiguous bilabial/lingual evidence is more compatible with `/m/` or `/n,l/`. A visibly held closure can therefore recover an `/m/` that Oculus emitted as `nn`, while an open or lingual face can retain the `nn` tongue prior. The estimate only redistributes the hidden `PP`/`nn` mass used by tongue-tip and tongue-body synthesis: it never changes the public 15-weight simplex, stacks an extra visible mouth pose, or overrides measured jaw and lip channels. Low reliability, weak speech, tracker loss, contradictory motion, or out-of-distribution features fade the correction smoothly back to the original reconstruction.

All articulation outputs exist in every mode; the tracking preset controls only which axes are measured. Missing measurements remain reconstructed from speech instead of being replaced with zero.

`Normal` is the default and preserves the established two-pole viseme observer after the shared silence-decision stage; it never builds or evaluates a learned model. `Beta Coarticulation` is explicitly separate: it uses the same silence decision, then a corpus-timed context simplex and independent learned carryover for jaw, lips, tongue tip, and tongue body. Both the previous-context and destination axes are mixed continuously, so a hard VRChat index change cannot switch between trained transition-table columns in one frame. The checked-in `4 x 15 x 15` table was fitted from 13,325 transitions in the [SPIRE EMA Corpus](https://huggingface.co/datasets/SpireLab/SPIRE_EMA_CORPUS/tree/55f21628de95514e3ff22eaccc75e1547d181297) and improved held-out transition-window MSE by 9.803% overall.

When compatible visible tracking is active but native tongue data is absent, Beta also applies a compact corpus-trained residual estimator. Balanced uses jaw opening, lip aperture, and lip protrusion; Quality is a separate fit that additionally uses jaw advance (`JawZ`/`JawForward`, never lateral `JawX`). It predicts only tongue-tip advance and height. Relative to the viseme-only residual MSE inside held-out SPIRE data, the reductions are 9.914%/13.340% for Balanced and 14.594%/15.147% for Quality; these percentages are not absolute reconstruction accuracy. Exactly one 24 ms observer starts from unfiltered calibrated visible semantics; feeding the estimator an already smoothed tracking stream changes its trained dynamics. Every Animator factor is normalized by a generated conservative envelope, preserving the trained affine model without intermediate BlendTree clipping. The prediction is composed through the remaining authored headroom, capped to 30% for visible `TongueOut` and 65% for `TongueY`, suppressed by closures and out-of-distribution input, and automatically yields to measured tongue tracking. SPIRE EMA and Unified Expressions are unpaired domains, so these held-out scores are corpus-domain evidence—not measured VRCFT accuracy or native tongue tracking. It never fabricates lateral motion, roll, twist, or asymmetry from this midsagittal corpus, and it adds no parameters or menu controls.

The hidden `/m/` versus `/n,l/` posterior has Aperture, Balanced, and Quality fits, so a tailored template only needs reusable `JawOpen`, `MouthOpen`, and `MouthClosed` semantics to participate; protrusion and jaw advance select richer fits when present. Training ran the installed Oculus LipSync 1.54 Enhanced provider over the paired SPIRE audio, then simulated the exact Beta observer at 100 Hz. On unseen speakers and sentences, the three models reached F1 scores of 0.9242, 0.9229, and 0.9168 respectively, and recovered 39–40 of 50 true `/m/` frames whose hard Oculus winner was `nn`. Those are corpus-domain compatibility results, not proof of phoneme recovery on live VRCFT. `/p/` and `/b/` share the same visible closure and reduce `PP` reliability; the component does not invent `p/b`, `t/d`, `k/g`, `s/z`, or `n/l` distinctions.

On a mesh that supports residual calibration, YUCP also extracts one build-only signed hidden-detail morph `H = (I - P_U)(V_PP - V_nn)`. `P_U` projects onto every verified driven articulator pose, so the posterior can transfer the authored PP-versus-`nn` tongue/interior difference without moving any jaw or lip coordinate represented by the tracking basis. The correction is `confidence * (posteriorPP - originalPP) * H`, adds no parameter, and is exactly zero when the model abstains. Because `H` is already orthogonal to the visible tracking basis, ordinary jaw or lip disagreement cannot erase it. Compatible reused templates are calibrated from their actual composite pose clips, including clips that drive several blendshapes as one semantic axis. The source mesh remains untouched; only a build clone receives the residual shapes.

The corpus ARPAbet-to-viseme mapping is a training surrogate for VRChat's hidden classifier, not recovered VRChat data. Both Beta models are causal and experimental; they cannot recover distinctions destroyed by VRChat's single winning index. The reproducible pinned-data pipeline, generated coefficient hashes, split policy, and full limitations are under `Tools/AdvancedVisemeTraining`.

The hidden-phone fit is pinned to the default 24 ms viseme observer. If a custom profile changes that upstream response, a log-domain compatibility gate fades the posterior toward abstention as the phase leaves the trained regime; it does not evaluate mismatched timing at full confidence.

Tracking is complementary replacement rather than additive pose stacking. Every observed jaw or lip coordinate follows a One-Euro-inspired adaptive fast/slow observer—not an exact 1€ filter—so small OSC or quantization motion settles onto the stable two-pole path while deliberate motion selects the low-lag one-pole path. Once active local tracking has settled, every measured visible axis has exact precedence whether or not it agrees with the speech prior. For a calibrated rig, write the authored residual as `R = V - U C = R_perp + R_parallel`, where `R_parallel` is the part reproducible by the verified tracking basis. The final mesh is `U z + d(R_perp + r R_parallel)p`: tracking replaces the `U` coordinates, authored detail `d` preserves tongue, teeth, mouth-interior, and other complement-space motion, and contradiction retention `r` can fade only the genuinely conflicting parallel part. It can therefore never reduce all viseme detail to zero merely because face tracking is active. Unsafe uncalibrated fallback retains conservative per-viseme suppression. Tracker confidence fades through startup and loss instead of switching controllers.

`Phonetic Assist` applies only three sparse physical constraints through monotone soft projections: bilabial closure for `PP`, labiodental contact for `FF`, and a sibilant jaw ceiling for `SS`/`CH`. Each projection yields on its target axis when that axis has a settled active local measurement; it remains available for missing measurements and conservative remote fusion. `Tracker Authoritative` skips those rules. No generic post-fusion MouthOpen/MouthClosed or Pucker/Suck clamp is imposed on a tailored template. Build-time nonnegative decomposition creates residual shapes on a generated mesh clone from either mapped articulator shapes or verified composite template poses. Normal-mode convex viseme blends and every steady authored viseme remain equal to the source pose within floating-point tolerance when speech owns the face. Beta deliberately allows different articulator groups to take different bounded paths between those exact endpoints. The source mesh is never modified.

Tracking encoding defaults to `Adaptive Binary`: 2-4 magnitude bits are allocated per channel according to perceptual importance, plus sign bits only for signed channels. It uses 25 bits for Balanced8, 39 for Quality12, or 57 for FullTongue18 including tracking-active and menu-toggle bits. `Uniform 4 Bit Binary` uses 35/55/82 bits. `Full Float` remains available for maximum input precision at 66/98/146 bits. Binary inputs follow VRCFaceTracking's `Parameter1`, `Parameter2`, `Parameter4`, and `ParameterNegative` naming and are decoded into smooth local float parameters by the generated FX controller.

Existing-installation compatibility is capability-based rather than template-specific. The builder scans parameter assets and Animator controllers referenced anywhere under the avatar, ranks `/v2/` float sources by semantic channel coverage, prefers controller-only decoded/proxy outputs over raw OSC inputs, and permits an explicit prefix when several candidates tie. It recognizes root and same-prefix `ExpressionTrackingActive`/`LipTrackingActive` declarations, including the Bool-on-wire/Float-in-Animator convention used by Jerry and Pawlygon templates; a genuine Animator Bool is converted to a private float safely. Another installation's prefixed activity gate is never borrowed, and conflicting Animator types are rejected. Partial templates fuse only channels they actually contain.

If the selected existing prefix also exposes a Float `SoftPalateClose`, Beta reuses it opportunistically at zero parameter cost. A sustained nonzero signal is required before capability latches, and it is treated only as oral/closed-palate evidence that can lower an unsupported nasal correction. YUCP never creates or syncs this optional channel, never borrows it from another prefix, and never treats nostril or nose blendshapes as nasality sensors.

For tailored rigs, YUCP can reuse separable positive/negative blendshape poses already driven by decoded parameters. VRCFury appends the generated controller as a higher-priority Override layer, so YUCP publishes the complete fused value on that verified basis instead of stacking another mouth pose. Owning calibration accepts static 1D or unnormalized Direct mappings whose complete endpoint pose is blendshape-only on the selected face renderer; a multi-blendshape endpoint is one composite basis ray. Coupled 2D trees, shared bindings, bones, materials, other renderers, and geometrically nonlinear multi-frame shapes are not inverted. If a safe decomposition is unavailable, YUCP keeps the conservative direct fallback; use `Outputs Only` when the installed template should remain the sole visual owner. Eye and brow animation remains untouched.

## Documentation

For detailed documentation on each component:
- Visit https://github.com/Yeusepe/Yeusepes-Modules
- Hover over component fields in Unity for tooltips
- Click the "?" icon in component headers for help

## Support

- GitHub Issues: https://github.com/yucp-club/YUCP-Components/issues
- VRCFury Documentation: https://vrcfury.com/

## License

MIT License - See LICENSE.md

Corpus-derived coefficient attribution is in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
