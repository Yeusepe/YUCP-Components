using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Rebuilds an avatar's humanoid <see cref="Avatar"/> after bones have been inserted into or
    /// moved within the skeleton.
    ///
    /// The imported Avatar binds human bones to specific transforms. Reparenting a mapped bone --
    /// which is exactly what inserting a joint mid-chain does -- silently orphans it: the Avatar
    /// still reports isValid, but GetBoneTransform returns null and that limb stops tracking.
    /// Rebuilding re-resolves the mapping against the new hierarchy.
    ///
    /// Capture must happen BEFORE the hierarchy is modified, while the original bindings are still
    /// readable.
    /// </summary>
    public sealed class HumanoidRebuilder
    {
        private readonly Animator animator;
        private readonly HashSet<Transform> mappedBones;

        // Not readonly: after a successful Rebuild this is replaced with the corrected description,
        // so a second Rebuild on the same instance starts from the fixed data. Reading
        // humanDescription back off a runtime-built Avatar does NOT round-trip -- it silently
        // returns import-era data, which is how chained rebuilds un-did each other's fixes.
        private HumanDescription description;

        private HumanoidRebuilder(Animator animator, HumanDescription description, HashSet<Transform> mappedBones)
        {
            this.animator = animator;
            this.description = description;
            this.mappedBones = mappedBones;
        }

        /// <summary>
        /// Snapshots the humanoid description and which transform each human bone currently binds
        /// to. Returns null when the rig is not humanoid, in which case no rebuild is needed.
        /// </summary>
        public static HumanoidRebuilder Capture(Animator animator)
        {
            if (animator == null || !animator.isHuman || animator.avatar == null) return null;

            var mapped = new HashSet<Transform>();
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                Transform t = null;
                try { t = animator.GetBoneTransform(bone); }
                catch { /* unmapped optional bones throw on some rigs */ }
                if (t != null) mapped.Add(t);
            }

            return new HumanoidRebuilder(animator, animator.avatar.humanDescription, mapped);
        }

        /// <summary>
        /// Rebuilds and assigns the Avatar. <paramref name="addedBones"/> are new transforms in the
        /// skeleton; <paramref name="movedBones"/> are existing bones whose local transform changed
        /// because they were reparented.
        /// </summary>
        public bool Rebuild(
            GameObject root,
            IReadOnlyList<Transform> addedBones,
            IReadOnlyList<Transform> movedBones,
            DigitigradeLegSplitPreviewState recorder,
            out string error)
        {
            return Rebuild(root, addedBones, movedBones, null, null, recorder, out error);
        }

        /// <summary>
        /// As above, but also repoints human bones at different transforms and/or drops mappings
        /// entirely. Used to move the humanoid rig onto a hidden plantigrade chain so VRChat's IK
        /// keeps seeing an ordinary human leg while the visible leg is digitigrade.
        /// </summary>
        public bool Rebuild(
            GameObject root,
            IReadOnlyList<Transform> addedBones,
            IReadOnlyList<Transform> movedBones,
            IReadOnlyDictionary<HumanBodyBones, Transform> remap,
            IReadOnlyCollection<HumanBodyBones> unmap,
            DigitigradeLegSplitPreviewState recorder,
            out string error)
        {
            error = null;

            var updated = description;
            updated.skeleton = BuildSkeleton(addedBones, movedBones);
            updated.human = BuildHumanMapping(remap, unmap);

            var renamed = Disambiguate(root, updated, remap);
            Avatar rebuilt;
            try
            {
                rebuilt = AvatarBuilder.BuildHumanAvatar(root, updated);
            }
            finally
            {
                foreach (var entry in renamed) entry.transform.name = entry.originalName;
            }

            if (rebuilt == null || !rebuilt.isValid || !rebuilt.isHuman)
            {
                error = "Unity rejected the rebuilt humanoid Avatar. See the console for the specific reason.";
                if (rebuilt != null) UnityEngine.Object.DestroyImmediate(rebuilt);
                return false;
            }

            rebuilt.name = (animator.avatar != null ? animator.avatar.name : root.name) + " (leg split)";

            if (recorder != null && recorder.originalAvatar == null)
            {
                recorder.animator = animator;
                recorder.originalAvatar = animator.avatar;
            }

            // Rebind re-derives a HUMAN-mapped bone's rest offset from its human parent, ignoring
            // any inserted non-human joint in between -- Unity simply does not support that
            // topology, and the foot snaps back to its pre-insert offset (the leg stretches). The
            // final avatar unmaps the foot so this cannot happen at runtime, but intermediate
            // rebuilds (split stage, before the rig remaps the humanoid) hit it squarely. Restore
            // the flagged bones afterwards so later stages build on correct geometry.
            var restore = movedBones.Concat(addedBones)
                .Where(b => b != null)
                .Select(b => (bone: b, pos: b.localPosition, rot: b.localRotation, scale: b.localScale))
                .ToList();

            animator.avatar = rebuilt;
            animator.Rebind();

            foreach (var (bone, pos, rot, scale) in restore)
            {
                bone.localPosition = pos;
                bone.localRotation = rot;
                bone.localScale = scale;
            }

            description = updated;
            return true;
        }

        private HumanBone[] BuildHumanMapping(
            IReadOnlyDictionary<HumanBodyBones, Transform> remap,
            IReadOnlyCollection<HumanBodyBones> unmap)
        {
            if ((remap == null || remap.Count == 0) && (unmap == null || unmap.Count == 0)) return description.human;

            var dropped = new HashSet<string>();
            if (unmap != null)
            {
                foreach (var bone in unmap) dropped.Add(HumanTrait.BoneName[(int)bone]);
            }

            var renamed = new Dictionary<string, string>();
            if (remap != null)
            {
                foreach (var pair in remap)
                {
                    if (pair.Value == null) continue;
                    renamed[HumanTrait.BoneName[(int)pair.Key]] = pair.Value.name;
                }
            }

            var result = new List<HumanBone>(description.human.Length);
            foreach (var entry in description.human)
            {
                if (dropped.Contains(entry.humanName)) continue;

                var updated = entry;
                if (renamed.TryGetValue(entry.humanName, out var boneName)) updated.boneName = boneName;
                result.Add(updated);
            }

            return result.ToArray();
        }

        private SkeletonBone[] BuildSkeleton(IReadOnlyList<Transform> addedBones, IReadOnlyList<Transform> movedBones)
        {
            // Start from the imported skeleton rather than walking the hierarchy: the hierarchy also
            // contains meshes and prop objects, and duplicate names make BuildHumanAvatar reject the
            // description outright.
            var skeleton = description.skeleton.ToList();

            // Only the bones the caller flagged are refreshed from the live hierarchy. Refreshing
            // everything from live transforms was tried and is a trap: skeleton entries define the
            // rest pose, so if the scene happens to be saved in any pose other than the T-pose,
            // a blanket refresh bakes that pose into the rig and mangles limbs that were never
            // touched. The import skeleton is the ground truth for everything we did not move.
            var entryByName = new Dictionary<string, SkeletonBone>();
            foreach (var entry in skeleton)
            {
                if (!entryByName.ContainsKey(entry.name)) entryByName[entry.name] = entry;
            }
            foreach (var bone in movedBones.Concat(addedBones))
            {
                if (bone != null) entryByName[bone.name] = Entry(bone);
            }

            // The array must be in hierarchy order, parent before child, the way importers emit it.
            // An inserted joint appended after its own child is silently ignored by
            // BuildHumanAvatar's default-pose computation: the pose is then derived as if the joint
            // did not exist, and Rebind snaps the child back to its pre-insert offset -- measured as
            // the leg stretching to its old length while the description itself reads correctly.
            // Rebuilding the array by walking the live hierarchy depth-first guarantees the order.
            var ordered = new List<SkeletonBone>(entryByName.Count);
            var emitted = new HashSet<string>();

            // skeleton[0] is the root entry and must stay first. Its name is the FBX root's, which
            // routinely differs from the scene object's name -- BuildHumanAvatar accepts that only
            // for the first entry, and rejects the whole description if it turns up anywhere else.
            if (skeleton.Count > 0 && emitted.Add(skeleton[0].name)) ordered.Add(entryByName[skeleton[0].name]);

            void Walk(Transform t)
            {
                if (t == null) return;
                if (entryByName.TryGetValue(t.name, out var entry) && emitted.Add(t.name)) ordered.Add(entry);
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            for (int i = 0; i < animator.transform.childCount; i++) Walk(animator.transform.GetChild(i));

            // Anything left over names a transform that no longer exists in the hierarchy; keep it
            // in original order so unrelated data is never silently dropped.
            foreach (var entry in skeleton)
            {
                if (emitted.Add(entry.name)) ordered.Add(entryByName[entry.name]);
            }

            return ordered.ToArray();
        }

        private static SkeletonBone Entry(Transform t) => new SkeletonBone
        {
            name = t.name,
            position = t.localPosition,
            rotation = t.localRotation,
            scale = t.localScale
        };

        /// <summary>
        /// BuildHumanAvatar rejects the entire description if any object anywhere in the hierarchy
        /// shares a name with a mapped human bone -- VRCFury slider rigs commonly ship an object
        /// called "Hips", for instance. Rename the impostors for the duration of the build only.
        /// </summary>
        private List<(Transform transform, string originalName)> Disambiguate(
            GameObject root,
            HumanDescription updated,
            IReadOnlyDictionary<HumanBodyBones, Transform> remap)
        {
            var humanNames = new HashSet<string>(updated.human.Select(h => h.boneName));

            // Bones the rebuild is aiming at: the ones that were already mapped, plus any new
            // targets. Without the second set a freshly created bone looks like an impostor and
            // gets renamed out from under the build.
            var intended = new HashSet<Transform>(mappedBones);
            if (remap != null)
            {
                foreach (var target in remap.Values)
                {
                    if (target != null) intended.Add(target);
                }
            }

            var renamed = new List<(Transform, string)>();

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || intended.Contains(t) || !humanNames.Contains(t.name)) continue;
                renamed.Add((t, t.name));
                t.name = t.name + "__yucp_disambiguated";
            }

            return renamed;
        }
    }
}
