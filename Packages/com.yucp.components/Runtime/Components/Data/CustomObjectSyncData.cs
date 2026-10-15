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
    /// <summary>
    /// Drop-in component that mirrors the original VRLabs Custom Object Sync workflow for a single object.
    /// Configuration data is consumed at build time by the YUCP processor to generate
    /// the animator layers, expression parameters, and prefab wiring required for syncing.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Custom Object Sync")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Custom-Object-Sync")]
    [SupportBanner]
    public class CustomObjectSyncData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        public const string DefaultGroupId = "Default";

        public enum ReferenceFrame
        {
            AvatarCentered,
            WorldSpace
        }

        [Header("Sync Mode")]
        [Tooltip("Sends the position directly for the lowest delay, at a higher parameter cost. Off sends it in steps that use fewer parameters.")]
        public bool quickSync = true;

        [Tooltip("Avatar anchors where syncing starts, so late joiners miss it. World anchors to the world origin and supports late joiners, but needs a larger radius.")]
        public ReferenceFrame referenceFrame = ReferenceFrame.AvatarCentered;

        [Header("Precision & Range")]
        [Tooltip("How far the object can move from its anchor: 2 to this power, in meters. Higher uses more parameters.")]
        [Range(1, 12)]
        public int maxRadius = 7;

        [Tooltip("How finely the position syncs. Higher uses more parameters.")]
        [Range(1, 12)]
        public int positionPrecision = 6;

        [Tooltip("How finely the rotation syncs. Higher uses more parameters.")]
        [Range(0, 12)]
        public int rotationPrecision = 8;

        [Tooltip("Bits sent per step while Quick Sync is off. Fewer bits use fewer parameters but add delay.")]
        [Range(4, 32)]
        public int bitCount = 16;

        [Header("Options")]
        [Tooltip("Syncs the object's rotation as well as its position.")]
        public bool rotationEnabled = true;

        [Tooltip("Remote players see the object ease into new positions instead of jumping.")]
        public bool addDampingConstraint = true;

        [Tooltip("How quickly the object catches up with its target. Lower is smoother and slower.")]
        [Range(0.01f, 1f)]
        public float dampingConstraintValue = 0.15f;

        [Tooltip("Adds a view of where remote players see the object, for testing.")]
        public bool addLocalDebugView = false;

        [Tooltip("The generated animator states use Write Defaults.")]
        public bool writeDefaults = true;

        [Tooltip("Adds a menu toggle that turns syncing on and off. Leave it off if you drive the parameter yourself.")]
        public bool generateMenu = false;

        [Tooltip("Submenu for the toggle, e.g. Utility/Custom Sync. Empty puts it at the root.")]
        public string menuLocation = "Utility/Custom Sync";

        [Tooltip("Parent for the generated target object. Empty uses this object's parent.")]
        [FormerlySerializedAs("syncTarget")]
        public Transform syncParent;

        [Header("Grouping")]
        [Tooltip("Shares one sync setup between components with the same group ID. Uses fewer parameters, but the objects take turns syncing.")]
        public bool enableGrouping = false;

        [Tooltip("Components with the same group ID share one setup.")]
        public string syncGroupId = DefaultGroupId;

        [Header("Visualization")]
        [Tooltip("Shows the sync radius and precision in the Scene view while this object is selected.")]
        public bool showSceneGizmo = true;

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

        /// <summary>
        /// Simplified configuration object consumed by the builder/processor.
        /// </summary>
        [Serializable]
        public class Settings
        {
            public GameObject appliedObject;
            public Transform syncParent;
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
            public bool generateMenu;
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
            if (lastBuildTicks <= 0 || lastBuildTicks > DateTime.MaxValue.Ticks) return null;
            return new DateTime(lastBuildTicks, DateTimeKind.Utc);
        }

        public Settings ToSettings()
        {
            return new Settings
            {
                appliedObject = gameObject,
                syncParent = syncParent,
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
                generateMenu = generateMenu,
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
                generateMenu = source.generateMenu;
                menuLocation = source.menuLocation;
                enableGrouping = source.enableGrouping;
                showSceneGizmo = source.showSceneGizmo;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                syncGroupId = source.syncGroupId;
                syncParent = source.syncParent;
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

