using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor
{
    internal static class PrettyHierarchyEditorColors
    {
        private static readonly Color32 DarkBackground = new Color32(56, 56, 56, 255);
        private static readonly Color32 DarkObjectSelectedBackground = new Color32(77, 77, 77, 255);
        private static readonly Color32 DarkObjectSelectedWindowFocusedBackground = new Color32(44, 93, 134, 255);
        private static readonly Color32 DarkHoverOverlay = new Color32(255, 255, 255, 15);

        private static readonly Color32 LightBackground = new Color32(200, 200, 200, 255);
        private static readonly Color32 LightObjectSelectedBackground = new Color32(178, 178, 178, 255);
        private static readonly Color32 LightObjectSelectedWindowFocusedBackground = new Color32(58, 114, 176, 255);
        private static readonly Color32 LightHoverOverlay = new Color32(0, 0, 0, 21);

        private static readonly Color32 DarkText = new Color32(210, 210, 210, 255);
        private static readonly Color32 DarkTextHighlighted = new Color32(255, 255, 255, 255);
        private const byte DarkTextAlphaObjectEnabled = 255;
        private const byte DarkTextAlphaObjectDisabled = 103;

        private static readonly Color32 LightText = new Color32(2, 2, 2, 255);
        private static readonly Color32 LightTextHighlighted = new Color32(255, 255, 255, 255);
        private const byte LightTextAlphaObjectEnabled = 255;
        private const byte LightTextAlphaObjectDisabled = 95;

        public static Color32 Background => EditorGUIUtility.isProSkin ? DarkBackground : LightBackground;
        public static Color32 ObjectSelectedBackground => EditorGUIUtility.isProSkin ? DarkObjectSelectedBackground : LightObjectSelectedBackground;
        public static Color32 ObjectSelectedWindowFocusedBackground => EditorGUIUtility.isProSkin ? DarkObjectSelectedWindowFocusedBackground : LightObjectSelectedWindowFocusedBackground;
        public static Color32 HoverOverlay => EditorGUIUtility.isProSkin ? DarkHoverOverlay : LightHoverOverlay;

        public static Color32 Text => EditorGUIUtility.isProSkin ? DarkText : LightText;
        public static Color32 TextHighlighted => EditorGUIUtility.isProSkin ? DarkTextHighlighted : LightTextHighlighted;
        public static byte TextAlphaObjectEnabled => EditorGUIUtility.isProSkin ? DarkTextAlphaObjectEnabled : LightTextAlphaObjectEnabled;
        public static byte TextAlphaObjectDisabled => EditorGUIUtility.isProSkin ? DarkTextAlphaObjectDisabled : LightTextAlphaObjectDisabled;

        public static Color CollapseIconTintColor => EditorGUIUtility.isProSkin ? Color.white : Color.black;
        public static Color EditPrefabIconTintColor => EditorGUIUtility.isProSkin ? Color.white : Color.black;

        public static Color32 GetDefaultBackgroundColor(bool windowIsFocused, bool selectionContainsObject)
        {
            return selectionContainsObject
                ? (windowIsFocused ? ObjectSelectedWindowFocusedBackground : ObjectSelectedBackground)
                : Background;
        }

        public static Color32 GetDefaultTextColor(bool windowIsFocused, bool selectionContainsObject, bool objectIsEnabled)
        {
            bool textHighlighted = IsTextHighlighted(windowIsFocused, selectionContainsObject, objectIsEnabled);
            Color32 color = textHighlighted ? TextHighlighted : Text;
            color.a = objectIsEnabled ? TextAlphaObjectEnabled : TextAlphaObjectDisabled;
            return color;
        }

        public static bool IsTextHighlighted(bool windowIsFocused, bool selectionContainsObject, bool objectIsEnabled)
        {
            return windowIsFocused && selectionContainsObject && objectIsEnabled;
        }
    }
}
