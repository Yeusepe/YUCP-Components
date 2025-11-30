using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;
using YUCP.Components.Editor.Utils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Attach to Blendshape components during avatar build.
    /// Detects surface clusters, samples blendshape deformations, solves transforms,
    /// generates animation clips, and creates VRCFury components for dynamic positioning.
    /// </summary>
    public class AttachToBlendshapeProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 10;


        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<AttachToBlendshapeData>(true);

            if (dataList.Length == 0)
            {
                return true;
            }

            var progressWindow = YUCPProgressWindow.Create();
            progressWindow.Progress(0, "Processing blendshape attachments...");

            try
            {
                var animator = avatarRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[AttachToBlendshapeProcessor] No Animator found on avatar");
                    progressWindow.CloseWindow();
                    return true;
                }

                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];

                    if (!ValidateData(data))
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Validation failed for '{data.name}'", data);
                        continue;
                    }

                    try
                    {
                        ProcessAttachment(data, avatarRoot, animator);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Error processing '{data.name}': {ex.Message}", data);
                        Debug.LogException(ex);
                    }

                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed blendshape attachment {i + 1}/{dataList.Length}");
                }

                progressWindow.CloseWindow();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Fatal error: {ex.Message}");
                progressWindow.CloseWindow();
                return false;
            }

            return true;
        }

        private bool ValidateData(AttachToBlendshapeData data)
        {
            if (data.targetMesh == null)
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Target mesh is not set", data);
                return false;
            }

            if (data.targetMesh.sharedMesh == null)
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Target mesh has no mesh data", data);
                return false;
            }

            if (!PoseSampler.HasBlendshapes(data.targetMesh))
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Target mesh has no blendshapes", data);
                return false;
            }

            if (data.trackingMode == BlendshapeTrackingMode.Specific && 
                (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0))
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Specific mode requires at least one blendshape name", data);
                return false;
            }

            return true;
        }

        private void ProcessAttachment(AttachToBlendshapeData data, GameObject avatarRoot, Animator animator)
        {
            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Processing attachment for '{data.name}'", data);
            }

            // Step 1: Detect surface cluster
            SurfaceCluster cluster = SurfaceClusterDetector.DetectCluster(
                data.targetMesh,
                data.transform.position,
                data.clusterTriangleCount,
                data.searchRadius,
                data.manualTriangleIndex);

            if (cluster == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to detect surface cluster for '{data.name}'", data);
                return;
            }

            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Detected cluster with {cluster.anchors.Count} triangles", data);
            }

            // Step 2: Determine which blendshapes to track
            List<string> blendshapesToTrack = DetermineBlendshapesToTrack(data, avatarRoot, cluster);

            if (blendshapesToTrack.Count == 0)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No blendshapes to track for '{data.name}'", data);
                return;
            }

            Debug.Log($"[AttachToBlendshapeProcessor] Tracking {blendshapesToTrack.Count} blendshapes: {string.Join(", ", blendshapesToTrack)}", data);

            // Step 3: Create base bone attachment
            string bonePath = "";
            if (data.attachToClosestBone)
            {
                bonePath = AttachToClosestBone(data, animator);
                if (data.debugMode)
                {
                    Debug.Log($"[AttachToBlendshapeProcessor] Attached to bone: '{bonePath}'", data);
                }
            }

            // Step 4: Transfer blendshapes to target mesh
            bool transferSuccess = BlendshapeTransfer.TransferBlendshapes(
                data.targetMesh,
                data.targetMeshToModify,
                blendshapesToTrack,
                cluster,
                data);

            if (!transferSuccess)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to transfer blendshapes for '{data.name}'", data);
                return;
            }

            // Step 5: Set build statistics
            data.SetBuildStats(cluster, blendshapesToTrack, blendshapesToTrack.Count, bonePath);

            Debug.Log($"[AttachToBlendshapeProcessor] Successfully processed '{data.name}': " +
                     $"Transferred {blendshapesToTrack.Count} blendshapes, {cluster.anchors.Count} triangle cluster", data);
        }

        private List<string> DetermineBlendshapesToTrack(
            AttachToBlendshapeData data,
            GameObject avatarRoot,
            SurfaceCluster cluster)
        {
            List<string> blendshapes = new List<string>();
            Mesh mesh = data.targetMesh.sharedMesh;

            switch (data.trackingMode)
            {
                case BlendshapeTrackingMode.All:
                    blendshapes = PoseSampler.GetAllBlendshapeNames(mesh);
                    Debug.Log($"[AttachToBlendshapeProcessor] All mode: tracking {blendshapes.Count} blendshapes");
                    break;

                case BlendshapeTrackingMode.Specific:
                    blendshapes = new List<string>(data.specificBlendshapes);
                    // Validate that they exist
                    blendshapes = blendshapes.Where(name => mesh.GetBlendShapeIndex(name) >= 0).ToList();
                    Debug.Log($"[AttachToBlendshapeProcessor] Specific mode: tracking {blendshapes.Count} blendshapes");
                    break;

                case BlendshapeTrackingMode.VisemsOnly:
                    blendshapes = VRChatVisemeDetector.GetVisemeBlendshapes(data.targetMesh, avatarRoot);
                    Debug.Log($"[AttachToBlendshapeProcessor] Viseme mode: tracking {blendshapes.Count} viseme blendshapes");
                    break;

                case BlendshapeTrackingMode.Smart:
                    blendshapes = VRChatVisemeDetector.DetectActiveBlendshapes(
                        data.targetMesh,
                        cluster,
                        data.smartDetectionThreshold);
                    Debug.Log($"[AttachToBlendshapeProcessor] Smart mode: detected {blendshapes.Count} active blendshapes");
                    break;
            }

            return blendshapes;
        }

        private string AttachToClosestBone(AttachToBlendshapeData data, Animator animator)
        {
            // Find all bones
            List<Transform> allBones = FindAllBones(animator, data.transform);

            // Filter bones
            List<Transform> filteredBones = FilterBones(allBones, data, animator);

            if (filteredBones.Count == 0)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No bones found for '{data.name}'", data);
                return "";
            }

            // Find closest bone
            Transform closestBone = FindClosestBone(data.transform, filteredBones, data.boneSearchRadius);

            if (closestBone == null)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No bone within range for '{data.name}'", data);
                return "";
            }

            // Get bone path
            string bonePath = GetBonePath(closestBone, animator.transform);

            // Create VRCFury armature link
            var link = FuryComponents.CreateArmatureLink(data.gameObject);
            if (link == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to create armature link for '{data.name}'", data);
                return bonePath;
            }

            // Link to bone
            if (!string.IsNullOrEmpty(data.boneOffset))
            {
                link.LinkTo(bonePath + "/" + data.boneOffset);
            }
            else
            {
                link.LinkTo(bonePath);
            }

            float distance = Vector3.Distance(data.transform.position, closestBone.position);
            Debug.Log($"[AttachToBlendshapeProcessor] Linked '{data.name}' to bone '{bonePath}' (distance: {distance:F3}m)", data);

            return bonePath;
        }


        private string GetRelativePath(Transform target, Transform root)
        {
            if (target == root)
                return "";

            List<string> path = new List<string>();
            Transform current = target;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        // Bone finding utilities (similar to AttachToClosestBoneProcessor)
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

        private List<Transform> FilterBones(List<Transform> bones, AttachToBlendshapeData data, Animator animator)
        {
            var filtered = new List<Transform>();

            foreach (var bone in bones)
            {
                if (data.ignoreHumanoidBones && IsHumanoidBone(bone, animator))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(data.boneNameFilter))
                {
                    if (!bone.name.ToLower().Contains(data.boneNameFilter.ToLower()))
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
