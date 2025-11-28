using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// Layout helper utilities for consistent spacing and alignment.
    /// Provides common layout patterns used across YUCP components.
    /// </summary>
    public static class YUCPLayoutHelper
    {
        public const float SpacingXS = 4f;
        public const float SpacingSM = 8f;
        public const float SpacingMD = 16f;
        public const float SpacingLG = 24f;
        public const float SpacingXL = 32f;

        public static void Space(float spacing = SpacingMD)
        {
            EditorGUILayout.Space(spacing);
        }

        public static void HorizontalDivider(float spacing = SpacingMD)
        {
            EditorGUILayout.Space(spacing);
            var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, YUCPColorHelper.BorderPrimary);
            EditorGUILayout.Space(spacing);
        }

        public static Rect GetRect(float height)
        {
            return GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        }
    }
}

