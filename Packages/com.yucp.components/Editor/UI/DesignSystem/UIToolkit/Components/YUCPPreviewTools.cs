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
        
        // Extended preview data for AttachToBlendshape
        public class AttachToBlendshapePreviewData : PreviewData
        {
            public Mesh targetMesh; // Target mesh (working mesh after transfer) for blendshape categorization
            public Mesh sourceMesh; // Source body mesh for blendshape categorization
            public Mesh originalTargetMesh; // Original target mesh (before transfer) for proper categorization
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
        
        // Support for single toggle button (like AutoBodyHider)
        private bool useToggleButton = false;
        
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
                if (onClear != null)
                {
                    // Separate clear button
                    clearButton.clicked += () => onClear?.Invoke();
                }
                else
                {
                    // Toggle button - hide clear button, use generate button for both
                    clearButton.style.display = DisplayStyle.None;
                    useToggleButton = true;
                }
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
            if (generateButton == null) return;
            
            if (useToggleButton)
            {
                // Single toggle button like AutoBodyHider
                bool hasPreview = previewData != null && previewData.previewGenerated;
                bool canGenerate = validateData?.Invoke() ?? false;
                
                generateButton.SetEnabled(hasPreview || canGenerate);
                
                if (hasPreview)
                {
                    generateButton.text = "Clear Preview";
                    generateButton.RemoveFromClassList("yucp-button-primary");
                    generateButton.AddToClassList("yucp-button-danger");
                }
                else
                {
                    generateButton.text = "Generate Preview";
                    generateButton.RemoveFromClassList("yucp-button-danger");
                    generateButton.AddToClassList("yucp-button-primary");
                }
            }
            else
            {
                // Separate buttons
                bool canGenerate = validateData?.Invoke() ?? false;
                generateButton.SetEnabled(canGenerate);
                
                if (clearButton != null)
                {
                    bool canClear = previewData != null && previewData.previewGenerated;
                    clearButton.SetEnabled(canClear);
                }
            }
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
            
            // Create tabs for organizing blendshapes (manual creation, not using template with slots)
            // Load stylesheet for tabs
            var tabsStylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Layouts/YUCPTabs.uss");
            
            // Categorize blendshapes first
            var bothBlendshapes = new List<string>();
            var bodyOnlyBlendshapes = new List<string>();
            var meshOnlyBlendshapes = new List<string>();
            
            // Get target mesh and source mesh to check which blendshapes exist where
            Mesh targetMesh = null; // Working mesh (after transfer)
            Mesh sourceMesh = null;
            Mesh originalTargetMesh = null; // Original mesh (before transfer)
            if (previewData is AttachToBlendshapePreviewData attachData)
            {
                targetMesh = attachData.targetMesh;
                sourceMesh = attachData.sourceMesh;
                originalTargetMesh = attachData.originalTargetMesh;
            }
            
            // Categorize blendshapes:
            // - "Both": Blendshapes that exist on BOTH source body mesh AND target mesh (shared blendshapes)
            // - "Body": ALL blendshapes from source body mesh (regardless of whether they're on mesh)
            // - "Mesh": ALL blendshapes from target mesh (regardless of whether they're on body)
            
            // Get ALL blendshapes from source body mesh
            if (sourceMesh != null)
            {
                int sourceBlendshapeCount = sourceMesh.blendShapeCount;
                for (int i = 0; i < sourceBlendshapeCount; i++)
                {
                    string blendshapeName = sourceMesh.GetBlendShapeName(i);
                    bodyOnlyBlendshapes.Add(blendshapeName);
                }
            }
            
            // Get ALL blendshapes from target mesh
            if (targetMesh != null)
            {
                int targetBlendshapeCount = targetMesh.blendShapeCount;
                for (int i = 0; i < targetBlendshapeCount; i++)
                {
                    string blendshapeName = targetMesh.GetBlendShapeName(i);
                    meshOnlyBlendshapes.Add(blendshapeName);
                }
            }
            
            // "Both" tab: Blendshapes that exist on BOTH meshes (shared)
            // Check which blendshapes from body also exist on mesh
            foreach (string blendshape in bodyOnlyBlendshapes)
            {
                bool existsOnTarget = targetMesh != null && targetMesh.GetBlendShapeIndex(blendshape) >= 0;
                if (existsOnTarget)
                {
                    // Exists on both - add to "Both" tab
                    bothBlendshapes.Add(blendshape);
                }
            }
            
            // Debug: Log categorization results
            Debug.Log($"[YUCPPreviewTools] Blendshape categorization: Both={bothBlendshapes.Count}, BodyOnly={bodyOnlyBlendshapes.Count}, MeshOnly={meshOnlyBlendshapes.Count}, SourceMesh={sourceMesh != null}, OriginalTargetMesh={originalTargetMesh != null}, WorkingTargetMesh={targetMesh != null}");
            
            // Only create tabs if we have blendshapes to categorize
            if (tabsStylesheet != null && (bothBlendshapes.Count > 0 || bodyOnlyBlendshapes.Count > 0 || meshOnlyBlendshapes.Count > 0))
            {
                var tabsContainer = new VisualElement();
                tabsContainer.AddToClassList("yucp-tabs-container");
                tabsContainer.styleSheets.Add(tabsStylesheet);
                
                var tabsHeader = new VisualElement();
                tabsHeader.AddToClassList("yucp-tabs-header");
                tabsHeader.name = "yucp-tabs-header";
                
                var tabsContent = new VisualElement();
                tabsContent.AddToClassList("yucp-tabs-content");
                tabsContent.name = "yucp-tabs-content";
                
                tabsContainer.Add(tabsHeader);
                tabsContainer.Add(tabsContent);
                
                // Create tab buttons and content
                int selectedTab = 0;
                var tabButtons = new List<Button>();
                var tabContents = new List<VisualElement>();
                
                void CreateTab(string label, List<string> blendshapes, int index, bool isSelected)
                {
                    // Create tab, even if empty (to show all tabs exist)
                    var tabButton = new Button();
                    tabButton.text = $"{label} ({blendshapes.Count})";
                    tabButton.AddToClassList("yucp-tab");
                    if (isSelected || (index == selectedTab && blendshapes.Count > 0))
                    {
                        tabButton.AddToClassList("yucp-tab-selected");
                    }
                    
                    var tabContent = new VisualElement();
                    tabContent.style.display = (isSelected || (index == selectedTab && blendshapes.Count > 0)) ? DisplayStyle.Flex : DisplayStyle.None;
                    
                    var scrollView = new ScrollView(ScrollViewMode.Vertical);
                    scrollView.AddToClassList("yucp-sliders-scroll");
                    scrollView.mode = ScrollViewMode.Vertical;
                    // Disable horizontal scrolling by constraining width
                    scrollView.style.width = Length.Percent(100);
                    scrollView.contentContainer.style.width = Length.Percent(100);
                    scrollView.contentContainer.style.minWidth = 0;
                    scrollView.contentContainer.style.maxWidth = Length.Percent(100);
                    
                    foreach (string blendshape in blendshapes)
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
                    
                    tabContent.Add(scrollView);
                    
                    int tabIndex = index;
                    tabButton.clicked += () => {
                        // Switch tabs
                        foreach (var btn in tabButtons)
                        {
                            btn.RemoveFromClassList("yucp-tab-selected");
                        }
                        foreach (var content in tabContents)
                        {
                            content.style.display = DisplayStyle.None;
                        }
                        tabButton.AddToClassList("yucp-tab-selected");
                        tabContent.style.display = DisplayStyle.Flex;
                    };
                    
                    tabButtons.Add(tabButton);
                    tabContents.Add(tabContent);
                    tabsHeader.Add(tabButton);
                    tabsContent.Add(tabContent);
                }
                
                // Create tabs for each category:
                // - "Both": Blendshapes that exist on BOTH meshes
                // - "Body": ALL blendshapes from body mesh
                // - "Mesh": ALL blendshapes from target mesh
                int tabIndex = 0;
                int selectedTabIndex = 0;
                
                // Determine which tab should be selected by default (first non-empty tab)
                if (bothBlendshapes.Count > 0)
                {
                    selectedTabIndex = 0;
                }
                else if (bodyOnlyBlendshapes.Count > 0)
                {
                    selectedTabIndex = 1;
                }
                else if (meshOnlyBlendshapes.Count > 0)
                {
                    selectedTabIndex = 2;
                }
                
                // Create tabs only if they have blendshapes
                if (bothBlendshapes.Count > 0)
                {
                    CreateTab("Both", bothBlendshapes, tabIndex++, selectedTabIndex == 0);
                }
                if (bodyOnlyBlendshapes.Count > 0)
                {
                    CreateTab("Body", bodyOnlyBlendshapes, tabIndex++, selectedTabIndex == 1);
                }
                if (meshOnlyBlendshapes.Count > 0)
                {
                    CreateTab("Mesh", meshOnlyBlendshapes, tabIndex++, selectedTabIndex == 2);
                }
                
                // Only add tabs if we have at least one tab
                if (tabButtons.Count > 0)
                {
                    slidersContainer.Add(tabsContainer);
                    var tabNames = new List<string>();
                    foreach (var btn in tabButtons)
                    {
                        tabNames.Add(btn.text);
                    }
                    Debug.Log($"[YUCPPreviewTools] Created {tabButtons.Count} tab(s): {string.Join(", ", tabNames)}");
                }
                else
                {
                    Debug.LogWarning("[YUCPPreviewTools] No tabs created - all categories are empty!");
                    // Fallback if no tabs created
                    var scrollView = new ScrollView(ScrollViewMode.Vertical);
                    scrollView.AddToClassList("yucp-sliders-scroll");
                    scrollView.mode = ScrollViewMode.Vertical;
                    
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
                }
            }
            else
            {
                // Fallback to simple scroll view if tabs stylesheet not found
                var scrollView = new ScrollView(ScrollViewMode.Vertical);
                scrollView.AddToClassList("yucp-sliders-scroll");
                scrollView.mode = ScrollViewMode.Vertical;
                // Disable horizontal scrolling by constraining width
                scrollView.style.width = Length.Percent(100);
                scrollView.contentContainer.style.width = Length.Percent(100);
                scrollView.contentContainer.style.minWidth = 0;
                scrollView.contentContainer.style.maxWidth = Length.Percent(100);
                
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
            }
            
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
        
        public void RefreshButtons()
        {
            UpdateButtons();
        }
    }
}

