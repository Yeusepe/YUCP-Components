using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Color picker with consistent styling.
    /// Version of Unity's ColorField with custom styling.
    /// </summary>
    public static class YUCPColorField
    {
        public static Color Draw(string label, Color value, bool showAlpha = true)
        {
            return Draw(new GUIContent(label), value, showAlpha);
        }

        public static Color Draw(GUIContent label, Color value, bool showAlpha = true)
        {
            return EditorGUILayout.ColorField(label, value, showAlpha, false, true);
        }

        public static Color Draw(Rect rect, string label, Color value, bool showAlpha = true)
        {
            return EditorGUI.ColorField(rect, new GUIContent(label), value, showAlpha, false, true);
        }
    }
}

