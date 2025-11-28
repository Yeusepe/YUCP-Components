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
    [AddComponentMenu("YUCP/Contact Tracker")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Contact-Tracker")]
    [SupportBanner("This component ports VRLabs Contact Tracker (MIT). Please support VRLabs!")]
    public class ContactTrackerData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Objects")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to track contacts for. This object will be moved into the Contact Tracker's Container during build. The component automatically uses the GameObject it's attached to as the tracked object.")]
        [SerializeField, HideInInspector]
        private GameObject _trackedObjectInfo;
        
        [Tooltip("TRACKER TARGET: The object that will be moved outside the prefab and positioned based on contact detection. This is the visual/functional object that follows the tracked contacts (e.g., a hand tracker that follows hand contacts).")]
        public Transform trackerTarget;

        [Header("Options")]
        [Tooltip("Expressions menu path where the control toggle should be created (e.g. \"Utility/Tracker\"). Leave blank to place it at the root menu.")]
        public string menuLocation = "Utility/Tracker";

        [Tooltip("OPTIONAL: Global parameter name for ContactTracker/Control. When set, this parameter will be registered as a global parameter that can be controlled by VRChat worlds or external sources. Leave empty to use local parameter only.")]
        public string globalParameterControl = "";

        [Header("Contact Settings")]
        [Tooltip("Collision tags for the 6 proximity contacts. Order: X+, X-, Y+, Y-, Z+, Z-")]
        public string[] collisionTags = new string[6] { "Head", "Head", "Head", "Head", "Head", "Head" };

        [Tooltip("Size parameter value for ContactTracker/Size. This sets the default size when not tracking.")]
        [Range(0f, 1f)]
        public float sizeParameter = 0f;

        [Header("Grouping")]
        [Tooltip("Enable to combine multiple components into a shared contact tracker setup.")]
        public bool enableGrouping = false;

        [Tooltip("Identifier used when grouping is enabled. Components with matching IDs are merged into the same setup.")]
        public string trackerGroupId = "Default";

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
            public Transform trackerTarget;
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
                targetObject = gameObject,
                trackerTarget = trackerTarget,
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

