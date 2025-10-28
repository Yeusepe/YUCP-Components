using UnityEngine;
using System.Collections.Generic;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Serializable planner settings with defaults per grip type.
    /// </summary>
    [System.Serializable]
    public struct PlannerSettings
    {
        [Header("Sampling")]
        public int sampleCount;
        public float padOffset;
        
        [Header("Weights")]
        public float wDistance;
        public float wNormal;
        public float wSeparation;
        public float wOpposition;
        public float wCurvature;
        
        [Header("Parameters")]
        public float separationSigma;
        public bool usePrimitives;
        public bool debugDraw;
        
        /// <summary>
        /// Get default settings for a grip type.
        /// </summary>
        public static PlannerSettings GetDefaults()
        {
            // Default settings for manual grip positioning
            return new PlannerSettings
            {
                sampleCount = 32,
                padOffset = 0.01f,
                wDistance = 1.0f,
                wNormal = 2.0f,
                wSeparation = 1.5f,
                wOpposition = 3.0f,
                wCurvature = 0.5f,
                separationSigma = 0.05f,
                usePrimitives = true,
                debugDraw = false
            };
        }
    }
    
    /// <summary>
    /// Finger target data structure.
    /// </summary>
    public struct FingerTarget
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector3 tangentPrimary;
    }
    
    /// <summary>
    /// Complete finger targets for all fingers.
    /// </summary>
    public struct FingerTargets
    {
        public FingerTarget thumb;
        public FingerTarget index;
        public FingerTarget middle;
        public FingerTarget ring;
        public FingerTarget little;
    }
}
