#if !YUCP_PACKAGE_MANAGER_DISABLED
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
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
    public class PackageManagerWindow : EditorWindow, IPackageChangePlanReviewHost
    {
        internal bool HasPackageImportItems =>
            _currentImportItems != null && _currentImportItems.Length > 0;

        internal bool IsAliasBootstrapFlow => _isAliasBootstrapFlow;

        internal string PrimaryActionLabel => GetPrimaryImportButtonText();

        internal static void ShowAliasBootstrap(PackageMetadata metadata)
        {
            if (metadata?.aliasPackage == null ||
                !AliasPackageDiscovery.IsServerAuthorized(
                    metadata.aliasPackage))
            {
                PackageMetadataMediaOwnership.Release(metadata);
                throw new ArgumentException(
                    "A server-authorized alias is required.",
                    nameof(metadata));
            }

            PackageManagerWindow window = null;
            try
            {
                foreach (PackageManagerWindow existing in
                    Resources.FindObjectsOfTypeAll<PackageManagerWindow>())
                {
                    existing?.Close();
                }

                window = CreateInstance<PackageManagerWindow>();
                window.InitializeForAlias(metadata, true);
            }
            catch
            {
                if (window != null)
                {
                    UnityEngine.Object.DestroyImmediate(window);
                }
                PackageMetadataMediaOwnership.Release(metadata);
                throw;
            }
        }

        internal static bool IsAliasBootstrapOpen(string aliasId)
        {
            if (string.IsNullOrWhiteSpace(aliasId))
            {
                return false;
            }

            return Resources.FindObjectsOfTypeAll<PackageManagerWindow>()
                .Any(window =>
                    window != null &&
                    window._hasPendingImportContext &&
                    window._isAliasBootstrapFlow &&
                    string.Equals(
                        (window._currentMetadata ?? window._cachedMetadata)
                            ?.aliasPackage?.aliasId,
                        aliasId,
                        StringComparison.Ordinal));
        }

        public static void ShowResumeProtectedPackage(InstalledPackageInfo packageInfo)
        {
            if (packageInfo == null)
            {
                Debug.LogWarning("[YUCP PackageManager] Cannot resume protected package because package info is missing.");
                return;
            }

            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                Debug.LogWarning("[YUCP PackageManager] Package Manager is disabled (Tools > YUCP > Package Manager > Enable).");
                return;
            }

            string packageLabel = !string.IsNullOrWhiteSpace(packageInfo.packageName)
                ? packageInfo.packageName
                : packageInfo.packageId ?? "the package";
            Debug.Log($"[YUCP PackageManager] '{packageLabel}' is installed and ready.");
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
        private static readonly (string Kind, string Label)[] ChangeReviewGroups =
        {
            (PackageChangeKind.Added, "Added"),
            (PackageChangeKind.ReplacedUnchanged, "Replaced unchanged"),
            (
                PackageChangeKind.ReplacedWithLocalModifications,
                "Replaced with local modifications"),
            (PackageChangeKind.Removed, "Removed"),
            (
                PackageChangeKind.RemovedWithLocalModifications,
                "Removed with local modifications"),
            (PackageChangeKind.BlockedCollision, "Blocked collisions"),
        };
        private const int ChangeReviewPathsPerGroup = 40;
        private VisualElement _changeReviewSection;
        private VisualElement _reviewActionBar;
        private Button _reviewConfirmButton;
        private Button _reviewCancelButton;
        private PackageReviewRequest _changeReviewRequest;
        private string _lifecycleProgressMessage = string.Empty;
        private double _lifecycleProgressStartedAt;
        private IVisualElementScheduledItem _lifecycleProgressTicker;
        private VisualElement _importProgressFill;
        private VisualElement _importProgressMirror;
        private Label _importProgressMirrorLabel;
        private bool _importProgressGeometryHooked;
        private IVisualElementScheduledItem _importProgressSweep;
        private float _importProgressSweepOffset;
        private TaskCompletionSource<bool> _changeReviewCompletion;
        private PackageChangePlan _changeReviewPlan;
        private IReadOnlyList<string> _changeReviewDirtyAssets =
            Array.Empty<string>();
        private string _changeReviewTargetLabel = string.Empty;
        private bool _changeReviewBlocked;
        private IDisposable _changeReviewRegistration;
        private Button _backButton;
        private Label _verifyStatusLabel;
        private Label _flowNoticeLinkLabel;
        private const string SupportDiscordUrl = "https://discord.gg/5YzqbBTA5e";
        private readonly List<Button> _hostedLifecycleButtons =
            new List<Button>();
        private VisualElement _hostedLifecycleControls;
        private VisualElement _flowNoticeElement;
        private Label _flowNoticeTitleLabel;
        private Label _flowNoticeBodyLabel;
        private VisualElement _conflictModeSection;
        private Button _overwriteModeButton;
        private Button _keepExistingModeButton;

        // State
        [SerializeField]
        private PackageMetadata _currentMetadata;
        private Texture2D _bannerGradientTexture;
        private bool _detailsExpanded = false;
        private bool _preferOverwriteExisting = true;
        private bool _isHostedLifecycleRunning;
        private bool _pendingLifecycleResumeScheduled;
        private int _cachedGradientHeight = 0;
        private const string DefaultPlaceholderTexturePath = "Packages/com.yucp.importer/Editor/PackageManager/Resources/MainLogo.png";
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
        [SerializeField]
        private PackageMetadata _cachedMetadata;
        private static PackageMetadata s_lastImportMetadata;
        private static string s_lastImportPackagePath;

        // Direct licensed packages require the product bootstrap.
        private VisualElement _licenseSection;
        private readonly List<LicensedAssetDescriptor> _licensedAssetDescriptors =
            new List<LicensedAssetDescriptor>();
        private readonly HashSet<string> _licensedAssetPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private class LicensedAssetDescriptor
        {
            public string destinationPath;
            public string displayName;
            public string licensePackageId;
            public string sourceFolder;
        }

        private enum FlowNoticeTone
        {
            Info,
            Success,
            Error,
        }

        private enum BrokerAuthenticationOperation
        {
            None,
            Refresh,
            SignIn,
            SignOut,
        }
        
        // Domain reload prevention
        private bool _isImportMode = false; // Track if window is in import mode (prevents domain reload)
        [SerializeField]
        private bool _isAliasBootstrapFlow;

        // Set once the window has been handed an import/alias-install context, so a stray
        // domain-reload restore (which resets _isImportMode) is not mistaken for an empty window.
        [SerializeField]
        private bool _hasPendingImportContext = false;
        
        // Fixed modal implementation state
        private bool _isModalFixed = false;
        private VisualElement _lastHoveredElement = null;
        private VisualElement _currentTooltipElement = null;
        
        private VisualElement _currentViewContainer;

        // Import completion tracking
        private bool _waitingForImportCompletion = false;
        private string _pendingPackageName;
        private bool _pendingImportAfterVerification = false;
        private bool? _isBrokerSignedIn;
        private BrokerAuthenticationOperation _authenticationOperation;
        private bool AuthenticationActionInFlight =>
            _authenticationOperation != BrokerAuthenticationOperation.None;
        private bool _authenticationRefreshScheduled;
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

            _changeReviewRegistration = PackageChangePlanReview.Register(this);
            AssetDatabase.importPackageStarted += OnImportPackageStarted;
            AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
            
            // Set minimum window size
            minSize = new Vector2(500, 600);
            
            // Ensure TrustedAuthority is initialized with all keys (root, cached, etc.)
            TrustedAuthority.ReloadAllKeys();
            
            // This window only exists as the import installer (driven by the interceptor or the
            // alias install flow). Package browsing/management now lives in the Creator Companion.
            // If Unity restores a stray instance after a domain reload with no import context,
            // close it instead of showing a standalone manager surface.
            EditorApplication.delayCall += () =>
            {
                if (this != null && !_isImportMode && !_hasPendingImportContext)
                {
                    Close();
                }
            };
            SchedulePendingLifecycleResume();
            ScheduleAuthenticationRefresh();
        }

        // The exe owns the sign-in state and it can change while Unity sits in
        // the background, so nothing it said earlier survives a focus change.
        private void OnFocus()
        {
            InvalidateBrokerSignIn();
        }

        private void InvalidateBrokerSignIn()
        {
            if (!_isAliasBootstrapFlow ||
                AuthenticationActionInFlight ||
                _pendingImportAfterVerification ||
                _waitingForImportCompletion)
            {
                return;
            }
            _isBrokerSignedIn = null;
            BuildLicenseSection();
            ScheduleAuthenticationRefresh();
        }

        private void OnDisable()
        {
            CompletePendingChangeReview(false);
            StopLifecycleProgressMessage();
            StopImportProgressSweep();
            _importProgressFill = null;
            _changeReviewRegistration?.Dispose();
            _changeReviewRegistration = null;
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
            PackageMetadataMediaOwnership.Release(_currentMetadata);
            if (!ReferenceEquals(_currentMetadata, _cachedMetadata))
            {
                PackageMetadataMediaOwnership.Release(_cachedMetadata);
            }
            _currentMetadata = null;
            _cachedMetadata = null;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            
            // Clear existing content to prevent duplicates
            root.Clear();

            // Unity can rebuild the visual tree after the installer receives its context.
            // Keep the current package state instead of replacing it with empty metadata.
            _currentMetadata = _cachedMetadata ?? _currentMetadata ?? new PackageMetadata();
            
            root.style.flexDirection = FlexDirection.Column;
            root.AddToClassList("yucp-root");
            
            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.yucp.importer/Editor/PackageManager/Styles/PackageManager.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // Container for the installer surface.
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

            _flowNoticeElement = CreateFlowNotice();
            _installerRoot.Add(_flowNoticeElement);

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

            _reviewActionBar = CreateReviewActionBar();
            _installerRoot.Add(_reviewActionBar);

            // Update banner height when window resizes
            root.RegisterCallback<GeometryChangedEvent>(OnWindowGeometryChanged);

            // Ensure gradient is created and applied after layout is calculated
            root.schedule.Execute(() =>
            {
                CreateBannerGradientTexture();
                ApplyGradientToOverlay();
                UpdateBannerHeight();
            });

            RestorePendingInstallerView();
        }

        private void RestorePendingInstallerView()
        {
            if (!_hasPendingImportContext ||
                _currentViewContainer == null ||
                _installerRoot == null)
            {
                return;
            }

            ShowInstallerView();
            RefreshUI();
            UpdateButtonStates();
            LoadResources();
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
            if (displayBanner != null)
            {
                _bannerImageContainer.style.backgroundImage = new StyleBackground(displayBanner);
                _originalBannerTexture = displayBanner;
            }
            else
            {
                _bannerImageContainer.AddToClassList("yucp-banner-image-empty");
                _originalBannerTexture = null;
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
            if (_currentMetadata?.icon != null)
            {
                var iconImage = new Image();
                iconImage.image = _currentMetadata.icon;
                iconImage.style.width = Length.Percent(100);
                iconImage.style.height = Length.Percent(100);
                iconWrap.Add(iconImage);
            }
            else
            {
                var iconText = new Label("Y");
                iconText.AddToClassList("yucp-hero-icon-text");
                iconWrap.Add(iconText);
            }
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

            AddHostedLifecycleControls(ctaColumn);

            _verifyStatusLabel = new Label();
            _verifyStatusLabel.AddToClassList("yucp-verify-status");
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
            return AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultPlaceholderTexturePath);
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
            _metadataGridElement = grid;

            showMoreBtn.clicked += () =>
            {
                _metadataGridExpanded = !_metadataGridExpanded;
                if (_metadataGridExpanded)
                {
                    grid.AddToClassList("yucp-meta-grid--expanded");
                }
                else
                {
                    grid.RemoveFromClassList("yucp-meta-grid--expanded");
                }
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

        /// <summary>
        /// A license requirement is only actionable when it names something the
        /// verification flow can actually check ownership against. A packageId on
        /// its own is just an identifier — gating the import on it turns "Import"
        /// into "Verify and Import" for packages that were exported without any
        /// storefront or Discord entitlement configured, and the verification that
        /// follows has nothing to verify and always fails.
        /// </summary>
        private static bool IsVerifiableLicenseRequirement(LicensePackageRequirement requirement)
        {
            return requirement != null &&
                !string.IsNullOrWhiteSpace(requirement.packageId) &&
                (!string.IsNullOrWhiteSpace(requirement.productId) ||
                 !string.IsNullOrWhiteSpace(requirement.gumroadPermalink) ||
                 !string.IsNullOrWhiteSpace(requirement.jinxxyProductId) ||
                 !string.IsNullOrWhiteSpace(requirement.discordRoleId));
        }

        private LicensePackageRequirement GetNextUnverifiedLicenseRequirement()
        {
            return _currentMetadata?.licensePackages?
                .FirstOrDefault(IsVerifiableLicenseRequirement);
        }

        private bool RequiresVerificationBeforeImport()
        {
            return GetNextUnverifiedLicenseRequirement() != null;
        }
        private string GetPrimaryImportButtonText()
        {
            if (_isAliasBootstrapFlow)
            {
                if (_authenticationOperation ==
                    BrokerAuthenticationOperation.Refresh)
                {
                    return "Checking sign-in...";
                }
                if (_authenticationOperation ==
                    BrokerAuthenticationOperation.SignIn)
                {
                    return "Signing in...";
                }
                if (_authenticationOperation ==
                    BrokerAuthenticationOperation.SignOut)
                {
                    return "Signing out...";
                }
                if (!_isBrokerSignedIn.HasValue)
                {
                    return "Checking sign-in...";
                }
                if (!_isBrokerSignedIn.Value)
                {
                    return "Sign in with YUCP";
                }
                if (!IsCurrentAliasInstalled())
                {
                    return "Verify and Import";
                }
                return HasUnhandledVersionedBootstrap()
                    ? "Review version change"
                    : "Install a version from VCC";
            }

            bool isMultiStep = _packageImportWizardInstance != null &&
                PackageUtilityReflection.IsMultiStepWizard(_packageImportWizardInstance);
            bool isProjectStep = _packageImportWizardInstance != null &&
                PackageUtilityReflection.IsProjectSettingStep(_packageImportWizardInstance);

            if (isMultiStep && !isProjectStep)
            {
                return "Next";
            }

            // Only the alias bootstrap flow above can actually verify entitlement.
            // A direct .unitypackage carrying a license requirement is refused
            // outright further down (it needs the product bootstrap), so promising
            // "Verify and Import" here would invite a click that cannot succeed.
            return RequiresVerificationBeforeImport() ? "Requires Creator Companion" : "Import";
        }

        private void RefreshPrimaryImportButton()
        {
            if (_importButton == null)
            {
                return;
            }

            string btnText = GetPrimaryImportButtonText();
            bool showBagIcon =
                btnText == "Sign in with YUCP" ||
                btnText == "Verify and Import" ||
                btnText == "Verify and Unlock";

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

                Texture2D bag =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        ImporterBagIconPath);
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
            AnimateImportButtonWidth();
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

            // Prefer the archive's real file list. The embedded breakdown is written
            // before the installer runtime and bundled packages are injected, so it
            // under-reports assemblies and would otherwise claim "No DLLs" on a
            // package that ships them.
            bool? hasAssemblies = PackageMetadataExtractor.ContainsAssemblies(
                _allImportItems ?? _currentImportItems);

            if (!hasAssemblies.HasValue && _currentMetadata?.assetBreakdown != null &&
                _currentMetadata.assetBreakdown.Any(ab =>
                    string.Equals(ab.type, "Assembly", StringComparison.OrdinalIgnoreCase)))
            {
                // Without the item list only the positive claim is supportable:
                // the breakdown can prove DLLs are present, never that they aren't.
                hasAssemblies = true;
            }

            if (hasAssemblies.HasValue)
            {
                chips.Add(hasAssemblies.Value ? "Contains DLLs" : "No DLLs");
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

                if (IsPrecompiledInstallerRuntimePath(path))
                {
                    return true;
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

        private static bool ImportItemsContainPrecompiledInstallerPayload(System.Array items)
        {
            if (items == null || items.Length == 0)
            {
                return IsPrecompiledInstallerPayloadPresentOnDisk();
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
                if (IsPrecompiledInstallerRuntimePath(path))
                {
                    return true;
                }
            }

            return IsPrecompiledInstallerPayloadPresentOnDisk();
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

                if (IsPrecompiledInstallerPayloadPresentOnDisk())
                {
                    return true;
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

        private static bool IsPrecompiledInstallerPayloadPresentOnDisk()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string runtimePath = Path.Combine(
                    projectRoot,
                    "Packages",
                    "yucp.installed-packages",
                    "Editor",
                    "YUCP.DirectVpmInstaller.Runtime.dll");
                return File.Exists(runtimePath);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPrecompiledInstallerRuntimePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return string.Equals(
                path.Replace('\\', '/'),
                "Packages/yucp.installed-packages/Editor/YUCP.DirectVpmInstaller.Runtime.dll",
                StringComparison.OrdinalIgnoreCase);
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
                sb.AppendLine(
                    "YUCP has confirmed who made it. Nothing");
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

        private VisualElement CreateReviewActionBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("yucp-review-bar");
            bar.style.display = DisplayStyle.None;

            var caption = new Label();
            caption.name = "review-bar-caption";
            caption.AddToClassList("yucp-review-bar-caption");
            caption.style.whiteSpace = WhiteSpace.Normal;
            bar.Add(caption);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);

            _reviewCancelButton = new Button(() => CompletePendingChangeReview(false))
            {
                text = "Cancel",
            };
            _reviewCancelButton.AddToClassList("yucp-cta-cancel");
            bar.Add(_reviewCancelButton);

            _reviewConfirmButton = new Button(() =>
            {
                if (!_changeReviewBlocked)
                {
                    CompletePendingChangeReview(true);
                }
            });
            _reviewConfirmButton.AddToClassList("yucp-cta-button");
            _reviewConfirmButton.style.marginLeft = 8;
            _reviewConfirmButton.text = "Continue";
            bar.Add(_reviewConfirmButton);

            return bar;
        }

        private void UpdateReviewScreenChrome()
        {
            bool reviewing = ChangeReviewPending;
            if (_reviewActionBar != null)
            {
                _reviewActionBar.style.display =
                    reviewing ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_detailsToggleButton != null)
            {
                _detailsToggleButton.style.display =
                    reviewing ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_contentsSection != null)
            {
                var title = _contentsSection.Q<Label>("details-title");
                if (title != null)
                {
                    title.text = reviewing ? "Confirm Changes" : "Package Contents";
                }
                var subtitle = _contentsSection.Q<Label>("details-subtitle");
                if (subtitle != null)
                {
                    subtitle.text = reviewing
                        ? "Nothing has been written yet. Continue applies exactly " +
                          "these changes to your project."
                        : "Review files and existing-file conflicts before installing.";
                }
            }
            if (!reviewing)
            {
                return;
            }
            if (_reviewConfirmButton != null)
            {
                _reviewConfirmButton.text =
                    string.IsNullOrWhiteSpace(_changeReviewRequest?.ApproveLabel)
                        ? "Continue"
                        : _changeReviewRequest.ApproveLabel;
                _reviewConfirmButton.SetEnabled(!_changeReviewBlocked);
                _reviewConfirmButton.tooltip = _changeReviewBlocked
                    ? "Resolve the problems listed above, then retry."
                    : "Apply exactly the changes listed above.";
            }
            if (_reviewCancelButton != null)
            {
                _reviewCancelButton.text =
                    string.IsNullOrWhiteSpace(_changeReviewRequest?.CancelLabel)
                        ? "Cancel"
                        : _changeReviewRequest.CancelLabel;
            }
            var barCaption = _reviewActionBar?.Q<Label>("review-bar-caption");
            if (barCaption != null)
            {
                barCaption.text = _changeReviewBlocked
                    ? "This install cannot continue yet."
                    : "Step 2 of 2 — nothing is written until you continue.";
            }
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
            detailsTitle.name = "details-title";
            detailsTitle.AddToClassList("yucp-details-title");
            detailsHeader.Add(detailsTitle);

            var detailsSubtitle = new Label(
                "Review files and existing-file conflicts before installing.");
            detailsSubtitle.name = "details-subtitle";
            detailsSubtitle.AddToClassList("yucp-details-subtitle");
            detailsSubtitle.style.whiteSpace = WhiteSpace.Normal;
            detailsHeader.Add(detailsSubtitle);
            section.Add(detailsHeader);

            var dependenciesContainer = new VisualElement();
            dependenciesContainer.name = "dependencies-container";
            dependenciesContainer.style.flexShrink = 0;
            dependenciesContainer.style.maxHeight = Length.Percent(34f);
            dependenciesContainer.style.overflow = Overflow.Hidden;
            section.Add(dependenciesContainer);

            var licensedSummaryContainer = new VisualElement();
            licensedSummaryContainer.name = "licensed-summary-container";
            section.Add(licensedSummaryContainer);

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

            _treeView = new PackageItemTreeView(
                _treeScrollView,
                IsLicensedAssetPath);

            // Wrap the tree in a ScrollView so only the asset list scrolls
            _treeScrollWrapper = new ScrollView(ScrollViewMode.Vertical);
            _treeScrollWrapper.name = "tree-wrapper";
            _treeScrollWrapper.AddToClassList("yucp-tree-scroll-wrapper");
            _treeScrollWrapper.style.flexGrow = 1;
            _treeScrollWrapper.style.flexShrink = 1;
            _treeScrollWrapper.style.minHeight = 0;
            _treeScrollWrapper.Add(_treeScrollView);
            section.Add(_treeScrollWrapper);

            _changeReviewSection = new VisualElement();
            _changeReviewSection.name = "change-review";
            _changeReviewSection.style.display = DisplayStyle.None;
            _changeReviewSection.style.flexGrow = 1;
            _changeReviewSection.style.flexShrink = 1;
            _changeReviewSection.style.minHeight = 0;
            section.Add(_changeReviewSection);

            ShowSampleTree();
            UpdateConflictModeSection();

            return section;
        }

        bool IPackageChangePlanReviewHost.CanReview =>
            rootVisualElement != null && _contentsSection != null;

        Task<bool> IPackageChangePlanReviewHost.ReviewAsync(
            PackageReviewRequest request)
        {
            CompletePendingChangeReview(false);
            _changeReviewRequest = request;
            _changeReviewPlan = request.Plan;
            _changeReviewDirtyAssets =
                request.DirtyAssets ?? Array.Empty<string>();
            _changeReviewTargetLabel = request.Summary ?? string.Empty;
            _changeReviewBlocked =
                request.Plan != null &&
                (!PackageChangePlanSigner.Verify(request.Plan) ||
                 _changeReviewDirtyAssets.Count > 0 ||
                 request.Plan.HasBlockedCollisions);
            _changeReviewCompletion = new TaskCompletionSource<bool>();

            SetImportButtonProgress(-1f);
            RenderChangeReview();
            _detailsExpanded = true;
            UpdateInstallerLayout();
            UpdateReviewScreenChrome();
            UpdateImportButtonEnabled();
            Focus();
            return _changeReviewCompletion.Task;
        }

        private bool ChangeReviewPending => _changeReviewCompletion != null;

        private void CompletePendingChangeReview(bool approved)
        {
            TaskCompletionSource<bool> completion = _changeReviewCompletion;
            if (completion == null)
            {
                return;
            }
            _changeReviewCompletion = null;
            _changeReviewPlan = null;
            _changeReviewRequest = null;
            _changeReviewDirtyAssets = Array.Empty<string>();
            if (_changeReviewSection != null)
            {
                _changeReviewSection.Clear();
                _changeReviewSection.style.display = DisplayStyle.None;
            }
            if (_treeScrollWrapper != null)
            {
                _treeScrollWrapper.style.display = DisplayStyle.Flex;
            }
            if (_conflictModeSection != null)
            {
                _conflictModeSection.style.display = DisplayStyle.Flex;
            }
            if (approved)
            {
                _detailsExpanded = false;
                UpdateInstallerLayout();
            }
            UpdateReviewScreenChrome();
            UpdateImportButtonEnabled();
            completion.TrySetResult(approved);
        }

        private void RenderChangeReview()
        {
            if (_changeReviewSection == null)
            {
                return;
            }
            _changeReviewSection.Clear();
            _changeReviewSection.style.display = DisplayStyle.Flex;
            if (_treeScrollWrapper != null)
            {
                _treeScrollWrapper.style.display = DisplayStyle.None;
            }
            if (_conflictModeSection != null)
            {
                _conflictModeSection.style.display = DisplayStyle.None;
            }

            _changeReviewSection.AddToClassList("yucp-review");

            var heading = new Label(
                string.IsNullOrWhiteSpace(_changeReviewRequest?.Heading)
                    ? "Exact project changes"
                    : _changeReviewRequest.Heading);
            heading.AddToClassList("yucp-review-heading");
            _changeReviewSection.Add(heading);

            if (!string.IsNullOrWhiteSpace(_changeReviewTargetLabel))
            {
                var target = new Label(_changeReviewTargetLabel);
                target.AddToClassList("yucp-review-summary");
                _changeReviewSection.Add(target);
            }

            if (_changeReviewPlan == null)
            {
                return;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("yucp-review-scroll");

            if (!PackageChangePlanSigner.Verify(_changeReviewPlan))
            {
                scroll.Add(BuildChangeReviewNotice(
                    "The change-plan signature is invalid. Close this review " +
                    "and start the operation again.",
                    ChangeReviewSeverity.Blocking));
            }
            if (_changeReviewDirtyAssets.Count > 0)
            {
                scroll.Add(BuildChangeReviewNotice(
                    "Save or revert these assets before continuing:\n" +
                    string.Join("\n", _changeReviewDirtyAssets.Take(12)),
                    ChangeReviewSeverity.Blocking));
            }
            if (_changeReviewPlan.HasBlockedCollisions)
            {
                scroll.Add(BuildChangeReviewNotice(
                    "This package would replace files it does not own. Move " +
                    "or remove those collisions, then retry.",
                    ChangeReviewSeverity.Blocking));
            }
            int preservedCount = _changeReviewPlan.entries.Count(
                entry => entry.RequiresPreservedCopy);
            if (preservedCount > 0)
            {
                scroll.Add(BuildChangeReviewNotice(
                    $"{preservedCount} locally modified file(s) are copied to " +
                    ".yucp/preserved-changes and Assets/YUCP Preserved Changes " +
                    "first.",
                    ChangeReviewSeverity.Caution));
            }

            foreach ((string kind, string label) in ChangeReviewGroups)
            {
                List<PackageChangePlanEntry> entries = _changeReviewPlan.entries
                    .Where(entry => string.Equals(
                        entry.changeKind,
                        kind,
                        StringComparison.Ordinal))
                    .ToList();
                if (entries.Count == 0)
                {
                    continue;
                }
                var group = new VisualElement();
                group.AddToClassList("yucp-review-group");
                var dot = new VisualElement();
                dot.AddToClassList("yucp-review-group-dot");
                dot.style.backgroundColor =
                    new StyleColor(ChangeReviewGroupColor(kind));
                group.Add(dot);
                var groupLabel = new Label(label.ToUpperInvariant());
                groupLabel.AddToClassList("yucp-review-group-label");
                group.Add(groupLabel);
                var groupCount = new Label(entries.Count.ToString());
                groupCount.AddToClassList("yucp-review-group-count");
                group.Add(groupCount);
                scroll.Add(group);

                foreach (PackageChangePlanEntry entry in
                         entries.Take(ChangeReviewPathsPerGroup))
                {
                    var row = new Label(entry.normalizedPath);
                    row.AddToClassList("yucp-review-path");
                    scroll.Add(row);
                }
                if (entries.Count > ChangeReviewPathsPerGroup)
                {
                    var more = new Label(
                        $"and {entries.Count - ChangeReviewPathsPerGroup} more");
                    more.AddToClassList("yucp-review-more");
                    scroll.Add(more);
                }
            }
            _changeReviewSection.Add(scroll);
        }

        private enum ChangeReviewSeverity
        {
            Blocking,
            Caution,
        }

        private static Color ChangeReviewGroupColor(string kind)
        {
            if (string.Equals(kind, PackageChangeKind.Added, StringComparison.Ordinal))
            {
                return new Color(0.36f, 0.80f, 0.52f, 0.95f);
            }
            if (string.Equals(
                    kind,
                    PackageChangeKind.BlockedCollision,
                    StringComparison.Ordinal))
            {
                return new Color(0.89f, 0.29f, 0.33f, 0.95f);
            }
            if (string.Equals(kind, PackageChangeKind.Removed, StringComparison.Ordinal) ||
                string.Equals(
                    kind,
                    PackageChangeKind.RemovedWithLocalModifications,
                    StringComparison.Ordinal))
            {
                return new Color(0.91f, 0.69f, 0.27f, 0.95f);
            }
            return new Color(0.38f, 0.52f, 1.00f, 0.95f);
        }

        private void SetImportButtonProgress(float? fraction)
        {
            if (_importButton == null)
            {
                return;
            }
            bool clearing = fraction.HasValue && fraction.Value < 0f;
            if (_importProgressFill != null &&
                _importProgressFill.parent != _importButton)
            {
                _importProgressFill = null;
                _importProgressMirror = null;
                _importProgressMirrorLabel = null;
            }
            if (clearing && _importProgressFill == null)
            {
                _importButton.RemoveFromClassList("yucp-cta-button--busy");
                return;
            }
            if (_importProgressFill == null)
            {
                if (_importButton.Q<Label>() == null)
                {
                    string existing = _importButton.text;
                    _importButton.text = string.Empty;
                    var label = new Label(existing);
                    label.pickingMode = PickingMode.Ignore;
                    _importButton.Add(label);
                }
                _importProgressFill = new VisualElement();
                _importProgressFill.AddToClassList("yucp-cta-progress");
                _importProgressFill.pickingMode = PickingMode.Ignore;

                _importProgressMirror = new VisualElement();
                _importProgressMirror.AddToClassList("yucp-cta-progress-mirror");
                _importProgressMirror.pickingMode = PickingMode.Ignore;
                _importProgressMirrorLabel = new Label(string.Empty);
                _importProgressMirrorLabel.pickingMode = PickingMode.Ignore;
                _importProgressMirror.Add(_importProgressMirrorLabel);
                _importProgressFill.Add(_importProgressMirror);
                _importButton.Add(_importProgressFill);

                if (!_importProgressGeometryHooked)
                {
                    _importProgressGeometryHooked = true;
                    _importButton.RegisterCallback<GeometryChangedEvent>(
                        _ => SyncImportProgressMirror());
                }
            }
            SyncImportProgressMirror();

            if (clearing)
            {
                StopImportProgressSweep();
                _importProgressFill.RemoveFromClassList("yucp-cta-progress--active");
                _importProgressFill.style.width = Length.Percent(0f);
                _importButton.RemoveFromClassList("yucp-cta-button--busy");
                return;
            }

            _importProgressFill.AddToClassList("yucp-cta-progress--active");
            _importButton.AddToClassList("yucp-cta-button--busy");
            if (fraction.HasValue)
            {
                StopImportProgressSweep();
                _importProgressFill.RemoveFromClassList(
                    "yucp-cta-progress--indeterminate");
                _importProgressFill.style.left = Length.Percent(0f);
                _importProgressFill.style.width =
                    Length.Percent(Mathf.Clamp01(fraction.Value) * 100f);
                return;
            }
            StartImportProgressSweep();
        }

        private void AnimateImportButtonWidth()
        {
            if (_importButton == null)
            {
                return;
            }
            // Measured a frame later: the font and icon widths are not resolved
            // until then, and measuring early undershoots.
            _importButton.schedule.Execute(MeasureImportButtonWidth);
        }

        private void MeasureImportButtonWidth()
        {
            if (_importButton == null)
            {
                return;
            }
            Label label = _importButton.Q<Label>();
            string text = label != null ? label.text : _importButton.text;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            TextElement measurer = label ?? (TextElement)_importButton;
            Vector2 size = measurer.MeasureTextSize(
                text,
                0f,
                VisualElement.MeasureMode.Undefined,
                0f,
                VisualElement.MeasureMode.Undefined);
            float horizontalPadding =
                _importButton.resolvedStyle.paddingLeft +
                _importButton.resolvedStyle.paddingRight +
                _importButton.resolvedStyle.borderLeftWidth +
                _importButton.resolvedStyle.borderRightWidth;
            if (horizontalPadding <= 0f)
            {
                horizontalPadding = 46f;
            }
            var icon = _importButton.Q<Image>();
            if (icon != null)
            {
                float iconWidth = icon.resolvedStyle.width;
                horizontalPadding +=
                    (iconWidth > 0f ? iconWidth : 16f) +
                    icon.resolvedStyle.marginRight;
            }
            float target = Mathf.Max(size.x + horizontalPadding + 2f, 160f);
            _importButton.style.width = target;
            SyncImportProgressMirror();
        }

        private void SyncImportProgressMirror()
        {
            if (_importProgressMirror == null || _importButton == null)
            {
                return;
            }
            float width = _importButton.resolvedStyle.width;
            if (width > 0f)
            {
                _importProgressMirror.style.width = width;
            }
            if (_importProgressMirrorLabel != null)
            {
                Label source = _importButton.Q<Label>();
                _importProgressMirrorLabel.text = source != null
                    ? source.text
                    : _importButton.text;
            }
        }

        private void StartImportProgressSweep()
        {
            if (_importProgressFill == null || _importProgressSweep != null)
            {
                return;
            }
            _importProgressFill.AddToClassList("yucp-cta-progress--indeterminate");
            _importProgressSweepOffset = -34f;
            _importProgressSweep = _importProgressFill.schedule
                .Execute(() =>
                {
                    _importProgressSweepOffset += 34f;
                    if (_importProgressSweepOffset > 100f)
                    {
                        _importProgressSweepOffset = -34f;
                    }
                    _importProgressFill.style.left =
                        Length.Percent(_importProgressSweepOffset);
                })
                .Every(900);
        }

        private void StopImportProgressSweep()
        {
            _importProgressSweep?.Pause();
            _importProgressSweep = null;
            _importProgressFill?.RemoveFromClassList(
                "yucp-cta-progress--indeterminate");
            if (_importProgressFill != null)
            {
                _importProgressFill.style.left = Length.Percent(0f);
            }
        }

        private static VisualElement BuildChangeReviewNotice(
            string message,
            ChangeReviewSeverity severity)
        {
            var notice = new VisualElement();
            notice.AddToClassList("yucp-review-notice");
            notice.AddToClassList(
                severity == ChangeReviewSeverity.Blocking
                    ? "yucp-review-notice--blocking"
                    : "yucp-review-notice--caution");
            var label = new Label(message);
            label.AddToClassList("yucp-review-notice-text");
            notice.Add(label);
            return notice;
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

        private void RefreshLicensedAssetSummarySection()
        {
            if (_contentsSection == null)
            {
                return;
            }

            var container = _contentsSection.Q<VisualElement>("licensed-summary-container");
            if (container == null)
            {
                return;
            }

            container.Clear();
            RefreshLicensedAssetDescriptors();

            if (_licensedAssetDescriptors.Count == 0)
            {
                return;
            }

            var card = new VisualElement();
            card.AddToClassList("yucp-licensed-summary-card");

            // Header: eyebrow + count
            var cardHeader = new VisualElement();
            cardHeader.style.flexDirection = FlexDirection.Row;
            cardHeader.style.alignItems = Align.Center;
            cardHeader.style.marginBottom = 4;

            var title = new Label("LICENSED CONTENT");
            title.AddToClassList("yucp-licensed-summary-title");
            title.style.flexGrow = 1;
            cardHeader.Add(title);

            int assetCount = _licensedAssetDescriptors.Count;
            var countLabel = new Label($"{assetCount} asset{(assetCount == 1 ? "" : "s")} locked");
            countLabel.AddToClassList("yucp-licensed-summary-count");
            cardHeader.Add(countLabel);

            card.Add(cardHeader);

            var previewList = new VisualElement();
            previewList.AddToClassList("yucp-licensed-preview-list");

            foreach (var descriptor in _licensedAssetDescriptors.Take(4))
            {
                var row = new VisualElement();
                row.AddToClassList("yucp-licensed-preview-item");
                row.tooltip = descriptor.destinationPath;

                var lockIcon = new Label("◈");
                lockIcon.AddToClassList("yucp-licensed-preview-icon");
                row.Add(lockIcon);

                var nameLabel = new Label(descriptor.displayName);
                nameLabel.style.flexGrow = 1;
                nameLabel.style.overflow = Overflow.Hidden;
                row.Add(nameLabel);

                previewList.Add(row);
            }

            if (_licensedAssetDescriptors.Count > 4)
            {
                var moreLabel = new Label($"+ {_licensedAssetDescriptors.Count - 4} more");
                moreLabel.AddToClassList("yucp-licensed-preview-more");
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

        private void RefreshLicensedAssetDescriptors()
        {
            _licensedAssetDescriptors.Clear();
            _licensedAssetPaths.Clear();

            var items = _allImportItems ?? _currentImportItems;
            if (items == null || items.Length == 0)
            {
                return;
            }

            foreach (var item in items)
            {
                if (!TryBuildLicensedAssetDescriptor(item, out var descriptor))
                {
                    continue;
                }

                descriptor.destinationPath = NormalizeImportPath(descriptor.destinationPath);
                if (string.IsNullOrEmpty(descriptor.destinationPath))
                {
                    continue;
                }

                if (_licensedAssetPaths.Add(descriptor.destinationPath))
                {
                    _licensedAssetDescriptors.Add(descriptor);
                }
            }
        }

        private static bool TryBuildLicensedAssetDescriptor(
            object item,
            out LicensedAssetDescriptor descriptor)
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

            descriptor = new LicensedAssetDescriptor
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
        /// <summary>
        /// Updates a button label without overlapping custom child content.
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

        private List<string> GetLicensedAssetPaths()
        {
            RefreshLicensedAssetDescriptors();
            return _licensedAssetDescriptors.Select(descriptor => descriptor.destinationPath).ToList();
        }

        private bool IsLicensedAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalized = NormalizeImportPath(path);
            return _licensedAssetPaths.Contains(normalized);
        }

        private void BuildLicenseSection()
        {
            if (_licenseSection == null)
            {
                return;
            }
            _licenseSection.Clear();

            bool showBrokerAuthentication = _isAliasBootstrapFlow;
            if (!showBrokerAuthentication &&
                !RequiresVerificationBeforeImport())
            {
                _licenseSection.style.display = DisplayStyle.None;
                UpdateImportButtonEnabled();
                return;
            }

            _licenseSection.style.display = DisplayStyle.Flex;
            _licenseSection.RemoveFromClassList("yucp-license-gate");
            _licenseSection.AddToClassList("lgate-root");
            _licenseSection.Add(
                showBrokerAuthentication
                    ? BuildAuthenticationSection()
                    : BuildVerificationServerNotice());

            VisualElement storefrontActions =
                BuildStorefrontActionsRow();
            if (storefrontActions != null)
            {
                _licenseSection.Add(storefrontActions);
            }
            UpdateImportButtonEnabled();
        }

        private VisualElement BuildAuthenticationSection()
        {
            var block = new VisualElement();
            block.AddToClassList("lgate-req-block");
            block.style.borderBottomWidth = 0;
            block.style.paddingBottom = 10;

            if (AuthenticationActionInFlight ||
                !_isBrokerSignedIn.HasValue)
            {
                var checkingTitle = new Label(
                    AuthenticationActionInFlight
                        ? "Updating YUCP sign-in..."
                        : "Checking YUCP sign-in...");
                checkingTitle.AddToClassList("lgate-req-name");
                block.Add(checkingTitle);
                block.Add(BuildBuyerFlowNote(
                    "Checking the Windows-protected account saved for secure package delivery."));
                return block;
            }

            if (_isBrokerSignedIn.Value)
            {
                var signedInTitle = new Label("Signed in with YUCP");
                signedInTitle.AddToClassList("lgate-req-name");
                block.Add(signedInTitle);
                block.Add(BuildBuyerFlowNote(
                    "Your saved account is verified and ready for this package.",
                    "You can also revoke YUCP Package Broker access from Authorized applications on the YUCP website."));
                var signOutButton = new Button(OnBrokerSignOutClicked)
                {
                    text = "Sign out",
                };
                signOutButton.AddToClassList("lgate-link-btn");
                signOutButton.SetEnabled(
                    !AuthenticationActionInFlight);
                block.Add(signOutButton);
                return block;
            }

            var signedOutTitle = new Label("Sign in to continue");
            signedOutTitle.AddToClassList("lgate-req-name");
            block.Add(signedOutTitle);
            block.Add(BuildBuyerFlowNote(
                "Sign in with the YUCP account that owns this product.",
                "Your browser opens only when Windows does not already have a valid saved YUCP session."));
            var signInButton = new Button(OnBrokerSignInClicked)
            {
                text = "Sign in with YUCP",
            };
            signInButton.AddToClassList("lgate-solid-btn");
            signInButton.SetEnabled(!AuthenticationActionInFlight);
            block.Add(signInButton);
            return block;
        }

        private void ScheduleAuthenticationRefresh()
        {
            if (!_isAliasBootstrapFlow ||
                _authenticationRefreshScheduled ||
                AuthenticationActionInFlight)
            {
                return;
            }
            _authenticationRefreshScheduled = true;
            EditorApplication.delayCall += async () =>
            {
                _authenticationRefreshScheduled = false;
                if (this != null)
                {
                    await RefreshAuthenticationStatusAsync();
                }
            };
        }

        /// <summary>
        /// The sign-in answer is cached from one status call, so a session that
        /// expires afterwards still reads as signed in and the operation fails
        /// on the far side. The broker saying it needs authentication is the
        /// authoritative answer, so believe it over the cache and put the screen
        /// back into its signed-out state instead of reporting a dead error.
        /// </summary>
        private bool HandleAuthenticationLoss(string errorCode)
        {
            if (!string.Equals(
                    errorCode,
                    "AUTHENTICATION_REQUIRED",
                    StringComparison.Ordinal))
            {
                return false;
            }
            _isBrokerSignedIn = false;
            BuildLicenseSection();
            UpdateImportButtonEnabled();
            ShowFlowNotice(
                "Sign in to continue",
                "This YUCP sign-in is no longer valid. Sign in, then start the " +
                "package action again.",
                FlowNoticeTone.Info);
            _ = RefreshAuthenticationStatusAsync();
            return true;
        }

        private async Task RefreshAuthenticationStatusAsync()
        {
            if (AuthenticationActionInFlight || !_isAliasBootstrapFlow)
            {
                return;
            }
            _authenticationOperation =
                BrokerAuthenticationOperation.Refresh;
            BuildLicenseSection();
            try
            {
                NativePackageBrokerAuthenticationResult result =
                    await PackageLifecycleCoordinator
                        .GetAuthenticationStatusAsync();
                if (this != null)
                {
                    _isBrokerSignedIn = result.signedIn;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[YUCP PackageManager] Could not verify saved sign-in: " +
                    exception.GetType().Name);
                if (this != null)
                {
                    _isBrokerSignedIn = false;
                    ShowFlowNotice(
                        "Sign-in status unavailable",
                        "Sign in with YUCP to reconnect secure package delivery.",
                        FlowNoticeTone.Info);
                }
            }
            finally
            {
                if (this != null)
                {
                    _authenticationOperation =
                        BrokerAuthenticationOperation.None;
                    BuildLicenseSection();
                }
            }
        }

        private async void OnBrokerSignInClicked()
        {
            await SignInWithBrokerAsync();
        }

        private async Task<bool> SignInWithBrokerAsync()
        {
            if (AuthenticationActionInFlight)
            {
                return false;
            }
            _authenticationOperation =
                BrokerAuthenticationOperation.SignIn;
            BuildLicenseSection();
            try
            {
                NativePackageBrokerAuthenticationResult result =
                    await PackageLifecycleCoordinator.SignInAsync();
                if (this != null)
                {
                    _isBrokerSignedIn = result.signedIn;
                    ShowFlowNotice(
                        "Signed in",
                        "Your YUCP account is ready for secure package delivery.",
                        FlowNoticeTone.Success);
                }
                return result.signedIn;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[YUCP PackageManager] YUCP sign-in failed: " +
                    exception.GetType().Name);
                if (this != null)
                {
                    _isBrokerSignedIn = false;
                    ShowFlowNotice(
                        "Sign-in failed",
                        "Could not finish YUCP sign-in. Try again.",
                        FlowNoticeTone.Error);
                }
                return false;
            }
            finally
            {
                if (this != null)
                {
                    _authenticationOperation =
                        BrokerAuthenticationOperation.None;
                    BuildLicenseSection();
                }
            }
        }

        private async void OnBrokerSignOutClicked()
        {
            if (AuthenticationActionInFlight)
            {
                return;
            }
            _authenticationOperation =
                BrokerAuthenticationOperation.SignOut;
            BuildLicenseSection();
            try
            {
                await PackageLifecycleCoordinator.SignOutAsync();
                if (this != null)
                {
                    _isBrokerSignedIn = false;
                    ShowFlowNotice(
                        "Signed out",
                        "The saved YUCP package session was removed from Windows.",
                        FlowNoticeTone.Success);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[YUCP PackageManager] YUCP sign-out failed: " +
                    exception.GetType().Name);
                if (this != null)
                {
                    ShowFlowNotice(
                        "Sign-out failed",
                        "Could not finish signing out. Try again.",
                        FlowNoticeTone.Error);
                }
            }
            finally
            {
                if (this != null)
                {
                    _authenticationOperation =
                        BrokerAuthenticationOperation.None;
                    BuildLicenseSection();
                }
            }
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

        private void UpdateImportButtonEnabled()
        {
            if (_importButton == null) return;
            if (ChangeReviewPending)
            {
                SetImportButtonProgress(-1f);
                SetVerifyStatusLabel(null);
                return;
            }
            bool hasUnverifiedLicense = RequiresVerificationBeforeImport();
            bool requiresExternalBootstrap =
                hasUnverifiedLicense && !_isAliasBootstrapFlow;
            bool isCheckingBrokerAuthentication =
                _isAliasBootstrapFlow &&
                (!_isBrokerSignedIn.HasValue ||
                 AuthenticationActionInFlight);
            bool requiresExplicitBootstrap =
                _isAliasBootstrapFlow &&
                IsCurrentAliasInstalled() &&
                !HasUnhandledVersionedBootstrap();

            if (_pendingImportAfterVerification)
            {
                // Disabling tints the whole control, which drains the white out
                // of the progress bar. Left enabled and unclickable instead.
                _importButton.SetEnabled(true);
                _importButton.pickingMode = PickingMode.Ignore;
                _importButton.tooltip =
                    "YUCP is preparing and checking this package.";
                const string statusText =
                    "YUCP is preparing your package...";
                _importButton.Clear();
                _importProgressFill = null;
                UpdateButtonLabel(_importButton, "Preparing...");
                AnimateImportButtonWidth();
                SetImportButtonProgress(null);
                SetVerifyStatusLabel(statusText);
                return;
            }

            StopLifecycleProgressMessage();
            SetVerifyStatusLabel(null);
            SetImportButtonProgress(-1f);
            _importButton.pickingMode = PickingMode.Position;
            _importButton.SetEnabled(
                !requiresExternalBootstrap &&
                !isCheckingBrokerAuthentication &&
                !requiresExplicitBootstrap);
            _importButton.tooltip = requiresExternalBootstrap
                ? "Install this product through its Creator Companion bootstrap."
                : isCheckingBrokerAuthentication
                    ? "Checking the Windows-protected YUCP account."
                : requiresExplicitBootstrap
                    ? "Install an exact version through VCC or import a new bootstrap."
                : _isAliasBootstrapFlow && _isBrokerSignedIn == false
                    ? "Sign in with YUCP to continue."
                : string.Empty;
            RefreshPrimaryImportButton();
        }

        private void SetVerifyStatusLabel(string text)
        {
            if (_verifyStatusLabel == null) return;
            if (string.IsNullOrEmpty(text))
            {
                _verifyStatusLabel.RemoveFromClassList("yucp-verify-status--visible");
            }
            else
            {
                _verifyStatusLabel.text = text;
                _verifyStatusLabel.AddToClassList("yucp-verify-status--visible");
            }
        }

        private VisualElement CreateFlowNotice()
        {
            var notice = new VisualElement();
            notice.AddToClassList("yucp-flow-notice");

            _flowNoticeTitleLabel = new Label();
            _flowNoticeTitleLabel.AddToClassList("yucp-flow-notice-title");
            notice.Add(_flowNoticeTitleLabel);

            _flowNoticeBodyLabel = new Label();
            _flowNoticeBodyLabel.AddToClassList("yucp-flow-notice-body");
            notice.Add(_flowNoticeBodyLabel);

            _flowNoticeLinkLabel = new Label("Contact us in our Discord");
            _flowNoticeLinkLabel.AddToClassList("yucp-flow-notice-link");
            _flowNoticeLinkLabel.style.display = DisplayStyle.None;
            _flowNoticeLinkLabel.RegisterCallback<ClickEvent>(
                _ => Application.OpenURL(SupportDiscordUrl));
            _flowNoticeLinkLabel.RegisterCallback<MouseEnterEvent>(
                _ => _flowNoticeLinkLabel.AddToClassList(
                    "yucp-flow-notice-link--hover"));
            _flowNoticeLinkLabel.RegisterCallback<MouseLeaveEvent>(
                _ => _flowNoticeLinkLabel.RemoveFromClassList(
                    "yucp-flow-notice-link--hover"));
            notice.Add(_flowNoticeLinkLabel);

            return notice;
        }

        private void ShowFlowNotice(string title, string body, FlowNoticeTone tone)
        {
            if (_flowNoticeElement == null)
            {
                return;
            }

            _flowNoticeElement.RemoveFromClassList("yucp-flow-notice-info");
            _flowNoticeElement.RemoveFromClassList("yucp-flow-notice-success");
            _flowNoticeElement.RemoveFromClassList("yucp-flow-notice-error");
            _flowNoticeElement.AddToClassList(tone switch
            {
                FlowNoticeTone.Success => "yucp-flow-notice-success",
                FlowNoticeTone.Error => "yucp-flow-notice-error",
                _ => "yucp-flow-notice-info"
            });

            _flowNoticeTitleLabel.text = string.IsNullOrWhiteSpace(title) ? "YUCP Installer" : title.Trim();
            _flowNoticeBodyLabel.text = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
            _flowNoticeBodyLabel.style.display = string.IsNullOrWhiteSpace(_flowNoticeBodyLabel.text)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            if (_flowNoticeLinkLabel != null)
            {
                _flowNoticeLinkLabel.style.display = tone == FlowNoticeTone.Error
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
            _flowNoticeElement.AddToClassList("yucp-flow-notice--visible");
        }

        private void ClearFlowNotice()
        {
            if (_flowNoticeElement == null)
            {
                return;
            }

            _flowNoticeElement.RemoveFromClassList("yucp-flow-notice--visible");
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

            var title = new Label("Product bootstrap required");
            title.AddToClassList("lgate-req-name");
            block.Add(title);

            block.Add(BuildBuyerFlowNote(
                "Install this product through Creator Companion.",
                "YUCP will handle sign-in and purchase verification " +
                "outside Unity."));

            return block;
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
            int licensedCount = GetLicensedAssetPaths().Count;

            if (dependencyCount > 0 || licensedCount > 0)
            {
                var bits = new List<string>();
                if (dependencyCount > 0)
                {
                    bits.Add($"{dependencyCount} required package{(dependencyCount == 1 ? string.Empty : "s")}");
                }

                if (licensedCount > 0)
                {
                    bits.Add($"{licensedCount} licensed");
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
            container.style.flexShrink = 0;
            container.style.minHeight = 0;

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

            var dependenciesScroll = new ScrollView(ScrollViewMode.Vertical);
            dependenciesScroll.style.flexGrow = 0;
            dependenciesScroll.style.flexShrink = 1;
            dependenciesScroll.style.minHeight = 0;
            dependenciesScroll.Add(dependenciesList);
            container.Add(dependenciesScroll);

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
            _treeView.SetTree(new PackageItemNode("Assets", "Assets", true, 0));
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
        /// Initialize the importer for a public VPM alias.
        /// </summary>
        internal void InitializeForAlias(
            PackageMetadata metadata,
            bool showWindow)
        {
            if (metadata?.aliasPackage == null ||
                !AliasPackageDiscovery.IsServerAuthorized(
                    metadata.aliasPackage))
            {
                throw new ArgumentException(
                    "A server-authorized alias is required.",
                    nameof(metadata));
            }

            _hasPendingImportContext = true;
            _isAliasBootstrapFlow = true;
            _currentImportItems = null;
            _allImportItems = null;
            _currentPackagePath = string.Empty;
            _currentPackageIconPath = string.Empty;
            _packageImportWizardInstance = null;
            _isProjectSettingsStep = false;
            _detailsExpanded = false;
            _preferOverwriteExisting = true;

            titleContent = new GUIContent("YUCP Importer");
            minSize = new Vector2(500, 600);
            SetMetadata(metadata);
            UpdateButtonStates();
            RefreshUI();
            RestorePendingInstallerView();
            SchedulePendingLifecycleResume();
            ScheduleAuthenticationRefresh();

            if (showWindow)
            {
                ShowUtility();
                Focus();
            }
        }

        internal static string[] GetHostedLifecycleActionLabels(
            bool installed,
            bool hasRollback)
        {
            if (!installed)
            {
                return Array.Empty<string>();
            }
            var actions = new List<string>
            {
                "Repair",
            };
            if (hasRollback)
            {
                actions.Add("Roll back");
            }
            actions.Add("Uninstall");
            return actions.ToArray();
        }

        private void AddHostedLifecycleControls(VisualElement parent)
        {
            _hostedLifecycleButtons.Clear();
            if (!_isAliasBootstrapFlow || !IsCurrentAliasInstalled())
            {
                return;
            }
            bool hasRollback = HasCurrentAliasRollback();
            string[] actions = GetHostedLifecycleActionLabels(
                true,
                hasRollback);
            var row = new VisualElement();
            _hostedLifecycleControls = row;
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 8;
            foreach (string action in actions)
            {
                string operation = action == "Roll back"
                    ? "rollback"
                    : action.ToLowerInvariant();
                var button = new Button(
                    () => RunHostedLifecycleAction(operation))
                {
                    text = action,
                };
                button.AddToClassList("yucp-cta-cancel");
                button.style.marginLeft = 6;
                button.style.marginBottom = 4;
                row.Add(button);
                _hostedLifecycleButtons.Add(button);
            }
            parent.Add(row);
        }

        /// <summary>
        /// The lifecycle store addresses its state by alias id, so an import
        /// carrying no usable alias is simply not installed rather than an
        /// error thrown while the window is still being built.
        /// </summary>
        private bool TryGetLifecycleAlias(out AliasPackageContract alias)
        {
            alias = (_currentMetadata ?? _cachedMetadata)?.aliasPackage;
            return alias != null && PackageProtocolIdentifier.IsSafe(alias.aliasId);
        }

        private bool IsCurrentAliasInstalled()
        {
            if (!TryGetLifecycleAlias(out AliasPackageContract alias))
            {
                return false;
            }
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            return PackageLifecycleCoordinator.GetCurrentReleaseRoot(
                    projectPath,
                    alias.aliasId) !=
                PackageLifecycleCoordinator.EmptyReleaseRoot;
        }

        private bool HasUnhandledVersionedBootstrap()
        {
            if (!TryGetLifecycleAlias(out AliasPackageContract alias))
            {
                return false;
            }
            if (!string.Equals(
                    alias.kind,
                    "alias-v2",
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(
                    alias.bootstrapIntent?.intentId))
            {
                return false;
            }
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            return !AliasPackageActivationStateStore.IsHandled(
                projectPath,
                alias);
        }

        private bool HasCurrentAliasRollback()
        {
            if (!TryGetLifecycleAlias(out AliasPackageContract alias))
            {
                return false;
            }
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            return PackageLifecycleCoordinator.HasPriorRelease(
                projectPath,
                alias.aliasId);
        }

        private async void RunHostedLifecycleAction(string operation)
        {
            if (_isHostedLifecycleRunning)
            {
                return;
            }
            PackageMetadata metadata = _currentMetadata ?? _cachedMetadata;
            if (metadata?.aliasPackage == null)
            {
                return;
            }
            _isHostedLifecycleRunning = true;
            SetHostedLifecycleControlsEnabled(false);
            _importButton?.SetEnabled(false);
            SetVerifyStatusLabel(ActionPendingMessage(operation));
            try
            {
                PackageLifecycleInstallResult result =
                    await PackageLifecycleCoordinator
                        .TryManageInstalledAsync(
                            metadata.aliasPackage,
                            operation,
                            CreateLifecycleProgressReporter(
                                "Managing Package"));
                if (this == null)
                {
                    return;
                }
                if (!result.succeeded)
                {
                    if (HandleAuthenticationLoss(result.errorCode))
                    {
                        return;
                    }
                    ShowFlowNotice(
                        "Package action could not finish",
                        result.errorMessage,
                        FlowNoticeTone.Error);
                    return;
                }
                ShowFlowNotice(
                    ActionSuccessTitle(operation),
                    ActionSuccessMessage(operation),
                    FlowNoticeTone.Success);
                SetVerifyStatusLabel(ActionSuccessMessage(operation));
                RefreshPrimaryImportButton();
                if (operation == "uninstall" &&
                    _hostedLifecycleControls != null)
                {
                    _hostedLifecycleControls.style.display =
                        DisplayStyle.None;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "YUCP package management failed: " +
                    exception.GetType().Name);
                if (this != null)
                {
                    ShowFlowNotice(
                        "Package action could not finish",
                        "Try the action again. Contact support if the problem continues.",
                        FlowNoticeTone.Error);
                    SetVerifyStatusLabel(
                        "The package action could not finish.");
                }
            }
            finally
            {
                SetImportButtonProgress(-1f);
                if (this != null)
                {
                    _isHostedLifecycleRunning = false;
                    SetHostedLifecycleControlsEnabled(true);
                    UpdateImportButtonEnabled();
                }
            }
        }

        private void SchedulePendingLifecycleResume()
        {
            if (_pendingLifecycleResumeScheduled ||
                !_isAliasBootstrapFlow ||
                !TryGetLifecycleAlias(out AliasPackageContract alias))
            {
                return;
            }
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            if (PackageLifecycleCoordinator.GetPendingOperation(
                    projectPath,
                    alias.aliasId) == null)
            {
                return;
            }

            _pendingLifecycleResumeScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _pendingLifecycleResumeScheduled = false;
                if (this != null)
                {
                    ResumePendingLifecycleAfterReload();
                }
            };
        }

        private async void ResumePendingLifecycleAfterReload()
        {
            if (_isHostedLifecycleRunning)
            {
                return;
            }
            PackageMetadata metadata = _currentMetadata ?? _cachedMetadata;
            if (metadata?.aliasPackage == null)
            {
                return;
            }

            _isHostedLifecycleRunning = true;
            _pendingImportAfterVerification = true;
            SetHostedLifecycleControlsEnabled(false);
            _importButton?.SetEnabled(false);
            SetVerifyStatusLabel(
                "Finishing the interrupted package installation...");
            try
            {
                PackageLifecycleInstallResult result =
                    await PackageLifecycleCoordinator.TryResumePendingAsync(
                        metadata.aliasPackage,
                        CreateLifecycleProgressReporter(
                            "Finishing Package Installation"));
                if (this == null)
                {
                    return;
                }
                if (result == null)
                {
                    return;
                }
                if (!result.succeeded)
                {
                    if (HandleAuthenticationLoss(result.errorCode))
                    {
                        return;
                    }
                    ShowFlowNotice(
                        "Package recovery could not finish",
                        result.errorMessage,
                        FlowNoticeTone.Error);
                    return;
                }

                AliasPackageActivation.DismissForSession(
                    metadata.aliasPackage);
                CompleteAliasInstallFlow(metadata.packageName);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "YUCP package recovery failed: " +
                    exception.GetType().Name);
                if (this != null)
                {
                    ShowFlowNotice(
                        "Package recovery could not finish",
                        "Try again. Contact support if the problem continues.",
                        FlowNoticeTone.Error);
                }
            }
            finally
            {
                SetImportButtonProgress(-1f);
                if (this != null)
                {
                    _isHostedLifecycleRunning = false;
                    _pendingImportAfterVerification = false;
                    SetHostedLifecycleControlsEnabled(true);
                    UpdateImportButtonEnabled();
                }
            }
        }

        private Action<PackageLifecycleUserProgress>
            CreateLifecycleProgressReporter(string title)
        {
            PackageManagerWindow window = this;
            return progress =>
            {
                if (window == null)
                {
                    return;
                }
                window.SetLifecycleProgressMessage(progress.message);
                window.SetImportButtonProgress(
                    progress.progress > 0f ? progress.progress : (float?)null);
                window.Repaint();
            };
        }

        /// <summary>
        /// Shows the message with a running elapsed time, so a step that takes
        /// minutes does not read as a stall.
        /// </summary>
        private void SetLifecycleProgressMessage(string message)
        {
            _lifecycleProgressMessage = message ?? string.Empty;
            if (_lifecycleProgressStartedAt <= 0d)
            {
                _lifecycleProgressStartedAt = EditorApplication.timeSinceStartup;
            }
            RenderLifecycleProgressMessage();
            if (_lifecycleProgressTicker == null && rootVisualElement != null)
            {
                _lifecycleProgressTicker = rootVisualElement.schedule
                    .Execute(RenderLifecycleProgressMessage)
                    .Every(1000);
            }
        }

        private void RenderLifecycleProgressMessage()
        {
            if (string.IsNullOrEmpty(_lifecycleProgressMessage))
            {
                return;
            }
            double elapsed =
                EditorApplication.timeSinceStartup - _lifecycleProgressStartedAt;
            SetVerifyStatusLabel(
                elapsed >= 10d
                    ? $"{_lifecycleProgressMessage}   {FormatElapsed(elapsed)}"
                    : _lifecycleProgressMessage);
        }

        private void StopLifecycleProgressMessage()
        {
            _lifecycleProgressTicker?.Pause();
            _lifecycleProgressTicker = null;
            _lifecycleProgressMessage = string.Empty;
            _lifecycleProgressStartedAt = 0d;
        }

        private static string FormatElapsed(double seconds)
        {
            int total = (int)Math.Max(0d, seconds);
            int minutes = total / 60;
            return minutes > 0
                ? $"{minutes}m {total % 60}s"
                : $"{total}s";
        }

        private void SetHostedLifecycleControlsEnabled(bool enabled)
        {
            foreach (Button button in _hostedLifecycleButtons)
            {
                button.SetEnabled(enabled);
            }
        }

        private static string ActionPendingMessage(string operation)
        {
            switch (operation)
            {
                case "repair":
                    return "Checking and repairing the package...";
                case "rollback":
                    return "Restoring the earlier package version...";
                case "uninstall":
                    return "Removing the package files...";
                default:
                    return "Updating the package...";
            }
        }

        private static string ActionSuccessTitle(string operation)
        {
            switch (operation)
            {
                case "repair":
                    return "Package repaired";
                case "rollback":
                    return "Earlier version restored";
                case "uninstall":
                    return "Package uninstalled";
                default:
                    return "Package updated";
            }
        }

        private static string ActionSuccessMessage(string operation)
        {
            if (operation == "uninstall")
            {
                return "YUCP removed unchanged package files. " +
                    "Files you changed were kept.";
            }
            return operation == "repair"
                ? "YUCP checked the installed files and repaired the package."
                : operation == "rollback"
                    ? "YUCP restored the previous installed version."
                    : "The package is up to date.";
        }

        /// <summary>
        /// Initialize window for package import with metadata and import items.
        /// </summary>
        public void InitializeForImport(string packagePath, System.Array importItems, System.Array allImportItems, string packageIconPath, object wizardInstance, bool isProjectSettingsStep)
        {
            _hasPendingImportContext = true;
            _isAliasBootstrapFlow = false;

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
            RefreshLicensedAssetDescriptors();

            Debug.Log($"[YUCP PackageManager] InitializeForImport: packagePath='{packagePath}', stepItems={GetImportItemCount(importItems)}, allItems={GetImportItemCount(_allImportItems)}, packageIconPath='{packageIconPath}', isProjectSettingsStep={isProjectSettingsStep}");

            // Set window title to match Unity's default
            titleContent = new GUIContent("Import Unity Package");
            
            // Set minimum window size
            minSize = new Vector2(500, 600);

            // Verify package signature FIRST (synchronously) before setting up UI
            // This ensures verification completes before UI elements are displayed
            VerifyPackage(packagePath);

            // Update button visibility and text based on wizard state
            UpdateButtonStates();

            // Extract metadata from ALL import items (not just current step) to find icon/banner
            // Also pass packageIconPath to extract icon even if no YUCP metadata exists
            var metadata = PackageMetadataExtractor.ExtractMetadataFromImportItems(allImportItems ?? importItems, packagePath, packageIconPath);
            SetMetadata(metadata);
            TryNormalizeAliasMetadataDisplay();
            s_lastImportMetadata = metadata;
            s_lastImportPackagePath = packagePath;
            LogTempInstallStatus();

            // For server-authorized alias packages the embedded metadata is intentionally minimal;
            // pull the real title/version/creator/media from the server so the installer shows what
            // is actually being imported. Best-effort and only when already signed in.

            // Build tree from current step's import items
            SetImportItems(importItems);

            // Refresh UI now that everything is set up (including verification result)
            RefreshUI();
            RestorePendingInstallerView();

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
            RefreshLicensedAssetDescriptors();
            if (_treeView != null)
            {
                BuildTreeFromImportItems();
            }
        }

        private void ShowSampleMetadata()
        {
            // For now, create sample metadata to demonstrate UI
            SetMetadata(new PackageMetadata
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
            });
        }

        private void RefreshUI()
        {
            // Initialization can provide metadata before Unity calls CreateGUI.
            // Retain that state and render it when the visual tree is ready.
            if (_currentViewContainer == null || _installerRoot == null)
            {
                return;
            }

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
            RefreshLicensedAssetDescriptors();
            RefreshDependenciesSection();
            RefreshLicensedAssetSummarySection();
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
            PackageMetadata replacement =
                metadata ?? new PackageMetadata();
            PackageMetadataMediaOwnership.Release(
                _currentMetadata,
                replacement);
            if (!ReferenceEquals(_currentMetadata, _cachedMetadata))
            {
                PackageMetadataMediaOwnership.Release(
                    _cachedMetadata,
                    replacement);
            }
            _cachedMetadata = replacement;
            _currentMetadata = _cachedMetadata;
            if (!string.IsNullOrEmpty(_currentPackagePath))
            {
                s_lastImportMetadata = _cachedMetadata;
                s_lastImportPackagePath = _currentPackagePath;
            }
            RefreshUI();
            BuildLicenseSection();
        }

        /// <summary>
        /// Use public alias fields before the server issues an install session.
        /// </summary>
        private void TryNormalizeAliasMetadataDisplay()
        {
            PackageMetadata current = _currentMetadata ?? _cachedMetadata;
            AliasPackageContract alias = current?.aliasPackage;
            if (alias == null || !AliasPackageDiscovery.IsServerAuthorized(alias))
            {
                return;
            }

            // Applied inline: a deferred callback is not guaranteed to run before
            // the installer is shown, which left the title and version blank.
            try
            {
                if (!string.IsNullOrWhiteSpace(alias.packageDisplayName))
                {
                    current.packageName = alias.packageDisplayName;
                }
                if (!string.IsNullOrWhiteSpace(alias.packageVersion))
                {
                    current.version = alias.packageVersion;
                }
                SetMetadata(current);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Alias metadata normalization failed: {ex.Message}");
            }
        }

        private void OnImportPackageStarted(string packageName)
        {
            // Try to extract metadata using reflection to access Unity's internal import items
            // Unity's PackageImport.ShowImportPackage is called with packagePath, items, and iconPath
            // We need to intercept this or extract from the package file directly
            
            // For now, create fallback metadata from package name
            // In the future, we'll extract from ImportPackageItem[] array using reflection
            SetMetadata(new PackageMetadata(packageName));
            
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

                    // Unlock assembly reload (import is complete)
                    if (_isImportMode)
                    {
                        EditorApplication.UnlockReloadAssemblies();
                        _isImportMode = false;
                        Debug.Log("[YUCP PackageManager] Unlocked assembly reload (import complete)");
                    }

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

        private async void OnImportClicked()
        {
            try
            {
                if (ChangeReviewPending)
                {
                    if (_changeReviewBlocked)
                    {
                        return;
                    }
                    CompletePendingChangeReview(true);
                    UpdateImportButtonEnabled();
                    return;
                }
                ClearFlowNotice();

                if (_isAliasBootstrapFlow)
                {
                    // No remembered answer is good enough to start work on.
                    if (!AuthenticationActionInFlight)
                    {
                        await RefreshAuthenticationStatusAsync();
                        if (this == null)
                        {
                            return;
                        }
                    }
                    if (_isBrokerSignedIn != true)
                    {
                        if (AuthenticationActionInFlight ||
                            !await SignInWithBrokerAsync())
                        {
                            return;
                        }
                    }
                }

                PackageMetadata installMetadata =
                    _currentMetadata ?? _cachedMetadata;
                if (installMetadata?.aliasPackage != null &&
                    AliasPackageDiscovery.IsServerAuthorized(
                        installMetadata.aliasPackage))
                {
                    await VerifyAndInstallAliasAsync(installMetadata);
                    return;
                }

                if (_currentImportItems == null || _currentImportItems.Length == 0)
                {
                    PackageMetadata pendingMetadata =
                        _currentMetadata ?? _cachedMetadata;
                    if (_isAliasBootstrapFlow &&
                        AliasPackageDiscovery.IsServerAuthorized(
                            pendingMetadata?.aliasPackage))
                    {
                        AliasPackageActivation.DismissForSession(
                            pendingMetadata.aliasPackage);
                    }

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
                Debug.Log(
                    "[YUCP PackageManager] Direct package verification " +
                    $"required={requiresVerification}.");

                if (requiresVerification)
                {
                    _pendingImportAfterVerification = false;
                    ShowFlowNotice(
                        "Use the product bootstrap",
                        "This direct package uses an older verification " +
                        "format. Install its product bootstrap through " +
                        "Creator Companion.",
                        FlowNoticeTone.Error);
                    UpdateImportButtonEnabled();
                    return;
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

                Debug.Log(
                    "[YUCP PackageManager] Import initiated. " +
                    $"packageSigned={_isPackageSigned}, " +
                    "signatureValid=" +
                    $"{_verificationResult != null && _verificationResult.valid}.");

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
                ShowFlowNotice("Import failed", $"Failed to import package: {ex.Message}", FlowNoticeTone.Error);
            }
        }

        private async Task VerifyAndInstallAliasAsync(
            PackageMetadata metadata)
        {
            _pendingImportAfterVerification = true;
            UpdateImportButtonEnabled();

            try
            {
                PackageLifecycleInstallResult installResult =
                    await PackageLifecycleCoordinator.TryInstallAsync(
                        metadata.aliasPackage,
                        CreateLifecycleProgressReporter(
                            "Installing Package"));
                if (this == null)
                {
                    return;
                }
                if (installResult.cancelled)
                {
                    ShowFlowNotice(
                        "Installation canceled",
                        installResult.errorMessage,
                        FlowNoticeTone.Info);
                    return;
                }
                if (!installResult.succeeded)
                {
                    if (HandleAuthenticationLoss(installResult.errorCode))
                    {
                        return;
                    }
                    ShowFlowNotice(
                        "Install Package",
                        installResult.errorMessage,
                        FlowNoticeTone.Error);
                    return;
                }
                if (installResult.alreadyInstalled)
                {
                    AliasPackageActivation.DismissForSession(
                        metadata.aliasPackage);
                    ShowFlowNotice(
                        "Already installed",
                        "This project already has the release requested by this bootstrap.",
                        FlowNoticeTone.Success);
                    return;
                }

                AliasPackageActivation.DismissForSession(
                    metadata.aliasPackage);
                CompleteAliasInstallFlow(metadata.packageName);
            }
            finally
            {
                SetImportButtonProgress(-1f);
                if (this != null)
                {
                    _pendingImportAfterVerification = false;
                    UpdateImportButtonEnabled();
                }
            }
        }

        private void OnCancelClicked()
        {
            try
            {
                if (ChangeReviewPending)
                {
                    CompletePendingChangeReview(false);
                    UpdateImportButtonEnabled();
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

        // Installer view management
        private void ShowInstallerView()
        {
            _currentViewContainer.Clear();
            if (_installerRoot != null)
            {
                _installerRoot.style.display = DisplayStyle.Flex;
                _currentViewContainer.Add(_installerRoot);
            }
        }

        private void CompleteAliasInstallFlow(string packageName)
        {
            _waitingForImportCompletion = false;
            _pendingImportAfterVerification = false;
            _pendingPackageName = string.Empty;

            if (_isImportMode)
            {
                EditorApplication.UnlockReloadAssemblies();
                _isImportMode = false;
                Debug.Log("[YUCP PackageManager] Unlocked assembly reload (alias install complete)");
            }

            Debug.Log(
                $"[YUCP PackageManager] Installed '{packageName}'. " +
                "Its VPM package remains registered for updates.");

            try
            {
                Close();
                GUIUtility.ExitGUI();
            }
            catch (ExitGUIException)
            {
                // Expected when closing the import window.
            }
        }

        private bool RegisterPackageAfterImport()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentPackagePath))
                    return false;

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

                    if (string.IsNullOrEmpty(packageId) && !string.IsNullOrWhiteSpace(metadata?.aliasPackage?.aliasId))
                    {
                        packageId = metadata.aliasPackage.aliasId.Trim();
                        Debug.Log($"[YUCP PackageManager] Falling back to alias contract packageId during registration. packageId='{packageId}'");
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
                    return false;
                }

                if (AliasPackageInstallStateStore.TryPersist(installedInfo, out string installStateManifestPath, out string installStateError))
                {
                    installedInfo.installStateManifestPath = installStateManifestPath ?? string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(installStateError))
                {
                    Debug.LogWarning($"[YUCP PackageManager] Failed to persist alias install-state manifest for '{installedInfo.packageId}': {installStateError}");
                }

                // Register in registry
                var registry = InstalledPackageRegistry.GetOrCreate();
                registry.RegisterPackage(installedInfo);

                Debug.Log($"[YUCP PackageManager] Registered package: {installedInfo.packageName} (ID: {packageId}, verified={installedInfo.isVerified}, installedFiles={installedInfo.installedFiles?.Count ?? 0})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to register package after import: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
        }
    }
}
#endif
