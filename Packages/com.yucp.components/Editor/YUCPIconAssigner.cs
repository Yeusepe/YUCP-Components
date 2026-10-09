using System;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor
{
    [InitializeOnLoad]
    public static class YUCPIconAssigner
    {
        private const string IconPath = "Packages/com.yucp.components/Resources/Icons/Mini_Icon.png";
        private const string SessionKey = "YUCPIconAssigned";

        static YUCPIconAssigner()
        {
            EditorApplication.delayCall += AssignIcons;
        }

        private static void AssignIcons()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning($"[YUCP] Icon not found at {IconPath}. Skipping icon assignment.");
                return;
            }

            var scriptGuids = AssetDatabase.FindAssets("t:MonoScript",
                new[] { "Packages/com.yucp.components/Runtime/Components/Data" });
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var scriptGuid in scriptGuids)
                {
                    string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);
                    try
                    {
                        var importer = AssetImporter.GetAtPath(scriptPath) as MonoImporter;
                        if (importer == null || importer.GetIcon() == icon)
                            continue;

                        var script = importer.GetScript();
                        var type = script == null ? null : script.GetClass();
                        if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type) ||
                            !Attribute.IsDefined(type, typeof(AddComponentMenu)))
                            continue;

                        importer.SetIcon(icon);
                        importer.SaveAndReimport();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[YUCP] Error assigning icon to {scriptPath}: {e.Message}");
                    }
                }

                SessionState.SetBool(SessionKey, true);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }
    }
}
