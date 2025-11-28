using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// Helper class for creating UI Toolkit elements programmatically.
    /// Provides static methods to create styled components following the YUCP design system.
    /// </summary>
    public static class YUCPUIToolkitHelper
    {
        /// <summary>
        /// Loads and applies all design system stylesheets to the root element.
        /// </summary>
        public static void LoadDesignSystemStyles(VisualElement root)
        {
            root.AddToClassList("yucp-root");
            
            var designSystemStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/Styles/YUCPDesignSystem.uss");
            if (designSystemStyle != null)
            {
                root.styleSheets.Add(designSystemStyle);
            }
            
            var cardStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPCard.uss");
            if (cardStyle != null)
            {
                root.styleSheets.Add(cardStyle);
            }
            
            var alertStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPAlert.uss");
            if (alertStyle != null)
            {
                root.styleSheets.Add(alertStyle);
            }
            
            var buttonStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPButton.uss");
            if (buttonStyle != null)
            {
                root.styleSheets.Add(buttonStyle);
            }
            
            var foldoutStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPFoldout.uss");
            if (foldoutStyle != null)
            {
                root.styleSheets.Add(foldoutStyle);
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

        /// <summary>
        /// Creates a card container with optional title and subtitle.
        /// </summary>
        public static VisualElement CreateCard(string title = null, string subtitle = null, bool isInfo = false)
        {
            var card = new VisualElement();
            card.AddToClassList("yucp-card");
            if (isInfo)
            {
                card.AddToClassList("yucp-card-info");
            }

            if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(subtitle))
            {
                var header = new VisualElement();
                header.AddToClassList("yucp-card-header");

                if (!string.IsNullOrEmpty(title))
                {
                    var titleLabel = new Label(title);
                    titleLabel.AddToClassList("yucp-card-title");
                    header.Add(titleLabel);
                }

                if (!string.IsNullOrEmpty(subtitle))
                {
                    var subtitleLabel = new Label(subtitle);
                    subtitleLabel.AddToClassList("yucp-card-subtitle");
                    header.Add(subtitleLabel);
                }

                card.Add(header);
            }

            var content = new VisualElement();
            content.AddToClassList("yucp-card-content");
            card.Add(content);

            return card;
        }

        /// <summary>
        /// Gets the content container of a card for adding child elements.
        /// </summary>
        public static VisualElement GetCardContent(VisualElement card)
        {
            return card.Q<VisualElement>(null, "yucp-card-content");
        }

        /// <summary>
        /// Creates a styled button with variant support.
        /// </summary>
        public static Button CreateButton(string text, Action onClick, ButtonVariant variant = ButtonVariant.Primary)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("yucp-button");

            switch (variant)
            {
                case ButtonVariant.Primary:
                    button.AddToClassList("yucp-button-primary");
                    break;
                case ButtonVariant.Secondary:
                    button.AddToClassList("yucp-button-secondary");
                    break;
                case ButtonVariant.Danger:
                    button.AddToClassList("yucp-button-danger");
                    break;
                case ButtonVariant.Ghost:
                    button.AddToClassList("yucp-button-ghost");
                    break;
            }

            return button;
        }

        /// <summary>
        /// Creates a help box/alert with message type support.
        /// </summary>
        public static VisualElement CreateHelpBox(string message, MessageType type = MessageType.Info, string title = null)
        {
            var alert = new VisualElement();
            alert.AddToClassList("yucp-alert");

            switch (type)
            {
                case MessageType.Info:
                    alert.AddToClassList("yucp-alert-info");
                    break;
                case MessageType.Warning:
                    alert.AddToClassList("yucp-alert-warning");
                    break;
                case MessageType.Error:
                    alert.AddToClassList("yucp-alert-error");
                    break;
                case MessageType.Success:
                    alert.AddToClassList("yucp-alert-success");
                    break;
            }

            if (!string.IsNullOrEmpty(title))
            {
                var titleLabel = new Label(title);
                titleLabel.AddToClassList("yucp-alert-title");
                alert.Add(titleLabel);
            }

            var messageLabel = new Label(message);
            messageLabel.AddToClassList("yucp-alert-message");
            alert.Add(messageLabel);

            return alert;
        }

        /// <summary>
        /// Creates a property field with consistent styling.
        /// </summary>
        public static PropertyField CreateField(SerializedProperty property, string label = null)
        {
            var field = new PropertyField(property, label ?? property.displayName);
            field.AddToClassList("yucp-field-input");
            return field;
        }

        /// <summary>
        /// Creates a section header with optional subtitle.
        /// </summary>
        public static VisualElement CreateSection(string title, string subtitle = null)
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("yucp-section-title");
            section.Add(titleLabel);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = new Label(subtitle);
                subtitleLabel.AddToClassList("yucp-section-subtitle");
                section.Add(subtitleLabel);
            }

            var content = new VisualElement();
            content.AddToClassList("yucp-section-content");
            section.Add(content);

            return section;
        }

        /// <summary>
        /// Gets the content container of a section for adding child elements.
        /// </summary>
        public static VisualElement GetSectionContent(VisualElement section)
        {
            return section.Q<VisualElement>(null, "yucp-section-content");
        }

        /// <summary>
        /// Creates a foldout with optional icon and badge.
        /// </summary>
        public static Foldout CreateFoldout(string title, bool expanded = false, Texture2D icon = null, string badge = null)
        {
            var foldout = new Foldout
            {
                text = title,
                value = expanded
            };
            foldout.AddToClassList("yucp-foldout");

            if (icon != null || !string.IsNullOrEmpty(badge))
            {
                var header = new VisualElement();
                header.AddToClassList("yucp-foldout-header");

                if (icon != null)
                {
                    var iconElement = new VisualElement();
                    iconElement.AddToClassList("yucp-foldout-icon");
                    iconElement.style.backgroundImage = new StyleBackground(icon);
                    header.Add(iconElement);
                }

                if (!string.IsNullOrEmpty(badge))
                {
                    var badgeLabel = new Label(badge);
                    badgeLabel.AddToClassList("yucp-foldout-badge");
                    header.Add(badgeLabel);
                }

                // Note: Foldout's toggle is not easily customizable, so icon/badge support is limited
                // This would require a custom foldout implementation
            }

            return foldout;
        }

        /// <summary>
        /// Adds spacing between elements.
        /// </summary>
        public static void AddSpacing(VisualElement parent, float spacing = 5f)
        {
            var spacer = new VisualElement();
            spacer.style.height = spacing;
            parent.Add(spacer);
        }

        /// <summary>
        /// Creates a horizontal divider.
        /// </summary>
        public static VisualElement CreateDivider()
        {
            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.backgroundColor = new StyleColor(new Color(0.5f, 0.5f, 0.5f, 0.3f));
            divider.style.marginTop = 10;
            divider.style.marginBottom = 10;
            return divider;
        }
        
        /// <summary>
        /// Creates a key detection field for keyboard input.
        /// </summary>
        public static YUCPKeyDetectionField CreateKeyDetectionField(KeyCode currentKey, Action<KeyCode> onKeyAssigned)
        {
            var field = new YUCPKeyDetectionField();
            field.Key = currentKey;
            field.SetOnKeyAssigned(onKeyAssigned);
            return field;
        }
        
        /// <summary>
        /// Creates a tracking mode selector for blendshape tracking modes.
        /// </summary>
        public static YUCPTrackingModeSelector CreateTrackingModeSelector(BlendshapeTrackingMode currentMode, Action<BlendshapeTrackingMode> onModeChanged)
        {
            var selector = new YUCPTrackingModeSelector();
            selector.Mode = currentMode;
            selector.SetOnModeChanged(onModeChanged);
            return selector;
        }
        
        /// <summary>
        /// Creates a solver mode card displaying solver information.
        /// </summary>
        public static YUCPSolverModeCard CreateSolverModeCard(SolverMode mode)
        {
            var card = new YUCPSolverModeCard();
            card.SetMode(mode);
            return card;
        }
        
        /// <summary>
        /// Creates a preset selector for optimization presets.
        /// </summary>
        public static YUCPPresetSelector CreatePresetSelector(Action<YUCPPresetSelector.Preset> onPresetSelected)
        {
            var selector = new YUCPPresetSelector();
            selector.SetOnPresetSelected(onPresetSelected);
            return selector;
        }
        
        /// <summary>
        /// Creates a blendshape list editor for managing blendshape names.
        /// </summary>
        public static YUCPBlendshapeListEditor CreateBlendshapeListEditor(Mesh targetMesh, List<string> blendshapes, Action<List<string>> onListChanged)
        {
            var editor = new YUCPBlendshapeListEditor();
            editor.TargetMesh = targetMesh;
            editor.BlendshapeList = blendshapes;
            editor.SetOnListChanged(onListChanged);
            return editor;
        }
        
        /// <summary>
        /// Creates an input mapping editor for editing input mappings.
        /// </summary>
        public static YUCPInputMappingEditor CreateInputMappingEditor(InputMapping mapping, Action onChanged, Action onRemove)
        {
            var editor = new YUCPInputMappingEditor();
            editor.Mapping = mapping;
            editor.SetOnChanged(onChanged);
            editor.SetOnRemove(onRemove);
            return editor;
        }
        
        /// <summary>
        /// Creates preview tools component for preview status and controls.
        /// </summary>
        public static YUCPPreviewTools CreatePreviewTools(YUCPPreviewTools.PreviewData data, Func<bool> validateData, Action onGenerate, Action onClear, Func<string, float> getWeight, Action<string, float> setWeight, Action onRestore, Action onZero)
        {
            var tools = new YUCPPreviewTools();
            tools.Data = data;
            tools.SetValidateData(validateData);
            tools.SetOnGenerate(onGenerate);
            tools.SetOnClear(onClear);
            tools.SetGetBlendshapeWeight(getWeight);
            tools.SetSetBlendshapeWeight(setWeight);
            tools.SetOnRestoreOriginal(onRestore);
            tools.SetOnZeroAll(onZero);
            return tools;
        }
    }
}

