using System;
using System.Collections.Generic;
using System.Linq;
using com.vrcfury.api;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Builds the Live Grip rig. Nothing custom runs in game.
    ///
    /// A grip is one way of holding the object: the object as placed, or one per LiveGripPoint.
    /// Build time, per grip: pinned fingers reach for their pin through the avatar's own finger
    /// muscles; the others close through them, so the curl axes are this avatar's anatomy, until
    /// their segments touch the object's convex hull.
    ///
    /// Runtime: per grip and finger a point anchored to the hand is projected by a VRC Position
    /// Constraint onto the plane the finger touched, carried by the object, and one Final IK LimbIK
    /// per finger follows the blend of its grips' goals. With grip points the object hangs from a
    /// socket on the hand that blends between the grips' placements. FX layers drive the blends from
    /// the grip parameter and enable the solvers while the toggle is on; the solvers sit under the
    /// object, so hiding it also releases the hand.
    /// </summary>
    public class LiveGripProcessor : IVRCSDKPreprocessAvatarCallback
    {
        // After the digitigrade rig (MinValue + 215) has rebuilt the humanoid, before VRCFury.
        public int callbackOrder => int.MinValue + 220;

        internal static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

        internal struct Finger
        {
            public int index;
            public Transform proximal, intermediate, distal;
            // World space, with the object where the grip holds it.
            public Vector3[] chain; // MCP, PIP, DIP, tip
            public Vector3 contact, normal, bendGoal;
            public Quaternion distalRotation;
            // How far a pinned fingertip ended from its pin.
            public float pinMiss;
            public string Name => FingerNames[index];
        }

        private sealed class Grip
        {
            public string name;
            public Matrix4x4 placement;
            public List<Finger> fingers;
            public Dictionary<string, float> pose;
        }

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var datas = avatarRoot.GetComponentsInChildren<LiveGripData>(true);
            if (datas.Length == 0) return true;

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            var animator = avatarRoot.GetComponent<Animator>();
            if (descriptor == null || animator == null || !animator.isHuman)
            {
                Debug.LogError("[YUCP Live Grip] The avatar needs a humanoid rig.", datas[0]);
                return false;
            }
            if (!FinalIkBridge.IsAvailable)
            {
                Debug.LogError("[YUCP Live Grip] Final IK is not in the project.", datas[0]);
                return false;
            }

            for (var i = 0; i < datas.Length; i++)
                if (!Build(avatarRoot, descriptor, animator, datas[i], i)) return false;
            return true;
        }

        internal static Transform HandOf(Animator animator, LiveGripData data) =>
            animator.GetBoneTransform(data.hand == LiveGripData.Hand.Left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);

        internal static MeshFilter FindMesh(LiveGripData data) =>
            // ponytail: first MeshFilter only; bake a SkinnedMeshRenderer here if a skinned prop needs it.
            data.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null);

        internal static Matrix4x4 Rigid(Transform t) => Matrix4x4.TRS(t.position, t.rotation, Vector3.one);

        /// <summary>The object's rigid world pose while the hand, where it is now, holds it at <paramref name="point"/>.</summary>
        internal static Matrix4x4 Placement(LiveGripData data, Transform hand, LiveGripPoint point) =>
            point == null ? Rigid(data.transform) : Rigid(hand) * Rigid(point.transform).inverse * Rigid(data.transform);

        private static bool Build(GameObject avatarRoot, VRCAvatarDescriptor descriptor, Animator animator, LiveGripData data, int index)
        {
            var hand = HandOf(animator, data);
            var mesh = FindMesh(data);
            if (mesh == null)
            {
                Debug.LogError("[YUCP Live Grip] The object needs a MeshFilter with a mesh.", data);
                return false;
            }

            var points = data.GetComponentsInChildren<LiveGripPoint>(true);
            var grips = new List<Grip>();
            foreach (var point in points.Length == 0 ? new LiveGripPoint[] { null } : points)
            {
                var grip = new Grip { name = point != null ? point.name : "Grip", placement = Placement(data, hand, point) };
                grip.fingers = Solve(data, animator, grip.placement, point != null ? point.Pins : null, out grip.pose);
                if (grip.fingers.Count == 0)
                {
                    Debug.LogError($"[YUCP Live Grip] {data.name} / {grip.name}: no finger reaches the object. Place it in the {data.hand} hand where it should be held.", data);
                    return false;
                }
                foreach (var missed in grip.fingers.Where(f => f.pinMiss > data.fingerThickness))
                    Debug.LogWarning($"[YUCP Live Grip] {data.name} / {grip.name}: the {missed.Name} pin is {missed.pinMiss * 1000f:F0} mm out of reach.", data);
                grips.Add(grip);
            }


            var existing = !data.useExistingToggle ? null
                : data.selectedToggle != null ? data.selectedToggle : VRCFuryHelper.FindToggles(data.gameObject).FirstOrDefault();
            if (data.useExistingToggle && VRCFuryHelper.GetToggleModel(existing) == null)
            {
                Debug.LogError($"[YUCP Live Grip] {data.name}: choose the VRCFury toggle that turns the grip on.", data);
                return false;
            }
            var toggle = existing != null ? ExistingToggle(data, existing) : ("YUCPLiveGrip" + index, false);

            var ik = data.mode == LiveGripData.Mode.Live ? BuildIk(avatarRoot, data, hand, grips) : null;


            VRCParentConstraint socket = null;
            string attached = null;
            if (points.Length > 0)
            {
                var socketBone = Bone("YUCP Live Grip Socket " + data.name, hand, grips[0].placement);
                socket = socketBone.gameObject.AddComponent<VRCParentConstraint>();
                Constrain(socket, grips.Select(g => Bone("Seat " + g.name, hand, g.placement)).ToArray());
                attached = Attach(data, hand, socketBone);
                if (attached == null)
                {
                    Debug.LogError($"[YUCP Live Grip] {data.name}'s Parent Constraint has no sources to hold it by.", data);
                    return false;
                }
            }


            var position = grips.Count < 2 ? null
                : string.IsNullOrWhiteSpace(data.gripParameter) ? "LiveGrip/" + data.name : data.gripParameter.Trim();

            var folder = DigitigradeLegRigProcessor.EnsureGeneratedFolder();
            AnimatorController NewController(string name)
            {
                var c = new AnimatorController { name = name };
                AssetDatabase.CreateAsset(c, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".controller"));
                c.AddParameter(new AnimatorControllerParameter
                {
                    name = toggle.Item1,
                    type = toggle.Item2 ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Bool,
                    defaultBool = existing == null && data.defaultOn,
                    defaultFloat = existing == null && data.defaultOn ? 1f : 0f
                });
                if (position != null) DigitigradeControllerBuilder.AddFloat(c, position, 0f);
                return c;
            }
            Motion Across(AnimatorController c, string name, Motion[] motions) => motions.Length == 1 ? motions[0]
                : DigitigradeControllerBuilder.Tree(c, name, position, motions.Select((m, i) => (i / (motions.Length - 1f), m)).ToArray());
            IEnumerable<(Component, string, float)> OneHot(Component constraint, int count, int on) =>
                Enumerable.Range(0, count).Select(i => (constraint, $"Sources.source{i}.Weight", i == on ? 1f : 0f));

            var fx = NewController("YUCP_LiveGrip");
            if (ik != null)
            {
                IEnumerable<(Component, string, float)> Enabled(Func<int, bool> on) => Enumerable.Range(0, FingerNames.Length)
                    .Where(f => ik.limbs[f] != null).Select(f => ((Component)ik.limbs[f], "m_Enabled", on(f) ? 1f : 0f));
                var off = Clip(fx, avatarRoot, "Live Grip Off", Enabled(_ => false));
                var holding = Across(fx, "Live Grip Fingers", grips.Select((grip, g) => (Motion)Clip(fx, avatarRoot, "Live Grip " + grip.name,
                    Enabled(f => ik.goals[f].Any(x => x.grip == g)))).ToArray());
                AddGate(fx, "Live Grip " + data.name, toggle, off, holding, null, 1f);
            }
            if (position != null)
            {

                DigitigradeControllerBuilder.AddLayer(fx, "Live Grip Points " + data.name, Across(fx, "Live Grip Points",
                    grips.Select((grip, g) => (Motion)Clip(fx, avatarRoot, "Live Grip Point " + grip.name,
                        OneHot(socket, grips.Count, g).Concat(ik == null ? Enumerable.Empty<(Component, string, float)>() : Enumerable.Range(0, FingerNames.Length)
                            .Where(f => ik.targets[f] != null)
                            .SelectMany(f =>
                            {
                                var on = ik.goals[f].FindIndex(x => x.grip == g);
                                return OneHot(ik.targets[f], ik.goals[f].Count, on).Concat(OneHot(ik.bends[f], ik.goals[f].Count, on));
                            })))).ToArray()));
            }
            EditorUtility.SetDirty(fx);

            AnimatorController gesture = null;
            if (ik == null)
            {


                gesture = NewController("YUCP_LiveGrip_Gesture");
                var mask = new AvatarMask { name = "Live Grip Fingers", hideFlags = HideFlags.HideInHierarchy };
                for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                    mask.SetHumanoidBodyPartActive(part, part == (data.hand == LiveGripData.Hand.Left ? AvatarMaskBodyPart.LeftFingers : AvatarMaskBodyPart.RightFingers));
                AssetDatabase.AddObjectToAsset(mask, gesture);
                var poses = grips.Select(g => (Motion)PoseClip(gesture, "Live Grip " + g.name, g.pose)).ToArray();
                var (released, held) = AddGate(gesture, "Live Grip " + data.name, toggle, null, Across(gesture, "Live Grip Poses", poses), mask, 0f);
                var left = data.hand == LiveGripData.Hand.Left;
                foreach (var (state, on) in new[] { (released, false), (held, true) })
                {
                    var weight = state.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
                    weight.playable = VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.Gesture;
                    weight.layer = 0;
                    weight.goalWeight = on ? 1f : 0f;
                    weight.blendDuration = 0.15f;
                    var tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
                    var type = on ? VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Animation : VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Tracking;
                    if (left) tracking.trackingLeftFingers = type;
                    else tracking.trackingRightFingers = type;
                }
                EditorUtility.SetDirty(gesture);
            }

            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.name = "YUCP_LiveGrip_Params";
            var expressionParameters = new List<VRCExpressionParameters.Parameter>();
            if (existing == null)
                expressionParameters.Add(new VRCExpressionParameters.Parameter
                {
                    name = toggle.Item1, valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = data.defaultOn ? 1f : 0f, saved = true, networkSynced = true
                });
            if (position != null)
                expressionParameters.Add(new VRCExpressionParameters.Parameter
                {
                    name = position, valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f, saved = false, networkSynced = true
                });
            parameters.parameters = expressionParameters.ToArray();
            AssetDatabase.CreateAsset(parameters, AssetDatabase.GenerateUniqueAssetPath(folder + "/YUCP_LiveGrip_Params.asset"));

            var menuName = string.IsNullOrWhiteSpace(data.menuName) ? data.name + " Grip" : data.menuName;
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "YUCP_LiveGrip_Menu";
            menu.controls = new List<VRCExpressionsMenu.Control>();
            if (existing == null)
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = menuName,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = toggle.Item1 },
                    value = 1f
                });
            if (position != null)
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = menuName + " Position",
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = position } }
                });
            AssetDatabase.CreateAsset(menu, AssetDatabase.GenerateUniqueAssetPath(folder + "/YUCP_LiveGrip_Menu.asset"));
            AssetDatabase.SaveAssets();



            var full = FuryComponents.CreateFullController(descriptor.gameObject);
            if (fx.layers.Length > 0) full.AddController(fx);
            if (gesture != null) full.AddController(gesture, VRCAvatarDescriptor.AnimLayerType.Gesture);
            full.AddParams(parameters);
            if (menu.controls.Count > 0) full.AddMenu(menu, data.menuPath ?? string.Empty);
            if (position != null) full.AddGlobalParam(position);
            if (existing != null) full.AddGlobalParam(toggle.Item1);
            EditorUtility.SetDirty(descriptor.gameObject);

            var summary = (ik != null ? "Live" : "Baked") + ". " +
                          string.Join("; ", grips.Select(g => g.name + ": " + string.Join(", ", g.fingers.Select(f => f.Name)))) +
                          (position != null ? $"; driven by \"{position}\"" : string.Empty) +
                          (attached != null ? "; " + attached : string.Empty) +
                          (existing != null ? $"; on with the \"{ToggleName(existing)}\" toggle" : "; menu toggle added");
            data.SetBuildSummary(summary);
            Debug.Log("[YUCP Live Grip] " + data.name + ": " + summary, data);
            return true;
        }

        private sealed class Ik
        {
            public List<(int grip, Transform goal, Transform bend)>[] goals;
            public MonoBehaviour[] limbs;
            public VRCParentConstraint[] targets;
            public VRCPositionConstraint[] bends;
        }

        /// <summary>
        /// Live mode: per grip and finger a surface goal on the object and a bend goal on the hand;
        /// per finger one LimbIK on its bones, off until the FX layer enables it.
        /// </summary>
        private static Ik BuildIk(GameObject avatarRoot, LiveGripData data, Transform hand, List<Grip> grips)
        {
            var ik = new Ik
            {
                goals = FingerNames.Select(_ => new List<(int grip, Transform goal, Transform bend)>()).ToArray(),
                limbs = new MonoBehaviour[FingerNames.Length],
                targets = new VRCParentConstraint[FingerNames.Length],
                bends = new VRCPositionConstraint[FingerNames.Length]
            };


            var frames = Bone("YUCP Live Grip " + data.name, avatarRoot.transform, Rigid(data.transform));
            for (var g = 0; g < grips.Count; g++)
            {

                frames.SetPositionAndRotation(grips[g].placement.MultiplyPoint3x4(Vector3.zero), grips[g].placement.rotation);
                foreach (var finger in grips[g].fingers)
                {
                    var label = grips[g].name + " " + finger.Name;
                    var anchor = DigitigradeRigBuilder.MakeBone(label + " Anchor", hand, finger.contact, hand.rotation);
                    var surface = DigitigradeRigBuilder.MakeBone(label + " Surface", frames, finger.contact,
                        Quaternion.FromToRotation(Vector3.up, finger.normal));
                    var slide = DigitigradeRigBuilder.MakeBone(label + " Slide", surface, finger.contact, surface.rotation);


                    var slideConstraint = slide.gameObject.AddComponent<VRCPositionConstraint>();
                    slideConstraint.AffectsPositionY = false;
                    Constrain(slideConstraint, anchor);
                    ik.goals[finger.index].Add((g,
                        DigitigradeRigBuilder.MakeBone(label + " Goal", slide, finger.chain[2], finger.distalRotation),
                        DigitigradeRigBuilder.MakeBone(label + " Bend", hand, finger.bendGoal, hand.rotation)));
                }
            }
            frames.SetPositionAndRotation(data.transform.position, data.transform.rotation);
            Constrain(frames.gameObject.AddComponent<VRCParentConstraint>(), data.transform);





            var solvers = Bone("YUCP Live Grip IK", data.transform, Rigid(data.transform));
            for (var f = 0; f < FingerNames.Length; f++)
            {
                if (ik.goals[f].Count == 0) continue;
                var first = ik.goals[f][0];
                var target = DigitigradeRigBuilder.MakeBone(FingerNames[f] + " Target", frames, first.goal.position, first.goal.rotation);
                ik.targets[f] = target.gameObject.AddComponent<VRCParentConstraint>();
                Constrain(ik.targets[f], ik.goals[f].Select(x => x.goal).ToArray());
                var bend = DigitigradeRigBuilder.MakeBone(FingerNames[f] + " Bend", hand, first.bend.position, hand.rotation);
                ik.bends[f] = bend.gameObject.AddComponent<VRCPositionConstraint>();
                Constrain(ik.bends[f], ik.goals[f].Select(x => x.bend).ToArray());

                var finger = grips[first.grip].fingers.First(x => x.index == f);
                var host = Bone(FingerNames[f], solvers, Rigid(solvers));
                ik.limbs[f] = FinalIkBridge.AddLimbIk(host.gameObject, finger.proximal, finger.intermediate, finger.distal, target,
                    Vector3.Cross(finger.chain[1] - finger.chain[0], finger.bendGoal - finger.chain[0]));
                FinalIkBridge.UseBendGoal(ik.limbs[f], bend);
                // OJO: a VRC constraint at weight 0 pins the bone at RotationAtRest, it does not pass the animation through, so the IK is switched with m_Enabled instead https://docs.unity3d.com/Manual/class-RotationConstraint.html
                ik.limbs[f].enabled = false;
            }
            return ik;
        }

        /// <summary>
        /// The parameter of an existing VRCFury toggle, made global so this controller can read it
        /// unrenamed. A slider toggle's parameter is a float.
        /// </summary>
        private static (string, bool) ExistingToggle(LiveGripData data, Component toggle)
        {
            var model = VRCFuryHelper.GetToggleModel(toggle);
            var type = model.GetType();
            var name = VRCFuryHelper.GetToggleGlobalParameter(toggle);
            if (string.IsNullOrEmpty(name))
            {
                name = "LiveGrip/" + data.name + "/Toggle";
                type.GetField("useGlobalParam").SetValue(model, true);
                type.GetField("globalParam").SetValue(model, name);
            }
            return (name, (bool)type.GetField("slider").GetValue(model));
        }

        internal static string ToggleName(Component toggle)
        {
            var model = VRCFuryHelper.GetToggleModel(toggle);
            var name = model?.GetType().GetField("name").GetValue(model) as string;
            return string.IsNullOrEmpty(name) ? toggle.name : name;
        }

        /// <summary>
        /// Two states: Held while the toggle is on, Released otherwise. A float toggle (a slider)
        /// counts as on above 0.5.
        /// </summary>
        private static (AnimatorState released, AnimatorState held) AddGate(AnimatorController controller, string name,
            (string, bool) toggle, Motion released, Motion held, AvatarMask mask, float weight)
        {
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
            var off = machine.AddState("Released");
            off.motion = released;
            off.writeDefaultValues = true;
            var on = machine.AddState("Held");
            on.motion = held;
            on.writeDefaultValues = true;
            machine.defaultState = off;

            AnimatorStateTransition Transition(AnimatorState from, AnimatorState to)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.duration = 0f;
                return t;
            }
            void Toggled(AnimatorStateTransition t, bool value)
            {
                if (toggle.Item2) t.AddCondition(value ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 0.5f, toggle.Item1);
                else t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, toggle.Item1);
            }
            Toggled(Transition(off, on), true);
            Toggled(Transition(on, off), false);
            controller.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = weight, stateMachine = machine, avatarMask = mask });
            return (off, on);
        }

        /// <summary>The baked hand: every finger muscle of the hand, held.</summary>
        private static AnimationClip PoseClip(AnimatorController controller, string name, Dictionary<string, float> pose)
        {
            var clip = new AnimationClip { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(clip, controller);
            foreach (var muscle in pose)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), MuscleAttribute(muscle.Key)),
                    AnimationCurve.Constant(0f, 0f, muscle.Value));
            return clip;
        }

        /// <summary>HumanTrait's "Left Index 1 Stretched" as a clip attribute: "LeftHand.Index.1 Stretched".</summary>
        internal static string MuscleAttribute(string muscle)
        {
            var parts = muscle.Split(new[] { ' ' }, 3);
            return parts[0] + "Hand." + parts[1] + "." + parts[2];
        }

        /// <summary>
        /// Hangs the object from the socket and says how. An object that already has a Parent
        /// Constraint keeps it with the source that holds it in the hand swapped for the socket, so
        /// its other sources (a world drop, say) still work. That source is the one under the hand,
        /// else the one nearest it: Live Grip runs before VRCFury, so an anchor that an Armature
        /// Link puts on the hand is still elsewhere in the hierarchy here.
        /// </summary>
        private static string Attach(LiveGripData data, Transform hand, Transform socket)
        {
            var constraint = data.GetComponent<VRCParentConstraint>();
            if (constraint == null)
            {
                Constrain(data.gameObject.AddComponent<VRCParentConstraint>(), socket);
                return "hangs from the hand";
            }
            var sources = Enumerable.Range(0, constraint.Sources.Count)
                .Where(i => constraint.Sources[i].SourceTransform != null).ToList();
            if (sources.Count == 0) return null;
            // ponytail: nearest-to-hand guess; add an explicit source field if a prop ever needs another.
            var slot = sources.Where(i => constraint.Sources[i].SourceTransform.IsChildOf(hand)).DefaultIfEmpty(-1).First();
            if (slot < 0) slot = sources.OrderBy(i => Vector3.Distance(constraint.Sources[i].SourceTransform.position, hand.position)).First();
            var source = constraint.Sources[slot];
            constraint.Sources[slot] = new VRCConstraintSource(socket, source.Weight, Vector3.zero, Vector3.zero);
            return $"rerouted its \"{source.SourceTransform.name}\" constraint source through the hand";
        }

        /// <summary>
        /// Closes the hand around the object at <paramref name="placement"/>, its rigid world pose.
        /// Pins are given on the object as it is authored. The avatar's pose is restored afterwards;
        /// results are in world space.
        /// </summary>
        internal static List<Finger> Solve(LiveGripData data, Animator animator, Matrix4x4 placement, Transform[] pins) =>
            Solve(data, animator, placement, pins, out _);

        /// <summary>As above; <paramref name="handPose"/> is every finger muscle of the hand at the grip, for baking.</summary>
        internal static List<Finger> Solve(LiveGripData data, Animator animator, Matrix4x4 placement, Transform[] pins,
            out Dictionary<string, float> handPose)
        {
            handPose = null;
            var side = data.hand == LiveGripData.Hand.Left ? "Left" : "Right";
            var hand = HandOf(animator, data);
            var thickness = data.fingerThickness;
            var held = placement * Rigid(data.transform).inverse;
            var shape = MakeShape(FindMesh(data), held);
            var saved = animator.GetComponentsInChildren<Transform>(true)
                .Select(t => (t, t.localPosition, t.localRotation)).ToArray();


            var handToWorld = hand.localToWorldMatrix;
            var poser = new HumanPoseHandler(animator.avatar, animator.transform);
            var pose = new HumanPose();
            poser.GetHumanPose(ref pose);
            var open = (float[])pose.muscles.Clone();
            var result = new List<Finger>();
            try
            {
                for (var f = 0; f < FingerNames.Length; f++)
                {
                    var name = FingerNames[f];
                    var bones = new[] { "Proximal", "Intermediate", "Distal" }
                        .Select(b => animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + name + b)))
                        .ToArray();
                    if (bones.Any(b => b == null)) continue;
                    var muscles = new[] { " 1 Stretched", " 2 Stretched", " 3 Stretched" }
                        .Select(m => Array.IndexOf(HumanTrait.MuscleName, side + " " + name + m)).ToArray();
                    var spread = Array.IndexOf(HumanTrait.MuscleName, side + " " + name + " Spread");

                    var tip = bones[2].childCount > 0
                        ? bones[2].GetChild(0).localPosition
                        : bones[2].InverseTransformPoint(bones[2].position + (bones[2].position - bones[1].position) * 0.8f);

                    Matrix4x4 Correction() => handToWorld * hand.worldToLocalMatrix;
                    Vector3[] Current()
                    {
                        poser.SetHumanPose(ref pose);
                        var correction = Correction();
                        return new[] { bones[0].position, bones[1].position, bones[2].position, bones[2].TransformPoint(tip) }
                            .Select(p => correction.MultiplyPoint3x4(p)).ToArray();
                    }
                    Vector3[] Chain(float[] curl)
                    {

                        for (var j = 0; j < 3; j++) pose.muscles[muscles[j]] = Mathf.Lerp(open[muscles[j]], -1f, curl[j]);
                        return Current();
                    }
                    float Gap(Vector3 p) => Vector3.Distance(p, shape.ClosestPoint(p));

                    var closed = Chain(new[] { 1f, 1f, 1f });
                    Chain(new float[3]);
                    Vector3[] chain;
                    Vector3 contact, normal;
                    var miss = 0f;
                    if (pins != null && pins[f] != null)
                    {

                        contact = shape.ClosestPoint(held.MultiplyPoint3x4(pins[f].position));
                        normal = Normal(shape, contact);
                        var target = contact + normal * thickness;
                        Descend(pose.muscles, new[] { muscles[0], spread, muscles[1], muscles[2] }, () =>
                        {
                            var c = Current();
                            var cost = (c[3] - target).sqrMagnitude;
                            for (var s = 0; s < 3; s++)
                            foreach (var p in Samples(c, s))
                            {
                                var sink = thickness - Gap(p);
                                if (sink > 0f) cost += 4f * sink * sink;
                            }
                            return cost;
                        });
                        chain = Current();
                        miss = Vector3.Distance(chain[3], target);
                    }
                    else
                    {
                        var curls = CloseUntilContact(Chain, Gap, thickness, out var touching);
                        if (touching < 0) continue;
                        chain = Chain(curls);
                        var sample = Samples(chain, touching).OrderBy(Gap).First();
                        contact = shape.ClosestPoint(sample);
                        if ((sample - contact).sqrMagnitude < 1e-12f)
                        {
                            Debug.LogWarning($"[YUCP Live Grip] {side} {name} starts inside the object; left to the animation.", data);
                            continue;
                        }
                        normal = (sample - contact).normalized;
                    }




                    var knuckle = chain[1] - (chain[0] + chain[2]) * 0.5f;
                    if (knuckle.magnitude < 0.05f * Vector3.Distance(chain[0], chain[1]))
                        knuckle = closed[1] - (closed[0] + closed[2]) * 0.5f;
                    result.Add(new Finger
                    {
                        index = f,
                        proximal = bones[0], intermediate = bones[1], distal = bones[2],
                        chain = chain,
                        contact = contact,
                        normal = normal,
                        bendGoal = chain[1] + knuckle.normalized * Vector3.Distance(chain[0], chain[1]),
                        distalRotation = Correction().rotation * bones[2].rotation,
                        pinMiss = miss
                    });
                }

                handPose = FingerNames
                    .SelectMany(n => new[] { " 1 Stretched", " Spread", " 2 Stretched", " 3 Stretched" }.Select(m => side + " " + n + m))
                    .ToDictionary(m => m, m => pose.muscles[Array.IndexOf(HumanTrait.MuscleName, m)]);
            }
            finally
            {
                foreach (var (t, position, rotation) in saved)
                {
                    t.localPosition = position;
                    t.localRotation = rotation;
                }
                UnityEngine.Object.DestroyImmediate(shape.gameObject);
            }
            return result;
        }

        /// <summary>
        /// The object's convex hull, held: PhysX only answers ClosestPoint for convex colliders, and
        /// only on active ones -- props are often toggled off, and the grip may hold it elsewhere.
        /// </summary>
        internal static MeshCollider MakeShape(MeshFilter filter, Matrix4x4 held)
        {
            var host = new GameObject("YUCP Live Grip Shape") { hideFlags = HideFlags.HideAndDontSave };
            host.transform.SetPositionAndRotation(held.MultiplyPoint3x4(filter.transform.position), held.rotation * filter.transform.rotation);
            host.transform.localScale = filter.transform.lossyScale;
            var collider = host.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = true;
            Physics.SyncTransforms();
            return collider;
        }

        /// <summary>Outward normal of the convex shape at a point on its surface.</summary>
        private static Vector3 Normal(Collider shape, Vector3 surface)
        {
            var probe = surface + (surface - shape.bounds.center).normalized * 0.002f;
            return (probe - shape.ClosestPoint(probe)).normalized;
        }

        /// <summary>
        /// Closes a three-joint finger the way a hand closes on an object: all joints curl together,
        /// and when a segment touches, it and the joints before it stop while the rest keep closing.
        /// <paramref name="chain"/> maps joint curls (0 open, 1 closed) to the four joint points;
        /// <paramref name="gap"/> is a point's distance to the object (0 inside). Returns the curls;
        /// <paramref name="touching"/> is the last segment that touched, -1 if none did.
        /// </summary>
        internal static float[] CloseUntilContact(Func<float[], Vector3[]> chain, Func<Vector3, float> gap,
            float thickness, out int touching, int steps = 64)
        {
            var curl = new float[3];
            var free = 0;
            touching = -1;
            for (var step = 0; step <= steps && free < 3;)
            {
                var next = (float[])curl.Clone();
                for (var j = free; j < 3; j++) next[j] = (float)step / steps;
                var points = chain(next);
                var hit = -1;
                for (var segment = free; segment < 3; segment++)
                    if (Samples(points, segment).Any(p => gap(p) < thickness)) hit = segment;
                if (hit < 0)
                {
                    curl = next;
                    step++;
                    continue;
                }

                touching = hit;
                free = hit + 1;
            }
            return curl;
        }

        /// <summary>
        /// Coordinate descent on <paramref name="values"/> at <paramref name="dofs"/>, kept in [-1, 1]
        /// (muscle space). The step halves whenever no coordinate improves, down to 0.005.
        /// </summary>
        internal static void Descend(float[] values, int[] dofs, Func<float> cost)
        {
            var best = cost();
            for (var step = 0.25f; step >= 0.005f; step *= 0.5f)
            for (var round = 0; round < 50; round++)
            {
                var improved = false;
                foreach (var d in dofs)
                foreach (var sign in new[] { 1f, -1f })
                {
                    var old = values[d];
                    values[d] = Mathf.Clamp(old + sign * step, -1f, 1f);
                    var c = cost();
                    if (c < best)
                    {
                        best = c;
                        improved = true;
                    }
                    else values[d] = old;
                }
                if (!improved) break;
            }
        }

        private static IEnumerable<Vector3> Samples(Vector3[] points, int segment) =>
            new[] { 0.25f, 0.5f, 0.75f, 1f }.Select(t => Vector3.Lerp(points[segment], points[segment + 1], t));

        private static Transform Bone(string name, Transform parent, Matrix4x4 pose) =>
            DigitigradeRigBuilder.MakeBone(name, parent, pose.MultiplyPoint3x4(Vector3.zero), pose.rotation);

        /// <summary>Zero offsets, first source fully weighted.</summary>
        private static void Constrain(VRCConstraintBase constraint, params Transform[] sources)
        {
            constraint.IsActive = false;
            constraint.Locked = false;
            for (var i = 0; i < sources.Length; i++)
                constraint.Sources.Add(new VRCConstraintSource(sources[i], i == 0 ? 1f : 0f, Vector3.zero, Vector3.zero));
            constraint.Locked = true;
            constraint.IsActive = true;
        }

        private static AnimationClip Clip(AnimatorController controller, GameObject root, string name,
            IEnumerable<(Component component, string property, float value)> curves)
        {
            var clip = new AnimationClip { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(clip, controller);
            foreach (var (component, property, value) in curves)
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(component.transform, root.transform), component.GetType(), property),
                    AnimationCurve.Constant(0f, 0f, value));
            return clip;
        }
    }
}
