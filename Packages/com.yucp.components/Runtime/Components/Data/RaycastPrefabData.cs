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
    [AddComponentMenu("YUCP/Raycast Prefab")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Raycast-Prefab")]
    [SupportBanner("This component ports VRLabs Raycast Prefab (MIT). Please support VRLabs!")]
    public class RaycastPrefabData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target")]
        [Tooltip("The casting target object that determines the raycast direction.")]
        public Transform castingTarget;

        [Header("Options")]
        [Tooltip("Expressions menu path where the control toggle should be created (e.g. \"Utility/Raycast\"). Leave blank to place it at the root menu.")]
        public string menuLocation = "Utility/Raycast";

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
            public GameObject targetObject;
            public Transform castingTarget;
            public string menuLocation;
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
                targetObject = gameObject,
                castingTarget = castingTarget,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
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
                menuLocation = source.menuLocation;
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

