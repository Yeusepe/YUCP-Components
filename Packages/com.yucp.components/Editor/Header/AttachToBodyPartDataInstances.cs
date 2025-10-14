using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;

namespace YUCP.Components.Resources
{    
    /// <summary>
    /// Header overlay editor for Symmetric Armature Auto-Link components.
    /// </summary>
    [CustomEditor(typeof(AttachToBodyPartData))]
    public class AttachToBodyPartDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Symmetric Armature Auto-Link"));
            
            var container = new IMGUIContainer(() => {
                serializedObject.Update();
                
                DrawSection("Target Settings", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("part"), new GUIContent("Body Part"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("side"), new GUIContent("Side"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("offset"), new GUIContent("Offset"));
                });
                
                DrawSection("Auto-Delete Components", () => {
                    EditorGUILayout.HelpBox("Optionally delete components based on which side is selected.", MessageType.Info);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("leftComponentToDelete"), new GUIContent("Delete if Left"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("rightComponentToDelete"), new GUIContent("Delete if Right"));
                });
                
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