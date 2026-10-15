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
    [AddComponentMenu("YUCP/Contact Tracker")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Contact-Tracker")]
    [SupportBanner]
    public class ContactTrackerData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Applied Object")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to track contacts for. This applied object will be moved into the Contact Tracker's Container during build. The component automatically uses the GameObject it's attached to as the tracked object.")]
        [SerializeField, HideInInspector]
        private GameObject _trackedObjectInfo;
        
        [Tooltip("Where the tracker is anchored. Empty keeps the prefab's default position.")]
        [FormerlySerializedAs("trackerTarget")]
        public Transform appliedTransform;

        [Header("Options")]
        [Tooltip("Submenu for the tracker toggle, e.g. Utility/Tracker. Empty adds no toggle.")]
        public string menuLocation = "Utility/Tracker";

        [Tooltip("Optional global parameter that controls the tracker. /<object> is appended to keep it unique.")]
        public string globalParameterControl = "";

        [Header("Contact Settings")]
        [Tooltip("The collision tag each direction's contact detects.")]
        public string[] collisionTags = new string[6] { "Head", "Head", "Head", "Head", "Head", "Head" };

        [Tooltip("Size of the tracker while it isn't tracking.")]
        [Range(0f, 1f)]
        public float sizeParameter = 0f;

        [Header("Grouping")]
        [Tooltip("Shares one contact tracker setup between components with the same group ID.")]
        public bool enableGrouping = false;

        [Tooltip("Components with the same group ID share one setup.")]
        public string trackerGroupId = "Default";

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
            public string[] collisionTags;
            public float sizeParameter;
            public string trackerGroupId;
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
            var tags = new string[6];
            if (collisionTags != null && collisionTags.Length >= 6)
            {
                Array.Copy(collisionTags, tags, 6);
            }
            else
            {
                for (int i = 0; i < 6; i++)
                {
                    tags[i] = collisionTags != null && i < collisionTags.Length ? collisionTags[i] : "Head";
                }
            }

            return new Settings
            {
                appliedObject = gameObject,
                appliedTransform = appliedTransform,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                globalParameterControl = globalParameterControl?.Trim() ?? string.Empty,
                collisionTags = tags,
                sizeParameter = Mathf.Clamp01(sizeParameter),
                trackerGroupId = enableGrouping ? NormalizeGroupId(trackerGroupId) : string.Empty,
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

            trackerGroupId = NormalizeGroupId(trackerGroupId);

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

            var members = descriptor.GetComponentsInChildren<ContactTrackerData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(trackerGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.trackerGroupId) != normalizedGroup) continue;
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
                if (source.collisionTags != null && source.collisionTags.Length == 6)
                {
                    collisionTags = (string[])source.collisionTags.Clone();
                }
                sizeParameter = source.sizeParameter;
                enableGrouping = source.enableGrouping;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                trackerGroupId = source.trackerGroupId;
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
