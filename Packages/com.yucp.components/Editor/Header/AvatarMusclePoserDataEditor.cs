using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using VF.Utils;
using YUCP.UI.DesignSystem.Utilities;

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
        
        private string[] toggleComponentNames;
        private int selectedToggleIndex = 0;
        
        // Track previous values to reduce unnecessary UI updates
        private GameObject previousToggleObject = null;
        private string[] previousToggleComponentNames = null;
        private Component previousSelectedToggle = null;
        private Animator previousAnimator = null;
        
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
        
        private void UpdateToggleSelection(VisualElement container)
        {
            container.Clear();
            if (data.toggleObject != null && toggleComponentNames != null && toggleComponentNames.Length > 0)
            {
                var popup = new PopupField<string>("Selected Toggle", new List<string>(toggleComponentNames), selectedToggleIndex);
                popup.RegisterValueChangedCallback(evt =>
                {
                    selectedToggleIndex = popup.index;
                    UpdateSelectedToggle();
                });
                container.Add(popup);
            }
        }
        
        private void UpdateToggleHelp(VisualElement container)
        {
            container.Clear();
            if (data.toggleObject != null && toggleComponentNames != null && toggleComponentNames.Length > 0)
            {
                if (data.selectedToggle != null)
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Using toggle: {data.selectedToggle.GetType().Name}", YUCPUIToolkitHelper.MessageType.Info));
                }
            }
            else if (data.toggleObject != null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("No toggle components found on the toggle object. Add a VRCFury toggle component.", YUCPUIToolkitHelper.MessageType.Warning));
            }
        }
        
        private void UpdateAvatarWarning(VisualElement container)
        {
            container.Clear();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("No humanoid avatar found. This component requires a humanoid avatar with an Animator.", YUCPUIToolkitHelper.MessageType.Warning));
            }
        }
        
        private bool AreArraysEqual(string[] a, string[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
        
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Avatar Muscle Poser"));
            
            var toggleCard = YUCPUIToolkitHelper.CreateCard("Toggle Configuration", "Configure the toggle component");
            var toggleContent = YUCPUIToolkitHelper.GetCardContent(toggleCard);
            
            var toggleObjectField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("toggleObject"), "Toggle Object");
            toggleObjectField.RegisterValueChangeCallback(evt =>
            {
                serializedObject.ApplyModifiedProperties();
                RefreshToggleList();
            });
            toggleContent.Add(toggleObjectField);
            
            var toggleSelectContainer = new VisualElement();
            toggleSelectContainer.name = "toggle-select-container";
            toggleContent.Add(toggleSelectContainer);
            
            var toggleHelp = new VisualElement();
            toggleHelp.name = "toggle-help";
            toggleContent.Add(toggleHelp);
            root.Add(toggleCard);
            
            // Pose Settings Card
            var poseCard = YUCPUIToolkitHelper.CreateCard("Pose Settings", "Configure pose recording settings");
            var poseContent = YUCPUIToolkitHelper.GetCardContent(poseCard);
            poseContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("showRotationRings"), "Show Rotation Handles"));
            poseContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("ringSize"), "Handle Size"));
            poseContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("poseAnimationClip"), "Animation Clip"));
            root.Add(poseCard);
            
            // How to Use Card
            var howToCard = YUCPUIToolkitHelper.CreateCard("How to Use", "Instructions for using the pose editor");
            var howToContent = YUCPUIToolkitHelper.GetCardContent(howToCard);
            howToContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "1. Hover over a bone in Scene view to see rotation handles\n" +
                "2. Click and drag a handle to rotate that axis\n" +
                "3. Click 'Record Pose' to save the animation\n" +
                "4. The animation will play when the toggle activates",
                YUCPUIToolkitHelper.MessageType.Info));
            root.Add(howToCard);
            
            // Selected Bone Card (conditional)
            var selectedBoneCard = YUCPUIToolkitHelper.CreateCard("Selected Bone", "Information about the currently selected bone");
            selectedBoneCard.name = "selected-bone-card";
            var selectedBoneContent = YUCPUIToolkitHelper.GetCardContent(selectedBoneCard);
            selectedBoneContent.name = "selected-bone-content";
            root.Add(selectedBoneCard);
            
            // Actions Card
            var actionsCard = YUCPUIToolkitHelper.CreateCard("Actions", "Pose recording and preview actions");
            var actionsContent = YUCPUIToolkitHelper.GetCardContent(actionsCard);
            
            var buttonsContainer = new VisualElement();
            buttonsContainer.style.flexDirection = FlexDirection.Row;
            buttonsContainer.style.marginBottom = 10;
            
            var recordButton = YUCPUIToolkitHelper.CreateButton("Record Pose", () => RecordPose(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            recordButton.style.height = 30;
            recordButton.style.flexGrow = 1;
            recordButton.style.marginRight = 5;
            recordButton.name = "record-button";
            buttonsContainer.Add(recordButton);
            
            var clearButton = YUCPUIToolkitHelper.CreateButton("Clear Pose", () => ClearPose(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            clearButton.style.height = 30;
            clearButton.style.flexGrow = 1;
            clearButton.style.marginRight = 5;
            buttonsContainer.Add(clearButton);
            
            var previewButton = YUCPUIToolkitHelper.CreateButton("Preview Pose", () => PreviewPose(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            previewButton.style.height = 30;
            previewButton.style.flexGrow = 1;
            previewButton.name = "preview-button";
            buttonsContainer.Add(previewButton);
            
            actionsContent.Add(buttonsContainer);
            
            var avatarWarning = new VisualElement();
            avatarWarning.name = "avatar-warning";
            actionsContent.Add(avatarWarning);
            root.Add(actionsCard);
            
            // Debug & Preview Card
            var debugCard = YUCPUIToolkitHelper.CreateCard("Debug & Preview", "Debug and preview settings");
            var debugContent = YUCPUIToolkitHelper.GetCardContent(debugCard);
            debugContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("showPreview"), "Show Preview"));
            debugContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugMode"), "Debug Mode"));
            root.Add(debugCard);
            
            previousToggleObject = data.toggleObject;
            previousToggleComponentNames = toggleComponentNames;
            previousSelectedToggle = data.selectedToggle;
            previousAnimator = animator;
            
            UpdateToggleSelection(toggleSelectContainer);
            UpdateToggleHelp(toggleHelp);
            UpdateAvatarWarning(avatarWarning);
            
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                FindAnimator();
                
                if (data.toggleObject != previousToggleObject || 
                    !AreArraysEqual(toggleComponentNames, previousToggleComponentNames))
                {
                    UpdateToggleSelection(toggleSelectContainer);
                    previousToggleObject = data.toggleObject;
                    previousToggleComponentNames = toggleComponentNames;
                }
                
                if (data.toggleObject != previousToggleObject || 
                    !AreArraysEqual(toggleComponentNames, previousToggleComponentNames) ||
                    data.selectedToggle != previousSelectedToggle)
                {
                    UpdateToggleHelp(toggleHelp);
                    previousSelectedToggle = data.selectedToggle;
                }
                
                UpdateSelectedBoneCard(selectedBoneCard, selectedBoneContent);
                
                bool canRecord = animator != null && animator.avatar != null && animator.avatar.isHuman;
                recordButton.SetEnabled(canRecord);
                clearButton.SetEnabled(canRecord);
                previewButton.SetEnabled(canRecord);
                
                if (animator != previousAnimator)
                {
                    UpdateAvatarWarning(avatarWarning);
                    previousAnimator = animator;
                }
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateSelectedBoneCard(VisualElement card, VisualElement content)
        {
            content.Clear();
            
            if (string.IsNullOrEmpty(data.SelectedBoneName))
            {
                card.style.display = DisplayStyle.None;
                return;
            }
            
            card.style.display = DisplayStyle.Flex;
            
            var boneLabel = new Label($"Bone: {data.SelectedBoneName}");
            boneLabel.style.fontSize = 10;
            boneLabel.style.marginBottom = 3;
            content.Add(boneLabel);
            
            if (data.CurrentMuscleValues != null && data.CurrentMuscleValues.Count > 0)
            {
                YUCPUIToolkitHelper.AddSpacing(content, 3);
                
                var musclesLabel = new Label("Affected Muscles:");
                musclesLabel.style.fontSize = 10;
                musclesLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                musclesLabel.style.marginBottom = 2;
                content.Add(musclesLabel);
                
                var musclesContainer = new VisualElement();
                musclesContainer.style.paddingLeft = 15;
                
                foreach (var kvp in data.CurrentMuscleValues.Take(5))
                {
                    var muscleLabel = new Label($"{kvp.Key}: {kvp.Value:F2}");
                    muscleLabel.style.fontSize = 10;
                    muscleLabel.style.marginBottom = 1;
                    musclesContainer.Add(muscleLabel);
                }
                
                if (data.CurrentMuscleValues.Count > 5)
                {
                    var moreLabel = new Label($"... and {data.CurrentMuscleValues.Count - 5} more");
                    moreLabel.style.fontSize = 10;
                    musclesContainer.Add(moreLabel);
                }
                
                content.Add(musclesContainer);
            }
        }
        
        public override void OnInspectorGUI()
        {
            // Legacy support - not used anymore
        }
        
        private void OnSceneGUI(SceneView sceneView)
        {
            if (!IsComponentSelected()) return;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return;
            if (!data.showRotationRings) return;
            
            Event e = Event.current;
            
            hoveredBone = DetectHoveredBone(sceneView, e);
            
            DrawBoneGizmos(e);
            
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
                
                float distance = HandleUtility.DistancePointLine(bonePos, ray.origin, ray.origin + ray.direction * 100f);
                
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
                
                Color gizmoColor = isSelected ? TEAL_COLOR : (isHovered ? new Color(1f, 0.8f, 0.2f) : Color.white * 0.3f);
                float size = isSelected ? 0.012f : (isHovered ? 0.01f : 0.006f);
                
                Handles.color = gizmoColor;
                
                Handles.DrawSolidDisc(boneTransform.position, 
                    SceneView.currentDrawingSceneView.camera.transform.forward, 
                    size);
                
                if (isHovered || isSelected)
                {
                    float handleSize = HandleUtility.GetHandleSize(boneTransform.position) * 0.3f;
                    Handles.color = Color.white;
                    
                    EditorGUI.BeginChangeCheck();
                    Quaternion newWorldRot = Handles.RotationHandle(currentWorldRot, boneTransform.position);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Quaternion parentWorldRot = boneTransform.parent != null 
                            ? boneTransform.parent.rotation 
                            : Quaternion.identity;
                        Quaternion newLocalRot = Quaternion.Inverse(parentWorldRot) * newWorldRot;
                        
                        Undo.RecordObject(boneTransform, $"Rotate {bone}");
                        currentBoneRotations[bone] = newLocalRot;
                        boneTransform.localRotation = newLocalRot;
                        selectedBone = bone;
                        
                        UpdateMuscleValuesFromBoneRotation(bone, newLocalRot);
                        
                        EditorUtility.SetDirty(data);
                        e.Use();
                    }
                    
                    Handles.color = gizmoColor * 0.5f;
                    Handles.DrawWireDisc(boneTransform.position, 
                        SceneView.currentDrawingSceneView.camera.transform.forward, 
                        size * 1.8f);
                }
            }
            
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
            
            humanPoseHandler.GetHumanPose(ref humanPose);
            
            Transform boneTransform = animator.GetBoneTransform(bone);
            if (boneTransform != null)
            {
                boneTransform.localRotation = localRotation;
            }
            
            humanPoseHandler.SetHumanPose(ref humanPose);
            
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
            
            if (humanPoseHandler == null)
            {
                humanPoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            }
            
            humanPoseHandler.GetHumanPose(ref humanPose);
            
            CreateMuscleAnimationClip(clip);
            
            var vrcFuryObject = data.toggleObject ?? data.gameObject;
            CallVRCFuryRecorder(clip, vrcFuryObject);
        }
        
        private void CreateMuscleAnimationClip(AnimationClip clip)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
            }
            
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
            
            if (humanPoseHandler != null)
            {
                humanPoseHandler.GetHumanPose(ref humanPose);
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
            try
            {
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
