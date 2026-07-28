using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Inserts one or more joints into an existing bone and redistributes that bone's skin
    /// weights across the resulting segments.
    ///
    /// The split does not move anything at rest -- it only decides which vertices follow which
    /// segment once the leg is posed. Membership is a pure function of which side of each cut
    /// plane a vertex sits on, so applying the identical rule to every SkinnedMeshRenderer bound
    /// to that bone keeps body and clothing consistent at the new joint by construction.
    ///
    /// Each cut plane can be tilted away from square-to-the-bone, because an anatomical joint
    /// crease rarely runs perpendicular to the bone axis. The tilt also becomes the generated
    /// bone's rest orientation, so the joint bends about the same axis the cut follows.
    /// </summary>
    public static class LegBoneSplitter
    {
        /// <summary>VRChat's skinning tops out at four influences per vertex.</summary>
        public const int MaxInfluences = 4;

        private const float MinWeight = 1e-5f;

        public struct Plan
        {
            /// <summary>Bone being split. Keeps the proximal segment.</summary>
            public Transform sourceBone;

            /// <summary>Bone marking the far end of the source segment (typically the foot).</summary>
            public Transform endBone;

            /// <summary>Split positions along the source bone, sorted ascending, each in (0,1).</summary>
            public float[] positions;

            /// <summary>Per-split offset off the knee-to-ankle line, in the source bone's local frame. Same length as <see cref="positions"/>.</summary>
            public Vector3[] offsets;

            /// <summary>Per-split tilt in degrees, in the source bone's local frame. Same length as <see cref="positions"/>.</summary>
            public Vector3[] angles;

            /// <summary>Names for the generated bones, same order and length as <see cref="positions"/>.</summary>
            public string[] names;

            /// <summary>Half-width of the smooth weight transition, as a fraction of bone length.</summary>
            public float blendBand;

            /// <summary>
            /// When true, the end bone is reparented under the last inserted joint (a nested chain).
            /// Leave FALSE for humanoid rigs: Unity's humanoid machinery does not support a mapped
            /// bone under an inserted non-human joint -- BuildHumanAvatar bakes the pre-insert
            /// offset into the Avatar and every Rebind (including VRChat's at runtime) stretches
            /// the leg back out. As a sibling, the hierarchy still matches the import skeleton, the
            /// humanoid is untouched, and skinning is unaffected because weights reference the
            /// bones array, not the hierarchy. The rig drives the foot with a parent constraint.
            /// </summary>
            public bool reparentChain;
        }

        public struct Report
        {
            public bool success;
            public string error;
            public int bonesCreated;
            public int meshesTouched;
            public int verticesClipped;
            public Transform[] createdBones;

            /// <summary>The existing bone that was reparented under the last new joint. Its local
            /// transform changed, so the humanoid Avatar has to be rebuilt against it.</summary>
            public Transform movedBone;

            public string Summary =>
                success
                    ? $"{bonesCreated} bone(s) inserted, {meshesTouched} mesh(es) re-weighted" +
                      (verticesClipped > 0 ? $", {verticesClipped} vertices clipped to {MaxInfluences} influences" : string.Empty)
                    : "Failed: " + error;
        }

        /// <summary>
        /// Applies a split plan to every skinned mesh under <paramref name="avatarRoot"/>.
        ///
        /// Pass a <paramref name="recorder"/> when running against a live scene instance so the
        /// change can be reverted later. Undo is deliberately not used: it is wiped by Play Mode and
        /// by domain reloads, and a preview that cannot be reverted then gets applied twice, leaving
        /// destroyed bones referenced by the skins.
        /// </summary>
        public static Report Apply(GameObject avatarRoot, Plan plan, DigitigradeLegSplitPreviewState recorder = null)
        {
            var report = new Report { createdBones = Array.Empty<Transform>() };

            if (avatarRoot == null) { report.error = "Avatar root is null."; return report; }
            if (plan.sourceBone == null) { report.error = "Source bone is not assigned."; return report; }
            if (plan.endBone == null) { report.error = "End bone is not assigned."; return report; }
            if (plan.positions == null || plan.positions.Length == 0) { report.error = "No split positions."; return report; }
            if (plan.names == null || plan.names.Length != plan.positions.Length) { report.error = "Split name/position count mismatch."; return report; }
            if (plan.angles == null || plan.angles.Length != plan.positions.Length) { report.error = "Split angle/position count mismatch."; return report; }
            if (plan.offsets == null || plan.offsets.Length != plan.positions.Length) { report.error = "Split offset/position count mismatch."; return report; }
            if (!IsDescendantOf(plan.endBone, plan.sourceBone)) { report.error = $"\"{plan.endBone.name}\" is not below \"{plan.sourceBone.name}\"."; return report; }

            // Splitting twice stacks joints and orphans the first set, which collapses the mesh.
            foreach (var name in plan.names)
            {
                if (FindDescendant(plan.sourceBone, name) != null)
                {
                    report.error = $"\"{name}\" already exists under \"{plan.sourceBone.name}\". This leg is already split -- revert the previous preview first.";
                    return report;
                }
            }

            foreach (var skin in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || Array.IndexOf(skin.bones, plan.sourceBone) < 0) continue;
                if (skin.bones.Any(b => b == null))
                {
                    report.error = $"Skinned mesh \"{skin.name}\" has missing bone references. " +
                                   "That usually means an earlier preview was applied and its bones were destroyed. " +
                                   "Use Clean Up Generated Bones before splitting again.";
                    return report;
                }
            }

            var start = plan.sourceBone.position;
            var axis = plan.endBone.position - start;
            var length = axis.magnitude;
            if (length < 1e-5f) { report.error = $"\"{plan.sourceBone.name}\" has zero length."; return report; }
            var dir = axis / length;

            // The direct child of the source bone that leads to the end bone. Only this branch is
            // reparented -- physbones, colliders and other helper children stay on the source bone.
            var chainChild = plan.endBone;
            while (chainChild.parent != plan.sourceBone) chainChild = chainChild.parent;

            var band = ClampBand(plan.blendBand, plan.positions);

            var planePoints = new Vector3[plan.positions.Length];
            var planeNormals = new Vector3[plan.positions.Length];
            for (int i = 0; i < plan.positions.Length; i++)
            {
                planePoints[i] = JointPosition(plan.sourceBone, start, dir, length, plan.positions[i], plan.offsets[i]);
                planeNormals[i] = TiltRotation(plan.sourceBone, plan.angles[i]) * dir;
            }

            var created = CreateBones(plan, planePoints, recorder);

            if (plan.reparentChain)
            {
                recorder?.RecordMove(chainChild);
                chainChild.SetParent(created[created.Length - 1], true);
                report.movedBone = chainChild;
            }

            report.createdBones = created;
            report.bonesCreated = created.Length;

            foreach (var skin in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || skin.sharedMesh == null) continue;
                if (Array.IndexOf(skin.bones, plan.sourceBone) < 0) continue;

                if (RebindSkin(skin, plan.sourceBone, created, planePoints, planeNormals, band, length,
                        recorder, out var clipped))
                {
                    report.meshesTouched++;
                    report.verticesClipped += clipped;
                }
            }

            report.success = true;
            return report;
        }

        /// <summary>
        /// World position of a joint: a point along the knee-to-ankle chord, pushed off that line
        /// by an offset expressed in the source bone's local frame. Rotation only, not the full
        /// transform, so a scaled bone does not distort the offset.
        /// </summary>
        public static Vector3 JointPosition(Transform sourceBone, Vector3 start, Vector3 dir, float length, float position, Vector3 offset)
        {
            return start + dir * (length * position) + sourceBone.rotation * offset;
        }

        /// <summary>
        /// Converts a tilt expressed in the source bone's local frame into a world-space rotation,
        /// so it can be applied to a world-space direction.
        /// </summary>
        public static Quaternion TiltRotation(Transform sourceBone, Vector3 angleDegrees)
        {
            if (angleDegrees == Vector3.zero) return Quaternion.identity;
            return sourceBone.rotation * Quaternion.Euler(angleDegrees) * Quaternion.Inverse(sourceBone.rotation);
        }

        /// <summary>
        /// Keeps transition bands from overlapping each other or running past the bone ends, which
        /// would break the partition of unity and leave vertices under-weighted.
        /// </summary>
        public static float ClampBand(float requested, float[] sortedPositions)
        {
            var smallestGap = Mathf.Min(sortedPositions[0], 1f - sortedPositions[sortedPositions.Length - 1]);
            for (int i = 1; i < sortedPositions.Length; i++)
            {
                smallestGap = Mathf.Min(smallestGap, sortedPositions[i] - sortedPositions[i - 1]);
            }
            return Mathf.Min(Mathf.Max(0f, requested), smallestGap * 0.5f);
        }

        /// <summary>
        /// Splits one unit of weight across the segments, given each cut plane's signed distance
        /// from the vertex (negative = knee side), normalised by bone length.
        ///
        /// Built as the difference of a monotonic smoothstep, so parallel planes telescope to
        /// exactly 1. Tilted planes can cross each other, which would otherwise leave a vertex
        /// short, so the result is normalised and falls back to a hard classification if every
        /// segment came out empty.
        /// </summary>
        public static void ComputeMemberships(float[] signedDistances, float band, float[] result)
        {
            var segments = signedDistances.Length + 1;

            var total = 0f;
            for (int s = 0; s < segments; s++)
            {
                var lo = s == 0 ? 1f : Gate(signedDistances[s - 1], band);
                var hi = s == signedDistances.Length ? 0f : Gate(signedDistances[s], band);
                result[s] = Mathf.Max(0f, lo - hi);
                total += result[s];
            }

            if (total > MinWeight)
            {
                for (int s = 0; s < segments; s++) result[s] /= total;
                return;
            }

            // Crossing planes left this vertex with no home. Put it entirely on the first segment
            // whose far plane it sits behind, so it still follows something sensible.
            var chosen = signedDistances.Length;
            for (int i = 0; i < signedDistances.Length; i++)
            {
                if (signedDistances[i] < 0f) { chosen = i; break; }
            }

            for (int s = 0; s < segments; s++) result[s] = s == chosen ? 1f : 0f;
        }

        /// <summary>Smoothstep rising from 0 to 1 across [-band, +band] around the plane.</summary>
        private static float Gate(float signedDistance, float band)
        {
            if (band <= 1e-5f) return signedDistance >= 0f ? 1f : 0f;
            var x = Mathf.Clamp01((signedDistance + band) / (2f * band));
            return x * x * (3f - 2f * x);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        private static Transform[] CreateBones(Plan plan, Vector3[] planePoints, DigitigradeLegSplitPreviewState recorder)
        {
            var created = new Transform[plan.positions.Length];
            var parent = plan.sourceBone;

            for (int i = 0; i < plan.positions.Length; i++)
            {
                var go = new GameObject(plan.names[i]);
                recorder?.createdBones.Add(go);

                var t = go.transform;
                t.SetParent(parent, false);
                t.localScale = Vector3.one;
                t.position = planePoints[i];
                // Angles are relative to the source bone, not to the previous split, so a chain of
                // joints stays predictable when you drag one of them.
                t.rotation = plan.sourceBone.rotation * Quaternion.Euler(plan.angles[i]);

                created[i] = t;
                parent = t;
            }

            return created;
        }

        private static bool IsDescendantOf(Transform candidate, Transform ancestor)
        {
            for (var t = candidate; t != null; t = t.parent)
            {
                if (t.parent == ancestor) return true;
            }
            return false;
        }

        private static bool RebindSkin(
            SkinnedMeshRenderer skin,
            Transform sourceBone,
            Transform[] created,
            Vector3[] planePoints,
            Vector3[] planeNormals,
            float band,
            float length,
            DigitigradeLegSplitPreviewState recorder,
            out int clipped)
        {
            clipped = 0;

            var bones = skin.bones;
            var sourceIndex = Array.IndexOf(bones, sourceBone);
            if (sourceIndex < 0) return false;

            var original = skin.sharedMesh;
            if (original.bindposes == null || original.bindposes.Length != bones.Length) return false;

            // Clone per renderer: each one ends up with its own bones array, so a shared source
            // mesh cannot be reused across renderers anyway.
            recorder?.RecordSkin(skin);

            var mesh = UnityEngine.Object.Instantiate(original);
            mesh.name = original.name + " (leg split)";

            var bindposes = mesh.bindposes;
            var sourceBindpose = bindposes[sourceIndex];

            // Segment 0 stays on the source bone; segment i>0 moves to created[i-1].
            var segmentBoneIndex = new int[created.Length + 1];
            segmentBoneIndex[0] = sourceIndex;

            var newBones = new List<Transform>(bones);
            var newBindposes = new List<Matrix4x4>(bindposes);

            for (int i = 0; i < created.Length; i++)
            {
                // Rebase the source bone's bindpose onto the new bone so vertices that move across
                // land in exactly the same place at rest. This also absorbs the tilt, which is why
                // changing the angle never shifts the mesh in the bind pose.
                newBindposes.Add(created[i].worldToLocalMatrix * sourceBone.localToWorldMatrix * sourceBindpose);
                newBones.Add(created[i]);
                segmentBoneIndex[i + 1] = newBones.Count - 1;
            }

            var vertices = mesh.vertices;

            // Views into mesh data -- allocated with Allocator.None, so copy out, never Dispose.
            var bonesPerVertex = mesh.GetBonesPerVertex().ToArray();
            var flatWeights = mesh.GetAllBoneWeights().ToArray();
            if (bonesPerVertex.Length != vertices.Length) return false;

            var sourceToWorld = sourceBone.localToWorldMatrix * sourceBindpose;

            var outBonesPerVertex = new List<byte>(bonesPerVertex.Length);
            var outWeights = new List<BoneWeight1>(flatWeights.Length + bonesPerVertex.Length);
            var scratch = new List<BoneWeight1>(MaxInfluences + created.Length);
            var signed = new float[created.Length];
            var memberships = new float[created.Length + 1];

            int cursor = 0;
            for (int v = 0; v < bonesPerVertex.Length; v++)
            {
                int first = cursor;
                int count = bonesPerVertex[v];
                cursor += count;

                bool touched = false;
                for (int k = 0; k < count; k++)
                {
                    if (flatWeights[first + k].boneIndex == sourceIndex) { touched = true; break; }
                }

                if (!touched)
                {
                    outBonesPerVertex.Add((byte)count);
                    for (int k = 0; k < count; k++) outWeights.Add(flatWeights[first + k]);
                    continue;
                }

                var restWorld = sourceToWorld.MultiplyPoint3x4(vertices[v]);
                for (int i = 0; i < signed.Length; i++)
                {
                    signed[i] = Vector3.Dot(restWorld - planePoints[i], planeNormals[i]) / length;
                }
                ComputeMemberships(signed, band, memberships);

                scratch.Clear();
                for (int k = 0; k < count; k++)
                {
                    var influence = flatWeights[first + k];
                    if (influence.boneIndex != sourceIndex)
                    {
                        scratch.Add(influence);
                        continue;
                    }

                    for (int s = 0; s < memberships.Length; s++)
                    {
                        if (memberships[s] <= 0f) continue;

                        var w = influence.weight * memberships[s];
                        if (w < MinWeight) continue;

                        scratch.Add(new BoneWeight1 { boneIndex = segmentBoneIndex[s], weight = w });
                    }
                }

                scratch.Sort((a, b) => b.weight.CompareTo(a.weight));
                if (scratch.Count > MaxInfluences)
                {
                    scratch.RemoveRange(MaxInfluences, scratch.Count - MaxInfluences);
                    clipped++;
                }

                var total = 0f;
                for (int k = 0; k < scratch.Count; k++) total += scratch[k].weight;
                if (total > MinWeight)
                {
                    for (int k = 0; k < scratch.Count; k++)
                    {
                        var influence = scratch[k];
                        influence.weight /= total;
                        scratch[k] = influence;
                    }
                }

                outBonesPerVertex.Add((byte)scratch.Count);
                outWeights.AddRange(scratch);
            }

            // Bindposes first: SetBoneWeights validates bone indices against the bindpose count,
            // and the redistributed weights reference indices that only exist after the extension.
            mesh.bindposes = newBindposes.ToArray();

            var bpv = new NativeArray<byte>(outBonesPerVertex.ToArray(), Allocator.Temp);
            var bw = new NativeArray<BoneWeight1>(outWeights.ToArray(), Allocator.Temp);
            try
            {
                mesh.SetBoneWeights(bpv, bw);
            }
            finally
            {
                bpv.Dispose();
                bw.Dispose();
            }

            skin.bones = newBones.ToArray();
            skin.sharedMesh = mesh;

            return true;
        }
    }
}
