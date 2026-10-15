using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(FootTrackerEmulator))]
    public class FootTrackerEmulatorEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (FootTrackerEmulator)target;
            var ui = YUCPInspector.Create(this, "Foot Tracker Emulator");
            ui.Status(() =>
            {
                var animator = data.GetComponent<Animator>();
                return animator != null && animator.isHuman ? null : new[] { YUCPInspector.Message.Error("Put this on a humanoid avatar.") };
            });

            var trackers = ui.Section("Trackers", "Drag the trackers in Play Mode to move the avatar's feet.");
            ui.Field(trackers, "leftTracker", "Left Tracker");
            ui.Field(trackers, "rightTracker", "Right Tracker");
            ui.Field(trackers, "autoCreateHandles", "Create Missing Trackers");

            var pinning = ui.Section("Pinning");
            ui.Field(pinning, "weight", "Weight");
            ui.Field(pinning, "applyRotation", "Pin Rotation");

            var error = ui.Section("Paw error", "Distance from each visible paw to its tracked foot. It should stay near zero.");
            ui.InfoRow(error, "Left Paw", () => $"{data.leftPawError * 1000f:F1} mm");
            ui.InfoRow(error, "Right Paw", () => $"{data.rightPawError * 1000f:F1} mm");
            ui.ShowWhen(error.parent, () => Application.isPlaying);
            return ui.Root;
        }
    }
}
