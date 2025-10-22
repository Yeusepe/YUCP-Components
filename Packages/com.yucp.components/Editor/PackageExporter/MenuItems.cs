using UnityEditor;
using UnityEngine;
using System.IO;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Menu items for quick access to Package Exporter features.
    /// </summary>
    public static class MenuItems
    {
        [MenuItem("Assets/Create/YUCP/Export Profile", priority = 100)]
        public static void CreateExportProfile()
        {
            CreateExportProfileInternal();
        }
        
        private static void CreateExportProfileInternal()
        {
            // Ensure directory exists
            string profilesDir = "Assets/YUCP/ExportProfiles";
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
                AssetDatabase.Refresh();
            }
            
            // Create profile
            var profile = ScriptableObject.CreateInstance<ExportProfile>();
            profile.packageName = "NewPackage";
            profile.version = "1.0.0";
            
            // Add some sensible defaults
            profile.foldersToExport.Add("Assets/");
            profile.includeDependencies = true;
            profile.recurseFolders = true;
            profile.generatePackageJson = true;
            
            // Generate unique path
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(profilesDir, "NewExportProfile.asset"));
            
            AssetDatabase.CreateAsset(profile, assetPath);
            AssetDatabase.SaveAssets();
            
            // Select and ping
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            
            Debug.Log($"[YUCP] Created export profile: {assetPath}");
        }
        
        [MenuItem("Tools/YUCP/Package Exporter/Open Exporter Window")]
        public static void OpenExporterWindow()
        {
            YUCPPackageExporterWindow.ShowWindow();
        }
        
        [MenuItem("Tools/YUCP/Package Exporter/Create Export Profile")]
        public static void CreateExportProfileFromMenu()
        {
            CreateExportProfileInternal();
        }
        
        [MenuItem("Tools/YUCP/Package Exporter/Open Export Profiles Folder")]
        public static void OpenExportProfilesFolder()
        {
            string profilesDir = "Assets/YUCP/ExportProfiles";
            
            if (!Directory.Exists(profilesDir))
            {
                Directory.CreateDirectory(profilesDir);
                AssetDatabase.Refresh();
            }
            
            // Select the folder in Unity
            var obj = AssetDatabase.LoadAssetAtPath<Object>(profilesDir);
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
        
        [MenuItem("Tools/YUCP/Package Exporter/Check ConfuserEx Installation")]
        public static void CheckConfuserExInstallation()
        {
            string status = ConfuserExManager.GetStatusInfo();
            bool isInstalled = ConfuserExManager.IsInstalled();
            
            if (isInstalled)
            {
                EditorUtility.DisplayDialog("ConfuserEx Status", status, "OK");
            }
            else
            {
                bool download = EditorUtility.DisplayDialog(
                    "ConfuserEx Not Installed",
                    status + "\n\nWould you like to download and install ConfuserEx now?",
                    "Download",
                    "Cancel"
                );
                
                if (download)
                {
                    EditorUtility.DisplayProgressBar("Installing ConfuserEx", "Downloading...", 0f);
                    
                    try
                    {
                        bool success = ConfuserExManager.EnsureInstalled((progress, statusText) =>
                        {
                            EditorUtility.DisplayProgressBar("Installing ConfuserEx", statusText, progress);
                        });
                        
                        EditorUtility.ClearProgressBar();
                        
                        if (success)
                        {
                            EditorUtility.DisplayDialog(
                                "Installation Complete",
                                "ConfuserEx has been successfully installed!\n\n" +
                                "You can now use obfuscation in your package exports.",
                                "OK"
                            );
                        }
                        else
                        {
                            EditorUtility.DisplayDialog(
                                "Installation Failed",
                                "Failed to install ConfuserEx. Check the console for details.",
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
    }
}
