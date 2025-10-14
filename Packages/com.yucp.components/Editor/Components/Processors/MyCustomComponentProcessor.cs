using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Example processor template for custom YUCP components.
    /// Runs during avatar build to execute component logic.
    /// </summary>
    public class MyCustomComponentProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => 0;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var comps = avatarRoot
                .GetComponentsInChildren<YUCP.Components.MyCustomComponent>(true);

            if (comps.Length > 0)
            {
                var progressWindow = YUCPProgressWindow.Create();
                progressWindow.Progress(0, "Processing custom components...");
                
                for (int i = 0; i < comps.Length; i++)
                {
                    var comp = comps[i];
                    bool flag = comp.exampleFlag;
                    
                    float progress = (float)(i + 1) / comps.Length;
                    progressWindow.Progress(progress, $"Processed custom component {i + 1}/{comps.Length}");
                }
                
                progressWindow.CloseWindow();
            }

            return true;
        }
    }
}