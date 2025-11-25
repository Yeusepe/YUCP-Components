using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Avatar Muscle Poser - Intuitive tool for posing avatar muscles using visual rotation rings.
    /// Hover over body parts in Scene view to see rotation rings, then drag to pose.
    /// Records poses as animations that play when the toggle activates.
    /// </summary>
    [AddComponentMenu("YUCP/Avatar Muscle Poser")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AvatarMusclePoserData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Toggle Configuration")]
        [Tooltip("GameObject that contains the toggle component to use for this pose.")]
        public GameObject toggleObject;

        [Tooltip("The toggle component to use. Auto-detected from toggleObject if not set.")]
        public Component selectedToggle;

        [Header("Pose Settings")]
        [Tooltip("Animation clip to record the pose to. Created automatically when recording.")]
        public AnimationClip poseAnimationClip;

        [Tooltip("Show rotation rings when hovering over bones in Scene view.")]
        public bool showRotationRings = true;

        [Tooltip("Size of rotation rings (in meters).")]
        [Range(0.01f, 0.2f)]
        public float ringSize = 0.05f;

        [Header("Debug & Preview")]
        [Tooltip("Show debug information during build.")]
        public bool debugMode = false;

        [Tooltip("Show preview visualization in Scene view.")]
        public bool showPreview = false;

        [Tooltip("Currently selected bone for posing (read-only).")]
        [SerializeField] private string selectedBoneName = "";

        [Tooltip("Current muscle values (read-only, for preview).")]
        [System.NonSerialized] private Dictionary<string, float> currentMuscleValues = new Dictionary<string, float>();
        
        public string SelectedBoneName => selectedBoneName;
        public Dictionary<string, float> CurrentMuscleValues => currentMuscleValues;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public void SetSelectedBone(string boneName)
        {
            selectedBoneName = boneName;
        }

        public void SetMuscleValues(Dictionary<string, float> values)
        {
            currentMuscleValues = values ?? new Dictionary<string, float>();
        }
    }
}

