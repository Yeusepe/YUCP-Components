using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Wrapper for property fields with consistent YUCP styling.
    /// Provides automatic tooltip support and validation display.
    /// </summary>
    public static class YUCPField
    {
        public static void Draw(SerializedProperty property, GUIContent label = null, bool includeChildren = true)
        {
            if (label == null)
            {
                label = new GUIContent(property.displayName, property.tooltip);
            }
            
            EditorGUILayout.PropertyField(property, label, includeChildren);
        }

        public static void Draw(SerializedProperty property, string customLabel, bool includeChildren = true)
        {
            var label = new GUIContent(customLabel, property.tooltip);
            Draw(property, label, includeChildren);
        }

        public static void Draw(Rect rect, SerializedProperty property, GUIContent label = null)
        {
            if (label == null)
            {
                label = new GUIContent(property.displayName, property.tooltip);
            }
            
            EditorGUI.PropertyField(rect, property, label);
        }
    }
}

