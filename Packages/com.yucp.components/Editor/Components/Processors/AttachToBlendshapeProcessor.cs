using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDK3.Avatars.Components;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;
using YUCP.Components.Editor.Utils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Attach to Blendshape components during avatar build.
    /// </summary>
    public class AttachToBlendshapeProcessor : IVRCSDKPreprocessAvatarCallback
    {


        public int callbackOrder => int.MinValue + 110;

        private const string KeepAliveLayerName = "[YUCP] Keep Blendshapes (Optimizer Guard)";
        private const string KeepAliveStateName = "Keep (Not Used)";

        private sealed class BuildWorkItem
        {
            public AttachToBlendshapeData data;
            public SurfaceCluster cluster;
            public List<string> trackedBlendshapes;
            public AttachToBlendshapeOutputMode outputMode;
            public Mesh attachmentMesh;
            public Transform attachmentTransform;
            public Material[] attachmentMaterials;
            public SkinnedMeshRenderer attachmentSmr;
            public MeshRenderer attachmentMr;
        }

        private static void EnsureSingleBoneSkinning(
            SkinnedMeshRenderer renderer,
            Mesh mesh,
            Transform bone,
            AttachToBlendshapeData data)
        {
            if (renderer == null || mesh == null || bone == null) return;



            bool hasValidSkinning =
                mesh.bindposes != null && mesh.bindposes.Length > 0 &&
                mesh.boneWeights != null && mesh.boneWeights.Length == mesh.vertexCount;
            if (hasValidSkinning)
            {
                return;
            }

            var weights = new BoneWeight[mesh.vertexCount];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i].boneIndex0 = 0;
                weights[i].weight0 = 1f;
            }



            var bindpose = bone.worldToLocalMatrix * renderer.transform.localToWorldMatrix;

            mesh.boneWeights = weights;
            mesh.bindposes = new[] { bindpose };

            renderer.rootBone = bone;
            renderer.bones = new[] { bone };
            renderer.updateWhenOffscreen = true;

            if (data != null && data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Added single-bone skinning to mesh '{mesh.name}' ({mesh.vertexCount} verts)", data);
            }
        }


        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<AttachToBlendshapeData>(true);

            if (dataList.Length == 0)
            {
                return true;
            }

            try
            {
                var animator = avatarRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[AttachToBlendshapeProcessor] No Animator found on avatar");
                    return false;
                }

                var workItems = new List<BuildWorkItem>();
                foreach (var data in dataList)
                {
                    if (!ValidateData(data))
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Validation failed for '{data.name}'", data);
                        return false;
                    }

                    var cluster = SurfaceClusterDetector.DetectCluster(
                        data.targetMesh,
                        data.transform.position,
                        data.clusterTriangleCount,
                        data.searchRadius,
                        data.manualTriangleIndex,
                        DetermineClusterSeedBlendshapes(data, avatarRoot));
                    if (cluster == null)
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Failed to detect surface cluster for '{data.name}'", data);
                        return false;
                    }

                    var tracked = DetermineBlendshapesToTrack(data, avatarRoot, cluster);
                    if (tracked.Count == 0)
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] No blendshapes were selected for '{data.name}'", data);
                        return false;
                    }

                    if (!TryGetTargetMeshToModify(data, out var attachmentMesh, out var attachmentTransform,
                            out var attachmentMaterials, out var attachmentSmr, out var attachmentMr))
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Could not resolve attachment mesh for '{data.name}'", data);
                        return false;
                    }

                    if (attachmentTransform == data.targetMesh.transform)
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Attachment target for '{data.name}' resolves to the base renderer.", data);
                        return false;
                    }

                    workItems.Add(new BuildWorkItem
                    {
                        data = data,
                        cluster = cluster,
                        trackedBlendshapes = tracked,
                        outputMode = ResolveOutputMode(data, avatarRoot, tracked),
                        attachmentMesh = attachmentMesh,
                        attachmentTransform = attachmentTransform,
                        attachmentMaterials = attachmentMaterials,
                        attachmentSmr = attachmentSmr,
                        attachmentMr = attachmentMr
                    });
                }

                foreach (var linked in workItems.Where(i => i.outputMode == AttachToBlendshapeOutputMode.LinkedRendererBake))
                {
                    if (!ProcessLinkedRenderer(linked, avatarRoot, animator)) return false;
                }

                foreach (var group in workItems
                             .Where(i => i.outputMode == AttachToBlendshapeOutputMode.MergeIntoBaseMesh)
                             .GroupBy(i => i.data.targetMesh))
                {
                    if (!ProcessMergedGroup(group.Key, group.ToList(), avatarRoot, animator)) return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Fatal error: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }

            return true;
        }

        private bool ProcessLinkedRenderer(BuildWorkItem item, GameObject avatarRoot, Animator animator)
        {
            string bonePath = item.data.attachToClosestBone ? AttachToClosestBone(item.data, animator) : "";
            if (!BlendshapeTransfer.TransferBlendshapes(
                    item.data.targetMesh,
                    item.data.targetMeshToModify,
                    item.trackedBlendshapes,
                    item.cluster,
                    item.data))
            {
                return false;
            }

            if (!CreateTransformBlendshapesAndLink(item.data, item.trackedBlendshapes, avatarRoot))
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Linked renderer generation failed for '{item.data.name}'.", item.data);
                return false;
            }

            TryGetTargetMeshToModify(item.data, out var generatedMesh, out _, out _, out _, out _);

            item.data.SetBuildStats(
                item.cluster,
                item.trackedBlendshapes,
                item.trackedBlendshapes.Count,
                bonePath,
                AttachToBlendshapeOutputMode.LinkedRendererBake,
                item.data.visibilityMode,
                "",
                generatedMesh != null ? generatedMesh.blendShapeCount : 0);
            return true;
        }

        private bool ProcessMergedGroup(
            SkinnedMeshRenderer baseRenderer,
            List<BuildWorkItem> items,
            GameObject avatarRoot,
            Animator animator)
        {
            Mesh baseMesh = baseRenderer != null ? baseRenderer.sharedMesh : null;
            var inputs = new List<AttachToBlendshapeMergedMeshBuilder.AttachmentInput>();
            foreach (var item in items)
            {
                inputs.Add(new AttachToBlendshapeMergedMeshBuilder.AttachmentInput
                {
                    data = item.data,
                    mesh = item.attachmentMesh,
                    transform = item.attachmentTransform,
                    materials = item.attachmentMaterials,
                    skinnedRenderer = item.attachmentSmr,
                    meshRenderer = item.attachmentMr,
                    cluster = item.cluster,
                    trackedBlendshapes = new HashSet<string>(item.trackedBlendshapes, StringComparer.Ordinal),
                    displayName = item.attachmentTransform.name,
                    animationPath = AnimationUtility.CalculateTransformPath(item.attachmentTransform, avatarRoot.transform)
                });
            }

            var build = AttachToBlendshapeMergedMeshBuilder.Build(baseRenderer, baseMesh, inputs);
            if (!build.success)
            {
                bool mustFail = items.Any(i => ContainsVisemes(i.trackedBlendshapes) ||
                                               i.data.outputMode == AttachToBlendshapeOutputMode.MergeIntoBaseMesh);
                Debug.LogError($"[AttachToBlendshapeProcessor] Merge failed for '{baseRenderer?.name}': {build.error}", baseRenderer);
                if (mustFail) return false;



                foreach (var item in items)
                {
                    if (!ProcessLinkedRenderer(item, avatarRoot, animator)) return false;
                }
                return true;
            }


            baseRenderer.sharedMesh = build.mesh;
            baseRenderer.sharedMaterials = build.materials;
            EditorUtility.SetDirty(baseRenderer);

            foreach (var attachment in build.attachments)
            {
                var item = items.First(i => i.data == attachment.input.data);
                if (!string.IsNullOrEmpty(attachment.visibilityBlendshapeName) && attachment.defaultHidden)
                {
                    int index = build.mesh.GetBlendShapeIndex(attachment.visibilityBlendshapeName);
                    if (index >= 0) baseRenderer.SetBlendShapeWeight(index, 100f);
                }

                int remapped = 0;
                var warnings = new List<string>(build.warnings);
                if (item.data.remapVisibilityAnimations)
                {
                    var paths = new HashSet<string>();
                    string rendererPath = AnimationUtility.CalculateTransformPath(item.attachmentTransform, avatarRoot.transform);
                    if (!string.IsNullOrEmpty(rendererPath)) paths.Add(rendererPath);

                    foreach (string path in paths)
                    {
                        var remap = FbxMergeAnimationRemapper.RemapAllAnimations(new FbxMergeAnimationRemapper.RemapContext
                        {
                            avatarRoot = avatarRoot,
                            attachmentPath = path,
                            baseRendererPath = AnimationUtility.CalculateTransformPath(baseRenderer.transform, avatarRoot.transform),
                            attachmentName = item.attachmentTransform.name,
                            attachmentTransform = item.attachmentTransform,
                            baseRenderer = baseRenderer,
                            mergedMesh = build.mesh,
                            attachmentVertexStart = attachment.vertexStart,
                            attachmentVertexCount = attachment.vertexCount,
                            baseMaterialOffset = attachment.materialStart,
                            attachmentBlendshapeNameMap = attachment.attachmentBlendshapeNameMap,
                            uvDiscardMaterialIndices = attachment.uvDiscardMaterialIndices,
                            uvDiscardTilePropertyName = FbxMergeToggleUvDiscardMapper.GetTilePropertyName(item.data.uvDiscardRow, item.data.uvDiscardColumn),
                            remapBlendshapeAnimations = true,
                            remapMaterialAnimations = true,
                            remapRendererAndObjectOffToUvDiscard = true,
                            useScaleToZeroFallback = item.data.visibilityMode == AttachToBlendshapeVisibilityMode.SizeBlendshape,
                            scaleToZeroBlendshapeName = attachment.visibilityBlendshapeName,
                            debugMode = item.data.debugMode,
                            logContext = item.data
                        });
                        remapped += remap.remappedCurveCount;
                        warnings.AddRange(remap.warnings);
                    }



                    Transform ancestor = item.attachmentTransform.parent;
                    while (ancestor != null && ancestor != avatarRoot.transform)
                    {
                        string path = AnimationUtility.CalculateTransformPath(ancestor, avatarRoot.transform);
                        if (!string.IsNullOrEmpty(path) && !paths.Contains(path))
                        {
                            var remap = FbxMergeAnimationRemapper.RemapAllAnimations(new FbxMergeAnimationRemapper.RemapContext
                            {
                                avatarRoot = avatarRoot,
                                attachmentPath = path,
                                baseRendererPath = AnimationUtility.CalculateTransformPath(baseRenderer.transform, avatarRoot.transform),
                                attachmentName = item.attachmentTransform.name,
                                attachmentTransform = null,
                                baseRenderer = baseRenderer,
                                mergedMesh = build.mesh,
                                attachmentVertexStart = attachment.vertexStart,
                                attachmentVertexCount = attachment.vertexCount,
                                baseMaterialOffset = attachment.materialStart,
                                attachmentBlendshapeNameMap = attachment.attachmentBlendshapeNameMap,
                                uvDiscardMaterialIndices = attachment.uvDiscardMaterialIndices,
                                uvDiscardTilePropertyName = FbxMergeToggleUvDiscardMapper.GetTilePropertyName(item.data.uvDiscardRow, item.data.uvDiscardColumn),
                                remapRendererAndObjectOffToUvDiscard = true,
                                useScaleToZeroFallback = item.data.visibilityMode == AttachToBlendshapeVisibilityMode.SizeBlendshape,
                                scaleToZeroBlendshapeName = attachment.visibilityBlendshapeName,
                                isAncestorPath = true,
                                debugMode = item.data.debugMode,
                                logContext = item.data
                            });
                            remapped += remap.remappedCurveCount;
                            warnings.AddRange(remap.warnings);
                        }
                        ancestor = ancestor.parent;
                    }

                    FbxMergeProcessor.TryInjectIntoVRCFuryToggles(
                        item.attachmentTransform.gameObject,
                        avatarRoot,
                        baseRenderer,
                        attachment.uvDiscardMaterialIndices,
                        FbxMergeToggleUvDiscardMapper.GetTilePropertyName(item.data.uvDiscardRow, item.data.uvDiscardColumn),
                        item.data.visibilityMode == AttachToBlendshapeVisibilityMode.SizeBlendshape,
                        attachment.visibilityBlendshapeName,
                        item.data.debugMode,
                        item.data,
                        warnings);
                }

                if (item.data.disableOriginalRendererOnMerge)
                {
                    if (item.attachmentSmr != null) item.attachmentSmr.enabled = false;
                    if (item.attachmentMr != null) item.attachmentMr.enabled = false;
                }

                item.data.SetBuildStats(
                    item.cluster,
                    item.trackedBlendshapes,
                    item.trackedBlendshapes.Count,
                    "",
                    AttachToBlendshapeOutputMode.MergeIntoBaseMesh,
                    item.data.visibilityMode,
                    attachment.visibilityBlendshapeName,
                    build.mesh.blendShapeCount,
                    remapped,
                    warnings);
            }

            return true;
        }

        private bool ValidateData(AttachToBlendshapeData data)
        {
            if (data.targetMesh == null)
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Target mesh is not set", data);
                return false;
            }

            if (data.targetMesh.sharedMesh == null)
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Target mesh has no mesh data", data);
                return false;
            }

            if (!PoseSampler.HasBlendshapes(data.targetMesh))
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Target mesh has no blendshapes", data);
                return false;
            }

            if (data.trackingMode == BlendshapeTrackingMode.Specific &&
                (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0))
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Specific mode requires at least one blendshape name", data);
                return false;
            }

            return true;
        }

        internal AttachToBlendshapeOutputMode ResolveOutputMode(
            AttachToBlendshapeData data,
            GameObject avatarRoot,
            List<string> trackedBlendshapes)
        {
            if (data == null) return AttachToBlendshapeOutputMode.LinkedRendererBake;




            bool trackingVisemes = data.trackingMode == BlendshapeTrackingMode.VisemsOnly ||
                                   ContainsVisemes(trackedBlendshapes);

            if (trackingVisemes)
            {
                if (data.debugMode)
                {
                    Debug.Log("[AttachToBlendshapeProcessor] Tracked blendshapes include visemes. Forcing MergeIntoBaseMesh (visemes are driven on the base mesh at runtime).", data);
                }
                return AttachToBlendshapeOutputMode.MergeIntoBaseMesh;
            }


            if (data.outputMode != AttachToBlendshapeOutputMode.Auto)
            {
                return data.outputMode;
            }


            var descriptor = avatarRoot != null ? avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            if (descriptor == null || data.targetMesh == null)
            {
                return AttachToBlendshapeOutputMode.LinkedRendererBake;
            }

            string baseMeshPath = AnimationUtility.CalculateTransformPath(data.targetMesh.transform, avatarRoot.transform);
            string objectPath = AnimationUtility.CalculateTransformPath(data.transform, avatarRoot.transform);

            bool hasBaseBlendshapeCurves = false;
            bool hasMaterialAnimOnObject = false;
            bool hasComplexTransformAnimOnObject = false;

            foreach (var clip in CollectAllAnimationClips(descriptor))
            {
                if (clip == null) continue;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                if (bindings == null || bindings.Length == 0) continue;

                foreach (var binding in bindings)
                {
                    if (binding.type == typeof(SkinnedMeshRenderer) &&
                        binding.propertyName != null &&
                        binding.propertyName.StartsWith("blendShape.") &&
                        binding.path == baseMeshPath)
                    {
                        hasBaseBlendshapeCurves = true;
                    }

                    if (binding.path == objectPath)
                    {
                        if (data.autoAvoidMergeIfMaterialAnimation &&
                            binding.propertyName != null &&
                            binding.propertyName.StartsWith("material."))
                        {
                            hasMaterialAnimOnObject = true;
                        }

                        if (binding.type == typeof(SkinnedMeshRenderer) &&
                            binding.propertyName != null &&
                            binding.propertyName.StartsWith("blendShape."))
                        {
                            return AttachToBlendshapeOutputMode.LinkedRendererBake;
                        }

                        if (data.autoAvoidMergeIfComplexTransformAnimation &&
                            binding.type == typeof(Transform) &&
                            IsTransformBinding(binding.propertyName))
                        {
                            var curve = AnimationUtility.GetEditorCurve(clip, binding);
                            if (curve != null && !IsCurveConstant(curve))
                            {
                                hasComplexTransformAnimOnObject = true;
                            }
                        }
                    }
                }

                if (hasMaterialAnimOnObject && data.autoAvoidMergeIfMaterialAnimation)
                {
                    return AttachToBlendshapeOutputMode.LinkedRendererBake;
                }
                if (hasComplexTransformAnimOnObject && data.autoAvoidMergeIfComplexTransformAnimation)
                {
                    return AttachToBlendshapeOutputMode.LinkedRendererBake;
                }
            }

            if (!hasBaseBlendshapeCurves)
            {
                return AttachToBlendshapeOutputMode.MergeIntoBaseMesh;
            }

            return AttachToBlendshapeOutputMode.LinkedRendererBake;
        }

        internal static List<string> DetermineClusterSeedBlendshapes(AttachToBlendshapeData data, GameObject avatarRoot)
        {
            if (data == null || data.targetMesh == null || data.targetMesh.sharedMesh == null)
                return new List<string>();
            switch (data.trackingMode)
            {
                case BlendshapeTrackingMode.Specific:
                    return (data.specificBlendshapes ?? new List<string>())
                        .Where(name => data.targetMesh.sharedMesh.GetBlendShapeIndex(name) >= 0)
                        .ToList();
                case BlendshapeTrackingMode.VisemsOnly:
                    return VRChatVisemeDetector.GetVisemeBlendshapes(data.targetMesh, avatarRoot);
                case BlendshapeTrackingMode.All:
                    return PoseSampler.GetAllBlendshapeNames(data.targetMesh.sharedMesh);
                case BlendshapeTrackingMode.Smart:
                    var all = PoseSampler.GetAllBlendshapeNames(data.targetMesh.sharedMesh);
                    var facial = all.Where(IsLikelyRuntimeFacialBlendshape).ToList();
                    return facial.Count > 0 ? facial : all;
                default:
                    return new List<string>();
            }
        }

        private static bool IsLikelyRuntimeFacialBlendshape(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (VRChatVisemeDetector.IsVisemeBlendshape(name)) return true;
            string value = name.ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty).Replace(".", string.Empty);
            string[] runtimePrefixes =
            {
                "jaw", "mouth", "lip", "tongue", "brow", "eyeblink", "eyelook",
                "eyesquint", "eyewide", "cheekpuff", "cheeksquint", "nosesneer"
            };
            return runtimePrefixes.Any(value.StartsWith);
        }

        private static bool ContainsVisemes(List<string> blendshapes)
        {
            if (blendshapes == null || blendshapes.Count == 0) return false;

            foreach (var name in blendshapes)
            {
                if (VRChatVisemeDetector.IsVisemeBlendshape(name))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTransformBinding(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return false;
            return propertyName.StartsWith("m_LocalPosition.") ||
                   propertyName.StartsWith("m_LocalRotation.") ||
                   propertyName.StartsWith("m_LocalScale.");
        }

        private static bool IsCurveConstant(AnimationCurve curve, float epsilon = 1e-4f)
        {
            if (curve == null || curve.length == 0) return true;
            float v0 = curve.keys[0].value;
            for (int i = 1; i < curve.length; i++)
            {
                if (Mathf.Abs(curve.keys[i].value - v0) > epsilon) return false;
            }
            return true;
        }

        private IEnumerable<AnimationClip> CollectAllAnimationClips(VRCAvatarDescriptor descriptor)
        {
            var clips = new HashSet<AnimationClip>();
            if (descriptor == null) return clips;

            void AddController(AnimatorController ac)
            {
                if (ac == null) return;
                foreach (var layer in ac.layers)
                {
                    if (layer.stateMachine == null) continue;
                    foreach (var state in GetAllStates(layer.stateMachine))
                    {
                        CollectClipsFromMotion(state.motion, clips);
                    }
                }
            }

            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.isDefault) continue;
                if (layer.animatorController is AnimatorController ac) AddController(ac);
            }
            foreach (var layer in descriptor.specialAnimationLayers)
            {
                if (layer.isDefault) continue;
                if (layer.animatorController is AnimatorController ac) AddController(ac);
            }

            return clips;
        }

        private static void CollectClipsFromMotion(Motion motion, HashSet<AnimationClip> clips)
        {
            if (motion == null || clips == null) return;
            if (motion is AnimationClip clip)
            {
                clips.Add(clip);
                return;
            }
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                {
                    CollectClipsFromMotion(child.motion, clips);
                }
            }
        }

        internal static bool TryGetTargetMeshToModify(
            AttachToBlendshapeData data,
            out Mesh mesh,
            out Transform meshTransform,
            out Material[] materials,
            out SkinnedMeshRenderer smr,
            out MeshRenderer mr)
        {
            mesh = null;
            meshTransform = null;
            materials = Array.Empty<Material>();
            smr = null;
            mr = null;

            UnityEngine.Object target = data.targetMeshToModify;

            if (target is SkinnedMeshRenderer tSmr)
            {
                smr = tSmr;
                mesh = tSmr.sharedMesh;
                meshTransform = tSmr.transform;
                materials = tSmr.sharedMaterials ?? Array.Empty<Material>();
                return mesh != null;
            }

            if (target is MeshFilter mf)
            {
                mesh = mf.sharedMesh;
                meshTransform = mf.transform;
                mr = mf.GetComponent<MeshRenderer>();
                materials = mr != null ? (mr.sharedMaterials ?? Array.Empty<Material>()) : Array.Empty<Material>();
                return mesh != null;
            }

            if (target is GameObject go)
            {
                var goSmr = go.GetComponent<SkinnedMeshRenderer>();
                if (goSmr != null)
                {
                    smr = goSmr;
                    mesh = goSmr.sharedMesh;
                    meshTransform = goSmr.transform;
                    materials = goSmr.sharedMaterials ?? Array.Empty<Material>();
                    return mesh != null;
                }
                var goMf = go.GetComponent<MeshFilter>();
                if (goMf != null)
                {
                    mesh = goMf.sharedMesh;
                    meshTransform = goMf.transform;
                    mr = goMf.GetComponent<MeshRenderer>();
                    materials = mr != null ? (mr.sharedMaterials ?? Array.Empty<Material>()) : Array.Empty<Material>();
                    return mesh != null;
                }
            }


            var selfSmr = data.GetComponent<SkinnedMeshRenderer>();
            if (selfSmr != null)
            {
                smr = selfSmr;
                mesh = selfSmr.sharedMesh;
                meshTransform = selfSmr.transform;
                materials = selfSmr.sharedMaterials ?? Array.Empty<Material>();
                return mesh != null;
            }
            var selfMf2 = data.GetComponent<MeshFilter>();
            if (selfMf2 != null)
            {
                mesh = selfMf2.sharedMesh;
                meshTransform = selfMf2.transform;
                mr = selfMf2.GetComponent<MeshRenderer>();
                materials = mr != null ? (mr.sharedMaterials ?? Array.Empty<Material>()) : Array.Empty<Material>();
                return mesh != null;
            }

            return false;
        }

        private List<string> DetermineBlendshapesToTrack(
            AttachToBlendshapeData data,
            GameObject avatarRoot,
            SurfaceCluster cluster)
        {
            List<string> blendshapes = new List<string>();
            Mesh mesh = data.targetMesh.sharedMesh;

            switch (data.trackingMode)
            {
                case BlendshapeTrackingMode.All:
                    blendshapes = PoseSampler.GetAllBlendshapeNames(mesh);
                    Debug.Log($"[AttachToBlendshapeProcessor] All mode: tracking {blendshapes.Count} blendshapes");
                    break;

                case BlendshapeTrackingMode.Specific:
                    blendshapes = new List<string>(data.specificBlendshapes);

                    blendshapes = blendshapes.Where(name => mesh.GetBlendShapeIndex(name) >= 0).ToList();
                    Debug.Log($"[AttachToBlendshapeProcessor] Specific mode: tracking {blendshapes.Count} blendshapes");
                    break;

                case BlendshapeTrackingMode.VisemsOnly:
                    blendshapes = VRChatVisemeDetector.GetVisemeBlendshapes(data.targetMesh, avatarRoot);
                    Debug.Log($"[AttachToBlendshapeProcessor] Viseme mode: tracking {blendshapes.Count} viseme blendshapes");
                    break;

                case BlendshapeTrackingMode.Smart:
                    blendshapes = VRChatVisemeDetector.DetectActiveBlendshapes(
                        data.targetMesh,
                        cluster,
                        data.smartDetectionThreshold);
                    Debug.Log($"[AttachToBlendshapeProcessor] Smart mode: detected {blendshapes.Count} active blendshapes");
                    break;
            }

            return blendshapes;
        }

        private string AttachToClosestBone(AttachToBlendshapeData data, Animator animator)
        {
            Transform closestBone = ClosestBoneFinder.Find(animator, data.transform, data.boneSearchRadius,
                data.ignoreHumanoidBones, data.boneNameFilter);

            if (closestBone == null)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No matching bone within range for '{data.name}'", data);
                return "";
            }


            string bonePath = AnimationUtility.CalculateTransformPath(closestBone, animator.transform);


            var link = FuryComponents.CreateArmatureLink(data.gameObject);
            if (link == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to create armature link for '{data.name}'", data);
                return bonePath;
            }


            if (!string.IsNullOrEmpty(data.boneOffset))
            {
                link.LinkTo(bonePath + "/" + data.boneOffset);
            }
            else
            {
                link.LinkTo(bonePath);
            }

            float distance = Vector3.Distance(data.transform.position, closestBone.position);
            Debug.Log($"[AttachToBlendshapeProcessor] Linked '{data.name}' to bone '{bonePath}' (distance: {distance:F3}m)", data);

            return bonePath;
        }


        /// <summary>
        /// Creates blendshapes on the ATTACHED OBJECT mesh which represent the solved rigid motion at each weight.
        /// Then uses VRCFury's BlendShapeLink to mirror the base mesh's blendshape weights onto these new shapes.
        ///
        /// Key detail: Unity's Mesh.AddBlendShapeFrame expects DELTA vertices, not absolute vertices.
        /// Each frame we compute v' = R(w) * v + T(w) (about the object pivot/origin, like Preview),
        /// then delta = v' - v and store that as the frame.
        /// </summary>
        private bool CreateTransformBlendshapesAndLink(AttachToBlendshapeData data, List<string> blendshapesToTrack, GameObject avatarRoot)
        {
            // Resolve the exact object selected in targetMeshToModify. The old implementation
            // incorrectly generated the final mesh on the component GameObject instead.
            if (!TryGetTargetMeshToModify(data, out var resolvedMesh, out var resolvedTransform,
                    out var resolvedMaterials, out var targetMeshRenderer, out var resolvedMeshRenderer))
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Could not resolve linked target for '{data.name}'.", data);
                return false;
            }

            Mesh targetMesh = resolvedMesh;
            if (targetMeshRenderer == null)
            {
                var meshFilter = resolvedTransform.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    Debug.LogError($"[AttachToBlendshapeProcessor] Resolved target '{resolvedTransform.name}' has no usable mesh.", data);
                    return false;
                }

                targetMeshRenderer = resolvedTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                targetMeshRenderer.sharedMesh = meshFilter.sharedMesh;
                targetMeshRenderer.sharedMaterials = resolvedMaterials;
                if (resolvedMeshRenderer != null) resolvedMeshRenderer.enabled = false;
            }

            targetMesh = targetMeshRenderer.sharedMesh;
            if (targetMesh == null) {
                Debug.LogError($"[AttachToBlendshapeProcessor] Target SkinnedMeshRenderer '{targetMeshRenderer.name}' has no mesh", data);
                return false;
            }

            // BlendshapeTransfer already produced the one preserved working copy. Do not clone
            // it again or create a redundant second family of transform blendshapes.
            targetMeshRenderer.sharedMesh = targetMesh;
            EnsureSingleBoneSkinning(targetMeshRenderer, targetMesh, resolvedTransform, data);

            // Get base mesh path for VRCFury
            string baseMeshName = data.targetMesh.transform.name;

            // The transferred shapes themselves contain the solved motion. Link base names to
            // those exact names instead of generating _YUCP_Transform_* duplicates.
            Dictionary<string, string> blendshapeMappings = new Dictionary<string, string>();
            foreach (string blendshapeName in blendshapesToTrack)
            {
                if (targetMesh.GetBlendShapeIndex(blendshapeName) >= 0)
                    blendshapeMappings[blendshapeName] = blendshapeName;
            }

            // Ensure bounds won't cull the mesh when vertices translate due to blendshape frames.

            targetMesh.RecalculateBounds();
            var b = targetMesh.bounds;
            b.Expand(0.1f);
            targetMeshRenderer.localBounds = b;


            var vrcFuryType = System.Type.GetType("VF.Model.VRCFury, VRCFury");
            if (vrcFuryType == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Could not find VRCFury type", data);
                return false;
            }

            var vrcFury = avatarRoot.GetComponent(vrcFuryType);
            if (vrcFury == null)
            {
                vrcFury = avatarRoot.AddComponent(vrcFuryType);
            }


            AddBlendShapeLinkFeature(vrcFury, baseMeshName, targetMeshRenderer, blendshapeMappings, data, avatarRoot, vrcFuryType);




            EnsureBlendshapesSurviveVrcFuryOptimizer(avatarRoot, targetMeshRenderer, blendshapesToTrack, Array.Empty<string>(), data);

            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Created {blendshapeMappings.Count} transform blendshapes (multi-frame deltas) and linked them via VRCFury BlendShapeLink", data);
            }
            return blendshapeMappings.Count > 0;
        }

        private void EnsureBlendshapesSurviveVrcFuryOptimizer(
            GameObject avatarRoot,
            SkinnedMeshRenderer targetMeshRenderer,
            IEnumerable<string> transferredBlendshapes,
            IEnumerable<string> transformBlendshapes,
            AttachToBlendshapeData data)
        {
            if (avatarRoot == null || targetMeshRenderer == null || targetMeshRenderer.sharedMesh == null) return;

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            var controller = FindControllerForKeepAlive(descriptor);
            if (controller == null)
            {
                if (data != null && data.debugMode)
                {
                    Debug.LogWarning(
                        "[AttachToBlendshapeProcessor] Could not find a non-default AnimatorController to attach keep-alive curves to. " +
                        "If VRCFury Blendshape Optimizer is enabled, it may bake away the generated blendshapes.",
                        data);
                }
                return;
            }

            var targetPath = AnimationUtility.CalculateTransformPath(targetMeshRenderer.transform, avatarRoot.transform);
            if (string.IsNullOrEmpty(targetPath)) return;

            var names = new HashSet<string>();
            foreach (var name in transferredBlendshapes ?? Array.Empty<string>()) names.Add(name);
            foreach (var name in transformBlendshapes ?? Array.Empty<string>()) names.Add(name);

            if (names.Count == 0) return;

            var clip = new AnimationClip
            {
                name = $"_YUCP_KeepBlendshapes_{targetMeshRenderer.name}",
                legacy = false
            };
            clip.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

            foreach (var name in names)
            {
                var index = targetMeshRenderer.sharedMesh.GetBlendShapeIndex(name);
                if (index < 0) continue;

                var defaultValue = targetMeshRenderer.GetBlendShapeWeight(index);
                var altValue = defaultValue < 99.999f ? defaultValue + 0.001f : defaultValue - 0.001f;
                altValue = Mathf.Clamp(altValue, 0f, 100f);

                if (Mathf.Approximately(defaultValue, altValue)) continue;

                var curve = new AnimationCurve(
                    new Keyframe(0f, defaultValue),
                    new Keyframe(0.01f, altValue),
                    new Keyframe(0.02f, defaultValue)
                );

                var binding = EditorCurveBinding.FloatCurve(targetPath, typeof(SkinnedMeshRenderer), $"blendShape.{name}");
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            AddOrUpdateKeepAliveLayer(controller, clip);
        }

        private static AnimatorController FindControllerForKeepAlive(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return null;

            AnimatorController TryGet(VRCAvatarDescriptor.CustomAnimLayer layer)
            {
                if (layer.isDefault) return null;
                return layer.animatorController as AnimatorController;
            }


            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX) continue;
                var ac = TryGet(layer);
                if (ac != null) return ac;
            }

            foreach (var layer in descriptor.specialAnimationLayers)
            {
                var ac = TryGet(layer);
                if (ac != null) return ac;
            }

            foreach (var layer in descriptor.baseAnimationLayers)
            {
                var ac = TryGet(layer);
                if (ac != null) return ac;
            }

            return null;
        }

        private static void AddOrUpdateKeepAliveLayer(AnimatorController controller, AnimationClip clip)
        {
            if (controller == null || clip == null) return;

            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name != KeepAliveLayerName) continue;

                var stateMachine = layers[i].stateMachine;
                if (stateMachine == null) return;

                var state = stateMachine.states.Select(s => s.state).FirstOrDefault(s => s != null && s.name == KeepAliveStateName);
                if (state == null)
                {
                    state = stateMachine.AddState(KeepAliveStateName);
                }

                state.motion = clip;
                stateMachine.defaultState = state;

                layers[i].defaultWeight = 0f;
                controller.layers = layers;
                return;
            }

            controller.AddLayer(KeepAliveLayerName);
            layers = controller.layers;
            var newIndex = layers.Length - 1;
            layers[newIndex].defaultWeight = 0f;

            var sm = layers[newIndex].stateMachine;
            var keepState = sm.AddState(KeepAliveStateName);
            keepState.motion = clip;
            sm.defaultState = keepState;

            controller.layers = layers;
        }

        /// <summary>
        /// Adds a VRCFury BlendShapeLink feature using reflection (since the class is internal).
        /// </summary>
        private void AddBlendShapeLinkFeature(object vrcFury, string baseMeshName,
                                            SkinnedMeshRenderer targetMesh,
                                            Dictionary<string, string> mappings,
                                            AttachToBlendshapeData data,
                                            GameObject avatarRoot,
                                            System.Type vrcFuryType)
        {
            try
            {

                var blendShapeLinkType = System.Type.GetType("VF.Model.Feature.BlendShapeLink, VRCFury");
                if (blendShapeLinkType == null)
                {
                    Debug.LogWarning($"[AttachToBlendshapeProcessor] Could not find BlendShapeLink type. Transform blendshapes created but not linked.", data);
                    return;
                }

                var linkFeature = System.Activator.CreateInstance(blendShapeLinkType);


                var baseObjField = blendShapeLinkType.GetField("baseObj");
                if (baseObjField != null)
                {
                    baseObjField.SetValue(linkFeature, baseMeshName);
                }


                var linkSkinsField = blendShapeLinkType.GetField("linkSkins");
                if (linkSkinsField != null)
                {
                    var linkSkinListType = System.Type.GetType("VF.Model.Feature.BlendShapeLink+LinkSkin, VRCFury");
                    var linkSkin = System.Activator.CreateInstance(linkSkinListType);
                    var rendererField = linkSkinListType.GetField("renderer");
                    if (rendererField != null)
                    {
                        rendererField.SetValue(linkSkin, targetMesh);
                    }

                    var linkSkinsList = System.Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(linkSkinListType));
                    var addMethod = linkSkinsList.GetType().GetMethod("Add");
                    addMethod.Invoke(linkSkinsList, new object[] { linkSkin });
                    linkSkinsField.SetValue(linkFeature, linkSkinsList);
                }


                var includesField = blendShapeLinkType.GetField("includes");
                if (includesField != null)
                {
                    var includeType = System.Type.GetType("VF.Model.Feature.BlendShapeLink+Include, VRCFury");
                    var includesList = System.Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(includeType));
                    var addMethod = includesList.GetType().GetMethod("Add");

                    foreach (var mapping in mappings)
                    {
                        var include = System.Activator.CreateInstance(includeType);
                        var nameOnBaseField = includeType.GetField("nameOnBase");
                        var nameOnLinkedField = includeType.GetField("nameOnLinked");
                        if (nameOnBaseField != null) nameOnBaseField.SetValue(include, mapping.Key);
                        if (nameOnLinkedField != null) nameOnLinkedField.SetValue(include, mapping.Value);
                        addMethod.Invoke(includesList, new object[] { include });
                    }
                    includesField.SetValue(linkFeature, includesList);
                }


                var includeAllField = blendShapeLinkType.GetField("includeAll");
                if (includeAllField != null)
                {
                    includeAllField.SetValue(linkFeature, false);
                }



                var newVrcFury = avatarRoot.AddComponent(vrcFuryType);
                var contentField = vrcFuryType.GetField("content",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (contentField != null)
                {
                    contentField.SetValue(newVrcFury, linkFeature);
                    if (data.debugMode)
                    {
                        Debug.Log($"[AttachToBlendshapeProcessor] Created VRCFury BlendShapeLink feature with {mappings.Count} mappings", data);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to add BlendShapeLink feature: {ex.Message}", data);
                Debug.LogException(ex, data);
            }
        }

        /// <summary>
        /// Recursively gets all states from an AnimatorStateMachine, including nested state machines.
        /// </summary>
        private List<AnimatorState> GetAllStates(AnimatorStateMachine stateMachine)
        {
            var states = new List<AnimatorState>();

            if (stateMachine == null)
                return states;


            states.AddRange(stateMachine.states.Select(s => s.state));


            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                states.AddRange(GetAllStates(subStateMachine.stateMachine));
            }

            return states;
        }

    }
}
