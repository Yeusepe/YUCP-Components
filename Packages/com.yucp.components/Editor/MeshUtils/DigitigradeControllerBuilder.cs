using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Generates joint-motion controls and per-leg paw/contact trees. Limited mode keeps the
    /// native limits fixed and scales local joint motion; legacy mode mirrors Rexouium's
    /// constraint-weight crossfades. All controls use native avatar components and FX curves.
    /// </summary>
    public static class DigitigradeControllerBuilder
    {
        public const string ParamMain = "DigiLegsMain";
        public const string ParamAnklesWeight = "DigiAnklesWeight";
        public const string ParamAnkleOnly = "DigiAnkleOnly";
        public const string ParamThighMovement = "DigiThighMovement";
        public const string ParamKneeMovement = "DigiKneeMovement";
        public const string ParamHockMovement = "DigiHockMovement";
        public const string ParamPawFollow = "DigiPawFollow";

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
            AddFloat(controller, ParamAnklesWeight, Mathf.Clamp01(data.anklesWeight));
            AddFloat(controller, ParamAnkleOnly, 0f);
            if (data.enforceJointLimits)
                AddFloat(controller, ParamPawFollow, Mathf.Clamp01(data.pawFlattenWeight));

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

                AddLayer(controller, "Digi" + side.suffix, tree);
            }

            if (data.enforceJointLimits)
            {
                BuildMovementLayer(avatarRoot, controller, rig, 0, ParamThighMovement, data.thighMovement);
                BuildMovementLayer(avatarRoot, controller, rig, 1, ParamKneeMovement, data.kneeMovement);
                BuildMovementLayer(avatarRoot, controller, rig, 2, ParamHockMovement, data.hockMovement);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        internal static void AddLayer(AnimatorController controller, string name, Motion motion)
        {
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
            var state = machine.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = true;
            machine.defaultState = state;
            controller.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1f, stateMachine = machine });
        }

        private static void BuildMovementLayer(GameObject root, AnimatorController controller,
            DigitigradeRigBuilder.Result rig, int joint, string parameter, float defaultValue)
        {
            AddFloat(controller, parameter, Mathf.Clamp01(defaultValue));
            var clips = new AnimationClip[2];
            for (int endpoint = 0; endpoint <= 1; endpoint++)
            {
                var clip = new AnimationClip { name = parameter + " " + endpoint, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(clip, controller);
                clips[endpoint] = clip;
                foreach (var side in rig.sides)
                {
                    var constraint = joint == 0 ? side.thighConstraint : joint == 1 ? side.shinConstraint : side.metatarsusConstraint;
                    var path = AnimationUtility.CalculateTransformPath(constraint.transform, root.transform);
                    clip.SetCurve(path, typeof(VRCRotationConstraint), "GlobalWeight", AnimationCurve.Constant(0f, 0f, endpoint));
                }
            }
            var movement = Tree(controller, parameter, parameter, (0f, clips[0]), (1f, clips[1]));
            AddLayer(controller, parameter, Tree(controller, parameter + " Mode", ParamMain,
                (0.4999f, clips[1]), (0.5f, movement)));
        }

        private static BlendTree BuildSideTree(
            GameObject avatarRoot,
            AnimatorController controller,
            DigitigradeRigBuilder.Side side,
            DigitigradeLegRigData data)
        {
            var off = Clip(avatarRoot, controller, side, data, Pose.Plantigrade, "Digi Off" + side.suffix);

            // 1:1 port of the Rexouium tree, per its FX teardown:
            //
            //   DigiLegsMain            0 off / 1 on
            //     DigiAnkleOnly         0 = whole-leg reactions (default) / 1 = ankle-only reactions
            //       ExtraOff{L,R}       0.5 = normal, 2.0 = per-leg hard off (stations, sitting)
            //         DigiAnklesWeight  0 = static digitigrade, 1 = fully contact-driven
            //           ...reactive branch differs per mode, below
            var sfx = side.suffix;

            if (data.enforceJointLimits)
            {
                // Keep solved joints authoritative: tracked rotations and fixed limit poses
                // would bypass the solver's bounds. Contacts may still flatten the paw.
                var free = Clip(avatarRoot, controller, side, data, Pose.Digitigrade, "Paw Free" + sfx, 0f);
                var tracked = Clip(avatarRoot, controller, side, data, Pose.Digitigrade, "Paw Tracked" + sfx, 1f);
                var follow = Tree(controller, "Paw follow" + sfx, ParamPawFollow, (0f, free), (1f, tracked));
                var plantedPaw = Tree(controller, "Planted paw" + sfx, ParamToeDown(sfx), (0f, follow), (1f, tracked));
                var contact = Tree(controller, "Paw contact" + sfx, ParamFootPlant(sfx), (0f, follow), (0.8f, plantedPaw));
                var weight = Tree(controller, "Paw reactivity" + sfx, ParamAnklesWeight, (0f, follow), (1f, contact));
                return Tree(controller, "Digi Main" + sfx, ParamMain, (0.4999f, off), (0.5f, weight));
            }

            var on = Clip(avatarRoot, controller, side, data, Pose.Digitigrade, "Digi On" + sfx);
            var up = Clip(avatarRoot, controller, side, data, Pose.AnkleUp, "Digi AnkleUp" + sfx);
            var down = Clip(avatarRoot, controller, side, data, Pose.AnkleDown, "Digi AnkleDown" + sfx);



            var footDownA = Tree(controller, "FootDownA" + sfx, ParamToeDown(sfx), (0f, on), (1f, off));
            var weightA = Tree(controller, "AnklesWeightA" + sfx, ParamAnklesWeight, (0f, on), (1f, footDownA));
            var extraOffA = Tree(controller, "ExtraOffA" + sfx, ParamExtraOff(sfx), (0.5f, weightA), (2f, off));




            var toeDownB = Tree(controller, "ToeDownB" + sfx, ParamToeDown(sfx), (0f, on), (1f, down));
            var toeUpB = Tree(controller, "ToeUpB" + sfx, ParamToeUp(sfx), (0f, toeDownB), (1f, up));
            var planted = Tree(controller, "Planted" + sfx, ParamToeDown(sfx), (0f, on), (1f, off));
            var footPlant = Tree(controller, "FootPlant" + sfx, ParamFootPlant(sfx), (0f, toeUpB), (0.8f, planted));
            var weightB = Tree(controller, "AnklesWeightB" + sfx, ParamAnklesWeight, (0f, on), (1f, footPlant));
            var extraOffB = Tree(controller, "ExtraOffB" + sfx, ParamExtraOff(sfx), (0.5f, weightB), (2f, off));

            var mode = Tree(controller, "Mode" + sfx, ParamAnkleOnly, (0f, extraOffA), (1f, extraOffB));
            return Tree(controller, "Digi Main" + sfx, ParamMain, (0f, off), (1f, mode));
        }

        internal static BlendTree Tree(AnimatorController controller, string name, string parameter, params (float threshold, Motion motion)[] children)
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
            string name,
            float? pawWeightOverride = null)
        {
            var clip = new AnimationClip { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(clip, controller);

            var digi = pose != Pose.Plantigrade;


            SetWeights(clip, avatarRoot, side.thighConstraint, digi ? 1 : 0, 2);
            SetWeights(clip, avatarRoot, side.shinConstraint, digi ? 1 : 0, 2);
            SetWeights(clip, avatarRoot, side.metatarsusConstraint,
                data.enforceJointLimits && digi ? 1 : (int)pose, 4);




            if (side.pawFlattenConstraint != null)
            {
                var pawPath = AnimationUtility.CalculateTransformPath(side.pawFlattenConstraint.transform, avatarRoot.transform);
                var pawWeight = pose == Pose.Plantigrade || (data.enforceJointLimits && pose == Pose.AnkleDown)
                    ? 1f : pawWeightOverride ?? Mathf.Clamp01(data.pawFlattenWeight);
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

        internal static void AddFloat(AnimatorController controller, string name, float defaultValue)
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
