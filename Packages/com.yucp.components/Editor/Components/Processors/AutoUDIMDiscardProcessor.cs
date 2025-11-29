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
    /// Processes Auto UDIM Discard components during avatar build.
    /// Automatically detects UV regions and creates corresponding UDIM discards with toggles.
    /// </summary>
    public class AutoUDIMDiscardProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 101; // Run right after AutoBodyHider

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var components = avatarRoot.GetComponentsInChildren<AutoUDIMDiscardData>(true);

            foreach (var data in components)
            {
                if (data != null && data.enabled)
                {
                    ProcessAutoDiscard(data);
                }
            }

            return true;
        }

        private void ProcessAutoDiscard(AutoUDIMDiscardData data)
        {
            try
            {
                Debug.Log($"[AutoUDIMDiscard] Processing: {data.gameObject.name}", data);

                // Validate
                if (data.targetBodyMesh == null)
                {
                    Debug.LogError($"[AutoUDIMDiscard] Target body mesh not set!", data);
                    return;
                }

                var clothingRenderer = data.GetComponent<SkinnedMeshRenderer>();
                if (clothingRenderer == null || clothingRenderer.sharedMesh == null)
                {
                    Debug.LogError($"[AutoUDIMDiscard] No SkinnedMeshRenderer or mesh found!", data);
                    return;
                }

                // Find compatible materials (use targetMaterials if specified, otherwise auto-detect)
                List<Material> targetMaterials = new List<Material>();
                if (data.targetMaterials != null && data.targetMaterials.Length > 0)
                {
                    foreach (var mat in data.targetMaterials)
                    {
                        if (mat != null && UDIMManipulator.IsPoiyomiWithUDIMSupport(mat))
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
                            if (mat != null && UDIMManipulator.IsPoiyomiWithUDIMSupport(mat))
                            {
                                targetMaterials.Add(mat);
                            }
                        }
                    }
                }
                
                if (targetMaterials.Count == 0)
                {
                    Debug.LogError($"[AutoUDIMDiscard] Body mesh doesn't have a Poiyomi or FastFur material with UDIM support!", data);
                    return;
                }
                
                string shaderName = UDIMManipulator.GetShaderDisplayName(targetMaterials[0]);
                Debug.Log($"[AutoUDIMDiscard] Using {shaderName} shader for UDIM discard on {targetMaterials.Count} material(s)", data);

                // Detect UV regions from clothing mesh
                int effectiveUVChannel = data.autoDetectUVChannel 
                    ? UDIMManipulator.DetectBestUVChannel(clothingRenderer.sharedMesh)
                    : data.uvChannel;
                List<AutoUDIMDiscardData.UVRegion> regions = DetectUVRegions(clothingRenderer.sharedMesh, data, effectiveUVChannel);

                if (regions == null || regions.Count == 0)
                {
                    Debug.LogWarning($"[AutoUDIMDiscard] No UV regions detected!", data);
                    return;
                }

                Debug.Log($"[AutoUDIMDiscard] Detected {regions.Count} UV regions", data);

                // Assign UDIM tiles to each region
                AssignUDIMTiles(regions, data);

                // Process each region
                List<string> usedTiles = new List<string>();
                Mesh originalBodyMesh = data.targetBodyMesh.sharedMesh;
                
                for (int i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    region.name = $"Region {i + 1}";
                    
                    Debug.Log($"[AutoUDIMDiscard] Processing {region.name}: {region.vertexIndices.Count} vertices -> UDIM {region.assignedRow},{region.assignedColumn}", data);

                    // Create hidden vertices array for this region
                    bool[] hiddenVertices = new bool[clothingRenderer.sharedMesh.vertexCount];
                    foreach (int vertexIndex in region.vertexIndices)
                    {
                        if (vertexIndex < hiddenVertices.Length)
                            hiddenVertices[vertexIndex] = true;
                    }

                    // Apply UDIM discard for this region
                    Mesh modifiedMesh = UDIMManipulator.ApplyUDIMDiscard(
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
                
                Debug.Log($"[AutoUDIMDiscard] Successfully processed {regions.Count} regions!", data);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoUDIMDiscard] Error processing: {ex.Message}", data);
                Debug.LogException(ex);
            }
        }

        private List<AutoUDIMDiscardData.UVRegion> DetectUVRegions(Mesh mesh, AutoUDIMDiscardData data, int uvChannel)
        {
            Vector2[] uvs = GetUVChannel(mesh, uvChannel);
            if (uvs == null || uvs.Length == 0)
            {
                Debug.LogError($"[AutoUDIMDiscard] No UV{uvChannel} data found on mesh!", data);
                return null;
            }

            // Group vertices by UV proximity
            List<List<int>> uvClusters = ClusterVerticesByUV(uvs, data.mergeTolerance);

            // Filter out small clusters
            int minVertices = Mathf.CeilToInt(mesh.vertexCount * (data.minRegionSize / 100f));
            uvClusters = uvClusters.Where(cluster => cluster.Count >= minVertices).ToList();

            // Convert clusters to regions
            List<AutoUDIMDiscardData.UVRegion> regions = new List<AutoUDIMDiscardData.UVRegion>();
            Color[] debugColors = new Color[] 
            { 
                Color.red, Color.green, new Color(0.212f, 0.749f, 0.694f), Color.yellow, 
                new Color(0.212f, 0.749f, 0.694f), Color.magenta, new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f) 
            };

            for (int i = 0; i < uvClusters.Count; i++)
            {
                var cluster = uvClusters[i];
                var region = new AutoUDIMDiscardData.UVRegion
                {
                    vertexIndices = cluster,
                    debugColor = debugColors[i % debugColors.Length]
                };

                // Calculate UV bounds
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);
                
                foreach (int vertexIdx in cluster)
                {
                    Vector2 uv = uvs[vertexIdx];
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }

                region.uvBounds = new Bounds(
                    new Vector3((min.x + max.x) / 2f, (min.y + max.y) / 2f, 0),
                    new Vector3(max.x - min.x, max.y - min.y, 0)
                );
                region.uvCenter = new Vector2((min.x + max.x) / 2f, (min.y + max.y) / 2f);

                regions.Add(region);
            }

            // Sort by UV center (top to bottom, left to right)
            regions = regions.OrderByDescending(r => r.uvCenter.y)
                           .ThenBy(r => r.uvCenter.x)
                           .ToList();

            return regions;
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

        private void AssignUDIMTiles(List<AutoUDIMDiscardData.UVRegion> regions, AutoUDIMDiscardData data)
        {
            if (data.autoAssignUDIMTile)
            {
                // Use orchestrator-assigned starting tile
                int currentRow = data.startRow >= 0 ? data.startRow : 3;
                int currentColumn = data.startColumn >= 0 ? data.startColumn : 0;
                
                // Check if coordinator has assigned tiles for this component
                if (AutoBodyHiderCoordinator.UDIMDiscardGroups.ContainsKey(data.targetBodyMesh))
                {
                    var group = AutoBodyHiderCoordinator.UDIMDiscardGroups[data.targetBodyMesh];
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
                            Debug.LogWarning($"[AutoUDIMDiscard] Ran out of UDIM tiles! Some regions may not be assigned.", data);
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
                            Debug.LogWarning($"[AutoUDIMDiscard] Ran out of UDIM tiles! Some regions may not be assigned.", data);
                            currentRow = 3;
                            currentColumn = 3;
                        }
                    }
                }
            }
        }

        private void ConfigurePoiyomiMaterial(Material material, int row, int column, AutoUDIMDiscardData data, Mesh mesh, int uvChannel)
        {
            string shaderNameLower = material.shader.name.ToLower();
            
            // Configure based on shader type
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

        private string GetGlobalParameterName(AutoUDIMDiscardData data, int regionIndex)
        {
            if (data.useSingleGlobalParameter)
            {
                return string.IsNullOrEmpty(data.singleGlobalParameterName) ? "AutoUDIMDiscard_All" : data.singleGlobalParameterName;
            }
            else
            {
                string baseName = string.IsNullOrEmpty(data.globalParameterBaseName) ? "AutoUDIMDiscard" : data.globalParameterBaseName;
                return $"{baseName}_{regionIndex + 1}";
            }
        }
        
        private void RegisterGlobalParameter(AutoUDIMDiscardData data, string parameterName, Material poiyomiMaterial, AutoUDIMDiscardData.UVRegion region)
        {
            try
            {
                var descriptor = data.transform.root.GetComponent<VRCAvatarDescriptor>();
                if (descriptor == null)
                {
                    Debug.LogWarning($"[AutoUDIMDiscard] No VRCAvatarDescriptor found on avatar root. Global parameter '{parameterName}' will not be registered.", data);
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
                    Debug.Log($"[AutoUDIMDiscard] Created toggle with global parameter '{parameterName}' for {region.name}", data);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoUDIMDiscard] Error registering global parameter '{parameterName}' for {region.name}: {ex.Message}", data);
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
        
        private void AddAnimationToToggle(object toggleComponent, AutoUDIMDiscardData data, Material poiyomiMaterial, AutoUDIMDiscardData.UVRegion region, string parameterName)
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
                Debug.LogError($"[AutoUDIMDiscard] Error adding animation to toggle: {ex.Message}", data);
            }
        }

        private AnimationClip CreateRegionAnimation(AutoUDIMDiscardData data, Material poiyomiMaterial, 
            AutoUDIMDiscardData.UVRegion region)
        {
            AnimationClip clip = new AnimationClip();
            clip.name = $"UDIM_Discard_{region.name}_{data.gameObject.name}";

            string tilePropertyName = $"_UDIMDiscardRow{region.assignedRow}_{region.assignedColumn}";

            if (!poiyomiMaterial.HasProperty(tilePropertyName))
            {
                Debug.LogError($"[AutoUDIMDiscard] Material doesn't have '{tilePropertyName}' property", data);
                return null;
            }

            string rendererPath = GetRelativePath(data.targetBodyMesh.transform, data.transform.root);

            // Animation plays when toggle is ON: set to 1 (discard ON)
            float animValue = 1f;

            AnimationCurve discardCurve = new AnimationCurve();
            discardCurve.AddKey(0f, animValue);
            discardCurve.AddKey(1f / 60f, animValue);

            // Unity always uses "material.PropertyName" format
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
                    Debug.LogError($"[AutoUDIMDiscard] Invalid UV channel: {channel}");
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

