using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(RigidbodyLauncherData))]
    public class RigidbodyLauncherDataEditor : UnityEditor.Editor
    {
        private RigidbodyLauncherData data;
        private string previousBuildSummary = null;
        private bool previousIncludeCredits = false;

        private SerializedProperty launcherTargetProp;
        private SerializedProperty menuLocationProp;
        private SerializedProperty enableGroupingProp;
        private SerializedProperty launcherGroupIdProp;
        private SerializedProperty verboseLoggingProp;
        private SerializedProperty includeCreditsProp;

        private static readonly string WikiUrl = "https://github.com/Yeusepe/Yeusepes-Modules/wiki/Rigidbody-Launcher";

        private void OnEnable()
        {
            data = (RigidbodyLauncherData)target;

            launcherTargetProp = serializedObject.FindProperty("launcherTarget");
            menuLocationProp = serializedObject.FindProperty("menuLocation");
            enableGroupingProp = serializedObject.FindProperty("enableGrouping");
            launcherGroupIdProp = serializedObject.FindProperty("launcherGroupId");
            verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
            includeCreditsProp = serializedObject.FindProperty("includeCredits");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Rigidbody Launcher"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(RigidbodyLauncherData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(RigidbodyLauncherData));
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
            
            var targetCard = YUCPUIToolkitHelper.CreateCard("Target", "The launcher target object.");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            targetContent.Add(YUCPUIToolkitHelper.CreateField(launcherTargetProp, "Launcher Target"));
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("The launcher target will be moved outside the prefab hierarchy. Uses a configurable joint connected to a world-constrained kinematic rigidbody to launch objects.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(targetCard);
            
            var optionsCard = YUCPUIToolkitHelper.CreateCard("Options", "Configure rigidbody launcher behavior.");
            var optionsContent = YUCPUIToolkitHelper.GetCardContent(optionsCard);
            optionsContent.Add(YUCPUIToolkitHelper.CreateField(menuLocationProp, "Menu Location"));
            root.Add(optionsCard);
            
            var groupingCard = YUCPUIToolkitHelper.CreateCard("Grouping & Collaboration", "Keep multiple components in sync automatically.");
            var groupingContent = YUCPUIToolkitHelper.GetCardContent(groupingCard);
            groupingContent.Add(YUCPUIToolkitHelper.CreateField(enableGroupingProp, "Enable Grouping"));
            
            var groupIdField = YUCPUIToolkitHelper.CreateField(launcherGroupIdProp, "Group ID");
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
                
                groupIdField.SetEnabled(enableGroupingProp.boolValue);
                
                UpdateGroupingHelp(groupingHelp);
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateOverviewCard(VisualElement container, RigidbodyLauncherData data, VRCAvatarDescriptor descriptor)
        {
            container.Clear();
            
            string targetPath = descriptor != null ? UnityEditor.AnimationUtility.CalculateTransformPath(data.transform, descriptor.transform) : data.gameObject.name;
            var groupingLabel = enableGroupingProp.boolValue
                ? RigidbodyLauncherData.NormalizeGroupId(launcherGroupIdProp.stringValue)
                : "Isolated (per-object)";
            
            var overviewCard = YUCPUIToolkitHelper.CreateCard("Rigidbody Launcher Overview", null);
            var overviewContent = YUCPUIToolkitHelper.GetCardContent(overviewCard);
            
            AddInfoRow(overviewContent, "Target", targetPath);
            AddInfoRow(overviewContent, "Group", groupingLabel);
            
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Powered by VRLabs Rigidbody Launcher (MIT). Please credit VRLabs when shipping your avatar.", YUCPUIToolkitHelper.MessageType.Info));
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("This component must be placed under a VRCAvatarDescriptor in order for the builder to configure the launcher.", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (data.transform == descriptor.transform)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Attach Rigidbody Launcher to the object you want to launch, not the descriptor root.", YUCPUIToolkitHelper.MessageType.Warning));
            }
            else if (!data.transform.IsChildOf(descriptor.transform))
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Rigidbody Launcher target must be within the avatar hierarchy. Please move it inside the descriptor object.", YUCPUIToolkitHelper.MessageType.Error));
            }
        }
        
        private void UpdateGroupingHelp(VisualElement container)
        {
            container.Clear();
            var groupingInfo = enableGroupingProp.boolValue
                ? "Components with the same Group ID share one launcher setup to reduce overhead."
                : "Grouping disabled: this component will get its own launcher setup.";
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(groupingInfo, YUCPUIToolkitHelper.MessageType.Info));
        }
    }
}

