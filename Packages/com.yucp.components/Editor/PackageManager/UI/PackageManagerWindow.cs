#define YUCP_PACKAGE_MANAGER_DISABLED
#if !YUCP_PACKAGE_MANAGER_DISABLED
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.Editor.PackageVerifier;
using YUCP.Components.Editor.PackageVerifier.Core;
using YUCP.Components.Editor.PackageVerifier.Data;
using YUCP.UI.DesignSystem.Utilities;
using PackageVerifierCore = YUCP.Components.Editor.PackageVerifier.Core;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Package Manager window for displaying package import UI with custom metadata.
    /// Initially displays read-only metadata (banner, icon, author, description, links).
    /// Future: Will handle package downloads and updates.
    /// </summary>
    public class PackageManagerWindow : EditorWindow
    {
        private enum ViewMode
        {
            InstalledPackages,
            PackageDetails,
            Installer
        }

        [MenuItem("Tools/YUCP/Package Manager")]
        public static void ShowWindow()
        {
            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                Debug.LogWarning("[YUCP PackageManager] Package Manager is disabled (Tools > YUCP > Package Manager > Enable).");
                return;
            }

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
            
            // Ensure view is shown after window is displayed
            EditorApplication.delayCall += () =>
            {
                if (window != null && !window._isImportMode)
                {
                    window.ShowInstalledPackagesView();
                }
            };
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
        private System.Array _currentImportItems; // Unity's ImportPackageItem[] array (current step)
        private System.Array _allImportItems; // Unity's ImportPackageItem[] array (all items in package)
        private string _currentPackagePath;
        private string _currentPackageIconPath;
        private object _packageImportWizardInstance; // For multi-step wizard support
        private bool _isProjectSettingsStep = false;
        
        // Verification state
        private PackageVerifierCore.VerificationResult _verificationResult;
        private VisualElement _verificationStatusElement;
        private bool _isPackageSigned = false; // Track if package has signing data (even if invalid)
        
        // Domain reload prevention
        private bool _isImportMode = false; // Track if window is in import mode (prevents domain reload)
        
        // Fixed modal implementation state
        private bool _isModalFixed = false;
        private VisualElement _lastHoveredElement = null;
        private VisualElement _currentTooltipElement = null;
        
        // View mode management
        private ViewMode _currentViewMode = ViewMode.InstalledPackages;
        private InstalledPackagesView _installedPackagesView;
        private PackageDetailsView _packageDetailsView;
        private InstalledPackageInfo _currentPackageInfo;
        private VisualElement _currentViewContainer;

        // Import completion tracking
        private bool _waitingForImportCompletion = false;
        private string _pendingPackageName;
        
        private void OnEnable()
        {
            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                Debug.LogWarning("[YUCP PackageManager] Package Manager is disabled; closing window.");
                EditorApplication.delayCall += Close;
                return;
            }

            // Initialize update checker
            EditorApplication.update += PackageUpdater.Update;
            
            CreateGUI();
            LoadResources();
            AssetDatabase.importPackageStarted += OnImportPackageStarted;
            AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
            
            // Set minimum window size
            minSize = new Vector2(500, 600);
            
            // Ensure TrustedAuthority is initialized with all keys (root, cached, etc.)
            TrustedAuthority.ReloadAllKeys();
            
            // Show default view if not in import mode
            // Use delayCall to ensure GUI is fully initialized
            if (!_isImportMode)
            {
                EditorApplication.delayCall += () =>
                {
                    if (!_isImportMode && _currentViewContainer != null)
                    {
                        ShowInstalledPackagesView();
                    }
                };
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= PackageUpdater.Update;
            AssetDatabase.importPackageStarted -= OnImportPackageStarted;
            AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
            DestroyCreatedTextures();
            
            // Clean up modal event handlers
            if (_isModalFixed && rootVisualElement != null)
            {
                // Hide any active tooltip before cleanup
                HideActiveTooltip();
                
                rootVisualElement.UnregisterCallback<MouseLeaveEvent>(OnRootMouseLeave);
                rootVisualElement.UnregisterCallback<MouseMoveEvent>(OnRootMouseMove);
                rootVisualElement.UnregisterCallback<MouseEnterEvent>(OnRootMouseEnter);
                rootVisualElement.UnregisterCallback<TooltipEvent>(OnTooltipEvent);
            }
            
            // Reset cursor state
            ResetCursor();
            
            // Unlock assembly reload if we were in import mode
            if (_isImportMode)
            {
                EditorApplication.UnlockReloadAssemblies();
                _isImportMode = false;
                Debug.Log("[YUCP PackageManager] Unlocked assembly reload (window closed)");
            }
            
            _isModalFixed = false;
            _lastHoveredElement = null;
            _currentTooltipElement = null;
        }

        protected virtual void ShowButton(Rect rect)
        {
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

            // Load design system styles
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            
            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.yucp.components/Editor/PackageManager/Styles/PackageManager.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // Container for views (InstalledPackages, Details, or Installer)
            _currentViewContainer = new VisualElement();
            _currentViewContainer.style.flexGrow = 1;
            _currentViewContainer.style.flexShrink = 1;
            _currentViewContainer.style.minHeight = 0;
            root.Add(_currentViewContainer);

            // Main scroll view (for installer view only) - create but don't add to view container yet
            _mainScrollView = new ScrollView();
            _mainScrollView.style.flexGrow = 1;
            _mainScrollView.style.display = DisplayStyle.None; // Hidden by default, shown only in installer mode

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
            
            // Only show sample metadata if in installer mode (InitializeForImport will handle this)
            // Otherwise, views will be shown by ShowInstalledPackagesView or ShowPackageDetailsView

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

            if (_bannerGradientTexture != null && Mathf.Abs(_cachedGradientHeight - height) <= 5)
            {
                return;
            }

            // Cache the height we're creating
            _cachedGradientHeight = height;

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
            if (_bannerGradientOverlay == null)
            {
                return;
            }

            // When details are expanded, fully grey-out the banner background.
            // When collapsed, restore the transparent->grey gradient.
            Color solidGrey = new Color(0.220f, 0.220f, 0.220f, 1f);

            if (_detailsExpanded)
            {
                _bannerGradientOverlay.style.backgroundImage = StyleKeyword.None;
                _bannerGradientOverlay.style.backgroundColor = solidGrey;
            }
            else
            {
                _bannerGradientOverlay.style.backgroundColor = new Color(0, 0, 0, 0);
                if (_bannerGradientTexture != null)
                {
                    _bannerGradientOverlay.style.backgroundImage = new StyleBackground(_bannerGradientTexture);
                }
            }

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

            // Package Name row with verification icon
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems = Align.Center;
            nameRow.style.flexShrink = 1;
            nameRow.style.minWidth = 0;
            
            // Package Name - large, prominent (Label, not TextField) with ellipsis
            string packageName = string.IsNullOrEmpty(_currentMetadata?.packageName) ? "Untitled Package" : _currentMetadata.packageName;
            var nameLabel = new Label(packageName);
            nameLabel.AddToClassList("yucp-metadata-name-field");
            nameLabel.AddToClassList("yucp-ellipsis-text");
            nameLabel.style.fontSize = 20;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.tooltip = packageName; // Show full text on hover
            nameLabel.style.flexShrink = 1;
            nameLabel.style.minWidth = 0;
            nameRow.Add(nameLabel);
            
            // Verification icon (beside package name)
            var verificationIcon = CreateVerificationIcon();
            if (verificationIcon != null)
            {
                verificationIcon.style.flexShrink = 0;
                nameRow.Add(verificationIcon);
            }
            
            // Spacer to push icon next to name (allows row to expand for ellipsis while keeping icon close)
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            spacer.style.flexShrink = 0;
            nameRow.Add(spacer);
            
            nameVersionColumn.Add(nameRow);

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
            buttonContainer.style.flexShrink = 0;

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

            // Verification Status
            _verificationStatusElement = CreateVerificationStatusElement();
            section.Add(_verificationStatusElement);

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

        private void VerifyPackage(string packagePath)
        {
            if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            {
                _verificationResult = null;
                _isPackageSigned = false;
                return;
            }

            try
            {

                // Reload all keys (root, cached, etc.) in case they were updated
                TrustedAuthority.ReloadAllKeys();

                // Extract manifest and signature
                // Pass ImportPackageItem array if available (during import) - this avoids needing SharpZipLib
                // Use _allImportItems to ensure we check all package contents, not just current step
                var extractionResult = PackageVerifierCore.ManifestExtractor.ExtractSigningData(packagePath, _allImportItems);

                // Check if package has signing data (manifest or signature found)
                _isPackageSigned = extractionResult.manifest != null && extractionResult.signature != null;

                if (!extractionResult.success || !_isPackageSigned)
                {
                    // Package not signed - this is OK, just not verified
                    // This is expected if the package was exported without signing or signing failed during export
                    _verificationResult = new PackageVerifierCore.VerificationResult
                    {
                        valid = false,
                        errors = { extractionResult.error ?? "Package is not signed. This package was exported without a signature." }
                    };
                    return;
                }


                // Verify package
                _verificationResult = PackageVerifierCore.PackageVerifier.VerifyPackage(
                    packagePath,
                    extractionResult.manifest,
                    extractionResult.signature
                );

                if (_verificationResult.valid)
                {
                }
                else
                {
                    Debug.LogWarning($"[YUCP PackageManager] Package verification failed: {string.Join(", ", _verificationResult.errors)}");
                }
            }
            catch (Exception ex)
            {
                _isPackageSigned = false;
                _verificationResult = new PackageVerifierCore.VerificationResult
                {
                    valid = false,
                    errors = { $"Verification error: {ex.Message}" }
                };
                Debug.LogError($"[YUCP PackageManager] Verification exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private bool IsPackagePlus()
        {
            // Check if package is a Package+ (has YUCP manifest)
            // Package+ packages have YUCP_PackageInfo.json in their import items
            if (_allImportItems == null || _allImportItems.Length == 0)
            {
                return false;
            }

            var itemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            if (itemType == null) return false;

            var destinationPathField = itemType.GetField("destinationAssetPath");
            if (destinationPathField == null) return false;

            foreach (var item in _allImportItems)
            {
                if (item == null) continue;
                string destinationPath = destinationPathField.GetValue(item) as string;
                if (destinationPath != null && destinationPath.Equals("Assets/YUCP_PackageInfo.json", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private VisualElement CreateVerificationIcon()
        {
            // Only show icon if package is a Package+ (has manifest)
            if (!IsPackagePlus())
            {
                return null; // Not a Package+, no verification icon
            }

            // Only show icon if package is signed
            if (!_isPackageSigned || _verificationResult == null)
            {
                return null; // Not signed, no icon
            }

            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-verification-icon");
            iconContainer.style.marginLeft = 8; // Add spacing between name and icon

            if (_verificationResult.valid)
            {
                // Package is signed and verified - show Verified.png
                var verifiedIcon = new Image();
                string verifiedIconPath = "Packages/com.yucp.components/Editor/PackageManager/Resources/Verified.png";
                Texture2D verifiedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(verifiedIconPath);
                
                // Build comprehensive tooltip
                string tooltipText = "✓ Package Verified\n\n";
                tooltipText += "This package has been cryptographically signed and verified by YUCP.\n\n";
                
                if (!string.IsNullOrEmpty(_verificationResult.publisherId))
                {
                    tooltipText += $"Publisher: {_verificationResult.publisherId}\n";
                    tooltipText += "(Extracted from verified certificate chain)\n";
                }
                
                tooltipText += "\nCertificate Chain Validation:\n";
                tooltipText += "• Root CA certificate verified (trusted authority)\n";
                tooltipText += "• Certificate chain validated (Root → Intermediate → Publisher)\n";
                tooltipText += "• Publisher certificate signature verified\n";
                tooltipText += "• Manifest signature verified with publisher certificate\n";
                tooltipText += "• Certificate validity dates checked\n\n";
                
                tooltipText += "Additional Security:\n";
                tooltipText += "• Package content hash verified (integrity check)\n";
                tooltipText += "• All signatures validated with Ed25519 cryptography\n\n";
                tooltipText += "The package's complete certificate chain, signatures, and content hash have all been validated.";
                
                if (verifiedTexture != null)
                {
                    verifiedIcon.image = verifiedTexture;
                    verifiedIcon.style.width = 20;
                    verifiedIcon.style.height = 20;
                    verifiedIcon.tooltip = tooltipText;
                    iconContainer.Add(verifiedIcon);
                }
                else
                {
                    // Fallback to checkmark if icon not found
                    var checkLabel = new Label("✓");
                    checkLabel.style.fontSize = 16;
                    checkLabel.style.color = new Color(0.2f, 0.8f, 0.4f);
                    checkLabel.tooltip = tooltipText;
                    iconContainer.Add(checkLabel);
                }
            }
            else
            {
                // Package is signed but doesn't match - show warning
                var warningIcon = new Label("WARNING");
                warningIcon.style.fontSize = 16;
                warningIcon.style.color = new Color(0.8f, 0.6f, 0.2f);
                
                // Build comprehensive tooltip with error details
                string tooltipText = "WARNING: Verification Failed\n\n";
                tooltipText += "This package is signed, but verification failed. The package may have been tampered with, the certificate chain is invalid, or the signature verification failed.\n\n";
                
                if (_verificationResult.errors != null && _verificationResult.errors.Count > 0)
                {
                    tooltipText += "Error Details:\n";
                    foreach (var error in _verificationResult.errors)
                    {
                        tooltipText += $"• {error}\n";
                    }
                    tooltipText += "\n";
                    
                    // Check for certificate chain specific errors
                    bool hasChainError = _verificationResult.errors.Any(e => 
                        e.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                        e.Contains("chain", StringComparison.OrdinalIgnoreCase) ||
                        e.Contains("root", StringComparison.OrdinalIgnoreCase));
                    
                    if (hasChainError)
                    {
                        tooltipText += "Certificate Chain Issues:\n";
                        tooltipText += "• Root CA may not be trusted\n";
                        tooltipText += "• Certificate chain may be incomplete or malformed\n";
                        tooltipText += "• Certificate signatures may be invalid\n";
                        tooltipText += "• Certificates may have expired\n\n";
                    }
                }
                
                tooltipText += "Warning:\n";
                tooltipText += "• Do not import if you did not expect this error\n";
                tooltipText += "• The package may have been modified or corrupted\n";
                tooltipText += "• The certificate chain may be invalid or untrusted\n";
                tooltipText += "• Contact the publisher if you believe this is an error";
                
                warningIcon.tooltip = tooltipText;
                
                iconContainer.Add(warningIcon);
            }

            return iconContainer;
        }

        private VisualElement CreateVerificationStatusElement()
        {
            var container = new VisualElement();
            container.style.marginTop = 12;
            container.style.marginBottom = 8;

            // Only show verification status for Package+ packages
            if (!IsPackagePlus())
            {
                return container; // Not a Package+, no verification status
            }

            if (_verificationResult == null)
            {
                // No verification attempted yet
                return container;
            }

            if (_verificationResult.valid)
            {
                return container;
            }
            else
            {
                // Only show warning for signed packages that have been modified (verification failed)
                // If package has metadata but is not signed, don't show a warning
                if (!_isPackageSigned)
                {
                    return container; // Not signed, no warning
                }

                // Package is signed but verification failed - this means it was modified
                var warningContainer = new VisualElement();
                warningContainer.style.flexDirection = FlexDirection.Column;
                warningContainer.style.paddingLeft = 12;
                warningContainer.style.paddingRight = 12;
                warningContainer.style.paddingTop = 8;
                warningContainer.style.paddingBottom = 8;
                warningContainer.style.backgroundColor = new Color(0.6f, 0.5f, 0.2f, 0.2f);
                warningContainer.style.borderLeftWidth = 3;
                warningContainer.style.borderLeftColor = new Color(0.8f, 0.6f, 0.2f);
                warningContainer.style.borderTopLeftRadius = 4;
                warningContainer.style.borderTopRightRadius = 4;
                warningContainer.style.borderBottomLeftRadius = 4;
                warningContainer.style.borderBottomRightRadius = 4;

                var warningRow = new VisualElement();
                warningRow.style.flexDirection = FlexDirection.Row;
                warningRow.style.alignItems = Align.Center;

                var warningIcon = new Label("WARNING");
                warningIcon.style.fontSize = 14;
                warningIcon.style.color = new Color(0.8f, 0.6f, 0.2f);
                warningIcon.style.marginRight = 8;
                warningRow.Add(warningIcon);

                var statusText = new Label("Package Not Verified");
                statusText.style.fontSize = 12;
                statusText.style.unityFontStyleAndWeight = FontStyle.Bold;
                statusText.style.color = new Color(1f, 0.9f, 0.7f);
                warningRow.Add(statusText);

                warningContainer.Add(warningRow);

                // Show error details if available
                if (_verificationResult.errors != null && _verificationResult.errors.Count > 0)
                {
                    var errorText = new Label(_verificationResult.errors[0]);
                    errorText.style.fontSize = 11;
                    errorText.style.color = new Color(0.9f, 0.8f, 0.6f);
                    errorText.style.marginTop = 4;
                    errorText.style.whiteSpace = WhiteSpace.Normal;
                    warningContainer.Add(errorText);
                }

                var noteText = new Label("You can still import this package, but it may be unsafe.");
                noteText.style.fontSize = 10;
                noteText.style.color = new Color(0.8f, 0.7f, 0.5f);
                noteText.style.marginTop = 4;
                noteText.style.whiteSpace = WhiteSpace.Normal;
                warningContainer.Add(noteText);

                container.Add(warningContainer);
            }

            return container;
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
            buttonText.name = "details-text";
            buttonText.style.marginRight = 4;
            buttonText.style.fontSize = 11;
            buttonContent.Add(buttonText);
            
            // Dependencies indicator (if any)
            var depsIndicator = new Label();
            depsIndicator.name = "dependencies-indicator";
            depsIndicator.style.display = DisplayStyle.None;
            depsIndicator.style.marginRight = 4;
            depsIndicator.style.fontSize = 10;
            depsIndicator.style.color = new Color(0.6f, 0.8f, 1f);
            buttonContent.Add(depsIndicator);
            
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

            // Update banner overlay immediately based on expand/collapse state
            ApplyGradientToOverlay();
            
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

            // Dependencies section container (will be populated/refreshed when metadata is available)
            var dependenciesContainer = new VisualElement();
            dependenciesContainer.name = "dependencies-container";
            section.Add(dependenciesContainer);

            var titleLabel = new Label("Package Contents");
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = 10;
            titleLabel.style.marginTop = 0;
            section.Add(titleLabel);

            // Overwrite/keep existing quick actions
            var overwriteActionsRow = new VisualElement();
            overwriteActionsRow.style.flexDirection = FlexDirection.Row;
            overwriteActionsRow.style.justifyContent = Justify.FlexEnd;
            overwriteActionsRow.style.alignItems = Align.Center;
            overwriteActionsRow.style.marginBottom = 8;

            var overwriteExistingButton = new Button(() => _treeView?.SetOverwriteExisting(true))
            {
                text = "Overwrite Existing"
            };
            overwriteExistingButton.tooltip = "Select all items that already exist in the project (they will be overwritten on import).";
            overwriteExistingButton.style.marginRight = 6;
            overwriteExistingButton.style.minHeight = 22;
            overwriteExistingButton.style.paddingLeft = 10;
            overwriteExistingButton.style.paddingRight = 10;
            overwriteExistingButton.style.fontSize = 11;

            var keepExistingButton = new Button(() => _treeView?.SetOverwriteExisting(false))
            {
                text = "Keep Existing"
            };
            keepExistingButton.tooltip = "Deselect all items that already exist in the project (existing files will be kept).";
            keepExistingButton.style.minHeight = 22;
            keepExistingButton.style.paddingLeft = 10;
            keepExistingButton.style.paddingRight = 10;
            keepExistingButton.style.fontSize = 11;

            overwriteActionsRow.Add(overwriteExistingButton);
            overwriteActionsRow.Add(keepExistingButton);
            section.Add(overwriteActionsRow);

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

        private void RefreshDependenciesSection()
        {
            if (_contentsSection == null) return;

            var dependenciesContainer = _contentsSection.Q<VisualElement>("dependencies-container");
            if (dependenciesContainer == null) return;

            // Clear existing dependencies
            dependenciesContainer.Clear();

            // Add new dependencies section if metadata has dependencies
            var dependenciesSection = CreateDependenciesSection();
            if (dependenciesSection != null)
            {
                dependenciesContainer.Add(dependenciesSection);
                
                // Update Package Contents title margin
                var titleLabel = _contentsSection.Q<Label>();
                if (titleLabel != null)
                {
                    titleLabel.style.marginTop = 20;
                }
            }
            else
            {
                // No dependencies, remove margin from Package Contents title
                var titleLabel = _contentsSection.Q<Label>();
                if (titleLabel != null)
                {
                    titleLabel.style.marginTop = 0;
                }
            }

            // Update details button indicator
            UpdateDetailsButtonDependenciesIndicator();
        }

        private void UpdateDetailsButtonDependenciesIndicator()
        {
            if (_detailsToggleButton == null) return;

            var indicator = _detailsToggleButton.Q<Label>("dependencies-indicator");
            if (indicator == null) return;

            if (_currentMetadata != null && _currentMetadata.dependencies != null && _currentMetadata.dependencies.Count > 0)
            {
                indicator.text = $"({_currentMetadata.dependencies.Count} required package{(_currentMetadata.dependencies.Count > 1 ? "s" : "")})";
                indicator.style.display = DisplayStyle.Flex;
            }
            else
            {
                indicator.style.display = DisplayStyle.None;
            }
        }

        private VisualElement CreateDependenciesSection()
        {
            if (_currentMetadata == null || _currentMetadata.dependencies == null || _currentMetadata.dependencies.Count == 0)
            {
                return null;
            }

            var container = new VisualElement();
            container.style.marginBottom = 20;

            // Title
            var titleLabel = new Label("Required Packages");
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = 12;
            container.Add(titleLabel);

            // Info text
            var infoText = new Label("The following packages will be automatically installed:");
            infoText.style.fontSize = 11;
            infoText.style.color = new Color(0.7f, 0.7f, 0.7f);
            infoText.style.marginBottom = 10;
            infoText.style.whiteSpace = WhiteSpace.Normal;
            container.Add(infoText);

            // Dependencies list
            var dependenciesList = new VisualElement();
            dependenciesList.style.flexDirection = FlexDirection.Column;

            foreach (var dependency in _currentMetadata.dependencies)
            {
                var depItem = new VisualElement();
                depItem.style.flexDirection = FlexDirection.Row;
                depItem.style.alignItems = Align.Center;
                depItem.style.paddingLeft = 12;
                depItem.style.paddingRight = 12;
                depItem.style.paddingTop = 8;
                depItem.style.paddingBottom = 8;
                depItem.style.marginBottom = 6;
                depItem.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
                depItem.style.borderTopLeftRadius = 4;
                depItem.style.borderTopRightRadius = 4;
                depItem.style.borderBottomLeftRadius = 4;
                depItem.style.borderBottomRightRadius = 4;

                // Package icon - use Unity's built-in Package Manager icon
                // Try both light and dark theme variants
                Texture2D packageIcon = null;
                string[] iconNames = { 
                    "Package Manager",           // Light theme
                    "d_Package Manager",         // Dark theme
                    "Installed",                 // Alternative: installed package icon
                    "d_Installed",               // Dark theme installed icon
                    "DefaultAsset Icon"          // Fallback - always available
                };
                
                foreach (string iconName in iconNames)
                {
                    var iconContent = EditorGUIUtility.IconContent(iconName);
                    if (iconContent != null && iconContent.image != null)
                    {
                        packageIcon = iconContent.image as Texture2D;
                        if (packageIcon != null) break;
                    }
                }
                
                if (packageIcon != null)
                {
                    var iconImage = new Image { image = packageIcon };
                    iconImage.style.width = 16;
                    iconImage.style.height = 16;
                    iconImage.style.marginRight = 10;
                    depItem.Add(iconImage);
                }
                else
                {
                    // Fallback if no icon found - use a simple bullet point
                    var iconLabel = new Label("•");
                    iconLabel.style.fontSize = 12;
                    iconLabel.style.marginRight = 10;
                    iconLabel.style.color = new Color(0.6f, 0.8f, 1f);
                    depItem.Add(iconLabel);
                }

                // Package name
                var nameLabel = new Label(dependency.Key);
                nameLabel.style.fontSize = 12;
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLabel.style.flexGrow = 1;
                depItem.Add(nameLabel);

                // Version
                var versionLabel = new Label($"v{dependency.Value}");
                versionLabel.style.fontSize = 11;
                versionLabel.style.color = new Color(0.6f, 0.8f, 1f);
                depItem.Add(versionLabel);

                dependenciesList.Add(depItem);
            }

            container.Add(dependenciesList);

            return container;
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
            // Lock assembly reload to prevent domain reload during import (like Unity's original window)
            if (!_isImportMode)
            {
                EditorApplication.LockReloadAssemblies();
                _isImportMode = true;
            }
            
            // Store import items first (needed for verification)
            _currentPackagePath = packagePath;
            _currentImportItems = importItems;
            _allImportItems = allImportItems ?? importItems; // Store all items for verification
            _currentPackageIconPath = packageIconPath;
            _packageImportWizardInstance = wizardInstance;
            _isProjectSettingsStep = isProjectSettingsStep;

            // Set window title to match Unity's default
            titleContent = new GUIContent("Import Unity Package");
            
            // Set minimum window size
            minSize = new Vector2(500, 600);

            // Show installer view
            ShowInstallerView();

            // Verify package signature FIRST (synchronously) before setting up UI
            // This ensures verification completes before UI elements are displayed
            VerifyPackage(packagePath);

            // Update button visibility and text based on wizard state
            UpdateButtonStates();

            // Extract metadata from ALL import items (not just current step) to find icon/banner
            // Also pass packageIconPath to extract icon even if no YUCP metadata exists
            var metadata = PackageMetadataExtractor.ExtractMetadataFromImportItems(allImportItems ?? importItems, packagePath, packageIconPath);
            SetMetadata(metadata);

            // Build tree from current step's import items
            SetImportItems(importItems);

            // Refresh UI now that everything is set up (including verification result)
            RefreshUI();

            // Make window modal using fixed implementation that preserves tooltip/cursor behavior
            ShowModalUtilityFixed();

            // Focus window
            Focus();
        }

        /// <summary>
        /// Fixed version of ShowModalUtility that preserves tooltip and cursor behavior.
        /// Based on Unity's implementation but skips the problematic EventDispatcher context push
        /// that breaks UI Toolkit event handling.
        /// </summary>
        private void ShowModalUtilityFixed()
        {
            if (_isModalFixed)
                return;

            try
            {
                // Step 1: Get ShowMode enum type via reflection (ShowMode is internal)
                var showModeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ShowMode");
                if (showModeType == null)
                {
                    Debug.LogWarning("[YUCP PackageManager] Could not find ShowMode type, falling back to ShowModalUtility");
                    ShowModalUtility();
                    _isModalFixed = true;
                    SetupModalEventHandlers();
                    return;
                }

                // Step 2: Show window with ModalUtility mode (via reflection to access internal method)
                var showWithModeMethod = typeof(EditorWindow).GetMethod("ShowWithMode",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { showModeType },
                    null);

                if (showWithModeMethod != null)
                {
                    var modalUtilityValue = Enum.Parse(showModeType, "ModalUtility");
                    showWithModeMethod.Invoke(this, new object[] { modalUtilityValue });
                }
                else
                {
                    // Fallback to standard ShowModalUtility if reflection fails
                    Debug.LogWarning("[YUCP PackageManager] Could not find ShowWithMode, falling back to ShowModalUtility");
                    ShowModalUtility();
                    _isModalFixed = true;
                    SetupModalEventHandlers();
                    return;
                }

                // Step 2: Try making modal without breaking event dispatcher
                // NOTE: We're skipping Internal_MakeModal to avoid breaking tooltip/cursor events
                // ShowWithMode(ModalUtility) should provide enough modal behavior
                // If full modal blocking is needed, uncomment MakeModalFixed() below
                // MakeModalFixed();

                _isModalFixed = true;
                SetupModalEventHandlers();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to show modal utility (fixed): {ex.Message}\n{ex.StackTrace}");
                // Fallback to standard implementation
                ShowModalUtility();
                _isModalFixed = true;
            }
        }

        /// <summary>
        /// Makes the window modal without breaking event dispatcher context.
        /// Calls Internal_MakeModal directly, skipping PushDispatcherContext that breaks tooltips/cursor.
        /// </summary>
        private void MakeModalFixed()
        {
            try
            {
                // Get the ContainerWindow from m_Parent.window
                var parentField = typeof(EditorWindow).GetField("m_Parent",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (parentField == null)
                {
                    Debug.LogWarning("[YUCP PackageManager] Could not find m_Parent field");
                    return;
                }

                var parent = parentField.GetValue(this);
                if (parent == null)
                {
                    Debug.LogWarning("[YUCP PackageManager] m_Parent is null");
                    return;
                }

                // Get window property from HostView
                var hostViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.HostView");
                if (hostViewType == null)
                {
                    Debug.LogWarning("[YUCP PackageManager] Could not find HostView type");
                    return;
                }

                var windowProperty = hostViewType.GetProperty("window",
                    BindingFlags.Public | BindingFlags.Instance);
                if (windowProperty == null)
                {
                    // Try field instead
                    var windowField = hostViewType.GetField("m_Window",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (windowField != null)
                    {
                        var containerWindow = windowField.GetValue(parent);
                        if (containerWindow != null)
                        {
                            CallInternalMakeModal(containerWindow);
                        }
                    }
                }
                else
                {
                    var containerWindow = windowProperty.GetValue(parent);
                    if (containerWindow != null)
                    {
                        CallInternalMakeModal(containerWindow);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] MakeModalFixed failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Calls Unity's Internal_MakeModal native function directly.
        /// </summary>
        private void CallInternalMakeModal(object containerWindow)
        {
            try
            {
                var internalMakeModalMethod = typeof(EditorWindow).GetMethod("Internal_MakeModal",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (internalMakeModalMethod != null)
                {
                    internalMakeModalMethod.Invoke(null, new[] { containerWindow });
                }
                else
                {
                    Debug.LogWarning("[YUCP PackageManager] Could not find Internal_MakeModal method");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] CallInternalMakeModal failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets up event handlers to manually manage tooltips and cursor state in modal windows.
        /// </summary>
        private void SetupModalEventHandlers()
        {
            if (rootVisualElement == null)
                return;

            // Register mouse leave events to manually hide tooltips
            rootVisualElement.RegisterCallback<MouseLeaveEvent>(OnRootMouseLeave, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<MouseMoveEvent>(OnRootMouseMove, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<MouseEnterEvent>(OnRootMouseEnter, TrickleDown.TrickleDown);

            // Register tooltip events to track and manually manage tooltips
            rootVisualElement.RegisterCallback<TooltipEvent>(OnTooltipEvent, TrickleDown.TrickleDown);
            
            // Also register on all child elements to catch tooltip events
            RegisterTooltipHandlersRecursive(rootVisualElement);
        }

        private void RegisterTooltipHandlersRecursive(VisualElement element)
        {
            if (element == null)
                return;

            // Register mouse leave on each element to hide tooltips
            element.RegisterCallback<MouseLeaveEvent>(OnElementMouseLeave);
            
            // Recursively register on children
            foreach (var child in element.Children())
            {
                RegisterTooltipHandlersRecursive(child);
            }
        }

        private void OnRootMouseLeave(MouseLeaveEvent evt)
        {
            // Hide any active tooltip
            HideActiveTooltip();
            
            // Manually reset cursor when mouse leaves the window
            ResetCursor();
            
            // Clear hover state
            _lastHoveredElement = null;
            _currentTooltipElement = null;
        }

        private void OnRootMouseEnter(MouseEnterEvent evt)
        {
            // Track mouse entering
        }

        private void OnElementMouseLeave(MouseLeaveEvent evt)
        {
            // When mouse leaves an element, hide its tooltip if it was showing
            var element = evt.target as VisualElement;
            if (element == _currentTooltipElement)
            {
                HideTooltipForElement(element);
                _currentTooltipElement = null;
            }
        }

        private void OnRootMouseMove(MouseMoveEvent evt)
        {
            // Track which element is being hovered
            var hoveredElement = evt.target as VisualElement;
            if (hoveredElement != _lastHoveredElement)
            {
                // Element changed - hide tooltip from previous element
                if (_lastHoveredElement != null && _lastHoveredElement == _currentTooltipElement)
                {
                    HideTooltipForElement(_lastHoveredElement);
                }
                _lastHoveredElement = hoveredElement;
            }
            
            // Periodically reset cursor to prevent it from getting stuck
            // This is a workaround for the modal window cursor issue
            if (Time.frameCount % 60 == 0) // Every 60 frames
            {
                ResetCursor();
            }
        }

        private void OnTooltipEvent(TooltipEvent evt)
        {
            // Track which element is showing a tooltip
            var element = evt.target as VisualElement;
            if (element != null && !string.IsNullOrEmpty(evt.tooltip))
            {
                _currentTooltipElement = element;
            }
            else if (string.IsNullOrEmpty(evt.tooltip))
            {
                // Tooltip is being cleared
                _currentTooltipElement = null;
            }
        }

        /// <summary>
        /// Manually hides the active tooltip by sending a TooltipEvent with null tooltip.
        /// </summary>
        private void HideActiveTooltip()
        {
            if (_currentTooltipElement != null)
            {
                HideTooltipForElement(_currentTooltipElement);
                _currentTooltipElement = null;
            }
        }

        /// <summary>
        /// Hides tooltip for a specific element by sending a TooltipEvent with null tooltip.
        /// </summary>
        private void HideTooltipForElement(VisualElement element)
        {
            if (element == null)
                return;

            try
            {
                // Send a TooltipEvent with null tooltip to hide it
                var hideEvent = TooltipEvent.GetPooled();
                hideEvent.target = element;
                hideEvent.tooltip = null;
                hideEvent.rect = Rect.zero;
                element.SendEvent(hideEvent);
                hideEvent.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to hide tooltip: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the cursor to default arrow.
        /// </summary>
        private void ResetCursor()
        {
            // Resetting cursor via AddCursorRect can throw/NRE when called outside a GUI event (e.g. OnDisable during domain reload).
            // Keep this best-effort and non-fatal.
            try
            {
                if (Event.current != null)
                {
                    EditorGUIUtility.AddCursorRect(new Rect(0, 0, 0, 0), MouseCursor.Arrow);
                }
            }
            catch
            {
                // ignore
            }
            
            // Also try to reset via reflection if available
            try
            {
                var setCursorMethod = typeof(EditorGUIUtility).GetMethod("SetMouseCursor",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (setCursorMethod != null)
                {
                    var arrowCursorType = typeof(MouseCursor);
                    var arrowValue = Enum.Parse(arrowCursorType, "Arrow");
                    setCursorMethod.Invoke(null, new[] { arrowValue });
                }
            }
            catch
            {
                // Reflection failed, that's okay
            }
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

            // Refresh dependencies section in details view
            RefreshDependenciesSection();

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

            // Refresh verification status if it exists
            if (_verificationStatusElement != null && _verificationStatusElement.parent != null)
            {
                var parent = _verificationStatusElement.parent;
                int index = parent.IndexOf(_verificationStatusElement);
                _verificationStatusElement.RemoveFromHierarchy();
                
                var newStatus = CreateVerificationStatusElement();
                parent.Insert(index, newStatus);
                _verificationStatusElement = newStatus;
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

        private void OnImportPackageCompleted(string packageName)
        {
            if (!_waitingForImportCompletion)
                return;
            
            // If we have a specific pending package name, ensure this callback matches it
            if (!string.IsNullOrEmpty(_pendingPackageName) &&
                !string.Equals(_pendingPackageName, packageName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _waitingForImportCompletion = false;
            _pendingPackageName = null;

            Debug.Log($"[YUCP PackageManager] Import completed for '{packageName}', organizing assets and registering package...");

            // Use delayCall to ensure Unity has fully finished processing the import
            EditorApplication.delayCall += () =>
            {
                try
                {
                    // Register package in registry (also moves assets into installed-packages container)
                    RegisterPackageAfterImport();

                    // Critical: many installs trigger an immediate domain reload right after we unlock reload assemblies.
                    // Any delayCall/update callbacks can be wiped. Persist a "pending resolve" so we can finish enabling
                    // *.yucp_disabled files after the reload.
                    Debug.Log("[YUCP PackageManager] Marking pending .yucp_disabled resolve (pre-unlock)...");
                    YucpDisabledFileResolver.SetPendingResolve(timeoutSeconds: 60.0);

                    // Unlock assembly reload (import is complete)
                    if (_isImportMode)
                    {
                        EditorApplication.UnlockReloadAssemblies();
                        _isImportMode = false;
                        Debug.Log("[YUCP PackageManager] Unlocked assembly reload (import complete)");
                    }

                    // If the install pipeline moved/created files using System.IO (e.g. writing into Packages/),
                    // Unity may not automatically pick up new scripts and trigger compilation.
                    // Force a refresh and request script compilation after unlocking assemblies.
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            Debug.Log("[YUCP PackageManager] Post-import: forcing AssetDatabase.Refresh + requesting script compilation...");
                            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                            // Request compilation; if this triggers a domain reload, the pending resolver will resume.
                            CompilationPipeline.RequestScriptCompilation();
                        }
                        catch (Exception refreshEx)
                        {
                            Debug.LogWarning($"[YUCP PackageManager] Post-import refresh/compile request failed: {refreshEx.Message}");
                        }
                    };

                    // Close the import window after successful import
                    try
                    {
                        Close();
                        GUIUtility.ExitGUI();
                    }
                    catch (ExitGUIException)
                    {
                        // Expected when closing modal windows
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[YUCP PackageManager] Error handling import completion: {ex.Message}\n{ex.StackTrace}");
                }
            };
        }

        private void OnImportClicked()
        {
            
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

                Debug.Log("[YUCP PackageManager] Import initiated, waiting for completion...");

                // Remember which package we're expecting completion for
                _waitingForImportCompletion = true;
                _pendingPackageName = packageName;
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
                
                // Unlock assembly reload before closing (import cancelled)
                if (_isImportMode)
                {
                    EditorApplication.UnlockReloadAssemblies();
                    _isImportMode = false;
                    Debug.Log("[YUCP PackageManager] Unlocked assembly reload (import cancelled)");
                }
                
                try
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
                catch (ExitGUIException)
                {
                    // ExitGUIException is expected and normal when closing modal windows
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

                        if (childPath.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase))
                        {
                            int status = (int)(enabledStatusField.GetValue(childItem) ?? -1);
                            if (status > 0) hasSelected = true;
                            if (status < 0) hasUnselected = true;
                        }
                    }

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

        // View management methods
        private void ShowInstalledPackagesView()
        {
            _currentViewMode = ViewMode.InstalledPackages;
            _currentViewContainer.Clear();
            
            // Always create a new view to ensure it's fresh
            _installedPackagesView = new InstalledPackagesView(OnPackageSelected);
            
            _installedPackagesView.RefreshPackages();
            _currentViewContainer.Add(_installedPackagesView);
            
            // Hide installer UI
            if (_mainScrollView != null)
            {
                _mainScrollView.style.display = DisplayStyle.None;
            }
            
            Debug.Log("[PackageManager] Showing InstalledPackagesView");
        }

        private void ShowPackageDetailsView(InstalledPackageInfo packageInfo)
        {
            _currentViewMode = ViewMode.PackageDetails;
            _currentPackageInfo = packageInfo;
            _currentViewContainer.Clear();
            
            _packageDetailsView = new PackageDetailsView(
                packageInfo,
                OnBackToInstalledPackages,
                OnUpdatePackage,
                OnUninstallPackage
            );
            
            _currentViewContainer.Add(_packageDetailsView);
            
            // Hide installer UI
            if (_mainScrollView != null)
            {
                _mainScrollView.style.display = DisplayStyle.None;
            }
        }

        private void ShowInstallerView()
        {
            _currentViewMode = ViewMode.Installer;
            _currentViewContainer.Clear();
            
            // Show installer UI (mainScrollView with all installer components)
            // The installer UI is already created in CreateGUI, just show it
            if (_mainScrollView != null)
            {
                _mainScrollView.style.display = DisplayStyle.Flex;
                _currentViewContainer.Add(_mainScrollView);
            }
        }

        private void OnPackageSelected(InstalledPackageInfo packageInfo)
        {
            ShowPackageDetailsView(packageInfo);
        }

        private void OnBackToInstalledPackages()
        {
            ShowInstalledPackagesView();
        }

        private void OnUpdatePackage(InstalledPackageInfo packageInfo)
        {
            // TODO: Implement update functionality
            EditorUtility.DisplayDialog("Update Package", 
                $"Update functionality for '{packageInfo.packageName}' will be implemented soon.", 
                "OK");
        }

        private void OnUninstallPackage(InstalledPackageInfo packageInfo)
        {
            if (PackageUninstaller.UninstallPackage(packageInfo.packageId))
            {
                // Refresh the view
                if (_currentViewMode == ViewMode.InstalledPackages && _installedPackagesView != null)
                {
                    _installedPackagesView.RefreshPackages();
                }
                else
                {
                    ShowInstalledPackagesView();
                }
            }
        }

        private void RegisterPackageAfterImport()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentPackagePath))
                    return;

                // Extract metadata
                var metadata = PackageMetadataExtractor.ExtractMetadataFromImportItems(
                    _allImportItems ?? _currentImportItems, 
                    _currentPackagePath, 
                    _currentPackageIconPath);

                // Extract manifest and packageId
                string packageId = null;
                string archiveSha256 = null;
                string publisherId = null;
                bool isVerified = false;

                try
                {
                    var extractionResult = PackageVerifierCore.ManifestExtractor.ExtractSigningData(_currentPackagePath, _allImportItems);
                    if (extractionResult != null && extractionResult.success && extractionResult.manifest != null)
                    {
                        var manifest = extractionResult.manifest;
                        packageId = manifest.packageId;
                        archiveSha256 = manifest.archiveSha256;
                        publisherId = manifest.publisherId;
                    }

                    // Check verification
                    if (_verificationResult != null)
                    {
                        isVerified = _verificationResult.valid;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageManager] Failed to extract manifest: {ex.Message}");
                }

                // Move imported assets into the dedicated installed-packages container and
                // collect their final locations so uninstall/update flows can track them.
                var installedFiles = new List<string>();
                try
                {
                    if (_allImportItems != null && _allImportItems.Length > 0)
                    {
                        installedFiles = InstalledPackagesOrganizer.MoveImportedAssetsToInstalledPackage(
                            _allImportItems,
                            packageId,
                            metadata.packageName);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageManager] Failed to organize installed files under '{InstalledPackagesOrganizer.RootAssetPath}': {ex.Message}");
                    // Fallback: keep original destination paths if we can't move them
                    installedFiles = new List<string>();
                    if (_allImportItems != null)
                    {
                        try
                        {
                            var itemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
                            if (itemType != null)
                            {
                                var destinationPathField = itemType.GetField("destinationAssetPath");
                                if (destinationPathField != null)
                                {
                                    foreach (var item in _allImportItems)
                                    {
                                        if (item == null) continue;
                                        string path = destinationPathField.GetValue(item) as string;
                                        if (!string.IsNullOrEmpty(path))
                                        {
                                            installedFiles.Add(path);
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // ignore fallback errors
                        }
                    }
                }

                // Create InstalledPackageInfo
                var installedInfo = new InstalledPackageInfo
                {
                    packageName = metadata.packageName,
                    version = metadata.version,
                    author = metadata.author,
                    description = metadata.description,
                    icon = metadata.icon,
                    banner = metadata.banner,
                    productLinks = metadata.productLinks,
                    dependencies = metadata.dependencies,
                    packageId = packageId ?? "",
                    archiveSha256 = archiveSha256 ?? "",
                    installedVersion = metadata.version,
                    isVerified = isVerified,
                    publisherId = publisherId ?? "",
                    installedFiles = installedFiles
                };
                installedInfo.SetInstalledDateTime(DateTime.Now);

                // Register in registry
                var registry = InstalledPackageRegistry.GetOrCreate();
                registry.RegisterPackage(installedInfo);

                Debug.Log($"[PackageManager] Registered package: {installedInfo.packageName} (ID: {packageId})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageManager] Failed to register package after import: {ex.Message}");
                Debug.LogException(ex);
            }
        }
    }
}
#endif

