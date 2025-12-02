using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.Components.Resources;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(ParameterToggleData))]
    [CanEditMultipleObjects]
    public class ParameterToggleDataEditor : UnityEditor.Editor
    {
        private ParameterToggleData data;
        private SerializedProperty conditionGroupsProp;
        private SerializedProperty stateProp;
        private SerializedProperty hasTransitionProp;
        private SerializedProperty transitionStateInProp;
        private SerializedProperty transitionTimeInProp;
        private SerializedProperty transitionStateOutProp;
        private SerializedProperty transitionTimeOutProp;
        private SerializedProperty simpleOutTransitionProp;
        private SerializedProperty expandIntoTransitionProp;
        private SerializedProperty separateLocalProp;
        private SerializedProperty localStateProp;
        private SerializedProperty localTransitionStateInProp;
        private SerializedProperty localTransitionStateOutProp;
        private SerializedProperty localTransitionTimeInProp;
        private SerializedProperty localTransitionTimeOutProp;
        private SerializedProperty savedProp;
        private SerializedProperty defaultOnProp;
        private SerializedProperty hasExitTimeProp;
        private SerializedProperty holdButtonProp;
        private SerializedProperty sliderProp;
        private SerializedProperty defaultSliderValueProp;
        private SerializedProperty sliderInactiveAtZeroProp;
        private SerializedProperty enableExclusiveTagProp;
        private SerializedProperty exclusiveTagProp;
        private SerializedProperty exclusiveOffStateProp;
        private SerializedProperty securityEnabledProp;
        private SerializedProperty enableIconProp;
        private SerializedProperty iconProp;
        private SerializedProperty useGlobalParamProp;
        private SerializedProperty globalParamProp;
        private SerializedProperty enableDriveGlobalParamProp;
        private SerializedProperty driveGlobalParamProp;
        private SerializedProperty invertRestLogicProp;
        private SerializedProperty paramOverrideProp;

        private VisualElement transitionContainer;
        private VisualElement localContainer;
        private VisualElement sliderOptionsContainer;
        private VisualElement exclusiveContainer;
        private VisualElement iconContainer;
        private VisualElement useGlobalContainer;
        private VisualElement driveGlobalContainer;

        private void OnEnable()
        {
            data = (ParameterToggleData)target;
            
            conditionGroupsProp = serializedObject.FindProperty("conditionGroups");
            stateProp = serializedObject.FindProperty("state");
            hasTransitionProp = serializedObject.FindProperty("hasTransition");
            transitionStateInProp = serializedObject.FindProperty("transitionStateIn");
            transitionTimeInProp = serializedObject.FindProperty("transitionTimeIn");
            transitionStateOutProp = serializedObject.FindProperty("transitionStateOut");
            transitionTimeOutProp = serializedObject.FindProperty("transitionTimeOut");
            simpleOutTransitionProp = serializedObject.FindProperty("simpleOutTransition");
            expandIntoTransitionProp = serializedObject.FindProperty("expandIntoTransition");
            separateLocalProp = serializedObject.FindProperty("separateLocal");
            localStateProp = serializedObject.FindProperty("localState");
            localTransitionStateInProp = serializedObject.FindProperty("localTransitionStateIn");
            localTransitionStateOutProp = serializedObject.FindProperty("localTransitionStateOut");
            localTransitionTimeInProp = serializedObject.FindProperty("localTransitionTimeIn");
            localTransitionTimeOutProp = serializedObject.FindProperty("localTransitionTimeOut");
            savedProp = serializedObject.FindProperty("saved");
            defaultOnProp = serializedObject.FindProperty("defaultOn");
            hasExitTimeProp = serializedObject.FindProperty("hasExitTime");
            holdButtonProp = serializedObject.FindProperty("holdButton");
            sliderProp = serializedObject.FindProperty("slider");
            defaultSliderValueProp = serializedObject.FindProperty("defaultSliderValue");
            sliderInactiveAtZeroProp = serializedObject.FindProperty("sliderInactiveAtZero");
            enableExclusiveTagProp = serializedObject.FindProperty("enableExclusiveTag");
            exclusiveTagProp = serializedObject.FindProperty("exclusiveTag");
            exclusiveOffStateProp = serializedObject.FindProperty("exclusiveOffState");
            securityEnabledProp = serializedObject.FindProperty("securityEnabled");
            enableIconProp = serializedObject.FindProperty("enableIcon");
            iconProp = serializedObject.FindProperty("icon");
            useGlobalParamProp = serializedObject.FindProperty("useGlobalParam");
            globalParamProp = serializedObject.FindProperty("globalParam");
            enableDriveGlobalParamProp = serializedObject.FindProperty("enableDriveGlobalParam");
            driveGlobalParamProp = serializedObject.FindProperty("driveGlobalParam");
            invertRestLogicProp = serializedObject.FindProperty("invertRestLogic");
            paramOverrideProp = serializedObject.FindProperty("paramOverride");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Parameter Toggle"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(ParameterToggleData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(ParameterToggleData));
            if (supportBanner != null) root.Add(supportBanner);
            
            root.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Creates a VRCFury toggle controlled by Animator parameter conditions instead of a menu item. " +
                "Condition groups are OR'd together (any group can activate). Within each group, conditions are combined based on the Group Operator (AND/OR).",
                YUCPUIToolkitHelper.MessageType.Info));
            
            // Parameter Conditions Card
            var conditionsCard = YUCPUIToolkitHelper.CreateCard("Parameter Conditions", "Define when the toggle activates");
            var conditionsContent = YUCPUIToolkitHelper.GetCardContent(conditionsCard);
            
            conditionsContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Groups are OR'd together (any group can activate the toggle). Within each group, conditions are combined using the Group Operator (AND/OR).",
                YUCPUIToolkitHelper.MessageType.Info));
            
            // Use PropertyField - Unity handles array UI automatically
            var conditionGroupsField = new PropertyField(conditionGroupsProp, "Condition Groups");
            conditionsContent.Add(conditionGroupsField);
            
            root.Add(conditionsCard);
            
            // Toggle Actions Card
            var actionsCard = YUCPUIToolkitHelper.CreateCard("Toggle Actions", "Configure what happens when toggle is ON");
            var actionsContent = YUCPUIToolkitHelper.GetCardContent(actionsCard);
            
            var stateFoldout = YUCPUIToolkitHelper.CreateFoldout("Main Action (when toggle is ON)", true);
            stateFoldout.Add(new PropertyField(stateProp));
            actionsContent.Add(stateFoldout);
            
            var hasTransitionField = YUCPUIToolkitHelper.CreateField(hasTransitionProp, "Enable Transitions");
            hasTransitionField.RegisterValueChangeCallback(evt => UpdateTransitionContainer());
            actionsContent.Add(hasTransitionField);
            
            transitionContainer = new VisualElement();
            transitionContainer.name = "transition-container";
            actionsContent.Add(transitionContainer);
            
            root.Add(actionsCard);
            
            // Local/Remote Separation Card
            var localCard = YUCPUIToolkitHelper.CreateCard("Local/Remote Separation", "Different actions for local vs remote players");
            var localContent = YUCPUIToolkitHelper.GetCardContent(localCard);
            
            var separateLocalField = YUCPUIToolkitHelper.CreateField(separateLocalProp, "Separate Local State");
            separateLocalField.RegisterValueChangeCallback(evt => UpdateLocalContainer());
            localContent.Add(separateLocalField);
            
            localContainer = new VisualElement();
            localContainer.name = "local-container";
            localContent.Add(localContainer);
            
            root.Add(localCard);
            
            // Basic Settings Card
            var basicCard = YUCPUIToolkitHelper.CreateCard("Basic Settings", "Toggle behavior settings");
            var basicContent = YUCPUIToolkitHelper.GetCardContent(basicCard);
            basicContent.Add(YUCPUIToolkitHelper.CreateField(savedProp, "Saved Between Worlds"));
            basicContent.Add(YUCPUIToolkitHelper.CreateField(defaultOnProp, "Default On"));
            basicContent.Add(YUCPUIToolkitHelper.CreateField(hasExitTimeProp, "Run Animation to Completion"));
            basicContent.Add(YUCPUIToolkitHelper.CreateField(holdButtonProp, "Hold Button"));
            root.Add(basicCard);
            
            // Slider Settings Card
            var sliderCard = YUCPUIToolkitHelper.CreateCard("Slider Settings", "Configure slider behavior");
            var sliderContent = YUCPUIToolkitHelper.GetCardContent(sliderCard);
            var sliderField = YUCPUIToolkitHelper.CreateField(sliderProp, "Use Slider (Radial)");
            sliderField.RegisterValueChangeCallback(evt => UpdateSliderOptionsContainer());
            sliderContent.Add(sliderField);
            
            sliderOptionsContainer = new VisualElement();
            sliderOptionsContainer.name = "slider-options-container";
            sliderContent.Add(sliderOptionsContainer);
            
            root.Add(sliderCard);
            
            // Exclusive Tags Card
            var exclusiveCard = YUCPUIToolkitHelper.CreateCard("Exclusive Tags", "Mutually exclusive toggle groups");
            var exclusiveContent = YUCPUIToolkitHelper.GetCardContent(exclusiveCard);
            var enableExclusiveField = YUCPUIToolkitHelper.CreateField(enableExclusiveTagProp, "Enable Exclusive Tags");
            enableExclusiveField.RegisterValueChangeCallback(evt => UpdateExclusiveContainer());
            exclusiveContent.Add(enableExclusiveField);
            
            exclusiveContainer = new VisualElement();
            exclusiveContainer.name = "exclusive-container";
            exclusiveContent.Add(exclusiveContainer);
            
            root.Add(exclusiveCard);
            
            // Security Card
            var securityCard = YUCPUIToolkitHelper.CreateCard("Security", "Protect toggle with security pin");
            var securityContent = YUCPUIToolkitHelper.GetCardContent(securityCard);
            securityContent.Add(YUCPUIToolkitHelper.CreateField(securityEnabledProp, "Protect with Security"));
            securityContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Requires a Security Lock component on the avatar.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(securityCard);
            
            // Icon Card
            var iconCard = YUCPUIToolkitHelper.CreateCard("Icon", "Custom menu icon");
            var iconContent = YUCPUIToolkitHelper.GetCardContent(iconCard);
            var enableIconField = YUCPUIToolkitHelper.CreateField(enableIconProp, "Enable Custom Icon");
            enableIconField.RegisterValueChangeCallback(evt => UpdateIconContainer());
            iconContent.Add(enableIconField);
            
            iconContainer = new VisualElement();
            iconContainer.name = "icon-container";
            iconContent.Add(iconContainer);
            
            root.Add(iconCard);
            
            // Global Parameters Card
            var globalParamCard = YUCPUIToolkitHelper.CreateCard("Global Parameters", "Use or drive global parameters");
            var globalParamContent = YUCPUIToolkitHelper.GetCardContent(globalParamCard);
            var useGlobalField = YUCPUIToolkitHelper.CreateField(useGlobalParamProp, "Use Global Parameter");
            useGlobalField.RegisterValueChangeCallback(evt => UpdateUseGlobalContainer());
            globalParamContent.Add(useGlobalField);
            
            useGlobalContainer = new VisualElement();
            useGlobalContainer.name = "use-global-container";
            globalParamContent.Add(useGlobalContainer);
            
            var enableDriveGlobalField = YUCPUIToolkitHelper.CreateField(enableDriveGlobalParamProp, "Drive Global Parameter");
            enableDriveGlobalField.RegisterValueChangeCallback(evt => UpdateDriveGlobalContainer());
            globalParamContent.Add(enableDriveGlobalField);
            
            driveGlobalContainer = new VisualElement();
            driveGlobalContainer.name = "drive-global-container";
            globalParamContent.Add(driveGlobalContainer);
            
            root.Add(globalParamCard);
            
            // Advanced Card
            var advancedCard = YUCPUIToolkitHelper.CreateCard("Advanced", "Advanced settings");
            var advancedContent = YUCPUIToolkitHelper.GetCardContent(advancedCard);
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(invertRestLogicProp, "Invert Rest Logic"));
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(paramOverrideProp, "Parameter Override"));
            advancedContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Leave empty for auto-generated parameter name.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(advancedCard);
            
            // Initialize containers
            UpdateTransitionContainer();
            UpdateLocalContainer();
            UpdateSliderOptionsContainer();
            UpdateExclusiveContainer();
            UpdateIconContainer();
            UpdateUseGlobalContainer();
            UpdateDriveGlobalContainer();
            
            return root;
        }

        private void UpdateTransitionContainer()
        {
            if (transitionContainer == null) return;
            
            transitionContainer.Clear();
            if (!hasTransitionProp.boolValue)
            {
                transitionContainer.style.display = DisplayStyle.None;
                return;
            }
            
            transitionContainer.style.display = DisplayStyle.Flex;
            
            var transitionInFoldout = YUCPUIToolkitHelper.CreateFoldout("Transition In", true);
            transitionInFoldout.Add(new PropertyField(transitionStateInProp));
            transitionInFoldout.Add(YUCPUIToolkitHelper.CreateField(transitionTimeInProp, "Duration (seconds)"));
            transitionContainer.Add(transitionInFoldout);
            
            transitionContainer.Add(YUCPUIToolkitHelper.CreateField(simpleOutTransitionProp, "Transition Out is reverse of Transition In"));
            
            if (!simpleOutTransitionProp.boolValue)
            {
                var transitionOutFoldout = YUCPUIToolkitHelper.CreateFoldout("Transition Out", true);
                transitionOutFoldout.Add(new PropertyField(transitionStateOutProp));
                transitionOutFoldout.Add(YUCPUIToolkitHelper.CreateField(transitionTimeOutProp, "Duration (seconds)"));
                transitionContainer.Add(transitionOutFoldout);
            }
            else
            {
                transitionContainer.Add(YUCPUIToolkitHelper.CreateField(transitionTimeOutProp, "Transition Out Duration (seconds)"));
            }
            
            transitionContainer.Add(YUCPUIToolkitHelper.CreateField(expandIntoTransitionProp, "Extend object enabling and material settings into transitions"));
        }

        private void UpdateLocalContainer()
        {
            if (localContainer == null) return;
            
            localContainer.Clear();
            if (!separateLocalProp.boolValue)
            {
                localContainer.style.display = DisplayStyle.None;
                return;
            }
            
            localContainer.style.display = DisplayStyle.Flex;
            
            var localStateFoldout = YUCPUIToolkitHelper.CreateFoldout("Local State", true);
            localStateFoldout.Add(new PropertyField(localStateProp));
            localContainer.Add(localStateFoldout);
            
            if (hasTransitionProp.boolValue)
            {
                var localTransitionInFoldout = YUCPUIToolkitHelper.CreateFoldout("Local Transition In", true);
                localTransitionInFoldout.Add(new PropertyField(localTransitionStateInProp));
                localTransitionInFoldout.Add(YUCPUIToolkitHelper.CreateField(localTransitionTimeInProp, "Duration (seconds)"));
                localContainer.Add(localTransitionInFoldout);
                
                var localTransitionOutFoldout = YUCPUIToolkitHelper.CreateFoldout("Local Transition Out", true);
                localTransitionOutFoldout.Add(new PropertyField(localTransitionStateOutProp));
                localTransitionOutFoldout.Add(YUCPUIToolkitHelper.CreateField(localTransitionTimeOutProp, "Duration (seconds)"));
                localContainer.Add(localTransitionOutFoldout);
            }
        }

        private void UpdateSliderOptionsContainer()
        {
            if (sliderOptionsContainer == null) return;
            
            sliderOptionsContainer.Clear();
            if (!sliderProp.boolValue)
            {
                sliderOptionsContainer.style.display = DisplayStyle.None;
                return;
            }
            
            sliderOptionsContainer.style.display = DisplayStyle.Flex;
            sliderOptionsContainer.Add(YUCPUIToolkitHelper.CreateField(defaultSliderValueProp, "Default %"));
            sliderOptionsContainer.Add(YUCPUIToolkitHelper.CreateField(sliderInactiveAtZeroProp, "Passthrough at 0%"));
            sliderOptionsContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "When checked, the slider will be bypassed when set to 0%, meaning that it will not control any properties at all, allowing the properties to resume being controlled by some other toggle or animator layer.",
                YUCPUIToolkitHelper.MessageType.Info));
        }

        private void UpdateExclusiveContainer()
        {
            if (exclusiveContainer == null) return;
            
            exclusiveContainer.Clear();
            if (!enableExclusiveTagProp.boolValue)
            {
                exclusiveContainer.style.display = DisplayStyle.None;
                return;
            }
            
            exclusiveContainer.style.display = DisplayStyle.Flex;
            exclusiveContainer.Add(YUCPUIToolkitHelper.CreateField(exclusiveTagProp, "Exclusive Tags"));
            exclusiveContainer.Add(YUCPUIToolkitHelper.CreateHelpBox("Comma-separated tags. Toggles sharing tags are mutually exclusive.", YUCPUIToolkitHelper.MessageType.Info));
            exclusiveContainer.Add(YUCPUIToolkitHelper.CreateField(exclusiveOffStateProp, "This is Exclusive Off State"));
            exclusiveContainer.Add(YUCPUIToolkitHelper.CreateHelpBox("Activates when all toggles with matching tags are off.", YUCPUIToolkitHelper.MessageType.Info));
        }

        private void UpdateIconContainer()
        {
            if (iconContainer == null) return;
            
            iconContainer.Clear();
            if (!enableIconProp.boolValue)
            {
                iconContainer.style.display = DisplayStyle.None;
                return;
            }
            
            iconContainer.style.display = DisplayStyle.Flex;
            iconContainer.Add(YUCPUIToolkitHelper.CreateField(iconProp, "Menu Icon"));
        }

        private void UpdateUseGlobalContainer()
        {
            if (useGlobalContainer == null) return;
            
            useGlobalContainer.Clear();
            if (!useGlobalParamProp.boolValue)
            {
                useGlobalContainer.style.display = DisplayStyle.None;
                return;
            }
            
            useGlobalContainer.style.display = DisplayStyle.Flex;
            useGlobalContainer.Add(YUCPUIToolkitHelper.CreateField(globalParamProp, "Global Parameter"));
            useGlobalContainer.Add(YUCPUIToolkitHelper.CreateHelpBox("Name of the global parameter to use instead of creating a new one.", YUCPUIToolkitHelper.MessageType.Info));
        }

        private void UpdateDriveGlobalContainer()
        {
            if (driveGlobalContainer == null) return;
            
            driveGlobalContainer.Clear();
            if (!enableDriveGlobalParamProp.boolValue)
            {
                driveGlobalContainer.style.display = DisplayStyle.None;
                return;
            }
            
            driveGlobalContainer.style.display = DisplayStyle.Flex;
            driveGlobalContainer.Add(YUCPUIToolkitHelper.CreateField(driveGlobalParamProp, "Drive Global Param"));
            driveGlobalContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Warning: Drive Global Param is an advanced feature. The driven parameter should not be placed in a menu or controlled by any other driver or shared with any other toggle. It should only be used as an input to manually-created state transitions in your avatar.",
                YUCPUIToolkitHelper.MessageType.Warning));
        }
    }
}
