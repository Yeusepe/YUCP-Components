using System;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.Components.Editor.Utils;
using YUCP.UI.DesignSystem.Utilities;
using Message = YUCP.UI.DesignSystem.Utilities.YUCPInspector.Message;
using Variant = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper.ButtonVariant;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(AvatarOptimizerPluginData))]
    public class AvatarOptimizerPluginDataEditor : UnityEditor.Editor
    {
        // A newly installed package only loads after a domain reload, which resets this, so one scan per domain is enough.
        private static readonly Lazy<bool> Installed = new Lazy<bool>(() =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("d4rkAvatarOptimizer") != null));

        // What each preset writes: Conservative, Balanced, Aggressive (bools as 0/1).
        private static readonly (string path, int conservative, int balanced, int aggressive)[] Presets =
        {
            ("useAutoSettings", 0, 0, 0),
            ("mergeSkinnedMeshes", 1, 1, 1),
            ("mergeStaticMeshesAsSkinned", 0, 1, 1),
            ("mergeSkinnedMeshesWithShaderToggle", 0, 0, 2),
            ("mergeSkinnedMeshesWithNaNimation", 0, 1, 2),
            ("naNimationAllow3BoneSkinning", 0, 0, 1),
            ("mergeSkinnedMeshesSeparatedByDefaultEnabledState", 1, 1, 0),
            ("mergeDifferentPropertyMaterials", 0, 0, 1),
            ("mergeSameDimensionTextures", 0, 0, 1),
            ("mergeMainTex", 0, 0, 1),
            ("writePropertiesAsStaticValues", 0, 0, 1),
            ("optimizeFXLayer", 1, 1, 1),
            ("combineApproximateMotionTimeAnimations", 0, 1, 1),
            ("mergeSameRatioBlendShapes", 1, 1, 1),
            ("mmdCompatibility", 1, 1, 0),
            ("disablePhysBonesWhenUnused", 1, 1, 1),
            ("deleteUnusedComponents", 1, 1, 1),
            ("deleteUnusedGameObjects", 1, 1, 2),
            ("useRingFingerAsFootCollider", 0, 0, 1),
        };

        private string installError;

        public override VisualElement CreateInspectorGUI()
        {
            var data = (AvatarOptimizerPluginData)target;
            var ui = YUCPInspector.Create(this, "Avatar Optimizer Plugin");
            var status = ui.Status(() => new[]
            {
                OnRoot(data) ? default : Message.Error("Put this on the avatar root (the object with the VRC Avatar Descriptor)."),
                Installed.Value ? default : Message.Error("To optimize this avatar, install d4rkAvatarOptimizer."),
                Message.Error(installError)
            });
            var move = ui.ButtonRow(status.parent);
            ui.Button(move, "Move to Avatar Root", () => MoveToRoot(data), Variant.Secondary,
                "Moves this component and its settings to the avatar root.");
            ui.ShowWhen(move, () => !OnRoot(data) && data.GetComponentInParent<VRCAvatarDescriptor>(true) != null);
            if (!Installed.Value)
            {
                var install = ui.ButtonRow(status.parent);
                ui.Button(install, "Install d4rkAvatarOptimizer", () => D4rkOptimizerInstaller.InstallOptimizer(
                        () => { installError = null; ui.Refresh(); },
                        error => { installError = $"Couldn't install d4rkAvatarOptimizer ({error}). Download it from the releases page instead."; ui.Refresh(); }),
                    Variant.Primary, "Adds d4rkAvatarOptimizer to the project through VPM. Unity downloads and compiles it, which takes about a minute.");
                ui.Button(install, "Open Releases Page", D4rkOptimizerInstaller.OpenGitHubReleasePage, Variant.Secondary,
                    "Opens d4rkAvatarOptimizer's GitHub releases. Download the .unitypackage and import it.");
            }

            var optimizer = ui.Section("Optimizer");
            ui.Field(optimizer, "enableOptimizer", "Optimize Avatar");
            var enabled = ui.ShowWhen(ui.Indent(optimizer), () => data.enableOptimizer);
            ui.Field(enabled, "useAutoSettings", "Automatic Settings");
            ui.Field(enabled, "excludeTransforms", "Excluded Objects");

            // Hidden while Automatic Settings is on: the optimizer overrides these then.
            var manual = ui.Section("Manual settings");
            ui.ShowWhen(manual.parent, () => data.enableOptimizer && !data.useAutoSettings);
            var presets = ui.ButtonRow(ui.Group(manual, "Presets"));
            ui.Button(presets, "Apply Conservative", () => ApplyPreset("Conservative", 0), Variant.Secondary,
                "Merges meshes and removes only what is clearly unused.");
            ui.Button(presets, "Apply Balanced", () => ApplyPreset("Balanced", 1), Variant.Secondary,
                "Also merges static meshes, uses NaNimation and combines animations.");
            ui.Button(presets, "Apply Aggressive", () => ApplyPreset("Aggressive", 2), Variant.Secondary,
                "Turns on every optimization, including ones that can break avatar features. Test the avatar afterwards.");

            var meshes = ui.Group(manual, "Meshes");
            ui.Field(meshes, "mergeSkinnedMeshes", "Merge Skinned Meshes");
            var merging = ui.ShowWhen(ui.Indent(meshes), () => data.mergeSkinnedMeshes);
            ui.Field(merging, "mergeStaticMeshesAsSkinned", "Include Static Meshes");
            ui.Segmented(merging, "mergeSkinnedMeshesWithShaderToggle", "Shader Toggles", "Off", "Basic", "Advanced");
            ui.Messages(merging, () => new[]
            {
                data.mergeSkinnedMeshesWithShaderToggle > 0 && EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64
                    ? Message.Warning("Shader toggles only work on the Windows build target. Switch to Windows, or set Shader Toggles to Off.")
                    : default
            });
            ui.Segmented(merging, "mergeSkinnedMeshesWithNaNimation", "NaNimation", "Off", "Basic", "Advanced");
            var nanimation = ui.ShowWhen(ui.Indent(merging), () => data.mergeSkinnedMeshesWithNaNimation > 0);
            ui.Field(nanimation, "naNimationAllow3BoneSkinning", "Allow 3-Bone Skinning");
            ui.Field(nanimation, "mergeSkinnedMeshesSeparatedByDefaultEnabledState", "Keep Default Visibility");

            var materials = ui.Group(manual, "Materials");
            ui.Field(materials, "mergeDifferentPropertyMaterials", "Merge Materials");
            var textures = ui.ShowWhen(ui.Indent(materials), () => data.mergeDifferentPropertyMaterials);
            ui.Field(textures, "mergeSameDimensionTextures", "Merge Textures to Arrays");
            ui.Field(ui.ShowWhen(ui.Indent(textures), () => data.mergeSameDimensionTextures), "mergeMainTex", "Include Main Textures");
            ui.Field(materials, "writePropertiesAsStaticValues", "Write Static Properties");

            var fx = ui.Group(manual, "FX layer");
            ui.Field(fx, "optimizeFXLayer", "Optimize FX Layer");
            ui.Field(ui.ShowWhen(ui.Indent(fx), () => data.optimizeFXLayer), "combineApproximateMotionTimeAnimations", "Combine Similar-Length Animations");

            var blendshapes = ui.Group(manual, "Blendshapes");
            ui.Field(blendshapes, "mergeSameRatioBlendShapes", "Merge Same-Ratio Blendshapes");
            ui.Field(blendshapes, "mmdCompatibility", "MMD Compatibility");

            var components = ui.Group(manual, "Components");
            ui.Field(components, "disablePhysBonesWhenUnused", "Disable Unused PhysBones");
            ui.Field(components, "deleteUnusedComponents", "Delete Unused Components");
            ui.Segmented(components, "deleteUnusedGameObjects", "Delete Unused Objects", "Off", "Safe", "Aggressive");
            ui.Field(components, "useRingFingerAsFootCollider", "Ring Finger as Foot Collider");

            var advanced = ui.Advanced();
            ui.Field(advanced, "debugMode", "Verbose Logging");
            ui.Field(advanced, "profileTimeUsed", "Log Step Times");
            var display = ui.Group(advanced, "Optimizer inspector");
            ui.Field(display, "showMeshMergePreview", "Show Mesh Merge Preview");
            ui.Field(display, "showFXLayerMergeResults", "Show FX Layer Results");
            ui.Field(display, "showDebugInfo", "Show Debug Info");
            ui.BuildSummary(() => data.OptimizationApplied ? $"{data.OptimizationsApplied} settings applied." : data.BuildError);
            return ui.Root;
        }

        private static bool OnRoot(AvatarOptimizerPluginData data) => data.GetComponent<VRCAvatarDescriptor>() != null;

        private static void MoveToRoot(AvatarOptimizerPluginData data)
        {
            var root = data.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (root == null) return;
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Move to Avatar Root");
            var moved = Undo.AddComponent<AvatarOptimizerPluginData>(root.gameObject);
            if (moved == null) return; // The root already has one; Unity says so in the Console.
            EditorUtility.CopySerialized(data, moved);
            Undo.DestroyObjectImmediate(data);
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = root.gameObject;
        }

        private void ApplyPreset(string name, int preset)
        {
            serializedObject.Update();
            foreach (var (path, conservative, balanced, aggressive) in Presets)
            {
                var value = preset == 0 ? conservative : preset == 1 ? balanced : aggressive;
                var property = serializedObject.FindProperty(path);
                if (property.propertyType == SerializedPropertyType.Boolean) property.boolValue = value != 0;
                else property.intValue = value;
            }
            serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName($"Apply {name} Preset");
        }
    }
}
