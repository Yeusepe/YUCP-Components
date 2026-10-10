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



            var unmap = new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes };
            if (!rebuilder.Rebuild(avatarRoot, rig.addedBones, new List<Transform>(), rig.humanRemap, unmap, null, out var rebuildError))
            {
                Debug.LogError("[YUCP Digitigrade Rig] " + rebuildError +
                    " The legs would upload with the humanoid rig pointing at the visible bones, which fights the constraints, so the build has been stopped.", data);
                return false;
            }

            var folder = EnsureGeneratedFolder();
            var controller = DigitigradeControllerBuilder.Build(avatarRoot, rig, data, folder);


            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.name = "YUCP_DigitigradeLegs_Params";
            var parameterDefaults = new List<(string name, float value)>
            {
                (DigitigradeControllerBuilder.ParamMain, 1f),
                (DigitigradeControllerBuilder.ParamAnklesWeight, Mathf.Clamp01(data.anklesWeight))
            };
            if (data.enforceJointLimits)
            {
                parameterDefaults.Add((DigitigradeControllerBuilder.ParamThighMovement, Mathf.Clamp01(data.thighMovement)));
                parameterDefaults.Add((DigitigradeControllerBuilder.ParamKneeMovement, Mathf.Clamp01(data.kneeMovement)));
                parameterDefaults.Add((DigitigradeControllerBuilder.ParamHockMovement, Mathf.Clamp01(data.hockMovement)));
                parameterDefaults.Add((DigitigradeControllerBuilder.ParamPawFollow, Mathf.Clamp01(data.pawFlattenWeight)));
            }
            else parameterDefaults.Add((DigitigradeControllerBuilder.ParamAnkleOnly, 0f));
            parameters.parameters = parameterDefaults.Select(p => new VRCExpressionParameters.Parameter
            {
                name = p.name, valueType = VRCExpressionParameters.ValueType.Float,
                defaultValue = p.value, saved = true, networkSynced = true
            }).ToArray();
            AssetDatabase.CreateAsset(parameters, AssetDatabase.GenerateUniqueAssetPath(folder + "/YUCP_DigitigradeLegs_Params.asset"));




            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "YUCP_DigitigradeLegs_Menu";
            menu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = string.IsNullOrWhiteSpace(data.menuName) ? "Digitigrade" : data.menuName,
                    type = data.enforceJointLimits ? VRCExpressionsMenu.Control.ControlType.Toggle : VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    parameter = data.enforceJointLimits ? new VRCExpressionsMenu.Control.Parameter { name = DigitigradeControllerBuilder.ParamMain } : null,
                    value = 1f,
                    subParameters = data.enforceJointLimits ? new VRCExpressionsMenu.Control.Parameter[0]
                        : new[] { new VRCExpressionsMenu.Control.Parameter { name = DigitigradeControllerBuilder.ParamMain } }
                }
            };
            if (data.enforceJointLimits)
            {
                menu.controls.AddRange(new[]
                {
                    Radial("Thigh Movement", DigitigradeControllerBuilder.ParamThighMovement),
                    Radial("Knee Movement", DigitigradeControllerBuilder.ParamKneeMovement),
                    Radial("Hock Movement", DigitigradeControllerBuilder.ParamHockMovement),
                    Radial("Paw Follow", DigitigradeControllerBuilder.ParamPawFollow),
                    Radial("Contact Response", DigitigradeControllerBuilder.ParamAnklesWeight)
                });
            }
            else
            {
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Ankle Only Mode", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = DigitigradeControllerBuilder.ParamAnkleOnly }, value = 1f
                });
                menu.controls.Add(Radial("Ankle Weight", DigitigradeControllerBuilder.ParamAnklesWeight));
            }
            AssetDatabase.CreateAsset(menu, AssetDatabase.GenerateUniqueAssetPath(folder + "/YUCP_DigitigradeLegs_Menu.asset"));



            VRCFuryHelper.AddFullControllerToVRCFury(descriptor, controller, parameters, menu, data.menuPath);

            var solvers = avatarRoot.GetComponentsInChildren<MonoBehaviour>(true)
                .Count(m => m != null && (m.GetType().FullName == "RootMotion.FinalIK.LimbIK" || m.GetType().FullName == "RootMotion.FinalIK.CCDIK"));

            var summary = $"rig built ({rig.addedBones.Count} hidden bones, {rig.sides.Count} legs, " +
                          $"{solvers} IK solvers), humanoid remapped, FX layer generated";
            data.SetBuildSummary(summary);



            Debug.Log("[YUCP Digitigrade Rig] " + summary, data);

            return true;
        }

        private static VRCExpressionsMenu.Control Radial(string name, string parameter) => new VRCExpressionsMenu.Control
        {
            name = name, type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
            subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = parameter } }
        };

        internal static string EnsureGeneratedFolder()
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
