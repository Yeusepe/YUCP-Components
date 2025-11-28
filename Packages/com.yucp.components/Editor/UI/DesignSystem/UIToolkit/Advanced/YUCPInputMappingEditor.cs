using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.UI.DesignSystem.Utilities
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
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Advanced/YUCPInputMappingEditor.uss");
            if (stylesheet != null)
            {
                styleSheets.Add(stylesheet);
            }
            
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Advanced/YUCPInputMappingEditor.uxml");
            
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
                
                // Set initial display state based on foldout value
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
                removeButton.clicked += () => {
                    if (EditorUtility.DisplayDialog("Remove Mapping", 
                        $"Remove mapping '{mapping?.mappingName}'?", "Yes", "Cancel"))
                    {
                        onRemove?.Invoke();
                    }
                };
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
            
            var sectionLabel = new Label("Input Configuration");
            sectionLabel.style.fontSize = 13;
            sectionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            sectionLabel.style.marginBottom = 3;
            inputSpecificContainer.Add(sectionLabel);
            
            switch (mapping.inputType)
            {
                case InputType.Keyboard:
                    CreateKeyboardInputField();
                    break;
                case InputType.ControllerButton:
                    CreateControllerButtonField();
                    break;
                case InputType.ControllerAxis:
                    CreateControllerAxisField();
                    break;
                case InputType.ControllerTrigger:
                    CreateControllerTriggerField();
                    break;
                case InputType.ControllerDpad:
                    CreateControllerDpadField();
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
        
        private void CreateControllerButtonField()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 5;
            
            var label = new Label("Button");
            label.style.width = 100;
            label.style.fontSize = 11;
            row.Add(label);
            
            var options = GetControllerButtonOptions();
            string currentValue = string.IsNullOrEmpty(mapping.controllerButton) ? "None" : mapping.controllerButton;
            if (!options.Contains(currentValue))
            {
                currentValue = "None";
                mapping.controllerButton = "None";
            }
            var popup = new PopupField<string>(options, currentValue);
            popup.style.flexGrow = 1;
            popup.RegisterValueChangedCallback(evt => {
                mapping.controllerButton = evt.newValue;
                onChanged?.Invoke();
            });
            row.Add(popup);
            inputSpecificContainer.Add(row);
        }
        
        private void CreateControllerAxisField()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 5;
            
            var label = new Label("Axis");
            label.style.width = 100;
            label.style.fontSize = 11;
            row.Add(label);
            
            var options = GetControllerAxisOptions();
            string currentValue = string.IsNullOrEmpty(mapping.controllerAxis) ? "None" : mapping.controllerAxis;
            if (!options.Contains(currentValue))
            {
                currentValue = "None";
                mapping.controllerAxis = "None";
            }
            var popup = new PopupField<string>(options, currentValue);
            popup.style.flexGrow = 1;
            popup.RegisterValueChangedCallback(evt => {
                mapping.controllerAxis = evt.newValue;
                onChanged?.Invoke();
            });
            row.Add(popup);
            inputSpecificContainer.Add(row);
        }
        
        private void CreateControllerTriggerField()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 5;
            
            var label = new Label("Trigger");
            label.style.width = 100;
            label.style.fontSize = 11;
            row.Add(label);
            
            var options = GetControllerTriggerOptions();
            string currentValue = string.IsNullOrEmpty(mapping.controllerTrigger) ? "None" : mapping.controllerTrigger;
            if (!options.Contains(currentValue))
            {
                currentValue = "None";
                mapping.controllerTrigger = "None";
            }
            var popup = new PopupField<string>(options, currentValue);
            popup.style.flexGrow = 1;
            popup.RegisterValueChangedCallback(evt => {
                mapping.controllerTrigger = evt.newValue;
                onChanged?.Invoke();
            });
            row.Add(popup);
            inputSpecificContainer.Add(row);
        }
        
        private void CreateControllerDpadField()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 5;
            
            var label = new Label("D-pad");
            label.style.width = 100;
            label.style.fontSize = 11;
            row.Add(label);
            
            var options = GetControllerDpadOptions();
            string currentValue = string.IsNullOrEmpty(mapping.controllerDpad) ? "None" : mapping.controllerDpad;
            if (!options.Contains(currentValue))
            {
                currentValue = "None";
                mapping.controllerDpad = "None";
            }
            var popup = new PopupField<string>(options, currentValue);
            popup.style.flexGrow = 1;
            popup.RegisterValueChangedCallback(evt => {
                mapping.controllerDpad = evt.newValue;
                onChanged?.Invoke();
            });
            row.Add(popup);
            inputSpecificContainer.Add(row);
        }
        
        private void UpdateParameterValueFields()
        {
            if (parameterValueContainer == null || mapping == null) return;
            
            parameterValueContainer.Clear();
            
            var row = new VisualElement();
            row.AddToClassList("yucp-parameter-value-row");
            
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
            
            parameterValueContainer.Add(row);
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
                "Right Stick X", "Right Stick Y", "Right Stick Angle" };
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

