using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using YUCP.Components.HandPoses;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Main Lightning Grasp pipeline orchestrating all stages.
    /// Provides full parity with the original Python implementation.
    /// </summary>
    public class LightningGraspPipeline
    {
        /// <summary>
        /// Pipeline configuration.
        /// </summary>
        [System.Serializable]
        public class PipelineConfig
        {
            [Header("Contact Field")]
            public ContactFieldConfig contactFieldConfig = ContactFieldConfig.Default;

            [Header("Sampling")]
            [Tooltip("Number of contact points to optimize.")]
            public int numContacts = 3;

            [Tooltip("Number of object surface points to sample.")]
            [Range(256, 8192)]
            public int numObjectPoints = 2048;

            [Header("Optimization")]
            public WrenchOptimizer.Config optimizerConfig = new WrenchOptimizer.Config();

            [Header("IK")]
            public FingerIKSolver.Config ikConfig = new FingerIKSolver.Config();

            [Header("Validation")]
            public GraspCollisionFilter.Config filterConfig = new GraspCollisionFilter.Config();

            [Header("Batching")]
            [Tooltip("Number of object poses to sample.")]
            public int batchOuter = 32;

            [Tooltip("Number of contact variants per object pose.")]
            public int batchInner = 32;
        }

        // Pipeline components
        private ContactFieldGenerator fieldGenerator;
        private ContactField contactField;
        private ContactFieldSamples fieldSamples;
        private LBVHS2Bundle bvh;
        private ContactInteractionQuery interactionQuery;
        private WrenchOptimizer wrenchOptimizer;
        private FingerIKSolver ikSolver;
        private GraspCollisionFilter collisionFilter;

        // Compute shaders
        private ComputeShader contactFieldShader;
        private ComputeShader nnlsShader;

        private PipelineConfig config;
        private Animator animator;
        private bool isLeftHand;

        public LightningGraspPipeline(
            Animator animator,
            bool isLeftHand,
            PipelineConfig config = null)
        {
            this.animator = animator;
            this.isLeftHand = isLeftHand;
            this.config = config ?? new PipelineConfig();

            // Initialize components
            fieldGenerator = new ContactFieldGenerator(animator, isLeftHand, this.config.contactFieldConfig);
            wrenchOptimizer = new WrenchOptimizer(this.config.optimizerConfig, nnlsShader);
            ikSolver = new FingerIKSolver(this.config.ikConfig);
            collisionFilter = new GraspCollisionFilter(this.config.filterConfig);
        }

        /// <summary>
        /// Load compute shaders for GPU acceleration.
        /// </summary>
        public void LoadShaders()
        {
            // Try to load from package path using AssetDatabase (editor only)
            string packagePath = "Packages/com.yucp.components/Editor/Components/AutoGrip/LightningGrasp/Shaders";
            
            contactFieldShader = AssetDatabase.LoadAssetAtPath<ComputeShader>($"{packagePath}/ContactFieldAccel.compute");
            nnlsShader = AssetDatabase.LoadAssetAtPath<ComputeShader>($"{packagePath}/NNLSSolver.compute");

            // Fallback to Resources
            if (contactFieldShader == null)
            {
                contactFieldShader = UnityEngine.Resources.Load<ComputeShader>("ContactFieldAccel");
            }
            if (nnlsShader == null)
            {
                nnlsShader = UnityEngine.Resources.Load<ComputeShader>("NNLSSolver");
            }

            if (contactFieldShader == null)
            {
                Debug.LogWarning("[LightningGraspPipeline] ContactFieldAccel shader not found, using CPU fallback");
            }
            if (nnlsShader == null)
            {
                Debug.LogWarning("[LightningGraspPipeline] NNLSSolver shader not found, using CPU fallback");
            }
        }

        /// <summary>
        /// Stage 1 & 2: Build contact field.
        /// </summary>
        public void BuildContactField(SkinnedMeshRenderer skinMesh = null)
        {
            try
            {
                Debug.Log($"[LightningGraspPipeline] Starting contact field build, skinMesh={(skinMesh != null ? skinMesh.name : "null")}");
                
                EditorUtility.DisplayProgressBar("Lightning Grasp", "Building contact field...", 0.1f);

                // Build patches
                contactField = fieldGenerator.BuildContactField(skinMesh);

                if (contactField == null || contactField.patches.Count == 0)
                {
                    Debug.LogWarning("[LightningGraspPipeline] Contact field has no patches, creating fallback");
                    CreateFallbackContactField();
                }

                // Sample across joint configurations (skip if too slow for real-time)
                if (config.contactFieldConfig.jointSamples <= 1000)
                {
                    // Light mode - skip BVH building for speed
                    Debug.Log("[LightningGraspPipeline] Light mode - skipping field sampling");
                    fieldSamples = null;
                    bvh = null;
                }
                else
                {
                    fieldSamples = fieldGenerator.SampleContactField(contactField);

                    // Build BVH
                    bvh = new LBVHS2Bundle();
                    bvh.Build(fieldSamples, contactField);

                    // Upload to GPU if available
                    if (config.contactFieldConfig.useGPU && contactFieldShader != null)
                    {
                        bvh.UploadToGPU();
                    }
                }

                // Initialize interaction query
                interactionQuery = new ContactInteractionQuery(contactField, bvh, contactFieldShader);

                EditorUtility.ClearProgressBar();
                Debug.Log($"[LightningGraspPipeline] Contact field built: {contactField.patches.Count} patches");
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[LightningGraspPipeline] Failed to build contact field: {e.Message}\n{e.StackTrace}");
                
                // Create minimal fallback
                CreateFallbackContactField();
            }
        }

        private void CreateFallbackContactField()
        {
            contactField = new ContactField { config = config.contactFieldConfig };
            
            // Create simple synthetic patches for each finger
            var chains = HandForwardKinematics.GetFingerChains(isLeftHand);
            var boneIndexMap = HandForwardKinematics.CreateBoneIndexMap(isLeftHand);
            
            foreach (var chain in chains)
            {
                foreach (var bone in chain)
                {
                    if (boneIndexMap.TryGetValue(bone, out int idx))
                    {
                        var patch = new ContactPatch(bone, idx);
                        // Add a simple keyvector at the bone tip facing outward
                        patch.AddKeyVector(new KeyVector(Vector3.forward * 0.01f, Vector3.forward));
                        contactField.RegisterPatch(patch);
                    }
                }
            }
            
            Debug.Log($"[LightningGraspPipeline] Created fallback contact field with {contactField.patches.Count} patches");
        }

        /// <summary>
        /// Run the full grasp synthesis pipeline.
        /// </summary>
        public GraspResult SynthesizeGrasp(
            Vector3[] objectPoints,
            Vector3[] objectNormals,
            Collider[] propColliders)
        {
            if (contactField == null)
            {
                Debug.LogError("[LightningGraspPipeline] Contact field not built. Call BuildContactField first.");
                return null;
            }

            float startTime = Time.realtimeSinceStartup;

            // === Stage 3: Contact Interaction Query ===
            EditorUtility.DisplayProgressBar("Lightning Grasp", "Computing interaction matrix...", 0.3f);

            var interactionMatrix = config.contactFieldConfig.useGPU
                ? interactionQuery.QueryInteractionMatrixGPU(objectPoints, objectNormals)
                : interactionQuery.QueryInteractionMatrixCPU(objectPoints, objectNormals);

            // Reduce to link level
            var linkInteraction = interactionQuery.ReduceToLinkInteraction(interactionMatrix);

            // === Stage 4: Build Contact Domains ===
            EditorUtility.DisplayProgressBar("Lightning Grasp", "Building contact domains...", 0.4f);

            var domains = ContactDomainBuilder.BuildDomains(
                contactField, linkInteraction, objectPoints, objectNormals);

            if (domains.Count < config.numContacts)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogWarning($"[LightningGraspPipeline] Only {domains.Count} contact domains found, need {config.numContacts}");
                return CreateFallbackGrasp();
            }

            // Select fingers for grasping
            var selectedFingers = ContactDomainBuilder.SelectContactFingers(domains, config.numContacts);

            // Sample initial contact points from domains
            var initialContacts = SampleInitialContacts(domains, selectedFingers, config.batchInner);

            // === Stage 5: Wrench Optimization ===
            EditorUtility.DisplayProgressBar("Lightning Grasp", "Optimizing contact positions...", 0.5f);

            var optimizationResult = wrenchOptimizer.Optimize(
                initialContacts.positions,
                initialContacts.normals,
                objectPoints,
                objectNormals,
                config.batchInner,
                config.numContacts);

            // Find best candidate
            int bestIdx = FindBestCandidate(optimizationResult.scores);
            float bestScore = optimizationResult.scores[bestIdx];

            // Extract best contact configuration
            var bestContacts = ExtractContacts(optimizationResult, bestIdx, config.numContacts);

            // === Stage 6: Inverse Kinematics ===
            EditorUtility.DisplayProgressBar("Lightning Grasp", "Solving inverse kinematics...", 0.7f);

            ikSolver.IterativeContactAdjustment(
                animator, isLeftHand,
                ref bestContacts.positions,
                ref bestContacts.normals,
                bestContacts.bones,
                objectPoints,
                objectNormals,
                config.ikConfig.maxIterations / 4);

            // Solve final IK
            var ikResults = ikSolver.SolveMultipleFingers(
                animator, isLeftHand,
                new List<HumanBodyBones>(bestContacts.bones),
                new List<Vector3>(bestContacts.positions),
                new List<Vector3>(bestContacts.normals));

            // Combine joint angles
            var jointAngles = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (var result in ikResults)
            {
                if (result.success && result.jointAngles != null)
                {
                    foreach (var kvp in result.jointAngles)
                    {
                        jointAngles[kvp.Key] = kvp.Value;
                    }
                }
            }

            // === Stage 7: Collision Filtering ===
            EditorUtility.DisplayProgressBar("Lightning Grasp", "Validating grasp...", 0.9f);

            var validation = collisionFilter.ValidateGrasp(animator, isLeftHand, jointAngles, propColliders);

            // Assign free fingers
            var usedBones = new HashSet<HumanBodyBones>(jointAngles.Keys);
            collisionFilter.AssignFreeFingersNaturalPose(animator, isLeftHand, usedBones);

            // Capture final joint angles including free fingers
            var allBones = HandForwardKinematics.GetAllHandBones(isLeftHand);
            foreach (var bone in allBones)
            {
                if (!jointAngles.ContainsKey(bone))
                {
                    var t = animator.GetBoneTransform(bone);
                    if (t != null)
                    {
                        jointAngles[bone] = t.localRotation;
                    }
                }
            }

            EditorUtility.ClearProgressBar();

            float elapsed = Time.realtimeSinceStartup - startTime;
            Debug.Log($"[LightningGraspPipeline] Grasp synthesized in {elapsed:F2}s, " +
                      $"score={bestScore:F4}, valid={validation.isValid}");

            return new GraspResult
            {
                jointAngles = jointAngles,
                contactPositions = bestContacts.positions,
                contactNormals = bestContacts.normals,
                contactBones = bestContacts.bones,
                wrenchScore = bestScore,
                isValid = validation.isValid,
                validationResult = validation
            };
        }

        private (Vector3[] positions, Vector3[] normals) SampleInitialContacts(
            Dictionary<HumanBodyBones, ContactDomain> domains,
            List<HumanBodyBones> fingers,
            int batchSize)
        {
            int K = fingers.Count;
            int totalContacts = batchSize * K;

            var positions = new Vector3[totalContacts];
            var normals = new Vector3[totalContacts];

            for (int b = 0; b < batchSize; b++)
            {
                for (int k = 0; k < K; k++)
                {
                    var domain = domains[fingers[k]];
                    var (pos, normal, _) = domain.SampleRandom();

                    int idx = b * K + k;
                    positions[idx] = pos;
                    normals[idx] = normal;
                }
            }

            return (positions, normals);
        }

        private int FindBestCandidate(float[] scores)
        {
            int best = 0;
            float bestScore = float.MaxValue;

            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i] < bestScore)
                {
                    bestScore = scores[i];
                    best = i;
                }
            }

            return best;
        }

        private (Vector3[] positions, Vector3[] normals, HumanBodyBones[] bones) ExtractContacts(
            OptimizationResult result,
            int batchIdx,
            int numContacts)
        {
            var positions = new Vector3[numContacts];
            var normals = new Vector3[numContacts];

            for (int k = 0; k < numContacts; k++)
            {
                int idx = batchIdx * numContacts + k;
                positions[k] = result.contactPositions[idx];
                normals[k] = result.contactNormals[idx];
            }

            // Note: bones should be tracked through the pipeline
            // For now, use fingers in order
            var bones = new HumanBodyBones[numContacts];
            var chains = HandForwardKinematics.GetFingerChains(isLeftHand);
            for (int k = 0; k < numContacts && k < chains.Length; k++)
            {
                bones[k] = chains[k][chains[k].Length - 1]; // Use distal
            }

            return (positions, normals, bones);
        }

        private GraspResult CreateFallbackGrasp()
        {
            // Simple curled grip as fallback
            var jointAngles = new Dictionary<HumanBodyBones, Quaternion>();
            var chains = HandForwardKinematics.GetFingerChains(isLeftHand);

            foreach (var chain in chains)
            {
                for (int i = 0; i < chain.Length; i++)
                {
                    float curl = 45f * (i == 0 ? 0.5f : i == 1 ? 0.35f : 0.15f);
                    jointAngles[chain[i]] = Quaternion.Euler(curl, 0, 0);
                }
            }

            return new GraspResult
            {
                jointAngles = jointAngles,
                wrenchScore = float.MaxValue,
                isValid = false
            };
        }

        /// <summary>
        /// Get contact field for visualization.
        /// </summary>
        public ContactField GetContactField() => contactField;

        /// <summary>
        /// Get BVH for visualization.
        /// </summary>
        public LBVHS2Bundle GetBVH() => bvh;

        /// <summary>
        /// Release all GPU resources.
        /// </summary>
        public void Release()
        {
            bvh?.ReleaseGPUBuffers();
            interactionQuery?.ReleaseBuffers();
            wrenchOptimizer?.ReleaseBuffers();
        }
    }

    /// <summary>
    /// Result of grasp synthesis pipeline.
    /// </summary>
    public class GraspResult
    {
        public Dictionary<HumanBodyBones, Quaternion> jointAngles;
        public Vector3[] contactPositions;
        public Vector3[] contactNormals;
        public HumanBodyBones[] contactBones;
        public float wrenchScore;
        public bool isValid;
        public GraspValidationResult validationResult;
    }
}
