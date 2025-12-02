using System;
using UnityEngine;
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
        [Header("Target Objects")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to launch. This object will be moved into the Rigidbody Launcher's Container during build. The component automatically uses the GameObject it's attached to as the launched object.")]
        [SerializeField, HideInInspector]
        private GameObject _launchedObjectInfo;
        
        [Tooltip("LAUNCHER TARGET: The object that will be launched. This object will be moved outside the prefab hierarchy and connected to a configurable joint that launches it when triggered.")]
        public Transform launcherTarget;

        [Header("Options")]
        [Tooltip("Expressions menu path where the control toggle should be created (e.g. \"Utility/Launcher\"). Leave blank to place it at the root menu.")]
        public string menuLocation = "Utility/Launcher";

        [Tooltip("OPTIONAL: Global parameter name for RigidbodyLauncher/Control. When set, this parameter will be registered as a global parameter that can be controlled by VRChat worlds or external sources. Leave empty to use local parameter only.")]
        public string globalParameterControl = "";

        [Header("Launch Settings")]
        [Tooltip("Launch speed/velocity. Negative value for forward direction. This affects the Target Velocity in the animation clip.")]
        public float launchSpeed = -10f;

        [Tooltip("Maximum force for the configurable joint X/Y/Z drives.")]
        [Range(0f, 10000f)]
        public float maximumForce = 1000f;

        [Tooltip("Layers that the particle system will detect collisions with.")]
        public LayerMask collisionLayers = -1;

        [Header("Grouping")]
        [Tooltip("Enable to combine multiple components into a shared launcher setup.")]
        public bool enableGrouping = false;

        [Tooltip("Identifier used when grouping is enabled. Components with matching IDs are merged into the same setup.")]
        public string launcherGroupId = "Default";

        [Header("Diagnostics")]
        [Tooltip("Print additional information while building.")]
        public bool verboseLogging = false;

        [Tooltip("Include the automatic credit banner in the inspector and documentation.")]
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
            public GameObject targetObject;
            public Transform launcherTarget;
            public string menuLocation;
            public string globalParameterControl;
            public float launchSpeed;
            public float maximumForce;
            public LayerMask collisionLayers;
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
            if (lastBuildTicks <= 0) return null;
            try
            {
                return new DateTime(lastBuildTicks, DateTimeKind.Utc);
            }
            catch
            {
                return null;
            }
        }

        public Settings ToSettings()
        {
            return new Settings
            {
                targetObject = gameObject,
                launcherTarget = launcherTarget,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                globalParameterControl = globalParameterControl?.Trim() ?? string.Empty,
                launchSpeed = launchSpeed,
                maximumForce = Mathf.Clamp(maximumForce, 0f, 10000f),
                collisionLayers = collisionLayers,
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
                collisionLayers = source.collisionLayers;
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

