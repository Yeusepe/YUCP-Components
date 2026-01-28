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

        private SerializedProperty appliedTransformProp;
        private SerializedProperty menuLocationProp;
        private SerializedProperty globalParameterControlProp;
        private SerializedProperty launchSpeedProp;
        private SerializedProperty maximumForceProp;
        private SerializedProperty gestureHandProp;
        private SerializedProperty launchGestureProp;
        private SerializedProperty resetGestureProp;
        private SerializedProperty collisionLayersProp;
        private SerializedProperty useGlobalParametersProp;
        private SerializedProperty parameterModeProp;
        private SerializedProperty launchParameterNameProp;
        private SerializedProperty resetParameterNameProp;
        private SerializedProperty enableGroupingProp;
        private SerializedProperty launcherGroupIdProp;
        private SerializedProperty verboseLoggingProp;
        private SerializedProperty includeCreditsProp;

        private static readonly string WikiUrl = "https://github.com/Yeusepe/Yeusepes-Modules/wiki/Rigidbody-Launcher";

        private void OnEnable()
        {
            data = (RigidbodyLauncherData)target;

            appliedTransformProp = serializedObject.FindProperty("appliedTransform");
            menuLocationProp = serializedObject.FindProperty("menuLocation");
            globalParameterControlProp = serializedObject.FindProperty("globalParameterControl");
            launchSpeedProp = serializedObject.FindProperty("launchSpeed");
            maximumForceProp = serializedObject.FindProperty("maximumForce");
            gestureHandProp = serializedObject.FindProperty("gestureHand");
            launchGestureProp = serializedObject.FindProperty("launchGesture");
            resetGestureProp = serializedObject.FindProperty("resetGesture");
            collisionLayersProp = serializedObject.FindProperty("collisionLayers");
            useGlobalParametersProp = serializedObject.FindProperty("useGlobalParameters");
            parameterModeProp = serializedObject.FindProperty("parameterMode");
            launchParameterNameProp = serializedObject.FindProperty("launchParameterName");
            resetParameterNameProp = serializedObject.FindProperty("resetParameterName");
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
            
            var targetCard = YUCPUIToolkitHelper.CreateCard("Applied Object", "Configure what gets launched.");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("This component is attached to the applied object you want to launch. That object will be moved into the Rigidbody Launcher's Container during build.", YUCPUIToolkitHelper.MessageType.Info));
            targetContent.Add(YUCPUIToolkitHelper.CreateField(appliedTransformProp, "Applied Object (Launched object)"));
            targetContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Applied object: The object that will be launched. This object will be moved outside the prefab hierarchy and connected to a configurable joint that launches it when triggered. Uses a configurable joint connected to a world-constrained kinematic rigidbody.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(targetCard);
            
            var optionsCard = YUCPUIToolkitHelper.CreateCard("Options", "Configure rigidbody launcher behavior.");
            var optionsContent = YUCPUIToolkitHelper.GetCardContent(optionsCard);
            optionsContent.Add(YUCPUIToolkitHelper.CreateField(menuLocationProp, "Menu Location"));
            optionsContent.Add(YUCPUIToolkitHelper.CreateField(globalParameterControlProp, "Global Parameter (Control)"));
            optionsContent.Add(YUCPUIToolkitHelper.CreateHelpBox("OPTIONAL: When set, this parameter will be registered as a global parameter that can be controlled by VRChat worlds or external sources. Leave empty to use local parameter only.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(optionsCard);
            
            var launchCard = YUCPUIToolkitHelper.CreateCard("Launch Settings", "Configure launch speed, force, trigger conditions, and collision.");
            var launchContent = YUCPUIToolkitHelper.GetCardContent(launchCard);
            launchContent.Add(YUCPUIToolkitHelper.CreateField(launchSpeedProp, "Launch Speed"));
            launchContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Launch speed/velocity. Negative value for forward direction. This affects the Target Velocity in the animation clip.", YUCPUIToolkitHelper.MessageType.Info));
            launchContent.Add(YUCPUIToolkitHelper.CreateField(maximumForceProp, "Maximum Force"));
            launchContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Maximum force for the configurable joint X/Y/Z drives.", YUCPUIToolkitHelper.MessageType.Info));
            
            // Global Parameters section
            var useGlobalParamsField = YUCPUIToolkitHelper.CreateField(useGlobalParametersProp, "Use Global Parameters");
            useGlobalParamsField.name = "use-global-params";
            launchContent.Add(useGlobalParamsField);
            launchContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Enable global parameter triggers instead of gestures.", YUCPUIToolkitHelper.MessageType.Info));
            
            // Global parameter fields (conditionally shown)
            var globalParamsContainer = new VisualElement();
            globalParamsContainer.name = "global-params-container";
            launchContent.Add(globalParamsContainer);
            
            var parameterModeField = YUCPUIToolkitHelper.CreateField(parameterModeProp, "Parameter Mode");
            parameterModeField.name = "parameter-mode";
            globalParamsContainer.Add(parameterModeField);
            globalParamsContainer.Add(YUCPUIToolkitHelper.CreateHelpBox("Single: One parameter (true=launch, false=reset). Dual: Separate parameters for launch and reset.", YUCPUIToolkitHelper.MessageType.Info));
            
            var launchParamField = YUCPUIToolkitHelper.CreateField(launchParameterNameProp, "Launch Parameter Name");
            launchParamField.name = "launch-param-name";
            globalParamsContainer.Add(launchParamField);
            var launchParamHelp = new VisualElement();
            launchParamHelp.name = "launch-param-help";
            globalParamsContainer.Add(launchParamHelp);
            
            var resetParamField = YUCPUIToolkitHelper.CreateField(resetParameterNameProp, "Reset Parameter Name");
            resetParamField.name = "reset-param-name";
            globalParamsContainer.Add(resetParamField);
            var resetParamHelp = new VisualElement();
            resetParamHelp.name = "reset-param-help";
            globalParamsContainer.Add(resetParamHelp);
            
            // Gesture fields (conditionally shown)
            var gestureContainer = new VisualElement();
            gestureContainer.name = "gesture-container";
            launchContent.Add(gestureContainer);
            
            var gestureHandField = YUCPUIToolkitHelper.CreateField(gestureHandProp, "Gesture Hand");
            gestureHandField.name = "gesture-hand";
            gestureContainer.Add(gestureHandField);
            gestureContainer.Add(YUCPUIToolkitHelper.CreateHelpBox("Which hand to use for gesture triggers (Left or Right).", YUCPUIToolkitHelper.MessageType.Info));
            
            var launchGestureField = YUCPUIToolkitHelper.CreateField(launchGestureProp, "Launch Gesture");
            launchGestureField.name = "launch-gesture";
            gestureContainer.Add(launchGestureField);
            gestureContainer.Add(YUCPUIToolkitHelper.CreateHelpBox("Gesture value for launching (default: 2 = HandOpen).", YUCPUIToolkitHelper.MessageType.Info));
            
            var resetGestureField = YUCPUIToolkitHelper.CreateField(resetGestureProp, "Reset Gesture");
            resetGestureField.name = "reset-gesture";
            gestureContainer.Add(resetGestureField);
            gestureContainer.Add(YUCPUIToolkitHelper.CreateHelpBox("Gesture value for resetting (default: 1 = Fist).", YUCPUIToolkitHelper.MessageType.Info));
            
            launchContent.Add(YUCPUIToolkitHelper.CreateField(collisionLayersProp, "Collision Layers"));
            launchContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Layers that the particle system will detect collisions with.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(launchCard);
            
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
            
            // Set initial visibility state
            bool initialUseGlobalParams = useGlobalParametersProp.boolValue;
            globalParamsContainer.style.display = initialUseGlobalParams ? DisplayStyle.Flex : DisplayStyle.None;
            gestureContainer.style.display = initialUseGlobalParams ? DisplayStyle.None : DisplayStyle.Flex;
            if (initialUseGlobalParams)
            {
                bool initialIsDualMode = parameterModeProp.enumValueIndex == 1;
                resetParamField.style.display = initialIsDualMode ? DisplayStyle.Flex : DisplayStyle.None;
                resetParamHelp.style.display = initialIsDualMode ? DisplayStyle.Flex : DisplayStyle.None;
            }
            
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
                
                // Update global parameter visibility
                bool useGlobalParams = useGlobalParametersProp.boolValue;
                globalParamsContainer.style.display = useGlobalParams ? DisplayStyle.Flex : DisplayStyle.None;
                gestureContainer.style.display = useGlobalParams ? DisplayStyle.None : DisplayStyle.Flex;
                
                // Update reset parameter visibility based on mode
                if (useGlobalParams)
                {
                    bool isDualMode = parameterModeProp.enumValueIndex == 1; // 0 = Single, 1 = Dual
                    resetParamField.style.display = isDualMode ? DisplayStyle.Flex : DisplayStyle.None;
                    resetParamHelp.style.display = isDualMode ? DisplayStyle.Flex : DisplayStyle.None;
                    
                    // Update help text based on mode
                    if (isDualMode)
                    {
                        launchParamHelp.Clear();
                        launchParamHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("Global parameter name for launch. This parameter triggers launch when set to true (1.0).", YUCPUIToolkitHelper.MessageType.Info));
                        resetParamHelp.Clear();
                        resetParamHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("Global parameter name for reset. This parameter triggers reset when set to true (1.0).", YUCPUIToolkitHelper.MessageType.Info));
                    }
                    else
                    {
                        launchParamHelp.Clear();
                        launchParamHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("Global parameter name. When true (1.0), triggers launch. When false (0.0), triggers reset.", YUCPUIToolkitHelper.MessageType.Info));
                    }
                }
                
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
            
            AddInfoRow(overviewContent, "Launched Object", targetPath);
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

