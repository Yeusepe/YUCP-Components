using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Styled buttons with consistent YUCP design system styling.
    /// Supports primary, secondary, danger, and ghost variants.
    /// </summary>
    public static class YUCPButton
    {
        public enum ButtonVariant
        {
            Primary,
            Secondary,
            Danger,
            Ghost
        }

        public static bool Draw(string text, ButtonVariant variant = ButtonVariant.Primary, params GUILayoutOption[] options)
        {
            return Draw(new GUIContent(text), variant, options);
        }

        public static bool Draw(GUIContent content, ButtonVariant variant = ButtonVariant.Primary, params GUILayoutOption[] options)
        {
            var style = GetStyle(variant);
            return GUILayout.Button(content, style, options);
        }

        public static bool Draw(Rect rect, string text, ButtonVariant variant = ButtonVariant.Primary)
        {
            return Draw(rect, new GUIContent(text), variant);
        }

        public static bool Draw(Rect rect, GUIContent content, ButtonVariant variant = ButtonVariant.Primary)
        {
            var style = GetStyle(variant);
            return GUI.Button(rect, content, style);
        }

        private static GUIStyle GetStyle(ButtonVariant variant)
        {
            string styleKey = $"YUCPButton_{variant}";
            var style = GUI.skin.FindStyle(styleKey);
            
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.button);
                style.name = styleKey;
                
                switch (variant)
                {
                    case ButtonVariant.Primary:
                        var primaryColor = new Color(0.212f, 0.749f, 0.694f, 1f);
                        style.normal.background = CreateColorTexture(primaryColor);
                        style.normal.textColor = Color.white;
                        style.hover.background = CreateColorTexture(new Color(0.282f, 0.820f, 0.765f, 1f));
                        style.hover.textColor = Color.white;
                        style.active.background = CreateColorTexture(new Color(0.176f, 0.659f, 0.612f, 1f));
                        style.active.textColor = Color.white;
                        break;
                        
                    case ButtonVariant.Secondary:
                        style = new GUIStyle(GUI.skin.button);
                        break;
                        
                    case ButtonVariant.Danger:
                        style.normal.background = CreateColorTexture(new Color(0.886f, 0.290f, 0.290f, 1f));
                        style.normal.textColor = Color.white;
                        style.hover.background = CreateColorTexture(new Color(0.945f, 0.380f, 0.380f, 1f));
                        style.hover.textColor = Color.white;
                        style.active.background = CreateColorTexture(new Color(0.780f, 0.220f, 0.220f, 1f));
                        style.active.textColor = Color.white;
                        break;
                        
                    case ButtonVariant.Ghost:
                        style.normal.background = null;
                        style.hover.background = null;
                        style.active.background = null;
                        style.normal.textColor = EditorStyles.label.normal.textColor;
                        style.hover.textColor = EditorStyles.label.normal.textColor;
                        style.active.textColor = EditorStyles.label.normal.textColor;
                        break;
                }
            }
            
            return style;
        }

        private static Texture2D CreateColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
        
    }
}

