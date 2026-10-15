using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;
using Message = YUCP.UI.DesignSystem.Utilities.YUCPInspector.Message;
using Variant = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper.ButtonVariant;

namespace YUCP.Components.Editor.UI
{
    /// <summary>
    /// Auto UV Discard: the body it hides, how this clothing is split into regions, and the global
    /// parameters that turn each region on and off.
    /// </summary>
    [CustomEditor(typeof(AutoUVDiscardData))]
    public class AutoUVDiscardDataEditor : UnityEditor.Editor
    {
        private AutoUVDiscardData data;
        private YUCPInspector ui;
        private Message? importResult;
        private string shownGroups;

        public override VisualElement CreateInspectorGUI()
        {
            data = (AutoUVDiscardData)target;
            ui = YUCPInspector.Create(this, "Auto UV Discard");
            ui.Status(Problems);

            var body = ui.Section("Body");
            ui.Field(body, "targetBodyMesh", "Body Mesh");
            ui.Messages(body, BodyProblems);
            UVDiscardInspector.Materials(ui, body, () => data.targetBodyMesh);

            BuildDetection();
            BuildParameters();

            var advanced = ui.Advanced();
            UVDiscardInspector.UVChannel(ui, advanced, OwnMesh);
            ui.Field(advanced, "autoAssignUVTile", "Automatic UV Tile");
            var start = ui.Indent(advanced);
            ui.Field(start, "startRow", "Start Row");
            ui.Field(start, "startColumn", "Start Column");
            ui.ShowWhen(start, () => !data.autoAssignUVTile);
            ui.BuildSummary(BuildSummary);
            return ui.Root;
        }

        private void BuildDetection()
        {
            var section = ui.Section("Detection");
            ui.Field(section, "detectionMode", "Method");
            ui.Caption(section, () => Description(data.detectionMode));

            var proximity = Mode(section, DetectionMode.UVProximity);
            ui.Field(proximity, "mergeTolerance", "Merge Tolerance");
            ui.Field(proximity, "minRegionSize", "Minimum Region Size");

            var mask = Mode(section, DetectionMode.MaskTexture);
            ui.Field(mask, "maskRegions", "Mask Regions");
            ui.Messages(mask, () => data.maskRegions != null && data.maskRegions.Any(r => r != null && r.enabled && r.maskTexture != null)
                ? null : new[] { Message.Error("Add a mask region with a texture.") });
            ui.Field(mask, "maskUVChannel", "UV Channel");
            ui.Field(mask, "maskThreshold", "Threshold");

            var seams = Mode(section, DetectionMode.UVSeams);
            ui.Field(seams, "seamUVChannel", "UV Channel");
            ui.Field(seams, "seamThreshold", "Seam Threshold");
            ui.Field(seams, "minRegionSize", "Minimum Region Size");

            var sharp = Mode(section, DetectionMode.SharpEdges);
            ui.Field(sharp, "sharpAngleThreshold", "Angle Threshold");
            ui.Field(sharp, "useImportedSharpEdges", "Use Imported Sharp Edges");
            ui.Field(sharp, "minRegionSize", "Minimum Region Size");

            // The import button and the groups it creates sit together.
            var groups = Mode(section, DetectionMode.BlenderVertexGroups);
            ui.Button(groups, "Import Vertex Groups", ImportBlenderVertexGroups, Variant.Primary,
                "Creates a group for each bone that moves enough of this mesh. Unity doesn't keep Blender's group names.");
            ui.Messages(groups, () => importResult.HasValue ? new[] { importResult.Value } : null);
            var list = new VisualElement();
            groups.Add(list);
            ui.OnRefresh(() => UpdateVertexGroupsList(list));
            UpdateVertexGroupsList(list);
            ui.Field(groups, "vertexGroupWeightThreshold", "Weight Threshold");

            var bones = Mode(section, DetectionMode.BoneInfluence);
            ui.Field(bones, "targetBones", "Target Bones");
            ui.Messages(bones, () => data.targetBones != null && data.targetBones.Any(b => b != null)
                ? null : new[] { Message.Error("Add a target bone.") });
            ui.Field(bones, "boneWeightThreshold", "Weight Threshold");
            ui.Field(bones, "includeChildBones", "Include Child Bones");
        }

        private VisualElement Mode(VisualElement parent, DetectionMode mode)
        {
            var group = new VisualElement();
            parent.Add(group);
            return ui.ShowWhen(group, () => data.detectionMode == mode);
        }

        // Matches AutoUVDiscardProcessor's detection for each mode.
        private static string Description(DetectionMode mode) => mode switch
        {
            DetectionMode.UVProximity => "Groups vertices whose UVs are close together. Best when UV islands are well separated.",
            DetectionMode.MaskTexture => "Each mask is one region: the vertices where it is at or above the threshold.",
            DetectionMode.UVSeams => "Splits the mesh into regions along its UV seams.",
            DetectionMode.SharpEdges => "Splits the mesh into regions along its sharp edges.",
            DetectionMode.BlenderVertexGroups => "Each imported vertex group is one region.",
            DetectionMode.MaterialSlots => "Each material slot on this mesh is one region.",
            DetectionMode.BoneInfluence => "Each target bone is one region: the vertices it moves.",
            _ => ""
        };

        private void BuildParameters()
        {
            var section = ui.Section("Global parameters");
            ui.Segmented(section, "useSingleGlobalParameter", "Parameter", "One per Region", "One for All");
            ui.ShowWhen(ui.Field(section, "globalParameterBaseName", "Base Name"), () => !data.useSingleGlobalParameter);
            ui.ShowWhen(ui.Field(section, "singleGlobalParameterName", "Global Parameter"), () => data.useSingleGlobalParameter);
            // Names as AutoUVDiscardProcessor.GetGlobalParameterName builds them.
            ui.Caption(section, () =>
            {
                if (data.useSingleGlobalParameter)
                    return "All regions use " + Or(data.singleGlobalParameterName, "AutoUVDiscard_All") + ".";
                var name = Or(data.globalParameterBaseName, "AutoUVDiscard");
                return $"Regions use {name}_1, {name}_2 and so on.";
            });

            var fromToggles = ui.Group(section, "From VRCFury toggles");
            var rows = new VisualElement();
            fromToggles.Add(rows);
            ui.Caption(fromToggles, "A region whose parameter matches one of these follows that toggle.");
            string shown = null;
            ui.OnRefresh(() =>
            {
                var found = ToggleParameters();
                var key = string.Join("\n", found.Select(p => p.parameter + "\t" + p.source));
                if (key == shown) return;
                shown = key;
                rows.Clear();
                foreach (var (parameter, source) in found) rows.Add(UVDiscardInspector.Row(parameter, source));
            });
            ui.ShowWhen(fromToggles, () => rows.childCount > 0);
        }

        private static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

        private IEnumerable<Message> Problems()
        {
            if (OwnMesh() == null) yield return Message.Error("Add a mesh to this object's Skinned Mesh Renderer.");
        }

        private IEnumerable<Message> BodyProblems()
        {
            if (data.targetBodyMesh == null)
                yield return Message.Error("Assign the body mesh.");
            else if (!(data.targetMaterials ?? Array.Empty<Material>()).Any(UVDiscardInspector.Supported)
                     && !data.targetBodyMesh.sharedMaterials.Any(UVDiscardInspector.Supported))
                yield return Message.Error("The body mesh needs a Poiyomi or FastFur material with UV support.");
        }

        private Mesh OwnMesh()
        {
            var renderer = data.GetComponent<SkinnedMeshRenderer>();
            return renderer != null ? renderer.sharedMesh : null;
        }

        private string BuildSummary()
        {
            var tiles = data.UsedUVTiles ?? new List<string>();
            if (data.DetectedRegions == 0 && tiles.Count == 0) return null;
            var regions = data.DetectedRegions == 1 ? "1 region" : $"{data.DetectedRegions} regions";
            // "Row3_Col0" -> "(3, 0)"
            return tiles.Count == 0 ? regions : $"{regions} on tiles {string.Join(", ", tiles.Select(t => "(" + t.Replace("Row", "").Replace("_Col", ", ") + ")"))}";
        }

        private List<(string parameter, string source)> ToggleParameters()
        {
            var found = new List<(string, string)>();
            foreach (var toggle in VRCFuryHelper.FindToggles(data.gameObject))
            {
                var parameter = VRCFuryHelper.GetToggleGlobalParameter(toggle);
                if (string.IsNullOrEmpty(parameter)) continue;
                var model = VRCFuryHelper.GetToggleModel(toggle);
                var name = Or(model.GetType().GetField("name").GetValue(model) as string, toggle.name);
                found.Add((parameter, $"{name} on {GetGameObjectPath(toggle.gameObject, data.gameObject)}"));
            }
            return found;
        }

        private static string GetGameObjectPath(GameObject obj, GameObject root) =>
            obj == root ? obj.name : AnimationUtility.CalculateTransformPath(obj.transform, root.transform);

        /// <summary>One row per imported group. Rebuilt only when the groups change.</summary>
        private void UpdateVertexGroupsList(VisualElement container)
        {
            var groups = data.blenderVertexGroups ?? new List<BlenderVertexGroup>();
            var key = string.Join("\n", groups.Select(g => g.name + "\t" + (g.weights?.Count ?? 0)));
            if (key == shownGroups) return;
            shownGroups = key;
            container.Clear();
            if (groups.Count == 0)
            {
                var empty = new Label("Import vertex groups to create regions.");
                empty.AddToClassList("yucp-list-empty");
                container.Add(empty);
                return;
            }
            foreach (var group in groups)
                container.Add(UVDiscardInspector.Row(group.name, $"{group.weights?.Count ?? 0:N0} vertices"));
        }

        /// <summary>
        /// Unity doesn't keep Blender's vertex groups, so this builds one group per bone from the
        /// mesh's dominant bone weights as a starting point. The result shows next to the button.
        /// </summary>
        private void ImportBlenderVertexGroups()
        {
            if (data == null) return;

            var renderer = data.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null)
            {
                importResult = Message.Error("Add a mesh to this object's Skinned Mesh Renderer, then import.");
                ui.Refresh();
                return;
            }

            Mesh mesh = renderer.sharedMesh;
            serializedObject.Update();
            var groupsProp = serializedObject.FindProperty("blenderVertexGroups");
            groupsProp.ClearArray();

            if (renderer.bones != null && renderer.bones.Length > 0)
            {
                BoneWeight[] weights = mesh.boneWeights;
                var boneVertexGroups = new Dictionary<int, List<VertexWeight>>();

                for (int v = 0; v < weights.Length; v++)
                {
                    BoneWeight bw = weights[v];
                    int dominantBone = -1;
                    float maxWeight = 0.1f;

                    if (bw.weight0 > maxWeight) { dominantBone = bw.boneIndex0; maxWeight = bw.weight0; }
                    if (bw.weight1 > maxWeight) { dominantBone = bw.boneIndex1; maxWeight = bw.weight1; }
                    if (bw.weight2 > maxWeight) { dominantBone = bw.boneIndex2; maxWeight = bw.weight2; }
                    if (bw.weight3 > maxWeight) { dominantBone = bw.boneIndex3; maxWeight = bw.weight3; }

                    if (dominantBone >= 0 && dominantBone < renderer.bones.Length)
                    {
                        if (!boneVertexGroups.ContainsKey(dominantBone))
                            boneVertexGroups[dominantBone] = new List<VertexWeight>();
                        boneVertexGroups[dominantBone].Add(new VertexWeight { vertexIndex = v, weight = maxWeight });
                    }
                }

                int minVertices = Mathf.Max(10, mesh.vertexCount / 50);
                int colorIdx = 0;

                foreach (var kvp in boneVertexGroups)
                {
                    if (kvp.Value.Count >= minVertices && kvp.Key < renderer.bones.Length && renderer.bones[kvp.Key] != null)
                    {
                        int idx = groupsProp.arraySize;
                        groupsProp.InsertArrayElementAtIndex(idx);
                        var groupProp = groupsProp.GetArrayElementAtIndex(idx);

                        groupProp.FindPropertyRelative("name").stringValue = renderer.bones[kvp.Key].name;
                        groupProp.FindPropertyRelative("enabled").boolValue = true;
                        // Preview colors only: spread hues so neighbouring groups differ.
                        groupProp.FindPropertyRelative("debugColor").colorValue = Color.HSVToRGB(colorIdx++ * 0.618034f % 1f, 0.8f, 1f);

                        var weightsProp = groupProp.FindPropertyRelative("weights");
                        weightsProp.ClearArray();
                        foreach (var vw in kvp.Value)
                        {
                            int wIdx = weightsProp.arraySize;
                            weightsProp.InsertArrayElementAtIndex(wIdx);
                            var weightProp = weightsProp.GetArrayElementAtIndex(wIdx);
                            weightProp.FindPropertyRelative("vertexIndex").intValue = vw.vertexIndex;
                            weightProp.FindPropertyRelative("weight").floatValue = vw.weight;
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();

            int groupCount = data.blenderVertexGroups?.Count ?? 0;
            importResult = groupCount > 0
                ? Message.Success($"Imported {groupCount} vertex groups from bone weights.")
                : Message.Warning("No vertex groups found. The mesh needs bones that each move enough of it.");
            ui.Refresh();
        }
    }
}
