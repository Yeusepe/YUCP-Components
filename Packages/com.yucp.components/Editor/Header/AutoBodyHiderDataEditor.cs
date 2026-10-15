using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;
using YUCP.UI.DesignSystem.Utilities;
using Message = YUCP.UI.DesignSystem.Utilities.YUCPInspector.Message;
using Variant = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper.ButtonVariant;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Auto Body Hider: the meshes, how covered body parts are found and hidden, the toggle that turns
    /// hiding on and off, and a Scene view preview of the hidden faces.
    /// </summary>
    [CustomEditor(typeof(AutoBodyHiderData))]
    public class AutoBodyHiderDataEditor : UnityEditor.Editor
    {
        private AutoBodyHiderData data;
        private YUCPInspector ui;
        private string detectionKey;
        private string toggleError;
        private string cacheResult;
        private bool[] countedFaces;
        private int hiddenFaces;

        // Scene state the toggle section reads, scanned once per refresh.
        private List<Component> toggles = new List<Component>();
        private Component ownToggle;
        private bool hasUVDiscardToggle;

        private void OnEnable()
        {
            data = (AutoBodyHiderData)target;
            detectionKey = DetectionKey();
            SceneView.duringSceneGui += DrawPreview;
        }

        private void OnDisable() => SceneView.duringSceneGui -= DrawPreview;

        public override VisualElement CreateInspectorGUI()
        {
            ui = YUCPInspector.Create(this, "Auto Body Hider");
            ScanScene();
            ui.OnRefresh(ScanScene);
            ui.OnRefresh(KeepPreviewCurrent);
            var meshes = ui.Section("Meshes");
            ui.Field(meshes, "targetBodyMesh", "Body Mesh");
            ui.Messages(meshes, () => new[] { Message.Error(BodyMeshProblem()) });
            ui.Field(meshes, "clothingMeshes", "Clothing Meshes");
            ui.Messages(meshes, () => new[] { Message.Error(data.detectionMethod == DetectionMethod.Manual ? null : ClothingProblem()) });

            BuildDetection();
            BuildHiding();
            BuildToggle();
            BuildPreview();
            BuildAdvanced();
            return ui.Root;
        }

        // ---- Sections ------------------------------------------------------------------------------

        private void BuildDetection()
        {
            var section = ui.Section("Detection");
            ui.Field(section, "detectionMethod", "Method");
            var method = ui.Indent(section);
            ShowFor(ui.Field(method, "proximityThreshold", "Proximity Threshold"), DetectionMethod.Proximity, DetectionMethod.Hybrid);
            ShowFor(ui.Field(method, "raycastDistance", "Raycast Distance"), DetectionMethod.Raycast, DetectionMethod.Hybrid, DetectionMethod.Smart);
            ShowFor(ui.Field(method, "hybridExpansionFactor", "Expansion Factor"), DetectionMethod.Hybrid);
            ShowFor(ui.Field(method, "manualMask", "Mask"), DetectionMethod.Manual);
            ShowFor(ui.Field(method, "manualMaskThreshold", "Mask Threshold"), DetectionMethod.Manual);
            ui.Messages(method, () => new[] { Message.Error(data.detectionMethod == DetectionMethod.Manual ? MaskProblem() : null) });
            var smart = ui.Disclosure(method, "Smart Detection", "smart");
            ui.Field(smart, "smartRayDirections", "Ray Directions");
            ui.Field(smart, "smartOcclusionThreshold", "Occlusion Threshold");
            ui.Field(smart, "smartUseNormals", "Use Normals");
            ui.Field(smart, "smartRequireBidirectional", "Require Both Sides");
            ui.Field(smart, "smartRayOffset", "Ray Offset");
            ui.Field(smart, "smartConservativeMode", "Conservative Mode");
            ui.Field(smart, "smartMinDistanceToClothing", "Minimum Distance to Clothing");
            ShowFor(smart, DetectionMethod.Smart);
            ui.Field(section, "safetyMargin", "Safety Margin");
        }

        private void ShowFor(VisualElement element, params DetectionMethod[] methods) =>
            ui.ShowWhen(element, () => methods.Contains(data.detectionMethod));

        private void BuildHiding()
        {
            var section = ui.Section("Hiding");
            ui.Segmented(section, "applicationMode", "Mode", "Automatic", "UV Discard", "Mesh Deletion");
            ui.Caption(section, () => data.applicationMode switch
            {
                ApplicationMode.UVDiscard => "Hides the faces in the shader, so a toggle can show them again. Needs Poiyomi or FastFur.",
                ApplicationMode.MeshDeletion => "Removes the faces from the mesh. Works with any shader, but can't be toggled.",
                _ => "Uses UV Discard on Poiyomi and FastFur materials and Mesh Deletion on others."
            });

            var uv = ui.Group(section, "UV discard");
            UVDiscardInspector.Materials(ui, uv, () => data.targetBodyMesh);
            ui.Messages(uv, () => new[] { Message.Warning(data.applicationMode == ApplicationMode.UVDiscard && data.targetBodyMesh != null && !HasSupportedMaterial()
                ? "Choose a Poiyomi or FastFur material with UV support, or the build uses Mesh Deletion." : null) });
            UVDiscardInspector.UVChannel(ui, uv, () => data.targetBodyMesh != null ? data.targetBodyMesh.sharedMesh : null);
            ui.Field(uv, "autoAssignUVTile", "Automatic UV Tile");
            var tile = ui.Indent(uv);
            ui.Field(tile, "uvDiscardRow", "Row");
            ui.Field(tile, "uvDiscardColumn", "Column");
            ui.ShowWhen(tile, () => !data.autoAssignUVTile);
            ui.ShowWhen(uv, () => data.applicationMode != ApplicationMode.MeshDeletion);
        }

        private void BuildToggle()
        {
            var section = ui.Section("Toggle");
            var control = new SegmentedField("Control", new[] { "None", "Menu Toggle", "Existing Toggle" })
            {
                tooltip = "What turns body hiding on and off. With None, the body stays hidden."
            };
            int Selected() => data.useExistingToggle ? 2 : data.createToggle ? 1 : 0;
            control.SetValueWithoutNotify(Selected());
            control.RegisterValueChangedCallback(e =>
            {
                ui.Object.Update();
                ui.Object.FindProperty("createToggle").boolValue = e.newValue == 1;
                ui.Object.FindProperty("useExistingToggle").boolValue = e.newValue == 2;
                ui.Object.ApplyModifiedProperties();
                ui.Refresh();
            });
            ui.OnRefresh(() => control.SetValueWithoutNotify(Selected()));
            section.Add(control);
            ui.Messages(section, ToggleMessages);

            // A new menu toggle, unless a toggle on this object already controls hiding.
            var menu = new VisualElement();
            section.Add(menu);
            ui.ShowWhen(menu, () => data.createToggle && !data.useExistingToggle && ownToggle == null && !hasUVDiscardToggle);
            ui.Segmented(menu, "toggleType", "Toggle Type", "Object Toggle", "Hidden Toggle");
            ui.Caption(menu, () => data.toggleType == ToggleType.ObjectToggle
                ? "Turns the clothing and body hiding on and off together."
                : "Turns only body hiding on and off. The clothing stays visible.");
            ui.Field(menu, "toggleMenuPath", "Menu Path");
            ui.Field(menu, "toggleSaved", "Saved");
            ui.Field(menu, "toggleDefaultOn", "On by Default");
            ui.Field(menu, "toggleSynced", "Synced");
            var synced = ui.Indent(menu);
            ui.Field(synced, "toggleParameterName", "Global Parameter");
            ui.Caption(synced, () => string.IsNullOrEmpty(data.toggleMenuPath) && !string.IsNullOrEmpty(data.toggleParameterName)
                ? "No menu item. Only the global parameter turns it on and off." : null);
            ui.ShowWhen(synced, () => data.toggleSynced);
            ui.Messages(menu, () => string.IsNullOrEmpty(data.toggleMenuPath) && !data.toggleSynced
                ? new[] { Message.Warning("Enter a Menu Path. A toggle that isn't synced needs a menu item.") } : null);
            var options = ui.Disclosure(menu, "Menu Item Options", "toggleOptions");
            ui.Field(options, "toggleSlider", "Slider");
            ui.Field(options, "toggleHoldButton", "Hold Button");
            ui.Field(options, "toggleExclusiveOffState", "Exclusive Off State");
            ui.Field(options, "toggleEnableExclusiveTag", "Exclusive Tags");
            var tags = ui.Indent(options);
            ui.Field(tags, "toggleExclusiveTag", "Tags");
            ui.ShowWhen(tags, () => data.toggleEnableExclusiveTag);
            ui.Field(options, "toggleEnableIcon", "Custom Icon");
            var icon = ui.Indent(options);
            ui.Field(icon, "toggleIcon", "Icon");
            ui.ShowWhen(icon, () => data.toggleEnableIcon);

            // A VRCFury toggle on this object or a child.
            var existing = new VisualElement();
            section.Add(existing);
            ui.ShowWhen(existing, () => data.useExistingToggle);
            var picker = new VisualElement();
            existing.Add(picker);
            List<Component> shown = null;
            List<string> labels = null;
            DropdownField dropdown = null;
            ui.OnRefresh(() =>
            {
                if (shown == null || !toggles.SequenceEqual(shown))
                {
                    shown = toggles.ToList();
                    picker.Clear();
                    dropdown = null;
                    if (shown.Count == 0) return;
                    labels = new List<string> { "Automatic (" + ToggleName(shown[0]) + ")" };
                    foreach (var toggle in shown)
                    {
                        var name = ToggleName(toggle);
                        var label = name;
                        for (var n = 2; labels.Contains(label); n++) label = $"{name} ({n})";
                        labels.Add(label);
                    }
                    dropdown = new DropdownField("Toggle", labels, 0) { tooltip = ui.Object.FindProperty("selectedToggle").tooltip };
                    dropdown.AddToClassList("yucp-field-input");
                    dropdown.AddToClassList(BaseField<string>.alignedFieldUssClassName);
                    var choices = shown;
                    dropdown.RegisterValueChangedCallback(_ =>
                    {
                        ui.Object.Update();
                        ui.Object.FindProperty("selectedToggle").objectReferenceValue = dropdown.index <= 0 ? null : choices[dropdown.index - 1];
                        ui.Object.ApplyModifiedProperties();
                        ui.Refresh();
                    });
                    picker.Add(dropdown);
                }
                if (dropdown == null) return;
                var wanted = labels[Mathf.Max(0, shown.IndexOf(data.selectedToggle) + 1)];
                if (dropdown.value != wanted) dropdown.SetValueWithoutNotify(wanted);
            });
            var details = new VisualElement();
            existing.Add(details);
            ui.InfoRow(details, "Object", () => EffectiveToggle() is Component t && t != null ? GetGameObjectPath(t.gameObject, data.gameObject) : "");
            ui.InfoRow(details, "Global Parameter", () =>
            {
                var info = EffectiveToggleInfo();
                return string.IsNullOrEmpty(info.globalParam) ? "None" : info.globalParam;
            });
            ui.InfoRow(details, "Actions", () =>
            {
                var info = EffectiveToggleInfo();
                return info.actionCount == 0 ? "None" : $"{info.actionCount} ({info.actionSummary})";
            });
            ui.ShowWhen(details, () => EffectiveToggle() != null);
            ui.Messages(existing, () => toggles.Count == 0
                ? new[] { Message.Warning("Add a VRCFury toggle to this object or a child, or choose Menu Toggle.") } : null);

            // The next step when there's no toggle to pick (HIG Writing: give people a way forward).
            var add = ui.Button(existing, "Add VRCFury Toggle", CreateVRCFuryToggleComponent, Variant.Secondary,
                "Adds a VRCFury toggle to this object for blend shapes, material swaps and other actions. Body hiding follows it.");
            ui.ShowWhen(add, () => ownToggle == null && toggles.Count == 0);
        }

        private IEnumerable<Message> ToggleMessages()
        {
            if (!data.useExistingToggle && ownToggle != null)
                yield return Message.Info("Body hiding follows the VRCFury toggle on this object.");
            else if (!data.useExistingToggle && hasUVDiscardToggle)
                yield return Message.Info("The UV Discard Toggle on this object turns body hiding on and off.");
            if (!string.IsNullOrEmpty(toggleError)) yield return Message.Error(toggleError);
            if (!data.createToggle && !data.useExistingToggle) yield break;
            if (data.applicationMode == ApplicationMode.MeshDeletion)
                yield return Message.Warning("Toggles need UV discard. Set Mode to Automatic or UV Discard.");
            else if (data.applicationMode == ApplicationMode.AutoDetect && data.targetBodyMesh != null && !HasSupportedMaterial())
                yield return Message.Warning("Toggles need UV discard, which needs a Poiyomi or FastFur material with UV support.");
        }

        private void BuildPreview()
        {
            var section = ui.Section("Preview");
            ui.InfoRow(section, "Hidden Faces", () => data.previewGenerated && data.previewTriangles != null ? PreviewSummary() : "No preview yet");
            ui.ShowWhen(ui.Field(section, "showPreview", "Show in Scene View"), () => data.previewGenerated);
            var buttons = ui.ButtonRow(section);
            ui.EnableWhen(ui.Button(buttons, "Generate Preview", GeneratePreview, Variant.Primary,
                "Finds the hidden faces now and shows them in red in the Scene view."), ValidateData);
            ui.EnableWhen(ui.Button(buttons, "Clear Preview", () => { ClearPreview(); ui.Refresh(); }), () => data.previewGenerated);
        }

        private void BuildAdvanced()
        {
            var advanced = ui.Advanced();
            ui.Field(advanced, "mirrorSymmetry", "Mirror Symmetry");
            ui.Field(advanced, "useBoneFiltering", "Bone Filtering");
            var bones = ui.Indent(advanced);
            ui.Field(bones, "filterBones", "Filter Bones");
            ui.ShowWhen(bones, () => data.useBoneFiltering);
            ui.Field(advanced, "optimizeTileUsage", "Optimize Tile Usage");
            ui.Field(advanced, "debugMode", "Verbose Logging");
            ui.Field(advanced, "debugSaveAnimation", "Save Toggle Animation");
            ui.Button(advanced, "Clear Detection Cache", () =>
            {
                DetectionCache.ClearCache();
                cacheResult = "Detection cache cleared. The next build detects again.";
                ui.Refresh();
            }, Variant.Secondary, "Forgets saved detection results for every Auto Body Hider.");
            ui.Messages(advanced, () => new[] { Message.Success(cacheResult) });
            ui.BuildSummary(() => string.IsNullOrEmpty(data.AppliedMode) ? null : $"{data.HiddenVertexCount:N0} vertices hidden with {data.AppliedMode}");
        }

        // ---- Status --------------------------------------------------------------------------------

        private bool ValidateData() => GetValidationError() == null;

        private string GetValidationError() =>
            BodyMeshProblem() ?? (data.detectionMethod == DetectionMethod.Manual ? MaskProblem() : ClothingProblem());

        private string BodyMeshProblem() =>
            data.targetBodyMesh == null ? "Assign the body mesh."
            : data.targetBodyMesh.sharedMesh == null ? "The body mesh has no mesh. Assign a Skinned Mesh Renderer that has one."
            : null;

        private string MaskProblem() => data.manualMask == null ? "Assign a mask, or choose another detection method." : null;

        private string ClothingProblem() =>
            data.clothingMeshes == null || data.clothingMeshes.Length == 0 ? "Add the clothing meshes that cover the body."
            : !data.clothingMeshes.Any(m => m != null && m.sharedMesh != null) ? "Assign clothing meshes that have a mesh."
            : null;

        // Matches the processor: the selected materials if any, otherwise the body's materials.
        private bool HasSupportedMaterial()
        {
            var selected = (data.targetMaterials ?? Array.Empty<Material>()).Where(m => m != null).ToArray();
            var candidates = selected.Length > 0 ? selected : data.targetBodyMesh != null ? data.targetBodyMesh.sharedMaterials : Array.Empty<Material>();
            return candidates.Any(UVDiscardInspector.Supported);
        }

        private void ScanScene()
        {
            toggles = VRCFuryHelper.FindToggles(data.gameObject);
            ownToggle = toggles.FirstOrDefault(t => t.gameObject == data.gameObject);
            hasUVDiscardToggle = data.GetComponent<UVDiscardToggleData>() != null;
        }

        // ---- VRCFury toggles -------------------------------------------------------------------------

        private Component EffectiveToggle() => data.selectedToggle != null ? data.selectedToggle : toggles.FirstOrDefault();

        private (string menuPath, string globalParam, int actionCount, string actionSummary) EffectiveToggleInfo() =>
            EffectiveToggle() is Component toggle && toggle != null ? GetVRCFuryToggleInfo(toggle) : ("", "", 0, "");

        private string ToggleName(Component toggle)
        {
            var menuPath = GetVRCFuryToggleInfo(toggle).menuPath;
            return string.IsNullOrEmpty(menuPath) ? toggle.name : menuPath;
        }

        private void CreateVRCFuryToggleComponent()
        {
            toggleError = null;
            try
            {
                var before = new HashSet<Component>(data.GetComponents<Component>());
                var toggle = com.vrcfury.api.FuryComponents.CreateToggle(data.gameObject);
                toggle.SetMenuPath("Clothing/Hide Body");
                toggle.SetSaved();
                foreach (var added in data.GetComponents<Component>().Where(c => !before.Contains(c)))
                    Undo.RegisterCreatedObjectUndo(added, "Add VRCFury Toggle");
                Debug.Log($"[AutoBodyHider] Added a VRCFury toggle to '{data.gameObject.name}'.", data);
            }
            catch (Exception ex)
            {
                toggleError = "Couldn't add a VRCFury toggle. Make sure VRCFury is installed and up to date.";
                Debug.LogException(ex, data);
            }
            ui.Refresh();
        }

        private (string menuPath, string globalParam, int actionCount, string actionSummary) GetVRCFuryToggleInfo(Component component)
        {
            var model = VRCFuryHelper.GetToggleModel(component);
            if (model == null) return ("", "", 0, "");

            string menuPath = model.GetType().GetField("name").GetValue(model) as string ?? "";
            string globalParam = VRCFuryHelper.GetToggleGlobalParameter(component) ?? "";
            var state = model.GetType().GetField("state").GetValue(model);
            var actions = state?.GetType().GetField("actions").GetValue(state) as System.Collections.IList;
            if (actions == null) return (menuPath, globalParam, 0, "");

            var types = new HashSet<string>();
            foreach (var action in actions)
            {
                if (action == null) continue;
                string name = action.GetType().Name;
                if (name.EndsWith("Action", StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - 6);
                types.Add(name);
            }
            return (menuPath, globalParam, actions.Count, string.Join(", ", types));
        }

        private static string GetGameObjectPath(GameObject obj, GameObject root) =>
            obj == root ? obj.name : AnimationUtility.CalculateTransformPath(obj.transform, root.transform);

        // ---- Preview -------------------------------------------------------------------------------

        private void GeneratePreview()
        {
            if (!ValidateData()) return;
            CheckForSettingsChanges(); // Start from the current settings so this preview isn't cleared at once.
            var progress = YUCPProgressWindow.Create();
            try
            {
                progress.Progress(0f, "Finding hidden body faces");
                GPURaycast.Initialize();
                var mesh = data.targetBodyMesh.sharedMesh;
                var vertices = mesh.vertices;
                var uv0 = new List<Vector2>();
                mesh.GetUVs(0, uv0);
                var body = data.targetBodyMesh.transform;
                data.previewVertexPositions = vertices.Select(v => body.TransformPoint(v)).ToArray();
                data.previewRawHiddenVertices = VertexDetection.DetectHiddenVertices(data, vertices, mesh.normals, uv0.ToArray(), progress);
                data.previewLocalVertices = vertices;
                data.previewTriangles = mesh.triangles;
                UpdatePreviewFromCache();
                data.previewGenerated = true;
                if (!data.showPreview)
                {
                    Undo.RecordObject(data, "Show Preview");
                    data.showPreview = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e, data);
            }
            finally
            {
                progress.CloseWindow();
                ui.Refresh();
            }
        }

        /// <summary>Reapplies symmetry and the safety margin to the detected vertices; no new detection.</summary>
        private void UpdatePreviewFromCache()
        {
            if (data.previewRawHiddenVertices == null || data.previewLocalVertices == null) return;
            var hidden = (bool[])data.previewRawHiddenVertices.Clone();
            if (data.mirrorSymmetry) hidden = VertexDetection.ApplySymmetryMirror(hidden, data.previewLocalVertices);
            if (data.safetyMargin > 0.0001f) hidden = VertexDetection.ApplySafetyMargin(hidden, data.previewLocalVertices, data);
            data.previewHiddenVertices = hidden;
            data.lastPreviewSafetyMargin = data.safetyMargin;
            data.lastPreviewMirrorSymmetry = data.mirrorSymmetry;
            CalculateHiddenFaces(data);
            SceneView.RepaintAll();
        }

        private static void CalculateHiddenFaces(AutoBodyHiderData data)
        {
            int faceCount = data.previewTriangles.Length / 3;
            data.previewHiddenFaces = new bool[faceCount];
            for (int i = 0; i < faceCount; i++)
            {
                data.previewHiddenFaces[i] = data.previewHiddenVertices[data.previewTriangles[i * 3]] ||
                                             data.previewHiddenVertices[data.previewTriangles[i * 3 + 1]] ||
                                             data.previewHiddenVertices[data.previewTriangles[i * 3 + 2]];
            }
        }

        /// <summary>Detection changes need a new preview; margin and symmetry changes only reapply it.</summary>
        private void KeepPreviewCurrent()
        {
            var changed = CheckForSettingsChanges();
            if (!data.previewGenerated || data.previewRawHiddenVertices == null) return;
            if (changed) ClearPreview();
            else if (Mathf.Abs(data.safetyMargin - data.lastPreviewSafetyMargin) > 0.0001f || data.mirrorSymmetry != data.lastPreviewMirrorSymmetry)
                UpdatePreviewFromCache();
        }

        /// <summary>True when a setting that changes detection has changed since the last check.</summary>
        private bool CheckForSettingsChanges()
        {
            var key = DetectionKey();
            if (key == detectionKey) return false;
            detectionKey = key;
            return true;
        }

        private string DetectionKey() => string.Join("|",
            data.detectionMethod, data.proximityThreshold, data.raycastDistance, data.hybridExpansionFactor,
            data.smartRayDirections, data.smartOcclusionThreshold, data.smartUseNormals, data.smartRequireBidirectional,
            data.smartRayOffset, data.smartConservativeMode, data.smartMinDistanceToClothing,
            Id(data.manualMask), data.manualMaskThreshold, Id(data.targetBodyMesh), data.useBoneFiltering,
            string.Join(",", (data.clothingMeshes ?? Array.Empty<SkinnedMeshRenderer>()).Select(Id)),
            string.Join(",", (data.filterBones ?? Array.Empty<Transform>()).Select(Id)));

        private static int Id(UnityEngine.Object obj) => obj != null ? obj.GetInstanceID() : 0;

        private void ClearPreview()
        {
            data.previewHiddenVertices = null;
            data.previewVertexPositions = null;
            data.previewRawHiddenVertices = null;
            data.previewLocalVertices = null;
            data.previewHiddenFaces = null;
            data.previewTriangles = null;
            data.previewGenerated = false;
            data.lastPreviewSafetyMargin = -1f;
            data.lastPreviewMirrorSymmetry = false;
            SceneView.RepaintAll();
        }

        private string PreviewSummary()
        {
            if (data.previewHiddenFaces != countedFaces)
            {
                countedFaces = data.previewHiddenFaces;
                hiddenFaces = countedFaces?.Count(hidden => hidden) ?? 0;
            }
            var total = data.previewTriangles.Length / 3;
            return $"{hiddenFaces:N0} of {total:N0} ({(total > 0 ? hiddenFaces * 100f / total : 0f):F1}%)";
        }

        // Drawn from duringSceneGui so the preview shows with gizmos turned off.
        private void DrawPreview(SceneView view)
        {
            if (data == null || !data.showPreview || !data.previewGenerated || data.targetBodyMesh == null) return;
            var faces = data.previewHiddenFaces;
            var triangles = data.previewTriangles;
            var positions = data.previewVertexPositions;
            if (faces == null || triangles == null || positions == null) return;

            if (Event.current.type == EventType.Repaint)
            {
                var fill = Color.red;
                fill.a = 0.6f;
                Handles.color = fill;
                for (int i = 0; i < faces.Length; i++)
                    if (faces[i])
                        Handles.DrawAAConvexPolygon(positions[triangles[i * 3]], positions[triangles[i * 3 + 1]], positions[triangles[i * 3 + 2]]);
            }
            YUCPSceneOverlay.Draw("Auto Body Hider preview", "Red faces are hidden: " + PreviewSummary() + ".");
        }
    }

    /// <summary>
    /// The UV discard settings Auto Body Hider and Auto UV Discard share: which body materials get UV
    /// discard, and the UV channel. One wording and one behaviour for both.
    /// </summary>
    internal static class UVDiscardInspector
    {
        internal static bool Supported(Material material) => material != null && UVManipulator.IsPoiyomiWithUVSupport(material);

        /// <summary>The "Materials" picker for targetMaterials, with a warning for selections UV discard skips.</summary>
        internal static void Materials(YUCPInspector ui, VisualElement parent, Func<SkinnedMeshRenderer> body)
        {
            ui.MaterialPicker(parent, "targetMaterials", "Materials", () => body(),
                "All supported materials", "Assign the body mesh to choose its materials.", m => Supported(m) ? "UV Discard" : null);
            ui.Messages(parent, () =>
            {
                var property = ui.Object.FindProperty("targetMaterials");
                var skipped = Enumerable.Range(0, property.arraySize)
                    .Select(i => property.GetArrayElementAtIndex(i).objectReferenceValue as Material)
                    .Where(m => m != null && !Supported(m)).Select(m => m.name).ToList();
                return skipped.Count == 0 ? null : new[]
                {
                    Message.Warning("Skipped because they don't support UV discard: " + string.Join(", ", skipped) + ".")
                };
            });
        }

        /// <summary>"Automatic UV Channel" with the detected channel, or the channel to use while it's off.</summary>
        internal static void UVChannel(YUCPInspector ui, VisualElement parent, Func<Mesh> mesh)
        {
            ui.Field(parent, "autoDetectUVChannel", "Automatic UV Channel");
            var indent = ui.Indent(parent);
            bool Automatic() => ui.Object.FindProperty("autoDetectUVChannel").boolValue;
            Mesh detectedFor = null;
            var detected = "";
            var row = ui.InfoRow(indent, "Detected Channel", () =>
            {
                var current = mesh();
                if (current != detectedFor)
                {
                    detectedFor = current;
                    detected = current != null ? "UV" + UVManipulator.DetectBestUVChannel(current) : "";
                }
                return detected;
            });
            ui.ShowWhen(row, () => Automatic() && mesh() != null);
            ui.ShowWhen(ui.Field(indent, "uvChannel", "UV Channel"), () => !Automatic());
        }

        /// <summary>A read-only row for a list the caller rebuilds (ui.InfoRow is for fixed rows).</summary>
        internal static VisualElement Row(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-info-row");
            var name = new Label(label);
            name.AddToClassList("yucp-info-label");
            var text = new Label(value);
            text.AddToClassList("yucp-info-value");
            text.selection.isSelectable = true;
            row.Add(name);
            row.Add(text);
            return row;
        }
    }
}
