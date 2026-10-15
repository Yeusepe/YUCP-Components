using UnityEngine;
using VRC.SDKBase;  // for IEditorOnly & IPreprocessCallbackBehaviour

namespace YUCP.Components
{
    [SupportBanner]
    [AddComponentMenu("YUCP/Closest Bone Auto-Link")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AttachToClosestBoneData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Tooltip("Optional. Links to this child path under the bone instead of the bone itself.")]
        public string offset = "";

        [Tooltip("Only bones within this many meters are considered. 0 searches the whole avatar.")]
        [Min(0f)]
        public float maxDistance = 0f;

        [Header("Bone Filtering")]
        [Tooltip("Optional. Only bones whose names contain this text are considered. Not case-sensitive.")]
        public string includeNameFilter = "";

        [Tooltip("Optional. Bones whose names contain this text are skipped. Not case-sensitive.")]
        public string excludeNameFilter = "";

        [Tooltip("Considers only bones that aren't part of the humanoid rig.")]
        public bool ignoreHumanoidBones = false;

        [Header("Debug")]
        [Tooltip("The bone this object was linked to in the last build.")]
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

