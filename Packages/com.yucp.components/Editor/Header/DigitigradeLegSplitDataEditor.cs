using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;
using Toolkit = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper;
using YUCP.Components.Editor.MeshUtils;

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

        private enum HandleMode { Move, Rotate }

        private int previewUndoGroup = -1;
        private HandleMode handleMode = HandleMode.Move;

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneView;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneView;
        }

        public override VisualElement CreateInspectorGUI()
        {
            var data = (DigitigradeLegSplitData)target;
            serializedObject.Update();
            var root = new VisualElement();
            Toolkit.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Digitigrade Leg Split"));
            var support = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(DigitigradeLegSplitData));
            if (support != null) root.Add(support);
            var validation = new VisualElement();
            root.Add(validation);

            var geometry = Toolkit.CreateCard("Joint Placement", "Add the extra lower-leg joint at build time. All bound meshes, including armature-linked clothing, follow the same split.");
            var geometryContent = Toolkit.GetCardContent(geometry);
            geometryContent.Add(Toolkit.CreateField(serializedObject.FindProperty("splits"), "Inserted Joints"));
            var handle = new EnumField("Scene Handle", handleMode);
            handle.AddToClassList("yucp-field-input");
            var handleHelp = new Label();
            handleHelp.AddToClassList("yucp-card-subtitle");
            handleHelp.style.whiteSpace = WhiteSpace.Normal;
            void RefreshHandle()
            {
                handleHelp.text = handleMode == HandleMode.Rotate
                    ? "Tilt the yellow disc to align the weight boundary with the mesh crease."
                    : "Drag the joint to the hock. It can move off the blue knee-to-ankle line.";
                SceneView.RepaintAll();
            }
            handle.RegisterValueChangedCallback(evt => { handleMode = (HandleMode)evt.newValue; RefreshHandle(); });
            geometryContent.Add(handle);
            geometryContent.Add(handleHelp);
            geometryContent.Add(Toolkit.CreateField(serializedObject.FindProperty("mirrorRightLeg"), "Mirror Right Leg"));
            root.Add(geometry);

            var skin = Toolkit.CreateCard("Mesh Deformation", "Control the crease transition and preserve the protruding hock tip when the leg folds.");
            var skinContent = Toolkit.GetCardContent(skin);
            AddSlider(skinContent, "blendBand", "Blend Band", 0.25f);
            AddSlider(skinContent, "jointTipPreservation", "Hock Tip Preservation", 1f);
            root.Add(skin);

            var proportions = Toolkit.CreateFoldout("Leg Proportions");
            proportions.viewDataKey = "digitigrade-split-proportions";
            // Keep the live measurements alongside the existing Scene handles.
            proportions.Add(new IMGUIContainer(() => { if (target != null) DrawProportions(data); }));
            root.Add(proportions);

            var preview = Toolkit.CreateCard("Scene Preview", "Apply the build's mesh split to this scene, pose the leg to inspect the fold, then revert before saving.");
            var previewContent = Toolkit.GetCardContent(preview);
            root.Add(preview);
            var summary = new Label();

            var bones = Toolkit.CreateFoldout("Source Bones");
            bones.viewDataKey = "digitigrade-split-bones";
            var leftSource = new ObjectField("Left Lower Leg") { objectType = typeof(Transform), bindingPath = "leftSourceBone" };
            leftSource.AddToClassList("yucp-field-input");
            leftSource.AddToClassList("unity-base-field__aligned");
            bones.Add(leftSource);
            bones.Add(Toolkit.CreateField(serializedObject.FindProperty("leftEndBone"), "Left Foot"));
            bones.Add(Toolkit.CreateField(serializedObject.FindProperty("rightSourceBone"), "Right Lower Leg"));
            bones.Add(Toolkit.CreateField(serializedObject.FindProperty("rightEndBone"), "Right Foot"));
            bones.Add(Toolkit.CreateButton("Auto-Resolve From Humanoid", () =>
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(data, "Resolve leg bones");
                data.ResolveBones();
                EditorUtility.SetDirty(data);
                serializedObject.Update();
                Refresh();
            }, Toolkit.ButtonVariant.Secondary));
            root.Add(bones);

            var diagnostics = Toolkit.CreateFoldout("Diagnostics");
            diagnostics.viewDataKey = "digitigrade-split-diagnostics";
            var logging = new Toggle("Verbose Logging") { bindingPath = "verboseLogging" };
            logging.AddToClassList("yucp-field-input");
            diagnostics.Add(logging);
            summary.AddToClassList("yucp-card-subtitle");
            summary.style.whiteSpace = WhiteSpace.Normal;
            diagnostics.Add(summary);
            root.Add(diagnostics);

            void Refresh()
            {
                if (target == null) return;
                validation.Clear();
                if (data.leftSourceBone == null || data.leftEndBone == null || data.rightSourceBone == null || data.rightEndBone == null)
                    validation.Add(Toolkit.CreateHelpBox("Leg bones are missing. Open Source Bones to auto-resolve or assign them.", Toolkit.MessageType.Warning));
                summary.text = string.IsNullOrEmpty(data.GetBuildSummary()) ? "" : "Last build: " + data.GetBuildSummary();
                RefreshPreviewControls(data, previewContent);
                SceneView.RepaintAll();
            }
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            root.Query<Label>(className: "yucp-card-subtitle").ForEach(label => label.style.whiteSpace = WhiteSpace.Normal);
            RefreshHandle();
            Refresh();
            return root;
        }

        private void AddSlider(VisualElement parent, string propertyName, string label, float maximum)
        {
            var property = serializedObject.FindProperty(propertyName);
            var slider = new Slider(label, 0f, maximum)
            {
                showInputField = true,
                bindingPath = property.propertyPath,
                tooltip = property.tooltip
            };
            slider.AddToClassList("yucp-field-input");
            slider.AddToClassList("unity-base-field__aligned");
            parent.Add(slider);
        }

        private void DrawProportions(DigitigradeLegSplitData data)
        {
            if (data.leftSourceBone == null || data.leftEndBone == null) return;

            var positions = data.GetSortedPositions();
            if (positions.Length == 0) return;

            var length = Vector3.Distance(data.leftSourceBone.position, data.leftEndBone.position);
            if (length < 1e-5f) return;

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

            EditorGUILayout.LabelField("Resulting segments (left leg)", EditorStyles.boldLabel);
            for (int i = 0; i < points.Count - 1; i++)
            {
                var name = i == 0 ? data.leftSourceBone.name : names[i - 1];
                EditorGUILayout.LabelField($"   {name} = {Vector3.Distance(points[i], points[i + 1]):0.000} m");
            }

            // Ratio of the last inserted segment against the shortened source segment.
            var shin = Vector3.Distance(points[0], points[1]);
            var metatarsus = Vector3.Distance(points[1], points[points.Count - 1]);
            var ratio = shin > 1e-5f ? metatarsus / shin : 0f;

            var message = $"metatarsus / shin = {ratio:0.00}   (reference digitigrade rig: 0.62)";
            if (ratio < HealthyRatio)
            {
                EditorGUILayout.HelpBox(
                    message + "\n\nBelow ~" + HealthyRatio.ToString("0.00") +
                    " the leg folds like a human on tiptoe rather than reading as digitigrade. " +
                    "Drag the handle further toward the knee.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }

        private void RefreshPreviewControls(DigitigradeLegSplitData data, VisualElement content)
        {
            content.Clear();
            if (Application.isPlaying)
            {
                content.Add(Toolkit.CreateHelpBox("Exit Play Mode to preview changes to the mesh split.", Toolkit.MessageType.Warning));
                return;
            }

            var animator = data.GetComponentInParent<Animator>();
            var state = animator != null ? animator.GetComponent<DigitigradeLegSplitPreviewState>() : null;
            var apply = Toolkit.CreateButton("Preview In Scene", () =>
            {
                serializedObject.ApplyModifiedProperties();
                ApplyPreview(data);
                RefreshPreviewControls(data, content);
            });
            apply.SetEnabled(state == null);
            content.Add(apply);
            var revert = Toolkit.CreateButton("Revert Preview", () =>
            {
                DigitigradeLegSplitPreview.Revert(state);
                RefreshPreviewControls(data, content);
            }, Toolkit.ButtonVariant.Secondary);
            revert.SetEnabled(state != null);
            content.Add(revert);
            if (state != null)
                content.Add(Toolkit.CreateHelpBox($"Preview applied: {state.createdBones.Count} bone(s), {state.skins.Count} mesh(es). Revert remains available after a domain reload."));

            if (animator != null && DigitigradeLegSplitPreview.NeedsCleanup(animator.gameObject))
            {
                content.Add(Toolkit.CreateHelpBox("Some meshes have missing bone references from a previous preview. Clean up the generated bones to repair them.", Toolkit.MessageType.Error));
                content.Add(Toolkit.CreateButton("Clean Up Generated Bones", () =>
                {
                    var removed = DigitigradeLegSplitPreview.CleanUp(animator.gameObject, data);
                    Debug.Log("[YUCP Digitigrade Legs] Cleanup removed " + removed + " dangling reference(s).", data);
                    RefreshPreviewControls(data, content);
                }, Toolkit.ButtonVariant.Danger));
            }
        }

        private void ApplyPreview(DigitigradeLegSplitData data)
        {
            var avatarRoot = data.GetComponentInParent<Animator>();
            if (avatarRoot == null)
            {
                EditorUtility.DisplayDialog("YUCP Digitigrade Legs", "Could not find the avatar's Animator.", "OK");
                return;
            }

            var positions = data.GetSortedPositions();
            if (positions.Length == 0) return;

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
                Debug.LogError("[YUCP Digitigrade Legs] Preview failed — " + string.Join(" | ", messages), data);
                return;
            }

            if (rebuilder != null)
            {
                messages.Add(rebuilder.Rebuild(avatarRoot.gameObject, addedBones, movedBones, recorder, out var rebuildError)
                    ? "humanoid Avatar rebuilt"
                    : "AVATAR REBUILD FAILED — " + rebuildError);
            }

            EditorUtility.SetDirty(avatarRoot.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(avatarRoot.gameObject.scene);

            Debug.Log("[YUCP Digitigrade Legs] Preview — " + string.Join(" | ", messages), data);
        }

        private static IEnumerable<(Transform source, Transform end, string suffix, string label, bool isRight)> EnumerateSides(DigitigradeLegSplitData data)
        {
            if (data.leftSourceBone != null && data.leftEndBone != null)
                yield return (data.leftSourceBone, data.leftEndBone, ".L", "Left", false);

            if (data.rightSourceBone != null && data.rightEndBone != null)
                yield return (data.rightSourceBone, data.rightEndBone, ".R", "Right", true);
        }

        private void OnSceneView(SceneView sceneView)
        {
            var data = target as DigitigradeLegSplitData;
            if (data == null || previewUndoGroup >= 0) return;

            foreach (var (source, end, _, label, isRight) in EnumerateSides(data))
            {
                DrawLegHandles(data, source, end, label, isRight);
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

                EditorGUI.BeginChangeCheck();
                if (handleMode == HandleMode.Rotate)
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
