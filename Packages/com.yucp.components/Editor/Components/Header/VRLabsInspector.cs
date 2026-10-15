using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// The parts every VRLabs-based component inspector shares (Follower, Rigidbody Throw/Launcher,
    /// Damping Constraints, Collision Detection, Contact Tracker, Raycast Prefab, Custom Object Sync):
    /// where the component may sit, its avatar menu settings, and its Advanced Options. One wording,
    /// one order, for all of them.
    /// </summary>
    internal static class VRLabsInspector
    {
        /// <summary>Status for a component that must live inside an avatar, on an object other than its root.</summary>
        public static IEnumerable<YUCPInspector.Message> Placement(Component component, Transform applied = null)
        {
            var descriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                yield return YUCPInspector.Message.Error("Put this inside an avatar (under a VRC Avatar Descriptor).");
                yield break;
            }
            if (component.transform == descriptor.transform)
                yield return YUCPInspector.Message.Warning("Put this on the object it controls, not on the avatar root.");
            else if (applied != null && !applied.IsChildOf(descriptor.transform))
                yield return YUCPInspector.Message.Error("The object it controls must be inside the avatar.");
        }

        /// <summary>
        /// The "Avatar menu" section: whether a control is added, where, and any global parameters
        /// worlds or other systems can drive. Pass null for parts a component doesn't have.
        /// </summary>
        public static VisualElement Menu(YUCPInspector ui, string generateMenu, string menuPath, params (string property, string label)[] globalParameters)
        {
            var section = ui.Section("Avatar menu");
            if (generateMenu != null)
            {
                ui.Field(section, generateMenu, "Add Menu Control");
                if (menuPath != null)
                {
                    var indent = ui.Indent(section);
                    ui.Field(indent, menuPath, "Menu Path");
                    ui.ShowWhen(indent, () => ui.Object.FindProperty(generateMenu).boolValue);
                }
            }
            else if (menuPath != null)
            {
                ui.Field(section, menuPath, "Menu Path");
            }
            foreach (var (property, label) in globalParameters)
                ui.Field(section, property, label);
            return section;
        }

        /// <summary>Advanced Options: grouping, logging, credits and the last build.</summary>
        public static VisualElement Advanced(YUCPInspector ui, string groupId, string credit,
            Func<string> buildSummary, Func<DateTime?> builtAt)
        {
            var advanced = ui.Advanced();
            ui.Field(advanced, "enableGrouping", "Share Setup");
            var indent = ui.Indent(advanced);
            ui.Field(indent, groupId, "Group ID");
            ui.Caption(indent, "Components with the same group ID share one setup.");
            ui.ShowWhen(indent, () => ui.Object.FindProperty("enableGrouping").boolValue);
            ui.Field(advanced, "verboseLogging");
            ui.Field(advanced, "includeCredits", "Show Credits");
            ui.ShowWhen(ui.Caption(advanced, credit), () => ui.Object.FindProperty("includeCredits").boolValue);
            ui.BuildSummary(buildSummary, builtAt);
            return advanced;
        }

        /// <summary>A transform the prefab is placed at, with an error under it when it's outside the avatar.</summary>
        public static PropertyField AvatarObject(YUCPInspector ui, VisualElement parent, string path, string label = null)
        {
            var field = ui.Field(parent, path, label);
            ui.Messages(parent, () =>
            {
                var chosen = ui.Object.FindProperty(path).objectReferenceValue as Transform;
                if (chosen == null) return null;
                var avatar = ((Component)ui.Object.targetObject).GetComponentInParent<VRCAvatarDescriptor>(true);
                return avatar != null && !chosen.IsChildOf(avatar.transform)
                    ? new[] { YUCPInspector.Message.Error("Choose an object inside the avatar.") }
                    : null;
            });
            return field;
        }

        /// <summary>The damping weight slider, named the same in every component that has one.</summary>
        public static PropertyField DampingWeight(YUCPInspector ui, VisualElement parent, string path) =>
            ui.Field(parent, path, "Damping Weight");

        /// <summary>
        /// Position and Rotation Damping Constraint: the same inspector, <paramref name="kind"/> is
        /// "Position" or "Rotation". Their Offset and At Rest fields are left out: no build step reads them.
        /// </summary>
        public static VisualElement DampingConstraint(UnityEditor.Editor editor, string kind, Func<string> buildSummary, Func<DateTime?> builtAt)
        {
            var ui = YUCPInspector.Create(editor, kind + " Damping Constraint");
            ui.Status(() => Placement((Component)editor.target));

            var damping = ui.Section("Damping");
            AvatarObject(ui, damping, kind.ToLowerInvariant() + "Target", kind + " Target");
            DampingWeight(ui, damping, "dampingWeight");
            ui.Field(damping, "source" + kind + "Offset", "Target Offset");

            AvatarObject(ui, ui.Advanced(), "appliedTransform", "Fallback Target");
            Advanced(ui, "constraintGroupId", "Based on VRLabs Damping Constraints (MIT). Credit VRLabs when you share the avatar.",
                buildSummary, builtAt);
            return ui.Root;
        }

        private static readonly List<string> Gestures = new List<string>
            { "Neutral", "Fist", "Hand Open", "Finger Point", "Victory", "Rock and Roll", "Hand Gun", "Thumbs Up" };

        /// <summary>A VRChat gesture (an int from 0 to 7), chosen by name.</summary>
        public static DropdownField Gesture(YUCPInspector ui, VisualElement parent, string path, string label)
        {
            int Index() => Mathf.Clamp(ui.Object.FindProperty(path).intValue, 0, Gestures.Count - 1);
            var field = new DropdownField(label, Gestures, Index()) { tooltip = ui.Object.FindProperty(path).tooltip };
            field.AddToClassList("yucp-field-input");
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            field.RegisterValueChangedCallback(_ =>
            {
                ui.Object.Update();
                ui.Object.FindProperty(path).intValue = field.index;
                ui.Object.ApplyModifiedProperties();
            });
            ui.OnRefresh(() =>
            {
                if (field.index != Index()) field.SetValueWithoutNotify(Gestures[Index()]);
            });
            parent.Add(field);
            return field;
        }

        /// <summary>
        /// The "Trigger" section of Rigidbody Throw and Launcher: hand gestures, or global parameters
        /// that worlds and other systems drive. <paramref name="verb"/> is "Throw" or "Launch".
        /// </summary>
        public static VisualElement Trigger(YUCPInspector ui, string verb, string gesture, string parameter)
        {
            bool Global() => ui.Object.FindProperty("useGlobalParameters").boolValue;
            bool Dual() => ui.Object.FindProperty("parameterMode").enumValueIndex == (int)ParameterMode.Dual;
            bool Empty(string path) => string.IsNullOrWhiteSpace(ui.Object.FindProperty(path).stringValue);

            var section = ui.Section("Trigger");
            ui.Segmented(section, "useGlobalParameters", "Triggered By", "Gesture", "Global Parameter");

            var gestures = new VisualElement();
            section.Add(gestures);
            ui.Segmented(gestures, "gestureHand", "Hand", "Left", "Right");
            Gesture(ui, gestures, gesture, verb + " Gesture");
            Gesture(ui, gestures, "resetGesture", "Reset Gesture");
            ui.ShowWhen(gestures, () => !Global());

            var parameters = new VisualElement();
            section.Add(parameters);
            ui.Segmented(parameters, "parameterMode", "Parameters", "One", "Two");
            ui.Field(parameters, parameter, verb + " Parameter");
            ui.ShowWhen(ui.Field(parameters, "resetParameterName", "Reset Parameter"), Dual);
            ui.Messages(parameters, () => new[]
            {
                YUCPInspector.Message.Error(Empty(parameter) ? $"Enter a {verb.ToLowerInvariant()} parameter name." : null),
                YUCPInspector.Message.Error(Dual() && Empty("resetParameterName") ? "Enter a reset parameter name." : null)
            });
            ui.ShowWhen(parameters, Global);
            return section;
        }
    }
}
