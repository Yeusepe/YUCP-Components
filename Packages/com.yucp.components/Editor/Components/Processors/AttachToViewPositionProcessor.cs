using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using com.vrcfury.api.Components;
using VRC.SDK3.Avatars.Components;
using YUCP.Components;
using VRC.SDKBase;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes view position and head link components during avatar build.
    /// Positions objects at avatar view position (optionally aligned to eye bones) and links to head bone.
    /// </summary>
    public class AttachToViewPositionProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            Debug.Log("[AttachToViewPositionProcessor] Preprocess: moving to view position and linking to head", avatarRoot);

            var dataList = avatarRoot.GetComponentsInChildren<AttachToViewPositionData>(true);
            
            if (dataList.Length > 0)
            {
                var progressWindow = YUCPProgressWindow.Create();
                progressWindow.Progress(0, "Processing view position links...");
                
                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];
                    var desc = data.GetComponentInParent<VRC_AvatarDescriptor>();
                    if (desc == null)
                    {
                        Debug.LogError($"No VRC_AvatarDescriptor found for '{data.name}'", data);
                        continue;
                    }

                    Vector3 localTarget = desc.ViewPosition;

                    if (data.eyeAlignment != EyeAlignment.None)
                    {
                        var animator = desc.GetComponent<Animator>();
                        if (animator != null)
                        {
                            var bone = data.eyeAlignment == EyeAlignment.LeftEye
                                ? HumanBodyBones.LeftEye
                                : HumanBodyBones.RightEye;
                            var eyeTransform = animator.GetBoneTransform(bone);
                            if (eyeTransform != null)
                            {
                                Vector3 eyeLocal = desc.transform.InverseTransformPoint(eyeTransform.position);
                                localTarget.z = eyeLocal.z;
                            }
                            else
                            {
                                Debug.LogWarning($"Eye bone {bone} not found on animator", data);
                            }
                        }
                    }

                    localTarget += data.offset;

                    Vector3 worldTarget = desc.transform.TransformPoint(localTarget);

                    data.transform.position = worldTarget;
                    Debug.Log($"Moved '{data.name}' to world position {worldTarget}", data);

                    var link = FuryComponents.CreateArmatureLink(data.gameObject);
                    if (link == null)
                    {
                        Debug.LogError($"Failed to create FuryArmatureLink on '{data.name}'", data);
                        continue;
                    }

                    link.LinkTo(HumanBodyBones.Head);
                    Debug.Log($"Linked '{data.name}' to Head bone with baked offset", data);
                    
                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed view position link {i + 1}/{dataList.Length}");
                }
                
                progressWindow.CloseWindow();
            }

            return true;
        }
    }
}