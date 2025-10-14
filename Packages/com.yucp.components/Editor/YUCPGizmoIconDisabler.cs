using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Disables 3D gizmo icons in Scene view for YUCP components while keeping Add Component menu icons.
    /// Based on Modular Avatar's approach using Unity's internal AnnotationUtility.
    /// </summary>
    [InitializeOnLoad]
    public static class YUCPGizmoIconDisabler
    {
        static YUCPGizmoIconDisabler()
        {
            EditorApplication.update += DisableYUCPSceneGizmos;
        }

        // From Acegikmo http://answers.unity.com/answers/1722605/view.html
        // In Unity 2022.1+, this can be replaced with GizmoUtility.SetIconEnabled(type, enabled);
        static MethodInfo setIconEnabled;

        static MethodInfo SetIconEnabled => setIconEnabled = setIconEnabled ?? Assembly.GetAssembly(typeof(UnityEditor.Editor))
            ?.GetType("UnityEditor.AnnotationUtility")
            ?.GetMethod("SetIconEnabled", BindingFlags.Static | BindingFlags.NonPublic);

        private static MethodInfo getAnnotations;

        private static MethodInfo GetAnnotations =>
            getAnnotations = getAnnotations ??
            Assembly.GetAssembly(typeof(UnityEditor.Editor))
                ?.GetType("UnityEditor.AnnotationUtility")
                ?.GetMethod("GetAnnotations", BindingFlags.Static | BindingFlags.NonPublic);

        private static Type t_Annotation = Assembly.GetAssembly(typeof(UnityEditor.Editor))
            ?.GetType("UnityEditor.AnnotationUtility+Annotation");

        private static FieldInfo f_classID =
            t_Annotation?.GetField("classID", BindingFlags.Instance | BindingFlags.Public);

        private static FieldInfo f_scriptClass =
            t_Annotation?.GetField("scriptClass", BindingFlags.Instance | BindingFlags.Public);

        static void SetGizmoIconEnabled(Type type, bool enabled)
        {
            if (SetIconEnabled == null) return;
            const int MONO_BEHAVIOR_CLASS_ID = 114; // https://docs.unity3d.com/Manual/ClassIDReference.html
            SetIconEnabled.Invoke(null, new object[] { MONO_BEHAVIOR_CLASS_ID, type.Name, enabled ? 1 : 0 });
        }

        static void DisableYUCPSceneGizmos()
        {
            if (SessionState.GetBool("YUCPSceneIconsDisabled", false) ||
                f_classID == null || f_scriptClass == null || GetAnnotations == null || SetIconEnabled == null)
            {
                EditorApplication.update -= DisableYUCPSceneGizmos;
                SessionState.SetBool("YUCPSceneIconsDisabled", true);
                return;
            }

            // Check if we have any YUCP components in the annotations yet
            var annotations = (Array)GetAnnotations.Invoke(null, new object[] { });
            bool hasYUCPComponent = false;
            
            for (int i = 0; i < annotations.Length; i++)
            {
                var annotation = annotations.GetValue(i);
                var classID = (int)f_classID.GetValue(annotation);
                var scriptClass = (string)f_scriptClass.GetValue(annotation);

                // Check for any YUCP component
                if (classID == 114 && scriptClass != null && scriptClass.Contains("YUCP"))
                {
                    hasYUCPComponent = true;
                    break;
                }
            }

            if (!hasYUCPComponent)
            {
                // Annotations aren't created yet for YUCP types, check back later.
                return;
            }

            // Disable scene gizmos for all YUCP components
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var ty in assembly.GetTypes())
                {
                    // Check if it's a YUCP component (inherits from MonoBehaviour and is in our namespace)
                    if (typeof(MonoBehaviour).IsAssignableFrom(ty) && 
                        !ty.IsAbstract && 
                        ty.Namespace != null && 
                        ty.Namespace.Contains("YUCP.Components"))
                    {
                        SetGizmoIconEnabled(ty, false);
                        Debug.Log($"[YUCP] Disabled scene gizmo icon for: {ty.Name}");
                    }
                }
            }

            EditorApplication.update -= DisableYUCPSceneGizmos;
            SessionState.SetBool("YUCPSceneIconsDisabled", true);
            
            Debug.Log("[YUCP] Scene gizmo icons disabled for all YUCP components. Icons will still appear in Add Component menu and Inspector.");
        }

        [MenuItem("Tools/YUCP/Re-enable Scene Gizmo Icons")]
        private static void ReEnableSceneGizmos()
        {
            SessionState.SetBool("YUCPSceneIconsDisabled", false);
            EditorApplication.update += DisableYUCPSceneGizmos;
            Debug.Log("[YUCP] Scene gizmo icons will be re-enabled on next editor update.");
        }

        [MenuItem("Tools/YUCP/Force Disable Scene Gizmo Icons")]
        private static void ForceDisableSceneGizmos()
        {
            // Force disable without waiting for annotations
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var ty in assembly.GetTypes())
                {
                    if (typeof(MonoBehaviour).IsAssignableFrom(ty) && 
                        !ty.IsAbstract && 
                        ty.Namespace != null && 
                        ty.Namespace.Contains("YUCP.Components"))
                    {
                        SetGizmoIconEnabled(ty, false);
                    }
                }
            }
            
            SessionState.SetBool("YUCPSceneIconsDisabled", true);
            Debug.Log("[YUCP] Scene gizmo icons force-disabled for all YUCP components.");
        }
    }
}
