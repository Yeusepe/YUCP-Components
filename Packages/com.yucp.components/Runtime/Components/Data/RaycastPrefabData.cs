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
    [AddComponentMenu("YUCP/Raycast Prefab")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Raycast-Prefab")]
    [SupportBanner]
    public class RaycastPrefabData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Applied Object")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to position via raycast. This applied object will be moved into the Raycast Prefab's Container during build. The component automatically uses the GameObject it's attached to as the raycast object.")]
        [SerializeField, HideInInspector]
        private GameObject _raycastObjectInfo;
        
        [Tooltip("The ray starts here and points along its forward axis. The object moves to where the ray hits.")]
        [FormerlySerializedAs("castingTarget")]
        public Transform raycastOrigin;

        [Header("Options")]
        [Tooltip("Adds a menu toggle for the raycast.")]
        public bool generateMenu = false;

        [Tooltip("Submenu for the toggle, e.g. Utility/Raycast. Empty puts it at the root.")]
        public string menuLocation = "Utility/Raycast";

        [Header("Raycast Settings")]
        [Tooltip("Layers the ray can hit.")]
        public LayerMask grounderLayers = -1;

        [Tooltip("How far the ray reaches.")]
        [Range(0.1f, 100f)]
        public float raycastDistance = 10f;

        [Header("Grouping")]
        [Tooltip("Shares one raycast setup between components with the same group ID.")]
        public bool enableGrouping = false;

        [Tooltip("Components with the same group ID share one setup.")]
        public string raycastGroupId = "Default";

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
            public Transform raycastOrigin;
            public bool generateMenu;
            public string menuLocation;
            public LayerMask grounderLayers;
            public float raycastDistance;
            public string raycastGroupId;
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
                raycastOrigin = raycastOrigin,
                generateMenu = generateMenu,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                grounderLayers = grounderLayers,
                raycastDistance = Mathf.Clamp(raycastDistance, 0.1f, 100f),
                raycastGroupId = enableGrouping ? NormalizeGroupId(raycastGroupId) : string.Empty,
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

            raycastGroupId = NormalizeGroupId(raycastGroupId);

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

            var members = descriptor.GetComponentsInChildren<RaycastPrefabData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(raycastGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.raycastGroupId) != normalizedGroup) continue;
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
                grounderLayers = source.grounderLayers;
                raycastDistance = source.raycastDistance;
                enableGrouping = source.enableGrouping;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                raycastGroupId = source.raycastGroupId;
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

