#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Tests
{
    public class LegBoneSplitterTests
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

        /// <summary>
        /// The whole correctness argument rests on segment memberships forming a partition of
        /// unity. If they don't, vertices silently lose weight and the mesh collapses when posed.
        /// Tilted planes can cross, so this sweeps ordered and crossed configurations alike.
        /// </summary>
        [Test]
        public void Memberships_AlwaysSumToOne([Values(0f, 0.02f, 0.06f)] float band)
        {
            var configurations = new[]
            {
                new[] { 0.67f },                  // single plane
                new[] { 0.45f, 0.75f },           // two parallel planes, correctly ordered
                new[] { 0.75f, 0.45f },           // crossed: tilt has flipped their order here
            };

            foreach (var positions in configurations)
            {
                var result = new float[positions.Length + 1];

                for (float u = -0.3f; u <= 1.3f; u += 0.01f)
                {
                    var signed = new float[positions.Length];
                    for (int i = 0; i < positions.Length; i++) signed[i] = u - positions[i];

                    LegBoneSplitter.ComputeMemberships(signed, band, result);

                    var sum = 0f;
                    foreach (var m in result)
                    {
                        Assert.GreaterOrEqual(m, 0f, $"negative membership at u={u}");
                        sum += m;
                    }

                    Assert.AreEqual(1f, sum, 1e-4f, $"memberships do not sum to 1 at u={u} (band {band})");
                }
            }
        }

        [Test]
        public void Memberships_PutKneeSideOnFirstSegmentAndAnkleSideOnLast()
        {
            var result = new float[2];

            LegBoneSplitter.ComputeMemberships(new[] { -0.5f }, 0.05f, result);
            Assert.AreEqual(1f, result[0], 1e-4f, "vertex behind the plane belongs to the source bone");

            LegBoneSplitter.ComputeMemberships(new[] { 0.5f }, 0.05f, result);
            Assert.AreEqual(1f, result[1], 1e-4f, "vertex past the plane belongs to the new joint");
        }

        [Test]
        public void ClampBand_NeverOverlapsNeighbouringJoints()
        {
            var band = LegBoneSplitter.ClampBand(0.25f, new[] { 0.45f, 0.5f });
            Assert.LessOrEqual(band, 0.025f + 1e-5f, "band must not exceed half the smallest gap");
        }

        [Test]
        public void Apply_MovesDistalWeightToNewBone_AndKeepsWeightsNormalised()
        {
            var (avatar, skin, knee) = BuildTestLeg();

            var report = LegBoneSplitter.Apply(avatar, PlanFor(knee, 0.6f, Vector3.zero));

            Assert.IsTrue(report.success, report.error);
            Assert.AreEqual(1, report.bonesCreated);
            Assert.AreEqual(1, report.meshesTouched);

            var newBone = report.createdBones[0];
            Assert.AreEqual(knee, newBone.parent, "new joint should sit under the source bone");
            Assert.IsNull(knee.Find("foot"), "foot should no longer be a direct child of the source bone");
            Assert.IsNotNull(newBone.Find("foot"), "foot should have been reparented under the new joint");
            Assert.AreEqual(0.6f, newBone.position.y, 1e-4f, "new joint sits at the split position");

            var bones = skin.bones;
            var newIndex = System.Array.IndexOf(bones, newBone);
            var kneeIndex = System.Array.IndexOf(bones, knee);
            Assert.GreaterOrEqual(newIndex, 0, "new bone was not added to the skin");
            Assert.AreEqual(bones.Length, skin.sharedMesh.bindposes.Length, "bindpose count must match bone count");

            var vertices = skin.sharedMesh.vertices;
            var bonesPerVertex = skin.sharedMesh.GetBonesPerVertex().ToArray();
            var weights = skin.sharedMesh.GetAllBoneWeights().ToArray();

            var cursor = 0;
            for (int v = 0; v < vertices.Length; v++)
            {
                var count = bonesPerVertex[v];
                var total = 0f;
                var onNew = 0f;
                var onKnee = 0f;

                for (int k = 0; k < count; k++)
                {
                    var influence = weights[cursor + k];
                    total += influence.weight;
                    if (influence.boneIndex == newIndex) onNew += influence.weight;
                    if (influence.boneIndex == kneeIndex) onKnee += influence.weight;
                }
                cursor += count;

                Assert.AreEqual(1f, total, 1e-4f, $"vertex {v} weights not normalised");

                var y = vertices[v].y;
                if (y > 0.7f) Assert.Greater(onNew, 0.99f, $"vertex at y={y} should follow the new joint");
                if (y < 0.5f) Assert.Greater(onKnee, 0.99f, $"vertex at y={y} should stay on the source bone");
            }
        }

        /// <summary>
        /// Tilting the cut plane must change only which vertices follow the new joint, never where
        /// the mesh sits in the bind pose. If the tilt leaked into the rest pose, dialling in the
        /// angle would drag the leg around and the control would be unusable.
        /// </summary>
        [Test]
        public void Apply_WithTilt_LeavesMeshUnchangedAtRest()
        {
            var (_, untiltedSkin, untiltedKnee) = BuildTestLeg();
            var before = new Mesh();
            cleanup.Add(before);
            untiltedSkin.BakeMesh(before);
            var expected = before.vertices;

            var (avatar, skin, knee) = BuildTestLeg();
            var report = LegBoneSplitter.Apply(avatar, PlanFor(knee, 0.6f, new Vector3(25f, 0f, 10f)));
            Assert.IsTrue(report.success, report.error);

            var after = new Mesh();
            cleanup.Add(after);
            skin.BakeMesh(after);
            var actual = after.vertices;

            Assert.AreEqual(expected.Length, actual.Length);
            for (int v = 0; v < expected.Length; v++)
            {
                Assert.Less(Vector3.Distance(expected[v], actual[v]), 1e-3f,
                    $"vertex {v} moved at rest after a tilted split");
            }

            // ...and the tilt really did reach the joint, rather than silently doing nothing.
            var localTilt = Quaternion.Inverse(untiltedKnee.rotation) * report.createdBones[0].rotation;
            Assert.Greater(Quaternion.Angle(Quaternion.identity, localTilt), 1f, "tilt was not applied to the joint");
        }

        /// <summary>
        /// The joint must be free to leave the straight knee-to-ankle line, because that line is a
        /// chord through a curved leg and the real hock sits behind it.
        /// </summary>
        [Test]
        public void Apply_OffsetMovesJointOffTheBoneAxis()
        {
            var (avatar, _, knee) = BuildTestLeg();

            var plan = PlanFor(knee, 0.6f, Vector3.zero);
            plan.offsets = new[] { new Vector3(0f, 0f, -0.08f) };

            var report = LegBoneSplitter.Apply(avatar, plan);
            Assert.IsTrue(report.success, report.error);

            var joint = report.createdBones[0].position;
            Assert.AreEqual(0.6f, joint.y, 1e-4f, "along-bone position should be unaffected by the offset");
            Assert.AreEqual(-0.08f, joint.z, 1e-4f, "joint should have moved off the bone axis");
        }

        private static LegBoneSplitter.Plan PlanFor(Transform knee, float position, Vector3 angle)
        {
            return new LegBoneSplitter.Plan
            {
                sourceBone = knee,
                endBone = knee.GetChild(0),
                positions = new[] { position },
                offsets = new[] { Vector3.zero },
                angles = new[] { angle },
                names = new[] { "Metatarsus" },
                blendBand = 0.05f,
                reparentChain = true
            };
        }

        /// <summary>
        /// Two bones on a vertical chain, knee at the origin and foot one unit up, with a strip of
        /// vertices running between them weighted entirely to the knee.
        /// </summary>
        private (GameObject avatar, SkinnedMeshRenderer skin, Transform knee) BuildTestLeg()
        {
            var avatar = new GameObject("TestAvatar");
            cleanup.Add(avatar);

            var knee = new GameObject("shin").transform;
            knee.SetParent(avatar.transform, false);

            var foot = new GameObject("foot").transform;
            foot.SetParent(knee, false);
            foot.localPosition = new Vector3(0f, 1f, 0f);

            var renderer = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            renderer.transform.SetParent(avatar.transform, false);

            var mesh = new Mesh { name = "TestLeg" };
            cleanup.Add(mesh);

            // A thin strip rather than a line, so BakeMesh has real triangles to work with.
            var vertices = new Vector3[22];
            var triangles = new List<int>();
            for (int i = 0; i < 11; i++)
            {
                vertices[i * 2] = new Vector3(-0.05f, i * 0.1f, 0f);
                vertices[i * 2 + 1] = new Vector3(0.05f, i * 0.1f, 0f);
            }
            for (int i = 0; i < 10; i++)
            {
                int b = i * 2;
                triangles.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();

            var bones = new[] { knee, foot };
            mesh.bindposes = new[]
            {
                knee.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
                foot.worldToLocalMatrix * renderer.transform.localToWorldMatrix
            };

            var boneWeights = new BoneWeight[vertices.Length];
            for (int i = 0; i < boneWeights.Length; i++)
            {
                boneWeights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            }
            mesh.boneWeights = boneWeights;

            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = knee;

            return (avatar, renderer, knee);
        }
    }
}
#endif
