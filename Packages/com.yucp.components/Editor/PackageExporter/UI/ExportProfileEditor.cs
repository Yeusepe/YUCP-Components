using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Custom inspector for ExportProfile ScriptableObjects.
    /// Provides intuitive UI for configuring package exports with folder browsers and assembly selection.
    /// </summary>
    [CustomEditor(typeof(ExportProfile))]
    public class ExportProfileEditor : UnityEditor.Editor
    {
    private bool showMetadata = true;
    private bool showFolders = true;
    private bool showExportInspector = false;
    private bool showExportOptions = false;
    private bool showExclusionFilters = false;
    private bool showDependencies = true;
    private bool showObfuscation = true;
    private bool showExportSettings = false;
    private bool showStatistics = false;
        
    private Vector2 folderScrollPos;
    private Vector2 exportInspectorScrollPos;
    private Vector2 dependencyScrollPos;
    private Vector2 assemblyScrollPos;
    
    private string inspectorSearchFilter = "";
    private bool showOnlyIncluded = false;
    private bool showOnlyExcluded = false;
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var profile = (ExportProfile)target;
            
            EditorGUILayout.Space(5);
            
            // Package Metadata
            showMetadata = EditorGUILayout.BeginFoldoutHeaderGroup(showMetadata, "Package Metadata");
            if (showMetadata)
            {
                DrawSection(() =>
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("packageName"), new GUIContent("Package Name"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("version"), new GUIContent("Version"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("author"), new GUIContent("Author"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("description"), new GUIContent("Description"));
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Package Icon
            EditorGUILayout.Space(5);
            DrawSection(() =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"), new GUIContent("Package Icon"));
                
                if (profile.icon != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(profile.icon, GUILayout.Width(64), GUILayout.Height(64));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            });
            
            // Export Folders
            EditorGUILayout.Space(5);
            showFolders = EditorGUILayout.BeginFoldoutHeaderGroup(showFolders, "Export Folders");
            if (showFolders)
            {
                DrawSection(() =>
                {
                    EditorGUILayout.HelpBox("Select folders to include in the package export", MessageType.Info);
                    
                    folderScrollPos = EditorGUILayout.BeginScrollView(folderScrollPos, GUILayout.MaxHeight(200));
                    
                    for (int i = 0; i < profile.foldersToExport.Count; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        
                        EditorGUI.BeginChangeCheck();
                        string newFolderPath = EditorGUILayout.TextField(profile.foldersToExport[i]);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(profile, "Change Export Folder");
                            profile.foldersToExport[i] = newFolderPath;
                            EditorUtility.SetDirty(profile);
                        }
                        
                        if (GUILayout.Button("Browse", GUILayout.Width(60)))
                        {
                            string selectedFolder = EditorUtility.OpenFolderPanel("Select Folder to Export", Application.dataPath, "");
                            if (!string.IsNullOrEmpty(selectedFolder))
                            {
                                // Convert to relative path if possible
                                string relativePath = GetRelativePath(selectedFolder);
                                Undo.RecordObject(profile, "Browse Export Folder");
                                profile.foldersToExport[i] = relativePath;
                                EditorUtility.SetDirty(profile);
                            }
                        }
                        
                        if (GUILayout.Button("X", GUILayout.Width(25)))
                        {
                            Undo.RecordObject(profile, "Remove Export Folder");
                            profile.foldersToExport.RemoveAt(i);
                            EditorUtility.SetDirty(profile);
                            GUIUtility.ExitGUI();
                        }
                        
                        EditorGUILayout.EndHorizontal();
                    }
                    
                    EditorGUILayout.EndScrollView();
                    
                    if (GUILayout.Button("+ Add Folder", GUILayout.Height(25)))
                    {
                        Undo.RecordObject(profile, "Add Export Folder");
                        profile.foldersToExport.Add("Assets/");
                        EditorUtility.SetDirty(profile);
                    }
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Export Inspector
            EditorGUILayout.Space(5);
            showExportInspector = EditorGUILayout.BeginFoldoutHeaderGroup(showExportInspector, 
                $"Export Inspector ({profile.discoveredAssets.Count} assets)");
            if (showExportInspector)
            {
                DrawSection(() =>
                {
                    DrawExportInspector(profile);
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Unity Export Options
            EditorGUILayout.Space(5);
            showExportOptions = EditorGUILayout.BeginFoldoutHeaderGroup(showExportOptions, "Unity Export Options");
            if (showExportOptions)
            {
                DrawSection(() =>
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("includeDependencies"), new GUIContent("Include Dependencies"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("recurseFolders"), new GUIContent("Recurse Folders"));
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Exclusion Filters
            EditorGUILayout.Space(5);
            showExclusionFilters = EditorGUILayout.BeginFoldoutHeaderGroup(showExclusionFilters, "Exclusion Filters");
            if (showExclusionFilters)
            {
                DrawSection(() =>
                {
                    EditorGUILayout.HelpBox("Exclude files and folders from export using patterns", MessageType.Info);
                    
                    EditorGUILayout.LabelField("File Patterns", EditorStyles.boldLabel);
                    DrawStringList(profile.excludeFilePatterns, "*.tmp");
                    
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Folder Names", EditorStyles.boldLabel);
                    DrawStringList(profile.excludeFolderNames, ".git");
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Package Dependencies
            EditorGUILayout.Space(5);
            showDependencies = EditorGUILayout.BeginFoldoutHeaderGroup(showDependencies, "Package Dependencies");
            if (showDependencies)
            {
                DrawSection(() =>
                {
                    EditorGUILayout.HelpBox(
                        "Configure how package dependencies are handled:\n\n" +
                        "• Bundle: Include package files directly in export\n" +
                        "• Dependency: Add to package.json for auto-download",
                        MessageType.Info);
                    
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("generatePackageJson"), 
                        new GUIContent("Generate package.json"));
                    
                    EditorGUILayout.Space(5);
                    
                    // Scan buttons
                    EditorGUILayout.BeginHorizontal();
                    
                    if (GUILayout.Button("Scan Installed Packages", GUILayout.Height(30)))
                    {
                        ScanAndPopulateDependencies(profile);
                    }
                    
                    GUI.enabled = profile.dependencies.Count > 0 && profile.foldersToExport.Count > 0;
                    if (GUILayout.Button("Auto-Detect Used", GUILayout.Height(30)))
                    {
                        AutoDetectUsedDependencies(profile);
                    }
                    GUI.enabled = true;
                    
                    EditorGUILayout.EndHorizontal();
                    
                    if (profile.foldersToExport.Count == 0)
                    {
                        EditorGUILayout.HelpBox("Add export folders first, then use 'Auto-Detect Used' to find dependencies", MessageType.Info);
                    }
                    
                    // Dependency list
                    dependencyScrollPos = EditorGUILayout.BeginScrollView(dependencyScrollPos, GUILayout.MaxHeight(200));
                    
                    if (profile.dependencies.Count == 0)
                    {
                        EditorGUILayout.HelpBox("No dependencies configured. Click 'Scan Installed Packages' to auto-detect.", MessageType.Info);
                    }
                    else
                    {
                        for (int i = 0; i < profile.dependencies.Count; i++)
                        {
                            var dependency = profile.dependencies[i];
                            
                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            
                            EditorGUILayout.BeginHorizontal();
                            
                            dependency.enabled = EditorGUILayout.Toggle(dependency.enabled, GUILayout.Width(20));
                            
                            string label = string.IsNullOrEmpty(dependency.displayName) 
                                ? dependency.packageName 
                                : dependency.displayName;
                            label += $" v{dependency.packageVersion}";
                            
                            if (dependency.isVpmDependency)
                            {
                                GUI.color = new Color(0.6f, 0.8f, 1f);
                                EditorGUILayout.LabelField("[VPM] " + label, EditorStyles.boldLabel);
                                GUI.color = Color.white;
                            }
                            else
                            {
                                EditorGUILayout.LabelField(label);
                            }
                            
                            if (GUILayout.Button("X", GUILayout.Width(25)))
                            {
                                profile.dependencies.RemoveAt(i);
                                EditorUtility.SetDirty(profile);
                                GUIUtility.ExitGUI();
                            }
                            
                            EditorGUILayout.EndHorizontal();
                            
                            if (dependency.enabled)
                            {
                                EditorGUI.indentLevel++;
                                
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Export Mode:", GUILayout.Width(100));
                                dependency.exportMode = (DependencyExportMode)EditorGUILayout.EnumPopup(dependency.exportMode);
                                EditorGUILayout.EndHorizontal();
                                
                                if (dependency.exportMode == DependencyExportMode.Bundle)
                                {
                                    EditorGUILayout.HelpBox("Package files will be included in export", MessageType.None);
                                }
                                else
                                {
                                    string depType = dependency.isVpmDependency ? "vpmDependencies" : "dependencies";
                                    EditorGUILayout.HelpBox($"Will be added to package.json {depType}", MessageType.None);
                                }
                                
                                EditorGUI.indentLevel--;
                            }
                            
                            EditorGUILayout.EndVertical();
                            EditorGUILayout.Space(3);
                        }
                    }
                    
                    EditorGUILayout.EndScrollView();
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Assembly Obfuscation
            EditorGUILayout.Space(5);
            showObfuscation = EditorGUILayout.BeginFoldoutHeaderGroup(showObfuscation, "Assembly Obfuscation");
            if (showObfuscation)
            {
                DrawSection(() =>
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("enableObfuscation"), new GUIContent("Enable Obfuscation"));
                    
                    if (profile.enableObfuscation)
                    {
                        EditorGUI.indentLevel++;
                        
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("obfuscationPreset"), new GUIContent("Protection Level"));
                        
                        // Show preset description
                        string description = ConfuserExPresetGenerator.GetPresetDescription(profile.obfuscationPreset);
                        if (!string.IsNullOrEmpty(description))
                        {
                            EditorGUILayout.HelpBox(description, MessageType.None);
                        }
                        
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("stripDebugSymbols"), new GUIContent("Strip Debug Symbols"));
                        
                        EditorGUILayout.Space(5);
                        EditorGUILayout.LabelField("Assemblies to Obfuscate", EditorStyles.boldLabel);
                        
                        // Scan buttons
                        if (GUILayout.Button("Scan Assemblies (Export Folders + Dependencies)", GUILayout.Height(30)))
                        {
                            ScanAllAssemblies(profile);
                        }
                        
                        // Assembly list
                        assemblyScrollPos = EditorGUILayout.BeginScrollView(assemblyScrollPos, GUILayout.MaxHeight(200));
                        
                        if (profile.assembliesToObfuscate.Count == 0)
                        {
                            EditorGUILayout.HelpBox("No assemblies configured. Click 'Scan for Assemblies' to auto-detect.", MessageType.Info);
                        }
                        else
                        {
                            for (int i = 0; i < profile.assembliesToObfuscate.Count; i++)
                            {
                                var assembly = profile.assembliesToObfuscate[i];
                                
                                EditorGUILayout.BeginHorizontal();
                                
                                assembly.enabled = EditorGUILayout.Toggle(assembly.enabled, GUILayout.Width(20));
                                
                                // Show assembly name and validation status
                                var assemblyInfo = new AssemblyScanner.AssemblyInfo(assembly.assemblyName, assembly.asmdefPath);
                                
                                string label = assembly.assemblyName;
                                if (!assemblyInfo.exists)
                                {
                                    GUI.color = Color.yellow;
                                    label += " (DLL not found)";
                                }
                                else
                                {
                                    label += $" ({AssemblyScanner.FormatFileSize(assemblyInfo.fileSize)})";
                                }
                                
                                EditorGUILayout.LabelField(label);
                                GUI.color = Color.white;
                                
                                if (GUILayout.Button("X", GUILayout.Width(25)))
                                {
                                    profile.assembliesToObfuscate.RemoveAt(i);
                                    EditorUtility.SetDirty(profile);
                                    GUIUtility.ExitGUI();
                                }
                                
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                        
                        EditorGUILayout.EndScrollView();
                        
                        EditorGUI.indentLevel--;
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Obfuscation is disabled. Enable it to protect your assemblies.", MessageType.Info);
                    }
                    
                    // ConfuserEx status
                    EditorGUILayout.Space(5);
                    string statusInfo = ConfuserExManager.GetStatusInfo();
                    EditorGUILayout.HelpBox(statusInfo, MessageType.None);
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Export Settings
            EditorGUILayout.Space(5);
            showExportSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showExportSettings, "Export Settings");
            if (showExportSettings)
            {
                DrawSection(() =>
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("exportPath"), new GUIContent("Export Path"));
                    
                    if (GUILayout.Button("Browse", GUILayout.Width(60)))
                    {
                        string selectedPath = EditorUtility.OpenFolderPanel("Select Export Folder", "", "");
                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            profile.exportPath = selectedPath;
                            EditorUtility.SetDirty(profile);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    if (string.IsNullOrEmpty(profile.exportPath))
                    {
                        EditorGUILayout.HelpBox("Export path is empty. Packages will be saved to Desktop.", MessageType.Info);
                    }
                    
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("autoIncrementVersion"), new GUIContent("Auto-Increment Version"));
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Statistics
            EditorGUILayout.Space(5);
            showStatistics = EditorGUILayout.BeginFoldoutHeaderGroup(showStatistics, "Statistics");
            if (showStatistics)
            {
                DrawSection(() =>
                {
                    GUI.enabled = false;
                    EditorGUILayout.TextField("Last Export", profile.LastExportTime);
                    EditorGUILayout.IntField("Export Count", profile.ExportCount);
                    GUI.enabled = true;
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            
            // Validation
            EditorGUILayout.Space(10);
            if (!profile.Validate(out string errorMessage))
            {
                EditorGUILayout.HelpBox($"Validation Error: {errorMessage}", MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox("Profile is valid and ready to export", MessageType.Info);
            }
            
            // Quick Export Button
            EditorGUILayout.Space(10);
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.9f);
            if (GUILayout.Button("Export This Profile", GUILayout.Height(35)))
            {
                if (profile.Validate(out string error))
                {
                    ExportSingleProfile(profile);
                }
                else
                {
                    EditorUtility.DisplayDialog("Validation Error", error, "OK");
                }
            }
            GUI.backgroundColor = Color.white;
            
            serializedObject.ApplyModifiedProperties();
        }
        
        private void DrawSection(System.Action content)
        {
            EditorGUILayout.Space(5);
            
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.1f);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;
            
            content?.Invoke();
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawStringList(List<string> list, string placeholder)
        {
            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                list[i] = EditorGUILayout.TextField(list[i]);
                
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    list.RemoveAt(i);
                    EditorUtility.SetDirty(target);
                    GUIUtility.ExitGUI();
                }
                
                EditorGUILayout.EndHorizontal();
            }
            
            if (GUILayout.Button($"+ Add Pattern (e.g., {placeholder})", GUILayout.Height(25)))
            {
                list.Add(placeholder);
                EditorUtility.SetDirty(target);
            }
        }
        
        private void ScanAndPopulateDependencies(ExportProfile profile)
        {
            Debug.Log("[ExportProfileEditor] Scanning for installed packages...");
            
            var foundPackages = DependencyScanner.ScanInstalledPackages();
            
            if (foundPackages.Count == 0)
            {
                EditorUtility.DisplayDialog("No Packages Found", 
                    "No installed packages were found in the project.", 
                    "OK");
                return;
            }
            
            // Clear existing dependencies
            profile.dependencies.Clear();
            
            // Convert to PackageDependencies
            var dependencies = DependencyScanner.ConvertToPackageDependencies(foundPackages);
            
            foreach (var dep in dependencies)
            {
                profile.dependencies.Add(dep);
            }
            
            EditorUtility.SetDirty(profile);
            
            int vpmCount = dependencies.Count(d => d.isVpmDependency);
            int regularCount = dependencies.Count - vpmCount;
            
            string message = $"Found {dependencies.Count} packages:\n\n" +
                           $"• {vpmCount} VRChat (VPM) packages\n" +
                           $"• {regularCount} Unity packages\n\n" +
                           "Configure export mode for each dependency:\n" +
                           "• Bundle: Include files in export\n" +
                           "• Dependency: Auto-download when installed\n\n" +
                           "Tip: Use 'Auto-Detect Used' to automatically enable packages used in your export folders.";
            
            EditorUtility.DisplayDialog("Scan Complete", message, "OK");
            
            Debug.Log($"[ExportProfileEditor] Scan complete: {dependencies.Count} dependencies found");
        }
        
        private void AutoDetectUsedDependencies(ExportProfile profile)
        {
            if (profile.dependencies.Count == 0)
            {
                EditorUtility.DisplayDialog("No Dependencies", 
                    "Scan for installed packages first before auto-detecting.", 
                    "OK");
                return;
            }
            
            EditorUtility.DisplayProgressBar("Auto-Detecting Dependencies", "Scanning assets...", 0.5f);
            
            try
            {
                DependencyScanner.AutoDetectUsedDependencies(profile);
                
                EditorUtility.ClearProgressBar();
                
                int enabledCount = profile.dependencies.Count(d => d.enabled);
                int disabledCount = profile.dependencies.Count - enabledCount;
                
                string message = $"Auto-detection complete!\n\n" +
                               $"• {enabledCount} dependencies enabled (used in export)\n" +
                               $"• {disabledCount} dependencies disabled (not used)\n\n" +
                               "Review the dependency list and adjust as needed.";
                
                EditorUtility.DisplayDialog("Auto-Detection Complete", message, "OK");
                
                EditorUtility.SetDirty(profile);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        
        private void ScanAllAssemblies(ExportProfile profile)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Scanning Assemblies", "Initializing...", 0f);
                
                var foundAssemblies = new List<AssemblyScanner.AssemblyInfo>();
                
                // Scan export folders
                EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Scanning {profile.foldersToExport.Count} export folders...", 0.2f);
                var folderAssemblies = AssemblyScanner.ScanFolders(profile.foldersToExport);
                foundAssemblies.AddRange(folderAssemblies);
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Found {folderAssemblies.Count} assemblies in export folders", 0.5f);
                
                // Count bundled dependencies
                int bundledDepsCount = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Bundle);
                
                if (bundledDepsCount > 0)
                {
                    EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Scanning {bundledDepsCount} bundled dependencies...", 0.6f);
                }
                
                // Scan enabled dependencies
                var dependencyAssemblies = AssemblyScanner.ScanVpmPackages(profile.dependencies);
                foundAssemblies.AddRange(dependencyAssemblies);
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", $"Found {dependencyAssemblies.Count} assemblies in bundled dependencies", 0.8f);
                
                if (foundAssemblies.Count == 0)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("No Assemblies Found", 
                        "No .asmdef files were found in export folders or enabled dependencies.", 
                        "OK");
                    return;
                }
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", "Processing assembly list...", 0.9f);
                
                profile.assembliesToObfuscate.Clear();
                
                foreach (var assemblyInfo in foundAssemblies)
                {
                    var settings = new AssemblyObfuscationSettings(assemblyInfo.assemblyName, assemblyInfo.asmdefPath);
                    settings.enabled = assemblyInfo.exists;
                    profile.assembliesToObfuscate.Add(settings);
                }
                
                EditorUtility.SetDirty(profile);
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", "Complete!", 1.0f);
                
                int existingCount = foundAssemblies.Count(a => a.exists);
                int missingCount = foundAssemblies.Count - existingCount;
                
                string message = $"Found {foundAssemblies.Count} assemblies:\n\n" +
                               $"• {existingCount} ready to obfuscate\n" +
                               $"• {missingCount} not compiled yet\n\n" +
                               $"From export folders: {folderAssemblies.Count}\n" +
                               $"From bundled dependencies: {dependencyAssemblies.Count}";
                
                EditorUtility.DisplayDialog("Scan Complete", message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        
        private string GetRelativePath(string absolutePath)
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            
            if (absolutePath.StartsWith(projectPath))
            {
                string relative = absolutePath.Substring(projectPath.Length);
                if (relative.StartsWith("\\") || relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }
                return relative;
            }
            
            return absolutePath;
        }
        
        private void ExportSingleProfile(ExportProfile profile)
        {
            bool shouldExport = EditorUtility.DisplayDialog(
                "Export Package",
                $"Export package: {profile.packageName} v{profile.version}\n\n" +
                $"Folders: {profile.foldersToExport.Count}\n" +
                $"Obfuscation: {(profile.enableObfuscation ? "Enabled" : "Disabled")}\n\n" +
                $"Output: {profile.GetOutputFilePath()}",
                "Export",
                "Cancel"
            );
            
            if (!shouldExport)
                return;
            
            EditorUtility.DisplayProgressBar("Exporting Package", "Starting export...", 0f);
            
            try
            {
                var result = PackageBuilder.ExportPackage(profile, (progress, status) =>
                {
                    EditorUtility.DisplayProgressBar("Exporting Package", status, progress);
                });
                
                EditorUtility.ClearProgressBar();
                
                if (result.success)
                {
                    bool openFolder = EditorUtility.DisplayDialog(
                        "Export Successful",
                        $"Package exported successfully!\n\n" +
                        $"Output: {result.outputPath}\n" +
                        $"Files: {result.filesExported}\n" +
                        $"Assemblies Obfuscated: {result.assembliesObfuscated}\n" +
                        $"Build Time: {result.buildTimeSeconds:F2}s",
                        "Open Folder",
                        "OK"
                    );
                    
                    if (openFolder)
                    {
                        EditorUtility.RevealInFinder(result.outputPath);
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Export Failed",
                        $"Export failed: {result.errorMessage}\n\n" +
                        "Check the console for more details.",
                        "OK"
                    );
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        
        /// <summary>
        /// Draw the Export Inspector UI
        /// </summary>
        private void DrawExportInspector(ExportProfile profile)
        {
            EditorGUILayout.HelpBox(
                "The Export Inspector shows all assets discovered from your export folders. " +
                "Scan to discover assets, then deselect unwanted items or add folders to the permanent ignore list.",
                MessageType.Info);
            
            // Action buttons
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = profile.foldersToExport.Count > 0;
            if (GUILayout.Button("Scan Assets", GUILayout.Height(30)))
            {
                ScanAssetsForInspector(profile);
            }
            GUI.enabled = true;
            
            GUI.enabled = profile.discoveredAssets.Count > 0;
            if (GUILayout.Button("Clear Scan", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("Clear Scan", 
                    "Clear all discovered assets and rescan later?", "Clear", "Cancel"))
                {
                    profile.ClearScan();
                    EditorUtility.SetDirty(profile);
                }
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            // Show scan required message
            if (!profile.HasScannedAssets)
            {
                EditorGUILayout.HelpBox(
                    "Click 'Scan Assets' to discover all assets from your export folders.",
                    MessageType.Warning);
                return;
            }
            
            // Statistics
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Asset Statistics", EditorStyles.boldLabel);
            string summary = AssetCollector.GetAssetSummary(profile.discoveredAssets);
            EditorGUILayout.HelpBox(summary, MessageType.None);
            
            // Filter controls
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            inspectorSearchFilter = EditorGUILayout.TextField("Search:", inspectorSearchFilter);
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                inspectorSearchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            showOnlyIncluded = GUILayout.Toggle(showOnlyIncluded, "Show Only Included", GUILayout.Width(150));
            showOnlyExcluded = GUILayout.Toggle(showOnlyExcluded, "Show Only Excluded", GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();
            
            // Asset list
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Discovered Assets", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Include All", GUILayout.Width(80)))
            {
                foreach (var asset in profile.discoveredAssets)
                    asset.included = true;
                EditorUtility.SetDirty(profile);
            }
            
            if (GUILayout.Button("Exclude All", GUILayout.Width(80)))
            {
                foreach (var asset in profile.discoveredAssets)
                    asset.included = false;
                EditorUtility.SetDirty(profile);
            }
            EditorGUILayout.EndHorizontal();
            
            // Filter assets based on search and filters
            var filteredAssets = profile.discoveredAssets.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(inspectorSearchFilter))
            {
                filteredAssets = filteredAssets.Where(a => 
                    a.assetPath.IndexOf(inspectorSearchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }
            
            if (showOnlyIncluded)
                filteredAssets = filteredAssets.Where(a => a.included);
            
            if (showOnlyExcluded)
                filteredAssets = filteredAssets.Where(a => !a.included);
            
            var filteredList = filteredAssets.ToList();
            
            // Display asset list
            exportInspectorScrollPos = EditorGUILayout.BeginScrollView(exportInspectorScrollPos, GUILayout.MaxHeight(400));
            
            if (filteredList.Count == 0)
            {
                EditorGUILayout.HelpBox("No assets match the current filters.", MessageType.Info);
            }
            else
            {
                // Group by folder for better organization
                var groupedByFolder = filteredList
                    .Where(a => !a.isFolder)
                    .GroupBy(a => a.GetFolderPath())
                    .OrderBy(g => g.Key);
                
                foreach (var group in groupedByFolder)
                {
                    // Folder header
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    
                    EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel);
                    
                    // Create/Edit .yucpignore button
                    string folderFullPath = Path.GetFullPath(group.Key);
                    bool hasIgnoreFile = YucpIgnoreHandler.HasIgnoreFile(folderFullPath);
                    
                    if (hasIgnoreFile)
                    {
                        if (GUILayout.Button("Edit .yucpignore", GUILayout.Width(110)))
                        {
                            OpenYucpIgnoreFile(folderFullPath);
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Create .yucpignore", GUILayout.Width(120)))
                        {
                            CreateYucpIgnoreFile(profile, folderFullPath);
                        }
                    }
                    
                    // Add folder to ignore list button
                    if (GUILayout.Button("Add to Ignore List", GUILayout.Width(120)))
                    {
                        AddFolderToIgnoreList(profile, group.Key);
                    }
                    
                    EditorGUILayout.EndHorizontal();
                    
                    // Files in this folder
                    foreach (var asset in group)
                    {
                        EditorGUILayout.BeginHorizontal();
                        
                        // Include checkbox
                        bool wasIncluded = asset.included;
                        asset.included = EditorGUILayout.Toggle(asset.included, GUILayout.Width(20));
                        if (asset.included != wasIncluded)
                            EditorUtility.SetDirty(profile);
                        
                        // Asset icon
                        Texture2D icon = AssetDatabase.GetCachedIcon(asset.assetPath) as Texture2D;
                        if (icon != null)
                            GUILayout.Label(icon, GUILayout.Width(16), GUILayout.Height(16));
                        
                        // Asset name and type
                        string displayName = asset.GetDisplayName();
                        string label = $"{displayName}";
                        if (asset.isDependency)
                            label += " [Dependency]";
                        
                        EditorGUILayout.LabelField(label, GUILayout.MinWidth(200));
                        
                        // Asset type badge
                        GUI.color = GetAssetTypeColor(asset.assetType);
                        GUILayout.Label(asset.assetType, EditorStyles.miniLabel, GUILayout.Width(80));
                        GUI.color = Color.white;
                        
                        // File size
                        if (!asset.isFolder && asset.fileSize > 0)
                        {
                            GUILayout.Label(FormatBytes(asset.fileSize), EditorStyles.miniLabel, GUILayout.Width(60));
                        }
                        
                        EditorGUILayout.EndHorizontal();
                    }
                    
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }
            }
            
            EditorGUILayout.EndScrollView();
            
            // Permanent ignore list
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Permanent Ignore List", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Folders in this list will be permanently ignored from all exports (like .gitignore).",
                MessageType.Info);
            
            if (profile.permanentIgnoreFolders == null || profile.permanentIgnoreFolders.Count == 0)
            {
                EditorGUILayout.HelpBox("No folders in ignore list.", MessageType.None);
            }
            else
            {
                for (int i = profile.permanentIgnoreFolders.Count - 1; i >= 0; i--)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(profile.permanentIgnoreFolders[i]);
                    
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        profile.permanentIgnoreFolders.RemoveAt(i);
                        EditorUtility.SetDirty(profile);
                    }
                    
                    EditorGUILayout.EndHorizontal();
                }
            }
            
            if (GUILayout.Button("+ Add Folder to Ignore List", GUILayout.Height(25)))
            {
                string selectedFolder = EditorUtility.OpenFolderPanel("Select Folder to Ignore", Application.dataPath, "");
                if (!string.IsNullOrEmpty(selectedFolder))
                {
                    string relativePath = GetRelativePath(selectedFolder);
                    AddFolderToIgnoreList(profile, relativePath);
                }
            }
        }
        
        /// <summary>
        /// Scan assets for the inspector
        /// </summary>
        private void ScanAssetsForInspector(ExportProfile profile)
        {
            EditorUtility.DisplayProgressBar("Scanning Assets", "Discovering assets from export folders...", 0f);
            
            try
            {
                profile.discoveredAssets = AssetCollector.ScanExportFolders(profile, profile.includeDependencies);
                profile.MarkScanned();
                EditorUtility.SetDirty(profile);
                
                EditorUtility.DisplayDialog(
                    "Scan Complete",
                    $"Discovered {profile.discoveredAssets.Count} assets.\n\n" +
                    AssetCollector.GetAssetSummary(profile.discoveredAssets),
                    "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ExportProfileEditor] Asset scan failed: {ex.Message}");
                EditorUtility.DisplayDialog("Scan Failed", $"Failed to scan assets:\n{ex.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        
        /// <summary>
        /// Add a folder to the permanent ignore list
        /// </summary>
        private void AddFolderToIgnoreList(ExportProfile profile, string folderPath)
        {
            if (profile.permanentIgnoreFolders == null)
                profile.permanentIgnoreFolders = new List<string>();
            
            if (!profile.permanentIgnoreFolders.Contains(folderPath))
            {
                profile.permanentIgnoreFolders.Add(folderPath);
                EditorUtility.SetDirty(profile);
                
                // Optionally rescan after adding to ignore list
                if (EditorUtility.DisplayDialog(
                    "Added to Ignore List",
                    $"Added '{folderPath}' to ignore list.\n\nRescan assets now to apply changes?",
                    "Rescan",
                    "Later"))
                {
                    ScanAssetsForInspector(profile);
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Already Ignored", $"'{folderPath}' is already in the ignore list.", "OK");
            }
        }
        
        /// <summary>
        /// Get color for asset type badge
        /// </summary>
        private Color GetAssetTypeColor(string assetType)
        {
            return assetType switch
            {
                "Script" => new Color(0.3f, 0.7f, 0.3f),
                "Prefab" => new Color(0.3f, 0.5f, 0.9f),
                "Material" => new Color(0.9f, 0.5f, 0.3f),
                "Texture" => new Color(0.8f, 0.3f, 0.8f),
                "Scene" => new Color(0.9f, 0.9f, 0.3f),
                "Shader" => new Color(0.5f, 0.9f, 0.9f),
                "Assembly" => new Color(0.9f, 0.3f, 0.3f),
                _ => Color.gray
            };
        }
        
        /// <summary>
        /// Format bytes to human-readable size
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }
        
        /// <summary>
        /// Create a .yucpignore file in a folder
        /// </summary>
        private void CreateYucpIgnoreFile(ExportProfile profile, string folderPath)
        {
            if (YucpIgnoreHandler.CreateIgnoreFile(folderPath))
            {
                AssetDatabase.Refresh();
                
                if (EditorUtility.DisplayDialog(
                    "Created .yucpignore",
                    $"Created .yucpignore file in:\n{folderPath}\n\nOpen the file to edit ignore patterns?",
                    "Open",
                    "Later"))
                {
                    OpenYucpIgnoreFile(folderPath);
                }
                
                // Optionally rescan
                if (EditorUtility.DisplayDialog(
                    "Rescan Assets",
                    "Rescan assets now to apply the new ignore file?",
                    "Rescan",
                    "Later"))
                {
                    ScanAssetsForInspector(profile);
                }
            }
        }
        
        /// <summary>
        /// Open a .yucpignore file in the default editor
        /// </summary>
        private void OpenYucpIgnoreFile(string folderPath)
        {
            string ignoreFilePath = YucpIgnoreHandler.GetIgnoreFilePath(folderPath);
            
            if (File.Exists(ignoreFilePath))
            {
                // Open in default text editor
                System.Diagnostics.Process.Start(ignoreFilePath);
            }
            else
            {
                EditorUtility.DisplayDialog("File Not Found", $".yucpignore file not found:\n{ignoreFilePath}", "OK");
            }
        }
    }
}

