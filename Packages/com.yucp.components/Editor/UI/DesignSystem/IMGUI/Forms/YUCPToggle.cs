using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Switch/toggle control with modern styling.
    /// Toggle with custom visual feedback.
    /// </summary>
    public static class YUCPToggle
    {
        public static bool Draw(string label, bool value)
        {
            return Draw(new GUIContent(label), value);
        }

        public static bool Draw(GUIContent label, bool value)
        {
            return EditorGUILayout.Toggle(label, value);
        }

        public static bool Draw(Rect rect, string label, bool value)
        {
            return EditorGUI.Toggle(rect, label, value);
        }
    }
}

