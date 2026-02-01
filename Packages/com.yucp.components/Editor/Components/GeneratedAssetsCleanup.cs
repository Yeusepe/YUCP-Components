using UnityEditor;

namespace YUCP.Components.Editor
{
    [InitializeOnLoad]
    public static class GeneratedAssetsCleanup
    {
        private const string GeneratedRoot = "Assets/YUCP/GeneratedAssets";

        static GeneratedAssetsCleanup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            CleanupGeneratedAssets();
        }

        private static void CleanupGeneratedAssets()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedRoot))
            {
                return;
            }

            AssetDatabase.DeleteAsset(GeneratedRoot);
            AssetDatabase.Refresh();
        }
    }
}
