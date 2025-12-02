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
    /// Processes closest bone attachment components during avatar build.
    /// Finds the nearest bone (including non-humanoid bones like ears, tails) and creates VRCFury armature links.
    /// Supports bone filtering by name pattern and humanoid bone exclusion.
    /// </summary>
    public class AttachToClosestBoneProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var animator = avatarRoot.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError("[AttachToClosestBoneProcessor] Animator missing on avatar.", avatarRoot);
                return true;
            }

            var dataList = avatarRoot.GetComponentsInChildren<AttachToClosestBoneData>(true);
            
            if (dataList.Length > 0)
            {
                var progressWindow = YUCPProgressWindow.Create();
                progressWindow.Progress(0, "Processing closest bone attachments...");
                
                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];
                    // Find all potential bones in the armature
                    var allBones = FindAllBones(animator, data.transform);
                    
                    // Filter bones using settings
                    var filteredBones = FilterBones(allBones, data, animator);
                    
                    if (filteredBones.Count == 0)
                    {
                        Debug.LogError($"[AttachToClosestBoneProcessor] No bones found matching filter criteria for '{data.name}'", data);
                        continue;
                    }

                    // Find the closest bone
                    Transform closestBone = FindClosestBone(data.transform, filteredBones, data.maxDistance);
                    
                    if (closestBone == null)
                    {
                        Debug.LogError($"[AttachToClosestBoneProcessor] No bones within range for '{data.name}'", data);
                        continue;
                    }

                    // Get the bone path relative to the armature root
                    string bonePath = GetBonePath(closestBone, animator.transform);
                    data.SetSelectedBonePath(bonePath);

                    // Create FuryArmatureLink
                    var link = FuryComponents.CreateArmatureLink(data.gameObject);
                    if (link == null)
                    {
                        Debug.LogError($"[AttachToClosestBoneProcessor] Failed to create FuryArmatureLink on '{data.name}'", data);
                        continue;
                    }

                    // Link to the bone path
                    if (!string.IsNullOrEmpty(data.offset))
                    {
                        link.LinkTo(bonePath + "/" + data.offset);
                    }
                    else
                    {
                        link.LinkTo(bonePath);
                    }

                    float distance = Vector3.Distance(data.transform.position, closestBone.position);
                    Debug.Log($"[AttachToClosestBoneProcessor] Linked '{data.name}' → '{bonePath}' (distance: {distance:F3}m)", data);
                    
                    // Update progress
                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed attachment {i + 1}/{dataList.Length}");
                }
                
                progressWindow.CloseWindow();
            }

            return true;
        }

        private List<Transform> FindAllBones(Animator animator, Transform exclude)
        {
            var bones = new List<Transform>();
            
            // Start from the animator's transform and recursively find all children
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

        private List<Transform> FilterBones(List<Transform> bones, AttachToClosestBoneData data, Animator animator)
        {
            var filtered = new List<Transform>();

            foreach (var bone in bones)
            {
                if (data.ignoreHumanoidBones && IsHumanoidBone(bone, animator))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(data.includeNameFilter))
                {
                    if (!bone.name.ToLower().Contains(data.includeNameFilter.ToLower()))
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(data.excludeNameFilter))
                {
                    if (bone.name.ToLower().Contains(data.excludeNameFilter.ToLower()))
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

        private Transform FindClosestBone(Transform target, List<Transform> bones, float maxDistance)
        {
            Transform closest = null;
            float closestDistance = float.MaxValue;

            foreach (var bone in bones)
            {
                float distance = Vector3.Distance(target.position, bone.position);
                
                if (maxDistance > 0 && distance > maxDistance)
                {
                    continue;
                }

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = bone;
                }
            }

            return closest;
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
    }
}

