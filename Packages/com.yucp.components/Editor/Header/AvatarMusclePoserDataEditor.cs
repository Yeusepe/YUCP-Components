using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using VF.Utils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Custom editor for Avatar Muscle Poser with intuitive rotation handles and hover highlighting.
    /// Hover over bones in Scene view to see rotation handles, drag to pose.
    /// </summary>
    [CustomEditor(typeof(AvatarMusclePoserData))]
    public class AvatarMusclePoserDataEditor : UnityEditor.Editor
    {
        private AvatarMusclePoserData data;
        private Animator animator;
        private HumanPoseHandler humanPoseHandler;
        private HumanPose humanPose;
        
        // Hover and selection state
        private HumanBodyBones? hoveredBone = null;
        private HumanBodyBones? selectedBone = null;
        private Dictionary<HumanBodyBones, Quaternion> originalBoneRotations = new Dictionary<HumanBodyBones, Quaternion>();
        private Dictionary<HumanBodyBones, Quaternion> currentBoneRotations = new Dictionary<HumanBodyBones, Quaternion>();
        
        // Toggle selection
        private string[] toggleComponentNames;
        private int selectedToggleIndex = 0;
        
        // Teal color
        private static readonly Color TEAL_COLOR = new Color(0.212f, 0.749f, 0.694f);
        
        // Interactive bones (major bones that affect muscles)
        private static readonly HumanBodyBones[] INTERACTIVE_BONES = new HumanBodyBones[]
        {
            HumanBodyBones.Head,
            HumanBodyBones.Neck,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.UpperChest,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes
        };
        
        private void OnEnable()
        {
            if (target is AvatarMusclePoserData)
            {
                data = (AvatarMusclePoserData)target;
                SceneView.duringSceneGui += OnSceneGUI;
                FindAnimator();
                RefreshToggleList();
            }
        }
        
        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            CleanupPreview();
        }
        
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Avatar Muscle Poser"));
            
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
            
            // Toggle Configuration
            DrawSection("Toggle Configuration", () => {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleObject"), new GUIContent("Toggle Object", "GameObject that contains the toggle component."));
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    RefreshToggleList();
                }
                
                // Toggle Selection Dropdown
                if (data.toggleObject != null && toggleComponentNames != null && toggleComponentNames.Length > 0)
                {
                    EditorGUI.BeginChangeCheck();
                    selectedToggleIndex = EditorGUILayout.Popup("Selected Toggle", selectedToggleIndex, toggleComponentNames);
                    if (EditorGUI.EndChangeCheck())
                    {
                        UpdateSelectedToggle();
                    }
                    
                    if (data.selectedToggle != null)
                    {
                        EditorGUILayout.HelpBox($"Using toggle: {data.selectedToggle.GetType().Name}", MessageType.Info);
                    }
                }
                else if (data.toggleObject != null)
                {
                    EditorGUILayout.HelpBox("No toggle components found on the toggle object. Add a VRCFury toggle component.", MessageType.Warning);
                }
            });
            
            // Pose Settings
            DrawSection("Pose Settings", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showRotationRings"), new GUIContent("Show Rotation Handles", "Display rotation handles when hovering over bones."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ringSize"), new GUIContent("Handle Size", "Size of rotation handles in meters."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("poseAnimationClip"), new GUIContent("Animation Clip", "Animation clip to record the pose to."));
            });
            
            // Instructions
            DrawSection("How to Use", () => {
                EditorGUILayout.HelpBox(
                    "1. Hover over a bone in Scene view to see rotation handles\n" +
                    "2. Click and drag a handle to rotate that axis\n" +
                    "3. Click 'Record Pose' to save the animation\n" +
                    "4. The animation will play when the toggle activates",
                    MessageType.Info);
            });
            
            // Selected Bone Info
            if (!string.IsNullOrEmpty(data.SelectedBoneName))
            {
                DrawSection("Selected Bone", () => {
                    EditorGUILayout.LabelField($"Bone: {data.SelectedBoneName}", EditorStyles.miniLabel);
                    
                    if (data.CurrentMuscleValues != null && data.CurrentMuscleValues.Count > 0)
                    {
                        EditorGUILayout.Space(3);
                        EditorGUILayout.LabelField("Affected Muscles:", EditorStyles.miniBoldLabel);
                        EditorGUI.indentLevel++;
                        foreach (var kvp in data.CurrentMuscleValues.Take(5))
                        {
                            EditorGUILayout.LabelField($"{kvp.Key}: {kvp.Value:F2}", EditorStyles.miniLabel);
                        }
                        if (data.CurrentMuscleValues.Count > 5)
                        {
                            EditorGUILayout.LabelField($"... and {data.CurrentMuscleValues.Count - 5} more", EditorStyles.miniLabel);
                        }
                        EditorGUI.indentLevel--;
                    }
                });
            }
            
            // Action Buttons
            DrawSection("Actions", () => {
                GUI.enabled = animator != null && animator.avatar != null && animator.avatar.isHuman;
                
                EditorGUILayout.BeginHorizontal();
                
                GUI.backgroundColor = TEAL_COLOR;
                if (GUILayout.Button("Record Pose", GUILayout.Height(30)))
                {
                    RecordPose();
                }
                GUI.backgroundColor = Color.white;
                
                if (GUILayout.Button("Clear Pose", GUILayout.Height(30)))
                {
                    ClearPose();
                }
                
                if (GUILayout.Button("Preview Pose", GUILayout.Height(30)))
                {
                    PreviewPose();
                }
                
                EditorGUILayout.EndHorizontal();
                
                GUI.enabled = true;
                
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.HelpBox("No humanoid avatar found. This component requires a humanoid avatar with an Animator.", MessageType.Warning);
                }
            });
            
            // Debug & Preview
            DrawSection("Debug & Preview", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showPreview"), new GUIContent("Show Preview", "Show preview visualization in Scene view."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"), new GUIContent("Debug Mode", "Show debug information during build."));
            });
            
            if (serializedObject != null && serializedObject.targetObject != null)
            {
                serializedObject.ApplyModifiedProperties();
            }
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
        
        private void OnSceneGUI(SceneView sceneView)
        {
            if (!IsComponentSelected()) return;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return;
            if (!data.showRotationRings) return;
            
            Event e = Event.current;
            
            // Detect hover
            hoveredBone = DetectHoveredBone(sceneView, e);
            
            // Draw bone gizmos and rotation handles
            DrawBoneGizmos(e);
            
            // Update scene view
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.Layout)
            {
                SceneView.RepaintAll();
            }
        }
        
        private HumanBodyBones? DetectHoveredBone(SceneView sceneView, Event e)
        {
            if (e.type != EventType.MouseMove && e.type != EventType.Layout) return hoveredBone;
            
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            HumanBodyBones? closestBone = null;
            float closestDistance = float.MaxValue;
            const float HOVER_THRESHOLD = 0.08f;
            
            foreach (var bone in INTERACTIVE_BONES)
            {
                Transform boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform == null) continue;
                
                Vector3 bonePos = boneTransform.position;
                
                // Calculate distance from mouse ray to bone position
                float distance = HandleUtility.DistancePointLine(bonePos, ray.origin, ray.origin + ray.direction * 100f);
                
                // Also check screen space distance
                Vector3 screenPos = sceneView.camera.WorldToScreenPoint(bonePos);
                float screenDistance = Vector2.Distance(e.mousePosition, new Vector2(screenPos.x, Screen.height - screenPos.y));
                
                float finalDistance = Mathf.Min(distance, screenDistance * 0.001f);
                
                if (finalDistance < HOVER_THRESHOLD && finalDistance < closestDistance)
                {
                    closestDistance = finalDistance;
                    closestBone = bone;
                }
            }
            
            return closestBone;
        }
        
        private void DrawBoneGizmos(Event e)
        {
            foreach (var bone in INTERACTIVE_BONES)
            {
                Transform boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform == null) continue;
                
                bool isHovered = hoveredBone == bone;
                bool isSelected = selectedBone == bone;
                
                // Get current local rotation (preserve original if not modified)
                if (!currentBoneRotations.ContainsKey(bone))
                {
                    currentBoneRotations[bone] = boneTransform.localRotation;
                }
                if (!originalBoneRotations.ContainsKey(bone))
                {
                    originalBoneRotations[bone] = boneTransform.localRotation;
                }
                
                Quaternion currentLocalRot = currentBoneRotations[bone];
                Quaternion currentWorldRot = boneTransform.parent != null 
                    ? boneTransform.parent.rotation * currentLocalRot 
                    : currentLocalRot;
                
                // Draw bone indicator - subtle and clean
                Color gizmoColor = isSelected ? TEAL_COLOR : (isHovered ? new Color(1f, 0.8f, 0.2f) : Color.white * 0.3f);
                float size = isSelected ? 0.012f : (isHovered ? 0.01f : 0.006f);
                
                Handles.color = gizmoColor;
                
                // Draw a small dot instead of sphere for cleaner look
                Handles.DrawSolidDisc(boneTransform.position, 
                    SceneView.currentDrawingSceneView.camera.transform.forward, 
                    size);
                
                // Draw rotation handle when hovered or selected
                if (isHovered || isSelected)
                {
                    // Use Unity's built-in rotation handle but with custom size
                    float handleSize = HandleUtility.GetHandleSize(boneTransform.position) * 0.3f;
                    Handles.color = Color.white;
                    
                    EditorGUI.BeginChangeCheck();
                    Quaternion newWorldRot = Handles.RotationHandle(currentWorldRot, boneTransform.position);
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Convert world rotation back to local rotation
                        Quaternion parentWorldRot = boneTransform.parent != null 
                            ? boneTransform.parent.rotation 
                            : Quaternion.identity;
                        Quaternion newLocalRot = Quaternion.Inverse(parentWorldRot) * newWorldRot;
                        
                        Undo.RecordObject(boneTransform, $"Rotate {bone}");
                        currentBoneRotations[bone] = newLocalRot;
                        boneTransform.localRotation = newLocalRot;
                        selectedBone = bone;
                        
                        // Update human pose to get muscle values
                        UpdateMuscleValuesFromBoneRotation(bone, newLocalRot);
                        
                        EditorUtility.SetDirty(data);
                        e.Use();
                    }
                    
                    // Draw subtle outline ring
                    Handles.color = gizmoColor * 0.5f;
                    Handles.DrawWireDisc(boneTransform.position, 
                        SceneView.currentDrawingSceneView.camera.transform.forward, 
                        size * 1.8f);
                }
            }
            
            // Clear selection on click outside
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (!hoveredBone.HasValue)
                {
                    selectedBone = null;
                    e.Use();
                }
            }
        }
        
        private void UpdateMuscleValuesFromBoneRotation(HumanBodyBones bone, Quaternion localRotation)
        {
            if (humanPoseHandler == null) return;
            
            // Get current human pose (this preserves root position and other bones)
            humanPoseHandler.GetHumanPose(ref humanPose);
            
            // Apply local rotation to bone (this won't affect root)
            Transform boneTransform = animator.GetBoneTransform(bone);
            if (boneTransform != null)
            {
                boneTransform.localRotation = localRotation;
            }
            
            // Update human pose to recalculate muscle values
            humanPoseHandler.SetHumanPose(ref humanPose);
            
            // Extract muscle values
            var muscleValues = new Dictionary<string, float>();
            string[] muscleNames = HumanTrait.MuscleName;
            
            for (int i = 0; i < muscleNames.Length && i < humanPose.muscles.Length; i++)
            {
                if (Mathf.Abs(humanPose.muscles[i]) > 0.001f)
                {
                    string animFormatName = ConvertToAnimFormat(muscleNames[i]);
                    muscleValues[animFormatName] = humanPose.muscles[i];
                }
            }
            
            data.SetMuscleValues(muscleValues);
            data.SetSelectedBone(bone.ToString());
        }
        
        private string ConvertToAnimFormat(string humanTraitName)
        {
            if (string.IsNullOrEmpty(humanTraitName)) return null;
            
            string result = humanTraitName;
            result = result.Replace("Left Thumb", "LeftHand.Thumb");
            result = result.Replace("Left Index", "LeftHand.Index");
            result = result.Replace("Left Middle", "LeftHand.Middle");
            result = result.Replace("Left Ring", "LeftHand.Ring");
            result = result.Replace("Left Little", "LeftHand.Little");
            
            result = result.Replace("Right Thumb", "RightHand.Thumb");
            result = result.Replace("Right Index", "RightHand.Index");
            result = result.Replace("Right Middle", "RightHand.Middle");
            result = result.Replace("Right Ring", "RightHand.Ring");
            result = result.Replace("Right Little", "RightHand.Little");
            
            result = result.Replace(" 1 ", ".1 ");
            result = result.Replace(" 2 ", ".2 ");
            result = result.Replace(" 3 ", ".3 ");
            result = result.Replace(" Spread", ".Spread");
            
            return result;
        }
        
        private void RecordPose()
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                EditorUtility.DisplayDialog("Error", "No humanoid avatar found. This component requires a humanoid avatar.", "OK");
                return;
            }
            
            // Create or get animation clip
            AnimationClip clip = data.poseAnimationClip;
            if (clip == null)
            {
                string path = EditorUtility.SaveFilePanelInProject("Save Pose Animation", "NewPose", "anim", "Path to save animation");
                if (string.IsNullOrEmpty(path)) return;
                
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
                data.poseAnimationClip = clip;
                EditorUtility.SetDirty(data);
            }
            
            // Get current muscle values
            if (humanPoseHandler == null)
            {
                humanPoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            }
            
            humanPoseHandler.GetHumanPose(ref humanPose);
            
            // Create animation clip with muscle values
            CreateMuscleAnimationClip(clip);
            
            // Use VRCFury recorder via reflection (RecorderUtils is internal)
            var vrcFuryObject = data.toggleObject ?? data.gameObject;
            CallVRCFuryRecorder(clip, vrcFuryObject);
        }
        
        private void CreateMuscleAnimationClip(AnimationClip clip)
        {
            // Clear existing curves
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
            }
            
            // Add muscle curves
            string[] muscleNames = HumanTrait.MuscleName;
            for (int i = 0; i < muscleNames.Length && i < humanPose.muscles.Length; i++)
            {
                if (Mathf.Abs(humanPose.muscles[i]) > 0.001f)
                {
                    string animFormatName = ConvertToAnimFormat(muscleNames[i]);
                    var curve = AnimationCurve.Constant(0f, 1f, humanPose.muscles[i]);
                    var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), animFormatName);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }
            
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
        }
        
        private void ClearPose()
        {
            // Reset all bone rotations to original local rotations
            foreach (var kvp in originalBoneRotations)
            {
                Transform boneTransform = animator.GetBoneTransform(kvp.Key);
                if (boneTransform != null)
                {
                    boneTransform.localRotation = kvp.Value;
                }
            }
            
            currentBoneRotations.Clear();
            originalBoneRotations.Clear();
            selectedBone = null;
            hoveredBone = null;
            data.SetSelectedBone("");
            data.SetMuscleValues(new Dictionary<string, float>());
            
            // Reset human pose
            if (humanPoseHandler != null)
            {
                humanPoseHandler.GetHumanPose(ref humanPose);
                // Reset all muscles to zero
                for (int i = 0; i < humanPose.muscles.Length; i++)
                {
                    humanPose.muscles[i] = 0f;
                }
                humanPoseHandler.SetHumanPose(ref humanPose);
            }
            
            SceneView.RepaintAll();
        }
        
        private void PreviewPose()
        {
            if (currentBoneRotations.Count == 0)
            {
                EditorUtility.DisplayDialog("No Pose", "No pose to preview. Rotate some bones first.", "OK");
                return;
            }
            
            data.showPreview = true;
            SceneView.RepaintAll();
        }
        
        private void CleanupPreview()
        {
            ClearPose();
            data.showPreview = false;
        }
        
        private void FindAnimator()
        {
            animator = data.GetComponentInParent<Animator>();
            if (animator == null)
            {
                animator = Object.FindObjectOfType<Animator>();
            }
            
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                humanPoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
                humanPose = new HumanPose();
            }
        }
        
        private bool IsComponentSelected()
        {
            return Selection.activeGameObject == data.gameObject;
        }
        
        private void RefreshToggleList()
        {
            if (data.toggleObject == null)
            {
                toggleComponentNames = new string[0];
                return;
            }
            
            var components = data.toggleObject.GetComponents<Component>();
            var toggleComponents = new List<Component>();
            var names = new List<string>();
            
            foreach (var comp in components)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                
                // Look for VRCFury toggle components
                if (typeName == "VRCFury" || typeName.Contains("Toggle"))
                {
                    toggleComponents.Add(comp);
                    names.Add($"{typeName} ({comp.GetInstanceID()})");
                }
            }
            
            toggleComponentNames = names.ToArray();
            
            // Auto-select first toggle if none selected
            if (data.selectedToggle == null && toggleComponents.Count > 0)
            {
                data.selectedToggle = toggleComponents[0];
                selectedToggleIndex = 0;
            }
            else if (data.selectedToggle != null)
            {
                // Find index of selected toggle
                selectedToggleIndex = toggleComponents.IndexOf(data.selectedToggle);
                if (selectedToggleIndex < 0) selectedToggleIndex = 0;
            }
        }
        
        private void UpdateSelectedToggle()
        {
            if (data.toggleObject == null || toggleComponentNames == null || selectedToggleIndex < 0 || selectedToggleIndex >= toggleComponentNames.Length)
            {
                data.selectedToggle = null;
                return;
            }
            
            var components = data.toggleObject.GetComponents<Component>();
            var toggleComponents = new List<Component>();
            
            foreach (var comp in components)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                if (typeName == "VRCFury" || typeName.Contains("Toggle"))
                {
                    toggleComponents.Add(comp);
                }
            }
            
            if (selectedToggleIndex < toggleComponents.Count)
            {
                data.selectedToggle = toggleComponents[selectedToggleIndex];
                EditorUtility.SetDirty(data);
            }
        }
        
        private void CallVRCFuryRecorder(AnimationClip clip, GameObject targetObject)
        {
            // Use reflection to call VRCFury's internal RecorderUtils.Record method
            try
            {
                // Try to find the type from loaded assemblies
                System.Type recorderUtilsType = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    recorderUtilsType = assembly.GetType("VF.Utils.RecorderUtils");
                    if (recorderUtilsType != null) break;
                }
                
                if (recorderUtilsType == null)
                {
                    EditorUtility.DisplayDialog("Error", "VRCFury RecorderUtils not found. Make sure VRCFury is installed.", "OK");
                    return;
                }
                
                var recordMethod = recorderUtilsType.GetMethod("Record", BindingFlags.Public | BindingFlags.Static);
                if (recordMethod == null)
                {
                    EditorUtility.DisplayDialog("Error", "VRCFury RecorderUtils.Record method not found.", "OK");
                    return;
                }
                
                // VFGameObject has implicit conversion from GameObject, so pass GameObject directly
                // The method signature is: Record(AnimationClip clip, VFGameObject baseObj, bool rewriteClip = true)
                recordMethod.Invoke(null, new object[] { clip, targetObject, true });
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AvatarMusclePoser] Failed to call VRCFury recorder: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to start VRCFury recorder: {ex.Message}", "OK");
            }
        }
    }
}
