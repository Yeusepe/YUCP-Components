using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(RigidbodyThrowData))]
    public class RigidbodyThrowDataEditor : UnityEditor.Editor
    {
        private RigidbodyThrowData data;
        private string previousBuildSummary = null;
        private bool previousIncludeCredits = false;

        private SerializedProperty throwTargetProp;
        private SerializedProperty enableRotationSyncProp;
        private SerializedProperty menuLocationProp;
        private SerializedProperty physicsMaterialProp;
        private SerializedProperty throwGestureProp;
        private SerializedProperty resetGestureProp;
        private SerializedProperty collisionLayersProp;
        private SerializedProperty enableGroupingProp;
        private SerializedProperty throwGroupIdProp;
        private SerializedProperty verboseLoggingProp;
        private SerializedProperty includeCreditsProp;

        private static readonly string WikiUrl = "https://github.com/Yeusepe/Yeusepes-Modules/wiki/Rigidbody-Throw";

        private void OnEnable()
        {
            data = (RigidbodyThrowData)target;

            throwTargetProp = serializedObject.FindProperty("throwTarget");
            enableRotationSyncProp = serializedObject.FindProperty("enableRotationSync");
            menuLocationProp = serializedObject.FindProperty("menuLocation");
            physicsMaterialProp = serializedObject.FindProperty("physicsMaterial");
            throwGestureProp = serializedObject.FindProperty("throwGesture");
            resetGestureProp = serializedObject.FindProperty("resetGesture");
            collisionLayersProp = serializedObject.FindProperty("collisionLayers");
            enableGroupingProp = serializedObject.FindProperty("enableGrouping");
            throwGroupIdProp = serializedObject.FindProperty("throwGroupId");
            verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
            includeCreditsProp = serializedObject.FindProperty("includeCredits");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Rigidbody Throw"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(RigidbodyThrowData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(RigidbodyThrowData));
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
            
            var targetCard = YUCPUIToolkitHelper.CreateCard("Target Objects", "Configure what gets thrown.");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("This component is attached to the object you want to throw. That object will be moved into the Rigidbody Throw's Container during build.", YUCPUIToolkitHelper.MessageType.Info));
            targetContent.Add(YUCPUIToolkitHelper.CreateField(throwTargetProp, "Throw Target"));
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("THROW TARGET: The object that will be thrown. This object will be moved outside the prefab hierarchy and will be thrown when the gesture condition is met. Uses a particle system to apply force and contacts/constraints to sync position (and optionally rotation) remotely across clients.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(targetCard);
            
            var optionsCard = YUCPUIToolkitHelper.CreateCard("Options", "Configure rigidbody throw behavior.");
            var optionsContent = YUCPUIToolkitHelper.GetCardContent(optionsCard);
            optionsContent.Add(YUCPUIToolkitHelper.CreateField(enableRotationSyncProp, "Enable Rotation Sync"));
            optionsContent.Add(YUCPUIToolkitHelper.CreateHelpBox("When enabled, rotation will be synced alongside position. This requires additional expression parameters (27 without rotation, 51 with rotation).", YUCPUIToolkitHelper.MessageType.Info));
            optionsContent.Add(YUCPUIToolkitHelper.CreateField(menuLocationProp, "Menu Location"));
            root.Add(optionsCard);
            
            var throwCard = YUCPUIToolkitHelper.CreateCard("Throw Settings", "Configure physics material, gesture conditions, and collision.");
            var throwContent = YUCPUIToolkitHelper.GetCardContent(throwCard);
            throwContent.Add(YUCPUIToolkitHelper.CreateField(physicsMaterialProp, "Physics Material"));
            throwContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Physics material for collision. Applied to the Collision Collider.", YUCPUIToolkitHelper.MessageType.Info));
            throwContent.Add(YUCPUIToolkitHelper.CreateField(throwGestureProp, "Throw Gesture"));
            throwContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Gesture value for throwing (default: 2 = HandOpen).", YUCPUIToolkitHelper.MessageType.Info));
            throwContent.Add(YUCPUIToolkitHelper.CreateField(resetGestureProp, "Reset Gesture"));
            throwContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Gesture value for resetting (default: 1 = Fist).", YUCPUIToolkitHelper.MessageType.Info));
            throwContent.Add(YUCPUIToolkitHelper.CreateField(collisionLayersProp, "Collision Layers"));
            throwContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Layers that collision detection will use.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(throwCard);
            
            var groupingCard = YUCPUIToolkitHelper.CreateCard("Grouping & Collaboration", "Keep multiple components in sync automatically.");
            var groupingContent = YUCPUIToolkitHelper.GetCardContent(groupingCard);
            groupingContent.Add(YUCPUIToolkitHelper.CreateField(enableGroupingProp, "Enable Grouping"));
            
            var groupIdField = YUCPUIToolkitHelper.CreateField(throwGroupIdProp, "Group ID");
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
        
        private void UpdateOverviewCard(VisualElement container, RigidbodyThrowData data, VRCAvatarDescriptor descriptor)
        {
            container.Clear();
            
            string targetPath = descriptor != null ? UnityEditor.AnimationUtility.CalculateTransformPath(data.transform, descriptor.transform) : data.gameObject.name;
            var groupingLabel = enableGroupingProp.boolValue
                ? RigidbodyThrowData.NormalizeGroupId(throwGroupIdProp.stringValue)
                : "Isolated (per-object)";
            
            var overviewCard = YUCPUIToolkitHelper.CreateCard("Rigidbody Throw Overview", null);
            var overviewContent = YUCPUIToolkitHelper.GetCardContent(overviewCard);
            
            AddInfoRow(overviewContent, "Thrown Object", targetPath);
            AddInfoRow(overviewContent, "Group", groupingLabel);
            AddInfoRow(overviewContent, "Rotation Sync", enableRotationSyncProp.boolValue ? "Enabled" : "Disabled");
            
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Powered by VRLabs Rigidbody Throw (MIT). Please credit VRLabs when shipping your avatar.", YUCPUIToolkitHelper.MessageType.Info));
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("This component must be placed under a VRCAvatarDescriptor in order for the builder to configure the throw system.", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (data.transform == descriptor.transform)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Attach Rigidbody Throw to the object you want to throw, not the descriptor root.", YUCPUIToolkitHelper.MessageType.Warning));
            }
            else if (!data.transform.IsChildOf(descriptor.transform))
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Rigidbody Throw target must be within the avatar hierarchy. Please move it inside the descriptor object.", YUCPUIToolkitHelper.MessageType.Error));
            }
        }
        
        private void UpdateGroupingHelp(VisualElement container)
        {
            container.Clear();
            var groupingInfo = enableGroupingProp.boolValue
                ? "Components with the same Group ID share one throw setup to reduce overhead."
                : "Grouping disabled: this component will get its own throw setup.";
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(groupingInfo, YUCPUIToolkitHelper.MessageType.Info));
        }
    }
}

