using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// How the grip animation is triggered at runtime in VRChat.
    /// </summary>
    public enum ActivationMode
    {
        /// <summary>Uses existing ParameterToggleData or VRCFury toggle.</summary>
        UseExistingActivationSource,
        /// <summary>Binds to VRChat gesture parameters (GestureLeft/GestureRight).</summary>
        BindToGestureLeftRight,
        /// <summary>Binds to pickup IsHeld parameter.</summary>
        BindToPickupIsHeld,
        /// <summary>Always active when object is enabled.</summary>
        AlwaysOnWhenObjectEnabled,
        /// <summary>Creates a menu toggle (if custom menu creation is implemented).</summary>
        CreateMenuToggle
    }

    /// <summary>
    /// How to select the hand mesh for radius estimation.
    /// </summary>
    public enum HandMeshSelectionMode
    {
        /// <summary>Auto-select SkinnedMeshRenderer with highest weight influence from hand bones.</summary>
        Auto,
        /// <summary>Use user-specified SkinnedMeshRenderer.</summary>
        Manual,
        /// <summary>Use bone lengths + default radii if no reliable mesh weights found.</summary>
        FallbackToBoneLengths
    }

    /// <summary>
    /// Finger types for per-finger overrides.
    /// </summary>
    public enum YUCPFingerType
    {
        Thumb,
        Index,
        Middle,
        Ring,
        Little
    }

    /// <summary>
    /// AutoGrip - Automatically generates mesh-aware hand poses for gripping props.
    /// Add this component to any prop object. During build, it generates a grip pose
    /// that avoids clipping, bakes it to an AnimationClip, and wires it to activation.
    /// </summary>
    [SupportBanner]
    [AddComponentMenu("YUCP/Auto Grip")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    public class AutoGripData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Activation")]
        [Tooltip("How the grip animation is triggered at runtime.")]
        public ActivationMode activationMode = ActivationMode.UseExistingActivationSource;

        [Tooltip("Existing toggle or activation source (required if using UseExistingActivationSource).")]
        public UnityEngine.Object activationSource;

        [Header("Hand Selection")]
        [Tooltip("Which hand(s) to generate grip for.")]
        public HandTarget handTarget = HandTarget.Both;

        [Tooltip("Optional override for the hand target transform.")]
        public Transform handTargetOverride;

        [Tooltip("Grip point - where the prop sits relative to the hand. Uses object origin if not set.")]
        public Transform gripPoint;

        [Tooltip("Generate grip pose for left hand.")]
        public bool generateLeftHand = true;

        [Tooltip("Generate grip pose for right hand.")]
        public bool generateRightHand = true;

        [Header("Generated Assets")]
        [Tooltip("Generated animation clip for left hand grip.")]
        public AnimationClip leftHandClip;

        [Tooltip("Generated animation clip for right hand grip.")]
        public AnimationClip rightHandClip;

        [Tooltip("Cache for bake diagnostics and input hash.")]
        public AutoGripBakeCache bakeCache;

        [Header("Build Options")]
        [Tooltip("Automatically bake grip pose during avatar build if needed.")]
        public bool autoBakeOnBuild = true;

        [Tooltip("Automatically wire generated clips to activation system.")]
        public bool autoWireToToggle = true;

        [Header("Solver Settings")]
        [Tooltip("Finger padding distance in millimeters to avoid clipping.")]
        [Range(0f, 10f)]
        public float fingerPaddingMm = 2f;

        [Tooltip("Number of solver iterations for optimization.")]
        [Range(10, 200)]
        public int solverIterations = 50;

        [Tooltip("Collision mask for prop colliders.")]
        public LayerMask collisionMask = ~0;

        [Tooltip("Enable verbose logging during bake.")]
        public bool verboseLogging = false;

        [Header("Advanced")]
        [Tooltip("How to select the hand mesh for radius estimation.")]
        public HandMeshSelectionMode handMeshSelection = HandMeshSelectionMode.Auto;

        [Tooltip("Manual SkinnedMeshRenderer to use when handMeshSelection is Manual.")]
        public SkinnedMeshRenderer manualHandMesh;

        [Tooltip("Per-finger radius overrides (in meters). Leave at 0 for auto-detection.")]
        public FingerRadiusOverrides fingerRadiusOverrides = new FingerRadiusOverrides();

        // IPreprocessCallbackBehaviour implementation
        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;
    }

    /// <summary>
    /// Per-finger radius overrides for grip calculation.
    /// </summary>
    [Serializable]
    public class FingerRadiusOverrides
    {
        [Range(0f, 0.05f)] public float thumb = 0f;
        [Range(0f, 0.05f)] public float index = 0f;
        [Range(0f, 0.05f)] public float middle = 0f;
        [Range(0f, 0.05f)] public float ring = 0f;
        [Range(0f, 0.05f)] public float little = 0f;

        public float GetRadius(YUCPFingerType finger)
        {
            switch (finger)
            {
                case YUCPFingerType.Thumb: return thumb;
                case YUCPFingerType.Index: return index;
                case YUCPFingerType.Middle: return middle;
                case YUCPFingerType.Ring: return ring;
                case YUCPFingerType.Little: return little;
                default: return 0f;
            }
        }
    }
}
