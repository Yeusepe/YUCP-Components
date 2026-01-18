using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Coordinates multiple AutoBodyHider and AutoUVDiscard components that target the same body mesh.
    /// Assigns unique UV tiles to each clothing piece and detects overlaps for layered clothing support.
    /// </summary>
    public class AutoBodyHiderCoordinator : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 99; // Run BEFORE AutoBodyHiderProcessor

        // Share overlap data with the processor
        public static Dictionary<SkinnedMeshRenderer, BodyMeshGroup> CoordinatedGroups = new Dictionary<SkinnedMeshRenderer, BodyMeshGroup>();
        
        // Share tile assignment data for AutoUVDiscard
        public static Dictionary<SkinnedMeshRenderer, UVDiscardGroup> UVDiscardGroups = new Dictionary<SkinnedMeshRenderer, UVDiscardGroup>();

        public class BodyMeshGroup
        {
            public SkinnedMeshRenderer bodyMesh;
            public List<AutoBodyHiderData> clothingPieces = new List<AutoBodyHiderData>();
            public Dictionary<AutoBodyHiderData, (int row, int col)> assignedTiles = new Dictionary<AutoBodyHiderData, (int, int)>();
            
            // Overlap tracking for layered clothing (filled by processor after detection)
            public List<OverlapRegion> overlapRegions = new List<OverlapRegion>();
            public Dictionary<OverlapRegion, (int row, int col)> overlapTiles = new Dictionary<OverlapRegion, (int, int)>();
            
            // Tile allocation state for processor
            public int nextAvailableRow = 1;
            public int nextAvailableCol = 0;
            public HashSet<(int, int)> usedTiles = new HashSet<(int, int)>();
        }
        
        public class UVDiscardGroup
        {
            public SkinnedMeshRenderer bodyMesh;
            public List<AutoUVDiscardData> components = new List<AutoUVDiscardData>();
            public Dictionary<AutoUVDiscardData, List<(int row, int col)>> assignedTiles = new Dictionary<AutoUVDiscardData, List<(int row, int col)>>();
            public HashSet<(int, int)> usedTiles = new HashSet<(int, int)>();
        }
        
        /// <summary>
        /// Represents a region where multiple clothing pieces overlap
        /// </summary>
        public class OverlapRegion
        {
            public List<AutoBodyHiderData> involvedClothing = new List<AutoBodyHiderData>();
            public string regionName;
            
            public OverlapRegion(List<AutoBodyHiderData> clothing)
            {
                involvedClothing = clothing;
                regionName = string.Join("+", clothing.Select(c => c.name));
            }
            
            public override bool Equals(object obj)
            {
                if (obj is OverlapRegion other)
                {
                    if (involvedClothing.Count != other.involvedClothing.Count) return false;
                    var sorted1 = involvedClothing.OrderBy(c => c.GetInstanceID()).ToList();
                    var sorted2 = other.involvedClothing.OrderBy(c => c.GetInstanceID()).ToList();
                    return sorted1.SequenceEqual(sorted2);
                }
                return false;
            }
            
            public override int GetHashCode()
            {
                int hash = 17;
                foreach (var c in involvedClothing.OrderBy(c => c.GetInstanceID()))
                {
                    hash = hash * 31 + c.GetInstanceID();
                }
                return hash;
            }
        }

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var allComponents = avatarRoot.GetComponentsInChildren<AutoBodyHiderData>(true);
            var allUVDiscardComponents = avatarRoot.GetComponentsInChildren<AutoUVDiscardData>(true);
            
            // Group components by target body mesh
            Dictionary<SkinnedMeshRenderer, BodyMeshGroup> bodyMeshGroups = new Dictionary<SkinnedMeshRenderer, BodyMeshGroup>();
            Dictionary<SkinnedMeshRenderer, UVDiscardGroup> uvDiscardGroups = new Dictionary<SkinnedMeshRenderer, UVDiscardGroup>();
            
            // Collect all AutoBodyHider components that will use UV discard
            foreach (var data in allComponents)
            {
                if (data.targetBodyMesh == null) continue;
                
                ApplicationMode mode = DetermineApplicationMode(data);
                
                if (mode == ApplicationMode.UVDiscard)
                {
                    if (!bodyMeshGroups.ContainsKey(data.targetBodyMesh))
                    {
                        bodyMeshGroups[data.targetBodyMesh] = new BodyMeshGroup { bodyMesh = data.targetBodyMesh };
                    }
                    
                    if (!bodyMeshGroups[data.targetBodyMesh].clothingPieces.Contains(data))
                    {
                        bodyMeshGroups[data.targetBodyMesh].clothingPieces.Add(data);
                    }
                }
            }
            
            // Collect all AutoUVDiscard components
            foreach (var data in allUVDiscardComponents)
            {
                if (data.targetBodyMesh == null || !data.enabled) continue;
                
                if (!uvDiscardGroups.ContainsKey(data.targetBodyMesh))
                {
                    uvDiscardGroups[data.targetBodyMesh] = new UVDiscardGroup { bodyMesh = data.targetBodyMesh };
                }
                
                if (!uvDiscardGroups[data.targetBodyMesh].components.Contains(data))
                {
                    uvDiscardGroups[data.targetBodyMesh].components.Add(data);
                }
            }
            
            // Assign unique UV tiles to each clothing piece in each group
            foreach (var group in bodyMeshGroups.Values)
            {
                if (group.clothingPieces.Count > 0)
                {
                    AssignUVTiles(group);
                }
            }
            
            // Assign UV tiles for AutoUVDiscard components
            foreach (var group in uvDiscardGroups.Values)
            {
                if (group.components.Count > 0)
                {
                    AssignUVDiscardTiles(group);
                }
            }
            
            // Store groups for processors to access
            CoordinatedGroups = bodyMeshGroups;
            UVDiscardGroups = uvDiscardGroups;
            
            return true;
        }
        
        private void AssignUVDiscardTiles(UVDiscardGroup group)
        {
            Debug.Log($"[AutoBodyHiderCoordinator] Coordinating {group.components.Count} AutoUVDiscard components for body mesh '{group.bodyMesh.name}'");
            
            HashSet<(int, int)> usedTiles = new HashSet<(int, int)>();
            
            // Collect tiles from AutoBodyHider components on the same body mesh
            if (CoordinatedGroups.ContainsKey(group.bodyMesh))
            {
                var bodyGroup = CoordinatedGroups[group.bodyMesh];
                foreach (var tile in bodyGroup.usedTiles)
                {
                    usedTiles.Add(tile);
                }
            }
            
            // Process each AutoUVDiscard component
            foreach (var data in group.components)
            {
                if (data.autoAssignUVTile)
                {
                    // Auto-assign tiles for regions
                    int nextRow = 1;
                    int nextCol = 0;
                    List<(int row, int col)> assignedTiles = new List<(int row, int col)>();
                    
                    // Estimate number of regions (will be refined during processing)
                    int estimatedRegions = 4;
                    
                    for (int i = 0; i < estimatedRegions; i++)
                    {
                        while (usedTiles.Contains((nextRow, nextCol)))
                        {
                            nextCol++;
                            if (nextCol >= 4)
                            {
                                nextCol = 0;
                                nextRow++;
                                if (nextRow >= 4)
                                {
                                    Debug.LogWarning($"[AutoBodyHiderCoordinator] Ran out of tiles for AutoUVDiscard '{data.name}'", data);
                                    break;
                                }
                            }
                        }
                        
                        if (nextRow >= 4) break;
                        
                        var tile = (nextRow, nextCol);
                        usedTiles.Add(tile);
                        assignedTiles.Add(tile);
                        
                        nextCol++;
                        if (nextCol >= 4)
                        {
                            nextCol = 0;
                            nextRow++;
                        }
                    }
                    
                    if (assignedTiles.Count > 0)
                    {
                        group.assignedTiles[data] = assignedTiles;
                        data.startRow = assignedTiles[0].row;
                        data.startColumn = assignedTiles[0].col;
                        Debug.Log($"[AutoBodyHiderCoordinator] Auto-assigned starting tile ({assignedTiles[0].row}, {assignedTiles[0].col}) to AutoUVDiscard '{data.name}'");
                    }
                }
                else
                {
                    // Manual assignment
                    if (data.startRow >= 0 && data.startColumn >= 0)
                    {
                        var tile = (data.startRow, data.startColumn);
                        if (!usedTiles.Contains(tile))
                        {
                            usedTiles.Add(tile);
                            group.assignedTiles[data] = new List<(int row, int col)> { tile };
                        }
                        else
                        {
                            Debug.LogWarning($"[AutoBodyHiderCoordinator] AutoUVDiscard '{data.name}' wants tile ({tile.Item1}, {tile.Item2}) but it's already used.", data);
                        }
                    }
                }
            }
            
            group.usedTiles = usedTiles;
        }
        
        private ApplicationMode DetermineApplicationMode(AutoBodyHiderData data)
        {
            if (data.applicationMode != ApplicationMode.AutoDetect)
            {
                return data.applicationMode;
            }
            
            Material[] materials = data.targetBodyMesh.sharedMaterials;
            foreach (var material in materials)
            {
                if (UVManipulator.IsPoiyomiWithUVSupport(material))
                {
                    return ApplicationMode.UVDiscard;
                }
            }
            
            return ApplicationMode.MeshDeletion;
        }
        
        private void AssignUVTiles(BodyMeshGroup group)
        {
            Debug.Log($"[AutoBodyHiderCoordinator] Coordinating {group.clothingPieces.Count} clothing pieces for body mesh '{group.bodyMesh.name}'");
            
            // Step 1: Assign individual tiles to each clothing piece
            HashSet<(int, int)> usedTiles = new HashSet<(int, int)>();
            List<AutoBodyHiderData> needsAssignment = new List<AutoBodyHiderData>();
            List<AutoBodyHiderData> skippedPieces = new List<AutoBodyHiderData>();
            
            // Separate clothing pieces into auto-assign and manual-assign groups
            List<AutoBodyHiderData> autoAssignPieces = new List<AutoBodyHiderData>();
            List<AutoBodyHiderData> manualAssignPieces = new List<AutoBodyHiderData>();
            
            foreach (var data in group.clothingPieces)
            {
                if (data.autoAssignUVTile)
                {
                    autoAssignPieces.Add(data);
                }
                else
                {
                    manualAssignPieces.Add(data);
                }
            }
            
            // Collect manually-specified tiles when not auto-assigning
            foreach (var data in manualAssignPieces)
            {
                var tile = (data.uvDiscardRow, data.uvDiscardColumn);
                
                if (usedTiles.Contains(tile))
                {
                    Debug.LogWarning($"[AutoBodyHiderCoordinator] Clothing '{data.name}' wants tile ({tile.Item1}, {tile.Item2}) but it's already used. Will auto-assign instead.", data);
                    autoAssignPieces.Add(data); // Move to auto-assign if conflict
                }
                else
                {
                    usedTiles.Add(tile);
                    group.assignedTiles[data] = tile;
                    Debug.Log($"[AutoBodyHiderCoordinator] Clothing '{data.name}' using manually-specified tile ({tile.Item1}, {tile.Item2})");
                }
            }
            
            // Auto-assign tiles for all pieces that need it
            // Start from (1, 0)
            int nextRow = 1;
            int nextCol = 0;
            
            foreach (var data in autoAssignPieces)
            {
                // Find next available tile
                while (usedTiles.Contains((nextRow, nextCol)))
                {
                    nextCol++;
                    if (nextCol >= 4)
                    {
                        nextCol = 0;
                        nextRow++;
                        if (nextRow >= 4)
                        {
                            skippedPieces.Add(data);
                            Debug.LogError($"[AutoBodyHiderCoordinator] Cannot assign tile to '{data.name}' - maximum 16 UV tiles exceeded!", data);
                            break;
                        }
                    }
                }
                
                if (nextRow >= 4) continue;
                
                // Assign the tile
                var tile = (nextRow, nextCol);
                usedTiles.Add(tile);
                group.assignedTiles[data] = tile;
                
                // Update the data component with the assigned tile
                data.uvDiscardRow = nextRow;
                data.uvDiscardColumn = nextCol;
                
                Debug.Log($"[AutoBodyHiderCoordinator] Auto-assigned tile ({nextRow}, {nextCol}) to clothing '{data.name}'");
                
                // Move to next tile for next assignment
                nextCol++;
                if (nextCol >= 4)
                {
                    nextCol = 0;
                    nextRow++;
                }
            }
            
            // Step 2: Reserve tiles for potential overlaps (actual detection happens in processor)
            var validPieces = group.clothingPieces.Where(p => group.assignedTiles.ContainsKey(p)).ToList();
            
            if (validPieces.Count >= 2)
            {
                Debug.Log($"[AutoBodyHiderCoordinator] {validPieces.Count} clothing pieces detected. Overlap detection will happen during processing...");
                // Store available tile range for processor to use
                group.nextAvailableRow = nextRow;
                group.nextAvailableCol = nextCol;
                group.usedTiles = usedTiles;
            }
            
            // Show warnings
            if (skippedPieces.Count > 0)
            {
                ShowTileLimitWarning(group, skippedPieces);
            }
            
            Debug.Log($"[AutoBodyHiderCoordinator] Tile assignment complete. " +
                     $"{group.assignedTiles.Count} individual tiles, " +
                     $"{group.overlapRegions.Count} overlap tiles assigned.");
        }
        
        private void ShowTileLimitWarning(BodyMeshGroup group, List<AutoBodyHiderData> skippedPieces)
        {
            string bodyMeshName = group.bodyMesh != null ? group.bodyMesh.name : "Unknown";
            int totalAttempted = group.assignedTiles.Count + skippedPieces.Count;
            
            string message = $"UV Tile Limit Exceeded!\n\n" +
                           $"Body Mesh: {bodyMeshName}\n" +
                           $"Total clothing pieces: {totalAttempted}\n" +
                           $"Maximum allowed: 16 UV tiles\n" +
                           $"Processed: {group.assignedTiles.Count}\n" +
                           $"Skipped: {skippedPieces.Count}\n\n" +
                           $"The following clothing pieces were SKIPPED and will NOT hide body parts:\n\n";
            
            foreach (var piece in skippedPieces)
            {
                message += $"  • {piece.name} (on '{piece.gameObject.name}')\n";
            }
            
            message += $"\n\nHow to fix this:\n" +
                      $"1. Remove some clothing pieces from this body mesh\n" +
                      $"2. Combine clothing pieces that are always worn together\n" +
                      $"3. Use Mesh Deletion mode for some pieces (permanent)\n" +
                      $"4. Split clothing across multiple body renderers\n\n" +
                      $"Note: Poiyomi and FastFur shaders support a maximum of 16 UV discard tiles (4x4 grid).";
            
            EditorUtility.DisplayDialog(
                "⚠️ UV Tile Limit Exceeded",
                message,
                "OK"
            );
            
            Debug.LogWarning($"[AutoBodyHiderCoordinator] {skippedPieces.Count} clothing pieces skipped due to UV tile limit!");
        }
    }
}

