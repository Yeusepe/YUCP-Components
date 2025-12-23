using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Runtime component that monitors blendshape weights and applies transform deltas.
    /// This is automatically added during build to make objects follow blendshape animations.
    /// </summary>
    public class AttachToBlendshapeRuntime : MonoBehaviour
    {
        [Serializable]
        public class BlendshapeTransformData
        {
            public string blendshapeName;
            public TransformSample[] samples;
        }

        [Serializable]
        public class TransformSample
        {
            public float blendshapeWeight; // 0-100
            public Vector3 positionDelta;
            public Vector4 rotationDelta; // x, y, z, w (quaternion)
            
            public Quaternion GetRotation()
            {
                return new Quaternion(rotationDelta.x, rotationDelta.y, rotationDelta.z, rotationDelta.w);
            }
        }

        [SerializeField] private SkinnedMeshRenderer sourceMesh;
        [SerializeField] private List<BlendshapeTransformData> blendshapeData = new List<BlendshapeTransformData>();
        [SerializeField] private Vector3 baseLocalPosition;
        [SerializeField] private Quaternion baseLocalRotation;

        private Dictionary<string, BlendshapeTransformData> blendshapeLookup;
        private Dictionary<string, float> lastWeights = new Dictionary<string, float>();

        private void Awake()
        {
            Debug.Log($"[AttachToBlendshapeRuntime] Awake called on {gameObject.name}. Source mesh: {(sourceMesh != null ? sourceMesh.name : "NULL")}, Blendshape data count: {blendshapeData.Count}", this);

            if (sourceMesh == null)
            {
                Debug.LogError("[AttachToBlendshapeRuntime] Source mesh is null", this);
                enabled = false;
                return;
            }

            if (sourceMesh.sharedMesh == null)
            {
                Debug.LogError("[AttachToBlendshapeRuntime] Source mesh sharedMesh is null", this);
                enabled = false;
                return;
            }

            // Build lookup dictionary
            blendshapeLookup = new Dictionary<string, BlendshapeTransformData>();
            foreach (var data in blendshapeData)
            {
                if (data.samples != null && data.samples.Length > 0)
                {
                    blendshapeLookup[data.blendshapeName] = data;
                    Debug.Log($"[AttachToBlendshapeRuntime] Added blendshape '{data.blendshapeName}' with {data.samples.Length} samples", this);
                }
            }

            // Store base transform
            baseLocalPosition = transform.localPosition;
            baseLocalRotation = transform.localRotation;

            Debug.Log($"[AttachToBlendshapeRuntime] Initialized with {blendshapeLookup.Count} blendshapes. Base position: {baseLocalPosition}, Base rotation: {baseLocalRotation}", this);

            if (blendshapeLookup.Count == 0)
            {
                Debug.LogWarning("[AttachToBlendshapeRuntime] No blendshape data found. Component disabled.", this);
                enabled = false;
            }
        }

        private int frameCount = 0;
        private void LateUpdate()
        {
            frameCount++;
            
            if (sourceMesh == null || sourceMesh.sharedMesh == null)
            {
                if (frameCount % 300 == 0) // Log every 5 seconds at 60fps
                {
                    Debug.LogWarning($"[AttachToBlendshapeRuntime] Source mesh is null or sharedMesh is null. Frame: {frameCount}", this);
                }
                return;
            }

            Vector3 totalPositionDelta = Vector3.zero;
            Quaternion totalRotationDelta = Quaternion.identity;
            bool hasAnyDelta = false;
            bool hasAnyNonZeroWeight = false;

            // Accumulate transforms from all tracked blendshapes
            foreach (var kvp in blendshapeLookup)
            {
                string blendshapeName = kvp.Key;
                BlendshapeTransformData data = kvp.Value;

                // Get current blendshape weight
                int blendshapeIndex = sourceMesh.sharedMesh.GetBlendShapeIndex(blendshapeName);
                if (blendshapeIndex < 0)
                {
                    if (frameCount == 1)
                    {
                        Debug.LogWarning($"[AttachToBlendshapeRuntime] Blendshape '{blendshapeName}' not found in mesh '{sourceMesh.sharedMesh.name}'", this);
                    }
                    continue;
                }

                float currentWeight = sourceMesh.GetBlendShapeWeight(blendshapeIndex);
                
                if (currentWeight > 0.01f)
                {
                    hasAnyNonZeroWeight = true;
                }
                
                // Skip if weight hasn't changed (optimization)
                if (lastWeights.TryGetValue(blendshapeName, out float lastWeight) && 
                    Mathf.Approximately(currentWeight, lastWeight))
                {
                    continue;
                }
                lastWeights[blendshapeName] = currentWeight;

                // Interpolate transform sample
                if (InterpolateSample(data.samples, currentWeight, out TransformSample sample))
                {
                    totalPositionDelta += sample.positionDelta;
                    totalRotationDelta *= sample.GetRotation();
                    hasAnyDelta = true;
                    
                    if (frameCount <= 10 || (frameCount % 300 == 0 && currentWeight > 0.01f))
                    {
                        Debug.Log($"[AttachToBlendshapeRuntime] Blendshape '{blendshapeName}' weight: {currentWeight:F2}, Position delta: {sample.positionDelta}, Rotation delta: {sample.GetRotation().eulerAngles}", this);
                    }
                }
            }

            // Apply accumulated transform
            if (hasAnyDelta)
            {
                transform.localPosition = baseLocalPosition + totalPositionDelta;
                transform.localRotation = baseLocalRotation * totalRotationDelta;
            }
            else if (hasAnyNonZeroWeight && frameCount % 300 == 0)
            {
                Debug.Log($"[AttachToBlendshapeRuntime] Has non-zero weights but no delta calculated. Total position delta: {totalPositionDelta}", this);
            }
        }

        private bool InterpolateSample(TransformSample[] samples, float weight, out TransformSample result)
        {
            result = default(TransformSample);
            
            if (samples == null || samples.Length == 0)
                return false;

            // Sort samples by weight
            var sortedSamples = samples.OrderBy(s => s.blendshapeWeight).ToArray();

            // Clamp weight to valid range
            weight = Mathf.Clamp(weight, 0f, 100f);

            // Find surrounding samples
            if (weight <= sortedSamples[0].blendshapeWeight)
            {
                // Below first sample - return first sample
                result = sortedSamples[0];
                return true;
            }

            if (weight >= sortedSamples[sortedSamples.Length - 1].blendshapeWeight)
            {
                // Above last sample - return last sample
                result = sortedSamples[sortedSamples.Length - 1];
                return true;
            }

            // Find two samples to interpolate between
            for (int i = 0; i < sortedSamples.Length - 1; i++)
            {
                var sample1 = sortedSamples[i];
                var sample2 = sortedSamples[i + 1];

                if (weight >= sample1.blendshapeWeight && weight <= sample2.blendshapeWeight)
                {
                    // Interpolate
                    float t = (weight - sample1.blendshapeWeight) / 
                              (sample2.blendshapeWeight - sample1.blendshapeWeight);

                    Quaternion rot1 = sample1.GetRotation();
                    Quaternion rot2 = sample2.GetRotation();
                    Quaternion interpolatedRot = Quaternion.Slerp(rot1, rot2, t);

                    result = new TransformSample
                    {
                        blendshapeWeight = weight,
                        positionDelta = Vector3.Lerp(sample1.positionDelta, sample2.positionDelta, t),
                        rotationDelta = new Vector4(interpolatedRot.x, interpolatedRot.y, 
                                                    interpolatedRot.z, interpolatedRot.w)
                    };
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Sets the blendshape transform data (called during build).
        /// </summary>
        public void SetBlendshapeData(SkinnedMeshRenderer source, 
                                     Dictionary<string, List<TransformSample>> samples)
        {
            sourceMesh = source;
            blendshapeData.Clear();

            Debug.Log($"[AttachToBlendshapeRuntime] SetBlendshapeData called. Source: {(source != null ? source.name : "NULL")}, Samples count: {samples.Count}", this);

            foreach (var kvp in samples)
            {
                var runtimeSamples = kvp.Value.Select(s => new TransformSample
                {
                    blendshapeWeight = s.blendshapeWeight,
                    positionDelta = s.positionDelta,
                    rotationDelta = new Vector4(s.rotationDelta.x, s.rotationDelta.y, 
                                               s.rotationDelta.z, s.rotationDelta.w)
                }).ToArray();

                blendshapeData.Add(new BlendshapeTransformData
                {
                    blendshapeName = kvp.Key,
                    samples = runtimeSamples
                });
                
                Debug.Log($"[AttachToBlendshapeRuntime] Added blendshape '{kvp.Key}' with {runtimeSamples.Length} samples. First sample: weight={runtimeSamples[0].blendshapeWeight}, pos={runtimeSamples[0].positionDelta}", this);
            }
        }
    }
}

