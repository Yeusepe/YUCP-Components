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

            DrawSection("Required Parameters", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("angle01ParameterName"),
                    new GUIContent("Angle01 Parameter", "Float (0-1) angle from your input source."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("magnitudeParameterName"),
                    new GUIContent("Magnitude Parameter", "Float (0-1) stick magnitude gate."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rotationIndexParameterName"),
                    new GUIContent("Index Parameter", "Int counter incremented/decremented on each sector crossing."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("directionParameterName"),
                    new GUIContent("Direction Parameter", "Int reporting last movement direction (1, -1, 0 idle)."));
                EditorGUILayout.HelpBox("Ensure these parameters already exist (usually as FX animator parameters) or are created by your controller tooling before build.", MessageType.Info);
            });

            DrawSection("Debugging", () => {
                var debugProp = serializedObject.FindProperty("createDebugPhaseParameter");
                EditorGUILayout.PropertyField(debugProp,
                    new GUIContent("Create DebugPhase", "Adds a DebugPhase Int parameter and updates it per sector."));
                if (debugProp.boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("debugPhaseParameterName"),
                        new GUIContent("DebugPhase Parameter", "Int value that reports the current sector index."));
                }
                EditorGUILayout.HelpBox("Enable DebugPhase to visualize which sector the rotation counter currently occupies.", MessageType.None);
            });

            DrawSection("Rotation Tracking", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("numberOfSectors"),
                    new GUIContent("Number of Sectors", "How many slices to divide 360° into (12 = 30° each)."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sectorHysteresis"),
                    new GUIContent("Sector Hysteresis", "Margin kept inside each sector to prevent rapid toggling on boundaries."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("magnitudeThreshold"),
                    new GUIContent("Magnitude Threshold", "Stick magnitude required to track rotation."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("layerName"),
                    new GUIContent("Layer Name", "Animator layer name used for the generated graph."));
                EditorGUILayout.HelpBox("The generator builds a sector-based tracking graph. It increments/decrements the Index parameter as you cross sector boundaries.", MessageType.Info);
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
                    EditorGUILayout.IntField("Generated Sectors", data.generatedSectorCount);
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

