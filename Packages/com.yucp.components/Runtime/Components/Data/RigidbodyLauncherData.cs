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
    [AddComponentMenu("YUCP/Rigidbody Launcher")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Rigidbody-Launcher")]
    [SupportBanner("This component ports VRLabs Rigidbody Launcher (MIT). Please support VRLabs!")]
    public class RigidbodyLauncherData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target")]
        [Tooltip("The launcher target object that will be moved outside the prefab hierarchy.")]
        public Transform launcherTarget;

        [Header("Options")]
        [Tooltip("Expressions menu path where the control toggle should be created (e.g. \"Utility/Launcher\"). Leave blank to place it at the root menu.")]
        public string menuLocation = "Utility/Launcher";

        [Header("Grouping")]
        [Tooltip("Enable to combine multiple components into a shared launcher setup.")]
        public bool enableGrouping = false;

        [Tooltip("Identifier used when grouping is enabled. Components with matching IDs are merged into the same setup.")]
        public string launcherGroupId = "Default";

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
            public Transform launcherTarget;
            public string menuLocation;
            public string launcherGroupId;
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
                launcherTarget = launcherTarget,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                launcherGroupId = enableGrouping ? NormalizeGroupId(launcherGroupId) : string.Empty,
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

            launcherGroupId = NormalizeGroupId(launcherGroupId);

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

            var members = descriptor.GetComponentsInChildren<RigidbodyLauncherData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(launcherGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.launcherGroupId) != normalizedGroup) continue;
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
                launcherGroupId = source.launcherGroupId;
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

