using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Header overlay editor for Closest Bone Auto-Link components.
    /// </summary>
    [CustomEditor(typeof(AttachToClosestBoneData))]
    public class AttachToClosestBoneDataEditor : UnityEditor.Editor
    {
        private bool showBoneFiltering = false;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Closest Bone Auto-Link"));
            
            var container = new IMGUIContainer(() => {
                serializedObject.Update();
                var data = (AttachToClosestBoneData)target;
                
                DrawSection("Settings", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("offset"), new GUIContent("Offset"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("maxDistance"), new GUIContent("Max Distance"));
                });
                
                // Bone Filtering (Foldout)
                EditorGUILayout.Space(5);
                showBoneFiltering = EditorGUILayout.BeginFoldoutHeaderGroup(showBoneFiltering, "Bone Filtering");
                if (showBoneFiltering)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("includeNameFilter"), new GUIContent("Include Name Filter"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("excludeNameFilter"), new GUIContent("Exclude Name Filter"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("ignoreHumanoidBones"), new GUIContent("Ignore Humanoid Bones"));
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
                
                // Debug info
                if (!string.IsNullOrEmpty(data.SelectedBonePath))
                {
                    EditorGUILayout.Space(10);
                    EditorGUILayout.HelpBox($"Selected Bone: {data.SelectedBonePath}", MessageType.Info);
                }
                
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

