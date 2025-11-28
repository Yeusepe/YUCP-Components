using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Search field with filtering capabilities.
    /// Provides real-time search with visual feedback.
    /// </summary>
    public static class YUCPSearchField
    {
        public static string Draw(string searchText, string placeholder = "Search...")
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            var style = new GUIStyle(EditorStyles.toolbarSearchField);
            var content = new GUIContent(searchText);
            
            var rect = GUILayoutUtility.GetRect(content, style, GUILayout.ExpandWidth(true), GUILayout.Height(18));
            var result = EditorGUI.TextField(rect, searchText, style);
            
            if (string.IsNullOrEmpty(result) && !string.IsNullOrEmpty(placeholder))
            {
                var placeholderStyle = new GUIStyle(EditorStyles.toolbarSearchField)
                {
                    normal = { textColor = new Color(0.5f, 0.5f, 0.5f, 1f) },
                    fontStyle = FontStyle.Italic
                };
                EditorGUI.LabelField(rect, placeholder, placeholderStyle);
            }
            
            EditorGUILayout.EndHorizontal();
            
            return result;
        }

        public static string Draw(Rect rect, string searchText, string placeholder = "Search...")
        {
            var style = new GUIStyle(EditorStyles.toolbarSearchField);
            var result = EditorGUI.TextField(rect, searchText, style);
            
            if (string.IsNullOrEmpty(result) && !string.IsNullOrEmpty(placeholder))
            {
                var placeholderStyle = new GUIStyle(EditorStyles.toolbarSearchField)
                {
                    normal = { textColor = new Color(0.5f, 0.5f, 0.5f, 1f) },
                    fontStyle = FontStyle.Italic
                };
                EditorGUI.LabelField(rect, placeholder, placeholderStyle);
            }
            
            return result;
        }
    }
}



