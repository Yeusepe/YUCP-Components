using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEditor;

namespace YUCP.Components.PackageGuardian.Editor.Windows.Graph
{
    /// <summary>
    /// Main graph view with commit visualization - GitHub/GitKraken style timeline.
    /// </summary>
    public class GraphView : VisualElement
    {
        private GraphViewModel _viewModel;
        private ScrollView _scrollView;
        private VisualElement _graphContainer;
        private Dictionary<string, GraphRow> _rows;
        private string _selectedCommitId;
        
        public System.Action<GraphNode> OnCommitSelected { get; set; }
        
        public GraphView()
        {
            style.flexGrow = 1;
            _rows = new Dictionary<string, GraphRow>();
            _viewModel = new GraphViewModel();
            
            CreateUI();
        }
        
        private void CreateUI()
        {
            // Graph scroll view (no toolbar, it's in the top bar now)
            _scrollView = new ScrollView();
            _scrollView.AddToClassList("pg-scrollview");
            _scrollView.style.flexGrow = 1;
            
            _graphContainer = new VisualElement();
            _graphContainer.style.paddingTop = 8;
            _graphContainer.style.paddingBottom = 8;
            _scrollView.Add(_graphContainer);
            
            Add(_scrollView);
        }
        
        public void Refresh()
        {
            _viewModel.Load();
            RenderGraph();
        }
        
        private void RenderGraph()
        {
            _graphContainer.Clear();
            _rows.Clear();
            
            if (_viewModel.Nodes.Count == 0)
            {
                var emptyState = new VisualElement();
                emptyState.AddToClassList("pg-empty-state");
                
                var emptyIcon = new Label("📝");
                emptyIcon.AddToClassList("pg-empty-state-icon");
                emptyState.Add(emptyIcon);
                
                var emptyTitle = new Label("No Commits Yet");
                emptyTitle.AddToClassList("pg-empty-state-title");
                emptyState.Add(emptyTitle);
                
                var emptyDesc = new Label("Create your first snapshot to begin tracking changes");
                emptyDesc.AddToClassList("pg-empty-state-description");
                emptyState.Add(emptyDesc);
                
                _graphContainer.Add(emptyState);
                return;
            }
            
            // Render each commit row
            foreach (var node in _viewModel.Nodes)
            {
                var row = new GraphRow(node, OnRowSelected);
                _rows[node.CommitId] = row;
                
                // Set initial selection state
                if (node.CommitId == _selectedCommitId)
                {
                    row.SetSelected(true);
                }
                
                _graphContainer.Add(row);
            }
        }
        
        private void OnRowSelected(GraphNode node)
        {
            // Clear previous selection
            if (!string.IsNullOrEmpty(_selectedCommitId) && _rows.ContainsKey(_selectedCommitId))
            {
                _rows[_selectedCommitId].SetSelected(false);
            }
            
            // Set new selection
            _selectedCommitId = node.CommitId;
            _viewModel.SelectedCommitId = node.CommitId;
            
            if (_rows.ContainsKey(node.CommitId))
            {
                _rows[node.CommitId].SetSelected(true);
            }
            
            // Notify listener
            OnCommitSelected?.Invoke(node);
        }
    }
}
