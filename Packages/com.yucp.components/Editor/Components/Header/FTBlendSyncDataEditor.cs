using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(FTBlendSyncData))]
    public sealed class FTBlendSyncDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (FTBlendSyncData)target;
            var ui = YUCPInspector.Create(this, "FT Blendsync");

            // Re-analyze only when the component or its mesh changes; registered first so every refresher sees the new plan.
            FTBlendSyncProcessor.Plan plan = null;
            var analyzed = (-1, 0, 0);
            YUCPInspector.Message result = default;
            void Analyze()
            {
                var renderer = data.GetRenderer();
                var mesh = renderer != null ? renderer.sharedMesh : null;
                var key = (EditorUtility.GetDirtyCount(data), mesh != null ? mesh.GetInstanceID() : 0, mesh != null ? mesh.blendShapeCount : 0);
                if (plan != null && key == analyzed) return;
                analyzed = key;
                plan = FTBlendSyncProcessor.Analyze(data);
                result = default;
            }
            Analyze();
            ui.OnRefresh(Analyze);
            // Each error sits next to what it's about (HIG Feedback). Custom mappings live in the collapsed
            // Advanced Options, so their errors stay in the status area where they can't be missed.
            bool IsFace(string e) => e.StartsWith("Assign the face mesh");
            bool IsTongue(string e) => e.IndexOf("tongue", StringComparison.OrdinalIgnoreCase) >= 0;
            ui.Status(() => plan.errors.Where(e => !IsFace(e) && !IsTongue(e)).Select(YUCPInspector.Message.Error));

            var face = ui.Section("Face");
            ui.Field(face, "targetRenderer", "Face Mesh");
            ui.Messages(face, () => plan.errors.Where(IsFace).Select(YUCPInspector.Message.Error));
            ui.Field(face, "outputStandards", "Output Standards");
            var mmd = ui.Indent(face);
            ui.Field(mmd, "preserveMmdShapes", "Preserve MMD Expressions");
            ui.ShowWhen(mmd, () => (data.outputStandards & FTBlendSyncStandard.MMD) != 0);
            ui.Field(face, "allowApproximations", "Allow Approximate Mappings");

            var tongue = ui.Section("Tongue directions");
            ui.Field(tongue, "generateTongueDirections", "Generate Tongue Directions");
            var tongueSettings = ui.Indent(tongue);
            ui.Field(tongueSettings, "tongueRoot", "Tongue Root Bone");
            ui.Field(tongueSettings, "tongueSideAngle", "Side-to-Side Angle");
            ui.Field(tongueSettings, "tongueUpDownAngle", "Up / Down Angle");
            ui.ShowWhen(tongueSettings, () => data.generateTongueDirections);
            ui.Messages(tongue, () => plan.errors.Where(IsTongue).Select(YUCPInspector.Message.Error));

            var coverage = ui.Section("Shape coverage");
            ui.InfoRow(coverage, "New Shapes", () => (plan.mappings.Count + plan.tongueShapes.Count).ToString());
            ui.InfoRow(coverage, "Approximate", () => plan.approximations.Count.ToString());
            ui.InfoRow(coverage, "Unsupported", () => plan.missing.Count.ToString());
            var validate = ui.Button(coverage, "Validate Mesh", () =>
            {
                Analyze();
                Mesh generated = null;
                try
                {
                    var avatar = data.GetComponentInParent<VRCAvatarDescriptor>();
                    generated = FTBlendSyncProcessor.Generate(data, avatar != null ? avatar.transform : data.transform.root, out _);
                    result = YUCPInspector.Message.Success($"The mesh builds with {generated.blendShapeCount} shapes. The original mesh is unchanged.");
                }
                catch (Exception e)
                {
                    result = YUCPInspector.Message.Error(e.Message);
                }
                finally
                {
                    if (generated != null) DestroyImmediate(generated);
                }
                ui.Refresh();
            }, YUCPUIToolkitHelper.ButtonVariant.Primary, "Builds the adapted mesh once to check it. Nothing is saved.");
            ui.EnableWhen(validate, () => plan.errors.Count == 0);
            ui.Messages(coverage, () => new[] { result });

            var approximate = ui.Group(coverage, "Approximate mappings");
            ui.Caption(approximate, () => string.Join(", ", plan.approximations));
            ui.ShowWhen(approximate, () => plan.approximations.Count > 0);
            var unsupported = ui.Group(coverage, "Unsupported expressions");
            ui.Caption(unsupported, "Nothing on the mesh matches these; add the shapes, or map them in Advanced Options.");
            ui.Caption(unsupported, () => string.Join(", ", plan.missing));
            ui.ShowWhen(unsupported, () => plan.missing.Count > 0);

            var advanced = ui.Advanced();
            ui.Field(advanced, "faceSpace", "Face Coordinate Space");
            ui.Field(advanced, "splitFeather", "Left / Right Blend Width");
            ui.Field(advanced, "reverseLeftRight", "Reverse Left / Right");
            ui.Field(advanced, "customMappings", "Custom Shape Mappings");
            var generatedMappings = ui.Group(advanced, "Generated mappings");
            ui.Caption(generatedMappings, () => string.Join("\n", plan.mappings
                .Select(m => m.Key + " ← " + string.Join(" + ", m.Value.Select(s =>
                    $"{s.shape} × {s.weight:g}" + (s.region == FTBlendSyncRegion.Whole ? "" : " (" + s.region + ")"))))
                .Concat(plan.tongueShapes.Select(name => name + " ← tongue bone"))));
            ui.ShowWhen(generatedMappings, () => plan.mappings.Count + plan.tongueShapes.Count > 0);
            return ui.Root;
        }
    }
}
