using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Tooltip system for providing contextual help on hover.
    /// Integrates with Unity's built-in tooltip system.
    /// </summary>
    public static class YUCPTooltip
    {
        public static void Draw(Rect rect, string tooltip)
        {
            if (!string.IsNullOrEmpty(tooltip))
            {
                GUI.Label(rect, new GUIContent("", tooltip));
            }
        }

        public static GUIContent WithTooltip(string text, string tooltip)
        {
            return new GUIContent(text, tooltip);
        }

        public static GUIContent WithTooltip(Texture2D icon, string tooltip)
        {
            return new GUIContent(icon, tooltip);
        }
    }
}



