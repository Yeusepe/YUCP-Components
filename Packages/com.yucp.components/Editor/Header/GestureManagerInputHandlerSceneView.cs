using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.Components.Editor.Header
{
    [InitializeOnLoad]
    public static class GestureManagerInputHandlerSceneView
    {
        private static Vector2 scrollPosition;
        private static bool showOverlay = true;
        private static GUIStyle overlayStyle;
        private static GUIStyle headerStyle;
        private static GUIStyle contentStyle;
        private static GUIStyle buttonStyle;
        private static GUIStyle statusStyle;
        private static GUIStyle parameterStyle;
        private static GUIStyle mappingStyle;

        static GestureManagerInputHandlerSceneView()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!showOverlay) return;

            var handlers = Object.FindObjectsOfType<YUCP.Components.GestureManagerInputHandler>();
            if (handlers.Length == 0) return;

            var activeHandler = handlers.FirstOrDefault(h => h.enabled && h.gameObject.activeInHierarchy);
            if (activeHandler == null) return;

            if (!activeHandler.debugMode) return;

            InitializeStyles();
            DrawModernOverlay(activeHandler, sceneView);
        }

        private static void InitializeStyles()
        {
            if (overlayStyle == null)
            {
                overlayStyle = new GUIStyle();
                overlayStyle.normal.background = CreateColorTexture(new Color(0.1f, 0.1f, 0.1f, 0.95f));
                overlayStyle.border = new RectOffset(8, 8, 8, 8);
                overlayStyle.padding = new RectOffset(16, 16, 16, 16);
                overlayStyle.margin = new RectOffset(10, 10, 10, 10);
            }

            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.boldLabel);
                headerStyle.fontSize = 16;
                headerStyle.normal.textColor = new Color(0.8f, 0.9f, 1f);
                headerStyle.alignment = TextAnchor.MiddleCenter;
            }

            if (contentStyle == null)
            {
                contentStyle = new GUIStyle(EditorStyles.label);
                contentStyle.fontSize = 12;
                contentStyle.normal.textColor = Color.white;
                contentStyle.wordWrap = true;
            }

            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.fontSize = 11;
                buttonStyle.normal.textColor = Color.white;
                buttonStyle.hover.textColor = new Color(0.8f, 0.9f, 1f);
                buttonStyle.normal.background = CreateColorTexture(new Color(0.2f, 0.4f, 0.8f, 0.8f));
                buttonStyle.hover.background = CreateColorTexture(new Color(0.3f, 0.5f, 0.9f, 0.9f));
                buttonStyle.padding = new RectOffset(8, 8, 4, 4);
            }

            if (statusStyle == null)
            {
                statusStyle = new GUIStyle(EditorStyles.label);
                statusStyle.fontSize = 11;
                statusStyle.normal.textColor = new Color(0.7f, 0.9f, 0.7f);
                statusStyle.fontStyle = FontStyle.Bold;
            }

            if (parameterStyle == null)
            {
                parameterStyle = new GUIStyle(EditorStyles.label);
                parameterStyle.fontSize = 11;
                parameterStyle.normal.textColor = new Color(1f, 0.8f, 0.4f);
                parameterStyle.fontStyle = FontStyle.Bold;
            }

            if (mappingStyle == null)
            {
                mappingStyle = new GUIStyle(EditorStyles.label);
                mappingStyle.fontSize = 10;
                mappingStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            }
        }

        private static Texture2D CreateColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void DrawModernOverlay(YUCP.Components.GestureManagerInputHandler handler, SceneView sceneView)
        {
            var position = new Rect(10, 10, 320, 400);
            
            // Draw main overlay
            GUI.Box(position, "", overlayStyle);
            
            GUILayout.BeginArea(position);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            // Header
            GUILayout.Space(8);
            GUILayout.Label("Gesture Manager Input Emulator", headerStyle);
            GUILayout.Space(4);
            
            // Status section
            GUILayout.Label("Status", statusStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("• Handler:", contentStyle);
            GUILayout.Label(handler.enabled ? "Active" : "Inactive", statusStyle);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("• Input Capture:", contentStyle);
            GUILayout.Label(handler.enableInputCapture ? "Enabled" : "Disabled", statusStyle);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("• Gesture Manager:", contentStyle);
            GUILayout.Label(handler.GestureManager != null ? "Connected" : "Not Found", statusStyle);
            GUILayout.EndHorizontal();
            
            GUILayout.Space(8);
            
            // Runtime mappings section
            GUILayout.Label("Active Mappings", statusStyle);
            var runtimeMappings = handler.runtimeMappings;
            if (runtimeMappings != null && runtimeMappings.Count > 0)
            {
                foreach (var mapping in runtimeMappings)
                {
                    GUILayout.BeginVertical("box");
                    GUILayout.Label($"{mapping.parameterName}", parameterStyle);
                    string inputDesc = GetInputDescription(mapping);
                    GUILayout.Label($"   Input: {mapping.inputType} → {inputDesc}", mappingStyle);
                    GUILayout.Label($"   Type: {mapping.parameterType}", mappingStyle);
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
            }
            else
            {
                GUILayout.Label("No active mappings", contentStyle);
            }
            
            GUILayout.Space(8);
            
            // Controls
            GUILayout.Label("Controls", statusStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", buttonStyle, GUILayout.Width(80)))
            {
                // Refresh handler state
            }
            if (GUILayout.Button("Stats", buttonStyle, GUILayout.Width(80)))
            {
                // Show detailed stats
            }
            if (GUILayout.Button("Close", buttonStyle, GUILayout.Width(80)))
            {
                showOverlay = false;
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(8);
            
            // Footer
            GUILayout.Label("💡 Press F1 to toggle overlay", contentStyle);
            GUILayout.Label("🔧 Debug mode active", contentStyle);
            
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            
            // Handle F1 key
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F1)
            {
                showOverlay = !showOverlay;
                Event.current.Use();
            }
        }

        private static string GetInputDescription(YUCP.Components.GestureManagerInputHandler.RuntimeInputMapping mapping)
        {
            switch (mapping.inputType)
            {
                case YUCP.Components.InputType.Keyboard:
                    return mapping.keyboardKey.ToString();
                case YUCP.Components.InputType.ControllerButton:
                    return mapping.controllerButton;
                case YUCP.Components.InputType.ControllerAxis:
                    return mapping.controllerAxis;
                case YUCP.Components.InputType.ControllerTrigger:
                    return mapping.controllerTrigger;
                case YUCP.Components.InputType.ControllerDpad:
                    return mapping.controllerDpad;
                default:
                    return "Unknown";
            }
        }
    }
}