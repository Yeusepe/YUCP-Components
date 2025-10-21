# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0]

### Added
- Package Exporter system with ConfuserEx obfuscation support
- Export Profile ScriptableObjects for reusable package configurations
- Multi-profile batch export capability
- Package dependency management (bundle or reference as auto-download)
- Automatic ConfuserEx CLI download and integration
- Three obfuscation presets (Mild, Normal, Aggressive)
- Package icon injection support
- Customizable export filters for files and folders
- Version control support for export configurations
- FastFurShader-V5 (Warren's Fast Fur Shader) support for UDIM discard
- All Auto Body Hider components now work with FastFur shaders in addition to Poiyomi
- Automatic shader detection for both Poiyomi and FastFur materials
- Proper keyword and property configuration for FastFur UDIM discard system
- Gesture Manager Input Emulator component for mapping keyboard and controller inputs to Gesture Manager parameters
- Support for both Gesture Manager mode (Vrc3Param system) and direct Animator mode
- Automatic mode detection based on Gesture Manager type
- Runtime input handling with configurable deadzones and sensitivity
- VRCFury integration for input mapping toggles and sliders

### Fixed
- Gesture Manager Input Emulator now handles unmapped controller buttons gracefully without throwing Unity Input Manager errors
- Added fallback to direct joystick KeyCode access when Input Manager buttons are not configured
- Improved error handling for controller axes and triggers that aren't set up in Input Manager

## [0.1.0] - 2025-10-14

### Added
- Initial release of YUCP Components
- Symmetric Armature Auto-Link component
- Closest Bone Auto-Link component
- View Position & Head Auto-Link component
- Auto Body Hider component with multiple detection methods
- Auto Grip Generator component (Beta)
- Auto UDIM Discard component (Beta)
- GPU-accelerated vertex detection
- Multi-clothing UDIM coordination
- Layered clothing optimization
- Custom progress window with YUCP branding
- Detection result caching system
- VRCFury integration for all components

### Dependencies
- VRCFury >= 1.0.0 (automatically installed)
- VRChat SDK3 Avatars
- Unity 2022.3.x

