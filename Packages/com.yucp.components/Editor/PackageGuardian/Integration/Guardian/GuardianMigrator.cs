using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.PackageGuardian.Editor.Integration.Guardian
{
    /// <summary>
    /// Utility to detect and migrate from legacy Guardian.
    /// </summary>
    public static class GuardianMigrator
    {
        private const string LEGACY_GUARDIAN_PATH = "Assets/.guardian";
        
        [InitializeOnLoadMethod]
        private static void CheckForLegacyGuardian()
        {
            EditorApplication.delayCall += () =>
            {
                if (DetectLegacyGuardian())
                {
                    OfferMigration();
                }
            };
        }
        
        /// <summary>
        /// Check if legacy Guardian data exists.
        /// </summary>
        public static bool DetectLegacyGuardian()
        {
            return Directory.Exists(LEGACY_GUARDIAN_PATH);
        }
        
        /// <summary>
        /// Offer to migrate legacy Guardian data.
        /// </summary>
        private static void OfferMigration()
        {
            if (EditorUtility.DisplayDialog(
                "Legacy Guardian Detected",
                "Legacy Guardian data has been detected in this project.\n\n" +
                "Package Guardian provides an improved version control system.\n\n" +
                "Would you like to:\n" +
                "1. Archive the old Guardian data (recommended)\n" +
                "2. Keep both systems",
                "Archive Legacy Data", "Keep Both"))
            {
                ArchiveLegacyGuardian();
            }
        }
        
        /// <summary>
        /// Archive legacy Guardian data.
        /// </summary>
        public static void ArchiveLegacyGuardian()
        {
            try
            {
                string archivePath = LEGACY_GUARDIAN_PATH + "_archived_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                Directory.Move(LEGACY_GUARDIAN_PATH, archivePath);
                
                Debug.Log($"[Package Guardian] Archived legacy Guardian data to: {archivePath}");
                AssetDatabase.Refresh();
                
                EditorUtility.DisplayDialog("Migration Complete",
                    $"Legacy Guardian data has been archived to:\n{archivePath}\n\n" +
                    "You can now use Package Guardian.\n\n" +
                    "Open it via: Tools > YUCP Components > Package Guardian",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Failed to archive legacy Guardian: {ex.Message}");
                EditorUtility.DisplayDialog("Migration Failed",
                    $"Failed to archive legacy Guardian data:\n{ex.Message}",
                    "OK");
            }
        }
    }
}

