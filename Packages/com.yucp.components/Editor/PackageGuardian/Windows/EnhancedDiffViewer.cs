using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Services;
using global::PackageGuardian.Core.Diff;
using global::PackageGuardian.Core.Objects;

namespace YUCP.Components.PackageGuardian.Editor.Windows
{
    /// <summary>
    /// Enhanced diff viewer with syntax highlighting and side-by-side comparison.
    /// </summary>
    public class EnhancedDiffViewer : EditorWindow
    {
        private global::PackageGuardian.Core.Diff.FileChange _fileChange;
        private string _commitId;
        private ScrollView _diffContainer;
        private Label _filePathLabel;
        private VisualElement _statsBar;
        
        public static void ShowWindow(global::PackageGuardian.Core.Diff.FileChange fileChange, string commitId)
        {
            var window = GetWindow<EnhancedDiffViewer>();
            window.titleContent = new GUIContent("Diff Viewer");
            window.minSize = new Vector2(900, 600);
            window._fileChange = fileChange;
            window._commitId = commitId;
            window.Show();
            
            window.LoadDiff();
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
            
            // Header
            var header = new VisualElement();
            header.style.paddingTop = 16;
            header.style.paddingBottom = 16;
            header.style.paddingLeft = 16;
            header.style.paddingRight = 16;
            header.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.16f, 0.16f, 0.16f);
            
            _filePathLabel = new Label();
            _filePathLabel.AddToClassList("pg-label");
            _filePathLabel.style.fontSize = 14;
            _filePathLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _filePathLabel.style.marginBottom = 8;
            header.Add(_filePathLabel);
            
            _statsBar = new VisualElement();
            _statsBar.style.flexDirection = FlexDirection.Row;
            _statsBar.style.alignItems = Align.Center;
            header.Add(_statsBar);
            
            root.Add(header);
            
            // Diff content
            _diffContainer = new ScrollView();
            _diffContainer.AddToClassList("pg-scrollview");
            _diffContainer.style.flexGrow = 1;
            _diffContainer.style.paddingTop = 16;
            _diffContainer.style.paddingBottom = 16;
            _diffContainer.style.paddingLeft = 16;
            _diffContainer.style.paddingRight = 16;
            root.Add(_diffContainer);
            
            // Footer
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
            
            var closeButton = new Button(Close);
            closeButton.text = "Close";
            closeButton.AddToClassList("pg-button");
            footer.Add(closeButton);
            
            root.Add(footer);
        }
        
        private void LoadDiff()
        {
            if (_fileChange == null) return;
            
            _filePathLabel.text = _fileChange.Path;
            _diffContainer.Clear();
            _statsBar.Clear();
            
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                if (repo == null) return;
                
                // Get file contents
                string oldContent = "";
                string newContent = "";
                
                if (!string.IsNullOrEmpty(_fileChange.OldOid))
                {
                    var oldBlob = repo.Store.ReadObject(_fileChange.OldOid) as Blob;
                    if (oldBlob != null)
                    {
                        oldContent = System.Text.Encoding.UTF8.GetString(oldBlob.Data);
                    }
                }
                
                if (!string.IsNullOrEmpty(_fileChange.NewOid))
                {
                    var newBlob = repo.Store.ReadObject(_fileChange.NewOid) as Blob;
                    if (newBlob != null)
                    {
                        newContent = System.Text.Encoding.UTF8.GetString(newBlob.Data);
                    }
                }
                
                // Simple line-by-line diff
                var oldLines = oldContent.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                var newLines = newContent.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                
                // Stats (simplified)
                int addedLines = Math.Max(0, newLines.Length - oldLines.Length);
                int deletedLines = Math.Max(0, oldLines.Length - newLines.Length);
                
                var addedLabel = new Label($"+{addedLines}");
                addedLabel.AddToClassList("pg-file-stats-added");
                addedLabel.style.marginRight = 12;
                _statsBar.Add(addedLabel);
                
                var deletedLabel = new Label($"-{deletedLines}");
                deletedLabel.AddToClassList("pg-file-stats-deleted");
                _statsBar.Add(deletedLabel);
                
                // Render simple side-by-side diff
                int maxLines = Math.Max(oldLines.Length, newLines.Length);
                for (int i = 0; i < maxLines; i++)
                {
                    string oldLine = i < oldLines.Length ? oldLines[i] : "";
                    string newLine = i < newLines.Length ? newLines[i] : "";
                    
                    if (oldLine != newLine)
                    {
                        if (i < oldLines.Length && i >= newLines.Length)
                        {
                            // Deleted line
                            var line = CreateDiffLineSimple(oldLine, i + 1, true, false);
                            _diffContainer.Add(line);
                        }
                        else if (i >= oldLines.Length && i < newLines.Length)
                        {
                            // Added line
                            var line = CreateDiffLineSimple(newLine, i + 1, false, true);
                            _diffContainer.Add(line);
                        }
                        else
                        {
                            // Modified line - show both
                            var oldLineElem = CreateDiffLineSimple(oldLine, i + 1, true, false);
                            _diffContainer.Add(oldLineElem);
                            var newLineElem = CreateDiffLineSimple(newLine, i + 1, false, true);
                            _diffContainer.Add(newLineElem);
                        }
                    }
                    else
                    {
                        // Unchanged line
                        var line = CreateDiffLineSimple(newLine, i + 1, false, false);
                        _diffContainer.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error loading diff: {ex.Message}");
                var errorLabel = new Label($"Error loading diff: {ex.Message}");
                errorLabel.style.color = new Color(0.89f, 0.29f, 0.29f);
                _diffContainer.Add(errorLabel);
            }
        }
        
        private VisualElement CreateDiffLineSimple(string content, int lineNum, bool isDeleted, bool isAdded)
        {
            var container = new VisualElement();
            container.AddToClassList("pg-diff-line");
            
            if (isAdded)
            {
                container.AddToClassList("pg-diff-line-added");
            }
            else if (isDeleted)
            {
                container.AddToClassList("pg-diff-line-deleted");
            }
            
            var lineNumber = new Label(lineNum.ToString());
            lineNumber.AddToClassList("pg-diff-line-number");
            container.Add(lineNumber);
            
            var contentLabel = new Label(content);
            contentLabel.AddToClassList("pg-diff-line-content");
            contentLabel.style.whiteSpace = WhiteSpace.Normal;
            container.Add(contentLabel);
            
            return container;
        }
    }
}

