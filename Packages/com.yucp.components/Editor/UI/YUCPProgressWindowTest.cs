using UnityEditor;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor.UI
{
    /// <summary>
    /// Test utility for previewing the YUCP Progress Window.
    /// </summary>
    public class YUCPProgressWindowTest
    {
        [MenuItem("Tools/YUCP/Test Progress Window")]
        public static void TestProgressWindow()
        {
            var window = YUCPProgressWindow.Create();
            
            EditorApplication.delayCall += () =>
            {
                int totalVertices = 5000;
                for (int i = 0; i <= totalVertices; i += 25)
                {
                    float progress = (float)i / totalVertices;
                    string message = $"Smart detection: {i}/{totalVertices} vertices analyzed";
                    window.Progress(progress, message);
                }
                
                window.Progress(1.0f, "Detection complete!");
                
                EditorApplication.delayCall += () =>
                {
                    window.CloseWindow();
                };
            };
        }
    }
}
