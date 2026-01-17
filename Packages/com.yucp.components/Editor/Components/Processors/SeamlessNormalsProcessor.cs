using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Seamless Normals components during avatar build.
    /// Transfers normals from source meshes to target meshes to create seamless appearance.
    /// </summary>
    public class SeamlessNormalsProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 200;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<SeamlessNormalsData>(true);
            
            if (dataList.Length == 0)
            {
                return true;
            }

            var progress = YUCPProgressWindow.Create();
            try
            {
                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];
                    
                    if (data.debugMode)
                    {
                        Debug.Log($"[SeamlessNormalsProcessor] Processing '{data.name}'", data);
                    }

                    try
                    {
                        ProcessComponent(data);
                        
                        float progressValue = (float)(i + 1) / dataList.Length;
                        progress.Progress(progressValue, $"Processed seamless normals {i + 1}/{dataList.Length}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SeamlessNormalsProcessor] Error processing '{data.name}': {ex.Message}", data);
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                progress.CloseWindow();
            }

            return true;
        }

        private void ProcessComponent(SeamlessNormalsData data)
        {
            // Validate data
            if (!ValidateData(data))
            {
                return;
            }

            // Get valid meshes
            Renderer[] sourceRenderers = data.GetSourceMeshes();
            Renderer[] targetRenderers = data.GetTargetMeshes();

            if (sourceRenderers.Length == 0)
            {
                Debug.LogError($"[SeamlessNormalsProcessor] No valid source meshes found for '{data.name}'.", data);
                return;
            }

            if (targetRenderers.Length == 0)
            {
                Debug.LogError($"[SeamlessNormalsProcessor] No valid target meshes found for '{data.name}'.", data);
                return;
            }

            // Create settings from data
            NormalBakeSettings settings = NormalBakeSettings.FromData(data);

            // Determine if we should use GPU
            bool useGPU = data.useGPUAcceleration && 
                         NormalTransferGPU.IsGPUAvailable() &&
                         (settings.method == NormalTransferMethod.Proximity || 
                          settings.method == NormalTransferMethod.Projection);

            int totalProcessed = 0;

            if (useGPU)
            {
                // GPU path
                try
                {
                    ProcessWithGPU(sourceRenderers, targetRenderers, settings, data.debugMode);
                    
                    // Count processed vertices
                    foreach (var targetRenderer in targetRenderers)
                    {
                        Mesh mesh = GetMeshFromRenderer(targetRenderer);
                        if (mesh != null)
                        {
                            totalProcessed += mesh.vertices.Length;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SeamlessNormalsProcessor] GPU transfer failed, falling back to CPU: {ex.Message}", data);
                    useGPU = false;
                }
            }

            if (!useGPU)
            {
                // CPU path
                NormalTransferCPU.TransferNormals(sourceRenderers, targetRenderers, settings, data.debugMode);
                
                // Count processed vertices
                foreach (var targetRenderer in targetRenderers)
                {
                    Mesh mesh = GetMeshFromRenderer(targetRenderer);
                    if (mesh != null)
                    {
                        totalProcessed += mesh.vertices.Length;
                    }
                }
            }

            // Update build stats
            string methodName = useGPU ? $"{settings.method} (GPU)" : settings.method.ToString();
            data.SetBuildStats(totalProcessed, methodName);

            if (data.debugMode)
            {
                Debug.Log($"[SeamlessNormalsProcessor] Processed {totalProcessed} vertices using {methodName} for '{data.name}'", data);
            }
        }

        private void ProcessWithGPU(
            Renderer[] sourceRenderers,
            Renderer[] targetRenderers,
            NormalBakeSettings settings,
            bool debugMode)
        {
            // Extract mesh data
            var sourceMeshes = new List<Mesh>();
            var sourceTransforms = new List<Transform>();
            
            foreach (var renderer in sourceRenderers)
            {
                Mesh mesh = GetMeshFromRenderer(renderer);
                if (mesh != null)
                {
                    sourceMeshes.Add(mesh);
                    sourceTransforms.Add(renderer.transform);
                }
            }

            // Process each target mesh
            foreach (var targetRenderer in targetRenderers)
            {
                Mesh targetMesh = GetMeshFromRenderer(targetRenderer);
                if (targetMesh == null) continue;

                // Create instance if needed
                Mesh instanceMesh = targetMesh;
                bool isShared = IsSharedMesh(targetMesh);
                
                if (isShared)
                {
                    instanceMesh = UnityEngine.Object.Instantiate(targetMesh);
                    instanceMesh.name = targetMesh.name + "_SeamlessNormals";
                    
                    if (targetRenderer is SkinnedMeshRenderer smr)
                    {
                        smr.sharedMesh = instanceMesh;
                    }
                    else if (targetRenderer is MeshRenderer mr)
                    {
                        MeshFilter mf = mr.GetComponent<MeshFilter>();
                        if (mf != null)
                        {
                            mf.sharedMesh = instanceMesh;
                        }
                    }
                }

                Vector3[] newNormals;
                
                switch (settings.method)
                {
                    case NormalTransferMethod.Proximity:
                        newNormals = NormalTransferGPU.ProximityTransfer(
                            sourceMeshes.ToArray(),
                            sourceTransforms.ToArray(),
                            instanceMesh,
                            targetRenderer.transform,
                            settings);
                        break;
                    case NormalTransferMethod.Projection:
                        newNormals = NormalTransferGPU.ProjectionTransfer(
                            sourceMeshes.ToArray(),
                            sourceTransforms.ToArray(),
                            instanceMesh,
                            targetRenderer.transform,
                            settings);
                        break;
                    default:
                        // Fall back to CPU for SharedField
                        NormalTransferCPU.TransferNormals(
                            sourceRenderers,
                            new[] { targetRenderer },
                            settings,
                            debugMode);
                        return;
                }

                // Apply new normals
                instanceMesh.normals = newNormals;
                instanceMesh.RecalculateTangents();
                
                EditorUtility.SetDirty(instanceMesh);
            }
        }

        private bool ValidateData(SeamlessNormalsData data)
        {
            if (data.sourceMeshes == null || data.sourceMeshes.Length == 0)
            {
                Debug.LogError($"[SeamlessNormalsProcessor] No source meshes specified for '{data.name}'.", data);
                return false;
            }

            if (data.targetMeshes == null || data.targetMeshes.Length == 0)
            {
                Debug.LogError($"[SeamlessNormalsProcessor] No target meshes specified for '{data.name}'.", data);
                return false;
            }

            // Check if at least one source mesh is valid
            bool hasValidSource = false;
            foreach (var renderer in data.sourceMeshes)
            {
                if (renderer != null)
                {
                    Mesh mesh = GetMeshFromRenderer(renderer);
                    if (mesh != null)
                    {
                        hasValidSource = true;
                        break;
                    }
                }
            }

            if (!hasValidSource)
            {
                Debug.LogError($"[SeamlessNormalsProcessor] No valid source meshes found for '{data.name}'.", data);
                return false;
            }

            // Check if at least one target mesh is valid
            bool hasValidTarget = false;
            foreach (var renderer in data.targetMeshes)
            {
                if (renderer != null)
                {
                    Mesh mesh = GetMeshFromRenderer(renderer);
                    if (mesh != null)
                    {
                        hasValidTarget = true;
                        break;
                    }
                }
            }

            if (!hasValidTarget)
            {
                Debug.LogError($"[SeamlessNormalsProcessor] No valid target meshes found for '{data.name}'.", data);
                return false;
            }

            return true;
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

        private bool IsSharedMesh(Mesh mesh)
        {
            if (mesh == null) return false;
            
            // Check if mesh is an asset (shared) or instance
            string path = AssetDatabase.GetAssetPath(mesh);
            return !string.IsNullOrEmpty(path);
        }
    }
}

