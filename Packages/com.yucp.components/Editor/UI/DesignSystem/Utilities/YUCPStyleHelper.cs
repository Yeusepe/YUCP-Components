using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// Helper utilities for creating and managing styles consistently.
    /// Provides common style patterns used across YUCP components.
    /// </summary>
    public static class YUCPStyleHelper
    {
        public static GUIStyle CreateCardStyle()
        {
            return new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(16, 16, 16, 16),
                margin = new RectOffset(0, 0, 8, 8)
            };
        }

        public static GUIStyle CreateSectionTitleStyle()
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.212f, 0.749f, 0.694f, 1f) }
            };
        }

        public static GUIStyle CreateSubtitleStyle()
        {
            return new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) }
            };
        }

        public static Texture2D CreateColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        public static Color GetYUCPTeal()
        {
            return new Color(0.212f, 0.749f, 0.694f, 1f); // #36BFB1
        }

        public static Color GetYUCPTealHover()
        {
            return new Color(0.282f, 0.820f, 0.765f, 1f); // #48d1c3
        }

        public static Color GetYUCPBackgroundPrimary()
        {
            return new Color(0.035f, 0.035f, 0.035f, 1f); // #090909
        }

        public static Color GetYUCPBackgroundSecondary()
        {
            return new Color(0.102f, 0.102f, 0.102f, 1f); // #1a1a1a
        }

        public static Color GetYUCPBackgroundTertiary()
        {
            return new Color(0.165f, 0.165f, 0.165f, 1f); // #2a2a2a
        }
    }
}



