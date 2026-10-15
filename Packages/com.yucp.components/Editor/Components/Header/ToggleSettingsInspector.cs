using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// The VRCFury toggle settings that Parameter Toggle, UV Discard Toggle and Mirrored Armature Link
    /// share: one wording, one order, one place (DESIGN.md word list). Pass the property path of each
    /// setting the component has and leave the rest null. A use* path is the checkbox that turns a
    /// setting on; its settings sit indented under it and show only while it is on.
    /// </summary>
    internal static class ToggleSettingsInspector
    {
        /// <summary>The "Avatar menu" section: where the toggle is and how it behaves.</summary>
        public static VisualElement Section(YUCPInspector ui, string title = "Avatar menu",
            string menuPath = null, string globalParameter = null, string useGlobalParameter = null,
            string saved = null, string defaultOn = null,
            string slider = null, string sliderDefault = null, string sliderPassthrough = null,
            string holdButton = null, string security = null,
            string exclusiveTags = null, string useExclusiveTags = null, string exclusiveOffState = null,
            string icon = null, string useIcon = null,
            Func<IEnumerable<YUCPInspector.Message>> messages = null)
        {
            var section = ui.Section(title);
            Field(ui, section, menuPath, "Menu Path");
            Option(ui, section, useGlobalParameter, "Global Parameter",
                (globalParameter, useGlobalParameter == null ? "Global Parameter" : "Parameter Name"));
            Field(ui, section, saved, "Saved");
            Field(ui, section, defaultOn, "On by Default");
            if (messages != null) ui.Messages(section, messages);

            var options = ui.Group(section);
            Option(ui, options, slider, "Slider", (sliderDefault, "Default Value"), (sliderPassthrough, "Passthrough at 0%"));
            Field(ui, options, holdButton, "Hold Button");
            Option(ui, options, useExclusiveTags, "Exclusive Tags",
                (exclusiveTags, useExclusiveTags == null ? "Exclusive Tags" : "Tags"), (exclusiveOffState, "Exclusive Off State"));
            Field(ui, options, security, "Protect with Security");
            Option(ui, options, useIcon, "Custom Icon", (icon, "Icon"));
            if (options.childCount == 0) options.RemoveFromHierarchy();
            return section;
        }

        /// <summary>
        /// A checkbox with its settings indented under it, shown only while it is on; returns the indent
        /// so callers can add more. Without a checkbox the settings go straight into <paramref name="parent"/>.
        /// </summary>
        public static VisualElement Option(YUCPInspector ui, VisualElement parent, string toggle, string label,
            params (string path, string label)[] settings)
        {
            var container = parent;
            if (toggle != null)
            {
                ui.Field(parent, toggle, label);
                container = ui.Indent(parent);
                ui.ShowWhen(container, () => IsOn(ui, toggle));
            }
            foreach (var (path, settingLabel) in settings) Field(ui, container, path, settingLabel);
            return container;
        }

        /// <summary>True while the checkbox is on. With several objects selected, mixed counts as on.</summary>
        public static bool IsOn(YUCPInspector ui, string path)
        {
            var property = ui.Object.FindProperty(path);
            return property != null && (property.boolValue || property.hasMultipleDifferentValues);
        }

        private static void Field(YUCPInspector ui, VisualElement parent, string path, string label)
        {
            if (path != null) ui.Field(parent, path, label);
        }
    }
}
