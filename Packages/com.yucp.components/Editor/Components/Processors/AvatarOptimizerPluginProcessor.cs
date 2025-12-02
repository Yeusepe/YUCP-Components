using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Avatar Optimizer Plugin components during avatar build.
    /// Detects if d4rkAvatarOptimizer is installed and configures it for this specific avatar.
    /// Works on a per-avatar basis - each avatar can have different optimizer settings.
    /// Runs very late in the build pipeline to allow user configuration before optimizer executes.
    /// </summary>
    public class AvatarOptimizerPluginProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 200; // Run very late, but before d4rk optimizer

        private static Type optimizerType;
        private static Type settingsType;
        private static bool hasCheckedForOptimizer = false;
        private static bool isOptimizerInstalled = false;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            // Check if d4rkAvatarOptimizer is installed
            if (!hasCheckedForOptimizer)
            {
                CheckForOptimizer();
            }

            // Look for plugin component on THIS avatar root only (per-avatar basis)
            var plugin = avatarRoot.GetComponent<AvatarOptimizerPluginData>();
            
            if (plugin == null)
            {
                return true; // No plugin on this avatar
            }

            // Detect if this is a clone (NDMF Apply on Play) and find original for stats
            AvatarOptimizerPluginData originalPlugin = plugin;
            bool isClone = avatarRoot.name.Contains("(Clone)");
            
            if (isClone)
            {
                // Try to find the original avatar in the scene
                string originalName = avatarRoot.name.Replace("(Clone)", "").Trim();
                var allAvatars = UnityEngine.Object.FindObjectsOfType<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                
                foreach (var avatar in allAvatars)
                {
                    if (avatar.gameObject.name == originalName && !avatar.gameObject.name.Contains("(Clone)"))
                    {
                        var origPlugin = avatar.GetComponent<AvatarOptimizerPluginData>();
                        if (origPlugin != null)
                        {
                            originalPlugin = origPlugin;
                            Debug.Log($"[AvatarOptimizerPlugin] Processing preview for '{originalName}' - stats will be saved to original component", originalPlugin);
                            break;
                        }
                    }
                }
            }

            if (!isOptimizerInstalled)
            {
                Debug.LogWarning($"[AvatarOptimizerPlugin] d4rkAvatarOptimizer is not installed. Plugin on avatar '{avatarRoot.name}' will be skipped.");
                originalPlugin.SetBuildStats(false, false, "d4rkAvatarOptimizer not installed");
                UnityEditor.EditorUtility.SetDirty(originalPlugin);
                return true;
            }

            YUCPProgressWindow progressWindow = null;
            
            try
            {
                progressWindow = YUCPProgressWindow.Create();
                progressWindow.Progress(0, $"Configuring Avatar Optimizer for '{avatarRoot.name}'...");
                
                ProcessPlugin(plugin, avatarRoot, originalPlugin);
                
                progressWindow.Progress(1f, "Avatar Optimizer configuration complete!");
            }
            finally
            {
                if (progressWindow != null)
                {
                    progressWindow.CloseWindow();
                }
            }

            return true;
        }

        private void CheckForOptimizer()
        {
            hasCheckedForOptimizer = true;
            
            // Try to find d4rkAvatarOptimizer type
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                optimizerType = assembly.GetType("d4rkAvatarOptimizer");
                if (optimizerType != null)
                {
                    settingsType = optimizerType.GetNestedType("Settings");
                    isOptimizerInstalled = true;
                    Debug.Log("[AvatarOptimizerPlugin] d4rkAvatarOptimizer detected!");
                    break;
                }
            }
            
            if (!isOptimizerInstalled)
            {
                Debug.LogWarning("[AvatarOptimizerPlugin] d4rkAvatarOptimizer not found in project. Install it from https://github.com/d4rkc0d3r/d4rkAvatarOptimizer");
            }
        }

        private void ProcessPlugin(AvatarOptimizerPluginData plugin, GameObject avatarRoot, AvatarOptimizerPluginData originalPlugin)
        {
            if (!plugin.enableOptimizer)
            {
                Debug.Log($"[AvatarOptimizerPlugin] Optimizer disabled for avatar '{avatarRoot.name}'", plugin);
                originalPlugin.SetBuildStats(true, false, "Optimizer disabled by user");
                UnityEditor.EditorUtility.SetDirty(originalPlugin);
                
                // Remove optimizer component if it exists
                Component existingOptimizer = avatarRoot.GetComponent(optimizerType);
                if (existingOptimizer != null)
                {
                    UnityEngine.Object.DestroyImmediate(existingOptimizer);
                    Debug.Log($"[AvatarOptimizerPlugin] Removed optimizer component from avatar '{avatarRoot.name}'");
                }
                
                return;
            }

            try
            {
                // Find or create d4rkAvatarOptimizer component on THIS avatar root
                // Each avatar gets its own optimizer instance with unique settings
                Component optimizer = avatarRoot.GetComponent(optimizerType);
                
                if (optimizer == null)
                {
                    // Create optimizer component for this specific avatar
                    optimizer = avatarRoot.AddComponent(optimizerType);
                    Debug.Log($"[AvatarOptimizerPlugin] Created d4rkAvatarOptimizer instance for avatar '{avatarRoot.name}'", plugin);
                }
                else
                {
                    Debug.Log($"[AvatarOptimizerPlugin] Configuring existing d4rkAvatarOptimizer on avatar '{avatarRoot.name}'", plugin);
                }

                // Apply this avatar's specific settings to its optimizer instance
                int appliedCount = ApplySettingsToOptimizer(plugin, optimizer);
                
                // Update the ORIGINAL component with stats (important for preview mode)
                originalPlugin.SetBuildStats(true, true, "", appliedCount);
                UnityEditor.EditorUtility.SetDirty(originalPlugin);
                
                // For scene objects, mark the scene dirty (only in edit mode)
                if (!UnityEditor.EditorUtility.IsPersistent(originalPlugin) && !Application.isPlaying)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(originalPlugin.gameObject.scene);
                }
                
                Debug.Log($"<color=green><b>[AvatarOptimizerPlugin] ✓ Successfully applied {appliedCount} optimization settings to '{avatarRoot.name}'</b></color>\n" +
                         $"Check the Avatar Optimizer Plugin component inspector for details.", originalPlugin);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarOptimizerPlugin] Error processing plugin on avatar '{avatarRoot.name}': {ex.Message}", plugin);
                originalPlugin.SetBuildStats(true, false, ex.Message);
                UnityEditor.EditorUtility.SetDirty(originalPlugin);
                if (!UnityEditor.EditorUtility.IsPersistent(originalPlugin) && !Application.isPlaying)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(originalPlugin.gameObject.scene);
                }
            }
        }

        private int ApplySettingsToOptimizer(AvatarOptimizerPluginData plugin, Component optimizer)
        {
            int appliedCount = 0;
            
            // Configure this specific avatar's optimizer instance with plugin settings
            // Get the settings field from the optimizer instance
            var settingsField = optimizerType.GetField("settings", BindingFlags.Public | BindingFlags.Instance);
            if (settingsField == null)
            {
                Debug.LogError("[AvatarOptimizerPlugin] Could not find 'settings' field on d4rkAvatarOptimizer");
                return 0;
            }

            object settings = settingsField.GetValue(optimizer);
            if (settings == null)
            {
                Debug.LogError("[AvatarOptimizerPlugin] Settings object is null");
                return 0;
            }

            // Apply DoAutoSettings to this avatar's optimizer
            var autoSettingsField = optimizerType.GetField("DoAutoSettings", BindingFlags.Public | BindingFlags.Instance);
            if (autoSettingsField != null)
            {
                autoSettingsField.SetValue(optimizer, plugin.useAutoSettings);
                appliedCount++;
                if (plugin.debugMode)
                {
                    Debug.Log($"[AvatarOptimizerPlugin] Set DoAutoSettings = {plugin.useAutoSettings} for this avatar");
                }
            }

            // Apply all per-avatar settings using reflection
            if (SetSettingField(settings, "ApplyOnUpload", plugin.enableOptimizer, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "WritePropertiesAsStaticValues", plugin.writePropertiesAsStaticValues, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MergeSkinnedMeshes", plugin.mergeSkinnedMeshes, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MergeSkinnedMeshesWithShaderToggle", plugin.mergeSkinnedMeshesWithShaderToggle, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MergeSkinnedMeshesWithNaNimation", plugin.mergeSkinnedMeshesWithNaNimation, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "NaNimationAllow3BoneSkinning", plugin.naNimationAllow3BoneSkinning, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MergeSkinnedMeshesSeparatedByDefaultEnabledState", plugin.mergeSkinnedMeshesSeparatedByDefaultEnabledState, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MergeStaticMeshesAsSkinned", plugin.mergeStaticMeshesAsSkinned, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MergeDifferentPropertyMaterials", plugin.mergeDifferentPropertyMaterials, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MergeSameDimensionTextures", plugin.mergeSameDimensionTextures, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MergeMainTex", plugin.mergeMainTex, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "OptimizeFXLayer", plugin.optimizeFXLayer, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "CombineApproximateMotionTimeAnimations", plugin.combineApproximateMotionTimeAnimations, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "DisablePhysBonesWhenUnused", plugin.disablePhysBonesWhenUnused, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MergeSameRatioBlendShapes", plugin.mergeSameRatioBlendShapes, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "MMDCompatibility", plugin.mmdCompatibility, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "DeleteUnusedComponents", plugin.deleteUnusedComponents, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "DeleteUnusedGameObjects", plugin.deleteUnusedGameObjects, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "UseRingFingerAsFootCollider", plugin.useRingFingerAsFootCollider, plugin.debugMode)) appliedCount++;
            if (SetSettingField(settings, "ProfileTimeUsed", plugin.profileTimeUsed, plugin.debugMode)) appliedCount++;

            // Write settings back
            settingsField.SetValue(optimizer, settings);

            // Apply excluded transforms
            if (plugin.excludeTransforms != null && plugin.excludeTransforms.Count > 0)
            {
                var excludeTransformsField = optimizerType.GetField("ExcludeTransforms", BindingFlags.Public | BindingFlags.Instance);
                if (excludeTransformsField != null)
                {
                    excludeTransformsField.SetValue(optimizer, plugin.excludeTransforms);
                    appliedCount++;
                    
                    if (plugin.debugMode)
                    {
                        Debug.Log($"[AvatarOptimizerPlugin] Set {plugin.excludeTransforms.Count} excluded transforms");
                    }
                }
            }
            
            // Apply inspector display options
            if (ApplyInspectorDisplayOptions(plugin, optimizer)) appliedCount++;

            EditorUtility.SetDirty(optimizer);
            
            return appliedCount;
        }

        private bool SetSettingField(object settings, string fieldName, object value, bool debugMode)
        {
            try
            {
                var field = settingsType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(settings, value);
                    
                    if (debugMode)
                    {
                        Debug.Log($"[AvatarOptimizerPlugin] Set {fieldName} = {value}");
                    }
                    return true;
                }
                else if (debugMode)
                {
                    Debug.LogWarning($"[AvatarOptimizerPlugin] Field '{fieldName}' not found on Settings");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AvatarOptimizerPlugin] Failed to set {fieldName}: {ex.Message}");
            }
            return false;
        }
        
        private bool ApplyInspectorDisplayOptions(AvatarOptimizerPluginData plugin, Component optimizer)
        {
            try
            {
                int count = 0;
                
                // Apply inspector display fields
                var field = optimizerType.GetField("ShowMeshAndMaterialMergePreview", BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(optimizer, plugin.showMeshMergePreview);
                    count++;
                }
                
                field = optimizerType.GetField("ShowFXLayerMergeResults", BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(optimizer, plugin.showFXLayerMergeResults);
                    count++;
                }
                
                field = optimizerType.GetField("ShowDebugInfo", BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(optimizer, plugin.showDebugInfo);
                    count++;
                }
                
                if (plugin.debugMode && count > 0)
                {
                    Debug.Log($"[AvatarOptimizerPlugin] Applied {count} inspector display options");
                }
                
                return count > 0;
            }
            catch (Exception ex)
            {
                if (plugin.debugMode)
                {
                    Debug.LogWarning($"[AvatarOptimizerPlugin] Failed to apply inspector options: {ex.Message}");
                }
                return false;
            }
        }
    }
}

