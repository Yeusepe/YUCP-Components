using System;
using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Enum dropdown with consistent styling.
    /// Enum field with custom visual feedback.
    /// </summary>
    public static class YUCPEnumField
    {
        public static T Draw<T>(string label, T value) where T : Enum
        {
            return Draw(new GUIContent(label), value);
        }

        public static T Draw<T>(GUIContent label, T value) where T : Enum
        {
            return (T)EditorGUILayout.EnumPopup(label, value);
        }

        public static T Draw<T>(Rect rect, string label, T value) where T : Enum
        {
            return (T)EditorGUI.EnumPopup(rect, label, value);
        }
    }
}

