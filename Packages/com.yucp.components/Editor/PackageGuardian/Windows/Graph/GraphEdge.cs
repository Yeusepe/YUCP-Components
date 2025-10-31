namespace YUCP.Components.PackageGuardian.Editor.Windows.Graph
{
    /// <summary>
    /// Represents an edge (connection) in the commit graph.
    /// </summary>
    public class GraphEdge
    {
        public string SourceCommitId { get; set; }
        public string TargetCommitId { get; set; }
        public int SourceLane { get; set; }
        public int TargetLane { get; set; }
        public bool IsDashed { get; set; } // For stashes
        
        public GraphEdge(string source, string target, int sourceLane, int targetLane, bool isDashed = false)
        {
            SourceCommitId = source;
            TargetCommitId = target;
            SourceLane = sourceLane;
            TargetLane = targetLane;
            IsDashed = isDashed;
        }
    }
}

