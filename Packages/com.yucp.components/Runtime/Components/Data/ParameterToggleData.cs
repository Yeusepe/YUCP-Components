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
        [Tooltip("The Animator parameter to check.")]
        public string parameterName = "";

        [Tooltip("The parameter's type in the Animator.")]
        public ToggleParameterType parameterType = ToggleParameterType.Bool;

        [Tooltip("How the parameter is compared with the threshold. If and IfNot check a bool.")]
        public ConditionMode conditionMode = ConditionMode.If;

        [Tooltip("The value the parameter is compared with. If and IfNot don't use it.")]
        public float threshold = 0f;
    }

    [Serializable]
    public class ParameterConditionGroup
    {
        [Tooltip("AND needs every condition in the group to be true. OR needs any one.")]
        public ConditionOperator groupOperator = ConditionOperator.AND;

        [Tooltip("The conditions in this group.")]
        public List<ParameterCondition> conditions = new List<ParameterCondition>();
    }

    [SupportBanner]
    [AddComponentMenu("YUCP/Parameter Toggle")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    public class ParameterToggleData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Parameter Conditions")]
        [Tooltip("Each group combines its own conditions with its operator.")]
        public List<ParameterConditionGroup> conditionGroups = new List<ParameterConditionGroup>();

        [Header("Toggle Actions")]
        [Tooltip("What the toggle does while it is on.")]
        public StateData state = new StateData();

        [Tooltip("Plays transition actions while the toggle turns on and off.")]
        public bool hasTransition = false;

        [Tooltip("Actions played while the toggle turns on.")]
        public StateData transitionStateIn = new StateData();

        [Tooltip("How long the transition in takes, in seconds.")]
        public float transitionTimeIn = 0f;

        [Tooltip("Actions played while the toggle turns off.")]
        public StateData transitionStateOut = new StateData();

        [Tooltip("How long the transition out takes, in seconds.")]
        public float transitionTimeOut = 0f;

        [Tooltip("Plays the transition in backwards when the toggle turns off.")]
        public bool simpleOutTransition = true;

        [Tooltip("Applies the On state's object toggles and material changes during the transitions too.")]
        public bool expandIntoTransition = true;

        [Header("Local/Remote Separation")]
        [Tooltip("Uses different actions for you than for other players.")]
        public bool separateLocal = false;

        [Tooltip("What you see while the toggle is on.")]
        public StateData localState = new StateData();

        [Tooltip("Actions you see while the toggle turns on.")]
        public StateData localTransitionStateIn = new StateData();

        [Tooltip("Actions you see while the toggle turns off.")]
        public StateData localTransitionStateOut = new StateData();

        [Tooltip("How long your transition in takes, in seconds.")]
        public float localTransitionTimeIn = 0f;

        [Tooltip("How long your transition out takes, in seconds.")]
        public float localTransitionTimeOut = 0f;

        [Header("Basic Settings")]
        [Tooltip("Remembers the toggle's state between worlds and avatar reloads.")]
        public bool saved = false;

        [Tooltip("The toggle starts on.")]
        public bool defaultOn = false;

        [Tooltip("Lets the On animation play to its end before the toggle turns off.")]
        public bool hasExitTime = false;

        [Tooltip("The toggle is on only while its button is held.")]
        public bool holdButton = false;

        [Header("Slider Settings")]
        [Tooltip("Uses a radial slider instead of an on/off toggle.")]
        public bool slider = false;

        [Tooltip("The slider's starting value, from 0 to 1.")]
        [Range(0f, 1f)]
        public float defaultSliderValue = 0f;

        [Tooltip("At 0% the slider stops controlling its properties, so another toggle or layer can.")]
        public bool sliderInactiveAtZero = false;

        [Header("Exclusive Tags")]
        [Tooltip("Turns off other toggles that share a tag when this one turns on.")]
        public bool enableExclusiveTag = false;

        [Tooltip("Comma-separated tags. Toggles that share a tag can't be on together.")]
        public string exclusiveTag = "";

        [Tooltip("Turns on when every toggle with these tags is off.")]
        public bool exclusiveOffState = false;

        [Header("Security")]
        [Tooltip("Locks the toggle until the avatar's Security Lock is unlocked. Needs a Security Lock component on the avatar.")]
        public bool securityEnabled = false;

        [Header("Icon")]
        [Tooltip("Gives the toggle a custom icon. This component adds no menu item of its own.")]
        public bool enableIcon = false;

        [Tooltip("The icon texture.")]
        public Texture2D icon;

        [Header("Global Parameters")]
        [Tooltip("Uses a named parameter instead of a generated one, so other toggles and systems can share it.")]
        public bool useGlobalParam = false;

        [Tooltip("The global parameter's name.")]
        public string globalParam = "";

        [Tooltip("Sets global parameters to match the toggle's state.")]
        public bool enableDriveGlobalParam = false;

        [Tooltip("Comma-separated names of the parameters to set.")]
        public string driveGlobalParam = "";

        [Header("Advanced")]
        [Tooltip("Reverses whether the toggle's objects show while the animator is disabled.")]
        public bool invertRestLogic = false;

        [Tooltip("The parameter name to use. Empty generates one.")]
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

