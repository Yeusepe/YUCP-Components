using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// The YUCP design system's building blocks: styles, sections, buttons, messages and disclosures.
    /// Inspectors use <see cref="YUCPInspector"/>, which builds on these; windows can use them directly.
    /// </summary>
    public static class YUCPUIToolkitHelper
    {
        private static readonly string[] Stylesheets =
        {
            "YUCPDesignSystem.uss",
            "YUCPControls.uss",
            "YUCPInspector.uss",
            "YUCPMaterialPicker.uss"
        };

        /// <summary>
        /// Applies the design system to a root: tokens, controls and layout, in the editor's skin.
        /// A window root also gets the <c>yucp-window</c> class; inspectors get <c>yucp-inspector</c>.
        /// </summary>
        public static void LoadDesignSystemStyles(VisualElement root)
        {
            root.AddToClassList("yucp-root");
            // Same token roles, light values (HIG Color: work in light and dark). Windows keep the
            // DevTools look, which is dark in either skin.
            root.EnableInClassList("yucp-root--light", !EditorGUIUtility.isProSkin && !root.ClassListContains("yucp-window"));
            foreach (var sheet in Stylesheets)
            {
                var style = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.yucp.ui/Editor/Styles/" + sheet);
                if (style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);
            }
        }

        public enum ButtonVariant
        {
            Primary,
            Secondary,
            Danger,
            Ghost
        }

        public enum MessageType
        {
            None,
            Info,
            Warning,
            Error,
            Success
        }

        /// <summary>A section: title, optional description, and a content container (<see cref="GetCardContent"/>).</summary>
        public static VisualElement CreateCard(string title = null, string subtitle = null)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            if (!string.IsNullOrEmpty(title))
            {
                var titleLabel = new Label(title);
                titleLabel.AddToClassList("yucp-section-title");
                section.Add(titleLabel);
            }
            if (!string.IsNullOrEmpty(subtitle))
            {
                var description = new Label(subtitle);
                description.AddToClassList("yucp-section-description");
                section.Add(description);
            }
            var content = new VisualElement();
            content.AddToClassList("yucp-section-content");
            section.Add(content);
            return section;
        }

        public static VisualElement GetCardContent(VisualElement section) =>
            section.Q<VisualElement>(null, "yucp-section-content");

        /// <summary>A button. Secondary is the neutral default; Ghost is a text-only button.</summary>
        public static Button CreateButton(string text, Action onClick, ButtonVariant variant = ButtonVariant.Primary)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("yucp-button");
            switch (variant)
            {
                case ButtonVariant.Primary: button.AddToClassList("yucp-button-primary"); break;
                case ButtonVariant.Danger: button.AddToClassList("yucp-button-danger"); break;
                case ButtonVariant.Ghost: button.AddToClassList("yucp-button-text"); break;
            }
            return button;
        }

        /// <summary>A message: a tinted box with white text (HIG Accessibility: 4.5:1 contrast).</summary>
        public static VisualElement CreateHelpBox(string message, MessageType type = MessageType.Info, string title = null)
        {
            var box = new VisualElement();
            box.AddToClassList("yucp-help-box");
            if (type == MessageType.Warning) box.AddToClassList("yucp-help-box-warning");
            if (type == MessageType.Error) box.AddToClassList("yucp-help-box-error");
            if (type == MessageType.Success) box.AddToClassList("yucp-help-box-success");
            if (!string.IsNullOrEmpty(title))
            {
                var titleLabel = new Label(title);
                titleLabel.AddToClassList("yucp-help-box-title");
                box.Add(titleLabel);
            }
            var text = new Label(message);
            text.AddToClassList("yucp-help-box-text");
            box.Add(text);
            return box;
        }

        /// <summary>A disclosure with an optional icon before the title and a badge after it.</summary>
        public static Foldout CreateFoldout(string title, bool expanded = false, Texture2D icon = null, string badge = null)
        {
            var foldout = new Foldout { text = title, value = expanded };
            foldout.AddToClassList("yucp-foldout");
            var toggleRow = foldout.Q(className: Toggle.inputUssClassName);
            if (icon != null && toggleRow != null)
            {
                var iconElement = new VisualElement();
                iconElement.AddToClassList("yucp-foldout-icon");
                iconElement.style.backgroundImage = new StyleBackground(icon);
                toggleRow.Insert(Mathf.Min(1, toggleRow.childCount), iconElement);
            }
            if (!string.IsNullOrEmpty(badge) && toggleRow != null)
            {
                var badgeLabel = new Label(badge);
                badgeLabel.AddToClassList("yucp-badge");
                badgeLabel.AddToClassList("yucp-badge-primary");
                badgeLabel.AddToClassList("yucp-foldout-badge");
                toggleRow.Add(badgeLabel);
            }
            return foldout;
        }
    }
}
