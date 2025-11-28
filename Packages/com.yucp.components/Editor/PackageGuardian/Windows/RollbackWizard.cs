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
    /// Rollback wizard for reverting to a previous commit with preview.
    /// </summary>
    public class RollbackWizard : EditorWindow
    {
        private GraphView _graphView;
        private Label _selectedCommitLabel;
        private Label _warningLabel;
        private ScrollView _changesPreview;
        private Button _rollbackButton;
        private Button _cancelButton;
        
        private string _selectedCommitId;
        
        public static void ShowWindow()
        {
            var window = GetWindow<RollbackWizard>();
            window.titleContent = new GUIContent("Rollback Wizard");
            window.minSize = new Vector2(800, 600);
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
            header.style.flexShrink = 0; // Don't shrink
            
            var title = new Label("Rollback to Previous State");
            title.AddToClassList("pg-title");
            title.style.marginBottom = 8;
            title.style.whiteSpace = WhiteSpace.Normal;
            header.Add(title);
            
            var subtitle = new Label("Select a commit to restore your project to that point in time");
            subtitle.AddToClassList("pg-label-secondary");
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            header.Add(subtitle);
            
            root.Add(header);
            
            // Content (must grow to fill space between header and footer)
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexDirection = FlexDirection.Row;
            content.style.minHeight = 0; // Allow scrolling
            
            // Left: Commit list
            var leftPane = new VisualElement();
            leftPane.style.width = Length.Percent(50);
            leftPane.style.borderRightWidth = 1;
            leftPane.style.borderRightColor = new Color(0.16f, 0.16f, 0.16f);
            leftPane.style.flexDirection = FlexDirection.Column;
            leftPane.AddToClassList("pg-panel");
            
            var leftTitle = new Label("SELECT COMMIT");
            leftTitle.AddToClassList("pg-section-title");
            leftTitle.style.flexShrink = 0;
            leftPane.Add(leftTitle);
            
            _graphView = new GraphView();
            _graphView.OnCommitSelected = OnCommitSelected;
            _graphView.style.flexGrow = 1;
            _graphView.style.minHeight = 0; // Allow scrolling
            leftPane.Add(_graphView);
            
            content.Add(leftPane);
            
            // Right: Preview
            var rightPane = new VisualElement();
            rightPane.style.flexGrow = 1;
            rightPane.style.flexDirection = FlexDirection.Column;
            rightPane.AddToClassList("pg-panel");
            
            var rightTitle = new Label("ROLLBACK PREVIEW");
            rightTitle.AddToClassList("pg-section-title");
            rightTitle.style.flexShrink = 0;
            rightPane.Add(rightTitle);
            
            // Selected commit info
            var commitInfo = new VisualElement();
            commitInfo.AddToClassList("pg-section");
            commitInfo.style.marginBottom = 16;
            commitInfo.style.flexShrink = 0;
            
            _selectedCommitLabel = new Label("No commit selected");
            _selectedCommitLabel.AddToClassList("pg-label");
            _selectedCommitLabel.style.whiteSpace = WhiteSpace.Normal;
            commitInfo.Add(_selectedCommitLabel);
            
            rightPane.Add(commitInfo);
            
            // Warning
            _warningLabel = new Label("WARNING: This will revert all changes made after the selected commit. Make sure to create a snapshot first!");
            _warningLabel.style.backgroundColor = new Color(0.9f, 0.65f, 0.29f, 0.2f);
            _warningLabel.style.color = new Color(0.89f, 0.65f, 0.29f);
            _warningLabel.style.paddingTop = 12;
            _warningLabel.style.paddingBottom = 12;
            _warningLabel.style.paddingLeft = 12;
            _warningLabel.style.paddingRight = 12;
            _warningLabel.style.marginBottom = 16;
            _warningLabel.style.whiteSpace = WhiteSpace.Normal;
            _warningLabel.style.display = DisplayStyle.None;
            _warningLabel.style.flexShrink = 0;
            rightPane.Add(_warningLabel);
            
            // Changes preview (must grow)
            var previewSection = new VisualElement();
            previewSection.AddToClassList("pg-section");
            previewSection.style.flexGrow = 1;
            previewSection.style.minHeight = 0; // Allow scrolling
            previewSection.style.flexDirection = FlexDirection.Column;
            
            var previewTitle = new Label("Files that will be affected:");
            previewTitle.AddToClassList("pg-label");
            previewTitle.style.marginBottom = 8;
            previewTitle.style.flexShrink = 0;
            previewSection.Add(previewTitle);
            
            _changesPreview = new ScrollView();
            _changesPreview.AddToClassList("pg-scrollview");
            _changesPreview.style.flexGrow = 1;
            _changesPreview.style.minHeight = 0; // Allow scrolling
            previewSection.Add(_changesPreview);
            
            rightPane.Add(previewSection);
            
            content.Add(rightPane);
            
            root.Add(content);
            
            // Footer (don't shrink)
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.FlexEnd;
            footer.style.paddingTop = 16;
            footer.style.paddingBottom = 16;
            footer.style.paddingLeft = 16;
            footer.style.paddingRight = 16;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = new Color(0.16f, 0.16f, 0.16f);
            footer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            footer.style.flexShrink = 0; // Don't shrink
            
            _cancelButton = new Button(Close);
            _cancelButton.text = "Cancel";
            _cancelButton.AddToClassList("pg-button");
            _cancelButton.style.marginRight = 8;
            footer.Add(_cancelButton);
            
            _rollbackButton = new Button(PerformRollback);
            _rollbackButton.text = "Rollback to Selected Commit";
            _rollbackButton.AddToClassList("pg-button");
            _rollbackButton.AddToClassList("pg-button-danger");
            _rollbackButton.SetEnabled(false);
            footer.Add(_rollbackButton);
            
            root.Add(footer);
            
            // Initial load
            _graphView.Refresh();
        }
        
        private void OnCommitSelected(GraphNode node)
        {
            _selectedCommitId = node.CommitId;
            _selectedCommitLabel.text = $"Commit: {node.CommitId.Substring(0, 8)} - {node.Message}";
            _warningLabel.style.display = DisplayStyle.Flex;
            _rollbackButton.SetEnabled(true);
            
            LoadChangesPreview();
        }
        
        private void LoadChangesPreview()
        {
            _changesPreview.Clear();
            
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                if (repo == null) return;
                
                var selectedCommit = repo.Store.ReadObject(_selectedCommitId) as Commit;
                if (selectedCommit == null) return;
                
                var currentHeadId = repo.Refs.ResolveHead();
                if (string.IsNullOrEmpty(currentHeadId))
                {
                    var noHead = new Label("No current commit to compare against");
                    noHead.AddToClassList("pg-label-secondary");
                    _changesPreview.Add(noHead);
                    return;
                }
                
                // Compare: target commit TO current HEAD
                // This shows what changed SINCE the target commit
                var diffEngine = new global::PackageGuardian.Core.Diff.DiffEngine(repo.Store);
                
                // Disable rename detection for rollback - we want to see actual file additions/deletions
                var diffOptions = new global::PackageGuardian.Core.Diff.DiffOptions
                {
                    DetectRenames = false,  // Show as add/delete so we can see what will actually happen
                    DetectCopies = false
                };
                
                var changes = diffEngine.CompareCommits(_selectedCommitId, currentHeadId, diffOptions);
                
                if (!changes.Any())
                {
                    var noChanges = new Label("No changes - already at this commit");
                    noChanges.AddToClassList("pg-label-secondary");
                    _changesPreview.Add(noChanges);
                    return;
                }
                
                // Group changes by action type
                // Note: Added in current = will be DELETED on rollback
                //       Deleted in current = will be RESTORED on rollback
                //       Modified in current = will be REVERTED on rollback
                var willBeDeleted = changes.Where(c => 
                    c.Type == global::PackageGuardian.Core.Diff.ChangeType.Added).ToList();
                var willBeRestored = changes.Where(c => 
                    c.Type == global::PackageGuardian.Core.Diff.ChangeType.Deleted).ToList();
                var willBeReverted = changes.Where(c => 
                    c.Type == global::PackageGuardian.Core.Diff.ChangeType.Modified).ToList();
                
                // Handle renames (shouldn't happen with rename detection off, but just in case)
                var willBeRenamed = changes.Where(c => 
                    c.Type == global::PackageGuardian.Core.Diff.ChangeType.Renamed || 
                    c.Type == global::PackageGuardian.Core.Diff.ChangeType.Copied).ToList();
                
                // Show summary
                var summary = new Label($"Rollback will affect {changes.Count} file(s):");
                summary.AddToClassList("pg-label");
                summary.style.marginBottom = 12;
                summary.style.unityFontStyleAndWeight = FontStyle.Bold;
                _changesPreview.Add(summary);
                
                if (willBeDeleted.Any())
                {
                    var deleteHeader = new Label($"Will DELETE ({willBeDeleted.Count} files added since target):");
                    deleteHeader.style.color = new Color(0.93f, 0.29f, 0.29f); // Red
                    deleteHeader.style.marginTop = 8;
                    deleteHeader.style.marginBottom = 4;
                    _changesPreview.Add(deleteHeader);
                    foreach (var change in willBeDeleted.OrderBy(c => c.Path))
                        _changesPreview.Add(CreateRollbackChangeItem(change, "DELETE", "Will be removed"));
                }
                
                if (willBeRestored.Any())
                {
                    var restoreHeader = new Label($"Will RESTORE ({willBeRestored.Count} files deleted since target):");
                    restoreHeader.style.color = new Color(0.22f, 0.75f, 0.69f); // Teal
                    restoreHeader.style.marginTop = 8;
                    restoreHeader.style.marginBottom = 4;
                    _changesPreview.Add(restoreHeader);
                    foreach (var change in willBeRestored.OrderBy(c => c.Path))
                        _changesPreview.Add(CreateRollbackChangeItem(change, "RESTORE", "Will be brought back"));
                }
                
                if (willBeReverted.Any())
                {
                    var revertHeader = new Label($"Will REVERT ({willBeReverted.Count} files modified since target):");
                    revertHeader.style.color = new Color(0.36f, 0.64f, 1.0f); // Blue
                    revertHeader.style.marginTop = 8;
                    revertHeader.style.marginBottom = 4;
                    _changesPreview.Add(revertHeader);
                    foreach (var change in willBeReverted.OrderBy(c => c.Path))
                        _changesPreview.Add(CreateRollbackChangeItem(change, "REVERT", "Will be restored to old version"));
                }
                
                if (willBeRenamed.Any())
                {
                    var renameHeader = new Label($"Will UNDO RENAMES ({willBeRenamed.Count} renamed/moved files):");
                    renameHeader.style.color = new Color(0.85f, 0.65f, 0.29f); // Orange
                    renameHeader.style.marginTop = 8;
                    renameHeader.style.marginBottom = 4;
                    _changesPreview.Add(renameHeader);
                    foreach (var change in willBeRenamed.OrderBy(c => c.Path))
                        _changesPreview.Add(CreateRollbackChangeItem(change, "RENAME", $"Will move back to old location"));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error loading changes preview: {ex.Message}");
                Debug.LogException(ex);
            }
        }
        
        private VisualElement CreateRollbackChangeItem(global::PackageGuardian.Core.Diff.FileChange change, string action, string description)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.paddingTop = 4;
            container.style.paddingBottom = 4;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 4;
            container.style.marginBottom = 2;
            
            // Action badge
            var badge = new Label(action);
            badge.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            badge.style.color = Color.white;
            badge.style.paddingTop = 2;
            badge.style.paddingBottom = 2;
            badge.style.paddingLeft = 6;
            badge.style.paddingRight = 6;
            badge.style.borderTopLeftRadius = 3;
            badge.style.borderTopRightRadius = 3;
            badge.style.borderBottomLeftRadius = 3;
            badge.style.borderBottomRightRadius = 3;
            badge.style.fontSize = 10;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.marginRight = 8;
            badge.style.minWidth = 65;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            
            if (action == "DELETE")
                badge.style.backgroundColor = new Color(0.93f, 0.29f, 0.29f, 0.3f);
            else if (action == "RESTORE")
                badge.style.backgroundColor = new Color(0.22f, 0.75f, 0.69f, 0.3f);
            else if (action == "REVERT")
                badge.style.backgroundColor = new Color(0.36f, 0.64f, 1.0f, 0.3f);
            
            container.Add(badge);
            
            // File path
            var pathLabel = new Label(change.Path);
            pathLabel.style.flexGrow = 1;
            pathLabel.AddToClassList("pg-label");
            container.Add(pathLabel);
            
            return container;
        }
        
        private VisualElement CreateChangeItem(global::PackageGuardian.Core.Diff.FileChange change)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.paddingTop = 4;
            container.style.paddingBottom = 4;
            container.style.paddingLeft = 4;
            container.style.paddingRight = 4;
            container.style.marginBottom = 2;
            
            // Icon
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
            }
            
            var iconLabel = new Label(icon);
            iconLabel.AddToClassList("pg-file-icon");
            iconLabel.AddToClassList(iconClass);
            container.Add(iconLabel);
            
            var pathLabel = new Label(change.Path);
            pathLabel.AddToClassList("pg-label");
            pathLabel.style.flexGrow = 1;
            container.Add(pathLabel);
            
            return container;
        }
        
        private void PerformRollback()
        {
            if (string.IsNullOrEmpty(_selectedCommitId))
            {
                EditorUtility.DisplayDialog("Error", "No commit selected", "OK");
                return;
            }
            
            var confirm = EditorUtility.DisplayDialog(
                "Confirm Rollback",
                $"Are you sure you want to rollback to commit {_selectedCommitId.Substring(0, 8)}?\n\n" +
                "This will revert all changes made after this commit. This action cannot be undone!\n\n" +
                "It's recommended to create a snapshot before proceeding.",
                "Rollback",
                "Cancel"
            );
            
            if (!confirm) return;
            
            try
            {
                EditorUtility.DisplayProgressBar("Package Guardian", "Rolling back...", 0.5f);
                
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                // Perform rollback
                repo.Checkout.CheckoutCommit(_selectedCommitId);
                
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Success", "Rollback completed successfully!", "OK");
                
                AssetDatabase.Refresh();
                Close();
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                
                // Show detailed error message
                string errorTitle = "Rollback Failed";
                string errorMessage = ex.Message;
                
                // For locked file errors, show a more helpful dialog
                if (ex is InvalidOperationException && ex.Message.Contains("locked by Unity"))
                {
                    errorTitle = "Rollback Partially Completed";
                }
                
                EditorUtility.DisplayDialog(errorTitle, errorMessage, "OK");
                Debug.LogError($"[Package Guardian] Rollback error: {ex}");
            }
        }
    }
}

