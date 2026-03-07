using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using com.vrcfury.api.Components;
using YUCP.Components.HandPoses;

namespace YUCP.Components.Editor
{
    [InitializeOnLoad]
    public class AutoGripProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 40;

        private static readonly Type FABRIKType = ResolveType("RootMotion.FinalIK.FABRIK");
        private static readonly Type BoneType = ResolveType("RootMotion.FinalIK.IKSolver+Bone");

        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

        private static readonly YUCPFingerType[] FingerOrder =
        {
            YUCPFingerType.Thumb,
            YUCPFingerType.Index,
            YUCPFingerType.Middle,
            YUCPFingerType.Ring,
            YUCPFingerType.Little
        };

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var components = avatarRoot.GetComponentsInChildren<AutoGripData>(true);

            foreach (var data in components)
            {
                if (data == null || !data.enabled) continue;

                try
                {
                    ProcessAutoGrip(data, avatarRoot);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AutoGrip] Error processing '{data.name}': {ex.Message}\n{ex.StackTrace}", data);
                }
            }

            return true;
        }

        private bool ValidateData(AutoGripData data, GameObject avatarRoot)
        {
            var animator = avatarRoot.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError($"[AutoGrip] '{data.name}': No Animator on avatar root.", data);
                return false;
            }

            if (!animator.isHuman)
            {
                Debug.LogError($"[AutoGrip] '{data.name}': Avatar rig must be Humanoid.", data);
                return false;
            }

            if (FABRIKType == null)
            {
                Debug.LogError($"[AutoGrip] '{data.name}': RootMotion.FinalIK.FABRIK not found. " +
                               "Install Final IK or Final IK Stub.", data);
                return false;
            }

            if (!data.fingerPoseInitialized)
            {
                Debug.LogWarning($"[AutoGrip] '{data.name}': Finger pose not initialized. Skipping.", data);
                return false;
            }

            if (!data.createToggle && !data.useExistingToggle)
                Debug.LogWarning($"[AutoGrip] '{data.name}': No toggle configured.", data);

            return true;
        }

        private void ProcessAutoGrip(AutoGripData data, GameObject avatarRoot)
        {
            if (!ValidateData(data, avatarRoot))
                return;

            var animator = avatarRoot.GetComponent<Animator>();

            if (data.verboseLogging)
                Debug.Log($"[AutoGrip] Processing '{data.name}'", data);

            bool doLeft = data.handTarget == HandTarget.Left || data.handTarget == HandTarget.Both;
            bool doRight = data.handTarget == HandTarget.Right || data.handTarget == HandTarget.Both;

            if (data.handTarget == HandTarget.Closest)
            {
                Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 propPos = data.transform.position;

                if (leftHand != null && rightHand != null)
                {
                    doLeft = Vector3.Distance(leftHand.position, propPos)
                           < Vector3.Distance(rightHand.position, propPos);
                    doRight = !doLeft;
                }
                else
                {
                    doRight = true;
                }
            }

            var fabrikNodePaths = new List<string>();

            if (doLeft)
                SetupFingerFABRIKs(data, avatarRoot, animator, YUCPHandSide.Left, "Left", fabrikNodePaths);

            if (doRight)
                SetupFingerFABRIKs(data, avatarRoot, animator, YUCPHandSide.Right, "Right", fabrikNodePaths);

            if (fabrikNodePaths.Count == 0)
            {
                Debug.LogWarning($"[AutoGrip] No FABRIK components created for '{data.name}'.", data);
                return;
            }

            var clip = CreateWeightToggleClip(fabrikNodePaths, data.name);
            WireToggle(data, clip);

            if (data.verboseLogging)
                Debug.Log($"[AutoGrip] Added {fabrikNodePaths.Count} FABRIK chains for '{data.name}'", data);
        }

        #region FABRIK Setup

        private void SetupFingerFABRIKs(AutoGripData data, GameObject avatarRoot, Animator animator,
            YUCPHandSide side, string sideName, List<string> outNodePaths)
        {
            string propSafe = SanitizeName(data.name);

            for (int i = 0; i < AutoGripData.FingerCount; i++)
            {
                var (_, proximal, intermediate, distal) =
                    YUCPAvatarRigHelper.GetFingerBones(animator, side, FingerOrder[i]);

                if (proximal == null || intermediate == null)
                {
                    if (data.verboseLogging)
                        Debug.LogWarning($"[AutoGrip] Cannot resolve {sideName} {FingerNames[i]} bones.", data);
                    continue;
                }

                Transform tip = FindOrCreateTip(distal ?? intermediate);

                var targetGO = new GameObject($"AutoGrip_Target_{FingerNames[i]}_{sideName}");
                targetGO.transform.SetParent(data.transform, false);
                targetGO.transform.position = data.GetFingerTipWorld(i);

                string nodeName = $"AutoGrip_{propSafe}_{FingerNames[i]}_{sideName}";
                var fabrikNode = new GameObject(nodeName);
                fabrikNode.transform.SetParent(avatarRoot.transform, false);

                var fabrik = fabrikNode.AddComponent(FABRIKType);

                Transform[] chain;
                if (distal != null)
                    chain = new[] { proximal, intermediate, distal, tip };
                else
                    chain = new[] { proximal, intermediate, tip };

                ConfigureFABRIK(fabrik, chain, targetGO.transform);
                outNodePaths.Add(nodeName);
            }
        }

        private Transform FindOrCreateTip(Transform lastBone)
        {
            if (lastBone.childCount > 0)
            {
                float bestDist = float.MaxValue;
                Transform best = null;
                for (int i = 0; i < lastBone.childCount; i++)
                {
                    Transform child = lastBone.GetChild(i);
                    float d = Vector3.Distance(lastBone.position, child.position);
                    if (d > 0.001f && d < bestDist)
                    {
                        bestDist = d;
                        best = child;
                    }
                }
                if (best != null) return best;
            }

            var tipGO = new GameObject("Tip");
            tipGO.transform.SetParent(lastBone, false);
            float estimatedLength = 0.01f;
            if (lastBone.parent != null)
            {
                float parentDist = Vector3.Distance(lastBone.parent.position, lastBone.position);
                if (parentDist > 0.001f) estimatedLength = parentDist * 0.7f;
            }
            tipGO.transform.localPosition = Vector3.forward * estimatedLength;
            return tipGO.transform;
        }

        private void ConfigureFABRIK(Component fabrik, Transform[] chain, Transform target)
        {
            var solverField = FABRIKType.GetField("solver", BindingFlags.Public | BindingFlags.Instance);
            if (solverField == null) return;
            object solver = solverField.GetValue(fabrik);
            if (solver == null) return;

            var solverType = solver.GetType();

            Array bonesArray = Array.CreateInstance(BoneType, chain.Length);
            for (int i = 0; i < chain.Length; i++)
            {
                object bone = Activator.CreateInstance(BoneType);
                var transformField = BoneType.GetField("transform",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (transformField != null) transformField.SetValue(bone, chain[i]);
                bonesArray.SetValue(bone, i);
            }

            var bonesField = solverType.GetField("bones",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (bonesField != null) bonesField.SetValue(solver, bonesArray);

            var targetField = solverType.GetField("target",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (targetField != null) targetField.SetValue(solver, target);

            var weightField = solverType.GetField("IKPositionWeight",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (weightField != null) weightField.SetValue(solver, 0f);

            var maxIterField = solverType.GetField("maxIterations",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (maxIterField != null) maxIterField.SetValue(solver, 4);

            var toleranceField = solverType.GetField("tolerance",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (toleranceField != null) toleranceField.SetValue(solver, 0f);
        }

        #endregion

        #region Animation

        private AnimationClip CreateWeightToggleClip(List<string> fabrikNodePaths, string propName)
        {
            var clip = new AnimationClip { name = $"AutoGrip_{SanitizeName(propName)}" };

            foreach (string nodePath in fabrikNodePaths)
            {
                var curve = new AnimationCurve();
                curve.AddKey(0f, 1f);
                curve.AddKey(1f / 60f, 1f);

                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(nodePath, FABRIKType, "solver.IKPositionWeight"),
                    curve);
            }

            return clip;
        }

        #endregion

        #region Toggle Wiring

        private void WireToggle(AutoGripData data, AnimationClip clip)
        {
            if (data.useExistingToggle)
            {
                Component toggleComp = data.selectedToggle ?? FindVRCFuryToggle(data.gameObject);
                if (toggleComp != null)
                    IntegrateWithExistingToggle(data, clip, toggleComp);
                else
                {
                    Debug.LogWarning($"[AutoGrip] No existing toggle found for '{data.name}'. Creating one.", data);
                    CreateNewToggle(data, clip);
                }
            }
            else if (data.createToggle)
            {
                CreateNewToggle(data, clip);
            }
        }

        private void CreateNewToggle(AutoGripData data, AnimationClip clip)
        {
            try
            {
                var toggle = FuryComponents.CreateToggle(data.gameObject);

                if (!string.IsNullOrEmpty(data.toggleMenuPath))
                    toggle.SetMenuPath(data.toggleMenuPath);
                if (data.toggleSaved)
                    toggle.SetSaved();
                if (data.toggleDefaultOn)
                    toggle.SetDefaultOn();

                var actions = toggle.GetActions();
                actions.AddAnimationClip(clip);
                SetMotionViaReflection(toggle, clip);

                if (data.verboseLogging)
                    Debug.Log($"[AutoGrip] Created VRCFury toggle for '{data.name}'", data);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoGrip] Toggle creation failed: {ex.Message}", data);
            }
        }

        private void IntegrateWithExistingToggle(AutoGripData data, AnimationClip clip, Component vrcFuryComponent)
        {
            try
            {
                var contentField = vrcFuryComponent.GetType().GetField("content", BindingFlags.Public | BindingFlags.Instance);
                if (contentField == null) return;

                var content = contentField.GetValue(vrcFuryComponent);
                if (content == null || content.GetType().Name != "Toggle") return;

                var stateField = content.GetType().GetField("state", BindingFlags.Public | BindingFlags.Instance);
                if (stateField == null) return;
                var state = stateField.GetValue(content);

                var actionsField = state.GetType().GetField("actions", BindingFlags.Public | BindingFlags.Instance);
                if (actionsField == null) return;
                var actionsList = actionsField.GetValue(state) as System.Collections.IList;
                if (actionsList == null) return;

                var animActionType = Type.GetType("VF.Model.StateAction.AnimationClipAction, VRCFury");
                if (animActionType == null) return;

                var animAction = Activator.CreateInstance(animActionType);
                var motionField = animActionType.GetField("motion", BindingFlags.Public | BindingFlags.Instance);
                if (motionField != null) motionField.SetValue(animAction, clip);

                actionsList.Add(animAction);
                EditorUtility.SetDirty(vrcFuryComponent);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoGrip] Toggle integration failed: {ex.Message}", data);
            }
        }

        private void SetMotionViaReflection(object toggle, AnimationClip clip)
        {
            try
            {
                var cField = toggle.GetType().GetField("c", BindingFlags.NonPublic | BindingFlags.Instance);
                if (cField == null) return;
                var model = cField.GetValue(toggle);
                var stateField = model.GetType().GetField("state", BindingFlags.Public | BindingFlags.Instance);
                if (stateField == null) return;
                var state = stateField.GetValue(model);
                var actionsField = state.GetType().GetField("actions", BindingFlags.Public | BindingFlags.Instance);
                if (actionsField == null) return;
                var actions = actionsField.GetValue(state) as System.Collections.IList;
                if (actions == null || actions.Count == 0) return;
                var last = actions[actions.Count - 1];
                var motionField = last.GetType().GetField("motion", BindingFlags.Public | BindingFlags.Instance);
                if (motionField != null) motionField.SetValue(last, clip);
            }
            catch (Exception) { }
        }

        private Component FindVRCFuryToggle(GameObject root)
        {
            foreach (var comp in root.GetComponents<Component>())
                if (comp != null && comp.GetType().Name == "VRCFury") return comp;
            foreach (Transform child in root.transform)
                foreach (var comp in child.GetComponents<Component>())
                    if (comp != null && comp.GetType().Name == "VRCFury") return comp;
            return null;
        }

        #endregion

        #region Utilities

        private static string SanitizeName(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "Prop";
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        #endregion
    }
}
