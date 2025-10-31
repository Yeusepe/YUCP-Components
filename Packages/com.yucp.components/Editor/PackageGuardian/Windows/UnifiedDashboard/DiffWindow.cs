using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Controls;

namespace YUCP.Components.PackageGuardian.Editor.Windows.UnifiedDashboard
{
    /// <summary>
    /// Window for viewing file diffs.
    /// </summary>
    public class DiffWindow : EditorWindow
    {
        private global::PackageGuardian.Core.Diff.FileChange _change;
        private string _commitId;
        private ScrollView _diffView;
        
        public static void ShowDiff(global::PackageGuardian.Core.Diff.FileChange change, string commitId)
        {
            // Use enhanced diff viewer instead
            EnhancedDiffViewer.ShowWindow(change, commitId);
        }
        
        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.backgroundColor = new StyleColor(new Color(0.035f, 0.035f, 0.035f)); // #090909
            
            // Load stylesheet
            var mainStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.yucp.components/Editor/PackageGuardian/Styles/PackageGuardian.uss");
            if (mainStyleSheet != null)
                root.styleSheets.Add(mainStyleSheet);
            
            // Header
            var header = new VisualElement();
            header.AddToClassList("pg-header");
            
            var title = new Label(_change != null ? $"Diff: {_change.Path}" : "File Diff");
            title.AddToClassList("pg-title");
            header.Add(title);
            
            root.Add(header);
            
            // Diff view
            _diffView = new ScrollView();
            _diffView.style.flexGrow = 1;
            _diffView.style.paddingTop = 10;
            _diffView.style.paddingBottom = 10;
            _diffView.style.paddingLeft = 20;
            _diffView.style.paddingRight = 20;
            root.Add(_diffView);
        }
        
        private void LoadDiff()
        {
            if (_diffView == null || _change == null)
                return;
            
            _diffView.Clear();
            
            try
            {
                var service = Services.RepositoryService.Instance;
                var repo = service.Repository;
                
                if (repo == null)
                {
                    _diffView.Add(PgLabel.Create("Repository not initialized", true));
                    return;
                }
                
                // Check if binary
                if (IsBinaryFile(_change.Path))
                {
                    _diffView.Add(PgLabel.Create("Binary file - diff not available", true));
                    return;
                }
                
                // Generate diff
                var diffEngine = new global::PackageGuardian.Core.Diff.DiffEngine(repo.Store);
                var lineDiffs = diffEngine.DiffTextFiles(_change.OldOid, _change.NewOid);
                
                if (lineDiffs.Count == 0)
                {
                    _diffView.Add(PgLabel.Create("No changes", true));
                    return;
                }
                
                // Render diff
                foreach (var line in lineDiffs)
                {
                    var lineElement = CreateDiffLine(line);
                    _diffView.Add(lineElement);
                }
            }
            catch (Exception ex)
            {
                _diffView.Add(PgLabel.Create($"Error loading diff: {ex.Message}", true));
            }
        }
        
        private VisualElement CreateDiffLine(global::PackageGuardian.Core.Diff.LineDiff line)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.paddingTop = 2;
            container.style.paddingBottom = 2;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;
            
            // Background color based on type
            Color bgColor;
            Color textColor = Color.white;
            string prefix = " ";
            
            switch (line.Type)
            {
                case global::PackageGuardian.Core.Diff.DiffLineType.Added:
                    bgColor = new Color(0.0f, 0.3f, 0.2f, 0.3f); // Dark green
                    textColor = new Color(0.21f, 0.75f, 0.69f); // #36BFB1
                    prefix = "+";
                    break;
                case global::PackageGuardian.Core.Diff.DiffLineType.Deleted:
                    bgColor = new Color(0.3f, 0.0f, 0.0f, 0.3f); // Dark red
                    textColor = new Color(0.89f, 0.29f, 0.29f); // #E24A4A
                    prefix = "-";
                    break;
                default:
                    bgColor = Color.clear;
                    textColor = new Color(0.8f, 0.8f, 0.8f);
                    prefix = " ";
                    break;
            }
            
            container.style.backgroundColor = new StyleColor(bgColor);
            
            // Line numbers
            var lineNumLabel = new Label();
            lineNumLabel.style.width = 80;
            lineNumLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            lineNumLabel.style.unityFontStyleAndWeight = FontStyle.Normal;
            
            if (line.Type == global::PackageGuardian.Core.Diff.DiffLineType.Added)
            {
                lineNumLabel.text = $"    {line.NewLineNumber}";
            }
            else if (line.Type == global::PackageGuardian.Core.Diff.DiffLineType.Deleted)
            {
                lineNumLabel.text = $"{line.OldLineNumber}    ";
            }
            else
            {
                lineNumLabel.text = $"{line.OldLineNumber,4} {line.NewLineNumber,4}";
            }
            
            container.Add(lineNumLabel);
            
            // Content with prefix
            var contentLabel = new Label($"{prefix} {line.Content}");
            contentLabel.style.flexGrow = 1;
            contentLabel.style.color = new StyleColor(textColor);
            contentLabel.style.whiteSpace = WhiteSpace.Normal;
            contentLabel.style.unityFontStyleAndWeight = FontStyle.Normal;
            
            // Use monospace font if available
            container.Add(contentLabel);
            
            return container;
        }
        
        private bool IsBinaryFile(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || 
                   ext == ".gif" || ext == ".psd" || ext == ".fbx" || 
                   ext == ".obj" || ext == ".dll" || ext == ".so" || 
                   ext == ".dylib" || ext == ".exe" || ext == ".unitypackage";
        }
    }
}

