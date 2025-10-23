using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Main Package Exporter window with profile management and batch export capabilities.
    /// Features YUCP-styled UI with logo, progress tracking, and multi-profile support.
    /// </summary>
    public class YUCPPackageExporterWindow : EditorWindow
    {
        [MenuItem("Tools/YUCP/Package Exporter")]
        public static void ShowWindow()
        {
            var window = GetWindow<YUCPPackageExporterWindow>();
            window.titleContent = new GUIContent("YUCP Package Exporter");
            window.minSize = new Vector2(700, 600);
            window.Show();
        }
        
        private List<ExportProfile> allProfiles = new List<ExportProfile>();
        private ExportProfile selectedProfile;
        private int selectedProfileIndex = -1;
        
        private Vector2 profileListScrollPos;
        private Vector2 mainScrollPos;
        private Vector2 exportInspectorScrollPos;
        private bool isExporting = false;
        private float currentProgress = 0f;
        private string currentStatus = "";
        
        // Multi-selection state for profiles
        private HashSet<int> selectedProfileIndices = new HashSet<int>();
        private int lastClickedProfileIndex = -1;
        
        // Export Inspector state
        private string inspectorSearchFilter = "";
        private bool showOnlyIncluded = false;
        private bool showOnlyExcluded = false;
        private bool showExportInspector = false;
        
        private Texture2D logoTexture;
        private GUIStyle headerStyle;
        private GUIStyle profileButtonStyle;
        private GUIStyle selectedProfileButtonStyle;
        private GUIStyle sectionHeaderStyle;
        
        private void OnEnable()
        {
            LoadProfiles();
            LoadResources();
        }
        
        private void LoadResources()
        {
            // Load YUCP logo
            logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.components/Resources/Icons/Logo@2x.png");
        }
        
        private void InitializeStyles()
        {
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.largeLabel);
                headerStyle.fontSize = 20;
                headerStyle.normal.textColor = new Color(0.8f, 0.9f, 1f);
                headerStyle.alignment = TextAnchor.MiddleLeft;
            }
            
            if (sectionHeaderStyle == null)
            {
                sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel);
                sectionHeaderStyle.fontSize = 14;
                sectionHeaderStyle.normal.textColor = new Color(0.2f, 0.75f, 0.73f); // Teal
            }
            
            if (profileButtonStyle == null)
            {
                profileButtonStyle = new GUIStyle(GUI.skin.button);
                profileButtonStyle.alignment = TextAnchor.MiddleLeft;
                profileButtonStyle.padding = new RectOffset(10, 10, 8, 8);
                profileButtonStyle.normal.textColor = Color.white;
                profileButtonStyle.fontSize = 12;
                profileButtonStyle.wordWrap = false;
                profileButtonStyle.clipping = TextClipping.Overflow;
                profileButtonStyle.fixedHeight = 0;
                profileButtonStyle.border = new RectOffset(4, 4, 4, 4);
            }
            
            if (selectedProfileButtonStyle == null)
            {
                selectedProfileButtonStyle = new GUIStyle(GUI.skin.button);
                selectedProfileButtonStyle.alignment = TextAnchor.MiddleLeft;
                selectedProfileButtonStyle.padding = new RectOffset(10, 10, 8, 8);
                selectedProfileButtonStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.75f, 0.73f, 0.5f));
                selectedProfileButtonStyle.normal.textColor = new Color(0.2f, 0.75f, 0.73f);
                selectedProfileButtonStyle.fontStyle = FontStyle.Bold;
                selectedProfileButtonStyle.fontSize = 12;
                selectedProfileButtonStyle.wordWrap = false;
                selectedProfileButtonStyle.clipping = TextClipping.Overflow;
                selectedProfileButtonStyle.fixedHeight = 0;
                selectedProfileButtonStyle.border = new RectOffset(4, 4, 4, 4);
            }
        }
        
        private Texture2D MakeTex(int width, int height, Color color)
        {
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = color;
            
            var tex = new Texture2D(width, height);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }
        
        private void OnGUI()
        {
            InitializeStyles();
            
            // Dark background
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.035f, 0.035f, 0.035f, 1f));
            
            GUILayout.BeginVertical();
            
            // Fixed height header
            GUILayout.BeginVertical(GUILayout.Height(100));
            DrawHeader();
            GUILayout.EndVertical();
            
            // Main content area - fixed left panel, scrollable right panel
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            
            DrawProfileList();
            
            mainScrollPos = GUILayout.BeginScrollView(mainScrollPos, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            DrawSelectedProfile();
            GUILayout.EndScrollView();
            
            GUILayout.EndHorizontal();
            
            // Sticky export buttons at bottom
            GUILayout.FlexibleSpace();
            DrawExportButtons();
            
            if (isExporting)
            {
                DrawProgressBar();
            }
            
            GUILayout.EndVertical();
        }
        
        private void DrawHeader()
        {
            GUILayout.Space(10);
            
            // Logo and title with fixed height
            GUILayout.BeginHorizontal(GUILayout.Height(60));
            GUILayout.Space(20);
            
            if (logoTexture != null)
            {
                // Smaller logo to fit in fixed height
                float logoHeight = 50;
                float logoWidth = logoHeight * (2020f / 865f); // ~116px
                
                // Create a properly scaled texture display
                Rect logoRect = GUILayoutUtility.GetRect(logoWidth, logoHeight, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(logoRect, logoTexture, ScaleMode.ScaleToFit, true);
                GUILayout.Space(15);
            }
            
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Package Exporter", headerStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            DrawHorizontalLine();
        }
        
        private void DrawProfileList()
        {
            GUILayout.Space(20);
            
            GUILayout.BeginVertical(GUILayout.Width(270), GUILayout.ExpandHeight(true));
            
            GUILayout.Label("Export Profiles", sectionHeaderStyle);
            GUILayout.Space(5);
            
            if (selectedProfileIndices.Count > 1)
            {
                EditorGUILayout.HelpBox(
                    $"{selectedProfileIndices.Count} profiles selected. Use the export button below to export only these.",
                    MessageType.None);
                GUILayout.Space(5);
            }
            
            // Profile list - expands to fill available height
            profileListScrollPos = GUILayout.BeginScrollView(profileListScrollPos, GUI.skin.box, GUILayout.ExpandHeight(true));
            
            if (allProfiles.Count == 0)
            {
                GUILayout.Label("No profiles found", EditorStyles.centeredGreyMiniLabel);
                GUILayout.Label("Create one using the button below", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                for (int i = 0; i < allProfiles.Count; i++)
                {
                    var profile = allProfiles[i];
                    if (profile == null)
                        continue;
                    
                    bool isSelected = selectedProfileIndices.Contains(i);
                    
                    // Create a custom clickable area with proper text display
                    GUILayout.BeginVertical();
                    
                    // Background box for the clickable area
                    var boxStyle = new GUIStyle(GUI.skin.box);
                    if (isSelected)
                    {
                        boxStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.75f, 0.73f, 0.3f));
                    }
                    
                    GUILayout.BeginVertical(boxStyle);
                    
                    // Add padding around the text
                    GUILayout.Space(8);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(10);
                    
                    // Text with proper styling
                    var labelStyle = new GUIStyle(EditorStyles.label);
                    labelStyle.normal.textColor = isSelected ? new Color(0.2f, 0.75f, 0.73f) : Color.white;
                    labelStyle.fontSize = 12;
                    labelStyle.fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal;
                    
                    GUILayout.Label(GetProfileButtonLabel(profile), labelStyle);
                    
                    GUILayout.FlexibleSpace();
                    GUILayout.Space(10);
                    GUILayout.EndHorizontal();
                    GUILayout.Space(8);
                    
                    GUILayout.EndVertical();
                    
                    // Make the entire area clickable with multi-selection support
                    if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                    {
                        HandleProfileSelection(i, Event.current);
                        Event.current.Use();
                    }
                    
                    GUILayout.EndVertical();
                    GUILayout.Space(5);
                }
            }
            
            GUILayout.EndScrollView();
            
            // Profile management buttons
            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button("+ New", GUILayout.Height(30)))
            {
                CreateNewProfile();
            }
            
            GUI.enabled = selectedProfile != null;
            if (GUILayout.Button("Clone", GUILayout.Height(30)))
            {
                CloneProfile(selectedProfile);
            }
            
            if (GUILayout.Button("Delete", GUILayout.Height(30)))
            {
                DeleteProfile(selectedProfile);
            }
            GUI.enabled = true;
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            if (GUILayout.Button("Refresh Profiles", GUILayout.Height(25)))
            {
                LoadProfiles();
            }
            
            GUILayout.EndVertical();
            
            GUILayout.Space(20);
        }
        
        private void DrawSelectedProfile()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            
            if (selectedProfile == null)
            {
                GUILayout.Space(20);
                GUILayout.Label("No profile selected", sectionHeaderStyle);
                GUILayout.Space(10);
                GUILayout.Label("Select a profile from the list or create a new one.", EditorStyles.wordWrappedLabel);
            }
            else
            {
                GUILayout.Label($"Selected: {selectedProfile.name}", sectionHeaderStyle);
                GUILayout.Space(10);
                
                DrawProfileSummary(selectedProfile);
            }
            
            GUILayout.EndVertical();
            GUILayout.Space(20);
        }
        
        private void DrawProfileSummary(ExportProfile profile)
        {
            // Section header style
            var summaryStyle = new GUIStyle(EditorStyles.boldLabel);
            
            // Package Metadata Section
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label("Package Metadata", summaryStyle, GUILayout.Height(20));
            GUILayout.Space(5);
            
            // Editable fields
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Package Name", "Unique identifier for your package (e.g., com.yucp.mypackage)"), GUILayout.Width(120));
            profile.packageName = EditorGUILayout.TextField(profile.packageName);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Version", "Package version following semantic versioning (e.g., 1.0.0)"), GUILayout.Width(120));
            profile.version = EditorGUILayout.TextField(profile.version);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Author", "Your name or organization"), GUILayout.Width(120));
            profile.author = EditorGUILayout.TextField(profile.author);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Icon", "Package icon that will be displayed in the Unity Package Manager"), GUILayout.Width(120));
            profile.icon = (Texture2D)EditorGUILayout.ObjectField(profile.icon, typeof(Texture2D), false);
            
            if (GUILayout.Button(new GUIContent("Browse", "Select an icon file from your computer"), GUILayout.Width(60)))
            {
                string iconPath = EditorUtility.OpenFilePanel("Select Package Icon", "", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(iconPath))
                {
                    // Import the icon file
                    string projectPath = "Assets/YUCP/ExportProfiles/Icons/";
                    if (!AssetDatabase.IsValidFolder("Assets/YUCP/ExportProfiles/Icons"))
                    {
                        AssetDatabase.CreateFolder("Assets/YUCP/ExportProfiles", "Icons");
                    }
                    
                    string fileName = Path.GetFileName(iconPath);
                    string targetPath = projectPath + fileName;
                    
                    // Copy and import the file
                    File.Copy(iconPath, targetPath, true);
                    AssetDatabase.ImportAsset(targetPath);
                    AssetDatabase.Refresh();
                    
                    // Load and assign the texture
                    profile.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssets();
                }
            }
            EditorGUILayout.EndHorizontal();
            
            if (profile.icon != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(profile.icon, GUILayout.Width(64), GUILayout.Height(64));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(new GUIContent("Description", "Brief description of what your package does"));
            profile.description = EditorGUILayout.TextArea(profile.description, GUILayout.Height(60));
            
            // Mark dirty when any field changes
            if (GUI.changed)
            {
                EditorUtility.SetDirty(profile);
            }
            
            GUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            // Quick stats
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label("Quick Summary", summaryStyle, GUILayout.Height(20));
            GUILayout.Space(5);
            
            EditorGUILayout.LabelField("Folders to Export", profile.foldersToExport.Count.ToString());
            
            // Dependencies summary
            if (profile.dependencies.Count > 0)
            {
                int bundled = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Bundle);
                int referenced = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Dependency);
                EditorGUILayout.LabelField("Dependencies", $"{bundled} bundled, {referenced} referenced");
            }
            
            EditorGUILayout.LabelField("Obfuscation", profile.enableObfuscation ? "Enabled" : "Disabled");
            
            if (profile.enableObfuscation)
            {
                int enabledCount = profile.assembliesToObfuscate.Count(a => a.enabled);
                EditorGUILayout.LabelField("Assemblies", $"{enabledCount} selected");
                EditorGUILayout.LabelField("Protection Level", profile.obfuscationPreset.ToString());
            }
            
            EditorGUILayout.LabelField("Output", string.IsNullOrEmpty(profile.exportPath) ? "Desktop" : profile.exportPath);
            
            if (!string.IsNullOrEmpty(profile.LastExportTime))
            {
                EditorGUILayout.LabelField("Last Export", profile.LastExportTime);
            }
            
            GUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            // Validation
            if (!profile.Validate(out string errorMessage))
            {
                EditorGUILayout.HelpBox($"Validation Error: {errorMessage}", MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox("Profile is valid and ready to export", MessageType.Info);
            }
            
            GUILayout.Space(10);
            
            // Export Options Section
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            
            GUILayout.Label("Export Options", summaryStyle, GUILayout.Height(20));
            GUILayout.Space(5);
            
            profile.includeDependencies = EditorGUILayout.Toggle(new GUIContent("Include Dependencies", "Include all dependency files directly in the exported package"), profile.includeDependencies);
            profile.recurseFolders = EditorGUILayout.Toggle(new GUIContent("Recurse Folders", "Search subfolders when collecting assets to export"), profile.recurseFolders);
            profile.generatePackageJson = EditorGUILayout.Toggle(new GUIContent("Generate package.json", "Create a package.json file with dependency information for VPM compatibility"), profile.generatePackageJson);
            profile.autoIncrementVersion = EditorGUILayout.Toggle(new GUIContent("Auto-Increment Version", "Automatically increment the version number on each export"), profile.autoIncrementVersion);
            
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(new GUIContent("Export Path", "Folder where the exported .unitypackage file will be saved"), GUILayout.Width(120));
            profile.exportPath = EditorGUILayout.TextField(profile.exportPath, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(new GUIContent("Browse", "Select a folder to save the exported package"), GUILayout.Width(60)))
            {
                string selectedPath = EditorUtility.OpenFolderPanel("Select Export Folder", "", "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    profile.exportPath = selectedPath;
                }
            }
            EditorGUILayout.EndHorizontal();
            
            if (string.IsNullOrEmpty(profile.exportPath))
            {
                EditorGUILayout.HelpBox("Empty path = Desktop", MessageType.Info);
            }
            
            if (GUI.changed)
            {
                EditorUtility.SetDirty(profile);
            }
            
            GUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            // Folders section
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            
            GUILayout.Label("Export Folders", summaryStyle, GUILayout.Height(20));
            GUILayout.Space(5);
            
            if (profile.foldersToExport.Count == 0)
            {
                EditorGUILayout.HelpBox("No folders added. Add folders to export.", MessageType.Warning);
            }
            else
            {
                for (int i = 0; i < profile.foldersToExport.Count; i++)
                {
                    GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField(profile.foldersToExport[i], GUILayout.ExpandWidth(true));
                    
                    if (GUILayout.Button("X", GUILayout.Width(25), GUILayout.Height(20)))
                    {
                        profile.foldersToExport.RemoveAt(i);
                        EditorUtility.SetDirty(profile);
                        AssetDatabase.SaveAssets();
                        GUIUtility.ExitGUI();
                    }
                    
                    GUILayout.EndHorizontal();
                }
            }
            
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            if (GUILayout.Button(new GUIContent("+ Add Folder", "Add a folder to the list of folders that will be exported"), GUILayout.Height(30), GUILayout.ExpandWidth(true)))
            {
                string selectedFolder = EditorUtility.OpenFolderPanel("Select Folder to Export", Application.dataPath, "");
                if (!string.IsNullOrEmpty(selectedFolder))
                {
                    string relativePath = GetRelativePath(selectedFolder);
                    profile.foldersToExport.Add(relativePath);
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssets();
                }
            }
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            // Export Inspector section
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Export Inspector ({profile.discoveredAssets.Count} assets)", summaryStyle, GUILayout.Height(20));
            showExportInspector = GUILayout.Toggle(showExportInspector, showExportInspector ? "▼" : "▶", EditorStyles.label, GUILayout.Width(20));
            GUILayout.EndHorizontal();
            
            if (showExportInspector)
            {
                GUILayout.Space(5);
                DrawExportInspector(profile, summaryStyle);
            }
            
            GUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            // Dependencies section
            GUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label("Package Dependencies", summaryStyle, GUILayout.Height(20));
            GUILayout.Space(5);
            
            EditorGUILayout.HelpBox(
                "Bundle: Include dependency files directly in the exported package\n" +
                "Dependency: Add to package.json for automatic download when package is installed",
                MessageType.Info);
            
            GUILayout.Space(5);
            
            if (profile.dependencies.Count == 0)
            {
                EditorGUILayout.HelpBox("No dependencies configured. Add manually or scan.", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < profile.dependencies.Count; i++)
                {
                    var dep = profile.dependencies[i];
                    DrawDependencyCard(dep, i, profile);
                }
            }
            
            GUILayout.Space(5);
            
            // Select all/none buttons
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            
            if (GUILayout.Button("Select All", GUILayout.Height(25)))
            {
                foreach (var dep in profile.dependencies)
                {
                    dep.enabled = true;
                }
                EditorUtility.SetDirty(profile);
            }
            
            if (GUILayout.Button("Deselect All", GUILayout.Height(25)))
            {
                foreach (var dep in profile.dependencies)
                {
                    dep.enabled = false;
                }
                EditorUtility.SetDirty(profile);
            }
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            
            if (GUILayout.Button(new GUIContent("+ Add Dependency", "Add a new package dependency manually"), GUILayout.Height(30), GUILayout.ExpandWidth(true)))
            {
                var newDep = new PackageDependency("com.example.package", "1.0.0", "Example Package", false);
                profile.dependencies.Add(newDep);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            
            if (GUILayout.Button(new GUIContent("Scan Installed", "Automatically detect and add installed packages as dependencies"), GUILayout.Height(30), GUILayout.ExpandWidth(true)))
            {
                ScanProfileDependencies(profile);
            }
            
            GUILayout.EndHorizontal();
            
            if (GUI.changed)
            {
                EditorUtility.SetDirty(profile);
            }
            
            GUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            // Obfuscation section
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            
            GUILayout.Label("Assembly Obfuscation", summaryStyle, GUILayout.Height(20));
            GUILayout.Space(5);
            
            profile.enableObfuscation = EditorGUILayout.Toggle(new GUIContent("Enable Obfuscation", "Protect compiled assemblies using ConfuserEx to prevent reverse engineering"), profile.enableObfuscation);
            
            if (profile.enableObfuscation)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(new GUIContent("Protection Level", "Choose how aggressively to obfuscate the code"), GUILayout.Width(120));
                profile.obfuscationPreset = (ConfuserExPreset)EditorGUILayout.EnumPopup(profile.obfuscationPreset, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
                
                string presetDesc = ConfuserExPresetGenerator.GetPresetDescription(profile.obfuscationPreset);
                if (!string.IsNullOrEmpty(presetDesc))
                {
                    EditorGUILayout.HelpBox(presetDesc, MessageType.None);
                }
                
                profile.stripDebugSymbols = EditorGUILayout.Toggle(new GUIContent("Strip Debug Symbols", "Remove debug information to reduce file size and improve protection"), profile.stripDebugSymbols);
                
                EditorGUILayout.Space(5);
                
                if (GUILayout.Button(new GUIContent("Scan Assemblies", "Find assemblies in export folders and enabled dependencies"), GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                {
                    ScanAllAssemblies(profile);
                }
                
                EditorGUILayout.Space(5);
                
                if (profile.assembliesToObfuscate.Count > 0)
                {
                    EditorGUILayout.LabelField($"Found Assemblies ({profile.assembliesToObfuscate.Count(a => a.enabled)}/{profile.assembliesToObfuscate.Count} selected):", EditorStyles.boldLabel);
                    
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    for (int i = 0; i < profile.assembliesToObfuscate.Count; i++)
                    {
                        var assembly = profile.assembliesToObfuscate[i];
                        
                        string displayName = assembly.assemblyName;
                        if (!new AssemblyScanner.AssemblyInfo(assembly.assemblyName, assembly.asmdefPath).exists)
                        {
                            displayName += " (not compiled)";
                        }
                        
                        assembly.enabled = EditorGUILayout.ToggleLeft(displayName, assembly.enabled);
                    }
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    EditorGUILayout.HelpBox("No assemblies found. Click 'Scan Assemblies' to find assemblies in your export folders and dependencies.", MessageType.Info);
                }
                
                EditorGUI.indentLevel--;
            }
            
            if (GUI.changed)
            {
                EditorUtility.SetDirty(profile);
            }
            
            GUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            // Quick actions
            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button(new GUIContent("Open in Inspector", "Open this profile in the Inspector for detailed editing"), GUILayout.Height(30)))
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            }
            
            if (GUILayout.Button(new GUIContent("Save Changes", "Save all changes made to this profile"), GUILayout.Height(30)))
            {
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Package Exporter] Saved changes to {profile.name}");
            }
            
            GUILayout.EndHorizontal();
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
        
        
        private void DrawProgressBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            
            GUILayout.BeginVertical();
            
            GUILayout.Label(currentStatus, EditorStyles.label);
            
            Rect progressRect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(progressRect, currentProgress, $"{(currentProgress * 100):F0}%");
            
            GUILayout.EndVertical();
            
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
        }
        
        private void DrawHorizontalLine()
        {
            Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
        
        private string GetProfileButtonLabel(ExportProfile profile)
        {
            string label = profile.packageName;
            if (!string.IsNullOrEmpty(profile.version))
            {
                label += $" v{profile.version}";
            }
            
            return label;
        }
        
        private void LoadProfiles()
        {
            allProfiles.Clear();
            
            // Find all ExportProfile assets in the project
            string[] guids = AssetDatabase.FindAssets("t:ExportProfile");
            
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<ExportProfile>(path);
                
                if (profile != null)
                {
                    allProfiles.Add(profile);
                }
            }
            
            allProfiles = allProfiles.OrderBy(p => p.packageName).ToList();
            
            Debug.Log($"[PackageExporter] Loaded {allProfiles.Count} export profiles");
            
            // Reselect if we had a selection
            if (selectedProfile != null)
            {
                selectedProfileIndex = allProfiles.IndexOf(selectedProfile);
                if (selectedProfileIndex < 0)
                {
                    selectedProfile = null;
                }
            }
        }
        
        private void CreateNewProfile()
        {
            // Ensure export profiles directory exists
            string profilesDir = "Assets/YUCP/ExportProfiles";
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
            }
            
            // Create new profile
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            profile.packageName = "NewPackage";
            profile.version = "1.0.0";
            
            // Generate unique asset path
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(profilesDir, "NewExportProfile.asset"));
            
            AssetDatabase.CreateAsset(profile, assetPath);
            AssetDatabase.SaveAssets();
            
            Debug.Log($"[PackageExporter] Created new export profile: {assetPath}");
            
            LoadProfiles();
            
            // Select the new profile
            selectedProfile = profile;
            selectedProfileIndex = allProfiles.IndexOf(profile);
            
            // Ping it in the project window
            EditorGUIUtility.PingObject(profile);
        }
        
        private void CloneProfile(ExportProfile source)
        {
            if (source == null)
                return;
            
            // Create a copy
            var clone = Instantiate(source);
            clone.name = source.name + " (Clone)";
            
            // Ensure directory exists
            string profilesDir = "Assets/YUCP/ExportProfiles";
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
            }
            
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(profilesDir, clone.name + ".asset"));
            
            AssetDatabase.CreateAsset(clone, assetPath);
            AssetDatabase.SaveAssets();
            
            Debug.Log($"[PackageExporter] Cloned profile: {assetPath}");
            
            LoadProfiles();
            
            selectedProfile = clone;
            selectedProfileIndex = allProfiles.IndexOf(clone);
        }
        
        private void DeleteProfile(ExportProfile profile)
        {
            if (profile == null)
                return;
            
            bool confirm = EditorUtility.DisplayDialog(
                "Delete Export Profile",
                $"Are you sure you want to delete the profile '{profile.name}'?\n\nThis cannot be undone.",
                "Delete",
                "Cancel"
            );
            
            if (!confirm)
                return;
            
            string assetPath = AssetDatabase.GetAssetPath(profile);
            
            if (selectedProfile == profile)
            {
                selectedProfile = null;
                selectedProfileIndex = -1;
            }
            
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.SaveAssets();
            
            Debug.Log($"[PackageExporter] Deleted profile: {assetPath}");
            
            LoadProfiles();
        }
        
        private void ExportProfile(ExportProfile profile)
        {
            if (profile == null)
                return;
            
            if (!profile.Validate(out string errorMessage))
            {
                EditorUtility.DisplayDialog("Validation Error", errorMessage, "OK");
                return;
            }
            
            // Show detailed export confirmation
            string foldersList = profile.foldersToExport.Count > 0 
                ? string.Join("\n", profile.foldersToExport.Take(5)) + (profile.foldersToExport.Count > 5 ? $"\n... and {profile.foldersToExport.Count - 5} more" : "")
                : "None configured";
            
            int bundledDeps = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Bundle);
            int refDeps = profile.dependencies.Count(d => d.enabled && d.exportMode == DependencyExportMode.Dependency);
            
            bool confirm = EditorUtility.DisplayDialog(
                "Export Package",
                $"Export package: {profile.packageName} v{profile.version}\n\n" +
                $"Export Folders ({profile.foldersToExport.Count}):\n{foldersList}\n\n" +
                $"Dependencies:\n" +
                $"  Bundled: {bundledDeps}\n" +
                $"  Referenced: {refDeps}\n\n" +
                $"Obfuscation: {(profile.enableObfuscation ? $"Enabled ({profile.obfuscationPreset}, {profile.assembliesToObfuscate.Count(a => a.enabled)} assemblies)" : "Disabled")}\n\n" +
                $"Output: {profile.GetOutputFilePath()}",
                "Export",
                "Cancel"
            );
            
            if (!confirm)
                return;
            
            isExporting = true;
            currentProgress = 0f;
            currentStatus = "Starting export...";
            
            try
            {
                var result = PackageBuilder.ExportPackage(profile, (progress, status) =>
                {
                    currentProgress = progress;
                    currentStatus = status;
                    Repaint();
                });
                
                isExporting = false;
                Repaint();
                
                if (result.success)
                {
                    bool openFolder = EditorUtility.DisplayDialog(
                        "Export Successful",
                        $"Package exported successfully!\n\n" +
                        $"Package: {profile.packageName} v{profile.version}\n" +
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
                    
                    // Reload profiles to update statistics
                    LoadProfiles();
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
            catch (Exception ex)
            {
                isExporting = false;
                Repaint();
                
                Debug.LogError($"[PackageExporter] Export failed: {ex.Message}");
                EditorUtility.DisplayDialog("Export Failed", $"An error occurred: {ex.Message}", "OK");
            }
        }
        
        private void ExportAllProfiles(List<ExportProfile> profilesToExport = null)
        {
            var profiles = profilesToExport ?? allProfiles;
            if (profiles.Count == 0)
                return;
            
            // Validate all profiles first
            var invalidProfiles = new List<string>();
            foreach (var profile in profiles)
            {
                if (!profile.Validate(out string error))
                {
                    invalidProfiles.Add($"{profile.name}: {error}");
                }
            }
            
            if (invalidProfiles.Count > 0)
            {
                string message = "The following profiles have validation errors:\n\n" + string.Join("\n", invalidProfiles);
                EditorUtility.DisplayDialog("Validation Errors", message, "OK");
                return;
            }
            
            bool confirm = EditorUtility.DisplayDialog(
                "Export Profiles",
                $"This will export {profiles.Count} package(s):\n\n" +
                string.Join("\n", profiles.Select(p => $"• {p.packageName} v{p.version}")) +
                "\n\nThis may take several minutes.",
                "Export All",
                "Cancel"
            );
            
            if (!confirm)
                return;
            
            isExporting = true;
            
            try
            {
                var results = PackageBuilder.ExportMultiple(profiles, (index, total, progress, status) =>
                {
                    float overallProgress = (index + progress) / total;
                    currentProgress = overallProgress;
                    currentStatus = $"[{index + 1}/{total}] {status}";
                    Repaint();
                });
                
                isExporting = false;
                Repaint();
                
                // Show summary
                int successCount = results.Count(r => r.success);
                int failCount = results.Count - successCount;
                
                string summaryMessage = $"Batch export complete!\n\n" +
                                      $"Successful: {successCount}\n" +
                                      $"Failed: {failCount}\n\n";
                
                if (failCount > 0)
                {
                    var failures = results.Where(r => !r.success).ToList();
                    summaryMessage += "Failed profiles:\n" + string.Join("\n", failures.Select(r => $"• {r.errorMessage}"));
                }
                
                EditorUtility.DisplayDialog("Batch Export Complete", summaryMessage, "OK");
                
                // Reload profiles to update statistics
                LoadProfiles();
            }
            catch (Exception ex)
            {
                isExporting = false;
                Repaint();
                
                Debug.LogError($"[PackageExporter] Batch export failed: {ex.Message}");
                EditorUtility.DisplayDialog("Batch Export Failed", $"An error occurred: {ex.Message}", "OK");
            }
        }
        
        private void ScanProfileDependencies(ExportProfile profile)
        {
            EditorUtility.DisplayProgressBar("Scanning Dependencies", "Finding installed packages...", 0.3f);
            
            try
            {
                var foundPackages = DependencyScanner.ScanInstalledPackages();
                
                if (foundPackages.Count == 0)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("No Packages Found", 
                        "No installed packages were found in the project.", 
                        "OK");
                    return;
                }
                
                EditorUtility.DisplayProgressBar("Scanning Dependencies", "Processing packages...", 0.6f);
                
                profile.dependencies.Clear();
                
                var dependencies = DependencyScanner.ConvertToPackageDependencies(foundPackages);
                foreach (var dep in dependencies)
                {
                    profile.dependencies.Add(dep);
                }
                
                EditorUtility.DisplayProgressBar("Scanning Dependencies", "Auto-detecting usage...", 0.8f);
                
                // Auto-detect which dependencies are actually used
                if (profile.foldersToExport.Count > 0)
                {
                    DependencyScanner.AutoDetectUsedDependencies(profile);
                }
                
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                EditorUtility.ClearProgressBar();
                
                int vpmCount = dependencies.Count(d => d.isVpmDependency);
                int autoEnabled = dependencies.Count(d => d.enabled);
                
                string message = $"Found {dependencies.Count} packages:\n\n" +
                               $"• {vpmCount} VRChat (VPM) packages\n" +
                               $"• {dependencies.Count - vpmCount} Unity packages\n" +
                               $"• {autoEnabled} auto-enabled (detected in use)\n\n" +
                               "Dependencies detected in your export folders have been automatically enabled.";
                
                EditorUtility.DisplayDialog("Scan Complete", message, "OK");
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
                AssetDatabase.SaveAssets();
                
                EditorUtility.DisplayProgressBar("Scanning Assemblies", "Complete!", 1.0f);
                
                int existingCount = foundAssemblies.Count(a => a.exists);
                EditorUtility.DisplayDialog("Scan Complete", 
                    $"Found {foundAssemblies.Count} assemblies ({existingCount} compiled)\n\nFrom export folders: {folderAssemblies.Count}\nFrom bundled dependencies: {dependencyAssemblies.Count}", 
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        
        private void DrawDependencyCard(PackageDependency dep, int index, ExportProfile profile)
        {
            // Calculate card height based on whether it's expanded
            float cardHeight = dep.enabled ? 175f : 35f;
            
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(cardHeight));
            
            // Header row
            GUILayout.BeginHorizontal(GUILayout.Height(25f));
            
            // Toggle checkbox
            dep.enabled = EditorGUILayout.Toggle(new GUIContent("", "Enable or disable this dependency"), dep.enabled, GUILayout.Width(20f));
            
            // Package name label
            string label = dep.isVpmDependency ? "[VPM] " : "";
            label += string.IsNullOrEmpty(dep.displayName) ? dep.packageName : dep.displayName;
            
            GUILayout.Label(new GUIContent(label, "Package dependency configuration"), EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            
            // Remove button
            if (GUILayout.Button(new GUIContent("X", "Remove this dependency"), GUILayout.Width(25f), GUILayout.Height(20f)))
            {
                profile.dependencies.RemoveAt(index);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                GUIUtility.ExitGUI();
            }
            
            GUILayout.EndHorizontal();
            
            // Content area - only show if enabled
            if (dep.enabled)
            {
                GUILayout.Space(5f);
                
                // Package Name field
                GUILayout.BeginHorizontal(GUILayout.Height(20f));
                GUILayout.Label(new GUIContent("Package Name:", "Unique identifier for the package"), GUILayout.Width(120f));
                dep.packageName = EditorGUILayout.TextField(dep.packageName, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                
                GUILayout.Space(2f);
                
                // Version field
                GUILayout.BeginHorizontal(GUILayout.Height(20f));
                GUILayout.Label(new GUIContent("Version:", "Package version"), GUILayout.Width(120f));
                dep.packageVersion = EditorGUILayout.TextField(dep.packageVersion, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                
                GUILayout.Space(2f);
                
                // Display Name field
                GUILayout.BeginHorizontal(GUILayout.Height(20f));
                GUILayout.Label(new GUIContent("Display Name:", "Human-readable name"), GUILayout.Width(120f));
                dep.displayName = EditorGUILayout.TextField(dep.displayName, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                
                GUILayout.Space(2f);
                
                // Export Mode dropdown
                GUILayout.BeginHorizontal(GUILayout.Height(20f));
                GUILayout.Label(new GUIContent("Export Mode:", "How this dependency should be handled"), GUILayout.Width(120f));
                dep.exportMode = (DependencyExportMode)EditorGUILayout.EnumPopup(dep.exportMode, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                
                GUILayout.Space(2f);
                
                // VPM Package toggle
                GUILayout.BeginHorizontal(GUILayout.Height(20f));
                dep.isVpmDependency = EditorGUILayout.Toggle(new GUIContent("VPM Package", "Is this a VRChat Package Manager dependency?"), dep.isVpmDependency);
                GUILayout.EndHorizontal();
                
                GUILayout.Space(5f);
            }
            
            GUILayout.EndVertical();
            GUILayout.Space(5f);
        }
        
        private void HandleProfileSelection(int index, Event currentEvent)
        {
            if (currentEvent.control || currentEvent.command)
            {
                // Ctrl/Cmd+Click: Toggle individual selection
                if (selectedProfileIndices.Contains(index))
                {
                    selectedProfileIndices.Remove(index);
                }
                else
                {
                    selectedProfileIndices.Add(index);
                }
                lastClickedProfileIndex = index;
                
                // Update selectedProfile to the first selected item
                if (selectedProfileIndices.Count > 0)
                {
                    selectedProfileIndex = selectedProfileIndices.Min();
                    selectedProfile = allProfiles[selectedProfileIndex];
                }
                else
                {
                    selectedProfileIndex = -1;
                    selectedProfile = null;
                }
            }
            else if (currentEvent.shift && lastClickedProfileIndex >= 0)
            {
                // Shift+Click: Range selection
                int start = Mathf.Min(lastClickedProfileIndex, index);
                int end = Mathf.Max(lastClickedProfileIndex, index);
                
                for (int i = start; i <= end; i++)
                {
                    if (i < allProfiles.Count)
                    {
                        selectedProfileIndices.Add(i);
                    }
                }
                
                // Update selectedProfile to the first selected item
                selectedProfileIndex = selectedProfileIndices.Min();
                selectedProfile = allProfiles[selectedProfileIndex];
            }
            else
            {
                // Normal click: Single selection
                selectedProfileIndices.Clear();
                selectedProfileIndices.Add(index);
                lastClickedProfileIndex = index;
                selectedProfileIndex = index;
                selectedProfile = allProfiles[index];
            }
            
            Repaint();
        }
        
        private void ExportSelectedProfiles()
        {
            if (selectedProfileIndices.Count == 0)
            {
                EditorUtility.DisplayDialog("No Selection", "No profiles are selected.", "OK");
                return;
            }
            
            var selectedProfiles = selectedProfileIndices.OrderBy(i => i).Select(i => allProfiles[i]).ToList();
            
            ExportAllProfiles(selectedProfiles);
        }
        
        private void DrawExportButtons()
        {
            DrawHorizontalLine();
            GUILayout.Space(10);
            
            GUILayout.BeginHorizontal();
            
            // Dynamic export button that changes based on selection
            GUI.enabled = selectedProfileIndices.Count > 0 && !isExporting;
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.73f); // Teal
            
            string buttonText;
            if (selectedProfileIndices.Count == 1)
            {
                buttonText = "Export Selected Profile";
            }
            else
            {
                buttonText = $"Export Selected Profiles ({selectedProfileIndices.Count})";
            }
            
            if (GUILayout.Button(buttonText, GUILayout.Height(50)))
            {
                if (selectedProfileIndices.Count == 1)
                {
                    ExportProfile(selectedProfile);
                }
                else
                {
                    ExportSelectedProfiles();
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            
            GUILayout.BeginHorizontal();
            
            GUI.enabled = allProfiles.Count > 0 && !isExporting;
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.9f);
            if (GUILayout.Button("Export All Profiles", GUILayout.Height(50)))
            {
                ExportAllProfiles();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
        }
        
        /// <summary>
        /// Draw the Export Inspector UI
        /// </summary>
        private void DrawExportInspector(ExportProfile profile, GUIStyle headerStyle)
        {
            EditorGUILayout.HelpBox(
                "The Export Inspector shows all assets discovered from your export folders. " +
                "Scan to discover assets, then deselect unwanted items or add folders to the permanent ignore list.",
                MessageType.Info);
            
            // Action buttons
            GUILayout.BeginHorizontal();
            
            GUI.enabled = profile.foldersToExport.Count > 0;
            if (GUILayout.Button("Scan Assets", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
            {
                ScanAssetsForInspector(profile);
            }
            GUI.enabled = true;
            
            GUI.enabled = profile.discoveredAssets.Count > 0;
            if (GUILayout.Button("Clear Scan", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
            {
                if (EditorUtility.DisplayDialog("Clear Scan", 
                    "Clear all discovered assets and rescan later?", "Clear", "Cancel"))
                {
                    profile.ClearScan();
                    EditorUtility.SetDirty(profile);
                }
            }
            GUI.enabled = true;
            
            GUILayout.EndHorizontal();
            
            // Show scan required message
            if (!profile.HasScannedAssets)
            {
                EditorGUILayout.HelpBox(
                    "Click 'Scan Assets' to discover all assets from your export folders.",
                    MessageType.Warning);
                return;
            }
            
            // Statistics
            GUILayout.Space(5);
            GUILayout.Label("Asset Statistics", EditorStyles.boldLabel);
            string summary = AssetCollector.GetAssetSummary(profile.discoveredAssets);
            EditorGUILayout.HelpBox(summary, MessageType.None);
            
            // Filter controls
            GUILayout.Space(5);
            GUILayout.Label("Filters", EditorStyles.boldLabel);
            
            GUILayout.BeginHorizontal();
            inspectorSearchFilter = EditorGUILayout.TextField("Search:", inspectorSearchFilter);
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                inspectorSearchFilter = "";
                GUI.FocusControl(null);
            }
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            showOnlyIncluded = GUILayout.Toggle(showOnlyIncluded, "Show Only Included", GUILayout.Width(150));
            showOnlyExcluded = GUILayout.Toggle(showOnlyExcluded, "Show Only Excluded", GUILayout.Width(150));
            GUILayout.EndHorizontal();
            
            // Asset list
            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Discovered Assets", EditorStyles.boldLabel);
            
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
            GUILayout.EndHorizontal();
            
            // Filter assets
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
            exportInspectorScrollPos = GUILayout.BeginScrollView(exportInspectorScrollPos, GUILayout.MaxHeight(400));
            
            if (filteredList.Count == 0)
            {
                EditorGUILayout.HelpBox("No assets match the current filters.", MessageType.Info);
            }
            else
            {
                // Group by folder
                var groupedByFolder = filteredList
                    .Where(a => !a.isFolder)
                    .GroupBy(a => a.GetFolderPath())
                    .OrderBy(g => g.Key);
                
                foreach (var group in groupedByFolder)
                {
                    // Folder header
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                    
                    GUILayout.Label(group.Key, EditorStyles.boldLabel);
                    
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
                    
                    GUILayout.EndHorizontal();
                    
                    // Files in this folder
                    foreach (var asset in group)
                    {
                        GUILayout.BeginHorizontal();
                        
                        // Include checkbox
                        bool wasIncluded = asset.included;
                        asset.included = EditorGUILayout.Toggle(asset.included, GUILayout.Width(20));
                        if (asset.included != wasIncluded)
                            EditorUtility.SetDirty(profile);
                        
                        // Asset icon
                        Texture2D icon = AssetDatabase.GetCachedIcon(asset.assetPath) as Texture2D;
                        if (icon != null)
                            GUILayout.Label(icon, GUILayout.Width(16), GUILayout.Height(16));
                        
                        // Asset name
                        string displayName = asset.GetDisplayName();
                        string label = $"{displayName}";
                        if (asset.isDependency)
                            label += " [Dep]";
                        
                        GUILayout.Label(label, GUILayout.MinWidth(150));
                        
                        // Asset type badge
                        GUI.color = GetAssetTypeColor(asset.assetType);
                        GUILayout.Label(asset.assetType, EditorStyles.miniLabel, GUILayout.Width(60));
                        GUI.color = Color.white;
                        
                        // File size
                        if (!asset.isFolder && asset.fileSize > 0)
                        {
                            GUILayout.Label(FormatBytes(asset.fileSize), EditorStyles.miniLabel, GUILayout.Width(60));
                        }
                        
                        GUILayout.EndHorizontal();
                    }
                    
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
            }
            
            GUILayout.EndScrollView();
            
            // Permanent ignore list
            GUILayout.Space(10);
            GUILayout.Label("Permanent Ignore List", EditorStyles.boldLabel);
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
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(profile.permanentIgnoreFolders[i]);
                    
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        profile.permanentIgnoreFolders.RemoveAt(i);
                        EditorUtility.SetDirty(profile);
                    }
                    
                    GUILayout.EndHorizontal();
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
                Debug.LogError($"[YUCPPackageExporterWindow] Asset scan failed: {ex.Message}");
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
