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
using YUCP.Components.Editor.Animations;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Attach to Blendshape components during avatar build.
    /// Detects surface clusters, samples blendshape deformations, solves transforms,
    /// generates animation clips, and creates VRCFury components for dynamic positioning.
    /// </summary>
    public class AttachToBlendshapeProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 10;

        private const string KeepAliveLayerName = "[YUCP] Keep Blendshapes (Optimizer Guard)";
        private const string KeepAliveStateName = "Keep (Not Used)";

        private static void EnsureSingleBoneSkinning(
            SkinnedMeshRenderer renderer,
            Mesh mesh,
            Transform bone,
            AttachToBlendshapeData data)
        {
            if (renderer == null || mesh == null || bone == null) return;

            // If the mesh already has valid skinning or blendshapes, leave it alone.
            // This is primarily for MeshFilter->SkinnedMeshRenderer conversions.
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

            // Bindpose converts from mesh space -> bone space.
            // Mesh space is renderer local space, so use renderer.localToWorldMatrix.
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

            var progressWindow = YUCPProgressWindow.Create();
            progressWindow.Progress(0, "Processing blendshape attachments...");

            try
            {
                var animator = avatarRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[AttachToBlendshapeProcessor] No Animator found on avatar");
                    progressWindow.CloseWindow();
                    return true;
                }

                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];

                    if (!ValidateData(data))
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Validation failed for '{data.name}'", data);
                        continue;
                    }

                    try
                    {
                        ProcessAttachment(data, avatarRoot, animator);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Error processing '{data.name}': {ex.Message}", data);
                        Debug.LogException(ex);
                    }

                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed blendshape attachment {i + 1}/{dataList.Length}");
                }

                progressWindow.CloseWindow();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Fatal error: {ex.Message}");
                progressWindow.CloseWindow();
                return false;
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

        private AttachToBlendshapeOutputMode ResolveOutputMode(
            AttachToBlendshapeData data,
            GameObject avatarRoot,
            List<string> trackedBlendshapes)
        {
            if (data == null) return AttachToBlendshapeOutputMode.LinkedRendererBake;

            // Visemes are driven by VRChat's lip sync engine directly on the base mesh
            // at runtime (not via animation clips). The only way for attachment geometry
            // to follow visemes is to be part of the base mesh.
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

            // Explicit override
            if (data.outputMode != AttachToBlendshapeOutputMode.Auto)
            {
                return data.outputMode;
            }

            // Auto mode: inspect animation clips to decide whether Merge is safe.
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

        private bool MergeIntoBaseMesh(AttachToBlendshapeData data, GameObject avatarRoot)
        {
            if (data == null || avatarRoot == null || data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                return false;
            }

            // Resolve which mesh we are merging (defaults to this GameObject if not specified)
            if (!TryGetTargetMeshToModify(data, out var attachmentMesh, out var attachmentTransform, out var attachmentMaterials, out var attachmentSmr, out var attachmentMr))
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Merge failed: could not resolve attachment mesh to merge.", data);
                return false;
            }

            var baseSmr = data.targetMesh;
            var baseMesh = baseSmr.sharedMesh;

            // Build combined triangle arrays (needed because MeshCollider triangleIndex is global)
            var baseTrianglesCombined = SkinnedMeshMerge.GetCombinedTriangles(baseMesh);
            var attachmentTrianglesCombined = SkinnedMeshMerge.GetCombinedTriangles(attachmentMesh);

            // Attachment vertex positions in base local (for mapping + delta computation)
            var attVertsLocal = attachmentMesh.vertices;
            var attVertsInBaseLocal = new Vector3[attVertsLocal.Length];
            for (int i = 0; i < attVertsLocal.Length; i++)
            {
                var wPos = attachmentTransform.TransformPoint(attVertsLocal[i]);
                attVertsInBaseLocal[i] = baseSmr.transform.InverseTransformPoint(wPos);
            }

            // Map attachment vertices to base surface
            var maps = ClosestSurfaceMapper.MapAttachmentVerticesToBaseSurface(
                baseSmr,
                baseMesh,
                attVertsInBaseLocal,
                data,
                out var matchedCount);

            if (matchedCount == 0)
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Merge failed: no attachment vertices could be mapped to the base surface.", data);
                return false;
            }

            // Merge geometry + materials + skinning
            var merge = SkinnedMeshMerge.Merge(
                baseSmr,
                attachmentMesh,
                attachmentTransform,
                attachmentMaterials,
                maps,
                baseTrianglesCombined,
                data);

            if (merge.mergedMesh == null)
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Merge failed: merged mesh was null.", data);
                return false;
            }

            // Rebuild base blendshapes onto the merged mesh, extending frames with attachment deltas
            var bakedOk = BakeBaseBlendshapesIntoMergedMesh(
                data,
                avatarRoot,
                baseSmr,
                baseMesh,
                merge.mergedMesh,
                maps,
                baseTrianglesCombined,
                attachmentTrianglesCombined,
                attVertsInBaseLocal,
                merge.baseVertexCount,
                merge.attachmentVertexStart,
                merge.attachmentVertexCount);

            if (!bakedOk)
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Merge failed: could not bake base blendshapes into merged mesh.", data);
                return false;
            }

            // Assign merged output
            baseSmr.sharedMesh = merge.mergedMesh;
            baseSmr.sharedMaterials = merge.mergedMaterials;
            EditorUtility.SetDirty(baseSmr);

            // Disable original renderer (keep the transform hierarchy for toggles/constraints)
            if (data.disableOriginalRendererOnMerge)
            {
                if (attachmentSmr != null) attachmentSmr.enabled = false;
                if (attachmentMr != null) attachmentMr.enabled = false;
            }

            // Preserve toggle-style transform animations by converting them into blendshapes on the merged mesh
            if (data.preserveToggleStyleTransformAnimationsOnMerge)
            {
                RetargetToggleStyleTransformAnimationsToBlendshapes(
                    data,
                    avatarRoot,
                    baseSmr,
                    merge.mergedMesh,
                    merge.attachmentVertexStart,
                    merge.attachmentVertexCount);
            }

            return true;
        }

        private static bool TryGetTargetMeshToModify(
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

            // Fallback to same GameObject
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

        private bool BakeBaseBlendshapesIntoMergedMesh(
            AttachToBlendshapeData data,
            GameObject avatarRoot,
            SkinnedMeshRenderer baseSmr,
            Mesh baseMesh,
            Mesh mergedMesh,
            ClosestSurfaceMapper.SurfaceMap[] maps,
            int[] baseTrianglesCombined,
            int[] attachmentTrianglesCombined,
            Vector3[] attachmentVertsInBaseLocal,
            int baseVertexCount,
            int attachmentVertexStart,
            int attachmentVertexCount)
        {
            if (baseSmr == null || baseMesh == null || mergedMesh == null) return false;
            if (baseMesh.blendShapeCount == 0) return true;

            // Save weights, then sample base mesh at neutral and per-frame weights
            int bsCount = baseMesh.blendShapeCount;
            var originalWeights = new float[bsCount];
            for (int i = 0; i < bsCount; i++) originalWeights[i] = baseSmr.GetBlendShapeWeight(i);

            var bake0 = new Mesh();
            var bakeW = new Mesh();
            try
            {
                // Neutral pose
                for (int i = 0; i < bsCount; i++) baseSmr.SetBlendShapeWeight(i, 0f);
                baseSmr.BakeMesh(bake0);
                var baseVerts0 = bake0.vertices;
                if (baseVerts0 == null || baseVerts0.Length != baseVertexCount)
                {
                    baseVerts0 = baseMesh.vertices;
                }

                // For each blendshape/frame on base mesh, extend with attachment deltas
                for (int bi = 0; bi < bsCount; bi++)
                {
                    string bsName = baseMesh.GetBlendShapeName(bi);
                    int frameCount = baseMesh.GetBlendShapeFrameCount(bi);

                    for (int fi = 0; fi < frameCount; fi++)
                    {
                        float frameWeight = baseMesh.GetBlendShapeFrameWeight(bi, fi);

                        // Bake base mesh at this weight (only this blendshape active)
                        for (int i = 0; i < bsCount; i++) baseSmr.SetBlendShapeWeight(i, i == bi ? frameWeight : 0f);
                        baseSmr.BakeMesh(bakeW);
                        var baseVertsW = bakeW.vertices;
                        if (baseVertsW == null || baseVertsW.Length != baseVertexCount)
                        {
                            // Fallback: approximate by applying stored frame deltas to baseVerts0
                            baseVertsW = baseVerts0;
                        }

                        // Attachment deltas for this frame weight
                        var attDeltas = ClosestSurfaceMapper.ComputeAttachmentDeltasFromBaseSurface(
                            maps,
                            baseVerts0,
                            baseVertsW,
                            baseTrianglesCombined,
                            attachmentVertsInBaseLocal,
                            attachmentTrianglesCombined,
                            data.unmatchedHandling);

                        // Base deltas from original mesh frame
                        var baseDeltaVerts = new Vector3[baseVertexCount];
                        baseMesh.GetBlendShapeFrameVertices(bi, fi, baseDeltaVerts, null, null);

                        var fullDeltaVerts = new Vector3[baseVertexCount + attachmentVertexCount];
                        Array.Copy(baseDeltaVerts, 0, fullDeltaVerts, 0, baseVertexCount);

                        for (int v = 0; v < attachmentVertexCount && v < attDeltas.Length; v++)
                        {
                            fullDeltaVerts[attachmentVertexStart + v] = attDeltas[v];
                        }

                        mergedMesh.AddBlendShapeFrame(bsName, frameWeight, fullDeltaVerts, null, null);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Error baking base blendshapes into merged mesh: {ex.Message}", data);
                Debug.LogException(ex, data);
                return false;
            }
            finally
            {
                // Restore original weights
                for (int i = 0; i < bsCount; i++) baseSmr.SetBlendShapeWeight(i, originalWeights[i]);
                UnityEngine.Object.DestroyImmediate(bake0);
                UnityEngine.Object.DestroyImmediate(bakeW);
            }
        }

        private void RetargetToggleStyleTransformAnimationsToBlendshapes(
            AttachToBlendshapeData data,
            GameObject avatarRoot,
            SkinnedMeshRenderer baseSmr,
            Mesh mergedMesh,
            int attachmentVertexStart,
            int attachmentVertexCount)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            string baseMeshPath = AnimationUtility.CalculateTransformPath(baseSmr.transform, avatarRoot.transform);
            string objectPath = AnimationUtility.CalculateTransformPath(data.transform, avatarRoot.transform);
            if (string.IsNullOrEmpty(baseMeshPath) || string.IsNullOrEmpty(objectPath)) return;

            // Snapshot current transform as "rest"
            Vector3 restLocalPos = data.transform.localPosition;
            Quaternion restLocalRot = data.transform.localRotation;
            Vector3 restLocalScale = data.transform.localScale;

            // Pivot in base local space
            Vector3 pivotBaseLocal = baseSmr.transform.InverseTransformPoint(data.transform.position);

            foreach (var clip in CollectAllAnimationClips(descriptor))
            {
                if (clip == null) continue;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                if (bindings == null || bindings.Length == 0) continue;

                // Collect transform bindings for this object path
                var tBindings = new List<EditorCurveBinding>();
                foreach (var b in bindings)
                {
                    if (b.path == objectPath && b.type == typeof(Transform) && IsTransformBinding(b.propertyName))
                    {
                        var c = AnimationUtility.GetEditorCurve(clip, b);
                        if (c == null) continue;
                        tBindings.Add(b);
                        if (!IsCurveConstant(c))
                        {
                            // Not toggle-style -> skip (Auto mode should have avoided merge)
                            tBindings.Clear();
                            break;
                        }
                    }
                }
                if (tBindings.Count == 0) continue;

                // Evaluate target transform at time 0
                Vector3 targetLocalPos = restLocalPos;
                Quaternion targetLocalRot = restLocalRot;
                Vector3 targetLocalScale = restLocalScale;

                float px = Eval(clip, objectPath, "m_LocalPosition.x", restLocalPos.x);
                float py = Eval(clip, objectPath, "m_LocalPosition.y", restLocalPos.y);
                float pz = Eval(clip, objectPath, "m_LocalPosition.z", restLocalPos.z);
                targetLocalPos = new Vector3(px, py, pz);

                float rx = Eval(clip, objectPath, "m_LocalRotation.x", restLocalRot.x);
                float ry = Eval(clip, objectPath, "m_LocalRotation.y", restLocalRot.y);
                float rz = Eval(clip, objectPath, "m_LocalRotation.z", restLocalRot.z);
                float rw = Eval(clip, objectPath, "m_LocalRotation.w", restLocalRot.w);
                targetLocalRot = new Quaternion(rx, ry, rz, rw);
                // Unity's Quaternion doesn't expose sqrMagnitude; compute it manually before normalizing.
                float qLenSqr =
                    targetLocalRot.x * targetLocalRot.x +
                    targetLocalRot.y * targetLocalRot.y +
                    targetLocalRot.z * targetLocalRot.z +
                    targetLocalRot.w * targetLocalRot.w;
                if (qLenSqr > 1e-6f) targetLocalRot = Quaternion.Normalize(targetLocalRot);

                float sx = Eval(clip, objectPath, "m_LocalScale.x", restLocalScale.x);
                float sy = Eval(clip, objectPath, "m_LocalScale.y", restLocalScale.y);
                float sz = Eval(clip, objectPath, "m_LocalScale.z", restLocalScale.z);
                targetLocalScale = new Vector3(sx, sy, sz);

                // Convert position/rotation deltas into base local space via world
                Transform parent = data.transform.parent;
                Vector3 restWorldPos = parent != null ? parent.TransformPoint(restLocalPos) : data.transform.root.TransformPoint(restLocalPos);
                Vector3 targetWorldPos = parent != null ? parent.TransformPoint(targetLocalPos) : data.transform.root.TransformPoint(targetLocalPos);
                Vector3 worldDeltaPos = targetWorldPos - restWorldPos;
                Vector3 baseLocalDeltaPos = baseSmr.transform.InverseTransformVector(worldDeltaPos);

                Quaternion restWorldRot = parent != null ? parent.rotation * restLocalRot : restLocalRot;
                Quaternion targetWorldRot = parent != null ? parent.rotation * targetLocalRot : targetLocalRot;
                Quaternion worldDeltaRot = targetWorldRot * Quaternion.Inverse(restWorldRot);
                Quaternion baseLocalDeltaRot = Quaternion.Inverse(baseSmr.transform.rotation) * worldDeltaRot * baseSmr.transform.rotation;

                Vector3 scaleRatio = new Vector3(
                    SafeRatio(targetLocalScale.x, restLocalScale.x),
                    SafeRatio(targetLocalScale.y, restLocalScale.y),
                    SafeRatio(targetLocalScale.z, restLocalScale.z));

                // Create a unique blendshape for this clip toggle pose
                string toggleBsName = MakeSafeBlendshapeName($"__YUCP_Toggle_{clip.name}_{data.GetInstanceID()}");
                if (mergedMesh.GetBlendShapeIndex(toggleBsName) >= 0)
                {
                    // Avoid collisions
                    toggleBsName = MakeSafeBlendshapeName($"__YUCP_Toggle_{clip.name}_{data.GetInstanceID()}_{Guid.NewGuid().ToString("N").Substring(0, 6)}");
                }

                var verts = mergedMesh.vertices;
                var fullDelta = new Vector3[verts.Length];
                for (int i = 0; i < attachmentVertexCount; i++)
                {
                    int idx = attachmentVertexStart + i;
                    if ((uint)idx >= (uint)verts.Length) break;
                    var v = verts[idx];
                    var rel = v - pivotBaseLocal;
                    rel = Vector3.Scale(rel, scaleRatio);
                    rel = baseLocalDeltaRot * rel;
                    var v2 = pivotBaseLocal + rel + baseLocalDeltaPos;
                    fullDelta[idx] = v2 - v;
                }

                mergedMesh.AddBlendShapeFrame(toggleBsName, 100f, fullDelta, null, null);

                // Rewrite clip: remove transform curves for this objectPath and add blendshape curve on base mesh
                foreach (var b in tBindings)
                {
                    AnimationUtility.SetEditorCurve(clip, b, null);
                }

                var bsBinding = EditorCurveBinding.FloatCurve(baseMeshPath, typeof(SkinnedMeshRenderer), $"blendShape.{toggleBsName}");
                var bsCurve = new AnimationCurve(
                    new Keyframe(0f, 100f),
                    new Keyframe(1f / 60f, 100f));
                AnimationUtility.SetEditorCurve(clip, bsBinding, bsCurve);
                EditorUtility.SetDirty(clip);

                if (data.debugMode)
                {
                    Debug.Log($"[AttachToBlendshapeProcessor] Retargeted toggle-style transform clip '{clip.name}' to blendshape '{toggleBsName}' on base mesh", data);
                }
            }

            float Eval(AnimationClip clip, string path, string prop, float fallback)
            {
                var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), prop);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) return fallback;
                return curve.Evaluate(0f);
            }
        }

        private static float SafeRatio(float a, float b)
        {
            if (Mathf.Abs(b) < 1e-6f) return 1f;
            return a / b;
        }

        private static string MakeSafeBlendshapeName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "__YUCP_Toggle";
            var chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.') continue;
                chars[i] = '_';
            }
            return new string(chars);
        }

        private void ProcessAttachment(AttachToBlendshapeData data, GameObject avatarRoot, Animator animator)
        {
            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Processing attachment for '{data.name}'", data);
            }

            // Step 1: Detect surface cluster
            SurfaceCluster cluster = SurfaceClusterDetector.DetectCluster(
                data.targetMesh,
                data.transform.position,
                data.clusterTriangleCount,
                data.searchRadius,
                data.manualTriangleIndex);

            if (cluster == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to detect surface cluster for '{data.name}'", data);
                return;
            }

            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Detected cluster with {cluster.anchors.Count} triangles", data);
            }

            // Step 2: Determine which blendshapes to track
            List<string> blendshapesToTrack = DetermineBlendshapesToTrack(data, avatarRoot, cluster);

            if (blendshapesToTrack.Count == 0)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No blendshapes to track for '{data.name}'", data);
                return;
            }

            Debug.Log($"[AttachToBlendshapeProcessor] Tracking {blendshapesToTrack.Count} blendshapes: {string.Join(", ", blendshapesToTrack)}", data);

            // Step 3: Create base bone attachment
            string bonePath = "";
            if (data.attachToClosestBone)
            {
                bonePath = AttachToClosestBone(data, animator);
                if (data.debugMode)
                {
                    Debug.Log($"[AttachToBlendshapeProcessor] Attached to bone: '{bonePath}'", data);
                }
            }

            // Step 4: Transfer blendshapes to target mesh
            bool transferSuccess = BlendshapeTransfer.TransferBlendshapes(
                data.targetMesh,
                data.targetMeshToModify,
                blendshapesToTrack,
                cluster,
                data);

            if (!transferSuccess)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to transfer blendshapes for '{data.name}'", data);
                return;
            }

            // Step 5: Choose output mode (Auto by default; visemes force merge)
            var outputModeUsed = ResolveOutputMode(data, avatarRoot, blendshapesToTrack);

            // Step 6: Bake output
            bool bakedOk = true;
            if (outputModeUsed == AttachToBlendshapeOutputMode.MergeIntoBaseMesh)
            {
                bakedOk = MergeIntoBaseMesh(data, avatarRoot);
                if (!bakedOk)
                {
                    Debug.LogWarning(
                        $"[AttachToBlendshapeProcessor] MergeIntoBaseMesh failed for '{data.name}', falling back to LinkedRendererBake.",
                        data);
                    outputModeUsed = AttachToBlendshapeOutputMode.LinkedRendererBake;
                }
            }

            if (outputModeUsed == AttachToBlendshapeOutputMode.LinkedRendererBake)
            {
                // Bake motion into blendshapes on the attachment mesh and link them via VRCFury BlendShapeLink
                CreateTransformBlendshapesAndLink(data, blendshapesToTrack, avatarRoot);
            }

            // Step 7: Set build statistics
            data.SetBuildStats(cluster, blendshapesToTrack, blendshapesToTrack.Count, bonePath, outputModeUsed);

            Debug.Log($"[AttachToBlendshapeProcessor] Successfully processed '{data.name}': " +
                     $"Transferred {blendshapesToTrack.Count} blendshapes, {cluster.anchors.Count} triangle cluster", data);
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
                    // Validate that they exist
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
            // Find all bones
            List<Transform> allBones = FindAllBones(animator, data.transform);

            // Filter bones
            List<Transform> filteredBones = FilterBones(allBones, data, animator);

            if (filteredBones.Count == 0)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No bones found for '{data.name}'", data);
                return "";
            }

            // Find closest bone
            Transform closestBone = FindClosestBone(data.transform, filteredBones, data.boneSearchRadius);

            if (closestBone == null)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No bone within range for '{data.name}'", data);
                return "";
            }

            // Get bone path
            string bonePath = GetBonePath(closestBone, animator.transform);

            // Create VRCFury armature link
            var link = FuryComponents.CreateArmatureLink(data.gameObject);
            if (link == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to create armature link for '{data.name}'", data);
                return bonePath;
            }

            // Link to bone
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

        // Bone finding utilities (similar to AttachToClosestBoneProcessor)
        private List<Transform> FindAllBones(Animator animator, Transform exclude)
        {
            var bones = new List<Transform>();
            CollectBonesRecursive(animator.transform, bones, exclude);
            return bones;
        }

        private void CollectBonesRecursive(Transform current, List<Transform> bones, Transform exclude)
        {
            if (current == exclude || IsDescendantOf(current, exclude))
            {
                return;
            }

            if (current.GetComponent<Animator>() == null)
            {
                bones.Add(current);
            }

            for (int i = 0; i < current.childCount; i++)
            {
                CollectBonesRecursive(current.GetChild(i), bones, exclude);
            }
        }

        private bool IsDescendantOf(Transform child, Transform parent)
        {
            Transform current = child;
            while (current != null)
            {
                if (current == parent)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private List<Transform> FilterBones(List<Transform> bones, AttachToBlendshapeData data, Animator animator)
        {
            var filtered = new List<Transform>();

            foreach (var bone in bones)
            {
                if (data.ignoreHumanoidBones && IsHumanoidBone(bone, animator))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(data.boneNameFilter))
                {
                    if (!bone.name.ToLower().Contains(data.boneNameFilter.ToLower()))
                    {
                        continue;
                    }
                }

                filtered.Add(bone);
            }

            return filtered;
        }

        private bool IsHumanoidBone(Transform bone, Animator animator)
        {
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var humanBone = (HumanBodyBones)i;
                var humanTransform = animator.GetBoneTransform(humanBone);
                if (humanTransform == bone)
                {
                    return true;
                }
            }
            return false;
        }

        private Transform FindClosestBone(Transform target, List<Transform> bones, float maxDistance)
        {
            Transform closest = null;
            float closestDistance = float.MaxValue;

            foreach (var bone in bones)
            {
                float distance = Vector3.Distance(target.position, bone.position);

                if (maxDistance > 0 && distance > maxDistance)
                {
                    continue;
                }

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = bone;
                }
            }

            return closest;
        }

        private string GetBonePath(Transform bone, Transform root)
        {
            var pathParts = new List<string>();
            Transform current = bone;

            while (current != null && current != root)
            {
                pathParts.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", pathParts);
        }

        /// <summary>
        /// Creates blendshapes on the ATTACHED OBJECT mesh which represent the solved rigid motion at each weight.
        /// Then uses VRCFury's BlendShapeLink to mirror the base mesh's blendshape weights onto these new shapes.
        ///
        /// Key detail: Unity's Mesh.AddBlendShapeFrame expects DELTA vertices, not absolute vertices.
        /// Each frame we compute v' = R(w) * v + T(w) (about the object pivot/origin, like Preview),
        /// then delta = v' - v and store that as the frame.
        /// </summary>
        private void CreateTransformBlendshapesAndLink(AttachToBlendshapeData data, List<string> blendshapesToTrack, GameObject avatarRoot)
        {
            // Ensure the attached object is rendered by a SkinnedMeshRenderer so blendshapes can drive it.
            SkinnedMeshRenderer targetMeshRenderer = data.transform.GetComponent<SkinnedMeshRenderer>();
            Mesh targetMesh = null;

            if (targetMeshRenderer == null) {
                var meshFilter = data.transform.GetComponent<MeshFilter>();
                var meshRenderer = data.transform.GetComponent<MeshRenderer>();
                if (meshFilter == null || meshFilter.sharedMesh == null) {
                    Debug.LogError($"[AttachToBlendshapeProcessor] Target object '{data.transform.name}' has no mesh to bake transform blendshapes into (needs MeshFilter or SkinnedMeshRenderer).", data);
                    return;
                }

                // Convert MeshFilter/MeshRenderer -> SkinnedMeshRenderer (so blendshapes can apply).
                targetMeshRenderer = data.transform.gameObject.AddComponent<SkinnedMeshRenderer>();
                targetMeshRenderer.sharedMesh = meshFilter.sharedMesh;
                if (meshRenderer != null) {
                    targetMeshRenderer.sharedMaterials = meshRenderer.sharedMaterials;
                    meshRenderer.enabled = false; // prevent double-render
                }

                // Minimal skinning setup will be created on the copied mesh below.
            }

            targetMesh = targetMeshRenderer.sharedMesh;
            if (targetMesh == null) {
                Debug.LogError($"[AttachToBlendshapeProcessor] Target SkinnedMeshRenderer '{targetMeshRenderer.name}' has no mesh", data);
                return;
            }

            // Always work on a copy so we don't permanently mutate an imported/asset mesh and so repeated builds don't stack shapes.
            // (Unity doesn't allow removing blendshapes from a Mesh, so we replace the mesh each build.)
            var targetMeshCopy = UnityEngine.Object.Instantiate(targetMesh);
            targetMeshCopy.name = $"{targetMesh.name}_YUCP_AttachToBlendshape";
            targetMeshRenderer.sharedMesh = targetMeshCopy;
            targetMesh = targetMeshCopy;
            EnsureSingleBoneSkinning(targetMeshRenderer, targetMeshCopy, data.transform, data);

            // Get base mesh path for VRCFury
            string baseMeshName = data.targetMesh.transform.name;

            // Create blendshapes on target mesh that represent the solved rigid motion
            Dictionary<string, string> blendshapeMappings = new Dictionary<string, string>();

            // Cache base vertices once; each frame uses deltas against this base.
            Vector3[] baseVertices;
            try
            {
                baseVertices = targetMesh.vertices;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[AttachToBlendshapeProcessor] Cannot read vertices from mesh '{targetMesh.name}'. " +
                    $"Enable Read/Write on the mesh import settings (or provide a readable mesh). Error: {ex.Message}",
                    data);
                return;
            }
            int vertexCount = baseVertices.Length;
            float maxTranslation = 0f;

            foreach (string blendshapeName in blendshapesToTrack)
            {
                var samples = BlendshapeTransfer.GetTransformSamples(blendshapeName);
                if (samples.Count == 0)
                    continue;

                // Create a blendshape name for the transform
                string transformBlendshapeName = $"_YUCP_Transform_{blendshapeName}";
                blendshapeMappings[blendshapeName] = transformBlendshapeName;

                // Use the exact sampled weights from the solver (matches Preview sampling density).
                var sortedSamples = samples.OrderBy(s => s.blendshapeWeight).ToList();

                foreach (var sample in sortedSamples) {
                    // Build deltaVertices for this frame: delta = (R * v + T) - v
                    // This simulates changing the object transform about its pivot (origin), like Preview does.
                    var deltaVertices = new Vector3[vertexCount];
                    var rot = sample.rotationDelta;
                    var pos = sample.positionDelta;
                    maxTranslation = Mathf.Max(maxTranslation, pos.magnitude);

                    for (int i = 0; i < vertexCount; i++) {
                        var v = baseVertices[i];
                        var v2 = (rot * v) + pos;
                        deltaVertices[i] = v2 - v;
                    }

                    // Add blendshape frame at the sampled weight.
                    targetMesh.AddBlendShapeFrame(transformBlendshapeName, sample.blendshapeWeight, deltaVertices, null, null);
                }
            }

            // Ensure bounds won't cull the mesh when vertices translate due to blendshape frames.
            // SkinnedMeshRenderer uses localBounds for culling.
            targetMesh.RecalculateBounds();
            var b = targetMesh.bounds;
            var expand = maxTranslation * 2f + 0.05f;
            b.extents += new Vector3(expand, expand, expand);
            targetMeshRenderer.localBounds = b;

            // Get or create VRCFury component on avatar root
            var vrcFuryType = System.Type.GetType("VF.Model.VRCFury, VRCFury");
            if (vrcFuryType == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Could not find VRCFury type", data);
                return;
            }
            
            var vrcFury = avatarRoot.GetComponent(vrcFuryType);
            if (vrcFury == null)
            {
                vrcFury = avatarRoot.AddComponent(vrcFuryType);
            }

            // Add BlendShapeLink feature using reflection (since it's internal)
            AddBlendShapeLinkFeature(vrcFury, baseMeshName, targetMeshRenderer, blendshapeMappings, data, avatarRoot, vrcFuryType);

            // VRCFury's Blendshape Optimizer bakes any blendshape that is not animated by clips.
            // Our BlendShapeLink drives weights at runtime (not via animation curves), so the optimizer would bake them away.
            // Add a zero-weight Animator layer containing a never-used clip that "animates" these weights, so the optimizer keeps them.
            EnsureBlendshapesSurviveVrcFuryOptimizer(avatarRoot, targetMeshRenderer, blendshapesToTrack, blendshapeMappings.Values, data);

            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Created {blendshapeMappings.Count} transform blendshapes (multi-frame deltas) and linked them via VRCFury BlendShapeLink", data);
            }
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

            // Prefer FX, then fall back to any non-default controller.
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
        /// Creates and injects a Direct BlendTree AnimatorController via VRCFury's FullController API.
        /// This is the ONLY runtime mechanism for transform animation - VRCFury FullController handles
        /// all transform updates synced to blendshape weights through Unity's animator system.
        /// </summary>
        private void InjectDirectBlendTreeController(
            AttachToBlendshapeData data,
            GameObject avatarRoot,
            string objectPath,
            Dictionary<string, List<MeshUtils.TransformSample>> editorSamples)
        {
            if (editorSamples == null || editorSamples.Count == 0)
            {
                if (data.debugMode)
                {
                    Debug.LogWarning($"[AttachToBlendshapeProcessor] No transform samples for Direct BlendTree injection", data);
                }
                return;
            }

            // Get base transform values
            Vector3 baseLocalPosition = data.transform.localPosition;
            Quaternion baseLocalRotation = data.transform.localRotation;

            // Generate the AnimatorController with Direct BlendTree
            var controller = BlendshapeAnimationGenerator.CreateBlendshapeTransformController(
                objectPath,
                editorSamples,
                baseLocalPosition,
                baseLocalRotation,
                out var blendshapeToParamMap);

            if (controller == null)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] Failed to create Direct BlendTree controller for '{data.name}'", data);
                return;
            }

            // Inject via VRCFury's FullController API
            try
            {
                var furyController = FuryComponents.CreateFullController(avatarRoot);
                furyController.AddController(controller, VRCAvatarDescriptor.AnimLayerType.FX);

                // Mark all our parameters as global so they can be driven
                foreach (var paramName in blendshapeToParamMap.Values)
                {
                    furyController.AddGlobalParam(paramName);
                }

                if (data.debugMode)
                {
                    Debug.Log($"[AttachToBlendshapeProcessor] Injected Direct BlendTree controller with {blendshapeToParamMap.Count} blendshape parameters via VRCFury FullController", data);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to inject Direct BlendTree controller: {ex.Message}", data);
                Debug.LogException(ex, data);
            }
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
                // Use reflection to create and add BlendShapeLink feature
                var blendShapeLinkType = System.Type.GetType("VF.Model.Feature.BlendShapeLink, VRCFury");
                if (blendShapeLinkType == null)
                {
                    Debug.LogWarning($"[AttachToBlendshapeProcessor] Could not find BlendShapeLink type. Transform blendshapes created but not linked.", data);
                    return;
                }

                var linkFeature = System.Activator.CreateInstance(blendShapeLinkType);
                
                // Set base mesh name
                var baseObjField = blendShapeLinkType.GetField("baseObj");
                if (baseObjField != null)
                {
                    baseObjField.SetValue(linkFeature, baseMeshName);
                }

                // Set link skins
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

                // Set includes (mappings)
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

                // Set includeAll to false since we're using specific includes
                var includeAllField = blendShapeLinkType.GetField("includeAll");
                if (includeAllField != null)
                {
                    includeAllField.SetValue(linkFeature, false);
                }

                // Add to VRCFury - create a new VRCFury component for this feature
                // VRCFury's new system uses one feature per component
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
        /// Creates transform animation curves that sync with blendshape animations.
        /// Finds all animation clips that control the base mesh's blendshapes and adds
        /// corresponding transform curves to make the object move/rotate with the blendshapes.
        /// </summary>
        private void CreateTransformAnimations(
            AttachToBlendshapeData data,
            List<string> blendshapesToTrack,
            GameObject avatarRoot,
            Animator animator)
        {
            if (data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] Cannot create transform animations - source mesh is null", data);
                return;
            }

            // Get paths for animation bindings
            // Try multiple path formats since Unity/VRCFury might use different formats
            string baseMeshPath = AnimationUtility.CalculateTransformPath(data.targetMesh.transform, avatarRoot.transform);
            string objectPath = AnimationUtility.CalculateTransformPath(data.transform, avatarRoot.transform);
            
            // Also try path without root name (VRCFury sometimes uses this)
            string baseMeshPathNoRoot = baseMeshPath;
            if (baseMeshPath.Contains("/"))
            {
                var parts = baseMeshPath.Split('/');
                if (parts.Length > 1)
                {
                    baseMeshPathNoRoot = string.Join("/", parts.Skip(1));
                }
            }

            if (string.IsNullOrEmpty(baseMeshPath) || string.IsNullOrEmpty(objectPath))
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] Failed to get paths for animation bindings. Base: '{baseMeshPath}', Object: '{objectPath}'", data);
                return;
            }

            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Paths - Base mesh: '{baseMeshPath}' (no root: '{baseMeshPathNoRoot}'), Object: '{objectPath}'", data);
            }

            // Get base transform values (for calculating absolute positions from deltas)
            Vector3 baseLocalPosition = data.transform.localPosition;
            Quaternion baseLocalRotation = data.transform.localRotation;

            // Get all controllers from the avatar
            var avatarDescriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (avatarDescriptor == null)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] Avatar descriptor not found", data);
                return;
            }

            // Get all animation controllers from the avatar
            List<AnimatorController> allControllers = new List<AnimatorController>();
            
            // Get base animation layers
            foreach (var layer in avatarDescriptor.baseAnimationLayers)
            {
                if (layer.isDefault) continue;
                if (layer.animatorController is AnimatorController ac)
                {
                    allControllers.Add(ac);
                }
            }
            
            // Get special animation layers
            foreach (var layer in avatarDescriptor.specialAnimationLayers)
            {
                if (layer.isDefault) continue;
                if (layer.animatorController is AnimatorController ac)
                {
                    allControllers.Add(ac);
                }
            }
            
            int curvesAdded = 0;

            // Process each blendshape
            foreach (string blendshapeName in blendshapesToTrack)
            {
                // Get transform samples for this blendshape
                var samples = BlendshapeTransfer.GetTransformSamples(blendshapeName);
                if (samples.Count == 0)
                {
                    if (data.debugMode)
                    {
                        Debug.LogWarning($"[AttachToBlendshapeProcessor] No transform samples found for blendshape '{blendshapeName}'", data);
                    }
                    continue;
                }

                // Create animation curves from samples
                // Curves map blendshape weight (0-100) to transform deltas
                AnimationCurve posX = new AnimationCurve();
                AnimationCurve posY = new AnimationCurve();
                AnimationCurve posZ = new AnimationCurve();
                AnimationCurve rotX = new AnimationCurve();
                AnimationCurve rotY = new AnimationCurve();
                AnimationCurve rotZ = new AnimationCurve();
                AnimationCurve rotW = new AnimationCurve();

                foreach (var sample in samples.OrderBy(s => s.blendshapeWeight))
                {
                    float time = sample.blendshapeWeight / 100f; // Normalize to 0-1

                    // Position curves (deltas from base)
                    posX.AddKey(new Keyframe(time, sample.positionDelta.x));
                    posY.AddKey(new Keyframe(time, sample.positionDelta.y));
                    posZ.AddKey(new Keyframe(time, sample.positionDelta.z));

                    // Rotation curves (deltas from base)
                    rotX.AddKey(new Keyframe(time, sample.rotationDelta.x));
                    rotY.AddKey(new Keyframe(time, sample.rotationDelta.y));
                    rotZ.AddKey(new Keyframe(time, sample.rotationDelta.z));
                    rotW.AddKey(new Keyframe(time, sample.rotationDelta.w));
                }

                // Find all animation clips that control this blendshape
                string blendshapePropertyName = "blendShape." + blendshapeName;
                bool foundAnyClips = false;

                if (data.debugMode)
                {
                    Debug.Log($"[AttachToBlendshapeProcessor] Searching for blendshape '{blendshapeName}' (property: '{blendshapePropertyName}') on path '{baseMeshPath}' in {allControllers.Count} controllers", data);
                }

                foreach (var controller in allControllers)
                {
                    if (data.debugMode)
                    {
                        Debug.Log($"[AttachToBlendshapeProcessor] Checking controller '{controller.name}' with {controller.layers.Length} layers", data);
                    }

                    // Iterate through all clips in this controller
                    foreach (var layer in controller.layers)
                    {
                        if (layer.stateMachine == null) continue;
                        
                        // Get all states from this layer
                        var states = GetAllStates(layer.stateMachine);
                        
                        if (data.debugMode)
                        {
                            Debug.Log($"[AttachToBlendshapeProcessor] Layer '{layer.name}' has {states.Count} states", data);
                        }

                        foreach (var state in states)
                        {
                            if (state.motion == null || !(state.motion is AnimationClip clip))
                                continue;

                            // Get all float bindings from the clip
                            var bindings = AnimationUtility.GetCurveBindings(clip);
                            
                            if (data.debugMode && bindings.Length > 0)
                            {
                                var blendshapeBindings = bindings.Where(b => b.propertyName.StartsWith("blendShape.")).ToArray();
                                if (blendshapeBindings.Length > 0)
                                {
                                    Debug.Log($"[AttachToBlendshapeProcessor] Clip '{clip.name}' has {blendshapeBindings.Length} blendshape bindings. Paths: {string.Join(", ", blendshapeBindings.Select(b => b.path))}", data);
                                }
                            }

                            // Check if this clip has a curve for our blendshape
                            bool hasBlendshapeCurve = false;
                            AnimationCurve blendshapeCurve = null;

                            foreach (var binding in bindings)
                            {
                                // Check for blendshape bindings
                                if (binding.type == typeof(SkinnedMeshRenderer) &&
                                    binding.propertyName.StartsWith("blendShape."))
                                {
                                    // Try multiple path matching strategies
                                    bool pathMatches = binding.path == baseMeshPath || 
                                                       binding.path == baseMeshPathNoRoot ||
                                                       binding.path.EndsWith("/" + baseMeshPath) ||
                                                       binding.path.EndsWith("/" + baseMeshPathNoRoot) ||
                                                       baseMeshPath.EndsWith("/" + binding.path) ||
                                                       baseMeshPathNoRoot.EndsWith("/" + binding.path);

                                    if (pathMatches && binding.propertyName == blendshapePropertyName)
                                    {
                                        blendshapeCurve = AnimationUtility.GetEditorCurve(clip, binding);
                                        if (blendshapeCurve != null)
                                        {
                                            hasBlendshapeCurve = true;
                                            if (data.debugMode)
                                            {
                                                Debug.Log($"[AttachToBlendshapeProcessor] ✓ Found blendshape curve '{blendshapeName}' in clip '{clip.name}' on path '{binding.path}' (expected: '{baseMeshPath}')", data);
                                            }
                                            break;
                                        }
                                    }
                                    else if (data.debugMode && binding.propertyName == blendshapePropertyName)
                                    {
                                        Debug.Log($"[AttachToBlendshapeProcessor] Path mismatch for '{blendshapeName}': binding path '{binding.path}' vs expected '{baseMeshPath}' or '{baseMeshPathNoRoot}'", data);
                                    }
                                }
                            }

                            if (!hasBlendshapeCurve || blendshapeCurve == null)
                                continue;

                        // This clip controls our blendshape - add transform curves
                        // The transform curves should be driven by the same parameter/time as the blendshape curve
                        // We'll remap the curves to match the blendshape curve's keyframe times

                        // Remap transform curves to match blendshape curve timing
                        AnimationCurve remappedPosX = RemapCurveToBlendshapeTiming(posX, blendshapeCurve, baseLocalPosition.x);
                        AnimationCurve remappedPosY = RemapCurveToBlendshapeTiming(posY, blendshapeCurve, baseLocalPosition.y);
                        AnimationCurve remappedPosZ = RemapCurveToBlendshapeTiming(posZ, blendshapeCurve, baseLocalPosition.z);
                        AnimationCurve remappedRotX = RemapCurveToBlendshapeTiming(rotX, blendshapeCurve, baseLocalRotation.x);
                        AnimationCurve remappedRotY = RemapCurveToBlendshapeTiming(rotY, blendshapeCurve, baseLocalRotation.y);
                        AnimationCurve remappedRotZ = RemapCurveToBlendshapeTiming(rotZ, blendshapeCurve, baseLocalRotation.z);
                        AnimationCurve remappedRotW = RemapCurveToBlendshapeTiming(rotW, blendshapeCurve, baseLocalRotation.w);

                        // Add transform curves to the clip using Unity's AnimationUtility
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalPosition.x"), remappedPosX);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalPosition.y"), remappedPosY);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalPosition.z"), remappedPosZ);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalRotation.x"), remappedRotX);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalRotation.y"), remappedRotY);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalRotation.z"), remappedRotZ);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalRotation.w"), remappedRotW);
                        
                        // Mark clip as dirty so changes are saved
                        EditorUtility.SetDirty(clip);

                        foundAnyClips = true;
                        curvesAdded++;

                        if (data.debugMode)
                        {
                            Debug.Log($"[AttachToBlendshapeProcessor] Added transform curves for '{blendshapeName}' to clip '{clip.name}' in controller '{controller.name}'", data);
                        }
                        }
                    }
                }

                if (!foundAnyClips)
                {
                    if (data.debugMode)
                    {
                        Debug.LogWarning($"[AttachToBlendshapeProcessor] No animation clips found that control blendshape '{blendshapeName}' on '{baseMeshPath}'. " +
                                       $"This might be because:\n" +
                                       $"1. The blendshape is controlled by VRChat's built-in systems (visemes, eye tracking, etc.)\n" +
                                       $"2. The path '{baseMeshPath}' doesn't match the animation clip bindings\n" +
                                       $"3. The blendshape is animated via parameters/expressions rather than direct animation clips\n" +
                                       $"Transform animations will not be created for this blendshape.", data);
                    }
                    else
                    {
                        Debug.LogWarning($"[AttachToBlendshapeProcessor] No animation clips found for blendshape '{blendshapeName}'. Enable debug mode for details.", data);
                    }
                }
            }

            if (curvesAdded > 0)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Created transform animations: {curvesAdded} curve sets added to animation clips", data);
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
            
            // Add direct states
            states.AddRange(stateMachine.states.Select(s => s.state));
            
            // Recursively add states from nested state machines
            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                states.AddRange(GetAllStates(subStateMachine.stateMachine));
            }
            
            return states;
        }

        /// <summary>
        /// Remaps a transform curve to match the timing of a blendshape curve.
        /// The blendshape curve may have different keyframe times than our sampled curve.
        /// We evaluate our curve at the blendshape curve's keyframe times and create a new curve.
        /// </summary>
        private AnimationCurve RemapCurveToBlendshapeTiming(
            AnimationCurve sourceCurve,
            AnimationCurve blendshapeCurve,
            float baseValue)
        {
            AnimationCurve remapped = new AnimationCurve();

            // Get all keyframe times from the blendshape curve
            HashSet<float> keyframeTimes = new HashSet<float>();
            foreach (var key in blendshapeCurve.keys)
            {
                keyframeTimes.Add(key.time);
            }

            // Also include start and end times
            if (blendshapeCurve.length > 0)
            {
                keyframeTimes.Add(0f);
                keyframeTimes.Add(blendshapeCurve.keys[blendshapeCurve.length - 1].time);
            }

            // Evaluate source curve at each keyframe time and add to remapped curve
            foreach (float time in keyframeTimes.OrderBy(t => t))
            {
                // Evaluate blendshape weight at this time
                // Note: blendshape curves in VRChat typically use 0-1 range, but our samples are 0-100
                float blendshapeWeight = blendshapeCurve.Evaluate(time);
                
                // Normalize blendshape weight to 0-100 range (in case it's 0-1)
                // VRChat blendshape curves are typically 0-1, but we'll handle both
                if (blendshapeWeight <= 1f && blendshapeWeight >= 0f)
                {
                    blendshapeWeight *= 100f; // Convert 0-1 to 0-100
                }
                
                // Map blendshape weight (0-100) to our curve's time (0-1)
                float sourceTime = Mathf.Clamp01(blendshapeWeight / 100f);
                
                // Evaluate our transform curve at the mapped time
                // sourceCurve contains deltas, so we add to base value
                float transformDelta = sourceCurve.Evaluate(sourceTime);
                
                // Add to remapped curve (absolute value = base + delta)
                remapped.AddKey(new Keyframe(time, baseValue + transformDelta));
            }

            // Set tangents for smooth interpolation
            if (remapped.length > 0)
            {
                for (int i = 0; i < remapped.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(remapped, i, AnimationUtility.TangentMode.Auto);
                    AnimationUtility.SetKeyRightTangentMode(remapped, i, AnimationUtility.TangentMode.Auto);
                }
            }

            return remapped;
        }
    }
}
