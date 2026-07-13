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

For `Advanced Viseme Reconstructor`, add the component anywhere under the avatar descriptor, assign the face renderer or use the descriptor renderer, and optionally create a reusable reconstruction profile. The default `Auto` input mode reuses a compatible decoded/proxy Unified Expressions stream from existing VRCFury or Modular Avatar installations. When none is found it generates the optimized Balanced8 inputs and a `YUCP/Face Tracking` toggle. Use `Outputs Only` to consume the generated `YUCP/AdvancedViseme/...` parameters without changing the avatar's mouth configuration.

The component cannot recover the original Oculus classifier weights because VRChat exposes only the winning `Viseme` index. Instead, it publishes an interruptible, frame-rate-correct continuous estimate. With the default prefix, the unsynced global output contract is:

- `YUCP/AdvancedViseme/Viseme/{sil,PP,FF,TH,DD,kk,CH,SS,nn,RR,aa,E,I,O,U}` for the normalized 15-weight reconstruction.
- `YUCP/AdvancedViseme/Articulation/{JawOpen,LipClose,MouthOpen,LipFunnel,LipPucker,LipSuck,SmileSad,LipBite,TongueOut}` for the lower-face basis.
- `YUCP/AdvancedViseme/Velocity/...` for signed articulator velocity and `Speech/{Energy,Onset,Release,TrackingBlend}` for speech-state signals.
- Quality12 additionally publishes `Articulation/{JawX,JawZ,MouthX,TongueY}` and their velocity parameters.

`Phonetic Assist` preserves bilabial closure, labiodental contact, and sibilant jaw limits while tracking is blended in. `Tracker Authoritative` follows calibrated tracking directly. Tracking confidence fades through startup and loss instead of switching controllers. When the face mesh contains compatible articulator shapes, build-time nonnegative decomposition creates residual shapes on a generated mesh clone so every authored viseme and convex viseme blend remains equal to the source pose within floating-point tolerance. The source mesh is never modified.

Tracking encoding defaults to `Adaptive Binary`: 2-4 magnitude bits are allocated per channel according to perceptual importance, plus sign bits only for signed channels. `Uniform 4 Bit Binary` uses 35 bits for Balanced8 or 55 bits for Quality12. `Full Float` remains available for maximum input precision at the original 66/98-bit cost. Binary inputs follow VRCFaceTracking's `Parameter1`, `Parameter2`, `Parameter4`, and `ParameterNegative` naming and are decoded into smooth local float parameters by the generated FX controller.

Existing-installation compatibility is capability-based rather than template-specific. The builder scans parameter assets and Animator controllers referenced anywhere under the avatar, ranks `/v2/` float sources by semantic channel coverage, prefers controller-only decoded/proxy outputs over raw OSC inputs, and permits an explicit prefix when several candidates tie. Missing channels fall back to reconstructed speech. For tailored rigs, YUCP can extract positive and negative lower-face poses from parameter-driven animation clips, retain explicit profile overrides, and rebind matching custom blendshape curves to the selected face renderer. Its lower-face controller is appended after discovered template controllers so their eye and brow animation remains intact.

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
