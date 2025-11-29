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
    /// Coordinates multiple AutoBodyHider components that target the same body mesh.
    /// Assigns unique UDIM tiles to each clothing piece and detects overlaps for layered clothing support.
    /// </summary>
    public class AutoBodyHiderCoordinator : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 99; // Run BEFORE AutoBodyHiderProcessor

        // Share overlap data with the processor
        public static Dictionary<SkinnedMeshRenderer, BodyMeshGroup> CoordinatedGroups = new Dictionary<SkinnedMeshRenderer, BodyMeshGroup>();

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
            
            // Group components by target body mesh
            Dictionary<SkinnedMeshRenderer, BodyMeshGroup> bodyMeshGroups = new Dictionary<SkinnedMeshRenderer, BodyMeshGroup>();
            
            // Collect all components that will use UDIM discard (including AutoDetect that resolves to UDIMDiscard)
            foreach (var data in allComponents)
            {
                if (data.targetBodyMesh == null) continue;
                
                // Determine application mode (handles AutoDetect by checking materials)
                ApplicationMode mode = DetermineApplicationMode(data);
                
                // Coordinate all components that will use UDIM discard
                // This automatically includes multiple meshes/components in AutoDetect mode
                if (mode == ApplicationMode.UDIMDiscard)
                {
                    if (!bodyMeshGroups.ContainsKey(data.targetBodyMesh))
                    {
                        bodyMeshGroups[data.targetBodyMesh] = new BodyMeshGroup { bodyMesh = data.targetBodyMesh };
                    }
                    
                    // Add if not already in the list to avoid duplicates
                    if (!bodyMeshGroups[data.targetBodyMesh].clothingPieces.Contains(data))
                    {
                        bodyMeshGroups[data.targetBodyMesh].clothingPieces.Add(data);
                    }
                }
            }
            
            // Assign unique UDIM tiles to each clothing piece in each group
            // Multiple meshes/components automatically use the orchestrator
            foreach (var group in bodyMeshGroups.Values)
            {
                if (group.clothingPieces.Count > 0)
                {
                    // Coordinate when there are components
                    AssignUDIMTiles(group);
                }
            }
            
            // Store groups for processor to access
            CoordinatedGroups = bodyMeshGroups;
            
            return true;
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
                if (UDIMManipulator.IsPoiyomiWithUDIMSupport(material))
                {
                    return ApplicationMode.UDIMDiscard;
                }
            }
            
            return ApplicationMode.MeshDeletion;
        }
        
        private void AssignUDIMTiles(BodyMeshGroup group)
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
                if (data.autoAssignUDIMTile)
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
                var tile = (data.udimDiscardRow, data.udimDiscardColumn);
                
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
            // Start from (1, 0) to avoid common texture tiles (0,0) through (0,3)
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
                            Debug.LogError($"[AutoBodyHiderCoordinator] Cannot assign tile to '{data.name}' - maximum 16 UDIM tiles exceeded!", data);
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
                data.udimDiscardRow = nextRow;
                data.udimDiscardColumn = nextCol;
                
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
            
            string message = $"UDIM Tile Limit Exceeded!\n\n" +
                           $"Body Mesh: {bodyMeshName}\n" +
                           $"Total clothing pieces: {totalAttempted}\n" +
                           $"Maximum allowed: 16 UDIM tiles\n" +
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
                      $"Note: Poiyomi and FastFur shaders support a maximum of 16 UDIM discard tiles (4x4 grid).";
            
            EditorUtility.DisplayDialog(
                "⚠️ UDIM Tile Limit Exceeded",
                message,
                "OK"
            );
            
            Debug.LogWarning($"[AutoBodyHiderCoordinator] {skippedPieces.Count} clothing pieces skipped due to UDIM tile limit!");
        }
    }
}

