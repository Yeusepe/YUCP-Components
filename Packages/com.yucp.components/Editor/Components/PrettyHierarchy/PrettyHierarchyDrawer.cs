using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    [InitializeOnLoad]
    internal static class PrettyHierarchyDrawer
    {
        static PrettyHierarchyDrawer()
        {
            EditorApplication.hierarchyWindowItemOnGUI += HandleHierarchyWindowItemOnGUI;
        }

        private static void HandleHierarchyWindowItemOnGUI(int instanceID, Rect selectionRect)
        {
            Object instance = EditorUtility.InstanceIDToObject(instanceID);
            if (instance == null) return;

            var go = instance as GameObject;
            if (go == null) return;

            var data = go.GetComponent<PrettyHierarchyData>();
            if (data == null) return;

            var item = new PrettyHierarchyItem(instanceID, selectionRect, data);

            // Cover the full row (including foldout area) so Unity's label and foldout arrow are hidden
            Color32 rowBg = PrettyHierarchyEditorColors.GetDefaultBackgroundColor(PrettyHierarchyEditorUtils.IsHierarchyFocused, item.IsSelected);
            float leftWidth = GetFoldoutAreaWidth(go);
            Rect fullRowRect = new Rect(selectionRect.x - leftWidth, selectionRect.y, selectionRect.width + leftWidth, selectionRect.height);
            EditorGUI.DrawRect(fullRowRect, rowBg);

            PaintBackground(item, rowBg);
            PaintHoverOverlay(item);
            PaintText(item);

            // First icon slot: optional closed/open folder icon + invisible collapse toggle (click to expand/collapse)
            if (go.transform.childCount > 0)
            {
                if (data.ShowExpandCollapseFolderIcon)
                    PaintExpandCollapseFolderIcon(item);
                PaintCollapseToggle(item);
            }
            if (data.ShowIcon) PaintIcon(item);
            if (data.ShowEditPrefabIcon) PaintEditPrefabIcon(item);

            if (data.BorderWidth > 0)
                PrettyHierarchyDrawerHelper.DrawBorder(item.BackgroundRect, data.BorderWidth, data.BorderColor,
                    data.CornerRadiusTopLeft, data.CornerRadiusTopRight, data.CornerRadiusBottomRight, data.CornerRadiusBottomLeft);
        }

        private static float GetFoldoutAreaWidth(GameObject go)
        {
            int depth = 0;
            Transform t = go.transform;
            while (t.parent != null) { depth++; t = t.parent; }
            const float indentPerLevel = 14f;
            const float foldoutWidth = 16f;
            return depth * indentPerLevel + foldoutWidth;
        }

        private static void PaintExpandCollapseFolderIcon(PrettyHierarchyItem item)
        {
            bool isExpanded = GetIsExpanded(item.InstanceID);
            Texture icon = null;
            
            if (isExpanded)
            {
                if (item.Data.OpenFolderCustomIcon != null) icon = item.Data.OpenFolderCustomIcon;
                else icon = EditorGUIUtility.IconContent(item.Data.OpenFolderIconName)?.image;
            }
            else
            {
                if (item.Data.ClosedFolderCustomIcon != null) icon = item.Data.ClosedFolderCustomIcon;
                else icon = EditorGUIUtility.IconContent(item.Data.ClosedFolderIconName)?.image;
            }

            if (icon == null) return;
            Color color = item.GameObject.activeInHierarchy ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(item.FolderIconRect, icon, ScaleMode.ScaleToFit, true, 0f, color, 0f, 0f);
        }

        private static void PaintCollapseToggle(PrettyHierarchyItem item)
        {
            bool isExpanded = GetIsExpanded(item.InstanceID);
            EditorGUI.BeginChangeCheck();
            bool newExpanded = GUI.Toggle(item.FolderIconRect, isExpanded, GUIContent.none, GUIStyle.none);
            if (EditorGUI.EndChangeCheck() && newExpanded != isExpanded)
                SetExpanded(item.InstanceID, newExpanded);
        }

        private static bool GetIsExpanded(int instanceID)
        {
            var window = GetLastHierarchyWindow();
            if (window == null) return false;
            var getExpanded = window.GetType().GetMethod("GetExpandedIDs", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (getExpanded == null) return false;
            var ids = (int[])getExpanded.Invoke(window, null);
            return ids != null && System.Linq.Enumerable.Contains(ids, instanceID);
        }

        private static void SetExpanded(int instanceID, bool expand)
        {
            var window = GetLastHierarchyWindow();
            if (window == null) return;
            object sceneHierarchy = window.GetType().GetProperty("sceneHierarchy", BindingFlags.Public | BindingFlags.Instance)?.GetValue(window);
            if (sceneHierarchy == null) return;
            var expandMethod = sceneHierarchy.GetType().GetMethod("ExpandTreeViewItem", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (expandMethod == null) return;
            expandMethod.Invoke(sceneHierarchy, new object[] { instanceID, expand });
        }

        private static object GetLastHierarchyWindow()
        {
            var windowType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
            var prop = windowType?.GetProperty("lastInteractedHierarchyWindow", BindingFlags.Public | BindingFlags.Static);
            return prop?.GetValue(null);
        }

        private static void PaintBackground(PrettyHierarchyItem item, Color32 hierarchyBgColor)
        {
            // Draw Shadow first if enabled
            if (item.Data.ShowShadow)
            {
                PrettyHierarchyDrawerHelper.DrawShadow(
                    item.BackgroundRect,
                    item.Data.ShadowColor,
                    item.Data.ShadowOffset,
                    item.Data.ShadowBlur,
                    item.Data.CornerRadiusTopLeft,
                    item.Data.CornerRadiusTopRight,
                    item.Data.CornerRadiusBottomRight,
                    item.Data.CornerRadiusBottomLeft
                );
            }

            if (item.Data.UseBackgroundGradient)
            {
                // Gradient Background
                Texture2D gradTex = PrettyHierarchyDrawerHelper.GetGradientTexture(item.Data.BackgroundGradient);

                PrettyHierarchyDrawerHelper.DrawRoundedGradientRect(
                    item.BackgroundRect,
                    gradTex,
                    Color.white,
                    item.Data.GradientAngle,
                    item.Data.CornerRadiusTopLeft,
                    item.Data.CornerRadiusTopRight,
                    item.Data.CornerRadiusBottomRight,
                    item.Data.CornerRadiusBottomLeft,
                    item.Data.BackgroundAlpha,
                    0.5f // Sharp main rect
                );
            }
            else
            {
                // Solid Background
                Color color;
                if (item.Data.UseDefaultBackgroundColor || item.IsSelected)
                    color = PrettyHierarchyEditorColors.GetDefaultBackgroundColor(PrettyHierarchyEditorUtils.IsHierarchyFocused, item.IsSelected);
                else
                    color = item.Data.BackgroundColor;

                PrettyHierarchyDrawerHelper.DrawRoundedRect(item.BackgroundRect, color,
                    item.Data.CornerRadiusTopLeft, item.Data.CornerRadiusTopRight,
                    item.Data.CornerRadiusBottomRight, item.Data.CornerRadiusBottomLeft,
                    item.Data.BackgroundAlpha);
            }
        }

        private static void PaintHoverOverlay(PrettyHierarchyItem item)
        {
            if (item.IsHovered && !item.IsSelected)
            {
                var overlay = PrettyHierarchyEditorColors.HoverOverlay;
                PrettyHierarchyDrawerHelper.DrawRoundedRect(item.BackgroundRect, overlay,
                        item.Data.CornerRadiusTopLeft, item.Data.CornerRadiusTopRight,
                        item.Data.CornerRadiusBottomRight, item.Data.CornerRadiusBottomLeft, 1f);
            }
        }

        private static void PaintText(PrettyHierarchyItem item)
        {
            Color color;
            if (item.Data.UseDefaultTextColor || item.IsSelected)
                color = PrettyHierarchyEditorColors.GetDefaultTextColor(PrettyHierarchyEditorUtils.IsHierarchyFocused, item.IsSelected, item.GameObject.activeInHierarchy);
            else
            {
                color = item.Data.TextColor;
                color.a = item.GameObject.activeInHierarchy
                    ? PrettyHierarchyEditorColors.TextAlphaObjectEnabled
                    : PrettyHierarchyEditorColors.TextAlphaObjectDisabled;
            }

            // Vertical middle so text is centered in the row; keep horizontal from data.Alignment (Left=0, Center=1, Right=2)
            int col = (int)item.Data.Alignment % 3;
            TextAnchor verticalMiddle = (TextAnchor)(3 + col); // MiddleLeft, MiddleCenter, MiddleRight

            var labelStyle = new GUIStyle
            {
                normal = new GUIStyleState { textColor = color },
                fontStyle = item.Data.FontStyle,
                alignment = verticalMiddle,
                fontSize = item.Data.FontSize,
                font = item.Data.Font
            };

            if (item.Data.UseTextGradient && !item.IsSelected)
            {
                PrettyHierarchyDrawerHelper.DrawGradientText(item.TextRect, item.GameObject.name, labelStyle, item.Data.TextGradient, item.Data.TextGradientAngle);
            }
            else if (item.Data.TextDropShadow)
            {
                EditorGUI.DropShadowLabel(item.TextRect, item.GameObject.name, labelStyle);
            }
            else
            {
                EditorGUI.LabelField(item.TextRect, item.GameObject.name, labelStyle);
            }
        }

        private static void PaintIcon(PrettyHierarchyItem item)
        {
            if (!item.Data.ShowIcon) return;

            Texture icon = null;
            if (item.Data.UseCustomIcon)
            {
                if (item.Data.UseCustomIconAsTexture && item.Data.CustomIcon != null)
                    icon = item.Data.CustomIcon;
                else
                    icon = EditorGUIUtility.IconContent(item.Data.CustomIconBuiltInName).image;
            }
            if (icon == null)
                icon = EditorGUIUtility.ObjectContent(EditorUtility.InstanceIDToObject(item.InstanceID), null).image;
            if (icon == null) return;

            if (!item.Data.UseCustomIcon && PrettyHierarchyEditorUtils.IsHierarchyFocused && item.IsSelected)
            {
                if (icon.name == "d_Prefab Icon" || icon.name == "Prefab Icon")
                    icon = EditorGUIUtility.IconContent("d_Prefab On Icon").image;
                else if (icon.name == "GameObject Icon")
                    icon = EditorGUIUtility.IconContent("GameObject On Icon").image;
            }

            Color color = item.GameObject.activeInHierarchy ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(item.PrefabIconRect, icon, ScaleMode.StretchToFill, true, 0f, color, 0f, 0f);
        }

        private static void PaintEditPrefabIcon(PrettyHierarchyItem item)
        {
            Object prefabSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(item.GameObject);
            if (prefabSource == null) return;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(item.GameObject)) return;

            Texture icon = EditorGUIUtility.IconContent("ArrowNavigationRight").image;
            if (icon != null)
            {
                // Hover highlight tint
                Color tint = item.EditPrefabIconRect.Contains(Event.current.mousePosition) 
                    ? Color.white 
                    : PrettyHierarchyEditorColors.EditPrefabIconTintColor;
                
                GUI.DrawTexture(item.EditPrefabIconRect, icon, ScaleMode.StretchToFill, true, 0f, tint, 0f, 0f);

                // Handle click to open Prefab
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && item.EditPrefabIconRect.Contains(Event.current.mousePosition))
                {
                    string assetPath = AssetDatabase.GetAssetPath(prefabSource);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<Object>(assetPath));
                        Event.current.Use(); // Consume the event so Unity doesn't select the row behind it
                    }
                }
            }
        }
    }
}
