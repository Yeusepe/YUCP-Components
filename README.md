# YUCP Components

Advanced VRChat avatar components with VRCFury integration, distributed via VPM.

![YUCP Components](Website/banner.png)

## 🚀 Features

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
- **Blendshape Auto-Link** - Attach objects to blendshape deformations with multiple solver modes

### Mesh Components
- **Auto Body Hider** - Automatically hide body parts covered by clothing
  - GPU-accelerated detection with multiple algorithms (Raycast, Proximity, Hybrid, Smart, Manual)
  - Poiyomi and FastFur UDIM support with multi-clothing coordination
  - Layered clothing optimization
- **UV Discard Toggle** (Beta) - Merge clothing meshes with UDIM-based visibility toggles
  - Automatic UDIM tile assignment
  - AutoBodyHider integration

### Animation Components
- **Auto Grip Generator** (Beta) - Automatically generate hand grip animations
  - Contact-based mesh analysis
  - Multiple grip styles (Wrap, Pinch, Point)
  - Hand pose generation

### Utility Components
- **Auto UDIM Discard** (Beta) - Auto-detect UV regions for UDIM toggles
- **Avatar Optimizer Plugin** (Beta) - Integration with d4rkAvatarOptimizer
  - Automatic configuration based on avatar complexity
  - Per-avatar optimization settings

### Technical Features
- VRCFury integration for all components
- GPU-accelerated mesh processing
- Content-addressed version control
- Detection result caching
- Custom progress windows
- Automatic icon assignment

## 📦 Installation

### Via VCC (Recommended)

Add this VPM repository to your VRChat Creator Companion:
```
http://vpm.yucp.club/index.json
```

Then install "YUCP Components" from the package list in your project. VRCFury will install automatically.

### Manual Installation

1. Download the latest `.unitypackage` from [Releases](https://github.com/Yeusepe/YUCP-Components/releases)
2. Import into your Unity project
3. Install [VRCFury](https://vrcfury.com/) as a dependency

## 🔧 Usage

### Pakacage Guardian

Access via `Tools > YUCP > Pakacage Guardian`:
- **Unified Interface** - Single window with tabbed navigation and YUCP brand styling
- **Overview Tab** - Repository status, quick actions, and recent activity
- **Commit Graph Tab** - Visual timeline with file changes and diff viewer
- **Stashes Tab** - Manage automatic and manual snapshots
- **Full Diff Engine** - Line-by-line comparison for text files

### Avatar Components

1. Add YUCP components to your avatar GameObjects from `Component > YUCP` menu
2. Configure component settings in the Inspector
3. Build your avatar - components process automatically via VRCFury
4. No manual setup needed - VRCFury handles all integration

For detailed component documentation, see [Packages/com.yucp.components/README.md](Packages/com.yucp.components/README.md)

## 📋 Requirements

- Unity 2022.3 or later
- VRChat SDK3 Avatars (automatically installed via VPM)
- VRCFury >= 1.0.0 (automatically installed via VPM)

## 📚 Documentation

- **Package Documentation**: See [Packages/com.yucp.components/README.md](Packages/com.yucp.components/README.md)
- **VRCFury Docs**: https://vrcfury.com/
- **VPM Guide**: https://vcc.docs.vrchat.com/guides/packages

## 🤝 Support

- **Issues**: [GitHub Issues](https://github.com/Yeusepe/YUCP-Components/issues)
- **VPM Listing**: http://vpm.yucp.club/

## 📄 License

MIT License - See [LICENSE.md](Packages/com.yucp.components/LICENSE.md)

## 🏗️ Development

This repository uses GitHub Actions for automated package building and VPM listing generation.

### Repository Structure
```
YUCP-Components/
├── Packages/
│   └── com.yucp.components/         # Main package
│       ├── package.json             # Package metadata
│       ├── Runtime/                 # Avatar components
│       └── Editor/                  # Editor tools
├── Website/                         # VPM listing website
│   ├── index.html
│   ├── source.json
│   └── ...
└── README.md
```

### Development Workflow

1. **Setup Repository Variable:**
   - GitHub Settings → Actions → Variables
   - Add `PACKAGE_NAME` = `com.yucp.components`

2. **Enable GitHub Pages:**
   - GitHub Settings → Pages
   - Source: **GitHub Actions**

3. **Install VRCFury** (for development):
   - Use VCC, OR add to `Packages/manifest.json`:
```json
"scopedRegistries": [
  {
    "name": "VRCFury",
    "url": "https://vcc.vrcfury.com",
    "scopes": ["com.vrcfury"]
  }
],
"dependencies": {
  "com.vrcfury.vrcfury": "1.0.0"
}
```

4. **Edit code** in `Packages/com.yucp.components/`
5. **Test** in Unity immediately

### Building Releases

Releases are automatically built via GitHub Actions when you:
1. Update the version in `Packages/com.yucp.components/package.json`
2. Run the "Build Release" action

The listing at http://vpm.yucp.club is automatically updated on each release.

### File Organization

| What | Where | Git Tracked |
|------|-------|-------------|
| Package source | `Packages/com.yucp.components/` | Yes |
| Release files | GitHub Releases | No (auto-generated) |
| VPM listing | GitHub Pages | No (auto-generated) |
| Development assets | `Assets/` | No (Unity workspace) |
| Website | `Website/` | Yes (optional) |

---

**Made with ❤️ by YUCP Club**
