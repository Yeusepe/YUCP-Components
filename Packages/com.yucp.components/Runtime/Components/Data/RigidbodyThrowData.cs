using System;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YUCP.Components
{
    public enum ParameterMode
    {
        Single,
        Dual
    }

    public enum GestureHand
    {
        Left,
        Right
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Rigidbody Throw")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Rigidbody-Throw")]
    [SupportBanner]
    public class RigidbodyThrowData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Applied Object")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to throw. This applied object will be moved into the Rigidbody Throw's Container during build. The component automatically uses the GameObject it's attached to as the thrown object.")]
        [SerializeField, HideInInspector]
        private GameObject _thrownObjectInfo;
        
        [Tooltip("Where the throw starts, such as a hand. Empty keeps the prefab's default position.")]
        [FormerlySerializedAs("throwTarget")]
        public Transform appliedTransform;

        [Header("Options")]
        [Tooltip("Syncs the object's rotation as well as its position. Uses 51 parameter bits instead of 27.")]
        public bool enableRotationSync = false;

        [Tooltip("Adds a menu toggle for the throw.")]
        public bool generateMenu = false;

        [Tooltip("Submenu for the toggle, e.g. Utility/Throw. Empty puts it at the root.")]
        public string menuLocation = "Utility/Throw";

        [Header("Throw Settings")]
        [Tooltip("Physics material of the thrown object's collider.")]
        public PhysicMaterial physicsMaterial;

        [Tooltip("The hand whose gestures throw and reset the object.")]
        public GestureHand gestureHand = GestureHand.Right;

        [Tooltip("The gesture that throws the object.")]
        [Range(0, 7)]
        public int throwGesture = 2;

        [Tooltip("The gesture that brings the object back.")]
        [Range(0, 7)]
        public int resetGesture = 1;

        [Tooltip("Layers the thrown object collides with.")]
        public LayerMask collisionLayers = -1;

        [Header("Global Parameter Settings")]
        [Tooltip("Throws and resets with global parameters instead of gestures.")]
        public bool useGlobalParameters = false;

        [Tooltip("One parameter throws when on and resets when off. Two uses separate throw and reset parameters.")]
        public ParameterMode parameterMode = ParameterMode.Single;

        [Tooltip("Global parameter that throws the object. /<object> is appended to keep it unique.")]
        public string throwParameterName = "ThrowObject";

        [Tooltip("Global parameter that resets the object. /<object> is appended to keep it unique.")]
        public string resetParameterName = "ResetObject";

        [Header("Grouping")]
        [Tooltip("Shares one throw setup between components with the same group ID.")]
        public bool enableGrouping = false;

        [Tooltip("Components with the same group ID share one setup.")]
        public string throwGroupId = "Default";

        [Header("Diagnostics")]
        [Tooltip("Logs extra detail while the avatar builds.")]
        public bool verboseLogging = false;

        [Tooltip("Shows the credit line for the VRLabs prefab this component is based on.")]
        public bool includeCredits = true;

#if UNITY_EDITOR
        [NonSerialized] private bool suppressGroupPropagation;
#endif

        [SerializeField, HideInInspector]
        private string lastBuildSummary;

        [SerializeField, HideInInspector]
        private long lastBuildTicks;

        [Serializable]
        public class Settings
        {
            public GameObject appliedObject;
            public Transform appliedTransform;
            public bool enableRotationSync;
            public bool generateMenu;
            public string menuLocation;
            public PhysicMaterial physicsMaterial;
            public int throwGesture;
            public int resetGesture;
            public GestureHand gestureHand;
            public LayerMask collisionLayers;
            public bool useGlobalParameters;
            public ParameterMode parameterMode;
            public string throwParameterName;
            public string resetParameterName;
            public string throwGroupId;
            public bool enableGrouping;
            public bool verboseLogging;
            public bool includeCredits;
        }

        public int PreprocessOrder => 0;

        public bool OnPreprocess() => true;

        public void SetBuildSummary(string summary)
        {
            lastBuildSummary = summary;
            lastBuildTicks = DateTime.UtcNow.Ticks;
        }

        public string GetBuildSummary() => lastBuildSummary;

        public DateTime? GetLastBuildTimeUtc()
        {
            if (lastBuildTicks <= 0 || lastBuildTicks > DateTime.MaxValue.Ticks) return null;
            return new DateTime(lastBuildTicks, DateTimeKind.Utc);
        }

        public Settings ToSettings()
        {
            return new Settings
            {
                appliedObject = gameObject,
                appliedTransform = appliedTransform,
                enableRotationSync = enableRotationSync,
                generateMenu = generateMenu,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                physicsMaterial = physicsMaterial,
                throwGesture = Mathf.Clamp(throwGesture, 0, 7),
                resetGesture = Mathf.Clamp(resetGesture, 0, 7),
                gestureHand = gestureHand,
                collisionLayers = collisionLayers,
                useGlobalParameters = useGlobalParameters,
                parameterMode = parameterMode,
                throwParameterName = throwParameterName?.Trim() ?? string.Empty,
                resetParameterName = resetParameterName?.Trim() ?? string.Empty,
                throwGroupId = enableGrouping ? NormalizeGroupId(throwGroupId) : string.Empty,
                enableGrouping = enableGrouping,
                verboseLogging = verboseLogging,
                includeCredits = includeCredits
            };
        }

        public static string NormalizeGroupId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Default";
            }
            return value.Trim();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            throwGroupId = NormalizeGroupId(throwGroupId);

            if (suppressGroupPropagation)
            {
                return;
            }

            PropagateGroupSettings();
        }

        private void PropagateGroupSettings()
        {
            var descriptor = GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            var members = descriptor.GetComponentsInChildren<RigidbodyThrowData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(throwGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.throwGroupId) != normalizedGroup) continue;
                member.ApplyGroupSettings(settings);
            }
        }

        internal void ApplyGroupSettings(Settings source)
        {
            suppressGroupPropagation = true;
            try
            {
                enableRotationSync = source.enableRotationSync;
                generateMenu = source.generateMenu;
                menuLocation = source.menuLocation;
                physicsMaterial = source.physicsMaterial;
                throwGesture = source.throwGesture;
                resetGesture = source.resetGesture;
                gestureHand = source.gestureHand;
                collisionLayers = source.collisionLayers;
                useGlobalParameters = source.useGlobalParameters;
                parameterMode = source.parameterMode;
                throwParameterName = source.throwParameterName;
                resetParameterName = source.resetParameterName;
                enableGrouping = source.enableGrouping;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                throwGroupId = source.throwGroupId;
            }
            finally
            {
                suppressGroupPropagation = false;
            }

            EditorUtility.SetDirty(this);
        }
#endif
    }
}
