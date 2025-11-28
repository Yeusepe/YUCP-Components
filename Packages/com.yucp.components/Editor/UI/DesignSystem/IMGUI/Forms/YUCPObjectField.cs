using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Object picker with preview and consistent styling.
    /// Version of Unity's ObjectField with custom visual feedback.
    /// </summary>
    public static class YUCPObjectField
    {
        public static T Draw<T>(string label, T obj, bool allowSceneObjects = true) where T : Object
        {
            return Draw<T>(new GUIContent(label), obj, allowSceneObjects);
        }

        public static T Draw<T>(GUIContent label, T obj, bool allowSceneObjects = true) where T : Object
        {
            return (T)EditorGUILayout.ObjectField(label, obj, typeof(T), allowSceneObjects);
        }

        public static T Draw<T>(Rect rect, string label, T obj, bool allowSceneObjects = true) where T : Object
        {
            return (T)EditorGUI.ObjectField(rect, label, obj, typeof(T), allowSceneObjects);
        }
    }
}

