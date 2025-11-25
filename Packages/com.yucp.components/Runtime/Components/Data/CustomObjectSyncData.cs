using System;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YUCP.Components
{
    /// <summary>
    /// Drop-in component that mirrors the original VRLabs Custom Object Sync workflow for a single object.
    /// Configuration data is consumed at build time by the YUCP processor to generate
    /// the animator layers, expression parameters, and prefab wiring required for syncing.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Custom Object Sync")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Custom-Object-Sync")]
    [SupportBanner("This component ports VRLabs Custom Object Sync (MIT). Please support VRLabs!")]
    public class CustomObjectSyncData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        public const string DefaultGroupId = "Default";

        public enum ReferenceFrame
        {
            AvatarCentered,
            WorldSpace
        }

        [Header("Sync Mode")]
        [Tooltip("Quick Sync sends floats directly (avatar-centered only, higher parameter usage, lowest latency).\n" +
                 "Disable to use the bit-packed multi-step sync that saves parameters at the cost of latency.")]
        public bool quickSync = true;

        [Tooltip("Reference frame used when quick sync is disabled.\n" +
                 "Avatar centered drops an anchor when sync starts (no late join). World space anchors to origin (supports late join, needs larger range).")]
        public ReferenceFrame referenceFrame = ReferenceFrame.AvatarCentered;

        [Header("Precision & Range")]
        [Tooltip("Maximum radius exponent. The actual range is 2^maxRadius meters.\nHigher values = larger range but more bits required.")]
        [Range(1, 12)]
        public int maxRadius = 7;

        [Tooltip("Position precision exponent (fractional bits per axis). Higher = more precision, more bits.")]
        [Range(1, 12)]
        public int positionPrecision = 6;

        [Tooltip("Rotation precision exponent (fractional bits per axis). Only used when rotation sync is enabled.")]
        [Range(0, 12)]
        public int rotationPrecision = 8;

        [Tooltip("Bits per sync step (only used when Quick Sync is disabled). Lower bits reduce parameters but increase latency.")]
        [Range(4, 32)]
        public int bitCount = 16;

        [Header("Options")]
        [Tooltip("Sync rotational data alongside position.")]
        public bool rotationEnabled = true;

        [Tooltip("Adds the damping constraint helper so remote objects ease into their new positions.")]
        public bool addDampingConstraint = true;

        [Tooltip("How aggressively the damping helper chases new positions (0 = never, 1 = instant).")]
        [Range(0.01f, 1f)]
        public float dampingConstraintValue = 0.15f;

        [Tooltip("Adds the local debug view rig so you can visualize the remote copy in Play Mode.")]
        public bool addLocalDebugView = false;

        [Tooltip("When enabled, generated animator states will use Write Defaults.")]
        public bool writeDefaults = true;

        [Tooltip("Expressions menu path where the enable toggle should be created (e.g. \"Utility/Custom Sync\"). Leave blank to place it at the root menu.")]
        public string menuLocation = "Utility/Custom Sync";

        [Header("Grouping")]
        [Tooltip("Enable to combine multiple components into a shared Custom Object Sync rig (reduces parameters, but objects take turns syncing). Leave off for per-object rigs.")]
        public bool enableGrouping = false;

        [Tooltip("Identifier used when grouping is enabled. Components with matching IDs are merged into the same rig.")]
        public string syncGroupId = DefaultGroupId;

        [Header("Visualization")]
        [Tooltip("Draw helper gizmos in the Scene view that illustrate travel radius, damping, and precision.")]
        public bool showSceneGizmo = true;

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

        /// <summary>
        /// Simplified configuration object consumed by the builder/processor.
        /// </summary>
        [Serializable]
        public class Settings
        {
            public GameObject targetObject;
            public bool quickSync;
            public ReferenceFrame referenceFrame;
            public int maxRadius;
            public int positionPrecision;
            public int rotationPrecision;
            public int bitCount;
            public bool rotationEnabled;
            public bool addDampingConstraint;
            public float dampingConstraintValue;
            public bool addLocalDebugView;
            public bool writeDefaults;
            public string menuLocation;
            public string syncGroupId;
            public bool enableGrouping;
            public bool showSceneGizmo;
            public bool verboseLogging;
            public bool includeCredits;
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
                targetObject = gameObject,
                quickSync = quickSync,
                referenceFrame = referenceFrame,
                maxRadius = Mathf.Clamp(maxRadius, 1, 12),
                positionPrecision = Mathf.Clamp(positionPrecision, 1, 12),
                rotationPrecision = Mathf.Clamp(rotationPrecision, 0, 12),
                bitCount = Mathf.Clamp(bitCount, 4, 32),
                rotationEnabled = rotationEnabled,
                addDampingConstraint = addDampingConstraint,
                dampingConstraintValue = Mathf.Clamp01(dampingConstraintValue),
                addLocalDebugView = addLocalDebugView,
                writeDefaults = writeDefaults,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                enableGrouping = enableGrouping,
                syncGroupId = enableGrouping ? NormalizeGroupId(syncGroupId) : string.Empty,
                showSceneGizmo = showSceneGizmo,
                verboseLogging = verboseLogging,
                includeCredits = includeCredits
            };
        }

        public static string NormalizeGroupId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultGroupId;
            }

            return value.Trim();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            syncGroupId = NormalizeGroupId(syncGroupId);

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

            var members = descriptor.GetComponentsInChildren<CustomObjectSyncData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(syncGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.syncGroupId) != normalizedGroup) continue;
                member.ApplyGroupSettings(settings);
            }
        }

        internal void ApplyGroupSettings(Settings source)
        {
            suppressGroupPropagation = true;
            try
            {
                quickSync = source.quickSync;
                referenceFrame = source.referenceFrame;
                maxRadius = source.maxRadius;
                positionPrecision = source.positionPrecision;
                rotationPrecision = source.rotationPrecision;
                bitCount = source.bitCount;
                rotationEnabled = source.rotationEnabled;
                addDampingConstraint = source.addDampingConstraint;
                dampingConstraintValue = source.dampingConstraintValue;
                addLocalDebugView = source.addLocalDebugView;
                writeDefaults = source.writeDefaults;
                menuLocation = source.menuLocation;
                enableGrouping = source.enableGrouping;
                showSceneGizmo = source.showSceneGizmo;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                syncGroupId = source.syncGroupId;
            }
            finally
            {
                suppressGroupPropagation = false;
            }

            EditorUtility.SetDirty(this);
        }

        private void OnDrawGizmosSelected()
        {
            if (!showSceneGizmo) return;

            var radiusMeters = Mathf.Pow(2f, Mathf.Clamp(maxRadius, 1, 12));
            var anchor = transform.position;

            Handles.color = new Color(0.2f, 0.7f, 0.95f, 0.4f);
            Handles.DrawSolidDisc(anchor, Vector3.up, 0.05f);
            Handles.color = new Color(0.2f, 0.7f, 0.95f, 0.85f);
            Handles.DrawWireDisc(anchor, Vector3.up, radiusMeters);
            Handles.DrawWireDisc(anchor, Vector3.right, radiusMeters);

            float precisionStep = Mathf.Pow(0.5f, Mathf.Clamp(positionPrecision, 1, 12)) * 100f;
            var labelPos = anchor + Vector3.up * 0.2f;
            Handles.color = Color.white;
            Handles.Label(labelPos, $"Radius ≈ {radiusMeters:0.#} m\nPrecision ≈ {precisionStep:0.###} cm");

            if (rotationEnabled)
            {
                double rotationStep = rotationPrecision <= 0 ? 360 : 360.0 / Math.Pow(2, rotationPrecision);
                Handles.Label(labelPos + Vector3.up * 0.3f, $"Rotation ≈ {rotationStep:0.###}°");
            }
        }
#endif
    }
}

