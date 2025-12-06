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

### Mesh Components
- **Auto Body Hider** - Automatically hide body parts covered by clothing
  - GPU-accelerated detection
  - Multiple detection algorithms (Raycast, Proximity, Hybrid, Smart, Manual)
  - Poiyomi and FastFur UDIM support with multi-clothing coordination
  - Layered clothing optimization

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

