using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes symmetric armature link components during avatar build.
    /// Detects closest side (left/right), deletes unused components, and creates VRCFury armature links.
    /// </summary>
    public class AttachToBodyPartProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var animator = avatarRoot.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError("[AttachToBodyPartProcessor] Animator missing on avatar.", avatarRoot);
                return true;
            }

            var dataList = avatarRoot.GetComponentsInChildren<AttachToBodyPartData>(true);
            
            if (dataList.Length > 0)
            {
                var progressWindow = YUCPProgressWindow.Create();
                progressWindow.Progress(0, "Processing symmetric armature links...");
                
                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];
                    var chosenSide = data.GetSelectedSide(animator);

                    Component toDelete = (chosenSide == AttachToBodyPartData.Side.Left)
                        ? data.leftComponentToDelete
                        : data.rightComponentToDelete;

                    if (toDelete != null)
                    {
                        if (toDelete is Transform tr)
                            Object.DestroyImmediate(tr.gameObject);
                        else
                            Object.DestroyImmediate(toDelete);
                    }

                    if (!data.TryResolveBone(animator, out HumanBodyBones bone))
                    {
                        Debug.LogError($"[AttachToBodyPartProcessor] Cannot resolve bone part={data.part}, side={data.side}", data);
                        continue;
                    }

                    var link = FuryComponents.CreateArmatureLink(data.gameObject);
                    if (link == null)
                    {
                        Debug.LogError($"[AttachToBodyPartProcessor] Failed to create FuryArmatureLink on '{data.name}'", data);
                        continue;
                    }

                    link.LinkTo(bone, data.offset);
                    Debug.Log($"[AttachToBodyPartProcessor] Linked '{data.name}' → {bone} offset='{data.offset}'", data);
                    
                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed symmetric link {i + 1}/{dataList.Length}");
                }
                
                progressWindow.CloseWindow();
            }

            return true;
        }
    }
}
