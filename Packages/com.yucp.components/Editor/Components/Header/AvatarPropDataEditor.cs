using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;
using YUCP.Components.Editor.Utils;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(AvatarPropData))]
    public class AvatarPropDataEditor : UnityEditor.Editor
    {
        private AvatarPropData data;
        private string previousBuildSummary = null;
        private bool previousIncludeCredits = false;
        private static bool? isFinalIKInstalled = null;
        private bool notInstalledUIPopulated = false;

        private SerializedProperty customPropProp;
        private SerializedProperty menuLocationProp;
        private SerializedProperty toggleNameProp;
        private SerializedProperty defaultOnProp;
        private SerializedProperty savedProp;
        private SerializedProperty includeCreditsProp;
        private SerializedProperty verboseLoggingProp;

        private static readonly string WikiUrl = "https://github.com/Yeusepe/Yeusepes-Modules/wiki/Avatar-Prop";

        private void OnEnable()
        {
            data = (AvatarPropData)target;

            customPropProp = serializedObject.FindProperty("customProp");
            menuLocationProp = serializedObject.FindProperty("menuLocation");
            toggleNameProp = serializedObject.FindProperty("toggleName");
            defaultOnProp = serializedObject.FindProperty("defaultOn");
            savedProp = serializedObject.FindProperty("saved");
            includeCreditsProp = serializedObject.FindProperty("includeCredits");
            verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
            
            // Check for Final IK installation once
            if (!isFinalIKInstalled.HasValue)
            {
                CheckForFinalIK();
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Avatar Prop"));
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(AvatarPropData));
            if (supportBanner != null) root.Add(supportBanner);
            
            // Not installed UI (conditional)
            var notInstalledUI = new VisualElement();
            notInstalledUI.name = "not-installed-ui";
            root.Add(notInstalledUI);
            
            // Installed banner (conditional)
            var installedBanner = new VisualElement();
            installedBanner.name = "installed-banner";
            root.Add(installedBanner);
            
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
            
            // Custom Prop Card
            var propCard = YUCPUIToolkitHelper.CreateCard("Custom Prop", "Replace the default sword with your own prop.");
            var propContent = YUCPUIToolkitHelper.GetCardContent(propCard);
            propContent.Add(YUCPUIToolkitHelper.CreateField(customPropProp, "Custom Prop"));
            propContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Optional: Assign a GameObject to use as your custom prop. Leave empty to use the default sword.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(propCard);
            
            // Toggle Settings Card
            var toggleCard = YUCPUIToolkitHelper.CreateCard("Toggle Settings", "Configure the expressions menu toggle.");
            var toggleContent = YUCPUIToolkitHelper.GetCardContent(toggleCard);
            toggleContent.Add(YUCPUIToolkitHelper.CreateField(menuLocationProp, "Menu Location"));
            toggleContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Menu path like 'Props/Weapons'. Leave empty for root menu.", YUCPUIToolkitHelper.MessageType.Info));
            toggleContent.Add(YUCPUIToolkitHelper.CreateField(toggleNameProp, "Toggle Name"));
            toggleContent.Add(YUCPUIToolkitHelper.CreateField(defaultOnProp, "Default On"));
            toggleContent.Add(YUCPUIToolkitHelper.CreateField(savedProp, "Saved"));
            root.Add(toggleCard);
            
            // Diagnostics Card
            var diagnosticsCard = YUCPUIToolkitHelper.CreateCard("Diagnostics", "Debug and credit settings.");
            var diagnosticsContent = YUCPUIToolkitHelper.GetCardContent(diagnosticsCard);
            diagnosticsContent.Add(YUCPUIToolkitHelper.CreateField(verboseLoggingProp, "Verbose Logging"));
            diagnosticsContent.Add(YUCPUIToolkitHelper.CreateField(includeCreditsProp, "Include Credits"));
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
            
            // Initial population
            if (isFinalIKInstalled == false)
            {
                UpdateNotInstalledUI(notInstalledUI);
                propCard.style.display = DisplayStyle.None;
                toggleCard.style.display = DisplayStyle.None;
                diagnosticsCard.style.display = DisplayStyle.None;
                helpLinks.style.display = DisplayStyle.None;
                installedBanner.style.display = DisplayStyle.None;
            }
            else
            {
                notInstalledUI.style.display = DisplayStyle.None;
                installedBanner.style.display = DisplayStyle.None; // Hide banner when installed
                UpdateCreditBanner(creditBanner);
                UpdateBuildSummary(buildSummary);
                UpdateDescriptorWarnings(descriptorWarnings);
                var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
                UpdateOverviewCard(overviewCard, data, descriptor);
            }
            
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                // Re-check installation status periodically (in case package was just installed)
                bool currentInstalled = FinalIKStubInstaller.IsInstalled();
                bool? previousInstalled = isFinalIKInstalled;
                
                if (isFinalIKInstalled != currentInstalled)
                {
                    isFinalIKInstalled = currentInstalled;
                    if (currentInstalled)
                    {
                        Debug.Log("[AvatarProp Editor] Final IK Stub detected!");
                    }
                    else
                    {
                        Debug.Log("[AvatarProp Editor] Final IK Stub not detected.");
                    }
                }
                
                if (isFinalIKInstalled == false)
                {
                    // Only update UI when status changes or first time, not every frame
                    if (previousInstalled != false || !notInstalledUIPopulated)
                    {
                        UpdateNotInstalledUI(notInstalledUI);
                        notInstalledUIPopulated = true;
                    }
                    notInstalledUI.style.display = DisplayStyle.Flex;
                    propCard.style.display = DisplayStyle.None;
                    toggleCard.style.display = DisplayStyle.None;
                    diagnosticsCard.style.display = DisplayStyle.None;
                    helpLinks.style.display = DisplayStyle.None;
                    installedBanner.style.display = DisplayStyle.None;
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
                
                // Reset flag when installed
                if (previousInstalled == false)
                {
                    notInstalledUIPopulated = false;
                }
                
                // Only update UI when status changes from not installed to installed
                if (previousInstalled == false)
                {
                    notInstalledUI.style.display = DisplayStyle.None;
                }
                installedBanner.style.display = DisplayStyle.None; // Hide banner when installed
                propCard.style.display = DisplayStyle.Flex;
                toggleCard.style.display = DisplayStyle.Flex;
                diagnosticsCard.style.display = DisplayStyle.Flex;
                helpLinks.style.display = DisplayStyle.Flex;
                
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
                var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
                UpdateOverviewCard(overviewCard, data, descriptor);
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateOverviewCard(VisualElement container, AvatarPropData data, VRCAvatarDescriptor descriptor)
        {
            container.Clear();
            
            string componentPath = descriptor != null 
                ? UnityEditor.AnimationUtility.CalculateTransformPath(data.transform, descriptor.transform) 
                : data.gameObject.name;
            string customPropName = data.customProp != null ? data.customProp.name : "(Default Sword)";
            
            var overviewCard = YUCPUIToolkitHelper.CreateCard("Avatar Prop Overview", null);
            var overviewContent = YUCPUIToolkitHelper.GetCardContent(overviewCard);
            
            AddInfoRow(overviewContent, "Component", componentPath);
            AddInfoRow(overviewContent, "Prop", customPropName);
            AddInfoRow(overviewContent, "Toggle", toggleNameProp.stringValue);
            
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Powered by ThatFatKidsMom's Avatar Prop (MIT). Please credit when shipping your avatar.", YUCPUIToolkitHelper.MessageType.Info));
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("This component must be placed under a VRCAvatarDescriptor.", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (!data.transform.IsChildOf(descriptor.transform))
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Component must be within the avatar hierarchy.", YUCPUIToolkitHelper.MessageType.Error));
            }
        }
        
        private void UpdateNotInstalledUI(VisualElement container)
        {
            if (container == null) return;
            
            container.Clear();
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            var installButton = YUCPUIToolkitHelper.CreateButton("Install Final IK Stub", () => InstallFinalIKStub(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            container.Add(installButton);
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Final IK Stub is required for this component. Click the button above to install it automatically. Unity will reload to compile the package.",
                YUCPUIToolkitHelper.MessageType.Info));
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            var checkButton = YUCPUIToolkitHelper.CreateButton("Check If Installed", () =>
            {
                isFinalIKInstalled = null;
                CheckForFinalIK();
                Repaint();
            }, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            container.Add(checkButton);
        }
        
        private void UpdateInstalledBanner(VisualElement container)
        {
            if (container == null) return;
            
            container.Clear();
            // Banner is hidden when installed - no need to show anything
        }
        
        private void InstallFinalIKStub()
        {
            FinalIKStubInstaller.InstallFinalIKStub(
                onSuccess: () => {
                    Debug.Log("[AvatarProp] Installation complete");
                },
                onError: (error) => {
                    EditorUtility.DisplayDialog(
                        "Installation Failed",
                        $"Failed to install Final IK Stub: {error}",
                        "OK");
                });
        }
        
        private void CheckForFinalIK()
        {
            // Reset the cached value to force re-check
            isFinalIKInstalled = null;
            isFinalIKInstalled = FinalIKStubInstaller.IsInstalled();
            if (isFinalIKInstalled == true)
            {
                Debug.Log("[AvatarProp Editor] Final IK Stub detected!");
            }
            else
            {
                Debug.Log("[AvatarProp Editor] Final IK Stub not detected.");
            }
        }
    }
}
