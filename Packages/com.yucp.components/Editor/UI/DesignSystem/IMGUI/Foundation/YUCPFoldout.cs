using System;
using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Foldout with icons, badges, and custom styling.
    /// Provides consistent foldout behavior across YUCP editors.
    /// </summary>
    public class YUCPFoldout : IDisposable
    {
        private readonly string key;
        private bool isExpanded;
        private readonly string title;
        private readonly Texture2D icon;
        private readonly string badge;

        public YUCPFoldout(string title, ref bool expanded, Texture2D icon = null, string badge = null)
        {
            this.title = title;
            this.icon = icon;
            this.badge = badge;
            this.isExpanded = expanded;
            this.key = $"YUCPFoldout_{title}_{EditorGUIUtility.GetControlID(FocusType.Passive)}";
            
            DrawHeader();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            
            if (icon != null)
            {
                var iconRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
                GUI.DrawTexture(iconRect, icon);
                GUILayout.Space(6);
            }
            
            isExpanded = EditorGUILayout.Foldout(isExpanded, title, true);
            
            if (!string.IsNullOrEmpty(badge))
            {
                GUILayout.FlexibleSpace();
                var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    padding = new RectOffset(4, 4, 2, 2),
                    alignment = TextAnchor.MiddleCenter
                };
                GUILayout.Label(badge, badgeStyle);
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (isExpanded)
            {
                EditorGUILayout.Space(4);
                EditorGUI.indentLevel++;
            }
        }

        private Texture2D CreateColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        public void Dispose()
        {
            if (isExpanded)
            {
                EditorGUILayout.Space(4);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);
        }

        public bool IsExpanded => isExpanded;
    }

    public static class YUCPFoldoutHelper
    {
        public static bool DrawFoldout(string title, bool expanded, Texture2D icon = null, string badge = null)
        {
            EditorGUILayout.BeginHorizontal();
            
            if (icon != null)
            {
                var iconRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
                GUI.DrawTexture(iconRect, icon);
                GUILayout.Space(4);
            }
            
            bool result = EditorGUILayout.Foldout(expanded, title, true);
            
            if (!string.IsNullOrEmpty(badge))
            {
                GUILayout.FlexibleSpace();
                var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    padding = new RectOffset(4, 4, 2, 2),
                    alignment = TextAnchor.MiddleCenter
                };
                badgeStyle.normal.background = CreateColorTexture(new Color(0.212f, 0.749f, 0.694f, 0.2f));
                badgeStyle.normal.textColor = new Color(0.212f, 0.749f, 0.694f, 1f);
                GUILayout.Label(badge, badgeStyle);
            }
            
            EditorGUILayout.EndHorizontal();
            
            return result;
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

