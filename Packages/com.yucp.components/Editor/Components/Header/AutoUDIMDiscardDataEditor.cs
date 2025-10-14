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
        public override void OnInspectorGUI()
        {
            // Display beta warning at the top
            BetaWarningHelper.DrawBetaWarningIMGUI(typeof(AutoUDIMDiscardData));
            
            serializedObject.Update();

            AutoUDIMDiscardData data = (AutoUDIMDiscardData)target;

            DrawPropertiesWithConditionalVisibility();

            // Preview button
            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Preview", GUILayout.Height(30)))
            {
                GeneratePreview(data);
            }

            if (data.previewGenerated && data.previewRegions != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox($"Detected {data.previewRegions.Count} UV regions", MessageType.Info);
                
                for (int i = 0; i < data.previewRegions.Count; i++)
                {
                    var region = data.previewRegions[i];
                    EditorGUILayout.LabelField($"Region {i + 1}: {region.vertexIndices.Count} vertices → UDIM ({region.assignedRow}, {region.assignedColumn})");
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPropertiesWithConditionalVisibility()
        {
            SerializedProperty targetBodyMesh = serializedObject.FindProperty("targetBodyMesh");
            SerializedProperty uvChannel = serializedObject.FindProperty("uvChannel");
            SerializedProperty mergeTolerance = serializedObject.FindProperty("mergeTolerance");
            SerializedProperty minRegionSize = serializedObject.FindProperty("minRegionSize");
            SerializedProperty startRow = serializedObject.FindProperty("startRow");
            SerializedProperty startColumn = serializedObject.FindProperty("startColumn");
            SerializedProperty createToggles = serializedObject.FindProperty("createToggles");
            SerializedProperty toggleMenuPath = serializedObject.FindProperty("toggleMenuPath");
            SerializedProperty toggleSaved = serializedObject.FindProperty("toggleSaved");
            SerializedProperty useMasterToggle = serializedObject.FindProperty("useMasterToggle");
            SerializedProperty masterTogglePath = serializedObject.FindProperty("masterTogglePath");
            SerializedProperty useParameterDriver = serializedObject.FindProperty("useParameterDriver");
            SerializedProperty parameterBaseName = serializedObject.FindProperty("parameterBaseName");
            SerializedProperty showPreview = serializedObject.FindProperty("showPreview");
            SerializedProperty useColorCoding = serializedObject.FindProperty("useColorCoding");
            SerializedProperty detectedRegions = serializedObject.FindProperty("detectedRegions");
            SerializedProperty usedTiles = serializedObject.FindProperty("usedTiles");

            EditorGUILayout.PropertyField(targetBodyMesh);
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Detection Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(uvChannel);
            EditorGUILayout.PropertyField(mergeTolerance);
            EditorGUILayout.PropertyField(minRegionSize);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("UDIM Tile Assignment", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(startRow);
            EditorGUILayout.PropertyField(startColumn);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Toggle Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(createToggles);

            if (createToggles.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(toggleMenuPath);
                EditorGUILayout.PropertyField(toggleSaved);
                EditorGUILayout.PropertyField(useMasterToggle);

                if (useMasterToggle.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(masterTogglePath);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.PropertyField(useParameterDriver);

                if (useParameterDriver.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(parameterBaseName);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.HelpBox("Parameter drivers create synced parameters that can be used by other systems.", MessageType.Info);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced Options", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(showPreview);
            EditorGUILayout.PropertyField(useColorCoding);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build Statistics", EditorStyles.boldLabel);
            GUI.enabled = false;
            EditorGUILayout.PropertyField(detectedRegions);
            
            if (usedTiles.arraySize > 0)
            {
                EditorGUILayout.LabelField("Used Tiles:");
                EditorGUI.indentLevel++;
                for (int i = 0; i < usedTiles.arraySize; i++)
                {
                    EditorGUILayout.LabelField(usedTiles.GetArrayElementAtIndex(i).stringValue);
                }
                EditorGUI.indentLevel--;
            }
            GUI.enabled = true;
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

