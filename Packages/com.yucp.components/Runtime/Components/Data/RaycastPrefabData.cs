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
        
        [Tooltip("Raycast origin: The object that determines the raycast direction. The raycast will be cast from this object's position in its forward direction. The raycast object will be positioned at the hit point using Final IK's Grounder.")]
        [FormerlySerializedAs("castingTarget")]
        public Transform raycastOrigin;

        [Header("Options")]
        [Tooltip("When enabled, an expression menu toggle is generated for this raycast. When disabled, no menu entry is added.")]
        public bool generateMenu = false;

        [Tooltip("Expressions menu path where the control toggle should be created (e.g. \"Utility/Raycast\"). Leave blank to place it at the root menu. Only used when Generate Menu is enabled.")]
        public string menuLocation = "Utility/Raycast";

        [Header("Raycast Settings")]
        [Tooltip("Layers that the Grounder IK will raycast against.")]
        public LayerMask grounderLayers = -1;

        [Tooltip("Maximum raycast distance.")]
        [Range(0.1f, 100f)]
        public float raycastDistance = 10f;

        [Header("Grouping")]
        [Tooltip("Enable to combine multiple components into a shared raycast setup.")]
        public bool enableGrouping = false;

        [Tooltip("Identifier used when grouping is enabled. Components with matching IDs are merged into the same setup.")]
        public string raycastGroupId = "Default";

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

