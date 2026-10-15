using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.Editor;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.Utils;
using YUCP.UI.DesignSystem.Utilities;
using Toolkit = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Attach to Blendshape inspector (DESIGN.md). Generate Preview transfers the tracked blendshapes
    /// for real, so the attachment follows the body in the Scene view; Clear Preview restores every
    /// mesh, weight and pose it changed.
    /// </summary>
    [CustomEditor(typeof(AttachToBlendshapeData))]
    public class AttachToBlendshapeDataEditor : UnityEditor.Editor
    {
        private const int SliderHeight = 22;

        private AttachToBlendshapeData data;
        private YUCPInspector ui;
        private Mesh previewBakeMesh;
        private string previewError;

        private Mesh BodyMesh => data.targetMesh != null ? data.targetMesh.sharedMesh : null;

        private void OnEnable()
        {
            data = (AttachToBlendshapeData)target;
            SceneView.duringSceneGui += DrawPreview;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawPreview;
            if (data != null)
            {
                if (data.previewGenerated) ClearPreview();
                else
                {
                    RestorePreviewBlendshapes();
                    RestorePreviewTransform();
                }
            }
            ReleasePreviewBakeMesh();
        }

        public override VisualElement CreateInspectorGUI()
        {
            data = (AttachToBlendshapeData)target;
            ui = YUCPInspector.Create(this, "Attach to Blendshape");
            var meshes = ui.Section("Meshes");
            ui.Field(meshes, "targetMesh", "Body Mesh");
            ui.Messages(meshes, () => new[] { YUCPInspector.Message.Error(BodyProblem()) });
            ui.Field(meshes, "targetMeshToModify", "Attachment Mesh");
            ui.Messages(meshes, () => new[] { YUCPInspector.Message.Error(AttachmentProblem()) });

            var blendshapes = ui.Section("Blendshapes");
            BuildBlendshapes(blendshapes);
            ui.Messages(blendshapes, () => new[] { YUCPInspector.Message.Error(BodyProblem() == null ? TrackingProblem() : null) });

            var motion = ui.Section("Motion");
            ui.Segmented(motion, "solverMode", "Solver", "Rigid", "Rigid Offset", "Affine");
            ui.Caption(motion, () => data.solverMode switch
            {
                SolverMode.RigidNormalOffset => "Rigid, pushed out along the surface normal. For studs and raised jewelry.",
                SolverMode.Affine => "Also stretches with the skin. For stickers and patches.",
                _ => "Moves and turns with the surface. For piercings and hard jewelry."
            });
            var offset = ui.Indent(motion);
            ui.Field(offset, "normalOffset", "Normal Offset");
            ui.ShowWhen(offset, () => data.solverMode == SolverMode.RigidNormalOffset);
            ui.Field(motion, "alignRotationToSurface", "Align to Surface");

            var output = ui.Section("Output");
            ui.Segmented(output, "outputMode", "Output", "Automatic", "Linked", "Merged");
            ui.Caption(output, () => data.outputMode switch
            {
                AttachToBlendshapeOutputMode.LinkedRendererBake => "Keeps the attachment a separate mesh with its blendshapes linked to the body's.",
                AttachToBlendshapeOutputMode.MergeIntoBaseMesh => "Merges the attachment into the body mesh, so visemes and other built-in blendshapes move it.",
                _ => "Chooses Merged or Linked at build time from the avatar's animations."
            });
            var merged = ui.Indent(output);
            ui.Field(merged, "bakeMethod", "Bake Method");
            ui.Field(merged, "visibilityMode", "Visibility");
            ui.ShowWhen(merged, Merges);

            BuildPreview(ui.Section("Preview"));
            BuildAdvanced(ui.Advanced());
            ui.Refresh();
            return ui.Root;
        }

        private bool Merges() => data.outputMode != AttachToBlendshapeOutputMode.LinkedRendererBake;

        private void BuildBlendshapes(VisualElement section)
        {
            ui.Segmented(section, "trackingMode", "Tracking", "All", "Specific", "Visemes", "Smart");
            ui.Caption(section, () => data.trackingMode switch
            {
                BlendshapeTrackingMode.All => "Tracks every body blendshape. For face tracking.",
                BlendshapeTrackingMode.VisemsOnly => "Tracks the avatar's visemes. For lip piercings and mouth jewelry.",
                BlendshapeTrackingMode.Smart => "Tracks the blendshapes that move the surface under the attachment.",
                _ => null
            });

            var specific = ui.Indent(section);
            BuildSpecificList(specific);
            ui.ShowWhen(specific, () => data.trackingMode == BlendshapeTrackingMode.Specific);

            var visemes = new List<string>();
            void FindVisemes() => visemes = data.trackingMode == BlendshapeTrackingMode.VisemsOnly && BodyMesh != null
                ? VRChatVisemeDetector.GetVisemeBlendshapes(data.targetMesh, AvatarRoot())
                : new List<string>();
            FindVisemes();
            ui.OnRefresh(FindVisemes);
            var visemeList = ui.Indent(section);
            ui.ShowWhen(ui.InfoRow(visemeList, "Visemes", () => string.Join(", ", visemes)), () => visemes.Count > 0);
            ui.Messages(visemeList, () => new[]
            {
                YUCPInspector.Message.Warning(BodyMesh != null && visemes.Count == 0
                    ? "No visemes found. Set the avatar's lip sync to viseme blendshapes, or use VRChat's viseme names."
                    : null)
            });
            ui.ShowWhen(visemeList, () => data.trackingMode == BlendshapeTrackingMode.VisemsOnly);

            var smart = ui.Indent(section);
            ui.Field(smart, "smartDetectionThreshold", "Detection Threshold");
            ui.ShowWhen(smart, () => data.trackingMode == BlendshapeTrackingMode.Smart);

            ui.Field(section, "samplesPerBlendshape", "Keyframes per Blendshape");
        }

        /// <summary>Specific blendshapes: picked from the body mesh's own names, so there's nothing to misspell.</summary>
        private void BuildSpecificList(VisualElement parent)
        {
            var names = new List<string>();
            var list = ui.List(parent, names, () =>
            {
                var row = new VisualElement();
                row.AddToClassList("yucp-list-item");
                var name = new Label();
                name.AddToClassList("yucp-list-item-name");
                row.Add(name);
                var missing = new Label("Not on Body Mesh");
                missing.AddToClassList("yucp-badge");
                missing.AddToClassList("yucp-badge-warning");
                row.Add(missing);
                return row;
            }, (row, i) =>
            {
                row.Q<Label>(className: "yucp-list-item-name").text = names[i];
                var onBody = BodyMesh != null && BodyMesh.GetBlendShapeIndex(names[i]) >= 0;
                row.Q<Label>(className: "yucp-badge").style.display = onBody ? DisplayStyle.None : DisplayStyle.Flex;
            },
                "Add the body blendshapes that move the attachment.",
                AddSpecificBlendshape,
                i => EditSpecificBlendshapes(p => p.DeleteArrayElementAtIndex(i)),
                "Add Blendshape", "Remove Blendshape");
            list.View.itemIndexChanged += (_, __) => EditSpecificBlendshapes(p =>
            {
                p.arraySize = names.Count;
                for (var i = 0; i < names.Count; i++) p.GetArrayElementAtIndex(i).stringValue = names[i];
            });

            string shown = null;
            ui.OnRefresh(() =>
            {
                var current = data.specificBlendshapes ?? new List<string>();
                var key = (BodyMesh != null ? BodyMesh.GetInstanceID() : 0) + "\n" + string.Join("\n", current);
                if (key == shown) return;
                shown = key;
                names.Clear();
                names.AddRange(current);
                list.Refresh();
            });
        }

        private void AddSpecificBlendshape()
        {
            var mesh = BodyMesh;
            var menu = new GenericMenu();
            if (mesh == null || mesh.blendShapeCount == 0)
                menu.AddDisabledItem(new GUIContent("Assign a body mesh with blendshapes first"));
            else
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    if (data.specificBlendshapes != null && data.specificBlendshapes.Contains(name))
                        menu.AddDisabledItem(new GUIContent(name), true);
                    else
                        menu.AddItem(new GUIContent(name), false, () => EditSpecificBlendshapes(p =>
                        {
                            p.arraySize++;
                            p.GetArrayElementAtIndex(p.arraySize - 1).stringValue = name;
                        }));
                }
            menu.ShowAsContext();
        }

        private void EditSpecificBlendshapes(Action<SerializedProperty> edit)
        {
            var so = ui.Object;
            so.Update();
            edit(so.FindProperty("specificBlendshapes"));
            so.ApplyModifiedProperties();
            ui.Refresh();
        }

        private void BuildPreview(VisualElement section)
        {
            var row = ui.ButtonRow(section);
            var generate = ui.Button(row, "Generate Preview", () => { GeneratePreview(); ui.Refresh(); },
                Toolkit.ButtonVariant.Primary, "Transfer the blendshapes and show the attachment following the body in the Scene view.");
            ui.ShowWhen(generate, () => !data.previewGenerated);
            ui.EnableWhen(generate, ValidateData);
            var clear = ui.Button(row, "Clear Preview", () => { ClearPreview(); ui.Refresh(); },
                Toolkit.ButtonVariant.Secondary, "Restore the meshes, the blendshape weights and the attachment's position.");
            ui.ShowWhen(clear, () => data.previewGenerated);
            ui.Messages(section, () => new[] { YUCPInspector.Message.Error(previewError) });
            ui.Caption(section, () => data.previewGenerated
                ? $"Tracking {data.previewBlendshapes?.Count ?? 0} blendshapes on {data.previewCluster?.anchors.Count ?? 0} surface triangles."
                : "To check how the attachment follows the body, generate a preview.");

            var weights = ui.Group(section, "Blendshape weights");
            ui.ShowWhen(weights, () => data.previewGenerated);
            var shownSet = new SegmentedField("Show", new[] { "Tracked", "Body", "Attachment" })
            {
                tooltip = "Tracked: the blendshapes the attachment follows. Body: every body blendshape. Attachment: the attachment mesh's blendshapes."
            };
            shownSet.SetValueWithoutNotify(0);
            weights.Add(shownSet);
            var names = new List<string>();
            var sliders = new ListView(names, SliderHeight, () =>
            {
                var slider = new Slider(0f, 100f) { showInputField = true };
                slider.AddToClassList(BaseField<float>.alignedFieldUssClassName);
                slider.RegisterValueChangedCallback(e =>
                {
                    if (slider.userData is string name) ApplyPreviewBlendshapeWeight(name, e.newValue);
                });
                return slider;
            }, (element, i) =>
            {
                var slider = (Slider)element;
                slider.userData = names[i];
                slider.label = names[i];
                slider.SetValueWithoutNotify(data.previewBlendshapeWeights != null && data.previewBlendshapeWeights.TryGetValue(names[i], out var weight)
                    ? weight
                    : GetCurrentBlendshapeWeight(names[i]));
            }) { selectionType = SelectionType.None };
            sliders.AddToClassList("yucp-list");
            weights.Add(sliders);
            var actions = ui.ButtonRow(weights);
            ui.Button(actions, "Restore Weights", () => { RestorePreviewBlendshapes(); sliders.RefreshItems(); },
                Toolkit.ButtonVariant.Secondary, "Set the blendshapes back to their weights before the preview.");
            ui.Button(actions, "Zero Tracked Weights", () => { ZeroPreviewBlendshapes(); sliders.RefreshItems(); },
                Toolkit.ButtonVariant.Secondary, "Set the tracked blendshapes to 0.");

            string shown = null;
            void UpdateSliders()
            {
                var attachment = PreviewAttachmentMesh();
                var key = $"{data.previewGenerated}|{shownSet.value}|{data.previewBlendshapes?.Count}|" +
                          $"{(BodyMesh != null ? BodyMesh.GetInstanceID() : 0)}|{(attachment != null ? attachment.GetInstanceID() : 0)}";
                if (key == shown) return;
                shown = key;
                names.Clear();
                if (data.previewGenerated)
                {
                    var mesh = shownSet.value == 1 ? BodyMesh : shownSet.value == 2 ? attachment : null;
                    if (shownSet.value == 0) names.AddRange(data.previewBlendshapes ?? new List<string>());
                    else if (mesh != null) names.AddRange(Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName));
                }
                // ponytail: list height is the only inline geometry; ten rows, then it scrolls.
                sliders.style.height = Mathf.Clamp(names.Count, 1, 10) * SliderHeight + 2;
                sliders.RefreshItems();
            }
            shownSet.RegisterValueChangedCallback(_ => UpdateSliders());
            ui.OnRefresh(UpdateSliders);
        }

        /// <summary>The attachment's mesh as the preview sees it: the working copy once generated.</summary>
        private Mesh PreviewAttachmentMesh()
        {
            if (data.previewWorkingMesh != null) return data.previewWorkingMesh;
            if (data.previewOriginalTargetMesh != null) return data.previewOriginalTargetMesh;
            if (data.targetMeshToModify is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (data.targetMeshToModify is MeshFilter mf) return mf.sharedMesh;
            if (data.targetMeshToModify is GameObject go)
            {
                var filter = go.GetComponent<MeshFilter>();
                if (filter != null) return filter.sharedMesh;
                var skinned = go.GetComponent<SkinnedMeshRenderer>();
                return skinned != null ? skinned.sharedMesh : null;
            }
            return null;
        }

        private void BuildAdvanced(VisualElement advanced)
        {
            var surface = ui.Group(advanced, "Surface detection");
            ui.Field(surface, "clusterTriangleCount", "Triangle Count");
            ui.Field(surface, "searchRadius", "Search Radius");
            ui.Field(surface, "manualTriangleIndex", "Manual Triangle");

            var bone = ui.Group(advanced, "Bone");
            ui.Field(bone, "attachToClosestBone", "Attach to Closest Bone");
            var boneSearch = ui.Indent(bone);
            ui.Field(boneSearch, "boneSearchRadius", "Search Radius");
            ui.Field(boneSearch, "boneNameFilter", "Name Filter");
            ui.Field(boneSearch, "ignoreHumanoidBones", "Ignore Humanoid Bones");
            ui.Field(boneSearch, "boneOffset", "Bone Offset");
            ui.ShowWhen(boneSearch, () => data.attachToClosestBone);

            var automatic = ui.Group(advanced, "Automatic output");
            ui.Field(automatic, "autoAvoidMergeIfComplexTransformAnimation", "Avoid Merge with Transform Animation");
            ui.Field(automatic, "autoAvoidMergeIfMaterialAnimation", "Avoid Merge with Material Animation");
            ui.ShowWhen(automatic, () => data.outputMode == AttachToBlendshapeOutputMode.Auto);

            var merging = ui.Group(advanced, "Merging");
            ui.Field(merging, "unmatchedHandling", "Unmatched Vertices");
            ui.Field(merging, "disableOriginalRendererOnMerge", "Disable Original Renderer");
            ui.Field(merging, "remapVisibilityAnimations", "Remap Animations");
            ui.ShowWhen(merging, Merges);

            var uv = ui.Group(advanced, "UV discard");
            ui.Segmented(uv, "autoDetectUVChannel", "UV Channel", "Manual", "Automatic");
            var channel = ui.Indent(uv);
            ui.Field(channel, "uvChannel", "Channel");
            ui.ShowWhen(channel, () => !data.autoDetectUVChannel);
            ui.Field(uv, "uvDiscardRow", "Tile Row");
            ui.Field(uv, "uvDiscardColumn", "Tile Column");
            ui.ShowWhen(uv, () => Merges() && data.visibilityMode == AttachToBlendshapeVisibilityMode.UVDiscard);

            var preview = ui.Group(advanced, "Preview");
            ui.Field(preview, "showPreview", "Show Surface in Scene");
            ui.Field(preview, "rotationSmoothingFactor", "Rotation Smoothing");

            ui.Field(advanced, "debugMode", "Verbose Logging");
            ui.BuildSummary(BuildSummary);
        }

        private string BuildSummary()
        {
            var tracked = data.TrackedBlendshapes;
            if (tracked == null || tracked.Count == 0) return null;
            var output = ObjectNames.NicifyVariableName(data.AppliedOutputMode.ToString()) + ", " +
                         ObjectNames.NicifyVariableName(data.AppliedVisibilityMode.ToString()) +
                         (string.IsNullOrEmpty(data.GeneratedVisibilityBlendshape) ? "" : $" ({data.GeneratedVisibilityBlendshape})");
            var parts = new List<string>
            {
                $"{tracked.Count} blendshapes tracked, {data.TransferredBlendshapeCount} transferred, {data.GeneratedBlendshapeCount} generated",
                output,
                $"{data.RemappedCurveCount} curves remapped",
                "Bone: " + (string.IsNullOrEmpty(data.SelectedBonePath) ? "none" : data.SelectedBonePath)
            };
            if (data.DetectedCluster != null)
                parts.Add($"{data.DetectedCluster.anchors.Count} surface triangles at {data.DetectedCluster.centerPosition.ToString("F3")}");
            return string.Join(". ", parts) + ".";
        }

        private GameObject AvatarRoot()
        {
            var descriptor = data.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            return descriptor != null ? descriptor.gameObject : null;
        }

        private void Log(string message)
        {
            if (data.debugMode) Debug.Log("[AttachToBlendshape Preview] " + message, data);
        }

        // ---- Preview pipeline --------------------------------------------------------------------

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

            int sourceIndex = data.targetMesh.sharedMesh.GetBlendShapeIndex(name);
            if (sourceIndex >= 0)
            {
                // Remember the weight before the preview first moves it, so any body slider is restored, not only tracked ones.
                if (data.previewOriginalWeights != null && !data.previewOriginalWeights.ContainsKey(name))
                    data.previewOriginalWeights[name] = data.targetMesh.GetBlendShapeWeight(sourceIndex);
                data.targetMesh.SetBlendShapeWeight(sourceIndex, value);
            }

            data.previewBlendshapeWeights[name] = value;

            if (data.previewUsesMergedMesh)
            {
                EditorUtility.SetDirty(data.targetMesh);
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }

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

            if (targetMeshFilter != null && data.previewTempSkinnedMesh != null)
            {
                targetSkinnedMesh = data.previewTempSkinnedMesh;
            }

            if (targetSkinnedMesh != null && data.previewWorkingMesh != null && targetSkinnedMesh.sharedMesh != data.previewWorkingMesh)
            {
                Log($"Updating SkinnedMeshRenderer to use working mesh before applying weight. Current: {targetSkinnedMesh.sharedMesh.name}, Working: {data.previewWorkingMesh.name}");
                targetSkinnedMesh.sharedMesh = data.previewWorkingMesh;
            }

            if (targetSkinnedMesh != null && targetSkinnedMesh.sharedMesh != null)
            {
                int targetIndex = targetSkinnedMesh.sharedMesh.GetBlendShapeIndex(name);
                if (targetIndex >= 0)
                {
                    targetSkinnedMesh.SetBlendShapeWeight(targetIndex, value);
                    Log($"Applied blendshape '{name}' weight {value} to target mesh '{targetSkinnedMesh.name}' (index: {targetIndex}, mesh: {targetSkinnedMesh.sharedMesh.name}, blendShapeCount: {targetSkinnedMesh.sharedMesh.blendShapeCount})");

                    EditorUtility.SetDirty(targetSkinnedMesh);
                    EditorUtility.SetDirty(targetSkinnedMesh.sharedMesh);

                    targetSkinnedMesh.enabled = false;
                    targetSkinnedMesh.enabled = true;

                    if (targetSkinnedMesh.gameObject.scene.IsValid())
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(targetSkinnedMesh.gameObject.scene);
                    }

                    UnityEditor.SceneView.RepaintAll();
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                }
                else if (data.debugMode) // Expected for body-only blendshapes moved from the Body sliders.
                {
                    Debug.LogWarning($"[AttachToBlendshape Preview] Blendshape '{name}' not found on target mesh '{targetSkinnedMesh.name}' (mesh: {targetSkinnedMesh.sharedMesh.name}, blendShapeCount: {targetSkinnedMesh.sharedMesh.blendShapeCount})", data);

                    if (targetSkinnedMesh.sharedMesh.blendShapeCount > 0)
                    {
                        var allBlendshapes = new System.Text.StringBuilder();
                        for (int i = 0; i < targetSkinnedMesh.sharedMesh.blendShapeCount; i++)
                        {
                            allBlendshapes.Append($"{targetSkinnedMesh.sharedMesh.GetBlendShapeName(i)}, ");
                        }
                        Log($"Available blendshapes on target mesh: {allBlendshapes.ToString()}");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[AttachToBlendshape Preview] Target SkinnedMeshRenderer is null or has no mesh. targetSkinnedMesh={targetSkinnedMesh != null}, targetMeshFilter={targetMeshFilter != null}, previewTempSkinnedMesh={data.previewTempSkinnedMesh != null}, previewWorkingMesh={data.previewWorkingMesh != null}", data);
            }

            UpdatePreviewAttachmentPose();

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

            if (bakeMesh.triangles == null || bakeMesh.triangles.Length == 0)
            {
                bakeMesh.triangles = data.targetMesh.sharedMesh.triangles;
            }

            SurfaceClusterDetector.EvaluateCluster(
                data.previewCluster,
                bakeMesh.vertices,
                bakeMesh.triangles,
                out data.previewBasePosition,
                out data.previewBaseNormal,
                out data.previewBaseTangent);

            data.previewBaseCaptured = true;
            data.previewHasLastTangent = false;

            try
            {
                var baseSolver = SolveClusterPose(
                    data.previewBasePosition,
                    data.previewBaseNormal,
                    data.previewBaseTangent,
                    null);

                if (baseSolver.success)
                {
                    data.previewBaseSolverPosition = baseSolver.position;
                    data.previewBaseSolverRotation = baseSolver.rotation;

                    data.previewPositionOffset = Quaternion.Inverse(baseSolver.rotation) *
                                                (data.transform.localPosition - baseSolver.position);

                    data.previewRotationOffset = Quaternion.Inverse(baseSolver.rotation) *
                                                data.transform.localRotation;

                    data.previewHasBaseSolver = true;
                }
                else
                {
                    data.previewHasBaseSolver = false;
                }
            }
            catch
            {
                data.previewHasBaseSolver = false;
            }

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
            if (data != null && data.previewUsesMergedMesh)
            {
                return;
            }

            if (data == null ||
                !data.previewGenerated ||
                data.previewCluster == null ||
                data.targetMesh == null ||
                data.targetMesh.sharedMesh == null)
            {
                return;
            }

            SkinnedMeshRenderer targetSkinnedMesh = null;
            MeshFilter targetMeshFilter = null;
            Mesh targetMeshInstance = null;

            if (data.previewWorkingMesh != null)
            {
                targetMeshInstance = data.previewWorkingMesh;
            }

            if (data.targetMeshToModify is SkinnedMeshRenderer smr)
            {
                targetSkinnedMesh = smr;

                if (targetMeshInstance == null)
                {
                    targetMeshInstance = smr.sharedMesh;
                }
            }
            else if (data.targetMeshToModify is MeshFilter mf)
            {
                targetMeshFilter = mf;

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

            if (data.previewWorkingMesh != null && targetMeshInstance != data.previewWorkingMesh)
            {
                Log($"Switching to working mesh. Original: {targetMeshInstance.name} ({targetMeshInstance.blendShapeCount} blendshapes), Working: {data.previewWorkingMesh.name} ({data.previewWorkingMesh.blendShapeCount} blendshapes)");
                targetMeshInstance = data.previewWorkingMesh;
            }

            SkinnedMeshRenderer activeSkinnedMesh = targetSkinnedMesh;

            if (targetMeshFilter != null)
            {
                if (data.previewTempSkinnedMesh == null)
                {

                    data.previewOriginalMeshFilter = targetMeshFilter;

                    var meshRenderer = targetMeshFilter.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        data.previewOriginalMeshRenderer = meshRenderer;
                        Log($"Stored MeshRenderer reference '{meshRenderer.name}' before creating temp SkinnedMeshRenderer");
                    }
                    else
                    {
                        Debug.LogWarning($"[AttachToBlendshape Preview] No MeshRenderer found on '{targetMeshFilter.name}' - will not be able to restore it", data);
                    }

                    data.previewTempSkinnedMesh = targetMeshFilter.gameObject.AddComponent<SkinnedMeshRenderer>();
                    data.previewTempSkinnedMesh.sharedMesh = targetMeshInstance;
                    Log($"Created temp SkinnedMeshRenderer with mesh '{targetMeshInstance.name}' (blendShapeCount: {targetMeshInstance.blendShapeCount})");

                    if (meshRenderer != null)
                    {
                        if (meshRenderer.sharedMaterial != null)
                        {
                            data.previewTempSkinnedMesh.sharedMaterial = meshRenderer.sharedMaterial;
                        }

                        meshRenderer.enabled = false;
                        Log($"Disabled MeshRenderer '{meshRenderer.name}'");
                    }
                    data.previewTempSkinnedMesh.enabled = true;
                }

                activeSkinnedMesh = data.previewTempSkinnedMesh;

                if (data.previewWorkingMesh != null && activeSkinnedMesh.sharedMesh != data.previewWorkingMesh)
                {
                    Log($"Updating temp SkinnedMeshRenderer to use working mesh. Current: {activeSkinnedMesh.sharedMesh.name} ({activeSkinnedMesh.sharedMesh.blendShapeCount} blendshapes), Working: {data.previewWorkingMesh.name} ({data.previewWorkingMesh.blendShapeCount} blendshapes)");
                    activeSkinnedMesh.sharedMesh = data.previewWorkingMesh;
                }
            }
            else if (targetSkinnedMesh != null)
            {

                if (data.previewWorkingMesh != null && targetSkinnedMesh.sharedMesh != data.previewWorkingMesh)
                {
                    Log($"Updating SkinnedMeshRenderer to use working mesh. Current: {targetSkinnedMesh.sharedMesh.name} ({targetSkinnedMesh.sharedMesh.blendShapeCount} blendshapes), Working: {data.previewWorkingMesh.name} ({data.previewWorkingMesh.blendShapeCount} blendshapes)");
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

                UnityEditor.EditorUtility.SetDirty(activeSkinnedMesh);

                activeSkinnedMesh.enabled = false;
                activeSkinnedMesh.enabled = true;

                if (activeSkinnedMesh.gameObject.scene.IsValid())
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeSkinnedMesh.gameObject.scene);
                }

                if (activeSkinnedMesh.sharedMesh != null)
                {
                    UnityEditor.EditorUtility.SetDirty(activeSkinnedMesh.sharedMesh);
                }
            }
            else if (data.debugMode)
            {
                Debug.LogWarning("[Preview] No active SkinnedMeshRenderer found for preview", data);
            }

            if (!data.previewBaseCaptured)
            {

                CapturePreviewBasePose();
            }

            var bakeMesh = GetPreviewBakeMesh();
            data.targetMesh.BakeMesh(bakeMesh);
            if (bakeMesh.triangles == null || bakeMesh.triangles.Length == 0)
            {
                bakeMesh.triangles = data.targetMesh.sharedMesh.triangles;
            }

            SurfaceClusterDetector.EvaluateCluster(
                data.previewCluster,
                bakeMesh.vertices,
                bakeMesh.triangles,
                out var clusterPos,
                out var clusterNormal,
                out var clusterTangent);

            Vector3? prevTan = data.previewHasLastTangent ? data.previewLastTangent : (Vector3?)null;
            var solver = SolveClusterPose(clusterPos, clusterNormal, clusterTangent, prevTan);
            if (solver.success)
            {

                data.previewLastTangent = clusterTangent;
                data.previewHasLastTangent = true;

                if (!data.previewHasBaseSolver)
                {
                    data.previewBaseSolverRotation = solver.rotation;
                    data.previewHasBaseSolver = true;
                }

                Vector3 currentClusterWorldPos = data.targetMesh.transform.TransformPoint(clusterPos);
                Vector3 baseClusterWorldPos = data.targetMesh.transform.TransformPoint(data.previewBasePosition);

                Vector3 clusterWorldDelta = currentClusterWorldPos - baseClusterWorldPos;

                Vector3 clusterLocalDelta;
                if (data.transform.parent != null)
                {
                    Vector3 newWorldPos = data.transform.parent.TransformPoint(data.previewOriginalLocalPosition) + clusterWorldDelta;
                    clusterLocalDelta = data.transform.parent.InverseTransformPoint(newWorldPos) - data.previewOriginalLocalPosition;
                }
                else
                {
                    clusterLocalDelta = data.transform.InverseTransformDirection(clusterWorldDelta);
                }

                data.transform.localPosition = data.previewOriginalLocalPosition + clusterLocalDelta;

                Quaternion surfaceRotationDelta = solver.rotation * Quaternion.Inverse(data.previewBaseSolverRotation);
                data.transform.localRotation = surfaceRotationDelta * data.previewOriginalLocalRotation;
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

        private void GeneratePreview()
        {
            Log("Generating preview...");
            previewError = null;

            try
            {
                CleanupOrphanedLegacyPreviewRenderer();

                // ponytail: the status area already says what to fix, and the button is disabled meanwhile.
                if (!ValidateData()) return;

                GameObject avatarRoot = AvatarRoot();

                data.previewCluster = SurfaceClusterDetector.DetectCluster(
                    data.targetMesh,
                    data.transform.position,
                    data.clusterTriangleCount,
                    data.searchRadius,
                    data.manualTriangleIndex,
                    AttachToBlendshapeProcessor.DetermineClusterSeedBlendshapes(data, avatarRoot));

                if (data.previewCluster == null)
                {
                    previewError = "No body surface found near the attachment. Move it onto the body, or raise Search Radius in Advanced Options.";
                    return;
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
                        data.previewBlendshapes = VRChatVisemeDetector.DetectActiveBlendshapes(
                            data.targetMesh,
                            data.previewCluster,
                            data.smartDetectionThreshold);
                        break;
                }

                CapturePreviewBlendshapeWeights();

                var processor = new AttachToBlendshapeProcessor();
                AttachToBlendshapeOutputMode previewOutputMode = processor.ResolveOutputMode(
                    data, avatarRoot, data.previewBlendshapes);
                if (previewOutputMode == AttachToBlendshapeOutputMode.MergeIntoBaseMesh)
                {
                    if (!GenerateMergedPreview(avatarRoot)) return;

                    data.previewGenerated = true;
                    data.showPreview = true;
                    SceneView.RepaintAll();
                    Repaint();
                    Log($"Generated merged preview: {data.previewBlendshapes.Count} tracked blendshapes, {data.previewCluster.anchors.Count} triangles");
                    return;
                }

                Log($"Transferring {data.previewBlendshapes.Count} blendshapes to target mesh...");
                bool transferSuccess = BlendshapeTransfer.TransferBlendshapes(
                    data.targetMesh,
                    data.targetMeshToModify,
                    data.previewBlendshapes,
                    data.previewCluster,
                    data);

                if (!transferSuccess)
                {
                    previewError = "Couldn't transfer the blendshapes to the attachment mesh. Turn on Verbose Logging and check the Console.";
                    return;
                }

                Log($"Blendshapes transferred successfully. Target mesh should now have {data.previewBlendshapes.Count} blendshapes.");

                data.previewGenerated = true;
                data.showPreview = true;
                CapturePreviewBasePose();
                UpdatePreviewAttachmentPose();

                SceneView.RepaintAll();
                Repaint();

                Log($"Preview generated: {data.previewBlendshapes.Count} blendshapes, {data.previewCluster.anchors.Count} triangles");
            }
            catch (Exception ex)
            {
                previewError = "Couldn't generate the preview: " + ex.Message;
                Debug.LogError($"[AttachToBlendshape Preview] Failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }

        private void CleanupOrphanedLegacyPreviewRenderer()
        {
            GameObject targetObject = null;
            if (data.targetMeshToModify is MeshFilter filter) targetObject = filter.gameObject;
            else if (data.targetMeshToModify is GameObject go) targetObject = go;
            else if (data.targetMeshToModify == null && data.GetComponent<MeshFilter>() != null) targetObject = data.gameObject;
            if (targetObject == null || targetObject.GetComponent<MeshFilter>() == null) return;

            MeshRenderer meshRenderer = targetObject.GetComponent<MeshRenderer>();
            foreach (SkinnedMeshRenderer candidate in targetObject.GetComponents<SkinnedMeshRenderer>())
            {
                Mesh candidateMesh = candidate.sharedMesh;
                Material[] legacyMaterials = candidate.sharedMaterials;
                bool legacyWorkingMesh = candidateMesh != null &&
                                         candidateMesh.name.EndsWith("_Blendshapes", StringComparison.Ordinal);
                bool unskinnedTemporaryRenderer = candidate.rootBone == null &&
                                                  (candidate.bones == null || candidate.bones.Length == 0);
                if (!legacyWorkingMesh || !unskinnedTemporaryRenderer) continue;

                candidate.sharedMesh = null;
                if (Application.isPlaying) UnityEngine.Object.Destroy(candidate);
                else UnityEngine.Object.DestroyImmediate(candidate);
                if (candidateMesh != null && !AssetDatabase.Contains(candidateMesh))
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(candidateMesh);
                    else UnityEngine.Object.DestroyImmediate(candidateMesh);
                }
                if (meshRenderer == null)
                {
                    meshRenderer = targetObject.AddComponent<MeshRenderer>();
                    meshRenderer.sharedMaterials = legacyMaterials ?? Array.Empty<Material>();
                }
                meshRenderer.enabled = true;
                Log("Removed an orphaned legacy temporary renderer.");
            }
        }

        private bool GenerateMergedPreview(GameObject avatarRoot)
        {
            if (!AttachToBlendshapeProcessor.TryGetTargetMeshToModify(
                    data,
                    out Mesh attachmentMesh,
                    out Transform attachmentTransform,
                    out Material[] attachmentMaterials,
                    out SkinnedMeshRenderer attachmentSmr,
                    out MeshRenderer attachmentMr))
            {
                previewError = "Couldn't find the attachment's mesh and renderer.";
                return false;
            }

            if (attachmentTransform == data.targetMesh.transform)
            {
                previewError = "The attachment mesh is the body mesh. Choose the attachment's own mesh.";
                return false;
            }

            string animationPath = avatarRoot != null
                ? AnimationUtility.CalculateTransformPath(attachmentTransform, avatarRoot.transform)
                : attachmentTransform.name;
            var input = new AttachToBlendshapeMergedMeshBuilder.AttachmentInput
            {
                data = data,
                mesh = attachmentMesh,
                transform = attachmentTransform,
                materials = attachmentMaterials,
                skinnedRenderer = attachmentSmr,
                meshRenderer = attachmentMr,
                cluster = data.previewCluster,
                trackedBlendshapes = new HashSet<string>(data.previewBlendshapes, StringComparer.Ordinal),
                displayName = attachmentTransform.name,
                animationPath = animationPath
            };

            Mesh originalBaseMesh = data.targetMesh.sharedMesh;
            Material[] originalBaseMaterials = data.targetMesh.sharedMaterials;
            var originalWeights = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < originalBaseMesh.blendShapeCount; i++)
                originalWeights[originalBaseMesh.GetBlendShapeName(i)] = data.targetMesh.GetBlendShapeWeight(i);

            var result = AttachToBlendshapeMergedMeshBuilder.Build(
                data.targetMesh, originalBaseMesh, new[] { input });
            if (!result.success)
            {
                previewError = result.error ?? "Couldn't merge the attachment into the body mesh.";
                return false;
            }

            data.previewUsesMergedMesh = true;
            data.previewOriginalBaseMesh = originalBaseMesh;
            data.previewOriginalBaseMaterials = originalBaseMaterials;
            data.previewOriginalAttachmentMaterials = attachmentMaterials;
            data.previewMergedMaterials = result.materials;
            data.previewOriginalMesh = attachmentMesh;
            data.previewOriginalTargetMesh = attachmentMesh;
            data.previewWorkingMesh = result.mesh;
            data.previewMergedSourceSkinnedRenderer = attachmentSmr;
            data.previewMergedSourceMeshRenderer = attachmentMr;
            data.previewMergedSourceRendererEnabled = attachmentSmr != null
                ? attachmentSmr.enabled
                : attachmentMr != null && attachmentMr.enabled;

            try
            {
                data.targetMesh.sharedMesh = result.mesh;
                data.targetMesh.sharedMaterials = result.materials;
                foreach (var pair in originalWeights)
                {
                    int index = result.mesh.GetBlendShapeIndex(pair.Key);
                    if (index >= 0) data.targetMesh.SetBlendShapeWeight(index, pair.Value);
                }

                var attachmentResult = result.attachments.FirstOrDefault();
                if (attachmentResult != null && attachmentResult.defaultHidden &&
                    !string.IsNullOrEmpty(attachmentResult.visibilityBlendshapeName))
                {
                    int visibilityIndex = result.mesh.GetBlendShapeIndex(attachmentResult.visibilityBlendshapeName);
                    if (visibilityIndex >= 0) data.targetMesh.SetBlendShapeWeight(visibilityIndex, 100f);
                }

                if (attachmentSmr != null)
                {
                    attachmentSmr.enabled = false;
                }
                else if (attachmentMr != null)
                {
                    attachmentMr.enabled = false;
                }
            }
            catch
            {
                ClearMergedPreview();
                throw;
            }

            EditorUtility.SetDirty(data.targetMesh);
            return true;
        }

        private void ClearPreview()
        {
            previewError = null;
            RestorePreviewBlendshapes();

            if (data.previewUsesMergedMesh)
            {
                ClearMergedPreview();
                RestorePreviewTransform();
                ResetPreviewState();
                SceneView.RepaintAll();
                Repaint();
                Log("Merged preview cleared and original renderers restored");
                return;
            }

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

            if (data.previewOriginalMesh != null)
            {
                if (targetSkinnedMesh != null)
                {

                    if (data.previewTempSkinnedMesh == null || targetSkinnedMesh != data.previewTempSkinnedMesh)
                    {

                        if (targetSkinnedMesh.sharedMesh == data.previewWorkingMesh ||
                            targetSkinnedMesh.sharedMesh == null ||
                            targetSkinnedMesh.sharedMesh.name.Contains("_Blendshapes"))
                        {
                            targetSkinnedMesh.sharedMesh = data.previewOriginalMesh;
                            Log($"Restored original mesh '{data.previewOriginalMesh.name}' to SkinnedMeshRenderer '{targetSkinnedMesh.name}'");
                        }
                    }
                }
                else if (targetMeshFilter != null)
                {

                    if (targetMeshFilter.sharedMesh == data.previewWorkingMesh ||
                        targetMeshFilter.sharedMesh == null ||
                        (targetMeshFilter.sharedMesh != null && targetMeshFilter.sharedMesh.name.Contains("_Blendshapes")))
                    {
                        targetMeshFilter.sharedMesh = data.previewOriginalMesh;
                        Log($"Restored original mesh '{data.previewOriginalMesh.name}' to MeshFilter '{targetMeshFilter.name}'");
                    }
                }
                else
                {
                    Debug.LogWarning("[AttachToBlendshape Preview] Could not find target mesh component to restore. Mesh may be missing.", data);
                }
            }
            else
            {
                Debug.LogWarning("[AttachToBlendshape Preview] previewOriginalMesh is null - cannot restore mesh. This may cause the mesh to disappear.", data);
            }

            if (data.previewWorkingMesh != null && data.previewWorkingMesh != data.previewOriginalMesh)
            {

                bool meshStillInUse = false;
                if (targetSkinnedMesh != null && targetSkinnedMesh.sharedMesh == data.previewWorkingMesh)
                {
                    meshStillInUse = true;
                    Debug.LogWarning($"[AttachToBlendshape Preview] Working mesh still in use by SkinnedMeshRenderer - skipping destruction", data);
                }
                if (targetMeshFilter != null && targetMeshFilter.sharedMesh == data.previewWorkingMesh)
                {
                    meshStillInUse = true;
                    Debug.LogWarning($"[AttachToBlendshape Preview] Working mesh still in use by MeshFilter - skipping destruction", data);
                }

                if (!meshStillInUse)
                {
                    string meshName = data.previewWorkingMesh.name;
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(data.previewWorkingMesh);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(data.previewWorkingMesh);
                    }
                    Log($"Destroyed working mesh copy '{meshName}'");
                    data.previewWorkingMesh = null;
                }
                else
                {
                    data.previewWorkingMesh = null;
                }
            }

            if (data.previewTempSkinnedMesh != null)
            {

                if (data.previewOriginalMeshFilter != null && data.previewOriginalMesh != null)
                {
                    if (data.previewOriginalMeshFilter.sharedMesh == null ||
                        data.previewOriginalMeshFilter.sharedMesh == data.previewWorkingMesh ||
                        (data.previewOriginalMeshFilter.sharedMesh != null && data.previewOriginalMeshFilter.sharedMesh.name.Contains("_Blendshapes")))
                    {
                        data.previewOriginalMeshFilter.sharedMesh = data.previewOriginalMesh;
                        Log($"Ensured MeshFilter '{data.previewOriginalMeshFilter.name}' has original mesh before cleanup");
                    }
                }

                MeshRenderer meshRenderer = data.previewOriginalMeshRenderer;

                if (meshRenderer == null && data.previewOriginalMeshFilter != null)
                {
                    meshRenderer = data.previewOriginalMeshFilter.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        Log($"Found MeshRenderer via GetComponent (stored reference was null)");
                    }
                }

                if (meshRenderer != null)
                {
                    meshRenderer.enabled = true;
                    Log($"Re-enabled MeshRenderer '{meshRenderer.name}'");
                }
                else if (data.previewOriginalMeshFilter != null)
                {

                    if (data.previewOriginalMeshFilter.gameObject != null)
                    {
                        var existingRenderer = data.previewOriginalMeshFilter.GetComponent<MeshRenderer>();
                        if (existingRenderer == null)
                        {

                            var newMeshRenderer = data.previewOriginalMeshFilter.gameObject.AddComponent<MeshRenderer>();
                            if (newMeshRenderer != null)
                            {

                                if (data.previewTempSkinnedMesh.sharedMaterial != null)
                                {
                                    newMeshRenderer.sharedMaterial = data.previewTempSkinnedMesh.sharedMaterial;
                                }
                                Log($"Recreated MeshRenderer on '{data.previewOriginalMeshFilter.name}' (Unity destroyed the original)");
                            }
                        }
                    }
                }

                data.previewOriginalMeshRenderer = null;

                Log($"Destroying temporary SkinnedMeshRenderer '{data.previewTempSkinnedMesh.name}'");
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

            Log("Preview cleared and original mesh restored");
        }

        private void ClearMergedPreview()
        {
            Mesh generatedMesh = data.previewWorkingMesh;
            Material[] generatedMaterials = data.previewMergedMaterials ?? Array.Empty<Material>();

            if (data.targetMesh != null)
            {
                if (data.previewOriginalBaseMesh != null)
                    data.targetMesh.sharedMesh = data.previewOriginalBaseMesh;
                if (data.previewOriginalBaseMaterials != null)
                    data.targetMesh.sharedMaterials = data.previewOriginalBaseMaterials;
                EditorUtility.SetDirty(data.targetMesh);
            }

            if (data.previewMergedSourceSkinnedRenderer != null)
                data.previewMergedSourceSkinnedRenderer.enabled = data.previewMergedSourceRendererEnabled;
            if (data.previewMergedSourceMeshRenderer != null)
                data.previewMergedSourceMeshRenderer.enabled = data.previewMergedSourceRendererEnabled;

            var originalMaterials = new HashSet<Material>();
            if (data.previewOriginalBaseMaterials != null)
                foreach (Material material in data.previewOriginalBaseMaterials) if (material != null) originalMaterials.Add(material);
            if (data.previewOriginalAttachmentMaterials != null)
                foreach (Material material in data.previewOriginalAttachmentMaterials) if (material != null) originalMaterials.Add(material);
            var destroyedMaterials = new HashSet<Material>();
            foreach (Material material in generatedMaterials)
            {
                if (material == null || originalMaterials.Contains(material) || !destroyedMaterials.Add(material)) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }

            if (generatedMesh != null && generatedMesh != data.previewOriginalBaseMesh)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(generatedMesh);
                else UnityEngine.Object.DestroyImmediate(generatedMesh);
            }

            data.previewUsesMergedMesh = false;
            data.previewOriginalBaseMesh = null;
            data.previewOriginalBaseMaterials = null;
            data.previewOriginalAttachmentMaterials = null;
            data.previewMergedMaterials = null;
            data.previewMergedSourceSkinnedRenderer = null;
            data.previewMergedSourceMeshRenderer = null;
            data.previewWorkingMesh = null;
            data.previewOriginalMesh = null;
            data.previewOriginalTargetMesh = null;
        }

        private void ResetPreviewState()
        {
            data.previewCluster = null;
            data.previewBlendshapes?.Clear();
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
        }

        // ---- Validation ----------------------------------------------------------------------------

        private bool ValidateData() => GetValidationError() == null;

        /// <summary>What stops the component from working, as an instruction; null when nothing does.</summary>
        private string GetValidationError() => BodyProblem() ?? TrackingProblem() ?? AttachmentProblem();

        private string BodyProblem()
        {
            if (data.targetMesh == null) return "Assign the body mesh.";
            if (data.targetMesh.sharedMesh == null) return "The body mesh's renderer has no mesh. Assign one to it.";
            if (!PoseSampler.HasBlendshapes(data.targetMesh)) return "The body mesh has no blendshapes. Choose the avatar's face or body mesh.";
            return null;
        }

        private string TrackingProblem() =>
            data.trackingMode == BlendshapeTrackingMode.Specific && (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0)
                ? "Add at least one blendshape to track, or choose another tracking mode." : null;

        private string AttachmentProblem()
        {
            var attachment = data.targetMeshToModify;
            if (attachment == null)
            {
                if (data.GetComponent<SkinnedMeshRenderer>() == null && data.GetComponent<MeshFilter>() == null)
                    return "Assign the attachment mesh, or add this component to the attachment's object.";
            }
            else if (!(attachment is SkinnedMeshRenderer || attachment is MeshFilter ||
                       attachment is GameObject go && (go.GetComponent<SkinnedMeshRenderer>() != null || go.GetComponent<MeshFilter>() != null)))
            {
                return "The attachment mesh needs a Skinned Mesh Renderer or a Mesh Filter.";
            }
            return null;
        }

        // ---- Scene view ------------------------------------------------------------------------------

        /// <summary>The surface the preview follows. Drawn from duringSceneGui so it shows with gizmos off.</summary>
        private void DrawPreview(SceneView view)
        {
            if (data == null || !data.showPreview || !data.previewGenerated || data.previewCluster == null) return;
            var mesh = BodyMesh;
            if (mesh == null) return;
            var body = data.targetMesh.transform;

            if (Event.current.type == EventType.Repaint)
            {
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                Handles.color = new Color(0f, 1f, 0f, 0.3f);
                foreach (var anchor in data.previewCluster.anchors)
                    Handles.DrawAAConvexPolygon(
                        body.TransformPoint(vertices[triangles[anchor.triIndex * 3]]),
                        body.TransformPoint(vertices[triangles[anchor.triIndex * 3 + 1]]),
                        body.TransformPoint(vertices[triangles[anchor.triIndex * 3 + 2]]));

                var center = body.TransformPoint(data.previewCluster.centerPosition);
                Handles.color = Color.yellow;
                Handles.SphereHandleCap(0, center, Quaternion.identity, 0.01f, EventType.Repaint);
                Handles.color = YUCPSceneOverlay.Accent;
                Handles.DrawLine(center, center + body.TransformDirection(data.previewCluster.averageNormal) * 0.05f);
            }

            YUCPSceneOverlay.Draw("Attach to Blendshape preview",
                $"Green: {data.previewCluster.anchors.Count} surface triangles. Yellow: center. Teal: normal. " +
                $"{data.previewBlendshapes?.Count ?? 0} blendshapes tracked.");
        }
    }
}
