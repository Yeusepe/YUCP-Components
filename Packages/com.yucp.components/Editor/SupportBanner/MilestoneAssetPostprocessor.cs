using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.SupportBanner
{
    /// <summary>
    /// Detects changes to ExportProfile assets and updates milestone tracking.
    /// </summary>
    public class MilestoneAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool profileChanged = false;
            
            // Check if any ExportProfile assets were imported, deleted, or moved
            foreach (string assetPath in importedAssets)
            {
                if (assetPath.EndsWith(".asset"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (asset != null && asset.GetType().Name == "ExportProfile")
                    {
                        profileChanged = true;
                        break;
                    }
                }
            }
            
            if (!profileChanged)
            {
                foreach (string assetPath in deletedAssets)
                {
                    if (assetPath.EndsWith(".asset") && assetPath.Contains("ExportProfile"))
                    {
                        profileChanged = true;
                        break;
                    }
                }
            }
            
            if (profileChanged)
            {
                // Update profile count from assets
                EditorApplication.delayCall += () => {
                    MilestoneTracker.UpdateProfileCountFromAssets();
                };
            }
        }
    }
}















