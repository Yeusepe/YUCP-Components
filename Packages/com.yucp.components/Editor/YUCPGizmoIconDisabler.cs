using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Disables 3D gizmo icons in Scene view for YUCP components while keeping Add Component menu icons.
    /// Based on Modular Avatar's approach using Unity's internal AnnotationUtility.
    /// </summary>
    [InitializeOnLoad]
    public static class YUCPGizmoIconDisabler
    {
        private const int MONO_BEHAVIOR_CLASS_ID = 114;
        private static bool _hasRunOnce = false;

        static YUCPGizmoIconDisabler()
        {
            // Run after a delay to ensure types are registered
            EditorApplication.delayCall += () =>
            {
                EditorApplication.update += DisableYUCPSceneGizmos;
            };
        }

        private static MethodInfo _setIconEnabled;
        private static MethodInfo SetIconEnabled
        {
            get
            {
                if (_setIconEnabled == null)
                {
                    var asm = Assembly.GetAssembly(typeof(UnityEditor.Editor));
                    var annotationUtility = asm?.GetType("UnityEditor.AnnotationUtility");
                    _setIconEnabled = annotationUtility?.GetMethod("SetIconEnabled", 
                        BindingFlags.Static | BindingFlags.NonPublic);
                }
                return _setIconEnabled;
            }
        }

        private static MethodInfo _getAnnotations;
        private static MethodInfo GetAnnotations
        {
            get
            {
                if (_getAnnotations == null)
                {
                    var asm = Assembly.GetAssembly(typeof(UnityEditor.Editor));
                    var annotationUtility = asm?.GetType("UnityEditor.AnnotationUtility");
                    _getAnnotations = annotationUtility?.GetMethod("GetAnnotations",
                        BindingFlags.Static | BindingFlags.NonPublic);
                }
                return _getAnnotations;
            }
        }

        static void SetGizmoIconEnabled(Type type, bool enabled)
        {
            if (SetIconEnabled == null)
            {
                Debug.LogWarning("[YUCP] Could not access SetIconEnabled - scene icons may be visible");
                return;
            }
            
            try
            {
                SetIconEnabled.Invoke(null, new object[] { MONO_BEHAVIOR_CLASS_ID, type.Name, enabled ? 1 : 0 });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[YUCP] Failed to disable icon for {type.Name}: {e.Message}");
            }
        }

        static void DisableYUCPSceneGizmos()
        {
            // Check if reflection APIs are available
            if (SetIconEnabled == null || GetAnnotations == null)
            {
                EditorApplication.update -= DisableYUCPSceneGizmos;
                Debug.LogWarning("[YUCP] Could not access Unity's internal AnnotationUtility - scene icons may be visible");
                return;
            }

            try
            {
                // Get all current annotations
                var annotations = (Array)GetAnnotations.Invoke(null, null);
                if (annotations == null || annotations.Length == 0)
                {
                    // Annotations not ready yet, try again next frame
                    return;
                }

                // Find all YUCP component types that need disabling
                var yucpTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(asm => {
                        try { return asm.GetTypes(); }
                        catch { return Type.EmptyTypes; }
                    })
                    .Where(t => 
                        t != null &&
                        !t.IsAbstract &&
                        typeof(MonoBehaviour).IsAssignableFrom(t) &&
                        t.Namespace != null &&
                        t.Namespace.StartsWith("YUCP.Components"))
                    .ToList();

                if (yucpTypes.Count == 0)
                {
                    // No types found yet, try again
                    if (!_hasRunOnce)
                    {
                        return;
                    }
                }

                // Disable icons for all YUCP component types
                int disabledCount = 0;
                foreach (var type in yucpTypes)
                {
                    SetGizmoIconEnabled(type, false);
                    disabledCount++;
                }

                if (disabledCount > 0 && !_hasRunOnce)
                {
                    Debug.Log($"[YUCP] Disabled scene gizmo icons for {disabledCount} component types. Icons will still appear in Add Component menu and Inspector.");
                }

                _hasRunOnce = true;
                
                // Remove from update loop
                EditorApplication.update -= DisableYUCPSceneGizmos;
            }
            catch (Exception e)
            {
                Debug.LogError($"[YUCP] Error disabling scene icons: {e.Message}");
                EditorApplication.update -= DisableYUCPSceneGizmos;
            }
        }
    }
}
