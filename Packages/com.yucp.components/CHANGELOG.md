# Changelog

All notable changes to YUCP Components will be documented in this file.

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
- Auto Grip Generator (Beta)
- Symmetric Armature Auto-Link
- Closest Bone Auto-Link
- View Position & Head Auto-Link
- Auto UDIM Discard
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

