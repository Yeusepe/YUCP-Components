using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Package Manager window for displaying package import UI with custom metadata.
    /// Initially displays read-only metadata (banner, icon, author, description, links).
    /// Future: Will handle package downloads and updates.
    /// </summary>
    public class PackageManagerWindow : EditorWindow
    {
        [MenuItem("Tools/YUCP/Package Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<PackageManagerWindow>();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.components/Resources/Icons/YUCPIcon.png");
            if (icon == null)
            {
                // Fallback if icon doesn't exist
                window.titleContent = new GUIContent("YUCP Package Manager");
            }
            else
            {
                window.titleContent = new GUIContent("YUCP Package Manager", icon);
            }
            window.minSize = new Vector2(500, 600);
            window.Show();
        }

        // UI Elements
        private VisualElement _bannerContainer;
        private VisualElement _bannerImageContainer;
        private VisualElement _bannerGradientOverlay;
        private VisualElement _metadataSection;
        private VisualElement _contentsSection;
        private ScrollView _mainScrollView;
        private Button _detailsToggleButton;
        private VisualElement _contentWrapper;
        private Button _importButton;
        private Button _cancelButton;
        private Button _backButton;

        // State
        private PackageMetadata _currentMetadata;
        private Texture2D _bannerGradientTexture;
        private bool _detailsExpanded = false;
        private int _cachedGradientHeight = 0;
        private const string DefaultGridPlaceholderPath = "Packages/com.yucp.devtools/Resources/DefaultGrid.png";
        private PackageItemTreeView _treeView;
        private ScrollView _treeScrollView;
        private System.Array _currentImportItems; // Unity's ImportPackageItem[] array
        private string _currentPackagePath;
        private string _currentPackageIconPath;
        private object _packageImportWizardInstance; // For multi-step wizard support
        private bool _isProjectSettingsStep = false;

        private void OnEnable()
        {
            CreateGUI();
            LoadResources();
            AssetDatabase.importPackageStarted += OnImportPackageStarted;
        }

        private void OnDisable()
        {
            AssetDatabase.importPackageStarted -= OnImportPackageStarted;
            DestroyCreatedTextures();
        }

        // Override ShowButton to hide any custom buttons (like lock button)
        // Note: This doesn't hide the native close button, but prevents custom buttons from showing
        protected virtual void ShowButton(Rect rect)
        {
            // Do nothing - hide any custom buttons
        }

        private void LoadResources()
        {
            // Gradient will be created when banner is set up
            // This is called after CreateGUI() so banner exists
            if (_bannerContainer != null)
            {
                CreateBannerGradientTexture();
                if (_bannerGradientOverlay != null && _bannerGradientTexture != null)
                {
                    _bannerGradientOverlay.style.backgroundImage = new StyleBackground(_bannerGradientTexture);
                }
            }
        }

        private void OnThemeChanged()
        {
            // Recreate gradient when theme changes
            CreateBannerGradientTexture();
            if (_bannerGradientOverlay != null && _bannerGradientTexture != null)
            {
                _bannerGradientOverlay.style.backgroundImage = new StyleBackground(_bannerGradientTexture);
            }
        }

        private void DestroyCreatedTextures()
        {
            if (_bannerGradientTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(_bannerGradientTexture);
                _bannerGradientTexture = null;
            }
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            
            // Clear existing content to prevent duplicates
            root.Clear();
            
            root.style.flexDirection = FlexDirection.Column;

            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.yucp.components/Editor/PackageManager/Styles/PackageManager.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // Main scroll view
            _mainScrollView = new ScrollView();
            _mainScrollView.style.flexGrow = 1;
            root.Add(_mainScrollView);

            // Get the scroll view's content container
            var scrollContent = _mainScrollView.contentContainer;
            scrollContent.style.flexDirection = FlexDirection.Column;
            scrollContent.style.position = Position.Relative;
            scrollContent.AddToClassList("yucp-scroll-content");

            // Banner section (background layer, positioned absolutely at top)
            _bannerContainer = CreateBannerSection();
            _bannerContainer.style.position = Position.Absolute;
            _bannerContainer.style.top = 0;
            _bannerContainer.style.left = 0;
            _bannerContainer.style.right = 0;
            scrollContent.Add(_bannerContainer);
            _bannerContainer.SendToBack();

            // Spacer to push content to bottom (in scroll content, not content wrapper)
            var spacer = new VisualElement();
            spacer.AddToClassList("yucp-spacer");
            scrollContent.Add(spacer);

            // Content wrapper - at bottom (normal flow, will appear on top of banner)
            _contentWrapper = new VisualElement();
            _contentWrapper.style.flexDirection = FlexDirection.Column;
            _contentWrapper.style.flexShrink = 0;
            _contentWrapper.style.position = Position.Relative;
            _contentWrapper.AddToClassList("yucp-content-wrapper");
            scrollContent.Add(_contentWrapper);

            // Metadata section (at bottom, no background)
            _metadataSection = CreateMetadataSection();
            _contentWrapper.Add(_metadataSection);

            // Details toggle button (submenu)
            _detailsToggleButton = CreateDetailsToggleButton();
            _contentWrapper.Add(_detailsToggleButton);

            // Contents section (hidden by default, shown when details expanded)
            _contentsSection = CreateContentsSection();
            _contentsSection.style.display = DisplayStyle.None;
            _contentWrapper.Add(_contentsSection);

            // Initialize with empty metadata - will be populated when import starts or via sample
            _currentMetadata = new PackageMetadata();
            ShowSampleMetadata();

            // Update banner height when window resizes
            root.RegisterCallback<GeometryChangedEvent>(OnWindowGeometryChanged);
            
            // Ensure gradient is created and applied after layout is calculated
            root.schedule.Execute(() =>
            {
                CreateBannerGradientTexture();
                ApplyGradientToOverlay();
                UpdateBannerHeight();
            });
        }

        private void OnWindowGeometryChanged(GeometryChangedEvent evt)
        {
            // Debounce rapid resize events to prevent log spam
            if (_bannerContainer != null)
            {
                float currentHeight = _bannerContainer.resolvedStyle.height;
                float newHeight = rootVisualElement.resolvedStyle.height * 0.75f;
                
                // Only update if height changed significantly (more than 1 pixel)
                if (Mathf.Abs(currentHeight - newHeight) > 1f)
                {
                    UpdateBannerHeight();
                }
            }
        }

        private void UpdateBannerHeight()
        {
            if (_bannerContainer == null || rootVisualElement == null) return;

            // Use root visual element height or window position height
            var rootHeight = rootVisualElement.resolvedStyle.height;
            if (rootHeight <= 0)
            {
                rootHeight = position.height;
            }

            var bannerHeight = rootHeight * 0.75f; // 3/4 of window height
            _bannerContainer.style.height = bannerHeight;

            // Only regenerate gradient if height changed significantly
            int newGradientHeight = Mathf.RoundToInt(bannerHeight);
            if (_bannerGradientTexture == null || Mathf.Abs(_cachedGradientHeight - newGradientHeight) > 5)
            {
                CreateBannerGradientTexture();
                ApplyGradientToOverlay();
            }
        }

        private VisualElement CreateBannerSection()
        {
            var bannerContainer = new VisualElement();
            bannerContainer.AddToClassList("yucp-banner-container");
            _bannerContainer = bannerContainer;

            bannerContainer.style.position = Position.Relative;
            bannerContainer.style.height = Length.Percent(75);
            bannerContainer.style.marginBottom = 0;
            bannerContainer.style.width = Length.Percent(100);
            bannerContainer.style.paddingLeft = 0;
            bannerContainer.style.paddingRight = 0;
            bannerContainer.style.paddingTop = 0;
            bannerContainer.style.paddingBottom = 0;
            bannerContainer.style.flexShrink = 0;
            bannerContainer.style.overflow = Overflow.Hidden;

            // Banner image container
            _bannerImageContainer = new VisualElement();
            _bannerImageContainer.AddToClassList("yucp-banner-image-container");
            _bannerImageContainer.style.position = Position.Absolute;
            _bannerImageContainer.style.top = 0;
            _bannerImageContainer.style.left = 0;
            _bannerImageContainer.style.right = 0;
            _bannerImageContainer.style.bottom = 0;

            Texture2D displayBanner = _currentMetadata?.banner;
            if (displayBanner == null)
            {
                displayBanner = GetPlaceholderTexture();
            }
            if (displayBanner != null)
            {
                _bannerImageContainer.style.backgroundImage = new StyleBackground(displayBanner);
            }
            bannerContainer.Add(_bannerImageContainer);

            // Gradient overlay (on top, transparent to #3e3e3e)
            _bannerGradientOverlay = new VisualElement();
            _bannerGradientOverlay.AddToClassList("yucp-banner-gradient-overlay");
            _bannerGradientOverlay.style.position = Position.Absolute;
            _bannerGradientOverlay.style.top = 0;
            _bannerGradientOverlay.style.left = 0;
            _bannerGradientOverlay.style.right = 0;
            _bannerGradientOverlay.style.bottom = 0;
            _bannerGradientOverlay.pickingMode = PickingMode.Ignore;
            bannerContainer.Add(_bannerGradientOverlay);

            // Gradient will be created after layout is calculated
            // Schedule it to run after the next frame

            return bannerContainer;
        }

        private void CreateBannerGradientTexture()
        {
            if (_bannerContainer == null)
            {
                return;
            }

            // Get current banner height - try multiple methods
            float bannerHeight = _bannerContainer.resolvedStyle.height;
            
            if (bannerHeight <= 0)
            {
                // Try to get from layout
                bannerHeight = _bannerContainer.layout.height;
            }
            
            if (bannerHeight <= 0)
            {
                // Fallback to window height calculation
                if (rootVisualElement != null)
                {
                    var rootHeight = rootVisualElement.resolvedStyle.height;
                    if (rootHeight <= 0)
                    {
                        rootHeight = rootVisualElement.layout.height;
                    }
                    bannerHeight = rootHeight > 0 ? rootHeight * 0.75f : position.height * 0.75f;
                }
                else
                {
                    bannerHeight = position.height * 0.75f;
                }
            }

            // Use a wider texture for better quality when stretched
            int width = 4;
            int height = Mathf.RoundToInt(bannerHeight);
            if (height <= 0)
            {
                height = 400; // Better fallback
            }

            // Only recreate gradient if height changed significantly (more than 5 pixels)
            if (_bannerGradientTexture != null && Mathf.Abs(_cachedGradientHeight - height) <= 5)
            {
                // Height hasn't changed significantly, reuse existing texture
                return;
            }

            // Cache the height we're creating
            _cachedGradientHeight = height;

            // Destroy old texture if it exists
            if (_bannerGradientTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(_bannerGradientTexture);
            }

            _bannerGradientTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            // Gradient from transparent to #3e3e3e (0x3e = 62, so 62/255 = 0.243f)
            Color gradientEndColor = new Color(0.220f, 0.220f, 0.220f);

            // Gradient from transparent at top to fully opaque at bottom
            for (int y = 0; y < height; y++)
            {
                float t = (float)y / (height - 1); // 0 at top, 1 at bottom
                float alpha = t; // Linear interpolation from 0 (transparent) to 1 (fully opaque)
                // At bottom, alpha = 1.0 (fully opaque), at top alpha = 0.0 (transparent)
                Color color = new Color(gradientEndColor.r, gradientEndColor.g, gradientEndColor.b, alpha);

                for (int x = 0; x < width; x++)
                {
                    // Set pixel from top to bottom (y=0 is top, y=height-1 is bottom)
                    _bannerGradientTexture.SetPixel(x, height - 1 - y, color);
                }
            }

            _bannerGradientTexture.Apply();
            _bannerGradientTexture.wrapMode = TextureWrapMode.Clamp;
            
            // Apply immediately if overlay exists
            ApplyGradientToOverlay();
        }

        private void ApplyGradientToOverlay()
        {
            if (_bannerGradientOverlay == null || _bannerGradientTexture == null)
            {
                return;
            }

            _bannerGradientOverlay.style.backgroundImage = new StyleBackground(_bannerGradientTexture);
            
            // Force a repaint
            _bannerGradientOverlay.MarkDirtyRepaint();
        }

        private static Texture2D GetPlaceholderTexture()
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultGridPlaceholderPath);
        }

        private VisualElement CreateMetadataSection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-metadata-section");

            // Hero-style header with icon and name
            var headerRow = new VisualElement();
            headerRow.AddToClassList("yucp-metadata-header");
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 0;

            // Icon (read-only, no interaction)
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-metadata-icon-container");

            var iconImageContainer = new VisualElement();
            iconImageContainer.AddToClassList("yucp-metadata-icon-image-container");

            var iconImage = new Image();
            Texture2D displayIcon = _currentMetadata?.icon;
            if (displayIcon == null)
            {
                displayIcon = GetPlaceholderTexture();
            }
            iconImage.image = displayIcon;
            iconImage.AddToClassList("yucp-metadata-icon-image");
            iconImageContainer.Add(iconImage);
            iconContainer.Add(iconImageContainer);
            headerRow.Add(iconContainer);

            // Name and version in a column (read-only)
            var nameVersionColumn = new VisualElement();
            nameVersionColumn.style.flexGrow = 1;
            nameVersionColumn.style.flexShrink = 1; // Allow shrinking
            nameVersionColumn.style.marginLeft = 16;
            nameVersionColumn.style.minWidth = 0; // Allow shrinking for ellipsis
            nameVersionColumn.style.overflow = Overflow.Hidden;

            // Package Name - large, prominent (Label, not TextField) with ellipsis
            string packageName = string.IsNullOrEmpty(_currentMetadata?.packageName) ? "Untitled Package" : _currentMetadata.packageName;
            var nameLabel = new Label(packageName);
            nameLabel.AddToClassList("yucp-metadata-name-field");
            nameLabel.AddToClassList("yucp-ellipsis-text");
            nameLabel.style.fontSize = 20;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.tooltip = packageName; // Show full text on hover
            nameVersionColumn.Add(nameLabel);

            // Author (below package name, above version) with ellipsis
            if (!string.IsNullOrEmpty(_currentMetadata?.author))
            {
                var authorValueLabel = new Label(_currentMetadata.author);
                authorValueLabel.AddToClassList("yucp-ellipsis-text");
                authorValueLabel.style.marginTop = 4;
                authorValueLabel.style.fontSize = 12;
                authorValueLabel.tooltip = _currentMetadata.author; // Show full text on hover
                nameVersionColumn.Add(authorValueLabel);
            }

            // Version badge (read-only)
            var versionRow = new VisualElement();
            versionRow.style.flexDirection = FlexDirection.Row;
            versionRow.style.alignItems = Align.Center;
            versionRow.style.marginTop = 6;

            var versionLabel = new Label("Version:");
            versionLabel.style.marginRight = 6;
            versionRow.Add(versionLabel);

            var versionValueLabel = new Label(_currentMetadata?.version ?? "");
            versionValueLabel.style.marginRight = 6;
            versionRow.Add(versionValueLabel);

            nameVersionColumn.Add(versionRow);
            headerRow.Add(nameVersionColumn);

            // Action buttons on the right side
            var buttonContainer = new VisualElement();
            buttonContainer.AddToClassList("yucp-action-buttons-container");
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.alignItems = Align.Center;
            buttonContainer.style.marginLeft = 16;
            buttonContainer.style.flexShrink = 0; // Don't shrink buttons

            // Cancel button
            _cancelButton = new Button(OnCancelClicked)
            {
                text = "Cancel"
            };
            _cancelButton.AddToClassList("yucp-action-button");
            _cancelButton.AddToClassList("yucp-cancel-button");
            buttonContainer.Add(_cancelButton);

            // Back button (for multi-step wizard, shown on project settings step)
            _backButton = new Button(OnBackClicked)
            {
                text = "Back"
            };
            _backButton.AddToClassList("yucp-action-button");
            _backButton.style.display = DisplayStyle.None; // Hidden by default
            buttonContainer.Add(_backButton);

            // Import button
            _importButton = new Button(OnImportClicked)
            {
                text = "Import"
            };
            _importButton.AddToClassList("yucp-action-button");
            _importButton.AddToClassList("yucp-import-button");
            buttonContainer.Add(_importButton);

            headerRow.Add(buttonContainer);
            section.Add(headerRow);

            // Description (read-only, multiline, no title)
            if (!string.IsNullOrEmpty(_currentMetadata?.description))
            {
                var descValueLabel = new Label(_currentMetadata.description);
                descValueLabel.style.whiteSpace = WhiteSpace.Normal;
                descValueLabel.style.maxHeight = 100;
                descValueLabel.style.overflow = Overflow.Hidden;
                descValueLabel.style.marginTop = 0;
                section.Add(descValueLabel);
            }

            // Product Links (icon only, tooltip on hover)
            if (_currentMetadata?.productLinks != null && _currentMetadata.productLinks.Count > 0)
            {
                var linksContainer = new VisualElement();
                linksContainer.style.flexDirection = FlexDirection.Row;
                linksContainer.style.flexWrap = Wrap.Wrap;
                linksContainer.style.marginTop = 8;

                foreach (var link in _currentMetadata.productLinks)
                {
                    if (string.IsNullOrEmpty(link.url))
                        continue;

                    // Icon button (no visible text)
                    var linkButton = new Button(() =>
                    {
                        if (!string.IsNullOrEmpty(link.url))
                        {
                            Application.OpenURL(link.url);
                        }
                    });

                    linkButton.AddToClassList("yucp-product-link-button");
                    
                    // Set tooltip with title and URL
                    string tooltipText = string.IsNullOrEmpty(link.label) ? link.url : $"{link.label}\n{link.url}";
                    linkButton.tooltip = tooltipText;

                    // Icon
                    var linkIcon = new Image();
                    Texture2D displayLinkIcon = link.GetDisplayIcon();
                    if (displayLinkIcon == null)
                    {
                        displayLinkIcon = GetPlaceholderTexture();
                    }
                    linkIcon.image = displayLinkIcon;
                    linkIcon.style.width = 32;
                    linkIcon.style.height = 32;
                    
                    // Remove default button styling
                    linkButton.style.backgroundImage = StyleKeyword.None;
                    linkButton.style.borderTopWidth = 0;
                    linkButton.style.borderRightWidth = 0;
                    linkButton.style.borderBottomWidth = 0;
                    linkButton.style.borderLeftWidth = 0;
                    linkButton.style.paddingLeft = 0;
                    linkButton.style.paddingRight = 0;
                    linkButton.style.paddingTop = 0;
                    linkButton.style.paddingBottom = 0;
                    linkButton.style.marginRight = 8;
                    linkButton.style.marginBottom = 0;
                    linkButton.style.width = 32;
                    linkButton.style.height = 32;
                    
                    linkButton.Add(linkIcon);
                    linksContainer.Add(linkButton);
                }

                section.Add(linksContainer);
            }

            return section;
        }

        private Button CreateDetailsToggleButton()
        {
            var button = new Button(OnDetailsToggleClicked);
            button.AddToClassList("yucp-details-toggle-button");
            
            // Remove default button text
            button.text = "";
            
            // Create button content container
            var buttonContent = new VisualElement();
            buttonContent.style.flexDirection = FlexDirection.Row;
            buttonContent.style.alignItems = Align.Center;
            buttonContent.style.justifyContent = Justify.Center;
            
            // Text label
            var buttonText = new Label("Details");
            buttonText.style.marginRight = 4;
            buttonText.style.fontSize = 11;
            buttonContent.Add(buttonText);
            
            // Arrow icon (down arrow when collapsed, up when expanded)
            var arrowIcon = new Label("▼");
            arrowIcon.name = "details-arrow";
            arrowIcon.style.fontSize = 9;
            arrowIcon.style.marginLeft = 0;
            buttonContent.Add(arrowIcon);
            
            button.Add(buttonContent);
            
            return button;
        }

        private void OnDetailsToggleClicked()
        {
            _detailsExpanded = !_detailsExpanded;
            AnimateDetailsToggle();
        }

        private void AnimateDetailsToggle()
        {
            var arrowIcon = _detailsToggleButton.Q<Label>("details-arrow");
            
            if (_detailsExpanded)
            {
                // Animate arrow change
                if (arrowIcon != null)
                {
                    arrowIcon.text = "▲";
                }
                
                // First, measure the natural height by temporarily showing it with auto height
                _contentsSection.style.display = DisplayStyle.Flex;
                _contentsSection.style.maxHeight = StyleKeyword.Auto;
                _contentsSection.style.overflow = Overflow.Hidden;
                
                // Wait for layout calculation
                rootVisualElement.schedule.Execute(() =>
                {
                    // Get natural height after layout is calculated
                    float naturalHeight = _contentsSection.resolvedStyle.height;
                    if (naturalHeight <= 0)
                    {
                        naturalHeight = _contentsSection.layout.height;
                    }
                    if (naturalHeight <= 0) naturalHeight = 400; // Fallback
                    
                    // Now set to 0 and animate to natural height
                    _contentsSection.style.maxHeight = 0;
                    
                    // Wait a frame before starting animation to ensure smooth start
                    rootVisualElement.schedule.Execute(() =>
                    {
                        _contentsSection.style.maxHeight = naturalHeight;
                        
                        // After animation completes, set to auto
                        _contentsSection.schedule.Execute(() =>
                        {
                            _contentsSection.style.maxHeight = StyleKeyword.Auto;
                            _contentsSection.style.overflow = Overflow.Visible;
                        }).StartingIn(350);
                    });
                });
            }
            else
            {
                // Animate arrow change
                if (arrowIcon != null)
                {
                    arrowIcon.text = "▼";
                }
                
                // Get current height
                float currentHeight = _contentsSection.resolvedStyle.height;
                if (currentHeight <= 0)
                {
                    currentHeight = _contentsSection.layout.height;
                }
                if (currentHeight <= 0) currentHeight = 400; // Fallback
                
                // Animate collapse using max-height
                _contentsSection.style.maxHeight = currentHeight;
                _contentsSection.style.overflow = Overflow.Hidden;
                
                rootVisualElement.schedule.Execute(() =>
                {
                    _contentsSection.style.maxHeight = 0;
                    
                    // After animation, hide completely
                    _contentsSection.schedule.Execute(() =>
                    {
                        _contentsSection.style.display = DisplayStyle.None;
                        _contentsSection.style.maxHeight = StyleKeyword.Auto;
                    }).StartingIn(350);
                });
            }
        }

        private VisualElement CreateContentsSection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-section");
            section.AddToClassList("yucp-contents-section");
            section.style.marginTop = 20;
            section.style.paddingLeft = 10;
            section.style.paddingRight = 10;
            section.style.paddingTop = 10;
            section.style.paddingBottom = 10;

            var titleLabel = new Label("Package Contents");
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = 10;
            section.Add(titleLabel);

            // Tree view scroll container
            _treeScrollView = new ScrollView();
            _treeScrollView.style.minHeight = 200;
            _treeScrollView.style.maxHeight = 400;
            _treeScrollView.style.flexGrow = 1;

            // Create tree view
            _treeView = new PackageItemTreeView(_treeScrollView.contentContainer);
            section.Add(_treeScrollView);

            // Show sample tree for demonstration (will be replaced with real data when import starts)
            ShowSampleTree();

            return section;
        }

        private void BuildTreeFromImportItems()
        {
            if (_currentImportItems == null || _currentImportItems.Length == 0)
            {
                return;
            }

            var rootNode = PackageItemTreeBuilder.BuildTree(_currentImportItems);
            _treeView.SetTree(rootNode);
        }

        private void ShowSampleTree()
        {
            // Always use fallback tree for now since reflection might fail
            // In production, this will use real ImportPackageItem[] from Unity
            CreateFallbackTree();
        }

        private object[] CreateSampleImportItems()
        {
            // Create sample data structure that mimics ImportPackageItem
            // This is for demonstration only - in real use, we'll get actual ImportPackageItem objects
            var sampleData = new List<object>();
            
            var samplePaths = new[]
            {
                "Assets/YUCP/Components/Scripts/Core/ComponentBase.cs",
                "Assets/YUCP/Components/Scripts/Core/ComponentManager.cs",
                "Assets/YUCP/Components/Scripts/UI/ButtonComponent.cs",
                "Assets/YUCP/Components/Scripts/UI/InputComponent.cs",
                "Assets/YUCP/Components/Scripts/UI/PanelComponent.cs",
                "Assets/YUCP/Components/Scripts/Animation/AnimatorComponent.cs",
                "Assets/YUCP/Components/Scripts/Audio/AudioManager.cs",
                "Assets/YUCP/Components/Prefabs/UI/Button.prefab",
                "Assets/YUCP/Components/Prefabs/UI/Panel.prefab",
                "Assets/YUCP/Components/Materials/UI/ButtonMaterial.mat",
                "Assets/YUCP/Components/Textures/Icons/ButtonIcon.png",
                "Assets/YUCP/Components/Shaders/UI/ButtonShader.shader",
                "Assets/YUCP/Components/Editor/Inspectors/ComponentInspector.cs"
            };

            // Use reflection to create mock ImportPackageItem objects
            var importItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            if (importItemType != null)
            {
                try
                {
                    foreach (var path in samplePaths)
                    {
                        var item = Activator.CreateInstance(importItemType);
                        var destPathProp = importItemType.GetProperty("destinationAssetPath");
                        var isFolderProp = importItemType.GetProperty("isFolder");
                        var enabledProp = importItemType.GetProperty("enabledStatus");
                        var existsProp = importItemType.GetProperty("exists");

                        if (destPathProp != null) destPathProp.SetValue(item, path);
                        if (isFolderProp != null) isFolderProp.SetValue(item, false);
                        if (enabledProp != null) enabledProp.SetValue(item, 1); // Enabled
                        if (existsProp != null) existsProp.SetValue(item, false); // New file

                        sampleData.Add(item);
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            }
            else
            {
                return null;
            }

            return sampleData.Count > 0 ? sampleData.ToArray() : null;
        }

        private void CreateFallbackTree()
        {
            // Create a simple tree directly without using ImportPackageItem
            var root = new PackageItemNode("Assets", "Assets", true, 0);
            root.IsExpanded = true;

            // Create sample folder structure
            var componentsFolder = new PackageItemNode("YUCP", "Assets/YUCP", true, 1);
            componentsFolder.IsExpanded = true;
            componentsFolder.IsSelected = true;
            componentsFolder.SelectionState = 1;

            var scriptsFolder = new PackageItemNode("Scripts", "Assets/YUCP/Components/Scripts", true, 2);
            scriptsFolder.IsExpanded = true;
            scriptsFolder.IsSelected = true;
            scriptsFolder.SelectionState = 1;

            var coreFolder = new PackageItemNode("Core", "Assets/YUCP/Components/Scripts/Core", true, 3);
            coreFolder.IsExpanded = true;
            coreFolder.IsSelected = true;
            coreFolder.SelectionState = 1;

            coreFolder.Children.Add(new PackageItemNode("ComponentBase.cs", "Assets/YUCP/Components/Scripts/Core/ComponentBase.cs", false, 4) { IsSelected = true });
            coreFolder.Children.Add(new PackageItemNode("ComponentManager.cs", "Assets/YUCP/Components/Scripts/Core/ComponentManager.cs", false, 4) { IsSelected = true });

            var uiFolder = new PackageItemNode("UI", "Assets/YUCP/Components/Scripts/UI", true, 3);
            uiFolder.IsExpanded = true;
            uiFolder.IsSelected = true;
            uiFolder.SelectionState = 1;

            uiFolder.Children.Add(new PackageItemNode("ButtonComponent.cs", "Assets/YUCP/Components/Scripts/UI/ButtonComponent.cs", false, 4) { IsSelected = true });
            uiFolder.Children.Add(new PackageItemNode("InputComponent.cs", "Assets/YUCP/Components/Scripts/UI/InputComponent.cs", false, 4) { IsSelected = true });
            uiFolder.Children.Add(new PackageItemNode("PanelComponent.cs", "Assets/YUCP/Components/Scripts/UI/PanelComponent.cs", false, 4) { IsSelected = true });

            scriptsFolder.Children.Add(coreFolder);
            scriptsFolder.Children.Add(uiFolder);

            var prefabsFolder = new PackageItemNode("Prefabs", "Assets/YUCP/Components/Prefabs", true, 2);
            prefabsFolder.IsExpanded = true;
            prefabsFolder.IsSelected = true;
            prefabsFolder.SelectionState = 1;

            prefabsFolder.Children.Add(new PackageItemNode("Button.prefab", "Assets/YUCP/Components/Prefabs/Button.prefab", false, 3) { IsSelected = true });
            prefabsFolder.Children.Add(new PackageItemNode("Panel.prefab", "Assets/YUCP/Components/Prefabs/Panel.prefab", false, 3) { IsSelected = true });

            componentsFolder.Children.Add(scriptsFolder);
            componentsFolder.Children.Add(prefabsFolder);

            root.Children.Add(componentsFolder);

            _treeView.SetTree(root);
        }

        private int CountNodes(PackageItemNode node)
        {
            int count = 1;
            foreach (var child in node.Children)
            {
                count += CountNodes(child);
            }
            return count;
        }

        /// <summary>
        /// Initialize window for package import with metadata and import items.
        /// </summary>
        public void InitializeForImport(string packagePath, System.Array importItems, System.Array allImportItems, string packageIconPath, object wizardInstance, bool isProjectSettingsStep)
        {
            Debug.Log($"[YUCP PackageManager] InitializeForImport called");
            Debug.Log($"[YUCP PackageManager] Package path: {packagePath}");
            Debug.Log($"[YUCP PackageManager] Import items count: {importItems?.Length ?? 0}");
            Debug.Log($"[YUCP PackageManager] All import items count: {allImportItems?.Length ?? 0}");
            Debug.Log($"[YUCP PackageManager] Package icon path: {packageIconPath}");
            Debug.Log($"[YUCP PackageManager] Is project settings step: {isProjectSettingsStep}");
            
            _currentPackagePath = packagePath;
            _currentImportItems = importItems;
            _currentPackageIconPath = packageIconPath;
            _packageImportWizardInstance = wizardInstance;
            _isProjectSettingsStep = isProjectSettingsStep;

            // Set window title to match Unity's default
            titleContent = new GUIContent("Import Unity Package");

            // Update button visibility and text based on wizard state
            UpdateButtonStates();

            // Extract metadata from ALL import items (not just current step) to find icon/banner
            Debug.Log("[YUCP PackageManager] Extracting metadata...");
            var metadata = PackageMetadataExtractor.ExtractMetadataFromImportItems(allImportItems ?? importItems, packagePath);
            Debug.Log($"[YUCP PackageManager] Metadata extracted: {metadata?.packageName ?? "null"}");
            SetMetadata(metadata);

            // Build tree from current step's import items
            Debug.Log("[YUCP PackageManager] Building tree view...");
            SetImportItems(importItems);

            // Make window modal and prevent closing
            MakeWindowModal();

            // Focus window
            Focus();
            Debug.Log("[YUCP PackageManager] Window initialized successfully");
        }

        private void MakeWindowModal()
        {
            // Prevent window from being closed by user
            wantsMouseMove = true;
            
            // Show as utility window (modal) - this prevents clicking off and makes it modal
            ShowUtility();
            
            // Try to hide the close button using reflection
            try
            {
                // Get the HostView (parent container)
                var hostViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.HostView");
                if (hostViewType != null)
                {
                    var parentField = typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (parentField != null)
                    {
                        var parent = parentField.GetValue(this);
                        if (parent != null)
                        {
                            // HostView inherits from View, which has m_Window (ContainerWindow)
                            // Try to get ContainerWindow from View base class
                            var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.View");
                            if (viewType != null)
                            {
                                var windowField = viewType.GetField("m_Window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (windowField != null)
                                {
                                    var containerWindow = windowField.GetValue(parent);
                                    if (containerWindow != null)
                                    {
                                        var containerWindowType = containerWindow.GetType();
                                        
                                        // Try to modify m_ButtonCount to hide close button
                                        // On Windows, m_ButtonCount = 2 means both minimize and close buttons
                                        // Setting it to 1 would hide the close button (only show minimize)
                                        var buttonCountField = containerWindowType.GetField("m_ButtonCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        if (buttonCountField != null)
                                        {
                                            // Set button count to 1 (only minimize button, no close button)
                                            buttonCountField.SetValue(containerWindow, 1);
                                            Debug.Log("[YUCP PackageManager] Close button hidden by setting m_ButtonCount to 1");
                                        }
                                        else
                                        {
                                            Debug.LogWarning("[YUCP PackageManager] Could not find m_ButtonCount field in ContainerWindow");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Reflection failed - that's okay, ShowUtility() is enough for modal behavior
                Debug.LogWarning($"[YUCP PackageManager] Could not hide close button via reflection: {ex.Message}");
            }
            
            Debug.Log("[YUCP PackageManager] Window set to modal mode");
        }

        private void UpdateButtonStates()
        {
            if (_importButton == null || _backButton == null) return;

            bool isMultiStep = _packageImportWizardInstance != null && 
                PackageUtilityReflection.IsMultiStepWizard(_packageImportWizardInstance);
            bool isProjectStep = _packageImportWizardInstance != null && 
                PackageUtilityReflection.IsProjectSettingStep(_packageImportWizardInstance);

            // Show Back button only on project settings step of multi-step wizard
            if (isMultiStep && isProjectStep)
            {
                _backButton.style.display = DisplayStyle.Flex;
                _importButton.text = "Import";
            }
            else if (isMultiStep && !isProjectStep)
            {
                _backButton.style.display = DisplayStyle.None;
                _importButton.text = "Next";
            }
            else
            {
                _backButton.style.display = DisplayStyle.None;
                _importButton.text = "Import";
            }
        }

        private void OnBackClicked()
        {
            Debug.Log("[YUCP PackageManager] Back button clicked");
            try
            {
                if (_packageImportWizardInstance == null || _currentImportItems == null)
                {
                    Debug.LogWarning("[YUCP PackageManager] Wizard instance or import items missing");
                    try
                    {
                        Close();
                    }
                    catch (ExitGUIException)
                    {
                        // Expected
                    }
                    return;
                }

                Debug.Log("[YUCP PackageManager] Updating import item selections before going back...");
                // Update enabledStatus before going back
                UpdateImportItemSelections();

                Debug.Log("[YUCP PackageManager] Calling DoPreviousStep");
                // Call DoPreviousStep
                PackageUtilityReflection.DoPreviousStep(_packageImportWizardInstance, _currentImportItems);

                // Window will be closed and recreated by wizard, so just close this one
                Debug.Log("[YUCP PackageManager] Closing window after back");
                try
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
                catch (ExitGUIException)
                {
                    // Expected
                    Debug.Log("[YUCP PackageManager] ExitGUIException during back (expected)");
                }
            }
            catch (ExitGUIException)
            {
                // Expected
                Debug.Log("[YUCP PackageManager] ExitGUIException during back (expected)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to go back: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    Close();
                }
                catch (ExitGUIException)
                {
                    // Expected
                }
            }
        }

        /// <summary>
        /// Set the import items to display in the tree view.
        /// </summary>
        public void SetImportItems(System.Array importItems)
        {
            _currentImportItems = importItems;
            if (_treeView != null)
            {
                BuildTreeFromImportItems();
            }
        }

        private void ShowSampleMetadata()
        {
            // For now, create sample metadata to demonstrate UI
            _currentMetadata = new PackageMetadata
            {
                packageName = "Very Long Package Name That Demonstrates How The UI Handles Extensive Package Titles With Multiple Words And Potentially Very Long Names That Might Wrap Or Truncate",
                version = "1.0.0",
                author = "Very Long Author Name That Shows How The System Handles Extended Author Information Including Multiple Names, Organizations, And Additional Attribution Details",
                description = "This is a very long sample package description that demonstrates how the Package Manager window handles extensive text content. The description area should properly wrap and display long descriptions without breaking the layout. This text is intentionally verbose to test the UI's ability to handle comprehensive package information. The Package Manager window will display metadata extracted from packages when importing, and it should gracefully handle descriptions of varying lengths. This includes support for multiple paragraphs, detailed feature lists, installation instructions, usage examples, and any other relevant information that package authors might want to include in their package metadata.",
                productLinks = new List<ProductLink>
                {
                    new ProductLink("http://vpm.yucp.club/", "VPM Repository"),
                    new ProductLink("http://patreon.com/Yeusepe", "Patreon")
                }
            };

            RefreshUI();
        }

        private void RefreshUI()
        {
            if (_metadataSection != null && _metadataSection.parent != null)
            {
                var parent = _metadataSection.parent;
                int index = parent.IndexOf(_metadataSection);
                _metadataSection.RemoveFromHierarchy();
                
                var newSection = CreateMetadataSection();
                parent.Insert(index, newSection);
                _metadataSection = newSection;
            }

            if (_bannerImageContainer != null)
            {
                Texture2D displayBanner = _currentMetadata?.banner;
                if (displayBanner == null)
                {
                    displayBanner = GetPlaceholderTexture();
                }
                if (displayBanner != null)
                {
                    _bannerImageContainer.style.backgroundImage = new StyleBackground(displayBanner);
                }
            }
        }

        /// <summary>
        /// Set the metadata to display in the window.
        /// Can be called externally when metadata is extracted from a package.
        /// </summary>
        public void SetMetadata(PackageMetadata metadata)
        {
            _currentMetadata = metadata ?? new PackageMetadata();
            RefreshUI();
        }

        private void OnImportPackageStarted(string packageName)
        {
            // Try to extract metadata using reflection to access Unity's internal import items
            // Unity's PackageImport.ShowImportPackage is called with packagePath, items, and iconPath
            // We need to intercept this or extract from the package file directly
            
            // For now, create fallback metadata from package name
            // In the future, we'll extract from ImportPackageItem[] array using reflection
            _currentMetadata = new PackageMetadata(packageName);
            RefreshUI();
            
            // Focus this window to show the import UI
            Focus();
        }

        private void OnImportClicked()
        {
            Debug.Log("[YUCP PackageManager] Import button clicked");
            
            try
            {
                if (_currentImportItems == null || _currentImportItems.Length == 0)
                {
                    Debug.LogWarning("[YUCP PackageManager] No import items, closing window");
                    try
                    {
                        Close();
                    }
                    catch (ExitGUIException)
                    {
                        // Expected
                    }
                    return;
                }

                Debug.Log($"[YUCP PackageManager] Updating import item selections...");
                // Update enabledStatus in ImportPackageItem[] based on tree selections
                UpdateImportItemSelections();

                // Get package name
                string packageName = _currentMetadata?.packageName ?? Path.GetFileNameWithoutExtension(_currentPackagePath ?? "");
                Debug.Log($"[YUCP PackageManager] Package name: {packageName}");

                // Check if multi-step wizard
                bool isMultiStep = _packageImportWizardInstance != null && 
                    PackageUtilityReflection.IsMultiStepWizard(_packageImportWizardInstance);
                bool isProjectStep = _packageImportWizardInstance != null && 
                    PackageUtilityReflection.IsProjectSettingStep(_packageImportWizardInstance);

                Debug.Log($"[YUCP PackageManager] Is multi-step wizard: {isMultiStep}");
                Debug.Log($"[YUCP PackageManager] Is project settings step: {isProjectStep}");

                if (isMultiStep && !isProjectStep)
                {
                    // Not final step - call DoNextStep
                    Debug.Log("[YUCP PackageManager] Moving to next step of multi-step wizard");
                    PackageUtilityReflection.DoNextStep(_packageImportWizardInstance, _currentImportItems);
                }
                else
                {
                    // Final step - finish import
                    if (isMultiStep && isProjectStep)
                    {
                        // Multi-step wizard on final step - need to combine items
                        // The wizard will handle this in FinishImport
                        Debug.Log("[YUCP PackageManager] Finishing multi-step import");
                        PackageUtilityReflection.FinishImport(_packageImportWizardInstance);
                    }
                    else
                    {
                        // Single-step import
                        Debug.Log("[YUCP PackageManager] Performing single-step import");
                        PackageUtilityReflection.ImportPackageAssets(packageName, _currentImportItems);
                    }
                }

                Debug.Log("[YUCP PackageManager] Import completed, closing window");
                try
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
                catch (ExitGUIException)
                {
                    // Expected when closing modal windows
                    Debug.Log("[YUCP PackageManager] ExitGUIException during import (expected)");
                }
            }
            catch (ExitGUIException)
            {
                // Expected
                Debug.Log("[YUCP PackageManager] ExitGUIException during import (expected)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to import package: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("Import Failed", $"Failed to import package: {ex.Message}", "OK");
            }
        }

        private void OnCancelClicked()
        {
            Debug.Log("[YUCP PackageManager] Cancel button clicked");
            try
            {
                if (_currentImportItems == null || _currentImportItems.Length == 0)
                {
                    Debug.LogWarning("[YUCP PackageManager] No import items, closing window");
                    try
                    {
                        Close();
                    }
                    catch (ExitGUIException)
                    {
                        // Expected
                    }
                    return;
                }

                // Get package name
                string packageName = _currentMetadata?.packageName ?? Path.GetFileNameWithoutExtension(_currentPackagePath ?? "");
                Debug.Log($"[YUCP PackageManager] Cancelling import for package: {packageName}");

                if (_packageImportWizardInstance != null)
                {
                    Debug.Log("[YUCP PackageManager] Using wizard's cancel method");
                    // Use wizard's cancel method
                    PackageUtilityReflection.CancelImport(_packageImportWizardInstance);
                }
                else
                {
                    Debug.Log("[YUCP PackageManager] Using fallback cancel method");
                    // Fallback to direct cancel
                    PackageUtilityReflection.ImportPackageAssetsCancelled(packageName, _currentImportItems);
                }

                Debug.Log("[YUCP PackageManager] Closing window after cancel");
                try
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
                catch (ExitGUIException)
                {
                    // ExitGUIException is expected and normal when closing modal windows
                    Debug.Log("[YUCP PackageManager] ExitGUIException during cancel (expected)");
                }
            }
            catch (ExitGUIException)
            {
                // ExitGUIException is expected when closing modal windows
                Debug.Log("[YUCP PackageManager] ExitGUIException during cancel (expected)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to cancel import: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    Close();
                }
                catch (ExitGUIException)
                {
                    // Expected
                }
            }
        }

        private void UpdateImportItemSelections()
        {
            if (_currentImportItems == null || _treeView == null)
                return;

            try
            {
                // Get selected paths from tree view
                var selectedPaths = _treeView.GetSelectedPaths();
                var selectedSet = new HashSet<string>(selectedPaths, StringComparer.OrdinalIgnoreCase);

                // Update enabledStatus in ImportPackageItem[] via reflection
                var itemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
                if (itemType == null) return;

                var destinationPathField = itemType.GetField("destinationAssetPath");
                var enabledStatusField = itemType.GetField("enabledStatus");
                var isFolderField = itemType.GetField("isFolder");

                if (destinationPathField == null || enabledStatusField == null || isFolderField == null)
                    return;

                // First pass: update individual items
                foreach (var item in _currentImportItems)
                {
                    if (item == null) continue;

                    string destinationPath = destinationPathField.GetValue(item) as string;
                    bool isFolder = (bool)(isFolderField.GetValue(item) ?? false);

                    if (string.IsNullOrEmpty(destinationPath)) continue;

                    // Check if item is selected
                    bool isSelected = selectedSet.Contains(destinationPath);
                    enabledStatusField.SetValue(item, isSelected ? 1 : -1);
                }

                // Second pass: update folder states (mixed if children have mixed selection)
                // This is simplified - full implementation would traverse tree hierarchy
                foreach (var item in _currentImportItems)
                {
                    if (item == null) continue;

                    bool isFolder = (bool)(isFolderField.GetValue(item) ?? false);
                    if (!isFolder) continue;

                    string folderPath = destinationPathField.GetValue(item) as string;
                    if (string.IsNullOrEmpty(folderPath)) continue;

                    // Check if any children are selected
                    bool hasSelected = false;
                    bool hasUnselected = false;

                    foreach (var childItem in _currentImportItems)
                    {
                        if (childItem == null || childItem == item) continue;

                        string childPath = destinationPathField.GetValue(childItem) as string;
                        if (string.IsNullOrEmpty(childPath)) continue;

                        if (childPath.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase) ||
                            childPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase))
                        {
                            int status = (int)(enabledStatusField.GetValue(childItem) ?? -1);
                            if (status > 0) hasSelected = true;
                            if (status < 0) hasUnselected = true;
                        }
                    }

                    // Set folder state: 2=mixed, 1=all selected, -1=none selected
                    if (hasSelected && hasUnselected)
                    {
                        enabledStatusField.SetValue(item, 2); // Mixed
                    }
                    else if (hasSelected)
                    {
                        enabledStatusField.SetValue(item, 1); // All
                    }
                    else
                    {
                        enabledStatusField.SetValue(item, -1); // None
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to update import item selections: {ex.Message}");
            }
        }
    }
}

