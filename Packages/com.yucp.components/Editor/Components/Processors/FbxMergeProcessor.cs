using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Build-time processor that merges one attachment renderer into a base skinned renderer,
    /// preserves blendshapes/material slots, remaps animation bindings, and optionally removes
    /// the original attachment object.
    /// </summary>
    public class FbxMergeProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 11;

        private sealed class AttachmentSource
        {
            public Mesh mesh;
            public Transform transform;
            public Material[] materials;
            public SkinnedMeshRenderer skinnedRenderer;
            public MeshRenderer meshRenderer;
            public GameObject sourceGameObject;
            public string displayName;
            public string rendererPath;
            public string objectPath;
        }

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var components = avatarRoot.GetComponentsInChildren<FbxMergeData>(true);
            foreach (var data in components)
            {
                try
                {
                    Process(data, avatarRoot);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Mesh Merger] Error processing '{data.name}': {ex.Message}", data);
                    Debug.LogException(ex, data);
                }
            }

            return true;
        }

        private void Process(FbxMergeData data, GameObject avatarRoot)
        {
            var warnings = new List<string>();
            int remappedCurveCount = 0;

            if (!ValidateData(data, avatarRoot, warnings))
            {
                data.SetBuildStats(0, 0, 0, 0, warnings.Count, warnings);
                return;
            }

            if (!TryResolveAttachmentSource(data, avatarRoot, out var source, warnings))
            {
                data.SetBuildStats(0, 0, 0, 0, warnings.Count, warnings);
                return;
            }

            var baseRenderer = data.baseRenderer;
            var baseMesh = baseRenderer.sharedMesh;
            if (baseMesh == null)
            {
                warnings.Add("Base renderer has no mesh.");
                data.SetBuildStats(0, 0, 0, 0, warnings.Count, warnings);
                return;
            }

            if (source.mesh == null)
            {
                warnings.Add("Attachment source has no mesh.");
                data.SetBuildStats(0, 0, 0, 0, warnings.Count, warnings);
                return;
            }

            if (source.transform == null)
            {
                warnings.Add("Attachment source transform is null.");
                data.SetBuildStats(0, 0, 0, 0, warnings.Count, warnings);
                return;
            }

            if (source.sourceGameObject == baseRenderer.gameObject)
            {
                warnings.Add("Attachment source resolved to the same GameObject as base renderer. Skipping merge.");
                data.SetBuildStats(0, 0, 0, 0, warnings.Count, warnings);
                return;
            }

            if (data.debugMode)
            {
                Debug.Log($"[Mesh Merger] Merging '{source.displayName}' into '{baseRenderer.name}'", data);
            }

            Vector3[] attachmentVertices = source.mesh.vertices;
            Vector3[] attachmentVerticesInBaseLocal = new Vector3[attachmentVertices.Length];
            for (int i = 0; i < attachmentVertices.Length; i++)
            {
                Vector3 world = source.transform.TransformPoint(attachmentVertices[i]);
                attachmentVerticesInBaseLocal[i] = baseRenderer.transform.InverseTransformPoint(world);
            }

            int[] baseTrianglesCombined = SkinnedMeshMerge.GetCombinedTriangles(baseMesh);
            var maps = ClosestSurfaceMapper.MapAttachmentVerticesToBaseSurface(
                baseRenderer,
                baseMesh,
                attachmentVerticesInBaseLocal,
                null,
                out int matchedCount);

            if (matchedCount == 0)
            {
                warnings.Add("No attachment vertices mapped to base surface. Attachment skinning may be inaccurate.");
            }

            var mergeResult = SkinnedMeshMerge.Merge(
                baseRenderer,
                source.mesh,
                source.transform,
                source.materials ?? Array.Empty<Material>(),
                maps,
                baseTrianglesCombined);

            if (mergeResult.mergedMesh == null)
            {
                warnings.Add("Merge failed: merged mesh was null.");
                data.SetBuildStats(0, 0, 0, 0, warnings.Count, warnings);
                return;
            }

            var blendshapeResult = MergedBlendshapeBuilder.MergeBlendshapes(
                baseMesh,
                source.mesh,
                mergeResult.mergedMesh,
                mergeResult.baseVertexCount,
                mergeResult.attachmentVertexStart,
                mergeResult.attachmentVertexCount,
                source.displayName,
                data.renameConflictingAttachmentBlendshapes,
                data.attachmentBlendshapePrefix,
                data.debugMode,
                data);

            warnings.AddRange(blendshapeResult.warnings);

            int uvChannel = FbxMergeToggleUvDiscardMapper.ResolveUvChannel(data, mergeResult.mergedMesh);
            FbxMergeToggleUvDiscardMapper.ApplyAttachmentTileToMergedMesh(
                mergeResult.mergedMesh,
                mergeResult.attachmentVertexStart,
                mergeResult.attachmentVertexCount,
                uvChannel,
                data.uvDiscardRow,
                data.uvDiscardColumn);

            baseRenderer.sharedMesh = mergeResult.mergedMesh;
            baseRenderer.sharedMaterials = mergeResult.mergedMaterials;
            EditorUtility.SetDirty(baseRenderer);

            bool hasVrcFuryToggle = HasVRCFuryToggleInHierarchy(source.sourceGameObject, avatarRoot);

            List<int> configuredUvMaterials = new List<int>();
            string scaleToZeroBlendshapeName = null;
            bool useScaleToZero = false;
            bool injectedVrcFury = false;

            if (data.remapRendererAndObjectOffToUvDiscard)
            {
                configuredUvMaterials = FbxMergeToggleUvDiscardMapper.ConfigureAttachmentMaterialsForUvDiscard(
                    baseRenderer,
                    mergeResult.baseSubmeshCount,
                    mergeResult.attachmentSubmeshCount,
                    uvChannel,
                    data.uvDiscardRow,
                    data.uvDiscardColumn,
                    data.debugMode,
                    data,
                    hasVrcFuryToggle);

                if (configuredUvMaterials.Count > 0)
                {
                    if (data.debugMode)
                    {
                        Debug.Log($"[Mesh Merger] {configuredUvMaterials.Count} attachment material(s) support UV discard. Using UV discard for toggle.", data);
                    }
                }
                else
                {
                    if (data.scaleToZeroFallback)
                    {
                        scaleToZeroBlendshapeName = CreateScaleToZeroBlendshape(
                            mergeResult.mergedMesh,
                            mergeResult.attachmentVertexStart,
                            mergeResult.attachmentVertexCount,
                            baseRenderer,
                            source.displayName);

                        if (scaleToZeroBlendshapeName != null)
                        {
                            useScaleToZero = true;

                            if (hasVrcFuryToggle)
                            {
                                int bsIndex = mergeResult.mergedMesh.GetBlendShapeIndex(scaleToZeroBlendshapeName);
                                if (bsIndex >= 0)
                                {
                                    baseRenderer.SetBlendShapeWeight(bsIndex, 100f);
                                    EditorUtility.SetDirty(baseRenderer);
                                }
                            }

                            Debug.Log($"[Mesh Merger] No UV-discard-compatible materials for '{source.displayName}'. Created scale-to-zero blendshape '{scaleToZeroBlendshapeName}' (VRCFury toggle: {hasVrcFuryToggle}).", data);
                        }
                        else
                        {
                            warnings.Add("No UV-discard-compatible materials found and scale-to-zero blendshape creation failed.");
                        }
                    }
                    else
                    {
                        warnings.Add("No UV-discard-compatible attachment materials found and scaleToZeroFallback is disabled.");
                    }
                }

                injectedVrcFury = TryInjectIntoVRCFuryToggles(
                    source.sourceGameObject,
                    avatarRoot,
                    baseRenderer,
                    configuredUvMaterials,
                    FbxMergeToggleUvDiscardMapper.GetTilePropertyName(data.uvDiscardRow, data.uvDiscardColumn),
                    useScaleToZero,
                    scaleToZeroBlendshapeName,
                    data.debugMode,
                    data,
                    warnings);

                if (data.debugMode)
                {
                    Debug.Log($"[Mesh Merger] VRCFury injection result: {injectedVrcFury} (useScaleToZero={useScaleToZero}, uvDiscardMats={configuredUvMaterials.Count})", data);
                }
            }

            if (data.remapAnimations)
            {
                string tilePropertyName = FbxMergeToggleUvDiscardMapper.GetTilePropertyName(data.uvDiscardRow, data.uvDiscardColumn);
                var baseContext = new FbxMergeAnimationRemapper.RemapContext
                {
                    avatarRoot = avatarRoot,
                    baseRendererPath = AnimationUtility.CalculateTransformPath(baseRenderer.transform, avatarRoot.transform),
                    attachmentName = source.displayName,
                    attachmentTransform = source.transform,
                    baseRenderer = baseRenderer,
                    mergedMesh = mergeResult.mergedMesh,
                    attachmentVertexStart = mergeResult.attachmentVertexStart,
                    attachmentVertexCount = mergeResult.attachmentVertexCount,
                    baseMaterialOffset = mergeResult.baseSubmeshCount,
                    attachmentBlendshapeNameMap = blendshapeResult.attachmentBlendshapeNameMap,
                    uvDiscardMaterialIndices = configuredUvMaterials,
                    uvDiscardTilePropertyName = tilePropertyName,
                    remapBlendshapeAnimations = data.remapBlendshapeAnimations,
                    remapMaterialAnimations = data.remapMaterialAnimations,
                    remapRendererAndObjectOffToUvDiscard = data.remapRendererAndObjectOffToUvDiscard,
                    useScaleToZeroFallback = useScaleToZero,
                    scaleToZeroBlendshapeName = scaleToZeroBlendshapeName,
                    debugMode = data.debugMode,
                    logContext = data
                };

                var directPaths = new HashSet<string>();
                if (!string.IsNullOrEmpty(source.rendererPath))
                {
                    directPaths.Add(source.rendererPath);
                }
                if (!string.IsNullOrEmpty(source.objectPath))
                {
                    directPaths.Add(source.objectPath);
                }

                foreach (var path in directPaths)
                {
                    var remapContext = baseContext;
                    remapContext.attachmentPath = path;
                    remapContext.isAncestorPath = false;
                    if (path != source.rendererPath)
                    {
                        remapContext.attachmentTransform = null;
                    }

                    var remapResult = FbxMergeAnimationRemapper.RemapAllAnimations(remapContext);
                    remappedCurveCount += remapResult.remappedCurveCount;
                    warnings.AddRange(remapResult.warnings);
                }

                if (data.remapRendererAndObjectOffToUvDiscard)
                {
                    var ancestorPaths = CollectAncestorPaths(source.sourceGameObject, avatarRoot, directPaths);
                    foreach (var path in ancestorPaths)
                    {
                        var remapContext = baseContext;
                        remapContext.attachmentPath = path;
                        remapContext.isAncestorPath = true;
                        remapContext.attachmentTransform = null;

                        var remapResult = FbxMergeAnimationRemapper.RemapAllAnimations(remapContext);
                        remappedCurveCount += remapResult.remappedCurveCount;
                        warnings.AddRange(remapResult.warnings);
                    }
                }
            }

            data.SetBuildStats(
                mergeResult.mergedMesh.vertexCount,
                mergeResult.mergedMesh.subMeshCount,
                mergeResult.mergedMesh.blendShapeCount,
                remappedCurveCount,
                warnings.Count,
                warnings);

            if (data.debugMode)
            {
                Debug.Log(
                    $"[Mesh Merger] Completed '{data.name}'. Verts: {mergeResult.mergedMesh.vertexCount}, " +
                    $"Submeshes: {mergeResult.mergedMesh.subMeshCount}, Blendshapes: {mergeResult.mergedMesh.blendShapeCount}, " +
                    $"Remapped Curves: {remappedCurveCount}, Warnings: {warnings.Count}",
                    data);
            }

            if (data.deleteAttachmentObjectAfterMerge && source.sourceGameObject != null && source.sourceGameObject != baseRenderer.gameObject)
            {
                bool hasToggleOnSource = injectedVrcFury && HasVRCFuryToggleOnObject(source.sourceGameObject);
                if (hasToggleOnSource)
                {
                    if (source.skinnedRenderer != null && source.skinnedRenderer != baseRenderer)
                    {
                        UnityEngine.Object.DestroyImmediate(source.skinnedRenderer);
                    }
                    if (source.meshRenderer != null)
                    {
                        UnityEngine.Object.DestroyImmediate(source.meshRenderer);
                    }
                    var meshFilter = source.sourceGameObject.GetComponent<MeshFilter>();
                    if (meshFilter != null)
                    {
                        UnityEngine.Object.DestroyImmediate(meshFilter);
                    }

                    if (data.debugMode)
                    {
                        Debug.Log($"[Mesh Merger] Stripped renderers from '{source.sourceGameObject.name}' instead of deleting (VRCFury Toggle preserved).", data);
                    }
                }
                else
                {
                    try
                    {
                        UnityEngine.Object.DestroyImmediate(source.sourceGameObject);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Mesh Merger] Failed to delete attachment object '{source.sourceGameObject.name}': {ex.Message}", data);
                    }
                }
            }
            else
            {
                if (source.skinnedRenderer != null && source.skinnedRenderer != baseRenderer)
                {
                    source.skinnedRenderer.enabled = false;
                }
                if (source.meshRenderer != null)
                {
                    source.meshRenderer.enabled = false;
                }
            }
        }

        private static bool HasVRCFuryToggleInHierarchy(GameObject attachmentObject, GameObject avatarRoot)
        {
            if (attachmentObject == null || avatarRoot == null)
            {
                return false;
            }

            Transform walk = attachmentObject.transform;
            while (walk != null && walk != avatarRoot.transform)
            {
                if (IsVRCFuryToggle(walk.gameObject))
                {
                    return true;
                }

                walk = walk.parent;
            }

            return false;
        }

        private static bool HasVRCFuryToggleOnObject(GameObject go)
        {
            return go != null && IsVRCFuryToggle(go);
        }

        private static bool IsVRCFuryToggle(GameObject go)
        {
            if (go == null) return false;

            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null || comp.GetType().Name != "VRCFury")
                {
                    continue;
                }

                var contentField = comp.GetType().GetField("content",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (contentField == null)
                {
                    continue;
                }

                var content = contentField.GetValue(comp);
                if (content != null && content.GetType().Name == "Toggle")
                {
                    return true;
                }
            }

            return false;
        }

        private static string CreateScaleToZeroBlendshape(
            Mesh mergedMesh,
            int attachmentVertexStart,
            int attachmentVertexCount,
            SkinnedMeshRenderer baseRenderer,
            string attachmentName)
        {
            Vector3[] verts = mergedMesh.vertices;
            if (verts == null || verts.Length == 0)
            {
                return null;
            }

            int start = Mathf.Max(0, attachmentVertexStart);
            int end = Mathf.Min(verts.Length, start + Mathf.Max(0, attachmentVertexCount));
            if (end <= start)
            {
                return null;
            }

            BoneWeight[] boneWeights = mergedMesh.boneWeights;
            Transform[] bones = baseRenderer.bones;
            Matrix4x4[] bindPoses = mergedMesh.bindposes;
            bool hasBoneData = boneWeights != null && boneWeights.Length == verts.Length
                            && bones != null && bones.Length > 0
                            && bindPoses != null && bindPoses.Length > 0;

            int targetBoneIndex = 0;

            if (hasBoneData)
            {
                float[] boneWeightSums = new float[bones.Length];
                for (int i = start; i < end; i++)
                {
                    var bw = boneWeights[i];
                    if (bw.boneIndex0 < boneWeightSums.Length) boneWeightSums[bw.boneIndex0] += bw.weight0;
                    if (bw.boneIndex1 < boneWeightSums.Length) boneWeightSums[bw.boneIndex1] += bw.weight1;
                    if (bw.boneIndex2 < boneWeightSums.Length) boneWeightSums[bw.boneIndex2] += bw.weight2;
                    if (bw.boneIndex3 < boneWeightSums.Length) boneWeightSums[bw.boneIndex3] += bw.weight3;
                }

                float maxWeight = 0f;
                for (int i = 0; i < boneWeightSums.Length; i++)
                {
                    if (boneWeightSums[i] > maxWeight)
                    {
                        maxWeight = boneWeightSums[i];
                        targetBoneIndex = i;
                    }
                }

                var singleBone = new BoneWeight
                {
                    boneIndex0 = targetBoneIndex,
                    weight0 = 1f,
                    boneIndex1 = 0,
                    weight1 = 0f,
                    boneIndex2 = 0,
                    weight2 = 0f,
                    boneIndex3 = 0,
                    weight3 = 0f
                };
                for (int i = start; i < end; i++)
                {
                    boneWeights[i] = singleBone;
                }
                mergedMesh.boneWeights = boneWeights;
            }

            Vector3 collapsePoint;
            if (hasBoneData && targetBoneIndex < bindPoses.Length)
            {
                collapsePoint = bindPoses[targetBoneIndex].inverse.MultiplyPoint3x4(Vector3.zero);
            }
            else
            {
                collapsePoint = baseRenderer.transform.InverseTransformPoint(baseRenderer.transform.position);
            }

            Vector3[] deltaVertices = new Vector3[verts.Length];
            Vector3[] deltaNormals = new Vector3[verts.Length];
            for (int i = start; i < end; i++)
            {
                deltaVertices[i] = collapsePoint - verts[i];
            }

            string safeName = SanitizeBlendshapeName($"__YUCP_FbxMerge_ScaleToZero_{attachmentName}");
            string candidate = safeName;
            int suffix = 0;
            while (mergedMesh.GetBlendShapeIndex(candidate) >= 0)
            {
                suffix++;
                candidate = safeName + "_" + suffix;
            }

            mergedMesh.AddBlendShapeFrame(candidate, 100f, deltaVertices, deltaNormals, null);
            return candidate;
        }

        private static string SanitizeBlendshapeName(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "__YUCP_FbxMerge_ScaleToZero";
            }

            char[] chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }

        private bool ValidateData(FbxMergeData data, GameObject avatarRoot, List<string> warnings)
        {
            if (data == null)
            {
                return false;
            }

            if (avatarRoot == null)
            {
                warnings.Add("Avatar root is null.");
                return false;
            }

            if (data.baseRenderer == null)
            {
                warnings.Add("Base renderer is not assigned.");
                return false;
            }

            if (data.baseRenderer.sharedMesh == null)
            {
                warnings.Add("Base renderer has no mesh.");
                return false;
            }

            if (data.attachmentTarget == null)
            {
                warnings.Add("Attachment target is not assigned.");
                return false;
            }

            return true;
        }

        private bool TryResolveAttachmentSource(FbxMergeData data, GameObject avatarRoot, out AttachmentSource source, List<string> warnings)
        {
            source = new AttachmentSource
            {
                mesh = null,
                transform = null,
                materials = Array.Empty<Material>(),
                skinnedRenderer = null,
                meshRenderer = null,
                sourceGameObject = null,
                displayName = "Attachment",
                rendererPath = null,
                objectPath = null
            };

            UnityEngine.Object target = data.attachmentTarget;
            if (target is SkinnedMeshRenderer smr)
            {
                source.skinnedRenderer = smr;
                source.mesh = smr.sharedMesh;
                source.transform = smr.transform;
                source.materials = smr.sharedMaterials ?? Array.Empty<Material>();
                source.sourceGameObject = smr.gameObject;
                source.displayName = smr.name;
                source.rendererPath = AnimationUtility.CalculateTransformPath(smr.transform, avatarRoot.transform);
                source.objectPath = source.rendererPath;
                return source.mesh != null;
            }

            if (target is MeshFilter mf)
            {
                source.mesh = mf.sharedMesh;
                source.transform = mf.transform;
                source.meshRenderer = mf.GetComponent<MeshRenderer>();
                source.materials = source.meshRenderer != null ? (source.meshRenderer.sharedMaterials ?? Array.Empty<Material>()) : Array.Empty<Material>();
                source.sourceGameObject = mf.gameObject;
                source.displayName = mf.name;
                source.rendererPath = AnimationUtility.CalculateTransformPath(mf.transform, avatarRoot.transform);
                source.objectPath = source.rendererPath;
                return source.mesh != null;
            }

            if (target is GameObject go)
            {
                source.sourceGameObject = go;
                source.objectPath = AnimationUtility.CalculateTransformPath(go.transform, avatarRoot.transform);
                source.displayName = go.name;

                var goSmr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (goSmr != null)
                {
                    source.skinnedRenderer = goSmr;
                    source.mesh = goSmr.sharedMesh;
                    source.transform = goSmr.transform;
                    source.materials = goSmr.sharedMaterials ?? Array.Empty<Material>();
                    source.rendererPath = AnimationUtility.CalculateTransformPath(goSmr.transform, avatarRoot.transform);
                    return source.mesh != null;
                }

                var goMf = go.GetComponentInChildren<MeshFilter>(true);
                if (goMf != null)
                {
                    source.mesh = goMf.sharedMesh;
                    source.transform = goMf.transform;
                    source.meshRenderer = goMf.GetComponent<MeshRenderer>();
                    source.materials = source.meshRenderer != null ? (source.meshRenderer.sharedMaterials ?? Array.Empty<Material>()) : Array.Empty<Material>();
                    source.rendererPath = AnimationUtility.CalculateTransformPath(goMf.transform, avatarRoot.transform);
                    return source.mesh != null;
                }
            }

            warnings.Add($"Attachment target type '{target.GetType().Name}' is not supported.");
            return false;
        }

        private static HashSet<string> CollectAncestorPaths(
            GameObject attachmentObject,
            GameObject avatarRoot,
            HashSet<string> excludePaths)
        {
            var paths = new HashSet<string>();
            if (attachmentObject == null || avatarRoot == null)
            {
                return paths;
            }

            Transform walk = attachmentObject.transform.parent;
            while (walk != null && walk != avatarRoot.transform)
            {
                string path = AnimationUtility.CalculateTransformPath(walk, avatarRoot.transform);
                if (!string.IsNullOrEmpty(path) && !excludePaths.Contains(path))
                {
                    paths.Add(path);
                }
                walk = walk.parent;
            }

            return paths;
        }

        private static bool TryInjectIntoVRCFuryToggles(
            GameObject attachmentObject,
            GameObject avatarRoot,
            SkinnedMeshRenderer baseRenderer,
            List<int> uvDiscardMaterialIndices,
            string tilePropertyName,
            bool useScaleToZero,
            string scaleToZeroBlendshapeName,
            bool debugMode,
            UnityEngine.Object logContext,
            List<string> warnings)
        {
            if (attachmentObject == null || avatarRoot == null)
            {
                return false;
            }

            bool injected = false;
            Transform walk = attachmentObject.transform;
            while (walk != null && walk != avatarRoot.transform)
            {
                var components = walk.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp == null || comp.GetType().Name != "VRCFury")
                    {
                        continue;
                    }

                    var contentField = comp.GetType().GetField("content",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (contentField == null)
                    {
                        continue;
                    }

                    var content = contentField.GetValue(comp);
                    if (content == null || content.GetType().Name != "Toggle")
                    {
                        continue;
                    }

                    AnimationClip toggleClip = CreateMergedToggleClip(
                        baseRenderer,
                        avatarRoot,
                        uvDiscardMaterialIndices,
                        tilePropertyName,
                        useScaleToZero,
                        scaleToZeroBlendshapeName,
                        attachmentObject.name);

                    if (toggleClip == null)
                    {
                        if (debugMode)
                        {
                            Debug.LogWarning($"[Mesh Merger] CreateMergedToggleClip returned null for Toggle on '{walk.name}' " +
                                $"(useScaleToZero={useScaleToZero}, blendshape='{scaleToZeroBlendshapeName}', uvMats={uvDiscardMaterialIndices?.Count ?? 0})", logContext);
                        }
                        continue;
                    }

                    if (InjectClipIntoVRCFuryToggle(content, toggleClip, debugMode, logContext))
                    {
                        injected = true;
                        EditorUtility.SetDirty(comp);

                        string mode = useScaleToZero ? $"blendshape '{scaleToZeroBlendshapeName}'" : $"UV discard ({uvDiscardMaterialIndices?.Count ?? 0} materials)";
                        Debug.Log($"[Mesh Merger] Injected {mode} toggle clip into VRCFury Toggle on '{walk.name}'", logContext);
                    }
                    else if (debugMode)
                    {
                        Debug.LogWarning($"[Mesh Merger] InjectClipIntoVRCFuryToggle failed for Toggle on '{walk.name}'", logContext);
                    }
                }

                walk = walk.parent;
            }

            return injected;
        }

        private static AnimationClip CreateMergedToggleClip(
            SkinnedMeshRenderer baseRenderer,
            GameObject avatarRoot,
            List<int> uvDiscardMaterialIndices,
            string tilePropertyName,
            bool useScaleToZero,
            string scaleToZeroBlendshapeName,
            string attachmentName)
        {
            string baseRendererPath = AnimationUtility.CalculateTransformPath(baseRenderer.transform, avatarRoot.transform);

            var clip = new AnimationClip();
            clip.name = $"MeshMerger_Toggle_{SanitizeBlendshapeName(attachmentName)}";
            bool hasCurves = false;

            if (useScaleToZero && !string.IsNullOrEmpty(scaleToZeroBlendshapeName))
            {
                var onCurve = new AnimationCurve();
                var key0 = new Keyframe(0f, 0f);
                key0.inTangent = float.PositiveInfinity;
                key0.outTangent = float.PositiveInfinity;
                onCurve.AddKey(key0);
                var key1 = new Keyframe(1f / 60f, 0f);
                key1.inTangent = float.PositiveInfinity;
                key1.outTangent = float.PositiveInfinity;
                onCurve.AddKey(key1);

                var binding = EditorCurveBinding.FloatCurve(
                    baseRendererPath,
                    typeof(SkinnedMeshRenderer),
                    "blendShape." + scaleToZeroBlendshapeName);
                AnimationUtility.SetEditorCurve(clip, binding, onCurve);
                hasCurves = true;
            }
            else if (uvDiscardMaterialIndices != null && uvDiscardMaterialIndices.Count > 0 && !string.IsNullOrEmpty(tilePropertyName))
            {
                var onCurve = new AnimationCurve();
                onCurve.AddKey(0f, 0f);
                onCurve.AddKey(1f / 60f, 0f);

                foreach (int materialIndex in uvDiscardMaterialIndices)
                {
                    string propertyPath = FbxMergeToggleUvDiscardMapper.GetMaterialTilePropertyPath(materialIndex, tilePropertyName);
                    var binding = EditorCurveBinding.FloatCurve(
                        baseRendererPath,
                        typeof(SkinnedMeshRenderer),
                        propertyPath);
                    AnimationUtility.SetEditorCurve(clip, binding, onCurve);
                    hasCurves = true;
                }
            }

            return hasCurves ? clip : null;
        }

        private static bool InjectClipIntoVRCFuryToggle(object toggleContent, AnimationClip clip, bool debugMode, UnityEngine.Object logContext)
        {
            try
            {
                var stateField = toggleContent.GetType().GetField("state",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (stateField == null)
                {
                    return false;
                }

                var state = stateField.GetValue(toggleContent);
                var actionsField = state.GetType().GetField("actions",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (actionsField == null)
                {
                    return false;
                }

                var actionsList = actionsField.GetValue(state) as System.Collections.IList;
                if (actionsList == null)
                {
                    return false;
                }

                var animActionType = System.Type.GetType("VF.Model.StateAction.AnimationClipAction, VRCFury");
                if (animActionType == null)
                {
                    return false;
                }

                var animAction = System.Activator.CreateInstance(animActionType);
                var motionField = animActionType.GetField("motion",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (motionField != null)
                {
                    motionField.SetValue(animAction, clip);
                }

                actionsList.Add(animAction);
                return true;
            }
            catch (System.Exception ex)
            {
                if (debugMode)
                {
                    Debug.LogWarning($"[Mesh Merger] Failed to inject into VRCFury Toggle: {ex.Message}", logContext);
                }
                return false;
            }
        }
    }
}
