using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components
{
    [Serializable]
    public class BlendshapeMapping
    {
        public string sourceName;
        public string targetName;
        public float confidence = 1f;
        public bool isManualOverride = false;
        public MappingStatus status = MappingStatus.Unknown;
        public string notes = "";
    }

    [Serializable]
    public class ComponentMapping
    {
        public string componentType;
        public string sourceBonePath;
        public string targetBonePath;
        public bool enabled = true;
        public string notes = "";
    }

    [Serializable]
    public class BoneMatchingSettings
    {
        public BoneMatchingMode mode = BoneMatchingMode.NameBased;
        public bool ignoreHumanoidBones = true;
        public string includeNameFilter = "";
        public string excludeNameFilter = "";
        public float maxDistance = 0.1f;
    }

    [Serializable]
    public class AvatarDescriptorSettings
    {
        public bool transferVisemes = true;
        public bool transferEyeLook = true;
        public bool transferViewPosition = true;
        public bool transferCollision = true;
        public bool transferAnimationOverrides = true;
        public bool transferExpressionsMenu = true;
        public bool transferExpressionParameters = true;
        
        [Tooltip("Viseme blendshape mappings")]
        public List<BlendshapeMapping> visemeMappings = new List<BlendshapeMapping>();
        
        [Tooltip("Eye look bone mappings")]
        public List<ComponentMapping> eyeLookMappings = new List<ComponentMapping>();
    }

    [Serializable]
    public class AnimationRemapping
    {
        public string clipPath;
        public bool enabled = true;
        public List<BlendshapeMapping> curveMappings = new List<BlendshapeMapping>();
        public string notes = "";
    }

    [Serializable]
    public class TransferReport
    {
        public string timestamp;
        public string sourceVariant;
        public List<string> targetVariants = new List<string>();
        public List<string> successfulTransfers = new List<string>();
        public List<string> warnings = new List<string>();
        public List<string> errors = new List<string>();
        public int totalMappings;
        public int successfulMappings;
        public int failedMappings;
    }

    public enum MappingStatus
    {
        Unknown,
        Mapped,
        MissingInTarget,
        Conflict,
        ManualOverride
    }

    public enum BoneMatchingMode
    {
        NameBased,
        PathBased,
        Hybrid
    }
}
