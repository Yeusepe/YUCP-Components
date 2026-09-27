using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace YUCP.Components.Editor
{
    public sealed class FTBlendSyncProcessor : IVRCSDKPreprocessAvatarCallback
    {

        public int callbackOrder => int.MinValue + 180;

        public sealed class Plan
        {
            public readonly Dictionary<string, List<FTBlendSyncSource>> mappings = new Dictionary<string, List<FTBlendSyncSource>>(StringComparer.Ordinal);
            public readonly List<string> approximations = new List<string>();
            public readonly List<string> missing = new List<string>();
            public readonly List<string> errors = new List<string>();
            public readonly List<string> tongueShapes = new List<string>();
        }

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var applied = new List<Tuple<SkinnedMeshRenderer, Mesh, Mesh, float[]>>();
            try
            {
                var dataList = avatarRoot.GetComponentsInChildren<FTBlendSyncData>(true).Where(d => d.enabled).ToArray();
                if (dataList.Length == 0) return true;
                var links = AdvancedVisemeBlendShapeLinkCatalog.Scan(avatarRoot);
                if (!links.IsReliable) throw new InvalidOperationException(links.Error);
                var renderers = new HashSet<SkinnedMeshRenderer>();
                foreach (var data in dataList)
                {
                    var renderer = data.GetRenderer();
                    if (renderer == null || !renderer.transform.IsChildOf(avatarRoot.transform))
                        throw new InvalidOperationException("FT Blendsync must target a renderer within this avatar.");
                    if (!renderers.Add(renderer)) throw new InvalidOperationException("Multiple FT Blendsync components target " + renderer.name);
                    var mesh = Generate(data, avatarRoot.transform, out var plan);
                    var original = renderer.sharedMesh;
                    var weights = Enumerable.Range(0, original.blendShapeCount).Select(renderer.GetBlendShapeWeight).ToArray();
                    applied.Add(Tuple.Create(renderer, original, mesh, weights));
                    renderer.sharedMesh = mesh;
                    for (int i = 0; i < weights.Length; i++) renderer.SetBlendShapeWeight(i, weights[i]);
                    for (int i = weights.Length; i < mesh.blendShapeCount; i++) renderer.SetBlendShapeWeight(i, 0);
                    AdaptLinkedMeshes(data, avatarRoot.transform, links.FindTargets(renderer), plan, applied);
                    if ((data.outputStandards & FTBlendSyncStandard.MMD) != 0 && data.preserveMmdShapes)
                    {
                        EnsureMmdCompatibility(avatarRoot);
                        if (AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot.transform) != "Body")
                            Debug.LogWarning("[FT Blendsync] MMD worlds usually require the face renderer at avatar-root/Body. Its path was not changed.", data);
                    }
                    Debug.Log($"[FT Blendsync] {renderer.name}: added {mesh.blendShapeCount - original.blendShapeCount} shapes; {plan.missing.Count} unsupported: {string.Join(", ", plan.missing)}", data);
                }
                return true;
            }
            catch (Exception e)
            {
                foreach (var entry in applied.AsEnumerable().Reverse())
                {
                    entry.Item1.sharedMesh = entry.Item2;
                    for (int i = 0; i < entry.Item4.Length; i++) entry.Item1.SetBlendShapeWeight(i, entry.Item4[i]);
                    UnityEngine.Object.DestroyImmediate(entry.Item3);
                }
                Debug.LogError("[FT Blendsync] " + e.Message, avatarRoot);
                return false;
            }
        }

        private static void AdaptLinkedMeshes(FTBlendSyncData data, Transform avatarRoot,
            IReadOnlyList<AdvancedVisemeBlendShapeLinkCatalog.Target> targets, Plan plan,
            List<Tuple<SkinnedMeshRenderer, Mesh, Mesh, float[]>> applied)
        {



            foreach (var group in targets.GroupBy(t => t.linkedRenderer))
            {
                var renderer = group.Key;
                if (renderer == null || renderer == data.GetRenderer()) continue;
                if (!renderer.transform.IsChildOf(avatarRoot))
                    throw new InvalidOperationException("FT Blendsync linked renderer is outside this avatar.");
                var source = renderer.sharedMesh;
                if (source == null) continue;
                var byBase = group.SelectMany(t => t.mappings).GroupBy(m => m.baseBlendShape)
                    .ToDictionary(g => g.Key, g => g.Select(m => m.linkedBlendShape).Distinct().ToArray());
                var mappings = new Dictionary<string, List<FTBlendSyncSource>>(StringComparer.Ordinal);
                foreach (var pair in byBase.Where(p => p.Value.Length == 1))
                    mappings[pair.Key] = new List<FTBlendSyncSource> { new FTBlendSyncSource { shape = pair.Value[0] } };
                foreach (var pair in plan.mappings)
                {
                    if (pair.Value.Any(s => !byBase.TryGetValue(s.shape, out var names) || names.Length != 1)) continue;
                    mappings[pair.Key] = pair.Value.Select(s => new FTBlendSyncSource
                        { shape = byBase[s.shape][0], weight = s.weight, region = s.region }).ToList();
                }
                foreach (var name in mappings.Keys.Where(n => source.GetBlendShapeIndex(n) >= 0).ToArray()) mappings.Remove(name);
                if (mappings.Count == 0) continue;
                var copy = UnityEngine.Object.Instantiate(source);
                copy.name = source.name + " (FT Blendsync links)";
                var weights = Enumerable.Range(0, source.blendShapeCount).Select(renderer.GetBlendShapeWeight).ToArray();
                applied.Add(Tuple.Create(renderer, source, copy, weights));
                var space = data.faceSpace != null ? data.faceSpace : avatarRoot;
                var matrix = space.worldToLocalMatrix * GetBindToWorld(renderer, data.tongueRoot);
                var mask = source.vertices.Select(v => Mathf.Clamp01(0.5f - matrix.MultiplyPoint3x4(v).x *
                    (data.reverseLeftRight ? -1 : 1) / Mathf.Max(0.00001f, data.splitFeather))).ToArray();
                foreach (var mapping in mappings) AddMapping(source, copy, mapping.Key, mapping.Value, mask);
                renderer.sharedMesh = copy;
                for (int i = 0; i < weights.Length; i++) renderer.SetBlendShapeWeight(i, weights[i]);
                for (int i = weights.Length; i < copy.blendShapeCount; i++) renderer.SetBlendShapeWeight(i, 0);
            }
        }

        public static Plan Analyze(FTBlendSyncData data)
        {
            var plan = new Plan();
            var renderer = data.GetRenderer();
            if (renderer == null || renderer.sharedMesh == null) { plan.errors.Add("Assign a skinned mesh renderer."); return plan; }
            var mesh = renderer.sharedMesh;
            var resolver = new Resolver(mesh, data.allowApproximations);
            var explicitTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in data.customMappings ?? new List<FTBlendSyncMapping>())
            {
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.target)) { plan.errors.Add("A custom mapping has no target name."); continue; }
                if (!explicitTargets.Add(mapping.target)) { plan.errors.Add("Duplicate custom target: " + mapping.target); continue; }
                if (mesh.GetBlendShapeIndex(mapping.target) >= 0) continue;
                if (mapping.sources == null || mapping.sources.Count == 0) { plan.errors.Add("No sources for " + mapping.target); continue; }
                if (mapping.sources.Any(s => s == null || string.IsNullOrEmpty(s.shape) || mesh.GetBlendShapeIndex(s.shape) < 0 || float.IsNaN(s.weight) || float.IsInfinity(s.weight)))
                { plan.errors.Add("Missing source or invalid weight for " + mapping.target); continue; }
                plan.mappings.Add(mapping.target, mapping.sources);
            }
            foreach (var target in FTBlendSyncCatalog.Targets(data.outputStandards).GroupBy(p => p.Key).Select(g => g.First()))
            {
                if (mesh.GetBlendShapeIndex(target.Key) >= 0 || explicitTargets.Contains(target.Key)) continue;
                var terms = resolver.Resolve(target.Value, new HashSet<string>());
                if (terms == null) { plan.missing.Add(target.Key); continue; }
                plan.mappings[target.Key] = terms;
                if (terms.Any(s => s.region != FTBlendSyncRegion.Whole) || resolver.approximate.Contains(target.Value)) plan.approximations.Add(target.Key);
            }
            if (data.generateTongueDirections)
            {
                if (data.tongueRoot == null) plan.errors.Add("Assign the tongue root bone before generating tongue directions.");
                else if (renderer.bones == null || !renderer.bones.Any(b => b != null && (b == data.tongueRoot || b.IsChildOf(data.tongueRoot))))
                    plan.errors.Add("The tongue root has no skin bones in the target renderer.");
                else if (resolver.Resolve("TongueOut", new HashSet<string>()) == null) plan.errors.Add("Tongue direction generation requires a tongue-out shape.");
                else foreach (var name in new[] { "TongueLeft", "TongueRight", "TongueUp", "TongueDown", "Tongue_Left", "Tongue_Right", "Tongue_Up", "Tongue_Down" })
                {
                    bool wanted = name.Contains("_") ? (data.outputStandards & FTBlendSyncStandard.SRanipal) != 0 : (data.outputStandards & FTBlendSyncStandard.UnifiedExpressions) != 0;
                    if (wanted && mesh.GetBlendShapeIndex(name) < 0 && !plan.mappings.ContainsKey(name) && !explicitTargets.Contains(name))
                    { plan.tongueShapes.Add(name); plan.missing.Remove(name); plan.approximations.Add(name); }
                }
            }
            return plan;
        }

        public static Mesh Generate(FTBlendSyncData data, Transform avatarRoot, out Plan plan)
        {
            plan = Analyze(data);
            if (plan.errors.Count > 0) throw new InvalidOperationException(string.Join("\n", plan.errors));
            var renderer = data.GetRenderer();
            var source = renderer.sharedMesh;
            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = source.name + " (FT Blendsync)";
            try
            {
                var space = data.faceSpace != null ? data.faceSpace : avatarRoot;
                if (space == null) space = renderer.transform;
                var matrix = space.worldToLocalMatrix * GetBindToWorld(renderer, data.tongueRoot);
                var positions = source.vertices;
                var leftMask = positions.Select(v => Mathf.Clamp01(0.5f - matrix.MultiplyPoint3x4(v).x * (data.reverseLeftRight ? -1 : 1) / Mathf.Max(0.00001f, data.splitFeather))).ToArray();
                foreach (var mapping in plan.mappings) AddMapping(source, mesh, mapping.Key, mapping.Value, leftMask);
                if (plan.tongueShapes.Count > 0) AddTongueShapes(data, space, source, mesh, plan.tongueShapes);
                return mesh;
            }
            catch { UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }

        private static void AddMapping(Mesh source, Mesh output, string target, List<FTBlendSyncSource> terms, float[] leftMask)
        {


            var frames = new SortedSet<float>();
            foreach (var term in terms)
            {
                if (term.weight == 0) continue;
                int index = source.GetBlendShapeIndex(term.shape);
                for (int f = 0; f < source.GetBlendShapeFrameCount(index); f++) frames.Add(source.GetBlendShapeFrameWeight(index, f));
            }
            if (frames.Count == 0) throw new InvalidOperationException("No nonzero sources for " + target);
            foreach (float frame in frames)
            {
                var vertices = new Vector3[source.vertexCount];
                var normals = new Vector3[source.vertexCount];
                var tangents = new Vector3[source.vertexCount];
                foreach (var term in terms)
                {
                    Sample(source, source.GetBlendShapeIndex(term.shape), frame, out var dv, out var dn, out var dt);
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        float mask = term.region == FTBlendSyncRegion.Left ? leftMask[i] : term.region == FTBlendSyncRegion.Right ? 1 - leftMask[i] : 1;
                        float scale = term.weight * mask;
                        vertices[i] += dv[i] * scale; normals[i] += dn[i] * scale; tangents[i] += dt[i] * scale;
                    }
                }
                output.AddBlendShapeFrame(target, frame, vertices, normals, tangents);
            }
        }

        public static void Sample(Mesh mesh, int index, float weight, out Vector3[] vertices, out Vector3[] normals, out Vector3[] tangents)
        {
            int count = mesh.vertexCount;
            vertices = new Vector3[count]; normals = new Vector3[count]; tangents = new Vector3[count];
            if (index < 0) return;
            int frames = mesh.GetBlendShapeFrameCount(index);
            if (frames == 0) return;
            var knots = new List<Tuple<float, int>> { Tuple.Create(0f, -1) };
            for (int f = 0; f < frames; f++)
            {
                float w = mesh.GetBlendShapeFrameWeight(index, f);
                if (w == 0) knots.RemoveAll(k => k.Item1 == 0);
                knots.Add(Tuple.Create(w, f));
            }
            knots.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            int upper = knots.FindIndex(k => k.Item1 >= weight);
            if (upper < 0) upper = knots.Count - 1;
            if (upper == 0 && knots.Count > 1) upper = 1;
            int lower = Mathf.Max(0, upper - 1);
            var lo = knots[lower]; var hi = knots[upper];
            var bv = new Vector3[count]; var bn = new Vector3[count]; var bt = new Vector3[count];
            if (lo.Item2 >= 0) mesh.GetBlendShapeFrameVertices(index, lo.Item2, vertices, normals, tangents);
            if (hi.Item2 >= 0) mesh.GetBlendShapeFrameVertices(index, hi.Item2, bv, bn, bt);
            float blend = hi.Item1 == lo.Item1 ? 0 : (weight - lo.Item1) / (hi.Item1 - lo.Item1);
            for (int i = 0; i < count; i++)
            {
                vertices[i] = Vector3.LerpUnclamped(vertices[i], bv[i], blend);
                normals[i] = Vector3.LerpUnclamped(normals[i], bn[i], blend);
                tangents[i] = Vector3.LerpUnclamped(tangents[i], bt[i], blend);
            }
        }

        private static Matrix4x4 GetBindToWorld(SkinnedMeshRenderer renderer, Transform anchor)
        {


            var bones = renderer.bones;
            var bindposes = renderer.sharedMesh.bindposes;
            int index = Array.FindIndex(bones, b => b != null && (anchor == null || b == anchor || b.IsChildOf(anchor)));
            if (index < 0) index = Array.FindIndex(bones, b => b != null);
            return index >= 0 && index < bindposes.Length
                ? bones[index].localToWorldMatrix * bindposes[index]
                : renderer.transform.localToWorldMatrix;
        }

        private static void AddTongueShapes(FTBlendSyncData data, Transform space, Mesh source, Mesh output, List<string> names)
        {
            var renderer = data.GetRenderer();
            var bones = renderer.bones;
            var tongueBones = new HashSet<int>(Enumerable.Range(0, bones.Length).Where(i => bones[i] != null && (bones[i] == data.tongueRoot || bones[i].IsChildOf(data.tongueRoot))));
            var influence = new float[source.vertexCount];

            using (var counts = source.GetBonesPerVertex())
            using (var weights = source.GetAllBoneWeights())
            {
                int offset = 0;
                for (int i = 0; i < counts.Length; i++)
                    for (int j = 0; j < counts[i]; j++)
                    { var w = weights[offset++]; if (tongueBones.Contains(w.boneIndex)) influence[i] += w.weight; }
            }
            if (!influence.Any(w => w > 0)) throw new InvalidOperationException("The assigned tongue bones have no weighted vertices.");
            var outTerms = new Resolver(source, true).Resolve("TongueOut", new HashSet<string>());
            var extended = source.vertices;
            foreach (var term in outTerms)
            {
                Sample(source, source.GetBlendShapeIndex(term.shape), 100, out var dv, out _, out _);
                for (int i = 0; i < extended.Length; i++) extended[i] += dv[i] * term.weight;
            }
            var worldToBind = GetBindToWorld(renderer, data.tongueRoot).inverse;
            var pivot = worldToBind.MultiplyPoint3x4(data.tongueRoot.position);
            var up = worldToBind.MultiplyVector(space.up).normalized;
            var right = worldToBind.MultiplyVector(space.right).normalized;
            var baseNormals = source.normals; var baseTangents = source.tangents;
            foreach (var name in names)
            {
                bool horizontal = name.EndsWith("Left") || name.EndsWith("Right");
                float sign = name.EndsWith("Left") || name.EndsWith("Up") ? -1 : 1;
                if (horizontal && data.reverseLeftRight) sign *= -1;
                var rotation = Quaternion.AngleAxis(sign * (horizontal ? data.tongueSideAngle : data.tongueUpDownAngle), horizontal ? up : right);
                var dv = new Vector3[source.vertexCount]; var dn = new Vector3[source.vertexCount]; var dt = new Vector3[source.vertexCount];
                for (int i = 0; i < dv.Length; i++)
                {
                    dv[i] = (rotation * (extended[i] - pivot) + pivot - extended[i]) * influence[i];
                    if (baseNormals.Length == dv.Length) dn[i] = (rotation * baseNormals[i] - baseNormals[i]) * influence[i];
                    if (baseTangents.Length == dv.Length) { var t = (Vector3)baseTangents[i]; dt[i] = (rotation * t - t) * influence[i]; }
                }
                output.AddBlendShapeFrame(name, 100, dv, dn, dt);
            }
        }

        private static void EnsureMmdCompatibility(GameObject avatarRoot)
        {
            var componentType = Type.GetType("VF.Model.VRCFury, VRCFury");
            var featureType = Type.GetType("VF.Model.Feature.MmdCompatibility, VRCFury");
            if (componentType == null || featureType == null) throw new InvalidOperationException("MMD shape preservation requires VRCFury. Install it or disable Preserve Mmd Shapes.");
            var content = componentType.GetField("content");
            if (avatarRoot.GetComponentsInChildren(componentType, true).Any(c => featureType.IsInstanceOfType(content.GetValue(c)))) return;
            content.SetValue(avatarRoot.AddComponent(componentType), Activator.CreateInstance(featureType));
        }

        private sealed class Resolver
        {
            private readonly Mesh mesh;
            private readonly bool allowApproximation;
            public readonly HashSet<string> approximate = new HashSet<string>();
            public Resolver(Mesh mesh, bool allowApproximation) { this.mesh = mesh; this.allowApproximation = allowApproximation; }
            private static FTBlendSyncSource Term(string shape, float weight = 1, FTBlendSyncRegion region = FTBlendSyncRegion.Whole) => new FTBlendSyncSource { shape = shape, weight = weight, region = region };

            public List<FTBlendSyncSource> Resolve(string name, HashSet<string> seen)
            {
                if (!seen.Add(name)) return null;
                try
                {
                    var def = FTBlendSyncCatalog.Shapes.FirstOrDefault(s => s.ue == name);
                    foreach (var candidate in def == null ? new[] { name } : def.Names)
                        if (mesh.GetBlendShapeIndex(candidate) >= 0) return new List<FTBlendSyncSource> { Term(candidate) };
                    foreach (var mmd in FTBlendSyncCatalog.Mmd.Where(p => p.Value == name))
                        if (mesh.GetBlendShapeIndex(mmd.Key) >= 0) return new List<FTBlendSyncSource> { Term(mmd.Key) };
                    if (FTBlendSyncCatalog.Combinations.TryGetValue(name, out var members)) return Combine(members, seen);
                    if (name == "TongueOut") return Combine(new[] { "TongueOutStep1", "TongueOutStep2" }, seen);
                    if (name == "MouthClosed") return Combine(new[] { "Mouth_Ape_Shape", "JawOpen" }, seen, new[] { 1f, -1f });
                    if (name == "Mouth_Ape_Shape") return Combine(new[] { "MouthClosed", "JawOpen" }, seen);
                    if (!allowApproximation) return null;
                    approximate.Add(name);
                    if (name == "HappyBrow") return Combine(new[] { "BrowOuterUpLeft", "BrowOuterUpRight" }, seen, new[] { 0.5f, 0.5f });
                    if (name == "HappyEyes") return Combine(new[] { "EyeClosedLeft", "EyeClosedRight", "CheekSquintLeft", "CheekSquintRight" }, seen);
                    if (name == "WideEyes") return Combine(new[] { "EyeWideLeft", "EyeWideRight" }, seen);
                    if (name == "AngryBrow") return Combine(new[] { "BrowDownLeft", "BrowDownRight" }, seen);
                    if (name == "Tongue_LongStep1" || name == "Tongue_LongStep2")
                        return Resolve(name.EndsWith("1") ? "TongueOutStep1" : "TongueOutStep2", seen);
                    if (name == "TongueOutStep1" || name == "TongueOutStep2") return Scaled(Resolve("TongueOut", seen), 0.5f);
                    if (name == "VowelA") return Scaled(Resolve("JawOpen", seen), 0.7f);
                    if (name == "VowelI") return Combine(new[] { "JawOpen", "MouthSmile" }, seen, new[] { 0.25f, 0.6f });
                    if (name == "VowelU") return Combine(new[] { "JawOpen", "LipPucker" }, seen, new[] { 0.2f, 0.8f });
                    if (name == "VowelE") return Combine(new[] { "JawOpen", "MouthSmile" }, seen, new[] { 0.4f, 0.4f });
                    if (name == "VowelO") return Combine(new[] { "JawOpen", "LipFunnel" }, seen, new[] { 0.5f, 0.7f });
                    if (name == "JawOpen") return Resolve("VowelA", seen);
                    if (name == "MouthUpperLeft" || name == "MouthLowerLeft") return Scaled(Resolve("MouthLeft", seen), 0.5f);
                    if (name == "MouthUpperRight" || name == "MouthLowerRight") return Scaled(Resolve("MouthRight", seen), 0.5f);
                    if (name == "LipFunnelUpper" || name == "LipFunnelLower")
                    {
                        bool upper = name.EndsWith("Upper");
                        var result = Combine(new[] { upper ? "Mouth_Upper_Overturn" : "Mouth_Lower_Overturn", upper ? "MouthUpperUp" : "MouthLowerDown" }, seen);
                        return result ?? Scaled(Resolve("LipFunnel", seen), 0.5f);
                    }
                    if (name == "Mouth_Upper_Overturn" || name == "Mouth_Lower_Overturn")
                        return Combine(new[] { name.Contains("Upper") ? "LipFunnelUpper" : "LipFunnelLower", name.Contains("Upper") ? "MouthUpperUp" : "MouthLowerDown" }, seen, new[] { 1f, -1f });
                    if (name == "Eye_Left_squeeze" || name == "Eye_Right_squeeze")
                    {
                        var side = name.Contains("Left") ? "Left" : "Right";
                        return Combine(new[] { "EyeSquint" + side, "CheekSquint" + side, "BrowDown" + side }, seen);
                    }
                    if (name.StartsWith("MouthSad")) return Combine(new[] { name.Replace("Sad", "Frown"), name.Replace("Sad", "Stretch") }, seen);
                    if (name.EndsWith("Left") || name.EndsWith("Right"))
                    {
                        bool left = name.EndsWith("Left");
                        string combined = name.Substring(0, name.Length - (left ? 4 : 5));
                        var sources = Resolve(combined, seen);
                        if (sources != null) return sources.Select(s => Term(s.shape, s.weight, left ? FTBlendSyncRegion.Left : FTBlendSyncRegion.Right)).ToList();
                    }
                    return null;
                }
                finally { seen.Remove(name); }
            }

            private List<FTBlendSyncSource> Combine(string[] names, HashSet<string> seen, float[] weights = null)
            {
                var terms = new List<FTBlendSyncSource>();
                for (int i = 0; i < names.Length; i++)
                {
                    var found = Resolve(names[i], seen);
                    if (found == null) return null;
                    terms.AddRange(Scaled(found, weights == null ? 1 : weights[i]));
                    if (approximate.Contains(names[i])) foreach (var parent in seen) approximate.Add(parent);
                }
                return terms;
            }
            private static List<FTBlendSyncSource> Scaled(List<FTBlendSyncSource> terms, float weight) => terms?.Select(t => Term(t.shape, t.weight * weight, t.region)).ToList();
        }
    }
}
