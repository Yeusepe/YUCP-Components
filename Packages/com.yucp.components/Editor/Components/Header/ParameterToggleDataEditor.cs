using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(ParameterToggleData))]
    [CanEditMultipleObjects]
    public class ParameterToggleDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var ui = YUCPInspector.Create(this, "Parameter Toggle");

            var conditions = ui.Section("Conditions", "The toggle is on while any group is true.");
            ui.Field(conditions, "conditionGroups", "Condition Groups");
            ui.Messages(conditions, () => targets.Cast<ParameterToggleData>().All(HasCondition)
                ? null
                : new[] { YUCPInspector.Message.Error("Add a condition with a parameter name.") });

            var action = ui.Section("Action");
            ui.Field(action, "state", "When On");
            ui.Field(action, "hasExitTime", "Run Animation to Completion");
            var transitions = ToggleSettingsInspector.Option(ui, action, "hasTransition", "Transitions");
            Transitions(ui, transitions, null);
            ui.Field(transitions, "expandIntoTransition", "Extend Into Transitions");

            ToggleSettingsInspector.Section(ui, "Toggle",
                useGlobalParameter: "useGlobalParam", globalParameter: "globalParam",
                saved: "saved", defaultOn: "defaultOn",
                slider: "slider", sliderDefault: "defaultSliderValue", sliderPassthrough: "sliderInactiveAtZero",
                holdButton: "holdButton", security: "securityEnabled",
                useExclusiveTags: "enableExclusiveTag", exclusiveTags: "exclusiveTag", exclusiveOffState: "exclusiveOffState",
                useIcon: "enableIcon", icon: "icon");

            var advanced = ui.Advanced();
            var local = ToggleSettingsInspector.Option(ui, advanced, "separateLocal", "Separate Local State", ("localState", "When On"));
            var localTransitions = new VisualElement();
            local.Add(localTransitions);
            Transitions(ui, localTransitions, "local");
            ui.ShowWhen(localTransitions, () => ToggleSettingsInspector.IsOn(ui, "hasTransition"));
            ui.Field(advanced, "invertRestLogic", "Invert Rest Logic");
            ui.Field(advanced, "paramOverride", "Parameter Override");
            var drive = ToggleSettingsInspector.Option(ui, advanced, "enableDriveGlobalParam", "Drive Global Parameter",
                ("driveGlobalParam", "Parameter Names"));
            ui.Caption(drive, "Use these only as inputs to your own transitions: not in a menu, another driver or another toggle.");
            return ui.Root;
        }

        /// <summary>Transition in and out. <paramref name="prefix"/> is null for the main state, "local" for the local one.</summary>
        private static void Transitions(YUCPInspector ui, VisualElement parent, string prefix)
        {
            string P(string name) => prefix == null ? name : prefix + char.ToUpperInvariant(name[0]) + name.Substring(1);

            var into = ui.Group(parent, "Transition in");
            ui.Field(into, P("transitionStateIn"), "Actions");
            ui.Field(into, P("transitionTimeIn"), "Duration");

            var outOf = ui.Group(parent, "Transition out");
            if (prefix == null) ui.Field(outOf, "simpleOutTransition", "Reverse of Transition In");
            // Reversed, the out transition replays the in actions backwards; its own actions are unused.
            var simple = ui.Object.FindProperty("simpleOutTransition");
            ui.ShowWhen(ui.Field(outOf, P("transitionStateOut"), "Actions"), () => !simple.boolValue || simple.hasMultipleDifferentValues);
            ui.Field(outOf, P("transitionTimeOut"), "Duration");
        }

        private static bool HasCondition(ParameterToggleData data) =>
            data.conditionGroups != null && data.conditionGroups.Any(group => group?.conditions != null
                && group.conditions.Any(condition => condition != null && !string.IsNullOrEmpty(condition.parameterName)));
    }
}
