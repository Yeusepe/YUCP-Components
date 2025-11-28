using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Vector2/3/4 inputs with consistent styling.
    /// Vector field with custom layout and visual feedback.
    /// </summary>
    public static class YUCPVectorField
    {
        public static Vector2 Draw(string label, Vector2 value)
        {
            return Draw(new GUIContent(label), value);
        }

        public static Vector2 Draw(GUIContent label, Vector2 value)
        {
            return EditorGUILayout.Vector2Field(label, value);
        }

        public static Vector3 Draw(string label, Vector3 value)
        {
            return Draw(new GUIContent(label), value);
        }

        public static Vector3 Draw(GUIContent label, Vector3 value)
        {
            return EditorGUILayout.Vector3Field(label, value);
        }

        public static Vector4 Draw(string label, Vector4 value)
        {
            return Draw(new GUIContent(label), value);
        }

        public static Vector4 Draw(GUIContent label, Vector4 value)
        {
            return EditorGUILayout.Vector4Field(label, value);
        }
    }
}

