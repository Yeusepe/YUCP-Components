using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Transfers blendshapes from a source mesh to a target mesh by deforming the target's vertices
    /// to match the source's surface deformation at different blendshape weights.
    /// </summary>
    public static class BlendshapeTransfer
    {
        /// <summary>
        /// Transfer blendshapes from source mesh to target mesh.
        /// </summary>
        public static bool TransferBlendshapes(
            SkinnedMeshRenderer sourceMesh,
            UnityEngine.Object targetMeshObj,
            List<string> blendshapesToTransfer,
            SurfaceCluster cluster,
            Components.AttachToBlendshapeData data)
        {
            if (sourceMesh == null || sourceMesh.sharedMesh == null)
            {
                Debug.LogError("[BlendshapeTransfer] Invalid source mesh");
                return false;
            }

            // Get target mesh (SkinnedMeshRenderer or MeshFilter)
            Mesh targetMesh = null;
            SkinnedMeshRenderer targetSkinnedMesh = null;
            MeshFilter targetMeshFilter = null;
            Transform targetTransform = null;

            if (targetMeshObj is SkinnedMeshRenderer skinned)
            {
                targetSkinnedMesh = skinned;
                targetMesh = skinned.sharedMesh;
                targetTransform = skinned.transform;
            }
            else if (targetMeshObj is MeshFilter filter)
            {
                targetMeshFilter = filter;
                targetMesh = filter.sharedMesh;
                targetTransform = filter.transform;
            }
            else if (targetMeshObj is GameObject go)
            {
                // Try to find MeshFilter first (more common for static meshes)
                targetMeshFilter = go.GetComponent<MeshFilter>();
                if (targetMeshFilter != null)
                {
                    targetMesh = targetMeshFilter.sharedMesh;
                    targetTransform = targetMeshFilter.transform;
                }
                else
                {
                    targetSkinnedMesh = go.GetComponent<SkinnedMeshRenderer>();
                    if (targetSkinnedMesh != null)
                    {
                        targetMesh = targetSkinnedMesh.sharedMesh;
                        targetTransform = targetSkinnedMesh.transform;
                    }
                }
            }
            else if (targetMeshObj == null)
            {
                // Try to find on the component's GameObject
                var component = data;
                targetSkinnedMesh = component.GetComponent<SkinnedMeshRenderer>();
                if (targetSkinnedMesh != null)
                {
                    targetMesh = targetSkinnedMesh.sharedMesh;
                    targetTransform = targetSkinnedMesh.transform;
                }
                else
                {
                    targetMeshFilter = component.GetComponent<MeshFilter>();
                    if (targetMeshFilter != null)
                    {
                        targetMesh = targetMeshFilter.sharedMesh;
                        targetTransform = targetMeshFilter.transform;
                    }
                }
            }

            if (targetMesh == null)
            {
                Debug.LogError("[BlendshapeTransfer] Target mesh not found. Set targetMeshToModify or add SkinnedMeshRenderer/MeshFilter to GameObject.", data);
                return false;
            }

            // Store the original target mesh BEFORE any modifications (for categorization in preview)
            if (data.previewOriginalTargetMesh == null)
            {
                data.previewOriginalTargetMesh = targetMesh;
            }

            // Create a copy of the mesh to modify (can't modify shared mesh directly, especially Unity primitives)
            // For preview, we need to remove any existing transferred blendshapes first
            Mesh workingMesh = UnityEngine.Object.Instantiate(targetMesh);
            workingMesh.name = targetMesh.name + "_Blendshapes";
            
            // Remove any existing blendshapes that we're about to transfer (for preview regeneration)
            // Unity doesn't have RemoveBlendShape, so we create a new mesh without those blendshapes
            if (blendshapesToTransfer != null && blendshapesToTransfer.Count > 0)
            {
                int removedCount = 0;
                for (int i = workingMesh.blendShapeCount - 1; i >= 0; i--)
                {
                    string existingBlendshapeName = workingMesh.GetBlendShapeName(i);
                    if (blendshapesToTransfer.Contains(existingBlendshapeName))
                    {
                        // Can't remove directly, so we'll need to recreate the mesh
                        // For now, we'll handle this in TransferSingleBlendshape by skipping existing ones
                        removedCount++;
                    }
                }
                
                if (removedCount > 0)
                {
                    // Create a fresh mesh without the blendshapes we're about to transfer
                    Mesh cleanMesh = new Mesh();
                    cleanMesh.name = workingMesh.name;
                    cleanMesh.vertices = workingMesh.vertices;
                    cleanMesh.triangles = workingMesh.triangles;
                    cleanMesh.normals = workingMesh.normals;
                    cleanMesh.uv = workingMesh.uv;
                    cleanMesh.tangents = workingMesh.tangents;
                    cleanMesh.colors = workingMesh.colors;
                    cleanMesh.bounds = workingMesh.bounds;
                    
                    // Copy only blendshapes that we're NOT transferring
                    for (int i = 0; i < workingMesh.blendShapeCount; i++)
                    {
                        string existingName = workingMesh.GetBlendShapeName(i);
                        if (!blendshapesToTransfer.Contains(existingName))
                        {
                            int frameCount = workingMesh.GetBlendShapeFrameCount(i);
                            for (int f = 0; f < frameCount; f++)
                            {
                                float frameWeight = workingMesh.GetBlendShapeFrameWeight(i, f);
                                Vector3[] frameDeltas = new Vector3[workingMesh.vertexCount];
                                Vector3[] frameNormals = new Vector3[workingMesh.vertexCount];
                                Vector3[] frameTangents = new Vector3[workingMesh.vertexCount];
                                workingMesh.GetBlendShapeFrameVertices(i, f, frameDeltas, frameNormals, frameTangents);
                                cleanMesh.AddBlendShapeFrame(existingName, frameWeight, frameDeltas, frameNormals, frameTangents);
                            }
                        }
                    }
                    
                    UnityEngine.Object.DestroyImmediate(workingMesh);
                    workingMesh = cleanMesh;
                    
                    if (data.debugMode)
                    {
                        Debug.Log($"[BlendshapeTransfer] Removed {removedCount} existing blendshapes from target mesh before transfer.", data);
                    }
                }
            }
            
            // Ensure mesh is readable/writable (important for Unity primitives)
            if (!workingMesh.isReadable)
            {
                Debug.LogWarning($"[BlendshapeTransfer] Target mesh '{targetMesh.name}' is not readable. Creating a new readable copy.", data);
                // Create a new mesh with the same data
                Mesh newMesh = new Mesh();
                newMesh.name = workingMesh.name;
                newMesh.vertices = workingMesh.vertices;
                newMesh.triangles = workingMesh.triangles;
                newMesh.normals = workingMesh.normals;
                newMesh.uv = workingMesh.uv;
                newMesh.tangents = workingMesh.tangents;
                newMesh.colors = workingMesh.colors;
                newMesh.bounds = workingMesh.bounds;
                
                // Copy blendshapes
                for (int i = 0; i < workingMesh.blendShapeCount; i++)
                {
                    string name = workingMesh.GetBlendShapeName(i);
                    int frameCount = workingMesh.GetBlendShapeFrameCount(i);
                    for (int f = 0; f < frameCount; f++)
                    {
                        float weight = workingMesh.GetBlendShapeFrameWeight(i, f);
                        Vector3[] deltas = new Vector3[workingMesh.vertexCount];
                        Vector3[] normals = new Vector3[workingMesh.vertexCount];
                        Vector3[] tangents = new Vector3[workingMesh.vertexCount];
                        workingMesh.GetBlendShapeFrameVertices(i, f, deltas, normals, tangents);
                        newMesh.AddBlendShapeFrame(name, weight, deltas, normals, tangents);
                    }
                }
                
                UnityEngine.Object.DestroyImmediate(workingMesh);
                workingMesh = newMesh;
            }

            try
            {
                // Get base state (all blendshapes at 0)
                Mesh baseBakeMesh = new Mesh();
                sourceMesh.BakeMesh(baseBakeMesh);
                
                SurfaceClusterDetector.EvaluateCluster(
                    cluster,
                    baseBakeMesh.vertices,
                    baseBakeMesh.triangles,
                    out Vector3 baseClusterPosition,
                    out Vector3 baseClusterNormal,
                    out Vector3 baseClusterTangent);

                // Calculate base transform for target mesh
                Vector3 baseTargetCentroid = CalculateCentroid(workingMesh.vertices);
                Quaternion baseTargetRotation = Quaternion.identity;

                // Store original vertex positions
                Vector3[] baseVertices = new Vector3[workingMesh.vertices.Length];
                Array.Copy(workingMesh.vertices, baseVertices, workingMesh.vertices.Length);

                int transferredCount = 0;

                // Transfer each blendshape
                foreach (string blendshapeName in blendshapesToTransfer)
                {
                    if (TransferSingleBlendshape(
                        sourceMesh,
                        blendshapeName,
                        cluster,
                        workingMesh,
                        baseVertices,
                        baseClusterPosition,
                        baseClusterNormal,
                        baseClusterTangent,
                        baseTargetCentroid,
                        targetTransform,
                        sourceMesh.transform,
                        data))
                    {
                        transferredCount++;
                    }
                }

                // Store original mesh and working mesh for restoration
                // Always update previewOriginalMesh to the current targetMesh (in case it changed after restore)
                // This ensures we always restore to the correct original mesh
                data.previewOriginalMesh = targetMesh;
                data.previewWorkingMesh = workingMesh;
                // Store the original target mesh (before any transfers) for categorization
                if (data.previewOriginalTargetMesh == null)
                {
                    data.previewOriginalTargetMesh = targetMesh;
                }
                
                // Apply the modified mesh (copy, not original)
                if (targetSkinnedMesh != null)
                {
                    targetSkinnedMesh.sharedMesh = workingMesh;
                    if (data.debugMode)
                    {
                        Debug.Log($"[BlendshapeTransfer] Applied blendshapes to SkinnedMeshRenderer '{targetSkinnedMesh.name}' (using mesh copy, original preserved)", data);
                    }
                }
                else if (targetMeshFilter != null)
                {
                    targetMeshFilter.sharedMesh = workingMesh;
                    if (data.debugMode)
                    {
                        Debug.Log($"[BlendshapeTransfer] Applied blendshapes to MeshFilter '{targetMeshFilter.name}' (using mesh copy, original preserved)", data);
                    }
                }

                UnityEngine.Object.DestroyImmediate(baseBakeMesh);

                Debug.Log($"[BlendshapeTransfer] Successfully transferred {transferredCount}/{blendshapesToTransfer.Count} blendshapes to target mesh", data);
                
                if (transferredCount == 0)
                {
                    Debug.LogError("[BlendshapeTransfer] No blendshapes were transferred. Check that blendshapes exist on source mesh and transfer succeeded.", data);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BlendshapeTransfer] Error transferring blendshapes: {ex.Message}", data);
                UnityEngine.Object.DestroyImmediate(workingMesh);
                return false;
            }
        }

        private static bool TransferSingleBlendshape(
            SkinnedMeshRenderer sourceMesh,
            string blendshapeName,
            SurfaceCluster cluster,
            Mesh workingMesh,
            Vector3[] baseVertices,
            Vector3 baseClusterPosition,
            Vector3 baseClusterNormal,
            Vector3 baseClusterTangent,
            Vector3 baseTargetCentroid,
            Transform targetTransform,
            Transform sourceTransform,
            Components.AttachToBlendshapeData data)
        {
            Mesh sourceSharedMesh = sourceMesh.sharedMesh;
            int blendshapeIndex = sourceSharedMesh.GetBlendShapeIndex(blendshapeName);

            if (blendshapeIndex < 0)
            {
                Debug.LogWarning($"[BlendshapeTransfer] Blendshape '{blendshapeName}' not found on source mesh", data);
                return false;
            }

            // Sample at different weights
            int sampleCount = data.samplesPerBlendshape;
            List<(float weight, Vector3[] vertices)> samples = new List<(float, Vector3[])>();

            // Store original blendshape weights
            Dictionary<int, float> originalWeights = new Dictionary<int, float>();
            for (int i = 0; i < sourceSharedMesh.blendShapeCount; i++)
            {
                originalWeights[i] = sourceMesh.GetBlendShapeWeight(i);
            }

            Mesh bakeMesh = new Mesh();
            bakeMesh.name = "BlendshapeTransfer_Temp";

            try
            {
                // Sample at different weights
                for (int i = 0; i < sampleCount; i++)
                {
                    float weight = i / (float)(sampleCount - 1) * 100f; // 0 to 100

                    // Set only this blendshape, zero others
                    for (int b = 0; b < sourceSharedMesh.blendShapeCount; b++)
                    {
                        sourceMesh.SetBlendShapeWeight(b, b == blendshapeIndex ? weight : 0f);
                    }

                    // Bake the mesh at this pose
                    sourceMesh.BakeMesh(bakeMesh);

                    // Evaluate cluster at this pose
                    SurfaceClusterDetector.EvaluateCluster(
                        cluster,
                        bakeMesh.vertices,
                        bakeMesh.triangles,
                        out Vector3 clusterPos,
                        out Vector3 clusterNorm,
                        out Vector3 clusterTan);

                    // Calculate transform delta from base state
                    BlendshapeSolver.SolverResult result = CalculateTransformDelta(
                        baseClusterPosition,
                        baseClusterNormal,
                        baseClusterTangent,
                        clusterPos,
                        clusterNorm,
                        clusterTan,
                        targetTransform,
                        sourceTransform,
                        data);

                    if (!result.success)
                    {
                        continue;
                    }

                    // Transform target mesh vertices
                    Vector3[] transformedVertices = TransformVertices(
                        baseVertices,
                        baseTargetCentroid,
                        result.position,
                        result.rotation,
                        result.scale);

                    samples.Add((weight, transformedVertices));
                }

                // Restore original weights
                foreach (var kvp in originalWeights)
                {
                    sourceMesh.SetBlendShapeWeight(kvp.Key, kvp.Value);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bakeMesh);
            }

            if (samples.Count == 0)
            {
                Debug.LogWarning($"[BlendshapeTransfer] No valid samples for blendshape '{blendshapeName}'", data);
                return false;
            }

            // Check if blendshape already exists
            int existingIndex = workingMesh.GetBlendShapeIndex(blendshapeName);
            if (existingIndex >= 0)
            {
                // Blendshape already exists - we need to remove it first since Unity doesn't allow
                // adding frames to existing blendshapes if they conflict, and we can't modify existing frames
                // The only way is to recreate the mesh without this blendshape, then add it fresh
                // For now, skip it and log a warning - the user should clear preview first
                Debug.LogWarning($"[BlendshapeTransfer] Blendshape '{blendshapeName}' already exists on target mesh. Skipping transfer. Clear preview first to regenerate.", data);
                return false;
            }

            // Add blendshape frames (blendshape doesn't exist, so we can create it fresh)
            bool isFirstFrame = true;
            foreach (var (weight, vertices) in samples.OrderBy(s => s.weight))
            {
                // Calculate deltas from base
                Vector3[] deltas = new Vector3[vertices.Length];
                Vector3[] normals = new Vector3[vertices.Length];
                Vector3[] tangents = new Vector3[vertices.Length];

                for (int i = 0; i < vertices.Length; i++)
                {
                    deltas[i] = vertices[i] - baseVertices[i];
                    // For simplicity, use zero normals/tangents (Unity will recalculate)
                    normals[i] = Vector3.zero;
                    tangents[i] = Vector3.zero;
                }

                try
                {
                    if (isFirstFrame)
                    {
                        // First frame - create new blendshape
                        workingMesh.AddBlendShapeFrame(blendshapeName, weight, deltas, normals, tangents);
                        existingIndex = workingMesh.GetBlendShapeIndex(blendshapeName);
                        isFirstFrame = false;
                    }
                    else
                    {
                        // Additional frames - add to existing blendshape (must be in ascending weight order)
                        if (existingIndex >= 0)
                        {
                            workingMesh.AddBlendShapeFrame(blendshapeName, weight, deltas, normals, tangents);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[BlendshapeTransfer] Failed to add blendshape frame for '{blendshapeName}' at weight {weight}: {ex.Message}", data);
                    return false;
                }
            }

            return true;
        }

        private static BlendshapeSolver.SolverResult CalculateTransformDelta(
            Vector3 baseClusterPosition,
            Vector3 baseClusterNormal,
            Vector3 baseClusterTangent,
            Vector3 deformedClusterPosition,
            Vector3 deformedClusterNormal,
            Vector3 deformedClusterTangent,
            Transform targetTransform,
            Transform sourceTransform,
            Components.AttachToBlendshapeData data)
        {
            BlendshapeSolver.SolverResult result = new BlendshapeSolver.SolverResult
            {
                scale = Vector3.one,
                success = false
            };

            try
            {
                // Calculate world space positions and rotations
                Vector3 baseWorldPos = sourceTransform.TransformPoint(baseClusterPosition);
                Vector3 deformedWorldPos = sourceTransform.TransformPoint(deformedClusterPosition);

                BlendshapeSolver.SurfaceFrame baseFrame = BlendshapeSolver.SurfaceFrame.FromVectors(
                    baseClusterPosition, baseClusterNormal, baseClusterTangent);
                BlendshapeSolver.SurfaceFrame deformedFrame = BlendshapeSolver.SurfaceFrame.FromVectors(
                    deformedClusterPosition, deformedClusterNormal, deformedClusterTangent);

                Quaternion baseWorldRot = sourceTransform.rotation * baseFrame.rotation;
                Quaternion deformedWorldRot = sourceTransform.rotation * deformedFrame.rotation;

                // Calculate deltas
                Vector3 positionDelta = deformedWorldPos - baseWorldPos;
                Quaternion rotationDelta = deformedWorldRot * Quaternion.Inverse(baseWorldRot);

                // Calculate scale delta for Affine mode
                Vector3 scaleDelta = Vector3.one;
                if (data.solverMode == Components.SolverMode.Affine)
                {
                    float baseSize = baseClusterTangent.magnitude;
                    float deformedSize = deformedClusterTangent.magnitude;
                    if (baseSize > 0.001f)
                    {
                        float scaleRatio = Mathf.Clamp(deformedSize / baseSize, 0.8f, 1.2f);
                        scaleDelta = new Vector3(scaleRatio, scaleRatio, scaleRatio);
                    }
                }

                // Apply normal offset for RigidNormalOffset mode
                if (data.solverMode == Components.SolverMode.RigidNormalOffset)
                {
                    Vector3 worldNormal = sourceTransform.TransformDirection(deformedClusterNormal);
                    positionDelta += worldNormal * data.normalOffset;
                }

                // Convert to local space relative to target
                if (targetTransform.parent != null)
                {
                    result.position = targetTransform.parent.InverseTransformDirection(positionDelta);
                    result.rotation = Quaternion.Inverse(targetTransform.parent.rotation) * rotationDelta * targetTransform.parent.rotation;
                }
                else
                {
                    result.position = positionDelta;
                    result.rotation = rotationDelta;
                }

                result.scale = scaleDelta;
                result.success = true;
            }
            catch (System.Exception ex)
            {
                result.errorMessage = $"Transform calculation failed: {ex.Message}";
            }

            return result;
        }

        private static Vector3[] TransformVertices(
            Vector3[] baseVertices,
            Vector3 centroid,
            Vector3 positionDelta,
            Quaternion rotationDelta,
            Vector3 scaleDelta)
        {
            Vector3[] transformed = new Vector3[baseVertices.Length];

            for (int i = 0; i < baseVertices.Length; i++)
            {
                Vector3 vertex = baseVertices[i];

                // Apply scale
                vertex = Vector3.Scale(vertex - centroid, scaleDelta) + centroid;

                // Apply rotation around centroid
                vertex = centroid + rotationDelta * (vertex - centroid);

                // Apply translation
                vertex += positionDelta;

                transformed[i] = vertex;
            }

            return transformed;
        }

        private static Vector3 CalculateCentroid(Vector3[] vertices)
        {
            if (vertices.Length == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            foreach (var v in vertices)
            {
                sum += v;
            }

            return sum / vertices.Length;
        }
    }
}

