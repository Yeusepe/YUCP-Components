using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
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

                // Check if body has Poiyomi material
                Material poiyomiMaterial = FindPoiyomiMaterial(data.targetBodyMesh);
                if (poiyomiMaterial == null)
                {
                    Debug.LogError($"[AutoUDIMDiscard] Body mesh doesn't have a Poiyomi material with UDIM support!", data);
                    return;
                }

                // Detect UV regions from clothing mesh
                List<AutoUDIMDiscardData.UVRegion> regions = DetectUVRegions(clothingRenderer.sharedMesh, data);

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
                        data.targetBodyMesh.sharedMesh,
                        hiddenVertices,
                        region.assignedRow,
                        region.assignedColumn
                    );

                    if (modifiedMesh != null)
                    {
                        data.targetBodyMesh.sharedMesh = modifiedMesh;
                    }

                    // Configure material for this tile
                    ConfigurePoiyomiMaterial(poiyomiMaterial, region.assignedRow, region.assignedColumn, data);

                    usedTiles.Add($"Row{region.assignedRow}_Col{region.assignedColumn}");

                    // Create toggle if enabled
                    if (data.createToggles)
                    {
                        string togglePath = data.useMasterToggle ? data.masterTogglePath : $"{data.toggleMenuPath}/{region.name}";
                        CreateRegionToggle(data, poiyomiMaterial, region, togglePath, i);
                    }
                }

                // Create master toggle if enabled
                if (data.useMasterToggle && data.createToggles)
                {
                    // Master toggle would control all regions together
                    // For now, individual toggles are created under the master path
                    Debug.Log($"[AutoUDIMDiscard] Master toggle mode: all regions grouped under '{data.masterTogglePath}'", data);
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

        private Material FindPoiyomiMaterial(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMaterials == null)
                return null;

            foreach (var mat in renderer.sharedMaterials)
            {
                if (UDIMManipulator.IsPoiyomiWithUDIMSupport(mat))
                    return mat;
            }

            return null;
        }

        private List<AutoUDIMDiscardData.UVRegion> DetectUVRegions(Mesh mesh, AutoUDIMDiscardData data)
        {
            Vector2[] uvs = GetUVChannel(mesh, data.uvChannel);
            if (uvs == null || uvs.Length == 0)
            {
                Debug.LogError($"[AutoUDIMDiscard] No UV{data.uvChannel} data found on mesh!", data);
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
                Color.red, Color.green, Color.blue, Color.yellow, 
                Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f) 
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
            int currentRow = data.startRow;
            int currentColumn = data.startColumn;

            foreach (var region in regions)
            {
                region.assignedRow = currentRow;
                region.assignedColumn = currentColumn;

                // Move to next tile
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

        private void ConfigurePoiyomiMaterial(Material material, int row, int column, AutoUDIMDiscardData data)
        {
            material.SetFloat("_EnableUDIMDiscardOptions", 1f);
            material.EnableKeyword("POI_UDIMDISCARD");
            material.SetFloat("_UDIMDiscardMode", 0f); // Vertex mode
            material.SetFloat("_UDIMDiscardUV", 1); // UV1

            string tilePropertyName = $"_UDIMDiscardRow{row}_{column}";
            if (material.HasProperty(tilePropertyName))
            {
                // Material base = 0 (discard OFF when toggle is OFF)
                material.SetFloat(tilePropertyName, 0f);
                material.SetOverrideTag(tilePropertyName + "Animated", "1");
            }

            EditorUtility.SetDirty(material);
        }

        private void CreateRegionToggle(AutoUDIMDiscardData data, Material poiyomiMaterial, 
            AutoUDIMDiscardData.UVRegion region, string menuPath, int regionIndex)
        {
            try
            {
                // Disable the clothing object by default (toggle OFF state)
                if (regionIndex == 0 && !data.gameObject.activeSelf)
                {
                    data.gameObject.SetActive(false);
                }

                var toggle = FuryComponents.CreateToggle(data.gameObject);
                toggle.SetMenuPath(menuPath);

                if (data.toggleSaved)
                    toggle.SetSaved();

                var actions = toggle.GetActions();

                // Always use ObjectToggle for now
                actions.AddTurnOn(data.gameObject);

                // Create animation for this region's UDIM tile
                AnimationClip toggleAnimation = CreateRegionAnimation(data, poiyomiMaterial, region);

                if (toggleAnimation != null)
                {
                    actions.AddAnimationClip(toggleAnimation);

                    // Set motion field via reflection
                    var toggleType = toggle.GetType();
                    var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var toggleModel = cField.GetValue(toggle);
                    var stateField = toggleModel.GetType().GetField("state", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var state = stateField.GetValue(toggleModel);
                    var actionsField = state.GetType().GetField("actions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
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

                Debug.Log($"[AutoUDIMDiscard] Created toggle for {region.name} at '{menuPath}'", data);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoUDIMDiscard] Error creating toggle for {region.name}: {ex.Message}", data);
                Debug.LogException(ex);
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

