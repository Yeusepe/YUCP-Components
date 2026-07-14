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

        /// <summary>
        /// One target-mesh blendshape contribution to a linked pose. The endpoint
        /// weight is expressed in Unity blendshape units, so a conventional full
        /// pose uses 100.
        /// </summary>
        public readonly struct BlendShapePoseElement
        {
            public readonly int blendShapeIndex;
            public readonly float endpointWeight;

            public BlendShapePoseElement(int blendShapeIndex, float endpointWeight)
            {
                this.blendShapeIndex = blendShapeIndex;
                this.endpointWeight = endpointWeight;
            }
        }

        /// <summary>
        /// An immutable composite pose on a linked target mesh. A default or empty
        /// value represents a missing mapping. Elements are sorted by blendshape
        /// index so identical mappings always produce identical floating-point
        /// accumulation order and generated assets.
        /// </summary>
        public readonly struct BlendShapePoseInput
        {
            private readonly BlendShapePoseElement[] elements;

            public int Count => elements?.Length ?? 0;
            public bool isMapped => Count > 0;
            public BlendShapePoseElement this[int index] => elements[index];

            public BlendShapePoseInput(params BlendShapePoseElement[] elements)
            {
                if (elements == null || elements.Length == 0)
                {
                    this.elements = null;
                    return;
                }

                this.elements = (BlendShapePoseElement[])elements.Clone();
                Array.Sort(this.elements, ComparePoseElements);
            }

            private static int ComparePoseElements(
                BlendShapePoseElement left,
                BlendShapePoseElement right)
            {
                var byIndex = left.blendShapeIndex.CompareTo(right.blendShapeIndex);
                if (byIndex != 0) return byIndex;
                return left.endpointWeight.CompareTo(right.endpointWeight);
            }
        }

        /// <summary>
        /// One column of the primary calibration basis mapped onto a linked mesh.
        /// Columns must remain in the same order as the supplied primary
        /// coefficient matrix. A missing pose is valid and contributes zero
        /// target geometry while preserving that column alignment.
        /// </summary>
        public readonly struct LinkedBasisPoseInput
        {
            public readonly AdvancedVisemeArticulator articulator;
            public readonly int direction;
            public readonly BlendShapePoseInput pose;

            public LinkedBasisPoseInput(
                AdvancedVisemeArticulator articulator,
                int direction,
                BlendShapePoseInput pose)
            {
                this.articulator = articulator;
                this.direction = direction;
                this.pose = pose;
            }
        }

        public sealed class Result
        {
            public Mesh mesh;
            public float[,] coefficients;
            public float[,] independentlyFittedCoefficients;
            /// <summary>
            /// Signed coefficients of the authored residual projected back into
            /// the measured basis. Rows are Oculus visemes and columns retain the
            /// exact calibration-basis order. Runtime ownership removes
            /// U * diag(g) * A^T * p without creating one conflict morph per
            /// viseme.
            /// </summary>
            public float[,] ownershipProjectionCoefficients;
            /// <summary>
            /// Optional +U carrier per retained basis column. It represents the
            /// correction produced by negative projection coefficients and is
            /// always driven with a nonnegative blendshape weight.
            /// </summary>
            public string[] ownershipCarrierBlendShapeNames;
            /// <summary>
            /// Positive geometry multiplier baked into the +U carrier.
            /// </summary>
            public float[] ownershipCarrierScales;
            /// <summary>
            /// Optional -U carrier per retained basis column. Splitting the two
            /// geometric signs keeps every final VRChat blendshape curve in the
            /// supported 0..100 range while preserving the signed correction.
            /// </summary>
            public string[] ownershipNegativeCarrierBlendShapeNames;
            /// <summary>
            /// Positive magnitude baked into the -U carrier.
            /// </summary>
            public float[] ownershipNegativeCarrierScales;
            /// <summary>
            /// Build-only -U geometry for each calibration basis column. Signed
            /// articulators use these shapes with a nonnegative magnitude rather
            /// than relying on a final negative blendshape weight.
            /// </summary>
            public string[] basisNegativeBlendShapeNames;
            /// <summary>
            /// Observable columns with finite nonzero source geometry, including
            /// rank-dependent columns which do not emit their own carrier.
            /// </summary>
            public bool[] ownershipNonZeroSelectedColumns;
            /// <summary>
            /// Per carrier/input-column dependency components. A true entry at
            /// [j,k] means authority for carrier j must also respect input axis k
            /// because those directions are not numerically identifiable.
            /// Independent columns contain only their diagonal entry.
            /// </summary>
            public bool[,] ownershipAuthorityGroups;
            /// <summary>
            /// True when the selected measured basis is rank deficient. In that
            /// uncommon case per-column ownership is not identifiable, so the
            /// Animator conservatively shares the weakest participating gain.
            /// </summary>
            public bool ownershipBasisRankDeficient;
            /// <summary>
            /// One entry per coefficient column. A true entry means that the
            /// corresponding tracking axis is observable and may therefore
            /// replace residual geometry in the same geometric subspace.
            /// </summary>
            public bool[] observableBasisColumns;
            public AdvancedVisemeArticulator[] basisArticulators;
            public int[] basisDirections;
            public string[] residualBlendShapeNames;
            public string hiddenPhoneResidualBlendShapeName;
            public string hiddenPhoneResidualNegativeBlendShapeName;
            public string generatedNamePrefix;
            public float fitRms;
            public float fitMaximum;
            public string error;
            public PoseBasisAxis[] poseBasisAxes;
            public bool success => mesh != null && string.IsNullOrEmpty(error);
        }

        private sealed class OrthogonalBasisAxis
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector3[] tangents;
            public double[] sourceCoefficients;
            public double normSquared;
        }

        private readonly struct GeometryMetric
        {
            public readonly double vertices;
            public readonly double normals;
            public readonly double tangents;

            public GeometryMetric(double vertices, double normals, double tangents)
            {
                this.vertices = vertices;
                this.normals = normals;
                this.tangents = tangents;
            }
        }

        public static Result Build(Mesh source, int[] visemeBlendShapeIndices, IReadOnlyList<BasisInput> basis)
        {
            return Build(source, visemeBlendShapeIndices, basis, null);
        }

        public static Result Build(
            Mesh source,
            int[] visemeBlendShapeIndices,
            IReadOnlyList<BasisInput> basis,
            IReadOnlyList<bool> observableColumns)
        {
            return Build(
                source, visemeBlendShapeIndices, basis, observableColumns,
                "YUCP_AVR");
        }

        public static Result Build(
            Mesh source,
            int[] visemeBlendShapeIndices,
            IReadOnlyList<BasisInput> basis,
            IReadOnlyList<bool> observableColumns,
            string generatedNamePrefix)
        {
            if (!TryValidateInputs(source, visemeBlendShapeIndices, basis?.Count ?? 0,
                    "At least one articulator blendshape is required for calibration.", out var validation))
                return validation;

            if (!TryResolveObservableColumns(
                    observableColumns, basis.Count, out var observableMask, out var maskError))
                return Error(maskError);

            var vertexCount = source.vertexCount;
            var basisVertices = new Vector3[basis.Count][];
            var basisNormals = new Vector3[basis.Count][];
            var basisTangents = new Vector3[basis.Count][];
            var basisArticulators = new AdvancedVisemeArticulator[basis.Count];
            var basisDirections = new int[basis.Count];
            for (var j = 0; j < basis.Count; j++)
            {
                if (!TryReadAtWeight100(source, basis[j].blendShapeIndex, vertexCount,
                        out basisVertices[j], out basisNormals[j], out basisTangents[j], out var error))
                {
                    return Error(error);
                }
                basisArticulators[j] = basis[j].articulator;
                basisDirections[j] = 1;
            }

            return BuildResidualResult(
                source,
                visemeBlendShapeIndices,
                basisVertices,
                basisNormals,
                basisTangents,
                null,
                observableMask,
                basisArticulators,
                basisDirections,
                generatedNamePrefix);
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
            return BuildFromPoses(source, visemeBlendShapeIndices, basis, null);
        }

        public static Result BuildFromPoses(
            Mesh source,
            int[] visemeBlendShapeIndices,
            IReadOnlyList<PoseBasisInput> basis,
            IReadOnlyList<bool> observableColumns)
        {
            return BuildFromPoses(
                source, visemeBlendShapeIndices, basis, observableColumns,
                "YUCP_AVR");
        }

        public static Result BuildFromPoses(
            Mesh source,
            int[] visemeBlendShapeIndices,
            IReadOnlyList<PoseBasisInput> basis,
            IReadOnlyList<bool> observableColumns,
            string generatedNamePrefix)
        {
            if (!TryValidateInputs(source, visemeBlendShapeIndices, basis?.Count ?? 0,
                    "At least one face-tracking pose is required for calibration.", out var validation))
                return validation;

            if (!TryResolveObservableColumns(
                    observableColumns, basis.Count, out var observableMask, out var maskError))
                return Error(maskError);

            var vertexCount = source.vertexCount;
            var basisVertices = new Vector3[basis.Count][];
            var basisNormals = new Vector3[basis.Count][];
            var basisTangents = new Vector3[basis.Count][];
            var axes = new PoseBasisAxis[basis.Count];
            var basisArticulators = new AdvancedVisemeArticulator[basis.Count];
            var basisDirections = new int[basis.Count];
            var activeBindingOwners = new Dictionary<string, int>(StringComparer.Ordinal);
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
                        out var activeBlendShapeIndices,
                        out var error))
                    return Error(error);

                foreach (var blendShapeIndex in activeBlendShapeIndices)
                {
                    var bindingKey = input.rendererPath + "\n" + blendShapeIndex;
                    if (!activeBindingOwners.TryGetValue(bindingKey, out var previousBasis))
                    {
                        activeBindingOwners.Add(bindingKey, j);
                        continue;
                    }

                    var previous = basis[previousBasis];
                    var blendShapeName = source.GetBlendShapeName(blendShapeIndex);
                    return Error(
                        $"Face-tracking pose '{input.clip.name}' ({input.articulator}, " +
                        $"{DirectionLabel(input.direction)}) and pose '{previous.clip.name}' " +
                        $"({previous.articulator}, {DirectionLabel(previous.direction)}) share active " +
                        $"blendshape '{blendShapeName}' at renderer '{input.rendererPath}'. " +
                        "Each blendshape may belong to only one calibrated pose ray; simultaneous " +
                        "rays would exceed VRChat's 0..100 final blendshape range.");
                }

                axes[j] = new PoseBasisAxis(input);
                basisArticulators[j] = input.articulator;
                basisDirections[j] = input.direction;
            }

            return BuildResidualResult(
                source,
                visemeBlendShapeIndices,
                basisVertices,
                basisNormals,
                basisTangents,
                axes,
                observableMask,
                basisArticulators,
                basisDirections,
                generatedNamePrefix);
        }

        /// <summary>
        /// Calibrates a linked target while retaining the primary calibration's
        /// coefficient-column semantics. Each target viseme and basis column may
        /// be a composite of several target blendshapes. Missing visemes are
        /// skipped; missing basis poses contribute zero geometry without changing
        /// coefficient alignment.
        ///
        /// <para>
        /// <paramref name="referenceCoefficients"/> must be the matrix used by the
        /// primary face. Those coefficients construct the production residual so
        /// copied primary articulation plus the target residual exactly restores
        /// the target's authored viseme. A separate target-only box-constrained
        /// least-squares fit is exposed through
        /// <see cref="Result.independentlyFittedCoefficients"/> for diagnostics
        /// and never changes the production residual.
        /// </para>
        /// </summary>
        public static Result BuildLinkedTarget(
            Mesh target,
            IReadOnlyList<BlendShapePoseInput> targetVisemePoses,
            IReadOnlyList<LinkedBasisPoseInput> targetBasisPoses,
            float[,] referenceCoefficients,
            string generatedNamePrefix)
        {
            return BuildLinkedTarget(
                target,
                targetVisemePoses,
                targetBasisPoses,
                referenceCoefficients,
                generatedNamePrefix,
                null);
        }

        public static Result BuildLinkedTarget(
            Mesh target,
            IReadOnlyList<BlendShapePoseInput> targetVisemePoses,
            IReadOnlyList<LinkedBasisPoseInput> targetBasisPoses,
            float[,] referenceCoefficients,
            string generatedNamePrefix,
            IReadOnlyList<bool> observableColumns)
        {
            if (target == null) return Error("Linked target mesh is missing.");
            if (targetVisemePoses == null ||
                targetVisemePoses.Count != VisemeReconstructionProfile.VisemeCount)
            {
                return Error("Exactly 15 linked Oculus viseme poses are required; use an empty pose for a missing mapping.");
            }

            var basisCount = targetBasisPoses?.Count ?? 0;
            if (!TryValidateReferenceCoefficients(
                    referenceCoefficients, basisCount, out var coefficientError))
                return Error(coefficientError);
            if (!TryResolveObservableColumns(
                    observableColumns, basisCount, out var observableMask, out var maskError))
                return Error(maskError);

            var vertexCount = target.vertexCount;
            var basisVertices = new Vector3[basisCount][];
            var basisNormals = new Vector3[basisCount][];
            var basisTangents = new Vector3[basisCount][];
            var basisArticulators = new AdvancedVisemeArticulator[basisCount];
            var basisDirections = new int[basisCount];
            for (var basisIndex = 0; basisIndex < basisCount; basisIndex++)
            {
                var input = targetBasisPoses[basisIndex];
                if (input.direction != -1 && input.direction != 1)
                {
                    return Error(
                        $"Linked basis column {basisIndex} for '{input.articulator}' has direction " +
                        $"{input.direction}; direction must be +1 or -1.");
                }

                basisArticulators[basisIndex] = input.articulator;
                basisDirections[basisIndex] = input.direction;

                if (!input.pose.isMapped)
                {
                    basisVertices[basisIndex] = new Vector3[vertexCount];
                    basisNormals[basisIndex] = new Vector3[vertexCount];
                    basisTangents[basisIndex] = new Vector3[vertexCount];
                    continue;
                }

                if (!TryReadBlendShapePose(
                        target,
                        input.pose,
                        vertexCount,
                        out basisVertices[basisIndex],
                        out basisNormals[basisIndex],
                        out basisTangents[basisIndex],
                        out var error))
                {
                    return Error(
                        $"Linked basis column {basisIndex} for '{input.articulator}' is invalid: {error}");
                }
            }

            var targetVertices = new Vector3[VisemeReconstructionProfile.VisemeCount][];
            var targetNormals = new Vector3[VisemeReconstructionProfile.VisemeCount][];
            var targetTangents = new Vector3[VisemeReconstructionProfile.VisemeCount][];
            for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
            {
                var pose = targetVisemePoses[viseme];
                if (!pose.isMapped) continue;
                if (!TryReadBlendShapePose(
                        target,
                        pose,
                        vertexCount,
                        out targetVertices[viseme],
                        out targetNormals[viseme],
                        out targetTangents[viseme],
                        out var error))
                {
                    return Error(
                        $"Linked viseme '{VisemeReconstructionProfile.VisemeNames[viseme]}' is invalid: {error}");
                }
            }

            return BuildLinkedResidualResult(
                target,
                targetVertices,
                targetNormals,
                targetTangents,
                basisVertices,
                basisNormals,
                basisTangents,
                referenceCoefficients,
                generatedNamePrefix,
                observableMask,
                basisArticulators,
                basisDirections);
        }

        /// <summary>
        /// Convenience overload for one-to-one target blendshape mappings. A
        /// negative viseme index represents a missing target viseme.
        /// </summary>
        public static Result BuildLinkedTarget(
            Mesh target,
            int[] targetVisemeBlendShapeIndices,
            IReadOnlyList<BasisInput> targetBasis,
            float[,] referenceCoefficients,
            string generatedNamePrefix)
        {
            return BuildLinkedTarget(
                target,
                targetVisemeBlendShapeIndices,
                targetBasis,
                referenceCoefficients,
                generatedNamePrefix,
                null);
        }

        public static Result BuildLinkedTarget(
            Mesh target,
            int[] targetVisemeBlendShapeIndices,
            IReadOnlyList<BasisInput> targetBasis,
            float[,] referenceCoefficients,
            string generatedNamePrefix,
            IReadOnlyList<bool> observableColumns)
        {
            if (targetVisemeBlendShapeIndices == null ||
                targetVisemeBlendShapeIndices.Length != VisemeReconstructionProfile.VisemeCount)
            {
                return Error("Exactly 15 linked Oculus viseme blendshape indices are required.");
            }

            var visemes = new BlendShapePoseInput[VisemeReconstructionProfile.VisemeCount];
            for (var viseme = 0; viseme < visemes.Length; viseme++)
            {
                var index = targetVisemeBlendShapeIndices[viseme];
                if (index < 0) continue;
                visemes[viseme] = new BlendShapePoseInput(
                    new BlendShapePoseElement(index, 100f));
            }

            var basisCount = targetBasis?.Count ?? 0;
            var linkedBasis = new LinkedBasisPoseInput[basisCount];
            for (var basisIndex = 0; basisIndex < basisCount; basisIndex++)
            {
                var input = targetBasis[basisIndex];
                linkedBasis[basisIndex] = new LinkedBasisPoseInput(
                    input.articulator,
                    1,
                    new BlendShapePoseInput(
                        new BlendShapePoseElement(input.blendShapeIndex, 100f)));
            }

            return BuildLinkedTarget(
                target,
                visemes,
                linkedBasis,
                referenceCoefficients,
                generatedNamePrefix,
                observableColumns);
        }

        private static Result BuildLinkedResidualResult(
            Mesh target,
            Vector3[][] targetVertices,
            Vector3[][] targetNormals,
            Vector3[][] targetTangents,
            Vector3[][] basisVertices,
            Vector3[][] basisNormals,
            Vector3[][] basisTangents,
            float[,] referenceCoefficients,
            string requestedNamePrefix,
            bool[] observableColumns,
            AdvancedVisemeArticulator[] basisArticulators,
            int[] basisDirections)
        {
            var vertexCount = target.vertexCount;
            var basisCount = basisVertices.Length;
            var generatedNamePrefix = ResolveUniqueLinkedNamePrefix(
                target, requestedNamePrefix);
            var result = new Result
            {
                coefficients = CloneMatrix(referenceCoefficients),
                independentlyFittedCoefficients = new float[
                    VisemeReconstructionProfile.VisemeCount, basisCount],
                ownershipProjectionCoefficients = new float[
                    VisemeReconstructionProfile.VisemeCount, basisCount],
                ownershipCarrierBlendShapeNames = new string[basisCount],
                ownershipCarrierScales = new float[basisCount],
                ownershipNegativeCarrierBlendShapeNames = new string[basisCount],
                ownershipNegativeCarrierScales = new float[basisCount],
                basisNegativeBlendShapeNames = new string[basisCount],
                ownershipNonZeroSelectedColumns = new bool[basisCount],
                observableBasisColumns = (bool[])observableColumns.Clone(),
                basisArticulators = (AdvancedVisemeArticulator[])basisArticulators.Clone(),
                basisDirections = (int[])basisDirections.Clone(),
                residualBlendShapeNames = new string[
                    VisemeReconstructionProfile.VisemeCount],
                generatedNamePrefix = generatedNamePrefix,
                poseBasisAxes = Array.Empty<PoseBasisAxis>()
            };

            var clone = UnityEngine.Object.Instantiate(target);
            clone.name = target.name + "_" + generatedNamePrefix;
            AddNegativeBasisShapes(
                clone, generatedNamePrefix,
                basisVertices, basisNormals, basisTangents,
                basisArticulators, basisDirections,
                result.basisNegativeBlendShapeNames);

            double squaredResidual = 0d;
            var residualSamples = 0L;
            var maxResidual = 0f;
            Vector3[] ppVertices = null;
            Vector3[] ppNormals = null;
            Vector3[] ppTangents = null;
            Vector3[] nnVertices = null;
            Vector3[] nnNormals = null;
            Vector3[] nnTangents = null;
            var ownershipRank = -1;
            var ownershipSelectedColumns = -1;

            for (var viseme = 0;
                 viseme < VisemeReconstructionProfile.VisemeCount;
                 viseme++)
            {
                var authoredVertices = targetVertices[viseme];
                if (authoredVertices == null) continue;
                var authoredNormals = targetNormals[viseme];
                var authoredTangents = targetTangents[viseme];

                if (viseme == 1)
                {
                    ppVertices = authoredVertices;
                    ppNormals = authoredNormals;
                    ppTangents = authoredTangents;
                }
                else if (viseme == 8)
                {
                    nnVertices = authoredVertices;
                    nnNormals = authoredNormals;
                    nnTangents = authoredTangents;
                }

                var fitted = SolveBoxConstrainedLeastSquares(
                    basisVertices, authoredVertices);
                for (var basisIndex = 0; basisIndex < fitted.Length; basisIndex++)
                    result.independentlyFittedCoefficients[viseme, basisIndex] =
                        fitted[basisIndex];

                var residualVertices = (Vector3[])authoredVertices.Clone();
                var residualNormals = (Vector3[])authoredNormals.Clone();
                var residualTangents = (Vector3[])authoredTangents.Clone();
                for (var basisIndex = 0; basisIndex < basisCount; basisIndex++)
                {
                    var coefficient = referenceCoefficients[viseme, basisIndex];
                    if (coefficient == 0f) continue;
                    SubtractScaled(
                        residualVertices, basisVertices[basisIndex], coefficient);
                    SubtractScaled(
                        residualNormals, basisNormals[basisIndex], coefficient);
                    SubtractScaled(
                        residualTangents, basisTangents[basisIndex], coefficient);
                }

                if (!AreFinite(residualVertices) || !AreFinite(residualNormals) ||
                    !AreFinite(residualTangents))
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                    result.error =
                        $"Linked viseme '{VisemeReconstructionProfile.VisemeNames[viseme]}' " +
                        "produced a non-finite residual.";
                    return result;
                }

                for (var vertex = 0; vertex < vertexCount; vertex++)
                {
                    var magnitude = residualVertices[vertex].magnitude;
                    squaredResidual += residualVertices[vertex].sqrMagnitude;
                    residualSamples++;
                    if (magnitude > maxResidual) maxResidual = magnitude;
                }

                var residualName =
                    $"{generatedNamePrefix}_Residual_{VisemeReconstructionProfile.VisemeNames[viseme]}";
                clone.AddBlendShapeFrame(
                    residualName,
                    100f,
                    residualVertices,
                    residualNormals,
                    residualTangents);
                result.residualBlendShapeNames[viseme] = residualName;

                var projection = ProjectGeometryOntoComplement(
                    basisVertices,
                    basisNormals,
                    basisTangents,
                    residualVertices,
                    residualNormals,
                    residualTangents,
                    observableColumns,
                    out _, out _, out _,
                    out var projectionRank,
                    out var selectedColumnCount,
                    out var authorityGroups);
                ownershipRank = projectionRank;
                ownershipSelectedColumns = selectedColumnCount;
                result.ownershipAuthorityGroups = authorityGroups;
                for (var basisIndex = 0; basisIndex < projection.Length; basisIndex++)
                    result.ownershipProjectionCoefficients[viseme, basisIndex] =
                        (float)projection[basisIndex];

                if (!AreFinite(result.ownershipProjectionCoefficients, viseme))
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                    result.error =
                        $"Linked viseme '{VisemeReconstructionProfile.VisemeNames[viseme]}' " +
                        "produced a non-finite ownership projection.";
                    return result;
                }
            }

            result.ownershipBasisRankDeficient = ownershipRank >= 0 &&
                                                   ownershipRank < ownershipSelectedColumns;
            AddOwnershipCarriers(
                clone,
                generatedNamePrefix,
                basisVertices,
                basisNormals,
                basisTangents,
                observableColumns,
                basisArticulators,
                basisDirections,
                result.ownershipProjectionCoefficients,
                result.ownershipCarrierBlendShapeNames,
                result.ownershipCarrierScales,
                result.ownershipNegativeCarrierBlendShapeNames,
                result.ownershipNegativeCarrierScales,
                result.ownershipNonZeroSelectedColumns);

            if (TryBuildHiddenPhoneResidual(
                    basisVertices,
                    basisNormals,
                    basisTangents,
                    ppVertices,
                    ppNormals,
                    ppTangents,
                    nnVertices,
                    nnNormals,
                    nnTangents,
                    out var hiddenVertices,
                    out var hiddenNormals,
                    out var hiddenTangents))
            {
                var hiddenName = generatedNamePrefix + "_Hidden_PP_Minus_nn";
                var hiddenNegativeName = hiddenName + "_Negative";
                clone.AddBlendShapeFrame(
                    hiddenName,
                    100f,
                    hiddenVertices,
                    hiddenNormals,
                    hiddenTangents);
                clone.AddBlendShapeFrame(
                    hiddenNegativeName,
                    100f,
                    ScaledCopy(hiddenVertices, -1f),
                    ScaledCopy(hiddenNormals, -1f),
                    ScaledCopy(hiddenTangents, -1f));
                result.hiddenPhoneResidualBlendShapeName = hiddenName;
                result.hiddenPhoneResidualNegativeBlendShapeName =
                    hiddenNegativeName;
            }

            result.mesh = clone;
            result.fitRms = residualSamples > 0
                ? Mathf.Sqrt((float)(squaredResidual / residualSamples))
                : 0f;
            result.fitMaximum = maxResidual;
            return result;
        }

        private static bool TryValidateReferenceCoefficients(
            float[,] coefficients,
            int basisCount,
            out string error)
        {
            error = null;
            if (coefficients == null)
            {
                error = "Primary calibration coefficients are missing.";
                return false;
            }
            if (coefficients.GetLength(0) !=
                    VisemeReconstructionProfile.VisemeCount ||
                coefficients.GetLength(1) != basisCount)
            {
                error =
                    $"Primary calibration coefficients must be " +
                    $"{VisemeReconstructionProfile.VisemeCount}x{basisCount}.";
                return false;
            }

            for (var viseme = 0;
                 viseme < VisemeReconstructionProfile.VisemeCount;
                 viseme++)
            for (var basis = 0; basis < basisCount; basis++)
            {
                var value = coefficients[viseme, basis];
                if (!IsFinite(value) || value < 0f || value > 1f)
                {
                    error =
                        $"Primary calibration coefficient [{viseme},{basis}] " +
                        $"must be finite and between zero and one, but was {value:G9}.";
                    return false;
                }
            }
            return true;
        }

        private static float[,] CloneMatrix(float[,] source)
        {
            return (float[,])source.Clone();
        }

        private static bool TryReadBlendShapePose(
            Mesh mesh,
            BlendShapePoseInput pose,
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
            if (!pose.isMapped)
            {
                error = "the composite pose is missing.";
                return false;
            }

            var usedBlendShapes = new HashSet<int>();
            for (var elementIndex = 0;
                 elementIndex < pose.Count;
                 elementIndex++)
            {
                var element = pose[elementIndex];
                if (element.blendShapeIndex < 0 ||
                    element.blendShapeIndex >= mesh.blendShapeCount)
                {
                    error =
                        $"blendshape index {element.blendShapeIndex} is invalid for mesh '{mesh.name}'.";
                    return false;
                }
                if (!usedBlendShapes.Add(element.blendShapeIndex))
                {
                    error =
                        $"blendshape '{mesh.GetBlendShapeName(element.blendShapeIndex)}' is mapped more than once.";
                    return false;
                }
                if (!IsFinite(element.endpointWeight) ||
                    element.endpointWeight <= 1e-6f ||
                    element.endpointWeight > 100.00001f)
                {
                    error =
                        $"blendshape '{mesh.GetBlendShapeName(element.blendShapeIndex)}' has invalid endpoint " +
                        $"weight {element.endpointWeight:G9}; mapped endpoints must remain in VRChat's 0..100 range.";
                    return false;
                }

                if (!TryReadLinearBlendShapeAtWeight(
                        mesh,
                        element.blendShapeIndex,
                        element.endpointWeight,
                        vertexCount,
                        out var elementVertices,
                        out var elementNormals,
                        out var elementTangents,
                        out error))
                {
                    error =
                        $"blendshape '{mesh.GetBlendShapeName(element.blendShapeIndex)}' " +
                        $"cannot form a linked composite pose: {error}";
                    return false;
                }

                Add(vertices, elementVertices);
                Add(normals, elementNormals);
                Add(tangents, elementTangents);
            }

            if (!AreFinite(vertices) || !AreFinite(normals) ||
                !AreFinite(tangents))
            {
                error = "the composite pose produces non-finite geometry.";
                return false;
            }
            return true;
        }

        private static string ResolveUniqueLinkedNamePrefix(
            Mesh target,
            string requestedPrefix)
        {
            const string standardPrefix = "YUCP_AVR_Linked_";
            var token = SanitizeLinkedNameToken(requestedPrefix);
            if (string.IsNullOrEmpty(token)) token = "Target";
            var root = token.StartsWith(standardPrefix, StringComparison.Ordinal)
                ? token
                : standardPrefix + token;
            var candidate = root;
            var suffix = 2;
            while (HasBlendShapeNamespace(target, candidate))
                candidate = root + "_" + suffix++;
            return candidate;
        }

        private static string SanitizeLinkedNameToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var source = value.Trim();
            var characters = new char[source.Length];
            var count = 0;
            var previousUnderscore = false;
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                var accepted = char.IsLetterOrDigit(character) ||
                               character == '-' || character == '_';
                var output = accepted ? character : '_';
                if (output == '_' && previousUnderscore) continue;
                characters[count++] = output;
                previousUnderscore = output == '_';
            }
            while (count > 0 && characters[count - 1] == '_') count--;
            return count == 0 ? string.Empty : new string(characters, 0, count);
        }

        private static bool HasBlendShapeNamespace(Mesh mesh, string prefix)
        {
            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                var name = mesh.GetBlendShapeName(index);
                if (string.Equals(name, prefix, StringComparison.Ordinal) ||
                    name.StartsWith(prefix + "_", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static Result BuildResidualResult(
            Mesh source,
            int[] visemeBlendShapeIndices,
            Vector3[][] basisVertices,
            Vector3[][] basisNormals,
            Vector3[][] basisTangents,
            PoseBasisAxis[] poseBasisAxes,
            bool[] observableColumns,
            AdvancedVisemeArticulator[] basisArticulators,
            int[] basisDirections,
            string requestedNamePrefix)
        {
            var generatedNamePrefix = ResolveUniqueLinkedNamePrefix(
                source, string.IsNullOrWhiteSpace(requestedNamePrefix)
                    ? "YUCP_AVR"
                    : requestedNamePrefix);
            var result = new Result
            {
                poseBasisAxes = poseBasisAxes,
                generatedNamePrefix = generatedNamePrefix,
                observableBasisColumns = (bool[])observableColumns.Clone(),
                basisArticulators = (AdvancedVisemeArticulator[])basisArticulators.Clone(),
                basisDirections = (int[])basisDirections.Clone()
            };
            var vertexCount = source.vertexCount;

            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name + "_YUCP_AVR";
            result.coefficients = new float[VisemeReconstructionProfile.VisemeCount, basisVertices.Length];
            result.ownershipProjectionCoefficients = new float[
                VisemeReconstructionProfile.VisemeCount, basisVertices.Length];
            result.ownershipCarrierBlendShapeNames = new string[basisVertices.Length];
            result.ownershipCarrierScales = new float[basisVertices.Length];
            result.ownershipNegativeCarrierBlendShapeNames = new string[basisVertices.Length];
            result.ownershipNegativeCarrierScales = new float[basisVertices.Length];
            result.basisNegativeBlendShapeNames = new string[basisVertices.Length];
            result.ownershipNonZeroSelectedColumns = new bool[basisVertices.Length];
            result.residualBlendShapeNames = new string[VisemeReconstructionProfile.VisemeCount];

            AddNegativeBasisShapes(
                clone, generatedNamePrefix,
                basisVertices, basisNormals, basisTangents,
                basisArticulators, basisDirections,
                result.basisNegativeBlendShapeNames);

            double squaredResidual = 0d;
            var residualSamples = 0L;
            var maxResidual = 0f;
            Vector3[] ppVertices = null;
            Vector3[] ppNormals = null;
            Vector3[] ppTangents = null;
            Vector3[] nnVertices = null;
            Vector3[] nnNormals = null;
            Vector3[] nnTangents = null;
            var ownershipRank = -1;
            var ownershipSelectedColumns = -1;

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

                var coefficients = SolveBoxConstrainedLeastSquares(
                    basisVertices, targetVertices);
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

                var residualName =
                    $"{generatedNamePrefix}_Residual_{VisemeReconstructionProfile.VisemeNames[i]}";
                clone.AddBlendShapeFrame(residualName, 100f, residualVertices, residualNormals, residualTangents);
                result.residualBlendShapeNames[i] = residualName;

                var projection = ProjectGeometryOntoComplement(
                    basisVertices,
                    basisNormals,
                    basisTangents,
                    residualVertices,
                    residualNormals,
                    residualTangents,
                    observableColumns,
                    out _, out _, out _,
                    out var projectionRank,
                    out var selectedColumnCount,
                    out var authorityGroups);
                ownershipRank = projectionRank;
                ownershipSelectedColumns = selectedColumnCount;
                result.ownershipAuthorityGroups = authorityGroups;
                for (var basisIndex = 0; basisIndex < projection.Length; basisIndex++)
                    result.ownershipProjectionCoefficients[i, basisIndex] =
                        (float)projection[basisIndex];

                if (!AreFinite(result.ownershipProjectionCoefficients, i))
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                    result.error = $"Viseme '{VisemeReconstructionProfile.VisemeNames[i]}' " +
                                   "produced a non-finite ownership projection.";
                    return result;
                }
            }

            result.ownershipBasisRankDeficient = ownershipRank >= 0 &&
                                                   ownershipRank < ownershipSelectedColumns;
            AddOwnershipCarriers(
                clone,
                generatedNamePrefix,
                basisVertices,
                basisNormals,
                basisTangents,
                observableColumns,
                basisArticulators,
                basisDirections,
                result.ownershipProjectionCoefficients,
                result.ownershipCarrierBlendShapeNames,
                result.ownershipCarrierScales,
                result.ownershipNegativeCarrierBlendShapeNames,
                result.ownershipNegativeCarrierScales,
                result.ownershipNonZeroSelectedColumns);

            if (TryBuildHiddenPhoneResidual(
                    basisVertices, basisNormals, basisTangents,
                    ppVertices, ppNormals, ppTangents,
                    nnVertices, nnNormals, nnTangents,
                    out var hiddenVertices, out var hiddenNormals, out var hiddenTangents))
            {
                var hiddenName = generatedNamePrefix + "_Hidden_PP_Minus_nn";
                var hiddenNegativeName = hiddenName + "_Negative";
                clone.AddBlendShapeFrame(
                    hiddenName, 100f, hiddenVertices, hiddenNormals, hiddenTangents);
                clone.AddBlendShapeFrame(
                    hiddenNegativeName, 100f,
                    ScaledCopy(hiddenVertices, -1f),
                    ScaledCopy(hiddenNormals, -1f),
                    ScaledCopy(hiddenTangents, -1f));
                result.hiddenPhoneResidualBlendShapeName = hiddenName;
                result.hiddenPhoneResidualNegativeBlendShapeName =
                    hiddenNegativeName;
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

        private static bool TryResolveObservableColumns(
            IReadOnlyList<bool> requested,
            int basisCount,
            out bool[] resolved,
            out string error)
        {
            resolved = new bool[basisCount];
            error = null;
            if (requested == null)
            {
                for (var index = 0; index < basisCount; index++)
                    resolved[index] = true;
                return true;
            }

            if (requested.Count != basisCount)
            {
                error =
                    $"The observable-column mask must contain exactly {basisCount} entries, " +
                    $"but contains {requested.Count}.";
                return false;
            }

            for (var index = 0; index < basisCount; index++)
                resolved[index] = requested[index];
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
            out List<int> activeBlendShapeIndices,
            out string error)
        {
            vertices = new Vector3[vertexCount];
            normals = new Vector3[vertexCount];
            tangents = new Vector3[vertexCount];
            activeBlendShapeIndices = new List<int>();
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
                if (endpointWeight > 100.00001f)
                {
                    error = $"Face-tracking pose '{clip.name}' evaluates blendshape " +
                            $"'{blendShapeName}' to {endpointWeight:G6}. Pose endpoints must " +
                            "remain in VRChat's 0..100 final blendshape range.";
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
                activeBlendShapeIndices.Add(blendShapeIndex);
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

        private static string DirectionLabel(int direction)
        {
            return direction > 0 ? "positive endpoint" : "negative endpoint";
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

        private static bool AreFinite(float[,] values, int row)
        {
            if (values == null || row < 0 || row >= values.GetLength(0)) return false;
            for (var column = 0; column < values.GetLength(1); column++)
            {
                var value = values[row, column];
                if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            }
            return true;
        }

        private static void AddNegativeBasisShapes(
            Mesh clone,
            string generatedNamePrefix,
            IReadOnlyList<Vector3[]> basisVertices,
            IReadOnlyList<Vector3[]> basisNormals,
            IReadOnlyList<Vector3[]> basisTangents,
            IReadOnlyList<AdvancedVisemeArticulator> basisArticulators,
            IReadOnlyList<int> basisDirections,
            string[] outputNames)
        {
            if (clone == null || outputNames == null || basisVertices == null ||
                basisNormals == null || basisTangents == null) return;
            for (var column = 0; column < outputNames.Length; column++)
            {
                if (column >= basisVertices.Count || column >= basisNormals.Count ||
                    column >= basisTangents.Count) break;
                var energy = SquaredNorm(basisVertices[column]) +
                             SquaredNorm(basisNormals[column]) +
                             SquaredNorm(basisTangents[column]);
                if (!IsFinite(energy) || energy <= 1e-24d) continue;
                var articulator = column < basisArticulators.Count
                    ? basisArticulators[column].ToString()
                    : "Axis";
                var direction = column < basisDirections.Count &&
                                basisDirections[column] < 0
                    ? "Neg"
                    : "Pos";
                var name =
                    $"{generatedNamePrefix}_Basis_{column:D2}_{articulator}_{direction}_Inverse";
                clone.AddBlendShapeFrame(
                    name, 100f,
                    ScaledCopy(basisVertices[column], -1f),
                    ScaledCopy(basisNormals[column], -1f),
                    ScaledCopy(basisTangents[column], -1f));
                outputNames[column] = name;
            }
        }

        private static void AddOwnershipCarriers(
            Mesh clone,
            string generatedNamePrefix,
            Vector3[][] basisVertices,
            Vector3[][] basisNormals,
            Vector3[][] basisTangents,
            IReadOnlyList<bool> observableColumns,
            IReadOnlyList<AdvancedVisemeArticulator> basisArticulators,
            IReadOnlyList<int> basisDirections,
            float[,] projectionCoefficients,
            string[] positiveCarrierNames,
            float[] positiveCarrierScales,
            string[] negativeCarrierNames,
            float[] negativeCarrierScales,
            bool[] nonZeroSelectedColumns)
        {
            if (clone == null || projectionCoefficients == null ||
                positiveCarrierNames == null || positiveCarrierScales == null ||
                negativeCarrierNames == null || negativeCarrierScales == null ||
                nonZeroSelectedColumns == null ||
                positiveCarrierScales.Length != positiveCarrierNames.Length ||
                negativeCarrierNames.Length != positiveCarrierNames.Length ||
                negativeCarrierScales.Length != positiveCarrierNames.Length ||
                nonZeroSelectedColumns.Length != positiveCarrierNames.Length)
                return;
            for (var column = 0; column < positiveCarrierNames.Length; column++)
            {
                if (observableColumns != null &&
                    (column >= observableColumns.Count || !observableColumns[column]))
                    continue;

                var geometryEnergy = SquaredNorm(basisVertices[column]) +
                                     SquaredNorm(basisNormals[column]) +
                                     SquaredNorm(basisTangents[column]);
                if (!IsFinite(geometryEnergy) || geometryEnergy <= 1e-24d) continue;
                nonZeroSelectedColumns[column] = true;

                var positiveCoefficientMaximum = 0f;
                var negativeCoefficientMaximum = 0f;
                for (var viseme = 0; viseme < projectionCoefficients.GetLength(0); viseme++)
                {
                    var coefficient = projectionCoefficients[viseme, column];
                    if (coefficient > 0f)
                        positiveCoefficientMaximum = Mathf.Max(
                            positiveCoefficientMaximum, coefficient);
                    else if (coefficient < 0f)
                        negativeCoefficientMaximum = Mathf.Max(
                            negativeCoefficientMaximum, -coefficient);
                }

                var articulator = column < basisArticulators.Count
                    ? basisArticulators[column].ToString()
                    : "Axis";
                var direction = column < basisDirections.Count && basisDirections[column] < 0
                    ? "Neg"
                    : "Pos";

                // A < 0 requires +U geometry in -U*A*p.
                if (negativeCoefficientMaximum > 1e-7f)
                {
                    var name = $"{generatedNamePrefix}_Ownership_{column:D2}_{articulator}_{direction}_Add";
                    clone.AddBlendShapeFrame(
                        name,
                        100f,
                        ScaledCopy(basisVertices[column], negativeCoefficientMaximum),
                        ScaledCopy(basisNormals[column], negativeCoefficientMaximum),
                        ScaledCopy(basisTangents[column], negativeCoefficientMaximum));
                    positiveCarrierNames[column] = name;
                    positiveCarrierScales[column] = negativeCoefficientMaximum;
                }

                // A > 0 requires -U geometry. Keeping the animated weight
                // nonnegative avoids VRChat's final blendshape clamp.
                if (positiveCoefficientMaximum > 1e-7f)
                {
                    var name = $"{generatedNamePrefix}_Ownership_{column:D2}_{articulator}_{direction}_Subtract";
                    clone.AddBlendShapeFrame(
                        name,
                        100f,
                        ScaledCopy(basisVertices[column], -positiveCoefficientMaximum),
                        ScaledCopy(basisNormals[column], -positiveCoefficientMaximum),
                        ScaledCopy(basisTangents[column], -positiveCoefficientMaximum));
                    negativeCarrierNames[column] = name;
                    negativeCarrierScales[column] = positiveCoefficientMaximum;
                }
            }
        }

        private static Vector3[] ScaledCopy(Vector3[] source, float scale)
        {
            var output = new Vector3[source.Length];
            for (var index = 0; index < source.Length; index++)
                output[index] = source[index] * scale;
            return output;
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
            ProjectGeometryOntoComplement(
                basisVertices,
                basisNormals,
                basisTangents,
                targetVertices,
                targetNormals,
                targetTangents,
                null,
                out hiddenVertices,
                out hiddenNormals,
                out hiddenTangents,
                out _, out _, out _);

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

        /// <summary>
        /// Splits authored vertex, normal, and tangent deltas into the component
        /// inside the selected measured basis and its orthogonal complement.
        /// Vertex geometry dominates the metric; normalized normal/tangent energy
        /// contributes a small term so a valid zero-vertex shading ray is not
        /// silently treated as unobservable. Returned coefficients retain the
        /// original full column order, and unselected columns are exactly zero.
        /// </summary>
        private static double[] ProjectGeometryOntoComplement(
            Vector3[][] basisVertices,
            Vector3[][] basisNormals,
            Vector3[][] basisTangents,
            Vector3[] targetVertices,
            Vector3[] targetNormals,
            Vector3[] targetTangents,
            IReadOnlyList<bool> selectedColumns,
            out Vector3[] residualVertices,
            out Vector3[] residualNormals,
            out Vector3[] residualTangents,
            out int rank,
            out int selectedNonZeroColumns,
            out bool[,] authorityGroups)
        {
            var coefficientCount = basisVertices.Length;
            var coefficients = new double[coefficientCount];
            residualVertices = (Vector3[])targetVertices.Clone();
            residualNormals = (Vector3[])targetNormals.Clone();
            residualTangents = (Vector3[])targetTangents.Clone();
            rank = 0;
            selectedNonZeroColumns = 0;
            authorityGroups = new bool[coefficientCount, coefficientCount];
            if (coefficientCount == 0) return coefficients;

            var metric = BuildGeometryMetric(
                basisVertices, basisNormals, basisTangents, selectedColumns);
            var maximumNormSquared = 0d;
            var nonZeroSelected = new bool[coefficientCount];
            var parents = new int[coefficientCount];
            for (var index = 0; index < parents.Length; index++) parents[index] = index;

            int Find(int value)
            {
                while (parents[value] != value)
                {
                    parents[value] = parents[parents[value]];
                    value = parents[value];
                }
                return value;
            }

            void Union(int left, int right)
            {
                left = Find(left);
                right = Find(right);
                if (left != right) parents[right] = left;
            }

            for (var basisIndex = 0; basisIndex < coefficientCount; basisIndex++)
            {
                if (selectedColumns != null && !selectedColumns[basisIndex]) continue;
                var norm = DotGeometry(
                    basisVertices[basisIndex], basisNormals[basisIndex], basisTangents[basisIndex],
                    basisVertices[basisIndex], basisNormals[basisIndex], basisTangents[basisIndex],
                    metric);
                if (IsFinite(norm) && norm > 1e-24d)
                {
                    selectedNonZeroColumns++;
                    nonZeroSelected[basisIndex] = true;
                }
                maximumNormSquared = Math.Max(
                    maximumNormSquared, norm);
            }
            if (!IsFinite(maximumNormSquared) || maximumNormSquared <= 1e-24d)
                return coefficients;

            // A relative amplitude below 1e-4 is not a trustworthy independent
            // tracking coordinate. Treat it as a dependency instead of emitting
            // enormous opposing carrier deltas that rely on float cancellation.
            var rankThreshold = Math.Max(1e-24d, maximumNormSquared * 1e-8d);
            var orthogonal = new List<OrthogonalBasisAxis>(coefficientCount);
            for (var basisIndex = 0; basisIndex < coefficientCount; basisIndex++)
            {
                if (selectedColumns != null && !selectedColumns[basisIndex]) continue;
                var axisVertices = (Vector3[])basisVertices[basisIndex].Clone();
                var axisNormals = (Vector3[])basisNormals[basisIndex].Clone();
                var axisTangents = (Vector3[])basisTangents[basisIndex].Clone();
                var sourceCoefficients = new double[coefficientCount];
                sourceCoefficients[basisIndex] = 1d;

                // A second modified Gram-Schmidt pass keeps nearly-collinear
                // blendshape axes stable without inverting a squared-condition
                // Gram matrix.
                for (var pass = 0; pass < 2; pass++)
                foreach (var previous in orthogonal)
                {
                    var scale = DotGeometry(
                        axisVertices, axisNormals, axisTangents,
                        previous.vertices, previous.normals, previous.tangents,
                        metric) / previous.normSquared;
                    if (!IsFinite(scale) || Math.Abs(scale) <= 1e-15d) continue;
                    SubtractScaled(axisVertices, previous.vertices, scale);
                    SubtractScaled(axisNormals, previous.normals, scale);
                    SubtractScaled(axisTangents, previous.tangents, scale);
                    for (var source = 0; source < coefficientCount; source++)
                        sourceCoefficients[source] -= scale * previous.sourceCoefficients[source];
                }

                var normSquared = DotGeometry(
                    axisVertices, axisNormals, axisTangents,
                    axisVertices, axisNormals, axisTangents,
                    metric);
                if (!IsFinite(normSquared) || normSquared <= rankThreshold)
                {
                    if (nonZeroSelected[basisIndex])
                    {
                        for (var source = 0; source < coefficientCount; source++)
                        {
                            if (source == basisIndex || !nonZeroSelected[source]) continue;
                            if (Math.Abs(sourceCoefficients[source]) > 1e-8d)
                                Union(basisIndex, source);
                        }
                    }
                    continue;
                }
                orthogonal.Add(new OrthogonalBasisAxis
                {
                    vertices = axisVertices,
                    normals = axisNormals,
                    tangents = axisTangents,
                    sourceCoefficients = sourceCoefficients,
                    normSquared = normSquared
                });
            }
            rank = orthogonal.Count;
            for (var left = 0; left < coefficientCount; left++)
            for (var right = 0; right < coefficientCount; right++)
                authorityGroups[left, right] = nonZeroSelected[left] &&
                                                nonZeroSelected[right] &&
                                                Find(left) == Find(right);

            // Project in the orthogonalized span. Repeat once to clean up the
            // float-array subtraction error, while accumulating coefficients in
            // the original basis so the identical transform can be applied to
            // authored normal and tangent deltas.
            for (var pass = 0; pass < 2; pass++)
            foreach (var axis in orthogonal)
            {
                var scale = DotGeometry(
                    residualVertices, residualNormals, residualTangents,
                    axis.vertices, axis.normals, axis.tangents,
                    metric) / axis.normSquared;
                if (!IsFinite(scale) || Math.Abs(scale) <= 1e-15d) continue;
                SubtractScaled(residualVertices, axis.vertices, scale);
                SubtractScaled(residualNormals, axis.normals, scale);
                SubtractScaled(residualTangents, axis.tangents, scale);
                for (var source = 0; source < coefficientCount; source++)
                    coefficients[source] += scale * axis.sourceCoefficients[source];
            }
            return coefficients;
        }

        private static GeometryMetric BuildGeometryMetric(
            Vector3[][] basisVertices,
            Vector3[][] basisNormals,
            Vector3[][] basisTangents,
            IReadOnlyList<bool> selectedColumns)
        {
            var maximumVertices = 0d;
            var maximumNormals = 0d;
            var maximumTangents = 0d;
            for (var index = 0; index < basisVertices.Length; index++)
            {
                if (selectedColumns != null && !selectedColumns[index]) continue;
                maximumVertices = Math.Max(maximumVertices, SquaredNorm(basisVertices[index]));
                maximumNormals = Math.Max(maximumNormals, SquaredNorm(basisNormals[index]));
                maximumTangents = Math.Max(maximumTangents, SquaredNorm(basisTangents[index]));
            }

            const double epsilon = 1e-24d;
            const double shadingInfluence = 1e-4d;
            if (maximumVertices > epsilon)
            {
                return new GeometryMetric(
                    1d / maximumVertices,
                    maximumNormals > epsilon ? shadingInfluence / maximumNormals : 0d,
                    maximumTangents > epsilon ? shadingInfluence / maximumTangents : 0d);
            }
            if (maximumNormals > epsilon)
            {
                return new GeometryMetric(
                    0d,
                    1d / maximumNormals,
                    maximumTangents > epsilon ? shadingInfluence / maximumTangents : 0d);
            }
            return new GeometryMetric(
                0d, 0d,
                maximumTangents > epsilon ? 1d / maximumTangents : 0d);
        }

        private static double DotGeometry(
            Vector3[] leftVertices,
            Vector3[] leftNormals,
            Vector3[] leftTangents,
            Vector3[] rightVertices,
            Vector3[] rightNormals,
            Vector3[] rightTangents,
            GeometryMetric metric)
        {
            return metric.vertices * Dot(leftVertices, rightVertices) +
                   metric.normals * Dot(leftNormals, rightNormals) +
                   metric.tangents * Dot(leftTangents, rightTangents);
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

        /// <summary>
        /// Solves min ||Ux-v|| squared subject to 0 &lt;= x &lt;= 1. The upper
        /// bound is part of the optimization rather than a post-fit clamp: each
        /// cyclic coordinate update exactly minimizes the convex quadratic over
        /// its feasible interval. This matches the usable 0..100 blendshape range
        /// while leaving any authored geometry beyond that range in the exact
        /// residual V-UC.
        /// </summary>
        public static float[] SolveBoxConstrainedLeastSquares(
            Vector3[][] basis,
            Vector3[] target,
            int iterations = 256)
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
            var maximumDiagonal = 0d;
            for (var j = 0; j < count; j++)
                maximumDiagonal = Math.Max(maximumDiagonal, gram[j, j]);
            if (maximumDiagonal <= 0d) return new float[count];

            // Relative to the strongest basis ray, this removes only axes whose
            // squared magnitude is beneath double-precision numerical utility.
            // No ridge term is added, so a reachable endpoint remains unbiased.
            var diagonalTolerance = maximumDiagonal * 1e-14;
            for (var iteration = 0; iteration < Mathf.Max(1, iterations); iteration++)
            {
                var largestChange = 0d;
                for (var j = 0; j < count; j++)
                {
                    var diagonal = gram[j, j];
                    if (diagonal <= diagonalTolerance) continue;
                    var numerator = projection[j];
                    for (var k = 0; k < count; k++)
                    {
                        if (k != j) numerator -= gram[j, k] * output[k];
                    }
                    var next = Math.Min(1d, Math.Max(0d, numerator / diagonal));
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
            if (blendShapeIndex < 0 || blendShapeIndex >= mesh.blendShapeCount)
            {
                vertices = new Vector3[vertexCount];
                normals = new Vector3[vertexCount];
                tangents = new Vector3[vertexCount];
                error = $"Blendshape index {blendShapeIndex} is invalid for mesh '{mesh.name}'.";
                return false;
            }
            if (TryReadLinearBlendShapeAtWeight(
                    mesh, blendShapeIndex, 100f, vertexCount,
                    out vertices, out normals, out tangents, out error))
                return true;
            error = $"Blendshape '{mesh.GetBlendShapeName(blendShapeIndex)}' cannot be " +
                    $"used as a linear reconstruction basis: {error}";
            return false;
        }
    }
}
