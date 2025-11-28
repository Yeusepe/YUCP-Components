using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor.UI
{
    /// <summary>
    /// Custom editor for Gesture Manager Input Emulator with input mapping configuration.
    /// Provides intuitive UI for mapping keyboard and controller inputs to Gesture Manager parameters.
    /// </summary>
    [CustomEditor(typeof(GestureManagerInputEmulator))]
    public class GestureManagerInputEmulatorEditor : UnityEditor.Editor
    {
        private GestureManagerInputEmulator data;
        private bool showAdvancedOptions = false;
        private bool showControllerSettings = false;
        private bool showBuildStats = false;
        private Vector2 scrollPosition;
        private int previousMappingsCount = -1;
        private GameObject previousGestureManager = null;
        private GestureManagerMode previousGestureManagerMode = (GestureManagerMode)(-1);
        private bool previousControllerConnected = false;

        private void OnEnable()
        {
            if (target is GestureManagerInputEmulator)
            {
                data = (GestureManagerInputEmulator)target;
                previousMappingsCount = data.inputMappings != null ? data.inputMappings.Count : -1;
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            data = (GestureManagerInputEmulator)target;
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Gesture Manager Input Emulator"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(GestureManagerInputEmulator));
            if (betaWarning != null) root.Add(betaWarning);
            
            root.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "You have to be clicked into Game view for the inputs to be detected (I know it is a Unity thing I am working on it)",
                YUCPUIToolkitHelper.MessageType.Info));
            
            // Gesture Manager Card
            var gestureManagerCard = YUCPUIToolkitHelper.CreateCard("Gesture Manager", "Configure Gesture Manager integration");
            var gestureManagerContent = YUCPUIToolkitHelper.GetCardContent(gestureManagerCard);
            gestureManagerContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("gestureManager"), "Gesture Manager Component"));
            gestureManagerContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("gestureManagerMode"), "Operation Mode"));
            
            var gestureManagerHelp = new VisualElement();
            gestureManagerHelp.name = "gesture-manager-help";
            gestureManagerContent.Add(gestureManagerHelp);
            root.Add(gestureManagerCard);
            
            // General Settings Card
            var generalCard = YUCPUIToolkitHelper.CreateCard("General Settings", "Configure general input emulation settings");
            var generalContent = YUCPUIToolkitHelper.GetCardContent(generalCard);
            generalContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("enableInputEmulation"), "Enable Input Emulation"));
            generalContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugMode"), "Debug Mode"));
            root.Add(generalCard);
            
            // Input Mappings Section
            YUCPUIToolkitHelper.AddSpacing(root, 10);
            var mappingsHeader = new Label("Input Mappings");
            mappingsHeader.style.fontSize = 13;
            mappingsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            mappingsHeader.style.marginBottom = 5;
            root.Add(mappingsHeader);
            
            var mappingsContainer = new VisualElement();
            mappingsContainer.name = "mappings-container";
            root.Add(mappingsContainer);
            
            // Initialize mappings container with existing mappings
            previousMappingsCount = data.inputMappings != null ? data.inputMappings.Count : 0;
            UpdateMappingsContainer(mappingsContainer);
            
            // Add/Remove Mapping Buttons
            var mappingButtons = new VisualElement();
            mappingButtons.style.flexDirection = FlexDirection.Row;
            mappingButtons.style.marginBottom = 10;
            
            var addButton = YUCPUIToolkitHelper.CreateButton("Add Mapping", () =>
            {
                var newMapping = new InputMapping
                {
                    mappingName = $"Mapping {data.inputMappings.Count + 1}",
                    inputType = InputType.Keyboard,
                    keyboardKey = KeyCode.Space,
                    parameterName = "NewParameter",
                    parameterType = ParameterType.Bool,
                    activeValue = 1f,
                    inactiveValue = 0f,
                    enabled = true
                };
                data.AddInputMapping(newMapping);
                EditorUtility.SetDirty(data);
                UpdateMappingsContainer(mappingsContainer);
                previousMappingsCount = data.inputMappings.Count;
            }, YUCPUIToolkitHelper.ButtonVariant.Primary);
            addButton.style.height = 25;
            addButton.style.flexGrow = 1;
            addButton.style.marginRight = 5;
            mappingButtons.Add(addButton);
            
            var clearButton = YUCPUIToolkitHelper.CreateButton("Clear All", () =>
            {
                if (EditorUtility.DisplayDialog("Clear All Mappings", 
                    "Are you sure you want to remove all input mappings?", "Yes", "Cancel"))
                {
                    data.inputMappings.Clear();
                    EditorUtility.SetDirty(data);
                    UpdateMappingsContainer(mappingsContainer);
                    previousMappingsCount = 0;
                }
            }, YUCPUIToolkitHelper.ButtonVariant.Danger);
            clearButton.style.height = 25;
            clearButton.style.flexGrow = 1;
            clearButton.name = "clear-all-button";
            mappingButtons.Add(clearButton);
            root.Add(mappingButtons);
            
            // Controller Settings Foldout
            var controllerFoldout = YUCPUIToolkitHelper.CreateFoldout("Controller Settings", showControllerSettings);
            controllerFoldout.RegisterValueChangedCallback(evt => { showControllerSettings = evt.newValue; });
            
            var controllerStatus = new VisualElement();
            controllerStatus.name = "controller-status";
            controllerFoldout.Add(controllerStatus);
            
            controllerFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("controllerDeadzone"), "Controller Deadzone"));
            controllerFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("axisSensitivity"), "Axis Sensitivity"));
            root.Add(controllerFoldout);
            
            // Advanced Options Foldout
            var advancedFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced Options", showAdvancedOptions);
            advancedFoldout.RegisterValueChangedCallback(evt => { showAdvancedOptions = evt.newValue; });
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateHelpBox("Advanced options for debugging and fine-tuning input behavior.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(advancedFoldout);
            
            // Build Statistics Foldout
            var buildStatsFoldout = YUCPUIToolkitHelper.CreateFoldout("Build Statistics", showBuildStats);
            buildStatsFoldout.RegisterValueChangedCallback(evt => { showBuildStats = evt.newValue; });
            
            var activeMappingsField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("activeMappings"), "Active Mappings");
            activeMappingsField.SetEnabled(false);
            buildStatsFoldout.Add(activeMappingsField);
            
            var detectedTypeField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("detectedGestureManagerType"), "Gesture Manager Type");
            detectedTypeField.SetEnabled(false);
            buildStatsFoldout.Add(detectedTypeField);
            
            var detectedModeField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("detectedMode"), "Operation Mode");
            detectedModeField.SetEnabled(false);
            buildStatsFoldout.Add(detectedModeField);
            buildStatsFoldout.name = "build-stats-foldout";
            root.Add(buildStatsFoldout);
            
            // Runtime Status (conditional)
            var runtimeStatus = new VisualElement();
            runtimeStatus.name = "runtime-status";
            root.Add(runtimeStatus);
            
            // Initialize previous values
            previousGestureManager = data.gestureManager;
            previousGestureManagerMode = data.gestureManagerMode;
            previousControllerConnected = IsControllerConnected();
            
            // Initial population
            UpdateGestureManagerHelp(gestureManagerHelp);
            UpdateControllerStatus(controllerStatus);
            
            // Dynamic updates
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                // Update gesture manager help only when it changes
                if (data.gestureManager != previousGestureManager || data.gestureManagerMode != previousGestureManagerMode)
                {
                    UpdateGestureManagerHelp(gestureManagerHelp);
                    previousGestureManager = data.gestureManager;
                    previousGestureManagerMode = data.gestureManagerMode;
                }
                
                // Update mappings container only when count changes
                int currentMappingsCount = data.inputMappings != null ? data.inputMappings.Count : 0;
                if (currentMappingsCount != previousMappingsCount)
                {
                    UpdateMappingsContainer(mappingsContainer);
                    previousMappingsCount = currentMappingsCount;
                }
                
                // Update clear button visibility
                clearButton.style.display = (data.inputMappings != null && data.inputMappings.Count > 0) ? DisplayStyle.Flex : DisplayStyle.None;
                
                // Update controller status only when connection state changes
                bool controllerConnected = IsControllerConnected();
                if (controllerConnected != previousControllerConnected)
                {
                    UpdateControllerStatus(controllerStatus);
                    previousControllerConnected = controllerConnected;
                }
                
                // Update build stats visibility
                buildStatsFoldout.style.display = (data.activeMappings != null && data.activeMappings.Count > 0) ? DisplayStyle.Flex : DisplayStyle.None;
                
                // Update runtime status
                UpdateRuntimeStatus(runtimeStatus);
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateMappingsContainer(VisualElement container)
        {
            container.Clear();
            
            if (data.inputMappings == null || data.inputMappings.Count == 0)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("No input mappings configured. Click 'Add Mapping' to create your first mapping.", YUCPUIToolkitHelper.MessageType.Info));
                return;
            }
            
            // Use YUCPInputMappingEditor for each mapping
            for (int i = 0; i < data.inputMappings.Count; i++)
            {
                int index = i; // Capture for closure
                var mapping = data.inputMappings[index];
                
                var mappingEditor = YUCPUIToolkitHelper.CreateInputMappingEditor(
                    mapping,
                    () => {
                        EditorUtility.SetDirty(data);
                        serializedObject.Update();
                    },
                    () => {
                        data.RemoveInputMapping(index);
                        EditorUtility.SetDirty(data);
                        serializedObject.Update();
                        UpdateMappingsContainer(container);
                        previousMappingsCount = data.inputMappings.Count;
                    }
                );
                
                container.Add(mappingEditor);
            }
        }
        
        private void UpdateRuntimeStatus(VisualElement container)
        {
            container.Clear();
            
            if (!Application.isPlaying)
                return;
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            var inputHandler = FindObjectOfType<GestureManagerInputHandler>();
            if (inputHandler != null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Runtime Input Handler Active", YUCPUIToolkitHelper.MessageType.Info));
                
                var debugButtons = new VisualElement();
                debugButtons.style.flexDirection = FlexDirection.Row;
                debugButtons.style.marginBottom = 5;
                
                var enableDebugButton = YUCPUIToolkitHelper.CreateButton("Force Enable Debug Mode", () => inputHandler.ForceEnableDebugMode(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
                enableDebugButton.style.flexGrow = 1;
                enableDebugButton.style.marginRight = 5;
                debugButtons.Add(enableDebugButton);
                
                var showOverlayButton = YUCPUIToolkitHelper.CreateButton("Force Show Debug Overlay", () => inputHandler.ForceShowDebugOverlay(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
                showOverlayButton.style.flexGrow = 1;
                showOverlayButton.style.marginRight = 5;
                debugButtons.Add(showOverlayButton);
                
                var hideOverlayButton = YUCPUIToolkitHelper.CreateButton("Hide Debug Overlay", () => inputHandler.HideDebugOverlay(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
                hideOverlayButton.style.flexGrow = 1;
                debugButtons.Add(hideOverlayButton);
                
                container.Add(debugButtons);
                
                var sceneViewButton = YUCPUIToolkitHelper.CreateButton("Toggle Scene View Overlay", () => inputHandler.showInSceneView = !inputHandler.showInSceneView, YUCPUIToolkitHelper.ButtonVariant.Secondary);
                sceneViewButton.style.marginBottom = 5;
                container.Add(sceneViewButton);
                
                var debugLabel = new Label($"Debug Overlay Visible: {inputHandler.IsDebugOverlayVisible()}");
                debugLabel.style.fontSize = 11;
                container.Add(debugLabel);
                
                var sceneLabel = new Label($"Scene View Overlay: {(inputHandler.showInSceneView ? "Enabled" : "Disabled")}");
                sceneLabel.style.fontSize = 11;
                container.Add(sceneLabel);
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Runtime Input Handler Not Found - Build avatar first", YUCPUIToolkitHelper.MessageType.Warning));
            }
        }

        public override void OnInspectorGUI()
        {
            // Legacy support - not used anymore
        }

        private void UpdateGestureManagerHelp(VisualElement container)
        {
            container.Clear();
            if (data.gestureManager == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("No Gesture Manager assigned. The component will attempt to auto-detect one on this avatar.", YUCPUIToolkitHelper.MessageType.Info));
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Using Gesture Manager: {data.gestureManager.GetType().Name}", YUCPUIToolkitHelper.MessageType.None));
            }
            
            if (data.gestureManagerMode == GestureManagerMode.GestureManagerMode)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Gesture Manager Mode: Uses Vrc3Param system for parameter control. Recommended for full Gesture Manager integration.", YUCPUIToolkitHelper.MessageType.Info));
            }
            else if (data.gestureManagerMode == GestureManagerMode.AnimatorMode)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Animator Mode: Directly controls Animator parameters. Use when Gesture Manager is not available.", YUCPUIToolkitHelper.MessageType.Info));
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Auto Detect: Will automatically determine the best mode based on the detected Gesture Manager.", YUCPUIToolkitHelper.MessageType.Info));
            }
        }
        
        private void UpdateControllerStatus(VisualElement container)
        {
            container.Clear();
            bool controllerConnected = IsControllerConnected();
            string[] joystickNames = Input.GetJoystickNames();
            string statusText = controllerConnected ? 
                $"Controller Connected: {joystickNames[0]}" : 
                "No Controller Detected";
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(statusText, controllerConnected ? YUCPUIToolkitHelper.MessageType.Info : YUCPUIToolkitHelper.MessageType.Warning));
        }
        
        private bool IsControllerConnected()
        {
            // Check if any controller is connected
            string[] joystickNames = Input.GetJoystickNames();
            return joystickNames.Length > 0 && !string.IsNullOrEmpty(joystickNames[0]);
        }



        private string GetInputDescription(InputMapping mapping)
        {
            switch (mapping.inputType)
            {
                case InputType.Keyboard:
                    return $"Keyboard: {mapping.keyboardKey}";
                case InputType.ControllerButton:
                    return $"Controller Button: {mapping.controllerButton}";
                case InputType.ControllerAxis:
                    return $"Controller Axis: {mapping.controllerAxis}";
                case InputType.ControllerTrigger:
                    return $"Controller Trigger: {mapping.controllerTrigger}";
                case InputType.ControllerDpad:
                    return $"Controller D-pad: {mapping.controllerDpad}";
                default:
                    return "Unknown Input";
            }
        }

    }
}
