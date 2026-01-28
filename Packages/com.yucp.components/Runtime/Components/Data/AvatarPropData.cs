using System;
using UnityEngine;
using VRC.SDKBase;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YUCP.Components
{
    /// <summary>
    /// Automatic Avatar Prop component that handles FX controller merging and toggle creation at build time.
    /// Based on ThatFatKidsMom's Avatar Prop system.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Avatar Prop")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Avatar-Prop")]
    [SupportBanner]
    public class AvatarPropData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Custom Prop")]
        [Tooltip("Custom prop to replace the default sword. Attach this component to your custom prop GameObject, " +
                 "or leave empty to use the default sword from the prefab.")]
        public GameObject customProp;

        [Header("Toggle Settings")]
        [Tooltip("Menu path for the toggle (e.g., 'Props/Avatar Prop'). Leave empty for root menu.")]
        public string menuLocation = "";

        [Tooltip("Display name for the toggle in the expressions menu.")]
        public string toggleName = "Avatar Prop";

        [Tooltip("Prop starts enabled by default.")]
        public bool defaultOn = false;

        [Tooltip("Save toggle state across avatar reloads.")]
        public bool saved = false;

        [Header("Credits")]
        [Tooltip("Include credit message for ThatFatKidsMom's Avatar Prop system.")]
        public bool includeCredits = true;

        [Header("Diagnostics")]
        [Tooltip("Print additional information while building.")]
        public bool verboseLogging = false;

        [SerializeField, HideInInspector]
        private string lastBuildSummary;

        [SerializeField, HideInInspector]
        private long lastBuildTicks;

        /// <summary>
        /// Settings object consumed by the processor.
        /// </summary>
        [Serializable]
        public class Settings
        {
            public GameObject appliedObject;
            public GameObject customProp;
            public string menuLocation;
            public string toggleName;
            public bool defaultOn;
            public bool saved;
            public bool includeCredits;
            public bool verboseLogging;
        }

        public int PreprocessOrder => 0;

        public bool OnPreprocess() => true;

        /// <summary>
        /// Used by the processor to capture build results for inspector display.
        /// </summary>
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
                customProp = customProp,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                toggleName = string.IsNullOrWhiteSpace(toggleName) ? "Avatar Prop" : toggleName.Trim(),
                defaultOn = defaultOn,
                saved = saved,
                includeCredits = includeCredits,
                verboseLogging = verboseLogging
            };
        }
    }
}
