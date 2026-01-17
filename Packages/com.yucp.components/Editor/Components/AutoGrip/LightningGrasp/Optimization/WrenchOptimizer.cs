using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Zeroth-order optimizer for contact point positions.
    /// Optimizes contact positions on object surface to minimize wrench score (force closure quality).
    /// Based on Lightning Grasp's batch_grasp_optimizer.py.
    /// </summary>
    public class WrenchOptimizer
    {
        /// <summary>
        /// Configuration for wrench optimization.
        /// </summary>
        public class Config
        {
            public int totalSteps = 20;
            public int variantsPerStep = 10;
            public float stepSize = 0.005f;
            public int nnlsIterations = 15;
            public float nnlsLearningRate = 0.15f;
            public float wrenchAlpha = 10f;  // Weight for torque component
        }

        private Config config;
        private ComputeShader nnlsShader;

        // GPU buffers
        private ComputeBuffer contactPosBuffer;
        private ComputeBuffer contactNormalBuffer;
        private ComputeBuffer wrenchScoreBuffer;

        public WrenchOptimizer(Config config = null, ComputeShader shader = null)
        {
            this.config = config ?? new Config();
            this.nnlsShader = shader;
        }

        /// <summary>
        /// Optimize contact positions for a batch of grasp candidates.
        /// </summary>
        public OptimizationResult Optimize(
            Vector3[] initialContactPositions,  // [batchSize * numContacts]
            Vector3[] initialContactNormals,    // [batchSize * numContacts]
            Vector3[] objectPoints,
            Vector3[] objectNormals,
            int batchSize,
            int numContacts)
        {
            var result = new OptimizationResult
            {
                contactPositions = (Vector3[])initialContactPositions.Clone(),
                contactNormals = (Vector3[])initialContactNormals.Clone(),
                scores = new float[batchSize]
            };

            // Compute initial scores
            ComputeWrenchScoresCPU(result.contactPositions, result.contactNormals, 
                                   batchSize, numContacts, result.scores);

            // Zeroth-order optimization loop
            for (int step = 0; step < config.totalSteps; step++)
            {
                // Optimize each contact point in turn
                for (int contactIdx = 0; contactIdx < numContacts; contactIdx++)
                {
                    OptimizeContactPoint(
                        result.contactPositions,
                        result.contactNormals,
                        result.scores,
                        objectPoints,
                        objectNormals,
                        batchSize,
                        numContacts,
                        contactIdx);
                }
            }

            return result;
        }

        private void OptimizeContactPoint(
            Vector3[] positions,
            Vector3[] normals,
            float[] scores,
            Vector3[] objectPoints,
            Vector3[] objectNormals,
            int batchSize,
            int numContacts,
            int pivotIdx)
        {
            // For each batch instance, try random perturbations
            for (int b = 0; b < batchSize; b++)
            {
                int baseIdx = b * numContacts + pivotIdx;
                Vector3 originalPos = positions[baseIdx];
                Vector3 originalNormal = normals[baseIdx];
                float bestScore = scores[b];

                // Get tangent plane at current position
                GetTangentPlane(originalNormal, out Vector3 tangentX, out Vector3 tangentY);

                for (int v = 0; v < config.variantsPerStep; v++)
                {
                    // Random perturbation in tangent plane
                    float dx = Random.Range(-1f, 1f) * config.stepSize;
                    float dy = Random.Range(-1f, 1f) * config.stepSize;
                    Vector3 newPos = originalPos + tangentX * dx + tangentY * dy;

                    // Project to nearest object point
                    int nearestIdx = FindNearestPoint(newPos, objectPoints);
                    Vector3 projectedPos = objectPoints[nearestIdx];
                    Vector3 projectedNormal = objectNormals[nearestIdx];

                    // Temporarily update
                    positions[baseIdx] = projectedPos;
                    normals[baseIdx] = projectedNormal;

                    // Compute score
                    float newScore = ComputeSingleWrenchScore(positions, normals, b, numContacts);

                    if (newScore < bestScore)
                    {
                        bestScore = newScore;
                        originalPos = projectedPos;
                        originalNormal = projectedNormal;
                    }
                    else
                    {
                        // Revert
                        positions[baseIdx] = originalPos;
                        normals[baseIdx] = originalNormal;
                    }
                }

                scores[b] = bestScore;
            }
        }

        private void GetTangentPlane(Vector3 normal, out Vector3 x, out Vector3 y)
        {
            // Compute orthonormal tangent vectors
            if (Mathf.Abs(normal.x) < 0.9f)
                x = Vector3.Cross(normal, Vector3.right).normalized;
            else
                x = Vector3.Cross(normal, Vector3.up).normalized;
            y = Vector3.Cross(normal, x).normalized;
        }

        private int FindNearestPoint(Vector3 pos, Vector3[] points)
        {
            int nearest = 0;
            float minDist = float.MaxValue;

            for (int i = 0; i < points.Length; i++)
            {
                float dist = Vector3.SqrMagnitude(pos - points[i]);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Compute wrench scores for all batches (CPU version).
        /// </summary>
        public void ComputeWrenchScoresCPU(
            Vector3[] positions,
            Vector3[] normals,
            int batchSize,
            int numContacts,
            float[] outScores)
        {
            for (int b = 0; b < batchSize; b++)
            {
                outScores[b] = ComputeSingleWrenchScore(positions, normals, b, numContacts);
            }
        }

        /// <summary>
        /// Compute wrench score for a single batch instance.
        /// </summary>
        public float ComputeSingleWrenchScore(
            Vector3[] positions,
            Vector3[] normals,
            int batchIdx,
            int numContacts)
        {
            int K = numContacts;
            float bestScore = float.MaxValue;

            // Try each contact as pivot
            for (int pivot = 0; pivot < K; pivot++)
            {
                int pivotIdx = batchIdx * K + pivot;
                Vector3 pivotPos = positions[pivotIdx];
                Vector3 pivotNormal = normals[pivotIdx];

                // Build NNLS matrices
                // A is [6, K-1], b is [6]
                int n = K - 1;
                if (n <= 0) continue;

                var A = new float[6, n];
                var b = new float[6];

                // Force component: b_force = -n0
                b[0] = -pivotNormal.x;
                b[1] = -pivotNormal.y;
                b[2] = -pivotNormal.z;
                // Torque component: b_torque = 0
                b[3] = 0;
                b[4] = 0;
                b[5] = 0;

                int col = 0;
                for (int i = 0; i < K; i++)
                {
                    if (i == pivot) continue;

                    int idx = batchIdx * K + i;
                    Vector3 ni = normals[idx];
                    Vector3 ri = positions[idx] - pivotPos;
                    Vector3 wi = Vector3.Cross(ri, ni) * config.wrenchAlpha;

                    A[0, col] = ni.x;
                    A[1, col] = ni.y;
                    A[2, col] = ni.z;
                    A[3, col] = wi.x;
                    A[4, col] = wi.y;
                    A[5, col] = wi.z;

                    col++;
                }

                // Solve NNLS
                float score = SolveNNLSAndGetResidual(A, b, n);
                bestScore = Mathf.Min(bestScore, score);
            }

            return bestScore;
        }

        /// <summary>
        /// Solve NNLS using projected gradient descent and return residual.
        /// </summary>
        private float SolveNNLSAndGetResidual(float[,] A, float[] b, int n)
        {
            int m = 6;
            var x = new float[n];

            // Precompute AtA and Atb
            var AtA = new float[n, n];
            var Atb = new float[n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    float sum = 0;
                    for (int k = 0; k < m; k++)
                    {
                        sum += A[k, i] * A[k, j];
                    }
                    AtA[i, j] = sum;
                }

                float dot = 0;
                for (int k = 0; k < m; k++)
                {
                    dot += A[k, i] * b[k];
                }
                Atb[i] = dot;
            }

            // Gradient descent
            for (int iter = 0; iter < config.nnlsIterations; iter++)
            {
                for (int i = 0; i < n; i++)
                {
                    float grad = -Atb[i];
                    for (int j = 0; j < n; j++)
                    {
                        grad += AtA[i, j] * x[j];
                    }
                    x[i] = Mathf.Max(0, x[i] - config.nnlsLearningRate * grad);
                }
            }

            // Compute residual |Ax - b|
            float residualSum = 0;
            for (int row = 0; row < m; row++)
            {
                float Ax = 0;
                for (int col = 0; col < n; col++)
                {
                    Ax += A[row, col] * x[col];
                }
                float diff = Ax - b[row];
                residualSum += diff * diff;
            }

            return Mathf.Sqrt(residualSum);
        }

        public void ReleaseBuffers()
        {
            contactPosBuffer?.Release();
            contactNormalBuffer?.Release();
            wrenchScoreBuffer?.Release();

            contactPosBuffer = null;
            contactNormalBuffer = null;
            wrenchScoreBuffer = null;
        }
    }

    /// <summary>
    /// Result of wrench optimization.
    /// </summary>
    public class OptimizationResult
    {
        public Vector3[] contactPositions;
        public Vector3[] contactNormals;
        public int[] contactPointIndices;
        public float[] scores;
    }
}
