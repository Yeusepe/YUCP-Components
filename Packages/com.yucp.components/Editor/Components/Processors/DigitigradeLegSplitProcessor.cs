using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Inserts the digitigrade leg joints at build time.
    ///
    /// Runs after VRCFury's armature link (which rebinds clothing skins onto the avatar's own
    /// bones), so a single pass over every SkinnedMeshRenderer covers body and clothing with the
    /// same weight rule. Running before armature link would leave clothing on its own bones and
    /// silently miss it.
    /// </summary>
    public class DigitigradeLegSplitProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 210;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return true;

            var components = avatarRoot.GetComponentsInChildren<DigitigradeLegSplitData>(true);
            if (components.Length == 0) return true;

            var ok = true;

            foreach (var component in components)
            {
                if (component == null) continue;

                component.ResolveBones();

                var positions = component.GetSortedPositions();
                if (positions.Length == 0)
                {
                    Debug.LogWarning("[YUCP Digitigrade Legs] No splits configured; skipping.", component);
                    continue;
                }

                var summaries = new List<string>();

                foreach (var side in Sides(component))
                {
                    var plan = new LegBoneSplitter.Plan
                    {
                        sourceBone = side.source,
                        endBone = side.end,
                        positions = positions,
                        offsets = component.GetSortedOffsets(side.isRight && component.mirrorRightLeg),
                        angles = component.GetSortedAngles(side.isRight && component.mirrorRightLeg),
                        names = component.GetSortedNames(side.suffix),
                        blendBand = component.blendBand,
                        // Sibling topology: the humanoid chain stays intact, so no Avatar rebuild
                        // is needed and nothing exists for Unity's humanoid machinery to snap back.
                        // Nested mode is unsupported for humanoid rigs -- BuildHumanAvatar bakes
                        // the pre-insert offset in and every Rebind stretches the leg.
                        reparentChain = false
                    };

                    var report = LegBoneSplitter.Apply(avatarRoot, plan);
                    summaries.Add(side.label + ": " + report.Summary);

                    if (!report.success)
                    {
                        Debug.LogError($"[YUCP Digitigrade Legs] {side.label} leg — {report.error}", component);
                        ok = false;
                        continue;
                    }

                    if (report.verticesClipped > 0)
                    {
                        Debug.LogWarning(
                            $"[YUCP Digitigrade Legs] {side.label} leg — {report.verticesClipped} vertices already had " +
                            $"{LegBoneSplitter.MaxInfluences} bone influences and lost their smallest one. " +
                            "Usually harmless; check the hock for pinching.", component);
                    }

                    if (component.verboseLogging)
                    {
                        Debug.Log($"[YUCP Digitigrade Legs] {side.label} leg — {report.Summary}", component);
                    }
                }

                var summary = string.Join(" | ", summaries);
                component.SetBuildSummary(summary);
                EditorUtility.SetDirty(component);

                // Always leave a trace. A silent success is indistinguishable from never having run,
                // which is a miserable thing to debug from the outside.
                Debug.Log("[YUCP Digitigrade Legs] Split applied — " + summary +
                          (avatarRoot.GetComponentInChildren<DigitigradeLegRigData>(true) == null
                              ? ".  NOTE: no Digitigrade Leg Rig component on this avatar, so no IK, constraints or FX layer were built. The split only inserts the bone."
                              : "."), component);
            }

            return ok;
        }

        private struct Side
        {
            public Transform source;
            public Transform end;
            public string suffix;
            public string label;
            public bool isRight;
        }

        private static IEnumerable<Side> Sides(DigitigradeLegSplitData component)
        {
            if (component.leftSourceBone != null && component.leftEndBone != null)
            {
                yield return new Side { source = component.leftSourceBone, end = component.leftEndBone, suffix = ".L", label = "Left", isRight = false };
            }

            if (component.rightSourceBone != null && component.rightEndBone != null)
            {
                yield return new Side { source = component.rightSourceBone, end = component.rightEndBone, suffix = ".R", label = "Right", isRight = true };
            }
        }
    }
}
