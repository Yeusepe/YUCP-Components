using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.Utils;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Custom editor for Attach to Blendshape component.
    /// Provides preview visualization, blendshape list, and configuration UI
    /// </summary>
    [CustomEditor(typeof(AttachToBlendshapeData))]
    public class AttachToBlendshapeDataEditor : UnityEditor.Editor
    {
        private AttachToBlendshapeData data;
        
        // Foldout states
        private bool showTargetSettings = true;
        private bool showBlendshapeTracking = true;
        private bool showSurfaceCluster = false;
        private bool showSolverConfiguration = true;
        private bool showBoneAttachment = false;
        private bool showAnimationSettings = false;
        private bool showAdvancedOptions = false;
        private bool showBuildStats = false;
        
        private Vector2 blendshapeScrollPos;
        private Vector2 previewSliderScrollPos;
        private Mesh previewBakeMesh;

        // Cache to prevent recalculation spam
        private List<string> cachedDetectedVisemes;
        private int cachedVisemeFrameCount = -1;

        private void OnEnable()
        {
            data = (AttachToBlendshapeData)target;
            LoadFoldoutStates();
        }

        private void OnDisable()
        {
            SaveFoldoutStates();
            if (data != null)
            {
                RestorePreviewBlendshapes();
                RestorePreviewTransform();
            }
            ReleasePreviewBakeMesh();
        }

        private void LoadFoldoutStates()
        {
            int id = data.GetInstanceID();
            showTargetSettings = SessionState.GetBool($"AttachBlendshape_Target_{id}", true);
            showBlendshapeTracking = SessionState.GetBool($"AttachBlendshape_Tracking_{id}", true);
            showSurfaceCluster = SessionState.GetBool($"AttachBlendshape_Cluster_{id}", false);
            showSolverConfiguration = SessionState.GetBool($"AttachBlendshape_Solver_{id}", true);
            showBoneAttachment = SessionState.GetBool($"AttachBlendshape_Bone_{id}", false);
            showAnimationSettings = SessionState.GetBool($"AttachBlendshape_Anim_{id}", false);
            showAdvancedOptions = SessionState.GetBool($"AttachBlendshape_Advanced_{id}", false);
            showBuildStats = SessionState.GetBool($"AttachBlendshape_Stats_{id}", false);
        }

        private void SaveFoldoutStates()
        {
            int id = data.GetInstanceID();
            SessionState.SetBool($"AttachBlendshape_Target_{id}", showTargetSettings);
            SessionState.SetBool($"AttachBlendshape_Tracking_{id}", showBlendshapeTracking);
            SessionState.SetBool($"AttachBlendshape_Cluster_{id}", showSurfaceCluster);
            SessionState.SetBool($"AttachBlendshape_Solver_{id}", showSolverConfiguration);
            SessionState.SetBool($"AttachBlendshape_Bone_{id}", showBoneAttachment);
            SessionState.SetBool($"AttachBlendshape_Anim_{id}", showAnimationSettings);
            SessionState.SetBool($"AttachBlendshape_Advanced_{id}", showAdvancedOptions);
            SessionState.SetBool($"AttachBlendshape_Stats_{id}", showBuildStats);
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            data = (AttachToBlendshapeData)target;
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Attach to Blendshape"));

            // Validation banner (will be updated dynamically)
            var validationBanner = new VisualElement();
            validationBanner.name = "validation-banner";
            root.Add(validationBanner);
            
            YUCPUIToolkitHelper.AddSpacing(root, 5);
            
            // Target Mesh Configuration Foldout
            var targetFoldout = YUCPUIToolkitHelper.CreateFoldout("Target Mesh Configuration", showTargetSettings);
            targetFoldout.RegisterValueChangedCallback(evt => { showTargetSettings = evt.newValue; });
            
            targetFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("targetMesh"), "Target Mesh"));
            
            var targetMeshHelp = new VisualElement();
            targetMeshHelp.name = "target-mesh-help";
            targetFoldout.Add(targetMeshHelp);
            root.Add(targetFoldout);

            // Blendshape Tracking Foldout
            var trackingFoldout = YUCPUIToolkitHelper.CreateFoldout("Blendshape Tracking", showBlendshapeTracking);
            trackingFoldout.RegisterValueChangedCallback(evt => { showBlendshapeTracking = evt.newValue; });
            
            var trackingModeSelector = YUCPUIToolkitHelper.CreateTrackingModeSelector(
                data.trackingMode,
                (mode) => {
                    data.trackingMode = mode;
                    EditorUtility.SetDirty(data);
                }
            );
            trackingFoldout.Add(trackingModeSelector);
            
            YUCPUIToolkitHelper.AddSpacing(trackingFoldout, 8);
            
            var trackingModeContent = new VisualElement();
            trackingModeContent.name = "tracking-mode-content";
            trackingFoldout.Add(trackingModeContent);
            
            root.Add(trackingFoldout);
            
            // Surface Cluster Settings Foldout
            var clusterFoldout = YUCPUIToolkitHelper.CreateFoldout("Surface Cluster Settings", showSurfaceCluster);
            clusterFoldout.RegisterValueChangedCallback(evt => { showSurfaceCluster = evt.newValue; });
            clusterFoldout.Add(YUCPUIToolkitHelper.CreateHelpBox("Surface cluster uses multiple triangles for stable attachment during deformation.", YUCPUIToolkitHelper.MessageType.None));
            
            YUCPUIToolkitHelper.AddSpacing(clusterFoldout, 5);
            clusterFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("clusterTriangleCount"), "Triangle Count"));
            clusterFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("searchRadius"), "Search Radius"));
            
            YUCPUIToolkitHelper.AddSpacing(clusterFoldout, 3);
            clusterFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("manualTriangleIndex"), "Manual Triangle"));
            
            var manualTriangleHelp = new VisualElement();
            manualTriangleHelp.name = "manual-triangle-help";
            clusterFoldout.Add(manualTriangleHelp);
            root.Add(clusterFoldout);
            
            // Solver Configuration Foldout
            var solverFoldout = YUCPUIToolkitHelper.CreateFoldout("Solver Configuration", showSolverConfiguration);
            solverFoldout.RegisterValueChangedCallback(evt => { showSolverConfiguration = evt.newValue; });
            solverFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("solverMode"), "Solver Mode"));
            
            var solverModeCardContainer = new VisualElement();
            solverModeCardContainer.name = "solver-mode-card";
            solverFoldout.Add(solverModeCardContainer);
            
            YUCPUIToolkitHelper.AddSpacing(solverFoldout, 5);
            solverFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("alignRotationToSurface"), "Align to Surface"));
            
            var normalOffsetCard = YUCPUIToolkitHelper.CreateCard("Normal Offset Settings", "Configure offset distance");
            normalOffsetCard.name = "normal-offset-card";
            var normalOffsetContent = YUCPUIToolkitHelper.GetCardContent(normalOffsetCard);
            normalOffsetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("normalOffset"), "Offset Distance"));
            solverFoldout.Add(normalOffsetCard);
            
            var rbfCard = YUCPUIToolkitHelper.CreateCard("RBF Deformation Settings", "Configure RBF deformation parameters");
            rbfCard.name = "rbf-card";
            var rbfContent = YUCPUIToolkitHelper.GetCardContent(rbfCard);
            rbfContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rbfDriverPointCount"), "Driver Points"));
            rbfContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rbfRadiusMultiplier"), "Radius Multiplier"));
            rbfContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useGPUAcceleration"), "GPU Acceleration"));
            solverFoldout.Add(rbfCard);
            
            YUCPUIToolkitHelper.AddSpacing(solverFoldout, 5);
            solverFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rotationSmoothingFactor"), "Rotation Smoothing"));
            root.Add(solverFoldout);
            
            // Bone Attachment Foldout
            var boneFoldout = YUCPUIToolkitHelper.CreateFoldout("Bone Attachment (Base Positioning)", showBoneAttachment);
            boneFoldout.RegisterValueChangedCallback(evt => { showBoneAttachment = evt.newValue; });
            boneFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("attachToClosestBone"), "Enable Bone Attachment"));
            
            var boneSettingsCard = YUCPUIToolkitHelper.CreateCard("Bone Detection Settings", "Configure bone detection parameters");
            boneSettingsCard.name = "bone-settings-card";
            var boneSettingsContent = YUCPUIToolkitHelper.GetCardContent(boneSettingsCard);
            boneSettingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("boneSearchRadius"), "Search Radius"));
            boneSettingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("boneNameFilter"), "Name Filter"));
            boneSettingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("ignoreHumanoidBones"), "Ignore Humanoid Bones"));
            boneSettingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("boneOffset"), "Bone Offset Path"));
            boneFoldout.Add(boneSettingsCard);
            
            var boneHelp = new VisualElement();
            boneHelp.name = "bone-help";
            boneFoldout.Add(boneHelp);
            root.Add(boneFoldout);
            
            // Animation Generation Foldout
            var animationFoldout = YUCPUIToolkitHelper.CreateFoldout("Animation Generation", showAnimationSettings);
            animationFoldout.RegisterValueChangedCallback(evt => { showAnimationSettings = evt.newValue; });
            animationFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("createDirectAnimations"), "Create Animation Assets"));
            
            var directAnimHelp = new VisualElement();
            directAnimHelp.name = "direct-anim-help";
            animationFoldout.Add(directAnimHelp);
            
            var visemeFxLayerField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("autoCreateVisemeFxLayer"), "Auto Viseme FX Layer");
            visemeFxLayerField.name = "viseme-fx-layer";
            animationFoldout.Add(visemeFxLayerField);
            
            var visemeFxHelp = new VisualElement();
            visemeFxHelp.name = "viseme-fx-help";
            animationFoldout.Add(visemeFxHelp);
            
            YUCPUIToolkitHelper.AddSpacing(animationFoldout, 5);
            animationFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("samplesPerBlendshape"), "Samples Per Blendshape"));
            
            var samplesHelp = new VisualElement();
            samplesHelp.name = "samples-help";
            animationFoldout.Add(samplesHelp);
            root.Add(animationFoldout);
            
            // Advanced Options Foldout
            var advancedFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced Options", showAdvancedOptions);
            advancedFoldout.RegisterValueChangedCallback(evt => { showAdvancedOptions = evt.newValue; });
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugSaveAnimations"), "Save Animations"));
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugMode"), "Debug Logging"));
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("showPreview"), "Show Preview Gizmos"));
            root.Add(advancedFoldout);
            
            // Preview Tools Section
            YUCPUIToolkitHelper.AddSpacing(root, 15);
            root.Add(YUCPUIToolkitHelper.CreateDivider());
            YUCPUIToolkitHelper.AddSpacing(root, 5);
            
            var previewHeader = new Label("PREVIEW & DETECTION");
            previewHeader.style.fontSize = 13;
            previewHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            previewHeader.style.color = new StyleColor(new Color(0.212f, 0.749f, 0.694f));
            previewHeader.style.marginBottom = 8;
            root.Add(previewHeader);
            
            var previewToolsContainer = new VisualElement();
            previewToolsContainer.name = "preview-tools-container";
            root.Add(previewToolsContainer);
            
            // Build Statistics Foldout
            var buildStatsFoldout = YUCPUIToolkitHelper.CreateFoldout("Build Statistics", showBuildStats);
            buildStatsFoldout.RegisterValueChangedCallback(evt => { showBuildStats = evt.newValue; });
            buildStatsFoldout.name = "build-stats-foldout";
            
            var buildStatsCard = YUCPUIToolkitHelper.CreateCard("Last Build Results", null);
            var buildStatsContent = YUCPUIToolkitHelper.GetCardContent(buildStatsCard);
            buildStatsContent.name = "build-stats-content";
            buildStatsFoldout.Add(buildStatsCard);
            root.Add(buildStatsFoldout);
            
            // Dynamic updates
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                validationBanner.Clear();
                if (!ValidateData())
                {
                    validationBanner.Add(YUCPUIToolkitHelper.CreateHelpBox($"Configuration Error\n\n{GetValidationError()}", YUCPUIToolkitHelper.MessageType.Error));
                }
                
                targetMeshHelp.Clear();
                if (data.targetMesh != null)
                {
                    if (!PoseSampler.HasBlendshapes(data.targetMesh))
                    {
                        targetMeshHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("Target mesh has no blendshapes!", YUCPUIToolkitHelper.MessageType.Error));
                    }
                    else
                    {
                        int blendshapeCount = data.targetMesh.sharedMesh.blendShapeCount;
                        targetMeshHelp.Add(YUCPUIToolkitHelper.CreateHelpBox($"Found {blendshapeCount} blendshape{(blendshapeCount != 1 ? "s" : "")} on target mesh", YUCPUIToolkitHelper.MessageType.Info));
                    }
                }
                else
                {
                    targetMeshHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("Select a SkinnedMeshRenderer with blendshapes", YUCPUIToolkitHelper.MessageType.None));
                }
                
                manualTriangleHelp.Clear();
                if (data.manualTriangleIndex >= 0)
                {
                    manualTriangleHelp.Add(YUCPUIToolkitHelper.CreateHelpBox($"Manual mode: Using triangle #{data.manualTriangleIndex} as primary anchor", YUCPUIToolkitHelper.MessageType.Info));
                }
                
                // Update solver mode card
                var solverCardContainer = root.Q<VisualElement>("solver-mode-card");
                if (solverCardContainer != null && showSolverConfiguration)
                {
                    solverCardContainer.Clear();
                    var solverCard = YUCPUIToolkitHelper.CreateSolverModeCard(data.solverMode);
                    solverCardContainer.Add(solverCard);
                }
                
                normalOffsetCard.style.display = (data.solverMode == SolverMode.RigidNormalOffset) ? DisplayStyle.Flex : DisplayStyle.None;
                rbfCard.style.display = (data.solverMode == SolverMode.CageRBF) ? DisplayStyle.Flex : DisplayStyle.None;
                
                boneSettingsCard.style.display = data.attachToClosestBone ? DisplayStyle.Flex : DisplayStyle.None;
                boneHelp.Clear();
                if (data.attachToClosestBone)
                {
                    boneHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("Bone attachment provides base positioning.\nBlendshape animations are applied relative to the bone.", YUCPUIToolkitHelper.MessageType.None));
                }
                else
                {
                    boneHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("Bone attachment disabled - object will stay in place without base bone link.", YUCPUIToolkitHelper.MessageType.Warning));
                }
                
                directAnimHelp.Clear();
                if (data.createDirectAnimations)
                {
                    directAnimHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("Animations will be saved to Assets/Generated/AttachToBlendshape/\n\n" +
                        "You'll need to manually wire these to your FX layer or use VRCFury's Direct Tree Controller.", YUCPUIToolkitHelper.MessageType.Info));
                }
                
                visemeFxLayerField.style.display = (data.trackingMode == BlendshapeTrackingMode.VisemsOnly) ? DisplayStyle.Flex : DisplayStyle.None;
                visemeFxHelp.Clear();
                if (data.trackingMode == BlendshapeTrackingMode.VisemsOnly && data.autoCreateVisemeFxLayer)
                {
                    visemeFxHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("When enabled, a controller asset is created in Assets/Generated/AttachToBlendshape/Controllers\n" +
                        "and automatically hooked up through VRCFury so the attachment follows live visemes.", YUCPUIToolkitHelper.MessageType.None));
                }
                
                samplesHelp.Clear();
                int samples = data.samplesPerBlendshape;
                int estimatedKeyframes = samples * 7;
                samplesHelp.Add(YUCPUIToolkitHelper.CreateHelpBox($"{samples} samples = ~{estimatedKeyframes} keyframes per blendshape\n" +
                    $"More samples = smoother animation but larger file size", YUCPUIToolkitHelper.MessageType.None));
                
                // Update tracking mode content
                UpdateTrackingModeContent(root.Q<VisualElement>("tracking-mode-content"));
                
                // Update preview tools
                UpdatePreviewTools(root.Q<VisualElement>("preview-tools-container"));
                
                UpdateBuildStatistics(buildStatsFoldout, buildStatsContent);
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateBuildStatistics(Foldout foldout, VisualElement content)
        {
            if (data.TrackedBlendshapes == null || data.TrackedBlendshapes.Count == 0)
            {
                foldout.style.display = DisplayStyle.None;
                return;
            }
            
            foldout.style.display = DisplayStyle.Flex;
            content.Clear();
            
            var trackedLabel = new Label($"Tracked Blendshapes: {data.TrackedBlendshapes.Count}");
            trackedLabel.SetEnabled(false);
            trackedLabel.style.fontSize = 11;
            trackedLabel.style.marginBottom = 2;
            content.Add(trackedLabel);
            
            var animationsLabel = new Label($"Generated Animations: {data.GeneratedAnimationCount}");
            animationsLabel.SetEnabled(false);
            animationsLabel.style.fontSize = 11;
            animationsLabel.style.marginBottom = 2;
            content.Add(animationsLabel);
            
            var boneLabel = new Label($"Selected Bone: {(string.IsNullOrEmpty(data.SelectedBonePath) ? "None" : data.SelectedBonePath)}");
            boneLabel.SetEnabled(false);
            boneLabel.style.fontSize = 11;
            boneLabel.style.marginBottom = 2;
            content.Add(boneLabel);
            
            if (data.DetectedCluster != null)
            {
                var clusterTriLabel = new Label($"Cluster Triangles: {data.DetectedCluster.anchors.Count}");
                clusterTriLabel.SetEnabled(false);
                clusterTriLabel.style.fontSize = 11;
                clusterTriLabel.style.marginBottom = 2;
                content.Add(clusterTriLabel);
                
                var clusterCenterLabel = new Label($"Cluster Center: {data.DetectedCluster.centerPosition.ToString("F3")}");
                clusterCenterLabel.SetEnabled(false);
                clusterCenterLabel.style.fontSize = 11;
                content.Add(clusterCenterLabel);
            }
        }

        private void UpdateTrackingModeContent(VisualElement container)
        {
            if (container == null || !showBlendshapeTracking) return;
            
            container.Clear();
            
            switch (data.trackingMode)
            {
                case BlendshapeTrackingMode.All:
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox("All blendshapes on the target mesh will be tracked.\n\nGood for: Facial tracking", YUCPUIToolkitHelper.MessageType.Info));
                    break;
                case BlendshapeTrackingMode.Specific:
                    var blendshapeEditor = YUCPUIToolkitHelper.CreateBlendshapeListEditor(
                        data.targetMesh?.sharedMesh,
                        data.specificBlendshapes,
                        (list) => {
                            data.specificBlendshapes = list;
                            EditorUtility.SetDirty(data);
                        }
                    );
                    container.Add(blendshapeEditor);
                    break;
                case BlendshapeTrackingMode.VisemsOnly:
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox("Only VRChat viseme blendshapes will be tracked.\n\nGood for: Lip piercings, mouth jewelry", YUCPUIToolkitHelper.MessageType.Info));
                    UpdateDetectedVisemes(container);
                    break;
                case BlendshapeTrackingMode.Smart:
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox("Automatically detects blendshapes that move this attachment.\n\nGood for: Optimal performance, localized areas", YUCPUIToolkitHelper.MessageType.Info));
                    YUCPUIToolkitHelper.AddSpacing(container, 3);
                    container.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("smartDetectionThreshold"), "Detection Threshold"));
                    break;
            }
        }
        
        private void UpdateDetectedVisemes(VisualElement container)
        {
            if (data.targetMesh == null || data.targetMesh.sharedMesh == null) return;
            
            int currentFrame = Time.frameCount;
            List<string> detectedVisemes;
            
            if (cachedDetectedVisemes != null && cachedVisemeFrameCount == currentFrame)
            {
                detectedVisemes = cachedDetectedVisemes;
            }
            else
            {
                GameObject avatarRoot = null;
                var avatarDescriptor = data.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                if (avatarDescriptor != null)
                {
                    avatarRoot = avatarDescriptor.gameObject;
                }
                
                detectedVisemes = VRChatVisemeDetector.GetVisemeBlendshapes(data.targetMesh, avatarRoot);
                cachedDetectedVisemes = detectedVisemes;
                cachedVisemeFrameCount = currentFrame;
            }
            
            YUCPUIToolkitHelper.AddSpacing(container, 5);
            
            if (detectedVisemes.Count > 0)
            {
                var card = YUCPUIToolkitHelper.CreateCard($"Detected Visemes ({detectedVisemes.Count})", null);
                var cardContent = YUCPUIToolkitHelper.GetCardContent(card);
                
                int maxDisplay = 15;
                int displayed = Mathf.Min(detectedVisemes.Count, maxDisplay);
                
                var scrollView = new ScrollView();
                scrollView.style.maxHeight = 150;
                
                for (int i = 0; i < displayed; i++)
                {
                    var label = new Label($"  - {detectedVisemes[i]}");
                    label.style.fontSize = 10;
                    scrollView.Add(label);
                }
                
                if (detectedVisemes.Count > maxDisplay)
                {
                    var moreLabel = new Label($"  ... and {detectedVisemes.Count - maxDisplay} more");
                    moreLabel.style.fontSize = 10;
                    scrollView.Add(moreLabel);
                }
                
                cardContent.Add(scrollView);
                container.Add(card);
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("No viseme blendshapes detected.\nCheck Avatar Descriptor or ensure blendshapes use standard VRChat naming.", YUCPUIToolkitHelper.MessageType.Warning));
            }
        }
        
        private YUCPPreviewTools previewToolsInstance;
        
        private void UpdatePreviewTools(VisualElement container)
        {
            if (container == null) return;
            
            if (previewToolsInstance == null)
            {
                var previewData = new YUCPPreviewTools.PreviewData
                {
                    previewGenerated = data.previewGenerated,
                    clusterTriangleCount = data.previewCluster?.anchors.Count ?? 0,
                    clusterCenter = data.previewCluster?.centerPosition ?? Vector3.zero,
                    blendshapes = data.previewBlendshapes,
                    blendshapeWeights = data.previewBlendshapeWeights,
                    originalWeights = data.previewOriginalWeights
                };
                
                previewToolsInstance = YUCPUIToolkitHelper.CreatePreviewTools(
                    previewData,
                    () => ValidateData(),
                    () => GeneratePreview(),
                    () => ClearPreview(),
                    (name) => GetCurrentBlendshapeWeight(name),
                    (name, value) => ApplyPreviewBlendshapeWeight(name, value),
                    () => RestorePreviewBlendshapes(),
                    () => ZeroPreviewBlendshapes()
                );
                
                container.Add(previewToolsInstance);
            }
            
            // Update preview data
            var updatedPreviewData = new YUCPPreviewTools.PreviewData
            {
                previewGenerated = data.previewGenerated,
                clusterTriangleCount = data.previewCluster?.anchors.Count ?? 0,
                clusterCenter = data.previewCluster?.centerPosition ?? Vector3.zero,
                blendshapes = data.previewBlendshapes,
                blendshapeWeights = data.previewBlendshapeWeights,
                originalWeights = data.previewOriginalWeights
            };
            
            previewToolsInstance.Data = updatedPreviewData;
            previewToolsInstance.RefreshSliders();
        }

        public override void OnInspectorGUI()
        {
            // Legacy support - not used anymore
        }

        private void EnsurePreviewBlendshapeCaches()
        {
            if (data.previewBlendshapeWeights == null)
            {
                data.previewBlendshapeWeights = new Dictionary<string, float>();
            }

            if (data.previewOriginalWeights == null)
            {
                data.previewOriginalWeights = new Dictionary<string, float>();
            }

            foreach (string blendshape in data.previewBlendshapes)
            {
                if (!data.previewBlendshapeWeights.ContainsKey(blendshape))
                {
                    float current = GetCurrentBlendshapeWeight(blendshape);
                    data.previewBlendshapeWeights[blendshape] = current;
                }

                if (!data.previewOriginalWeights.ContainsKey(blendshape))
                {
                    data.previewOriginalWeights[blendshape] = GetCurrentBlendshapeWeight(blendshape);
                }
            }
        }

        private float GetCurrentBlendshapeWeight(string name)
        {
            if (data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                return 0f;
            }

            int index = data.targetMesh.sharedMesh.GetBlendShapeIndex(name);
            if (index < 0)
            {
                return 0f;
            }

            return data.targetMesh.GetBlendShapeWeight(index);
        }

        private void ApplyPreviewBlendshapeWeight(string name, float value)
        {
            if (data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                return;
            }

            int index = data.targetMesh.sharedMesh.GetBlendShapeIndex(name);
            if (index < 0)
            {
                return;
            }

            data.targetMesh.SetBlendShapeWeight(index, value);
            data.previewBlendshapeWeights[name] = value;
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            UpdatePreviewAttachmentPose();
        }

        private void RestorePreviewBlendshapes()
        {
            if (data.previewOriginalWeights == null || data.previewOriginalWeights.Count == 0)
            {
                return;
            }

            foreach (var kvp in data.previewOriginalWeights)
            {
                ApplyPreviewBlendshapeWeight(kvp.Key, kvp.Value);
            }

            data.previewBlendshapeWeights?.Clear();
            UpdatePreviewAttachmentPose();
        }

        private void ZeroPreviewBlendshapes()
        {
            EnsurePreviewBlendshapeCaches();
            foreach (string blendshape in data.previewBlendshapes)
            {
                ApplyPreviewBlendshapeWeight(blendshape, 0f);
            }
        }

        private void CapturePreviewBlendshapeWeights()
        {
            if (data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                return;
            }

            data.previewBlendshapeWeights = new Dictionary<string, float>();
            data.previewOriginalWeights = new Dictionary<string, float>();

            foreach (string blendshape in data.previewBlendshapes)
            {
                int index = data.targetMesh.sharedMesh.GetBlendShapeIndex(blendshape);
                if (index < 0) continue;

                float current = data.targetMesh.GetBlendShapeWeight(index);
                data.previewBlendshapeWeights[blendshape] = current;
                if (!data.previewOriginalWeights.ContainsKey(blendshape))
                {
                    data.previewOriginalWeights[blendshape] = current;
                }
            }
        }

        private void CapturePreviewBasePose()
        {
            if (data.previewCluster == null || data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                return;
            }

            if (!data.previewOriginalTransformCaptured)
            {
                data.previewOriginalLocalPosition = data.transform.localPosition;
                data.previewOriginalLocalRotation = data.transform.localRotation;
                data.previewOriginalTransformCaptured = true;
            }

            var originalWeights = SaveAllBlendshapeWeights();
            var bakeMesh = GetPreviewBakeMesh();
            data.targetMesh.BakeMesh(bakeMesh);

            SurfaceClusterDetector.EvaluateCluster(
                data.previewCluster,
                bakeMesh.vertices,
                bakeMesh.triangles,
                out data.previewBasePosition,
                out data.previewBaseNormal,
                out data.previewBaseTangent);

            data.previewBaseCaptured = true;
            data.previewHasLastTangent = false;

            RestoreAllBlendshapeWeights(originalWeights);
        }

        private float[] SaveAllBlendshapeWeights()
        {
            if (data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                return null;
            }

            Mesh mesh = data.targetMesh.sharedMesh;
            int count = mesh.blendShapeCount;
            float[] weights = new float[count];

            for (int i = 0; i < count; i++)
            {
                weights[i] = data.targetMesh.GetBlendShapeWeight(i);
                data.targetMesh.SetBlendShapeWeight(i, 0f);
            }

            return weights;
        }

        private void RestoreAllBlendshapeWeights(float[] weights)
        {
            if (weights == null || data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                return;
            }

            int count = Mathf.Min(weights.Length, data.targetMesh.sharedMesh.blendShapeCount);
            for (int i = 0; i < count; i++)
            {
                data.targetMesh.SetBlendShapeWeight(i, weights[i]);
            }
        }

        private void UpdatePreviewAttachmentPose()
        {
            if (data == null ||
                !data.previewGenerated ||
                data.previewCluster == null ||
                data.targetMesh == null ||
                data.targetMesh.sharedMesh == null)
            {
                return;
            }

            if (!data.previewBaseCaptured)
            {
                CapturePreviewBasePose();
                if (!data.previewBaseCaptured)
                {
                    return;
                }
            }

            var bakeMesh = GetPreviewBakeMesh();
            data.targetMesh.BakeMesh(bakeMesh);

            SurfaceClusterDetector.EvaluateCluster(
                data.previewCluster,
                bakeMesh.vertices,
                bakeMesh.triangles,
                out Vector3 clusterPosition,
                out Vector3 clusterNormal,
                out Vector3 clusterTangent);

            Vector3? previousTangent = data.previewHasLastTangent ? data.previewLastTangent : (Vector3?)null;
            Vector3 savedLocalPos = data.transform.localPosition;
            Quaternion savedLocalRot = data.transform.localRotation;

            if (data.previewOriginalTransformCaptured)
            {
                data.transform.localPosition = data.previewOriginalLocalPosition;
                data.transform.localRotation = data.previewOriginalLocalRotation;
            }

            var result = SolveClusterPose(clusterPosition, clusterNormal, clusterTangent, previousTangent);

            data.transform.localPosition = savedLocalPos;
            data.transform.localRotation = savedLocalRot;

            if (result.success)
            {
                Vector3 finalPos;
                Quaternion finalRot;

                if (data.previewHasBaseSolver)
                {
                    finalPos = result.position + data.previewPositionOffset;
                    if (data.alignRotationToSurface)
                    {
                        finalRot = result.rotation * data.previewRotationOffset;
                    }
                    else
                    {
                        finalRot = data.previewOriginalLocalRotation;
                    }
                }
                else
                {
                    finalPos = data.previewOriginalLocalPosition;
                    finalRot = data.previewOriginalLocalRotation;
                }

                data.transform.localPosition = finalPos;
                data.transform.localRotation = finalRot;
            }
            else if (data.previewOriginalTransformCaptured)
            {
                data.transform.localPosition = data.previewOriginalLocalPosition;
                data.transform.localRotation = data.previewOriginalLocalRotation;
            }

            data.previewLastTangent = clusterTangent;
            data.previewHasLastTangent = true;
        }

        private BlendshapeSolver.SolverResult SolveClusterPose(
            Vector3 clusterPosition,
            Vector3 clusterNormal,
            Vector3 clusterTangent,
            Vector3? previousTangent)
        {
            switch (data.solverMode)
            {
                case SolverMode.Rigid:
                    return BlendshapeSolver.SolveRigid(
                        clusterPosition,
                        clusterNormal,
                        clusterTangent,
                        data.transform,
                        data.targetMesh.transform,
                        data.alignRotationToSurface,
                        previousTangent,
                        data.rotationSmoothingFactor);

                case SolverMode.RigidNormalOffset:
                    return BlendshapeSolver.SolveRigidNormalOffset(
                        clusterPosition,
                        clusterNormal,
                        clusterTangent,
                        data.transform,
                        data.targetMesh.transform,
                        data.alignRotationToSurface,
                        data.normalOffset,
                        previousTangent,
                        data.rotationSmoothingFactor);

                case SolverMode.Affine:
                    return BlendshapeSolver.SolveAffine(
                        clusterPosition,
                        clusterNormal,
                        clusterTangent,
                        data.transform,
                        data.targetMesh.transform,
                        data.previewBasePosition,
                        data.previewBaseNormal,
                        data.previewBaseTangent,
                        data.alignRotationToSurface,
                        previousTangent,
                        data.rotationSmoothingFactor);

                case SolverMode.CageRBF:
                    return BlendshapeSolver.SolveRigid(
                        clusterPosition,
                        clusterNormal,
                        clusterTangent,
                        data.transform,
                        data.targetMesh.transform,
                        data.alignRotationToSurface,
                        previousTangent,
                        data.rotationSmoothingFactor);

                default:
                    return BlendshapeSolver.SolveRigid(
                        clusterPosition,
                        clusterNormal,
                        clusterTangent,
                        data.transform,
                        data.targetMesh.transform,
                        data.alignRotationToSurface,
                        previousTangent,
                        data.rotationSmoothingFactor);
            }
        }

        private void RestorePreviewTransform()
        {
            if (data == null)
            {
                return;
            }

            if (data.previewOriginalTransformCaptured)
            {
                data.transform.localPosition = data.previewOriginalLocalPosition;
                data.transform.localRotation = data.previewOriginalLocalRotation;
            }

            data.previewOriginalTransformCaptured = false;
            data.previewBaseCaptured = false;
            data.previewHasLastTangent = false;
        }

        private Mesh GetPreviewBakeMesh()
        {
            if (previewBakeMesh == null)
            {
                previewBakeMesh = new Mesh { name = "AttachToBlendshapePreviewBake" };
            }
            return previewBakeMesh;
        }

        private void ReleasePreviewBakeMesh()
        {
            if (previewBakeMesh == null) return;

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(previewBakeMesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(previewBakeMesh);
            }

            previewBakeMesh = null;
        }


        private void ShowBlendshapeSelectionMenu(Mesh mesh)
        {
            GenericMenu menu = new GenericMenu();

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendshapeName = mesh.GetBlendShapeName(i);
                bool alreadyAdded = data.specificBlendshapes.Contains(blendshapeName);

                if (alreadyAdded)
                {
                    menu.AddDisabledItem(new GUIContent($"{blendshapeName} (already added)"));
                }
                else
                {
                    menu.AddItem(new GUIContent(blendshapeName), false, () => {
                        data.specificBlendshapes.Add(blendshapeName);
                        EditorUtility.SetDirty(data);
                    });
                }
            }

            menu.ShowAsContext();
        }



        private void GeneratePreview()
        {
            Debug.Log("[AttachToBlendshape Preview] Generating preview...");

            try
            {
                if (!ValidateData())
                {
                    EditorUtility.DisplayDialog("Validation Failed", GetValidationError(), "OK");
                    return;
                }

                // Detect cluster
                data.previewCluster = SurfaceClusterDetector.DetectCluster(
                    data.targetMesh,
                    data.transform.position,
                    data.clusterTriangleCount,
                    data.searchRadius,
                    data.manualTriangleIndex);

                if (data.previewCluster == null)
                {
                    EditorUtility.DisplayDialog("Preview Failed", "Failed to detect surface cluster.", "OK");
                    return;
                }

                // Determine blendshapes based on mode
                GameObject avatarRoot = null;
                var avatarDescriptor = data.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                if (avatarDescriptor != null)
                {
                    avatarRoot = avatarDescriptor.gameObject;
                }

                switch (data.trackingMode)
                {
                    case BlendshapeTrackingMode.All:
                        data.previewBlendshapes = PoseSampler.GetAllBlendshapeNames(data.targetMesh.sharedMesh);
                        break;

                    case BlendshapeTrackingMode.Specific:
                        data.previewBlendshapes = new List<string>(data.specificBlendshapes)
                            .Where(name => data.targetMesh.sharedMesh.GetBlendShapeIndex(name) >= 0)
                            .ToList();
                        break;

                    case BlendshapeTrackingMode.VisemsOnly:
                        data.previewBlendshapes = VRChatVisemeDetector.GetVisemeBlendshapes(data.targetMesh, avatarRoot);
                        break;

                    case BlendshapeTrackingMode.Smart:
                        EditorUtility.DisplayProgressBar("Smart Detection", "Analyzing blendshapes...", 0.5f);
                        data.previewBlendshapes = VRChatVisemeDetector.DetectActiveBlendshapes(
                            data.targetMesh,
                            data.previewCluster,
                            data.smartDetectionThreshold);
                        EditorUtility.ClearProgressBar();
                        break;
                }

                CapturePreviewBlendshapeWeights();

                data.previewGenerated = true;
                data.showPreview = true;
                CapturePreviewBasePose();
                UpdatePreviewAttachmentPose();

                SceneView.RepaintAll();
                Repaint();

                Debug.Log($"[AttachToBlendshape Preview] Preview generated: {data.previewBlendshapes.Count} blendshapes, {data.previewCluster.anchors.Count} triangles");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[AttachToBlendshape Preview] Failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }

        private void ClearPreview()
        {
            RestorePreviewBlendshapes();
            RestorePreviewTransform();
            data.previewCluster = null;
            data.previewBlendshapes.Clear();
            data.previewBlendshapeWeights?.Clear();
            data.previewOriginalWeights?.Clear();
            data.previewBaseCaptured = false;
            data.previewOriginalTransformCaptured = false;
            data.previewHasLastTangent = false;
            data.previewHasBaseSolver = false;
            data.previewPositionOffset = Vector3.zero;
            data.previewRotationOffset = Quaternion.identity;
            data.previewGenerated = false;
            data.showPreview = false;

            SceneView.RepaintAll();
            Repaint();

            Debug.Log("[AttachToBlendshape Preview] Preview cleared");
        }

        private bool ValidateData()
        {
            if (data.targetMesh == null) return false;
            if (data.targetMesh.sharedMesh == null) return false;
            if (!PoseSampler.HasBlendshapes(data.targetMesh)) return false;
            if (data.trackingMode == BlendshapeTrackingMode.Specific && 
                (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0)) return false;
            return true;
        }

        private string GetValidationError()
        {
            if (data.targetMesh == null) return "Target mesh is not set.";
            if (data.targetMesh.sharedMesh == null) return "Target mesh has no mesh data.";
            if (!PoseSampler.HasBlendshapes(data.targetMesh)) return "Target mesh has no blendshapes.";
            if (data.trackingMode == BlendshapeTrackingMode.Specific && 
                (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0))
                return "Specific mode requires at least one blendshape name.";
            return "";
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void DrawGizmos(AttachToBlendshapeData data, GizmoType gizmoType)
        {
            if (!data.showPreview || !data.previewGenerated) return;
            if (data.previewCluster == null || data.targetMesh == null) return;

            Mesh mesh = data.targetMesh.sharedMesh;
            if (mesh == null) return;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            // Draw cluster triangles
            Handles.color = new Color(0f, 1f, 0f, 0.3f);

            foreach (var anchor in data.previewCluster.anchors)
            {
                int idx0 = triangles[anchor.triIndex * 3];
                int idx1 = triangles[anchor.triIndex * 3 + 1];
                int idx2 = triangles[anchor.triIndex * 3 + 2];

                Vector3 v0 = data.targetMesh.transform.TransformPoint(vertices[idx0]);
                Vector3 v1 = data.targetMesh.transform.TransformPoint(vertices[idx1]);
                Vector3 v2 = data.targetMesh.transform.TransformPoint(vertices[idx2]);

                Handles.DrawAAConvexPolygon(v0, v1, v2);
            }

            // Draw cluster center and normal
            Vector3 worldCenter = data.targetMesh.transform.TransformPoint(data.previewCluster.centerPosition);
            Vector3 worldNormal = data.targetMesh.transform.TransformDirection(data.previewCluster.averageNormal);

            Handles.color = Color.yellow;
            Handles.SphereHandleCap(0, worldCenter, Quaternion.identity, 0.01f, EventType.Repaint);
            
            Handles.color = new Color(0.212f, 0.749f, 0.694f);
            Handles.DrawLine(worldCenter, worldCenter + worldNormal * 0.05f);

            // Draw info overlay
            Handles.BeginGUI();

            GUI.Box(new Rect(10, 10, 300, 120), "");
            GUI.Label(new Rect(15, 15, 290, 20), "Attach to Blendshape Preview", EditorStyles.boldLabel);

            GUI.color = new Color(0f, 1f, 0f, 0.3f);
            GUI.Box(new Rect(15, 40, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 38, 250, 20), $"= Surface cluster ({data.previewCluster.anchors.Count} triangles)");

            GUI.color = Color.yellow;
            GUI.Box(new Rect(15, 60, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 58, 250, 20), "= Cluster center");

            GUI.color = new Color(0.212f, 0.749f, 0.694f);
            GUI.Box(new Rect(15, 80, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 78, 250, 20), "= Surface normal");

            GUI.Label(new Rect(15, 100, 280, 20), $"Tracking {data.previewBlendshapes.Count} blendshapes");

            Handles.EndGUI();
        }
    }
}
