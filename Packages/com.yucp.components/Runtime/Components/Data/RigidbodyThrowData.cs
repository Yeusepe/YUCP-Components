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
    [AddComponentMenu("YUCP/Rigidbody Throw")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules/wiki/Rigidbody-Throw")]
    [SupportBanner("This component ports VRLabs Rigidbody Throw (MIT). Please support VRLabs!")]
    public class RigidbodyThrowData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Objects")]
        [Tooltip("ATTACH THIS COMPONENT to the GameObject you want to throw. This object will be moved into the Rigidbody Throw's Container during build. The component automatically uses the GameObject it's attached to as the thrown object.")]
        [SerializeField, HideInInspector]
        private GameObject _thrownObjectInfo;
        
        [Tooltip("THROW TARGET: The object that will be thrown. This object will be moved outside the prefab hierarchy and will be thrown when the gesture condition is met. Uses particle system and contacts to sync position (and optionally rotation) across clients.")]
        public Transform throwTarget;

        [Header("Options")]
        [Tooltip("Enable rotation sync for the thrown object (requires additional parameters).")]
        public bool enableRotationSync = false;

        [Tooltip("Expressions menu path where the control toggle should be created (e.g. \"Utility/Throw\"). Leave blank to place it at the root menu.")]
        public string menuLocation = "Utility/Throw";

        [Header("Throw Settings")]
        [Tooltip("Physics material for collision. Applied to the Collision Collider.")]
        public PhysicMaterial physicsMaterial;

        [Tooltip("Gesture value for throwing (default: 2 = HandOpen).")]
        [Range(0, 7)]
        public int throwGesture = 2;

        [Tooltip("Gesture value for resetting (default: 1 = Fist).")]
        [Range(0, 7)]
        public int resetGesture = 1;

        [Tooltip("Layers that collision detection will use.")]
        public LayerMask collisionLayers = -1;

        [Header("Grouping")]
        [Tooltip("Enable to combine multiple components into a shared throw setup.")]
        public bool enableGrouping = false;

        [Tooltip("Identifier used when grouping is enabled. Components with matching IDs are merged into the same setup.")]
        public string throwGroupId = "Default";

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
            public Transform throwTarget;
            public bool enableRotationSync;
            public string menuLocation;
            public PhysicMaterial physicsMaterial;
            public int throwGesture;
            public int resetGesture;
            public LayerMask collisionLayers;
            public string throwGroupId;
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
                throwTarget = throwTarget,
                enableRotationSync = enableRotationSync,
                menuLocation = menuLocation?.Trim() ?? string.Empty,
                physicsMaterial = physicsMaterial,
                throwGesture = Mathf.Clamp(throwGesture, 0, 7),
                resetGesture = Mathf.Clamp(resetGesture, 0, 7),
                collisionLayers = collisionLayers,
                throwGroupId = enableGrouping ? NormalizeGroupId(throwGroupId) : string.Empty,
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

            throwGroupId = NormalizeGroupId(throwGroupId);

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

            var members = descriptor.GetComponentsInChildren<RigidbodyThrowData>(true);
            if (members == null || members.Length == 0) return;

            var normalizedGroup = NormalizeGroupId(throwGroupId);
            var settings = ToSettings();

            foreach (var member in members)
            {
                if (member == null || member == this) continue;
                if (NormalizeGroupId(member.throwGroupId) != normalizedGroup) continue;
                member.ApplyGroupSettings(settings);
            }
        }

        internal void ApplyGroupSettings(Settings source)
        {
            suppressGroupPropagation = true;
            try
            {
                enableRotationSync = source.enableRotationSync;
                menuLocation = source.menuLocation;
                physicsMaterial = source.physicsMaterial;
                throwGesture = source.throwGesture;
                resetGesture = source.resetGesture;
                collisionLayers = source.collisionLayers;
                enableGrouping = source.enableGrouping;
                verboseLogging = source.verboseLogging;
                includeCredits = source.includeCredits;
                throwGroupId = source.throwGroupId;
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

