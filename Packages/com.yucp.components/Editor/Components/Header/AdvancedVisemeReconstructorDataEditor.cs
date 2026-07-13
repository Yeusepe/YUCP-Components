using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(AdvancedVisemeReconstructorData))]
    public sealed class AdvancedVisemeReconstructorDataEditor : UnityEditor.Editor
    {
        private AdvancedVisemeReconstructorData data;
        private SerializedProperty faceRendererProp;
        private SerializedProperty profileProp;
        private SerializedProperty mouthOwnershipProp;
        private SerializedProperty trackingInputsProp;
        private SerializedProperty trackingEncodingProp;
        private SerializedProperty fusionModeProp;
        private SerializedProperty parameterPrefixProp;
        private SerializedProperty createToggleProp;
        private SerializedProperty menuPathProp;
        private SerializedProperty existingPrefixProp;
        private SerializedProperty verboseLoggingProp;

        private void OnEnable()
        {
            data = (AdvancedVisemeReconstructorData)target;
            faceRendererProp = serializedObject.FindProperty("faceRenderer");
            profileProp = serializedObject.FindProperty("profile");
            mouthOwnershipProp = serializedObject.FindProperty("mouthOwnership");
            trackingInputsProp = serializedObject.FindProperty("trackingInputs");
            trackingEncodingProp = serializedObject.FindProperty("trackingEncoding");
            fusionModeProp = serializedObject.FindProperty("fusionMode");
            parameterPrefixProp = serializedObject.FindProperty("parameterPrefix");
            createToggleProp = serializedObject.FindProperty("createFaceTrackingToggle");
            menuPathProp = serializedObject.FindProperty("faceTrackingMenuPath");
            existingPrefixProp = serializedObject.FindProperty("existingTrackingPrefix");
            verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();

            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Advanced Viseme Reconstructor"));

            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(AdvancedVisemeReconstructorData));
            if (supportBanner != null) root.Add(supportBanner);

            var validation = new VisualElement { name = "validation-messages" };
            root.Add(validation);

            var setupCard = YUCPUIToolkitHelper.CreateCard("Setup", "Face rig and reconstruction behavior.");
            var setup = YUCPUIToolkitHelper.GetCardContent(setupCard);
            setup.Add(YUCPUIToolkitHelper.CreateField(faceRendererProp, "Face Renderer"));
            setup.Add(YUCPUIToolkitHelper.CreateField(profileProp, "Profile"));
            setup.Add(YUCPUIToolkitHelper.CreateField(mouthOwnershipProp, "Mouth Ownership"));

            var profileButton = YUCPUIToolkitHelper.CreateButton(
                "Create Profile",
                HandleProfileAction,
                YUCPUIToolkitHelper.ButtonVariant.Secondary);
            profileButton.name = "profile-action";
            profileButton.style.marginTop = 4;
            setup.Add(profileButton);
            root.Add(setupCard);

            var trackingCard = YUCPUIToolkitHelper.CreateCard("Face Tracking", "Optional Unified Expressions input.");
            var tracking = YUCPUIToolkitHelper.GetCardContent(trackingCard);
            tracking.Add(YUCPUIToolkitHelper.CreateField(trackingInputsProp, "Inputs"));

            var encodingContainer = new VisualElement { name = "encoding-container" };
            encodingContainer.Add(YUCPUIToolkitHelper.CreateField(trackingEncodingProp, "Encoding"));
            tracking.Add(encodingContainer);

            var fusionContainer = new VisualElement { name = "fusion-container" };
            fusionContainer.Add(YUCPUIToolkitHelper.CreateField(fusionModeProp, "Fusion"));
            tracking.Add(fusionContainer);

            var toggleContainer = new VisualElement { name = "toggle-container" };
            toggleContainer.Add(YUCPUIToolkitHelper.CreateField(createToggleProp, "Menu Toggle"));
            var menuPathContainer = new VisualElement { name = "menu-path-container" };
            menuPathContainer.Add(YUCPUIToolkitHelper.CreateField(menuPathProp, "Menu Path"));
            toggleContainer.Add(menuPathContainer);
            tracking.Add(toggleContainer);

            var reuseContainer = new VisualElement { name = "reuse-prefix-container" };
            reuseContainer.Add(YUCPUIToolkitHelper.CreateField(existingPrefixProp, "Existing Prefix"));
            tracking.Add(reuseContainer);

            var budgetLabel = new Label { name = "tracking-budget" };
            budgetLabel.style.fontSize = 10;
            budgetLabel.style.marginTop = 4;
            budgetLabel.style.opacity = 0.7f;
            tracking.Add(budgetLabel);
            root.Add(trackingCard);

            var toolsCard = YUCPUIToolkitHelper.CreateCard("Rig Tools", "Optional mapping and calibration.");
            var tools = YUCPUIToolkitHelper.GetCardContent(toolsCard);
            var toolRow = new VisualElement();
            toolRow.style.flexDirection = FlexDirection.Row;

            var autoMapButton = YUCPUIToolkitHelper.CreateButton(
                "Auto-map",
                () => AutoMap(data),
                YUCPUIToolkitHelper.ButtonVariant.Primary);
            autoMapButton.name = "auto-map-rig";
            autoMapButton.style.flexGrow = 1;
            autoMapButton.style.marginRight = 4;
            toolRow.Add(autoMapButton);

            var recalibrateButton = YUCPUIToolkitHelper.CreateButton(
                "Recalibrate",
                () => Recalibrate(data),
                YUCPUIToolkitHelper.ButtonVariant.Secondary);
            recalibrateButton.name = "recalibrate";
            recalibrateButton.style.flexGrow = 1;
            recalibrateButton.style.marginLeft = 4;
            toolRow.Add(recalibrateButton);

            tools.Add(toolRow);
            root.Add(toolsCard);

            var advanced = YUCPUIToolkitHelper.CreateFoldout("Advanced", false);
            advanced.name = "advanced-settings";
            advanced.Add(YUCPUIToolkitHelper.CreateField(parameterPrefixProp, "Parameter Prefix"));
            advanced.Add(YUCPUIToolkitHelper.CreateField(verboseLoggingProp, "Verbose Logging"));
            var status = new VisualElement { name = "build-status" };
            advanced.Add(status);
            root.Add(advanced);

            UpdateDynamicUI(
                validation,
                encodingContainer,
                fusionContainer,
                toggleContainer,
                menuPathContainer,
                reuseContainer,
                budgetLabel,
                profileButton,
                autoMapButton,
                recalibrateButton,
                status);

            root.schedule.Execute(() =>
            {
                if (target == null) return;
                serializedObject.UpdateIfRequiredOrScript();
                UpdateDynamicUI(
                    validation,
                    encodingContainer,
                    fusionContainer,
                    toggleContainer,
                    menuPathContainer,
                    reuseContainer,
                    budgetLabel,
                    profileButton,
                    autoMapButton,
                    recalibrateButton,
                    status);
            }).Every(150);

            return root;
        }

        private void UpdateDynamicUI(
            VisualElement validation,
            VisualElement encodingContainer,
            VisualElement fusionContainer,
            VisualElement toggleContainer,
            VisualElement menuPathContainer,
            VisualElement reuseContainer,
            Label budgetLabel,
            Button profileButton,
            Button autoMapButton,
            Button recalibrateButton,
            VisualElement status)
        {
            var ownership = (AdvancedVisemeMouthOwnership)mouthOwnershipProp.enumValueIndex;
            var tracking = (AdvancedVisemeTrackingInputs)trackingInputsProp.enumValueIndex;
            var encoding = (AdvancedVisemeTrackingEncoding)trackingEncodingProp.enumValueIndex;
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            var renderer = data.faceRenderer != null ? data.faceRenderer : descriptor?.VisemeSkinnedMesh;
            var profile = profileProp.objectReferenceValue as VisemeReconstructionProfile;
            var trackingEnabled = tracking != AdvancedVisemeTrackingInputs.Disabled;

            validation.Clear();
            if (descriptor == null)
            {
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "This component must be under a VRCAvatarDescriptor.",
                    YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (ownership == AdvancedVisemeMouthOwnership.DriveLowerFace &&
                     (renderer == null || renderer.sharedMesh == null))
            {
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Drive Lower Face requires an Oculus-viseme renderer.",
                    YUCPUIToolkitHelper.MessageType.Error));
            }

            fusionContainer.style.display = trackingEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            encodingContainer.style.display = tracking == AdvancedVisemeTrackingInputs.Balanced8 ||
                                              tracking == AdvancedVisemeTrackingInputs.Quality12 ||
                                              tracking == AdvancedVisemeTrackingInputs.Auto
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            toggleContainer.style.display = trackingEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            menuPathContainer.style.display = trackingEnabled && createToggleProp.boolValue
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            reuseContainer.style.display = tracking == AdvancedVisemeTrackingInputs.ReuseExisting ||
                                           tracking == AdvancedVisemeTrackingInputs.Auto
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            switch (tracking)
            {
                case AdvancedVisemeTrackingInputs.Balanced8:
                    budgetLabel.text = $"Up to {DisplayedTrackingBits(tracking, encoding)} synced bits";
                    break;
                case AdvancedVisemeTrackingInputs.Quality12:
                    budgetLabel.text = $"Up to {DisplayedTrackingBits(tracking, encoding)} synced bits";
                    break;
                case AdvancedVisemeTrackingInputs.ReuseExisting:
                    budgetLabel.text = "Reuses existing v2 parameters";
                    break;
                case AdvancedVisemeTrackingInputs.Auto:
                    budgetLabel.text = $"Auto-reuse; fallback up to {DisplayedTrackingBits(AdvancedVisemeTrackingInputs.Balanced8, encoding)} bits";
                    break;
                default:
                    budgetLabel.text = "No synced parameter cost";
                    break;
            }

            profileButton.text = profile == null ? "Create Profile" : "Reset Profile";
            var canUseRigTools = profile != null && renderer != null && renderer.sharedMesh != null;
            autoMapButton.SetEnabled(canUseRigTools);
            recalibrateButton.SetEnabled(canUseRigTools && descriptor != null);

            status.Clear();
            if (profile != null && (profile.LastReconstructionRms > 0f || profile.LastReconstructionMaximum > 0f))
                status.Add(new Label($"Fit: RMS {profile.LastReconstructionRms:G4}, max {profile.LastReconstructionMaximum:G4}"));
            if (!string.IsNullOrEmpty(data.GetBuildSummary()))
                status.Add(new Label("Last build: " + data.GetBuildSummary()));
        }

        private void HandleProfileAction()
        {
            serializedObject.Update();
            var profile = profileProp.objectReferenceValue as VisemeReconstructionProfile;
            if (profile == null)
            {
                Undo.RecordObject(data, "Create Viseme Reconstruction Profile");
                profileProp.objectReferenceValue = CreateProfileAsset(data);
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Reset reconstruction profile?",
                    "This replaces all profile values with YUCP defaults.",
                    "Reset",
                    "Cancel")) return;

            Undo.RecordObject(profile, "Reset Viseme Reconstruction Profile");
            profile.ResetToDefaults();
            EditorUtility.SetDirty(profile);
        }

        private int DisplayedTrackingBits(
            AdvancedVisemeTrackingInputs tracking,
            AdvancedVisemeTrackingEncoding encoding)
        {
            var bits = AdvancedVisemeMath.TrackingParameterBits(tracking, encoding);
            return createToggleProp.boolValue ? bits : Mathf.Max(0, bits - 1);
        }

        private static VisemeReconstructionProfile CreateProfileAsset(AdvancedVisemeReconstructorData data)
        {
            const string folder = "Assets/YUCP/AdvancedVisemeProfiles";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Sanitize(data.gameObject.name)}.asset");
            var profile = ScriptableObject.CreateInstance<VisemeReconstructionProfile>();
            profile.ResetToDefaults();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static void AutoMap(AdvancedVisemeReconstructorData data)
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null || data.profile == null) return;
            Undo.RecordObject(data, "Auto-map Advanced Viseme Renderer");
            if (data.faceRenderer == null) data.faceRenderer = descriptor.VisemeSkinnedMesh;
            var renderer = data.faceRenderer;
            if (renderer == null || renderer.sharedMesh == null) return;

            Undo.RecordObject(data.profile, "Auto-map Advanced Viseme Rig");
            data.profile.EnsureDefaults();
            var resolved = AdvancedVisemeReconstructorProcessor.ResolveArticulatorBlendShapes(renderer.sharedMesh, data.profile);
            foreach (var binding in data.profile.articulatorBindings)
            {
                if (binding != null && resolved.TryGetValue(binding.articulator, out var shape))
                    binding.blendShapeName = shape;
            }
            EditorUtility.SetDirty(data.profile);
            EditorUtility.SetDirty(data);
        }

        private static void Recalibrate(AdvancedVisemeReconstructorData data)
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            var renderer = data.faceRenderer != null ? data.faceRenderer : descriptor?.VisemeSkinnedMesh;
            if (descriptor == null || renderer == null || renderer.sharedMesh == null || data.profile == null) return;

            data.profile.EnsureDefaults();
            var names = AdvancedVisemeReconstructorProcessor.ResolveVisemeNames(descriptor, renderer.sharedMesh);
            var indices = new int[names.Length];
            for (var i = 0; i < names.Length; i++)
                indices[i] = string.IsNullOrEmpty(names[i]) ? -1 : renderer.sharedMesh.GetBlendShapeIndex(names[i]);
            var resolved = AdvancedVisemeReconstructorProcessor.ResolveArticulatorBlendShapes(renderer.sharedMesh, data.profile);
            var basis = AdvancedVisemeReconstructorProcessor.BuildCalibrationBasis(renderer.sharedMesh, resolved);
            var result = AdvancedVisemeMeshCalibrator.Build(renderer.sharedMesh, indices, basis);
            if (!result.success)
            {
                EditorUtility.DisplayDialog("Calibration failed", result.error, "OK");
                return;
            }

            Undo.RecordObject(data.profile, "Recalibrate Advanced Visemes");
            data.profile.SetDiagnostics(result.fitRms, result.fitMaximum);
            EditorUtility.SetDirty(data.profile);
            Object.DestroyImmediate(result.mesh);
            EditorUtility.DisplayDialog(
                "Calibration complete",
                $"RMS: {result.fitRms:G4}\nMaximum: {result.fitMaximum:G4}",
                "OK");
        }

        private static string Sanitize(string value)
        {
            var chars = (value ?? "Avatar").ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }
    }
}
