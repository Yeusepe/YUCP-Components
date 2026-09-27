using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{

    public static class FTBlendSyncChecks
    {
        [MenuItem("Tools/YUCP/Tests/FT Blendsync")]
        public static void Run()
        {
            var root = new GameObject("FT Blendsync test");
            var source = new Mesh { name = "Test", vertices = new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0), Vector3.zero } };
            Mesh output = null;
            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                var data = root.AddComponent<FTBlendSyncData>();
                data.outputStandards = FTBlendSyncStandard.UnifiedExpressions | FTBlendSyncStandard.MMD;
                var one = Enumerable.Repeat(Vector3.up, 3).ToArray();
                var two = Enumerable.Repeat(Vector3.up * 2, 3).ToArray();
                var zero = new Vector3[3];
                source.AddBlendShapeFrame("eyeBlinkLeft", 50, one, one, one);
                source.AddBlendShapeFrame("eyeBlinkLeft", 100, two, two, two);
                source.AddBlendShapeFrame("eyeBlinkRight", 100, one, zero, zero);
                source.AddBlendShapeFrame("cheekPuff", 100, one, zero, zero);
                source.AddBlendShapeFrame("vrc.v_aa", 100, two, zero, zero);
                source.AddBlendShapeFrame("mouthSmileLeft", 100, one, zero, zero);
                source.AddBlendShapeFrame("mouthSmileRight", 100, two, zero, zero);
                source.AddBlendShapeFrame("TongueLeft", 100, one, zero, zero);
                int originalCount = source.blendShapeCount;
                output = FTBlendSyncProcessor.Generate(data, root.transform, out var plan);
                Require(source.blendShapeCount == originalCount && renderer.sharedMesh == source, "Generation mutated the source.");
                for (int i = 0; i < originalCount; i++) Require(source.GetBlendShapeName(i) == output.GetBlendShapeName(i), "Original indices changed.");
                Require(output.GetBlendShapeFrameCount(output.GetBlendShapeIndex("EyeClosedLeft")) == 2, "Alias lost animation frames.");
                FTBlendSyncProcessor.Sample(output, output.GetBlendShapeIndex("EyeClosedLeft"), 75, out var dv, out var dn, out var dt);
                Require((dv[0] - Vector3.up * 1.5f).sqrMagnitude < 1e-8f && dv[0] == dn[0] && dn[0] == dt[0], "Alias interpolation/normals/tangents changed.");
                FTBlendSyncProcessor.Sample(output, output.GetBlendShapeIndex("CheekPuffLeft"), 100, out var left, out _, out _);
                FTBlendSyncProcessor.Sample(output, output.GetBlendShapeIndex("CheekPuffRight"), 100, out var right, out _, out _);
                for (int i = 0; i < 3; i++) Require((left[i] + right[i] - one[i]).sqrMagnitude < 1e-8f, "Split does not reconstruct the combined shape.");
                Require(left[0] == one[0] && left[1] == Vector3.zero, "Split sides are reversed.");
                Require(output.GetBlendShapeIndex("あ") >= 0 && output.GetBlendShapeIndex("まばたき") >= 0, "MMD names missing.");
                FTBlendSyncProcessor.Sample(output, output.GetBlendShapeIndex("にやり"), 100, out var smile, out _, out _);
                Require(smile[0] == Vector3.up * 3, "MMD smile did not combine both sides.");
                Require(output.GetBlendShapeIndex("TongueRight") < 0 && plan.missing.Contains("TongueRight"), "Invented a missing tongue movement.");
                Require(!plan.mappings.ContainsKey("TongueLeft"), "Existing authored shape was replaced.");
                data.allowApproximations = false;
                Require(!FTBlendSyncProcessor.Analyze(data).mappings.ContainsKey("CheekPuffLeft"), "Approximation switch ignored.");
                data.customMappings.Add(new FTBlendSyncMapping { target = "custom", sources = { new FTBlendSyncSource { shape = "missing" } } });
                Require(FTBlendSyncProcessor.Analyze(data).errors.Count > 0, "Invalid custom source accepted.");
                data.customMappings.Clear();
                source.AddBlendShapeFrame("tongueOut", 100, two, zero, zero);
                source.AddBlendShapeFrame("jawOpen", 100, two, zero, zero);
                source.AddBlendShapeFrame("mouthClose", 100, one, zero, zero);
                source.AddBlendShapeFrame("eyeLookUpLeft", 100, one, zero, zero);
                data.outputStandards |= FTBlendSyncStandard.SRanipal;
                data.allowApproximations = true;
                UnityEngine.Object.DestroyImmediate(output);
                output = FTBlendSyncProcessor.Generate(data, root.transform, out plan);
                FTBlendSyncProcessor.Sample(output, output.GetBlendShapeIndex("Tongue_LongStep1"), 100, out var step1, out _, out _);
                FTBlendSyncProcessor.Sample(output, output.GetBlendShapeIndex("Tongue_LongStep2"), 100, out var step2, out _, out _);
                Require(step1[0] + step2[0] == two[0], "SRanipal tongue steps doubled tongue-out.");
                Require(output.GetBlendShapeIndex("TongueOutStep1") >= 0 && output.GetBlendShapeIndex("Eye_Left_Up") >= 0, "Alternate template names missing.");
                FTBlendSyncProcessor.Sample(output, output.GetBlendShapeIndex("Mouth_Ape_Shape"), 100, out var ape, out _, out _);
                Require(ape[0] == one[0] + two[0], "SRanipal mouth-close correction incorrect.");
                data.generateTongueDirections = true;
                Require(FTBlendSyncProcessor.Analyze(data).errors.Count > 0, "Missing tongue bone accepted.");
                var tongue = new GameObject("tongue").transform;
                tongue.SetParent(root.transform, false);
                data.tongueRoot = tongue;
                renderer.bones = new[] { tongue };
                source.bindposes = new[] { Matrix4x4.identity };
                source.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight(), new BoneWeight() };
                UnityEngine.Object.DestroyImmediate(output);
                output = FTBlendSyncProcessor.Generate(data, root.transform, out plan);
                FTBlendSyncProcessor.Sample(output, output.GetBlendShapeIndex("TongueRight"), 100, out var bend, out _, out _);
                Require(bend[0].sqrMagnitude > 0 && bend[1] == Vector3.zero, "Tongue generation moved unweighted vertices or produced no movement.");
                CheckLinkedAliases();
                CheckRendererOffset();
                Debug.Log("[FT Blendsync] PASS: source isolation, stable indices, multi-frame aliases, normals/tangents, split conservation, MMD expressions, preservation, missing shapes and invalid settings.");
            }
            finally
            {
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
        private static void CheckLinkedAliases()
        {
            var type = Type.GetType("VF.Model.VRCFury, VRCFury");
            if (type == null) return;
            var avatar = new GameObject("FT linked test");
            var source = new Mesh { vertices = new[] { Vector3.zero } };
            var linked = new Mesh { vertices = new[] { Vector3.zero } };
            SkinnedMeshRenderer body = null, accessory = null;
            try
            {
                source.AddBlendShapeFrame("eyeWideLeft", 100, new[] { Vector3.up }, null, null);
                linked.AddBlendShapeFrame("EyeWideLeft", 100, new[] { Vector3.right }, null, null);
                body = new GameObject("Body").AddComponent<SkinnedMeshRenderer>(); body.transform.SetParent(avatar.transform); body.sharedMesh = source;
                accessory = new GameObject("Whiskers").AddComponent<SkinnedMeshRenderer>(); accessory.transform.SetParent(avatar.transform); accessory.sharedMesh = linked;
                var data = body.gameObject.AddComponent<FTBlendSyncData>(); data.outputStandards = FTBlendSyncStandard.UnifiedExpressions;
                var feature = Activator.CreateInstance(Type.GetType("VF.Model.Feature.BlendShapeLink, VRCFury"));
                type.GetField("content").SetValue(accessory.gameObject.AddComponent(type), feature);
                feature.GetType().GetField("baseObj").SetValue(feature, "Body");
                var list = (System.Collections.IList)feature.GetType().GetField("linkSkins").GetValue(feature);
                var entry = Activator.CreateInstance(list.GetType().GetGenericArguments()[0]);
                entry.GetType().GetField("renderer").SetValue(entry, accessory); list.Add(entry);
                Require(new FTBlendSyncProcessor().OnPreprocessAvatar(avatar), "Linked adapter failed.");
                var catalog = AdvancedVisemeBlendShapeLinkCatalog.Scan(avatar);
                Require(catalog.Targets.Count == 1 && catalog.Targets[0].isExact, "Aliases introduced ambiguous accessory links.");
                Require(catalog.Targets[0].mappings.Count == 2, "Original or new accessory alias lost its link.");
                FTBlendSyncProcessor.Sample(accessory.sharedMesh, accessory.sharedMesh.GetBlendShapeIndex("eyeWideLeft"), 100, out var delta, out _, out _);
                Require(delta[0] == Vector3.right && linked.blendShapeCount == 1, "Accessory alias changed authored geometry/source.");
            }
            finally
            {
                if (body != null && body.sharedMesh != source) UnityEngine.Object.DestroyImmediate(body.sharedMesh);
                if (accessory != null && accessory.sharedMesh != linked) UnityEngine.Object.DestroyImmediate(accessory.sharedMesh);
                UnityEngine.Object.DestroyImmediate(avatar); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(linked);
            }
        }
        private static void CheckRendererOffset()
        {
            var avatar = new GameObject("FT bind-space test");
            var source = new Mesh { vertices = new[] { new Vector3(-0.01f, 0, 0.04f), new Vector3(0.01f, 0, 0.04f) } };
            Mesh baseline = null, shifted = null;
            try
            {
                var tongue = new GameObject("tongue").transform; tongue.SetParent(avatar.transform, false);
                var renderer = new GameObject("Body").AddComponent<SkinnedMeshRenderer>(); renderer.transform.SetParent(avatar.transform, false);
                source.bindposes = new[] { Matrix4x4.identity };
                source.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 2).ToArray();
                source.AddBlendShapeFrame("tongueOut", 100, Enumerable.Repeat(Vector3.forward * 0.04f, 2).ToArray(), null, null);
                source.AddBlendShapeFrame("cheekPuff", 100, Enumerable.Repeat(Vector3.up, 2).ToArray(), null, null);
                renderer.sharedMesh = source; renderer.bones = new[] { tongue };
                var data = renderer.gameObject.AddComponent<FTBlendSyncData>();
                data.faceSpace = avatar.transform; data.tongueRoot = tongue; data.generateTongueDirections = true;
                baseline = FTBlendSyncProcessor.Generate(data, avatar.transform, out _);
                renderer.transform.localPosition = new Vector3(0.26f, 0.03f, -0.02f);
                renderer.transform.localRotation = Quaternion.Euler(-90, 15, 0);
                shifted = FTBlendSyncProcessor.Generate(data, avatar.transform, out _);
                foreach (var name in new[] { "TongueLeft", "TongueRight", "TongueUp", "TongueDown", "CheekPuffLeft", "CheekPuffRight" })
                {
                    FTBlendSyncProcessor.Sample(baseline, baseline.GetBlendShapeIndex(name), 100, out var expected, out _, out _);
                    FTBlendSyncProcessor.Sample(shifted, shifted.GetBlendShapeIndex(name), 100, out var actual, out _, out _);
                    for (int i = 0; i < actual.Length; i++) Require((actual[i] - expected[i]).sqrMagnitude < 1e-10f, name + " depends on renderer offset instead of skin bind coordinates.");
                }
                FTBlendSyncProcessor.Sample(shifted, shifted.GetBlendShapeIndex("TongueLeft"), 100, out var left, out _, out _);
                FTBlendSyncProcessor.Sample(shifted, shifted.GetBlendShapeIndex("TongueRight"), 100, out var right, out _, out _);
                Require(Mathf.Abs(left[0].x + right[1].x) < 1e-6f && Mathf.Abs(left[0].z - right[1].z) < 1e-6f, "Generated tongue directions are not symmetric.");
            }
            finally
            {
                if (baseline != null) UnityEngine.Object.DestroyImmediate(baseline);
                if (shifted != null) UnityEngine.Object.DestroyImmediate(shifted);
                UnityEngine.Object.DestroyImmediate(avatar); UnityEngine.Object.DestroyImmediate(source);
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
