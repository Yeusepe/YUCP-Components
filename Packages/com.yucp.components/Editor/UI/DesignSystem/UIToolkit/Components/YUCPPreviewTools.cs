using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// UI Toolkit component for preview status and controls.
    /// </summary>
    public class YUCPPreviewTools : VisualElement
    {
        public class PreviewData
        {
            public bool previewGenerated;
            public int clusterTriangleCount;
            public Vector3 clusterCenter;
            public List<string> blendshapes;
            public Dictionary<string, float> blendshapeWeights;
            public Dictionary<string, float> originalWeights;
        }
        
        private VisualElement statusContainer;
        private VisualElement buttonsContainer;
        private Button generateButton;
        private Button clearButton;
        private VisualElement slidersContainer;
        
        private PreviewData previewData;
        private Func<bool> validateData;
        private Action onGenerate;
        private Action onClear;
        private Func<string, float> getBlendshapeWeight;
        private Action<string, float> setBlendshapeWeight;
        private Action onRestoreOriginal;
        private Action onZeroAll;
        
        private Dictionary<string, Slider> weightSliders = new Dictionary<string, Slider>();
        
        public PreviewData Data
        {
            get => previewData;
            set
            {
                previewData = value;
                RefreshUI();
            }
        }
        
        public YUCPPreviewTools()
        {
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPPreviewTools.uxml");
            
            if (template != null)
            {
                template.CloneTree(this);
            }
            
            statusContainer = this.Q<VisualElement>("preview-status");
            buttonsContainer = this.Q<VisualElement>("preview-buttons");
            generateButton = this.Q<Button>("generate-button");
            clearButton = this.Q<Button>("clear-button");
            slidersContainer = this.Q<VisualElement>("blendshape-sliders");
            
            if (generateButton != null)
            {
                generateButton.clicked += () => onGenerate?.Invoke();
            }
            
            if (clearButton != null)
            {
                clearButton.clicked += () => onClear?.Invoke();
            }
        }
        
        public void SetValidateData(Func<bool> callback)
        {
            validateData = callback;
        }
        
        public void SetOnGenerate(Action callback)
        {
            onGenerate = callback;
        }
        
        public void SetOnClear(Action callback)
        {
            onClear = callback;
        }
        
        public void SetGetBlendshapeWeight(Func<string, float> callback)
        {
            getBlendshapeWeight = callback;
        }
        
        public void SetSetBlendshapeWeight(Action<string, float> callback)
        {
            setBlendshapeWeight = callback;
        }
        
        public void SetOnRestoreOriginal(Action callback)
        {
            onRestoreOriginal = callback;
        }
        
        public void SetOnZeroAll(Action callback)
        {
            onZeroAll = callback;
        }
        
        private void RefreshUI()
        {
            UpdateStatusCard();
            UpdateButtons();
            UpdateSliders();
        }
        
        private void UpdateStatusCard()
        {
            if (statusContainer == null) return;
            
            statusContainer.Clear();
            
            var card = new VisualElement();
            card.AddToClassList("yucp-preview-status-card");
            
            if (previewData != null && previewData.previewGenerated)
            {
                card.AddToClassList("generated");
                
                var title = new Label("Preview Generated");
                title.AddToClassList("yucp-preview-status-title");
                card.Add(title);
                
                YUCPUIToolkitHelper.AddSpacing(card, 3);
                
                var clusterLabel = new Label("Surface Cluster:");
                clusterLabel.AddToClassList("yucp-preview-status-info");
                card.Add(clusterLabel);
                
                var triangleLabel = new Label($"  - {previewData.clusterTriangleCount} triangles");
                triangleLabel.AddToClassList("yucp-preview-status-info");
                card.Add(triangleLabel);
                
                var centerLabel = new Label($"  - Center: {previewData.clusterCenter.ToString("F3")}");
                centerLabel.AddToClassList("yucp-preview-status-info");
                card.Add(centerLabel);
                
                YUCPUIToolkitHelper.AddSpacing(card, 2);
                
                var blendshapeCountLabel = new Label($"Detected Blendshapes: {previewData.blendshapes?.Count ?? 0}");
                blendshapeCountLabel.AddToClassList("yucp-preview-status-info");
                card.Add(blendshapeCountLabel);
                
                if (previewData.blendshapes != null && previewData.blendshapes.Count > 0 && previewData.blendshapes.Count <= 20)
                {
                    YUCPUIToolkitHelper.AddSpacing(card, 3);
                    
                    var scrollView = new ScrollView();
                    scrollView.AddToClassList("yucp-preview-blendshape-list");
                    scrollView.style.maxHeight = 100;
                    
                    foreach (string name in previewData.blendshapes)
                    {
                        var itemLabel = new Label($"  - {name}");
                        itemLabel.style.fontSize = 10;
                        scrollView.Add(itemLabel);
                    }
                    
                    card.Add(scrollView);
                }
                else if (previewData.blendshapes != null && previewData.blendshapes.Count > 20)
                {
                    var tooManyLabel = new Label($"  (Too many to display - {previewData.blendshapes.Count} total)");
                    tooManyLabel.AddToClassList("yucp-preview-status-info");
                    card.Add(tooManyLabel);
                }
            }
            else
            {
                card.AddToClassList("not-generated");
                
                var title = new Label("Preview Not Generated");
                title.AddToClassList("yucp-preview-status-title");
                card.Add(title);
                
                YUCPUIToolkitHelper.AddSpacing(card, 3);
                
                var clickLabel = new Label("Click 'Generate Preview' to:");
                clickLabel.AddToClassList("yucp-preview-status-info");
                card.Add(clickLabel);
                
                var detectLabel = new Label("  - Detect surface cluster");
                detectLabel.AddToClassList("yucp-preview-status-info");
                card.Add(detectLabel);
                
                var findLabel = new Label("  - Find relevant blendshapes");
                findLabel.AddToClassList("yucp-preview-status-info");
                card.Add(findLabel);
                
                var visualizeLabel = new Label("  - Visualize in Scene view");
                visualizeLabel.AddToClassList("yucp-preview-status-info");
                card.Add(visualizeLabel);
            }
            
            statusContainer.Add(card);
        }
        
        private void UpdateButtons()
        {
            if (generateButton == null || clearButton == null) return;
            
            bool canGenerate = validateData?.Invoke() ?? false;
            generateButton.SetEnabled(canGenerate);
            
            bool canClear = previewData != null && previewData.previewGenerated;
            clearButton.SetEnabled(canClear);
        }
        
        private void UpdateSliders()
        {
            if (slidersContainer == null) return;
            
            slidersContainer.Clear();
            weightSliders.Clear();
            
            if (previewData == null || !previewData.previewGenerated || 
                previewData.blendshapes == null || previewData.blendshapes.Count == 0)
            {
                slidersContainer.style.display = DisplayStyle.None;
                return;
            }
            
            slidersContainer.style.display = DisplayStyle.Flex;
            
            var title = new Label("Blendshape Preview Sliders");
            title.AddToClassList("yucp-sliders-title");
            slidersContainer.Add(title);
            
            var scrollView = new ScrollView();
            scrollView.AddToClassList("yucp-sliders-scroll");
            
            foreach (string blendshape in previewData.blendshapes)
            {
                var sliderItem = new VisualElement();
                sliderItem.AddToClassList("yucp-slider-item");
                
                float current = previewData.blendshapeWeights?.TryGetValue(blendshape, out var cachedValue) == true
                    ? cachedValue
                    : (getBlendshapeWeight?.Invoke(blendshape) ?? 0f);
                
                var slider = new Slider(blendshape, 0f, 100f) { value = current };
                slider.RegisterValueChangedCallback(evt => {
                    setBlendshapeWeight?.Invoke(blendshape, evt.newValue);
                    if (previewData.blendshapeWeights != null)
                    {
                        previewData.blendshapeWeights[blendshape] = evt.newValue;
                    }
                });
                
                weightSliders[blendshape] = slider;
                sliderItem.Add(slider);
                scrollView.Add(sliderItem);
            }
            
            slidersContainer.Add(scrollView);
            
            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("yucp-slider-buttons");
            
            var resetButton = YUCPUIToolkitHelper.CreateButton("Reset to Original", () => onRestoreOriginal?.Invoke(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            buttonsRow.Add(resetButton);
            
            var zeroButton = YUCPUIToolkitHelper.CreateButton("Zero All", () => onZeroAll?.Invoke(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            zeroButton.style.width = 120;
            buttonsRow.Add(zeroButton);
            
            slidersContainer.Add(buttonsRow);
        }
        
        public void RefreshSliders()
        {
            if (previewData == null || previewData.blendshapes == null) return;
            
            foreach (var kvp in weightSliders)
            {
                string blendshape = kvp.Key;
                Slider slider = kvp.Value;
                
                float current = previewData.blendshapeWeights?.TryGetValue(blendshape, out var cachedValue) == true
                    ? cachedValue
                    : (getBlendshapeWeight?.Invoke(blendshape) ?? 0f);
                
                slider.value = current;
            }
        }
    }
}

