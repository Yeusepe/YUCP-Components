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
    [AddComponentMenu("YUCP/Follower")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Follower")]
    [SupportBanner]
    public class FollowerData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Objects")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to follow the player. This object will be moved into the Follower's Container during build. The component automatically uses the GameObject it's attached to as the followed object.")]
        [SerializeField, HideInInspector]
        private GameObject _followedObjectInfo;
        
        [Tooltip("FOLLOWER TARGET: The object that will follow the player. This object will be moved outside the prefab hierarchy and will smoothly follow the player's position using damping constraints.")]
        public Transform followerTarget;

        [Tooltip("LOOK TARGET: The object the follower looks at (for look constraint). If not set, will use Follower Target/Look Target from prefab. This determines what direction the follower faces.")]
        public Transform lookTarget;

        [Header("Options")]
        [Tooltip("Expressions menu path where the control toggle should be created (e.g. \"Utility/Follower\"). Leave blank to place it at the root menu.")]
        public string menuLocation = "Utility/Follower";

        [Tooltip("OPTIONAL: Global parameter name for Follower/Stop. When set, this parameter will be registered as a global parameter that can be controlled by VRChat worlds or external sources. Leave empty to use local parameter only.")]
        public string globalParameterStop = "";

        [Header("Follow Settings")]
        [Tooltip("Follow speed multiplier. Higher values = faster following. This affects the Follow animation clip.")]
        [Range(0.1f, 5f)]
        public float followSpeed = 1f;

        [Header("Grouping")]
        [Tooltip("Enable to combine multiple components into a shared follower setup.")]
        public bool enableGrouping = false;

        [Tooltip("Identifier used when grouping is enabled. Components with matching IDs are merged into the same setup.")]
        public string followerGroupId = "Default";

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
            public Transform followerTarget;
            public Transform lookTarget;
            public float followSpeed;
            public string menuLocation;
            public string globalParameterStop;
            public string followerGroupId;
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
                followerTarget = followerTarget,
                lookTarget = lookTarget,
                followSpeed = Mathf.Clamp(followSpeed, 0.1f, 5f),
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                globalParameterStop = globalParameterStop?.Trim() ?? string.Empty,
                followerGroupId = enableGrouping ? NormalizeGroupId(followerGroupId) : string.Empty,
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

            followerGroupId = NormalizeGroupId(followerGroupId);

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

            var members = descriptor.GetComponentsInChildren<FollowerData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(followerGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.followerGroupId) != normalizedGroup) continue;
                member.ApplyGroupSettings(settings);
            }
        }

        internal void ApplyGroupSettings(Settings source)
        {
            suppressGroupPropagation = true;
            try
            {
                menuLocation = source.menuLocation;
                globalParameterStop = source.globalParameterStop;
                lookTarget = source.lookTarget;
                followSpeed = source.followSpeed;
                enableGrouping = source.enableGrouping;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                followerGroupId = source.followerGroupId;
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

