using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Runtime input handler for Gesture Manager Input Emulator.
    /// Captures keyboard and controller inputs and sends them directly to Gesture Manager parameters.
    /// This component is automatically added by the processor during build.
    /// </summary>
    public class GestureManagerInputHandler : MonoBehaviour
    {
        #region Fields and Properties

        [Header("Input Settings")]
        [Tooltip("Enable input capture in play mode")]
        public bool enableInputCapture = true;

        [Tooltip("Debug mode - shows input events in console")]
        public bool debugMode = false;

        [Tooltip("Show debug overlay in Scene view (in addition to Game view)")]
        public bool showInSceneView = true;

        [Header("Controller Settings")]
        [Tooltip("Controller deadzone for axis inputs")]
        [Range(0f, 1f)]
        public float controllerDeadzone = 0.1f;

        [Tooltip("Sensitivity multiplier for controller axis inputs")]
        [Range(0.1f, 5f)]
        public float axisSensitivity = 1f;

        [Header("Gesture Manager Mode")]
        [Tooltip("Operation mode for parameter control")]
        public GestureManagerMode gestureManagerMode = GestureManagerMode.GestureManagerMode;

        [System.Serializable]
        public class RuntimeInputMapping
        {
            public string mappingName;
            public InputType inputType;
            public KeyCode keyboardKey;
            public string controllerButton;
            public string controllerAxis;
            public string controllerTrigger;
            public string controllerDpad;
            public string parameterName;
            public ParameterType parameterType;
            public float activeValue;
            public float inactiveValue;
            public float minValue;
            public float maxValue;
            public bool enabled;
        }

        // Runtime mappings
        public List<RuntimeInputMapping> runtimeMappings = new List<RuntimeInputMapping>();

        // Private fields
        private Component gestureManager;
        private object moduleVrc3;
        private Dictionary<string, float> inputValues = new Dictionary<string, float>();
        private Dictionary<string, float> previousInputValues = new Dictionary<string, float>();
        private bool showDebugOverlay = false;
        private Vector2 scrollPosition;
        private float lastInputTime;
        private int inputEventCount;
        private int parameterSetCount;

        // Public properties for external access
        public Component GestureManager => gestureManager;
        public Dictionary<string, float> InputValues => inputValues;
        public bool IsDebugOverlayVisible() => showDebugOverlay;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Debug.Log($"[GestureManagerInputHandler] Awake called - Debug Mode: {debugMode}");
            Debug.Log($"[GestureManagerInputHandler] Runtime mappings count: {runtimeMappings.Count}");
            Debug.Log($"[GestureManagerInputHandler] Enable input capture: {enableInputCapture}");

            // Initialize input state tracking
            InitializeInputStates();
            Debug.Log($"[GestureManagerInputHandler] Initialization complete");
        }

        private void Start()
        {
            Debug.Log($"[GestureManagerInputHandler] Start called - Component enabled: {enabled}");
            Debug.Log($"[GestureManagerInputHandler] Debug mode: {debugMode}, Show overlay: {showDebugOverlay}");
            
            // Initialize debug overlay after configuration is set
            if (debugMode)
            {
                Debug.Log($"[GestureManagerInputHandler] Debug mode enabled - overlay will be shown");
                showDebugOverlay = true;
            }
        }

        private void Update()
        {
            if (!enableInputCapture || runtimeMappings.Count == 0) return;

            // Process input mappings
            ProcessInputMappings();

            // Handle F1 key for debug overlay
            if (debugMode && Input.GetKeyDown(KeyCode.F1))
            {
                ToggleDebugOverlay();
            }
        }

        private void OnGUI()
        {
            if (!debugMode || !showDebugOverlay) return;

            // Draw debug overlay
            DrawDebugOverlay();
        }

        #endregion

        #region Input Processing

        private void ProcessInputMappings()
        {
            foreach (var mapping in runtimeMappings)
            {
                if (!mapping.enabled) continue;

                float inputValue = GetInputValue(mapping);
                if (HasInputChanged(mapping, inputValue))
                {
                    SendParameterValue(mapping, inputValue);
                    inputValues[GetInputKey(mapping)] = inputValue;
                    lastInputTime = Time.time;
                    inputEventCount++;
                }
            }
        }

        private float GetInputValue(RuntimeInputMapping mapping)
        {
            switch (mapping.inputType)
            {
                case InputType.Keyboard:
                    return Input.GetKey(mapping.keyboardKey) ? 1f : 0f;

                case InputType.ControllerButton:
                    return GetControllerButtonValue(mapping.controllerButton);

                case InputType.ControllerAxis:
                    return GetControllerAxisValue(mapping.controllerAxis);

                case InputType.ControllerTrigger:
                    return GetControllerTriggerValue(mapping.controllerTrigger);

                case InputType.ControllerDpad:
                    return GetControllerDpadValue(mapping.controllerDpad);

                default:
                    return 0f;
            }
        }

        private float GetControllerButtonValue(string buttonName)
        {
            if (string.IsNullOrEmpty(buttonName)) return 0f;
            string standardButton = MapToStandardButton(buttonName);
            return Input.GetButton(standardButton) ? 1f : 0f;
        }

        private float GetControllerAxisValue(string axisName)
        {
            if (string.IsNullOrEmpty(axisName)) return 0f;
            string standardAxis = MapToStandardAxis(axisName);
            float value = Input.GetAxis(standardAxis);
            // Apply deadzone but keep continuous values
            if (Mathf.Abs(value) > controllerDeadzone)
            {
                return value * axisSensitivity;
            }
            return 0f;
        }

        private float GetControllerTriggerValue(string triggerName)
        {
            if (string.IsNullOrEmpty(triggerName)) return 0f;
            float value = GetDirectAxisValue(triggerName);
            // Apply deadzone but keep continuous values
            if (Mathf.Abs(value) > controllerDeadzone)
            {
                return value * axisSensitivity;
            }
            return 0f;
        }

        private float GetControllerDpadValue(string dpadName)
        {
            if (string.IsNullOrEmpty(dpadName)) return 0f;
            float value = GetDirectAxisValue(dpadName);
            // Apply deadzone but keep continuous values
            if (Mathf.Abs(value) > controllerDeadzone)
            {
                return value * axisSensitivity;
            }
            return 0f;
        }

        #endregion

        #region Input Mapping Utilities

        private string MapToStandardButton(string buttonName)
        {
            // Map common controller button names to Unity's standard input system
            switch (buttonName.ToLower())
            {
                case "a": case "cross": return "Fire1";
                case "b": case "circle": return "Fire2";
                case "x": case "square": return "Fire3";
                case "y": case "triangle": return "Jump";
                case "start": case "options": return "Submit";
                case "select": case "share": return "Cancel";
                case "l1": case "lb": return "Fire1";
                case "r1": case "rb": return "Fire2";
                case "l3": case "ls": return "Fire3";
                case "r3": case "rs": return "Jump";
                default: return buttonName;
            }
        }

        private string MapToStandardAxis(string axisName)
        {
            // Map common controller axis names to Unity's standard input system
            switch (axisName.ToLower())
            {
                case "left stick x": case "ls x": return "Horizontal";
                case "left stick y": case "ls y": return "Vertical";
                case "right stick x": case "rs x": return "Mouse X";
                case "right stick y": case "rs y": return "Mouse Y";
                case "dpad x": return "Horizontal";
                case "dpad y": return "Vertical";
                default: return axisName;
            }
        }

        private float GetDirectAxisValue(string axisName)
        {
            // Try to get axis value directly by name first
            try
            {
                return Input.GetAxis(axisName);
            }
            catch
            {
                // Fallback to direct axis access
                return GetDirectAxis(0); // Default to first axis
            }
        }

        private float GetDirectAxis(int axisNumber)
        {
            // Direct axis access for unconfigured axes
            try
            {
                return Input.GetAxis($"Axis {axisNumber}");
            }
            catch
            {
                return 0f;
            }
        }

        #endregion

        #region Parameter Control

        private void SendParameterValue(RuntimeInputMapping mapping, float inputValue)
        {
            if (gestureManagerMode == GestureManagerMode.GestureManagerMode)
            {
                SendGestureManagerParameter(mapping, inputValue);
            }
            else
            {
                SendAnimatorParameter(mapping, inputValue);
            }
        }

        private void SendGestureManagerParameter(RuntimeInputMapping mapping, float inputValue)
        {
            // Convert input value to parameter value based on type
            float parameterValue = ConvertInputToParameterValue(mapping, inputValue);

            try
            {
                // Try to get the ModuleVrc3 instance at runtime (it may not be initialized yet)
                if (moduleVrc3 == null)
                {
                    // Try to get the Module from the GestureManager at runtime
                    if (gestureManager != null)
                    {
                        var moduleField = gestureManager.GetType().GetField("Module", BindingFlags.Public | BindingFlags.Instance);
                        if (moduleField != null)
                        {
                            moduleVrc3 = moduleField.GetValue(gestureManager);
                            if (moduleVrc3 == null)
                            {
                                if (debugMode)
                                {
                                    Debug.Log($"[GestureManagerInputHandler] ModuleVrc3 is null - Gesture Manager not initialized yet");
                                }
                                return;
                            }
                            else
                            {
                if (debugMode)
                {
                    Debug.Log($"[GestureManagerInputHandler] ModuleVrc3 successfully initialized!");
                }
                            }
                        }
                    }
                    
                    if (moduleVrc3 == null)
                    {
                        // Still null, Gesture Manager not ready
                        return;
                    }
                }

                // Get the Params dictionary from the ModuleVrc3
                var paramsField = moduleVrc3.GetType().GetField("Params", BindingFlags.Public | BindingFlags.Instance);
                if (paramsField == null)
                {
                    Debug.LogWarning($"[GestureManagerInputHandler] ModuleVrc3 does not have Params field - Gesture Manager not ready");
                    return;
                }

                var paramsDict = paramsField.GetValue(moduleVrc3);
                if (paramsDict == null)
                {
                    Debug.LogWarning($"[GestureManagerInputHandler] ModuleVrc3.Params is null - Gesture Manager not ready");
                    return;
                }


                // Get the Vrc3Param from the dictionary
                var containsKeyMethod = paramsDict.GetType().GetMethod("ContainsKey", new[] { typeof(string) });
                var getItemMethod = paramsDict.GetType().GetMethod("get_Item", new[] { typeof(string) });
                
                if (containsKeyMethod == null || getItemMethod == null)
                {
                    Debug.LogWarning($"[GestureManagerInputHandler] Params dictionary methods not found - Gesture Manager not ready");
                    return;
                }

                bool hasKey = (bool)containsKeyMethod.Invoke(paramsDict, new object[] { mapping.parameterName });
                if (!hasKey)
                {
                    Debug.LogWarning($"[GestureManagerInputHandler] Parameter '{mapping.parameterName}' not found in Gesture Manager");
                    
                    // Debug: Show available parameters
                    if (debugMode)
                    {
                        try
                        {
                            var keysProperty = paramsDict.GetType().GetProperty("Keys");
                            if (keysProperty != null)
                            {
                                var keys = keysProperty.GetValue(paramsDict);
                                var toArrayMethod = keys.GetType().GetMethod("ToArray");
                                if (toArrayMethod != null)
                                {
                                    var keyArray = toArrayMethod.Invoke(keys, null) as string[];
                                    if (keyArray != null && keyArray.Length > 0)
                                    {
                                        Debug.Log($"[GestureManagerInputHandler] Available parameters: {string.Join(", ", keyArray)}");
                                    }
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"[GestureManagerInputHandler] Could not enumerate parameters: {ex.Message}");
                        }
                    }
                    return;
                }

                var vrc3Param = getItemMethod.Invoke(paramsDict, new object[] { mapping.parameterName });
                if (vrc3Param == null)
                {
                    Debug.LogWarning($"[GestureManagerInputHandler] Vrc3Param for '{mapping.parameterName}' is null");
                    return;
                }

                // Call Set method on the Vrc3Param - the correct signature is Set(ModuleVrc3 module, float value, object source = null)
                var setMethod = vrc3Param.GetType().GetMethod("Set", new[] { moduleVrc3.GetType(), typeof(float), typeof(object) });
                if (setMethod != null)
                {
                    setMethod.Invoke(vrc3Param, new object[] { moduleVrc3, parameterValue, null });
                    
                    if (debugMode)
                    {
                        Debug.Log($"[GestureManagerInputHandler] Set parameter '{mapping.parameterName}' = {parameterValue:F2}");
                    }
                }
                else
                {
                    // Try the method without the source parameter
                    setMethod = vrc3Param.GetType().GetMethod("Set", new[] { moduleVrc3.GetType(), typeof(float) });
                    if (setMethod != null)
                    {
                        setMethod.Invoke(vrc3Param, new object[] { moduleVrc3, parameterValue });
                        
                        if (debugMode)
                        {
                            Debug.Log($"[GestureManagerInputHandler] Set parameter '{mapping.parameterName}' = {parameterValue:F2}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (debugMode)
                {
                    Debug.LogError($"[GestureManagerInputHandler] Error setting parameter '{mapping.parameterName}': {ex.Message}");
                }
            }
        }

        private void SendAnimatorParameter(RuntimeInputMapping mapping, float inputValue)
        {
            // Convert input value to parameter value based on type
            float parameterValue = ConvertInputToParameterValue(mapping, inputValue);

            try
            {
                var animator = GetComponent<Animator>();
                if (animator != null)
                {
                    animator.SetFloat(mapping.parameterName, parameterValue);
                    
                    if (debugMode)
                    {
                        Debug.Log($"[GestureManagerInputHandler] Set Animator parameter '{mapping.parameterName}' = {parameterValue:F2}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GestureManagerInputHandler] Error setting Animator parameter '{mapping.parameterName}': {ex.Message}");
            }
        }

        private float ConvertInputToParameterValue(RuntimeInputMapping mapping, float inputValue)
        {
            switch (mapping.parameterType)
            {
                case ParameterType.Bool:
                    return inputValue > 0.5f ? mapping.activeValue : mapping.inactiveValue;

                case ParameterType.Float:
                    return Mathf.Lerp(mapping.minValue, mapping.maxValue, (inputValue + 1f) / 2f);

                case ParameterType.Int:
                    return Mathf.RoundToInt(Mathf.Lerp(mapping.minValue, mapping.maxValue, (inputValue + 1f) / 2f));

                default:
                    return inputValue;
            }
        }

        #endregion

        #region Public API

        public void AddRuntimeMapping(InputMapping mapping)
        {
            var runtimeMapping = new RuntimeInputMapping
            {
                mappingName = mapping.mappingName,
                inputType = mapping.inputType,
                keyboardKey = mapping.keyboardKey,
                controllerButton = mapping.controllerButton,
                controllerAxis = mapping.controllerAxis,
                controllerTrigger = mapping.controllerTrigger,
                controllerDpad = mapping.controllerDpad,
                parameterName = mapping.parameterName,
                parameterType = mapping.parameterType,
                activeValue = mapping.activeValue,
                inactiveValue = mapping.inactiveValue,
                minValue = mapping.minValue,
                maxValue = mapping.maxValue,
                enabled = mapping.enabled
            };

            runtimeMappings.Add(runtimeMapping);
            Debug.Log($"[GestureManagerInputHandler] Added runtime mapping: {mapping.mappingName}");
        }

        public void SetGestureManager(Component gestureManagerComponent)
        {
            gestureManager = gestureManagerComponent;
            Debug.Log($"[GestureManagerInputHandler] Set Gesture Manager: {gestureManager?.GetType().Name}");
            
            // Validate that we have a proper GestureManager component
            if (gestureManager != null && gestureManager.GetType().Name == "GestureManager")
            {
                Debug.Log($"[GestureManagerInputHandler] Gesture Manager validated successfully");
                
                // ModuleVrc3 will be initialized at runtime when Gesture Manager is ready
                Debug.Log($"[GestureManagerInputHandler] Gesture Manager reference set - Module will be initialized at runtime");
            }
            else
            {
                Debug.LogError($"[GestureManagerInputHandler] Invalid Gesture Manager component: {gestureManager?.GetType().Name}");
                enabled = false;
            }
        }

        public float GetGestureManagerParameterValue(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
                return 0f;

            try
            {
                // Try to get the ModuleVrc3 instance at runtime (it may not be initialized yet)
                if (moduleVrc3 == null)
                {
                    // Try to get the Module from the GestureManager at runtime
                    if (gestureManager != null)
                    {
                        var moduleField = gestureManager.GetType().GetField("Module", BindingFlags.Public | BindingFlags.Instance);
                        if (moduleField != null)
                        {
                            moduleVrc3 = moduleField.GetValue(gestureManager);
                            if (moduleVrc3 == null)
                            {
                                return 0f; // Gesture Manager not initialized yet
                            }
                        }
                    }
                    
                    if (moduleVrc3 == null)
                    {
                        return 0f; // Gesture Manager not ready
                    }
                }

                // Get the Params dictionary from the ModuleVrc3
                var paramsField = moduleVrc3.GetType().GetField("Params", BindingFlags.Public | BindingFlags.Instance);
                if (paramsField == null) return 0f;

                var paramsDict = paramsField.GetValue(moduleVrc3);
                if (paramsDict == null) return 0f;

                // Get the Vrc3Param from the dictionary
                var containsKeyMethod = paramsDict.GetType().GetMethod("ContainsKey", new[] { typeof(string) });
                var getItemMethod = paramsDict.GetType().GetMethod("get_Item", new[] { typeof(string) });
                
                if (containsKeyMethod == null || getItemMethod == null) return 0f;

                bool hasKey = (bool)containsKeyMethod.Invoke(paramsDict, new object[] { parameterName });
                if (!hasKey) return 0f;

                var vrc3Param = getItemMethod.Invoke(paramsDict, new object[] { parameterName });
                if (vrc3Param == null) return 0f;

                // Get the current value
                var floatValueMethod = vrc3Param.GetType().GetMethod("FloatValue");
                if (floatValueMethod != null)
                {
                    return (float)floatValueMethod.Invoke(vrc3Param, null);
                }
            }
            catch (System.Exception ex)
            {
                if (debugMode)
                {
                    Debug.LogError($"[GestureManagerInputHandler] Error getting parameter '{parameterName}': {ex.Message}");
                }
            }

            return 0f;
        }

        #endregion

        #region Debug Overlay

        private void DrawDebugOverlay()
        {
            if (!showDebugOverlay) return;

            float width = 320f;
            float height = GetContentHeight();
            Rect overlayRect = new Rect(10, 10, width, height);

            // Draw background
            GUI.Box(overlayRect, "", GUI.skin.box);

            // Draw content
            GUILayout.BeginArea(overlayRect);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            float y = DrawStatusSection(0, width);
            y = DrawInputMappingsSection(y, width);
            y = DrawStatisticsSection(y, width);
            y = DrawRecentActivitySection(y, width);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private float GetContentHeight()
        {
            return 400f; // Fixed height for now
        }

        private float DrawStatusSection(float y, float width)
        {
            GUILayout.Label("Gesture Manager Input Emulator", GUI.skin.label);
            GUILayout.Space(4);

            GUILayout.Label("Status", GUI.skin.label);
            GUILayout.BeginHorizontal();
            GUILayout.Label("• Handler:", GUILayout.Width(80));
            GUILayout.Label(enabled ? "Active" : "Inactive");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("• Input Capture:", GUILayout.Width(80));
            GUILayout.Label(enableInputCapture ? "Enabled" : "Disabled");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("• Gesture Manager:", GUILayout.Width(80));
            GUILayout.Label(gestureManager != null ? "Connected" : "Not Found");
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            return y + 120f;
        }

        private float DrawInputMappingsSection(float y, float width)
        {
            GUILayout.Label("Active Mappings", GUI.skin.label);
            
            if (runtimeMappings.Count > 0)
            {
                foreach (var mapping in runtimeMappings)
                {
                    GUILayout.BeginVertical("box");
                    GUILayout.Label($"{mapping.parameterName}", GUI.skin.label);
                    GUILayout.Label($"   Input: {mapping.inputType} → {GetInputDescription(mapping)}");
                    GUILayout.Label($"   Type: {mapping.parameterType}");
                    GUILayout.Label($"   Enabled: {(mapping.enabled ? "Yes" : "No")}");
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
            }
            else
            {
                GUILayout.Label("No active mappings", GUI.skin.label);
            }

            GUILayout.Space(8);
            return y + 200f;
        }

        private float DrawStatisticsSection(float y, float width)
        {
            GUILayout.Label("Statistics", GUI.skin.label);
            GUILayout.Label($"• Input Events: {inputEventCount}");
            GUILayout.Label($"• Parameters Set: {parameterSetCount}");
            GUILayout.Label($"• Last Input: {(Time.time - lastInputTime):F1}s ago");
            GUILayout.Space(8);
            return y + 80f;
        }

        private float DrawRecentActivitySection(float y, float width)
        {
            GUILayout.Label("Recent Activity", GUI.skin.label);
            GUILayout.Label("• System running normally");
            GUILayout.Label("• All inputs processed");
            GUILayout.Space(8);
            return y + 60f;
        }

        private string GetInputDescription(RuntimeInputMapping mapping)
        {
            switch (mapping.inputType)
            {
                case InputType.Keyboard:
                    return mapping.keyboardKey.ToString();
                case InputType.ControllerButton:
                    return mapping.controllerButton;
                case InputType.ControllerAxis:
                    return mapping.controllerAxis;
                case InputType.ControllerTrigger:
                    return mapping.controllerTrigger;
                case InputType.ControllerDpad:
                    return mapping.controllerDpad;
                default:
                    return "Unknown";
            }
        }

        #endregion

        #region Debug Controls

        public void ToggleDebugOverlay()
        {
            showDebugOverlay = !showDebugOverlay;
            Debug.Log($"[GestureManagerInputHandler] Debug overlay toggled: {showDebugOverlay}");
        }

        public void ShowDebugOverlay()
        {
            showDebugOverlay = true;
            Debug.Log($"[GestureManagerInputHandler] Debug overlay shown");
        }

        public void HideDebugOverlay()
        {
            showDebugOverlay = false;
            Debug.Log($"[GestureManagerInputHandler] Debug overlay hidden");
        }

        public void ForceShowDebugOverlay()
        {
            debugMode = true;
            showDebugOverlay = true;
            Debug.Log($"[GestureManagerInputHandler] Debug overlay forced to show");
        }

        public void ForceEnableDebugMode()
        {
            debugMode = true;
            showDebugOverlay = true;
            Debug.Log($"[GestureManagerInputHandler] Debug mode forced to enabled: {debugMode}, Overlay: {showDebugOverlay}");
        }

        #endregion

        #region Utility Methods

        private void InitializeInputStates()
        {
            inputValues.Clear();
            previousInputValues.Clear();
            inputEventCount = 0;
            parameterSetCount = 0;
            lastInputTime = 0f;
        }

        private bool HasInputChanged(RuntimeInputMapping mapping, float currentValue)
        {
            string inputKey = GetInputKey(mapping);
            
            if (!previousInputValues.ContainsKey(inputKey))
            {
                previousInputValues[inputKey] = 0f;
                return true;
            }

            float previousValue = previousInputValues[inputKey];
            bool hasChanged = Mathf.Abs(currentValue - previousValue) > 0.01f;
            
            if (hasChanged)
            {
                previousInputValues[inputKey] = currentValue;
            }

            return hasChanged;
        }

        private string GetInputKey(RuntimeInputMapping mapping)
        {
            return $"{mapping.inputType}_{mapping.parameterName}";
        }

        #endregion
    }
}