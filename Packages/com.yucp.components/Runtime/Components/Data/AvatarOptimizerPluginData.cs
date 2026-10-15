using UnityEngine;
using VRC.SDKBase;
using System.Collections.Generic;

namespace YUCP.Components
{
    /// <summary>
    /// YUCP plugin for d4rkAvatarOptimizer integration.
    /// Allows configuring optimizer settings per-avatar with automatic detection.
    /// Must be placed on the avatar root (same level as VRCAvatarDescriptor).
    /// Only works if d4rkAvatarOptimizer is installed in the project.
    /// </summary>
    [SupportBanner]
    [BetaWarning("This component is in beta. The optimizer integration may need manual settings.")]
    [AddComponentMenu("YUCP/Avatar Optimizer Plugin")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor))]
    public class AvatarOptimizerPluginData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Optimizer")]
        [Tooltip("Optimizes this avatar with d4rkAvatarOptimizer when it's built or uploaded.")]
        public bool enableOptimizer = true;
        
        [Tooltip("Lets the optimizer choose settings from the avatar's complexity, in place of the manual settings. Recommended for most avatars.")]
        public bool useAutoSettings = true;
        
        [Header("Meshes")]
        [Tooltip("Merges skinned meshes into fewer meshes, which cuts draw calls and often the material count.")]
        public bool mergeSkinnedMeshes = true;
        
        [Tooltip("Turns static meshes into skinned meshes so they can be merged too.")]
        public bool mergeStaticMeshesAsSkinned = false;
        
        [Header("Mesh toggles")]
        [Tooltip("Lets meshes with different materials merge by toggling them in the shader. Advanced merges more; Windows build target only.")]
        [Range(0, 2)]
        public int mergeSkinnedMeshesWithShaderToggle = 0;
        
        [Tooltip("Lets meshes that are toggled separately merge by hiding them with NaN values in animations. Advanced merges more.")]
        [Range(0, 2)]
        public int mergeSkinnedMeshesWithNaNimation = 0;
        
        [Tooltip("Allows 3-bone skin weights so more meshes can merge. Deformation can change slightly.")]
        public bool naNimationAllow3BoneSkinning = false;
        
        [Tooltip("Merges meshes that start hidden separately from meshes that start visible, so default visibility stays the same.")]
        public bool mergeSkinnedMeshesSeparatedByDefaultEnabledState = true;
        
        [Header("Materials")]
        [Tooltip("Merges materials whose properties differ by generating a combined shader. Windows build target only; test the avatar afterwards.")]
        public bool mergeDifferentPropertyMaterials = false;
        
        [Tooltip("Packs merged textures of the same size into texture arrays, which saves texture memory.")]
        public bool mergeSameDimensionTextures = false;
        
        [Tooltip("Includes the main texture in the texture arrays. Check the result, because main texture quality can drop.")]
        public bool mergeMainTex = false;
        
        [Tooltip("Writes material properties that aren't animated as fixed values in the generated shaders, which makes them faster. Always on while materials are merged.")]
        public bool writePropertiesAsStaticValues = false;
        
        [Header("FX layer")]
        [Tooltip("Removes unused FX layers and merges compatible ones, which makes the animator cheaper.")]
        public bool optimizeFXLayer = true;
        
        [Tooltip("Combines animations of about the same length, which removes more layers.")]
        public bool combineApproximateMotionTimeAnimations = false;
        
        [Header("Components")]
        [Tooltip("Turns off PhysBones that aren't used, such as ones that move nothing visible.")]
        public bool disablePhysBonesWhenUnused = true;
        
        [Tooltip("Deletes components that don't affect the avatar, such as disabled and editor-only ones.")]
        public bool deleteUnusedComponents = true;
        
        [Tooltip("Deletes objects the avatar doesn't use. Safe deletes only clearly unused objects; Aggressive deletes more.")]
        [Range(0, 2)]
        public int deleteUnusedGameObjects = 0;
        
        [Header("Blendshapes")]
        [Tooltip("Combines blendshapes that are always animated together at the same ratio, which reduces the blendshape count.")]
        public bool mergeSameRatioBlendShapes = true;
        
        [Tooltip("Keeps MMD blendshapes from being merged or renamed, so MMD dances still move the face.")]
        public bool mmdCompatibility = true;
        
        [Header("Contacts")]
        [Tooltip("Moves the ring finger contacts to the feet, which frees a contact slot. Ring fingers stop interacting.")]
        public bool useRingFingerAsFootCollider = false;
        
        [Header("Exclusions")]
        [Tooltip("Objects the optimizer leaves alone, with their children. Use it for penetrators, pose bones or systems that break when optimized.")]
        public List<Transform> excludeTransforms = new List<Transform>();
        
        [Header("Logging")]
        [Tooltip("Logs how long each optimization step takes.")]
        public bool profileTimeUsed = false;
        
        [Tooltip("Logs each setting as it's applied to the optimizer.")]
        public bool debugMode = false;
        
        [Header("Optimizer inspector")]
        [Tooltip("Shows the mesh merge preview in d4rkAvatarOptimizer's inspector.")]
        public bool showMeshMergePreview = true;
        
        [Tooltip("Shows the FX layer merge results in d4rkAvatarOptimizer's inspector.")]
        public bool showFXLayerMergeResults = true;
        
        [Tooltip("Shows debug information in d4rkAvatarOptimizer's inspector.")]
        public bool showDebugInfo = false;
        
        [Header("Last build")]
        [Tooltip("Whether the last build found d4rkAvatarOptimizer.")]
        [SerializeField] private bool optimizerDetected = false;
        
        [Tooltip("Whether the last build applied the settings.")]
        [SerializeField] private bool optimizationApplied = false;
        
        [Tooltip("Why the last build didn't apply the settings, if it didn't.")]
        [SerializeField] private string buildError = "";
        
        [Tooltip("How many settings the last build applied.")]
        [SerializeField] private int optimizationsApplied = 0;
        
        public bool OptimizerDetected => optimizerDetected;
        public bool OptimizationApplied => optimizationApplied;
        public string BuildError => buildError;
        public int OptimizationsApplied => optimizationsApplied;
        
        public int PreprocessOrder => int.MaxValue - 100; // Run very late, before d4rk optimizer
        public bool OnPreprocess() => true;
        
        public void SetBuildStats(bool detected, bool applied, string error = "", int count = 0)
        {
            optimizerDetected = detected;
            optimizationApplied = applied;
            buildError = error;
            optimizationsApplied = count;
        }
        
        private void Reset()
        {
            // Initialize with safe recommended defaults
            enableOptimizer = true;
            useAutoSettings = true;
            mergeSkinnedMeshes = true;
            optimizeFXLayer = true;
            disablePhysBonesWhenUnused = true;
            mergeSameRatioBlendShapes = true;
            mmdCompatibility = true;
            deleteUnusedComponents = true;
            showMeshMergePreview = true;
            showFXLayerMergeResults = true;
        }
        
        /// <summary>
        /// Get count of enabled features
        /// </summary>
        public int GetEnabledOptimizationCount()
        {
            int count = 0;
            
            if (mergeSkinnedMeshes) count++;
            if (mergeStaticMeshesAsSkinned) count++;
            if (mergeDifferentPropertyMaterials) count++;
            if (mergeSameDimensionTextures) count++;
            if (mergeMainTex) count++;
            if (mergeSkinnedMeshesWithShaderToggle > 0) count++;
            if (mergeSkinnedMeshesWithNaNimation > 0) count++;
            if (naNimationAllow3BoneSkinning) count++;
            if (!mergeSkinnedMeshesSeparatedByDefaultEnabledState) count++; // Inverted logic
            if (optimizeFXLayer) count++;
            if (combineApproximateMotionTimeAnimations) count++;
            if (disablePhysBonesWhenUnused) count++;
            if (deleteUnusedComponents) count++;
            if (deleteUnusedGameObjects > 0) count++;
            if (mergeSameRatioBlendShapes) count++;
            if (writePropertiesAsStaticValues) count++;
            if (useRingFingerAsFootCollider) count++;
            
            return count;
        }
    }
}
