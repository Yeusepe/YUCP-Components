using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Progress indicators with YUCP styling.
    /// Supports custom colors and labels.
    /// </summary>
    public static class YUCPProgressBar
    {
        public static void Draw(float value, float min = 0f, float max = 1f, string label = null, Color? color = null)
        {
            value = Mathf.Clamp01((value - min) / (max - min));
            var progressColor = color ?? new Color(0.212f, 0.749f, 0.694f, 1f); // YUCP Teal
            
            var rect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
            
            // Background
            EditorGUI.DrawRect(rect, new Color(0.325f, 0.325f, 0.325f, 1f));
            
            // Progress
            var progressRect = new Rect(rect.x, rect.y, rect.width * value, rect.height);
            EditorGUI.DrawRect(progressRect, progressColor);
            
            // Label
            if (!string.IsNullOrEmpty(label))
            {
                var labelStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white },
                    fontSize = 10
                };
                GUI.Label(rect, label, labelStyle);
            }
        }
    }
}



