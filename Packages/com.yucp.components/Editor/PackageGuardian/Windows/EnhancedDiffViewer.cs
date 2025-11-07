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
        private ListView _diffListView;
        private Label _filePathLabel;
        private VisualElement _statsBar;
        private List<DiffLineData> _diffLines;
        
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
            
            // Diff content with virtualization
            _diffLines = new List<DiffLineData>();
            _diffListView = new ListView();
            _diffListView.AddToClassList("pg-scrollview");
            _diffListView.style.flexGrow = 1;
            _diffListView.fixedItemHeight = 20; // Fixed height enables virtualization
            _diffListView.makeItem = MakeDiffLineItem;
            _diffListView.bindItem = BindDiffLineItem;
            _diffListView.itemsSource = _diffLines;
            _diffListView.selectionType = SelectionType.None;
            root.Add(_diffListView);
            
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
            _diffLines.Clear();
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
                
                // Build diff line data for virtualization
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
                            _diffLines.Add(new DiffLineData
                            {
                                Content = oldLine,
                                OldLineNumber = i + 1,
                                NewLineNumber = 0,
                                IsDeleted = true,
                                IsAdded = false
                            });
                        }
                        else if (i >= oldLines.Length && i < newLines.Length)
                        {
                            // Added line
                            _diffLines.Add(new DiffLineData
                            {
                                Content = newLine,
                                OldLineNumber = 0,
                                NewLineNumber = i + 1,
                                IsDeleted = false,
                                IsAdded = true
                            });
                        }
                        else
                        {
                            // Modified line - show both
                            _diffLines.Add(new DiffLineData
                            {
                                Content = oldLine,
                                OldLineNumber = i + 1,
                                NewLineNumber = 0,
                                IsDeleted = true,
                                IsAdded = false
                            });
                            _diffLines.Add(new DiffLineData
                            {
                                Content = newLine,
                                OldLineNumber = 0,
                                NewLineNumber = i + 1,
                                IsDeleted = false,
                                IsAdded = true
                            });
                        }
                    }
                    else
                    {
                        // Unchanged line
                        _diffLines.Add(new DiffLineData
                        {
                            Content = newLine,
                            OldLineNumber = i + 1,
                            NewLineNumber = i + 1,
                            IsDeleted = false,
                            IsAdded = false
                        });
                    }
                }
                
                // Update list view
                _diffListView.itemsSource = _diffLines;
                _diffListView.Rebuild();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Error loading diff: {ex.Message}");
                _diffLines.Clear();
                _diffLines.Add(new DiffLineData
                {
                    Content = $"Error loading diff: {ex.Message}",
                    OldLineNumber = 0,
                    NewLineNumber = 0,
                    IsDeleted = false,
                    IsAdded = false,
                    IsError = true
                });
                _diffListView.itemsSource = _diffLines;
                _diffListView.Rebuild();
            }
        }
        
        private VisualElement MakeDiffLineItem()
        {
            return new VisualElement();
        }
        
        private void BindDiffLineItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _diffLines.Count)
                return;
                
            var lineData = _diffLines[index];
            element.Clear();
            
            var line = CreateDiffLineSimple(
                lineData.Content,
                lineData.OldLineNumber,
                lineData.NewLineNumber,
                lineData.IsDeleted,
                lineData.IsAdded,
                lineData.IsError
            );
            
            element.Add(line);
        }
        
        private VisualElement CreateDiffLineSimple(string content, int oldLineNum, int newLineNum, bool isDeleted, bool isAdded, bool isError = false)
        {
            var container = new VisualElement();
            container.AddToClassList("pg-diff-line");
            
            if (isError)
            {
                container.style.backgroundColor = new Color(0.3f, 0.0f, 0.0f, 0.3f);
            }
            else if (isAdded)
            {
                container.AddToClassList("pg-diff-line-added");
            }
            else if (isDeleted)
            {
                container.AddToClassList("pg-diff-line-deleted");
            }
            
            // Line numbers
            var lineNumberContainer = new VisualElement();
            lineNumberContainer.style.width = 100;
            lineNumberContainer.style.flexDirection = FlexDirection.Row;
            lineNumberContainer.style.marginRight = 12;
            
            var oldLineLabel = new Label(oldLineNum > 0 ? oldLineNum.ToString() : "");
            oldLineLabel.style.width = 45;
            oldLineLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            oldLineLabel.AddToClassList("pg-diff-line-number");
            lineNumberContainer.Add(oldLineLabel);
            
            var newLineLabel = new Label(newLineNum > 0 ? newLineNum.ToString() : "");
            newLineLabel.style.width = 45;
            newLineLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            newLineLabel.AddToClassList("pg-diff-line-number");
            lineNumberContainer.Add(newLineLabel);
            
            container.Add(lineNumberContainer);
            
            // Content with prefix
            string prefix = isAdded ? "+" : isDeleted ? "-" : " ";
            var contentLabel = new Label($"{prefix} {content}");
            contentLabel.AddToClassList("pg-diff-line-content");
            contentLabel.style.whiteSpace = WhiteSpace.Normal;
            contentLabel.style.fontSize = 11;
            if (isError)
            {
                contentLabel.style.color = new Color(0.89f, 0.29f, 0.29f);
            }
            container.Add(contentLabel);
            
            return container;
        }
        
        private class DiffLineData
        {
            public string Content { get; set; }
            public int OldLineNumber { get; set; }
            public int NewLineNumber { get; set; }
            public bool IsDeleted { get; set; }
            public bool IsAdded { get; set; }
            public bool IsError { get; set; }
        }
    }
}

