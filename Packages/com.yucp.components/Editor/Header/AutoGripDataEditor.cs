using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.Components.HandPoses;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(AutoGripData))]
    public class AutoGripDataEditor : UnityEditor.Editor
    {
        private AutoGripData data;
        private VisualElement statusContainer;
        private VisualElement toggleFieldsContainer;

        private Animator cachedAnimator;
        private MeshCollider tempCollider;

        private static readonly YUCPFingerType[] FingerOrder =
        {
            YUCPFingerType.Thumb,
            YUCPFingerType.Index,
            YUCPFingerType.Middle,
            YUCPFingerType.Ring,
            YUCPFingerType.Little
        };

        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

        private static readonly Color[] FingerColors =
        {
            new Color(0.95f, 0.35f, 0.30f),
            new Color(0.95f, 0.65f, 0.25f),
            new Color(0.95f, 0.90f, 0.30f),
            new Color(0.40f, 0.85f, 0.45f),
            new Color(0.40f, 0.55f, 0.95f)
        };

        private void OnEnable()
        {
            data = (AutoGripData)target;
            SceneView.duringSceneGui += OnSceneGUI;
            FindAnimator();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            DestroyTempCollider();
        }

        #region Inspector UI

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();

            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Auto Grip"));

            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(AutoGripData));
            if (betaWarning != null) root.Add(betaWarning);

            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(AutoGripData));
            if (supportBanner != null) root.Add(supportBanner);

            BuildStatusSection(root);
            BuildGripSettingsCard(root);
            BuildFingerPoseCard(root);
            BuildToggleSettingsCard(root);
            BuildDebugFoldout(root);

            root.schedule.Execute(RefreshStatus).Every(500);

            return root;
        }

        private void BuildStatusSection(VisualElement root)
        {
            statusContainer = new VisualElement();
            statusContainer.name = "status-container";
            root.Add(statusContainer);
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (data == null || statusContainer == null) return;
            statusContainer.Clear();
            FindAnimator();

            if (cachedAnimator == null)
            {
                statusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "No avatar found. Place this component under an avatar with an Animator.",
                    YUCPUIToolkitHelper.MessageType.Warning));
                return;
            }

            if (!cachedAnimator.isHuman)
            {
                statusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Avatar must have a Humanoid rig.",
                    YUCPUIToolkitHelper.MessageType.Error));
                return;
            }

            if (!data.fingerPoseInitialized)
                statusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Finger pose not initialized. Click 'Initialize from Avatar' to place fingertip gizmos, then drag them onto the object surface.",
                    YUCPUIToolkitHelper.MessageType.Info));

            if (!data.createToggle && !data.useExistingToggle)
                statusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "No toggle configured. Enable 'Create Toggle' or 'Use Existing Toggle'.",
                    YUCPUIToolkitHelper.MessageType.Warning));
        }

        private void BuildGripSettingsCard(VisualElement root)
        {
            var card = YUCPUIToolkitHelper.CreateCard("Grip Settings", "Configure which hand grips the prop");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            content.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("handTarget"), "Hand"));

            root.Add(card);
        }

        private void BuildFingerPoseCard(VisualElement root)
        {
            var card = YUCPUIToolkitHelper.CreateCard("Finger Pose", "Position fingertips on the object surface");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            content.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Drag the colored spheres in the Scene view onto the object's surface. " +
                "Each sphere represents a fingertip target. FABRIK IK solves finger positions at runtime.",
                YUCPUIToolkitHelper.MessageType.Info));

            var buttonsRow = new VisualElement();
            buttonsRow.style.flexDirection = FlexDirection.Row;
            buttonsRow.style.marginTop = 4;

            var initButton = YUCPUIToolkitHelper.CreateButton("Initialize from Avatar", InitializeFingersFromAvatar, YUCPUIToolkitHelper.ButtonVariant.Primary);
            initButton.style.flexGrow = 1;
            initButton.style.marginRight = 4;
            buttonsRow.Add(initButton);

            var resetButton = YUCPUIToolkitHelper.CreateButton("Reset Fingers", ResetFingers, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            resetButton.style.flexGrow = 1;
            buttonsRow.Add(resetButton);

            content.Add(buttonsRow);
            root.Add(card);
        }

        private void BuildToggleSettingsCard(VisualElement root)
        {
            var card = YUCPUIToolkitHelper.CreateCard("Toggle Settings", "VRCFury toggle integration");
            var content = YUCPUIToolkitHelper.GetCardContent(card);

            content.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useExistingToggle"), "Use Existing Toggle"));

            toggleFieldsContainer = new VisualElement();
            toggleFieldsContainer.name = "toggle-fields";

            var selectedToggleField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("selectedToggle"), "Selected Toggle");
            selectedToggleField.name = "selected-toggle-field";
            toggleFieldsContainer.Add(selectedToggleField);

            var createToggleField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("createToggle"), "Create Toggle");
            createToggleField.name = "create-toggle-field";
            toggleFieldsContainer.Add(createToggleField);

            var menuPathField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("toggleMenuPath"), "Menu Path");
            menuPathField.name = "menu-path-field";
            toggleFieldsContainer.Add(menuPathField);

            toggleFieldsContainer.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("toggleSaved"), "Saved"));
            toggleFieldsContainer.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("toggleDefaultOn"), "Default On"));

            content.Add(toggleFieldsContainer);
            root.Add(card);

            UpdateToggleFieldVisibility();
            root.schedule.Execute(UpdateToggleFieldVisibility).Every(200);
        }

        private void UpdateToggleFieldVisibility()
        {
            if (data == null || toggleFieldsContainer == null) return;

            var sel = toggleFieldsContainer.Q("selected-toggle-field");
            var crt = toggleFieldsContainer.Q("create-toggle-field");
            var mp = toggleFieldsContainer.Q("menu-path-field");

            if (sel != null) sel.style.display = data.useExistingToggle ? DisplayStyle.Flex : DisplayStyle.None;
            if (crt != null) crt.style.display = data.useExistingToggle ? DisplayStyle.None : DisplayStyle.Flex;
            if (mp != null) mp.style.display = (!data.useExistingToggle && data.createToggle) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BuildDebugFoldout(VisualElement root)
        {
            var foldout = YUCPUIToolkitHelper.CreateFoldout("Debug", false);
            foldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("verboseLogging"), "Verbose Logging"));
            root.Add(foldout);
        }

        #endregion

        #region Scene Gizmos

        private void OnSceneGUI(SceneView sceneView)
        {
            if (data == null) return;
            if (Selection.activeGameObject != data.gameObject) return;
            if (!data.fingerPoseInitialized) return;
            if (data.fingerTipLocals == null || data.fingerTipLocals.Length != AutoGripData.FingerCount) return;

            EnsureTempCollider();

            for (int i = 0; i < AutoGripData.FingerCount; i++)
            {
                Vector3 tipWorld = data.GetFingerTipWorld(i);
                Handles.color = FingerColors[i];

                float handleSize = HandleUtility.GetHandleSize(tipWorld) * 0.04f;

                EditorGUI.BeginChangeCheck();
#pragma warning disable CS0618
                Vector3 newTipWorld = Handles.FreeMoveHandle(
                    tipWorld, handleSize, Vector3.zero, Handles.SphereHandleCap);
#pragma warning restore CS0618
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 snapped = SnapToSurface(newTipWorld);
                    Undo.RecordObject(data, $"Move {FingerNames[i]} Tip");
                    data.SetFingerTipWorld(i, snapped);
                    EditorUtility.SetDirty(data);
                }

                Handles.Label(tipWorld + Vector3.up * handleSize * 1.5f, FingerNames[i],
                    new GUIStyle("label") { normal = { textColor = FingerColors[i] }, fontSize = 10 });
            }

            DrawFingerBoneWires();
        }

        private void DrawFingerBoneWires()
        {
            FindAnimator();
            if (cachedAnimator == null || !cachedAnimator.isHuman) return;

            YUCPHandSide side = GetActiveHandSide();

            for (int i = 0; i < AutoGripData.FingerCount; i++)
            {
                Handles.color = FingerColors[i] * 0.6f;

                var (_, proximal, intermediate, distal) =
                    YUCPAvatarRigHelper.GetFingerBones(cachedAnimator, side, FingerOrder[i]);

                if (proximal == null) continue;

                Vector3 tipWorld = data.GetFingerTipWorld(i);

                if (intermediate != null)
                {
                    Handles.DrawLine(proximal.position, intermediate.position);
                    if (distal != null)
                    {
                        Handles.DrawLine(intermediate.position, distal.position);
                        Handles.DrawLine(distal.position, tipWorld);
                    }
                    else
                    {
                        Handles.DrawLine(intermediate.position, tipWorld);
                    }
                }
                else
                {
                    Handles.DrawLine(proximal.position, tipWorld);
                }
            }
        }

        #endregion

        #region Surface Snapping

        private Vector3 SnapToSurface(Vector3 worldPos)
        {
            var meshFilters = data.GetComponentsInChildren<MeshFilter>();
            var skinnedRenderers = data.GetComponentsInChildren<SkinnedMeshRenderer>();

            Vector3 closestPoint = worldPos;
            float closestDist = float.MaxValue;

            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;
                Vector3 candidate = FindClosestPointOnMesh(mf.sharedMesh, mf.transform, worldPos);
                float dist = Vector3.Distance(candidate, worldPos);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestPoint = candidate;
                }
            }

            foreach (var sr in skinnedRenderers)
            {
                if (sr.sharedMesh == null) continue;
                Mesh baked = new Mesh();
                sr.BakeMesh(baked);
                Vector3 candidate = FindClosestPointOnMesh(baked, sr.transform, worldPos);
                float dist = Vector3.Distance(candidate, worldPos);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestPoint = candidate;
                }
                Object.DestroyImmediate(baked);
            }

            return closestPoint;
        }

        private Vector3 FindClosestPointOnMesh(Mesh mesh, Transform meshTransform, Vector3 worldPos)
        {
            Vector3 localPos = meshTransform.InverseTransformPoint(worldPos);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            Vector3 closest = localPos;
            float minDist = float.MaxValue;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]];
                Vector3 b = vertices[triangles[t + 1]];
                Vector3 c = vertices[triangles[t + 2]];

                Vector3 pt = ClosestPointOnTriangle(localPos, a, b, c);
                float dist = (pt - localPos).sqrMagnitude;
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = pt;
                }
            }

            return meshTransform.TransformPoint(closest);
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denom = 1f / (va + vb + vc);
            float v2 = vb * denom;
            float w2 = vc * denom;
            return a + ab * v2 + ac * w2;
        }

        private void EnsureTempCollider()
        {
            // no-op: surface snapping uses direct triangle math instead of physics collider
        }

        private void DestroyTempCollider()
        {
            if (tempCollider != null)
            {
                Object.DestroyImmediate(tempCollider);
                tempCollider = null;
            }
        }

        #endregion

        #region Helpers

        private void InitializeFingersFromAvatar()
        {
            FindAnimator();
            if (cachedAnimator == null || !cachedAnimator.isHuman)
            {
                EditorUtility.DisplayDialog("Auto Grip",
                    "No humanoid avatar found. Place this component under an avatar.", "OK");
                return;
            }

            Undo.RecordObject(data, "Initialize Finger Pose");

            YUCPHandSide side = GetActiveHandSide();

            if (data.fingerTipLocals == null || data.fingerTipLocals.Length != AutoGripData.FingerCount)
                data.fingerTipLocals = new Vector3[AutoGripData.FingerCount];

            for (int i = 0; i < AutoGripData.FingerCount; i++)
            {
                Vector3 tipWorld = GetFingerTipPosition(cachedAnimator, side, FingerOrder[i]);
                Vector3 snapped = SnapToSurface(tipWorld);
                data.SetFingerTipWorld(i, snapped);
            }

            data.fingerPoseInitialized = true;
            EditorUtility.SetDirty(data);
            SceneView.RepaintAll();
        }

        private void ResetFingers()
        {
            Undo.RecordObject(data, "Reset Finger Pose");
            data.fingerTipLocals = new Vector3[AutoGripData.FingerCount];
            data.fingerPoseInitialized = false;
            EditorUtility.SetDirty(data);
            SceneView.RepaintAll();
        }

        private void FindAnimator()
        {
            if (data == null) return;
            cachedAnimator = data.GetComponentInParent<Animator>();
        }

        private YUCPHandSide GetActiveHandSide()
        {
            if (data.handTarget == HandTarget.Left) return YUCPHandSide.Left;
            return YUCPHandSide.Right;
        }

        private static Vector3 GetFingerTipPosition(Animator animator, YUCPHandSide handSide, YUCPFingerType fingerType)
        {
            if (animator == null || fingerType == YUCPFingerType.None) return Vector3.zero;

            var (_, proximal, intermediate, distal) = YUCPAvatarRigHelper.GetFingerBones(animator, handSide, fingerType);

            if (distal != null)
            {
                if (distal.childCount > 0)
                {
                    float best = float.MaxValue;
                    Vector3 bestPos = distal.position;
                    for (int c = 0; c < distal.childCount; c++)
                    {
                        float d = Vector3.Distance(distal.position, distal.GetChild(c).position);
                        if (d > 0.001f && d < best) { best = d; bestPos = distal.GetChild(c).position; }
                    }
                    if (best < float.MaxValue) return bestPos;
                }

                float tipLen = 0.01f;
                if (distal.parent != null)
                {
                    float parentDist = Vector3.Distance(distal.parent.position, distal.position);
                    if (parentDist > 0.001f) tipLen = parentDist * 0.7f;
                }
                return distal.position + distal.rotation * (Vector3.forward * tipLen);
            }

            if (intermediate != null) return intermediate.position;
            if (proximal != null) return proximal.position;
            return Vector3.zero;
        }

        #endregion
    }
}
