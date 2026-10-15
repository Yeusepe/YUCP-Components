using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.UI.DesignSystem.Utilities;
using Message = YUCP.UI.DesignSystem.Utilities.YUCPInspector.Message;
using Variant = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper.ButtonVariant;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(FbxMergeData))]
    public class FbxMergeDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (FbxMergeData)target;
            var ui = YUCPInspector.Create(this, "Mesh Merger");
            ui.Status(() => new[] { AvatarOf(data) == null ? Message.Error("Put this inside an avatar.") : default });

            var meshes = ui.Section("Meshes");
            ui.Field(meshes, "baseRenderer", "Base Mesh");
            ui.Messages(meshes, () => new[] { BaseProblem(data) });
            ui.Field(meshes, "attachmentTarget", "Attachment");
            ui.Messages(meshes, () => new[] { AttachmentProblem(data) });
            ui.Field(meshes, "deleteAttachmentObjectAfterMerge", "Delete Attachment After Merge");
            var pick = ui.ButtonRow(meshes);
            ui.EnableWhen(ui.Button(pick, "Detect Base Mesh", () => DetectBaseMesh(data), Variant.Secondary,
                "Uses the avatar's skinned mesh with the most vertices."), () => AvatarOf(data) != null);
            // One button covers "this object" and "another object": with the Inspector unlocked the selection is this object.
            ui.Button(pick, "Use Selection", () => UseSelection(data), Variant.Secondary,
                "Uses the selected object's mesh as the attachment. Lock the Inspector to pick another object.");

            var animations = ui.Section("Animations");
            ui.Field(animations, "remapAnimations");
            var curves = ui.ShowWhen(ui.Indent(animations), () => data.remapAnimations);
            ui.Field(curves, "remapBlendshapeAnimations", "Remap Blendshape Curves");
            ui.Field(curves, "remapMaterialAnimations", "Remap Material Curves");
            ui.Field(animations, "remapRendererAndObjectOffToUvDiscard", "Convert Off States to UV Discard");
            var fallback = ui.ShowWhen(ui.Indent(animations), () => data.remapRendererAndObjectOffToUvDiscard);
            ui.Field(fallback, "scaleToZeroFallback", "Scale-to-Zero Fallback");
            ui.Caption(fallback, () => data.attachmentTarget == null ? ""
                : SupportsUvDiscard(data) ? "The attachment's materials support UV discard, so toggles use it."
                : data.scaleToZeroFallback ? "The attachment's materials don't support UV discard, so toggles shrink it with a blendshape."
                : "");
            ui.Messages(fallback, () => new[]
            {
                data.attachmentTarget != null && !data.scaleToZeroFallback && !SupportsUvDiscard(data)
                    ? Message.Warning("The attachment's materials don't support UV discard, so its off animations won't be converted. Turn on Scale-to-Zero Fallback.")
                    : default
            });

            var advanced = ui.Advanced();
            ui.Field(advanced, "renameConflictingAttachmentBlendshapes", "Rename Conflicting Blendshapes");
            var prefix = ui.ShowWhen(ui.Indent(advanced), () => data.renameConflictingAttachmentBlendshapes);
            ui.Field(prefix, "attachmentBlendshapePrefix", "Blendshape Prefix");
            ui.Field(advanced, "unmatchedHandling", "Unmatched Surface Handling");
            var uv = ui.Group(advanced, "UV discard");
            ui.Field(uv, "autoDetectUVChannel", "Detect UV Channel");
            var channel = ui.ShowWhen(ui.Indent(uv), () => !data.autoDetectUVChannel);
            ui.Field(channel, "uvChannel", "UV Channel");
            ui.Field(uv, "uvDiscardRow", "Discard Row");
            ui.Field(uv, "uvDiscardColumn", "Discard Column");
            ui.Field(advanced, "debugMode", "Verbose Logging");
            ui.Button(ui.ButtonRow(advanced), "Restore Defaults", RestoreDefaults, Variant.Secondary,
                "Resets the merge, animation and UV discard options to their recommended values.");

            var build = ui.ShowWhen(ui.Group(advanced, "Last build"), () => data.MergedVertexCount > 0 || data.WarningCount > 0);
            ui.InfoRow(build, "Merged Vertices", () => data.MergedVertexCount.ToString());
            ui.InfoRow(build, "Merged Submeshes", () => data.MergedSubmeshCount.ToString());
            ui.InfoRow(build, "Merged Blendshapes", () => data.MergedBlendshapeCount.ToString());
            ui.InfoRow(build, "Remapped Curves", () => data.RemappedCurveCount.ToString());
            ui.InfoRow(build, "Warnings", () => data.WarningCount.ToString());
            ui.Messages(build, () => data.RemapWarnings?.Take(3).Select(Message.Warning));
            ui.Caption(build, () => data.RemapWarnings != null && data.RemapWarnings.Count > 3
                ? $"{data.RemapWarnings.Count - 3} more warnings aren't shown."
                : "");
            return ui.Root;
        }

        private static VRCAvatarDescriptor AvatarOf(FbxMergeData data) => data.GetComponentInParent<VRCAvatarDescriptor>();

        private static Message BaseProblem(FbxMergeData data)
        {
            if (data.baseRenderer == null) return Message.Warning("Assign the mesh to merge into, usually the body.");
            if (data.baseRenderer.sharedMesh == null) return Message.Error("This renderer has no mesh. Assign its mesh, or choose another renderer.");
            return default;
        }

        private static Message AttachmentProblem(FbxMergeData data)
        {
            if (data.attachmentTarget == null) return Message.Warning("Assign the attachment to merge, such as a tail or hair.");
            if (data.baseRenderer != null && ResolveAttachmentObject(data.attachmentTarget) == data.baseRenderer.gameObject)
                return Message.Error("The attachment is the base mesh. Choose a different attachment.");
            return default;
        }

        private static bool SupportsUvDiscard(FbxMergeData data)
        {
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
                    if (goMf != null) renderer = goMf.GetComponent<MeshRenderer>();
                }
            }

            var materials = renderer != null ? renderer.sharedMaterials : null;
            return materials != null && materials.Any(UVManipulator.IsPoiyomiWithUVSupport);
        }

        private void DetectBaseMesh(FbxMergeData data)
        {
            var avatar = AvatarOf(data);
            if (avatar == null) return;
            var best = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r != null && r.sharedMesh != null)
                .OrderByDescending(r => r.sharedMesh.vertexCount)
                .FirstOrDefault();
            if (best == null) return;

            serializedObject.Update();
            serializedObject.FindProperty("baseRenderer").objectReferenceValue = best;
            serializedObject.ApplyModifiedProperties();
        }

        private void UseSelection(FbxMergeData data)
        {
            var go = Selection.activeGameObject != null ? Selection.activeGameObject : data.gameObject;
            serializedObject.Update();
            serializedObject.FindProperty("attachmentTarget").objectReferenceValue = ResolveAttachmentCandidate(go);
            serializedObject.ApplyModifiedProperties();
        }

        private void RestoreDefaults()
        {
            serializedObject.Update();
            void Set(string path, bool value) => serializedObject.FindProperty(path).boolValue = value;
            Set("deleteAttachmentObjectAfterMerge", true);
            Set("renameConflictingAttachmentBlendshapes", true);
            serializedObject.FindProperty("attachmentBlendshapePrefix").stringValue = "Merged_";
            Set("remapAnimations", true);
            Set("remapBlendshapeAnimations", true);
            Set("remapMaterialAnimations", true);
            Set("remapRendererAndObjectOffToUvDiscard", true);
            Set("scaleToZeroFallback", true);
            Set("autoDetectUVChannel", true);
            serializedObject.FindProperty("uvChannel").intValue = 1;
            serializedObject.FindProperty("uvDiscardRow").intValue = 3;
            serializedObject.FindProperty("uvDiscardColumn").intValue = 3;
            serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("Restore Mesh Merger Defaults");
        }

        /// <summary>The object's own skinned mesh or mesh filter, else its first child's, else the object itself.</summary>
        private static Object ResolveAttachmentCandidate(GameObject go)
        {
            if (go.TryGetComponent<SkinnedMeshRenderer>(out var smr)) return smr;
            if (go.TryGetComponent<MeshFilter>(out var mf)) return mf;
            var childSmr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (childSmr != null) return childSmr;
            var childMf = go.GetComponentInChildren<MeshFilter>(true);
            if (childMf != null) return childMf;
            return go;
        }

        private static GameObject ResolveAttachmentObject(Object attachmentTarget) => attachmentTarget switch
        {
            GameObject go => go,
            Component c => c.gameObject,
            _ => null
        };
    }
}
