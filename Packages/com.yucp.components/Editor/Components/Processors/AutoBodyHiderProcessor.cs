using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Auto Body Hider components during avatar build.
    /// Detects which body vertices are hidden by clothing and either moves their UVs to a discard tile (Poiyomi)
    /// or physically deletes them from the mesh (other shaders).
    /// Supports GPU-accelerated detection with multiple algorithms (Raycast, Proximity, Hybrid, Smart, Manual).
    /// </summary>
    public class AutoBodyHiderProcessor : IVRCSDKPreprocessAvatarCallback
    {
        private const string GeneratedMaterialsRoot = "Assets/YUCPGenerated";
        private const string GeneratedMaterialsFolder = GeneratedMaterialsRoot + "/AutoBodyHider";

        public int callbackOrder => int.MinValue + 100;

        private class BodyMeshProcessing
        {
            public SkinnedMeshRenderer bodyMesh;
            public Mesh originalMesh;
            public List<AutoBodyHiderData> uvComponents = new List<AutoBodyHiderData>();
            public List<AutoBodyHiderData> deletionComponents = new List<AutoBodyHiderData>();
            public Dictionary<AutoBodyHiderData, bool[]> hiddenVerticesMap = new Dictionary<AutoBodyHiderData, bool[]>();
        }

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<AutoBodyHiderData>(true);


            Dictionary<SkinnedMeshRenderer, BodyMeshProcessing> bodyMeshes = new Dictionary<SkinnedMeshRenderer, BodyMeshProcessing>();

            foreach (var data in dataList)
            {
                if (data.debugMode)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Processing '{data.name}'", data);
                }


                if (!ValidateData(data))
                {
                    continue;
                }

                try
                {

                    if (!bodyMeshes.ContainsKey(data.targetBodyMesh))
                    {
                        bodyMeshes[data.targetBodyMesh] = new BodyMeshProcessing
                        {
                            bodyMesh = data.targetBodyMesh,
                            originalMesh = data.targetBodyMesh.sharedMesh
                        };
                    }

                    var group = bodyMeshes[data.targetBodyMesh];
                    ApplicationMode mode = DetermineApplicationMode(data);

                    if (mode == ApplicationMode.UVDiscard)
                    {
                        group.uvComponents.Add(data);
                    }
                    else
                    {
                        group.deletionComponents.Add(data);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[AutoBodyHiderProcessor] Error processing '{data.name}': {ex.Message}", data);
                    Debug.LogException(ex);
                }
            }


            foreach (var group in bodyMeshes.Values)
            {
                ProcessBodyMeshGroup(group);
            }

            return true;
        }

        private bool ValidateData(AutoBodyHiderData data)
        {
            if (data.targetBodyMesh == null)
            {
                Debug.LogError("[AutoBodyHiderProcessor] No target body mesh specified.", data);
                return false;
            }

            if (data.targetBodyMesh.sharedMesh == null)
            {
                Debug.LogError("[AutoBodyHiderProcessor] Target body mesh has no mesh data.", data);
                return false;
            }


            if (data.detectionMethod != DetectionMethod.Manual)
            {
                bool hasValidClothingMesh = false;
                if (data.clothingMeshes != null && data.clothingMeshes.Length > 0)
                {
                    foreach (var mesh in data.clothingMeshes)
                    {
                        if (mesh != null && mesh.sharedMesh != null)
                        {
                            hasValidClothingMesh = true;
                            break;
                        }
                    }
                }

                if (!hasValidClothingMesh)
                {
                    Debug.LogError("[AutoBodyHiderProcessor] Automatic detection requires at least one valid clothing mesh reference.", data);
                    return false;
                }
            }

            if (data.detectionMethod == DetectionMethod.Manual && data.manualMask == null)
            {
                Debug.LogError("[AutoBodyHiderProcessor] Manual detection requires a mask texture.", data);
                return false;
            }

            return true;
        }

        private void ProcessBodyMeshGroup(BodyMeshProcessing group)
        {

            if (group.uvComponents.Count > 0)
            {
                ProcessUVGroup(group);
            }


            if (group.deletionComponents.Count > 0)
            {
                ProcessDeletionGroup(group);
            }
        }

        private void ProcessUVGroup(BodyMeshProcessing group)
        {
            Debug.Log($"[AutoBodyHiderProcessor] Processing {group.uvComponents.Count} UV discard components for '{group.bodyMesh.name}'");


            int skippedCount = 0;
            foreach (var data in group.uvComponents)
            {

                if (data.uvDiscardRow < 0 || data.uvDiscardRow > 3 ||
                    data.uvDiscardColumn < 0 || data.uvDiscardColumn > 3)
                {
                    skippedCount++;
                    Debug.LogWarning($"[AutoBodyHiderProcessor] Skipping '{data.name}' - no valid UV tile assigned (tile limit exceeded)", data);
                    data.SetBuildStats(0, "Skipped (Tile Limit)");
                }
            }


            List<AutoBodyHiderData> validComponents = new List<AutoBodyHiderData>();
            foreach (var data in group.uvComponents)
            {
                if (data.uvDiscardRow >= 0 && data.uvDiscardRow <= 3 &&
                    data.uvDiscardColumn >= 0 && data.uvDiscardColumn <= 3)
                {
                    validComponents.Add(data);
                }
            }

            if (validComponents.Count == 0)
            {
                Debug.LogWarning($"[AutoBodyHiderProcessor] No valid UV components to process for '{group.bodyMesh.name}'");
                return;
            }

            Debug.Log($"[AutoBodyHiderProcessor] Processing {validComponents.Count} valid UV components (skipped {skippedCount})");

            Mesh originalMesh = group.originalMesh;
            Vector3[] vertices = originalMesh.vertices;


            YUCPProgressWindow progressWindow = null;
            bool showProgress = validComponents.Count > 1 ||
                               (validComponents.Count > 0 && ShouldShowProgress(validComponents[0], vertices.Length));

            if (showProgress)
            {
                progressWindow = YUCPProgressWindow.Create();
                progressWindow.Progress(0, $"Processing {validComponents.Count} clothing pieces...");
            }

            try
            {
                int totalSteps = validComponents.Count + 2;
                int currentStep = 0;


                Dictionary<AutoBodyHiderData, int> coverageMap = new Dictionary<AutoBodyHiderData, int>();

                foreach (var data in validComponents)
                {
                    if (progressWindow != null)
                    {
                        float progress = (float)currentStep / totalSteps;
                        progressWindow.Progress(progress, $"Detecting hidden vertices for '{data.name}'...");
                    }

                    bool[] hiddenVertices = DetectHiddenVerticesForData(data, originalMesh);

                    if (hiddenVertices != null)
                    {

                        int hiddenCount = 0;
                        foreach (bool h in hiddenVertices) if (h) hiddenCount++;

                        hiddenVertices = ApplyPostProcessing(data, vertices, hiddenVertices, ref hiddenCount);

                        group.hiddenVerticesMap[data] = hiddenVertices;
                        coverageMap[data] = hiddenCount;
                        data.SetBuildStats(hiddenCount, "UV Discard (Multi)");
                    }

                    currentStep++;
                }


                bool anyOptimizationEnabled = validComponents.Any(c => c.optimizeTileUsage);

                if (anyOptimizationEnabled)
                {

                    validComponents = validComponents
                        .OrderByDescending(c => coverageMap.ContainsKey(c) ? coverageMap[c] : 0)
                        .ToList();

                    Debug.Log($"[AutoBodyHiderProcessor] Optimization enabled - sorted {validComponents.Count} pieces by coverage:");
                    foreach (var data in validComponents)
                    {
                        int coverage = coverageMap.ContainsKey(data) ? coverageMap[data] : 0;
                        Debug.Log($"  • '{data.name}': {coverage} vertices");
                    }
                }


                if (validComponents.Count >= 2)
                {
                    if (progressWindow != null)
                    {
                        progressWindow.Progress((float)currentStep / totalSteps, "Detecting actual overlaps between clothing pieces...");
                    }
                    DetectAndAssignActualOverlaps(group);
                }


                if (progressWindow != null)
                {
                    float progress = (float)currentStep / totalSteps;
                    progressWindow.Progress(progress, "Merging UV discards into body mesh...");
                }

                Mesh modifiedMesh = MergeUVDiscards(group);
                group.bodyMesh.sharedMesh = modifiedMesh;
                currentStep++;


                if (progressWindow != null)
                {
                    float progress = (float)currentStep / totalSteps;
                    progressWindow.Progress(progress, "Configuring material UV tiles...");
                }

                ConfigureMaterialsForMultipleUV(group);
                currentStep++;


                if (progressWindow != null)
                {
                    progressWindow.Progress(1.0f, "Creating toggles...");
                }

                foreach (var data in validComponents)
                {

                    if (data.createToggle || data.useExistingToggle)
                    {
                        CreateUVToggleForComponent(data, group);
                    }
                }

                if (progressWindow != null)
                {
                    progressWindow.Progress(1.0f, "UV discard processing complete!");
                }
            }
            finally
            {
                if (progressWindow != null)
                {
                    progressWindow.CloseWindow();
                }
            }
        }

        private void ProcessDeletionGroup(BodyMeshProcessing group)
        {
            Debug.Log($"[AutoBodyHiderProcessor] Processing {group.deletionComponents.Count} mesh deletion components for '{group.bodyMesh.name}'");


            Mesh currentMesh = group.bodyMesh.sharedMesh;

            foreach (var data in group.deletionComponents)
            {
                ProcessBodyHider(data);

            }
        }

        private void ProcessBodyHider(AutoBodyHiderData data)
        {
            Mesh bodyMesh = data.targetBodyMesh.sharedMesh;
            Vector3[] vertices = bodyMesh.vertices;


            var clothingMeshes = data.GetClothingMeshes();
            Mesh clothingMesh = (clothingMeshes != null && clothingMeshes.Length > 0) ? clothingMeshes[0].sharedMesh : null;

            YUCPProgressWindow progressWindow = null;
            if (ShouldShowProgress(data, vertices.Length))
            {
                progressWindow = YUCPProgressWindow.Create();
            }

            try
            {
                bool[] hiddenVertices;
                int hiddenCount;

                if (DetectionCache.TryGetCachedResult(data, bodyMesh, clothingMesh, out hiddenVertices, out hiddenCount))
                {
                    if (data.debugMode)
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] Using cached result: {hiddenCount} hidden vertices");
                    }
                    if (progressWindow != null)
                    {
                        progressWindow.Progress(0.5f, "Using cached detection result...");
                    }
                }
                else
                {
                    Vector3[] normals = bodyMesh.normals;
                    List<Vector2> uv0List = new List<Vector2>();
                    bodyMesh.GetUVs(0, uv0List);
                    Vector2[] uv0 = uv0List.ToArray();

                    if (data.debugMode)
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] Body mesh has {vertices.Length} vertices");
                    }

                    if (progressWindow != null)
                    {
                        progressWindow.Progress(0, "Starting vertex detection...");
                    }

                    var startTime = System.Diagnostics.Stopwatch.StartNew();
                    hiddenVertices = VertexDetection.DetectHiddenVertices(data, vertices, normals, uv0, progressWindow);
                    startTime.Stop();

                    if (progressWindow != null)
                    {
                        progressWindow.Progress(1.0f, "Detection complete!");
                    }

                    hiddenCount = 0;
                    foreach (bool hidden in hiddenVertices)
                    {
                        if (hidden) hiddenCount++;
                    }

                    if (data.debugMode)
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] Detected {hiddenCount} hidden vertices ({(hiddenCount * 100f / vertices.Length):F1}%) in {startTime.ElapsedMilliseconds}ms");
                    }

                    DetectionCache.CacheResult(data, bodyMesh, clothingMesh, hiddenVertices, hiddenCount);
                }

                ProcessHiddenVertices(data, bodyMesh, vertices, hiddenVertices, hiddenCount);
            }
            finally
            {
                if (progressWindow != null)
                {
                    progressWindow.CloseWindow();
                }
            }
        }

        private void ProcessHiddenVertices(AutoBodyHiderData data, Mesh bodyMesh, Vector3[] vertices, bool[] hiddenVertices, int hiddenCount)
        {
            if (hiddenCount == 0)
            {
                Debug.LogWarning("[AutoBodyHiderProcessor] No hidden vertices detected. Check your settings.", data);
                data.SetBuildStats(0, "None");
                return;
            }

            Debug.Log($"[AutoBodyHider DEBUG] Initial hidden vertices: {hiddenCount}", data);

            if (data.mirrorSymmetry)
            {
                hiddenVertices = VertexDetection.ApplySymmetryMirror(hiddenVertices, vertices);

                int countAfterMirror = 0;
                foreach (bool hidden in hiddenVertices)
                {
                    if (hidden) countAfterMirror++;
                }
                Debug.Log($"[AutoBodyHider DEBUG] After symmetry mirror: {countAfterMirror} hidden vertices", data);
                hiddenCount = countAfterMirror;
            }

            if (data.safetyMargin > 0.0001f)
            {
                hiddenVertices = VertexDetection.ApplySafetyMargin(hiddenVertices, vertices, data);

                hiddenCount = 0;
                foreach (bool hidden in hiddenVertices)
                {
                    if (hidden) hiddenCount++;
                }

                Debug.Log($"[AutoBodyHider DEBUG] After safety margin: {hiddenCount} hidden vertices", data);
            }

            ApplicationMode mode = DetermineApplicationMode(data);

            if (mode == ApplicationMode.UVDiscard)
            {
                ApplyUVDiscardMode(data, bodyMesh, hiddenVertices, hiddenCount);
            }
            else
            {
                ApplyMeshDeletionMode(data, bodyMesh, hiddenVertices, hiddenCount);
            }
        }

        private ApplicationMode DetermineApplicationMode(AutoBodyHiderData data)
        {
            Material[] materials = data.targetBodyMesh.sharedMaterials;


            if (data.targetMaterials != null && data.targetMaterials.Length > 0)
            {
                var validTargetMaterials = data.targetMaterials.Where(m => m != null).ToArray();
                if (validTargetMaterials.Length > 0)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Using {validTargetMaterials.Length} user-specified target material(s)");

                    if (data.applicationMode == ApplicationMode.UVDiscard)
                    {

                        bool hasCompatible = validTargetMaterials.Any(m => UVManipulator.IsPoiyomiWithUVSupport(m));
                        if (hasCompatible)
                        {
                            return ApplicationMode.UVDiscard;
                        }
                        else
                        {
                            Debug.LogError($"[AutoBodyHiderProcessor] UV Discard mode selected but none of the target materials have compatible shaders (Poiyomi/FastFur). Falling back to Mesh Deletion.", data);
                            return ApplicationMode.MeshDeletion;
                        }
                    }
                    else if (data.applicationMode == ApplicationMode.MeshDeletion)
                    {
                        return ApplicationMode.MeshDeletion;
                    }
                    else
                    {

                        bool hasCompatible = validTargetMaterials.Any(m => UVManipulator.IsPoiyomiWithUVSupport(m));
                        if (hasCompatible)
                        {
                            var compatibleMaterials = validTargetMaterials.Where(m => UVManipulator.IsPoiyomiWithUVSupport(m)).ToArray();
                            string shaderName = compatibleMaterials.Length > 0 ? UVManipulator.GetShaderDisplayName(compatibleMaterials[0]) : "Poiyomi/FastFur";
                            Debug.Log($"[AutoBodyHiderProcessor] Auto-detected {shaderName} shader with UV support on {compatibleMaterials.Length} target material(s), using UV discard mode");
                            return ApplicationMode.UVDiscard;
                        }
                    }
                }
            }


            Debug.Log($"[AutoBodyHiderProcessor] Body mesh '{data.targetBodyMesh.name}' has {materials.Length} materials:");
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].shader != null)
                {
                    Debug.Log($"[AutoBodyHiderProcessor]   [{i}] '{materials[i].name}' - Shader: '{materials[i].shader.name}'");
                }
                else
                {
                    Debug.Log($"[AutoBodyHiderProcessor]   [{i}] NULL or missing shader");
                }
            }


            if (data.applicationMode == ApplicationMode.UVDiscard)
            {

                bool hasCompatibleShader = false;
                foreach (var material in materials)
                {
                    if (UVManipulator.IsPoiyomiWithUVSupport(material))
                    {
                        hasCompatibleShader = true;
                        break;
                    }
                }

                if (!hasCompatibleShader)
                {
                    Debug.LogError($"[AutoBodyHiderProcessor] UV Discard mode selected but body mesh '{data.targetBodyMesh.name}' has no compatible shader (Poiyomi/FastFur). Falling back to Mesh Deletion.", data);
                    return ApplicationMode.MeshDeletion;
                }

                return ApplicationMode.UVDiscard;
            }
            else if (data.applicationMode == ApplicationMode.MeshDeletion)
            {
                return ApplicationMode.MeshDeletion;
            }


            foreach (var material in materials)
            {
                if (UVManipulator.IsPoiyomiWithUVSupport(material))
                {
                    string shaderName = UVManipulator.GetShaderDisplayName(material);
                    Debug.Log($"[AutoBodyHiderProcessor] Auto-detected {shaderName} shader with UV support on '{material.name}', using UV discard mode");
                    return ApplicationMode.UVDiscard;
                }
            }

            Debug.Log($"[AutoBodyHiderProcessor] No UV-compatible shader found on body mesh, using mesh deletion mode");
            return ApplicationMode.MeshDeletion;
        }

        private void ApplyUVDiscardMode(AutoBodyHiderData data, Mesh originalMesh, bool[] hiddenVertices, int hiddenCount)
        {
            Mesh modifiedMesh = UVManipulator.ApplyUVDiscard(originalMesh, hiddenVertices, data);

            data.targetBodyMesh.sharedMesh = modifiedMesh;

            Material[] materials = data.targetBodyMesh.sharedMaterials;
            List<Material> configuredMaterials = new List<Material>();
            HashSet<int> affectedMaterialIndices = GetAffectedMaterialIndices(originalMesh, new[] { hiddenVertices });
            bool hasExplicitTargetMaterials = data.targetMaterials != null && data.targetMaterials.Any(m => m != null);


            if (hasExplicitTargetMaterials)
            {
                var validTargetMaterials = data.targetMaterials.Where(m => m != null).ToArray();

                foreach (var targetMaterial in validTargetMaterials)
                {

                    for (int i = 0; i < materials.Length; i++)
                    {
                        bool isMatchingMaterial = IsSameMaterialAsset(materials[i], targetMaterial);

                        if (isMatchingMaterial && affectedMaterialIndices.Contains(i) && UVManipulator.IsPoiyomiWithUVSupport(materials[i]))
                        {
                            Material materialCopy = CreateUploadMaterialCopy(materials[i], $"{data.name}_{i}", data);
                            ThryMaterialOptimizerBridge.UnlockIfNeeded(new[] { materialCopy }, data);


                            bool shouldConfigureForToggle = data.createToggle || data.useExistingToggle;

                            if (!shouldConfigureForToggle)
                            {
                                UVManipulator.ConfigurePoiyomiMaterial(materialCopy, data, originalMesh);
                            }
                            else
                            {
                                ConfigurePoiyomiForToggle(materialCopy, data, originalMesh);
                            }

                            materials[i] = materialCopy;
                            configuredMaterials.Add(materialCopy);
                        }
                    }
                }

                if (configuredMaterials.Count == 0)
                {
                    Debug.LogError($"[AutoBodyHiderProcessor] None of the {validTargetMaterials.Length} target material(s) matched an affected compatible material slot. UV discard material setup was skipped to avoid rewriting unrelated materials.", data);
                }
                else
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Configured {configuredMaterials.Count} target material(s) for UV discard.", data);
                }
            }


            if (!hasExplicitTargetMaterials && configuredMaterials.Count == 0)
            {
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (!affectedMaterialIndices.Contains(materialIndex))
                    {
                        continue;
                    }

                    if (UVManipulator.IsPoiyomiWithUVSupport(material))
                    {
                        Material materialCopy = CreateUploadMaterialCopy(material, $"{data.name}_{materialIndex}", data);
                        ThryMaterialOptimizerBridge.UnlockIfNeeded(new[] { materialCopy }, data);


                        bool shouldConfigureForToggle = data.createToggle || data.useExistingToggle;

                        if (!shouldConfigureForToggle)
                        {
                            UVManipulator.ConfigurePoiyomiMaterial(materialCopy, data, originalMesh);
                        }
                        else
                        {
                            ConfigurePoiyomiForToggle(materialCopy, data, originalMesh);
                        }

                        materials[materialIndex] = materialCopy;

                        configuredMaterials.Add(materialCopy);
                    }
                }

                if (configuredMaterials.Count > 0)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Auto-detected and configured {configuredMaterials.Count} compatible material(s) for UV discard.", data);
                }
            }

            ThryMaterialOptimizerBridge.SyncAndLock(configuredMaterials, data);
            data.targetBodyMesh.sharedMaterials = materials;



            Material materialForToggle = configuredMaterials.Count > 0 ? configuredMaterials[0] : null;

            if (materialForToggle != null)
            {
                Component toggleToUse = null;


                if (data.useExistingToggle && data.selectedToggle != null)
                {
                    toggleToUse = data.selectedToggle;
                    Debug.Log($"[AutoBodyHiderProcessor] Using selected toggle: {data.selectedToggle.name} on {GetGameObjectPath(data.selectedToggle.gameObject, data.gameObject)}", data);
                }
                else if (data.useExistingToggle)
                {

                    toggleToUse = VRCFuryHelper.FindToggles(data.gameObject).FirstOrDefault();
                    if (toggleToUse != null)
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] Auto-detected toggle: {toggleToUse.name} on {GetGameObjectPath(toggleToUse.gameObject, data.gameObject)}", data);
                    }
                }
                else
                {

                    var components = data.gameObject.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (VRCFuryHelper.GetToggleModel(comp) != null)
                        {
                            toggleToUse = comp;
                            break;
                        }
                    }
                }

                if (data.createToggle && toggleToUse == null)
                {

                    CreateUVToggle(data, materialForToggle);
                }
                else if (toggleToUse != null)
                {

                    IntegrateWithVRCFuryToggle(data, materialForToggle, toggleToUse);
                }
            }

            data.SetBuildStats(hiddenCount, "UV Discard");

            if (data.debugMode)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Applied UV discard mode. Hidden {hiddenCount} vertices.", data);
            }
        }

        private void ApplyMeshDeletionMode(AutoBodyHiderData data, Mesh originalMesh, bool[] hiddenVertices, int hiddenCount)
        {
            Mesh modifiedMesh = MeshDeleter.DeleteHiddenVertices(originalMesh, hiddenVertices);

            data.targetBodyMesh.sharedMesh = modifiedMesh;

            data.SetBuildStats(hiddenCount, "Mesh Deletion");

            if (data.debugMode)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Applied mesh deletion mode. Removed {hiddenCount} vertices.", data);
                Debug.Log($"[AutoBodyHiderProcessor] New mesh has {modifiedMesh.vertexCount} vertices (was {originalMesh.vertexCount})");
            }
        }

        private bool ShouldShowProgress(AutoBodyHiderData data, int vertexCount)
        {
            switch (data.detectionMethod)
            {
                case DetectionMethod.Smart:
                    return true;
                case DetectionMethod.Raycast:
                case DetectionMethod.Hybrid:
                    return vertexCount > 5000;
                default:
                    return false;
            }
        }

        private HashSet<int> GetAffectedMaterialIndices(Mesh mesh, IEnumerable<bool[]> hiddenVertexSets)
        {
            HashSet<int> affected = new HashSet<int>();
            if (mesh == null || hiddenVertexSets == null)
            {
                return affected;
            }

            bool[] combinedHidden = new bool[mesh.vertexCount];
            foreach (var hiddenVertices in hiddenVertexSets)
            {
                if (hiddenVertices == null)
                {
                    continue;
                }

                int count = Mathf.Min(hiddenVertices.Length, combinedHidden.Length);
                for (int i = 0; i < count; i++)
                {
                    combinedHidden[i] |= hiddenVertices[i];
                }
            }

            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                int[] triangles = mesh.GetTriangles(subMesh);
                for (int i = 0; i < triangles.Length; i++)
                {
                    int vertexIndex = triangles[i];
                    if (vertexIndex >= 0 && vertexIndex < combinedHidden.Length && combinedHidden[vertexIndex])
                    {
                        affected.Add(subMesh);
                        break;
                    }
                }
            }

            return affected;
        }

        private bool IsSameMaterialAsset(Material candidate, Material target)
        {
            if (candidate == null || target == null)
            {
                return false;
            }

            if (candidate == target)
            {
                return true;
            }

            string candidatePath = AssetDatabase.GetAssetPath(candidate);
            string targetPath = AssetDatabase.GetAssetPath(target);
            return !string.IsNullOrEmpty(candidatePath) &&
                   !string.IsNullOrEmpty(targetPath) &&
                   candidatePath == targetPath;
        }

        private Material CreateUploadMaterialCopy(Material source, string suffix, UnityEngine.Object context)
        {
            Material copy = UnityEngine.Object.Instantiate(source);
            copy.name = SanitizeAssetName($"{source.name}_{suffix}_YUCP");

            EnsureGeneratedMaterialsFolder();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{GeneratedMaterialsFolder}/{copy.name}.mat");
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.ImportAsset(path);

            Debug.Log($"[AutoBodyHiderProcessor] Created upload material copy '{copy.name}' at '{path}' so shader optimizers can lock it for VRChat upload.", context);
            return copy;
        }

        private static void EnsureGeneratedMaterialsFolder()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedMaterialsRoot))
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(GeneratedMaterialsRoot));
            }

            if (!AssetDatabase.IsValidFolder(GeneratedMaterialsFolder))
            {
                AssetDatabase.CreateFolder(GeneratedMaterialsRoot, Path.GetFileName(GeneratedMaterialsFolder));
            }
        }

        private static string SanitizeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "AutoBodyHiderMaterial_YUCP";
            }

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return value;
        }

        private void ConfigurePoiyomiForToggle(Material material, AutoBodyHiderData data, Mesh mesh = null)
        {
            string shaderNameLower = material.shader.name.ToLower();


            if (shaderNameLower.Contains("poiyomi"))
            {
                material.EnableKeyword("POI_UDIMDISCARD");
                if (material.HasProperty("_EnableUDIMDiscardOptions"))
                {
                    material.SetFloat("_EnableUDIMDiscardOptions", 1f);
                }
            }
            else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("wffs"))
            {

                if (material.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                {

                    float currentValue = material.GetFloat("_WFFS_FEATURES_UVDISCARD");
                    Debug.Log($"[AutoBodyHiderProcessor] FastFur material '{material.name}' - _WFFS_FEATURES_UVDISCARD current value: {currentValue}");

                    if (currentValue < 0.5f)
                    {


                        material.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                        if (material.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                        {
                            try { material.SetInt("_WFFS_FEATURES_UVDISCARD", 1); } catch { }
                        }


                        material.EnableKeyword("WFFS_FEATURES_UVDISCARD");


                        UnityEditor.EditorUtility.SetDirty(material);
                        material.name = material.name;

                        float newValue = material.GetFloat("_WFFS_FEATURES_UVDISCARD");
                        Debug.Log($"[AutoBodyHiderProcessor] Installed UV Discard module on FastFur material '{material.name}' (was {currentValue}, now {newValue}, keyword enabled: {material.IsKeywordEnabled("WFFS_FEATURES_UVDISCARD")})");
                    }
                    else
                    {

                        material.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                        Debug.Log($"[AutoBodyHiderProcessor] FastFur material '{material.name}' - UV Discard module already installed, ensuring keyword is enabled");
                    }
                }
                else
                {
                    Debug.LogWarning($"[AutoBodyHiderProcessor] Material '{material.name}' missing '_WFFS_FEATURES_UVDISCARD' property. This shader may not support UV Discard.");
                }


                if (material.HasProperty("_EnableUDIMDiscardOptions"))
                {

                    material.SetFloat("_EnableUDIMDiscardOptions", 1f);
                    try { material.SetInt("_EnableUDIMDiscardOptions", 1); } catch { }

                    UnityEditor.EditorUtility.SetDirty(material);
                    material.name = material.name;

                    float enabledValue = material.GetFloat("_EnableUDIMDiscardOptions");
                    Debug.Log($"[AutoBodyHiderProcessor] Enabled UV Discard on FastFur material '{material.name}' - _EnableUDIMDiscardOptions value: {enabledValue}");
                }
                else
                {
                    Debug.LogWarning($"[AutoBodyHiderProcessor] Material '{material.name}' missing '_EnableUDIMDiscardOptions' property after installing module. Module may not be properly installed.");
                }
            }

            if (material.HasProperty("_UDIMDiscardMode"))
            {
                material.SetFloat("_UDIMDiscardMode", 0f);
            }


            if (mesh == null && data.targetBodyMesh != null)
            {
                mesh = data.targetBodyMesh.sharedMesh;
            }
            int uvChannel = UVManipulator.GetEffectiveUVChannel(data, mesh);
            if (material.HasProperty("_UDIMDiscardUV"))
            {
                material.SetFloat("_UDIMDiscardUV", uvChannel);
            }
            else if (material.HasProperty("_UDIMDiscardUVChannel"))
            {
                material.SetFloat("_UDIMDiscardUVChannel", uvChannel);
            }

            string tilePropertyName = $"_UDIMDiscardRow{data.uvDiscardRow}_{data.uvDiscardColumn}";
            if (material.HasProperty(tilePropertyName))
            {



                float baseValue = 0f;
                material.SetFloat(tilePropertyName, baseValue);
                material.SetOverrideTag(tilePropertyName + "Animated", "1");

                string shaderName = UVManipulator.GetShaderDisplayName(material);
                Debug.Log($"[AutoBodyHider] Configured {shaderName} for toggle: {tilePropertyName} = {baseValue} (OFF state), Animated tag set", data);
            }
            else
            {
                Debug.LogError($"[AutoBodyHider] Property {tilePropertyName} not found on material!", data);
            }
        }

        private void IntegrateWithVRCFuryToggle(AutoBodyHiderData data, Material poiyomiMaterial, Component vrcFuryComponent)
        {
            try
            {
                AnimationClip animation = CreateUVToggleAnimation(data, poiyomiMaterial);
                if (animation == null) return;
                VRCFuryHelper.AddAnimationToToggle(vrcFuryComponent, animation);
                Debug.Log("[AutoBodyHider] Added UV discard animation to existing VRCFury Toggle", data);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AutoBodyHider] Error integrating with VRCFury Toggle: {e.Message}", data);
                Debug.LogException(e);
            }
        }


        private void CreateUVToggle(AutoBodyHiderData data, Material poiyomiMaterial)
        {
            try
            {



                if (data.toggleType == ToggleType.ObjectToggle && !data.toggleDefaultOn)
                {
                    data.gameObject.SetActive(false);
                }

                var toggle = FuryComponents.CreateToggle(data.gameObject);

                bool hasMenuPath = !string.IsNullOrEmpty(data.toggleMenuPath);
                bool hasCustomParam = !string.IsNullOrEmpty(data.toggleParameterName);

                if (hasMenuPath)
                {
                    toggle.SetMenuPath(data.toggleMenuPath);
                }
                else if (data.toggleSynced && hasCustomParam)
                {

                    var toggleType = toggle.GetType();
                    var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var toggleModel = cField.GetValue(toggle);
                    var addMenuItemField = toggleModel.GetType().GetField("addMenuItem", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (addMenuItemField != null)
                    {
                        addMenuItemField.SetValue(toggleModel, false);
                        Debug.Log($"[AutoBodyHider] Parameter only mode - menu item disabled", data);
                    }
                }

                if (data.toggleSaved)
                    toggle.SetSaved();

                if (data.toggleDefaultOn)
                    toggle.SetDefaultOn();

                if (data.toggleSlider)
                    toggle.SetSlider();

                if (data.toggleExclusiveOffState)
                    toggle.SetExclusiveOffState();


                if (data.toggleSynced)
                {
                    if (hasCustomParam)
                        toggle.SetGlobalParameter(data.toggleParameterName);
                    else
                        toggle.SetGlobalParameter("");
                }


                if (data.toggleHoldButton || data.toggleEnableExclusiveTag || data.toggleEnableIcon)
                {
                    var toggleType = toggle.GetType();
                    var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var toggleModel = cField.GetValue(toggle);

                    if (data.toggleHoldButton)
                    {
                        var holdButtonField = toggleModel.GetType().GetField("holdButton", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (holdButtonField != null) holdButtonField.SetValue(toggleModel, true);
                    }

                    if (data.toggleEnableExclusiveTag && !string.IsNullOrEmpty(data.toggleExclusiveTag))
                    {
                        toggle.AddExclusiveTag(data.toggleExclusiveTag);
                    }

                    if (data.toggleEnableIcon && data.toggleIcon != null)
                    {
                        var enableIconField = toggleModel.GetType().GetField("enableIcon", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        var iconField = toggleModel.GetType().GetField("icon", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (enableIconField != null && iconField != null)
                        {
                            enableIconField.SetValue(toggleModel, true);

                            var guidTexture2dType = iconField.FieldType;
                            var implicitOp = guidTexture2dType.GetMethod("op_Implicit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new Type[] { typeof(Texture2D) }, null);
                            if (implicitOp != null)
                            {
                                var guidTextureInstance = implicitOp.Invoke(null, new object[] { data.toggleIcon });
                                iconField.SetValue(toggleModel, guidTextureInstance);
                            }
                        }
                    }
                }

                var actions = toggle.GetActions();

                if (data.toggleType == ToggleType.ObjectToggle)
                    actions.AddTurnOn(data.gameObject);

                AnimationClip toggleAnimation = CreateUVToggleAnimation(data, poiyomiMaterial);

                if (toggleAnimation != null)
                {
                    actions.AddAnimationClip(toggleAnimation);
                }
                else
                {
                    Debug.LogError($"[AutoBodyHiderProcessor] Failed to create UV toggle animation", data);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoBodyHiderProcessor] Error creating UV toggle: {ex.Message}", data);
                Debug.LogException(ex);
            }
        }

        private AnimationClip CreateUVToggleAnimation(AutoBodyHiderData data, Material poiyomiMaterial)
        {
            AnimationClip clip = new AnimationClip();
            clip.name = $"UV_Discard_Toggle_{data.gameObject.name}";

            string rendererPath = AnimationUtility.CalculateTransformPath(data.targetBodyMesh.transform, data.transform.root);

            Material[] materials = data.targetBodyMesh.sharedMaterials;
            int materialIndex = -1;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == poiyomiMaterial)
                {
                    materialIndex = i;
                    break;
                }
            }

            if (materialIndex == -1)
            {
                Debug.LogError($"[AutoBodyHiderProcessor] Could not find material on renderer", data);
                return null;
            }



            float animValue = 1f;

            AnimationCurve discardCurve = new AnimationCurve();
            discardCurve.AddKey(0f, animValue);
            discardCurve.AddKey(1f/60f, animValue);


            List<string> tilesToAnimate = new List<string>();


            string individualTile = $"_UDIMDiscardRow{data.uvDiscardRow}_{data.uvDiscardColumn}";
            tilesToAnimate.Add(individualTile);


            var coordGroup = AutoBodyHiderCoordinator.CoordinatedGroups.ContainsKey(data.targetBodyMesh)
                           ? AutoBodyHiderCoordinator.CoordinatedGroups[data.targetBodyMesh]
                           : null;

            if (coordGroup != null)
            {
                foreach (var overlap in coordGroup.overlapRegions)
                {
                    if (overlap.involvedClothing.Contains(data))
                    {
                        var tile = coordGroup.overlapTiles[overlap];
                        string overlapTileName = $"_UDIMDiscardRow{tile.row}_{tile.col}";
                        tilesToAnimate.Add(overlapTileName);
                        Debug.Log($"[AutoBodyHider] Toggle for '{data.name}' will also control overlap tile ({tile.row},{tile.col}) shared with {string.Join(",", overlap.involvedClothing.Where(c => c != data).Select(c => c.name))}", data);
                    }
                }
            }


            foreach (var tilePropertyName in tilesToAnimate)
            {
                if (!poiyomiMaterial.HasProperty(tilePropertyName))
                {
                    Debug.LogWarning($"[AutoBodyHiderProcessor] Material doesn't have '{tilePropertyName}' property", data);
                    continue;
                }

                string propertyPath = $"material.{tilePropertyName}";

                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    rendererPath,
                    typeof(SkinnedMeshRenderer),
                    propertyPath
                );

                AnimationUtility.SetEditorCurve(clip, binding, discardCurve);
                Debug.Log($"[AutoBodyHider] Added animation curve for tile '{tilePropertyName}'", data);
            }


            if (data.debugSaveAnimation)
            {
                string debugPath = $"Assets/Generated/YUCP_UV_Toggle_{data.gameObject.name}.anim";
                AssetDatabase.CreateAsset(clip, debugPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[AutoBodyHider] Animation saved to: {debugPath}", data);
            }

            Debug.Log($"[AutoBodyHider] === Animation Clip Details ===", data);
            Debug.Log($"[AutoBodyHider] Clip name: {clip.name}", data);
            Debug.Log($"[AutoBodyHider] Renderer path: '{rendererPath}'", data);
            Debug.Log($"[AutoBodyHider] Material index: {materialIndex}", data);
            Debug.Log($"[AutoBodyHider] Animating {tilesToAnimate.Count} tiles (individual + overlaps)", data);
            Debug.Log($"[AutoBodyHider] Animation value: {animValue} (discard ON)", data);
            Debug.Log($"[AutoBodyHider] Total bindings: {AnimationUtility.GetCurveBindings(clip).Length}", data);

            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                Debug.Log($"[AutoBodyHider]   Binding: path='{b.path}' | type={b.type.Name} | property='{b.propertyName}' | value={curve.keys[0].value}", data);
            }

            Debug.Log($"[AutoBodyHider] Clip being added to toggle on GameObject: {data.gameObject.name}", data);
            Debug.Log($"[AutoBodyHider] ==============================", data);

            return clip;
        }




        private bool[] DetectHiddenVerticesForData(AutoBodyHiderData data, Mesh bodyMesh)
        {
            Vector3[] vertices = bodyMesh.vertices;


            var clothingMeshes = data.GetClothingMeshes();
            Mesh clothingMesh = (clothingMeshes != null && clothingMeshes.Length > 0) ? clothingMeshes[0].sharedMesh : null;

            bool[] hiddenVertices;
            int hiddenCount;

            if (DetectionCache.TryGetCachedResult(data, bodyMesh, clothingMesh, out hiddenVertices, out hiddenCount))
            {
                if (data.debugMode)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Using cached result for '{data.name}': {hiddenCount} hidden vertices");
                }
                return hiddenVertices;
            }

            Vector3[] normals = bodyMesh.normals;
            List<Vector2> uv0List = new List<Vector2>();
            bodyMesh.GetUVs(0, uv0List);
            Vector2[] uv0 = uv0List.ToArray();

            if (data.debugMode)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Detecting hidden vertices for '{data.name}' ({vertices.Length} vertices)");
            }

            hiddenVertices = VertexDetection.DetectHiddenVertices(data, vertices, normals, uv0, null);

            hiddenCount = 0;
            foreach (bool hidden in hiddenVertices)
            {
                if (hidden) hiddenCount++;
            }

            if (data.debugMode)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Detected {hiddenCount} hidden vertices for '{data.name}' ({(hiddenCount * 100f / vertices.Length):F1}%)");
            }

            DetectionCache.CacheResult(data, bodyMesh, clothingMesh, hiddenVertices, hiddenCount);

            return hiddenVertices;
        }

        private bool[] ApplyPostProcessing(AutoBodyHiderData data, Vector3[] vertices, bool[] hiddenVertices, ref int hiddenCount)
        {
            if (data.mirrorSymmetry)
            {
                hiddenVertices = VertexDetection.ApplySymmetryMirror(hiddenVertices, vertices);

                hiddenCount = 0;
                foreach (bool hidden in hiddenVertices)
                {
                    if (hidden) hiddenCount++;
                }

                if (data.debugMode)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] After symmetry: {hiddenCount} hidden vertices for '{data.name}'");
                }
            }

            if (data.safetyMargin > 0.0001f)
            {
                hiddenVertices = VertexDetection.ApplySafetyMargin(hiddenVertices, vertices, data);

                hiddenCount = 0;
                foreach (bool hidden in hiddenVertices)
                {
                    if (hidden) hiddenCount++;
                }

                if (data.debugMode)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] After safety margin: {hiddenCount} hidden vertices for '{data.name}'");
                }
            }

            return hiddenVertices;
        }

        private Mesh MergeUVDiscards(BodyMeshProcessing group)
        {

            Mesh baseMesh = UnityEngine.Object.Instantiate(group.originalMesh);
            baseMesh.name = group.originalMesh.name + "_MultiUV";


            int targetUVChannel = 1;
            if (group.uvComponents.Count > 0)
            {
                targetUVChannel = UVManipulator.GetEffectiveUVChannel(group.uvComponents[0], group.originalMesh);
            }


            List<Vector2> uv0List = new List<Vector2>();
            baseMesh.GetUVs(0, uv0List);
            Vector2[] uv0 = uv0List.ToArray();


            List<Vector2> targetUVChannelList = new List<Vector2>();
            baseMesh.GetUVs(targetUVChannel, targetUVChannelList);
            Vector2[] targetUV;

            if (targetUVChannelList.Count == 0 || targetUVChannelList.Count != uv0.Length)
            {

                targetUV = new Vector2[uv0.Length];
                System.Array.Copy(uv0, targetUV, uv0.Length);
            }
            else
            {
                targetUV = targetUVChannelList.ToArray();
            }


            List<AutoBodyHiderData>[] vertexOwners = new List<AutoBodyHiderData>[uv0.Length];
            for (int i = 0; i < vertexOwners.Length; i++)
            {
                vertexOwners[i] = new List<AutoBodyHiderData>();
            }


            foreach (var kvp in group.hiddenVerticesMap)
            {
                var data = kvp.Key;
                var hiddenVertices = kvp.Value;

                for (int i = 0; i < hiddenVertices.Length; i++)
                {
                    if (hiddenVertices[i])
                    {
                        vertexOwners[i].Add(data);
                    }
                }
            }


            var coordGroup = AutoBodyHiderCoordinator.CoordinatedGroups.ContainsKey(group.bodyMesh)
                           ? AutoBodyHiderCoordinator.CoordinatedGroups[group.bodyMesh]
                           : null;


            for (int i = 0; i < targetUV.Length; i++)
            {
                if (vertexOwners[i].Count == 0)
                {

                    continue;
                }
                else if (vertexOwners[i].Count == 1)
                {

                    var data = vertexOwners[i][0];
                    float uOffset = data.uvDiscardColumn;
                    float vOffset = data.uvDiscardRow;
                    targetUV[i] = new Vector2(targetUV[i].x + uOffset, targetUV[i].y + vOffset);
                }
                else
                {

                    if (coordGroup != null)
                    {
                        var overlap = FindOverlapRegion(coordGroup, vertexOwners[i]);
                        if (overlap != null && coordGroup.overlapTiles.ContainsKey(overlap))
                        {
                            var tile = coordGroup.overlapTiles[overlap];
                            float uOffset = tile.col;
                            float vOffset = tile.row;
                            targetUV[i] = new Vector2(targetUV[i].x + uOffset, targetUV[i].y + vOffset);
                        }
                        else
                        {

                            var data = vertexOwners[i][0];
                            float uOffset = data.uvDiscardColumn;
                            float vOffset = data.uvDiscardRow;
                            targetUV[i] = new Vector2(targetUV[i].x + uOffset, targetUV[i].y + vOffset);

                            Debug.LogWarning($"[AutoBodyHiderProcessor] No overlap tile found for vertex {i} covered by {vertexOwners[i].Count} pieces. Using first piece's tile.");
                        }
                    }
                }
            }


            List<Vector2> finalUVList = new List<Vector2>(targetUV);
            baseMesh.SetUVs(targetUVChannel, finalUVList);


            int[] tileCounts = new int[16];
            foreach (var kvp in group.hiddenVerticesMap)
            {
                var data = kvp.Key;
                int count = kvp.Value.Count(h => h);
                Debug.Log($"[AutoBodyHiderProcessor] '{data.name}' covers {count} vertices (tile {data.uvDiscardRow},{data.uvDiscardColumn})");
            }

            return baseMesh;
        }

        /// <summary>
        /// Detect ACTUAL overlaps by comparing hidden vertex arrays
        /// Creates overlap tiles when clothing pieces share vertices
        /// </summary>
        private void DetectAndAssignActualOverlaps(BodyMeshProcessing group)
        {
            var coordGroup = AutoBodyHiderCoordinator.CoordinatedGroups.ContainsKey(group.bodyMesh)
                           ? AutoBodyHiderCoordinator.CoordinatedGroups[group.bodyMesh]
                           : null;

            if (coordGroup == null)
            {
                Debug.LogWarning("[AutoBodyHiderProcessor] No coordinator data found for overlap detection");
                return;
            }

            var validPieces = group.hiddenVerticesMap.Keys.ToList();
            int potentialOverlaps = validPieces.Count * (validPieces.Count - 1) / 2;
            int actualOverlaps = 0;
            int optimizationSkips = 0;


            bool optimizationEnabled = validPieces.Any(p => p.optimizeTileUsage);

            Debug.Log($"[AutoBodyHiderProcessor] Analyzing {potentialOverlaps} potential overlaps between {validPieces.Count} clothing pieces (Optimization: {optimizationEnabled})...");


            bool[] claimedVertices = new bool[group.originalMesh.vertexCount];



            if (optimizationEnabled)
            {

                foreach (var data in validPieces)
                {
                    if (!data.optimizeTileUsage) continue;

                    var hiddenVertices = group.hiddenVerticesMap[data];
                    for (int v = 0; v < Math.Min(hiddenVertices.Length, claimedVertices.Length); v++)
                    {
                        if (hiddenVertices[v])
                        {
                            claimedVertices[v] = true;
                        }
                    }
                }
            }


            for (int i = 0; i < validPieces.Count; i++)
            {
                for (int j = i + 1; j < validPieces.Count; j++)
                {
                    var piece1 = validPieces[i];
                    var piece2 = validPieces[j];

                    var hidden1 = group.hiddenVerticesMap[piece1];
                    var hidden2 = group.hiddenVerticesMap[piece2];


                    int sharedCount = 0;
                    int minLength = Math.Min(hidden1.Length, hidden2.Length);

                    for (int v = 0; v < minLength; v++)
                    {
                        if (hidden1[v] && hidden2[v])
                        {
                            sharedCount++;
                        }
                    }


                    if (optimizationEnabled && sharedCount > 0)
                    {

                        if (!piece2.createToggle)
                        {
                            int piece2TotalCoverage = hidden2.Count(h => h);
                            float coverageRatio = (float)sharedCount / piece2TotalCoverage;


                            if (coverageRatio >= 0.95f)
                            {
                                Debug.Log($"[AutoBodyHiderProcessor] ⚡ Skipping overlap tile for '{piece1.name}' + '{piece2.name}' " +
                                         $"({coverageRatio:P0} overlap, piece2 has no toggle, fully covered by piece1)");
                                optimizationSkips++;
                                continue;
                            }
                        }


                        if (!piece1.createToggle)
                        {
                            int piece1TotalCoverage = hidden1.Count(h => h);
                            float coverageRatio = (float)sharedCount / piece1TotalCoverage;

                            if (coverageRatio >= 0.95f)
                            {
                                Debug.Log($"[AutoBodyHiderProcessor] ⚡ Skipping overlap tile for '{piece1.name}' + '{piece2.name}' " +
                                         $"({coverageRatio:P0} overlap, piece1 has no toggle, fully covered by piece2)");
                                optimizationSkips++;
                                continue;
                            }
                        }
                    }


                    if (sharedCount > 0)
                    {

                        while (coordGroup.usedTiles.Contains((coordGroup.nextAvailableRow, coordGroup.nextAvailableCol)))
                        {
                            coordGroup.nextAvailableCol++;
                            if (coordGroup.nextAvailableCol >= 4)
                            {
                                coordGroup.nextAvailableCol = 0;
                                coordGroup.nextAvailableRow++;
                                if (coordGroup.nextAvailableRow >= 4)
                                {
                                    Debug.LogWarning($"[AutoBodyHiderProcessor] Ran out of tiles for overlaps! " +
                                                   $"Overlap between '{piece1.name}' and '{piece2.name}' ({sharedCount} shared vertices) will not be handled optimally.");
                                    return;
                                }
                            }
                        }

                        var tile = (coordGroup.nextAvailableRow, coordGroup.nextAvailableCol);
                        coordGroup.usedTiles.Add(tile);

                        var overlap = new AutoBodyHiderCoordinator.OverlapRegion(new List<AutoBodyHiderData> { piece1, piece2 });
                        coordGroup.overlapRegions.Add(overlap);
                        coordGroup.overlapTiles[overlap] = tile;

                        actualOverlaps++;

                        Debug.Log($"[AutoBodyHiderProcessor] ✓ REAL overlap detected: '{piece1.name}' + '{piece2.name}' " +
                                 $"share {sharedCount} vertices → tile ({tile.Item1},{tile.Item2})");


                        coordGroup.nextAvailableCol++;
                        if (coordGroup.nextAvailableCol >= 4)
                        {
                            coordGroup.nextAvailableCol = 0;
                            coordGroup.nextAvailableRow++;
                        }
                    }
                    else
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] ✗ No overlap: '{piece1.name}' + '{piece2.name}' (0 shared vertices, skipped)");
                    }
                }
            }

            Debug.Log($"[AutoBodyHiderProcessor] Overlap detection complete: " +
                     $"{actualOverlaps} real overlaps created, {optimizationSkips} optimized away, " +
                     $"{potentialOverlaps - actualOverlaps - optimizationSkips} naturally skipped (no shared vertices). " +
                     $"Total tiles used: {coordGroup.assignedTiles.Count} individual + {actualOverlaps} overlap = " +
                     $"{coordGroup.assignedTiles.Count + actualOverlaps} tiles");
        }

        /// <summary>
        /// Find the overlap region that matches the given set of clothing pieces
        /// </summary>
        private AutoBodyHiderCoordinator.OverlapRegion FindOverlapRegion(AutoBodyHiderCoordinator.BodyMeshGroup group, List<AutoBodyHiderData> clothingPieces)
        {
            var testRegion = new AutoBodyHiderCoordinator.OverlapRegion(clothingPieces);

            foreach (var overlap in group.overlapRegions)
            {
                if (overlap.Equals(testRegion))
                {
                    return overlap;
                }
            }

            return null;
        }

        private void ConfigureMaterialsForMultipleUV(BodyMeshProcessing group)
        {
            Material[] materials = group.bodyMesh.sharedMaterials;
            Material poiyomiMaterial = null;
            HashSet<int> affectedMaterialIndices = GetAffectedMaterialIndices(group.originalMesh, group.hiddenVerticesMap.Values);

            Debug.Log($"[AutoBodyHiderProcessor] ConfigureMaterialsForMultipleUV - Searching for compatible material on '{group.bodyMesh.name}'");
            Debug.Log($"[AutoBodyHiderProcessor] Body mesh has {materials.Length} materials:");
            Debug.Log($"[AutoBodyHiderProcessor] Hidden vertices affect material slots: {string.Join(", ", affectedMaterialIndices.OrderBy(i => i))}");


            List<Material> targetMaterials = new List<Material>();
            foreach (var data in group.uvComponents)
            {
                if (data.targetMaterials != null && data.targetMaterials.Length > 0)
                {
                    foreach (var mat in data.targetMaterials)
                    {
                        if (mat != null && !targetMaterials.Contains(mat))
                        {
                            targetMaterials.Add(mat);
                        }
                    }
                }
            }

            if (targetMaterials.Count > 0)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Using {targetMaterials.Count} target material(s) from components");
            }


            List<int> poiyomiMaterialIndices = new List<int>();
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                {
                    continue;
                }

                if (!affectedMaterialIndices.Contains(i))
                {
                    if (targetMaterials.Count > 0)
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] Material at index {i}: '{materials[i].name}' skipped because no hidden vertices use this material slot");
                    }
                    continue;
                }

                bool shouldInclude = false;
                if (targetMaterials.Count > 0)
                {

                    bool isTargetMaterial = targetMaterials.Any(targetMat => IsSameMaterialAsset(materials[i], targetMat));

                    if (isTargetMaterial && UVManipulator.IsPoiyomiWithUVSupport(materials[i]))
                    {
                        shouldInclude = true;
                    }
                }
                else
                {

                    if (UVManipulator.IsPoiyomiWithUVSupport(materials[i]))
                    {
                        shouldInclude = true;
                    }
                }

                if (shouldInclude)
                {
                    if (poiyomiMaterial == null)
                    {
                        poiyomiMaterial = materials[i];
                    }
                    poiyomiMaterialIndices.Add(i);
                    Debug.Log($"[AutoBodyHiderProcessor] Found compatible material at index {i}: '{materials[i].name}' (Shader: '{materials[i].shader.name}')");
                }
                else
                {

                    if (targetMaterials.Count > 0)
                    {
                        bool isCompatible = UVManipulator.IsPoiyomiWithUVSupport(materials[i]);
                        Debug.Log($"[AutoBodyHiderProcessor] Material at index {i}: '{materials[i].name}' (Shader: '{materials[i].shader.name}') - Compatible: {isCompatible}");
                    }
                }
            }

            if (poiyomiMaterialIndices.Count == 0)
            {
                Debug.LogError($"[AutoBodyHiderProcessor] No Poiyomi or FastFur material found on body mesh '{group.bodyMesh.name}'. " +
                             $"UV Discard mode requires a Poiyomi or FastFur shader with UV support. " +
                             $"Please use Mesh Deletion mode instead, or add a compatible shader to the body mesh.");
                return;
            }

            string shaderName = poiyomiMaterial != null ? UVManipulator.GetShaderDisplayName(poiyomiMaterial) : "Poiyomi/FastFur";
            Debug.Log($"[AutoBodyHiderProcessor] Configuring multi-UV on {poiyomiMaterialIndices.Count} material(s)");
            List<Material> configuredMaterials = new List<Material>();


            int uvChannel = 1;
            if (group.uvComponents.Count > 0)
            {
                uvChannel = UVManipulator.GetEffectiveUVChannel(group.uvComponents[0], group.originalMesh);
            }


            foreach (int materialIndex in poiyomiMaterialIndices)
            {
                Material materialToConfigure = materials[materialIndex];
                Material materialCopy = CreateUploadMaterialCopy(materialToConfigure, $"MultiUV_{materialIndex}", group.bodyMesh);
                ThryMaterialOptimizerBridge.UnlockIfNeeded(new[] { materialCopy }, group.bodyMesh);

                string shaderNameLower = materialCopy.shader.name.ToLower();
                Debug.Log($"[AutoBodyHiderProcessor] Configuring material '{materialCopy.name}' with shader '{materialCopy.shader.name}' (lowercase: '{shaderNameLower}')");


                if (shaderNameLower.Contains("poiyomi"))
                {
                    materialCopy.EnableKeyword("POI_UDIMDISCARD");

                    if (materialCopy.HasProperty("_EnableUDIMDiscardOptions"))
                    {
                        materialCopy.SetFloat("_EnableUDIMDiscardOptions", 1f);
                    }
                }
                else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("fast fur") || shaderNameLower.Contains("wffs") || shaderNameLower.Contains("warren"))
                {

                    if (materialCopy.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                    {

                        float currentValue = materialCopy.GetFloat("_WFFS_FEATURES_UVDISCARD");
                        Debug.Log($"[AutoBodyHiderProcessor] FastFur material '{materialCopy.name}' - _WFFS_FEATURES_UVDISCARD current value: {currentValue}");

                        if (currentValue < 0.5f)
                        {


                            materialCopy.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                            if (materialCopy.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                            {
                                try { materialCopy.SetInt("_WFFS_FEATURES_UVDISCARD", 1); } catch { }
                            }


                            materialCopy.EnableKeyword("WFFS_FEATURES_UVDISCARD");


                            UnityEditor.EditorUtility.SetDirty(materialCopy);
                            materialCopy.name = materialCopy.name;

                            float newValue = materialCopy.GetFloat("_WFFS_FEATURES_UVDISCARD");
                            Debug.Log($"[AutoBodyHiderProcessor] Installed UV Discard module on FastFur material '{materialCopy.name}' (was {currentValue}, now {newValue}, keyword enabled: {materialCopy.IsKeywordEnabled("WFFS_FEATURES_UVDISCARD")})");
                        }
                        else
                        {

                            materialCopy.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                            Debug.Log($"[AutoBodyHiderProcessor] FastFur material '{materialCopy.name}' - UV Discard module already installed, ensuring keyword is enabled");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[AutoBodyHiderProcessor] Material '{materialCopy.name}' missing '_WFFS_FEATURES_UVDISCARD' property. This shader may not support UV Discard.");
                    }


                    if (materialCopy.HasProperty("_EnableUDIMDiscardOptions"))
                    {

                        materialCopy.SetFloat("_EnableUDIMDiscardOptions", 1f);
                        try { materialCopy.SetInt("_EnableUDIMDiscardOptions", 1); } catch { }

                        UnityEditor.EditorUtility.SetDirty(materialCopy);
                        materialCopy.name = materialCopy.name;

                        float enabledValue = materialCopy.GetFloat("_EnableUDIMDiscardOptions");
                        Debug.Log($"[AutoBodyHiderProcessor] Enabled UV Discard on FastFur material '{materialCopy.name}' - _EnableUDIMDiscardOptions value: {enabledValue}");
                    }
                    else
                    {
                        Debug.LogWarning($"[AutoBodyHiderProcessor] Material '{materialCopy.name}' missing '_EnableUDIMDiscardOptions' property after installing module. Module may not be properly installed.");
                    }
                }


                if (materialCopy.HasProperty("_UDIMDiscardMode"))
                {
                    materialCopy.SetFloat("_UDIMDiscardMode", 0f);
                }

                if (materialCopy.HasProperty("_UDIMDiscardUV"))
                {
                    materialCopy.SetFloat("_UDIMDiscardUV", uvChannel);
                }
                else if (materialCopy.HasProperty("_UDIMDiscardUVChannel"))
                {

                    materialCopy.SetFloat("_UDIMDiscardUVChannel", uvChannel);
                }


                var coordGroup = AutoBodyHiderCoordinator.CoordinatedGroups.ContainsKey(group.bodyMesh)
                               ? AutoBodyHiderCoordinator.CoordinatedGroups[group.bodyMesh]
                               : null;


                foreach (var data in group.uvComponents)
                {
                    string tilePropertyName = $"_UDIMDiscardRow{data.uvDiscardRow}_{data.uvDiscardColumn}";

                    if (materialCopy.HasProperty(tilePropertyName))
                    {

                        bool hasToggle = data.createToggle || data.useExistingToggle;

                        if (hasToggle)
                        {

                            materialCopy.SetFloat(tilePropertyName, 0f);
                            materialCopy.SetOverrideTag(tilePropertyName + "Animated", "1");
                        }
                        else
                        {

                            materialCopy.SetFloat(tilePropertyName, 1f);
                        }

                        Debug.Log($"[AutoBodyHiderProcessor] Configured individual tile ({data.uvDiscardRow}, {data.uvDiscardColumn}) for '{data.name}' on material '{materialCopy.name}' (Toggle: {hasToggle})");
                    }
                    else
                    {
                        Debug.LogWarning($"[AutoBodyHiderProcessor] Property {tilePropertyName} not found on material '{materialCopy.name}'");
                    }
                }


                if (coordGroup != null && coordGroup.overlapRegions.Count > 0)
                {
                    foreach (var overlap in coordGroup.overlapRegions)
                    {
                        if (coordGroup.overlapTiles.ContainsKey(overlap))
                        {
                            var tile = coordGroup.overlapTiles[overlap];
                            string tilePropertyName = $"_UDIMDiscardRow{tile.row}_{tile.col}";

                            if (materialCopy.HasProperty(tilePropertyName))
                            {

                                bool anyHasToggle = overlap.involvedClothing.Any(c => c.createToggle || c.useExistingToggle);

                                if (anyHasToggle)
                                {

                                    materialCopy.SetFloat(tilePropertyName, 0f);
                                    materialCopy.SetOverrideTag(tilePropertyName + "Animated", "1");
                                }
                                else
                                {

                                    materialCopy.SetFloat(tilePropertyName, 1f);
                                }

                                Debug.Log($"[AutoBodyHiderProcessor] Configured overlap tile ({tile.row}, {tile.col}) for '{overlap.regionName}' on material '{materialCopy.name}' (Toggle: {anyHasToggle})");
                            }
                            else
                            {
                                Debug.LogWarning($"[AutoBodyHiderProcessor] Property {tilePropertyName} not found on material '{materialCopy.name}'");
                            }
                        }
                    }
                }


                materials[materialIndex] = materialCopy;
                configuredMaterials.Add(materialCopy);
                EditorUtility.SetDirty(materialCopy);
            }

            ThryMaterialOptimizerBridge.SyncAndLock(configuredMaterials, group.bodyMesh);

            group.bodyMesh.sharedMaterials = materials;
            EditorUtility.SetDirty(group.bodyMesh);
        }

        private void CreateUVToggleForComponent(AutoBodyHiderData data, BodyMeshProcessing group)
        {
            Material[] materials = group.bodyMesh.sharedMaterials;
            Material poiyomiMaterial = null;

            foreach (var mat in materials)
            {
                if (mat.name.Contains("MultiUV") && UVManipulator.IsPoiyomiWithUVSupport(mat))
                {
                    poiyomiMaterial = mat;
                    break;
                }
            }

            if (poiyomiMaterial == null)
            {
                Debug.LogError($"[AutoBodyHiderProcessor] Could not find multi-UV material for toggle on '{data.name}'");
                return;
            }


            Component toggleToUse = null;
            if (data.useExistingToggle && data.selectedToggle != null)
            {
                toggleToUse = data.selectedToggle;
            }
            else if (data.useExistingToggle)
            {
                toggleToUse = VRCFuryHelper.FindToggles(data.gameObject).FirstOrDefault();
            }

            if (toggleToUse != null)
            {
                IntegrateWithVRCFuryToggle(data, poiyomiMaterial, toggleToUse);
            }
            else if (data.createToggle)
            {
                CreateUVToggle(data, poiyomiMaterial);
            }
        }


        private string GetGameObjectPath(GameObject obj, GameObject root) =>
            obj == root ? obj.name : AnimationUtility.CalculateTransformPath(obj.transform, root.transform);

    }
}

