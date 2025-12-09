using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Grid/list view of installed packages with search/filter functionality
    /// </summary>
    public class InstalledPackagesView : VisualElement
    {
        private Action<InstalledPackageInfo> _onPackageSelected;
        private List<InstalledPackageInfo> _allPackages = new List<InstalledPackageInfo>();
        private List<InstalledPackageInfo> _filteredPackages = new List<InstalledPackageInfo>();
        
        private TextField _searchField;
        private Button _filterAllButton;
        private Button _filterUpdatesButton;
        private Button _filterRecentButton;
        private Button _filterVerifiedButton;
        private Button _viewToggleButton;
        private ScrollView _packagesScrollView;
        private VisualElement _packagesContainer;
        
        private string _currentFilter = "all";
        private bool _isGridView = true;
        
        private const string PREFS_KEY_VIEW_MODE = "YUCP.PackageManager.ViewMode";

        public InstalledPackagesView(Action<InstalledPackageInfo> onPackageSelected)
        {
            _onPackageSelected = onPackageSelected;
            
            // Load saved view preference
            _isGridView = EditorPrefs.GetBool(PREFS_KEY_VIEW_MODE, true);
            
            AddToClassList("installed-packages-view");
            style.flexGrow = 1;
            style.flexShrink = 1;
            style.minHeight = 0;
            
            BuildView();
            RefreshPackages();
        }

        private void BuildView()
        {
            style.flexDirection = FlexDirection.Column;
            style.flexGrow = 1;
            style.flexShrink = 1;
            
            // Header with search and filters
            var header = new VisualElement();
            header.AddToClassList("packages-view-header");
            header.style.flexShrink = 0;
            
            // Search bar
            _searchField = new TextField();
            _searchField.AddToClassList("packages-search-field");
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            // Use tooltip as placeholder hint
            _searchField.tooltip = "Search packages...";
            header.Add(_searchField);
            
            // Filter row with buttons on left and view toggle on right
            var filterRow = new VisualElement();
            filterRow.AddToClassList("packages-filter-row");
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.justifyContent = Justify.SpaceBetween;
            filterRow.style.alignItems = Align.Center;
            
            // Filter buttons container
            var filterContainer = new VisualElement();
            filterContainer.AddToClassList("packages-filter-container");
            
            _filterAllButton = new Button(() => SetFilter("all")) { text = "All" };
            _filterAllButton.AddToClassList("packages-filter-button");
            _filterAllButton.AddToClassList("packages-filter-button-active");
            filterContainer.Add(_filterAllButton);
            
            _filterUpdatesButton = new Button(() => SetFilter("updates")) { text = "Updates" };
            _filterUpdatesButton.AddToClassList("packages-filter-button");
            filterContainer.Add(_filterUpdatesButton);
            
            _filterRecentButton = new Button(() => SetFilter("recent")) { text = "Recent" };
            _filterRecentButton.AddToClassList("packages-filter-button");
            filterContainer.Add(_filterRecentButton);
            
            _filterVerifiedButton = new Button(() => SetFilter("verified")) { text = "Verified" };
            _filterVerifiedButton.AddToClassList("packages-filter-button");
            filterContainer.Add(_filterVerifiedButton);
            
            filterRow.Add(filterContainer);
            
            // View toggle (grid/list) on the right
            _viewToggleButton = new Button(ToggleView) { text = _isGridView ? "☰ List" : "⊞ Grid" };
            _viewToggleButton.AddToClassList("packages-view-toggle");
            filterRow.Add(_viewToggleButton);
            
            header.Add(filterRow);
            
            Add(header);
            
            // Packages container
            _packagesScrollView = new ScrollView();
            _packagesScrollView.AddToClassList("packages-scroll-view");
            _packagesScrollView.style.flexGrow = 1;
            _packagesScrollView.style.flexShrink = 1;
            _packagesContainer = new VisualElement();
            _packagesContainer.AddToClassList("packages-container");
            if (_isGridView)
            {
                _packagesContainer.AddToClassList("packages-container-grid");
            }
            else
            {
                _packagesContainer.AddToClassList("packages-container-list");
            }
            _packagesScrollView.Add(_packagesContainer);
            Add(_packagesScrollView);
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            ApplyFilters();
        }

        private void SetFilter(string filter)
        {
            _currentFilter = filter;
            
            // Update button states
            _filterAllButton.RemoveFromClassList("packages-filter-button-active");
            _filterUpdatesButton.RemoveFromClassList("packages-filter-button-active");
            _filterRecentButton.RemoveFromClassList("packages-filter-button-active");
            _filterVerifiedButton.RemoveFromClassList("packages-filter-button-active");
            
            switch (filter)
            {
                case "all":
                    _filterAllButton.AddToClassList("packages-filter-button-active");
                    break;
                case "updates":
                    _filterUpdatesButton.AddToClassList("packages-filter-button-active");
                    break;
                case "recent":
                    _filterRecentButton.AddToClassList("packages-filter-button-active");
                    break;
                case "verified":
                    _filterVerifiedButton.AddToClassList("packages-filter-button-active");
                    break;
            }
            
            ApplyFilters();
        }

        private void ToggleView()
        {
            _isGridView = !_isGridView;
            EditorPrefs.SetBool(PREFS_KEY_VIEW_MODE, _isGridView);
            _viewToggleButton.text = _isGridView ? "☰ List" : "⊞ Grid";
            _packagesContainer.RemoveFromClassList("packages-container-grid");
            _packagesContainer.RemoveFromClassList("packages-container-list");
            if (_isGridView)
            {
                _packagesContainer.AddToClassList("packages-container-grid");
            }
            else
            {
                _packagesContainer.AddToClassList("packages-container-list");
            }
            RefreshPackageCards();
        }

        private void ApplyFilters()
        {
            string searchQuery = _searchField.value?.ToLowerInvariant() ?? "";
            
            _filteredPackages = _allPackages.Where(pkg =>
            {
                // Search filter
                if (!string.IsNullOrEmpty(searchQuery))
                {
                    bool matchesSearch = 
                        (pkg.packageName?.ToLowerInvariant().Contains(searchQuery) ?? false) ||
                        (pkg.author?.ToLowerInvariant().Contains(searchQuery) ?? false) ||
                        (pkg.description?.ToLowerInvariant().Contains(searchQuery) ?? false);
                    
                    if (!matchesSearch)
                        return false;
                }
                
                // Category filter
                switch (_currentFilter)
                {
                    case "updates":
                        return pkg.hasUpdate;
                    case "recent":
                        // Show packages installed in last 7 days
                        var installedDate = pkg.GetInstalledDateTime();
                        if (installedDate == DateTime.MinValue)
                            return false;
                        return (DateTime.Now - installedDate).TotalDays <= 7;
                    case "verified":
                        return pkg.isVerified;
                    case "all":
                    default:
                        return true;
                }
            }).ToList();
            
            RefreshPackageCards();
        }

        public void RefreshPackages()
        {
            try
            {
                var registry = InstalledPackageRegistry.GetOrCreate();
                _allPackages = registry.GetAllPackages();
                
                // Add mock data if no packages are installed (for testing)
                if (_allPackages.Count == 0)
                {
                    _allPackages = CreateMockPackages();
                    Debug.Log($"[InstalledPackagesView] Using {_allPackages.Count} mock packages for testing");
                }
                else
                {
                    Debug.Log($"[InstalledPackagesView] Found {_allPackages.Count} installed packages");
                }
                
                ApplyFilters();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InstalledPackagesView] Error refreshing packages: {ex.Message}");
                Debug.LogException(ex);
                _allPackages = CreateMockPackages();
                ApplyFilters();
            }
        }

        private List<InstalledPackageInfo> CreateMockPackages()
        {
            var mockPackages = new List<InstalledPackageInfo>();

            // Mock package 1 - Verified with update
            var pkg1 = new InstalledPackageInfo
            {
                packageId = Guid.NewGuid().ToString("N").ToLowerInvariant(),
                packageName = "VRChat Avatar Tools",
                version = "2.1.0",
                installedVersion = "2.0.5",
                latestVersion = "2.1.0",
                author = "Yeusepe",
                description = "A comprehensive toolkit for creating and managing VRChat avatars. Includes advanced animation tools, expression menu builder, and performance optimization utilities.",
                publisherId = "yucp",
                archiveSha256 = "a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6",
                isVerified = true,
                hasUpdate = true,
                installedFiles = new List<string> { "Assets/VRChatAvatarTools", "Assets/VRChatAvatarTools/Editor" },
                productLinks = new List<ProductLink>
                {
                    new ProductLink("https://vpm.yucp.club/", "VPM Repository"),
                    new ProductLink("https://patreon.com/Yeusepe", "Patreon")
                }
            };
            pkg1.SetInstalledDateTime(DateTime.Now.AddDays(-10));
            mockPackages.Add(pkg1);

            // Mock package 2 - Recent installation
            var pkg2 = new InstalledPackageInfo
            {
                packageId = Guid.NewGuid().ToString("N").ToLowerInvariant(),
                packageName = "OSC QR Code Generator",
                version = "1.3.2",
                installedVersion = "1.3.2",
                author = "Community Contributor",
                description = "Generate QR codes for OSC communication in VRChat. Supports custom endpoints and automatic configuration.",
                publisherId = "community",
                archiveSha256 = "b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7",
                isVerified = false,
                hasUpdate = false,
                installedFiles = new List<string> { "Assets/OSCQR" },
                productLinks = new List<ProductLink>()
            };
            pkg2.SetInstalledDateTime(DateTime.Now.AddDays(-2));
            mockPackages.Add(pkg2);

            // Mock package 3 - Verified, no update
            var pkg3 = new InstalledPackageInfo
            {
                packageId = Guid.NewGuid().ToString("N").ToLowerInvariant(),
                packageName = "Spotify OSC Integration",
                version = "3.0.0",
                installedVersion = "3.0.0",
                author = "Yeusepe",
                description = "Real-time Spotify integration for VRChat avatars. Display currently playing track, control playback, and sync with avatar animations.",
                publisherId = "yucp",
                archiveSha256 = "c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8",
                isVerified = true,
                hasUpdate = false,
                installedFiles = new List<string> { "Assets/SpotifyOSC", "Assets/SpotifyOSC/Editor", "Assets/SpotifyOSC/Runtime" },
                productLinks = new List<ProductLink>
                {
                    new ProductLink("https://vpm.yucp.club/", "VPM Repository")
                }
            };
            pkg3.SetInstalledDateTime(DateTime.Now.AddDays(-30));
            mockPackages.Add(pkg3);

            // Mock package 4 - Unverified, with update
            var pkg4 = new InstalledPackageInfo
            {
                packageId = Guid.NewGuid().ToString("N").ToLowerInvariant(),
                packageName = "Custom Animation Controller",
                version = "1.5.0",
                installedVersion = "1.4.8",
                latestVersion = "1.5.0",
                author = "Third Party Developer",
                description = "Advanced animation controller system with state machine editor and timeline integration.",
                publisherId = "thirdparty",
                archiveSha256 = "d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9",
                isVerified = false,
                hasUpdate = true,
                installedFiles = new List<string> { "Assets/CustomAnimController" },
                productLinks = new List<ProductLink>()
            };
            pkg4.SetInstalledDateTime(DateTime.Now.AddDays(-5));
            mockPackages.Add(pkg4);

            // Mock package 5 - Very recent (today)
            var pkg5 = new InstalledPackageInfo
            {
                packageId = Guid.NewGuid().ToString("N").ToLowerInvariant(),
                packageName = "Twitch Chat Integration",
                version = "0.9.1",
                installedVersion = "0.9.1",
                author = "Community",
                description = "Display Twitch chat messages in VRChat. Supports custom formatting, emote rendering, and moderation features.",
                publisherId = "community",
                archiveSha256 = "e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9t0",
                isVerified = true,
                hasUpdate = false,
                installedFiles = new List<string> { "Assets/TwitchOSC" },
                productLinks = new List<ProductLink>()
            };
            pkg5.SetInstalledDateTime(DateTime.Now.AddHours(-3));
            mockPackages.Add(pkg5);

            return mockPackages;
        }

        private void RefreshPackageCards()
        {
            if (_packagesContainer == null)
            {
                Debug.LogError("[InstalledPackagesView] _packagesContainer is null!");
                return;
            }
            
            _packagesContainer.Clear();
            
            if (_filteredPackages.Count == 0)
            {
                var emptyContainer = new VisualElement();
                emptyContainer.style.flexGrow = 1;
                emptyContainer.style.alignItems = Align.Center;
                emptyContainer.style.justifyContent = Justify.Center;
                
                var emptyLabel = new Label("No packages installed");
                emptyLabel.style.fontSize = 14;
                emptyLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                emptyContainer.Add(emptyLabel);
                
                var hintLabel = new Label("Packages will appear here after installation");
                hintLabel.style.fontSize = 11;
                hintLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
                hintLabel.style.marginTop = 8;
                emptyContainer.Add(hintLabel);
                
                _packagesContainer.Add(emptyContainer);
                return;
            }
            
            foreach (var package in _filteredPackages)
            {
                var card = new PackageCard(package, _onPackageSelected, _isGridView);
                _packagesContainer.Add(card);
            }
        }
    }
}






