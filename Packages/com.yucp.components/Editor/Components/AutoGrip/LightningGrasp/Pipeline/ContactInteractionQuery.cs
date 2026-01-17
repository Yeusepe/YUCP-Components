using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Queries which object points can be reached by which contact patches.
    /// Returns the interaction matrix used for contact domain building.
    /// </summary>
    public class ContactInteractionQuery
    {
        private ContactField contactField;
        private LBVHS2Bundle bvh;
        private ComputeShader computeShader;

        // GPU buffers
        private ComputeBuffer objectPointsBuffer;
        private ComputeBuffer objectNormalsBuffer;
        private ComputeBuffer interactionMatrixBuffer;
        private ComputeBuffer nearestHandPointBuffer;
        private ComputeBuffer linkInteractionBuffer;
        private ComputeBuffer linkToPatchStartBuffer;
        private ComputeBuffer linkToPatchesBuffer;

        public ContactInteractionQuery(ContactField field, LBVHS2Bundle bvh, ComputeShader shader = null)
        {
            this.contactField = field;
            this.bvh = bvh;
            this.computeShader = shader;
        }

        /// <summary>
        /// Query interaction matrix using CPU (fallback when GPU unavailable).
        /// Returns: [numPatches, numObjectPoints] matrix where 1 = patch can reach point.
        /// </summary>
        public int[,] QueryInteractionMatrixCPU(
            Vector3[] objectPoints,
            Vector3[] objectNormals,
            float maxDistance = 0.05f)
        {
            int numPatches = contactField.patches.Count;
            int numPoints = objectPoints.Length;

            var matrix = new int[numPatches, numPoints];
            var nearestHandPoint = new int[numPatches, numPoints];

            for (int p = 0; p < numPoints; p++)
            {
                var hitPatches = bvh.QueryPoint(objectPoints[p], objectNormals[p], maxDistance);

                foreach (var patchId in hitPatches)
                {
                    if (patchId >= 0 && patchId < numPatches)
                    {
                        matrix[patchId, p] = 1;
                    }
                }
            }

            return matrix;
        }

        /// <summary>
        /// Query interaction matrix using GPU compute shader.
        /// </summary>
        public int[,] QueryInteractionMatrixGPU(
            Vector3[] objectPoints,
            Vector3[] objectNormals,
            float maxDistance = 0.05f)
        {
            if (computeShader == null)
            {
                Debug.LogWarning("[ContactInteractionQuery] No compute shader, falling back to CPU");
                return QueryInteractionMatrixCPU(objectPoints, objectNormals, maxDistance);
            }

            int numPatches = contactField.patches.Count;
            int numPoints = objectPoints.Length;

            // Upload object data
            UploadObjectData(objectPoints, objectNormals);

            // Allocate output buffers
            int matrixSize = numPatches * numPoints;
            if (interactionMatrixBuffer == null || interactionMatrixBuffer.count != matrixSize)
            {
                interactionMatrixBuffer?.Release();
                nearestHandPointBuffer?.Release();

                interactionMatrixBuffer = new ComputeBuffer(matrixSize, sizeof(int));
                nearestHandPointBuffer = new ComputeBuffer(matrixSize, sizeof(int));
            }

            // Clear output
            var zeros = new int[matrixSize];
            interactionMatrixBuffer.SetData(zeros);
            var negOnes = new int[matrixSize];
            for (int i = 0; i < matrixSize; i++) negOnes[i] = -1;
            nearestHandPointBuffer.SetData(negOnes);

            // Set shader parameters
            int kernelId = computeShader.FindKernel("TraverseContactField");
            bvh.BindToShader(computeShader, kernelId);

            computeShader.SetBuffer(kernelId, "_ObjectPoints", objectPointsBuffer);
            computeShader.SetBuffer(kernelId, "_ObjectNormals", objectNormalsBuffer);
            computeShader.SetBuffer(kernelId, "_InteractionMatrix", interactionMatrixBuffer);
            computeShader.SetBuffer(kernelId, "_NearestHandPointIdx", nearestHandPointBuffer);

            computeShader.SetInt("_NumPatches", numPatches);
            computeShader.SetInt("_NumObjectPoints", numPoints);
            computeShader.SetFloat("_MaxContactDistance", maxDistance);
            computeShader.SetFloat("_AngleThreshold", Mathf.Cos(Mathf.PI * 0.5f));

            // Dispatch
            int threadGroups = Mathf.CeilToInt(numPoints / 64f);
            computeShader.Dispatch(kernelId, threadGroups, 1, 1);

            // Read back results
            var flatMatrix = new int[matrixSize];
            interactionMatrixBuffer.GetData(flatMatrix);

            // Reshape to 2D
            var matrix = new int[numPatches, numPoints];
            for (int p = 0; p < numPatches; p++)
            {
                for (int pt = 0; pt < numPoints; pt++)
                {
                    matrix[p, pt] = flatMatrix[p * numPoints + pt];
                }
            }

            return matrix;
        }

        /// <summary>
        /// Reduce patch interaction to link interaction.
        /// Returns: [numLinks, numObjectPoints] matrix.
        /// </summary>
        public int[,] ReduceToLinkInteraction(int[,] patchInteraction)
        {
            int numLinks = contactField.contactLinkBones.Count;
            int numPoints = patchInteraction.GetLength(1);

            var linkMatrix = new int[numLinks, numPoints];

            for (int linkIdx = 0; linkIdx < numLinks; linkIdx++)
            {
                var bone = contactField.contactLinkBones[linkIdx];
                var patchIds = contactField.GetPatchIdsForBone(bone);

                for (int pt = 0; pt < numPoints; pt++)
                {
                    foreach (var patchId in patchIds)
                    {
                        if (patchInteraction[patchId, pt] > 0)
                        {
                            linkMatrix[linkIdx, pt] = 1;
                            break;
                        }
                    }
                }
            }

            return linkMatrix;
        }

        /// <summary>
        /// Get nearest hand point indices for each (patch, object point) pair.
        /// </summary>
        public int[,] GetNearestHandPointIndices(
            Vector3[] objectPoints,
            Vector3[] objectNormals,
            float maxDistance = 0.05f)
        {
            int numPatches = contactField.patches.Count;
            int numPoints = objectPoints.Length;

            var indices = new int[numPatches, numPoints];

            // Initialize to -1
            for (int p = 0; p < numPatches; p++)
                for (int pt = 0; pt < numPoints; pt++)
                    indices[p, pt] = -1;

            // Query each point
            for (int pt = 0; pt < numPoints; pt++)
            {
                var hitPatches = bvh.QueryPoint(objectPoints[pt], objectNormals[pt], maxDistance);
                foreach (var patchId in hitPatches)
                {
                    if (patchId >= 0 && patchId < numPatches)
                    {
                        // For now, just mark as having some index (actual index tracking would need BVH enhancement)
                        indices[patchId, pt] = 0;
                    }
                }
            }

            return indices;
        }

        private void UploadObjectData(Vector3[] points, Vector3[] normals)
        {
            if (objectPointsBuffer == null || objectPointsBuffer.count != points.Length)
            {
                objectPointsBuffer?.Release();
                objectNormalsBuffer?.Release();

                objectPointsBuffer = new ComputeBuffer(points.Length, 3 * sizeof(float));
                objectNormalsBuffer = new ComputeBuffer(normals.Length, 3 * sizeof(float));
            }

            objectPointsBuffer.SetData(points);
            objectNormalsBuffer.SetData(normals);
        }

        public void ReleaseBuffers()
        {
            objectPointsBuffer?.Release();
            objectNormalsBuffer?.Release();
            interactionMatrixBuffer?.Release();
            nearestHandPointBuffer?.Release();
            linkInteractionBuffer?.Release();
            linkToPatchStartBuffer?.Release();
            linkToPatchesBuffer?.Release();

            objectPointsBuffer = null;
            objectNormalsBuffer = null;
            interactionMatrixBuffer = null;
            nearestHandPointBuffer = null;
            linkInteractionBuffer = null;
            linkToPatchStartBuffer = null;
            linkToPatchesBuffer = null;
        }
    }

    /// <summary>
    /// Contact domain for a single finger link.
    /// Contains the object surface points this link can reach.
    /// </summary>
    public class ContactDomain
    {
        public HumanBodyBones linkBone;
        public List<int> objectPointIndices = new List<int>();
        public List<Vector3> objectPoints = new List<Vector3>();
        public List<Vector3> objectNormals = new List<Vector3>();

        /// <summary>
        /// Sample a random point from this domain.
        /// </summary>
        public (Vector3 point, Vector3 normal, int index) SampleRandom()
        {
            if (objectPointIndices.Count == 0)
                return (Vector3.zero, Vector3.up, -1);

            int idx = Random.Range(0, objectPointIndices.Count);
            return (objectPoints[idx], objectNormals[idx], objectPointIndices[idx]);
        }

        public int PointCount => objectPointIndices.Count;
    }

    /// <summary>
    /// Builds contact domains from interaction matrix.
    /// </summary>
    public class ContactDomainBuilder
    {
        /// <summary>
        /// Build contact domains from link interaction matrix.
        /// </summary>
        public static Dictionary<HumanBodyBones, ContactDomain> BuildDomains(
            ContactField field,
            int[,] linkInteraction,
            Vector3[] objectPoints,
            Vector3[] objectNormals)
        {
            var domains = new Dictionary<HumanBodyBones, ContactDomain>();

            int numLinks = linkInteraction.GetLength(0);
            int numPoints = linkInteraction.GetLength(1);

            for (int linkIdx = 0; linkIdx < numLinks && linkIdx < field.contactLinkBones.Count; linkIdx++)
            {
                var bone = field.contactLinkBones[linkIdx];
                var domain = new ContactDomain { linkBone = bone };

                for (int pt = 0; pt < numPoints; pt++)
                {
                    if (linkInteraction[linkIdx, pt] > 0)
                    {
                        domain.objectPointIndices.Add(pt);
                        domain.objectPoints.Add(objectPoints[pt]);
                        domain.objectNormals.Add(objectNormals[pt]);
                    }
                }

                if (domain.PointCount > 0)
                {
                    domains[bone] = domain;
                }
            }

            return domains;
        }

        /// <summary>
        /// Filter domains to select n_contact fingers based on dependency.
        /// Ensures selected fingers can work independently.
        /// </summary>
        public static List<HumanBodyBones> SelectContactFingers(
            Dictionary<HumanBodyBones, ContactDomain> domains,
            int numContacts = 3)
        {
            var selected = new List<HumanBodyBones>();

            // Prioritize: middle, index, thumb, ring, little
            var priority = new HumanBodyBones[]
            {
                HumanBodyBones.RightMiddleProximal, HumanBodyBones.LeftMiddleProximal,
                HumanBodyBones.RightIndexProximal, HumanBodyBones.LeftIndexProximal,
                HumanBodyBones.RightThumbProximal, HumanBodyBones.LeftThumbProximal,
                HumanBodyBones.RightRingProximal, HumanBodyBones.LeftRingProximal,
                HumanBodyBones.RightLittleProximal, HumanBodyBones.LeftLittleProximal,
                // Also check intermediate/distal
                HumanBodyBones.RightMiddleDistal, HumanBodyBones.LeftMiddleDistal,
                HumanBodyBones.RightIndexDistal, HumanBodyBones.LeftIndexDistal,
                HumanBodyBones.RightThumbDistal, HumanBodyBones.LeftThumbDistal,
            };

            foreach (var bone in priority)
            {
                if (domains.ContainsKey(bone) && domains[bone].PointCount > 0)
                {
                    // Check if this finger (proximal) is already represented
                    bool alreadyHasFinger = false;
                    foreach (var s in selected)
                    {
                        if (IsSameFinger(s, bone))
                        {
                            alreadyHasFinger = true;
                            break;
                        }
                    }

                    if (!alreadyHasFinger)
                    {
                        selected.Add(bone);
                        if (selected.Count >= numContacts) break;
                    }
                }
            }

            return selected;
        }

        private static bool IsSameFinger(HumanBodyBones a, HumanBodyBones b)
        {
            // Check if bones belong to same finger
            string aName = a.ToString();
            string bName = b.ToString();

            // Extract finger name (e.g., "Index", "Middle")
            string[] fingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (var finger in fingerNames)
            {
                if (aName.Contains(finger) && bName.Contains(finger))
                {
                    // Also check same hand
                    bool aLeft = aName.Contains("Left");
                    bool bLeft = bName.Contains("Left");
                    return aLeft == bLeft;
                }
            }
            return false;
        }
    }
}
