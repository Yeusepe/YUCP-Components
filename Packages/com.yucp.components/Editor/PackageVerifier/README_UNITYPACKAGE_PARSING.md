# Unity Package Parsing

## Overview

The `ManifestExtractor` class extracts signing data from `.unitypackage` files. Unity packages are tar.gz archives containing asset files.

## Implementation

The extractor uses `ICSharpCode.SharpZipLib` which is commonly available in Unity projects (used by VRChat SDK and other packages).

### Dependencies

The extractor requires:
- `ICSharpCode.SharpZipLib.Tar` for tar archive reading
- `ICSharpCode.SharpZipLib.GZip` for gzip decompression

### How It Works

1. Opens the `.unitypackage` file (tar.gz format)
2. Decompresses using GZip
3. Reads tar archive entries
4. Extracts files to temporary directory
5. Finds manifest and signature files by pathname
6. Reads and parses JSON files
7. Cleans up temporary files

### File Structure

Unity packages store files in folders with GUID names:
```
{GUID}/
  asset          # The actual file content
  asset.meta     # Unity metadata
  pathname       # Destination path in project (e.g., "Assets/_Signing/PackageManifest.json")
```

The extractor looks for:
- `Assets/_Signing/PackageManifest.json` (manifest)
- `Assets/_Signing/PackageManifest.sig` (signature)

### If SharpZipLib is Not Available

If `ICSharpCode.SharpZipLib` is not available in your project:

1. **Add via Package Manager:**
   - Many Unity projects already include it
   - VRChat SDK includes it

2. **Manual Installation:**
   - Download from: https://github.com/icsharpcode/SharpZipLib
   - Add DLLs to `Assets/Plugins/`

3. **Alternative:**
   - Use Unity's `AssetDatabase.ImportPackage()` with a callback
   - Extract files after temporary import
   - This is slower but doesn't require external libraries



