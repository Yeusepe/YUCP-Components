using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components
{
    public enum InputType
    {
        Keyboard,
        ControllerButton,
        ControllerAxis,
        ControllerTrigger,
        ControllerDpad
    }

    public enum ParameterType
    {
        Bool,
        Float,
        Int
    }

    public enum GestureManagerMode
    {
        AutoDetect,
        GestureManagerMode,
        AnimatorMode
    }

    [System.Serializable]
    public class InputMapping
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

    [BetaWarning("This component is in BETA and may not work as intended. Gesture Manager input emulation is experimental and may require manual parameter configuration.")]
    public class GestureManagerInputEmulator : MonoBehaviour
    {
        [Header("Gesture Manager Reference")]
        [Tooltip("The GameObject containing the Gesture Manager component")]
        public GameObject gestureManager;

        [Header("Input Mappings")]
        [Tooltip("List of input mappings to configure")]
        public List<InputMapping> inputMappings = new List<InputMapping>();

        [Header("General Settings")]
        [Tooltip("Enable debug mode for detailed logging")]
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

        [Header("Build Statistics")]
        [Tooltip("Number of input mappings processed during build")]
        public int processedMappingsCount = 0;

        [Tooltip("Number of runtime components created")]
        public int runtimeComponentsCreated = 0;

        [Header("Input Emulation")]
        [Tooltip("Enable input emulation")]
        public bool enableInputEmulation = true;

        public List<InputMapping> activeMappings => GetActiveMappings();

        public List<InputMapping> GetActiveMappings()
        {
            return inputMappings.FindAll(mapping => mapping.enabled);
        }

        public void AddInputMapping(InputMapping mapping)
        {
            inputMappings.Add(mapping);
        }

        public void RemoveInputMapping(int index)
        {
            if (index >= 0 && index < inputMappings.Count)
            {
                inputMappings.RemoveAt(index);
            }
        }

        public void SetBuildStats(int processedCount, int runtimeCount)
        {
            processedMappingsCount = processedCount;
            runtimeComponentsCreated = runtimeCount;
        }

        private void Awake()
        {
            // Auto-detect Gesture Manager if not assigned
            if (gestureManager == null)
            {
                var comp = FindObjectOfType<MonoBehaviour>();
                if (comp != null && comp.GetType().Name == "GestureManager")
                {
                    gestureManager = comp.gameObject;
                    Debug.Log($"[GestureManagerInputEmulator] Auto-detected Gesture Manager: {gestureManager.name}");
                }
            }
        }
    }
}