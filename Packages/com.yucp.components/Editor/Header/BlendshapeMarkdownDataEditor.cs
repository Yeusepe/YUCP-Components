using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(BlendshapeMarkdownData))]
    public class BlendshapeMarkdownDataEditor : UnityEditor.Editor
    {
        private BlendshapeMarkdownData data;

        private SerializedProperty targetRendererProp;
        private SerializedProperty enableNativeInspectorIntegrationProp;
        private SerializedProperty replaceDefaultBlendshapeListProp;
        private SerializedProperty showSearchBarProp;
        private SerializedProperty showBlendshapeCountsProp;
        private SerializedProperty presetProp;
        private SerializedProperty useSlashAsPathSeparatorProp;
        private SerializedProperty showUngroupedBlendshapesProp;
        private SerializedProperty ungroupedSectionTitleProp;
        private SerializedProperty topLevelAutoGroupPrefixProp;
        private SerializedProperty topLevelAutoGroupTitleProp;
        private SerializedProperty headingRulesProp;
        private SerializedProperty expandTopLevelByDefaultProp;
        private SerializedProperty expandNestedByDefaultProp;
        private SerializedProperty colorRulesProp;
        private SerializedProperty debugLoggingProp;

        private bool showGeneral = true;
        private bool showHeadingRules = true;
        private bool showColorRules = true;
        private bool showPreview = true;
        private bool showAdvanced = false;

        public override VisualElement CreateInspectorGUI()
        {
            OnEnable();
            serializedObject.Update();

            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Blendshape Markdown"));

            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(BlendshapeMarkdownData));
            if (supportBanner != null)
            {
                root.Add(supportBanner);
            }

            root.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Blendshape Markdown is a configuration component. Add it to a renderer, define heading and color rules, and the native SkinnedMeshRenderer inspector will show grouped blendshape sections instead of one flat list.",
                YUCPUIToolkitHelper.MessageType.Info));

            var warningContainer = new VisualElement();
            root.Add(warningContainer);

            var generalFoldout = new Foldout { value = showGeneral, text = "Inspector Integration" };
            generalFoldout.RegisterValueChangedCallback(evt => showGeneral = evt.newValue);
            generalFoldout.Add(BuildGeneralCard());
            root.Add(generalFoldout);
            YUCPUIToolkitHelper.AddSpacing(root, 6);

            var headingRulesFoldout = new Foldout { value = showHeadingRules, text = "Heading Rules" };
            headingRulesFoldout.RegisterValueChangedCallback(evt => showHeadingRules = evt.newValue);
            headingRulesFoldout.Add(BuildHeadingRulesCard());
            root.Add(headingRulesFoldout);
            YUCPUIToolkitHelper.AddSpacing(root, 6);

            var colorRulesFoldout = new Foldout { value = showColorRules, text = "Color Rules" };
            colorRulesFoldout.RegisterValueChangedCallback(evt => showColorRules = evt.newValue);
            colorRulesFoldout.Add(BuildColorRulesCard());
            root.Add(colorRulesFoldout);
            YUCPUIToolkitHelper.AddSpacing(root, 6);

            var previewFoldout = new Foldout { value = showPreview, text = "Preview" };
            previewFoldout.RegisterValueChangedCallback(evt => showPreview = evt.newValue);
            VisualElement previewContainer = BuildPreviewCard();
            previewFoldout.Add(previewContainer);
            root.Add(previewFoldout);
            YUCPUIToolkitHelper.AddSpacing(root, 6);

            var advancedFoldout = new Foldout { value = showAdvanced, text = "Advanced" };
            advancedFoldout.RegisterValueChangedCallback(evt => showAdvanced = evt.newValue);
            advancedFoldout.Add(BuildAdvancedCard());
            root.Add(advancedFoldout);

            root.schedule.Execute(() =>
            {
                if (target == null)
                {
                    return;
                }

                serializedObject.Update();
                UpdateWarnings(warningContainer);
                UpdatePreview(previewContainer);
            }).Every(250);

            UpdateWarnings(warningContainer);
            UpdatePreview(previewContainer);

            return root;
        }

        private void OnEnable()
        {
            data = (BlendshapeMarkdownData)target;

            targetRendererProp = serializedObject.FindProperty("targetRenderer");
            enableNativeInspectorIntegrationProp = serializedObject.FindProperty("enableNativeInspectorIntegration");
            replaceDefaultBlendshapeListProp = serializedObject.FindProperty("replaceDefaultBlendshapeList");
            showSearchBarProp = serializedObject.FindProperty("showSearchBar");
            showBlendshapeCountsProp = serializedObject.FindProperty("showBlendshapeCounts");
            presetProp = serializedObject.FindProperty("preset");
            useSlashAsPathSeparatorProp = serializedObject.FindProperty("useSlashAsPathSeparator");
            showUngroupedBlendshapesProp = serializedObject.FindProperty("showUngroupedBlendshapes");
            ungroupedSectionTitleProp = serializedObject.FindProperty("ungroupedSectionTitle");
            topLevelAutoGroupPrefixProp = serializedObject.FindProperty("topLevelAutoGroupPrefix");
            topLevelAutoGroupTitleProp = serializedObject.FindProperty("topLevelAutoGroupTitle");
            headingRulesProp = serializedObject.FindProperty("headingRules");
            expandTopLevelByDefaultProp = serializedObject.FindProperty("expandTopLevelByDefault");
            expandNestedByDefaultProp = serializedObject.FindProperty("expandNestedByDefault");
            colorRulesProp = serializedObject.FindProperty("colorRules");
            debugLoggingProp = serializedObject.FindProperty("debugLogging");

            LoadFoldoutStates();
        }

        private void OnDisable()
        {
            SaveFoldoutStates();
        }

        private VisualElement BuildGeneralCard()
        {
            var card = YUCPUIToolkitHelper.CreateCard("Renderer & Native Inspector", "Choose the target renderer and how the native inspector should behave.");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            content.Add(CreateLabeledRow("Target Renderer", CustomUI.CreateNativeField(targetRendererProp, true), 130));
            content.Add(CreateLabeledRow("Enable Native Patch", CustomUI.CreateNativeField(enableNativeInspectorIntegrationProp, true), 130));
            content.Add(CreateLabeledRow("Replace Default List", CustomUI.CreateNativeField(replaceDefaultBlendshapeListProp, true), 130));
            content.Add(CreateLabeledRow("Show Search Bar", CustomUI.CreateNativeField(showSearchBarProp, true), 130));
            content.Add(CreateLabeledRow("Show Section Counts", CustomUI.CreateNativeField(showBlendshapeCountsProp, true), 130));

            content.Add(YUCPUIToolkitHelper.CreateDivider());

            content.Add(CreateLabeledRow("Preset", CustomUI.CreateNativeField(presetProp, true), 130));

            var actionRow = CustomUI.CreateRow();
            var applyPresetButton = YUCPUIToolkitHelper.CreateButton("Apply Preset Rules", () =>
            {
                ApplySelectedPresetRules();
                serializedObject.Update();
            }, YUCPUIToolkitHelper.ButtonVariant.Secondary);

            var appendPresetButton = YUCPUIToolkitHelper.CreateButton("Append Preset Rules", () =>
            {
                AppendSelectedPresetRules();
                serializedObject.Update();
            }, YUCPUIToolkitHelper.ButtonVariant.Ghost);

            actionRow.Add(applyPresetButton);
            actionRow.Add(appendPresetButton);
            content.Add(actionRow);

            content.Add(CreateLabeledRow("Use / as Path Splitter", CustomUI.CreateNativeField(useSlashAsPathSeparatorProp, true), 130));
            content.Add(CreateLabeledRow("Group Ungrouped Items", CustomUI.CreateNativeField(showUngroupedBlendshapesProp, true), 130));
            content.Add(CreateLabeledRow("Ungrouped Label", CustomUI.CreateNativeField(ungroupedSectionTitleProp, true), 130));
            content.Add(CreateLabeledRow("Auto Group Prefix", CustomUI.CreateNativeField(topLevelAutoGroupPrefixProp, true), 130));
            content.Add(CreateLabeledRow("Auto Group Label", CustomUI.CreateNativeField(topLevelAutoGroupTitleProp, true), 130));
            content.Add(CreateLabeledRow("Expand Top Level", CustomUI.CreateNativeField(expandTopLevelByDefaultProp, true), 130));
            content.Add(CreateLabeledRow("Expand Nested", CustomUI.CreateNativeField(expandNestedByDefaultProp, true), 130));

            return card;
        }

        private VisualElement BuildHeadingRulesCard()
        {
            var card = YUCPUIToolkitHelper.CreateCard("Section Detection Rules", "Blendshape names are checked top-to-bottom until one rule matches.");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            content.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Examples:\n• # Title\n• ==Body/Head==\n• |---Section---|\n• ||-------Nested-------||\n\nUse Prefix Token or Wrapped Token for the friendly setup. Only use Raw Regex when the easy rules cannot express your pattern.",
                YUCPUIToolkitHelper.MessageType.None));

            var propertyField = new PropertyField(headingRulesProp, "Heading Rules");
            propertyField.Bind(serializedObject);
            propertyField.AddToClassList("yucp-field-input");
            content.Add(propertyField);

            return card;
        }

        private VisualElement BuildColorRulesCard()
        {
            var card = YUCPUIToolkitHelper.CreateCard("Section Colors", "Style section headers similarly to Pretty Hierarchy using simple match rules.");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            content.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Color rules match either the section title or the full parsed path. The first enabled rule that matches wins.",
                YUCPUIToolkitHelper.MessageType.None));

            var propertyField = new PropertyField(colorRulesProp, "Color Rules");
            propertyField.Bind(serializedObject);
            propertyField.AddToClassList("yucp-field-input");
            content.Add(propertyField);

            return card;
        }

        private VisualElement BuildPreviewCard()
        {
            var card = YUCPUIToolkitHelper.CreateCard("Parsed Section Preview", "Live summary of how the current rules will organize this renderer.");
            return YUCPUIToolkitHelper.GetCardContent(card);
        }

        private VisualElement BuildAdvancedCard()
        {
            var card = YUCPUIToolkitHelper.CreateCard("Advanced Options", "Only touch these when the default setup is not enough.");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            content.Add(CreateLabeledRow("Debug Logging", CustomUI.CreateNativeField(debugLoggingProp, true), 130));
            content.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Regex rules are protected with a timeout so a bad pattern does not lock the editor. If a regex silently fails, check the pattern first.",
                YUCPUIToolkitHelper.MessageType.Warning));

            return card;
        }

        private void UpdateWarnings(VisualElement warningContainer)
        {
            warningContainer.Clear();

            SkinnedMeshRenderer renderer = data.GetTargetRenderer();
            if (renderer == null)
            {
                warningContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Assign a SkinnedMeshRenderer, or place this component on the same GameObject as the target renderer.",
                    YUCPUIToolkitHelper.MessageType.Warning));
                return;
            }

            if (renderer.sharedMesh == null)
            {
                warningContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "The target renderer does not have a shared mesh assigned yet.",
                    YUCPUIToolkitHelper.MessageType.Warning));
                return;
            }

            if (renderer.sharedMesh.blendShapeCount == 0)
            {
                warningContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "The target renderer's mesh does not contain any blendshapes.",
                    YUCPUIToolkitHelper.MessageType.Warning));
            }

            if (CountConfigsForRenderer(renderer) > 1)
            {
                warningContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Multiple Blendshape Markdown components currently target this renderer. The native renderer inspector will use the first enabled match it finds.",
                    YUCPUIToolkitHelper.MessageType.Warning));
            }
        }

        private void UpdatePreview(VisualElement previewContent)
        {
            previewContent.Clear();

            SkinnedMeshRenderer renderer = data.GetTargetRenderer();
            if (renderer == null || renderer.sharedMesh == null)
            {
                previewContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Assign a valid target renderer to preview the parsed hierarchy.",
                    YUCPUIToolkitHelper.MessageType.Info));
                return;
            }

            BlendshapeMarkdownDocument document = BlendshapeMarkdownParser.Parse(renderer, data);
            previewContent.Add(CreateReadOnlyRow("Renderer", renderer.name));
            previewContent.Add(CreateReadOnlyRow("Mesh", renderer.sharedMesh.name));
            previewContent.Add(CreateReadOnlyRow("Blendshape Count", renderer.sharedMesh.blendShapeCount.ToString()));
            previewContent.Add(CreateReadOnlyRow("Matched Headings", document.HeadingCount.ToString()));

            if (document.HeadingCount == 0)
            {
                previewContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "No heading rules matched this mesh yet. Add or adjust rules until your section markers are detected.",
                    YUCPUIToolkitHelper.MessageType.Warning));
                return;
            }

            previewContent.Add(YUCPUIToolkitHelper.CreateDivider());
            previewContent.Add(CustomUI.CreateLabel("First Sections", 0, true));

            int shownCount = 0;
            AddPreviewEntries(previewContent, document.Root, ref shownCount);
        }

        private void AddPreviewEntries(VisualElement parent, BlendshapeMarkdownSection section, ref int shownCount)
        {
            if (shownCount >= 8)
            {
                return;
            }

            for (int index = 0; index < section.Children.Count; index++)
            {
                if (shownCount >= 8)
                {
                    return;
                }

                if (section.Children[index] is BlendshapeMarkdownSection childSection)
                {
                    var row = CustomUI.CreateRow();
                    row.style.marginLeft = Math.Max(0, childSection.Depth - 1) * 12;
                    row.Add(CustomUI.CreateLabel(childSection.FullPath, 0));
                    parent.Add(row);
                    shownCount++;
                    AddPreviewEntries(parent, childSection, ref shownCount);
                }
            }
        }

        private VisualElement CreateLabeledRow(string label, VisualElement field, float minWidth)
        {
            var row = CustomUI.CreateRow(Justify.SpaceBetween);
            row.Add(CustomUI.CreateLabel(label, minWidth));
            field.style.flexGrow = 1;
            row.Add(field);
            return row;
        }

        private VisualElement CreateReadOnlyRow(string label, string value)
        {
            var row = CustomUI.CreateRow(Justify.SpaceBetween);
            row.Add(CustomUI.CreateLabel(label, 130));
            var valueLabel = new Label(value);
            valueLabel.style.color = new Color(1f, 1f, 1f, 0.85f);
            valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            valueLabel.style.flexGrow = 1;
            row.Add(valueLabel);
            return row;
        }

        private void ApplySelectedPresetRules()
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(data, "Apply Blendshape Markdown Preset");
            data.ApplyPresetRules((BlendshapeMarkdownPreset)presetProp.enumValueIndex);
            EditorUtility.SetDirty(data);
        }

        private void AppendSelectedPresetRules()
        {
            BlendshapeMarkdownPreset preset = (BlendshapeMarkdownPreset)presetProp.enumValueIndex;

            switch (preset)
            {
                case BlendshapeMarkdownPreset.MarkdownHashes:
                    AddHeadingRule(new BlendshapeMarkdownHeadingRule
                    {
                        name = "Markdown Hashes",
                        mode = BlendshapeMarkdownHeadingRuleMode.PrefixToken,
                        repeatToken = "#",
                        requireWhitespaceAfterPrefix = true
                    });
                    break;

                case BlendshapeMarkdownPreset.EqualsWrapped:
                    AddHeadingRule(new BlendshapeMarkdownHeadingRule
                    {
                        name = "Equals Wrapper",
                        mode = BlendshapeMarkdownHeadingRuleMode.WrappedToken,
                        repeatToken = "="
                    });
                    break;

                case BlendshapeMarkdownPreset.PipeDashWrapped:
                    AddHeadingRule(new BlendshapeMarkdownHeadingRule
                    {
                        name = "Pipe Dash Wrapper",
                        mode = BlendshapeMarkdownHeadingRuleMode.WrappedToken,
                        repeatToken = "-",
                        leftWrapper = "|",
                        rightWrapper = "|"
                    });
                    break;

                case BlendshapeMarkdownPreset.DoublePipeDashWrapped:
                    AddHeadingRule(new BlendshapeMarkdownHeadingRule
                    {
                        name = "Double Pipe Dash Wrapper",
                        mode = BlendshapeMarkdownHeadingRuleMode.WrappedToken,
                        repeatToken = "-",
                        leftWrapper = "||",
                        rightWrapper = "||"
                    });
                    break;

                case BlendshapeMarkdownPreset.MixedConvenience:
                    AppendPresetRule(BlendshapeMarkdownPreset.MarkdownHashes);
                    AppendPresetRule(BlendshapeMarkdownPreset.EqualsWrapped);
                    AppendPresetRule(BlendshapeMarkdownPreset.PipeDashWrapped);
                    AppendPresetRule(BlendshapeMarkdownPreset.DoublePipeDashWrapped);
                    break;
            }
        }

        private void AppendPresetRule(BlendshapeMarkdownPreset preset)
        {
            BlendshapeMarkdownPreset currentPreset = (BlendshapeMarkdownPreset)presetProp.enumValueIndex;
            presetProp.enumValueIndex = (int)preset;
            AppendSelectedPresetRules();
            presetProp.enumValueIndex = (int)currentPreset;
        }

        private void AddHeadingRule(BlendshapeMarkdownHeadingRule rule)
        {
            serializedObject.Update();
            int index = headingRulesProp.arraySize;
            headingRulesProp.InsertArrayElementAtIndex(index);
            SerializedProperty element = headingRulesProp.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("name").stringValue = rule.name;
            element.FindPropertyRelative("enabled").boolValue = rule.enabled;
            element.FindPropertyRelative("mode").enumValueIndex = (int)rule.mode;
            element.FindPropertyRelative("repeatToken").stringValue = rule.repeatToken;
            element.FindPropertyRelative("leftWrapper").stringValue = rule.leftWrapper;
            element.FindPropertyRelative("rightWrapper").stringValue = rule.rightWrapper;
            element.FindPropertyRelative("requireWhitespaceAfterPrefix").boolValue = rule.requireWhitespaceAfterPrefix;
            element.FindPropertyRelative("ignoreCase").boolValue = rule.ignoreCase;
            element.FindPropertyRelative("rawRegex").stringValue = rule.rawRegex;
            element.FindPropertyRelative("rawRegexDepthGroup").intValue = rule.rawRegexDepthGroup;
            element.FindPropertyRelative("rawRegexTitleGroup").intValue = rule.rawRegexTitleGroup;
            element.FindPropertyRelative("trimTitleWhitespace").boolValue = rule.trimTitleWhitespace;
            serializedObject.ApplyModifiedProperties();
        }

        private int CountConfigsForRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
            {
                return 0;
            }

            int count = 0;
            BlendshapeMarkdownData[] allConfigs = UnityEngine.Object.FindObjectsByType<BlendshapeMarkdownData>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < allConfigs.Length; index++)
            {
                if (allConfigs[index] != null && allConfigs[index].TargetsRenderer(renderer))
                {
                    count++;
                }
            }

            return count;
        }

        private void LoadFoldoutStates()
        {
            int id = data != null ? data.GetInstanceID() : 0;
            showGeneral = SessionState.GetBool($"BlendshapeMarkdown_General_{id}", true);
            showHeadingRules = SessionState.GetBool($"BlendshapeMarkdown_HeadingRules_{id}", true);
            showColorRules = SessionState.GetBool($"BlendshapeMarkdown_ColorRules_{id}", true);
            showPreview = SessionState.GetBool($"BlendshapeMarkdown_Preview_{id}", true);
            showAdvanced = SessionState.GetBool($"BlendshapeMarkdown_Advanced_{id}", false);
        }

        private void SaveFoldoutStates()
        {
            if (data == null)
            {
                return;
            }

            int id = data.GetInstanceID();
            SessionState.SetBool($"BlendshapeMarkdown_General_{id}", showGeneral);
            SessionState.SetBool($"BlendshapeMarkdown_HeadingRules_{id}", showHeadingRules);
            SessionState.SetBool($"BlendshapeMarkdown_ColorRules_{id}", showColorRules);
            SessionState.SetBool($"BlendshapeMarkdown_Preview_{id}", showPreview);
            SessionState.SetBool($"BlendshapeMarkdown_Advanced_{id}", showAdvanced);
        }
    }
}
