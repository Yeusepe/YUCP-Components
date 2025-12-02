using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum ToggleParameterType
    {
        Bool,
        Int,
        Float
    }

    public enum ConditionMode
    {
        Equals,
        NotEqual,
        Greater,
        Less,
        GreaterOrEqual,
        LessOrEqual,
        If,
        IfNot
    }

    public enum ConditionOperator
    {
        AND,
        OR
    }

    [Serializable]
    public class ParameterCondition
    {
        [Tooltip("Name of the Animator parameter to check.")]
        public string parameterName = "";

        [Tooltip("Type of the parameter (Bool, Int, or Float).")]
        public ToggleParameterType parameterType = ToggleParameterType.Bool;

        [Tooltip("How to compare the parameter value.")]
        public ConditionMode conditionMode = ConditionMode.If;

        [Tooltip("Value to compare against (for Equals, NotEqual, Greater, Less, etc.).")]
        public float threshold = 0f;
    }

    [Serializable]
    public class ParameterConditionGroup
    {
        [Tooltip("How to combine conditions within this group (AND = all must be true, OR = any can be true).")]
        public ConditionOperator groupOperator = ConditionOperator.AND;

        [Tooltip("List of parameter conditions in this group.")]
        public List<ParameterCondition> conditions = new List<ParameterCondition>();
    }

    [SupportBanner]
    [AddComponentMenu("YUCP/Parameter Toggle")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    public class ParameterToggleData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Parameter Conditions")]
        [Tooltip("Condition groups that activate this toggle. Groups are OR'd together (any group can activate).\n\n" +
                 "Within each group, conditions are combined based on the Group Operator (AND/OR).\n\n" +
                 "Example: Group 1 (AND): x == true AND y == 0\n" +
                 "         Group 2 (OR): a == true OR b > 5\n" +
                 "Result: Toggle activates when (x==true AND y==0) OR (a==true OR b>5)")]
        public List<ParameterConditionGroup> conditionGroups = new List<ParameterConditionGroup>();

        [Header("Toggle Actions")]
        [Tooltip("Actions to perform when toggle is ON (main state).")]
        public StateData state = new StateData();

        [Tooltip("Enable transition animations when turning on/off.")]
        public bool hasTransition = false;

        [Tooltip("Actions to play when transitioning IN (turning on).")]
        public StateData transitionStateIn = new StateData();

        [Tooltip("Blend duration for transition in (seconds).")]
        public float transitionTimeIn = 0f;

        [Tooltip("Actions to play when transitioning OUT (turning off).")]
        public StateData transitionStateOut = new StateData();

        [Tooltip("Blend duration for transition out (seconds).")]
        public float transitionTimeOut = 0f;

        [Tooltip("Transition out is reverse of transition in.")]
        public bool simpleOutTransition = true;

        [Tooltip("Extend object enabling and material settings into transitions.")]
        public bool expandIntoTransition = true;

        [Header("Local/Remote Separation")]
        [Tooltip("Use different actions for local vs remote players.")]
        public bool separateLocal = false;

        [Tooltip("Actions for local player when toggle is ON.")]
        public StateData localState = new StateData();

        [Tooltip("Transition in actions for local player.")]
        public StateData localTransitionStateIn = new StateData();

        [Tooltip("Transition out actions for local player.")]
        public StateData localTransitionStateOut = new StateData();

        [Tooltip("Local transition in duration (seconds).")]
        public float localTransitionTimeIn = 0f;

        [Tooltip("Local transition out duration (seconds).")]
        public float localTransitionTimeOut = 0f;

        [Header("Basic Settings")]
        [Tooltip("Save toggle state across avatar reloads.")]
        public bool saved = false;

        [Tooltip("Toggle starts in the ON state by default.")]
        public bool defaultOn = false;

        [Tooltip("Run animation to completion before allowing exit.")]
        public bool hasExitTime = false;

        [Tooltip("Hold button instead of latching toggle.")]
        public bool holdButton = false;

        [Header("Slider Settings")]
        [Tooltip("Use a slider (radial) instead of a toggle.")]
        public bool slider = false;

        [Tooltip("Default slider value (0-1, 0-100%).")]
        [Range(0f, 1f)]
        public float defaultSliderValue = 0f;

        [Tooltip("Passthrough at 0% (unusual). When checked, slider is bypassed at 0%, allowing other systems to control properties.")]
        public bool sliderInactiveAtZero = false;

        [Header("Exclusive Tags")]
        [Tooltip("Enable exclusive tags (mutually exclusive with other toggles sharing the same tags).")]
        public bool enableExclusiveTag = false;

        [Tooltip("Exclusive tags (comma-separated). Toggles sharing tags are mutually exclusive.")]
        public string exclusiveTag = "";

        [Tooltip("This is the exclusive off state (activates when all toggles with matching tags are off).")]
        public bool exclusiveOffState = false;

        [Header("Security")]
        [Tooltip("Protect toggle with security pin (requires Security Lock component on avatar).")]
        public bool securityEnabled = false;

        [Header("Icon")]
        [Tooltip("Enable custom menu icon (note: no menu will be created, but icon can be used for other purposes).")]
        public bool enableIcon = false;

        [Tooltip("Custom icon texture.")]
        public Texture2D icon;

        [Header("Global Parameters")]
        [Tooltip("Use a global parameter instead of creating a new one.")]
        public bool useGlobalParam = false;

        [Tooltip("Global parameter name to use.")]
        public string globalParam = "";

        [Tooltip("Drive a global parameter based on toggle state (advanced).")]
        public bool enableDriveGlobalParam = false;

        [Tooltip("Global parameter(s) to drive (comma-separated).")]
        public string driveGlobalParam = "";

        [Header("Advanced")]
        [Tooltip("Hide when animator disabled / Show when animator disabled (inverts rest logic).")]
        public bool invertRestLogic = false;

        [Tooltip("Internal parameter name override (leave empty for auto-generated).")]
        public string paramOverride = "";

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;
    }

    [Serializable]
    public class StateData
    {
        [Tooltip("Animation clip to play.")]
        public AnimationClip animationClip;

        [Tooltip("Objects to turn on.")]
        public List<GameObject> turnOnObjects = new List<GameObject>();

        [Tooltip("Objects to turn off.")]
        public List<GameObject> turnOffObjects = new List<GameObject>();

        [Tooltip("Blendshape actions.")]
        public List<BlendshapeActionData> blendshapeActions = new List<BlendshapeActionData>();

        [Tooltip("Material swap actions.")]
        public List<MaterialSwapActionData> materialSwaps = new List<MaterialSwapActionData>();

        [Tooltip("Material property actions.")]
        public List<MaterialPropertyActionData> materialProperties = new List<MaterialPropertyActionData>();

        [Tooltip("Scale actions.")]
        public List<ScaleActionData> scaleActions = new List<ScaleActionData>();

        [Tooltip("FX float parameter actions.")]
        public List<FxFloatActionData> fxFloatActions = new List<FxFloatActionData>();

        [Tooltip("PhysBone reset actions.")]
        public List<ResetPhysboneActionData> resetPhysbones = new List<ResetPhysboneActionData>();
    }

    [Serializable]
    public class BlendshapeActionData
    {
        public string blendShapeName = "";
        [Range(0f, 100f)]
        public float blendShapeValue = 100f;
        public Renderer renderer;
        public bool allRenderers = true;
    }

    [Serializable]
    public class MaterialSwapActionData
    {
        public Renderer renderer;
        public int materialIndex = 0;
        public Material material;
    }

    [Serializable]
    public class MaterialPropertyActionData
    {
        public GameObject rendererObject;
        public bool affectAllMeshes = false;
        public string propertyName = "";
        public MaterialPropertyType propertyType = MaterialPropertyType.Float;
        public float value = 0f;
        public Vector4 valueVector = Vector4.zero;
        public Color valueColor = Color.white;
    }

    public enum MaterialPropertyType
    {
        Float,
        Color,
        Vector,
        St
    }

    [Serializable]
    public class ScaleActionData
    {
        public GameObject obj;
        [Range(0.01f, 10f)]
        public float scale = 1f;
    }

    [Serializable]
    public class FxFloatActionData
    {
        public string name = "";
        public float value = 1f;
    }

    [Serializable]
    public class ResetPhysboneActionData
    {
        public VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone physBone;
    }
}

