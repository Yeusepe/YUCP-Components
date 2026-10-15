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
    [AddComponentMenu("YUCP/Position Damping Constraint")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Position-Damping-Constraint")]
    [SupportBanner]
    public class PositionDampingConstraintData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Applied Object")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to dampen. This applied object will be moved into the Damping Constraint's Container during build. The component automatically uses the GameObject it's attached to as the dampened object.")]
        [SerializeField, HideInInspector]
        private GameObject _dampenedObjectInfo;
        
        [Tooltip("What the object follows while Position Target is empty.")]
        [FormerlySerializedAs("targetObject")]
        public Transform appliedTransform;

        [Tooltip("What the object follows, with a delay.")]
        public Transform positionTarget;

        [Header("Damping Settings")]
        [Tooltip("How quickly the object catches up with its target. Lower is smoother and slower.")]
        [Range(0.01f, 1f)]
        public float dampingWeight = 0.15f;

        [Header("Constraint Settings")]
        [Tooltip("Position offset from the target. Applied in local or world space.")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("Position at rest. Default position when constraint weight is 0 or axis is frozen.")]
        public Vector3 positionAtRest = Vector3.zero;

        [Tooltip("Offset added to the target's position.")]
        public Vector3 sourcePositionOffset = Vector3.zero;

        [Header("Grouping")]
        [Tooltip("Shares one constraint setup between components with the same group ID.")]
        public bool enableGrouping = false;

        [Tooltip("Components with the same group ID share one setup.")]
        public string constraintGroupId = "Default";

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
            if (lastBuildTicks <= 0 || lastBuildTicks > DateTime.MaxValue.Ticks) return null;
            return new DateTime(lastBuildTicks, DateTimeKind.Utc);
        }

        public Settings ToSettings()
        {
            return new Settings
            {
                appliedObject = gameObject,
                appliedTransform = appliedTransform,
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

