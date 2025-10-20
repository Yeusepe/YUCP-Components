using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using VF.Utils;
using UnityEditor.Animations;
using UnityEngine.SceneManagement;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Custom editor for Auto Grip Generator with preview visualization and header overlay.
    /// Shows grip analysis, contact points, and muscle values in Scene view.
    /// </summary>
    [CustomEditor(typeof(AutoGripData))]
    public class AutoGripDataEditor : UnityEditor.Editor
    {
        private AutoGripData data;
        private bool isGeneratingPreview = false;
        private static GripGenerator.GripResult previewGripLeft;
        private static GripGenerator.GripResult previewGripRight;
        private GripStyleDetector.ObjectAnalysis objectAnalysis;
        
        private static System.Action previewRestoreAction = null;

        [InitializeOnLoadMethod]
        private static void InitPreviewSystem()
        {
            // Monitor editor updates - cleanup only when clone is destroyed externally
            EditorApplication.update += () => {
                if (previewRestoreAction != null && previewClone == null)
                {
                    // Clone was destroyed externally, cleanup
                    var action = previewRestoreAction;
                    previewRestoreAction = null;
                    action();
                }
            };
        }

        private void OnEnable()
        {
            if (target is AutoGripData)
            {
                data = (AutoGripData)target;
                SceneView.duringSceneGui += OnSceneGUI;
            }
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Auto Grip Generator"));
            
            // Add beta warning if attribute is present
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(AutoGripData));
            if (betaWarning != null)
            {
                root.Add(betaWarning);
            }
            
            var container = new IMGUIContainer(() => {
                OnInspectorGUIContent();
            });
            
            root.Add(container);
            return root;
        }

        public override void OnInspectorGUI()
        {
            // Add beta warning for fallback IMGUI path
            BetaWarningHelper.DrawBetaWarningIMGUI(typeof(AutoGripData));
            
            OnInspectorGUIContent();
        }

        private void OnInspectorGUIContent()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(10);

            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 14;
            headerStyle.normal.textColor = Color.cyan;
            EditorGUILayout.LabelField("GRIP ANALYSIS", headerStyle);

            if (data.grippedObject != null)
            {
                if (objectAnalysis == null)
                {
                    objectAnalysis = GripStyleDetector.AnalyzeObject(data.grippedObject);
                }

                EditorGUILayout.HelpBox(
                    $"Object Analysis:\n" +
                    $"Size: {objectAnalysis.size.x:F3} x {objectAnalysis.size.y:F3} x {objectAnalysis.size.z:F3}m\n" +
                    $"Max dimension: {objectAnalysis.maxDimension:F3}m\n" +
                    $"Aspect ratio: {objectAnalysis.aspectRatio:F2}\n" +
                    $"Has handle: {objectAnalysis.hasHandle}\n" +
                    $"Recommended grip: {objectAnalysis.recommendedStyle}\n\n" +
                    $"{GripStyleDetector.GetGripStyleDescription(objectAnalysis.recommendedStyle)}",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.HelpBox("Set 'Gripped Object' to see analysis", MessageType.None);
            }

            EditorGUILayout.Space(10);

            GUI.enabled = !isGeneratingPreview && ValidateData();
            
            string buttonText;
            Color buttonColor;
            if (isGeneratingPreview)
            {
                buttonText = "Generating...";
                buttonColor = Color.gray;
            }
            else if (data.showPreview && (previewGripLeft != null || previewGripRight != null))
            {
                buttonText = "Hide Preview";
                buttonColor = new Color(1f, 0.5f, 0.5f);
            }
            else
            {
                buttonText = "Show Preview";
                buttonColor = Color.green;
            }
            
            GUI.backgroundColor = buttonColor;
            if (GUILayout.Button(buttonText, GUILayout.Height(40)))
            {
                if (data.showPreview)
                {
                    ClearPreview();
                }
                else
                {
                    GeneratePreview();
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            if (previewGripLeft != null || previewGripRight != null)
            {
                EditorGUILayout.Space(10);
                ShowPreviewStats();
            }

            if (!ValidateData())
            {
                EditorGUILayout.HelpBox(GetValidationError(), MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(data.GeneratedGripInfo))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Last Build", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(data.GeneratedGripInfo, EditorStyles.wordWrappedLabel);
            }

            if (serializedObject != null && serializedObject.targetObject != null)
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private bool ValidateData()
        {
            if (data.grippedObject == null) return false;
            return true;
        }

        private string GetValidationError()
        {
            if (data.grippedObject == null) return "Gripped Object is required";
            return "";
        }

        private void GeneratePreview()
        {
            Debug.Log("[AutoGrip Preview] Starting preview generation...");
            isGeneratingPreview = true;
            
            try
            {
                var animator = FindAnimator();
                if (animator == null)
                {
                    Debug.LogError("[AutoGrip Preview] No Animator found in scene");
                    return;
                }
                
                Debug.Log($"[AutoGrip Preview] Found animator on '{animator.gameObject.name}'");

                objectAnalysis = GripStyleDetector.AnalyzeObject(data.grippedObject);
                Debug.Log($"[AutoGrip Preview] Object analysis complete - Style: {objectAnalysis.recommendedStyle}, Size: {objectAnalysis.size}");

                switch (data.targetHand)
                {
                    case HandTarget.Left:
                        Debug.Log("[AutoGrip Preview] Generating left hand grip...");
                        previewGripLeft = GripGenerator.GenerateGrip(animator, data.grippedObject, data, true);
                        Debug.Log($"[AutoGrip Preview] Left grip result: {(previewGripLeft != null ? "Success" : "NULL")}");
                        if (previewGripLeft != null)
                        {
                            Debug.Log($"[AutoGrip Preview] Left contacts: {previewGripLeft.contactPoints.Count}, muscles: {previewGripLeft.muscleValues.Count}");
                        }
                        break;

                    case HandTarget.Right:
                        Debug.Log("[AutoGrip Preview] Generating right hand grip...");
                        previewGripRight = GripGenerator.GenerateGrip(animator, data.grippedObject, data, false);
                        Debug.Log($"[AutoGrip Preview] Right grip result: {(previewGripRight != null ? "Success" : "NULL")}");
                        if (previewGripRight != null)
                        {
                            Debug.Log($"[AutoGrip Preview] Right contacts: {previewGripRight.contactPoints.Count}, muscles: {previewGripRight.muscleValues.Count}");
                        }
                        break;

                    case HandTarget.Both:
                        Debug.Log("[AutoGrip Preview] Generating both hands grip...");
                        previewGripLeft = GripGenerator.GenerateGrip(animator, data.grippedObject, data, true);
                        previewGripRight = GripGenerator.GenerateGrip(animator, data.grippedObject, data, false);
                        break;

                    case HandTarget.Closest:
                        bool isLeft = DetermineClosestHand(animator);
                        Debug.Log($"[AutoGrip Preview] Closest hand is: {(isLeft ? "Left" : "Right")}");
                        if (isLeft)
                        {
                            previewGripLeft = GripGenerator.GenerateGrip(animator, data.grippedObject, data, true);
                            previewGripRight = null; // Clear the other hand
                        }
                        else
                        {
                            previewGripRight = GripGenerator.GenerateGrip(animator, data.grippedObject, data, false);
                            previewGripLeft = null; // Clear the other hand
                        }
                        break;
            }

            data.showPreview = true;
            
            PreviewGripPose();
            
            SceneView.RepaintAll();
            Repaint();

                Debug.Log("[AutoGrip Preview] Preview generated successfully - Check Scene view for visualization");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AutoGrip Preview] Failed: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                isGeneratingPreview = false;
            }
        }

        private void PreviewGripPose()
        {
            var animator = FindAnimator();
            if (animator == null)
            {
                Debug.LogError("[AutoGrip Preview] No Animator found on avatar");
                return;
            }

            // Create a temporary AnimationClip with the grip pose
            var gripClip = CreateGripAnimationClip();
            if (gripClip == null)
            {
                Debug.LogWarning("[AutoGrip Preview] Failed to create grip animation clip");
                return;
            }

            // Start Unity's AnimationMode for proper preview
            StartAnimationPreview(animator.gameObject, gripClip);
        }


        private void ClearPreview()
        {
            // Stop AnimationMode to restore avatar to original pose
            StopAnimationPreview();
            
            previewGripLeft = null;
            previewGripRight = null;
            data.showPreview = false;
            
            SceneView.RepaintAll();
            Repaint();
            
            Debug.Log("[AutoGrip Preview] Preview cleared");
        }

        private void ShowPreviewStats()
        {
            if (previewGripLeft != null)
            {
                EditorGUILayout.LabelField("Left Hand Grip", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Contact points: {previewGripLeft.contactPoints.Count}");
                EditorGUILayout.LabelField($"Muscle curves: {previewGripLeft.muscleValues.Count}");
                EditorGUILayout.Space(5);
            }

            if (previewGripRight != null)
            {
                EditorGUILayout.LabelField("Right Hand Grip", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Contact points: {previewGripRight.contactPoints.Count}");
                EditorGUILayout.LabelField($"Muscle curves: {previewGripRight.muscleValues.Count}");
            }
        }

        private Animator FindAnimator()
        {
            var animator = data.GetComponentInParent<Animator>();
            if (animator != null) return animator;

            animator = GameObject.FindObjectOfType<Animator>();
            return animator;
        }

        private bool DetermineClosestHand(Animator animator)
        {
            var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            if (leftHand == null) return false;
            if (rightHand == null) return true;

            float leftDist = Vector3.Distance(data.transform.position, leftHand.position);
            float rightDist = Vector3.Distance(data.transform.position, rightHand.position);

            return leftDist <= rightDist;
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void DrawGizmos(AutoGripData data, GizmoType gizmoType)
        {
            if (!data.showPreview) return;

            if (previewGripLeft != null)
            {
                DrawGripGizmos(previewGripLeft, Color.cyan);
            }

            if (previewGripRight != null)
            {
                DrawGripGizmos(previewGripRight, Color.magenta);
            }

            if (data.grippedObject != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(data.grippedObject.position, 0.01f);
            }

            DrawSceneLegend();
        }

        private static void DrawGripGizmos(GripGenerator.GripResult grip, Color color)
        {
            Gizmos.color = color;
            Handles.color = color;

            foreach (var contact in grip.contactPoints)
            {
                Gizmos.DrawSphere(contact.position, 0.02f);
                Gizmos.DrawWireSphere(contact.position, 0.03f);
                
                Handles.DrawLine(contact.position, contact.position + contact.normal * 0.05f);
            }
        }

        private static void DrawSceneLegend()
        {
            Handles.BeginGUI();

            GUI.Box(new Rect(10, 10, 250, 100), "");
            GUI.Label(new Rect(15, 15, 240, 20), "Auto Grip Preview", EditorStyles.boldLabel);

            int totalContacts = 0;
            if (previewGripLeft != null) totalContacts += previewGripLeft.contactPoints.Count;
            if (previewGripRight != null) totalContacts += previewGripRight.contactPoints.Count;

            GUI.color = Color.cyan;
            GUI.Box(new Rect(15, 40, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 38, 200, 20), "= Left hand contacts");

            GUI.color = Color.magenta;
            GUI.Box(new Rect(15, 60, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 58, 200, 20), "= Right hand contacts");

            GUI.Label(new Rect(15, 80, 230, 20), $"Total contacts: {totalContacts}");

            Handles.EndGUI();
        }

        private static GameObject previewClone;
        private static GameObject originalAvatar;

        private AnimationClip CreateGripAnimationClip()
        {
            var gripData = previewGripLeft ?? previewGripRight;
            if (gripData == null) return null;

            var clip = new AnimationClip();
            clip.name = "AutoGrip_Preview";
            
            // DON'T add root transform curves - they cause the avatar to move to origin
            // Instead, we'll rely on applyRootMotion = false and keeping only muscle curves

            var animator = FindAnimator();
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                // Get the current pose from the animator
                var humanPose = new HumanPose();
                var humanPoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
                humanPoseHandler.GetHumanPose(ref humanPose);
                
                string[] muscleNames = HumanTrait.MuscleName;
                
                // First, add ALL muscles from the current pose to prevent them from going to default
                // This ensures non-animated muscles maintain their current position
                for (int i = 0; i < muscleNames.Length; i++)
                {
                    string muscleName = muscleNames[i];
                    float currentValue = humanPose.muscles[i];
                    
                    // Convert to .anim format
                    string animFormatName = ConvertToAnimFormat(muscleName);
                    
                    // Create a constant curve at the current value (no animation)
                    var curve = AnimationCurve.Constant(0f, 1f, currentValue);
                    var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), animFormatName);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
                
                Debug.Log($"[AutoGrip Clip] Added {muscleNames.Length} muscles at current pose to prevent default position");
                
                // Now override the grip muscles with transition curves
                foreach (var muscleEntry in gripData.muscleValues)
                {
                    // Find the current value of this muscle
                    // Convert from .anim format to HumanTrait format to find the index
                    string humanTraitName = ConvertFromAnimFormat(muscleEntry.Key);
                    float currentValue = 0f;
                    
                    for (int i = 0; i < muscleNames.Length; i++)
                    {
                        if (muscleNames[i] == humanTraitName)
                        {
                            currentValue = humanPose.muscles[i];
                            break;
                        }
                    }
                    
                    // Create a curve that transitions from current value to the grip value over 1 second
                    var curve = AnimationCurve.Linear(0f, currentValue, 1f, muscleEntry.Value);
                    var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), muscleEntry.Key);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                    Debug.Log($"[AutoGrip Clip] Adding muscle: {muscleEntry.Key} = {currentValue} -> {muscleEntry.Value}");
                }
                
                Debug.Log($"[AutoGrip Clip] Created clip with {gripData.muscleValues.Count} grip muscles (transitioning from current pose to grip)");
            }
            else
            {
                Debug.LogWarning("[AutoGrip Clip] No valid animator found, using 0 as default values");
                // Fallback: assume 0 as default
                foreach (var muscleEntry in gripData.muscleValues)
                {
                    var curve = AnimationCurve.Linear(0f, 0f, 1f, muscleEntry.Value);
                    var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), muscleEntry.Key);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }

            return clip;
        }

        private void StartAnimationPreview(GameObject avatarObject, AnimationClip clip)
        {
            Debug.Log("[AutoGrip Preview] Starting preview system...");
            StopAnimationPreview(); // Clean up first
            
            // Use VRCFury's EXACT approach with reflection
            var animWindowType = System.Type.GetType("UnityEditor.AnimationWindow,UnityEditor");
            Debug.Log($"[AutoGrip Preview] AnimationWindow type: {animWindowType}");
            
            var animWindow = EditorWindow.GetWindow(animWindowType);
            
            if (animWindow == null)
            {
                Debug.LogError("[AutoGrip Preview] Failed to open Animation Window");
                return;
            }

            Debug.Log("[AutoGrip Preview] Animation Window opened");
            animWindow.Focus();
            
            // Get AnimationWindowState using reflection
            var stateProperty = animWindowType.GetProperty("state", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var animState = stateProperty?.GetValue(animWindow);
            
            if (animState == null)
            {
                Debug.LogError("[AutoGrip Preview] Failed to get Animation Window state");
                return;
            }

            Debug.Log("[AutoGrip Preview] Got Animation Window state");

            // Hide original, create clone (VRCFury approach)
            originalAvatar = avatarObject;
            var originalAnimator = FindAnimator();
            
            Debug.Log($"[AutoGrip Preview] Hiding original avatar: {originalAvatar.name}");
            originalAvatar.SetActive(false);

            Debug.Log("[AutoGrip Preview] Creating clone...");
            previewClone = Instantiate(avatarObject);
            previewClone.name = avatarObject.name + " (AutoGrip Preview)";
            
            // Preserve the exact position and rotation
            previewClone.transform.position = avatarObject.transform.position;
            previewClone.transform.rotation = avatarObject.transform.rotation;
            previewClone.transform.localScale = avatarObject.transform.localScale;
            
            previewClone.SetActive(true);
            Debug.Log($"[AutoGrip Preview] Clone created: {previewClone.name} at position {previewClone.transform.position}");

            // Follow VRCFury's RecorderUtils approach: destroy ALL existing Animator/Animation components
            Debug.Log("[AutoGrip Preview] Destroying all existing Animator components on clone...");
            foreach (var an in previewClone.GetComponentsInChildren<Animator>())
            {
                DestroyImmediate(an);
            }
            
            Debug.Log("[AutoGrip Preview] Destroying all existing Animation components on clone...");
            foreach (var a in previewClone.GetComponentsInChildren<Animation>())
            {
                DestroyImmediate(a);
            }
            
            // Store the clone's position BEFORE adding Animator (Unity might reset it)
            var savedPosition = previewClone.transform.position;
            var savedRotation = previewClone.transform.rotation;
            
            // Add completely fresh Animator ONLY to clone root - no baggage, no constraints, no IK
            Debug.Log("[AutoGrip Preview] Adding fresh Animator to clone root...");
            var animator = previewClone.AddComponent<Animator>();
            animator.avatar = originalAnimator.avatar;
            animator.applyRootMotion = false;
            
            // Restore position after adding Animator (in case Unity reset it)
            previewClone.transform.position = savedPosition;
            previewClone.transform.rotation = savedRotation;
            
            Debug.Log($"[AutoGrip Preview] Fresh Animator created - Avatar: {animator.avatar?.name}, IsHuman: {animator.avatar?.isHuman}, ApplyRootMotion: {animator.applyRootMotion}");
            Debug.Log($"[AutoGrip Preview] Clone position after Animator: {previewClone.transform.position}");
            
            // Create a simple controller with the grip animation
            var controller = new AnimatorController();
            controller.AddLayer("AutoGrip Preview");
            var layer = controller.layers[0];
            var state = layer.stateMachine.AddState("Grip Pose");
            state.motion = clip;
            
            // Set the controller on the clone's Animator
            animator.runtimeAnimatorController = controller;
            
            // Restore position after setting controller (might reset again)
            previewClone.transform.position = savedPosition;
            previewClone.transform.rotation = savedRotation;
            
            // Force the Animator to play the animation immediately
            animator.Play("Grip Pose", 0, 0f);
            animator.Update(0f);
            
            // Restore position after playing animation (might reset yet again)
            previewClone.transform.position = savedPosition;
            previewClone.transform.rotation = savedRotation;
            
            Debug.Log($"[AutoGrip Preview] Position locked at: {previewClone.transform.position}");
            
            Debug.Log($"[AutoGrip Preview] Controller set with {AnimationUtility.GetCurveBindings(clip).Length} curves");
            Debug.Log($"[AutoGrip Preview] Animator state: {animator.GetCurrentAnimatorStateInfo(0).shortNameHash}");
            
            // Debug: Check if the muscle values are being applied
            if (animator.avatar != null && animator.avatar.isHuman)
            {
                var humanPose = new HumanPose();
                var humanPoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
                humanPoseHandler.GetHumanPose(ref humanPose);
                
                Debug.Log($"[AutoGrip Preview] Clone muscle values after animation:");
                for (int i = 0; i < humanPose.muscles.Length; i++)
                {
                    if (humanPose.muscles[i] != 0f)
                    {
                        string muscleName = HumanTrait.MuscleName[i];
                        Debug.Log($"[AutoGrip Preview] {muscleName}: {humanPose.muscles[i]}");
                    }
                }
            }

            // Use reflection to FORCE Animation Window to target the clone and clip
            Debug.Log("[AutoGrip Preview] Setting Animation Window target via reflection...");
            var animStateType = animState.GetType();
            var selectionField = animStateType.GetProperty("selection");
            var selection = selectionField?.GetValue(animState);
            
            if (selection != null)
            {
                var gameObjectField = selection.GetType().GetProperty("gameObject");
                gameObjectField?.SetValue(selection, previewClone);
                Debug.Log("[AutoGrip Preview] Animation Window target set to clone");
            }
            
            var clipField = animStateType.GetProperty("activeAnimationClip");
            clipField?.SetValue(animState, clip);
            Debug.Log($"[AutoGrip Preview] Animation Window clip set to: {clip.name}");

            // Setup restore action (like VRCFury) to cleanup when user is done
            previewRestoreAction = () => {
                if (!data.showPreview) return; // User already clicked hide
                StopAnimationPreview();
            };

            Debug.Log("[AutoGrip Preview] Setup complete! Animation Window now previewing grip - Press Preview button or scrub timeline!");
            SceneView.RepaintAll();
        }

        private void StopAnimationPreview()
        {
            // Clear the restore action so EditorApplication.update doesn't interfere
            previewRestoreAction = null;
            
            if (previewClone != null)
            {
                DestroyImmediate(previewClone);
                previewClone = null;
            }

            if (originalAvatar != null)
            {
                originalAvatar.SetActive(true);
                originalAvatar = null;
            }

            Debug.Log("[AutoGrip Preview] Preview stopped, clone destroyed");
            SceneView.RepaintAll();
        }

        // Convert HumanTrait muscle name to .anim file format
        // "Left Index 1 Stretched" -> "LeftHand.Index.1 Stretched"
        // "Right Thumb 2 Stretched" -> "RightHand.Thumb.2 Stretched"
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

        // Convert .anim format back to HumanTrait format
        // "LeftHand.Index.1 Stretched" -> "Left Index 1 Stretched"
        // "RightHand.Thumb.2 Stretched" -> "Right Thumb 2 Stretched"
        // "LeftHand.Index.Spread" -> "Left Index Spread"
        private string ConvertFromAnimFormat(string animName)
        {
            if (string.IsNullOrEmpty(animName)) return null;
            
            string result = animName;
            
            // Remove "Hand." from the name
            result = result.Replace("LeftHand.", "Left ");
            result = result.Replace("RightHand.", "Right ");
            
            // Replace dots with spaces for the segment number
            // "Left Index.1 Stretched" -> "Left Index 1 Stretched"
            result = result.Replace(".1 ", " 1 ");
            result = result.Replace(".2 ", " 2 ");
            result = result.Replace(".3 ", " 3 ");
            
            // For spread muscles: "Left Index.Spread" -> "Left Index Spread"
            result = result.Replace(".Spread", " Spread");
            
            return result;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!data.showPreview) return;

            DrawPreviewGizmos();
        }

        private void DrawPreviewGizmos()
        {
            if (previewGripLeft != null)
            {
                foreach (var contact in previewGripLeft.contactPoints)
                {
                    Handles.color = Color.cyan;
                    Handles.SphereHandleCap(0, contact.position, Quaternion.identity, 0.01f, EventType.Repaint);
                    Handles.color = Color.yellow;
                    Handles.DrawLine(contact.position, contact.position + contact.normal * 0.02f);
                }
            }

            if (previewGripRight != null)
            {
                foreach (var contact in previewGripRight.contactPoints)
                {
                    Handles.color = Color.magenta;
                    Handles.SphereHandleCap(0, contact.position, Quaternion.identity, 0.01f, EventType.Repaint);
                    Handles.color = Color.yellow;
                    Handles.DrawLine(contact.position, contact.position + contact.normal * 0.02f);
                }
            }

            // Draw info overlay
            Handles.BeginGUI();

            GUI.Box(new Rect(10, 10, 250, 100), "");
            GUI.Label(new Rect(15, 15, 240, 20), "Auto Grip Preview", EditorStyles.boldLabel);

            int totalContacts = 0;
            if (previewGripLeft != null) totalContacts += previewGripLeft.contactPoints.Count;
            if (previewGripRight != null) totalContacts += previewGripRight.contactPoints.Count;

            GUI.color = Color.cyan;
            GUI.Box(new Rect(15, 40, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 38, 200, 20), "= Left hand contacts");

            GUI.color = Color.magenta;
            GUI.Box(new Rect(15, 60, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 58, 200, 20), "= Right hand contacts");

            GUI.Label(new Rect(15, 80, 230, 20), $"Total contacts: {totalContacts}");

            Handles.EndGUI();
        }
    }
}

