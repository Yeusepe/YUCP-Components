using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Solves object transforms using surface cluster deformation.
    /// Supports multiple solver modes for different attachment types.
    /// </summary>
    public static class BlendshapeSolver
    {
        public struct SolverResult
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool success;
            public string errorMessage;
        }

        public struct SurfaceFrame
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector3 tangent;
            public Vector3 binormal;
            public Quaternion rotation;

            public static SurfaceFrame FromVectors(Vector3 pos, Vector3 norm, Vector3 tan)
            {
                SurfaceFrame frame = new SurfaceFrame();
                frame.position = pos;
                frame.normal = norm;
                frame.tangent = tan;
                frame.binormal = Vector3.Cross(norm, tan).normalized;



                Matrix4x4 matrix = new Matrix4x4();
                matrix.SetColumn(0, tan);
                matrix.SetColumn(1, frame.binormal);
                matrix.SetColumn(2, norm);
                matrix.SetColumn(3, new Vector4(0, 0, 0, 1));

                frame.rotation = matrix.rotation;

                return frame;
            }
        }

        /// <summary>
        /// Solve using Rigid mode: single rotation + translation.
        /// Best for small rigid objects like piercings, badges, small jewelry.
        /// </summary>
        public static SolverResult SolveRigid(
            Vector3 clusterPosition,
            Vector3 clusterNormal,
            Vector3 clusterTangent,
            Transform objectTransform,
            Transform targetMeshTransform,
            bool alignRotation,
            Vector3? previousTangent = null,
            float smoothingFactor = 0.3f)
        {
            SolverResult result = new SolverResult
            {
                scale = Vector3.one,
                success = false
            };

            try
            {

                Vector3 stabilizedTangent = clusterTangent;
                if (previousTangent.HasValue && smoothingFactor > 0f)
                {
                    stabilizedTangent = StabilizeTangent(
                        clusterTangent,
                        previousTangent.Value,
                        clusterNormal,
                        smoothingFactor);
                }


                SurfaceFrame frame = SurfaceFrame.FromVectors(
                    clusterPosition,
                    clusterNormal,
                    stabilizedTangent);


                frame = OrthonormalizeFrame(frame);


                Vector3 worldPosition = targetMeshTransform.TransformPoint(frame.position);


                Quaternion worldRotation;
                if (alignRotation)
                {

                    worldRotation = targetMeshTransform.rotation * frame.rotation;
                }
                else
                {

                    worldRotation = objectTransform.rotation;
                }


                if (objectTransform.parent != null)
                {
                    result.position = objectTransform.parent.InverseTransformPoint(worldPosition);
                    result.rotation = Quaternion.Inverse(objectTransform.parent.rotation) * worldRotation;
                }
                else
                {
                    result.position = worldPosition;
                    result.rotation = worldRotation;
                }

                result.success = true;
            }
            catch (System.Exception ex)
            {
                result.errorMessage = $"Rigid solver failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Solve using Rigid + Normal Offset mode.
        /// Adds a small outward push along the surface normal.
        /// </summary>
        public static SolverResult SolveRigidNormalOffset(
            Vector3 clusterPosition,
            Vector3 clusterNormal,
            Vector3 clusterTangent,
            Transform objectTransform,
            Transform targetMeshTransform,
            bool alignRotation,
            float normalOffset,
            Vector3? previousTangent = null,
            float smoothingFactor = 0.3f)
        {

            SolverResult rigidResult = SolveRigid(
                clusterPosition,
                clusterNormal,
                clusterTangent,
                objectTransform,
                targetMeshTransform,
                alignRotation,
                previousTangent,
                smoothingFactor);

            if (!rigidResult.success)
            {
                return rigidResult;
            }


            Vector3 worldNormal = targetMeshTransform.TransformDirection(clusterNormal);
            Vector3 worldOffsetPosition = targetMeshTransform.TransformPoint(clusterPosition) + worldNormal * normalOffset;


            if (objectTransform.parent != null)
            {
                rigidResult.position = objectTransform.parent.InverseTransformPoint(worldOffsetPosition);
            }
            else
            {
                rigidResult.position = worldOffsetPosition;
            }

            return rigidResult;
        }

        /// <summary>
        /// Solve using Affine mode: allows minor shear/scale.
        /// Suitable for wider objects like stickers that need to stretch with skin.
        /// </summary>
        public static SolverResult SolveAffine(
            Vector3 clusterPosition,
            Vector3 clusterNormal,
            Vector3 clusterTangent,
            Transform objectTransform,
            Transform targetMeshTransform,
            Vector3 baseClusterPosition,
            Vector3 baseClusterNormal,
            Vector3 baseClusterTangent,
            bool alignRotation,
            Vector3? previousTangent = null,
            float smoothingFactor = 0.3f)
        {

            SolverResult result = SolveRigid(
                clusterPosition,
                clusterNormal,
                clusterTangent,
                objectTransform,
                targetMeshTransform,
                alignRotation,
                previousTangent,
                smoothingFactor);

            if (!result.success)
            {
                return result;
            }



            float baseSize = baseClusterTangent.magnitude;
            float deformedSize = clusterTangent.magnitude;

            if (baseSize > 0.001f)
            {
                float scaleRatio = deformedSize / baseSize;


                scaleRatio = Mathf.Clamp(scaleRatio, 0.8f, 1.2f);

                result.scale = new Vector3(scaleRatio, scaleRatio, scaleRatio);
            }

            return result;
        }

        /// <summary>
        /// Stabilize tangent vector using smoothing.
        /// </summary>
        private static Vector3 StabilizeTangent(
            Vector3 currentTangent,
            Vector3 previousTangent,
            Vector3 normal,
            float smoothingFactor)
        {

            Vector3 blended = Vector3.Lerp(currentTangent, previousTangent, smoothingFactor);


            blended = (blended - Vector3.Dot(blended, normal) * normal).normalized;


            if (Vector3.Dot(blended, previousTangent) < 0f)
            {

                return previousTangent;
            }

            return blended;
        }

        /// <summary>
        /// Apply Gram-Schmidt orthonormalization.
        /// </summary>
        private static SurfaceFrame OrthonormalizeFrame(SurfaceFrame frame)
        {
            SurfaceFrame orthonormal = frame;


            orthonormal.normal = frame.normal.normalized;


            orthonormal.tangent = (frame.tangent - Vector3.Dot(frame.tangent, orthonormal.normal) * orthonormal.normal).normalized;


            orthonormal.binormal = Vector3.Cross(orthonormal.normal, orthonormal.tangent).normalized;


            Matrix4x4 matrix = new Matrix4x4();
            matrix.SetColumn(0, orthonormal.tangent);
            matrix.SetColumn(1, orthonormal.binormal);
            matrix.SetColumn(2, orthonormal.normal);
            matrix.SetColumn(3, new Vector4(0, 0, 0, 1));

            orthonormal.rotation = matrix.rotation;

            return orthonormal;
        }
    }
}
