using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.Editor.UI;
using YUCP.Components.PackageGuardian.Editor.Services;
using YUCP.Components.PackageGuardian.Editor.Windows.Graph;
using global::PackageGuardian.Core.Objects;
using global::PackageGuardian.Core.Diff;

namespace YUCP.Components.PackageGuardian.Editor.Windows
{
    /// <summary>
    /// Main Package Guardian window - Professional VCS Dashboard.
    /// 3-Pane Layout: Top Bar + Left (Graph/Timeline) + Right (Details/Diff)
    /// </summary>
    public class PackageGuardianWindow : EditorWindow
    {
        // UI Elements
        private GroupedGraphView _graphView;
        private Label _currentBranchLabel;
        private Label _lastCommitLabel;
        private Label _statusLabel;
        private TextField _searchField;
        private Label _totalCommitsLabel;
        private Label _totalFilesLabel;
        private Label _pendingChangesCountLabel;
        private Label _lastActivityLabel;
        
        private VisualElement _rightPaneContainer;
        private VisualElement _snapshotPanel;
        private VisualElement _commitDetailsPanel;
        private ScrollView _commitDetailsScrollView;
        private TextField _snapshotMessageField;
        private ScrollView _pendingChangesView;
        private Button _createSnapshotButton;
        private Label _pendingChangesCount;
        
        private VisualElement _commitHeaderSection;
        private Label _selectedCommitHash;
        private Label _selectedCommitTypeBadge;
        private Label _selectedCommitMessage;
        private VisualElement _commitMetaSection;
        private Label _selectedCommitAuthor;
        private Label _selectedCommitTime;
        private VisualElement _commitStatsSection;
        private VisualElement _filesSection;
        private ScrollView _fileChangesView;
        private VisualElement _diffPreviewPanel;
        private VisualElement _emptyState;
        private VisualElement _commitDetailsContentContainer;
        
        private string _selectedCommitId;
        private string _selectedFilePath;
        private string _currentSearchText = "";

        // Async loading state
        private bool _isLoadingPendingChanges;
        private YUCPProgressWindow _progressWindow;
        
        // Responsive design elements
        private Button _mobileToggleButton;
        private VisualElement _overlayBackdrop;
        private VisualElement _contentContainer;
        private VisualElement _leftPane;
        private bool _isOverlayOpen = false;
        private bool _leftPaneInOverlayMode = false;
        
        // Resize throttling to prevent lag during window resize
        private IVisualElementScheduledItem _resizeThrottleScheduler;
        private float _lastProcessedWidth = -1f;
        private const float RESIZE_DEBOUNCE_MS = 150f;

		[MenuItem("Tools/Package Guardian/Dashboard")]
        public static void ShowWindow()
        {
            var window = GetWindow<PackageGuardianWindow>();
            window.titleContent = new GUIContent("Package Guardian");
            window.minSize = new Vector2(400, 500); // Reduced minimum size for responsive design
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("pg-window");

            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/PackageGuardian/Styles/PackageGuardian.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // Build the UI
            var mainContainer = new VisualElement();
            mainContainer.AddToClassList("pg-main-container");

            // Top Bar
            mainContainer.Add(CreateTopBar());

            // Content Container (Left + Right Panes)
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("pg-content-container");
            
            // Create overlay backdrop (for mobile menu)
            _overlayBackdrop = new VisualElement();
            _overlayBackdrop.AddToClassList("pg-overlay-backdrop");
            _overlayBackdrop.RegisterCallback<ClickEvent>(evt => CloseOverlay());
            _overlayBackdrop.style.display = DisplayStyle.None;
            _overlayBackdrop.style.visibility = Visibility.Hidden;
            _contentContainer.Add(_overlayBackdrop);
            
            // Create normal left pane
            _leftPane = CreateLeftPane();
            _contentContainer.Add(_leftPane);
            
            // Note: We'll move the left pane into overlay position instead of duplicating it
            // This avoids lag from rendering GraphView twice

            _contentContainer.Add(CreateRightPane());

            mainContainer.Add(_contentContainer);
            root.Add(mainContainer);

            // Set initial status and show snapshot panel without blocking
            _statusLabel.text = "Initializing...";
            ShowSnapshotPanel();
            
            // Refresh graph view to load commits
            _graphView.Refresh();
            
            // Load pending changes asynchronously with progress
            LoadPendingChangesAsync();

            // Responsive: toggle compact layout based on window width
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            
            // Schedule initial responsive check after layout is ready
            root.schedule.Execute(() => 
            {
                UpdateResponsiveClass(rootVisualElement.resolvedStyle.width);
            }).StartingIn(100);
        }

        /// <summary>
        /// Creates the top bar with global controls and status.
        /// </summary>
        private VisualElement CreateTopBar()
        {
            var topBar = new VisualElement();
            topBar.AddToClassList("pg-top-bar");

            // Left: Quick actions
            var leftSection = new VisualElement();
            leftSection.AddToClassList("pg-top-bar-left");
            
            // Mobile toggle button (hamburger menu)
            _mobileToggleButton = new Button(ToggleOverlay);
            _mobileToggleButton.text = "☰";
            _mobileToggleButton.AddToClassList("pg-mobile-toggle");
            leftSection.Add(_mobileToggleButton);

            var snapshotButton = new Button(ShowSnapshotPanel);
            snapshotButton.text = "New Snapshot";
            snapshotButton.AddToClassList("pg-button");
            snapshotButton.AddToClassList("pg-button-primary");
            snapshotButton.AddToClassList("pg-top-bar-action-button");
            leftSection.Add(snapshotButton);

            var rollbackButton = new Button(OnRollback);
            rollbackButton.text = "Rollback";
            rollbackButton.AddToClassList("pg-button");
            rollbackButton.AddToClassList("pg-top-bar-action-button");
            leftSection.Add(rollbackButton);

            var stashButton = new Button(OnCreateStash);
            stashButton.text = "Stash Changes";
            stashButton.AddToClassList("pg-button");
            stashButton.AddToClassList("pg-top-bar-action-button");
            leftSection.Add(stashButton);
            
            var stashManagerButton = new Button(() => StashManagerWindow.ShowWindow());
            stashManagerButton.text = "Manage Stashes";
            stashManagerButton.AddToClassList("pg-button");
            stashManagerButton.AddToClassList("pg-top-bar-action-button");
            leftSection.Add(stashManagerButton);

            topBar.Add(leftSection);

            var centerSection = new VisualElement();
            centerSection.AddToClassList("pg-top-bar-center");
            centerSection.style.flexDirection = FlexDirection.Row;
            centerSection.style.alignItems = Align.Center;

            _currentBranchLabel = new Label("main");
            _currentBranchLabel.style.fontSize = 11;
            _currentBranchLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _currentBranchLabel.style.color = new Color(0.36f, 0.64f, 1.0f);
            _currentBranchLabel.style.marginRight = 12;
            _currentBranchLabel.style.paddingLeft = 10;
            _currentBranchLabel.style.paddingRight = 10;
            _currentBranchLabel.style.paddingTop = 5;
            _currentBranchLabel.style.paddingBottom = 5;
            _currentBranchLabel.style.backgroundColor = new Color(0.36f, 0.64f, 1.0f, 0.15f);
            _currentBranchLabel.style.borderTopLeftRadius = 3;
            _currentBranchLabel.style.borderTopRightRadius = 3;
            _currentBranchLabel.style.borderBottomLeftRadius = 3;
            _currentBranchLabel.style.borderBottomRightRadius = 3;
            centerSection.Add(_currentBranchLabel);

            _lastCommitLabel = new Label("No commits");
            _lastCommitLabel.style.fontSize = 10;
            _lastCommitLabel.style.color = new Color(0.69f, 0.69f, 0.69f);
            _lastCommitLabel.style.marginRight = 12;
            _lastCommitLabel.style.paddingLeft = 8;
            _lastCommitLabel.style.paddingRight = 8;
            _lastCommitLabel.style.paddingTop = 4;
            _lastCommitLabel.style.paddingBottom = 4;
            _lastCommitLabel.style.backgroundColor = new Color(0.165f, 0.165f, 0.165f);
            _lastCommitLabel.style.borderTopLeftRadius = 3;
            _lastCommitLabel.style.borderTopRightRadius = 3;
            _lastCommitLabel.style.borderBottomLeftRadius = 3;
            _lastCommitLabel.style.borderBottomRightRadius = 3;
            centerSection.Add(_lastCommitLabel);

            _statusLabel = new Label("Ready");
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new Color(0.21f, 0.75f, 0.69f);
            _statusLabel.style.paddingLeft = 8;
            _statusLabel.style.paddingRight = 8;
            _statusLabel.style.paddingTop = 4;
            _statusLabel.style.paddingBottom = 4;
            _statusLabel.style.backgroundColor = new Color(0.21f, 0.75f, 0.69f, 0.15f);
            _statusLabel.style.borderTopLeftRadius = 3;
            _statusLabel.style.borderTopRightRadius = 3;
            _statusLabel.style.borderBottomLeftRadius = 3;
            _statusLabel.style.borderBottomRightRadius = 3;
            centerSection.Add(_statusLabel);

            topBar.Add(centerSection);

            // Right: Search and settings
            var rightSection = new VisualElement();
            rightSection.AddToClassList("pg-top-bar-right");

            // Search field
            _searchField = new TextField();
            _searchField.AddToClassList("pg-search-input");
            _searchField.value = "";
            _searchField.style.minWidth = 150;
            _searchField.style.maxWidth = 200;
            _searchField.style.marginLeft = 8;
            _searchField.style.marginRight = 8;
            _searchField.tooltip = "Search commits and files";
            _searchField.RegisterValueChangedCallback(evt => OnSearchChanged(evt.newValue));
            rightSection.Add(_searchField);

            var refreshButton = new Button(RefreshDashboard);
            refreshButton.text = "Refresh";
            refreshButton.AddToClassList("pg-button");
            rightSection.Add(refreshButton);

            // View options menu
            var viewMenu = new Button(ShowViewMenu);
            viewMenu.text = "View ▼";
            viewMenu.AddToClassList("pg-button");
            rightSection.Add(viewMenu);
            
            var settingsButton = new Button(() => UnityEditor.SettingsService.OpenProjectSettings("Project/Package Guardian"));
            settingsButton.text = "Settings";
            settingsButton.AddToClassList("pg-button");
            rightSection.Add(settingsButton);

            topBar.Add(rightSection);

            return topBar;
        }

        /// <summary>
        /// Creates the left pane with the commit graph/timeline.
        /// </summary>
        private VisualElement CreateLeftPane()
        {
            var leftPane = new VisualElement();
            leftPane.AddToClassList("pg-left-pane");
            leftPane.style.flexDirection = FlexDirection.Column;

            var overviewSection = CreateOverviewSection();
            leftPane.Add(overviewSection);

            var panel = new VisualElement();
            panel.AddToClassList("pg-panel");
            panel.style.flexGrow = 1;
            panel.style.minHeight = 0;
            panel.style.flexDirection = FlexDirection.Column;

            var header = new VisualElement();
            header.AddToClassList("pg-panel-header");

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.justifyContent = Justify.SpaceBetween;

            var title = new Label("Commit History");
            title.AddToClassList("pg-title");
            titleRow.Add(title);

            header.Add(titleRow);
            panel.Add(header);

            _graphView = new GroupedGraphView(showStashesSeparately: true);
            _graphView.OnCommitSelected = OnCommitSelected;
            _graphView.style.flexGrow = 1;
            _graphView.style.minHeight = 0;
            panel.Add(_graphView);

            leftPane.Add(panel);

            return leftPane;
        }

        private VisualElement CreateOverviewSection()
        {
            var overview = new VisualElement();
            overview.AddToClassList("pg-overview-section");
            overview.style.flexShrink = 0;
            overview.style.paddingTop = 12;
            overview.style.paddingBottom = 12;
            overview.style.paddingLeft = 16;
            overview.style.paddingRight = 16;
            overview.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f);
            overview.style.borderBottomWidth = 2;
            overview.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);

            var statsContainer = new VisualElement();
            statsContainer.style.flexDirection = FlexDirection.Row;
            statsContainer.style.flexWrap = Wrap.Wrap;

            _totalCommitsLabel = new Label("0");
            _totalFilesLabel = new Label("0");
            _pendingChangesCountLabel = new Label("0");

            var commitsCard = CreateStatCard("Commits", _totalCommitsLabel, new Color(0.36f, 0.64f, 1.0f));
            var filesCard = CreateStatCard("Files", _totalFilesLabel, new Color(0.21f, 0.75f, 0.69f));
            var pendingCard = CreateStatCard("Pending", _pendingChangesCountLabel, new Color(0.89f, 0.65f, 0.29f));

            commitsCard.style.marginRight = 12;
            filesCard.style.marginRight = 12;
            
            statsContainer.Add(commitsCard);
            statsContainer.Add(filesCard);
            statsContainer.Add(pendingCard);

            overview.Add(statsContainer);

            var recentActivity = new VisualElement();
            recentActivity.style.flexDirection = FlexDirection.Row;
            recentActivity.style.alignItems = Align.Center;
            recentActivity.style.marginTop = 10;
            recentActivity.style.paddingTop = 10;
            recentActivity.style.borderTopWidth = 1;
            recentActivity.style.borderTopColor = new Color(0.15f, 0.15f, 0.15f);

            var activityIcon = new Label("●");
            activityIcon.style.fontSize = 8;
            activityIcon.style.color = new Color(0.21f, 0.75f, 0.69f);
            activityIcon.style.marginRight = 8;
            recentActivity.Add(activityIcon);

            _lastActivityLabel = new Label("No commits yet");
            _lastActivityLabel.style.fontSize = 11;
            _lastActivityLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            recentActivity.Add(_lastActivityLabel);

            overview.Add(recentActivity);

            return overview;
        }

        private VisualElement CreateStatCard(string label, Label valueLabel, Color accentColor)
        {
            var card = new VisualElement();
            card.AddToClassList("pg-stat-card");
            card.style.flexGrow = 1;
            card.style.flexBasis = new StyleLength(StyleKeyword.Auto);
            card.style.minWidth = 80;
            card.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = accentColor;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.borderTopLeftRadius = 0;
            card.style.borderTopRightRadius = 0;
            card.style.borderBottomLeftRadius = 0;
            card.style.borderBottomRightRadius = 0;

            valueLabel.text = "0";
            valueLabel.style.fontSize = 20;
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            valueLabel.style.color = accentColor;
            valueLabel.style.marginBottom = 4;
            card.Add(valueLabel);

            var labelText = new Label(label.ToUpperInvariant());
            labelText.style.fontSize = 10;
            labelText.style.color = new Color(0.69f, 0.69f, 0.69f);
            labelText.style.letterSpacing = 0.5f;
            card.Add(labelText);

            return card;
        }

        /// <summary>
        /// Creates the right pane with snapshot creation and commit details.
        /// </summary>
        private VisualElement CreateRightPane()
        {
            var rightPane = new VisualElement();
            rightPane.AddToClassList("pg-right-pane");

            _rightPaneContainer = new VisualElement();
            _rightPaneContainer.style.flexGrow = 1;
            _rightPaneContainer.style.flexDirection = FlexDirection.Column;
            _rightPaneContainer.style.minWidth = 0;
            _rightPaneContainer.style.minHeight = 0;

            // Snapshot Creation Panel (initially visible)
            _snapshotPanel = CreateSnapshotPanel();
            _snapshotPanel.style.flexGrow = 1;
            _snapshotPanel.style.minHeight = 0;
            _rightPaneContainer.Add(_snapshotPanel);

            _commitDetailsPanel = CreateCommitDetailsPanel();
            _commitDetailsPanel.style.flexGrow = 1;
            _commitDetailsPanel.style.minHeight = 0;
            _commitDetailsPanel.style.display = DisplayStyle.None;
            _rightPaneContainer.Add(_commitDetailsPanel);

            rightPane.Add(_rightPaneContainer);

            return rightPane;
        }

        /// <summary>
        /// Creates the snapshot creation panel.
        /// </summary>
        private VisualElement CreateSnapshotPanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.paddingTop = 20;
            panel.style.paddingBottom = 20;
            panel.style.paddingLeft = 24;
            panel.style.paddingRight = 24;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 24;
            header.style.paddingBottom = 16;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.165f, 0.165f, 0.165f);

            var title = new Label("Create New Snapshot");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.95f, 0.95f, 0.95f);
            header.Add(title);

            var closeButton = new Button(() => _snapshotPanel.style.display = DisplayStyle.None);
            closeButton.text = "×";
            closeButton.style.fontSize = 24;
            closeButton.style.width = 32;
            closeButton.style.height = 32;
            closeButton.style.backgroundColor = new Color(0.165f, 0.165f, 0.165f);
            closeButton.style.color = new Color(0.8f, 0.8f, 0.8f);
            closeButton.style.borderTopLeftRadius = 3;
            closeButton.style.borderTopRightRadius = 3;
            closeButton.style.borderBottomLeftRadius = 3;
            closeButton.style.borderBottomRightRadius = 3;
            closeButton.RegisterCallback<MouseEnterEvent>(evt => {
                closeButton.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            });
            closeButton.RegisterCallback<MouseLeaveEvent>(evt => {
                closeButton.style.backgroundColor = new Color(0.165f, 0.165f, 0.165f);
            });
            header.Add(closeButton);

            panel.Add(header);

            var messageSection = new VisualElement();
            messageSection.style.flexShrink = 0;
            messageSection.style.marginBottom = 20;
            messageSection.style.paddingTop = 16;
            messageSection.style.paddingBottom = 16;
            messageSection.style.paddingLeft = 16;
            messageSection.style.paddingRight = 16;
            messageSection.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            messageSection.style.borderTopWidth = 1;
            messageSection.style.borderBottomWidth = 1;
            messageSection.style.borderLeftWidth = 1;
            messageSection.style.borderRightWidth = 1;
            messageSection.style.borderTopColor = new Color(0.165f, 0.165f, 0.165f);
            messageSection.style.borderBottomColor = new Color(0.165f, 0.165f, 0.165f);
            messageSection.style.borderLeftColor = new Color(0.165f, 0.165f, 0.165f);
            messageSection.style.borderRightColor = new Color(0.165f, 0.165f, 0.165f);

            var messageLabel = new Label("SNAPSHOT MESSAGE");
            messageLabel.style.fontSize = 11;
            messageLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            messageLabel.style.color = new Color(0.69f, 0.69f, 0.69f);
            messageLabel.style.letterSpacing = 0.5f;
            messageLabel.style.marginBottom = 12;
            messageSection.Add(messageLabel);

            _snapshotMessageField = new TextField();
            _snapshotMessageField.multiline = true;
            _snapshotMessageField.value = "";
            _snapshotMessageField.style.minHeight = 100;
            _snapshotMessageField.style.flexShrink = 0;
            _snapshotMessageField.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f);
            _snapshotMessageField.style.borderTopWidth = 1;
            _snapshotMessageField.style.borderBottomWidth = 1;
            _snapshotMessageField.style.borderLeftWidth = 1;
            _snapshotMessageField.style.borderRightWidth = 1;
            _snapshotMessageField.style.borderTopColor = new Color(0.165f, 0.165f, 0.165f);
            _snapshotMessageField.style.borderBottomColor = new Color(0.165f, 0.165f, 0.165f);
            _snapshotMessageField.style.borderLeftColor = new Color(0.165f, 0.165f, 0.165f);
            _snapshotMessageField.style.borderRightColor = new Color(0.165f, 0.165f, 0.165f);
            _snapshotMessageField.style.color = new Color(0.95f, 0.95f, 0.95f);
            _snapshotMessageField.style.paddingTop = 8;
            _snapshotMessageField.style.paddingBottom = 8;
            _snapshotMessageField.style.paddingLeft = 12;
            _snapshotMessageField.style.paddingRight = 12;
            _snapshotMessageField.tooltip = "Describe what changed in this snapshot";
            messageSection.Add(_snapshotMessageField);

            var hint = new Label("Describe what changed in this snapshot");
            hint.style.fontSize = 10;
            hint.style.color = new Color(0.5f, 0.5f, 0.5f);
            hint.style.marginTop = 8;
            messageSection.Add(hint);

            panel.Add(messageSection);

            // Pending changes section
            var changesSection = new VisualElement();
            changesSection.AddToClassList("pg-section");
            changesSection.style.flexGrow = 1;

            var changesHeader = new VisualElement();
            changesHeader.style.flexDirection = FlexDirection.Row;
            changesHeader.style.justifyContent = Justify.SpaceBetween;
            changesHeader.style.alignItems = Align.Center;
            changesHeader.style.marginBottom = 8;

            var changesLabel = new Label("PENDING CHANGES");
            changesLabel.AddToClassList("pg-section-title");
            changesHeader.Add(changesLabel);

            _pendingChangesCount = new Label("0 files");
            _pendingChangesCount.style.fontSize = 10;
            _pendingChangesCount.style.color = new Color(0.69f, 0.69f, 0.69f);
            _pendingChangesCount.style.paddingLeft = 8;
            _pendingChangesCount.style.paddingRight = 8;
            _pendingChangesCount.style.paddingTop = 4;
            _pendingChangesCount.style.paddingBottom = 4;
            _pendingChangesCount.style.backgroundColor = new Color(0.165f, 0.165f, 0.165f);
            _pendingChangesCount.style.borderTopLeftRadius = 3;
            _pendingChangesCount.style.borderTopRightRadius = 3;
            _pendingChangesCount.style.borderBottomLeftRadius = 3;
            _pendingChangesCount.style.borderBottomRightRadius = 3;
            changesHeader.Add(_pendingChangesCount);

            changesSection.Add(changesHeader);

            _pendingChangesView = new ScrollView();
            _pendingChangesView.AddToClassList("pg-scrollview");
            _pendingChangesView.style.flexGrow = 1;
            changesSection.Add(_pendingChangesView);

            panel.Add(changesSection);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.FlexEnd;
            actions.style.marginTop = 16;
            actions.style.flexShrink = 0;

            _createSnapshotButton = new Button(OnCreateSnapshotFromPanel);
            _createSnapshotButton.text = "Create Snapshot";
            _createSnapshotButton.style.height = 40;
            _createSnapshotButton.style.fontSize = 13;
            _createSnapshotButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _createSnapshotButton.style.backgroundColor = new Color(0.21f, 0.75f, 0.69f);
            _createSnapshotButton.style.color = new Color(1f, 1f, 1f);
            _createSnapshotButton.style.paddingLeft = 24;
            _createSnapshotButton.style.paddingRight = 24;
            _createSnapshotButton.style.borderTopLeftRadius = 3;
            _createSnapshotButton.style.borderTopRightRadius = 3;
            _createSnapshotButton.style.borderBottomLeftRadius = 3;
            _createSnapshotButton.style.borderBottomRightRadius = 3;
            _createSnapshotButton.RegisterCallback<MouseEnterEvent>(evt => {
                if (_createSnapshotButton.enabledSelf)
                {
                    _createSnapshotButton.style.backgroundColor = new Color(0.33f, 0.85f, 0.79f);
                }
            });
            _createSnapshotButton.RegisterCallback<MouseLeaveEvent>(evt => {
                if (_createSnapshotButton.enabledSelf)
                {
                    _createSnapshotButton.style.backgroundColor = new Color(0.21f, 0.75f, 0.69f);
                }
            });
            _createSnapshotButton.SetEnabled(false);
            actions.Add(_createSnapshotButton);

            panel.Add(actions);

            return panel;
        }

        private VisualElement CreateCommitDetailsPanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.minWidth = 0;
            panel.style.minHeight = 0;

            _emptyState = new VisualElement();
            _emptyState.AddToClassList("pg-empty-state");
            _emptyState.style.flexGrow = 1;
            _emptyState.style.display = DisplayStyle.Flex;

            var emptyTitle = new Label("Select a Commit");
            emptyTitle.AddToClassList("pg-empty-state-title");
            _emptyState.Add(emptyTitle);

            var emptyDesc = new Label("Click on any commit in the timeline to view details");
            emptyDesc.AddToClassList("pg-empty-state-description");
            _emptyState.Add(emptyDesc);

            panel.Add(_emptyState);

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("pg-scrollview");
            scrollView.style.flexGrow = 1;
            scrollView.style.minHeight = 0;
            scrollView.style.display = DisplayStyle.None;

            var contentContainer = scrollView.contentContainer;
            contentContainer.style.flexDirection = FlexDirection.Column;

            _commitHeaderSection = CreateCommitHeaderSection();
            contentContainer.Add(_commitHeaderSection);

            _commitMetaSection = CreateCommitMetaSection();
            contentContainer.Add(_commitMetaSection);

            _commitStatsSection = CreateStatsSection();
            contentContainer.Add(_commitStatsSection);

            _filesSection = CreateFilesSection();
            contentContainer.Add(_filesSection);

            panel.Add(scrollView);
            _commitDetailsScrollView = scrollView;
            _commitDetailsContentContainer = contentContainer;

            return panel;
        }

        private VisualElement CreateCommitHeaderSection()
        {
            var section = new VisualElement();
            section.style.flexShrink = 0;
            section.style.paddingTop = 24;
            section.style.paddingBottom = 24;
            section.style.paddingLeft = 24;
            section.style.paddingRight = 24;
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = new Color(0.165f, 0.165f, 0.165f);
            section.style.backgroundColor = new Color(0.059f, 0.059f, 0.059f);

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 16;
            headerRow.style.flexWrap = Wrap.Wrap;

            _selectedCommitHash = new Label();
            _selectedCommitHash.style.fontSize = 18;
            _selectedCommitHash.style.unityFontStyleAndWeight = FontStyle.Bold;
            _selectedCommitHash.style.color = new Color(0.36f, 0.64f, 1.0f);
            _selectedCommitHash.style.marginRight = 12;
            _selectedCommitHash.style.flexShrink = 0;
            _selectedCommitHash.style.whiteSpace = WhiteSpace.Normal;
            headerRow.Add(_selectedCommitHash);

            _selectedCommitTypeBadge = new Label();
            _selectedCommitTypeBadge.style.fontSize = 10;
            _selectedCommitTypeBadge.style.paddingTop = 5;
            _selectedCommitTypeBadge.style.paddingBottom = 5;
            _selectedCommitTypeBadge.style.paddingLeft = 10;
            _selectedCommitTypeBadge.style.paddingRight = 10;
            _selectedCommitTypeBadge.style.borderTopLeftRadius = 3;
            _selectedCommitTypeBadge.style.borderTopRightRadius = 3;
            _selectedCommitTypeBadge.style.borderBottomLeftRadius = 3;
            _selectedCommitTypeBadge.style.borderBottomRightRadius = 3;
            _selectedCommitTypeBadge.style.flexShrink = 0;
            headerRow.Add(_selectedCommitTypeBadge);

            section.Add(headerRow);

            _selectedCommitMessage = new Label();
            _selectedCommitMessage.style.fontSize = 14;
            _selectedCommitMessage.style.color = new Color(0.9f, 0.9f, 0.9f);
            _selectedCommitMessage.style.whiteSpace = WhiteSpace.Normal;
            _selectedCommitMessage.style.marginTop = 0;
            section.Add(_selectedCommitMessage);

            return section;
        }

        private VisualElement CreateCommitMetaSection()
        {
            var section = new VisualElement();
            section.style.flexShrink = 0;
            section.style.paddingTop = 20;
            section.style.paddingBottom = 20;
            section.style.paddingLeft = 24;
            section.style.paddingRight = 24;
            section.style.backgroundColor = new Color(0.059f, 0.059f, 0.059f);

            var metaContainer = new VisualElement();
            metaContainer.style.flexDirection = FlexDirection.Row;
            metaContainer.style.flexWrap = Wrap.Wrap;

            var authorContainer = new VisualElement();
            authorContainer.style.flexDirection = FlexDirection.Column;
            authorContainer.style.marginRight = 32;
            authorContainer.style.minWidth = 200;

            var authorLabel = new Label("Author");
            authorLabel.style.fontSize = 11;
            authorLabel.style.color = new Color(0.69f, 0.69f, 0.69f);
            authorLabel.style.marginBottom = 6;
            authorContainer.Add(authorLabel);

            _selectedCommitAuthor = new Label();
            _selectedCommitAuthor.style.fontSize = 13;
            _selectedCommitAuthor.style.color = new Color(0.9f, 0.9f, 0.9f);
            _selectedCommitAuthor.style.whiteSpace = WhiteSpace.Normal;
            authorContainer.Add(_selectedCommitAuthor);

            metaContainer.Add(authorContainer);

            var dateContainer = new VisualElement();
            dateContainer.style.flexDirection = FlexDirection.Column;
            dateContainer.style.minWidth = 200;

            var dateLabel = new Label("Date");
            dateLabel.style.fontSize = 11;
            dateLabel.style.color = new Color(0.69f, 0.69f, 0.69f);
            dateLabel.style.marginBottom = 6;
            dateContainer.Add(dateLabel);

            _selectedCommitTime = new Label();
            _selectedCommitTime.style.fontSize = 13;
            _selectedCommitTime.style.color = new Color(0.9f, 0.9f, 0.9f);
            _selectedCommitTime.style.whiteSpace = WhiteSpace.Normal;
            dateContainer.Add(_selectedCommitTime);

            metaContainer.Add(dateContainer);
            section.Add(metaContainer);

            return section;
        }

        private VisualElement CreateStatsSection()
        {
            var section = new VisualElement();
            section.style.flexShrink = 0;
            section.style.paddingTop = 20;
            section.style.paddingBottom = 20;
            section.style.paddingLeft = 24;
            section.style.paddingRight = 24;
            section.style.borderTopWidth = 1;
            section.style.borderTopColor = new Color(0.165f, 0.165f, 0.165f);
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = new Color(0.165f, 0.165f, 0.165f);
            section.style.backgroundColor = new Color(0.059f, 0.059f, 0.059f);

            var statsContainer = new VisualElement();
            statsContainer.style.flexDirection = FlexDirection.Row;
            statsContainer.style.flexWrap = Wrap.Wrap;
            section.Add(statsContainer);

            return section;
        }

        private VisualElement CreateFilesSection()
        {
            var section = new VisualElement();
            section.style.flexGrow = 1;
            section.style.flexDirection = FlexDirection.Column;
            section.style.minHeight = 0;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.paddingTop = 20;
            header.style.paddingBottom = 16;
            header.style.paddingLeft = 24;
            header.style.paddingRight = 24;
            header.style.flexShrink = 0;

            var fileChangesTitle = new Label("Changed Files");
            fileChangesTitle.style.fontSize = 14;
            fileChangesTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            fileChangesTitle.style.color = new Color(0.9f, 0.9f, 0.9f);
            header.Add(fileChangesTitle);

            section.Add(header);

            _fileChangesView = new ScrollView();
            _fileChangesView.AddToClassList("pg-scrollview");
            _fileChangesView.style.flexGrow = 1;
            _fileChangesView.style.minHeight = 200;
            
            var filesContentContainer = _fileChangesView.contentContainer;
            filesContentContainer.style.paddingLeft = 24;
            filesContentContainer.style.paddingRight = 24;
            filesContentContainer.style.paddingBottom = 24;
            
            section.Add(_fileChangesView);

            return section;
        }

        private void ShowEmptyState()
        {
            _emptyState.style.display = DisplayStyle.Flex;
            if (_commitDetailsScrollView != null)
                _commitDetailsScrollView.style.display = DisplayStyle.None;
        }

        private void HideEmptyState()
        {
            _emptyState.style.display = DisplayStyle.None;
            if (_commitDetailsScrollView != null)
                _commitDetailsScrollView.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Handles commit selection from the graph view.
        /// </summary>
        private void OnCommitSelected(GraphNode node)
        {
            _selectedCommitId = node.CommitId;
            
            _snapshotPanel.style.display = DisplayStyle.None;
            _commitDetailsPanel.style.display = DisplayStyle.Flex;
            HideEmptyState();
            
            LoadCommitDetails(node.CommitId);
            
            // Close overlay when commit is selected (for mobile)
            CloseOverlay();
        }
        
        /// <summary>
        /// Shows the snapshot creation panel.
        /// </summary>
        private void ShowSnapshotPanel()
        {
            _commitDetailsPanel.style.display = DisplayStyle.None;
            _snapshotPanel.style.display = DisplayStyle.Flex;
            
            // Load pending changes
            LoadPendingChanges();
        }
        
        /// <summary>
        /// Loads pending changes for snapshot creation.
        /// </summary>
        private void LoadPendingChanges()
        {
            // Switch to non-blocking async load
            _pendingChangesView.Clear();
            _pendingChangesCount.text = "Loading...";
            _createSnapshotButton.SetEnabled(false);
            LoadPendingChangesAsync();
        }
        
        private void LoadPendingChangesAsync()
        {
            if (_isLoadingPendingChanges)
                return;
            
            _isLoadingPendingChanges = true;
            _progressWindow = YUCPProgressWindow.Create();
            SafeProgress(0.05f, "Preparing Package Guardian...");
            
            RepositoryService service = null;
            try
            {
                // Ensure repository is available (main thread)
                service = RepositoryService.Instance;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Failed to initialize repository: {ex.Message}");
                _pendingChangesCount.text = "Repository initialization failed";
                _createSnapshotButton.SetEnabled(false);
                CloseProgress();
                _isLoadingPendingChanges = false;
                return;
            }
            
            var repo = service.Repository;
            
            Task.Run(() =>
            {
                try
                {
                    // Stage 1: Scan filesystem for current tree
                    ScheduleProgress(0.20f, "Scanning project files (Assets, Packages)...");
                    var currentTreeOid = repo.Snapshots.BuildTreeFromDisk(repo.Root, new List<string> { "Assets", "Packages" });
                    
                    // Stage 2: Load HEAD state
                    ScheduleProgress(0.60f, "Loading repository state...");
                    var headCommit = repo.Refs.ResolveHead();
                    string oldTreeOid = null;
                    if (!string.IsNullOrEmpty(headCommit))
                    {
                        try
                        {
                            var head = repo.Store.ReadObject(headCommit) as Commit;
                            if (head != null)
                            {
                                oldTreeOid = repo.Hasher.ToHex(head.TreeId);
                            }
                        }
                        catch (System.IO.InvalidDataException ex)
                        {
                            // Object integrity check failed - log warning but continue
                            // This allows the UI to still function even if some objects are corrupted
                            UnityEngine.Debug.LogWarning($"[Package Guardian] Corrupted object in HEAD: {ex.Message}. Proceeding with empty baseline.");
                        }
                    }
                    
                    // Stage 3: Compute diffs
                    ScheduleProgress(0.85f, "Calculating diffs...");
                    var diffEngine = new DiffEngine(repo.Store);
                    var fileChanges = diffEngine.CompareTrees("", oldTreeOid, currentTreeOid);
                    
                    // Finish on main thread: populate UI
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            _pendingChangesCount.text = $"{fileChanges.Count} files changed";
                            _createSnapshotButton.SetEnabled(fileChanges.Count > 0);
                            
                            if (_pendingChangesCountLabel != null)
                            {
                                _pendingChangesCountLabel.text = fileChanges.Count.ToString();
                            }
                            _pendingChangesView.Clear();
                            
                            if (!fileChanges.Any())
                            {
                                var emptyLabel = new Label("No pending changes");
                                emptyLabel.AddToClassList("pg-label-secondary");
                                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                                emptyLabel.style.paddingTop = 40;
                                _pendingChangesView.Add(emptyLabel);
                            }
                            else
                            {
                                var grouped = GroupFilesByType(fileChanges);
                                foreach (var group in grouped)
                                {
                                    var groupContainer = new VisualElement();
                                    groupContainer.AddToClassList("pg-file-group");
                                    
                                    var groupHeader = new VisualElement();
                                    groupHeader.AddToClassList("pg-file-group-header");
                                    
                                    var groupTitle = new Label(group.Key);
                                    groupTitle.AddToClassList("pg-file-group-title");
                                    groupHeader.Add(groupTitle);
                                    
                                    var groupCount = new Label($"({group.Value.Count})");
                                    groupCount.AddToClassList("pg-file-group-count");
                                    groupHeader.Add(groupCount);
                                    
                                    groupContainer.Add(groupHeader);
                                    
                                    foreach (var change in group.Value.OrderBy(c => c.Path))
                                    {
                                        groupContainer.Add(CreatePendingChangeItem(change));
                                    }
                                    
                                    _pendingChangesView.Add(groupContainer);
                                }
                            }
                            
                            _statusLabel.text = "Ready";
                        }
                        finally
                        {
                            CloseProgress();
                            _isLoadingPendingChanges = false;
                            // Update overall dashboard once initial data is ready
                            RefreshDashboard();
                        }
                    };
                }
                catch (Exception ex)
                {
                    EditorApplication.delayCall += () =>
                    {
                        Debug.LogError($"[Package Guardian] Error loading pending changes: {ex.Message}");
                        _pendingChangesCount.text = "Error loading changes";
                        _createSnapshotButton.SetEnabled(false);
                        CloseProgress();
                        _isLoadingPendingChanges = false;
                    };
                }
            });
        }
        
        private void ScheduleProgress(float t, string info)
        {
            EditorApplication.delayCall += () => SafeProgress(t, info);
        }
        
        private void SafeProgress(float t, string info)
        {
            if (_progressWindow != null)
            {
                _progressWindow.Progress(Mathf.Clamp01(t), info);
            }
        }
        
        private void CloseProgress()
        {
            if (_progressWindow != null)
            {
                _progressWindow.CloseWindow();
                _progressWindow = null;
            }
        }
        
        /// <summary>
        /// Creates a pending change item for the snapshot panel.
        /// </summary>
        private VisualElement CreatePendingChangeItem(global::PackageGuardian.Core.Diff.FileChange change)
        {
            var container = new VisualElement();
            container.AddToClassList("pg-file-item");
            container.style.cursor = new UnityEngine.UIElements.Cursor { texture = null };

            // Make clickable
            container.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    FileHistoryWindow.ShowWindow(change.Path);
                    evt.StopPropagation();
                }
            });

            // Right-click context menu
            container.RegisterCallback<ContextClickEvent>(evt =>
            {
                ShowFileContextMenu(change);
                evt.StopPropagation();
            });

            string icon = "?";
            string iconClass = "";

            switch (change.Type)
            {
                case global::PackageGuardian.Core.Diff.ChangeType.Added:
                    icon = "+";
                    iconClass = "pg-file-icon-added";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Modified:
                    icon = "~";
                    iconClass = "pg-file-icon-modified";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Deleted:
                    icon = "-";
                    iconClass = "pg-file-icon-deleted";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Renamed:
                    icon = "R";
                    iconClass = "pg-file-icon-renamed";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Copied:
                    icon = "C";
                    iconClass = "pg-file-icon-renamed";
                    break;
            }

            var iconBadge = new Label(icon);
            iconBadge.AddToClassList("pg-file-icon");
            iconBadge.AddToClassList(iconClass);
            container.Add(iconBadge);

            var fileInfo = new VisualElement();
            fileInfo.AddToClassList("pg-file-info");

            // For renames/copies, show both paths
            string displayPath = change.Path;
            if (change.Type == global::PackageGuardian.Core.Diff.ChangeType.Renamed && !string.IsNullOrEmpty(change.NewPath))
            {
                displayPath = $"{change.Path} -> {change.NewPath}";
            }
            else if (change.Type == global::PackageGuardian.Core.Diff.ChangeType.Copied && !string.IsNullOrEmpty(change.NewPath))
            {
                displayPath = $"{change.Path} => {change.NewPath}";
            }

            var pathLabel = new Label(displayPath);
            pathLabel.AddToClassList("pg-file-path");
            pathLabel.style.whiteSpace = WhiteSpace.Normal;
            fileInfo.Add(pathLabel);

            // Add similarity score for renames/copies
            if ((change.Type == global::PackageGuardian.Core.Diff.ChangeType.Renamed || 
                 change.Type == global::PackageGuardian.Core.Diff.ChangeType.Copied) && 
                change.SimilarityScore > 0)
            {
                var scoreLabel = new Label($"{change.SimilarityScore:P0} similar");
                scoreLabel.AddToClassList("pg-label-small");
                scoreLabel.style.color = new UnityEngine.Color(0.58f, 0.64f, 0.69f);
                scoreLabel.style.marginTop = 2;
                fileInfo.Add(scoreLabel);
            }

            container.Add(fileInfo);

            return container;
        }

        /// <summary>
        /// Loads and displays commit details.
        /// </summary>
        private void LoadCommitDetails(string commitId)
        {
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;

                if (repo == null)
                {
                    Debug.LogWarning("[Package Guardian] Repository not initialized.");
                    return;
                }

                var commit = repo.Store.ReadObject(commitId) as Commit;
                if (commit == null)
                {
                    Debug.LogWarning($"[Package Guardian] Commit not found: {commitId}");
                    return;
                }

                HideEmptyState();

                // Update commit hash
                _selectedCommitHash.text = commitId.Substring(0, 8);

                // Update commit type badge (we'll need the GraphNode for this, but for now use commit message)
                UpdateCommitTypeBadge(commit);

                // Update commit message (first line only)
                string message = commit.Message;
                int newlineIndex = message.IndexOfAny(new[] { '\r', '\n' });
                if (newlineIndex >= 0)
                {
                    message = message.Substring(0, newlineIndex);
                }
                _selectedCommitMessage.text = message;

                // Update meta
                _selectedCommitAuthor.text = string.IsNullOrEmpty(commit.Author) ? "Unknown" : commit.Author;
                _selectedCommitTime.text = DateTimeOffset.FromUnixTimeSeconds(commit.Timestamp).ToLocalTime().ToString("MMM dd, yyyy 'at' HH:mm");

                // Update statistics
                UpdateCommitStatistics(commitId);

                // Load file changes
                LoadFileChanges(commitId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error loading commit details: {ex.Message}");
            }
        }

        private void UpdateCommitTypeBadge(Commit commit)
        {
            string badgeText;
            Color badgeColor;
            Color textColor = Color.white;
            
            string message = commit.Message ?? "";
            bool isStash = message.Contains("stash", StringComparison.OrdinalIgnoreCase) || 
                          message.Contains("Stash", StringComparison.OrdinalIgnoreCase);
            bool isMerge = commit.Parents != null && commit.Parents.Count > 1;
            
            if (isStash)
            {
                badgeText = "STASH";
                badgeColor = new Color(0.7f, 0.6f, 0.9f, 0.2f);
                textColor = new Color(0.7f, 0.6f, 0.9f);
            }
            else if (isMerge)
            {
                badgeText = "MERGE";
                badgeColor = new Color(0.89f, 0.65f, 0.29f, 0.2f);
                textColor = new Color(0.89f, 0.65f, 0.29f);
            }
            else if (message.Contains("UPM:") || message.Contains("Package"))
            {
                badgeText = "PACKAGE";
                badgeColor = new Color(0.36f, 0.64f, 1.0f, 0.2f);
                textColor = new Color(0.36f, 0.64f, 1.0f);
            }
            else if (message.Contains("Manual") || message.Contains("Snapshot"))
            {
                badgeText = "MANUAL";
                badgeColor = new Color(0.21f, 0.75f, 0.69f, 0.2f);
                textColor = new Color(0.21f, 0.75f, 0.69f);
            }
            else
            {
                badgeText = "AUTO";
                badgeColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
                textColor = new Color(0.7f, 0.7f, 0.7f);
            }
            
            _selectedCommitTypeBadge.text = badgeText;
            _selectedCommitTypeBadge.style.backgroundColor = badgeColor;
            _selectedCommitTypeBadge.style.color = textColor;
            _selectedCommitTypeBadge.style.borderLeftWidth = 2;
            _selectedCommitTypeBadge.style.borderLeftColor = textColor;
        }

        private void UpdateCommitStatistics(string commitId)
        {
            var statsContainer = _commitStatsSection[0] as VisualElement;
            if (statsContainer == null) return;
            
            statsContainer.Clear();
            
            try
            {
                var repo = RepositoryService.Instance.Repository;
                var commit = repo.Store.ReadObject(commitId) as Commit;
                
                if (commit == null || commit.Parents == null || commit.Parents.Count == 0)
                {
                    AddStatItem(statsContainer, "Type", "Initial Commit");
                    return;
                }
                
                string parentId = repo.Hasher.ToHex(commit.Parents[0]);
                var diffEngine = new DiffEngine(repo.Store);
                var changes = diffEngine.CompareCommits(parentId, commitId);
                
                int added = changes.Count(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Added);
                int modified = changes.Count(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Modified);
                int deleted = changes.Count(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Deleted);
                int renamed = changes.Count(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Renamed);
                
                AddStatItem(statsContainer, "Files Changed", changes.Count.ToString());
                if (added > 0) AddStatItem(statsContainer, "Added", added.ToString(), new Color(0.21f, 0.75f, 0.69f));
                if (modified > 0) AddStatItem(statsContainer, "Modified", modified.ToString(), new Color(0.36f, 0.64f, 1.0f));
                if (deleted > 0) AddStatItem(statsContainer, "Deleted", deleted.ToString(), new Color(0.89f, 0.29f, 0.29f));
                if (renamed > 0) AddStatItem(statsContainer, "Renamed", renamed.ToString(), new Color(0.89f, 0.65f, 0.29f));
            }
            catch
            {
                // Ignore errors in stats
            }
        }

        private void AddStatItem(VisualElement container, string label, string value, Color? valueColor = null)
        {
            var statItem = new VisualElement();
            statItem.style.flexDirection = FlexDirection.Column;
            statItem.style.marginRight = 40;
            statItem.style.flexShrink = 0;
            
            var statLabel = new Label(label);
            statLabel.style.fontSize = 11;
            statLabel.style.color = new Color(0.69f, 0.69f, 0.69f);
            statLabel.style.marginBottom = 8;
            statItem.Add(statLabel);
            
            var statValue = new Label(value);
            statValue.style.fontSize = 20;
            statValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            statValue.style.color = valueColor ?? new Color(0.9f, 0.9f, 0.9f);
            statItem.Add(statValue);
            
            container.Add(statItem);
        }

        /// <summary>
        /// Loads and displays file changes for the selected commit.
        /// </summary>
        private void LoadFileChanges(string commitId)
        {
            var contentContainer = _fileChangesView.contentContainer;
            contentContainer.Clear();

            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;

                if (repo == null) return;

                var commit = repo.Store.ReadObject(commitId) as Commit;
                if (commit == null) return;

                string parentCommitId = null;
                if (commit.Parents.Any())
                {
                    parentCommitId = repo.Hasher.ToHex(commit.Parents.First());
                }

                List<global::PackageGuardian.Core.Diff.FileChange> fileChanges;
                var diffEngine = new DiffEngine(repo.Store);
                
                if (parentCommitId != null)
                {
                    fileChanges = diffEngine.CompareCommits(parentCommitId, commitId);
                }
                else
                {
                    fileChanges = new List<global::PackageGuardian.Core.Diff.FileChange>();
                }

                if (!fileChanges.Any())
                {
                    var emptyLabel = new Label("No file changes in this commit");
                    emptyLabel.AddToClassList("pg-label-secondary");
                    emptyLabel.style.paddingTop = 20;
                    emptyLabel.style.paddingBottom = 20;
                    emptyLabel.style.paddingLeft = 20;
                    emptyLabel.style.paddingRight = 20;
                    contentContainer.Add(emptyLabel);
                    return;
                }

                var addedFiles = fileChanges.Where(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Added).ToList();
                var modifiedFiles = fileChanges.Where(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Modified).ToList();
                var deletedFiles = fileChanges.Where(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Deleted).ToList();
                var renamedFiles = fileChanges.Where(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Renamed).ToList();
                
                if (addedFiles.Count > 0)
                {
                    AddFileGroupToView(contentContainer, "Added", addedFiles, new Color(0.21f, 0.75f, 0.69f));
                }
                if (modifiedFiles.Count > 0)
                {
                    AddFileGroupToView(contentContainer, "Modified", modifiedFiles, new Color(0.36f, 0.64f, 1.0f));
                }
                if (renamedFiles.Count > 0)
                {
                    AddFileGroupToView(contentContainer, "Renamed", renamedFiles, new Color(0.89f, 0.65f, 0.29f));
                }
                if (deletedFiles.Count > 0)
                {
                    AddFileGroupToView(contentContainer, "Deleted", deletedFiles, new Color(0.89f, 0.29f, 0.29f));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error loading file changes: {ex.Message}");
            }
        }

        private void AddFileGroupToView(VisualElement container, string groupTitle, List<global::PackageGuardian.Core.Diff.FileChange> files, Color accentColor)
        {
            var groupContainer = new VisualElement();
            groupContainer.style.marginBottom = 16;
            groupContainer.style.flexShrink = 0;
            groupContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            groupContainer.style.borderTopWidth = 1;
            groupContainer.style.borderBottomWidth = 1;
            groupContainer.style.borderLeftWidth = 1;
            groupContainer.style.borderRightWidth = 1;
            groupContainer.style.borderTopColor = new Color(0.165f, 0.165f, 0.165f);
            groupContainer.style.borderBottomColor = new Color(0.165f, 0.165f, 0.165f);
            groupContainer.style.borderLeftColor = accentColor;
            groupContainer.style.borderRightColor = new Color(0.165f, 0.165f, 0.165f);
            groupContainer.style.borderLeftWidth = 3;
            
            bool isExpanded = true;
            
            var groupHeader = new VisualElement();
            groupHeader.style.flexDirection = FlexDirection.Row;
            groupHeader.style.alignItems = Align.Center;
            groupHeader.style.paddingTop = 12;
            groupHeader.style.paddingBottom = 12;
            groupHeader.style.paddingLeft = 16;
            groupHeader.style.paddingRight = 16;
            groupHeader.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            groupHeader.style.cursor = new UnityEngine.UIElements.Cursor { texture = null };
            
            var expandIcon = new Label("▼");
            expandIcon.name = "expandIcon";
            expandIcon.style.width = 16;
            expandIcon.style.height = 16;
            expandIcon.style.fontSize = 10;
            expandIcon.style.color = new Color(0.69f, 0.69f, 0.69f);
            expandIcon.style.marginRight = 12;
            expandIcon.style.flexShrink = 0;
            groupHeader.Add(expandIcon);
            
            var groupTitleLabel = new Label(groupTitle);
            groupTitleLabel.style.fontSize = 13;
            groupTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            groupTitleLabel.style.color = accentColor;
            groupTitleLabel.style.marginRight = 12;
            groupTitleLabel.style.flexGrow = 1;
            groupHeader.Add(groupTitleLabel);
            
            var countLabel = new Label($"{files.Count} file{(files.Count != 1 ? "s" : "")}");
            countLabel.style.fontSize = 10;
            countLabel.style.color = new Color(0.69f, 0.69f, 0.69f);
            countLabel.style.paddingLeft = 8;
            countLabel.style.paddingRight = 8;
            countLabel.style.paddingTop = 4;
            countLabel.style.paddingBottom = 4;
            countLabel.style.backgroundColor = new Color(0.165f, 0.165f, 0.165f);
            countLabel.style.borderTopLeftRadius = 3;
            countLabel.style.borderTopRightRadius = 3;
            countLabel.style.borderBottomLeftRadius = 3;
            countLabel.style.borderBottomRightRadius = 3;
            groupHeader.Add(countLabel);
            
            var filesContainer = new VisualElement();
            filesContainer.style.flexDirection = FlexDirection.Column;
            filesContainer.style.paddingLeft = 16;
            filesContainer.style.paddingRight = 16;
            filesContainer.style.paddingTop = 8;
            filesContainer.style.paddingBottom = 12;
            filesContainer.style.display = DisplayStyle.Flex;
            
            foreach (var change in files.OrderBy(c => c.Path))
            {
                var item = CreateFileChangeItem(change);
                item.style.borderLeftWidth = 2;
                item.style.borderLeftColor = accentColor;
                item.style.marginBottom = 4;
                filesContainer.Add(item);
            }
            
            groupHeader.RegisterCallback<ClickEvent>(evt => {
                isExpanded = !isExpanded;
                expandIcon.text = isExpanded ? "▼" : "▶";
                filesContainer.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            });
            
            groupHeader.RegisterCallback<MouseEnterEvent>(evt => {
                groupHeader.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            });
            groupHeader.RegisterCallback<MouseLeaveEvent>(evt => {
                groupHeader.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            });
            
            groupContainer.Add(groupHeader);
            groupContainer.Add(filesContainer);
            container.Add(groupContainer);
        }

        /// <summary>
        /// Groups file changes by file type with comprehensive Unity categorization.
        /// </summary>
        private Dictionary<string, List<global::PackageGuardian.Core.Diff.FileChange>> GroupFilesByType(
            List<global::PackageGuardian.Core.Diff.FileChange> changes)
        {
            var groups = new Dictionary<string, List<global::PackageGuardian.Core.Diff.FileChange>>();

            foreach (var change in changes)
            {
                string groupName = CategorizeFile(change.Path);

                if (!groups.ContainsKey(groupName))
                {
                    groups[groupName] = new List<global::PackageGuardian.Core.Diff.FileChange>();
                }

                groups[groupName].Add(change);
            }

            return groups;
        }

        /// <summary>
        /// Categorize a file based on its extension and path.
        /// </summary>
        private string CategorizeFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLower();
            var fileName = System.IO.Path.GetFileName(path).ToLower();

            // Scripts & Code
            if (ext == ".cs") return "Scripts (C#)";
            if (ext == ".js") return "Scripts (JavaScript)";
            if (ext == ".shader" || ext == ".cginc" || ext == ".hlsl" || ext == ".glsl") return "Shaders";
            if (ext == ".shadergraph" || ext == ".shadersubgraph") return "Shader Graphs";
            if (ext == ".compute") return "Compute Shaders";

            // Scenes & Prefabs
            if (ext == ".unity") return "Scenes";
            if (ext == ".prefab") return "Prefabs";

            // Models & 3D
            if (ext == ".fbx") return "Models (FBX)";
            if (ext == ".obj") return "Models (OBJ)";
            if (ext == ".blend") return "Models (Blender)";
            if (ext == ".dae") return "Models (Collada)";
            if (ext == ".3ds" || ext == ".max") return "Models (3DS Max)";
            if (ext == ".ma" || ext == ".mb") return "Models (Maya)";

            // Animations
            if (ext == ".anim") return "Animations";
            if (ext == ".controller") return "Animator Controllers";
            if (ext == ".overrideController") return "Animator Overrides";
            if (ext == ".mask") return "Avatar Masks";

            // Audio
            if (ext == ".wav" || ext == ".mp3" || ext == ".ogg" || ext == ".aiff" || ext == ".aif") return "Audio";
            if (ext == ".mixer") return "Audio Mixers";

            // Textures & Images
            if (ext == ".png") return "Textures (PNG)";
            if (ext == ".jpg" || ext == ".jpeg") return "Textures (JPEG)";
            if (ext == ".tga") return "Textures (TGA)";
            if (ext == ".psd") return "Textures (PSD)";
            if (ext == ".tiff" || ext == ".tif") return "Textures (TIFF)";
            if (ext == ".exr") return "Textures (EXR/HDR)";
            if (ext == ".hdr") return "Textures (HDR)";
            if (ext == ".dds") return "Textures (DDS)";

            // Materials & Rendering
            if (ext == ".mat") return "Materials";
            if (ext == ".cubemap") return "Cubemaps";
            if (ext == ".flare") return "Lens Flares";
            if (ext == ".renderTexture") return "Render Textures";
            if (ext == ".lighting") return "Lighting Settings";
            if (ext == ".giparams") return "Lightmap Parameters";

            // Physics
            if (ext == ".physicMaterial") return "Physics Materials";
            if (ext == ".physicsMaterial2D") return "Physics Materials 2D";

            // UI
            if (ext == ".fontsettings" || ext == ".ttf" || ext == ".otf") return "Fonts";
            if (ext == ".guiskin") return "GUI Skins";
            if (ext == ".uss") return "UI Toolkit Styles";
            if (ext == ".uxml") return "UI Toolkit Documents";

            // Terrain
            if (ext == ".terrainlayer") return "Terrain Layers";
            if (ext == ".asset" && path.Contains("Terrain")) return "Terrain Data";

            // Timeline & Cinemachine
            if (ext == ".playable") return "Timeline";
            if (ext == ".signal") return "Timeline Signals";

            // Visual Effects
            if (ext == ".vfx") return "Visual Effect Graphs";
            if (ext == ".vfxoperator" || ext == ".vfxblock") return "VFX Components";

            // Packages & Configuration
            if (ext == ".json")
            {
                if (fileName.Contains("manifest") || fileName.Contains("package")) return "Packages";
                if (fileName.Contains("assembly")) return "Assembly Definitions";
                return "Configuration (JSON)";
            }
            if (ext == ".asmdef" || ext == ".asmref") return "Assembly Definitions";
            if (ext == ".rsp") return "Compiler Settings";

            // Project Settings
            if (path.StartsWith("ProjectSettings/"))
            {
                if (fileName.Contains("input")) return "Project Settings (Input)";
                if (fileName.Contains("tag") || fileName.Contains("layer")) return "Project Settings (Tags/Layers)";
                if (fileName.Contains("quality")) return "Project Settings (Quality)";
                if (fileName.Contains("physics")) return "Project Settings (Physics)";
                if (fileName.Contains("graphics")) return "Project Settings (Graphics)";
                return "Project Settings";
            }

            // Data & Assets
            if (ext == ".asset")
            {
                if (path.Contains("ScriptableObject") || path.Contains("Settings")) return "Scriptable Objects";
                return "Asset Files";
            }
            if (ext == ".bytes") return "Binary Data";
            if (ext == ".txt") return "Text Files";
            if (ext == ".xml") return "XML Data";
            if (ext == ".csv") return "CSV Data";

            // Video
            if (ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".webm") return "Video";

            // Meta Files
            if (ext == ".meta") return "Meta Files";

            // Other
            return "Other";
        }

        /// <summary>
        /// Show context menu for a file.
        /// </summary>
        private void ShowFileContextMenu(global::PackageGuardian.Core.Diff.FileChange change)
        {
            var menu = new GenericMenu();
            
            menu.AddItem(new GUIContent("View File History"), false, () => 
            {
                FileHistoryWindow.ShowWindow(change.Path);
            });
            
            menu.AddItem(new GUIContent("View Diff"), false, () => 
            {
                OnViewDiff(change);
            });
            
            menu.AddSeparator("");
            
            if (change.Type != global::PackageGuardian.Core.Diff.ChangeType.Deleted)
            {
                menu.AddItem(new GUIContent("Open in Unity"), false, () => 
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(change.Path);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                });
                
                menu.AddItem(new GUIContent("Show in Explorer"), false, () => 
                {
                    string fullPath = System.IO.Path.Combine(Application.dataPath, "..", change.Path);
                    EditorUtility.RevealInFinder(fullPath);
                });
            }
            
            menu.AddSeparator("");
            
            menu.AddItem(new GUIContent("Copy Path"), false, () => 
            {
                EditorGUIUtility.systemCopyBuffer = change.Path;
            });
            
            menu.ShowAsContext();
        }

        /// <summary>
        /// Creates a file change list item.
        /// </summary>
        private VisualElement CreateFileChangeItem(global::PackageGuardian.Core.Diff.FileChange change)
        {
            var container = new VisualElement();
            container.AddToClassList("pg-file-item");
            container.style.cursor = new UnityEngine.UIElements.Cursor { texture = null };

            // Make clickable - open file history on click
            container.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) // Left click
                {
                    FileHistoryWindow.ShowWindow(change.Path);
                    evt.StopPropagation();
                }
            });

            // Right-click context menu
            container.RegisterCallback<ContextClickEvent>(evt =>
            {
                ShowFileContextMenu(change);
                evt.StopPropagation();
            });

            // Change type icon
            string icon = "?";
            string iconClass = "";

            switch (change.Type)
            {
                case global::PackageGuardian.Core.Diff.ChangeType.Added:
                    icon = "+";
                    iconClass = "pg-file-icon-added";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Modified:
                    icon = "~";
                    iconClass = "pg-file-icon-modified";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Deleted:
                    icon = "-";
                    iconClass = "pg-file-icon-deleted";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Renamed:
                    icon = "→";
                    iconClass = "pg-file-icon-renamed";
                    break;
            }

            var iconBadge = new Label(icon);
            iconBadge.AddToClassList("pg-file-icon");
            iconBadge.AddToClassList(iconClass);
            container.Add(iconBadge);

            var fileInfo = new VisualElement();
            fileInfo.AddToClassList("pg-file-info");
            fileInfo.style.flexGrow = 1;
            fileInfo.style.minWidth = 0;

            var pathLabel = new Label(change.Path);
            pathLabel.AddToClassList("pg-file-path");
            pathLabel.style.whiteSpace = WhiteSpace.Normal;
            pathLabel.style.overflow = Overflow.Hidden;
            pathLabel.style.textOverflow = TextOverflow.Ellipsis;
            fileInfo.Add(pathLabel);

            container.Add(fileInfo);

            // View diff button
            var viewButton = new Button(() => OnViewDiff(change));
            viewButton.text = "View Diff";
            viewButton.AddToClassList("pg-button");
            viewButton.AddToClassList("pg-button-small");
            container.Add(viewButton);

            return container;
        }

        // ============================================================================
        // Event Handlers
        // ============================================================================

        private void OnCreateSnapshotFromPanel()
        {
            var message = _snapshotMessageField.value;
            
            if (string.IsNullOrWhiteSpace(message))
            {
                EditorUtility.DisplayDialog("Package Guardian", "Please enter a snapshot message", "OK");
                return;
            }

            try
            {
                var service = RepositoryService.Instance;
                string commitId = service.CreateSnapshot(message);
                Debug.Log($"[Package Guardian] Snapshot created: {commitId}");
                
                // Clear the message field
                _snapshotMessageField.value = "";
                
                // Refresh dashboard
                RefreshDashboard();
                
                // Hide snapshot panel
                _snapshotPanel.style.display = DisplayStyle.None;
                
                // Show success message
                EditorUtility.DisplayDialog("Success", "Snapshot created successfully!", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create snapshot: {ex.Message}", "OK");
                Debug.LogError($"[Package Guardian] Snapshot creation error: {ex}");
            }
        }

        private void OnRollback()
        {
            RollbackWizard.ShowWindow();
        }

        private void OnCreateStash()
        {
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                var settings = Settings.PackageGuardianSettings.Instance;
                string author = $"{settings.authorName} <{settings.authorEmail}>";
                string stashId = repo.Stash.CreateAutoStash("Manual stash", author);
                Debug.Log($"[Package Guardian] Stash created: {stashId}");
                EditorUtility.DisplayDialog("Success", "Changes stashed successfully!", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create stash: {ex.Message}", "OK");
            }
        }

        private void OnSearchChanged(string searchText)
        {
            _currentSearchText = searchText.ToLower();
            _graphView.Refresh();
        }
        
        private void ShowViewMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Health & Safety"), false, () => HealthWindow.ShowWindow());
            menu.AddItem(new GUIContent("Stash Manager"), false, () => StashManagerWindow.ShowWindow());
            menu.AddItem(new GUIContent("Rollback Wizard"), false, () => RollbackWizard.ShowWindow());
            menu.AddItem(new GUIContent("File History"), false, () => FileHistoryWindow.ShowWindow());
            menu.ShowAsContext();
        }

        private void OnViewDiff(global::PackageGuardian.Core.Diff.FileChange change)
        {
            if (_selectedCommitId != null)
            {
                Windows.UnifiedDashboard.DiffWindow.ShowDiff(change, _selectedCommitId);
            }
        }

        private void RefreshDashboard()
        {
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;

                if (repo == null)
                {
                    _statusLabel.text = "Not Initialized";
                    UpdateOverviewStats(0, 0, 0, null);
                    return;
                }

                var headCommitId = repo.Refs.ResolveHead();
                Commit headCommit = null;
                if (!string.IsNullOrEmpty(headCommitId))
                {
                    headCommit = repo.Store.ReadObject(headCommitId) as Commit;
                    if (headCommit != null)
                    {
                        var shortMessage = headCommit.Message.Split('\n', '\r')[0];
                        _lastCommitLabel.text = shortMessage.Length > 30 ? shortMessage.Substring(0, 30) + "..." : shortMessage;
                    }
                }
                else
                {
                    _lastCommitLabel.text = "No commits";
                }

                _statusLabel.text = "Ready";

                var viewModel = new GraphViewModel();
                viewModel.Load();
                
                int totalCommits = viewModel.Nodes.Count(n => !n.IsStash);
                int totalStashes = viewModel.Nodes.Count(n => n.IsStash);
                
                var latestCommit = viewModel.Nodes.Where(n => !n.IsStash).OrderByDescending(n => n.Timestamp).FirstOrDefault();
                
                UpdateOverviewStats(totalCommits, totalStashes, 0, latestCommit);

                _graphView.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error refreshing dashboard: {ex.Message}");
                _statusLabel.text = "Error";
                UpdateOverviewStats(0, 0, 0, null);
            }
        }

        private void UpdateOverviewStats(int commits, int stashes, int uncommitted, GraphNode latestCommit)
        {
            if (_totalCommitsLabel != null)
            {
                _totalCommitsLabel.text = commits.ToString();
            }
            
            if (_totalFilesLabel != null)
            {
                _totalFilesLabel.text = "—";
            }
            
            if (_pendingChangesCountLabel != null)
            {
                _pendingChangesCountLabel.text = uncommitted.ToString();
            }
            
            if (_lastActivityLabel != null)
            {
                if (latestCommit != null)
                {
                    var timeAgo = GetTimeAgo(latestCommit.Timestamp);
                    _lastActivityLabel.text = $"{latestCommit.Message.Split('\n', '\r')[0]} • {timeAgo}";
                }
                else
                {
                    _lastActivityLabel.text = "No commits yet";
                }
            }
        }

        private string GetTimeAgo(long timestamp)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var diff = now - timestamp;
            
            if (diff < 60) return "just now";
            if (diff < 3600) return $"{diff / 60}m ago";
            if (diff < 86400) return $"{diff / 3600}h ago";
            if (diff < 604800) return $"{diff / 86400}d ago";
            
            var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime();
            return date.ToString("MMM dd");
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            float newWidth = evt.newRect.width;
            
            // Skip if width hasn't changed significantly (less than 10px)
            if (_lastProcessedWidth > 0 && System.Math.Abs(newWidth - _lastProcessedWidth) < 10f)
            {
                return;
            }
            
            // Cancel any pending resize update
            if (_resizeThrottleScheduler != null && _resizeThrottleScheduler.isActive)
            {
                _resizeThrottleScheduler.Pause();
            }
            
            // Schedule a debounced update
            _resizeThrottleScheduler = rootVisualElement.schedule.Execute(() =>
            {
                UpdateResponsiveClass(newWidth);
                _lastProcessedWidth = newWidth;
            }).StartingIn((long)RESIZE_DEBOUNCE_MS);
        }

        private void UpdateResponsiveClass(float width)
        {
            var root = rootVisualElement;
            
            // Remove all responsive classes first
            root.RemoveFromClassList("pg-window-narrow");
            root.RemoveFromClassList("pg-window-medium");
            root.RemoveFromClassList("pg-window-wide");
            root.RemoveFromClassList("pg-compact");
            
            // Apply appropriate class based on width
            if (width < 700f)
            {
                root.AddToClassList("pg-window-narrow");
            }
            else if (width < 1000f)
            {
                root.AddToClassList("pg-window-medium");
                root.AddToClassList("pg-compact"); // Keep compact for backward compatibility
            }
            else
            {
                root.AddToClassList("pg-window-wide");
            }
            
            // Close overlay if window is wide enough
            if (width >= 700f && _isOverlayOpen)
            {
                CloseOverlay();
            }
        }
        
        // ============================================================================
        // RESPONSIVE DESIGN METHODS
        // ============================================================================
        
        private void ToggleOverlay()
        {
            if (_isOverlayOpen)
            {
                CloseOverlay();
            }
            else
            {
                OpenOverlay();
            }
        }
        
        private void OpenOverlay()
        {
            _isOverlayOpen = true;
            _leftPaneInOverlayMode = true;
            
            // Show backdrop first
            if (_overlayBackdrop != null)
            {
                _overlayBackdrop.style.display = DisplayStyle.Flex;
                _overlayBackdrop.style.visibility = Visibility.Visible;
                _overlayBackdrop.style.position = Position.Absolute;
                _overlayBackdrop.style.left = 0;
                _overlayBackdrop.style.right = 0;
                _overlayBackdrop.style.top = 0;
                _overlayBackdrop.style.bottom = 0;
                _overlayBackdrop.style.opacity = 0;
                _overlayBackdrop.BringToFront();
                
                // Fade in backdrop
                _overlayBackdrop.schedule.Execute(() => 
                {
                    if (_overlayBackdrop != null)
                    {
                        _overlayBackdrop.style.opacity = 1;
                    }
                }).StartingIn(10);
            }
            
            // Convert left pane to overlay mode
            if (_leftPane != null)
            {
                // Save original position for restoration
                _leftPane.RemoveFromClassList("pg-left-pane");
                _leftPane.AddToClassList("pg-left-pane-overlay");
                
                // Force dimensions and positioning with inline styles
                _leftPane.style.display = DisplayStyle.Flex;
                _leftPane.style.visibility = Visibility.Visible;
                _leftPane.style.position = Position.Absolute;
                _leftPane.style.width = new StyleLength(new Length(80, LengthUnit.Percent));
                _leftPane.style.maxWidth = 400;
                _leftPane.style.minWidth = 300;
                _leftPane.style.top = 0;
                _leftPane.style.bottom = 0;
                _leftPane.style.left = new StyleLength(new Length(-80, LengthUnit.Percent));
                _leftPane.style.opacity = 0;
                
                // Ensure it's in front
                _leftPane.BringToFront();
                
                // Animate to visible position
                _leftPane.schedule.Execute(() => 
                {
                    if (_leftPane != null)
                    {
                        _leftPane.style.left = 0;
                        _leftPane.style.opacity = 1;
                    }
                }).StartingIn(10);
            }
        }
        
        private void CloseOverlay()
        {
            _isOverlayOpen = false;
            
            if (!_leftPaneInOverlayMode)
                return;
                
            _leftPaneInOverlayMode = false;
            
            // Animate overlay out
            if (_leftPane != null)
            {
                _leftPane.style.left = new StyleLength(new Length(-80, LengthUnit.Percent));
                _leftPane.style.opacity = 0;
            }
            
            // Fade out backdrop
            if (_overlayBackdrop != null)
            {
                _overlayBackdrop.style.opacity = 0;
            }
            
            // Restore normal layout after animation completes (300ms)
            rootVisualElement.schedule.Execute(() => 
            {
                if (_leftPane != null && !_isOverlayOpen)
                {
                    // Restore to normal mode
                    _leftPane.RemoveFromClassList("pg-left-pane-overlay");
                    _leftPane.AddToClassList("pg-left-pane");
                    
                    // Clear inline styles to let CSS take over
                    _leftPane.style.position = Position.Relative;
                    _leftPane.style.width = StyleKeyword.Null;
                    _leftPane.style.minWidth = StyleKeyword.Null;
                    _leftPane.style.top = StyleKeyword.Null;
                    _leftPane.style.bottom = StyleKeyword.Null;
                    _leftPane.style.left = StyleKeyword.Null;
                    _leftPane.style.opacity = StyleKeyword.Null;
                }
                if (_overlayBackdrop != null && !_isOverlayOpen)
                {
                    _overlayBackdrop.style.display = DisplayStyle.None;
                    _overlayBackdrop.style.visibility = Visibility.Hidden;
                }
            }).StartingIn(300);
        }
    }
}
