using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(FbxMergeData))]
    public class FbxMergeDataEditor : UnityEditor.Editor
    {
        private FbxMergeData data;

        private SerializedProperty baseRendererProp;
        private SerializedProperty attachmentTargetProp;
        private SerializedProperty deleteAttachmentObjectAfterMergeProp;
        private SerializedProperty renameConflictingAttachmentBlendshapesProp;
        private SerializedProperty attachmentBlendshapePrefixProp;
        private SerializedProperty unmatchedHandlingProp;
        private SerializedProperty remapAnimationsProp;
        private SerializedProperty remapBlendshapeAnimationsProp;
        private SerializedProperty remapMaterialAnimationsProp;
        private SerializedProperty remapRendererAndObjectOffToUvDiscardProp;
        private SerializedProperty scaleToZeroFallbackProp;
        private SerializedProperty autoDetectUVChannelProp;
        private SerializedProperty uvChannelProp;
        private SerializedProperty uvDiscardRowProp;
        private SerializedProperty uvDiscardColumnProp;
        private SerializedProperty debugModeProp;

        private bool showAdvanced = false;
        private bool showStats = false;

        private void OnEnable()
        {
            data = (FbxMergeData)target;

            baseRendererProp = serializedObject.FindProperty("baseRenderer");
            attachmentTargetProp = serializedObject.FindProperty("attachmentTarget");
            deleteAttachmentObjectAfterMergeProp = serializedObject.FindProperty("deleteAttachmentObjectAfterMerge");
            renameConflictingAttachmentBlendshapesProp = serializedObject.FindProperty("renameConflictingAttachmentBlendshapes");
            attachmentBlendshapePrefixProp = serializedObject.FindProperty("attachmentBlendshapePrefix");
            unmatchedHandlingProp = serializedObject.FindProperty("unmatchedHandling");
            remapAnimationsProp = serializedObject.FindProperty("remapAnimations");
            remapBlendshapeAnimationsProp = serializedObject.FindProperty("remapBlendshapeAnimations");
            remapMaterialAnimationsProp = serializedObject.FindProperty("remapMaterialAnimations");
            remapRendererAndObjectOffToUvDiscardProp = serializedObject.FindProperty("remapRendererAndObjectOffToUvDiscard");
            scaleToZeroFallbackProp = serializedObject.FindProperty("scaleToZeroFallback");
            autoDetectUVChannelProp = serializedObject.FindProperty("autoDetectUVChannel");
            uvChannelProp = serializedObject.FindProperty("uvChannel");
            uvDiscardRowProp = serializedObject.FindProperty("uvDiscardRow");
            uvDiscardColumnProp = serializedObject.FindProperty("uvDiscardColumn");
            debugModeProp = serializedObject.FindProperty("debugMode");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();

            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Mesh Merger"));

            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(FbxMergeData));
            if (betaWarning != null) root.Add(betaWarning);

            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(FbxMergeData));
            if (supportBanner != null) root.Add(supportBanner);

            var setupSummary = new VisualElement { name = "setup-summary" };
            root.Add(setupSummary);

            // --- Setup Card ---
            var setupCard = YUCPUIToolkitHelper.CreateCard("Setup", "Pick target meshes.");
            var setupContent = YUCPUIToolkitHelper.GetCardContent(setupCard);

            setupContent.Add(YUCPUIToolkitHelper.CreateField(baseRendererProp, "Base Renderer"));
            setupContent.Add(YUCPUIToolkitHelper.CreateField(attachmentTargetProp, "Attachment Target"));

            var buttonsRow1 = new VisualElement();
            buttonsRow1.style.flexDirection = FlexDirection.Row;
            buttonsRow1.style.flexWrap = Wrap.Wrap;
            buttonsRow1.style.marginTop = 4;

            var autoBaseBtn = YUCPUIToolkitHelper.CreateButton("Auto Detect Base", AutoDetectBaseRenderer, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            autoBaseBtn.style.flexGrow = 1;
            autoBaseBtn.style.marginRight = 4;
            autoBaseBtn.style.marginBottom = 2;
            buttonsRow1.Add(autoBaseBtn);

            var thisObjBtn = YUCPUIToolkitHelper.CreateButton("Use This Object", UseThisObjectAsAttachment, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            thisObjBtn.style.flexGrow = 1;
            thisObjBtn.style.marginBottom = 2;
            buttonsRow1.Add(thisObjBtn);
            setupContent.Add(buttonsRow1);

            var buttonsRow2 = new VisualElement();
            buttonsRow2.style.flexDirection = FlexDirection.Row;
            buttonsRow2.style.marginTop = 2;

            var selBtn = YUCPUIToolkitHelper.CreateButton("Use Selection", UseSelectionAsAttachment, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            selBtn.style.flexGrow = 1;
            selBtn.style.marginRight = 4;
            buttonsRow2.Add(selBtn);

            var defaultsBtn = YUCPUIToolkitHelper.CreateButton("Reset Defaults", ApplyRecommendedDefaults, YUCPUIToolkitHelper.ButtonVariant.Ghost);
            defaultsBtn.style.flexGrow = 1;
            buttonsRow2.Add(defaultsBtn);
            setupContent.Add(buttonsRow2);

            root.Add(setupCard);

            // --- Options Card ---
            var optionsCard = YUCPUIToolkitHelper.CreateCard("Options", "Core merge and toggle settings.");
            var optionsContent = YUCPUIToolkitHelper.GetCardContent(optionsCard);

            optionsContent.Add(YUCPUIToolkitHelper.CreateField(deleteAttachmentObjectAfterMergeProp, "Delete Attachment After Merge"));
            optionsContent.Add(YUCPUIToolkitHelper.CreateField(remapAnimationsProp, "Remap Animations"));
            optionsContent.Add(YUCPUIToolkitHelper.CreateDivider());

            optionsContent.Add(YUCPUIToolkitHelper.CreateField(remapRendererAndObjectOffToUvDiscardProp, "Convert OFF States to Toggle"));
            optionsContent.Add(YUCPUIToolkitHelper.CreateField(scaleToZeroFallbackProp, "Scale-to-Zero Fallback"));

            var toggleStatus = new VisualElement { name = "toggle-status" };
            optionsContent.Add(toggleStatus);

            root.Add(optionsCard);

            // --- Advanced Foldout ---
            var advancedFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced", showAdvanced);
            advancedFoldout.RegisterValueChangedCallback(evt => showAdvanced = evt.newValue);

            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(renameConflictingAttachmentBlendshapesProp, "Rename Conflicting Blendshapes"));
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(attachmentBlendshapePrefixProp, "Blendshape Prefix"));
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(unmatchedHandlingProp, "Unmatched Surface Handling"));
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateDivider());

            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(remapBlendshapeAnimationsProp, "Remap Blendshape Curves"));
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(remapMaterialAnimationsProp, "Remap Material Curves"));
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateDivider());

            var uvSection = YUCPUIToolkitHelper.CreateSection("UV Discard Settings");
            var uvSectionContent = YUCPUIToolkitHelper.GetSectionContent(uvSection);
            uvSectionContent.Add(YUCPUIToolkitHelper.CreateField(autoDetectUVChannelProp, "Auto Detect UV Channel"));
            uvSectionContent.Add(YUCPUIToolkitHelper.CreateField(uvChannelProp, "UV Channel"));

            var tileRow = new VisualElement();
            tileRow.style.flexDirection = FlexDirection.Row;

            var rowField = YUCPUIToolkitHelper.CreateField(uvDiscardRowProp, "Row");
            rowField.style.flexGrow = 1;
            rowField.style.marginRight = 4;
            tileRow.Add(rowField);

            var colField = YUCPUIToolkitHelper.CreateField(uvDiscardColumnProp, "Column");
            colField.style.flexGrow = 1;
            tileRow.Add(colField);
            uvSectionContent.Add(tileRow);

            advancedFoldout.Add(uvSection);
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateDivider());
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(debugModeProp, "Debug Mode"));

            root.Add(advancedFoldout);

            // --- Build Stats Foldout ---
            var statsFoldout = YUCPUIToolkitHelper.CreateFoldout("Build Stats", showStats);
            statsFoldout.RegisterValueChangedCallback(evt => showStats = evt.newValue);
            var statsContainer = new VisualElement { name = "stats-container" };
            statsFoldout.Add(statsContainer);
            root.Add(statsFoldout);

            UpdateSetupSummary(setupSummary);
            UpdateToggleStatus(toggleStatus);
            UpdateStats(statsContainer);

            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                UpdateSetupSummary(setupSummary);
                UpdateToggleStatus(toggleStatus);
                UpdateStats(statsContainer);
                serializedObject.ApplyModifiedProperties();
            }).Every(500);

            return root;
        }

        private void UpdateSetupSummary(VisualElement container)
        {
            container.Clear();

            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "This component must be inside an avatar hierarchy with a VRCAvatarDescriptor.",
                    YUCPUIToolkitHelper.MessageType.Error));
                return;
            }

            if (data.baseRenderer == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Step 1: assign Base Renderer (usually your body skinned mesh).",
                    YUCPUIToolkitHelper.MessageType.Warning));
                return;
            }

            if (data.baseRenderer.sharedMesh == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Base Renderer has no mesh assigned.",
                    YUCPUIToolkitHelper.MessageType.Error));
                return;
            }

            if (data.attachmentTarget == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Step 2: assign Attachment Target (tail/hair/etc.).",
                    YUCPUIToolkitHelper.MessageType.Warning));
                return;
            }

            if (ResolveAttachmentObject(data.attachmentTarget) == data.baseRenderer.gameObject)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Attachment resolves to the same object as Base Renderer. Choose a different attachment.",
                    YUCPUIToolkitHelper.MessageType.Error));
                return;
            }

            string baseName = data.baseRenderer.name;
            string attachmentName = ResolveAttachmentName(data.attachmentTarget);
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                $"Ready: '{attachmentName}' will merge into '{baseName}'.",
                YUCPUIToolkitHelper.MessageType.Success));
        }

        private void UpdateToggleStatus(VisualElement container)
        {
            container.Clear();

            if (!data.remapRendererAndObjectOffToUvDiscard)
            {
                return;
            }

            bool hasUvDiscardSupport = CheckAttachmentUvDiscardSupport();

            if (hasUvDiscardSupport)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Attachment materials support UV discard. Toggles will use UV discard.",
                    YUCPUIToolkitHelper.MessageType.Success));
            }
            else if (data.attachmentTarget == null)
            {
                return;
            }
            else if (data.scaleToZeroFallback)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Attachment materials lack UV discard support. Toggles will collapse vertices via scale-to-zero blendshape.",
                    YUCPUIToolkitHelper.MessageType.Info));
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Attachment materials lack UV discard support and fallback is disabled. OFF animations will not be converted.",
                    YUCPUIToolkitHelper.MessageType.Warning));
            }
        }

        private bool CheckAttachmentUvDiscardSupport()
        {
            if (data.attachmentTarget == null) return false;

            Renderer renderer = null;

            if (data.attachmentTarget is SkinnedMeshRenderer smr)
            {
                renderer = smr;
            }
            else if (data.attachmentTarget is MeshFilter mf)
            {
                renderer = mf.GetComponent<MeshRenderer>();
            }
            else if (data.attachmentTarget is GameObject go)
            {
                renderer = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (renderer == null)
                {
                    var goMf = go.GetComponentInChildren<MeshFilter>(true);
                    if (goMf != null)
                    {
                        renderer = goMf.GetComponent<MeshRenderer>();
                    }
                }
            }

            if (renderer == null) return false;

            var materials = renderer.sharedMaterials;
            if (materials == null) return false;

            return materials.Any(m => UVManipulator.IsPoiyomiWithUVSupport(m));
        }

        private void UpdateStats(VisualElement container)
        {
            container.Clear();

            AddInfoRow(container, "Merged Vertices", data.MergedVertexCount.ToString());
            AddInfoRow(container, "Merged Submeshes", data.MergedSubmeshCount.ToString());
            AddInfoRow(container, "Merged Blendshapes", data.MergedBlendshapeCount.ToString());
            AddInfoRow(container, "Remapped Curves", data.RemappedCurveCount.ToString());
            AddInfoRow(container, "Warnings", data.WarningCount.ToString());

            if (data.WarningCount > 0 && data.RemapWarnings != null)
            {
                foreach (var warning in data.RemapWarnings.Take(3))
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox(warning, YUCPUIToolkitHelper.MessageType.Warning));
                }
                if (data.RemapWarnings.Count > 3)
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        $"{data.RemapWarnings.Count - 3} more warning(s) were recorded.",
                        YUCPUIToolkitHelper.MessageType.None));
                }
            }
        }

        private void AddInfoRow(VisualElement parent, string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;

            var labelElement = new Label(label);
            labelElement.style.width = 150;
            labelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            labelElement.style.fontSize = 10;
            row.Add(labelElement);

            var valueElement = new Label(value);
            valueElement.style.fontSize = 11;
            row.Add(valueElement);

            parent.Add(row);
        }

        private void AutoDetectBaseRenderer()
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return;
            }

            var all = descriptor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var best = all
                .Where(r => r != null && r.sharedMesh != null)
                .OrderByDescending(r => r.sharedMesh.vertexCount)
                .FirstOrDefault();

            if (best != null)
            {
                Undo.RecordObject(data, "Auto Detect Base Renderer");
                baseRendererProp.objectReferenceValue = best;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
            }
        }

        private void UseThisObjectAsAttachment()
        {
            var go = data.gameObject;
            var attachment = ResolveAttachmentCandidate(go);
            if (attachment == null)
            {
                return;
            }

            Undo.RecordObject(data, "Set Attachment Target");
            attachmentTargetProp.objectReferenceValue = attachment;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
        }

        private void UseSelectionAsAttachment()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                return;
            }

            var attachment = ResolveAttachmentCandidate(go);
            if (attachment == null)
            {
                attachment = go;
            }

            Undo.RecordObject(data, "Set Attachment Target From Selection");
            attachmentTargetProp.objectReferenceValue = attachment;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
        }

        private void ApplyRecommendedDefaults()
        {
            Undo.RecordObject(data, "Apply FBX Merge Recommended Defaults");

            deleteAttachmentObjectAfterMergeProp.boolValue = true;
            renameConflictingAttachmentBlendshapesProp.boolValue = true;
            attachmentBlendshapePrefixProp.stringValue = "Merged_";

            remapAnimationsProp.boolValue = true;
            remapBlendshapeAnimationsProp.boolValue = true;
            remapMaterialAnimationsProp.boolValue = true;
            remapRendererAndObjectOffToUvDiscardProp.boolValue = true;
            scaleToZeroFallbackProp.boolValue = true;

            autoDetectUVChannelProp.boolValue = true;
            uvChannelProp.intValue = 1;
            uvDiscardRowProp.intValue = 3;
            uvDiscardColumnProp.intValue = 3;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
        }

        private static UnityEngine.Object ResolveAttachmentCandidate(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                return smr;
            }

            var mf = go.GetComponent<MeshFilter>();
            if (mf != null)
            {
                return mf;
            }

            var childSmr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (childSmr != null)
            {
                return childSmr;
            }

            var childMf = go.GetComponentInChildren<MeshFilter>(true);
            if (childMf != null)
            {
                return childMf;
            }

            return go;
        }

        private static GameObject ResolveAttachmentObject(UnityEngine.Object attachmentTarget)
        {
            if (attachmentTarget is GameObject go)
            {
                return go;
            }
            if (attachmentTarget is Component c)
            {
                return c.gameObject;
            }
            return null;
        }

        private static string ResolveAttachmentName(UnityEngine.Object attachmentTarget)
        {
            if (attachmentTarget == null)
            {
                return "None";
            }

            if (attachmentTarget is Component c)
            {
                return c.name;
            }

            return attachmentTarget.name;
        }
    }
}
