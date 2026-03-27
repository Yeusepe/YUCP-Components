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

        public static void ShowResumeProtectedPackage(InstalledPackageInfo packageInfo)
        {
            if (packageInfo == null)
                return;

            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                Debug.LogWarning("[YUCP PackageManager] Package Manager is disabled; cannot resume protected package setup.");
                return;
            }

            var window = GetWindow<PackageManagerWindow>(true, "Unlock Protected Package");
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.importer/Resources/Icons/YUCPIcon.png");
            window.titleContent = icon == null
                ? new GUIContent("Unlock Protected Package")
                : new GUIContent("Unlock Protected Package", icon);
            window.minSize = new Vector2(500, 600);
            window.Show();

            EditorApplication.delayCall += () =>
            {
                if (window != null)
                    window.InitializeForProtectedResume(packageInfo);
            };
        }

        // UI Elements
        private VisualElement _bannerContainer;
        private VisualElement _bannerImageContainer;
        private VisualElement _bannerFadeOverlay;
        private VisualElement _bannerGradientOverlay;
        private VisualElement _installerRoot;
        private VisualElement _bannerSection;
        private VisualElement _bannerHeroContainer;
        private VisualElement _chipTooltipPopup;
        private const int BannerFadeDurationMs = 450;

        private VisualElement _metadataSection;
        private VisualElement _contentsSection;
        private VisualElement _detailsToggleButton;
        private Button _importButton;
        private Button _cancelButton;
        private Button _backButton;
        private Label _verifyStatusLabel;
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
        private const string ImporterBagIconPath = "Packages/com.yucp.importer/Editor/PackageManager/Resources/Bag.png";
        private const string VerifiedBadgePath = "Packages/com.yucp.importer/Editor/PackageManager/Resources/VerifiedBadge.png";
        private PackageItemTreeView _treeView;
        private VisualElement _treeScrollView;
        private ScrollView _treeScrollWrapper;
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
        private readonly HashSet<string> _verifiedLicensePackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _isCreatorIdentitySigningIn;
        private bool _creatorIdentityNeedsReauthentication;
        private bool _creatorIdentityNeedsSignInRetry;
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
        private bool _pendingImportAfterVerification = false;
        private bool _isResumeVerificationMode = false;
        private InstalledPackageInfo _resumeProtectedPackageInfo;

        // Gallery carousel state
        private int _selectedGalleryIndex = -1;
        private Texture2D _originalBannerTexture;
        private IVisualElementScheduledItem _galleryCarouselSchedule;
        private VisualElement _galleryStripElement;

        // Metadata show-more state
        private bool _metadataGridExpanded = false;
        private VisualElement _metadataGridElement;
        
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

            // Installer root — single-page flex layout, no scroll
            _installerRoot = new VisualElement();
            _installerRoot.AddToClassList("yucp-installer-root");
            _installerRoot.style.display = DisplayStyle.None; // hidden by default

            // Banner section (normal flow, position: relative — hero overlay works correctly inside)
            _bannerSection = CreateBannerSection();
            _installerRoot.Add(_bannerSection);

            // Metadata card (compact 2-column grid)
            _metadataSection = CreateMetadataSection();
            _installerRoot.Add(_metadataSection);

            // Details view (hidden by default; fills full height when expanded)
            _contentsSection = CreateContentsSection();
            _contentsSection.style.display = DisplayStyle.None;
            _installerRoot.Add(_contentsSection);

            // Details toggle button — always visible at the bottom of the installer
            _detailsToggleButton = CreateDetailsToggleButton();
            _detailsToggleButton.style.flexShrink = 0;
            _detailsToggleButton.style.marginLeft = 8;
            _detailsToggleButton.style.marginRight = 8;
            _detailsToggleButton.style.marginBottom = 4;
            _installerRoot.Add(_detailsToggleButton);

            // Initialize with empty metadata
            _currentMetadata = new PackageMetadata();

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
            if (_bannerSection != null)
            {
                UpdateBannerHeight();
            }
        }

        private void UpdateBannerHeight()
        {
            if (_bannerSection == null || rootVisualElement == null) return;

            if (_detailsExpanded) return;

            // Banner uses flex-grow:1 so its height is set by flexbox layout.
            // We only need to regenerate the gradient texture when the rendered height changes.
            float bannerHeight = _bannerSection.resolvedStyle.height;
            if (bannerHeight <= 10)
                bannerHeight = Mathf.Max(160f, position.height * 0.5f);

            int newGradientHeight = Mathf.RoundToInt(bannerHeight);
            if (_bannerGradientTexture == null || Mathf.Abs(_cachedGradientHeight - newGradientHeight) > 5)
            {
                CreateBannerGradientTexture();
                ApplyGradientToOverlay();
            }
        }

        private static float MeasureElementHeight(VisualElement element, float fallback)
        {
            if (element == null)
            {
                return fallback;
            }

            float resolved = element.resolvedStyle.height;
            if (resolved > 0)
            {
                return resolved;
            }

            float layout = element.layout.height;
            if (layout > 0)
            {
                return layout;
            }

            return fallback;
        }

        private VisualElement CreateBannerSection()
        {
            var bannerSection = new VisualElement();
            bannerSection.AddToClassList("yucp-banner-section");
            _bannerContainer = bannerSection; // keep for gradient compatibility
            _bannerSection = bannerSection;

            bannerSection.style.width = Length.Percent(100);
            bannerSection.style.flexGrow = 1;
            bannerSection.style.flexShrink = 1;
            bannerSection.style.minHeight = 160;
            bannerSection.style.overflow = Overflow.Hidden;

            // Banner image
            _bannerImageContainer = new VisualElement();
            _bannerImageContainer.AddToClassList("yucp-banner-image-container");
            _bannerImageContainer.style.position = Position.Absolute;
            _bannerImageContainer.style.top = 0;
            _bannerImageContainer.style.left = 0;
            _bannerImageContainer.style.right = 0;
            _bannerImageContainer.style.bottom = 0;

            Texture2D displayBanner = _currentMetadata?.banner;
            if (displayBanner == null) displayBanner = GetPlaceholderTexture();
            if (displayBanner != null)
            {
                _bannerImageContainer.style.backgroundImage = new StyleBackground(displayBanner);
                _originalBannerTexture = displayBanner;
            }
            bannerSection.Add(_bannerImageContainer);

            // Fade overlay — sits between the image and the gradient for smooth crossfades
            _bannerFadeOverlay = new VisualElement();
            _bannerFadeOverlay.AddToClassList("yucp-banner-fade-overlay");
            _bannerFadeOverlay.style.position = Position.Absolute;
            _bannerFadeOverlay.style.top = 0;
            _bannerFadeOverlay.style.left = 0;
            _bannerFadeOverlay.style.right = 0;
            _bannerFadeOverlay.style.bottom = 0;
            _bannerFadeOverlay.pickingMode = PickingMode.Ignore;
            bannerSection.Add(_bannerFadeOverlay);

            // Gradient overlay
            _bannerGradientOverlay = new VisualElement();
            _bannerGradientOverlay.AddToClassList("yucp-banner-gradient-overlay");
            _bannerGradientOverlay.style.position = Position.Absolute;
            _bannerGradientOverlay.style.top = 0;
            _bannerGradientOverlay.style.left = 0;
            _bannerGradientOverlay.style.right = 0;
            _bannerGradientOverlay.style.bottom = 0;
            _bannerGradientOverlay.pickingMode = PickingMode.Ignore;
            bannerSection.Add(_bannerGradientOverlay);

            // Hero overlay at bottom of banner
            CreateBannerHero();
            if (_bannerHeroContainer != null)
                bannerSection.Add(_bannerHeroContainer);

            return bannerSection;
        }

        private void CreateBannerHero()
        {
            var hero = new VisualElement();
            hero.AddToClassList("yucp-hero-overlay");
            _bannerHeroContainer = hero;

            // Package icon
            var iconWrap = new VisualElement();
            iconWrap.AddToClassList("yucp-hero-icon");
            var iconImage = new Image();
            iconImage.image = _currentMetadata?.icon ?? GetPlaceholderTexture();
            iconImage.style.width = Length.Percent(100);
            iconImage.style.height = Length.Percent(100);
            iconWrap.Add(iconImage);
            hero.Add(iconWrap);

            // Text block
            var textBlock = new VisualElement();
            textBlock.AddToClassList("yucp-hero-text");

            var nameRow = new VisualElement();
            nameRow.AddToClassList("yucp-hero-name-row");
            string packageName = string.IsNullOrEmpty(_currentMetadata?.packageName) ? "Untitled Package" : _currentMetadata.packageName;
            var nameLabel = new Label(packageName);
            nameLabel.AddToClassList("yucp-hero-name");
            nameLabel.tooltip = packageName;
            nameRow.Add(nameLabel);

            var verificationIcon = CreateVerificationIcon();
            if (verificationIcon != null)
            {
                verificationIcon.AddToClassList("yucp-product-verified-badge");
                nameRow.Add(verificationIcon);
            }
            textBlock.Add(nameRow);

            string authorText = "";
            if (!string.IsNullOrEmpty(_currentMetadata?.author))
                authorText += $"By {_currentMetadata.author}";
            if (!string.IsNullOrEmpty(_currentMetadata?.version))
            {
                if (authorText.Length > 0) authorText += "  ·  ";
                authorText += $"v{_currentMetadata.version}";
            }
            if (authorText.Length > 0)
            {
                var metaLabel = new Label(authorText);
                metaLabel.AddToClassList("yucp-hero-meta");
                metaLabel.tooltip = authorText;
                textBlock.Add(metaLabel);
            }

            if (!string.IsNullOrEmpty(_currentMetadata?.tagline))
            {
                var taglineLabel = new Label(_currentMetadata.tagline);
                taglineLabel.AddToClassList("yucp-hero-tagline");
                textBlock.Add(taglineLabel);
            }

            hero.Add(textBlock);

            // CTA column — cancel/back + import all on one row
            var ctaColumn = new VisualElement();
            ctaColumn.AddToClassList("yucp-cta-column");

            var ctaRow = new VisualElement();
            ctaRow.style.flexDirection = FlexDirection.Row;
            ctaRow.style.alignItems = Align.Center;

            _cancelButton = new Button(OnCancelClicked) { text = "Cancel" };
            _cancelButton.AddToClassList("yucp-cta-cancel");
            ctaRow.Add(_cancelButton);

            _backButton = new Button(OnBackClicked) { text = "Back" };
            _backButton.AddToClassList("yucp-cta-cancel");
            _backButton.style.display = DisplayStyle.None;
            _backButton.style.marginLeft = 6;
            ctaRow.Add(_backButton);

            _importButton = new Button(OnImportClicked);
            _importButton.AddToClassList("yucp-cta-button");
            _importButton.style.marginLeft = 8;
            ctaRow.Add(_importButton);

            ctaColumn.Add(ctaRow);

            _verifyStatusLabel = new Label();
            _verifyStatusLabel.AddToClassList("yucp-verify-status");
            _verifyStatusLabel.style.display = DisplayStyle.None;
            ctaColumn.Add(_verifyStatusLabel);

            string ctaSub = BuildCtaSublabel();
            if (!string.IsNullOrEmpty(ctaSub))
            {
                var subLabel = new Label(ctaSub);
                subLabel.AddToClassList("yucp-cta-sublabel");
                ctaColumn.Add(subLabel);
            }

            hero.Add(ctaColumn);
            // Set initial button content (text/icon)
            RefreshPrimaryImportButton();
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

            // Match the original gradient tint colour.
            Color tintColor = new Color(60f / 255f, 60f / 255f, 60f / 255f, 1f);

            for (int y = 0; y < height; y++)
            {
                float vertical = (float)y / Mathf.Max(1, height - 1);
                // Keep the top 30% of the banner fully clear; fade to 88% max at the
                // very bottom so the banner image always bleeds through slightly.
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
            var panel = new VisualElement();
            panel.AddToClassList("yucp-product-panel");
            panel.AddToClassList("yucp-product-panel-loading");

            panel.style.marginTop = 0;
            panel.style.marginLeft = 8;
            panel.style.marginRight = 8;
            panel.style.marginBottom = 0;
            panel.style.flexGrow = 0;
            panel.style.flexShrink = 0;
            panel.style.minHeight = 0;
            panel.style.overflow = Overflow.Hidden;
            panel.style.paddingLeft = 20;
            panel.style.paddingRight = 20;
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 0;
            panel.style.position = Position.Relative;

            panel.schedule.Execute(() => panel.RemoveFromClassList("yucp-product-panel-loading")).ExecuteLater(60);

            // Chip row (full width) — always visible
            var chipRow = BuildChipRow(null);
            if (chipRow != null)
            {
                chipRow.style.marginTop = 4;
                chipRow.style.marginBottom = 0;
                panel.Add(chipRow);
            }

            // Description — always visible, clamped to ~3 lines
            if (!string.IsNullOrEmpty(_currentMetadata?.description))
            {
                var descLabel = new Label(_currentMetadata.description);
                descLabel.AddToClassList("yucp-meta-description");
                panel.Add(descLabel);
            }

            // "Show more" toggle button — collapses What's Inside / What's New / From the Creator
            var showMoreBtn = new Button();
            showMoreBtn.AddToClassList("yucp-show-more-button");
            showMoreBtn.text = "Show more ↓";

            // Collapsible 2-column content grid (hidden by default)
            var grid = new VisualElement();
            grid.AddToClassList("yucp-meta-grid");
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexGrow = 0;
            grid.style.flexShrink = 1;
            grid.style.minHeight = 0;
            grid.style.marginTop = 10;
            grid.style.display = DisplayStyle.None; // collapsed by default
            _metadataGridElement = grid;

            showMoreBtn.clicked += () =>
            {
                _metadataGridExpanded = !_metadataGridExpanded;
                grid.style.display = _metadataGridExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                showMoreBtn.text = _metadataGridExpanded ? "Show less ↑" : "Show more ↓";
            };
            panel.Add(showMoreBtn);

            // Left column: What's Inside + What's New
            var leftCol = new VisualElement();
            leftCol.AddToClassList("yucp-meta-col");
            leftCol.style.flexGrow = 1;
            leftCol.style.flexShrink = 1;
            leftCol.style.flexBasis = 0;
            leftCol.style.overflow = Overflow.Hidden;

            var insideSection = BuildWhatsInsideSection();
            if (insideSection != null)
            {
                insideSection.style.flexShrink = 0;
                insideSection.style.marginTop = 0;
                insideSection.style.paddingTop = 0;
                insideSection.style.borderTopWidth = 0;
                leftCol.Add(insideSection);
            }

            var releaseSection = BuildReleaseNotesSection();
            if (releaseSection != null)
            {
                releaseSection.style.flexShrink = 0;
                releaseSection.style.marginTop = 8;
                releaseSection.style.paddingTop = 8;
                leftCol.Add(releaseSection);
            }

            // Right column: From Creator + Verification
            var rightCol = new VisualElement();
            rightCol.AddToClassList("yucp-meta-col");
            rightCol.AddToClassList("yucp-meta-col-right");
            rightCol.style.flexGrow = 1;
            rightCol.style.flexShrink = 1;
            rightCol.style.flexBasis = 0;
            rightCol.style.overflow = Overflow.Hidden;

            var creatorSection = BuildCreatorSection();
            if (creatorSection != null)
            {
                creatorSection.style.flexShrink = 0;
                creatorSection.style.marginTop = 0;
                creatorSection.style.paddingTop = 0;
                creatorSection.style.borderTopWidth = 0;
                rightCol.Add(creatorSection);
            }

            _verificationStatusElement = CreateVerificationStatusElement();
            _verificationStatusElement.style.flexShrink = 0;
            rightCol.Add(_verificationStatusElement);

            // Create full license section (goes into contentsSection)
            _licenseSection = new VisualElement();
            _licenseSection.style.display = DisplayStyle.None;

            grid.Add(leftCol);
            grid.Add(rightCol);
            panel.Add(grid);

            // Gallery strip (full width, always visible)
            var gallery = BuildGalleryStrip();
            if (gallery != null)
            {
                gallery.style.flexShrink = 0;
                panel.Add(gallery);
            }

            return panel;
        }

        // ────────────────────────────────────────────────────────────
        //  Product panel helpers
        // ────────────────────────────────────────────────────────────

        private string BuildCtaSublabel()
        {
            var parts = new List<string>();
            if (_currentMetadata != null && _currentMetadata.totalFileSize > 0)
                parts.Add(FormatBytes(_currentMetadata.totalFileSize));
            if (_currentMetadata != null && _currentMetadata.totalFileCount > 0)
                parts.Add($"{_currentMetadata.totalFileCount} files");
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }

        private LicensePackageRequirement GetNextUnverifiedLicenseRequirement()
        {
            var reqs = _currentMetadata?.licensePackages;
            if (reqs == null)
            {
                return null;
            }

            foreach (var req in reqs)
            {
                if (req == null || string.IsNullOrEmpty(req.packageId))
                {
                    continue;
                }

                bool isVerified = LicenseVerificationService.GetCachedToken(req.packageId) != null ||
                    _verifiedLicensePackageIds.Contains(req.packageId);
                if (!isVerified &&
                    _licenseStates.TryGetValue(req.packageId, out var state) &&
                    state != null &&
                    state.isVerified)
                {
                    isVerified = true;
                }

                if (!isVerified)
                {
                    return req;
                }
            }

            return null;
        }

        private bool RequiresVerificationBeforeImport()
        {
            return GetNextUnverifiedLicenseRequirement() != null;
        }

        private string GetPrimaryImportButtonText()
        {
            if (_isResumeVerificationMode)
            {
                return RequiresVerificationBeforeImport() ? "Verify and Unlock" : "Unlock protected content";
            }

            bool isMultiStep = _packageImportWizardInstance != null &&
                PackageUtilityReflection.IsMultiStepWizard(_packageImportWizardInstance);
            bool isProjectStep = _packageImportWizardInstance != null &&
                PackageUtilityReflection.IsProjectSettingStep(_packageImportWizardInstance);

            if (isMultiStep && !isProjectStep)
            {
                return "Next";
            }

            return RequiresVerificationBeforeImport() ? "Verify and Import" : "Import";
        }

        private void RefreshPrimaryImportButton()
        {
            if (_importButton == null)
            {
                return;
            }

            string btnText = GetPrimaryImportButtonText();
            bool showBagIcon = btnText == "Verify and Import" || btnText == "Verify and Unlock";

            // Clear any previous content
            _importButton.text = string.Empty;
            _importButton.Clear();

            if (showBagIcon)
            {
                // Bag icon on the LEFT, label on the right
                var content = new VisualElement();
                content.style.flexDirection = FlexDirection.Row;
                content.style.alignItems = Align.Center;
                content.style.justifyContent = Justify.Center;

                Texture2D bag = AssetDatabase.LoadAssetAtPath<Texture2D>(ImporterBagIconPath)
                    ?? AssetDatabase.LoadAssetAtPath<Texture2D>(CreatorIdentityBagIconPath);
                if (bag != null)
                {
                    var iconImg = new Image { image = bag };
                    iconImg.AddToClassList("yucp-cta-icon");
                    iconImg.style.marginRight = 6;
                    content.Add(iconImg);
                }

                var lbl = new Label(btnText);
                lbl.style.flexShrink = 1;
                content.Add(lbl);

                _importButton.Add(content);
            }
            else
            {
                _importButton.text = btnText;
            }
        }

        private VisualElement BuildChipRow(Color? accent)
        {
            var allChips = new List<(string text, string variant)>();

            // Category
            if (_currentMetadata != null && !string.IsNullOrEmpty(_currentMetadata.category)
                && _currentMetadata.category != "None")
                allChips.Add((_currentMetadata.category, "yucp-chip-category"));

            // Version chip
            if (!string.IsNullOrEmpty(_currentMetadata?.version))
                allChips.Add((_currentMetadata.version, ""));

            // Platforms — include all (dynamic trimming will handle overflow)
            if (_currentMetadata?.supportedPlatforms != null)
            {
                foreach (var p in _currentMetadata.supportedPlatforms.Where(p => !string.IsNullOrEmpty(p)))
                    allChips.Add((p, "yucp-chip-platform"));
            }

            // Tags — include all
            if (_currentMetadata?.tags != null)
            {
                foreach (var tag in _currentMetadata.tags.Where(t => !string.IsNullOrEmpty(t)))
                    allChips.Add((tag, "yucp-chip-content"));
            }

            // System-derived safety / trust badges
            foreach (var safetyChip in GetDerivedSafetyChips())
                allChips.Add((safetyChip, ""));

            if (allChips.Count == 0) return null;

            var row = new VisualElement();
            row.AddToClassList("yucp-chip-row");

            // Build all chip elements
            var chipElements = new List<(VisualElement chip, string text)>();
            int idx = 0;
            foreach (var (text, variant) in allChips)
            {
                var chip = new VisualElement();
                chip.AddToClassList("yucp-chip");
                if (!string.IsNullOrEmpty(variant))
                    chip.AddToClassList(variant);
                chip.AddToClassList("yucp-chip-slide-up");
                chip.style.flexShrink = 0;

                var label = new Label(text);
                chip.Add(label);
                row.Add(chip);
                chipElements.Add((chip, text));

                int delay = 80 + idx * 40;
                chip.schedule.Execute(() => chip.RemoveFromClassList("yucp-chip-slide-up")).ExecuteLater(delay);
                idx++;
            }

            // Overflow chip — hidden until trim calculation reveals it is needed
            var overflowChip = new VisualElement();
            overflowChip.AddToClassList("yucp-chip");
            overflowChip.AddToClassList("yucp-chip-overflow");
            overflowChip.style.flexShrink = 0;
            overflowChip.style.display = DisplayStyle.None;
            var overflowLabel = new Label("+0");
            overflowChip.Add(overflowLabel);
            row.Add(overflowChip);

            // Hidden texts — shared between trim and popup
            var hiddenTexts = new List<string>();
            overflowChip.RegisterCallback<MouseEnterEvent>(_ => ShowChipTooltip(overflowChip, hiddenTexts));
            overflowChip.RegisterCallback<MouseLeaveEvent>(_ => HideChipTooltip());

            // Natural widths cached from the first layout pass when all chips are visible.
            // We use cached values so trim calculations never need to reset display and wait for re-layout.
            var naturalWidths = new float[chipElements.Count]; // chip.layout.width + margin
            var naturalOverflowWidth = new float[] { 0f };
            var cached = new bool[] { false };
            var lastWidth = new float[] { -1f };

            row.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float w = row.resolvedStyle.width;
                if (w <= 0f) return;

                // Cache natural widths once — only possible when all chips are visible
                if (!cached[0])
                {
                    bool allValid = true;
                    for (int i = 0; i < chipElements.Count; i++)
                    {
                        float cw = chipElements[i].chip.layout.width;
                        if (cw <= 0f) { allValid = false; break; }
                        // layout.width is the full outer size; add the 6px CSS margin-right
                        naturalWidths[i] = cw + 6f;
                    }
                    // Also cache the overflow chip's natural width (it needs to be visible briefly to measure)
                    // Use a reasonable fallback — we'll refine once measured
                    if (allValid)
                    {
                        cached[0] = true;
                        // Measure overflow chip width: make it visible for one frame
                        overflowChip.style.display = DisplayStyle.Flex;
                        overflowChip.schedule.Execute(() =>
                        {
                            float ow = overflowChip.layout.width;
                            naturalOverflowWidth[0] = (ow > 0f) ? ow + 6f : 52f;
                            overflowChip.style.display = DisplayStyle.None;
                            TrimChipsToWidth(chipElements, overflowChip, naturalWidths, naturalOverflowWidth[0], w, hiddenTexts);
                            lastWidth[0] = w;
                        }).ExecuteLater(16); // one frame
                    }
                    return;
                }

                if (Mathf.Abs(w - lastWidth[0]) < 2f) return;
                lastWidth[0] = w;

                TrimChipsToWidth(chipElements, overflowChip, naturalWidths, naturalOverflowWidth[0], w, hiddenTexts);
            });

            return row;
        }

        private static void TrimChipsToWidth(
            List<(VisualElement chip, string text)> chipElements,
            VisualElement overflowChip,
            float[] naturalWidths,
            float overflowReserve,
            float rowWidth,
            List<string> hiddenTexts)
        {
            if (overflowReserve <= 0f) overflowReserve = 52f;
            hiddenTexts.Clear();

            // Find how many chips fit before we'd need the "+N" chip
            float used = 0f;
            int fitsAll = chipElements.Count; // how many fit without overflow chip

            for (int i = 0; i < chipElements.Count; i++)
            {
                used += naturalWidths[i];
                if (used > rowWidth)
                {
                    fitsAll = i; // first chip that didn't fit
                    break;
                }
            }

            if (fitsAll == chipElements.Count)
            {
                // Everything fits — show all, hide overflow
                foreach (var (chip, _) in chipElements)
                    chip.style.display = DisplayStyle.Flex;
                overflowChip.style.display = DisplayStyle.None;
                return;
            }

            // Walk back until there's room for the overflow chip
            int visibleCount = fitsAll;
            while (visibleCount > 0)
            {
                float w2 = 0f;
                for (int i = 0; i < visibleCount; i++) w2 += naturalWidths[i];
                if (rowWidth - w2 >= overflowReserve) break;
                visibleCount--;
            }
            if (visibleCount < 1) visibleCount = 1;

            // Apply visibility
            for (int i = 0; i < chipElements.Count; i++)
            {
                var (chip, text) = chipElements[i];
                if (i < visibleCount)
                {
                    chip.style.display = DisplayStyle.Flex;
                }
                else
                {
                    chip.style.display = DisplayStyle.None;
                    hiddenTexts.Add(text);
                }
            }

            var lbl = overflowChip.Q<Label>();
            if (lbl != null) lbl.text = $"+{hiddenTexts.Count}";
            overflowChip.style.display = DisplayStyle.Flex;
        }

        private void ShowChipTooltip(VisualElement anchor, List<string> texts)
        {
            if (texts == null || texts.Count == 0) return;

            // Create lazily and attach to rootVisualElement for correct z-ordering
            if (_chipTooltipPopup == null)
            {
                _chipTooltipPopup = new VisualElement();
                _chipTooltipPopup.AddToClassList("yucp-chip-tooltip");
                _chipTooltipPopup.pickingMode = PickingMode.Ignore;
                _chipTooltipPopup.style.position = Position.Absolute;
                _chipTooltipPopup.style.display = DisplayStyle.None;
                rootVisualElement.Add(_chipTooltipPopup);
            }

            // Rebuild content
            _chipTooltipPopup.Clear();
            foreach (var text in texts)
            {
                var row = new VisualElement();
                row.AddToClassList("yucp-chip-tooltip-row");
                var dot = new Label("·");
                dot.AddToClassList("yucp-chip-tooltip-dot");
                row.Add(dot);
                var lbl = new Label(text);
                lbl.AddToClassList("yucp-chip-tooltip-label");
                row.Add(lbl);
                _chipTooltipPopup.Add(row);
            }

            _chipTooltipPopup.style.display = DisplayStyle.Flex;

            // Position above the anchor chip, aligned to its left edge
            _chipTooltipPopup.schedule.Execute(() =>
            {
                var anchorBounds = anchor.worldBound;
                var rootBounds = rootVisualElement.worldBound;
                float popupWidth = _chipTooltipPopup.resolvedStyle.width;
                float windowWidth = rootVisualElement.resolvedStyle.width;

                float left = anchorBounds.x - rootBounds.x;
                if (left + popupWidth > windowWidth - 8f)
                    left = windowWidth - popupWidth - 8f;
                if (left < 8f) left = 8f;

                // "bottom" positions relative to rootVisualElement bottom edge
                float bottomFromRoot = (rootBounds.height) - (anchorBounds.y - rootBounds.y) + 6f;

                _chipTooltipPopup.style.left = left;
                _chipTooltipPopup.style.bottom = bottomFromRoot;
                _chipTooltipPopup.style.top = StyleKeyword.Auto;
            }).ExecuteLater(1);
        }

        private void HideChipTooltip()
        {
            if (_chipTooltipPopup != null)
                _chipTooltipPopup.style.display = DisplayStyle.None;
        }

        private VisualElement BuildWhatsInsideSection()
        {
            bool hasBreakdown = _currentMetadata?.assetBreakdown != null && _currentMetadata.assetBreakdown.Count > 0;
            bool hasDeps = _currentMetadata?.dependencies != null && _currentMetadata.dependencies.Count > 0;
            bool hasUnityVer = !string.IsNullOrEmpty(_currentMetadata?.minimumUnityVersion);

            if (!hasBreakdown && !hasDeps && !hasUnityVer) return null;

            var section = new VisualElement();
            section.AddToClassList("yucp-info-section-block");

            var title = new Label("WHAT'S INSIDE");
            title.AddToClassList("yucp-info-section-title");
            section.Add(title);

            // Asset breakdown: "1 Prefab · 248 Textures · 17 Materials"
            if (hasBreakdown)
            {
                var statsRow = new VisualElement();
                statsRow.AddToClassList("yucp-info-section-body");

                for (int i = 0; i < _currentMetadata.assetBreakdown.Count; i++)
                {
                    var ab = _currentMetadata.assetBreakdown[i];
                    if (i > 0)
                    {
                        var sep = new Label("·");
                        sep.AddToClassList("yucp-asset-stat-separator");
                        statsRow.Add(sep);
                    }
                    var stat = new Label($"{ab.count} {ab.type}{(ab.count != 1 ? "s" : "")}");
                    stat.AddToClassList("yucp-asset-stat");
                    statsRow.Add(stat);
                }
                section.Add(statsRow);
            }

            // Dependencies (Dictionary<string, string>: name → version)
            if (hasDeps)
            {
                var depNames = new List<string>();
                foreach (var kvp in _currentMetadata.dependencies)
                {
                    depNames.Add(kvp.Key);
                }
                if (depNames.Count > 0)
                {
                    var reqLabel = new Label("Requires: " + string.Join(" · ", depNames));
                    reqLabel.AddToClassList("yucp-requirement-text");
                    section.Add(reqLabel);
                }
            }

            // Unity version
            if (hasUnityVer)
            {
                var unityLabel = new Label($"Unity {_currentMetadata.minimumUnityVersion}+");
                unityLabel.AddToClassList("yucp-requirement-text");
                section.Add(unityLabel);
            }

            return section;
        }

        private IEnumerable<string> GetDerivedSafetyChips()
        {
            var chips = new List<string>();

            bool hasAssetBreakdown = _currentMetadata?.assetBreakdown != null && _currentMetadata.assetBreakdown.Count > 0;
            bool hasAssemblies = hasAssetBreakdown && _currentMetadata.assetBreakdown.Any(ab =>
                string.Equals(ab.type, "Assembly", StringComparison.OrdinalIgnoreCase));

            if (hasAssetBreakdown)
            {
                chips.Add(hasAssemblies ? "Contains DLLs" : "No DLLs");
            }

            if (_currentMetadata?.dependencies != null && _currentMetadata.dependencies.Count > 0)
            {
                chips.Add("Dependencies Required");
            }

            if (_currentMetadata?.licensePackages != null && _currentMetadata.licensePackages.Count > 0)
            {
                chips.Add("Protected Assets");
            }

            if (_isPackageSigned && _verificationResult?.valid == true)
            {
                chips.Add("Verified Package");
            }

            return chips;
        }

        private VisualElement BuildCreatorSection()
        {
            bool hasNote = !string.IsNullOrEmpty(_currentMetadata?.creatorNote);
            bool hasLinks = _currentMetadata?.productLinks != null && _currentMetadata.productLinks.Count > 0;

            if (!hasNote && !hasLinks) return null;

            var section = new VisualElement();
            section.AddToClassList("yucp-info-section-block");

            var title = new Label("FROM THE CREATOR");
            title.AddToClassList("yucp-info-section-title");
            section.Add(title);

            if (hasNote)
            {
                var noteLabel = new Label($"\"{_currentMetadata.creatorNote}\"");
                noteLabel.AddToClassList("yucp-creator-note");
                section.Add(noteLabel);
            }

            if (hasLinks)
            {
                var linksRow = new VisualElement();
                linksRow.AddToClassList("yucp-creator-links");

                foreach (var link in _currentMetadata.productLinks)
                {
                    if (string.IsNullOrEmpty(link.url)) continue;

                    var linkBtn = new Button(() => Application.OpenURL(link.url));
                    linkBtn.AddToClassList("yucp-creator-link-button");
                    linkBtn.tooltip = string.IsNullOrEmpty(link.label) ? link.url : $"{link.label}\n{link.url}";

                    var linkIcon = new Image();
                    Texture2D ico = link.GetDisplayIcon() ?? GetPlaceholderTexture();
                    linkIcon.image = ico;
                    linkIcon.style.width = 28;
                    linkIcon.style.height = 28;
                    linkBtn.Add(linkIcon);

                    linksRow.Add(linkBtn);
                }
                section.Add(linksRow);
            }

            return section;
        }

        private VisualElement BuildReleaseNotesSection()
        {
            if (string.IsNullOrEmpty(_currentMetadata?.releaseNotes)) return null;

            var section = new VisualElement();
            section.AddToClassList("yucp-info-section-block");

            string versionSuffix = !string.IsNullOrEmpty(_currentMetadata?.version) ? $" (v{_currentMetadata.version})" : "";
            var title = new Label($"WHAT'S NEW{versionSuffix}");
            title.AddToClassList("yucp-info-section-title");
            section.Add(title);

            var notes = new Label(_currentMetadata.releaseNotes);
            notes.AddToClassList("yucp-release-notes-text");
            section.Add(notes);

            return section;
        }

        private VisualElement BuildGalleryStrip()
        {
            if (_currentMetadata?.galleryImages == null || _currentMetadata.galleryImages.Count == 0)
                return null;

            var images = _currentMetadata.galleryImages.Where(t => t != null).ToList();
            if (images.Count == 0) return null;

            var strip = new VisualElement();
            strip.AddToClassList("yucp-gallery-strip");
            _galleryStripElement = strip;

            for (int i = 0; i < images.Count; i++)
            {
                var capturedIndex = i;
                var capturedTex = images[i];

                var thumb = new VisualElement();
                thumb.AddToClassList("yucp-gallery-thumb");
                thumb.style.backgroundImage = new StyleBackground(capturedTex);

                thumb.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_selectedGalleryIndex == capturedIndex)
                    {
                        // Clicking the already-selected thumb → deselect, restore original banner
                        _selectedGalleryIndex = -1;
                        foreach (var child in strip.Children())
                            child.RemoveFromClassList("yucp-gallery-thumb-selected");
                        if (_originalBannerTexture != null)
                            SetBannerImageWithTransition(_originalBannerTexture);
                    }
                    else
                    {
                        _selectedGalleryIndex = capturedIndex;
                        foreach (var child in strip.Children())
                            child.RemoveFromClassList("yucp-gallery-thumb-selected");
                        thumb.AddToClassList("yucp-gallery-thumb-selected");
                        SetBannerImageWithTransition(capturedTex);
                    }
                });

                strip.Add(thumb);
            }

            // Auto-carousel: advance every 5 seconds (only if there are multiple images)
            if (images.Count > 1)
            {
                _galleryCarouselSchedule?.Pause();
                _galleryCarouselSchedule = strip.schedule
                    .Execute(() => AdvanceGalleryCarousel(strip, images))
                    .Every(10000)
                    .StartingIn(10000);
            }

            return strip;
        }

        private void SetBannerImageWithTransition(Texture2D newTexture)
        {
            if (newTexture == null || _bannerImageContainer == null) return;

            if (_bannerFadeOverlay == null)
            {
                _bannerImageContainer.style.backgroundImage = new StyleBackground(newTexture);
                return;
            }

            // Set new image on the overlay and fade it in
            _bannerFadeOverlay.style.backgroundImage = new StyleBackground(newTexture);
            _bannerFadeOverlay.AddToClassList("yucp-fade-active");

            // After the fade completes: swap to background layer and reset overlay
            _bannerFadeOverlay.schedule.Execute(() =>
            {
                _bannerImageContainer.style.backgroundImage = new StyleBackground(newTexture);
                _bannerFadeOverlay.RemoveFromClassList("yucp-fade-active");
            }).ExecuteLater(BannerFadeDurationMs);
        }

        private void AdvanceGalleryCarousel(VisualElement strip, List<Texture2D> images)
        {
            if (strip == null || images == null || images.Count == 0 || _bannerImageContainer == null) return;

            int nextIndex = (_selectedGalleryIndex + 1) % images.Count;
            _selectedGalleryIndex = nextIndex;

            foreach (var child in strip.Children())
                child.RemoveFromClassList("yucp-gallery-thumb-selected");

            var thumbs = strip.Children().ToList();
            if (nextIndex < thumbs.Count)
                thumbs[nextIndex].AddToClassList("yucp-gallery-thumb-selected");

            SetBannerImageWithTransition(images[nextIndex]);
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
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
                return null;

            // Only show icon if package is signed
            if (!_isPackageSigned || _verificationResult == null)
                return null;

            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("yucp-verification-icon");
            iconContainer.style.marginLeft = 6;
            iconContainer.style.alignSelf = Align.Center;

            if (_verificationResult.valid)
            {
                // Use VerifiedBadge.png — falls back to a text checkmark
                Texture2D badgeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(VerifiedBadgePath)
                    ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.importer/Editor/PackageManager/Resources/Verified.png");

                // Build a friendly, easy-to-read tooltip
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("✦ Signed Package");
                sb.AppendLine();
                sb.AppendLine("This package has been digitally signed by its publisher.");
                sb.AppendLine("That means YUCP has confirmed who made it — and that nothing");
                sb.AppendLine("has been changed since it was published.");
                sb.AppendLine();

                if (!string.IsNullOrEmpty(_verificationResult.publisherId))
                {
                    sb.AppendLine($"Publisher:  {_verificationResult.publisherId}");
                    sb.AppendLine();
                }

                sb.AppendLine("What was checked:");
                sb.AppendLine("  • The publisher's identity certificate is valid and trusted");
                sb.AppendLine("  • The package contents haven't been altered");
                sb.AppendLine("  • The digital signature matches the publisher's certificate");
                sb.AppendLine();
                sb.Append("You can import this package with confidence.");

                string tooltipText = sb.ToString();

                if (badgeTexture != null)
                {
                    var img = new Image { image = badgeTexture };
                    img.style.width = 18;
                    img.style.height = 18;
                    img.style.flexShrink = 0;
                    img.tooltip = tooltipText;
                    iconContainer.Add(img);
                }
                else
                {
                    var check = new Label("✦");
                    check.style.fontSize = 14;
                    check.style.color = new Color(0.3f, 0.85f, 0.5f);
                    check.tooltip = tooltipText;
                    iconContainer.Add(check);
                }
            }
            else
            {
                // Signed but verification failed — warn the user clearly
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("⚠ Signature Verification Failed");
                sb.AppendLine();
                sb.AppendLine("This package claims to be signed, but the signature");
                sb.AppendLine("could not be verified. This may mean:");
                sb.AppendLine();
                sb.AppendLine("  • The package was modified after signing");
                sb.AppendLine("  • The publisher's certificate is invalid or expired");
                sb.AppendLine("  • The signature data is corrupted");
                sb.AppendLine();

                if (_verificationResult.errors != null && _verificationResult.errors.Count > 0)
                {
                    sb.AppendLine("Details:");
                    foreach (var error in _verificationResult.errors)
                        sb.AppendLine($"  • {error}");
                    sb.AppendLine();
                }

                sb.Append("Do not import unless you trust the source directly.");

                string tooltipText = sb.ToString();

                var warningLabel = new Label("⚠");
                warningLabel.style.fontSize = 14;
                warningLabel.style.color = new Color(0.9f, 0.65f, 0.1f);
                warningLabel.tooltip = tooltipText;
                iconContainer.Add(warningLabel);
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

            _treeView = new PackageItemTreeView(_treeScrollView);

            // Wrap the tree in a ScrollView so only the asset list scrolls
            _treeScrollWrapper = new ScrollView(ScrollViewMode.Vertical);
            _treeScrollWrapper.name = "tree-wrapper";
            _treeScrollWrapper.AddToClassList("yucp-tree-scroll-wrapper");
            _treeScrollWrapper.style.flexGrow = 1;
            _treeScrollWrapper.style.flexShrink = 1;
            _treeScrollWrapper.style.minHeight = 0;
            _treeScrollWrapper.Add(_treeScrollView);
            section.Add(_treeScrollWrapper);

            ShowSampleTree();
            UpdateConflictModeSection();

            return section;
        }

        private void UpdateInstallerLayout()
        {
            if (_metadataSection == null || _contentsSection == null || _detailsToggleButton == null)
            {
                return;
            }

            // Banner and metadata: shown in summary mode, hidden in details mode
            if (_bannerSection != null)
                _bannerSection.style.display = _detailsExpanded ? DisplayStyle.None : DisplayStyle.Flex;
            if (_metadataSection != null)
                _metadataSection.style.display = _detailsExpanded ? DisplayStyle.None : DisplayStyle.Flex;

            // Contents section: shown in details mode
            if (_contentsSection != null)
            {
                _contentsSection.style.display = _detailsExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                if (_detailsExpanded)
                {
                    _contentsSection.style.flexGrow = 1;
                    _contentsSection.style.flexShrink = 1;
                    _contentsSection.style.overflow = Overflow.Hidden;
                }
            }

            if (_treeScrollWrapper != null)
            {
                if (_detailsExpanded)
                {
                    _treeScrollWrapper.style.flexGrow = 1;
                    _treeScrollWrapper.style.flexShrink = 1;
                    _treeScrollWrapper.style.minHeight = 0;
                    _treeScrollWrapper.style.maxHeight = StyleKeyword.None;
                }
                else
                {
                    _treeScrollWrapper.style.flexGrow = 0;
                    _treeScrollWrapper.style.flexShrink = 0;
                    _treeScrollWrapper.style.minHeight = 260;
                }
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

        /// <summary>
        /// Updates the visible label on a button built by <see cref="PopulateCreatorIdentityButton"/>.
        /// Setting <c>button.text</c> directly would overlap with the child Label element, so we
        /// clear the outer text and update the first child Label instead.
        /// </summary>
        private static void UpdateButtonLabel(Button button, string text)
        {
            if (button == null) return;
            button.text = string.Empty;
            var label = button.Q<Label>();
            if (label != null)
                label.text = text;
            else
                button.text = text;
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
            _licenseSection.Add(BuildVerificationServerNotice());

            // Pre-compute verification states
            foreach (var req in reqs)
            {
                if (req == null || string.IsNullOrEmpty(req.packageId)) continue;
                var cachedToken = LicenseVerificationService.GetCachedToken(req.packageId);
                if (cachedToken != null)
                {
                    _verifiedLicensePackageIds.Add(req.packageId);
                }
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

                var buyerNote = BuildBuyerFlowNote(
                    GetCreatorIdentitySignedOutPrimaryText(),
                    GetCreatorIdentitySignedOutSecondaryText());
                unifiedBlock.Add(buyerNote);

                var storefrontActions = BuildStorefrontActionsRow();
                if (storefrontActions != null)
                {
                    unifiedBlock.Add(storefrontActions);
                }

                var signInBtn = new Button(OnCreatorIdentitySignInClicked)
                {
                    text = GetCreatorIdentitySignInButtonLabel()
                };
                signInBtn.SetEnabled(!_isCreatorIdentitySigningIn);
                signInBtn.AddToClassList("lgate-solid-btn");
                signInBtn.style.marginTop = 10;
                PopulateCreatorIdentityButton(signInBtn, GetCreatorIdentitySignInButtonLabel());
                unifiedBlock.Add(signInBtn);

                _licenseSection.Add(unifiedBlock);
            }
            else
            {
                // ── SIGNED IN: "Connected as X | Sign out" → per-package rows ─────
                var idRow = new VisualElement();
                idRow.AddToClassList("lgate-id-row");

                var connectedLabel = new Label($"Signed in as {creatorName}");
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
                    var verificationRequirements = BuildVerificationRequirements(req);

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
                        block.Add(BuildBuyerFlowNote(
                            GetCreatorIdentityVerifyPrimaryText(),
                            "If you bought on a different store than the one currently linked, the hosted verification page will show the next action there."));

                        var storefrontActions = BuildStorefrontActionsRow();
                        if (storefrontActions != null)
                        {
                            block.Add(storefrontActions);
                        }

                        var actionRow = new VisualElement();
                        actionRow.AddToClassList("lgate-discord-row");

                        string buttonLabel = GetDiscordVerificationButtonLabel(true);
                        var verifyBtn = new Button { text = buttonLabel };
                        verifyBtn.AddToClassList("lgate-discord-btn");
                        PopulateCreatorIdentityButton(verifyBtn, buttonLabel);
                        verifyBtn.SetEnabled(!_isCreatorIdentitySigningIn && verificationRequirements.Length > 0);
                        state.verifyButton = verifyBtn;
                        verifyBtn.clicked += () => OnVerifyInBrowserClicked(req, state, verifyBtn, verificationRequirements);
                        actionRow.Add(verifyBtn);
                        block.Add(actionRow);

                        if (verificationRequirements.Length == 0)
                        {
                            block.Add(BuildBuyerFlowNote(
                                "This package is missing verification metadata.",
                                "Ask the package creator to republish it with hosted verification requirements."));
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

        private static VerificationIntentService.VerificationRequirement[] BuildVerificationRequirements(LicensePackageRequirement req)
        {
            if (req == null)
            {
                return Array.Empty<VerificationIntentService.VerificationRequirement>();
            }

            var requirements = new List<VerificationIntentService.VerificationRequirement>();
            if (!string.IsNullOrEmpty(req.creatorAuthUserId) && !string.IsNullOrEmpty(req.productId))
            {
                requirements.Add(new VerificationIntentService.VerificationRequirement
                {
                    methodKey = "existing-entitlement",
                    providerKey = "yucp",
                    kind = "existing_entitlement",
                    title = "Check your connected YUCP access",
                    description = "Use the signed-in YUCP buyer account to check whether this package is already linked to your purchases.",
                    creatorAuthUserId = req.creatorAuthUserId,
                    productId = req.productId,
                });
            }

            if (!string.IsNullOrEmpty(req.gumroadPermalink))
            {
                requirements.Add(new VerificationIntentService.VerificationRequirement
                {
                    methodKey = "gumroad-oauth",
                    providerKey = "gumroad",
                    kind = "buyer_provider_link",
                    title = "Gumroad account",
                    description = "Sign in with your Gumroad account to verify your purchase.",
                    creatorAuthUserId = req.creatorAuthUserId,
                    productId = req.productId,
                    providerProductRef = req.gumroadPermalink,
                });
                requirements.Add(new VerificationIntentService.VerificationRequirement
                {
                    methodKey = "gumroad-license",
                    providerKey = "gumroad",
                    kind = "manual_license",
                    title = "Verify a Gumroad license",
                    description = "Open the hosted verification page to enter your Gumroad purchase proof securely.",
                    providerProductRef = req.gumroadPermalink,
                });
            }

            if (!string.IsNullOrEmpty(req.jinxxyProductId))
            {
                requirements.Add(new VerificationIntentService.VerificationRequirement
                {
                    methodKey = "jinxxy-license",
                    providerKey = "jinxxy",
                    kind = "manual_license",
                    title = "Verify a Jinxxy license",
                    description = "Open the hosted verification page to enter your Jinxxy purchase proof securely.",
                    providerProductRef = req.jinxxyProductId,
                });
            }

            return requirements.ToArray();
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

            var body = new Label("Import installs the package now. Verify your purchase to unlock licensed derived assets on this machine.");
            body.AddToClassList("lgate-body");
            block.Add(body);

            if (!state.isVerified)
            {
                string noteText = creatorSignedIn
                    ? GetCreatorIdentityVerifyPrimaryText()
                    : GetCreatorIdentitySignedOutPrimaryText();
                block.Add(BuildBuyerFlowNote(noteText, "If you do not own this package yet, open the storefront below."));

                var storefrontActions = BuildStorefrontActionsRow();
                if (storefrontActions != null)
                {
                    block.Add(storefrontActions);
                }
            }

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
                var verificationRequirements = BuildVerificationRequirements(req);

                var verifyBtn = new Button();
                verifyBtn.AddToClassList("lgate-discord-btn");
                PopulateCreatorIdentityButton(verifyBtn, GetDiscordVerificationButtonLabel(creatorSignedIn));
                verifyBtn.SetEnabled(!_isCreatorIdentitySigningIn && verificationRequirements.Length > 0);
                verifyBtn.clicked += () => OnVerifyInBrowserClicked(req, state, verifyBtn, verificationRequirements);
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

        private void OnVerifyInBrowserClicked(
            LicensePackageRequirement req,
            LicenseVerificationState state,
            Button verifyBtn,
            VerificationIntentService.VerificationRequirement[] verificationRequirements)
        {
            bool isSignedIn = CreatorIdentityOAuthService.IsSignedIn();
            Debug.Log($"[YUCP PackageManager] OnVerifyInBrowserClicked: isSignedIn={isSignedIn}, signingIn={_isCreatorIdentitySigningIn}, packageId='{req?.packageId}', serverUrl='{GetLicenseServerUrl()}'");

            if (!isSignedIn)
            {
                Debug.Log("[YUCP PackageManager] Not signed in — starting creator identity sign-in flow");
                // Capture everything needed for intent creation now, before BuildLicenseSection
                // destroys the current button references.
                string serverUrlForVerify = GetLicenseServerUrl();
                string packageIdForVerify = req.packageId;
                string packageNameForVerify = req.packageName;
                var requirementsForVerify = verificationRequirements;
                Action<string> jwtCallback = jwt =>
                {
                    Debug.Log($"[YUCP PackageManager] Browser verification succeeded for packageId='{packageIdForVerify}'");
                    state.isVerified = true;
                    _verifiedLicensePackageIds.Add(packageIdForVerify);
                    EditorApplication.delayCall += () =>
                    {
                        BuildLicenseSection();
                        UpdateImportButtonEnabled();
                        if (_pendingImportAfterVerification)
                            OnImportClicked();
                    };
                };
                Action<string> errCallback = err =>
                {
                    Debug.LogWarning($"[YUCP PackageManager] Browser verification failed for packageId='{packageIdForVerify}': {err}");
                    PendingVerifyRelay.Cancel();
                    EditorApplication.delayCall += () =>
                    {
                        _pendingImportAfterVerification = false;
                        if (LicenseVerificationService.IsCreatorIdentityReauthenticationError(err))
                        {
                            _creatorIdentityNeedsReauthentication = true;
                        }
                        BuildLicenseSection();
                        UpdateImportButtonEnabled();
                        if (!LicenseVerificationService.IsCreatorIdentityReauthenticationError(err))
                        {
                            EditorUtility.DisplayDialog("Purchase Verification Failed", $"{err}", "OK");
                        }
                    };
                };
                // Start verification immediately after sign-in succeeds so the relay can receive
                // the verification URL before the delayed UI refresh runs.
                BeginCreatorIdentitySignIn(backgroundOnSuccess: () =>
                {
                    VerificationIntentService.s_openUrlOverride = url => PendingVerifyRelay.SetVerifyUrl(url);
                    VerificationIntentService.VerifyInBrowserAsync(
                        serverUrlForVerify, packageIdForVerify, packageNameForVerify,
                        requirementsForVerify, jwtCallback, errCallback);
                });
                return;
            }

            if (verificationRequirements == null || verificationRequirements.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Verification Unavailable",
                    "This package does not currently expose any hosted verification methods.",
                    "OK");
                return;
            }

            Debug.Log($"[YUCP PackageManager] Starting browser verification for packageId='{req.packageId}', serverUrl='{GetLicenseServerUrl()}'");
            verifyBtn.SetEnabled(false);
            UpdateButtonLabel(verifyBtn, ReferenceEquals(verifyBtn, _importButton) ? "Verifying..." : "Opening browser...");

            string serverUrl = GetLicenseServerUrl();

            VerificationIntentService.VerifyInBrowserAsync(
                serverUrl,
                req.packageId,
                req.packageName,
                verificationRequirements,
                jwt =>
                {
                    Debug.Log($"[YUCP PackageManager] Browser verification succeeded for packageId='{req.packageId}'");
                    state.isVerified = true;
                    _verifiedLicensePackageIds.Add(req.packageId);
                    EditorApplication.delayCall += () =>
                    {
                        BuildLicenseSection();
                        UpdateImportButtonEnabled();

                        if (_pendingImportAfterVerification)
                        {
                            OnImportClicked();
                        }
                    };
                },
                err =>
                {
                    Debug.LogWarning($"[YUCP PackageManager] Browser verification failed for packageId='{req.packageId}': {err}");
                    EditorApplication.delayCall += () =>
                    {
                        _pendingImportAfterVerification = false;

                        if (LicenseVerificationService.IsCreatorIdentityReauthenticationError(err))
                        {
                            _creatorIdentityNeedsReauthentication = true;
                            BuildLicenseSection();
                            UpdateImportButtonEnabled();
                            return;
                        }

                        verifyBtn.SetEnabled(true);
                        if (ReferenceEquals(verifyBtn, _importButton))
                        {
                            UpdateImportButtonEnabled();
                        }
                        else
                        {
                            PopulateCreatorIdentityButton(verifyBtn, GetDiscordVerificationButtonLabel(CreatorIdentityOAuthService.IsSignedIn()));
                        }
                        EditorUtility.DisplayDialog("Purchase Verification Failed",
                            $"{err}", "OK");
                    };
                });
        }

        private void UpdateImportButtonEnabled()
        {
            if (_importButton == null) return;
            bool hasUnverifiedLicense = RequiresVerificationBeforeImport();

            if (_pendingImportAfterVerification)
            {
                _importButton.SetEnabled(false);
                _importButton.tooltip = _isResumeVerificationMode
                    ? "Complete purchase verification in your browser to continue unlocking the protected content."
                    : "Complete purchase verification in your browser to continue importing.";
                string statusText = _isCreatorIdentitySigningIn
                    ? "Signing in..."
                    : "Waiting for browser verification...";
                // Clear any icon content then update via helper to avoid text overlap
                _importButton.Clear();
                UpdateButtonLabel(_importButton, _isCreatorIdentitySigningIn ? "Signing in..." : "Verifying...");
                SetVerifyStatusLabel(statusText);
                return;
            }

            SetVerifyStatusLabel(null);
            _importButton.SetEnabled(true);
            if (_isResumeVerificationMode)
            {
                _importButton.tooltip = hasUnverifiedLicense
                    ? "Verify your purchase and then unlock the protected content in one step."
                    : "Unlock the protected content for this package on this machine.";
            }
            else
            {
                _importButton.tooltip = hasUnverifiedLicense
                    ? "Verify your purchase and then import the package in one step."
                    : string.Empty;
            }
            RefreshPrimaryImportButton();
        }

        private void SetVerifyStatusLabel(string text)
        {
            if (_verifyStatusLabel == null) return;
            if (string.IsNullOrEmpty(text))
            {
                _verifyStatusLabel.text = string.Empty;
                _verifyStatusLabel.style.display = DisplayStyle.None;
            }
            else
            {
                _verifyStatusLabel.text = text;
                _verifyStatusLabel.style.display = DisplayStyle.Flex;
            }
        }

        private void OnCreatorIdentitySignInClicked()
        {
            BeginCreatorIdentitySignIn();
        }

        private void BeginCreatorIdentitySignIn(Action onSuccess = null, Action backgroundOnSuccess = null)
        {
            if (_isCreatorIdentitySigningIn)
            {
                Debug.Log("[YUCP PackageManager] BeginCreatorIdentitySignIn: already signing in, ignoring duplicate call");
                return;
            }

            string serverUrl = GetLicenseServerUrl();
            Debug.Log($"[YUCP PackageManager] BeginCreatorIdentitySignIn: serverUrl='{serverUrl}'");

            if (string.IsNullOrEmpty(serverUrl))
            {
                Debug.LogError("[YUCP PackageManager] BeginCreatorIdentitySignIn: server URL is empty — cannot sign in. Check Package Manager settings.");
                EditorUtility.DisplayDialog("Sign-In Failed", "The verification server URL is not configured. Please check the YUCP Package Manager settings.", "OK");
                return;
            }

            _isCreatorIdentitySigningIn = true;
            _creatorIdentityNeedsSignInRetry = false;
            BuildLicenseSection();
            UpdateImportButtonEnabled();

            // If a chained action follows sign-in (e.g. verification), set up a relay so the
            // OAuth success page in the browser auto-redirects to the verification URL rather
            // than requiring the user to manually return to Unity.
            if (backgroundOnSuccess != null || onSuccess != null)
            {
                string relayUrl = PendingVerifyRelay.Start();
                CreatorIdentityOAuthService.s_pendingVerifyRelayUrl = relayUrl;
            }

            Debug.Log("[YUCP PackageManager] Opening browser for Creator Identity sign-in...");
            CreatorIdentityOAuthService.SignInAsync(
                serverUrl,
                onSuccess: () =>
                {
                    Debug.Log("[YUCP PackageManager] Creator Identity sign-in succeeded");

                    // Fire the chained verification handoff before the delayed UI refresh so the
                    // relay can receive the verification URL immediately.
                    backgroundOnSuccess?.Invoke();

                    EditorApplication.delayCall += () =>
                    {
                        _isCreatorIdentitySigningIn = false;
                        _creatorIdentityNeedsReauthentication = false;
                        _creatorIdentityNeedsSignInRetry = false;
                        if (backgroundOnSuccess != null)
                        {
                            // Verification has already been kicked off; just refresh the UI.
                            UpdateImportButtonEnabled();
                        }
                        else if (onSuccess != null)
                        {
                            // Wire the relay to receive the intent URL once it's created,
                            // so the browser tab redirects instead of a new tab opening.
                            VerificationIntentService.s_openUrlOverride =
                                url => PendingVerifyRelay.SetVerifyUrl(url);

                            // Proceed directly into the chained action (e.g. verification) while
                            // the original verifyBtn/state refs are still valid. The chained
                            // callback's own success/failure paths call BuildLicenseSection().
                            UpdateImportButtonEnabled();
                            onSuccess.Invoke();
                        }
                        else
                        {
                            BuildLicenseSection();
                            UpdateImportButtonEnabled();
                        }
                    };
                },
                focusUnityOnSuccess: backgroundOnSuccess == null && onSuccess == null,
                onError: err =>
                {
                    Debug.LogWarning($"[YUCP PackageManager] Creator Identity sign-in failed: {err}");
                    PendingVerifyRelay.Cancel();
                    VerificationIntentService.s_openUrlOverride = null;
                    EditorApplication.delayCall += () =>
                    {
                        _isCreatorIdentitySigningIn = false;
                        if (!CreatorIdentityOAuthService.IsUnityOAuthScopeRejectionError(err))
                        {
                            _creatorIdentityNeedsSignInRetry = true;
                        }
                        BuildLicenseSection();
                        UpdateImportButtonEnabled();
                        EditorUtility.DisplayDialog("Sign-In Failed",
                            $"Could not complete sign-in: {err}", "OK");
                    };
                });
        }

        private VisualElement BuildBuyerFlowNote(string primaryText, string secondaryText = null)
        {
            var container = new VisualElement();
            container.style.marginTop = 8;
            container.style.marginBottom = 4;

            if (!string.IsNullOrWhiteSpace(primaryText))
            {
                var primary = new Label(primaryText);
                primary.AddToClassList("lgate-req-note");
                primary.style.whiteSpace = WhiteSpace.Normal;
                container.Add(primary);
            }

            if (!string.IsNullOrWhiteSpace(secondaryText))
            {
                var secondary = new Label(secondaryText);
                secondary.AddToClassList("lgate-req-note");
                secondary.style.whiteSpace = WhiteSpace.Normal;
                secondary.style.marginTop = 2;
                container.Add(secondary);
            }

            return container;
        }

        private string GetCreatorIdentitySignInButtonLabel()
        {
            if (_isCreatorIdentitySigningIn)
            {
                return "Connecting…";
            }

            return (_creatorIdentityNeedsReauthentication || _creatorIdentityNeedsSignInRetry)
                ? "Sign in again"
                : "Sign in with YUCP";
        }

        private string GetDiscordVerificationButtonLabel(bool creatorSignedIn)
        {
            if (_isCreatorIdentitySigningIn)
            {
                return "Connecting...";
            }

            if (!creatorSignedIn)
            {
                return (_creatorIdentityNeedsReauthentication || _creatorIdentityNeedsSignInRetry)
                    ? "Sign in again"
                    : "Sign in with YUCP";
            }

            return _creatorIdentityNeedsReauthentication ? "Sign in again" : "Verify in browser";
        }

        private string GetCreatorIdentitySignedOutPrimaryText()
        {
            if (_creatorIdentityNeedsSignInRetry)
            {
                return "This YUCP server was not ready to finish Unity purchase sign-in. Sign in again after the server has been updated.";
            }

            return _creatorIdentityNeedsReauthentication
                ? "Your previous YUCP buyer session no longer has permission to verify this package. Sign in again to continue."
                : "Sign in opens your browser and prepares a hosted verification flow for this package.";
        }

        private string GetCreatorIdentitySignedOutSecondaryText()
        {
            if (_creatorIdentityNeedsSignInRetry)
            {
                return "Use the same buyer account you used for this purchase. If this keeps happening, try again later or switch to a server that already supports Unity purchase verification.";
            }

            return _creatorIdentityNeedsReauthentication
                ? "Use the same buyer account you used for this purchase so Unity can request a fresh verification session."
                : "Use the same buyer account you used when you purchased access. The browser flow can then help you connect the right store account or enter purchase proof.";
        }

        private string GetCreatorIdentityVerifyPrimaryText()
        {
            return _creatorIdentityNeedsReauthentication
                ? "Your current YUCP buyer session must be refreshed before verification can continue."
                : "Verify in browser opens a hosted YUCP page where you can confirm ownership, connect the right account, or enter supported purchase proof.";
        }

        private VisualElement BuildStorefrontActionsRow()
        {
            if (_currentMetadata?.productLinks == null || _currentMetadata.productLinks.Count == 0)
            {
                return null;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 6;
            row.style.marginBottom = 4;

            foreach (var link in _currentMetadata.productLinks)
            {
                if (link == null || string.IsNullOrWhiteSpace(link.url))
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(link.label)
                    ? "Open storefront"
                    : $"Open {link.label}";
                var button = new Button(() => Application.OpenURL(link.url))
                {
                    text = label
                };
                button.AddToClassList("lgate-link-btn");
                button.style.marginRight = 8;
                button.style.marginTop = 4;
                row.Add(button);
            }

            return row.childCount > 0 ? row : null;
        }

        private VisualElement BuildVerificationServerNotice()
        {
            var block = new VisualElement();
            block.AddToClassList("lgate-req-block");
            block.style.borderBottomWidth = 0;
            block.style.paddingBottom = 10;

            var title = new Label("Verification server");
            title.AddToClassList("lgate-req-name");
            block.Add(title);

            string resolvedUrl = GetLicenseServerUrl();
            block.Add(BuildBuyerFlowNote(
                $"Current server: {resolvedUrl}",
                "Change this in Unity Project Settings under YUCP Package Manager when you need sign-in and purchase verification to use dev instead of production."));

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.flexWrap = Wrap.Wrap;
            buttonRow.style.marginTop = 4;

            var openSettingsButton = new Button(() => SettingsService.OpenProjectSettings("Project/YUCP Package Manager"))
            {
                text = "Open Unity Settings"
            };
            openSettingsButton.AddToClassList("lgate-link-btn");
            openSettingsButton.style.marginTop = 4;
            buttonRow.Add(openSettingsButton);

            block.Add(buttonRow);
            return block;
        }

        private static string GetLicenseServerUrl()
        {
            return LicenseServerResolver.GetLicenseServerUrl();
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

        private void InitializeForProtectedResume(InstalledPackageInfo packageInfo)
        {
            _isResumeVerificationMode = true;
            _resumeProtectedPackageInfo = packageInfo;
            _isImportMode = false;
            _currentPackagePath = string.Empty;
            _currentPackageIconPath = string.Empty;
            _currentImportItems = null;
            _allImportItems = null;
            _packageImportWizardInstance = null;
            _isProjectSettingsStep = false;
            _pendingImportAfterVerification = false;
            _pendingPackageName = null;
            _waitingForImportCompletion = false;
            _detailsExpanded = false;
            _preferOverwriteExisting = true;

            ShowInstallerView();
            SetMetadata(packageInfo);
            UpdateButtonStates();
            UpdateImportButtonEnabled();
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

            if (_isResumeVerificationMode)
            {
                _backButton.style.display = DisplayStyle.None;
                RefreshPrimaryImportButton();
                return;
            }

            bool isMultiStep = _packageImportWizardInstance != null && 
                PackageUtilityReflection.IsMultiStepWizard(_packageImportWizardInstance);
            bool isProjectStep = _packageImportWizardInstance != null && 
                PackageUtilityReflection.IsProjectSettingStep(_packageImportWizardInstance);

            // Show Back button only on project settings step of multi-step wizard
            if (isMultiStep && isProjectStep)
            {
                _backButton.style.display = DisplayStyle.Flex;
            }
            else if (isMultiStep && !isProjectStep)
            {
                _backButton.style.display = DisplayStyle.None;
            }
            else
            {
                _backButton.style.display = DisplayStyle.None;
            }

            RefreshPrimaryImportButton();
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
            // Rebuild banner hero with updated metadata
            if (_bannerSection != null)
            {
                if (_bannerHeroContainer != null)
                    _bannerHeroContainer.RemoveFromHierarchy();
                CreateBannerHero();
                if (_bannerHeroContainer != null)
                    _bannerSection.Add(_bannerHeroContainer);
            }

            if (_metadataSection != null && _metadataSection.parent != null)
            {
                var parent = _metadataSection.parent;
                int index = parent.IndexOf(_metadataSection);

                // Remove old license section from contents view before rebuilding
                var oldLicenseSection = _licenseSection;
                if (oldLicenseSection != null && oldLicenseSection.parent != null)
                    oldLicenseSection.RemoveFromHierarchy();

                _metadataSection.RemoveFromHierarchy();
                
                var newSection = CreateMetadataSection();
                parent.Insert(index, newSection);
                _metadataSection = newSection;
                _metadataGridExpanded = false; // reset show-more to collapsed for new package
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
                // Reset gallery selection state for the new package
                // (_galleryCarouselSchedule is already replaced inside BuildGalleryStrip)
                _originalBannerTexture = displayBanner;
                _selectedGalleryIndex = -1;
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
                if (_isResumeVerificationMode)
                {
                    HandleResumeProtectedImportClick();
                    return;
                }

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

                bool requiresVerification = (!isMultiStep || isProjectStep) && RequiresVerificationBeforeImport();
                Debug.Log($"[YUCP PackageManager] RequiresVerification={requiresVerification}, isSignedIn={CreatorIdentityOAuthService.IsSignedIn()}, serverUrl='{GetLicenseServerUrl()}', pendingAfterVerification={_pendingImportAfterVerification}, signingIn={_isCreatorIdentitySigningIn}");

                if (requiresVerification)
                {
                    var req = GetNextUnverifiedLicenseRequirement();
                    Debug.Log($"[YUCP PackageManager] NextUnverifiedReq: packageId='{req?.packageId}', productId='{req?.productId}', creatorAuthUserId='{req?.creatorAuthUserId}'");
                    if (req != null)
                    {
                        var verificationRequirements = BuildVerificationRequirements(req);
                        Debug.Log($"[YUCP PackageManager] VerificationRequirements count={verificationRequirements.Length}");
                        if (verificationRequirements.Length == 0)
                        {
                            EditorUtility.DisplayDialog(
                                "Verification Unavailable",
                                "This package requires verification before import, but it does not currently expose any hosted verification methods.",
                                "OK");
                            return;
                        }

                        // Reset stale sign-in state so re-clicking always works
                        if (_isCreatorIdentitySigningIn)
                        {
                            Debug.Log("[YUCP PackageManager] Resetting stale _isCreatorIdentitySigningIn flag before retry");
                            _isCreatorIdentitySigningIn = false;
                        }

                        _pendingImportAfterVerification = true;
                        if (!_licenseStates.TryGetValue(req.packageId, out var state) || state == null)
                        {
                            state = new LicenseVerificationState();
                            _licenseStates[req.packageId] = state;
                        }

                        OnVerifyInBrowserClicked(req, state, _importButton, verificationRequirements);
                        return;
                    }
                }

                _pendingImportAfterVerification = false;

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

        private void HandleResumeProtectedImportClick()
        {
            if (_resumeProtectedPackageInfo == null)
            {
                EditorUtility.DisplayDialog(
                    "Protected Package Unavailable",
                    "The protected package could not be resumed because its installer metadata is missing.",
                    "OK");
                return;
            }

            bool requiresVerification = RequiresVerificationBeforeImport();
            Debug.Log($"[YUCP PackageManager] ResumeProtectedImport requiresVerification={requiresVerification}, isSignedIn={CreatorIdentityOAuthService.IsSignedIn()}, pendingAfterVerification={_pendingImportAfterVerification}, signingIn={_isCreatorIdentitySigningIn}");
            if (requiresVerification)
            {
                var req = GetNextUnverifiedLicenseRequirement();
                if (req == null)
                {
                    EditorUtility.DisplayDialog(
                        "Verification Unavailable",
                        "This package requires verification before unlocking, but no hosted verification method is available.",
                        "OK");
                    return;
                }

                var verificationRequirements = BuildVerificationRequirements(req);
                if (verificationRequirements.Length == 0)
                {
                    EditorUtility.DisplayDialog(
                        "Verification Unavailable",
                        "This package requires verification before unlocking, but it does not currently expose any hosted verification methods.",
                        "OK");
                    return;
                }

                if (_isCreatorIdentitySigningIn)
                {
                    Debug.Log("[YUCP PackageManager] Resetting stale _isCreatorIdentitySigningIn flag before retry");
                    _isCreatorIdentitySigningIn = false;
                }

                _pendingImportAfterVerification = true;
                if (!_licenseStates.TryGetValue(req.packageId, out var state) || state == null)
                {
                    state = new LicenseVerificationState();
                    _licenseStates[req.packageId] = state;
                }

                OnVerifyInBrowserClicked(req, state, _importButton, verificationRequirements);
                return;
            }

            _pendingImportAfterVerification = false;
            ProtectedPayloadInstallService.QueuePendingApply(_resumeProtectedPackageInfo);
            Close();
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
        }

        private void ShowInstallerView()
        {
            _currentViewMode = ViewMode.Installer;
            _currentViewContainer.Clear();
            if (_installerRoot != null)
            {
                _installerRoot.style.display = DisplayStyle.Flex;
                _currentViewContainer.Add(_installerRoot);
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

                var installedInfo = InstalledPackageInfoFactory.Create(
                    metadata,
                    packageId ?? string.Empty,
                    archiveSha256 ?? string.Empty,
                    publisherId ?? string.Empty,
                    isVerified,
                    installedFiles);
                installedInfo.SetInstalledDateTime(DateTime.Now);

                if (string.IsNullOrEmpty(installedInfo.packageId))
                {
                    Debug.LogWarning($"[YUCP PackageManager] Imported assets but skipped registry registration because packageId is unavailable. packageName='{installedInfo.packageName}', signed={_isPackageSigned}, verificationValid={isVerified}, cachedExtractionError='{_cachedSigningExtractionError ?? ""}'");
                    return;
                }

                if (!CouplingImportGuard.TryApplyCouplingOrRollback(installedInfo, out string couplingError))
                {
                    Debug.LogError($"[YUCP PackageManager] Coupling failed for PackageID '{installedInfo.packageId}': {couplingError}");
                    EditorUtility.DisplayDialog(
                        "Coupling Failed",
                        $"The package import was rolled back because the local coupling pass failed.\n\n{couplingError}",
                        "OK");
                    return;
                }

                // Register in registry
                var registry = InstalledPackageRegistry.GetOrCreate();
                registry.RegisterPackage(installedInfo);

                Debug.Log($"[YUCP PackageManager] Registered package: {installedInfo.packageName} (ID: {packageId}, verified={installedInfo.isVerified}, installedFiles={installedInfo.installedFiles?.Count ?? 0})");

                if (hasTempInstallDescriptor && installedInfo.protectedPayload != null)
                {
                    ProtectedPayloadInstallService.QueuePendingApply(installedInfo);
                    Debug.Log($"[YUCP PackageManager] Queued protected payload apply for '{installedInfo.packageName}'. Waiting for installer/domain reload handoff to complete.");
                }
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
