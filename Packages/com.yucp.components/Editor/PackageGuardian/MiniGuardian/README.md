# Mini Package Guardian

## Overview

The Mini Package Guardian is a lightweight, standalone version of the full Package Guardian system. It's automatically bundled with YUCP packages when `com.yucp.components` is **not** a dependency, providing essential import protection without the full feature set.

## Purpose

**Problem**: YUCP packages need import protection even when the full Package Guardian isn't available.

**Solution**: Mini Guardian provides core protection features that share the same underlying code as the full guardian, ensuring consistency and allowing updates to propagate automatically.

## What It Does

### Core Protection Features

1. **Duplicate Detection & Removal**
   - Detects duplicate guardian/installer scripts
   - Removes redundant files automatically
   - Prevents conflicts from multiple installations

2. **Import Error Recovery**
   - Uses transaction-based operations
   - Automatically rolls back on failure
   - Backs up files before modifications
   - Recovers from Unity crashes

3. **Smart File Conflict Resolution**
   - Handles `.yucp_disabled` files
   - Uses heuristics to determine best action:
     - File size comparison
     - Content hash (MD5) for small files
     - Timestamp analysis
   - Decides whether to update or remove duplicates

4. **Redundant Import Detection**
   - Tracks import state with hashing
   - Warns when importing identical package
   - Prevents unnecessary operations

5. **Circuit Breaker Pattern**
   - Stops after 3 consecutive failures
   - Prevents infinite failure loops
   - Can be manually reset

## Shared Code Architecture

The Mini Guardian uses the same core classes as the full guardian:

```
Shared Components:
├── Core/Transactions/GuardianTransaction.cs    (Transaction support with rollback)
└── Mini uses the same transaction system as full guardian

This ensures:
✓ Code consistency
✓ Updates propagate automatically  
✓ Same reliability
✓ Smaller codebase (no duplication)
```

## File Structure

When bundled with a YUCP package, Mini Guardian creates:

```
Packages/
└── yucp.packageguardian/
    ├── package.json                              # Mini package manifest
    ├── Core/
    │   └── Transactions/
    │       └── GuardianTransaction.cs            # Shared transaction system
    └── Editor/
        └── PackageGuardianMini.cs                # Mini guardian (490 lines)
```

## How It Works

### Import Flow

```
1. Package imported
   ↓
2. Mini Guardian detects YUCP files
   ↓
3. Starts transaction (with rollback)
   ↓
4. Executes protection:
   - Remove duplicates
   - Handle .yucp_disabled files
   - Detect redundant imports
   - Verify integrity
   ↓
5a. Success → Commit transaction
5b. Failure → Rollback transaction
   ↓
6. Refresh AssetDatabase
```

### Conflict Resolution Logic

```
For each .yucp_disabled file:

1. Check if enabled version exists
   No → Simply enable the file
   Yes → Conflict detected, analyze...

2. Compare file sizes
   Same size → Likely duplicate
   Different → Likely update

3. Compute hash (if < 100KB)
   Same hash → Exact duplicate → Remove disabled
   Different hash → Update → Replace enabled

4. Check timestamps
   Disabled newer → Update → Replace enabled
   Enabled newer → Keep enabled → Remove disabled

5. Execute decision with transaction backup
```

## Automatic Bundling

The Package Exporter automatically includes Mini Guardian:

```csharp
// In PackageBuilder.cs

if (!profile.dependencies.Contains("com.yucp.components"))
{
    // Bundle Mini Guardian + GuardianTransaction.cs
    InjectMiniGuardian();
}
else
{
    // Full guardian already available via dependency
    // Skip bundling
}
```

## Feature Comparison

| Feature | Mini Guardian | Full Guardian |
|---------|---------------|---------------|
| **Size** | ~490 lines | ~5,000+ lines |
| **Import Protection** | ✅ Yes | ✅ Yes |
| **Transaction Rollback** | ✅ Yes | ✅ Yes |
| **Duplicate Detection** | ✅ Yes | ✅ Yes |
| **Circuit Breaker** | ✅ Yes | ✅ Yes |
| **Health Monitoring** | ❌ No | ✅ 13 categories |
| **Auto-Fix UI** | ❌ No | ✅ Yes |
| **Health Reports** | ❌ No | ✅ Markdown |
| **Scheduled Checks** | ❌ No | ✅ Configurable |
| **Advanced Validation** | ❌ No | ✅ Yes |

## Menu Items

Mini Guardian provides minimal UI:

- **Tools > YUCP > Mini Guardian Status**: View protection status
- **Tools > YUCP > Reset Mini Guardian**: Reset circuit breaker

## When to Use

### Use Mini Guardian When:
- ✅ Exporting YUCP packages without com.yucp.components dependency
- ✅ Want lightweight protection (~2 files vs. full system)
- ✅ Need basic import safety
- ✅ Target projects may not have components package

### Use Full Guardian When:
- ✅ com.yucp.components is already a dependency
- ✅ Need comprehensive health monitoring
- ✅ Want auto-fix capabilities
- ✅ Need detailed health reports
- ✅ Want advanced validation

## Upgrade Path

If a user installs com.yucp.components:

1. **Before**: Mini Guardian provides protection
2. **Install**: User adds com.yucp.components as dependency
3. **After**: Full guardian takes over automatically
4. **Result**: All features now available

The mini guardian peacefully coexists with the full one - the full guardian's circuit breaker and services simply supersede the mini version's capabilities.

## Code Sharing Example

```csharp
// Mini Guardian uses the same transaction class
using PackageGuardian.Core.Transactions;

// Start transaction (same API as full guardian)
using (var transaction = new GuardianTransaction())
{
    // Backup files
    transaction.BackupFile(filePath);
    
    // Execute operations
    transaction.ExecuteFileOperation(src, dest, FileOperationType.Move);
    
    // Commit or rollback (automatic via Dispose)
    transaction.Commit();
}

// If exception occurs, transaction automatically rolls back!
```

This ensures:
- Updates to `GuardianTransaction` benefit both systems
- Same behavior and reliability
- No code duplication
- Easier maintenance

## Technical Details

### Circuit Breaker

```csharp
Max Failures: 3
Storage: EditorPrefs
Auto-Reset: After 24 hours
Manual Reset: Tools > YUCP > Reset Mini Guardian
```

### Transaction System

```csharp
Features:
- File backup before operations
- Rollback on exception
- Commit on success
- IDisposable pattern (auto-rollback)

Supported Operations:
- Copy file
- Move file
- Delete file
- Create file
```

### Conflict Resolution

```csharp
Heuristics (in order):
1. File size comparison (40 confidence points)
2. Content hash MD5 (50 points for match)
3. Timestamp analysis (20 points)

Decision:
- > 70 confidence = High certainty
- 40-70 = Medium certainty
- < 40 = Low certainty (default: keep existing)
```

## Performance

- **Import Time**: +50-200ms (typical)
- **Memory**: < 1MB additional
- **File Operations**: Transaction-backed (safe)
- **Hash Computation**: Only for files < 100KB

## Debugging

Enable detailed logging:

```csharp
// Mini Guardian logs with prefix
[Mini Guardian] Import detected - protecting...
[Mini Guardian] Found 2 .yucp_disabled file(s)
[Mini Guardian] Removed duplicate: PackageGuardian.cs
[Mini Guardian] Protection complete
```

Check status:
```
Tools > YUCP > Mini Guardian Status
```

Reset protection:
```
Tools > YUCP > Reset Mini Guardian
```

## Limitations

Mini Guardian is focused and intentionally limited:

1. **No UI** - Minimal menu items only
2. **No Health Checks** - Import protection only
3. **No Reports** - No markdown generation
4. **No Monitoring** - Runs only on import
5. **Basic Heuristics** - Simpler than full system

These limitations keep it lightweight while still providing essential protection.

## Future Enhancements

Potential improvements:

- [ ] Configurable heuristics via EditorPrefs
- [ ] More detailed logging options
- [ ] Integration test suite
- [ ] Performance metrics

## Credits

Mini Guardian is a distilled version of the full Package Guardian, sharing core transaction logic while remaining standalone and lightweight.

Created: October 31, 2025  
Version: 2.0.0  
Lines of Code: ~490 (vs 5,000+ for full system)

---

*For full protection features, install `com.yucp.components`*







