using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Builds the digitigrade leg rig and its FX layer.
    ///
    /// Runs after DigitigradeLegSplitProcessor, so a three-segment rig that had a joint inserted is
    /// already four-segment and its humanoid Avatar already rebuilt by the time this reads the bones.
    /// </summary>
    public class DigitigradeLegRigProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 215;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return true;

            var components = avatarRoot.GetComponentsInChildren<DigitigradeLegRigData>(true);
            if (components.Length == 0) return true;

            var animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                Debug.LogError("[YUCP Digitigrade Rig] The avatar needs a humanoid rig.", components[0]);
                return false;
            }

            foreach (var component in components)
            {
                if (component == null) continue;
                if (!Build(avatarRoot, descriptor, animator, component)) return false;
            }

            return true;
        }

        private static bool Build(GameObject avatarRoot, VRCAvatarDescriptor descriptor, Animator animator, DigitigradeLegRigData data)
        {
            data.ResolveBones();

            // The split stage no longer touches the humanoid at all (the metatarsus is a sibling
            // bone), so the Avatar on the animator is still the pristine import and this is the
            // only rebuild in the whole pipeline.
            var rebuilder = HumanoidRebuilder.Capture(animator);
            if (rebuilder == null)
            {
                Debug.LogError("[YUCP Digitigrade Rig] Could not read the humanoid Avatar.", data);
                return false;
            }

            var rig = DigitigradeRigBuilder.Build(avatarRoot, animator, data);
            if (!rig.success)
            {
                Debug.LogError("[YUCP Digitigrade Rig] " + rig.error, data);
                return false;
            }

            // Move the humanoid onto the hidden plantigrade chain so VRChat's IK keeps seeing an
            // ordinary human leg, and drop the toes so the visible toe bone is free for the rig.
            var unmap = new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes };
            if (!rebuilder.Rebuild(avatarRoot, rig.addedBones, new List<Transform>(), rig.humanRemap, unmap, null, out var rebuildError))
            {
                Debug.LogError("[YUCP Digitigrade Rig] " + rebuildError +
                    " The legs would upload with the humanoid rig pointing at the visible bones, which fights the constraints, so the build has been stopped.", data);
                return false;
            }

            var folder = EnsureGeneratedFolder();
            var controller = DigitigradeControllerBuilder.Build(avatarRoot, rig, data, folder);

            // The menu toggle is dead unless the parameter is registered as an expression parameter.
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.name = "YUCP_DigitigradeLegs_Params";
            parameters.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = DigitigradeControllerBuilder.ParamMain,
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true
                },
                new VRCExpressionParameters.Parameter
                {
                    name = DigitigradeControllerBuilder.ParamAnkleOnly,
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true
                },
                new VRCExpressionParameters.Parameter
                {
                    name = DigitigradeControllerBuilder.ParamAnklesWeight,
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = data.anklesWeight,
                    saved = true,
                    networkSynced = true
                }
            };
            AssetDatabase.CreateAsset(parameters, AssetDatabase.GenerateUniqueAssetPath(folder + "/YUCP_DigitigradeLegs_Params.asset"));

            // The toggle goes through VRCFury's menu merge. This avatar has no root expressions
            // menu asset of its own -- VRCFury composes it -- so editing descriptor.expressionsMenu
            // directly would silently do nothing and the toggle would never appear.
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "YUCP_DigitigradeLegs_Menu";
            // The Rex's three controls. Radials, not toggles: everything downstream is a
            // constraint-weight crossfade, so every intermediate value is a valid pose and the
            // reactivity dial is genuinely analog.
            menu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = string.IsNullOrWhiteSpace(data.menuName) ? "Digitigrade" : data.menuName,
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = DigitigradeControllerBuilder.ParamMain } }
                },
                new VRCExpressionsMenu.Control
                {
                    name = "Ankle Only Mode",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = DigitigradeControllerBuilder.ParamAnkleOnly },
                    value = 1f
                },
                new VRCExpressionsMenu.Control
                {
                    name = "Ankle Weight",
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = DigitigradeControllerBuilder.ParamAnklesWeight } }
                }
            };
            AssetDatabase.CreateAsset(menu, AssetDatabase.GenerateUniqueAssetPath(folder + "/YUCP_DigitigradeLegs_Menu.asset"));

            // One Full Controller carrying controller + params + menu together -- VRCFury validates
            // each component in isolation, so splitting them across components fails its checks.
            VRCFuryHelper.AddFullControllerToVRCFury(descriptor, controller, parameters, menu, data.menuPath);

            var solvers = avatarRoot.GetComponentsInChildren<MonoBehaviour>(true)
                .Count(m => m != null && m.GetType().FullName == "RootMotion.FinalIK.LimbIK");

            var summary = $"rig built ({rig.addedBones.Count} hidden bones, {rig.sides.Count} legs, " +
                          $"{solvers} LimbIK solvers), humanoid remapped, FX layer generated";
            data.SetBuildSummary(summary);

            // Unconditional: without this, a build that quietly did nothing looks exactly like a
            // build that worked.
            Debug.Log("[YUCP Digitigrade Rig] " + summary, data);

            return true;
        }

        private static string EnsureGeneratedFolder()
        {
            const string folder = "Assets/YUCP Generated";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
            return folder;
        }
    }
}
