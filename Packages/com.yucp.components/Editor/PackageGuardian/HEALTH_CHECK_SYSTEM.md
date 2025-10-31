# Package Guardian - Enhanced Health Check System

## Overview

Package Guardian now features a comprehensive, robust health monitoring system designed to protect your Unity project from common issues and potential breakage. The system provides automated checks, auto-fix capabilities, and detailed reporting.

## Key Features

### 1. Comprehensive Health Checks

The health check system validates 13 different categories of issues:

#### Core Checks
- **Compilation Errors**: Detects script compilation errors and warnings
- **Package Conflicts**: Identifies conflicting package combinations
- **Broken References**: Finds missing script references in scenes and prefabs

#### Enhanced Checks (NEW)
- **Missing Meta Files**: Detects assets without .meta files (can cause GUID conflicts)
- **Duplicate GUIDs**: Identifies duplicate asset GUIDs (prevents corruption)
- **Invalid File Names**: Finds problematic file names that may cause platform issues
- **Shader Compilation**: Checks for shader compilation errors
- **Memory Usage**: Monitors Unity Editor memory consumption
- **Unity API Compatibility**: Validates Unity version and API usage
- **Scene Integrity**: Verifies scenes in build settings
- **Project Settings**: Checks for best practice configuration
- **Dependency Integrity**: Validates package manifest and dependencies

### 2. Auto-Fix Capabilities

Many issues can be automatically resolved with a single click:

- **Missing Meta Files**: Reimport affected assets
- **Duplicate GUIDs**: Regenerate GUIDs for duplicate assets
- **Missing Scenes**: Remove broken scene references from build settings
- **Memory Issues**: Clear console and run garbage collection
- **Package Lock**: Regenerate packages-lock.json
- **Color Space**: Switch to Linear color space

### 3. Automated Health Monitoring

The system continuously monitors your project in the background:

- **Scheduled Checks**: Configurable interval (1-60 minutes)
- **Critical Notifications**: Immediate alerts for critical issues
- **Background Operation**: Non-intrusive monitoring
- **Persistent Results**: Tracks issues over time

**Access:** Tools > Package Guardian > Health Monitor Settings

### 4. Health Report Generation

Generate comprehensive Markdown reports:

- **Executive Summary**: Overall health status and statistics
- **Issue Breakdown**: Detailed analysis by category and severity
- **System Information**: Hardware and Unity configuration
- **Recommendations**: Actionable steps to improve project health
- **Auto-fix Summary**: List of automatically fixable issues

**Access:** Tools > Package Guardian > Generate Health Report

### 5. Enhanced User Interface

The Health tab now features:

- **Auto-Fix Buttons**: One-click resolution for fixable issues
- **Severity Badges**: Visual indicators for issue priority
- **Category Labels**: Clear categorization of issues
- **Suggested Actions**: Highlighted recommendations
- **Status Dashboard**: Real-time health summary

## Issue Severity Levels

| Level | Description | Action Required |
|-------|-------------|-----------------|
| **CRITICAL** | Causes data loss or corruption | Fix immediately |
| **ERROR** | Breaks functionality | Fix before continuing |
| **WARNING** | May cause problems | Review and address |
| **INFO** | Best practice recommendations | Address when possible |

## Issue Categories

### Asset Integrity
- Missing Meta Files
- Duplicate GUIDs
- Invalid File Names
- Broken References

### Compilation & Build
- Compilation Errors
- Shader Compilation Errors
- Scene Validation

### Package Management
- Package Conflicts
- Missing Dependencies
- Dependency Integrity

### Performance & Optimization
- Memory Warnings
- Performance Warnings
- Large Files

### Configuration
- Project Settings Issues
- Unity API Compatibility

### Operations
- Dangerous Deletions
- Locked Files
- Binary Conflicts

## Usage Guide

### Quick Health Check

1. Open Unity Editor
2. Go to **Tools > Package Guardian > Run Health Check**
3. Review detected issues in the Health window
4. Click **Auto-Fix** buttons for fixable issues
5. Follow suggested actions for other issues

### Configure Automated Monitoring

1. Go to **Tools > Package Guardian > Health Monitor Settings**
2. Enable **Automated Health Monitoring**
3. Configure check interval (default: 5 minutes)
4. Enable **Critical Notifications** for alerts
5. Click **Save**

### Generate Health Report

1. Go to **Tools > Package Guardian > Generate Health Report**
2. Report is saved to `HealthReports/` folder
3. Open report in Markdown viewer
4. Share with team or archive for reference

### View Health Status

1. Open Package Guardian window
2. Click the **Health** tab
3. Review issues grouped by severity
4. Use **Auto-Fix** buttons for quick resolution
5. Click **Settings** to configure monitoring

## Best Practices

### Recommended Workflow

1. **Enable automated monitoring** for continuous protection
2. **Run health checks** before:
   - Creating snapshots/commits
   - Switching Unity versions
   - Major refactoring
   - Sharing project with team
3. **Fix critical issues immediately** - they can cause data loss
4. **Address errors** before continuing development
5. **Review warnings** regularly to prevent future problems
6. **Generate reports** weekly for team review

### Performance Considerations

The health check system is optimized for minimal impact:

- Checks run in background without blocking Editor
- File scanning limited to reasonable amounts (5000 files max)
- Memory checks are lightweight
- Configurable intervals prevent over-checking

### Team Collaboration

- **Share health reports** with team members
- **Set monitoring standards** (e.g., no commits with errors)
- **Document auto-fixes** in version control messages
- **Review health trends** over time

## API Reference

### HealthMonitorService

```csharp
// Enable/disable automated monitoring
HealthMonitorService.SetEnabled(true);

// Configure check interval (seconds)
HealthMonitorService.SetCheckInterval(300.0);

// Enable/disable notifications
HealthMonitorService.SetShowNotifications(true);

// Force immediate check
HealthMonitorService.ForceHealthCheck();

// Get last results
var issues = HealthMonitorService.GetLastResults();
```

### ProjectValidator

```csharp
var validator = new ProjectValidator(projectRootPath);

// Validate entire project
var issues = validator.ValidateProject();

// Validate specific changes
var changeIssues = validator.ValidateChanges(fileChanges);

// Validate rollback operation
var rollbackIssues = validator.ValidateRollback(changes);
```

### ValidationIssue

```csharp
// Check if issue can be auto-fixed
if (issue.CanAutoFix)
{
    // Apply auto-fix
    issue.AutoFix?.Invoke();
}

// Access issue properties
var severity = issue.Severity;
var category = issue.Category;
var title = issue.Title;
var description = issue.Description;
var affectedPaths = issue.AffectedPaths;
var suggestedAction = issue.SuggestedAction;
```

## Troubleshooting

### Health Check Not Running

1. Check if monitoring is enabled in Settings
2. Verify check interval is reasonable (1-60 minutes)
3. Check Unity Console for errors
4. Try forcing a manual check

### Auto-Fix Not Working

1. Ensure you have write permissions
2. Check if assets are locked by other processes
3. Close Unity and reopen if assets are locked
4. Review Console for specific error messages

### High Memory Usage

1. Close other applications
2. Clear Unity Console (right-click > Clear)
3. Run garbage collection (auto-fix button)
4. Restart Unity Editor if necessary
5. Check for memory leaks in custom scripts

### False Positives

Some checks may report issues that are intentional:

- **Missing Scenes**: Scenes may be in DLC or optional content
- **Company Name**: "DefaultCompany" is fine for personal projects
- **Color Space**: Gamma may be intentional for specific art styles
- **Scripting Backend**: Mono may be preferred for development builds

Configure monitoring settings to reduce noise from these issues.

## Technical Implementation

### Architecture

```
PackageGuardian/
├── Core/
│   └── Validation/
│       ├── ProjectValidator.cs      # 13 health check methods
│       └── ValidationIssue.cs       # Issue model with auto-fix
├── Services/
│   ├── HealthMonitorService.cs      # Automated monitoring
│   └── HealthReportGenerator.cs     # Report generation
├── Settings/
│   └── HealthMonitorSettings.cs     # Configuration UI
└── Windows/
    └── UnifiedDashboard/
        └── HealthTab.cs              # Enhanced UI
```

### Check Execution Flow

1. **Trigger**: Manual, automated, or on-demand
2. **Collection**: ProjectValidator runs all checks
3. **Analysis**: Issues categorized by severity
4. **Notification**: Critical issues trigger alerts
5. **Display**: Results shown in Health tab
6. **Action**: User applies auto-fixes or follows suggestions

### Performance Optimization

- File scanning limited to 5000 files
- Prefab/scene checks capped at 100-200 assets
- Regex patterns pre-compiled
- Background execution without blocking
- Cached results to avoid redundant checks

## Future Enhancements

Potential improvements for future versions:

- [ ] Parallel health checks for faster execution
- [ ] Machine learning to predict issues
- [ ] Integration with CI/CD pipelines
- [ ] Custom rule definitions
- [ ] Historical trend analysis
- [ ] Team dashboard with aggregated metrics
- [ ] Automated fixing on file save
- [ ] Integration with version control

## Credits

The Package Guardian health check system is inspired by:

- Unity's built-in validation systems
- Static analysis tools (SonarQube, ReSharper)
- Cloud diagnostics best practices
- Community feedback and real-world usage

## Support

For issues, questions, or feature requests:

1. Check this documentation first
2. Review Console logs for error details
3. Generate health report for debugging
4. Contact package maintainer with report attached

## Version History

### v2.0.0 (Current)
- Complete overhaul of health check system
- Added 9 new validation categories
- Implemented auto-fix capabilities
- Added automated monitoring service
- Created health report generation
- Enhanced UI with better visualization
- Renamed folder to proper "PackageGuardian" spelling

### v1.0.0
- Initial health check implementation
- Basic validation for compilation, packages, and references

---

*Package Guardian - Your Unity project health monitoring system*

