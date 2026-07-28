using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Builds the Rexouium-style digitigrade rig around an existing four-segment leg.
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

            // Both plantigrade passes must finish before either digitigrade pass reads them.
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
            var plantiKneeIk = MakeBone("Planti_Knee_IK" + suffix, plantiHipIk, side.shin.position, side.shin.rotation);

            // The Rex's signature fold, measured off its live rig: the first-pass solver leg is
            // SHORTER than the human leg (its IK shin runs ~53% of the human shin). Reaching for
            // the same foot, the short chain strains -- its solved thigh points more directly at
            // the ankle than the human thigh does. The digi chain roots on that thigh, so as the
            // leg lifts its span to the goal shrinks and the hock folds deeper while the visible
            // knee stays open. A full-length first pass (kneeStraightness 0) mirrors the human
            // leg exactly, which makes the hock constant and folds the knee like a human -- the
            // behavior this knob exists to avoid.
            var passShinScale = Mathf.Lerp(1f, 0.53f, Mathf.Clamp01(data.kneeStraightness));
            var passAnklePos = side.shin.position + (side.foot.position - side.shin.position) * passShinScale;
            var plantiAnkleIk = MakeBone("Planti_Ankle_IK" + suffix, plantiKneeIk, passAnklePos, side.foot.rotation);

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

            // Rex's HipAim_ForDigi_IK: in digi mode the visible thigh does not copy a rotation,
            // it AIMS at the solved knee, with roll stabilized by an up-vector object hung behind
            // the hips. Aiming guarantees the thigh points at wherever the knee actually is, so
            // knee motion reads through the whole thigh instead of being averaged away.
            var legScaleAim = Vector3.Distance(side.plantiKnee.position, side.plantiAnkle.position);
            var upVector = MakeBone("HipUpVector" + suffix, hips,
                side.plantiHip.position - hips.forward * (2.2f * legScaleAim) - hips.up * (1.3f * legScaleAim),
                hips.rotation);

            var hipAim = MakeBone("HipAim" + suffix, side.plantiHip, side.thigh.position, side.thigh.rotation);
            var aim = hipAim.gameObject.AddComponent<VRCAimConstraint>();
            var aimSo = new SerializedObject(aim);
            aimSo.FindProperty("IsActive").boolValue = true;
            aimSo.FindProperty("Locked").boolValue = true;
            aimSo.FindProperty("Sources.source0.SourceTransform").objectReferenceValue = digiKneeIk;
            aimSo.FindProperty("Sources.source0.Weight").floatValue = 1f;
            aimSo.FindProperty("AimAxis").vector3Value = AxisTowards(side.thigh, side.shin.position);
            aimSo.FindProperty("UpAxis").vector3Value = new Vector3(0f, 0f, -1f);
            aimSo.FindProperty("WorldUp").enumValueIndex = 1;    // ObjectUp, as the Rex uses
            aimSo.FindProperty("WorldUpTransform").objectReferenceValue = upVector;
            aimSo.ApplyModifiedPropertiesWithoutUndo();

            // --- goal: the real (plantigrade) ankle, position and rotation
            var goalHost = new GameObject("Goal" + suffix);
            goalHost.transform.SetParent(solverHost, false);
            goalHost.transform.position = side.plantiAnkle.position;
            goalHost.transform.rotation = side.plantiAnkle.rotation;
            AddParentConstraint(goalHost, side.plantiAnkle);

            var bendNormal = ResolveBendNormal(side, data);

            // Pass 1 normalises the plantigrade leg: same endpoints, but a clean twist-free
            // solution with a knee plane we control.
            var plantiSolver = FinalIkBridge.AddLimbIk(goalHost, plantiHipIk, plantiKneeIk, plantiAnkleIk, goalHost.transform, -bendNormal);

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
            var digiSolver = FinalIkBridge.AddLimbIk(digiHost, digiKneeIk, digiMetaIk, digiAnkleIk, digiGoal, bendNormal);

            // Rex softness: the paw is only partially flattened toward the tracked foot's
            // orientation (Rex runs 0.2); the rest of its rotation rides the hock fold. At 1.0
            // the paw locks to the foot and the whole leg reads mechanical.
            if (digiSolver != null)
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
                Quaternion.AngleAxis(-data.ankleUpAngle, bendNormal) * side.metatarsus.rotation);
            side.ankleDownNode = MakeBone("Meta_Down" + suffix, side.shin, side.metatarsus.position,
                Quaternion.AngleAxis(data.ankleDownAngle, bendNormal) * side.metatarsus.rotation);

            // --- the switchboard. source0 = plantigrade, source1 = solved digitigrade.
            side.thighConstraint = AddRotationConstraint(side.thigh.gameObject,
                new[] { side.plantiHip, hipAim }, new[] { 1f, 0f });
            side.shinConstraint = AddRotationConstraint(side.shin.gameObject,
                new[] { side.plantiKnee, digiKneeIk }, new[] { 1f, 0f });
            side.metatarsusConstraint = AddRotationConstraint(side.metatarsus.gameObject,
                new[] { side.metatarsusRestNode, digiMetaIk, side.ankleDownNode, side.ankleUpNode },
                new[] { 1f, 0f, 0f, 0f });

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

            // Rex's ankle softness, layered after the socket: a rotation constraint pulling the
            // paw toward the tracked foot. Weight 1 in plantigrade -- the paw simply IS the tracked
            // foot, as the avatar always behaved -- and pawFlattenWeight (Rex runs 0.2) in
            // digitigrade, where the paw mostly rides the fold. The FX clips animate GlobalWeight.
            side.pawFlattenConstraint = AddRotationConstraint(side.foot.gameObject,
                new[] { side.plantiAnkle }, new[] { 1f });
            side.pawFlattenConstraint.GlobalWeight = 1f;

            BuildContacts(result, side, data, bendNormal);

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
                // Straight leg: bend about the sideways axis, pointing outward from the body.
                normal = side.suffix == ".L" ? Vector3.left : Vector3.right;
            }

            return normal.normalized;
        }

        /// <summary>
        /// The Rex-style contact sensors that feed the reactive branch of the FX tree.
        ///
        /// An animator cannot read a bone's rotation, so the Rex measures it with proximity
        /// contacts: a small sender swings with the tracked ankle, and receivers parked at the
        /// positions it reaches at the up/down limit poses read the pitch out as 0..1 parameters.
        /// Foot plant is a follower pinned on the avatar's forward axis -- as the tracked foot
        /// steps fore or aft the sender walks out of the receiver, so proximity reads
        /// "how planted under the body is this foot". Geometry is derived from this avatar's leg
        /// rather than copying the Rex's hand-tuned distances; the logic is identical.
        /// </summary>
        private static void BuildContacts(Result result, Side side, DigitigradeLegRigData data, Vector3 bendNormal)
        {
            var sfx = side.suffix;
            var legScale = Vector3.Distance(side.plantiKnee.position, side.plantiAnkle.position);
            if (legScale < 1e-4f) return;

            // --- toe pitch sensor ---
            var toe = FindToe(side.foot);
            var reach = toe != null ? toe.position - side.foot.position : Vector3.zero;
            if (reach.sqrMagnitude < 1e-8f) reach = side.foot.forward * (0.4f * legScale);

            var pitchTag = "YUCPToePitch" + sfx;
            var senderHost = MakeBone("ToeSense" + sfx, side.plantiAnkle, side.plantiAnkle.position + reach, side.plantiAnkle.rotation);
            var toeSender = senderHost.gameObject.AddComponent<VRCContactSender>();
            toeSender.radius = 0.02f * legScale;
            toeSender.collisionTags = new List<string> { pitchTag };

            // Same rotations the limit-pose nodes use, so the readout saturates exactly at the poses.
            var upPos = side.plantiAnkle.position + Quaternion.AngleAxis(-data.ankleUpAngle, bendNormal) * reach;
            var downPos = side.plantiAnkle.position + Quaternion.AngleAxis(data.ankleDownAngle, bendNormal) * reach;
            var restPos = senderHost.position;

            AddProximityReceiver(side.plantiKnee, "ToeUpRec" + sfx, upPos,
                Vector3.Distance(upPos, restPos), DigitigradeControllerBuilder.ParamToeUp(sfx), pitchTag);
            AddProximityReceiver(side.plantiKnee, "ToeDownRec" + sfx, downPos,
                Vector3.Distance(downPos, restPos), DigitigradeControllerBuilder.ParamToeDown(sfx), pitchTag);

            // --- foot plant sensor ---
            var plantTag = "YUCPPlant" + sfx;
            var signal = MakeBone("PlantSignal" + sfx, side.plantiAnkle, side.plantiAnkle.position, side.plantiAnkle.rotation);
            var plantSender = signal.gameObject.AddComponent<VRCContactSender>();
            plantSender.radius = 0.04f * legScale;
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
            receiver.radius = Mathf.Max(radius, 0.02f);
            receiver.collisionTags = new List<string> { tag };
            receiver.allowSelf = true;
            receiver.allowOthers = false;
            receiver.localOnly = false;
        }

        /// <summary>The local axis of <paramref name="bone"/> that points at <paramref name="worldTarget"/>.</summary>
        private static Vector3 AxisTowards(Transform bone, Vector3 worldTarget)
        {
            var local = bone.InverseTransformDirection((worldTarget - bone.position).normalized);
            // Snap to the dominant axis so the constraint aims along the bone's actual roll axis.
            var abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            if (abs.y >= abs.x && abs.y >= abs.z) return new Vector3(0f, Mathf.Sign(local.y), 0f);
            if (abs.x >= abs.z) return new Vector3(Mathf.Sign(local.x), 0f, 0f);
            return new Vector3(0f, 0f, Mathf.Sign(local.z));
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
