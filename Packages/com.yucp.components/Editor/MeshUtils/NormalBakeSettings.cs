using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Serializable settings structure for normal transfer operations.
    /// Used by both CPU and GPU implementations.
    /// </summary>
    [System.Serializable]
    public class NormalBakeSettings
    {
        public NormalTransferMethod method;
        
        // Proximity settings
        public float proximityThreshold = 0.01f;
        public float proximityBlendStrength = 0.0f;
        
        // Projection settings
        public float projectionDistance = 0.05f;
        public SeamlessNormalsData.ProjectionDirection projectionDirection = SeamlessNormalsData.ProjectionDirection.BothDirections;
        public float projectionBlendStrength = 0.0f;
        
        // Shared field settings
        public float sharedFieldPositionThreshold = 0.001f;
        public float sharedFieldHardEdgeAngle = 60.0f;
        
        // Advanced settings
        public float maxTransferDistance = 0.0f;
        public bool respectHardEdges = true;
        public float hardEdgeAngle = 60.0f;

        public static NormalBakeSettings FromData(SeamlessNormalsData data)
        {
            return new NormalBakeSettings
            {
                method = data.transferMethod,
                proximityThreshold = data.proximityThreshold,
                proximityBlendStrength = data.proximityBlendStrength,
                projectionDistance = data.projectionDistance,
                projectionDirection = data.projectionDirection,
                projectionBlendStrength = data.projectionBlendStrength,
                sharedFieldPositionThreshold = data.sharedFieldPositionThreshold,
                sharedFieldHardEdgeAngle = data.sharedFieldHardEdgeAngle,
                maxTransferDistance = data.maxTransferDistance,
                respectHardEdges = data.respectHardEdges,
                hardEdgeAngle = data.hardEdgeAngle
            };
        }
    }
}

