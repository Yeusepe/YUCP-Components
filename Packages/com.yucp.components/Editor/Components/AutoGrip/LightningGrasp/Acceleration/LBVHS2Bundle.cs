using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Linear Bounding Volume Hierarchy for sphere bundles (LBVH-S2).
    /// Accelerates spatial queries for contact field traversal.
    /// Based on Lightning Grasp's lbvh_s2bundle.py.
    /// </summary>
    public class LBVHS2Bundle
    {
        /// <summary>
        /// BVH node structure.
        /// </summary>
        public struct BVHNode
        {
            public Vector3 boundsMin;
            public Vector3 boundsMax;
            public int leftChild;   // -1 for leaf
            public int rightChild;  // -1 for leaf
            public int patchId;     // Only valid for leaves
            public int firstSample; // Start index in sample arrays
            public int sampleCount; // Number of samples in this node

            public bool IsLeaf => leftChild < 0 && rightChild < 0;
        }

        private List<BVHNode> nodes = new List<BVHNode>();
        private Vector4[] samplePositions;
        private Vector4[] sampleNormals;
        private int[] samplePatchIds;
        private float[] angleRanges;

        // GPU buffers
        private ComputeBuffer nodeBuffer;
        private ComputeBuffer positionBuffer;
        private ComputeBuffer normalBuffer;
        private ComputeBuffer patchIdBuffer;
        private ComputeBuffer angleRangeBuffer;

        /// <summary>
        /// Build BVH from contact field samples.
        /// </summary>
        public void Build(ContactFieldSamples samples, ContactField field)
        {
            // Pack samples
            samples.PackForGPU(out samplePositions, out sampleNormals, out var patchOffsets);

            int totalSamples = samplePositions.Length;
            samplePatchIds = new int[totalSamples];

            // Fill patch IDs
            for (int p = 0; p < samples.patchSamples.Count; p++)
            {
                int start = patchOffsets[p];
                int end = patchOffsets[p + 1];
                for (int i = start; i < end; i++)
                {
                    samplePatchIds[i] = p;
                }
            }

            // Store angle ranges
            angleRanges = new float[field.patches.Count];
            for (int p = 0; p < field.patches.Count; p++)
            {
                angleRanges[p] = field.patches[p].angleRange;
            }

            // Build tree recursively
            nodes.Clear();
            int rootIdx = BuildNode(0, totalSamples);

            Debug.Log($"[LBVHS2Bundle] Built BVH with {nodes.Count} nodes for {totalSamples} samples");
        }

        private int BuildNode(int start, int count)
        {
            if (count == 0) return -1;

            // Compute bounds
            Vector3 boundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 boundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = start; i < start + count; i++)
            {
                Vector3 pos = new Vector3(samplePositions[i].x, samplePositions[i].y, samplePositions[i].z);
                boundsMin = Vector3.Min(boundsMin, pos);
                boundsMax = Vector3.Max(boundsMax, pos);
            }

            // Leaf condition: small count or all same patch
            bool isLeaf = count <= 16 || IsSamePatch(start, count);

            if (isLeaf)
            {
                var node = new BVHNode
                {
                    boundsMin = boundsMin,
                    boundsMax = boundsMax,
                    leftChild = -1,
                    rightChild = -1,
                    patchId = samplePatchIds[start],
                    firstSample = start,
                    sampleCount = count
                };

                int idx = nodes.Count;
                nodes.Add(node);
                return idx;
            }

            // Find split axis (longest extent)
            Vector3 extent = boundsMax - boundsMin;
            int splitAxis = 0;
            if (extent.y > extent.x) splitAxis = 1;
            if (extent.z > extent[splitAxis]) splitAxis = 2;

            // Sort samples by split axis (simple median split)
            float splitValue = (boundsMin[splitAxis] + boundsMax[splitAxis]) * 0.5f;
            int splitIdx = PartitionSamples(start, count, splitAxis, splitValue);

            // Ensure we don't have empty children
            if (splitIdx == start) splitIdx = start + count / 2;
            if (splitIdx == start + count) splitIdx = start + count / 2;

            // Create internal node
            var internalNode = new BVHNode
            {
                boundsMin = boundsMin,
                boundsMax = boundsMax,
                leftChild = -1,  // Will be set after recursion
                rightChild = -1,
                patchId = -1,
                firstSample = start,
                sampleCount = count
            };

            int nodeIdx = nodes.Count;
            nodes.Add(internalNode);

            // Recurse
            int leftCount = splitIdx - start;
            int rightCount = count - leftCount;

            var leftChild = BuildNode(start, leftCount);
            var rightChild = BuildNode(splitIdx, rightCount);

            // Update children
            var updatedNode = nodes[nodeIdx];
            updatedNode.leftChild = leftChild;
            updatedNode.rightChild = rightChild;
            nodes[nodeIdx] = updatedNode;

            return nodeIdx;
        }

        private bool IsSamePatch(int start, int count)
        {
            int firstPatch = samplePatchIds[start];
            for (int i = start + 1; i < start + count; i++)
            {
                if (samplePatchIds[i] != firstPatch) return false;
            }
            return true;
        }

        private int PartitionSamples(int start, int count, int axis, float splitValue)
        {
            int left = start;
            int right = start + count - 1;

            while (left <= right)
            {
                float leftVal = axis == 0 ? samplePositions[left].x :
                               axis == 1 ? samplePositions[left].y : samplePositions[left].z;

                if (leftVal < splitValue)
                {
                    left++;
                }
                else
                {
                    // Swap with right
                    SwapSamples(left, right);
                    right--;
                }
            }

            return left;
        }

        private void SwapSamples(int a, int b)
        {
            if (a == b) return;

            var tmpPos = samplePositions[a];
            samplePositions[a] = samplePositions[b];
            samplePositions[b] = tmpPos;

            var tmpNorm = sampleNormals[a];
            sampleNormals[a] = sampleNormals[b];
            sampleNormals[b] = tmpNorm;

            var tmpPatch = samplePatchIds[a];
            samplePatchIds[a] = samplePatchIds[b];
            samplePatchIds[b] = tmpPatch;
        }

        /// <summary>
        /// Upload data to GPU buffers.
        /// </summary>
        public void UploadToGPU()
        {
            ReleaseGPUBuffers();

            if (nodes.Count == 0) return;

            // Pack nodes
            int nodeStride = 12 * sizeof(float); // 12 floats per node
            nodeBuffer = new ComputeBuffer(nodes.Count, nodeStride);
            var nodeData = new float[nodes.Count * 12];
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                int offset = i * 12;
                nodeData[offset + 0] = node.boundsMin.x;
                nodeData[offset + 1] = node.boundsMin.y;
                nodeData[offset + 2] = node.boundsMin.z;
                nodeData[offset + 3] = node.boundsMax.x;
                nodeData[offset + 4] = node.boundsMax.y;
                nodeData[offset + 5] = node.boundsMax.z;
                nodeData[offset + 6] = node.leftChild;
                nodeData[offset + 7] = node.rightChild;
                nodeData[offset + 8] = node.patchId;
                nodeData[offset + 9] = node.firstSample;
                nodeData[offset + 10] = node.sampleCount;
                nodeData[offset + 11] = 0; // padding
            }
            nodeBuffer.SetData(nodeData);

            // Upload samples
            positionBuffer = new ComputeBuffer(samplePositions.Length, 4 * sizeof(float));
            positionBuffer.SetData(samplePositions);

            normalBuffer = new ComputeBuffer(sampleNormals.Length, 4 * sizeof(float));
            normalBuffer.SetData(sampleNormals);

            patchIdBuffer = new ComputeBuffer(samplePatchIds.Length, sizeof(int));
            patchIdBuffer.SetData(samplePatchIds);

            if (angleRanges.Length > 0)
            {
                angleRangeBuffer = new ComputeBuffer(angleRanges.Length, sizeof(float));
                angleRangeBuffer.SetData(angleRanges);
            }
        }

        /// <summary>
        /// Bind buffers to a compute shader.
        /// </summary>
        public void BindToShader(ComputeShader shader, int kernel)
        {
            if (nodeBuffer != null) shader.SetBuffer(kernel, "_BVHNodes", nodeBuffer);
            if (positionBuffer != null) shader.SetBuffer(kernel, "_SamplePositions", positionBuffer);
            if (normalBuffer != null) shader.SetBuffer(kernel, "_SampleNormals", normalBuffer);
            if (patchIdBuffer != null) shader.SetBuffer(kernel, "_SamplePatchIds", patchIdBuffer);
            if (angleRangeBuffer != null) shader.SetBuffer(kernel, "_AngleRanges", angleRangeBuffer);

            shader.SetInt("_NumNodes", nodes.Count);
            shader.SetInt("_NumSamples", samplePositions?.Length ?? 0);
        }

        /// <summary>
        /// CPU-side query: find all patches that can reach a point.
        /// </summary>
        public List<int> QueryPoint(Vector3 point, Vector3 objectNormal, float maxDistance = 0.05f)
        {
            var result = new List<int>();
            QueryPointRecursive(0, point, objectNormal, maxDistance, result);
            return result;
        }

        private void QueryPointRecursive(int nodeIdx, Vector3 point, Vector3 objectNormal, float maxDist, List<int> result)
        {
            if (nodeIdx < 0 || nodeIdx >= nodes.Count) return;

            var node = nodes[nodeIdx];

            // AABB test
            Vector3 closest = new Vector3(
                Mathf.Clamp(point.x, node.boundsMin.x, node.boundsMax.x),
                Mathf.Clamp(point.y, node.boundsMin.y, node.boundsMax.y),
                Mathf.Clamp(point.z, node.boundsMin.z, node.boundsMax.z));

            if (Vector3.Distance(point, closest) > maxDist) return;

            if (node.IsLeaf)
            {
                // Check samples in leaf
                for (int i = node.firstSample; i < node.firstSample + node.sampleCount; i++)
                {
                    Vector3 samplePos = new Vector3(samplePositions[i].x, samplePositions[i].y, samplePositions[i].z);
                    Vector3 sampleNormal = new Vector3(sampleNormals[i].x, sampleNormals[i].y, sampleNormals[i].z);

                    // Distance check
                    if (Vector3.Distance(samplePos, point) > maxDist) continue;

                    // Normal alignment check
                    int patchId = samplePatchIds[i];
                    float angleRange = patchId < angleRanges.Length ? angleRanges[patchId] : Mathf.PI * 0.5f;
                    float dotProduct = Vector3.Dot(sampleNormal, -objectNormal);
                    if (dotProduct < Mathf.Cos(angleRange)) continue;

                    if (!result.Contains(patchId))
                    {
                        result.Add(patchId);
                    }
                }
            }
            else
            {
                QueryPointRecursive(node.leftChild, point, objectNormal, maxDist, result);
                QueryPointRecursive(node.rightChild, point, objectNormal, maxDist, result);
            }
        }

        /// <summary>
        /// Release GPU resources.
        /// </summary>
        public void ReleaseGPUBuffers()
        {
            nodeBuffer?.Release();
            positionBuffer?.Release();
            normalBuffer?.Release();
            patchIdBuffer?.Release();
            angleRangeBuffer?.Release();

            nodeBuffer = null;
            positionBuffer = null;
            normalBuffer = null;
            patchIdBuffer = null;
            angleRangeBuffer = null;
        }

        public int NodeCount => nodes.Count;
        public int SampleCount => samplePositions?.Length ?? 0;
    }
}
