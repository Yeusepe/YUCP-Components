using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    public static class AdvancedVisemeMeshCalibrator
    {
        public readonly struct BasisInput
        {
            public readonly AdvancedVisemeArticulator articulator;
            public readonly int blendShapeIndex;

            public BasisInput(AdvancedVisemeArticulator articulator, int blendShapeIndex)
            {
                this.articulator = articulator;
                this.blendShapeIndex = blendShapeIndex;
            }
        }

        public sealed class Result
        {
            public Mesh mesh;
            public float[,] coefficients;
            public string[] residualBlendShapeNames;
            public float fitRms;
            public float fitMaximum;
            public string error;
            public bool success => mesh != null && string.IsNullOrEmpty(error);
        }

        public static Result Build(Mesh source, int[] visemeBlendShapeIndices, IReadOnlyList<BasisInput> basis)
        {
            var result = new Result();
            if (source == null)
            {
                result.error = "Face mesh is missing.";
                return result;
            }
            if (visemeBlendShapeIndices == null || visemeBlendShapeIndices.Length != VisemeReconstructionProfile.VisemeCount)
            {
                result.error = "Exactly 15 Oculus viseme blendshape indices are required.";
                return result;
            }
            if (basis == null || basis.Count == 0)
            {
                result.error = "At least one articulator blendshape is required for calibration.";
                return result;
            }

            var vertexCount = source.vertexCount;
            var basisVertices = new Vector3[basis.Count][];
            var basisNormals = new Vector3[basis.Count][];
            var basisTangents = new Vector3[basis.Count][];
            for (var j = 0; j < basis.Count; j++)
            {
                if (!TryReadAtWeight100(source, basis[j].blendShapeIndex, vertexCount,
                        out basisVertices[j], out basisNormals[j], out basisTangents[j], out var error))
                {
                    result.error = error;
                    return result;
                }
            }

            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name + "_YUCP_AVR";
            result.coefficients = new float[VisemeReconstructionProfile.VisemeCount, basis.Count];
            result.residualBlendShapeNames = new string[VisemeReconstructionProfile.VisemeCount];

            double squaredResidual = 0d;
            var residualSamples = 0L;
            var maxResidual = 0f;

            for (var i = 0; i < VisemeReconstructionProfile.VisemeCount; i++)
            {
                var visemeIndex = visemeBlendShapeIndices[i];
                if (visemeIndex < 0)
                {
                    if (i == 0) continue;
                    UnityEngine.Object.DestroyImmediate(clone);
                    result.error = $"Viseme '{VisemeReconstructionProfile.VisemeNames[i]}' is not mapped on the face mesh.";
                    return result;
                }

                if (!TryReadAtWeight100(source, visemeIndex, vertexCount,
                        out var targetVertices, out var targetNormals, out var targetTangents, out var error))
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                    result.error = error;
                    return result;
                }

                var coefficients = SolveNonNegativeLeastSquares(basisVertices, targetVertices);
                var residualVertices = (Vector3[])targetVertices.Clone();
                var residualNormals = (Vector3[])targetNormals.Clone();
                var residualTangents = (Vector3[])targetTangents.Clone();

                for (var j = 0; j < basis.Count; j++)
                {
                    var coefficient = coefficients[j];
                    result.coefficients[i, j] = coefficient;
                    if (coefficient <= 0f) continue;
                    for (var v = 0; v < vertexCount; v++)
                    {
                        residualVertices[v] -= basisVertices[j][v] * coefficient;
                        residualNormals[v] -= basisNormals[j][v] * coefficient;
                        residualTangents[v] -= basisTangents[j][v] * coefficient;
                    }
                }

                for (var v = 0; v < vertexCount; v++)
                {
                    var magnitude = residualVertices[v].magnitude;
                    squaredResidual += residualVertices[v].sqrMagnitude;
                    residualSamples++;
                    if (magnitude > maxResidual) maxResidual = magnitude;
                }

                var residualName = $"YUCP_AVR_Residual_{VisemeReconstructionProfile.VisemeNames[i]}";
                clone.AddBlendShapeFrame(residualName, 100f, residualVertices, residualNormals, residualTangents);
                result.residualBlendShapeNames[i] = residualName;
            }

            result.mesh = clone;
            result.fitRms = residualSamples > 0 ? Mathf.Sqrt((float)(squaredResidual / residualSamples)) : 0f;
            result.fitMaximum = maxResidual;
            return result;
        }

        public static float[] SolveNonNegativeLeastSquares(Vector3[][] basis, Vector3[] target, int iterations = 256)
        {
            if (basis == null || basis.Length == 0) return Array.Empty<float>();
            if (target == null) throw new ArgumentNullException(nameof(target));

            var count = basis.Length;
            var gram = new double[count, count];
            var projection = new double[count];
            for (var j = 0; j < count; j++)
            {
                if (basis[j] == null || basis[j].Length != target.Length)
                    throw new ArgumentException("All basis vectors must match the target vertex count.");

                for (var v = 0; v < target.Length; v++) projection[j] += Vector3.Dot(basis[j][v], target[v]);
                for (var k = j; k < count; k++)
                {
                    double value = 0d;
                    for (var v = 0; v < target.Length; v++) value += Vector3.Dot(basis[j][v], basis[k][v]);
                    gram[j, k] = value;
                    gram[k, j] = value;
                }
            }

            var output = new double[count];
            const double regularization = 1e-12;
            for (var iteration = 0; iteration < Mathf.Max(1, iterations); iteration++)
            {
                var largestChange = 0d;
                for (var j = 0; j < count; j++)
                {
                    var diagonal = gram[j, j] + regularization;
                    if (diagonal <= regularization) continue;
                    var numerator = projection[j];
                    for (var k = 0; k < count; k++)
                    {
                        if (k != j) numerator -= gram[j, k] * output[k];
                    }
                    var next = Math.Max(0d, numerator / diagonal);
                    largestChange = Math.Max(largestChange, Math.Abs(next - output[j]));
                    output[j] = next;
                }
                if (largestChange < 1e-8) break;
            }

            var result = new float[count];
            for (var i = 0; i < count; i++) result[i] = (float)output[i];
            return result;
        }

        private static bool TryReadAtWeight100(
            Mesh mesh,
            int blendShapeIndex,
            int vertexCount,
            out Vector3[] vertices,
            out Vector3[] normals,
            out Vector3[] tangents,
            out string error)
        {
            vertices = new Vector3[vertexCount];
            normals = new Vector3[vertexCount];
            tangents = new Vector3[vertexCount];
            error = null;
            if (blendShapeIndex < 0 || blendShapeIndex >= mesh.blendShapeCount)
            {
                error = $"Blendshape index {blendShapeIndex} is invalid for mesh '{mesh.name}'.";
                return false;
            }

            var frameCount = mesh.GetBlendShapeFrameCount(blendShapeIndex);
            if (frameCount == 0)
            {
                error = $"Blendshape '{mesh.GetBlendShapeName(blendShapeIndex)}' has no frames.";
                return false;
            }

            var frame = frameCount - 1;
            mesh.GetBlendShapeFrameVertices(blendShapeIndex, frame, vertices, normals, tangents);
            var weight = mesh.GetBlendShapeFrameWeight(blendShapeIndex, frame);
            if (Mathf.Abs(weight) < 1e-6f)
            {
                error = $"Blendshape '{mesh.GetBlendShapeName(blendShapeIndex)}' has a zero-weight final frame.";
                return false;
            }

            var scale = 100f / weight;
            if (!Mathf.Approximately(scale, 1f))
            {
                for (var i = 0; i < vertexCount; i++)
                {
                    vertices[i] *= scale;
                    normals[i] *= scale;
                    tangents[i] *= scale;
                }
            }
            return true;
        }
    }
}
