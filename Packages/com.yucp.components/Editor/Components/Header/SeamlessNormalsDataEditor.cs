using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using System.Collections.Generic;
using System.Linq;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Custom editor for Seamless Normals providing preview visualization and real-time parameter adjustment.
    /// </summary>
    [CustomEditor(typeof(SeamlessNormalsData))]
    public class SeamlessNormalsDataEditor : UnityEditor.Editor
    {
        private SeamlessNormalsData data;
        private bool isGeneratingPreview = false;

        private void OnEnable()
        {
            data = (SeamlessNormalsData)target;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Seamless Normals"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(SeamlessNormalsData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(SeamlessNormalsData));
            if (supportBanner != null) root.Add(supportBanner);

            // Source Meshes Card
            var sourceCard = YUCPUIToolkitHelper.CreateCard("Source Meshes", "Meshes to transfer normals from");
            var sourceContent = YUCPUIToolkitHelper.GetCardContent(sourceCard);
            sourceContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("sourceMeshes"), "Source Meshes"));
            root.Add(sourceCard);

            // Target Meshes Card
            var targetCard = YUCPUIToolkitHelper.CreateCard("Target Meshes", "Meshes that will receive transferred normals");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("targetMeshes"), "Target Meshes"));
            root.Add(targetCard);

            // Transfer Method Card
            var methodCard = YUCPUIToolkitHelper.CreateCard("Transfer Method", "Choose how normals are transferred");
            var methodContent = YUCPUIToolkitHelper.GetCardContent(methodCard);
            methodContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("transferMethod"), "Method"));
            root.Add(methodCard);

            // Method-specific settings
            var proximityCard = YUCPUIToolkitHelper.CreateCard("Proximity Settings", "Settings for proximity-based transfer");
            proximityCard.name = "proximity-settings-card";
            var proximityContent = YUCPUIToolkitHelper.GetCardContent(proximityCard);
            proximityContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("proximityThreshold"), "Proximity Threshold"));
            proximityContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("proximityBlendStrength"), "Blend Strength"));
            root.Add(proximityCard);

            var projectionCard = YUCPUIToolkitHelper.CreateCard("Projection Settings", "Settings for projection-based transfer");
            projectionCard.name = "projection-settings-card";
            var projectionContent = YUCPUIToolkitHelper.GetCardContent(projectionCard);
            projectionContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("projectionDistance"), "Projection Distance"));
            projectionContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("projectionDirection"), "Projection Direction"));
            projectionContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("projectionBlendStrength"), "Blend Strength"));
            root.Add(projectionCard);

            var sharedFieldCard = YUCPUIToolkitHelper.CreateCard("Shared Field Settings", "Settings for shared normal field computation");
            sharedFieldCard.name = "shared-field-settings-card";
            var sharedFieldContent = YUCPUIToolkitHelper.GetCardContent(sharedFieldCard);
            sharedFieldContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("sharedFieldPositionThreshold"), "Position Threshold"));
            sharedFieldContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("sharedFieldHardEdgeAngle"), "Hard Edge Angle"));
            root.Add(sharedFieldCard);

            // Performance Card
            var performanceCard = YUCPUIToolkitHelper.CreateCard("Performance", "GPU acceleration settings");
            var performanceContent = YUCPUIToolkitHelper.GetCardContent(performanceCard);
            performanceContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useGPUAcceleration"), "Use GPU Acceleration"));
            root.Add(performanceCard);

            // Advanced Options Card
            var advancedCard = YUCPUIToolkitHelper.CreateCard("Advanced Options", "Additional transfer settings");
            var advancedContent = YUCPUIToolkitHelper.GetCardContent(advancedCard);
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("maxTransferDistance"), "Max Transfer Distance"));
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("respectHardEdges"), "Respect Hard Edges"));
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("hardEdgeAngle"), "Hard Edge Angle"));
            root.Add(advancedCard);

            // Debug & Preview Card
            var debugCard = YUCPUIToolkitHelper.CreateCard("Debug & Preview", "Preview and debugging options");
            var debugContent = YUCPUIToolkitHelper.GetCardContent(debugCard);
            debugContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugMode"), "Debug Mode"));
            debugContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("showPreview"), "Show Preview"));
            
            // Process Now button (for editor testing)
            var processButton = new Button(() => ProcessNormals())
            {
                text = "Process Normals Now",
                style = { marginTop = 10, height = 30 }
            };
            debugContent.Add(processButton);
            
            // Preview button
            var previewButton = new Button(() => GeneratePreview())
            {
                text = "Generate Preview",
                style = { marginTop = 10 }
            };
            debugContent.Add(previewButton);

            // Build stats (read-only)
            var statsLabel = new Label($"Processed: {data.ProcessedVertexCount} vertices | Method: {data.AppliedMethod}")
            {
                style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Italic }
            };
            debugContent.Add(statsLabel);
            
            root.Add(debugCard);

            // Update UI based on method selection
            var methodField = root.Q<PropertyField>(null, "unity-property-field");
            if (methodField != null)
            {
                methodField.RegisterValueChangeCallback(evt => UpdateMethodVisibility(root));
            }

            UpdateMethodVisibility(root);

            return root;
        }

        private void UpdateMethodVisibility(VisualElement root)
        {
            // Show/hide method-specific cards based on selected method
            var proximityCard = root.Q("proximity-settings-card");
            var projectionCard = root.Q("projection-settings-card");
            var sharedFieldCard = root.Q("shared-field-settings-card");

            if (proximityCard != null)
                proximityCard.style.display = data.transferMethod == NormalTransferMethod.Proximity ? DisplayStyle.Flex : DisplayStyle.None;
            
            if (projectionCard != null)
                projectionCard.style.display = data.transferMethod == NormalTransferMethod.Projection ? DisplayStyle.Flex : DisplayStyle.None;
            
            if (sharedFieldCard != null)
                sharedFieldCard.style.display = data.transferMethod == NormalTransferMethod.SharedField ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ProcessNormals()
        {
            if (data == null)
            {
                Debug.LogError("[SeamlessNormalsDataEditor] Component data is null.");
                return;
            }

            Renderer[] sourceRenderers = data.GetSourceMeshes();
            Renderer[] targetRenderers = data.GetTargetMeshes();

            if (sourceRenderers.Length == 0)
            {
                Debug.LogError("[SeamlessNormalsDataEditor] No valid source meshes found.");
                return;
            }

            if (targetRenderers.Length == 0)
            {
                Debug.LogError("[SeamlessNormalsDataEditor] No valid target meshes found.");
                return;
            }

            // Check vertex alignment for seamless blending
            if (data.transferMethod == NormalTransferMethod.Proximity)
            {
                CheckVertexAlignment(sourceRenderers, targetRenderers, data.proximityThreshold);
            }

            try
            {
                NormalBakeSettings settings = NormalBakeSettings.FromData(data);
                
                // Store original normals for comparison
                var originalNormals = new Dictionary<Mesh, Vector3[]>();
                foreach (var renderer in targetRenderers)
                {
                    Mesh mesh = GetMeshFromRenderer(renderer);
                    if (mesh != null && mesh.normals != null)
                    {
                        originalNormals[mesh] = (Vector3[])mesh.normals.Clone();
                    }
                }
                
                NormalTransferCPU.TransferNormals(sourceRenderers, targetRenderers, settings, true); // Always debug for editor
                
                // Verify normals changed
                bool normalsChanged = false;
                foreach (var renderer in targetRenderers)
                {
                    Mesh mesh = GetMeshFromRenderer(renderer);
                    if (mesh != null && mesh.normals != null && originalNormals.ContainsKey(mesh))
                    {
                        Vector3[] oldNormals = originalNormals[mesh];
                        Vector3[] newNormals = mesh.normals;
                        
                        if (oldNormals.Length == newNormals.Length)
                        {
                            for (int i = 0; i < newNormals.Length; i++)
                            {
                                if (Vector3.Distance(oldNormals[i], newNormals[i]) > 0.001f)
                                {
                                    normalsChanged = true;
                                    break;
                                }
                            }
                        }
                        
                        if (normalsChanged)
                        {
                            Debug.Log($"[SeamlessNormalsDataEditor] ✓ Normals changed on mesh '{mesh.name}'");
                            // Force mesh to upload new data
                            mesh.UploadMeshData(false);
                        }
                    }
                }
                
                // Update stats
                int totalProcessed = 0;
                foreach (var renderer in targetRenderers)
                {
                    Mesh mesh = GetMeshFromRenderer(renderer);
                    if (mesh != null)
                    {
                        totalProcessed += mesh.vertices.Length;
                    }
                }
                data.SetBuildStats(totalProcessed, settings.method.ToString());
                
                if (normalsChanged)
                {
                    Debug.Log($"[SeamlessNormalsDataEditor] ✓ Processed {totalProcessed} vertices using {settings.method} method.");
                    Debug.Log($"[SeamlessNormalsDataEditor] Mesh normals have been updated. Check the Scene view to see the changes.");
                }
                else
                {
                    Debug.LogWarning($"[SeamlessNormalsDataEditor] ⚠ Normals did not change. Check that:");
                    Debug.LogWarning($"  - Source and target meshes are within {settings.proximityThreshold}m of each other");
                    Debug.LogWarning($"  - Meshes are properly assigned");
                    Debug.LogWarning($"  - Try increasing the proximity threshold");
                }
                
                // Force repaint and refresh
                SceneView.RepaintAll();
                InternalEditorUtility.RepaintAllViews();
                EditorUtility.SetDirty(data);
                
                // Mark renderers as dirty too
                foreach (var renderer in targetRenderers)
                {
                    EditorUtility.SetDirty(renderer);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SeamlessNormalsDataEditor] Error processing normals: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void CheckVertexAlignment(Renderer[] sourceRenderers, Renderer[] targetRenderers, float threshold)
        {
            int matchingVertices = 0;
            int totalSourceVertices = 0;
            int totalTargetVertices = 0;

            foreach (var sourceRenderer in sourceRenderers)
            {
                Mesh sourceMesh = GetMeshFromRenderer(sourceRenderer);
                if (sourceMesh == null) continue;

                Vector3[] sourceVerts = sourceMesh.vertices;
                Transform sourceTransform = sourceRenderer.transform;
                totalSourceVertices += sourceVerts.Length;

                foreach (var targetRenderer in targetRenderers)
                {
                    Mesh targetMesh = GetMeshFromRenderer(targetRenderer);
                    if (targetMesh == null) continue;

                    Vector3[] targetVerts = targetMesh.vertices;
                    Transform targetTransform = targetRenderer.transform;
                    totalTargetVertices += targetVerts.Length;

                    float thresholdSq = threshold * threshold;

                    foreach (var sourceVert in sourceVerts)
                    {
                        Vector3 sourceWorldPos = sourceTransform.TransformPoint(sourceVert);
                        foreach (var targetVert in targetVerts)
                        {
                            Vector3 targetWorldPos = targetTransform.TransformPoint(targetVert);
                            if ((sourceWorldPos - targetWorldPos).sqrMagnitude <= thresholdSq)
                            {
                                matchingVertices++;
                                break; // Count each source vertex only once
                            }
                        }
                    }
                }
            }

            float matchPercentage = totalSourceVertices > 0 ? (matchingVertices / (float)totalSourceVertices) * 100f : 0f;
            
            if (matchPercentage < 10f)
            {
                Debug.LogWarning($"[SeamlessNormalsDataEditor] ⚠ Only {matchPercentage:F1}% of vertices are within threshold ({threshold}m). " +
                    $"For seamless blending, vertices at mesh edges should be at the EXACT same position. " +
                    $"Consider using a larger threshold or ensuring meshes share vertices at edges.");
            }
            else
            {
                Debug.Log($"[SeamlessNormalsDataEditor] ✓ Found {matchingVertices} matching vertices ({matchPercentage:F1}% of source vertices within {threshold}m threshold).");
            }
        }

        private void GeneratePreview()
        {
            if (isGeneratingPreview)
            {
                Debug.LogWarning("[SeamlessNormalsDataEditor] Preview generation already in progress.");
                return;
            }

            isGeneratingPreview = true;

            try
            {
                Renderer[] sourceRenderers = data.GetSourceMeshes();
                Renderer[] targetRenderers = data.GetTargetMeshes();

                if (sourceRenderers.Length == 0 || targetRenderers.Length == 0)
                {
                    Debug.LogWarning("[SeamlessNormalsDataEditor] Cannot generate preview: missing source or target meshes.");
                    return;
                }

                // Collect source mesh data for preview
                var sourceVerticesList = new List<Vector3>();
                var sourceNormalsList = new List<Vector3>();
                
                foreach (var renderer in sourceRenderers)
                {
                    Mesh mesh = GetMeshFromRenderer(renderer);
                    if (mesh == null) continue;

                    Vector3[] vertices = mesh.vertices;
                    Vector3[] normals = mesh.normals ?? new Vector3[vertices.Length];
                    Transform transform = renderer.transform;

                    for (int i = 0; i < vertices.Length; i++)
                    {
                        sourceVerticesList.Add(transform.TransformPoint(vertices[i]));
                        sourceNormalsList.Add(transform.TransformDirection(normals[i]).normalized);
                    }
                }

                // Collect target mesh data for preview
                var targetVerticesList = new List<Vector3>();
                var targetNormalsList = new List<Vector3>();
                var targetOriginalNormalsList = new List<Vector3>();

                foreach (var renderer in targetRenderers)
                {
                    Mesh mesh = GetMeshFromRenderer(renderer);
                    if (mesh == null) continue;

                    Vector3[] vertices = mesh.vertices;
                    Vector3[] normals = mesh.normals ?? new Vector3[vertices.Length];
                    Transform transform = renderer.transform;

                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 worldPos = transform.TransformPoint(vertices[i]);
                        Vector3 worldNormal = transform.TransformDirection(normals[i]).normalized;
                        
                        targetVerticesList.Add(worldPos);
                        targetOriginalNormalsList.Add(worldNormal);
                        targetNormalsList.Add(worldNormal); // Will be updated by transfer
                    }
                }

                // Perform preview transfer (simplified - just proximity for preview)
                NormalBakeSettings settings = NormalBakeSettings.FromData(data);
                
                // For preview, we'll do a simple proximity transfer
                float thresholdSq = settings.proximityThreshold * settings.proximityThreshold;
                
                for (int i = 0; i < targetVerticesList.Count; i++)
                {
                    Vector3 targetPos = targetVerticesList[i];
                    Vector3 bestNormal = targetOriginalNormalsList[i];
                    float minDistSq = float.MaxValue;

                    // Find nearest source vertex
                    for (int j = 0; j < sourceVerticesList.Count; j++)
                    {
                        float distSq = (targetPos - sourceVerticesList[j]).sqrMagnitude;
                        if (distSq < thresholdSq && distSq < minDistSq)
                        {
                            minDistSq = distSq;
                            bestNormal = sourceNormalsList[j];
                        }
                    }

                    if (minDistSq < float.MaxValue)
                    {
                        // Blend normals
                        targetNormalsList[i] = Vector3.Lerp(bestNormal, targetOriginalNormalsList[i], settings.proximityBlendStrength).normalized;
                    }
                }

                // Store preview data
                data.previewSourceNormals = sourceNormalsList.ToArray();
                data.previewTargetNormals = targetNormalsList.ToArray();
                data.previewTargetVertices = targetVerticesList.ToArray();
                data.previewGenerated = true;

                // Force scene view to repaint
                SceneView.RepaintAll();
                EditorUtility.SetDirty(data);
                
                Debug.Log($"[SeamlessNormalsDataEditor] Preview generated: {targetVerticesList.Count} target vertices, {sourceVerticesList.Count} source vertices. Enable 'Show Preview' to see visualization in Scene view.");
            }
            finally
            {
                isGeneratingPreview = false;
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!data.showPreview || !data.previewGenerated)
                return;

            if (data.previewTargetVertices == null || data.previewTargetNormals == null)
                return;

            // Draw source normals (blue) - if available
            if (data.previewSourceNormals != null && data.previewSourceNormals.Length > 0)
            {
                Handles.color = Color.blue;
                // Note: We don't have source vertex positions stored, so we'll skip drawing source normals
                // They would need to be stored separately if we want to visualize them
            }

            // Draw target normals (green = original, yellow = transferred)
            if (data.previewTargetVertices.Length == data.previewTargetNormals.Length)
            {
                // Draw transferred normals (yellow/green)
                Handles.color = new Color(1f, 1f, 0f, 0.8f); // Yellow
                float normalLength = 0.02f;
                
                // Sample every Nth vertex to avoid clutter
                int sampleRate = Mathf.Max(1, data.previewTargetVertices.Length / 500);
                
                for (int i = 0; i < data.previewTargetVertices.Length; i += sampleRate)
                {
                    Vector3 pos = data.previewTargetVertices[i];
                    Vector3 normal = data.previewTargetNormals[i];
                    Handles.DrawLine(pos, pos + normal * normalLength);
                }

                // Draw vertex positions as small dots
                Handles.color = new Color(0f, 1f, 0f, 0.5f); // Green
                for (int i = 0; i < data.previewTargetVertices.Length; i += sampleRate * 5)
                {
                    Vector3 forward = sceneView.camera != null ? sceneView.camera.transform.forward : Vector3.forward;
                    Handles.DrawSolidDisc(data.previewTargetVertices[i], forward, 0.001f);
                }
            }
        }

        private Mesh GetMeshFromRenderer(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr)
            {
                return smr.sharedMesh;
            }
            else if (renderer is MeshRenderer mr)
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                return mf?.sharedMesh;
            }

            return null;
        }
    }
}

