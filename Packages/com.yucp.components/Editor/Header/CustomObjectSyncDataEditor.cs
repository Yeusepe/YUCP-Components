using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(CustomObjectSyncData))]
    public class CustomObjectSyncDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (CustomObjectSyncData)target;
            var ui = YUCPInspector.Create(this, "Custom Object Sync");
            ui.Status(() => VRLabsInspector.Placement(data));

            var sync = ui.Section("Sync");
            // Quick Sync always syncs relative to the avatar; switching it on sets that in the data too (undoable).
            ui.Field(sync, "quickSync").RegisterCallback<ChangeEvent<bool>>(e =>
            {
                var frame = ui.Object.FindProperty("referenceFrame");
                if (!e.newValue || frame.enumValueIndex == (int)CustomObjectSyncData.ReferenceFrame.AvatarCentered) return;
                frame.enumValueIndex = (int)CustomObjectSyncData.ReferenceFrame.AvatarCentered;
                ui.Object.ApplyModifiedProperties();
            });
            ui.EnableWhen(ui.Segmented(sync, "referenceFrame", "Reference Frame", "Avatar", "World"), () => !data.quickSync);
            ui.ShowWhen(ui.Caption(sync, "Quick Sync always uses the avatar."), () => data.quickSync);
            ui.Field(sync, "rotationEnabled", "Sync Rotation");
            ui.Field(sync, "addDampingConstraint", "Add Damping");
            var damping = ui.Indent(sync);
            VRLabsInspector.DampingWeight(ui, damping, "dampingConstraintValue");
            ui.ShowWhen(damping, () => data.addDampingConstraint);

            var range = ui.Section("Range and precision");
            ui.Field(range, "maxRadius");
            ui.Caption(range, () => $"The object can move up to {Mathf.Pow(2f, Mathf.Clamp(data.maxRadius, 1, 12)):0} m from its anchor.");
            ui.Field(range, "positionPrecision");
            ui.EnableWhen(ui.Field(range, "rotationPrecision"), () => data.rotationEnabled);
            ui.EnableWhen(ui.Field(range, "bitCount", "Bits per Step"), () => !data.quickSync);
            var cost = ui.Group(range, "Parameter cost");
            ui.InfoRow(cost, "Synced Bits", () => $"{Cost(data).bits} of 256");
            ui.InfoRow(cost, "Update Interval", () => $"{Cost(data).interval:0.##} s");
            ui.ShowWhen(ui.InfoRow(cost, "Delay", () => $"{Cost(data).delay:0.##} s"), () => !data.quickSync);
            ui.ShowWhen(ui.InfoRow(cost, "Objects in Group", () => GroupSize(data).ToString()), () => data.enableGrouping);

            VRLabsInspector.Menu(ui, "generateMenu", "menuLocation");
            var advanced = ui.Advanced();
            VRLabsInspector.AvatarObject(ui, advanced, "syncParent");
            ui.Field(advanced, "writeDefaults");
            ui.Field(advanced, "addLocalDebugView");
            ui.Field(advanced, "showSceneGizmo");
            VRLabsInspector.Advanced(ui, "syncGroupId", "Based on VRLabs Custom Object Sync (MIT). Credit VRLabs when you share the avatar.",
                data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
            return ui.Root;
        }

        /// <summary>Synced parameter bits and timing, matching what CustomObjectSyncCreator generates.</summary>
        private static (int bits, float interval, float delay) Cost(CustomObjectSyncData data)
        {
            var s = data.ToSettings();
            var objects = GroupSize(data);
            var objectBits = Mathf.CeilToInt(Mathf.Log(objects, 2f));
            // Quick Sync: the enable bool, three position floats and sign bools, and three rotation floats.
            if (s.quickSync) return (1 + 3 * 8 + 3 + (s.rotationEnabled ? 3 * 8 : 0) + objectBits, objects * 0.2f, 0f);

            var dataBits = 3 * (s.maxRadius + s.positionPrecision) + (s.rotationEnabled ? 3 * s.rotationPrecision : 0);
            var steps = Mathf.CeilToInt(dataBits / (float)s.bitCount);
            var interval = objects * steps * 0.2f;
            var conversion = Mathf.Max(s.rotationPrecision, s.maxRadius + s.positionPrecision) * 1.5f / 60f;
            return (1 + s.bitCount + Mathf.CeilToInt(Mathf.Log(steps, 2f)) + objectBits, interval, interval + 2f * conversion);
        }

        /// <summary>How many objects share this rig: grouped components with the same group ID.</summary>
        private static int GroupSize(CustomObjectSyncData data)
        {
            var avatar = data.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (!data.enableGrouping || avatar == null) return 1;
            var id = CustomObjectSyncData.NormalizeGroupId(data.syncGroupId);
            return Mathf.Max(1, avatar.GetComponentsInChildren<CustomObjectSyncData>(true)
                .Count(m => m.enableGrouping && CustomObjectSyncData.NormalizeGroupId(m.syncGroupId) == id));
        }
    }
}
