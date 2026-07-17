using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(ParameterCompressorData))]
    public sealed class ParameterCompressorDataEditor : UnityEditor.Editor
    {
        private enum InspectorMode
        {
            Simple,
            Advanced
        }

        private ParameterCompressorData data;
        private SerializedProperty automaticSelectionProp;
        private SerializedProperty reserveSyncedBitsProp;
        private SerializedProperty optimizationBiasProp;
        private SerializedProperty parameterPrefixProp;
        private SerializedProperty profileProp;
        private SerializedProperty rulesProp;

        private InspectorMode inspectorMode;
        private VisualElement validation;
        private VisualElement simpleHost;
        private VisualElement advancedHost;
        private VisualElement manualSimpleHint;
        private VisualElement ruleList;
        private Button simpleButton;
        private Button advancedButton;
        private Label summaryValue;
        private Label summaryDetails;
        private Label advancedSummary;

        private void OnEnable()
        {
            data = (ParameterCompressorData)target;
            automaticSelectionProp = serializedObject.FindProperty(
                "automaticSelection");
            reserveSyncedBitsProp = serializedObject.FindProperty(
                "reserveSyncedBits");
            optimizationBiasProp = serializedObject.FindProperty(
                "optimizationBias");
            parameterPrefixProp = serializedObject.FindProperty(
                "parameterPrefix");
            profileProp = serializedObject.FindProperty("profile");
            rulesProp = serializedObject.FindProperty("rules");

            inspectorMode = (InspectorMode)Mathf.Clamp(
                SessionState.GetInt(ModeKey, (int)InspectorMode.Simple),
                (int)InspectorMode.Simple,
                (int)InspectorMode.Advanced);
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();

            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader
                .CreateHeaderOverlay("Parameter Compressor"));

            var support = SupportBannerHelper.CreateSupportBannerVisualElement(
                typeof(ParameterCompressorData));
            if (support != null) root.Add(support);

            validation = new VisualElement { name = "validation-messages" };
            root.Add(validation);

            var tabStyles = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Layouts/YUCPTabs.uss");
            if (tabStyles != null) root.styleSheets.Add(tabStyles);

            BuildTabs(root);

            simpleHost = new VisualElement { name = "simple-mode-ui" };
            simpleHost.AddToClassList("yucp-tabs-content");
            BuildSimple(simpleHost);
            root.Add(simpleHost);

            advancedHost = new VisualElement { name = "advanced-mode-ui" };
            advancedHost.AddToClassList("yucp-tabs-content");
            BuildAdvanced(advancedHost);
            root.Add(advancedHost);

            UpdateDynamicUi();
            root.schedule.Execute(() =>
            {
                if (target == null) return;
                serializedObject.UpdateIfRequiredOrScript();
                UpdateDynamicUi();
            }).Every(250);

            return root;
        }

        private string ModeKey =>
            "YUCP_ParameterCompressor_UIMode_" +
            (data != null ? data.GetInstanceID() : 0);

        private void BuildTabs(VisualElement root)
        {
            var tabs = new VisualElement { name = "mode-switcher" };
            tabs.AddToClassList("yucp-tabs-header");

            simpleButton = new Button(() => SetMode(InspectorMode.Simple))
            {
                text = "Simple",
                name = "simple-mode-tab"
            };
            simpleButton.AddToClassList("yucp-tab");
            simpleButton.style.flexGrow = 1;
            tabs.Add(simpleButton);

            advancedButton = new Button(() => SetMode(InspectorMode.Advanced))
            {
                text = "Advanced",
                name = "advanced-mode-tab"
            };
            advancedButton.AddToClassList("yucp-tab");
            advancedButton.style.flexGrow = 1;
            tabs.Add(advancedButton);
            root.Add(tabs);
        }

        private void BuildSimple(VisualElement root)
        {
            var setup = YUCPUIToolkitHelper.CreateCard(
                "Make Parameters Fit",
                "The compressor keeps local controls unchanged and shares them through a smaller network channel.");
            var content = YUCPUIToolkitHelper.GetCardContent(setup);
            content.Add(YUCPUIToolkitHelper.CreateField(
                automaticSelectionProp, "Choose Safe Parameters Automatically"));
            content.Add(CreateReservedBitsSlider("Keep Space Free"));
            content.Add(CreateBiasSlider());
            manualSimpleHint = YUCPUIToolkitHelper.CreateHelpBox(
                "Automatic selection is off. Open Advanced to choose which parameters are included.",
                YUCPUIToolkitHelper.MessageType.Info);
            content.Add(manualSimpleHint);
            root.Add(setup);

            var summary = YUCPUIToolkitHelper.CreateCard(
                "Synced Space", "The exact plan is calculated after avatar tools finish generating their parameters.");
            var summaryContent = YUCPUIToolkitHelper.GetCardContent(summary);
            summaryValue = new Label { name = "compression-summary-value" };
            summaryValue.style.fontSize = 18;
            summaryValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            summaryValue.style.marginBottom = 4;
            summaryContent.Add(summaryValue);
            summaryDetails = new Label { name = "compression-summary-details" };
            summaryDetails.style.whiteSpace = WhiteSpace.Normal;
            summaryContent.Add(summaryDetails);
            root.Add(summary);
        }

        private void BuildAdvanced(VisualElement root)
        {
            var behavior = YUCPUIToolkitHelper.CreateCard(
                "Compression Policy",
                "Control how the final merged avatar is analyzed and how much space is reserved.");
            var behaviorContent = YUCPUIToolkitHelper.GetCardContent(behavior);
            behaviorContent.Add(YUCPUIToolkitHelper.CreateField(
                automaticSelectionProp, "Automatic Selection"));
            behaviorContent.Add(CreateReservedBitsSlider("Reserved Synced Bits"));
            behaviorContent.Add(CreateBiasSlider());
            advancedSummary = new Label { name = "advanced-compression-summary" };
            advancedSummary.style.whiteSpace = WhiteSpace.Normal;
            advancedSummary.style.marginTop = 4;
            behaviorContent.Add(advancedSummary);
            root.Add(behavior);

            var identity = YUCPUIToolkitHelper.CreateFoldout(
                "Transport Identity", false);
            identity.Add(YUCPUIToolkitHelper.CreateField(
                parameterPrefixProp, "Parameter Prefix"));
            identity.Add(YUCPUIToolkitHelper.CreateField(
                profileProp, "Reusable Profile"));
            root.Add(identity);

            var rulesCard = YUCPUIToolkitHelper.CreateCard(
                "Parameter Rules",
                "Override automatic choices, update priority, precision, range, or scheduling group for individual parameters.");
            var rulesContent = YUCPUIToolkitHelper.GetCardContent(rulesCard);
            ruleList = new VisualElement { name = "parameter-rule-list" };
            rulesContent.Add(ruleList);

            var addRule = new Button(AddRule)
            {
                text = "Add Parameter Rule",
                name = "add-parameter-rule"
            };
            addRule.style.marginTop = 6;
            rulesContent.Add(addRule);
            root.Add(rulesCard);
            RebuildRules();
        }

        private VisualElement CreateReservedBitsSlider(string label)
        {
            var slider = new SliderInt(
                label, 0, ParameterCompressionContract.MaximumReservedBits)
            {
                showInputField = true,
                tooltip = "Space left available for parameters created later in the build."
            };
            slider.AddToClassList("yucp-field-input");
            slider.BindProperty(reserveSyncedBitsProp);
            return slider;
        }

        private VisualElement CreateBiasSlider()
        {
            var container = new VisualElement { name = "compression-bias" };
            var title = new Label("Sync Preference");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            container.Add(title);

            var slider = new Slider(0f, 1f)
            {
                showInputField = false,
                tooltip = "Move left for faster refreshes or right to use fewer synced bits."
            };
            slider.AddToClassList("yucp-field-input");
            slider.BindProperty(optimizationBiasProp);
            container.Add(slider);

            var labels = new VisualElement();
            labels.style.flexDirection = FlexDirection.Row;
            labels.style.justifyContent = Justify.SpaceBetween;
            var fast = new Label("Faster updates");
            fast.style.opacity = 0.7f;
            var small = new Label("Smaller footprint");
            small.style.opacity = 0.7f;
            labels.Add(fast);
            labels.Add(small);
            container.Add(labels);
            container.style.marginTop = 3;
            container.style.marginBottom = 5;
            return container;
        }

        private void AddRule()
        {
            serializedObject.Update();
            Undo.RecordObject(data, "Add Parameter Compression Rule");
            var index = rulesProp.arraySize;
            rulesProp.arraySize++;
            var rule = rulesProp.GetArrayElementAtIndex(index);
            rule.FindPropertyRelative("parameterName").stringValue = string.Empty;
            rule.FindPropertyRelative("selection").enumValueIndex =
                (int)ParameterCompressionRuleSelection.Automatic;
            rule.FindPropertyRelative("priority").enumValueIndex =
                (int)ParameterCompressionPriority.Normal;
            rule.FindPropertyRelative("precision").enumValueIndex =
                (int)ParameterCompressionPrecision.Automatic;
            rule.FindPropertyRelative("range").enumValueIndex =
                (int)ParameterCompressionRangeMode.Automatic;
            rule.FindPropertyRelative("minimum").floatValue = 0f;
            rule.FindPropertyRelative("maximum").floatValue = 1f;
            rule.FindPropertyRelative("group").stringValue =
                ParameterCompressionContract.DefaultGroup;
            rule.FindPropertyRelative("stableId").stringValue =
                ParameterCompressionContract.NewStableId();
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
            RebuildRules();
        }

        private void RemoveRule(int index)
        {
            if (index < 0 || index >= rulesProp.arraySize) return;
            serializedObject.Update();
            Undo.RecordObject(data, "Remove Parameter Compression Rule");
            rulesProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
            RebuildRules();
        }

        private void RebuildRules()
        {
            if (ruleList == null || rulesProp == null) return;
            serializedObject.UpdateIfRequiredOrScript();
            ruleList.Clear();

            if (rulesProp.arraySize == 0)
            {
                var empty = new Label(
                    "No overrides yet. Automatic mode will protect unsafe inputs and choose persistent menu controls for you.");
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.opacity = 0.75f;
                empty.style.marginBottom = 4;
                ruleList.Add(empty);
                return;
            }

            for (var index = 0; index < rulesProp.arraySize; index++)
            {
                var capturedIndex = index;
                var rule = rulesProp.GetArrayElementAtIndex(index);
                var name = rule.FindPropertyRelative("parameterName").stringValue;
                var card = YUCPUIToolkitHelper.CreateCard(
                    string.IsNullOrWhiteSpace(name)
                        ? "New Parameter"
                        : name,
                    "Rule " + (index + 1));
                var content = YUCPUIToolkitHelper.GetCardContent(card);

                var parameterField = YUCPUIToolkitHelper.CreateField(
                    rule.FindPropertyRelative("parameterName"), "Parameter");
                content.Add(parameterField);
                content.Add(YUCPUIToolkitHelper.CreateField(
                    rule.FindPropertyRelative("selection"), "Include"));
                content.Add(YUCPUIToolkitHelper.CreateField(
                    rule.FindPropertyRelative("priority"), "Update Priority"));
                content.Add(YUCPUIToolkitHelper.CreateField(
                    rule.FindPropertyRelative("precision"), "Precision"));

                var rangeMode = rule.FindPropertyRelative("range");
                var rangeField = YUCPUIToolkitHelper.CreateField(
                    rangeMode, "Range");
                content.Add(rangeField);
                var customRange = new VisualElement
                {
                    name = "custom-range-" + index
                };
                customRange.Add(YUCPUIToolkitHelper.CreateField(
                    rule.FindPropertyRelative("minimum"), "Minimum"));
                customRange.Add(YUCPUIToolkitHelper.CreateField(
                    rule.FindPropertyRelative("maximum"), "Maximum"));
                content.Add(customRange);
                Action refreshRange = () =>
                {
                    customRange.style.display = rangeMode.enumValueIndex ==
                        (int)ParameterCompressionRangeMode.Custom
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                };
                rangeField.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
                    refreshRange());
                refreshRange();

                content.Add(YUCPUIToolkitHelper.CreateField(
                    rule.FindPropertyRelative("group"), "Group"));

                var remove = new Button(() => RemoveRule(capturedIndex))
                {
                    text = "Remove Rule",
                    name = "remove-parameter-rule-" + index
                };
                remove.style.marginTop = 5;
                content.Add(remove);
                ruleList.Add(card);
            }
        }

        private void SetMode(InspectorMode mode)
        {
            inspectorMode = mode;
            SessionState.SetInt(ModeKey, (int)mode);
            UpdateMode();
        }

        private void UpdateMode()
        {
            if (simpleHost != null)
                simpleHost.style.display = inspectorMode == InspectorMode.Simple
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (advancedHost != null)
                advancedHost.style.display = inspectorMode == InspectorMode.Advanced
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

            SetSelected(simpleButton, inspectorMode == InspectorMode.Simple);
            SetSelected(advancedButton, inspectorMode == InspectorMode.Advanced);
        }

        private static void SetSelected(Button button, bool selected)
        {
            if (button == null) return;
            if (selected) button.AddToClassList("yucp-tab-selected");
            else button.RemoveFromClassList("yucp-tab-selected");
        }

        private void UpdateDynamicUi()
        {
            serializedObject.UpdateIfRequiredOrScript();
            UpdateMode();
            if (manualSimpleHint != null)
                manualSimpleHint.style.display = automaticSelectionProp.boolValue
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            UpdateSummary();
            UpdateValidation();
        }

        private void UpdateSummary()
        {
            var currentBits = CurrentSyncedBits();
            var targetBits = Mathf.Max(
                0,
                ParameterCompressionContract.VrchatParameterBudget -
                reserveSyncedBitsProp.intValue);
            var build = data.GetBuildSummary();

            string value;
            string details;
            if (build.hasResult)
            {
                value = build.beforeBits + "  →  " + build.afterBits + " bits";
                var parts = new List<string>
                {
                    build.compressedParameters + " compressed",
                    build.protectedParameters + " protected",
                    build.carrierBits + " carrier bits"
                };
                if (build.nominalFullRefreshSeconds > 0f)
                    parts.Add("up to " +
                              build.nominalFullRefreshSeconds.ToString("0.0") +
                              "s nominal full refresh");
                if (!string.IsNullOrWhiteSpace(build.transportName))
                    parts.Add(build.transportName);
                details = string.Join("  •  ", parts);
                if (!string.IsNullOrWhiteSpace(build.message))
                    details += "\n" + build.message;
            }
            else if (currentBits >= 0)
            {
                value = currentBits <= targetBits
                    ? currentBits + "  →  " + currentBits + " bits"
                    : currentBits + "  →  target ≤ " + targetBits + " bits";
                details = currentBits <= targetBits
                    ? "This avatar already fits the selected reserve. The build-time planner will leave safe parameters direct."
                    : "The build-time planner will inspect the final merged avatar and choose the smallest safe transport that reaches this target.";
            }
            else
            {
                value = "Build-time plan";
                details = "Place this component inside an avatar to preview its current synced parameter use.";
            }

            if (summaryValue != null) summaryValue.text = value;
            if (summaryDetails != null) summaryDetails.text = details;
            if (advancedSummary != null)
                advancedSummary.text = value + "\n" + details;
        }

        private int CurrentSyncedBits()
        {
            var descriptor = data != null
                ? data.GetComponentInParent<VRCAvatarDescriptor>()
                : null;
            var parameters = descriptor != null
                ? descriptor.expressionParameters
                : null;
            if (parameters == null || parameters.parameters == null) return -1;
            return parameters.parameters
                .Where(parameter => parameter != null && parameter.networkSynced)
                .Sum(parameter => parameter.valueType ==
                                  VRCExpressionParameters.ValueType.Bool ? 1 : 8);
        }

        private void UpdateValidation()
        {
            if (validation == null) return;
            validation.Clear();
            if (data == null) return;

            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Place this component anywhere inside a VRChat avatar.",
                    YUCPUIToolkitHelper.MessageType.Warning));
                return;
            }

            var compressors = descriptor.GetComponentsInChildren<
                ParameterCompressorData>(true);
            if (compressors.Length > 1)
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Use one Parameter Compressor per avatar. It already sees parameters from every prefab and component.",
                    YUCPUIToolkitHelper.MessageType.Error));

            var emptyRules = data.rules != null
                ? data.rules.Count(rule => rule != null &&
                                          string.IsNullOrWhiteSpace(
                                              rule.parameterName))
                : 0;
            if (emptyRules > 0)
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    emptyRules + " parameter rule" +
                    (emptyRules == 1 ? " is" : "s are") +
                    " missing a parameter name.",
                    YUCPUIToolkitHelper.MessageType.Warning));

            var duplicateNames = data.rules == null
                ? Array.Empty<string>()
                : data.rules
                    .Where(rule => rule != null &&
                                   !string.IsNullOrWhiteSpace(rule.parameterName))
                    .GroupBy(rule =>
                            ParameterCompressionContract.NormalizeParameterName(
                                rule.parameterName),
                        StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToArray();
            if (duplicateNames.Length > 0)
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Combine duplicate rules for: " +
                    string.Join(", ", duplicateNames) + ".",
                    YUCPUIToolkitHelper.MessageType.Error));
        }
    }
}
