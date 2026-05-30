# Changelog

## [2026-05-30]

### com.yucp.importer
- Added `AliasPackageAutoInstaller` to automatically handle package aliases.
- Added `AuthorizedVpmPackageInstaller` for secure VPM package installation.
- Improved `UpdateDeliveryService` and `PackageMetadataExtractor` for more reliable updates.
- Refactored `PackageManagerWindow` and `InstalledPackagesView` for better performance and UI consistency.
- Enhanced `YucpDisabledFileResolver` for improved file conflict management.
- Added comprehensive unit tests for alias package states and update delivery.

### com.yucp.components
- Added `ThryMaterialOptimizerBridge` for integration with Thry Material Optimizer.
- Significant improvements to `AutoBodyHiderProcessor` for better mesh processing results.

### General
- Added local Git hooks (`commit-msg`, `pre-commit`) to ensure code quality and consistency.
