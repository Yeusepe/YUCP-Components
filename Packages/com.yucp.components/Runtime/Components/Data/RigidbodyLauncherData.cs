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
    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Rigidbody Launcher")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Rigidbody-Launcher")]
    [SupportBanner]
    public class RigidbodyLauncherData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Applied Object")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to launch. This applied object will be moved into the Rigidbody Launcher's Container during build. The component automatically uses the GameObject it's attached to as the launched object.")]
        [SerializeField, HideInInspector]
        private GameObject _launchedObjectInfo;
        
        [Tooltip("Where the launch starts. Empty keeps the prefab's default position.")]
        [FormerlySerializedAs("launcherTarget")]
        public Transform appliedTransform;

        [Header("Options")]
        [Tooltip("Submenu for the launcher toggle, e.g. Utility/Launcher. Empty puts it at the root.")]
        public string menuLocation = "Utility/Launcher";

        [Tooltip("Optional global parameter that controls the launcher. /<object> is appended to keep it unique.")]
        public string globalParameterControl = "";

        [Header("Launch Settings")]
        [Tooltip("How fast the object launches. Negative values launch it forward.")]
        public float launchSpeed = -10f;

        [Tooltip("The most force the launcher's joint can apply.")]
        [Range(0f, 10000f)]
        public float maximumForce = 1000f;

        [Tooltip("The hand whose gestures launch and reset the object.")]
        public GestureHand gestureHand = GestureHand.Right;

        [Tooltip("The gesture that launches the object.")]
        [Range(0, 7)]
        public int launchGesture = 2;

        [Tooltip("The gesture that brings the object back.")]
        [Range(0, 7)]
        public int resetGesture = 1;

        [Tooltip("Layers the launched object collides with.")]
        public LayerMask collisionLayers = -1;

        [Header("Global Parameter Settings")]
        [Tooltip("Launches and resets with global parameters instead of gestures.")]
        public bool useGlobalParameters = false;

        [Tooltip("One parameter launches when on and resets when off. Two uses separate launch and reset parameters.")]
        public ParameterMode parameterMode = ParameterMode.Single;

        [Tooltip("Global parameter that launches the object. /<object> is appended to keep it unique.")]
        public string launchParameterName = "LaunchObject";

        [Tooltip("Global parameter that resets the object. /<object> is appended to keep it unique.")]
        public string resetParameterName = "ResetObject";

        [Header("Grouping")]
        [Tooltip("Shares one launcher setup between components with the same group ID.")]
        public bool enableGrouping = false;

        [Tooltip("Components with the same group ID share one setup.")]
        public string launcherGroupId = "Default";

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
            public string menuLocation;
            public string globalParameterControl;
            public float launchSpeed;
            public float maximumForce;
            public GestureHand gestureHand;
            public int launchGesture;
            public int resetGesture;
            public LayerMask collisionLayers;
            public bool useGlobalParameters;
            public ParameterMode parameterMode;
            public string launchParameterName;
            public string resetParameterName;
            public string launcherGroupId;
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
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                globalParameterControl = globalParameterControl?.Trim() ?? string.Empty,
                launchSpeed = launchSpeed,
                maximumForce = Mathf.Clamp(maximumForce, 0f, 10000f),
                gestureHand = gestureHand,
                launchGesture = launchGesture,
                resetGesture = resetGesture,
                collisionLayers = collisionLayers,
                useGlobalParameters = useGlobalParameters,
                parameterMode = parameterMode,
                launchParameterName = launchParameterName?.Trim() ?? string.Empty,
                resetParameterName = resetParameterName?.Trim() ?? string.Empty,
                launcherGroupId = enableGrouping ? NormalizeGroupId(launcherGroupId) : string.Empty,
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

            launcherGroupId = NormalizeGroupId(launcherGroupId);

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

            var members = descriptor.GetComponentsInChildren<RigidbodyLauncherData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(launcherGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.launcherGroupId) != normalizedGroup) continue;
                member.ApplyGroupSettings(settings);
            }
        }

        internal void ApplyGroupSettings(Settings source)
        {
            suppressGroupPropagation = true;
            try
            {
                menuLocation = source.menuLocation;
                globalParameterControl = source.globalParameterControl;
                launchSpeed = source.launchSpeed;
                maximumForce = source.maximumForce;
                gestureHand = source.gestureHand;
                launchGesture = source.launchGesture;
                resetGesture = source.resetGesture;
                collisionLayers = source.collisionLayers;
                useGlobalParameters = source.useGlobalParameters;
                parameterMode = source.parameterMode;
                launchParameterName = source.launchParameterName;
                resetParameterName = source.resetParameterName;
                enableGrouping = source.enableGrouping;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                launcherGroupId = source.launcherGroupId;
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
