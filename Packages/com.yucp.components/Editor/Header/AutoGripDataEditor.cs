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
        private bool isContinuousPreview = false;
        private float lastIKUpdateTime = 0f;
        private const float IK_UPDATE_INTERVAL = 0.016f; // ~60 FPS max
        private static GripGenerator.GripResult previewGripLeft;
        private static GripGenerator.GripResult previewGripRight;
        private GripStyleDetector.ObjectAnalysis objectAnalysis;
        
        private static System.Action previewRestoreAction = null;
        
        // Store original bone rotations for proper restoration
        private static Dictionary<HumanBodyBones, Quaternion> originalBoneRotations = new Dictionary<HumanBodyBones, Quaternion>();
        
        // Store last valid IK solutions to prevent unnatural folding
        private static Dictionary<HumanBodyBones, Quaternion> lastValidLeftRotations = new Dictionary<HumanBodyBones, Quaternion>();
        private static Dictionary<HumanBodyBones, Quaternion> lastValidRightRotations = new Dictionary<HumanBodyBones, Quaternion>();

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
                
                // Also cleanup bone rotations if previews are cleared
                if (previewGripLeft == null && previewGripRight == null && originalBoneRotations.Count > 0)
                {
                    originalBoneRotations.Clear();
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

        /// <summary>
        /// Draw grip configuration header.
        /// </summary>
        private void DrawGripConfiguration()
        {
            EditorGUILayout.Space(10);
            
            // Main header
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 16;
            headerStyle.normal.textColor = new Color(0.212f, 0.749f, 0.694f);
            EditorGUILayout.LabelField("GRIP CONFIGURATION", headerStyle);
            
            EditorGUILayout.Space(5);
            
            // Show that we're using manual grip positioning
            EditorGUILayout.LabelField("Manual Finger Positioning", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Position finger gizmos manually in Scene view to create your grip.", MessageType.Info);
            
            EditorGUILayout.Space(5);
            
            // Auto-show gizmos toggle
            EditorGUI.BeginChangeCheck();
            data.autoShowGizmos = EditorGUILayout.Toggle("Auto-Show Gizmos in Scene View", data.autoShowGizmos);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(data);
            }
            
            EditorGUILayout.Space(10);
        }
        
        /// <summary>
        /// Check if all finger positions are set to zero (no preset applied).
        /// </summary>
        private bool AreAllFingerPositionsZero()
        {
            return data.leftThumbTip == Vector3.zero && data.leftIndexTip == Vector3.zero && 
                   data.leftMiddleTip == Vector3.zero && data.leftRingTip == Vector3.zero && 
                   data.leftLittleTip == Vector3.zero && data.rightThumbTip == Vector3.zero && 
                   data.rightIndexTip == Vector3.zero && data.rightMiddleTip == Vector3.zero && 
                   data.rightRingTip == Vector3.zero && data.rightLittleTip == Vector3.zero;
        }
        
        /// <summary>
        /// Draw manual finger positioning controls (only shown for Custom grip type).
        /// </summary>
        private void DrawManualFingerPositioning()
        {
            EditorGUILayout.Space(10);
            
            // Header with foldout
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 12;
            headerStyle.normal.textColor = new Color(1f, 0.8f, 0.2f); // Orange
            
            EditorGUILayout.LabelField("Manual Finger Positioning", headerStyle);
            EditorGUILayout.HelpBox("Drag the gizmos in Scene view to position finger tips manually. " +
                                  "Use the rotation handles to adjust finger orientations.", MessageType.Info);
            
            EditorGUILayout.Space(5);
            
            // Show current finger tip positions in a compact format
            EditorGUILayout.LabelField("Current Finger Positions:", EditorStyles.miniBoldLabel);
            
            EditorGUI.indentLevel++;
            
            // Get hand transforms for coordinate conversion
            Animator animator = data.GetComponentInParent<Animator>();
            Transform leftHandTransform = animator?.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHandTransform = animator?.GetBoneTransform(HumanBodyBones.RightHand);
            
            // Left hand positions
            EditorGUILayout.LabelField("Left Hand:", EditorStyles.miniLabel);
            EditorGUI.indentLevel++;
            DrawFingerPositionField("Thumb", data.leftThumbTip, leftHandTransform, "Thumb", true);
            DrawFingerPositionField("Index", data.leftIndexTip, leftHandTransform, "Index", true);
            DrawFingerPositionField("Middle", data.leftMiddleTip, leftHandTransform, "Middle", true);
            DrawFingerPositionField("Ring", data.leftRingTip, leftHandTransform, "Ring", true);
            DrawFingerPositionField("Little", data.leftLittleTip, leftHandTransform, "Little", true);
            EditorGUI.indentLevel--;
            
            EditorGUILayout.Space(3);
            
            // Right hand positions
            EditorGUILayout.LabelField("Right Hand:", EditorStyles.miniLabel);
            EditorGUI.indentLevel++;
            DrawFingerPositionField("Thumb", data.rightThumbTip, rightHandTransform, "Thumb", false);
            DrawFingerPositionField("Index", data.rightIndexTip, rightHandTransform, "Index", false);
            DrawFingerPositionField("Middle", data.rightMiddleTip, rightHandTransform, "Middle", false);
            DrawFingerPositionField("Ring", data.rightRingTip, rightHandTransform, "Ring", false);
            DrawFingerPositionField("Little", data.rightLittleTip, rightHandTransform, "Little", false);
            EditorGUI.indentLevel--;
            
            EditorGUI.indentLevel--;
            
            EditorGUILayout.Space(5);
            
            // Reset button
            if (GUILayout.Button("Reset All Finger Positions", GUILayout.Height(20)))
            {
                Undo.RecordObject(data, "Reset Finger Positions");
                ResetAllFingerPositions();
                EditorUtility.SetDirty(data);
            }

            // Reset manual tips to calculated snapped positions (ignores manual seeds)
            if (GUILayout.Button("Set Manual Tips From Calculated Surface", GUILayout.Height(22)))
            {
                var sceneAnimator = FindAnimator();
                if (sceneAnimator == null)
                {
                    Debug.LogWarning("[AutoGrip Editor] No Animator found to compute snapped tips");
                }
                else if (data.grippedObject == null)
                {
                    Debug.LogWarning("[AutoGrip Editor] Assign a Gripped Object before resetting tips");
                }
                else
                {
                    Undo.RecordObject(data, "Set Manual Tips From Calculated Surface");

                    void ApplySnapped(bool isLeftHand)
                    {
                        // Always compute from defaults and snap to actual surface
                        var snapped = GripGenerator.ComputeSnappedDefaultTargets(sceneAnimator, data, isLeftHand, out var _);

                        // Store as LOCAL positions relative to gripped object
                        if (isLeftHand)
                        {
                            data.leftThumbTip = WorldToLocalPosition(snapped.thumbTip);
                            data.leftIndexTip = WorldToLocalPosition(snapped.indexTip);
                            data.leftMiddleTip = WorldToLocalPosition(snapped.middleTip);
                            data.leftRingTip = WorldToLocalPosition(snapped.ringTip);
                            data.leftLittleTip = WorldToLocalPosition(snapped.littleTip);

                            data.leftThumbRotation = snapped.thumbRotation;
                            data.leftIndexRotation = snapped.indexRotation;
                            data.leftMiddleRotation = snapped.middleRotation;
                            data.leftRingRotation = snapped.ringRotation;
                            data.leftLittleRotation = snapped.littleRotation;
                        }
                        else
                        {
                            data.rightThumbTip = WorldToLocalPosition(snapped.thumbTip);
                            data.rightIndexTip = WorldToLocalPosition(snapped.indexTip);
                            data.rightMiddleTip = WorldToLocalPosition(snapped.middleTip);
                            data.rightRingTip = WorldToLocalPosition(snapped.ringTip);
                            data.rightLittleTip = WorldToLocalPosition(snapped.littleTip);

                            data.rightThumbRotation = snapped.thumbRotation;
                            data.rightIndexRotation = snapped.indexRotation;
                            data.rightMiddleRotation = snapped.middleRotation;
                            data.rightRingRotation = snapped.ringRotation;
                            data.rightLittleRotation = snapped.littleRotation;
                        }
                    }

                    switch (data.targetHand)
                    {
                        case HandTarget.Left:
                            ApplySnapped(true);
                            break;
                        case HandTarget.Right:
                            ApplySnapped(false);
                            break;
                        case HandTarget.Both:
                        case HandTarget.Closest:
                            ApplySnapped(true);
                            ApplySnapped(false);
                            break;
                    }

                    EditorUtility.SetDirty(data);
                    SceneView.RepaintAll();
                    Repaint();
                    Debug.Log("[AutoGrip Editor] Manual tips updated from calculated surface positions");
                }
            }
        }
        
        /// <summary>
        /// Draw a compact finger position field showing the actual world position.
        /// </summary>
        private void DrawFingerPositionField(string fingerName, Vector3 storedPosition, Transform handTransform, string fingerType, bool isLeftHand)
        {
            // Get the actual world position that the gizmo is using
            Vector3 worldPosition = GetFingerWorldPosition(storedPosition, handTransform, fingerType, isLeftHand);
            
            // Show the world position as editable
            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPosition = EditorGUILayout.Vector3Field(fingerName, worldPosition);
            if (EditorGUI.EndChangeCheck())
            {
                // Update the stored position based on the new world position
                UpdateFingerPosition(newWorldPosition, fingerType, isLeftHand);
                EditorUtility.SetDirty(data);
            }
            
            // Show coordinate system info
            string coordinateInfo = data.grippedObject != null ? "(Local to Object)" : "(World Space)";
            EditorGUILayout.LabelField(coordinateInfo, EditorStyles.miniLabel);
        }
        
        /// <summary>
        /// Reset all finger positions to zero.
        /// </summary>
        private void ResetAllFingerPositions()
        {
            data.leftThumbTip = Vector3.zero;
            data.leftIndexTip = Vector3.zero;
            data.leftMiddleTip = Vector3.zero;
            data.leftRingTip = Vector3.zero;
            data.leftLittleTip = Vector3.zero;
            
            data.rightThumbTip = Vector3.zero;
            data.rightIndexTip = Vector3.zero;
            data.rightMiddleTip = Vector3.zero;
            data.rightRingTip = Vector3.zero;
            data.rightLittleTip = Vector3.zero;
            
            data.leftThumbRotation = Quaternion.identity;
            data.leftIndexRotation = Quaternion.identity;
            data.leftMiddleRotation = Quaternion.identity;
            data.leftRingRotation = Quaternion.identity;
            data.leftLittleRotation = Quaternion.identity;
            
            data.rightThumbRotation = Quaternion.identity;
            data.rightIndexRotation = Quaternion.identity;
            data.rightMiddleRotation = Quaternion.identity;
            data.rightRingRotation = Quaternion.identity;
            data.rightLittleRotation = Quaternion.identity;
        }
        

        private void OnInspectorGUIContent()
        {
            serializedObject.Update();
            
            // Draw grip configuration at the top
            DrawGripConfiguration();
            
            // Draw only essential properties, excluding all the manual finger fields
            DrawPropertiesExcluding(serializedObject, 
                "m_Script", 
                "leftThumbTip", "leftIndexTip", "leftMiddleTip", "leftRingTip", "leftLittleTip",
                "rightThumbTip", "rightIndexTip", "rightMiddleTip", "rightRingTip", "rightLittleTip",
                "leftThumbRotation", "leftIndexRotation", "leftMiddleRotation", "leftRingRotation", "leftLittleRotation",
                "rightThumbRotation", "rightIndexRotation", "rightMiddleRotation", "rightRingRotation", "rightLittleRotation"
            );
            
            // Draw manual finger positioning section only if Custom grip type is selected
            // Show manual finger positioning fields
            DrawManualFingerPositioning();

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(10);

            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 14;
            headerStyle.normal.textColor = new Color(0.212f, 0.749f, 0.694f);
            EditorGUILayout.LabelField("OBJECT ANALYSIS", headerStyle);

            if (data.grippedObject != null)
            {
                if (objectAnalysis == null)
                {
                    objectAnalysis = GripStyleDetector.AnalyzeObject(data.grippedObject);
                }

                // Compact analysis display
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"Size: {objectAnalysis.size.x:F2} × {objectAnalysis.size.y:F2} × {objectAnalysis.size.z:F2}m", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Max Dimension: {objectAnalysis.maxDimension:F2}m", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Aspect Ratio: {objectAnalysis.aspectRatio:F1}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Has Handle: {(objectAnalysis.hasHandle ? "Yes" : "No")}", EditorStyles.miniLabel);
                
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Manual Grip Positioning", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(GripStyleDetector.GetGripStyleDescription(), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
                
            }
            else
            {
                EditorGUILayout.HelpBox("Assign a Gripped Object to see analysis and recommendations", MessageType.None);
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

            // Show continuous preview indicator
            if (isContinuousPreview)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("🔄 Continuous IK Preview Active - Move gizmos to see real-time finger updates", MessageType.Info);
            }

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
            Debug.Log("[AutoGrip Preview] Starting IK preview generation...");
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

                // Generate IK preview instead of full grip
                GenerateIKPreview(animator);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AutoGrip Preview] Error during preview generation: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                isGeneratingPreview = false;
                Debug.Log("[AutoGrip Preview] Preview generation complete");
            }
        }
        
        /// <summary>
        /// Generate IK preview showing finger positions and bone rotations.
        /// </summary>
        private void GenerateIKPreview(Animator animator)
        {
            // Clear previous previews
            previewGripLeft = null;
            previewGripRight = null;
            
            // Generate IK solutions for each hand based on target
            switch (data.targetHand)
            {
                case HandTarget.Left:
                    previewGripLeft = GenerateIKGrip(animator, true);
                    break;
                    
                case HandTarget.Right:
                    previewGripRight = GenerateIKGrip(animator, false);
                    break;
                    
                case HandTarget.Both:
                    previewGripLeft = GenerateIKGrip(animator, true);
                    previewGripRight = GenerateIKGrip(animator, false);
                    break;
                    
                case HandTarget.Closest:
                    bool isLeft = DetermineClosestHand(animator);
                    if (isLeft)
                    {
                        previewGripLeft = GenerateIKGrip(animator, true);
                    }
                    else
                    {
                        previewGripRight = GenerateIKGrip(animator, false);
                    }
                    break;
            }
            
            data.showPreview = true;
            PreviewIKPose();
            
            Debug.Log($"[AutoGrip Preview] IK preview generated - Left: {(previewGripLeft != null ? "Yes" : "No")}, Right: {(previewGripRight != null ? "Yes" : "No")}");
        }
        
        /// <summary>
        /// Generate IK grip for a specific hand.
        /// </summary>
        private GripGenerator.GripResult GenerateIKGrip(Animator animator, bool isLeftHand)
        {
            if (data.grippedObject == null)
            {
                Debug.LogWarning("[AutoGrip Preview] No gripped object assigned");
                return null;
            }
            
            string hand = isLeftHand ? "Left" : "Right";
            Debug.Log($"[AutoGrip Preview] Generating IK grip for {hand} hand...");
            
            // Compute snapped fingertip targets from manual gizmo positions
            var snappedTargets = GripGenerator.GetSnappedTargets(animator, data, isLeftHand, out var contacts);

            // If all snapped targets are zero (no user positions and no good surface), try initializing defaults once
            bool allZeroSnapped = snappedTargets.thumbTip == Vector3.zero &&
                                  snappedTargets.indexTip == Vector3.zero &&
                                  snappedTargets.middleTip == Vector3.zero &&
                                  snappedTargets.ringTip == Vector3.zero &&
                                  snappedTargets.littleTip == Vector3.zero;
            if (allZeroSnapped && data.grippedObject != null)
            {
                var defaults = FingerTipSolver.InitializeFingerTips(data.grippedObject, isLeftHand);
                var handUp = (animator.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand)?.up) ?? Vector3.up;
                contacts = new List<GripGenerator.ContactPoint>();
                // Fallback is simple: set positions to defaults so IK can attempt a pose
                snappedTargets.thumbTip = defaults.thumbTip;
                snappedTargets.indexTip = defaults.indexTip;
                snappedTargets.middleTip = defaults.middleTip;
                snappedTargets.ringTip = defaults.ringTip;
                snappedTargets.littleTip = defaults.littleTip;
                snappedTargets.thumbRotation = Quaternion.LookRotation(Vector3.forward, handUp);
                snappedTargets.indexRotation = Quaternion.LookRotation(Vector3.forward, handUp);
                snappedTargets.middleRotation = Quaternion.LookRotation(Vector3.forward, handUp);
                snappedTargets.ringRotation = Quaternion.LookRotation(Vector3.forward, handUp);
                snappedTargets.littleRotation = Quaternion.LookRotation(Vector3.forward, handUp);
            }
            
            // Debug: Log the snapped finger tip positions and rotations being used
            Debug.Log($"[AutoGrip Preview] {hand} snapped targets - " +
                    $"Thumb: {snappedTargets.thumbTip} (rot: {snappedTargets.thumbRotation.eulerAngles}), " +
                    $"Index: {snappedTargets.indexTip} (rot: {snappedTargets.indexRotation.eulerAngles}), " +
                    $"Middle: {snappedTargets.middleTip} (rot: {snappedTargets.middleRotation.eulerAngles}), " +
                    $"Ring: {snappedTargets.ringTip} (rot: {snappedTargets.ringRotation.eulerAngles}), " +
                    $"Little: {snappedTargets.littleTip} (rot: {snappedTargets.littleRotation.eulerAngles})");
            
            // Check if all finger positions are zero (no preset applied)
            bool allZero = snappedTargets.thumbTip == Vector3.zero && 
                          snappedTargets.indexTip == Vector3.zero && 
                          snappedTargets.middleTip == Vector3.zero && 
                          snappedTargets.ringTip == Vector3.zero && 
                          snappedTargets.littleTip == Vector3.zero;
            
            if (allZero)
            {
                Debug.LogWarning($"[AutoGrip Preview] All {hand} hand finger positions are zero! " +
                               "Try selecting a grip preset (Pinch, Power, etc.) or manually positioning finger gizmos in Scene view.");
                return null;
            }
            
            // Use FingerTipSolver to solve IK based on snapped targets with collision detection
            var solverResult = FingerTipSolver.SolveFingerTips(animator, snappedTargets, isLeftHand, data.grippedObject);
            
            if (!solverResult.success)
            {
                Debug.LogWarning($"[AutoGrip Preview] Failed to solve IK for {hand} hand: {solverResult.errorMessage}");
                return null;
            }
            
            // Validate the solution - if invalid, keep the last valid pose
            var validRotations = solverResult.solvedRotations;
            if (!solverResult.isValidPose)
            {
                Debug.LogWarning($"[AutoGrip Preview] {hand} hand pose is invalid (mesh penetration detected) - keeping last valid pose");
                
                // Use last valid rotations if available
                var lastValidDict = isLeftHand ? lastValidLeftRotations : lastValidRightRotations;
                if (lastValidDict.Count > 0)
                {
                    validRotations = lastValidDict;
                }
                else
                {
                    // No previous valid pose - use the current one anyway but warn
                    Debug.LogWarning($"[AutoGrip Preview] No previous valid pose for {hand} hand - using current pose anyway");
                }
            }
            else
            {
                // Update last valid pose
                var lastValidDict = isLeftHand ? lastValidLeftRotations : lastValidRightRotations;
                lastValidDict.Clear();
                foreach (var kvp in solverResult.solvedRotations)
                {
                    lastValidDict[kvp.Key] = kvp.Value;
                }
                Debug.Log($"[AutoGrip Preview] {hand} hand pose is valid - stored for future reference");
            }
            
            // Create a GripResult with the IK solution (bone rotations, not muscles)
            var gripResult = new GripGenerator.GripResult
            {
                muscleValues = new Dictionary<string, float>(), // Empty for preview
                contactPoints = new List<GripGenerator.ContactPoint>() // Will be filled from contacts
            };
            
            // Store the validated rotations for preview (we'll apply these directly to bones)
            gripResult.solvedRotations = validRotations;
            
            // Populate contact points for gizmos using snapped contacts
            if (contacts != null)
            {
                gripResult.contactPoints.AddRange(contacts);
            }
            
            Debug.Log($"[AutoGrip Preview] {hand} hand IK solved - Bone rotations: {gripResult.solvedRotations.Count}");
            
            return gripResult;
        }
        
        /// <summary>
        /// Get finger tip targets for the specified hand.
        /// </summary>
        private FingerTipSolver.FingerTipTarget GetFingerTargets(bool isLeftHand)
        {
            // Get hand transform for default positioning
            Animator animator = data.GetComponentInParent<Animator>();
            Transform handTransform = animator?.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            
            if (isLeftHand)
            {
                return new FingerTipSolver.FingerTipTarget
                {
                    // Convert local positions to world positions, using defaults if no gripped object
                    thumbTip = GetFingerWorldPosition(data.leftThumbTip, handTransform, "Thumb", isLeftHand),
                    indexTip = GetFingerWorldPosition(data.leftIndexTip, handTransform, "Index", isLeftHand),
                    middleTip = GetFingerWorldPosition(data.leftMiddleTip, handTransform, "Middle", isLeftHand),
                    ringTip = GetFingerWorldPosition(data.leftRingTip, handTransform, "Ring", isLeftHand),
                    littleTip = GetFingerWorldPosition(data.leftLittleTip, handTransform, "Little", isLeftHand),
                    
                    thumbRotation = data.leftThumbRotation,
                    indexRotation = data.leftIndexRotation,
                    middleRotation = data.leftMiddleRotation,
                    ringRotation = data.leftRingRotation,
                    littleRotation = data.leftLittleRotation
                };
            }
            else
            {
                return new FingerTipSolver.FingerTipTarget
                {
                    // Convert local positions to world positions, using defaults if no gripped object
                    thumbTip = GetFingerWorldPosition(data.rightThumbTip, handTransform, "Thumb", isLeftHand),
                    indexTip = GetFingerWorldPosition(data.rightIndexTip, handTransform, "Index", isLeftHand),
                    middleTip = GetFingerWorldPosition(data.rightMiddleTip, handTransform, "Middle", isLeftHand),
                    ringTip = GetFingerWorldPosition(data.rightRingTip, handTransform, "Ring", isLeftHand),
                    littleTip = GetFingerWorldPosition(data.rightLittleTip, handTransform, "Little", isLeftHand),
                    
                    thumbRotation = data.rightThumbRotation,
                    indexRotation = data.rightIndexRotation,
                    middleRotation = data.rightMiddleRotation,
                    ringRotation = data.rightRingRotation,
                    littleRotation = data.rightLittleRotation
                };
            }
        }
        
        /// <summary>
        /// Preview the IK pose by applying bone rotations directly.
        /// </summary>
        private void PreviewIKPose()
        {
            var animator = FindAnimator();
            if (animator == null)
            {
                Debug.LogError("[AutoGrip Preview] No Animator found on avatar");
                return;
            }

            // Save original bone rotations before applying IK
            SaveOriginalBoneRotations(animator);

            // Apply bone rotations directly for IK preview
            ApplyBoneRotationsToAnimator(animator);
            
            SceneView.RepaintAll();
            Repaint();
            
            Debug.Log("[AutoGrip Preview] IK preview applied using direct bone rotations - Check Scene view for visualization");
        }
        
        /// <summary>
        /// Save original bone rotations before applying IK preview.
        /// </summary>
        private void SaveOriginalBoneRotations(Animator animator)
        {
            originalBoneRotations.Clear();
            
            // Save all finger bone rotations
            HumanBodyBones[] fingerBones = {
                HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
                HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
                HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
                HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
                HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
                HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
                HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
                HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
                HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
                HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
            };
            
            foreach (var bone in fingerBones)
            {
                var boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform != null)
                {
                    // Save LOCAL rotation to restore consistently via Animator API
                    originalBoneRotations[bone] = boneTransform.localRotation;
                }
            }
            
            Debug.Log($"[AutoGrip Preview] Saved {originalBoneRotations.Count} original bone rotations");
        }
        
        /// <summary>
        /// Apply bone rotations directly to the animator for IK preview.
        /// </summary>
        private void ApplyBoneRotationsToAnimator(Animator animator)
        {
            // Apply left hand bone rotations
            if (previewGripLeft != null && previewGripLeft.solvedRotations != null)
            {
                foreach (var boneRotation in previewGripLeft.solvedRotations)
                {
                    var boneTransform = animator.GetBoneTransform(boneRotation.Key);
                    if (boneTransform != null)
                    {
                        // Convert target world rotation to local, then damp and write via Animator
                        Quaternion parentWorld = boneTransform.parent != null ? boneTransform.parent.rotation : Quaternion.identity;
                        Quaternion targetLocal = Quaternion.Inverse(parentWorld) * boneRotation.Value;
                        Quaternion currentLocal = boneTransform.localRotation;
                        Quaternion nextLocal = Quaternion.Slerp(currentLocal, targetLocal, 0.25f);
                        if (Application.isPlaying)
                        {
                            animator.SetBoneLocalRotation(boneRotation.Key, nextLocal);
                        }
                        else
                        {
                            boneTransform.localRotation = nextLocal; // editor preview
                        }
                    }
                }
            }
            
            // Apply right hand bone rotations
            if (previewGripRight != null && previewGripRight.solvedRotations != null)
            {
                foreach (var boneRotation in previewGripRight.solvedRotations)
                {
                    var boneTransform = animator.GetBoneTransform(boneRotation.Key);
                    if (boneTransform != null)
                    {
                        Quaternion parentWorld = boneTransform.parent != null ? boneTransform.parent.rotation : Quaternion.identity;
                        Quaternion targetLocal = Quaternion.Inverse(parentWorld) * boneRotation.Value;
                        Quaternion currentLocal = boneTransform.localRotation;
                        Quaternion nextLocal = Quaternion.Slerp(currentLocal, targetLocal, 0.25f);
                        if (Application.isPlaying)
                        {
                            animator.SetBoneLocalRotation(boneRotation.Key, nextLocal);
                        }
                        else
                        {
                            boneTransform.localRotation = nextLocal; // editor preview
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply muscle values directly to the animator for IK preview.
        /// </summary>
        private void ApplyMuscleValuesToAnimator(Animator animator)
        {
            // Apply left hand muscle values
            if (previewGripLeft != null && previewGripLeft.muscleValues != null)
            {
                foreach (var muscle in previewGripLeft.muscleValues)
                {
                    animator.SetFloat(muscle.Key, muscle.Value);
                    Debug.Log($"[AutoGrip Preview] Set {muscle.Key} = {muscle.Value}");
                }
                Debug.Log($"[AutoGrip Preview] Applied {previewGripLeft.muscleValues.Count} left hand muscle values");
            }
            
            // Apply right hand muscle values
            if (previewGripRight != null && previewGripRight.muscleValues != null)
            {
                foreach (var muscle in previewGripRight.muscleValues)
                {
                    animator.SetFloat(muscle.Key, muscle.Value);
                    Debug.Log($"[AutoGrip Preview] Set {muscle.Key} = {muscle.Value}");
                }
                Debug.Log($"[AutoGrip Preview] Applied {previewGripRight.muscleValues.Count} right hand muscle values");
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
            // Reset bone rotations to clear IK preview
            var animator = FindAnimator();
            if (animator != null && originalBoneRotations.Count > 0)
            {
                ResetBoneRotations(animator);
            }
            
            previewGripLeft = null;
            previewGripRight = null;
            data.showPreview = false;
            
            SceneView.RepaintAll();
            Repaint();
            
            Debug.Log("[AutoGrip Preview] IK preview cleared");
        }
        
        /// <summary>
        /// Reset bone rotations to clear the IK preview.
        /// </summary>
        private void ResetBoneRotations(Animator animator)
        {
            // Restore original bone rotations
            foreach (var boneRotation in originalBoneRotations)
            {
                var boneTransform = animator.GetBoneTransform(boneRotation.Key);
                if (boneTransform != null)
                {
                    // Restore saved LOCAL rotation (Animator API only in play mode)
                    if (Application.isPlaying)
                    {
                        animator.SetBoneLocalRotation(boneRotation.Key, boneRotation.Value);
                    }
                    else
                    {
                        boneTransform.localRotation = boneRotation.Value;
                    }
                }
            }
            
            Debug.Log($"[AutoGrip Preview] Restored {originalBoneRotations.Count} original bone rotations");
            originalBoneRotations.Clear();
        }
        
        /// <summary>
        /// Reset all muscle values to zero to clear the IK preview.
        /// </summary>
        private void ResetMuscleValues(Animator animator)
        {
            // Reset all hand muscle values to zero
            string[] leftMuscles = {
                "Left Thumb 1 Stretched", "Left Thumb Spread", "Left Thumb 2 Stretched", "Left Thumb 3 Stretched",
                "Left Index 1 Stretched", "Left Index Spread", "Left Index 2 Stretched", "Left Index 3 Stretched",
                "Left Middle 1 Stretched", "Left Middle Spread", "Left Middle 2 Stretched", "Left Middle 3 Stretched",
                "Left Ring 1 Stretched", "Left Ring Spread", "Left Ring 2 Stretched", "Left Ring 3 Stretched",
                "Left Little 1 Stretched", "Left Little Spread", "Left Little 2 Stretched", "Left Little 3 Stretched"
            };
            
            string[] rightMuscles = {
                "Right Thumb 1 Stretched", "Right Thumb Spread", "Right Thumb 2 Stretched", "Right Thumb 3 Stretched",
                "Right Index 1 Stretched", "Right Index Spread", "Right Index 2 Stretched", "Right Index 3 Stretched",
                "Right Middle 1 Stretched", "Right Middle Spread", "Right Middle 2 Stretched", "Right Middle 3 Stretched",
                "Right Ring 1 Stretched", "Right Ring Spread", "Right Ring 2 Stretched", "Right Ring 3 Stretched",
                "Right Little 1 Stretched", "Right Little Spread", "Right Little 2 Stretched", "Right Little 3 Stretched"
            };
            
            foreach (var muscle in leftMuscles)
            {
                animator.SetFloat(muscle, 0f);
            }
            
            foreach (var muscle in rightMuscles)
            {
                animator.SetFloat(muscle, 0f);
            }
            
            Debug.Log("[AutoGrip Preview] Reset all muscle values to zero");
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
                DrawGripGizmos(previewGripLeft, new Color(0.212f, 0.749f, 0.694f));
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

            GUI.color = new Color(0.212f, 0.749f, 0.694f);
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
            // Always show gizmos when component is selected and autoShowGizmos is enabled
            if (data.autoShowGizmos && Selection.activeGameObject == data.gameObject)
            {
                DrawFingerTipGizmos();
            }
            
            // Show preview gizmos if enabled
            if (data.showPreview)
            {
                DrawPreviewGizmos();
                
                // Enable continuous IK calculation when preview is on
                if (!isContinuousPreview)
                {
                    isContinuousPreview = true;
                    Debug.Log("[AutoGrip Preview] Continuous IK calculation enabled");
                }
                
                // Continuously recalculate IK when gizmos are visible
                UpdateContinuousPreview();
            }
            else if (isContinuousPreview)
            {
                // Disable continuous preview when preview is turned off
                isContinuousPreview = false;
                Debug.Log("[AutoGrip Preview] Continuous IK calculation disabled");
            }
        }
        
        /// <summary>
        /// Update continuous preview by recalculating IK in real-time.
        /// </summary>
        private void UpdateContinuousPreview()
        {
            // Frame rate limiting to avoid excessive IK calculations
            float currentTime = (float)EditorApplication.timeSinceStartup;
            if (currentTime - lastIKUpdateTime < IK_UPDATE_INTERVAL)
            {
                return;
            }
            lastIKUpdateTime = currentTime;
            
            var animator = FindAnimator();
            if (animator == null) return;
            
            // Only recalculate if we have valid finger positions
            bool hasValidPositions = HasValidFingerPositions();
            if (!hasValidPositions) return;
            
            // Recalculate IK for each hand based on target
            switch (data.targetHand)
            {
                case HandTarget.Left:
                    previewGripLeft = GenerateIKGrip(animator, true);
                    break;
                    
                case HandTarget.Right:
                    previewGripRight = GenerateIKGrip(animator, false);
                    break;
                    
                case HandTarget.Both:
                    previewGripLeft = GenerateIKGrip(animator, true);
                    previewGripRight = GenerateIKGrip(animator, false);
                    break;
                    
                case HandTarget.Closest:
                    bool isLeft = DetermineClosestHand(animator);
                    if (isLeft)
                    {
                        previewGripLeft = GenerateIKGrip(animator, true);
                        previewGripRight = null;
                    }
                    else
                    {
                        previewGripRight = GenerateIKGrip(animator, false);
                        previewGripLeft = null;
                    }
                    break;
            }
            
            // Apply the updated IK solution
            ApplyBoneRotationsToAnimator(animator);
        }
        
        /// <summary>
        /// Check if we have valid finger positions for IK calculation.
        /// </summary>
        private bool HasValidFingerPositions()
        {
            switch (data.targetHand)
            {
                case HandTarget.Left:
                    return data.leftThumbTip != Vector3.zero || data.leftIndexTip != Vector3.zero || 
                           data.leftMiddleTip != Vector3.zero || data.leftRingTip != Vector3.zero || 
                           data.leftLittleTip != Vector3.zero;
                           
                case HandTarget.Right:
                    return data.rightThumbTip != Vector3.zero || data.rightIndexTip != Vector3.zero || 
                           data.rightMiddleTip != Vector3.zero || data.rightRingTip != Vector3.zero || 
                           data.rightLittleTip != Vector3.zero;
                           
                case HandTarget.Both:
                    return (data.leftThumbTip != Vector3.zero || data.leftIndexTip != Vector3.zero || 
                            data.leftMiddleTip != Vector3.zero || data.leftRingTip != Vector3.zero || 
                            data.leftLittleTip != Vector3.zero) ||
                           (data.rightThumbTip != Vector3.zero || data.rightIndexTip != Vector3.zero || 
                            data.rightMiddleTip != Vector3.zero || data.rightRingTip != Vector3.zero || 
                            data.rightLittleTip != Vector3.zero);
                           
                case HandTarget.Closest:
                    return data.leftThumbTip != Vector3.zero || data.leftIndexTip != Vector3.zero || 
                           data.leftMiddleTip != Vector3.zero || data.leftRingTip != Vector3.zero || 
                           data.leftLittleTip != Vector3.zero ||
                           data.rightThumbTip != Vector3.zero || data.rightIndexTip != Vector3.zero || 
                           data.rightMiddleTip != Vector3.zero || data.rightRingTip != Vector3.zero || 
                           data.rightLittleTip != Vector3.zero;
                           
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Draw draggable finger tip gizmos in Scene view.
        /// </summary>
        private void DrawFingerTipGizmos()
        {
            if (data.grippedObject == null) return;
            
            // Get hand bone for reference
            Animator animator = data.GetComponentInParent<Animator>();
            if (animator == null) return;
            
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            
            // Determine which hands to show gizmos for
            switch (data.targetHand)
            {
                case HandTarget.Left:
                    if (leftHand != null)
                    {
                        DrawHandGizmos(true, leftHand.position);
                    }
                    break;
                    
                case HandTarget.Right:
                    if (rightHand != null)
                    {
                        DrawHandGizmos(false, rightHand.position);
                    }
                    break;
                    
                case HandTarget.Both:
                    if (leftHand != null)
                    {
                        DrawHandGizmos(true, leftHand.position);
                    }
                    if (rightHand != null)
                    {
                        DrawHandGizmos(false, rightHand.position);
                    }
                    break;
                    
                case HandTarget.Closest:
                    // Show gizmos only for the closest hand
                    if (leftHand != null && rightHand != null)
                    {
                        float leftDistance = Vector3.Distance(leftHand.position, data.grippedObject.position);
                        float rightDistance = Vector3.Distance(rightHand.position, data.grippedObject.position);
                        
                        if (leftDistance < rightDistance)
                        {
                            DrawHandGizmos(true, leftHand.position);
                        }
                        else
                        {
                            DrawHandGizmos(false, rightHand.position);
                        }
                    }
                    else if (leftHand != null)
                    {
                        DrawHandGizmos(true, leftHand.position);
                    }
                    else if (rightHand != null)
                    {
                        DrawHandGizmos(false, rightHand.position);
                    }
                    break;
            }
        }
        
        /// <summary>
        /// Draw gizmos for one hand.
        /// </summary>
        private void DrawHandGizmos(bool isLeftHand, Vector3 handPosition)
        {
            Vector3 thumbTip, indexTip, middleTip, ringTip, littleTip;
            Quaternion thumbRot, indexRot, middleRot, ringRot, littleRot;
            
            // Get hand transform for default positioning
            Animator animator = data.GetComponentInParent<Animator>();
            Transform handTransform = animator?.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            
            if (isLeftHand)
            {
                // Convert local positions to world positions, using defaults if no gripped object
                thumbTip = GetFingerWorldPosition(data.leftThumbTip, handTransform, "Thumb", isLeftHand);
                indexTip = GetFingerWorldPosition(data.leftIndexTip, handTransform, "Index", isLeftHand);
                middleTip = GetFingerWorldPosition(data.leftMiddleTip, handTransform, "Middle", isLeftHand);
                ringTip = GetFingerWorldPosition(data.leftRingTip, handTransform, "Ring", isLeftHand);
                littleTip = GetFingerWorldPosition(data.leftLittleTip, handTransform, "Little", isLeftHand);
                thumbRot = data.leftThumbRotation;
                indexRot = data.leftIndexRotation;
                middleRot = data.leftMiddleRotation;
                ringRot = data.leftRingRotation;
                littleRot = data.leftLittleRotation;
            }
            else
            {
                // Convert local positions to world positions, using defaults if no gripped object
                thumbTip = GetFingerWorldPosition(data.rightThumbTip, handTransform, "Thumb", isLeftHand);
                indexTip = GetFingerWorldPosition(data.rightIndexTip, handTransform, "Index", isLeftHand);
                middleTip = GetFingerWorldPosition(data.rightMiddleTip, handTransform, "Middle", isLeftHand);
                ringTip = GetFingerWorldPosition(data.rightRingTip, handTransform, "Ring", isLeftHand);
                littleTip = GetFingerWorldPosition(data.rightLittleTip, handTransform, "Little", isLeftHand);
                thumbRot = data.rightThumbRotation;
                indexRot = data.rightIndexRotation;
                middleRot = data.rightMiddleRotation;
                ringRot = data.rightRingRotation;
                littleRot = data.rightLittleRotation;
            }
            
            // Draw finger tip gizmos with colors
            DrawFingerGizmo(thumbTip, thumbRot, Color.red, "Thumb", isLeftHand);
            DrawFingerGizmo(indexTip, indexRot, Color.yellow, "Index", isLeftHand);
            DrawFingerGizmo(middleTip, middleRot, Color.green, "Middle", isLeftHand);
            DrawFingerGizmo(ringTip, ringRot, new Color(0.212f, 0.749f, 0.694f), "Ring", isLeftHand);
            DrawFingerGizmo(littleTip, littleRot, new Color(0.212f, 0.749f, 0.694f), "Little", isLeftHand);
            
            // Calculate palm center (midpoint between wrist and middle finger tip)
            var middleDistal = animator?.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
            Vector3 palmCenter = handPosition; // Default to wrist if no middle finger
            if (middleDistal != null)
            {
                Vector3 middleFingerTipPos = middleDistal.position + middleDistal.forward * 0.02f;
                palmCenter = (handPosition + middleFingerTipPos) * 0.5f;
            }
            
            // Draw lines from palm center to finger tips
            DrawHandToFingerLines(palmCenter, thumbTip, indexTip, middleTip, ringTip, littleTip);
        }
        
        /// <summary>
        /// Draw a single finger gizmo with position and rotation handles.
        /// </summary>
        private void DrawFingerGizmo(Vector3 position, Quaternion rotation, Color color, string fingerName, bool isLeftHand)
        {
            // If position is zero, use a default position relative to the hand
            if (position == Vector3.zero)
            {
                Animator animator = data.GetComponentInParent<Animator>();
                if (animator == null) return;
                
                Transform handTransform = animator.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (handTransform == null) return;
                
                // Calculate default position for this finger (returns local coordinates)
                Vector3 localDefaultPosition = CalculateDefaultFingerPosition(handTransform, fingerName, isLeftHand);
                position = LocalToWorldPosition(localDefaultPosition);
                
                // Use a lighter color to indicate this is a default position
                color = new Color(color.r, color.g, color.b, 0.5f);
            }
            
            Handles.color = color;
            
            // Draw position handle
            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(position, rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(data, $"Move {fingerName} Tip");
                UpdateFingerPosition(newPosition, fingerName, isLeftHand);
                EditorUtility.SetDirty(data);
            }
            
            // Draw rotation handle
            EditorGUI.BeginChangeCheck();
            Quaternion newRotation = Handles.RotationHandle(rotation, position);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(data, $"Rotate {fingerName} Tip");
                UpdateFingerRotation(newRotation, fingerName, isLeftHand);
                EditorUtility.SetDirty(data);
            }
            
            // Draw coordinate axes (smaller)
            Handles.color = color * 0.7f;
            Handles.DrawLine(position, position + rotation * Vector3.right * 0.005f);
            Handles.DrawLine(position, position + rotation * Vector3.up * 0.005f);
            Handles.DrawLine(position, position + rotation * Vector3.forward * 0.005f);
            
            // Draw finger name label with default indicator
            string labelText = fingerName;
            if (color.a < 1.0f) // Semi-transparent color indicates default position
            {
                labelText += " (Default)";
            }
            Handles.Label(position + Vector3.up * 0.01f, labelText);
            
            // Draw sphere at tip (smaller)
            Handles.color = color;
            Handles.SphereHandleCap(0, position, rotation, 0.003f, EventType.Repaint);
        }
        
        /// <summary>
        /// Update finger position in data component (stores in local coordinates relative to gripped object).
        /// </summary>
        private void UpdateFingerPosition(Vector3 worldPosition, string fingerName, bool isLeftHand)
        {
            // If no gripped object, store world position directly
            Vector3 positionToStore = data.grippedObject != null ? WorldToLocalPosition(worldPosition) : worldPosition;
            
            if (isLeftHand)
            {
                switch (fingerName)
                {
                    case "Thumb": data.leftThumbTip = positionToStore; break;
                    case "Index": data.leftIndexTip = positionToStore; break;
                    case "Middle": data.leftMiddleTip = positionToStore; break;
                    case "Ring": data.leftRingTip = positionToStore; break;
                    case "Little": data.leftLittleTip = positionToStore; break;
                }
            }
            else
            {
                switch (fingerName)
                {
                    case "Thumb": data.rightThumbTip = positionToStore; break;
                    case "Index": data.rightIndexTip = positionToStore; break;
                    case "Middle": data.rightMiddleTip = positionToStore; break;
                    case "Ring": data.rightRingTip = positionToStore; break;
                    case "Little": data.rightLittleTip = positionToStore; break;
                }
            }
            
            // Trigger continuous preview update if preview is active
            if (isContinuousPreview)
            {
                SceneView.RepaintAll();
            }
        }
        
        /// <summary>
        /// Update finger rotation in data component.
        /// </summary>
        private void UpdateFingerRotation(Quaternion rotation, string fingerName, bool isLeftHand)
        {
            if (isLeftHand)
            {
                switch (fingerName)
                {
                    case "Thumb": data.leftThumbRotation = rotation; break;
                    case "Index": data.leftIndexRotation = rotation; break;
                    case "Middle": data.leftMiddleRotation = rotation; break;
                    case "Ring": data.leftRingRotation = rotation; break;
                    case "Little": data.leftLittleRotation = rotation; break;
                }
            }
            else
            {
                switch (fingerName)
                {
                    case "Thumb": data.rightThumbRotation = rotation; break;
                    case "Index": data.rightIndexRotation = rotation; break;
                    case "Middle": data.rightMiddleRotation = rotation; break;
                    case "Ring": data.rightRingRotation = rotation; break;
                    case "Little": data.rightLittleRotation = rotation; break;
                }
            }
            
            // Trigger continuous preview update if preview is active
            if (isContinuousPreview)
            {
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// Convert world position to local position relative to gripped object.
        /// </summary>
        private Vector3 WorldToLocalPosition(Vector3 worldPosition)
        {
            if (data.grippedObject == null) return worldPosition;
            return data.grippedObject.InverseTransformPoint(worldPosition);
        }
        
        /// <summary>
        /// Convert local position relative to gripped object to world position.
        /// </summary>
        private Vector3 LocalToWorldPosition(Vector3 localPosition)
        {
            if (data.grippedObject == null) return localPosition;
            return data.grippedObject.TransformPoint(localPosition);
        }
        
        /// <summary>
        /// Get world position for a finger, using default position if stored position is zero or no gripped object.
        /// </summary>
        private Vector3 GetFingerWorldPosition(Vector3 storedPosition, Transform handTransform, string fingerName, bool isLeftHand)
        {
            // If we have a gripped object and a non-zero stored position, convert from local to world
            if (data.grippedObject != null && storedPosition != Vector3.zero)
            {
                return LocalToWorldPosition(storedPosition);
            }
            
            // If no gripped object and we have a stored position, it's already a world position
            if (data.grippedObject == null && storedPosition != Vector3.zero)
            {
                return storedPosition;
            }
            
            // Otherwise, calculate a default world position relative to the hand
            if (handTransform != null)
            {
                return CalculateDefaultFingerPosition(handTransform, fingerName, isLeftHand);
            }
            
            // Fallback: return the stored position as-is (will be zero)
            return storedPosition;
        }

        /// <summary>
        /// Calculate a default finger position when the stored position is zero.
        /// </summary>
        private Vector3 CalculateDefaultFingerPosition(Transform handTransform, string fingerName, bool isLeftHand)
        {
            Vector3 basePosition;
            
            // If no gripped object, use object center as base position
            if (data.grippedObject == null)
            {
                // Use the component's transform position as the object center
                basePosition = data.transform.position;
            }
            else
            {
                // Use hand position as base
                basePosition = handTransform.position;
            }
            
            // Get hand forward direction (palm normal)
            Vector3 handForward = handTransform.forward;
            Vector3 handRight = handTransform.right;
            Vector3 handUp = handTransform.up;
            
            // Calculate finger spread directions
            float spreadAngle = fingerName switch
            {
                "Thumb" => isLeftHand ? 45f : -45f,  // Thumb points inward
                "Index" => 0f,                       // Index points forward
                "Middle" => 0f,                      // Middle points forward
                "Ring" => -15f,                      // Ring points slightly inward
                "Little" => -30f,                    // Little points more inward
                _ => 0f
            };
            
            // Calculate finger extension distance (reduced for better default positioning)
            float extensionDistance = fingerName switch
            {
                "Thumb" => 0.04f,    // 4cm
                "Index" => 0.05f,    // 5cm
                "Middle" => 0.06f,   // 6cm
                "Ring" => 0.05f,     // 5cm
                "Little" => 0.04f,   // 4cm
                _ => 0.05f
            };
            
            // Calculate finger direction
            Vector3 fingerDirection = Quaternion.AngleAxis(spreadAngle, handUp) * handForward;
            
            // Calculate default position in world coordinates
            Vector3 worldDefaultPosition = basePosition + fingerDirection * extensionDistance;
            
            // If no gripped object, return world coordinates directly
            if (data.grippedObject == null)
            {
                return worldDefaultPosition;
            }
            
            // Convert to local coordinates relative to gripped object
            Vector3 localDefaultPosition = WorldToLocalPosition(worldDefaultPosition);
            
            return localDefaultPosition;
        }

        /// <summary>
        /// Draw lines from hand to finger tips.
        /// </summary>
        private void DrawHandToFingerLines(Vector3 handPosition, Vector3 thumbTip, Vector3 indexTip, Vector3 middleTip, Vector3 ringTip, Vector3 littleTip)
        {
            Handles.color = Color.white * 0.5f;
            
            // Determine if this is left or right hand based on hand position
            Animator animator = data.GetComponentInParent<Animator>();
            bool isLeftHand = false;
            if (animator != null)
            {
                Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                
                if (leftHand != null && rightHand != null)
                {
                    float leftDistance = Vector3.Distance(handPosition, leftHand.position);
                    float rightDistance = Vector3.Distance(handPosition, rightHand.position);
                    isLeftHand = leftDistance < rightDistance;
                }
            }
            
            // Always draw lines, using default positions if stored positions are zero
            DrawFingerLine(handPosition, thumbTip, "Thumb", isLeftHand);
            DrawFingerLine(handPosition, indexTip, "Index", isLeftHand);
            DrawFingerLine(handPosition, middleTip, "Middle", isLeftHand);
            DrawFingerLine(handPosition, ringTip, "Ring", isLeftHand);
            DrawFingerLine(handPosition, littleTip, "Little", isLeftHand);
        }
        
        /// <summary>
        /// Draw a line from hand to finger tip, using default position if tip is zero.
        /// </summary>
        private void DrawFingerLine(Vector3 handPosition, Vector3 fingerTip, string fingerName, bool isLeftHand)
        {
            Vector3 lineEnd = fingerTip;
            
            // If finger tip is zero, calculate default position
            if (fingerTip == Vector3.zero)
            {
                Animator animator = data.GetComponentInParent<Animator>();
                if (animator != null)
                {
                    Transform handTransform = animator.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                    if (handTransform != null)
                    {
                        // CalculateDefaultFingerPosition now returns local coordinates, convert to world
                        Vector3 localDefault = CalculateDefaultFingerPosition(handTransform, fingerName, isLeftHand);
                        lineEnd = LocalToWorldPosition(localDefault);
                    }
                }
            }
            
            Handles.DrawLine(handPosition, lineEnd);
        }

        private void DrawPreviewGizmos()
        {
            if (previewGripLeft != null)
            {
                foreach (var contact in previewGripLeft.contactPoints)
                {
                    Handles.color = new Color(0.212f, 0.749f, 0.694f);
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

            GUI.color = new Color(0.212f, 0.749f, 0.694f);
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

