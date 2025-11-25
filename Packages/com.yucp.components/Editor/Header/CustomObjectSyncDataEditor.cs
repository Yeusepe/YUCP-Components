using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(CustomObjectSyncData))]
    public class CustomObjectSyncDataEditor : UnityEditor.Editor
    {
        private CustomObjectSyncData data;

        private SerializedProperty quickSyncProp;
        private SerializedProperty referenceFrameProp;
        private SerializedProperty maxRadiusProp;
        private SerializedProperty positionPrecisionProp;
        private SerializedProperty rotationPrecisionProp;
        private SerializedProperty bitCountProp;
        private SerializedProperty rotationEnabledProp;
        private SerializedProperty addDampingProp;
        private SerializedProperty dampingValueProp;
        private SerializedProperty addDebugProp;
        private SerializedProperty writeDefaultsProp;
        private SerializedProperty menuLocationProp;
        private SerializedProperty syncGroupIdProp;
        private SerializedProperty enableGroupingProp;
        private SerializedProperty showSceneGizmoProp;
        private SerializedProperty verboseLoggingProp;
        private SerializedProperty includeCreditsProp;

        private GUIStyle sectionTitleStyle;
        private GUIStyle statsValueStyle;
        private GUIStyle miniWrapStyle;
        private GUIStyle budgetCardStyle;
        private GUIStyle budgetTitleStyle;
        private GUIStyle budgetValueStyle;
        private GUIStyle sectionSubtitleStyle;
        private GUIStyle cardStyle;
        private GUIStyle infoLabelStyle;
        private GUIStyle infoValueStyle;
        private GUIStyle summaryCardStyle;
        private GUIStyle summaryHeaderStyle;

        private static readonly GUIContent ReferenceFrameLabel = new GUIContent("Reference Frame", "Avatar centered drops an anchor when the sync starts. World space anchors to origin and supports late join.");
        private static readonly GUIContent MenuLocationLabel = new GUIContent("Menu Location", "Expression menu path where the enable toggle will be created.");
        private static readonly string VrLabsRepoUrl = "https://github.com/VRLabs/Custom-Object-Sync";
        private static readonly string WikiUrl = "https://github.com/Yeusepe/Yeusepes-Modules/wiki/Custom-Object-Sync";

        private void OnEnable()
        {
            data = (CustomObjectSyncData)target;

            quickSyncProp = serializedObject.FindProperty("quickSync");
            referenceFrameProp = serializedObject.FindProperty("referenceFrame");
            maxRadiusProp = serializedObject.FindProperty("maxRadius");
            positionPrecisionProp = serializedObject.FindProperty("positionPrecision");
            rotationPrecisionProp = serializedObject.FindProperty("rotationPrecision");
            bitCountProp = serializedObject.FindProperty("bitCount");
            rotationEnabledProp = serializedObject.FindProperty("rotationEnabled");
            addDampingProp = serializedObject.FindProperty("addDampingConstraint");
            dampingValueProp = serializedObject.FindProperty("dampingConstraintValue");
            addDebugProp = serializedObject.FindProperty("addLocalDebugView");
            writeDefaultsProp = serializedObject.FindProperty("writeDefaults");
            menuLocationProp = serializedObject.FindProperty("menuLocation");
            syncGroupIdProp = serializedObject.FindProperty("syncGroupId");
            enableGroupingProp = serializedObject.FindProperty("enableGrouping");
            showSceneGizmoProp = serializedObject.FindProperty("showSceneGizmo");
            verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
            includeCreditsProp = serializedObject.FindProperty("includeCredits");
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Custom Object Sync"));
            var container = new IMGUIContainer(OnInspectorGUIContent);
            root.Add(container);
            return root;
        }

        public override void OnInspectorGUI()
        {
            OnInspectorGUIContent();
        }

        private void OnInspectorGUIContent()
        {
            serializedObject.Update();

            BetaWarningHelper.DrawBetaWarningIMGUI(typeof(CustomObjectSyncData));
            SupportBannerHelper.DrawSupportBannerIMGUI(typeof(CustomObjectSyncData));

            DrawCreditBanner();
            DrawBuildSummary();
            DrawDescriptorWarnings();

            DrawSummaryCard();

            DrawCard("Sync Strategy", "Decide how this object synchronizes over the network.", () =>
            {
                EditorGUILayout.PropertyField(quickSyncProp, new GUIContent("Quick Sync", quickSyncProp.tooltip));

                using (new EditorGUI.DisabledScope(quickSyncProp.boolValue))
                {
                    EditorGUILayout.PropertyField(referenceFrameProp, ReferenceFrameLabel);
                }

                if (quickSyncProp.boolValue && referenceFrameProp.enumValueIndex != (int)CustomObjectSyncData.ReferenceFrame.AvatarCentered)
                {
                    referenceFrameProp.enumValueIndex = (int)CustomObjectSyncData.ReferenceFrame.AvatarCentered;
                }

                EditorGUILayout.PropertyField(rotationEnabledProp, new GUIContent("Sync Rotation"));
                EditorGUILayout.PropertyField(addDebugProp, new GUIContent("Add Local Debug View"));
            });

            DrawCard("Precision & Range", "Control how far and how precisely motion is captured.", () =>
            {
                DrawRadiusField();
                EditorGUILayout.IntSlider(positionPrecisionProp, 1, 12, new GUIContent("Position Precision"));
                EditorGUILayout.IntSlider(rotationPrecisionProp, 0, 12, new GUIContent("Rotation Precision"));

                using (new EditorGUI.DisabledScope(quickSyncProp.boolValue))
                {
                    EditorGUILayout.IntSlider(bitCountProp, 4, 32, new GUIContent("Bits Per Step"));
                }

                if (quickSyncProp.boolValue)
                {
                    EditorGUILayout.HelpBox("Bit count is disabled while Quick Sync is enabled because floats are sent directly.", MessageType.Info);
                }
            });

            DrawCard("Motion Options", "Fine-tune smoothing and animator integration.", () =>
            {
                EditorGUILayout.PropertyField(addDampingProp, new GUIContent("Add Damping Constraint"));
                using (new EditorGUI.DisabledScope(!addDampingProp.boolValue))
                {
                    EditorGUILayout.Slider(dampingValueProp, 0.01f, 1f, new GUIContent("Damping Strength"));
                }

                EditorGUILayout.PropertyField(writeDefaultsProp, new GUIContent("Write Defaults"));
                EditorGUILayout.PropertyField(menuLocationProp, MenuLocationLabel);
            });

            DrawCard("Diagnostics & Debug", "Surface build output and logging helpers.", () =>
            {
                EditorGUILayout.PropertyField(verboseLoggingProp, new GUIContent("Verbose Logging"));
                EditorGUILayout.PropertyField(includeCreditsProp, new GUIContent("Include Credits Banner"));
            });

            DrawCard("Grouping & Collaboration", "Keep multiple components in sync automatically.", () =>
            {
                EditorGUILayout.PropertyField(enableGroupingProp, new GUIContent("Enable Grouping", enableGroupingProp.tooltip));
                using (new EditorGUI.DisabledScope(!enableGroupingProp.boolValue))
                {
                    EditorGUILayout.PropertyField(syncGroupIdProp, new GUIContent("Group ID", syncGroupIdProp.tooltip));
                }
                var groupingInfo = enableGroupingProp.boolValue
                    ? "Components with the same Group ID share one Custom Object Sync rig to reduce parameters."
                    : "Grouping disabled: this component will get its own rig (same behavior as the original VRLabs wizard).";
                EditorGUILayout.HelpBox(groupingInfo, MessageType.Info);
            });

            DrawCard("Scene Visualization", "Toggle an in-scene gizmo that mirrors your settings for quick spatial feedback.", () =>
            {
                EditorGUILayout.PropertyField(showSceneGizmoProp, new GUIContent("Show Scene Gizmo", showSceneGizmoProp.tooltip));
                EditorGUILayout.HelpBox("When enabled, selecting this object in the Scene view shows discs for travel radius plus labels for precision and rotation. Use it to size ranges without guessing.", MessageType.Info);
            });

            DrawHelpLinks();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRadiusField()
        {
            EnsureStyles();
            int rawValue = Mathf.Clamp(maxRadiusProp.intValue, 1, 12);
            double rangeMeters = Math.Pow(2, rawValue);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.IntSlider(maxRadiusProp, 1, 12, new GUIContent($"Max Radius (2^{rawValue} m)"));
                GUILayout.Label($"{rangeMeters:0.#} m", GUILayout.Width(70));
            }
            EditorGUILayout.LabelField("Choose how far the object can move from its anchor point. Example: value 8 allows roughly 256m of travel. Higher values consume more bits.", miniWrapStyle);
        }

        private void DrawParameterBudget(ParameterSummary summary)
        {
            EnsureStyles();

            EditorGUILayout.BeginVertical(budgetCardStyle);
            EditorGUILayout.LabelField("Expression Parameters", budgetTitleStyle);
            EditorGUILayout.LabelField(summary.Total.ToString(), budgetValueStyle);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"Group Size: {summary.GroupSize}", miniWrapStyle);
            if (!string.IsNullOrEmpty(summary.Breakdown))
            {
                EditorGUILayout.LabelField(summary.Breakdown, miniWrapStyle);
            }
            if (!string.IsNullOrEmpty(summary.Extra))
            {
                EditorGUILayout.LabelField(summary.Extra, miniWrapStyle);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawDescriptorWarnings()
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("This component must be placed under a VRCAvatarDescriptor in order for the builder to configure sync data.", MessageType.Error);
                return;
            }

            if (data.transform == descriptor.transform)
            {
                EditorGUILayout.HelpBox("Attach Custom Object Sync to the object you want to sync, not the descriptor root.", MessageType.Warning);
            }
            else if (!data.transform.IsChildOf(descriptor.transform))
            {
                EditorGUILayout.HelpBox("Custom Object Sync target must be within the avatar hierarchy. Please move it inside the descriptor object.", MessageType.Error);
            }
        }

        private void DrawCreditBanner()
        {
            if (!includeCreditsProp.boolValue) return;

            EditorGUILayout.HelpBox("Powered by VRLabs Custom Object Sync (MIT). Please credit VRLabs when shipping your avatar.", MessageType.Info);
            if (GUILayout.Button("Open VRLabs Custom Object Sync Repository"))
            {
                Application.OpenURL(VrLabsRepoUrl);
            }
        }

        private void DrawBuildSummary()
        {
            var summary = data.GetBuildSummary();
            if (string.IsNullOrEmpty(summary)) return;

            var timestamp = data.GetLastBuildTimeUtc();
            string label = summary;
            if (timestamp.HasValue)
            {
                label += $" • {timestamp.Value.ToLocalTime():g}";
            }
            EditorGUILayout.HelpBox($"Last build: {label}", MessageType.None);
        }

        private void DrawHelpLinks()
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Documentation"))
                {
                    Application.OpenURL(WikiUrl);
                }

                if (GUILayout.Button("Join VRLabs Discord"))
                {
                    Application.OpenURL("https://discord.vrlabs.dev/");
                }
            }
        }

        private ParameterSummary CalculateParameterSummary()
        {
            const int axisCount = 3;
            int objectCount = Mathf.Max(1, GetGroupObjectCount());

            bool quickSync = quickSyncProp.boolValue;
            bool rotationEnabled = rotationEnabledProp.boolValue;

            int maxRadius = Mathf.Clamp(maxRadiusProp.intValue, 1, 12);
            int positionPrecision = Mathf.Clamp(positionPrecisionProp.intValue, 1, 12);
            int rotationPrecision = Mathf.Clamp(rotationPrecisionProp.intValue, 0, 12);
            int bitCount = Mathf.Clamp(bitCountProp.intValue, 1, 32);

            int objectParameterCount = objectCount > 1 ? Mathf.CeilToInt(Mathf.Log(objectCount, 2f)) : 0;
            int rotationBits = rotationEnabled ? rotationPrecision * axisCount : 0;
            int positionBits = axisCount * (maxRadius + positionPrecision);
            int totalBits = rotationBits + positionBits;

            if (quickSync)
            {
                int totalParameters = objectParameterCount + totalBits + 1;
                float syncInterval = objectCount * 0.2f;
                string breakdown = BuildBreakdown(totalBits, objectParameterCount, includeStepBits: false, 0);
                string extra = $"Sync interval ≈ {syncInterval:0.###}s";
                return new ParameterSummary(totalParameters, breakdown, extra, objectCount);
            }

            int syncSteps = Mathf.Max(1, Mathf.CeilToInt(totalBits / Mathf.Max(1f, bitCount)));
            int stepParameterCount = Mathf.CeilToInt(Mathf.Log(syncSteps + 1, 2f));
            int totalExpressionParameters = objectParameterCount + stepParameterCount + bitCount + 1;

            float conversionTime = Mathf.Max(rotationPrecision, maxRadius + positionPrecision) * 1.5f / 60f;
            float syncTime = objectCount * syncSteps * 0.2f;
            float syncDelay = syncTime + (2f * conversionTime);

            string stepBreakdown = BuildBreakdown(bitCount, objectParameterCount, includeStepBits: true, stepParameterCount);
            string extraInfo = $"Sync steps: {syncSteps}, Interval ≈ {syncTime:0.###}s, Delay ≈ {syncDelay:0.###}s";

            return new ParameterSummary(totalExpressionParameters, stepBreakdown, extraInfo, objectCount);
        }

        private int GetGroupObjectCount()
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return 1;
            }

            var members = descriptor.GetComponentsInChildren<CustomObjectSyncData>(true);
            if (members == null || members.Length == 0)
            {
                return 1;
            }

            string targetGroup = CustomObjectSyncData.NormalizeGroupId(syncGroupIdProp.stringValue);
            int count = 0;
            foreach (var member in members)
            {
                if (member == null) continue;
                if (CustomObjectSyncData.NormalizeGroupId(member.syncGroupId) == targetGroup)
                {
                    count++;
                }
            }

            return Mathf.Max(1, count);
        }

        private void EnsureStyles()
        {
            if (sectionTitleStyle == null)
            {
                sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (sectionSubtitleStyle == null)
            {
                sectionSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };
            }

            if (statsValueStyle == null)
            {
                statsValueStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontSize = Math.Max(14, EditorStyles.boldLabel.fontSize + 2)
                };
            }

            if (miniWrapStyle == null)
            {
                miniWrapStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                {
                    richText = false
                };
            }

            if (budgetCardStyle == null)
            {
                budgetCardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 10)
                };
            }

            if (budgetTitleStyle == null)
            {
                budgetTitleStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (budgetValueStyle == null)
            {
                budgetValueStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (cardStyle == null)
            {
                cardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 10)
                };
            }

            if (infoLabelStyle == null)
            {
                infoLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontStyle = FontStyle.Bold
                };
            }

            if (infoValueStyle == null)
            {
                infoValueStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true
                };
            }

            if (summaryCardStyle == null)
            {
                summaryCardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(14, 14, 12, 12)
                };
            }

            if (summaryHeaderStyle == null)
            {
                summaryHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13
                };
            }
        }

        private static string BuildBreakdown(int dataBits, int objectBits, bool includeStepBits, int secondaryBitCount)
        {
            string breakdown = $"Enable toggle + {dataBits} data bits";

            if (includeStepBits)
            {
                breakdown += $" + {secondaryBitCount} step bits";
            }

            if (objectBits > 0)
            {
                breakdown += $" + {objectBits} object bits";
            }

            return breakdown;
        }

        private readonly struct ParameterSummary
        {
            public ParameterSummary(int total, string breakdown, string extra, int groupSize)
            {
                Total = total;
                Breakdown = breakdown;
                Extra = extra;
                GroupSize = groupSize;
            }

            public int Total { get; }
            public string Breakdown { get; }
            public string Extra { get; }
            public int GroupSize { get; }
        }

        private void DrawCard(string title, string subtitle, Action body)
        {
            EnsureStyles();
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField(title, sectionTitleStyle);
            if (!string.IsNullOrEmpty(subtitle))
            {
                EditorGUILayout.LabelField(subtitle, sectionSubtitleStyle);
            }
            EditorGUILayout.Space(2);
            EditorGUI.indentLevel++;
            body?.Invoke();
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private void DrawSummaryCard()
        {
            EnsureStyles();
            var summary = CalculateParameterSummary();
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            string targetPath = descriptor != null ? AnimationUtility.CalculateTransformPath(data.transform, descriptor.transform) : data.gameObject.name;
            string modeLabel = quickSyncProp.boolValue ? "Quick Sync (fast, higher cost)" : "Bit Packed (slower, parameter efficient)";

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(summaryCardStyle);
            EditorGUILayout.LabelField("Custom Object Sync Overview", summaryHeaderStyle);
            EditorGUILayout.Space(2);
            DrawInfoRow("Target", targetPath);
            var groupingLabel = enableGroupingProp.boolValue
                ? CustomObjectSyncData.NormalizeGroupId(syncGroupIdProp.stringValue)
                : "Isolated (per-object)";
            DrawInfoRow("Group", groupingLabel);
            DrawInfoRow("Mode", modeLabel);
            EditorGUILayout.Space(4);
            DrawParameterBudget(summary);
            EditorGUILayout.EndVertical();
        }

        private void DrawInfoRow(string label, string value)
        {
            EnsureStyles();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, infoLabelStyle, GUILayout.Width(70));
                EditorGUILayout.LabelField(value, infoValueStyle);
            }
        }
    }
}

