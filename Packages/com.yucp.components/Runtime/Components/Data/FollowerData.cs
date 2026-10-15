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
    [AddComponentMenu("YUCP/Follower")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Follower")]
    [SupportBanner]
    public class FollowerData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Applied Object")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to follow the player. This applied object will be moved into the Follower's Container during build. If no Applied Object override is set, the component uses the GameObject it's attached to.")]
        [SerializeField, HideInInspector]
        private GameObject _followedObjectInfo;
        
        [Tooltip("The object that follows the player. Empty uses this object.")]
        public Transform appliedObjectOverride;

        [Tooltip("Where the followed object moves to. Empty uses the followed object.")]
        [FormerlySerializedAs("followerTarget")]
        [FormerlySerializedAs("appliedTransform")]
        public Transform positionTarget;

        [Tooltip("What the followed object faces. Empty uses the follower prefab's own look target.")]
        public Transform lookTarget;

        [Header("Options")]
        [Tooltip("Adds a menu toggle that stops the follower in game.")]
        public bool generateMenu = false;

        [Tooltip("Submenu for the toggle, e.g. Utility/Follower. Empty puts it at the root.")]
        public string menuLocation = "Utility/Follower";

        [Tooltip("Optional global parameter that stops the follower. /<object> is appended to keep it unique.")]
        public string globalParameterStop = "";

        [Header("Follow Settings")]
        [Tooltip("How fast the object catches up. Higher is faster.")]
        [Range(0.1f, 5f)]
        public float followSpeed = 1f;

        [Header("Grouping")]
        [Tooltip("Shares one follower setup between components with the same group ID.")]
        public bool enableGrouping = false;

        [Tooltip("Components with the same group ID share one setup.")]
        public string followerGroupId = "Default";

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
            public Transform positionTarget;
            public Transform lookTarget;
            public float followSpeed;
            public bool generateMenu;
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
            if (lastBuildTicks <= 0 || lastBuildTicks > DateTime.MaxValue.Ticks) return null;
            return new DateTime(lastBuildTicks, DateTimeKind.Utc);
        }

        public Settings ToSettings()
        {
            return new Settings
            {
                appliedObject = appliedObjectOverride != null ? appliedObjectOverride.gameObject : gameObject,
                positionTarget = positionTarget,
                lookTarget = lookTarget,
                followSpeed = Mathf.Clamp(followSpeed, 0.1f, 5f),
                generateMenu = generateMenu,
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
            if (!enableGrouping)
            {
                return;
            }

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
                generateMenu = source.generateMenu;
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
