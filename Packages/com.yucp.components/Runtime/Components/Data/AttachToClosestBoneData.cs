using UnityEngine;
using VRC.SDKBase;  // for IEditorOnly & IPreprocessCallbackBehaviour

namespace YUCP.Components
{
    [AddComponentMenu("YUCP/Closest Bone Auto-Link")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AttachToClosestBoneData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Tooltip("Optional offset string for fine tuning the attachment.")]
        public string offset = "";

        [Tooltip("Search radius in meters. Only bones within this distance will be considered. Set to 0 for unlimited range.")]
        [Min(0f)]
        public float maxDistance = 0f;

        [Header("Bone Filtering")]
        [Tooltip("If set, only consider bones whose names contain this text (case-insensitive).")]
        public string includeNameFilter = "";

        [Tooltip("If set, ignore bones whose names contain this text (case-insensitive).")]
        public string excludeNameFilter = "";

        [Tooltip("If true, ignore humanoid bones and only consider extra bones.")]
        public bool ignoreHumanoidBones = false;

        [Header("Debug")]
        [Tooltip("The bone path that was selected at build time (read-only, populated by processor).")]
        [SerializeField] private string selectedBonePath = "";

        public string SelectedBonePath => selectedBonePath;
        
        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public void SetSelectedBonePath(string path)
        {
            selectedBonePath = path;
        }
    }
}

