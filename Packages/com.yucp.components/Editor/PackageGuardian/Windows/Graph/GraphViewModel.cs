using System;
using System.Collections.Generic;
using System.Linq;
using PackageGuardian.Core.Objects;
using YUCP.Components.PackageGuardian.Editor.Services;

namespace YUCP.Components.PackageGuardian.Editor.Windows.Graph
{
    /// <summary>
    /// View model for the commit graph.
    /// </summary>
    public class GraphViewModel
    {
        public List<GraphNode> Nodes { get; private set; }
        public List<GraphEdge> Edges { get; private set; }
        public string SelectedCommitId { get; set; }
        
        public GraphViewModel()
        {
            Nodes = new List<GraphNode>();
            Edges = new List<GraphEdge>();
        }
        
        /// <summary>
        /// Load graph data from repository.
        /// </summary>
        public void Load()
        {
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                var nodes = new Dictionary<string, GraphNode>();
                var visited = new HashSet<string>();
                
                // Start from HEAD and all branches
                var startPoints = new List<string>();
                
                // Add HEAD
                string headCommit = repo.Refs.ResolveHead();
                if (!string.IsNullOrWhiteSpace(headCommit))
                {
                    startPoints.Add(headCommit);
                }
                
                // Add all branch refs
                foreach (var refName in repo.Refs.ListRefs("refs/heads/"))
                {
                    string commitId = repo.Refs.ReadRef(refName);
                    if (!string.IsNullOrWhiteSpace(commitId))
                    {
                        startPoints.Add(commitId);
                    }
                }
                
                // Walk commit history
                var queue = new Queue<string>(startPoints);
                while (queue.Count > 0)
                {
                    string commitId = queue.Dequeue();
                    if (visited.Contains(commitId))
                        continue;
                    visited.Add(commitId);
                    
                    try
                    {
                        var commitObj = repo.Store.ReadObject(commitId);
                        if (commitObj is not Commit commit)
                            continue;
                        
                        var node = new GraphNode
                        {
                            CommitId = commitId,
                            Message = commit.Message,
                            Author = commit.Author,
                            Timestamp = commit.Timestamp,
                            IsStash = false
                        };
                        
                        // Add parents
                        foreach (var parentBytes in commit.Parents)
                        {
                            string parentId = BytesToHex(parentBytes);
                            node.Parents.Add(parentId);
                            queue.Enqueue(parentId);
                        }
                        
                        nodes[commitId] = node;
                    }
                    catch
                    {
                        // Skip invalid commits
                    }
                }
                
                // Build parent-child relationships
                foreach (var node in nodes.Values)
                {
                    foreach (var parentId in node.Parents)
                    {
                        if (nodes.TryGetValue(parentId, out var parent))
                        {
                            parent.Children.Add(node.CommitId);
                        }
                    }
                }
                
                // Add stashes
                var stashes = repo.Stash.List();
                foreach (var stash in stashes)
                {
                    if (!nodes.ContainsKey(stash.CommitId))
                    {
                        try
                        {
                            var commitObj = repo.Store.ReadObject(stash.CommitId);
                            if (commitObj is Commit commit)
                            {
                                var node = new GraphNode
                                {
                                    CommitId = stash.CommitId,
                                    Message = stash.Message,
                                    Author = "",
                                    Timestamp = stash.Timestamp,
                                    IsStash = true
                                };
                                
                                foreach (var parentBytes in commit.Parents)
                                {
                                    node.Parents.Add(BytesToHex(parentBytes));
                                }
                                
                                nodes[stash.CommitId] = node;
                            }
                        }
                        catch
                        {
                            // Skip invalid stashes
                        }
                    }
                }
                
                // Add refs to nodes
                AnnotateRefs(nodes, repo);
                
                // Sort topologically (timestamp-based for simplicity)
                var sortedNodes = nodes.Values.OrderByDescending(n => n.Timestamp).ToList();
                
                // Assign row numbers
                for (int i = 0; i < sortedNodes.Count; i++)
                {
                    sortedNodes[i].Row = i;
                }
                
                // Assign lanes
                LaneAssigner.AssignLanes(sortedNodes);
                
                // Build edges
                var edges = new List<GraphEdge>();
                foreach (var node in sortedNodes)
                {
                    foreach (var parentId in node.Parents)
                    {
                        var parent = sortedNodes.FirstOrDefault(n => n.CommitId == parentId);
                        if (parent != null)
                        {
                            edges.Add(new GraphEdge(
                                node.CommitId,
                                parentId,
                                node.Lane,
                                parent.Lane,
                                node.IsStash)); // Dashed for stashes
                        }
                    }
                }
                
                Nodes = sortedNodes;
                Edges = edges;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Package Guardian] Failed to load graph: {ex.Message}");
                Nodes = new List<GraphNode>();
                Edges = new List<GraphEdge>();
            }
        }
        
        private void AnnotateRefs(Dictionary<string, GraphNode> nodes, global::PackageGuardian.Core.Repository.Repository repo)
        {
            // Annotate branch refs
            foreach (var refName in repo.Refs.ListRefs("refs/heads/"))
            {
                string commitId = repo.Refs.ReadRef(refName);
                if (nodes.TryGetValue(commitId, out var node))
                {
                    string branchName = refName.Substring("refs/heads/".Length);
                    node.Refs.Add(branchName);
                }
            }
            
            // Annotate tags
            foreach (var refName in repo.Refs.ListRefs("refs/tags/"))
            {
                string commitId = repo.Refs.ReadRef(refName);
                if (nodes.TryGetValue(commitId, out var node))
                {
                    string tagName = refName.Substring("refs/tags/".Length);
                    node.Refs.Add($"tag: {tagName}");
                }
            }
        }
        
        private string BytesToHex(byte[] bytes)
        {
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }
    }
}

