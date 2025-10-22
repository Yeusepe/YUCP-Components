using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YUCP.Components;
using YUCP.Components.Editor;

namespace YUCP.Components.Editor.UI
{
    [CustomEditor(typeof(AutoUDIMDiscardData))]
    public class AutoUDIMDiscardDataEditor : UnityEditor.Editor
    {
        private bool showAdvancedOptions = false;
        private bool showBuildStats = false;

        public override void OnInspectorGUI()
        {
            // Display beta warning at the top
            BetaWarningHelper.DrawBetaWarningIMGUI(typeof(AutoUDIMDiscardData));
            
            serializedObject.Update();
            AutoUDIMDiscardData data = (AutoUDIMDiscardData)target;

            // Target Mesh
            DrawSection("Target Mesh", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetBodyMesh"), new GUIContent("Body Mesh"));
            });

            // Detection Settings
            DrawSection("Detection Settings", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("uvChannel"), new GUIContent("UV Channel"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeTolerance"), new GUIContent("Merge Tolerance"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("minRegionSize"), new GUIContent("Min Region Size %"));
            });

            // UDIM Tile Assignment
            DrawSection("UDIM Tile Assignment", () => {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("startRow"), new GUIContent("Start Row"), GUILayout.MinWidth(100));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("startColumn"), new GUIContent("Start Column"), GUILayout.MinWidth(100));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.HelpBox("Each detected region will be assigned to consecutive tiles starting from this position.", MessageType.None);
            });

            // Toggle Settings
            EditorGUILayout.Space(5);
            var createTogglesProp = serializedObject.FindProperty("createToggles");
            EditorGUILayout.PropertyField(createTogglesProp, new GUIContent("Create Toggles"));

            if (createTogglesProp.boolValue)
            {
                DrawSection("Toggle Configuration", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleMenuPath"), new GUIContent("Menu Path Prefix"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleSaved"), new GUIContent("Saved"));
                    
                    EditorGUILayout.Space(3);
                    var useMasterToggleProp = serializedObject.FindProperty("useMasterToggle");
                    EditorGUILayout.PropertyField(useMasterToggleProp, new GUIContent("Use Master Toggle"));
                    
                    if (useMasterToggleProp.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("masterTogglePath"), new GUIContent("Master Toggle Path"));
                        EditorGUI.indentLevel--;
                    }
                    
                    EditorGUILayout.Space(3);
                    var useParameterDriverProp = serializedObject.FindProperty("useParameterDriver");
                    EditorGUILayout.PropertyField(useParameterDriverProp, new GUIContent("Use Parameter Driver"));
                    
                    if (useParameterDriverProp.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("parameterBaseName"), new GUIContent("Parameter Base Name"));
                        EditorGUILayout.HelpBox("Parameter drivers create synced parameters.", MessageType.Info);
                        EditorGUI.indentLevel--;
                    }
                });
            }

            // Advanced Options (Foldout)
            EditorGUILayout.Space(5);
            showAdvancedOptions = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvancedOptions, "Advanced Options");
            if (showAdvancedOptions)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showPreview"), new GUIContent("Show Preview"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("useColorCoding"), new GUIContent("Use Color Coding"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Preview Button
            EditorGUILayout.Space(10);
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Generate Preview", GUILayout.Height(35)))
            {
                GeneratePreview(data);
            }
            GUI.backgroundColor = Color.white;

            // Preview Results
            if (data.previewGenerated && data.previewRegions != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox($"Detected {data.previewRegions.Count} UV regions", MessageType.Info);
                
                for (int i = 0; i < data.previewRegions.Count && i < 10; i++)
                {
                    var region = data.previewRegions[i];
                    EditorGUILayout.LabelField($"Region {i + 1}: {region.vertexIndices.Count} vertices → UDIM ({region.assignedRow}, {region.assignedColumn})");
                }
                
                if (data.previewRegions.Count > 10)
                {
                    EditorGUILayout.LabelField($"... and {data.previewRegions.Count - 10} more regions");
                }
            }

            // Build Statistics (Foldout)
            EditorGUILayout.Space(5);
            var detectedRegionsProp = serializedObject.FindProperty("detectedRegions");
            if (detectedRegionsProp.intValue > 0)
            {
                showBuildStats = EditorGUILayout.BeginFoldoutHeaderGroup(showBuildStats, "Build Statistics");
                if (showBuildStats)
                {
                    EditorGUI.indentLevel++;
                    GUI.enabled = false;
                    EditorGUILayout.PropertyField(detectedRegionsProp, new GUIContent("Detected Regions"));
                    
                    var usedTiles = serializedObject.FindProperty("usedTiles");
                    if (usedTiles.arraySize > 0)
                    {
                        EditorGUILayout.LabelField("Used Tiles:");
                        for (int i = 0; i < usedTiles.arraySize; i++)
                        {
                            EditorGUILayout.LabelField($"  • {usedTiles.GetArrayElementAtIndex(i).stringValue}");
                        }
                    }
                    GUI.enabled = true;
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSection(string title, System.Action content)
        {
            EditorGUILayout.Space(5);
            
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.1f);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;
            
            if (!string.IsNullOrEmpty(title))
            {
                var style = new GUIStyle(EditorStyles.boldLabel);
                style.alignment = TextAnchor.MiddleLeft;
                EditorGUILayout.LabelField(title, style);
                EditorGUILayout.Space(3);
            }
            
            content?.Invoke();
            
            EditorGUILayout.EndVertical();
        }

        private void GeneratePreview(AutoUDIMDiscardData data)
        {
            var clothingRenderer = data.GetComponent<SkinnedMeshRenderer>();
            if (clothingRenderer == null || clothingRenderer.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Error", "No SkinnedMeshRenderer or mesh found on this object!", "OK");
                return;
            }

            // Detect regions (simplified preview version)
            Vector2[] uvs = GetUVChannel(clothingRenderer.sharedMesh, data.uvChannel);
            if (uvs == null || uvs.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", $"No UV{data.uvChannel} data found on mesh!", "OK");
                return;
            }

            List<List<int>> clusters = ClusterVerticesByUV(uvs, data.mergeTolerance);

            int minVertices = Mathf.CeilToInt(clothingRenderer.sharedMesh.vertexCount * (data.minRegionSize / 100f));
            clusters = clusters.Where(c => c.Count >= minVertices).ToList();

            // Create preview regions
            data.previewRegions = new List<AutoUDIMDiscardData.UVRegion>();
            Color[] debugColors = new Color[]
            {
                Color.red, Color.green, Color.blue, Color.yellow,
                Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f)
            };

            for (int i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                var region = new AutoUDIMDiscardData.UVRegion
                {
                    vertexIndices = cluster,
                    debugColor = debugColors[i % debugColors.Length]
                };

                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);

                foreach (int vertexIdx in cluster)
                {
                    Vector2 uv = uvs[vertexIdx];
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }

                region.uvBounds = new Bounds(
                    new Vector3((min.x + max.x) / 2f, (min.y + max.y) / 2f, 0),
                    new Vector3(max.x - min.x, max.y - min.y, 0)
                );
                region.uvCenter = new Vector2((min.x + max.x) / 2f, (min.y + max.y) / 2f);

                data.previewRegions.Add(region);
            }

            // Sort regions
            data.previewRegions = data.previewRegions.OrderByDescending(r => r.uvCenter.y)
                                                     .ThenBy(r => r.uvCenter.x)
                                                     .ToList();

            // Assign UDIM tiles
            int currentRow = data.startRow;
            int currentColumn = data.startColumn;

            foreach (var region in data.previewRegions)
            {
                region.assignedRow = currentRow;
                region.assignedColumn = currentColumn;
                region.name = $"Region_{currentRow}_{currentColumn}";

                currentColumn++;
                if (currentColumn > 3)
                {
                    currentColumn = 0;
                    currentRow++;
                }
            }

            data.previewGenerated = true;
            EditorUtility.SetDirty(data);
            
            Debug.Log($"[AutoUDIMDiscard] Preview generated: {data.previewRegions.Count} regions detected");
        }

        private Vector2[] GetUVChannel(Mesh mesh, int channel)
        {
            List<Vector2> uvList = new List<Vector2>();

            switch (channel)
            {
                case 0: mesh.GetUVs(0, uvList); break;
                case 1: mesh.GetUVs(1, uvList); break;
                case 2: mesh.GetUVs(2, uvList); break;
                case 3: mesh.GetUVs(3, uvList); break;
                default: return null;
            }

            return uvList.ToArray();
        }

        private List<List<int>> ClusterVerticesByUV(Vector2[] uvs, float tolerance)
        {
            List<List<int>> clusters = new List<List<int>>();
            bool[] assigned = new bool[uvs.Length];

            for (int i = 0; i < uvs.Length; i++)
            {
                if (assigned[i]) continue;

                List<int> cluster = new List<int>();
                Queue<int> toProcess = new Queue<int>();
                toProcess.Enqueue(i);
                assigned[i] = true;

                while (toProcess.Count > 0)
                {
                    int current = toProcess.Dequeue();
                    cluster.Add(current);

                    for (int j = 0; j < uvs.Length; j++)
                    {
                        if (assigned[j]) continue;

                        float distance = Vector2.Distance(uvs[current], uvs[j]);
                        if (distance <= tolerance)
                        {
                            assigned[j] = true;
                            toProcess.Enqueue(j);
                        }
                    }
                }

                if (cluster.Count > 0)
                    clusters.Add(cluster);
            }

            return clusters;
        }
    }
}

