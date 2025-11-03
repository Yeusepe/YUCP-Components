using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.PackageGuardian.Editor.Settings
{
    /// <summary>
    /// Settings for Package Guardian VCS.
    /// Stored as ScriptableObject in ProjectSettings.
    /// </summary>
    [System.Serializable]
    public class PackageGuardianSettings : ScriptableObject
    {
        private static PackageGuardianSettings _instance;
        
        [Header("Automatic Snapshots")]
        [Tooltip("Automatically create snapshots when files are saved")]
        public bool autoSnapshotOnSave = false;
        
        [Tooltip("Automatically create stashes when Unity Package Manager events occur")]
        public bool autoStashOnUPM = true;
        
        [Tooltip("Automatically create stashes after .unitypackage or asset imports")]
        public bool autoStashOnAssetImport = true;
        
        [Tooltip("Automatically create a stash when a scene is saved")]
        public bool autoStashOnSceneSave = true;
        
        [Header("Author Information")]
        [Tooltip("Author name for commits")]
        public string authorName = "Unity User";
        
        [Tooltip("Author email for commits")]
        public string authorEmail = "user@unity.com";
        
        [Header("Tracked Directories")]
        [Tooltip("Root directories to include in snapshots")]
        public List<string> trackedRoots = new List<string> { "Assets", "Packages" };
        
        [Header("Large Files")]
        [Tooltip("Enable chunked storage for large files")]
        public bool enableLargeFileSupport = false;
        
        [Tooltip("File size threshold in bytes for large file handling (default: 50MB)")]
        public long largeFileThreshold = 52428800; // 50MB
        
        [Header("Performance")]
        [Tooltip("Enable fsync for critical writes (slower but safer)")]
        public bool enableFsync = false;
        
        [Header("UI")]
        [Tooltip("Theme for Package Guardian windows")]
        public Theme themeOverride = Theme.FollowUnity;
        
        [Tooltip("Language for UI")]
        public Language language = Language.English;
        
        public enum Theme
        {
            FollowUnity,
            Dark,
            Light
        }
        
        public enum Language
        {
            English,
            Spanish
        }
        
        /// <summary>
        /// Get singleton instance of settings.
        /// </summary>
        public static PackageGuardianSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = LoadOrCreate();
                }
                return _instance;
            }
        }
        
        private static PackageGuardianSettings LoadOrCreate()
        {
            // Try to load JSON settings from ProjectSettings
            string settingsPath = "ProjectSettings/PackageGuardianSettings.json";
            
            if (System.IO.File.Exists(settingsPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(settingsPath);
                    var settings = CreateInstance<PackageGuardianSettings>();
                    UnityEditor.EditorJsonUtility.FromJsonOverwrite(json, settings);
                    return settings;
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to load settings: {ex.Message}. Using defaults.");
                }
            }
            
            // Create new settings with safe defaults
            var newSettings = CreateInstance<PackageGuardianSettings>();
            newSettings.autoSnapshotOnSave = false; // Disabled by default to avoid import loops
            newSettings.autoStashOnUPM = true; // Enabled by default to track package changes
            newSettings.autoStashOnAssetImport = true; // Enabled to stash after imports
            newSettings.autoStashOnSceneSave = true; // Enabled to stash on scene save
            newSettings.authorName = "Unity User";
            newSettings.authorEmail = "user@unity.com";
            
            // Try to save defaults
            try
            {
                System.IO.Directory.CreateDirectory("ProjectSettings");
                var json = UnityEditor.EditorJsonUtility.ToJson(newSettings, true);
                System.IO.File.WriteAllText(settingsPath, json);
            }
            catch
            {
                // Ignore save errors during initialization
            }
            
            return newSettings;
        }
        
        /// <summary>
        /// Save settings to disk.
        /// </summary>
        public void Save()
        {
            try
            {
                string settingsPath = "ProjectSettings/PackageGuardianSettings.json";
                var json = UnityEditor.EditorJsonUtility.ToJson(this, true);
                System.IO.File.WriteAllText(settingsPath, json);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[Package Guardian] Failed to save settings: {ex.Message}");
            }
        }
    }
}

