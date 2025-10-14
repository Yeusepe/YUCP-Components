# YUCP Components - VRChat Package

Advanced VRChat avatar components with VRCFury integration, distributed via VPM (VRChat Package Manager).

## For Users

### Installation

Add this VPM repository to your VRChat Creator Companion:
```
http://vpm.yucp.club/index.json
```

Then in VCC:
1. Open your avatar project
2. Click "Manage Project"
3. Find "YUCP Components"
4. Click "+" to install
5. VRCFury will install automatically

### Components Included

- **Symmetric Armature Auto-Link** - Auto-attach to left/right body parts
- **Closest Bone Auto-Link** - Find nearest bone (including extra bones)
- **View Position & Head Auto-Link** - Position at avatar view position
- **Auto Body Hider** - Hide body parts covered by clothing (GPU-accelerated)
- **Auto Grip Generator** (Beta) - Generate hand grip animations
- **Auto UDIM Discard** (Beta) - Auto-detect UV regions for UDIM toggles

## For Developers

### Quick Setup

1. **Set GitHub Repository Variable:**
   - Settings → Secrets and variables → Actions → Variables
   - Name: `PACKAGE_NAME`
   - Value: `com.yucp.components`

2. **Enable GitHub Pages:**
   - Settings → Pages
   - Source: **GitHub Actions**
   - Custom domain: **vpm.yucp.club** (optional)

### Package Structure

```
Packages/com.yucp.components/
├── package.json              # Package metadata + VRCFury dependency
├── README.md                 # Package documentation
├── LICENSE.md                # MIT License
├── CHANGELOG.md              # Version history
├── Runtime/                  # Components that run on avatars
│   ├── *.asmdef             # References VRCFury
│   └── Components/          # All component scripts
└── Editor/                   # Editor-only scripts
    ├── *.asmdef             # References VRCFury Editor
    ├── Components/          # Custom inspectors
    ├── MeshUtils/           # Mesh processing utilities
    ├── UI/                  # Custom UI windows
    └── Resources/           # Icons, fonts, styles
```

### Development Workflow

1. **Install VRCFury in Unity** (for development):
   - Use VCC to add VRCFury to this project, OR
   - Add to `Packages/manifest.json`:
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

2. **Edit code** in `Packages/com.yucp.components/`
3. **Test** in Unity immediately
4. **Update version** in `package.json` before release

### Publishing

1. Update `package.json` version (e.g., `"version": "0.1.1"`)
2. Commit and push changes
3. Go to GitHub → Actions → "Build Release" → Run workflow
4. Wait for automation to complete
5. Your VPM URL is ready: `http://vpm.yucp.club/index.json`

## Package Details

### Dependencies
- VRCFury >= 1.0.0 (auto-installed)
- VRChat SDK3 Avatars (auto-installed)
- Unity 2022.3.x

### Key Features
- VRCFury integration for all components
- GPU-accelerated mesh processing
- Multi-clothing UDIM coordination
- Layered clothing optimization
- Detection result caching
- Custom progress windows
- Automatic icon assignment

## File Organization

| What | Where | Git Tracked |
|------|-------|-------------|
| Package source | `Packages/com.yucp.components/` | Yes |
| Release files | GitHub Releases | No (auto-generated) |
| VPM listing | GitHub Pages | No (auto-generated) |
| Development assets | `Assets/` | No (Unity workspace) |
| Website | `Website/` | Yes (optional customization) |

## Resources

- [VRCFury Documentation](https://vrcfury.com/)
- [VRCFury GitHub](https://github.com/VRCFury/VRCFury)
- [VRCFury VPM](https://vcc.vrcfury.com/)
- [VPM Packages Guide](https://vcc.docs.vrchat.com/guides/packages)
- [Poiyomi VPM Example](https://poiyomi.github.io/vpm/)
- [VRChat Template Package](https://github.com/vrchat-community/template-package)

## License

MIT License - See LICENSE.md in package folder

## Support

- GitHub Issues: https://github.com/yucp-club/YUCP-Components/issues
- Package URL: http://vpm.yucp.club/
