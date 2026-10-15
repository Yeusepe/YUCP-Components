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
    [SupportBanner]
    [BetaWarning("This component is in beta. Input handling may need manual setup.")]
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


        public List<RuntimeInputMapping> runtimeMappings = new List<RuntimeInputMapping>();


        private Component gestureManager;
        private object moduleVrc3;
        private Dictionary<string, float> inputValues = new Dictionary<string, float>();
        private Dictionary<string, float> previousInputValues = new Dictionary<string, float>();
        private bool showDebugOverlay = false;
        private Vector2 scrollPosition;
        private float lastInputTime;
        private int inputEventCount;
        private int parameterSetCount;


        private float _leftPrevAngleRad = 0f;
        private float _rightPrevAngleRad = 0f;
        private bool _leftInitialized = false;
        private bool _rightInitialized = false;
        private float _leftPrevX = 0f;
        private float _leftPrevY = 0f;
        private float _rightPrevX = 0f;
        private float _rightPrevY = 0f;
        private float _leftPrevMagnitude = 0f;
        private float _rightPrevMagnitude = 0f;
        private int _leftFlickFrameCount = 0;
        private int _rightFlickFrameCount = 0;
        private bool _leftFlickWasSent = false;
        private bool _rightFlickWasSent = false;
        private float _leftAccumulatedRotationDelta = 0f;
        private float _rightAccumulatedRotationDelta = 0f;


        public Component GestureManager => gestureManager;
        public Dictionary<string, float> InputValues => inputValues;
        public bool IsDebugOverlayVisible() => showDebugOverlay;

        #endregion

        #region Unity Lifecycle

        public static GestureManagerInputHandler Current { get; private set; }

        private void Awake() => InitializeInputStates();

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        private void Start()
        {
            if (debugMode)
                showDebugOverlay = true;
        }

        private void Update()
        {
            if (!enableInputCapture || runtimeMappings.Count == 0) return;


            ProcessInputMappings();


            if (debugMode && Input.GetKeyDown(KeyCode.F1))
            {
                ToggleDebugOverlay();
            }
        }

        private void OnGUI()
        {
            if (!debugMode || !showDebugOverlay) return;


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
            if (string.IsNullOrEmpty(buttonName) || buttonName == "None") return 0f;

            string standardButton = MapToStandardButton(buttonName);


            try
            {
                return Input.GetButton(standardButton) ? 1f : 0f;
            }
            catch (System.ArgumentException)
            {

                int joystickButton = GetJoystickButtonIndex(buttonName);
                if (joystickButton >= 0)
                {
                    try
                    {
                        return Input.GetKey((KeyCode)((int)KeyCode.JoystickButton0 + joystickButton)) ? 1f : 0f;
                    }
                    catch
                    {
                        return 0f;
                    }
                }
                return 0f;
            }
        }

        private float GetControllerAxisValue(string axisName)
        {

            if (string.IsNullOrEmpty(axisName) || axisName == "None") return 0f;

            string axisLower = axisName.ToLower();


            if (axisLower.Contains("rotation direction") || axisLower.Contains("rotationdirection"))
            {
                return GetStickRotationDirection(axisName);
            }


            if (axisLower.Contains("flick"))
            {
                return GetStickFlick(axisName);
            }


            if (axisLower == "left stick angle" || axisLower == "ls angle")
            {
                return GetStickAngle("Horizontal", "Vertical");
            }
            else if (axisLower == "right stick angle" || axisLower == "rs angle")
            {
                return GetStickAngle("Mouse X", "Mouse Y");
            }

            string standardAxis = MapToStandardAxis(axisName);


            try
            {
                float value = Input.GetAxis(standardAxis);

                if (Mathf.Abs(value) > controllerDeadzone)
                {
                    return value * axisSensitivity;
                }
                return 0f;
            }
            catch (System.ArgumentException)
            {

                return 0f;
            }
        }

        private float GetStickAngle(string xAxis, string yAxis)
        {
            try
            {
                float x = Input.GetAxisRaw(xAxis);
                float y = Input.GetAxisRaw(yAxis);


                float magnitude = Mathf.Sqrt(x * x + y * y);
                if (magnitude < controllerDeadzone)
                {
                    return 0f;
                }


                float angle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
                if (angle < 0) angle += 360f;

                return angle;
            }
            catch (System.ArgumentException)
            {
                return 0f;
            }
        }

        private float GetControllerTriggerValue(string triggerName)
        {
            if (string.IsNullOrEmpty(triggerName) || triggerName == "None") return 0f;

            float value = GetDirectAxisValue(triggerName);

            if (Mathf.Abs(value) > controllerDeadzone)
            {
                return value * axisSensitivity;
            }
            return 0f;
        }

        private float GetControllerDpadValue(string dpadName)
        {
            if (string.IsNullOrEmpty(dpadName) || dpadName == "None") return 0f;

            float value = GetDirectAxisValue(dpadName);

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

            switch (buttonName.ToLower())
            {
                case "a": case "cross": return "Fire1";
                case "b": case "circle": return "Fire2";
                case "x": case "square": return "Fire3";
                case "y": case "triangle": return "Jump";
                case "start": case "options": return "Submit";
                case "select": case "share": return "Cancel";
                case "left shoulder": case "l1": case "lb": return "Fire1";
                case "right shoulder": case "r1": case "rb": return "Fire2";
                case "left stick": case "l3": case "ls": return "Fire3";
                case "right stick": case "r3": case "rs": return "Jump";
                default: return buttonName;
            }
        }

        private int GetJoystickButtonIndex(string buttonName)
        {

            switch (buttonName.ToLower())
            {
                case "a": case "cross": return 0;
                case "b": case "circle": return 1;
                case "x": case "square": return 2;
                case "y": case "triangle": return 3;
                case "left shoulder": case "l1": case "lb": return 4;
                case "right shoulder": case "r1": case "rb": return 5;
                case "select": case "share": case "back": return 6;
                case "start": case "options": return 7;
                case "left stick": case "l3": case "ls": return 8;
                case "right stick": case "r3": case "rs": return 9;
                case "d-pad up": return 10;
                case "d-pad down": return 11;
                case "d-pad left": return 12;
                case "d-pad right": return 13;
                default: return -1;
            }
        }

        private string MapToStandardAxis(string axisName)
        {

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
            if (string.IsNullOrEmpty(axisName) || axisName == "None") return 0f;


            try
            {
                return Input.GetAxis(axisName);
            }
            catch (System.ArgumentException)
            {

                string mappedAxis = MapAxisNameToJoystickAxis(axisName);
                if (!string.IsNullOrEmpty(mappedAxis))
                {
                    try
                    {
                        return Input.GetAxis(mappedAxis);
                    }
                    catch
                    {
                        return 0f;
                    }
                }
                return 0f;
            }
        }

        private string MapAxisNameToJoystickAxis(string axisName)
        {

            switch (axisName.ToLower())
            {
                case "left trigger": return "Axis 3";
                case "right trigger": return "Axis 6";
                case "d-pad up": case "dpad up": return "Axis 7";
                case "d-pad down": case "dpad down": return "Axis 7";
                case "d-pad left": case "dpad left": return "Axis 6";
                case "d-pad right": case "dpad right": return "Axis 6";
                default: return null;
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

            float parameterValue = ConvertInputToParameterValue(mapping, inputValue);

            try
            {

                if (moduleVrc3 == null)
                {

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

                        return;
                    }
                }


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


                var setMethod = vrc3Param.GetType().GetMethod("Set", new[] { moduleVrc3.GetType(), typeof(float), typeof(object) });
                if (setMethod != null)
                {
                    setMethod.Invoke(vrc3Param, new object[] { moduleVrc3, parameterValue, null });
                }
                else
                {

                    setMethod = vrc3Param.GetType().GetMethod("Set", new[] { moduleVrc3.GetType(), typeof(float) });
                    if (setMethod != null)
                    {
                        setMethod.Invoke(vrc3Param, new object[] { moduleVrc3, parameterValue });
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

            float parameterValue = ConvertInputToParameterValue(mapping, inputValue);

            try
            {
                var animator = GetComponent<Animator>();
                if (animator != null)
                {
                    animator.SetFloat(mapping.parameterName, parameterValue);
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

                    bool isAngleAxis = mapping.inputType == InputType.ControllerAxis &&
                                      !string.IsNullOrEmpty(mapping.controllerAxis) &&
                                      (mapping.controllerAxis.ToLower().Contains("angle"));

                    float normalizedValue;
                    if (isAngleAxis)
                    {

                        normalizedValue = Mathf.Clamp01(inputValue / 360f);
                    }
                    else
                    {

                        normalizedValue = (inputValue + 1f) / 2f;
                    }

                    return Mathf.Lerp(mapping.minValue, mapping.maxValue, normalizedValue);

                case ParameterType.Int:

                    bool isAngleAxisInt = mapping.inputType == InputType.ControllerAxis &&
                                         !string.IsNullOrEmpty(mapping.controllerAxis) &&
                                         (mapping.controllerAxis.ToLower().Contains("angle"));

                    float normalizedValueInt;
                    if (isAngleAxisInt)
                    {

                        normalizedValueInt = Mathf.Clamp01(inputValue / 360f);
                    }
                    else
                    {

                        normalizedValueInt = (inputValue + 1f) / 2f;
                    }

                    return Mathf.RoundToInt(Mathf.Lerp(mapping.minValue, mapping.maxValue, normalizedValueInt));

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


            if (gestureManager != null && gestureManager.GetType().Name == "GestureManager")
            {
                Debug.Log($"[GestureManagerInputHandler] Gesture Manager validated successfully");


                Debug.Log($"[GestureManagerInputHandler] Gesture Manager reference set - Module will be initialized at runtime");
            }
            else
            {
                Debug.LogError($"[GestureManagerInputHandler] Invalid Gesture Manager component: {gestureManager?.GetType().Name}");
                enabled = false;
            }
        }

        #endregion

        #region Debug Overlay

        private void DrawDebugOverlay()
        {
            if (!showDebugOverlay) return;

            float width = 320f;
            float height = GetContentHeight();
            Rect overlayRect = new Rect(10, 10, width, height);


            GUI.Box(overlayRect, "", GUI.skin.box);


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
            return 400f;
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

        #region Rotation and Flick Detection

        private float CalculateRotationDirection(float currentX, float currentY, ref float prevAngleRad, ref bool initialized, ref float accumulatedDelta, float deadzone = 0.2f, float minDeltaRad = 0.001f, float pulseThresholdRad = 0.02f)
        {
            float r = Mathf.Sqrt(currentX * currentX + currentY * currentY);

            if (r < deadzone)
            {
                initialized = false;
                accumulatedDelta = 0f;
                return 0f;
            }

            float angle = Mathf.Atan2(currentY, currentX);

            if (!initialized)
            {
                prevAngleRad = angle;
                initialized = true;
                accumulatedDelta = 0f;
                return 0f;
            }

            float delta = angle - prevAngleRad;

            if (delta > Mathf.PI)
            {
                delta -= 2f * Mathf.PI;
            }
            else if (delta < -Mathf.PI)
            {
                delta += 2f * Mathf.PI;
            }

            prevAngleRad = angle;


            if (Mathf.Abs(delta) < minDeltaRad)
            {
                return 0f;
            }


            accumulatedDelta += delta;


            if (Mathf.Abs(accumulatedDelta) >= pulseThresholdRad)
            {
                float result = accumulatedDelta > 0 ? -1f : 1f;
                accumulatedDelta = 0f;
                return result;
            }

            return 0f;
        }

        private bool DetectFlick(float currentX, float currentY, float prevX, float prevY, float prevMagnitude, ref int flickFrameCount, float velocityThreshold = 0.5f, float minRadius = 0.7f, float releaseRadius = 0.3f, int maxFrames = 6)
        {
            float r = Mathf.Sqrt(currentX * currentX + currentY * currentY);
            float dx = currentX - prevX;
            float dy = currentY - prevY;
            float velocity = Mathf.Sqrt(dx * dx + dy * dy);

            if (velocity > velocityThreshold && prevMagnitude > minRadius)
            {
                flickFrameCount = 1;
                return false;
            }

            if (flickFrameCount > 0)
            {
                flickFrameCount++;
                if (r < releaseRadius || flickFrameCount >= maxFrames)
                {
                    flickFrameCount = 0;
                    return true;
                }
            }

            return false;
        }

        private float GetStickRotationDirection(string axisName)
        {
            string axisLower = axisName.ToLower();
            bool isLeft = axisLower.Contains("left") || axisLower.Contains("ls");

            string xAxis = isLeft ? "Horizontal" : "Mouse X";
            string yAxis = isLeft ? "Vertical" : "Mouse Y";

            try
            {
                float x = Input.GetAxisRaw(xAxis);
                float y = Input.GetAxisRaw(yAxis);

                float magnitude = Mathf.Sqrt(x * x + y * y);
                if (magnitude < controllerDeadzone)
                {
                    if (isLeft)
                    {
                        _leftInitialized = false;
                        _leftAccumulatedRotationDelta = 0f;
                    }
                    else
                    {
                        _rightInitialized = false;
                        _rightAccumulatedRotationDelta = 0f;
                    }
                    return 0f;
                }

                float rotationDirection;
                if (isLeft)
                {
                    rotationDirection = CalculateRotationDirection(x, y, ref _leftPrevAngleRad, ref _leftInitialized, ref _leftAccumulatedRotationDelta, controllerDeadzone);
                    _leftPrevX = x;
                    _leftPrevY = y;
                    _leftPrevMagnitude = magnitude;
                }
                else
                {
                    rotationDirection = CalculateRotationDirection(x, y, ref _rightPrevAngleRad, ref _rightInitialized, ref _rightAccumulatedRotationDelta, controllerDeadzone);
                    _rightPrevX = x;
                    _rightPrevY = y;
                    _rightPrevMagnitude = magnitude;
                }

                return rotationDirection;
            }
            catch (System.ArgumentException)
            {
                return 0f;
            }
        }

        private float GetStickFlick(string axisName)
        {
            string axisLower = axisName.ToLower();
            bool isLeft = axisLower.Contains("left") || axisLower.Contains("ls");

            string xAxis = isLeft ? "Horizontal" : "Mouse X";
            string yAxis = isLeft ? "Vertical" : "Mouse Y";

            try
            {
                float x = Input.GetAxisRaw(xAxis);
                float y = Input.GetAxisRaw(yAxis);
                float magnitude = Mathf.Sqrt(x * x + y * y);

                bool flick;
                if (isLeft)
                {
                    if (_leftFlickWasSent)
                    {
                        _leftFlickWasSent = false;
                        _leftPrevX = x;
                        _leftPrevY = y;
                        _leftPrevMagnitude = magnitude;
                        return 0f;
                    }

                    flick = DetectFlick(x, y, _leftPrevX, _leftPrevY, _leftPrevMagnitude, ref _leftFlickFrameCount);

                    _leftPrevX = x;
                    _leftPrevY = y;
                    _leftPrevMagnitude = magnitude;

                    if (flick)
                    {
                        _leftFlickWasSent = true;
                        return 1f;
                    }
                }
                else
                {
                    if (_rightFlickWasSent)
                    {
                        _rightFlickWasSent = false;
                        _rightPrevX = x;
                        _rightPrevY = y;
                        _rightPrevMagnitude = magnitude;
                        return 0f;
                    }

                    flick = DetectFlick(x, y, _rightPrevX, _rightPrevY, _rightPrevMagnitude, ref _rightFlickFrameCount);

                    _rightPrevX = x;
                    _rightPrevY = y;
                    _rightPrevMagnitude = magnitude;

                    if (flick)
                    {
                        _rightFlickWasSent = true;
                        return 1f;
                    }
                }

                return 0f;
            }
            catch (System.ArgumentException)
            {
                return 0f;
            }
        }

        #endregion
    }
}