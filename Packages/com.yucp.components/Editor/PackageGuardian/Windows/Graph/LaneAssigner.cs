using System.Collections.Generic;
using System.Linq;

namespace YUCP.Components.PackageGuardian.Editor.Windows.Graph
{
    /// <summary>
    /// Assigns lanes to commits to minimize crossings in the graph.
    /// </summary>
    public static class LaneAssigner
    {
        /// <summary>
        /// Assign lanes to nodes using a greedy topological approach.
        /// </summary>
        public static void AssignLanes(List<GraphNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return;
            
            // Track which lanes are occupied at each row
            var activeLanes = new Dictionary<int, string>(); // lane -> commit id currently using it
            var commitToLane = new Dictionary<string, int>();
            
            // Process nodes in order (assumed already sorted topologically)
            foreach (var node in nodes)
            {
                int assignedLane = -1;
                
                // Try to reuse parent's lane if available
                if (node.Parents.Count > 0)
                {
                    string firstParent = node.Parents[0];
                    if (commitToLane.TryGetValue(firstParent, out int parentLane))
                    {
                        // Check if parent's lane is free
                        if (!activeLanes.ContainsKey(parentLane) || activeLanes[parentLane] == firstParent)
                        {
                            assignedLane = parentLane;
                        }
                    }
                }
                
                // If couldn't reuse parent's lane, find first free lane
                if (assignedLane == -1)
                {
                    assignedLane = FindFreeLane(activeLanes);
                }
                
                node.Lane = assignedLane;
                commitToLane[node.CommitId] = assignedLane;
                activeLanes[assignedLane] = node.CommitId;
                
                // Free up lanes for commits that are no longer needed
                // (commits with no more children to process)
                CleanupLanes(activeLanes, node, nodes);
            }
        }
        
        private static int FindFreeLane(Dictionary<int, string> activeLanes)
        {
            // Find the smallest non-negative integer not in active lanes
            int lane = 0;
            while (activeLanes.ContainsKey(lane))
            {
                lane++;
            }
            return lane;
        }
        
        private static void CleanupLanes(Dictionary<int, string> activeLanes, GraphNode currentNode, List<GraphNode> allNodes)
        {
            // For each parent, check if all children have been processed
            foreach (var parentId in currentNode.Parents)
            {
                var parent = allNodes.FirstOrDefault(n => n.CommitId == parentId);
                if (parent == null)
                    continue;
                
                // Check if all children of this parent have been processed (have rows assigned)
                bool allChildrenProcessed = parent.Children.All(childId =>
                {
                    var child = allNodes.FirstOrDefault(n => n.CommitId == childId);
                    return child != null && child.Row <= currentNode.Row;
                });
                
                if (allChildrenProcessed)
                {
                    // Free up the parent's lane
                    var laneToFree = activeLanes.FirstOrDefault(kvp => kvp.Value == parentId).Key;
                    activeLanes.Remove(laneToFree);
                }
            }
        }
    }
}

