using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor;

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

        // Input mapping display settings
        private bool[] mappingFoldouts;

        private void OnEnable()
        {
            if (target is GestureManagerInputEmulator)
            {
                data = (GestureManagerInputEmulator)target;
                InitializeMappingFoldouts();
            }
        }

        private void InitializeMappingFoldouts()
        {
            if (data != null && data.inputMappings != null)
            {
                mappingFoldouts = new bool[data.inputMappings.Count];
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Gesture Manager Input Emulator"));
            
            var container = new IMGUIContainer(() => {
                OnInspectorGUIContent();
            });
            
            root.Add(container);
            return root;
        }

        public override void OnInspectorGUI()
        {
            OnInspectorGUIContent();
        }

        private void OnInspectorGUIContent()
        {
            serializedObject.Update();
            data = (GestureManagerInputEmulator)target;

            // Beta warning
            BetaWarningHelper.DrawBetaWarningIMGUI(typeof(GestureManagerInputEmulator));

            // Game view requirement notice
            EditorGUILayout.Space(5);
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.8f, 0.3f, 0.3f); // Yellow/orange background
            EditorGUILayout.HelpBox(
                "You have to be clicked into Game view for the inputs to be detected (I know it is a Unity thing I am working on it)",
                MessageType.Info);
            GUI.backgroundColor = originalColor;
            EditorGUILayout.Space(5);

            // Handle key detection
            HandleKeyDetection();

            // Ensure mapping foldouts array is properly sized
            if (mappingFoldouts == null || mappingFoldouts.Length != data.inputMappings.Count)
            {
                InitializeMappingFoldouts();
            }

            // Gesture Manager Section
            DrawSection("Gesture Manager", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("gestureManager"), new GUIContent("Gesture Manager Component"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("gestureManagerMode"), new GUIContent("Operation Mode"));
                
                if (data.gestureManager == null)
                {
                    EditorGUILayout.HelpBox("No Gesture Manager assigned. The component will attempt to auto-detect one on this avatar.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox($"Using Gesture Manager: {data.gestureManager.GetType().Name}", MessageType.None);
                }

                // Show mode-specific help
                if (data.gestureManagerMode == GestureManagerMode.GestureManagerMode)
                {
                    EditorGUILayout.HelpBox("Gesture Manager Mode: Uses Vrc3Param system for parameter control. Recommended for full Gesture Manager integration.", MessageType.Info);
                }
                else if (data.gestureManagerMode == GestureManagerMode.AnimatorMode)
                {
                    EditorGUILayout.HelpBox("Animator Mode: Directly controls Animator parameters. Use when Gesture Manager is not available.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Auto Detect: Will automatically determine the best mode based on the detected Gesture Manager.", MessageType.Info);
                }
            });

            // General Settings
            DrawSection("General Settings", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableInputEmulation"), new GUIContent("Enable Input Emulation"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"), new GUIContent("Debug Mode"));
            });

            // Input Mappings Section
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Input Mappings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            if (data.inputMappings == null || data.inputMappings.Count == 0)
            {
                EditorGUILayout.HelpBox("No input mappings configured. Click 'Add Mapping' to create your first mapping.", MessageType.Info);
            }
            else
            {
                // Responsive scroll view that fits the inspector
                float availableHeight = EditorGUIUtility.currentViewWidth > 400 ? 250f : 200f;
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(availableHeight));
                
                for (int i = 0; i < data.inputMappings.Count; i++)
                {
                    DrawInputMapping(i);
                }
                
                EditorGUILayout.EndScrollView();
            }

            // Add/Remove Mapping Buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Mapping", GUILayout.Height(25)))
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
                InitializeMappingFoldouts();
                EditorUtility.SetDirty(data);
            }
            
            if (data.inputMappings.Count > 0)
            {
                if (GUILayout.Button("Clear All", GUILayout.Height(25)))
                {
                    if (EditorUtility.DisplayDialog("Clear All Mappings", 
                        "Are you sure you want to remove all input mappings?", "Yes", "Cancel"))
                    {
                        data.inputMappings.Clear();
                        InitializeMappingFoldouts();
                        EditorUtility.SetDirty(data);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // Controller Settings (Advanced)
            EditorGUILayout.Space(10);
            showControllerSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showControllerSettings, "Controller Settings");
            if (showControllerSettings)
            {
                EditorGUI.indentLevel++;
                
                // Controller status indicator
                bool controllerConnected = IsControllerConnected();
                string[] joystickNames = Input.GetJoystickNames();
                string statusText = controllerConnected ? 
                    $"Controller Connected: {joystickNames[0]}" : 
                    "No Controller Detected";
                
                EditorGUILayout.HelpBox(statusText, controllerConnected ? MessageType.Info : MessageType.Warning);
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("controllerDeadzone"), new GUIContent("Controller Deadzone"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("axisSensitivity"), new GUIContent("Axis Sensitivity"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Advanced Options
            EditorGUILayout.Space(5);
            showAdvancedOptions = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvancedOptions, "Advanced Options");
            if (showAdvancedOptions)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Advanced options for debugging and fine-tuning input behavior.", MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Build Statistics
            if (data.activeMappings.Count > 0)
            {
                EditorGUILayout.Space(10);
                showBuildStats = EditorGUILayout.BeginFoldoutHeaderGroup(showBuildStats, "Build Statistics");
                if (showBuildStats)
                {
                    EditorGUI.indentLevel++;
                    GUI.enabled = false;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("activeMappings"), new GUIContent("Active Mappings"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("detectedGestureManagerType"), new GUIContent("Gesture Manager Type"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("detectedMode"), new GUIContent("Operation Mode"));
                    GUI.enabled = true;
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            // Test Input Button
            EditorGUILayout.Space(10);
            
            // Show runtime status
            if (Application.isPlaying)
            {
                var inputHandler = FindObjectOfType<GestureManagerInputHandler>();
                if (inputHandler != null)
                {
                    EditorGUILayout.HelpBox("✅ Runtime Input Handler Active", MessageType.Info);
                    
                    // Debug overlay controls
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Force Enable Debug Mode"))
                    {
                        inputHandler.ForceEnableDebugMode();
                    }
                    if (GUILayout.Button("Force Show Debug Overlay"))
                    {
                        inputHandler.ForceShowDebugOverlay();
                    }
                    if (GUILayout.Button("Hide Debug Overlay"))
                    {
                        inputHandler.HideDebugOverlay();
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    // Scene view controls
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Toggle Scene View Overlay"))
                    {
                        inputHandler.showInSceneView = !inputHandler.showInSceneView;
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.LabelField($"Debug Overlay Visible: {inputHandler.IsDebugOverlayVisible()}");
                    EditorGUILayout.LabelField($"Scene View Overlay: {(inputHandler.showInSceneView ? "Enabled" : "Disabled")}");
                }
                else
                {
                    EditorGUILayout.HelpBox("Runtime Input Handler Not Found - Build avatar first", MessageType.Warning);
                }
            }
            

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInputMapping(int index)
        {
            if (index >= data.inputMappings.Count) return;

            var mapping = data.inputMappings[index];
            bool isEnabled = mapping.enabled;

            // Mapping header with foldout
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.BeginHorizontal();
            
            // Enable/disable toggle
            mapping.enabled = EditorGUILayout.Toggle(mapping.enabled, GUILayout.Width(20));
            
            // Foldout for mapping details - responsive width
            string foldoutText = $"{mapping.mappingName} {(isEnabled ? "" : "(Disabled)")}";
            if (EditorGUIUtility.currentViewWidth < 400)
            {
                // Truncate text for narrow inspectors
                if (foldoutText.Length > 25)
                    foldoutText = foldoutText.Substring(0, 22) + "...";
            }
            
            mappingFoldouts[index] = EditorGUILayout.Foldout(mappingFoldouts[index], foldoutText, true);
            
            // Remove button - only show if there's space
            if (EditorGUIUtility.currentViewWidth > 200)
            {
                if (GUILayout.Button("×", GUILayout.Width(20), GUILayout.Height(18)))
                {
                    if (EditorUtility.DisplayDialog("Remove Mapping", 
                        $"Remove mapping '{mapping.mappingName}'?", "Yes", "Cancel"))
                    {
                        data.RemoveInputMapping(index);
                        InitializeMappingFoldouts();
                        EditorUtility.SetDirty(data);
                        return;
                    }
                }
            }
            
            EditorGUILayout.EndHorizontal();

            if (mappingFoldouts[index])
            {
                EditorGUI.indentLevel++;
                
                // Basic mapping info
                EditorGUILayout.BeginHorizontal();
                mapping.mappingName = EditorGUILayout.TextField("Name", mapping.mappingName);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                mapping.inputType = (InputType)EditorGUILayout.EnumPopup("Input Type", mapping.inputType);
                EditorGUILayout.EndHorizontal();

                // Input-specific fields
                DrawInputSpecificFields(mapping);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Parameter Settings", EditorStyles.boldLabel);
                
                mapping.parameterName = EditorGUILayout.TextField("Parameter Name", mapping.parameterName);
                mapping.parameterType = (ParameterType)EditorGUILayout.EnumPopup("Parameter Type", mapping.parameterType);

                // Parameter value fields based on type - responsive layout
                if (mapping.parameterType == ParameterType.Bool)
                {
                    if (EditorGUIUtility.currentViewWidth > 400)
                    {
                        EditorGUILayout.BeginHorizontal();
                        mapping.activeValue = EditorGUILayout.FloatField("Active Value", mapping.activeValue, GUILayout.Width(180));
                        mapping.inactiveValue = EditorGUILayout.FloatField("Inactive Value", mapping.inactiveValue, GUILayout.Width(180));
                        EditorGUILayout.EndHorizontal();
                    }
                    else
                    {
                        mapping.activeValue = EditorGUILayout.FloatField("Active Value", mapping.activeValue);
                        mapping.inactiveValue = EditorGUILayout.FloatField("Inactive Value", mapping.inactiveValue);
                    }
                }
                else if (mapping.parameterType == ParameterType.Float)
                {
                    if (EditorGUIUtility.currentViewWidth > 400)
                    {
                        EditorGUILayout.BeginHorizontal();
                        mapping.minValue = EditorGUILayout.FloatField("Min Value", mapping.minValue, GUILayout.Width(180));
                        mapping.maxValue = EditorGUILayout.FloatField("Max Value", mapping.maxValue, GUILayout.Width(180));
                        EditorGUILayout.EndHorizontal();
                    }
                    else
                    {
                        mapping.minValue = EditorGUILayout.FloatField("Min Value", mapping.minValue);
                        mapping.maxValue = EditorGUILayout.FloatField("Max Value", mapping.maxValue);
                    }
                }
                else if (mapping.parameterType == ParameterType.Int)
                {
                    if (EditorGUIUtility.currentViewWidth > 400)
                    {
                        EditorGUILayout.BeginHorizontal();
                        mapping.activeValue = EditorGUILayout.IntField("Active Value", (int)mapping.activeValue, GUILayout.Width(180));
                        mapping.inactiveValue = EditorGUILayout.IntField("Inactive Value", (int)mapping.inactiveValue, GUILayout.Width(180));
                        EditorGUILayout.EndHorizontal();
                    }
                    else
                    {
                        mapping.activeValue = EditorGUILayout.IntField("Active Value", (int)mapping.activeValue);
                        mapping.inactiveValue = EditorGUILayout.IntField("Inactive Value", (int)mapping.inactiveValue);
                    }
                }

                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void DrawInputSpecificFields(InputMapping mapping)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Input Configuration", EditorStyles.boldLabel);

            switch (mapping.inputType)
            {
                case InputType.Keyboard:
                    DrawKeyboardInput(mapping);
                    break;

                case InputType.ControllerButton:
                    DrawControllerButtonInput(mapping);
                    break;

                case InputType.ControllerAxis:
                    DrawControllerAxisInput(mapping);
                    break;

                case InputType.ControllerTrigger:
                    DrawControllerTriggerInput(mapping);
                    break;

                case InputType.ControllerDpad:
                    DrawControllerDpadInput(mapping);
                    break;
            }
        }

        private void DrawKeyboardInput(InputMapping mapping)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Keyboard Key", GUILayout.Width(100));
            
            string keyText;
            if (isDetectingKey && keyDetectionMapping == mapping)
            {
                keyText = "Press any key...";
                GUI.backgroundColor = Color.yellow;
            }
            else if (mapping.keyboardKey == KeyCode.None)
            {
                keyText = "Click to assign key";
            }
            else
            {
                keyText = mapping.keyboardKey.ToString();
            }
            
            if (GUILayout.Button(keyText, EditorStyles.popup))
            {
                if (!isDetectingKey)
                {
                    StartKeyDetection(mapping);
                }
                else
                {
                    // Cancel detection
                    isDetectingKey = false;
                    keyDetectionMapping = null;
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            
            if (mapping.keyboardKey != KeyCode.None)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(104); // Align with the button above
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    mapping.keyboardKey = KeyCode.None;
                }
                EditorGUILayout.EndHorizontal();
            }
            
            if (isDetectingKey && keyDetectionMapping == mapping)
            {
                EditorGUILayout.HelpBox("Press any key or mouse button to assign. Click the button again to cancel.", MessageType.Info);
            }
        }

        private void DrawControllerButtonInput(InputMapping mapping)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Button", GUILayout.Width(100));
            
            string[] buttonOptions = GetControllerButtonOptions();
            int currentIndex = Array.IndexOf(buttonOptions, mapping.controllerButton);
            if (currentIndex < 0) currentIndex = 0;
            
            int newIndex = EditorGUILayout.Popup(currentIndex, buttonOptions);
            if (newIndex != currentIndex)
            {
                mapping.controllerButton = buttonOptions[newIndex];
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawControllerAxisInput(InputMapping mapping)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Axis", GUILayout.Width(100));
            
            string[] axisOptions = GetControllerAxisOptions();
            int currentIndex = Array.IndexOf(axisOptions, mapping.controllerAxis);
            if (currentIndex < 0) currentIndex = 0;
            
            int newIndex = EditorGUILayout.Popup(currentIndex, axisOptions);
            if (newIndex != currentIndex)
            {
                mapping.controllerAxis = axisOptions[newIndex];
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawControllerTriggerInput(InputMapping mapping)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Trigger", GUILayout.Width(100));
            
            string[] triggerOptions = GetControllerTriggerOptions();
            int currentIndex = Array.IndexOf(triggerOptions, mapping.controllerTrigger);
            if (currentIndex < 0) currentIndex = 0;
            
            int newIndex = EditorGUILayout.Popup(currentIndex, triggerOptions);
            if (newIndex != currentIndex)
            {
                mapping.controllerTrigger = triggerOptions[newIndex];
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawControllerDpadInput(InputMapping mapping)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("D-pad", GUILayout.Width(100));
            
            string[] dpadOptions = GetControllerDpadOptions();
            int currentIndex = Array.IndexOf(dpadOptions, mapping.controllerDpad);
            if (currentIndex < 0) currentIndex = 0;
            
            int newIndex = EditorGUILayout.Popup(currentIndex, dpadOptions);
            if (newIndex != currentIndex)
            {
                mapping.controllerDpad = dpadOptions[newIndex];
            }
            EditorGUILayout.EndHorizontal();
        }

        private string[] GetControllerButtonOptions()
        {
            var options = new List<string> { "None" };
            
            // Plug-and-play controller button options (works with any controller)
            options.AddRange(new string[] {
                "A", "B", "X", "Y", 
                "Start", "Select", 
                "Left Shoulder", "Right Shoulder",
                "Left Stick", "Right Stick",
                "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right"
            });
            
            return options.ToArray();
        }

        private string[] GetControllerAxisOptions()
        {
            var options = new List<string> { "None" };
            
            // Plug-and-play controller axis options (works with any controller)
            options.AddRange(new string[] {
                "Left Stick X", "Left Stick Y", "Left Stick Angle",
                "Right Stick X", "Right Stick Y", "Right Stick Angle"
            });
            
            return options.ToArray();
        }

        private string[] GetControllerTriggerOptions()
        {
            var options = new List<string> { "None" };
            
            // Plug-and-play controller trigger options (works with any controller)
            options.AddRange(new string[] {
                "Left Trigger", "Right Trigger"
            });
            
            return options.ToArray();
        }

        private string[] GetControllerDpadOptions()
        {
            var options = new List<string> { "None" };
            
            // Plug-and-play D-pad options (works with any controller)
            options.AddRange(new string[] {
                "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right"
            });
            
            return options.ToArray();
        }

        private bool IsControllerConnected()
        {
            // Check if any controller is connected
            string[] joystickNames = Input.GetJoystickNames();
            return joystickNames.Length > 0 && !string.IsNullOrEmpty(joystickNames[0]);
        }

        private bool isDetectingKey = false;
        private InputMapping keyDetectionMapping = null;

        private void StartKeyDetection(InputMapping mapping)
        {
            isDetectingKey = true;
            keyDetectionMapping = mapping;
        }

        private void HandleKeyDetection()
        {
            if (!isDetectingKey || keyDetectionMapping == null) return;

            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                keyDetectionMapping.keyboardKey = e.keyCode;
                isDetectingKey = false;
                keyDetectionMapping = null;
                Repaint();
            }
            else if (e.type == EventType.MouseDown)
            {
                // Handle mouse clicks as well
                KeyCode mouseKey = KeyCode.Mouse0 + e.button;
                keyDetectionMapping.keyboardKey = mouseKey;
                isDetectingKey = false;
                keyDetectionMapping = null;
                Repaint();
            }
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

        private void DrawSection(string title, System.Action content)
        {
            EditorGUILayout.Space(5);
            
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.1f);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;
            
            if (!string.IsNullOrEmpty(title))
            {
                var style = new GUIStyle(EditorStyles.boldLabel);
                style.alignment = TextAnchor.MiddleLeft;
                EditorGUILayout.LabelField(title, style);
                EditorGUILayout.Space(3);
            }
            
            content?.Invoke();
            
            EditorGUILayout.EndVertical();
        }
    }
}
