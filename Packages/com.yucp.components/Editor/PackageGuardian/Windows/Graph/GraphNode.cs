using System.Collections.Generic;

namespace YUCP.Components.PackageGuardian.Editor.Windows.Graph
{
    /// <summary>
    /// Represents a node in the commit graph.
    /// </summary>
    public class GraphNode
    {
        public string CommitId { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public long Timestamp { get; set; }
        public List<string> Parents { get; set; }
        public List<string> Children { get; set; }
        public int Row { get; set; }
        public int Lane { get; set; }
        public bool IsStash { get; set; }
        public List<string> Refs { get; set; } // Branch names, tags
        
        public int ParentCount => Parents?.Count ?? 0;
        
        public GraphNode()
        {
            Parents = new List<string>();
            Children = new List<string>();
            Refs = new List<string>();
        }
    }
}

