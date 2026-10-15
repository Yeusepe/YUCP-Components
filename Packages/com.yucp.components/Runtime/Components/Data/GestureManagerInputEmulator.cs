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

    [SupportBanner]
    [BetaWarning("This component is in beta. Input emulation may need manual parameter setup.")]
    public class GestureManagerInputEmulator : MonoBehaviour
    {
        [Header("Gesture Manager Reference")]
        [Tooltip("The object with Gesture Manager. Empty finds it on this object, its parents or its children.")]
        public GameObject gestureManager;

        [Header("Input Mappings")]
        [Tooltip("The keys and controller inputs, and the parameters they drive.")]
        public List<InputMapping> inputMappings = new List<InputMapping>();

        [Header("General Settings")]
        [Tooltip("Shows the input overlay in Play Mode and logs each input. Press F1 to hide or show the overlay.")]
        public bool debugMode = false;

        [Tooltip("Show debug overlay in Scene view (in addition to Game view)")]
        public bool showInSceneView = true;

        [Header("Controller Settings")]
        [Tooltip("Stick movement below this amount is ignored.")]
        [Range(0f, 1f)]
        public float controllerDeadzone = 0.1f;

        [Tooltip("Multiplies controller stick values.")]
        [Range(0.1f, 5f)]
        public float axisSensitivity = 1f;

        [Header("Gesture Manager Mode")]
        [Tooltip("How the avatar's parameters are set.")]
        public GestureManagerMode gestureManagerMode = GestureManagerMode.GestureManagerMode;

        [Header("Build Statistics")]
        [Tooltip("Number of input mappings processed during build")]
        public int processedMappingsCount = 0;

        [Tooltip("Number of runtime components created")]
        public int runtimeComponentsCreated = 0;

        [Header("Input Emulation")]
        [Tooltip("Drives the avatar's parameters from the keyboard and controller in Play Mode.")]
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