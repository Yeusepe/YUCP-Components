using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Range slider with value display and consistent styling.
    /// Shows current value alongside the slider.
    /// </summary>
    public static class YUCPSlider
    {
        public static float Draw(string label, float value, float min, float max, bool showValue = true)
        {
            return Draw(new GUIContent(label), value, min, max, showValue);
        }

        public static float Draw(GUIContent label, float value, float min, float max, bool showValue = true)
        {
            EditorGUILayout.BeginHorizontal();
            
            if (showValue)
            {
                var valueLabel = $"{value:F2}";
                EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
                value = EditorGUILayout.Slider(value, min, max);
                EditorGUILayout.LabelField(valueLabel, GUILayout.Width(50));
            }
            else
            {
                value = EditorGUILayout.Slider(label, value, min, max);
            }
            
            EditorGUILayout.EndHorizontal();
            
            return value;
        }

        public static float Draw(Rect rect, string label, float value, float min, float max)
        {
            return EditorGUI.Slider(rect, label, value, min, max);
        }
    }
}

