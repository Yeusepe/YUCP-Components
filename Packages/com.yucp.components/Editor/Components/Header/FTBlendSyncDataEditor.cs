using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(FTBlendSyncData))]
    public sealed class FTBlendSyncDataEditor : UnityEditor.Editor
    {
        private FTBlendSyncData Data => (FTBlendSyncData)target;
        private VisualElement validation, tongueSettings, mmdSettings, meshResult;
        private Label summary;
        private Foldout mappings, approximations, unsupported;
        private Button validateButton;

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("FT Blendsync"));
            var support = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(FTBlendSyncData));
            if (support != null) root.Add(support);

            validation = new VisualElement { name = "validation-messages" };
            root.Add(validation);

            var setup = AddCard(root, "Face Setup", "Adapt your face mesh for the selected expression standards.");
            setup.Add(Field("targetRenderer", "Face Renderer"));
            setup.Add(Field("outputStandards", "Output Standards"));
            setup.Add(Field("allowApproximations", "Allow Approximate Mappings"));
            setup.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Use one matching face-tracking controller. Shapes are generated when the avatar builds; your original mesh stays unchanged.",
                YUCPUIToolkitHelper.MessageType.Info));

            var tongue = AddCard(root, "Tongue Directions", "Create missing directional poses from the tongue's existing skin weights.");
            tongue.Add(Field("generateTongueDirections", "Generate Tongue Directions"));
            tongueSettings = new VisualElement { name = "tongue-settings" };
            tongueSettings.Add(Field("tongueRoot", "Tongue Root Bone"));
            tongueSettings.Add(Field("tongueSideAngle", "Side-to-Side Angle"));
            tongueSettings.Add(Field("tongueUpDownAngle", "Up / Down Angle"));
            tongueSettings.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Live directions need TongueX / TongueY tracking data. Standard iPhone ARKit supplies tongue-out only.",
                YUCPUIToolkitHelper.MessageType.Info));
            tongue.Add(tongueSettings);

            mmdSettings = YUCPUIToolkitHelper.CreateFoldout("MMD Compatibility", false);
            mmdSettings.name = "mmd-settings";
            mmdSettings.viewDataKey = "ft-blendsync-mmd";
            mmdSettings.Add(Field("preserveMmdShapes", "Preserve MMD Expressions"));
            mmdSettings.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Adds common Japanese facial expressions. Dance worlds usually expect the face mesh at avatar-root/Body. Turn face tracking off when dancing.",
                YUCPUIToolkitHelper.MessageType.Info));
            root.Add(mmdSettings);

            var advanced = YUCPUIToolkitHelper.CreateFoldout("Advanced Mapping", false);
            advanced.viewDataKey = "ft-blendsync-advanced";
            advanced.Add(Field("faceSpace", "Face Coordinate Space"));
            advanced.Add(Field("splitFeather", "Left / Right Blend Width"));
            advanced.Add(Field("reverseLeftRight", "Reverse Left / Right"));
            advanced.Add(Field("customMappings", "Custom Shape Mappings"));
            root.Add(advanced);

            var coverage = AddCard(root, "Shape Coverage", "Check the mappings before building your avatar.");
            summary = WrappedLabel(string.Empty);
            summary.name = "shape-coverage-summary";
            summary.AddToClassList("yucp-font-heading-4");
            summary.style.marginBottom = 8;
            coverage.Add(summary);
            var actions = new VisualElement { name = "coverage-actions" };
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            var analyzeButton = YUCPUIToolkitHelper.CreateButton("Analyze Coverage", RefreshReport,
                YUCPUIToolkitHelper.ButtonVariant.Secondary);
            analyzeButton.name = "analyze-coverage";
            analyzeButton.style.flexGrow = 1;
            actions.Add(analyzeButton);
            validateButton = YUCPUIToolkitHelper.CreateButton("Validate Mesh", ValidateMesh);
            validateButton.name = "validate-mesh";
            validateButton.style.flexGrow = 1;
            actions.Add(validateButton);
            coverage.Add(actions);
            meshResult = new VisualElement { name = "mesh-validation-result" };
            coverage.Add(meshResult);

            mappings = YUCPUIToolkitHelper.CreateFoldout("Generated Mappings", false);
            approximations = YUCPUIToolkitHelper.CreateFoldout("Approximate Mappings", false);
            unsupported = YUCPUIToolkitHelper.CreateFoldout("Unsupported Expressions", false);
            coverage.Add(mappings);
            coverage.Add(approximations);
            coverage.Add(unsupported);

            root.TrackSerializedObjectValue(serializedObject, _ => RefreshReport());
            RefreshReport();
            return root;
        }

        private PropertyField Field(string name, string label)
        {
            var field = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty(name), label);
            field.name = name;
            return field;
        }

        private static VisualElement AddCard(VisualElement root, string title, string subtitle)
        {
            var card = YUCPUIToolkitHelper.CreateCard(title, subtitle);

            card.Q<Label>(className: "yucp-card-subtitle").style.whiteSpace = WhiteSpace.Normal;
            root.Add(card);
            return YUCPUIToolkitHelper.GetCardContent(card);
        }

        private static Label WrappedLabel(string text)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private void RefreshReport()
        {
            if (target == null) return;
            serializedObject.UpdateIfRequiredOrScript();
            var plan = FTBlendSyncProcessor.Analyze(Data);
            tongueSettings.style.display = Data.generateTongueDirections ? DisplayStyle.Flex : DisplayStyle.None;
            mmdSettings.style.display = (Data.outputStandards & FTBlendSyncStandard.MMD) != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            validation.Clear();
            meshResult.Clear();
            foreach (var error in plan.errors)
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(error, YUCPUIToolkitHelper.MessageType.Error));
            validateButton.SetEnabled(plan.errors.Count == 0);
            summary.text = $"{plan.mappings.Count + plan.tongueShapes.Count} new shapes  ·  {plan.approximations.Count} approximate  ·  {plan.missing.Count} unsupported";

            mappings.Clear();
            mappings.text = $"Generated Mappings ({plan.mappings.Count + plan.tongueShapes.Count})";
            foreach (var item in plan.mappings)
                mappings.Add(WrappedLabel(item.Key + " ← " + string.Join(" + ", item.Value.Select(s =>
                    $"{s.shape} × {s.weight:g}" + (s.region == FTBlendSyncRegion.Whole ? "" : " (" + s.region + ")")))));
            foreach (var name in plan.tongueShapes) mappings.Add(WrappedLabel(name + " ← tongue bone approximation"));
            approximations.Clear();
            approximations.text = $"Approximate Mappings ({plan.approximations.Count})";
            approximations.style.display = plan.approximations.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var name in plan.approximations) approximations.Add(WrappedLabel(name));
            unsupported.Clear();
            unsupported.text = $"Unsupported Expressions ({plan.missing.Count})";
            unsupported.style.display = plan.missing.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (plan.missing.Count > 0)
                unsupported.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "These expressions have no matching source and will be omitted. Add authored shapes or custom mappings to support them.",
                    YUCPUIToolkitHelper.MessageType.Warning));
            foreach (var name in plan.missing) unsupported.Add(WrappedLabel(name));
        }

        private void ValidateMesh()
        {
            serializedObject.ApplyModifiedProperties();
            RefreshReport();
            Mesh generated = null;
            try
            {
                var avatar = Data.GetComponentInParent<VRCAvatarDescriptor>();
                generated = FTBlendSyncProcessor.Generate(Data, avatar != null ? avatar.transform : Data.transform.root, out _);
                meshResult.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    $"Validation passed: {generated.blendShapeCount} total shapes. Your original mesh is unchanged.",
                    YUCPUIToolkitHelper.MessageType.Success));
            }
            catch (Exception e)
            {
                meshResult.Add(YUCPUIToolkitHelper.CreateHelpBox(e.Message, YUCPUIToolkitHelper.MessageType.Error));
            }
            finally { if (generated != null) DestroyImmediate(generated); }
        }
    }
}
