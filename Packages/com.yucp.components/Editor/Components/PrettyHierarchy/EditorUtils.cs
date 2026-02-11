using UnityEditor;

namespace YUCP.Components.Editor
{
    internal static class PrettyHierarchyEditorUtils
    {
        public static bool IsHierarchyFocused =>
            EditorWindow.focusedWindow != null &&
            EditorWindow.focusedWindow.titleContent.text == "Hierarchy";
    }
}
