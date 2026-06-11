# Changelog

## [2026-06-11]

### com.yucp.importer
- Introduced `YucpEditorDialog` for improved user interactions within the package manager.
- Significant refactor of the `PackageManagerWindow` UI, consolidating views and updating styles.
- Enhanced reliability of core installation services, including update delivery and automatic package alias handling.
- Added comprehensive unit tests for `AuthorizedVpmPackageInstaller`, `UpdateDeliveryService`, and `YucpEditorDialog`.

## [2026-06-08]

### com.yucp.importer
- Enhanced `AliasPackageAutoInstaller` for more robust automatic package alias handling.
- Added unit tests for `AliasPackageInstallState` to ensure reliable installation flows.

## [2026-05-31]

### com.yucp.importer
- Enhanced `AuthorizedVpmPackageInstaller` and `UpdateDeliveryService` for more robust package management.
- Improved `PackageMetadataExtractor` logic for better reliability.
- Added `ExecutionIntegrityTestHooks` for internal validation.
- Updated `PackageManagerWindow` user interface.
- Added comprehensive unit tests for `UpdateDeliveryService`.

## [2026-05-30]

### com.yucp.importer
- Bumped the package to `0.1.9` so VPM alias shims require the importer build that detects installed alias packages and starts the authorized install flow.
- Added `AliasPackageAutoInstaller` to automatically handle package aliases.
- Added `AuthorizedVpmPackageInstaller` for secure VPM package installation.
- Improved `UpdateDeliveryService` and `PackageMetadataExtractor` for more reliable updates.
- Refactored `PackageManagerWindow` and `InstalledPackagesView` for better performance and UI consistency.
- Enhanced `YucpDisabledFileResolver` for improved file conflict management.
- Updated `CreatorIdentityOAuthService`.
- Added comprehensive unit tests for alias package states and update delivery.

### com.yucp.components
- Added `ThryMaterialOptimizerBridge` for integration with Thry Material Optimizer.
- Significant improvements to `AutoBodyHiderProcessor` for better mesh processing results.

### General
- Added local Git hooks (`commit-msg`, `pre-commit`) to ensure code quality and consistency.
