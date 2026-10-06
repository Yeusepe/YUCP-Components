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
            var dataList = avatarRoot.GetComponentsInChildren<AttachToClosestBoneData>(true);
            if (dataList.Length == 0)
                return true;

            var animator = avatarRoot.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError("[AttachToClosestBoneProcessor] Animator missing on avatar.", avatarRoot);
                return true;
            }

            var progressWindow = YUCPProgressWindow.Create();
            try
            {
                progressWindow.Progress(0, "Processing closest bone attachments...");
                
                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];
                    Transform closestBone = ClosestBoneFinder.Find(animator, data.transform, data.maxDistance,
                        data.ignoreHumanoidBones, data.includeNameFilter, data.excludeNameFilter);

                    if (closestBone == null)
                    {
                        Debug.LogError($"[AttachToClosestBoneProcessor] No matching bones within range for '{data.name}'", data);
                        continue;
                    }

                    string bonePath = AnimationUtility.CalculateTransformPath(closestBone, animator.transform);
                    data.SetSelectedBonePath(bonePath);

                    var link = FuryComponents.CreateArmatureLink(data.gameObject);
                    if (link == null)
                    {
                        Debug.LogError($"[AttachToClosestBoneProcessor] Failed to create FuryArmatureLink on '{data.name}'", data);
                        continue;
                    }

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

                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed attachment {i + 1}/{dataList.Length}");
                }
            }
            finally
            {
                progressWindow.CloseWindow();
            }

            return true;
        }

    }
}

