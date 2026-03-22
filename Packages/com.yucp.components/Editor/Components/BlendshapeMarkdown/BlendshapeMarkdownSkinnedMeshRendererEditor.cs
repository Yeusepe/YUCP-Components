using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(SkinnedMeshRenderer), true)]
    [CanEditMultipleObjects]
    internal class BlendshapeMarkdownSkinnedMeshRendererEditor : UnityEditor.Editor
    {
        private static readonly GUIContent BoundsContent = EditorGUIUtility.TrTextContent("Bounds", "The bounding box that encapsulates the mesh.");
        private static readonly GUIContent QualityContent = EditorGUIUtility.TrTextContent("Quality", "Number of bones to use per vertex during skinning.");
        private static readonly GUIContent UpdateWhenOffscreenContent = EditorGUIUtility.TrTextContent("Update When Offscreen", "If an accurate bounding volume representation should be calculated every frame.");
        private static readonly GUIContent RootBoneContent = EditorGUIUtility.TrTextContent("Root Bone", "Transform with which the bounds move, and the space in which skinning is computed.");
        private static readonly GUIContent BlendshapesContent = EditorGUIUtility.TrTextContent("BlendShapes");

        private UnityEditor.Editor defaultEditor;
        private MethodInfo onMeshUiMethod;
        private MethodInfo drawMaterialsMethod;
        private MethodInfo lightingSettingsGuiMethod;
        private MethodInfo rayTracingSettingsGuiMethod;
        private MethodInfo otherSettingsGuiMethod;
        private MethodInfo onSceneGuiMethod;
        private MethodInfo doEditModeInspectorButtonMethod;
        private Type sceneViewEditModeType;

        private string searchText = string.Empty;

        private void OnEnable()
        {
            Type skinnedMeshRendererEditorType = FindType("UnityEditor.SkinnedMeshRendererEditor");
            if (skinnedMeshRendererEditorType == null)
            {
                return;
            }

            defaultEditor = CreateEditor(targets, skinnedMeshRendererEditorType);
            if (defaultEditor == null)
            {
                return;
            }

            onMeshUiMethod = FindMethodInHierarchy(defaultEditor.GetType(), "OnMeshUI", Type.EmptyTypes);
            drawMaterialsMethod = FindMethodInHierarchy(defaultEditor.GetType(), "DrawMaterials", Type.EmptyTypes);
            lightingSettingsGuiMethod = FindMethodInHierarchy(defaultEditor.GetType(), "LightingSettingsGUI", new[] { typeof(bool) });
            rayTracingSettingsGuiMethod = FindMethodInHierarchy(defaultEditor.GetType(), "RayTracingSettingsGUI", Type.EmptyTypes);
            otherSettingsGuiMethod = FindMethodInHierarchy(defaultEditor.GetType(), "OtherSettingsGUI", new[] { typeof(bool), typeof(bool), typeof(bool) });
            onSceneGuiMethod = FindMethodInHierarchy(defaultEditor.GetType(), "OnSceneGUI", Type.EmptyTypes);

            Type editModeType = FindType("UnityEditorInternal.EditMode");
            sceneViewEditModeType = editModeType?.GetNestedType("SceneViewEditMode", BindingFlags.Public | BindingFlags.NonPublic);
            Type toolModeOwnerType = FindType("UnityEditor.IToolModeOwner");
            doEditModeInspectorButtonMethod = FindStaticMethod(
                editModeType,
                "DoEditModeInspectorModeButton",
                sceneViewEditModeType != null && toolModeOwnerType != null
                    ? new[] { sceneViewEditModeType, typeof(string), typeof(GUIContent), toolModeOwnerType }
                    : null);
        }

        private void OnDisable()
        {
            if (defaultEditor != null)
            {
                DestroyImmediate(defaultEditor);
                defaultEditor = null;
            }
        }

        public override void OnInspectorGUI()
        {
            if (defaultEditor == null || !CanDrawCustomInspector())
            {
                DrawFallbackInspector();
                return;
            }

            if (!TryGetActiveConfig(out SkinnedMeshRenderer renderer, out BlendshapeMarkdownData config, out int duplicateCount))
            {
                DrawFallbackInspector();
                return;
            }

            serializedObject.Update();
            defaultEditor.serializedObject.Update();

            DrawEditModeButton();
            DrawBoundsSection();
            DrawBlendshapeMarkdownSection(renderer, config, duplicateCount);
            DrawStandardProperty("m_Quality", QualityContent);
            DrawStandardProperty("m_UpdateWhenOffscreen", UpdateWhenOffscreenContent);
            Invoke(defaultEditor, onMeshUiMethod);
            DrawStandardProperty("m_RootBone", RootBoneContent);
            Invoke(defaultEditor, drawMaterialsMethod);
            Invoke(defaultEditor, lightingSettingsGuiMethod, false);
            Invoke(defaultEditor, rayTracingSettingsGuiMethod);
            Invoke(defaultEditor, otherSettingsGuiMethod, false, true, false);

            serializedObject.ApplyModifiedProperties();
            defaultEditor.serializedObject.ApplyModifiedProperties();
        }

        public void OnSceneGUI()
        {
            if (defaultEditor != null && onSceneGuiMethod != null)
            {
                Invoke(defaultEditor, onSceneGuiMethod);
            }
        }

        public override bool HasPreviewGUI()
        {
            return defaultEditor != null && defaultEditor.HasPreviewGUI();
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            defaultEditor?.OnPreviewGUI(r, background);
        }

        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background)
        {
            defaultEditor?.OnInteractivePreviewGUI(r, background);
        }

        public override void OnPreviewSettings()
        {
            defaultEditor?.OnPreviewSettings();
        }

        public override GUIContent GetPreviewTitle()
        {
            return defaultEditor != null ? defaultEditor.GetPreviewTitle() : base.GetPreviewTitle();
        }

        public override string GetInfoString()
        {
            return defaultEditor != null ? defaultEditor.GetInfoString() : base.GetInfoString();
        }

        public override bool RequiresConstantRepaint()
        {
            return defaultEditor != null && defaultEditor.RequiresConstantRepaint();
        }

        private void DrawFallbackInspector()
        {
            if (defaultEditor != null)
            {
                defaultEditor.OnInspectorGUI();
            }
            else
            {
                DrawDefaultInspector();
            }
        }

        private bool CanDrawCustomInspector()
        {
            return onMeshUiMethod != null &&
                   drawMaterialsMethod != null &&
                   lightingSettingsGuiMethod != null &&
                   rayTracingSettingsGuiMethod != null &&
                   otherSettingsGuiMethod != null;
        }

        private bool TryGetActiveConfig(out SkinnedMeshRenderer renderer, out BlendshapeMarkdownData config, out int duplicateCount)
        {
            renderer = target as SkinnedMeshRenderer;
            config = null;
            duplicateCount = 0;

            if (renderer == null || targets.Length != 1 || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount == 0)
            {
                return false;
            }

            List<BlendshapeMarkdownData> configs = GetConfigsForRenderer(renderer);
            for (int index = 0; index < configs.Count; index++)
            {
                BlendshapeMarkdownData candidate = configs[index];
                if (candidate == null || !candidate.enableNativeInspectorIntegration)
                {
                    continue;
                }

                duplicateCount++;

                if (config == null)
                {
                    config = candidate;
                }
            }

            return config != null && config.replaceDefaultBlendshapeList;
        }

        private static List<BlendshapeMarkdownData> GetConfigsForRenderer(SkinnedMeshRenderer renderer)
        {
            var orderedConfigs = new List<BlendshapeMarkdownData>();
            var seen = new HashSet<int>();

            BlendshapeMarkdownData[] localConfigs = renderer.GetComponents<BlendshapeMarkdownData>();
            for (int index = 0; index < localConfigs.Length; index++)
            {
                BlendshapeMarkdownData localConfig = localConfigs[index];
                if (localConfig == null || !localConfig.TargetsRenderer(renderer))
                {
                    continue;
                }

                orderedConfigs.Add(localConfig);
                seen.Add(localConfig.GetInstanceID());
            }

            BlendshapeMarkdownData[] allConfigs = UnityEngine.Object.FindObjectsByType<BlendshapeMarkdownData>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < allConfigs.Length; index++)
            {
                BlendshapeMarkdownData config = allConfigs[index];
                if (config == null || seen.Contains(config.GetInstanceID()) || !config.TargetsRenderer(renderer))
                {
                    continue;
                }

                orderedConfigs.Add(config);
                seen.Add(config.GetInstanceID());
            }

            return orderedConfigs;
        }

        private void DrawEditModeButton()
        {
            if (doEditModeInspectorButtonMethod == null || sceneViewEditModeType == null || defaultEditor == null)
            {
                return;
            }

            try
            {
                object colliderMode = Enum.Parse(sceneViewEditModeType, "Collider");
                object editButton = typeof(PrimitiveBoundsHandle).GetField("editModeButton", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                doEditModeInspectorButtonMethod.Invoke(null, new[] { colliderMode, "Edit Bounds", editButton, defaultEditor });
            }
            catch
            {
                // Keep native inspector usable even if Unity changes this internal API.
            }
        }

        private void DrawBoundsSection()
        {
            SerializedProperty boundsProp = serializedObject.FindProperty("m_AABB");
            SerializedProperty dirtyBoundsProp = serializedObject.FindProperty("m_DirtyAABB");

            if (boundsProp == null)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(boundsProp, BoundsContent);
            if (EditorGUI.EndChangeCheck() && dirtyBoundsProp != null)
            {
                dirtyBoundsProp.boolValue = false;
            }
        }

        private void DrawStandardProperty(string propertyName, GUIContent label)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, label);
            }
        }

        private void DrawBlendshapeMarkdownSection(
            SkinnedMeshRenderer renderer,
            BlendshapeMarkdownData config,
            int duplicateCount)
        {
            SerializedProperty blendshapeWeightsProp = serializedObject.FindProperty("m_BlendShapeWeights");
            if (blendshapeWeightsProp == null)
            {
                return;
            }

            Mesh mesh = renderer.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0)
            {
                return;
            }

            if (blendshapeWeightsProp.arraySize != mesh.blendShapeCount)
            {
                blendshapeWeightsProp.arraySize = mesh.blendShapeCount;
            }

            EditorGUILayout.PropertyField(blendshapeWeightsProp, BlendshapesContent, false);
            if (!blendshapeWeightsProp.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;

            if (PlayerSettings.legacyClampBlendShapeWeights)
            {
                EditorGUILayout.HelpBox("Note that BlendShape weight range is clamped. This can be disabled in Player Settings.", MessageType.Info);
            }

            if (duplicateCount > 1)
            {
                EditorGUILayout.HelpBox("Multiple Blendshape Markdown components target this renderer. Using the first enabled config found.", MessageType.Warning);
            }

            DrawTopBar(config);

            BlendshapeMarkdownDocument document = BlendshapeMarkdownParser.Parse(renderer, config);
            if (document.HeadingCount == 0)
            {
                EditorGUILayout.HelpBox("No configured heading rules matched this mesh. Add section markers such as # Title or ==Body/Head== in your blendshape names, or update the rules on the Blendshape Markdown component.", MessageType.Warning);
            }

            DrawSectionNodes(document.Root.Children, 0, mesh, blendshapeWeightsProp, config, renderer);

            EditorGUI.indentLevel--;
        }

        private void DrawTopBar(BlendshapeMarkdownData config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Blendshape Markdown", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Settings", GUILayout.Width(70f)))
            {
                Selection.activeObject = config;
                EditorGUIUtility.PingObject(config);
            }
            EditorGUILayout.EndHorizontal();

            if (config.showSearchBar)
            {
                EditorGUILayout.BeginHorizontal();
                searchText = EditorGUILayout.TextField("Search", searchText);
                if (GUILayout.Button("Clear", GUILayout.Width(50f)))
                {
                    searchText = string.Empty;
                    GUI.FocusControl(string.Empty);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Expand All"))
            {
                ApplyExpandedStateToAllGroups(target as SkinnedMeshRenderer, config, true);
            }

            if (GUILayout.Button("Collapse All"))
            {
                ApplyExpandedStateToAllGroups(target as SkinnedMeshRenderer, config, false);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawSectionNodes(
            List<BlendshapeMarkdownNode> nodes,
            int depth,
            Mesh mesh,
            SerializedProperty blendshapeWeightsProp,
            BlendshapeMarkdownData config,
            SkinnedMeshRenderer renderer)
        {
            var pendingBlendshapes = new List<BlendshapeMarkdownBlendshapeItem>();

            for (int index = 0; index < nodes.Count; index++)
            {
                if (nodes[index] is BlendshapeMarkdownBlendshapeItem item)
                {
                    if (BlendshapeMatchesSearch(item))
                    {
                        pendingBlendshapes.Add(item);
                    }

                    continue;
                }

                if (nodes[index] is BlendshapeMarkdownSection section)
                {
                    if (!SectionMatchesSearch(section))
                    {
                        continue;
                    }

                    FlushPendingBlendshapes(pendingBlendshapes, depth, mesh, blendshapeWeightsProp, config, renderer);
                    DrawSection(section, depth, mesh, blendshapeWeightsProp, config, renderer);
                }
            }

            FlushPendingBlendshapes(pendingBlendshapes, depth, mesh, blendshapeWeightsProp, config, renderer);
        }

        private void DrawSection(
            BlendshapeMarkdownSection section,
            int depth,
            Mesh mesh,
            SerializedProperty blendshapeWeightsProp,
            BlendshapeMarkdownData config,
            SkinnedMeshRenderer renderer)
        {
            int subtreeBlendshapeCount = section.CountBlendshapeDescendants();
            BlendshapeMarkdownSectionStyle style = BlendshapeMarkdownColorResolver.Resolve(config, section);
            bool isExpanded = GetExpandedState(GetSectionStateKey(renderer, config, section.Key), depth == 0 ? config.expandTopLevelByDefault : config.expandNestedByDefault);

            DrawSectionHeader(section.Title, subtreeBlendshapeCount, depth, style, config.showBlendshapeCounts, ref isExpanded, section, renderer, config);

            if (!isExpanded)
            {
                return;
            }

            DrawSectionNodes(section.Children, depth + 1, mesh, blendshapeWeightsProp, config, renderer);
        }

        private void FlushPendingBlendshapes(
            List<BlendshapeMarkdownBlendshapeItem> pendingBlendshapes,
            int depth,
            Mesh mesh,
            SerializedProperty blendshapeWeightsProp,
            BlendshapeMarkdownData config,
            SkinnedMeshRenderer renderer)
        {
            if (pendingBlendshapes.Count == 0)
            {
                return;
            }

            if (depth > 0)
            {
                DrawBlendshapeItemsDirect(pendingBlendshapes, mesh, blendshapeWeightsProp, depth);
            }
            else
            {
                DrawTopLevelPendingBlendshapes(pendingBlendshapes, depth, mesh, blendshapeWeightsProp, config, renderer);
            }

            pendingBlendshapes.Clear();
        }

        private void DrawTopLevelPendingBlendshapes(
            List<BlendshapeMarkdownBlendshapeItem> pendingBlendshapes,
            int depth,
            Mesh mesh,
            SerializedProperty blendshapeWeightsProp,
            BlendshapeMarkdownData config,
            SkinnedMeshRenderer renderer)
        {
            int startIndex = 0;
            while (startIndex < pendingBlendshapes.Count)
            {
                bool isAutoGroup = ShouldAutoGroupTopLevel(pendingBlendshapes[startIndex], config);
                int endIndex = startIndex + 1;

                while (endIndex < pendingBlendshapes.Count &&
                       ShouldAutoGroupTopLevel(pendingBlendshapes[endIndex], config) == isAutoGroup)
                {
                    endIndex++;
                }

                DrawTopLevelBlendshapeCluster(pendingBlendshapes, startIndex, endIndex - startIndex, isAutoGroup, depth, mesh, blendshapeWeightsProp, config, renderer);
                startIndex = endIndex;
            }
        }

        private void DrawTopLevelBlendshapeCluster(
            List<BlendshapeMarkdownBlendshapeItem> items,
            int startIndex,
            int count,
            bool isAutoGroup,
            int depth,
            Mesh mesh,
            SerializedProperty blendshapeWeightsProp,
            BlendshapeMarkdownData config,
            SkinnedMeshRenderer renderer)
        {
            if (count <= 0)
            {
                return;
            }

            if (isAutoGroup)
            {
                string groupKey = GetSyntheticGroupStateKey(renderer, config, "AutoGroup", items[startIndex].SourceIndex);
                bool isExpanded = GetExpandedState(groupKey, config.expandTopLevelByDefault);
                DrawSyntheticGroupHeader(config.GetTopLevelAutoGroupTitle(), count, depth, GetDefaultSyntheticGroupStyle(), config.showBlendshapeCounts, ref isExpanded, groupKey);

                if (isExpanded)
                {
                    for (int index = 0; index < count; index++)
                    {
                        DrawBlendshapeSlider(items[startIndex + index], mesh, blendshapeWeightsProp, depth + 1);
                    }
                }

                return;
            }

            if (config.showUngroupedBlendshapes)
            {
                string groupKey = GetSyntheticGroupStateKey(renderer, config, "Ungrouped", items[startIndex].SourceIndex);
                bool isExpanded = GetExpandedState(groupKey, config.expandTopLevelByDefault);
                DrawSyntheticGroupHeader(config.GetUngroupedSectionTitle(), count, depth, GetDefaultSyntheticGroupStyle(), config.showBlendshapeCounts, ref isExpanded, groupKey);

                if (isExpanded)
                {
                    for (int index = 0; index < count; index++)
                    {
                        DrawBlendshapeSlider(items[startIndex + index], mesh, blendshapeWeightsProp, depth + 1);
                    }
                }

                return;
            }

            for (int index = 0; index < count; index++)
            {
                DrawBlendshapeSlider(items[startIndex + index], mesh, blendshapeWeightsProp, depth);
            }
        }

        private void DrawSectionHeader(
            string title,
            int count,
            int depth,
            BlendshapeMarkdownSectionStyle style,
            bool showCount,
            ref bool expanded,
            BlendshapeMarkdownSection section,
            SkinnedMeshRenderer renderer,
            BlendshapeMarkdownData config)
        {
            string stateKey = GetSectionStateKey(renderer, config, section.Key);
            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 6f);
            float indent = depth * 14f;
            Rect backgroundRect = new Rect(rowRect.x + indent, rowRect.y + 1f, rowRect.width - indent, rowRect.height - 2f);
            DrawHeaderBackground(backgroundRect, style.BackgroundColor);

            if (HandleHeaderClick(backgroundRect, expanded, out bool newExpanded))
            {
                expanded = newExpanded;
                SetExpandedState(stateKey, expanded);
                if (Event.current.shift)
                {
                    ApplyExpandedStateRecursive(section, renderer, config, expanded, depth);
                }
            }

            Rect foldoutRect = new Rect(backgroundRect.x + 4f, backgroundRect.y + 2f, 13f, EditorGUIUtility.singleLineHeight);
            EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);

            Rect labelRect = new Rect(foldoutRect.xMax + 2f, backgroundRect.y + 2f, backgroundRect.width - 50f, EditorGUIUtility.singleLineHeight);
            var labelStyle = new GUIStyle(EditorStyles.boldLabel);
            labelStyle.normal.textColor = style.TextColor;
            GUI.Label(labelRect, title, labelStyle);

            if (showCount)
            {
                Rect countRect = new Rect(backgroundRect.xMax - 46f, backgroundRect.y + 2f, 42f, EditorGUIUtility.singleLineHeight);
                var countStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };
                countStyle.normal.textColor = style.TextColor;
                GUI.Label(countRect, count.ToString(), countStyle);
            }
        }

        private void DrawSyntheticGroupHeader(
            string title,
            int count,
            int depth,
            BlendshapeMarkdownSectionStyle style,
            bool showCount,
            ref bool expanded,
            string stateKey)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 6f);
            float indent = depth * 14f;
            Rect backgroundRect = new Rect(rowRect.x + indent, rowRect.y + 1f, rowRect.width - indent, rowRect.height - 2f);
            DrawHeaderBackground(backgroundRect, style.BackgroundColor);

            if (HandleHeaderClick(backgroundRect, expanded, out bool newExpanded))
            {
                expanded = newExpanded;
                SetExpandedState(stateKey, expanded);
            }

            Rect foldoutRect = new Rect(backgroundRect.x + 4f, backgroundRect.y + 2f, 13f, EditorGUIUtility.singleLineHeight);
            EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);

            Rect labelRect = new Rect(foldoutRect.xMax + 2f, backgroundRect.y + 2f, backgroundRect.width - 50f, EditorGUIUtility.singleLineHeight);
            var labelStyle = new GUIStyle(EditorStyles.label);
            labelStyle.normal.textColor = style.TextColor;
            GUI.Label(labelRect, title, labelStyle);

            if (showCount)
            {
                Rect countRect = new Rect(backgroundRect.xMax - 46f, backgroundRect.y + 2f, 42f, EditorGUIUtility.singleLineHeight);
                var countStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };
                countStyle.normal.textColor = style.TextColor;
                GUI.Label(countRect, count.ToString(), countStyle);
            }
        }

        private static void DrawHeaderBackground(Rect rect, Color backgroundColor)
        {
            EditorGUI.DrawRect(rect, backgroundColor);

            Color borderColor = new Color(0f, 0f, 0f, 0.18f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), borderColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), borderColor);
        }

        private static bool HandleHeaderClick(Rect rect, bool currentState, out bool newState)
        {
            Event currentEvent = Event.current;
            newState = currentState;

            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 || !rect.Contains(currentEvent.mousePosition))
            {
                return false;
            }

            newState = !currentState;
            currentEvent.Use();
            GUI.changed = true;
            return true;
        }

        private void DrawBlendshapeSlider(
            BlendshapeMarkdownBlendshapeItem item,
            Mesh mesh,
            SerializedProperty blendshapeWeightsProp,
            int depth)
        {
            Rect rowRect = EditorGUILayout.GetControlRect();
            rowRect = EditorGUI.IndentedRect(rowRect);
            rowRect.x += depth * 14f;
            rowRect.width -= depth * 14f;

            GetBlendshapeWeightRange(mesh, item.Index, out float minWeight, out float maxWeight);
            SerializedProperty weightProp = blendshapeWeightsProp.GetArrayElementAtIndex(item.Index);
            EditorGUI.Slider(rowRect, weightProp, minWeight, maxWeight, item.Name);
        }

        private bool SectionMatchesSearch(BlendshapeMarkdownSection section)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            if (ContainsSearch(section.Title) || ContainsSearch(section.FullPath))
            {
                return true;
            }

            for (int index = 0; index < section.Children.Count; index++)
            {
                if (section.Children[index] is BlendshapeMarkdownBlendshapeItem item && BlendshapeMatchesSearch(item))
                {
                    return true;
                }

                if (section.Children[index] is BlendshapeMarkdownSection childSection && SectionMatchesSearch(childSection))
                {
                    return true;
                }
            }

            return false;
        }

        private bool BlendshapeMatchesSearch(BlendshapeMarkdownBlendshapeItem item)
        {
            return string.IsNullOrWhiteSpace(searchText) || ContainsSearch(item.Name);
        }

        private bool ContainsSearch(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(searchText ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void GetBlendshapeWeightRange(Mesh mesh, int blendshapeIndex, out float minWeight, out float maxWeight)
        {
            minWeight = 0f;
            maxWeight = 0f;

            int frameCount = mesh.GetBlendShapeFrameCount(blendshapeIndex);
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                float frameWeight = mesh.GetBlendShapeFrameWeight(blendshapeIndex, frameIndex);
                minWeight = Mathf.Min(minWeight, frameWeight);
                maxWeight = Mathf.Max(maxWeight, frameWeight);
            }
        }

        private static string GetSectionStateKey(SkinnedMeshRenderer renderer, BlendshapeMarkdownData config, string sectionKey)
        {
            return $"BlendshapeMarkdown_Section_{renderer.GetInstanceID()}_{config.GetInstanceID()}_{sectionKey}";
        }

        private static string GetSyntheticGroupStateKey(SkinnedMeshRenderer renderer, BlendshapeMarkdownData config, string groupType, int sourceIndex)
        {
            return $"BlendshapeMarkdown_{groupType}_{renderer.GetInstanceID()}_{config.GetInstanceID()}_{sourceIndex}";
        }

        private static bool GetExpandedState(string key, bool defaultValue)
        {
            return SessionState.GetBool(key, defaultValue);
        }

        private static void SetExpandedState(string key, bool value)
        {
            SessionState.SetBool(key, value);
        }

        private static void ApplyExpandedStateRecursive(
            BlendshapeMarkdownSection section,
            SkinnedMeshRenderer renderer,
            BlendshapeMarkdownData config,
            bool expanded,
            int visualDepth)
        {
            SetExpandedState(GetSectionStateKey(renderer, config, section.Key), expanded);

            for (int index = 0; index < section.Children.Count; index++)
            {
                if (section.Children[index] is BlendshapeMarkdownSection childSection)
                {
                    ApplyExpandedStateRecursive(childSection, renderer, config, expanded, visualDepth + 1);
                }
            }
        }

        private static void ApplyExpandedStateToAllGroups(
            SkinnedMeshRenderer renderer,
            BlendshapeMarkdownData config,
            bool expanded)
        {
            if (renderer == null || config == null)
            {
                return;
            }

            BlendshapeMarkdownDocument document = BlendshapeMarkdownParser.Parse(renderer, config);

            int rootClusterStart = -1;
            bool rootClusterIsAutoGroup = false;

            for (int index = 0; index < document.Root.Children.Count; index++)
            {
                if (document.Root.Children[index] is BlendshapeMarkdownBlendshapeItem item)
                {
                    bool isAutoGroup = ShouldAutoGroupTopLevel(item, config);
                    if (rootClusterStart < 0)
                    {
                        rootClusterStart = item.SourceIndex;
                        rootClusterIsAutoGroup = isAutoGroup;
                    }
                    else if (rootClusterIsAutoGroup != isAutoGroup)
                    {
                        SetExpandedState(GetSyntheticGroupStateKey(renderer, config, rootClusterIsAutoGroup ? "AutoGroup" : "Ungrouped", rootClusterStart), expanded);
                        rootClusterStart = item.SourceIndex;
                        rootClusterIsAutoGroup = isAutoGroup;
                    }

                    continue;
                }

                if (rootClusterStart >= 0)
                {
                    SetExpandedState(GetSyntheticGroupStateKey(renderer, config, rootClusterIsAutoGroup ? "AutoGroup" : "Ungrouped", rootClusterStart), expanded);
                    rootClusterStart = -1;
                }

                if (document.Root.Children[index] is BlendshapeMarkdownSection section)
                {
                    ApplyExpandedStateRecursive(section, renderer, config, expanded, 0);
                }
            }

            if (rootClusterStart >= 0)
            {
                SetExpandedState(GetSyntheticGroupStateKey(renderer, config, rootClusterIsAutoGroup ? "AutoGroup" : "Ungrouped", rootClusterStart), expanded);
            }
        }

        private static bool ShouldAutoGroupTopLevel(BlendshapeMarkdownBlendshapeItem item, BlendshapeMarkdownData config)
        {
            if (item == null || config == null)
            {
                return false;
            }

            string prefix = config.GetTopLevelAutoGroupPrefix();
            return !string.IsNullOrEmpty(prefix) &&
                   !string.IsNullOrEmpty(item.Name) &&
                   item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static BlendshapeMarkdownSectionStyle GetDefaultSyntheticGroupStyle()
        {
            return EditorGUIUtility.isProSkin
                ? new BlendshapeMarkdownSectionStyle(false, new Color(0.94f, 0.94f, 0.96f), new Color(0.22f, 0.22f, 0.24f, 0.95f))
                : new BlendshapeMarkdownSectionStyle(false, new Color(0.16f, 0.16f, 0.18f), new Color(0.86f, 0.86f, 0.89f, 1f));
        }

        private void DrawBlendshapeItemsDirect(
            List<BlendshapeMarkdownBlendshapeItem> items,
            Mesh mesh,
            SerializedProperty blendshapeWeightsProp,
            int depth)
        {
            for (int index = 0; index < items.Count; index++)
            {
                DrawBlendshapeSlider(items[index], mesh, blendshapeWeightsProp, depth);
            }
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static MethodInfo FindMethodInHierarchy(Type type, string name, Type[] parameterTypes)
        {
            while (type != null)
            {
                MethodInfo method = parameterTypes != null
                    ? type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, parameterTypes, null)
                    : null;

                if (method != null)
                {
                    return method;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static MethodInfo FindStaticMethod(Type type, string name, Type[] parameterTypes)
        {
            if (type == null || parameterTypes == null)
            {
                return null;
            }

            return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, parameterTypes, null);
        }

        private static void Invoke(object instance, MethodInfo method, params object[] args)
        {
            if (instance == null || method == null)
            {
                return;
            }

            method.Invoke(instance, args);
        }
    }
}
