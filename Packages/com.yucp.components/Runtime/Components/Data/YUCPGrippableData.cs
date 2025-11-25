using UnityEngine;
using VRC.SDKBase;
using YUCP.Components.HandPoses;

namespace YUCP.Components
{
    /// <summary>
    /// Component for objects that can be gripped with hand poses.
    /// References a hand pose asset that defines how the hand should grip this object.
    /// </summary>
    [AddComponentMenu("YUCP/Grippable Object")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class YUCPGrippableData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Grip Configuration")]
        [Tooltip("The hand pose asset that defines how to grip this object.")]
        public YUCPHandPoseAsset handPoseAsset;

        [Header("Grip Target")]
        [Tooltip("The object that will be gripped (usually this GameObject or a child mesh).")]
        public Transform grippedObject;

        [Tooltip("Which hand(s) should grip this object.\n\n" +
                 "• Left: Always left hand\n" +
                 "• Right: Always right hand\n" +
                 "• Both: Create grips for both hands with separate toggles\n" +
                 "• Closest: Automatically detect nearest hand")]
        public HandTarget targetHand = HandTarget.Closest;

        [Header("Toggle Settings")]
        [Tooltip("Menu path for the toggle (e.g., 'Props/Phone').\n\n" +
                 "If using Both hands, will create 'Props/Phone L' and 'Props/Phone R'")]
        public string menuPath = "Props/Object";

        [Tooltip("Save toggle state across avatar reloads.")]
        public bool saved = true;

        [Tooltip("Object starts enabled by default.")]
        public bool defaultOn = false;

        [Tooltip("Optional global parameter name for syncing across network.\n\n" +
                 "Leave empty for local-only toggle.\n" +
                 "If set, creates a synced bool parameter with this name.")]
        public string globalParameter = "";

        [Header("Blend Pose Settings")]
        [Tooltip("Blend value for blend poses (0.0 = open, 1.0 = closed). Only used if hand pose is a blend pose.")]
        [Range(0f, 1f)]
        public float blendValue = 1.0f;

        [Header("Debug")]
        [Tooltip("Show debug information during build.")]
        public bool debugMode = false;

        [SerializeField] private string generatedGripInfo = "";
        public string GeneratedGripInfo => generatedGripInfo;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public void SetGeneratedInfo(string info)
        {
            generatedGripInfo = info;
        }

        private void Reset()
        {
            if (grippedObject == null)
            {
                grippedObject = transform;
            }
        }
    }
}

