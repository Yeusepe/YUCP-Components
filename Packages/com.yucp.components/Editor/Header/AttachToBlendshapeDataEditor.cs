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
using YUCP.Components.Editor;
using static YUCP.Components.Editor.MeshUtils.BlendshapeTransfer;

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
        
        // Track previous values to prevent unnecessary UI updates
        private SkinnedMeshRenderer previousTargetMesh = null;
        private int previousManualTriangleIndex = -2;
        private SolverMode previousSolverMode = (SolverMode)(-1);
        private bool previousAttachToClosestBone = false;
        private BlendshapeTrackingMode previousTrackingMode = (BlendshapeTrackingMode)(-1);
        private int previousSamplesPerBlendshape = -1;
        private string previousValidationError = null;
        private int previousTrackedBlendshapesCount = -1;
        private int previousGeneratedAnimationCount = -1;
        private string previousSelectedBonePath = null;
        private bool previousPreviewGenerated = false;
        private int previousPreviewBlendshapeCount = -1;

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
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Attach to Blendshape"));

            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(AttachToBlendshapeData));
            if (betaWarning != null) root.Add(betaWarning);

            // Validation banner (will be updated dynamically)
            var validationBanner = new VisualElement();
            validationBanner.name = "validation-banner";
            root.Add(validationBanner);
            
            // Source Mesh Card
            var sourceCard = YUCPUIToolkitHelper.CreateCard("Source Mesh", "The mesh with blendshapes to attach to (usually avatar head/body)");
            var sourceContent = YUCPUIToolkitHelper.GetCardContent(sourceCard);
            sourceContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("targetMesh"), "Source Mesh"));
            
            var sourceMeshHelp = new VisualElement();
            sourceMeshHelp.name = "target-mesh-help";
            sourceContent.Add(sourceMeshHelp);
            root.Add(sourceCard);

            // Target Mesh Card
            var targetCard = YUCPUIToolkitHelper.CreateCard("Target Mesh", "The mesh that will receive transferred blendshapes (e.g., piercing, accessory)");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            
            var targetMeshField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("targetMeshToModify"), "Target Mesh");
            targetContent.Add(targetMeshField);
            
            var targetMeshHelp = new VisualElement();
            targetMeshHelp.name = "target-mesh-to-modify-help";
            targetContent.Add(targetMeshHelp);
            root.Add(targetCard);

            // Blendshape Tracking Card
            var trackingCard = YUCPUIToolkitHelper.CreateCard("Blendshape Tracking", "Configure which blendshapes to transfer");
            var trackingContent = YUCPUIToolkitHelper.GetCardContent(trackingCard);
            
            var trackingModeSelector = YUCPUIToolkitHelper.CreateTrackingModeSelector(
                data.trackingMode,
                (mode) => {
                    data.trackingMode = mode;
                    EditorUtility.SetDirty(data);
                }
            );
            trackingContent.Add(trackingModeSelector);
            
            YUCPUIToolkitHelper.AddSpacing(trackingContent, 8);
            
            var trackingModeContent = new VisualElement();
            trackingModeContent.name = "tracking-mode-content";
            trackingContent.Add(trackingModeContent);
            
            root.Add(trackingCard);
            
            // Surface Attachment Card
            var clusterCard = YUCPUIToolkitHelper.CreateCard("Surface Attachment", "Configure attachment point on source mesh");
            var clusterContent = YUCPUIToolkitHelper.GetCardContent(clusterCard);
            clusterContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Surface cluster uses multiple triangles for stable attachment during deformation.", YUCPUIToolkitHelper.MessageType.None));
            
            YUCPUIToolkitHelper.AddSpacing(clusterContent, 5);
            clusterContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("clusterTriangleCount"), "Triangle Count"));
            clusterContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("searchRadius"), "Search Radius"));
            
            YUCPUIToolkitHelper.AddSpacing(clusterContent, 3);
            clusterContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("manualTriangleIndex"), "Manual Triangle"));
            
            var manualTriangleHelp = new VisualElement();
            manualTriangleHelp.name = "manual-triangle-help";
            clusterContent.Add(manualTriangleHelp);
            root.Add(clusterCard);
            
            // Solver Configuration Card
            var solverCard = YUCPUIToolkitHelper.CreateCard("Solver Configuration", "How to calculate vertex deformation");
            var solverContent = YUCPUIToolkitHelper.GetCardContent(solverCard);
            solverContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("solverMode"), "Solver Mode"));
            
            var solverModeCardContainer = new VisualElement();
            solverModeCardContainer.name = "solver-mode-card";
            solverContent.Add(solverModeCardContainer);
            
            YUCPUIToolkitHelper.AddSpacing(solverContent, 5);
            solverContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("alignRotationToSurface"), "Align to Surface"));
            
            var normalOffsetCard = YUCPUIToolkitHelper.CreateCard("Normal Offset Settings", "Configure offset distance");
            normalOffsetCard.name = "normal-offset-card";
            var normalOffsetContent = YUCPUIToolkitHelper.GetCardContent(normalOffsetCard);
            normalOffsetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("normalOffset"), "Offset Distance"));
            solverContent.Add(normalOffsetCard);
            
            var rbfCard = YUCPUIToolkitHelper.CreateCard("RBF Deformation Settings", "Configure RBF deformation parameters");
            rbfCard.name = "rbf-card";
            var rbfContent = YUCPUIToolkitHelper.GetCardContent(rbfCard);
            rbfContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rbfDriverPointCount"), "Driver Points"));
            rbfContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rbfRadiusMultiplier"), "Radius Multiplier"));
            rbfContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useGPUAcceleration"), "GPU Acceleration"));
            solverContent.Add(rbfCard);
            
            YUCPUIToolkitHelper.AddSpacing(solverContent, 5);
            solverContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rotationSmoothingFactor"), "Rotation Smoothing"));
            root.Add(solverCard);
            
            // Bone Attachment Card
            var boneCard = YUCPUIToolkitHelper.CreateCard("Bone Attachment", "Optional base positioning relative to bone");
            var boneContent = YUCPUIToolkitHelper.GetCardContent(boneCard);
            boneContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("attachToClosestBone"), "Enable Bone Attachment"));
            
            var boneSettingsCard = YUCPUIToolkitHelper.CreateCard("Bone Detection Settings", "Configure bone detection parameters");
            boneSettingsCard.name = "bone-settings-card";
            var boneSettingsContent = YUCPUIToolkitHelper.GetCardContent(boneSettingsCard);
            boneSettingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("boneSearchRadius"), "Search Radius"));
            boneSettingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("boneNameFilter"), "Name Filter"));
            boneSettingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("ignoreHumanoidBones"), "Ignore Humanoid Bones"));
            boneSettingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("boneOffset"), "Bone Offset Path"));
            boneContent.Add(boneSettingsCard);
            
            var boneHelp = new VisualElement();
            boneHelp.name = "bone-help";
            boneContent.Add(boneHelp);
            root.Add(boneCard);
            
            // Blendshape Transfer Settings Card
            var transferCard = YUCPUIToolkitHelper.CreateCard("Blendshape Transfer", "Configure blendshape transfer settings");
            var transferContent = YUCPUIToolkitHelper.GetCardContent(transferCard);
            
            transferContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("samplesPerBlendshape"), "Keyframes Per Blendshape"));
            
            var samplesHelp = new VisualElement();
            samplesHelp.name = "samples-help";
            transferContent.Add(samplesHelp);
            root.Add(transferCard);
            
            // Advanced Options Card
            var advancedCard = YUCPUIToolkitHelper.CreateCard("Advanced Options", "Debug and preview settings");
            var advancedContent = YUCPUIToolkitHelper.GetCardContent(advancedCard);
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugMode"), "Debug Logging"));
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("showPreview"), "Show Preview Gizmos"));
            root.Add(advancedCard);
            
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
            
            // Initialize previous values
            previousTargetMesh = data.targetMesh;
            previousManualTriangleIndex = data.manualTriangleIndex;
            previousSolverMode = data.solverMode;
            previousAttachToClosestBone = data.attachToClosestBone;
            previousTrackingMode = data.trackingMode;
            previousSamplesPerBlendshape = data.samplesPerBlendshape;
            previousValidationError = ValidateData() ? null : GetValidationError();
            previousTrackedBlendshapesCount = data.TrackedBlendshapes?.Count ?? -1;
            previousGeneratedAnimationCount = data.TransferredBlendshapeCount;
            previousSelectedBonePath = data.SelectedBonePath;
            previousPreviewGenerated = data.previewGenerated;
            previousPreviewBlendshapeCount = data.previewBlendshapes?.Count ?? -1;
            
            // Initial population
            UpdateValidationBanner(validationBanner);
            UpdateTargetMeshHelp(targetMeshHelp);
            UpdateTargetMeshToModifyHelp(root.Q<VisualElement>("target-mesh-to-modify-help"));
            UpdateManualTriangleHelp(manualTriangleHelp);
            UpdateSolverModeCard(root.Q<VisualElement>("solver-mode-card"));
            UpdateBoneHelp(boneHelp);
            UpdateSamplesHelp(samplesHelp);
            UpdateTrackingModeContent(root.Q<VisualElement>("tracking-mode-content"));
            UpdatePreviewTools(root.Q<VisualElement>("preview-tools-container"));
            UpdateBuildStatistics(buildStatsFoldout, buildStatsContent);
            
            // Dynamic updates
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                // Update validation banner only when validation error changes
                string currentValidationError = ValidateData() ? null : GetValidationError();
                if (currentValidationError != previousValidationError)
                {
                    UpdateValidationBanner(validationBanner);
                    previousValidationError = currentValidationError;
                }
                
                // Update source mesh help only when source mesh changes
                if (data.targetMesh != previousTargetMesh)
                {
                    UpdateTargetMeshHelp(targetMeshHelp);
                    previousTargetMesh = data.targetMesh;
                }
                
                // Update target mesh to modify help
                UpdateTargetMeshToModifyHelp(root.Q<VisualElement>("target-mesh-to-modify-help"));
                
                // Update manual triangle help only when index changes
                if (data.manualTriangleIndex != previousManualTriangleIndex)
                {
                    UpdateManualTriangleHelp(manualTriangleHelp);
                    previousManualTriangleIndex = data.manualTriangleIndex;
                }
                
                // Update solver mode card only when solver mode changes
                var solverCardContainer = root.Q<VisualElement>("solver-mode-card");
                if (data.solverMode != previousSolverMode)
                {
                    UpdateSolverModeCard(solverCardContainer);
                    previousSolverMode = data.solverMode;
                }
                
                normalOffsetCard.style.display = (data.solverMode == SolverMode.RigidNormalOffset) ? DisplayStyle.Flex : DisplayStyle.None;
                rbfCard.style.display = (data.solverMode == SolverMode.CageRBF) ? DisplayStyle.Flex : DisplayStyle.None;
                
                boneSettingsCard.style.display = data.attachToClosestBone ? DisplayStyle.Flex : DisplayStyle.None;
                
                // Update bone help only when attachToClosestBone changes
                if (data.attachToClosestBone != previousAttachToClosestBone)
                {
                    UpdateBoneHelp(boneHelp);
                    previousAttachToClosestBone = data.attachToClosestBone;
                }
                
                // Update samples help only when samplesPerBlendshape changes
                if (data.samplesPerBlendshape != previousSamplesPerBlendshape)
                {
                    UpdateSamplesHelp(samplesHelp);
                    previousSamplesPerBlendshape = data.samplesPerBlendshape;
                }
                
                // Update tracking mode content only when tracking mode changes
                if (data.trackingMode != previousTrackingMode || data.targetMesh != previousTargetMesh)
                {
                    UpdateTrackingModeContent(root.Q<VisualElement>("tracking-mode-content"));
                }
                
                // Update preview tools only when preview state actually changes (to avoid resetting scroll/sliders)
                bool previewStateChanged = data.previewGenerated != previousPreviewGenerated ||
                    (data.previewBlendshapes?.Count ?? -1) != previousPreviewBlendshapeCount;
                if (previewStateChanged)
                {
                    UpdatePreviewTools(root.Q<VisualElement>("preview-tools-container"));
                    previousPreviewGenerated = data.previewGenerated;
                    previousPreviewBlendshapeCount = data.previewBlendshapes?.Count ?? -1;
                }
                
                // Update button states without refreshing entire UI (preserves scroll/sliders)
                if (previewToolsInstance != null)
                {
                    previewToolsInstance.RefreshButtons();
                }
                
                // Don't update Data constantly - it triggers RefreshUI which clears sliders
                // The sliders update themselves via callbacks when values change
                
                // Update build statistics only when relevant data changes
                int currentTrackedCount = data.TrackedBlendshapes?.Count ?? -1;
                if (currentTrackedCount != previousTrackedBlendshapesCount || 
                    data.TransferredBlendshapeCount != previousGeneratedAnimationCount ||
                    data.SelectedBonePath != previousSelectedBonePath)
                {
                    UpdateBuildStatistics(buildStatsFoldout, buildStatsContent);
                    previousTrackedBlendshapesCount = currentTrackedCount;
                    previousGeneratedAnimationCount = data.TransferredBlendshapeCount;
                    previousSelectedBonePath = data.SelectedBonePath;
                }
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateValidationBanner(VisualElement container)
        {
            container.Clear();
            if (!ValidateData())
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Configuration Error\n\n{GetValidationError()}", YUCPUIToolkitHelper.MessageType.Error));
            }
        }
        
        private void UpdateTargetMeshHelp(VisualElement container)
        {
            container.Clear();
            if (data.targetMesh != null)
            {
                if (!PoseSampler.HasBlendshapes(data.targetMesh))
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox("Source mesh has no blendshapes!", YUCPUIToolkitHelper.MessageType.Error));
                }
                else
                {
                    int blendshapeCount = data.targetMesh.sharedMesh.blendShapeCount;
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Found {blendshapeCount} blendshape{(blendshapeCount != 1 ? "s" : "")} on source mesh", YUCPUIToolkitHelper.MessageType.Info));
                }
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Select a SkinnedMeshRenderer with blendshapes (source mesh)", YUCPUIToolkitHelper.MessageType.None));
            }
        }
        
        private void UpdateTargetMeshToModifyHelp(VisualElement container)
        {
            if (container == null) return;
            container.Clear();
            if (data.targetMeshToModify != null)
            {
                SkinnedMeshRenderer smr = null;
                MeshFilter mf = null;
                
                // Check if it's directly a component
                if (data.targetMeshToModify is SkinnedMeshRenderer directSmr)
                {
                    smr = directSmr;
                }
                else if (data.targetMeshToModify is MeshFilter directMf)
                {
                    mf = directMf;
                }
                // Check if it's a GameObject with the component
                else if (data.targetMeshToModify is GameObject go)
                {
                    smr = go.GetComponent<SkinnedMeshRenderer>();
                    if (smr == null)
                    {
                        mf = go.GetComponent<MeshFilter>();
                    }
                }
                
                if (smr != null)
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Target: SkinnedMeshRenderer '{smr.name}'\nSupports blendshapes on skinned meshes.", YUCPUIToolkitHelper.MessageType.Info));
                }
                else if (mf != null)
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Target: MeshFilter '{mf.name}'\nPerfect for static meshes like piercings, accessories, or props.", YUCPUIToolkitHelper.MessageType.Info));
                }
                else
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox("Target must be a SkinnedMeshRenderer, MeshFilter, or GameObject with one of these components.\n\n• SkinnedMeshRenderer: For skinned meshes\n• MeshFilter: For static meshes (piercings, accessories, props)\n• GameObject: Will auto-detect MeshFilter or SkinnedMeshRenderer", YUCPUIToolkitHelper.MessageType.Error));
                }
            }
            else
            {
                // Check if component's GameObject has a mesh
                var smr = data.GetComponent<SkinnedMeshRenderer>();
                var mf = data.GetComponent<MeshFilter>();
                if (smr != null)
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Auto-detected: SkinnedMeshRenderer on this GameObject\nSupports blendshapes on skinned meshes.", YUCPUIToolkitHelper.MessageType.Info));
                }
                else if (mf != null)
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Auto-detected: MeshFilter on this GameObject\nPerfect for static meshes like piercings, accessories, or props.", YUCPUIToolkitHelper.MessageType.Info));
                }
                else
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox("Set target mesh or add SkinnedMeshRenderer/MeshFilter to this GameObject.\n\n• SkinnedMeshRenderer: For skinned meshes\n• MeshFilter: For static meshes (piercings, accessories, props)", YUCPUIToolkitHelper.MessageType.Warning));
                }
            }
        }
        
        private void UpdateManualTriangleHelp(VisualElement container)
        {
            container.Clear();
            if (data.manualTriangleIndex >= 0)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox($"Manual mode: Using triangle #{data.manualTriangleIndex} as primary anchor", YUCPUIToolkitHelper.MessageType.Info));
            }
        }
        
        private void UpdateSolverModeCard(VisualElement container)
        {
            if (container == null || !showSolverConfiguration) return;
            container.Clear();
            var solverCard = YUCPUIToolkitHelper.CreateSolverModeCard(data.solverMode);
            container.Add(solverCard);
        }
        
        private void UpdateBoneHelp(VisualElement container)
        {
            container.Clear();
            if (data.attachToClosestBone)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Bone attachment provides base positioning.\nBlendshape animations are applied relative to the bone.", YUCPUIToolkitHelper.MessageType.None));
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Bone attachment disabled - object will stay in place without base bone link.", YUCPUIToolkitHelper.MessageType.Warning));
            }
        }
        
        private void UpdateSamplesHelp(VisualElement container)
        {
            container.Clear();
            int samples = data.samplesPerBlendshape;
            container.Add(YUCPUIToolkitHelper.CreateHelpBox($"{samples} keyframes per blendshape\n" +
                $"More keyframes = smoother deformation but larger file size", YUCPUIToolkitHelper.MessageType.None));
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
            
            var transferredLabel = new Label($"Transferred Blendshapes: {data.TransferredBlendshapeCount}");
            transferredLabel.SetEnabled(false);
            transferredLabel.style.fontSize = 11;
            transferredLabel.style.marginBottom = 2;
            content.Add(transferredLabel);
            
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
                // Get target mesh for categorization - use the WORKING mesh (after transfer) to check which blendshapes exist
                // This allows us to categorize blendshapes that affect both meshes vs only body
                Mesh targetMeshForPreview = data.previewWorkingMesh; // Use working mesh (after transfer)
                
                // Fallback to original mesh if working mesh not available yet
                if (targetMeshForPreview == null)
                {
                    targetMeshForPreview = data.previewOriginalTargetMesh;
                    
                    // Fallback to current mesh if original not stored yet
                    if (targetMeshForPreview == null)
                    {
                        if (data.targetMeshToModify is SkinnedMeshRenderer smr)
                        {
                            targetMeshForPreview = smr.sharedMesh;
                        }
                        else if (data.targetMeshToModify is MeshFilter mf)
                        {
                            targetMeshForPreview = mf.sharedMesh;
                        }
                        else if (data.targetMeshToModify is GameObject go)
                        {
                            var meshFilter = go.GetComponent<MeshFilter>();
                            if (meshFilter != null)
                            {
                                targetMeshForPreview = meshFilter.sharedMesh;
                            }
                            else
                            {
                                var skinnedMesh = go.GetComponent<SkinnedMeshRenderer>();
                                if (skinnedMesh != null)
                                {
                                    targetMeshForPreview = skinnedMesh.sharedMesh;
                                }
                            }
                        }
                    }
                }
                
                // Get source body mesh for categorization
                Mesh sourceMeshForPreview = null;
                if (data.targetMesh != null)
                {
                    sourceMeshForPreview = data.targetMesh.sharedMesh;
                }
                
                var previewData = new YUCPPreviewTools.AttachToBlendshapePreviewData
                {
                    previewGenerated = data.previewGenerated,
                    clusterTriangleCount = data.previewCluster?.anchors.Count ?? 0,
                    clusterCenter = data.previewCluster?.centerPosition ?? Vector3.zero,
                    blendshapes = data.previewBlendshapes,
                    blendshapeWeights = data.previewBlendshapeWeights,
                    originalWeights = data.previewOriginalWeights,
                    targetMesh = targetMeshForPreview, // Working mesh (after transfer)
                    sourceMesh = sourceMeshForPreview,
                    originalTargetMesh = data.previewOriginalTargetMesh // Original mesh (before transfer)
                };
                
                // Use single toggle button like AutoBodyHider
                previewToolsInstance = YUCPUIToolkitHelper.CreatePreviewTools(
                    previewData,
                    () => ValidateData(),
                    () => {
                        if (data.previewGenerated)
                        {
                            ClearPreview();
                        }
                        else
                        {
                            GeneratePreview();
                        }
                    },
                    null, // No separate clear callback - handled by toggle
                    (name) => GetCurrentBlendshapeWeight(name),
                    (name, value) => ApplyPreviewBlendshapeWeight(name, value),
                    () => RestorePreviewBlendshapes(),
                    () => ZeroPreviewBlendshapes()
                );
                
                container.Add(previewToolsInstance);
            }
            
            // Only update Data when preview state changes (setting Data triggers RefreshUI which clears sliders)
            // For weight updates, the sliders update themselves via the setWeight callback
            // So we don't need to constantly update Data here
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

            // Set weight on source mesh (body)
            int sourceIndex = data.targetMesh.sharedMesh.GetBlendShapeIndex(name);
            if (sourceIndex >= 0)
            {
                data.targetMesh.SetBlendShapeWeight(sourceIndex, value);
            }

            // Store the weight
            data.previewBlendshapeWeights[name] = value;
            
            // Apply weight to target mesh directly (the one we transferred blendshapes to)
            SkinnedMeshRenderer targetSkinnedMesh = null;
            MeshFilter targetMeshFilter = null;
            
            if (data.targetMeshToModify is SkinnedMeshRenderer smr)
            {
                targetSkinnedMesh = smr;
            }
            else if (data.targetMeshToModify is MeshFilter mf)
            {
                targetMeshFilter = mf;
            }
            else if (data.targetMeshToModify is GameObject go)
            {
                targetMeshFilter = go.GetComponent<MeshFilter>();
                if (targetMeshFilter == null)
                {
                    targetSkinnedMesh = go.GetComponent<SkinnedMeshRenderer>();
                }
            }
            
            // For MeshFilter, use the temporary SkinnedMeshRenderer
            if (targetMeshFilter != null && data.previewTempSkinnedMesh != null)
            {
                targetSkinnedMesh = data.previewTempSkinnedMesh;
            }
            
            // CRITICAL: Ensure the SkinnedMeshRenderer is using the working mesh (with transferred blendshapes)
            if (targetSkinnedMesh != null && data.previewWorkingMesh != null && targetSkinnedMesh.sharedMesh != data.previewWorkingMesh)
            {
                Debug.Log($"[AttachToBlendshape Preview] Updating SkinnedMeshRenderer to use working mesh before applying weight. Current: {targetSkinnedMesh.sharedMesh.name}, Working: {data.previewWorkingMesh.name}", data);
                targetSkinnedMesh.sharedMesh = data.previewWorkingMesh;
            }
            
            // Apply blendshape weight to target mesh
            if (targetSkinnedMesh != null && targetSkinnedMesh.sharedMesh != null)
            {
                int targetIndex = targetSkinnedMesh.sharedMesh.GetBlendShapeIndex(name);
                if (targetIndex >= 0)
                {
                    targetSkinnedMesh.SetBlendShapeWeight(targetIndex, value);
                    Debug.Log($"[AttachToBlendshape Preview] Applied blendshape '{name}' weight {value} to target mesh '{targetSkinnedMesh.name}' (index: {targetIndex}, mesh: {targetSkinnedMesh.sharedMesh.name}, blendShapeCount: {targetSkinnedMesh.sharedMesh.blendShapeCount})", data);
                    
                    // Force immediate update - multiple methods to ensure it works
                    EditorUtility.SetDirty(targetSkinnedMesh);
                    EditorUtility.SetDirty(targetSkinnedMesh.sharedMesh);
                    
                    // Force mesh recalculation
                    targetSkinnedMesh.enabled = false;
                    targetSkinnedMesh.enabled = true;
                    
                    // Force scene update
                    if (targetSkinnedMesh.gameObject.scene.IsValid())
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(targetSkinnedMesh.gameObject.scene);
                    }
                    
                    // Force Unity to update the mesh immediately
                    UnityEditor.SceneView.RepaintAll();
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                }
                else
                {
                    Debug.LogWarning($"[AttachToBlendshape Preview] Blendshape '{name}' not found on target mesh '{targetSkinnedMesh.name}' (mesh: {targetSkinnedMesh.sharedMesh.name}, blendShapeCount: {targetSkinnedMesh.sharedMesh.blendShapeCount})", data);
                    
                    // Debug: List all blendshapes on the target mesh
                    if (targetSkinnedMesh.sharedMesh.blendShapeCount > 0)
                    {
                        var allBlendshapes = new System.Text.StringBuilder();
                        for (int i = 0; i < targetSkinnedMesh.sharedMesh.blendShapeCount; i++)
                        {
                            allBlendshapes.Append($"{targetSkinnedMesh.sharedMesh.GetBlendShapeName(i)}, ");
                        }
                        Debug.Log($"[AttachToBlendshape Preview] Available blendshapes on target mesh: {allBlendshapes.ToString()}", data);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[AttachToBlendshape Preview] Target SkinnedMeshRenderer is null or has no mesh. targetSkinnedMesh={targetSkinnedMesh != null}, targetMeshFilter={targetMeshFilter != null}, previewTempSkinnedMesh={data.previewTempSkinnedMesh != null}, previewWorkingMesh={data.previewWorkingMesh != null}", data);
            }
            
            // Update the target mesh's blendshape weights to match (for position/rotation updates)
            UpdatePreviewAttachmentPose();
            
            // Force immediate update
            UnityEditor.EditorUtility.SetDirty(data.targetMesh);
            if (data.targetMeshToModify is SkinnedMeshRenderer smr2 && smr2 != null)
            {
                UnityEditor.EditorUtility.SetDirty(smr2);
            }
            else if (data.targetMeshToModify is MeshFilter mf2 && mf2 != null)
            {
                UnityEditor.EditorUtility.SetDirty(mf2);
            }
            
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
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

            // Get target mesh renderer
            SkinnedMeshRenderer targetSkinnedMesh = null;
            MeshFilter targetMeshFilter = null;
            Mesh targetMeshInstance = null;

            // Use the working mesh (with transferred blendshapes) if available
            if (data.previewWorkingMesh != null)
            {
                targetMeshInstance = data.previewWorkingMesh;
            }

            if (data.targetMeshToModify is SkinnedMeshRenderer smr)
            {
                targetSkinnedMesh = smr;
                // Use working mesh if available, otherwise use original
                if (targetMeshInstance == null)
                {
                    targetMeshInstance = smr.sharedMesh;
                }
            }
            else if (data.targetMeshToModify is MeshFilter mf)
            {
                targetMeshFilter = mf;
                // Use working mesh if available, otherwise use original
                if (targetMeshInstance == null)
                {
                    targetMeshInstance = mf.sharedMesh;
                }
            }
            else if (data.targetMeshToModify is GameObject go)
            {
                targetMeshFilter = go.GetComponent<MeshFilter>();
                if (targetMeshFilter != null)
                {
                    // Use working mesh if available, otherwise use original
                    if (targetMeshInstance == null)
                    {
                        targetMeshInstance = targetMeshFilter.sharedMesh;
                    }
                }
                else
                {
                    targetSkinnedMesh = go.GetComponent<SkinnedMeshRenderer>();
                    if (targetSkinnedMesh != null)
                    {
                        // Use working mesh if available, otherwise use original
                        if (targetMeshInstance == null)
                        {
                            targetMeshInstance = targetSkinnedMesh.sharedMesh;
                        }
                    }
                }
            }
            else
            {
                targetSkinnedMesh = data.GetComponent<SkinnedMeshRenderer>();
                if (targetSkinnedMesh != null)
                {
                    // Use working mesh if available, otherwise use original
                    if (targetMeshInstance == null)
                    {
                        targetMeshInstance = targetSkinnedMesh.sharedMesh;
                    }
                }
                else
                {
                    targetMeshFilter = data.GetComponent<MeshFilter>();
                    if (targetMeshFilter != null)
                    {
                        // Use working mesh if available, otherwise use original
                        if (targetMeshInstance == null)
                        {
                            targetMeshInstance = targetMeshFilter.sharedMesh;
                        }
                    }
                }
            }

            if (targetMeshInstance == null)
            {
                Debug.LogWarning("[AttachToBlendshape Preview] No target mesh instance found for preview", data);
                return;
            }
            
            // Ensure we're using the working mesh (with transferred blendshapes)
            if (data.previewWorkingMesh != null && targetMeshInstance != data.previewWorkingMesh)
            {
                Debug.Log($"[AttachToBlendshape Preview] Switching to working mesh. Original: {targetMeshInstance.name} ({targetMeshInstance.blendShapeCount} blendshapes), Working: {data.previewWorkingMesh.name} ({data.previewWorkingMesh.blendShapeCount} blendshapes)", data);
                targetMeshInstance = data.previewWorkingMesh;
            }

            // Update blendshape weights on target mesh based on source mesh blendshape weights
            // The blendshapes should have been transferred during GeneratePreview
            // Now we just need to sync the weights
            SkinnedMeshRenderer activeSkinnedMesh = targetSkinnedMesh;
            
            // For MeshFilter, we need to use a temporary SkinnedMeshRenderer for preview
            // because MeshFilter doesn't support runtime blendshape weights
            if (targetMeshFilter != null)
            {
                if (data.previewTempSkinnedMesh == null)
                {
                    // Create temporary SkinnedMeshRenderer for preview
                    data.previewOriginalMeshFilter = targetMeshFilter;
                    
                    // Store reference to MeshRenderer BEFORE creating SkinnedMeshRenderer
                    // (creating SkinnedMeshRenderer might affect the MeshRenderer)
                    var meshRenderer = targetMeshFilter.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        data.previewOriginalMeshRenderer = meshRenderer;
                        Debug.Log($"[AttachToBlendshape Preview] Stored MeshRenderer reference '{meshRenderer.name}' before creating temp SkinnedMeshRenderer", data);
                    }
                    else
                    {
                        Debug.LogWarning($"[AttachToBlendshape Preview] No MeshRenderer found on '{targetMeshFilter.name}' - will not be able to restore it", data);
                    }
                    
                    data.previewTempSkinnedMesh = targetMeshFilter.gameObject.AddComponent<SkinnedMeshRenderer>();
                    data.previewTempSkinnedMesh.sharedMesh = targetMeshInstance; // This should be the working mesh with transferred blendshapes
                    Debug.Log($"[AttachToBlendshape Preview] Created temp SkinnedMeshRenderer with mesh '{targetMeshInstance.name}' (blendShapeCount: {targetMeshInstance.blendShapeCount})", data);
                    
                    // Copy material from MeshRenderer if it exists
                    if (meshRenderer != null)
                    {
                        if (meshRenderer.sharedMaterial != null)
                        {
                            data.previewTempSkinnedMesh.sharedMaterial = meshRenderer.sharedMaterial;
                        }
                        // Hide the MeshRenderer and show the SkinnedMeshRenderer for preview
                        meshRenderer.enabled = false;
                        Debug.Log($"[AttachToBlendshape Preview] Disabled MeshRenderer '{meshRenderer.name}'", data);
                    }
                    data.previewTempSkinnedMesh.enabled = true;
                }
                
                activeSkinnedMesh = data.previewTempSkinnedMesh;
                
                // CRITICAL: Ensure the temporary SkinnedMeshRenderer is using the working mesh (with transferred blendshapes)
                if (data.previewWorkingMesh != null && activeSkinnedMesh.sharedMesh != data.previewWorkingMesh)
                {
                    Debug.Log($"[AttachToBlendshape Preview] Updating temp SkinnedMeshRenderer to use working mesh. Current: {activeSkinnedMesh.sharedMesh.name} ({activeSkinnedMesh.sharedMesh.blendShapeCount} blendshapes), Working: {data.previewWorkingMesh.name} ({data.previewWorkingMesh.blendShapeCount} blendshapes)", data);
                    activeSkinnedMesh.sharedMesh = data.previewWorkingMesh;
                }
            }
            else if (targetSkinnedMesh != null)
            {
                // For SkinnedMeshRenderer, ensure it's using the working mesh
                if (data.previewWorkingMesh != null && targetSkinnedMesh.sharedMesh != data.previewWorkingMesh)
                {
                    Debug.Log($"[AttachToBlendshape Preview] Updating SkinnedMeshRenderer to use working mesh. Current: {targetSkinnedMesh.sharedMesh.name} ({targetSkinnedMesh.sharedMesh.blendShapeCount} blendshapes), Working: {data.previewWorkingMesh.name} ({data.previewWorkingMesh.blendShapeCount} blendshapes)", data);
                    targetSkinnedMesh.sharedMesh = data.previewWorkingMesh;
                }
                activeSkinnedMesh = targetSkinnedMesh;
            }
            
            if (activeSkinnedMesh != null && activeSkinnedMesh.sharedMesh != null)
            {
                foreach (string blendshapeName in data.previewBlendshapes)
                {
                    if (!data.previewBlendshapeWeights.ContainsKey(blendshapeName))
                    {
                        continue;
                    }

                    float sourceWeight = data.previewBlendshapeWeights[blendshapeName];
                    int targetBlendshapeIndex = activeSkinnedMesh.sharedMesh.GetBlendShapeIndex(blendshapeName);

                    if (targetBlendshapeIndex >= 0)
                    {
                        activeSkinnedMesh.SetBlendShapeWeight(targetBlendshapeIndex, sourceWeight);
                        if (data.debugMode)
                        {
                            Debug.Log($"[Preview] Set blendshape '{blendshapeName}' weight to {sourceWeight} on target mesh", data);
                        }
                    }
                    else if (data.debugMode)
                    {
                        Debug.LogWarning($"[Preview] Blendshape '{blendshapeName}' not found on target mesh. Was it transferred?", data);
                    }
                }
                
                // Force mesh update - Unity needs to recalculate the mesh with blendshape weights
                // Mark the renderer as dirty and force an update
                UnityEditor.EditorUtility.SetDirty(activeSkinnedMesh);
                
                // Force immediate mesh recalculation by toggling enabled state
                activeSkinnedMesh.enabled = false;
                activeSkinnedMesh.enabled = true;
                
                // Force Unity to update the scene
                if (activeSkinnedMesh.gameObject.scene.IsValid())
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeSkinnedMesh.gameObject.scene);
                }
                
                // Also mark the mesh itself as dirty if it's not a shared mesh
                if (activeSkinnedMesh.sharedMesh != null)
                {
                    UnityEditor.EditorUtility.SetDirty(activeSkinnedMesh.sharedMesh);
                }
            }
            else if (data.debugMode)
            {
                Debug.LogWarning("[Preview] No active SkinnedMeshRenderer found for preview", data);
            }

            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
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

                // Actually transfer blendshapes to target mesh for preview
                Debug.Log($"[AttachToBlendshape Preview] Transferring {data.previewBlendshapes.Count} blendshapes to target mesh...", data);
                bool transferSuccess = BlendshapeTransfer.TransferBlendshapes(
                    data.targetMesh,
                    data.targetMeshToModify,
                    data.previewBlendshapes,
                    data.previewCluster,
                    data);

                if (!transferSuccess)
                {
                    EditorUtility.DisplayDialog("Preview Failed", "Failed to transfer blendshapes to target mesh for preview.", "OK");
                    return;
                }
                
                Debug.Log($"[AttachToBlendshape Preview] Blendshapes transferred successfully. Target mesh should now have {data.previewBlendshapes.Count} blendshapes.", data);

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
            
            // Restore original mesh (revert from copy to original, like VRCFury)
            if (data.previewOriginalMesh != null)
            {
                // Find the target mesh component (same logic as BlendshapeTransfer uses)
                SkinnedMeshRenderer targetSkinnedMesh = null;
                MeshFilter targetMeshFilter = null;
                
                var targetMeshObj = data.targetMeshToModify;
                
                if (targetMeshObj is SkinnedMeshRenderer smr)
                {
                    targetSkinnedMesh = smr;
                }
                else if (targetMeshObj is MeshFilter mf)
                {
                    targetMeshFilter = mf;
                }
                else if (targetMeshObj is GameObject go)
                {
                    targetMeshFilter = go.GetComponent<MeshFilter>();
                    if (targetMeshFilter == null)
                    {
                        targetSkinnedMesh = go.GetComponent<SkinnedMeshRenderer>();
                    }
                }
                else if (targetMeshObj == null)
                {
                    targetSkinnedMesh = data.GetComponent<SkinnedMeshRenderer>();
                    if (targetSkinnedMesh == null)
                    {
                        targetMeshFilter = data.GetComponent<MeshFilter>();
                    }
                }
                
                // Restore original mesh
                if (targetSkinnedMesh != null)
                {
                    // Check if we're using the working mesh (either directly or via temp SkinnedMeshRenderer)
                    if (targetSkinnedMesh.sharedMesh == data.previewWorkingMesh || 
                        (data.previewTempSkinnedMesh != null && data.previewTempSkinnedMesh.sharedMesh == data.previewWorkingMesh))
                    {
                        if (data.previewTempSkinnedMesh != null && data.previewTempSkinnedMesh == targetSkinnedMesh)
                        {
                            // This is the temp SkinnedMeshRenderer - we'll destroy it below
                        }
                        else
                        {
                            targetSkinnedMesh.sharedMesh = data.previewOriginalMesh;
                            Debug.Log($"[AttachToBlendshape Preview] Restored original mesh to SkinnedMeshRenderer '{targetSkinnedMesh.name}'", data);
                        }
                    }
                }
                else if (targetMeshFilter != null)
                {
                    // For MeshFilter, check if temp SkinnedMeshRenderer is using the working mesh
                    if (data.previewTempSkinnedMesh != null && data.previewTempSkinnedMesh.sharedMesh == data.previewWorkingMesh)
                    {
                        // Temp SkinnedMeshRenderer will be destroyed below, and MeshFilter will be restored
                        // MeshFilter should already have original mesh, but restore it just in case
                        if (targetMeshFilter.sharedMesh == data.previewWorkingMesh || targetMeshFilter.sharedMesh == null)
                        {
                            targetMeshFilter.sharedMesh = data.previewOriginalMesh;
                            Debug.Log($"[AttachToBlendshape Preview] Restored original mesh to MeshFilter '{targetMeshFilter.name}'", data);
                        }
                    }
                    else if (targetMeshFilter.sharedMesh == data.previewWorkingMesh || targetMeshFilter.sharedMesh == null)
                    {
                        targetMeshFilter.sharedMesh = data.previewOriginalMesh;
                        Debug.Log($"[AttachToBlendshape Preview] Restored original mesh to MeshFilter '{targetMeshFilter.name}'", data);
                    }
                }
                else
                {
                    Debug.LogWarning("[AttachToBlendshape Preview] Could not find target mesh component to restore. Mesh may be missing.", data);
                }
                
                // Clean up working mesh copy
                if (data.previewWorkingMesh != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(data.previewWorkingMesh);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(data.previewWorkingMesh);
                    }
                    data.previewWorkingMesh = null;
                }
                
                // Don't clear previewOriginalMesh - we need it for the next preview generation
                // It will be overwritten when generating preview again
            }
            
            // Remove temporary SkinnedMeshRenderer if it was created for MeshFilter preview
            if (data.previewTempSkinnedMesh != null)
            {
                // Restore MeshRenderer if it was hidden (don't destroy it, just enable it)
                // Use stored reference first, then fallback to GetComponent
                MeshRenderer meshRenderer = data.previewOriginalMeshRenderer;
                
                // If stored reference is null or destroyed, try to get it from MeshFilter
                if (meshRenderer == null && data.previewOriginalMeshFilter != null)
                {
                    meshRenderer = data.previewOriginalMeshFilter.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        Debug.Log($"[AttachToBlendshape Preview] Found MeshRenderer via GetComponent (stored reference was null)", data);
                    }
                }
                
                // Check if stored reference was destroyed (Unity might destroy MeshRenderer when SkinnedMeshRenderer is added)
                if (data.previewOriginalMeshRenderer != null)
                {
                    // Check if the object still exists (not destroyed)
                    if (data.previewOriginalMeshRenderer == null)
                    {
                        Debug.LogWarning($"[AttachToBlendshape Preview] Stored MeshRenderer reference was destroyed by Unity (likely when SkinnedMeshRenderer was added)", data);
                    }
                }
                
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = true;
                    Debug.Log($"[AttachToBlendshape Preview] Re-enabled MeshRenderer '{meshRenderer.name}'", data);
                }
                else
                {
                    Debug.LogWarning($"[AttachToBlendshape Preview] MeshRenderer not found - it may have been destroyed. Stored reference: {data.previewOriginalMeshRenderer != null}, MeshFilter: {data.previewOriginalMeshFilter != null}", data);
                    
                    // Unity might have destroyed the MeshRenderer when we added SkinnedMeshRenderer
                    // Try to recreate it if it doesn't exist (Unity primitives should have one)
                    if (data.previewOriginalMeshFilter != null)
                    {
                        // Check if MeshRenderer was destroyed by checking if GameObject still exists
                        if (data.previewOriginalMeshFilter.gameObject != null)
                        {
                            var existingRenderer = data.previewOriginalMeshFilter.GetComponent<MeshRenderer>();
                            if (existingRenderer == null)
                            {
                                // MeshRenderer was destroyed, recreate it
                                var newMeshRenderer = data.previewOriginalMeshFilter.gameObject.AddComponent<MeshRenderer>();
                                if (newMeshRenderer != null)
                                {
                                    // Try to restore material from SkinnedMeshRenderer if it exists
                                    if (data.previewTempSkinnedMesh != null && data.previewTempSkinnedMesh.sharedMaterial != null)
                                    {
                                        newMeshRenderer.sharedMaterial = data.previewTempSkinnedMesh.sharedMaterial;
                                    }
                                    Debug.Log($"[AttachToBlendshape Preview] Recreated MeshRenderer on '{data.previewOriginalMeshFilter.name}' (Unity destroyed the original)", data);
                                }
                            }
                        }
                    }
                }
                
                // Clear stored reference
                data.previewOriginalMeshRenderer = null;
                
                // Destroy only the temporary SkinnedMeshRenderer, not the MeshRenderer
                Debug.Log($"[AttachToBlendshape Preview] Destroying temporary SkinnedMeshRenderer '{data.previewTempSkinnedMesh.name}'", data);
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(data.previewTempSkinnedMesh);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(data.previewTempSkinnedMesh);
                }
                data.previewTempSkinnedMesh = null;
                data.previewOriginalMeshFilter = null;
            }
            
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

            Debug.Log("[AttachToBlendshape Preview] Preview cleared and original mesh restored");
        }

        private bool ValidateData()
        {
            if (data.targetMesh == null) return false;
            if (data.targetMesh.sharedMesh == null) return false;
            if (!PoseSampler.HasBlendshapes(data.targetMesh)) return false;
            if (data.trackingMode == BlendshapeTrackingMode.Specific && 
                (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0)) return false;
            // Check if target mesh to modify exists or can be auto-detected
            if (data.targetMeshToModify == null)
            {
                var smr = data.GetComponent<SkinnedMeshRenderer>();
                var mf = data.GetComponent<MeshFilter>();
                if (smr == null && mf == null) return false;
            }
            return true;
        }

        private string GetValidationError()
        {
            if (data.targetMesh == null) return "Source mesh is not set.";
            if (data.targetMesh.sharedMesh == null) return "Source mesh has no mesh data.";
            if (!PoseSampler.HasBlendshapes(data.targetMesh)) return "Source mesh has no blendshapes.";
            if (data.trackingMode == BlendshapeTrackingMode.Specific && 
                (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0))
                return "Specific mode requires at least one blendshape name.";
            if (data.targetMeshToModify == null)
            {
                var smr = data.GetComponent<SkinnedMeshRenderer>();
                var mf = data.GetComponent<MeshFilter>();
                if (smr == null && mf == null)
                    return "Target mesh to modify is not set and no SkinnedMeshRenderer/MeshFilter found on this GameObject.";
            }
            else
            {
                // Validate that the assigned object has a valid component
                bool hasValidComponent = false;
                if (data.targetMeshToModify is SkinnedMeshRenderer || data.targetMeshToModify is MeshFilter)
                {
                    hasValidComponent = true;
                }
                else if (data.targetMeshToModify is GameObject go)
                {
                    hasValidComponent = go.GetComponent<SkinnedMeshRenderer>() != null || go.GetComponent<MeshFilter>() != null;
                }
                
                if (!hasValidComponent)
                {
                    return "Target mesh must be a SkinnedMeshRenderer, MeshFilter, or GameObject with one of these components.";
                }
            }
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
