using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;
using Toolkit = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper;

namespace YUCP.Components.Editor.UI
{
    [CustomEditor(typeof(GestureManagerInputEmulator))]
    public class GestureManagerInputEmulatorEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (GestureManagerInputEmulator)target;
            var ui = YUCPInspector.Create(this, "Gesture Manager Input Emulator");

            GestureManagerInputHandler Handler() => GestureManagerInputHandler.Current;
            ui.Status(() => Application.isPlaying && Handler() == null
                ? new[] { YUCPInspector.Message.Warning("No input handler is running. Build the avatar first.") }
                : null);

            var gestureManager = ui.Section("Gesture Manager");
            ui.Field(gestureManager, "enableInputEmulation", "Emulate Input");
            ui.Field(gestureManager, "gestureManager", "Gesture Manager");
            ui.Segmented(gestureManager, "gestureManagerMode", "Mode", "Automatic", "Gesture Manager", "Animator");
            ui.Caption(gestureManager, () => data.gestureManagerMode switch
            {
                GestureManagerMode.GestureManagerMode => "Sets parameters through Gesture Manager (recommended).",
                GestureManagerMode.AnimatorMode => "Sets Animator parameters directly, for when Gesture Manager isn't available.",
                _ => "Picks the mode that suits the Gesture Manager it finds."
            });

            // The mapping editor writes straight to the mapping, so every change is recorded here for undo.
            void Change(string name, Action change)
            {
                Undo.RecordObject(data, name);
                change();
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            }

            var mappings = ui.Section("Input mappings", "Click in the Game view in Play Mode so it receives these inputs.");
            ui.Caption(mappings, () => data.inputMappings.Count == 0 ? "Add a mapping to drive a parameter from a key or controller input." : "");
            var list = new VisualElement();
            mappings.Add(list);
            string shown = null;
            string Key() => string.Join("\n", data.inputMappings.Select(m => JsonUtility.ToJson(m)));
            void Rebuild()
            {
                list.Clear();
                foreach (var mapping in data.inputMappings)
                {
                    string before = null;
                    list.Add(new YUCPInputMappingEditor(mapping, () =>
                    {
                        var after = JsonUtility.ToJson(mapping);
                        JsonUtility.FromJsonOverwrite(before, mapping);
                        Change("Edit Input Mapping", () => JsonUtility.FromJsonOverwrite(after, mapping));
                        before = after;
                        shown = Key();
                    }, () =>
                    {
                        Change("Remove Input Mapping", () => data.RemoveInputMapping(data.inputMappings.IndexOf(mapping)));
                        ui.Refresh();
                    }));
                    before = JsonUtility.ToJson(mapping);
                }
                shown = Key();
            }
            // Rebuilt only when the mappings change outside their editors (undo, Add, Clear).
            ui.OnRefresh(() => { if (Key() != shown) Rebuild(); });
            Rebuild();
            ui.Button(mappings, "Add Mapping", () =>
            {
                Change("Add Input Mapping", () => data.AddInputMapping(new InputMapping
                {
                    mappingName = $"Mapping {data.inputMappings.Count + 1}",
                    inputType = InputType.Keyboard,
                    keyboardKey = KeyCode.Space,
                    parameterName = "NewParameter",
                    parameterType = ParameterType.Bool,
                    activeValue = 1f,
                    inactiveValue = 0f,
                    enabled = true
                }));
                ui.Refresh();
            }, Toolkit.ButtonVariant.Primary);

            var controller = ui.Section("Controller");
            ui.InfoRow(controller, "Connected", () =>
            {
                var names = Input.GetJoystickNames();
                return names.Length > 0 && !string.IsNullOrEmpty(names[0]) ? names[0] : "None";
            });
            ui.Field(controller, "controllerDeadzone", "Deadzone");
            ui.Field(controller, "axisSensitivity", "Axis Sensitivity");

            var advanced = ui.Advanced();
            ui.Field(advanced, "debugMode", "Debug Overlay");
            var overlay = ui.Button(advanced, "Show Debug Overlay", () =>
            {
                var h = Handler();
                if (h == null) return;
                if (h.IsDebugOverlayVisible()) h.HideDebugOverlay();
                else h.ForceShowDebugOverlay();
                ui.Refresh();
            }, Toolkit.ButtonVariant.Secondary, "Shows or hides the input overlay in the Game view now.");
            ui.OnRefresh(() =>
            {
                var text = Handler() != null && Handler().IsDebugOverlayVisible() ? "Hide Debug Overlay" : "Show Debug Overlay";
                if (overlay.text != text) overlay.text = text;
            });
            ui.ShowWhen(overlay, () => Handler() != null);
            var clear = ui.Button(advanced, "Clear All Mappings", () =>
            {
                Change("Clear Input Mappings", data.inputMappings.Clear);
                ui.Refresh();
            }, Toolkit.ButtonVariant.Danger);
            ui.ShowWhen(clear, () => data.inputMappings.Count > 0);
            ui.BuildSummary(() => data.processedMappingsCount > 0
                ? $"{data.processedMappingsCount} mappings, {data.runtimeComponentsCreated} input handler"
                : null);
            return ui.Root;
        }
    }
}
