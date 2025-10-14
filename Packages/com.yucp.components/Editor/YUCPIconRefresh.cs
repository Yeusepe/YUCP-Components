using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Forces Unity to refresh all icons and clear any cached icon data.
    /// Use this when icons don't update properly in the UI.
    /// </summary>
    public static class YUCPIconRefresh
    {
        [MenuItem("Tools/YUCP/Force Refresh Icons")]
        private static void ForceRefreshIcons()
        {
            Debug.Log("[YUCP] Forcing icon refresh...");
            
            // Force Unity to refresh the asset database
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            
            // Clear any cached icon data
            EditorUtility.RequestScriptReload();
            
            // Force a repaint of all windows
            EditorApplication.QueuePlayerLoopUpdate();
            
            Debug.Log("[YUCP] Icon refresh completed! Check the Add Component menu now.");
        }
        
        [MenuItem("Tools/YUCP/Verify Icon Assignment")]
        private static void VerifyIconAssignment()
        {
            Debug.Log("[YUCP] Verifying icon assignment...");
            
            // Check Mini_Icon.png GUID
            string miniIconPath = "Packages/com.yucp.components/Resources/Icons/Mini_Icon.png";
            string miniIconGuid = AssetDatabase.AssetPathToGUID(miniIconPath);
            Debug.Log($"[YUCP] Mini_Icon.png GUID: {miniIconGuid}");
            
            // Check a few component meta files
            string[] components = {
                "Packages/com.yucp.components/Runtime/Components/Data/AutoGripData.cs.meta",
                "Packages/com.yucp.components/Runtime/Components/Data/AutoUDIMDiscardData.cs.meta",
                "Packages/com.yucp.components/Runtime/Components/Data/AutoBodyHiderData.cs.meta"
            };
            
            foreach (string metaPath in components)
            {
                if (System.IO.File.Exists(metaPath))
                {
                    string content = System.IO.File.ReadAllText(metaPath);
                    if (content.Contains("icon:"))
                    {
                        Debug.Log($"[YUCP] {System.IO.Path.GetFileNameWithoutExtension(metaPath)} has icon assigned");
                        if (content.Contains(miniIconGuid))
                        {
                            Debug.Log($"[YUCP] ✓ Using correct Mini_Icon.png GUID");
                        }
                        else
                        {
                            Debug.LogWarning($"[YUCP] ✗ Using wrong GUID - needs reassignment");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[YUCP] ✗ {System.IO.Path.GetFileNameWithoutExtension(metaPath)} has no icon assigned");
                    }
                }
            }
        }
    }
}
