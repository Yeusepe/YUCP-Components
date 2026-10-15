using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.Editor.MeshUtils;
using YUCP.UI.DesignSystem.Utilities;
using Message = YUCP.UI.DesignSystem.Utilities.YUCPInspector.Message;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(DigitigradeLegRigData))]
    public class DigitigradeLegRigDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (DigitigradeLegRigData)target;
            var ui = YUCPInspector.Create(this, "Digitigrade Leg Rig");
            ui.Status(() => Problems(data));
            bool Limits() => data.enforceJointLimits;

            var wearer = ui.Section("Wearer defaults", "Starting values for the wearer's menu sliders. Adjust them live in Play Mode with Gesture Manager.");
            var movement = ui.ShowWhen(new VisualElement(), Limits);
            wearer.Add(movement);
            ui.Field(movement, "thighMovement");
            ui.Field(movement, "kneeMovement");
            ui.Field(movement, "hockMovement");
            ui.Caption(movement, "Lower movement can pull the paw away from tracking.");
            ui.Field(wearer, "pawFlattenWeight", "Paw Follow");
            ui.Field(wearer, "anklesWeight", "Contact Response");

            var limits = ui.Section("Joint limits");
            ui.Field(limits, "enforceJointLimits");
            var bounded = ui.ShowWhen(ui.Indent(limits), Limits);
            ui.Field(bounded, "kneeAngleLimits", "Knee Angles");
            ui.Field(bounded, "hockAngleLimits", "Hock Angles");
            ui.Messages(bounded, () => new[]
            {
                ValidLimits(data.kneeAngleLimits) && ValidLimits(data.hockAngleLimits)
                    ? default
                    : Message.Error("Keep each range between 0 and 180 degrees, with the minimum below the maximum.")
            });
            var legacy = ui.ShowWhen(ui.Indent(limits), () => !Limits());
            ui.Caption(legacy, "The leg uses the original two-pass fold, without the wearer's joint movement sliders.");
            ui.Field(legacy, "ankleUpAngle");
            ui.Field(legacy, "ankleDownAngle");
            ui.Field(legacy, "kneeStraightness");

            var menu = ui.Section("Avatar menu");
            ui.Field(menu, "menuPath", "Menu Path");
            ui.Field(menu, "menuName", "Toggle Name");
            ui.Caption(menu, () => Limits()
                ? "The wearer's sliders go in the same submenu."
                : "The submenu also gets Ankle Only Mode and Ankle Weight.");

            var advanced = ui.Advanced();
            ui.Field(advanced, "stanceAngle");
            ui.Field(advanced, "bendNormalOverride");
            var bones = ui.Group(advanced, "Leg bones");
            foreach (var side in new[] { "Left", "Right" })
                foreach (var bone in new[] { "Thigh", "Shin", "Metatarsus", "Foot" })
                    ui.Field(bones, side.ToLowerInvariant() + bone, side + " " + bone);
            ui.Caption(bones, "Empty slots use the avatar's humanoid bones.");
            var resolve = ui.ButtonRow(bones);
            ui.Button(resolve, "Auto-Resolve From Humanoid", () =>
            {
                Undo.RecordObject(data, "Auto-Resolve Leg Bones");
                data.ResolveBones();
                ui.Refresh();
            }, YUCPUIToolkitHelper.ButtonVariant.Secondary, "Fills the empty slots with the bones the build would use.");
            ui.ShowWhen(resolve, () => !data.IsComplete());
            ui.Field(advanced, "verboseLogging");
            ui.BuildSummary(data.GetBuildSummary);
            return ui.Root;
        }

        private static IEnumerable<Message> Problems(DigitigradeLegRigData data)
        {
            var animator = data.GetComponentInParent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                yield return Message.Error("Put this on a humanoid avatar.");
                yield break;
            }

            if (!FinalIkBridge.IsAvailable)
                yield return Message.Error("To build the rig, import Final IK.");
            else if (data.enforceJointLimits && !FinalIkBridge.JointLimitsAvailable)
                yield return Message.Error("Joint limits need Final IK's CCDIK and Rotation Limit Hinge. Update Final IK, or turn off Enforce Joint Limits.");

            // The bones the build will use: what ResolveBones fills in, without changing the component.
            Transform Human(Transform assigned, HumanBodyBones bone) => assigned != null ? assigned : animator.GetBoneTransform(bone);
            var legs = new[]
            {
                (thigh: Human(data.leftThigh, HumanBodyBones.LeftUpperLeg), shin: Human(data.leftShin, HumanBodyBones.LeftLowerLeg),
                    foot: Human(data.leftFoot, HumanBodyBones.LeftFoot), metatarsus: data.leftMetatarsus),
                (thigh: Human(data.rightThigh, HumanBodyBones.RightUpperLeg), shin: Human(data.rightShin, HumanBodyBones.RightLowerLeg),
                    foot: Human(data.rightFoot, HumanBodyBones.RightFoot), metatarsus: data.rightMetatarsus)
            };
            var chainMissing = false;
            var metatarsusMissing = false;
            foreach (var leg in legs)
            {
                chainMissing |= leg.thigh == null || leg.shin == null || leg.foot == null;
                metatarsusMissing |= (leg.metatarsus != null ? leg.metatarsus : DigitigradeLegRigData.FindBetween(leg.shin, leg.foot)) == null;
            }

            if (chainMissing)
                yield return Message.Error("Assign the missing leg bones in Advanced Options.");
            else if (metatarsusMissing && animator.GetComponentInChildren<DigitigradeLegSplitData>(true) == null)
                yield return Message.Error("Add Digitigrade Leg Split, or assign the metatarsus bones in Advanced Options.");
        }

        // ponytail: mirrors DigitigradeRigBuilder.ValidLimits (private there), so the build error shows here first.
        private static bool ValidLimits(Vector2 limits) => limits.x > 0f && limits.y < 180f && limits.x < limits.y;
    }
}
