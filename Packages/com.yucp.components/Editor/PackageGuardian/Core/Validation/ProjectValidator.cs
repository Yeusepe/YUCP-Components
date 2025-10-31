using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using PackageGuardian.Core.Diff;

namespace PackageGuardian.Core.Validation
{
    /// <summary>
    /// Validates project state and detects potential issues.
    /// </summary>
    public sealed class ProjectValidator
    {
        private readonly string _projectRoot;
        private const long LARGE_FILE_THRESHOLD = 50 * 1024 * 1024; // 50MB
        
        public ProjectValidator(string projectRoot)
        {
            _projectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
        }
        
        /// <summary>
        /// Validates the current project state and returns any detected issues.
        /// </summary>
        public List<ValidationIssue> ValidateProject()
        {
            var issues = new List<ValidationIssue>();
            
            // Check for compilation errors
            issues.AddRange(CheckCompilationErrors());
            
            // Check for package conflicts
            issues.AddRange(CheckPackageConflicts());
            
            // Check for missing script references in scenes/prefabs
            issues.AddRange(CheckBrokenReferences());
            
            // Enhanced checks
            issues.AddRange(CheckMissingMetaFiles());
            issues.AddRange(CheckDuplicateGUIDs());
            issues.AddRange(CheckInvalidFileNames());
            issues.AddRange(CheckShaderCompilation());
            issues.AddRange(CheckMemoryUsage());
            issues.AddRange(CheckUnityAPICompatibility());
            issues.AddRange(CheckSceneIntegrity());
            issues.AddRange(CheckProjectSettings());
            issues.AddRange(CheckDependencyIntegrity());
            
            return issues;
        }
        
        /// <summary>
        /// Validates a set of file changes before committing.
        /// </summary>
        public List<ValidationIssue> ValidateChanges(List<FileChange> changes)
        {
            var issues = new List<ValidationIssue>();
            
            if (changes == null || changes.Count == 0)
                return issues;
            
            // Check for dangerous deletions
            issues.AddRange(CheckDangerousDeletions(changes));
            
            // Check for large files
            issues.AddRange(CheckLargeFiles(changes));
            
            // Check for binary conflicts
            issues.AddRange(CheckBinaryFiles(changes));
            
            // Check for locked files
            issues.AddRange(CheckLockedFiles(changes));
            
            return issues;
        }
        
        /// <summary>
        /// Validates a rollback operation.
        /// </summary>
        public List<ValidationIssue> ValidateRollback(List<FileChange> changes)
        {
            var issues = new List<ValidationIssue>();
            
            // Warn about files that will be deleted
            var deletions = changes.Where(c => c.Type == ChangeType.Added).ToList();
            if (deletions.Count > 0)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    IssueCategory.DangerousDeletion,
                    "Files will be deleted",
                    $"Rollback will delete {deletions.Count} file(s) that were added after the target commit.",
                    deletions.Select(d => d.Path).ToArray(),
                    "These files will be permanently removed. Consider creating a stash before rolling back."
                ));
            }
            
            // Warn about files that will be restored
            var restorations = changes.Where(c => c.Type == ChangeType.Deleted).ToList();
            if (restorations.Count > 0)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Info,
                    IssueCategory.DangerousDeletion,
                    "Files will be restored",
                    $"Rollback will restore {restorations.Count} file(s) that were deleted after the target commit.",
                    restorations.Select(r => r.Path).ToArray()
                ));
            }
            
            return issues;
        }
        
        private List<ValidationIssue> CheckCompilationErrors()
        {
            var issues = new List<ValidationIssue>();
            
            // Check if Unity is currently compiling
            if (EditorApplication.isCompiling)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    IssueCategory.CompilationError,
                    "Compilation in progress",
                    "Unity is currently compiling scripts. Wait for compilation to complete before creating a snapshot.",
                    suggestedAction: "Wait for compilation to finish."
                ));
                return issues;
            }
            
            // Get compilation messages
            var assemblies = CompilationPipeline.GetAssemblies();
            
            // Check console for errors (this is a heuristic)
            try
            {
                var logEntries = GetConsoleErrors();
                if (logEntries.Count > 0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error,
                        IssueCategory.CompilationError,
                        "Compilation errors detected",
                        $"There are {logEntries.Count} error(s) in the Unity console. Creating a snapshot with compilation errors may cause issues.",
                        logEntries.ToArray(),
                        "Fix compilation errors before creating a snapshot, or proceed if these are expected."
                    ));
                }
            }
            catch
            {
                // If we can't get console errors, just continue
            }
            
            return issues;
        }
        
        private List<ValidationIssue> CheckPackageConflicts()
        {
            var issues = new List<ValidationIssue>();
            
            try
            {
                // Read package manifest
                string manifestPath = Path.Combine(_projectRoot, "Packages", "manifest.json");
                if (!File.Exists(manifestPath))
                    return issues;
                
                var manifestJson = File.ReadAllText(manifestPath);
                
                // Check for known problematic package combinations
                var knownConflicts = new Dictionary<string, string[]>
                {
                    { "com.unity.inputsystem", new[] { "com.unity.input" } },
                    { "com.unity.render-pipelines.universal", new[] { "com.unity.render-pipelines.high-definition" } }
                };
                
                foreach (var conflict in knownConflicts)
                {
                    bool hasPackage = manifestJson.Contains($"\"{conflict.Key}\"");
                    if (hasPackage)
                    {
                        foreach (var conflictingPackage in conflict.Value)
                        {
                            if (manifestJson.Contains($"\"{conflictingPackage}\""))
                            {
                                issues.Add(new ValidationIssue(
                                    IssueSeverity.Error,
                                    IssueCategory.PackageConflict,
                                    "Package conflict detected",
                                    $"Package '{conflict.Key}' conflicts with '{conflictingPackage}'. These packages cannot be used together.",
                                    new[] { conflict.Key, conflictingPackage },
                                    $"Remove one of the conflicting packages: {conflict.Key} or {conflictingPackage}"
                                ));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check package conflicts: {ex.Message}");
            }
            
            return issues;
        }
        
        private List<ValidationIssue> CheckBrokenReferences()
        {
            var issues = new List<ValidationIssue>();
            
            try
            {
                // Find all prefabs and scenes
                var prefabs = AssetDatabase.FindAssets("t:Prefab")
                    .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                    .Take(100) // Limit to avoid performance issues
                    .ToList();
                
                var brokenPrefabs = new List<string>();
                
                foreach (var prefabPath in prefabs)
                {
                    // Load prefab and check for missing scripts
                    var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
                    if (prefab != null)
                    {
                        var components = prefab.GetComponentsInChildren<UnityEngine.Component>(true);
                        bool hasMissing = components.Any(c => c == null);
                        
                        if (hasMissing)
                        {
                            brokenPrefabs.Add(prefabPath);
                        }
                    }
                }
                
                if (brokenPrefabs.Count > 0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Warning,
                        IssueCategory.BrokenReference,
                        "Missing script references",
                        $"Found {brokenPrefabs.Count} prefab(s) with missing script references. These may cause errors.",
                        brokenPrefabs.ToArray(),
                        "Fix or remove missing script references before creating a snapshot."
                    ));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check broken references: {ex.Message}");
            }
            
            return issues;
        }
        
        private List<ValidationIssue> CheckDangerousDeletions(List<FileChange> changes)
        {
            var issues = new List<ValidationIssue>();
            
            // Find critical files being deleted
            var deletions = changes.Where(c => c.Type == ChangeType.Deleted).ToList();
            var criticalDeletions = deletions.Where(d => 
                d.Path.EndsWith(".unity") || // Scenes
                d.Path.EndsWith("manifest.json") || // Package manifest
                d.Path.Contains("ProjectSettings/") || // Project settings
                d.Path.EndsWith(".asmdef") // Assembly definitions
            ).ToList();
            
            if (criticalDeletions.Count > 0)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    IssueCategory.DangerousDeletion,
                    "Critical files being deleted",
                    $"You are deleting {criticalDeletions.Count} critical file(s) including scenes, project settings, or package manifests.",
                    criticalDeletions.Select(d => d.Path).ToArray(),
                    "Verify these deletions are intentional before proceeding."
                ));
            }
            
            return issues;
        }
        
        private List<ValidationIssue> CheckLargeFiles(List<FileChange> changes)
        {
            var issues = new List<ValidationIssue>();
            
            // Check for large files being added
            var additions = changes.Where(c => c.Type == ChangeType.Added || c.Type == ChangeType.Modified).ToList();
            var largeFiles = new List<(string path, long size)>();
            
            foreach (var addition in additions)
            {
                string fullPath = Path.Combine(_projectRoot, addition.Path);
                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Length > LARGE_FILE_THRESHOLD)
                    {
                        largeFiles.Add((addition.Path, fileInfo.Length));
                    }
                }
            }
            
            if (largeFiles.Count > 0)
            {
                long totalSize = largeFiles.Sum(f => f.size);
                string sizeStr = FormatBytes(totalSize);
                
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    IssueCategory.LargeFile,
                    "Large files detected",
                    $"You are adding {largeFiles.Count} large file(s) totaling {sizeStr}. This may impact repository performance.",
                    largeFiles.Select(f => $"{f.path} ({FormatBytes(f.size)})").ToArray(),
                    "Consider using Git LFS or external asset storage for large binary files."
                ));
            }
            
            return issues;
        }
        
        private List<ValidationIssue> CheckBinaryFiles(List<FileChange> changes)
        {
            var issues = new List<ValidationIssue>();
            
            // Check for modified binary files (potential conflicts)
            var modifications = changes.Where(c => c.Type == ChangeType.Modified && c.IsBinary).ToList();
            
            if (modifications.Count > 0)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Info,
                    IssueCategory.BinaryConflict,
                    "Binary files modified",
                    $"{modifications.Count} binary file(s) have been modified. Binary files cannot be merged and may cause conflicts.",
                    modifications.Select(m => m.Path).ToArray(),
                    "Be aware that rolling back or merging binary files will use the entire file, not a merge."
                ));
            }
            
            return issues;
        }
        
        private List<ValidationIssue> CheckLockedFiles(List<FileChange> changes)
        {
            var issues = new List<ValidationIssue>();
            
            // Check if any files are currently locked (in use)
            var lockedFiles = new List<string>();
            
            foreach (var change in changes)
            {
                string fullPath = Path.Combine(_projectRoot, change.Path);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        // Try to open the file exclusively
                        using (var stream = File.Open(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            // If we can open it, it's not locked
                        }
                    }
                    catch (IOException)
                    {
                        // File is locked
                        if (change.Path.EndsWith(".dll") || change.Path.EndsWith(".so") || change.Path.EndsWith(".dylib"))
                        {
                            lockedFiles.Add(change.Path);
                        }
                    }
                    catch
                    {
                        // Ignore other errors
                    }
                }
            }
            
            if (lockedFiles.Count > 0)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Warning,
                    IssueCategory.LockedFile,
                    "Locked files detected",
                    $"{lockedFiles.Count} file(s) are currently locked by Unity or another process. These files may not be updated until Unity restarts.",
                    lockedFiles.ToArray(),
                    "Restart Unity to unlock these files, or proceed and they will be updated on next restart."
                ));
            }
            
            return issues;
        }
        
        private List<string> GetConsoleErrors()
        {
            var errors = new List<string>();
            
            // Use reflection to access Unity's LogEntries
            try
            {
                var logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                if (logEntriesType != null)
                {
                    var getCountMethod = logEntriesType.GetMethod("GetCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    
                    if (getCountMethod != null && getEntryMethod != null)
                    {
                        int count = (int)getCountMethod.Invoke(null, null);
                        var entryType = Type.GetType("UnityEditor.LogEntry,UnityEditor");
                        
                        if (entryType != null)
                        {
                            var messageField = entryType.GetField("message");
                            var modeField = entryType.GetField("mode");
                            
                            for (int i = 0; i < Math.Min(count, 50); i++) // Limit to 50 entries
                            {
                                var entry = Activator.CreateInstance(entryType);
                                var args = new object[] { i, entry };
                                getEntryMethod.Invoke(null, args);
                                entry = args[1];
                                
                                if (messageField != null && modeField != null)
                                {
                                    int mode = (int)modeField.GetValue(entry);
                                    // Mode 2 = Error, Mode 4 = ScriptingError, Mode 16 = ScriptCompilationError
                                    if (mode == 2 || mode == 4 || mode == 16)
                                    {
                                        string message = messageField.GetValue(entry)?.ToString();
                                        if (!string.IsNullOrEmpty(message))
                                        {
                                            errors.Add(message);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // If reflection fails, return empty list
            }
            
            return errors;
        }
        
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        
        // ===== ENHANCED VALIDATION METHODS =====
        
        /// <summary>
        /// Checks for assets missing their .meta files
        /// </summary>
        private List<ValidationIssue> CheckMissingMetaFiles()
        {
            var issues = new List<ValidationIssue>();
            var missingMeta = new List<string>();
            
            try
            {
                // Check Assets folder
                var assetsPath = Path.Combine(_projectRoot, "Assets");
                if (Directory.Exists(assetsPath))
                {
                    CheckDirectoryForMissingMeta(assetsPath, missingMeta);
                }
                
                // Check Packages folder
                var packagesPath = Path.Combine(_projectRoot, "Packages");
                if (Directory.Exists(packagesPath))
                {
                    CheckDirectoryForMissingMeta(packagesPath, missingMeta);
                }
                
                if (missingMeta.Count > 0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error,
                        IssueCategory.MissingMetaFile,
                        "Missing .meta files detected",
                        $"Found {missingMeta.Count} asset file(s) without corresponding .meta files. This can cause GUID conflicts and asset corruption.",
                        missingMeta.Take(20).ToArray(),
                        "Reimport the affected assets or let Unity regenerate the meta files.",
                        () => {
                            foreach (var path in missingMeta)
                            {
                                AssetDatabase.ImportAsset(path.Replace(_projectRoot + Path.DirectorySeparatorChar, ""), ImportAssetOptions.ForceUpdate);
                            }
                            AssetDatabase.Refresh();
                        }
                    ));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check meta files: {ex.Message}");
            }
            
            return issues;
        }
        
        private void CheckDirectoryForMissingMeta(string directory, List<string> missingMeta)
        {
            try
            {
                foreach (var file in Directory.GetFiles(directory))
                {
                    if (!file.EndsWith(".meta"))
                    {
                        var metaFile = file + ".meta";
                        if (!File.Exists(metaFile))
                        {
                            missingMeta.Add(file);
                        }
                    }
                }
                
                foreach (var dir in Directory.GetDirectories(directory))
                {
                    if (!dir.Contains(".git") && !dir.Contains("Library") && !dir.Contains("Temp"))
                    {
                        var metaFile = dir + ".meta";
                        if (!File.Exists(metaFile))
                        {
                            missingMeta.Add(dir);
                        }
                        CheckDirectoryForMissingMeta(dir, missingMeta);
                    }
                }
            }
            catch
            {
                // Skip directories we can't access
            }
        }
        
        /// <summary>
        /// Checks for duplicate GUIDs in the project
        /// </summary>
        private List<ValidationIssue> CheckDuplicateGUIDs()
        {
            var issues = new List<ValidationIssue>();
            var guidMap = new Dictionary<string, List<string>>();
            
            try
            {
                var metaFiles = Directory.GetFiles(_projectRoot, "*.meta", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains(".git"))
                    .Take(5000); // Limit for performance
                
                foreach (var metaFile in metaFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(metaFile);
                        var guidMatch = Regex.Match(content, @"guid:\s*([a-f0-9]{32})");
                        
                        if (guidMatch.Success)
                        {
                            var guid = guidMatch.Groups[1].Value;
                            if (!guidMap.ContainsKey(guid))
                            {
                                guidMap[guid] = new List<string>();
                            }
                            guidMap[guid].Add(metaFile.Replace(".meta", ""));
                        }
                    }
                    catch
                    {
                        // Skip files we can't read
                    }
                }
                
                var duplicates = guidMap.Where(kvp => kvp.Value.Count > 1).ToList();
                
                if (duplicates.Count > 0)
                {
                    var affectedFiles = duplicates.SelectMany(d => d.Value).Take(20).ToArray();
                    
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Critical,
                        IssueCategory.DuplicateGUID,
                        "Duplicate GUIDs detected",
                        $"Found {duplicates.Count} GUID(s) used by multiple assets. This WILL cause asset corruption and data loss!",
                        affectedFiles,
                        "Delete and reimport duplicate assets, or use Unity's 'Reimport' to regenerate GUIDs.",
                        () => {
                            foreach (var duplicate in duplicates)
                            {
                                // Auto-fix: Reimport all but the first file
                                for (int i = 1; i < duplicate.Value.Count; i++)
                                {
                                    var assetPath = duplicate.Value[i].Replace(_projectRoot + Path.DirectorySeparatorChar, "");
                                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                                }
                            }
                            AssetDatabase.Refresh();
                        }
                    ));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check duplicate GUIDs: {ex.Message}");
            }
            
            return issues;
        }
        
        /// <summary>
        /// Checks for invalid file names that could cause issues
        /// </summary>
        private List<ValidationIssue> CheckInvalidFileNames()
        {
            var issues = new List<ValidationIssue>();
            var invalidFiles = new List<string>();
            
            try
            {
                // Unity doesn't like certain characters in file names
                var invalidChars = new[] { '~', '#', '%', '&', '{', '}', '\\', '<', '>', '*', '?', '/', '$', '!', '\'', '"', ':', '@', '+', '`', '|', '=' };
                var invalidPatterns = new[] { "..", "  " }; // Double dots, double spaces
                
                var files = Directory.GetFiles(_projectRoot, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains(".git"))
                    .Take(5000);
                
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    
                    // Check for invalid characters
                    if (invalidChars.Any(c => fileName.Contains(c)))
                    {
                        invalidFiles.Add(file);
                        continue;
                    }
                    
                    // Check for invalid patterns
                    if (invalidPatterns.Any(p => fileName.Contains(p)))
                    {
                        invalidFiles.Add(file);
                        continue;
                    }
                    
                    // Check for leading/trailing spaces
                    if (fileName != fileName.Trim())
                    {
                        invalidFiles.Add(file);
                    }
                }
                
                if (invalidFiles.Count > 0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Warning,
                        IssueCategory.InvalidFileName,
                        "Invalid file names detected",
                        $"Found {invalidFiles.Count} file(s) with names that may cause issues in Unity or on different platforms.",
                        invalidFiles.Take(20).ToArray(),
                        "Rename files to use only alphanumeric characters, underscores, hyphens, and dots."
                    ));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check file names: {ex.Message}");
            }
            
            return issues;
        }
        
        /// <summary>
        /// Checks for shader compilation errors
        /// </summary>
        private List<ValidationIssue> CheckShaderCompilation()
        {
            var issues = new List<ValidationIssue>();
            
            try
            {
                // Find all shaders
                var shaders = AssetDatabase.FindAssets("t:Shader")
                    .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                    .Take(200)
                    .ToList();
                
                var brokenShaders = new List<string>();
                
                foreach (var shaderPath in shaders)
                {
                    var shader = AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(shaderPath);
                    if (shader != null)
                    {
                        // Check if shader is supported on current platform
                        if (!shader.isSupported)
                        {
                            brokenShaders.Add(shaderPath);
                        }
                    }
                }
                
                if (brokenShaders.Count > 0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error,
                        IssueCategory.ShaderCompilationError,
                        "Shader compilation errors",
                        $"Found {brokenShaders.Count} shader(s) that failed to compile or are not supported on this platform.",
                        brokenShaders.ToArray(),
                        "Check the Console for shader compilation errors and fix shader code."
                    ));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check shaders: {ex.Message}");
            }
            
            return issues;
        }
        
        /// <summary>
        /// Checks memory usage and warns if high
        /// </summary>
        private List<ValidationIssue> CheckMemoryUsage()
        {
            var issues = new List<ValidationIssue>();
            
            try
            {
                // Get memory usage
                var totalMemory = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
                var usedMemory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                var monoMemory = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                
                // Warn if using more than 2GB
                if (usedMemory > 2L * 1024 * 1024 * 1024)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Warning,
                        IssueCategory.MemoryWarning,
                        "High memory usage detected",
                        $"Unity Editor is using {FormatBytes(usedMemory)} of memory. High memory usage can cause instability.",
                        new[] { $"Total Reserved: {FormatBytes(totalMemory)}", $"Mono Heap: {FormatBytes(monoMemory)}" },
                        "Consider closing other applications, clearing console, or restarting Unity Editor.",
                        () => {
                            // Auto-fix: Clear console and run GC
                            var logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                            logEntriesType?.GetMethod("Clear")?.Invoke(null, null);
                            System.GC.Collect();
                            UnityEngine.Resources.UnloadUnusedAssets();
                        }
                    ));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check memory: {ex.Message}");
            }
            
            return issues;
        }
        
        /// <summary>
        /// Checks Unity version and API compatibility
        /// </summary>
        private List<ValidationIssue> CheckUnityAPICompatibility()
        {
            var issues = new List<ValidationIssue>();
            
            try
            {
                var unityVersion = UnityEngine.Application.unityVersion;
                var versionParts = unityVersion.Split('.');
                
                if (versionParts.Length >= 2)
                {
                    int major = int.Parse(versionParts[0]);
                    int minor = int.Parse(versionParts[1]);
                    
                    // Warn about alpha/beta versions
                    if (unityVersion.Contains("a") || unityVersion.Contains("b"))
                    {
                        issues.Add(new ValidationIssue(
                            IssueSeverity.Warning,
                            IssueCategory.UnityAPICompatibility,
                            "Using pre-release Unity version",
                            $"Unity {unityVersion} is a pre-release version. Projects may experience instability or compatibility issues.",
                            null,
                            "Consider using a stable LTS release for production projects."
                        ));
                    }
                    
                    // Check scripting backend
                    var scriptingBackend = PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup);
                    if (scriptingBackend == ScriptingImplementation.Mono2x)
                    {
                        issues.Add(new ValidationIssue(
                            IssueSeverity.Info,
                            IssueCategory.UnityAPICompatibility,
                            "Using Mono scripting backend",
                            "The Mono scripting backend is being used. IL2CPP provides better performance for builds.",
                            null,
                            "Consider switching to IL2CPP for production builds (Player Settings > Scripting Backend)."
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check Unity compatibility: {ex.Message}");
            }
            
            return issues;
        }
        
        /// <summary>
        /// Validates scene integrity
        /// </summary>
        private List<ValidationIssue> CheckSceneIntegrity()
        {
            var issues = new List<ValidationIssue>();
            
            try
            {
                // Check if there are any scenes in build settings
                var scenesInBuild = EditorBuildSettings.scenes;
                
                if (scenesInBuild.Length == 0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Warning,
                        IssueCategory.SceneValidation,
                        "No scenes in build settings",
                        "There are no scenes added to the build settings. Your project cannot be built without scenes.",
                        null,
                        "Add scenes to Build Settings (File > Build Settings > Add Open Scenes)."
                    ));
                }
                else
                {
                    var missingScenes = scenesInBuild.Where(s => s.enabled && !File.Exists(s.path)).ToList();
                    
                    if (missingScenes.Count > 0)
                    {
                        issues.Add(new ValidationIssue(
                            IssueSeverity.Error,
                            IssueCategory.SceneValidation,
                            "Missing scenes in build settings",
                            $"{missingScenes.Count} scene(s) in build settings no longer exist.",
                            missingScenes.Select(s => s.path).ToArray(),
                            "Remove missing scenes from Build Settings and add existing scenes.",
                            () => {
                                // Auto-fix: Remove missing scenes
                                var validScenes = scenesInBuild.Where(s => File.Exists(s.path)).ToArray();
                                EditorBuildSettings.scenes = validScenes;
                            }
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check scenes: {ex.Message}");
            }
            
            return issues;
        }
        
        /// <summary>
        /// Checks project settings for common issues
        /// </summary>
        private List<ValidationIssue> CheckProjectSettings()
        {
            var issues = new List<ValidationIssue>();
            
            try
            {
                // Check if company name is set
                if (string.IsNullOrEmpty(PlayerSettings.companyName) || PlayerSettings.companyName == "DefaultCompany")
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Info,
                        IssueCategory.ProjectSettingsIssue,
                        "Company name not set",
                        "The company name in Project Settings is not configured. This is used for application data paths.",
                        null,
                        "Set your company name in Edit > Project Settings > Player > Company Name.",
                        () => {
                            // Can't auto-fix this as we don't know what it should be
                        }
                    ));
                }
                
                // Check if product name is set
                if (string.IsNullOrEmpty(PlayerSettings.productName))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Warning,
                        IssueCategory.ProjectSettingsIssue,
                        "Product name not set",
                        "The product name in Project Settings is empty.",
                        null,
                        "Set your product name in Edit > Project Settings > Player > Product Name."
                    ));
                }
                
                // Check color space
                if (PlayerSettings.colorSpace != UnityEngine.ColorSpace.Linear)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Info,
                        IssueCategory.ProjectSettingsIssue,
                        "Using Gamma color space",
                        "The project is using Gamma color space. Linear color space provides better visual quality for modern rendering.",
                        null,
                        "Consider switching to Linear color space (Edit > Project Settings > Player > Other Settings > Color Space).",
                        () => {
                            PlayerSettings.colorSpace = UnityEngine.ColorSpace.Linear;
                        }
                    ));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check project settings: {ex.Message}");
            }
            
            return issues;
        }
        
        /// <summary>
        /// Checks package dependencies for integrity
        /// </summary>
        private List<ValidationIssue> CheckDependencyIntegrity()
        {
            var issues = new List<ValidationIssue>();
            
            try
            {
                // Check if manifest.json exists
                string manifestPath = Path.Combine(_projectRoot, "Packages", "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Critical,
                        IssueCategory.DependencyIntegrity,
                        "Package manifest missing",
                        "The Packages/manifest.json file is missing. This is critical for package management.",
                        null,
                        "Restore the manifest.json file from backup or let Unity regenerate it."
                    ));
                    return issues;
                }
                
                // Check if packages-lock.json exists
                string lockPath = Path.Combine(_projectRoot, "Packages", "packages-lock.json");
                if (!File.Exists(lockPath))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Warning,
                        IssueCategory.DependencyIntegrity,
                        "Package lock file missing",
                        "The Packages/packages-lock.json file is missing. This may cause inconsistent package versions.",
                        null,
                        "Resolve packages (Window > Package Manager > Resolve) to regenerate the lock file.",
                        () => {
                            UnityEditor.PackageManager.Client.Resolve();
                        }
                    ));
                }
                
                // Parse manifest and check for issues
                var manifestJson = File.ReadAllText(manifestPath);
                
                // Check for local file references (could break on other machines)
                if (manifestJson.Contains("file:") && !manifestJson.Contains("file:../"))
                {
                    var fileMatches = Regex.Matches(manifestJson, @"""file:[^""]+""");
                    if (fileMatches.Count > 0)
                    {
                        issues.Add(new ValidationIssue(
                            IssueSeverity.Warning,
                            IssueCategory.DependencyIntegrity,
                            "Absolute file path dependencies",
                            $"Found {fileMatches.Count} package(s) using absolute file paths. These will break on other machines.",
                            fileMatches.Cast<Match>().Select(m => m.Value).Take(10).ToArray(),
                            "Use relative paths (file:../) or git URLs for package dependencies."
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to check dependencies: {ex.Message}");
            }
            
            return issues;
        }
    }
}

