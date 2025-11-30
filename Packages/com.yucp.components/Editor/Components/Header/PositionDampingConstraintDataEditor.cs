using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(PositionDampingConstraintData))]
    public class PositionDampingConstraintDataEditor : UnityEditor.Editor
    {
        private PositionDampingConstraintData data;
        private string previousBuildSummary = null;
        private bool previousIncludeCredits = false;

        private SerializedProperty targetObjectProp;
        private SerializedProperty positionTargetProp;
        private SerializedProperty dampingWeightProp;
        private SerializedProperty enableGroupingProp;
        private SerializedProperty constraintGroupIdProp;
        private SerializedProperty verboseLoggingProp;
        private SerializedProperty includeCreditsProp;

        private static readonly string WikiUrl = "https://github.com/Yeusepe/Yeusepes-Modules/wiki/Position-Damping-Constraint";

        private void OnEnable()
        {
            data = (PositionDampingConstraintData)target;

            targetObjectProp = serializedObject.FindProperty("targetObject");
            positionTargetProp = serializedObject.FindProperty("positionTarget");
            dampingWeightProp = serializedObject.FindProperty("dampingWeight");
            enableGroupingProp = serializedObject.FindProperty("enableGrouping");
            constraintGroupIdProp = serializedObject.FindProperty("constraintGroupId");
            verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
            includeCreditsProp = serializedObject.FindProperty("includeCredits");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Position Damping Constraint"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(PositionDampingConstraintData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(PositionDampingConstraintData));
            if (supportBanner != null) root.Add(supportBanner);
            
            var creditBanner = new VisualElement();
            creditBanner.name = "credit-banner";
            root.Add(creditBanner);
            
            var buildSummary = new VisualElement();
            buildSummary.name = "build-summary";
            root.Add(buildSummary);
            
            var descriptorWarnings = new VisualElement();
            descriptorWarnings.name = "descriptor-warnings";
            root.Add(descriptorWarnings);
            
            var overviewCard = new VisualElement();
            overviewCard.name = "overview-card";
            root.Add(overviewCard);
            
            var targetCard = YUCPUIToolkitHelper.CreateCard("Target Objects", "Configure what gets dampened and what it dampens towards.");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("This component is attached to the object you want to dampen. That object will be moved into the Damping Constraint's Container during build.", YUCPUIToolkitHelper.MessageType.Info));
            targetContent.Add(YUCPUIToolkitHelper.CreateField(targetObjectProp, "Dampened Object"));
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("DAMPENED OBJECT: The object that will have its position dampened. This is automatically set to the GameObject this component is attached to.", YUCPUIToolkitHelper.MessageType.Info));
            targetContent.Add(YUCPUIToolkitHelper.CreateField(positionTargetProp, "Position Target"));
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("POSITION TARGET: The target position the constraint dampens towards. This object will be moved outside the prefab hierarchy. The dampened object will smoothly follow this target's position based on the damping weight. If not set, will use Dampened Object's transform.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(targetCard);
            
            var dampingCard = YUCPUIToolkitHelper.CreateCard("Damping Settings", "Control the damping strength.");
            var dampingContent = YUCPUIToolkitHelper.GetCardContent(dampingCard);
            
            var dampingSlider = new Slider("Damping Weight", 0.01f, 1f);
            dampingSlider.value = dampingWeightProp.floatValue;
            dampingSlider.AddToClassList("yucp-field-input");
            bool isSliderDragging = false;
            dampingSlider.RegisterCallback<MouseCaptureEvent>(evt =>
            {
                isSliderDragging = true;
            });
            dampingSlider.RegisterValueChangedCallback(evt =>
            {
                dampingWeightProp.floatValue = evt.newValue;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
            });
            dampingSlider.RegisterCallback<MouseCaptureOutEvent>(evt =>
            {
                isSliderDragging = false;
                dampingWeightProp.floatValue = dampingSlider.value;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
            });
            dampingContent.Add(dampingSlider);
            dampingContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Lower values create stronger damping effect. The constraint uses a feedback loop where the object targets itself at full weight and another source at this weight.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(dampingCard);

            var constraintCard = YUCPUIToolkitHelper.CreateCard("Constraint Settings", "Control offsets and constraint behavior.");
            var constraintContent = YUCPUIToolkitHelper.GetCardContent(constraintCard);
            
            constraintContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("positionOffset"), "Position Offset"));
            constraintContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Position offset from the target.", YUCPUIToolkitHelper.MessageType.Info));
            
            constraintContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("positionAtRest"), "Position At Rest"));
            constraintContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Default position when constraint weight is 0 or axis is not constrained.", YUCPUIToolkitHelper.MessageType.Info));
            
            constraintContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("sourcePositionOffset"), "Source Position Offset"));
            constraintContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Position offset applied to the constraint source.", YUCPUIToolkitHelper.MessageType.Info));
            
            root.Add(constraintCard);
            
            var groupingCard = YUCPUIToolkitHelper.CreateCard("Grouping & Collaboration", "Keep multiple components in sync automatically.");
            var groupingContent = YUCPUIToolkitHelper.GetCardContent(groupingCard);
            groupingContent.Add(YUCPUIToolkitHelper.CreateField(enableGroupingProp, "Enable Grouping"));
            
            var groupIdField = YUCPUIToolkitHelper.CreateField(constraintGroupIdProp, "Group ID");
            groupIdField.name = "group-id";
            groupingContent.Add(groupIdField);
            
            var groupingHelp = new VisualElement();
            groupingHelp.name = "grouping-help";
            groupingContent.Add(groupingHelp);
            root.Add(groupingCard);
            
            var diagnosticsCard = YUCPUIToolkitHelper.CreateCard("Diagnostics & Debug", "Surface build output and logging helpers.");
            var diagnosticsContent = YUCPUIToolkitHelper.GetCardContent(diagnosticsCard);
            diagnosticsContent.Add(YUCPUIToolkitHelper.CreateField(verboseLoggingProp, "Verbose Logging"));
            diagnosticsContent.Add(YUCPUIToolkitHelper.CreateField(includeCreditsProp, "Include Credits Banner"));
            root.Add(diagnosticsCard);
            
            YUCPUIToolkitHelper.AddSpacing(root, 6);
            var helpLinks = new VisualElement();
            helpLinks.style.flexDirection = FlexDirection.Row;
            helpLinks.style.marginBottom = 10;
            
            var docButton = YUCPUIToolkitHelper.CreateButton("Open Documentation", () => Application.OpenURL(WikiUrl), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            docButton.style.flexGrow = 1;
            docButton.style.marginRight = 5;
            helpLinks.Add(docButton);
            
            root.Add(helpLinks);
            
            previousBuildSummary = data.GetBuildSummary();
            previousIncludeCredits = includeCreditsProp.boolValue;
            
            UpdateCreditBanner(creditBanner);
            UpdateBuildSummary(buildSummary);
            UpdateDescriptorWarnings(descriptorWarnings);
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            UpdateOverviewCard(overviewCard, data, descriptor);
            UpdateGroupingHelp(groupingHelp);
            
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                bool currentIncludeCredits = includeCreditsProp.boolValue;
                if (currentIncludeCredits != previousIncludeCredits)
                {
                    UpdateCreditBanner(creditBanner);
                    previousIncludeCredits = currentIncludeCredits;
                }
                
                string currentBuildSummary = data.GetBuildSummary();
                if (currentBuildSummary != previousBuildSummary)
                {
                    UpdateBuildSummary(buildSummary);
                    previousBuildSummary = currentBuildSummary;
                }
                
                UpdateDescriptorWarnings(descriptorWarnings);
                descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
                UpdateOverviewCard(overviewCard, data, descriptor);
                
                dampingSlider.SetEnabled(true);
                if (!isSliderDragging)
                {
                    dampingSlider.value = dampingWeightProp.floatValue;
                }
                
                groupIdField.SetEnabled(enableGroupingProp.boolValue);
                
                string currentGroupId = constraintGroupIdProp.stringValue;
                bool currentEnableGrouping = enableGroupingProp.boolValue;
                UpdateGroupingHelp(groupingHelp);
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateOverviewCard(VisualElement container, PositionDampingConstraintData data, VRCAvatarDescriptor descriptor)
        {
            container.Clear();
            
            string targetPath = descriptor != null ? UnityEditor.AnimationUtility.CalculateTransformPath(data.transform, descriptor.transform) : data.gameObject.name;
            var groupingLabel = enableGroupingProp.boolValue
                ? PositionDampingConstraintData.NormalizeGroupId(constraintGroupIdProp.stringValue)
                : "Isolated (per-object)";
            
            var overviewCard = YUCPUIToolkitHelper.CreateCard("Position Damping Constraint Overview", null);
            var overviewContent = YUCPUIToolkitHelper.GetCardContent(overviewCard);
            
            AddInfoRow(overviewContent, "Dampened Object", targetPath);
            AddInfoRow(overviewContent, "Group", groupingLabel);
            AddInfoRow(overviewContent, "Damping Weight", $"{dampingWeightProp.floatValue:0.##}");
            
            container.Add(overviewCard);
        }
        
        private void AddInfoRow(VisualElement parent, string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            
            var labelElement = new Label(label);
            labelElement.style.fontSize = 10;
            labelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            labelElement.style.width = 100;
            row.Add(labelElement);
            
            var valueElement = new Label(value);
            valueElement.style.fontSize = 11;
            valueElement.style.whiteSpace = WhiteSpace.Normal;
            row.Add(valueElement);
            
            parent.Add(row);
        }
        
        private void UpdateCreditBanner(VisualElement container)
        {
            container.Clear();
            if (includeCreditsProp.boolValue)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Powered by VRLabs Damping Constraints (MIT). Please credit VRLabs when shipping your avatar.", YUCPUIToolkitHelper.MessageType.Info));
            }
        }
        
        private void UpdateBuildSummary(VisualElement container)
        {
            container.Clear();
            var summary = data.GetBuildSummary();
            if (!string.IsNullOrEmpty(summary))
            {
                var timestamp = data.GetLastBuildTimeUtc();
                string label = summary;
                if (timestamp.HasValue)
                {
                    label += $" • {timestamp.Value.ToLocalTime():g}";
                }
                container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Last build: {label}", YUCPUIToolkitHelper.MessageType.None));
            }
        }
        
        private void UpdateDescriptorWarnings(VisualElement container)
        {
            container.Clear();
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("This component must be placed under a VRCAvatarDescriptor in order for the builder to configure the constraint.", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (data.transform == descriptor.transform)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Attach Position Damping Constraint to the object you want to dampen, not the descriptor root.", YUCPUIToolkitHelper.MessageType.Warning));
            }
            else if (!data.transform.IsChildOf(descriptor.transform))
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Position Damping Constraint target must be within the avatar hierarchy. Please move it inside the descriptor object.", YUCPUIToolkitHelper.MessageType.Error));
            }
        }
        
        private void UpdateGroupingHelp(VisualElement container)
        {
            container.Clear();
            var groupingInfo = enableGroupingProp.boolValue
                ? "Components with the same Group ID share one constraint setup to reduce overhead."
                : "Grouping disabled: this component will get its own constraint setup.";
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(groupingInfo, YUCPUIToolkitHelper.MessageType.Info));
        }
    }
}

