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
    [AddComponentMenu("YUCP/Position Damping Constraint")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Position-Damping-Constraint")]
    [SupportBanner]
    public class PositionDampingConstraintData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Objects")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to dampen. This object will be moved into the Damping Constraint's Container during build. The component automatically uses the GameObject it's attached to as the dampened object.")]
        [SerializeField, HideInInspector]
        private GameObject _dampenedObjectInfo;
        
        [Tooltip("DAMPENED OBJECT: The object that will have its position dampened. This is automatically set to the GameObject this component is attached to.")]
        public Transform targetObject;

        [Tooltip("POSITION TARGET: The target position the constraint dampens towards. This object will be moved outside the prefab hierarchy. The dampened object will smoothly follow this target's position based on the damping weight.")]
        public Transform positionTarget;

        [Header("Damping Settings")]
        [Tooltip("Weight of the second source in the constraint. Lower values = stronger damping effect.")]
        [Range(0.01f, 1f)]
        public float dampingWeight = 0.15f;

        [Header("Constraint Settings")]
        [Tooltip("Position offset from the target. Applied in local or world space.")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("Position at rest. Default position when constraint weight is 0 or axis is frozen.")]
        public Vector3 positionAtRest = Vector3.zero;

        [Tooltip("Source position offset. Position offset applied to the constraint source.")]
        public Vector3 sourcePositionOffset = Vector3.zero;

        [Header("Grouping")]
        [Tooltip("Enable to combine multiple components into a shared constraint setup.")]
        public bool enableGrouping = false;

        [Tooltip("Identifier used when grouping is enabled. Components with matching IDs are merged into the same setup.")]
        public string constraintGroupId = "Default";

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
            public Transform targetTransform;
            public Transform positionTarget;
            public float dampingWeight;
            public Vector3 positionOffset;
            public Vector3 positionAtRest;
            public Vector3 sourcePositionOffset;
            public string constraintGroupId;
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
                targetTransform = targetObject,
                positionTarget = positionTarget,
                dampingWeight = Mathf.Clamp(dampingWeight, 0.01f, 1f),
                positionOffset = positionOffset,
                positionAtRest = positionAtRest,
                sourcePositionOffset = sourcePositionOffset,
                constraintGroupId = enableGrouping ? NormalizeGroupId(constraintGroupId) : string.Empty,
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

            constraintGroupId = NormalizeGroupId(constraintGroupId);

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

            var members = descriptor.GetComponentsInChildren<PositionDampingConstraintData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(constraintGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.constraintGroupId) != normalizedGroup) continue;
                member.ApplyGroupSettings(settings);
            }
        }

        internal void ApplyGroupSettings(Settings source)
        {
            suppressGroupPropagation = true;
            try
            {
                dampingWeight = source.dampingWeight;
                enableGrouping = source.enableGrouping;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                constraintGroupId = source.constraintGroupId;
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

