using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDK3.Avatars.Components;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Auto UV Discard components during avatar build.
    /// Automatically detects UV regions and creates corresponding UV discards with toggles.
    /// </summary>
    public class AutoUVDiscardProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 101; // Run right after AutoBodyHider

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var components = avatarRoot.GetComponentsInChildren<AutoUVDiscardData>(true);

            foreach (var data in components)
            {
                if (data != null && data.enabled)
                {
                    ProcessAutoDiscard(data);
                }
            }

            return true;
        }

        private void ProcessAutoDiscard(AutoUVDiscardData data)
        {
            try
            {
                Debug.Log($"[AutoUVDiscard] Processing: {data.gameObject.name}", data);

                // Validate
                if (data.targetBodyMesh == null)
                {
                    Debug.LogError($"[AutoUVDiscard] Target body mesh not set!", data);
                    return;
                }

                var clothingRenderer = data.GetComponent<SkinnedMeshRenderer>();
                if (clothingRenderer == null || clothingRenderer.sharedMesh == null)
                {
                    Debug.LogError($"[AutoUVDiscard] No SkinnedMeshRenderer or mesh found!", data);
                    return;
                }

                // Find compatible materials (use targetMaterials if specified, otherwise auto-detect)
                List<Material> targetMaterials = new List<Material>();
                if (data.targetMaterials != null && data.targetMaterials.Length > 0)
                {
                    foreach (var mat in data.targetMaterials)
                    {
                        if (mat != null && UVManipulator.IsPoiyomiWithUVSupport(mat))
                        {
                            targetMaterials.Add(mat);
                        }
                    }
                }
                
                // If no target materials specified, auto-detect all compatible materials
                if (targetMaterials.Count == 0)
                {
                    if (data.targetBodyMesh != null && data.targetBodyMesh.sharedMaterials != null)
                    {
                        foreach (var mat in data.targetBodyMesh.sharedMaterials)
                        {
                            if (mat != null && UVManipulator.IsPoiyomiWithUVSupport(mat))
                            {
                                targetMaterials.Add(mat);
                            }
                        }
                    }
                }
                
                if (targetMaterials.Count == 0)
                {
                    Debug.LogError($"[AutoUVDiscard] Body mesh doesn't have a Poiyomi or FastFur material with UV support!", data);
                    return;
                }
                
                string shaderName = UVManipulator.GetShaderDisplayName(targetMaterials[0]);
                Debug.Log($"[AutoUVDiscard] Using {shaderName} shader for UV discard on {targetMaterials.Count} material(s)", data);

                // Detect UV regions from clothing mesh
                int effectiveUVChannel = data.autoDetectUVChannel 
                    ? UVManipulator.DetectBestUVChannel(clothingRenderer.sharedMesh)
                    : data.uvChannel;
                List<AutoUVDiscardData.UVRegion> regions = DetectUVRegions(clothingRenderer.sharedMesh, data, effectiveUVChannel);

                if (regions == null || regions.Count == 0)
                {
                    Debug.LogWarning($"[AutoUVDiscard] No UV regions detected!", data);
                    return;
                }

                Debug.Log($"[AutoUVDiscard] Detected {regions.Count} UV regions", data);

                // Assign UV tiles to each region
                AssignUVTiles(regions, data);

                // Process each region
                List<string> usedTiles = new List<string>();
                Mesh originalBodyMesh = data.targetBodyMesh.sharedMesh;
                
                for (int i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    region.name = $"Region {i + 1}";
                    
                    Debug.Log($"[AutoUVDiscard] Processing {region.name}: {region.vertexIndices.Count} vertices -> UV {region.assignedRow},{region.assignedColumn}", data);

                    // Create hidden vertices array for this region
                    bool[] hiddenVertices = new bool[clothingRenderer.sharedMesh.vertexCount];
                    foreach (int vertexIndex in region.vertexIndices)
                    {
                        if (vertexIndex < hiddenVertices.Length)
                            hiddenVertices[vertexIndex] = true;
                    }

                    // Apply UV discard for this region
                    Mesh modifiedMesh = UVManipulator.ApplyUVDiscard(
                        originalBodyMesh,
                        hiddenVertices,
                        region.assignedRow,
                        region.assignedColumn,
                        effectiveUVChannel
                    );

                    if (modifiedMesh != null)
                    {
                        data.targetBodyMesh.sharedMesh = modifiedMesh;
                        originalBodyMesh = modifiedMesh; // Use modified mesh for next iteration
                    }

                    // Configure all target materials for this tile
                    foreach (var material in targetMaterials)
                    {
                        ConfigurePoiyomiMaterial(material, region.assignedRow, region.assignedColumn, data, originalBodyMesh, effectiveUVChannel);
                    }

                    usedTiles.Add($"Row{region.assignedRow}_Col{region.assignedColumn}");

                    // Register global parameter for this region
                    string globalParamName = GetGlobalParameterName(data, i);
                    if (!string.IsNullOrEmpty(globalParamName))
                    {
                        RegisterGlobalParameter(data, globalParamName, targetMaterials[0], region);
                    }
                }

                // Store stats
                data.SetBuildStats(regions.Count, usedTiles);
                
                Debug.Log($"[AutoUVDiscard] Successfully processed {regions.Count} regions!", data);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoUVDiscard] Error processing: {ex.Message}", data);
                Debug.LogException(ex);
            }
        }

        private List<AutoUVDiscardData.UVRegion> DetectUVRegions(Mesh mesh, AutoUVDiscardData data, int uvChannel)
        {
            // Dispatch to mode-specific detection method
            return data.detectionMode switch
            {
                DetectionMode.UVProximity => DetectByUVProximity(mesh, data, uvChannel),
                DetectionMode.MaskTexture => DetectByMaskTexture(mesh, data),
                DetectionMode.UVSeams => DetectByUVSeams(mesh, data),
                DetectionMode.SharpEdges => DetectBySharpEdges(mesh, data),
                DetectionMode.BlenderVertexGroups => DetectByBlenderVertexGroups(mesh, data),
                DetectionMode.MaterialSlots => DetectByMaterialSlots(mesh, data),
                DetectionMode.BoneInfluence => DetectByBoneInfluence(mesh, data),
                _ => DetectByUVProximity(mesh, data, uvChannel)
            };
        }

        #region Detection Mode Implementations

        private List<AutoUVDiscardData.UVRegion> DetectByUVProximity(Mesh mesh, AutoUVDiscardData data, int uvChannel)
        {
            Vector2[] uvs = GetUVChannel(mesh, uvChannel);
            if (uvs == null || uvs.Length == 0)
            {
                Debug.LogError($"[AutoUVDiscard] No UV{uvChannel} data found on mesh!", data);
                return null;
            }

            // Group vertices by UV proximity
            List<List<int>> uvClusters = ClusterVerticesByUV(uvs, data.mergeTolerance);

            // Filter out small clusters
            int minVertices = Mathf.CeilToInt(mesh.vertexCount * (data.minRegionSize / 100f));
            uvClusters = uvClusters.Where(cluster => cluster.Count >= minVertices).ToList();

            return ConvertClustersToRegions(uvClusters, uvs, data);
        }

        private List<AutoUVDiscardData.UVRegion> DetectByMaskTexture(Mesh mesh, AutoUVDiscardData data)
        {
            if (data.maskRegions == null || data.maskRegions.Count == 0)
            {
                Debug.LogError("[AutoUVDiscard] No mask regions defined! Add at least one mask.", data);
                return null;
            }

            Vector2[] uvs = GetUVChannel(mesh, data.maskUVChannel);
            if (uvs == null || uvs.Length == 0)
            {
                Debug.LogError($"[AutoUVDiscard] No UV{data.maskUVChannel} data found on mesh for mask sampling!", data);
                return null;
            }

            var regions = new List<AutoUVDiscardData.UVRegion>();

            // Each mask in the list defines a separate region
            foreach (var maskDef in data.maskRegions)
            {
                if (!maskDef.enabled || maskDef.maskTexture == null)
                    continue;

                var vertexIndices = new List<int>();

                // Sample mask for each vertex
                for (int v = 0; v < uvs.Length; v++)
                {
                    float maskValue = UVManipulator.SampleMaskAtUV(maskDef.maskTexture, uvs[v]);
                    
                    if (maskValue >= data.maskThreshold)
                    {
                        vertexIndices.Add(v);
                    }
                }

                if (vertexIndices.Count > 0)
                {
                    var region = new AutoUVDiscardData.UVRegion
                    {
                        vertexIndices = vertexIndices,
                        name = maskDef.name,
                        debugColor = maskDef.debugColor
                    };
                    
                    CalculateRegionBounds(region, uvs);
                    regions.Add(region);
                }
            }

            if (regions.Count == 0)
            {
                Debug.LogWarning("[AutoUVDiscard] No regions detected from masks. Check mask textures and threshold.", data);
                return null;
            }

            Debug.Log($"[AutoUVDiscard] Detected {regions.Count} regions from {data.maskRegions.Count} masks", data);
            return regions;
        }

        private List<AutoUVDiscardData.UVRegion> DetectByUVSeams(Mesh mesh, AutoUVDiscardData data)
        {
            var seamEdges = UVManipulator.FindUVSeamEdges(mesh, data.seamUVChannel, data.seamThreshold);
            
            if (seamEdges.Count == 0)
            {
                Debug.LogWarning("[AutoUVDiscard] No UV seams detected!", data);
                return null;
            }

            Debug.Log($"[AutoUVDiscard] Detected {seamEdges.Count} seam edges", data);

            // Flood fill to create regions bounded by seams
            var clusters = UVManipulator.FloodFillRegionsFromEdges(mesh, seamEdges);
            
            // Filter out small clusters
            int minVertices = Mathf.CeilToInt(mesh.vertexCount * (data.minRegionSize / 100f));
            clusters = clusters.Where(c => c.Count >= minVertices).ToList();

            Vector2[] uvs = GetUVChannel(mesh, data.seamUVChannel);
            return ConvertClustersToRegions(clusters, uvs, data);
        }

        private List<AutoUVDiscardData.UVRegion> DetectBySharpEdges(Mesh mesh, AutoUVDiscardData data)
        {
            var sharpEdges = UVManipulator.FindSharpEdges(mesh, data.sharpAngleThreshold);
            
            if (sharpEdges.Count == 0)
            {
                Debug.LogWarning("[AutoUVDiscard] No sharp edges detected!", data);
                return null;
            }

            Debug.Log($"[AutoUVDiscard] Detected {sharpEdges.Count} sharp edges", data);

            // Flood fill to create regions bounded by sharp edges
            var clusters = UVManipulator.FloodFillRegionsFromEdges(mesh, sharpEdges);
            
            // Filter out small clusters
            int minVertices = Mathf.CeilToInt(mesh.vertexCount * (data.minRegionSize / 100f));
            clusters = clusters.Where(c => c.Count >= minVertices).ToList();

            Vector2[] uvs = GetUVChannel(mesh, 0);
            return ConvertClustersToRegions(clusters, uvs, data);
        }

        private List<AutoUVDiscardData.UVRegion> DetectByBlenderVertexGroups(Mesh mesh, AutoUVDiscardData data)
        {
            if (data.blenderVertexGroups == null || data.blenderVertexGroups.Count == 0)
            {
                Debug.LogError("[AutoUVDiscard] No Blender vertex groups defined! Use 'Import Groups' button.", data);
                return null;
            }

            var clusters = new List<List<int>>();
            var regions = new List<AutoUVDiscardData.UVRegion>();

            foreach (var group in data.blenderVertexGroups)
            {
                if (!group.enabled || group.weights == null || group.weights.Count == 0)
                    continue;

                var vertexIndices = new List<int>();
                foreach (var w in group.weights)
                {
                    if (w.weight >= data.vertexGroupWeightThreshold && w.vertexIndex < mesh.vertexCount)
                    {
                        vertexIndices.Add(w.vertexIndex);
                    }
                }

                if (vertexIndices.Count > 0)
                {
                    var region = new AutoUVDiscardData.UVRegion
                    {
                        vertexIndices = vertexIndices,
                        name = group.name,
                        debugColor = group.debugColor
                    };
                    regions.Add(region);
                }
            }

            // Calculate UV bounds for each region
            Vector2[] uvs = GetUVChannel(mesh, 0);
            if (uvs != null && uvs.Length > 0)
            {
                foreach (var region in regions)
                {
                    CalculateRegionBounds(region, uvs);
                }
            }

            return regions;
        }

        private List<AutoUVDiscardData.UVRegion> DetectByMaterialSlots(Mesh mesh, AutoUVDiscardData data)
        {
            var submeshGroups = UVManipulator.GroupVerticesBySubmesh(mesh);
            
            if (submeshGroups.Count == 0)
            {
                Debug.LogWarning("[AutoUVDiscard] No submeshes found!", data);
                return null;
            }

            Debug.Log($"[AutoUVDiscard] Detected {submeshGroups.Count} material slots", data);

            var clusters = submeshGroups.Values.ToList();
            Vector2[] uvs = GetUVChannel(mesh, 0);
            
            var regions = ConvertClustersToRegions(clusters, uvs, data);
            
            // Name regions by submesh index
            for (int i = 0; i < regions.Count; i++)
            {
                regions[i].name = $"Material {i}";
            }
            
            return regions;
        }

        private List<AutoUVDiscardData.UVRegion> DetectByBoneInfluence(Mesh mesh, AutoUVDiscardData data)
        {
            if (data.targetBones == null || data.targetBones.Count == 0)
            {
                Debug.LogError("[AutoUVDiscard] No target bones specified!", data);
                return null;
            }

            // Get bone indices from renderer
            var renderer = data.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.bones == null)
            {
                Debug.LogError("[AutoUVDiscard] No SkinnedMeshRenderer with bones found!", data);
                return null;
            }

            // Map target bones to their indices
            var targetBoneIndices = new HashSet<int>();
            for (int i = 0; i < renderer.bones.Length; i++)
            {
                if (data.targetBones.Contains(renderer.bones[i]))
                {
                    targetBoneIndices.Add(i);
                    
                    // Include child bones if enabled
                    if (data.includeChildBones)
                    {
                        AddChildBoneIndices(renderer.bones[i], renderer.bones, targetBoneIndices);
                    }
                }
            }

            if (targetBoneIndices.Count == 0)
            {
                Debug.LogWarning("[AutoUVDiscard] No matching bones found in mesh!", data);
                return null;
            }

            var boneGroups = UVManipulator.GroupVerticesByBone(mesh, targetBoneIndices, data.boneWeightThreshold);
            var clusters = boneGroups.Where(kvp => kvp.Value.Count > 0).Select(kvp => kvp.Value).ToList();

            Vector2[] uvs = GetUVChannel(mesh, 0);
            var regions = ConvertClustersToRegions(clusters, uvs, data);
            
            // Name regions by bone
            int idx = 0;
            foreach (var kvp in boneGroups.Where(kvp => kvp.Value.Count > 0))
            {
                if (idx < regions.Count && kvp.Key < renderer.bones.Length)
                {
                    regions[idx].name = renderer.bones[kvp.Key].name;
                }
                idx++;
            }
            
            return regions;
        }

        private void AddChildBoneIndices(Transform parent, Transform[] allBones, HashSet<int> indices)
        {
            foreach (Transform child in parent)
            {
                for (int i = 0; i < allBones.Length; i++)
                {
                    if (allBones[i] == child)
                    {
                        indices.Add(i);
                        AddChildBoneIndices(child, allBones, indices);
                        break;
                    }
                }
            }
        }

        #endregion

        #region Utility Methods

        private List<AutoUVDiscardData.UVRegion> ConvertClustersToRegions(List<List<int>> clusters, Vector2[] uvs, AutoUVDiscardData data)
        {
            List<AutoUVDiscardData.UVRegion> regions = new List<AutoUVDiscardData.UVRegion>();
            Color[] debugColors = new Color[] 
            { 
                Color.red, Color.green, new Color(0.212f, 0.749f, 0.694f), Color.yellow, 
                new Color(0.212f, 0.749f, 0.694f), Color.magenta, new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f) 
            };

            for (int i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                var region = new AutoUVDiscardData.UVRegion
                {
                    vertexIndices = cluster,
                    debugColor = debugColors[i % debugColors.Length]
                };

                if (uvs != null && uvs.Length > 0)
                {
                    CalculateRegionBounds(region, uvs);
                }

                regions.Add(region);
            }

            // Sort by UV center (top to bottom, left to right)
            regions = regions.OrderByDescending(r => r.uvCenter.y)
                           .ThenBy(r => r.uvCenter.x)
                           .ToList();

            return regions;
        }

        private void CalculateRegionBounds(AutoUVDiscardData.UVRegion region, Vector2[] uvs)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            
            foreach (int vertexIdx in region.vertexIndices)
            {
                if (vertexIdx < uvs.Length)
                {
                    Vector2 uv = uvs[vertexIdx];
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }
            }

            region.uvBounds = new Bounds(
                new Vector3((min.x + max.x) / 2f, (min.y + max.y) / 2f, 0),
                new Vector3(max.x - min.x, max.y - min.y, 0)
            );
            region.uvCenter = new Vector2((min.x + max.x) / 2f, (min.y + max.y) / 2f);
        }

        private List<List<int>> ClusterVerticesByUV(Vector2[] uvs, float tolerance)
        {
            List<List<int>> clusters = new List<List<int>>();
            bool[] assigned = new bool[uvs.Length];

            for (int i = 0; i < uvs.Length; i++)
            {
                if (assigned[i]) continue;

                // Start a new cluster
                List<int> cluster = new List<int>();
                Queue<int> toProcess = new Queue<int>();
                toProcess.Enqueue(i);
                assigned[i] = true;

                while (toProcess.Count > 0)
                {
                    int current = toProcess.Dequeue();
                    cluster.Add(current);

                    // Find nearby vertices
                    for (int j = 0; j < uvs.Length; j++)
                    {
                        if (assigned[j]) continue;

                        float distance = Vector2.Distance(uvs[current], uvs[j]);
                        if (distance <= tolerance)
                        {
                            assigned[j] = true;
                            toProcess.Enqueue(j);
                        }
                    }
                }

                if (cluster.Count > 0)
                    clusters.Add(cluster);
            }

            return clusters;
        }

        #endregion

        private void AssignUVTiles(List<AutoUVDiscardData.UVRegion> regions, AutoUVDiscardData data)
        {
            if (data.autoAssignUVTile)
            {
                // Use orchestrator-assigned starting tile
                int currentRow = data.startRow >= 0 ? data.startRow : 3;
                int currentColumn = data.startColumn >= 0 ? data.startColumn : 0;
                
                // Check if coordinator has assigned tiles for this component
                if (AutoBodyHiderCoordinator.UVDiscardGroups.ContainsKey(data.targetBodyMesh))
                {
                    var group = AutoBodyHiderCoordinator.UVDiscardGroups[data.targetBodyMesh];
                    if (group.assignedTiles.ContainsKey(data) && group.assignedTiles[data].Count > 0)
                    {
                        // Use coordinator-assigned starting tile
                        var firstTile = group.assignedTiles[data][0];
                        currentRow = firstTile.row;
                        currentColumn = firstTile.col;
                    }
                }
                
                foreach (var region in regions)
                {
                    region.assignedRow = currentRow;
                    region.assignedColumn = currentColumn;
                    
                    currentColumn++;
                    if (currentColumn > 3)
                    {
                        currentColumn = 0;
                        currentRow++;
                        if (currentRow > 3)
                        {
                            Debug.LogWarning($"[AutoUVDiscard] Ran out of UV tiles! Some regions may not be assigned.", data);
                            currentRow = 3;
                            currentColumn = 3;
                        }
                    }
                }
            }
            else
            {
                // Manual assignment
                int currentRow = data.startRow >= 0 ? data.startRow : 3;
                int currentColumn = data.startColumn >= 0 ? data.startColumn : 0;

                foreach (var region in regions)
                {
                    region.assignedRow = currentRow;
                    region.assignedColumn = currentColumn;

                    currentColumn++;
                    if (currentColumn > 3)
                    {
                        currentColumn = 0;
                        currentRow++;
                        if (currentRow > 3)
                        {
                            Debug.LogWarning($"[AutoUVDiscard] Ran out of UV tiles! Some regions may not be assigned.", data);
                            currentRow = 3;
                            currentColumn = 3;
                        }
                    }
                }
            }
        }

        private void ConfigurePoiyomiMaterial(Material material, int row, int column, AutoUVDiscardData data, Mesh mesh, int uvChannel)
        {
            string shaderNameLower = material.shader.name.ToLower();
            
            // Configure shader
            if (shaderNameLower.Contains("poiyomi"))
            {
                material.SetFloat("_EnableUDIMDiscardOptions", 1f);
                material.EnableKeyword("POI_UDIMDISCARD");
            }
            else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("fast fur") || shaderNameLower.Contains("wffs") || shaderNameLower.Contains("warren"))
            {
                // FastFur: Install UV Discard module first
                if (material.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                {
                    float currentValue = material.GetFloat("_WFFS_FEATURES_UVDISCARD");
                    if (currentValue < 0.5f)
                    {
                        material.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                        try { material.SetInt("_WFFS_FEATURES_UVDISCARD", 1); } catch { }
                        material.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                        EditorUtility.SetDirty(material);
                        material.name = material.name;
                    }
                    else
                    {
                        material.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                    }
                }
                
                // Enable UDIM discard option
                if (material.HasProperty("_EnableUDIMDiscardOptions"))
                {
                    material.SetFloat("_EnableUDIMDiscardOptions", 1f);
                    try { material.SetInt("_EnableUDIMDiscardOptions", 1); } catch { }
                    EditorUtility.SetDirty(material);
                    material.name = material.name;
                }
            }
            
            material.SetFloat("_UDIMDiscardMode", 0f);
            material.SetFloat("_UDIMDiscardUV", uvChannel);

            string tilePropertyName = $"_UDIMDiscardRow{row}_{column}";
            if (material.HasProperty(tilePropertyName))
            {
                material.SetFloat(tilePropertyName, 0f);
                material.SetOverrideTag(tilePropertyName + "Animated", "1");
            }

            EditorUtility.SetDirty(material);
        }

        private string GetGlobalParameterName(AutoUVDiscardData data, int regionIndex)
        {
            if (data.useSingleGlobalParameter)
            {
                return string.IsNullOrEmpty(data.singleGlobalParameterName) ? "AutoUVDiscard_All" : data.singleGlobalParameterName;
            }
            else
            {
                string baseName = string.IsNullOrEmpty(data.globalParameterBaseName) ? "AutoUVDiscard" : data.globalParameterBaseName;
                return $"{baseName}_{regionIndex + 1}";
            }
        }
        
        private void RegisterGlobalParameter(AutoUVDiscardData data, string parameterName, Material poiyomiMaterial, AutoUVDiscardData.UVRegion region)
        {
            try
            {
                var descriptor = data.transform.root.GetComponent<VRCAvatarDescriptor>();
                if (descriptor == null)
                {
                    Debug.LogWarning($"[AutoUVDiscard] No VRCAvatarDescriptor found on avatar root. Global parameter '{parameterName}' will not be registered.", data);
                    return;
                }
                
                VRCFuryHelper.AddGlobalParamToVRCFury(descriptor, parameterName);
                
                // Find existing VRCFury toggles that use this global parameter
                var availableToggles = ScanVRCFuryTogglesForGlobalParameter(data.gameObject, parameterName);
                
                if (availableToggles.Count > 0)
                {
                    // Add animation to existing toggle(s) that use this parameter
                    foreach (var toggle in availableToggles)
                    {
                        AddAnimationToToggle(toggle, data, poiyomiMaterial, region, parameterName);
                    }
                }
                else
                {
                    // Create a new toggle with this global parameter
                    var toggle = FuryComponents.CreateToggle(data.gameObject);
                    toggle.SetGlobalParameter(parameterName);
                    
                    // Disable menu item creation for global parameter only mode
                    var toggleType = toggle.GetType();
                    var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (cField != null)
                    {
                        var toggleModel = cField.GetValue(toggle);
                        var addMenuItemField = toggleModel.GetType().GetField("addMenuItem", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (addMenuItemField != null)
                        {
                            addMenuItemField.SetValue(toggleModel, false);
                        }
                    }
                    
                    AddAnimationToToggle(toggle, data, poiyomiMaterial, region, parameterName);
                    Debug.Log($"[AutoUVDiscard] Created toggle with global parameter '{parameterName}' for {region.name}", data);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoUVDiscard] Error registering global parameter '{parameterName}' for {region.name}: {ex.Message}", data);
                Debug.LogException(ex);
            }
        }
        
        private List<object> ScanVRCFuryTogglesForGlobalParameter(GameObject root, string globalParameter)
        {
            var matchingToggles = new List<object>();
            
            // Check root GameObject
            var rootComponents = root.GetComponents<Component>();
            foreach (var comp in rootComponents)
            {
                if (comp != null && comp.GetType().Name == "VRCFury")
                {
                    string toggleParam = GetGlobalParameterFromToggle(comp);
                    if (toggleParam == globalParameter)
                    {
                        matchingToggles.Add(comp);
                    }
                }
            }
            
            // Check all children
            foreach (Transform child in root.transform)
            {
                var childComponents = child.GetComponents<Component>();
                foreach (var comp in childComponents)
                {
                    if (comp != null && comp.GetType().Name == "VRCFury")
                    {
                        string toggleParam = GetGlobalParameterFromToggle(comp);
                        if (toggleParam == globalParameter)
                        {
                            matchingToggles.Add(comp);
                        }
                    }
                }
            }
            
            return matchingToggles;
        }
        
        private string GetGlobalParameterFromToggle(Component toggle)
        {
            try
            {
                var toggleType = toggle.GetType();
                var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (cField == null) return null;
                
                var toggleModel = cField.GetValue(toggle);
                if (toggleModel == null) return null;
                
                var stateField = toggleModel.GetType().GetField("state", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (stateField == null) return null;
                
                var state = stateField.GetValue(toggleModel);
                if (state == null) return null;
                
                var globalParameterField = state.GetType().GetField("globalParameter", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (globalParameterField == null) return null;
                
                return globalParameterField.GetValue(state) as string;
            }
            catch
            {
                return null;
            }
        }
        
        private void AddAnimationToToggle(object toggleComponent, AutoUVDiscardData data, Material poiyomiMaterial, AutoUVDiscardData.UVRegion region, string parameterName)
        {
            try
            {
                AnimationClip toggleAnimation = CreateRegionAnimation(data, poiyomiMaterial, region);
                if (toggleAnimation == null) return;
                
                var toggleType = toggleComponent.GetType();
                var actionsMethod = toggleType.GetMethod("GetActions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (actionsMethod != null)
                {
                    var actions = actionsMethod.Invoke(toggleComponent, null) as dynamic;
                    if (actions != null)
                    {
                        actions.AddAnimationClip(toggleAnimation);
                        
                        var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (cField != null)
                        {
                            var toggleModel = cField.GetValue(toggleComponent);
                            var stateField = toggleModel.GetType().GetField("state", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (stateField != null)
                            {
                                var state = stateField.GetValue(toggleModel);
                                var actionsField = state.GetType().GetField("actions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                if (actionsField != null)
                                {
                                    var actionsList = actionsField.GetValue(state) as System.Collections.IList;
                                    if (actionsList != null && actionsList.Count > 0)
                                    {
                                        var lastAction = actionsList[actionsList.Count - 1];
                                        var motionField = lastAction.GetType().GetField("motion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                        if (motionField != null)
                                        {
                                            motionField.SetValue(lastAction, toggleAnimation);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoUVDiscard] Error adding animation to toggle: {ex.Message}", data);
            }
        }

        private AnimationClip CreateRegionAnimation(AutoUVDiscardData data, Material poiyomiMaterial, 
            AutoUVDiscardData.UVRegion region)
        {
            AnimationClip clip = new AnimationClip();
            clip.name = $"UV_Discard_{region.name}_{data.gameObject.name}";

            string tilePropertyName = $"_UDIMDiscardRow{region.assignedRow}_{region.assignedColumn}";

            if (!poiyomiMaterial.HasProperty(tilePropertyName))
            {
                Debug.LogError($"[AutoUVDiscard] Material doesn't have '{tilePropertyName}' property", data);
                return null;
            }

            string rendererPath = GetRelativePath(data.targetBodyMesh.transform, data.transform.root);

            // Animation plays when toggle is ON: set to 1 (discard ON)
            float animValue = 1f;

            AnimationCurve discardCurve = new AnimationCurve();
            discardCurve.AddKey(0f, animValue);
            discardCurve.AddKey(1f / 60f, animValue);

            // Unity uses "material.PropertyName" format
            string propertyPath = $"material.{tilePropertyName}";

            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                rendererPath,
                typeof(SkinnedMeshRenderer),
                propertyPath
            );

            AnimationUtility.SetEditorCurve(clip, binding, discardCurve);

            return clip;
        }

        private Vector2[] GetUVChannel(Mesh mesh, int channel)
        {
            List<Vector2> uvList = new List<Vector2>();

            switch (channel)
            {
                case 0: mesh.GetUVs(0, uvList); break;
                case 1: mesh.GetUVs(1, uvList); break;
                case 2: mesh.GetUVs(2, uvList); break;
                case 3: mesh.GetUVs(3, uvList); break;
                default:
                    Debug.LogError($"[AutoUVDiscard] Invalid UV channel: {channel}");
                    return null;
            }

            return uvList.ToArray();
        }

        private string GetRelativePath(Transform target, Transform root)
        {
            if (target == root)
                return "";

            List<string> path = new List<string>();
            Transform current = target;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }
    }
}

