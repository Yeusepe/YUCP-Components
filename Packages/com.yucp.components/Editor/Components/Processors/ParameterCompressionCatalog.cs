using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;

namespace YUCP.Components.Editor
{
    internal enum ParameterCompressionMenuRole
    {
        None,
        Toggle,
        Radial,
        Puppet,
        Button,
        SubMenuGate
    }

    internal sealed class ParameterCompressionCatalogEntry
    {
        internal VRCExpressionParameters.Parameter parameter;
        internal ParameterCompressionMenuRole menuRole;
        internal ParameterCompressionRule rule;
        internal bool explicitlyIncluded;
        internal bool allowUnverified;
        internal bool eligible;
        internal bool hardUnsafe;
        internal string reason;
        internal int minimumLevels;
        internal int desiredLevels;
        internal float minimum;
        internal float maximum;
        internal ParameterCompressionPriority priority;
        internal string group;

        internal int CurrentBits =>
            parameter.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8;
    }

    /// <summary>
    /// Reads the final merged menu, parameter asset, controller and avatar
    /// components. The catalog deliberately distinguishes "not useful to
    /// compress automatically" from "unsafe even when requested" so a creator
    /// can opt into slow puppet axes without accidentally treating a contact or
    /// momentary button as persistent state.
    /// </summary>
    internal sealed class ParameterCompressionCatalog
    {
        internal readonly List<ParameterCompressionCatalogEntry> entries =
            new List<ParameterCompressionCatalogEntry>();

        internal static ParameterCompressionCatalog Scan(
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor,
            ParameterCompressorData component)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (component == null) throw new ArgumentNullException(nameof(component));

            var catalog = new ParameterCompressionCatalog();
            var roles = ScanMenu(descriptor.expressionsMenu);
            var unsafeDriverTargets = ScanUnsafeDriverTargets(descriptor);
            var hardRuntimeInputs = ScanHardRuntimeInputs(avatarRoot);
            var animatorTypes = ScanAnimatorTypes(descriptor);
            var rules = ResolveRules(component);
            var registeredParameters = ScanRegisteredParameters(avatarRoot);
            var reservedPrefix = component.NormalizedPrefix + "/";
            var parameters = descriptor.expressionParameters?.parameters ??
                             Array.Empty<VRCExpressionParameters.Parameter>();

            foreach (var parameter in parameters)
            {
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.name) ||
                    !parameter.networkSynced)
                    continue;

                rules.TryGetValue(parameter.name, out var rule);
                roles.TryGetValue(parameter.name, out var role);
                var registered = registeredParameters.Contains(parameter.name);
                var entry = new ParameterCompressionCatalogEntry
                {
                    parameter = parameter,
                    menuRole = role,
                    rule = rule,
                    explicitlyIncluded = registered ||
                                         rule != null &&
                                         (rule.selection ==
                                          ParameterCompressionRuleSelection.Include ||
                                          rule.selection ==
                                          ParameterCompressionRuleSelection.IncludeUnverified),
                    allowUnverified = rule != null &&
                                      rule.selection ==
                                      ParameterCompressionRuleSelection.IncludeUnverified,
                    priority = rule?.priority ?? DefaultPriority(role),
                    group = ParameterCompressionContract.NormalizeGroup(rule?.group),
                    minimum = DefaultMinimum(parameter.valueType, role),
                    maximum = DefaultMaximum(parameter.valueType, role)
                };

                entry.desiredLevels = NativeLevels(parameter.valueType);
                // Automatic Float precision may yield by at most eight native
                // network codes. That tiny interval is enough to cross useful
                // radix cliffs (for example 26*255 just above 9^4) without
                // silently turning a smooth setting into a low-bit control.
                entry.minimumLevels = parameter.valueType ==
                                      VRCExpressionParameters.ValueType.Float
                    ? Math.Max(2, entry.desiredLevels - 8)
                    : entry.desiredLevels;
                ApplyRuleQuantization(entry, rule);

                if (parameter.name.StartsWith(reservedPrefix, StringComparison.Ordinal) ||
                    IsKnownTransportName(parameter.name))
                {
                    Protect(entry, true,
                        "It is already a transport/carrier parameter.");
                }
                else if (hardRuntimeInputs.Contains(parameter.name) ||
                         IsKnownLiveInputName(parameter.name))
                {
                    Protect(entry, true,
                        "Contacts, PhysBones and other live sensor outputs are not persistent state.");
                }
                else if (role == ParameterCompressionMenuRole.Button ||
                         role == ParameterCompressionMenuRole.SubMenuGate)
                {
                    Protect(entry, true,
                        "Momentary buttons and submenu gates can lose their edge when sampled.");
                }
                else if (unsafeDriverTargets.Contains(parameter.name))
                {
                    Protect(entry, true,
                        "An Animator Parameter Driver adds or randomizes this value.");
                }
                else if (animatorTypes.TryGetValue(parameter.name, out var animatorType) &&
                         !TypesAgree(parameter.valueType, animatorType))
                {
                    Protect(entry, true,
                        $"Its expression type ({parameter.valueType}) conflicts with the Animator type ({animatorType}).");
                }
                else if (rule != null &&
                         rule.selection == ParameterCompressionRuleSelection.KeepDirect)
                {
                    Protect(entry, false, "A rule keeps it directly synced.");
                }
                else if (entry.explicitlyIncluded && !registered &&
                         role == ParameterCompressionMenuRole.None &&
                         !entry.allowUnverified)
                {
                    Protect(entry, true,
                        "No persistent menu control proves this value is stable. " +
                        "Use Include Unverified only after checking every writer.");
                }
                else if (entry.explicitlyIncluded)
                {
                    entry.eligible = true;
                    entry.reason = registered
                        ? "Registered by a YUCP producer for the shared transport."
                        : role == ParameterCompressionMenuRole.Puppet
                        ? "Explicitly included; continuous puppets may look stepped at transport latency."
                        : "Explicitly included by a parameter rule.";
                }
                else if (!component.automaticSelection)
                {
                    Protect(entry, false, "Automatic selection is disabled.");
                }
                else if (role == ParameterCompressionMenuRole.Toggle ||
                         role == ParameterCompressionMenuRole.Radial)
                {
                    entry.eligible = true;
                    entry.reason = role == ParameterCompressionMenuRole.Toggle
                        ? "Persistent menu toggle."
                        : "Persistent radial setting.";
                }
                else if (role == ParameterCompressionMenuRole.Puppet)
                {
                    Protect(entry, false,
                        "Continuous puppet axes stay direct unless explicitly included.");
                }
                else
                {
                    Protect(entry, false,
                        "No persistent menu control or explicit rule proves that this is stable state.");
                }

                catalog.entries.Add(entry);
            }

            return catalog;
        }

        private static HashSet<string> ScanRegisteredParameters(
            GameObject avatarRoot)
        {
            var output = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reconstructor in avatarRoot.GetComponentsInChildren<
                         AdvancedVisemeReconstructorData>(true))
            {
                if (reconstructor == null || !reconstructor.createTuningMenu)
                    continue;
                foreach (var control in AdvancedVisemeTuning.Controls)
                    output.Add(reconstructor.TuningParameterName(control));
            }
            return output;
        }

        private static Dictionary<string, ParameterCompressionRule> ResolveRules(
            ParameterCompressorData component)
        {
            var output = new Dictionary<string, ParameterCompressionRule>(
                StringComparer.Ordinal);
            foreach (var rule in component.EnumerateRules())
            {
                if (rule == null) continue;
                var name = ParameterCompressionContract.NormalizeParameterName(
                    rule.parameterName);
                if (string.IsNullOrEmpty(name)) continue;
                output[name] = rule;
            }
            return output;
        }

        private static Dictionary<string, ParameterCompressionMenuRole> ScanMenu(
            VRCExpressionsMenu root)
        {
            var roles = new Dictionary<string, ParameterCompressionMenuRole>(
                StringComparer.Ordinal);
            var visited = new HashSet<VRCExpressionsMenu>();

            void Add(string name, ParameterCompressionMenuRole role)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                if (!roles.TryGetValue(name, out var current) ||
                    RoleRisk(role) > RoleRisk(current))
                    roles[name] = role;
            }

            void Visit(VRCExpressionsMenu menu)
            {
                if (menu == null || !visited.Add(menu) || menu.controls == null) return;
                foreach (var control in menu.controls)
                {
                    if (control == null) continue;
                    switch (control.type)
                    {
                        case VRCExpressionsMenu.Control.ControlType.Toggle:
                            Add(control.parameter?.name,
                                ParameterCompressionMenuRole.Toggle);
                            break;
                        case VRCExpressionsMenu.Control.ControlType.RadialPuppet:
                            Add(control.parameter?.name,
                                ParameterCompressionMenuRole.SubMenuGate);
                            if (control.subParameters != null &&
                                control.subParameters.Length > 0)
                                Add(control.subParameters[0]?.name,
                                    ParameterCompressionMenuRole.Radial);
                            break;
                        case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet:
                        case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet:
                            Add(control.parameter?.name,
                                ParameterCompressionMenuRole.SubMenuGate);
                            if (control.subParameters != null)
                                foreach (var sub in control.subParameters)
                                    Add(sub?.name,
                                        ParameterCompressionMenuRole.Puppet);
                            break;
                        case VRCExpressionsMenu.Control.ControlType.Button:
                            Add(control.parameter?.name,
                                ParameterCompressionMenuRole.Button);
                            break;
                        case VRCExpressionsMenu.Control.ControlType.SubMenu:
                            Add(control.parameter?.name,
                                ParameterCompressionMenuRole.SubMenuGate);
                            Visit(control.subMenu);
                            break;
                    }
                }
            }

            Visit(root);
            return roles;
        }

        private static int RoleRisk(ParameterCompressionMenuRole role)
        {
            switch (role)
            {
                case ParameterCompressionMenuRole.Button: return 6;
                case ParameterCompressionMenuRole.SubMenuGate: return 5;
                case ParameterCompressionMenuRole.Puppet: return 4;
                case ParameterCompressionMenuRole.Radial: return 3;
                case ParameterCompressionMenuRole.Toggle: return 2;
                default: return 1;
            }
        }

        private static HashSet<string> ScanUnsafeDriverTargets(
            VRCAvatarDescriptor descriptor)
        {
            var output = new HashSet<string>(StringComparer.Ordinal);
            foreach (var controller in EnumerateControllers(descriptor))
            {
                if (controller == null) continue;
                foreach (var layer in controller.layers)
                    VisitMachine(layer.stateMachine, state =>
                    {
                        foreach (var driver in state.behaviours
                                     .OfType<VRCAvatarParameterDriver>())
                        {
                            if (driver.parameters == null) continue;
                            foreach (var write in driver.parameters)
                            {
                                if (write == null || string.IsNullOrWhiteSpace(write.name))
                                    continue;
                                if (write.type ==
                                    VRC_AvatarParameterDriver.ChangeType.Add ||
                                    write.type ==
                                    VRC_AvatarParameterDriver.ChangeType.Random ||
                                    write.type ==
                                    VRC_AvatarParameterDriver.ChangeType.Copy)
                                    output.Add(write.name);
                            }
                        }
                    });
            }
            return output;
        }

        private static HashSet<string> ScanHardRuntimeInputs(GameObject avatarRoot)
        {
            var output = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receiver in avatarRoot.GetComponentsInChildren<
                         VRCContactReceiver>(true))
                if (!string.IsNullOrWhiteSpace(receiver.parameter))
                    output.Add(receiver.parameter);
            foreach (var bone in avatarRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (string.IsNullOrWhiteSpace(bone.parameter)) continue;
                output.Add(bone.parameter + "_IsGrabbed");
                output.Add(bone.parameter + "_Angle");
                output.Add(bone.parameter + "_Stretch");
                output.Add(bone.parameter + "_Squish");
                output.Add(bone.parameter + "_IsPosed");
            }
            return output;
        }

        private static Dictionary<string, AnimatorControllerParameterType>
            ScanAnimatorTypes(VRCAvatarDescriptor descriptor)
        {
            var output = new Dictionary<string, AnimatorControllerParameterType>(
                StringComparer.Ordinal);
            foreach (var controller in EnumerateControllers(descriptor))
            {
                if (controller == null) continue;
                foreach (var parameter in controller.parameters)
                {
                    if (!output.TryGetValue(parameter.name, out var old))
                        output[parameter.name] = parameter.type;
                    else if (old != parameter.type)
                        output[parameter.name] = AnimatorControllerParameterType.Trigger;
                }
            }
            return output;
        }

        private static IEnumerable<AnimatorController> EnumerateControllers(
            VRCAvatarDescriptor descriptor)
        {
            if (descriptor.baseAnimationLayers != null)
                foreach (var layer in descriptor.baseAnimationLayers)
                    if (layer.animatorController is AnimatorController controller)
                        yield return controller;
            if (descriptor.specialAnimationLayers != null)
                foreach (var layer in descriptor.specialAnimationLayers)
                    if (layer.animatorController is AnimatorController controller)
                        yield return controller;
        }

        private static void VisitMachine(
            AnimatorStateMachine machine,
            Action<AnimatorState> visitor)
        {
            if (machine == null) return;
            foreach (var state in machine.states)
                if (state.state != null) visitor(state.state);
            foreach (var child in machine.stateMachines)
                VisitMachine(child.stateMachine, visitor);
        }

        private static void ApplyRuleQuantization(
            ParameterCompressionCatalogEntry entry,
            ParameterCompressionRule rule)
        {
            if (rule == null) return;
            if (rule.range != ParameterCompressionRangeMode.Automatic)
                ParameterCompressionContract.ResolveRange(
                    rule.range, rule.minimum, rule.maximum,
                    out entry.minimum, out entry.maximum);

            var requested = ParameterCompressionContract.QuantizationLevelCount(
                rule.precision);
            if (requested <= 0) return;
            entry.desiredLevels = Math.Max(2,
                Math.Min(entry.desiredLevels, requested));
            entry.minimumLevels = entry.desiredLevels;
        }

        private static int NativeLevels(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool: return 2;
                case VRCExpressionParameters.ValueType.Int: return 256;
                default: return 255;
            }
        }

        private static float DefaultMinimum(
            VRCExpressionParameters.ValueType type,
            ParameterCompressionMenuRole role)
        {
            if (type == VRCExpressionParameters.ValueType.Float &&
                role != ParameterCompressionMenuRole.Radial)
                return -1f;
            return 0f;
        }

        private static float DefaultMaximum(
            VRCExpressionParameters.ValueType type,
            ParameterCompressionMenuRole role)
        {
            return type == VRCExpressionParameters.ValueType.Int ? 255f : 1f;
        }

        private static ParameterCompressionPriority DefaultPriority(
            ParameterCompressionMenuRole role)
        {
            switch (role)
            {
                case ParameterCompressionMenuRole.Toggle:
                    return ParameterCompressionPriority.Interactive;
                case ParameterCompressionMenuRole.Radial:
                case ParameterCompressionMenuRole.Puppet:
                    return ParameterCompressionPriority.Normal;
                default:
                    return ParameterCompressionPriority.Background;
            }
        }

        private static void Protect(
            ParameterCompressionCatalogEntry entry,
            bool hardUnsafe,
            string reason)
        {
            entry.eligible = false;
            entry.hardUnsafe = hardUnsafe;
            entry.reason = reason;
        }

        private static bool TypesAgree(
            VRCExpressionParameters.ValueType expression,
            AnimatorControllerParameterType animator)
        {
            switch (expression)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return animator == AnimatorControllerParameterType.Bool;
                case VRCExpressionParameters.ValueType.Int:
                    return animator == AnimatorControllerParameterType.Int;
                case VRCExpressionParameters.ValueType.Float:
                    return animator == AnimatorControllerParameterType.Float;
                default:
                    return false;
            }
        }

        private static bool IsKnownTransportName(string name)
        {
            return name.IndexOf("/_TuningSync/", StringComparison.Ordinal) >= 0 ||
                   name.IndexOf("SyncData", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.StartsWith("__YUCP_ParameterCompressor", StringComparison.Ordinal);
        }

        private static bool IsKnownLiveInputName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            switch (name)
            {
                case "Viseme":
                case "Voice":
                case "IsLocal":
                case "GestureLeft":
                case "GestureRight":
                case "GestureLeftWeight":
                case "GestureRightWeight":
                case "VelocityX":
                case "VelocityY":
                case "VelocityZ":
                case "AngularY":
                case "Upright":
                case "Grounded":
                case "AFK":
                    return true;
            }
            return name.StartsWith("FT/", StringComparison.OrdinalIgnoreCase) ||
                   name.IndexOf("/v2/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("EyeTracking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("LipTracking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.EndsWith("TrackingActive", StringComparison.OrdinalIgnoreCase);
        }
    }
}
