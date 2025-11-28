using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// UI Toolkit component for preset button selection.
    /// </summary>
    public class YUCPPresetSelector : VisualElement
    {
        public enum Preset
        {
            Conservative,
            Balanced,
            Aggressive,
            Custom
        }
        
        private Button conservativeButton;
        private Button balancedButton;
        private Button aggressiveButton;
        private Button customButton;
        
        private Preset? selectedPreset = null;
        private Action<Preset> onPresetSelected;
        
        public Preset? SelectedPreset
        {
            get => selectedPreset;
            set
            {
                if (selectedPreset != value)
                {
                    selectedPreset = value;
                    UpdateButtonStates();
                    if (value.HasValue)
                    {
                        onPresetSelected?.Invoke(value.Value);
                    }
                }
            }
        }
        
        public YUCPPresetSelector()
        {
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPPresetSelector.uxml");
            
            if (template != null)
            {
                template.CloneTree(this);
            }
            
            conservativeButton = this.Q<Button>("preset-conservative");
            balancedButton = this.Q<Button>("preset-balanced");
            aggressiveButton = this.Q<Button>("preset-aggressive");
            customButton = this.Q<Button>("preset-custom");
            
            if (conservativeButton != null)
            {
                conservativeButton.clicked += () => SelectedPreset = Preset.Conservative;
            }
            
            if (balancedButton != null)
            {
                balancedButton.clicked += () => SelectedPreset = Preset.Balanced;
            }
            
            if (aggressiveButton != null)
            {
                aggressiveButton.clicked += () => SelectedPreset = Preset.Aggressive;
            }
            
            if (customButton != null)
            {
                customButton.clicked += () => SelectedPreset = Preset.Custom;
            }
            
            UpdateButtonStates();
        }
        
        public void SetOnPresetSelected(Action<Preset> callback)
        {
            onPresetSelected = callback;
        }
        
        private void UpdateButtonStates()
        {
            SetButtonSelected(conservativeButton, selectedPreset == Preset.Conservative);
            SetButtonSelected(balancedButton, selectedPreset == Preset.Balanced);
            SetButtonSelected(aggressiveButton, selectedPreset == Preset.Aggressive);
            SetButtonSelected(customButton, selectedPreset == Preset.Custom);
        }
        
        private void SetButtonSelected(Button button, bool selected)
        {
            if (button == null) return;
            
            if (selected)
            {
                button.AddToClassList("selected");
            }
            else
            {
                button.RemoveFromClassList("selected");
            }
        }
    }
}


