using UnityEngine;
using UnityEditor;
using System;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Custom property drawer that shows a warning in the Inspector if VRCFury is not installed.
    /// Applied automatically to all YUCP components via Editor script.
    /// </summary>
    [InitializeOnLoad]
    public static class VRCFuryRequiredDrawer
    {
        private const string VRCFURY_TYPE_NAME = "com.vrcfury.api.FuryComponents";
        private static bool? vrcFuryInstalled = null;

        static VRCFuryRequiredDrawer()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnPostHeaderGUI;
        }

        private static bool IsVRCFuryInstalled()
        {
            if (vrcFuryInstalled == null)
            {
                Type vrcFuryType = Type.GetType(VRCFURY_TYPE_NAME + ", com.vrcfury.api");
                vrcFuryInstalled = vrcFuryType != null;
            }
            return vrcFuryInstalled.Value;
        }

        private static void OnPostHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.target == null) return;

            // Check if this is a YUCP component
            string typeName = editor.target.GetType().FullName;
            if (!typeName.StartsWith("YUCP.Components"))
                return;

            // Only show warning if VRCFury is not installed
            if (IsVRCFuryInstalled())
                return;

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "VRCFury is required for this component to work!\n\n" +
                "Install VRCFury from: https://vrcfury.com/download\n\n" +
                "Or go to Tools > YUCP > Check VRCFury Installation",
                MessageType.Error
            );
            
            if (GUILayout.Button("Open VRCFury Download Page", GUILayout.Height(30)))
            {
                Application.OpenURL("https://vrcfury.com/download");
            }
            
            EditorGUILayout.Space(5);
        }
    }
}

