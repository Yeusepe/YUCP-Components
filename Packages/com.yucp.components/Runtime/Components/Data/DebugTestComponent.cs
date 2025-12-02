using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Simple test component to verify debug overlay functionality
    /// </summary>
    [SupportBanner]
    public class DebugTestComponent : MonoBehaviour
    {
        private bool showOverlay = false;
        private Vector2 scrollPosition = Vector2.zero;

        private void Update()
        {
            // Test F1 key
            if (Input.GetKeyDown(KeyCode.F1))
            {
                showOverlay = !showOverlay;
                Debug.Log($"[DebugTestComponent] F1 pressed! Overlay: {showOverlay}");
            }

            // Test any key
            if (Input.anyKeyDown)
            {
                Debug.Log($"[DebugTestComponent] Key pressed: {Input.inputString}");
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            // Show a small indicator
            GUI.Box(new Rect(10, 10, 200, 30), "");
            GUI.Label(new Rect(15, 15, 190, 20), "Debug Test Active - Press F1");

            if (showOverlay)
            {
                // Show overlay
                float overlayWidth = 300f;
                float overlayHeight = 200f;
                float overlayX = Screen.width - overlayWidth - 20f;
                float overlayY = 20f;

                GUI.Box(new Rect(overlayX, overlayY, overlayWidth, overlayHeight), "");
                GUI.Label(new Rect(overlayX + 10f, overlayY + 10f, overlayWidth - 20f, 30f), "Debug Test Overlay");
                
                if (GUI.Button(new Rect(overlayX + 10f, overlayY + 40f, 100f, 20f), "Close"))
                {
                    showOverlay = false;
                }

                GUI.Label(new Rect(overlayX + 10f, overlayY + 70f, overlayWidth - 20f, 20f), "This is a test overlay");
                GUI.Label(new Rect(overlayX + 10f, overlayY + 90f, overlayWidth - 20f, 20f), "Press F1 to toggle");
            }
        }
    }
}

