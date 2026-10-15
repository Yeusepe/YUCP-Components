using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;
using YUCP.Components.Editor.MeshUtils;
using Message = YUCP.UI.DesignSystem.Utilities.YUCPInspector.Message;
using Variant = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper.ButtonVariant;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(DigitigradeLegSplitData))]
    public class DigitigradeLegSplitDataEditor : UnityEditor.Editor
    {
        /// <summary>
        /// Below this metatarsus/shin ratio the leg folds like a human standing on tiptoe rather
        /// than reading as digitigrade. Rexouium's reference rig sits at 0.62.
        /// </summary>
        private const float HealthyRatio = 0.45f;

        private int previewUndoGroup = -1;
        private bool editing;
        private (bool right, int index) selectedJoint;
        private Message? previewResult;
        private YUCPInspector ui;

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneView;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneView;
            ui = null;
            StopEditing();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var data = (DigitigradeLegSplitData)target;
            ui = YUCPInspector.Create(this, "Digitigrade Leg Split");
            var status = ui.Status(() => new[]
            {
                MissingBones(data)
                    ? Message.Warning(Humanoid(data)
                        ? "To place the joints, fill in the leg bones from the humanoid rig."
                        : "To place the joints, assign the leg bones in Advanced Options.")
                    : default
            });
            var resolve = ui.ButtonRow(status.parent);
            ui.Button(resolve, "Auto-Resolve From Humanoid", () =>
            {
                Undo.RecordObject(data, "Auto-Resolve Leg Bones");
                data.ResolveBones();
                ui.Refresh();
            }, Variant.Secondary, "Fills the empty leg bones from the avatar's humanoid rig.");
            ui.ShowWhen(resolve, () => MissingBones(data) && Humanoid(data));

            var joints = ui.Section("Joint placement", "The joints are added at build time. Every mesh bound to the leg, clothing included, follows them.");
            ui.Field(joints, "splits", "Inserted Joints");
            ui.Field(joints, "mirrorRightLeg");
            var edit = ui.ButtonRow(joints);
            ui.ShowWhen(ui.Button(edit, "Edit Joints", StartEditing, Variant.Secondary,
                "Move and tilt the joints in the Scene view.", "EditCollider"), () => !editing);
            ui.ShowWhen(ui.Button(edit, "Done", StopEditing, Variant.Primary, "Stop editing (Esc).", "EditCollider"), () => editing);
            ui.EnableWhen(edit, () => EnumerateSides(data).Any() && data.splits != null && data.splits.Count > 0);
            ui.Caption(joints, "Move (W) drags a joint to the hock; Rotate (E) tilts its disc to the mesh crease.");

            // Live measurements of the left leg, so the joint can be placed by proportion.
            var segments = new List<(string name, float length)>();
            var ratio = 0f;
            void Measure() => ratio = MeasureLeftLeg(data, segments);
            Measure();
            ui.OnRefresh(Measure);
            var proportions = ui.ShowWhen(ui.Group(joints, "Left leg proportions"), () => segments.Count > 0);
            var rows = new VisualElement();
            proportions.Add(rows);
            void UpdateRows()
            {
                // Rows follow the joint count; ui.InfoRow can't be added from inside a refresh.
                if (rows.childCount != segments.Count)
                {
                    rows.Clear();
                    foreach (var _ in segments)
                    {
                        var row = new VisualElement();
                        row.AddToClassList("yucp-info-row");
                        var name = new Label();
                        name.AddToClassList("yucp-info-label");
                        var value = new Label();
                        value.AddToClassList("yucp-info-value");
                        value.selection.isSelectable = true;
                        row.Add(name);
                        row.Add(value);
                        rows.Add(row);
                    }
                }
                for (var i = 0; i < segments.Count; i++)
                {
                    var name = (Label)rows[i][0];
                    var value = (Label)rows[i][1];
                    var length = $"{segments[i].length:0.000} m";
                    if (name.text != segments[i].name) name.text = segments[i].name;
                    if (value.text != length) value.text = length;
                }
            }
            UpdateRows();
            ui.OnRefresh(UpdateRows);
            ui.InfoRow(proportions, "Metatarsus to Shin", () => $"{ratio:0.00} (reference rig: 0.62)");
            ui.Messages(proportions, () => new[]
            {
                segments.Count > 0 && ratio < HealthyRatio
                    ? Message.Warning($"Below {HealthyRatio:0.00} the leg folds like a human on tiptoe. Move the joint toward the knee.")
                    : default
            });

            var skin = ui.Section("Mesh deformation");
            ui.Field(skin, "blendBand");
            ui.Field(skin, "jointTipPreservation", "Hock Tip Preservation");

            DigitigradeLegSplitPreviewState state = null;
            var needsCleanup = false;
            void Scan()
            {
                var animator = data.GetComponentInParent<Animator>();
                state = animator != null ? animator.GetComponent<DigitigradeLegSplitPreviewState>() : null;
                needsCleanup = animator != null && DigitigradeLegSplitPreview.NeedsCleanup(animator.gameObject);
            }
            Scan();
            ui.OnRefresh(Scan);

            var preview = ui.Section("Scene preview", "Applies the build's split to this scene so you can pose the leg. Clear it before you save.");
            ui.Messages(preview, () => new[]
            {
                Application.isPlaying ? Message.Info("Exit Play Mode to preview the split.") : default,
                needsCleanup ? Message.Error("Some meshes lost bones from an earlier preview. Clean up the generated bones to repair them.") : default,
                previewResult ?? default
            });
            ui.Caption(preview, () => state != null
                ? $"The preview added {state.createdBones.Count} bones and changed {state.skins.Count} meshes. It can be cleared after a reload too."
                : "");
            var actions = ui.ButtonRow(preview);
            ui.ShowWhen(ui.Button(actions, "Generate Preview", () =>
            {
                serializedObject.ApplyModifiedProperties();
                previewResult = ApplyPreview(data);
                ui.Refresh();
            }, Variant.Secondary, "Applies the split to this scene."), () => state == null);
            ui.ShowWhen(ui.Button(actions, "Clear Preview", () =>
            {
                DigitigradeLegSplitPreview.Revert(state);
                previewResult = null;
                ui.Refresh();
            }, Variant.Secondary, "Restores the meshes and bones the preview changed."), () => state != null);
            ui.ShowWhen(ui.Button(actions, "Clean Up Generated Bones", () =>
            {
                var animator = data.GetComponentInParent<Animator>();
                if (animator == null || !EditorUtility.DisplayDialog("Clean up generated bones?",
                        "This removes the leftover preview bones and repairs the meshes that used them. You can't undo this.",
                        "Clean Up", "Cancel")) return;
                var removed = DigitigradeLegSplitPreview.CleanUp(animator.gameObject, data);
                previewResult = Message.Success($"Repaired {removed} bone references.");
                ui.Refresh();
            }, Variant.Danger, "Removes leftover preview bones and repairs the meshes that used them."), () => needsCleanup);
            ui.EnableWhen(actions, () => !Application.isPlaying);

            var advanced = ui.Advanced();
            var bones = ui.Group(advanced, "Leg bones");
            ui.Field(bones, "leftSourceBone", "Left Lower Leg");
            ui.Field(bones, "leftEndBone", "Left Foot");
            ui.Field(bones, "rightSourceBone", "Right Lower Leg");
            ui.Field(bones, "rightEndBone", "Right Foot");
            ui.Field(advanced, "verboseLogging");
            ui.BuildSummary(data.GetBuildSummary, data.GetLastBuildTimeUtc);

            ui.Root.TrackSerializedObjectValue(serializedObject, _ => SceneView.RepaintAll());
            return ui.Root;
        }

        private static bool MissingBones(DigitigradeLegSplitData data) =>
            data.leftSourceBone == null || data.leftEndBone == null || data.rightSourceBone == null || data.rightEndBone == null;

        private static bool Humanoid(DigitigradeLegSplitData data)
        {
            var animator = data.GetComponentInParent<Animator>();
            return animator != null && animator.isHuman;
        }

        /// <summary>
        /// Joint-to-joint lengths along the left leg, and the metatarsus/shin ratio. Leaves
        /// <paramref name="segments"/> empty when the leg can't be measured.
        /// </summary>
        private static float MeasureLeftLeg(DigitigradeLegSplitData data, List<(string name, float length)> segments)
        {
            segments.Clear();
            if (data.leftSourceBone == null || data.leftEndBone == null) return 0f;

            var positions = data.GetSortedPositions();
            if (positions.Length == 0) return 0f;

            var length = Vector3.Distance(data.leftSourceBone.position, data.leftEndBone.position);
            if (length < 1e-5f) return 0f;

            // Measure the real joint-to-joint distances rather than the along-axis fractions --
            // an off-axis offset lengthens the segments either side of it.
            var dir = (data.leftEndBone.position - data.leftSourceBone.position) / length;
            var offsets = data.GetSortedOffsets(false);
            var names = data.GetSortedNames(".L");

            var points = new List<Vector3> { data.leftSourceBone.position };
            for (int i = 0; i < positions.Length; i++)
            {
                points.Add(LegBoneSplitter.JointPosition(data.leftSourceBone, data.leftSourceBone.position, dir, length, positions[i], offsets[i]));
            }
            points.Add(data.leftEndBone.position);

            for (int i = 0; i < points.Count - 1; i++)
            {
                segments.Add((i == 0 ? data.leftSourceBone.name : names[i - 1], Vector3.Distance(points[i], points[i + 1])));
            }

            // Ratio of the last inserted segment against the shortened source segment.
            var shin = Vector3.Distance(points[0], points[1]);
            var metatarsus = Vector3.Distance(points[1], points[points.Count - 1]);
            return shin > 1e-5f ? metatarsus / shin : 0f;
        }

        /// <summary>Applies the preview; returns a message when it couldn't, or null when it did.</summary>
        private Message? ApplyPreview(DigitigradeLegSplitData data)
        {
            var avatarRoot = data.GetComponentInParent<Animator>();
            if (avatarRoot == null)
            {
                return Message.Error("To preview the split, put this inside an avatar with an Animator.");
            }

            var positions = data.GetSortedPositions();
            if (positions.Length == 0) return Message.Warning("Add an inserted joint to preview the split.");

            var recorder = avatarRoot.gameObject.AddComponent<DigitigradeLegSplitPreviewState>();
            var rebuilder = HumanoidRebuilder.Capture(avatarRoot);
            var addedBones = new List<Transform>();
            var movedBones = new List<Transform>();

            var messages = new List<string>();
            foreach (var (source, end, suffix, label, isRight) in EnumerateSides(data))
            {
                var mirror = isRight && data.mirrorRightLeg;
                var plan = new LegBoneSplitter.Plan
                {
                    sourceBone = source,
                    endBone = end,
                    positions = positions,
                    offsets = data.GetSortedOffsets(mirror),
                    angles = data.GetSortedAngles(mirror),
                    names = data.GetSortedNames(suffix),
                    blendBand = data.blendBand,
                    jointTipPreservation = data.jointTipPreservation
                };

                var report = LegBoneSplitter.Apply(avatarRoot.gameObject, plan, recorder);
                messages.Add(label + ": " + report.Summary);

                if (report.success)
                {
                    addedBones.AddRange(report.createdBones);
                    if (report.movedBone != null) movedBones.Add(report.movedBone);
                }
            }

            if (addedBones.Count == 0)
            {
                // Nothing was applied, so leave no half-finished record behind.
                DestroyImmediate(recorder);
                Debug.LogError("[YUCP Digitigrade Legs] Preview failed: " + string.Join(" | ", messages), data);
                return Message.Error("The preview didn't add any bones. The Console has the details.");
            }

            Message? result = null;
            if (rebuilder != null)
            {
                if (rebuilder.Rebuild(avatarRoot.gameObject, addedBones, movedBones, recorder, out var rebuildError))
                {
                    messages.Add("humanoid Avatar rebuilt");
                }
                else
                {
                    messages.Add("Avatar rebuild failed: " + rebuildError);
                    result = Message.Warning("The preview is on, but the humanoid avatar couldn't be rebuilt. The Console has the details.");
                }
            }

            EditorUtility.SetDirty(avatarRoot.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(avatarRoot.gameObject.scene);

            Debug.Log("[YUCP Digitigrade Legs] Preview: " + string.Join(" | ", messages), data);
            return result;
        }

        private static IEnumerable<(Transform source, Transform end, string suffix, string label, bool isRight)> EnumerateSides(DigitigradeLegSplitData data)
        {
            if (data.leftSourceBone != null && data.leftEndBone != null)
                yield return (data.leftSourceBone, data.leftEndBone, ".L", "Left", false);

            if (data.rightSourceBone != null && data.rightEndBone != null)
                yield return (data.rightSourceBone, data.rightEndBone, ".R", "Right", true);
        }

        // ---- Scene view: Edit Joints mode, like Unity's Edit Collider ---------------------------

        private void StartEditing()
        {
            editing = true;
            selectedJoint = (false, 0);
            Tools.hidden = true;
            SceneView.RepaintAll();
            ui?.Refresh();
        }

        private void StopEditing()
        {
            if (!editing) return;
            editing = false;
            Tools.hidden = false;
            SceneView.RepaintAll();
            ui?.Refresh();
        }

        private void OnSceneView(SceneView sceneView)
        {
            var data = target as DigitigradeLegSplitData;
            if (data == null || previewUndoGroup >= 0) return;

            if (editing && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                StopEditing();
                Event.current.Use();
                return;
            }

            foreach (var (source, end, _, label, isRight) in EnumerateSides(data))
            {
                DrawLegHandles(data, source, end, label, isRight);
            }

            if (editing)
            {
                YUCPSceneOverlay.Draw("Editing leg joints", Tools.current == Tool.Rotate
                    ? "Tilt the yellow disc to the mesh crease. W to move, Esc to finish."
                    : "Click a joint, then drag it to the hock. E to tilt, Esc to finish.");
            }
        }

        private void DrawLegHandles(DigitigradeLegSplitData data, Transform source, Transform end, string label, bool isRight)
        {
            var start = source.position;
            var axis = end.position - start;
            var length = axis.magnitude;
            if (length < 1e-5f) return;
            var dir = axis / length;

            var mirrorSide = isRight && data.mirrorRightLeg;

            // The chord between the two bone origins. It is only the reference `position` is
            // measured along -- the joints do not have to sit on it -- so draw it as a faint hint.
            Handles.color = new Color(0.4f, 0.8f, 1f, 0.25f);
            Handles.DrawDottedLine(start, end.position, 4f);

            // The bone chain this will actually produce, joint by joint.
            var ordered = data.GetOrderedSplits();
            var orderedOffsets = data.GetSortedOffsets(mirrorSide);
            var chain = new List<Vector3> { start };
            for (int i = 0; i < ordered.Count; i++)
            {
                chain.Add(LegBoneSplitter.JointPosition(source, start, dir, length, ordered[i].position, orderedOffsets[i]));
            }
            chain.Add(end.position);

            Handles.color = new Color(0.4f, 0.8f, 1f, 0.95f);
            for (int i = 0; i < chain.Count - 1; i++) Handles.DrawLine(chain[i], chain[i + 1], 3f);
            Handles.SphereHandleCap(0, start, Quaternion.identity, length * 0.04f, EventType.Repaint);
            Handles.SphereHandleCap(0, end.position, Quaternion.identity, length * 0.04f, EventType.Repaint);

            if (data.splits == null) return;

            for (int i = 0; i < data.splits.Count; i++)
            {
                var split = data.splits[i];
                if (split == null) continue;

                var mirror = mirrorSide;
                var offset = mirror ? new Vector3(-split.offset.x, split.offset.y, split.offset.z) : split.offset;
                var angle = mirror ? new Vector3(split.angle.x, -split.angle.y, -split.angle.z) : split.angle;

                var position = LegBoneSplitter.JointPosition(source, start, dir, length, split.position, offset);
                var jointRotation = source.rotation * Quaternion.Euler(angle);
                var normal = LegBoneSplitter.TiltRotation(source, angle) * dir;

                // Show where the joint sits relative to the straight chord it was pulled off.
                var onAxis = start + dir * (length * split.position);
                if (offset != Vector3.zero)
                {
                    Handles.color = new Color(1f, 0.85f, 0.2f, 0.5f);
                    Handles.DrawDottedLine(onAxis, position, 3f);
                }

                // The cut plane itself. This is the weight boundary -- if it does not line up with
                // the crease on the mesh, that is what the angle is for.
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.25f);
                Handles.DrawSolidDisc(position, normal, length * 0.14f);
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                Handles.DrawWireDisc(position, normal, length * 0.14f);
                Handles.DrawLine(position, position + normal * (length * 0.1f));

                if (editing && selectedJoint != (isRight, i))
                {
                    // Only the selected joint gets a gizmo; the others are click-to-select dots.
                    var size = HandleUtility.GetHandleSize(position) * 0.08f;
                    if (Handles.Button(position, Quaternion.identity, size, size, Handles.SphereHandleCap))
                    {
                        selectedJoint = (isRight, i);
                    }
                }
                else if (editing)
                {
                    EditorGUI.BeginChangeCheck();
                    if (Tools.current == Tool.Rotate)
                    {
                        var rotated = Handles.RotationHandle(jointRotation, position);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(data, "Tilt leg split");
                            var local = NormalizeEuler((Quaternion.Inverse(source.rotation) * rotated).eulerAngles);
                            split.angle = mirror ? new Vector3(local.x, -local.y, -local.z) : local;
                            EditorUtility.SetDirty(data);
                        }
                    }
                    else
                    {
                        // Free move: whatever component lands along the bone becomes `position`, and
                        // the rest becomes the off-axis offset. Dragging perpendicular used to do
                        // nothing because this was a Slider locked to `dir`.
                        var moved = Handles.PositionHandle(position, source.rotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(data, "Move leg split");

                            var delta = moved - start;
                            var along = Mathf.Clamp(Vector3.Dot(delta, dir) / length, 0.02f, 0.98f);
                            var residual = delta - dir * (length * along);
                            var localOffset = Quaternion.Inverse(source.rotation) * residual;

                            split.position = along;
                            split.offset = mirror
                                ? new Vector3(-localOffset.x, localOffset.y, localOffset.z)
                                : localOffset;
                            EditorUtility.SetDirty(data);
                        }
                    }
                }

                Handles.color = Color.white;
                Handles.Label(position + Vector3.up * (length * 0.06f),
                    $"{label} {split.name}   pos {split.position:0.00}" +
                    $"   offset {split.offset.x:0.000}/{split.offset.y:0.000}/{split.offset.z:0.000}" +
                    $"   tilt {split.angle.x:0}/{split.angle.y:0}/{split.angle.z:0}");
            }
        }

        /// <summary>Quaternion.eulerAngles returns 0..360; fold to -180..180 so the inspector reads sensibly.</summary>
        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(Fold(euler.x), Fold(euler.y), Fold(euler.z));

            float Fold(float a)
            {
                a %= 360f;
                if (a > 180f) a -= 360f;
                if (a < -180f) a += 360f;
                return Mathf.Abs(a) < 1e-4f ? 0f : a;
            }
        }
    }
}
