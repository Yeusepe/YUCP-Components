using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Header overlay editor for View Position & Head Auto-Link components.
    /// </summary>
    [CustomEditor(typeof(AttachToViewPositionData))]
    public class AttachToViewPositionDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("View Position & Head Auto-Link"));
            
            var container = new IMGUIContainer(() => {
                serializedObject.Update();
                
                DrawSection("Settings", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("offset"), new GUIContent("Offset"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("eyeAlignment"), new GUIContent("Eye Alignment"));
                });
                
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Positions this object at the avatar's view position (eye level) and links it to the head bone.",
                    MessageType.Info);
                
                serializedObject.ApplyModifiedProperties();
            });
            
            root.Add(container);
            return root;
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
