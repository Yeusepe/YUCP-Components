#if YUCP_INTERNAL_LG
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Ported concepts from Lightning Grasp's batch_grasp_optimizer.py.
    /// Implements zeroth-order optimization for grasp synthesis.
    /// 
    /// NOTE: This is a simplified C# port. Original uses PyTorch/CUDA batching.
    /// This version runs sequentially on CPU.
    /// </summary>
    public class BatchedGraspOptimizerPort
    {
        /// <summary>
        /// Optimization parameters.
        /// </summary>
        public struct OptimizerParams
        {
            public float learningRate;       // Zeroth-order step size
            public int maxIterations;        // Maximum optimization steps
            public float convergenceThresh;  // Score improvement threshold
            public int batchSize;            // Candidates per iteration
            public float sigma;              // Perturbation scale
        }

        public static OptimizerParams DefaultParams = new OptimizerParams
        {
            learningRate = 0.01f,
            maxIterations = 50,
            convergenceThresh = 0.001f,
            batchSize = 16,
            sigma = 0.1f
        };

        /// <summary>
        /// Contact point for wrench computation.
        /// </summary>
        public struct ContactPoint
        {
            public Vector3 position;
            public Vector3 normal;
            public float weight;
        }

        /// <summary>
        /// Grasp solution candidate.
        /// </summary>
        public struct GraspSolution
        {
            public Vector3 objectPosition;
            public Quaternion objectRotation;
            public List<ContactPoint> contacts;
            public float[] jointAngles;
            public float wrenchScore;
            public bool isValid;
        }

        private OptimizerParams parameters;
        private System.Random random;

        public BatchedGraspOptimizerPort(OptimizerParams? parameters = null)
        {
            this.parameters = parameters ?? DefaultParams;
            this.random = new System.Random();
        }

        /// <summary>
        /// Computes wrench score for a set of contact points.
        /// Ported from compute_wrench_score_vectorized().
        /// 
        /// Wrench score measures force-closure quality:
        /// - Lower score = better grasp stability
        /// - Considers contact positions, normals, and friction
        /// </summary>
        public float ComputeWrenchScore(
            List<ContactPoint> contacts,
            Vector3 objectCenter,
            float frictionCoeff = 0.5f)
        {
            if (contacts == null || contacts.Count < 2)
            {
                return float.MaxValue; // Invalid grasp
            }

            // Build wrench matrix [Fx, Fy, Fz, Tx, Ty, Tz]
            // Each contact contributes friction cone approximation
            int numCones = 4; // Friction cone discretization
            int numContacts = contacts.Count;
            int numWrenches = numContacts * numCones;

            var wrenchMatrix = new float[6, numWrenches];

            int col = 0;
            foreach (var contact in contacts)
            {
                Vector3 r = contact.position - objectCenter;
                Vector3 n = contact.normal;

                // Friction cone directions
                Vector3 tangent1 = Vector3.Cross(n, Vector3.up).normalized;
                if (tangent1.sqrMagnitude < 0.01f)
                {
                    tangent1 = Vector3.Cross(n, Vector3.right).normalized;
                }
                Vector3 tangent2 = Vector3.Cross(n, tangent1).normalized;

                for (int i = 0; i < numCones; i++)
                {
                    float angle = i * Mathf.PI * 2f / numCones;
                    Vector3 force = n + frictionCoeff * (Mathf.Cos(angle) * tangent1 + Mathf.Sin(angle) * tangent2);
                    force.Normalize();

                    Vector3 torque = Vector3.Cross(r, force);

                    wrenchMatrix[0, col] = force.x * contact.weight;
                    wrenchMatrix[1, col] = force.y * contact.weight;
                    wrenchMatrix[2, col] = force.z * contact.weight;
                    wrenchMatrix[3, col] = torque.x * contact.weight;
                    wrenchMatrix[4, col] = torque.y * contact.weight;
                    wrenchMatrix[5, col] = torque.z * contact.weight;

                    col++;
                }
            }

            // Solve NNLS: min ||Wx - b||^2 where x >= 0
            // Target wrench b is arbitrary direction (we want to resist all directions)
            // Use simplified scoring: minimum singular value of wrench matrix
            float score = ComputeWrenchScoreFromMatrix(wrenchMatrix, 6, numWrenches);

            return score;
        }

        /// <summary>
        /// Simplified NNLS-based wrench score computation.
        /// Ported from batched_nnls().
        /// </summary>
        private float ComputeWrenchScoreFromMatrix(float[,] W, int rows, int cols)
        {
            // Compute approximate condition/stability via power iteration
            // For full implementation, see original batched_nnls

            float maxNorm = 0f;
            float minNorm = float.MaxValue;

            // Compute column norms
            for (int c = 0; c < cols; c++)
            {
                float norm = 0f;
                for (int r = 0; r < rows; r++)
                {
                    norm += W[r, c] * W[r, c];
                }
                norm = Mathf.Sqrt(norm);

                maxNorm = Mathf.Max(maxNorm, norm);
                if (norm > 0.001f)
                {
                    minNorm = Mathf.Min(minNorm, norm);
                }
            }

            // Score is inverse of "coverage" - lower is better
            if (minNorm < 0.001f || maxNorm < 0.001f)
            {
                return float.MaxValue;
            }

            // Condition-like ratio
            float score = maxNorm / minNorm;
            return score;
        }

        /// <summary>
        /// Performs one optimization step using zeroth-order gradient.
        /// Ported from BatchedZerothOrderKinematicGraspOptimizer.optimize_step().
        /// </summary>
        public GraspSolution OptimizeStep(
            GraspSolution current,
            System.Func<GraspSolution, float> scoreFunc)
        {
            float currentScore = scoreFunc(current);
            GraspSolution best = current;
            best.wrenchScore = currentScore;

            // Generate perturbations
            for (int i = 0; i < parameters.batchSize; i++)
            {
                var candidate = PerturbSolution(current);
                float candidateScore = scoreFunc(candidate);

                if (candidateScore < best.wrenchScore)
                {
                    best = candidate;
                    best.wrenchScore = candidateScore;
                }
            }

            return best;
        }

        /// <summary>
        /// Runs full optimization loop.
        /// </summary>
        public GraspSolution Optimize(
            GraspSolution initial,
            System.Func<GraspSolution, float> scoreFunc)
        {
            var current = initial;
            current.wrenchScore = scoreFunc(current);
            float lastScore = current.wrenchScore;

            for (int iter = 0; iter < parameters.maxIterations; iter++)
            {
                current = OptimizeStep(current, scoreFunc);

                // Check convergence
                float improvement = lastScore - current.wrenchScore;
                if (improvement < parameters.convergenceThresh && improvement >= 0)
                {
                    break;
                }

                lastScore = current.wrenchScore;
            }

            return current;
        }

        private GraspSolution PerturbSolution(GraspSolution solution)
        {
            var perturbed = solution;

            // Perturb object pose
            perturbed.objectPosition += RandomVector3() * parameters.sigma * 0.01f;
            perturbed.objectRotation *= RandomRotation(parameters.sigma * 5f);

            // Perturb contact points
            if (perturbed.contacts != null)
            {
                var newContacts = new List<ContactPoint>();
                foreach (var c in perturbed.contacts)
                {
                    var nc = c;
                    nc.position += RandomVector3() * parameters.sigma * 0.005f;
                    newContacts.Add(nc);
                }
                perturbed.contacts = newContacts;
            }

            return perturbed;
        }

        private Vector3 RandomVector3()
        {
            return new Vector3(
                (float)(random.NextDouble() * 2 - 1),
                (float)(random.NextDouble() * 2 - 1),
                (float)(random.NextDouble() * 2 - 1)
            );
        }

        private Quaternion RandomRotation(float maxDegrees)
        {
            Vector3 axis = RandomVector3().normalized;
            float angle = (float)(random.NextDouble() * 2 - 1) * maxDegrees;
            return Quaternion.AngleAxis(angle, axis);
        }
    }
}
#endif
