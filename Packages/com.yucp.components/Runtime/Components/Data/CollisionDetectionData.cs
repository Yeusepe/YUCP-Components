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
    [AddComponentMenu("YUCP/Collision Detection")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Collision-Detection")]
    [SupportBanner]
    public class CollisionDetectionData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Applied Object")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to detect collisions on. This applied object will be moved into the collision detection system's Container during build. The component automatically uses the GameObject it's attached to.")]
        [SerializeField, HideInInspector]
        private GameObject _appliedObjectInfo;
        
        [Header("Options")]
        [Tooltip("Clears the collision as soon as contact ends, instead of waiting for a reset.")]
        public bool alwaysReset = false;

        [Tooltip("Submenu for the reset toggles, e.g. Utility/Collision. Empty adds no toggles.")]
        public string menuLocation = "Utility/Collision";

        [Tooltip("Optional global parameter that resets the collision. /<object> is appended to keep it unique.")]
        public string globalParameterReset = "";

        [Tooltip("Optional global parameter that turns Always Reset on. /<object> is appended to keep it unique.")]
        public string globalParameterAlwaysReset = "";

        [Header("Collision Settings")]
        [Tooltip("Layers that count as a collision.")]
        public LayerMask collisionLayers = -1;

        [Tooltip("Detects trigger colliders instead of solid ones.")]
        public bool useTriggers = false;

        [Tooltip("Size of the area that detects collisions.")]
        [Range(0.1f, 10f)]
        public float particleScale = 1f;

        [Header("Grouping")]
        [Tooltip("Shares one collision detection setup between components with the same group ID.")]
        public bool enableGrouping = false;

        [Tooltip("Components with the same group ID share one setup.")]
        public string collisionGroupId = "Default";

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
            public bool alwaysReset;
            public string menuLocation;
            public string globalParameterReset;
            public string globalParameterAlwaysReset;
            public LayerMask collisionLayers;
            public bool useTriggers;
            public float particleScale;
            public string collisionGroupId;
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
                alwaysReset = alwaysReset,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                globalParameterReset = globalParameterReset?.Trim() ?? string.Empty,
                globalParameterAlwaysReset = globalParameterAlwaysReset?.Trim() ?? string.Empty,
                collisionLayers = collisionLayers,
                useTriggers = useTriggers,
                particleScale = Mathf.Clamp(particleScale, 0.1f, 10f),
                collisionGroupId = enableGrouping ? NormalizeGroupId(collisionGroupId) : string.Empty,
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

            collisionGroupId = NormalizeGroupId(collisionGroupId);

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

            var members = descriptor.GetComponentsInChildren<CollisionDetectionData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(collisionGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.collisionGroupId) != normalizedGroup) continue;
                member.ApplyGroupSettings(settings);
            }
        }

        internal void ApplyGroupSettings(Settings source)
        {
            suppressGroupPropagation = true;
            try
            {
                alwaysReset = source.alwaysReset;
                menuLocation = source.menuLocation;
                globalParameterReset = source.globalParameterReset;
                globalParameterAlwaysReset = source.globalParameterAlwaysReset;
                collisionLayers = source.collisionLayers;
                useTriggers = source.useTriggers;
                particleScale = source.particleScale;
                enableGrouping = source.enableGrouping;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                collisionGroupId = source.collisionGroupId;
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
