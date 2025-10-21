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
    private bool showExportOptions = false;
    private bool showExclusionFilters = false;
    private bool showDependencies = true;
    private bool showObfuscation = true;
    private bool showExportSettings = false;
    private bool showStatistics = false;
        
    private Vector2 folderScrollPos;
    private Vector2 dependencyScrollPos;
    private Vector2 assemblyScrollPos;
        
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
                        
                        profile.foldersToExport[i] = EditorGUILayout.TextField(profile.foldersToExport[i]);
                        
                        if (GUILayout.Button("Browse", GUILayout.Width(60)))
                        {
                            string selectedFolder = EditorUtility.OpenFolderPanel("Select Folder to Export", Application.dataPath, "");
                            if (!string.IsNullOrEmpty(selectedFolder))
                            {
                                // Convert to relative path if possible
                                string relativePath = GetRelativePath(selectedFolder);
                                profile.foldersToExport[i] = relativePath;
                            }
                        }
                        
                        if (GUILayout.Button("X", GUILayout.Width(25)))
                        {
                            profile.foldersToExport.RemoveAt(i);
                            EditorUtility.SetDirty(profile);
                            GUIUtility.ExitGUI();
                        }
                        
                        EditorGUILayout.EndHorizontal();
                    }
                    
                    EditorGUILayout.EndScrollView();
                    
                    if (GUILayout.Button("+ Add Folder", GUILayout.Height(25)))
                    {
                        profile.foldersToExport.Add("Assets/");
                        EditorUtility.SetDirty(profile);
                    }
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
                        if (GUILayout.Button("Scan for Assemblies in Export Folders", GUILayout.Height(30)))
                        {
                            ScanAndPopulateAssemblies(profile);
                        }
                        
                        if (GUILayout.Button("Scan VPM Packages", GUILayout.Height(25)))
                        {
                            ScanVpmPackagesForObfuscation(profile);
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
        
        private void ScanAndPopulateAssemblies(ExportProfile profile)
        {
            Debug.Log("[ExportProfileEditor] Scanning for assemblies...");
            
            var foundAssemblies = AssemblyScanner.ScanFolders(profile.foldersToExport);
            
            if (foundAssemblies.Count == 0)
            {
                EditorUtility.DisplayDialog("No Assemblies Found", 
                    "No .asmdef files were found in the selected export folders.\n\n" +
                    "Make sure you've added folders that contain assembly definition files.", 
                    "OK");
                return;
            }
            
            // Clear existing assemblies
            profile.assembliesToObfuscate.Clear();
            
            // Add found assemblies
            foreach (var assemblyInfo in foundAssemblies)
            {
                var settings = new AssemblyObfuscationSettings(assemblyInfo.assemblyName, assemblyInfo.asmdefPath);
                
                // Enable by default only if DLL exists
                settings.enabled = assemblyInfo.exists;
                
                profile.assembliesToObfuscate.Add(settings);
            }
            
            EditorUtility.SetDirty(profile);
            
            int existingCount = foundAssemblies.Count(a => a.exists);
            int missingCount = foundAssemblies.Count - existingCount;
            
            string message = $"Found {foundAssemblies.Count} assemblies:\n\n" +
                           $"• {existingCount} ready to obfuscate\n" +
                           $"• {missingCount} not compiled yet (build project first)";
            
            EditorUtility.DisplayDialog("Scan Complete", message, "OK");
            
            Debug.Log($"[ExportProfileEditor] Scan complete: {foundAssemblies.Count} assemblies found");
        }
        
        private void ScanVpmPackagesForObfuscation(ExportProfile profile)
        {
            var foundAssemblies = AssemblyScanner.ScanVpmPackages(profile.dependencies);
            
            if (foundAssemblies.Count == 0)
            {
                EditorUtility.DisplayDialog("No VPM Assemblies Found", 
                    "No .asmdef files were found in enabled dependency packages. Make sure you have dependencies enabled in the 'Package Dependencies' section.", 
                    "OK");
                return;
            }
            
            // Add to existing list instead of clearing
            foreach (var assemblyInfo in foundAssemblies)
            {
                // Check if already exists
                if (!profile.assembliesToObfuscate.Any(a => a.assemblyName == assemblyInfo.assemblyName))
                {
                    var settings = new AssemblyObfuscationSettings(assemblyInfo.assemblyName, assemblyInfo.asmdefPath);
                    settings.enabled = assemblyInfo.exists;
                    profile.assembliesToObfuscate.Add(settings);
                }
            }
            
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            
            int existingCount = foundAssemblies.Count(a => a.exists);
            EditorUtility.DisplayDialog("VPM Scan Complete", 
                $"Found {foundAssemblies.Count} VPM assemblies ({existingCount} compiled)", 
                "OK");
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
    }
}

