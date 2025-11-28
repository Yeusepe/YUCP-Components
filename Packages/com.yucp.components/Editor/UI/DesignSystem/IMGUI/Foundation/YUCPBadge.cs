using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Status badges and labels for displaying counts, status, or tags.
    /// Provides consistent badge styling across YUCP editors.
    /// </summary>
    public static class YUCPBadge
    {
        public enum BadgeVariant
        {
            Default,
            Primary,
            Success,
            Warning,
            Danger,
            Info
        }

        public static void Draw(string text, BadgeVariant variant = BadgeVariant.Default, float minWidth = 0)
        {
            var style = GetStyle(variant);
            var content = new GUIContent(text);
            var size = style.CalcSize(content);
            
            if (minWidth > 0 && size.x < minWidth)
            {
                size.x = minWidth;
            }
            
            var rect = GUILayoutUtility.GetRect(size.x, size.y, GUILayout.Width(size.x));
            GUI.Label(rect, content, style);
        }

        public static void Draw(Rect rect, string text, BadgeVariant variant = BadgeVariant.Default)
        {
            var style = GetStyle(variant);
            GUI.Label(rect, text, style);
        }

        private static GUIStyle GetStyle(BadgeVariant variant)
        {
            string styleKey = $"YUCPBadge_{variant}";
            var style = GUI.skin.FindStyle(styleKey);
            
            if (style == null)
            {
                style = new GUIStyle(EditorStyles.miniLabel)
                {
                    name = styleKey,
                    padding = new RectOffset(6, 6, 2, 2),
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 10
                };
                
                switch (variant)
                {
                    case BadgeVariant.Default:
                        style.normal.background = CreateColorTexture(new Color(0.165f, 0.165f, 0.165f, 1f));
                        style.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
                        break;
                        
                    case BadgeVariant.Primary:
                        style.normal.background = CreateColorTexture(new Color(0.212f, 0.749f, 0.694f, 0.2f));
                        style.normal.textColor = new Color(0.212f, 0.749f, 0.694f, 1f);
                        break;
                        
                    case BadgeVariant.Success:
                        style.normal.background = CreateColorTexture(new Color(0.212f, 0.749f, 0.694f, 0.2f));
                        style.normal.textColor = new Color(0.212f, 0.749f, 0.694f, 1f);
                        break;
                        
                    case BadgeVariant.Warning:
                        style.normal.background = CreateColorTexture(new Color(0.886f, 0.647f, 0.290f, 0.2f));
                        style.normal.textColor = new Color(0.886f, 0.647f, 0.290f, 1f);
                        break;
                        
                    case BadgeVariant.Danger:
                        style.normal.background = CreateColorTexture(new Color(0.886f, 0.290f, 0.290f, 0.2f));
                        style.normal.textColor = new Color(0.886f, 0.290f, 0.290f, 1f);
                        break;
                        
                    case BadgeVariant.Info:
                        style.normal.background = CreateColorTexture(new Color(0.227f, 0.639f, 1f, 0.2f));
                        style.normal.textColor = new Color(0.227f, 0.639f, 1f, 1f);
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



