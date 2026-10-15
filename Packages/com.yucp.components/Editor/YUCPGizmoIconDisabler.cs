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

        // Recuerda: SetIconEnabled tira "Annotation not found!" si el tipo no tiene gizmo, so primero TryGetGizmoInfo https://docs.unity3d.com/ScriptReference/GizmoUtility.TryGetGizmoInfo.html
        private static void DisableYUCPSceneGizmos()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (type.Namespace != null && type.Namespace.StartsWith("YUCP.Components", StringComparison.Ordinal) &&
                    GizmoUtility.TryGetGizmoInfo(type, out var info) && info.iconEnabled)
                    GizmoUtility.SetIconEnabled(type, false);
            }
        }
    }
}
