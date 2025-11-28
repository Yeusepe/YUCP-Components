using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Section dividers with optional labels for visual separation.
    /// Provides clear visual hierarchy between sections.
    /// </summary>
    public static class YUCPDivider
    {
        public static void Draw(string label = null, float spacing = 12f)
        {
            EditorGUILayout.Space(spacing);
            
            if (!string.IsNullOrEmpty(label))
            {
                var labelStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    normal = { textColor = new Color(0.5f, 0.5f, 0.5f, 1f) },
                    alignment = TextAnchor.MiddleLeft
                };
                
                EditorGUILayout.LabelField(label, labelStyle);
                EditorGUILayout.Space(4);
            }
            
            var lineColor = new Color(0.165f, 0.165f, 0.165f, 1f);
            var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, lineColor);
            
            EditorGUILayout.Space(spacing);
        }
    }
}



