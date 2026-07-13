# Changelog

All notable changes to YUCP Components will be documented in this file.

## [Unreleased]

### Added
- Viseme Test Emulator component with microphone selection, automatic Play Mode execution, continuous 15-weight Oculus descriptor output, dominant `Viseme`/continuous `Voice` parameter driving, Mouth & Jaw Tracking Control suppression, Gesture Manager integration, manual testing, and automatic Oculus LipSync plugin support.
- Custom Object Sync grouping system: add a Group ID field per component and automatically merge matching components into a single VRLabs Custom Object Sync rig to reduce parameter usage.
- Group-aware editing: changing the settings on any component automatically propagates the new values to every member of the same group, keeping builds consistent.
- Parameter budget now reflects the actual number of objects in the current group and surfaces the calculated sync cost plus group size summary.

### Changed
- Custom Object Sync inspector now uses the standard YUCP styling, refreshed parameter budget card, and an updated grouping section that explains the new workflow.
- Custom Object Sync inspector flow reorganized with summary + card-based sections for easier tuning.
- Max Radius control now shows the computed meter range inline and explains the trade-off between coverage and parameter cost.
- Added an optional Scene view gizmo that visualizes travel radius, precision, and rotation when selecting a Custom Object Sync component.
- Custom Object Sync grouping is now opt-in via an “Enable Grouping” toggle; leave it off to mirror the original VRLabs per-object workflow, or enable it to share rigs intentionally.

### Removed
- Auto Grip Generator component, editor tooling, and preprocessing pipeline.

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

