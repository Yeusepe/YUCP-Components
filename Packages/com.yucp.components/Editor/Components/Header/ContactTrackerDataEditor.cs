using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(ContactTrackerData))]
    public class ContactTrackerDataEditor : UnityEditor.Editor
    {
        private ContactTrackerData data;
        private string previousBuildSummary = null;
        private bool previousIncludeCredits = false;

        private SerializedProperty trackerTargetProp;
        private SerializedProperty menuLocationProp;
        private SerializedProperty globalParameterControlProp;
        private SerializedProperty collisionTagsProp;
        private SerializedProperty sizeParameterProp;
        private SerializedProperty enableGroupingProp;
        private SerializedProperty trackerGroupIdProp;
        private SerializedProperty verboseLoggingProp;
        private SerializedProperty includeCreditsProp;

        private static readonly string WikiUrl = "https://github.com/Yeusepe/Yeusepes-Modules/wiki/Contact-Tracker";

        private void OnEnable()
        {
            data = (ContactTrackerData)target;

            trackerTargetProp = serializedObject.FindProperty("trackerTarget");
            menuLocationProp = serializedObject.FindProperty("menuLocation");
            globalParameterControlProp = serializedObject.FindProperty("globalParameterControl");
            collisionTagsProp = serializedObject.FindProperty("collisionTags");
            sizeParameterProp = serializedObject.FindProperty("sizeParameter");
            enableGroupingProp = serializedObject.FindProperty("enableGrouping");
            trackerGroupIdProp = serializedObject.FindProperty("trackerGroupId");
            verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
            includeCreditsProp = serializedObject.FindProperty("includeCredits");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Contact Tracker"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(ContactTrackerData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(ContactTrackerData));
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
            
            var targetCard = YUCPUIToolkitHelper.CreateCard("Target Objects", "Configure what gets tracked and what follows the tracking.");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("This component is attached to the object you want to track contacts for. That object will be moved into the Contact Tracker's Container during build.", YUCPUIToolkitHelper.MessageType.Info));
            targetContent.Add(YUCPUIToolkitHelper.CreateField(trackerTargetProp, "Tracker Target"));
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("TRACKER TARGET: The object that will be moved outside the prefab and positioned based on contact detection. This is the visual/functional object that follows the tracked contacts (e.g., a hand tracker that follows hand contacts). It will be centered on tracked objects using six proximity contacts (X+, X-, Y+, Y-, Z+, Z-).", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(targetCard);
            
            var optionsCard = YUCPUIToolkitHelper.CreateCard("Options", "Configure contact tracker behavior.");
            var optionsContent = YUCPUIToolkitHelper.GetCardContent(optionsCard);
            optionsContent.Add(YUCPUIToolkitHelper.CreateField(menuLocationProp, "Menu Location"));
            optionsContent.Add(YUCPUIToolkitHelper.CreateField(globalParameterControlProp, "Global Parameter (Control)"));
            optionsContent.Add(YUCPUIToolkitHelper.CreateHelpBox("OPTIONAL: When set, this parameter will be registered as a global parameter that can be controlled by VRChat worlds or external sources. Leave empty to use local parameter only.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(optionsCard);
            
            var contactCard = YUCPUIToolkitHelper.CreateCard("Contact Settings", "Configure proximity contact collision tags and size.");
            var contactContent = YUCPUIToolkitHelper.GetCardContent(contactCard);
            contactContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Collision tags for the 6 proximity contacts. Order: X+, X-, Y+, Y-, Z+, Z-", YUCPUIToolkitHelper.MessageType.Info));
            contactContent.Add(new PropertyField(collisionTagsProp, "Collision Tags"));
            contactContent.Add(YUCPUIToolkitHelper.CreateField(sizeParameterProp, "Size Parameter"));
            contactContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Size parameter value for ContactTracker/Size. This sets the default size when not tracking (0-1).", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(contactCard);
            
            var groupingCard = YUCPUIToolkitHelper.CreateCard("Grouping & Collaboration", "Keep multiple components in sync automatically.");
            var groupingContent = YUCPUIToolkitHelper.GetCardContent(groupingCard);
            groupingContent.Add(YUCPUIToolkitHelper.CreateField(enableGroupingProp, "Enable Grouping"));
            
            var groupIdField = YUCPUIToolkitHelper.CreateField(trackerGroupIdProp, "Group ID");
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
        
        private void UpdateOverviewCard(VisualElement container, ContactTrackerData data, VRCAvatarDescriptor descriptor)
        {
            container.Clear();
            
            string targetPath = descriptor != null ? UnityEditor.AnimationUtility.CalculateTransformPath(data.transform, descriptor.transform) : data.gameObject.name;
            var groupingLabel = enableGroupingProp.boolValue
                ? ContactTrackerData.NormalizeGroupId(trackerGroupIdProp.stringValue)
                : "Isolated (per-object)";
            
            var overviewCard = YUCPUIToolkitHelper.CreateCard("Contact Tracker Overview", null);
            var overviewContent = YUCPUIToolkitHelper.GetCardContent(overviewCard);
            
            AddInfoRow(overviewContent, "Tracked Object", targetPath);
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Powered by VRLabs Contact Tracker (MIT). Please credit VRLabs when shipping your avatar.", YUCPUIToolkitHelper.MessageType.Info));
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("This component must be placed under a VRCAvatarDescriptor in order for the builder to configure contact tracking.", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (data.transform == descriptor.transform)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Attach Contact Tracker to the object you want to track contacts for, not the descriptor root.", YUCPUIToolkitHelper.MessageType.Warning));
            }
            else if (!data.transform.IsChildOf(descriptor.transform))
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Contact Tracker target must be within the avatar hierarchy. Please move it inside the descriptor object.", YUCPUIToolkitHelper.MessageType.Error));
            }
        }
        
        private void UpdateGroupingHelp(VisualElement container)
        {
            container.Clear();
            var groupingInfo = enableGroupingProp.boolValue
                ? "Components with the same Group ID share one contact tracker setup to reduce overhead."
                : "Grouping disabled: this component will get its own contact tracker setup.";
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(groupingInfo, YUCPUIToolkitHelper.MessageType.Info));
        }
    }
}

