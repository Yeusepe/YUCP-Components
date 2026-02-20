using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    public static class FbxMergeAnimationRemapper
    {
        public struct RemapContext
        {
            public GameObject avatarRoot;
            public string attachmentPath;
            public string baseRendererPath;
            public string attachmentName;
            public Transform attachmentTransform;
            public SkinnedMeshRenderer baseRenderer;
            public Mesh mergedMesh;
            public int attachmentVertexStart;
            public int attachmentVertexCount;
            public int baseMaterialOffset;
            public Dictionary<string, string> attachmentBlendshapeNameMap;
            public List<int> uvDiscardMaterialIndices;
            public string uvDiscardTilePropertyName;
            public bool remapBlendshapeAnimations;
            public bool remapMaterialAnimations;
            public bool remapRendererAndObjectOffToUvDiscard;
            public bool useScaleToZeroFallback;
            public string scaleToZeroBlendshapeName;
            public bool isAncestorPath;
            public bool debugMode;
            public UnityEngine.Object logContext;
        }

        public struct RemapResult
        {
            public int remappedCurveCount;
            public int warningCount;
            public List<string> warnings;
        }

        public static RemapResult RemapAllAnimations(RemapContext context)
        {
            var result = new RemapResult
            {
                remappedCurveCount = 0,
                warningCount = 0,
                warnings = new List<string>()
            };

            if (context.avatarRoot == null)
            {
                result.warnings.Add("Cannot remap animations: avatar root is null.");
                result.warningCount = result.warnings.Count;
                return result;
            }

            var descriptor = context.avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                result.warnings.Add("Cannot remap animations: VRCAvatarDescriptor not found.");
                result.warningCount = result.warnings.Count;
                return result;
            }

            var clips = CollectAllAnimationClips(descriptor);
            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }

                int clipChanges = RemapClip(clip, context, result.warnings);
                if (clipChanges > 0)
                {
                    result.remappedCurveCount += clipChanges;
                    EditorUtility.SetDirty(clip);
                }
            }

            result.warningCount = result.warnings.Count;
            if (context.debugMode)
            {
                Debug.Log(
                    $"[Mesh Merger] Processed {clips.Count} clip(s), remapped {result.remappedCurveCount} curve operation(s), warnings: {result.warningCount}",
                    context.logContext);
            }

            return result;
        }

        private static int RemapClip(AnimationClip clip, RemapContext context, List<string> warnings)
        {
            if (clip == null || string.IsNullOrEmpty(context.attachmentPath) || string.IsNullOrEmpty(context.baseRendererPath))
            {
                return 0;
            }

            int changes = 0;
            var addFloatCurves = new Dictionary<EditorCurveBinding, AnimationCurve>();
            var removeFloatCurves = new HashSet<EditorCurveBinding>();
            var addObjectCurves = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>();
            var removeObjectCurves = new HashSet<EditorCurveBinding>();
            var transformCurves = new List<KeyValuePair<EditorCurveBinding, AnimationCurve>>();

            var uvDiscardCurveByMaterial = new Dictionary<int, AnimationCurve>();
            AnimationCurve scaleToZeroCurve = null;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != context.attachmentPath)
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null)
                {
                    continue;
                }

                if (context.isAncestorPath)
                {
                    if (IsObjectActiveBinding(binding) && context.remapRendererAndObjectOffToUvDiscard)
                    {
                        if (context.useScaleToZeroFallback && !string.IsNullOrEmpty(context.scaleToZeroBlendshapeName))
                        {
                            scaleToZeroCurve = MergeScaleToZeroCurves(scaleToZeroCurve, ConvertOffCurveToScaleToZeroCurve(curve));
                            changes += 1;
                            if (context.debugMode)
                            {
                                Debug.Log($"[Mesh Merger] Clip '{clip.name}': ancestor m_IsActive on '{binding.path}' -> blendshape '{context.scaleToZeroBlendshapeName}'", context.logContext);
                            }
                        }
                        else if (MergeUvDiscardOffCurve(uvDiscardCurveByMaterial, context, curve, warnings, clip.name))
                        {
                            changes += 1;
                        }
                    }
                    continue;
                }

                if (context.remapBlendshapeAnimations &&
                    binding.propertyName != null &&
                    binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                {
                    string sourceName = binding.propertyName.Substring("blendShape.".Length);
                    string targetName = ResolveBlendshapeName(sourceName, context.attachmentBlendshapeNameMap);
                    if (string.IsNullOrEmpty(targetName))
                    {
                        warnings.Add($"Clip '{clip.name}': could not map blendshape '{sourceName}'.");
                        continue;
                    }

                    var newBinding = EditorCurveBinding.FloatCurve(
                        context.baseRendererPath,
                        typeof(SkinnedMeshRenderer),
                        "blendShape." + targetName);

                    addFloatCurves[newBinding] = CloneCurve(curve);
                    removeFloatCurves.Add(binding);
                    changes += 2;
                    continue;
                }

                if (context.remapMaterialAnimations && TryParseMaterialPropertyName(binding.propertyName, out int sourceMaterialIndex, out string materialSuffix))
                {
                    int remappedIndex = context.baseMaterialOffset + sourceMaterialIndex;
                    string remappedProperty = BuildMaterialPropertyName(remappedIndex, materialSuffix);

                    var newBinding = EditorCurveBinding.FloatCurve(
                        context.baseRendererPath,
                        typeof(SkinnedMeshRenderer),
                        remappedProperty);

                    addFloatCurves[newBinding] = CloneCurve(curve);
                    removeFloatCurves.Add(binding);
                    changes += 2;
                    continue;
                }

                if (IsRendererEnabledBinding(binding) && context.remapRendererAndObjectOffToUvDiscard)
                {
                    if (context.useScaleToZeroFallback && !string.IsNullOrEmpty(context.scaleToZeroBlendshapeName))
                    {
                        scaleToZeroCurve = MergeScaleToZeroCurves(scaleToZeroCurve, ConvertOffCurveToScaleToZeroCurve(curve));
                        removeFloatCurves.Add(binding);
                        changes += 1;
                        if (context.debugMode)
                        {
                            Debug.Log($"[Mesh Merger] Clip '{clip.name}': m_Enabled on '{binding.path}' -> blendshape '{context.scaleToZeroBlendshapeName}'", context.logContext);
                        }
                    }
                    else if (MergeUvDiscardOffCurve(uvDiscardCurveByMaterial, context, curve, warnings, clip.name))
                    {
                        removeFloatCurves.Add(binding);
                        changes += 1;
                    }
                    continue;
                }

                if (IsObjectActiveBinding(binding) && context.remapRendererAndObjectOffToUvDiscard)
                {
                    if (context.useScaleToZeroFallback && !string.IsNullOrEmpty(context.scaleToZeroBlendshapeName))
                    {
                        scaleToZeroCurve = MergeScaleToZeroCurves(scaleToZeroCurve, ConvertOffCurveToScaleToZeroCurve(curve));
                        removeFloatCurves.Add(binding);
                        changes += 1;
                        if (context.debugMode)
                        {
                            Debug.Log($"[Mesh Merger] Clip '{clip.name}': m_IsActive on '{binding.path}' -> blendshape '{context.scaleToZeroBlendshapeName}'", context.logContext);
                        }
                    }
                    else if (MergeUvDiscardOffCurve(uvDiscardCurveByMaterial, context, curve, warnings, clip.name))
                    {
                        removeFloatCurves.Add(binding);
                        changes += 1;
                    }
                    continue;
                }

                if (IsTransformBinding(binding))
                {
                    transformCurves.Add(new KeyValuePair<EditorCurveBinding, AnimationCurve>(binding, curve));
                    continue;
                }

                warnings.Add($"Clip '{clip.name}': unsupported float curve '{binding.propertyName}' on attachment path '{binding.path}'.");
            }

            if (transformCurves.Count > 0)
            {
                bool allConstant = transformCurves.All(kvp => IsCurveConstant(kvp.Value));
                if (allConstant)
                {
                    if (TryCreateTransformRetargetBlendshape(
                        clip,
                        context,
                        transformCurves,
                        out string transformBlendshapeName))
                    {
                        foreach (var kvp in transformCurves)
                        {
                            removeFloatCurves.Add(kvp.Key);
                            changes += 1;
                        }

                        var bsBinding = EditorCurveBinding.FloatCurve(
                            context.baseRendererPath,
                            typeof(SkinnedMeshRenderer),
                            "blendShape." + transformBlendshapeName);

                        var bsCurve = BuildAlwaysOnCurveFrom(transformCurves.Select(x => x.Value));
                        addFloatCurves[bsBinding] = bsCurve;
                        changes += 1;
                    }
                    else
                    {
                        warnings.Add($"Clip '{clip.name}': failed to convert constant transform curves to merged blendshape.");
                    }
                }
                else
                {
                    warnings.Add($"Clip '{clip.name}': found non-constant transform curves on attachment path; leaving them unchanged.");
                }
            }

            if (uvDiscardCurveByMaterial.Count > 0)
            {
                foreach (var kvp in uvDiscardCurveByMaterial)
                {
                    int materialIndex = kvp.Key;
                    string propertyPath = FbxMergeToggleUvDiscardMapper.GetMaterialTilePropertyPath(materialIndex, context.uvDiscardTilePropertyName);
                    var binding = EditorCurveBinding.FloatCurve(context.baseRendererPath, typeof(SkinnedMeshRenderer), propertyPath);
                    addFloatCurves[binding] = kvp.Value;
                    changes += 1;
                }
            }

            if (scaleToZeroCurve != null && !string.IsNullOrEmpty(context.scaleToZeroBlendshapeName))
            {
                var bsBinding = EditorCurveBinding.FloatCurve(
                    context.baseRendererPath,
                    typeof(SkinnedMeshRenderer),
                    "blendShape." + context.scaleToZeroBlendshapeName);
                addFloatCurves[bsBinding] = scaleToZeroCurve;
                changes += 1;

                if (context.debugMode)
                {
                    Debug.Log($"[Mesh Merger] Clip '{clip.name}': emitting blendshape curve '{context.scaleToZeroBlendshapeName}' on '{context.baseRendererPath}' ({scaleToZeroCurve.length} keys)", context.logContext);
                }
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.path != context.attachmentPath)
                {
                    continue;
                }

                var objCurve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (objCurve == null)
                {
                    continue;
                }

                if (context.remapMaterialAnimations && TryParseMaterialPropertyName(binding.propertyName, out int sourceMaterialIndex, out string materialSuffix))
                {
                    int remappedIndex = context.baseMaterialOffset + sourceMaterialIndex;
                    string remappedProperty = BuildMaterialPropertyName(remappedIndex, materialSuffix);
                    var newBinding = EditorCurveBinding.PPtrCurve(context.baseRendererPath, typeof(SkinnedMeshRenderer), remappedProperty);

                    addObjectCurves[newBinding] = objCurve;
                    removeObjectCurves.Add(binding);
                    changes += 2;
                }
                else
                {
                    warnings.Add($"Clip '{clip.name}': unsupported object-reference curve '{binding.propertyName}' on attachment path.");
                }
            }

            foreach (var binding in removeFloatCurves)
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
            }
            foreach (var pair in addFloatCurves)
            {
                AnimationUtility.SetEditorCurve(clip, pair.Key, pair.Value);
            }

            foreach (var binding in removeObjectCurves)
            {
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            }
            foreach (var pair in addObjectCurves)
            {
                AnimationUtility.SetObjectReferenceCurve(clip, pair.Key, pair.Value);
            }

            return changes;
        }

        private static bool MergeUvDiscardOffCurve(
            Dictionary<int, AnimationCurve> uvDiscardCurveByMaterial,
            RemapContext context,
            AnimationCurve sourceCurve,
            List<string> warnings,
            string clipName)
        {
            if (string.IsNullOrEmpty(context.uvDiscardTilePropertyName))
            {
                warnings.Add($"Clip '{clipName}': OFF->UV remap requested but UV discard tile property is empty.");
                return false;
            }

            if (context.uvDiscardMaterialIndices == null || context.uvDiscardMaterialIndices.Count == 0)
            {
                warnings.Add($"Clip '{clipName}': OFF->UV remap requested but no UV-discard-compatible material slots were configured.");
                return false;
            }

            AnimationCurve converted = ConvertOffCurveToDiscardCurve(sourceCurve);

            foreach (int materialIndex in context.uvDiscardMaterialIndices)
            {
                if (!uvDiscardCurveByMaterial.TryGetValue(materialIndex, out var existing))
                {
                    uvDiscardCurveByMaterial[materialIndex] = converted;
                }
                else
                {
                    uvDiscardCurveByMaterial[materialIndex] = MergeBinaryCurvesMax(existing, converted);
                }
            }

            return true;
        }

        private static bool TryCreateTransformRetargetBlendshape(
            AnimationClip clip,
            RemapContext context,
            List<KeyValuePair<EditorCurveBinding, AnimationCurve>> transformCurves,
            out string blendshapeName)
        {
            blendshapeName = null;
            if (context.baseRenderer == null || context.mergedMesh == null || context.attachmentTransform == null)
            {
                return false;
            }

            Vector3 restLocalPos = context.attachmentTransform.localPosition;
            Quaternion restLocalRot = context.attachmentTransform.localRotation;
            Vector3 restLocalScale = context.attachmentTransform.localScale;

            Vector3 targetLocalPos = new Vector3(
                EvalTransformCurve(transformCurves, "m_LocalPosition.x", restLocalPos.x),
                EvalTransformCurve(transformCurves, "m_LocalPosition.y", restLocalPos.y),
                EvalTransformCurve(transformCurves, "m_LocalPosition.z", restLocalPos.z));

            Quaternion targetLocalRot = new Quaternion(
                EvalTransformCurve(transformCurves, "m_LocalRotation.x", restLocalRot.x),
                EvalTransformCurve(transformCurves, "m_LocalRotation.y", restLocalRot.y),
                EvalTransformCurve(transformCurves, "m_LocalRotation.z", restLocalRot.z),
                EvalTransformCurve(transformCurves, "m_LocalRotation.w", restLocalRot.w));

            float qLen =
                targetLocalRot.x * targetLocalRot.x +
                targetLocalRot.y * targetLocalRot.y +
                targetLocalRot.z * targetLocalRot.z +
                targetLocalRot.w * targetLocalRot.w;
            if (qLen > 1e-8f)
            {
                targetLocalRot = Quaternion.Normalize(targetLocalRot);
            }
            else
            {
                targetLocalRot = restLocalRot;
            }

            Vector3 targetLocalScale = new Vector3(
                EvalTransformCurve(transformCurves, "m_LocalScale.x", restLocalScale.x),
                EvalTransformCurve(transformCurves, "m_LocalScale.y", restLocalScale.y),
                EvalTransformCurve(transformCurves, "m_LocalScale.z", restLocalScale.z));

            Transform parent = context.attachmentTransform.parent;
            Vector3 restWorldPos = parent != null ? parent.TransformPoint(restLocalPos) : restLocalPos;
            Vector3 targetWorldPos = parent != null ? parent.TransformPoint(targetLocalPos) : targetLocalPos;

            Quaternion restWorldRot = parent != null ? parent.rotation * restLocalRot : restLocalRot;
            Quaternion targetWorldRot = parent != null ? parent.rotation * targetLocalRot : targetLocalRot;

            Vector3 worldDeltaPos = targetWorldPos - restWorldPos;
            Quaternion worldDeltaRot = targetWorldRot * Quaternion.Inverse(restWorldRot);

            Vector3 baseLocalDeltaPos = context.baseRenderer.transform.InverseTransformVector(worldDeltaPos);
            Quaternion baseLocalDeltaRot =
                Quaternion.Inverse(context.baseRenderer.transform.rotation) *
                worldDeltaRot *
                context.baseRenderer.transform.rotation;

            Vector3 scaleRatio = new Vector3(
                SafeRatio(targetLocalScale.x, restLocalScale.x),
                SafeRatio(targetLocalScale.y, restLocalScale.y),
                SafeRatio(targetLocalScale.z, restLocalScale.z));

            Vector3 pivotBaseLocal = context.baseRenderer.transform.InverseTransformPoint(context.attachmentTransform.position);
            Vector3[] verts = context.mergedMesh.vertices;
            if (verts == null || verts.Length == 0)
            {
                return false;
            }

            Vector3[] delta = new Vector3[verts.Length];
            float maxMagnitude = 0f;
            int start = Mathf.Max(0, context.attachmentVertexStart);
            int end = Mathf.Min(verts.Length, start + Mathf.Max(0, context.attachmentVertexCount));

            for (int i = start; i < end; i++)
            {
                var v = verts[i];
                var rel = v - pivotBaseLocal;
                rel = Vector3.Scale(rel, scaleRatio);
                rel = baseLocalDeltaRot * rel;
                var transformed = pivotBaseLocal + rel + baseLocalDeltaPos;
                var d = transformed - v;
                delta[i] = d;
                maxMagnitude = Mathf.Max(maxMagnitude, d.magnitude);
            }

            if (maxMagnitude < 1e-7f)
            {
                return false;
            }

            string rawName = $"__YUCP_FbxMerge_{context.attachmentName}_{clip.name}";
            blendshapeName = MakeSafeBlendshapeName(rawName);
            int suffix = 1;
            while (context.mergedMesh.GetBlendShapeIndex(blendshapeName) >= 0)
            {
                blendshapeName = MakeSafeBlendshapeName(rawName + "_" + suffix);
                suffix++;
            }

            context.mergedMesh.AddBlendShapeFrame(blendshapeName, 100f, delta, null, null);
            return true;
        }

        private static float EvalTransformCurve(
            List<KeyValuePair<EditorCurveBinding, AnimationCurve>> transformCurves,
            string propertyName,
            float fallback)
        {
            for (int i = 0; i < transformCurves.Count; i++)
            {
                var pair = transformCurves[i];
                if (pair.Key.propertyName == propertyName && pair.Value != null)
                {
                    return pair.Value.Evaluate(0f);
                }
            }
            return fallback;
        }

        private static float SafeRatio(float value, float baseValue)
        {
            if (Mathf.Abs(baseValue) < 1e-8f)
            {
                return 1f;
            }
            return value / baseValue;
        }

        private static bool IsTransformBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Transform) || binding.propertyName == null)
            {
                return false;
            }

            return binding.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal) ||
                   binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal) ||
                   binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal);
        }

        private static bool IsRendererEnabledBinding(EditorCurveBinding binding)
        {
            if (binding.propertyName != "m_Enabled")
            {
                return false;
            }

            return binding.type == typeof(Renderer) ||
                   binding.type == typeof(MeshRenderer) ||
                   binding.type == typeof(SkinnedMeshRenderer);
        }

        private static bool IsObjectActiveBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive";
        }

        private static bool IsCurveConstant(AnimationCurve curve, float epsilon = 1e-4f)
        {
            if (curve == null || curve.length <= 1)
            {
                return true;
            }

            float baseValue = curve.keys[0].value;
            for (int i = 1; i < curve.length; i++)
            {
                if (Mathf.Abs(curve.keys[i].value - baseValue) > epsilon)
                {
                    return false;
                }
            }
            return true;
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
            {
                return null;
            }
            return new AnimationCurve(source.keys);
        }

        private static AnimationCurve ConvertOffCurveToDiscardCurve(AnimationCurve source)
        {
            if (source == null || source.length == 0)
            {
                return new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(1f / 60f, 0f));
            }

            var keys = new List<Keyframe>(source.length);
            for (int i = 0; i < source.length; i++)
            {
                var src = source.keys[i];
                float value = src.value <= 0.5f ? 1f : 0f;
                keys.Add(new Keyframe(src.time, value));
            }
            return new AnimationCurve(keys.ToArray());
        }

        private static Keyframe StepKeyframe(float time, float value)
        {
            var key = new Keyframe(time, value);
            key.inTangent = float.PositiveInfinity;
            key.outTangent = float.PositiveInfinity;
            return key;
        }

        private static AnimationCurve ConvertOffCurveToScaleToZeroCurve(AnimationCurve source)
        {
            if (source == null || source.length == 0)
            {
                return new AnimationCurve(StepKeyframe(0f, 0f), StepKeyframe(1f / 60f, 0f));
            }

            var keys = new Keyframe[source.length];
            for (int i = 0; i < source.length; i++)
            {
                var src = source.keys[i];
                keys[i] = StepKeyframe(src.time, src.value <= 0.5f ? 100f : 0f);
            }
            return new AnimationCurve(keys);
        }

        private static AnimationCurve MergeScaleToZeroCurves(AnimationCurve existing, AnimationCurve incoming)
        {
            if (existing == null) return incoming;
            if (incoming == null) return existing;

            var times = new SortedSet<float>();
            foreach (var k in existing.keys) times.Add(k.time);
            foreach (var k in incoming.keys) times.Add(k.time);
            times.Add(0f);

            var merged = new AnimationCurve();
            foreach (float t in times)
            {
                float value = Mathf.Max(existing.Evaluate(t), incoming.Evaluate(t));
                merged.AddKey(StepKeyframe(t, value >= 50f ? 100f : 0f));
            }
            return merged;
        }

        private static AnimationCurve MergeBinaryCurvesMax(AnimationCurve a, AnimationCurve b)
        {
            if (a == null) return b;
            if (b == null) return a;

            var times = new SortedSet<float>();
            foreach (var k in a.keys) times.Add(k.time);
            foreach (var k in b.keys) times.Add(k.time);
            times.Add(0f);

            var merged = new AnimationCurve();
            foreach (float t in times)
            {
                float value = Mathf.Max(a.Evaluate(t), b.Evaluate(t));
                merged.AddKey(new Keyframe(t, value >= 0.5f ? 1f : 0f));
            }
            return merged;
        }

        private static AnimationCurve BuildAlwaysOnCurveFrom(IEnumerable<AnimationCurve> sourceCurves)
        {
            var times = new SortedSet<float> { 0f, 1f / 60f };
            foreach (var c in sourceCurves)
            {
                if (c == null) continue;
                foreach (var k in c.keys) times.Add(k.time);
            }

            var curve = new AnimationCurve();
            foreach (var t in times)
            {
                curve.AddKey(new Keyframe(t, 100f));
            }
            return curve;
        }

        private static bool TryParseMaterialPropertyName(string propertyName, out int sourceMaterialIndex, out string propertySuffix)
        {
            sourceMaterialIndex = 0;
            propertySuffix = null;

            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            if (propertyName.StartsWith("material.", StringComparison.Ordinal))
            {
                sourceMaterialIndex = 0;
                propertySuffix = propertyName.Substring("material.".Length);
                return !string.IsNullOrEmpty(propertySuffix);
            }

            const string prefix = "materials.Array.data[";
            if (propertyName.StartsWith(prefix, StringComparison.Ordinal))
            {
                int close = propertyName.IndexOf(']', prefix.Length);
                if (close <= prefix.Length)
                {
                    return false;
                }

                string indexText = propertyName.Substring(prefix.Length, close - prefix.Length);
                if (!int.TryParse(indexText, out sourceMaterialIndex))
                {
                    return false;
                }

                int dot = propertyName.IndexOf('.', close);
                if (dot < 0 || dot + 1 >= propertyName.Length)
                {
                    return false;
                }

                propertySuffix = propertyName.Substring(dot + 1);
                return !string.IsNullOrEmpty(propertySuffix);
            }

            return false;
        }

        private static string BuildMaterialPropertyName(int materialIndex, string propertySuffix)
        {
            if (materialIndex <= 0)
            {
                return "material." + propertySuffix;
            }
            return $"materials.Array.data[{materialIndex}].{propertySuffix}";
        }

        private static string ResolveBlendshapeName(string sourceName, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(sourceName))
            {
                return null;
            }

            if (map != null && map.TryGetValue(sourceName, out string targetName))
            {
                return targetName;
            }

            return sourceName;
        }

        private static string MakeSafeBlendshapeName(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "__YUCP_FbxMerge";
            }

            char[] chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
                {
                    continue;
                }
                chars[i] = '_';
            }
            return new string(chars);
        }

        private static HashSet<AnimationClip> CollectAllAnimationClips(VRCAvatarDescriptor descriptor)
        {
            var clips = new HashSet<AnimationClip>();
            if (descriptor == null)
            {
                return clips;
            }

            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.isDefault)
                {
                    continue;
                }
                if (layer.animatorController is AnimatorController ac)
                {
                    CollectClipsFromController(ac, clips);
                }
            }

            foreach (var layer in descriptor.specialAnimationLayers)
            {
                if (layer.isDefault)
                {
                    continue;
                }
                if (layer.animatorController is AnimatorController ac)
                {
                    CollectClipsFromController(ac, clips);
                }
            }

            return clips;
        }

        private static void CollectClipsFromController(AnimatorController controller, HashSet<AnimationClip> clips)
        {
            if (controller == null || clips == null)
            {
                return;
            }

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null)
                {
                    continue;
                }
                CollectClipsFromStateMachine(layer.stateMachine, clips);
            }
        }

        private static void CollectClipsFromStateMachine(AnimatorStateMachine stateMachine, HashSet<AnimationClip> clips)
        {
            if (stateMachine == null)
            {
                return;
            }

            foreach (var state in stateMachine.states)
            {
                CollectClipsFromMotion(state.state != null ? state.state.motion : null, clips);
            }

            foreach (var child in stateMachine.stateMachines)
            {
                CollectClipsFromStateMachine(child.stateMachine, clips);
            }
        }

        private static void CollectClipsFromMotion(Motion motion, HashSet<AnimationClip> clips)
        {
            if (motion == null || clips == null)
            {
                return;
            }

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
    }
}
