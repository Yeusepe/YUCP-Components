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
using YUCP.Importer.Editor.PackageManager.Core;
using YUCP.Importer.Editor.PackageVerifier;
using YUCP.Importer.Editor.PackageVerifier.Core;
using YUCP.Importer.Editor.PackageVerifier.Data;
using YUCP.Importer.Editor.PackageVerifier.Settings;
using PackageVerifierCore = YUCP.Importer.Editor.PackageVerifier.Core;

namespace YUCP.Importer.Editor.PackageManager
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
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.importer/Resources/Icons/YUCPIcon.png");
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
        private VisualElement _detailsToggleButton;
        private VisualElement _contentWrapper;
        private VisualElement _scrollContent;
        private VisualElement _summarySpacer;
        private Button _importButton;
        private Button _cancelButton;
        private Button _backButton;
        private VisualElement _conflictModeSection;
        private Button _overwriteModeButton;
        private Button _keepExistingModeButton;

        // State
        private PackageMetadata _currentMetadata;
        private Texture2D _bannerGradientTexture;
        private bool _detailsExpanded = false;
        private bool _preferOverwriteExisting = true;
        private int _cachedGradientHeight = 0;
        private const string DefaultGridPlaceholderPath = "Packages/com.yucp.devtools/Resources/DefaultGrid.png";
        private const string CreatorIdentityBagIconPath = "Packages/com.yucp.devtools/Editor/PackageSigning/Resources/Bag.png";
        private PackageItemTreeView _treeView;
        private VisualElement _treeScrollView;
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
        private PackageManifest _cachedManifest;
        private SignatureData _cachedSignature;
        private string _cachedSigningExtractionError;
        private PackageMetadata _cachedMetadata;
        private static PackageMetadata s_lastImportMetadata;
        private static string s_lastImportPackagePath;

        // License gate state
        private VisualElement _licenseSection;
        private readonly Dictionary<string, LicenseVerificationState> _licenseStates = new Dictionary<string, LicenseVerificationState>();
        private bool _isCreatorIdentitySigningIn;
        private readonly List<ProtectedDerivedDescriptor> _protectedDerivedDescriptors = new List<ProtectedDerivedDescriptor>();
        private static readonly HashSet<string> s_protectedDerivedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private class ProtectedDerivedDescriptor
        {
            public string destinationPath;
            public string licensePackageId;
            public string displayName;
            public string sourceFolder;
        }

        private class LicenseVerificationState
        {
            public bool isVerified;
            public string selectedProvider = "gumroad"; // "gumroad", "jinxxy", or "discord"
            public string licenseKey = "";
            public VisualElement statusBadge;
            public Button verifyButton;
            public VisualElement keyInputRow;  // shown for gumroad/jinxxy
            public VisualElement discordRow;   // shown for discord
        }
        
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
            root.AddToClassList("yucp-root");
            
            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.yucp.importer/Editor/PackageManager/Styles/PackageManager.uss");
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
            _mainScrollView.AddToClassList("yucp-main-scrollview");
            _mainScrollView.style.flexGrow = 1;
            _mainScrollView.style.flexShrink = 1;
            _mainScrollView.style.minHeight = 0;
            _mainScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _mainScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _mainScrollView.style.display = DisplayStyle.None; // Hidden by default, shown only in installer mode

            // Get the scroll view's content container
            _scrollContent = _mainScrollView.contentContainer;
            _scrollContent.style.flexDirection = FlexDirection.Column;
            _scrollContent.style.flexGrow = 0;
            _scrollContent.style.flexShrink = 0;
            _scrollContent.style.position = Position.Relative;
            _scrollContent.AddToClassList("yucp-scroll-content");

            // Banner section (background layer, positioned absolutely at top)
            _bannerContainer = CreateBannerSection();
            _bannerContainer.style.position = Position.Absolute;
            _bannerContainer.style.top = 0;
            _bannerContainer.style.left = 0;
            _bannerContainer.style.right = 0;
            _scrollContent.Add(_bannerContainer);
            _bannerContainer.SendToBack();

            // Spacer to push content to bottom (in scroll content, not content wrapper)
            _summarySpacer = new VisualElement();
            _summarySpacer.AddToClassList("yucp-spacer");
            _scrollContent.Add(_summarySpacer);

            // Content wrapper - at bottom (normal flow, will appear on top of banner)
            _contentWrapper = new VisualElement();
            _contentWrapper.style.flexDirection = FlexDirection.Column;
            _contentWrapper.style.flexShrink = 0;
            _contentWrapper.style.position = Position.Relative;
            _contentWrapper.AddToClassList("yucp-content-wrapper");
            _scrollContent.Add(_contentWrapper);

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

            if (_detailsExpanded)
            {
                _bannerContainer.style.height = 0;
                return;
            }

            // Use root visual element height or window position height
            var rootHeight = rootVisualElement.resolvedStyle.height;
            if (rootHeight <= 0)
            {
                rootHeight = position.height;
            }

            var bannerHeight = Mathf.Clamp(rootHeight * 0.58f, 280f, 520f);
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

            Color tintColor = new Color(60f / 255f, 60f / 255f, 60f / 255f, 1f);

            for (int y = 0; y < height; y++)
            {
                float vertical = (float)y / Mathf.Max(1, height - 1);
                float alpha = vertical;
                for (int x = 0; x < width; x++)
                {
                    Color color = new Color(tintColor.r, tintColor.g, tintColor.b, alpha);
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

            if (_bannerGradientTexture != null && !_detailsExpanded)
            {
                _bannerGradientOverlay.style.backgroundImage = new StyleBackground(_bannerGradientTexture);
                _bannerGradientOverlay.style.backgroundColor = Color.clear;
            }
            else
            {
                _bannerGradientOverlay.style.backgroundImage = StyleKeyword.None;
                _bannerGradientOverlay.style.backgroundColor = _detailsExpanded
                    ? new Color(0.05f, 0.05f, 0.05f, 0.96f)
                    : new Color(0.05f, 0.05f, 0.05f, 0.62f);
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
            section.AddToClassList("yucp-installer-summary");

            var headerRow = new VisualElement();
            headerRow.AddToClassList("yucp-metadata-header");
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.FlexStart;
            headerRow.style.marginBottom = 0;

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

            var nameVersionColumn = new VisualElement();
            nameVersionColumn.style.flexGrow = 1;
            nameVersionColumn.style.flexShrink = 1;
            nameVersionColumn.style.marginLeft = 16;
            nameVersionColumn.style.minWidth = 0;
            nameVersionColumn.style.overflow = Overflow.Hidden;

            var packageContext = new Label("Package");
            packageContext.AddToClassList("yucp-package-context-label");
            nameVersionColumn.Add(packageContext);

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
            nameLabel.tooltip = packageName;
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

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            spacer.style.flexShrink = 0;
            nameRow.Add(spacer);

            nameVersionColumn.Add(nameRow);

            if (!string.IsNullOrEmpty(_currentMetadata?.author))
            {
                var authorValueLabel = new Label(_currentMetadata.author);
                authorValueLabel.AddToClassList("yucp-package-author-label");
                authorValueLabel.AddToClassList("yucp-ellipsis-text");
                authorValueLabel.style.marginTop = 4;
                authorValueLabel.style.fontSize = 12;
                authorValueLabel.tooltip = _currentMetadata.author;
                nameVersionColumn.Add(authorValueLabel);
            }

            var versionRow = new VisualElement();
            versionRow.AddToClassList("yucp-version-row");
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

            var buttonContainer = new VisualElement();
            buttonContainer.AddToClassList("yucp-action-buttons-container");
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.alignItems = Align.FlexStart;
            buttonContainer.style.marginLeft = 16;
            buttonContainer.style.flexShrink = 0;

            _cancelButton = new Button(OnCancelClicked)
            {
                text = "Cancel"
            };
            _cancelButton.AddToClassList("yucp-action-button");
            _cancelButton.AddToClassList("yucp-cancel-button");
            buttonContainer.Add(_cancelButton);

            _backButton = new Button(OnBackClicked)
            {
                text = "Back"
            };
            _backButton.AddToClassList("yucp-action-button");
            _backButton.style.display = DisplayStyle.None; // Hidden by default
            buttonContainer.Add(_backButton);

            _importButton = new Button(OnImportClicked)
            {
                text = "Import"
            };
            _importButton.AddToClassList("yucp-action-button");
            _importButton.AddToClassList("yucp-import-button");
            buttonContainer.Add(_importButton);

            headerRow.Add(buttonContainer);
            section.Add(headerRow);

            if (!string.IsNullOrEmpty(_currentMetadata?.description))
            {
                var descValueLabel = new Label(_currentMetadata.description);
                descValueLabel.AddToClassList("yucp-package-description");
                descValueLabel.style.whiteSpace = WhiteSpace.Normal;
                descValueLabel.style.maxHeight = 100;
                descValueLabel.style.overflow = Overflow.Hidden;
                descValueLabel.style.marginTop = 8;
                section.Add(descValueLabel);
            }

            _verificationStatusElement = CreateVerificationStatusElement();
            section.Add(_verificationStatusElement);

            _licenseSection = new VisualElement();
            _licenseSection.style.display = DisplayStyle.None;
            section.Add(_licenseSection);

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

                    string tooltipText = string.IsNullOrEmpty(link.label) ? link.url : $"{link.label}\n{link.url}";
                    linkButton.tooltip = tooltipText;

                    var linkIcon = new Image();
                    Texture2D displayLinkIcon = link.GetDisplayIcon();
                    if (displayLinkIcon == null)
                    {
                        displayLinkIcon = GetPlaceholderTexture();
                    }
                    linkIcon.image = displayLinkIcon;
                    linkIcon.style.width = 32;
                    linkIcon.style.height = 32;

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

        private void ResetCachedSigningData()
        {
            _cachedManifest = null;
            _cachedSignature = null;
            _cachedSigningExtractionError = null;
        }

        private void CacheSigningExtractionResult(PackageVerifierCore.ManifestExtractor.ExtractionResult extractionResult)
        {
            ResetCachedSigningData();

            if (extractionResult == null)
            {
                _cachedSigningExtractionError = "Extraction result was null.";
                return;
            }

            _cachedManifest = extractionResult.manifest;
            _cachedSignature = extractionResult.signature;
            _cachedSigningExtractionError = extractionResult.error;
        }

        private static int GetImportItemCount(System.Array items)
        {
            return items?.Length ?? 0;
        }

        private static bool HasDirectVpmInstallerLoaded()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType("YUCP.DirectVpmInstaller.DirectVpmInstaller") != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ImportItemsContainInstallerPayload(System.Array items)
        {
            if (items == null || items.Length == 0)
            {
                return IsInstallerPayloadPresentOnDisk();
            }

            var itemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            var destinationPathField = itemType?.GetField("destinationAssetPath");
            if (destinationPathField == null)
            {
                return false;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                string path = destinationPathField.GetValue(item) as string;
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (!path.StartsWith("Packages/yucp.installed-packages/Editor/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (path.IndexOf("YUCP_Installer_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("YUCP_FullDomainReload_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("YUCP_InstallerTxn_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("YUCP_InstallerHealthTools_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return IsInstallerPayloadPresentOnDisk();
        }

        private static bool IsInstallerPayloadPresentOnDisk()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string installerRoot = Path.Combine(projectRoot, "Packages", "yucp.installed-packages", "Editor");
                if (!Directory.Exists(installerRoot))
                {
                    return false;
                }

                foreach (string path in Directory.GetFiles(installerRoot, "*.cs", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(path);
                    if (fileName.StartsWith("YUCP_Installer_", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("YUCP_FullDomainReload_", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("YUCP_InstallerTxn_", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("YUCP_InstallerHealthTools_", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                foreach (string path in Directory.GetFiles(installerRoot, "*.asmdef", SearchOption.TopDirectoryOnly))
                {
                    if (Path.GetFileName(path).StartsWith("YUCP_Installer_", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private void LogTempInstallStatus()
        {
            string packageJsonPath = PackageMetadataExtractor.GetPackageJsonDestinationPath(_allImportItems ?? _currentImportItems);
            if (string.IsNullOrEmpty(packageJsonPath))
            {
                return;
            }

            bool hasTempInstallDescriptor = PackageMetadataExtractor.HasTempInstallDescriptor(_allImportItems ?? _currentImportItems);
            bool hasInstallerPayload = ImportItemsContainInstallerPayload(_allImportItems ?? _currentImportItems);
            Debug.Log($"[YUCP PackageManager] package.json import marker: '{packageJsonPath}'. tempInstallDescriptor={hasTempInstallDescriptor}, installerPayloadPresent={hasInstallerPayload}");

            if (!hasTempInstallDescriptor)
            {
                return;
            }

            if (HasDirectVpmInstallerLoaded())
            {
                return;
            }

            if (hasInstallerPayload)
            {
                Debug.Log("[YUCP PackageManager] DirectVpmInstaller payload is present in the import. It will become available after Unity compiles the imported editor scripts.");
            }
            else
            {
                Debug.LogError("[YUCP PackageManager] Detected a YUCP temp-install descriptor, but the import items do not include a DirectVpmInstaller payload. Derived-content/VPM handoff cannot run.");
            }
        }

        private void VerifyPackage(string packagePath)
        {
            ResetCachedSigningData();

            if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            {
                _verificationResult = null;
                _isPackageSigned = false;
                _cachedSigningExtractionError = "Package path was empty or the file does not exist.";
                return;
            }

            try
            {
                Debug.Log($"[YUCP PackageManager] Starting verification for '{packagePath}'. importItems={GetImportItemCount(_allImportItems)}");

                // Reload all keys (root, cached, etc.) in case they were updated
                TrustedAuthority.ReloadAllKeys();

                // Extract manifest and signature
                // Pass ImportPackageItem array if available (during import) - this avoids needing SharpZipLib
                // Use _allImportItems to ensure we check all package contents, not just current step
                var extractionResult = PackageVerifierCore.ManifestExtractor.ExtractSigningData(packagePath, _allImportItems);
                if (extractionResult == null)
                {
                    _isPackageSigned = false;
                    _cachedSigningExtractionError = "ManifestExtractor returned null.";
                    _verificationResult = new PackageVerifierCore.VerificationResult
                    {
                        valid = false,
                        errors = { _cachedSigningExtractionError }
                    };
                    Debug.LogError("[YUCP PackageManager] ManifestExtractor returned null during verification.");
                    return;
                }
                CacheSigningExtractionResult(extractionResult);

                // Check if package has signing data (manifest or signature found)
                _isPackageSigned = extractionResult.manifest != null && extractionResult.signature != null;
                Debug.Log($"[YUCP PackageManager] Signing extraction finished. success={extractionResult.success}, signed={_isPackageSigned}, manifestPresent={extractionResult.manifest != null}, signaturePresent={extractionResult.signature != null}, error='{extractionResult.error ?? ""}'");

                if (extractionResult.manifest != null)
                {
                    Debug.Log($"[YUCP PackageManager] Extracted manifest summary: packageId='{extractionResult.manifest.packageId}', version='{extractionResult.manifest.version}', authorityId='{extractionResult.manifest.authorityId}', publisherId='{extractionResult.manifest.publisherId}', hasCertificateChain={extractionResult.manifest.certificateChain != null && extractionResult.manifest.certificateChain.Length > 0}");
                }

                if (!extractionResult.success || !_isPackageSigned)
                {
                    // Package not signed - this is OK, just not verified
                    // This is expected if the package was exported without signing or signing failed during export
                    _verificationResult = new PackageVerifierCore.VerificationResult
                    {
                        valid = false,
                        errors = { extractionResult.error ?? "Package is not signed. This package was exported without a signature." }
                    };
                    Debug.LogWarning($"[YUCP PackageManager] Package is not fully signed/verified: {_verificationResult.errors[0]}");
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
                    Debug.Log($"[YUCP PackageManager] Package verification succeeded. packageId='{_verificationResult.packageId}', publisherId='{_verificationResult.publisherId}', version='{_verificationResult.version}'");
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
            string verifiedIconPath = "Packages/com.yucp.importer/Editor/PackageManager/Resources/Verified.png";
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
                warningContainer.AddToClassList("lgate-warning");

                var statusText = new Label("Package Not Verified");
                statusText.AddToClassList("lgate-warning-title");
                warningContainer.Add(statusText);

                // Show error details if available
                if (_verificationResult.errors != null && _verificationResult.errors.Count > 0)
                {
                    var errorText = new Label(_verificationResult.errors[0]);
                    errorText.AddToClassList("lgate-warning-body");
                    warningContainer.Add(errorText);
                }

                var noteText = new Label("You can still import this package, but it may be unsafe.");
                noteText.AddToClassList("lgate-warning-body");
                warningContainer.Add(noteText);

                container.Add(warningContainer);
            }

            return container;
        }

        private VisualElement CreateDetailsToggleButton()
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-dtb");
            row.AddManipulator(new Clickable(OnDetailsToggleClicked));

            // Teal left accent bar
            var accent = new VisualElement();
            accent.AddToClassList("yucp-dtb-accent");
            row.Add(accent);

            // Icon
            var icon = new Label("≡");
            icon.name = "details-icon";
            icon.AddToClassList("yucp-dtb-icon");
            row.Add(icon);

            // Main text
            var text = new Label("Review package contents");
            text.name = "details-text";
            text.AddToClassList("yucp-dtb-text");
            row.Add(text);

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            row.Add(spacer);

            // Dependencies / file count badge
            var depsIndicator = new Label();
            depsIndicator.name = "dependencies-indicator";
            depsIndicator.AddToClassList("yucp-dtb-count");
            depsIndicator.style.display = DisplayStyle.None;
            row.Add(depsIndicator);

            // Chevron
            var arrow = new Label("›");
            arrow.name = "details-arrow";
            arrow.AddToClassList("yucp-dtb-arrow");
            row.Add(arrow);

            return row;
        }

        private void OnDetailsToggleClicked()
        {
            _detailsExpanded = !_detailsExpanded;
            AnimateDetailsToggle();
        }

        private void AnimateDetailsToggle()
        {
            UpdateInstallerLayout();
        }

        private VisualElement CreateContentsSection()
        {
            var section = new VisualElement();
            section.AddToClassList("yucp-contents-section");

            var detailsHeader = new VisualElement();
            detailsHeader.AddToClassList("yucp-details-header");

            var detailsTitle = new Label("Package Contents");
            detailsTitle.AddToClassList("yucp-details-title");
            detailsHeader.Add(detailsTitle);

            var detailsSubtitle = new Label("Review files, licensed derived content, and any existing-file conflicts before installing.");
            detailsSubtitle.AddToClassList("yucp-details-subtitle");
            detailsSubtitle.style.whiteSpace = WhiteSpace.Normal;
            detailsHeader.Add(detailsSubtitle);
            section.Add(detailsHeader);

            var dependenciesContainer = new VisualElement();
            dependenciesContainer.name = "dependencies-container";
            section.Add(dependenciesContainer);

            var protectedSummaryContainer = new VisualElement();
            protectedSummaryContainer.name = "protected-summary-container";
            section.Add(protectedSummaryContainer);

            _conflictModeSection = new VisualElement();
            _conflictModeSection.AddToClassList("yucp-conflict-mode-section");

            var conflictLabel = new Label("Conflicting files");
            conflictLabel.AddToClassList("yucp-conflict-mode-label");
            _conflictModeSection.Add(conflictLabel);

            var conflictSegment = new VisualElement();
            conflictSegment.AddToClassList("yucp-conflict-segment");

            _overwriteModeButton = new Button(() => ApplyConflictSelectionMode(true))
            {
                text = "Replace"
            };
            _overwriteModeButton.AddToClassList("yucp-conflict-option");
            _overwriteModeButton.tooltip = "Import and replace files that already exist in the project.";
            conflictSegment.Add(_overwriteModeButton);

            _keepExistingModeButton = new Button(() => ApplyConflictSelectionMode(false))
            {
                text = "Keep"
            };
            _keepExistingModeButton.AddToClassList("yucp-conflict-option");
            _keepExistingModeButton.tooltip = "Skip importing files that already exist in the project.";
            conflictSegment.Add(_keepExistingModeButton);

            _conflictModeSection.Add(conflictSegment);
            section.Add(_conflictModeSection);

            _treeScrollView = new VisualElement();
            _treeScrollView.AddToClassList("yucp-tree-scroll");
            _treeScrollView.style.minHeight = 260;
            _treeScrollView.style.flexGrow = 0;
            _treeScrollView.style.flexShrink = 0;

            _treeView = new PackageItemTreeView(_treeScrollView);
            section.Add(_treeScrollView);

            ShowSampleTree();
            UpdateConflictModeSection();

            return section;
        }

        private void UpdateInstallerLayout()
        {
            if (_metadataSection == null || _contentsSection == null || _detailsToggleButton == null || _bannerContainer == null)
            {
                return;
            }

            if (_summarySpacer != null)
            {
                _summarySpacer.style.display = _detailsExpanded ? DisplayStyle.None : DisplayStyle.Flex;
            }

            _bannerContainer.style.display = _detailsExpanded ? DisplayStyle.None : DisplayStyle.Flex;
            _metadataSection.style.display = _detailsExpanded ? DisplayStyle.None : DisplayStyle.Flex;
            _contentsSection.style.display = _detailsExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            if (_contentWrapper != null)
            {
                _contentWrapper.EnableInClassList("yucp-content-wrapper-details", _detailsExpanded);
                _contentWrapper.style.marginTop = _detailsExpanded ? 20 : 0;
            }

            if (_scrollContent != null)
            {
                if (_detailsExpanded)
                {
                    _scrollContent.style.minHeight = StyleKeyword.Auto;
                }
                else
                {
                    float contentHeight = rootVisualElement?.resolvedStyle.height > 0
                        ? rootVisualElement.resolvedStyle.height
                        : position.height;
                    _scrollContent.style.minHeight = contentHeight;
                }
            }

            if (_treeScrollView != null)
            {
                _treeScrollView.style.minHeight = _detailsExpanded ? Mathf.Max(280f, position.height - 250f) : 260f;
                _treeScrollView.style.maxHeight = StyleKeyword.None;
            }

            var dtbText = _detailsToggleButton.Q<Label>("details-text");
            if (dtbText != null)
                dtbText.text = _detailsExpanded ? "Back to summary" : "Review package contents";

            var dtbIcon = _detailsToggleButton.Q<Label>("details-icon");
            if (dtbIcon != null)
                dtbIcon.text = _detailsExpanded ? "←" : "≡";

            var dtbArrow = _detailsToggleButton.Q<Label>("details-arrow");
            if (dtbArrow != null)
                dtbArrow.style.display = _detailsExpanded ? DisplayStyle.None : DisplayStyle.Flex;

            if (_detailsExpanded && _mainScrollView != null && _contentsSection != null)
            {
                _mainScrollView.schedule.Execute(() => _mainScrollView.ScrollTo(_contentsSection)).ExecuteLater(1);
            }

            ApplyGradientToOverlay();
            UpdateBannerHeight();
        }

        private void ApplyConflictSelectionMode(bool overwriteExisting)
        {
            _preferOverwriteExisting = overwriteExisting;
            _treeView?.SetOverwriteExisting(overwriteExisting);
            UpdateConflictModeControls(overwriteExisting);
        }

        private void UpdateConflictModeSection()
        {
            if (_conflictModeSection == null)
            {
                return;
            }

            int conflictCount = CountConflictingImportItems(_currentImportItems);
            _conflictModeSection.style.display = conflictCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateConflictModeControls(_preferOverwriteExisting);
        }

        private void UpdateConflictModeControls(bool overwriteExisting)
        {
            if (_overwriteModeButton == null || _keepExistingModeButton == null)
            {
                return;
            }

            _overwriteModeButton.EnableInClassList("yucp-conflict-option-selected", overwriteExisting);
            _keepExistingModeButton.EnableInClassList("yucp-conflict-option-selected", !overwriteExisting);
        }

        private void RefreshProtectedSummarySection()
        {
            if (_contentsSection == null)
            {
                return;
            }

            var container = _contentsSection.Q<VisualElement>("protected-summary-container");
            if (container == null)
            {
                return;
            }

            container.Clear();
            RefreshProtectedDerivedDescriptors();

            if (_protectedDerivedDescriptors.Count == 0)
            {
                return;
            }

            var card = new VisualElement();
            card.AddToClassList("yucp-protected-summary-card");

            // Header: eyebrow + count
            var cardHeader = new VisualElement();
            cardHeader.style.flexDirection = FlexDirection.Row;
            cardHeader.style.alignItems = Align.Center;
            cardHeader.style.marginBottom = 4;

            var title = new Label("LICENSED CONTENT");
            title.AddToClassList("yucp-protected-summary-title");
            title.style.flexGrow = 1;
            cardHeader.Add(title);

            int assetCount = _protectedDerivedDescriptors.Count;
            var countLabel = new Label($"{assetCount} asset{(assetCount == 1 ? "" : "s")} locked");
            countLabel.AddToClassList("yucp-protected-summary-count");
            cardHeader.Add(countLabel);

            card.Add(cardHeader);

            var previewList = new VisualElement();
            previewList.AddToClassList("yucp-protected-preview-list");

            foreach (var descriptor in _protectedDerivedDescriptors.Take(4))
            {
                var row = new VisualElement();
                row.AddToClassList("yucp-protected-preview-item");
                row.tooltip = descriptor.destinationPath;

                var lockIcon = new Label("◈");
                lockIcon.AddToClassList("yucp-protected-preview-icon");
                row.Add(lockIcon);

                var nameLabel = new Label(descriptor.displayName);
                nameLabel.style.flexGrow = 1;
                nameLabel.style.overflow = Overflow.Hidden;
                row.Add(nameLabel);

                previewList.Add(row);
            }

            if (_protectedDerivedDescriptors.Count > 4)
            {
                var moreLabel = new Label($"+ {_protectedDerivedDescriptors.Count - 4} more");
                moreLabel.AddToClassList("yucp-protected-preview-more");
                previewList.Add(moreLabel);
            }

            card.Add(previewList);
            container.Add(card);
        }

        private VisualElement BuildInfoChip(string count, string labelText)
        {
            var chip = new VisualElement();
            chip.AddToClassList("yucp-license-chip");

            var countBadge = new Label(count);
            countBadge.AddToClassList("yucp-license-chip-count");
            chip.Add(countBadge);

            var label = new Label(labelText);
            label.AddToClassList("yucp-license-chip-label");
            label.style.whiteSpace = WhiteSpace.NoWrap;
            chip.Add(label);
            return chip;
        }

        private void RefreshProtectedDerivedDescriptors()
        {
            _protectedDerivedDescriptors.Clear();
            s_protectedDerivedPaths.Clear();

            var items = _allImportItems ?? _currentImportItems;
            if (items == null || items.Length == 0)
            {
                return;
            }

            foreach (var item in items)
            {
                if (!TryBuildProtectedDerivedDescriptor(item, out var descriptor))
                {
                    continue;
                }

                descriptor.destinationPath = NormalizeImportPath(descriptor.destinationPath);
                if (string.IsNullOrEmpty(descriptor.destinationPath))
                {
                    continue;
                }

                if (s_protectedDerivedPaths.Add(descriptor.destinationPath))
                {
                    _protectedDerivedDescriptors.Add(descriptor);
                }
            }
        }

        private static bool TryBuildProtectedDerivedDescriptor(object item, out ProtectedDerivedDescriptor descriptor)
        {
            descriptor = null;
            if (item == null)
            {
                return false;
            }

            string destinationPath = NormalizeImportPath(GetImportItemString(item, "destinationAssetPath"));
            if (string.IsNullOrEmpty(destinationPath) ||
                !destinationPath.StartsWith("Packages/com.yucp.temp/Patches/DerivedFbxAsset_", StringComparison.OrdinalIgnoreCase) ||
                !destinationPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string sourceFolder = GetImportItemString(item, "sourceFolder");
            string assetText = ReadImportItemAssetText(item);
            if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(assetText))
            {
                return false;
            }

            if (!TryReadSerializedBool(assetText, "requiresLicense", out bool requiresLicense) || !requiresLicense)
            {
                return false;
            }

            string licensePackageId = TryReadSerializedValue(assetText, "licensePackageId");
            if (string.IsNullOrWhiteSpace(licensePackageId))
            {
                return false;
            }

            string displayName = TryReadSerializedValue(assetText, "targetFbxName");
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = TryReadSerializedValue(assetText, "friendlyName");
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                string fileName = Path.GetFileNameWithoutExtension(destinationPath);
                displayName = fileName.StartsWith("DerivedFbxAsset_", StringComparison.OrdinalIgnoreCase)
                    ? fileName.Substring("DerivedFbxAsset_".Length).Trim('_')
                    : fileName;
            }

            descriptor = new ProtectedDerivedDescriptor
            {
                destinationPath = destinationPath,
                licensePackageId = licensePackageId.Trim(),
                displayName = displayName.Trim(),
                sourceFolder = sourceFolder
            };
            return true;
        }

        private static string ReadImportItemAssetText(object item)
        {
            string sourceFolder = GetImportItemString(item, "sourceFolder");
            string exportedAssetPath = GetImportItemString(item, "exportedAssetPath");
            if (string.IsNullOrEmpty(sourceFolder))
            {
                return null;
            }

            string assetPath = Path.Combine(sourceFolder, "asset");
            if (File.Exists(assetPath))
            {
                return File.ReadAllText(assetPath);
            }

            if (!string.IsNullOrEmpty(exportedAssetPath))
            {
                string alternatePath = Path.Combine(sourceFolder, exportedAssetPath);
                if (File.Exists(alternatePath))
                {
                    return File.ReadAllText(alternatePath);
                }
            }

            return null;
        }

        private static string GetImportItemString(object item, string memberName)
        {
            if (item == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            var type = item.GetType();
            var field = type.GetField(memberName);
            if (field != null)
            {
                return field.GetValue(item) as string;
            }

            var property = type.GetProperty(memberName);
            return property?.GetValue(item) as string;
        }

        private static bool TryReadSerializedBool(string assetText, string propertyName, out bool value)
        {
            value = false;
            string serializedValue = TryReadSerializedValue(assetText, propertyName);
            if (string.IsNullOrEmpty(serializedValue))
            {
                return false;
            }

            serializedValue = serializedValue.Trim();
            if (serializedValue == "1" || serializedValue.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (serializedValue == "0" || serializedValue.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            return false;
        }

        private static string TryReadSerializedValue(string assetText, string propertyName)
        {
            if (string.IsNullOrEmpty(assetText) || string.IsNullOrEmpty(propertyName))
            {
                return null;
            }

            using (var reader = new StringReader(assetText))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith(propertyName + ":", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int colonIndex = trimmed.IndexOf(':');
                    if (colonIndex < 0 || colonIndex >= trimmed.Length - 1)
                    {
                        return string.Empty;
                    }

                    return trimmed.Substring(colonIndex + 1).Trim().Trim('"');
                }
            }

            return null;
        }

        private void PopulateCreatorIdentityButton(Button button, string labelText)
        {
            if (button == null)
            {
                return;
            }

            button.text = string.Empty;
            button.Clear();
            button.style.backgroundColor = Color.white;
            button.style.borderTopLeftRadius = 14;
            button.style.borderTopRightRadius = 14;
            button.style.borderBottomLeftRadius = 14;
            button.style.borderBottomRightRadius = 14;
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.paddingLeft = 20;
            button.style.paddingRight = 20;
            button.style.paddingTop = 10;
            button.style.paddingBottom = 10;
            button.style.alignSelf = Align.Center;
            button.style.minHeight = 36;
            button.style.width = 220;
            button.style.maxWidth = 320;
            button.style.flexGrow = 0;
            button.style.flexShrink = 0;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.borderTopWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.opacity = 1f;

            var content = new VisualElement();
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;
            content.style.justifyContent = Justify.Center;

            Texture2D bag = AssetDatabase.LoadAssetAtPath<Texture2D>(CreatorIdentityBagIconPath);
            if (bag != null)
            {
                var iconWrap = new VisualElement();
                iconWrap.style.width = 20;
                iconWrap.style.height = 20;
                iconWrap.style.flexShrink = 0;
                iconWrap.style.overflow = Overflow.Hidden;
                iconWrap.style.alignItems = Align.Center;
                iconWrap.style.justifyContent = Justify.Center;

                var image = new Image();
                image.image = bag;
                image.scaleMode = ScaleMode.ScaleToFit;
                image.style.width = Length.Percent(100);
                image.style.height = Length.Percent(100);
                iconWrap.Add(image);
                content.Add(iconWrap);
            }

            var label = new Label(labelText);
            label.AddToClassList("yucp-license-primary-button-label");
            label.style.marginLeft = 6;
            label.style.alignSelf = Align.Center;
            label.style.color = new Color(0.08f, 0.08f, 0.08f);
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            content.Add(label);

            button.Add(content);
            button.RegisterCallback<MouseEnterEvent>(_ => button.style.opacity = 0.92f);
            button.RegisterCallback<MouseLeaveEvent>(_ => button.style.opacity = 1f);
        }

        private int CountConflictingImportItems(System.Array items)
        {
            if (items == null || items.Length == 0)
            {
                return 0;
            }

            return items.Cast<object>().Count(item =>
                GetImportItemBool(item, "exists") ||
                GetImportItemBool(item, "pathConflict") ||
                GetImportItemBool(item, "assetChanged"));
        }

        private bool GetImportItemBool(object item, string propertyName)
        {
            if (item == null)
            {
                return false;
            }

            var property = item.GetType().GetProperty(propertyName);
            if (property == null)
            {
                return false;
            }

            var value = property.GetValue(item);
            return value is bool b && b;
        }

        private List<string> GetProtectedPayloadPaths(System.Array items)
        {
            RefreshProtectedDerivedDescriptors();
            return _protectedDerivedDescriptors.Select(descriptor => descriptor.destinationPath).ToList();
        }

        internal static bool IsProtectedPayloadPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalized = NormalizeImportPath(path);
            return s_protectedDerivedPaths.Contains(normalized);
        }

        private void BuildLicenseSection()
        {
            if (_licenseSection == null) return;
            _licenseSection.Clear();
            _licenseStates.Clear();

            var reqs = _currentMetadata?.licensePackages;
            if (reqs == null || reqs.Count == 0)
            {
                _licenseSection.style.display = DisplayStyle.None;
                UpdateImportButtonEnabled();
                return;
            }

            bool creatorSignedIn = CreatorIdentityOAuthService.IsSignedIn();
            string creatorName = CreatorIdentityOAuthService.GetDisplayName() ?? "Creator";
            string serverUrl = GetLicenseServerUrl();

            if (creatorSignedIn)
            {
                CreatorIdentityOAuthService.TryBeginBackgroundRefresh(serverUrl, () =>
                {
                    BuildLicenseSection();
                    UpdateImportButtonEnabled();
                });
            }

            _licenseSection.style.display = DisplayStyle.Flex;
            _licenseSection.RemoveFromClassList("yucp-license-gate");
            _licenseSection.AddToClassList("lgate-root");

            // Pre-compute verification states
            foreach (var req in reqs)
            {
                if (req == null || string.IsNullOrEmpty(req.packageId)) continue;
                var cachedToken = LicenseVerificationService.GetCachedToken(req.packageId);
                _licenseStates[req.packageId] = new LicenseVerificationState { isVerified = cachedToken != null };
            }

            if (!creatorSignedIn)
            {
                // ── NOT SIGNED IN: one unified block — packages + sign-in action ──
                var unifiedBlock = new VisualElement();
                unifiedBlock.AddToClassList("lgate-req-block");
                unifiedBlock.style.borderBottomWidth = 0;

                foreach (var req in reqs)
                {
                    if (req == null || string.IsNullOrEmpty(req.packageId)) continue;
                    var state = _licenseStates[req.packageId];
                    string displayName = string.IsNullOrEmpty(req.packageName) ? req.packageId : req.packageName;

                    var nameRow = new VisualElement();
                    nameRow.AddToClassList("lgate-req-name-row");

                    var pkgName = new Label(displayName);
                    pkgName.AddToClassList("lgate-req-name");
                    nameRow.Add(pkgName);

                    var badge = BuildLicenseBadge(state.isVerified);
                    state.statusBadge = badge;
                    nameRow.Add(badge);
                    unifiedBlock.Add(nameRow);
                }

                var signInBtn = new Button(OnCreatorIdentitySignInClicked)
                {
                    text = _isCreatorIdentitySigningIn ? "Connecting…" : "Sign in to verify"
                };
                signInBtn.SetEnabled(!_isCreatorIdentitySigningIn);
                signInBtn.AddToClassList("lgate-solid-btn");
                signInBtn.style.marginTop = 10;
                PopulateCreatorIdentityButton(signInBtn, _isCreatorIdentitySigningIn ? "Connecting…" : "Sign in to verify");
                unifiedBlock.Add(signInBtn);

                // License key fallback (collapsed)
                foreach (var req in reqs)
                {
                    if (req == null || string.IsNullOrEmpty(req.packageId)) continue;
                    bool hasGumroad = !string.IsNullOrEmpty(req.gumroadPermalink);
                    bool hasJinxxy = !string.IsNullOrEmpty(req.jinxxyProductId);
                    if (!hasGumroad && !hasJinxxy) continue;

                    var state = _licenseStates[req.packageId];
                    state.selectedProvider = hasGumroad ? "gumroad" : "jinxxy";

                    var keyInputRow = new VisualElement();
                    keyInputRow.AddToClassList("lgate-input-row");
                    keyInputRow.style.display = DisplayStyle.None;
                    state.keyInputRow = keyInputRow;

                    var keyField = new TextField { value = state.licenseKey };
                    keyField.AddToClassList("lgate-key-field");
                    keyField.RegisterValueChangedCallback(e => state.licenseKey = e.newValue);

                    var verifyBtn = new Button { text = "Verify" };
                    verifyBtn.AddToClassList("lgate-solid-btn");
                    state.verifyButton = verifyBtn;
                    verifyBtn.clicked += () => OnVerifyLicenseClicked(req, state, state.statusBadge);

                    keyInputRow.Add(keyField);
                    keyInputRow.Add(verifyBtn);

                    var keyToggle = new Button(() =>
                    {
                        bool visible = keyInputRow.style.display == DisplayStyle.Flex;
                        keyInputRow.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
                    });
                    keyToggle.text = "Use license key instead";
                    keyToggle.AddToClassList("lgate-key-toggle");
                    unifiedBlock.Add(keyToggle);
                    unifiedBlock.Add(keyInputRow);
                }

                _licenseSection.Add(unifiedBlock);
            }
            else
            {
                // ── SIGNED IN: "Connected as X | Sign out" → per-package rows ─────
                var idRow = new VisualElement();
                idRow.AddToClassList("lgate-id-row");

                var connectedLabel = new Label($"Connected as {creatorName}");
                connectedLabel.AddToClassList("lgate-id-title");
                connectedLabel.style.flexGrow = 1;
                idRow.Add(connectedLabel);

                var signOutBtn = new Button(() =>
                {
                    CreatorIdentityOAuthService.SignOut();
                    BuildLicenseSection();
                }) { text = "Sign out" };
                signOutBtn.AddToClassList("lgate-link-btn");
                idRow.Add(signOutBtn);
                _licenseSection.Add(idRow);

                foreach (var req in reqs)
                {
                    if (req == null || string.IsNullOrEmpty(req.packageId)) continue;
                    var state = _licenseStates[req.packageId];
                    string displayName = string.IsNullOrEmpty(req.packageName) ? req.packageId : req.packageName;

                    bool hasDiscord = HasDiscordProvider(req);
                    bool hasGumroad = !string.IsNullOrEmpty(req.gumroadPermalink);
                    bool hasJinxxy = !string.IsNullOrEmpty(req.jinxxyProductId);
                    bool hasLicenseKey = hasGumroad || hasJinxxy;
                    state.selectedProvider = hasDiscord ? "discord" : (hasGumroad ? "gumroad" : "jinxxy");

                    var block = new VisualElement();
                    block.AddToClassList("lgate-req-block");

                    var nameRow = new VisualElement();
                    nameRow.AddToClassList("lgate-req-name-row");

                    var pkgName = new Label(displayName);
                    pkgName.AddToClassList("lgate-req-name");
                    nameRow.Add(pkgName);

                    var badge = BuildLicenseBadge(state.isVerified);
                    state.statusBadge = badge;
                    nameRow.Add(badge);
                    block.Add(nameRow);

                    if (state.isVerified)
                    {
                        var note = new Label("Licensed content is unlocked on this machine.");
                        note.AddToClassList("lgate-req-note");
                        block.Add(note);
                    }
                    else
                    {
                        if (hasDiscord)
                        {
                            var discordRow = new VisualElement();
                            discordRow.AddToClassList("lgate-discord-row");
                            state.discordRow = discordRow;

                            var discordBtn = new Button { text = "Verify Purchase" };
                            discordBtn.AddToClassList("lgate-discord-btn");
                            PopulateCreatorIdentityButton(discordBtn, "Verify Purchase");
                            discordBtn.clicked += () => OnVerifyDiscordClicked(req, state, badge, discordBtn);
                            discordRow.Add(discordBtn);
                            block.Add(discordRow);
                        }

                        if (hasLicenseKey)
                        {
                            var keyInputRow = new VisualElement();
                            keyInputRow.AddToClassList("lgate-input-row");
                            keyInputRow.style.display = DisplayStyle.None;
                            state.keyInputRow = keyInputRow;

                            var keyField = new TextField { value = state.licenseKey };
                            keyField.AddToClassList("lgate-key-field");
                            keyField.RegisterValueChangedCallback(e => state.licenseKey = e.newValue);

                            var verifyBtn = new Button { text = "Verify" };
                            verifyBtn.AddToClassList("lgate-solid-btn");
                            state.verifyButton = verifyBtn;
                            verifyBtn.clicked += () => OnVerifyLicenseClicked(req, state, badge);

                            keyInputRow.Add(keyField);
                            keyInputRow.Add(verifyBtn);

                            string toggleText = hasDiscord ? "Use license key instead" : "Enter license key";
                            var keyToggle = new Button(() =>
                            {
                                bool visible = keyInputRow.style.display == DisplayStyle.Flex;
                                keyInputRow.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
                            });
                            keyToggle.text = toggleText;
                            keyToggle.AddToClassList("lgate-key-toggle");
                            block.Add(keyToggle);
                            block.Add(keyInputRow);
                        }
                    }

                    _licenseSection.Add(block);
                }
            }

            UpdateImportButtonEnabled();
        }

        private static bool HasDiscordProvider(LicensePackageRequirement req)
        {
            return req != null &&
                ((!string.IsNullOrEmpty(req.productId) && !string.IsNullOrEmpty(req.creatorAuthUserId)) ||
                 !string.IsNullOrEmpty(req.discordGuildId) ||
                 !string.IsNullOrEmpty(req.discordRoleId));
        }

        private static bool IsDiscordOnlyRequirement(LicensePackageRequirement req)
        {
            return HasDiscordProvider(req) &&
                string.IsNullOrEmpty(req.gumroadPermalink) &&
                string.IsNullOrEmpty(req.jinxxyProductId);
        }

        private static VisualElement BuildHeroPill(string text)
        {
            var pill = new VisualElement();
            pill.AddToClassList("yucp-hero-pill");

            var label = new Label(text);
            label.style.fontSize = 9;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(new Color(0.95f, 0.95f, 0.97f));
            pill.Add(label);
            return pill;
        }

        private void BuildSimplifiedDiscordLicenseSection(LicensePackageRequirement req, bool creatorSignedIn, string creatorName)
        {
            if (req == null || string.IsNullOrEmpty(req.packageId))
            {
                return;
            }

            var cachedToken = LicenseVerificationService.GetCachedToken(req.packageId);
            var state = new LicenseVerificationState
            {
                isVerified = cachedToken != null,
                selectedProvider = "discord"
            };
            _licenseStates[req.packageId] = state;

            var block = new VisualElement();
            block.AddToClassList("lgate-simple");

            var eyebrow = new Label("PURCHASE VERIFICATION");
            eyebrow.AddToClassList("lgate-eyebrow");
            block.Add(eyebrow);

            var title = new Label("Unlock licensed content");
            title.AddToClassList("lgate-title");
            block.Add(title);

            var body = new Label("You can always install. Verify your purchase to unlock licensed derived assets on this machine.");
            body.AddToClassList("lgate-body");
            block.Add(body);

            var div = new VisualElement();
            div.AddToClassList("lgate-divider");
            block.Add(div);

            if (state.isVerified)
            {
                var verifiedRow = new VisualElement();
                verifiedRow.AddToClassList("lgate-simple-verified");

                var checkLabel = new Label("✓");
                checkLabel.AddToClassList("lgate-check");
                verifiedRow.Add(checkLabel);

                var verifiedText = new Label(creatorSignedIn
                    ? $"Verified for {creatorName} on this machine."
                    : "Verified on this machine.");
                verifiedText.AddToClassList("lgate-req-note");
                verifiedRow.Add(verifiedText);
                block.Add(verifiedRow);
            }
            else
            {
                var btnRow = new VisualElement();
                btnRow.AddToClassList("lgate-simple-btn-row");

                var verifyBtn = new Button();
                verifyBtn.AddToClassList("lgate-discord-btn");
                PopulateCreatorIdentityButton(verifyBtn, _isCreatorIdentitySigningIn ? "Connecting…" : "Verify Your License");
                verifyBtn.SetEnabled(!_isCreatorIdentitySigningIn);
                verifyBtn.clicked += () => OnVerifyDiscordClicked(req, state, null, verifyBtn);
                btnRow.Add(verifyBtn);
                block.Add(btnRow);
            }

            _licenseSection.Add(block);
        }

        private VisualElement BuildLicenseBadge(bool verified)
        {
            var status = new VisualElement();
            status.AddToClassList("lgate-status");

            var dot = new VisualElement();
            dot.AddToClassList("lgate-status-dot");
            dot.EnableInClassList("lgate-status-dot-verified", verified);
            dot.EnableInClassList("lgate-status-dot-unverified", !verified);
            status.Add(dot);

            var text = new Label(verified ? "Verified" : "Not verified");
            text.AddToClassList("lgate-status-text");
            text.EnableInClassList("lgate-status-text-verified", verified);
            text.EnableInClassList("lgate-status-text-unverified", !verified);
            status.Add(text);

            return status;
        }

        private void OnVerifyLicenseClicked(
            LicensePackageRequirement req,
            LicenseVerificationState state,
            VisualElement badgeSlot)
        {
            if (string.IsNullOrWhiteSpace(state.licenseKey))
            {
                EditorUtility.DisplayDialog("License Required", "Please enter your license key.", "OK");
                return;
            }

            state.verifyButton?.SetEnabled(false);

            string serverUrl = GetLicenseServerUrl();
            string permalink = state.selectedProvider == "gumroad" ? req.gumroadPermalink : null;
            string jinxxyId  = state.selectedProvider == "jinxxy"  ? req.jinxxyProductId  : null;

            LicenseVerificationService.VerifyAsync(
                serverUrl,
                req.packageId,
                state.licenseKey,
                state.selectedProvider,
                permalink ?? jinxxyId ?? "",
                jwt =>
                {
                    state.isVerified = true;
                    EditorApplication.delayCall += () =>
                    {
                        BuildLicenseSection();
                        UpdateImportButtonEnabled();
                    };
                },
                err =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        state.verifyButton?.SetEnabled(true);
                        EditorUtility.DisplayDialog("Verification Failed",
                            $"Could not verify license: {err}", "OK");
                    };
                });
        }

        private void OnVerifyDiscordClicked(
            LicensePackageRequirement req,
            LicenseVerificationState state,
            VisualElement badgeSlot,
            Button discordBtn)
        {
            if (!CreatorIdentityOAuthService.IsSignedIn())
            {
                BeginCreatorIdentitySignIn();
                return;
            }

            discordBtn.SetEnabled(false);
            discordBtn.text = "Verifying…";

            string serverUrl = GetLicenseServerUrl();

            LicenseVerificationService.VerifyDiscordAsync(
                serverUrl,
                req.packageId,
                req.productId,
                req.creatorAuthUserId,
                jwt =>
                {
                    state.isVerified = true;
                    EditorApplication.delayCall += () =>
                    {
                        BuildLicenseSection();
                        UpdateImportButtonEnabled();
                    };
                },
                err =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        discordBtn.SetEnabled(true);
                        PopulateCreatorIdentityButton(discordBtn, "Verify Your License");
                        EditorUtility.DisplayDialog("Discord Verification Failed",
                            $"{err}", "OK");
                    };
                });
        }

        private void UpdateImportButtonEnabled()
        {
            if (_importButton == null) return;
            bool hasUnverifiedLicense = false;
            foreach (var kv in _licenseStates)
            {
                if (!kv.Value.isVerified)
                {
                    hasUnverifiedLicense = true;
                    break;
                }
            }

            _importButton.SetEnabled(!hasUnverifiedLicense);
            _importButton.tooltip = hasUnverifiedLicense
                ? "Verify your purchase above to enable import."
                : string.Empty;
        }

        private void OnCreatorIdentitySignInClicked()
        {
            BeginCreatorIdentitySignIn();
        }

        private void BeginCreatorIdentitySignIn(Action onSuccess = null)
        {
            if (_isCreatorIdentitySigningIn)
            {
                return;
            }

            _isCreatorIdentitySigningIn = true;
            BuildLicenseSection();

            string serverUrl = GetLicenseServerUrl();
#pragma warning disable CS4014
            CreatorIdentityOAuthService.SignInAsync(
                serverUrl,
                () =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        _isCreatorIdentitySigningIn = false;
                        BuildLicenseSection();
                        onSuccess?.Invoke();
                    };
                },
                error =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        _isCreatorIdentitySigningIn = false;
                        BuildLicenseSection();
                        EditorUtility.DisplayDialog("Creator Identity Sign-in Failed", error, "OK");
                    };
                });
#pragma warning restore CS4014
        }

        private static string GetLicenseServerUrl()
        {
            const string preferredServerUrlKey = "YUCP.PackageManager.PreferredServerUrl";

            string preferredUrl = TrustedAuthoritiesSettings.NormalizeUrl(EditorPrefs.GetString(preferredServerUrlKey, string.Empty));
            string signingUrl = TrustedAuthoritiesSettings.NormalizeUrl(GetSigningSettingsServerUrl());
            string legacyUrl = TrustedAuthoritiesSettings.NormalizeUrl(EditorPrefs.GetString("yucp_server_url", string.Empty));
            List<string> trustedUrls = TrustedAuthoritiesSettings.GetUrls();

            if (trustedUrls.Count == 0)
            {
                return preferredUrl
                    ?? signingUrl
                    ?? legacyUrl
                    ?? TrustedAuthoritiesSettings.DefaultTrustedUrl;
            }

            if (!string.IsNullOrEmpty(preferredUrl) && TrustedAuthoritiesSettings.IsTrustedUrl(preferredUrl))
            {
                return preferredUrl;
            }

            if (!string.IsNullOrEmpty(signingUrl) && TrustedAuthoritiesSettings.IsTrustedUrl(signingUrl))
            {
                return signingUrl;
            }

            if (!string.IsNullOrEmpty(legacyUrl) && TrustedAuthoritiesSettings.IsTrustedUrl(legacyUrl))
            {
                return legacyUrl;
            }

            return trustedUrls[0];
        }

        private static string GetSigningSettingsServerUrl()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    Type signingSettingsType = null;

                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        signingSettingsType = assembly.GetType("YUCP.DevTools.Editor.PackageSigning.Data.SigningSettings");
                        if (signingSettingsType != null)
                        {
                            break;
                        }
                    }

                    if (signingSettingsType == null)
                    {
                        signingSettingsType = Type.GetType("YUCP.DevTools.Editor.PackageSigning.Data.SigningSettings, Assembly-CSharp-Editor");
                    }

                    if (signingSettingsType != null)
                    {
                        var settings = AssetDatabase.LoadAssetAtPath(path, signingSettingsType);
                        if (settings != null)
                        {
                            var field = signingSettingsType.GetField("serverUrl", BindingFlags.Public | BindingFlags.Instance);
                            return field?.GetValue(settings) as string;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to resolve license server URL from SigningSettings: {ex.Message}");
            }

            return null;
        }

        private void RefreshDependenciesSection()
        {
            var dependenciesContainer = _contentsSection.Q<VisualElement>("dependencies-container");
            if (dependenciesContainer == null) return;

            dependenciesContainer.Clear();

            var dependenciesSection = CreateDependenciesSection();
            if (dependenciesSection != null)
            {
                dependenciesContainer.Add(dependenciesSection);
            }

            UpdateDetailsButtonDependenciesIndicator();
        }

        private void UpdateDetailsButtonDependenciesIndicator()
        {
            if (_detailsToggleButton == null) return;

            var indicator = _detailsToggleButton.Q<Label>("dependencies-indicator");
            if (indicator == null) return;

            int dependencyCount = _currentMetadata?.dependencies?.Count ?? 0;
            int protectedCount = GetProtectedPayloadPaths(_allImportItems ?? _currentImportItems).Count;

            if (dependencyCount > 0 || protectedCount > 0)
            {
                var bits = new List<string>();
                if (dependencyCount > 0)
                {
                    bits.Add($"{dependencyCount} required package{(dependencyCount == 1 ? string.Empty : "s")}");
                }

                if (protectedCount > 0)
                {
                    bits.Add($"{protectedCount} licensed");
                }

                indicator.text = $"({string.Join(" • ", bits)})";
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
                    iconLabel.style.color = new Color(0.86f, 0.86f, 0.86f);
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
                versionLabel.style.color = new Color(0.84f, 0.84f, 0.84f);
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
            _detailsExpanded = false;
            _preferOverwriteExisting = true;
            RefreshProtectedDerivedDescriptors();

            Debug.Log($"[YUCP PackageManager] InitializeForImport: packagePath='{packagePath}', stepItems={GetImportItemCount(importItems)}, allItems={GetImportItemCount(_allImportItems)}, packageIconPath='{packageIconPath}', isProjectSettingsStep={isProjectSettingsStep}");

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
            s_lastImportMetadata = metadata;
            s_lastImportPackagePath = packagePath;
            LogTempInstallStatus();

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
            RefreshProtectedDerivedDescriptors();
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
            RefreshProtectedDerivedDescriptors();
            RefreshDependenciesSection();
            RefreshProtectedSummarySection();
            UpdateConflictModeSection();

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

            if (_licenseSection != null)
            {
                BuildLicenseSection();
            }

            UpdateInstallerLayout();
        }

        /// <summary>
        /// Set the metadata to display in the window.
        /// Can be called externally when metadata is extracted from a package.
        /// </summary>
        public void SetMetadata(PackageMetadata metadata)
        {
            _cachedMetadata = metadata ?? new PackageMetadata();
            _currentMetadata = _cachedMetadata;
            if (!string.IsNullOrEmpty(_currentPackagePath))
            {
                s_lastImportMetadata = _cachedMetadata;
                s_lastImportPackagePath = _currentPackagePath;
            }
            RefreshUI();
            BuildLicenseSection();
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

            Debug.Log($"[YUCP PackageManager] Import completed for '{packageName}'. packagePath='{_currentPackagePath}', allItems={GetImportItemCount(_allImportItems)}, signed={_isPackageSigned}, verificationValid={_verificationResult != null && _verificationResult.valid}");

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

                int selectedCount = _treeView != null ? _treeView.GetSelectedPaths().Count : 0;
                Debug.Log($"[YUCP PackageManager] Updating import item selections. selectedPaths={selectedCount}, currentStepItems={GetImportItemCount(_currentImportItems)}, allItems={GetImportItemCount(_allImportItems)}");
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

                Debug.Log($"[YUCP PackageManager] Import initiated, waiting for completion. packagePath='{_currentPackagePath}', packageSigned={_isPackageSigned}, verificationValid={_verificationResult != null && _verificationResult.valid}");

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
                    if (string.IsNullOrEmpty(destinationPath)) continue;

                    // Check if item is selected
                    string normalizedDestinationPath = NormalizeImportPath(destinationPath);
                    bool isSelected = selectedSet.Contains(normalizedDestinationPath);
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
                    string normalizedFolderPath = NormalizeImportPath(folderPath);

                    foreach (var childItem in _currentImportItems)
                    {
                        if (childItem == null || childItem == item) continue;

                        string childPath = destinationPathField.GetValue(childItem) as string;
                        if (string.IsNullOrEmpty(childPath)) continue;

                        string normalizedChildPath = NormalizeImportPath(childPath);

                        if (normalizedChildPath.StartsWith(normalizedFolderPath + "/", StringComparison.OrdinalIgnoreCase))
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

        private static string NormalizeImportPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            return path.Replace('\\', '/').TrimStart('/');
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

                Debug.Log($"[YUCP PackageManager] RegisterPackageAfterImport starting. packagePath='{_currentPackagePath}', allItems={GetImportItemCount(_allImportItems)}, cachedManifestPresent={_cachedManifest != null}, cachedSignaturePresent={_cachedSignature != null}");

                // Extract metadata
                var metadata = _cachedMetadata;
                if (metadata == null &&
                    !string.IsNullOrEmpty(_currentPackagePath) &&
                    string.Equals(s_lastImportPackagePath, _currentPackagePath, StringComparison.OrdinalIgnoreCase))
                {
                    metadata = s_lastImportMetadata;
                }

                if (metadata == null)
                {
                    metadata = PackageMetadataExtractor.ExtractMetadataFromImportItems(
                        _allImportItems ?? _currentImportItems,
                        _currentPackagePath,
                        _currentPackageIconPath);
                }

                // Extract manifest and packageId
                string packageId = null;
                string archiveSha256 = null;
                string publisherId = null;
                bool isVerified = false;

                try
                {
                    if (_cachedManifest != null)
                    {
                        packageId = _cachedManifest.packageId;
                        archiveSha256 = _cachedManifest.archiveSha256;
                        publisherId = _cachedManifest.publisherId;
                        Debug.Log($"[YUCP PackageManager] Using cached manifest for registration. packageId='{packageId}', archiveSha256Present={!string.IsNullOrEmpty(archiveSha256)}, publisherId='{publisherId}'");
                    }
                    else
                    {
                        Debug.LogWarning($"[YUCP PackageManager] Cached manifest unavailable during registration. Falling back to re-extract signing data from import items. Previous extraction error: '{_cachedSigningExtractionError ?? ""}'");
                        var extractionResult = PackageVerifierCore.ManifestExtractor.ExtractSigningData(_currentPackagePath, _allImportItems);
                        CacheSigningExtractionResult(extractionResult);
                        if (extractionResult != null && extractionResult.success && extractionResult.manifest != null)
                        {
                            var manifest = extractionResult.manifest;
                            packageId = manifest.packageId;
                            archiveSha256 = manifest.archiveSha256;
                            publisherId = manifest.publisherId;
                        }
                    }

                    // Check verification
                    if (_verificationResult != null)
                    {
                        isVerified = _verificationResult.valid;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP PackageManager] Failed to extract manifest during registration: {ex.Message}");
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
                    Debug.LogWarning($"[YUCP PackageManager] Failed to organize installed files under '{InstalledPackagesOrganizer.RootAssetPath}': {ex.Message}");
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

                Debug.Log($"[YUCP PackageManager] Post-import file tracking collected {installedFiles.Count} paths. FirstPath='{installedFiles.FirstOrDefault() ?? ""}'");

                bool hasTempInstallDescriptor = PackageMetadataExtractor.HasTempInstallDescriptor(_allImportItems ?? _currentImportItems);
                if (hasTempInstallDescriptor)
                {
                    if (HasDirectVpmInstallerLoaded())
                    {
                        Debug.Log("[YUCP PackageManager] Temp-install descriptor detected. Waiting for DirectVpmInstaller/YUCP disabled-file resolution to complete the derived-content handoff.");
                    }
                    else if (ImportItemsContainInstallerPayload(_allImportItems ?? _currentImportItems))
                    {
                        Debug.Log("[YUCP PackageManager] Temp-install descriptor detected and installer payload was imported. Waiting for Unity script compilation/domain reload before the derived-content handoff runs.");
                    }
                    else
                    {
                        Debug.LogError("[YUCP PackageManager] Temp-install descriptor detected, but the imported package did not include a DirectVpmInstaller payload. The Unity import completed, but derived-content/VPM installation cannot run.");
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

                if (string.IsNullOrEmpty(installedInfo.packageId))
                {
                    Debug.LogWarning($"[YUCP PackageManager] Imported assets but skipped registry registration because packageId is unavailable. packageName='{installedInfo.packageName}', signed={_isPackageSigned}, verificationValid={isVerified}, cachedExtractionError='{_cachedSigningExtractionError ?? ""}'");
                    return;
                }

                // Register in registry
                var registry = InstalledPackageRegistry.GetOrCreate();
                registry.RegisterPackage(installedInfo);

                Debug.Log($"[YUCP PackageManager] Registered package: {installedInfo.packageName} (ID: {packageId}, verified={installedInfo.isVerified}, installedFiles={installedInfo.installedFiles?.Count ?? 0})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to register package after import: {ex.Message}");
                Debug.LogException(ex);
            }
        }
    }
}
#endif
