using UnityEditor;

namespace YUCP.Importer.Editor.PackageManager
{
    public static class PackageManagerRuntimeSettings
    {
        private const string EnabledKey = "YUCP.Importer.PackageManager.Enabled";

        public static bool IsEnabled()
        {
            return EditorPrefs.GetBool(EnabledKey, true);
        }

        public static void SetEnabled(bool enabled)
        {
            EditorPrefs.SetBool(EnabledKey, enabled);
        }
    }
}
