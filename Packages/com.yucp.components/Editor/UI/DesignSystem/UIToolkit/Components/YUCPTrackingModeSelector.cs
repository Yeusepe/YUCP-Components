using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using YUCP.Components;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// UI Toolkit component for selecting blendshape tracking mode.
    /// </summary>
    public class YUCPTrackingModeSelector : VisualElement
    {
        private Button modeAllButton;
        private Button modeSpecificButton;
        private Button modeVisemesButton;
        private Button modeSmartButton;
        
        private BlendshapeTrackingMode currentMode;
        private Action<BlendshapeTrackingMode> onModeChanged;
        
        public BlendshapeTrackingMode Mode
        {
            get => currentMode;
            set
            {
                if (currentMode != value)
                {
                    currentMode = value;
                    UpdateButtonStates();
                    onModeChanged?.Invoke(value);
                }
            }
        }
        
        public YUCPTrackingModeSelector()
        {
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPTrackingModeSelector.uxml");
            
            if (template != null)
            {
                template.CloneTree(this);
            }
            
            modeAllButton = this.Q<Button>("mode-all");
            modeSpecificButton = this.Q<Button>("mode-specific");
            modeVisemesButton = this.Q<Button>("mode-visemes");
            modeSmartButton = this.Q<Button>("mode-smart");
            
            if (modeAllButton != null)
            {
                modeAllButton.clicked += () => Mode = BlendshapeTrackingMode.All;
            }
            
            if (modeSpecificButton != null)
            {
                modeSpecificButton.clicked += () => Mode = BlendshapeTrackingMode.Specific;
            }
            
            if (modeVisemesButton != null)
            {
                modeVisemesButton.clicked += () => Mode = BlendshapeTrackingMode.VisemsOnly;
            }
            
            if (modeSmartButton != null)
            {
                modeSmartButton.clicked += () => Mode = BlendshapeTrackingMode.Smart;
            }
            
            UpdateButtonStates();
        }
        
        public void SetOnModeChanged(Action<BlendshapeTrackingMode> callback)
        {
            onModeChanged = callback;
        }
        
        private void UpdateButtonStates()
        {
            SetButtonSelected(modeAllButton, currentMode == BlendshapeTrackingMode.All);
            SetButtonSelected(modeSpecificButton, currentMode == BlendshapeTrackingMode.Specific);
            SetButtonSelected(modeVisemesButton, currentMode == BlendshapeTrackingMode.VisemsOnly);
            SetButtonSelected(modeSmartButton, currentMode == BlendshapeTrackingMode.Smart);
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

