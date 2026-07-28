using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Generates the FX layer that crossfades the leg constraints between plantigrade and
    /// digitigrade, mirroring the Rexouium controller's structure.
    ///
    /// Per leg, one layer holding a nested 1D tree:
    ///   DigiLegsMain          off / on
    ///     ExtraOff{L,R}       per-leg hard override for sitting and stations
    ///       DigiAnklesWeight  static digitigrade  vs  reactive
    ///         FootPlant       airborne  vs  planted
    ///           ToeCurlUp     metatarsus straightens toward the up limit
    ///           ToeCurlDown   metatarsus drops toward the down limit
    ///
    /// Clips are single-keyframe constraint weight sets -- exactly the four states the rig's
    /// rotation constraints expose -- so everything between them is the animator blending weights.
    /// </summary>
    public static class DigitigradeControllerBuilder
    {
        public const string ParamMain = "DigiLegsMain";
        public const string ParamAnklesWeight = "DigiAnklesWeight";
        public const string ParamAnkleOnly = "DigiAnkleOnly";

        public static string ParamExtraOff(string suffix) => "DigiExtraOff" + Clean(suffix);
        public static string ParamToeUp(string suffix) => "DigiToeUp" + Clean(suffix);
        public static string ParamToeDown(string suffix) => "DigiToeDown" + Clean(suffix);
        public static string ParamFootPlant(string suffix) => "DigiFootPlant" + Clean(suffix);

        private static string Clean(string suffix) => suffix.Replace(".", string.Empty);

        /// <summary>Which constraint source is fully weighted, per state.</summary>
        private enum Pose { Plantigrade = 0, Digitigrade = 1, AnkleDown = 2, AnkleUp = 3 }

        public static AnimatorController Build(
            GameObject avatarRoot,
            DigitigradeRigBuilder.Result rig,
            DigitigradeLegRigData data,
            string assetFolder)
        {
            var controller = new AnimatorController { name = "YUCP_DigitigradeLegs" };
            AssetDatabase.CreateAsset(controller, AssetDatabase.GenerateUniqueAssetPath(assetFolder + "/YUCP_DigitigradeLegs.controller"));

            AddFloat(controller, ParamMain, 1f);
            AddFloat(controller, ParamAnklesWeight, data.anklesWeight);
            AddFloat(controller, ParamAnkleOnly, 1f);

            foreach (var side in rig.sides)
            {
                AddFloat(controller, ParamExtraOff(side.suffix), 0f);
                AddFloat(controller, ParamToeUp(side.suffix), 0f);
                AddFloat(controller, ParamToeDown(side.suffix), 0f);
                AddFloat(controller, ParamFootPlant(side.suffix), 0f);
            }

            foreach (var side in rig.sides)
            {
                var tree = BuildSideTree(avatarRoot, controller, side, data);

                var stateMachine = new AnimatorStateMachine
                {
                    name = "Digi" + side.suffix,
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(stateMachine, controller);

                var state = stateMachine.AddState("Digi" + side.suffix);
                state.motion = tree;
                state.writeDefaultValues = true;
                stateMachine.defaultState = state;

                controller.AddLayer(new AnimatorControllerLayer
                {
                    name = "Digi" + side.suffix,
                    defaultWeight = 1f,
                    stateMachine = stateMachine
                });
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static BlendTree BuildSideTree(
            GameObject avatarRoot,
            AnimatorController controller,
            DigitigradeRigBuilder.Side side,
            DigitigradeLegRigData data)
        {
            var off = Clip(avatarRoot, controller, side, data, Pose.Plantigrade, "Digi Off" + side.suffix);
            var on = Clip(avatarRoot, controller, side, data, Pose.Digitigrade, "Digi On" + side.suffix);
            var up = Clip(avatarRoot, controller, side, data, Pose.AnkleUp, "Digi AnkleUp" + side.suffix);
            var down = Clip(avatarRoot, controller, side, data, Pose.AnkleDown, "Digi AnkleDown" + side.suffix);

            // 1:1 port of the Rexouium tree, per its FX teardown:
            //
            //   DigiLegsMain            0 off / 1 on
            //     DigiAnkleOnly         0 = whole-leg reactions / 1 = ankle-only reactions (default)
            //       ExtraOff{L,R}       0.5 = normal, 2.0 = per-leg hard off (stations, sitting)
            //         DigiAnklesWeight  0 = static digitigrade, 1 = fully contact-driven
            //           ...reactive branch differs per mode, below
            var sfx = side.suffix;

            // Mode 0 -- whole-leg reactions: toes pressed down flatten the ENTIRE leg back to
            // plantigrade (Rex "Digi Foot Down").
            var footDownA = Tree(controller, "FootDownA" + sfx, ParamToeDown(sfx), (0f, on), (1f, off));
            var weightA = Tree(controller, "AnklesWeightA" + sfx, ParamAnklesWeight, (0f, on), (1f, footDownA));
            var extraOffA = Tree(controller, "ExtraOffA" + sfx, ParamExtraOff(sfx), (0.5f, weightA), (2f, off));

            // Mode 1 -- ankle-only reactions (Rex default): foot pitch drives just the metatarsus
            // between its limit poses; planted-and-toes-down fades the leg out so the folded hock
            // does not fight the floor.
            var toeDownB = Tree(controller, "ToeDownB" + sfx, ParamToeDown(sfx), (0f, on), (1f, down));
            var toeUpB = Tree(controller, "ToeUpB" + sfx, ParamToeUp(sfx), (0f, toeDownB), (1f, up));
            var planted = Tree(controller, "Planted" + sfx, ParamToeDown(sfx), (0f, on), (1f, off));
            var footPlant = Tree(controller, "FootPlant" + sfx, ParamFootPlant(sfx), (0f, toeUpB), (0.8f, planted));
            var weightB = Tree(controller, "AnklesWeightB" + sfx, ParamAnklesWeight, (0f, on), (1f, footPlant));
            var extraOffB = Tree(controller, "ExtraOffB" + sfx, ParamExtraOff(sfx), (0.5f, weightB), (2f, off));

            var mode = Tree(controller, "Mode" + sfx, ParamAnkleOnly, (0f, extraOffA), (1f, extraOffB));
            return Tree(controller, "Digi Main" + sfx, ParamMain, (0f, off), (1f, mode));
        }

        private static BlendTree Tree(AnimatorController controller, string name, string parameter, params (float threshold, Motion motion)[] children)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            tree.children = children
                .Select(c => new ChildMotion { motion = c.motion, threshold = c.threshold, timeScale = 1f })
                .ToArray();

            return tree;
        }

        /// <summary>
        /// One keyframe per constraint source: the chosen pose at weight 1, everything else at 0.
        /// </summary>
        private static AnimationClip Clip(
            GameObject avatarRoot,
            AnimatorController controller,
            DigitigradeRigBuilder.Side side,
            DigitigradeLegRigData data,
            Pose pose,
            string name)
        {
            var clip = new AnimationClip { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(clip, controller);

            var digi = pose != Pose.Plantigrade;

            // Thigh and shin only have plantigrade/digitigrade; the limit poses are metatarsus-only.
            SetWeights(clip, avatarRoot, side.thighConstraint, digi ? 1 : 0, 2);
            SetWeights(clip, avatarRoot, side.shinConstraint, digi ? 1 : 0, 2);
            SetWeights(clip, avatarRoot, side.metatarsusConstraint, (int)pose, 4);

            // The paw's socket constraint is never blended; only its softness changes. Plantigrade
            // pins the paw fully to the tracked foot (the original avatar's behavior); digitigrade
            // relaxes it to the flatten weight so the paw rides the hock fold.
            if (side.pawFlattenConstraint != null)
            {
                var pawPath = AnimationUtility.CalculateTransformPath(side.pawFlattenConstraint.transform, avatarRoot.transform);
                var pawWeight = pose == Pose.Plantigrade ? 1f : Mathf.Clamp01(data.pawFlattenWeight);
                clip.SetCurve(pawPath, typeof(VRCRotationConstraint), "GlobalWeight",
                    AnimationCurve.Constant(0f, 0f, pawWeight));
            }

            return clip;
        }

        private static void SetWeights(AnimationClip clip, GameObject avatarRoot, VRC.Dynamics.VRCConstraintBase constraint, int activeSource, int sourceCount)
        {
            if (constraint == null) return;

            var path = AnimationUtility.CalculateTransformPath(constraint.transform, avatarRoot.transform);
            for (int i = 0; i < sourceCount; i++)
            {
                var curve = AnimationCurve.Constant(0f, 0f, i == activeSource ? 1f : 0f);
                clip.SetCurve(path, constraint.GetType(), $"Sources.source{i}.Weight", curve);
            }
        }

        private static void AddFloat(AnimatorController controller, string name, float defaultValue)
        {
            if (controller.parameters.Any(p => p.name == name)) return;
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = defaultValue
            });
        }
    }
}
