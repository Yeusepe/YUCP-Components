using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Builds a joint-limited digitigrade rig, or the optional Rexouium-style two-pass rig.
    ///
    /// Shape of the result, per leg:
    ///   Hips
    ///     Planti_Hip / Planti_Knee / Planti_Ankle    hidden, unskinned, humanoid maps here
    ///     thigh / shin / metatarsus / foot           visible, constraint-driven
    ///   YUCP_DigiLegRig
    ///     LegNodes            parent-constrained to Hips
    ///       Planti_*_IK       LimbIK pass 1 re-solves the plantigrade leg cleanly
    ///       Digi_*_IK         LimbIK pass 2 folds shin + metatarsus onto the same goal
    ///     Solvers             the LimbIK hosts and IKExecutionOrder
    ///
    /// The visible bones then rotation-constrain to source0 (plantigrade) or source1 (solved
    /// digitigrade), which the FX layer crossfades.
    /// </summary>
    public static class DigitigradeRigBuilder
    {
        public const string RigRootName = "YUCP_DigiLegRig";

        public sealed class Result
        {
            public bool success;
            public string error;
            public GameObject rigRoot;
            public readonly List<Transform> addedBones = new List<Transform>();
            public readonly Dictionary<HumanBodyBones, Transform> humanRemap = new Dictionary<HumanBodyBones, Transform>();
            public readonly List<Side> sides = new List<Side>();
        }

        /// <summary>Everything the FX controller needs to address one leg.</summary>
        public sealed class Side
        {
            public string suffix;               // ".L" / ".R"
            public Transform thigh, shin, metatarsus, foot;
            public Transform plantiHip, plantiKnee, plantiAnkle;
            public Transform ankleUpNode, ankleDownNode, metatarsusRestNode;
            public VRCRotationConstraint thighConstraint, shinConstraint, metatarsusConstraint;

            /// <summary>Single-source parent constraint pinning the paw to its socket under the
            /// metatarsus. Never blended: blending paw POSITIONS across disagreeing sources smears
            /// the ankle into a curve. The Rex nests its ankle under TopFut for the same reason.</summary>
            public VRCParentConstraint footConstraint;

            /// <summary>The soft pull of the paw toward the tracked foot's rotation -- Rex's ankle
            /// constraint. GlobalWeight is animated: 1 in plantigrade (the paw IS the tracked foot,
            /// as the original avatar behaved), pawFlattenWeight in digitigrade.</summary>
            public VRCRotationConstraint pawFlattenConstraint;
        }

        public static Result Build(GameObject avatarRoot, Animator animator, DigitigradeLegRigData data)
        {
            var result = new Result();

            if (!FinalIkBridge.IsAvailable)
            {
                result.error = "Final IK was not found in this project. The digitigrade rig needs RootMotion.FinalIK.LimbIK, which is a paid asset and cannot ship with YUCP.";
                return result;
            }

            if (data.enforceJointLimits && (!FinalIkBridge.JointLimitsAvailable ||
                !ValidLimits(data.kneeAngleLimits) || !ValidLimits(data.hockAngleLimits)))
            {
                result.error = "Joint limits require Final IK CCDIK and RotationLimitHinge, with 0 < minimum < maximum < 180 degrees for both joints.";
                return result;
            }

            if (!data.IsComplete())
            {
                result.error = "The visible leg chain is incomplete. Every leg needs thigh, shin, metatarsus and foot -- add YUCP/Digitigrade Leg Split first if this rig only has three segments.";
                return result;
            }

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) { result.error = "Could not resolve the Hips bone."; return result; }

            result.rigRoot = new GameObject(RigRootName);
            result.rigRoot.transform.SetParent(avatarRoot.transform, false);

            var legNodes = new GameObject("LegNodes");
            legNodes.transform.SetParent(result.rigRoot.transform, false);
            legNodes.transform.position = hips.position;
            legNodes.transform.rotation = hips.rotation;
            AddParentConstraint(legNodes, hips);

            var solverHost = new GameObject("Solvers");
            solverHost.transform.SetParent(result.rigRoot.transform, false);

            var plantiSolvers = new List<MonoBehaviour>();
            var digiSolvers = new List<MonoBehaviour>();

            foreach (var side in EnumerateSides(data))
            {
                BuildSide(result, side, hips, legNodes.transform, solverHost.transform, data, plantiSolvers, digiSolvers);
            }

            // Legacy passes need ordered evaluation; constrained legs each solve the complete chain.
            var execHost = new GameObject("IK Execution Order");
            execHost.transform.SetParent(result.rigRoot.transform, false);
            FinalIkBridge.AddExecutionOrder(execHost, animator, plantiSolvers.Concat(digiSolvers).ToList());

            result.success = true;
            return result;
        }

        private static void BuildSide(
            Result result,
            Side side,
            Transform hips,
            Transform legNodes,
            Transform solverHost,
            DigitigradeLegRigData data,
            List<MonoBehaviour> plantiSolvers,
            List<MonoBehaviour> digiSolvers)
        {
            var suffix = side.suffix;

            // --- hidden plantigrade chain: hip -> knee -> ankle, straight through the same endpoints
            side.plantiHip = MakeBone("Planti_Hip" + suffix, hips, side.thigh.position, side.thigh.rotation);
            side.plantiKnee = MakeBone("Planti_Knee" + suffix, side.plantiHip, side.shin.position, side.shin.rotation);
            side.plantiAnkle = MakeBone("Planti_Ankle" + suffix, side.plantiKnee, side.foot.position, side.foot.rotation);

            result.addedBones.Add(side.plantiHip);
            result.addedBones.Add(side.plantiKnee);
            result.addedBones.Add(side.plantiAnkle);

            result.humanRemap[suffix == ".L" ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg] = side.plantiHip;
            result.humanRemap[suffix == ".L" ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg] = side.plantiKnee;
            result.humanRemap[suffix == ".L" ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot] = side.plantiAnkle;

            // --- solver nodes: a mirror of both chains, living in hips space
            var plantiHipIk = MakeBone("Planti_Hip_IK" + suffix, legNodes, side.thigh.position, side.thigh.rotation);

            // Rex redistributes the helper leg's lengths: a longer thigh and a shin at ~53%
            // of the human shin. Move the KNEE toward the ankle, keeping both endpoints fixed.
            // Shortening the shin by moving the ankle instead leaves the standing goal out of
            // reach and holds the first pass fully extended until the tracked foot rises.
            var passShinScale = Mathf.Lerp(1f, 0.53f, Mathf.Clamp01(data.kneeStraightness));
            var passKneePos = Vector3.Lerp(side.foot.position, side.shin.position, passShinScale);
            var plantiKneeIk = data.enforceJointLimits ? null : MakeBone("Planti_Knee_IK" + suffix, plantiHipIk, passKneePos, side.shin.rotation);
            var plantiAnkleIk = data.enforceJointLimits ? null : MakeBone("Planti_Ankle_IK" + suffix, plantiKneeIk, side.foot.position, side.foot.rotation);

            // The digitigrade solver chain must mirror the visible bones EXACTLY.
            //
            // Only the solved rotations are transferred to the real leg, never positions, so a
            // solver chain of different proportions produces angles for a limb that does not exist:
            // the solver reaches its goal perfectly while the real paw falls short, and the leg
            // reads as stretched. Fold depth has to come from the bone layout itself -- move the
            // split, or offset the joint off the knee-to-ankle chord -- not from inflating this.
            // The digi chain hangs directly off the SOLVED plantigrade hip -- hierarchy
            // propagation is same-frame, where a copy-constraint hop would add a frame of lag
            // between the tracked leg and the visible one.
            var digiKneeIk = MakeBone("Digi_Knee_IK" + suffix, plantiHipIk, side.shin.position, side.shin.rotation);
            var digiMetaIk = MakeBone("Digi_Meta_IK" + suffix, digiKneeIk, side.metatarsus.position, side.metatarsus.rotation);
            var digiAnkleIk = MakeBone("Digi_Ankle_IK" + suffix, digiMetaIk, side.foot.position, side.foot.rotation);

            // Aim at the visible chain's solved knee. Rex stabilizes roll with an object ahead
            // of and above the hips. Derive both local axes from the rest pose so avatars with
            // different bone rolls keep their original orientation when the aim activates.
            var legScaleAim = Vector3.Distance(side.plantiKnee.position, side.plantiAnkle.position);
            var avatarTransform = result.rigRoot.transform.parent;
            var thighDirection = (side.shin.position - side.thigh.position).normalized;
            var upDirection = avatarTransform.forward * 2.2f + avatarTransform.up * 1.3f;
            var bendNormal = ResolveBendNormal(side, data);
            if (Vector3.ProjectOnPlane(upDirection, thighDirection).sqrMagnitude < 1e-8f)
                upDirection = Vector3.Cross(thighDirection, bendNormal).normalized;
            var hipAim = plantiHipIk;
            if (!data.enforceJointLimits)
            {
                var upVector = MakeBone("HipUpVector" + suffix, hips,
                    side.plantiHip.position + upDirection * legScaleAim,
                    hips.rotation);

                hipAim = MakeBone("HipAim" + suffix, side.plantiHip, side.thigh.position, side.thigh.rotation);
                var aim = hipAim.gameObject.AddComponent<VRCAimConstraint>();
                aim.IsActive = false;
                aim.Locked = false;
                aim.Sources.Add(new VRCConstraintSource(digiKneeIk, 1f, Vector3.zero, Vector3.zero));
                aim.AimAxis = side.thigh.InverseTransformDirection(thighDirection).normalized;
                aim.UpAxis = side.thigh.InverseTransformDirection(
                    Vector3.ProjectOnPlane(upDirection, thighDirection).normalized).normalized;
                aim.WorldUp = VRCConstraintBase.WorldUpType.ObjectUp;
                aim.WorldUpTransform = upVector;
                aim.Locked = true;
                aim.IsActive = true;
            }

            // --- goal: the real (plantigrade) ankle, position and rotation
            var goalHost = new GameObject("Goal" + suffix);
            goalHost.transform.SetParent(solverHost, false);
            goalHost.transform.position = side.plantiAnkle.position;
            goalHost.transform.rotation = side.plantiAnkle.rotation;
            AddParentConstraint(goalHost, side.plantiAnkle);

            // Pass 1 normalises the plantigrade leg: same endpoints, but a clean twist-free
            // solution with a knee plane we control.
            var plantiSolver = data.enforceJointLimits ? null : FinalIkBridge.AddLimbIk(goalHost, plantiHipIk, plantiKneeIk, plantiAnkleIk, goalHost.transform, -bendNormal);

            // Pass 2 folds shin + metatarsus onto the same goal. The flipped bend normal is what
            // makes that joint bend backwards into a hock.
            // The digitigrade goal is NOT the plantigrade goal: on a mesh authored straight, the
            // same foot target just re-derives the bind pose and the toggle does nothing. Standing
            // digitigrade means standing on the toes -- so the goal is the tracked ankle pitched
            // about the TOE TIP. The toe stays planted, the ankle lifts, the reach from the knee
            // shortens, and the hock folds to absorb it. Parented under the goal so it follows the
            // tracked foot everywhere.
            var toe = FindToe(side.foot);
            var toePivot = toe != null ? toe.position : side.foot.position + (side.foot.position - side.metatarsus.position) * 0.5f;
            var stance = Quaternion.AngleAxis(data.stanceAngle, bendNormal);
            var digiGoal = new GameObject("Digi Goal" + suffix).transform;
            digiGoal.SetParent(goalHost.transform, false);
            digiGoal.position = stance * (goalHost.transform.position - toePivot) + toePivot;
            digiGoal.rotation = stance * goalHost.transform.rotation;

            var digiHost = new GameObject("Digi Solver" + suffix);
            digiHost.transform.SetParent(solverHost, false);
            var digiSolver = data.enforceJointLimits
                ? FinalIkBridge.AddConstrainedLeg(digiHost, new[] { plantiHipIk, digiKneeIk, digiMetaIk, digiAnkleIk },
                    digiGoal, bendNormal, data.kneeAngleLimits, data.hockAngleLimits)
                : FinalIkBridge.AddLimbIk(digiHost, digiKneeIk, digiMetaIk, digiAnkleIk, digiGoal, bendNormal);

            // Rex softness: the paw is only partially flattened toward the tracked foot's
            // orientation (Rex runs 0.2); the rest of its rotation rides the hock fold. At 1.0
            // the paw locks to the foot and the whole leg reads mechanical.
            if (digiSolver != null && !data.enforceJointLimits)
            {
                var soft = new SerializedObject(digiSolver);
                soft.FindProperty("solver.IKRotationWeight").floatValue = Mathf.Clamp01(data.pawFlattenWeight);
                soft.ApplyModifiedPropertiesWithoutUndo();
            }

            if (plantiSolver != null) plantiSolvers.Add(plantiSolver);
            if (digiSolver != null) digiSolvers.Add(digiSolver);

            // --- the plantigrade source for the metatarsus: the BIND pose, riding the hidden
            // plantigrade knee (as Rex hangs TopFut_L Node off Planti_Left_Knee).
            //
            // The mesh was skinned in the bind pose, so OFF must reproduce it exactly -- any
            // synthetic "straightened" pose here visibly bends the leg where the mesh expects
            // none. The ON/OFF difference comes from the DIGITIGRADE side instead: the stance
            // pitch below shortens the digi solver's reach so the hock genuinely folds.
            side.metatarsusRestNode = MakeBone("Meta_Rest" + suffix, side.plantiKnee,
                side.metatarsus.position, side.metatarsus.rotation);
            side.ankleUpNode = MakeBone("Meta_Up" + suffix, side.shin, side.metatarsus.position,
                Quaternion.AngleAxis(data.ankleUpAngle, bendNormal) * side.metatarsus.rotation);
            side.ankleDownNode = MakeBone("Meta_Down" + suffix, side.shin, side.metatarsus.position,
                Quaternion.AngleAxis(-data.ankleDownAngle, bendNormal) * side.metatarsus.rotation);

            // --- the switchboard. source0 = plantigrade, source1 = solved digitigrade.
            side.thighConstraint = AddRotationConstraint(side.thigh.gameObject,
                new[] { side.plantiHip, hipAim }, new[] { 1f, 0f });
            side.shinConstraint = AddRotationConstraint(side.shin.gameObject,
                new[] { side.plantiKnee, digiKneeIk }, new[] { 1f, 0f });
            side.metatarsusConstraint = AddRotationConstraint(side.metatarsus.gameObject,
                new[] { side.metatarsusRestNode, digiMetaIk, side.ankleDownNode, side.ankleUpNode },
                new[] { 1f, 0f, 0f, 0f });

            if (data.enforceJointLimits)
            {
                ConfigureMotionConstraint(side.thighConstraint, plantiHipIk);
                ConfigureMotionConstraint(side.shinConstraint, digiKneeIk);
                ConfigureMotionConstraint(side.metatarsusConstraint, digiMetaIk);
            }

            // The paw rides a socket NESTED under the metatarsus, exactly as the Rex nests its
            // ankle under TopFut. The visible foot cannot be parented there itself -- Unity's
            // humanoid rejected that while the foot was mapped, and the skeleton must keep
            // matching the import -- but a generic socket node can be, and a single-source parent
            // constraint is rigid: the paw's position is DERIVED from the metatarsus fold at every
            // blend value. Blending paw positions across sources (the previous 4-source design)
            // smears the ankle into a curve at any mid-blend, which Ankle Weight lives in.
            var pawSocket = MakeBone("PawSocket" + suffix, side.metatarsus, side.foot.position, side.foot.rotation);
            side.footConstraint = AddMultiParentConstraint(side.foot.gameObject,
                new[] { pawSocket }, new[] { 1f });

            // Flatten on the socket so the unweighted rotation inherits the metatarsus, as
            // Rex's ankle inherits TopFut. VRC blends from RotationAtRest relative to the parent,
            // not from a preceding constraint: stacking this on the foot would blend from the
            // shin and override the socket rotation. FX animates GlobalWeight on this socket.
            side.pawFlattenConstraint = AddRotationConstraint(pawSocket.gameObject,
                new[] { side.plantiAnkle }, new[] { 1f });
            side.pawFlattenConstraint.RotationAtRest = pawSocket.localEulerAngles;
            side.pawFlattenConstraint.GlobalWeight = 1f;

            BuildContacts(result, side, bendNormal);

            result.sides.Add(side);
        }

        /// <summary>
        /// The plane the hock folds in. At rest the metatarsus is already bent relative to the shin,
        /// so the cross product of the two segments gives it directly; a straight leg falls back to
        /// the avatar's sideways axis.
        /// </summary>
        private static Vector3 ResolveBendNormal(Side side, DigitigradeLegRigData data)
        {
            if (data.bendNormalOverride != Vector3.zero) return data.bendNormalOverride.normalized;

            var upper = side.metatarsus.position - side.shin.position;
            var lower = side.foot.position - side.metatarsus.position;
            var normal = Vector3.Cross(upper, lower);

            if (normal.sqrMagnitude < 1e-8f)
            {
                // Both hocks fold in the same sagittal direction. A side-dependent world
                // axis reverses the right hock and breaks rotated avatars.
                normal = -data.GetComponentInParent<Animator>(true).transform.right;
            }

            return normal.normalized;
        }

        /// <summary>
        /// The Rex-style contact sensors that feed the reactive branch of the FX tree.
        ///
        /// An animator cannot read a bone's rotation, so the Rex measures it with proximity
        /// contacts: a small sender swings with the tracked ankle. Sensor angles and radii
        /// define an input response independently of the metatarsus output limit poses.
        /// Foot plant is a follower pinned on the avatar's forward axis -- as the tracked foot
        /// steps fore or aft the sender walks out of the receiver, so proximity reads
        /// "how planted under the body is this foot". Geometry is derived from this avatar's leg
        /// rather than copying the Rex's hand-tuned distances; the logic is identical.
        /// </summary>
        private static bool ValidLimits(Vector2 limits) => limits.x > 0f && limits.y < 180f && limits.x < limits.y;

        private static void BuildContacts(Result result, Side side, Vector3 bendNormal)
        {
            var sfx = side.suffix;
            var legScale = Vector3.Distance(side.plantiKnee.position, side.plantiAnkle.position);
            if (legScale < 1e-4f) return;

            // --- toe pitch sensor ---
            // Rex's virtual lever is ~0.57 shin lengths, pointing forward at rest. Using
            // the mesh's toe makes sensitivity depend on paw length and imported bone roll.
            var forward = Vector3.ProjectOnPlane(result.rigRoot.transform.parent.forward, bendNormal).normalized;
            if (forward.sqrMagnitude < 1e-8f)
                forward = Vector3.Cross(bendNormal, side.plantiKnee.position - side.plantiAnkle.position).normalized;
            var reach = forward * (0.57f * legScale);

            var pitchTag = "YUCPToePitch" + sfx;
            var senderHost = MakeBone("ToeSense" + sfx, side.plantiAnkle, side.plantiAnkle.position + reach, side.plantiAnkle.rotation);
            var toeSender = senderHost.gameObject.AddComponent<VRCContactSender>();
            toeSender.radius = reach.magnitude / (60f * Mathf.Abs(senderHost.lossyScale.x));
            toeSender.collisionTags = new List<string> { pitchTag };

            // Measured from the reference rig: +18/-60 degree input centers with radii
            // 0.08/0.28 for a 0.3-unit lever. The smaller radii leave a neutral dead zone.
            // These are sensor calibration values, not anatomical joint limits.
            var upPos = side.plantiAnkle.position + Quaternion.AngleAxis(18f, bendNormal) * reach;
            var downPos = side.plantiAnkle.position + Quaternion.AngleAxis(-60f, bendNormal) * reach;

            AddProximityReceiver(side.plantiKnee, "ToeUpRec" + sfx, upPos,
                reach.magnitude * (0.08f / 0.3f), DigitigradeControllerBuilder.ParamToeUp(sfx), pitchTag);
            AddProximityReceiver(side.plantiKnee, "ToeDownRec" + sfx, downPos,
                reach.magnitude * (0.28f / 0.3f), DigitigradeControllerBuilder.ParamToeDown(sfx), pitchTag);

            // --- foot plant sensor ---
            var plantTag = "YUCPPlant" + sfx;
            var signal = MakeBone("PlantSignal" + sfx, side.plantiAnkle, side.plantiAnkle.position, side.plantiAnkle.rotation);
            var plantSender = signal.gameObject.AddComponent<VRCContactSender>();
            plantSender.radius = 0.04f * legScale / Mathf.Abs(signal.lossyScale.x);
            plantSender.collisionTags = new List<string> { plantTag };

            var follower = MakeBone("PlantSense" + sfx, result.rigRoot.transform, side.plantiAnkle.position, result.rigRoot.transform.rotation);
            var pin = follower.gameObject.AddComponent<VRCPositionConstraint>();
            pin.IsActive = false;
            pin.Locked = false;
            pin.Sources.Add(new VRCConstraintSource(side.plantiAnkle, 1f, Vector3.zero, Vector3.zero));
            pin.AffectsPositionZ = false;   // frozen fore/aft, exactly Rex's FootPlantReciever
            pin.Locked = true;
            pin.IsActive = true;

            AddProximityReceiver(follower, "PlantRec" + sfx, follower.position,
                0.4f * legScale, DigitigradeControllerBuilder.ParamFootPlant(sfx), plantTag);
        }

        private static void AddProximityReceiver(Transform parent, string name, Vector3 position, float radius, string parameter, string tag)
        {
            var host = MakeBone(name, parent, position, parent.rotation);
            var receiver = host.gameObject.AddComponent<VRCContactReceiver>();
            receiver.receiverType = VRC.Dynamics.ContactReceiver.ReceiverType.Proximity;
            receiver.parameter = parameter;
            receiver.radius = Mathf.Max(radius / Mathf.Abs(host.lossyScale.x), 0.0001f);
            receiver.collisionTags = new List<string> { tag };
            receiver.allowSelf = true;
            receiver.allowOthers = false;
            receiver.localOnly = false;
        }

        /// <summary>First descendant of the paw that looks like the toe tip.</summary>
        private static Transform FindToe(Transform foot)
        {
            if (foot == null) return null;
            for (int i = 0; i < foot.childCount; i++)
            {
                var child = foot.GetChild(i);
                if (child.name.ToLowerInvariant().Contains("toe")) return child;
            }
            return foot.childCount > 0 ? foot.GetChild(0) : null;
        }

        private static Transform MakeBone(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localScale = Vector3.one;
            t.position = position;
            t.rotation = rotation;
            return t;
        }

        private static VRCParentConstraint AddMultiParentConstraint(GameObject host, Transform[] sources, float[] weights)
        {
            var constraint = host.AddComponent<VRCParentConstraint>();
            constraint.IsActive = false;
            constraint.Locked = false;
            for (int i = 0; i < sources.Length; i++)
            {
                constraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource(sources[i], weights[i], Vector3.zero, Vector3.zero));
            }
            constraint.Locked = true;
            constraint.IsActive = true;
            return constraint;
        }

        private static VRCParentConstraint AddParentConstraint(GameObject host, Transform source)
        {
            var constraint = host.AddComponent<VRCParentConstraint>();
            constraint.IsActive = false;
            constraint.Locked = false;
            constraint.Sources.Clear();
            constraint.Sources.Add(new VRCConstraintSource(source, 1f, Vector3.zero, Vector3.zero));
            constraint.Locked = true;
            constraint.IsActive = true;
            return constraint;
        }

        private static VRCRotationConstraint AddRotationConstraint(GameObject host, Transform[] sources, float[] weights)
        {
            var constraint = host.AddComponent<VRCRotationConstraint>();
            constraint.IsActive = false;
            constraint.Locked = false;
            constraint.Sources.Clear();
            for (int i = 0; i < sources.Length; i++)
            {
                constraint.Sources.Add(new VRCConstraintSource(sources[i], weights[i], Vector3.zero, Vector3.zero));
            }
            constraint.Locked = true;
            constraint.IsActive = true;
            return constraint;
        }

        private static void ConfigureMotionConstraint(VRCRotationConstraint constraint, Transform solved)
        {
            // Scale LOCAL joint motion after the solve. Scaling world rotations would undo
            // the parent's styling and break the hinge angles; changing solver limits would
            // leave the previous fold stuck inside a newly expanded range.
            constraint.SolveInLocalSpace = true;
            var rest = constraint.transform.localRotation;
            var hinge = solved.GetComponent(FinalIkBridge.FindType(FinalIkBridge.HingeTypeName));
            if (hinge != null)
            {
                var so = new SerializedObject(hinge);
                var safeRestAngle = Mathf.Clamp(0f, so.FindProperty("min").floatValue, so.FindProperty("max").floatValue);
                rest *= Quaternion.AngleAxis(safeRestAngle, so.FindProperty("axis").vector3Value);
            }
            // Both endpoints lie on the same hinge within its anatomical limits. Blending
            // between them preserves those bounds, including a bind pose outside the range.
            constraint.RotationAtRest = rest.eulerAngles;
        }

        private static IEnumerable<Side> EnumerateSides(DigitigradeLegRigData data)
        {
            yield return new Side
            {
                suffix = ".L",
                thigh = data.leftThigh, shin = data.leftShin,
                metatarsus = data.leftMetatarsus, foot = data.leftFoot
            };
            yield return new Side
            {
                suffix = ".R",
                thigh = data.rightThigh, shin = data.rightShin,
                metatarsus = data.rightMetatarsus, foot = data.rightFoot
            };
        }
    }
}
