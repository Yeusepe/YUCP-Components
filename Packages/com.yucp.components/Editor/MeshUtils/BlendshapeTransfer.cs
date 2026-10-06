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

            if (!targetMesh.isReadable)
            {
                Debug.LogError($"[BlendshapeTransfer] Target mesh '{targetMesh.name}' is not readable. Enable Read/Write on its importer.", data);
                return false;
            }


            if (data.previewOriginalTargetMesh == null)
            {
                data.previewOriginalTargetMesh = targetMesh;
            }



            Mesh workingMesh = UnityEngine.Object.Instantiate(targetMesh);
            workingMesh.name = targetMesh.name + "_Blendshapes";



            if (blendshapesToTransfer != null && blendshapesToTransfer.Count > 0)
            {
                int removedCount = 0;
                for (int i = workingMesh.blendShapeCount - 1; i >= 0; i--)
                {
                    string existingBlendshapeName = workingMesh.GetBlendShapeName(i);
                    if (blendshapesToTransfer.Contains(existingBlendshapeName))
                    {


                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {



                    var preserved = new List<(string name, float weight, Vector3[] dv, Vector3[] dn, Vector3[] dt)>();
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
                                preserved.Add((existingName, frameWeight, frameDeltas, frameNormals, frameTangents));
                            }
                        }
                    }

                    workingMesh.ClearBlendShapes();
                    foreach (var frame in preserved)
                    {
                        workingMesh.AddBlendShapeFrame(frame.name, frame.weight, frame.dv, frame.dn, frame.dt);
                    }

                    if (data.debugMode)
                    {
                        Debug.Log($"[BlendshapeTransfer] Removed {removedCount} existing blendshapes from target mesh before transfer.", data);
                    }
                }
            }

            try
            {

                Mesh baseBakeMesh = new Mesh();
                sourceMesh.BakeMesh(baseBakeMesh);

                SurfaceClusterDetector.EvaluateCluster(
                    cluster,
                    baseBakeMesh.vertices,
                    baseBakeMesh.triangles,
                    out Vector3 baseClusterPosition,
                    out Vector3 baseClusterNormal,
                    out Vector3 baseClusterTangent);



                Vector3 baseTargetPointLocal = Vector3.zero;

                Vector3 baseClusterWorldPos = sourceMesh.transform.TransformPoint(baseClusterPosition);
                Vector3 baseTargetPointWorldPos = targetTransform.TransformPoint(baseTargetPointLocal);
                Vector3 clusterToTargetOffsetWorld = baseTargetPointWorldPos - baseClusterWorldPos;


                Vector3[] baseVertices = new Vector3[workingMesh.vertices.Length];
                Array.Copy(workingMesh.vertices, baseVertices, workingMesh.vertices.Length);

                int transferredCount = 0;


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
                        baseTargetPointLocal,
                        clusterToTargetOffsetWorld,
                        targetTransform,
                        sourceMesh.transform,
                        data))
                    {
                        transferredCount++;
                    }
                }




                data.previewOriginalMesh = targetMesh;
                data.previewWorkingMesh = workingMesh;

                if (data.previewOriginalTargetMesh == null)
                {
                    data.previewOriginalTargetMesh = targetMesh;
                }


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
	            Vector3 baseTargetPointLocal,
	            Vector3 clusterToTargetOffsetWorld,
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


            int sampleCount = data.samplesPerBlendshape;
            List<(float weight, Vector3[] vertices)> samples = new List<(float, Vector3[])>();


            Dictionary<int, float> originalWeights = new Dictionary<int, float>();
            for (int i = 0; i < sourceSharedMesh.blendShapeCount; i++)
            {
                originalWeights[i] = sourceMesh.GetBlendShapeWeight(i);
            }

            Mesh bakeMesh = new Mesh();
            bakeMesh.name = "BlendshapeTransfer_Temp";

            try
            {

                for (int i = 0; i < sampleCount; i++)
                {
                    float weight = i / (float)(sampleCount - 1) * 100f;


                    for (int b = 0; b < sourceSharedMesh.blendShapeCount; b++)
                    {
                        sourceMesh.SetBlendShapeWeight(b, b == blendshapeIndex ? weight : 0f);
                    }


                    sourceMesh.BakeMesh(bakeMesh);


                    SurfaceClusterDetector.EvaluateCluster(
                        cluster,
                        bakeMesh.vertices,
                        bakeMesh.triangles,
                        out Vector3 clusterPos,
                        out Vector3 clusterNorm,
                        out Vector3 clusterTan);


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


	                    Vector3 deformedClusterWorldPos = sourceTransform.TransformPoint(clusterPos);


	                    Vector3 targetPointWorldPos = deformedClusterWorldPos + clusterToTargetOffsetWorld;


	                    Vector3 basePointWorldPos = targetTransform.TransformPoint(baseTargetPointLocal);


	                    Vector3 pointWorldDelta = targetPointWorldPos - basePointWorldPos;



	                    Vector3 positionDeltaForRelativePosition;
	                    if (targetTransform.parent != null)
	                    {

	                        Vector3 newPointWorldPos = basePointWorldPos + pointWorldDelta;
	                        Vector3 newPointLocalPos = targetTransform.parent.InverseTransformPoint(newPointWorldPos);
	                        positionDeltaForRelativePosition = newPointLocalPos - targetTransform.localPosition;
	                    }
	                    else
	                    {

	                        positionDeltaForRelativePosition = targetTransform.InverseTransformDirection(pointWorldDelta);
	                    }




                    Vector3 adjustedPositionDelta = positionDeltaForRelativePosition;



	                    Vector3[] transformedVertices = TransformVertices(
	                        baseVertices,
	                        baseTargetPointLocal,
	                        adjustedPositionDelta,
	                        result.rotation,
	                        result.scale);

                    samples.Add((weight, transformedVertices));
                }


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


            int existingIndex = workingMesh.GetBlendShapeIndex(blendshapeName);
            if (existingIndex >= 0)
            {




                Debug.LogWarning($"[BlendshapeTransfer] Blendshape '{blendshapeName}' already exists on target mesh. Skipping transfer. Clear preview first to regenerate.", data);
                return false;
            }


            bool isFirstFrame = true;
            foreach (var (weight, vertices) in samples.OrderBy(s => s.weight))
            {

                Vector3[] deltas = new Vector3[vertices.Length];
                Vector3[] normals = new Vector3[vertices.Length];
                Vector3[] tangents = new Vector3[vertices.Length];

                for (int i = 0; i < vertices.Length; i++)
                {
                    deltas[i] = vertices[i] - baseVertices[i];

                    normals[i] = Vector3.zero;
                    tangents[i] = Vector3.zero;
                }

                try
                {
                    if (isFirstFrame)
                    {

                        workingMesh.AddBlendShapeFrame(blendshapeName, weight, deltas, normals, tangents);
                        existingIndex = workingMesh.GetBlendShapeIndex(blendshapeName);
                        isFirstFrame = false;
                    }
                    else
                    {

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

                Vector3 baseWorldPos = sourceTransform.TransformPoint(baseClusterPosition);
                Vector3 deformedWorldPos = sourceTransform.TransformPoint(deformedClusterPosition);

                BlendshapeSolver.SurfaceFrame baseFrame = BlendshapeSolver.SurfaceFrame.FromVectors(
                    baseClusterPosition, baseClusterNormal, baseClusterTangent);
                BlendshapeSolver.SurfaceFrame deformedFrame = BlendshapeSolver.SurfaceFrame.FromVectors(
                    deformedClusterPosition, deformedClusterNormal, deformedClusterTangent);

                Quaternion baseWorldRot = sourceTransform.rotation * baseFrame.rotation;
                Quaternion deformedWorldRot = sourceTransform.rotation * deformedFrame.rotation;


                Vector3 positionDelta = deformedWorldPos - baseWorldPos;
                Quaternion rotationDelta = deformedWorldRot * Quaternion.Inverse(baseWorldRot);


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


                if (data.solverMode == Components.SolverMode.RigidNormalOffset)
                {
                    Vector3 worldNormal = sourceTransform.TransformDirection(deformedClusterNormal);
                    positionDelta += worldNormal * data.normalOffset;
                }




                if (targetTransform.parent != null)
                {


                    Vector3 targetWorldPos = targetTransform.position;
                    Vector3 newWorldPos = targetWorldPos + positionDelta;

                    Vector3 newLocalPos = targetTransform.parent.InverseTransformPoint(newWorldPos);
                    result.position = newLocalPos - targetTransform.localPosition;

                    result.rotation = Quaternion.Inverse(targetTransform.parent.rotation) * rotationDelta * targetTransform.parent.rotation;
                }
                else
                {





                    Vector3 localDirection = targetTransform.InverseTransformDirection(positionDelta);
                    result.position = localDirection;


                    result.rotation = Quaternion.Inverse(targetTransform.rotation) * rotationDelta * targetTransform.rotation;
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
	            Vector3 pivot,
	            Vector3 positionDelta,
	            Quaternion rotationDelta,
	            Vector3 scaleDelta)
	        {
	            Vector3[] transformed = new Vector3[baseVertices.Length];



	            for (int i = 0; i < baseVertices.Length; i++)
	            {
	                Vector3 vertex = baseVertices[i];


	                Vector3 relativeVertex = vertex - pivot;


	                relativeVertex = Vector3.Scale(relativeVertex, scaleDelta);


	                relativeVertex = rotationDelta * relativeVertex;


	                transformed[i] = pivot + relativeVertex + positionDelta;
	            }

	            return transformed;
	        }
    }
}
