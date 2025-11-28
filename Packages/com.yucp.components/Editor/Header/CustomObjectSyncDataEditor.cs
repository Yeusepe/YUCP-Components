using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(CustomObjectSyncData))]
    public class CustomObjectSyncDataEditor : UnityEditor.Editor
    {
        private CustomObjectSyncData data;
        private int previousMaxRadius = -1;
        private bool previousQuickSync = false;
        private bool previousEnableGrouping = false;
        private string previousGroupId = null;
        private bool previousIncludeCredits = false;
        private string previousBuildSummary = null;

        private SerializedProperty quickSyncProp;
        private SerializedProperty referenceFrameProp;
        private SerializedProperty maxRadiusProp;
        private SerializedProperty positionPrecisionProp;
        private SerializedProperty rotationPrecisionProp;
        private SerializedProperty bitCountProp;
        private SerializedProperty rotationEnabledProp;
        private SerializedProperty addDampingProp;
        private SerializedProperty dampingValueProp;
        private SerializedProperty addDebugProp;
        private SerializedProperty writeDefaultsProp;
        private SerializedProperty menuLocationProp;
        private SerializedProperty syncGroupIdProp;
        private SerializedProperty enableGroupingProp;
        private SerializedProperty showSceneGizmoProp;
        private SerializedProperty verboseLoggingProp;
        private SerializedProperty includeCreditsProp;

        private GUIStyle sectionTitleStyle;
        private GUIStyle statsValueStyle;
        private GUIStyle miniWrapStyle;
        private GUIStyle budgetCardStyle;
        private GUIStyle budgetTitleStyle;
        private GUIStyle budgetValueStyle;
        private GUIStyle sectionSubtitleStyle;
        private GUIStyle cardStyle;
        private GUIStyle infoLabelStyle;
        private GUIStyle infoValueStyle;
        private GUIStyle summaryCardStyle;
        private GUIStyle summaryHeaderStyle;

        private static readonly GUIContent ReferenceFrameLabel = new GUIContent("Reference Frame", "Avatar centered drops an anchor when the sync starts. World space anchors to origin and supports late join.");
        private static readonly GUIContent MenuLocationLabel = new GUIContent("Menu Location", "Expression menu path where the enable toggle will be created.");
        private static readonly string VrLabsRepoUrl = "https://github.com/VRLabs/Custom-Object-Sync";
        private static readonly string WikiUrl = "https://github.com/Yeusepe/Yeusepes-Modules/wiki/Custom-Object-Sync";

        private void OnEnable()
        {
            data = (CustomObjectSyncData)target;

            quickSyncProp = serializedObject.FindProperty("quickSync");
            referenceFrameProp = serializedObject.FindProperty("referenceFrame");
            maxRadiusProp = serializedObject.FindProperty("maxRadius");
            positionPrecisionProp = serializedObject.FindProperty("positionPrecision");
            rotationPrecisionProp = serializedObject.FindProperty("rotationPrecision");
            bitCountProp = serializedObject.FindProperty("bitCount");
            rotationEnabledProp = serializedObject.FindProperty("rotationEnabled");
            addDampingProp = serializedObject.FindProperty("addDampingConstraint");
            dampingValueProp = serializedObject.FindProperty("dampingConstraintValue");
            addDebugProp = serializedObject.FindProperty("addLocalDebugView");
            writeDefaultsProp = serializedObject.FindProperty("writeDefaults");
            menuLocationProp = serializedObject.FindProperty("menuLocation");
            syncGroupIdProp = serializedObject.FindProperty("syncGroupId");
            enableGroupingProp = serializedObject.FindProperty("enableGrouping");
            showSceneGizmoProp = serializedObject.FindProperty("showSceneGizmo");
            verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
            includeCreditsProp = serializedObject.FindProperty("includeCredits");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Custom Object Sync"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(CustomObjectSyncData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(CustomObjectSyncData));
            if (supportBanner != null) root.Add(supportBanner);
            
            // Credit banner (conditional)
            var creditBanner = new VisualElement();
            creditBanner.name = "credit-banner";
            root.Add(creditBanner);
            
            // Build summary (conditional)
            var buildSummary = new VisualElement();
            buildSummary.name = "build-summary";
            root.Add(buildSummary);
            
            // Descriptor warnings (conditional)
            var descriptorWarnings = new VisualElement();
            descriptorWarnings.name = "descriptor-warnings";
            root.Add(descriptorWarnings);
            
            // Summary card
            var summaryCard = new VisualElement();
            summaryCard.name = "summary-card";
            root.Add(summaryCard);
            
            // Sync Strategy Card
            var syncStrategyCard = YUCPUIToolkitHelper.CreateCard("Sync Strategy", "Decide how this object synchronizes over the network.");
            var syncStrategyContent = YUCPUIToolkitHelper.GetCardContent(syncStrategyCard);
            syncStrategyContent.Add(YUCPUIToolkitHelper.CreateField(quickSyncProp, "Quick Sync"));
            
            var referenceFrameField = YUCPUIToolkitHelper.CreateField(referenceFrameProp, "Reference Frame");
            referenceFrameField.name = "reference-frame";
            syncStrategyContent.Add(referenceFrameField);
            
            syncStrategyContent.Add(YUCPUIToolkitHelper.CreateField(rotationEnabledProp, "Sync Rotation"));
            syncStrategyContent.Add(YUCPUIToolkitHelper.CreateField(addDebugProp, "Add Local Debug View"));
            root.Add(syncStrategyCard);
            
            // Precision & Range Card
            var precisionCard = YUCPUIToolkitHelper.CreateCard("Precision & Range", "Control how far and how precisely motion is captured.");
            var precisionContent = YUCPUIToolkitHelper.GetCardContent(precisionCard);
            
            var radiusContainer = new VisualElement();
            radiusContainer.name = "radius-container";
            precisionContent.Add(radiusContainer);
            
            var positionPrecisionField = new IntegerField("Position Precision") { bindingPath = "positionPrecision" };
            positionPrecisionField.AddToClassList("yucp-field-input");
            // Note: IntegerField doesn't support lowValue/highValue in UI Toolkit
            // Validation should be handled in the component or via custom validation
            precisionContent.Add(positionPrecisionField);
            
            var rotationPrecisionField = new IntegerField("Rotation Precision") { bindingPath = "rotationPrecision" };
            rotationPrecisionField.AddToClassList("yucp-field-input");
            precisionContent.Add(rotationPrecisionField);
            
            var bitCountField = new IntegerField("Bits Per Step") { bindingPath = "bitCount" };
            bitCountField.AddToClassList("yucp-field-input");
            bitCountField.name = "bit-count";
            precisionContent.Add(bitCountField);
            
            var quickSyncHelp = YUCPUIToolkitHelper.CreateHelpBox("Bit count is disabled while Quick Sync is enabled because floats are sent directly.", YUCPUIToolkitHelper.MessageType.Info);
            quickSyncHelp.name = "quick-sync-help";
            precisionContent.Add(quickSyncHelp);
            root.Add(precisionCard);
            
            // Motion Options Card
            var motionCard = YUCPUIToolkitHelper.CreateCard("Motion Options", "Fine-tune smoothing and animator integration.");
            var motionContent = YUCPUIToolkitHelper.GetCardContent(motionCard);
            motionContent.Add(YUCPUIToolkitHelper.CreateField(addDampingProp, "Add Damping Constraint"));
            
            var dampingValueField = new Slider("Damping Strength", 0.01f, 1f);
            dampingValueField.value = dampingValueProp.floatValue;
            dampingValueField.AddToClassList("yucp-field-input");
            dampingValueField.name = "damping-value";
            dampingValueField.RegisterCallback<MouseCaptureOutEvent>(evt =>
            {
                dampingValueProp.floatValue = dampingValueField.value;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
            });
            motionContent.Add(dampingValueField);
            
            motionContent.Add(YUCPUIToolkitHelper.CreateField(writeDefaultsProp, "Write Defaults"));
            motionContent.Add(YUCPUIToolkitHelper.CreateField(menuLocationProp, "Menu Location"));
            root.Add(motionCard);
            
            // Diagnostics & Debug Card
            var diagnosticsCard = YUCPUIToolkitHelper.CreateCard("Diagnostics & Debug", "Surface build output and logging helpers.");
            var diagnosticsContent = YUCPUIToolkitHelper.GetCardContent(diagnosticsCard);
            diagnosticsContent.Add(YUCPUIToolkitHelper.CreateField(verboseLoggingProp, "Verbose Logging"));
            diagnosticsContent.Add(YUCPUIToolkitHelper.CreateField(includeCreditsProp, "Include Credits Banner"));
            root.Add(diagnosticsCard);
            
            // Grouping & Collaboration Card
            var groupingCard = YUCPUIToolkitHelper.CreateCard("Grouping & Collaboration", "Keep multiple components in sync automatically.");
            var groupingContent = YUCPUIToolkitHelper.GetCardContent(groupingCard);
            groupingContent.Add(YUCPUIToolkitHelper.CreateField(enableGroupingProp, "Enable Grouping"));
            
            var groupIdField = YUCPUIToolkitHelper.CreateField(syncGroupIdProp, "Group ID");
            groupIdField.name = "group-id";
            groupingContent.Add(groupIdField);
            
            var groupingHelp = new VisualElement();
            groupingHelp.name = "grouping-help";
            groupingContent.Add(groupingHelp);
            root.Add(groupingCard);
            
            // Scene Visualization Card
            var sceneCard = YUCPUIToolkitHelper.CreateCard("Scene Visualization", "Toggle an in-scene gizmo that mirrors your settings for quick spatial feedback.");
            var sceneContent = YUCPUIToolkitHelper.GetCardContent(sceneCard);
            sceneContent.Add(YUCPUIToolkitHelper.CreateField(showSceneGizmoProp, "Show Scene Gizmo"));
            sceneContent.Add(YUCPUIToolkitHelper.CreateHelpBox("When enabled, selecting this object in the Scene view shows discs for travel radius plus labels for precision and rotation. Use it to size ranges without guessing.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(sceneCard);
            
            // Help Links
            YUCPUIToolkitHelper.AddSpacing(root, 6);
            var helpLinks = new VisualElement();
            helpLinks.style.flexDirection = FlexDirection.Row;
            helpLinks.style.marginBottom = 10;
            
            var docButton = YUCPUIToolkitHelper.CreateButton("Open Documentation", () => Application.OpenURL(WikiUrl), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            docButton.style.flexGrow = 1;
            docButton.style.marginRight = 5;
            helpLinks.Add(docButton);
            
            var discordButton = YUCPUIToolkitHelper.CreateButton("Join VRLabs Discord", () => Application.OpenURL("https://discord.vrlabs.dev/"), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            discordButton.style.flexGrow = 1;
            helpLinks.Add(discordButton);
            root.Add(helpLinks);
            
            // Initialize previous values
            previousMaxRadius = maxRadiusProp.intValue;
            previousQuickSync = quickSyncProp.boolValue;
            previousEnableGrouping = enableGroupingProp.boolValue;
            previousGroupId = syncGroupIdProp.stringValue;
            previousIncludeCredits = includeCreditsProp.boolValue;
            previousBuildSummary = data.GetBuildSummary();
            
            // Initial population
            UpdateCreditBanner(creditBanner);
            UpdateBuildSummary(buildSummary);
            UpdateDescriptorWarnings(descriptorWarnings);
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            UpdateSummaryCard(summaryCard, data, descriptor);
            UpdateRadiusField(radiusContainer);
            UpdateGroupingHelp(groupingHelp);
            
            // Dynamic updates
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                // Update credit banner only when it changes
                bool currentIncludeCredits = includeCreditsProp.boolValue;
                if (currentIncludeCredits != previousIncludeCredits)
                {
                    UpdateCreditBanner(creditBanner);
                    previousIncludeCredits = currentIncludeCredits;
                }
                
                // Update build summary only when it changes
                string currentBuildSummary = data.GetBuildSummary();
                if (currentBuildSummary != previousBuildSummary)
                {
                    UpdateBuildSummary(buildSummary);
                    previousBuildSummary = currentBuildSummary;
                }
                
                // Update descriptor warnings (these can change based on hierarchy, so check more frequently)
                UpdateDescriptorWarnings(descriptorWarnings);
                descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
                
                // Update summary card only when relevant values change
                bool currentQuickSync = quickSyncProp.boolValue;
                string currentGroupId = syncGroupIdProp.stringValue;
                bool currentEnableGrouping = enableGroupingProp.boolValue;
                if (currentQuickSync != previousQuickSync || currentGroupId != previousGroupId || 
                    currentEnableGrouping != previousEnableGrouping || maxRadiusProp.intValue != previousMaxRadius)
                {
                    UpdateSummaryCard(summaryCard, data, descriptor);
                    previousQuickSync = currentQuickSync;
                    previousGroupId = currentGroupId;
                    previousEnableGrouping = currentEnableGrouping;
                }
                
                // Update conditional fields
                referenceFrameField.SetEnabled(!quickSyncProp.boolValue);
                if (quickSyncProp.boolValue && referenceFrameProp.enumValueIndex != (int)CustomObjectSyncData.ReferenceFrame.AvatarCentered)
                {
                    referenceFrameProp.enumValueIndex = (int)CustomObjectSyncData.ReferenceFrame.AvatarCentered;
                }
                
                bitCountField.SetEnabled(!quickSyncProp.boolValue);
                quickSyncHelp.style.display = quickSyncProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                
                dampingValueField.SetEnabled(addDampingProp.boolValue);
                
                groupIdField.SetEnabled(enableGroupingProp.boolValue);
                
                // Update grouping help only when it changes
                if (currentEnableGrouping != previousEnableGrouping || currentGroupId != previousGroupId)
                {
                    UpdateGroupingHelp(groupingHelp);
                }
                
                // Update radius field only when it changes
                int currentMaxRadius = maxRadiusProp.intValue;
                if (currentMaxRadius != previousMaxRadius)
                {
                    UpdateRadiusField(radiusContainer);
                    previousMaxRadius = currentMaxRadius;
                }
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateSummaryCard(VisualElement container, CustomObjectSyncData data, VRCAvatarDescriptor descriptor)
        {
            container.Clear();
            
            var summary = CalculateParameterSummary();
            string targetPath = descriptor != null ? UnityEditor.AnimationUtility.CalculateTransformPath(data.transform, descriptor.transform) : data.gameObject.name;
            string modeLabel = quickSyncProp.boolValue ? "Quick Sync (fast, higher cost)" : "Bit Packed (slower, parameter efficient)";
            
            var summaryCard = YUCPUIToolkitHelper.CreateCard("Custom Object Sync Overview", null);
            var summaryContent = YUCPUIToolkitHelper.GetCardContent(summaryCard);
            
            var title = new Label("Custom Object Sync Overview");
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 2;
            summaryContent.Add(title);
            
            AddInfoRow(summaryContent, "Target", targetPath);
            var groupingLabel = enableGroupingProp.boolValue
                ? CustomObjectSyncData.NormalizeGroupId(syncGroupIdProp.stringValue)
                : "Isolated (per-object)";
            AddInfoRow(summaryContent, "Group", groupingLabel);
            AddInfoRow(summaryContent, "Mode", modeLabel);
            
            YUCPUIToolkitHelper.AddSpacing(summaryContent, 4);
            
            // Parameter budget card
            var budgetCard = YUCPUIToolkitHelper.CreateCard("Expression Parameters", null);
            var budgetContent = YUCPUIToolkitHelper.GetCardContent(budgetCard);
            
            var budgetValue = new Label(summary.Total.ToString());
            budgetValue.style.fontSize = 18;
            budgetValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            budgetValue.style.unityTextAlign = TextAnchor.MiddleCenter;
            budgetValue.style.marginBottom = 2;
            budgetContent.Add(budgetValue);
            
            var groupSizeLabel = new Label($"Group Size: {summary.GroupSize}");
            groupSizeLabel.style.fontSize = 10;
            groupSizeLabel.style.marginBottom = 2;
            budgetContent.Add(groupSizeLabel);
            
            if (!string.IsNullOrEmpty(summary.Breakdown))
            {
                var breakdownLabel = new Label(summary.Breakdown);
                breakdownLabel.style.fontSize = 10;
                breakdownLabel.style.marginBottom = 2;
                budgetContent.Add(breakdownLabel);
            }
            
            if (!string.IsNullOrEmpty(summary.Extra))
            {
                var extraLabel = new Label(summary.Extra);
                extraLabel.style.fontSize = 10;
                budgetContent.Add(extraLabel);
            }
            
            summaryContent.Add(budgetCard);
            container.Add(summaryCard);
        }
        
        private void AddInfoRow(VisualElement parent, string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            
            var labelElement = new Label(label);
            labelElement.style.fontSize = 10;
            labelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            labelElement.style.width = 70;
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Powered by VRLabs Custom Object Sync (MIT). Please credit VRLabs when shipping your avatar.", YUCPUIToolkitHelper.MessageType.Info));
                var repoButton = YUCPUIToolkitHelper.CreateButton("Open VRLabs Custom Object Sync Repository", () => Application.OpenURL(VrLabsRepoUrl), YUCPUIToolkitHelper.ButtonVariant.Secondary);
                container.Add(repoButton);
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
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("This component must be placed under a VRCAvatarDescriptor in order for the builder to configure sync data.", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (data.transform == descriptor.transform)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Attach Custom Object Sync to the object you want to sync, not the descriptor root.", YUCPUIToolkitHelper.MessageType.Warning));
            }
            else if (!data.transform.IsChildOf(descriptor.transform))
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Custom Object Sync target must be within the avatar hierarchy. Please move it inside the descriptor object.", YUCPUIToolkitHelper.MessageType.Error));
            }
        }
        
        private void UpdateGroupingHelp(VisualElement container)
        {
            container.Clear();
            var groupingInfo = enableGroupingProp.boolValue
                ? "Components with the same Group ID share one Custom Object Sync rig to reduce parameters."
                : "Grouping disabled: this component will get its own rig (same behavior as the original VRLabs wizard).";
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(groupingInfo, YUCPUIToolkitHelper.MessageType.Info));
        }
        
        private void UpdateRadiusField(VisualElement container)
        {
            container.Clear();
            
            int rawValue = Mathf.Clamp(maxRadiusProp.intValue, 1, 12);
            double rangeMeters = Math.Pow(2, rawValue);
            
            var sliderContainer = new VisualElement();
            sliderContainer.style.flexDirection = FlexDirection.Row;
            sliderContainer.style.marginBottom = 5;
            
            var slider = new Slider($"Max Radius (2^{rawValue} m)", 1, 12);
            slider.value = rawValue;
            slider.AddToClassList("yucp-field-input");
            slider.style.flexGrow = 1;
            slider.style.marginRight = 5;
            
            var valueLabel = new Label($"{rangeMeters:0.#} m");
            valueLabel.style.width = 70;
            valueLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            
            int currentDiscreteSection = rawValue;
            float previousSliderValue = rawValue;
            bool isDragging = false;
            const float snapZoneSize = 0.3f;
            const float snapSpeed = 30f;
            const float velocityThreshold = 0.05f;
            
            slider.RegisterValueChangedCallback(evt =>
            {
                if (!isDragging) return;
                
                int nearestDiscrete = Mathf.RoundToInt(evt.newValue);
                float distanceToNearest = Mathf.Abs(evt.newValue - nearestDiscrete);
                
                if (distanceToNearest < snapZoneSize)
                {
                    if (nearestDiscrete != currentDiscreteSection)
                    {
                        currentDiscreteSection = nearestDiscrete;
                    }
                }
                else
                {
                    float distanceFromCurrent = Mathf.Abs(evt.newValue - currentDiscreteSection);
                    if (distanceFromCurrent > snapZoneSize)
                    {
                        currentDiscreteSection = nearestDiscrete;
                    }
                }
            });
            
            slider.RegisterCallback<MouseDownEvent>(evt => 
            { 
                isDragging = true;
                previousSliderValue = slider.value;
                currentDiscreteSection = Mathf.RoundToInt(slider.value);
            });
            
            slider.RegisterCallback<MouseCaptureOutEvent>(evt =>
            {
                isDragging = false;
                int finalValue = Mathf.RoundToInt(slider.value);
                currentDiscreteSection = finalValue;
                slider.value = finalValue;
                previousSliderValue = finalValue;
                
                if (finalValue != maxRadiusProp.intValue)
                {
                    maxRadiusProp.intValue = finalValue;
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(data);
                }
            });
            
            slider.schedule.Execute(() =>
            {
                int displayValue;
                double newRangeMeters;
                
                if (!isDragging)
                {
                    displayValue = Mathf.RoundToInt(slider.value);
                    newRangeMeters = Math.Pow(2, displayValue);
                    slider.label = $"Max Radius (2^{displayValue} m)";
                    valueLabel.text = $"{newRangeMeters:0.#} m";
                    return;
                }
                
                float currentSliderValue = slider.value;
                float mouseVelocity = Mathf.Abs(currentSliderValue - previousSliderValue);
                float distanceToDiscrete = Mathf.Abs(currentSliderValue - currentDiscreteSection);
                
                bool isMovingSlowly = mouseVelocity < velocityThreshold;
                bool isInSnapZone = distanceToDiscrete < snapZoneSize;
                bool shouldSnap = isInSnapZone && isMovingSlowly;
                
                if (shouldSnap)
                {
                    float targetValue = currentDiscreteSection;
                    float newValue = Mathf.Lerp(currentSliderValue, targetValue, snapSpeed * 0.016f);
                    
                    if (Mathf.Abs(newValue - targetValue) < 0.005f)
                    {
                        newValue = targetValue;
                    }
                    
                    float currentActualValue = slider.value;
                    if (Mathf.Abs(currentActualValue - newValue) > 0.003f && Mathf.Abs(currentActualValue - previousSliderValue) < 0.1f)
                    {
                        slider.SetValueWithoutNotify(newValue);
                    }
                    
                    displayValue = Mathf.RoundToInt(newValue);
                }
                else
                {
                    displayValue = Mathf.RoundToInt(currentSliderValue);
                }
                
                previousSliderValue = slider.value;
                
                newRangeMeters = Math.Pow(2, displayValue);
                slider.label = $"Max Radius (2^{displayValue} m)";
                valueLabel.text = $"{newRangeMeters:0.#} m";
            }).Every(16);
            
            sliderContainer.Add(slider);
            sliderContainer.Add(valueLabel);
            
            container.Add(sliderContainer);
            
            var helpLabel = new Label("Choose how far the object can move from its anchor point. Example: value 8 allows roughly 256m of travel. Higher values consume more bits.");
            helpLabel.style.fontSize = 10;
            helpLabel.style.whiteSpace = WhiteSpace.Normal;
            helpLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            container.Add(helpLabel);
        }

        public override void OnInspectorGUI()
        {
            // Legacy support - not used anymore
        }

        private void DrawRadiusField()
        {
            EnsureStyles();
            int rawValue = Mathf.Clamp(maxRadiusProp.intValue, 1, 12);
            double rangeMeters = Math.Pow(2, rawValue);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.IntSlider(maxRadiusProp, 1, 12, new GUIContent($"Max Radius (2^{rawValue} m)"));
                GUILayout.Label($"{rangeMeters:0.#} m", GUILayout.Width(70));
            }
            EditorGUILayout.LabelField("Choose how far the object can move from its anchor point. Example: value 8 allows roughly 256m of travel. Higher values consume more bits.", miniWrapStyle);
        }

        private void DrawParameterBudget(ParameterSummary summary)
        {
            EnsureStyles();

            EditorGUILayout.BeginVertical(budgetCardStyle);
            EditorGUILayout.LabelField("Expression Parameters", budgetTitleStyle);
            EditorGUILayout.LabelField(summary.Total.ToString(), budgetValueStyle);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"Group Size: {summary.GroupSize}", miniWrapStyle);
            if (!string.IsNullOrEmpty(summary.Breakdown))
            {
                EditorGUILayout.LabelField(summary.Breakdown, miniWrapStyle);
            }
            if (!string.IsNullOrEmpty(summary.Extra))
            {
                EditorGUILayout.LabelField(summary.Extra, miniWrapStyle);
            }
            EditorGUILayout.EndVertical();
        }

        // Removed IMGUI methods: DrawDescriptorWarnings, DrawCreditBanner, DrawBuildSummary
        // These were IMGUI-only methods that are no longer needed with UI Toolkit migration

        private void DrawHelpLinks()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Documentation"))
                {
                    Application.OpenURL(WikiUrl);
                }

                if (GUILayout.Button("Join VRLabs Discord"))
                {
                    Application.OpenURL("https://discord.vrlabs.dev/");
                }
            }
        }

        private ParameterSummary CalculateParameterSummary()
        {
            const int axisCount = 3;
            int objectCount = Mathf.Max(1, GetGroupObjectCount());

            bool quickSync = quickSyncProp.boolValue;
            bool rotationEnabled = rotationEnabledProp.boolValue;

            int maxRadius = Mathf.Clamp(maxRadiusProp.intValue, 1, 12);
            int positionPrecision = Mathf.Clamp(positionPrecisionProp.intValue, 1, 12);
            int rotationPrecision = Mathf.Clamp(rotationPrecisionProp.intValue, 0, 12);
            int bitCount = Mathf.Clamp(bitCountProp.intValue, 1, 32);

            int objectParameterCount = objectCount > 1 ? Mathf.CeilToInt(Mathf.Log(objectCount, 2f)) : 0;
            int rotationBits = rotationEnabled ? rotationPrecision * axisCount : 0;
            int positionBits = axisCount * (maxRadius + positionPrecision);
            int totalBits = rotationBits + positionBits;

            if (quickSync)
            {
                int totalParameters = objectParameterCount + totalBits + 1;
                float syncInterval = objectCount * 0.2f;
                string breakdown = BuildBreakdown(totalBits, objectParameterCount, includeStepBits: false, 0);
                string extra = $"Sync interval ≈ {syncInterval:0.###}s";
                return new ParameterSummary(totalParameters, breakdown, extra, objectCount);
            }

            int syncSteps = Mathf.Max(1, Mathf.CeilToInt(totalBits / Mathf.Max(1f, bitCount)));
            int stepParameterCount = Mathf.CeilToInt(Mathf.Log(syncSteps + 1, 2f));
            int totalExpressionParameters = objectParameterCount + stepParameterCount + bitCount + 1;

            float conversionTime = Mathf.Max(rotationPrecision, maxRadius + positionPrecision) * 1.5f / 60f;
            float syncTime = objectCount * syncSteps * 0.2f;
            float syncDelay = syncTime + (2f * conversionTime);

            string stepBreakdown = BuildBreakdown(bitCount, objectParameterCount, includeStepBits: true, stepParameterCount);
            string extraInfo = $"Sync steps: {syncSteps}, Interval ≈ {syncTime:0.###}s, Delay ≈ {syncDelay:0.###}s";

            return new ParameterSummary(totalExpressionParameters, stepBreakdown, extraInfo, objectCount);
        }

        private int GetGroupObjectCount()
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return 1;
            }

            var members = descriptor.GetComponentsInChildren<CustomObjectSyncData>(true);
            if (members == null || members.Length == 0)
            {
                return 1;
            }

            string targetGroup = CustomObjectSyncData.NormalizeGroupId(syncGroupIdProp.stringValue);
            int count = 0;
            foreach (var member in members)
            {
                if (member == null) continue;
                if (CustomObjectSyncData.NormalizeGroupId(member.syncGroupId) == targetGroup)
                {
                    count++;
                }
            }

            return Mathf.Max(1, count);
        }

        private void EnsureStyles()
        {
            if (sectionTitleStyle == null)
            {
                sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (sectionSubtitleStyle == null)
            {
                sectionSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };
            }

            if (statsValueStyle == null)
            {
                statsValueStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontSize = Math.Max(14, EditorStyles.boldLabel.fontSize + 2)
                };
            }

            if (miniWrapStyle == null)
            {
                miniWrapStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                {
                    richText = false
                };
            }

            if (budgetCardStyle == null)
            {
                budgetCardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 10)
                };
            }

            if (budgetTitleStyle == null)
            {
                budgetTitleStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (budgetValueStyle == null)
            {
                budgetValueStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (cardStyle == null)
            {
                cardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 10)
                };
            }

            if (infoLabelStyle == null)
            {
                infoLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontStyle = FontStyle.Bold
                };
            }

            if (infoValueStyle == null)
            {
                infoValueStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true
                };
            }

            if (summaryCardStyle == null)
            {
                summaryCardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(14, 14, 12, 12)
                };
            }

            if (summaryHeaderStyle == null)
            {
                summaryHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13
                };
            }
        }

        private static string BuildBreakdown(int dataBits, int objectBits, bool includeStepBits, int secondaryBitCount)
        {
            string breakdown = $"Enable toggle + {dataBits} data bits";

            if (includeStepBits)
            {
                breakdown += $" + {secondaryBitCount} step bits";
            }

            if (objectBits > 0)
            {
                breakdown += $" + {objectBits} object bits";
            }

            return breakdown;
        }

        private readonly struct ParameterSummary
        {
            public ParameterSummary(int total, string breakdown, string extra, int groupSize)
            {
                Total = total;
                Breakdown = breakdown;
                Extra = extra;
                GroupSize = groupSize;
            }

            public int Total { get; }
            public string Breakdown { get; }
            public string Extra { get; }
            public int GroupSize { get; }
        }


        private void DrawSummaryCard()
        {
            EnsureStyles();
            var summary = CalculateParameterSummary();
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            string targetPath = descriptor != null ? AnimationUtility.CalculateTransformPath(data.transform, descriptor.transform) : data.gameObject.name;
            string modeLabel = quickSyncProp.boolValue ? "Quick Sync (fast, higher cost)" : "Bit Packed (slower, parameter efficient)";

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(summaryCardStyle);
            EditorGUILayout.LabelField("Custom Object Sync Overview", summaryHeaderStyle);
            EditorGUILayout.Space(2);
            DrawInfoRow("Target", targetPath);
            var groupingLabel = enableGroupingProp.boolValue
                ? CustomObjectSyncData.NormalizeGroupId(syncGroupIdProp.stringValue)
                : "Isolated (per-object)";
            DrawInfoRow("Group", groupingLabel);
            DrawInfoRow("Mode", modeLabel);
            EditorGUILayout.Space(4);
            DrawParameterBudget(summary);
            EditorGUILayout.EndVertical();
        }

        private void DrawInfoRow(string label, string value)
        {
            EnsureStyles();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, infoLabelStyle, GUILayout.Width(70));
                EditorGUILayout.LabelField(value, infoValueStyle);
            }
        }
    }
}

