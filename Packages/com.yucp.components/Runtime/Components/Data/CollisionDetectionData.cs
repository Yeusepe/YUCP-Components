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
    [SupportBanner("This component ports VRLabs Collision Detection (MIT). Please support VRLabs!")]
    public class CollisionDetectionData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Object")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to detect collisions on. This object will be moved into the collision detection system's Container during build. The component automatically uses the GameObject it's attached to as the target.")]
        [SerializeField, HideInInspector]
        private GameObject _targetObjectInfo;
        
        [Header("Options")]
        [Tooltip("Whether IsColliding should reset immediately after stopping collision, or stay on until Reset is enabled.")]
        public bool alwaysReset = false;

        [Tooltip("Expressions menu path where the reset toggle should be created (e.g. \"Utility/Collision\"). Leave blank to place it at the root menu.")]
        public string menuLocation = "Utility/Collision";

        [Header("Collision Settings")]
        [Tooltip("Layers that the particle system will detect collisions with.")]
        public LayerMask collisionLayers = -1;

        [Tooltip("Use trigger colliders instead of collision detection. When enabled, the collision module is disabled and triggers module is enabled.")]
        public bool useTriggers = false;

        [Tooltip("Scale of the collision detection area. This affects the size of the particle system bounds.")]
        [Range(0.1f, 10f)]
        public float particleScale = 1f;

        [Header("Grouping")]
        [Tooltip("Enable to combine multiple components into a shared collision detection setup.")]
        public bool enableGrouping = false;

        [Tooltip("Identifier used when grouping is enabled. Components with matching IDs are merged into the same setup.")]
        public string collisionGroupId = "Default";

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
            public bool alwaysReset;
            public string menuLocation;
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
                alwaysReset = alwaysReset,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
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

