using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// UI Toolkit component for editing an InputMapping with foldout and all fields.
    /// </summary>
    public class YUCPInputMappingEditor : VisualElement
    {
        private Toggle enabledToggle;
        private Foldout foldout;
        private Button removeButton;
        private VisualElement mappingContentContainer;
        private TextField nameField;
        private EnumField inputTypeField;
        private VisualElement inputSpecificContainer;
        private TextField parameterNameField;
        private EnumField parameterTypeField;
        private VisualElement parameterValueContainer;

        private InputMapping mapping;
        private Action onChanged;
        private Action onRemove;

        private YUCPKeyDetectionField keyDetectionField;

        public InputMapping Mapping
        {
            get => mapping;
            set
            {
                mapping = value;
                RefreshUI();
            }
        }

        public YUCPInputMappingEditor()
        {
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/YUCPInputMappingEditor.uss");
            if (stylesheet != null)
            {
                styleSheets.Add(stylesheet);
            }

            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/YUCPInputMappingEditor.uxml");

            if (template != null)
            {
                template.CloneTree(this);
            }

            enabledToggle = this.Q<Toggle>("mapping-enabled");
            foldout = this.Q<Foldout>("mapping-foldout");
            removeButton = this.Q<Button>("mapping-remove");
            mappingContentContainer = this.Q<VisualElement>("mapping-content");
            nameField = this.Q<TextField>("mapping-name");
            inputTypeField = this.Q<EnumField>("mapping-input-type");
            inputSpecificContainer = this.Q<VisualElement>("input-specific-fields");
            parameterNameField = this.Q<TextField>("mapping-parameter-name");
            parameterTypeField = this.Q<EnumField>("mapping-parameter-type");
            parameterValueContainer = this.Q<VisualElement>("parameter-value-fields");

            // Initialize enum fields with their types
            if (inputTypeField != null && inputTypeField.value == null)
            {
                inputTypeField.Init(InputType.Keyboard);
            }

            if (parameterTypeField != null && parameterTypeField.value == null)
            {
                parameterTypeField.Init(ParameterType.Bool);
            }

            if (enabledToggle != null)
            {
                enabledToggle.RegisterValueChangedCallback(evt => {
                    if (mapping != null)
                    {
                        mapping.enabled = evt.newValue;
                        UpdateFoldoutText();
                        onChanged?.Invoke();
                    }
                });
            }

            if (foldout != null)
            {
                // Start expanded by default
                foldout.value = true;

                // Set initial display state
                if (mappingContentContainer != null)
                {
                    mappingContentContainer.style.display = DisplayStyle.Flex;
                }

                foldout.RegisterValueChangedCallback(evt => {
                    if (mappingContentContainer != null)
                    {
                        mappingContentContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                });
            }

            if (removeButton != null)
            {
                removeButton.tooltip = "Remove Mapping";
                removeButton.clicked += () => onRemove?.Invoke(); // undoable in the owning inspector
            }

            if (nameField != null)
            {
                nameField.RegisterValueChangedCallback(evt => {
                    if (mapping != null)
                    {
                        mapping.mappingName = evt.newValue;
                        UpdateFoldoutText();
                        onChanged?.Invoke();
                    }
                });
            }

            if (inputTypeField != null)
            {
                inputTypeField.RegisterValueChangedCallback(evt => {
                    if (mapping != null)
                    {
                        mapping.inputType = (InputType)evt.newValue;
                        UpdateInputSpecificFields();
                        onChanged?.Invoke();
                    }
                });
            }

            if (parameterNameField != null)
            {
                parameterNameField.RegisterValueChangedCallback(evt => {
                    if (mapping != null)
                    {
                        mapping.parameterName = evt.newValue;
                        onChanged?.Invoke();
                    }
                });
            }

            if (parameterTypeField != null)
            {
                parameterTypeField.RegisterValueChangedCallback(evt => {
                    if (mapping != null)
                    {
                        mapping.parameterType = (ParameterType)evt.newValue;
                        UpdateParameterValueFields();
                        onChanged?.Invoke();
                    }
                });
            }
        }

        public YUCPInputMappingEditor(InputMapping mapping, Action onChanged, Action onRemove) : this()
        {
            this.onChanged = onChanged;
            this.onRemove = onRemove;
            Mapping = mapping;
        }

        public void SetOnChanged(Action callback)
        {
            onChanged = callback;
        }

        public void SetOnRemove(Action callback)
        {
            onRemove = callback;
        }

        private void RefreshUI()
        {
            if (mapping == null) return;

            if (enabledToggle != null)
                enabledToggle.value = mapping.enabled;

            UpdateFoldoutText();

            if (nameField != null)
                nameField.value = mapping.mappingName ?? "";

            if (inputTypeField != null)
                inputTypeField.value = mapping.inputType;

            if (parameterNameField != null)
                parameterNameField.value = mapping.parameterName ?? "";

            if (parameterTypeField != null)
                parameterTypeField.value = mapping.parameterType;

            UpdateInputSpecificFields();
            UpdateParameterValueFields();
        }

        private void UpdateFoldoutText()
        {
            if (foldout == null || mapping == null) return;

            string text = mapping.mappingName ?? "Unnamed Mapping";
            if (!mapping.enabled)
                text += " (Disabled)";

            foldout.text = text;
        }

        private void UpdateInputSpecificFields()
        {
            if (inputSpecificContainer == null || mapping == null) return;

            inputSpecificContainer.Clear();

            switch (mapping.inputType)
            {
                case InputType.Keyboard:
                    CreateKeyboardInputField();
                    break;
                case InputType.ControllerButton:
                    CreatePopup("Button", GetControllerButtonOptions(), mapping.controllerButton, v => mapping.controllerButton = v);
                    break;
                case InputType.ControllerAxis:
                    CreatePopup("Axis", GetControllerAxisOptions(), mapping.controllerAxis, v => mapping.controllerAxis = v);
                    break;
                case InputType.ControllerTrigger:
                    CreatePopup("Trigger", GetControllerTriggerOptions(), mapping.controllerTrigger, v => mapping.controllerTrigger = v);
                    break;
                case InputType.ControllerDpad:
                    CreatePopup("D-Pad", GetControllerDpadOptions(), mapping.controllerDpad, v => mapping.controllerDpad = v);
                    break;
            }
        }

        private void CreateKeyboardInputField()
        {
            keyDetectionField = new YUCPKeyDetectionField();
            keyDetectionField.Key = mapping.keyboardKey;
            keyDetectionField.SetOnKeyAssigned(keyCode => {
                mapping.keyboardKey = keyCode;
                onChanged?.Invoke();
            });
            inputSpecificContainer.Add(keyDetectionField);
        }

        private void CreatePopup(string label, List<string> options, string value, Action<string> set)
        {
            if (string.IsNullOrEmpty(value) || !options.Contains(value)) value = "None";
            var popup = new PopupField<string>(label, options, value);
            popup.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            popup.RegisterValueChangedCallback(evt => {
                set(evt.newValue);
                onChanged?.Invoke();
            });
            inputSpecificContainer.Add(popup);
        }

        private void UpdateParameterValueFields()
        {
            if (parameterValueContainer == null || mapping == null) return;

            parameterValueContainer.Clear();

            var row = parameterValueContainer;

            switch (mapping.parameterType)
            {
                case ParameterType.Bool:
                    var activeField = new FloatField("Active Value") { value = mapping.activeValue };
                    activeField.RegisterValueChangedCallback(evt => {
                        mapping.activeValue = evt.newValue;
                        onChanged?.Invoke();
                    });
                    row.Add(activeField);

                    var inactiveField = new FloatField("Inactive Value") { value = mapping.inactiveValue };
                    inactiveField.RegisterValueChangedCallback(evt => {
                        mapping.inactiveValue = evt.newValue;
                        onChanged?.Invoke();
                    });
                    row.Add(inactiveField);
                    break;

                case ParameterType.Float:
                    var minField = new FloatField("Min Value") { value = mapping.minValue };
                    minField.RegisterValueChangedCallback(evt => {
                        mapping.minValue = evt.newValue;
                        onChanged?.Invoke();
                    });
                    row.Add(minField);

                    var maxField = new FloatField("Max Value") { value = mapping.maxValue };
                    maxField.RegisterValueChangedCallback(evt => {
                        mapping.maxValue = evt.newValue;
                        onChanged?.Invoke();
                    });
                    row.Add(maxField);
                    break;

                case ParameterType.Int:
                    var activeIntField = new IntegerField("Active Value") { value = (int)mapping.activeValue };
                    activeIntField.RegisterValueChangedCallback(evt => {
                        mapping.activeValue = evt.newValue;
                        onChanged?.Invoke();
                    });
                    row.Add(activeIntField);

                    var inactiveIntField = new IntegerField("Inactive Value") { value = (int)mapping.inactiveValue };
                    inactiveIntField.RegisterValueChangedCallback(evt => {
                        mapping.inactiveValue = evt.newValue;
                        onChanged?.Invoke();
                    });
                    row.Add(inactiveIntField);
                    break;
            }
            foreach (var field in row.Children()) field.AddToClassList(BaseField<float>.alignedFieldUssClassName);
        }

        private List<string> GetControllerButtonOptions()
        {
            return new List<string> { "None", "A", "B", "X", "Y", "Start", "Select",
                "Left Shoulder", "Right Shoulder", "Left Stick", "Right Stick",
                "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right" };
        }

        private List<string> GetControllerAxisOptions()
        {
            return new List<string> { "None", "Left Stick X", "Left Stick Y", "Left Stick Angle",
                "Left Stick Rotation Direction", "Left Stick Flick",
                "Right Stick X", "Right Stick Y", "Right Stick Angle",
                "Right Stick Rotation Direction", "Right Stick Flick" };
        }

        private List<string> GetControllerTriggerOptions()
        {
            return new List<string> { "None", "Left Trigger", "Right Trigger" };
        }

        private List<string> GetControllerDpadOptions()
        {
            return new List<string> { "None", "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right" };
        }
    }
}
