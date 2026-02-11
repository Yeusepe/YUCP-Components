using UnityEditor;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    internal readonly struct PrettyHierarchyItem
    {
        public int InstanceID { get; }
        public bool IsSelected { get; }
        public bool IsHovered { get; }
        public GameObject GameObject { get; }
        public PrettyHierarchyData Data { get; }
        public Rect BackgroundRect { get; }
        public Rect IconAreaRect { get; }
        /// <summary>Full right-hand area (for gradient).</summary>
        public Rect TextAreaRect { get; }
        /// <summary>Inner rect for the label (with margins/padding).</summary>
        public Rect TextRect { get; }
        public Rect CollapseToggleIconRect { get; }
        /// <summary>Position for the expand/collapse folder icon and toggle (default slot + offset).</summary>
        public Rect FolderIconRect { get; }
        public Rect PrefabIconRect { get; }
        public Rect EditPrefabIconRect { get; }

        public PrettyHierarchyItem(int instanceID, Rect selectionRect, PrettyHierarchyData data)
        {
            InstanceID = instanceID;
            IsSelected = Selection.Contains(instanceID);
            GameObject = data.gameObject;
            Data = data;

            // Row-level margins: inset the content area from Unity's row
            float contentX = selectionRect.x + data.MarginLeft;
            float contentY = selectionRect.y + data.MarginTop;
            float contentW = Mathf.Max(0, selectionRect.width - data.MarginLeft - data.MarginRight);
            float contentH = Mathf.Max(0, selectionRect.height - data.MarginTop - data.MarginBottom);
            Rect contentRect = new Rect(contentX, contentY, contentW, contentH);

            // Computed height: icon contribution vs text contribution (used for layout)
            float iconHeight = data.IconSize + data.IconMarginTop + data.IconMarginBottom;
            GUIStyle labelStyle = BuildLabelStyle(data);
            float textLineHeight = labelStyle.CalcHeight(new GUIContent("Ay"), contentW > 0 ? contentW : 1000f);
            textLineHeight = Mathf.Max(textLineHeight, data.FontSize + 4f);
            float textHeight = textLineHeight + data.TextMarginTop + data.TextMarginBottom;
            float computedHeight = Mathf.Max(iconHeight, textHeight) + 2f;

            // Clamp to the row height Unity assigned so we never overlay the next row.
            // The hierarchy window uses fixed row height; we cannot change it from hierarchyWindowItemOnGUI.
            float usedHeight = Mathf.Min(computedHeight, contentRect.height);

            // Background rect: same width as content, height clamped to row, vertically centered in content rect
            float bgY = contentRect.y + (contentRect.height - usedHeight) * 0.5f;
            BackgroundRect = new Rect(contentRect.x, bgY, contentRect.width, usedHeight);
            IsHovered = BackgroundRect.Contains(Event.current.mousePosition);

            // Icon area: collapse toggle slot + main icon
            float iconZoneWidth = data.IconSize * 2f + 4f;
            IconAreaRect = new Rect(
                BackgroundRect.x + data.IconMarginLeft,
                BackgroundRect.y + data.IconMarginTop,
                iconZoneWidth,
                BackgroundRect.height - data.IconMarginTop - data.IconMarginBottom);

            float iconCenterY = BackgroundRect.y + BackgroundRect.height * 0.5f;
            float halfIcon = data.IconSize * 0.5f;
            float iconStep = data.IconSize + 2f;
            CollapseToggleIconRect = new Rect(
                BackgroundRect.x + data.IconMarginLeft,
                iconCenterY - halfIcon,
                data.IconSize,
                data.IconSize);
            FolderIconRect = new Rect(
                CollapseToggleIconRect.x + data.FolderIconOffsetX,
                CollapseToggleIconRect.y + data.FolderIconOffsetY,
                data.IconSize,
                data.IconSize);
            PrefabIconRect = new Rect(
                BackgroundRect.x + data.IconMarginLeft + iconStep,
                iconCenterY - halfIcon,
                data.IconSize,
                data.IconSize);
            EditPrefabIconRect = new Rect(
                BackgroundRect.xMax - data.IconSize - data.IconMarginRight - 2f,
                BackgroundRect.y + (BackgroundRect.height - data.IconSize) * 0.5f,
                data.IconSize,
                data.IconSize);

            // Text area (full right-hand zone for gradient)
            float textAreaX = BackgroundRect.x + data.IconMarginLeft + iconZoneWidth + data.IconMarginRight;
            TextAreaRect = new Rect(textAreaX, BackgroundRect.y, Mathf.Max(0, BackgroundRect.width - (textAreaX - BackgroundRect.x)), BackgroundRect.height);

            // Text rect: inner area for the label (margins + padding)
            float textX = TextAreaRect.x + data.TextMarginLeft + data.PaddingLeft;
            float textW = Mathf.Max(0, TextAreaRect.width - data.TextMarginLeft - data.TextMarginRight - data.PaddingLeft - data.PaddingRight - (data.ShowEditPrefabIcon ? data.IconSize + 4f : 0f));
            TextRect = new Rect(textX, TextAreaRect.y + data.TextMarginTop, textW, TextAreaRect.height - data.TextMarginTop - data.TextMarginBottom);
        }

        private static GUIStyle BuildLabelStyle(PrettyHierarchyData data)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                font = data.Font,
                fontStyle = data.FontStyle,
                fontSize = data.FontSize,
                alignment = data.Alignment
            };
            return style;
        }
    }
}
