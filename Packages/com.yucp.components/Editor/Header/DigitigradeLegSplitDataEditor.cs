using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YUCP.Components;
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
        private bool foldBones;
        private HandleMode handleMode = HandleMode.Move;

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneView;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneView;
        }

        public override void OnInspectorGUI()
        {
            var data = (DigitigradeLegSplitData)target;

            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Inserts extra joints into the lower leg so the rig can fold as a digitigrade leg. " +
                "Applied at build time to every skinned mesh bound to the leg, including VRCFury " +
                "armature-linked clothing.", MessageType.None);

            DrawBones(data);
            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("splits"), true);

            handleMode = (HandleMode)EditorGUILayout.EnumPopup(
                new GUIContent("Scene Handle", "Move drags the joint anywhere, on or off the bone line. Rotate tilts the cut plane."),
                handleMode);
            EditorGUILayout.HelpBox(
                handleMode == HandleMode.Rotate
                    ? "The yellow disc is the weight boundary. Tilt it until it follows the crease on the mesh."
                    : "Drag the joint anywhere -- the blue line is only the straight chord between the two bone origins, not a rail. Off-line movement is stored as the split's offset.",
                MessageType.None);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("blendBand"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mirrorRightLeg"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("verboseLogging"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawProportions(data);

            EditorGUILayout.Space();
            DrawPreviewControls(data);

            var summary = data.GetBuildSummary();
            if (!string.IsNullOrEmpty(summary))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last build", summary, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawBones(DigitigradeLegSplitData data)
        {
            foldBones = EditorGUILayout.Foldout(foldBones, "Bones", true);
            if (!foldBones)
            {
                if (data.leftSourceBone == null || data.rightSourceBone == null)
                {
                    EditorGUILayout.HelpBox("Leg bones are not resolved. Open Bones and assign them, or use Auto-Resolve.", MessageType.Warning);
                }
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leftSourceBone"), new GUIContent("Left Lower Leg"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leftEndBone"), new GUIContent("Left Foot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rightSourceBone"), new GUIContent("Right Lower Leg"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rightEndBone"), new GUIContent("Right Foot"));

            if (GUILayout.Button("Auto-Resolve From Humanoid"))
            {
                Undo.RecordObject(data, "Resolve leg bones");
                var resolved = data.ResolveBones();
                // ResolveBones writes straight to the target, so re-sync before the pending
                // serializedObject changes get applied over the top of it.
                serializedObject.Update();
                if (!resolved)
                {
                    EditorUtility.DisplayDialog("YUCP Digitigrade Legs",
                        "Could not resolve the leg bones. The avatar needs a humanoid rig with the leg bones mapped, or you can assign them by hand.",
                        "OK");
                }
            }
            EditorGUI.indentLevel--;
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

        private void DrawPreviewControls(DigitigradeLegSplitData data)
        {
            var animator = data.GetComponentInParent<Animator>();
            var state = animator != null ? animator.GetComponent<DigitigradeLegSplitPreviewState>() : null;

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Preview is disabled in Play Mode. Applying it here would bake into the play " +
                    "scene and be discarded on exit.", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                "Preview runs the exact same code the build uses, against this scene instance. " +
                "Pose the leg to check the fold, then Revert before saving.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(state != null))
                {
                    if (GUILayout.Button("Preview In Scene")) ApplyPreview(data);
                }

                using (new EditorGUI.DisabledScope(state == null))
                {
                    if (GUILayout.Button("Revert Preview")) DigitigradeLegSplitPreview.Revert(state);
                }
            }

            if (state != null)
            {
                EditorGUILayout.HelpBox(
                    $"Preview applied: {state.createdBones.Count} bone(s), {state.skins.Count} mesh(es). " +
                    "The record is stored on the avatar, so Revert still works after a domain reload.",
                    MessageType.Info);
            }

            if (animator != null && DigitigradeLegSplitPreview.NeedsCleanup(animator.gameObject))
            {
                EditorGUILayout.HelpBox(
                    "This avatar has skinned meshes with missing bone references, which collapses the " +
                    "mesh. That happens when a preview's bones were destroyed without reverting. " +
                    "Clean Up strips the dangling references and any leftover generated bones.",
                    MessageType.Error);

                if (GUILayout.Button("Clean Up Generated Bones"))
                {
                    var removed = DigitigradeLegSplitPreview.CleanUp(animator.gameObject, data);
                    Debug.Log("[YUCP Digitigrade Legs] Cleanup removed " + removed + " dangling reference(s).", data);
                }
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
                    blendBand = data.blendBand
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
