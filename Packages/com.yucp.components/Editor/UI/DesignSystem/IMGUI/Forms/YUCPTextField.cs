using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Text input with validation and consistent styling.
    /// Text field with custom styling.
    /// </summary>
    public static class YUCPTextField
    {
        public static string Draw(string label, string value, string placeholder = null)
        {
            return Draw(new GUIContent(label), value, placeholder);
        }

        public static string Draw(GUIContent label, string value, string placeholder = null)
        {
            EditorGUILayout.BeginHorizontal();
            
            if (label != null)
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            }
            
            var style = new GUIStyle(EditorStyles.textField)
            {
                padding = new RectOffset(8, 8, 6, 6)
            };
            
            var result = EditorGUILayout.TextField(value ?? "", style);
            
            if (string.IsNullOrEmpty(result) && !string.IsNullOrEmpty(placeholder))
            {
                var placeholderStyle = new GUIStyle(EditorStyles.textField)
                {
                    normal = { textColor = new Color(0.5f, 0.5f, 0.5f, 1f) },
                    fontStyle = FontStyle.Italic
                };
                var rect = GUILayoutUtility.GetLastRect();
                EditorGUI.LabelField(rect, placeholder, placeholderStyle);
            }
            
            EditorGUILayout.EndHorizontal();
            
            return result;
        }
    }
}

