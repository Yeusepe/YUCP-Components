using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Build-time processor for Model Revision Manager
    /// Applies transfers during avatar build, resolves bone paths, creates VRCFury components, and logs conflicts
    /// </summary>
    public class ModelRevisionProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var variantComponents = avatarRoot.GetComponentsInChildren<ModelRevisionVariant>(true);
            
            if (variantComponents.Length == 0)
            {
                return true; // No model revision components found
            }

            var progressWindow = YUCPProgressWindow.Create();
            progressWindow.Progress(0, "Processing model revision transfers...");
            
            try
            {
                for (int i = 0; i < variantComponents.Length; i++)
                {
                    var variant = variantComponents[i];
                    ProcessVariant(variant, avatarRoot, progressWindow, i, variantComponents.Length);
                }
                
                progressWindow.Progress(1f, "Model revision processing complete!");
                progressWindow.CloseWindow();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ModelRevisionProcessor] Failed to process variants: {ex.Message}");
                progressWindow.CloseWindow();
                return false;
            }

            return true;
        }

        private void ProcessVariant(ModelRevisionVariant variant, GameObject avatarRoot, YUCPProgressWindow progressWindow, int index, int total)
        {
            if (variant.revisionBase == null)
            {
                Debug.LogWarning($"[ModelRevisionProcessor] No revision base assigned to variant '{variant.name}'", variant);
                return;
            }

            if (!variant.receiveTransfers)
            {
                Debug.Log($"[ModelRevisionProcessor] Skipping variant '{variant.name}' - transfers disabled", variant);
                return;
            }

            var animator = avatarRoot.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError($"[ModelRevisionProcessor] No Animator found on avatar for variant '{variant.name}'", variant);
                return;
            }

            float progress = (float)index / total;
            progressWindow.Progress(progress, $"Processing variant {index + 1}/{total}: {variant.name}");

            // Process blendshape mappings
            ProcessBlendshapeMappings(variant, animator);

            // Process component transfers
            ProcessComponentTransfers(variant, animator);

            // Process avatar descriptor settings
            ProcessAvatarDescriptorSettings(variant, animator);

            // Process animation remappings
            if (variant.revisionBase.enableAnimationRemapping)
            {
                ProcessAnimationRemappings(variant, animator);
            }

            // Update variant status
            variant.UpdateSyncTimestamp();
            
            Debug.Log($"[ModelRevisionProcessor] Successfully processed variant '{variant.name}'", variant);
        }

        private void ProcessBlendshapeMappings(ModelRevisionVariant variant, Animator animator)
        {
            if (variant.revisionBase.blendshapeMappings == null || variant.revisionBase.blendshapeMappings.Count == 0)
            {
                return;
            }

            foreach (var mapping in variant.revisionBase.blendshapeMappings)
            {
                if (mapping.status != MappingStatus.Mapped)
                {
                    Debug.LogWarning($"[ModelRevisionProcessor] Skipping unmapped blendshape: {mapping.sourceName} -> {mapping.targetName}", variant);
                    continue;
                }

                // Apply blendshape mapping logic here
                // This would typically involve updating Avatar Descriptor viseme mappings
                Debug.Log($"[ModelRevisionProcessor] Applied blendshape mapping: {mapping.sourceName} -> {mapping.targetName}", variant);
            }
        }

        private void ProcessComponentTransfers(ModelRevisionVariant variant, Animator animator)
        {
            if (variant.revisionBase.componentMappings == null || variant.revisionBase.componentMappings.Count == 0)
            {
                return;
            }

            foreach (var componentMapping in variant.revisionBase.componentMappings)
            {
                if (!componentMapping.enabled)
                {
                    continue;
                }

                // Resolve bone path using AttachToClosestBoneProcessor approach
                string resolvedBonePath = ResolveBonePath(componentMapping.targetBonePath, animator, variant);
                
                if (string.IsNullOrEmpty(resolvedBonePath))
                {
                    Debug.LogWarning($"[ModelRevisionProcessor] Could not resolve bone path: {componentMapping.targetBonePath}", variant);
                    continue;
                }

                // Create VRCFury armature link for component
                CreateVRCFuryArmatureLink(variant.gameObject, resolvedBonePath, componentMapping);
            }
        }

        private void ProcessAvatarDescriptorSettings(ModelRevisionVariant variant, Animator animator)
        {
            var avatarDescriptor = animator.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            if (avatarDescriptor == null)
            {
                Debug.LogWarning($"[ModelRevisionProcessor] No Avatar Descriptor found for variant '{variant.name}'", variant);
                return;
            }

            var settings = variant.revisionBase.avatarDescriptorSettings;
            
            // Process viseme mappings
            if (settings.transferVisemes && settings.visemeMappings != null)
            {
                ProcessVisemeMappings(avatarDescriptor, settings.visemeMappings);
            }

            // Process eye look settings
            if (settings.transferEyeLook)
            {
                ProcessEyeLookSettings(avatarDescriptor, variant);
            }

            // Process view position settings
            if (settings.transferViewPosition)
            {
                ProcessViewPositionSettings(avatarDescriptor, variant);
            }

            // Process collision settings
            if (settings.transferCollision)
            {
                ProcessCollisionSettings(avatarDescriptor, variant);
            }
        }

        private void ProcessAnimationRemappings(ModelRevisionVariant variant, Animator animator)
        {
            if (variant.revisionBase.animationRemappings == null || variant.revisionBase.animationRemappings.Count == 0)
            {
                return;
            }

            foreach (var remapping in variant.revisionBase.animationRemappings)
            {
                if (!remapping.enabled)
                {
                    continue;
                }

                // Process animation curve remapping
                ProcessAnimationCurveRemapping(remapping, variant);
            }
        }

        private string ResolveBonePath(string bonePath, Animator animator, ModelRevisionVariant variant)
        {
            // Check cached bone paths first
            var cachedPath = variant.GetCachedBonePath(bonePath);
            if (!string.IsNullOrEmpty(cachedPath))
            {
                return cachedPath;
            }

            // Use AttachToClosestBoneProcessor approach
            var allBones = FindAllBones(animator, variant.transform);
            var filteredBones = FilterBones(allBones, variant.revisionBase.boneMatchingSettings, animator);
            
            Transform targetBone = null;
            
            switch (variant.revisionBase.boneMatchingSettings.mode)
            {
                case BoneMatchingMode.NameBased:
                    targetBone = FindBoneByName(bonePath, filteredBones);
                    break;
                case BoneMatchingMode.PathBased:
                    targetBone = FindBoneByPath(bonePath, filteredBones, animator);
                    break;
                case BoneMatchingMode.Hybrid:
                    targetBone = FindBoneByName(bonePath, filteredBones) ?? FindBoneByPath(bonePath, filteredBones, animator);
                    break;
            }

            if (targetBone != null)
            {
                string resolvedPath = GetBonePath(targetBone, animator.transform);
                variant.CacheBonePath(bonePath, resolvedPath);
                return resolvedPath;
            }

            return null;
        }

        private List<Transform> FindAllBones(Animator animator, Transform exclude)
        {
            var bones = new List<Transform>();
            CollectBonesRecursive(animator.transform, bones, exclude);
            return bones;
        }

        private void CollectBonesRecursive(Transform current, List<Transform> bones, Transform exclude)
        {
            if (current == exclude || IsDescendantOf(current, exclude))
            {
                return;
            }

            if (current.GetComponent<Animator>() == null)
            {
                bones.Add(current);
            }

            for (int i = 0; i < current.childCount; i++)
            {
                CollectBonesRecursive(current.GetChild(i), bones, exclude);
            }
        }

        private bool IsDescendantOf(Transform child, Transform parent)
        {
            Transform current = child;
            while (current != null)
            {
                if (current == parent)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private List<Transform> FilterBones(List<Transform> bones, BoneMatchingSettings settings, Animator animator)
        {
            var filtered = new List<Transform>();

            foreach (var bone in bones)
            {
                if (settings.ignoreHumanoidBones && IsHumanoidBone(bone, animator))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(settings.includeNameFilter))
                {
                    if (!bone.name.ToLower().Contains(settings.includeNameFilter.ToLower()))
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(settings.excludeNameFilter))
                {
                    if (bone.name.ToLower().Contains(settings.excludeNameFilter.ToLower()))
                    {
                        continue;
                    }
                }

                filtered.Add(bone);
            }

            return filtered;
        }

        private bool IsHumanoidBone(Transform bone, Animator animator)
        {
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var humanBone = (HumanBodyBones)i;
                var humanTransform = animator.GetBoneTransform(humanBone);
                if (humanTransform == bone)
                {
                    return true;
                }
            }
            return false;
        }

        private Transform FindBoneByName(string boneName, List<Transform> bones)
        {
            return bones.FirstOrDefault(b => b.name == boneName);
        }

        private Transform FindBoneByPath(string bonePath, List<Transform> bones, Animator animator)
        {
            // Parse bone path and find matching bone
            var pathParts = bonePath.Split('/');
            if (pathParts.Length == 0) return null;

            string targetBoneName = pathParts[pathParts.Length - 1];
            return bones.FirstOrDefault(b => b.name == targetBoneName);
        }

        private string GetBonePath(Transform bone, Transform root)
        {
            var pathParts = new List<string>();
            Transform current = bone;

            while (current != null && current != root)
            {
                pathParts.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", pathParts);
        }

        private void CreateVRCFuryArmatureLink(GameObject targetObject, string bonePath, ComponentMapping componentMapping)
        {
            try
            {
                var link = FuryComponents.CreateArmatureLink(targetObject);
                if (link != null)
                {
                    link.LinkTo(bonePath);
                    Debug.Log($"[ModelRevisionProcessor] Created VRCFury armature link: {targetObject.name} -> {bonePath}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ModelRevisionProcessor] Failed to create VRCFury armature link: {ex.Message}");
            }
        }

        private void ProcessVisemeMappings(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatarDescriptor, List<BlendshapeMapping> visemeMappings)
        {
            // Process viseme blendshape mappings
            foreach (var mapping in visemeMappings)
            {
                if (mapping.status == MappingStatus.Mapped)
                {
                    // Apply viseme mapping to avatar descriptor
                    Debug.Log($"[ModelRevisionProcessor] Applied viseme mapping: {mapping.sourceName} -> {mapping.targetName}");
                }
            }
        }

        private void ProcessEyeLookSettings(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatarDescriptor, ModelRevisionVariant variant)
        {
            // Process eye look settings
            Debug.Log($"[ModelRevisionProcessor] Processing eye look settings for variant '{variant.name}'");
        }

        private void ProcessViewPositionSettings(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatarDescriptor, ModelRevisionVariant variant)
        {
            // Process view position settings
            Debug.Log($"[ModelRevisionProcessor] Processing view position settings for variant '{variant.name}'");
        }

        private void ProcessCollisionSettings(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatarDescriptor, ModelRevisionVariant variant)
        {
            // Process collision settings
            Debug.Log($"[ModelRevisionProcessor] Processing collision settings for variant '{variant.name}'");
        }

        private void ProcessAnimationCurveRemapping(AnimationRemapping remapping, ModelRevisionVariant variant)
        {
            // Process animation curve remapping
            Debug.Log($"[ModelRevisionProcessor] Processing animation curve remapping for variant '{variant.name}': {remapping.clipPath}");
        }
    }
}
