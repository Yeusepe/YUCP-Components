# Export Profile Templates

This folder contains example export profile templates to help you get started quickly.

## Using Templates

1. Duplicate `Example_ExportProfile.asset`
2. Rename it for your project (e.g., "MyAvatar_Export.asset")
3. Configure the settings in the Inspector:
   - Update package name and version
   - Set export folders to your content
   - Scan for dependencies
   - Configure obfuscation if needed
4. Export your package!

## Recommended Folder Structure

For best results, organize your export profiles:

```
Assets/
└── YUCP/
    └── ExportProfiles/
        ├── Avatar_Export.asset
        ├── Clothing_Export.asset
        └── Props_Export.asset
```

This keeps all your export configurations in one place and makes them easy to find and version control.

## Template Settings Explained

The example template includes:
- **Package Name**: "MyPackage" (change this!)
- **Version**: 1.0.0 (follows semantic versioning)
- **Include Dependencies**: Enabled (recommended)
- **Recurse Folders**: Enabled (includes subfolders)
- **Generate package.json**: Enabled (for dependency management)
- **Obfuscation**: Disabled by default (enable if you need code protection)
- **Export Path**: Empty (exports to Desktop by default)

## Common Configurations

### Avatar Package
- **Folders**: `Assets/MyAvatar`
- **Dependencies**: VRCFury, YUCP Components (as Dependency mode)
- **Obfuscation**: Usually not needed
- **Icon**: Avatar thumbnail

### Tool/Script Package
- **Folders**: `Assets/MyTool`, `Packages/com.company.tool`
- **Dependencies**: Minimal
- **Obfuscation**: Recommended for proprietary code
- **Icon**: Tool logo

### Clothing Package
- **Folders**: `Assets/Clothing/MyOutfit`
- **Dependencies**: Auto-detect (likely VRCFury, Poiyomi)
- **Obfuscation**: Not needed
- **Icon**: Clothing preview





