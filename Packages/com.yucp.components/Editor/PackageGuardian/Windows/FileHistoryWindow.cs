using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Services;
using global::PackageGuardian.Core.Objects;

namespace YUCP.Components.PackageGuardian.Editor.Windows
{
    /// <summary>
    /// Window for viewing and restoring file history across commits.
    /// Similar to "git log -- <file>" functionality.
    /// </summary>
    public class FileHistoryWindow : EditorWindow
    {
        private TextField _filePathField;
        private Button _browseButton;
        private Button _searchButton;
        private ScrollView _historyList;
        private VisualElement _previewPane;
        private Label _previewTitle;
        private ScrollView _previewContent;
        
        private string _currentFilePath;
        private List<FileVersion> _versions;
        
		[MenuItem("Tools/Package Guardian/File History")]
        public static void ShowWindow()
        {
            var window = GetWindow<FileHistoryWindow>();
            window.titleContent = new GUIContent("File History");
            window.minSize = new Vector2(900, 600);
            window.Show();
        }
        
        /// <summary>
        /// Open the window with a specific file pre-selected.
        /// </summary>
        public static void ShowWindow(string filePath)
        {
            var window = GetWindow<FileHistoryWindow>();
            window.titleContent = new GUIContent("File History");
            window.minSize = new Vector2(900, 600);
            window._currentFilePath = filePath;
            if (window._filePathField != null)
            {
                window._filePathField.value = filePath;
                window.SearchHistory();
            }
            window.Show();
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
            
            CreateUI();
        }
        
        private void CreateUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            
            // Header
            var header = new VisualElement();
            header.style.paddingTop = 20;
            header.style.paddingBottom = 20;
            header.style.paddingLeft = 20;
            header.style.paddingRight = 20;
            header.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.16f, 0.16f, 0.16f);
            header.style.flexShrink = 0;
            
            var title = new Label("File History");
            title.AddToClassList("pg-title");
            title.style.marginBottom = 8;
            header.Add(title);
            
            var subtitle = new Label("View and restore previous versions of any file");
            subtitle.AddToClassList("pg-label-secondary");
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            header.Add(subtitle);
            
            root.Add(header);
            
            // Search bar
            var searchBar = new VisualElement();
            searchBar.style.flexDirection = FlexDirection.Row;
            searchBar.style.paddingTop = 16;
            searchBar.style.paddingBottom = 16;
            searchBar.style.paddingLeft = 20;
            searchBar.style.paddingRight = 20;
            searchBar.style.backgroundColor = new Color(0.09f, 0.09f, 0.09f);
            searchBar.style.borderBottomWidth = 1;
            searchBar.style.borderBottomColor = new Color(0.16f, 0.16f, 0.16f);
            searchBar.style.flexShrink = 0;
            
            var pathLabel = new Label("File Path:");
            pathLabel.AddToClassList("pg-label");
            pathLabel.style.alignSelf = Align.Center;
            pathLabel.style.marginRight = 8;
            searchBar.Add(pathLabel);
            
            _filePathField = new TextField();
            _filePathField.style.flexGrow = 1;
            _filePathField.AddToClassList("pg-search-input");
            _filePathField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    SearchHistory();
                }
            });
            searchBar.Add(_filePathField);
            
            _browseButton = new Button(BrowseFile);
            _browseButton.text = "Browse";
            _browseButton.AddToClassList("pg-button");
            _browseButton.style.marginLeft = 8;
            searchBar.Add(_browseButton);
            
            _searchButton = new Button(SearchHistory);
            _searchButton.text = "Search";
            _searchButton.AddToClassList("pg-button");
            _searchButton.AddToClassList("pg-button-primary");
            _searchButton.style.marginLeft = 8;
            searchBar.Add(_searchButton);
            
            root.Add(searchBar);
            
            // Content (split view)
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexDirection = FlexDirection.Row;
            content.style.minHeight = 0;
            
            // Left: Version list
            var leftPane = new VisualElement();
            leftPane.style.width = Length.Percent(40);
            leftPane.style.borderRightWidth = 1;
            leftPane.style.borderRightColor = new Color(0.16f, 0.16f, 0.16f);
            leftPane.style.flexDirection = FlexDirection.Column;
            leftPane.AddToClassList("pg-panel");
            
            var leftTitle = new Label("VERSION HISTORY");
            leftTitle.AddToClassList("pg-section-title");
            leftTitle.style.flexShrink = 0;
            leftPane.Add(leftTitle);
            
            _historyList = new ScrollView();
            _historyList.style.flexGrow = 1;
            _historyList.style.minHeight = 0;
            leftPane.Add(_historyList);
            
            content.Add(leftPane);
            
            // Right: Preview
            var rightPane = new VisualElement();
            rightPane.style.flexGrow = 1;
            rightPane.style.flexDirection = FlexDirection.Column;
            rightPane.AddToClassList("pg-panel");
            
            _previewTitle = new Label("FILE PREVIEW");
            _previewTitle.AddToClassList("pg-section-title");
            _previewTitle.style.flexShrink = 0;
            rightPane.Add(_previewTitle);
            
            _previewPane = new VisualElement();
            _previewPane.style.flexGrow = 1;
            _previewPane.style.flexDirection = FlexDirection.Column;
            
            _previewContent = new ScrollView();
            _previewContent.style.flexGrow = 1;
            _previewContent.style.minHeight = 0;
            _previewContent.style.paddingTop = 12;
            _previewContent.style.paddingBottom = 12;
            _previewContent.style.paddingLeft = 12;
            _previewContent.style.paddingRight = 12;
            _previewPane.Add(_previewContent);
            
            rightPane.Add(_previewPane);
            
            content.Add(rightPane);
            
            root.Add(content);
            
            // Show empty state
            ShowEmptyState();
        }
        
        private void BrowseFile()
        {
            string path = EditorUtility.OpenFilePanel("Select File", Application.dataPath, "");
            if (string.IsNullOrEmpty(path))
                return;
            
            // Convert absolute path to project-relative
            string projectPath = Path.GetFullPath(Directory.GetCurrentDirectory());
            if (path.StartsWith(projectPath))
            {
                path = path.Substring(projectPath.Length + 1).Replace('\\', '/');
            }
            
            _filePathField.value = path;
            SearchHistory();
        }
        
        private void SearchHistory()
        {
            _currentFilePath = _filePathField.value;
            
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                EditorUtility.DisplayDialog("File History", "Please enter a file path", "OK");
                return;
            }
            
            try
            {
                var service = RepositoryService.Instance;
                if (service == null || service.Repository == null)
                {
                    EditorUtility.DisplayDialog("File History", "Repository not initialized", "OK");
                    return;
                }
                
                // Normalize path
                _currentFilePath = _currentFilePath.Replace('\\', '/');
                
                // Find all commits that affected this file
                _versions = FindFileVersions(_currentFilePath);
                
                if (_versions.Count == 0)
                {
                    ShowEmptyState("No history found for this file");
                    return;
                }
                
                DisplayVersionList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error searching file history: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to search file history:\n{ex.Message}", "OK");
            }
        }
        
        private List<FileVersion> FindFileVersions(string filePath)
        {
            var versions = new List<FileVersion>();
            var service = RepositoryService.Instance;
            var repo = service.Repository;
            
            // Get all commits in reverse chronological order
            var commits = GetAllCommits().OrderByDescending(c => c.Timestamp).ToList();
            
            foreach (var commit in commits)
            {
                // Check if this file exists in this commit
                string blobId = FindFileInCommit(repo, commit.CommitId, filePath);
                
                if (!string.IsNullOrEmpty(blobId))
                {
                    versions.Add(new FileVersion
                    {
                        CommitId = commit.CommitId,
                        CommitMessage = commit.Message,
                        Timestamp = commit.Timestamp,
                        Author = commit.Author,
                        BlobId = blobId,
                        FilePath = filePath
                    });
                }
            }
            
            return versions;
        }
        
        private List<(string CommitId, string Message, long Timestamp, string Author)> GetAllCommits()
        {
            var commits = new List<(string, string, long, string)>();
            var service = RepositoryService.Instance;
            var repo = service.Repository;
            
            // Start from HEAD and walk backwards
            string currentId = repo.Refs.ResolveHead();
            var visited = new HashSet<string>();
            
            while (!string.IsNullOrEmpty(currentId) && !visited.Contains(currentId))
            {
                visited.Add(currentId);
                
                var commit = repo.Store.ReadObject(currentId) as Commit;
                if (commit == null)
                    break;
                
                commits.Add((currentId, commit.Message, commit.Timestamp, commit.Author));
                
                // Follow first parent
                if (commit.Parents != null && commit.Parents.Count > 0)
                {
                    currentId = BytesToHex(commit.Parents[0]);
                }
                else
                {
                    break;
                }
            }
            
            return commits;
        }
        
        private string FindFileInCommit(global::PackageGuardian.Core.Repository.Repository repo, string commitId, string filePath)
        {
            var commit = repo.Store.ReadObject(commitId) as Commit;
            if (commit == null)
                return null;
            
            string treeId = BytesToHex(commit.TreeId);
            return FindFileInTree(repo, treeId, filePath);
        }
        
        private string FindFileInTree(global::PackageGuardian.Core.Repository.Repository repo, string treeId, string filePath)
        {
            var pathParts = filePath.Split('/');
            string currentTreeId = treeId;
            
            for (int i = 0; i < pathParts.Length; i++)
            {
                var tree = repo.Store.ReadObject(currentTreeId) as global::PackageGuardian.Core.Objects.Tree;
                if (tree == null)
                    return null;
                
                string part = pathParts[i];
                var entry = tree.Entries.FirstOrDefault(e => e.Name == part);
                
                if (entry == null)
                    return null;
                
                string entryId = BytesToHex(entry.ObjectId);
                
                if (i == pathParts.Length - 1)
                {
                    // Last part - should be a file
                    return entry.Mode == "040000" ? null : entryId;
                }
                else
                {
                    // Directory - continue searching
                    if (entry.Mode != "040000")
                        return null;
                    
                    currentTreeId = entryId;
                }
            }
            
            return null;
        }
        
        private void DisplayVersionList()
        {
            _historyList.Clear();
            
            foreach (var version in _versions)
            {
                var item = CreateVersionItem(version);
                _historyList.Add(item);
            }
        }
        
        private VisualElement CreateVersionItem(FileVersion version)
        {
            var container = new VisualElement();
            container.AddToClassList("pg-list-item");
            container.style.paddingTop = 12;
            container.style.paddingBottom = 12;
            container.style.paddingLeft = 12;
            container.style.paddingRight = 12;
            container.style.marginBottom = 4;
            container.style.cursor = new UnityEngine.UIElements.Cursor { texture = null };
            
            // Make it clickable
            container.RegisterCallback<MouseDownEvent>(evt => OnVersionSelected(version));
            
            // Commit hash
            var hashLabel = new Label(version.CommitId.Substring(0, 8));
            hashLabel.AddToClassList("pg-text-mono");
            hashLabel.style.color = new Color(0.36f, 0.64f, 1.0f);
            hashLabel.style.marginBottom = 4;
            container.Add(hashLabel);
            
            // Message
            var messageLabel = new Label(version.CommitMessage);
            messageLabel.AddToClassList("pg-label");
            messageLabel.style.marginBottom = 4;
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            container.Add(messageLabel);
            
            // Metadata
            var metadata = new Label($"{FormatTimestamp(version.Timestamp)} • {version.Author}");
            metadata.AddToClassList("pg-label-small");
            container.Add(metadata);
            
            // Restore button
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.marginTop = 8;
            
            var viewButton = new Button(() => OnVersionSelected(version));
            viewButton.text = "View";
            viewButton.AddToClassList("pg-button");
            viewButton.style.marginRight = 4;
            buttonContainer.Add(viewButton);
            
            var restoreButton = new Button(() => RestoreVersion(version));
            restoreButton.text = "Restore";
            restoreButton.AddToClassList("pg-button");
            restoreButton.AddToClassList("pg-button-primary");
            buttonContainer.Add(restoreButton);
            
            container.Add(buttonContainer);
            
            return container;
        }
        
        private void OnVersionSelected(FileVersion version)
        {
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                var blob = repo.Store.ReadObject(version.BlobId) as Blob;
                if (blob == null)
                {
                    ShowEmptyState("Failed to load file content");
                    return;
                }
                
                _previewContent.Clear();
                _previewTitle.text = $"FILE PREVIEW - {version.CommitId.Substring(0, 8)}";
                
                // Try to display as text
                try
                {
                    string content = System.Text.Encoding.UTF8.GetString(blob.Data);
                    
                    var contentLabel = new Label(content);
                    contentLabel.style.whiteSpace = WhiteSpace.Normal;
                    contentLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
                    contentLabel.style.fontSize = 11;
                    _previewContent.Add(contentLabel);
                }
                catch
                {
                    var binaryLabel = new Label($"Binary file ({blob.Data.Length} bytes)");
                    binaryLabel.AddToClassList("pg-label-secondary");
                    _previewContent.Add(binaryLabel);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error loading file preview: {ex.Message}");
            }
        }
        
        private void RestoreVersion(FileVersion version)
        {
            if (!EditorUtility.DisplayDialog("Restore File Version",
                $"Restore '{version.FilePath}' to version from commit {version.CommitId.Substring(0, 8)}?\n\n" +
                $"This will overwrite the current file.\n\n" +
                $"Commit: {version.CommitMessage}",
                "Restore", "Cancel"))
            {
                return;
            }
            
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                var blob = repo.Store.ReadObject(version.BlobId) as Blob;
                if (blob == null)
                {
                    EditorUtility.DisplayDialog("Error", "Failed to load file content", "OK");
                    return;
                }
                
                // Write file to disk
                string fullPath = Path.Combine(repo.Root, version.FilePath);
                string directory = Path.GetDirectoryName(fullPath);
                
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                File.WriteAllBytes(fullPath, blob.Data);
                
                AssetDatabase.Refresh();
                
                EditorUtility.DisplayDialog("Success", 
                    $"File restored successfully from commit {version.CommitId.Substring(0, 8)}", "OK");
                
                Debug.Log($"[Package Guardian] Restored {version.FilePath} from commit {version.CommitId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error restoring file: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to restore file:\n{ex.Message}", "OK");
            }
        }
        
        private void ShowEmptyState(string message = null)
        {
            _historyList.Clear();
            _previewContent.Clear();
            
            var emptyState = new VisualElement();
            emptyState.style.flexGrow = 1;
            emptyState.style.alignItems = Align.Center;
            emptyState.style.justifyContent = Justify.Center;
            emptyState.style.paddingTop = 40;
            emptyState.style.paddingBottom = 40;
            
            var emptyLabel = new Label(message ?? "Enter a file path to view its history");
            emptyLabel.AddToClassList("pg-label-secondary");
            emptyLabel.style.fontSize = 14;
            emptyState.Add(emptyLabel);
            
            _historyList.Add(emptyState);
        }
        
        private string FormatTimestamp(long timestamp)
        {
            var dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime();
            var now = DateTimeOffset.Now;
            var diff = now - dateTime;
            
            if (diff.TotalDays < 1)
            {
                if (diff.TotalHours < 1)
                {
                    return $"{(int)diff.TotalMinutes} minutes ago";
                }
                return $"{(int)diff.TotalHours} hours ago";
            }
            else if (diff.TotalDays < 7)
            {
                return $"{(int)diff.TotalDays} days ago";
            }
            else
            {
                return dateTime.ToString("MMM dd, yyyy");
            }
        }
        
        private string BytesToHex(byte[] bytes)
        {
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }
        
        private class FileVersion
        {
            public string CommitId { get; set; }
            public string CommitMessage { get; set; }
            public long Timestamp { get; set; }
            public string Author { get; set; }
            public string BlobId { get; set; }
            public string FilePath { get; set; }
        }
    }
}

