using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(UVDiscardToggleData))]
    public class UVDiscardToggleDataEditor : UnityEditor.Editor
    {
        private bool showAdvancedToggle = false;
        private bool showDebug = false;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("UV Discard Toggle"));
            
            var container = new IMGUIContainer(() => {
                OnInspectorGUIContent();
            });
            
            root.Add(container);
            return root;
        }

        private void OnInspectorGUIContent()
        {
            serializedObject.Update();
            var data = (UVDiscardToggleData)target;

            // Show integration banner if AutoBodyHider is present
            var autoBodyHider = data.clothingMesh != null ? data.clothingMesh.GetComponent<AutoBodyHiderData>() : null;
            if (autoBodyHider != null)
            {
                EditorGUILayout.Space(5);
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 0.4f);
                EditorGUILayout.HelpBox(
                    "Auto Body Hider Integration Detected\n\n" +
                    "This UV Discard Toggle will work together with the AutoBodyHider component on the clothing mesh. " +
                    "Both will use the same UDIM tile for coordinated body hiding and clothing toggling.",
                    MessageType.Info);
                GUI.backgroundColor = originalColor;
                EditorGUILayout.Space(5);
            }

            // Target Meshes
            DrawSection("Target Meshes", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetBodyMesh"), new GUIContent("Body Mesh"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("clothingMesh"), new GUIContent("Clothing Mesh"));
            });

            // UDIM Settings
            DrawSection("UDIM Discard Settings", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("udimUVChannel"), new GUIContent("UV Channel"));
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("udimDiscardRow"), new GUIContent("Row"), GUILayout.MinWidth(100));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("udimDiscardColumn"), new GUIContent("Column"), GUILayout.MinWidth(100));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.HelpBox("Avoid row 0 (especially 0,0) as it overlaps with the main texture. Row 3, Column 3 is safest.", MessageType.None);
            });

            // Toggle Settings
            DrawSection("Toggle Settings", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("menuPath"), new GUIContent("Menu Path"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("globalParameter"), new GUIContent("Global Parameter (Optional)"));
                
                // Show parameter mode info
                var menuPathProp = serializedObject.FindProperty("menuPath");
                var globalParameterProp = serializedObject.FindProperty("globalParameter");
                bool hasMenuPath = !string.IsNullOrEmpty(menuPathProp.stringValue);
                bool hasGlobalParam = !string.IsNullOrEmpty(globalParameterProp.stringValue);
                
                if (!hasMenuPath && hasGlobalParam)
                {
                    EditorGUILayout.HelpBox(
                        $"Global Parameter Only: Controlled by '{globalParameterProp.stringValue}' (no menu item).",
                        MessageType.Info);
                }
                else if (hasMenuPath && hasGlobalParam)
                {
                    EditorGUILayout.HelpBox(
                        $"Synced Toggle: Menu controls '{globalParameterProp.stringValue}' (synced across players).",
                        MessageType.Info);
                }
                else if (hasMenuPath && !hasGlobalParam)
                {
                    EditorGUILayout.HelpBox(
                        "Local Toggle: VRCFury auto-generates local parameter (not synced).",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Menu path or global parameter is required!", MessageType.Warning);
                }
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("saved"), new GUIContent("Saved"), GUILayout.MinWidth(100));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultOn"), new GUIContent("Default ON"), GUILayout.MinWidth(100));
                EditorGUILayout.EndHorizontal();
            });

            // Advanced Toggle Options (Foldout)
            EditorGUILayout.Space(5);
            showAdvancedToggle = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvancedToggle, "Advanced Toggle Options");
            if (showAdvancedToggle)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("slider"), new GUIContent("Use Slider"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("holdButton"), new GUIContent("Hold Button"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("securityEnabled"), new GUIContent("Security Enabled"));
                
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableExclusiveTag"), new GUIContent("Exclusive Tags"));
                if (serializedObject.FindProperty("enableExclusiveTag").boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("exclusiveTag"), new GUIContent("Tag Name"));
                    EditorGUI.indentLevel--;
                }
                
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableIcon"), new GUIContent("Custom Icon"));
                if (serializedObject.FindProperty("enableIcon").boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"), new GUIContent("Icon Texture"));
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Debug Options (Foldout)
            EditorGUILayout.Space(5);
            showDebug = EditorGUILayout.BeginFoldoutHeaderGroup(showDebug, "Debug Options");
            if (showDebug)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugSaveAnimation"), new GUIContent("Save Animation to Assets"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Validation Errors
            EditorGUILayout.Space(10);
            if (data.targetBodyMesh == null)
                EditorGUILayout.HelpBox("Target Body Mesh is required", MessageType.Error);
            else if (data.clothingMesh == null)
                EditorGUILayout.HelpBox("Clothing Mesh is required", MessageType.Error);
            else if (data.targetBodyMesh.sharedMesh == null)
                EditorGUILayout.HelpBox("Target Body Mesh has no mesh data", MessageType.Error);
            else if (data.clothingMesh.sharedMesh == null)
                EditorGUILayout.HelpBox("Clothing Mesh has no mesh data", MessageType.Error);
            else if (string.IsNullOrEmpty(data.menuPath) && string.IsNullOrEmpty(data.globalParameter))
                EditorGUILayout.HelpBox("Either Menu Path or Global Parameter must be set", MessageType.Error);
            else if (data.targetBodyMesh != null && data.targetBodyMesh.sharedMaterials != null)
            {
                bool hasPoiyomi = false;
                foreach (var mat in data.targetBodyMesh.sharedMaterials)
                {
                    if (UDIMManipulator.IsPoiyomiWithUDIMSupport(mat))
                    {
                        hasPoiyomi = true;
                        break;
                    }
                }
                if (!hasPoiyomi)
                    EditorGUILayout.HelpBox("Body mesh needs a Poiyomi material with UDIM support", MessageType.Warning);
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

