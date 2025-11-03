using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor;

namespace YUCP.Components.Editor.UI
{
    /// <summary>
    /// Custom editor for Rotation Counter component with configuration UI.
    /// </summary>
    [CustomEditor(typeof(RotationCounterData))]
    public class RotationCounterDataEditor : UnityEditor.Editor
    {
        private RotationCounterData data;

        private void OnEnable()
        {
            if (target is RotationCounterData)
            {
                data = (RotationCounterData)target;
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Rotation Counter"));
            
            var container = new IMGUIContainer(() => {
                OnInspectorGUIContent();
            });
            
            root.Add(container);
            return root;
        }

        public override void OnInspectorGUI()
        {
            OnInspectorGUIContent();
        }

        private void OnInspectorGUIContent()
        {
            serializedObject.Update();
            data = (RotationCounterData)target;

            // Beta warning
            BetaWarningHelper.DrawBetaWarningIMGUI(typeof(RotationCounterData));

            EditorGUILayout.Space(5);

            // Angle Input Section
            DrawSection("Angle Input", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("angleParameterName"), 
                    new GUIContent("Angle Parameter Name", "Name of the angle parameter (Float, 0-1 representing 0-360 degrees)"));
                
                EditorGUILayout.HelpBox("This parameter must be set by Gesture Manager Input Emulator or another system that provides angle input (0-1 range).", 
                    MessageType.Info);
            });

            // Rotation Output Section
            DrawSection("Rotation Output", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rotationIndexParameterName"), 
                    new GUIContent("Rotation Index Parameter Name", "Name of the Int parameter that will be incremented on each full rotation"));
                
                EditorGUILayout.HelpBox("This parameter will be incremented (+1) on forward rotations and decremented (-1) on reverse rotations. " +
                    "Use this parameter to drive animations or other behaviors based on cumulative rotation count.", 
                    MessageType.Info);
            });

            // Rotation Detection Section
            DrawSection("Rotation Detection", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("numberOfZones"), 
                    new GUIContent("Number of Zones", "Number of zones to divide the angle range (8 = 45° each, 16 = 22.5° each, etc.)"));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("nearZeroThreshold"), 
                    new GUIContent("Near Zero Threshold", "Angle below this value is considered near 0° for wraparound detection"));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("nearMaxThreshold"), 
                    new GUIContent("Near Max Threshold", "Angle above this value is considered near 360° for wraparound detection"));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("clockwiseIsPositive"), 
                    new GUIContent("Clockwise Increases", "If enabled, clockwise rotation increments RotationIndex; otherwise decrements."));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("hysteresisEpsilon"), 
                    new GUIContent("Hysteresis Epsilon", "Small buffer to stabilize arming/trigger thresholds and avoid flicker."));
                
                EditorGUILayout.HelpBox("More zones = more accurate tracking but more states in the generated controller. " +
                    "Recommended: 8 zones for 45° precision, 16 zones for 22.5° precision.", 
                    MessageType.Info);
            });
            
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("The rotation counter controller will be generated and automatically integrated via VRCFury FullController at build time. " +
                "No manual setup required - VRCFury handles all integration.", 
                MessageType.Info);

            // Build Statistics
            if (data.controllerGenerated)
            {
                EditorGUILayout.Space(5);
                DrawSection("Build Statistics", () => {
                    GUI.enabled = false;
                    EditorGUILayout.IntField("Generated Zones", data.generatedZonesCount);
                    EditorGUILayout.Toggle("Controller Generated", data.controllerGenerated);
                    GUI.enabled = true;
                });
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSection(string title, System.Action content)
        {
            EditorGUILayout.Space(5);
            
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.1f);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;
            
            if (!string.IsNullOrEmpty(title))
            {
                var style = new GUIStyle(EditorStyles.boldLabel);
                style.alignment = TextAnchor.MiddleLeft;
                EditorGUILayout.LabelField(title, style);
                EditorGUILayout.Space(3);
            }
            
            content?.Invoke();
            
            EditorGUILayout.EndVertical();
        }
    }
}

