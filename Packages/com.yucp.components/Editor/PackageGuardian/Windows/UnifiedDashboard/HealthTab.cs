using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
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
        
        public HealthTab()
        {
            AddToClassList("pg-tab-content");
            
            // Header
            var header = new VisualElement();
            header.AddToClassList("pg-section-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 16;
            
            var title = new Label("Health & Safety");
            title.AddToClassList("pg-section-title");
            header.Add(title);
            
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            
            _refreshButton = new Button(Refresh);
            _refreshButton.text = "Refresh";
            _refreshButton.AddToClassList("pg-button");
            buttonRow.Add(_refreshButton);
            
            _validateButton = new Button(RunFullValidation);
            _validateButton.text = "Run Full Validation";
            _validateButton.AddToClassList("pg-button");
            _validateButton.AddToClassList("pg-button-primary");
            buttonRow.Add(_validateButton);
            
            var settingsButton = new Button(() => {
                Settings.HealthMonitorSettings.ShowWindow();
            });
            settingsButton.text = "Settings";
            settingsButton.AddToClassList("pg-button");
            buttonRow.Add(settingsButton);
            
            header.Add(buttonRow);
            Add(header);
            
            // Status summary
            _statusLabel = new Label("Status: Unknown");
            _statusLabel.AddToClassList("pg-label");
            _statusLabel.style.marginBottom = 16;
            Add(_statusLabel);
            
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
                
                // Get project validation issues
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
                        AddIssueGroup("Information", info, new Color(0.36f, 0.47f, 1.0f));
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Package Guardian] Failed to refresh health tab: {ex.Message}");
                _issuesContainer.Add(new Label($"Error: {ex.Message}"));
            }
        }
        
        private void RunFullValidation()
        {
            EditorUtility.DisplayProgressBar("Package Guardian", "Running full validation...", 0.5f);
            
            try
            {
                Refresh();
                EditorUtility.DisplayDialog("Package Guardian", "Validation complete. Check the Health & Safety tab for results.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
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
                _statusLabel.style.color = new Color(0.36f, 0.47f, 1.0f); // Blue
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
            group.style.marginBottom = 16;
            
            var groupHeader = new Label(groupTitle);
            groupHeader.AddToClassList("pg-section-title");
            groupHeader.style.color = color;
            groupHeader.style.marginBottom = 8;
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
            card.AddToClassList("pg-section");
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = accentColor;
            card.style.marginBottom = 8;
            
            // Header row with title and auto-fix button
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 4;
            
            // Title
            var title = new Label(issue.Title);
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.94f, 0.96f, 0.98f);
            title.style.flexGrow = 1;
            headerRow.Add(title);
            
            // Auto-fix button
            if (issue.CanAutoFix)
            {
                var autoFixButton = new Button(() => {
                    try
                    {
                        issue.AutoFix?.Invoke();
                        EditorUtility.DisplayDialog("Package Guardian", "Auto-fix applied successfully!", "OK");
                        Refresh();
                    }
                    catch (System.Exception ex)
                    {
                        EditorUtility.DisplayDialog("Package Guardian", $"Auto-fix failed: {ex.Message}", "OK");
                        Debug.LogError($"[Package Guardian] Auto-fix failed: {ex}");
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
                actionTitle.style.color = new Color(0.36f, 0.47f, 1.0f);
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

