using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Reverts and repairs scene previews using the record serialised on the avatar rather than
    /// Unity's undo stack, which does not survive Play Mode or a domain reload.
    /// </summary>
    public static class DigitigradeLegSplitPreview
    {
        public static void Revert(DigitigradeLegSplitPreviewState state)
        {
            if (state == null) return;

            var root = state.gameObject;

            // Restore skins first, so nothing still references the bones about to be destroyed.
            foreach (var record in state.skins)
            {
                if (record == null || record.skin == null) continue;
                record.skin.bones = record.originalBones;
                record.skin.sharedMesh = record.originalMesh;
            }

            foreach (var record in state.movedBones)
            {
                if (record == null || record.bone == null) continue;
                record.bone.SetParent(record.originalParent, false);
                record.bone.localPosition = record.localPosition;
                record.bone.localRotation = record.localRotation;
                record.bone.localScale = record.localScale;
            }

            foreach (var bone in state.createdBones)
            {
                if (bone != null) Object.DestroyImmediate(bone);
            }

            if (state.animator != null && state.originalAvatar != null)
            {
                state.animator.avatar = state.originalAvatar;
                state.animator.Rebind();
            }

            Object.DestroyImmediate(state);

            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        }

        /// <summary>True when any skinned mesh references a destroyed bone, which collapses the mesh.</summary>
        public static bool NeedsCleanup(GameObject avatarRoot)
        {
            if (avatarRoot == null) return false;

            foreach (var skin in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin != null && skin.bones.Any(b => b == null)) return true;
            }
            return false;
        }

        /// <summary>
        /// Last-resort repair for an avatar whose preview bones were destroyed without reverting.
        /// Drops the dangling bone references and the weights that pointed at them, renormalises the
        /// affected vertices, and removes any leftover generated bones.
        /// </summary>
        public static int CleanUp(GameObject avatarRoot, DigitigradeLegSplitData data)
        {
            if (avatarRoot == null) return 0;

            var repaired = 0;

            foreach (var skin in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || skin.sharedMesh == null) continue;

                var bones = skin.bones;
                var dangling = new HashSet<int>();
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null) dangling.Add(i);
                }
                if (dangling.Count == 0) continue;

                var mesh = Object.Instantiate(skin.sharedMesh);
                mesh.name = skin.sharedMesh.name + " (repaired)";

                // Weight that pointed at a destroyed joint originally came off the bone that was
                // split, so hand it back there rather than to the renderer root -- otherwise
                // fully-orphaned vertices snap across the body instead of returning to the leg.
                var fallbackIndices = new List<int>();
                if (data != null)
                {
                    foreach (var source in new[] { data.leftSourceBone, data.rightSourceBone })
                    {
                        var index = System.Array.IndexOf(skin.bones, source);
                        if (index >= 0) fallbackIndices.Add(index);
                    }
                }
                if (fallbackIndices.Count == 0)
                {
                    var rootIndex = System.Array.IndexOf(skin.bones, skin.rootBone);
                    if (rootIndex >= 0) fallbackIndices.Add(rootIndex);
                }

                var bindposes = mesh.bindposes;
                var fallbackOrigins = fallbackIndices
                    .Select(i => bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero))
                    .ToList();
                var vertices = mesh.vertices;

                var bonesPerVertex = mesh.GetBonesPerVertex().ToArray();
                var flatWeights = mesh.GetAllBoneWeights().ToArray();

                var outCounts = new List<byte>(bonesPerVertex.Length);
                var outWeights = new List<BoneWeight1>(flatWeights.Length);
                var scratch = new List<BoneWeight1>(8);

                var cursor = 0;
                for (int v = 0; v < bonesPerVertex.Length; v++)
                {
                    var count = bonesPerVertex[v];
                    scratch.Clear();
                    var total = 0f;
                    for (int k = 0; k < count; k++)
                    {
                        var influence = flatWeights[cursor + k];
                        if (dangling.Contains(influence.boneIndex)) continue;
                        scratch.Add(influence);
                        total += influence.weight;
                    }
                    cursor += count;

                    if (scratch.Count == 0 && fallbackOrigins.Count > 0)
                    {
                        // Every influence was dangling. Send the vertex back to the nearest bone
                        // that was split, which is where its weight came from.
                        var best = 0;
                        var bestDistance = float.MaxValue;
                        for (int f = 0; f < fallbackOrigins.Count; f++)
                        {
                            var distance = (vertices[v] - fallbackOrigins[f]).sqrMagnitude;
                            if (distance < bestDistance) { bestDistance = distance; best = f; }
                        }

                        scratch.Add(new BoneWeight1 { boneIndex = fallbackIndices[best], weight = 1f });
                        total = 1f;
                    }

                    if (total > 1e-5f)
                    {
                        for (int k = 0; k < scratch.Count; k++)
                        {
                            var influence = scratch[k];
                            influence.weight /= total;
                            scratch[k] = influence;
                        }
                    }

                    outCounts.Add((byte)scratch.Count);
                    outWeights.AddRange(scratch);
                }

                var bpv = new Unity.Collections.NativeArray<byte>(outCounts.ToArray(), Unity.Collections.Allocator.Temp);
                var bw = new Unity.Collections.NativeArray<BoneWeight1>(outWeights.ToArray(), Unity.Collections.Allocator.Temp);
                try { mesh.SetBoneWeights(bpv, bw); }
                finally { bpv.Dispose(); bw.Dispose(); }

                // No weight references those slots any more, so repoint them at a live bone rather
                // than reindexing every influence. A null entry anywhere in the array is what Unity
                // treats as "collapse this vertex".
                var placeholder = fallbackIndices.Count > 0 ? bones[fallbackIndices[0]] : skin.rootBone;
                if (placeholder != null)
                {
                    foreach (var index in dangling) bones[index] = placeholder;
                    skin.bones = bones;
                }

                skin.sharedMesh = mesh;
                repaired += dangling.Count;
            }

            // Remove any generated bones still hanging around from an unreverted preview.
            if (data != null)
            {
                var names = new HashSet<string>(data.GetSortedNames(".L").Concat(data.GetSortedNames(".R")));
                foreach (var t in avatarRoot.GetComponentsInChildren<Transform>(true).ToArray())
                {
                    if (t == null || !names.Contains(t.name)) continue;

                    // Re-home children before deleting, so the foot is not destroyed with the joint.
                    foreach (var child in t.Cast<Transform>().ToArray()) child.SetParent(t.parent, true);
                    Object.DestroyImmediate(t.gameObject);
                }
            }

            var state = avatarRoot.GetComponent<DigitigradeLegSplitPreviewState>();
            if (state != null) Object.DestroyImmediate(state);

            EditorUtility.SetDirty(avatarRoot);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(avatarRoot.scene);
            return repaired;
        }
    }
}
