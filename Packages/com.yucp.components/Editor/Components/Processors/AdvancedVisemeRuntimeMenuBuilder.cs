using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Builds the optional local tuning menu as a deterministic asset tree.
    /// The caller remains responsible for merging the returned menu and its
    /// matching expression parameters through VRCFury's public API.
    /// </summary>
    internal static class AdvancedVisemeRuntimeMenuBuilder
    {
        internal const int MaxControlsPerMenu = 8;

        private static readonly AdvancedVisemeTuningMenuSections[] OrderedSections =
        {
            AdvancedVisemeTuningMenuSections.Speech,
            AdvancedVisemeTuningMenuSections.Tracking,
            AdvancedVisemeTuningMenuSections.Phonetics,
            AdvancedVisemeTuningMenuSections.Tongue
        };

        /// <summary>
        /// Creates or replaces a menu asset. Controls are ordered by the stable
        /// tuning catalog rather than by dictionary enumeration order.
        /// </summary>
        public static VRCExpressionsMenu Build(
            string assetPath,
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> controls)
        {
            ValidateAssetPath(assetPath);
            ValidateControlGroups(controls);
            assetPath = assetPath.Replace('\\', '/');

            var grouped = GroupControls(controls);
            var root = NewMenu(Path.GetFileNameWithoutExtension(assetPath));
            var submenus = new List<VRCExpressionsMenu>();

            foreach (var section in OrderedSections)
            {
                var sectionControls = grouped[section];
                if (sectionControls.Count == 0) continue;

                var label = AdvancedVisemeTuning.SectionLabel(section);
                var submenu = NewMenu(label);
                foreach (var control in sectionControls)
                {
                    submenu.controls.Add(NewRadialControl(
                        AdvancedVisemeTuning.Label(control),
                        controls[control]));
                }

                root.controls.Add(NewSubmenuControl(label, submenu));
                submenus.Add(submenu);
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null &&
                !AssetDatabase.DeleteAsset(assetPath))
                throw new InvalidOperationException(
                    $"Could not replace the existing advanced-viseme menu asset at '{assetPath}'.");

            AssetDatabase.CreateAsset(root, assetPath);
            foreach (var submenu in submenus)
                AssetDatabase.AddObjectToAsset(submenu, root);

            EditorUtility.SetDirty(root);
            foreach (var submenu in submenus) EditorUtility.SetDirty(submenu);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            return AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(assetPath) ?? root;
        }

        internal static IReadOnlyDictionary<AdvancedVisemeTuningMenuSections, int>
            CountControlsBySection(
                IReadOnlyDictionary<AdvancedVisemeTuningControl, string> controls)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));

            var counts = OrderedSections.ToDictionary(section => section, _ => 0);
            foreach (var control in controls.Keys)
            {
                ValidateKnownControl(control);
                counts[AdvancedVisemeTuning.Section(control)]++;
            }

            return counts;
        }

        internal static void ValidateControlGroups(
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> controls)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));

            foreach (var pair in controls)
            {
                ValidateKnownControl(pair.Key);
                if (string.IsNullOrWhiteSpace(pair.Value))
                    throw new ArgumentException(
                        $"Tuning control '{pair.Key}' has no Animator parameter name.",
                        nameof(controls));
            }

            var counts = CountControlsBySection(controls);
            foreach (var section in OrderedSections)
            {
                if (counts[section] <= MaxControlsPerMenu) continue;
                throw new InvalidOperationException(
                    $"Advanced-viseme tuning section '{AdvancedVisemeTuning.SectionLabel(section)}' " +
                    $"contains {counts[section]} controls; VRChat menus allow at most " +
                    $"{MaxControlsPerMenu} controls per submenu.");
            }

            var rootControlCount = counts.Count(pair => pair.Value > 0);
            if (rootControlCount > MaxControlsPerMenu)
                throw new InvalidOperationException(
                    $"The advanced-viseme tuning root contains {rootControlCount} submenus; " +
                    $"VRChat menus allow at most {MaxControlsPerMenu} controls.");
        }

        internal static bool TryValidateControlGroups(
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> controls,
            out string error)
        {
            try
            {
                ValidateControlGroups(controls);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static IReadOnlyList<AdvancedVisemeTuningControl> OrderedControlsForSection(
            AdvancedVisemeTuningMenuSections section,
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> controls)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            if (!OrderedSections.Contains(section))
                throw new ArgumentOutOfRangeException(nameof(section), section,
                    "Expected one semantic advanced-viseme tuning section.");

            foreach (var control in controls.Keys) ValidateKnownControl(control);
            return AdvancedVisemeTuning.Controls
                .Where(control => controls.ContainsKey(control) &&
                                  AdvancedVisemeTuning.Section(control) == section)
                .ToArray();
        }

        private static Dictionary<AdvancedVisemeTuningMenuSections,
            IReadOnlyList<AdvancedVisemeTuningControl>> GroupControls(
                IReadOnlyDictionary<AdvancedVisemeTuningControl, string> controls)
        {
            return OrderedSections.ToDictionary(
                section => section,
                section => OrderedControlsForSection(section, controls));
        }

        private static VRCExpressionsMenu NewMenu(string name)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = string.IsNullOrWhiteSpace(name) ? "Advanced Viseme Tuning" : name;
            menu.controls = new List<VRCExpressionsMenu.Control>();
            return menu;
        }

        private static VRCExpressionsMenu.Control NewSubmenuControl(
            string label,
            VRCExpressionsMenu submenu)
        {
            return new VRCExpressionsMenu.Control
            {
                name = label,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                parameter = EmptyParameter(),
                subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                subMenu = submenu
            };
        }

        private static VRCExpressionsMenu.Control NewRadialControl(
            string label,
            string parameterName)
        {
            return new VRCExpressionsMenu.Control
            {
                name = label,
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                parameter = EmptyParameter(),
                subParameters = new[]
                {
                    new VRCExpressionsMenu.Control.Parameter { name = parameterName }
                }
            };
        }

        private static VRCExpressionsMenu.Control.Parameter EmptyParameter()
        {
            return new VRCExpressionsMenu.Control.Parameter { name = string.Empty };
        }

        private static void ValidateKnownControl(AdvancedVisemeTuningControl control)
        {
            if (!Enum.IsDefined(typeof(AdvancedVisemeTuningControl), control))
                throw new ArgumentOutOfRangeException(nameof(control), control,
                    "Unknown advanced-viseme tuning control.");
        }

        private static void ValidateAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("A menu asset path is required.", nameof(assetPath));
            if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The menu path must end in '.asset'.", nameof(assetPath));

            var normalized = assetPath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) &&
                !normalized.StartsWith("Packages/", StringComparison.Ordinal))
                throw new ArgumentException(
                    "The menu path must be a Unity project path under Assets or Packages.",
                    nameof(assetPath));

            var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory) || !AssetDatabase.IsValidFolder(directory))
                throw new ArgumentException(
                    $"The menu asset directory '{directory}' does not exist.",
                    nameof(assetPath));
        }
    }
}
