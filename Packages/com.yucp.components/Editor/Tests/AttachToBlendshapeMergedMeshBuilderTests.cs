#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Resources;

namespace YUCP.Components.Editor.Tests
{
    public class AttachToBlendshapeMergedMeshBuilderTests
    {
        private readonly List<Object> cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in cleanup)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            cleanup.Clear();
        }

        [Test]
        public void MergePreservesBaseDataAndExtendsTrackedViseme()
        {
            var baseGo = Track(new GameObject("Base"));
            var bone = Track(new GameObject("Bone"));
            bone.transform.SetParent(baseGo.transform, false);
            var renderer = baseGo.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { bone.transform };
            renderer.rootBone = bone.transform;
            renderer.sharedMesh = Track(CreateBaseMesh());

            var attachmentGo = Track(new GameObject("Piercing"));
            attachmentGo.transform.position = new Vector3(0f, 0f, 0.05f);
            var attachmentMesh = Track(CreateAttachmentMesh());
            var filter = attachmentGo.AddComponent<MeshFilter>();
            filter.sharedMesh = attachmentMesh;
            var meshRenderer = attachmentGo.AddComponent<MeshRenderer>();
            var data = attachmentGo.AddComponent<AttachToBlendshapeData>();
            data.targetMesh = renderer;
            data.targetMeshToModify = filter;
            data.bakeMethod = AttachToBlendshapeBakeMethod.ClosestSurfaceDisplacement;
            data.visibilityMode = AttachToBlendshapeVisibilityMode.SizeBlendshape;

            var result = AttachToBlendshapeMergedMeshBuilder.Build(
                renderer,
                renderer.sharedMesh,
                new[] { CreateInput(data, attachmentMesh, attachmentGo.transform, meshRenderer) });

            Assert.That(result.success, Is.True, result.error);
            Track(result.mesh);
            Assert.That(result.mesh.vertexCount, Is.EqualTo(6));
            Assert.That(result.mesh.subMeshCount, Is.EqualTo(2));
            Assert.That(result.mesh.indexFormat, Is.EqualTo(IndexFormat.UInt32));

            var uv7 = new List<Vector4>();
            result.mesh.GetUVs(7, uv7);
            Assert.That(uv7.Count, Is.EqualTo(6));
            Assert.That(uv7[0].x, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(uv7[3].x, Is.EqualTo(0.3f).Within(0.0001f));

            int viseme = result.mesh.GetBlendShapeIndex("vrc.v_aa");
            Assert.That(viseme, Is.GreaterThanOrEqualTo(0));
            var dv = new Vector3[6];
            var dn = new Vector3[6];
            var dt = new Vector3[6];
            result.mesh.GetBlendShapeFrameVertices(viseme, 0, dv, dn, dt);
            Assert.That(dv[0].z, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(dn[0].x, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(dv[3].z, Is.GreaterThan(0.09f));

            string visibilityName = result.attachments[0].visibilityBlendshapeName;
            int visibility = result.mesh.GetBlendShapeIndex(visibilityName);
            Assert.That(visibility, Is.GreaterThanOrEqualTo(0));
            result.mesh.GetBlendShapeFrameVertices(visibility, 0, dv, dn, dt);
            Assert.That(dv[0], Is.EqualTo(Vector3.zero));
            Assert.That(dv[3].sqrMagnitude, Is.GreaterThan(0f));
        }

        [Test]
        public void UnmappedVisemeAttachmentFailsWithoutChangingRenderer()
        {
            var baseGo = Track(new GameObject("Base"));
            var bone = Track(new GameObject("Bone"));
            bone.transform.SetParent(baseGo.transform, false);
            var renderer = baseGo.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { bone.transform };
            renderer.rootBone = bone.transform;
            Mesh original = Track(CreateBaseMesh());
            renderer.sharedMesh = original;

            var attachmentGo = Track(new GameObject("FarAway"));
            attachmentGo.transform.position = new Vector3(100f, 100f, 100f);
            Mesh attachment = Track(CreateAttachmentMesh());
            var data = attachmentGo.AddComponent<AttachToBlendshapeData>();
            data.targetMesh = renderer;
            data.unmatchedHandling = AttachToBlendshapeUnmatchedHandling.Skip;

            var result = AttachToBlendshapeMergedMeshBuilder.Build(
                renderer,
                original,
                new[] { CreateInput(data, attachment, attachmentGo.transform, null) });

            Assert.That(result.success, Is.False);
            Assert.That(result.error, Does.Contain("mapped"));
            Assert.That(renderer.sharedMesh, Is.SameAs(original));
        }

        [Test]
        public void MergeInverseSkinsAttachmentIntoBindSpaceWithoutStretching()
        {
            var baseGo = Track(new GameObject("PosedBase"));
            var bone = Track(new GameObject("TranslatedBone"));
            bone.transform.SetParent(baseGo.transform, false);
            bone.transform.localPosition = new Vector3(2f, 0.5f, 0f);
            bone.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);

            var renderer = baseGo.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { bone.transform };
            renderer.rootBone = bone.transform;
            renderer.sharedMesh = Track(CreateBaseMesh());

            var attachmentGo = Track(new GameObject("PosedPiercing"));
            attachmentGo.transform.position = bone.transform.TransformPoint(new Vector3(0f, 0f, 0.05f));
            attachmentGo.transform.rotation = bone.transform.rotation;
            Mesh attachmentMesh = Track(CreateAttachmentMesh());
            var filter = attachmentGo.AddComponent<MeshFilter>();
            filter.sharedMesh = attachmentMesh;
            var meshRenderer = attachmentGo.AddComponent<MeshRenderer>();
            var data = attachmentGo.AddComponent<AttachToBlendshapeData>();
            data.targetMesh = renderer;
            data.targetMeshToModify = filter;
            data.bakeMethod = AttachToBlendshapeBakeMethod.ClosestSurfaceDisplacement;
            data.visibilityMode = AttachToBlendshapeVisibilityMode.SizeBlendshape;

            var result = AttachToBlendshapeMergedMeshBuilder.Build(
                renderer,
                renderer.sharedMesh,
                new[] { CreateInput(data, attachmentMesh, attachmentGo.transform, meshRenderer) });

            Assert.That(result.success, Is.True, result.error);
            Track(result.mesh);
            renderer.sharedMesh = result.mesh;

            Mesh baked = Track(new Mesh());
            renderer.BakeMesh(baked);
            Vector3[] bakedVertices = baked.vertices;
            Vector3[] sourceVertices = attachmentMesh.vertices;
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector3 expected = renderer.transform.InverseTransformPoint(
                    attachmentGo.transform.TransformPoint(sourceVertices[i]));
                Assert.That(Vector3.Distance(bakedVertices[3 + i], expected), Is.LessThan(0.0001f),
                    $"Appended vertex {i} was skinned twice.");
            }

            int visibility = result.mesh.GetBlendShapeIndex(result.attachments[0].visibilityBlendshapeName);
            renderer.SetBlendShapeWeight(visibility, 100f);
            renderer.BakeMesh(baked);
            bakedVertices = baked.vertices;
            Vector3 expectedPivot = renderer.transform.InverseTransformPoint(attachmentGo.transform.position);
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Assert.That(Vector3.Distance(bakedVertices[3 + i], expectedPivot), Is.LessThan(0.0001f),
                    $"Visibility collapse vertex {i} did not reach the attachment pivot.");
            }
        }

        [Test]
        public void GeneratePreviewUsesMergedRendererForVisemeAndRestoresTransactionally()
        {
            var baseGo = Track(new GameObject("PreviewBase"));
            var bone = Track(new GameObject("PreviewBone"));
            bone.transform.SetParent(baseGo.transform, false);
            bone.transform.localPosition = new Vector3(2f, 0.5f, 0f);
            bone.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
            var renderer = baseGo.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { bone.transform };
            renderer.rootBone = bone.transform;
            Mesh originalBase = Track(CreateBaseMesh());
            renderer.sharedMesh = originalBase;
            renderer.SetBlendShapeWeight(0, 65f);

            var attachmentGo = Track(new GameObject("PreviewPiercing"));
            attachmentGo.transform.position = bone.transform.TransformPoint(new Vector3(0f, 0f, 0.05f));
            attachmentGo.transform.rotation = bone.transform.rotation;
            Mesh attachmentMesh = Track(CreateAttachmentMesh());
            var filter = attachmentGo.AddComponent<MeshFilter>();
            filter.sharedMesh = attachmentMesh;
            var sourceRenderer = attachmentGo.AddComponent<MeshRenderer>();
            var data = attachmentGo.AddComponent<AttachToBlendshapeData>();
            data.targetMesh = renderer;
            data.targetMeshToModify = filter;
            data.trackingMode = BlendshapeTrackingMode.Specific;
            data.specificBlendshapes = new List<string> { "vrc.v_aa" };
            data.bakeMethod = AttachToBlendshapeBakeMethod.RigidPivotTransform;
            data.visibilityMode = AttachToBlendshapeVisibilityMode.SizeBlendshape;
            data.searchRadius = 0f;
            data.clusterTriangleCount = 1;

            // Reproduce the orphan left by the old MeshFilter preview across a domain reload.
            Mesh staleMesh = Track(Object.Instantiate(attachmentMesh));
            staleMesh.name = attachmentMesh.name + "_Blendshapes";
            var staleRenderer = Track(attachmentGo.AddComponent<SkinnedMeshRenderer>());
            staleRenderer.sharedMesh = staleMesh;
            Assert.That(sourceRenderer == null, Is.True,
                "The legacy temporary renderer reproduction should replace Unity's MeshRenderer.");

            var inspector = Track(UnityEditor.Editor.CreateEditor(data, typeof(AttachToBlendshapeDataEditor)));
            MethodInfo generate = typeof(AttachToBlendshapeDataEditor).GetMethod(
                "GeneratePreview", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo clear = typeof(AttachToBlendshapeDataEditor).GetMethod(
                "ClearPreview", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(generate, Is.Not.Null);
            Assert.That(clear, Is.Not.Null);

            generate.Invoke(inspector, null);

            Assert.That(data.previewGenerated, Is.True);
            Assert.That(data.previewUsesMergedMesh, Is.True);
            Assert.That(staleRenderer == null, Is.True, "Orphaned legacy preview renderer was not removed.");
            Assert.That(renderer.sharedMesh, Is.SameAs(data.previewWorkingMesh));
            Assert.That(renderer.sharedMesh.vertexCount, Is.EqualTo(6));
            sourceRenderer = attachmentGo.GetComponent<MeshRenderer>();
            Assert.That(sourceRenderer, Is.Not.Null, "Legacy cleanup did not recreate the source MeshRenderer.");
            Assert.That(sourceRenderer.enabled, Is.False);
            Assert.That(data.previewTempSkinnedMesh, Is.Null,
                "Merge preview must not create the legacy unskinned temporary renderer.");

            Mesh generated = renderer.sharedMesh;
            Mesh baked = Track(new Mesh());
            renderer.BakeMesh(baked);
            Vector3[] bakedVertices = baked.vertices;
            Vector3[] sourceVertices = attachmentMesh.vertices;
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector3 expected = renderer.transform.InverseTransformPoint(
                    attachmentGo.transform.TransformPoint(sourceVertices[i]));
                Assert.That(Vector3.Distance(bakedVertices[3 + i], expected), Is.LessThan(0.0001f),
                    $"Preview vertex {i} changed shape or was skinned twice.");
            }

            clear.Invoke(inspector, null);

            Assert.That(data.previewGenerated, Is.False);
            Assert.That(data.previewUsesMergedMesh, Is.False);
            Assert.That(renderer.sharedMesh, Is.SameAs(originalBase));
            Assert.That(sourceRenderer.enabled, Is.True);
            Assert.That(generated == null, Is.True, "Generated preview mesh was not destroyed.");
        }

        [Test]
        public void SurfaceClusterPreservesRotationWithOpposingTriangleWinding()
        {
            Vector3[] vertices =
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            int[] triangles = { 0, 1, 2, 0, 2, 1 };
            var cluster = new SurfaceCluster
            {
                anchors = new List<TriangleAnchor>
                {
                    new TriangleAnchor(0, new Vector3(1f / 3f, 1f / 3f, 1f / 3f), 0.5f),
                    new TriangleAnchor(1, new Vector3(1f / 3f, 1f / 3f, 1f / 3f), 0.5f)
                },
                totalWeight = 1f
            };

            Quaternion rotation = Quaternion.AngleAxis(30f, Vector3.right);
            Vector3[] deformed = System.Array.ConvertAll(vertices, vertex => rotation * vertex);
            SurfaceClusterDetector.EvaluateCluster(cluster, vertices, triangles, out _, out Vector3 normal0, out _);
            SurfaceClusterDetector.EvaluateCluster(cluster, deformed, triangles, out _, out Vector3 normal1, out _);

            Assert.That(normal0.magnitude, Is.GreaterThan(0.99f), "Opposing winding cancelled the base cluster normal.");
            Assert.That(normal1.magnitude, Is.GreaterThan(0.99f), "Opposing winding cancelled the deformed cluster normal.");
            Assert.That(Vector3.Angle(normal0, normal1), Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void SurfaceClusterGrowsConnectedPatchAndClampsAnchorsToTriangles()
        {
            var go = Track(new GameObject("ConnectedPatch"));
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = Track(new Mesh());
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(1.9f, 1.9f, 0.01f), new Vector3(2.1f, 1.9f, 0.01f), new Vector3(2f, 2.1f, 0.01f)
            };
            mesh.SetTriangles(new[]
            {
                0, 1, 2,
                3, 4, 5,
                6, 7, 8
            }, 0);
            renderer.sharedMesh = mesh;

            SurfaceCluster cluster = SurfaceClusterDetector.DetectCluster(
                renderer, new Vector3(2f, 2f, 0.02f), 2, 0f, 0);

            Assert.That(cluster, Is.Not.Null);
            Assert.That(cluster.anchors.Count, Is.EqualTo(2));
            Assert.That(cluster.anchors[0].triIndex, Is.EqualTo(0));
            Assert.That(cluster.anchors[1].triIndex, Is.EqualTo(1),
                "Disconnected nearby surface was allowed to vote in the patch.");
            foreach (TriangleAnchor anchor in cluster.anchors)
            {
                Assert.That(anchor.barycentric.x, Is.InRange(0f, 1f));
                Assert.That(anchor.barycentric.y, Is.InRange(0f, 1f));
                Assert.That(anchor.barycentric.z, Is.InRange(0f, 1f));
                Assert.That(anchor.barycentric.x + anchor.barycentric.y + anchor.barycentric.z,
                    Is.EqualTo(1f).Within(0.0001f));
            }
        }

        [Test]
        public void SurfaceClusterPrefersNearbySurfaceAffectedByTrackedShape()
        {
            var go = Track(new GameObject("DeformationAwareSeed"));
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = Track(new Mesh());
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(-1f, -1f, -0.002f), new Vector3(1f, -1f, -0.002f), new Vector3(0f, 1f, -0.002f)
            };
            mesh.SetTriangles(new[] { 0, 1, 2, 3, 4, 5 }, 0);
            var delta = new[]
            {
                Vector3.zero, Vector3.zero, Vector3.zero,
                new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.05f, 0f)
            };
            mesh.AddBlendShapeFrame("jawOpen", 100f, delta, new Vector3[6], new Vector3[6]);
            renderer.sharedMesh = mesh;

            SurfaceCluster cluster = SurfaceClusterDetector.DetectCluster(
                renderer, new Vector3(0f, 0f, 0.001f), 1, 0f, -1, new[] { "jawOpen" });

            Assert.That(cluster, Is.Not.Null);
            Assert.That(cluster.anchors[0].triIndex, Is.EqualTo(1),
                "The nearer static layer won over the nearby tracked deforming surface.");
        }

        private AttachToBlendshapeMergedMeshBuilder.AttachmentInput CreateInput(
            AttachToBlendshapeData data,
            Mesh mesh,
            Transform transform,
            MeshRenderer renderer)
        {
            return new AttachToBlendshapeMergedMeshBuilder.AttachmentInput
            {
                data = data,
                mesh = mesh,
                transform = transform,
                materials = new Material[1],
                meshRenderer = renderer,
                cluster = new SurfaceCluster
                {
                    anchors = new List<TriangleAnchor> { new TriangleAnchor(0, new Vector3(1f / 3f, 1f / 3f, 1f / 3f), 1f) },
                    totalWeight = 1f
                },
                trackedBlendshapes = new HashSet<string> { "vrc.v_aa" },
                displayName = transform.name,
                animationPath = transform.name
            };
        }

        private Mesh CreateBaseMesh()
        {
            var mesh = new Mesh { name = "BaseMesh", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.tangents = new[]
            {
                new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1)
            };
            mesh.colors = new[] { Color.red, Color.green, Color.blue };
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            for (int channel = 0; channel < 8; channel++)
            {
                float value = channel / 10f;
                mesh.SetUVs(channel, new List<Vector4>
                {
                    new Vector4(value, 0, 0, 0), new Vector4(value, 0, 0, 0), new Vector4(value, 0, 0, 0)
                });
            }
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = new[]
            {
                OneBone(), OneBone(), OneBone()
            };

            var dv = new[] { new Vector3(0, 0, 0.1f), new Vector3(0, 0, 0.1f), new Vector3(0, 0, 0.1f) };
            var dn = new[] { new Vector3(0.2f, 0, 0), new Vector3(0.2f, 0, 0), new Vector3(0.2f, 0, 0) };
            var dt = new[] { new Vector3(0, 0.1f, 0), new Vector3(0, 0.1f, 0), new Vector3(0, 0.1f, 0) };
            mesh.AddBlendShapeFrame("vrc.v_aa", 100f, dv, dn, dt);
            return mesh;
        }

        private Mesh CreateAttachmentMesh()
        {
            var mesh = new Mesh { name = "Attachment" };
            mesh.vertices = new[]
            {
                new Vector3(-0.1f, -0.1f, 0f), new Vector3(0.1f, -0.1f, 0f), new Vector3(0f, 0.1f, 0f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.tangents = new[]
            {
                new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1)
            };
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            for (int channel = 0; channel < 8; channel++)
            {
                mesh.SetUVs(channel, new List<Vector4>
                {
                    new Vector4(0.3f, 0, 0, 0), new Vector4(0.3f, 0, 0, 0), new Vector4(0.3f, 0, 0, 0)
                });
            }
            return mesh;
        }

        private static BoneWeight OneBone()
        {
            return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        }

        private T Track<T>(T obj) where T : Object
        {
            cleanup.Add(obj);
            return obj;
        }
    }
}
#endif
