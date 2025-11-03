using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using YUCP.Components.Editor.UI;
using YUCP.Components.PackageGuardian.Editor.Services;
using PackageGuardian.Core.Validation;

namespace YUCP.Components.PackageGuardian.Editor.Windows.UnifiedDashboard
{
    /// <summary>
    /// Health & Safety tab showing validation issues and warnings.
    /// </summary>
    public sealed class HealthTab : VisualElement
    {
        private VisualElement _issuesContainer;
        private Label _statusLabel;
        private Button _refreshButton;
        private Button _validateButton;
        
        // Async loading state
        private bool _isValidating;
        private YUCPProgressWindow _progressWindow;
        private CancellationTokenSource _cancellationSource;
        
        public HealthTab()
        {
            AddToClassList("pg-tab-content");
            
            // Header with title only
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 12;
            header.style.paddingTop = 8;
            header.style.paddingBottom = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
            
            var title = new Label("Project Health & Safety");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.94f, 0.96f, 0.98f);
            header.Add(title);
            
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.AddToClassList("pg-button-row");
            
            _refreshButton = new Button(Refresh);
            _refreshButton.text = "Quick Refresh";
            _refreshButton.AddToClassList("pg-button");
            _refreshButton.style.marginRight = 8;
            buttonRow.Add(_refreshButton);
            
            _validateButton = new Button(RunFullValidationAsync);
            _validateButton.text = "Full Scan";
            _validateButton.AddToClassList("pg-button");
            _validateButton.AddToClassList("pg-button-primary");
            _validateButton.style.marginRight = 8;
            buttonRow.Add(_validateButton);
            
            var settingsButton = new Button(() => {
                Settings.HealthMonitorSettings.ShowWindow();
            });
            settingsButton.text = "Settings";
            settingsButton.AddToClassList("pg-button");
            buttonRow.Add(settingsButton);
            
            header.Add(buttonRow);
            Add(header);
            
            // Status summary card
            var statusCard = new VisualElement();
            statusCard.style.backgroundColor = new Color(0.15f, 0.17f, 0.2f);
            statusCard.style.borderTopLeftRadius = 4;
            statusCard.style.borderTopRightRadius = 4;
            statusCard.style.borderBottomLeftRadius = 4;
            statusCard.style.borderBottomRightRadius = 4;
            statusCard.style.paddingLeft = 16;
            statusCard.style.paddingRight = 16;
            statusCard.style.paddingTop = 12;
            statusCard.style.paddingBottom = 12;
            statusCard.style.marginBottom = 16;
            statusCard.style.borderLeftWidth = 3;
            statusCard.style.borderLeftColor = new Color(0.5f, 0.5f, 0.5f);
            
            _statusLabel = new Label("Status: Unknown");
            _statusLabel.style.fontSize = 13;
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _statusLabel.style.color = new Color(0.94f, 0.96f, 0.98f);
            statusCard.Add(_statusLabel);
            Add(statusCard);
            
            // Issues container
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            
            _issuesContainer = new VisualElement();
            _issuesContainer.AddToClassList("pg-issues-container");
            scrollView.Add(_issuesContainer);
            
            Add(scrollView);
            
            // Initial load
            Refresh();
        }
        
        public void Refresh()
        {
            _issuesContainer.Clear();
            
            try
            {
                var service = RepositoryService.Instance;
                var issues = new List<ValidationIssue>();
                
                // Get project validation issues (synchronous for quick refresh)
                issues.AddRange(service.ValidateProject());
                
                // Get pending changes validation
                issues.AddRange(service.ValidatePendingChanges());
                
                // Update status
                UpdateStatus(issues);
                
                // Display issues
                if (issues.Count == 0)
                {
                    ShowNoIssues();
                }
                else
                {
                    // Group by severity
                    var critical = issues.FindAll(i => i.Severity == IssueSeverity.Critical);
                    var errors = issues.FindAll(i => i.Severity == IssueSeverity.Error);
                    var warnings = issues.FindAll(i => i.Severity == IssueSeverity.Warning);
                    var info = issues.FindAll(i => i.Severity == IssueSeverity.Info);
                    
                    if (critical.Count > 0)
                    {
                        AddIssueGroup("Critical Issues", critical, new Color(0.8f, 0.1f, 0.1f));
                    }
                    
                    if (errors.Count > 0)
                    {
                        AddIssueGroup("Errors", errors, new Color(0.89f, 0.29f, 0.29f));
                    }
                    
                    if (warnings.Count > 0)
                    {
                        AddIssueGroup("Warnings", warnings, new Color(0.96f, 0.62f, 0.04f));
                    }
                    
                    if (info.Count > 0)
                    {
                        AddIssueGroup("Information", info, new Color(0.5f, 0.5f, 0.5f));
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Package Guardian] Failed to refresh health tab: {ex.Message}");
                _issuesContainer.Add(new Label($"Error: {ex.Message}"));
            }
        }
        
        private async void RunFullValidationAsync()
        {
            if (_isValidating)
            {
                EditorUtility.DisplayDialog("Validation in Progress", "Please wait for the current validation to complete.", "OK");
                return;
            }
            
            _isValidating = true;
            _progressWindow = YUCPProgressWindow.Create();
            _cancellationSource = new CancellationTokenSource();
            
            try
            {
                _statusLabel.text = "Status: Validating...";
                
                var service = RepositoryService.Instance;
                var validator = new ProjectValidator(service.ProjectRoot);
                
                var issues = await Task.Run(() => validator.ValidateProjectAsync(
                    (progress, message) =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            _progressWindow?.Progress(progress, message);
                        };
                    },
                    _cancellationSource.Token
                ));
                
                EditorApplication.delayCall += () =>
                {
                    _issuesContainer.Clear();
                    UpdateStatus(issues);
                    
                    if (issues.Count == 0)
                    {
                        ShowNoIssues();
                    }
                    else
                    {
                        var critical = issues.FindAll(i => i.Severity == IssueSeverity.Critical);
                        var errors = issues.FindAll(i => i.Severity == IssueSeverity.Error);
                        var warnings = issues.FindAll(i => i.Severity == IssueSeverity.Warning);
                        var info = issues.FindAll(i => i.Severity == IssueSeverity.Info);
                        
                        if (critical.Count > 0)
                        {
                            AddIssueGroup("Critical Issues", critical, new Color(0.8f, 0.1f, 0.1f));
                        }
                        
                        if (errors.Count > 0)
                        {
                            AddIssueGroup("Errors", errors, new Color(0.89f, 0.29f, 0.29f));
                        }
                        
                        if (warnings.Count > 0)
                        {
                            AddIssueGroup("Warnings", warnings, new Color(0.96f, 0.62f, 0.04f));
                        }
                        
                        if (info.Count > 0)
                        {
                            AddIssueGroup("Information", info, new Color(0.5f, 0.5f, 0.5f));
                        }
                    }
                };
            }
            catch (System.OperationCanceledException)
            {
                Debug.Log("[Package Guardian] Validation cancelled");
                EditorApplication.delayCall += () =>
                {
                    _statusLabel.text = "Status: Cancelled";
                };
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Package Guardian] Validation failed: {ex.Message}");
                EditorApplication.delayCall += () =>
                {
                    _statusLabel.text = "Status: Error";
                    _issuesContainer.Add(new Label($"Validation error: {ex.Message}"));
                };
            }
            finally
            {
                _progressWindow?.CloseWindow();
                _progressWindow = null;
                _cancellationSource?.Dispose();
                _cancellationSource = null;
                _isValidating = false;
            }
        }
        
        private void UpdateStatus(List<ValidationIssue> issues)
        {
            if (issues.Count == 0)
            {
                _statusLabel.text = "Status: All Clear";
                _statusLabel.style.color = new Color(0.06f, 0.72f, 0.51f); // Green
                return;
            }
            
            var critical = issues.FindAll(i => i.Severity == IssueSeverity.Critical);
            var errors = issues.FindAll(i => i.Severity == IssueSeverity.Error);
            var warnings = issues.FindAll(i => i.Severity == IssueSeverity.Warning);
            
            if (critical.Count > 0)
            {
                _statusLabel.text = $"Status: Critical ({critical.Count} critical, {errors.Count} errors, {warnings.Count} warnings)";
                _statusLabel.style.color = new Color(0.8f, 0.1f, 0.1f); // Dark red
            }
            else if (errors.Count > 0)
            {
                _statusLabel.text = $"Status: Errors Found ({errors.Count} errors, {warnings.Count} warnings)";
                _statusLabel.style.color = new Color(0.89f, 0.29f, 0.29f); // Red
            }
            else if (warnings.Count > 0)
            {
                _statusLabel.text = $"Status: Warnings ({warnings.Count} warnings)";
                _statusLabel.style.color = new Color(0.96f, 0.62f, 0.04f); // Orange
            }
            else
            {
                _statusLabel.text = $"Status: OK ({issues.Count} info)";
                _statusLabel.style.color = new Color(0.7f, 0.7f, 0.7f); // Grey
            }
        }
        
        private void ShowNoIssues()
        {
            var emptyState = new VisualElement();
            emptyState.AddToClassList("pg-empty-state");
            
            var icon = new Label("✓");
            icon.style.fontSize = 48;
            icon.style.color = new Color(0.06f, 0.72f, 0.51f);
            emptyState.Add(icon);
            
            var emptyTitle = new Label("No Issues Detected");
            emptyTitle.AddToClassList("pg-empty-state-title");
            emptyState.Add(emptyTitle);
            
            var emptyDesc = new Label("Your project is healthy and ready for snapshots.");
            emptyDesc.AddToClassList("pg-empty-state-description");
            emptyState.Add(emptyDesc);
            
            _issuesContainer.Add(emptyState);
        }
        
        private void AddIssueGroup(string groupTitle, List<ValidationIssue> issues, Color color)
        {
            var group = new VisualElement();
            group.style.marginBottom = 24;
            
            // Group header with count
            var groupHeader = new VisualElement();
            groupHeader.style.flexDirection = FlexDirection.Row;
            groupHeader.style.alignItems = Align.Center;
            groupHeader.style.marginBottom = 12;
            
            var groupLabel = new Label(groupTitle);
            groupLabel.style.fontSize = 15;
            groupLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            groupLabel.style.color = color;
            groupHeader.Add(groupLabel);
            
            var countLabel = new Label($"({issues.Count})");
            countLabel.style.fontSize = 13;
            countLabel.style.color = new Color(0.7f, 0.75f, 0.8f);
            countLabel.style.marginLeft = 8;
            groupHeader.Add(countLabel);
            
            group.Add(groupHeader);
            
            foreach (var issue in issues)
            {
                group.Add(CreateIssueCard(issue, color));
            }
            
            _issuesContainer.Add(group);
        }
        
        private VisualElement CreateIssueCard(ValidationIssue issue, Color accentColor)
        {
            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.12f, 0.14f, 0.16f);
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = accentColor;
            card.style.borderTopLeftRadius = 4;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4;
            card.style.borderBottomRightRadius = 4;
            card.style.paddingLeft = 16;
            card.style.paddingRight = 16;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            card.style.marginBottom = 12;
            
            // Header row with title and auto-fix button
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.FlexStart;
            headerRow.style.marginBottom = 8;
            
            // Title
            var title = new Label(issue.Title);
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.94f, 0.96f, 0.98f);
            title.style.flexGrow = 1;
            title.style.whiteSpace = WhiteSpace.Normal;
            headerRow.Add(title);
            
            // Auto-fix button
            if (issue.CanAutoFix && issue.AutoFix != null)
            {
                var autoFixButton = new Button(() => {
                    try
                    {
                        issue.AutoFix.Invoke();
                        EditorApplication.delayCall += () => {
                            EditorUtility.DisplayDialog("Package Guardian", "Auto-fix applied successfully!", "OK");
                            Refresh();
                        };
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[Package Guardian] Auto-fix failed: {ex}");
                        EditorApplication.delayCall += () => {
                            EditorUtility.DisplayDialog("Package Guardian", $"Auto-fix failed: {ex.Message}", "OK");
                        };
                    }
                });
                autoFixButton.text = "Auto-Fix";
                autoFixButton.AddToClassList("pg-button");
                autoFixButton.style.backgroundColor = new Color(0.06f, 0.72f, 0.51f);
                autoFixButton.style.color = Color.white;
                autoFixButton.style.paddingLeft = 12;
                autoFixButton.style.paddingRight = 12;
                autoFixButton.style.paddingTop = 4;
                autoFixButton.style.paddingBottom = 4;
                autoFixButton.style.fontSize = 11;
                autoFixButton.style.borderTopLeftRadius = 3;
                autoFixButton.style.borderTopRightRadius = 3;
                autoFixButton.style.borderBottomLeftRadius = 3;
                autoFixButton.style.borderBottomRightRadius = 3;
                headerRow.Add(autoFixButton);
            }
            
            card.Add(headerRow);
            
            // Severity badge
            var severityBadge = new Label(issue.Severity.ToString().ToUpper());
            severityBadge.style.fontSize = 10;
            severityBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            severityBadge.style.paddingLeft = 6;
            severityBadge.style.paddingRight = 6;
            severityBadge.style.paddingTop = 2;
            severityBadge.style.paddingBottom = 2;
            severityBadge.style.borderTopLeftRadius = 3;
            severityBadge.style.borderTopRightRadius = 3;
            severityBadge.style.borderBottomLeftRadius = 3;
            severityBadge.style.borderBottomRightRadius = 3;
            severityBadge.style.alignSelf = Align.FlexStart;
            severityBadge.style.marginBottom = 6;
            severityBadge.style.backgroundColor = accentColor;
            severityBadge.style.color = Color.white;
            card.Add(severityBadge);
            
            // Description
            var desc = new Label(issue.Description);
            desc.AddToClassList("pg-label");
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.marginBottom = 8;
            card.Add(desc);
            
            // Category indicator
            var categoryLabel = new Label($"Category: {issue.Category}");
            categoryLabel.style.fontSize = 10;
            categoryLabel.style.color = new Color(0.7f, 0.75f, 0.8f);
            categoryLabel.style.marginBottom = 8;
            card.Add(categoryLabel);
            
            // Affected paths
            if (issue.AffectedPaths != null && issue.AffectedPaths.Length > 0)
            {
                var pathsLabel = new Label("Affected files:");
                pathsLabel.AddToClassList("pg-label-small");
                pathsLabel.style.marginTop = 4;
                pathsLabel.style.marginBottom = 2;
                pathsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                card.Add(pathsLabel);
                
                // Show paths in a collapsible section for large lists
                int maxPaths = System.Math.Min(5, issue.AffectedPaths.Length);
                for (int i = 0; i < maxPaths; i++)
                {
                    var pathLabel = new Label($"  • {issue.AffectedPaths[i]}");
                    pathLabel.AddToClassList("pg-text-mono");
                    pathLabel.style.fontSize = 11;
                    pathLabel.style.whiteSpace = WhiteSpace.Normal;
                    pathLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
                    card.Add(pathLabel);
                }
                
                if (issue.AffectedPaths.Length > maxPaths)
                {
                    var moreLabel = new Label($"  ... and {issue.AffectedPaths.Length - maxPaths} more");
                    moreLabel.AddToClassList("pg-label-small");
                    moreLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                    card.Add(moreLabel);
                }
            }
            
            // Suggested action
            if (!string.IsNullOrEmpty(issue.SuggestedAction))
            {
                var actionContainer = new VisualElement();
                actionContainer.style.backgroundColor = new Color(0.1f, 0.12f, 0.15f);
                actionContainer.style.borderTopLeftRadius = 4;
                actionContainer.style.borderTopRightRadius = 4;
                actionContainer.style.borderBottomLeftRadius = 4;
                actionContainer.style.borderBottomRightRadius = 4;
                actionContainer.style.paddingLeft = 10;
                actionContainer.style.paddingRight = 10;
                actionContainer.style.paddingTop = 8;
                actionContainer.style.paddingBottom = 8;
                actionContainer.style.marginTop = 10;
                
                var actionTitle = new Label("Suggested Action:");
                actionTitle.style.fontSize = 11;
                actionTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                actionTitle.style.color = new Color(0.6f, 0.6f, 0.6f);
                actionTitle.style.marginBottom = 4;
                actionContainer.Add(actionTitle);
                
                var actionLabel = new Label(issue.SuggestedAction);
                actionLabel.AddToClassList("pg-label");
                actionLabel.style.whiteSpace = WhiteSpace.Normal;
                actionLabel.style.color = new Color(0.8f, 0.85f, 0.9f);
                actionContainer.Add(actionLabel);
                
                card.Add(actionContainer);
            }
            
            return card;
        }
    }
}

