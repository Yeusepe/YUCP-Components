using System;
using System.Collections.Generic;
using UnityEditor;
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

        /// <summary>
        /// One non-negative, unit-weight ray from an existing face-tracking pose.
        /// A signed articulator is represented by two inputs whose directions are
        /// +1 and -1; the clips themselves still contain non-negative blendshape
        /// weights.
        /// </summary>
        public readonly struct PoseBasisInput
        {
            public readonly AdvancedVisemeArticulator articulator;
            public readonly int direction;
            public readonly AnimationClip clip;
            public readonly string rendererPath;

            public PoseBasisInput(
                AdvancedVisemeArticulator articulator,
                int direction,
                AnimationClip clip,
                string rendererPath)
            {
                this.articulator = articulator;
                this.direction = direction;
                this.clip = clip;
                this.rendererPath = rendererPath;
            }
        }

        /// <summary>
        /// Metadata for a calibrated composite pose ray. Its index matches the
        /// second dimension of <see cref="Result.coefficients"/>.
        /// </summary>
        public readonly struct PoseBasisAxis
        {
            public readonly AdvancedVisemeArticulator articulator;
            public readonly int direction;
            public readonly AnimationClip clip;
            public readonly string rendererPath;

            public PoseBasisAxis(PoseBasisInput input)
            {
                articulator = input.articulator;
                direction = input.direction;
                clip = input.clip;
                rendererPath = input.rendererPath;
            }
        }

        public sealed class Result
        {
            public Mesh mesh;
            public float[,] coefficients;
            public string[] residualBlendShapeNames;
            public string[] conflictingResidualBlendShapeNames;
            public string hiddenPhoneResidualBlendShapeName;
            public float fitRms;
            public float fitMaximum;
            public string error;
            public PoseBasisAxis[] poseBasisAxes;
            public bool success => mesh != null && string.IsNullOrEmpty(error);
        }

        private sealed class OrthogonalBasisAxis
        {
            public Vector3[] vertices;
            public double[] sourceCoefficients;
            public double normSquared;
        }

        public static Result Build(Mesh source, int[] visemeBlendShapeIndices, IReadOnlyList<BasisInput> basis)
        {
            if (!TryValidateInputs(source, visemeBlendShapeIndices, basis?.Count ?? 0,
                    "At least one articulator blendshape is required for calibration.", out var validation))
                return validation;

            var vertexCount = source.vertexCount;
            var basisVertices = new Vector3[basis.Count][];
            var basisNormals = new Vector3[basis.Count][];
            var basisTangents = new Vector3[basis.Count][];
            for (var j = 0; j < basis.Count; j++)
            {
                if (!TryReadAtWeight100(source, basis[j].blendShapeIndex, vertexCount,
                        out basisVertices[j], out basisNormals[j], out basisTangents[j], out var error))
                {
                    return Error(error);
                }
            }

            return BuildResidualResult(
                source,
                visemeBlendShapeIndices,
                basisVertices,
                basisNormals,
                basisTangents,
                null);
        }

        /// <summary>
        /// Builds a residual decomposition against the actual endpoint geometry
        /// of existing face-tracking clips. Every clip is treated as one
        /// composite basis ray, even when it drives several blendshapes.
        /// </summary>
        public static Result BuildFromPoses(
            Mesh source,
            int[] visemeBlendShapeIndices,
            IReadOnlyList<PoseBasisInput> basis)
        {
            if (!TryValidateInputs(source, visemeBlendShapeIndices, basis?.Count ?? 0,
                    "At least one face-tracking pose is required for calibration.", out var validation))
                return validation;

            var vertexCount = source.vertexCount;
            var basisVertices = new Vector3[basis.Count][];
            var basisNormals = new Vector3[basis.Count][];
            var basisTangents = new Vector3[basis.Count][];
            var axes = new PoseBasisAxis[basis.Count];
            for (var j = 0; j < basis.Count; j++)
            {
                var input = basis[j];
                if (input.direction != -1 && input.direction != 1)
                {
                    return Error(
                        $"Face-tracking pose {j} has direction {input.direction}; direction must be +1 or -1.");
                }
                if (input.clip == null)
                    return Error($"Face-tracking pose {j} has no animation clip.");
                if (input.rendererPath == null)
                    return Error($"Face-tracking pose '{input.clip.name}' has no renderer path.");

                if (!TryReadCompositePose(
                        source,
                        input.clip,
                        input.rendererPath,
                        vertexCount,
                        out basisVertices[j],
                        out basisNormals[j],
                        out basisTangents[j],
                        out var error))
                    return Error(error);

                axes[j] = new PoseBasisAxis(input);
            }

            return BuildResidualResult(
                source,
                visemeBlendShapeIndices,
                basisVertices,
                basisNormals,
                basisTangents,
                axes);
        }

        private static Result BuildResidualResult(
            Mesh source,
            int[] visemeBlendShapeIndices,
            Vector3[][] basisVertices,
            Vector3[][] basisNormals,
            Vector3[][] basisTangents,
            PoseBasisAxis[] poseBasisAxes)
        {
            var result = new Result { poseBasisAxes = poseBasisAxes };
            var vertexCount = source.vertexCount;

            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name + "_YUCP_AVR";
            result.coefficients = new float[VisemeReconstructionProfile.VisemeCount, basisVertices.Length];
            result.residualBlendShapeNames = new string[VisemeReconstructionProfile.VisemeCount];
            result.conflictingResidualBlendShapeNames =
                new string[VisemeReconstructionProfile.VisemeCount];

            double squaredResidual = 0d;
            var residualSamples = 0L;
            var maxResidual = 0f;
            Vector3[] ppVertices = null;
            Vector3[] ppNormals = null;
            Vector3[] ppTangents = null;
            Vector3[] nnVertices = null;
            Vector3[] nnNormals = null;
            Vector3[] nnTangents = null;

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

                if (i == 1)
                {
                    ppVertices = targetVertices;
                    ppNormals = targetNormals;
                    ppTangents = targetTangents;
                }
                else if (i == 8)
                {
                    nnVertices = targetVertices;
                    nnNormals = targetNormals;
                    nnTangents = targetTangents;
                }

                var coefficients = SolveNonNegativeLeastSquares(basisVertices, targetVertices);
                var residualVertices = (Vector3[])targetVertices.Clone();
                var residualNormals = (Vector3[])targetNormals.Clone();
                var residualTangents = (Vector3[])targetTangents.Clone();

                for (var j = 0; j < basisVertices.Length; j++)
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

                var projection = ProjectOntoComplement(
                    basisVertices, residualVertices, out var perpendicularVertices);
                var conflictingVertices = Difference(residualVertices, perpendicularVertices);
                var conflictingNormals = new Vector3[vertexCount];
                var conflictingTangents = new Vector3[vertexCount];
                for (var basisIndex = 0; basisIndex < projection.Length; basisIndex++)
                {
                    var coefficient = projection[basisIndex];
                    if (Math.Abs(coefficient) <= 1e-15d) continue;
                    AddScaled(conflictingNormals, basisNormals[basisIndex], coefficient);
                    AddScaled(conflictingTangents, basisTangents[basisIndex], coefficient);
                }

                if (!AreFinite(conflictingVertices) || !AreFinite(conflictingNormals) ||
                    !AreFinite(conflictingTangents))
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                    result.error = $"Viseme '{VisemeReconstructionProfile.VisemeNames[i]}' " +
                                   "produced a non-finite conflicting residual projection.";
                    return result;
                }

                var residualEnergy = SquaredNorm(residualVertices) +
                                     SquaredNorm(residualNormals) +
                                     SquaredNorm(residualTangents);
                var conflictingEnergy = SquaredNorm(conflictingVertices) +
                                        SquaredNorm(conflictingNormals) +
                                        SquaredNorm(conflictingTangents);
                var negligibleConflictEnergy = Math.Max(1e-24d, residualEnergy * 1e-12d);
                if (IsFinite(conflictingEnergy) && conflictingEnergy > negligibleConflictEnergy)
                {
                    var conflictName =
                        $"YUCP_AVR_Conflict_{VisemeReconstructionProfile.VisemeNames[i]}";
                    clone.AddBlendShapeFrame(
                        conflictName,
                        100f,
                        conflictingVertices,
                        conflictingNormals,
                        conflictingTangents);
                    result.conflictingResidualBlendShapeNames[i] = conflictName;
                }
            }

            if (TryBuildHiddenPhoneResidual(
                    basisVertices, basisNormals, basisTangents,
                    ppVertices, ppNormals, ppTangents,
                    nnVertices, nnNormals, nnTangents,
                    out var hiddenVertices, out var hiddenNormals, out var hiddenTangents))
            {
                const string hiddenName = "YUCP_AVR_Hidden_PP_Minus_nn";
                clone.AddBlendShapeFrame(
                    hiddenName, 100f, hiddenVertices, hiddenNormals, hiddenTangents);
                result.hiddenPhoneResidualBlendShapeName = hiddenName;
            }

            result.mesh = clone;
            result.fitRms = residualSamples > 0 ? Mathf.Sqrt((float)(squaredResidual / residualSamples)) : 0f;
            result.fitMaximum = maxResidual;
            return result;
        }

        private static bool TryValidateInputs(
            Mesh source,
            int[] visemeBlendShapeIndices,
            int basisCount,
            string emptyBasisError,
            out Result errorResult)
        {
            errorResult = null;
            if (source == null)
            {
                errorResult = Error("Face mesh is missing.");
                return false;
            }
            if (visemeBlendShapeIndices == null ||
                visemeBlendShapeIndices.Length != VisemeReconstructionProfile.VisemeCount)
            {
                errorResult = Error("Exactly 15 Oculus viseme blendshape indices are required.");
                return false;
            }
            if (basisCount == 0)
            {
                errorResult = Error(emptyBasisError);
                return false;
            }
            return true;
        }

        private static Result Error(string message)
        {
            return new Result { error = message };
        }

        private static bool TryReadCompositePose(
            Mesh mesh,
            AnimationClip clip,
            string rendererPath,
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

            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (objectBindings != null && objectBindings.Length > 0)
            {
                error = $"Face-tracking pose '{clip.name}' contains object-reference curves; " +
                        "only static blendshape curves are supported.";
                return false;
            }

            var bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings == null || bindings.Length == 0)
            {
                error = $"Face-tracking pose '{clip.name}' contains no animation curves.";
                return false;
            }

            var usedBlendShapes = new HashSet<int>();
            var activeCurveCount = 0;
            foreach (var binding in bindings)
            {
                if (binding.type != typeof(SkinnedMeshRenderer) ||
                    !string.Equals(binding.path, rendererPath, StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(binding.propertyName) ||
                    !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                {
                    error = $"Face-tracking pose '{clip.name}' has unsupported curve " +
                            $"'{binding.path}:{binding.propertyName}'. Every curve must target " +
                            $"SkinnedMeshRenderer blendshapes at '{rendererPath}'.";
                    return false;
                }

                var blendShapeName = binding.propertyName.Substring("blendShape.".Length);
                if (string.IsNullOrEmpty(blendShapeName))
                {
                    error = $"Face-tracking pose '{clip.name}' contains an unnamed blendshape curve.";
                    return false;
                }

                var blendShapeIndex = mesh.GetBlendShapeIndex(blendShapeName);
                if (blendShapeIndex < 0)
                {
                    error = $"Face-tracking pose '{clip.name}' references blendshape " +
                            $"'{blendShapeName}', which is missing from mesh '{mesh.name}'.";
                    return false;
                }
                if (!usedBlendShapes.Add(blendShapeIndex))
                {
                    error = $"Face-tracking pose '{clip.name}' drives blendshape " +
                            $"'{blendShapeName}' more than once.";
                    return false;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                {
                    error = $"Face-tracking pose '{clip.name}' has an empty curve for " +
                            $"blendshape '{blendShapeName}'.";
                    return false;
                }

                var endpointWeight = curve.Evaluate(clip.length);
                if (!IsFinite(endpointWeight))
                {
                    error = $"Face-tracking pose '{clip.name}' evaluates blendshape " +
                            $"'{blendShapeName}' to a non-finite endpoint weight.";
                    return false;
                }
                if (endpointWeight < -1e-5f)
                {
                    error = $"Face-tracking pose '{clip.name}' evaluates blendshape " +
                            $"'{blendShapeName}' to negative weight {endpointWeight:G6}. " +
                            "A pose basis ray must contain non-negative endpoint weights.";
                    return false;
                }
                if (endpointWeight <= 1e-5f) continue;

                if (!TryReadLinearBlendShapeAtWeight(
                        mesh,
                        blendShapeIndex,
                        endpointWeight,
                        vertexCount,
                        out var shapeVertices,
                        out var shapeNormals,
                        out var shapeTangents,
                        out error))
                {
                    error = $"Face-tracking pose '{clip.name}' cannot use blendshape " +
                            $"'{blendShapeName}': {error}";
                    return false;
                }

                Add(vertices, shapeVertices);
                Add(normals, shapeNormals);
                Add(tangents, shapeTangents);
                activeCurveCount++;
            }

            if (activeCurveCount == 0)
            {
                error = $"Face-tracking pose '{clip.name}' has no positive endpoint blendshape weights.";
                return false;
            }
            if (!AreFinite(vertices) || !AreFinite(normals) || !AreFinite(tangents))
            {
                error = $"Face-tracking pose '{clip.name}' produces non-finite geometry.";
                return false;
            }

            var energy = SquaredNorm(vertices) + SquaredNorm(normals) + SquaredNorm(tangents);
            if (!IsFinite(energy) || energy <= 1e-24d)
            {
                error = $"Face-tracking pose '{clip.name}' produces an empty geometry ray.";
                return false;
            }
            return true;
        }

        private static bool TryReadLinearBlendShapeAtWeight(
            Mesh mesh,
            int blendShapeIndex,
            float targetWeight,
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

            var frameCount = mesh.GetBlendShapeFrameCount(blendShapeIndex);
            if (frameCount == 0)
            {
                error = "the blendshape has no frames.";
                return false;
            }

            var referenceFrame = -1;
            var referenceWeight = 0f;
            for (var frame = 0; frame < frameCount; frame++)
            {
                var frameWeight = mesh.GetBlendShapeFrameWeight(blendShapeIndex, frame);
                if (!IsFinite(frameWeight))
                {
                    error = $"frame {frame} has a non-finite weight.";
                    return false;
                }
                if (Mathf.Abs(frameWeight) > Mathf.Abs(referenceWeight))
                {
                    referenceFrame = frame;
                    referenceWeight = frameWeight;
                }
            }
            if (referenceFrame < 0 || Mathf.Abs(referenceWeight) < 1e-6f)
            {
                error = "all blendshape frame weights are zero.";
                return false;
            }

            var referenceVertices = new Vector3[vertexCount];
            var referenceNormals = new Vector3[vertexCount];
            var referenceTangents = new Vector3[vertexCount];
            mesh.GetBlendShapeFrameVertices(
                blendShapeIndex,
                referenceFrame,
                referenceVertices,
                referenceNormals,
                referenceTangents);
            if (!AreFinite(referenceVertices) || !AreFinite(referenceNormals) ||
                !AreFinite(referenceTangents))
            {
                error = $"frame {referenceFrame} contains non-finite geometry.";
                return false;
            }

            if (frameCount > 1)
            {
                var frameVertices = new Vector3[vertexCount];
                var frameNormals = new Vector3[vertexCount];
                var frameTangents = new Vector3[vertexCount];
                for (var frame = 0; frame < frameCount; frame++)
                {
                    mesh.GetBlendShapeFrameVertices(
                        blendShapeIndex,
                        frame,
                        frameVertices,
                        frameNormals,
                        frameTangents);
                    if (!AreFinite(frameVertices) || !AreFinite(frameNormals) ||
                        !AreFinite(frameTangents))
                    {
                        error = $"frame {frame} contains non-finite geometry.";
                        return false;
                    }

                    var expectedScale =
                        mesh.GetBlendShapeFrameWeight(blendShapeIndex, frame) / referenceWeight;
                    if (!ApproximatelyScaled(frameVertices, referenceVertices, expectedScale) ||
                        !ApproximatelyScaled(frameNormals, referenceNormals, expectedScale) ||
                        !ApproximatelyScaled(frameTangents, referenceTangents, expectedScale))
                    {
                        error = $"multi-frame geometry is nonlinear at frame {frame}; " +
                                "all frame deltas must be collinear with their frame weights.";
                        return false;
                    }
                }
            }

            var targetScale = targetWeight / referenceWeight;
            ScaleInto(referenceVertices, targetScale, vertices);
            ScaleInto(referenceNormals, targetScale, normals);
            ScaleInto(referenceTangents, targetScale, tangents);
            if (!AreFinite(vertices) || !AreFinite(normals) || !AreFinite(tangents))
            {
                error = "the requested endpoint weight produces non-finite geometry.";
                return false;
            }
            return true;
        }

        private static bool ApproximatelyScaled(Vector3[] actual, Vector3[] reference, float scale)
        {
            const float absoluteTolerance = 1e-7f;
            const float relativeTolerance = 1e-4f;
            for (var i = 0; i < actual.Length; i++)
            {
                var expected = reference[i] * scale;
                var tolerance = absoluteTolerance + relativeTolerance *
                    Mathf.Max(actual[i].magnitude, expected.magnitude);
                if ((actual[i] - expected).magnitude > tolerance) return false;
            }
            return true;
        }

        private static void ScaleInto(Vector3[] source, float scale, Vector3[] destination)
        {
            for (var i = 0; i < source.Length; i++) destination[i] = source[i] * scale;
        }

        private static void Add(Vector3[] destination, Vector3[] source)
        {
            for (var i = 0; i < destination.Length; i++) destination[i] += source[i];
        }

        private static void AddScaled(Vector3[] destination, Vector3[] source, double scale)
        {
            if (destination.Length != source.Length)
                throw new ArgumentException("Blendshape delta arrays must have matching lengths.");
            var scalar = (float)scale;
            for (var i = 0; i < destination.Length; i++) destination[i] += source[i] * scalar;
        }

        private static bool AreFinite(Vector3[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (!IsFinite(values[i].x) || !IsFinite(values[i].y) || !IsFinite(values[i].z))
                    return false;
            }
            return true;
        }

        private static bool TryBuildHiddenPhoneResidual(
            Vector3[][] basisVertices,
            Vector3[][] basisNormals,
            Vector3[][] basisTangents,
            Vector3[] ppVertices,
            Vector3[] ppNormals,
            Vector3[] ppTangents,
            Vector3[] nnVertices,
            Vector3[] nnNormals,
            Vector3[] nnTangents,
            out Vector3[] hiddenVertices,
            out Vector3[] hiddenNormals,
            out Vector3[] hiddenTangents)
        {
            hiddenVertices = Array.Empty<Vector3>();
            hiddenNormals = Array.Empty<Vector3>();
            hiddenTangents = Array.Empty<Vector3>();
            if (ppVertices == null || ppNormals == null || ppTangents == null ||
                nnVertices == null || nnNormals == null || nnTangents == null)
                return false;

            var targetVertices = Difference(ppVertices, nnVertices);
            var targetNormals = Difference(ppNormals, nnNormals);
            var targetTangents = Difference(ppTangents, nnTangents);
            var projection = ProjectOntoComplement(
                basisVertices, targetVertices, out hiddenVertices);
            hiddenNormals = targetNormals;
            hiddenTangents = targetTangents;
            for (var basisIndex = 0; basisIndex < projection.Length; basisIndex++)
            {
                var coefficient = projection[basisIndex];
                if (Math.Abs(coefficient) <= 1e-15d) continue;
                SubtractScaled(hiddenNormals, basisNormals[basisIndex], coefficient);
                SubtractScaled(hiddenTangents, basisTangents[basisIndex], coefficient);
            }

            var targetEnergy = SquaredNorm(targetVertices) +
                               SquaredNorm(targetNormals) +
                               SquaredNorm(targetTangents);
            var hiddenEnergy = SquaredNorm(hiddenVertices) +
                               SquaredNorm(hiddenNormals) +
                               SquaredNorm(hiddenTangents);
            if (!IsFinite(hiddenEnergy)) return false;

            // Relative amplitude 1e-6 is below the mesh reconstruction tolerance,
            // while the absolute floor makes an exactly-zero authored difference
            // deterministic on meshes expressed at any normal avatar scale.
            var negligibleEnergy = Math.Max(1e-24d, targetEnergy * 1e-12d);
            return hiddenEnergy > negligibleEnergy;
        }

        private static double[] ProjectOntoComplement(
            Vector3[][] basis,
            Vector3[] target,
            out Vector3[] residual)
        {
            var coefficientCount = basis.Length;
            var coefficients = new double[coefficientCount];
            residual = (Vector3[])target.Clone();
            if (coefficientCount == 0) return coefficients;

            var maximumNormSquared = 0d;
            for (var basisIndex = 0; basisIndex < coefficientCount; basisIndex++)
                maximumNormSquared = Math.Max(
                    maximumNormSquared, Dot(basis[basisIndex], basis[basisIndex]));
            if (!IsFinite(maximumNormSquared) || maximumNormSquared <= 1e-24d)
                return coefficients;

            var rankThreshold = Math.Max(1e-24d, maximumNormSquared * 1e-12d);
            var orthogonal = new List<OrthogonalBasisAxis>(coefficientCount);
            for (var basisIndex = 0; basisIndex < coefficientCount; basisIndex++)
            {
                var axis = (Vector3[])basis[basisIndex].Clone();
                var sourceCoefficients = new double[coefficientCount];
                sourceCoefficients[basisIndex] = 1d;

                // A second modified Gram-Schmidt pass keeps nearly-collinear
                // blendshape axes stable without inverting a squared-condition
                // Gram matrix.
                for (var pass = 0; pass < 2; pass++)
                foreach (var previous in orthogonal)
                {
                    var scale = Dot(axis, previous.vertices) / previous.normSquared;
                    if (!IsFinite(scale) || Math.Abs(scale) <= 1e-15d) continue;
                    SubtractScaled(axis, previous.vertices, scale);
                    for (var source = 0; source < coefficientCount; source++)
                        sourceCoefficients[source] -= scale * previous.sourceCoefficients[source];
                }

                var normSquared = Dot(axis, axis);
                if (!IsFinite(normSquared) || normSquared <= rankThreshold) continue;
                orthogonal.Add(new OrthogonalBasisAxis
                {
                    vertices = axis,
                    sourceCoefficients = sourceCoefficients,
                    normSquared = normSquared
                });
            }

            // Project in the orthogonalized span. Repeat once to clean up the
            // float-array subtraction error, while accumulating coefficients in
            // the original basis so the identical transform can be applied to
            // authored normal and tangent deltas.
            for (var pass = 0; pass < 2; pass++)
            foreach (var axis in orthogonal)
            {
                var scale = Dot(residual, axis.vertices) / axis.normSquared;
                if (!IsFinite(scale) || Math.Abs(scale) <= 1e-15d) continue;
                SubtractScaled(residual, axis.vertices, scale);
                for (var source = 0; source < coefficientCount; source++)
                    coefficients[source] += scale * axis.sourceCoefficients[source];
            }
            return coefficients;
        }

        private static Vector3[] Difference(Vector3[] left, Vector3[] right)
        {
            if (left.Length != right.Length)
                throw new ArgumentException("Blendshape delta arrays must have matching lengths.");
            var output = new Vector3[left.Length];
            for (var i = 0; i < output.Length; i++) output[i] = left[i] - right[i];
            return output;
        }

        private static double Dot(Vector3[] left, Vector3[] right)
        {
            if (left.Length != right.Length)
                throw new ArgumentException("Blendshape delta arrays must have matching lengths.");
            var value = 0d;
            for (var i = 0; i < left.Length; i++)
            {
                value += (double)left[i].x * right[i].x;
                value += (double)left[i].y * right[i].y;
                value += (double)left[i].z * right[i].z;
            }
            return value;
        }

        private static double SquaredNorm(Vector3[] values)
        {
            return Dot(values, values);
        }

        private static void SubtractScaled(Vector3[] target, Vector3[] basis, double scale)
        {
            if (target.Length != basis.Length)
                throw new ArgumentException("Blendshape delta arrays must have matching lengths.");
            var scalar = (float)scale;
            for (var i = 0; i < target.Length; i++) target[i] -= basis[i] * scalar;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
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
