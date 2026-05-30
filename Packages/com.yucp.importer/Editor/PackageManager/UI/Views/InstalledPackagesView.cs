using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Importer.Editor.PackageManager
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

            // ── Top bar ────────────────────────────────────────────────────
            var topBar = new VisualElement();
            topBar.AddToClassList("installed-packages-topbar");

            var titleCol = new VisualElement();
            var title = new Label("YUCP Packages");
            title.AddToClassList("installed-packages-title");
            titleCol.Add(title);
            var subtitle = new Label("Manage your installed content");
            subtitle.AddToClassList("installed-packages-subtitle");
            titleCol.Add(subtitle);
            topBar.Add(titleCol);
            Add(topBar);

            // ── Search + filter area ────────────────────────────────────────
            var searchArea = new VisualElement();
            searchArea.AddToClassList("installed-packages-search-area");

            // Header with search and filters
            var header = new VisualElement();
            header.AddToClassList("packages-view-header");
            header.style.flexShrink = 0;

            // Search bar with inline placeholder
            var searchWrap = new VisualElement();
            searchWrap.style.position = Position.Relative;
            searchWrap.style.flexShrink = 0;

            _searchField = new TextField();
            _searchField.AddToClassList("packages-search-field");
            _searchField.RegisterValueChangedCallback(OnSearchChanged);

            // Placeholder label shown when empty
            var placeholder = new Label("Search packages…");
            placeholder.style.position = Position.Absolute;
            placeholder.style.left = 12;
            placeholder.style.top = 0;
            placeholder.style.bottom = 0;
            placeholder.style.fontSize = 13;
            placeholder.style.color = new Color(0.75f, 0.75f, 0.80f, 0.32f);
            placeholder.style.unityTextAlign = TextAnchor.MiddleLeft;
            placeholder.pickingMode = PickingMode.Ignore;

            _searchField.RegisterValueChangedCallback(e =>
                placeholder.style.display = string.IsNullOrEmpty(e.newValue) ? DisplayStyle.Flex : DisplayStyle.None);

            searchWrap.Add(_searchField);
            searchWrap.Add(placeholder);
            header.Add(searchWrap);
            
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

            searchArea.Add(header);
            Add(searchArea);

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
                Debug.Log($"[InstalledPackagesView] Found {_allPackages.Count} installed packages");
                
                ApplyFilters();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InstalledPackagesView] Error refreshing packages: {ex.Message}");
                Debug.LogException(ex);
                _allPackages = new List<InstalledPackageInfo>();
                ApplyFilters();
            }
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
                emptyContainer.style.paddingTop = 40;
                emptyContainer.style.paddingBottom = 40;

                var emptyLabel = new Label("No packages found");
                emptyLabel.style.fontSize = 15;
                emptyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                emptyLabel.style.color = new Color(0.75f, 0.75f, 0.82f, 0.70f);
                emptyContainer.Add(emptyLabel);

                var hintLabel = new Label("Packages you install will appear here");
                hintLabel.style.fontSize = 12;
                hintLabel.style.color = new Color(0.55f, 0.55f, 0.62f, 0.60f);
                hintLabel.style.marginTop = 6;
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
































