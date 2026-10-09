using System;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor
{
    [InitializeOnLoad]
    public static class YUCPGizmoIconDisabler
    {
        static YUCPGizmoIconDisabler()
        {
            EditorApplication.delayCall += DisableYUCPSceneGizmos;
        }

        private static void DisableYUCPSceneGizmos()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (type.IsAbstract || type.Namespace == null ||
                    !type.Namespace.StartsWith("YUCP.Components", StringComparison.Ordinal))
                    continue;

                try
                {
                    GizmoUtility.SetIconEnabled(type, false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[YUCP] Failed to disable icon for {type.Name}: {e.Message}");
                }
            }
        }
    }
}
