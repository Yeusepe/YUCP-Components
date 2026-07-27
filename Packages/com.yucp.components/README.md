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
- **Viseme Phrase Trigger** - Learns the wearer's visual pronunciation of up to four short phrases from four microphone takes, then bakes a compact duration-aware matcher into the avatar.
  - Stores timestamped `Viseme`/`Voice` traces only; microphone audio is never written to the enrollment asset.
  - Uses VRChat's hard viseme identity after a local 30 ms categorical run filter, with Advanced Viseme Reconstructor speech boundaries; reconstruction smoothing does not delay token matching.
  - Publishes a public timed match pulse on every client while using exactly one hidden synced bit per phrase.
  - Supports reusable prefabs: the prefab supplies prompts and output names, while each avatar creator records their own enrollment.

### Parameter Memory
- **Parameter Compressor** - Compresses safe persistent settings from the final merged avatar into one small shared network transport.
  - Automatically recognizes menu toggles and radial settings while protecting buttons, contacts, PhysBones, random/additive drivers, and live puppet axes.
  - Uses 3-7 synchronized Bool wires with a delay-insensitive constant-weight alphabet, explicit framing, atomic block commits, and continuous late-join repair.
  - Keeps the wearer's original parameters immediate, full precision, and saved; only hidden carriers are synchronized and unsaved.
  - Adds one zero-weight state-machine layer with no BlendTrees and never modifies source controllers or parameter assets.

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

#### Parameter Compressor

Add one `YUCP/Parameter Compressor` anywhere below the avatar descriptor. In Simple mode, leave automatic selection enabled, choose how much parameter space to keep free, and move the preference toward faster updates or a smaller transport. The build-time summary reports the final before/after cost and nominal reference refresh. Advanced mode can force a stable parameter in or out, choose its precision and range, change update priority, and place related values in the same transaction group.

The processor runs after VRCFury has produced the real merged menu, Expression Parameters asset, and FX controller. It compresses only persistent state that it can classify safely. Momentary buttons, submenu gates, contacts, PhysBones, random/additive driver outputs, conflicting types, existing carriers, OSC-like unowned inputs, and continuous puppet axes remain direct unless an applicable safe override exists. Original parameters retain their names, defaults, Saved behavior, and full local values; the build clone marks selected declarations unsynced and adds unsaved carrier Bools. Other prefab controllers and VRCFury BlendShape Links continue reading the original parameter names.

With `B` Bool wires, the conservative transport uses every word with Hamming weight `floor(B/2)` and an all-zero spacer. Torn wire updates have a lower weight and therefore cannot become another valid symbol. One word is reserved for synchronization, leaving radix `C(B,floor(B/2))-1`. Parameter identity and quantized value share one enumerative interval instead of paying separate index bits. Six wires therefore provide radix 19; `26 * 255 = 6630 < 19^3 = 6859`, so 26 native VRChat Float domains fit in three payload digits. Background snapshots omit repeated identities, stage values privately, and publish a complete block only at a valid closing frame. The design follows [delay-insensitive unordered codes](https://past.date-conference.com/proceedings-archive/2011/DATE11/PDFFILES/11.3_2.PDF), [Sperner's optimal antichain bound](https://ocw.mit.edu/courses/18-318-topics-in-algebraic-combinatorics-spring-2006/resources/sperner/), and [successive-refinement](https://isl.stanford.edu/~cover/papers/transIT/0269equi.pdf) principles. VRChat's Playable synchronization is slow and not lossless, so the controller rejects partial frames and replays snapshots forever rather than relying on pulse timing.

For `Advanced Viseme Reconstructor`, add the component anywhere under the avatar descriptor, assign the face renderer or use the descriptor renderer, and optionally create a reusable reconstruction profile. The default `Auto` input mode reuses a compatible decoded/proxy Unified Expressions stream when its Animator controller and synced tracking-active declaration are referenced under the avatar. If none exists, `Auto` stays speech-only and adds **zero face-tracking bits**. On avatars without a Parameter Compressor, Compact Synced avatar-menu sharing retains its fixed 13-bit private transport. When a Parameter Compressor is present, those saved tuning values join the generic shared plan instead, allowing a six-wire build when only six bits remain. Choose Balanced8, Quality12, or FullTongue18 only when this component should create new tracking inputs. Use `Outputs Only` to consume the generated `YUCP/AdvancedViseme/...` parameters without changing the avatar's mouth configuration.

#### Viseme Phrase Trigger

First keep one Advanced Viseme Reconstructor on the same avatar, then add the Viseme Phrase Trigger component to the avatar or to any prefab below it. Add a short prompt and stable parameter key for each phrase. `Natural Speech` may match inside a longer utterance; `Paused Command` additionally requires silence before and after the enrolled phrase. Leave the AVR source prefix blank when the avatar contains exactly one reconstructor, or select it explicitly when several output-only reconstructors exist.

Each avatar creator presses **Record / Improve** once and says the prompt four times. The overlay starts the selected microphone, detects when each utterance really ends, saves it, and advances automatically; the microphone can still be changed before speech begins. Any saved take can be selected and recorded again without discarding the old take until its replacement succeeds. **Skip for now** closes the guide without deleting completed work. Enrollment uses the same Oculus LipSync analyzer family as the Viseme Test Emulator when the licensed plugin is installed, while the fallback analyzer is clearly marked approximate. Audio is never stored. An optional ordinary-speech recording gives the baker negative examples for a safer threshold. A build with missing, stale, mutually inconsistent, or indistinguishable registered phrases stops before AVR generation and reopens enrollment instead of uploading a detector that cannot work.

VRCFury's normal Play Mode preprocessing also checks enrollment. If a phrase still needs teaching, YUCP returns to Edit Mode and opens the guide against the real avatar rather than recording into a temporary Play Mode copy. Completing the guide continues the requested preview. Skipping continues that preview with phrase recognition disabled; upload and build validation are never bypassed.

At build time, constrained [Soft-DTW](https://proceedings.mlr.press/v70/cuturi17a.html) consistency and hard multi-take alignment are reduced to a bounded, duration-aware Animator token automaton; no runtime scripts, neural model, microphone application, OSC companion, or runtime DTW is included in the avatar. The baker protects every recorded pronunciation as a complete correlated path, then adds a small order-two context lattice so a prefix from one take can combine with a locally compatible suffix from another. A log-median timing profile fills the natural cadence between compatible takes without taking an unsafe Cartesian union of every phone's limits. The generated Animator uses alphabet-pruned, driver-free `(committed, candidate)` states to reproduce enrollment's 30 ms categorical cleanup without averaging viseme IDs. A changed winner is recorded immediately, is published at the first Animator evaluation on or after the 30 ms wall-clock threshold, and disappears cleanly if it was only classifier flicker; rapid `A-B-C` speech advances directly in one transition. Only labels used by the enrolled phrases plus one published `Other` class are generated, while each unused raw winner retains its own probation clock. At frame intervals above 30 ms, the Animator deliberately preserves a one-sample run because its true sub-frame duration is unknowable. Runtime timing corridors receive one phrase-wide 30 ms observation allowance before negative calibration instead of multiplying uncertainty per phone. Observer proof satisfies learned minima at or below 30 ms; longer minima are enforced unchanged. The generated controller adds no uncalibrated timing grace. Quarantine waits for a different committed category, routes a fresh Natural Speech root directly into matching, and otherwise returns to Ready; Paused Commands retain their silence boundary.

The weighted recognition math is compiled into the automaton rather than interpolating the raw `Viseme` number in a Blend Tree. Oculus visemes are categorical labels, so a numeric midpoint between `PP` and `FF` is not another meaningful sound. [Advanced Blend Trees](https://vrc.school/docs/Other/Advanced-BlendTrees/) are excellent for continuous arithmetic and remapping, but sequence order, failure links, and duration memory remain in the timed automaton; this also avoids adding frame-time integration layers merely to recover information VRChat does not expose. The public `Confidence` value is the quantized remaining weighted-path margin, not a fabricated interpolation of adjacent viseme IDs.

When at least three different enrollment paths demonstrate real classifier variability, the baker may add at most two weighted one-confusion paths. Each changes exactly one interior winner among conservative visual neighbours (`aa/E/I`, `O/U`, `CH/SS`, or `DD/nn`), preserves the complete source path's order and timing, and pays an explicit confidence cost. Two simultaneous guesses, arbitrary insertions, boundary substitutions, and unrestricted vowel products remain impossible. Recorded ordinary speech vetoes an inferred path that loses the required negative margin. If the shared 32-state budget is tight, generic confusion guesses are removed before personalized context bridges, and all inferred paths are removed before any creator-recorded pronunciation. Matching runs only for `IsLocal`; remote avatars keep the detector idle and merely decode the network event edge. This stabilizer and all diagnostic parameters are Animator-only and add zero synced bits. With component prefix `{prefix}`, public parameter key `{key}`, and stable enrollment ID `{id}`, the output contract is:

- `{prefix}/{key}/Matched`: global, controller-local Bool pulse, normally held for 0.25 seconds. VRCFury BlendShape Links, toggles, and prefab controllers should consume this parameter.
- `{prefix}/{key}/Confidence`: unsynced local Float showing the live remaining acceptance-budget margin while a candidate path is active. Its final value is held through local acceptance, then reset when the matcher returns to ready.
- `{prefix}/{key}/Progress`: unsynced local Float showing progress through the surviving candidate path.
- `{prefix}/_Network/{id}`: hidden, unsaved, synced Bool event carrier. It costs exactly one bit and must not be used as the public pulse.

The carrier changes stable state once per accepted match. Every client detects either edge and creates its own full-length `Matched` pulse, avoiding loss through VRChat's slower Playable synchronization. A 1.25-second initialization window learns the current carrier state without firing after a late join, and every phrase has at least a 1.25-second cooldown. An avatar may contain four phrases and 32 logical shared matcher states. Natural accent and coarticulation differences inside the four approved takes are retained automatically. Enrollment asks for another take or a more distinctive trigger only when the evidence cannot produce a safe bounded language—for example inconsistent or indistinguishable traces, insufficient negative margin, prefix ambiguity, the 12-run path cap, or the avatar-wide 32-state budget. Phrase matching is visual and personalized, not semantic speech recognition: [viseme ambiguity](https://arxiv.org/abs/1805.02934) means homophenes and unregistered look-alike phrases can still match, so do not use it for safety-critical or irreversible actions.

To inspect the raw input before building, keep the Viseme Test Emulator on the avatar, enter Play Mode, and use Gesture Manager to verify `Viseme`, `Voice`, and Advanced Viseme speech-boundary parameters from the same microphone. Once enrollment is ready, continue into Play Mode and test each phrase at normal, slower, and faster pacing, then try ordinary speech and registered look-alike phrases as negatives. Remote edge behavior and late-join suppression still require a VRChat client test because Unity preview has no Playable parameter network.

The component must be a child of the same object hierarchy as the `VRCAvatarDescriptor`; scene-root components are intentionally ignored by avatar preprocessing. When `Drive Lower Face` owns lip sync, remove any separate VRCFury Advanced Visemes component from that avatar so only one system owns the mouth. Existing VRCFT templates and VRCFury BlendShape Links are supported and should remain installed.

The inspector opens in a novice-friendly `Simple` tab with lossless controls for expression strength, speech liveliness, quiet speech, reaction speed, pause stability, pronunciation, face-tracking priority, and inferred tongue motion. `Speech Liveliness` makes speech-only shapes quicker and more distinct by moving the visible result toward the existing fast observer; it fades continuously to zero when face tracking is active and never drives outside the authored viseme hull. Moving a slider for the first time creates the reusable profile automatically; simply viewing the component does not create an asset. `Advanced` retains the complete motion, threshold, mapping, calibration, parameter, and diagnostic controls. Its `Fine-tune Visemes` card presents the 15 sounds as friendly chips and gives each sound independent Jaw & Opening, Lips, and Tongue percentages, with individual axes under Precise Controls. For example, reducing only `R > Jaw & Opening` leaves every other viseme and all R tongue detail unchanged. These trims are baked into the existing articulation matrix and add no Animator parameters or runtime BlendTrees. Both tabs edit the same profile values, so switching views never rewrites or layers hidden presets.

`Avatar Menu` can generate saved radial sliders under `YUCP/Viseme Settings`. The generated root contains a one-page `Simple` menu with friendly names—including `Speech Liveliness`—and an `Advanced` branch containing the existing Speech, Tracking, Phonetics, and Tongue submenus. Both branches point to the same full-precision local Float parameters. Without the generic Parameter Compressor, `Share Settings` retains the established one-Int/five-Bool private transport for a fixed **13 synced bits**. With the generic component, AVR skips that private layer and registers the same saved Float settings with the shared avatar-wide transport; a six-Bool plan can preserve their native 255 remote levels when six bits remain. In both cases the wearer remains immediate and unquantized, hidden carriers are unsaved, and background replay repairs packet loss and fills late joiners. `Local Only` keeps zero-bit behavior. `Silence Stability` controls the causal speech-memory release: the center uses the profile value, zero is an exact bypass, and the upper half retains an established talkspurt longer. Profile defaults remain the deterministic behavior seen where a local menu preference is unavailable.

Runtime controls modify separate terms instead of stacking another mouth pose. Speech and tracking smoothness interpolate among frame-rate-correct observer poles; voice sensitivity changes expressive energy; silence stability changes only how strongly an established talkspurt can resist a transient `sil`; remote trust affects only remote fusion; Tracked Surface Yield and Authored Detail control the measured and complementary parts of calibrated residuals; phonetic controls scale PP/FF/sibilant projections; and tongue controls scale only synthesized speech/inference axes. A settled active local face-tracking measurement still has exact authority on its own coordinate, so an open tracked mouth is not pulled by an `aa` viseme and native tongue tracking is not weakened by a tuning slider.

The silence decision is causal and deliberately asymmetric, adapting the short/long overhang used by [WebRTC VAD](https://webrtc.googlesource.com/src/+/refs/heads/master/common_audio/vad/vad_core.c) and the burst-confirmation plus hangover pattern in [3GPP/ETSI VAD](https://www.etsi.org/deliver/etsi_ts/146000_146099/146032/11.00.00_60/ts_146032v110000p.pdf). A leaky observer updates speech history with `h <- h + (1-exp(-dt/tau)) (target-h)`: non-`sil` charges it quickly and `sil` releases it at the configured rate. Its stored center and endpoint responses are frame-rate-correct; intermediate upper-half menu values continuously interpolate those coefficients. A continuous linear 0.35-to-0.55 history ramp turns that memory into hold authority, so a long talkspurt earns more protection than a short sound without an Animator countdown or feedback state machine. `Voice` is deliberately excluded from this decision and therefore cannot pin the mouth; it still drives live speech energy, onset, release, and expressive amplitude. At full confidence the existing fast simplex and its already-computed speech-pose gain are frozen, while partial confidence blends continuously into the normal release and the slow stage keeps settling. This retains the pose that was actually present instead of forcing quiet speech larger. Any real non-`sil` index bypasses the hold immediately. All 15 weights remain nonnegative and sum to one, face tracking continues updating, and genuine silence converges normally to the authored `sil` pose.

The component cannot recover the original Oculus classifier weights because VRChat exposes only the winning `Viseme` index. Instead, it publishes an interruptible, frame-rate-correct continuous estimate. With the default prefix, the unsynced global output contract is:

- `YUCP/AdvancedViseme/Viseme/{sil,PP,FF,TH,DD,kk,CH,SS,nn,RR,aa,E,I,O,U}` for the normalized 15-weight reconstruction.
- `YUCP/AdvancedViseme/Articulation/{JawOpen,LipClose,MouthOpen,LipFunnel,LipPucker,LipSuck,SmileSad,LipBite,TongueOut,JawX,JawZ,MouthX,TongueY,TongueX,TongueRoll,TongueArchY,TongueShape,TongueTwistRight,TongueTwistLeft}` for the complete reconstructed lower-face basis.
- `YUCP/AdvancedViseme/Velocity/...` for signed articulator velocity and `Speech/{Energy,Onset,Release,Talking,TrackingBlend,Vowel,Bilabial,Labiodental,Sibilant,Coronal,Dorsal,Rhotic,TongueContact,M,N}` for speech-state and phonetic evidence.
- In Beta mode with compatible jaw/aperture tracking, `YUCP/AdvancedViseme/Speech/Hypothesis/{M,N,Confidence}` reports the confidence-gated hidden-phone posterior used by the tongue prior. These local Animator outputs are not synced inputs.

`Speech/M` and `Speech/N` are compatibility evidence, not phoneme classifiers. `M` reports evidence compatible with the merged Oculus `PP` class (`p`/`b`/`m`), while `N` reports evidence compatible with the merged `nn` class (`n`/`l`); VRChat's winning viseme index cannot recover the distinctions inside either class.

Beta's `Speech/Hypothesis` output is deliberately narrower and probabilistic. It combines the causal Oculus observation history with measured jaw opening and lip aperture to estimate whether the ambiguous bilabial/lingual evidence is more compatible with `/m/` or `/n,l/`. A visibly held closure can therefore recover an `/m/` that Oculus emitted as `nn`, while an open or lingual face can retain the `nn` tongue prior. The estimate only redistributes the hidden `PP`/`nn` mass used by tongue-tip and tongue-body synthesis: it never changes the public 15-weight simplex, stacks an extra visible mouth pose, or overrides measured jaw and lip channels. Low reliability, weak speech, tracker loss, contradictory motion, or out-of-distribution features fade the correction smoothly back to the original reconstruction.

All articulation outputs exist in every mode; the tracking preset controls only which axes are measured. Missing measurements remain reconstructed from speech instead of being replaced with zero.

Whenever runtime face-tracking authority is absent, both reconstruction modes replace the hard winner's one-hot target with a duration-conditioned Oculus shape estimate. Five corpus-fitted probability-simplex controls describe a 224 ms positive trajectory: the first four form a 168 ms cubic Bezier and the fifth extends it with a 56 ms linear tail. Three Unity Hermite keys reproduce those two segments exactly. The 15 existing winner states use a 72 ms fixed-duration, destination-interruptible crossfade, so rapid `A -> B -> A` and `A -> B -> C` changes blend from the live in-flight pose instead of restarting from a static target. Every curve and every transition is a convex combination of normalized nonnegative vectors.

The no-tracker renderer consumes that fitted trajectory directly. It no longer hides the learned motion behind the older two-pole speech observer, which was mathematically smooth but visually reproduced the hard winner's hold-and-switch cadence one observer delay later. Linear calibrated articulation and residual detail are commuted through the same decoder epoch, preserving `U(Cp) + Rp = Vp` without modifying a mesh. The observer/fusion path is retained for active face tracking, and the exact full-authority tracking endpoint is unchanged.

This remains a causal conditional estimate: VRChat's hard `Viseme` index discards the original 15 Oculus weights, so the native curve cannot be recovered exactly and the model does not predict a future phoneme. The design follows research showing that realistic speech needs continuous viseme-space trajectories and phonetic-context motion rather than independent frame targets ([Kshirsagar and Magnenat-Thalmann](https://www.isca-archive.org/avsp_2001/kshirsagar01_avsp.html), [Bao et al.](https://arxiv.org/abs/2301.06059), [Kim and Kim](https://arxiv.org/abs/2507.20568)). It adds no synced bit or mesh operation. Hard `Viseme` identity still owns speech history, silence decisions, phonetic constraints, and Beta context.

`Normal` remains the default and uses the established two-pole viseme observer after the shared silence-decision stage. `Beta Coarticulation` is explicitly separate: it uses the same silence decision, then a corpus-timed context simplex and independent learned carryover for jaw, lips, tongue tip, and tongue body. Both the previous-context and destination axes are mixed continuously, so a hard VRChat index change cannot switch between trained transition-table columns in one frame. The checked-in `4 x 15 x 15` table was fitted from 13,325 transitions in the [SPIRE EMA Corpus](https://huggingface.co/datasets/SpireLab/SPIRE_EMA_CORPUS/tree/55f21628de95514e3ff22eaccc75e1547d181297) and improved held-out transition-window MSE by 9.803% overall.

When compatible visible tracking is active but native tongue data is absent, Beta also applies a compact corpus-trained residual estimator. Balanced uses jaw opening, lip aperture, and lip protrusion; Quality is a separate fit that additionally uses jaw advance (`JawZ`/`JawForward`, never lateral `JawX`). It predicts only tongue-tip advance and height. Relative to the viseme-only residual MSE inside held-out SPIRE data, the reductions are 9.914%/13.340% for Balanced and 14.594%/15.147% for Quality; these percentages are not absolute reconstruction accuracy. Exactly one 24 ms observer starts from unfiltered calibrated visible semantics; feeding the estimator an already smoothed tracking stream changes its trained dynamics. Every Animator factor is normalized by a generated conservative envelope, preserving the trained affine model without intermediate BlendTree clipping. The prediction is composed through the remaining authored headroom, capped to 30% for visible `TongueOut` and 65% for `TongueY`, suppressed by closures and out-of-distribution input, and automatically yields to measured tongue tracking. SPIRE EMA and Unified Expressions are unpaired domains, so these held-out scores are corpus-domain evidence—not measured VRCFT accuracy or native tongue tracking. It never fabricates lateral motion, roll, twist, or asymmetry from this midsagittal corpus, and it adds no parameters or menu controls.

The hidden `/m/` versus `/n,l/` posterior has Aperture, Balanced, and Quality fits, so a tailored template only needs reusable `JawOpen`, `MouthOpen`, and `MouthClosed` semantics to participate; protrusion and jaw advance select richer fits when present. Training ran the installed Oculus LipSync 1.54 Enhanced provider over the paired SPIRE audio, then simulated the exact Beta observer at 100 Hz. On unseen speakers and sentences, the three models reached F1 scores of 0.9242, 0.9229, and 0.9168 respectively, and recovered 39–40 of 50 true `/m/` frames whose hard Oculus winner was `nn`. Those are corpus-domain compatibility results, not proof of phoneme recovery on live VRCFT. `/p/` and `/b/` share the same visible closure and reduce `PP` reliability; the component does not invent `p/b`, `t/d`, `k/g`, `s/z`, or `n/l` distinctions.

On a mesh that supports residual calibration, YUCP also extracts one build-only signed hidden-detail morph `H = (I - P_U)(V_PP - V_nn)`. `P_U` projects onto every verified driven articulator pose, so the posterior can transfer the authored PP-versus-`nn` tongue/interior difference without moving any jaw or lip coordinate represented by the tracking basis. The correction is `confidence * (posteriorPP - originalPP) * H`, adds no parameter, and is exactly zero when the model abstains. Because `H` is already orthogonal to the visible tracking basis, ordinary jaw or lip disagreement cannot erase it. Compatible reused templates are calibrated from their actual composite pose clips, including clips that drive several blendshapes as one semantic axis. The source mesh remains untouched; only a build clone receives the residual shapes.

The corpus ARPAbet-to-viseme mapping is a training surrogate for VRChat's hidden classifier, not recovered VRChat data. Both Beta models are causal and experimental; they cannot recover distinctions destroyed by VRChat's single winning index. The reproducible pinned-data pipeline, generated coefficient hashes, split policy, and full limitations are under `Tools/AdvancedVisemeTraining`.

The hidden-phone fit is pinned to the default 24 ms viseme observer. If a custom profile changes that upstream response, a log-domain compatibility gate fades the posterior toward abstention as the phase leaves the trained regime; it does not evaluate mismatched timing at full confidence.

Tracking is complementary replacement rather than additive pose stacking. Every observed jaw or lip coordinate follows a One-Euro-inspired adaptive fast/slow observer—not an exact 1€ filter—so small OSC or quantization motion settles onto the stable two-pole path while deliberate motion selects the low-lag one-pole path. Once active local tracking has settled, every measured axis has exact precedence whether or not it agrees with the speech prior. For a calibrated rig, write the authored residual as `R = V - U C` and solve signed projection coefficients `A` such that the tracker-reproducible part of each residual lies in the `U` span. With reconstructed speech weights `p`, fused articulators `z`, per-axis tracking authority `G`, authored detail `d`, and Tracked Surface Yield `lambda`, the generated mesh evaluates `U z + d R p - d lambda U G A^T p`. At the normalized full-detail endpoint (`d = 1`) with speech owning the articulators (`G = 0` and `z = C^T p`), Normal mode exactly restores every authored viseme and convex blend. With full identifiable tracking authority and `lambda = 1`, only residual motion reproducible by those measured coordinates yields; tongue, teeth, mouth-interior, and other complementary detail remains. Independent full-rank axes yield independently, while a rank-dependent group conservatively uses its weakest participating authority. The signed geometry is encoded as at most two build-only ±geometry carriers per retained ray, both driven with normalized nonnegative 0..100 weights so VRChat's final blendshape clamp cannot change the result. Tracker confidence fades through startup and loss instead of switching controllers.

For a compatible controller-only pose proxy, the visible local axis is evaluated directly from that template parameter while tracking is active. AVR does not smooth the proxy again or render through its multi-stage public diagnostic path; speech fallback and residual detail are composed around the native pose. The public articulation, velocity, and speech parameters are mirrors for creators and are not read back to render the tracked face. Vectorized observer, projection, and residual trees keep mathematically independent scalar channels in one Animator evaluation stage, and linked-renderer residual curves with the same coefficient are emitted in one composite clip. Beta's visible-tongue mixture is evaluated as an exact viseme-by-latent tensor contraction, and its hidden `PP <-> nn` update is applied as the rank-one correction `delta * (a_PP - a_nn)` instead of rebuilding and reprojecting two extra 15-weight simplexes. All frame-rate alpha outputs share one sampled vector lookup. These transformations are algebraically exact; they reduce Animator work and dependency latency without approximating Normal or Beta reconstruction.

`Phonetic Assist` applies only three sparse physical constraints through monotone soft projections: bilabial closure for `PP`, labiodental contact for `FF`, and a sibilant jaw ceiling for `SS`/`CH`. Each projection yields on its target axis when that axis has a settled active local measurement; it remains available for missing measurements and conservative remote fusion. `Tracker Authoritative` skips those rules. No generic post-fusion MouthOpen/MouthClosed or Pucker/Suck clamp is imposed on a tailored template. Build-time box-constrained decomposition (`0 <= C <= 1`) creates residual shapes on a generated mesh clone from either mapped articulator shapes or verified composite template poses. Geometry beyond a basis shape's usable 100% endpoint stays in the residual, so VRChat's final clamp cannot change the calibrated result. Normal-mode convex viseme blends and every steady authored viseme remain equal to the source pose within floating-point tolerance when speech owns the face. Beta deliberately allows different articulator groups to take different bounded paths between those exact endpoints. The source mesh is never modified; generated assets are staged and avatar mesh assignments are rolled back if the build cannot complete.

VRCFury BlendShape Link is handled as part of the same build-time decomposition. YUCP verifies the installed feature schema, preserves direct one-to-many and tailored mappings, and generates renderer-local residual/carrier namespaces. The primary face curves and public parameter contract do not change because a link exists; VRCFury continues copying the original articulator curves, while YUCP adds only the target-local detail needed to reproduce that renderer's authored visemes. Root-level targets are valid. Because installed VRCFury applies each link feature once, an indirect chain carrying authored visemes is rejected with a request to link that renderer directly to the primary face; this prevents hierarchy/component order from changing speech. Ambiguous Animator paths, nonlinear multi-frame geometry, competing incoming writers, many-to-one mappings, unsupported schemas, or mappings that change after mesh generation also fail closed when authored speech would otherwise be lost. Unsigned articulation-only links remain native; a mapped signed axis receives only the target-local inverse carrier needed to preserve its negative half. No extra expression parameters or synced bits are introduced.

Tracking encoding defaults to `Adaptive Binary`: 2-4 magnitude bits are allocated per channel according to perceptual importance, plus sign bits only for signed channels. It uses 25 bits for Balanced8, 39 for Quality12, or 57 for FullTongue18 including tracking-active and menu-toggle bits. `Uniform 4 Bit Binary` uses 35/55/82 bits. `Full Float` remains available for maximum input precision at 66/98/146 bits. Binary inputs follow VRCFaceTracking's `Parameter1`, `Parameter2`, `Parameter4`, and `ParameterNegative` naming and are decoded into smooth local float parameters by the generated FX controller.

The learned transition tables remain full precision: their measured matrices are full-rank, and rounding their constants alone would not reduce Unity Animator work because animation curves are still evaluated as floats. Runtime quantization is therefore reserved for the network wire where it actually saves avatar parameter memory. The one internal lookup that benefits from fewer samples—the hidden-phone logistic—uses a 13-knot symmetric minimax approximation with a tested worst-case error below `0.0054`, smaller than the previous 19-knot table's error.

Budget validation first unions the avatar's referenced expression assets with every Advanced Viseme component, then rechecks the final merged descriptor near the end of preprocessing. Parameters generated later by VRCFury, Modular Avatar, or another feature therefore cannot silently push an otherwise valid early estimate beyond VRChat's 256 synced-bit limit. An existing same-name wire parameter is reusable only when its type and network-sync contract match; controller-only decoded proxies remain zero-cost.

Existing-installation compatibility is capability-based rather than template-specific. The builder scans parameter assets and Animator controllers referenced anywhere under the avatar, ranks `/v2/` float sources by semantic channel coverage, prefers controller-only decoded/proxy outputs over raw OSC inputs, and permits an explicit prefix when several candidates tie. It recognizes root and same-prefix `ExpressionTrackingActive`/`LipTrackingActive` declarations, including the Bool-on-wire/Float-in-Animator convention used by Jerry and Pawlygon templates; a genuine Animator Bool is converted to a private float safely. Another installation's prefixed activity gate is never borrowed, and conflicting Animator types are rejected. Partial templates fuse only channels they actually contain.

If the selected existing prefix also exposes a Float `SoftPalateClose`, Beta reuses it opportunistically at zero parameter cost. A sustained nonzero signal is required before capability latches, and it is treated only as oral/closed-palate evidence that can lower an unsupported nasal correction. YUCP never creates or syncs this optional channel, never borrows it from another prefix, and never treats nostril or nose blendshapes as nasality sensors.

For tailored rigs, YUCP can reuse separable positive/negative blendshape poses already driven by decoded parameters. VRCFury appends the generated controller as a higher-priority Override layer, so YUCP publishes the complete fused value on that verified basis instead of stacking another mouth pose. Owning calibration accepts static 1D or unnormalized Direct mappings whose complete endpoint pose is blendshape-only on the selected face renderer; a multi-blendshape endpoint is one composite basis ray. Coupled 2D trees, shared bindings, bones, materials, other renderers, and geometrically nonlinear multi-frame shapes are not inverted. A direct fallback is used only when its correction geometry is clamp-safe; otherwise the build fails clearly instead of emitting negative blendshape weights that VRChat would silently clamp. Use `Outputs Only` when the installed template should remain the sole visual owner. Eye and brow animation remains untouched.

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
