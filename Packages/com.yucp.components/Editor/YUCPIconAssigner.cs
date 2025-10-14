using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Automatically assigns the YUCP icon to all component scripts in the package.
    /// Runs once on editor startup to ensure all components have the custom icon.
    /// </summary>
    [InitializeOnLoad]
    public static class YUCPIconAssigner
    {
        private const string ICON_PATH = "Packages/com.yucp.components/Resources/Icons/Mini_Icon.png";
        private const string SESSION_KEY = "YUCPIconAssigned";
        
        static YUCPIconAssigner()
        {
            EditorApplication.delayCall += AssignIcons;
        }
        
        private static void AssignIcons()
        {
            // Only run once per session
            if (SessionState.GetBool(SESSION_KEY, false))
                return;
                
            try
            {
                // Load the icon texture
                Texture2D iconTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ICON_PATH);
                if (iconTexture == null)
                {
                    Debug.LogWarning($"[YUCP] Icon not found at {ICON_PATH}. Skipping icon assignment.");
                    return;
                }
                
                // Get the icon's GUID
                string iconGuid = AssetDatabase.AssetPathToGUID(ICON_PATH);
                if (string.IsNullOrEmpty(iconGuid))
                {
                    Debug.LogWarning($"[YUCP] Could not get GUID for icon at {ICON_PATH}");
                    return;
                }
                
                Debug.Log($"[YUCP] Found Mini_Icon.png with GUID: {iconGuid}");
                
                // Find all YUCP component scripts
                string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Packages/com.yucp.components/Runtime/Components/Data" });
                int assignedCount = 0;
                
                foreach (string scriptGuid in scriptGuids)
                {
                    string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);
                    
                    // Skip if not a .cs file
                    if (!scriptPath.EndsWith(".cs"))
                        continue;
                    
                    // Skip if it's not a component (doesn't inherit from MonoBehaviour)
                    if (!IsComponentScript(scriptPath))
                        continue;
                    
                    // Assign the icon
                    if (AssignIconToScript(scriptPath, iconGuid))
                    {
                        assignedCount++;
                    }
                }
                
                // Mark as completed
                SessionState.SetBool(SESSION_KEY, true);
                
                Debug.Log($"[YUCP] Successfully assigned icon to {assignedCount} component scripts");
                
                // Refresh the asset database to show changes
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP] Error assigning icons: {ex.Message}");
                Debug.LogException(ex);
            }
        }
        
        private static bool IsComponentScript(string scriptPath)
        {
            try
            {
                // Read the script content
                string content = File.ReadAllText(scriptPath);
                
                // Check if it inherits from MonoBehaviour and has AddComponentMenu
                bool inheritsMonoBehaviour = content.Contains(": MonoBehaviour") || content.Contains(":MonoBehaviour");
                bool hasAddComponentMenu = content.Contains("[AddComponentMenu");
                
                return inheritsMonoBehaviour && hasAddComponentMenu;
            }
            catch
            {
                return false;
            }
        }
        
        private static bool AssignIconToScript(string scriptPath, string iconGuid)
        {
            try
            {
                string metaPath = scriptPath + ".meta";
                
                if (!File.Exists(metaPath))
                {
                    Debug.LogWarning($"[YUCP] No .meta file found for {scriptPath}");
                    return false;
                }
                
                // Read the meta file
                string[] metaLines = File.ReadAllLines(metaPath);
                bool iconAssigned = false;
                
                // Check if icon is already assigned
                foreach (string line in metaLines)
                {
                    if (line.Trim().StartsWith("icon:"))
                    {
                        // Check if it's already our icon
                        if (line.Contains(iconGuid))
                        {
                            return true; // Already has our icon
                        }
                        iconAssigned = true;
                        break;
                    }
                }
                
                // Find the MonoImporter section and add/update the icon
                bool inMonoImporter = false;
                bool iconLineFound = false;
                
                for (int i = 0; i < metaLines.Length; i++)
                {
                    string line = metaLines[i];
                    
                    if (line.Trim() == "MonoImporter:")
                    {
                        inMonoImporter = true;
                        continue;
                    }
                    
                    if (inMonoImporter)
                    {
                        // If we hit another section, stop
                        if (!line.StartsWith(" ") && !string.IsNullOrWhiteSpace(line))
                        {
                            // If we haven't found an icon line yet, add it
                            if (!iconLineFound)
                            {
                                // Insert icon line before the closing of MonoImporter
                                string[] newLines = new string[metaLines.Length + 1];
                                Array.Copy(metaLines, 0, newLines, 0, i);
                                newLines[i] = "  icon: {fileID: 2800000, guid: " + iconGuid + ", type: 3}";
                                Array.Copy(metaLines, i, newLines, i + 1, metaLines.Length - i);
                                metaLines = newLines;
                            }
                            break;
                        }
                        
                        // Update existing icon line
                        if (line.Trim().StartsWith("icon:"))
                        {
                            metaLines[i] = "  icon: {fileID: 2800000, guid: " + iconGuid + ", type: 3}";
                            iconLineFound = true;
                            break;
                        }
                    }
                }
                
                // If we're still in MonoImporter and didn't find an icon line, add it at the end
                if (inMonoImporter && !iconLineFound)
                {
                    // Find the last line of MonoImporter section
                    for (int i = 0; i < metaLines.Length; i++)
                    {
                        if (metaLines[i].Trim() == "MonoImporter:")
                        {
                            // Find where to insert (after externalObjects, before userData)
                            int insertIndex = i + 1;
                            while (insertIndex < metaLines.Length && 
                                   (metaLines[insertIndex].Trim().StartsWith("externalObjects") ||
                                    metaLines[insertIndex].Trim().StartsWith("serializedVersion") ||
                                    metaLines[insertIndex].Trim().StartsWith("defaultReferences")))
                            {
                                insertIndex++;
                            }
                            
                            // Insert the icon line
                            string[] newLines = new string[metaLines.Length + 1];
                            Array.Copy(metaLines, 0, newLines, 0, insertIndex);
                            newLines[insertIndex] = "  icon: {fileID: 2800000, guid: " + iconGuid + ", type: 3}";
                            Array.Copy(metaLines, insertIndex, newLines, insertIndex + 1, metaLines.Length - insertIndex);
                            metaLines = newLines;
                            break;
                        }
                    }
                }
                
                // Write the updated meta file
                File.WriteAllLines(metaPath, metaLines);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP] Error assigning icon to {scriptPath}: {ex.Message}");
                return false;
            }
        }
        
        [MenuItem("Tools/YUCP/Reassign Icons")]
        private static void ReassignIcons()
        {
            SessionState.SetBool(SESSION_KEY, false);
            AssignIcons();
            Debug.Log("[YUCP] Icon reassignment completed!");
        }
        
        [MenuItem("Tools/YUCP/Clear Icon Cache")]
        private static void ClearIconCache()
        {
            SessionState.SetBool(SESSION_KEY, false);
            Debug.Log("[YUCP] Icon cache cleared. Icons will be reassigned on next editor startup.");
        }
    }
}
