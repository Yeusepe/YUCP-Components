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
    [BetaWarning("This component is in BETA and may not work as intended. Avatar optimization integration is experimental and may require manual configuration.")]
    [AddComponentMenu("YUCP/Avatar Optimizer Plugin")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor))]
    public class AvatarOptimizerPluginData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("▼ General Configuration")]
        [Tooltip("Enable optimizer for this avatar during upload/build")]
        public bool enableOptimizer = true;
        
        [Tooltip("Automatically configure settings based on avatar complexity\n\n" +
                 "Recommended for most users - the optimizer will analyze your avatar and choose appropriate settings.")]
        public bool useAutoSettings = true;
        
        [Header("▼ Mesh Merging")]
        [Tooltip("Merge multiple skinned meshes into fewer meshes to reduce draw calls\n\n" +
                 "Benefits:\n" +
                 "• Reduces skinned mesh count\n" +
                 "• Improves rendering performance\n" +
                 "• May reduce material count\n\n" +
                 "Recommended: ON")]
        public bool mergeSkinnedMeshes = true;
        
        [Tooltip("Convert static (non-skinned) meshes to skinned meshes so they can be merged\n\n" +
                 "Use when: You have many static meshes that could be combined\n" +
                 "Skip when: Static meshes are used for specific performance reasons")]
        public bool mergeStaticMeshesAsSkinned = false;
        
        [Header("▼ Advanced Mesh Merging")]
        [Tooltip("Use shader toggle system for mesh merging\n\n" +
                 "0 = Disabled (standard merging)\n" +
                 "1 = Basic (compatible shader toggles)\n" +
                 "2 = Advanced (aggressive shader toggles)\n\n" +
                 "Requires: Windows build target\n" +
                 "Benefit: Can merge meshes with different materials more aggressively")]
        [Range(0, 2)]
        public int mergeSkinnedMeshesWithShaderToggle = 0;
        
        [Tooltip("Use NaNimation technique for mesh merging\n\n" +
                 "0 = Disabled\n" +
                 "1 = Basic NaNimation\n" +
                 "2 = Advanced NaNimation\n\n" +
                 "How it works: Uses NaN (Not a Number) in animations to hide meshes instead of toggling\n" +
                 "Benefit: Can merge meshes that have different visibility states")]
        [Range(0, 2)]
        public int mergeSkinnedMeshesWithNaNimation = 0;
        
        [Tooltip("Allow 3-bone skinning weights when using NaNimation\n\n" +
                 "ON: More aggressive merging, may affect mesh quality slightly\n" +
                 "OFF: Conservative merging, maintains 4-bone weights")]
        public bool naNimationAllow3BoneSkinning = false;
        
        [Tooltip("Keep meshes with different default enabled states separated during merging\n\n" +
                 "ON: Safer, maintains visibility behavior\n" +
                 "OFF: More aggressive, may change default visibility")]
        public bool mergeSkinnedMeshesSeparatedByDefaultEnabledState = true;
        
        [Header("▼ Material Optimization")]
        [Tooltip("Merge materials with different property values\n\n" +
                 "Requires: Windows build target (custom shader support)\n" +
                 "How it works: Creates optimized shaders that combine multiple materials\n" +
                 "Benefit: Dramatically reduces material count\n\n" +
                 "Warning: Advanced feature, test thoroughly")]
        public bool mergeDifferentPropertyMaterials = false;
        
        [Tooltip("Merge textures of the same dimensions into texture arrays\n\n" +
                 "Requires: mergeDifferentPropertyMaterials = ON\n" +
                 "Benefit: Reduces texture memory usage\n" +
                 "Compatible: Most modern shaders")]
        public bool mergeSameDimensionTextures = false;
        
        [Tooltip("Include _MainTex in texture merging\n\n" +
                 "Requires: mergeSameDimensionTextures = ON\n" +
                 "Warning: May affect main texture quality, test carefully")]
        public bool mergeMainTex = false;
        
        [Tooltip("Write non-animated material properties as static values in generated shaders\n\n" +
                 "Benefit: Reduces shader parameters and improves performance\n" +
                 "Automatic: Enabled when using material merging features")]
        public bool writePropertiesAsStaticValues = false;
        
        [Header("▼ FX Layer Optimization")]
        [Tooltip("Optimize and merge FX animator layers\n\n" +
                 "What it does:\n" +
                 "• Removes empty/unused layers\n" +
                 "• Merges compatible layers\n" +
                 "• Optimizes layer structure\n\n" +
                 "Benefit: Reduces animator complexity\n" +
                 "Recommended: ON")]
        public bool optimizeFXLayer = true;
        
        [Tooltip("Combine animations that have approximately the same motion time\n\n" +
                 "Requires: optimizeFXLayer = ON\n" +
                 "What it does: Merges animations with similar durations to reduce layer count\n" +
                 "Use when: You have many similar-length animations")]
        public bool combineApproximateMotionTimeAnimations = false;
        
        [Header("▼ Component Cleanup")]
        [Tooltip("Automatically disable PhysBones when they are not being used\n\n" +
                 "Detection:\n" +
                 "• Checks if PhysBone transforms are animated\n" +
                 "• Checks if PhysBone affects visible meshes\n" +
                 "• Disables if completely unused\n\n" +
                 "Recommended: ON")]
        public bool disablePhysBonesWhenUnused = true;
        
        [Tooltip("Delete components that don't affect the avatar\n\n" +
                 "Removed components:\n" +
                 "• Unused MonoBehaviours\n" +
                 "• Empty/disabled components\n" +
                 "• IEditorOnly components\n\n" +
                 "Recommended: ON")]
        public bool deleteUnusedComponents = true;
        
        [Tooltip("Delete GameObjects that are not used\n\n" +
                 "0 = Disabled (keep all GameObjects)\n" +
                 "1 = Safe (only delete clearly unused objects)\n" +
                 "2 = Aggressive (delete more aggressively)\n\n" +
                 "Recommended: 1 (Safe) for most avatars")]
        [Range(0, 2)]
        public int deleteUnusedGameObjects = 0;
        
        [Header("▼ Blendshape Optimization")]
        [Tooltip("Merge blendshapes that always move together with the same ratio\n\n" +
                 "What it does:\n" +
                 "• Detects blendshapes animated identically\n" +
                 "• Combines them into single blendshapes\n" +
                 "• Reduces blendshape count\n\n" +
                 "Benefit: Better performance, fewer blendshape slots used\n" +
                 "Recommended: ON")]
        public bool mergeSameRatioBlendShapes = true;
        
        [Tooltip("Preserve MikuMikuDance (MMD) blendshape compatibility\n\n" +
                 "What it does:\n" +
                 "• Keeps common MMD blendshape names\n" +
                 "• Prevents merging/renaming MMD-specific shapes\n\n" +
                 "Enable if: Using MMD models or animations\n" +
                 "Disable if: Not using MMD content")]
        public bool mmdCompatibility = true;
        
        [Header("▼ Advanced Features")]
        [Tooltip("Use ring finger bone as foot collider\n\n" +
                 "What it does: Moves ring finger contact receivers to feet\n" +
                 "Why: Ring finger collisions are rarely used in VRChat\n" +
                 "Benefit: Frees up a contact receiver slot\n\n" +
                 "Warning: Disables ring finger interactions")]
        public bool useRingFingerAsFootCollider = false;
        
        [Header("▼ Exclusions")]
        [Tooltip("GameObjects/Transforms to completely exclude from all optimizations\n\n" +
                 "Use for:\n" +
                 "• Custom systems that break with optimization\n" +
                 "• Penetrators or posebones\n" +
                 "• Specific accessories that need to stay separate\n\n" +
                 "Excluded objects and their children won't be touched by the optimizer")]
        public List<Transform> excludeTransforms = new List<Transform>();
        
        [Header("▼ Debug & Profiling")]
        [Tooltip("Show detailed time profiling for each optimization step\n\n" +
                 "Displays:\n" +
                 "• Time spent on each optimization\n" +
                 "• Performance bottlenecks\n" +
                 "• Optimization order\n\n" +
                 "Use for: Understanding what takes the longest")]
        public bool profileTimeUsed = false;
        
        [Tooltip("Enable detailed debug logging\n\n" +
                 "Shows:\n" +
                 "• Which settings are applied\n" +
                 "• Reflection calls\n" +
                 "• Configuration steps\n\n" +
                 "Use for: Troubleshooting issues")]
        public bool debugMode = false;
        
        [Header("▼ Inspector Display Options")]
        [Tooltip("Show d4rkAvatarOptimizer's mesh merge preview in this component's inspector")]
        public bool showMeshMergePreview = true;
        
        [Tooltip("Show d4rkAvatarOptimizer's FX layer merge results in this component's inspector")]
        public bool showFXLayerMergeResults = true;
        
        [Tooltip("Show detailed debug information in this component's inspector")]
        public bool showDebugInfo = false;
        
        [Header("■ Build Statistics (Read-only)")]
        [Tooltip("Whether d4rkAvatarOptimizer was detected at build time")]
        [SerializeField] private bool optimizerDetected = false;
        
        [Tooltip("Whether optimization was applied")]
        [SerializeField] private bool optimizationApplied = false;
        
        [Tooltip("Build-time error message if any")]
        [SerializeField] private string buildError = "";
        
        [Tooltip("Number of optimizations applied")]
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
