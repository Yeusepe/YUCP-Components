using System;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// A keyvector represents a single contact point on a finger surface.
    /// It consists of a position and an outward-facing normal, both in link-local space.
    /// This is the atomic unit of the contact field.
    /// </summary>
    [Serializable]
    public struct KeyVector
    {
        /// <summary>
        /// Position of the contact point in link-local space (meters).
        /// </summary>
        public Vector3 position;

        /// <summary>
        /// Outward-facing normal direction at the contact point (normalized).
        /// </summary>
        public Vector3 normal;

        public KeyVector(Vector3 position, Vector3 normal)
        {
            this.position = position;
            this.normal = normal.normalized;
        }

        /// <summary>
        /// Transform this keyvector by a local-to-world matrix.
        /// </summary>
        public KeyVector Transform(Matrix4x4 localToWorld)
        {
            return new KeyVector(
                localToWorld.MultiplyPoint3x4(position),
                localToWorld.MultiplyVector(normal).normalized
            );
        }

        /// <summary>
        /// Check if this keyvector's normal aligns with a target direction within threshold.
        /// </summary>
        public bool IsAligned(Vector3 targetDirection, float cosThreshold = 0.866f) // Default: 30 degrees
        {
            return Vector3.Dot(normal, targetDirection) > cosThreshold;
        }

        /// <summary>
        /// Pack into GPU-friendly format (float4 position, float4 normal).
        /// </summary>
        public void Pack(out Vector4 outPosition, out Vector4 outNormal)
        {
            outPosition = new Vector4(position.x, position.y, position.z, 1f);
            outNormal = new Vector4(normal.x, normal.y, normal.z, 0f);
        }

        /// <summary>
        /// Create from packed GPU format.
        /// </summary>
        public static KeyVector Unpack(Vector4 packedPosition, Vector4 packedNormal)
        {
            return new KeyVector(
                new Vector3(packedPosition.x, packedPosition.y, packedPosition.z),
                new Vector3(packedNormal.x, packedNormal.y, packedNormal.z)
            );
        }

        public override string ToString()
        {
            return $"KeyVector(pos={position}, normal={normal})";
        }
    }
}
