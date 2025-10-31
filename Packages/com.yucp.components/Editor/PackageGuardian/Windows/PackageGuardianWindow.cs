using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
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
        private GraphView _graphView;
        private Label _currentBranchLabel;
        private Label _lastCommitLabel;
        private Label _statusLabel;
        private TextField _searchField;
        
        // Right pane elements
        private VisualElement _rightPaneContainer;
        private VisualElement _snapshotPanel;
        private VisualElement _commitDetailsPanel;
        private TextField _snapshotMessageField;
        private ScrollView _pendingChangesView;
        private Button _createSnapshotButton;
        private Label _pendingChangesCount;
        
        private Label _selectedCommitHash;
        private Label _selectedCommitMessage;
        private Label _selectedCommitAuthor;
        private Label _selectedCommitTime;
        private ScrollView _fileChangesView;
        private VisualElement _diffPreviewPanel;
        
        private string _selectedCommitId;
        private string _selectedFilePath;
        private string _currentSearchText = "";

        [MenuItem("Tools/YUCP/Package Guardian")]
        public static void ShowWindow()
        {
            var window = GetWindow<PackageGuardianWindow>();
            window.titleContent = new GUIContent("Package Guardian");
            window.minSize = new Vector2(1200, 700);
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
            var contentContainer = new VisualElement();
            contentContainer.AddToClassList("pg-content-container");

            contentContainer.Add(CreateLeftPane());
            contentContainer.Add(CreateRightPane());

            mainContainer.Add(contentContainer);
            root.Add(mainContainer);

            // Initial refresh
            RefreshDashboard();
            
            // Show snapshot panel by default
            ShowSnapshotPanel();
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

            var snapshotButton = new Button(ShowSnapshotPanel);
            snapshotButton.text = "New Snapshot";
            snapshotButton.AddToClassList("pg-button");
            snapshotButton.AddToClassList("pg-button-primary");
            leftSection.Add(snapshotButton);

            var rollbackButton = new Button(OnRollback);
            rollbackButton.text = "Rollback";
            rollbackButton.AddToClassList("pg-button");
            leftSection.Add(rollbackButton);

            var stashButton = new Button(OnCreateStash);
            stashButton.text = "Stash Changes";
            stashButton.AddToClassList("pg-button");
            leftSection.Add(stashButton);
            
            var stashManagerButton = new Button(() => StashManagerWindow.ShowWindow());
            stashManagerButton.text = "Manage Stashes";
            stashManagerButton.AddToClassList("pg-button");
            leftSection.Add(stashManagerButton);

            topBar.Add(leftSection);

            // Center: Status information
            var centerSection = new VisualElement();
            centerSection.AddToClassList("pg-top-bar-center");

            _currentBranchLabel = new Label("Branch: main");
            _currentBranchLabel.AddToClassList("pg-status-badge");
            _currentBranchLabel.AddToClassList("pg-status-badge-active");
            centerSection.Add(_currentBranchLabel);

            _lastCommitLabel = new Label("Last: None");
            _lastCommitLabel.AddToClassList("pg-status-badge");
            centerSection.Add(_lastCommitLabel);

            _statusLabel = new Label("Ready");
            _statusLabel.AddToClassList("pg-status-badge");
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
            
            var settingsButton = new Button(() => UnityEditor.SettingsService.OpenProjectSettings("Project/PackageGuardianSettings"));
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

            var panel = new VisualElement();
            panel.AddToClassList("pg-panel");

            // Header
            var header = new VisualElement();
            header.AddToClassList("pg-panel-header");

            var title = new Label("Commit History");
            title.AddToClassList("pg-title");
            header.Add(title);

            panel.Add(header);

            // Graph view
            _graphView = new GraphView();
            _graphView.OnCommitSelected = OnCommitSelected;
            _graphView.AddToClassList("pg-graph-container");
            panel.Add(_graphView);

            leftPane.Add(panel);

            return leftPane;
        }

        /// <summary>
        /// Creates the right pane with snapshot creation and commit details.
        /// </summary>
        private VisualElement CreateRightPane()
        {
            var rightPane = new VisualElement();
            rightPane.AddToClassList("pg-right-pane");

            _rightPaneContainer = new VisualElement();
            _rightPaneContainer.AddToClassList("pg-panel");
            _rightPaneContainer.style.flexGrow = 1;

            // Snapshot Creation Panel (initially visible)
            _snapshotPanel = CreateSnapshotPanel();
            _rightPaneContainer.Add(_snapshotPanel);

            // Commit Details Panel (initially hidden)
            _commitDetailsPanel = CreateCommitDetailsPanel();
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

            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 16;

            var title = new Label("Create New Snapshot");
            title.AddToClassList("pg-title");
            header.Add(title);

            var closeButton = new Button(() => _snapshotPanel.style.display = DisplayStyle.None);
            closeButton.text = "×";
            closeButton.AddToClassList("pg-button");
            closeButton.style.fontSize = 20;
            closeButton.style.width = 32;
            closeButton.style.height = 32;
            header.Add(closeButton);

            panel.Add(header);

            // Message input section
            var messageSection = new VisualElement();
            messageSection.AddToClassList("pg-section");
            messageSection.style.flexShrink = 0;

            var messageLabel = new Label("Snapshot Message");
            messageLabel.AddToClassList("pg-section-title");
            messageSection.Add(messageLabel);

            _snapshotMessageField = new TextField();
            _snapshotMessageField.multiline = true;
            _snapshotMessageField.value = "";
            _snapshotMessageField.style.minHeight = 80;
            _snapshotMessageField.style.flexShrink = 0;
            messageSection.Add(_snapshotMessageField);

            var hint = new Label("Describe what changed in this snapshot");
            hint.AddToClassList("pg-label-small");
            hint.style.marginTop = 4;
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
            _pendingChangesCount.AddToClassList("pg-label-small");
            changesHeader.Add(_pendingChangesCount);

            changesSection.Add(changesHeader);

            _pendingChangesView = new ScrollView();
            _pendingChangesView.AddToClassList("pg-scrollview");
            _pendingChangesView.style.flexGrow = 1;
            changesSection.Add(_pendingChangesView);

            panel.Add(changesSection);

            // Action buttons
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.FlexEnd;
            actions.style.marginTop = 16;
            actions.style.flexShrink = 0;

            _createSnapshotButton = new Button(OnCreateSnapshotFromPanel);
            _createSnapshotButton.text = "Create Snapshot";
            _createSnapshotButton.AddToClassList("pg-button");
            _createSnapshotButton.AddToClassList("pg-button-primary");
            _createSnapshotButton.SetEnabled(false);
            actions.Add(_createSnapshotButton);

            panel.Add(actions);

            return panel;
        }

        /// <summary>
        /// Creates the commit details panel.
        /// </summary>
        private VisualElement CreateCommitDetailsPanel()
        {
            var panel = new VisualElement();
            panel.style.flexGrow = 1;
            panel.style.flexDirection = FlexDirection.Column;

            // Commit Details Section
            var detailsSection = CreateCommitDetailsSection();
            panel.Add(detailsSection);

            // File Changes Section
            var fileChangesSection = new VisualElement();
            fileChangesSection.AddToClassList("pg-section");
            fileChangesSection.style.flexGrow = 1;

            var fileChangesHeader = new Label("CHANGED FILES");
            fileChangesHeader.AddToClassList("pg-section-title");
            fileChangesSection.Add(fileChangesHeader);

            _fileChangesView = new ScrollView();
            _fileChangesView.AddToClassList("pg-scrollview");
            _fileChangesView.AddToClassList("pg-file-list");
            fileChangesSection.Add(_fileChangesView);

            panel.Add(fileChangesSection);

            return panel;
        }

        /// <summary>
        /// Creates the commit details section.
        /// </summary>
        private VisualElement CreateCommitDetailsSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");

            var header = new Label("COMMIT DETAILS");
            header.AddToClassList("pg-section-title");
            section.Add(header);

            var emptyState = new VisualElement();
            emptyState.AddToClassList("pg-empty-state");
            emptyState.style.minHeight = 100;

            var emptyTitle = new Label("Select a Commit");
            emptyTitle.AddToClassList("pg-empty-state-title");
            emptyState.Add(emptyTitle);

            var emptyDesc = new Label("Click on any commit in the timeline to view details");
            emptyDesc.AddToClassList("pg-empty-state-description");
            emptyState.Add(emptyDesc);

            section.Add(emptyState);

            // Details container (hidden until commit selected)
            var detailsContainer = new VisualElement();
            detailsContainer.style.display = DisplayStyle.None;
            detailsContainer.style.flexShrink = 0;

            _selectedCommitHash = new Label();
            _selectedCommitHash.AddToClassList("pg-text-mono");
            _selectedCommitHash.style.flexShrink = 0;
            detailsContainer.Add(_selectedCommitHash);

            var separator1 = new VisualElement();
            separator1.AddToClassList("pg-separator");
            separator1.style.flexShrink = 0;
            detailsContainer.Add(separator1);

            _selectedCommitMessage = new Label();
            _selectedCommitMessage.AddToClassList("pg-label");
            _selectedCommitMessage.style.whiteSpace = WhiteSpace.Normal;
            _selectedCommitMessage.style.unityFontStyleAndWeight = FontStyle.Bold;
            _selectedCommitMessage.style.fontSize = 14;
            _selectedCommitMessage.style.flexShrink = 0;
            detailsContainer.Add(_selectedCommitMessage);

            _selectedCommitAuthor = new Label();
            _selectedCommitAuthor.AddToClassList("pg-label-secondary");
            _selectedCommitAuthor.style.marginTop = 8;
            _selectedCommitAuthor.style.flexShrink = 0;
            detailsContainer.Add(_selectedCommitAuthor);

            _selectedCommitTime = new Label();
            _selectedCommitTime.AddToClassList("pg-label-small");
            _selectedCommitTime.style.marginTop = 4;
            _selectedCommitTime.style.flexShrink = 0;
            detailsContainer.Add(_selectedCommitTime);

            section.Add(detailsContainer);

            return section;
        }

        /// <summary>
        /// Handles commit selection from the graph view.
        /// </summary>
        private void OnCommitSelected(GraphNode node)
        {
            _selectedCommitId = node.CommitId;
            
            // Switch to commit details view
            _snapshotPanel.style.display = DisplayStyle.None;
            _commitDetailsPanel.style.display = DisplayStyle.Flex;
            
            LoadCommitDetails(node.CommitId);
        }
        
        /// <summary>
        /// Shows the snapshot creation panel.
        /// </summary>
        private void ShowSnapshotPanel()
        {
            // Switch to snapshot view
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
            _pendingChangesView.Clear();
            
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;

                if (repo == null)
                {
                    _pendingChangesCount.text = "Repository not initialized";
                    _createSnapshotButton.SetEnabled(false);
                    return;
                }

                var currentTreeOid = repo.Snapshots.BuildTreeFromDisk(repo.Root, new List<string> { "Assets", "Packages" });
                var headCommit = repo.Refs.ResolveHead();
                string oldTreeOid = null;
                
                if (!string.IsNullOrEmpty(headCommit))
                {
                    var commit = repo.Store.ReadObject(headCommit) as Commit;
                    if (commit != null)
                    {
                        oldTreeOid = repo.Hasher.ToHex(commit.TreeId);
                    }
                }

                var diffEngine = new DiffEngine(repo.Store);
                var fileChanges = diffEngine.CompareTrees("", oldTreeOid, currentTreeOid);

                _pendingChangesCount.text = $"{fileChanges.Count} files changed";
                _createSnapshotButton.SetEnabled(fileChanges.Count > 0);

                if (!fileChanges.Any())
                {
                    var emptyLabel = new Label("No pending changes");
                    emptyLabel.AddToClassList("pg-label-secondary");
                    emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    emptyLabel.style.paddingTop = 40;
                    _pendingChangesView.Add(emptyLabel);
                    return;
                }

                // Group by type
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
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error loading pending changes: {ex.Message}");
                _pendingChangesCount.text = "Error loading changes";
                _createSnapshotButton.SetEnabled(false);
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

                // Show details container, hide empty state
                var emptyState = _commitDetailsPanel.Q<VisualElement>(className: "pg-empty-state");
                var detailsContainer = _commitDetailsPanel[_commitDetailsPanel.childCount - 1];
                if (emptyState != null) emptyState.style.display = DisplayStyle.None;
                detailsContainer.style.display = DisplayStyle.Flex;

                // Update commit details
                _selectedCommitHash.text = $"Commit {commitId.Substring(0, 8)}";
                _selectedCommitMessage.text = commit.Message;
                _selectedCommitAuthor.text = $"Author: {commit.Author}";
                _selectedCommitTime.text = $"Date: {DateTimeOffset.FromUnixTimeSeconds(commit.Timestamp).ToLocalTime():g}";

                // Load file changes
                LoadFileChanges(commitId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error loading commit details: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads and displays file changes for the selected commit.
        /// </summary>
        private void LoadFileChanges(string commitId)
        {
            _fileChangesView.Clear();

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

                // Use CompareCommits if we have a parent, otherwise compare with empty tree
                List<global::PackageGuardian.Core.Diff.FileChange> fileChanges;
                var diffEngine = new DiffEngine(repo.Store);
                
                if (parentCommitId != null)
                {
                    fileChanges = diffEngine.CompareCommits(parentCommitId, commitId);
                }
                else
                {
                    // First commit - all files are added
                    fileChanges = new List<global::PackageGuardian.Core.Diff.FileChange>();
                    // TODO: Implement first commit comparison
                }

                if (!fileChanges.Any())
                {
                    var emptyLabel = new Label("No file changes in this commit");
                    emptyLabel.AddToClassList("pg-label-secondary");
                    emptyLabel.style.paddingTop = 20;
                    emptyLabel.style.paddingBottom = 20;
                    emptyLabel.style.paddingLeft = 20;
                    emptyLabel.style.paddingRight = 20;
                    _fileChangesView.Add(emptyLabel);
                    return;
                }

                // Group files by type
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
                        groupContainer.Add(CreateFileChangeItem(change));
                    }

                    _fileChangesView.Add(groupContainer);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error loading file changes: {ex.Message}");
            }
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

            // File info
            var fileInfo = new VisualElement();
            fileInfo.AddToClassList("pg-file-info");

            var pathLabel = new Label(change.Path);
            pathLabel.AddToClassList("pg-file-path");
            pathLabel.style.whiteSpace = WhiteSpace.Normal;
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
                    return;
                }

                // Update status
                var headCommitId = repo.Refs.ResolveHead();
                if (!string.IsNullOrEmpty(headCommitId))
                {
                    var commit = repo.Store.ReadObject(headCommitId) as Commit;
                    if (commit != null)
                    {
                        _lastCommitLabel.text = $"Last: {commit.Message.Substring(0, Math.Min(30, commit.Message.Length))}";
                    }
                }
                else
                {
                    _lastCommitLabel.text = "Last: None";
                }

                _statusLabel.text = "Ready";

                // Refresh graph
                _graphView.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error refreshing dashboard: {ex.Message}");
                _statusLabel.text = "Error";
            }
        }
    }
}
